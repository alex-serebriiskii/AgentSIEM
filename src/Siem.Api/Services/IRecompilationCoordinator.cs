namespace Siem.Api.Services;

public interface IRecompilationCoordinator
{
    bool SignalInvalidation(InvalidationSignal signal);
    Task SignalAndWaitAsync(InvalidationSignal signal, CancellationToken ct = default);
}
