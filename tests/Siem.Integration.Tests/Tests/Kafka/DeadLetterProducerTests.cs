using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Siem.Api.Kafka;
using Siem.Integration.Tests.Fixtures;

namespace Siem.Integration.Tests.Tests.Kafka;

[NotInParallel("database")]
public class DeadLetterProducerTests
{
    private const string SourceTopic = "dead-letter-test-source";
    private const string DeadLetterTopic = $"{SourceTopic}.dead-letter";

    [Test]
    public async Task ProduceAsync_RoutesFailedEventToDeadLetterTopic()
    {
        var config = new KafkaConsumerConfig
        {
            BootstrapServers = IntegrationTestFixture.KafkaBootstrapServers,
            Topic = SourceTopic
        };

        using var dlProducer = new DeadLetterProducer(
            config, NullLogger<DeadLetterProducer>.Instance);

        var originalPayload = Encoding.UTF8.GetBytes("""{"broken": true}""");
        var original = new ConsumeResult<string, byte[]>
        {
            Topic = SourceTopic,
            Partition = new Partition(0),
            Offset = new Offset(42),
            Message = new Message<string, byte[]>
            {
                Key = "agent-001",
                Value = originalPayload,
                Timestamp = new Timestamp(DateTime.UtcNow)
            }
        };

        var error = new InvalidOperationException("Test processing failure");
        await dlProducer.ProduceAsync(original, error, CancellationToken.None);

        // Consume from the dead-letter topic and verify
        using var consumer = new ConsumerBuilder<string, byte[]>(
            new ConsumerConfig
            {
                BootstrapServers = IntegrationTestFixture.KafkaBootstrapServers,
                GroupId = $"dl-test-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            }).Build();

        consumer.Subscribe(DeadLetterTopic);
        var dlMessage = consumer.Consume(TimeSpan.FromSeconds(10));
        consumer.Close();

        dlMessage.Should().NotBeNull();
        dlMessage!.Message.Key.Should().Be("agent-001");
        dlMessage.Message.Value.Should().BeEquivalentTo(originalPayload);
    }

    [Test]
    public async Task ProduceAsync_PreservesErrorMetadataHeaders()
    {
        var config = new KafkaConsumerConfig
        {
            BootstrapServers = IntegrationTestFixture.KafkaBootstrapServers,
            Topic = $"dl-header-test-{Guid.NewGuid():N}"
        };
        var dlTopic = $"{config.Topic}.dead-letter";

        using var dlProducer = new DeadLetterProducer(
            config, NullLogger<DeadLetterProducer>.Instance);

        var original = new ConsumeResult<string, byte[]>
        {
            Topic = config.Topic,
            Partition = new Partition(2),
            Offset = new Offset(99),
            Message = new Message<string, byte[]>
            {
                Key = "agent-002",
                Value = Encoding.UTF8.GetBytes("payload"),
                Timestamp = new Timestamp(DateTime.UtcNow)
            }
        };

        var error = new EventDeserializationException(
            "Bad JSON", Encoding.UTF8.GetBytes("payload"),
            new FormatException("inner"));

        await dlProducer.ProduceAsync(original, error, CancellationToken.None);

        using var consumer = new ConsumerBuilder<string, byte[]>(
            new ConsumerConfig
            {
                BootstrapServers = IntegrationTestFixture.KafkaBootstrapServers,
                GroupId = $"dl-header-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            }).Build();

        consumer.Subscribe(dlTopic);
        var dlMessage = consumer.Consume(TimeSpan.FromSeconds(10));
        consumer.Close();

        dlMessage.Should().NotBeNull();

        var headers = dlMessage!.Message.Headers;
        GetHeaderString(headers, "x-original-topic").Should().Be(config.Topic);
        GetHeaderString(headers, "x-error-type").Should().Be("EventDeserializationException");
        GetHeaderString(headers, "x-error-message").Should().Contain("Bad JSON");

        var partition = BitConverter.ToInt32(headers.GetLastBytes("x-original-partition"));
        partition.Should().Be(2);

        var offset = BitConverter.ToInt64(headers.GetLastBytes("x-original-offset"));
        offset.Should().Be(99);
    }

    [Test]
    public async Task ProduceAsync_PreservesOriginalHeaders()
    {
        var config = new KafkaConsumerConfig
        {
            BootstrapServers = IntegrationTestFixture.KafkaBootstrapServers,
            Topic = $"dl-orig-header-{Guid.NewGuid():N}"
        };
        var dlTopic = $"{config.Topic}.dead-letter";

        using var dlProducer = new DeadLetterProducer(
            config, NullLogger<DeadLetterProducer>.Instance);

        var originalHeaders = new Headers
        {
            { "x-source-sdk", Encoding.UTF8.GetBytes("langchain") }
        };

        var original = new ConsumeResult<string, byte[]>
        {
            Topic = config.Topic,
            Partition = new Partition(0),
            Offset = new Offset(0),
            Message = new Message<string, byte[]>
            {
                Key = "key",
                Value = Encoding.UTF8.GetBytes("data"),
                Headers = originalHeaders,
                Timestamp = new Timestamp(DateTime.UtcNow)
            }
        };

        await dlProducer.ProduceAsync(original, new Exception("fail"), CancellationToken.None);

        using var consumer = new ConsumerBuilder<string, byte[]>(
            new ConsumerConfig
            {
                BootstrapServers = IntegrationTestFixture.KafkaBootstrapServers,
                GroupId = $"dl-orig-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            }).Build();

        consumer.Subscribe(dlTopic);
        var dlMessage = consumer.Consume(TimeSpan.FromSeconds(10));
        consumer.Close();

        dlMessage.Should().NotBeNull();

        // Original headers are prefixed with x-orig-
        GetHeaderString(dlMessage!.Message.Headers, "x-orig-x-source-sdk")
            .Should().Be("langchain");
    }

    private static string GetHeaderString(Headers headers, string key) =>
        Encoding.UTF8.GetString(headers.GetLastBytes(key));
}
