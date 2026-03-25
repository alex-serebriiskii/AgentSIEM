using System.Text.Json;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Api.Kafka;
using Siem.Rules.Core;

namespace Siem.Api.Normalization;

/// <summary>
/// Maps framework-specific events (OpenTelemetry, LangChain, custom SDKs)
/// to the canonical F# <see cref="AgentEvent"/> type.
/// </summary>
public class AgentEventNormalizer : IEventNormalizer
{
    private readonly ILogger<AgentEventNormalizer> _logger;

    public AgentEventNormalizer(ILogger<AgentEventNormalizer> logger)
    {
        _logger = logger;
    }

    public AgentEvent Normalize(RawAgentEvent raw)
    {
        // Collect extra/framework-specific properties into the map
        var properties = new Dictionary<string, JsonElement>();

        if (raw.Extra != null)
        {
            foreach (var kvp in raw.Extra)
            {
                properties[kvp.Key] = kvp.Value;
            }
        }

        // Build the F# AgentEvent record.
        // F# records compile to classes with a constructor taking all fields in order.
        return new AgentEvent(
            eventId:      raw.EventId != Guid.Empty ? raw.EventId : Guid.NewGuid(),
            timestamp:    raw.Timestamp != default ? raw.Timestamp : DateTime.UtcNow,
            sessionId:    raw.SessionId ?? "",
            traceId:      raw.TraceId ?? "",
            agentId:      raw.AgentId ?? "unknown",
            agentName:    raw.AgentName ?? "unknown",
            eventType:    NormalizeEventType(raw.EventType ?? "unknown"),
            modelId:      ToFSharpOption(raw.ModelId),
            inputTokens:  ToFSharpOptionInt(raw.InputTokens),
            outputTokens: ToFSharpOptionInt(raw.OutputTokens),
            latencyMs:    ToFSharpOptionDouble(raw.LatencyMs),
            toolName:     ToFSharpOption(raw.ToolName),
            toolInput:    ToFSharpOption(raw.ToolInput),
            toolOutput:   ToFSharpOption(raw.ToolOutput),
            contentHash:  ToFSharpOption(raw.ContentHash),
            properties:   MapModule.OfSeq(
                              properties.Select(kvp =>
                                  new Tuple<string, JsonElement>(kvp.Key, kvp.Value)))
        );
    }

    /// <summary>
    /// Normalizes event type strings from different frameworks to canonical types.
    /// OpenTelemetry: "llm.call", "tool.invoke"
    /// LangChain: "on_llm_start", "on_tool_start"
    /// Custom: anything goes — normalize to our canonical set.
    /// </summary>
    private static string NormalizeEventType(string rawType)
    {
        return rawType.ToLowerInvariant() switch
        {
            // OpenTelemetry semantic conventions
            "llm.call" or "gen_ai.chat" or "llm.completion"
                => "llm_call",
            "tool.invoke" or "tool.call" or "tool.execute"
                => "tool_invocation",
            "retrieval" or "rag.query" or "rag.retrieval"
                => "rag_retrieval",

            // LangChain callbacks
            "on_llm_start" or "on_llm_end"
                => "llm_call",
            "on_tool_start" or "on_tool_end"
                => "tool_invocation",
            "on_retriever_start" or "on_retriever_end"
                => "rag_retrieval",
            "on_chain_start" or "on_chain_end"
                => "agent_decision",

            // Pass through anything we don't recognize
            var other => other
        };
    }

    private static FSharpOption<string> ToFSharpOption(string? value) =>
        value != null
            ? FSharpOption<string>.Some(value)
            : FSharpOption<string>.None;

    private static FSharpOption<int> ToFSharpOptionInt(int? value) =>
        value.HasValue
            ? FSharpOption<int>.Some(value.Value)
            : FSharpOption<int>.None;

    private static FSharpOption<double> ToFSharpOptionDouble(double? value) =>
        value.HasValue
            ? FSharpOption<double>.Some(value.Value)
            : FSharpOption<double>.None;
}
