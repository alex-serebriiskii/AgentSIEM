using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Api.Data.Entities;

namespace Siem.Api.Alerting;

/// <summary>
/// Checks for active suppressions -- user-created rules that silence
/// specific rule+agent combinations for a set duration.
/// </summary>
public class SuppressionChecker
{
    private readonly SiemDbContext _db;

    public SuppressionChecker(SiemDbContext db) => _db = db;

    /// <summary>
    /// Returns true if there is an active (non-expired) suppression matching
    /// the given rule and/or agent. Suppressions can target: a specific rule,
    /// a specific agent, or a rule+agent combination.
    /// </summary>
    public async Task<bool> IsSuppressedAsync(
        Guid ruleId, string agentId, CancellationToken ct = default)
    {
        return await _db.Suppressions
            .Where(s => s.ExpiresAt > DateTime.UtcNow)
            .Where(s =>
                (s.RuleId == ruleId && s.AgentId == null) ||
                (s.RuleId == null && s.AgentId == agentId) ||
                (s.RuleId == ruleId && s.AgentId == agentId))
            .AnyAsync(ct);
    }
}
