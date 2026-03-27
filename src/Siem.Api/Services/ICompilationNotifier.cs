namespace Siem.Api.Services;

public interface ICompilationNotifier
{
    Task WaitForNextCompilationAsync(CancellationToken ct);
    void NotifyCompilationComplete();
}
