namespace Siem.Api.Services;

public enum InvalidationReason
{
    RuleCreated,
    RuleUpdated,
    RuleDeleted,
    ListUpdated,
    ListDeleted,
    Startup,
    ManualReload
}

public record InvalidationSignal(
    InvalidationReason Reason,
    Guid? EntityId = null,
    string? Detail = null
);
