using Microsoft.EntityFrameworkCore;

namespace Siem.Api.Data.Entities;

[Keyless]
public class AgentEventReadModel
{
    public Guid EventId { get; set; }
    public DateTime Timestamp { get; set; }
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string TraceId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string? SeverityHint { get; set; }
    public string? ModelId { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public double? LatencyMs { get; set; }
    public string? ToolName { get; set; }
    public string? ToolInput { get; set; }
    public string? ToolOutput { get; set; }
    public string? ContentHash { get; set; }
    public string Properties { get; set; } = "{}";
    public DateTime IngestedAt { get; set; }
    public short? KafkaPartition { get; set; }
    public long? KafkaOffset { get; set; }
    public string? SourceSdk { get; set; }
}
