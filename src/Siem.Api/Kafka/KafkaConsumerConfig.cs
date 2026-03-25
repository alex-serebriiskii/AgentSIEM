namespace Siem.Api.Kafka;

/// <summary>
/// Configuration POCO for the Kafka consumer, bound from the "Kafka" config section.
/// </summary>
public class KafkaConsumerConfig
{
    /// <summary>Kafka broker addresses (comma-separated).</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Topic to consume agent events from.</summary>
    public string Topic { get; set; } = "agent-events";

    /// <summary>Consumer group identifier.</summary>
    public string GroupId { get; set; } = "siem-processors";

    /// <summary>
    /// Number of messages processed before committing offsets.
    /// Lower values reduce duplicate processing on restart; higher values
    /// reduce broker overhead.
    /// </summary>
    public int CommitBatchSize { get; set; } = 100;

    /// <summary>Maximum bytes fetched per poll (default 1 MB).</summary>
    public int FetchMaxBytes { get; set; } = 1_048_576;

    /// <summary>Max wait time (ms) for a fetch to accumulate messages.</summary>
    public int FetchWaitMaxMs { get; set; } = 100;

    /// <summary>Session timeout for broker heartbeats (ms).</summary>
    public int SessionTimeoutMs { get; set; } = 45_000;

    /// <summary>Heartbeat interval (ms).</summary>
    public int HeartbeatIntervalMs { get; set; } = 10_000;

    /// <summary>Max interval (ms) between polls before the broker kicks the consumer.</summary>
    public int MaxPollIntervalMs { get; set; } = 300_000;
}
