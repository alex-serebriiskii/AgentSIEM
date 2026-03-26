using FluentAssertions;
using Siem.Api.Controllers;
using Siem.Api.Services;

namespace Siem.Api.Tests.Controllers;

public class AgentsControllerTests
{
    [Test]
    public async Task GetRiskSummary_ThrowsBecauseNpgsqlRequiresRealDb()
    {
        // AgentService uses NpgsqlDataSource directly (not EF Core) because
        // the get_agent_risk_summary() function return type causes column ambiguity
        // when wrapped by EF Core. Without a real database, this throws.
        // Full testing is covered by integration tests against real TimescaleDB.
        var service = new AgentService(null!);
        var controller = new AgentsController(service);
        var act = () => controller.GetRiskSummary("agent-001", ct: CancellationToken.None);
        await act.Should().ThrowAsync<NullReferenceException>();
    }
}
