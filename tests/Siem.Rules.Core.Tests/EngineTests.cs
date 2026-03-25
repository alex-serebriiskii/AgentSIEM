using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using NSubstitute;
using Siem.Rules.Core;
using static Siem.Rules.Core.Tests.TestHelpers;

namespace Siem.Rules.Core.Tests;

public class EngineTests
{
    [Test]
    public async Task EvaluateEvent_MatchingRule_ReturnsTriggeredResult()
    {
        // Arrange
        var stateProvider = Substitute.For<Evaluator.IStateProvider>();

        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));
        var ruleDef = CreateRuleDefinition(condition, severity: Severity.High);
        var compiledRules = Engine.compileAllRules(EmptyListResolver(),
            ListModule.OfSeq(new[] { ruleDef }));

        var engine = new Engine.RuleEngine(
            compiledRules: compiledRules,
            state: stateProvider);

        var evt = CreateEvent(eventType: "tool_invocation");

        // Act
        var results = await FSharpAsync.StartAsTask(
            Engine.evaluateEvent(engine, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        // Assert
        var resultList = results.ToList();
        resultList.Should().HaveCount(1);
        resultList[0].Triggered.Should().BeTrue();
        resultList[0].Severity.Should().Be(Severity.High);
    }

    [Test]
    public async Task EvaluateEvent_NoMatchingRule_ReturnsEmptyList()
    {
        var stateProvider = Substitute.For<Evaluator.IStateProvider>();

        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("llm_call"));
        var ruleDef = CreateRuleDefinition(condition);
        var compiledRules = Engine.compileAllRules(EmptyListResolver(),
            ListModule.OfSeq(new[] { ruleDef }));

        var engine = new Engine.RuleEngine(
            compiledRules: compiledRules,
            state: stateProvider);

        var evt = CreateEvent(eventType: "tool_invocation");

        var results = await FSharpAsync.StartAsTask(
            Engine.evaluateEvent(engine, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        var resultList = results.ToList();
        resultList.Should().BeEmpty();
    }

    [Test]
    public async Task EvaluateEvent_MultipleRules_ReturnsOnlyTriggered()
    {
        var stateProvider = Substitute.For<Evaluator.IStateProvider>();

        var matchingRule = CreateRuleDefinition(
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation")),
            severity: Severity.High);
        var nonMatchingRule = CreateRuleDefinition(
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("llm_call")),
            severity: Severity.Low);

        var compiledRules = Engine.compileAllRules(EmptyListResolver(),
            ListModule.OfSeq(new[] { matchingRule, nonMatchingRule }));

        var engine = new Engine.RuleEngine(
            compiledRules: compiledRules,
            state: stateProvider);

        var evt = CreateEvent(eventType: "tool_invocation");

        var results = await FSharpAsync.StartAsTask(
            Engine.evaluateEvent(engine, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        var resultList = results.ToList();
        resultList.Should().HaveCount(1);
        resultList[0].Severity.Should().Be(Severity.High);
    }

    [Test]
    public async Task CompileAllRules_DisabledRule_IsExcluded()
    {
        var enabledRule = CreateRuleDefinition(
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation")),
            enabled: true);
        var disabledRule = CreateRuleDefinition(
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("llm_call")),
            enabled: false);

        var compiledRules = Engine.compileAllRules(EmptyListResolver(),
            ListModule.OfSeq(new[] { enabledRule, disabledRule }));

        compiledRules.Length.Should().Be(1);
    }
}
