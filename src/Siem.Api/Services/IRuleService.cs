using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public interface IRuleService
{
    Task<ServiceResult<RuleResponse>> CreateAsync(CreateRuleRequest request, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<RuleResponse>>> ListAsync(bool? enabled, CancellationToken ct);
    Task<ServiceResult<RuleResponse>> GetAsync(Guid id, CancellationToken ct);
    Task<ServiceResult<RuleResponse>> UpdateAsync(Guid id, UpdateRuleRequest request, CancellationToken ct);
    Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct);
    Task<ServiceResult<object>> ActivateAsync(Guid id, CancellationToken ct);
}
