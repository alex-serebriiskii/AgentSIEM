using Microsoft.EntityFrameworkCore;

namespace Siem.Api.Data.Entities;

[Keyless]
public class ToolUsageHourlyView
{
    public DateTime Bucket { get; set; }
    public string ToolName { get; set; } = "";
    public string AgentId { get; set; } = "";
    public long InvocationCount { get; set; }
    public double? AvgLatencyMs { get; set; }
    public long UniqueSessions { get; set; }
}
