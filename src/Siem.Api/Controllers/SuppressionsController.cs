using Microsoft.AspNetCore.Mvc;
using Siem.Api.Models.Requests;
using Siem.Api.Services;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/suppressions")]
public class SuppressionsController(ISuppressionService suppressionService) : ControllerBase
{
    /// <summary>
    /// List active suppressions with optional filters for rule_id and agent_id.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> ListSuppressions(
        [FromQuery] Guid? rule_id,
        [FromQuery] string? agent_id,
        CancellationToken ct)
    {
        var result = await suppressionService.ListAsync(rule_id, agent_id, ct);
        return Ok(result.Value);
    }

    /// <summary>
    /// Create a new suppression. At least one of RuleId or AgentId must be provided.
    /// </summary>
    [HttpPost("")]
    public async Task<IActionResult> CreateSuppression(
        [FromBody] CreateSuppressionRequest request,
        CancellationToken ct)
    {
        var result = await suppressionService.CreateAsync(request, ct);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(
            nameof(ListSuppressions),
            result.Value);
    }

    /// <summary>
    /// Delete a suppression by ID (hard delete).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSuppression(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var result = await suppressionService.DeleteAsync(id, ct);
        if (result.IsNotFound) return NotFound();

        return Ok();
    }
}
