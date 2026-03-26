using Microsoft.EntityFrameworkCore;
using Npgsql;
using Siem.Api.Data;

namespace Siem.Api.Alerting;

/// <summary>
/// Checks for active suppressions -- user-created rules that silence
/// specific rule+agent combinations for a set duration.
/// </summary>
public class SuppressionChecker
{
    private readonly SiemDbContext _db;
    private readonly ILogger<SuppressionChecker> _logger;

    public SuppressionChecker(SiemDbContext db, ILogger<SuppressionChecker> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Returns true if there is an active (non-expired) suppression matching
    /// the given rule and/or agent. Suppressions can target: a specific rule,
    /// a specific agent, or a rule+agent combination.
    /// </summary>
    public virtual async Task<bool> IsSuppressedAsync(
        Guid ruleId, string agentId, CancellationToken ct = default)
    {
        try
        {
            return await _db.Suppressions
                .Where(s => s.ExpiresAt > DateTime.UtcNow)
                .Where(s =>
                    (s.RuleId == ruleId && s.AgentId == null) ||
                    (s.RuleId == null && s.AgentId == agentId) ||
                    (s.RuleId == ruleId && s.AgentId == agentId))
                .AnyAsync(ct);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex,
                "Failed to check suppressions for rule {RuleId} agent {AgentId}",
                ruleId, agentId);
            return false;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex,
                "Timeout checking suppressions for rule {RuleId} agent {AgentId}",
                ruleId, agentId);
            return false;
        }
    }
}
