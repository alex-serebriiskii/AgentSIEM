using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Siem.Api.Notifications;
using Siem.Rules.Core;
using static Siem.Rules.Core.Evaluator;

namespace Siem.Api.Alerting;

/// <summary>
/// 6-stage alert pipeline orchestrator: dedup -> throttle -> suppress -> enrich -> persist -> route.
/// Uses IAlertProcessingScopeFactory to resolve scoped services (enricher, persistence, suppression checker).
/// </summary>
public class AlertPipeline : IAlertPipeline
{
    private readonly IAlertDeduplicator _dedup;
    private readonly IAlertThrottler _throttler;
    private readonly IAlertProcessingScopeFactory _scopeFactory;
    private readonly INotificationRouter _router;
    private readonly ILogger<AlertPipeline> _logger;

    private static readonly Meter Meter = new("Siem.Alerts");
    private static readonly Counter<long> AlertsReceived =
        Meter.CreateCounter<long>("siem.alerts.received");
    private static readonly Counter<long> AlertsDeduplicated =
        Meter.CreateCounter<long>("siem.alerts.deduplicated");
    private static readonly Counter<long> AlertsThrottled =
        Meter.CreateCounter<long>("siem.alerts.throttled");
    private static readonly Counter<long> AlertsSuppressed =
        Meter.CreateCounter<long>("siem.alerts.suppressed");
    private static readonly Counter<long> AlertsCreated =
        Meter.CreateCounter<long>("siem.alerts.created");
    private static readonly Counter<long> NotificationRoutingErrors =
        Meter.CreateCounter<long>("siem.alerts.notification_routing_errors");

    public AlertPipeline(
        IAlertDeduplicator dedup,
        IAlertThrottler throttler,
        IAlertProcessingScopeFactory scopeFactory,
        INotificationRouter router,
        ILogger<AlertPipeline> logger)
    {
        _dedup = dedup;
        _throttler = throttler;
        _scopeFactory = scopeFactory;
        _router = router;
        _logger = logger;
    }

    public async Task ProcessAsync(
        EvaluationResult result,
        AgentEvent evt,
        CancellationToken ct = default)
    {
        AlertsReceived.Add(1);

        // Stage 1: Deduplication
        var fingerprint = ComputeFingerprint(result, evt);
        if (await _dedup.IsDuplicateAsync(fingerprint, ct))
        {
            AlertsDeduplicated.Add(1);
            _logger.LogDebug("Alert deduplicated: rule={RuleId} agent={AgentId}",
                result.RuleId, evt.AgentId);
            return;
        }

        // Stage 2: Throttle
        if (await _throttler.IsThrottledAsync(result.RuleId, ct))
        {
            AlertsThrottled.Add(1);
            _logger.LogDebug("Alert throttled: rule={RuleId}", result.RuleId);
            return;
        }

        // Stages 3-5 require scoped services (DbContext)
        using var scope = _scopeFactory.CreateScope();

        // Stage 3: Suppression
        if (await scope.Suppression.IsSuppressedAsync(result.RuleId, evt.AgentId, ct))
        {
            AlertsSuppressed.Add(1);
            _logger.LogDebug("Alert suppressed: rule={RuleId} agent={AgentId}",
                result.RuleId, evt.AgentId);
            return;
        }

        // Stage 4: Enrich
        var enrichedAlert = await scope.Enricher.EnrichAsync(result, evt, ct);

        // Stage 5: Persist
        var alertId = await scope.Persistence.SaveAsync(enrichedAlert, evt, ct);
        enrichedAlert = enrichedAlert with { AlertId = alertId };

        AlertsCreated.Add(1, new KeyValuePair<string, object?>("severity",
            enrichedAlert.Severity));

        _logger.LogInformation(
            "Alert created: id={AlertId} rule={RuleId} severity={Severity} agent={AgentId}",
            alertId, result.RuleId, enrichedAlert.Severity, evt.AgentId);

        // Stage 6: Route notifications
        // Per-channel failures are handled inside RouteAsync (retry queue).
        // We await here so structural errors (e.g. router misconfiguration) surface.
        try
        {
            await _router.RouteAsync(enrichedAlert, ct);
        }
        catch (Exception ex)
        {
            NotificationRoutingErrors.Add(1);
            _logger.LogError(ex,
                "Notification routing failed for alert {AlertId}", alertId);
        }
    }

    private static string ComputeFingerprint(EvaluationResult result, AgentEvent evt)
    {
        // Fingerprint = SHA256(rule_id + agent_id + severity).
        // Two alerts from the same rule for the same agent with the same
        // severity within the dedup window are considered duplicates.
        var input = $"{result.RuleId}:{evt.AgentId}:{result.Severity}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }
}
