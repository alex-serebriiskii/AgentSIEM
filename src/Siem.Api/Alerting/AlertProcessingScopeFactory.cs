using Microsoft.Extensions.DependencyInjection;

namespace Siem.Api.Alerting;

public interface IAlertProcessingScopeFactory
{
    IAlertProcessingScope CreateScope();
}

public interface IAlertProcessingScope : IDisposable
{
    SuppressionChecker Suppression { get; }
    AlertEnricher Enricher { get; }
    AlertPersistence Persistence { get; }
}

public class AlertProcessingScopeFactory(IServiceScopeFactory scopeFactory) : IAlertProcessingScopeFactory
{
    public IAlertProcessingScope CreateScope() => new AlertProcessingScope(scopeFactory.CreateScope());

    private class AlertProcessingScope(IServiceScope scope) : IAlertProcessingScope
    {
        public SuppressionChecker Suppression => scope.ServiceProvider.GetRequiredService<SuppressionChecker>();
        public AlertEnricher Enricher => scope.ServiceProvider.GetRequiredService<AlertEnricher>();
        public AlertPersistence Persistence => scope.ServiceProvider.GetRequiredService<AlertPersistence>();
        public void Dispose() => scope.Dispose();
    }
}
