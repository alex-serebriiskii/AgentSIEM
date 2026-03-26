using Microsoft.AspNetCore.Mvc;
using Siem.Api.Services;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    /// <summary>
    /// Top agents by event count over the given time range.
    /// Queries the agent_activity_hourly continuous aggregate.
    /// </summary>
    [HttpGet("top-agents")]
    public async Task<IActionResult> GetTopAgents(
        [FromQuery] int hours = 24,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var results = await dashboardService.GetTopAgentsAsync(hours, limit, ct);
        return Ok(results);
    }

    /// <summary>
    /// Event volume trend over time, bucketed by hour.
    /// Queries the agent_activity_hourly continuous aggregate.
    /// </summary>
    [HttpGet("event-volume")]
    public async Task<IActionResult> GetEventVolume(
        [FromQuery] int hours = 24,
        CancellationToken ct = default)
    {
        var results = await dashboardService.GetEventVolumeAsync(hours, ct);
        return Ok(results);
    }

    /// <summary>
    /// Alert distribution by severity and status.
    /// Queries the alerts table directly.
    /// </summary>
    [HttpGet("alert-distribution")]
    public async Task<IActionResult> GetAlertDistribution(
        [FromQuery] int hours = 24,
        CancellationToken ct = default)
    {
        var results = await dashboardService.GetAlertDistributionAsync(hours, ct);
        return Ok(results);
    }

    /// <summary>
    /// Top tools by invocation count over the given time range.
    /// Queries the tool_usage_hourly continuous aggregate.
    /// </summary>
    [HttpGet("tool-usage")]
    public async Task<IActionResult> GetToolUsage(
        [FromQuery] int hours = 24,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var results = await dashboardService.GetToolUsageAsync(hours, limit, ct);
        return Ok(results);
    }
}
