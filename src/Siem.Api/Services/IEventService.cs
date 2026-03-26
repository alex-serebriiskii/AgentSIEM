using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public interface IEventService
{
    Task<PaginatedResult<EventResponse>> SearchAsync(
        DateTimeOffset? start,
        DateTimeOffset? end,
        string? agentId,
        string? eventType,
        string? sessionId,
        string? toolName,
        string? properties,
        int page,
        int pageSize,
        CancellationToken ct);
}
