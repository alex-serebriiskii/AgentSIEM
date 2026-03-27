using System.Diagnostics;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Rules.Core;

namespace Siem.Api.Services;

public class RuleCompilationOrchestrator : IRuleCompilationOrchestrator
{
    private readonly IListCacheService _listCache;
    private readonly ICompiledRulesCache _rulesCache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICompilationNotifier _notifier;
    private readonly ILogger<RuleCompilationOrchestrator> _logger;

    public RuleCompilationOrchestrator(
        IListCacheService listCache,
        ICompiledRulesCache rulesCache,
        IServiceScopeFactory scopeFactory,
        ICompilationNotifier notifier,
        ILogger<RuleCompilationOrchestrator> logger)
    {
        _listCache = listCache;
        _rulesCache = rulesCache;
        _scopeFactory = scopeFactory;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task CompileAsync(InvalidationSignal trigger, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var compilationId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogInformation(
            "[{CompilationId}] Starting recompilation (trigger: {Reason})",
            compilationId, trigger.Reason);

        // Step 1: Refresh list cache from DB
        var listVersion = await _listCache.RefreshAsync(ct);

        // Step 2: Load all enabled rules from DB (scoped service)
        List<RuleDefinition> rules;
        using (var scope = _scopeFactory.CreateScope())
        {
            var ruleLoader = scope.ServiceProvider.GetRequiredService<RuleLoadingService>();
            rules = await ruleLoader.LoadEnabledRulesAsync(ct);
        }

        // Step 3: Build the F# list resolver (bridges C# cache -> F# function)
        var listResolver = FuncConvert.FromFunc<Guid, FSharpSet<string>>(
            listId => _listCache.ResolveList(listId));

        // Step 4: Compile all rules via F# engine
        var compiled = Engine.compileAllRules(
            listResolver, ListModule.OfSeq(rules));

        // Step 5: Validate -- run test events against the new engine
        var validationErrors = ValidateCompiledRules(compiled);

        if (validationErrors.Count > 0)
        {
            _logger.LogWarning(
                "[{CompilationId}] Validation found {ErrorCount} issues: {Errors}",
                compilationId, validationErrors.Count,
                string.Join("; ", validationErrors));
        }

        // Step 6: Atomic swap -- construct new engine and publish
        _rulesCache.SwapEngine(compiled, _listCache);

        sw.Stop();
        _logger.LogInformation(
            "[{CompilationId}] Recompilation complete: {RuleCount} rules in {ElapsedMs}ms " +
            "(list version: {ListVersion})",
            compilationId, compiled.Length, sw.ElapsedMilliseconds, listVersion);

        // Notify anyone waiting on SignalAndWaitAsync
        _notifier.NotifyCompilationComplete();
    }

    /// <summary>
    /// Run a synthetic test event through each compiled rule to catch
    /// runtime errors that compilation doesn't surface.
    /// </summary>
    private static List<string> ValidateCompiledRules(
        FSharpList<Compiler.CompiledRule> compiled)
    {
        var errors = new List<string>();

        var testEvent = new AgentEvent(
            eventId:      Guid.NewGuid(),
            timestamp:    DateTime.UtcNow,
            sessionId:    "validation-session",
            traceId:      "validation-trace",
            agentId:      "validation-agent",
            agentName:    "Validation Agent",
            eventType:    "tool_invocation",
            modelId:      FSharpOption<string>.Some("test-model"),
            inputTokens:  FSharpOption<int>.Some(100),
            outputTokens: FSharpOption<int>.Some(200),
            latencyMs:    FSharpOption<double>.Some(50.0),
            toolName:     FSharpOption<string>.Some("test-tool"),
            toolInput:    FSharpOption<string>.Some("test-input"),
            toolOutput:   FSharpOption<string>.Some("test-output"),
            contentHash:  FSharpOption<string>.Some("abc123"),
            properties:   MapModule.Empty<string, System.Text.Json.JsonElement>()
        );

        foreach (var rule in compiled)
        {
            try
            {
                // Just run the predicate -- we don't care about the result,
                // we care that it doesn't throw
                rule.Predicate.Invoke(testEvent);

                // Also validate sequence step predicates
                if (FSharpOption<FSharpList<Tuple<string, FSharpFunc<AgentEvent, bool>>>>
                        .get_IsSome(rule.CompiledSteps))
                {
                    var steps = rule.CompiledSteps.Value;
                    foreach (var step in steps)
                    {
                        step.Item2.Invoke(testEvent);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Rule {rule.RuleId}: {ex.GetType().Name} -- {ex.Message}");
            }
        }

        return errors;
    }
}
