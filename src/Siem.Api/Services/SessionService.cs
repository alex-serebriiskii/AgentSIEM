using Microsoft.EntityFrameworkCore;
using Npgsql;
using Siem.Api.Data;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public class SessionService(SiemDbContext db, NpgsqlDataSource dataSource, PaginationConfig paginationConfig) : ISessionService
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
        if (limit > paginationConfig.SessionTimelineMaxLimit) limit = paginationConfig.SessionTimelineMaxLimit;

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
                EventId = reader.Get<Guid>("event_id"),
                Timestamp = reader.Get<DateTime>("timestamp"),
                EventType = reader.Get<string>("event_type"),
                AgentId = reader.Get<string>("agent_id"),
                ToolName = reader.GetStringOrNull("tool_name"),
                ModelId = reader.GetStringOrNull("model_id"),
                InputTokens = reader.GetOrDefault<int>("input_tokens"),
                OutputTokens = reader.GetOrDefault<int>("output_tokens"),
                LatencyMs = reader.GetOrDefault<double>("latency_ms"),
                AlertIds = reader.GetOrFallback<Guid[]>("alert_ids", []),
                AlertSeverities = reader.GetOrFallback<string[]>("alert_severities", [])
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
