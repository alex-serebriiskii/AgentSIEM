using FluentAssertions;
using Siem.Api.Services;

namespace Siem.Api.Tests.Services;

public class ToolAnomalyDetectorTests
{
    [Test]
    public void ComputeZScore_NormalCase_ReturnsCorrectValue()
    {
        // z = (value - mean) / stddev = (150 - 100) / 25 = 2.0
        var result = ToolAnomalyDetector.ComputeZScore(150, 100, 25);
        result.Should().BeApproximately(2.0, 0.001);
    }

    [Test]
    public void ComputeZScore_ZeroStddev_ReturnsZero()
    {
        // Division by zero protection — all baseline days had the same count
        var result = ToolAnomalyDetector.ComputeZScore(150, 100, 0);
        result.Should().Be(0);
    }

    [Test]
    public void ComputeZScore_BelowMean_ReturnsNegative()
    {
        // z = (50 - 100) / 25 = -2.0
        var result = ToolAnomalyDetector.ComputeZScore(50, 100, 25);
        result.Should().BeApproximately(-2.0, 0.001);
    }

    [Test]
    public void ComputeZScore_AtMean_ReturnsZero()
    {
        var result = ToolAnomalyDetector.ComputeZScore(100, 100, 25);
        result.Should().BeApproximately(0.0, 0.001);
    }

    [Test]
    public void ComputeZScore_HighAnomaly_ExceedsThreshold()
    {
        // z = (300 - 100) / 50 = 4.0 (well above 2.0 threshold)
        var result = ToolAnomalyDetector.ComputeZScore(300, 100, 50);
        result.Should().BeGreaterThan(2.0);
    }

    [Test]
    public void ToolAnomaly_Record_StoresAllFields()
    {
        var anomaly = new ToolAnomaly("web_search", 500, 100.5, 3.97);
        anomaly.ToolName.Should().Be("web_search");
        anomaly.TodayCount.Should().Be(500);
        anomaly.AvgDailyCount.Should().Be(100.5);
        anomaly.ZScore.Should().BeApproximately(3.97, 0.001);
    }
}
