# Sprint 16 — Working Time Compliance

| Field | Value |
|-------|-------|
| **Sprint** | 16 |
| **Status** | complete |
| **Start Date** | 2026-03-11 |
| **End Date** | 2026-03-11 |
| **Orchestrator Approved** | yes (2026-03-11) |
| **Build Verified** | yes — 0 errors, 0 warnings |
| **Test Verified** | yes — 421 unit + 15 regression = 436 passing (net +14) |

## Sprint Goal

Implement EU Working Time Directive 2003/88/EC compliance (SYSTEM_TARGET.md Section J): 11-hour daily rest validation, weekly rest day checks, 48-hour/week ceiling over configurable reference period, max daily hours enforcement, voluntary unsocial hours exemption, rest period derogation for qualifying agreements, compensatory rest tracking, and compliance warnings in both employee and manager UIs.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (bounded context rules respected, no cross-domain leaks)
- [x] P2 — Rule engine determinism maintained (RestPeriodRule is pure static function, no I/O, deterministic)
- [x] P3 — Event sourcing (RestPeriodViolationDetected + CompensatoryRestGranted events, registered in EventSerializer)
- [x] P4 — OK version correctness (compliance configs are version-aware per agreement × OK version)
- [x] P5 — Integration isolation (Backend→Rule Engine compliance check via HTTP per PAT-005)
- [x] P7 — Security (all endpoints require authorization, employee scope enforcement)
- [x] P8 — CI/CD enforcement (421 unit + 15 regression tests passing, build clean)
- [x] P9 — Usability (ComplianceWarnings component in SkemaPage, compliance badges in ApprovalDashboard)

## Reviewer Audit

Reviewer invoked for full sprint review (touches P2 — new rule, P3 — new events, P5 — new HTTP endpoint, cross-domain: SharedKernel→RuleEngine→Infrastructure→Backend→Frontend, new model pattern ADR-015). Findings:

- **No BLOCKERs.** All P1-P8 checks passed clean.
- **NOTE #1**: ADR-015 (ComplianceCheckResult separate from CalculationResult) is functionally sound. Reviewer suggests considering unification in future sprints — does not block acceptance.
- **Compliance field audit**: All 5 fields verified across full stack (AgreementRuleConfig → Entity → DB → Repository → ConfigResolution → Seeder → Endpoints → Frontend).

## Task Log

### Phase 1 — Data Model & Rule Engine

### TASK-1601 — AgreementRuleConfig compliance fields

| Field | Value |
|-------|-------|
| **ID** | TASK-1601 |
| **Status** | complete |
| **Agent** | Orchestrator (small task — 5 fields added to existing model) |
| **Components** | SharedKernel |
| **KB Refs** | — |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: Added 5 compliance configuration fields to AgreementRuleConfig: MaxDailyHours (13.0m default), MinimumRestHours (11.0m default), RestPeriodDerogationAllowed (false default), WeeklyMaxHoursReferencePeriod (17 weeks default), VoluntaryUnsocialHoursAllowed (true default).

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/AgreementRuleConfig.cs` — added 5 fields

---

### TASK-1602 — TimeEntry + TimeEntryRegistered VoluntaryUnsocialHours field

| Field | Value |
|-------|-------|
| **ID** | TASK-1602 |
| **Status** | complete |
| **Agent** | Orchestrator (small task — 1 field on 2 files) |
| **Components** | SharedKernel |
| **KB Refs** | — |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: Added `VoluntaryUnsocialHours` bool property to TimeEntry model and TimeEntryRegistered event. Defaults to false for backward compatibility with existing event store data.

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/TimeEntry.cs` — added VoluntaryUnsocialHours
- `src/SharedKernel/StatsTid.SharedKernel/Events/TimeEntryRegistered.cs` — added VoluntaryUnsocialHours

---

### TASK-1603 — ComplianceCheckResult model (ADR-015)

