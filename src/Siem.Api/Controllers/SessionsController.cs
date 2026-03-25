using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Models.Responses;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly SiemDbContext _db;

    public SessionsController(SiemDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// List sessions with optional filters for agent_id and has_alerts.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> ListSessions(
        [FromQuery] string? agent_id,
        [FromQuery] bool? has_alerts,
        CancellationToken ct)
    {
        var query = _db.AgentSessions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(agent_id))
            query = query.Where(s => s.AgentId == agent_id);

        if (has_alerts.HasValue)
            query = query.Where(s => s.HasAlerts == has_alerts.Value);

        var sessions = await query
            .OrderByDescending(s => s.LastEventAt)
            .ToListAsync(ct);

        return Ok(sessions.Select(SessionResponse.FromEntity));
    }

    /// <summary>
    /// Get a single session by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetSession(
        [FromRoute] string id, CancellationToken ct)
    {
        var session = await _db.AgentSessions.FindAsync([id], ct);
        if (session == null) return NotFound();

        return Ok(SessionResponse.FromEntity(session));
    }

    /// <summary>
    /// Get the timeline of events for a session.
    /// Uses the get_session_timeline database function for optimized retrieval.
    /// </summary>
    [HttpGet("{id}/timeline")]
    public async Task<IActionResult> GetSessionTimeline(
        [FromRoute] string id, CancellationToken ct)
    {
        // Verify session exists
        var session = await _db.AgentSessions.FindAsync([id], ct);
        if (session == null) return NotFound();

        // Use the get_session_timeline database function via raw SQL
        var events = await _db.AgentEvents
            .FromSqlInterpolated(
                $"SELECT * FROM get_session_timeline({id})")
            .ToListAsync(ct);

        return Ok(new
        {
            SessionId = id,
            Events = events.Select(e => new
            {
                e.EventId,
                e.Timestamp,
                e.AgentId,
                e.AgentName,
                e.EventType,
                e.SeverityHint,
                e.ModelId,
                e.InputTokens,
                e.OutputTokens,
                e.LatencyMs,
                e.ToolName,
                e.ToolInput,
                e.ToolOutput,
                e.ContentHash,
                e.SourceSdk
            })
        });
    }
}
