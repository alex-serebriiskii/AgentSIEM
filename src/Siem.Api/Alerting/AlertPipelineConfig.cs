namespace Siem.Api.Alerting;

/// <summary>
/// Configuration for the alert pipeline noise-reduction stages.
/// </summary>
public class AlertPipelineConfig
{
    /// <summary>
    /// Time window for deduplication. Alerts with the same fingerprint
    /// within this window are considered duplicates.
    /// </summary>
    public int DeduplicationWindowMinutes { get; set; } = 15;

    public TimeSpan DeduplicationWindow => TimeSpan.FromMinutes(DeduplicationWindowMinutes);

    /// <summary>
    /// Maximum number of alerts per rule within the throttle window.
    /// </summary>
    public int ThrottleMaxAlertsPerWindow { get; set; } = 10;

    /// <summary>
    /// Sliding window size for per-rule throttling.
    /// </summary>
    public int ThrottleWindowMinutes { get; set; } = 5;

    public TimeSpan ThrottleWindow => TimeSpan.FromMinutes(ThrottleWindowMinutes);
}
