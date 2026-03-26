using Microsoft.AspNetCore.Mvc;
using Siem.Api.Services;

namespace Siem.Api.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentsController(IAgentService agentService) : ControllerBase
{
    /// <summary>
    /// Get risk summary for an agent. Aggregates recent activity across
    /// events, alerts, tools, and tokens using the get_agent_risk_summary database function.
    /// </summary>
    [HttpGet("{id}/risk")]
    public async Task<IActionResult> GetRiskSummary(
        [FromRoute] string id,
        [FromQuery] string lookback = "24 hours",
        CancellationToken ct = default)
    {
        var result = await agentService.GetRiskSummaryAsync(id, lookback, ct);
        return Ok(result.Value);
    }
}
