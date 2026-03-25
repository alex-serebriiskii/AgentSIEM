using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Siem.Api.Services;
using Siem.Integration.Tests.Fixtures;
using Siem.Integration.Tests.Helpers;
using Siem.Rules.Core;

namespace Siem.Integration.Tests.Tests.Services;

[NotInParallel("database")]
public class RuleLoadingServiceTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
    }

    [Test]
    public async Task LoadEnabledRules_ReturnsParsedFSharpTypes()
    {
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "Rule A"));
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "Rule B"));
            await db.SaveChangesAsync();
        }

        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var service = new RuleLoadingService(
            db2, NullLogger<RuleLoadingService>.Instance);

        var rules = await service.LoadEnabledRulesAsync();

        rules.Should().HaveCount(2);
        rules.Should().AllSatisfy(r =>
        {
            r.Condition.IsField.Should().BeTrue();
            r.Severity.IsMedium.Should().BeTrue();
        });
    }

    [Test]
    public async Task LoadEnabledRules_SkipsMalformedRules()
    {
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "Valid"));
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(
                name: "Malformed",
                conditionJson: """{"type":"totally_invalid_type"}"""));
            await db.SaveChangesAsync();
        }

        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var service = new RuleLoadingService(
            db2, NullLogger<RuleLoadingService>.Instance);

        var rules = await service.LoadEnabledRulesAsync();
        rules.Should().HaveCount(1);
        rules[0].Name.Should().Be("Valid");
    }

    [Test]
    public async Task LoadEnabledRules_DisabledRulesExcluded()
    {
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "Active", enabled: true));
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(name: "Disabled", enabled: false));
            await db.SaveChangesAsync();
        }

        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var service = new RuleLoadingService(
            db2, NullLogger<RuleLoadingService>.Instance);

        var rules = await service.LoadEnabledRulesAsync();
        rules.Should().HaveCount(1);
        rules[0].Name.Should().Be("Active");
    }

    [Test]
    public async Task LoadEnabledRules_WithTemporalConfig_ParsesCorrectly()
    {
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateTemporalRule(
                name: "Rate Limiter",
                windowSeconds: 120,
                threshold: 10));
            await db.SaveChangesAsync();
        }

        await using var db2 = IntegrationTestFixture.CreateDbContext();
        var service = new RuleLoadingService(
            db2, NullLogger<RuleLoadingService>.Instance);

        var rules = await service.LoadEnabledRulesAsync();
        rules.Should().HaveCount(1);

        var rule = rules[0];
        rule.EvaluationType.IsTemporal.Should().BeTrue();
    }
}
