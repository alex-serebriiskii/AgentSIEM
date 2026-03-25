using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Siem.Api.Data.Entities;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Data;

[NotInParallel("database")]
public class EfCoreCrudTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test]
    public async Task CreateAndReadRule_RoundTripsAllFields()
    {
        var rule = TestRuleFactory.CreateSingleEventRule(
            name: "CRUD Test Rule",
            severity: "high");

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(rule);
            await db.SaveChangesAsync();
        }

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var loaded = await db.Rules.FindAsync(rule.Id);
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("CRUD Test Rule");
            loaded.Severity.Should().Be("high");
            loaded.Enabled.Should().BeTrue();
            // PostgreSQL normalizes JSONB (adds spaces, may reorder keys), so compare semantically
            var actual = JsonDocument.Parse(loaded.ConditionJson).RootElement;
            var expected = JsonDocument.Parse(TestRuleFactory.FieldEqualsCondition).RootElement;
            actual.GetProperty("type").GetString().Should().Be(expected.GetProperty("type").GetString());
            actual.GetProperty("field").GetString().Should().Be(expected.GetProperty("field").GetString());
            actual.GetProperty("operator").GetString().Should().Be(expected.GetProperty("operator").GetString());
            loaded.EvaluationType.Should().Be("SingleEvent");
        }
    }

    [Test]
    public async Task CreateAlert_WithJsonbContext_RoundTrips()
    {
        var alert = new AlertEntity
        {
            AlertId = Guid.NewGuid(),
            RuleId = Guid.NewGuid(),
            RuleName = "Test Rule",
            Severity = "medium",
            Status = "open",
            Title = "Test Alert",
            Context = """{"agent":"test-agent","reason":"threshold exceeded"}""",
            AgentId = "agent-001",
            TriggeredAt = DateTime.UtcNow,
            Labels = """{"team":"security"}"""
        };

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Alerts.Add(alert);
            await db.SaveChangesAsync();
        }

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var loaded = await db.Alerts.FindAsync(alert.AlertId);
            loaded.Should().NotBeNull();
            loaded!.Context.Should().Contain("threshold exceeded");
            loaded.Labels.Should().Contain("security");
        }
    }

    [Test]
    public async Task ManagedList_CascadeDeleteMembers()
    {
        var listId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var list = new ManagedListEntity
        {
            Id = listId,
            Name = "Cascade Test",
            Description = "Test cascade delete",
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
            Members =
            [
                new ListMemberEntity { ListId = listId, Value = "item-1", AddedAt = now },
                new ListMemberEntity { ListId = listId, Value = "item-2", AddedAt = now }
            ]
        };

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.ManagedLists.Add(list);
            await db.SaveChangesAsync();
        }

        // Verify members exist
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var memberCount = await db.ListMembers.CountAsync(m => m.ListId == listId);
            memberCount.Should().Be(2);
        }

        // Delete the list
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var toDelete = await db.ManagedLists.FindAsync(listId);
            db.ManagedLists.Remove(toDelete!);
            await db.SaveChangesAsync();
        }

        // Verify members are gone (cascade delete)
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var memberCount = await db.ListMembers.CountAsync(m => m.ListId == listId);
            memberCount.Should().Be(0);
        }
    }

    [Test]
    public async Task Suppression_QueryByRuleAndAgent()
    {
        var ruleId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Suppressions.Add(new SuppressionEntity
            {
                Id = Guid.NewGuid(),
                RuleId = ruleId,
                AgentId = "agent-001",
                Reason = "Maintenance window",
                CreatedBy = "admin",
                CreatedAt = now,
                ExpiresAt = now.AddHours(1)
            });
            db.Suppressions.Add(new SuppressionEntity
            {
                Id = Guid.NewGuid(),
                RuleId = ruleId,
                AgentId = "agent-002",
                Reason = "Different agent",
                CreatedBy = "admin",
                CreatedAt = now,
                ExpiresAt = now.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var suppressions = await db.Suppressions
                .Where(s => s.RuleId == ruleId && s.AgentId == "agent-001")
                .ToListAsync();

            suppressions.Should().HaveCount(1);
            suppressions[0].Reason.Should().Be("Maintenance window");
        }
    }

    [Test]
    public async Task AlertWithEvents_IncludesRelatedEvents()
    {
        var alertId = Guid.NewGuid();
        var alert = new AlertEntity
        {
            AlertId = alertId,
            RuleId = Guid.NewGuid(),
            RuleName = "Test Rule",
            Severity = "medium",
            Status = "open",
            Title = "Alert with events",
            Context = "{}",
            AgentId = "agent-001",
            TriggeredAt = DateTime.UtcNow,
            Labels = "{}",
            AlertEvents =
            [
                new AlertEventEntity
                {
                    AlertId = alertId,
                    EventId = Guid.NewGuid(),
                    EventTimestamp = DateTime.UtcNow,
                    SequenceOrder = 1
                },
                new AlertEventEntity
                {
                    AlertId = alertId,
                    EventId = Guid.NewGuid(),
                    EventTimestamp = DateTime.UtcNow,
                    SequenceOrder = 2
                }
            ]
        };

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Alerts.Add(alert);
            await db.SaveChangesAsync();
        }

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            var loaded = await db.Alerts
                .Include(a => a.AlertEvents)
                .FirstAsync(a => a.AlertId == alertId);

            loaded.AlertEvents.Should().HaveCount(2);
        }
    }
}
