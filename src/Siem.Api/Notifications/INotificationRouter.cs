using Siem.Api.Alerting;

namespace Siem.Api.Notifications;

public interface INotificationRouter
{
    Task RouteAsync(EnrichedAlert alert, CancellationToken ct = default);
}
