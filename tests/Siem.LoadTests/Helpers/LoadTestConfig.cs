namespace Siem.LoadTests.Helpers;

/// <summary>
/// Environment-variable-driven threshold scaling for CI runners.
/// Set LOAD_TEST_THROUGHPUT_FACTOR=0.5 on weaker CI machines to halve throughput thresholds.
/// Set LOAD_TEST_LATENCY_FACTOR=2.0 to double latency thresholds (more permissive).
/// </summary>
public static class LoadTestConfig
{
    public static double ThroughputFactor { get; } =
        double.TryParse(
            Environment.GetEnvironmentVariable("LOAD_TEST_THROUGHPUT_FACTOR"),
            out var f) ? f : 1.0;

    public static double LatencyFactor { get; } =
        double.TryParse(
            Environment.GetEnvironmentVariable("LOAD_TEST_LATENCY_FACTOR"),
            out var f) ? f : 1.0;

    /// <summary>
    /// Scale a throughput threshold (events/sec) by the CI factor.
    /// Higher factor = higher threshold (stricter).
    /// </summary>
    public static double ScaleThroughput(double baseThreshold) =>
        baseThreshold * ThroughputFactor;

    /// <summary>
    /// Scale a latency threshold (ms) by the CI factor.
    /// Higher factor = higher threshold (more permissive).
    /// </summary>
    public static double ScaleLatency(double baseThresholdMs) =>
        baseThresholdMs * LatencyFactor;
}
