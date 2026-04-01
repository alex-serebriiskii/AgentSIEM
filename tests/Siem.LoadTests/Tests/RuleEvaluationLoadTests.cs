using System.Diagnostics;
using FluentAssertions;
using Microsoft.FSharp.Control;
using Siem.LoadTests.Fixtures;
using Siem.LoadTests.Helpers;
using Siem.Rules.Core;

namespace Siem.LoadTests.Tests;

[NotInParallel("database")]
public class RuleEvaluationLoadTests
{
    [Before(Test)]
    public async Task Cleanup()
    {
        await DbHelper.TruncateAllTablesAsync();
        await DbHelper.FlushRedisAsync();
    }

    [Test, Timeout(120_000)]
    public async Task EvaluateRules_50kEvents_58Rules_MeetsLatencyTargets(CancellationToken testCt)
    {
        const int eventCount = 50_000;
        const int singleEventRuleCount = 50;
        const int temporalRuleCount = 5;
        const int sequenceRuleCount = 3;

        // Create and compile all rules
        var allRules = new List<Siem.Api.Data.Entities.RuleEntity>();
        allRules.AddRange(LoadTestRuleFactory.CreateVariedSingleEventRules(singleEventRuleCount));
        allRules.AddRange(LoadTestRuleFactory.CreateVariedTemporalRules(temporalRuleCount));
        allRules.AddRange(LoadTestRuleFactory.CreateVariedSequenceRules(sequenceRuleCount));

        var rulesCache = await LoadTestRuleFactory.CompileAndCacheRulesAsync(allRules);
        var engine = rulesCache.Engine;

        // Generate diverse events
        var generator = new LoadTestEventGenerator(agentCount: 50, sessionsPerAgent: 3, seed: 555);
        var events = generator.GenerateEvents(eventCount, timeSpreadMinutes: 10);

        // Warmup: evaluate first 500
        for (int i = 0; i < 500; i++)
        {
            await FSharpAsync.StartAsTask(
                Engine.evaluateEvent(engine, events[i]),
                taskCreationOptions: null,
                cancellationToken: CancellationToken.None);
        }

        // Measured run
        var latencyRecorder = new LatencyRecorder();
        var meter = new ThroughputMeter(TimeSpan.FromSeconds(1));
        var sw = Stopwatch.StartNew();
        int triggeredCount = 0;

        for (int i = 0; i < eventCount; i++)
        {
            var evalSw = Stopwatch.StartNew();
            var results = await FSharpAsync.StartAsTask(
                Engine.evaluateEvent(engine, events[i]),
                taskCreationOptions: null,
                cancellationToken: CancellationToken.None);
            evalSw.Stop();

            latencyRecorder.Record(evalSw.Elapsed.TotalMilliseconds);
            meter.Record();

            if (!results.IsEmpty) triggeredCount++;
        }

        sw.Stop();

        var latencyStats = latencyRecorder.GetStats();
        var throughputStats = meter.GetStats();

        // Assertions
        var scaledP50 = LoadTestConfig.ScaleLatency(1.0);
        var scaledP99 = LoadTestConfig.ScaleLatency(10.0);
        var scaledThroughput = LoadTestConfig.ScaleThroughput(50_000);

        latencyStats.P50.Should().BeLessThan(scaledP50,
            $"P50 evaluation latency should be < {scaledP50}ms; actual: {latencyStats.P50:F3}ms");

        latencyStats.P99.Should().BeLessThan(scaledP99,
            $"P99 evaluation latency should be < {scaledP99}ms; actual: {latencyStats.P99:F3}ms");

        throughputStats.AverageRate.Should().BeGreaterThan(scaledThroughput,
            $"evaluation throughput should exceed {scaledThroughput:N0}/sec; " +
            $"actual: {throughputStats.AverageRate:N0}/sec in {sw.Elapsed.TotalSeconds:F1}s");

        // At least some rules should trigger to confirm rules are actually being evaluated
        triggeredCount.Should().BeGreaterThan(0,
            "at least some events should trigger rules to confirm evaluation is functional");
    }

    [Test, Timeout(60_000)]
    public async Task EvaluateRules_SingleEventOnly_HighThroughput(CancellationToken testCt)
    {
        const int eventCount = 100_000;

        var rules = LoadTestRuleFactory.CreateVariedSingleEventRules(20);
        var rulesCache = await LoadTestRuleFactory.CompileAndCacheRulesAsync(rules);
        var engine = rulesCache.Engine;

        var generator = new LoadTestEventGenerator(agentCount: 30, sessionsPerAgent: 2, seed: 111);
        var events = generator.GenerateEvents(eventCount, timeSpreadMinutes: 5);

        // Warmup
        for (int i = 0; i < 200; i++)
        {
            await FSharpAsync.StartAsTask(
                Engine.evaluateEvent(engine, events[i]),
                taskCreationOptions: null,
                cancellationToken: CancellationToken.None);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < eventCount; i++)
        {
            await FSharpAsync.StartAsTask(
                Engine.evaluateEvent(engine, events[i]),
                taskCreationOptions: null,
                cancellationToken: CancellationToken.None);
        }
        sw.Stop();

        var eventsPerSec = eventCount / sw.Elapsed.TotalSeconds;
        var scaledThreshold = LoadTestConfig.ScaleThroughput(100_000);

        eventsPerSec.Should().BeGreaterThan(scaledThreshold,
            $"SingleEvent-only evaluation should exceed {scaledThreshold:N0}/sec; " +
            $"actual: {eventsPerSec:N0}/sec in {sw.Elapsed.TotalSeconds:F1}s");
    }
}
