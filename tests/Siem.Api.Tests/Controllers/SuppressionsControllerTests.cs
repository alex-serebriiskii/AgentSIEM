using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Siem.Api.Controllers;
using Siem.Api.Data;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;
using Siem.Api.Services;
using Siem.Api.Tests.Controllers.Helpers;

namespace Siem.Api.Tests.Controllers;

public class SuppressionsControllerTests : IDisposable
{
    private readonly SiemDbContext _db;
    private readonly ISuppressionService _service;
    private readonly SuppressionsController _controller;

    public SuppressionsControllerTests()
    {
        _db = DbContextFactory.Create();
        _service = new SuppressionService(_db);
        _controller = new SuppressionsController(_service);
    }

    public void Dispose() => _db.Dispose();

    // --- ListSuppressions ---

    [Test]
    public async Task ListSuppressions_ReturnsOnlyActive_ExcludesExpired()
    {
        var active = TestEntityBuilders.CreateSuppression(
            ruleId: Guid.NewGuid(),
            expiresAt: DateTime.UtcNow.AddHours(1));
        var expired = TestEntityBuilders.CreateSuppression(
            ruleId: Guid.NewGuid(),
            expiresAt: DateTime.UtcNow.AddHours(-1));
        _db.Suppressions.AddRange(active, expired);
        await _db.SaveChangesAsync();

        var result = await _controller.ListSuppressions(null, null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var suppressions = ok.Value.Should().BeAssignableTo<IEnumerable<SuppressionResponse>>().Subject.ToList();
        suppressions.Should().HaveCount(1);
        suppressions[0].Id.Should().Be(active.Id);
        suppressions[0].IsActive.Should().BeTrue();
    }

    [Test]
    public async Task ListSuppressions_FilterByRuleId_ReturnsOnlyMatching()
    {
        var ruleId = Guid.NewGuid();
        _db.Suppressions.Add(TestEntityBuilders.CreateSuppression(ruleId: ruleId));
        _db.Suppressions.Add(TestEntityBuilders.CreateSuppression(ruleId: Guid.NewGuid()));
        await _db.SaveChangesAsync();

        var result = await _controller.ListSuppressions(ruleId, null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var suppressions = ok.Value.Should().BeAssignableTo<IEnumerable<SuppressionResponse>>().Subject.ToList();
        suppressions.Should().HaveCount(1);
        suppressions[0].RuleId.Should().Be(ruleId);
    }

    [Test]
    public async Task ListSuppressions_FilterByAgentId_ReturnsOnlyMatching()
    {
        _db.Suppressions.Add(TestEntityBuilders.CreateSuppression(agentId: "agent-A"));
        _db.Suppressions.Add(TestEntityBuilders.CreateSuppression(agentId: "agent-B"));
        await _db.SaveChangesAsync();

        var result = await _controller.ListSuppressions(null, "agent-A", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var suppressions = ok.Value.Should().BeAssignableTo<IEnumerable<SuppressionResponse>>().Subject.ToList();
        suppressions.Should().HaveCount(1);
        suppressions[0].AgentId.Should().Be("agent-A");
    }

    // --- CreateSuppression ---

    [Test]
    public async Task CreateSuppression_ValidRequest_ReturnsCreated()
    {
        var request = new CreateSuppressionRequest
        {
            RuleId = Guid.NewGuid(),
            Reason = "Maintenance window",
            CreatedBy = "admin",
            DurationMinutes = 60
        };

        var result = await _controller.CreateSuppression(request, CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = created.Value.Should().BeOfType<SuppressionResponse>().Subject;
        response.RuleId.Should().Be(request.RuleId);
        response.Reason.Should().Be("Maintenance window");
        response.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task CreateSuppression_BothRuleIdAndAgentId_CreatesCombination()
    {
        var ruleId = Guid.NewGuid();
        var request = new CreateSuppressionRequest
        {
            RuleId = ruleId,
            AgentId = "agent-X",
            Reason = "Known false positive",
            CreatedBy = "admin",
            DurationMinutes = 30
        };

        var result = await _controller.CreateSuppression(request, CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = created.Value.Should().BeOfType<SuppressionResponse>().Subject;
        response.RuleId.Should().Be(ruleId);
        response.AgentId.Should().Be("agent-X");
    }

    [Test]
    public async Task CreateSuppression_NeitherRuleIdNorAgentId_ReturnsBadRequest()
    {
        var request = new CreateSuppressionRequest
        {
            Reason = "No target",
            CreatedBy = "admin",
            DurationMinutes = 60
        };

        var result = await _controller.CreateSuppression(request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task CreateSuppression_MissingReason_ReturnsBadRequest()
    {
        var request = new CreateSuppressionRequest
        {
            RuleId = Guid.NewGuid(),
            Reason = "",
            CreatedBy = "admin",
            DurationMinutes = 60
        };

        var result = await _controller.CreateSuppression(request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task CreateSuppression_MissingCreatedBy_ReturnsBadRequest()
    {
        var request = new CreateSuppressionRequest
        {
            RuleId = Guid.NewGuid(),
            Reason = "Test",
            CreatedBy = "",
            DurationMinutes = 60
        };

        var result = await _controller.CreateSuppression(request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task CreateSuppression_ZeroDuration_ReturnsBadRequest()
    {
        var request = new CreateSuppressionRequest
        {
            RuleId = Guid.NewGuid(),
            Reason = "Test",
            CreatedBy = "admin",
            DurationMinutes = 0
        };

        var result = await _controller.CreateSuppression(request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // --- DeleteSuppression ---

    [Test]
    public async Task DeleteSuppression_ExistingId_ReturnsOk()
    {
        var suppression = TestEntityBuilders.CreateSuppression(ruleId: Guid.NewGuid());
        _db.Suppressions.Add(suppression);
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteSuppression(suppression.Id, CancellationToken.None);

        result.Should().BeOfType<OkResult>();
        _db.Suppressions.Should().BeEmpty();
    }

    [Test]
    public async Task DeleteSuppression_NonexistentId_ReturnsNotFound()
    {
        var result = await _controller.DeleteSuppression(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }
}
