# Sprint 5 — On-Call Duty, Flex Unification, Retroactive Corrections, SLS Export

| Field | Value |
|-------|-------|
| **Sprint** | 5 |
| **Status** | complete |
| **Start Date** | 2026-03-02 |
| **End Date** | 2026-03-02 |
| **Orchestrator Approved** | yes — 2026-03-02 |
| **Build Verified** | yes — `dotnet build` 0 warnings 0 errors |
| **Test Verified** | yes — 143 unit + 15 regression = 158 total passing |

## Sprint Goal
Complete Phase 1 by adding on-call duty basics, unifying the flex endpoint, laying retroactive correction foundations, producing SLS-formatted payroll export, and addressing Sprint 4 backlog (event emission, HTTP parallelization).

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (bounded contexts respected, on-call duty integrated into payroll chain)
- [x] P2 — Rule engine determinism maintained (OnCallDutyRule is pure function, no I/O)
- [x] P3 — Event sourcing append-only semantics respected (PeriodCalculationCompleted + RetroactiveCorrectionRequested events)
- [x] P4 — OK version correctness (on-call configs keyed by OK version, entry-date resolution intact)
- [x] P5 — Integration isolation and delivery guarantees (Payroll calls Rule Engine via HTTP only)
- [x] P6 — Payroll integration correctness (ON_CALL_DUTY integrated into PeriodCalculationService, traceability chain maintained)
- [x] P7 — Security and access control (new /api/payroll/recalculate endpoint requires authentication)
- [ ] P8 — CI/CD enforcement (no CI changes this sprint)
- [ ] P9 — Usability and UX (no frontend changes this sprint)

## Task Log

### TASK-501 — Flex Endpoint Unification

| Field | Value |
|-------|-------|
| **ID** | TASK-501 |
| **Status** | complete |
| **Agent** | Rule Engine (cross-domain: Payroll) |
| **Components** | RuleEngine/Program, Payroll/PeriodCalculationService |
| **KB Refs** | PAT-005, ADR-002, DEP-002 |
| **Reviewer Audit** | performed — WARNING (anonymous type fragility, assessed as acceptable for Sprint 5) |
| **Orchestrator Approved** | yes — 2026-03-02 |

**Description**: Unified the evaluate-flex endpoint to return CalculationResult-compatible response with ruleId + lineItems, plus all FlexBalanceResult fields. Simplified PeriodCalculationService.CallFlexRuleAsync to use standard CalculationResult deserialization instead of JsonDocument workaround.

**Validation Criteria**:
- [x] evaluate-flex endpoint returns object with ruleId, lineItems, plus all FlexBalanceResult fields
- [x] lineItems contains FLEX_PAYOUT item when ExcessForPayout > 0, empty array otherwise
- [x] PeriodCalculationService.CallFlexRuleAsync uses standard CalculationResult deserialization
- [x] System.Text.Json import preserved
- [x] Backward-compatible (all FlexBalanceResult fields still in response)

**Files Changed**:
- `src/RuleEngine/StatsTid.RuleEngine.Api/Program.cs` — unified evaluate-flex endpoint response
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` — simplified CallFlexRuleAsync

---

### TASK-502 — On-Call Duty Basics

| Field | Value |
|-------|-------|
| **ID** | TASK-502 |
| **Status** | complete |
| **Agent** | Rule Engine + Data Model |
| **Components** | SharedKernel/Models, RuleEngine/Rules, RuleEngine/Config |
| **KB Refs** | ADR-002, PAT-003, RES-001, PAT-001 |
| **Reviewer Audit** | performed — no findings on P2 determinism |
| **Orchestrator Approved** | yes — 2026-03-02 |

**Description**: Added basic on-call duty (rådighedsvagt) rule. On-call entries (ActivityType = "ON_CALL") produce ON_CALL_DUTY line items at a configurable reduced rate (typically 1/3). AC disabled by default, HK/PROSA enabled.

**Validation Criteria**:
- [x] AgreementRuleConfig has OnCallDutyEnabled and OnCallDutyRate properties
- [x] All 6 AgreementConfigProvider entries updated (AC: disabled, HK/PROSA: enabled at 0.33m)
- [x] OnCallDutyRule.cs created as pure function
- [x] Filters ON_CALL activity type entries, produces ON_CALL_DUTY line items at reduced rate
- [x] Returns empty result when disabled
- [x] Registered in RuleRegistry ConfigAwareTimeRules and EvaluateTimeRule switch

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/AgreementRuleConfig.cs` — added OnCallDutyEnabled, OnCallDutyRate
- `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/OnCallDutyRule.cs` — new file (60 lines)
- `src/RuleEngine/StatsTid.RuleEngine.Api/Config/AgreementConfigProvider.cs` — updated 6 configs
- `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RuleRegistry.cs` — registered ON_CALL_DUTY

