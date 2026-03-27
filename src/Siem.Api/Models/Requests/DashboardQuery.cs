namespace Siem.Api.Models.Requests;

public class DashboardQuery
{
    public int Hours { get; set; } = 24;
    public int Limit { get; set; } = 20;
}
