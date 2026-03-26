using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public interface ISuppressionService
{
    Task<ServiceResult<IReadOnlyList<SuppressionResponse>>> ListAsync(
        Guid? ruleId, string? agentId, CancellationToken ct);

    Task<ServiceResult<SuppressionResponse>> CreateAsync(
        CreateSuppressionRequest request, CancellationToken ct);

    Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct);
}
