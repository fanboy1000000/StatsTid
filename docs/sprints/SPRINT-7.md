# Sprint 7 — Local Config, Period Approval, Org-Scope Enforcement

| Field | Value |
|-------|-------|
| **Sprint** | 7 |
| **Status** | complete |
| **Start Date** | 2026-03-04 |
| **End Date** | 2026-03-04 |
| **Orchestrator Approved** | yes — 2026-03-04 |
| **Build Verified** | yes — `dotnet build` 0 warnings 0 errors |
| **Test Verified** | yes — 202 unit + 15 regression = 217 total passing |

## Sprint Goal
Make the RBAC foundation from Sprint 6 operational: enforce org-scope on all API endpoints, add management CRUD for orgs/users/roles, implement the period approval workflow, add local configuration endpoints with central constraint validation, and gate payroll export behind period approval.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (Reviewer BLOCKER: Backend→Payroll reference fixed by moving ConfigResolutionService to Infrastructure)
- [ ] P2 — Rule engine determinism maintained (no rule engine changes this sprint)
- [x] P3 — Event sourcing append-only semantics respected (all mutations emit domain events; Reviewer WARNING: UserUpdated event added)
- [ ] P4 — OK version correctness (no version resolution changes)
- [x] P5 — Integration isolation and delivery guarantees (Payroll service correctly isolated; ConfigResolutionService moved to shared Infrastructure)
- [x] P6 — Payroll integration correctness (approval guard on calculate-and-export; central config constraints enforced)
- [x] P7 — Security and access control (all endpoints authorized; all resource-keyed endpoints enforce org-scope via OrgScopeValidator)
- [x] P8 — CI/CD enforcement (0 warnings, 0 errors, 217 tests pass)
- [ ] P9 — Usability and UX (no frontend changes)

## Task Log

### TASK-701 — OrgScopeValidator service

| Field | Value |
|-------|-------|
| **ID** | TASK-701 |
| **Status** | complete |
| **Agent** | Security & Compliance |
| **Components** | Infrastructure/Security |
| **KB Refs** | ADR-008, ADR-009 |
| **Reviewer Audit** | performed — no findings specific to this task |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Created the OrgScopeValidator service in Infrastructure/Security that provides org-scope enforcement for all API endpoints. Uses OrganizationRepository and UserRepository to resolve target org paths and check actor scopes via RoleScope.CoversOrg(). Employee role: ownership check only. Higher roles: materialized path scope check. GLOBAL scope always allows.

**Validation Criteria**:
- [x] Pure service with no endpoint modifications
- [x] ValidateEmployeeAccessAsync resolves employee → org → materialized path → checks scopes
- [x] ValidateOrgAccessAsync resolves org → checks scopes
- [x] GLOBAL scope allows without org path lookup
- [x] Failed access attempts logged via ILogger

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/Security/OrgScopeValidator.cs` — new file (144 lines)

---

### TASK-702 — Repositories + Backend refactor + seed data

| Field | Value |
|-------|-------|
| **ID** | TASK-702 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Components** | Infrastructure, Backend API, PostgreSQL |
| **KB Refs** | ADR-008, ADR-010 |
| **Reviewer Audit** | performed — BLOCKER on seed data fixed (MaxFlexBalance 120→80) |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Created LocalConfigurationRepository and ApprovalPeriodRepository for Sprint 7 data access. Refactored Backend Program.cs from 372 lines into endpoint group extension methods (AuthEndpoints, TimeEndpoints) to enable parallel agent work. Added seed data for local configurations. Registered OrgScopeValidator and new repositories in DI.

**Validation Criteria**:
- [x] LocalConfigurationRepository: CRUD for local_configurations table
- [x] ApprovalPeriodRepository: CRUD for approval_periods + audit table
- [x] Backend Program.cs slim (DI + wiring only)
- [x] Endpoint groups as extension methods
- [x] Seed data within central constraints (fixed: 80.0 not 120.0)

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/LocalConfigurationRepository.cs` — new file
- `src/Infrastructure/StatsTid.Infrastructure/ApprovalPeriodRepository.cs` — new file
- `src/Backend/StatsTid.Backend.Api/Program.cs` — refactored (slim DI + wiring)
- `src/Backend/StatsTid.Backend.Api/Endpoints/AuthEndpoints.cs` — new file (from refactor)
- `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs` — new file (from refactor)
- `docker/postgres/init.sql` — seed data for local configurations

---

### TASK-703 — ConfigResolutionService

