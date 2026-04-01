using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Siem.Api.Alerting;
using Siem.Api.Kafka;
using Siem.Api.Normalization;
using Siem.Api.Services;
using Siem.Api.Storage;
using Siem.LoadTests.Fixtures;
using Siem.LoadTests.Helpers;

namespace Siem.LoadTests.Tests;

[NotInParallel("database")]
public class SustainedPipelineThroughputTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
        await DbHelper.FlushRedisAsync();
    }

    [Test, Timeout(300_000)] // 5 minute timeout
    public async Task SustainedThroughput_60Seconds_Exceeds10kEventsPerSecond(CancellationToken testCt)
    {
        const int durationSeconds = 60;
        const int targetEventsPerSecond = 10_000;
        var totalEvents = durationSeconds * targetEventsPerSecond; // 600,000

        // Pre-generate all events as simulated ConsumeResults (no actual Kafka)
        var generator = new LoadTestEventGenerator(agentCount: 50, sessionsPerAgent: 3, seed: 42);
        var events = generator.GenerateEvents(totalEvents, timeSpreadMinutes: durationSeconds / 6);

        var consumeResults = events.Select((evt, i) => CreateConsumeResult(
            LoadTestEventGenerator.SerializeToKafkaPayload(evt), i)).ToList();

        // Seed rules and compile
        var singleEventRules = LoadTestRuleFactory.CreateVariedSingleEventRules(15);
        var rulesCache = await LoadTestRuleFactory.CompileAndCacheRulesAsync(singleEventRules);

        // Build pipeline with real batch writer, normalizer, rules; mock alert pipeline
        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var batchWriter = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 2000, MaxFlushIntervalSeconds = 2 });

        var normalizer = new AgentEventNormalizer(NullLogger<AgentEventNormalizer>.Instance);
        var alertPipeline = Substitute.For<IAlertPipeline>();
        var sessionTracker = Substitute.For<ISessionTracker>();

        var pipeline = new EventProcessingPipeline(
            rulesCache, normalizer, batchWriter, alertPipeline,
            sessionTracker, NullLogger<EventProcessingPipeline>.Instance);

        // Warmup: process 1000 events
        for (int i = 0; i < 1000; i++)
            await pipeline.ProcessAsync(consumeResults[i], CancellationToken.None);
        await pipeline.FlushBatchWriter();
        await DbHelper.TruncateAllTablesAsync();

        // Measured run
        var meter = new ThroughputMeter(TimeSpan.FromSeconds(1));
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < totalEvents; i++)
        {
            await pipeline.ProcessAsync(consumeResults[i], CancellationToken.None);
            meter.Record();
        }

        await pipeline.FlushBatchWriter();
        sw.Stop();

        var stats = meter.GetStats();
        var scaledThreshold = LoadTestConfig.ScaleThroughput(targetEventsPerSecond);
        var scaledMinThreshold = LoadTestConfig.ScaleThroughput(5_000);

        stats.AverageRate.Should().BeGreaterThan(scaledThreshold,
            $"average throughput should exceed {scaledThreshold:N0} events/sec; " +
            $"actual: {stats.AverageRate:N0} events/sec over {sw.Elapsed.TotalSeconds:F1}s");

        var minRollingRate = meter.GetMinRollingRate(TimeSpan.FromSeconds(5));
        minRollingRate.Should().BeGreaterThan(scaledMinThreshold,
            $"no 5-second window should drop below {scaledMinThreshold:N0} events/sec; " +
            $"min rolling rate: {minRollingRate:N0}");

        // Verify all events persisted
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_events";
        var dbCount = (long)(await cmd.ExecuteScalarAsync())!;
        dbCount.Should().Be(totalEvents);
    }

    [Test, Timeout(120_000)]
    public async Task BatchWriterThroughput_Sustained30Seconds_StableRate(CancellationToken testCt)
    {
        const int durationSeconds = 30;
        const int batchSize = 500;
        const int targetEventsPerSecond = 10_000;

        var generator = new LoadTestEventGenerator(agentCount: 30, sessionsPerAgent: 3, seed: 7777);

        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = batchSize, MaxFlushIntervalSeconds = 1 });

        var meter = new ThroughputMeter(TimeSpan.FromSeconds(1));
        var sw = Stopwatch.StartNew();
        long totalWritten = 0;

        while (sw.Elapsed.TotalSeconds < durationSeconds)
        {
            var events = generator.GenerateEvents(batchSize, timeSpreadMinutes: 5);
            foreach (var evt in events)
                await writer.EnqueueAsync(evt);
            await writer.FlushAsync();
            meter.Record(batchSize);
            totalWritten += batchSize;
        }

        sw.Stop();
        var stats = meter.GetStats();
        var scaledThreshold = LoadTestConfig.ScaleThroughput(targetEventsPerSecond);

        stats.AverageRate.Should().BeGreaterThan(scaledThreshold,
            $"sustained batch write rate should exceed {scaledThreshold:N0} events/sec; " +
            $"actual: {stats.AverageRate:N0} events/sec, total: {totalWritten:N0} in {sw.Elapsed.TotalSeconds:F1}s");

        // Verify count
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_events";
        var dbCount = (long)(await cmd.ExecuteScalarAsync())!;
        dbCount.Should().Be(totalWritten);
    }

    private static ConsumeResult<string, byte[]> CreateConsumeResult(byte[] value, int offset)
    {
        return new ConsumeResult<string, byte[]>
        {
            Topic = "load-test-events",
            Partition = new Partition(0),
            Offset = new Offset(offset),
            Message = new Message<string, byte[]>
            {
                Key = "load-test",
                Value = value,
                Timestamp = new Timestamp(DateTime.UtcNow)
            }
        };
    }
}
