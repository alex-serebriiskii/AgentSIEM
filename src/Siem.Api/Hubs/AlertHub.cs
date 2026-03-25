using Microsoft.AspNetCore.SignalR;

namespace Siem.Api.Hubs;

/// <summary>
/// SignalR hub for real-time alert streaming to dashboard clients.
/// Clients can subscribe to agent-specific or severity-specific groups
/// to receive filtered alert feeds.
/// </summary>
public class AlertHub : Hub
{
    /// <summary>
    /// Adds the calling connection to the group "agent:{agentId}"
    /// to receive alerts for a specific agent.
    /// </summary>
    public async Task SubscribeToAgent(string agentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{agentId}");
    }

    /// <summary>
    /// Adds the calling connection to the group "severity:{severity}"
    /// to receive alerts at a specific severity level.
    /// </summary>
    public async Task SubscribeToSeverity(string severity)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"severity:{severity}");
    }

    /// <summary>
    /// Removes the calling connection from the agent-specific group.
    /// </summary>
    public async Task UnsubscribeFromAgent(string agentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"agent:{agentId}");
    }

    /// <summary>
    /// Removes the calling connection from the severity-specific group.
    /// </summary>
    public async Task UnsubscribeFromSeverity(string severity)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"severity:{severity}");
    }
}
