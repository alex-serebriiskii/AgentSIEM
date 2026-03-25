namespace Siem.Api.Models.Responses;

using System.Text.Json;
using Siem.Api.Data.Entities;

public class RuleResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; }
    public string Severity { get; set; } = "";
    public JsonElement ConditionJson { get; set; }
    public string EvaluationType { get; set; } = "";
    public JsonElement? TemporalConfig { get; set; }
    public JsonElement? SequenceConfig { get; set; }
    public JsonElement? ActionsJson { get; set; }
    public string[] Tags { get; set; } = [];
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static RuleResponse FromEntity(RuleEntity entity)
    {
        return new RuleResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Enabled = entity.Enabled,
            Severity = entity.Severity,
            ConditionJson = JsonDocument.Parse(entity.ConditionJson).RootElement,
            EvaluationType = entity.EvaluationType,
            TemporalConfig = entity.TemporalConfig != null
                ? JsonDocument.Parse(entity.TemporalConfig).RootElement
                : null,
            SequenceConfig = entity.SequenceConfig != null
                ? JsonDocument.Parse(entity.SequenceConfig).RootElement
                : null,
            ActionsJson = JsonDocument.Parse(entity.ActionsJson).RootElement,
            Tags = entity.Tags,
            CreatedBy = entity.CreatedBy,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
