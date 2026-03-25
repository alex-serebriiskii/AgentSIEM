using Siem.Rules.Core;

namespace Siem.Api.Alerting;

/// <summary>
/// Processes triggered rule evaluation results through the alert lifecycle:
/// dedup, throttle, suppression, enrichment, persistence, and notification routing.
/// </summary>
public interface IAlertPipeline
{
    /// <summary>
    /// Process a triggered rule evaluation result for the given event.
    /// </summary>
    Task ProcessAsync(
        Evaluator.EvaluationResult result,
        AgentEvent agentEvent,
        CancellationToken ct);
}
