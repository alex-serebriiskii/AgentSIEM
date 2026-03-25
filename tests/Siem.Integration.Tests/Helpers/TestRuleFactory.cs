using Siem.Api.Data.Entities;

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
        string severity = "medium",
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
        int threshold = 5)
    {
        var now = DateTime.UtcNow;
        return new RuleEntity
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Description = "Integration test temporal rule",
            Enabled = true,
            Severity = "high",
            ConditionJson = FieldEqualsCondition,
            EvaluationType = "Temporal",
            TemporalConfig = $$"""{"windowSeconds":{{windowSeconds}},"threshold":{{threshold}},"partitionField":"agentId"}""",
            ActionsJson = "[]",
            Tags = [],
            CreatedBy = "integration-test",
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
