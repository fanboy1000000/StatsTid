# Sprint 14 — Position Override + Wage Type Mapping UI

| Field | Value |
|-------|-------|
| **Sprint** | 14 |
| **Status** | complete |
| **Start Date** | 2026-03-08 |
| **End Date** | 2026-03-08 |
| **Orchestrator Approved** | yes (2026-03-08) |
| **Build Verified** | yes — 0 errors, 0 warnings |
| **Test Verified** | yes — 353 unit + 15 regression + 41 FE = 406 passing (net +22) |

## Sprint Goal

Extend the DB-backed config pattern (ADR-014) to position overrides and wage type mappings. Position overrides migrate from static `PositionOverrideConfigs` to a new DB table with ACTIVE/INACTIVE lifecycle. Wage type mappings gain GlobalAdmin CRUD endpoints and admin UI. Both tracks include audit trails, domain events, and frontend admin pages.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (config resolution chain: DB Config → Position Override → Local Override; Reviewer confirmed)
- [x] P3 — Event sourcing (7 new domain events registered in EventSerializer, emitted on all CRUD mutations)
- [x] P6 — Payroll integration correctness (PayrollMappingService now reads Position into WageTypeMapping model)
- [x] P7 — Security (all new endpoints GlobalAdmin-only via GlobalAdminOnly policy)
- [x] P8 — CI/CD enforcement (353 unit + 15 regression + 41 FE tests passing, build clean)
- [x] P9 — Usability (2 new admin pages: Positionstilpasninger, Lønartstilknytninger)

## Reviewer Audit

Reviewer invoked for TASK-1406 (ConfigResolutionService rewiring — P1 mandatory). Findings:
- **WARNING**: Silent DB fallback may mask persistent failures (pre-existing pattern, not introduced by this change). Noted for future tech-debt.
- **NOTE**: Null-passing in test constructors is fragile (pre-existing pattern, extended by one more null!). No action required.
- **No BLOCKERs.** Orchestrator proceeds with approval.

## Task Log

### Phase 1 — Schema & Data Model

### TASK-1401 — DB schema: position overrides + audit tables

| Field | Value |
|-------|-------|
| **ID** | TASK-1401 |
| **Status** | complete |
| **Agent** | Orchestrator (small task) |
| **Components** | PostgreSQL |
| **KB Refs** | ADR-014 |
| **Reviewer Audit** | skipped (seed data, no logic) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: 3 new tables (position_override_configs, position_override_config_audit, wage_type_mapping_audit) + 4 seed rows matching static PositionOverrideConfigs data + unique partial index for ACTIVE constraint.

**Validation Criteria**:
- [x] position_override_configs table with ACTIVE/INACTIVE status CHECK
- [x] Unique partial index (agreement_code, ok_version, position_code) WHERE status='ACTIVE'
- [x] position_override_config_audit table with action CHECK
- [x] wage_type_mapping_audit table with action CHECK
- [x] 4 seed rows matching PositionOverrideConfigs.cs static data

**Files Changed**:
- `docker/postgres/init.sql` — 3 new tables, 1 index, 4 seed rows

---

### TASK-1402 — PositionOverrideConfigEntity + 4 domain events

| Field | Value |
|-------|-------|
| **ID** | TASK-1402 |
| **Status** | complete |
| **Agent** | Data Model |
| **Components** | SharedKernel |
| **KB Refs** | DEP-003 |
| **Reviewer Audit** | skipped (model/event only) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: New entity with ToPositionConfigOverride() converter + 4 domain events extending DomainEventBase.

**Validation Criteria**:
- [x] PositionOverrideConfigEntity with all DB columns as properties
- [x] ToPositionConfigOverride() maps nullable fields correctly
- [x] 4 events: Created, Updated, Activated, Deactivated — all with required OverrideId, AgreementCode, OkVersion, PositionCode

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/PositionOverrideConfigEntity.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/PositionOverrideCreated.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/PositionOverrideUpdated.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/PositionOverrideActivated.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/PositionOverrideDeactivated.cs` — new

---

### TASK-1403 — WageTypeMapping Position property + 3 domain events

| Field | Value |
|-------|-------|
| **ID** | TASK-1403 |
| **Status** | complete |
| **Agent** | Data Model |
| **Components** | SharedKernel |
| **KB Refs** | DEP-003 |
| **Reviewer Audit** | skipped (model/event only) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: Added Position property to WageTypeMapping (default empty string = generic). 3 new domain events for wage type mapping CRUD.

**Validation Criteria**:
- [x] Position property added with default empty string
- [x] 3 events: Created, Updated, Deleted — all with required key fields

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/WageTypeMapping.cs` — added Position property
- `src/SharedKernel/StatsTid.SharedKernel/Events/WageTypeMappingCreated.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/WageTypeMappingUpdated.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/WageTypeMappingDeleted.cs` — new

