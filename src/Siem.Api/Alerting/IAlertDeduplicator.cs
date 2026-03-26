namespace Siem.Api.Alerting;

public interface IAlertDeduplicator
{
    Task<bool> IsDuplicateAsync(string fingerprint, CancellationToken ct = default);
}
