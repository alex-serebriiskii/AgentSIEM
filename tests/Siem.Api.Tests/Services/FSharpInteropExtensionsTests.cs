using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Siem.Api.Services;

namespace Siem.Api.Tests.Services;

public class FSharpInteropExtensionsTests
{
    [Test]
    public void ToFSharpList_EmptyEnumerable_ReturnsEmptyList()
    {
        var result = Array.Empty<string>().ToFSharpList();

        ListModule.Length(result).Should().Be(0);
    }

    [Test]
    public void ToFSharpList_WithElements_ReturnsCorrectList()
    {
        var result = new[] { "a", "b", "c" }.ToFSharpList();

        ListModule.Length(result).Should().Be(3);
        ListModule.Head(result).Should().Be("a");
    }

    [Test]
    public void ToFSharpList_FromLinqEnumerable_Works()
    {
        var result = Enumerable.Range(1, 5).ToFSharpList();

        ListModule.Length(result).Should().Be(5);
    }

    [Test]
    public void ToFSharpOption_NullReference_ReturnsNone()
    {
        string? value = null;

        var result = value.ToFSharpOption();

        FSharpOption<string>.get_IsNone(result).Should().BeTrue();
    }

    [Test]
    public void ToFSharpOption_NonNullReference_ReturnsSome()
    {
        string? value = "hello";

        var result = value.ToFSharpOption();

        FSharpOption<string>.get_IsSome(result).Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Test]
    public void ToFSharpOptionValue_NullNullable_ReturnsNone()
    {
        int? value = null;

        var result = value.ToFSharpOptionValue();

        FSharpOption<int>.get_IsNone(result).Should().BeTrue();
    }

    [Test]
    public void ToFSharpOptionValue_HasValueNullable_ReturnsSome()
    {
        int? value = 42;

        var result = value.ToFSharpOptionValue();

        FSharpOption<int>.get_IsSome(result).Should().BeTrue();
        result.Value.Should().Be(42);
    }
}
