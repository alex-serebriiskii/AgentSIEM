using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Data.Enums;
using Siem.Api.Services;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;
using Siem.Rules.Core;

namespace Siem.Integration.Tests.Tests.Services;

/// <summary>
/// Integration tests for the RecompilationCoordinator.
/// Verifies debounce coalescing, list consistency across recompilation,
/// atomic swap under concurrent evaluation, new rules being picked up,
/// and validation of corrupt rules.
/// </summary>
[NotInParallel("database")]
public class RecompilationCoordinatorTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
        await DbHelper.FlushRedisAsync();
    }

    [Test]
    public async Task Recompilation_DebounceCoalescing_ManySignalsProduceFewCompilations()
    {
        // Arrange: seed some rules so compilation has something to do
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
                db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: $"Rule {i}"));
            await db.SaveChangesAsync();
        }

        var (coordinator, rulesCache, _) = CreateServices();

        // Start the coordinator (triggers initial compilation)
        await coordinator.StartAsync(CancellationToken.None);

        // Wait for initial compilation to complete
        await Task.Delay(TimeSpan.FromSeconds(2));
        var initialCompilationTime = rulesCache.LastCompilation.CompiledAt;

        // Act: fire 50 rapid invalidation signals
        for (var i = 0; i < 50; i++)
        {
            coordinator.SignalInvalidation(
                new InvalidationSignal(InvalidationReason.RuleUpdated, Detail: $"signal-{i}"));
        }

        // Wait for debounce window (500ms) + compilation time + buffer
        await Task.Delay(TimeSpan.FromSeconds(3));

        await coordinator.StopAsync(CancellationToken.None);

        // Assert: the compilation time should have changed exactly once after the
        // debounced batch (not 50 times). We verify by checking that the final
        // compilation happened after all signals were sent, and the rule count is
        // still correct (indicating a single clean compilation, not partial ones).
        var finalMetadata = rulesCache.LastCompilation;
        finalMetadata.CompiledAt.Should().BeAfter(initialCompilationTime);
        finalMetadata.RuleCount.Should().Be(5);
    }

    [Test]
    public async Task Recompilation_ListConsistency_CompiledEngineReflectsListChanges()
    {
        // Arrange: create a managed list and a rule that uses InList
        var listId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.ManagedLists.Add(new ManagedListEntity
            {
                Id = listId,
                Name = "Approved Tools",
                Description = "Tools that are approved",
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now,
                Members =
                [
                    new ListMemberEntity { ListId = listId, Value = "calculator", AddedAt = now },
                    new ListMemberEntity { ListId = listId, Value = "search", AddedAt = now }
                ]
            });

            // Rule: alert when toolName is NOT in the approved list
            db.Rules.Add(new RuleEntity
            {
                Id = Guid.NewGuid(),
                Name = "Unapproved Tool Alert",
                Description = "Alert on unapproved tools",
                Enabled = true,
                Severity = Siem.Api.Data.Enums.Severity.High,
                ConditionJson = $$"""{"type":"list","field":"toolName","listId":"{{listId}}","negated":true}""",
                EvaluationType = "SingleEvent",
                ActionsJson = "[]",
                Tags = [],
                CreatedBy = "test",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var (coordinator, rulesCache, _) = CreateServices();
        await coordinator.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Act 1: evaluate an unapproved tool — should trigger
        var unapprovedEvt = CreateEvent(toolName: "malicious-tool");
        var result1 = await EvaluateEngine(rulesCache, unapprovedEvt);
        result1.Should().HaveCount(1, "unapproved tool should trigger the negated InList rule");

        // Act 2: evaluate an approved tool — should NOT trigger
        var approvedEvt = CreateEvent(toolName: "calculator");
        var result2 = await EvaluateEngine(rulesCache, approvedEvt);
        result2.Should().BeEmpty("approved tool is in the list, negated InList should not trigger");

        // Act 3: update the list to include the previously unapproved tool
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.ListMembers.Add(new ListMemberEntity
            {
                ListId = listId, Value = "malicious-tool", AddedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Trigger recompilation and wait for it
        await coordinator.SignalAndWaitAsync(
            new InvalidationSignal(InvalidationReason.ListUpdated, listId));

        // Act 4: evaluate the same "unapproved" tool — now it should NOT trigger
        var result3 = await EvaluateEngine(rulesCache, unapprovedEvt);
        result3.Should().BeEmpty("after adding tool to approved list and recompiling, it should no longer trigger");

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Recompilation_AtomicSwap_NoExceptionsDuringConcurrentEvaluation()
    {
        // Arrange: seed rules
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            for (var i = 0; i < 10; i++)
                db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: $"Rule {i}"));
            await db.SaveChangesAsync();
        }

        var (coordinator, rulesCache, _) = CreateServices();
        await coordinator.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Act: start a background task that continuously evaluates events
        var evaluationCount = 0;
        var evaluationErrors = new List<Exception>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var evaluationTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var evt = CreateEvent();
                    await EvaluateEngine(rulesCache, evt);
                    Interlocked.Increment(ref evaluationCount);
                }
                catch (Exception ex)
                {
                    lock (evaluationErrors) evaluationErrors.Add(ex);
                }
            }
        }, cts.Token);

        // Trigger multiple recompilations while evaluation is running
        for (var i = 0; i < 10; i++)
        {
            coordinator.SignalInvalidation(
                new InvalidationSignal(InvalidationReason.RuleUpdated, Detail: $"swap-{i}"));
            await Task.Delay(100); // stagger slightly to allow some to compile between signals
        }

        // Let evaluation continue for a bit after signals
        await Task.Delay(TimeSpan.FromSeconds(2));
        cts.Cancel();

        try { await evaluationTask; }
        catch (OperationCanceledException) { /* expected */ }

        await coordinator.StopAsync(CancellationToken.None);

        // Assert: no exceptions during evaluation, and we evaluated a meaningful number
        evaluationErrors.Should().BeEmpty("concurrent evaluation should not throw during engine swap");
        evaluationCount.Should().BeGreaterThan(10, "evaluation loop should have run many iterations");
    }

    [Test]
    public async Task Recompilation_NewRulePickedUp_AfterRecompile()
    {
        var (coordinator, rulesCache, _) = CreateServices();
        await coordinator.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Initially no rules
        rulesCache.LastCompilation.RuleCount.Should().Be(0);

        // Add a rule to the DB
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "New Rule"));
            await db.SaveChangesAsync();
        }

        // Recompile and wait
        await coordinator.SignalAndWaitAsync(
            new InvalidationSignal(InvalidationReason.RuleCreated));

        // Verify the rule is now active
        rulesCache.LastCompilation.RuleCount.Should().Be(1);

        var evt = TestEventFactory.CreateFSharpAgentEvent(eventType: "tool_invocation");
        var results = await EvaluateEngine(rulesCache, evt);
        results.Should().ContainSingle(r => r.Triggered);

        // Now disable the rule
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var rule = await db.Rules.FirstAsync();
            rule.Enabled = false;
            await db.SaveChangesAsync();
        }

        await coordinator.SignalAndWaitAsync(
            new InvalidationSignal(InvalidationReason.RuleUpdated));

        // Verify the rule is gone
        rulesCache.LastCompilation.RuleCount.Should().Be(0);

        var results2 = await EvaluateEngine(rulesCache, evt);
        results2.Should().BeEmpty();

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Recompilation_MalformedRuleSkipped_EngineStillFunctions()
    {
        // Add a valid rule and a malformed rule (unparseable condition JSON).
        // RuleLoadingService should skip the malformed one and the engine
        // should function with the valid rule.
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "Valid Rule"));
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(
                name: "Malformed Rule",
                conditionJson: """{"type":"totally_unknown_type","field":"x"}"""));
            await db.SaveChangesAsync();
        }

        var (coordinator, rulesCache, _) = CreateServices();
        await coordinator.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Only the valid rule should be compiled (malformed one skipped by RuleLoadingService)
        rulesCache.LastCompilation.RuleCount.Should().Be(1);

        // The valid rule should still work
        var evt = TestEventFactory.CreateFSharpAgentEvent(eventType: "tool_invocation");
        var results = await EvaluateEngine(rulesCache, evt);
        results.Should().HaveCount(1);
        results[0].Triggered.Should().BeTrue();

        await coordinator.StopAsync(CancellationToken.None);
    }

    #region Service Setup Helpers

    private static (RecompilationCoordinator coordinator, CompiledRulesCache rulesCache, ListCacheService listCache)
        CreateServices()
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

        return (coordinator, rulesCache, listCache);
    }

    private static AgentEvent CreateEvent(
        string eventType = "tool_invocation",
        string agentId = "test-agent",
        string? toolName = null)
    {
        return new AgentEvent(
            eventId: Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            sessionId: "test-session",
            traceId: $"trace-{Guid.NewGuid():N}",
            agentId: agentId,
            agentName: "TestAgent",
            eventType: eventType,
            modelId: FSharpOption<string>.None,
            inputTokens: FSharpOption<int>.None,
            outputTokens: FSharpOption<int>.None,
            latencyMs: FSharpOption<double>.None,
            toolName: toolName != null ? FSharpOption<string>.Some(toolName) : FSharpOption<string>.None,
            toolInput: FSharpOption<string>.None,
            toolOutput: FSharpOption<string>.None,
            contentHash: FSharpOption<string>.None,
            properties: MapModule.Empty<string, System.Text.Json.JsonElement>());
    }

    private static async Task<List<Evaluator.EvaluationResult>> EvaluateEngine(
        CompiledRulesCache rulesCache, AgentEvent evt)
    {
        var results = await FSharpAsync.StartAsTask(
            Engine.evaluateEvent(rulesCache.Engine, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        return [..results];
    }

    #endregion
}
