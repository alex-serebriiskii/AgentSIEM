using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Api.Alerting;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Notifications;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;
using Siem.Rules.Core;
using static Siem.Rules.Core.Evaluator;

namespace Siem.Integration.Tests.Tests.Alerting;

[NotInParallel("database")]
public class AlertPipelineEndToEndTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
        await DbHelper.FlushRedisAsync();
    }

    private static EvaluationResult CreateEvalResult(
        Guid? ruleId = null,
        Severity? severity = null)
    {
        return new EvaluationResult(
            triggered: true,
            ruleId: ruleId ?? Guid.NewGuid(),
            severity: severity ?? Severity.Medium,
            detail: FSharpOption<string>.Some("Test alert triggered"),
            context: MapModule.Empty<string, object>(),
            actions: FSharpList<RuleAction>.Empty);
    }

    private static AlertPipeline CreatePipeline(
        IServiceProvider serviceProvider,
        INotificationChannel[]? channels = null,
        AlertPipelineConfig? config = null)
    {
        config ??= new AlertPipelineConfig
        {
            DeduplicationWindowMinutes = 15,
            ThrottleMaxAlertsPerWindow = 10,
            ThrottleWindowMinutes = 5
        };

        var dedup = new AlertDeduplicator(IntegrationTestFixture.RedisMultiplexer, config);
        var throttler = new AlertThrottler(IntegrationTestFixture.RedisMultiplexer, config);
        var retryWorker = new NotificationRetryWorker(NullLogger<NotificationRetryWorker>.Instance, new NotificationRetryConfig());
        var router = new NotificationRouter(
            channels ?? [],
            retryWorker,
            NullLogger<NotificationRouter>.Instance);

        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        return new AlertPipeline(
            dedup,
            throttler,
            scopeFactory,
            router,
            NullLogger<AlertPipeline>.Instance);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SiemDbContext>(options =>
            options.UseNpgsql(IntegrationTestFixture.TimescaleConnectionString));
        services.AddScoped<SuppressionChecker>();
        services.AddScoped<AlertEnricher>();
        services.AddScoped<AlertPersistence>();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        return services.BuildServiceProvider();
    }

    // --- Test 1: Full pipeline creates alert ---

    [Test]
    public async Task FullPipeline_MatchingEvent_CreatesAlert()
    {
        // Arrange: create a rule in DB so enricher can look it up
        var ruleId = Guid.NewGuid();
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(id: ruleId, name: "E2E Test Rule"));
            await db.SaveChangesAsync();
        }

        using var sp = BuildServiceProvider();
        var recordingChannel = new RecordingNotificationChannel();
        var pipeline = CreatePipeline(sp, channels: [recordingChannel]);

        var evalResult = CreateEvalResult(ruleId: ruleId);
        var evt = TestEventFactory.CreateToolInvocation(agentId: "agent-e2e", sessionId: "sess-e2e");

        // Act
        await pipeline.ProcessAsync(evalResult, evt, CancellationToken.None);

        // Assert: alert persisted in DB
        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var alerts = await db2.Alerts.Where(a => a.RuleId == ruleId).ToListAsync();
        alerts.Should().HaveCount(1);
        alerts[0].Status.Should().Be("open");
        alerts[0].RuleName.Should().Be("E2E Test Rule");

        // Assert: alert-event junction created
        var junctions = await db2.AlertEvents
            .Where(ae => ae.AlertId == alerts[0].AlertId)
            .ToListAsync();
        junctions.Should().HaveCount(1);
        junctions[0].EventId.Should().Be(evt.EventId);

        // Assert: notification channel was called
        // Allow a brief moment for fire-and-forget routing
        await Task.Delay(100);
        recordingChannel.SentAlerts.Should().HaveCount(1);
    }

    // --- Test 2: Duplicate events produce one alert ---

    [Test]
    public async Task FullPipeline_DuplicateEvents_ProduceOneAlert()
    {
        var ruleId = Guid.NewGuid();
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(id: ruleId, name: "Dedup Test Rule"));
            await db.SaveChangesAsync();
        }

        using var sp = BuildServiceProvider();
        var pipeline = CreatePipeline(sp);

        var evalResult = CreateEvalResult(ruleId: ruleId);
        // Same agent + same rule + same severity = same fingerprint
        var evt1 = TestEventFactory.CreateToolInvocation(agentId: "agent-dedup", sessionId: "sess-dedup");
        var evt2 = TestEventFactory.CreateToolInvocation(agentId: "agent-dedup", sessionId: "sess-dedup");

        // Act
        await pipeline.ProcessAsync(evalResult, evt1, CancellationToken.None);
        await pipeline.ProcessAsync(evalResult, evt2, CancellationToken.None);

        // Assert: only one alert persisted
        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var alertCount = await db2.Alerts.CountAsync(a => a.RuleId == ruleId);
        alertCount.Should().Be(1);
    }

    // --- Test 3: Throttle caps alerts ---

    [Test]
    public async Task FullPipeline_ThrottledRule_CapsAlerts()
    {
        var ruleId = Guid.NewGuid();
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(id: ruleId, name: "Throttle Test Rule"));
            await db.SaveChangesAsync();
        }

        using var sp = BuildServiceProvider();
        var config = new AlertPipelineConfig
        {
            DeduplicationWindowMinutes = 15,
            ThrottleMaxAlertsPerWindow = 10,
            ThrottleWindowMinutes = 5
        };
        var pipeline = CreatePipeline(sp, config: config);

        // Act: send 12 events — each with a unique agent to avoid dedup
        for (int i = 0; i < 12; i++)
        {
            var evalResult = CreateEvalResult(ruleId: ruleId);
            var evt = TestEventFactory.CreateToolInvocation(
                agentId: $"agent-throttle-{i}",
                sessionId: $"sess-throttle-{i}");
            await pipeline.ProcessAsync(evalResult, evt, CancellationToken.None);
            await Task.Delay(2); // ensure unique timestamps for throttle sorted set
        }

        // Assert: at most 10 alerts created
        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var alertCount = await db2.Alerts.CountAsync(a => a.RuleId == ruleId);
        alertCount.Should().BeLessOrEqualTo(10);
        alertCount.Should().BeGreaterThanOrEqualTo(1);
    }

    // --- Test 4: Suppressed alert not persisted ---

    [Test]
    public async Task FullPipeline_SuppressedAlert_NotPersisted()
    {
        var ruleId = Guid.NewGuid();
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(id: ruleId, name: "Suppressed Test Rule"));
            // Create an active suppression for this rule
            db.Suppressions.Add(new SuppressionEntity
            {
                Id = Guid.NewGuid(),
                RuleId = ruleId,
                AgentId = null, // applies to all agents
                Reason = "Test suppression",
                CreatedBy = "test",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        using var sp = BuildServiceProvider();
        var pipeline = CreatePipeline(sp);

        var evalResult = CreateEvalResult(ruleId: ruleId);
        var evt = TestEventFactory.CreateToolInvocation(agentId: "agent-suppressed", sessionId: "sess-suppressed");

        // Act
        await pipeline.ProcessAsync(evalResult, evt, CancellationToken.None);

        // Assert: no alert persisted
        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var alertCount = await db2.Alerts.CountAsync(a => a.RuleId == ruleId);
        alertCount.Should().Be(0);
    }

    // --- Test 5: Notification dispatch routes to channels ---

    [Test]
    public async Task FullPipeline_NotificationDispatch_RoutesToChannels()
    {
        var ruleId = Guid.NewGuid();
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(id: ruleId, name: "Notify Test Rule", severity: "high"));
            await db.SaveChangesAsync();
        }

        using var sp = BuildServiceProvider();
        var lowChannel = new RecordingNotificationChannel("low-channel", "low");
        var highChannel = new RecordingNotificationChannel("high-channel", "high");
        var criticalChannel = new RecordingNotificationChannel("critical-channel", "critical");

        var pipeline = CreatePipeline(sp, channels: [lowChannel, highChannel, criticalChannel]);

        var evalResult = CreateEvalResult(ruleId: ruleId, severity: Severity.High);
        var evt = TestEventFactory.CreateToolInvocation(agentId: "agent-notify", sessionId: "sess-notify");

        // Act
        await pipeline.ProcessAsync(evalResult, evt, CancellationToken.None);

        // Allow fire-and-forget routing to complete
        await Task.Delay(200);

        // Assert: low and high channels should receive (severity >= minimum)
        // critical channel should NOT receive (high < critical)
        lowChannel.SentAlerts.Should().HaveCount(1);
        highChannel.SentAlerts.Should().HaveCount(1);
        criticalChannel.SentAlerts.Should().BeEmpty();
    }

    // --- Test 6: Retry worker processes queued notifications ---

    [Test]
    public async Task FullPipeline_RetryWorker_ProcessesQueuedNotification()
    {
        // Verify the retry worker picks up queued notifications and delivers them.
        // We enqueue a notification with immediate NextAttemptAt to a recording channel.
        var recordingChannel = new RecordingNotificationChannel("retry-test", "low");
        var retryWorker = new NotificationRetryWorker(NullLogger<NotificationRetryWorker>.Instance, new NotificationRetryConfig());

        using var cts = new CancellationTokenSource();
        var workerTask = retryWorker.StartAsync(cts.Token);

        var testAlert = new EnrichedAlert
        {
            AlertId = Guid.NewGuid(),
            RuleId = Guid.NewGuid(),
            RuleName = "Retry Test Rule",
            Severity = "medium",
            Title = "Retry Test",
            Detail = "Testing retry",
            AgentId = "agent-retry",
            AgentName = "TestAgent",
            SessionId = "sess-retry",
            TriggeredAt = DateTime.UtcNow
        };

        // Enqueue with immediate retry (simulates a re-queued notification)
        retryWorker.EnqueueRetry(new PendingNotification(
            Channel: recordingChannel,
            Alert: testAlert,
            AttemptCount: 1,
            NextAttemptAt: DateTime.UtcNow));

        // Wait for processing
        await Task.Delay(500);

        // Assert: channel should have received the alert
        recordingChannel.SentAlerts.Should().HaveCount(1);
        recordingChannel.SentAlerts[0].AlertId.Should().Be(testAlert.AlertId);

        // Cleanup
        cts.Cancel();
        try { await workerTask; } catch (OperationCanceledException) { }
        await retryWorker.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task FullPipeline_RetryWorker_FailedDeliveryAttempted()
    {
        // Verify the retry worker attempts delivery even when the channel
        // will fail — proving the worker processes queued items and doesn't
        // skip them. The re-enqueueing behavior uses 30s+ backoff intervals
        // which aren't practical to wait for in a test.
        var failOnceChannel = new FailOnceNotificationChannel();
        var retryWorker = new NotificationRetryWorker(NullLogger<NotificationRetryWorker>.Instance, new NotificationRetryConfig());

        using var cts = new CancellationTokenSource();
        _ = retryWorker.StartAsync(cts.Token);

        var testAlert = new EnrichedAlert
        {
            AlertId = Guid.NewGuid(),
            RuleId = Guid.NewGuid(),
            RuleName = "Retry Fail Test",
            Severity = "medium",
            Title = "Retry Fail Test",
            Detail = "Testing retry failure path",
            AgentId = "agent-retry-fail",
            AgentName = "TestAgent",
            SessionId = "sess-retry-fail",
            TriggeredAt = DateTime.UtcNow
        };

        // Enqueue: first call will fail
        retryWorker.EnqueueRetry(new PendingNotification(
            Channel: failOnceChannel,
            Alert: testAlert,
            AttemptCount: 1,
            NextAttemptAt: DateTime.UtcNow));

        // Wait for the attempt
        await Task.Delay(500);

        // Assert: channel was called once (worker attempted delivery)
        failOnceChannel.TotalCalls.Should().Be(1);

        // Cleanup
        cts.Cancel();
        await retryWorker.StopAsync(CancellationToken.None);
    }

    // --- Helper classes ---

    /// <summary>
    /// A notification channel that records all alerts sent to it.
    /// </summary>
    private class RecordingNotificationChannel : INotificationChannel
    {
        private readonly List<EnrichedAlert> _sentAlerts = [];

        public RecordingNotificationChannel(
            string name = "recording", string minimumSeverity = "low")
        {
            Name = name;
            MinimumSeverity = minimumSeverity;
        }

        public string Name { get; }
        public string MinimumSeverity { get; }
        public IReadOnlyList<EnrichedAlert> SentAlerts => _sentAlerts;

        public Task SendAsync(EnrichedAlert alert, CancellationToken ct = default)
        {
            _sentAlerts.Add(alert);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A notification channel that throws on the first call and succeeds on subsequent calls.
    /// </summary>
    private class FailOnceNotificationChannel : INotificationChannel
    {
        private int _callCount;

        public string Name => "fail-once";
        public string MinimumSeverity => "low";
        public int TotalCalls => _callCount;
        public int SuccessfulCalls { get; private set; }

        public Task SendAsync(EnrichedAlert alert, CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _callCount);
            if (call == 1)
                throw new InvalidOperationException("Simulated notification failure");

            SuccessfulCalls++;
            return Task.CompletedTask;
        }
    }
}
