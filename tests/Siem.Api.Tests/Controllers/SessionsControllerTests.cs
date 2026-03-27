using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Siem.Api.Controllers;
using Siem.Api.Data;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;
using Siem.Api.Services;
using Siem.Api.Tests.Controllers.Helpers;

namespace Siem.Api.Tests.Controllers;

public class SessionsControllerTests : IDisposable
{
    private readonly SiemDbContext _db;
    private readonly SessionsController _controller;

    public SessionsControllerTests()
    {
        _db = DbContextFactory.Create();
        // NpgsqlDataSource is null — timeline endpoint requires real PostgreSQL
        // and is covered by integration tests. EF-only endpoints work fine.
        var service = new SessionService(_db, null!, new PaginationConfig());
        _controller = new SessionsController(service);
    }

    public void Dispose() => _db.Dispose();

    // --- ListSessions ---

    [Test]
    public async Task ListSessions_ReturnsAllOrderedByLastEventAt()
    {
        var older = TestEntityBuilders.CreateSession(
            sessionId: "sess-old", lastEventAt: DateTime.UtcNow.AddHours(-2));
        var newer = TestEntityBuilders.CreateSession(
            sessionId: "sess-new", lastEventAt: DateTime.UtcNow.AddHours(-1));
        _db.AgentSessions.AddRange(older, newer);
        await _db.SaveChangesAsync();

        var result = await _controller.ListSessions(null, null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var sessions = ok.Value.Should().BeAssignableTo<IEnumerable<SessionResponse>>().Subject.ToList();
        sessions.Should().HaveCount(2);
        sessions[0].SessionId.Should().Be("sess-new");
        sessions[1].SessionId.Should().Be("sess-old");
    }

    [Test]
    public async Task ListSessions_FilterByAgentId_ReturnsOnlyMatching()
    {
        _db.AgentSessions.Add(TestEntityBuilders.CreateSession(agentId: "agent-A"));
        _db.AgentSessions.Add(TestEntityBuilders.CreateSession(agentId: "agent-B"));
        await _db.SaveChangesAsync();

        var result = await _controller.ListSessions("agent-A", null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var sessions = ok.Value.Should().BeAssignableTo<IEnumerable<SessionResponse>>().Subject.ToList();
        sessions.Should().HaveCount(1);
        sessions[0].AgentId.Should().Be("agent-A");
    }

    [Test]
    public async Task ListSessions_FilterByHasAlerts_ReturnsOnlyMatching()
    {
        _db.AgentSessions.Add(TestEntityBuilders.CreateSession(hasAlerts: true));
        _db.AgentSessions.Add(TestEntityBuilders.CreateSession(hasAlerts: false));
        await _db.SaveChangesAsync();

        var result = await _controller.ListSessions(null, true, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var sessions = ok.Value.Should().BeAssignableTo<IEnumerable<SessionResponse>>().Subject.ToList();
        sessions.Should().HaveCount(1);
        sessions[0].HasAlerts.Should().BeTrue();
    }

    // --- GetSession ---

    [Test]
    public async Task GetSession_ExistingId_ReturnsSession()
    {
        var session = TestEntityBuilders.CreateSession(sessionId: "sess-123");
        _db.AgentSessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await _controller.GetSession("sess-123", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<SessionResponse>().Subject;
        response.SessionId.Should().Be("sess-123");
    }

    [Test]
    public async Task GetSession_NonexistentId_ReturnsNotFound()
    {
        var result = await _controller.GetSession("nonexistent", CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    // --- GetSessionTimeline ---

    [Test]
    public async Task GetSessionTimeline_NonexistentSession_ReturnsNotFound()
    {
        var result = await _controller.GetSessionTimeline("nonexistent", new SessionTimelineQuery(), ct: CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task GetSessionTimeline_ExistingSession_ThrowsBecauseNpgsqlRequiresRealDb()
    {
        // Timeline uses NpgsqlDataSource directly (not EF Core).
        // Without a real database connection, this throws NullReferenceException.
        // Full testing is covered by integration tests against real TimescaleDB.
        var session = TestEntityBuilders.CreateSession(sessionId: "sess-timeline");
        _db.AgentSessions.Add(session);
        await _db.SaveChangesAsync();

        var act = () => _controller.GetSessionTimeline("sess-timeline", new SessionTimelineQuery(), ct: CancellationToken.None);
        await act.Should().ThrowAsync<NullReferenceException>();
    }
}
