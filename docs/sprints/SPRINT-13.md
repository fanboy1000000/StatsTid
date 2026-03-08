# Sprint 13 — Employee Experience: Unified "Min Tid" Page

| Field | Value |
|-------|-------|
| **Sprint** | 13 |
| **Status** | complete |
| **Start Date** | 2026-03-08 |
| **End Date** | 2026-03-08 |
| **Orchestrator Approved** | yes (2026-03-08) |
| **Build Verified** | yes — 0 errors, 0 warnings |
| **Test Verified** | yes — 334 unit + 15 regression + 38 FE = 387 passing |

## Sprint Goal

Consolidate the employee experience into a single "Min Tid" page combining: (1) balance overview cards (flex, vacation, norm hours, overtime/merarbejde), (2) time registration (existing Skema grid), and (3) month approval (existing approval footer). New backend balance summary endpoint aggregates data from events, time entries, and agreement config. "Mine perioder" removed from primary navigation.

## Re-prioritization Note

Sprint 13 was projected for "Position Override + Wage Type Mapping UI" (Phase 3d). Re-prioritized to employee experience consolidation per user request (Tier 1 — single sprint affected). Position Override + Wage Type Mapping UI shifts to Sprint 14.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (balance endpoint is read-only aggregation, no new domain logic)
- [x] P7 — Security and access control (balance endpoint respects org-scope, employee ownership check)
- [x] P8 — CI/CD enforcement (334 unit + 15 regression + 38 FE tests passing, build clean)
- [x] P9 — Usability and UX (primary goal — unified employee page with balance cards)

## Task Log

### Phase 1 — Backend

### TASK-1301 — Balance summary endpoint

| Field | Value |
|-------|-------|
| **ID** | TASK-1301 |
| **Status** | complete |
| **Agent** | Orchestrator (delegated) |
| **Components** | Backend API |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (read-only aggregation, no P1-P4 concerns) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: New `GET /api/balance/{employeeId}/summary?year=X&month=Y` endpoint that aggregates: (1) flex balance from FlexBalanceUpdated events, (2) vacation days used this year from AbsenceRegistered events, (3) norm hours expected from agreement config (weekday-based calculation), (4) actual hours worked this month from TimeEntryRegistered events. Single API call returns all balance data. Falls back to CentralAgreementConfigs if DB config unavailable.

**Validation Criteria**:
- [x] Returns flexBalance (current balance, delta from last period)
- [x] Returns vacationDaysUsed (count of distinct VACATION absence days in current year)
- [x] Returns vacationDaysEntitlement (default 25 — Danish standard)
- [x] Returns normHoursExpected (weekdays/5 × weeklyNormHours — accurate per-month calculation)
- [x] Returns normHoursActual (sum of time entries for the month)
- [x] Returns overtimeHours (max(0, actual - expected))
- [x] Endpoint requires authentication (EmployeeOrAbove), respects org-scope
- [x] Registered in Program.cs

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs` — new endpoint file
- `src/Backend/StatsTid.Backend.Api/Program.cs` — registered MapBalanceEndpoints()

---

### Phase 2 — Frontend

### TASK-1302 — useBalanceSummary hook + TypeScript types

| Field | Value |
|-------|-------|
| **ID** | TASK-1302 |
| **Status** | complete |
| **Agent** | UX |
| **Components** | Frontend |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (frontend only) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: New hook and types for the balance summary API.

**Validation Criteria**:
- [x] BalanceSummary TypeScript interface matching backend response (9 fields)
- [x] useBalanceSummary(employeeId, year, month) hook using apiClient
- [x] Loading/error states with refetch callback

**Files Changed**:
- `frontend/src/hooks/useBalanceSummary.ts` — new hook with BalanceSummary interface

---

### TASK-1303 — BalanceSummary component

| Field | Value |
|-------|-------|
| **ID** | TASK-1303 |
| **Status** | complete |
| **Agent** | UX |
| **Components** | Frontend |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (frontend only) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: Balance cards strip component showing 4 cards: Flex saldo, Ferie, Normtimer, Merarbejde/Overarbejde. Uses designsystem.dk visual language with CSS Modules and design tokens. Skeleton loading state.

**Validation Criteria**:
- [x] 4 balance cards in a CSS grid (responsive: 4→2→1 columns)
- [x] Flex card: shows balance in hours with delta indicator (▲/▼/●)
- [x] Vacation card: shows days used / days entitled
- [x] Norm card: shows actual / expected hours
- [x] Overtime card: shows hours, label varies by agreement ("Merarbejde" for AC, "Overarbejde" for HK/PROSA)
- [x] CSS Modules with design tokens, Danish labels, skeleton loading

**Files Changed**:
- `frontend/src/components/BalanceSummary.tsx` — new component
- `frontend/src/components/BalanceSummary.module.css` — new styles

---

### TASK-1304 — Integrate into SkemaPage + sidebar rename

| Field | Value |
|-------|-------|
| **ID** | TASK-1304 |
| **Status** | complete |
| **Agent** | UX |
| **Components** | Frontend |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (frontend only) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: Added BalanceSummary to SkemaPage between month navigation and timer. Renamed sidebar "Skema" → "Min Tid". Removed "Mine perioder" from employee nav (route still accessible).

**Validation Criteria**:
- [x] BalanceSummary renders above timer on SkemaPage
- [x] Balance data refreshes when month changes (useBalanceSummary depends on year/month)
- [x] Sidebar label changed from "Skema" to "Min Tid"
- [x] "Mine perioder" removed from employee nav items
- [x] MyPeriods route still accessible directly

**Files Changed**:
- `frontend/src/pages/SkemaPage.tsx` — added useBalanceSummary hook + BalanceSummary component
- `frontend/src/components/layout/Sidebar.tsx` — renamed "Skema" → "Min Tid", removed "Mine perioder"

---

### Phase 3 — Tests

### TASK-1305 — Backend + frontend tests

| Field | Value |
|-------|-------|
| **ID** | TASK-1305 |
| **Status** | complete |
| **Agent** | Test & QA / UX |
| **Components** | Tests, Frontend Tests |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (test-only) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: Unit tests for balance calculation logic and frontend tests for BalanceSummary component.

**Validation Criteria**:
- [x] Backend: 6 test methods / 10 test cases (norm hours, weekday count, overtime calc, agreement config)
- [x] Frontend: 5 tests (render 4 cards, Merarbejde/Overarbejde label, null data, vacation format, norm format)

**Files Changed**:
- `tests/StatsTid.Tests.Unit/Sprint13BalanceTests.cs` — 10 backend test cases
- `frontend/src/components/__tests__/BalanceSummary.test.tsx` — 5 frontend tests

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | Read-only aggregation, no rule changes |
| Wage type mappings produce correct SLS codes | N/A | No mapping changes |
| Overtime/supplement calculations are deterministic | N/A | No rule changes |
| Absence effects on norm/flex/pension are correct | N/A | No rule changes |
| Retroactive recalculation produces stable results | N/A | No rule changes |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 334 | all passing |
| Regression tests | 15 | all passing |
| Smoke tests | 4 | 3/4 (pre-existing auth scope issue) |
| Frontend tests | 38 | all passing |
| **Total** | 387 | — |

## Sprint Retrospective

**What went well**: Clean sprint — read-only aggregation endpoint with no architectural risk. All 3 phases (backend, frontend, tests) completed in parallel. Tier 1 re-prioritization handled smoothly.

**What to improve**: Vacation entitlement is hardcoded at 25 days. Future sprint should make this configurable per employee or agreement.

**Knowledge produced**: None (no new patterns or decisions — reused existing endpoint, hook, and component patterns).
