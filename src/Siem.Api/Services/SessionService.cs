using Microsoft.EntityFrameworkCore;
using Npgsql;
using Siem.Api.Data;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public class SessionService(SiemDbContext db, NpgsqlDataSource dataSource) : ISessionService
{
    public async Task<ServiceResult<IReadOnlyList<SessionResponse>>> ListAsync(
        string? agentId, bool? hasAlerts, CancellationToken ct)
    {
        var query = db.AgentSessions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(agentId))
            query = query.Where(s => s.AgentId == agentId);

        if (hasAlerts.HasValue)
            query = query.Where(s => s.HasAlerts == hasAlerts.Value);

        var sessions = await query
            .OrderByDescending(s => s.LastEventAt)
            .ToListAsync(ct);

        return ServiceResult<IReadOnlyList<SessionResponse>>.Success(
            sessions.Select(SessionResponse.FromEntity).ToList());
    }

    public async Task<ServiceResult<SessionResponse>> GetAsync(string id, CancellationToken ct)
    {
        var session = await db.AgentSessions.FindAsync([id], ct);
        if (session == null)
            return ServiceResult<SessionResponse>.NotFound();

        return ServiceResult<SessionResponse>.Success(SessionResponse.FromEntity(session));
    }

    public async Task<ServiceResult<SessionTimelineResponse>> GetTimelineAsync(
        string id, int limit, CancellationToken ct)
    {
        if (limit < 1) limit = 1;
        if (limit > 5000) limit = 5000;

        var session = await db.AgentSessions.FindAsync([id], ct);
        if (session == null)
            return ServiceResult<SessionTimelineResponse>.NotFound();

        var events = new List<SessionTimelineEventResponse>();
        await using var cmd = dataSource.CreateCommand(
            "SELECT * FROM get_session_timeline(@session_id, @lim)");
        cmd.Parameters.AddWithValue("session_id", id);
        cmd.Parameters.AddWithValue("lim", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(new SessionTimelineEventResponse
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
                    ? null : reader.GetInt32(reader.GetOrdinal("input_tokens")),
                OutputTokens = reader.IsDBNull(reader.GetOrdinal("output_tokens"))
                    ? null : reader.GetInt32(reader.GetOrdinal("output_tokens")),
                LatencyMs = reader.IsDBNull(reader.GetOrdinal("latency_ms"))
                    ? null : reader.GetDouble(reader.GetOrdinal("latency_ms")),
                AlertIds = reader.IsDBNull(reader.GetOrdinal("alert_ids"))
                    ? [] : (Guid[])reader.GetValue(reader.GetOrdinal("alert_ids")),
                AlertSeverities = reader.IsDBNull(reader.GetOrdinal("alert_severities"))
                    ? [] : (string[])reader.GetValue(reader.GetOrdinal("alert_severities"))
            });
        }

        return ServiceResult<SessionTimelineResponse>.Success(new SessionTimelineResponse
        {
            SessionId = id,
            EventCount = events.Count,
            Events = events
        });
    }
}