---

### TASK-503 — Event Emission + HTTP Parallelization

| Field | Value |
|-------|-------|
| **ID** | TASK-503 |
| **Status** | complete |
| **Agent** | Payroll Integration |
| **Components** | Integrations/Payroll |
| **KB Refs** | PAT-005, ADR-001, DEP-003, PAT-004 |
| **Reviewer Audit** | performed — WARNING (retroactive idempotency gap, deferred to Phase 2) |
| **Orchestrator Approved** | yes — 2026-03-02 |

**Description**: Registered IEventStore in Payroll DI. PeriodCalculationService now emits PeriodCalculationCompleted event after successful calculation (non-fatal on failure). Parallelized 4 independent rule calls (norm, supplement, overtime, on-call) + absence via Task.WhenAll. Flex remains sequential.

**Validation Criteria**:
- [x] IEventStore registered via PostgresEventStore
- [x] PeriodCalculationCompleted event emitted after success
- [x] Event emission failure does not fail calculation
- [x] Independent rules parallelized via Task.WhenAll
- [x] Flex rule remains sequential
- [x] ON_CALL_DUTY added to parallel rule list (Reviewer fix)

**Files Changed**:
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — IEventStore DI registration
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` — parallelization + event emission

---

### TASK-504 — Retroactive Correction Models

| Field | Value |
|-------|-------|
| **ID** | TASK-504 |
| **Status** | complete |
| **Agent** | Data Model |
| **Components** | SharedKernel/Events, SharedKernel/Models, Infrastructure/EventSerializer |
| **KB Refs** | ADR-005, PAT-001, PAT-004, DEP-003 |
| **Reviewer Audit** | performed — no findings |
| **Orchestrator Approved** | yes — 2026-03-02 |

**Description**: Created RetroactiveCorrectionRequested event (extends DomainEventBase) with OriginalPeriodStart/End, Reason, CorrectedByActorId, correction metrics. Created CorrectionExportLine model with original/corrected/difference amounts and traceability. Registered event in EventSerializer.

**Validation Criteria**:
- [x] RetroactiveCorrectionRequested event extends DomainEventBase
- [x] CorrectionExportLine model with diff fields
- [x] All properties use init-only (PAT-001)
- [x] EventSerializer type map includes "RetroactiveCorrectionRequested"

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Events/RetroactiveCorrectionRequested.cs` — new file
- `src/SharedKernel/StatsTid.SharedKernel/Models/CorrectionExportLine.cs` — new file
- `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` — type map registration

---

### TASK-505 — Retroactive Correction Service

| Field | Value |
|-------|-------|
| **ID** | TASK-505 |
| **Status** | complete |
| **Agent** | Payroll Integration |
| **Components** | Integrations/Payroll |
| **KB Refs** | PAT-005, ADR-001, PAT-004 |
| **Reviewer Audit** | performed — NOTE (correction aggregation by wage type loses day-level granularity, acceptable for SLS) |
| **Orchestrator Approved** | yes — 2026-03-02 |

**Description**: Created RetroactiveCorrectionService that re-runs PeriodCalculationService for a past period, diffs against previous export lines by wage type, and produces CorrectionExportLine objects. Emits RetroactiveCorrectionRequested event. Added POST /api/payroll/recalculate endpoint with authentication.

**Validation Criteria**:
- [x] RetroactiveCorrectionService with RecalculateAsync method
- [x] Re-runs PeriodCalculationService for past period
- [x] Produces CorrectionExportLines only when diff != 0
- [x] Emits RetroactiveCorrectionRequested event (non-fatal)
- [x] POST /api/payroll/recalculate endpoint with authentication
- [x] RecalculateRequest DTO includes PreviousExportLines and Reason

**Files Changed**:
- `src/Integrations/StatsTid.Integrations.Payroll/Services/RetroactiveCorrectionService.cs` — new file (177 lines)
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — DI + endpoint + DTO

---

### TASK-506 — On-Call Wage Mappings + SLS Export Formatter

| Field | Value |
|-------|-------|
| **ID** | TASK-506 |
| **Status** | complete |
| **Agent** | Payroll Integration |
| **Components** | Database/Seed Data, Integrations/Payroll |
| **KB Refs** | DEP-002, RES-001 |
| **Reviewer Audit** | performed — NOTE (rule_versions seed gap, fixed by Orchestrator) |
| **Orchestrator Approved** | yes — 2026-03-02 |

