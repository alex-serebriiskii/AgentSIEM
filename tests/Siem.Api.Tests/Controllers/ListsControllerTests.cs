using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Siem.Api.Controllers;
using Siem.Api.Data;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;
using Siem.Api.Services;
using Siem.Api.Tests.Controllers.Helpers;

namespace Siem.Api.Tests.Controllers;

public class ListsControllerTests : IDisposable
{
    private readonly SiemDbContext _db;
    private readonly IRecompilationCoordinator _coordinator;
    private readonly IListService _service;
    private readonly ListsController _controller;

    public ListsControllerTests()
    {
        _db = DbContextFactory.Create();
        _coordinator = Substitute.For<IRecompilationCoordinator>();
        _coordinator.SignalInvalidation(Arg.Any<InvalidationSignal>()).Returns(true);
        _service = new ListService(_db, _coordinator, NullLogger<ListService>.Instance);
        _controller = new ListsController(_service);
    }

    public void Dispose() => _db.Dispose();

    // --- ListAll ---

    [Test]
    public async Task ListAll_ReturnsAllListsWithMemberCounts()
    {
        var list = TestEntityBuilders.CreateManagedList(
            name: "Approved Tools", members: ["tool-a", "tool-b"]);
        _db.ManagedLists.Add(list);
        await _db.SaveChangesAsync();

        var result = await _controller.ListAll(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var lists = ok.Value.Should().BeAssignableTo<IEnumerable<ManagedListSummaryResponse>>().Subject.ToList();
        lists.Should().HaveCount(1);
        lists[0].Name.Should().Be("Approved Tools");
        lists[0].MemberCount.Should().Be(2);
    }

    [Test]
    public async Task ListAll_EmptyDatabase_ReturnsEmptyList()
    {
        var result = await _controller.ListAll(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var lists = ok.Value.Should().BeAssignableTo<IEnumerable<ManagedListSummaryResponse>>().Subject.ToList();
        lists.Should().BeEmpty();
    }

    // --- GetList ---

    [Test]
    public async Task GetList_ExistingId_ReturnsListWithMembers()
    {
        var list = TestEntityBuilders.CreateManagedList(
            name: "Blocked Agents", members: ["agent-bad"]);
        _db.ManagedLists.Add(list);
        await _db.SaveChangesAsync();

        var result = await _controller.GetList(list.Id, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ManagedListDetailResponse>().Subject;
        response.Name.Should().Be("Blocked Agents");
        response.Members.Should().HaveCount(1);
    }

    [Test]
    public async Task GetList_NonexistentId_ReturnsNotFound()
    {
        var result = await _controller.GetList(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    // --- CreateList ---

    [Test]
    public async Task CreateList_ValidRequest_ReturnsCreatedAndPersists()
    {
        var request = new CreateListRequest
        {
            Name = "My List",
            Description = "A test list",
            Enabled = true,
            Members = []
        };

        var result = await _controller.CreateList(request, CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
        _db.ManagedLists.Should().HaveCount(1);
    }

    [Test]
    public async Task CreateList_EmptyName_ReturnsBadRequest()
    {
        var request = new CreateListRequest { Name = "", Members = [] };

        var result = await _controller.CreateList(request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task CreateList_WithInitialMembers_PersistsMembers()
    {
        var request = new CreateListRequest
        {
            Name = "Tools",
            Description = "Approved tools",
            Members = ["tool-a", "tool-b", "tool-c"]
        };

        var result = await _controller.CreateList(request, CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
        _db.ListMembers.Should().HaveCount(3);
    }

    [Test]
    public async Task CreateList_SignalsRecompilation()
    {
        var request = new CreateListRequest { Name = "List", Members = [] };

        await _controller.CreateList(request, CancellationToken.None);

        _coordinator.Received(1).SignalInvalidation(
            Arg.Is<InvalidationSignal>(s => s.Reason == InvalidationReason.ListUpdated));
    }

    // --- UpdateListMembers ---

    [Test]
    public async Task UpdateListMembers_ExistingId_ReplacesAllMembers()
    {
        var list = TestEntityBuilders.CreateManagedList(
            name: "Tools", members: ["old-tool"]);
        _db.ManagedLists.Add(list);
        await _db.SaveChangesAsync();

        var request = new UpdateListMembersRequest
        {
            Members = ["new-tool-a", "new-tool-b"]
        };

        var result = await _controller.UpdateListMembers(list.Id, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var members = _db.ListMembers.Where(m => m.ListId == list.Id).ToList();
        members.Should().HaveCount(2);
        members.Select(m => m.Value).Should().Contain(["new-tool-a", "new-tool-b"]);
    }

    [Test]
    public async Task UpdateListMembers_NonexistentId_ReturnsNotFound()
    {
        var request = new UpdateListMembersRequest { Members = ["x"] };

        var result = await _controller.UpdateListMembers(Guid.NewGuid(), request, CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task UpdateListMembers_SignalsRecompilation()
    {
        var list = TestEntityBuilders.CreateManagedList(name: "Tools", members: ["old"]);
        _db.ManagedLists.Add(list);
        await _db.SaveChangesAsync();

        var request = new UpdateListMembersRequest { Members = ["new"] };
        await _controller.UpdateListMembers(list.Id, request, CancellationToken.None);

        _coordinator.Received(1).SignalInvalidation(
            Arg.Is<InvalidationSignal>(s => s.Reason == InvalidationReason.ListUpdated));
    }
}
