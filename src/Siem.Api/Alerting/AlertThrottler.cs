using Siem.Api.Shared;
using StackExchange.Redis;

namespace Siem.Api.Alerting;

/// <summary>
/// Per-rule rate limiter using Redis sorted set sliding window.
/// Prevents a single noisy rule from flooding the notification channels.
/// </summary>
public class AlertThrottler : IAlertThrottler
{
    private readonly IDatabase _redis;
    private readonly int _maxPerWindow;
    private readonly TimeSpan _window;
    private readonly ILogger<AlertThrottler> _logger;

    public AlertThrottler(
        IConnectionMultiplexer redis,
        AlertPipelineConfig config,
        ILogger<AlertThrottler> logger)
    {
        _redis = redis.GetDatabase();
        _maxPerWindow = config.ThrottleMaxAlertsPerWindow;
        _window = config.ThrottleWindow;
        _logger = logger;
    }

    /// <summary>
    /// Returns true if the rule has exceeded its alert rate limit
    /// within the configured sliding window.
    /// </summary>
    public async Task<bool> IsThrottledAsync(Guid ruleId, CancellationToken ct = default)
    {
        try
        {
            var key = RedisKeys.AlertThrottle(ruleId);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var windowStart = now - (long)_window.TotalMilliseconds;

            var transaction = _redis.CreateTransaction();

            // Add this alert attempt
            _ = transaction.SortedSetAddAsync(key, now.ToString(), now);

            // Remove entries outside window
            _ = transaction.SortedSetRemoveRangeByScoreAsync(
                key, double.NegativeInfinity, windowStart);

            // Set TTL to twice the window to allow cleanup
            _ = transaction.KeyExpireAsync(key, _window + _window);

            await transaction.ExecuteAsync();

            var count = await _redis.SortedSetLengthAsync(key);
            return count > _maxPerWindow;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex,
                "Redis connection failed checking throttle for rule {RuleId}",
                ruleId);
            return false;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex,
                "Redis timeout checking throttle for rule {RuleId}",
                ruleId);
            return false;
        }
    }
}
