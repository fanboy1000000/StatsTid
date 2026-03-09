# Sprint 15 — Entitlement & Balance Management

| Field | Value |
|-------|-------|
| **Sprint** | 15 |
| **Status** | complete |
| **Start Date** | 2026-03-09 |
| **End Date** | 2026-03-09 |
| **Orchestrator Approved** | yes (2026-03-09) |
| **Build Verified** | yes — 0 errors, 0 warnings |
| **Test Verified** | yes — 407 unit + 15 regression = 422 passing (net +54) |

## Sprint Goal

Implement entitlement & balance management (SYSTEM_TARGET.md Section K): 5 entitlement types (vacation, feriefridage, care days, child sick, senior days), DB-backed config with agreement/OK-version-aware quotas, employee balance tracking, real-time quota validation on absence registration, and frontend balance display with progress bars.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (entitlement validation calls Rule Engine via HTTP per PAT-005, atomic balance adjustment eliminates TOCTOU)
- [x] P2 — Rule engine determinism maintained (EntitlementValidationRule is pure static function, no I/O)
- [x] P3 — Event sourcing (EntitlementBalanceAdjusted + EntitlementConfigSeeded events, registered in EventSerializer)
- [x] P5 — Integration isolation (Backend→Rule Engine via HTTP, not direct function call)
- [x] P7 — Security (all endpoints require authorization, employee scope enforcement)
- [x] P8 — CI/CD enforcement (407 unit + 15 regression tests passing, build clean)
- [x] P9 — Usability (BalanceSummary with progress bars, quota error alerts on SkemaPage)

## Reviewer Audit

Reviewer invoked for TASK-1503 (Rule Engine validation — P2 mandatory) and TASK-1506 (SkemaEndpoints integration — P1/P5 cross-domain). Findings:

- **WARNING #1**: SkemaEndpoints reimplemented entitlement validation inline instead of calling Rule Engine via HTTP (PAT-005 violation). **FIXED**: Refactored to call `/api/rules/validate-entitlement` via `IHttpClientFactory`.
- **WARNING #2**: TOCTOU race condition between quota validation read and balance adjustment write. **FIXED**: Added atomic `CheckAndAdjustAsync` method using single SQL UPDATE with WHERE guard.
- **No BLOCKERs.** Both WARNINGs addressed before approval.

## Task Log

### Phase 1 — Data Model & Config

### TASK-1501 — Entitlement data model (EntitlementConfig, EntitlementBalance)

| Field | Value |
|-------|-------|
| **ID** | TASK-1501 |
| **Status** | complete |
| **Agent** | Data Model Agent |
| **Components** | SharedKernel |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (model-only, no logic) |
| **Orchestrator Approved** | yes — 2026-03-09 |

**Description**: Created EntitlementConfig model (13 properties: type, agreement, OK version, quota, accrual model, reset month, carryover, pro-rating, per-episode flag), EntitlementBalance model with computed Remaining property, and DefaultEntitlementConfigs static class with 30 configs (5 types × 3 agreements × 2 OK versions).

**Validation Criteria**:
- [x] EntitlementConfig model has all required fields
- [x] EntitlementBalance has computed Remaining = TotalQuota + CarryoverIn - Used - Planned
- [x] DefaultEntitlementConfigs.GetAll() returns 30 configs with deterministic GUIDs

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/EntitlementConfig.cs` — new: enums + sealed class
- `src/SharedKernel/StatsTid.SharedKernel/Models/EntitlementBalance.cs` — new: balance model
- `src/SharedKernel/StatsTid.SharedKernel/Config/DefaultEntitlementConfigs.cs` — new: 30 seed configs

---

### TASK-1502 — Domain events (EntitlementBalanceAdjusted, EntitlementConfigSeeded)

| Field | Value |
|-------|-------|
| **ID** | TASK-1502 |
| **Status** | complete |
| **Agent** | Data Model Agent |
| **Components** | SharedKernel, Infrastructure |
| **KB Refs** | DEP-003 |
| **Reviewer Audit** | skipped (event registration only) |
| **Orchestrator Approved** | yes — 2026-03-09 |

**Description**: Created 2 new domain events and registered them in EventSerializer type map.

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Events/EntitlementBalanceAdjusted.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/EntitlementConfigSeeded.cs` — new
- `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` — added 2 type map entries

