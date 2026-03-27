using Siem.Api.Data.Enums;
using FSharpSeverity = Siem.Rules.Core.Severity;

namespace Siem.Api.Shared;

public static class SeverityMapping
{
    public static string ToString(FSharpSeverity severity) => severity.Tag switch
    {
        FSharpSeverity.Tags.Low => "low",
        FSharpSeverity.Tags.Medium => "medium",
        FSharpSeverity.Tags.High => "high",
        FSharpSeverity.Tags.Critical => "critical",
        _ => "medium"
    };

    public static Severity ToEnum(FSharpSeverity severity) => severity.Tag switch
    {
        FSharpSeverity.Tags.Low => Severity.Low,
        FSharpSeverity.Tags.Medium => Severity.Medium,
        FSharpSeverity.Tags.High => Severity.High,
        FSharpSeverity.Tags.Critical => Severity.Critical,
        _ => Severity.Medium
    };

    public static FSharpSeverity FromString(string s) => s.ToLowerInvariant() switch
    {
        "low"      => FSharpSeverity.Low,
        "medium"   => FSharpSeverity.Medium,
        "high"     => FSharpSeverity.High,
        "critical" => FSharpSeverity.Critical,
        _          => FSharpSeverity.Medium
    };

    public static FSharpSeverity FromEnum(Severity severity) => severity switch
    {
        Severity.Low      => FSharpSeverity.Low,
        Severity.Medium   => FSharpSeverity.Medium,
        Severity.High     => FSharpSeverity.High,
        Severity.Critical => FSharpSeverity.Critical,
        _                 => FSharpSeverity.Medium
    };

    public static int ToOrder(string severity) => severity switch
    {
        "low"      => 0,
        "medium"   => 1,
        "high"     => 2,
        "critical" => 3,
        _          => 0
    };
}