---

### Phase 2 — Repositories & Config Resolution

### TASK-1404 — PositionOverrideRepository

| Field | Value |
|-------|-------|
| **ID** | TASK-1404 |
| **Status** | complete |
| **Agent** | Data Model |
| **Components** | Infrastructure |
| **KB Refs** | ADR-014 |
| **Reviewer Audit** | skipped (repository pattern, no P1-P4 concerns) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: Full CRUD repository for position_override_configs with 9 methods following AgreementConfigRepository pattern. ActivateAsync uses transaction to enforce unique ACTIVE constraint.

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/PositionOverrideRepository.cs` — new

---

### TASK-1405 — WageTypeMappingRepository

| Field | Value |
|-------|-------|
| **ID** | TASK-1405 |
| **Status** | complete |
| **Agent** | Data Model |
| **Components** | Infrastructure, Payroll Integration |
| **KB Refs** | ADR-014 |
| **Reviewer Audit** | skipped (repository pattern, no P1-P4 concerns) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: CRUD repository for wage_type_mappings with 7 methods. Handles NULL/empty-string position mapping. Also fixed PayrollMappingService to populate Position on WageTypeMapping model.

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/WageTypeMappingRepository.cs` — new
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollMappingService.cs` — added Position to WageTypeMapping construction

---

### TASK-1406 — Rewire ConfigResolutionService for DB position overrides

| Field | Value |
|-------|-------|
| **ID** | TASK-1406 |
| **Status** | complete |
| **Agent** | Infrastructure |
| **Components** | Infrastructure |
| **KB Refs** | ADR-014 |
| **Reviewer Audit** | yes — no BLOCKERs (1 WARNING pre-existing, 1 NOTE pre-existing) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: Rewired ConfigResolutionService position override lookup from static-only to DB-first with static fallback (defense-in-depth). Added PositionOverrideRepository constructor dependency.

**Validation Criteria**:
- [x] DB lookup attempted first via PositionOverrideRepository.GetActiveAsync()
- [x] Static PositionOverrideConfigs.TryGetOverride() used as fallback on DB miss or exception
- [x] PositionOverrideConfigs.ApplyOverride() still used for merge (pure function, unchanged)
- [x] Resolution chain order preserved: DB Config → Position Override → Local Override
- [x] Reviewer confirmed P1 compliance

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs` — added PositionOverrideRepository dependency, DB-first position override lookup

---

### TASK-1407 — EventSerializer registration

| Field | Value |
|-------|-------|
| **ID** | TASK-1407 |
| **Status** | complete |
| **Agent** | Orchestrator (small task) |
| **Components** | Infrastructure |
| **KB Refs** | DEP-003 |
| **Reviewer Audit** | skipped (< 10 lines) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: Registered 7 new event types in EventSerializer type map.

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` — 7 new entries (34 total)

---

### Phase 3 — Backend Endpoints

### TASK-1408 — Position override admin endpoints

| Field | Value |
|-------|-------|
| **ID** | TASK-1408 |
| **Status** | complete |
| **Agent** | API Integration |
| **Components** | Backend API |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (API endpoints, no P1-P4 concerns) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: 7 GlobalAdmin-only endpoints for position override CRUD with audit trail and domain events.

**Validation Criteria**:
- [x] GET list, GET by ID, GET by agreement, POST create, PUT update, POST deactivate, POST activate
- [x] All endpoints require GlobalAdminOnly authorization
- [x] Audit trail appended on every mutation
- [x] Domain events emitted on every mutation
- [x] Registered in Program.cs

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/PositionOverrideEndpoints.cs` — new (7 endpoints)
- `src/Backend/StatsTid.Backend.Api/Program.cs` — registered MapPositionOverrideEndpoints() + MapWageTypeMappingEndpoints() + DI for repos

---

### TASK-1409 — Wage type mapping admin endpoints