**Description**: Added 6 ON_CALL_DUTY wage type mapping rows (SLS_0710, 3 agreements × 2 OK versions). Added 6 ON_CALL_DUTY rule_versions rows (Reviewer fix). Created SlsExportFormatter static utility producing pipe-delimited H/D/T records with InvariantCulture and deterministic checksum.

**Validation Criteria**:
- [x] 6 ON_CALL_DUTY wage type mappings (SLS_0710)
- [x] 6 ON_CALL_DUTY rule_versions entries
- [x] ON CONFLICT DO NOTHING pattern maintained
- [x] SlsExportFormatter with H/D/T pipe-delimited records
- [x] InvariantCulture formatting (Orchestrator fix for locale-dependent timestamps)
- [x] Deterministic checksum

**Files Changed**:
- `docker/postgres/init.sql` — 12 new seed data rows (6 wage mappings + 6 rule versions)
- `src/Integrations/StatsTid.Integrations.Payroll/Services/SlsExportFormatter.cs` — new file (55 lines)

---

### TASK-507 — Unit + Regression Tests

| Field | Value |
|-------|-------|
| **ID** | TASK-507 |
| **Status** | complete |
| **Agent** | Test & QA |
| **Components** | Tests |
| **KB Refs** | ADR-002, DEP-002, DEP-003 |
| **Reviewer Audit** | performed — no findings on test coverage |
| **Orchestrator Approved** | yes — 2026-03-02 |

**Description**: Added 25 new tests: 8 OnCallDutyRule unit tests (enabled/disabled, filtering, rate), 13 Sprint 5 model tests (event types, serialization, SLS formatter, correction diff), 4 regression tests (AC on-call disabled, HK on-call, flex payout, correction diff arithmetic). Required fields fix applied by Orchestrator.

**Validation Criteria**:
- [x] 8 OnCallDutyRule tests covering enabled/disabled, filtering, rate application
- [x] 13 Sprint 5 model tests covering events, CorrectionExportLine, SlsExportFormatter
- [x] 4 regression tests (on-call AC/HK, flex payout, correction diff)
- [x] All 158 tests pass
- [x] No regressions in existing tests

**Files Changed**:
- `tests/StatsTid.Tests.Unit/OnCallDutyRuleTests.cs` — new file (8 tests)
- `tests/StatsTid.Tests.Unit/Sprint5ModelTests.cs` — new file (13 tests)
- `tests/StatsTid.Tests.Regression/RegressionTests.cs` — 4 new regression tests
- `tests/StatsTid.Tests.Unit/StatsTid.Tests.Unit.csproj` — added Payroll project reference
- `tests/StatsTid.Tests.Regression/StatsTid.Tests.Regression.csproj` — added Payroll project reference

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified | On-call duty: AC disabled, HK/PROSA enabled at 1/3 rate per Danish state rules |
| Wage type mappings produce correct SLS codes | verified | ON_CALL_DUTY → SLS_0710 (6 new mappings) |
| Overtime/supplement calculations are deterministic | verified | 8 OnCallDutyRule determinism tests + existing regression tests |
| Absence effects on norm/flex/pension are correct | verified | Flex payout regression test passing |
| Retroactive recalculation produces stable results | verified | Correction diff arithmetic regression test |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 143 | all passing |
| Regression tests | 15 | all passing |
| Smoke tests | 4 | N/A (requires Docker) |
| **Total** | **158** | — |

## Sprint Retrospective

**What went well**: Multi-agent parallel execution with worktree isolation scaled well to 3-agent phases. All 6 worktree merges were clean (no conflicts). Reviewer caught the critical ON_CALL_DUTY gap in PeriodCalculationService before it became a runtime issue. Locale fix for SlsExportFormatter caught early by test suite.

**What to improve**:
- Phase 2 agents duplicated IEventStore registration in Program.cs — clearer cross-agent coordination needed
- Test agent missed required properties on RetroactiveCorrectionRequested — agent context should include complete model definitions
- Sprint 6: Consider adding idempotency tokens for retroactive correction events (Reviewer WARNING)
- Sprint 6: Consider defining explicit FlexEvaluationResponse DTO in SharedKernel (Reviewer WARNING)

**Knowledge produced**: PAT-006 (proposed — Unified Rule Endpoint Response Format) pending formal approval
