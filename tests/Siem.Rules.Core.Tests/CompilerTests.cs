using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Rules.Core;
using static Siem.Rules.Core.Tests.TestHelpers;

namespace Siem.Rules.Core.Tests;

public class CompilerTests
{
    [Test]
    public async Task Compile_FieldCondition_Eq_MatchesEvent()
    {
        // Arrange
        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(eventType: "tool_invocation");

        // Act
        var result = predicate.Invoke(evt);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public async Task Compile_FieldCondition_Eq_DoesNotMatchDifferentValue()
    {
        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("llm_call"));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(eventType: "tool_invocation");

        var result = predicate.Invoke(evt);

        result.Should().BeFalse();
    }

    [Test]
    public async Task Compile_InList_MemberFound_ReturnsTrue()
    {
        var listId = Guid.NewGuid();
        var condition = Condition.NewInList("agentId", listId, false);
        var resolver = ListResolverFor(listId, "agent-001", "agent-002");
        var predicate = Compiler.compile(resolver, condition);
        var evt = CreateEvent(agentId: "agent-001");

        var result = predicate.Invoke(evt);

        result.Should().BeTrue();
    }

    [Test]
    public async Task Compile_InList_MemberNotFound_ReturnsFalse()
    {
        var listId = Guid.NewGuid();
        var condition = Condition.NewInList("agentId", listId, false);
        var resolver = ListResolverFor(listId, "agent-002", "agent-003");
        var predicate = Compiler.compile(resolver, condition);
        var evt = CreateEvent(agentId: "agent-001");

        var result = predicate.Invoke(evt);

        result.Should().BeFalse();
    }

    [Test]
    public async Task Compile_InList_Negated_ReturnsTrueWhenNotInList()
    {
        var listId = Guid.NewGuid();
        var condition = Condition.NewInList("agentId", listId, true);
        var resolver = ListResolverFor(listId, "agent-002", "agent-003");
        var predicate = Compiler.compile(resolver, condition);
        var evt = CreateEvent(agentId: "agent-001");

        var result = predicate.Invoke(evt);

        result.Should().BeTrue();
    }

    [Test]
    public async Task Compile_AndCondition_AllMatch_ReturnsTrue()
    {
        var conditions = ListModule.OfSeq(new[]
        {
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation")),
            Condition.NewField("agentId", ComparisonOp.Eq, JsonString("agent-001"))
        });
        var condition = Condition.NewAnd(conditions);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(eventType: "tool_invocation", agentId: "agent-001");

        var result = predicate.Invoke(evt);

        result.Should().BeTrue();
    }

    [Test]
    public async Task Compile_AndCondition_OneFails_ReturnsFalse()
    {
        var conditions = ListModule.OfSeq(new[]
        {
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation")),
            Condition.NewField("agentId", ComparisonOp.Eq, JsonString("agent-999"))
        });
        var condition = Condition.NewAnd(conditions);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(eventType: "tool_invocation", agentId: "agent-001");

        var result = predicate.Invoke(evt);

        result.Should().BeFalse();
    }

    [Test]
    public async Task Compile_OrCondition_OneMatches_ReturnsTrue()
    {
        var conditions = ListModule.OfSeq(new[]
        {
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("llm_call")),
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"))
        });
        var condition = Condition.NewOr(conditions);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(eventType: "tool_invocation");

        var result = predicate.Invoke(evt);

