using Siem.Api.Alerting;

namespace Siem.Api.Notifications;

/// <summary>
/// Routes alerts to notification channels based on severity ordering.
/// Channels whose MinimumSeverity is at or below the alert's severity
/// are dispatched in parallel. Failed sends are queued to NotificationRetryWorker.
/// </summary>
public class NotificationRouter
{
    private readonly IReadOnlyList<INotificationChannel> _channels;
    private readonly NotificationRetryWorker _retryWorker;
    private readonly ILogger<NotificationRouter> _logger;

    private static readonly Dictionary<string, int> SeverityOrder = new()
    {
        ["low"] = 0,
        ["medium"] = 1,
        ["high"] = 2,
        ["critical"] = 3
    };

    public NotificationRouter(
        IEnumerable<INotificationChannel> channels,
        NotificationRetryWorker retryWorker,
        ILogger<NotificationRouter> logger)
    {
        _channels = channels.ToList();
        _retryWorker = retryWorker;
        _logger = logger;
    }

    public async Task RouteAsync(EnrichedAlert alert, CancellationToken ct = default)
    {
        var alertSeverity = SeverityOrder.GetValueOrDefault(alert.Severity, 0);

        var matchingChannels = _channels
            .Where(ch => alertSeverity >= SeverityOrder.GetValueOrDefault(
                ch.MinimumSeverity, 0))
            .ToList();

        _logger.LogDebug(
            "Routing alert={AlertId} severity={Severity} to {ChannelCount} channels",
            alert.AlertId, alert.Severity, matchingChannels.Count);

        var tasks = matchingChannels
            .Select(ch => SendWithRetryAsync(ch, alert, ct));

        await Task.WhenAll(tasks);
    }

    private async Task SendWithRetryAsync(
        INotificationChannel channel,
        EnrichedAlert alert,
        CancellationToken ct)
    {
        try
        {
            await channel.SendAsync(alert, ct);

            _logger.LogDebug(
                "Notification sent: channel={Channel} alert={AlertId}",
                channel.Name, alert.AlertId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Notification failed for channel={Channel} alert={AlertId}. Queuing retry.",
                channel.Name, alert.AlertId);

            _retryWorker.EnqueueRetry(new PendingNotification(
                Channel: channel,
                Alert: alert,
                AttemptCount: 1,
                NextAttemptAt: DateTime.UtcNow.AddSeconds(30)));
        }
    }
}
