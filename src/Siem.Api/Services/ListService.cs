using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Siem.Api.Models.Requests;
using Siem.Api.Models.Responses;

namespace Siem.Api.Services;

public class ListService(SiemDbContext db, IRecompilationCoordinator coordinator, ILogger<ListService> logger) : IListService
{
    public async Task<ServiceResult<IReadOnlyList<ManagedListSummaryResponse>>> ListAllAsync(
        CancellationToken ct)
    {
        var lists = await db.ManagedLists
            .OrderBy(l => l.Name)
            .Select(l => new ManagedListSummaryResponse
            {
                Id = l.Id,
                Name = l.Name,
                Description = l.Description,
                Enabled = l.Enabled,
                MemberCount = l.Members.Count,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt
            })
            .ToListAsync(ct);

        return ServiceResult<IReadOnlyList<ManagedListSummaryResponse>>.Success(lists);
    }

    public async Task<ServiceResult<ManagedListDetailResponse>> GetAsync(
        Guid id, CancellationToken ct)
    {
        var list = await db.ManagedLists
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (list == null)
            return ServiceResult<ManagedListDetailResponse>.NotFound();

        return ServiceResult<ManagedListDetailResponse>.Success(
            ManagedListDetailResponse.FromEntity(list));
    }

    public async Task<ServiceResult<ManagedListSummaryResponse>> CreateAsync(
        CreateListRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ServiceResult<ManagedListSummaryResponse>.Fail("Name is required");

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

        db.ManagedLists.Add(entity);
        await db.SaveChangesAsync(ct);

        await InvalidationHelper.SignalWithRetryAsync(coordinator,
            new InvalidationSignal(InvalidationReason.ListUpdated, entity.Id), logger);

        return ServiceResult<ManagedListSummaryResponse>.Success(
            ManagedListSummaryResponse.FromEntity(entity));
    }

    public async Task<ServiceResult<object>> UpdateMembersAsync(
        Guid id, UpdateListMembersRequest request, CancellationToken ct)
    {
        var list = await db.ManagedLists
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (list == null)
            return ServiceResult<object>.NotFound();

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
        await db.SaveChangesAsync(ct);

        await InvalidationHelper.SignalWithRetryAsync(coordinator,
            new InvalidationSignal(InvalidationReason.ListUpdated, id), logger);

        return ServiceResult<object>.Success(
            new { memberCount = request.Members.Count });
    }
}
