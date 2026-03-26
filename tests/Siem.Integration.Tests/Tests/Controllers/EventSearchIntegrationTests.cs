using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Siem.Api.Controllers;
using Siem.Api.Models.Responses;
using Siem.Api.Storage;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Controllers;

[NotInParallel("database")]
public class EventSearchIntegrationTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    private static (List<EventResponse> Data, int TotalCount) ExtractResult(IActionResult result)
    {
        var ok = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = JsonSerializer.Deserialize<List<EventResponse>>(
            root.GetProperty("data").GetRawText(), opts)!;
        return (data, root.GetProperty("totalCount").GetInt32());
    }

    [Test]
    public async Task SearchEvents_TimeRangeFilter_ReturnsOnlyEventsInRange()
    {
        var now = DateTime.UtcNow;
        await SeedEvents("agent-1", 5, hoursAgo: 1);
        await SeedEvents("agent-1", 3, hoursAgo: 48);

        await using var db = IntegrationTestFixture.CreateDbContext();
        var controller = new EventsController(db);

        var result = await controller.SearchEvents(
            start: new DateTimeOffset(now.AddHours(-2), TimeSpan.Zero),
            end: new DateTimeOffset(now, TimeSpan.Zero),
            ct: CancellationToken.None);

        var (data, totalCount) = ExtractResult(result);
        totalCount.Should().Be(5);
    }

    [Test]
    public async Task SearchEvents_AgentFilter_ReturnsOnlyMatchingAgent()
    {
        await SeedEvents("agent-A", 3, hoursAgo: 0);
        await SeedEvents("agent-B", 2, hoursAgo: 0);

        await using var db = IntegrationTestFixture.CreateDbContext();
        var controller = new EventsController(db);

        var result = await controller.SearchEvents(
            start: new DateTimeOffset(DateTime.UtcNow.AddHours(-1), TimeSpan.Zero),
            agent_id: "agent-A",
            ct: CancellationToken.None);

        var (data, totalCount) = ExtractResult(result);
        totalCount.Should().Be(3);
        data.Should().OnlyContain(e => e.AgentId == "agent-A");
    }

    [Test]
    public async Task SearchEvents_EventTypeFilter_ReturnsOnlyMatchingType()
    {
        await SeedEvents("agent-1", 5, hoursAgo: 0);  // tool_invocation by default
        await SeedLlmEvents("agent-1", 3, hoursAgo: 0);

        await using var db = IntegrationTestFixture.CreateDbContext();
        var controller = new EventsController(db);

        var result = await controller.SearchEvents(
            start: new DateTimeOffset(DateTime.UtcNow.AddHours(-1), TimeSpan.Zero),
            event_type: "llm_call",
            ct: CancellationToken.None);

        var (data, totalCount) = ExtractResult(result);
        totalCount.Should().Be(3);
        data.Should().OnlyContain(e => e.EventType == "llm_call");
    }

    [Test]
    public async Task SearchEvents_ToolNameFilter_ReturnsOnlyMatchingTool()
    {
        await SeedEventsWithTool("agent-1", "web_search", 4, hoursAgo: 0);
        await SeedEventsWithTool("agent-1", "file_read", 2, hoursAgo: 0);

        await using var db = IntegrationTestFixture.CreateDbContext();
        var controller = new EventsController(db);

        var result = await controller.SearchEvents(
            start: new DateTimeOffset(DateTime.UtcNow.AddHours(-1), TimeSpan.Zero),
            tool_name: "web_search",
            ct: CancellationToken.None);

        var (data, totalCount) = ExtractResult(result);
        totalCount.Should().Be(4);
        data.Should().OnlyContain(e => e.ToolName == "web_search");
    }

    [Test]
    public async Task SearchEvents_JsonbPropertiesFilter_ReturnsMatching()
    {
        // Seed events with specific JSONB properties
        await SeedEventsWithProperties("agent-1", """{"documentId":"secret-123"}""", 2, hoursAgo: 0);
        await SeedEventsWithProperties("agent-1", """{"documentId":"public-456"}""", 3, hoursAgo: 0);

        await using var db = IntegrationTestFixture.CreateDbContext();
        var controller = new EventsController(db);

        var result = await controller.SearchEvents(
            start: new DateTimeOffset(DateTime.UtcNow.AddHours(-1), TimeSpan.Zero),
            properties: """{"documentId":"secret-123"}""",
            ct: CancellationToken.None);

        var (data, totalCount) = ExtractResult(result);
        totalCount.Should().Be(2);
    }

    [Test]
    public async Task SearchEvents_Pagination_ReturnsCorrectPage()
    {
        await SeedEvents("agent-1", 10, hoursAgo: 0);

        await using var db = IntegrationTestFixture.CreateDbContext();
        var controller = new EventsController(db);

        var result = await controller.SearchEvents(
            start: new DateTimeOffset(DateTime.UtcNow.AddHours(-1), TimeSpan.Zero),
            page: 2,
            pageSize: 3,
            ct: CancellationToken.None);

        var ok = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("page").GetInt32().Should().Be(2);
        root.GetProperty("pageSize").GetInt32().Should().Be(3);
        root.GetProperty("totalCount").GetInt32().Should().Be(10);
        root.GetProperty("totalPages").GetInt32().Should().Be(4);
        root.GetProperty("data").GetArrayLength().Should().Be(3);
    }

    [Test]
    public async Task SearchEvents_OrderedByTimestampDescending()
    {
        await SeedEvents("agent-1", 5, hoursAgo: 0);

        await using var db = IntegrationTestFixture.CreateDbContext();
        var controller = new EventsController(db);

        var result = await controller.SearchEvents(
            start: new DateTimeOffset(DateTime.UtcNow.AddHours(-1), TimeSpan.Zero),
            ct: CancellationToken.None);

        var (data, _) = ExtractResult(result);
        for (int i = 1; i < data.Count; i++)
        {
            data[i - 1].Timestamp.Should().BeOnOrAfter(data[i].Timestamp);
        }
    }

    private static async Task SeedEvents(string agentId, int count, int hoursAgo)
    {
        await SeedEventsWithTool(agentId, "default-tool", count, hoursAgo);
    }

    private static async Task SeedEventsWithTool(string agentId, string toolName, int count, int hoursAgo)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            maxBatchSize: count + 10, maxFlushInterval: TimeSpan.FromMinutes(5));

        var baseTime = DateTime.UtcNow.AddHours(-hoursAgo).AddMinutes(-count);
        for (int i = 0; i < count; i++)
        {
            var evt = TestEventFactory.CreateToolInvocation(
                agentId: agentId,
                toolName: toolName,
                timestamp: baseTime.AddSeconds(i));
            writer.Enqueue(evt);
        }
        await writer.FlushAsync();
    }

    private static async Task SeedLlmEvents(string agentId, int count, int hoursAgo)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var writer = new BatchEventWriter(
            dataSource, NullLogger<BatchEventWriter>.Instance,
            maxBatchSize: count + 10, maxFlushInterval: TimeSpan.FromMinutes(5));

        var baseTime = DateTime.UtcNow.AddHours(-hoursAgo).AddMinutes(-count);
        for (int i = 0; i < count; i++)
        {
            var evt = TestEventFactory.CreateLlmCall(
                agentId: agentId,
                sessionId: $"sess-{Guid.NewGuid():N}");
            writer.Enqueue(evt);
        }
        await writer.FlushAsync();
    }

    private static async Task SeedEventsWithProperties(string agentId, string propertiesJson, int count, int hoursAgo)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();

        var baseTime = DateTime.UtcNow.AddHours(-hoursAgo).AddMinutes(-count);
        for (int i = 0; i < count; i++)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO agent_events (event_id, timestamp, session_id, trace_id, agent_id, agent_name,
                    event_type, properties)
                VALUES (@eid, @ts, @sid, @tid, @aid, @aname, 'tool_invocation', @props::jsonb)
                """;
            cmd.Parameters.AddWithValue("eid", Guid.NewGuid());
            cmd.Parameters.AddWithValue("ts", baseTime.AddSeconds(i));
            cmd.Parameters.AddWithValue("sid", "test-session");
            cmd.Parameters.AddWithValue("tid", $"trace-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("aid", agentId);
            cmd.Parameters.AddWithValue("aname", "TestAgent");
            cmd.Parameters.AddWithValue("props", propertiesJson);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
