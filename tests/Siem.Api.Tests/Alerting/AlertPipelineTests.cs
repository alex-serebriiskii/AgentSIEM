using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Siem.Api.Alerting;
using Siem.Api.Data;
using Siem.Api.Notifications;
using Siem.Rules.Core;

namespace Siem.Api.Tests.Alerting;

public class AlertPipelineTests
{
    private readonly IAlertDeduplicator _dedup;
    private readonly IAlertThrottler _throttler;
    private readonly INotificationRouter _router;
    private readonly SuppressionChecker _suppression;
    private readonly AlertEnricher _enricher;
    private readonly AlertPersistence _persistence;
    private readonly AlertPipeline _pipeline;

    public AlertPipelineTests()
    {
        _dedup = Substitute.For<IAlertDeduplicator>();
        _throttler = Substitute.For<IAlertThrottler>();
        _router = Substitute.For<INotificationRouter>();

        _suppression = Substitute.For<SuppressionChecker>(default(SiemDbContext)!, default(Microsoft.Extensions.Logging.ILogger<SuppressionChecker>)!);
        _enricher = Substitute.For<AlertEnricher>(default(SiemDbContext)!, default(Microsoft.Extensions.Logging.ILogger<AlertEnricher>)!);
        _persistence = Substitute.For<AlertPersistence>(default(SiemDbContext)!, default(Microsoft.Extensions.Logging.ILogger<AlertPersistence>)!);

        // Wire up IServiceScopeFactory to return our mocks
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(SuppressionChecker)).Returns(_suppression);
        serviceProvider.GetService(typeof(AlertEnricher)).Returns(_enricher);
        serviceProvider.GetService(typeof(AlertPersistence)).Returns(_persistence);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        // Default: nothing is duplicate/throttled/suppressed
        _dedup.IsDuplicateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _throttler.IsThrottledAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _suppression.IsSuppressedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Default enricher returns a basic alert
        _enricher.EnrichAsync(Arg.Any<Evaluator.EvaluationResult>(), Arg.Any<AgentEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => new EnrichedAlert
            {
                RuleId = ci.Arg<Evaluator.EvaluationResult>().RuleId,
                Severity = "medium",
                Title = "Test Alert",
                AgentId = ci.Arg<AgentEvent>().AgentId,
                TriggeredAt = DateTime.UtcNow
            });

        // Default persistence returns a new alert ID
        _persistence.SaveAsync(Arg.Any<EnrichedAlert>(), Arg.Any<AgentEvent>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        _pipeline = new AlertPipeline(
            _dedup, _throttler, scopeFactory, _router,
            NullLogger<AlertPipeline>.Instance);
    }

    private static Evaluator.EvaluationResult CreateEvalResult(Guid? ruleId = null)
    {
        return new Evaluator.EvaluationResult(
            triggered: true,
            ruleId: ruleId ?? Guid.NewGuid(),
            severity: Severity.Medium,
            detail: FSharpOption<string>.Some("Test alert"),
            context: MapModule.Empty<string, object>(),
            actions: FSharpList<RuleAction>.Empty);
    }

    private static AgentEvent CreateTestEvent(string agentId = "agent-001")
    {
        return new AgentEvent(
            eventId: Guid.NewGuid(),
            timestamp: DateTime.UtcNow,
            sessionId: "sess-001",
            traceId: "trace-001",
            agentId: agentId,
            agentName: "TestAgent",
            eventType: "tool_invocation",
            modelId: FSharpOption<string>.None,
            inputTokens: FSharpOption<int>.None,
            outputTokens: FSharpOption<int>.None,
            latencyMs: FSharpOption<double>.None,
            toolName: FSharpOption<string>.None,
            toolInput: FSharpOption<string>.None,
            toolOutput: FSharpOption<string>.None,
            contentHash: FSharpOption<string>.None,
            properties: MapModule.Empty<string, System.Text.Json.JsonElement>());
    }

    // ---- Stage 1: Deduplication ----

    [Test]
    public async Task ProcessAsync_WhenDuplicate_ShortCircuitsBeforeThrottle()
    {
        _dedup.IsDuplicateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await _pipeline.ProcessAsync(CreateEvalResult(), CreateTestEvent());

        await _throttler.DidNotReceive()
            .IsThrottledAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _router.DidNotReceive()
            .RouteAsync(Arg.Any<EnrichedAlert>(), Arg.Any<CancellationToken>());
    }

    // ---- Stage 2: Throttle ----

    [Test]
    public async Task ProcessAsync_WhenThrottled_ShortCircuitsBeforeSuppression()
    {
        _throttler.IsThrottledAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await _pipeline.ProcessAsync(CreateEvalResult(), CreateTestEvent());

        await _suppression.DidNotReceive()
            .IsSuppressedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _router.DidNotReceive()
            .RouteAsync(Arg.Any<EnrichedAlert>(), Arg.Any<CancellationToken>());
    }

