using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Rules.Core;

namespace Siem.Api.Alerting;

/// <summary>
/// Scoped service that writes alert and alert-event entities to the database,
/// then calls the update_session_alerts stored procedure.
/// </summary>
public class AlertPersistence
{
    private readonly SiemDbContext _db;
    private readonly ILogger<AlertPersistence> _logger;

    public AlertPersistence(SiemDbContext db, ILogger<AlertPersistence> logger)
    {
        _db = db;
        _logger = logger;
    }

    public virtual async Task<Guid> SaveAsync(
        EnrichedAlert alert,
        AgentEvent evt,
        CancellationToken ct = default)
    {
        var alertEntity = new AlertEntity
        {
            AlertId = Guid.NewGuid(),
            RuleId = alert.RuleId,
            RuleName = alert.RuleName,
            Severity = alert.Severity,
            Status = "open",
            Title = alert.Title,
            Detail = alert.Detail,
            AgentId = alert.AgentId,
            SessionId = alert.SessionId,
            Context = JsonSerializer.Serialize(alert.RuleContext),
            Labels = JsonSerializer.Serialize(alert.Labels),
            TriggeredAt = alert.TriggeredAt
        };

        _db.Alerts.Add(alertEntity);

        // Record the triggering event in the junction table
        _db.AlertEvents.Add(new AlertEventEntity
        {
            AlertId = alertEntity.AlertId,
            EventId = evt.EventId,
            EventTimestamp = evt.Timestamp,
            SequenceOrder = null // set for sequence rules
        });

        await _db.SaveChangesAsync(ct);

        // Update the session's alert tracking via stored procedure
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT update_session_alerts({evt.SessionId}, {alert.Severity})",
            ct);

        _logger.LogDebug("Alert persisted: {AlertId} for rule {RuleId}",
            alertEntity.AlertId, alert.RuleId);

        return alertEntity.AlertId;
    }
}