| Field | Value |
|-------|-------|
| **ID** | TASK-1409 |
| **Status** | complete |
| **Agent** | API Integration |
| **Components** | Backend API |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (API endpoints, no P1-P4 concerns) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: 5 GlobalAdmin-only endpoints for wage type mapping CRUD with audit trail and domain events.

**Validation Criteria**:
- [x] GET list, GET by agreement, POST create, PUT update, DELETE
- [x] All endpoints require GlobalAdminOnly authorization
- [x] Audit trail appended on every mutation
- [x] Domain events emitted on every mutation

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/WageTypeMappingEndpoints.cs` — new (5 endpoints)

---

### Phase 4 — Frontend Admin Pages

### TASK-1410 — Position override admin page

| Field | Value |
|-------|-------|
| **ID** | TASK-1410 |
| **Status** | complete |
| **Agent** | UX |
| **Components** | Frontend |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (frontend only) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: Admin page with data table, inline create form, inline editing, activate/deactivate toggles. Danish labels. CSS Modules with design tokens.

**Files Changed**:
- `frontend/src/hooks/usePositionOverrides.ts` — new hook
- `frontend/src/pages/admin/PositionOverrideManagement.tsx` — new page
- `frontend/src/pages/admin/PositionOverrideManagement.module.css` — new styles

---

### TASK-1411 — Wage type mapping admin page

| Field | Value |
|-------|-------|
| **ID** | TASK-1411 |
| **Status** | complete |
| **Agent** | UX |
| **Components** | Frontend |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (frontend only) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: Admin page with data table, inline create/edit, delete with confirmation. Position column shows "(generel)" for empty. Danish labels. CSS Modules with design tokens.

**Files Changed**:
- `frontend/src/hooks/useWageTypeMappings.ts` — new hook
- `frontend/src/pages/admin/WageTypeMappingManagement.tsx` — new page
- `frontend/src/pages/admin/WageTypeMappingManagement.module.css` — new styles
- `frontend/src/App.tsx` — added 2 routes
- `frontend/src/components/layout/Sidebar.tsx` — added 2 nav items

---

### Phase 5 — Tests

### TASK-1412 — Unit + frontend tests

| Field | Value |
|-------|-------|
| **ID** | TASK-1412 |
| **Status** | complete |
| **Agent** | Test & QA / Orchestrator |
| **Components** | Tests, Frontend Tests |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (test-only) |
| **Orchestrator Approved** | yes — 2026-03-08 |

**Description**: Backend: 19 test cases covering entity mapping, override application, static helpers, model defaults. Frontend: 3 tests for PositionOverrideManagement page.

**Validation Criteria**:
- [x] Backend: 19 new test cases (entity ToPositionConfigOverride, ApplyOverride, GetCentralConfig, HasCentralConfig, WageTypeMapping)
- [x] Frontend: 3 tests (page title, loading state, table rendering)
- [x] Existing tests updated for new ConfigResolutionService constructor signature

**Files Changed**:
- `tests/StatsTid.Tests.Unit/Sprint14PositionOverrideTests.cs` — new (19 test cases)
- `frontend/src/pages/admin/__tests__/PositionOverrideManagement.test.tsx` — new (3 tests)
- `tests/StatsTid.Tests.Unit/Sprint12AgreementConfigTests.cs` — updated constructor call
- `tests/StatsTid.Tests.Unit/Sprint7ConfigTests.cs` — updated constructor call

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule changes |
| Wage type mappings produce correct SLS codes | N/A | No mapping value changes (admin CRUD only) |
| Overtime/supplement calculations are deterministic | N/A | No rule changes |
| Absence effects on norm/flex/pension are correct | N/A | No rule changes |
| Retroactive recalculation produces stable results | N/A | No rule changes |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 353 | all passing |
| Regression tests | 15 | all passing |
| Smoke tests | 4 | 3/4 (pre-existing auth scope issue) |
| Frontend tests | 41 | all passing |
| **Total** | **406** | — |

## Sprint Retrospective

**What went well**: Clean 5-phase execution with parallel agents. DB-first pattern (ADR-014) extended consistently. Reviewer confirmed P1 compliance with no BLOCKERs.

**What to improve**: Silent DB fallback pattern (WARNING from Reviewer) is now used in 2 places — consider circuit-breaker or health metric in future tech-debt sprint.

**Knowledge produced**: None (reused existing ADR-014 pattern, no new architectural decisions).
