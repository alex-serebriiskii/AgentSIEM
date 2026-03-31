using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using NSubstitute;
using Siem.Api.Data;
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

    private static RuleLoadingService CreateService()
    {
        var dbFactory = new IntegrationDbContextFactory();
        return new RuleLoadingService(dbFactory, NullLogger<RuleLoadingService>.Instance);
    }

    private class IntegrationDbContextFactory : IDbContextFactory<SiemDbContext>
    {
        public SiemDbContext CreateDbContext() => IntegrationTestFixture.CreateDbContext();
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

        var service = CreateService();

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

        var service = CreateService();

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

        var service = CreateService();

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

        var service = CreateService();

        var rules = await service.LoadEnabledRulesAsync();
        rules.Should().HaveCount(1);

        var rule = rules[0];
        rule.EvaluationType.IsTemporal.Should().BeTrue();
    }

    [Test]
    public async Task RuleSurvivesRestart_CreateThenLoadInNewContext()
    {
        // Phase 2 exit criterion: rules persist across restarts.
        // Simulate: create rule in one context, dispose it, load in a fresh context,
        // compile, and evaluate against a matching event.
        var ruleId = Guid.NewGuid();

        // Step 1: Persist a rule (simulating what RulesController.CreateRule does)
        await using (var db = IntegrationTestFixture.CreateDbContext())
        {
            db.Rules.Add(TestRuleFactory.CreateSingleEventRule(
                id: ruleId,
                name: "Persistent Rule"));
            await db.SaveChangesAsync();
        }
        // Context disposed — simulates application restart

        // Step 2: Load from a fresh context
        var service = CreateService();
        var rules = await service.LoadEnabledRulesAsync();

        rules.Should().HaveCount(1);
        rules[0].Id.Should().Be(ruleId);

        // Step 3: Compile and evaluate
        var listResolver = FuncConvert.FromFunc<Guid, FSharpSet<string>>(
            _ => SetModule.Empty<string>());
        var compiled = Compiler.compileRule(listResolver, rules[0]);

        var stateProvider = Substitute.For<Evaluator.IStateProvider>();
        var result = await FSharpAsync.StartAsTask(
            Evaluator.evaluate(stateProvider, compiled,
                TestEventFactory.CreateFSharpAgentEvent(eventType: "tool_invocation")),
            FSharpOption<TaskCreationOptions>.None,
            FSharpOption<CancellationToken>.None);

        result.Triggered.Should().BeTrue();
    }
}
