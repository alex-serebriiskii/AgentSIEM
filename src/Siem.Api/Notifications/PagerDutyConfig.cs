namespace Siem.Api.Notifications;

public class PagerDutyConfig
{
    public string RoutingKey { get; set; } = "";
    public string SiemBaseUrl { get; set; } = "";
}
