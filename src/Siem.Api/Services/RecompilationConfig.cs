namespace Siem.Api.Services;

/// <summary>
/// Configuration for the rule recompilation coordinator.
/// </summary>
public class RecompilationConfig
{
    /// <summary>
    /// Debounce window in milliseconds. After the last invalidation signal,
    /// the coordinator waits this long before starting compilation.
    /// </summary>
    public int DebounceWindowMs { get; set; } = 500;

    /// <summary>
    /// Maximum time in seconds to wait for debouncing before forcing compilation.
    /// </summary>
    public int MaxDebounceDelaySeconds { get; set; } = 5;

    /// <summary>Timeout in seconds for SignalAndWaitAsync callers.</summary>
    public int SignalTimeoutSeconds { get; set; } = 30;

    /// <summary>Delay in seconds before retrying after a compilation error.</summary>
    public int ErrorRecoveryDelaySeconds { get; set; } = 2;

    /// <summary>Capacity of the invalidation signal channel.</summary>
    public int ChannelCapacity { get; set; } = 100;
}
