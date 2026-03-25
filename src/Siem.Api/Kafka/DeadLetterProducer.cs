using System.Diagnostics.Metrics;
using System.Text;
using Confluent.Kafka;

namespace Siem.Api.Kafka;

/// <summary>
/// Routes failed events to a dead-letter topic ({original-topic}.dead-letter)
/// with headers preserving the original Kafka metadata and error context
/// so events can be investigated and replayed.
/// </summary>
public class DeadLetterProducer : IDisposable
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly string _deadLetterTopic;
    private readonly ILogger<DeadLetterProducer> _logger;

    private static readonly Meter Meter = new("Siem.DeadLetter");
    private static readonly Counter<long> DeadLetterCount =
        Meter.CreateCounter<long>("siem.dead_letter.produced");

    public DeadLetterProducer(
        KafkaConsumerConfig config,
        ILogger<DeadLetterProducer> logger)
    {
        _deadLetterTopic = $"{config.Topic}.dead-letter";
        _logger = logger;

        _producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = config.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();
    }

    /// <summary>
    /// Produce the failed event to the dead-letter topic with error metadata headers.
    /// </summary>
    public async Task ProduceAsync(
        ConsumeResult<string, byte[]> original,
        Exception error,
        CancellationToken ct)
    {
        try
        {
            var headers = new Headers
            {
                { "x-original-topic", Encoding.UTF8.GetBytes(original.Topic) },
                { "x-original-partition", BitConverter.GetBytes(original.Partition.Value) },
                { "x-original-offset", BitConverter.GetBytes(original.Offset.Value) },
                { "x-error-type", Encoding.UTF8.GetBytes(error.GetType().Name) },
                { "x-error-message", Encoding.UTF8.GetBytes(
                    error.Message.Length > 500 ? error.Message[..500] : error.Message) },
                { "x-dead-lettered-at", Encoding.UTF8.GetBytes(
                    DateTime.UtcNow.ToString("O")) },
                { "x-retry-count", BitConverter.GetBytes(0) }
            };

            // Preserve original message headers
            if (original.Message.Headers != null)
            {
                foreach (var h in original.Message.Headers)
                {
                    headers.Add($"x-orig-{h.Key}", h.GetValueBytes());
                }
            }

            await _producer.ProduceAsync(_deadLetterTopic, new Message<string, byte[]>
            {
                Key = original.Message.Key,
                Value = original.Message.Value,
                Headers = headers,
                Timestamp = new Timestamp(DateTime.UtcNow)
            }, ct);

            DeadLetterCount.Add(1);

            _logger.LogWarning(
                "Dead-lettered event: partition={Partition} offset={Offset} error={Error}",
                original.Partition.Value, original.Offset.Value, error.GetType().Name);
        }
        catch (Exception ex)
        {
            // If we can't even dead-letter, log prominently. The offset will
            // still be committed (the original event was already consumed),
            // so this is a genuine data loss scenario that needs alerting.
            _logger.LogCritical(ex,
                "FAILED TO DEAD-LETTER event at partition={Partition} offset={Offset}. " +
                "Original error: {OriginalError}. EVENT MAY BE LOST.",
                original.Partition.Value, original.Offset.Value, error.Message);
        }
    }

    public void Dispose() => _producer?.Dispose();
}
