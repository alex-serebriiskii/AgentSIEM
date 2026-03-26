namespace Siem.Api.Services;

/// <summary>
/// Tracks agent sessions by upserting session records on each event.
/// </summary>
public interface ISessionTracker
{
    /// <summary>
    /// Upsert session: creates on first event, increments event_count on subsequent events.
    /// </summary>
    Task TrackEventAsync(string sessionId, string agentId, string agentName, DateTime timestamp, CancellationToken ct = default);
}
