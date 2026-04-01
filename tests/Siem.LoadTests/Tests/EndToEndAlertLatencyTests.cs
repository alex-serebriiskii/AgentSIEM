using System.Diagnostics;
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
using Siem.Api.Data.Enums;
using Siem.Api.Kafka;
using Siem.Api.Normalization;
using Siem.Api.Notifications;
using Siem.Api.Services;
using Siem.Api.Storage;
using Siem.LoadTests.Fixtures;
using Siem.LoadTests.Helpers;

namespace Siem.LoadTests.Tests;

[NotInParallel("database")]
public class EndToEndAlertLatencyTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
        await DbHelper.FlushRedisAsync();
    }

    [Test, Timeout(180_000)]
    public async Task EndToEnd_EventToAlert_P95LatencyBaseline(CancellationToken testCt)
    {
        const int eventCount = 1000;

        // Seed 10 SingleEvent rules matching tool_invocation with Critical severity
        var rules = LoadTestRuleFactory.CreateVariedSingleEventRules(10);
        var rulesCache = await LoadTestRuleFactory.CompileAndCacheRulesAsync(rules);

        // Build full pipeline with real alert pipeline and in-memory notification
        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var batchWriter = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 2000, MaxFlushIntervalSeconds = 2 });

        var normalizer = new AgentEventNormalizer(NullLogger<AgentEventNormalizer>.Instance);
        var sessionTracker = Substitute.For<ISessionTracker>();

        var notificationChannel = new InMemoryNotificationChannel(
            "e2e-test", Severity.Low, latency: TimeSpan.FromMilliseconds(1));

        using var sp = BuildServiceProvider();
        // Permissive dedup/throttle so most alerts get through
        var alertConfig = new AlertPipelineConfig
        {
            DeduplicationWindowMinutes = 1,
            ThrottleMaxAlertsPerWindow = 100,
            ThrottleWindowMinutes = 5
        };
        var alertPipeline = CreateAlertPipeline(sp, alertConfig, [notificationChannel]);

        var pipeline = new EventProcessingPipeline(
            rulesCache, normalizer, batchWriter, alertPipeline,
            sessionTracker, NullLogger<EventProcessingPipeline>.Instance);

        // Generate events
        var generator = new LoadTestEventGenerator(agentCount: 50, sessionsPerAgent: 3, seed: 3333);
        var events = generator.GenerateEvents(eventCount, timeSpreadMinutes: 10);

        // Warmup
        for (int i = 0; i < 50; i++)
        {
            var payload = LoadTestEventGenerator.SerializeToKafkaPayload(events[i]);
            await pipeline.ProcessAsync(CreateConsumeResult(payload, i), CancellationToken.None);
        }
        await pipeline.FlushBatchWriter();
        await DbHelper.TruncateAllTablesAsync();
        await DbHelper.FlushRedisAsync();
        notificationChannel.ReceivedAlertIds.Clear();

        // Measured run
        var latencyRecorder = new LatencyRecorder();
        int triggeredCount = 0;

        for (int i = 0; i < eventCount; i++)
        {
            var payload = LoadTestEventGenerator.SerializeToKafkaPayload(events[i]);

            var sw = Stopwatch.StartNew();
            var result = await pipeline.ProcessAsync(
                CreateConsumeResult(payload, i + 50), CancellationToken.None);
            sw.Stop();

            if (result == ProcessingResult.Processed)
            {
                latencyRecorder.Record(sw.Elapsed.TotalMilliseconds);
                triggeredCount++;
            }
        }

        await pipeline.FlushBatchWriter();

        // Assertions
        var stats = latencyRecorder.GetStats();
        var scaledP95 = LoadTestConfig.ScaleLatency(500);
        var scaledP50 = LoadTestConfig.ScaleLatency(100);

        stats.P95.Should().BeLessThan(scaledP95,
            $"P95 end-to-end alert latency (event → notification) should be < {scaledP95}ms; " +
            $"actual P95: {stats.P95:F1}ms, P50: {stats.P50:F1}ms, P99: {stats.P99:F1}ms, " +
            $"Max: {stats.Max:F1}ms");

        stats.P50.Should().BeLessThan(scaledP50,
            $"P50 end-to-end alert latency should be < {scaledP50}ms; " +
            $"actual P50: {stats.P50:F1}ms");

        // At least 10% of events should have triggered alerts
        var triggerRate = (double)triggeredCount / eventCount;
        triggerRate.Should().BeGreaterThan(0.10,
            $"at least 10% of events should trigger alerts; " +
            $"actual: {triggeredCount}/{eventCount} ({triggerRate:P1})");

        // Verify alerts persisted in DB
        await using var db = LoadTestFixture.CreateDbContext();
        var alertsInDb = await db.Alerts.CountAsync();
        alertsInDb.Should().BeGreaterThan(0,
            "alerts should be persisted in the database");

        // Verify notification channel received alerts
        notificationChannel.ReceivedAlertIds.Should().NotBeEmpty(
            "notification channel should have received alert dispatches");
    }

    private static ConsumeResult<string, byte[]> CreateConsumeResult(byte[] value, int offset)
    {
        return new ConsumeResult<string, byte[]>
        {
            Topic = "e2e-latency-test",
            Partition = new Partition(0),
            Offset = new Offset(offset),
            Message = new Message<string, byte[]>
            {
                Key = "e2e-test",
                Value = value,
                Timestamp = new Timestamp(DateTime.UtcNow)
            }
        };
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
