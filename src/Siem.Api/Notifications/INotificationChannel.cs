using Siem.Api.Alerting;

namespace Siem.Api.Notifications;

public interface INotificationChannel
{
    string Name { get; }
    string MinimumSeverity { get; }
    Task SendAsync(EnrichedAlert alert, CancellationToken ct = default);
}
