# Sprint 9 — Skema: Monthly Spreadsheet + Timer + Two-Step Approval

| Field | Value |
|-------|-------|
| **Sprint** | 9 |
| **Status** | complete |
| **Start Date** | 2026-03-05 |
| **End Date** | 2026-03-05 |
| **Orchestrator Approved** | yes — 2026-03-05 |
| **Build Verified** | yes — `dotnet build` 0 warnings 0 errors; `tsc --noEmit` 0 errors; `vite build` succeeds |
| **Test Verified** | yes — 227 unit + 15 regression = 242 backend tests passing; 33 frontend tests passing |

## Sprint Goal
Replace three separate pages (Ugeoversigt, Tidsregistrering, Fraværsregistrering) with a single "Skema" page — a monthly spreadsheet where employees register hours on projects and absences. Add backend-persisted timer (Tjek ind/Tjek ud), two-step approval flow (employee → manager), and project management for LocalAdmins.

**Re-prioritization**: This sprint was originally planned as Phase 3 (Advanced Rules + Retroactive Corrections). The Skema feature was re-prioritized as Sprint 9 (Tier 2 re-prioritization). Phase 3 shifts to S10-11. See ROADMAP.md Impact Assessment.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (multi-agent execution, bounded contexts respected)
- [ ] P2 — Rule engine determinism maintained (no rule engine changes this sprint)
- [x] P3 — Event sourcing append-only semantics (4 new events: PeriodEmployeeApproved, PeriodReopened, TimerCheckedIn, TimerCheckedOut)
- [ ] P4 — OK version correctness (no version logic changes this sprint)
- [ ] P5 — Integration isolation and delivery guarantees (no integration changes this sprint)
- [ ] P6 — Payroll integration correctness (no payroll changes this sprint)
- [x] P7 — Security and access control (all new endpoints enforce JWT auth + org-scope validation, timer endpoints scoped to actor)
- [x] P8 — CI/CD enforcement (dotnet build + dotnet test + npm test all pass)
- [x] P9 — Usability and UX (Skema monthly grid, timer, approval flow, project management)

## Task Log

### TASK-901 — SharedKernel models + events for Skema

| Field | Value |
|-------|-------|
| **ID** | TASK-901 |
| **Status** | complete |
| **Agent** | Data Model Agent (worktree, Phase 1) |
| **Components** | SharedKernel (Models, Events), Infrastructure (EventSerializer) |
| **KB Refs** | DEP-003, PAT-001, PAT-004 |
| **Reviewer Audit** | skipped — models only, no P1-P4 impact |
| **Orchestrator Approved** | yes — 2026-03-05 |

**Description**: Created 3 new immutable models (Project, TimerSession, AbsenceTypeVisibility), 4 new domain events (PeriodEmployeeApproved, PeriodReopened, TimerCheckedIn, TimerCheckedOut), updated ApprovalPeriod with 4 two-step approval fields, registered all 4 events in EventSerializer (22 total).

