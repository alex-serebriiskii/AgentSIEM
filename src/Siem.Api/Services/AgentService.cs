using Npgsql;
using Siem.Api.Data;
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
            AgentId = reader.Get<string>("agent_id"),
            AgentName = reader.Get<string>("agent_name"),
            TotalEvents = reader.Get<long>("total_events"),
            TotalSessions = reader.Get<long>("total_sessions"),
            OpenAlerts = reader.Get<long>("open_alerts"),
            CriticalAlerts = reader.Get<long>("critical_alerts"),
            UniqueTools = reader.Get<long>("unique_tools"),
            TotalTokens = reader.Get<long>("total_tokens"),
            AvgLatencyMs = reader.GetOrFallback("avg_latency_ms", 0.0),
            EventsPerMinute = reader.Get<double>("events_per_minute"),
            TopEventTypes = reader.GetOrFallback("top_event_types", "{}"),
            TopTools = reader.GetOrFallback("top_tools", "{}")
        };

        return ServiceResult<AgentRiskSummaryResponse>.Success(
            AgentRiskSummaryResponse.FromEntity(summary));
    }
}
