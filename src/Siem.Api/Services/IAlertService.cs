using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public interface IAlertService
{
    Task<PaginatedResult<AlertResponse>> ListAsync(
        string? status, string? severity, string? agentId,
        int page, int pageSize, CancellationToken ct);

    Task<ServiceResult<AlertResponse>> GetAsync(Guid id, CancellationToken ct);
    Task<ServiceResult<AlertResponse>> AcknowledgeAsync(Guid id, CancellationToken ct);
    Task<ServiceResult<AlertResponse>> ResolveAsync(Guid id, ResolveAlertRequest request, CancellationToken ct);
}
