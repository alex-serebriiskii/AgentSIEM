using Microsoft.EntityFrameworkCore;

namespace Siem.Api.Data.Entities;

[Keyless]
public class AgentActivityHourlyView
{
    public DateTime Bucket { get; set; }
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string EventType { get; set; } = "";
    public long EventCount { get; set; }
    public long? TotalInputTokens { get; set; }
    public long? TotalOutputTokens { get; set; }
    public long? TotalTokens { get; set; }
    public double? AvgLatencyMs { get; set; }
    public double? MaxLatencyMs { get; set; }
    public double? P95LatencyMs { get; set; }
    public long UniqueSessionsCount { get; set; }
    public long UniqueToolsUsed { get; set; }
}
