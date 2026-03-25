using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;
using Siem.Api.Services;
using FSharpSerialization = Siem.Rules.Core.Serialization;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/rules")]
public class RulesController : ControllerBase
{
    private readonly SiemDbContext _db;
    private readonly IRecompilationCoordinator _coordinator;

    public RulesController(SiemDbContext db, IRecompilationCoordinator coordinator)
    {
        _db = db;
        _coordinator = coordinator;
    }

    /// <summary>
    /// Create a new rule. Validates the condition tree via the F# parser before saving.
    /// Signals recompilation (fire-and-forget) after persisting.
    /// </summary>
    [HttpPost("")]
    public async Task<IActionResult> CreateRule(
        [FromBody] CreateRuleRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });

        // Validate condition tree is parseable by the F# engine
        try
        {
            FSharpSerialization.parseCondition(request.ConditionJson);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Invalid condition tree", detail = ex.Message });
        }

        var now = DateTime.UtcNow;
        var entity = new RuleEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Enabled = true,
            Severity = request.Severity,
            ConditionJson = request.ConditionJson.GetRawText(),
            EvaluationType = request.EvaluationType,
            TemporalConfig = request.TemporalConfig?.GetRawText(),
            SequenceConfig = request.SequenceConfig?.GetRawText(),
            ActionsJson = request.ActionsJson?.GetRawText() ?? "[]",
            Tags = request.Tags,
            CreatedBy = request.CreatedBy,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Rules.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Fire-and-forget: the coordinator debounces and compiles
        _coordinator.SignalInvalidation(
            new InvalidationSignal(InvalidationReason.RuleCreated, entity.Id));

        return CreatedAtAction(
            nameof(GetRule),
            new { id = entity.Id },
            RuleResponse.FromEntity(entity));
    }

    /// <summary>
    /// List all rules, optionally filtered by enabled status.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> ListRules(
        [FromQuery] bool? enabled, CancellationToken ct)
    {
        var query = _db.Rules.AsQueryable();

        if (enabled.HasValue)
            query = query.Where(r => r.Enabled == enabled.Value);

        var rules = await query
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);

        return Ok(rules.Select(RuleResponse.FromEntity));
    }

    /// <summary>
    /// Get a single rule by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRule(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var rule = await _db.Rules.FindAsync([id], ct);
        if (rule == null) return NotFound();

        return Ok(RuleResponse.FromEntity(rule));
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
        var rule = await _db.Rules.FindAsync([id], ct);
        if (rule == null) return NotFound();

        // If condition is being updated, validate it
        if (request.ConditionJson.HasValue)
        {
            try
            {
                FSharpSerialization.parseCondition(request.ConditionJson.Value);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "Invalid condition tree", detail = ex.Message });
            }

            rule.ConditionJson = request.ConditionJson.Value.GetRawText();
        }

        if (request.Name != null) rule.Name = request.Name;
        if (request.Description != null) rule.Description = request.Description;
        if (request.Severity != null) rule.Severity = request.Severity;
        if (request.EvaluationType != null) rule.EvaluationType = request.EvaluationType;
        if (request.TemporalConfig.HasValue) rule.TemporalConfig = request.TemporalConfig.Value.GetRawText();
        if (request.SequenceConfig.HasValue) rule.SequenceConfig = request.SequenceConfig.Value.GetRawText();
        if (request.ActionsJson.HasValue) rule.ActionsJson = request.ActionsJson.Value.GetRawText();
        if (request.Tags != null) rule.Tags = request.Tags;
        if (request.CreatedBy != null) rule.CreatedBy = request.CreatedBy;

        rule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _coordinator.SignalInvalidation(
            new InvalidationSignal(InvalidationReason.RuleUpdated, id));

        return Ok(RuleResponse.FromEntity(rule));
    }

    /// <summary>
    /// Soft-delete a rule (set enabled=false). Signals recompilation.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRule(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var rule = await _db.Rules.FindAsync([id], ct);
        if (rule == null) return NotFound();

        rule.Enabled = false;
        rule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _coordinator.SignalInvalidation(
            new InvalidationSignal(InvalidationReason.RuleDeleted, id));

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
        var rule = await _db.Rules.FindAsync([id], ct);
        if (rule == null) return NotFound();

        rule.Enabled = true;
        rule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Wait for recompilation so the caller knows the rule is live
        await _coordinator.SignalAndWaitAsync(
            new InvalidationSignal(InvalidationReason.RuleUpdated, id), ct);

        return Ok(new { status = "active", compiledAt = DateTime.UtcNow });
    }
}
