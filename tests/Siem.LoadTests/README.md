# Siem.LoadTests

Load tests for AgentSIEM that validate throughput, latency, and correctness under sustained load. These tests simulate realistic agent event streams at scale since the real agent systems cannot generate enough throughput for stage validation.

## Running

```bash
# Build
dotnet build tests/Siem.LoadTests

# Run all load tests (requires Docker for Testcontainers)
dotnet run --project tests/Siem.LoadTests
```

Tests start TimescaleDB, Redis, and Kafka containers automatically via Testcontainers.

## CI Threshold Scaling

Set environment variables to adjust thresholds on weaker CI runners:

| Variable | Default | Effect |
|----------|---------|--------|
| `LOAD_TEST_THROUGHPUT_FACTOR` | `1.0` | Multiplies events/sec thresholds (lower = more permissive) |
| `LOAD_TEST_LATENCY_FACTOR` | `1.0` | Multiplies latency thresholds (higher = more permissive) |
| `PERF_BASELINE_ENABLED` | unset | Set to `true` to enable production-target query tests (Scenarios E2/E3) |

Example: `LOAD_TEST_THROUGHPUT_FACTOR=0.5` halves all throughput requirements.

## Test Scenarios

### A: Sustained Pipeline Throughput (`SustainedPipelineThroughputTests`)

Feeds 600k events through the full `EventProcessingPipeline` (deserialize, normalize, batch write, rule evaluation) over 60 seconds. Bypasses Kafka to isolate pipeline throughput from broker limits.

- Average throughput > 10,000 events/sec
- No 5-second rolling window drops below 5,000 events/sec
- All events persisted to TimescaleDB

Also includes a 30-second `BatchEventWriter`-only sustained rate test.

### B: Multi-Agent Diversity (`MultiAgentDiversityTests`)

Writes 100k events across 100 agents, 500 sessions, and 7 event types through `BatchEventWriter`. Validates no silent drops and that the weighted event type distribution is correct.

- `COUNT(*)` = 100,000
- `COUNT(DISTINCT agent_id)` = 100
- `COUNT(DISTINCT session_id)` = 500
- Event type percentages within 20% of configured weights (tool_invocation ~35%, llm_call ~25%, rag_retrieval ~15%)

### C: Rule Evaluation Under Load (`RuleEvaluationLoadTests`)

Compiles 58 rules (50 SingleEvent + 5 Temporal + 3 Sequence) and evaluates 50k events directly through the F# engine.

- P50 evaluation latency < 1ms
- P99 evaluation latency < 10ms
- Throughput > 50,000 evaluations/sec

Also includes a SingleEvent-only test targeting 100k+ evaluations/sec.

### D: Alert Pipeline Saturation (`AlertPipelineSaturationTests`)

Fires 1,000 evaluation results (10 rules x 100 alerts) through the real `AlertPipeline` with Redis-backed deduplication and throttling, using `Parallel.ForEachAsync` at concurrency 10.

- No exceptions under concurrent load
- Per-rule alert count does not exceed throttle limit (10)
- Total alerts in DB significantly less than 1,000 (dedup/throttle working)

Also includes:

- **Suppression under load** — Seeds 5 suppressions on rules 0-4, fires 1,000 alerts across 10 rules. Verifies suppressed rules produce zero alerts while unsuppressed rules are capped by throttle.
- **Notification routing** — Wires 3 `InMemoryNotificationChannel` instances (signalr/Low, slack/High, pagerduty/Critical) with 500 alerts across Mixed/High/Critical rules. Verifies severity-based routing: pagerduty only receives Critical, signalr receives all.

### E: Query Performance Under Write Load (`QueryUnderWriteLoadTests`)

Seeds 50k events, then runs `SessionTimeline` and `AgentRiskSummary` queries while a background task continuously writes 500-event batches.

- **E1**: P95 session timeline < 200ms (production target: 50ms)
- **E2**: P95 agent risk summary < 300ms (production target: 100ms)
- Background writer completes without deadlock

