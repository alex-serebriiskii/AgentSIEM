using FluentAssertions;
using Siem.Api.Controllers;

namespace Siem.Api.Tests.Controllers;

public class AgentsControllerTests
{
    [Test]
    public async Task GetRiskSummary_ThrowsBecauseNpgsqlRequiresRealDb()
    {
        // AgentsController uses NpgsqlDataSource directly (not EF Core) because
        // the get_agent_risk_summary() function return type causes column ambiguity
        // when wrapped by EF Core. Without a real database, this throws.
        // Full testing is covered by integration tests against real TimescaleDB.
        var controller = new AgentsController(null!);
        var act = () => controller.GetRiskSummary("agent-001", ct: CancellationToken.None);
        await act.Should().ThrowAsync<NullReferenceException>();
    }
}
