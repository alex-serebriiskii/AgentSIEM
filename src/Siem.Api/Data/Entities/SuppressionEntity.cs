namespace Siem.Api.Data.Entities;

public class SuppressionEntity
{
    public Guid Id { get; set; }
    public Guid? RuleId { get; set; }
    public string? AgentId { get; set; }
    public string Reason { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
