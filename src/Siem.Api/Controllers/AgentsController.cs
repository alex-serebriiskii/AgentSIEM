using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Siem.Api.Data.Entities;
using Siem.Api.Models.Responses;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly NpgsqlDataSource _dataSource;

    public AgentsController(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>
    /// Get risk summary for an agent. Aggregates recent activity across
    /// events, alerts, tools, and tokens using the get_agent_risk_summary database function.
    /// </summary>
    [HttpGet("{id}/risk")]
    public async Task<IActionResult> GetRiskSummary(
        [FromRoute] string id,
        [FromQuery] string lookback = "24 hours",
        CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(
            "SELECT * FROM get_agent_risk_summary(@agent_id, @lookback::interval)");
        cmd.Parameters.AddWithValue("agent_id", id);
        cmd.Parameters.AddWithValue("lookback", lookback);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return Ok(new
            {
                agentId = id,
                agentName = (string?)null,
                totalEvents = 0,
                totalSessions = 0,
                openAlerts = 0,
                criticalAlerts = 0,
                uniqueTools = 0,
                totalTokens = 0,
                avgLatencyMs = 0.0,
                eventsPerMinute = 0.0,
                topEventTypes = new { },
                topTools = new { }
            });
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

        return Ok(AgentRiskSummaryResponse.FromEntity(summary));
    }
}
