using System.Text.Json;
using FluentAssertions;
using Siem.Rules.Core;
using static Siem.Rules.Core.Tests.TestHelpers;

namespace Siem.Rules.Core.Tests;

public class SerializationTests
{
    [Test]
    public async Task ParseCondition_Field_ReturnsFieldCondition()
    {
        var json = JsonValue("""
        {
            "type": "field",
            "field": "eventType",
            "operator": "Eq",
            "value": "tool_invocation"
        }
        """);

        var condition = Serialization.parseCondition(json);

        condition.IsField.Should().BeTrue();
    }

    [Test]
    public async Task ParseCondition_Threshold_ReturnsThresholdCondition()
    {
        var json = JsonValue("""
        {
            "type": "threshold",
            "field": "latencyMs",
            "limit": 500.0,
            "above": true
        }
        """);

        var condition = Serialization.parseCondition(json);

        condition.IsThreshold.Should().BeTrue();
    }

    [Test]
    public async Task ParseCondition_And_ReturnsAndCondition()
    {
        var json = JsonValue("""
        {
            "type": "and",
            "conditions": [
                { "type": "field", "field": "eventType", "operator": "Eq", "value": "tool_invocation" },
                { "type": "field", "field": "agentId", "operator": "Eq", "value": "agent-001" }
            ]
        }
        """);

        var condition = Serialization.parseCondition(json);

        condition.IsAnd.Should().BeTrue();
    }

    [Test]
    public async Task ParseCondition_Or_ReturnsOrCondition()
    {
        var json = JsonValue("""
        {
            "type": "or",
            "conditions": [
                { "type": "field", "field": "eventType", "operator": "Eq", "value": "llm_call" },
                { "type": "field", "field": "eventType", "operator": "Eq", "value": "tool_invocation" }
            ]
        }
        """);

        var condition = Serialization.parseCondition(json);

        condition.IsOr.Should().BeTrue();
    }

    [Test]
    public async Task ParseCondition_Not_ReturnsNotCondition()
    {
        var json = JsonValue("""
        {
            "type": "not",
            "inner": { "type": "field", "field": "eventType", "operator": "Eq", "value": "llm_call" }
        }
        """);

        var condition = Serialization.parseCondition(json);

        condition.IsNot.Should().BeTrue();
    }

    [Test]
    public async Task ParseCondition_List_ReturnsInListCondition()
    {
        var listId = Guid.NewGuid();
        var json = JsonValue($$"""
        {
            "type": "list",
            "field": "agentId",
            "listId": "{{listId}}",
            "negated": false
        }
        """);

        var condition = Serialization.parseCondition(json);

        condition.IsInList.Should().BeTrue();
    }

    [Test]
    public async Task ParseCondition_UnknownType_ThrowsException()
    {
        var json = JsonValue("""
        {
            "type": "unknown_type",
            "field": "eventType"
        }
        """);

        var act = () => Serialization.parseCondition(json);

        act.Should().Throw<Exception>();
    }

    [Test]
    public async Task ParseCondition_Exists_ReturnsExistsCondition()
    {
        var json = JsonValue("""
        {
            "type": "exists",
            "field": "toolName"
        }
        """);

        var condition = Serialization.parseCondition(json);

        condition.IsExists.Should().BeTrue();
    }

    [Test]
    public async Task ParseCondition_AnyOf_ReturnsAnyOfCondition()
    {
        var json = JsonValue("""
        {
            "type": "any_of",
            "field": "eventType",
            "values": ["tool_invocation", "llm_call"]
        }
        """);

        var condition = Serialization.parseCondition(json);

        condition.IsAnyOf.Should().BeTrue();
    }

    [Test]
    public async Task ParseCondition_UnknownOperator_ThrowsException()
    {
        var json = JsonValue("""
        {
            "type": "field",
            "field": "eventType",
            "operator": "InvalidOp",
            "value": "test"
        }
        """);

        var act = () => Serialization.parseCondition(json);

        act.Should().Throw<Exception>();
    }

    [Test]
    public async Task ParseCondition_MissingType_ThrowsWithPropertyName()
    {
        var json = JsonValue("{}");

        var act = () => Serialization.parseCondition(json);

        act.Should().Throw<Exception>()
            .WithMessage("*Missing required property*'type'*");
    }

    [Test]
    public async Task ParseCondition_MissingField_ThrowsWithPropertyName()
    {
        var json = JsonValue("""
        {
            "type": "field",
            "operator": "Eq",
            "value": "x"
        }
        """);

        var act = () => Serialization.parseCondition(json);

        act.Should().Throw<Exception>()
            .WithMessage("*Missing required property*'field'*");
    }

    [Test]
    public async Task ParseCondition_MissingConditions_ThrowsWithPropertyName()
    {
        var json = JsonValue("""{"type": "and"}""");

        var act = () => Serialization.parseCondition(json);

        act.Should().Throw<Exception>()
            .WithMessage("*Missing required property*'conditions'*");
    }
}
