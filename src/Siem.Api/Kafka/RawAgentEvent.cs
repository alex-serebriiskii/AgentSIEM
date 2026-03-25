using System.Text.Json;

namespace Siem.Api.Kafka;

/// <summary>
/// Raw event as received from Kafka before normalization.
/// Intentionally permissive — fields are nullable because different
/// agent frameworks provide different subsets of the schema.
/// </summary>
public class RawAgentEvent
{
    public Guid EventId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? SessionId { get; set; }
    public string? TraceId { get; set; }
    public string? AgentId { get; set; }
    public string? AgentName { get; set; }
    public string? EventType { get; set; }
    public string? ModelId { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public double? LatencyMs { get; set; }
    public string? ToolName { get; set; }
    public string? ToolInput { get; set; }
    public string? ToolOutput { get; set; }
    public string? ContentHash { get; set; }

    /// <summary>
    /// Catch-all for framework-specific fields not in the canonical schema.
    /// Preserved through normalization into AgentEvent.Properties.
    /// </summary>
    public Dictionary<string, JsonElement>? Extra { get; set; }

    // Kafka metadata — populated after deserialization, not from JSON
    public int KafkaPartition { get; set; }
    public long KafkaOffset { get; set; }
    public DateTime KafkaTimestamp { get; set; }
}
