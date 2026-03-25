using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Models.Requests;
using Siem.Api.Services;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/lists")]
public class ListsController : ControllerBase
{
    private readonly SiemDbContext _db;
    private readonly RecompilationCoordinator _coordinator;

    public ListsController(SiemDbContext db, RecompilationCoordinator coordinator)
    {
        _db = db;
        _coordinator = coordinator;
    }

    /// <summary>
    /// List all managed lists (without members for brevity).
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> ListAll(CancellationToken ct)
    {
        var lists = await _db.ManagedLists
            .OrderBy(l => l.Name)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.Description,
                l.Enabled,
                MemberCount = l.Members.Count,
                l.CreatedAt,
                l.UpdatedAt
            })
            .ToListAsync(ct);

        return Ok(lists);
    }

    /// <summary>
    /// Get a single managed list with its members.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetList(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var list = await _db.ManagedLists
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (list == null) return NotFound();

        return Ok(new
        {
            list.Id,
            list.Name,
            list.Description,
            list.Enabled,
            Members = list.Members
                .OrderBy(m => m.Value)
                .Select(m => new { m.Value, m.AddedAt }),
            list.CreatedAt,
            list.UpdatedAt
        });
    }

    /// <summary>
    /// Create a new managed list with optional initial members.
    /// </summary>
    [HttpPost("")]
    public async Task<IActionResult> CreateList(
        [FromBody] CreateListRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });

        var now = DateTime.UtcNow;
        var entity = new ManagedListEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Enabled = request.Enabled,
            CreatedAt = now,
            UpdatedAt = now,
            Members = request.Members
                .Select(v => new ListMemberEntity
                {
                    Value = v,
                    AddedAt = now
                })
                .ToList()
        };

        _db.ManagedLists.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Signal recompilation so rules referencing lists pick up the new list
        _coordinator.SignalInvalidation(
            new InvalidationSignal(InvalidationReason.ListUpdated, entity.Id));

        return CreatedAtAction(
            nameof(GetList),
            new { id = entity.Id },
            new
            {
                entity.Id,
                entity.Name,
                entity.Description,
                entity.Enabled,
                MemberCount = entity.Members.Count,
                entity.CreatedAt,
                entity.UpdatedAt
            });
    }

    /// <summary>
    /// Replace all members of a managed list. Signals recompilation
    /// because rules snapshot list contents at compile time.
    /// </summary>
    [HttpPut("{id:guid}/members")]
    public async Task<IActionResult> UpdateListMembers(
        [FromRoute] Guid id,
        [FromBody] UpdateListMembersRequest request,
        CancellationToken ct)
    {
        var list = await _db.ManagedLists
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (list == null) return NotFound();

        // Replace all members
        list.Members.Clear();
        var now = DateTime.UtcNow;
        foreach (var value in request.Members)
        {
            list.Members.Add(new ListMemberEntity
            {
                Value = value,
                AddedAt = now
            });
        }

        list.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        // List change = recompilation needed (rules snapshot list contents)
        _coordinator.SignalInvalidation(
            new InvalidationSignal(InvalidationReason.ListUpdated, id));

        return Ok(new { memberCount = request.Members.Count });
    }
}
