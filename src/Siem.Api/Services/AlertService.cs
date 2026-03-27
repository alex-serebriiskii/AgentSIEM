using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Data.Enums;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public class AlertService(SiemDbContext db, PaginationConfig paginationConfig) : IAlertService
{
    public async Task<PaginatedResult<AlertResponse>> ListAsync(
        string? status, string? severity, string? agentId,
        int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = PaginationConfig.Clamp(page, pageSize, paginationConfig.AlertsMaxPageSize);

        var query = db.Alerts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && EnumExtensions.TryParseAlertStatus(status, out var parsedStatus))
            query = query.Where(a => a.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(severity) && EnumExtensions.TryParseSeverity(severity, out var parsedSeverity))
            query = query.Where(a => a.Severity == parsedSeverity);

        if (!string.IsNullOrWhiteSpace(agentId))
            query = query.Where(a => a.AgentId == agentId);

        var totalCount = await query.CountAsync(ct);
        var totalPages = PaginationConfig.TotalPages(totalCount, pageSize);

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

        if (!EnumExtensions.IsValidTransition(alert.Status, AlertStatus.Acknowledged))
            return ServiceResult<AlertResponse>.Fail($"Cannot transition from {alert.Status.ToStorageString()} to acknowledged");

        alert.Status = AlertStatus.Acknowledged;
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

        if (!EnumExtensions.IsValidTransition(alert.Status, AlertStatus.Resolved))
            return ServiceResult<AlertResponse>.Fail($"Cannot transition from {alert.Status.ToStorageString()} to resolved");

        alert.Status = AlertStatus.Resolved;
        alert.ResolvedAt = DateTime.UtcNow;
        alert.ResolutionNote = request.ResolutionNote;
        await db.SaveChangesAsync(ct);

        return ServiceResult<AlertResponse>.Success(AlertResponse.FromEntity(alert));
    }
}
