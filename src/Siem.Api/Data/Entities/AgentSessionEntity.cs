namespace Siem.Api.Data.Entities;

public class AgentSessionEntity
{
    public string SessionId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime LastEventAt { get; set; }
    public int EventCount { get; set; } = 1;
    public bool HasAlerts { get; set; }
    public short AlertCount { get; set; }
    public string? MaxSeverity { get; set; }
    public string Metadata { get; set; } = "{}";
}
