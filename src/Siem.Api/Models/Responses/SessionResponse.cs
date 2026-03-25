namespace Siem.Api.Models.Responses;

using System.Text.Json;
using Siem.Api.Data.Entities;

public class SessionResponse
{
    public string SessionId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime LastEventAt { get; set; }
    public int EventCount { get; set; }
    public bool HasAlerts { get; set; }
    public short AlertCount { get; set; }
    public string? MaxSeverity { get; set; }
    public JsonElement Metadata { get; set; }

    public static SessionResponse FromEntity(AgentSessionEntity entity)
    {
        return new SessionResponse
        {
            SessionId = entity.SessionId,
            AgentId = entity.AgentId,
            AgentName = entity.AgentName,
            StartedAt = entity.StartedAt,
            LastEventAt = entity.LastEventAt,
            EventCount = entity.EventCount,
            HasAlerts = entity.HasAlerts,
            AlertCount = entity.AlertCount,
            MaxSeverity = entity.MaxSeverity,
            Metadata = JsonDocument.Parse(entity.Metadata).RootElement
        };
    }
}
