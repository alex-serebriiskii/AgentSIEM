namespace Siem.Api.Alerting;

/// <summary>
/// An alert enriched with context from the database, ready for
/// persistence and notification routing.
/// </summary>
public record EnrichedAlert
{
    public Guid AlertId { get; init; }
    public Guid RuleId { get; init; }
    public string RuleName { get; init; } = "";
    public string Severity { get; init; } = "medium";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";

    // From the triggering event
    public string AgentId { get; init; } = "";
    public string AgentName { get; init; } = "";
    public string SessionId { get; init; } = "";

    // Enrichment fields
    public int RecentAlertCount { get; init; }
    public int SessionEventCount { get; init; }
    public string[] RecentTools { get; init; } = Array.Empty<string>();
    public Dictionary<string, object> RuleContext { get; init; } = new();
    public Dictionary<string, string> Labels { get; init; } = new();

    public DateTime TriggeredAt { get; init; } = DateTime.UtcNow;
}
