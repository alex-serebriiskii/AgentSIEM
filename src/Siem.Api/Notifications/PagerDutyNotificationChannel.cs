using System.Text;
using System.Text.Json;
using Siem.Api.Alerting;

namespace Siem.Api.Notifications;

/// <summary>
/// Creates incidents in PagerDuty via Events API v2 for critical alerts only.
/// Uses dedup key "siem-{alertId}" for automatic dedup and resolve integration.
/// 15-second timeout.
/// </summary>
public class PagerDutyNotificationChannel : INotificationChannel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PagerDutyConfig _config;
    private readonly ILogger<PagerDutyNotificationChannel> _logger;

    private const string PagerDutyEventsUrl = "https://events.pagerduty.com/v2/enqueue";

    public PagerDutyNotificationChannel(
        IHttpClientFactory httpClientFactory,
        PagerDutyConfig config,
        ILogger<PagerDutyNotificationChannel> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public string Name => "pagerduty";
    public string MinimumSeverity => "critical";

    public async Task SendAsync(EnrichedAlert alert, CancellationToken ct = default)
    {
        var httpClient = _httpClientFactory.CreateClient("pagerduty");

        var payload = new
        {
            routing_key = _config.RoutingKey,
            event_action = "trigger",
            dedup_key = $"siem-{alert.AlertId}",
            payload = new
            {
                summary = alert.Title,
                source = $"siem-agent-{alert.AgentId}",
                severity = "critical",
                timestamp = alert.TriggeredAt.ToString("O"),
                component = alert.AgentName,
                group = alert.Labels.GetValueOrDefault("category", "uncategorized"),
                @class = alert.RuleName,
                custom_details = new
                {
                    rule_id = alert.RuleId,
                    agent_id = alert.AgentId,
                    session_id = alert.SessionId,
                    session_event_count = alert.SessionEventCount,
                    recent_alert_count = alert.RecentAlertCount,
                    recent_tools = alert.RecentTools,
                    detail = alert.Detail,
                    siem_url = $"{_config.SiemBaseUrl}/alerts/{alert.AlertId}"
                }
            },
            links = new[]
            {
                new { href = $"{_config.SiemBaseUrl}/alerts/{alert.AlertId}", text = "View in SIEM" },
                new { href = $"{_config.SiemBaseUrl}/sessions/{alert.SessionId}", text = "Session timeline" }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, PagerDutyEventsUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var response = await httpClient.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();

        _logger.LogDebug(
            "PagerDuty incident created for alert={AlertId}", alert.AlertId);
    }
}
