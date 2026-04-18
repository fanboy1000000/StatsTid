# Sprint 18 — Codex BLOCKER Remediation

| Field | Value |
|-------|-------|
| **Sprint** | 18 |
| **Status** | planned |
| **Start Date** | 2026-04-18 |
| **End Date** | — |
| **Orchestrator Approved** | no |
| **Build Verified** | no |
| **Test Verified** | no |

## Sprint Goal

Remediate the 3 Codex BLOCKERs and 1 high-impact WARNING surfaced by the external review on 2026-04-18 ([`docs/reviews/codex-2026-04-18.md`](../reviews/codex-2026-04-18.md)): OK-version drift at the write/payroll boundary, wage-type mapping lookup semantics, under-scoped orchestrator/payroll endpoints, and `UserUpdated` missing from `EventSerializer`. Stabilize correctness before the Phase 3h UI/UX polish sprint.

## Entropy Scan Findings

_Pre-sprint entropy scan (Step 0a) to be run at sprint start._

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | pending | _Run before first task_ |
| Pattern compliance spot-check | pending | _Check for Rule-Engine→Infrastructure imports (Codex Rec #2 — informational only for S18)_ |
| Orphan detection | pending | _Focus on files created in S16–S17_ |
| Documentation drift | pending | _Codex already surfaced drift in docs/QUALITY.md:15,37 and MEMORY.md references in docs/WORKFLOW.md:176,253 — address or document as accepted_ |
| Quality grade review | pending | _Update docs/QUALITY.md after S17; security grade may warrant revision given Codex BLOCKER #7_ |

## Architectural Constraints Verified

_Check each constraint that was explicitly validated during this sprint._

- [ ] P1 — Architectural integrity preserved
- [ ] P2 — Rule engine determinism maintained (no I/O, no side effects)
- [ ] P3 — Event sourcing append-only semantics respected
- [ ] P4 — OK version correctness (entry-date resolution) **← primary focus**
- [ ] P5 — Integration isolation and delivery guarantees
- [ ] P6 — Payroll integration correctness (traceability chain) **← primary focus**
- [ ] P7 — Security and access control **← primary focus**
- [ ] P8 — CI/CD enforcement
- [ ] P9 — Usability and UX

## Task Log

### TASK-1801 — OK-version resolution at write and payroll boundaries

| Field | Value |
|-------|-------|
| **ID** | TASK-1801 |
| **Status** | planned |
| **Agent** | Rule Engine + API Integration |
| **Components** | Backend API (TimeEndpoints, contracts), Payroll Integration (PeriodCalculationService), Rule Engine (OkVersionResolver usage) |
| **KB Refs** | ADR-003 (OK version resolved by entry date), ADR-013 (retroactive corrections) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | required (P2, P4 — MANDATORY trigger) |
| **External Review (Codex)** | required (high-risk: legal rule logic) |
| **Orchestrator Approved** | no |

**Description**: Codex BLOCKER #4. The `OkVersionResolver` exists but production write and payroll paths trust caller-supplied `OkVersion` from `RegisterTimeEntryRequest`/`RegisterAbsenceRequest`/`EmploymentProfile` instead of server-resolving from the entry date. This violates P4 (OK version correctness — entry-date resolution). Fix: in write endpoints and payroll period calculation, resolve `OkVersion` from entry/absence date via `OkVersionResolver` and override or reject caller-supplied mismatches. Retroactive split (`OkTransitionDate`/`PreviousOkVersion`) continues to work when explicitly supplied.

**Validation Criteria**:
- [ ] `TimeEndpoints` registration paths ignore caller `OkVersion` and resolve from entry date
- [ ] `RegisterAbsenceRequest` handling resolves OK version from date range
- [ ] `PeriodCalculationService` uses resolver, not `profile.OkVersion`
- [ ] Mismatch handling decision documented (override silently with audit / reject 400 / log warning) and consistent across endpoints
- [ ] `OkVersionResolver` behavior on transition-day boundary unchanged

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs` — resolve OkVersion from date
- `src/Backend/StatsTid.Backend.Api/Contracts/RegisterTimeEntryRequest.cs` — OkVersion becomes optional/ignored
- `src/Backend/StatsTid.Backend.Api/Contracts/RegisterAbsenceRequest.cs` — same
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` — resolve per-entry
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — if payroll endpoint takes OkVersion as input

---

### TASK-1802 — Wage-type mapping lookup fix

| Field | Value |
|-------|-------|
| **ID** | TASK-1802 |
| **Status** | planned |
| **Agent** | Payroll Integration + Data Model |
| **Components** | PostgreSQL schema (`wage_type_mappings`), Payroll Integration (PayrollMappingService, PayrollExportService, PeriodCalculationService) |
| **KB Refs** | PAT-005 (period calculation HTTP), ADR-014 (DB-backed configs) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | required (P6 — OPTIONAL but Orchestrator discretion = YES) |
| **External Review (Codex)** | required (high-risk: payroll export) |
| **Orchestrator Approved** | no |

**Description**: Codex BLOCKER #6. `docker/postgres/init.sql:74-79` defines `wage_type_mappings.position` as `NOT NULL DEFAULT ''`, but `PayrollMappingService.cs:30-59,88-108` looks up generic rows with `position IS NULL`. Generic mappings are therefore invisible at runtime. Compounding this, payroll code paths pass `position: null` everywhere (`Program.cs:39-46,50-63`, `PeriodCalculationService.cs:154-177`), so position-aware precedence is never exercised. Fix: standardize on empty-string convention (update query to `position = ''` for generic lookup, or add both branches), and pass `profile.Position` through the payroll chain where available.

**Validation Criteria**:
- [ ] Generic wage type mappings resolve at runtime (integration test proves it)
- [ ] Position-aware precedence is exercised: when a position-specific mapping exists, it wins over generic
- [ ] Schema and query semantics are consistent (document which convention was chosen)
- [ ] Payroll code paths pass `profile.Position` instead of `null` where applicable
- [ ] Regression tests cover: generic-only mapping, position-specific mapping, both-present precedence

**Files Changed**:
- `docker/postgres/init.sql` (only if changing schema convention)
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollMappingService.cs` — lookup query
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` — position pass-through
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — position pass-through at endpoint boundary
- `tests/StatsTid.Tests.Regression/RegressionTests.cs` — new generic/position precedence tests

---

### TASK-1803 — Role-scope orchestrator / payroll / recalculate endpoints

| Field | Value |
|-------|-------|
| **ID** | TASK-1803 |
| **Status** | planned |
| **Agent** | Security |
| **Components** | Infrastructure/Security (AuthorizationPolicies), Orchestrator (Program), Payroll (Program) |
| **KB Refs** | ADR-007 (JWT/RBAC), ADR-009 (scope-embedded JWT), FAIL-001 (claim remapping) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | required (P7 — Security mandatory) |
| **External Review (Codex)** | required (high-risk: JWT/auth) |
| **Orchestrator Approved** | no |

**Description**: Codex BLOCKER #7. Sensitive orchestrator and payroll endpoints — including retroactive recalculate and payroll export — are guarded only by the `"Authenticated"` policy, allowing any valid employee token to trigger them. Fix: apply role-specific policies (`GlobalAdmin` or `LocalAdmin` depending on scope) to `recalculate`, payroll export, and orchestrator execution endpoints. Also address the dev-signing-key fallback when `JwtSettings:SigningKey` is absent — either fail fast in non-Development environments, or gate the fallback behind an explicit `ASPNETCORE_ENVIRONMENT=Development` check.

**Validation Criteria**:
- [ ] `recalculate` endpoint rejects employee tokens (admin-only)
- [ ] Payroll export endpoints require admin scope
- [ ] Orchestrator `/execute` endpoint is not reachable with an employee token
- [ ] Dev signing-key fallback cannot activate in Production environment
- [ ] Unit tests cover: employee rejected, admin accepted, missing scope rejected

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/Security/AuthorizationPolicies.cs` — new role-specific policies if needed
- `src/Orchestrator/StatsTid.Orchestrator/Program.cs` — attach role policy to `/execute`
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — attach role policies to payroll endpoints
- `src/Infrastructure/StatsTid.Infrastructure/Security/JwtValidationSetup.cs` — harden dev fallback
- `tests/StatsTid.Tests.Unit/Security/*.cs` — new authorization tests

---

### TASK-1804 — EventSerializer coverage test + register UserUpdated

| Field | Value |
|-------|-------|
| **ID** | TASK-1804 |
| **Status** | planned |
| **Agent** | Test & QA + Data Model |
| **Components** | Infrastructure (EventSerializer), Tests |
| **KB Refs** | DEP-003 (EventSerializer must register all types), ADR-005 (explicit type map polymorphic serialization) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | required (P3 — mandatory) |
| **External Review (Codex)** | not required (data model + test coverage; not a high-risk category) |
| **Orchestrator Approved** | no |

**Description**: Codex WARNING on dim. 3. `UserUpdated` is a `DomainEventBase` descendant appended by `AdminEndpoints.cs:389-400` but is missing from `EventSerializer._eventTypeMap`, so replay/deserialization of `user-*` streams can fail. Fix: (1) register `UserUpdated` in the type map; (2) add a reflection-based test that scans `DomainEventBase` descendants in the SharedKernel assembly and fails if any are missing from the serializer map. This prevents future DEP-003 violations from slipping through.

**Validation Criteria**:
- [ ] `UserUpdated` is registered in `EventSerializer._eventTypeMap`
- [ ] New reflection test enumerates all concrete `DomainEventBase` descendants and asserts each is in the map
- [ ] Test fails (as expected) if a new event is added without registration
- [ ] Round-trip serialization test for `UserUpdated`

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` — add UserUpdated entry
- `tests/StatsTid.Tests.Unit/Events/EventSerializerCoverageTests.cs` — new reflection test (or add to existing DomainEventBaseActorTests file)
- `tests/StatsTid.Tests.Unit/Events/DomainEventBaseActorTests.cs` — possible extension

---

### TASK-1805 — OK-version runtime regression tests

| Field | Value |
|-------|-------|
| **ID** | TASK-1805 |
| **Status** | planned |
| **Agent** | Test & QA |
| **Components** | Tests/Regression |
| **KB Refs** | ADR-003 (OK version resolved by entry date), ADR-013 (retroactive corrections, no cascade) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | not required (test-only; Reviewer may still be spawned at Orchestrator discretion if complexity warrants) |
| **External Review (Codex)** | not required (test-only) |
| **Orchestrator Approved** | no |

**Description**: Codex Rec #8. Existing tests cover the `OkVersionResolver` utility in isolation. Add regression tests that exercise the full runtime paths fixed by TASK-1801: backend write registration with entries that straddle the OK24→OK26 transition, weekly calculation retrieval, payroll split/replay. These tests should fail before TASK-1801 is applied and pass after, proving the fix.

**Validation Criteria**:
- [ ] Test: registering a time entry with date before transition resolves to OK24 regardless of caller-supplied value
- [ ] Test: registering an entry after transition resolves to OK26
- [ ] Test: absence spanning the transition date triggers split behavior correctly
- [ ] Test: weekly calculation retrieves per-entry OK version consistent with entry date
- [ ] Test: payroll export for a retroactive correction respects `OkTransitionDate`
- [ ] Regression suite passes

**Files Changed**:
- `tests/StatsTid.Tests.Regression/RegressionTests.cs` — add OK-version runtime tests

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | _No rule logic changes planned — verify via regression suite_ |
| Wage type mappings produce correct SLS codes | pending | _TASK-1802 directly targets this_ |
| Overtime/supplement calculations are deterministic | pending | _Not affected by S18 changes — verify via regression suite_ |
| Absence effects on norm/flex/pension are correct | pending | _Verify via regression suite_ |
| Retroactive recalculation produces stable results | pending | _TASK-1801 and TASK-1805 touch this directly_ |

## External Review (Step 7a)

_Codex sprint-end review against the sprint-start commit. See [AGENTS.md](../AGENTS.md) External Review section._

| Field | Value |
|-------|-------|
| **Invoked** | pending |
| **Sprint-start commit** | `d1b04c8` (governance update adding Codex review workflow) |
| **Command** | `codex review --base d1b04c8 "..."` |
| **Review Cycles** | — |
| **Findings** | — |
| **Resolution** | — |

### Findings

_To be recorded after sprint-end review completes._

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | — | pending |
| Regression tests | — | pending |
| Smoke tests | — | pending (requires Docker) |
| **Total** | — | — |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 5 |
| Constraint Violations | — |
| Reviewer Findings | — |
| External Review Cycles | — |
| External Findings | — |
| Re-dispatches | — |
| First-Pass Rate | — |

## Sprint Retrospective

**What went well**: _To be filled at sprint end._

**What to improve**: _To be filled at sprint end._

**Knowledge produced**: _List any new KB entries (ADR/PAT/DEP/RES/FAIL) created during this sprint._
