using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Siem.Api.Controllers;
using Siem.Api.Services;

namespace Siem.Api.Tests.Controllers;

public class EngineControllerTests
{
    private readonly ICompiledRulesCache _rulesCache;
    private readonly IRecompilationCoordinator _coordinator;
    private readonly EngineController _controller;

    public EngineControllerTests()
    {
        _rulesCache = Substitute.For<ICompiledRulesCache>();
        _rulesCache.LastCompilation.Returns(CompilationMetadata.Empty);
        _coordinator = Substitute.For<IRecompilationCoordinator>();
        _controller = new EngineController(_rulesCache, _coordinator);
    }

    [Test]
    public void GetEngineStatus_ReturnsCompilationMetadata()
    {
        var result = _controller.GetEngineStatus();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Test]
    public async Task ForceRecompile_CallsSignalAndWaitAsync()
    {
        await _controller.ForceRecompile(CancellationToken.None);

        await _coordinator.Received(1).SignalAndWaitAsync(
            Arg.Is<InvalidationSignal>(s => s.Reason == InvalidationReason.ManualReload),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ForceRecompile_ReturnsOkResult()
    {
        var result = await _controller.ForceRecompile(CancellationToken.None);
        result.Should().BeOfType<OkObjectResult>();
    }
}
