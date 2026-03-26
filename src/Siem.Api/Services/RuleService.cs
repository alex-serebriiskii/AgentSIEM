using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;
using FSharpSerialization = Siem.Rules.Core.Serialization;

namespace Siem.Api.Services;

public class RuleService(SiemDbContext db, IRecompilationCoordinator coordinator) : IRuleService
{
    public async Task<ServiceResult<RuleResponse>> CreateAsync(
        CreateRuleRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ServiceResult<RuleResponse>.Fail("Name is required");

        try
        {
            FSharpSerialization.parseCondition(request.ConditionJson);
        }
        catch (Exception ex)
        {
            return ServiceResult<RuleResponse>.Fail("Invalid condition tree", ex.Message);
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

        db.Rules.Add(entity);
        await db.SaveChangesAsync(ct);

        coordinator.SignalInvalidation(
            new InvalidationSignal(InvalidationReason.RuleCreated, entity.Id));

        return ServiceResult<RuleResponse>.Success(RuleResponse.FromEntity(entity));
    }

    public async Task<ServiceResult<IReadOnlyList<RuleResponse>>> ListAsync(
        bool? enabled, CancellationToken ct)
    {
        var query = db.Rules.AsQueryable();

        if (enabled.HasValue)
            query = query.Where(r => r.Enabled == enabled.Value);

        var rules = await query
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);

        return ServiceResult<IReadOnlyList<RuleResponse>>.Success(
            rules.Select(RuleResponse.FromEntity).ToList());
    }

    public async Task<ServiceResult<RuleResponse>> GetAsync(Guid id, CancellationToken ct)
    {
        var rule = await db.Rules.FindAsync([id], ct);
        if (rule == null)
            return ServiceResult<RuleResponse>.NotFound();

        return ServiceResult<RuleResponse>.Success(RuleResponse.FromEntity(rule));
    }

    public async Task<ServiceResult<RuleResponse>> UpdateAsync(
        Guid id, UpdateRuleRequest request, CancellationToken ct)
    {
        var rule = await db.Rules.FindAsync([id], ct);
        if (rule == null)
            return ServiceResult<RuleResponse>.NotFound();

        if (request.ConditionJson.HasValue)
        {
            try
            {
                FSharpSerialization.parseCondition(request.ConditionJson.Value);
            }
            catch (Exception ex)
            {
                return ServiceResult<RuleResponse>.Fail("Invalid condition tree", ex.Message);
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
        await db.SaveChangesAsync(ct);

        coordinator.SignalInvalidation(
            new InvalidationSignal(InvalidationReason.RuleUpdated, id));

        return ServiceResult<RuleResponse>.Success(RuleResponse.FromEntity(rule));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var rule = await db.Rules.FindAsync([id], ct);
        if (rule == null)
            return ServiceResult<bool>.NotFound();

        rule.Enabled = false;
        rule.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        coordinator.SignalInvalidation(
            new InvalidationSignal(InvalidationReason.RuleDeleted, id));

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<object>> ActivateAsync(Guid id, CancellationToken ct)
    {
        var rule = await db.Rules.FindAsync([id], ct);
        if (rule == null)
            return ServiceResult<object>.NotFound();

        rule.Enabled = true;
        rule.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await coordinator.SignalAndWaitAsync(
            new InvalidationSignal(InvalidationReason.RuleUpdated, id), ct);

        return ServiceResult<object>.Success(
            new { status = "active", compiledAt = DateTime.UtcNow });
    }
}