---

### Phase 2 — Rule Engine & Infrastructure

### TASK-1503 — EntitlementValidationRule (pure function)

| Field | Value |
|-------|-------|
| **ID** | TASK-1503 |
| **Status** | complete |
| **Agent** | Rule Engine Agent |
| **Components** | Rule Engine |
| **KB Refs** | PAT-006 |
| **Reviewer Audit** | performed — no findings on rule itself (pure, deterministic) |
| **Orchestrator Approved** | yes — 2026-03-09 |

**Description**: Pure static validation function: pro-rates quota by part-time fraction, validates per-episode limits, returns ALLOWED/WARNING/REJECTED with effective quota and remaining balance. Exposed at POST `/api/rules/validate-entitlement`.

**Files Changed**:
- `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/EntitlementValidationRule.cs` — new
- `src/RuleEngine/StatsTid.RuleEngine.Api/Contracts/ValidateEntitlementRequest.cs` — new
- `src/RuleEngine/StatsTid.RuleEngine.Api/Program.cs` — added endpoint

---

### TASK-1504 — DB schema + seed data (entitlement_configs, entitlement_balances)

| Field | Value |
|-------|-------|
| **ID** | TASK-1504 |
| **Status** | complete |
| **Agent** | Orchestrator (small task — schema only) |
| **Components** | PostgreSQL |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (seed data, no logic) |
| **Orchestrator Approved** | yes — 2026-03-09 |

**Description**: 2 new tables with unique constraints. 30 seed rows for all agreement/type/version combinations.

**Files Changed**:
- `docker/postgres/init.sql` — added entitlement_configs + entitlement_balances tables + seed data

---

### TASK-1505 — Infrastructure repositories + seeder

