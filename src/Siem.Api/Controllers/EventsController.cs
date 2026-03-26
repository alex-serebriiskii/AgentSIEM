using Microsoft.AspNetCore.Mvc;
using Siem.Api.Services;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController(IEventService eventService) : ControllerBase
{
    /// <summary>
    /// Search events with time range, agent, type, session, tool, and JSONB property filters.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> SearchEvents(
        [FromQuery] DateTimeOffset? start = null,
        [FromQuery] DateTimeOffset? end = null,
        [FromQuery] string? agent_id = null,
        [FromQuery] string? event_type = null,
        [FromQuery] string? session_id = null,
        [FromQuery] string? tool_name = null,
        [FromQuery] string? properties = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var result = await eventService.SearchAsync(
            start, end, agent_id, event_type, session_id, tool_name, properties,
            page, pageSize, ct);

        return Ok(result);
    }
}
