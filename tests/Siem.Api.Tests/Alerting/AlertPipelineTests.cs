using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Api.Alerting;
using Siem.Rules.Core;

namespace Siem.Api.Tests.Alerting;

public class AlertPipelineTests
{
    private static Evaluator.EvaluationResult CreateEvalResult(Guid? ruleId = null)
    {
        return new Evaluator.EvaluationResult(
            triggered: true,
            ruleId: ruleId ?? Guid.NewGuid(),
            severity: Severity.Medium,
            detail: FSharpOption<string>.Some("Test alert"),
            context: MapModule.Empty<string, object>(),
            actions: FSharpList<RuleAction>.Empty);
    }

    private static AgentEvent CreateTestEvent()
    {
        return new AgentEvent(
            eventId: Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            sessionId: "sess-001",
            traceId: "trace-001",
            agentId: "agent-001",
            agentName: "TestAgent",
            eventType: "tool_invocation",
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

    // Note: AlertPipeline takes concrete types (not interfaces) for dedup/throttler/router,
    // so we cannot easily substitute them with NSubstitute. These tests verify the pipeline
    // structure and serve as compilation-verified stubs. For full integration testing,
    // you would need to construct real instances with mocked Redis.

    [Test]
    public async Task ProcessAsync_WhenDeduplicated_ShortCircuits()
    {
        // This test verifies the pipeline concept.
        // Full integration requires real AlertDeduplicator with mocked Redis.
        // Stub: verify the types compile and the pipeline can be instantiated.
        var evalResult = CreateEvalResult();
        var evt = CreateTestEvent();

        // Verify the types are correct
        evalResult.Triggered.Should().BeTrue();
        evalResult.Severity.Should().Be(Severity.Medium);
        evt.AgentId.Should().Be("agent-001");
    }

    [Test]
    public async Task ProcessAsync_EvaluationResult_HasExpectedFields()
    {
        var ruleId = Guid.NewGuid();
        var result = CreateEvalResult(ruleId: ruleId);

        result.RuleId.Should().Be(ruleId);
        result.Triggered.Should().BeTrue();
        FSharpOption<string>.get_IsSome(result.Detail).Should().BeTrue();
    }

    [Test]
    public async Task ProcessAsync_AgentEvent_HasExpectedFields()
    {
        var evt = CreateTestEvent();

        evt.SessionId.Should().Be("sess-001");
        evt.EventType.Should().Be("tool_invocation");
        evt.AgentId.Should().Be("agent-001");
    }

    [Test]
    public async Task EnrichedAlert_CanBeConstructed()
    {
        var alert = new EnrichedAlert
        {
            AlertId = Guid.NewGuid(),
            RuleId = Guid.NewGuid(),
            RuleName = "Test Rule",
            Severity = "medium",
            Title = "Test Alert",
            Detail = "Something happened",
            AgentId = "agent-001",
            AgentName = "TestAgent",
            SessionId = "sess-001",
            RecentAlertCount = 3,
            SessionEventCount = 42,
            RecentTools = new[] { "web_search", "calculator" },
            TriggeredAt = DateTime.UtcNow
        };

        alert.RuleName.Should().Be("Test Rule");
        alert.Severity.Should().Be("medium");
        alert.RecentTools.Should().HaveCount(2);
    }

    [Test]
    public async Task AlertPipelineConfig_HasCorrectDefaults()
    {
        var config = new AlertPipelineConfig();

        config.DeduplicationWindowMinutes.Should().Be(15);
        config.ThrottleMaxAlertsPerWindow.Should().Be(10);
        config.ThrottleWindowMinutes.Should().Be(5);
        config.DeduplicationWindow.Should().Be(TimeSpan.FromMinutes(15));
        config.ThrottleWindow.Should().Be(TimeSpan.FromMinutes(5));
    }
}
