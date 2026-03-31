using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FSharp.Collections;
using NSubstitute;
using Siem.Api.Data;
using Siem.Api.Services;
using Siem.Rules.Core;

namespace Siem.Api.Tests.Services;

public class RuleCompilationOrchestratorTests
{
    private readonly IListCacheService _listCache;
    private readonly ICompiledRulesCache _rulesCache;
    private readonly ICompilationNotifier _notifier;
    private readonly RuleCompilationOrchestrator _orchestrator;

    public RuleCompilationOrchestratorTests()
    {
        _listCache = Substitute.For<IListCacheService>();
        _listCache.RefreshAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _listCache.ResolveList(Arg.Any<Guid>()).Returns(SetModule.Empty<string>());

        _rulesCache = Substitute.For<ICompiledRulesCache>();
        _notifier = Substitute.For<ICompilationNotifier>();

        var dbName = $"OrchestratorTest-{Guid.NewGuid():N}";
        var dbFactory = new InMemoryDbContextFactory(dbName);
        var ruleLoader = new RuleLoadingService(dbFactory, NullLogger<RuleLoadingService>.Instance);

        _orchestrator = new RuleCompilationOrchestrator(
            _listCache, _rulesCache,
            ruleLoader,
            _notifier,
            NullLogger<RuleCompilationOrchestrator>.Instance);
    }

    private class InMemoryDbContextFactory(string dbName) : IDbContextFactory<SiemDbContext>
    {
        public SiemDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<SiemDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            return new SiemDbContext(options);
        }
    }

    [Test]
    public async Task CompileAsync_RefreshesListCache()
    {
        await _orchestrator.CompileAsync(
            new InvalidationSignal(InvalidationReason.Startup), CancellationToken.None);

        await _listCache.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompileAsync_SwapsEngine()
    {
        await _orchestrator.CompileAsync(
            new InvalidationSignal(InvalidationReason.Startup), CancellationToken.None);

        _rulesCache.Received(1).SwapEngine(
            Arg.Any<FSharpList<Compiler.CompiledRule>>(),
            Arg.Any<IListCacheService>());
    }

    [Test]
    public async Task CompileAsync_NotifiesCompletion()
    {
        await _orchestrator.CompileAsync(
            new InvalidationSignal(InvalidationReason.Startup), CancellationToken.None);

        _notifier.Received(1).NotifyCompilationComplete();
    }

    [Test]
    public async Task CompileAsync_ExecutesInOrder_RefreshSwapNotify()
    {
        var callOrder = new List<string>();
        _listCache.RefreshAsync(Arg.Any<CancellationToken>())
            .Returns(1L)
            .AndDoes(_ => callOrder.Add("refresh"));
        _rulesCache.When(x => x.SwapEngine(
                Arg.Any<FSharpList<Compiler.CompiledRule>>(),
                Arg.Any<IListCacheService>()))
            .Do(_ => callOrder.Add("swap"));
        _notifier.When(x => x.NotifyCompilationComplete())
            .Do(_ => callOrder.Add("notify"));

        await _orchestrator.CompileAsync(
            new InvalidationSignal(InvalidationReason.Startup), CancellationToken.None);

        callOrder.Should().ContainInOrder("refresh", "swap", "notify");
    }

    [Test]
    public async Task CompileAsync_WithNoRules_SwapsEmptyEngine()
    {
        await _orchestrator.CompileAsync(
            new InvalidationSignal(InvalidationReason.RuleDeleted), CancellationToken.None);

        _rulesCache.Received(1).SwapEngine(
            Arg.Is<FSharpList<Compiler.CompiledRule>>(list => list.Length == 0),
            Arg.Any<IListCacheService>());
    }
}
