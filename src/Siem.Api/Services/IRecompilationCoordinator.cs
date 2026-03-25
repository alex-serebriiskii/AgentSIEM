namespace Siem.Api.Services;

public interface IRecompilationCoordinator
{
    void SignalInvalidation(InvalidationSignal signal);
    Task SignalAndWaitAsync(InvalidationSignal signal, CancellationToken ct = default);
}
