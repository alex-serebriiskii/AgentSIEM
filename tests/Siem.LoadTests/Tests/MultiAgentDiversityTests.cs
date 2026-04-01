using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Siem.Api.Storage;
using Siem.LoadTests.Fixtures;
using Siem.LoadTests.Helpers;

namespace Siem.LoadTests.Tests;

[NotInParallel("database")]
public class MultiAgentDiversityTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test]
    public async Task WriteDiverseEvents_100kEvents_NoSilentDrops()
    {
        const int eventCount = 100_000;
        const int agentCount = 100;
        const int sessionsPerAgent = 5;

        var generator = new LoadTestEventGenerator(
            agentCount: agentCount,
            sessionsPerAgent: sessionsPerAgent,
            seed: 12345);

        var events = generator.GenerateEvents(eventCount);

        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 2000, MaxFlushIntervalSeconds = 300 });

        foreach (var evt in events)
            await writer.EnqueueAsync(evt);
        await writer.FlushAsync();

        // Assert: all events persisted
        await using var conn = await dataSource.OpenConnectionAsync();

        var totalCount = await ExecuteScalar<long>(conn, "SELECT COUNT(*) FROM agent_events");
        totalCount.Should().Be(eventCount, "all events should be persisted without drops");

        var distinctAgents = await ExecuteScalar<long>(conn, "SELECT COUNT(DISTINCT agent_id) FROM agent_events");
        distinctAgents.Should().Be(agentCount, "all agents should appear");

        var distinctSessions = await ExecuteScalar<long>(conn, "SELECT COUNT(DISTINCT session_id) FROM agent_events");
        distinctSessions.Should().Be(agentCount * sessionsPerAgent, "all sessions should appear");

        var distinctEventTypes = await ExecuteScalar<long>(conn, "SELECT COUNT(DISTINCT event_type) FROM agent_events");
        distinctEventTypes.Should().BeGreaterOrEqualTo(5,
            "weighted distribution with 100k events should hit at least 5 of 7 event types");
    }

    [Test]
    public async Task WriteDiverseEvents_EventTypeDistribution_MatchesWeights()
    {
        const int eventCount = 100_000;

        var generator = new LoadTestEventGenerator(agentCount: 50, sessionsPerAgent: 3, seed: 99999);
        var events = generator.GenerateEvents(eventCount);

        await using var dataSource = NpgsqlDataSource.Create(
            LoadTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            new BatchEventWriterConfig { MaxBatchSize = 2000, MaxFlushIntervalSeconds = 300 });

        foreach (var evt in events)
            await writer.EnqueueAsync(evt);
        await writer.FlushAsync();

        await using var conn = await dataSource.OpenConnectionAsync();

        // Expected weights: tool_invocation 35%, llm_call 25%, rag_retrieval 15%
        var toolCount = await ExecuteScalar<long>(conn,
            "SELECT COUNT(*) FROM agent_events WHERE event_type = 'tool_invocation'");
        var llmCount = await ExecuteScalar<long>(conn,
            "SELECT COUNT(*) FROM agent_events WHERE event_type = 'llm_call'");
        var ragCount = await ExecuteScalar<long>(conn,
            "SELECT COUNT(*) FROM agent_events WHERE event_type = 'rag_retrieval'");

        // Within 20% tolerance of expected
        var toolPct = (double)toolCount / eventCount;
        var llmPct = (double)llmCount / eventCount;
        var ragPct = (double)ragCount / eventCount;

        toolPct.Should().BeInRange(0.28, 0.42, "tool_invocation should be ~35%");
        llmPct.Should().BeInRange(0.20, 0.30, "llm_call should be ~25%");
        ragPct.Should().BeInRange(0.12, 0.18, "rag_retrieval should be ~15%");
    }

    private static async Task<T> ExecuteScalar<T>(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (T)(await cmd.ExecuteScalarAsync())!;
    }
}
