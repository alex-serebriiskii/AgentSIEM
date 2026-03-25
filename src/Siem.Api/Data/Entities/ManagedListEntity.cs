namespace Siem.Api.Data.Entities;

public class ManagedListEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ListMemberEntity> Members { get; set; } = [];
}
