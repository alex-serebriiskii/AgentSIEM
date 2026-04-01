using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Api.Alerting;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Notifications;
using Severity = Siem.Api.Data.Enums.Severity;
using Siem.LoadTests.Fixtures;
using Siem.LoadTests.Helpers;
using Siem.Rules.Core;
using static Siem.Rules.Core.Evaluator;

namespace Siem.LoadTests.Tests;

[NotInParallel("database")]
public class AlertPipelineSaturationTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
        await DbHelper.FlushRedisAsync();
    }

    [Test, Timeout(120_000)]
    public async Task AlertPipeline_1000Alerts_DedupAndThrottle_WorkCorrectly(CancellationToken testCt)
    {
        const int rulesCount = 10;
        const int alertsPerRule = 100;
        const int totalAlerts = rulesCount * alertsPerRule;

        // Seed rules in DB
        var ruleIds = new Guid[rulesCount];
        await using (var db = LoadTestFixture.CreateDbContext())
        {
            for (int i = 0; i < rulesCount; i++)
            {
                ruleIds[i] = Guid.NewGuid();
                db.Rules.Add(new RuleEntity
                {
                    Id = ruleIds[i],
                    Name = $"Saturation Rule {i}",
                    Description = "Load test rule",
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
            }
            await db.SaveChangesAsync();
        }

        // Build real alert pipeline
        using var sp = BuildServiceProvider();
        var config = new AlertPipelineConfig
        {
            DeduplicationWindowMinutes = 15,
            ThrottleMaxAlertsPerWindow = 10,
            ThrottleWindowMinutes = 5
        };
        var pipeline = CreatePipeline(sp, config);

        // Generate evaluation results: 100 per rule, varying agents
        var generator = new LoadTestEventGenerator(agentCount: 50, sessionsPerAgent: 2, seed: 333);
        var events = generator.GenerateEvents(totalAlerts, timeSpreadMinutes: 5);
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var sw = Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalAlerts),
            new ParallelOptions { MaxDegreeOfParallelism = 10 },
            async (idx, ct) =>
            {
                try
                {
                    var ruleIdx = idx / alertsPerRule;
                    var evalResult = new EvaluationResult(
                        triggered: true,
                        ruleId: ruleIds[ruleIdx],
                        severity: Siem.Rules.Core.Severity.Medium,
                        detail: FSharpOption<string>.Some($"Alert {idx}"),
                        context: MapModule.Empty<string, object>(),
                        actions: FSharpList<RuleAction>.Empty);

                    await pipeline.ProcessAsync(evalResult, events[idx], ct);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

        sw.Stop();

        // Assertions
        exceptions.Should().BeEmpty("alert pipeline should handle concurrent load without exceptions");

        var scaledTimeout = LoadTestConfig.ScaleLatency(30_000);
        sw.ElapsedMilliseconds.Should().BeLessThan((long)scaledTimeout,
            $"processing {totalAlerts} alerts should complete within {scaledTimeout / 1000}s; " +
            $"actual: {sw.ElapsedMilliseconds}ms");

        await using var db2 = LoadTestFixture.CreateDbContext();
        var totalAlertsInDb = await db2.Alerts.CountAsync();
        totalAlertsInDb.Should().BeLessThan(totalAlerts,
            "dedup and throttle should filter many alerts");
        totalAlertsInDb.Should().BeGreaterThan(0,
            "at least some alerts should be created");

        // Per-rule throttle check
        foreach (var ruleId in ruleIds)
        {
            var ruleAlertCount = await db2.Alerts.CountAsync(a => a.RuleId == ruleId);
            ruleAlertCount.Should().BeLessOrEqualTo(config.ThrottleMaxAlertsPerWindow,
                $"rule {ruleId} should not exceed throttle limit of {config.ThrottleMaxAlertsPerWindow}");
        }
    }

    private static AlertPipeline CreatePipeline(
        IServiceProvider serviceProvider, AlertPipelineConfig config)
    {
        var dedup = new AlertDeduplicator(LoadTestFixture.RedisMultiplexer, config);
        var throttler = new AlertThrottler(
            LoadTestFixture.RedisMultiplexer, config, NullLogger<AlertThrottler>.Instance);
        var retryWorker = new NotificationRetryWorker(
            NullLogger<NotificationRetryWorker>.Instance, new NotificationRetryConfig());
        var router = new NotificationRouter(
            [], retryWorker, NullLogger<NotificationRouter>.Instance);

        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var processingScopeFactory = new AlertProcessingScopeFactory(scopeFactory);

        return new AlertPipeline(
            dedup, throttler, processingScopeFactory, router,
            NullLogger<AlertPipeline>.Instance);
    }

    [Test, Timeout(120_000)]
    public async Task AlertPipeline_SuppressionUnderConcurrentLoad_SilencesCorrectRules(
        CancellationToken testCt)
    {
        const int rulesCount = 10;
        const int suppressedCount = 5;
        const int alertsPerRule = 100;
        const int totalAlerts = rulesCount * alertsPerRule;

        // Seed rules
        var ruleIds = new Guid[rulesCount];
        await using (var db = LoadTestFixture.CreateDbContext())
        {
            for (int i = 0; i < rulesCount; i++)
            {
                ruleIds[i] = Guid.NewGuid();
                db.Rules.Add(new RuleEntity
                {
                    Id = ruleIds[i],
                    Name = $"Suppression Rule {i}",
                    Description = "Load test rule",
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
            }

            // Suppress rules 0-4
            for (int i = 0; i < suppressedCount; i++)
            {
                db.Suppressions.Add(new SuppressionEntity
                {
                    Id = Guid.NewGuid(),
                    RuleId = ruleIds[i],
                    Reason = "Load test suppression",
                    CreatedBy = "load-test",
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                });
            }
            await db.SaveChangesAsync();
        }

        using var sp = BuildServiceProvider();
        var config = new AlertPipelineConfig
        {
            DeduplicationWindowMinutes = 15,
            ThrottleMaxAlertsPerWindow = 10,
            ThrottleWindowMinutes = 5
        };
        var pipeline = CreatePipeline(sp, config);

        var generator = new LoadTestEventGenerator(agentCount: 50, sessionsPerAgent: 2, seed: 444);
        var events = generator.GenerateEvents(totalAlerts, timeSpreadMinutes: 5);
        var exceptions = new ConcurrentBag<Exception>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalAlerts),
            new ParallelOptions { MaxDegreeOfParallelism = 10 },
            async (idx, ct) =>
            {
                try
                {
                    var ruleIdx = idx / alertsPerRule;
                    var evalResult = new EvaluationResult(
                        triggered: true,
                        ruleId: ruleIds[ruleIdx],
                        severity: Siem.Rules.Core.Severity.Medium,
                        detail: FSharpOption<string>.Some($"Suppression test {idx}"),
                        context: MapModule.Empty<string, object>(),
                        actions: FSharpList<RuleAction>.Empty);
                    await pipeline.ProcessAsync(evalResult, events[idx], ct);
                }
                catch (Exception ex) { exceptions.Add(ex); }
            });

        exceptions.Should().BeEmpty("pipeline should handle concurrent load without exceptions");

        await using var db2 = LoadTestFixture.CreateDbContext();

        // Suppressed rules should have zero alerts
        foreach (var ruleId in ruleIds.Take(suppressedCount))
        {
            var count = await db2.Alerts.CountAsync(a => a.RuleId == ruleId);
            count.Should().Be(0,
                $"rule {ruleId} is suppressed and should produce zero alerts");
        }

        // Unsuppressed rules should have some alerts (capped by throttle)
        foreach (var ruleId in ruleIds.Skip(suppressedCount))
        {
            var count = await db2.Alerts.CountAsync(a => a.RuleId == ruleId);
            count.Should().BeGreaterThan(0,
                $"unsuppressed rule {ruleId} should produce at least one alert");
            count.Should().BeLessOrEqualTo(config.ThrottleMaxAlertsPerWindow,
                $"rule {ruleId} should not exceed throttle limit");
        }
    }

    [Test, Timeout(120_000)]
    public async Task AlertPipeline_WithNotificationChannels_RoutesCorrectly(
        CancellationToken testCt)
    {
        const int rulesCount = 10;
        const int alertsPerRule = 50;
        const int totalAlerts = rulesCount * alertsPerRule;

        // 3 Critical, 4 High, 3 Medium rules
        var severities = new[]
        {
            Severity.Critical, Severity.Critical, Severity.Critical,
            Severity.High, Severity.High, Severity.High, Severity.High,
            Severity.Medium, Severity.Medium, Severity.Medium
        };
        var fsharpSeverities = new[]
        {
            Siem.Rules.Core.Severity.Critical, Siem.Rules.Core.Severity.Critical, Siem.Rules.Core.Severity.Critical,
            Siem.Rules.Core.Severity.High, Siem.Rules.Core.Severity.High, Siem.Rules.Core.Severity.High, Siem.Rules.Core.Severity.High,
            Siem.Rules.Core.Severity.Medium, Siem.Rules.Core.Severity.Medium, Siem.Rules.Core.Severity.Medium
        };

        var ruleIds = new Guid[rulesCount];
        await using (var db = LoadTestFixture.CreateDbContext())
        {
            for (int i = 0; i < rulesCount; i++)
            {
                ruleIds[i] = Guid.NewGuid();
                db.Rules.Add(new RuleEntity
                {
                    Id = ruleIds[i],
                    Name = $"Routing Rule {i}",
                    Description = "Load test rule",
                    Enabled = true,
                    Severity = severities[i],
                    ConditionJson = """{"type":"field","field":"eventType","operator":"Eq","value":"tool_invocation"}""",
                    EvaluationType = "SingleEvent",
                    ActionsJson = "[]",
                    Tags = [],
                    CreatedBy = "load-test",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }

        // Notification channels with severity thresholds
        var signalr = new InMemoryNotificationChannel("signalr", Severity.Low);
        var slack = new InMemoryNotificationChannel("slack", Severity.High);
        var pagerduty = new InMemoryNotificationChannel("pagerduty", Severity.Critical);

        using var sp = BuildServiceProvider();
        var config = new AlertPipelineConfig
        {
            DeduplicationWindowMinutes = 15,
            ThrottleMaxAlertsPerWindow = 10,
            ThrottleWindowMinutes = 5
        };
        var pipeline = CreatePipelineWithChannels(sp, config, [signalr, slack, pagerduty]);

        var generator = new LoadTestEventGenerator(agentCount: 50, sessionsPerAgent: 2, seed: 555);
        var events = generator.GenerateEvents(totalAlerts, timeSpreadMinutes: 5);
        var exceptions = new ConcurrentBag<Exception>();

        var sw = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalAlerts),
            new ParallelOptions { MaxDegreeOfParallelism = 10 },
            async (idx, ct) =>
            {
                try
                {
                    var ruleIdx = idx / alertsPerRule;
                    var evalResult = new EvaluationResult(
                        triggered: true,
                        ruleId: ruleIds[ruleIdx],
                        severity: fsharpSeverities[ruleIdx],
                        detail: FSharpOption<string>.Some($"Routing test {idx}"),
                        context: MapModule.Empty<string, object>(),
                        actions: FSharpList<RuleAction>.Empty);
                    await pipeline.ProcessAsync(evalResult, events[idx], ct);
                }
                catch (Exception ex) { exceptions.Add(ex); }
            });
        sw.Stop();

        exceptions.Should().BeEmpty("pipeline should handle concurrent load without exceptions");

        var scaledTimeout = LoadTestConfig.ScaleLatency(60_000);
        sw.ElapsedMilliseconds.Should().BeLessThan((long)scaledTimeout,
            $"processing {totalAlerts} alerts with routing should complete within {scaledTimeout / 1000}s");

        // SignalR (Low minimum) should receive from all severities
        signalr.ReceivedAlertIds.Should().NotBeEmpty(
            "signalr channel (Low min) should receive alerts");

        // PagerDuty (Critical minimum) should receive fewer than signalr
        pagerduty.ReceivedAlertIds.Count.Should().BeLessOrEqualTo(signalr.ReceivedAlertIds.Count,
            "pagerduty (Critical) should receive <= signalr (Low)");

        // All channels should receive less than total due to dedup/throttle
        signalr.ReceivedAlertIds.Count.Should().BeLessThan(totalAlerts,
            "dedup/throttle should reduce alert count");

        // PagerDuty should only have received alerts (Critical rules = 3 rules, throttle 10 each = max 30)
        pagerduty.ReceivedAlertIds.Count.Should().BeLessOrEqualTo(30,
            "pagerduty should only receive from 3 Critical rules, max 10 each");
    }

    private static AlertPipeline CreatePipelineWithChannels(
        IServiceProvider serviceProvider, AlertPipelineConfig config,
        IReadOnlyList<INotificationChannel> channels)
    {
        var dedup = new AlertDeduplicator(LoadTestFixture.RedisMultiplexer, config);
        var throttler = new AlertThrottler(
            LoadTestFixture.RedisMultiplexer, config, NullLogger<AlertThrottler>.Instance);
        var retryWorker = new NotificationRetryWorker(
            NullLogger<NotificationRetryWorker>.Instance, new NotificationRetryConfig());
        var router = new NotificationRouter(
            channels, retryWorker, NullLogger<NotificationRouter>.Instance);

        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var processingScopeFactory = new AlertProcessingScopeFactory(scopeFactory);

        return new AlertPipeline(
            dedup, throttler, processingScopeFactory, router,
            NullLogger<AlertPipeline>.Instance);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SiemDbContext>(options =>
            options.UseNpgsql(LoadTestFixture.TimescaleConnectionString));
        services.AddScoped<SuppressionChecker>();
        services.AddScoped<AlertEnricher>();
        services.AddScoped<AlertPersistence>();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        return services.BuildServiceProvider();
    }
}
