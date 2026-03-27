using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Siem.Api.Alerting;
using Siem.Api.Data.Enums;
using Siem.Api.Notifications;

namespace Siem.Api.Tests.Notifications;

public class NotificationRouterTests
{
    private readonly INotificationChannel _lowChannel;
    private readonly INotificationChannel _mediumChannel;
    private readonly INotificationChannel _highChannel;
    private readonly INotificationChannel _criticalChannel;
    private readonly INotificationRetryWorker _retryWorker;
    private readonly NotificationRouter _router;

    public NotificationRouterTests()
    {
        _lowChannel = CreateMockChannel("low-channel", Severity.Low);
        _mediumChannel = CreateMockChannel("medium-channel", Severity.Medium);
        _highChannel = CreateMockChannel("high-channel", Severity.High);
        _criticalChannel = CreateMockChannel("critical-channel", Severity.Critical);

        _retryWorker = Substitute.For<INotificationRetryWorker>();

        _router = new NotificationRouter(
            new[] { _lowChannel, _mediumChannel, _highChannel, _criticalChannel },
            _retryWorker,
            NullLogger<NotificationRouter>.Instance);
    }

    private static INotificationChannel CreateMockChannel(string name, Severity minimumSeverity)
    {
        var channel = Substitute.For<INotificationChannel>();
        channel.Name.Returns(name);
        channel.MinimumSeverity.Returns(minimumSeverity);
        return channel;
    }

    private static EnrichedAlert CreateAlert(Severity severity = Severity.Medium)
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
        var alert = CreateAlert(Severity.Critical);

        await _router.RouteAsync(alert);

        await _lowChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _mediumChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _highChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _criticalChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteAsync_LowAlert_RoutesOnlyToLowChannel()
    {
        var alert = CreateAlert(Severity.Low);

        await _router.RouteAsync(alert);

        await _lowChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _mediumChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
        await _highChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
        await _criticalChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteAsync_MediumAlert_SkipsHighAndCriticalChannels()
    {
        var alert = CreateAlert(Severity.Medium);

        await _router.RouteAsync(alert);

        await _lowChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _mediumChannel.Received(1).SendAsync(alert, Arg.Any<CancellationToken>());
        await _highChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
        await _criticalChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RouteAsync_HighAlert_RoutesToLowMediumAndHigh()
    {
        var alert = CreateAlert(Severity.High);

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

        var alert = CreateAlert(Severity.Low);

        var act = () => _router.RouteAsync(alert);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task RouteAsync_ChannelThrows_EnqueuesRetry()
    {
        _lowChannel.SendAsync(Arg.Any<EnrichedAlert>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var alert = CreateAlert(Severity.Low);

        await _router.RouteAsync(alert);

        _retryWorker.Received(1).EnqueueRetry(
            Arg.Is<PendingNotification>(p =>
                p.Channel == _lowChannel &&
                p.Alert == alert &&
                p.AttemptCount == 1));
    }

    [Test]
    public async Task RouteAsync_NoMatchingChannels_CompletesSuccessfully()
    {
        // Router with only a critical channel
        var criticalOnly = new NotificationRouter(
            new[] { _criticalChannel },
            _retryWorker,
            NullLogger<NotificationRouter>.Instance);

        var alert = CreateAlert(Severity.Low);

        var act = () => criticalOnly.RouteAsync(alert);

        await act.Should().NotThrowAsync();
        await _criticalChannel.DidNotReceive().SendAsync(alert, Arg.Any<CancellationToken>());
    }
}
