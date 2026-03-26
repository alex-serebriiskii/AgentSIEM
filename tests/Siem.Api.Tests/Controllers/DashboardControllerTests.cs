using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Siem.Api.Controllers;
using Siem.Api.Data;
using Siem.Api.Tests.Controllers.Helpers;

namespace Siem.Api.Tests.Controllers;

public class DashboardControllerTests : IDisposable
{
    private readonly SiemDbContext _db;
    private readonly DashboardController _controller;

    public DashboardControllerTests()
    {
        _db = DbContextFactory.Create();
        _controller = new DashboardController(_db);
    }

    public void Dispose() => _db.Dispose();

    // --- Alert Distribution (uses EF Core on alerts table — works with InMemory) ---

    [Test]
    public async Task GetAlertDistribution_ReturnsGroupedBySeverityAndStatus()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(severity: "high", status: "open"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(severity: "high", status: "open"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(severity: "low", status: "open"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(severity: "high", status: "resolved"));
        await _db.SaveChangesAsync();

        var result = await _controller.GetAlertDistribution(hours: 24, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var items = JsonSerializer.Deserialize<List<AlertDistributionItem>>(json, opts)!;

        items.Should().HaveCount(3);
        // Ordered by count descending: high/open=2, then low/open=1, high/resolved=1
        items[0].Count.Should().Be(2);
        items[0].Severity.Should().Be("high");
        items[0].Status.Should().Be("open");
    }

    [Test]
    public async Task GetAlertDistribution_ExcludesAlertsOutsideTimeRange()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(triggeredAt: DateTime.UtcNow.AddMinutes(-30)));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(triggeredAt: DateTime.UtcNow.AddHours(-48)));
        await _db.SaveChangesAsync();

        var result = await _controller.GetAlertDistribution(hours: 24, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var items = JsonSerializer.Deserialize<List<AlertDistributionItem>>(json, opts)!;

        // Only 1 alert within the 24-hour window
        items.Should().HaveCount(1);
        items[0].Count.Should().Be(1);
    }

    private record AlertDistributionItem(string Severity, string Status, int Count);

    [Test]
    public async Task GetAlertDistribution_NoAlerts_ReturnsEmptyList()
    {
        var result = await _controller.GetAlertDistribution(ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var items = JsonSerializer.Deserialize<List<JsonElement>>(json)!;
        items.Should().BeEmpty();
    }

    // --- Top Agents, Event Volume, Tool Usage ---
    // These query continuous aggregate views in production.
    // InMemory provider ignores ToView() and treats them as empty sets.
    // Full testing with real data is covered by integration tests.

    [Test]
    public async Task GetTopAgents_EmptyData_ReturnsEmptyList()
    {
        var result = await _controller.GetTopAgents(ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var items = JsonSerializer.Deserialize<List<JsonElement>>(json)!;
        items.Should().BeEmpty();
    }

    [Test]
    public async Task GetEventVolume_EmptyData_ReturnsEmptyList()
    {
        var result = await _controller.GetEventVolume(ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var items = JsonSerializer.Deserialize<List<JsonElement>>(json)!;
        items.Should().BeEmpty();
    }

    [Test]
    public async Task GetToolUsage_EmptyData_ReturnsEmptyList()
    {
        var result = await _controller.GetToolUsage(ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var items = JsonSerializer.Deserialize<List<JsonElement>>(json)!;
        items.Should().BeEmpty();
    }
}
