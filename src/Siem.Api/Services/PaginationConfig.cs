using System.ComponentModel.DataAnnotations;

namespace Siem.Api.Services;

/// <summary>
/// Default and maximum pagination limits for API endpoints.
/// </summary>
public class PaginationConfig
{
    /// <summary>Maximum allowed page size for alert queries.</summary>
    [Range(1, int.MaxValue)]
    public int AlertsMaxPageSize { get; set; } = 200;

    /// <summary>Maximum allowed page size for event search.</summary>
    [Range(1, int.MaxValue)]
    public int EventsMaxPageSize { get; set; } = 500;

    /// <summary>Maximum event limit for session timeline.</summary>
    [Range(1, int.MaxValue)]
    public int SessionTimelineMaxLimit { get; set; } = 5000;

    /// <summary>
    /// Clamps page and pageSize to valid ranges.
    /// </summary>
    public static (int page, int pageSize) Clamp(int page, int pageSize, int maxPageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > maxPageSize) pageSize = maxPageSize;
        return (page, pageSize);
    }

    /// <summary>
    /// Calculates total pages from total count and page size.
    /// </summary>
    public static int TotalPages(int totalCount, int pageSize) =>
        (int)Math.Ceiling((double)totalCount / pageSize);
}
