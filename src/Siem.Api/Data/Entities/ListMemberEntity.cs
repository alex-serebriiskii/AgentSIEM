namespace Siem.Api.Data.Entities;

public class ListMemberEntity
{
    public Guid ListId { get; set; }
    public string Value { get; set; } = "";
    public DateTime AddedAt { get; set; }
    public ManagedListEntity List { get; set; } = null!;
}
