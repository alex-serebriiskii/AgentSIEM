using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Models.Responses;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly SiemDbContext _db;
    private readonly NpgsqlDataSource _dataSource;

    public SessionsController(SiemDbContext db, NpgsqlDataSource dataSource)
    {
        _db = db;
        _dataSource = dataSource;
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
    /// Returns events in chronological order with alert annotations.
    /// </summary>
    [HttpGet("{id}/timeline")]
    public async Task<IActionResult> GetSessionTimeline(
        [FromRoute] string id,
        [FromQuery] int limit = 1000,
        CancellationToken ct = default)
    {
        if (limit < 1) limit = 1;
        if (limit > 5000) limit = 5000;

        // Verify session exists
        var session = await _db.AgentSessions.FindAsync([id], ct);
        if (session == null) return NotFound();

        // Use the get_session_timeline database function via raw ADO.NET.
        // EF Core's FromSqlInterpolated wraps the query in a subquery causing column ambiguity.
        var events = new List<object>();
        await using var cmd = _dataSource.CreateCommand(
            "SELECT * FROM get_session_timeline(@session_id, @lim)");
        cmd.Parameters.AddWithValue("session_id", id);
        cmd.Parameters.AddWithValue("lim", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(new
            {
                EventId = reader.GetGuid(reader.GetOrdinal("event_id")),
                Timestamp = reader.GetDateTime(reader.GetOrdinal("timestamp")),
                EventType = reader.GetString(reader.GetOrdinal("event_type")),
                AgentId = reader.GetString(reader.GetOrdinal("agent_id")),
                ToolName = reader.IsDBNull(reader.GetOrdinal("tool_name"))
                    ? null : reader.GetString(reader.GetOrdinal("tool_name")),
                ModelId = reader.IsDBNull(reader.GetOrdinal("model_id"))
                    ? null : reader.GetString(reader.GetOrdinal("model_id")),
                InputTokens = reader.IsDBNull(reader.GetOrdinal("input_tokens"))
                    ? (int?)null : reader.GetInt32(reader.GetOrdinal("input_tokens")),
                OutputTokens = reader.IsDBNull(reader.GetOrdinal("output_tokens"))
                    ? (int?)null : reader.GetInt32(reader.GetOrdinal("output_tokens")),
                LatencyMs = reader.IsDBNull(reader.GetOrdinal("latency_ms"))
                    ? (double?)null : reader.GetDouble(reader.GetOrdinal("latency_ms")),
                AlertIds = reader.IsDBNull(reader.GetOrdinal("alert_ids"))
                    ? Array.Empty<Guid>() : (Guid[])reader.GetValue(reader.GetOrdinal("alert_ids")),
                AlertSeverities = reader.IsDBNull(reader.GetOrdinal("alert_severities"))
                    ? Array.Empty<string>() : (string[])reader.GetValue(reader.GetOrdinal("alert_severities"))
            });
        }

        return Ok(new
        {
            sessionId = id,
            eventCount = events.Count,
            events
        });
    }
}