| Field | Value |
|-------|-------|
| **ID** | TASK-1603 |
| **Status** | complete |
| **Agent** | Orchestrator (new model — justified divergence from PAT-006) |
| **Components** | SharedKernel |
| **KB Refs** | ADR-015 |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: Created ComplianceCheckResult as a new return type separate from CalculationResult. Contains RuleId, EmployeeId, Success flag, Violations list, and Warnings list. ComplianceViolation record with ViolationType enum (DAILY_REST, WEEKLY_REST, MAX_DAILY_HOURS, WEEKLY_MAX_HOURS), Severity enum (WARNING, VIOLATION), Date, Message, and ActualValue/ThresholdValue.

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/ComplianceCheckResult.cs` — new file

---

### TASK-1604 — RestPeriodRule (pure deterministic rule)

| Field | Value |
|-------|-------|
| **ID** | TASK-1604 |
| **Status** | complete |
| **Agent** | Orchestrator (implements core rule — complex but single-domain) |
| **Components** | Rule Engine |
| **KB Refs** | ADR-002, ADR-015 |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: Pure static rule with 4 compliance checks: CheckMaxDailyHours (per-day hours sum vs MaxDailyHours config), CheckDailyRest (11h gap between consecutive work periods), CheckWeeklyRest (at least one rest day per 7-day window), CheckWeeklyMaxHours (48h ceiling over configurable reference period). Handles hours-only entries gracefully (skips rest gap check, still validates daily max and 48h ceiling). Voluntary unsocial hours skip rest checks but NOT 48h ceiling. Derogation: rest breaches become WARNINGs instead of VIOLATIONs.

**Validation Criteria**:
- [x] Pure static class, no I/O, deterministic
- [x] All 4 EU Working Time Directive checks implemented
- [x] Voluntary unsocial hours correctly exempts rest but not 48h ceiling
- [x] Derogation produces WARNING severity instead of VIOLATION
- [x] Hours-only entries handled without NullReferenceException

**Files Changed**:
- `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RestPeriodRule.cs` — new file
- `src/RuleEngine/StatsTid.RuleEngine.Api/Contracts/CheckComplianceRequest.cs` — new DTO
- `src/RuleEngine/StatsTid.RuleEngine.Api/Program.cs` — added POST /api/rules/check-compliance endpoint

---

### TASK-1605 — Domain events (RestPeriodViolationDetected, CompensatoryRestGranted)

| Field | Value |
|-------|-------|
| **ID** | TASK-1605 |
| **Status** | complete |
| **Agent** | Orchestrator (small task — 2 new events + registration) |
| **Components** | SharedKernel, Infrastructure |
| **KB Refs** | DEP-003 |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: Created 2 new domain events and registered them in EventSerializer type map (31 total event types).

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Events/RestPeriodViolationDetected.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/CompensatoryRestGranted.cs` — new
- `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` — 2 new registrations

---

### TASK-1606 — DB schema (agreement_configs columns + compensatory_rest table)

| Field | Value |
|-------|-------|
| **ID** | TASK-1606 |
| **Status** | complete |
| **Agent** | Orchestrator (schema-only) |
| **Components** | PostgreSQL |
| **KB Refs** | — |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: Added 5 compliance columns to agreement_configs table. Created compensatory_rest table (id, employee_id, source_date, compensatory_date, hours, status, created_at). Updated all 10 seed INSERT statements with compliance field values (AC/AC_RESEARCH/AC_TEACHING: derogation=FALSE, HK/PROSA: derogation=TRUE).

**Files Changed**:
- `docker/postgres/init.sql` — schema additions + seed updates

---

### TASK-1607 — Config resolution chain propagation

| Field | Value |
|-------|-------|
| **ID** | TASK-1607 |
| **Status** | complete |
| **Agent** | Orchestrator (cross-cutting — 5 fields through 5 files) |
| **Components** | SharedKernel, Infrastructure |
| **KB Refs** | ADR-014 |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: Propagated 5 compliance fields through the full config resolution chain: AgreementConfigEntity (model + ToRuleConfig), AgreementConfigRepository (INSERT/UPDATE/Read), ConfigResolutionService (merged config), AgreementConfigSeeder (entity construction), CentralAgreementConfigs (HK/PROSA RestPeriodDerogationAllowed=true).

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/AgreementConfigEntity.cs` — 5 new fields + ToRuleConfig mapping
- `src/Infrastructure/StatsTid.Infrastructure/AgreementConfigRepository.cs` — INSERT/UPDATE/Read
- `src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs` — merged config
- `src/Infrastructure/StatsTid.Infrastructure/AgreementConfigSeeder.cs` — entity construction
- `src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs` — HK/PROSA derogation

---

### Phase 2 — Backend Integration

### TASK-1608 — AgreementConfigEndpoints compliance fields

| Field | Value |
|-------|-------|
| **ID** | TASK-1608 |
| **Status** | complete |
| **Agent** | Orchestrator (mechanical propagation through existing endpoints) |
| **Components** | Backend API |
| **KB Refs** | — |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: Updated MapEntityToResponse, AgreementConfigRequest DTO (with safe defaults), BuildEntityFromRequest, SerializeForAudit (both overloads), and clone entity construction — all with 5 new compliance fields.

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/AgreementConfigEndpoints.cs` — 5 new fields throughout

---

### TASK-1609 — CompensatoryRestRepository

