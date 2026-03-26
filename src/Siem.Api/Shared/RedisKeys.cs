namespace Siem.Api.Shared;

public static class RedisKeys
{
    public static string AlertDedup(string fingerprint) => $"alert:dedup:{fingerprint}";
    public static string AlertThrottle(Guid ruleId) => $"alert:throttle:{ruleId}";
}
