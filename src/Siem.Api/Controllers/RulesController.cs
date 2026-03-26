using Microsoft.AspNetCore.Mvc;
using Siem.Api.Models.Requests;
using Siem.Api.Services;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/rules")]
public class RulesController(IRuleService ruleService) : ControllerBase
{
    /// <summary>
    /// Create a new rule. Validates the condition tree via the F# parser before saving.
    /// Signals recompilation (fire-and-forget) after persisting.
    /// </summary>
    [HttpPost("")]
    public async Task<IActionResult> CreateRule(
        [FromBody] CreateRuleRequest request, CancellationToken ct)
    {
        var result = await ruleService.CreateAsync(request, ct);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error, detail = result.ErrorDetail });

        return CreatedAtAction(
            nameof(GetRule),
            new { id = result.Value!.Id },
            result.Value);
    }

    /// <summary>
    /// List all rules, optionally filtered by enabled status.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> ListRules(
        [FromQuery] bool? enabled, CancellationToken ct)
    {
        var result = await ruleService.ListAsync(enabled, ct);
        return Ok(result.Value);
    }

    /// <summary>
    /// Get a single rule by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRule(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var result = await ruleService.GetAsync(id, ct);
        if (result.IsNotFound) return NotFound();

        return Ok(result.Value);
    }

    /// <summary>
    /// Update a rule (partial update). Signals recompilation after persisting.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateRule(
        [FromRoute] Guid id,
        [FromBody] UpdateRuleRequest request,
        CancellationToken ct)
    {
        var result = await ruleService.UpdateAsync(id, request, ct);
        if (result.IsNotFound) return NotFound();
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error, detail = result.ErrorDetail });

        return Ok(result.Value);
    }

    /// <summary>
    /// Soft-delete a rule (set enabled=false). Signals recompilation.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRule(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var result = await ruleService.DeleteAsync(id, ct);
        if (result.IsNotFound) return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Activate a rule and wait for compilation to complete.
    /// Returns only after the rule is live in the engine.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> ActivateRule(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var result = await ruleService.ActivateAsync(id, ct);
        if (result.IsNotFound) return NotFound();

        return Ok(result.Value);
    }
}
