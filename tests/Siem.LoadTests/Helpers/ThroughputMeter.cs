using System.Diagnostics;

namespace Siem.LoadTests.Helpers;

/// <summary>
/// Tracks throughput (events/sec) over configurable time windows.
/// Thread-safe for concurrent recording.
/// </summary>
public class ThroughputMeter
{
    private readonly TimeSpan _windowSize;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly object _lock = new();
    private readonly List<(long elapsedMs, int count)> _windows = [];
    private int _currentWindowCount;
    private long _currentWindowStart;
    private long _totalCount;

    public ThroughputMeter(TimeSpan? windowSize = null)
    {
        _windowSize = windowSize ?? TimeSpan.FromSeconds(1);
        _currentWindowStart = _stopwatch.ElapsedMilliseconds;
    }

    /// <summary>Record one or more events.</summary>
    public void Record(int count = 1)
    {
        lock (_lock)
        {
            _totalCount += count;
            var now = _stopwatch.ElapsedMilliseconds;
            if (now - _currentWindowStart >= _windowSize.TotalMilliseconds)
            {
                _windows.Add((_currentWindowStart, _currentWindowCount));
                _currentWindowCount = count;
                _currentWindowStart = now;
            }
            else
            {
                _currentWindowCount += count;
            }
        }
    }

    /// <summary>Flush the current window and compute statistics.</summary>
    public ThroughputStats GetStats()
    {
        lock (_lock)
        {
            // Flush current window
            if (_currentWindowCount > 0)
            {
                _windows.Add((_currentWindowStart, _currentWindowCount));
                _currentWindowCount = 0;
                _currentWindowStart = _stopwatch.ElapsedMilliseconds;
            }

            if (_windows.Count == 0)
                return new ThroughputStats(0, 0, 0, 0, 0, 0);

            var rates = _windows
                .Select(w => w.count / _windowSize.TotalSeconds)
                .OrderBy(r => r)
                .ToList();

            var totalElapsed = _stopwatch.Elapsed.TotalSeconds;
            var avgRate = totalElapsed > 0 ? _totalCount / totalElapsed : 0;

            return new ThroughputStats(
                AverageRate: avgRate,
                MinRate: rates[0],
                MaxRate: rates[^1],
                P50Rate: Percentile(rates, 0.50),
                P95Rate: Percentile(rates, 0.95),
                WindowCount: _windows.Count);
        }
    }

    /// <summary>
    /// Get the minimum rate over rolling N-second super-windows.
    /// Useful for asserting "no 5-second window drops below X".
    /// </summary>
    public double GetMinRollingRate(TimeSpan superWindow)
    {
        lock (_lock)
        {
            if (_windows.Count == 0) return 0;

            var windowMs = (long)_windowSize.TotalMilliseconds;
            var superMs = (long)superWindow.TotalMilliseconds;
            double minRate = double.MaxValue;

            for (int i = 0; i < _windows.Count; i++)
            {
                int totalInWindow = 0;
                long startMs = _windows[i].elapsedMs;
                for (int j = i; j < _windows.Count && _windows[j].elapsedMs - startMs < superMs; j++)
                {
                    totalInWindow += _windows[j].count;
                }
                var rate = totalInWindow / superWindow.TotalSeconds;
                if (rate < minRate) minRate = rate;
            }

            return minRate == double.MaxValue ? 0 : minRate;
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

public record ThroughputStats(
    double AverageRate,
    double MinRate,
    double MaxRate,
    double P50Rate,
    double P95Rate,
    int WindowCount);
