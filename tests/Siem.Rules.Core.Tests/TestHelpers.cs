using System.Text.Json;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Rules.Core;

namespace Siem.Rules.Core.Tests;

/// <summary>
/// Helper methods for building F# types in C# test code.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Build an AgentEvent with sensible defaults. Override any field via named parameters.
    /// </summary>
    public static AgentEvent CreateEvent(
        Guid? eventId = null,
        DateTime? timestamp = null,
        string sessionId = "sess-001",
        string traceId = "trace-001",
        string agentId = "agent-001",
        string agentName = "TestAgent",
        string eventType = "tool_invocation",
        string? modelId = null,
        int? inputTokens = null,
        int? outputTokens = null,
        double? latencyMs = null,
        string? toolName = null,
        string? toolInput = null,
        string? toolOutput = null,
        string? contentHash = null,
        Dictionary<string, JsonElement>? properties = null)
    {
        var props = properties != null
            ? MapModule.OfSeq(
                properties.Select(kvp =>
                    new Tuple<string, JsonElement>(kvp.Key, kvp.Value)))
            : MapModule.Empty<string, JsonElement>();

        return new AgentEvent(
            eventId: eventId ?? Guid.NewGuid(),
            timestamp: timestamp ?? DateTime.UtcNow,
            sessionId: sessionId,
            traceId: traceId,
            agentId: agentId,
            agentName: agentName,
            eventType: eventType,
            modelId: ToOption(modelId),
            inputTokens: ToOptionInt(inputTokens),
            outputTokens: ToOptionInt(outputTokens),
            latencyMs: ToOptionDouble(latencyMs),
            toolName: ToOption(toolName),
            toolInput: ToOption(toolInput),
            toolOutput: ToOption(toolOutput),
            contentHash: ToOption(contentHash),
            properties: props);
    }

    public static FSharpOption<string> ToOption(string? value) =>
        value != null
            ? FSharpOption<string>.Some(value)
            : FSharpOption<string>.None;

    public static FSharpOption<int> ToOptionInt(int? value) =>
        value.HasValue
            ? FSharpOption<int>.Some(value.Value)
            : FSharpOption<int>.None;

    public static FSharpOption<double> ToOptionDouble(double? value) =>
        value.HasValue
            ? FSharpOption<double>.Some(value.Value)
            : FSharpOption<double>.None;

    /// <summary>
    /// Create a JsonElement from a raw JSON string.
    /// </summary>
    public static JsonElement JsonValue(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>
    /// Create a JsonElement representing a JSON string value.
    /// </summary>
    public static JsonElement JsonString(string value)
    {
        return JsonValue($"\"{value}\"");
    }

    /// <summary>
    /// Create a JsonElement representing a JSON number.
    /// </summary>
    public static JsonElement JsonNumber(double value)
    {
        return JsonValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Create a no-op list resolver that returns an empty set for any list ID.
    /// </summary>
    public static FSharpFunc<Guid, FSharpSet<string>> EmptyListResolver()
    {
        return FuncConvert.FromFunc<Guid, FSharpSet<string>>(_ => SetModule.Empty<string>());
    }

    /// <summary>
    /// Create a list resolver that returns the given members for a specific list ID.
    /// </summary>
    public static FSharpFunc<Guid, FSharpSet<string>> ListResolverFor(Guid listId, params string[] members)
    {
        var set = SetModule.OfSeq(members);
        return FuncConvert.FromFunc<Guid, FSharpSet<string>>(id =>
            id == listId ? set : SetModule.Empty<string>());
    }

    /// <summary>
    /// Build a simple RuleDefinition for testing.
    /// </summary>
    public static RuleDefinition CreateRuleDefinition(
        Condition condition,
        EvaluationType? evaluationType = null,
        Severity? severity = null,
        bool enabled = true,
        Guid? id = null)
    {
        return new RuleDefinition(
            id: id ?? Guid.NewGuid(),
            name: "Test Rule",
            description: "A rule for testing",
            enabled: enabled,
            severity: severity ?? Severity.Medium,
            condition: condition,
            evaluationType: evaluationType ?? EvaluationType.SingleEvent,
            actions: FSharpList<RuleAction>.Empty,
            tags: FSharpList<string>.Empty,
            createdBy: "test",
            createdAt: DateTime.UtcNow,
            updatedAt: DateTime.UtcNow);
    }
}
