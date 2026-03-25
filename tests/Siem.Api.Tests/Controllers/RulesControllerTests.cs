using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Siem.Api.Controllers;
using Siem.Api.Data;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;
using Siem.Api.Services;
using Siem.Api.Tests.Controllers.Helpers;

namespace Siem.Api.Tests.Controllers;

public class RulesControllerTests : IDisposable
{
    private readonly SiemDbContext _db;
    private readonly IRecompilationCoordinator _coordinator;
    private readonly RulesController _controller;

    public RulesControllerTests()
    {
        _db = DbContextFactory.Create();
        _coordinator = Substitute.For<IRecompilationCoordinator>();
        _controller = new RulesController(_db, _coordinator);
    }

    public void Dispose() => _db.Dispose();

    private static CreateRuleRequest ValidCreateRequest() => new()
    {
        Name = "Test Rule",
        Description = "A test rule",
        Severity = "medium",
        ConditionJson = TestEntityBuilders.ParseJson(TestEntityBuilders.ValidConditionJson),
        EvaluationType = "SingleEvent",
        CreatedBy = "test-user"
    };

    // --- CreateRule ---

    [Test]
    public async Task CreateRule_ValidRequest_ReturnsCreatedAndPersists()
    {
        var request = ValidCreateRequest();

        var result = await _controller.CreateRule(request, CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.Value.Should().BeOfType<RuleResponse>();
        _db.Rules.Should().HaveCount(1);
    }

    [Test]
    public async Task CreateRule_EmptyName_ReturnsBadRequest()
    {
        var request = ValidCreateRequest();
        request.Name = "";

        var result = await _controller.CreateRule(request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task CreateRule_InvalidConditionJson_ReturnsBadRequest()
    {
        var request = ValidCreateRequest();
        request.ConditionJson = TestEntityBuilders.ParseJson("""{"type":"unknown_garbage"}""");

        var result = await _controller.CreateRule(request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task CreateRule_SignalsRecompilation()
    {
        var request = ValidCreateRequest();

        await _controller.CreateRule(request, CancellationToken.None);

        _coordinator.Received(1).SignalInvalidation(
            Arg.Is<InvalidationSignal>(s => s.Reason == InvalidationReason.RuleCreated));
    }

    // --- ListRules ---

    [Test]
    public async Task ListRules_ReturnsAllOrderedByUpdatedAt()
    {
        var older = TestEntityBuilders.CreateRule(
            name: "Old Rule", updatedAt: DateTime.UtcNow.AddHours(-2));
        var newer = TestEntityBuilders.CreateRule(
            name: "New Rule", updatedAt: DateTime.UtcNow.AddHours(-1));
        _db.Rules.AddRange(older, newer);
        await _db.SaveChangesAsync();

        var result = await _controller.ListRules(null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rules = ok.Value.Should().BeAssignableTo<IEnumerable<RuleResponse>>().Subject.ToList();
        rules.Should().HaveCount(2);
        rules[0].Name.Should().Be("New Rule");
        rules[1].Name.Should().Be("Old Rule");
    }

    [Test]
    public async Task ListRules_FilterByEnabled_ReturnsOnlyMatching()
    {
        _db.Rules.Add(TestEntityBuilders.CreateRule(name: "Enabled", enabled: true));
        _db.Rules.Add(TestEntityBuilders.CreateRule(name: "Disabled", enabled: false));
        await _db.SaveChangesAsync();

        var result = await _controller.ListRules(true, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rules = ok.Value.Should().BeAssignableTo<IEnumerable<RuleResponse>>().Subject.ToList();
        rules.Should().HaveCount(1);
        rules[0].Name.Should().Be("Enabled");
    }

    [Test]
    public async Task ListRules_EmptyDatabase_ReturnsEmptyList()
    {
        var result = await _controller.ListRules(null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rules = ok.Value.Should().BeAssignableTo<IEnumerable<RuleResponse>>().Subject.ToList();
        rules.Should().BeEmpty();
    }

    // --- GetRule ---

    [Test]
    public async Task GetRule_ExistingId_ReturnsRule()
    {
        var rule = TestEntityBuilders.CreateRule(name: "My Rule");
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        var result = await _controller.GetRule(rule.Id, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RuleResponse>().Subject;
        response.Name.Should().Be("My Rule");
    }

    [Test]
    public async Task GetRule_NonexistentId_ReturnsNotFound()
    {
        var result = await _controller.GetRule(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    // --- UpdateRule ---

    [Test]
    public async Task UpdateRule_ExistingId_UpdatesAndReturnsOk()
    {
        var rule = TestEntityBuilders.CreateRule(name: "Original");
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        var request = new UpdateRuleRequest { Name = "Updated" };
        var result = await _controller.UpdateRule(rule.Id, request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RuleResponse>().Subject;
        response.Name.Should().Be("Updated");
    }

    [Test]
    public async Task UpdateRule_NonexistentId_ReturnsNotFound()
    {
        var request = new UpdateRuleRequest { Name = "Updated" };
        var result = await _controller.UpdateRule(Guid.NewGuid(), request, CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task UpdateRule_InvalidConditionJson_ReturnsBadRequest()
    {
        var rule = TestEntityBuilders.CreateRule();
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        var request = new UpdateRuleRequest
        {
            ConditionJson = TestEntityBuilders.ParseJson("""{"type":"unknown_garbage"}""")
        };
        var result = await _controller.UpdateRule(rule.Id, request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // --- DeleteRule ---

    [Test]
    public async Task DeleteRule_ExistingId_SoftDeletesAndReturnsNoContent()
    {
        var rule = TestEntityBuilders.CreateRule(enabled: true);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteRule(rule.Id, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        var updated = await _db.Rules.FindAsync(rule.Id);
        updated!.Enabled.Should().BeFalse();
    }

    [Test]
    public async Task DeleteRule_NonexistentId_ReturnsNotFound()
    {
        var result = await _controller.DeleteRule(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task DeleteRule_SignalsRecompilation()
    {
        var rule = TestEntityBuilders.CreateRule();
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        await _controller.DeleteRule(rule.Id, CancellationToken.None);

        _coordinator.Received(1).SignalInvalidation(
            Arg.Is<InvalidationSignal>(s => s.Reason == InvalidationReason.RuleDeleted));
    }

    // --- ActivateRule ---

    [Test]
    public async Task ActivateRule_ExistingId_EnablesAndSignalsAndWait()
    {
        var rule = TestEntityBuilders.CreateRule(enabled: false);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        var result = await _controller.ActivateRule(rule.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Rules.FindAsync(rule.Id);
        updated!.Enabled.Should().BeTrue();
        await _coordinator.Received(1).SignalAndWaitAsync(
            Arg.Is<InvalidationSignal>(s => s.Reason == InvalidationReason.RuleUpdated),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ActivateRule_NonexistentId_ReturnsNotFound()
    {
        var result = await _controller.ActivateRule(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }
}
