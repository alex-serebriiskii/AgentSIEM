using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Prometheus;

namespace Siem.Integration.Tests.Tests.Observability;

/// <summary>
/// Verifies that Prometheus metrics infrastructure is correctly configured
/// and that pipeline metric instruments are defined.
/// </summary>
public class PrometheusEndpointTests
{
    [Test]
    public void PipelineMeter_DefinesExpectedInstruments()
    {
        // Verify that the Siem meters exist and have expected instruments
        // by creating a listener that captures instrument names
        var instruments = new List<string>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name.StartsWith("Siem."))
            {
                instruments.Add($"{instrument.Meter.Name}:{instrument.Name}");
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.Start();

        // Force static constructors to run — typeof() alone doesn't trigger them
        RuntimeHelpers.RunClassConstructor(typeof(Siem.Api.Kafka.EventProcessingPipeline).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(Siem.Api.Alerting.AlertPipeline).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(Siem.Api.Kafka.KafkaConsumerWorker).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(Siem.Api.Notifications.NotificationRouter).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(Siem.Api.Services.ToolAnomalyDetector).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(Siem.Api.Storage.BatchEventWriter).TypeHandle);

        instruments.Should().Contain(i => i.Contains("siem.rules.triggered"));
        instruments.Should().Contain(i => i.Contains("siem.events.consumed"));
        instruments.Should().Contain(i => i.Contains("siem.alerts.created"));
        instruments.Should().Contain(i => i.Contains("siem.notifications.sent"));
        instruments.Should().Contain(i => i.Contains("siem.anomalies.detected"));
        instruments.Should().Contain(i => i.Contains("siem.storage.events_written"));
    }

    [Test]
    public void PrometheusMetricsRegistry_IsAccessible()
    {
        // Verify that the default Prometheus registry exists and can be scraped
        var registry = Metrics.DefaultRegistry;
        registry.Should().NotBeNull();
    }

    [Test]
    public async Task PrometheusMetrics_CanBeExportedAsText()
    {
        // Verify that the registry can export metrics in Prometheus text format
        using var stream = new MemoryStream();
        await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream);
        stream.Length.Should().BeGreaterThan(0,
            "Prometheus metrics should produce non-empty output");
    }
}
