namespace Siem.Api.Services;

public class CompilationMetadata
{
    public DateTime CompiledAt { get; init; }
    public int RuleCount { get; init; }
    public IReadOnlyList<ListCacheInfo> ListCacheInfo { get; init; } = [];

    public static CompilationMetadata Empty => new()
    {
        CompiledAt = DateTime.MinValue,
        RuleCount = 0,
        ListCacheInfo = []
    };
}
