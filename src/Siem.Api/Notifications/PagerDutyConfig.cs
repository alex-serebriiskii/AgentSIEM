namespace Siem.Api.Notifications;

public class PagerDutyConfig
{
    public string RoutingKey { get; set; } = "";
    public string SiemBaseUrl { get; set; } = "";

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 15;
}
