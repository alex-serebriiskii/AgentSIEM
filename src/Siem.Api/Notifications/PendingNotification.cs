using Siem.Api.Alerting;

namespace Siem.Api.Notifications;

public record PendingNotification(
    INotificationChannel Channel,
    EnrichedAlert Alert,
    int AttemptCount = 0,
    DateTime? NextAttemptAt = null
);