Thresholds are relaxed compared to production targets to account for Testcontainer overhead and same-machine write contention.

Two additional production-target tests are gated by `PERF_BASELINE_ENABLED=true`:

- **E3**: P95 session timeline < 50ms (production target)
- **E4**: P95 agent risk summary < 100ms (production target)

These are intended for dedicated performance hardware and will skip on standard CI.

### F: Kafka Consumer Lag (`KafkaConsumerLagTests`)

Produces 10k events to a real Kafka topic, then consumes and processes them through the full pipeline.

- All 10k events consumed and persisted within 30 seconds
- Zero processing errors (no dead-lettered events)

Limited to 10k events due to single-broker Testcontainer constraints. The sustained 10k/sec target is validated by Scenario A which bypasses Kafka.

### G: Dashboard Query Performance (`DashboardQueryLoadTests`)

Seeds 100k events and 200 alerts, refreshes continuous aggregates, then queries all 4 dashboard endpoints under background write load.

- P95 < 200ms for aggregate-backed endpoints (top-agents, event-volume, tool-usage)
- P95 < 300ms for alert-distribution (queries raw alerts table)
- Correctness test verifies aggregates return non-empty, accurate data after refresh

### H: Recompilation Under Evaluation Load (`RecompilationUnderLoadTests`)

Seeds 23 rules (20 SingleEvent + 3 Temporal), then runs 4 parallel evaluation threads for 10 seconds while a 5th thread fires 20 recompilation signals with actual DB rule changes.

- Zero exceptions from evaluation threads during engine swaps
- Total evaluations > 50,000 (proves engine was under real load)
- Final engine reflects all 43 rules (23 initial + 20 dynamic)
- Atomic swap verified: no null or partially-constructed engine observed

### I: Notification Retry Under Load (`NotificationRetryLoadTests`)

Tests the `NotificationRetryWorker` with channels that simulate failures.

- **Sustained failures**: 200 notifications through a channel that fails twice then succeeds. All 200 eventually succeed within 30 seconds (zero backoff for speed).
- **Channel overflow**: 500 notifications into a capacity-100 bounded channel with a permanently-failing channel. Verifies `EnqueueRetry` never throws and the worker processes without crash.

### J: Metrics Accuracy (`MetricsAccuracyLoadTests`)

Runs 10k events through the full pipeline (normalization, batch write, rule evaluation, alert pipeline with real dedup/throttle) and verifies `System.Diagnostics.Metrics` counter consistency via `MeterListener`.

- `siem.rules.triggered` > 0
- `siem.storage.events_written` = events processed
- Alert counter conservation: `received = created + deduplicated + throttled + suppressed`
- All counters non-negative

### K: End-to-End Alert Latency (`EndToEndAlertLatencyTests`)

Measures wall-clock time from `EventProcessingPipeline.ProcessAsync` start to completion for events that trigger alerts. Exercises the full path: deserialize, normalize, batch write, rule evaluation, dedup, throttle, suppression check, enrichment, persistence, notification routing.

- P95 end-to-end latency < 500ms (Phase 6 production target baseline)
- P50 < 100ms
- At least 10% of events trigger alerts
- Alerts persisted in DB and notification channel receives dispatches

## Helpers

| File | Purpose |
|------|---------|
| `LoadTestEventGenerator` | Generates diverse event streams with weighted type distribution, seeded Random for reproducibility |
| `LoadTestRuleFactory` | Creates varied SingleEvent/Temporal/Sequence rules and compiles them into `CompiledRulesCache` |
| `ThroughputMeter` | Tracks events/sec over sliding windows with percentile stats |
| `LatencyRecorder` | Collects latency samples and computes p50/p95/p99/max |
| `LoadTestConfig` | Environment-variable-driven threshold scaling for CI |
| `InMemoryNotificationChannel` | Test notification channels: `InMemoryNotificationChannel` (records alerts), `FailingNotificationChannel` (fails N times then succeeds), `AlwaysFailingNotificationChannel` (always throws) |
| `DbHelper` | Truncates all tables and flushes Redis between tests |
