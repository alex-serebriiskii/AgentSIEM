using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Siem.Api.Data;
using Siem.Api.Data.Entities;
using Severity = Siem.Api.Data.Enums.Severity;
using Siem.Api.Services;
using Siem.LoadTests.Fixtures;
using Siem.Rules.Core;

namespace Siem.LoadTests.Helpers;

/// <summary>
/// Generates varied rule sets for load testing the F# rules engine.
/// </summary>
public static class LoadTestRuleFactory
{
    private static readonly string[] FieldConditions =
    [
        """{"type":"field","field":"eventType","operator":"Eq","value":"tool_invocation"}""",
        """{"type":"field","field":"eventType","operator":"Eq","value":"llm_call"}""",
        """{"type":"field","field":"eventType","operator":"Eq","value":"rag_retrieval"}""",
        """{"type":"field","field":"eventType","operator":"Eq","value":"external_api_call"}""",
        """{"type":"field","field":"agentName","operator":"Contains","value":"Agent"}""",
        """{"type":"field","field":"toolName","operator":"Eq","value":"shell_run"}""",
        """{"type":"field","field":"toolName","operator":"Eq","value":"code_execute"}""",
        """{"type":"field","field":"modelId","operator":"Eq","value":"gpt-4"}""",
    ];

    private static readonly string[] ThresholdConditions =
    [
        """{"type":"threshold","field":"latencyMs","limit":500.0,"above":true}""",
        """{"type":"threshold","field":"latencyMs","limit":1000.0,"above":true}""",
        """{"type":"threshold","field":"inputTokens","limit":1000,"above":true}""",
        """{"type":"threshold","field":"outputTokens","limit":2000,"above":true}""",
    ];