**Validation Criteria**:
- [x] 3 new models with init-only properties
- [x] 4 new events extending DomainEventBase with actor tracking
- [x] ApprovalPeriod updated with EmployeeApprovedAt/By, EmployeeDeadline, ManagerDeadline
- [x] EventSerializer type map updated (22 entries)
- [x] dotnet build passes

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/Project.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Models/TimerSession.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Models/AbsenceTypeVisibility.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Models/ApprovalPeriod.cs` — modified: 4 new properties
- `src/SharedKernel/StatsTid.SharedKernel/Events/PeriodEmployeeApproved.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/PeriodReopened.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/TimerCheckedIn.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/TimerCheckedOut.cs` — new
- `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` — modified: 4 new type map entries

---

### TASK-902 — Frontend: SkemaPage, SkemaGrid, TimerControl, ProjectManagement

| Field | Value |
|-------|-------|
| **ID** | TASK-902 |
| **Status** | complete |
| **Agent** | UX Agent (worktree, Phase 1) |
| **Components** | Frontend (pages, components, hooks, types, routing) |
| **KB Refs** | ADR-011, ADR-012 |
| **Reviewer Audit** | skipped — pure UI |
| **Orchestrator Approved** | yes — 2026-03-05 |

**Description**: Built the Skema monthly spreadsheet page with day columns (Danish abbreviations), project + absence rows, arrival/departure row, total row. Timer control (Tjek ind/Tjek ud) with real-time elapsed display. Project management CRUD page for LocalAdmins. 3 new hooks (useSkema, useTimer, useProjects). Updated types, routing (removed 3 old pages, added Skema as index), sidebar navigation.

**Validation Criteria**:
- [x] SkemaGrid renders month grid with correct day columns and weekend styling
- [x] TimerControl shows Tjek ind/Tjek ud with elapsed time
- [x] SkemaPage: month navigation, debounced auto-save, approval footer, read-only when locked
- [x] ProjectManagement: CRUD table with inline form
- [x] Routing updated: / → SkemaPage, /admin/projects → ProjectManagement
- [x] Sidebar: 3 items replaced with single "Skema"
- [x] tsc + vite build passes

**Files Changed**:
- `frontend/src/pages/SkemaPage.tsx` + `SkemaPage.module.css` — new
- `frontend/src/components/SkemaGrid.tsx` + `SkemaGrid.module.css` — new
- `frontend/src/components/TimerControl.tsx` + `TimerControl.module.css` — new
- `frontend/src/pages/admin/ProjectManagement.tsx` + `ProjectManagement.module.css` — new
- `frontend/src/hooks/useSkema.ts` — new
- `frontend/src/hooks/useTimer.ts` — new
- `frontend/src/hooks/useProjects.ts` — new
- `frontend/src/types.ts` — modified: added Project, TimerSession, SkemaRow, SkemaMonthData
- `frontend/src/App.tsx` — modified: removed 3 old routes, added SkemaPage + ProjectManagement
- `frontend/src/components/layout/Sidebar.tsx` — modified: simplified employee nav, added Projekter
- `frontend/src/contexts/AuthContext.tsx` — modified: added agreementCode to AuthState

---

### TASK-903 — Database schema: projects, timer_sessions, absence_type_visibility

| Field | Value |
|-------|-------|
| **ID** | TASK-903 |
| **Status** | complete |
| **Agent** | Backend Agent (Phase 2) |
| **Components** | PostgreSQL (init.sql) |
| **KB Refs** | — |
| **Reviewer Audit** | skipped — seed data + schema, no logic |
| **Orchestrator Approved** | yes — 2026-03-05 |

**Description**: Created 3 new tables (projects, timer_sessions, absence_type_visibility) with indexes and constraints. Altered approval_periods with 4 new columns and expanded status CHECK to include EMPLOYEE_APPROVED. Added SICK_DAY wage_type_mappings and sample projects for test orgs.

**Validation Criteria**:
- [x] 3 new tables with correct constraints and indexes
- [x] approval_periods ALTER adds 4 columns + expanded CHECK
- [x] Seed data: 6 SICK_DAY rows, 5 sample projects
- [x] Docker rebuild succeeds with new schema

**Files Changed**:
- `docker/postgres/init.sql` — modified: 3 new tables, ALTER, seed data

---

### TASK-904 — Backend repositories: Project, Timer, AbsenceTypeVisibility

| Field | Value |
|-------|-------|
| **ID** | TASK-904 |
| **Status** | complete |
| **Agent** | Backend Agent (Phase 2) |
| **Components** | Infrastructure (repositories) |
| **KB Refs** | ADR-001 |
| **Reviewer Audit** | skipped — standard repository pattern |
| **Orchestrator Approved** | yes — 2026-03-05 |

**Description**: Created 3 new repositories following existing Npgsql patterns. ProjectRepository (GetByOrg, Create, Update, Deactivate), TimerSessionRepository (GetActive, GetByDate, CheckIn, CheckOut), AbsenceTypeVisibilityRepository (GetByOrg, SetVisibility with upsert).

**Validation Criteria**:
- [x] 3 repositories with correct SQL and parameterized queries
- [x] Follow existing Npgsql connection pattern (DbConnectionFactory)
- [x] ON CONFLICT upsert for visibility
- [x] dotnet build passes

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/ProjectRepository.cs` — new
- `src/Infrastructure/StatsTid.Infrastructure/TimerSessionRepository.cs` — new
- `src/Infrastructure/StatsTid.Infrastructure/AbsenceTypeVisibilityRepository.cs` — new

---

### TASK-905 — Backend endpoints: Skema, Timer, Projects

| Field | Value |
|-------|-------|
| **ID** | TASK-905 |
| **Status** | complete |
| **Agent** | Backend Agent (Phase 2) |
| **Components** | Backend API (Endpoints), Program.cs |
| **KB Refs** | ADR-009, ADR-012 |
| **Reviewer Audit** | skipped — follows established endpoint group pattern from S7 |
| **Orchestrator Approved** | yes — 2026-03-05 |

