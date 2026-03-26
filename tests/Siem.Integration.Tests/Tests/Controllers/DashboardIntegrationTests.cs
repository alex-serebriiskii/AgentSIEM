using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Siem.Api.Controllers;
using Siem.Api.Data.Entities;
using Siem.Api.Storage;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Controllers;

[NotInParallel("database")]
public class DashboardIntegrationTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test]
    public async Task GetTopAgents_AfterRefresh_ReturnsAgentsSortedByEventCount()
    {
        // Seed events for two agents with different counts
        await SeedEvents("agent-busy", 20, hoursAgo: 0);
        await SeedEvents("agent-quiet", 5, hoursAgo: 0);

        // Manually refresh the continuous aggregate
        await RefreshAggregate("agent_activity_hourly");

        await using var db = IntegrationTestFixture.CreateDbContext();
        var controller = new DashboardController(db);

        var result = await controller.GetTopAgents(hours: 24, limit: 10, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var items = JsonSerializer.Deserialize<List<JsonElement>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        items.Should().HaveCountGreaterOrEqualTo(2);
        // First agent should have more events
        var first = items[0].TryGetProperty("totalEvents", out var te1) ? te1
            : items[0].GetProperty("TotalEvents");
        var second = items[1].TryGetProperty("totalEvents", out var te2) ? te2
            : items[1].GetProperty("TotalEvents");
        first.GetInt64().Should().BeGreaterThan(second.GetInt64());
    }

    [Test]
    public async Task GetEventVolume_AfterRefresh_ReturnsBucketedTimeSeries()
    {
        await SeedEvents("agent-1", 10, hoursAgo: 0);
        await RefreshAggregate("agent_activity_hourly");

        await using var db = IntegrationTestFixture.CreateDbContext();
        var controller = new DashboardController(db);

        var result = await controller.GetEventVolume(hours: 24, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var items = JsonSerializer.Deserialize<List<JsonElement>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        items.Should().NotBeEmpty();
        // Each bucket should have a timestamp and event count (property name may be PascalCase or camelCase)
        var hasBucket = items[0].TryGetProperty("bucket", out _) || items[0].TryGetProperty("Bucket", out _);
        hasBucket.Should().BeTrue();
        var hasEventCount = items[0].TryGetProperty("eventCount", out _) || items[0].TryGetProperty("EventCount", out _);
        hasEventCount.Should().BeTrue();
    }

    [Test]
    public async Task GetAlertDistribution_ReturnsGroupedCounts()
    {
        await using var db = IntegrationTestFixture.CreateDbContext();

        db.Alerts.Add(CreateAlert("high", "open"));
        db.Alerts.Add(CreateAlert("high", "open"));
        db.Alerts.Add(CreateAlert("low", "resolved"));
        await db.SaveChangesAsync();

        var controller = new DashboardController(db);
        var result = await controller.GetAlertDistribution(hours: 24, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var items = JsonSerializer.Deserialize<List<JsonElement>>(json, opts)!;

        items.Should().HaveCount(2);
    }

    [Test]
    public async Task GetToolUsage_AfterRefresh_ReturnsToolsSortedByInvocationCount()
    {
        await SeedEventsWithTool("agent-1", "web_search", 15, hoursAgo: 0);
        await SeedEventsWithTool("agent-1", "file_read", 5, hoursAgo: 0);
        await RefreshAggregate("tool_usage_hourly");

        await using var db = IntegrationTestFixture.CreateDbContext();
        var controller = new DashboardController(db);

        var result = await controller.GetToolUsage(hours: 24, limit: 10, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var items = JsonSerializer.Deserialize<List<JsonElement>>(json, opts)!;

        items.Should().HaveCountGreaterOrEqualTo(2);
        var firstTool = items[0].TryGetProperty("invocationCount", out var ic1) ? ic1
            : items[0].GetProperty("InvocationCount");
        var secondTool = items[1].TryGetProperty("invocationCount", out var ic2) ? ic2
            : items[1].GetProperty("InvocationCount");
        firstTool.GetInt64().Should().BeGreaterThan(secondTool.GetInt64());
    }

    [Test]
    public async Task GetTopAgents_ExcludesOldData()
    {
        await SeedEvents("agent-old", 10, hoursAgo: 48);
        await RefreshAggregate("agent_activity_hourly");

        await using var db = IntegrationTestFixture.CreateDbContext();
        var controller = new DashboardController(db);

        var result = await controller.GetTopAgents(hours: 24, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var items = JsonSerializer.Deserialize<List<JsonElement>>(json)!;
        items.Should().BeEmpty();
    }

    private static async Task RefreshAggregate(string viewName)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            IntegrationTestFixture.TimescaleConnectionString);
        await using var cmd = dataSource.CreateCommand(
            $"CALL refresh_continuous_aggregate('{viewName}', NULL, NULL)");
        await cmd.ExecuteNonQueryAsync();
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

    private static AlertEntity CreateAlert(string severity, string status)
    {
        return new AlertEntity
        {
            AlertId = Guid.NewGuid(),
            RuleId = Guid.NewGuid(),
            RuleName = "Test Rule",
            Severity = severity,
            Status = status,
            Title = "Test Alert",
            Detail = "Test detail",
            Context = "{}",
            AgentId = "test-agent",
            SessionId = "test-session",
            TriggeredAt = DateTime.UtcNow,
            Labels = "{}"
        };
    }
}