        result.Should().BeTrue();
    }

    [Test]
    public async Task Compile_NotCondition_InvertsResult()
    {
        var inner = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("llm_call"));
        var condition = Condition.NewNot(inner);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(eventType: "tool_invocation");

        var result = predicate.Invoke(evt);

        result.Should().BeTrue();
    }

    [Test]
    public async Task Compile_Threshold_AboveLimit_ReturnsTrue()
    {
        var condition = Condition.NewThreshold("latencyMs", 100.0, true);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(latencyMs: 150.0);

        var result = predicate.Invoke(evt);

        result.Should().BeTrue();
    }

    [Test]
    public async Task Compile_Threshold_BelowLimit_ReturnsFalse()
    {
        var condition = Condition.NewThreshold("latencyMs", 100.0, true);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(latencyMs: 50.0);

        var result = predicate.Invoke(evt);

        result.Should().BeFalse();
    }

    [Test]
    public async Task Compile_Threshold_BelowMode_ReturnsTrueWhenBelow()
    {
        var condition = Condition.NewThreshold("latencyMs", 100.0, false);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(latencyMs: 50.0);

        var result = predicate.Invoke(evt);

        result.Should().BeTrue();
    }

    // --- ComparisonOp coverage ---

    [Test]
    public async Task Compile_FieldCondition_Neq_ReturnsTrueWhenDifferent()
    {
        var condition = Condition.NewField("eventType", ComparisonOp.Neq, JsonString("llm_call"));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(eventType: "tool_invocation");

        predicate.Invoke(evt).Should().BeTrue();
    }

    [Test]
    public async Task Compile_FieldCondition_Contains_ReturnsTrueWhenSubstringFound()
    {
        var condition = Condition.NewField("agentName", ComparisonOp.Contains, JsonString("Agent"));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(agentName: "TestAgent");

        predicate.Invoke(evt).Should().BeTrue();
    }

    [Test]
    public async Task Compile_FieldCondition_StartsWith_ReturnsTrueWhenPrefixMatches()
    {
        var condition = Condition.NewField("agentName", ComparisonOp.StartsWith, JsonString("Test"));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(agentName: "TestAgent");

        predicate.Invoke(evt).Should().BeTrue();
    }

    [Test]
    public async Task Compile_FieldCondition_EndsWith_ReturnsTrueWhenSuffixMatches()
    {
        var condition = Condition.NewField("agentName", ComparisonOp.EndsWith, JsonString("Agent"));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(agentName: "TestAgent");

        predicate.Invoke(evt).Should().BeTrue();
    }

    [Test]
    public async Task Compile_FieldCondition_Regex_ReturnsTrueWhenPatternMatches()
    {
        var condition = Condition.NewField("agentName", ComparisonOp.Regex, JsonValue("\"^Test\\\\w+$\""));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(agentName: "TestAgent");

        predicate.Invoke(evt).Should().BeTrue();
    }

    [Test]
    public async Task Compile_FieldCondition_Gt_ReturnsTrueWhenAbove()
    {
        var condition = Condition.NewField("latencyMs", ComparisonOp.Gt, JsonNumber(100.0));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(latencyMs: 150.0);

        predicate.Invoke(evt).Should().BeTrue();
    }

    [Test]
    public async Task Compile_FieldCondition_Lt_ReturnsTrueWhenBelow()
    {
        var condition = Condition.NewField("latencyMs", ComparisonOp.Lt, JsonNumber(100.0));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(latencyMs: 50.0);

        predicate.Invoke(evt).Should().BeTrue();
    }

    [Test]
    public async Task Compile_FieldCondition_Gte_ReturnsTrueWhenEqual()
    {
        var condition = Condition.NewField("latencyMs", ComparisonOp.Gte, JsonNumber(100.0));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(latencyMs: 100.0);

        predicate.Invoke(evt).Should().BeTrue();
    }

    [Test]
    public async Task Compile_FieldCondition_Lte_ReturnsTrueWhenEqual()
    {
        var condition = Condition.NewField("latencyMs", ComparisonOp.Lte, JsonNumber(100.0));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(latencyMs: 100.0);

        predicate.Invoke(evt).Should().BeTrue();
    }

    // --- None field (field not present on event) ---

    [Test]
    public async Task Compile_FieldCondition_NoneField_ReturnsFalse()
    {
        var condition = Condition.NewField("toolName", ComparisonOp.Eq, JsonString("search"));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(toolName: null);

        predicate.Invoke(evt).Should().BeFalse();
    }

    [Test]
    public async Task Compile_Threshold_NoneField_ReturnsFalse()
    {
        var condition = Condition.NewThreshold("latencyMs", 100.0, true);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(latencyMs: null);

        predicate.Invoke(evt).Should().BeFalse();
    }

    [Test]
    public async Task Compile_InList_NoneField_ReturnsNegatedValue()
    {
        var listId = Guid.NewGuid();
        var condition = Condition.NewInList("toolName", listId, true);
        var resolver = ListResolverFor(listId, "search");
        var predicate = Compiler.compile(resolver, condition);
        var evt = CreateEvent(toolName: null);

        // When field is None and negated=true, returns true
        predicate.Invoke(evt).Should().BeTrue();
    }

    // --- Exists ---

    [Test]
    public async Task Compile_Exists_FieldPresent_ReturnsTrue()
    {
        var condition = Condition.NewExists("toolName");
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(toolName: "search");

        predicate.Invoke(evt).Should().BeTrue();
    }

    [Test]
    public async Task Compile_Exists_FieldAbsent_ReturnsFalse()
    {
        var condition = Condition.NewExists("toolName");
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(toolName: null);

        predicate.Invoke(evt).Should().BeFalse();
    }

    // --- AnyOf ---

    [Test]
    public async Task Compile_AnyOf_MatchingValue_ReturnsTrue()
    {
        var values = ListModule.OfSeq(new[] { JsonString("tool_invocation"), JsonString("llm_call") });
        var condition = Condition.NewAnyOf("eventType", values);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(eventType: "tool_invocation");

        predicate.Invoke(evt).Should().BeTrue();
    }

    [Test]
    public async Task Compile_AnyOf_NoMatchingValue_ReturnsFalse()
    {
        var values = ListModule.OfSeq(new[] { JsonString("llm_call"), JsonString("agent_start") });
        var condition = Condition.NewAnyOf("eventType", values);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(eventType: "tool_invocation");

        predicate.Invoke(evt).Should().BeFalse();
    }

    [Test]
    public async Task Compile_AnyOf_NoneField_ReturnsFalse()
    {
        var values = ListModule.OfSeq(new[] { JsonString("search") });
        var condition = Condition.NewAnyOf("toolName", values);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(toolName: null);

        predicate.Invoke(evt).Should().BeFalse();
    }

    // --- Or no match ---

    [Test]
    public async Task Compile_OrCondition_NoneMatch_ReturnsFalse()
    {
        var conditions = ListModule.OfSeq(new[]
        {
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("llm_call")),
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("agent_start"))
        });
        var condition = Condition.NewOr(conditions);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(eventType: "tool_invocation");

        predicate.Invoke(evt).Should().BeFalse();
    }

    [Test]
    public async Task Compile_FieldCondition_Gt_NonNumericValue_ReturnsFalse()
    {
        var condition = Condition.NewField("toolName", ComparisonOp.Gt, JsonNumber(100));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(toolName: "not-a-number");

        predicate.Invoke(evt).Should().BeFalse();
    }

    [Test]
    public async Task Compile_Threshold_NonNumericValue_ReturnsFalse()
    {
        var condition = Condition.NewThreshold("toolName", 100.0, true);
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(toolName: "not-a-number");

        predicate.Invoke(evt).Should().BeFalse();
    }

    [Test]
    public async Task Compile_FieldCondition_Gt_ValidNumeric_StillWorks()
    {
        var condition = Condition.NewField("latencyMs", ComparisonOp.Gt, JsonNumber(100));
        var predicate = Compiler.compile(EmptyListResolver(), condition);
        var evt = CreateEvent(latencyMs: 200.0);

        predicate.Invoke(evt).Should().BeTrue();
    }
}
