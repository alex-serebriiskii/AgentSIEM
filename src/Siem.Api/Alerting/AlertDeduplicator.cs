using StackExchange.Redis;

namespace Siem.Api.Alerting;

/// <summary>
/// Redis-based deduplication. Checks whether an alert with the same
/// fingerprint was already created within a time window. Uses SET NX
/// with TTL -- if the key already exists, it's a duplicate.
/// </summary>
public class AlertDeduplicator : IAlertDeduplicator
{
    private readonly IDatabase _redis;
    private readonly TimeSpan _window;

    public AlertDeduplicator(IConnectionMultiplexer redis, AlertPipelineConfig config)
    {
        _redis = redis.GetDatabase();
        _window = config.DeduplicationWindow;
    }

    /// <summary>
    /// Returns true if an alert with this fingerprint was already seen
    /// within the deduplication window.
    /// </summary>
    public async Task<bool> IsDuplicateAsync(string fingerprint, CancellationToken ct = default)
    {
        var key = $"alert:dedup:{fingerprint}";

        // SET NX = set if not exists. Returns false if key already existed.
        var wasSet = await _redis.StringSetAsync(
            key,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            _window,
            When.NotExists);

        // If we couldn't set (key existed) -> it's a duplicate
        return !wasSet;
    }
}