    // ---- Stage 3: Suppression ----

    [Test]
    public async Task ProcessAsync_WhenSuppressed_ShortCircuitsBeforeEnrichment()
    {
        _suppression.IsSuppressedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await _pipeline.ProcessAsync(CreateEvalResult(), CreateTestEvent());

        await _enricher.DidNotReceive()
            .EnrichAsync(Arg.Any<Evaluator.EvaluationResult>(), Arg.Any<AgentEvent>(), Arg.Any<CancellationToken>());
        await _router.DidNotReceive()
            .RouteAsync(Arg.Any<EnrichedAlert>(), Arg.Any<CancellationToken>());
    }

    // ---- Stage 4-6: Full pipeline ----

    [Test]
    public async Task ProcessAsync_NoFilters_EnrichesPersistsAndRoutes()
    {
        var evalResult = CreateEvalResult();
        var evt = CreateTestEvent();

        await _pipeline.ProcessAsync(evalResult, evt);

        // Enrichment happened
        await _enricher.Received(1)
            .EnrichAsync(evalResult, evt, Arg.Any<CancellationToken>());

        // Persistence happened
        await _persistence.Received(1)
            .SaveAsync(Arg.Any<EnrichedAlert>(), evt, Arg.Any<CancellationToken>());

        // Routing happened
        await _router.Received(1)
            .RouteAsync(Arg.Any<EnrichedAlert>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_PersistenceReturnsId_AlertIdSetBeforeRouting()
    {
        var expectedId = Guid.NewGuid();
        _persistence.SaveAsync(Arg.Any<EnrichedAlert>(), Arg.Any<AgentEvent>(), Arg.Any<CancellationToken>())
            .Returns(expectedId);

        await _pipeline.ProcessAsync(CreateEvalResult(), CreateTestEvent());

        await _router.Received(1).RouteAsync(
            Arg.Is<EnrichedAlert>(a => a.AlertId == expectedId),
            Arg.Any<CancellationToken>());
    }

    // ---- Stage 6: Routing error handling ----

    [Test]
    public async Task ProcessAsync_RoutingThrows_DoesNotPropagateException()
    {
        _router.RouteAsync(Arg.Any<EnrichedAlert>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Router misconfigured"));

        var act = () => _pipeline.ProcessAsync(CreateEvalResult(), CreateTestEvent());

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ProcessAsync_RoutingThrows_AlertStillPersisted()
    {
        _router.RouteAsync(Arg.Any<EnrichedAlert>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        await _pipeline.ProcessAsync(CreateEvalResult(), CreateTestEvent());

        // Persistence happened before routing
        await _persistence.Received(1)
            .SaveAsync(Arg.Any<EnrichedAlert>(), Arg.Any<AgentEvent>(), Arg.Any<CancellationToken>());
    }

    // ---- Fingerprint / dedup key ----

    [Test]
    public async Task ProcessAsync_SameRuleAndAgent_ProducesSameFingerprint()
    {
        var ruleId = Guid.NewGuid();
        var evalResult = CreateEvalResult(ruleId);
        var evt = CreateTestEvent("agent-same");

        string? firstFingerprint = null;
        string? secondFingerprint = null;

        _dedup.IsDuplicateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (firstFingerprint == null)
                    firstFingerprint = ci.Arg<string>();
                else
                    secondFingerprint = ci.Arg<string>();
                return false;
            });

        await _pipeline.ProcessAsync(evalResult, evt);
        await _pipeline.ProcessAsync(evalResult, evt);

        firstFingerprint.Should().Be(secondFingerprint);
    }

    [Test]
    public async Task ProcessAsync_DifferentAgents_ProduceDifferentFingerprints()
    {
        var ruleId = Guid.NewGuid();
        var evalResult = CreateEvalResult(ruleId);

        string? firstFingerprint = null;
        string? secondFingerprint = null;

        _dedup.IsDuplicateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (firstFingerprint == null)
                    firstFingerprint = ci.Arg<string>();
                else
                    secondFingerprint = ci.Arg<string>();
                return false;
            });

        await _pipeline.ProcessAsync(evalResult, CreateTestEvent("agent-A"));
        await _pipeline.ProcessAsync(evalResult, CreateTestEvent("agent-B"));

        firstFingerprint.Should().NotBe(secondFingerprint);
    }

    // ---- Config defaults ----

    [Test]
    public async Task AlertPipelineConfig_HasCorrectDefaults()
    {
        var config = new AlertPipelineConfig();

        config.DeduplicationWindowMinutes.Should().Be(15);
        config.ThrottleMaxAlertsPerWindow.Should().Be(10);
        config.ThrottleWindowMinutes.Should().Be(5);
        config.DeduplicationWindow.Should().Be(TimeSpan.FromMinutes(15));
        config.ThrottleWindow.Should().Be(TimeSpan.FromMinutes(5));
    }
}
