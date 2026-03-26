using Microsoft.EntityFrameworkCore;

namespace Siem.Api.Data.Entities;

[Keyless]
public class AgentRiskSummary
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
    public string TopEventTypes { get; set; } = "{}";
    public string TopTools { get; set; } = "{}";
}