| Field | Value |
|-------|-------|
| **ID** | TASK-703 |
| **Status** | complete |
| **Agent** | Payroll Integration → moved to Infrastructure by Orchestrator |
| **Components** | Infrastructure (moved from Payroll) |
| **KB Refs** | ADR-010, PAT-003 |
| **Reviewer Audit** | performed — BLOCKER: moved from Payroll to Infrastructure to eliminate bounded context violation. WARNING: duplicated central config dictionary noted (sync test recommended). |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Implements ADR-010 central + local config merge at service layer. Contains central config dictionary (6 configs: AC/HK/PROSA x OK24/OK26), protected keys that cannot be overridden locally, and constraint validation for allowed overrides. Originally created in Payroll; moved to Infrastructure after Reviewer audit identified bounded context violation.

**Validation Criteria**:
- [x] Merges central AgreementRuleConfig with local DB overrides
- [x] Protected keys (HasOvertime, supplement rates, etc.) cannot be overridden
- [x] Allowed overrides validated against central constraints
- [x] Falls back to central config on DB errors
- [x] ValidateLocalOverride provides pre-validation for API layer
- [x] Lives in Infrastructure (not Payroll) — shared by both services

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs` — new file (moved from Payroll)
- `src/Integrations/StatsTid.Integrations.Payroll/Services/ConfigResolutionService.cs` — deleted

---

### TASK-704 — Retrofit org-scope on existing endpoints

| Field | Value |
|-------|-------|
| **ID** | TASK-704 |
| **Status** | complete |
| **Agent** | Security & Compliance |
| **Components** | Backend API |
| **KB Refs** | ADR-009 |
| **Reviewer Audit** | performed — no findings specific to this task |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Added OrgScopeValidator checks to all 7 time-entry/absence/flex/calculate endpoints. Employee role: ownership check (actorId == employeeId). Higher roles: OrgScopeValidator.ValidateEmployeeAccessAsync → 403 on scope mismatch.

**Validation Criteria**:
- [x] All GET endpoints enforce org-scope for non-Employee roles
- [x] All POST endpoints enforce org-scope for non-Employee roles
- [x] Employee role: ownership check only
- [x] 403 response with reason on scope mismatch

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs` — modified (added OrgScopeValidator to all endpoints)

---

### TASK-705 — Org + User + Role management CRUD endpoints

| Field | Value |
|-------|-------|
| **ID** | TASK-705 |
| **Status** | complete |
| **Agent** | Security & Compliance |
| **Components** | Backend API |
| **KB Refs** | ADR-008, ADR-009 |
| **Reviewer Audit** | performed — WARNING: UserUpdated event missing, fixed by Orchestrator |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Created AdminEndpoints with 8 CRUD endpoints for organization, user, and role management. All endpoints scoped via OrgScopeValidator with privilege hierarchy enforcement. Domain events emitted for all mutations.

**Validation Criteria**:
- [x] 8 endpoints: org list/create, user list/create/update, role list/grant/revoke
- [x] All scoped via OrgScopeValidator
- [x] Privilege hierarchy enforcement for role grants
- [x] Domain events emitted for all mutations (including UserUpdated — added post-review)
- [x] Audit records for role operations

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` — new file
- `src/SharedKernel/StatsTid.SharedKernel/Events/UserUpdated.cs` — new file (added post-review)
- `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` — modified (registered UserUpdated)

---

### TASK-706 — Period approval endpoints

| Field | Value |
|-------|-------|
| **ID** | TASK-706 |
| **Status** | complete |
| **Agent** | Security & Compliance |
| **Components** | Backend API |
| **KB Refs** | ADR-009 |
| **Reviewer Audit** | performed — no findings specific to this task |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Created ApprovalEndpoints with 5 period approval workflow endpoints. State machine: DRAFT → SUBMITTED → APPROVED | REJECTED (rejected can re-submit). All transitions emit domain events and audit records. Leader scope enforcement via OrgScopeValidator.

**Validation Criteria**:
- [x] 5 endpoints: submit, approve, reject, pending list, employee periods
- [x] State machine: valid transitions enforced, invalid transitions rejected
- [x] Domain events: PeriodSubmitted, PeriodApproved, PeriodRejected
- [x] Audit records written for all transitions
- [x] Scope enforcement: employee for submit/view own, leader for approve/reject

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs` — new file

---

### TASK-707 — Local configuration endpoints

| Field | Value |
|-------|-------|
| **ID** | TASK-707 |
| **Status** | complete |
| **Agent** | Security & Compliance |
| **Components** | Backend API |
| **KB Refs** | ADR-010 |
| **Reviewer Audit** | performed — NOTE: config visibility to employees (design choice, no action) |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Created ConfigEndpoints with 5 local configuration endpoints. Uses ConfigResolutionService for merge + validation. Mutations emit LocalConfigurationChanged events and audit records. Protected config areas validated before persist.

