# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

AgentSIEM (VibeSIEM) is a SIEM system for monitoring LLM agent activity. It ingests agent events from Kafka, evaluates them against a rules engine, and generates alerts with notifications. All core components are implemented and tested (F# rules engine, Kafka consumer pipeline, alert pipeline, notification channels, REST API, EF Core data layer with TimescaleDB migrations). The `reference_materials/` directory contains the original design blueprints (code samples, SQL schema, architecture diagrams) used during scaffolding.

## Build & Test Commands

```bash
# Build the entire solution
dotnet build

# Run unit tests (no Docker needed)
dotnet run --project tests/Siem.Rules.Core.Tests
dotnet run --project tests/Siem.Api.Tests

# Run integration tests (requires Docker for Testcontainers)
dotnet run --project tests/Siem.Integration.Tests

# Run everything with Docker Compose
docker compose up
```

Note: This project uses **TUnit** (not xUnit/NUnit). Tests are run with `dotnet run`, not `dotnet test`. TUnit projects have `OutputType: Exe`.

## Architecture

The system uses a **functional core / imperative shell** pattern:

- **Siem.Rules.Core (F#)** — Pure functional rules engine. Contains the Condition AST (discriminated unions), rule compiler (condition trees → executable predicates), and evaluator (strategy dispatch via pattern matching). This is referenced as a project dependency by the C# host.
- **Siem.Api (C# / ASP.NET Core)** — The imperative shell. Hosts Kafka consumers, REST API, DI, EF Core, Redis integration, alert pipeline, and notification routing.

### Event Flow

1. **Kafka Consumer** (`KafkaConsumerWorker`) consumes from `agent-events` topic with manual offset commits (at-least-once delivery)
2. **Normalization** (`AgentEventNormalizer`) maps framework-specific events (OpenTelemetry, LangChain, custom) to canonical `AgentEvent` type
3. **Batch Write** (`BatchEventWriter`) buffers events and writes to TimescaleDB via COPY protocol
4. **Rule Evaluation** — Events are evaluated against all compiled rules via the F# engine (`Engine.evaluateEvent`)
5. **Alert Pipeline** — Triggered rules pass through dedup → throttle → suppression → enrichment → persistence → notification routing

### Rules Engine

- Three evaluation types: **SingleEvent** (stateless predicate), **Temporal** (sliding window count/rate via Redis), **Sequence** (ordered multi-step patterns tracked in Redis)
- Rules are stored as JSONB in PostgreSQL, parsed into F# discriminated unions via `Serialization.parseCondition`, then compiled into closures
- **Recompilation** is debounced (500ms window) and coordinated by `RecompilationCoordinator` — list cache refresh + rule compilation + validation happen atomically before an engine swap
- Managed lists (approved tools, blocked agents, etc.) are snapshotted at compile time into F# Sets; list changes trigger recompilation

### Data Layer

- **TimescaleDB** (PostgreSQL + timescaledb extension) for event storage — `agent_events` hypertable partitioned by day
- **Redis** for rule evaluation state (sliding windows, sequence progress) and alert dedup/throttle
- **EF Core** for CRUD on rules, alerts, sessions, managed lists
- Schema is in `reference_materials/TimescaleDB_Schema.sql`

### Notification Channels

Severity-based routing with retry: SignalR (all), Webhooks (medium+), Slack (high+), PagerDuty (critical only).

## Key Design Decisions

- F# for the rules engine core — exhaustive pattern matching catches missed cases at compile time when new condition variants are added
- C#-to-F# bridge uses `FuncConvert.FromFunc` for the list resolver and `FSharpAsync.StartAsTask` for async interop
- `CompiledRulesCache` uses a `volatile` field for lock-free reads by background workers; new engines are swapped atomically
- Batch event writes use PostgreSQL COPY protocol (5-10x faster than individual INSERTs)
- Dead letter topic (`{topic}.dead-letter`) preserves failed events with original headers and error metadata

## Reference Materials

The `reference_materials/` directory contains design artifacts — not production code:
- `TimescaleDB_Schema.sql` — Full database schema including hypertables, indexes, continuous aggregates, compression/retention policies, and helper functions
- `Siem.Rules.Core.fs` — F# rules engine: condition AST, compiler, evaluator, serialization
- `KafkaConsumerPipeline.cs` — Kafka consumer, event processing pipeline, batch writer, dead letter producer
- `AlertPipeline.cs` — Alert lifecycle: dedup, throttle, suppression, enrichment, persistence, notification channels
- `CacheInvalidation.cs` — List cache, recompilation coordinator, compiled rules cache, REST controllers
- `CSharpIntegration.cs` — Rule loading service, Redis state provider, C#↔F# bridging patterns
- SVG files — Architecture diagrams for each subsystem
