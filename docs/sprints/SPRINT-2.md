# Sprint 2 — Rule Engine Expansion, OK Versions, Frontend Scaffold

| Field | Value |
|-------|-------|
| **Sprint** | 2 |
| **Status** | complete |
| **Start Date** | 2026-01-20 |
| **End Date** | 2026-01-31 |
| **Orchestrator Approved** | yes — 2026-01-31 |
| **Build Verified** | yes — `dotnet build` 0 warnings 0 errors |
| **Test Verified** | yes — 74 unit + 0 regression = 74 total passing |

## Sprint Goal
Expand the rule engine with overtime, supplement, absence, and flex balance rules. Implement OK version transitions (OK24 → OK26). Add Danish public holiday calendar. Create agreement-specific configurations (AC, HK, PROSA). Scaffold the React frontend.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved
- [x] P2 — Rule engine determinism maintained (no I/O, no side effects)
- [x] P3 — Event sourcing append-only semantics respected
- [x] P4 — OK version correctness (entry-date resolution)
- [x] P5 — Integration isolation and delivery guarantees
- [x] P6 — Payroll integration correctness (traceability chain)
- [ ] P7 — Security and access control — _deferred to Sprint 3_
- [ ] P8 — CI/CD enforcement — _deferred to Sprint 3_
- [x] P9 — Usability and UX (frontend scaffold)

## Task Log

### TASK-201 — Implement overtime rule with AC/HK/PROSA differentiation

| Field | Value |
|-------|-------|
| **ID** | TASK-201 |
| **Status** | complete |
| **Agent** | Rule Engine |
| **Components** | Rule Engine, SharedKernel |
| **KB Refs** | ADR-002, PAT-003, RES-001 |
| **Orchestrator Approved** | yes — 2026-01-24 |

**Description**: Implemented OvertimeRule as a pure function. HK/PROSA: 37-40h at 50% supplement, >40h at 100% supplement. AC: excess hours tracked as merarbejde at 1.0x rate with no overtime calculation. Agreement-specific behavior driven entirely by AgreementRuleConfig.

**Validation Criteria**:
- [x] HK/PROSA overtime thresholds: 50% (37-40h), 100% (>40h)
- [x] AC produces MERARBEJDE at 1.0x, never OVERTIME
- [x] Pure function — config passed as parameter, no I/O
- [x] Deterministic across repeated invocations

**Files Changed**:
- `src/RuleEngine/**/Rules/OvertimeRule.cs` — Overtime/merarbejde calculation
- `src/SharedKernel/**/Models/OvertimeResult.cs` — Result type with overtime/merarbejde breakdown
- `src/SharedKernel/**/Models/AgreementRuleConfig.cs` — HasOvertime, HasMerarbejde, thresholds
- `src/RuleEngine/**/Config/AgreementConfigProvider.cs` — AC, HK, PROSA config dictionaries

---

### TASK-202 — Implement supplement rule with precedence logic

| Field | Value |
|-------|-------|
| **ID** | TASK-202 |
| **Status** | complete |
| **Agent** | Rule Engine |
| **Components** | Rule Engine, SharedKernel |
| **KB Refs** | ADR-002, PAT-002, DEP-001 |
| **Orchestrator Approved** | yes — 2026-01-24 |

**Description**: Implemented SupplementRule as a pure function. Evaluates evening, night, weekend, and holiday supplements with strict precedence: Holiday > Weekend > Evening/Night. No double-dipping. AC has all supplements disabled.

**Validation Criteria**:
- [x] Precedence order enforced: Holiday > Weekend > Evening/Night
- [x] Only one supplement applied per work period
- [x] AC supplements all disabled (RES-001)
- [x] Calendar context used for holiday/weekend detection
- [x] Pure function — no I/O

**Files Changed**:
- `src/RuleEngine/**/Rules/SupplementRule.cs` — Supplement evaluation with precedence
- `src/SharedKernel/**/Models/SupplementResult.cs` — Result type with supplement type and rate
- `src/SharedKernel/**/Events/SupplementCalculated.cs` — Domain event

---

### TASK-203 — Implement absence rule and absence events

| Field | Value |
|-------|-------|
| **ID** | TASK-203 |
| **Status** | complete |
| **Agent** | Rule Engine, Data Model |
| **Components** | Rule Engine, SharedKernel |
| **KB Refs** | ADR-002, PAT-001 |
| **Orchestrator Approved** | yes — 2026-01-24 |