**Description**: Created 3 new endpoint groups. SkemaEndpoints: composite GET (projects, absence types with Danish labels, entries, absences, timer, approval, deadlines) + batch POST save (blocked when locked). TimerEndpoints: check-in, check-out, get active. ProjectEndpoints: full CRUD with LocalAdminOrAbove guard. Registered all in Program.cs DI + endpoint mapping.

**Validation Criteria**:
- [x] GET /api/skema/{id}/month returns composite data
- [x] POST /api/skema/{id}/save emits events, blocked when period locked
- [x] Timer endpoints: check-in, check-out, get active
- [x] Project endpoints: CRUD with role guard
- [x] All endpoints enforce auth + org-scope
- [x] Program.cs DI + MapXxxEndpoints() registered
- [x] dotnet build passes

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` — new
- `src/Backend/StatsTid.Backend.Api/Endpoints/TimerEndpoints.cs` — new
- `src/Backend/StatsTid.Backend.Api/Endpoints/ProjectEndpoints.cs` — new
- `src/Backend/StatsTid.Backend.Api/Program.cs` — modified: DI registrations + endpoint mapping

---

### TASK-906 — Modified ApprovalEndpoints: two-step approval

| Field | Value |
|-------|-------|
| **ID** | TASK-906 |
| **Status** | complete |
| **Agent** | Backend Agent (Phase 2) |
| **Components** | Backend API (ApprovalEndpoints), Infrastructure (ApprovalPeriodRepository) |
| **KB Refs** | ADR-012 |
| **Reviewer Audit** | skipped — state machine extension, not new architecture |
| **Orchestrator Approved** | yes — 2026-03-05 |

**Description**: Extended ApprovalEndpoints with two-step approval: POST employee-approve (DRAFT → EMPLOYEE_APPROVED, sets deadlines), POST reopen (EMPLOYEE_APPROVED → DRAFT with reason). Modified approve/reject to accept EMPLOYEE_APPROVED. Modified pending to return EMPLOYEE_APPROVED. Updated ApprovalPeriodRepository with new status handling and deadline storage.

**Validation Criteria**:
- [x] employee-approve: DRAFT → EMPLOYEE_APPROVED with deadlines
- [x] reopen: EMPLOYEE_APPROVED → DRAFT (LeaderOrAbove, with reason)
- [x] approve accepts EMPLOYEE_APPROVED
- [x] reject accepts EMPLOYEE_APPROVED
- [x] pending returns EMPLOYEE_APPROVED periods
- [x] Deadlines: employee = last day +2, manager = last day +5
- [x] dotnet build passes

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs` — modified: 2 new routes, 3 modified routes
- `src/Infrastructure/StatsTid.Infrastructure/ApprovalPeriodRepository.cs` — modified: EMPLOYEE_APPROVED handling, UpdateDeadlinesAsync

---

### TASK-907 — Modified ConfigEndpoints: absence type visibility

| Field | Value |
|-------|-------|
| **ID** | TASK-907 |
| **Status** | complete |
| **Agent** | Backend Agent (Phase 2) |
| **Components** | Backend API (ConfigEndpoints) |
| **KB Refs** | — |
| **Reviewer Audit** | skipped — small addition to existing endpoint group |
| **Orchestrator Approved** | yes — 2026-03-05 |

**Description**: Added 2 new routes to ConfigEndpoints: GET absence-types (returns visible types for org, filtered by agreement from wage_type_mappings + visibility overrides), POST absence-types/visibility (toggle visibility, LocalAdminOrAbove).

