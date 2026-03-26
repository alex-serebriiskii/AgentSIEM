namespace Siem.Api.Models.Requests;

public class CreateSuppressionRequest
{
    public Guid? RuleId { get; set; }
    public string? AgentId { get; set; }
    public string Reason { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public int DurationMinutes { get; set; }
}