    /// <summary>
    /// Create varied SingleEvent rules with different condition trees.
    /// </summary>
    public static List<RuleEntity> CreateVariedSingleEventRules(int count)
    {
        var rules = new List<RuleEntity>();
        var severities = new[] { Severity.Low, Severity.Medium, Severity.High, Severity.Critical };
        var now = DateTime.UtcNow;

        for (int i = 0; i < count; i++)
        {
            var conditionJson = (i % 4) switch
            {
                0 => FieldConditions[i % FieldConditions.Length],
                1 => ThresholdConditions[i % ThresholdConditions.Length],
                2 => $$$"""{"type":"and","conditions":[{{{FieldConditions[i % FieldConditions.Length]}}},{{{ThresholdConditions[i % ThresholdConditions.Length]}}}]}""",
                _ => $$$"""{"type":"or","conditions":[{{{FieldConditions[i % FieldConditions.Length]}}},{{{ThresholdConditions[i % ThresholdConditions.Length]}}}]}""",
            };

            rules.Add(new RuleEntity
            {
                Id = Guid.NewGuid(),
                Name = $"LoadTest SingleEvent Rule {i}",
                Description = $"Load test rule {i}",
                Enabled = true,
                Severity = severities[i % severities.Length],
                ConditionJson = conditionJson,
                EvaluationType = "SingleEvent",
                ActionsJson = "[]",
                Tags = [],
                CreatedBy = "load-test",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        return rules;
    }

    /// <summary>
    /// Create varied Temporal rules with different windows and thresholds.
    /// </summary>
    public static List<RuleEntity> CreateVariedTemporalRules(int count)
    {
        var rules = new List<RuleEntity>();
        var now = DateTime.UtcNow;

        for (int i = 0; i < count; i++)
        {
            var windowSeconds = 30 + (i * 15); // 30s, 45s, 60s, ...
            var threshold = 3 + i;

            rules.Add(new RuleEntity
            {
                Id = Guid.NewGuid(),
                Name = $"LoadTest Temporal Rule {i}",
                Description = $"Temporal rule with {windowSeconds}s window, threshold {threshold}",
                Enabled = true,
                Severity = Severity.High,
                ConditionJson = FieldConditions[i % FieldConditions.Length],
                EvaluationType = "Temporal",
                TemporalConfig = $$"""{"windowSeconds":{{windowSeconds}},"threshold":{{threshold}},"aggregation":"count","partitionField":"agentId"}""",
                ActionsJson = "[]",
                Tags = [],
                CreatedBy = "load-test",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        return rules;
    }

    /// <summary>
    /// Create Sequence rules with different step patterns.
    /// </summary>
    public static List<RuleEntity> CreateVariedSequenceRules(int count)
    {
        var rules = new List<RuleEntity>();
        var now = DateTime.UtcNow;

        var stepPatterns = new[]
        {
            new[] { ("rag_step", """{"type":"field","field":"eventType","operator":"Eq","value":"rag_retrieval"}"""), ("api_step", """{"type":"field","field":"eventType","operator":"Eq","value":"external_api_call"}""") },
            new[] { ("tool_step", """{"type":"field","field":"eventType","operator":"Eq","value":"tool_invocation"}"""), ("llm_step", """{"type":"field","field":"eventType","operator":"Eq","value":"llm_call"}""") },
            new[] { ("perm_step", """{"type":"field","field":"eventType","operator":"Eq","value":"permission_check"}"""), ("tool_step", """{"type":"field","field":"eventType","operator":"Eq","value":"tool_invocation"}"""), ("api_step", """{"type":"field","field":"eventType","operator":"Eq","value":"external_api_call"}""") },
        };

        for (int i = 0; i < count; i++)
        {
            var steps = stepPatterns[i % stepPatterns.Length];
            var stepsJson = string.Join(",", steps.Select(s =>
                $$"""{"label":"{{s.Item1}}","condition":{{s.Item2}}}"""));

            rules.Add(new RuleEntity
            {
                Id = Guid.NewGuid(),
                Name = $"LoadTest Sequence Rule {i}",
                Description = $"Sequence rule with {steps.Length} steps",
                Enabled = true,
                Severity = Severity.Critical,
                ConditionJson = """{"type":"exists","field":"eventType"}""",
                EvaluationType = "Sequence",
                SequenceConfig = $$"""{"maxSpanSeconds":300,"steps":[{{stepsJson}}]}""",
                ActionsJson = "[]",
                Tags = [],
                CreatedBy = "load-test",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        return rules;
    }

    /// <summary>
    /// Seed rules in DB, compile them via F# engine, and return a ready CompiledRulesCache.
    /// </summary>
    public static async Task<CompiledRulesCache> CompileAndCacheRulesAsync(
        IEnumerable<RuleEntity> rules)
    {
        await using var db = LoadTestFixture.CreateDbContext();
        db.Rules.AddRange(rules);
        await db.SaveChangesAsync();

        var dbFactory = new LoadTestDbContextFactory();
        var ruleLoader = new RuleLoadingService(dbFactory, NullLogger<RuleLoadingService>.Instance);
        var loadedRules = await ruleLoader.LoadEnabledRulesAsync();

        var listResolver = Microsoft.FSharp.Core.FuncConvert.FromFunc<Guid, Microsoft.FSharp.Collections.FSharpSet<string>>(
            _ => Microsoft.FSharp.Collections.SetModule.Empty<string>());
        var compiledRules = Engine.compileAllRules(listResolver, loadedRules.ToFSharpList());

        var stateProvider = new RedisStateProvider(LoadTestFixture.RedisMultiplexer);
        var cache = new CompiledRulesCache(stateProvider);

        var listCache = new ListCacheService(
            NSubstitute.Substitute.For<IDbContextFactory<SiemDbContext>>(),
            NullLogger<ListCacheService>.Instance);
        cache.SwapEngine(compiledRules, listCache);

        return cache;
    }

    private class LoadTestDbContextFactory : IDbContextFactory<SiemDbContext>
    {
        public SiemDbContext CreateDbContext() => LoadTestFixture.CreateDbContext();
    }
}