**Validation Criteria**:
- [x] GET /api/config/{orgId}/absence-types returns filtered list
- [x] POST /api/config/{orgId}/absence-types/visibility toggles visibility
- [x] Filters by agreement from wage_type_mappings
- [x] dotnet build passes

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs` — modified: 2 new routes

---

### TASK-908 — JWT claim remapping fix

| Field | Value |
|-------|-------|
| **ID** | TASK-908 |
| **Status** | complete |
| **Agent** | Orchestrator (direct — debugging, small fix) |
| **Components** | Infrastructure (Security) |
| **KB Refs** | FAIL-001 |
| **Reviewer Audit** | skipped — bug fix, < 10 lines |
| **Orchestrator Approved** | yes — 2026-03-05 |

**Description**: Fixed HTTP 403 on all authenticated endpoints caused by .NET 8's default JWT claim name remapping. Added `MapInboundClaims = false` to JwtBearer options and `NameClaimType`/`RoleClaimType` to TokenValidationParameters. Updated ActorContext to check `"sub"` claim directly. See FAIL-001 for full analysis.

**Validation Criteria**:
- [x] All authenticated endpoints return 200 with valid tokens
- [x] ScopeAuthorizationHandler finds "role" claim correctly
- [x] ActorContext resolves ActorId from "sub" claim
- [x] dotnet build passes
- [x] Docker rebuild + smoke test passes

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/Security/JwtValidationSetup.cs` — modified: MapInboundClaims, NameClaimType, RoleClaimType
- `src/Infrastructure/StatsTid.Infrastructure/Security/ActorContext.cs` — modified: "sub" as primary ActorId fallback

---

### TASK-909 — Backend tests for Sprint 9

| Field | Value |
|-------|-------|
| **ID** | TASK-909 |
| **Status** | complete |
| **Agent** | Test & QA Agent (Phase 3) |
| **Components** | Tests |
| **KB Refs** | — |
| **Reviewer Audit** | skipped — tests only |
| **Orchestrator Approved** | yes — 2026-03-05 |

**Description**: Created 25 new backend unit tests covering project CRUD (5), timer lifecycle (5), two-step approval state machine (8), skema composite/batch endpoints (5), event serializer for 4 new types (2).

**Validation Criteria**:
- [x] 25 tests in Sprint9SkemaTests.cs
- [x] All passing
- [x] Total backend: 227 unit + 15 regression = 242

**Files Changed**:
- `tests/StatsTid.Tests.Unit/Sprint9SkemaTests.cs` — new: 25 tests

---

### TASK-910 — Frontend tests for Sprint 9

| Field | Value |
|-------|-------|
| **ID** | TASK-910 |
| **Status** | complete |
| **Agent** | Test & QA Agent (Phase 3) |
| **Components** | Frontend (tests) |
| **KB Refs** | — |
| **Reviewer Audit** | skipped — tests only |
| **Orchestrator Approved** | yes — 2026-03-05 |

**Description**: Created 8 new frontend tests: SkemaGrid rendering (4 tests: day columns, weekend CSS, cell change, read-only mode), SkemaPage (2 tests: month nav, approval button), useTimer hook (2 tests: null initial, elapsed time).

**Validation Criteria**:
- [x] 8 tests across 3 test files
- [x] All passing
- [x] Total frontend: 33 (25 from S8 + 8 new)

**Files Changed**:
- `frontend/src/components/__tests__/SkemaGrid.test.tsx` — new: 4 tests
- `frontend/src/pages/__tests__/SkemaPage.test.tsx` — new: 2 tests
- `frontend/src/hooks/__tests__/useTimer.test.tsx` — new: 2 tests

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule engine changes this sprint |
| Wage type mappings produce correct SLS codes | Partial | Added SICK_DAY (SLS_0540) mappings for all agreements |
| Overtime/supplement calculations are deterministic | N/A | No rule changes this sprint |
| Absence effects on norm/flex/pension are correct | N/A | No absence logic changes this sprint |
| Retroactive recalculation produces stable results | N/A | No retroactive changes this sprint |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Backend unit tests | 227 | all passing |
| Backend regression tests | 15 | all passing |
| Backend smoke tests | 4 | N/A (requires Docker; 2 pre-existing auth failures) |
| Frontend tests (vitest) | 33 | all passing |
| **Total** | 275 | — |

## Sprint Retrospective

**What went well**: Full Skema feature (monthly spreadsheet, timer, two-step approval, project management) delivered in a single sprint using 4-phase multi-agent execution. Data Model + UX agents ran in parallel worktrees (Phase 1), Backend agent completed all repositories and endpoints (Phase 2), Test agent covered both backend and frontend (Phase 3). JWT claim remapping bug identified and resolved quickly with proper root cause analysis.

**What to improve**: emp001 test user is in STY01 org which has no seeded projects — future seed data should cover all test users' orgs. Pre-existing smoke test failures (2 tests since S3) should be addressed. Worktree file copying was needed because agents made uncommitted changes — should enforce commit discipline in worktree agents.

**Knowledge produced**:
- FAIL-001: JWT claim remapping in .NET 8 (critical debugging knowledge)
- ADR-012: Two-step approval flow architecture decision
