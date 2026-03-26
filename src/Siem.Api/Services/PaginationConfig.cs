namespace Siem.Api.Services;

/// <summary>
/// Default and maximum pagination limits for API endpoints.
/// </summary>
public class PaginationConfig
{
    /// <summary>Maximum allowed page size for event search.</summary>
    public int EventsMaxPageSize { get; set; } = 500;

    /// <summary>Maximum event limit for session timeline.</summary>
    public int SessionTimelineMaxLimit { get; set; } = 5000;
}
