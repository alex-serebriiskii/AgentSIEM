# Phase 3 Checklist — Stateful Rules & Recompilation

> **Status: COMPLETE** | All exit criteria met as of 2026-03-25
>
> **Build**: Clean (0 errors, 0 warnings) | **Tests**: 212/212 passing (60 rules core + 88 API/controller + 64 integration)

## Exit Criteria

| Criterion | Status | Evidence |
|-----------|--------|----------|
| Temporal rule (">30 tool calls in 5 min") fires on Nth event, respects partitions and windows | **Pass** | `TemporalRule_FiresOnNthEvent_WhenThresholdReached`, `TemporalRule_PartitionIsolation_IndependentCountsPerAgent`, `TemporalRule_WindowExpiration_ResetsCount`, `TemporalRule_RateAggregation_CalculatesEventsPerMinute`, `TemporalRule_ContextIncludesWindowCount`, `TemporalRule_DoesNotFire_WhenConditionDoesNotMatch` (6 tests) |
| Sequence rule ("RAG retrieval then external API call") fires only when both steps match in order within same session | **Pass** | `SequenceRule_FiresWhenAllStepsMatchInOrder`, `SequenceRule_DoesNotFire_WhenStepsAreOutOfOrder`, `SequenceRule_DoesNotFire_OnPartialMatch`, `SequenceRule_SessionIsolation_CrossSessionStepsDontCombine`, `SequenceRule_TTLExpiration_ResetsProgress`, `SequenceRule_ThreeStepSequence_FiresOnlyAfterAllSteps`, `SequenceRule_CompletionClearsProgress_AllowsRefire` (7 tests) |
| Bulk-importing 50 rules via API produces exactly one recompilation | **Pass** | `Recompilation_DebounceCoalescing_ManySignalsProduceFewCompilations` — 50 signals coalesced, single compilation with correct rule count |
| Engine status endpoint reports compiled rule count and staleness | **Pass** | `GetEngineStatus_ReportsAccurateRuleCount`, `GetEngineStatus_ReportsStaleness`, `ForceRecompile_PicksUpNewRules`, `GetEngineStatus_IncludesListCacheInfo_WhenListsExist` (4 integration tests) |

## New Tests Added in Phase 3

| Test File | New Tests | Details |
|-----------|-----------|---------|
| `tests/Siem.Integration.Tests/Tests/Rules/TemporalRuleEvaluationTests.cs` | 6 | Fires on Nth event, condition mismatch, window expiration, partition isolation, rate aggregation, context includes count |
| `tests/Siem.Integration.Tests/Tests/Rules/SequenceRuleEvaluationTests.cs` | 7 | In-order completion, out-of-order handling, partial match, session isolation, TTL expiration, 3-step sequence, refire after completion |
| `tests/Siem.Integration.Tests/Tests/Services/RecompilationCoordinatorTests.cs` | 5 | Debounce coalescing, list consistency (InList + recompile after list change), atomic swap under concurrent eval, new rule pickup + disable, malformed rule skipped |
| `tests/Siem.Integration.Tests/Tests/Controllers/EngineControllerIntegrationTests.cs` | 4 | Accurate rule count, staleness reporting, force recompile picks up new rules, list cache info included |

## Component Status

### Production Code — All Complete

| Component | Status | Location |
|-----------|--------|----------|
| RedisStateProvider (sliding windows + sequence progress) | Verified | `src/Siem.Api/Services/RedisStateProvider.cs` |
| RecompilationCoordinator (Channel-based, debounce, atomic swap) | Verified | `src/Siem.Api/Services/RecompilationCoordinator.cs` |
| IRecompilationCoordinator interface | Verified | `src/Siem.Api/Services/IRecompilationCoordinator.cs` |
| ValidateCompiledRules (dry-run synthetic events before swap) | Verified | Inside `RecompilationCoordinator.cs` |
| RulesController recompilation triggers | Verified | `src/Siem.Api/Controllers/RulesController.cs` |
| ListsController recompilation triggers | Verified | `src/Siem.Api/Controllers/ListsController.cs` |
| EngineController (GET status, POST recompile) | Verified | `src/Siem.Api/Controllers/EngineController.cs` |
| CompiledRulesCache (volatile atomic swap) | Verified | `src/Siem.Api/Services/CompiledRulesCache.cs` |
| ListCacheService (atomic refresh) | Verified | `src/Siem.Api/Services/ListCacheService.cs` |
| CompilationMetadata | Verified | `src/Siem.Api/Services/CompilationMetadata.cs` |
| DI registration (all singletons + hosted service) | Verified | `src/Siem.Api/Program.cs` |

## No Production Code Changes

Phase 3 validation required **zero changes** to production code. All Phase 3 deliverables were correctly implemented during Phase 1 scaffolding. The work consisted entirely of adding tests to verify the exit criteria, plus a `CreateSequenceRule` helper in `TestRuleFactory`.
