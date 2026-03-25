using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Siem.Api.Alerting;
using Siem.Api.Notifications;

namespace Siem.Api.Tests.Notifications;

public class NotificationRouterTests : IDisposable
{
    private readonly INotificationChannel _lowChannel;
    private readonly INotificationChannel _mediumChannel;
    private readonly INotificationChannel _highChannel;
    private readonly INotificationChannel _criticalChannel;
    private readonly NotificationRetryWorker _retryWorker;
    private readonly NotificationRouter _router;

    public NotificationRouterTests()
    {
        _lowChannel = CreateMockChannel("low-channel", "low");
        _mediumChannel = CreateMockChannel("medium-channel", "medium");
        _highChannel = CreateMockChannel("high-channel", "high");
        _criticalChannel = CreateMockChannel("critical-channel", "critical");

        _retryWorker = new NotificationRetryWorker(
            NullLogger<NotificationRetryWorker>.Instance);

        _router = new NotificationRouter(
            new[] { _lowChannel, _mediumChannel, _highChannel, _criticalChannel },
            _retryWorker,
            NullLogger<NotificationRouter>.Instance);
    }

    public void Dispose()
    {
        _retryWorker.Dispose();
    }

    private static INotificationChannel CreateMockChannel(string name, string minimumSeverity)
    {
        var channel = Substitute.For<INotificationChannel>();
        channel.Name.Returns(name);
        channel.MinimumSeverity.Returns(minimumSeverity);
        return channel;
    }

    private static EnrichedAlert CreateAlert(string severity = "medium")
    {
        return new EnrichedAlert
        {
            AlertId = Guid.NewGuid(),
            RuleId = Guid.NewGuid(),
            RuleName = "Test Rule",
            Severity = severity,
            Title = "Test Alert",
            Detail = "Test detail",
            AgentId = "agent-001",
            AgentName = "TestAgent",
            SessionId = "sess-001",
            TriggeredAt = DateTime.UtcNow
        };
    }

    [Test]
    public async Task RouteAsync_CriticalAlert_RoutesToAllChannels()
    {
        var alert = CreateAlert("critical");

        await _router.RouteAsync(alert);

        await _lowChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _mediumChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _highChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _criticalChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteAsync_LowAlert_RoutesOnlyToLowChannel()
    {
        var alert = CreateAlert("low");

        await _router.RouteAsync(alert);

        await _lowChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _mediumChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
        await _highChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
        await _criticalChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteAsync_MediumAlert_SkipsHighAndCriticalChannels()
    {
        var alert = CreateAlert("medium");

        await _router.RouteAsync(alert);

        await _lowChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _mediumChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _highChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
        await _criticalChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteAsync_HighAlert_RoutesToLowMediumAndHigh()
    {
        var alert = CreateAlert("high");

        await _router.RouteAsync(alert);

        await _lowChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _mediumChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _highChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _criticalChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteAsync_ChannelThrows_DoesNotPropagateException()
    {
        _lowChannel.SendAsync(Arg.Any<EnrichedAlert>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var alert = CreateAlert("low");

        var act = () => _router.RouteAsync(alert);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task RouteAsync_UnknownSeverity_TreatedAsLow()
    {
        var alert = CreateAlert("unknown");

        await _router.RouteAsync(alert);

        // Unknown severity maps to 0 (same as low), so only low channel matches
        await _lowChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _mediumChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteAsync_NoMatchingChannels_CompletesSuccessfully()
    {
        // Router with only a critical channel
        var criticalOnly = new NotificationRouter(
            new[] { _criticalChannel },
            _retryWorker,
            NullLogger<NotificationRouter>.Instance);

        var alert = CreateAlert("low");

        var act = () => criticalOnly.RouteAsync(alert);

        await act.Should().NotThrowAsync();
        await _criticalChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
    }
}
