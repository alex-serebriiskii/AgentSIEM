namespace Siem.Api.Services;

public class ToolAnomalyConfig
{
    /// <summary>How often to run anomaly detection (in minutes).</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Z-score threshold above which a tool is flagged as anomalous.</summary>
    public double ZScoreThreshold { get; set; } = 2.0;

    /// <summary>Number of days of history to use for the baseline.</summary>
    public int BaselineDays { get; set; } = 7;

    /// <summary>Enable or disable the anomaly detector.</summary>
    public bool Enabled { get; set; } = true;
}