**Validation Criteria**:
- [x] 5 endpoints: effective config, local overrides, create, deactivate, constraints reference
- [x] Central constraint validation via ConfigResolutionService.ValidateLocalOverride
- [x] Domain events emitted for all mutations
- [x] Audit records written
- [x] Route ordering: literal routes mapped before parameterized

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs` — new file

---

### TASK-708 — Approval guard on payroll export

| Field | Value |
|-------|-------|
| **ID** | TASK-708 |
| **Status** | complete |
| **Agent** | Payroll Integration |
| **Components** | Payroll Integration |
| **KB Refs** | SYSTEM_TARGET.md § H |
| **Reviewer Audit** | performed — WARNING: guard only on calculate-and-export, not low-level endpoints (intentional, documented) |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Added approval guard on `/api/payroll/calculate-and-export`. Checks period approval status via ApprovalPeriodRepository — only APPROVED periods may proceed to payroll export. Returns 403 with descriptive error if period is not approved.

**Validation Criteria**:
- [x] Period status checked before export
- [x] 403 returned if not APPROVED
- [x] Descriptive error with employeeId, period dates, current status
- [x] `/api/payroll/recalculate` intentionally not guarded (admin-initiated retroactive corrections)

**Files Changed**:
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` — modified (added guard + DI registrations)

---

### TASK-709 — Sprint 7 tests

| Field | Value |
|-------|-------|
| **ID** | TASK-709 |
| **Status** | complete |
| **Agent** | Test & QA |
| **Components** | Tests |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (Test & QA only) |
| **Orchestrator Approved** | yes — 2026-03-04 |

**Description**: Created 38 new unit tests across 3 files covering Sprint 7 features: org-scope validation logic (10 tests), config resolution + constraint validation (20 tests), and approval state machine + event serialization (8 tests).

**Validation Criteria**:
- [x] At least 30 new tests (actual: 38)
- [x] Scope validation: GLOBAL, ORG_AND_DESCENDANTS, ORG_ONLY, employee ownership
- [x] Config resolution: central config lookup, protected key rejection, constraint validation
- [x] Approval: state machine model, event serialization roundtrips
- [x] All tests deterministic (no I/O, no async, no network)
- [x] All 217 tests pass

**Files Changed**:
- `tests/StatsTid.Tests.Unit/Security/Sprint7ScopeTests.cs` — new file (10 tests)
- `tests/StatsTid.Tests.Unit/Sprint7ConfigTests.cs` — new file (20 tests)
- `tests/StatsTid.Tests.Unit/Sprint7ApprovalTests.cs` — new file (8 tests)

---

## Reviewer Audit Summary

**Reviewer triggered**: MANDATORY (P1: new service boundary, P7: security enforcement, cross-domain)

| Severity | Finding | Resolution |
|----------|---------|------------|
| BLOCKER | Backend→Payroll project reference violates bounded contexts | Fixed: moved ConfigResolutionService to Infrastructure |
| BLOCKER | Seed data MaxFlexBalance 120 exceeds HK central max of 100 | Fixed: changed to 80 |
| WARNING | PUT /api/admin/users/{userId} missing domain event | Fixed: added UserUpdated event + emission |
| WARNING | Duplicated central config dictionary between Rule Engine and ConfigResolutionService | Accepted: documented sync hazard. Recommend sync test in future sprint. |
| WARNING | Approval guard only on calculate-and-export, not low-level exports | Accepted: intentional design. Low-level endpoints are internal. |
| NOTE | OrgScopeValidator singleton with per-request DB connections | No action: safe pattern, per-call connections. |
| NOTE | ApprovalPeriod init-only Status | No action: consistent with immutable model pattern. |
| NOTE | Config visibility to EmployeeOrAbove | No action: design choice, scope enforcement prevents cross-org. |

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule changes this sprint |
| Wage type mappings produce correct SLS codes | N/A | No mapping changes this sprint |
| Overtime/supplement calculations are deterministic | verified | Central config constraints prevent local override of supplement rates/flags |
| Absence effects on norm/flex/pension are correct | N/A | No absence rule changes |
| Retroactive recalculation produces stable results | N/A | No recalculation changes |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 202 | all passing |
| Regression tests | 15 | all passing |
| Smoke tests | 4 | N/A (requires Docker) |
| **Total** | 217 | — |

## Sprint Retrospective

**What went well**: Backend refactoring into endpoint group files enabled true parallel agent work — 5 agents ran simultaneously on separate files with zero merge conflicts. Reviewer audit caught 2 real BLOCKERs (boundary violation, seed data constraint violation) before they could cause issues.

**What to improve**: Central config dictionary duplication between Rule Engine and ConfigResolutionService is a maintenance hazard. Consider extracting to SharedKernel in a future sprint. Low-level payroll export endpoints should have access controls documented.

**Knowledge produced**: No new KB entries this sprint (ADR-008/009/010 from Sprint 6 were sufficient).
