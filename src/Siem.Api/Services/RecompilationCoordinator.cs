using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Api.Data;
using Siem.Rules.Core;

namespace Siem.Api.Services;

public enum InvalidationReason
{
    RuleCreated,
    RuleUpdated,
    RuleDeleted,
    ListUpdated,
    ListDeleted,
    Startup,
    ManualReload
}

public record InvalidationSignal(
    InvalidationReason Reason,
    Guid? EntityId = null,
    string? Detail = null
);

/// <summary>
/// BackgroundService that orchestrates rule recompilation.
/// Receives invalidation signals, debounces rapid changes (500ms window),
/// serializes compilation (only one at a time), refreshes lists + compiles
/// rules atomically, validates before swapping, and publishes the new engine
/// via atomic volatile write.
/// </summary>
public class RecompilationCoordinator : BackgroundService, IRecompilationCoordinator
{
    private readonly Channel<InvalidationSignal> _channel;
    private readonly IListCacheService _listCache;
    private readonly ICompiledRulesCache _rulesCache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecompilationCoordinator> _logger;

    // Debounce window: wait this long after the last signal before compiling
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);

    // If compilation hasn't happened in this long, force one (safety net)
    private static readonly TimeSpan MaxDebounceDelay = TimeSpan.FromSeconds(5);

    // Event that fires after each successful compilation
    private CancellationTokenSource _compilationCompleted = new();

    public RecompilationCoordinator(
        IListCacheService listCache,
        ICompiledRulesCache rulesCache,
        IServiceScopeFactory scopeFactory,
        ILogger<RecompilationCoordinator> logger)
    {
        _listCache = listCache;
        _rulesCache = rulesCache;
        _scopeFactory = scopeFactory;
        _logger = logger;

        // Bounded channel: if signals pile up faster than we can process,
        // drop the oldest -- we'll reload everything anyway.
        _channel = Channel.CreateBounded<InvalidationSignal>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
    }

    /// <summary>
    /// Public API: any service can signal that recompilation is needed.
    /// This is fire-and-forget from the caller's perspective.
    /// </summary>
    public void SignalInvalidation(InvalidationSignal signal)
    {
        if (!_channel.Writer.TryWrite(signal))
        {
            _logger.LogWarning(
                "Invalidation channel full, signal dropped: {Reason}", signal.Reason);
        }
    }

    /// <summary>
    /// Signal and wait for the recompilation to complete.
    /// Used by REST controllers that want to return a response only after
    /// the new rules are active (important for test/validation workflows).
    /// </summary>
    public async Task SignalAndWaitAsync(
        InvalidationSignal signal, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register a one-shot callback for the next compilation completion
        var registration = _compilationCompleted.Token.Register(() => tcs.TrySetResult());

        SignalInvalidation(signal);

        try
        {
            // Wait for compilation with a reasonable timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        finally
        {
            await registration.DisposeAsync();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Recompilation coordinator started");

        // Initial compilation at startup
        await RunCompilationAsync(
            new InvalidationSignal(InvalidationReason.Startup), stoppingToken);

        // Main loop: read signals, debounce, compile
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for the first signal
                var firstSignal = await _channel.Reader.ReadAsync(stoppingToken);
                var signals = new List<InvalidationSignal> { firstSignal };
                var debounceStart = DateTime.UtcNow;

                // Debounce: keep reading signals until the window expires
                // or we hit the maximum delay
                while (DateTime.UtcNow - debounceStart < MaxDebounceDelay)
                {
                    using var delayCts = CancellationTokenSource
                        .CreateLinkedTokenSource(stoppingToken);
                    delayCts.CancelAfter(DebounceWindow);

                    try
                    {
                        var nextSignal = await _channel.Reader.ReadAsync(delayCts.Token);
                        signals.Add(nextSignal);
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Debounce window expired with no new signals -- time to compile
                        break;
                    }
                }

                _logger.LogInformation(
                    "Debounce complete: {SignalCount} signals coalesced. Reasons: {Reasons}",
                    signals.Count,
                    string.Join(", ", signals.Select(s => s.Reason).Distinct()));

                // Run the actual compilation
                await RunCompilationAsync(signals[^1], stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Recompilation failed -- engine continues with previous rule set");

                // Don't crash the loop. The old engine is still valid.
                // Wait a bit before retrying to avoid tight failure loops.
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task RunCompilationAsync(
        InvalidationSignal trigger, CancellationToken ct)
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
        NotifyCompilationComplete();
    }

    private void NotifyCompilationComplete()
    {
        var old = Interlocked.Exchange(
            ref _compilationCompleted, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
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
