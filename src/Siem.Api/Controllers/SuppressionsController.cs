using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/suppressions")]
public class SuppressionsController : ControllerBase
{
    private readonly SiemDbContext _db;

    public SuppressionsController(SiemDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// List active suppressions with optional filters for rule_id and agent_id.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> ListSuppressions(
        [FromQuery] Guid? rule_id,
        [FromQuery] string? agent_id,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var query = _db.Suppressions
            .Where(s => s.ExpiresAt > now);

        if (rule_id.HasValue)
            query = query.Where(s => s.RuleId == rule_id.Value);

        if (!string.IsNullOrWhiteSpace(agent_id))
            query = query.Where(s => s.AgentId == agent_id);

        var suppressions = await query
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return Ok(suppressions.Select(SuppressionResponse.FromEntity));
    }

    /// <summary>
    /// Create a new suppression. At least one of RuleId or AgentId must be provided.
    /// </summary>
    [HttpPost("")]
    public async Task<IActionResult> CreateSuppression(
        [FromBody] CreateSuppressionRequest request,
        CancellationToken ct)
    {
        if (request.RuleId == null && string.IsNullOrWhiteSpace(request.AgentId))
            return BadRequest(new { error = "At least one of RuleId or AgentId must be provided" });

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "Reason is required" });

        if (string.IsNullOrWhiteSpace(request.CreatedBy))
            return BadRequest(new { error = "CreatedBy is required" });

        if (request.DurationMinutes <= 0)
            return BadRequest(new { error = "DurationMinutes must be greater than 0" });

        var now = DateTime.UtcNow;
        var entity = new SuppressionEntity
        {
            Id = Guid.NewGuid(),
            RuleId = request.RuleId,
            AgentId = string.IsNullOrWhiteSpace(request.AgentId) ? null : request.AgentId,
            Reason = request.Reason,
            CreatedBy = request.CreatedBy,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(request.DurationMinutes)
        };

        _db.Suppressions.Add(entity);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(ListSuppressions),
            SuppressionResponse.FromEntity(entity));
    }

    /// <summary>
    /// Delete a suppression by ID (hard delete).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSuppression(
        [FromRoute] Guid id, CancellationToken ct)
    {
        var entity = await _db.Suppressions.FindAsync([id], ct);
        if (entity == null) return NotFound();

        _db.Suppressions.Remove(entity);
        await _db.SaveChangesAsync(ct);

        return Ok();
    }
}
