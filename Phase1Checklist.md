# Phase 1 Checklist — AgentSIEM Project State

> **Status: COMPLETE** | All exit criteria met as of 2026-03-25
>
> **Build**: Clean (0 errors, 0 warnings) | **Tests**: 162/162 passing (39 rules core + 85 API/controller + 38 integration)

## Exit Criteria

| Criterion | Status | Evidence |
|-----------|--------|----------|
| Test event published to Kafka appears in `agent_events` hypertable within 5 seconds | Pass | `EndToEnd_KafkaToTimescaleDb_CompletesWithin5Seconds` (automated) |
| Batch writes achieve 10k+ events/second | Pass | `BatchWriteThroughput_Exceeds10kEventsPerSecond` (automated) |
| Dead letter routing works for malformed events | Pass | `DeadLetterProducerTests` — 3 automated tests covering payload, headers, original header preservation |
| Consumer survives Kafka rebalance without data loss | Pass | Manual test per `RebalanceTest.md` — 2,000 events, zero loss, automatic recovery after rebalance |

## Scaffolded Components

| Component | Status | Location | Notes |
|-----------|--------|----------|-------|
| F# Rules Engine | Done | `src/Siem.Rules.Core/` (6 files) | Types.fs -> FieldResolver.fs -> Compiler.fs -> Evaluator.fs -> Engine.fs -> Serialization.fs |
| EF Core Data Layer | Done | `src/Siem.Api/Data/` (10 files) | DbContext + 8 entities + design-time factory |
| Database Migrations | Done | `src/Siem.Api/Data/Migrations/` (5 files) | InitialSchema (EF tables) + TimescaleDbFeatures (hypertable, indexes, aggregates, functions, policies) |
| Kafka Consumer Pipeline | Done | `src/Siem.Api/Kafka/` (7 files) | Consumer worker, event processing, dead letter producer, health check, config, service extensions |
| Event Normalization | Done | `src/Siem.Api/Normalization/` (2 files) | OpenTelemetry, LangChain, custom -> canonical AgentEvent |
| Batch Event Writer | Done | `src/Siem.Api/Storage/` (1 file) | PostgreSQL COPY protocol to TimescaleDB |
| Alert Pipeline | Done | `src/Siem.Api/Alerting/` (10 files) | Dedup -> throttle -> suppression -> enrichment -> persistence -> notification routing |
| Notification Channels | Done | `src/Siem.Api/Notifications/` (11 files) | SignalR (all), Webhook (medium+), Slack (high+), PagerDuty (critical) + retry worker |
| Core Services | Done | `src/Siem.Api/Services/` (9 files) | CompiledRulesCache, RecompilationCoordinator + IRecompilationCoordinator, ListCacheService, RedisStateProvider, RuleLoadingService, FSharpInteropExtensions |
| REST Controllers | Done | `src/Siem.Api/Controllers/` (5 files) | Rules, Alerts, Engine, Lists, Sessions |
| Request/Response Models | Done | `src/Siem.Api/Models/` (8 files) | Create/Update/Resolve requests + response DTOs |
| SignalR Hub | Done | `src/Siem.Api/Hubs/AlertHub.cs` | Endpoint: `/hubs/alerts` |
| DI + Middleware | Done | `src/Siem.Api/Program.cs` | Full DI registration, Swagger, health checks, auto-migration on startup |
| Docker / docker-compose | Done | `Dockerfile`, `docker-compose.yml`, `.dockerignore` | Multi-stage build, 4 services: TimescaleDB, Redis, Kafka (KRaft), siem-api |
| Unit Tests — Rules Core | Done | `tests/Siem.Rules.Core.Tests/` (6 files, 39 tests) | Compiler, Engine, Evaluator, FieldResolver, Serialization |
| Unit Tests — API | Done | `tests/Siem.Api.Tests/` (6 files, 85 tests) | AlertPipeline, AlertDeduplicator, AgentEventNormalizer, NotificationRouter, FSharpInteropExtensions, CompiledRulesCache, all 5 controllers |
| Integration Tests | Done | `tests/Siem.Integration.Tests/` (8 test files + fixtures + helpers, 38 tests) | Testcontainers for TimescaleDB + Redis + Kafka; BatchEventWriter (incl. 10k throughput benchmark), RedisStateProvider, EF CRUD, RuleLoading, ListCache, Migrations, Kafka pipeline (incl. end-to-end latency), dead-letter routing |
| CI/CD Pipeline | Done | `.github/workflows/ci.yml` | GitHub Actions: build, unit tests, integration tests, Docker image build |

## Deferred

Infrastructure-as-Code has been moved to `WhenAppropriate.md`.

## Prerequisites for Running

Docker must be installed to use `docker compose up`. The `dotnet-ef` tool is needed for standalone migrations:
```bash
dotnet tool install --global dotnet-ef
```

Note: `docker compose up` runs migrations automatically on startup.

## Tech Stack

- **.NET 10.0** (C# + F#), ASP.NET Core Web API + SignalR
- **Data**: EF Core + Npgsql (PostgreSQL/TimescaleDB), StackExchange.Redis 2.8.41
- **Messaging**: Confluent.Kafka 2.8.0
- **Testing**: TUnit 0.12.6, NSubstitute 5.3.0, AwesomeAssertions 7.0.0, Testcontainers 4.3.0
- **CI/CD**: GitHub Actions (build + test + Docker image)
- **Build config**: `Directory.Build.props` (net10.0, nullable, warnings-as-errors), `Directory.Packages.props` (centralized versions)

## Key Reference Materials

Located in `reference_materials/` — design blueprints, not production code:

| File | Purpose |
|------|---------|
| `TimescaleDB_Schema.sql` | Full DB schema with hypertables, indexes, continuous aggregates, retention policies |
| `Siem.Rules.Core.fs` | Standalone F# engine reference implementation |
| `KafkaConsumerPipeline.cs` | Reference consumer + event processing pipeline |
| `AlertPipeline.cs` | Reference alert lifecycle pipeline |
| `CacheInvalidation.cs` | Reference cache + recompilation logic |
| `CSharpIntegration.cs` | Reference C#-F# bridging patterns |
| `AgentSIEM_Project_Plan.docx` | Project planning document |
| `*.svg` (6 files) | Architecture diagrams for each subsystem |

## Architecture Summary

**Pattern**: Functional core (F#) / Imperative shell (C#)

**Event Flow**: Kafka -> Normalize -> Batch Write (TimescaleDB) -> Rule Evaluation (F# engine) -> Alert Pipeline -> Notifications

**Rule Types**: SingleEvent (stateless), Temporal (Redis sliding windows), Sequence (Redis ordered patterns)

**State**: TimescaleDB for persistence, Redis for rule evaluation state + alert dedup/throttle
