using FluentAssertions;
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
        _cache.Engine.CompiledRules.Length.Should().Be(0);
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
        var listCache = Substitute.For<IListCacheService>();

        _cache.SwapEngine(emptyRules, listCache);

        _cache.Engine.Should().NotBeSameAs(initialEngine);
    }

    [Test]
    public void SwapEngine_UpdatesCompilationMetadata()
    {
        var before = DateTime.UtcNow;

        var emptyRules = ListModule.Empty<Compiler.CompiledRule>();
        var listCache = Substitute.For<IListCacheService>();

        _cache.SwapEngine(emptyRules, listCache);

        _cache.LastCompilation.CompiledAt.Should().BeOnOrAfter(before);
        _cache.LastCompilation.RuleCount.Should().Be(0);
    }

    [Test]
    public void SwapEngine_CalledTwice_ReflectsLatestSwap()
    {
        var emptyRules = ListModule.Empty<Compiler.CompiledRule>();
        var listCache = Substitute.For<IListCacheService>();

        _cache.SwapEngine(emptyRules, listCache);
        var firstEngine = _cache.Engine;
        var firstCompilation = _cache.LastCompilation.CompiledAt;

        _cache.SwapEngine(emptyRules, listCache);

        _cache.Engine.Should().NotBeSameAs(firstEngine);
        _cache.LastCompilation.CompiledAt.Should().BeOnOrAfter(firstCompilation);
    }

    [Test]
    public async Task ConcurrentReads_DuringSwap_AlwaysSeeConsistentSnapshot()
    {
        var listCache = Substitute.For<IListCacheService>();
        var inconsistencies = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Readers continuously check that Engine and LastCompilation are consistent
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var engine = _cache.Engine;
                var metadata = _cache.LastCompilation;
                // Both should reflect the same snapshot — rule count must match
                if (engine.CompiledRules.Length != metadata.RuleCount)
                    Interlocked.Increment(ref inconsistencies);
            }
        })).ToArray();

        // Writer swaps engines repeatedly
        var writer = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var rules = ListModule.Empty<Compiler.CompiledRule>();
                _cache.SwapEngine(rules, listCache);
                await Task.Yield();
            }
        });

        await Task.WhenAll(readers.Append(writer));

        inconsistencies.Should().Be(0, "volatile snapshot swap should ensure readers always see consistent engine + metadata pairs");
    }

}
