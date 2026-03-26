using Siem.Api.Data.Entities;

namespace Siem.Api.Models.Responses;

public class ManagedListSummaryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; }
    public int MemberCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static ManagedListSummaryResponse FromEntity(ManagedListEntity entity)
    {
        return new ManagedListSummaryResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Enabled = entity.Enabled,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            MemberCount = entity.Members?.Count ?? 0
        };
    }
}

public class ManagedListDetailResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; }
    public List<ManagedListMemberResponse> Members { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static ManagedListDetailResponse FromEntity(ManagedListEntity entity)
    {
        return new ManagedListDetailResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Enabled = entity.Enabled,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Members = entity.Members
                .OrderBy(m => m.Value)
                .Select(m => new ManagedListMemberResponse { Value = m.Value, AddedAt = m.AddedAt })
                .ToList()
        };
    }
}

public class ManagedListMemberResponse
{
    public string Value { get; set; } = "";
    public DateTime AddedAt { get; set; }
}
