using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Siem.Api.Alerting;
using Siem.Api.Data;
using Siem.Api.Kafka;
using Siem.Api.Normalization;
using Siem.Api.Notifications;
using Siem.Api.Services;
using Siem.Api.Storage;
using Siem.LoadTests.Fixtures;
using Siem.LoadTests.Helpers;

namespace Siem.LoadTests.Tests;

[NotInParallel("database")]
public class MetricsAccuracyLoadTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
        await DbHelper.FlushRedisAsync();
    }

    [Test, Timeout(180_000)]
    public async Task PipelineMetrics_AfterHighThroughputRun_CountersAreConsistent(
        CancellationToken testCt)
    {
        const int eventCount = 10_000;

        // Set up metric collection via MeterListener
        var counters = new ConcurrentDictionary<string, long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name.StartsWith("Siem."))
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            counters.AddOrUpdate(instrument.Name, value, (_, prev) => prev + value);
        });
        listener.Start();

        // Capture baseline values (meters are static, may have residual values)
        await Task.Delay(100); // let listener settle
        var baseline = new Dictionary<string, long>(counters);

        // Seed rules that will trigger on tool_invocation events
        var rules = LoadTestRuleFactory.CreateVariedSingleEventRules(10);
        var rulesCache = await LoadTestRuleFactory.CompileAndCacheRulesAsync(rules);

        // Build pipeline with real alert pipeline
        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var batchWriter = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 2000, MaxFlushIntervalSeconds = 2 });

        var normalizer = new AgentEventNormalizer(NullLogger<AgentEventNormalizer>.Instance);
        var sessionTracker = Substitute.For<ISessionTracker>();

        var notificationChannel = new InMemoryNotificationChannel(
            "metrics-test", Api.Data.Enums.Severity.Low);

        using var sp = BuildServiceProvider();
        var alertConfig = new AlertPipelineConfig
        {
            DeduplicationWindowMinutes = 1, // short window so more alerts get through
            ThrottleMaxAlertsPerWindow = 100,
            ThrottleWindowMinutes = 5
        };
        var alertPipeline = CreateAlertPipeline(sp, alertConfig, [notificationChannel]);

        var pipeline = new EventProcessingPipeline(
            rulesCache, normalizer, batchWriter, alertPipeline,
            sessionTracker, NullLogger<EventProcessingPipeline>.Instance);

        // Generate events and process them
        var generator = new LoadTestEventGenerator(agentCount: 20, sessionsPerAgent: 3, seed: 2222);
        var events = generator.GenerateEvents(eventCount, timeSpreadMinutes: 10);

        int processedCount = 0;
        for (int i = 0; i < eventCount; i++)
        {
            var payload = LoadTestEventGenerator.SerializeToKafkaPayload(events[i]);
            var consumeResult = new ConsumeResult<string, byte[]>
            {
                Topic = "metrics-test",
                Partition = new Partition(0),
                Offset = new Offset(i),
                Message = new Message<string, byte[]>
                {
                    Key = "test",
                    Value = payload,
                    Timestamp = new Timestamp(DateTime.UtcNow)
                }
            };

            var result = await pipeline.ProcessAsync(consumeResult, CancellationToken.None);
            if (result == ProcessingResult.Processed)
                processedCount++;
        }

        await pipeline.FlushBatchWriter();

        // Give metrics time to propagate
        listener.RecordObservableInstruments();
        await Task.Delay(200);

        // Compute deltas from baseline
        long GetDelta(string name)
        {
            var current = counters.GetValueOrDefault(name, 0);
            var baseVal = baseline.GetValueOrDefault(name, 0);
            return current - baseVal;
        }

        var rulesTriggered = GetDelta("siem.rules.triggered");
        var alertsReceived = GetDelta("siem.alerts.received");
        var alertsCreated = GetDelta("siem.alerts.created");
        var alertsDeduped = GetDelta("siem.alerts.deduplicated");
        var alertsThrottled = GetDelta("siem.alerts.throttled");
        var alertsSuppressed = GetDelta("siem.alerts.suppressed");
        var eventsWritten = GetDelta("siem.storage.events_written");

        // Assertions
        rulesTriggered.Should().BeGreaterThan(0,
            "some rules should have triggered against the event stream");

        alertsReceived.Should().BeGreaterThan(0,
            "the alert pipeline should have received some triggered rules");

        // Conservation: received = created + deduped + throttled + suppressed
        var accountedFor = alertsCreated + alertsDeduped + alertsThrottled + alertsSuppressed;
        alertsReceived.Should().Be(accountedFor,
            $"alert pipeline conservation: received ({alertsReceived}) should equal " +
            $"created ({alertsCreated}) + deduped ({alertsDeduped}) + throttled ({alertsThrottled}) " +
            $"+ suppressed ({alertsSuppressed}) = {accountedFor}");

        // Events written should match events processed
        eventsWritten.Should().Be(eventCount,
            $"siem.storage.events_written ({eventsWritten}) should equal events processed ({eventCount})");

        // All counters should be non-negative
        foreach (var (name, value) in counters)
        {
            var delta = value - baseline.GetValueOrDefault(name, 0);
            delta.Should().BeGreaterOrEqualTo(0,
                $"metric {name} delta should be non-negative (was {delta})");
        }
    }

    private static AlertPipeline CreateAlertPipeline(
        IServiceProvider serviceProvider, AlertPipelineConfig config,
        IReadOnlyList<INotificationChannel> channels)
    {
        var dedup = new AlertDeduplicator(LoadTestFixture.RedisMultiplexer, config);
        var throttler = new AlertThrottler(
            LoadTestFixture.RedisMultiplexer, config, NullLogger<AlertThrottler>.Instance);
        var retryWorker = new NotificationRetryWorker(
            NullLogger<NotificationRetryWorker>.Instance, new NotificationRetryConfig());
        var router = new NotificationRouter(
            channels, retryWorker, NullLogger<NotificationRouter>.Instance);

        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var processingScopeFactory = new AlertProcessingScopeFactory(scopeFactory);

        return new AlertPipeline(
            dedup, throttler, processingScopeFactory, router,
            NullLogger<AlertPipeline>.Instance);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SiemDbContext>(options =>
            options.UseNpgsql(LoadTestFixture.TimescaleConnectionString));
        services.AddScoped<SuppressionChecker>();
        services.AddScoped<AlertEnricher>();
        services.AddScoped<AlertPersistence>();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        return services.BuildServiceProvider();
    }
}
