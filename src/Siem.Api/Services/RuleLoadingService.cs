using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Api.Data;
using Siem.Api.Shared;
using Siem.Rules.Core;

namespace Siem.Api.Services;

/// <summary>
/// Loads rule definitions from the database and converts them to F# types.
/// The DB stores conditions as JSONB; this service parses them into
/// the F# Condition discriminated union via the Serialization module.
/// </summary>
public class RuleLoadingService
{
    private readonly SiemDbContext _db;
    private readonly ILogger<RuleLoadingService> _logger;

    public RuleLoadingService(SiemDbContext db, ILogger<RuleLoadingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<RuleDefinition>> LoadEnabledRulesAsync(
        CancellationToken ct = default)
    {
        var dbRules = await _db.Rules
            .Where(r => r.Enabled)
            .ToListAsync(ct);

        var fsharpRules = new List<RuleDefinition>();

        foreach (var dbRule in dbRules)
        {
            try
            {
                // Parse the JSONB condition into the F# discriminated union
                var conditionJson = JsonDocument.Parse(dbRule.ConditionJson).RootElement;
                var condition = Serialization.parseCondition(conditionJson);

                // Map the DB entity to the F# RuleDefinition record
                var rule = new RuleDefinition(
                    id:             dbRule.Id,
                    name:           dbRule.Name,
                    description:    dbRule.Description,
                    enabled:        dbRule.Enabled,
                    severity:       SeverityMapping.FromEnum(dbRule.Severity),
                    condition:      condition,
                    evaluationType: MapEvaluationType(dbRule),
                    actions:        MapActions(dbRule.ActionsJson),
                    tags:           dbRule.Tags?.ToFSharpList()
                                        ?? ListModule.Empty<string>(),
                    createdBy:      dbRule.CreatedBy,
                    createdAt:      dbRule.CreatedAt,
                    updatedAt:      dbRule.UpdatedAt
                );

                fsharpRules.Add(rule);
            }
            catch (Exception ex)
            {
                // A malformed rule shouldn't take down the engine.
                _logger.LogError(ex, "Failed to parse rule {RuleId}: {RuleName}",
                    dbRule.Id, dbRule.Name);
            }
        }

        return fsharpRules;
    }

    private static EvaluationType MapEvaluationType(Data.Entities.RuleEntity rule)
    {
        return rule.EvaluationType switch
        {
            "Temporal" when rule.TemporalConfig is not null =>
                EvaluationType.NewTemporal(ParseTemporalConfig(rule.TemporalConfig)),

            "Sequence" when rule.SequenceConfig is not null =>
                EvaluationType.NewSequence(ParseSequenceConfig(rule.SequenceConfig)),

            _ => EvaluationType.SingleEvent
        };
    }

    private static TemporalConfig ParseTemporalConfig(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var windowSeconds = root.GetProperty("windowSeconds").GetDouble();
        var threshold = root.GetProperty("threshold").GetDouble();
        var partitionField = root.GetProperty("partitionField").GetString() ?? "agentId";

        var aggregation = root.TryGetProperty("aggregation", out var aggEl)
            ? aggEl.GetString() switch
            {
                "Rate" => TemporalAggregation.Rate,
                _ => TemporalAggregation.Count
            }
            : TemporalAggregation.Count;

        return new TemporalConfig(
            windowDuration: TimeSpan.FromSeconds(windowSeconds),
            threshold: threshold,
            aggregation: aggregation,
            partitionField: partitionField
        );
    }

    private static SequenceConfig ParseSequenceConfig(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var maxSpanSeconds = root.GetProperty("maxSpanSeconds").GetDouble();

        var steps = new List<SequenceStep>();
        foreach (var stepEl in root.GetProperty("steps").EnumerateArray())
        {
            var label = stepEl.GetProperty("label").GetString() ?? "";
            var conditionEl = stepEl.GetProperty("condition");
            var condition = Serialization.parseCondition(conditionEl);
            steps.Add(new SequenceStep(label: label, condition: condition));
        }

        return new SequenceConfig(
            maxSpan: TimeSpan.FromSeconds(maxSpanSeconds),
            steps: steps.ToFSharpList()
        );
    }

    private static FSharpList<RuleAction> MapActions(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ListModule.Empty<RuleAction>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
            return ListModule.Empty<RuleAction>();

        var actions = new List<RuleAction>();

        foreach (var actionEl in root.EnumerateArray())
        {
            var actionType = actionEl.GetProperty("type").GetString();

            switch (actionType)
            {
                case "create_alert":
                {
                    var labels = ParseStringMap(actionEl, "labels");
                    var assignTo = actionEl.TryGetProperty("assignTo", out var assignEl)
                        ? FSharpOption<string>.Some(assignEl.GetString()!)
                        : FSharpOption<string>.None;
                    actions.Add(RuleAction.NewCreateAlert(
                        MapModule.OfSeq(labels), assignTo));
                    break;
                }
                case "enrich_event":
                {
                    var fields = ParseStringMap(actionEl, "fields");
                    actions.Add(RuleAction.NewEnrichEvent(MapModule.OfSeq(fields)));
                    break;
                }
                case "suppress":
                {
                    var durationSeconds = actionEl.GetProperty("durationSeconds").GetDouble();
                    var reason = actionEl.TryGetProperty("reason", out var reasonEl)
                        ? reasonEl.GetString() ?? ""
                        : "";
                    actions.Add(RuleAction.NewSuppress(
                        TimeSpan.FromSeconds(durationSeconds), reason));
                    break;
                }
                case "webhook":
                {
                    var url = actionEl.GetProperty("url").GetString() ?? "";
                    var headers = ParseStringMap(actionEl, "headers");
                    actions.Add(RuleAction.NewWebhook(url, MapModule.OfSeq(headers)));
                    break;
                }
            }
        }

        return actions.ToFSharpList();
    }

    private static IEnumerable<Tuple<string, string>> ParseStringMap(
        JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var mapEl)
            || mapEl.ValueKind != JsonValueKind.Object)
            return [];

        return mapEl.EnumerateObject()
            .Select(p => Tuple.Create(p.Name, p.Value.GetString() ?? ""));
    }
}