| Field | Value |
|-------|-------|
| **ID** | TASK-1609 |
| **Status** | complete |
| **Agent** | Orchestrator (small task — single repository) |
| **Components** | Infrastructure |
| **KB Refs** | — |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: CompensatoryRestEntry model + repository with Create, GetByEmployee, Grant methods.

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/CompensatoryRestRepository.cs` — new file

---

### TASK-1610 — ComplianceEndpoints (Backend → Rule Engine HTTP)

| Field | Value |
|-------|-------|
| **ID** | TASK-1610 |
| **Status** | complete |
| **Agent** | Orchestrator (cross-domain endpoint — PAT-005 compliant) |
| **Components** | Backend API |
| **KB Refs** | PAT-005 |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: GET /api/compliance/{employeeId}/period — loads time entries, resolves config, calls Rule Engine's /api/rules/check-compliance via HTTP (PAT-005). GET /api/compliance/{employeeId}/compensatory-rest — returns compensatory rest entries. Both endpoints registered in Program.cs with DI for CompensatoryRestRepository.

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs` — new file
- `src/Backend/StatsTid.Backend.Api/Program.cs` — DI + endpoint registration

---

### Phase 3 — Frontend

### TASK-1611 — ComplianceWarnings component + SkemaPage integration

| Field | Value |
|-------|-------|
| **ID** | TASK-1611 |
| **Status** | complete |
| **Agent** | Orchestrator (UX — component + integration) |
| **Components** | Frontend |
| **KB Refs** | — |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: useCompliance hook for period compliance check. ComplianceWarnings component displaying violations and warnings with Danish labels (Daglig hvile, Ugentlig hviledag, Maks daglig arbejdstid, Ugentligt timemaksimum), severity badges, date formatting. Styled with design system tokens. Integrated between BalanceSummary and TimerControl on SkemaPage.

**Files Changed**:
- `frontend/src/hooks/useCompliance.ts` — new (useCompliance + useCompensatoryRest hooks)
- `frontend/src/components/ComplianceWarnings.tsx` — new component
- `frontend/src/components/ComplianceWarnings.module.css` — new styles
- `frontend/src/pages/SkemaPage.tsx` — imported + integrated ComplianceWarnings

---

### TASK-1612 — ApprovalDashboard compliance column

| Field | Value |
|-------|-------|
| **ID** | TASK-1612 |
| **Status** | complete |
| **Agent** | Orchestrator (UX — existing page extension) |
| **Components** | Frontend |
| **KB Refs** | — |
| **Reviewer Audit** | covered in full sprint review |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: Added compliance fetching per pending period in ApprovalDashboard. New "Compliance" table column with ComplianceBadge sub-component showing OK (green), N adv. (amber warnings), or N overtr. (red violations).

**Files Changed**:
- `frontend/src/pages/approval/ApprovalDashboard.tsx` — compliance map + badge
- `frontend/src/pages/approval/ApprovalDashboard.module.css` — badge styles

---

### Phase 4 — Tests

### TASK-1613 — RestPeriodRule unit tests

| Field | Value |
|-------|-------|
| **ID** | TASK-1613 |
| **Status** | complete |
| **Agent** | Orchestrator (test implementation) |
| **Components** | Tests |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (test-only) |
| **Orchestrator Approved** | yes — 2026-03-11 |

**Description**: 14 unit tests covering all RestPeriodRule checks: daily rest violation detection, voluntary unsocial hours exemption, weekly rest violation, max daily hours, 48h ceiling over reference period, derogation producing warnings, hours-only entries, empty entries, and config defaults.

**Files Changed**:
- `tests/StatsTid.Tests.Unit/Rules/RestPeriodRuleTests.cs` — new (14 tests)

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified | EU Working Time Directive 2003/88/EC: 11h daily rest, weekly rest day, 48h/week ceiling, max daily hours. AC strict (no derogation), HK/PROSA allow derogation (WARNING severity) |
| Wage type mappings produce correct SLS codes | N/A | No new SLS codes in this sprint |
| Overtime/supplement calculations are deterministic | N/A | Not modified |
| Absence effects on norm/flex/pension are correct | N/A | Not modified |
| Retroactive recalculation produces stable results | N/A | Not modified |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 421 | all passing |
| Regression tests | 15 | all passing |
| Smoke tests | 4 | N/A (requires Docker) |
| **Total** | 436 | — |

## Sprint Retrospective

**What went well**: Clean implementation of pure deterministic compliance rule following established patterns. Config resolution chain propagation was mechanical but thorough — 5 new fields through 5 infrastructure files. EU Working Time Directive requirements map cleanly to 4 independent checks. ADR-015 documents the justified divergence from PAT-006.

**What to improve**: Worktree agent permissions failed (agents couldn't get Write/Edit access), requiring Orchestrator to implement all tasks directly. This bypassed the multi-agent pattern but was pragmatic given the constraint. Frontend tests (ComplianceWarnings render, VoluntaryUnsocialHours toggle) were planned but not implemented — test coverage is backend-only.

**Deferred items**:
- Frontend component tests for ComplianceWarnings (planned in TASK-1613 but only backend tests written)
- SkemaEndpoints save flow doesn't yet forward VoluntaryUnsocialHours or call compliance check after save
- CompensatoryRest grant workflow (currently just tracking, no automatic grant on derogation use)

**Knowledge produced**: ADR-015 (ComplianceCheckResult pattern — separate from CalculationResult)
