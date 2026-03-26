using System.Diagnostics;
using System.Diagnostics.Metrics;
using Confluent.Kafka;

namespace Siem.Api.Kafka;

/// <summary>
/// BackgroundService that manages the Kafka consumer lifecycle: polling,
/// offset tracking, batch commits, and rebalance handling.
/// One instance per consumer group member. Scale by running multiple pod replicas.
/// </summary>
public class KafkaConsumerWorker : BackgroundService
{
    private readonly KafkaConsumerConfig _config;
    private readonly EventProcessingPipeline _pipeline;
    private readonly DeadLetterProducer _deadLetter;
    private readonly ConsumerHealthCheck _health;
    private readonly ILogger<KafkaConsumerWorker> _logger;

    // Metrics
    private static readonly Meter Meter = new("Siem.Kafka");
    private static readonly Counter<long> EventsConsumed =
        Meter.CreateCounter<long>("siem.events.consumed");
    private static readonly Counter<long> EventsFailed =
        Meter.CreateCounter<long>("siem.events.failed");
    private static readonly Histogram<double> ProcessingDuration =
        Meter.CreateHistogram<double>("siem.events.processing_ms");
    private static readonly Counter<long> DeserializationErrors =
        Meter.CreateCounter<long>("siem.events.deserialization_errors");

    public KafkaConsumerWorker(
        KafkaConsumerConfig config,
        EventProcessingPipeline pipeline,
        DeadLetterProducer deadLetter,
        ConsumerHealthCheck health,
        ILogger<KafkaConsumerWorker> logger)
    {
        _config = config;
        _pipeline = pipeline;
        _deadLetter = deadLetter;
        _health = health;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run on a thread pool thread to avoid blocking startup
        await Task.Yield();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _config.BootstrapServers,
            GroupId = _config.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,

            // Manual offset commits for at-least-once delivery.
            // We commit AFTER successful processing, not on consume.
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,

            // Tuning for throughput
            FetchMaxBytes = _config.FetchMaxBytes,
            FetchWaitMaxMs = _config.FetchWaitMaxMs,

            // Session/heartbeat timeouts
            SessionTimeoutMs = _config.SessionTimeoutMs,
            HeartbeatIntervalMs = _config.HeartbeatIntervalMs,

            // Max poll interval before broker kicks the consumer
            MaxPollIntervalMs = _config.MaxPollIntervalMs,

            // Cooperative sticky minimizes rebalance disruption — partitions
            // only move when necessary
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig)
            .SetPartitionsAssignedHandler((c, partitions) =>
            {
                _logger.LogInformation(
                    "Partitions assigned: {Partitions}",
                    string.Join(", ", partitions.Select(p => p.Partition.Value)));
                _health.RecordPartitionAssignment(partitions);
            })
            .SetPartitionsRevokedHandler((c, partitions) =>
            {
                _logger.LogWarning(
                    "Partitions revoked: {Partitions}",
                    string.Join(", ", partitions.Select(p => p.Partition.Value)));

                // Flush the batch writer before losing partitions —
                // we need to commit offsets for everything we've buffered
                Task.Run(() => _pipeline.FlushBatchWriter()).GetAwaiter().GetResult();
            })
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka consumer error: {Error}", error.Reason);
                _health.RecordError(error);
            })
            .Build();

        consumer.Subscribe(_config.Topic);
        _logger.LogInformation(
            "Kafka consumer started: topic={Topic}, group={Group}",
            _config.Topic, _config.GroupId);

        // Track offsets per partition for batch commits
        var pendingOffsets = new Dictionary<TopicPartition, Offset>();
        var messagesSinceCommit = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = consumer.Consume(
                    TimeSpan.FromMilliseconds(_config.ConsumeTimeoutMs));

                if (consumeResult == null)
                {
                    // Poll timeout — no messages. Good time to flush
                    // any buffered batch writes and commit offsets.
                    if (pendingOffsets.Count > 0)
                    {
                        await _pipeline.FlushBatchWriter();
                        CommitOffsets(consumer, pendingOffsets);
                        messagesSinceCommit = 0;
                    }
                    continue;
                }

                _health.RecordConsume();

                var sw = Stopwatch.StartNew();
                var result = await ProcessMessageAsync(consumeResult, stoppingToken);
                sw.Stop();

                ProcessingDuration.Record(sw.Elapsed.TotalMilliseconds);
                EventsConsumed.Add(1);

                // Track the offset for this partition (next offset to read)
                pendingOffsets[consumeResult.TopicPartition] =
                    consumeResult.Offset + 1;
                messagesSinceCommit++;

                // Commit offsets periodically to reduce broker overhead
                if (messagesSinceCommit >= _config.CommitBatchSize)
                {
                    await _pipeline.FlushBatchWriter();
                    CommitOffsets(consumer, pendingOffsets);
                    messagesSinceCommit = 0;
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                _logger.LogError("Topic {Topic} not found — waiting for creation",
                    _config.Topic);
                await Task.Delay(TimeSpan.FromSeconds(_config.TopicNotFoundRetrySeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in consumer loop");
                _health.RecordError(ex);

                // Brief pause to avoid tight failure loops
                await Task.Delay(TimeSpan.FromSeconds(_config.ErrorBackoffSeconds), stoppingToken);
            }
        }

        // Graceful shutdown: flush remaining writes and commit final offsets
        _logger.LogInformation("Consumer shutting down — flushing pending work...");
        try
        {
            await _pipeline.FlushBatchWriter();
            if (pendingOffsets.Count > 0)
                CommitOffsets(consumer, pendingOffsets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during shutdown flush");
        }

        consumer.Close();
        _logger.LogInformation("Kafka consumer stopped");
    }

    private async Task<ProcessingResult> ProcessMessageAsync(
        ConsumeResult<string, byte[]> consumeResult,
        CancellationToken ct)
    {
        try
        {
            return await _pipeline.ProcessAsync(consumeResult, ct);
        }
        catch (EventDeserializationException ex)
        {
            EventsFailed.Add(1);
            DeserializationErrors.Add(1);
            _logger.LogError(ex,
                "Deserialization failed for partition {Partition} offset {Offset}",
                consumeResult.Partition.Value, consumeResult.Offset.Value);

            await _deadLetter.ProduceAsync(consumeResult, ex, ct);
            return ProcessingResult.DeadLettered;
        }
        catch (Exception ex)
        {
            EventsFailed.Add(1);
            _logger.LogError(ex,
                "Failed to process event from partition {Partition} offset {Offset}",
                consumeResult.Partition.Value, consumeResult.Offset.Value);

            // Route to dead letter topic — don't block the partition
            await _deadLetter.ProduceAsync(consumeResult, ex, ct);
            return ProcessingResult.DeadLettered;
        }
    }

    private void CommitOffsets(
        IConsumer<string, byte[]> consumer,
        Dictionary<TopicPartition, Offset> offsets)
    {
        if (offsets.Count == 0) return;

        try
        {
            var toCommit = offsets
                .Select(kv => new TopicPartitionOffset(kv.Key, kv.Value))
                .ToList();

            consumer.Commit(toCommit);

            _logger.LogDebug(
                "Committed offsets for {PartitionCount} partitions",
                toCommit.Count);

            offsets.Clear();
        }
        catch (KafkaException ex)
        {
            _logger.LogError(ex, "Offset commit failed — will retry on next cycle");
            // Don't clear offsets — they'll be committed on next attempt.
            // Worst case: some events get reprocessed (at-least-once).
        }
    }
}
