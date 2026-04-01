using System.Collections.Concurrent;
using Siem.Api.Alerting;
using Siem.Api.Data.Enums;
using Siem.Api.Notifications;

namespace Siem.LoadTests.Helpers;

/// <summary>
/// Records received alerts for verification in load tests.
/// Configurable simulated network latency.
/// </summary>
public class InMemoryNotificationChannel : INotificationChannel
{
    public string Name { get; }
    public Severity MinimumSeverity { get; }
    public TimeSpan Latency { get; }
    public ConcurrentBag<Guid> ReceivedAlertIds { get; } = [];

    public InMemoryNotificationChannel(string name, Severity minimumSeverity, TimeSpan? latency = null)
    {
        Name = name;
        MinimumSeverity = minimumSeverity;
        Latency = latency ?? TimeSpan.FromMilliseconds(5);
    }

    public async Task SendAsync(EnrichedAlert alert, CancellationToken ct = default)
    {
        if (Latency > TimeSpan.Zero)
            await Task.Delay(Latency, ct);
        ReceivedAlertIds.Add(alert.AlertId);
    }
}

/// <summary>
/// Throws HttpRequestException for the first <see cref="FailCount"/> calls,
/// then succeeds. Tracks attempt and success counts via Interlocked.
/// </summary>
public class FailingNotificationChannel : INotificationChannel
{
    private int _attemptCount;
    private int _successCount;

    public string Name { get; }
    public Severity MinimumSeverity { get; }
    public int FailCount { get; }

    public int AttemptCount => _attemptCount;
    public int SuccessCount => _successCount;

    public FailingNotificationChannel(string name, Severity minimumSeverity, int failCount)
    {
        Name = name;
        MinimumSeverity = minimumSeverity;
        FailCount = failCount;
    }

    public Task SendAsync(EnrichedAlert alert, CancellationToken ct = default)
    {
        var attempt = Interlocked.Increment(ref _attemptCount);
        if (attempt <= FailCount)
            throw new HttpRequestException($"Simulated failure #{attempt}");

        Interlocked.Increment(ref _successCount);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Always throws on SendAsync. Used to test bounded channel overflow behavior.
/// </summary>
public class AlwaysFailingNotificationChannel : INotificationChannel
{
    public string Name { get; }
    public Severity MinimumSeverity { get; }

    public AlwaysFailingNotificationChannel(string name, Severity minimumSeverity)
    {
        Name = name;
        MinimumSeverity = minimumSeverity;
    }

    public Task SendAsync(EnrichedAlert alert, CancellationToken ct = default)
    {
        throw new HttpRequestException("Simulated permanent failure");
    }
}
