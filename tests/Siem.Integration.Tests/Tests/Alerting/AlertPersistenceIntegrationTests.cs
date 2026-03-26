using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Siem.Api.Alerting;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Alerting;

[NotInParallel("database")]
public class AlertPersistenceIntegrationTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    private static EnrichedAlert CreateTestAlert(
        Guid? ruleId = null,
        string agentId = "test-agent",
        string sessionId = "test-session",
        string severity = "medium",
        Dictionary<string, object>? context = null,
        Dictionary<string, string>? labels = null)
    {
        return new EnrichedAlert
        {
            AlertId = Guid.NewGuid(),
            RuleId = ruleId ?? Guid.NewGuid(),
            RuleName = "Test Rule",
            Severity = severity,
            Title = "Test Alert Title",
            Detail = "Test alert detail",
            AgentId = agentId,
            AgentName = "TestAgent",
            SessionId = sessionId,
            RecentAlertCount = 0,
            SessionEventCount = 5,
            RecentTools = ["tool-a", "tool-b"],
            RuleContext = context ?? new Dictionary<string, object>
            {
                ["threshold"] = 100,
                ["reason"] = "exceeded limit"
            },
            Labels = labels ?? new Dictionary<string, string>
            {
                ["team"] = "security",
                ["env"] = "production"
            },
            TriggeredAt = DateTime.UtcNow
        };
    }

    [Test]
    public async Task SaveAsync_CreatesAlertInDatabase()
    {
        var enrichedAlert = CreateTestAlert();
        var evt = TestEventFactory.CreateToolInvocation(
            agentId: enrichedAlert.AgentId,
            sessionId: enrichedAlert.SessionId);

        Guid alertId;
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var persistence = new AlertPersistence(db, NullLogger<AlertPersistence>.Instance);
            alertId = await persistence.SaveAsync(enrichedAlert, evt);
        }

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var saved = await db.Alerts.FindAsync(alertId);
            saved.Should().NotBeNull();
            saved!.RuleId.Should().Be(enrichedAlert.RuleId);
            saved.RuleName.Should().Be("Test Rule");
            saved.Severity.Should().Be("medium");
            saved.Status.Should().Be("open");
            saved.Title.Should().Be("Test Alert Title");
            saved.AgentId.Should().Be(enrichedAlert.AgentId);
            saved.SessionId.Should().Be(enrichedAlert.SessionId);
        }
    }

    [Test]
    public async Task SaveAsync_CreatesAlertEventJunction()
    {
        var enrichedAlert = CreateTestAlert();
        var evt = TestEventFactory.CreateToolInvocation(
            agentId: enrichedAlert.AgentId,
            sessionId: enrichedAlert.SessionId);

        Guid alertId;
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var persistence = new AlertPersistence(db, NullLogger<AlertPersistence>.Instance);
            alertId = await persistence.SaveAsync(enrichedAlert, evt);
        }

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var junctions = await db.AlertEvents
                .Where(ae => ae.AlertId == alertId)
                .ToListAsync();

            junctions.Should().HaveCount(1);
            junctions[0].EventId.Should().Be(evt.EventId);
            junctions[0].EventTimestamp.Should().BeCloseTo(evt.Timestamp, TimeSpan.FromSeconds(1));
        }
    }

    [Test]
    public async Task SaveAsync_StoresContextAndLabelsAsJson()
    {
        var context = new Dictionary<string, object>
        {
            ["field"] = "eventType",
            ["matched"] = "tool_invocation"
        };
        var labels = new Dictionary<string, string>
        {
            ["priority"] = "p1",
            ["team"] = "platform"
        };

        var enrichedAlert = CreateTestAlert(context: context, labels: labels);
        var evt = TestEventFactory.CreateToolInvocation(
            agentId: enrichedAlert.AgentId,
            sessionId: enrichedAlert.SessionId);

        Guid alertId;
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var persistence = new AlertPersistence(db, NullLogger<AlertPersistence>.Instance);
            alertId = await persistence.SaveAsync(enrichedAlert, evt);
        }

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var saved = await db.Alerts.FindAsync(alertId);
            saved.Should().NotBeNull();

            // Context should be stored as JSON
            saved!.Context.Should().Contain("eventType");
            saved.Context.Should().Contain("tool_invocation");

            // Labels should be stored as JSON
            saved.Labels.Should().Contain("p1");
            saved.Labels.Should().Contain("platform");
        }
    }
}
