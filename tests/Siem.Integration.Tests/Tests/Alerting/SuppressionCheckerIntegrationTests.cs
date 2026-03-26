using FluentAssertions;
using Siem.Api.Alerting;
using Siem.Api.Data.Entities;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;

namespace Siem.Integration.Tests.Tests.Alerting;

[NotInParallel("database")]
public class SuppressionCheckerIntegrationTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test]
    public async Task NotSuppressed_WhenNoSuppressions()
    {
        await using var db = IntegrationTestFixture.CreateDbContext();
        var checker = new SuppressionChecker(db);

        var result = await checker.IsSuppressedAsync(Guid.NewGuid(), "agent-001");

        result.Should().BeFalse();
    }

    [Test]
    public async Task Suppressed_WhenRuleMatch()
    {
        var ruleId = Guid.NewGuid();

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Suppressions.Add(new SuppressionEntity
            {
                Id = Guid.NewGuid(),
                RuleId = ruleId,
                AgentId = null, // applies to all agents
                Reason = "Rule suppression",
                CreatedBy = "admin",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var checker = new SuppressionChecker(db2);

        var result = await checker.IsSuppressedAsync(ruleId, "any-agent");

        result.Should().BeTrue();
    }

    [Test]
    public async Task Suppressed_WhenAgentMatch()
    {
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Suppressions.Add(new SuppressionEntity
            {
                Id = Guid.NewGuid(),
                RuleId = null, // applies to all rules
                AgentId = "agent-001",
                Reason = "Agent suppression",
                CreatedBy = "admin",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var checker = new SuppressionChecker(db2);

        var result = await checker.IsSuppressedAsync(Guid.NewGuid(), "agent-001");

        result.Should().BeTrue();
    }

    [Test]
    public async Task Suppressed_WhenCombinationMatch()
    {
        var ruleId = Guid.NewGuid();

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Suppressions.Add(new SuppressionEntity
            {
                Id = Guid.NewGuid(),
                RuleId = ruleId,
                AgentId = "agent-001",
                Reason = "Combination suppression",
                CreatedBy = "admin",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var checker = new SuppressionChecker(db2);

        var result = await checker.IsSuppressedAsync(ruleId, "agent-001");

        result.Should().BeTrue();
    }

    [Test]
    public async Task NotSuppressed_WhenExpired()
    {
        var ruleId = Guid.NewGuid();

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Suppressions.Add(new SuppressionEntity
            {
                Id = Guid.NewGuid(),
                RuleId = ruleId,
                AgentId = null,
                Reason = "Expired suppression",
                CreatedBy = "admin",
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                ExpiresAt = DateTime.UtcNow.AddHours(-1) // already expired
            });
            await db.SaveChangesAsync();
        }

        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var checker = new SuppressionChecker(db2);

        var result = await checker.IsSuppressedAsync(ruleId, "agent-001");

        result.Should().BeFalse();
    }

    [Test]
    public async Task NotSuppressed_WhenDifferentRule()
    {
        var suppressedRuleId = Guid.NewGuid();
        var differentRuleId = Guid.NewGuid();

        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Suppressions.Add(new SuppressionEntity
            {
                Id = Guid.NewGuid(),
                RuleId = suppressedRuleId,
                AgentId = null,
                Reason = "Wrong rule",
                CreatedBy = "admin",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var checker = new SuppressionChecker(db2);

        var result = await checker.IsSuppressedAsync(differentRuleId, "agent-001");

        result.Should().BeFalse();
    }
}
