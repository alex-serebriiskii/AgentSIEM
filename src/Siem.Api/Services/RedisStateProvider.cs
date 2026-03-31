using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Siem.Api.Shared;
using StackExchange.Redis;
using Siem.Rules.Core;

namespace Siem.Api.Services;

/// <summary>
/// Implements the F# IStateProvider interface using StackExchange.Redis.
/// Sliding windows use sorted sets with timestamp scores.
/// Sequence progress uses simple string keys with TTL.
/// </summary>
public class RedisStateProvider : Evaluator.IStateProvider
{
    private readonly IConnectionMultiplexer _redis;

    public RedisStateProvider(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public FSharpAsync<long> IncrementSlidingWindowAsync(string key, TimeSpan window)
    {
        return FSharpAsync.AwaitTask(IncrementSlidingWindowCoreAsync(key, window));
    }

    private async Task<long> IncrementSlidingWindowCoreAsync(string key, TimeSpan window)
    {
        var db = _redis.GetDatabase();
        return await RedisSlidingWindowHelper.IncrementAsync(
            db, key, Guid.NewGuid().ToString(), window);
    }

    public FSharpAsync<int> GetSequenceProgressAsync(string key)
    {
        return FSharpAsync.AwaitTask(GetSequenceProgressCoreAsync(key));
    }

    private async Task<int> GetSequenceProgressCoreAsync(string key)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(key);

        if (value.IsNullOrEmpty)
            return 0;

        return int.TryParse(value.ToString(), out var step) ? step : 0;
    }

    public FSharpAsync<Unit> SetSequenceProgressAsync(string key, int step, TimeSpan ttl)
    {
        return FSharpAsync.AwaitTask(SetSequenceProgressCoreAsync(key, step, ttl));
    }

    private async Task<Unit> SetSequenceProgressCoreAsync(string key, int step, TimeSpan ttl)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(key, step.ToString(), ttl);
        return (Unit)Activator.CreateInstance(typeof(Unit), nonPublic: true)!;
    }

    public FSharpAsync<Unit> ClearSequenceAsync(string key)
    {
        return FSharpAsync.AwaitTask(ClearSequenceCoreAsync(key));
    }

    private async Task<Unit> ClearSequenceCoreAsync(string key)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);
        return (Unit)Activator.CreateInstance(typeof(Unit), nonPublic: true)!;
    }
}
