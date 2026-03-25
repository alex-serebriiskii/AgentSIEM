namespace Siem.Api.Notifications;

public class SlackConfig
{
    public string WebhookUrl { get; set; } = "";
    public string Channel { get; set; } = "";
    public string SiemBaseUrl { get; set; } = "";
}
