using Microsoft.AspNetCore.SignalR;
using Siem.Api.Alerting;
using Siem.Api.Hubs;

namespace Siem.Api.Notifications;

/// <summary>
/// Pushes alerts to connected dashboard clients via SignalR in real time.
/// All severities are sent — the UI filters client-side.
/// Alerts are sent to the "all" group, plus agent-specific and severity-specific groups.
/// </summary>
public class SignalRNotificationChannel : INotificationChannel
{
    private readonly IHubContext<AlertHub> _hubContext;

    public SignalRNotificationChannel(IHubContext<AlertHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public string Name => "signalr";
    public string MinimumSeverity => "low";

    public async Task SendAsync(EnrichedAlert alert, CancellationToken ct = default)
    {
        var payload = new
        {
            alertId = alert.AlertId,
            ruleId = alert.RuleId,
            ruleName = alert.RuleName,
            severity = alert.Severity,
            title = alert.Title,
            agentId = alert.AgentId,
            agentName = alert.AgentName,
            sessionId = alert.SessionId,
            triggeredAt = alert.TriggeredAt,
            recentAlertCount = alert.RecentAlertCount,
            labels = alert.Labels
        };

        // Send to all connected clients
        await _hubContext.Clients.All.SendAsync("AlertReceived", payload, ct);

        // Send to agent-specific group
        await _hubContext.Clients.Group($"agent:{alert.AgentId}")
            .SendAsync("AlertReceived", payload, ct);

        // Send to severity-specific group
        await _hubContext.Clients.Group($"severity:{alert.Severity}")
            .SendAsync("AlertReceived", payload, ct);
    }
}
