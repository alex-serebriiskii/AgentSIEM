using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.FSharp.Core;
using Npgsql;
using NpgsqlTypes;
using Siem.Rules.Core;

namespace Siem.Api.Storage;

/// <summary>
/// Buffers events in memory and writes to TimescaleDB in batches using the
/// PostgreSQL COPY binary protocol for maximum throughput (5-10x faster than
/// individual INSERTs). Flushes on buffer size, time interval, or explicit
/// request from the offset commit path.
/// </summary>
public class BatchEventWriter : IAsyncDisposable
{
    private readonly Channel<AgentEvent> _buffer;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<BatchEventWriter> _logger;
    private readonly Timer _flushTimer;
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    private readonly int _maxBatchSize;
    private readonly TimeSpan _maxFlushInterval;
    private readonly TimeSpan _flushLockTimeout;

    // Metrics
    private static readonly Meter Meter = new("Siem.Storage");
    private static readonly Counter<long> EventsWritten =
        Meter.CreateCounter<long>("siem.storage.events_written");
    private static readonly Histogram<double> BatchWriteDuration =
        Meter.CreateHistogram<double>("siem.storage.batch_write_ms");
    private static readonly Histogram<int> BatchSizeMetric =
        Meter.CreateHistogram<int>("siem.storage.batch_size");

    public BatchEventWriter(
        NpgsqlDataSource dataSource,
        ILogger<BatchEventWriter> logger,
        BatchEventWriterConfig config)
    {
        _dataSource = dataSource;
        _logger = logger;
        _maxBatchSize = config.MaxBatchSize;
        _maxFlushInterval = TimeSpan.FromSeconds(config.MaxFlushIntervalSeconds);
        _flushLockTimeout = TimeSpan.FromSeconds(config.FlushLockTimeoutSeconds);

        // BoundedChannel provides backpressure to the consumer when the DB
        // can't keep up. SingleReader = true enables optimizations since
        // only the flush loop reads from the channel.
        _buffer = Channel.CreateBounded<AgentEvent>(
            new BoundedChannelOptions(config.MaxBatchSize * config.ChannelSizeMultiplier)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true
            });

        // Timer-based flush for low-throughput periods
        _flushTimer = new Timer(
            _ => _ = FlushAsync(),
            null,
            _maxFlushInterval,
            _maxFlushInterval);
    }

    /// <summary>
    /// Enqueue an event for batch writing. Awaits backpressure if the buffer is full.
    /// </summary>
    public async ValueTask EnqueueAsync(AgentEvent evt)
    {
        await _buffer.Writer.WriteAsync(evt);
    }

    /// <summary>
    /// Flush all buffered events to TimescaleDB. Called by:
    /// the consumer on offset commit, the timer, and shutdown.
    /// </summary>
    public async Task FlushAsync()
    {
        if (!await _flushLock.WaitAsync(_flushLockTimeout))
        {
            _logger.LogWarning("Flush lock timeout — another flush is stuck");
            return;
        }

        try
        {
            var batch = new List<AgentEvent>();

            while (batch.Count < _maxBatchSize * 2 &&
                   _buffer.Reader.TryRead(out var evt))
            {
                batch.Add(evt);
            }

            if (batch.Count == 0) return;

            var sw = Stopwatch.StartNew();
            await WriteBatchAsync(batch);
            sw.Stop();

            BatchWriteDuration.Record(sw.Elapsed.TotalMilliseconds);
            BatchSizeMetric.Record(batch.Count);
            EventsWritten.Add(batch.Count);

            _logger.LogDebug(
                "Flushed {Count} events in {ElapsedMs}ms",
                batch.Count, sw.ElapsedMilliseconds);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>
    /// Write a batch of events using PostgreSQL COPY binary protocol.
    /// </summary>
    private async Task WriteBatchAsync(List<AgentEvent> batch)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var writer = await conn.BeginBinaryImportAsync(
            "COPY agent_events (" +
            "event_id, timestamp, session_id, trace_id, agent_id, agent_name, " +
            "event_type, model_id, input_tokens, output_tokens, latency_ms, " +
            "tool_name, tool_input, tool_output, content_hash, properties" +
            ") FROM STDIN (FORMAT BINARY)");

        foreach (var evt in batch)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(evt.EventId, NpgsqlDbType.Uuid);
            await writer.WriteAsync(evt.Timestamp, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(evt.SessionId, NpgsqlDbType.Text);
            await writer.WriteAsync(evt.TraceId, NpgsqlDbType.Text);
            await writer.WriteAsync(evt.AgentId, NpgsqlDbType.Text);
            await writer.WriteAsync(evt.AgentName, NpgsqlDbType.Text);
            await writer.WriteAsync(evt.EventType, NpgsqlDbType.Text);

            // F# Option fields → SQL nulls
            await WriteOptional(writer, evt.ModelId, NpgsqlDbType.Text);
            await WriteOptional(writer, evt.InputTokens, NpgsqlDbType.Integer);
            await WriteOptional(writer, evt.OutputTokens, NpgsqlDbType.Integer);
            await WriteOptional(writer, evt.LatencyMs, NpgsqlDbType.Double);
            await WriteOptional(writer, evt.ToolName, NpgsqlDbType.Text);
            await WriteOptional(writer, evt.ToolInput, NpgsqlDbType.Text);
            await WriteOptional(writer, evt.ToolOutput, NpgsqlDbType.Text);
            await WriteOptional(writer, evt.ContentHash, NpgsqlDbType.Text);

            // Properties map → JSONB
            var propsDict = new Dictionary<string, JsonElement>();
            foreach (var kvp in evt.Properties)
            {
                propsDict[kvp.Key] = kvp.Value;
            }
            var propsJson = JsonSerializer.Serialize(propsDict);
            await writer.WriteAsync(propsJson, NpgsqlDbType.Jsonb);
        }

        await writer.CompleteAsync();
    }

    private static async Task WriteOptional<T>(
        NpgsqlBinaryImporter writer,
        FSharpOption<T> opt,
        NpgsqlDbType dbType)
    {
        if (FSharpOption<T>.get_IsSome(opt))
            await writer.WriteAsync(opt.Value, dbType);
        else
            await writer.WriteNullAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _flushTimer.DisposeAsync();
        await FlushAsync();
        _flushLock.Dispose();
    }
}
