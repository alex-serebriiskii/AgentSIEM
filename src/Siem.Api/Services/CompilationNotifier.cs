namespace Siem.Api.Services;

public class CompilationNotifier : ICompilationNotifier
{
    private readonly RecompilationConfig _config;
    private CancellationTokenSource _compilationCompleted = new();

    public CompilationNotifier(RecompilationConfig config)
    {
        _config = config;
    }

    public async Task WaitForNextCompilationAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register a one-shot callback for the next compilation completion
        var registration = _compilationCompleted.Token.Register(() => tcs.TrySetResult());

        try
        {
            // Wait for compilation with a reasonable timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_config.SignalTimeoutSeconds));

            await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        finally
        {
            await registration.DisposeAsync();
        }
    }

    public void NotifyCompilationComplete()
    {
        var old = Interlocked.Exchange(
            ref _compilationCompleted, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}
