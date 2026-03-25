namespace Siem.Api.Data.Entities;

public class AlertEventEntity
{
    public Guid AlertId { get; set; }
    public Guid EventId { get; set; }
    public DateTime EventTimestamp { get; set; }
    public short? SequenceOrder { get; set; }
    public AlertEntity Alert { get; set; } = null!;
}
