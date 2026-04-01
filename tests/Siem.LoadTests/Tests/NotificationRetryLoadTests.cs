using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Siem.Api.Alerting;
using Siem.Api.Data.Enums;
using Siem.Api.Notifications;
using Siem.LoadTests.Helpers;

namespace Siem.LoadTests.Tests;

public class NotificationRetryLoadTests
{
    [Test, Timeout(60_000)]
    public async Task RetryWorker_SustainedFailures_RetriesAndEventuallySucceeds(
        CancellationToken testCt)
    {
        const int notificationCount = 200;
        const int failCount = 2; // fails twice, succeeds on 3rd attempt

        var channel = new FailingNotificationChannel(
            "retry-test", Severity.Low, failCount: failCount);

        var config = new NotificationRetryConfig
        {
            MaxAttempts = 3,
            BackoffIntervalsSeconds = [0, 0, 0], // zero backoff for speed
            ChannelCapacity = 5000
        };

        var retryWorker = new NotificationRetryWorker(
            NullLogger<NotificationRetryWorker>.Instance, config);

        // Start the worker
        await retryWorker.StartAsync(CancellationToken.None);

        // Enqueue notifications
        for (int i = 0; i < notificationCount; i++)
        {
            var alert = CreateAlert(Guid.NewGuid());
            retryWorker.EnqueueRetry(new PendingNotification(
                Channel: channel,
                Alert: alert,
                AttemptCount: 0));
        }

        // Wait for processing to complete
        var sw = Stopwatch.StartNew();
        var timeout = LoadTestConfig.ScaleLatency(30_000);
        while (sw.ElapsedMilliseconds < timeout)
        {
            // Each notification needs failCount attempts before succeeding,
            // and the FailingNotificationChannel uses a global counter.
            // After the first failCount calls fail, all subsequent calls succeed.
            // So: first failCount notifications fail and get re-queued,
            // then all remaining succeed on first try.
            // Total successes should eventually reach notificationCount.
            if (channel.SuccessCount >= notificationCount)
                break;
            await Task.Delay(100, testCt);
        }

        await retryWorker.StopAsync(CancellationToken.None);

        channel.SuccessCount.Should().Be(notificationCount,
            $"all {notificationCount} notifications should eventually succeed; " +
            $"actual: {channel.SuccessCount}, attempts: {channel.AttemptCount}");
    }

    [Test, Timeout(30_000)]
    public async Task RetryWorker_ChannelCapacityOverflow_DropsOldestGracefully(
        CancellationToken testCt)
    {
        const int capacity = 100;
        const int notificationCount = 500; // 5x capacity

        var channel = new AlwaysFailingNotificationChannel(
            "overflow-test", Severity.Low);

        var config = new NotificationRetryConfig
        {
            MaxAttempts = 3,
            BackoffIntervalsSeconds = [0, 0, 0],
            ChannelCapacity = capacity
        };

        var retryWorker = new NotificationRetryWorker(
            NullLogger<NotificationRetryWorker>.Instance, config);

        // Start the worker
        await retryWorker.StartAsync(CancellationToken.None);

        // Rapid-fire enqueue — should not throw
        var enqueueExceptions = new List<Exception>();
        for (int i = 0; i < notificationCount; i++)
        {
            try
            {
                var alert = CreateAlert(Guid.NewGuid());
                retryWorker.EnqueueRetry(new PendingNotification(
                    Channel: channel,
                    Alert: alert,
                    AttemptCount: 0));
            }
            catch (Exception ex)
            {
                enqueueExceptions.Add(ex);
            }
        }

        // Let the worker process for a bit
        await Task.Delay(2000, testCt);

        await retryWorker.StopAsync(CancellationToken.None);

        enqueueExceptions.Should().BeEmpty(
            "EnqueueRetry should never throw, even when channel is full (DropOldest)");
    }

    private static EnrichedAlert CreateAlert(Guid alertId)
    {
        return new EnrichedAlert
        {
            AlertId = alertId,
            RuleId = Guid.NewGuid(),
            RuleName = "Retry Test Rule",
            Severity = Severity.Medium,
            Title = "Retry Test Alert",
            Detail = "Test",
            AgentId = "test-agent",
            AgentName = "TestAgent",
            SessionId = "test-session",
            TriggeredAt = DateTime.UtcNow
        };
    }
}
