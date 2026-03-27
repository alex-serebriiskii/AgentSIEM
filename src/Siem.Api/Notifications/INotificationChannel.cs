using Siem.Api.Alerting;
using Siem.Api.Data.Enums;

namespace Siem.Api.Notifications;

public interface INotificationChannel
{
    string Name { get; }
    Severity MinimumSeverity { get; }
    Task SendAsync(EnrichedAlert alert, CancellationToken ct = default);
}
