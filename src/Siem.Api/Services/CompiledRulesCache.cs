using Microsoft.FSharp.Collections;
using Siem.Rules.Core;

namespace Siem.Api.Services;

/// <summary>
/// Singleton that holds the current compiled rule engine.
/// Uses a volatile field for lock-free reads by background workers.
/// The RecompilationCoordinator swaps in new engines atomically.
/// </summary>
public class CompiledRulesCache : ICompiledRulesCache
{
    private sealed record EngineSnapshot(Engine.RuleEngine Engine, CompilationMetadata Metadata);

    private volatile EngineSnapshot _snapshot;
    private readonly Evaluator.IStateProvider _stateProvider;

    public CompiledRulesCache(Evaluator.IStateProvider stateProvider)
    {
        _stateProvider = stateProvider;

        // Initialize with empty engine -- CompileAsync is called on startup
        _snapshot = new EngineSnapshot(
            new Engine.RuleEngine(
                compiledRules: ListModule.Empty<Compiler.CompiledRule>(),
                state: stateProvider),
            CompilationMetadata.Empty
        );
    }

    /// <summary>
    /// The hot engine instance. Background workers read this without locking --
    /// the volatile snapshot field ensures they see the latest compiled version.
    /// </summary>
    public Engine.RuleEngine Engine => _snapshot.Engine;

    /// <summary>
    /// Metadata about the current compilation -- exposed via the management API
    /// so operators can verify rules are loaded and fresh.
    /// </summary>
    public CompilationMetadata LastCompilation => _snapshot.Metadata;

    /// <summary>
    /// Called by the RecompilationCoordinator after successful compilation.
    /// Constructs a new engine and performs the atomic swap.
    /// Engine and metadata are bundled in a single snapshot so readers
    /// always see a consistent pair.
    /// </summary>
    public void SwapEngine(
        FSharpList<Compiler.CompiledRule> compiledRules,
        IListCacheService listCache)
    {
        var newEngine = new Engine.RuleEngine(
            compiledRules: compiledRules,
            state: _stateProvider
        );

        var newMetadata = new CompilationMetadata
        {
            CompiledAt = DateTime.UtcNow,
            RuleCount = compiledRules.Length,
            ListCacheInfo = listCache.GetCacheInfo()
        };

        // THE SWAP -- single volatile write ensures readers see
        // a consistent engine + metadata pair
        _snapshot = new EngineSnapshot(newEngine, newMetadata);
    }
}
