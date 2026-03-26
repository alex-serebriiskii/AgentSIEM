using System.Threading.Channels;

namespace Siem.Api.Notifications;

/// <summary>
/// Background worker that retries failed notification deliveries with
/// exponential backoff (30s, 2min, 10min). Max 3 attempts per notification.
/// Uses System.Threading.Channels for an async producer/consumer queue.
/// </summary>
public class NotificationRetryWorker : BackgroundService, INotificationRetryWorker
{
    private readonly Channel<PendingNotification> _channel;
    private readonly ILogger<NotificationRetryWorker> _logger;
    private readonly int _maxAttempts;
    private readonly TimeSpan[] _backoffIntervals;

    public NotificationRetryWorker(
        ILogger<NotificationRetryWorker> logger,
        NotificationRetryConfig config)
    {
        _channel = System.Threading.Channels.Channel.CreateBounded<PendingNotification>(
            new BoundedChannelOptions(config.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });
        _logger = logger;
        _maxAttempts = config.MaxAttempts;
        _backoffIntervals = config.BackoffIntervalsSeconds
            .Select(s => TimeSpan.FromSeconds(s))
            .ToArray();
    }

    /// <summary>
    /// Enqueues a failed notification for retry. Called by NotificationRouter.
    /// </summary>
    public void EnqueueRetry(PendingNotification notification)
    {
        if (!_channel.Writer.TryWrite(notification))
        {
            _logger.LogWarning(
                "Retry queue full, dropping notification for channel={Channel} alert={AlertId}",
                notification.Channel.Name, notification.Alert.AlertId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationRetryWorker started");

        await foreach (var pending in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                // Wait until the scheduled retry time
                if (pending.NextAttemptAt.HasValue)
                {
                    var delay = pending.NextAttemptAt.Value - DateTime.UtcNow;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, stoppingToken);
                    }
                }

                await pending.Channel.SendAsync(pending.Alert, stoppingToken);

                _logger.LogInformation(
                    "Retry succeeded: channel={Channel} alert={AlertId} attempt={Attempt}",
                    pending.Channel.Name, pending.Alert.AlertId, pending.AttemptCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var nextAttempt = pending.AttemptCount + 1;

                if (nextAttempt >= _maxAttempts)
                {
                    _logger.LogWarning(
                        "Notification permanently failed after {Attempts} attempts: " +
                        "channel={Channel} alert={AlertId} error={Error}",
                        nextAttempt, pending.Channel.Name,
                        pending.Alert.AlertId, ex.Message);
                    continue;
                }

                // Exponential backoff
                var backoff = _backoffIntervals[
                    Math.Min(nextAttempt - 1, _backoffIntervals.Length - 1)];

                var retry = pending with
                {
                    AttemptCount = nextAttempt,
                    NextAttemptAt = DateTime.UtcNow + backoff
                };

                _logger.LogDebug(
                    "Retry queued: channel={Channel} alert={AlertId} " +
                    "attempt={Attempt} nextAttempt={NextAttempt}",
                    pending.Channel.Name, pending.Alert.AlertId,
                    nextAttempt, retry.NextAttemptAt);

                EnqueueRetry(retry);
            }
        }

        _logger.LogInformation("NotificationRetryWorker stopped");
    }
}
