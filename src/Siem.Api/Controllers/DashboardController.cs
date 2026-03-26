using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly SiemDbContext _db;

    public DashboardController(SiemDbContext db)
    {
        _db = db;
    }

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
        if (hours < 1) hours = 1;
        if (limit < 1) limit = 1;
        if (limit > 100) limit = 100;

        var cutoff = DateTime.UtcNow.AddHours(-hours);

        var results = await _db.AgentActivityHourly
            .Where(a => a.Bucket >= cutoff)
            .GroupBy(a => new { a.AgentId, a.AgentName })
            .Select(g => new
            {
                g.Key.AgentId,
                g.Key.AgentName,
                TotalEvents = g.Sum(a => a.EventCount),
                TotalTokens = g.Sum(a => a.TotalTokens ?? 0),
                MaxLatencyMs = g.Max(a => a.MaxLatencyMs ?? 0)
            })
            .OrderByDescending(a => a.TotalEvents)
            .Take(limit)
            .ToListAsync(ct);

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
        if (hours < 1) hours = 1;

        var cutoff = DateTime.UtcNow.AddHours(-hours);

        var results = await _db.AgentActivityHourly
            .Where(a => a.Bucket >= cutoff)
            .GroupBy(a => a.Bucket)
            .Select(g => new
            {
                Bucket = g.Key,
                EventCount = g.Sum(a => a.EventCount),
                TotalTokens = g.Sum(a => a.TotalTokens ?? 0)
            })
            .OrderBy(a => a.Bucket)
            .ToListAsync(ct);

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
        if (hours < 1) hours = 1;

        var cutoff = DateTime.UtcNow.AddHours(-hours);

        var results = await _db.Alerts
            .Where(a => a.TriggeredAt >= cutoff)
            .GroupBy(a => new { a.Severity, a.Status })
            .Select(g => new
            {
                g.Key.Severity,
                g.Key.Status,
                Count = g.Count()
            })
            .OrderByDescending(a => a.Count)
            .ToListAsync(ct);

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
        if (hours < 1) hours = 1;
        if (limit < 1) limit = 1;
        if (limit > 100) limit = 100;

        var cutoff = DateTime.UtcNow.AddHours(-hours);

        var results = await _db.ToolUsageHourly
            .Where(t => t.Bucket >= cutoff)
            .GroupBy(t => t.ToolName)
            .Select(g => new
            {
                ToolName = g.Key,
                InvocationCount = g.Sum(t => t.InvocationCount),
                AvgLatencyMs = g.Average(t => t.AvgLatencyMs),
                UniqueSessions = g.Sum(t => t.UniqueSessions)
            })
            .OrderByDescending(t => t.InvocationCount)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(results);
    }
}
