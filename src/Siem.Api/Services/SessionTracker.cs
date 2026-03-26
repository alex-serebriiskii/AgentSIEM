using Npgsql;

namespace Siem.Api.Services;

/// <summary>
/// Tracks agent sessions by calling the upsert_session() database function
/// on each ingested event. Creates a new session on first event, updates
/// last_event_at and increments event_count on subsequent events.
/// </summary>
public class SessionTracker : ISessionTracker
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<SessionTracker> _logger;

    public SessionTracker(NpgsqlDataSource dataSource, ILogger<SessionTracker> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task TrackEventAsync(
        string sessionId, string agentId, string agentName,
        DateTime timestamp, CancellationToken ct = default)
    {
        try
        {
            await using var cmd = _dataSource.CreateCommand(
                "SELECT upsert_session(@session_id, @agent_id, @agent_name, @timestamp)");
            cmd.Parameters.AddWithValue("session_id", sessionId);
            cmd.Parameters.AddWithValue("agent_id", agentId);
            cmd.Parameters.AddWithValue("agent_name", agentName);
            cmd.Parameters.AddWithValue("timestamp", timestamp);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (NpgsqlException ex)
        {
            // Session tracking is best-effort — don't fail the pipeline for transient DB errors
            _logger.LogWarning(ex,
                "Failed to upsert session {SessionId} for agent {AgentId}",
                sessionId, agentId);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex,
                "Timeout upserting session {SessionId} for agent {AgentId}",
                sessionId, agentId);
        }
    }
}
