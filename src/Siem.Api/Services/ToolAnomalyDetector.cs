using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Siem.Api.Services;

/// <summary>
/// Result of anomaly detection for a single tool.
/// </summary>
public record ToolAnomaly(
    string ToolName,
    long TodayCount,
    double AvgDailyCount,
    double ZScore);

/// <summary>
/// Background service that periodically checks tool usage against a 7-day baseline.
/// Tools with z-score above the configured threshold are flagged as anomalous.
/// Queries the tool_usage_hourly continuous aggregate for efficient computation.
/// </summary>
public class ToolAnomalyDetector : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ToolAnomalyConfig _config;
    private readonly ILogger<ToolAnomalyDetector> _logger;

    private static readonly Meter Meter = new("Siem.Anomaly");
    private static readonly Counter<long> AnomaliesDetected =
        Meter.CreateCounter<long>("siem.anomalies.detected");

    public ToolAnomalyDetector(
        NpgsqlDataSource dataSource,
        IOptions<ToolAnomalyConfig> config,
        ILogger<ToolAnomalyDetector> logger)
    {
        _dataSource = dataSource;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("ToolAnomalyDetector is disabled");
            return;
        }

        _logger.LogInformation(
            "ToolAnomalyDetector started (interval={IntervalMin}m, threshold={Threshold}, baseline={Days}d)",
            _config.IntervalMinutes, _config.ZScoreThreshold, _config.BaselineDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var anomalies = await DetectAnomaliesAsync(stoppingToken);

                foreach (var anomaly in anomalies)
                {
                    AnomaliesDetected.Add(1);
                    _logger.LogWarning(
                        "Tool usage anomaly detected: tool={ToolName} todayCount={TodayCount} " +
                        "baselineAvg={AvgDailyCount:F1} zScore={ZScore:F2}",
                        anomaly.ToolName, anomaly.TodayCount,
                        anomaly.AvgDailyCount, anomaly.ZScore);
                }

                if (anomalies.Count == 0)
                {
                    _logger.LogDebug("No tool usage anomalies detected");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ToolAnomalyDetector iteration failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_config.IntervalMinutes), stoppingToken);
        }

        _logger.LogInformation("ToolAnomalyDetector stopped");
    }

    /// <summary>
    /// Queries tool_usage_hourly to compare today's tool usage against a multi-day baseline.
    /// Returns tools with z-score exceeding the configured threshold.
    /// </summary>
    internal async Task<List<ToolAnomaly>> DetectAnomaliesAsync(CancellationToken ct)
    {
        var sql = """
            WITH today AS (
                SELECT tool_name,
                       SUM(invocation_count) AS today_count
                FROM tool_usage_hourly
                WHERE bucket >= NOW() - INTERVAL '24 hours'
                GROUP BY tool_name
            ),
            baseline AS (
                SELECT tool_name,
                       AVG(daily_count) AS avg_daily_count,
                       STDDEV(daily_count) AS stddev_daily_count
                FROM (
                    SELECT tool_name,
                           time_bucket('1 day', bucket) AS day,
                           SUM(invocation_count) AS daily_count
                    FROM tool_usage_hourly
                    WHERE bucket >= NOW() - make_interval(days => @baselineDays)
                      AND bucket < NOW() - INTERVAL '24 hours'
                    GROUP BY tool_name, day
                ) daily
                GROUP BY tool_name
            )
            SELECT t.tool_name,
                   t.today_count,
                   b.avg_daily_count,
                   (t.today_count - b.avg_daily_count) / NULLIF(b.stddev_daily_count, 0)
                       AS z_score
            FROM today t
            JOIN baseline b ON b.tool_name = t.tool_name
            WHERE (t.today_count - b.avg_daily_count) / NULLIF(b.stddev_daily_count, 0) > @threshold
            ORDER BY z_score DESC
            """;

        var anomalies = new List<ToolAnomaly>();

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("baselineDays", _config.BaselineDays);
        cmd.Parameters.AddWithValue("threshold", _config.ZScoreThreshold);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var zScore = reader.IsDBNull(3) ? 0.0 : reader.GetDouble(3);
            anomalies.Add(new ToolAnomaly(
                ToolName: reader.GetString(0),
                TodayCount: reader.GetInt64(1),
                AvgDailyCount: reader.GetDouble(2),
                ZScore: zScore));
        }

        return anomalies;
    }

    /// <summary>
    /// Computes z-score for a given value against a mean and standard deviation.
    /// Extracted for unit testing.
    /// </summary>
    public static double ComputeZScore(double value, double mean, double stddev)
    {
        if (stddev == 0) return 0;
        return (value - mean) / stddev;
    }
}
