using Microsoft.AspNetCore.Mvc;
using Siem.Api.Models.Requests;
using Siem.Api.Services;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController(IAlertService alertService) : ControllerBase
{
    /// <summary>
    /// List alerts with optional filters for status, severity, and agent_id.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> ListAlerts(
        [FromQuery] string? status,
        [FromQuery] string? severity,
        [FromQuery] string? agent_id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await alertService.ListAsync(status, severity, agent_id, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get a single alert with its associated events.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetAlert(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var result = await alertService.GetAsync(id, ct);
        if (result.IsNotFound) return NotFound();

        return Ok(result.Value);
    }

    /// <summary>
    /// Acknowledge an alert (set status to "acknowledged").
    /// </summary>
    [HttpPut("{id:guid}/acknowledge")]
    public async Task<IActionResult> AcknowledgeAlert(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var result = await alertService.AcknowledgeAsync(id, ct);
        if (result.IsNotFound) return NotFound();
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
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
        var result = await alertService.ResolveAsync(id, request, ct);
        if (result.IsNotFound) return NotFound();

        return Ok(result.Value);
    }
}
