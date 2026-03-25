using FluentAssertions;
using Siem.Api.Alerting;

namespace Siem.Api.Tests.Alerting;

public class AlertDeduplicatorTests
{
    [Test]
    public void AlertPipelineConfig_DeduplicationWindow_ReturnsCorrectTimeSpan()
    {
        var config = new AlertPipelineConfig { DeduplicationWindowMinutes = 30 };

        config.DeduplicationWindow.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Test]
    public void AlertPipelineConfig_ThrottleWindow_ReturnsCorrectTimeSpan()
    {
        var config = new AlertPipelineConfig { ThrottleWindowMinutes = 10 };

        config.ThrottleWindow.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Test]
    public void AlertPipelineConfig_Defaults_AreCorrect()
    {
        var config = new AlertPipelineConfig();

        config.DeduplicationWindowMinutes.Should().Be(15);
        config.ThrottleMaxAlertsPerWindow.Should().Be(10);
        config.ThrottleWindowMinutes.Should().Be(5);
    }
}
