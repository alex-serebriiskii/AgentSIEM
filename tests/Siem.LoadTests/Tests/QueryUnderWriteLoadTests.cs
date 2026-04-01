using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Siem.Api.Controllers;
using Siem.Api.Data.Entities;
using Siem.Api.Data.Enums;
using Siem.Api.Models.Requests;
using Siem.Api.Services;
using Siem.Api.Storage;
using Siem.LoadTests.Fixtures;
using Siem.LoadTests.Helpers;

namespace Siem.LoadTests.Tests;

[NotInParallel("database")]
public class QueryUnderWriteLoadTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test, Timeout(180_000)]
    public async Task SessionTimeline_UnderWriteLoad_P95Under200ms(CancellationToken testCt)
    {
        const int agentCount = 20;
        const int sessionsPerAgent = 3;
        const int eventsPerSession = 833; // ~50k total
        const int queryIterations = 20;

        var generator = new LoadTestEventGenerator(
            agentCount: agentCount, sessionsPerAgent: sessionsPerAgent, seed: 4444);

        // Seed phase: write 50k events
        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var seedWriter = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 2000, MaxFlushIntervalSeconds = 300 });

        var sessionIds = new List<string>();
        foreach (var (agentId, sessionId) in generator.GetAgentSessionPairs())
        {
            sessionIds.Add(sessionId);
            var events = generator.GenerateEventsForAgent(agentId, sessionId, eventsPerSession, timeSpreadMinutes: 30);
            foreach (var evt in events)
                await seedWriter.EnqueueAsync(evt);
        }
        await seedWriter.FlushAsync();

        // Seed agent_sessions rows
        foreach (var (agentId, sessionId) in generator.GetAgentSessionPairs())
            await SeedSession(dataSource, sessionId, agentId, eventsPerSession);

        // Start background writer (simulates active ingestion)
        using var cts = new CancellationTokenSource();
        var backgroundWriteTask = RunBackgroundWrites(dataSource, generator, cts.Token);

        // Warmup query
        await using var db = LoadTestFixture.CreateDbContext();
        var controller = new SessionsController(
            new SessionService(db, dataSource, new PaginationConfig()));
        await controller.GetSessionTimeline(sessionIds[0], new SessionTimelineQuery(), ct: CancellationToken.None);

        // Measured queries
        var latencyRecorder = new LatencyRecorder();
        for (int i = 0; i < queryIterations; i++)
        {
            var targetSession = sessionIds[i % sessionIds.Count];
            var sw = Stopwatch.StartNew();
            var result = await controller.GetSessionTimeline(
                targetSession, new SessionTimelineQuery(), ct: CancellationToken.None);
            sw.Stop();

            result.Should().BeOfType<OkObjectResult>();
            latencyRecorder.Record(sw.Elapsed.TotalMilliseconds);
        }

        // Stop background writes
        cts.Cancel();
        try { await backgroundWriteTask; } catch (OperationCanceledException) { }

        var stats = latencyRecorder.GetStats();
        // Relaxed threshold for Testcontainers: production target is 50ms
        var scaledP95 = LoadTestConfig.ScaleLatency(200);

        stats.P95.Should().BeLessThan(scaledP95,
            $"P95 session timeline should be < {scaledP95}ms under write load; " +
            $"actual P95: {stats.P95:F1}ms, P50: {stats.P50:F1}ms, Max: {stats.Max:F1}ms " +
            $"(production target: 50ms)");
    }

    [Test, Timeout(180_000)]
    public async Task AgentRiskSummary_UnderWriteLoad_P95Under300ms(CancellationToken testCt)
    {
        const int agentCount = 20;
        const int sessionsPerAgent = 3;
        const int eventsPerSession = 833;
        const int queryIterations = 20;

        var generator = new LoadTestEventGenerator(
            agentCount: agentCount, sessionsPerAgent: sessionsPerAgent, seed: 5555);

        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var seedWriter = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 2000, MaxFlushIntervalSeconds = 300 });

        foreach (var (agentId, sessionId) in generator.GetAgentSessionPairs())
        {
            var events = generator.GenerateEventsForAgent(agentId, sessionId, eventsPerSession, timeSpreadMinutes: 30);
            foreach (var evt in events)
                await seedWriter.EnqueueAsync(evt);
        }
        await seedWriter.FlushAsync();

        // Seed alerts for some agents
        await using (var db = LoadTestFixture.CreateDbContext())
        {
            foreach (var agentId in generator.AgentIds.Take(10))
            {
                db.Alerts.Add(new AlertEntity
                {
                    AlertId = Guid.NewGuid(),
                    RuleId = Guid.NewGuid(),
                    RuleName = "Load Test Rule",
                    Severity = Severity.High,
                    Status = AlertStatus.Open,
                    Title = "Load Test Alert",
                    Detail = "Test",
                    Context = "{}",
                    AgentId = agentId,
                    SessionId = $"{agentId}-sess-00",
                    TriggeredAt = DateTime.UtcNow,
                    Labels = "{}"
                });
            }
            await db.SaveChangesAsync();
        }

        // Start background writes
        using var cts = new CancellationTokenSource();
        var backgroundWriteTask = RunBackgroundWrites(dataSource, generator, cts.Token);

        // Warmup
        var controller = new AgentsController(new AgentService(dataSource));
        await controller.GetRiskSummary(generator.AgentIds[0], ct: CancellationToken.None);

        // Measured queries
        var latencyRecorder = new LatencyRecorder();
        for (int i = 0; i < queryIterations; i++)
        {
            var targetAgent = generator.AgentIds[i % generator.AgentIds.Count];
            var sw = Stopwatch.StartNew();
            var result = await controller.GetRiskSummary(targetAgent, ct: CancellationToken.None);
            sw.Stop();

            result.Should().BeOfType<OkObjectResult>();
            latencyRecorder.Record(sw.Elapsed.TotalMilliseconds);
        }

        cts.Cancel();
        try { await backgroundWriteTask; } catch (OperationCanceledException) { }

        var stats = latencyRecorder.GetStats();
        // Relaxed threshold for Testcontainers: production target is 100ms
        var scaledP95 = LoadTestConfig.ScaleLatency(300);

        stats.P95.Should().BeLessThan(scaledP95,
            $"P95 agent risk summary should be < {scaledP95}ms under write load; " +
            $"actual P95: {stats.P95:F1}ms, P50: {stats.P50:F1}ms, Max: {stats.Max:F1}ms " +
            $"(production target: 100ms)");
    }

    [Test, Timeout(180_000)]
    public async Task SessionTimeline_ProductionTarget_P95Under50ms(CancellationToken testCt)
    {
        if (Environment.GetEnvironmentVariable("PERF_BASELINE_ENABLED") != "true")
            Assert.Fail("Skipped: set PERF_BASELINE_ENABLED=true to run production-target query tests");

        const int agentCount = 20;
        const int sessionsPerAgent = 3;
        const int eventsPerSession = 833;
        const int queryIterations = 20;

        var generator = new LoadTestEventGenerator(
            agentCount: agentCount, sessionsPerAgent: sessionsPerAgent, seed: 6666);

        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var seedWriter = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 2000, MaxFlushIntervalSeconds = 300 });

        var sessionIds = new List<string>();
        foreach (var (agentId, sessionId) in generator.GetAgentSessionPairs())
        {
            sessionIds.Add(sessionId);
            var events = generator.GenerateEventsForAgent(agentId, sessionId, eventsPerSession, timeSpreadMinutes: 30);
            foreach (var evt in events)
                await seedWriter.EnqueueAsync(evt);
        }
        await seedWriter.FlushAsync();

        foreach (var (agentId, sessionId) in generator.GetAgentSessionPairs())
            await SeedSession(dataSource, sessionId, agentId, eventsPerSession);

        using var cts = new CancellationTokenSource();
        var backgroundWriteTask = RunBackgroundWrites(dataSource, generator, cts.Token);

        await using var db = LoadTestFixture.CreateDbContext();
        var controller = new SessionsController(
            new SessionService(db, dataSource, new PaginationConfig()));
        await controller.GetSessionTimeline(sessionIds[0], new SessionTimelineQuery(), ct: CancellationToken.None);

        var latencyRecorder = new LatencyRecorder();
        for (int i = 0; i < queryIterations; i++)
        {
            var targetSession = sessionIds[i % sessionIds.Count];
            var sw = Stopwatch.StartNew();
            var result = await controller.GetSessionTimeline(
                targetSession, new SessionTimelineQuery(), ct: CancellationToken.None);
            sw.Stop();
            result.Should().BeOfType<OkObjectResult>();
            latencyRecorder.Record(sw.Elapsed.TotalMilliseconds);
        }

        cts.Cancel();
        try { await backgroundWriteTask; } catch (OperationCanceledException) { }

        var stats = latencyRecorder.GetStats();
        var scaledP95 = LoadTestConfig.ScaleLatency(50);

        stats.P95.Should().BeLessThan(scaledP95,
            $"P95 session timeline should be < {scaledP95}ms (production target); " +
            $"actual P95: {stats.P95:F1}ms, P50: {stats.P50:F1}ms, Max: {stats.Max:F1}ms");
    }

    [Test, Timeout(180_000)]
    public async Task AgentRiskSummary_ProductionTarget_P95Under100ms(CancellationToken testCt)
    {
        if (Environment.GetEnvironmentVariable("PERF_BASELINE_ENABLED") != "true")
            Assert.Fail("Skipped: set PERF_BASELINE_ENABLED=true to run production-target query tests");

        const int agentCount = 20;
        const int sessionsPerAgent = 3;
        const int eventsPerSession = 833;
        const int queryIterations = 20;

        var generator = new LoadTestEventGenerator(
            agentCount: agentCount, sessionsPerAgent: sessionsPerAgent, seed: 7777);

        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var seedWriter = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 2000, MaxFlushIntervalSeconds = 300 });

        foreach (var (agentId, sessionId) in generator.GetAgentSessionPairs())
        {
            var events = generator.GenerateEventsForAgent(agentId, sessionId, eventsPerSession, timeSpreadMinutes: 30);
            foreach (var evt in events)
                await seedWriter.EnqueueAsync(evt);
        }
        await seedWriter.FlushAsync();

        await using (var db = LoadTestFixture.CreateDbContext())
        {
            foreach (var agentId in generator.AgentIds.Take(10))
            {
                db.Alerts.Add(new AlertEntity
                {
                    AlertId = Guid.NewGuid(),
                    RuleId = Guid.NewGuid(),
                    RuleName = "Perf Baseline Rule",
                    Severity = Severity.High,
                    Status = AlertStatus.Open,
                    Title = "Perf Baseline Alert",
                    Detail = "Test",
                    Context = "{}",
                    AgentId = agentId,
                    SessionId = $"{agentId}-sess-00",
                    TriggeredAt = DateTime.UtcNow,
                    Labels = "{}"
                });
            }
            await db.SaveChangesAsync();
        }

        using var cts = new CancellationTokenSource();
        var backgroundWriteTask = RunBackgroundWrites(dataSource, generator, cts.Token);

        var controller = new AgentsController(new AgentService(dataSource));
        await controller.GetRiskSummary(generator.AgentIds[0], ct: CancellationToken.None);

        var latencyRecorder = new LatencyRecorder();
        for (int i = 0; i < queryIterations; i++)
        {
            var targetAgent = generator.AgentIds[i % generator.AgentIds.Count];
            var sw = Stopwatch.StartNew();
            var result = await controller.GetRiskSummary(targetAgent, ct: CancellationToken.None);
            sw.Stop();
            result.Should().BeOfType<OkObjectResult>();
            latencyRecorder.Record(sw.Elapsed.TotalMilliseconds);
        }

        cts.Cancel();
        try { await backgroundWriteTask; } catch (OperationCanceledException) { }

        var stats = latencyRecorder.GetStats();
        var scaledP95 = LoadTestConfig.ScaleLatency(100);

        stats.P95.Should().BeLessThan(scaledP95,
            $"P95 agent risk summary should be < {scaledP95}ms (production target); " +
            $"actual P95: {stats.P95:F1}ms, P50: {stats.P50:F1}ms, Max: {stats.Max:F1}ms");
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

    private static async Task SeedSession(
        NpgsqlDataSource dataSource, string sessionId, string agentId, int eventCount)
    {
        await using var cmd = dataSource.CreateCommand(
            "INSERT INTO agent_sessions (session_id, agent_id, agent_name, started_at, last_event_at, event_count) " +
            "VALUES (@sid, @aid, @aname, NOW() - INTERVAL '30 minutes', NOW(), @count) " +
            "ON CONFLICT (session_id) DO NOTHING");
        cmd.Parameters.AddWithValue("sid", sessionId);
        cmd.Parameters.AddWithValue("aid", agentId);
        cmd.Parameters.AddWithValue("aname", $"Agent-{agentId.Split('-').Last()}");
        cmd.Parameters.AddWithValue("count", eventCount);
        await cmd.ExecuteNonQueryAsync();
    }
}
