using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public class SuppressionService(SiemDbContext db) : ISuppressionService
{
    public async Task<ServiceResult<IReadOnlyList<SuppressionResponse>>> ListAsync(
        Guid? ruleId, string? agentId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var query = db.Suppressions.Where(s => s.ExpiresAt > now);

        if (ruleId.HasValue)
            query = query.Where(s => s.RuleId == ruleId.Value);

        if (!string.IsNullOrWhiteSpace(agentId))
            query = query.Where(s => s.AgentId == agentId);

        var suppressions = await query
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return ServiceResult<IReadOnlyList<SuppressionResponse>>.Success(
            suppressions.Select(SuppressionResponse.FromEntity).ToList());
    }

    public async Task<ServiceResult<SuppressionResponse>> CreateAsync(
        CreateSuppressionRequest request, CancellationToken ct)
    {
        if (request.RuleId == null && string.IsNullOrWhiteSpace(request.AgentId))
            return ServiceResult<SuppressionResponse>.Fail(
                "At least one of RuleId or AgentId must be provided");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return ServiceResult<SuppressionResponse>.Fail("Reason is required");

        if (string.IsNullOrWhiteSpace(request.CreatedBy))
            return ServiceResult<SuppressionResponse>.Fail("CreatedBy is required");

        if (request.DurationMinutes <= 0)
            return ServiceResult<SuppressionResponse>.Fail(
                "DurationMinutes must be greater than 0");

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

        db.Suppressions.Add(entity);
        await db.SaveChangesAsync(ct);

        return ServiceResult<SuppressionResponse>.Success(
            SuppressionResponse.FromEntity(entity));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.Suppressions.FindAsync([id], ct);
        if (entity == null)
            return ServiceResult<bool>.NotFound();

        db.Suppressions.Remove(entity);
        await db.SaveChangesAsync(ct);

        return ServiceResult<bool>.Success(true);
    }
}
