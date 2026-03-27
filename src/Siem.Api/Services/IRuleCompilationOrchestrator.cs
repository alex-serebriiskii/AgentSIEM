namespace Siem.Api.Services;

public interface IRuleCompilationOrchestrator
{
    Task CompileAsync(InvalidationSignal trigger, CancellationToken ct);
}
