namespace Siem.Api.Data.Entities;

public class AlertEntity
{
    public Guid AlertId { get; set; }
    public Guid RuleId { get; set; }
    public string RuleName { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Status { get; set; } = "open";
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public string Context { get; set; } = "{}";
    public string AgentId { get; set; } = "";
    public string? SessionId { get; set; }
    public DateTime TriggeredAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? AssignedTo { get; set; }
    public string? ResolutionNote { get; set; }
    public string Labels { get; set; } = "{}";
    public bool Suppressed { get; set; }
    public Guid? SuppressedBy { get; set; }
    public DateTime? SuppressionExpiresAt { get; set; }
    public List<AlertEventEntity> AlertEvents { get; set; } = [];
}
