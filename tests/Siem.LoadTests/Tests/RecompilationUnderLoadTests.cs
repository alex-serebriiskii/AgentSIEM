using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Services;
using Siem.LoadTests.Fixtures;
using Siem.LoadTests.Helpers;
using Siem.Rules.Core;
using Severity = Siem.Api.Data.Enums.Severity;

namespace Siem.LoadTests.Tests;

[NotInParallel("database")]
public class RecompilationUnderLoadTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
        await DbHelper.FlushRedisAsync();
    }

    [Test, Timeout(120_000)]
    public async Task Recompilation_AtomicSwap_UnderHighThroughputEvaluation(
        CancellationToken testCt)
    {
        const int initialRuleCount = 23; // 20 SingleEvent + 3 Temporal
        const int recompilationSignals = 20;
        const int evaluationDurationSeconds = 10;
        const int evaluationThreads = 4;

        // Seed initial rules
        var singleEventRules = LoadTestRuleFactory.CreateVariedSingleEventRules(20);
        var temporalRules = LoadTestRuleFactory.CreateVariedTemporalRules(3);
        var allRules = singleEventRules.Concat(temporalRules).ToList();

        await using (var db = LoadTestFixture.CreateDbContext())
        {
            db.Rules.AddRange(allRules);
            await db.SaveChangesAsync();
        }

        // Build real recompilation infrastructure
        var (coordinator, rulesCache) = CreateServices();

        // Start coordinator (triggers initial compilation)
        await coordinator.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(3));

        rulesCache.LastCompilation.RuleCount.Should().Be(initialRuleCount,
            "initial compilation should include all seeded rules");

        // Pre-generate events for evaluation
        var generator = new LoadTestEventGenerator(agentCount: 50, sessionsPerAgent: 3, seed: 888);
        var events = generator.GenerateEvents(100_000, timeSpreadMinutes: 10);

        // Track evaluation metrics
        long totalEvaluations = 0;
        var exceptions = new ConcurrentBag<Exception>();

        // Launch parallel evaluation tasks
        using var evaluationCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(evaluationDurationSeconds));

        var evaluationTasks = Enumerable.Range(0, evaluationThreads)
            .Select(threadIdx => Task.Run(async () =>
            {
                int eventIdx = threadIdx * 1000; // stagger starting position
                while (!evaluationCts.IsCancellationRequested)
                {
                    try
                    {
                        var engine = rulesCache.Engine;
                        if (engine is null)
                        {
                            exceptions.Add(new InvalidOperationException(
                                $"Engine was null on thread {threadIdx}"));
                            continue;
                        }

                        var evt = events[eventIdx % events.Count];
                        await FSharpAsync.StartAsTask(
                            Engine.evaluateEvent(engine, evt),
                            FSharpOption<TaskCreationOptions>.None,
                            FSharpOption<CancellationToken>.None);

                        Interlocked.Increment(ref totalEvaluations);
                        eventIdx++;
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { exceptions.Add(ex); }
                }
            }, evaluationCts.Token))
            .ToList();

        // Simultaneously trigger recompilations with actual DB rule changes
        var recompilationTask = Task.Run(async () =>
        {
            for (int i = 0; i < recompilationSignals; i++)
            {
                // Add a new rule to the DB so the engine actually changes
                await using (var db = LoadTestFixture.CreateDbContext())
                {
                    db.Rules.Add(new RuleEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = $"Dynamic Rule {i}",
                        Description = $"Added during load test iteration {i}",
                        Enabled = true,
                        Severity = Severity.Medium,
                        ConditionJson = """{"type":"field","field":"eventType","operator":"Eq","value":"tool_invocation"}""",
                        EvaluationType = "SingleEvent",
                        ActionsJson = "[]",
                        Tags = [],
                        CreatedBy = "load-test",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync();
                }

                coordinator.SignalInvalidation(
                    new InvalidationSignal(InvalidationReason.RuleCreated,
                        Detail: $"dynamic-rule-{i}"));

                await Task.Delay(200); // stagger signals
            }
        });

        // Wait for recompilation signals to finish
        await recompilationTask;

        // Let evaluations continue briefly after last signal to ensure
        // the engine swap settles
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Cancel evaluation loops
        evaluationCts.Cancel();
        foreach (var task in evaluationTasks)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }

        // Wait for final recompilation to settle
        await Task.Delay(TimeSpan.FromSeconds(2));
        await coordinator.StopAsync(CancellationToken.None);

        // Assertions
        exceptions.Should().BeEmpty(
            "no evaluation thread should encounter exceptions during engine swaps");

        var scaledThroughput = LoadTestConfig.ScaleThroughput(50_000);
        totalEvaluations.Should().BeGreaterThan((long)scaledThroughput,
            $"total evaluations should exceed {scaledThroughput:N0} to prove the engine " +
            $"was under real load; actual: {totalEvaluations:N0}");

        var finalMeta = rulesCache.LastCompilation;
        var expectedFinalCount = initialRuleCount + recompilationSignals;
        finalMeta.RuleCount.Should().Be(expectedFinalCount,
            $"final engine should have all {expectedFinalCount} rules " +
            $"({initialRuleCount} initial + {recompilationSignals} dynamic)");

        finalMeta.CompiledAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10),
            "last compilation should be recent");
    }

    private static (RecompilationCoordinator coordinator, CompiledRulesCache rulesCache)
        CreateServices()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<SiemDbContext>(options =>
            options.UseNpgsql(LoadTestFixture.TimescaleConnectionString));
        var provider = services.BuildServiceProvider();

        var dbFactory = provider.GetRequiredService<IDbContextFactory<SiemDbContext>>();
        var listCache = new ListCacheService(
            dbFactory, NullLogger<ListCacheService>.Instance);
        var ruleLoader = new RuleLoadingService(
            dbFactory, NullLogger<RuleLoadingService>.Instance);
        var stateProvider = new RedisStateProvider(LoadTestFixture.RedisMultiplexer);
        var rulesCache = new CompiledRulesCache(stateProvider);

        var config = new RecompilationConfig
        {
            DebounceWindowMs = 200, // faster debounce for load test
            MaxDebounceDelaySeconds = 2,
            ErrorRecoveryDelaySeconds = 1
        };
        var notifier = new CompilationNotifier(config);
        var orchestrator = new RuleCompilationOrchestrator(
            listCache, rulesCache, ruleLoader, notifier,
            NullLogger<RuleCompilationOrchestrator>.Instance);
        var coordinator = new RecompilationCoordinator(
            orchestrator, notifier,
            NullLogger<RecompilationCoordinator>.Instance, config);

        return (coordinator, rulesCache);
    }
}
