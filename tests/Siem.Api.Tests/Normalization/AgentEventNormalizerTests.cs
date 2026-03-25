using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.FSharp.Core;
using NSubstitute;
using Siem.Api.Kafka;
using Siem.Api.Normalization;

namespace Siem.Api.Tests.Normalization;

public class AgentEventNormalizerTests
{
    private AgentEventNormalizer _normalizer = null!;

    [Before(Test)]
    public void Setup()
    {
        var logger = Substitute.For<ILogger<AgentEventNormalizer>>();
        _normalizer = new AgentEventNormalizer(logger);
    }

    [Test]
    public async Task Normalize_OpenTelemetryEvent_NormalizesEventType()
    {
        var raw = new RawAgentEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            SessionId = "sess-001",
            TraceId = "trace-001",
            AgentId = "agent-001",
            AgentName = "TestAgent",
            EventType = "llm.call",
            ModelId = "gpt-4"
        };

        var result = _normalizer.Normalize(raw);

        result.EventType.Should().Be("llm_call");
        result.AgentId.Should().Be("agent-001");
        FSharpOption<string>.get_IsSome(result.ModelId).Should().BeTrue();
        result.ModelId.Value.Should().Be("gpt-4");
    }

    [Test]
    public async Task Normalize_LangChainEvent_NormalizesEventType()
    {
        var raw = new RawAgentEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            SessionId = "sess-001",
            TraceId = "trace-001",
            AgentId = "agent-001",
            AgentName = "TestAgent",
            EventType = "on_tool_start"
        };

        var result = _normalizer.Normalize(raw);

        result.EventType.Should().Be("tool_invocation");
    }

    [Test]
    public async Task Normalize_CustomEvent_PassesThroughEventType()
    {
        var raw = new RawAgentEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            SessionId = "sess-001",
            TraceId = "trace-001",
            AgentId = "agent-001",
            AgentName = "TestAgent",
            EventType = "my_custom_event"
        };

        var result = _normalizer.Normalize(raw);

        result.EventType.Should().Be("my_custom_event");
    }

    [Test]
    public async Task Normalize_NullOptionalFields_SetsNone()
    {
        var raw = new RawAgentEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            AgentId = "agent-001",
            AgentName = "TestAgent",
            EventType = "tool_invocation"
        };

        var result = _normalizer.Normalize(raw);

        FSharpOption<string>.get_IsNone(result.ModelId).Should().BeTrue();
        FSharpOption<string>.get_IsNone(result.ToolName).Should().BeTrue();
        FSharpOption<int>.get_IsNone(result.InputTokens).Should().BeTrue();
        FSharpOption<double>.get_IsNone(result.LatencyMs).Should().BeTrue();
    }

    [Test]
    public async Task Normalize_WithExtraProperties_MapsToProperties()
    {
        var extra = new Dictionary<string, JsonElement>
        {
            ["custom_key"] = JsonDocument.Parse("\"custom_value\"").RootElement
        };

        var raw = new RawAgentEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            AgentId = "agent-001",
            AgentName = "TestAgent",
            EventType = "tool_invocation",
            Extra = extra
        };

        var result = _normalizer.Normalize(raw);

        // The properties map should contain our custom key
        result.Properties.ContainsKey("custom_key").Should().BeTrue();
    }

    [Test]
    public async Task Normalize_MissingAgentId_DefaultsToUnknown()
    {
        var raw = new RawAgentEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            EventType = "tool_invocation"
        };

        var result = _normalizer.Normalize(raw);

        result.AgentId.Should().Be("unknown");
        result.AgentName.Should().Be("unknown");
    }

    [Test]
    public async Task Normalize_EmptyEventId_GeneratesNewGuid()
    {
        var raw = new RawAgentEvent
        {
            EventId = Guid.Empty,
            Timestamp = DateTime.UtcNow,
            AgentId = "agent-001",
            AgentName = "TestAgent",
            EventType = "tool_invocation"
        };

        var result = _normalizer.Normalize(raw);

        result.EventId.Should().NotBe(Guid.Empty);
    }

    [Test]
    public async Task Normalize_ToolInvokeOtel_NormalizesToToolInvocation()
    {
        var raw = new RawAgentEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            AgentId = "agent-001",
            AgentName = "TestAgent",
            EventType = "tool.invoke",
            ToolName = "web_search"
        };

        var result = _normalizer.Normalize(raw);

        result.EventType.Should().Be("tool_invocation");
        FSharpOption<string>.get_IsSome(result.ToolName).Should().BeTrue();
        result.ToolName.Value.Should().Be("web_search");
    }
}
