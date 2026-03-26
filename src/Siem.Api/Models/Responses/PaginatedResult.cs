namespace Siem.Api.Models.Responses;

public record PaginatedResult<T>(
    IReadOnlyList<T> Data,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
