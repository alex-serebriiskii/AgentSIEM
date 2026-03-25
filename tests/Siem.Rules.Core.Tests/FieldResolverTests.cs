using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Core;
using Siem.Rules.Core;
using static Siem.Rules.Core.Tests.TestHelpers;

namespace Siem.Rules.Core.Tests;

public class FieldResolverTests
{
    [Test]
    public async Task Resolve_EventType_ReturnsSomeWithValue()
    {
        var evt = CreateEvent(eventType: "tool_invocation");

        var result = FieldResolver.resolve("eventType", evt);

        result.Should().NotBeNull();
        FSharpOption<object>.get_IsSome(result).Should().BeTrue();
        result.Value.ToString().Should().Be("tool_invocation");
    }

    [Test]
    public async Task Resolve_AgentId_ReturnsSomeWithValue()
    {
        var evt = CreateEvent(agentId: "agent-042");

        var result = FieldResolver.resolve("agentId", evt);

        FSharpOption<object>.get_IsSome(result).Should().BeTrue();
        result.Value.ToString().Should().Be("agent-042");
    }

    [Test]
    public async Task Resolve_AgentName_ReturnsSomeWithValue()
    {
        var evt = CreateEvent(agentName: "MyAgent");

        var result = FieldResolver.resolve("agentName", evt);

        FSharpOption<object>.get_IsSome(result).Should().BeTrue();
        result.Value.ToString().Should().Be("MyAgent");
    }

    [Test]
    public async Task Resolve_SessionId_ReturnsSomeWithValue()
    {
        var evt = CreateEvent(sessionId: "sess-123");

        var result = FieldResolver.resolve("sessionId", evt);

        FSharpOption<object>.get_IsSome(result).Should().BeTrue();
        result.Value.ToString().Should().Be("sess-123");
    }

    [Test]
    public async Task Resolve_ModelId_WhenPresent_ReturnsSome()
    {
        var evt = CreateEvent(modelId: "gpt-4");

        var result = FieldResolver.resolve("modelId", evt);

        FSharpOption<object>.get_IsSome(result).Should().BeTrue();
        result.Value.ToString().Should().Be("gpt-4");
    }

    [Test]
    public async Task Resolve_ModelId_WhenAbsent_ReturnsNone()
    {
        var evt = CreateEvent(modelId: null);

        var result = FieldResolver.resolve("modelId", evt);

        FSharpOption<object>.get_IsNone(result).Should().BeTrue();
    }

    [Test]
    public async Task Resolve_ToolName_WhenPresent_ReturnsSome()
    {
        var evt = CreateEvent(toolName: "web_search");

        var result = FieldResolver.resolve("toolName", evt);

        FSharpOption<object>.get_IsSome(result).Should().BeTrue();
        result.Value.ToString().Should().Be("web_search");
    }

    [Test]
    public async Task Resolve_PropertiesDotPath_ReturnsSome()
    {
        var props = new Dictionary<string, JsonElement>
        {
            ["customField"] = JsonString("customValue")
        };
        var evt = CreateEvent(properties: props);

        var result = FieldResolver.resolve("properties.customField", evt);

        FSharpOption<object>.get_IsSome(result).Should().BeTrue();
    }

    [Test]
    public async Task Resolve_MissingField_ReturnsNone()
    {
        var evt = CreateEvent();

        var result = FieldResolver.resolve("properties.nonExistent", evt);

        FSharpOption<object>.get_IsNone(result).Should().BeTrue();
    }

    [Test]
    public async Task Resolve_LatencyMs_WhenPresent_ReturnsSome()
    {
        var evt = CreateEvent(latencyMs: 42.5);

        var result = FieldResolver.resolve("latencyMs", evt);

        FSharpOption<object>.get_IsSome(result).Should().BeTrue();
    }
}
