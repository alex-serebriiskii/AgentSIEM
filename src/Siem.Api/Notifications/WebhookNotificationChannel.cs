using System.Text;
using System.Text.Json;
using Siem.Api.Alerting;

namespace Siem.Api.Notifications;

/// <summary>
/// POSTs alert JSON to configured webhook endpoints with custom headers.
/// Medium severity and above. 10-second timeout per endpoint.
/// </summary>
public class WebhookNotificationChannel : INotificationChannel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebhookConfig _config;
    private readonly ILogger<WebhookNotificationChannel> _logger;

    public WebhookNotificationChannel(
        IHttpClientFactory httpClientFactory,
        WebhookConfig config,
        ILogger<WebhookNotificationChannel> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public string Name => "webhook";
    public string MinimumSeverity => "medium";

    public async Task SendAsync(EnrichedAlert alert, CancellationToken ct = default)
    {
        var httpClient = _httpClientFactory.CreateClient("webhook");

        foreach (var endpoint in _config.Endpoints)
        {
            var payload = new
            {
                alert_id = alert.AlertId,
                rule_id = alert.RuleId,
                rule_name = alert.RuleName,
                severity = alert.Severity,
                title = alert.Title,
                detail = alert.Detail,
                agent_id = alert.AgentId,
                agent_name = alert.AgentName,
                session_id = alert.SessionId,
                triggered_at = alert.TriggeredAt,
                context = alert.RuleContext,
                labels = alert.Labels,
                siem_url = $"/alerts/{alert.AlertId}"
            };

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            foreach (var header in endpoint.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await httpClient.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            _logger.LogDebug(
                "Webhook delivered: endpoint={Endpoint} alert={AlertId}",
                endpoint.Url, alert.AlertId);
        }
    }
}
