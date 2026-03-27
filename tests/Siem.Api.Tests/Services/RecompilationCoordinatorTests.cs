using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Siem.Api.Services;

namespace Siem.Api.Tests.Services;

public class RecompilationCoordinatorTests : IDisposable
{
    private readonly IRuleCompilationOrchestrator _orchestrator;
    private readonly ICompilationNotifier _notifier;
    private readonly RecompilationCoordinator _coordinator;

    public RecompilationCoordinatorTests()
    {
        _orchestrator = Substitute.For<IRuleCompilationOrchestrator>();
        _notifier = Substitute.For<ICompilationNotifier>();
        _coordinator = new RecompilationCoordinator(
            _orchestrator,
            _notifier,
            NullLogger<RecompilationCoordinator>.Instance,
            new RecompilationConfig());
    }

    public void Dispose() => _coordinator.Dispose();

    [Test]
    public async Task SignalInvalidation_ReturnsTrue()
    {
        var result = _coordinator.SignalInvalidation(
            new InvalidationSignal(InvalidationReason.RuleUpdated));

        result.Should().BeTrue();
    }

    [Test]
    public async Task SignalAndWaitAsync_DelegatesToNotifier()
    {
        var signal = new InvalidationSignal(InvalidationReason.ManualReload);

        await _coordinator.SignalAndWaitAsync(signal);

        await _notifier.Received(1).WaitForNextCompilationAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_RunsInitialCompilation()
    {
        using var cts = new CancellationTokenSource();

        // Let the initial compilation complete, then stop
        _orchestrator.CompileAsync(Arg.Any<InvalidationSignal>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => cts.Cancel());

        await _coordinator.StartAsync(cts.Token);

        // Give it a moment to run ExecuteAsync
        try { await Task.Delay(200, cts.Token); } catch (OperationCanceledException) { }

        await _coordinator.StopAsync(CancellationToken.None);

        await _orchestrator.Received().CompileAsync(
            Arg.Is<InvalidationSignal>(s => s.Reason == InvalidationReason.Startup),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_DebouncesMultipleSignals()
    {
        var compilationCount = 0;
        var config = new RecompilationConfig
        {
            DebounceWindowMs = 100,
            MaxDebounceDelaySeconds = 1
        };
        var coordinator = new RecompilationCoordinator(
            _orchestrator, _notifier,
            NullLogger<RecompilationCoordinator>.Instance, config);

        _orchestrator.CompileAsync(Arg.Any<InvalidationSignal>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => Interlocked.Increment(ref compilationCount));

        using var cts = new CancellationTokenSource();
        await coordinator.StartAsync(cts.Token);

        // Wait for initial startup compilation
        await Task.Delay(300);
        var countAfterStartup = compilationCount;

        // Fire 10 rapid signals
        for (int i = 0; i < 10; i++)
        {
            coordinator.SignalInvalidation(
                new InvalidationSignal(InvalidationReason.RuleUpdated));
        }

        // Wait for debounce window + compilation
        await Task.Delay(500);

        cts.Cancel();
        await coordinator.StopAsync(CancellationToken.None);

        // Should have startup compilation + 1 debounced compilation (not 10)
        var postSignalCompilations = compilationCount - countAfterStartup;
        postSignalCompilations.Should().Be(1);
    }

    [Test]
    public async Task ExecuteAsync_CompilationFailure_DoesNotCrashLoop()
    {
        var callCount = 0;
        var config = new RecompilationConfig
        {
            DebounceWindowMs = 50,
            ErrorRecoveryDelaySeconds = 0
        };
        var coordinator = new RecompilationCoordinator(
            _orchestrator, _notifier,
            NullLogger<RecompilationCoordinator>.Instance, config);

        _orchestrator.CompileAsync(Arg.Any<InvalidationSignal>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var count = Interlocked.Increment(ref callCount);
                if (count == 1) // Startup succeeds
                    return Task.CompletedTask;
                if (count == 2) // First signal fails
                    throw new InvalidOperationException("compile error");
                return Task.CompletedTask; // Subsequent succeeds
            });

        using var cts = new CancellationTokenSource();
        await coordinator.StartAsync(cts.Token);
        await Task.Delay(200); // Wait for startup

        // Signal twice - first will fail, second should still be processed
        coordinator.SignalInvalidation(new InvalidationSignal(InvalidationReason.RuleUpdated));
        await Task.Delay(300);
        coordinator.SignalInvalidation(new InvalidationSignal(InvalidationReason.RuleUpdated));
        await Task.Delay(300);

        cts.Cancel();
        await coordinator.StopAsync(CancellationToken.None);

        // Should have attempted at least 3 compilations (startup + failed + retry)
        callCount.Should().BeGreaterThanOrEqualTo(3);
    }
}
