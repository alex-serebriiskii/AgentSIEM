namespace Siem.Api.Notifications;

public class WebhookConfig
{
    public List<WebhookEndpoint> Endpoints { get; set; } = [];
}

public class WebhookEndpoint
{
    public string Url { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = [];
}
