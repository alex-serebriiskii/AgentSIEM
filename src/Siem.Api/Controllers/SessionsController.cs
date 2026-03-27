using Microsoft.AspNetCore.Mvc;
using Siem.Api.Models.Requests;
using Siem.Api.Services;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController(ISessionService sessionService) : ControllerBase
{
    /// <summary>
    /// List sessions with optional filters for agent_id and has_alerts.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> ListSessions(
        [FromQuery] string? agent_id,
        [FromQuery] bool? has_alerts,
        CancellationToken ct)
    {
        var result = await sessionService.ListAsync(agent_id, has_alerts, ct);
        return Ok(result.Value);
    }

    /// <summary>
    /// Get a single session by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetSession(
        [FromRoute] string id, CancellationToken ct)
    {
        var result = await sessionService.GetAsync(id, ct);
        if (result.IsNotFound) return NotFound();

        return Ok(result.Value);
    }

    /// <summary>
    /// Get the timeline of events for a session.
    /// Uses the get_session_timeline database function for optimized retrieval.
    /// Returns events in chronological order with alert annotations.
    /// </summary>
    [HttpGet("{id}/timeline")]
    public async Task<IActionResult> GetSessionTimeline(
        [FromRoute] string id,
        [FromQuery] SessionTimelineQuery query,
        CancellationToken ct = default)
    {
        var result = await sessionService.GetTimelineAsync(id, query.Limit, ct);
        if (result.IsNotFound) return NotFound();

        return Ok(result.Value);
    }
}
