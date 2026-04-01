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
public class KafkaConsumerLagTests
{
    private const string TestTopic = "load-test-lag";

    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
        await DbHelper.FlushRedisAsync();
    }

    [Test, Timeout(120_000)]
    public async Task KafkaConsumer_10kEvents_ConsumerKeepsPace(CancellationToken testCt)
    {
        const int eventCount = 10_000;

        var generator = new LoadTestEventGenerator(agentCount: 30, sessionsPerAgent: 2, seed: 8888);
        var events = generator.GenerateEvents(eventCount, timeSpreadMinutes: 5);

        var bootstrapServers = LoadTestFixture.KafkaBootstrapServers;
        var uniqueTopic = $"{TestTopic}-{Guid.NewGuid():N}";

        // Produce all events to Kafka
        var produceWatch = Stopwatch.StartNew();
        using (var producer = new ProducerBuilder<string, byte[]>(
            new ProducerConfig { BootstrapServers = bootstrapServers })
            .Build())
        {
            foreach (var evt in events)
            {
                var payload = LoadTestEventGenerator.SerializeToKafkaPayload(evt);
                await producer.ProduceAsync(uniqueTopic, new Message<string, byte[]>
                {
                    Key = evt.AgentId,
                    Value = payload,
                    Timestamp = new Timestamp(DateTime.UtcNow)
                });
            }
            producer.Flush(TimeSpan.FromSeconds(10));
        }
        produceWatch.Stop();

        // Build pipeline
        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var batchWriter = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 1000, MaxFlushIntervalSeconds = 1 });

        var normalizer = new AgentEventNormalizer(NullLogger<AgentEventNormalizer>.Instance);
        var stateProvider = new RedisStateProvider(LoadTestFixture.RedisMultiplexer);
        var rulesCache = new CompiledRulesCache(stateProvider);
        var alertPipeline = Substitute.For<IAlertPipeline>();
        var sessionTracker = Substitute.For<ISessionTracker>();

        var pipeline = new EventProcessingPipeline(
            rulesCache, normalizer, batchWriter, alertPipeline,
            sessionTracker, NullLogger<EventProcessingPipeline>.Instance);

        // Consume and process all events
        var consumeWatch = Stopwatch.StartNew();
        int consumed = 0;

        using var consumer = new ConsumerBuilder<string, byte[]>(
            new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = $"load-test-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
                FetchMaxBytes = 1_048_576
            }).Build();

        consumer.Subscribe(uniqueTopic);

        var scaledTimeout = (int)LoadTestConfig.ScaleLatency(30_000);
        var deadline = DateTime.UtcNow.AddMilliseconds(scaledTimeout);

        while (consumed < eventCount && DateTime.UtcNow < deadline)
        {
            var msg = consumer.Consume(TimeSpan.FromSeconds(5));
            if (msg == null) continue;

            await pipeline.ProcessAsync(msg, CancellationToken.None);
            consumed++;

            // Periodic flush
            if (consumed % 500 == 0)
                await pipeline.FlushBatchWriter();
        }

        await pipeline.FlushBatchWriter();
        consumer.Close();
        consumeWatch.Stop();

        // Assertions
        consumed.Should().Be(eventCount,
            $"all {eventCount} events should be consumed within timeout; actual: {consumed}");

        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_events";
        var dbCount = (long)(await cmd.ExecuteScalarAsync())!;
        dbCount.Should().Be(eventCount,
            $"all {eventCount} events should be persisted to TimescaleDB; actual: {dbCount}");

        var lagSeconds = consumeWatch.Elapsed.TotalSeconds;
        var scaledLagLimit = LoadTestConfig.ScaleLatency(10_000) / 1000.0;
        lagSeconds.Should().BeLessThan(scaledLagLimit,
            $"consumer should process {eventCount} events within {scaledLagLimit:F0}s; " +
            $"actual: {lagSeconds:F1}s (produce: {produceWatch.Elapsed.TotalSeconds:F1}s, " +
            $"consume+process: {consumeWatch.Elapsed.TotalSeconds:F1}s)");
    }

    [Test, Timeout(120_000)]
    public async Task KafkaConsumer_10kEvents_ZeroDeadLettered(CancellationToken testCt)
    {
        const int eventCount = 10_000;

        var generator = new LoadTestEventGenerator(agentCount: 20, sessionsPerAgent: 2, seed: 9999);
        var events = generator.GenerateEvents(eventCount, timeSpreadMinutes: 5);

        var bootstrapServers = LoadTestFixture.KafkaBootstrapServers;
        var uniqueTopic = $"{TestTopic}-dl-{Guid.NewGuid():N}";
        var dlTopic = $"{uniqueTopic}.dead-letter";

        // Produce all valid events
        using (var producer = new ProducerBuilder<string, byte[]>(
            new ProducerConfig { BootstrapServers = bootstrapServers })
            .Build())
        {
            foreach (var evt in events)
            {
                var payload = LoadTestEventGenerator.SerializeToKafkaPayload(evt);
                await producer.ProduceAsync(uniqueTopic, new Message<string, byte[]>
                {
                    Key = evt.AgentId,
                    Value = payload,
                    Timestamp = new Timestamp(DateTime.UtcNow)
                });
            }
            producer.Flush(TimeSpan.FromSeconds(10));
        }

        // Consume and process
        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var batchWriter = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 1000, MaxFlushIntervalSeconds = 1 });

        var normalizer = new AgentEventNormalizer(NullLogger<AgentEventNormalizer>.Instance);
        var stateProvider = new RedisStateProvider(LoadTestFixture.RedisMultiplexer);
        var rulesCache = new CompiledRulesCache(stateProvider);
        var alertPipeline = Substitute.For<IAlertPipeline>();
        var sessionTracker = Substitute.For<ISessionTracker>();

        var pipeline = new EventProcessingPipeline(
            rulesCache, normalizer, batchWriter, alertPipeline,
            sessionTracker, NullLogger<EventProcessingPipeline>.Instance);

        using var consumer = new ConsumerBuilder<string, byte[]>(
            new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = $"dl-test-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            }).Build();

        consumer.Subscribe(uniqueTopic);
        int consumed = 0;
        int errors = 0;
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (consumed < eventCount && DateTime.UtcNow < deadline)
        {
            var msg = consumer.Consume(TimeSpan.FromSeconds(5));
            if (msg == null) continue;

            try
            {
                await pipeline.ProcessAsync(msg, CancellationToken.None);
            }
            catch
            {
                errors++;
            }
            consumed++;

            if (consumed % 500 == 0)
                await pipeline.FlushBatchWriter();
        }

        await pipeline.FlushBatchWriter();
        consumer.Close();

        errors.Should().Be(0,
            $"all events are valid — zero should fail processing; actual errors: {errors}");
    }
}
