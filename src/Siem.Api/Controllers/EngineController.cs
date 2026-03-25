using Microsoft.AspNetCore.Mvc;
using Siem.Api.Services;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/engine")]
public class EngineController : ControllerBase
{
    private readonly CompiledRulesCache _rulesCache;
    private readonly IRecompilationCoordinator _coordinator;

    public EngineController(
        CompiledRulesCache rulesCache,
        IRecompilationCoordinator coordinator)
    {
        _rulesCache = rulesCache;
        _coordinator = coordinator;
    }

    /// <summary>
    /// Returns the current engine state: how many rules are compiled,
    /// when the last compilation happened, list cache contents, and staleness.
    /// Essential for operational monitoring.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetEngineStatus()
    {
        var meta = _rulesCache.LastCompilation;
        return Ok(new
        {
            compiledAt = meta.CompiledAt,
            ruleCount = meta.RuleCount,
            listCaches = meta.ListCacheInfo,
            staleness = (DateTime.UtcNow - meta.CompiledAt).ToString()
        });
    }

    /// <summary>
    /// Force a recompilation. Waits for completion before returning.
    /// Useful after manual DB changes, during incident response,
    /// or for peace of mind.
    /// </summary>
    [HttpPost("recompile")]
    public async Task<IActionResult> ForceRecompile(CancellationToken ct)
    {
        await _coordinator.SignalAndWaitAsync(
            new InvalidationSignal(InvalidationReason.ManualReload), ct);

        var meta = _rulesCache.LastCompilation;
        return Ok(new
        {
            status = "recompiled",
            compiledAt = meta.CompiledAt,
            ruleCount = meta.RuleCount
        });
    }
}
