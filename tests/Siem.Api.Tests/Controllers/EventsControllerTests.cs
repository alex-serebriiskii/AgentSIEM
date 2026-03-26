using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Siem.Api.Controllers;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Models.Responses;
using Siem.Api.Services;
using Siem.Api.Tests.Controllers.Helpers;

namespace Siem.Api.Tests.Controllers;

public class EventsControllerTests : IDisposable
{
    private readonly SiemDbContext _db;
    private readonly EventsController _controller;

    public EventsControllerTests()
    {
        _db = DbContextFactory.Create();
        var service = new EventService(_db);
        _controller = new EventsController(service);
    }

    public void Dispose() => _db.Dispose();

    private AgentEventReadModel CreateEvent(
        string agentId = "agent-001",
        string eventType = "tool_invocation",
        string sessionId = "sess-001",
        string? toolName = null,
        DateTime? timestamp = null)
    {
        return new AgentEventReadModel
        {
            EventId = Guid.NewGuid(),
            Timestamp = timestamp ?? DateTime.UtcNow.AddMinutes(-10),
            AgentId = agentId,
            AgentName = "TestAgent",
            SessionId = sessionId,
            TraceId = "trace-001",
            EventType = eventType,
            ToolName = toolName,
            Properties = "{}",
            IngestedAt = DateTime.UtcNow
        };
    }

    private static PaginatedResult<EventResponse> ExtractPaginatedResult(IActionResult result)
    {
        var ok = (OkObjectResult)result;
        return (PaginatedResult<EventResponse>)ok.Value!;
    }

    // --- Default behavior ---

    [Test]
    public async Task SearchEvents_DefaultTimeRange_ReturnsRecentEvents()
    {
        // Event within last hour (should be included)
        _db.AgentEvents.Add(CreateEvent(timestamp: DateTime.UtcNow.AddMinutes(-30)));
        // Event from 2 hours ago (should be excluded by default 1-hour window)
        _db.AgentEvents.Add(CreateEvent(timestamp: DateTime.UtcNow.AddHours(-2)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().HaveCount(1);
        paginated.Page.Should().Be(1);
        paginated.PageSize.Should().Be(100);
        paginated.TotalCount.Should().Be(1);
    }

    [Test]
    public async Task SearchEvents_ExplicitTimeRange_ReturnsEventsInRange()
    {
        var now = DateTime.UtcNow;
        _db.AgentEvents.Add(CreateEvent(timestamp: now.AddHours(-3)));
        _db.AgentEvents.Add(CreateEvent(timestamp: now.AddHours(-5)));
        _db.AgentEvents.Add(CreateEvent(timestamp: now.AddHours(-10)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(
            start: new DateTimeOffset(now.AddHours(-6), TimeSpan.Zero),
            end: new DateTimeOffset(now, TimeSpan.Zero),
            ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.TotalCount.Should().Be(2);
    }

    // --- Filters ---

    [Test]
    public async Task SearchEvents_FilterByAgentId_ReturnsOnlyMatching()
    {
        _db.AgentEvents.Add(CreateEvent(agentId: "agent-A", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        _db.AgentEvents.Add(CreateEvent(agentId: "agent-B", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(agent_id: "agent-A", ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.TotalCount.Should().Be(1);
        paginated.Data[0].AgentId.Should().Be("agent-A");
    }

    [Test]
    public async Task SearchEvents_FilterByEventType_ReturnsOnlyMatching()
    {
        _db.AgentEvents.Add(CreateEvent(eventType: "tool_invocation", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        _db.AgentEvents.Add(CreateEvent(eventType: "llm_call", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(event_type: "llm_call", ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.TotalCount.Should().Be(1);
        paginated.Data[0].EventType.Should().Be("llm_call");
    }

    [Test]
    public async Task SearchEvents_FilterBySessionId_ReturnsOnlyMatching()
    {
        _db.AgentEvents.Add(CreateEvent(sessionId: "sess-A", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        _db.AgentEvents.Add(CreateEvent(sessionId: "sess-B", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(session_id: "sess-A", ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.TotalCount.Should().Be(1);
        paginated.Data[0].SessionId.Should().Be("sess-A");
    }

    [Test]
    public async Task SearchEvents_FilterByToolName_ReturnsOnlyMatching()
    {
        _db.AgentEvents.Add(CreateEvent(toolName: "web_search", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        _db.AgentEvents.Add(CreateEvent(toolName: "file_read", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        _db.AgentEvents.Add(CreateEvent(timestamp: DateTime.UtcNow.AddMinutes(-5)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(tool_name: "web_search", ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.TotalCount.Should().Be(1);
        paginated.Data[0].ToolName.Should().Be("web_search");
    }

    // --- Pagination ---

    [Test]
    public async Task SearchEvents_Pagination_ReturnsCorrectPage()
    {
        for (int i = 0; i < 5; i++)
            _db.AgentEvents.Add(CreateEvent(timestamp: DateTime.UtcNow.AddMinutes(-i - 1)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(page: 2, pageSize: 2, ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().HaveCount(2);
        paginated.Page.Should().Be(2);
        paginated.PageSize.Should().Be(2);
        paginated.TotalCount.Should().Be(5);
        paginated.TotalPages.Should().Be(3);
    }

    [Test]
    public async Task SearchEvents_EmptyResults_ReturnsEmptyList()
    {
        var result = await _controller.SearchEvents(ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().BeEmpty();
        paginated.TotalCount.Should().Be(0);
        paginated.TotalPages.Should().Be(0);
    }

    // --- Ordering ---

    [Test]
    public async Task SearchEvents_ResultsOrderedByTimestampDescending()
    {
        var now = DateTime.UtcNow;
        _db.AgentEvents.Add(CreateEvent(agentId: "older", timestamp: now.AddMinutes(-30)));
        _db.AgentEvents.Add(CreateEvent(agentId: "newer", timestamp: now.AddMinutes(-5)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().HaveCount(2);
        paginated.Data[0].AgentId.Should().Be("newer");
        paginated.Data[1].AgentId.Should().Be("older");
    }
}
