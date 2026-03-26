using Microsoft.AspNetCore.Mvc;
using Siem.Api.Models.Requests;
using Siem.Api.Services;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/lists")]
public class ListsController(IListService listService) : ControllerBase
{
    /// <summary>
    /// List all managed lists (without members for brevity).
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> ListAll(CancellationToken ct)
    {
        var result = await listService.ListAllAsync(ct);
        return Ok(result.Value);
    }

    /// <summary>
    /// Get a single managed list with its members.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetList(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var result = await listService.GetAsync(id, ct);
        if (result.IsNotFound) return NotFound();

        return Ok(result.Value);
    }

    /// <summary>
    /// Create a new managed list with optional initial members.
    /// </summary>
    [HttpPost("")]
    public async Task<IActionResult> CreateList(
        [FromBody] CreateListRequest request, CancellationToken ct)
    {
        var result = await listService.CreateAsync(request, ct);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(
            nameof(GetList),
            new { id = result.Value!.Id },
            result.Value);
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
        var result = await listService.UpdateMembersAsync(id, request, ct);
        if (result.IsNotFound) return NotFound();

        return Ok(result.Value);
    }
}
