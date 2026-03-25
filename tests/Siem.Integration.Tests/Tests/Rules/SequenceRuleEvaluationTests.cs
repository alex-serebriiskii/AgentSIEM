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
/// Integration tests for sequence rule evaluation with real Redis state.
/// Verifies ordered step matching, session isolation, TTL expiration,
/// and multi-step sequences using the RedisStateProvider.
/// </summary>
[NotInParallel("database")]
public class SequenceRuleEvaluationTests
{
    private RedisStateProvider _stateProvider = null!;

    [Before(Test)]
    public async Task Setup()
    {
        await DbHelper.FlushRedisAsync();
        _stateProvider = new RedisStateProvider(IntegrationTestFixture.RedisMultiplexer);
    }

    [Test]
    public async Task SequenceRule_FiresWhenAllStepsMatchInOrder()
    {
        var rule = CreateTwoStepSequenceRule();
        var sessionId = "session-ordered";

        // Step 1: rag_retrieval
        var evt1 = CreateEvent(eventType: "rag_retrieval", sessionId: sessionId);
        var result1 = await Evaluate(rule, evt1);
        result1.Triggered.Should().BeFalse("step 1 alone should not complete the sequence");

        // Step 2: external_api_call
        var evt2 = CreateEvent(eventType: "external_api_call", sessionId: sessionId);
        var result2 = await Evaluate(rule, evt2);
        result2.Triggered.Should().BeTrue("both steps matched in order, sequence should complete");
        result2.Detail.Value.Should().Contain("Sequence complete");
    }

    [Test]
    public async Task SequenceRule_DoesNotFire_WhenStepsAreOutOfOrder()
    {
        var rule = CreateTwoStepSequenceRule();
        var sessionId = "session-outoforder";

        // Send step 2 first — should not advance anything since step 0 expects rag_retrieval
        var evt1 = CreateEvent(eventType: "external_api_call", sessionId: sessionId);
        var result1 = await Evaluate(rule, evt1);
        result1.Triggered.Should().BeFalse();

        // Send step 1 — should advance to step 1 (since we're at step 0)
        var evt2 = CreateEvent(eventType: "rag_retrieval", sessionId: sessionId);
        var result2 = await Evaluate(rule, evt2);
        result2.Triggered.Should().BeFalse();

        // Send step 2 again — now it should match step 1 and complete
        var evt3 = CreateEvent(eventType: "external_api_call", sessionId: sessionId);
        var result3 = await Evaluate(rule, evt3);
        result3.Triggered.Should().BeTrue("after step 1 matched, step 2 should complete the sequence");
    }

    [Test]
    public async Task SequenceRule_DoesNotFire_OnPartialMatch()
    {
        var rule = CreateTwoStepSequenceRule();
        var sessionId = "session-partial";

        // Only send step 1
        var evt1 = CreateEvent(eventType: "rag_retrieval", sessionId: sessionId);
        var result1 = await Evaluate(rule, evt1);
        result1.Triggered.Should().BeFalse();

        // Send unrelated events — should not trigger or advance
        for (var i = 0; i < 5; i++)
        {
            var evt = CreateEvent(eventType: "llm_call", sessionId: sessionId);
            var result = await Evaluate(rule, evt);
            result.Triggered.Should().BeFalse();
        }
    }

    [Test]
    public async Task SequenceRule_SessionIsolation_CrossSessionStepsDontCombine()
    {
        var rule = CreateTwoStepSequenceRule();

        // Session-A: step 1
        var evt1 = CreateEvent(eventType: "rag_retrieval", sessionId: "session-A");
        var result1 = await Evaluate(rule, evt1);
        result1.Triggered.Should().BeFalse();

        // Session-B: step 2 — should NOT complete because step 1 was in session-A
        var evt2 = CreateEvent(eventType: "external_api_call", sessionId: "session-B");
        var result2 = await Evaluate(rule, evt2);
        result2.Triggered.Should().BeFalse(
            "step 2 in session-B should not combine with step 1 from session-A");
    }

    [Test]
    public async Task SequenceRule_TTLExpiration_ResetsProgress()
    {
        // Use a very short maxSpan so progress expires quickly
        var rule = CreateTwoStepSequenceRule(maxSpanSeconds: 1);
        var sessionId = "session-ttl";

        // Step 1
        var evt1 = CreateEvent(eventType: "rag_retrieval", sessionId: sessionId);
        var result1 = await Evaluate(rule, evt1);
        result1.Triggered.Should().BeFalse();

        // Wait for the sequence TTL to expire
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        // Step 2 — progress should have expired, so this is evaluated against step 0
        var evt2 = CreateEvent(eventType: "external_api_call", sessionId: sessionId);
        var result2 = await Evaluate(rule, evt2);
        result2.Triggered.Should().BeFalse(
            "step 1 progress expired, so step 2 event is checked against step 0 (rag_retrieval) and doesn't match");
    }

