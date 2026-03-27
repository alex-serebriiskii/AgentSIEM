using System.Text.Json;
using FluentAssertions;
using Siem.Api.Models.Responses;
using Siem.Api.Tests.Controllers.Helpers;

namespace Siem.Api.Tests.Models;

public class AlertResponseTests
{
    [Test]
    public async Task FromEntity_MalformedContext_ReturnsEmptyObject()
    {
        var entity = TestEntityBuilders.CreateAlert();
        entity.Context = "not valid json";

        var response = AlertResponse.FromEntity(entity);

        response.Context.ValueKind.Should().Be(JsonValueKind.Object);
        response.Context.EnumerateObject().Should().BeEmpty();
    }

    [Test]
    public async Task FromEntity_EmptyLabels_ReturnsEmptyObject()
    {
        var entity = TestEntityBuilders.CreateAlert();
        entity.Labels = "";

        var response = AlertResponse.FromEntity(entity);

        response.Labels.ValueKind.Should().Be(JsonValueKind.Object);
        response.Labels.EnumerateObject().Should().BeEmpty();
    }

    [Test]
    public async Task FromEntity_NullContext_ReturnsEmptyObject()
    {
        var entity = TestEntityBuilders.CreateAlert();
        entity.Context = null!;

        var response = AlertResponse.FromEntity(entity);

        response.Context.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Test]
    public async Task FromEntity_ValidJson_ParsesCorrectly()
    {
        var entity = TestEntityBuilders.CreateAlert();
        entity.Context = """{"key": "value"}""";
        entity.Labels = """{"env": "prod"}""";

        var response = AlertResponse.FromEntity(entity);

        response.Context.GetProperty("key").GetString().Should().Be("value");
        response.Labels.GetProperty("env").GetString().Should().Be("prod");
    }
}
