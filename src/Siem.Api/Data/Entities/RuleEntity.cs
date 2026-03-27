using Siem.Api.Data.Enums;

namespace Siem.Api.Data.Entities;

public class RuleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public Severity Severity { get; set; } = Severity.Medium;
    public string ConditionJson { get; set; } = "{}";
    public string EvaluationType { get; set; } = "SingleEvent";
    public string? TemporalConfig { get; set; }
    public string? SequenceConfig { get; set; }
    public string ActionsJson { get; set; } = "[]";
    public string[] Tags { get; set; } = [];
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
