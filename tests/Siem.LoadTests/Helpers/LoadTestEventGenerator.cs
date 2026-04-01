using System.Text;
using System.Text.Json;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Rules.Core;

namespace Siem.LoadTests.Helpers;

/// <summary>
/// Generates diverse, realistic agent event streams for load testing.
/// Uses seeded Random for reproducible distributions across test runs.
/// </summary>
public class LoadTestEventGenerator
{
    private readonly Random _random;
    private readonly string[] _agentIds;
    private readonly Dictionary<string, string[]> _agentSessions;

    private static readonly string[] EventTypes =
        ["tool_invocation", "llm_call", "rag_retrieval", "external_api_call", "agent_decision", "memory_access", "permission_check"];

    private static readonly double[] EventTypeWeights =
        [0.35, 0.25, 0.15, 0.10, 0.05, 0.05, 0.05];

    private static readonly string[] ToolNames =
        ["web_search", "file_read", "code_execute", "shell_run", "database_query", "send_email", "api_call", "calculator"];

    private static readonly string[] ModelIds =
        ["gpt-4", "claude-3-opus", "claude-3-sonnet", "gpt-4-turbo"];

    private static readonly double[] CumulativeWeights;

    static LoadTestEventGenerator()
    {
        CumulativeWeights = new double[EventTypeWeights.Length];
        double sum = 0;
        for (int i = 0; i < EventTypeWeights.Length; i++)
        {
            sum += EventTypeWeights[i];
            CumulativeWeights[i] = sum;
        }
    }

    public LoadTestEventGenerator(int agentCount = 50, int sessionsPerAgent = 3, int seed = 42)
    {
        _random = new Random(seed);
        _agentIds = Enumerable.Range(0, agentCount).Select(i => $"agent-{i:D4}").ToArray();
        _agentSessions = new Dictionary<string, string[]>();

        foreach (var agentId in _agentIds)
        {
            _agentSessions[agentId] = Enumerable.Range(0, sessionsPerAgent)
                .Select(j => $"{agentId}-sess-{j:D2}")
                .ToArray();
        }
    }

    /// <summary>All agent IDs in the pool.</summary>
    public IReadOnlyList<string> AgentIds => _agentIds;

    /// <summary>All (agentId, sessionId) pairs for assertions after seeding.</summary>
    public IEnumerable<(string AgentId, string SessionId)> GetAgentSessionPairs()
    {
        foreach (var (agentId, sessions) in _agentSessions)
            foreach (var sessionId in sessions)
                yield return (agentId, sessionId);
    }

    /// <summary>
    /// Generate a batch of diverse events.
    /// Timestamps are spread across the last <paramref name="timeSpreadMinutes"/> minutes.
    /// </summary>
    public List<AgentEvent> GenerateEvents(int count, int timeSpreadMinutes = 10)
    {
        var events = new List<AgentEvent>(count);
        var baseTime = DateTime.UtcNow.AddMinutes(-timeSpreadMinutes);
        var spanTicks = TimeSpan.FromMinutes(timeSpreadMinutes).Ticks;

        for (int i = 0; i < count; i++)
        {
            var agentId = _agentIds[_random.Next(_agentIds.Length)];
            var sessions = _agentSessions[agentId];
            var sessionId = sessions[_random.Next(sessions.Length)];
            var eventType = PickWeightedEventType();
            var timestamp = baseTime.AddTicks((long)(_random.NextDouble() * spanTicks));

            events.Add(CreateEvent(agentId, sessionId, eventType, timestamp));
        }

        return events;
    }

    /// <summary>
    /// Generate events for a specific agent and session (useful for targeted seeding).
    /// </summary>
    public List<AgentEvent> GenerateEventsForAgent(
        string agentId, string sessionId, int count, int timeSpreadMinutes = 10)
    {
        var events = new List<AgentEvent>(count);
        var baseTime = DateTime.UtcNow.AddMinutes(-timeSpreadMinutes);
        var spanTicks = TimeSpan.FromMinutes(timeSpreadMinutes).Ticks;

        for (int i = 0; i < count; i++)
        {
            var eventType = PickWeightedEventType();
            var timestamp = baseTime.AddTicks((long)(_random.NextDouble() * spanTicks));
            events.Add(CreateEvent(agentId, sessionId, eventType, timestamp));
        }

        return events;
    }

