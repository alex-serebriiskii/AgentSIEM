using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Siem.Api.Controllers;
using Siem.Api.Data;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;
using Siem.Api.Tests.Controllers.Helpers;

namespace Siem.Api.Tests.Controllers;

public class AlertsControllerTests : IDisposable
{
    private readonly SiemDbContext _db;
    private readonly AlertsController _controller;

    public AlertsControllerTests()
    {
        _db = DbContextFactory.Create();
        _controller = new AlertsController(_db);
    }

    public void Dispose() => _db.Dispose();

    // Helper to extract paginated response data
    private static (List<AlertResponse> Data, int Page, int PageSize, int TotalCount, int TotalPages)
        ExtractPaginatedResult(IActionResult result)
    {
        var ok = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var data = JsonSerializer.Deserialize<List<AlertResponse>>(
            root.GetProperty("data").GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return (
            data,
            root.GetProperty("page").GetInt32(),
            root.GetProperty("pageSize").GetInt32(),
            root.GetProperty("totalCount").GetInt32(),
            root.GetProperty("totalPages").GetInt32()
        );
    }

    // --- ListAlerts ---

    [Test]
    public async Task ListAlerts_ReturnsAllOrderedByTriggeredAt()
    {
        var older = TestEntityBuilders.CreateAlert(triggeredAt: DateTime.UtcNow.AddHours(-2));
        var newer = TestEntityBuilders.CreateAlert(triggeredAt: DateTime.UtcNow.AddHours(-1));
        _db.Alerts.AddRange(older, newer);
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, null, null, ct: CancellationToken.None);

        var (alerts, page, pageSize, totalCount, _) = ExtractPaginatedResult(result);
        alerts.Should().HaveCount(2);
        alerts[0].AlertId.Should().Be(newer.AlertId);
        alerts[1].AlertId.Should().Be(older.AlertId);
        page.Should().Be(1);
        pageSize.Should().Be(50);
        totalCount.Should().Be(2);
    }

    [Test]
    public async Task ListAlerts_FilterByStatus_ReturnsOnlyMatching()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(status: "open"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(status: "acknowledged"));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts("open", null, null, ct: CancellationToken.None);

        var (alerts, _, _, totalCount, _) = ExtractPaginatedResult(result);
        alerts.Should().HaveCount(1);
        alerts[0].Status.Should().Be("open");
        totalCount.Should().Be(1);
    }

    [Test]
    public async Task ListAlerts_FilterBySeverity_ReturnsOnlyMatching()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(severity: "high"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(severity: "low"));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, "high", null, ct: CancellationToken.None);

        var (alerts, _, _, totalCount, _) = ExtractPaginatedResult(result);
        alerts.Should().HaveCount(1);
        alerts[0].Severity.Should().Be("high");
        totalCount.Should().Be(1);
    }

    [Test]
    public async Task ListAlerts_FilterByAgentId_ReturnsOnlyMatching()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(agentId: "agent-A"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(agentId: "agent-B"));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, null, "agent-A", ct: CancellationToken.None);

        var (alerts, _, _, totalCount, _) = ExtractPaginatedResult(result);
        alerts.Should().HaveCount(1);
        alerts[0].AgentId.Should().Be("agent-A");
        totalCount.Should().Be(1);
    }

    [Test]
    public async Task ListAlerts_EmptyDatabase_ReturnsEmptyList()
    {
        var result = await _controller.ListAlerts(null, null, null, ct: CancellationToken.None);

        var (alerts, page, _, totalCount, totalPages) = ExtractPaginatedResult(result);
        alerts.Should().BeEmpty();
        page.Should().Be(1);
        totalCount.Should().Be(0);
        totalPages.Should().Be(0);
    }

    // --- Pagination ---

    [Test]
    public async Task ListAlerts_DefaultPagination_ReturnsFirstPage()
    {
        for (int i = 0; i < 3; i++)
            _db.Alerts.Add(TestEntityBuilders.CreateAlert());
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, null, null, ct: CancellationToken.None);

        var (alerts, page, pageSize, totalCount, totalPages) = ExtractPaginatedResult(result);
        alerts.Should().HaveCount(3);
        page.Should().Be(1);
        pageSize.Should().Be(50);
        totalCount.Should().Be(3);
        totalPages.Should().Be(1);
    }

    [Test]
    public async Task ListAlerts_ExplicitPageAndSize_ReturnsPaginatedResults()
    {
        for (int i = 0; i < 5; i++)
            _db.Alerts.Add(TestEntityBuilders.CreateAlert(
                triggeredAt: DateTime.UtcNow.AddMinutes(-i)));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, null, null, page: 2, pageSize: 2, ct: CancellationToken.None);

        var (alerts, page, pageSize, totalCount, totalPages) = ExtractPaginatedResult(result);
        alerts.Should().HaveCount(2);
        page.Should().Be(2);
        pageSize.Should().Be(2);
        totalCount.Should().Be(5);
        totalPages.Should().Be(3);
    }

    [Test]
    public async Task ListAlerts_PageBeyondData_ReturnsEmpty()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert());
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, null, null, page: 5, pageSize: 10, ct: CancellationToken.None);

        var (alerts, _, _, totalCount, _) = ExtractPaginatedResult(result);
        alerts.Should().BeEmpty();
        totalCount.Should().Be(1);
    }

    // --- GetAlert ---

    [Test]
    public async Task GetAlert_ExistingId_ReturnsAlertWithEvents()
    {
        var alert = TestEntityBuilders.CreateAlert();
        var evt = TestEntityBuilders.CreateAlertEvent(alert.AlertId);
        alert.AlertEvents.Add(evt);
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _controller.GetAlert(alert.AlertId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<AlertResponse>().Subject;
        response.AlertId.Should().Be(alert.AlertId);
        response.AlertEvents.Should().HaveCount(1);
    }

    [Test]
    public async Task GetAlert_NonexistentId_ReturnsNotFound()
    {
        var result = await _controller.GetAlert(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    // --- AcknowledgeAlert ---

    [Test]
    public async Task AcknowledgeAlert_OpenAlert_SetsStatusAndTimestamp()
    {
        var alert = TestEntityBuilders.CreateAlert(status: "open");
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _controller.AcknowledgeAlert(alert.AlertId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<AlertResponse>().Subject;
        response.Status.Should().Be("acknowledged");
        response.AcknowledgedAt.Should().NotBeNull();
    }

    [Test]
    public async Task AcknowledgeAlert_ResolvedAlert_ReturnsBadRequest()
    {
        var alert = TestEntityBuilders.CreateAlert(status: "resolved");
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();

        var result = await _controller.AcknowledgeAlert(alert.AlertId, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task AcknowledgeAlert_NonexistentId_ReturnsNotFound()
    {
        var result = await _controller.AcknowledgeAlert(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    // --- ResolveAlert ---

    [Test]
    public async Task ResolveAlert_ExistingAlert_SetsStatusAndNote()
    {
        var alert = TestEntityBuilders.CreateAlert(status: "open");
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();

        var request = new ResolveAlertRequest { ResolutionNote = "Fixed the issue" };
        var result = await _controller.ResolveAlert(alert.AlertId, request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<AlertResponse>().Subject;
        response.Status.Should().Be("resolved");
        response.ResolvedAt.Should().NotBeNull();
        response.ResolutionNote.Should().Be("Fixed the issue");
    }

    [Test]
    public async Task ResolveAlert_NonexistentId_ReturnsNotFound()
    {
        var request = new ResolveAlertRequest { ResolutionNote = "n/a" };
        var result = await _controller.ResolveAlert(Guid.NewGuid(), request, CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }
}
