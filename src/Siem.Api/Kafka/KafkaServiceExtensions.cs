using Siem.Api.Normalization;
using Siem.Api.Storage;

namespace Siem.Api.Kafka;

/// <summary>
/// DI registration for the Kafka consumer pipeline and all its dependencies.
/// </summary>
public static class KafkaServiceExtensions
{
    /// <summary>
    /// Registers the Kafka consumer pipeline: config, normalizer, batch writer,
    /// dead letter producer, health check, processing pipeline, and the
    /// background consumer worker.
    /// </summary>
    public static IServiceCollection AddKafkaPipeline(
        this IServiceCollection services,
        IConfiguration config)
    {
        var kafkaConfig = config.GetSection("Kafka").Get<KafkaConsumerConfig>()
            ?? new KafkaConsumerConfig();

        services.AddSingleton(kafkaConfig);
        services.AddSingleton<IEventNormalizer, AgentEventNormalizer>();
        services.AddSingleton<BatchEventWriter>();
        services.AddSingleton<DeadLetterProducer>();
        services.AddSingleton<ConsumerHealthCheck>();
        services.AddSingleton<EventProcessingPipeline>();
        services.AddHostedService<KafkaConsumerWorker>();

        // Register the health check for Kubernetes probes
        services.AddHealthChecks()
            .AddCheck<ConsumerHealthCheck>("kafka-consumer");

        return services;
    }
}
