namespace Siem.Api.Kafka;

/// <summary>
/// Thrown when a Kafka message cannot be deserialized into a <see cref="RawAgentEvent"/>.
/// Preserves the original message bytes so the event can be replayed or
/// inspected in a dead-letter topic.
/// </summary>
public class EventDeserializationException : Exception
{
    /// <summary>The raw Kafka message bytes that failed deserialization.</summary>
    public byte[] RawBytes { get; }

    public EventDeserializationException(string message, byte[] rawBytes, Exception innerException)
        : base(message, innerException)
    {
        RawBytes = rawBytes;
    }
}
