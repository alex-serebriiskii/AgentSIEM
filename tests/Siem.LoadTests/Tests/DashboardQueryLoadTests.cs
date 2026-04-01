using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Siem.Api.Controllers;
using Siem.Api.Data.Entities;
using Siem.Api.Data.Enums;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;
using Siem.Api.Services;
using Siem.Api.Storage;
using Siem.LoadTests.Fixtures;
using Siem.LoadTests.Helpers;

namespace Siem.LoadTests.Tests;

[NotInParallel("database")]
public class DashboardQueryLoadTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test, Timeout(300_000)]
    public async Task DashboardEndpoints_UnderWriteLoad_P95UnderTargets(CancellationToken testCt)
    {
        const int eventCount = 100_000;
        const int agentCount = 50;
        const int alertCount = 200;
        const int queryIterations = 30;

        // Seed events
        var generator = new LoadTestEventGenerator(agentCount: agentCount, sessionsPerAgent: 3, seed: 9999);
        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var seedWriter = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 2000, MaxFlushIntervalSeconds = 300 });

        var events = generator.GenerateEvents(eventCount, timeSpreadMinutes: 60);
        foreach (var evt in events)
            await seedWriter.EnqueueAsync(evt);
        await seedWriter.FlushAsync();

        // Seed alerts across severities
        await using (var db = LoadTestFixture.CreateDbContext())
        {
            var severities = new[] { Severity.Low, Severity.Medium, Severity.High, Severity.Critical };
            for (int i = 0; i < alertCount; i++)
            {
                db.Alerts.Add(new AlertEntity
                {
                    AlertId = Guid.NewGuid(),
                    RuleId = Guid.NewGuid(),
                    RuleName = $"Dashboard Test Rule {i % 10}",
                    Severity = severities[i % severities.Length],
                    Status = i % 3 == 0 ? AlertStatus.Resolved : AlertStatus.Open,
                    Title = $"Dashboard Test Alert {i}",
                    Detail = "Test",
                    Context = "{}",
                    AgentId = generator.AgentIds[i % generator.AgentIds.Count],
                    SessionId = $"{generator.AgentIds[i % generator.AgentIds.Count]}-sess-00",
                    TriggeredAt = DateTime.UtcNow.AddMinutes(-i),
                    Labels = "{}"
                });
            }
            await db.SaveChangesAsync();
        }

        // Refresh continuous aggregates before measuring
        await RefreshContinuousAggregate(dataSource, "agent_activity_hourly");
        await RefreshContinuousAggregate(dataSource, "tool_usage_hourly");

        // Start background writer
        using var cts = new CancellationTokenSource();
        var backgroundWriteTask = RunBackgroundWrites(dataSource, generator, cts.Token);

        // Create controller with real service
        await using var db2 = LoadTestFixture.CreateDbContext();
        var dashboardService = new DashboardService(db2);
        var controller = new DashboardController(dashboardService);
        var query = new DashboardQuery { Hours = 24, Limit = 20 };

        // Warmup
        await controller.GetTopAgents(query, CancellationToken.None);
        await controller.GetEventVolume(query, CancellationToken.None);
        await controller.GetAlertDistribution(query, CancellationToken.None);
        await controller.GetToolUsage(query, CancellationToken.None);

        // Measured queries
        var topAgentsLatency = new LatencyRecorder();
        var eventVolumeLatency = new LatencyRecorder();
        var alertDistLatency = new LatencyRecorder();
        var toolUsageLatency = new LatencyRecorder();

        for (int i = 0; i < queryIterations; i++)
        {
            var sw = Stopwatch.StartNew();
            var r1 = await controller.GetTopAgents(query, CancellationToken.None);
            sw.Stop();
            r1.Should().BeOfType<OkObjectResult>();
            topAgentsLatency.Record(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            var r2 = await controller.GetEventVolume(query, CancellationToken.None);
            sw.Stop();
            r2.Should().BeOfType<OkObjectResult>();
            eventVolumeLatency.Record(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            var r3 = await controller.GetAlertDistribution(query, CancellationToken.None);
            sw.Stop();
            r3.Should().BeOfType<OkObjectResult>();
            alertDistLatency.Record(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            var r4 = await controller.GetToolUsage(query, CancellationToken.None);
            sw.Stop();
            r4.Should().BeOfType<OkObjectResult>();
            toolUsageLatency.Record(sw.Elapsed.TotalMilliseconds);
        }

        cts.Cancel();
        try { await backgroundWriteTask; } catch (OperationCanceledException) { }

        // Assert P95 thresholds
        var aggregateP95 = LoadTestConfig.ScaleLatency(200);
        var alertDistP95 = LoadTestConfig.ScaleLatency(300);

        var topAgentsStats = topAgentsLatency.GetStats();
        topAgentsStats.P95.Should().BeLessThan(aggregateP95,
            $"P95 top-agents should be < {aggregateP95}ms; actual P95: {topAgentsStats.P95:F1}ms");

        var eventVolumeStats = eventVolumeLatency.GetStats();
        eventVolumeStats.P95.Should().BeLessThan(aggregateP95,
            $"P95 event-volume should be < {aggregateP95}ms; actual P95: {eventVolumeStats.P95:F1}ms");

        var alertDistStats = alertDistLatency.GetStats();
        alertDistStats.P95.Should().BeLessThan(alertDistP95,
            $"P95 alert-distribution should be < {alertDistP95}ms; actual P95: {alertDistStats.P95:F1}ms");

        var toolUsageStats = toolUsageLatency.GetStats();
        toolUsageStats.P95.Should().BeLessThan(aggregateP95,
            $"P95 tool-usage should be < {aggregateP95}ms; actual P95: {toolUsageStats.P95:F1}ms");
    }

    [Test, Timeout(180_000)]
    public async Task DashboardEndpoints_ContinuousAggregates_ReturnCorrectData(
        CancellationToken testCt)
    {
        const int eventCount = 50_000;
        const int agentCount = 30;

        var generator = new LoadTestEventGenerator(agentCount: agentCount, sessionsPerAgent: 3, seed: 1111);
        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var seedWriter = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 2000, MaxFlushIntervalSeconds = 300 });

        var events = generator.GenerateEvents(eventCount, timeSpreadMinutes: 60);
        foreach (var evt in events)
            await seedWriter.EnqueueAsync(evt);
        await seedWriter.FlushAsync();

        await RefreshContinuousAggregate(dataSource, "agent_activity_hourly");
        await RefreshContinuousAggregate(dataSource, "tool_usage_hourly");

        await using var db = LoadTestFixture.CreateDbContext();
        var dashboardService = new DashboardService(db);
        var controller = new DashboardController(dashboardService);
        var query = new DashboardQuery { Hours = 24, Limit = 50 };

        // Top agents should return data for seeded agents
        var topAgentsResult = await controller.GetTopAgents(query, CancellationToken.None);
        var topAgents = (topAgentsResult as OkObjectResult)?.Value as IReadOnlyList<TopAgentResult>;
        topAgents.Should().NotBeNullOrEmpty("top agents should return data after seeding events");
        topAgents!.Sum(a => a.TotalEvents).Should().BeGreaterThan(0,
            "aggregate should have non-zero event counts");

        // Event volume should have at least one bucket
        var eventVolumeResult = await controller.GetEventVolume(query, CancellationToken.None);
        var eventVolume = (eventVolumeResult as OkObjectResult)?.Value as IReadOnlyList<EventVolumeResult>;
        eventVolume.Should().NotBeNullOrEmpty(
            "event volume should have at least one hourly bucket");
        eventVolume!.Sum(b => b.EventCount).Should().BeGreaterThan(0,
            "total event count across buckets should be non-zero");

        // Tool usage should return data for seeded tools
        var toolUsageResult = await controller.GetToolUsage(query, CancellationToken.None);
        var toolUsage = (toolUsageResult as OkObjectResult)?.Value as IReadOnlyList<ToolUsageResult>;
        toolUsage.Should().NotBeNullOrEmpty("tool usage should return data after seeding events");
        toolUsage!.Sum(t => t.InvocationCount).Should().BeGreaterThan(0,
            "total tool invocations should be non-zero");
    }

    private static async Task RefreshContinuousAggregate(
        NpgsqlDataSource dataSource, string viewName)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CALL refresh_continuous_aggregate('{viewName}', NULL, NULL)";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task RunBackgroundWrites(
        NpgsqlDataSource dataSource,
        LoadTestEventGenerator generator,
        CancellationToken ct)
    {
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 500, MaxFlushIntervalSeconds = 1 });

        while (!ct.IsCancellationRequested)
        {
            var events = generator.GenerateEvents(500, timeSpreadMinutes: 5);
            foreach (var evt in events)
            {
                if (ct.IsCancellationRequested) break;
                await writer.EnqueueAsync(evt);
            }
            await writer.FlushAsync();
        }
    }
}
