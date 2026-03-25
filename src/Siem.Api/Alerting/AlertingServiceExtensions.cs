using Siem.Api.Notifications;

namespace Siem.Api.Alerting;

public static class AlertingServiceExtensions
{
    public static IServiceCollection AddAlertPipeline(
        this IServiceCollection services,
        IConfiguration config)
    {
        // Configuration
        services.Configure<AlertPipelineConfig>(config.GetSection("AlertPipeline"));
        services.AddSingleton(sp =>
            config.GetSection("AlertPipeline").Get<AlertPipelineConfig>()
            ?? new AlertPipelineConfig());

        // Noise reduction (singleton -- uses Redis, no DbContext)
        services.AddSingleton<AlertDeduplicator>();
        services.AddSingleton<AlertThrottler>();

        // Scoped services (use DbContext per scope)
        services.AddScoped<SuppressionChecker>();
        services.AddScoped<AlertEnricher>();
        services.AddScoped<AlertPersistence>();

        // Notification router (singleton)
        services.AddSingleton<NotificationRouter>();

        // Pipeline (singleton -- creates its own scopes via IServiceScopeFactory)
        services.AddSingleton<IAlertPipeline, AlertPipeline>();

        // Notification retry infrastructure
        services.AddHostedService<NotificationRetryWorker>();

        return services;
    }
}
