using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FSharp.Collections;
using NSubstitute;
using Siem.Api.Services;
using Siem.Rules.Core;

namespace Siem.Api.Tests.Services;

public class CompiledRulesCacheTests
{
    private readonly Evaluator.IStateProvider _stateProvider;
    private readonly CompiledRulesCache _cache;

    public CompiledRulesCacheTests()
    {
        _stateProvider = Substitute.For<Evaluator.IStateProvider>();
        _cache = new CompiledRulesCache(_stateProvider);
    }

    [Test]
    public void Engine_InitialState_HasZeroRules()
    {
        _cache.Engine.Should().NotBeNull();
    }

    [Test]
    public void LastCompilation_InitialState_IsEmpty()
    {
        _cache.LastCompilation.RuleCount.Should().Be(0);
        _cache.LastCompilation.CompiledAt.Should().Be(DateTime.MinValue);
        _cache.LastCompilation.ListCacheInfo.Should().BeEmpty();
    }

    [Test]
    public void SwapEngine_UpdatesEngineInstance()
    {
        var initialEngine = _cache.Engine;

        var emptyRules = ListModule.Empty<Compiler.CompiledRule>();
        var listCache = CreateListCacheService();

        _cache.SwapEngine(emptyRules, listCache);

        _cache.Engine.Should().NotBeSameAs(initialEngine);
    }

    [Test]
    public void SwapEngine_UpdatesCompilationMetadata()
    {
        var before = DateTime.UtcNow;

        var emptyRules = ListModule.Empty<Compiler.CompiledRule>();
        var listCache = CreateListCacheService();

        _cache.SwapEngine(emptyRules, listCache);

        _cache.LastCompilation.CompiledAt.Should().BeOnOrAfter(before);
        _cache.LastCompilation.RuleCount.Should().Be(0);
    }

    [Test]
    public void SwapEngine_CalledTwice_ReflectsLatestSwap()
    {
        var emptyRules = ListModule.Empty<Compiler.CompiledRule>();
        var listCache = CreateListCacheService();

        _cache.SwapEngine(emptyRules, listCache);
        var firstEngine = _cache.Engine;
        var firstCompilation = _cache.LastCompilation.CompiledAt;

        _cache.SwapEngine(emptyRules, listCache);

        _cache.Engine.Should().NotBeSameAs(firstEngine);
        _cache.LastCompilation.CompiledAt.Should().BeOnOrAfter(firstCompilation);
    }

    private static ListCacheService CreateListCacheService()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var logger = Substitute.For<ILogger<ListCacheService>>();
        return new ListCacheService(scopeFactory, logger);
    }
}
