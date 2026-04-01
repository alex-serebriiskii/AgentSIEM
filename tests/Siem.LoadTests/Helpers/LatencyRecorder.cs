namespace Siem.LoadTests.Helpers;

/// <summary>
/// Collects latency samples and computes percentiles.
/// Thread-safe for concurrent recording.
/// </summary>
public class LatencyRecorder
{
    private readonly List<double> _samples = [];
    private readonly object _lock = new();

    /// <summary>Record a latency sample in milliseconds.</summary>
    public void Record(double ms)
    {
        lock (_lock)
        {
            _samples.Add(ms);
        }
    }

    /// <summary>Compute percentile statistics over all recorded samples.</summary>
    public LatencyStats GetStats()
    {
        lock (_lock)
        {
            if (_samples.Count == 0)
                return new LatencyStats(0, 0, 0, 0, 0, 0);

            var sorted = _samples.OrderBy(x => x).ToList();
            return new LatencyStats(
                Count: sorted.Count,
                P50: Percentile(sorted, 0.50),
                P95: Percentile(sorted, 0.95),
                P99: Percentile(sorted, 0.99),
                Max: sorted[^1],
                Avg: sorted.Average());
        }
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var index = (sorted.Count - 1) * p;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }
}

public record LatencyStats(
    int Count,
    double P50,
    double P95,
    double P99,
    double Max,
    double Avg);
