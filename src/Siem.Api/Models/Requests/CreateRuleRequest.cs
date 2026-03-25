namespace Siem.Api.Models.Requests;

using System.Text.Json;

public class CreateRuleRequest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Severity { get; set; } = "medium";
    public JsonElement ConditionJson { get; set; }
    public string EvaluationType { get; set; } = "SingleEvent";
    public JsonElement? TemporalConfig { get; set; }
    public JsonElement? SequenceConfig { get; set; }
    public JsonElement? ActionsJson { get; set; }
    public string[] Tags { get; set; } = [];
    public string CreatedBy { get; set; } = "";
}
