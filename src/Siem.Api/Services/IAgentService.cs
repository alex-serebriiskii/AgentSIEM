using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public interface IAgentService
{
    Task<ServiceResult<AgentRiskSummaryResponse>> GetRiskSummaryAsync(
        string id, string lookback, CancellationToken ct);
}
