using FluentAssertions;
using Siem.Api.Alerting;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Alerting;

[NotInParallel("database")]
public class AlertThrottlerIntegrationTests
{
    private AlertThrottler _throttler = null!;
    private AlertPipelineConfig _config = null!;

    [Before(Test)]
    public async Task Setup()
    {
        await DbHelper.FlushRedisAsync();
        _config = new AlertPipelineConfig
        {
            ThrottleMaxAlertsPerWindow = 10,
            ThrottleWindowMinutes = 5
        };
        _throttler = new AlertThrottler(IntegrationTestFixture.RedisMultiplexer, _config);
    }

    [Test]
    public async Task Throttle_BelowLimit_NotThrottled()
    {
        var ruleId = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            var isThrottled = await _throttler.IsThrottledAsync(ruleId);
            isThrottled.Should().BeFalse($"alert {i + 1} should not be throttled (under limit of 10)");
        }
    }

    [Test]
    public async Task Throttle_AtLimit_IsThrottled()
    {
        var ruleId = Guid.NewGuid();

        // Send 10 alerts (at limit)
        // Add small delays to ensure unique millisecond timestamps
        // (the throttler uses ms timestamp as the sorted set member)
        for (int i = 0; i < 10; i++)
        {
            await _throttler.IsThrottledAsync(ruleId);
            await Task.Delay(2);
        }

        // 11th should be throttled
        var isThrottled = await _throttler.IsThrottledAsync(ruleId);
        isThrottled.Should().BeTrue("11th alert should be throttled");
    }

    [Test]
    public async Task Throttle_DifferentRules_Independent()
    {
        var ruleA = Guid.NewGuid();
        var ruleB = Guid.NewGuid();

        // Exhaust throttle for rule A
        for (int i = 0; i < 11; i++)
        {
            await _throttler.IsThrottledAsync(ruleA);
        }

        // Rule B should still be fine
        var isThrottled = await _throttler.IsThrottledAsync(ruleB);
        isThrottled.Should().BeFalse("rule B is independent and should not be throttled");
    }

    [Test]
    public async Task Throttle_WindowExpiration_ResetsCount()
    {
        // Use a very short throttle window
        var redis = IntegrationTestFixture.RedisMultiplexer.GetDatabase();
        var ruleId = Guid.NewGuid();
        var key = $"alert:throttle:{ruleId}";

        // Manually add entries to the sorted set that are already outside the window
        var pastTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds();
        for (int i = 0; i < 15; i++)
        {
            await redis.SortedSetAddAsync(key, (pastTimestamp + i).ToString(), pastTimestamp + i);
        }

        // Now call IsThrottledAsync which cleans up old entries outside the 5-min window
        var isThrottled = await _throttler.IsThrottledAsync(ruleId);

        // The old entries should have been cleaned up, and only the new one remains
        isThrottled.Should().BeFalse("old entries outside window should be cleaned up");
    }
}
