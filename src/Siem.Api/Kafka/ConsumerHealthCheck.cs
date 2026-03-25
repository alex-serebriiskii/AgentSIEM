using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Siem.Api.Kafka;

/// <summary>
/// Tracks Kafka consumer health for Kubernetes liveness/readiness probes
/// and the management API. A consumer is unhealthy if it hasn't consumed
/// a message recently or has accumulated too many errors.
/// </summary>
public class ConsumerHealthCheck : IHealthCheck
{
    private DateTime _lastConsumeTime = DateTime.MinValue;
    private DateTime _lastErrorTime = DateTime.MinValue;
    private int _recentErrorCount;
    private List<TopicPartitionOffset> _assignedPartitions = new();
    private readonly object _lock = new();

    public void RecordConsume()
    {
        lock (_lock)
        {
            _lastConsumeTime = DateTime.UtcNow;
            // Decay error count on successful consumption
            if (_recentErrorCount > 0) _recentErrorCount--;
        }
    }

    public void RecordError(object error)
    {
        lock (_lock)
        {
            _lastErrorTime = DateTime.UtcNow;
            _recentErrorCount++;
        }
    }

    public void RecordPartitionAssignment(IEnumerable<TopicPartition> partitions)
    {
        lock (_lock)
        {
            _assignedPartitions = partitions
                .Select(p => new TopicPartitionOffset(p, Offset.Unset))
                .ToList();
        }
    }

    public ConsumerHealthStatus GetStatus()
    {
        lock (_lock)
        {
            var timeSinceConsume = DateTime.UtcNow - _lastConsumeTime;
            var isHealthy =
                _lastConsumeTime != DateTime.MinValue &&
                timeSinceConsume < TimeSpan.FromMinutes(5) &&
                _recentErrorCount < 50;

            return new ConsumerHealthStatus
            {
                IsHealthy = isHealthy,
                LastConsumeTime = _lastConsumeTime,
                TimeSinceLastConsume = timeSinceConsume,
                RecentErrorCount = _recentErrorCount,
                AssignedPartitions = _assignedPartitions.Count,
                LastErrorTime = _lastErrorTime
            };
        }
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = GetStatus();

        var data = new Dictionary<string, object>
        {
            ["lastConsumeTime"] = status.LastConsumeTime,
            ["timeSinceLastConsume"] = status.TimeSinceLastConsume.ToString(),
            ["recentErrorCount"] = status.RecentErrorCount,
            ["assignedPartitions"] = status.AssignedPartitions,
            ["lastErrorTime"] = status.LastErrorTime
        };

        if (status.IsHealthy)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Kafka consumer is healthy", data));
        }

        var description = status.RecentErrorCount >= 50
            ? $"Too many recent errors: {status.RecentErrorCount}"
            : $"No messages consumed for {status.TimeSinceLastConsume}";

        return Task.FromResult(HealthCheckResult.Unhealthy(description, data: data));
    }
}

public class ConsumerHealthStatus
{
    public bool IsHealthy { get; init; }
    public DateTime LastConsumeTime { get; init; }
    public TimeSpan TimeSinceLastConsume { get; init; }
    public int RecentErrorCount { get; init; }
    public int AssignedPartitions { get; init; }
    public DateTime LastErrorTime { get; init; }
}
