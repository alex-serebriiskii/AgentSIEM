using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public interface ISessionService
{
    Task<ServiceResult<IReadOnlyList<SessionResponse>>> ListAsync(
        string? agentId, bool? hasAlerts, CancellationToken ct);

    Task<ServiceResult<SessionResponse>> GetAsync(string id, CancellationToken ct);

    Task<ServiceResult<SessionTimelineResponse>> GetTimelineAsync(
        string id, int limit, CancellationToken ct);
}
