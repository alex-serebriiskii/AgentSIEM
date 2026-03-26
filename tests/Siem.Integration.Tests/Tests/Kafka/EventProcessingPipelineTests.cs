using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using Siem.Api.Alerting;
using Siem.Api.Kafka;
using Siem.Api.Normalization;
using Siem.Api.Services;
using Siem.Api.Storage;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;
using Siem.Rules.Core;

namespace Siem.Integration.Tests.Tests.Kafka;

[NotInParallel("database")]
public class EventProcessingPipelineTests
{
    private const string TestTopic = "pipeline-test-events";

    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test]
    public async Task ProcessAsync_ValidEvent_PersistsToTimescaleDb()
    {
        // Arrange: produce a valid event to Kafka
        var eventId = Guid.NewGuid();
        var eventJson = JsonSerializer.Serialize(new
        {
            eventId,
            timestamp = DateTime.UtcNow,
            sessionId = "sess-001",
            traceId = "trace-001",
            agentId = "agent-001",
            agentName = "TestAgent",
            eventType = "tool_invocation",
            toolName = "search",
            latencyMs = 42.0
        });

        var consumeResult = await ProduceAndConsume(TestTopic, eventJson);

        // Act: process through the pipeline
        var pipeline = CreatePipeline();
        var result = await pipeline.ProcessAsync(consumeResult, CancellationToken.None);

        // Flush to ensure batch write completes
        await pipeline.FlushBatchWriter();

        // Assert: event appears in TimescaleDB
        result.Should().Be(ProcessingResult.NoRulesTriggered);

        await using var conn = new NpgsqlConnection(IntegrationTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("id", eventId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(1);
    }

    [Test]
    public async Task ProcessAsync_MultipleEvents_AllPersistedInBatch()
    {
        var eventIds = new List<Guid>();
        var pipeline = CreatePipeline();

        for (var i = 0; i < 10; i++)
        {
            var eventId = Guid.NewGuid();
            eventIds.Add(eventId);

            var eventJson = JsonSerializer.Serialize(new
            {
                eventId,
                timestamp = DateTime.UtcNow,
                sessionId = $"sess-{i}",
                traceId = $"trace-{i}",
                agentId = "agent-batch",
                agentName = "BatchAgent",
                eventType = "tool_invocation",
                toolName = $"tool-{i}",
                latencyMs = i * 10.0
            });

            var consumeResult = await ProduceAndConsume($"{TestTopic}-batch", eventJson);
            await pipeline.ProcessAsync(consumeResult, CancellationToken.None);
        }

        await pipeline.FlushBatchWriter();

        await using var conn = new NpgsqlConnection(IntegrationTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_events WHERE agent_id = 'agent-batch'";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(10);
    }

    [Test]
    public async Task ProcessAsync_OpenTelemetryEventType_NormalizedCorrectly()
    {
        var eventId = Guid.NewGuid();
        var eventJson = JsonSerializer.Serialize(new
        {
            eventId,
            timestamp = DateTime.UtcNow,
            sessionId = "sess-otel",
            traceId = "trace-otel",
            agentId = "agent-otel",
            agentName = "OtelAgent",
            eventType = "llm.call",  // OpenTelemetry convention
            modelId = "gpt-4",
            inputTokens = 100,
            outputTokens = 200
        });

        var consumeResult = await ProduceAndConsume($"{TestTopic}-otel", eventJson);
        var pipeline = CreatePipeline();
        await pipeline.ProcessAsync(consumeResult, CancellationToken.None);
        await pipeline.FlushBatchWriter();

        await using var conn = new NpgsqlConnection(IntegrationTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT event_type FROM agent_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("id", eventId);
        var eventType = (string)(await cmd.ExecuteScalarAsync())!;
        eventType.Should().Be("llm_call"); // Normalized from llm.call
    }

    [Test]
    public async Task ProcessAsync_LangChainEventType_NormalizedCorrectly()
    {
        var eventId = Guid.NewGuid();
        var eventJson = JsonSerializer.Serialize(new
        {
            eventId,
            timestamp = DateTime.UtcNow,
            sessionId = "sess-lc",
            traceId = "trace-lc",
            agentId = "agent-lc",
            agentName = "LangChainAgent",
            eventType = "on_tool_start",  // LangChain callback
            toolName = "calculator"
        });

        var consumeResult = await ProduceAndConsume($"{TestTopic}-lc", eventJson);
        var pipeline = CreatePipeline();
        await pipeline.ProcessAsync(consumeResult, CancellationToken.None);
        await pipeline.FlushBatchWriter();

        await using var conn = new NpgsqlConnection(IntegrationTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT event_type FROM agent_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("id", eventId);
        var eventType = (string)(await cmd.ExecuteScalarAsync())!;
        eventType.Should().Be("tool_invocation"); // Normalized from on_tool_start
    }

    [Test]
    public async Task ProcessAsync_EventWithExtraProperties_PreservedAsJsonb()
    {
        var eventId = Guid.NewGuid();
        var eventJson = JsonSerializer.Serialize(new
        {
            eventId,
            timestamp = DateTime.UtcNow,
            sessionId = "sess-extra",
            traceId = "trace-extra",
            agentId = "agent-extra",
            agentName = "ExtraAgent",
            eventType = "tool_invocation",
            extra = new Dictionary<string, object>
            {
                ["custom_field"] = "custom_value",
                ["framework_version"] = "2.0"
            }
        });

        var consumeResult = await ProduceAndConsume($"{TestTopic}-extra", eventJson);
        var pipeline = CreatePipeline();
        await pipeline.ProcessAsync(consumeResult, CancellationToken.None);
        await pipeline.FlushBatchWriter();

        await using var conn = new NpgsqlConnection(IntegrationTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT properties->>'custom_field' FROM agent_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("id", eventId);
        var value = (string?)(await cmd.ExecuteScalarAsync());
        value.Should().Be("custom_value");
    }

    [Test]
    public void DeserializeMessage_InvalidJson_ThrowsEventDeserializationException()
    {
        var invalidBytes = Encoding.UTF8.GetBytes("not valid json {{{");
        var consumeResult = CreateConsumeResult("test-topic", invalidBytes);

        var pipeline = CreatePipeline();
        var act = () => pipeline.ProcessAsync(consumeResult, CancellationToken.None);
        act.Should().ThrowAsync<EventDeserializationException>();
    }

    [Test]
    public async Task EndToEnd_KafkaToTimescaleDb_CompletesWithin5Seconds()
    {
        // Phase 1 exit criterion: event published to Kafka appears in
        // agent_events hypertable within 5 seconds.
        var eventId = Guid.NewGuid();
        var eventJson = JsonSerializer.Serialize(new
        {
            eventId,
            timestamp = DateTime.UtcNow,
            sessionId = "sess-latency",
            traceId = "trace-latency",
            agentId = "agent-latency",
            agentName = "LatencyAgent",
            eventType = "tool_invocation",
            toolName = "ping"
        });

        var sw = Stopwatch.StartNew();

        // Produce to Kafka, consume, process through pipeline, flush to DB
        var consumeResult = await ProduceAndConsume($"{TestTopic}-latency", eventJson);
        var pipeline = CreatePipeline();
        await pipeline.ProcessAsync(consumeResult, CancellationToken.None);
        await pipeline.FlushBatchWriter();

        // Verify the event is in TimescaleDB
        await using var conn = new NpgsqlConnection(IntegrationTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("id", eventId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;

        sw.Stop();

        count.Should().Be(1);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            $"end-to-end Kafka->TimescaleDB should complete within 5s, actual: {sw.ElapsedMilliseconds}ms");
    }

    [Test]
    public async Task ProcessAsync_LlmCallWithTokens_AllFieldsPersisted()
    {
        var eventId = Guid.NewGuid();
        var eventJson = JsonSerializer.Serialize(new
        {
            eventId,
            timestamp = DateTime.UtcNow,
            sessionId = "sess-llm",
            traceId = "trace-llm",
            agentId = "agent-llm",
            agentName = "LlmAgent",
            eventType = "llm_call",
            modelId = "claude-3-opus",
            inputTokens = 500,
            outputTokens = 1200,
            latencyMs = 3200.5
        });

        var consumeResult = await ProduceAndConsume($"{TestTopic}-llm", eventJson);
        var pipeline = CreatePipeline();
        await pipeline.ProcessAsync(consumeResult, CancellationToken.None);
        await pipeline.FlushBatchWriter();

        await using var conn = new NpgsqlConnection(IntegrationTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT model_id, input_tokens, output_tokens, latency_ms FROM agent_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("id", eventId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("claude-3-opus");
        reader.GetInt32(1).Should().Be(500);
        reader.GetInt32(2).Should().Be(1200);
        reader.GetDouble(3).Should().BeApproximately(3200.5, 0.1);
    }

    // --- Phase 2: Rule detection tests ---

    [Test]
    public async Task ProcessAsync_WithMatchingRule_ReturnsProcessedAndCallsAlertPipeline()
    {
        // Arrange: insert a rule that matches tool_invocation events
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "Detect Tool Use"));
            await db.SaveChangesAsync();
        }

        var (pipeline, alertPipeline) = await CreatePipelineWithRules();

        var eventJson = JsonSerializer.Serialize(new
        {
            eventId = Guid.NewGuid(),
            timestamp = DateTime.UtcNow,
            sessionId = "sess-rule-match",
            traceId = "trace-rule-match",
            agentId = "agent-001",
            agentName = "RuleTestAgent",
            eventType = "tool_invocation",
            toolName = "search"
        });

        var consumeResult = await ProduceAndConsume($"{TestTopic}-rule-match", eventJson);

        // Act
        var result = await pipeline.ProcessAsync(consumeResult, CancellationToken.None);
        await pipeline.FlushBatchWriter();

        // Assert
        result.Should().Be(ProcessingResult.Processed);
        await alertPipeline.Received(1).ProcessAsync(
            Arg.Is<Evaluator.EvaluationResult>(r => r.Triggered),
            Arg.Any<Siem.Rules.Core.AgentEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_WithNonMatchingRule_ReturnsNoRulesTriggered()
    {
        // Arrange: rule matches tool_invocation, but we send llm_call
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "Detect Tool Use Only"));
            await db.SaveChangesAsync();
        }

        var (pipeline, alertPipeline) = await CreatePipelineWithRules();

        var eventJson = JsonSerializer.Serialize(new
        {
            eventId = Guid.NewGuid(),
            timestamp = DateTime.UtcNow,
            sessionId = "sess-no-match",
            traceId = "trace-no-match",
            agentId = "agent-002",
            agentName = "NoMatchAgent",
            eventType = "llm_call",
            modelId = "gpt-4"
        });

        var consumeResult = await ProduceAndConsume($"{TestTopic}-no-match", eventJson);

        // Act
        var result = await pipeline.ProcessAsync(consumeResult, CancellationToken.None);

        // Assert
        result.Should().Be(ProcessingResult.NoRulesTriggered);
        await alertPipeline.DidNotReceive().ProcessAsync(
            Arg.Any<Evaluator.EvaluationResult>(),
            Arg.Any<Siem.Rules.Core.AgentEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_WithMatchingRule_EventStillPersistsToTimescaleDb()
    {
        // Arrange
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "Persist Check Rule"));
            await db.SaveChangesAsync();
        }

        var (pipeline, _) = await CreatePipelineWithRules();

        var eventId = Guid.NewGuid();
        var eventJson = JsonSerializer.Serialize(new
        {
            eventId,
            timestamp = DateTime.UtcNow,
            sessionId = "sess-persist",
            traceId = "trace-persist",
            agentId = "agent-003",
            agentName = "PersistAgent",
            eventType = "tool_invocation",
            toolName = "calculator"
        });

        var consumeResult = await ProduceAndConsume($"{TestTopic}-persist-rule", eventJson);

        // Act
        var result = await pipeline.ProcessAsync(consumeResult, CancellationToken.None);
        await pipeline.FlushBatchWriter();

        // Assert: rule triggered AND event persisted
        result.Should().Be(ProcessingResult.Processed);

        await using var conn = new NpgsqlConnection(IntegrationTestFixture.TimescaleConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("id", eventId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(1);
    }

    // --- Pipeline factory methods ---

    private EventProcessingPipeline CreatePipeline()
    {
        var dataSource = NpgsqlDataSource.Create(IntegrationTestFixture.TimescaleConnectionString);
        var batchWriter = new BatchEventWriter(
            dataSource,
            NullLogger<BatchEventWriter>.Instance,
            maxBatchSize: 100,
            maxFlushInterval: TimeSpan.FromMinutes(5));

        var normalizer = new AgentEventNormalizer(
            NullLogger<AgentEventNormalizer>.Instance);

        var stateProvider = new RedisStateProvider(IntegrationTestFixture.RedisMultiplexer);
        var rulesCache = new CompiledRulesCache(stateProvider);
        var alertPipeline = Substitute.For<IAlertPipeline>();
        var sessionTracker = Substitute.For<ISessionTracker>();

        return new EventProcessingPipeline(
            rulesCache,
            normalizer,
            batchWriter,
            alertPipeline,
            sessionTracker,
            NullLogger<EventProcessingPipeline>.Instance);
    }

    private async Task<(EventProcessingPipeline Pipeline, IAlertPipeline MockAlert)> CreatePipelineWithRules()
    {
        var dataSource = NpgsqlDataSource.Create(IntegrationTestFixture.TimescaleConnectionString);
        var batchWriter = new BatchEventWriter(
            dataSource,
            NullLogger<BatchEventWriter>.Instance,
            maxBatchSize: 100,
            maxFlushInterval: TimeSpan.FromMinutes(5));

        var normalizer = new AgentEventNormalizer(
            NullLogger<AgentEventNormalizer>.Instance);

        var stateProvider = new RedisStateProvider(IntegrationTestFixture.RedisMultiplexer);
        var rulesCache = new CompiledRulesCache(stateProvider);

        // Load rules from DB, compile, and swap into cache
        await using var db = IntegrationTestFixture.CreateDbContext();
        var ruleLoader = new RuleLoadingService(db, NullLogger<RuleLoadingService>.Instance);
        var rules = await ruleLoader.LoadEnabledRulesAsync();
        var listResolver = Microsoft.FSharp.Core.FuncConvert.FromFunc<Guid, Microsoft.FSharp.Collections.FSharpSet<string>>(
            _ => Microsoft.FSharp.Collections.SetModule.Empty<string>());
        var compiledRules = Engine.compileAllRules(listResolver, rules.ToFSharpList());

        // We need a ListCacheService for SwapEngine — create a minimal one
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var listCache = new ListCacheService(scopeFactory, NullLogger<ListCacheService>.Instance);
        rulesCache.SwapEngine(compiledRules, listCache);

        var alertPipeline = Substitute.For<IAlertPipeline>();
        var sessionTracker = Substitute.For<ISessionTracker>();

        var pipeline = new EventProcessingPipeline(
            rulesCache,
            normalizer,
            batchWriter,
            alertPipeline,
            sessionTracker,
            NullLogger<EventProcessingPipeline>.Instance);

        return (pipeline, alertPipeline);
    }

    private static async Task<ConsumeResult<string, byte[]>> ProduceAndConsume(
        string topic, string json)
    {
        var bootstrapServers = IntegrationTestFixture.KafkaBootstrapServers;

        // Produce
        using var producer = new ProducerBuilder<string, byte[]>(
            new ProducerConfig { BootstrapServers = bootstrapServers })
            .Build();

        await producer.ProduceAsync(topic, new Message<string, byte[]>
        {
            Key = "test-key",
            Value = Encoding.UTF8.GetBytes(json),
            Timestamp = new Timestamp(DateTime.UtcNow)
        });

        // Consume
        using var consumer = new ConsumerBuilder<string, byte[]>(
            new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = $"test-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            }).Build();

        consumer.Subscribe(topic);
        var result = consumer.Consume(TimeSpan.FromSeconds(10));
        consumer.Close();

        return result ?? throw new TimeoutException($"No message consumed from {topic}");
    }

    private static ConsumeResult<string, byte[]> CreateConsumeResult(
        string topic, byte[] value)
    {
        return new ConsumeResult<string, byte[]>
        {
            Topic = topic,
            Partition = new Partition(0),
            Offset = new Offset(0),
            Message = new Message<string, byte[]>
            {
                Key = "test-key",
                Value = value,
                Timestamp = new Timestamp(DateTime.UtcNow)
            }
        };
    }
}
