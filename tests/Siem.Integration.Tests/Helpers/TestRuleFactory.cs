using System.Text.Json;
using Siem.Api.Data.Entities;
using Siem.Api.Data.Enums;

namespace Siem.Integration.Tests.Helpers;

public static class TestRuleFactory
{
    public const string FieldEqualsCondition =
        """{"type":"field","field":"eventType","operator":"Eq","value":"tool_invocation"}""";

    public const string ThresholdCondition =
        """{"type":"threshold","field":"latencyMs","limit":500.0,"above":true}""";

    public static RuleEntity CreateSingleEventRule(
        Guid? id = null,
        string name = "Test Rule",
        string? conditionJson = null,
        Severity severity = Severity.Medium,
        bool enabled = true)
    {
        var now = DateTime.UtcNow;
        return new RuleEntity
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Description = "Integration test rule",
            Enabled = enabled,
            Severity = severity,
            ConditionJson = conditionJson ?? FieldEqualsCondition,
            EvaluationType = "SingleEvent",
            ActionsJson = "[]",
            Tags = [],
            CreatedBy = "integration-test",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static RuleEntity CreateTemporalRule(
        Guid? id = null,
        string name = "Temporal Rule",
        double windowSeconds = 60,
        int threshold = 5,
        string? conditionJson = null,
        string aggregation = "count",
        string partitionField = "agentId")
    {
        var now = DateTime.UtcNow;
        return new RuleEntity
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Description = "Integration test temporal rule",
            Enabled = true,
            Severity = Severity.High,
            ConditionJson = conditionJson ?? FieldEqualsCondition,
            EvaluationType = "Temporal",
            TemporalConfig = $$"""{"windowSeconds":{{windowSeconds}},"threshold":{{threshold}},"aggregation":"{{aggregation}}","partitionField":"{{partitionField}}"}""",
            ActionsJson = "[]",
            Tags = [],
            CreatedBy = "integration-test",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static RuleEntity CreateSequenceRule(
        Guid? id = null,
        string name = "Sequence Rule",
        double maxSpanSeconds = 300,
        (string label, string conditionJson)[]? steps = null)
    {
        var now = DateTime.UtcNow;
        steps ??=
        [
            ("rag_retrieval", """{"type":"field","field":"eventType","operator":"Eq","value":"rag_retrieval"}"""),
            ("external_api", """{"type":"field","field":"eventType","operator":"Eq","value":"external_api_call"}""")
        ];

        var stepsJson = string.Join(",", steps.Select(s =>
            $$"""{"label":"{{s.label}}","condition":{{s.conditionJson}}}"""));

        return new RuleEntity
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Description = "Integration test sequence rule",
            Enabled = true,
            Severity = Severity.Critical,
            ConditionJson = """{"type":"exists","field":"eventType"}""",
            EvaluationType = "Sequence",
            SequenceConfig = $$"""{"maxSpanSeconds":{{maxSpanSeconds}},"steps":[{{stepsJson}}]}""",
            ActionsJson = "[]",
            Tags = [],
            CreatedBy = "integration-test",
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
