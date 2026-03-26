namespace Siem.Api.Notifications;

/// <summary>
/// Configuration for the notification retry worker.
/// </summary>
public class NotificationRetryConfig
{
    /// <summary>Maximum delivery attempts per notification.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Backoff intervals in seconds for each retry attempt.
    /// Index 0 = first retry, index 1 = second retry, etc.
    /// </summary>
    public int[] BackoffIntervalsSeconds { get; set; } = [30, 120, 600];

    /// <summary>Bounded channel capacity for pending retries.</summary>
    public int ChannelCapacity { get; set; } = 1000;
}
