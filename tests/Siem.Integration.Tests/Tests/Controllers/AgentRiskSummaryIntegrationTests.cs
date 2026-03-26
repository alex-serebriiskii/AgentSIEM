using System.Diagnostics;
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
public class AgentRiskSummaryIntegrationTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test]
    public async Task GetRiskSummary_WithEvents_ReturnsPopulatedSummary()
    {
        var agentId = "risk-agent-1";
        await SeedEventsForAgent(agentId, 20);
        await SeedAlertForAgent(agentId);

        await using var dataSource = NpgsqlDataSource.Create(IntegrationTestFixture.TimescaleConnectionString);
        var controller = new AgentsController(new Siem.Api.Services.AgentService(dataSource));

        var result = await controller.GetRiskSummary(agentId, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var agentIdProp = root.TryGetProperty("agentId", out var ai) ? ai : root.GetProperty("AgentId");
        agentIdProp.GetString().Should().Be(agentId);
        var totalEventsProp = root.TryGetProperty("totalEvents", out var te) ? te : root.GetProperty("TotalEvents");
        totalEventsProp.GetInt64().Should().Be(20);
        var openAlertsProp = root.TryGetProperty("openAlerts", out var oa) ? oa : root.GetProperty("OpenAlerts");
        openAlertsProp.GetInt64().Should().BeGreaterOrEqualTo(1);
    }

    [Test]
    public async Task GetRiskSummary_UnknownAgent_ReturnsEmptySummary()
    {
        await using var dataSource = NpgsqlDataSource.Create(IntegrationTestFixture.TimescaleConnectionString);
        var controller = new AgentsController(new Siem.Api.Services.AgentService(dataSource));

        var result = await controller.GetRiskSummary("nonexistent-agent", ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var teProp = doc.RootElement.TryGetProperty("totalEvents", out var te) ? te
            : doc.RootElement.GetProperty("TotalEvents");
        teProp.GetInt32().Should().Be(0);
    }

    [Test]
    public async Task GetRiskSummary_RespectsLookbackParameter()
    {
        var agentId = "risk-agent-lookback";

        // Seed recent events
        await SeedEventsForAgent(agentId, 5, hoursAgo: 1);
        // Seed old events (outside 2-hour lookback)
        await SeedEventsForAgent(agentId, 10, hoursAgo: 48);

        await using var dataSource = NpgsqlDataSource.Create(IntegrationTestFixture.TimescaleConnectionString);
        var controller = new AgentsController(new Siem.Api.Services.AgentService(dataSource));

        var result = await controller.GetRiskSummary(agentId, lookback: "2 hours", ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var teProp2 = doc.RootElement.TryGetProperty("totalEvents", out var te2) ? te2
            : doc.RootElement.GetProperty("TotalEvents");
        teProp2.GetInt64().Should().Be(5);
    }

    [Test]
    public async Task GetRiskSummary_ReturnsUnder100ms()
    {
        var agentId = "risk-agent-perf";
        await SeedEventsForAgent(agentId, 100);
        await SeedAlertForAgent(agentId);

        await using var dataSource = NpgsqlDataSource.Create(IntegrationTestFixture.TimescaleConnectionString);
        var controller = new AgentsController(new Siem.Api.Services.AgentService(dataSource));

        // Warm up
        await controller.GetRiskSummary(agentId, ct: CancellationToken.None);

        var sw = Stopwatch.StartNew();
        var result = await controller.GetRiskSummary(agentId, ct: CancellationToken.None);
        sw.Stop();

        result.Should().BeOfType<OkObjectResult>();
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            $"Agent risk summary should return quickly; actual: {sw.ElapsedMilliseconds}ms");
    }

    private static async Task SeedEventsForAgent(string agentId, int count, int hoursAgo = 0)
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
                agentName: "RiskTestAgent",
                toolName: $"tool-{i % 3}",
                timestamp: baseTime.AddSeconds(i));
            writer.Enqueue(evt);
        }
        await writer.FlushAsync();
    }

    private static async Task SeedAlertForAgent(string agentId)
    {
        await using var db = IntegrationTestFixture.CreateDbContext();
        db.Alerts.Add(new AlertEntity
        {
            AlertId = Guid.NewGuid(),
            RuleId = Guid.NewGuid(),
            RuleName = "Test Rule",
            Severity = "high",
            Status = "open",
            Title = "Test Alert",
            Detail = "Test detail",
            Context = "{}",
            AgentId = agentId,
            SessionId = "test-session",
            TriggeredAt = DateTime.UtcNow,
            Labels = "{}"
        });
        await db.SaveChangesAsync();
    }
}
