using Microsoft.FSharp.Collections;
using Siem.Rules.Core;

namespace Siem.Api.Services;

/// <summary>
/// Singleton that holds the current compiled rule engine.
/// Uses a volatile field for lock-free reads by background workers.
/// The RecompilationCoordinator swaps in new engines atomically.
/// </summary>
public class CompiledRulesCache
{
    private volatile Engine.RuleEngine _engine;
    private readonly Evaluator.IStateProvider _stateProvider;

    private CompilationMetadata _lastCompilation = CompilationMetadata.Empty;

    public CompiledRulesCache(Evaluator.IStateProvider stateProvider)
    {
        _stateProvider = stateProvider;

        // Initialize with empty engine -- CompileAsync is called on startup
        _engine = new Engine.RuleEngine(
            compiledRules: ListModule.Empty<Compiler.CompiledRule>(),
            state: stateProvider
        );
    }

    /// <summary>
    /// The hot engine instance. Background workers read this without locking --
    /// the volatile field ensures they see the latest compiled version.
    /// </summary>
    public Engine.RuleEngine Engine => _engine;

    /// <summary>
    /// Metadata about the current compilation -- exposed via the management API
    /// so operators can verify rules are loaded and fresh.
    /// </summary>
    public CompilationMetadata LastCompilation => _lastCompilation;

    /// <summary>
    /// Called by the RecompilationCoordinator after successful compilation.
    /// Constructs a new engine and performs the atomic swap.
    /// </summary>
    public void SwapEngine(
        FSharpList<Compiler.CompiledRule> compiledRules,
        ListCacheService listCache)
    {
        var newEngine = new Engine.RuleEngine(
            compiledRules: compiledRules,
            state: _stateProvider
        );

        // Capture metadata before the swap
        _lastCompilation = new CompilationMetadata
        {
            CompiledAt = DateTime.UtcNow,
            RuleCount = compiledRules.Length,
            ListCacheInfo = listCache.GetCacheInfo()
        };

        // THE SWAP -- this is the only line that changes what workers see
        _engine = newEngine;
    }
}
