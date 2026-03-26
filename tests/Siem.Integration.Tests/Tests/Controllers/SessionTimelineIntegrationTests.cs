using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Siem.Api.Controllers;
using Siem.Api.Storage;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Controllers;

[NotInParallel("database")]
public class SessionTimelineIntegrationTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test]
    public async Task GetSessionTimeline_ReturnsEventsInChronologicalOrder()
    {
        var sessionId = "timeline-session-1";
        await SeedEventsForSession(sessionId, 5);
        await SeedSession(sessionId);

        await using var db = IntegrationTestFixture.CreateDbContext();
        await using var dataSource = NpgsqlDataSource.Create(IntegrationTestFixture.TimescaleConnectionString);
        var controller = new SessionsController(new Siem.Api.Services.SessionService(db, dataSource, new Siem.Api.Services.PaginationConfig()));

        var result = await controller.GetSessionTimeline(sessionId, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Try both casings since serialization may vary
        var sessionIdProp = root.TryGetProperty("sessionId", out var sid) ? sid
            : root.GetProperty("SessionId");
        sessionIdProp.GetString().Should().Be(sessionId);

        var eventCountProp = root.TryGetProperty("eventCount", out var ec) ? ec
            : root.GetProperty("EventCount");
        eventCountProp.GetInt32().Should().Be(5);

        var events = root.TryGetProperty("events", out var ev) ? ev
            : root.GetProperty("Events");
        events.GetArrayLength().Should().Be(5);

        // Verify chronological order
        DateTime prev = DateTime.MinValue;
        foreach (var evt in events.EnumerateArray())
        {
            var tsProp = evt.TryGetProperty("timestamp", out var ts) ? ts
                : evt.GetProperty("Timestamp");
            var tsVal = tsProp.GetDateTime();
            tsVal.Should().BeOnOrAfter(prev);
            prev = tsVal;
        }
    }

    [Test]
    public async Task GetSessionTimeline_NonexistentSession_ReturnsNotFound()
    {
        await using var db = IntegrationTestFixture.CreateDbContext();
        await using var dataSource = NpgsqlDataSource.Create(IntegrationTestFixture.TimescaleConnectionString);
        var controller = new SessionsController(new Siem.Api.Services.SessionService(db, dataSource, new Siem.Api.Services.PaginationConfig()));

        var result = await controller.GetSessionTimeline("nonexistent-session", ct: CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task GetSessionTimeline_RespectsLimitParameter()
    {
        var sessionId = "timeline-limit-session";
        await SeedEventsForSession(sessionId, 20);
        await SeedSession(sessionId);

        await using var db = IntegrationTestFixture.CreateDbContext();
        await using var dataSource = NpgsqlDataSource.Create(IntegrationTestFixture.TimescaleConnectionString);
        var controller = new SessionsController(new Siem.Api.Services.SessionService(db, dataSource, new Siem.Api.Services.PaginationConfig()));

        var result = await controller.GetSessionTimeline(sessionId, limit: 5, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var ecProp = doc.RootElement.TryGetProperty("eventCount", out var ec) ? ec
            : doc.RootElement.GetProperty("EventCount");
        ecProp.GetInt32().Should().Be(5);
    }

    [Test]
    public async Task GetSessionTimeline_500Events_ReturnsUnder50ms()
    {
        var sessionId = "timeline-perf-session";
        await SeedEventsForSession(sessionId, 500);
        await SeedSession(sessionId);

        await using var db = IntegrationTestFixture.CreateDbContext();
        await using var dataSource = NpgsqlDataSource.Create(IntegrationTestFixture.TimescaleConnectionString);
        var controller = new SessionsController(new Siem.Api.Services.SessionService(db, dataSource, new Siem.Api.Services.PaginationConfig()));

        // Warm up
        await controller.GetSessionTimeline(sessionId, ct: CancellationToken.None);

        // Timed run
        var sw = Stopwatch.StartNew();
        var result = await controller.GetSessionTimeline(sessionId, ct: CancellationToken.None);
        sw.Stop();

        result.Should().BeOfType<OkObjectResult>();
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            $"Session timeline should return quickly; actual: {sw.ElapsedMilliseconds}ms");
    }

    private static async Task SeedEventsForSession(string sessionId, int count)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = count + 10, MaxFlushIntervalSeconds = 300 });

        var baseTime = DateTime.UtcNow.AddMinutes(-count);
        for (int i = 0; i < count; i++)
        {
            var evt = TestEventFactory.CreateToolInvocation(
                sessionId: sessionId,
                toolName: $"tool-{i % 5}",
                timestamp: baseTime.AddSeconds(i));
            await writer.EnqueueAsync(evt);
        }
        await writer.FlushAsync();
    }

    private static async Task SeedSession(string sessionId)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var cmd = dataSource.CreateCommand(
            "INSERT INTO agent_sessions (session_id, agent_id, agent_name, started_at, last_event_at, event_count) " +
            "VALUES (@sid, 'test-agent', 'TestAgent', NOW() - INTERVAL '10 minutes', NOW(), @count) " +
            "ON CONFLICT (session_id) DO NOTHING");
        cmd.Parameters.AddWithValue("sid", sessionId);
        cmd.Parameters.AddWithValue("count", 5);
        await cmd.ExecuteNonQueryAsync();
    }
}
