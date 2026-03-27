using System.Threading.Channels;

namespace Siem.Api.Services;

/// <summary>
/// BackgroundService that receives invalidation signals, debounces rapid
/// changes (500ms window), and delegates compilation to the orchestrator.
/// </summary>
public class RecompilationCoordinator : BackgroundService, IRecompilationCoordinator
{
    private readonly Channel<InvalidationSignal> _channel;
    private readonly IRuleCompilationOrchestrator _orchestrator;
    private readonly ICompilationNotifier _notifier;
    private readonly ILogger<RecompilationCoordinator> _logger;
    private readonly RecompilationConfig _config;

    // Debounce timings read from config
    private readonly TimeSpan _debounceWindow;
    private readonly TimeSpan _maxDebounceDelay;

    public RecompilationCoordinator(
        IRuleCompilationOrchestrator orchestrator,
        ICompilationNotifier notifier,
        ILogger<RecompilationCoordinator> logger,
        RecompilationConfig config)
    {
        _orchestrator = orchestrator;
        _notifier = notifier;
        _logger = logger;
        _config = config;
        _debounceWindow = TimeSpan.FromMilliseconds(config.DebounceWindowMs);
        _maxDebounceDelay = TimeSpan.FromSeconds(config.MaxDebounceDelaySeconds);

        // Bounded channel: if signals pile up faster than we can process,
        // drop the oldest -- we'll reload everything anyway.
        _channel = Channel.CreateBounded<InvalidationSignal>(
            new BoundedChannelOptions(config.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
    }

    /// <summary>
    /// Public API: any service can signal that recompilation is needed.
    /// This is fire-and-forget from the caller's perspective.
    /// </summary>
    public bool SignalInvalidation(InvalidationSignal signal)
    {
        if (!_channel.Writer.TryWrite(signal))
        {
            _logger.LogWarning(
                "Invalidation channel full, signal dropped: {Reason}", signal.Reason);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Signal and wait for the recompilation to complete.
    /// Used by REST controllers that want to return a response only after
    /// the new rules are active (important for test/validation workflows).
    /// </summary>
    public async Task SignalAndWaitAsync(
        InvalidationSignal signal, CancellationToken ct = default)
    {
        SignalInvalidation(signal);
        await _notifier.WaitForNextCompilationAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Recompilation coordinator started");

        // Initial compilation at startup
        await _orchestrator.CompileAsync(
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
                while (DateTime.UtcNow - debounceStart < _maxDebounceDelay)
                {
                    using var delayCts = CancellationTokenSource
                        .CreateLinkedTokenSource(stoppingToken);
                    delayCts.CancelAfter(_debounceWindow);

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
                await _orchestrator.CompileAsync(signals[^1], stoppingToken);
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
                await Task.Delay(TimeSpan.FromSeconds(_config.ErrorRecoveryDelaySeconds), stoppingToken);
            }
        }
    }
}
