using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public class EventService(SiemDbContext db) : IEventService
{
    public async Task<PaginatedResult<EventResponse>> SearchAsync(
        DateTimeOffset? start,
        DateTimeOffset? end,
        string? agentId,
        string? eventType,
        string? sessionId,
        string? toolName,
        string? properties,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 500) pageSize = 500;

        var effectiveStart = start?.UtcDateTime ?? DateTime.UtcNow.AddHours(-1);
        var effectiveEnd = end?.UtcDateTime ?? DateTime.UtcNow;

        var query = db.AgentEvents.AsQueryable();

        query = query.Where(e => e.Timestamp >= effectiveStart && e.Timestamp <= effectiveEnd);

        if (!string.IsNullOrWhiteSpace(agentId))
            query = query.Where(e => e.AgentId == agentId);

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.EventType == eventType);

        if (!string.IsNullOrWhiteSpace(sessionId))
            query = query.Where(e => e.SessionId == sessionId);

        if (!string.IsNullOrWhiteSpace(toolName))
            query = query.Where(e => e.ToolName == toolName);

        if (!string.IsNullOrWhiteSpace(properties))
        {
            query = query.Where(e =>
                EF.Functions.JsonContains(e.Properties, properties));
        }

        var totalCount = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var events = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PaginatedResult<EventResponse>(
            events.Select(EventResponse.FromEntity).ToList(),
            page,
            pageSize,
            totalCount,
            totalPages);
    }
}
