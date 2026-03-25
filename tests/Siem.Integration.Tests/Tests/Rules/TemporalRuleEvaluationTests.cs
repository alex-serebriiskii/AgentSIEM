using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Siem.Api.Services;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;
using Siem.Rules.Core;

namespace Siem.Integration.Tests.Tests.Rules;

/// <summary>
/// Integration tests for temporal rule evaluation with real Redis state.
/// Verifies sliding window counts, rate calculations, partition isolation,
/// and window expiration using the RedisStateProvider.
/// </summary>
[NotInParallel("database")]
public class TemporalRuleEvaluationTests
{
    private RedisStateProvider _stateProvider = null!;

    [Before(Test)]
    public async Task Setup()
    {
        await DbHelper.FlushRedisAsync();
        _stateProvider = new RedisStateProvider(IntegrationTestFixture.RedisMultiplexer);
    }

    [Test]
    public async Task TemporalRule_FiresOnNthEvent_WhenThresholdReached()
    {
        // Arrange: threshold of 5 with a 5-minute window
        var ruleId = Guid.NewGuid();
        var temporalConfig = new TemporalConfig(
            windowDuration: TimeSpan.FromMinutes(5),
            threshold: 5.0,
            aggregation: TemporalAggregation.Count,
            partitionField: "agentId");

        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));
        var rule = CompileRule(ruleId, condition, EvaluationType.NewTemporal(temporalConfig));

        // Act: send events 1 through 6
        var results = new List<Evaluator.EvaluationResult>();
        for (var i = 0; i < 6; i++)
        {
            var evt = CreateEvent(eventType: "tool_invocation", agentId: "agent-temporal");
            var result = await Evaluate(rule, evt);
            results.Add(result);
        }

        // Assert: first 4 should not trigger, 5th and beyond should trigger
        results.Take(4).Should().AllSatisfy(r => r.Triggered.Should().BeFalse());
        results[4].Triggered.Should().BeTrue();
        results[5].Triggered.Should().BeTrue();
    }

    [Test]
    public async Task TemporalRule_DoesNotFire_WhenConditionDoesNotMatch()
    {
        var ruleId = Guid.NewGuid();
        var temporalConfig = new TemporalConfig(
            windowDuration: TimeSpan.FromMinutes(5),
            threshold: 2.0,
            aggregation: TemporalAggregation.Count,
            partitionField: "agentId");

        // Rule looks for "tool_invocation" events
        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));
        var rule = CompileRule(ruleId, condition, EvaluationType.NewTemporal(temporalConfig));

        // Send 10 events that don't match the condition
        var results = new List<Evaluator.EvaluationResult>();
        for (var i = 0; i < 10; i++)
        {
            var evt = CreateEvent(eventType: "llm_call", agentId: "agent-no-match");
            var result = await Evaluate(rule, evt);
            results.Add(result);
        }

        // None should trigger — condition doesn't match, so window is never incremented
        results.Should().AllSatisfy(r => r.Triggered.Should().BeFalse());
    }

    [Test]
    public async Task TemporalRule_WindowExpiration_ResetsCount()
    {
        // Use a very short window so we can test expiration
        var ruleId = Guid.NewGuid();
        var temporalConfig = new TemporalConfig(
            windowDuration: TimeSpan.FromSeconds(1),
            threshold: 3.0,
            aggregation: TemporalAggregation.Count,
            partitionField: "agentId");

        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));
        var rule = CompileRule(ruleId, condition, EvaluationType.NewTemporal(temporalConfig));

        // Send 2 events (below threshold)
        for (var i = 0; i < 2; i++)
        {
            var evt = CreateEvent(eventType: "tool_invocation", agentId: "agent-expire");
            await Evaluate(rule, evt);
        }

        // Wait for the window to expire
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        // Send 2 more events — the old ones should have expired, so count restarts
        var results = new List<Evaluator.EvaluationResult>();
        for (var i = 0; i < 2; i++)
        {
            var evt = CreateEvent(eventType: "tool_invocation", agentId: "agent-expire");
            var result = await Evaluate(rule, evt);
            results.Add(result);
        }

        // Count should be 1 and 2 (not 3 and 4), so neither hits threshold of 3
        results.Should().AllSatisfy(r => r.Triggered.Should().BeFalse());
    }

    [Test]
    public async Task TemporalRule_PartitionIsolation_IndependentCountsPerAgent()
    {
        var ruleId = Guid.NewGuid();
        var temporalConfig = new TemporalConfig(
            windowDuration: TimeSpan.FromMinutes(5),
            threshold: 3.0,
            aggregation: TemporalAggregation.Count,
            partitionField: "agentId");

        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));
        var rule = CompileRule(ruleId, condition, EvaluationType.NewTemporal(temporalConfig));

        // Agent-A: send 4 events (should trigger on 3rd)
        var agentAResults = new List<Evaluator.EvaluationResult>();
        for (var i = 0; i < 4; i++)
        {
            var evt = CreateEvent(eventType: "tool_invocation", agentId: "agent-A");
            agentAResults.Add(await Evaluate(rule, evt));
        }

        // Agent-B: send 2 events (should never trigger — below threshold)
        var agentBResults = new List<Evaluator.EvaluationResult>();
        for (var i = 0; i < 2; i++)
        {
            var evt = CreateEvent(eventType: "tool_invocation", agentId: "agent-B");
            agentBResults.Add(await Evaluate(rule, evt));
        }

        // Agent-A should trigger on 3rd event
        agentAResults.Take(2).Should().AllSatisfy(r => r.Triggered.Should().BeFalse());
        agentAResults[2].Triggered.Should().BeTrue();
        agentAResults[3].Triggered.Should().BeTrue();

        // Agent-B never hits threshold — isolated count
        agentBResults.Should().AllSatisfy(r => r.Triggered.Should().BeFalse());
    }

    [Test]
    public async Task TemporalRule_RateAggregation_CalculatesEventsPerMinute()
    {
        var ruleId = Guid.NewGuid();
        // Rate threshold: 120 events per minute (= 2 per second)
        // Window of 1 minute
        var temporalConfig = new TemporalConfig(
            windowDuration: TimeSpan.FromMinutes(1),
            threshold: 120.0,
            aggregation: TemporalAggregation.Rate,
            partitionField: "agentId");

        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));
        var rule = CompileRule(ruleId, condition, EvaluationType.NewTemporal(temporalConfig));

        // Send 119 events (rate = 119/1min = 119, below 120)
        Evaluator.EvaluationResult? lastBelow = null;
        for (var i = 0; i < 119; i++)
        {
            var evt = CreateEvent(eventType: "tool_invocation", agentId: "agent-rate");
            lastBelow = await Evaluate(rule, evt);
        }

        lastBelow!.Triggered.Should().BeFalse();

        // 120th event pushes rate to 120/1min = 120, meets threshold
        var triggeringEvt = CreateEvent(eventType: "tool_invocation", agentId: "agent-rate");
        var triggerResult = await Evaluate(rule, triggeringEvt);

        triggerResult.Triggered.Should().BeTrue();
    }

    [Test]
    public async Task TemporalRule_ContextIncludesWindowCount()
    {
        var ruleId = Guid.NewGuid();
        var temporalConfig = new TemporalConfig(
            windowDuration: TimeSpan.FromMinutes(5),
            threshold: 2.0,
            aggregation: TemporalAggregation.Count,
            partitionField: "agentId");

        var condition = Condition.NewField("eventType", ComparisonOp.Eq, JsonString("tool_invocation"));
        var rule = CompileRule(ruleId, condition, EvaluationType.NewTemporal(temporalConfig));

        // Send 1 event (below threshold)
        var evt1 = CreateEvent(eventType: "tool_invocation", agentId: "agent-ctx");
        var result1 = await Evaluate(rule, evt1);

        // Result should include window_count even when not triggered
        result1.Triggered.Should().BeFalse();
        result1.Context.Should().ContainKey("window_count");

        // Send 2nd event (meets threshold)
        var evt2 = CreateEvent(eventType: "tool_invocation", agentId: "agent-ctx");
        var result2 = await Evaluate(rule, evt2);

        result2.Triggered.Should().BeTrue();
        result2.Context.Should().ContainKey("window_count");
        ((long)result2.Context["window_count"]).Should().Be(2L);
    }

    #region Helpers

    private static System.Text.Json.JsonElement JsonString(string value)
    {
        return System.Text.Json.JsonDocument.Parse($"\"{value}\"").RootElement.Clone();
    }

    private static AgentEvent CreateEvent(
        string eventType = "tool_invocation",
        string agentId = "test-agent",
        string sessionId = "test-session")
    {
        return new AgentEvent(
            eventId: Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            sessionId: sessionId,
            traceId: $"trace-{Guid.NewGuid():N}",
            agentId: agentId,
            agentName: "TestAgent",
            eventType: eventType,
            modelId: FSharpOption<string>.None,
            inputTokens: FSharpOption<int>.None,
            outputTokens: FSharpOption<int>.None,
            latencyMs: FSharpOption<double>.None,
            toolName: FSharpOption<string>.None,
            toolInput: FSharpOption<string>.None,
            toolOutput: FSharpOption<string>.None,
            contentHash: FSharpOption<string>.None,
            properties: MapModule.Empty<string, System.Text.Json.JsonElement>());
    }

    private static Compiler.CompiledRule CompileRule(
        Guid ruleId, Condition condition, EvaluationType evalType)
    {
        var listResolver = FuncConvert.FromFunc<Guid, FSharpSet<string>>(
            _ => SetModule.Empty<string>());

        var ruleDef = new RuleDefinition(
            id: ruleId,
            name: "Test Temporal Rule",
            description: "Integration test",
            enabled: true,
            severity: Severity.High,
            condition: condition,
            evaluationType: evalType,
            actions: FSharpList<RuleAction>.Empty,
            tags: FSharpList<string>.Empty,
            createdBy: "test",
            createdAt: DateTime.UtcNow,
            updatedAt: DateTime.UtcNow);

        return Compiler.compileRule(listResolver, ruleDef);
    }

    private async Task<Evaluator.EvaluationResult> Evaluate(
        Compiler.CompiledRule rule, AgentEvent evt)
    {
        return await FSharpAsync.StartAsTask(
            Evaluator.evaluate(_stateProvider, rule, evt),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);
    }

    #endregion
}
