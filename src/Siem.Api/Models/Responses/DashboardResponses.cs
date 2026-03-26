namespace Siem.Api.Models.Responses;

public record TopAgentResult(
    string AgentId,
    string AgentName,
    long TotalEvents,
    long TotalTokens,
    double MaxLatencyMs);

public record EventVolumeResult(
    DateTime Bucket,
    long EventCount,
    long TotalTokens);

public record AlertDistributionResult(
    string Severity,
    string Status,
    int Count);

public record ToolUsageResult(
    string ToolName,
    long InvocationCount,
    double AvgLatencyMs,
    long UniqueSessions);
