namespace Siem.Api.Models.Responses;

using System.Text.Json;
using Siem.Api.Data.Entities;

public class EventResponse
{
    public Guid EventId { get; set; }
    public DateTime Timestamp { get; set; }
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string EventType { get; set; } = "";
    public string? SessionId { get; set; }
    public string? ToolName { get; set; }
    public string? ModelId { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public double? LatencyMs { get; set; }
    public JsonElement Properties { get; set; }
    public string? SourceSdk { get; set; }

    public static EventResponse FromEntity(AgentEventReadModel entity)
    {
        return new EventResponse
        {
            EventId = entity.EventId,
            Timestamp = entity.Timestamp,
            AgentId = entity.AgentId,
            AgentName = entity.AgentName,
            EventType = entity.EventType,
            SessionId = entity.SessionId,
            ToolName = entity.ToolName,
            ModelId = entity.ModelId,
            InputTokens = entity.InputTokens,
            OutputTokens = entity.OutputTokens,
            LatencyMs = entity.LatencyMs,
            Properties = JsonDocument.Parse(entity.Properties).RootElement,
            SourceSdk = entity.SourceSdk
        };
    }
}
