using StackExchange.Redis;

namespace Siem.Api.Shared;

/// <summary>
/// Shared Redis sorted set sliding window: add entry, prune expired, set TTL, return count.
/// </summary>
public static class RedisSlidingWindowHelper
{
    public static async Task<long> IncrementAsync(
        IDatabase db,
        string key,
        string member,
        TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - (long)window.TotalMilliseconds;

        var transaction = db.CreateTransaction();
        _ = transaction.SortedSetAddAsync(key, member, now);
        _ = transaction.SortedSetRemoveRangeByScoreAsync(
            key, double.NegativeInfinity, windowStart);
        _ = transaction.KeyExpireAsync(key, window + window);
        await transaction.ExecuteAsync();

        return await db.SortedSetLengthAsync(key);
    }
}