**Description**: Implemented AbsenceRule for evaluating vacation, sick leave, care days, child's sick day, and parental leave. Absence effects impact norm fulfillment and flex balance. Created absence entry model and absence registered event.

**Validation Criteria**:
- [x] Vacation, sick leave, care days, child's sick day, parental leave supported
- [x] Absence reduces norm requirement proportionally
- [x] Absence effects are rule-driven per agreement config
- [x] Immutable models (PAT-001)

**Files Changed**:
- `src/RuleEngine/**/Rules/AbsenceRule.cs` — Absence evaluation
- `src/SharedKernel/**/Models/AbsenceEntry.cs` — Absence entry model
- `src/SharedKernel/**/Events/AbsenceRegistered.cs` — Domain event
- `src/Backend/StatsTid.Backend.Api/Contracts/RegisterAbsenceRequest.cs` — API contract

---

### TASK-204 — Implement flex balance rule

| Field | Value |
|-------|-------|
| **ID** | TASK-204 |
| **Status** | complete |
| **Agent** | Rule Engine |
| **Components** | Rule Engine, SharedKernel |
| **KB Refs** | ADR-002, PAT-003 |
| **Orchestrator Approved** | yes — 2026-01-24 |

**Description**: Implemented FlexBalanceRule for tracking flex saldo. Calculates carry-over, maximum saldo, and automatic conversion. Different flex limits per agreement. Pure function with config-driven behavior.

**Validation Criteria**:
- [x] Flex balance calculated from actual vs norm hours
- [x] Maximum saldo enforced per agreement
- [x] Carry-over rules applied
- [x] Pure function — config parameter, no I/O

**Files Changed**:
- `src/RuleEngine/**/Rules/FlexBalanceRule.cs` — Flex balance calculation
- `src/SharedKernel/**/Events/FlexBalanceUpdated.cs` — Domain event

---

### TASK-205 — Implement OK version resolution and Danish public holidays

| Field | Value |
|-------|-------|
| **ID** | TASK-205 |
| **Status** | complete |
| **Agent** | Rule Engine |
| **Components** | Rule Engine, SharedKernel (Calendar) |
| **KB Refs** | ADR-003, DEP-001 |
| **Orchestrator Approved** | yes — 2026-01-28 |

**Description**: Implemented OkVersionResolver that maps entry dates to OK agreement periods (OK24: 2024-04-01 to 2026-03-31, OK26: 2026-04-01+). Created DanishPublicHolidays calendar with version-aware holiday list (Store Bededag removed from OK24 onwards). Version resolution uses entry date, not current date, ensuring deterministic replay.

**Validation Criteria**:
- [x] Entry date determines OK version, not current date
- [x] OK24 and OK26 boundaries correctly defined
- [x] Store Bededag absent from OK24 holiday list
- [x] Holiday calculations support Easter-based moveable feasts
- [x] Deterministic — same date always resolves to same version

**Files Changed**:
- `src/RuleEngine/**/Config/OkVersionResolver.cs` — Date-to-version mapping
- `src/SharedKernel/**/Calendar/DanishPublicHolidays.cs` — Version-aware holiday calendar
- `src/SharedKernel/**/Models/RuleVersion.cs` — OK version model

---

### TASK-206 — Expand domain events and event serialization

| Field | Value |
|-------|-------|
| **ID** | TASK-206 |
| **Status** | complete |
| **Agent** | Data Model |
| **Components** | SharedKernel, Infrastructure |
| **KB Refs** | ADR-005, DEP-003, PAT-004 |
| **Orchestrator Approved** | yes — 2026-01-28 |

**Description**: Created new domain events for Sprint 2 features (OvertimeCalculated, SupplementCalculated, AbsenceRegistered, FlexBalanceUpdated). Registered all new types in EventSerializer type map. All events extend DomainEventBase.

**Validation Criteria**:
- [x] All new events extend DomainEventBase
- [x] All events registered in EventSerializer type map
- [x] Serialization round-trip works for all types
- [x] Immutable event properties (PAT-001)

**Files Changed**:
- `src/SharedKernel/**/Events/OvertimeCalculated.cs` — New event
- `src/SharedKernel/**/Events/SupplementCalculated.cs` — New event
- `src/SharedKernel/**/Events/AbsenceRegistered.cs` — New event
- `src/SharedKernel/**/Events/FlexBalanceUpdated.cs` — New event
- `src/Infrastructure/EventSerializer.cs` — Updated type map with 4 new types

