namespace Siem.Api.Storage;

/// <summary>
/// Configuration for the BatchEventWriter buffering and flush behavior.
/// </summary>
public class BatchEventWriterConfig
{
    /// <summary>Maximum events per write batch.</summary>
    public int MaxBatchSize { get; set; } = 500;

    /// <summary>Maximum time in seconds between automatic flushes.</summary>
    public int MaxFlushIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// Channel buffer size as a multiplier of MaxBatchSize.
    /// Total buffer capacity = MaxBatchSize * ChannelSizeMultiplier.
    /// </summary>
    public int ChannelSizeMultiplier { get; set; } = 4;

    /// <summary>Timeout in seconds when acquiring the flush lock.</summary>
    public int FlushLockTimeoutSeconds { get; set; } = 30;
}
