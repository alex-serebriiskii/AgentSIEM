using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public interface IDashboardService
{
    Task<IReadOnlyList<TopAgentResult>> GetTopAgentsAsync(int hours, int limit, CancellationToken ct);
    Task<IReadOnlyList<EventVolumeResult>> GetEventVolumeAsync(int hours, CancellationToken ct);
    Task<IReadOnlyList<AlertDistributionResult>> GetAlertDistributionAsync(int hours, CancellationToken ct);
    Task<IReadOnlyList<ToolUsageResult>> GetToolUsageAsync(int hours, int limit, CancellationToken ct);
}
