using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Siem.Api.Controllers;
using Siem.Api.Data;
using Siem.Api.Services;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;
using Siem.Rules.Core;

namespace Siem.Integration.Tests.Tests.Controllers;

/// <summary>
/// Integration tests for the EngineController backed by real infrastructure.
/// Verifies that status reports accurate rule counts and staleness,
/// and that force recompile picks up DB changes.
/// </summary>
[NotInParallel("database")]
public class EngineControllerIntegrationTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
        await DbHelper.FlushRedisAsync();
    }

    [Test]
    public async Task GetEngineStatus_ReportsAccurateRuleCount()
    {
        // Seed 3 rules
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            for (var i = 0; i < 3; i++)
                db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: $"Rule {i}"));
            await db.SaveChangesAsync();
        }

        var (coordinator, controller) = CreateControllerWithCoordinator();
        await coordinator.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Act
        var result = controller.GetEngineStatus();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var status = DeserializeAnonymous(ok.Value!);
        status.ruleCount.Should().Be(3);
        status.compiledAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task GetEngineStatus_ReportsStaleness()
    {
        var (coordinator, controller) = CreateControllerWithCoordinator();
        await coordinator.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Wait a known duration
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Act
        var result = controller.GetEngineStatus();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var status = DeserializeAnonymous(ok.Value!);

        // Staleness should be at least 1 second (we waited)
        var staleness = TimeSpan.Parse(status.staleness);
        staleness.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task ForceRecompile_PicksUpNewRules()
    {
        var (coordinator, controller) = CreateControllerWithCoordinator();
        await coordinator.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Initially 0 rules
        var initialResult = controller.GetEngineStatus();
        var initialStatus = DeserializeAnonymous(
            ((OkObjectResult)initialResult).Value!);
        initialStatus.ruleCount.Should().Be(0);

        // Add 2 rules to DB
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "Rule A"));
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "Rule B"));
            await db.SaveChangesAsync();
        }

        // Force recompile via the controller
        var recompileResult = await controller.ForceRecompile(CancellationToken.None);
        var recompileOk = recompileResult.Should().BeOfType<OkObjectResult>().Subject;
        var recompileStatus = DeserializeAnonymous(recompileOk.Value!);

        recompileStatus.ruleCount.Should().Be(2);
        recompileStatus.status.Should().Be("recompiled");
        recompileStatus.compiledAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // Verify the engine actually evaluates against the new rules
        var stateProvider = new RedisStateProvider(IntegrationTestFixture.RedisMultiplexer);
        var rulesCache = GetRulesCache(controller);
        var evt = TestEventFactory.CreateFSharpAgentEvent(eventType: "tool_invocation");
        var evalResults = await FSharpAsync.StartAsTask(
            Engine.evaluateEvent(rulesCache.Engine, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        // Both rules match "tool_invocation" so we should get 2 triggered results
        evalResults.Length.Should().Be(2);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task GetEngineStatus_IncludesListCacheInfo_WhenListsExist()
    {
        // Seed a managed list
        var listId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.ManagedLists.Add(new Api.Data.Entities.ManagedListEntity
            {
                Id = listId,
                Name = "Blocked Agents",
                Description = "Agents to block",
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now,
                Members =
                [
                    new Api.Data.Entities.ListMemberEntity
                        { ListId = listId, Value = "evil-agent", AddedAt = now }
                ]
            });
            await db.SaveChangesAsync();
        }

        var (coordinator, controller) = CreateControllerWithCoordinator();
        await coordinator.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Act
        var result = controller.GetEngineStatus();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var status = DeserializeAnonymous(ok.Value!);

        // The listCaches should contain info about the "Blocked Agents" list
        status.listCacheCount.Should().BeGreaterThanOrEqualTo(1);

        await coordinator.StopAsync(CancellationToken.None);
    }

    #region Helpers

    private static (RecompilationCoordinator coordinator, EngineController controller)
        CreateControllerWithCoordinator()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SiemDbContext>(options =>
            options.UseNpgsql(IntegrationTestFixture.TimescaleConnectionString));
        services.AddScoped<RuleLoadingService>();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var provider = services.BuildServiceProvider();

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var listCache = new ListCacheService(
            scopeFactory, NullLogger<ListCacheService>.Instance);
        var stateProvider = new RedisStateProvider(IntegrationTestFixture.RedisMultiplexer);
        var rulesCache = new CompiledRulesCache(stateProvider);

        var coordinator = new RecompilationCoordinator(
            listCache,
            rulesCache,
            scopeFactory,
            NullLogger<RecompilationCoordinator>.Instance,
            new RecompilationConfig());

        var controller = new EngineController(rulesCache, coordinator);

        return (coordinator, controller);
    }

    /// <summary>
    /// The controller returns anonymous types. Use reflection to extract values.
    /// </summary>
    private static AnonymousResult DeserializeAnonymous(object value)
    {
        var type = value.GetType();
        return new AnonymousResult
        {
            ruleCount = GetPropOrDefault<int>(type, value, "ruleCount"),
            compiledAt = GetPropOrDefault<DateTime>(type, value, "compiledAt"),
            staleness = GetPropOrDefault<string>(type, value, "staleness") ?? "",
            status = GetPropOrDefault<string>(type, value, "status") ?? "",
            listCacheCount = GetListCacheCount(type, value)
        };
    }

    private static T? GetPropOrDefault<T>(Type type, object value, string name)
    {
        var prop = type.GetProperty(name);
        return prop != null ? (T?)prop.GetValue(value) : default;
    }

    private static int GetListCacheCount(Type type, object value)
    {
        var prop = type.GetProperty("listCaches");
        if (prop == null) return 0;
        var list = prop.GetValue(value);
        if (list is System.Collections.ICollection coll) return coll.Count;
        if (list is System.Collections.IEnumerable en)
            return en.Cast<object>().Count();
        return 0;
    }

    private static CompiledRulesCache GetRulesCache(EngineController controller)
    {
        var field = typeof(EngineController)
            .GetField("_rulesCache",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (CompiledRulesCache)field!.GetValue(controller)!;
    }

    private class AnonymousResult
    {
        public int ruleCount { get; init; }
        public DateTime compiledAt { get; init; }
        public string staleness { get; init; } = "";
        public string status { get; init; } = "";
        public int listCacheCount { get; init; }
    }

    #endregion
}
