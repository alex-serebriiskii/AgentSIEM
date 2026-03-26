using Npgsql;
using Siem.Api.Data.Entities;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public class AgentService(NpgsqlDataSource dataSource) : IAgentService
{
    public async Task<ServiceResult<AgentRiskSummaryResponse>> GetRiskSummaryAsync(
        string id, string lookback, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT * FROM get_agent_risk_summary(@agent_id, @lookback::interval)");
        cmd.Parameters.AddWithValue("agent_id", id);
        cmd.Parameters.AddWithValue("lookback", lookback);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Return empty summary for unknown agent
            var empty = new AgentRiskSummary
            {
                AgentId = id,
                AgentName = null!,
                TotalEvents = 0,
                TotalSessions = 0,
                OpenAlerts = 0,
                CriticalAlerts = 0,
                UniqueTools = 0,
                TotalTokens = 0,
                AvgLatencyMs = 0.0,
                EventsPerMinute = 0.0,
                TopEventTypes = "{}",
                TopTools = "{}"
            };
            return ServiceResult<AgentRiskSummaryResponse>.Success(
                AgentRiskSummaryResponse.FromEntity(empty));
        }

        var summary = new AgentRiskSummary
        {
            AgentId = reader.GetString(reader.GetOrdinal("agent_id")),
            AgentName = reader.GetString(reader.GetOrdinal("agent_name")),
            TotalEvents = reader.GetInt64(reader.GetOrdinal("total_events")),
            TotalSessions = reader.GetInt64(reader.GetOrdinal("total_sessions")),
            OpenAlerts = reader.GetInt64(reader.GetOrdinal("open_alerts")),
            CriticalAlerts = reader.GetInt64(reader.GetOrdinal("critical_alerts")),
            UniqueTools = reader.GetInt64(reader.GetOrdinal("unique_tools")),
            TotalTokens = reader.GetInt64(reader.GetOrdinal("total_tokens")),
            AvgLatencyMs = reader.IsDBNull(reader.GetOrdinal("avg_latency_ms"))
                ? 0.0 : reader.GetDouble(reader.GetOrdinal("avg_latency_ms")),
            EventsPerMinute = reader.GetDouble(reader.GetOrdinal("events_per_minute")),
            TopEventTypes = reader.IsDBNull(reader.GetOrdinal("top_event_types"))
                ? "{}" : reader.GetString(reader.GetOrdinal("top_event_types")),
            TopTools = reader.IsDBNull(reader.GetOrdinal("top_tools"))
                ? "{}" : reader.GetString(reader.GetOrdinal("top_tools"))
        };

        return ServiceResult<AgentRiskSummaryResponse>.Success(
            AgentRiskSummaryResponse.FromEntity(summary));
    }
}
