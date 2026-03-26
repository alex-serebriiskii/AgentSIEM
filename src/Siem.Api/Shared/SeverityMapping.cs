using Siem.Rules.Core;

namespace Siem.Api.Shared;

public static class SeverityMapping
{
    public static string ToString(Severity severity) => severity.Tag switch
    {
        Severity.Tags.Low => "low",
        Severity.Tags.Medium => "medium",
        Severity.Tags.High => "high",
        Severity.Tags.Critical => "critical",
        _ => "medium"
    };

    public static Severity FromString(string s) => s.ToLowerInvariant() switch
    {
        "low"      => Severity.Low,
        "medium"   => Severity.Medium,
        "high"     => Severity.High,
        "critical" => Severity.Critical,
        _          => Severity.Medium
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
