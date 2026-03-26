using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public class DashboardService(SiemDbContext db) : IDashboardService
{
    public async Task<IReadOnlyList<TopAgentResult>> GetTopAgentsAsync(
        int hours, int limit, CancellationToken ct)
    {
        if (hours < 1) hours = 1;
        if (limit < 1) limit = 1;
        if (limit > 100) limit = 100;

        var cutoff = DateTime.UtcNow.AddHours(-hours);

        var results = await db.AgentActivityHourly
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

        return results.Select(r => new TopAgentResult(
            r.AgentId, r.AgentName, r.TotalEvents, r.TotalTokens, r.MaxLatencyMs)).ToList();
    }

    public async Task<IReadOnlyList<EventVolumeResult>> GetEventVolumeAsync(
        int hours, CancellationToken ct)
    {
        if (hours < 1) hours = 1;

        var cutoff = DateTime.UtcNow.AddHours(-hours);

        var results = await db.AgentActivityHourly
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

        return results.Select(r => new EventVolumeResult(
            r.Bucket, r.EventCount, r.TotalTokens)).ToList();
    }

    public async Task<IReadOnlyList<AlertDistributionResult>> GetAlertDistributionAsync(
        int hours, CancellationToken ct)
    {
        if (hours < 1) hours = 1;

        var cutoff = DateTime.UtcNow.AddHours(-hours);

        var results = await db.Alerts
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

        return results.Select(r => new AlertDistributionResult(
            r.Severity, r.Status, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<ToolUsageResult>> GetToolUsageAsync(
        int hours, int limit, CancellationToken ct)
    {
        if (hours < 1) hours = 1;
        if (limit < 1) limit = 1;
        if (limit > 100) limit = 100;

        var cutoff = DateTime.UtcNow.AddHours(-hours);

        var results = await db.ToolUsageHourly
            .Where(t => t.Bucket >= cutoff)
            .GroupBy(t => t.ToolName)
            .Select(g => new
            {
                ToolName = g.Key,
                InvocationCount = g.Sum(t => t.InvocationCount),
                AvgLatencyMs = g.Average(t => t.AvgLatencyMs) ?? 0.0,
                UniqueSessions = g.Sum(t => t.UniqueSessions)
            })
            .OrderByDescending(t => t.InvocationCount)
            .Take(limit)
            .ToListAsync(ct);

        return results.Select(r => new ToolUsageResult(
            r.ToolName, r.InvocationCount, r.AvgLatencyMs, r.UniqueSessions)).ToList();
    }
}
