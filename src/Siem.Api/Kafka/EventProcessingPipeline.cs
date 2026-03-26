using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.FSharp.Control;
using Siem.Api.Alerting;
using Siem.Api.Normalization;
using Siem.Api.Services;
using Siem.Api.Storage;
using Siem.Rules.Core;

namespace Siem.Api.Kafka;

/// <summary>
/// Processing result for a single Kafka message.
/// </summary>
public enum ProcessingResult
{
    /// <summary>Event processed and at least one rule triggered.</summary>
    Processed,

    /// <summary>Event processed but no rules matched.</summary>
    NoRulesTriggered,

    /// <summary>Event failed processing and was sent to the dead-letter topic.</summary>
    DeadLettered
}

/// <summary>
/// Stateless 5-stage event processing pipeline:
/// deserialize -> normalize -> buffer -> evaluate -> dispatch.
/// </summary>
public class EventProcessingPipeline
{
    private readonly ICompiledRulesCache _rulesCache;
    private readonly IEventNormalizer _normalizer;
    private readonly BatchEventWriter _batchWriter;
    private readonly IAlertPipeline _alertPipeline;
    private readonly ISessionTracker _sessionTracker;
    private readonly ILogger<EventProcessingPipeline> _logger;

    // Metrics
    private static readonly Meter Meter = new("Siem.Pipeline");
    private static readonly Counter<long> RulesTriggered =
        Meter.CreateCounter<long>("siem.rules.triggered");
    private static readonly Histogram<double> NormalizationDuration =
        Meter.CreateHistogram<double>("siem.normalize_ms");
    private static readonly Histogram<double> EvaluationDuration =
        Meter.CreateHistogram<double>("siem.evaluate_ms");

    public EventProcessingPipeline(
        ICompiledRulesCache rulesCache,
        IEventNormalizer normalizer,
        BatchEventWriter batchWriter,
        IAlertPipeline alertPipeline,
        ISessionTracker sessionTracker,
        ILogger<EventProcessingPipeline> logger)
    {
        _rulesCache = rulesCache;
        _normalizer = normalizer;
        _batchWriter = batchWriter;
        _alertPipeline = alertPipeline;
        _sessionTracker = sessionTracker;
        _logger = logger;
    }

    /// <summary>
    /// Process a single Kafka message through all pipeline stages.
    /// </summary>
    public async Task<ProcessingResult> ProcessAsync(
        ConsumeResult<string, byte[]> consumeResult,
        CancellationToken ct)
    {
        // Stage 1: Deserialize raw JSON to permissive model
        var rawEvent = DeserializeMessage(consumeResult);

        // Stage 2: Normalize to canonical F# AgentEvent
        var sw = Stopwatch.StartNew();
        var agentEvent = _normalizer.Normalize(rawEvent);
        NormalizationDuration.Record(sw.Elapsed.TotalMilliseconds);

        // Stage 3: Buffer for batch write to TimescaleDB
        // Events are always persisted, regardless of whether rules trigger.
        await _batchWriter.EnqueueAsync(agentEvent);

        // Stage 3b: Track session (best-effort — does not block pipeline on failure)
        await _sessionTracker.TrackEventAsync(
            agentEvent.SessionId, agentEvent.AgentId, agentEvent.AgentName,
            agentEvent.Timestamp, ct);

        // Stage 4: Evaluate rules
        sw.Restart();
        var engine = _rulesCache.Engine;

        var results = await FSharpAsync.StartAsTask(
            Engine.evaluateEvent(engine, agentEvent),
            taskCreationOptions: null,
            cancellationToken: ct);

        EvaluationDuration.Record(sw.Elapsed.TotalMilliseconds);

        if (results.IsEmpty)
            return ProcessingResult.NoRulesTriggered;

        // Stage 5: Dispatch triggered rules to alert pipeline
        RulesTriggered.Add(results.Length);

        foreach (var result in results)
        {
            await _alertPipeline.ProcessAsync(result, agentEvent, ct);
        }

        return ProcessingResult.Processed;
    }

    /// <summary>
    /// Flush the batch writer (called before offset commits and on shutdown).
    /// </summary>
    public Task FlushBatchWriter() => _batchWriter.FlushAsync();

    private RawAgentEvent DeserializeMessage(ConsumeResult<string, byte[]> msg)
    {
        try
        {
            var rawEvent = JsonSerializer.Deserialize<RawAgentEvent>(
                msg.Message.Value,
                SerializerOptions);

            if (rawEvent == null)
                throw new InvalidOperationException("Deserialized to null");

            // Carry Kafka metadata for traceability
            rawEvent.KafkaPartition = msg.Partition.Value;
            rawEvent.KafkaOffset = msg.Offset.Value;
            rawEvent.KafkaTimestamp = msg.Message.Timestamp.UtcDateTime;

            return rawEvent;
        }
        catch (JsonException ex)
        {
            throw new EventDeserializationException(
                $"Failed to deserialize event at partition {msg.Partition.Value} " +
                $"offset {msg.Offset.Value}: {ex.Message}",
                msg.Message.Value,
                ex);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
