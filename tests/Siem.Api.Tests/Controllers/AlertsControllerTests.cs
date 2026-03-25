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

    // --- ListAlerts ---

    [Test]
    public async Task ListAlerts_ReturnsAllOrderedByTriggeredAt()
    {
        var older = TestEntityBuilders.CreateAlert(triggeredAt: DateTime.UtcNow.AddHours(-2));
        var newer = TestEntityBuilders.CreateAlert(triggeredAt: DateTime.UtcNow.AddHours(-1));
        _db.Alerts.AddRange(older, newer);
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, null, null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var alerts = ok.Value.Should().BeAssignableTo<IEnumerable<AlertResponse>>().Subject.ToList();
        alerts.Should().HaveCount(2);
        alerts[0].AlertId.Should().Be(newer.AlertId);
        alerts[1].AlertId.Should().Be(older.AlertId);
    }

    [Test]
    public async Task ListAlerts_FilterByStatus_ReturnsOnlyMatching()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(status: "open"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(status: "acknowledged"));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts("open", null, null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var alerts = ok.Value.Should().BeAssignableTo<IEnumerable<AlertResponse>>().Subject.ToList();
        alerts.Should().HaveCount(1);
        alerts[0].Status.Should().Be("open");
    }

    [Test]
    public async Task ListAlerts_FilterBySeverity_ReturnsOnlyMatching()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(severity: "high"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(severity: "low"));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, "high", null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var alerts = ok.Value.Should().BeAssignableTo<IEnumerable<AlertResponse>>().Subject.ToList();
        alerts.Should().HaveCount(1);
        alerts[0].Severity.Should().Be("high");
    }

    [Test]
    public async Task ListAlerts_FilterByAgentId_ReturnsOnlyMatching()
    {
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(agentId: "agent-A"));
        _db.Alerts.Add(TestEntityBuilders.CreateAlert(agentId: "agent-B"));
        await _db.SaveChangesAsync();

        var result = await _controller.ListAlerts(null, null, "agent-A", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var alerts = ok.Value.Should().BeAssignableTo<IEnumerable<AlertResponse>>().Subject.ToList();
        alerts.Should().HaveCount(1);
        alerts[0].AgentId.Should().Be("agent-A");
    }

    [Test]
    public async Task ListAlerts_EmptyDatabase_ReturnsEmptyList()
    {
        var result = await _controller.ListAlerts(null, null, null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var alerts = ok.Value.Should().BeAssignableTo<IEnumerable<AlertResponse>>().Subject.ToList();
        alerts.Should().BeEmpty();
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
