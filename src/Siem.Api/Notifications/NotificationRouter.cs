using System.Diagnostics.Metrics;
using Siem.Api.Alerting;
using Siem.Api.Data.Enums;

namespace Siem.Api.Notifications;

/// <summary>
/// Routes alerts to notification channels based on severity ordering.
/// Channels whose MinimumSeverity is at or below the alert's severity
/// are dispatched in parallel. Failed sends are queued to NotificationRetryWorker.
/// </summary>
public class NotificationRouter : INotificationRouter
{
    private readonly IReadOnlyList<INotificationChannel> _channels;
    private readonly INotificationRetryWorker _retryWorker;
    private readonly ILogger<NotificationRouter> _logger;

    private static readonly Meter Meter = new("Siem.Notifications");
    private static readonly Counter<long> NotificationsSent =
        Meter.CreateCounter<long>("siem.notifications.sent");
    private static readonly Counter<long> NotificationsFailed =
        Meter.CreateCounter<long>("siem.notifications.failed");

    public NotificationRouter(
        IEnumerable<INotificationChannel> channels,
        INotificationRetryWorker retryWorker,
        ILogger<NotificationRouter> logger)
    {
        _channels = channels.ToList();
        _retryWorker = retryWorker;
        _logger = logger;
    }

    public async Task RouteAsync(EnrichedAlert alert, CancellationToken ct = default)
    {
        var alertSeverity = alert.Severity.ToOrder();

        var matchingChannels = _channels
            .Where(ch => alertSeverity >= ch.MinimumSeverity.ToOrder())
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

            NotificationsSent.Add(1,
                new KeyValuePair<string, object?>("channel", channel.Name));

            _logger.LogDebug(
                "Notification sent: channel={Channel} alert={AlertId}",
                channel.Name, alert.AlertId);
        }
        catch (Exception ex)
        {
            NotificationsFailed.Add(1,
                new KeyValuePair<string, object?>("channel", channel.Name));

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
