using Microsoft.FSharp.Collections;
using Siem.Rules.Core;

namespace Siem.Api.Services;

public interface ICompiledRulesCache
{
    Engine.RuleEngine Engine { get; }
    CompilationMetadata LastCompilation { get; }
    void SwapEngine(FSharpList<Compiler.CompiledRule> compiledRules, IListCacheService listCache);
}
