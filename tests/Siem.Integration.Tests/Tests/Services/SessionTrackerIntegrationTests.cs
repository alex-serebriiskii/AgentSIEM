using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Siem.Api.Services;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Services;

[NotInParallel("database")]
public class SessionTrackerIntegrationTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test]
    public async Task TrackEvent_FirstEvent_CreatesSession()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        var tracker = new SessionTracker(dataSource, NullLogger<SessionTracker>.Instance);

        var sessionId = $"session-{Guid.NewGuid():N}";
        var timestamp = DateTime.UtcNow;
        await tracker.TrackEventAsync(sessionId, "agent-1", "TestAgent", timestamp);

        await using var db = IntegrationTestFixture.CreateDbContext();
        var session = await db.AgentSessions.FindAsync(sessionId);
        session.Should().NotBeNull();
        session!.AgentId.Should().Be("agent-1");
        session.AgentName.Should().Be("TestAgent");
        session.EventCount.Should().Be(1);
    }

    [Test]
    public async Task TrackEvent_SubsequentEvents_IncrementsEventCount()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        var tracker = new SessionTracker(dataSource, NullLogger<SessionTracker>.Instance);

        var sessionId = $"session-{Guid.NewGuid():N}";
        var baseTime = DateTime.UtcNow;

        await tracker.TrackEventAsync(sessionId, "agent-1", "TestAgent", baseTime);
        await tracker.TrackEventAsync(sessionId, "agent-1", "TestAgent", baseTime.AddSeconds(1));
        await tracker.TrackEventAsync(sessionId, "agent-1", "TestAgent", baseTime.AddSeconds(2));

        await using var db = IntegrationTestFixture.CreateDbContext();
        var session = await db.AgentSessions.FindAsync(sessionId);
        session.Should().NotBeNull();
        session!.EventCount.Should().Be(3);
    }

    [Test]
    public async Task TrackEvent_UpdatesLastEventAt()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        var tracker = new SessionTracker(dataSource, NullLogger<SessionTracker>.Instance);

        var sessionId = $"session-{Guid.NewGuid():N}";
        var firstTime = DateTime.UtcNow.AddMinutes(-5);
        var lastTime = DateTime.UtcNow;

        await tracker.TrackEventAsync(sessionId, "agent-1", "TestAgent", firstTime);
        await tracker.TrackEventAsync(sessionId, "agent-1", "TestAgent", lastTime);

        await using var db = IntegrationTestFixture.CreateDbContext();
        var session = await db.AgentSessions.FindAsync(sessionId);
        session.Should().NotBeNull();
        session!.LastEventAt.Should().BeCloseTo(lastTime, TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task TrackEvent_DifferentSessions_CreatesMultipleSessions()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        var tracker = new SessionTracker(dataSource, NullLogger<SessionTracker>.Instance);

        var sessionA = $"session-A-{Guid.NewGuid():N}";
        var sessionB = $"session-B-{Guid.NewGuid():N}";

        await tracker.TrackEventAsync(sessionA, "agent-1", "AgentA", DateTime.UtcNow);
        await tracker.TrackEventAsync(sessionB, "agent-2", "AgentB", DateTime.UtcNow);

        await using var db = IntegrationTestFixture.CreateDbContext();
        var a = await db.AgentSessions.FindAsync(sessionA);
        var b = await db.AgentSessions.FindAsync(sessionB);
        a.Should().NotBeNull();
        b.Should().NotBeNull();
        a!.AgentId.Should().Be("agent-1");
        b!.AgentId.Should().Be("agent-2");
    }
}
