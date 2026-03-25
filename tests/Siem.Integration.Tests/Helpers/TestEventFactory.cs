using System.Text.Json;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Rules.Core;

namespace Siem.Integration.Tests.Helpers;

public static class TestEventFactory
{
    public static AgentEvent CreateToolInvocation(
        Guid? eventId = null,
        string agentId = "test-agent",
        string agentName = "TestAgent",
        string sessionId = "test-session",
        string toolName = "test-tool",
        DateTime? timestamp = null)
    {
        return new AgentEvent(
            eventId: eventId ?? Guid.NewGuid(),
            timestamp: timestamp ?? DateTime.UtcNow,
            sessionId: sessionId,
            traceId: $"trace-{Guid.NewGuid():N}",
            agentId: agentId,
            agentName: agentName,
            eventType: "tool_invocation",
            modelId: FSharpOption<string>.None,
            inputTokens: FSharpOption<int>.None,
            outputTokens: FSharpOption<int>.None,
            latencyMs: FSharpOption<double>.Some(42.0),
            toolName: FSharpOption<string>.Some(toolName),
            toolInput: FSharpOption<string>.Some("{}"),
            toolOutput: FSharpOption<string>.Some("ok"),
            contentHash: FSharpOption<string>.None,
            properties: MapModule.Empty<string, JsonElement>());
    }

    public static AgentEvent CreateLlmCall(
        Guid? eventId = null,
        string agentId = "test-agent",
        string sessionId = "test-session",
        int inputTokens = 100,
        int outputTokens = 200,
        double latencyMs = 500.0)
    {
        return new AgentEvent(
            eventId: eventId ?? Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            sessionId: sessionId,
            traceId: $"trace-{Guid.NewGuid():N}",
            agentId: agentId,
            agentName: "TestAgent",
            eventType: "llm_call",
            modelId: FSharpOption<string>.Some("gpt-4"),
            inputTokens: FSharpOption<int>.Some(inputTokens),
            outputTokens: FSharpOption<int>.Some(outputTokens),
            latencyMs: FSharpOption<double>.Some(latencyMs),
            toolName: FSharpOption<string>.None,
            toolInput: FSharpOption<string>.None,
            toolOutput: FSharpOption<string>.None,
            contentHash: FSharpOption<string>.Some("abc123"),
            properties: MapModule.Empty<string, JsonElement>());
    }

    public static AgentEvent CreateFSharpAgentEvent(
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
            properties: MapModule.Empty<string, JsonElement>());
    }

    public static AgentEvent CreateWithProperties(
        string agentId = "test-agent",
        Dictionary<string, JsonElement>? properties = null)
    {
        var props = properties != null
            ? MapModule.OfSeq(properties.Select(kvp =>
                new Tuple<string, JsonElement>(kvp.Key, kvp.Value)))
            : MapModule.Empty<string, JsonElement>();

        return new AgentEvent(
            eventId: Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            sessionId: "test-session",
            traceId: $"trace-{Guid.NewGuid():N}",
            agentId: agentId,
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
            properties: props);
    }
}
