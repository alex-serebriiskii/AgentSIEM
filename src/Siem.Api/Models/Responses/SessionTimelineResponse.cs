namespace Siem.Api.Models.Responses;

public class SessionTimelineResponse
{
    public string SessionId { get; set; } = "";
    public int EventCount { get; set; }
    public List<SessionTimelineEventResponse> Events { get; set; } = [];
}

public class SessionTimelineEventResponse
{
    public Guid EventId { get; set; }
    public DateTime Timestamp { get; set; }
    public string EventType { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string? ToolName { get; set; }
    public string? ModelId { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public double? LatencyMs { get; set; }
    public Guid[] AlertIds { get; set; } = [];
    public string[] AlertSeverities { get; set; } = [];
}
