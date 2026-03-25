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
}