---

### TASK-207 — Scaffold React frontend

| Field | Value |
|-------|-------|
| **ID** | TASK-207 |
| **Status** | complete |
| **Agent** | UX |
| **Components** | Frontend |
| **KB Refs** | — |
| **Orchestrator Approved** | yes — 2026-01-31 |

**Description**: Created React + TypeScript + Vite frontend with react-router-dom. Pages: TimeRegistration, WeeklyView, AbsenceRegistration, HealthDashboard. Components: TimeEntryForm, WeekGrid, FlexBalanceCard. Hooks: useTimeEntries, useAbsences, useFlexBalance.

**Validation Criteria**:
- [x] Vite project builds successfully
- [x] Router with 4 pages
- [x] API hooks consume backend endpoints
- [x] TypeScript types match backend contracts

**Files Changed**:
- `frontend/` — Complete React scaffold (package.json, tsconfig, vite config)
- `frontend/src/pages/` — TimeRegistration, WeeklyView, AbsenceRegistration, HealthDashboard
- `frontend/src/components/` — TimeEntryForm, WeekGrid, FlexBalanceCard
- `frontend/src/hooks/` — useTimeEntries, useAbsences, useFlexBalance
- `frontend/src/types.ts` — Shared TypeScript types

---

### TASK-208 — Expand unit tests to cover Sprint 2 rules

| Field | Value |
|-------|-------|
| **ID** | TASK-208 |
| **Status** | complete |
| **Agent** | Test & QA |
| **Components** | Tests |
| **KB Refs** | ADR-002, ADR-003, PAT-002, RES-001 |
| **Orchestrator Approved** | yes — 2026-01-31 |

**Description**: Expanded unit test suite from 12 to 74 tests. Coverage for OvertimeRule (AC vs HK/PROSA), SupplementRule (precedence, double-dipping prevention), AbsenceRule (all types), FlexBalanceRule, OkVersionResolver, DanishPublicHolidays, and AgreementConfig.

**Validation Criteria**:
- [x] Overtime tests verify AC merarbejde vs HK/PROSA overtime
- [x] Supplement tests verify precedence order
- [x] Absence tests cover all absence types
- [x] OK version tests prove entry-date resolution
- [x] Holiday tests include Store Bededag removal
- [x] All 74 tests green

**Files Changed**:
- `tests/StatsTid.Tests.Unit/OvertimeRuleTests.cs` — Overtime/merarbejde tests
- `tests/StatsTid.Tests.Unit/SupplementRuleTests.cs` — Supplement precedence tests
- `tests/StatsTid.Tests.Unit/AbsenceRuleTests.cs` — Absence evaluation tests
- `tests/StatsTid.Tests.Unit/FlexBalanceRuleTests.cs` — Flex balance tests
- `tests/StatsTid.Tests.Unit/OkVersionResolverTests.cs` — Version resolution tests
- `tests/StatsTid.Tests.Unit/DanishPublicHolidaysTests.cs` — Holiday calendar tests
- `tests/StatsTid.Tests.Unit/AgreementConfigTests.cs` — Config validation tests

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified | AC (merarbejde, no overtime), HK (50%/100% overtime), PROSA (overtime + supplements) |
| Wage type mappings produce correct SLS codes | verified | NORMAL_HOURS, OVERTIME_50, OVERTIME_100, MERARBEJDE, EVENING, NIGHT, WEEKEND, HOLIDAY mapped |
| Overtime/supplement calculations are deterministic | verified | Unit tests prove same input → same output; pure functions with no I/O |
| Absence effects on norm/flex/pension are correct | verified | Vacation, sick leave, care days tested; norm reduction proportional |
| Retroactive recalculation produces stable results | verified | OK version entry-date resolution ensures replay stability |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 74 | all passing |
| Regression tests | 0 | N/A |
| Smoke tests | 4 | all passing (with Docker) |
| **Total** | 74 | — |

## Sprint Retrospective

**What went well**: Rule engine fully expanded in one sprint. Clear separation between AC and HK/PROSA behavior. OK version resolution is deterministic and well-tested. Supplement precedence prevents double-dipping.

**What to improve**: No authentication or authorization. No CI/CD pipeline. Frontend is a scaffold — needs auth integration. Regression test suite not yet created.

**Knowledge produced**: ADR-003, PAT-002, PAT-003, DEP-001, DEP-002, RES-001
