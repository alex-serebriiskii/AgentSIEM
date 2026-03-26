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

        // Notification channels
        // SignalR is always registered (real-time UI channel)
        services.AddSingleton<INotificationChannel, SignalRNotificationChannel>();

        // Conditional channels — only registered when config section exists
        var webhooksSection = config.GetSection("Webhooks");
        if (webhooksSection.Exists())
        {
            var webhookConfig = webhooksSection.Get<WebhookConfig>() ?? new WebhookConfig();
            services.AddSingleton(webhookConfig);
            services.AddSingleton<INotificationChannel, WebhookNotificationChannel>();
        }

        var slackSection = config.GetSection("Slack");
        if (slackSection.Exists())
        {
            var slackConfig = slackSection.Get<SlackConfig>() ?? new SlackConfig();
            services.AddSingleton(slackConfig);
            services.AddSingleton<INotificationChannel, SlackNotificationChannel>();
        }

        var pagerDutySection = config.GetSection("PagerDuty");
        if (pagerDutySection.Exists())
        {
            var pagerDutyConfig = pagerDutySection.Get<PagerDutyConfig>() ?? new PagerDutyConfig();
            services.AddSingleton(pagerDutyConfig);
            services.AddSingleton<INotificationChannel, PagerDutyNotificationChannel>();
        }

        // Notification router (singleton)
        services.AddSingleton<NotificationRouter>();

        // Pipeline (singleton -- creates its own scopes via IServiceScopeFactory)
        services.AddSingleton<IAlertPipeline, AlertPipeline>();

        // Notification retry infrastructure
        // Register as singleton first so NotificationRouter can resolve it,
        // then register the same instance as a hosted service.
        services.AddSingleton<NotificationRetryWorker>();
        services.AddHostedService<NotificationRetryWorker>(sp =>
            sp.GetRequiredService<NotificationRetryWorker>());

        return services;
    }
}