| Field | Value |
|-------|-------|
| **ID** | TASK-1505 |
| **Status** | complete |
| **Agent** | Data Model Agent |
| **Components** | Infrastructure |
| **KB Refs** | ADR-014 |
| **Reviewer Audit** | performed — TOCTOU finding on AdjustUsedAsync (see WARNING #2) |
| **Orchestrator Approved** | yes — 2026-03-09 |

**Description**: EntitlementConfigRepository (3 query methods), EntitlementBalanceRepository (4 methods including atomic CheckAndAdjustAsync), EntitlementConfigSeeder (idempotent boot-time seeding from DefaultEntitlementConfigs).

**Validation Criteria**:
- [x] Repositories compile and integrate with DI
- [x] CheckAndAdjustAsync performs atomic check+adjust in single SQL statement
- [x] Seeder is idempotent (ON CONFLICT DO NOTHING)

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/EntitlementConfigRepository.cs` — new
- `src/Infrastructure/StatsTid.Infrastructure/EntitlementBalanceRepository.cs` — new (includes CheckAndAdjustAsync)
- `src/Infrastructure/StatsTid.Infrastructure/EntitlementConfigSeeder.cs` — new

---

### Phase 3 — Backend Integration

### TASK-1506 — SkemaEndpoints entitlement validation + balance adjustment

| Field | Value |
|-------|-------|
| **ID** | TASK-1506 |
| **Status** | complete |
| **Agent** | API Integration Agent + Orchestrator (post-review fix) |
| **Components** | Backend API |
| **KB Refs** | PAT-005 |
| **Reviewer Audit** | performed — 2 WARNINGs (PAT-005 violation, TOCTOU race). Both fixed. |
| **Orchestrator Approved** | yes — 2026-03-09 |

**Description**: POST `/api/skema/{employeeId}/save` now validates absence quota by calling Rule Engine's `/api/rules/validate-entitlement` via HTTP (PAT-005 compliant). Post-save balance adjustment uses atomic `CheckAndAdjustAsync` to eliminate TOCTOU race. Returns 422 with remaining/requested if quota exceeded, 503 if Rule Engine unavailable.

**Validation Criteria**:
- [x] Calls Rule Engine via HTTP, not inline logic
- [x] Uses atomic CheckAndAdjustAsync for balance writes
- [x] Returns 422 on quota breach with diagnostic info
- [x] Emits EntitlementBalanceAdjusted events

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` — refactored validation + adjustment
- `src/Backend/StatsTid.Backend.Api/Program.cs` — DI registration for repos + seeder

---

### TASK-1507 — BalanceEndpoints entitlement enrichment

| Field | Value |
|-------|-------|
| **ID** | TASK-1507 |
| **Status** | complete |
| **Agent** | API Integration Agent |
| **Components** | Backend API |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (read-only endpoint extension) |
| **Orchestrator Approved** | yes — 2026-03-09 |

**Description**: GET `/api/balance/{employeeId}/summary` response extended with `entitlements` array containing type, Danish label, quota, used, planned, carryover, remaining, and entitlement year. Vacation days now derived from config rather than hardcoded 25.

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs` — added entitlement data to response

---

### Phase 4 — Frontend

### TASK-1508 — BalanceSummary entitlement cards

| Field | Value |
|-------|-------|
| **ID** | TASK-1508 |
| **Status** | complete |
| **Agent** | UX Agent |
| **Components** | Frontend |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (pure UI) |
| **Orchestrator Approved** | yes — 2026-03-09 |

**Description**: EntitlementCard sub-component with progress bars showing used/total quota, carryover display, and remaining days. Integrated into BalanceSummary component.

**Files Changed**:
- `frontend/src/hooks/useBalanceSummary.ts` — added EntitlementInfo interface
- `frontend/src/components/BalanceSummary.tsx` — EntitlementCard rendering
- `frontend/src/components/BalanceSummary.module.css` — progress bar styles

---

### TASK-1509 — SkemaPage quota error handling

| Field | Value |
|-------|-------|
| **ID** | TASK-1509 |
| **Status** | complete |
| **Agent** | UX Agent |
| **Components** | Frontend |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (pure UI) |
| **Orchestrator Approved** | yes — 2026-03-09 |

**Description**: SkemaPage displays dismissible quota error alerts when save returns 422. Danish absence labels in error messages. useSkema hook parses QuotaError from 422 responses.

**Files Changed**:
- `frontend/src/hooks/useSkema.ts` — QuotaError interface, 422 handling, clearQuotaError
- `frontend/src/pages/SkemaPage.tsx` — error alert display

---

### Phase 5 — Tests

### TASK-1510 — Entitlement unit tests

| Field | Value |
|-------|-------|
| **ID** | TASK-1510 |
| **Status** | complete |
| **Agent** | Test & QA Agent |
| **Components** | Tests |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (test-only) |
| **Orchestrator Approved** | yes — 2026-03-09 |

**Description**: 54 new unit tests covering EntitlementValidationRule (24 tests: pro-rating, per-episode, warning thresholds, rejection), DefaultEntitlementConfigs (23 tests: count, uniqueness, deterministic GUIDs, agreement coverage), EntitlementBalance computed properties (7 tests).

**Files Changed**:
- `tests/StatsTid.Tests.Unit/EntitlementValidationRuleTests.cs` — new (24 tests)
- `tests/StatsTid.Tests.Unit/DefaultEntitlementConfigTests.cs` — new (23 tests)
- `tests/StatsTid.Tests.Unit/EntitlementBalanceTests.cs` — new (7 tests)

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified | 5 entitlement types per 3 agreements per 2 OK versions = 30 configs matching Danish state agreements |
| Wage type mappings produce correct SLS codes | N/A | No new SLS codes in this sprint |
| Overtime/supplement calculations are deterministic | N/A | Not modified |
| Absence effects on norm/flex/pension are correct | verified | Quota validation respects per-episode (CHILD_SICK), ferieår reset (Sep), pro-rating by part-time |
| Retroactive recalculation produces stable results | N/A | Not modified |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 407 | all passing |
| Regression tests | 15 | all passing |
| Smoke tests | 4 | N/A (requires Docker) |
| **Total** | 422 | — |

## Sprint Retrospective

**What went well**: Clean domain decomposition — entitlement model is fully separate from agreement config (different cardinality). Rule Engine validation is pure and deterministic. Atomic SQL prevents race conditions.

**What to improve**: Initial implementation had PAT-005 violation (inline validation) and TOCTOU race. Reviewer caught both. Consider adding these patterns to a checklist for future agents.

**Knowledge produced**: None formally proposed. The PAT-005 enforcement and TOCTOU resolution patterns are well-documented in existing KB.
