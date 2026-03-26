using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Siem.Api.Controllers;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Models.Responses;
using Siem.Api.Tests.Controllers.Helpers;

namespace Siem.Api.Tests.Controllers;

public class EventsControllerTests : IDisposable
{
    private readonly SiemDbContext _db;
    private readonly EventsController _controller;

    public EventsControllerTests()
    {
        _db = DbContextFactory.Create();
        _controller = new EventsController(_db);
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

    private static (List<EventResponse> Data, int Page, int PageSize, int TotalCount, int TotalPages)
        ExtractPaginatedResult(IActionResult result)
    {
        var ok = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var data = JsonSerializer.Deserialize<List<EventResponse>>(
            root.GetProperty("data").GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return (
            data,
            root.GetProperty("page").GetInt32(),
            root.GetProperty("pageSize").GetInt32(),
            root.GetProperty("totalCount").GetInt32(),
            root.GetProperty("totalPages").GetInt32()
        );
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

        var (events, page, pageSize, totalCount, _) = ExtractPaginatedResult(result);
        events.Should().HaveCount(1);
        page.Should().Be(1);
        pageSize.Should().Be(100);
        totalCount.Should().Be(1);
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

        var (events, _, _, totalCount, _) = ExtractPaginatedResult(result);
        totalCount.Should().Be(2);
    }

    // --- Filters ---

    [Test]
    public async Task SearchEvents_FilterByAgentId_ReturnsOnlyMatching()
    {
        _db.AgentEvents.Add(CreateEvent(agentId: "agent-A", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        _db.AgentEvents.Add(CreateEvent(agentId: "agent-B", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(agent_id: "agent-A", ct: CancellationToken.None);

        var (events, _, _, totalCount, _) = ExtractPaginatedResult(result);
        totalCount.Should().Be(1);
        events[0].AgentId.Should().Be("agent-A");
    }

    [Test]
    public async Task SearchEvents_FilterByEventType_ReturnsOnlyMatching()
    {
        _db.AgentEvents.Add(CreateEvent(eventType: "tool_invocation", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        _db.AgentEvents.Add(CreateEvent(eventType: "llm_call", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(event_type: "llm_call", ct: CancellationToken.None);

        var (events, _, _, totalCount, _) = ExtractPaginatedResult(result);
        totalCount.Should().Be(1);
        events[0].EventType.Should().Be("llm_call");
    }

    [Test]
    public async Task SearchEvents_FilterBySessionId_ReturnsOnlyMatching()
    {
        _db.AgentEvents.Add(CreateEvent(sessionId: "sess-A", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        _db.AgentEvents.Add(CreateEvent(sessionId: "sess-B", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(session_id: "sess-A", ct: CancellationToken.None);

        var (events, _, _, totalCount, _) = ExtractPaginatedResult(result);
        totalCount.Should().Be(1);
        events[0].SessionId.Should().Be("sess-A");
    }

    [Test]
    public async Task SearchEvents_FilterByToolName_ReturnsOnlyMatching()
    {
        _db.AgentEvents.Add(CreateEvent(toolName: "web_search", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        _db.AgentEvents.Add(CreateEvent(toolName: "file_read", timestamp: DateTime.UtcNow.AddMinutes(-5)));
        _db.AgentEvents.Add(CreateEvent(timestamp: DateTime.UtcNow.AddMinutes(-5)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(tool_name: "web_search", ct: CancellationToken.None);

        var (events, _, _, totalCount, _) = ExtractPaginatedResult(result);
        totalCount.Should().Be(1);
        events[0].ToolName.Should().Be("web_search");
    }

    // --- Pagination ---

    [Test]
    public async Task SearchEvents_Pagination_ReturnsCorrectPage()
    {
        for (int i = 0; i < 5; i++)
            _db.AgentEvents.Add(CreateEvent(timestamp: DateTime.UtcNow.AddMinutes(-i - 1)));
        await _db.SaveChangesAsync();

        var result = await _controller.SearchEvents(page: 2, pageSize: 2, ct: CancellationToken.None);

        var (events, page, pageSize, totalCount, totalPages) = ExtractPaginatedResult(result);
        events.Should().HaveCount(2);
        page.Should().Be(2);
        pageSize.Should().Be(2);
        totalCount.Should().Be(5);
        totalPages.Should().Be(3);
    }

    [Test]
    public async Task SearchEvents_EmptyResults_ReturnsEmptyList()
    {
        var result = await _controller.SearchEvents(ct: CancellationToken.None);

        var (events, _, _, totalCount, totalPages) = ExtractPaginatedResult(result);
        events.Should().BeEmpty();
        totalCount.Should().Be(0);
        totalPages.Should().Be(0);
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

        var (events, _, _, _, _) = ExtractPaginatedResult(result);
        events.Should().HaveCount(2);
        events[0].AgentId.Should().Be("newer");
        events[1].AgentId.Should().Be("older");
    }
}
