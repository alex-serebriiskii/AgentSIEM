using System.Text.Json;
using Siem.Api.Data.Entities;
using Siem.Api.Data.Enums;

namespace Siem.Api.Tests.Controllers.Helpers;

public static class TestEntityBuilders
{
    /// <summary>
    /// A simple valid condition JSON that passes the F# Serialization.parseCondition parser.
    /// </summary>
    public const string ValidConditionJson = """{"type":"field","field":"eventType","operator":"Eq","value":"tool_invocation"}""";

    public static RuleEntity CreateRule(
        Guid? id = null,
        string name = "Test Rule",
        string description = "Test description",
        bool enabled = true,
        Severity severity = Severity.Medium,
        string? conditionJson = null,
        string evaluationType = "SingleEvent",
        string createdBy = "test-user",
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        var now = createdAt ?? DateTime.UtcNow;
        return new RuleEntity
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Description = description,
            Enabled = enabled,
            Severity = severity,
            ConditionJson = conditionJson ?? ValidConditionJson,
            EvaluationType = evaluationType,
            ActionsJson = "[]",
            Tags = [],
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = updatedAt ?? now
        };
    }

    public static AlertEntity CreateAlert(
        Guid? alertId = null,
        Guid? ruleId = null,
        string ruleName = "Test Rule",
        Severity severity = Severity.Medium,
        AlertStatus status = AlertStatus.Open,
        string title = "Test Alert",
        string agentId = "agent-001",
        string? sessionId = "sess-001",
        DateTime? triggeredAt = null)
    {
        return new AlertEntity
        {
            AlertId = alertId ?? Guid.NewGuid(),
            RuleId = ruleId ?? Guid.NewGuid(),
            RuleName = ruleName,
            Severity = severity,
            Status = status,
            Title = title,
            Detail = "Test detail",
            Context = "{}",
            AgentId = agentId,
            SessionId = sessionId,
            TriggeredAt = triggeredAt ?? DateTime.UtcNow,
            Labels = "{}"
        };
    }

    public static AlertEventEntity CreateAlertEvent(
        Guid alertId,
        Guid? eventId = null,
        DateTime? eventTimestamp = null,
        short? sequenceOrder = null)
    {
        return new AlertEventEntity
        {
            AlertId = alertId,
            EventId = eventId ?? Guid.NewGuid(),
            EventTimestamp = eventTimestamp ?? DateTime.UtcNow,
            SequenceOrder = sequenceOrder
        };
    }

    public static AgentSessionEntity CreateSession(
        string? sessionId = null,
        string agentId = "agent-001",
        string agentName = "TestAgent",
        bool hasAlerts = false,
        DateTime? lastEventAt = null)
    {
        var now = DateTime.UtcNow;
        return new AgentSessionEntity
        {
            SessionId = sessionId ?? $"sess-{Guid.NewGuid():N}",
            AgentId = agentId,
            AgentName = agentName,
            StartedAt = now.AddMinutes(-10),
            LastEventAt = lastEventAt ?? now,
            EventCount = 5,
            HasAlerts = hasAlerts,
            AlertCount = hasAlerts ? (short)1 : (short)0,
            MaxSeverity = hasAlerts ? "medium" : null,
            Metadata = "{}"
        };
    }

    public static ManagedListEntity CreateManagedList(
        Guid? id = null,
        string name = "Test List",
        string description = "Test description",
        bool enabled = true,
        List<string>? members = null)
    {
        var now = DateTime.UtcNow;
        var entity = new ManagedListEntity
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Description = description,
            Enabled = enabled,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (members != null)
        {
            entity.Members = members.Select(v => new ListMemberEntity
            {
                ListId = entity.Id,
                Value = v,
                AddedAt = now
            }).ToList();
        }

        return entity;
    }

    public static SuppressionEntity CreateSuppression(
        Guid? id = null,
        Guid? ruleId = null,
        string? agentId = null,
        string reason = "Test suppression",
        string createdBy = "test-user",
        DateTime? createdAt = null,
        DateTime? expiresAt = null)
    {
        var now = createdAt ?? DateTime.UtcNow;
        return new SuppressionEntity
        {
            Id = id ?? Guid.NewGuid(),
            RuleId = ruleId,
            AgentId = agentId,
            Reason = reason,
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = expiresAt ?? now.AddHours(1)
        };
    }

    public static JsonElement ParseJson(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }
}
