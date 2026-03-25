using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data;
using Siem.Rules.Core;
using static Siem.Rules.Core.Evaluator;

namespace Siem.Api.Alerting;

/// <summary>
/// Scoped service that enriches alerts with context from the database.
/// Runs AFTER noise reduction (no point enriching an alert we're going to drop)
/// and BEFORE persistence (enriched data is stored with the alert).
/// </summary>
public class AlertEnricher
{
    private readonly SiemDbContext _db;
    private readonly ILogger<AlertEnricher> _logger;

    public AlertEnricher(SiemDbContext db, ILogger<AlertEnricher> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<EnrichedAlert> EnrichAsync(
        EvaluationResult result,
        AgentEvent evt,
        CancellationToken ct = default)
    {
        // Load the rule definition for name and labels
        var rule = await _db.Rules.FindAsync(new object[] { result.RuleId }, ct);

        // Count recent alerts for this agent (last 24h) -- helps with triage
        var recentAlertCount = await _db.Alerts
            .CountAsync(a =>
                a.AgentId == evt.AgentId &&
                a.TriggeredAt >= DateTime.UtcNow.AddHours(-24),
                ct);

        // Get session event count for context
        var session = await _db.AgentSessions
            .FirstOrDefaultAsync(s => s.SessionId == evt.SessionId, ct);

        // Find distinct tools used in this session recently
        var recentTools = await _db.AgentEvents
            .Where(e => e.SessionId == evt.SessionId &&
                        e.ToolName != null &&
                        e.Timestamp >= DateTime.UtcNow.AddHours(-1))
            .Select(e => e.ToolName!)
            .Distinct()
            .Take(10)
            .ToArrayAsync(ct);

        // Parse rule labels from the actions config
        var labels = new Dictionary<string, string>();
        if (rule?.ActionsJson != null)
        {
            try
            {
                var actions = JsonSerializer.Deserialize<List<JsonElement>>(rule.ActionsJson);
                if (actions != null)
                {
                    var createAlertAction = actions.FirstOrDefault(a =>
                        a.TryGetProperty("type", out var t) && t.GetString() == "create_alert");

                    if (createAlertAction.ValueKind != JsonValueKind.Undefined &&
                        createAlertAction.TryGetProperty("labels", out var labelsEl))
                    {
                        foreach (var prop in labelsEl.EnumerateObject())
                        {
                            labels[prop.Name] = prop.Value.GetString() ?? "";
                        }
                    }
                }
            }
            catch
            {
                // Non-critical, proceed without labels
            }
        }

        // Convert F# Map to C# dictionary for the context
        var ruleContext = new Dictionary<string, object>();
        foreach (var kvp in result.Context)
        {
            ruleContext[kvp.Key] = kvp.Value;
        }

        var severityStr = result.Severity.Tag switch
        {
            Severity.Tags.Low => "low",
            Severity.Tags.Medium => "medium",
            Severity.Tags.High => "high",
            Severity.Tags.Critical => "critical",
            _ => "medium"
        };

        return new EnrichedAlert
        {
            RuleId = result.RuleId,
            RuleName = rule?.Name ?? "Unknown rule",
            Severity = severityStr,
            Title = $"[{severityStr.ToUpper()}] {rule?.Name ?? "Rule triggered"}: {evt.AgentName}",
            Detail = result.Detail != null
                ? Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result.Detail)
                    ? result.Detail.Value
                    : "Rule conditions matched"
                : "Rule conditions matched",
            AgentId = evt.AgentId,
            AgentName = evt.AgentName,
            SessionId = evt.SessionId,
            RecentAlertCount = recentAlertCount,
            SessionEventCount = session?.EventCount ?? 0,
            RecentTools = recentTools,
            RuleContext = ruleContext,
            Labels = labels,
            TriggeredAt = DateTime.UtcNow
        };
    }
}
