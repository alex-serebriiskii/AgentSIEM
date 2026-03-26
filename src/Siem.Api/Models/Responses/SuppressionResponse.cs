using Siem.Api.Data.Entities;

namespace Siem.Api.Models.Responses;

public class SuppressionResponse
{
    public Guid Id { get; set; }
    public Guid? RuleId { get; set; }
    public string? AgentId { get; set; }
    public string Reason { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }

    public static SuppressionResponse FromEntity(SuppressionEntity entity)
    {
        return new SuppressionResponse
        {
            Id = entity.Id,
            RuleId = entity.RuleId,
            AgentId = entity.AgentId,
            Reason = entity.Reason,
            CreatedBy = entity.CreatedBy,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt,
            IsActive = entity.ExpiresAt > DateTime.UtcNow
        };
    }
}
