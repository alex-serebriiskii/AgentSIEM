using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Siem.Api.Controllers;
using Siem.Api.Data;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;
using Siem.Api.Services;
using Siem.Api.Tests.Controllers.Helpers;

namespace Siem.Api.Tests.Controllers;

public class AlertsControllerTests : IDisposable
{
    private readonly SiemDbContext _db;
    private readonly AlertsController _controller;

    public AlertsControllerTests()
    {
        _db = DbContextFactory.Create();
        var service = new AlertService(_db);
        _controller = new AlertsController(service);
    }

    public void Dispose() => _db.Dispose();

    private static PaginatedResult<AlertResponse> ExtractPaginatedResult(IActionResult result)
    {
        var ok = (OkObjectResult)result;
        return (PaginatedResult<AlertResponse>)ok.Value!;
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

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().HaveCount(2);
        paginated.Data[0].AlertId.Should().Be(newer.AlertId);
        paginated.Data[1].AlertId.Should().Be(older.AlertId);
        paginated.Page.Should().Be(1);
        paginated.PageSize.Should().Be(50);
        paginated.TotalCount.Should().Be(2);
    }

    [Test]
    public async Task ListAlerts_FilterByStatus_ReturnsOnlyMatching()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(status: "open"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(status: "acknowledged"));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts("open", null, null, ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().HaveCount(1);
        paginated.Data[0].Status.Should().Be("open");
        paginated.TotalCount.Should().Be(1);
    }

    [Test]
    public async Task ListAlerts_FilterBySeverity_ReturnsOnlyMatching()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(severity: "high"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(severity: "low"));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, "high", null, ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().HaveCount(1);
        paginated.Data[0].Severity.Should().Be("high");
        paginated.TotalCount.Should().Be(1);
    }

    [Test]
    public async Task ListAlerts_FilterByAgentId_ReturnsOnlyMatching()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(agentId: "agent-A"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(agentId: "agent-B"));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, null, "agent-A", ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().HaveCount(1);
        paginated.Data[0].AgentId.Should().Be("agent-A");
        paginated.TotalCount.Should().Be(1);
    }

    [Test]
    public async Task ListAlerts_EmptyDatabase_ReturnsEmptyList()
    {
        var result = await _controller.ListAlerts(null, null, null, ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().BeEmpty();
        paginated.Page.Should().Be(1);
        paginated.TotalCount.Should().Be(0);
        paginated.TotalPages.Should().Be(0);
    }

    // --- Pagination ---

    [Test]
    public async Task ListAlerts_DefaultPagination_ReturnsFirstPage()
    {
        for (int i = 0; i < 3; i++)
            _db.Alerts.Add(TestEntityBuilders.CreateAlert());
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, null, null, ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().HaveCount(3);
        paginated.Page.Should().Be(1);
        paginated.PageSize.Should().Be(50);
        paginated.TotalCount.Should().Be(3);
        paginated.TotalPages.Should().Be(1);
    }

    [Test]
    public async Task ListAlerts_ExplicitPageAndSize_ReturnsPaginatedResults()
    {
        for (int i = 0; i < 5; i++)
            _db.Alerts.Add(TestEntityBuilders.CreateAlert(
                triggeredAt: DateTime.UtcNow.AddMinutes(-i)));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, null, null, page: 2, pageSize: 2, ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().HaveCount(2);
        paginated.Page.Should().Be(2);
        paginated.PageSize.Should().Be(2);
        paginated.TotalCount.Should().Be(5);
        paginated.TotalPages.Should().Be(3);
    }

    [Test]
    public async Task ListAlerts_PageBeyondData_ReturnsEmpty()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert());
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, null, null, page: 5, pageSize: 10, ct: CancellationToken.None);

        var paginated = ExtractPaginatedResult(result);
        paginated.Data.Should().BeEmpty();
        paginated.TotalCount.Should().Be(1);
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
