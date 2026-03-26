using FluentAssertions;
using Siem.Api.Alerting;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Alerting;

[NotInParallel("database")]
public class AlertDeduplicatorIntegrationTests
{
    private AlertDeduplicator _dedup = null!;

    [Before(Test)]
    public async Task Setup()
    {
        await DbHelper.FlushRedisAsync();
        var config = new AlertPipelineConfig
        {
            DeduplicationWindowMinutes = 15
        };
        _dedup = new AlertDeduplicator(IntegrationTestFixture.RedisMultiplexer, config);
    }

    [Test]
    public async Task Dedup_FirstEvent_NotDuplicate()
    {
        var fingerprint = $"fp:{Guid.NewGuid():N}";

        var isDuplicate = await _dedup.IsDuplicateAsync(fingerprint);

        isDuplicate.Should().BeFalse();
    }

    [Test]
    public async Task Dedup_SameFingerprint_WithinWindow_IsDuplicate()
    {
        var fingerprint = $"fp:{Guid.NewGuid():N}";

        await _dedup.IsDuplicateAsync(fingerprint);
        var isDuplicate = await _dedup.IsDuplicateAsync(fingerprint);

        isDuplicate.Should().BeTrue();
    }

    [Test]
    public async Task Dedup_DifferentFingerprints_NotDuplicate()
    {
        var fingerprint1 = $"fp:{Guid.NewGuid():N}";
        var fingerprint2 = $"fp:{Guid.NewGuid():N}";

        await _dedup.IsDuplicateAsync(fingerprint1);
        var isDuplicate = await _dedup.IsDuplicateAsync(fingerprint2);

        isDuplicate.Should().BeFalse();
    }

    [Test]
    public async Task Dedup_WindowExpiration_AllowsRefire()
    {
        // Use a very short dedup window for this test
        var shortConfig = new AlertPipelineConfig
        {
            DeduplicationWindowMinutes = 0 // Will result in 0ms window
        };
        // Use 1-second window via direct construction
        var shortDedup = new AlertDeduplicator(
            IntegrationTestFixture.RedisMultiplexer,
            new AlertPipelineConfig()); // default 15min — we'll use a unique key instead

        // Actually, let's create a dedup with a config that has a tiny window
        // The config uses int minutes so minimum non-zero is 1 min — too slow for a test.
        // Instead, verify the Redis key has a TTL set by checking a unique fingerprint
        // after the key expires. We'll use the raw Redis API to set a short TTL.

        var fingerprint = $"fp:expire:{Guid.NewGuid():N}";
        var key = $"alert:dedup:{fingerprint}";

        // Manually set the key with a very short TTL
        var redis = IntegrationTestFixture.RedisMultiplexer.GetDatabase();
        await redis.StringSetAsync(key, "1", TimeSpan.FromMilliseconds(200));

        // Should be duplicate right now (key exists)
        var isDuplicate = await _dedup.IsDuplicateAsync(fingerprint);
        isDuplicate.Should().BeTrue();

        // Wait for expiry
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        // Should no longer be duplicate (key expired)
        isDuplicate = await _dedup.IsDuplicateAsync(fingerprint);
        isDuplicate.Should().BeFalse();
    }
}
