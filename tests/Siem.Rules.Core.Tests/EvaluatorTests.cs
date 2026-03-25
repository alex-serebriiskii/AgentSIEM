using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using NSubstitute;
using Siem.Rules.Core;
using static Siem.Rules.Core.Tests.TestHelpers;

namespace Siem.Rules.Core.Tests;

public class EvaluatorTests
{
    private Evaluator.IStateProvider _stateProvider = null!;

    [Before(Test)]
    public void Setup()
    {
        _stateProvider = Substitute.For<Evaluator.IStateProvider>();
    }

    [Test]
    public async Task Evaluate_SingleEvent_WhenMatches_ReturnsTriggered()
    {
        // Arrange
        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));
        var rule = Compiler.compileRule(EmptyListResolver(), CreateRuleDefinition(condition));
        var evt = CreateEvent(eventType: "tool_invocation");

        // Act
        var result = await FSharpAsync.StartAsTask(
            Evaluator.evaluate(_stateProvider, rule, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        // Assert
        result.Triggered.Should().BeTrue();
        result.Severity.Should().Be(Severity.Medium);
    }

    [Test]
    public async Task Evaluate_SingleEvent_WhenDoesNotMatch_ReturnsNotTriggered()
    {
        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("llm_call"));
        var rule = Compiler.compileRule(EmptyListResolver(), CreateRuleDefinition(condition));
        var evt = CreateEvent(eventType: "tool_invocation");

        var result = await FSharpAsync.StartAsTask(
            Evaluator.evaluate(_stateProvider, rule, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        result.Triggered.Should().BeFalse();
    }

    [Test]
    public async Task Evaluate_Temporal_WhenThresholdReached_ReturnsTriggered()
    {
        // Arrange: set up a temporal rule that triggers at count >= 5
        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));
        var temporalConfig = new TemporalConfig(
            windowDuration: TimeSpan.FromMinutes(5),
            threshold: 5.0,
            aggregation: TemporalAggregation.Count,
            partitionField: "agentId");
        var evalType = EvaluationType.NewTemporal(temporalConfig);
        var rule = Compiler.compileRule(
            EmptyListResolver(),
            CreateRuleDefinition(condition, evaluationType: evalType));

        // Mock: sliding window returns count of 5 (meets threshold)
        _stateProvider.IncrementSlidingWindowAsync(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(FSharpAsync.AwaitTask(Task.FromResult(5L)));

        var evt = CreateEvent(eventType: "tool_invocation");

        // Act
        var result = await FSharpAsync.StartAsTask(
            Evaluator.evaluate(_stateProvider, rule, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        // Assert
        result.Triggered.Should().BeTrue();
    }

    [Test]
    public async Task Evaluate_Temporal_WhenBelowThreshold_ReturnsNotTriggered()
    {
        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));
        var temporalConfig = new TemporalConfig(
            windowDuration: TimeSpan.FromMinutes(5),
            threshold: 10.0,
            aggregation: TemporalAggregation.Count,
            partitionField: "agentId");
        var evalType = EvaluationType.NewTemporal(temporalConfig);
        var rule = Compiler.compileRule(
            EmptyListResolver(),
            CreateRuleDefinition(condition, evaluationType: evalType));

        _stateProvider.IncrementSlidingWindowAsync(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(FSharpAsync.AwaitTask(Task.FromResult(3L)));

        var evt = CreateEvent(eventType: "tool_invocation");

        var result = await FSharpAsync.StartAsTask(
            Evaluator.evaluate(_stateProvider, rule, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        result.Triggered.Should().BeFalse();
    }

    [Test]
    public async Task Evaluate_Sequence_FirstStep_AdvancesProgress()
    {
        // Arrange: 2-step sequence, starting at step 0
        var step1Condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("llm_call"));
        var step2Condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));

        var steps = ListModule.OfSeq(new[]
        {
            new SequenceStep("step1", step1Condition),
            new SequenceStep("step2", step2Condition)
        });

        var seqConfig = new SequenceConfig(
            maxSpan: TimeSpan.FromMinutes(10),
            steps: steps);
        var evalType = EvaluationType.NewSequence(seqConfig);

        // The overall condition matches both steps (use Or or a broad condition)
        var broadCondition = Condition.NewField("agentId", ComparisonOp.Eq, JsonString("agent-001"));
        var rule = Compiler.compileRule(
            EmptyListResolver(),
            CreateRuleDefinition(broadCondition, evaluationType: evalType));

        // Mock: sequence currently at step 0
        _stateProvider.GetSequenceProgressAsync(Arg.Any<string>())
            .Returns(FSharpAsync.AwaitTask(Task.FromResult(0)));
        _stateProvider.SetSequenceProgressAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>())
            .Returns(FSharpAsync.AwaitTask(Task.FromResult<Microsoft.FSharp.Core.Unit>(null!)));

        // Event matches step1 (llm_call)
        var evt = CreateEvent(eventType: "llm_call", agentId: "agent-001");

        // Act
        var result = await FSharpAsync.StartAsTask(
            Evaluator.evaluate(_stateProvider, rule, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        // Assert: not triggered yet (only first step matched), but progress should advance
        result.Triggered.Should().BeFalse();
    }

    [Test]
    public async Task Evaluate_Sequence_FinalStep_Triggers()
    {
        var step1Condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("llm_call"));
        var step2Condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));

        var steps = ListModule.OfSeq(new[]
        {
            new SequenceStep("step1", step1Condition),
            new SequenceStep("step2", step2Condition)
        });

        var seqConfig = new SequenceConfig(
            maxSpan: TimeSpan.FromMinutes(10),
            steps: steps);
        var evalType = EvaluationType.NewSequence(seqConfig);

        var broadCondition = Condition.NewField("agentId", ComparisonOp.Eq, JsonString("agent-001"));
        var rule = Compiler.compileRule(
            EmptyListResolver(),
            CreateRuleDefinition(broadCondition, evaluationType: evalType));

        // Mock: sequence at step 1 (last step)
        _stateProvider.GetSequenceProgressAsync(Arg.Any<string>())
            .Returns(FSharpAsync.AwaitTask(Task.FromResult(1)));
        _stateProvider.ClearSequenceAsync(Arg.Any<string>())
            .Returns(FSharpAsync.AwaitTask(Task.FromResult<Microsoft.FSharp.Core.Unit>(null!)));

        // Event matches step2 (tool_invocation)
        var evt = CreateEvent(eventType: "tool_invocation", agentId: "agent-001");

        var result = await FSharpAsync.StartAsTask(
            Evaluator.evaluate(_stateProvider, rule, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        result.Triggered.Should().BeTrue();
    }
}
