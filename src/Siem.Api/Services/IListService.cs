using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public interface IListService
{
    Task<ServiceResult<IReadOnlyList<ManagedListSummaryResponse>>> ListAllAsync(CancellationToken ct);
    Task<ServiceResult<ManagedListDetailResponse>> GetAsync(Guid id, CancellationToken ct);
    Task<ServiceResult<ManagedListSummaryResponse>> CreateAsync(CreateListRequest request, CancellationToken ct);
    Task<ServiceResult<object>> UpdateMembersAsync(Guid id, UpdateListMembersRequest request, CancellationToken ct);
}
