using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly SiemDbContext _db;

    public AlertsController(SiemDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// List alerts with optional filters for status, severity, and agent_id.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> ListAlerts(
        [FromQuery] string? status,
        [FromQuery] string? severity,
        [FromQuery] string? agent_id,
        CancellationToken ct)
    {
        var query = _db.Alerts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        if (!string.IsNullOrWhiteSpace(severity))
            query = query.Where(a => a.Severity == severity);

        if (!string.IsNullOrWhiteSpace(agent_id))
            query = query.Where(a => a.AgentId == agent_id);

        var alerts = await query
            .OrderByDescending(a => a.TriggeredAt)
            .ToListAsync(ct);

        return Ok(alerts.Select(a => AlertResponse.FromEntity(a)));
    }

    /// <summary>
    /// Get a single alert with its associated events.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetAlert(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var alert = await _db.Alerts
            .Include(a => a.AlertEvents)
            .FirstOrDefaultAsync(a => a.AlertId == id, ct);

        if (alert == null) return NotFound();

        return Ok(AlertResponse.FromEntity(alert, includeEvents: true));
    }

    /// <summary>
    /// Acknowledge an alert (set status to "acknowledged").
    /// </summary>
    [HttpPut("{id:guid}/acknowledge")]
    public async Task<IActionResult> AcknowledgeAlert(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var alert = await _db.Alerts.FindAsync([id], ct);
        if (alert == null) return NotFound();

        if (alert.Status == "resolved")
            return BadRequest(new { error = "Cannot acknowledge a resolved alert" });

        alert.Status = "acknowledged";
        alert.AcknowledgedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(AlertResponse.FromEntity(alert));
    }

    /// <summary>
    /// Resolve an alert with an optional resolution note.
    /// </summary>
    [HttpPut("{id:guid}/resolve")]
    public async Task<IActionResult> ResolveAlert(
        [FromRoute] Guid id,
        [FromBody] ResolveAlertRequest request,
        CancellationToken ct)
    {
        var alert = await _db.Alerts.FindAsync([id], ct);
        if (alert == null) return NotFound();

        alert.Status = "resolved";
        alert.ResolvedAt = DateTime.UtcNow;
        alert.ResolutionNote = request.ResolutionNote;
        await _db.SaveChangesAsync(ct);

        return Ok(AlertResponse.FromEntity(alert));
    }
}
