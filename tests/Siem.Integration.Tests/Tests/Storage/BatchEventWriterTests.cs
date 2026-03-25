using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Siem.Api.Storage;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Storage;

[NotInParallel("database")]
public class BatchEventWriterTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test]
    public async Task WritesSingleEvent_AppearsInTimescaleDb()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            maxBatchSize: 10, maxFlushInterval: TimeSpan.FromMinutes(5));

        var evt = TestEventFactory.CreateToolInvocation();
        writer.Enqueue(evt);
        await writer.FlushAsync();

        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("id", evt.EventId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(1);
    }

    [Test]
    public async Task WritesBatch_AllEventsAppear()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            maxBatchSize: 100, maxFlushInterval: TimeSpan.FromMinutes(5));

        var eventIds = new List<Guid>();
        for (var i = 0; i < 50; i++)
        {
            var evt = TestEventFactory.CreateToolInvocation();
            eventIds.Add(evt.EventId);
            writer.Enqueue(evt);
        }

        await writer.FlushAsync();

        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_events";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(50);
    }

    [Test]
    public async Task HandlesNullOptionalFields()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            maxBatchSize: 10, maxFlushInterval: TimeSpan.FromMinutes(5));

        // CreateToolInvocation has several None optional fields (modelId, inputTokens, etc.)
        var evt = TestEventFactory.CreateToolInvocation();
        writer.Enqueue(evt);
        await writer.FlushAsync();

        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT model_id, input_tokens, output_tokens FROM agent_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("id", evt.EventId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.IsDBNull(0).Should().BeTrue();  // model_id
        reader.IsDBNull(1).Should().BeTrue();  // input_tokens
        reader.IsDBNull(2).Should().BeTrue();  // output_tokens
    }

    [Test]
    public async Task WritesJsonbProperties_StoredCorrectly()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            maxBatchSize: 10, maxFlushInterval: TimeSpan.FromMinutes(5));

        var props = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["custom_key"] = System.Text.Json.JsonDocument.Parse("\"custom_value\"").RootElement
        };
        var evt = TestEventFactory.CreateWithProperties(properties: props);
        writer.Enqueue(evt);
        await writer.FlushAsync();

        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT properties->>'custom_key' FROM agent_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("id", evt.EventId);
        var value = (string?)(await cmd.ExecuteScalarAsync());
        value.Should().Be("custom_value");
    }

    [Test]
    public async Task BatchWriteThroughput_Exceeds10kEventsPerSecond()
    {
        const int eventCount = 10_000;

        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            maxBatchSize: eventCount, maxFlushInterval: TimeSpan.FromMinutes(5));

        for (var i = 0; i < eventCount; i++)
        {
            writer.Enqueue(TestEventFactory.CreateToolInvocation());
        }

        var sw = Stopwatch.StartNew();
        await writer.FlushAsync();
        sw.Stop();

        // Phase 1 exit criterion: batch writes achieve 10k+ events/second
        var eventsPerSecond = eventCount / sw.Elapsed.TotalSeconds;
        eventsPerSecond.Should().BeGreaterThan(10_000,
            $"COPY protocol should exceed 10k events/sec, actual: {eventsPerSecond:N0} events/sec in {sw.ElapsedMilliseconds}ms");

        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM agent_events";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(eventCount);
    }

    [Test]
    public async Task WritesLlmCallEvent_WithAllFields()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            maxBatchSize: 10, maxFlushInterval: TimeSpan.FromMinutes(5));

        var evt = TestEventFactory.CreateLlmCall(inputTokens: 150, outputTokens: 300);
        writer.Enqueue(evt);
        await writer.FlushAsync();

        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT model_id, input_tokens, output_tokens FROM agent_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("id", evt.EventId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("gpt-4");
        reader.GetInt32(1).Should().Be(150);
        reader.GetInt32(2).Should().Be(300);
    }
}
