namespace Siem.Api.Data.Enums;

public static class EnumExtensions
{
    public static string ToStorageString(this AlertStatus status) => status switch
    {
        AlertStatus.Open => "open",
        AlertStatus.Acknowledged => "acknowledged",
        AlertStatus.Resolved => "resolved",
        _ => "open"
    };

    public static string ToStorageString(this Severity severity) => severity switch
    {
        Severity.Low => "low",
        Severity.Medium => "medium",
        Severity.High => "high",
        Severity.Critical => "critical",
        _ => "medium"
    };

    public static bool TryParseAlertStatus(string? value, out AlertStatus result)
    {
        result = value?.ToLowerInvariant() switch
        {
            "open" => AlertStatus.Open,
            "acknowledged" => AlertStatus.Acknowledged,
            "resolved" => AlertStatus.Resolved,
            _ => default
        };

        return value?.ToLowerInvariant() is "open" or "acknowledged" or "resolved";
    }

    public static bool TryParseSeverity(string? value, out Severity result)
    {
        result = value?.ToLowerInvariant() switch
        {
            "low" => Severity.Low,
            "medium" => Severity.Medium,
            "high" => Severity.High,
            "critical" => Severity.Critical,
            _ => default
        };

        return value?.ToLowerInvariant() is "low" or "medium" or "high" or "critical";
    }

    public static AlertStatus ParseAlertStatus(string value)
    {
        if (TryParseAlertStatus(value, out var result))
            return result;
        throw new ArgumentException($"Invalid alert status: '{value}'", nameof(value));
    }

    public static Severity ParseSeverity(string value)
    {
        if (TryParseSeverity(value, out var result))
            return result;
        throw new ArgumentException($"Invalid severity: '{value}'", nameof(value));
    }

    public static int ToOrder(this Severity severity) => severity switch
    {
        Severity.Low => 0,
        Severity.Medium => 1,
        Severity.High => 2,
        Severity.Critical => 3,
        _ => 0
    };

    public static bool IsValidTransition(AlertStatus from, AlertStatus to) =>
        (from, to) switch
        {
            (AlertStatus.Open, AlertStatus.Acknowledged) => true,
            (AlertStatus.Open, AlertStatus.Resolved) => true,
            (AlertStatus.Acknowledged, AlertStatus.Resolved) => true,
            _ => false
        };
}