    /// <summary>
    /// Serialize an AgentEvent to JSON bytes matching the RawAgentEvent format for Kafka production.
    /// </summary>
    public static byte[] SerializeToKafkaPayload(AgentEvent evt)
    {
        var obj = new Dictionary<string, object?>
        {
            ["eventId"] = evt.EventId,
            ["timestamp"] = evt.Timestamp,
            ["sessionId"] = evt.SessionId,
            ["traceId"] = evt.TraceId,
            ["agentId"] = evt.AgentId,
            ["agentName"] = evt.AgentName,
            ["eventType"] = evt.EventType,
        };

        if (FSharpOption<string>.get_IsSome(evt.ModelId))
            obj["modelId"] = evt.ModelId.Value;
        if (FSharpOption<int>.get_IsSome(evt.InputTokens))
            obj["inputTokens"] = evt.InputTokens.Value;
        if (FSharpOption<int>.get_IsSome(evt.OutputTokens))
            obj["outputTokens"] = evt.OutputTokens.Value;
        if (FSharpOption<double>.get_IsSome(evt.LatencyMs))
            obj["latencyMs"] = evt.LatencyMs.Value;
        if (FSharpOption<string>.get_IsSome(evt.ToolName))
            obj["toolName"] = evt.ToolName.Value;
        if (FSharpOption<string>.get_IsSome(evt.ToolInput))
            obj["toolInput"] = evt.ToolInput.Value;
        if (FSharpOption<string>.get_IsSome(evt.ToolOutput))
            obj["toolOutput"] = evt.ToolOutput.Value;

        return JsonSerializer.SerializeToUtf8Bytes(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private AgentEvent CreateEvent(string agentId, string sessionId, string eventType, DateTime timestamp)
    {
        var agentName = $"Agent-{agentId.Split('-').Last()}";

        return eventType switch
        {
            "llm_call" => CreateLlmCallEvent(agentId, agentName, sessionId, timestamp),
            "tool_invocation" => CreateToolInvocationEvent(agentId, agentName, sessionId, timestamp),
            _ => CreateGenericEvent(agentId, agentName, sessionId, eventType, timestamp),
        };
    }

    private AgentEvent CreateLlmCallEvent(string agentId, string agentName, string sessionId, DateTime timestamp)
    {
        var modelId = ModelIds[_random.Next(ModelIds.Length)];
        var inputTokens = _random.Next(50, 2000);
        var outputTokens = _random.Next(50, 4000);
        var latencyMs = 200.0 + _random.NextDouble() * 5000.0;

        return new AgentEvent(
            eventId: Guid.NewGuid(),
            timestamp: timestamp,
            sessionId: sessionId,
            traceId: $"trace-{Guid.NewGuid():N}",
            agentId: agentId,
            agentName: agentName,
            eventType: "llm_call",
            modelId: FSharpOption<string>.Some(modelId),
            inputTokens: FSharpOption<int>.Some(inputTokens),
            outputTokens: FSharpOption<int>.Some(outputTokens),
            latencyMs: FSharpOption<double>.Some(latencyMs),
            toolName: FSharpOption<string>.None,
            toolInput: FSharpOption<string>.None,
            toolOutput: FSharpOption<string>.None,
            contentHash: FSharpOption<string>.Some($"hash-{Guid.NewGuid():N}"),
            properties: MapModule.Empty<string, JsonElement>());
    }

    private AgentEvent CreateToolInvocationEvent(string agentId, string agentName, string sessionId, DateTime timestamp)
    {
        var toolName = ToolNames[_random.Next(ToolNames.Length)];
        var latencyMs = 10.0 + _random.NextDouble() * 2000.0;

        return new AgentEvent(
            eventId: Guid.NewGuid(),
            timestamp: timestamp,
            sessionId: sessionId,
            traceId: $"trace-{Guid.NewGuid():N}",
            agentId: agentId,
            agentName: agentName,
            eventType: "tool_invocation",
            modelId: FSharpOption<string>.None,
            inputTokens: FSharpOption<int>.None,
            outputTokens: FSharpOption<int>.None,
            latencyMs: FSharpOption<double>.Some(latencyMs),
            toolName: FSharpOption<string>.Some(toolName),
            toolInput: FSharpOption<string>.Some($"{{\"query\":\"test-{_random.Next()}\"}}"),
            toolOutput: FSharpOption<string>.Some("ok"),
            contentHash: FSharpOption<string>.None,
            properties: MapModule.Empty<string, JsonElement>());
    }

    private AgentEvent CreateGenericEvent(string agentId, string agentName, string sessionId, string eventType, DateTime timestamp)
    {
        var latencyMs = 5.0 + _random.NextDouble() * 500.0;

        return new AgentEvent(
            eventId: Guid.NewGuid(),
            timestamp: timestamp,
            sessionId: sessionId,
            traceId: $"trace-{Guid.NewGuid():N}",
            agentId: agentId,
            agentName: agentName,
            eventType: eventType,
            modelId: FSharpOption<string>.None,
            inputTokens: FSharpOption<int>.None,
            outputTokens: FSharpOption<int>.None,
            latencyMs: FSharpOption<double>.Some(latencyMs),
            toolName: FSharpOption<string>.None,
            toolInput: FSharpOption<string>.None,
            toolOutput: FSharpOption<string>.None,
            contentHash: FSharpOption<string>.None,
            properties: MapModule.Empty<string, JsonElement>());
    }

    private string PickWeightedEventType()
    {
        var roll = _random.NextDouble();
        for (int i = 0; i < CumulativeWeights.Length; i++)
        {
            if (roll <= CumulativeWeights[i])
                return EventTypes[i];
        }
        return EventTypes[^1];
    }
}
