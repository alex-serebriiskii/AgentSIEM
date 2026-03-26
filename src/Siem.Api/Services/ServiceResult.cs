namespace Siem.Api.Services;

public record ServiceResult<T>
{
    public T? Value { get; init; }
    public string? Error { get; init; }
    public string? ErrorDetail { get; init; }
    public bool IsNotFound { get; init; }
    public bool IsSuccess => Error == null && !IsNotFound;

    public static ServiceResult<T> Success(T value) => new() { Value = value };
    public static ServiceResult<T> NotFound() => new() { IsNotFound = true };
    public static ServiceResult<T> Fail(string error, string? detail = null)
        => new() { Error = error, ErrorDetail = detail };
}
