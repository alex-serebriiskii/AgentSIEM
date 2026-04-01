# VibeSIEM

A SIEM system for monitoring LLM agent activity. Ingests agent events from Kafka, evaluates them against a rules engine, and generates alerts with notifications.

## Architecture

**Pattern**: Functional core (F#) / Imperative shell (C#)

```
Kafka -> Normalize -> Batch Write (TimescaleDB) -> Rule Evaluation (F# engine) -> Alert Pipeline -> Notifications
```

### Components

| Component | Tech | Purpose |
|-----------|------|---------|
| Rules Engine | F# (Siem.Rules.Core) | Condition AST, compiler, evaluator with exhaustive pattern matching |
| API + Pipeline | C# / ASP.NET Core (Siem.Api) | Kafka consumer, REST API, DI, EF Core, alert pipeline |
| Event Storage | TimescaleDB (PostgreSQL) | Hypertable partitioned by day, COPY protocol batch writes |
| Rule State | Redis | Sliding windows for temporal rules, sequence tracking, alert dedup/throttle |
| Messaging | Apache Kafka | Event ingestion with at-least-once delivery, dead-letter routing |
| Notifications | SignalR, Webhooks, Slack, PagerDuty | Severity-based routing with retry |

### Rule Types

- **SingleEvent** -- Stateless predicate evaluated per event
- **Temporal** -- Sliding window count/rate via Redis
- **Sequence** -- Ordered multi-step pattern tracking via Redis

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://docs.docker.com/get-docker/) (for local dev and integration tests)

## Quick Start

### Run with Docker Compose

```bash
docker compose up
```

This starts TimescaleDB, Redis, Kafka (KRaft mode), and the SIEM API on port 5000.

### Run locally

```bash
# Start infrastructure
docker compose up timescaledb redis kafka -d

# Run migrations
dotnet tool install --global dotnet-ef
dotnet ef database update --project src/Siem.Api

# Start the API
dotnet run --project src/Siem.Api
```

The API is available at `http://localhost:5000` with Swagger at `/swagger`.

## API Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET/POST/PUT/DELETE /api/rules` | CRUD for detection rules |
| `GET/PATCH /api/alerts` | Query and manage alerts |
| `GET/POST/DELETE /api/lists` | Managed lists (approved tools, blocked agents, etc.) |
| `GET /api/sessions` | Agent session timeline |
| `GET/POST /api/engine` | Engine status, trigger recompilation |
| `GET /hubs/alerts` | SignalR hub for real-time alert streaming |
| `GET /health` | Health check (includes Kafka consumer status) |

## Testing

```bash
# Unit tests (fast, no Docker needed)
dotnet run --project tests/Siem.Rules.Core.Tests
dotnet run --project tests/Siem.Api.Tests

# Integration tests (requires Docker for Testcontainers)
dotnet run --project tests/Siem.Integration.Tests
```

### Test Suites

| Suite | Tests | What it covers |
|-------|-------|---------------|
| Siem.Rules.Core.Tests | 39 | F# compiler, evaluator, field resolver, serialization |
| Siem.Api.Tests | 85 | Alert pipeline, normalizer, notification router, controllers, cache |
| Siem.Integration.Tests | 38 | EF Core CRUD, migrations, batch writer (incl. 10k/s throughput benchmark), Redis state, list cache, Kafka pipeline (incl. end-to-end latency), dead-letter routing |

### Manual Tests

The Kafka consumer rebalance survival test requires a running `docker compose` environment. See [RebalanceTest.md](RebalanceTest.md) for the step-by-step procedure.

## Run pre-built image (for testers)

No source code needed. Download the compose file and run:

```bash
curl -fsSL https://raw.githubusercontent.com/alex-serebriiskii/AgentSIEM/main/docker-compose.ghcr.yml -o docker-compose.yml
docker compose up
```

The API will be available at `http://localhost:5000` with Swagger at `/swagger`.

To pin a specific version instead of `latest`:

```bash
# Edit docker-compose.yml and change the siem-api image tag:
# image: ghcr.io/alex-serebriiskii/agentsiem:<git-sha-or-version>
```

The published image supports both `linux/amd64` and `linux/arm64` (Apple Silicon).

## CI/CD

GitHub Actions runs on push/PR to `main`:

1. Build the solution
2. Run all unit tests
3. Run integration tests (Testcontainers spins up TimescaleDB, Redis, Kafka)
4. Calculate next semantic version
5. Build and push multi-arch Docker image to [GHCR](https://ghcr.io/alex-serebriiskii/agentsiem) (main branch only)
6. Create a GitHub Release with auto-generated notes

### Versioning

Versions follow **semver** (`MAJOR.MINOR.PATCH`) and are auto-incremented on each merge to `main`:

- **Minor bump** (default): `0.1.0` -> `0.2.0` -> `0.3.0`
- **Patch bump**: Add the `patch` label to a PR before merging -> `0.3.0` -> `0.3.1`
- **Major bump**: Edit the `VERSION` file in the repo root (e.g., change `0` to `1`), then merge -> next version becomes `v1.1.0`

## Project Structure

```
src/
  Siem.Rules.Core/       F# rules engine (Types, Compiler, Evaluator, Engine, Serialization)
  Siem.Api/
    Controllers/          REST API (Rules, Alerts, Engine, Lists, Sessions)
    Kafka/                Consumer worker, processing pipeline, dead-letter producer
    Normalization/        Event type mapping (OpenTelemetry, LangChain, custom)
    Storage/              BatchEventWriter (COPY protocol)
    Alerting/             Dedup, throttle, suppression, enrichment, notifications
    Services/             CompiledRulesCache, RecompilationCoordinator, ListCache
    Data/                 EF Core context, entities, migrations
    Hubs/                 SignalR alert hub
tests/
  Siem.Rules.Core.Tests/     F# engine unit tests
  Siem.Api.Tests/            API unit + controller tests
  Siem.Integration.Tests/    Testcontainers-based integration tests
```

## Tech Stack

- **.NET 10.0** (C# + F#), ASP.NET Core, SignalR
- **TimescaleDB** (PostgreSQL + hypertables), **Redis**, **Apache Kafka**
- **EF Core** + Npgsql, Confluent.Kafka, StackExchange.Redis
- **TUnit**, NSubstitute, AwesomeAssertions, Testcontainers
