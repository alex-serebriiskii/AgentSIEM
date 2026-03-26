using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public class AlertService(SiemDbContext db) : IAlertService
{
    public async Task<PaginatedResult<AlertResponse>> ListAsync(
        string? status, string? severity, string? agentId,
        int page, int pageSize, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        var query = db.Alerts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        if (!string.IsNullOrWhiteSpace(severity))
            query = query.Where(a => a.Severity == severity);

        if (!string.IsNullOrWhiteSpace(agentId))
            query = query.Where(a => a.AgentId == agentId);

        var totalCount = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var alerts = await query
            .OrderByDescending(a => a.TriggeredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PaginatedResult<AlertResponse>(
            alerts.Select(a => AlertResponse.FromEntity(a)).ToList(),
            page,
            pageSize,
            totalCount,
            totalPages);
    }

    public async Task<ServiceResult<AlertResponse>> GetAsync(Guid id, CancellationToken ct)
    {
        var alert = await db.Alerts
            .Include(a => a.AlertEvents)
            .FirstOrDefaultAsync(a => a.AlertId == id, ct);

        if (alert == null)
            return ServiceResult<AlertResponse>.NotFound();

        return ServiceResult<AlertResponse>.Success(
            AlertResponse.FromEntity(alert, includeEvents: true));
    }

    public async Task<ServiceResult<AlertResponse>> AcknowledgeAsync(Guid id, CancellationToken ct)
    {
        var alert = await db.Alerts.FindAsync([id], ct);
        if (alert == null)
            return ServiceResult<AlertResponse>.NotFound();

        if (alert.Status == "resolved")
            return ServiceResult<AlertResponse>.Fail("Cannot acknowledge a resolved alert");

        alert.Status = "acknowledged";
        alert.AcknowledgedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return ServiceResult<AlertResponse>.Success(AlertResponse.FromEntity(alert));
    }

    public async Task<ServiceResult<AlertResponse>> ResolveAsync(
        Guid id, ResolveAlertRequest request, CancellationToken ct)
    {
        var alert = await db.Alerts.FindAsync([id], ct);
        if (alert == null)
            return ServiceResult<AlertResponse>.NotFound();

        alert.Status = "resolved";
        alert.ResolvedAt = DateTime.UtcNow;
        alert.ResolutionNote = request.ResolutionNote;
        await db.SaveChangesAsync(ct);

        return ServiceResult<AlertResponse>.Success(AlertResponse.FromEntity(alert));
    }
}
