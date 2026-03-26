using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Models.Responses;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly SiemDbContext _db;

    public EventsController(SiemDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Search events with time range, agent, type, session, tool, and JSONB property filters.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> SearchEvents(
        [FromQuery] DateTimeOffset? start = null,
        [FromQuery] DateTimeOffset? end = null,
        [FromQuery] string? agent_id = null,
        [FromQuery] string? event_type = null,
        [FromQuery] string? session_id = null,
        [FromQuery] string? tool_name = null,
        [FromQuery] string? properties = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 500) pageSize = 500;

        var effectiveStart = start?.UtcDateTime ?? DateTime.UtcNow.AddHours(-1);
        var effectiveEnd = end?.UtcDateTime ?? DateTime.UtcNow;

        var query = _db.AgentEvents.AsQueryable();

        query = query.Where(e => e.Timestamp >= effectiveStart && e.Timestamp <= effectiveEnd);

        if (!string.IsNullOrWhiteSpace(agent_id))
            query = query.Where(e => e.AgentId == agent_id);

        if (!string.IsNullOrWhiteSpace(event_type))
            query = query.Where(e => e.EventType == event_type);

        if (!string.IsNullOrWhiteSpace(session_id))
            query = query.Where(e => e.SessionId == session_id);

        if (!string.IsNullOrWhiteSpace(tool_name))
            query = query.Where(e => e.ToolName == tool_name);

        // JSONB containment filter requires PostgreSQL — applied via raw SQL.
        // When using InMemory provider (unit tests), this filter is skipped.
        if (!string.IsNullOrWhiteSpace(properties))
        {
            query = query.Where(e =>
                EF.Functions.JsonContains(e.Properties, properties));
        }

        var totalCount = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var events = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new
        {
            data = events.Select(EventResponse.FromEntity),
            page,
            pageSize,
            totalCount,
            totalPages
        });
    }
}
