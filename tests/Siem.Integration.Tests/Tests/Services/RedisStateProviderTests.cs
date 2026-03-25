using FluentAssertions;
using Microsoft.FSharp.Control;
using Siem.Api.Services;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Services;

[NotInParallel("database")]
public class RedisStateProviderTests
{
    private RedisStateProvider _provider = null!;

    [Before(Test)]
    public async Task Setup()
    {
        await DbHelper.FlushRedisAsync();
        _provider = new RedisStateProvider(IntegrationTestFixture.RedisMultiplexer);
    }

    [Test]
    public async Task IncrementSlidingWindow_ReturnsIncreasingCount()
    {
        var key = $"test:window:{Guid.NewGuid():N}";
        var window = TimeSpan.FromMinutes(5);

        for (var i = 1; i <= 5; i++)
        {
            var count = await FSharpAsync.StartAsTask(
                _provider.IncrementSlidingWindowAsync(key, window),
                null, null);
            count.Should().Be(i);
        }
    }

    [Test]
    public async Task GetSequenceProgress_ReturnsZero_WhenMissing()
    {
        var key = $"test:seq:{Guid.NewGuid():N}";

        var progress = await FSharpAsync.StartAsTask(
            _provider.GetSequenceProgressAsync(key),
            null, null);

        progress.Should().Be(0);
    }

    [Test]
    public async Task SetAndGetSequenceProgress_RoundTrips()
    {
        var key = $"test:seq:{Guid.NewGuid():N}";

        await FSharpAsync.StartAsTask(
            _provider.SetSequenceProgressAsync(key, 3, TimeSpan.FromMinutes(5)),
            null, null);

        var progress = await FSharpAsync.StartAsTask(
            _provider.GetSequenceProgressAsync(key),
            null, null);

        progress.Should().Be(3);
    }

    [Test]
    public async Task SetSequenceProgress_RespectsTtl()
    {
        var key = $"test:seq:{Guid.NewGuid():N}";

        await FSharpAsync.StartAsTask(
            _provider.SetSequenceProgressAsync(key, 2, TimeSpan.FromMilliseconds(200)),
            null, null);

        // Wait for key to expire
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        var progress = await FSharpAsync.StartAsTask(
            _provider.GetSequenceProgressAsync(key),
            null, null);

        progress.Should().Be(0);
    }

    [Test]
    public async Task ClearSequence_RemovesKey()
    {
        var key = $"test:seq:{Guid.NewGuid():N}";

        await FSharpAsync.StartAsTask(
            _provider.SetSequenceProgressAsync(key, 5, TimeSpan.FromMinutes(5)),
            null, null);

        await FSharpAsync.StartAsTask(
            _provider.ClearSequenceAsync(key),
            null, null);

        var progress = await FSharpAsync.StartAsTask(
            _provider.GetSequenceProgressAsync(key),
            null, null);

        progress.Should().Be(0);
    }

    [Test]
    public async Task IncrementSlidingWindow_DifferentKeys_Independent()
    {
        var key1 = $"test:window:a:{Guid.NewGuid():N}";
        var key2 = $"test:window:b:{Guid.NewGuid():N}";
        var window = TimeSpan.FromMinutes(5);

        // Increment key1 three times
        for (var i = 0; i < 3; i++)
        {
            await FSharpAsync.StartAsTask(
                _provider.IncrementSlidingWindowAsync(key1, window),
                null, null);
        }

        // Increment key2 once
        var count2 = await FSharpAsync.StartAsTask(
            _provider.IncrementSlidingWindowAsync(key2, window),
            null, null);

        count2.Should().Be(1);
    }
}
