namespace Siem.Api.Notifications;

public class WebhookConfig
{
    public List<WebhookEndpoint> Endpoints { get; set; } = [];

    /// <summary>HTTP request timeout in seconds per endpoint.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

public class WebhookEndpoint
{
    public string Url { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = [];
}
