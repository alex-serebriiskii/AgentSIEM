using System.Text;
using System.Text.Json;
using Siem.Api.Alerting;

namespace Siem.Api.Notifications;

/// <summary>
/// Sends Block Kit formatted messages to Slack via webhook.
/// High severity and above. 10-second timeout.
/// </summary>
public class SlackNotificationChannel : INotificationChannel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SlackConfig _config;
    private readonly ILogger<SlackNotificationChannel> _logger;

    public SlackNotificationChannel(
        IHttpClientFactory httpClientFactory,
        SlackConfig config,
        ILogger<SlackNotificationChannel> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public string Name => "slack";
    public string MinimumSeverity => "high";

    public async Task SendAsync(EnrichedAlert alert, CancellationToken ct = default)
    {
        var httpClient = _httpClientFactory.CreateClient("slack");

        var severityEmoji = alert.Severity switch
        {
            "critical" => ":rotating_light:",
            "high" => ":warning:",
            "medium" => ":large_yellow_circle:",
            _ => ":information_source:"
        };

        var toolList = alert.RecentTools.Length > 0
            ? string.Join(", ", alert.RecentTools.Take(5))
            : "none";

        var payload = new
        {
            channel = _config.Channel,
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new { type = "plain_text", text = alert.Title }
                },
                new
                {
                    type = "section",
                    fields = new[]
                    {
                        new { type = "mrkdwn", text = $"*Severity:*\n{severityEmoji} {alert.Severity}" },
                        new { type = "mrkdwn", text = $"*Agent:*\n{alert.AgentName}" },
                        new { type = "mrkdwn", text = $"*Session events:*\n{alert.SessionEventCount}" },
                        new { type = "mrkdwn", text = $"*Recent alerts (24h):*\n{alert.RecentAlertCount}" },
                        new { type = "mrkdwn", text = $"*Tools used:*\n{toolList}" },
                        new { type = "mrkdwn", text = $"*Rule:*\n{alert.RuleName}" }
                    }
                },
                new
                {
                    type = "section",
                    text = new { type = "mrkdwn", text = $">{alert.Detail}" }
                },
                new
                {
                    type = "actions",
                    elements = new[]
                    {
                        new
                        {
                            type = "button",
                            text = new { type = "plain_text", text = "View alert" },
                            url = $"{_config.SiemBaseUrl}/alerts/{alert.AlertId}"
                        },
                        new
                        {
                            type = "button",
                            text = new { type = "plain_text", text = "View session" },
                            url = $"{_config.SiemBaseUrl}/sessions/{alert.SessionId}"
                        }
                    }
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, _config.WebhookUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var response = await httpClient.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug("Slack notification sent for alert={AlertId}", alert.AlertId);
    }
}
