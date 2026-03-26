namespace Siem.Api.Alerting;

public interface IAlertThrottler
{
    Task<bool> IsThrottledAsync(Guid ruleId, CancellationToken ct = default);
}
