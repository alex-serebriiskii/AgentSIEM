using FluentAssertions;
using Siem.Api.Services;

namespace Siem.Api.Tests.Services;

public class CompilationNotifierTests
{
    [Test]
    public async Task WaitForNextCompilationAsync_CompletesWhenNotified()
    {
        var notifier = new CompilationNotifier(new RecompilationConfig());

        var waitTask = notifier.WaitForNextCompilationAsync(CancellationToken.None);
        waitTask.IsCompleted.Should().BeFalse();

        notifier.NotifyCompilationComplete();

        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task WaitForNextCompilationAsync_MultipleWaitersAllUnblock()
    {
        var notifier = new CompilationNotifier(new RecompilationConfig());

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => notifier.WaitForNextCompilationAsync(CancellationToken.None))
            .ToArray();

        tasks.Should().AllSatisfy(t => t.IsCompleted.Should().BeFalse());

        notifier.NotifyCompilationComplete();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task WaitForNextCompilationAsync_TimesOut()
    {
        var config = new RecompilationConfig { SignalTimeoutSeconds = 1 };
        var notifier = new CompilationNotifier(config);

        var act = () => notifier.WaitForNextCompilationAsync(CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task WaitForNextCompilationAsync_RespectsCancellationToken()
    {
        var notifier = new CompilationNotifier(new RecompilationConfig());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => notifier.WaitForNextCompilationAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task NotifyCompilationComplete_ResetsForNextWait()
    {
        var notifier = new CompilationNotifier(new RecompilationConfig());

        // First cycle: wait + notify
        var firstWait = notifier.WaitForNextCompilationAsync(CancellationToken.None);
        notifier.NotifyCompilationComplete();
        await firstWait.WaitAsync(TimeSpan.FromSeconds(1));

        // Second cycle: new wait should block again
        var secondWait = notifier.WaitForNextCompilationAsync(CancellationToken.None);
        secondWait.IsCompleted.Should().BeFalse();

        // Second notify unblocks second wait
        notifier.NotifyCompilationComplete();
        await secondWait.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
