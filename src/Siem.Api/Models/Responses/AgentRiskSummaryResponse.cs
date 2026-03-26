namespace Siem.Api.Models.Responses;

using System.Text.Json;
using Siem.Api.Data.Entities;

public class AgentRiskSummaryResponse
{
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    public long TotalEvents { get; set; }
    public long TotalSessions { get; set; }
    public long OpenAlerts { get; set; }
    public long CriticalAlerts { get; set; }
    public long UniqueTools { get; set; }
    public long TotalTokens { get; set; }
    public double AvgLatencyMs { get; set; }
    public double EventsPerMinute { get; set; }
    public JsonElement TopEventTypes { get; set; }
    public JsonElement TopTools { get; set; }

    public static AgentRiskSummaryResponse FromEntity(AgentRiskSummary entity)
    {
        return new AgentRiskSummaryResponse
        {
            AgentId = entity.AgentId,
            AgentName = entity.AgentName,
            TotalEvents = entity.TotalEvents,
            TotalSessions = entity.TotalSessions,
            OpenAlerts = entity.OpenAlerts,
            CriticalAlerts = entity.CriticalAlerts,
            UniqueTools = entity.UniqueTools,
            TotalTokens = entity.TotalTokens,
            AvgLatencyMs = entity.AvgLatencyMs,
            EventsPerMinute = entity.EventsPerMinute,
            TopEventTypes = JsonDocument.Parse(entity.TopEventTypes).RootElement,
            TopTools = JsonDocument.Parse(entity.TopTools).RootElement
        };
    }
}
