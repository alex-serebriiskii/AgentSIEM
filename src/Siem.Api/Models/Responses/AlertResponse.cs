namespace Siem.Api.Models.Responses;

using System.Text.Json;
using Siem.Api.Data.Entities;
using Siem.Api.Data.Enums;

public class AlertResponse
{
    public Guid AlertId { get; set; }
    public Guid RuleId { get; set; }
    public string RuleName { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Status { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public JsonElement Context { get; set; }
    public string AgentId { get; set; } = "";
    public string? SessionId { get; set; }
    public DateTime TriggeredAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? AssignedTo { get; set; }
    public string? ResolutionNote { get; set; }
    public JsonElement Labels { get; set; }
    public bool Suppressed { get; set; }
    public Guid? SuppressedBy { get; set; }
    public DateTime? SuppressionExpiresAt { get; set; }
    public List<AlertEventResponse>? AlertEvents { get; set; }

    public static AlertResponse FromEntity(AlertEntity entity, bool includeEvents = false)
    {
        var response = new AlertResponse
        {
            AlertId = entity.AlertId,
            RuleId = entity.RuleId,
            RuleName = entity.RuleName,
            Severity = entity.Severity.ToStorageString(),
            Status = entity.Status.ToStorageString(),
            Title = entity.Title,
            Detail = entity.Detail,
            Context = JsonDocument.Parse(entity.Context).RootElement,
            AgentId = entity.AgentId,
            SessionId = entity.SessionId,
            TriggeredAt = entity.TriggeredAt,
            AcknowledgedAt = entity.AcknowledgedAt,
            ResolvedAt = entity.ResolvedAt,
            AssignedTo = entity.AssignedTo,
            ResolutionNote = entity.ResolutionNote,
            Labels = JsonDocument.Parse(entity.Labels).RootElement,
            Suppressed = entity.Suppressed,
            SuppressedBy = entity.SuppressedBy,
            SuppressionExpiresAt = entity.SuppressionExpiresAt
        };

        if (includeEvents && entity.AlertEvents.Count > 0)
        {
            response.AlertEvents = entity.AlertEvents
                .Select(ae => new AlertEventResponse
                {
                    EventId = ae.EventId,
                    EventTimestamp = ae.EventTimestamp,
                    SequenceOrder = ae.SequenceOrder
                })
                .OrderBy(ae => ae.SequenceOrder ?? 0)
                .ThenBy(ae => ae.EventTimestamp)
                .ToList();
        }

        return response;
    }
}

public class AlertEventResponse
{
    public Guid EventId { get; set; }
    public DateTime EventTimestamp { get; set; }
    public short? SequenceOrder { get; set; }
}