    [Test]
    public async Task SequenceRule_ThreeStepSequence_FiresOnlyAfterAllSteps()
    {
        var rule = CreateThreeStepSequenceRule();
        var sessionId = "session-3step";

        // Step 1: rag_retrieval
        var evt1 = CreateEvent(eventType: "rag_retrieval", sessionId: sessionId);
        var result1 = await Evaluate(rule, evt1);
        result1.Triggered.Should().BeFalse();

        // Step 2: external_api_call
        var evt2 = CreateEvent(eventType: "external_api_call", sessionId: sessionId);
        var result2 = await Evaluate(rule, evt2);
        result2.Triggered.Should().BeFalse("only 2 of 3 steps complete");

        // Step 3: data_export
        var evt3 = CreateEvent(eventType: "data_export", sessionId: sessionId);
        var result3 = await Evaluate(rule, evt3);
        result3.Triggered.Should().BeTrue("all 3 steps matched in order");
        result3.Detail.Value.Should().Contain("3 steps");
    }

    [Test]
    public async Task SequenceRule_CompletionClearsProgress_AllowsRefire()
    {
        var rule = CreateTwoStepSequenceRule();
        var sessionId = "session-refire";

        // Complete the sequence once
        var evt1 = CreateEvent(eventType: "rag_retrieval", sessionId: sessionId);
        await Evaluate(rule, evt1);
        var evt2 = CreateEvent(eventType: "external_api_call", sessionId: sessionId);
        var result1 = await Evaluate(rule, evt2);
        result1.Triggered.Should().BeTrue();

        // The sequence progress should be cleared — start fresh
        var evt3 = CreateEvent(eventType: "rag_retrieval", sessionId: sessionId);
        var result3 = await Evaluate(rule, evt3);
        result3.Triggered.Should().BeFalse("new sequence started, only step 1 matched");

        var evt4 = CreateEvent(eventType: "external_api_call", sessionId: sessionId);
        var result4 = await Evaluate(rule, evt4);
        result4.Triggered.Should().BeTrue("second sequence completed successfully");
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

    private static Compiler.CompiledRule CompileSequenceRule(
        Guid ruleId,
        Condition broadCondition,
        SequenceConfig seqConfig)
    {
        var listResolver = FuncConvert.FromFunc<Guid, FSharpSet<string>>(
            _ => SetModule.Empty<string>());

        var ruleDef = new RuleDefinition(
            id: ruleId,
            name: "Test Sequence Rule",
            description: "Integration test",
            enabled: true,
            severity: Severity.Critical,
            condition: broadCondition,
            evaluationType: EvaluationType.NewSequence(seqConfig),
            actions: FSharpList<RuleAction>.Empty,
            tags: FSharpList<string>.Empty,
            createdBy: "test",
            createdAt: DateTime.UtcNow,
            updatedAt: DateTime.UtcNow);

        return Compiler.compileRule(listResolver, ruleDef);
    }

    private static Compiler.CompiledRule CreateTwoStepSequenceRule(double maxSpanSeconds = 300)
    {
        var step1 = new SequenceStep("rag_retrieval",
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("rag_retrieval")));
        var step2 = new SequenceStep("external_api_call",
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("external_api_call")));

        var seqConfig = new SequenceConfig(
            maxSpan: TimeSpan.FromSeconds(maxSpanSeconds),
            steps: ListModule.OfSeq(new[] { step1, step2 }));

        // Broad condition that matches all events (exists eventType)
        var broadCondition = Condition.NewExists("eventType");

        return CompileSequenceRule(Guid.NewGuid(), broadCondition, seqConfig);
    }

    private static Compiler.CompiledRule CreateThreeStepSequenceRule()
    {
        var step1 = new SequenceStep("rag_retrieval",
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("rag_retrieval")));
        var step2 = new SequenceStep("external_api_call",
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("external_api_call")));
        var step3 = new SequenceStep("data_export",
            Condition.NewField("eventType", ComparisonOp.Eq, JsonString("data_export")));

        var seqConfig = new SequenceConfig(
            maxSpan: TimeSpan.FromMinutes(10),
            steps: ListModule.OfSeq(new[] { step1, step2, step3 }));

        var broadCondition = Condition.NewExists("eventType");

        return CompileSequenceRule(Guid.NewGuid(), broadCondition, seqConfig);
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
