# DEP-004: Endpoint Registry — UI / API / Data Model Alignment

- **Status**: approved
- **Sprint**: S10 (post)
- **Category**: dependency
- **Domains**: Frontend, Backend, Rule Engine, Payroll, Infrastructure
- **Tags**: endpoint-registry, alignment, api, frontend, data-model, traceability

## Purpose

Lightweight structural contract mapping every API endpoint to its UI consumer(s), backing data model(s), and domain event(s). Prevents architectural drift between frontend and backend agents.

**Rules:**
- No UI feature should exist without a mapped API endpoint and data model.
- Endpoints without a UI consumer must have a documented service-to-service or integration purpose.
- Agents must consult this registry before adding new endpoints or UI pages.

---

## Backend API Service (backend-api:8080)

### Authentication

| Endpoint | Method | UI Consumer | Hook | Data Model | Event | Auth |
|----------|--------|-------------|------|------------|-------|------|
| `/api/auth/login` | POST | LoginPage | AuthContext | User, RoleAssignment | — | Public |

### Skema (Monthly Spreadsheet)

| Endpoint | Method | UI Consumer | Hook | Data Model | Event | Auth |
|----------|--------|-------------|------|------------|-------|------|
| `/api/skema/{employeeId}/month` | GET | SkemaPage | useSkema | TimeEntry, AbsenceEntry, Project, WorkTimeProjection, ApprovalPeriod | — | Employee+ |
| `/api/skema/{employeeId}/save` | POST | SkemaPage | useSkema | TimeEntry, AbsenceEntry, WorkTimeProjection | TimeEntryRegistered, AbsenceRegistered, WorkTimeRegistered | Employee+ |

> GET now returns `workTime` (intervals + manualHours) + `dailyNorm` (per-day norm) instead of `timerSession`; POST `/save` accepts an optional `workTime` block (S56, ADR-028). The allocation gate lives in the existing `/api/approval/{periodId}/employee-approve` endpoint (discriminated 422).

### Balance & Årsoversigt (section added S65)

| Endpoint | Method | UI Consumer | Hook | Data Model | Event | Auth |
|----------|--------|-------------|------|------------|-------|------|
| `/api/balance/{employeeId}/summary` | GET | SkemaPage, ApprovalDetailPanel | useBalanceSummary | EntitlementBalance, FlexBalance | — | Employee+ |
| `/api/balance/{employeeId}/series` | GET | **None (FE-orphaned S65)** | — (useAccrualSeries deleted S65) | EntitlementBalance | — | Employee+ |
| `/api/balance/{employeeId}/year-overview` | GET | ArsoversigtPage (`tid/oversigt`) | useYearOverview | WorkTimeProjection, AbsenceEntry (projection), EntitlementBalance, EmployeeProfile | — (pure read) | Employee+ |

> `/series` was consumed by the S61 OversightPage accrual trend; S65 deleted that surface (OversightPage/LeaveOverview/AccrualTrend ×{tsx,css,test} + useAccrualSeries) and replaced the route with ArsoversigtPage on the new year-overview endpoint. `/series` is RETAINED (regression-covered, BalanceSeriesTests) with a ROADMAP follow-up: consolidate into year-overview or retire. The year-overview server `today` derives from the injected `TimeProvider` seam (S65, new-endpoint-only).

### Timer (RETIRED — Sprint 56, ADR-028 D5)

The three `/api/timer/*` endpoints (check-in / check-out / GET), the `useTimer` hook, `TimerControl`,
the `TimerSession` model, and the `timer_sessions` table were REMOVED. Self-recorded work time now
flows through the Skema save/GET above. The `TimerCheckedIn`/`TimerCheckedOut` events remain registered
in EventSerializer (deserialize-only) for historical event replay.

### Approval Workflow

| Endpoint | Method | UI Consumer | Hook | Data Model | Event | Auth |
|----------|--------|-------------|------|------------|-------|------|
| `/api/approval/submit` | POST | MyPeriods | useApprovals | ApprovalPeriod | PeriodSubmitted | Employee+ |
| `/api/approval/{periodId}/employee-approve` | POST | SkemaPage | useSkema | ApprovalPeriod | PeriodEmployeeApproved | Employee+ |
| `/api/approval/{periodId}/approve` | POST | ApprovalDashboard | usePendingApprovals | ApprovalPeriod | PeriodApproved | Leader+ |
| `/api/approval/{periodId}/reject` | POST | ApprovalDashboard | usePendingApprovals | ApprovalPeriod | PeriodRejected | Leader+ |
| `/api/approval/{periodId}/reopen` | POST | ApprovalDashboard | usePendingApprovals | ApprovalPeriod | PeriodReopened | Leader+ |
| `/api/approval/pending` | GET | ApprovalDashboard | usePendingApprovals | ApprovalPeriod | — | Leader+ |
| `/api/approval/{employeeId}` | GET | MyPeriods | useApprovals | ApprovalPeriod | — | Employee+ |

### Administration

| Endpoint | Method | UI Consumer | Hook | Data Model | Event | Auth |
|----------|--------|-------------|------|------------|-------|------|
| `/api/admin/organizations` | GET | OrgManagement, UserManagement, RoleManagement | useOrganizations | Organization | — | Employee+ |
| `/api/admin/organizations` | POST | OrgManagement | useOrganizations | Organization | OrganizationCreated | LocalAdmin+ |
| `/api/admin/organizations/{orgId}/users` | GET | UserManagement, RoleManagement | useOrgUsers | User | — | HR+ |
| `/api/admin/users` | POST | UserManagement | useOrgUsers | User | UserCreated | HR+ |
| `/api/admin/users/{userId}` | PUT | UserManagement | useOrgUsers | User | UserUpdated | HR+ |
| `/api/admin/users/{userId}/roles` | GET | RoleManagement | useUserRoles | RoleAssignment | — | LocalAdmin+ |
| `/api/admin/roles/grant` | POST | RoleManagement | useUserRoles | RoleAssignment | RoleAssignmentGranted | LocalAdmin+ |
| `/api/admin/roles/revoke` | POST | RoleManagement | useUserRoles | RoleAssignment | RoleAssignmentRevoked | LocalAdmin+ |

### Projects

| Endpoint | Method | UI Consumer | Hook | Data Model | Event | Auth |
|----------|--------|-------------|------|------------|-------|------|
| `/api/projects/{orgId}` | GET | ProjectManagement | useProjects | Project | — | LocalAdmin+ |
| `/api/projects/{orgId}` | POST | ProjectManagement | useProjects | Project | — | LocalAdmin+ |
| `/api/projects/{orgId}/{projectId}` | PUT | ProjectManagement | useProjects | Project | — | LocalAdmin+ |
| `/api/projects/{orgId}/{projectId}` | DELETE | ProjectManagement | useProjects | Project | — | LocalAdmin+ |

### Configuration

| Endpoint | Method | UI Consumer | Hook | Data Model | Event | Auth |
|----------|--------|-------------|------|------------|-------|------|
| `/api/config/constraints` | GET | ConfigManagement | useConfigConstraints | AgreementRuleConfig | — | LocalAdmin+ |
| `/api/config/{orgId}` | GET | ConfigManagement | useEffectiveConfig | AgreementRuleConfig, LocalConfiguration | — | LocalAdmin+ |
| `/api/config/{orgId}/local` | GET | ConfigManagement | useLocalConfig | LocalConfiguration | — | LocalAdmin+ |
| `/api/config/{orgId}` | POST | ConfigManagement | useLocalConfig | LocalConfiguration | LocalConfigurationChanged | LocalAdmin+ |
| `/api/config/{orgId}/{configId}` | DELETE | ConfigManagement | useLocalConfig | LocalConfiguration | LocalConfigurationChanged | LocalAdmin+ |
| `/api/config/{orgId}/absence-types` | GET | SkemaPage (via skema endpoint) | useSkema | AbsenceTypeVisibility, WageTypeMapping | — | Employee+ |
| `/api/config/{orgId}/absence-types/visibility` | POST | ConfigManagement | — | AbsenceTypeVisibility | — | LocalAdmin+ |

### Legacy Time Entry Endpoints

These endpoints predate the Skema monthly spreadsheet (S9). Frontend hooks exist but are superseded by useSkema. Retained for backward compatibility and potential direct API usage.

| Endpoint | Method | UI Consumer | Hook | Data Model | Event | Auth | Status |
|----------|--------|-------------|------|------------|-------|------|--------|
| `/api/time-entries` | POST | — | useTimeEntries (legacy) | TimeEntry | TimeEntryRegistered | Employee+ | Superseded by Skema |
| `/api/time-entries/{employeeId}` | GET | — | useTimeEntries (legacy) | TimeEntry | — | Employee+ | Superseded by Skema |
| `/api/absences` | POST | — | useAbsences (legacy) | AbsenceEntry | AbsenceRegistered | Employee+ | Superseded by Skema |
| `/api/absences/{employeeId}` | GET | — | useAbsences (legacy) | AbsenceEntry | — | Employee+ | Superseded by Skema |
| `/api/flex-balance/{employeeId}` | GET | — | useFlexBalance (legacy) | FlexBalance | — | Employee+ | Superseded by Skema |
| `/api/time-entries/calculate` | POST | — | — | CalculationResult | NormCheckCompleted | Employee+ | Superseded by Skema |
| `/api/time-entries/calculate-week` | POST | — | — | CalculationResult | — | Employee+ | Superseded by Skema |

### Health

| Endpoint | Method | UI Consumer | Data Model | Purpose |
|----------|--------|-------------|------------|---------|
| `/health` | GET | HealthDashboard | — | Liveness check (all 5 services) |

---

## Rule Engine Service (rule-engine:8080) — Service-to-Service Only

No direct UI consumers. Called by PeriodCalculationService (PAT-005) and Backend API calculate endpoints.

| Endpoint | Method | Caller | Data Model | Purpose |
|----------|--------|--------|------------|---------|
| `/api/rules/evaluate` | POST | PeriodCalculationService, Backend API | CalculationResult, AgreementRuleConfig | Evaluate any registered rule (norm, supplement, overtime, on-call, call-in, travel) |
| `/api/rules/evaluate-absence` | POST | PeriodCalculationService | CalculationResult | Evaluate absence rules |
| `/api/rules/evaluate-flex` | POST | PeriodCalculationService | FlexEvaluationResponse | Evaluate flex balance |
| `/api/rules/available/{okVersion}` | GET | — | RuleVersion | List available rules per OK version |

## Orchestrator Service (orchestrator:8080) — Service-to-Service Only

| Endpoint | Method | Caller | Data Model | Purpose |
|----------|--------|--------|------------|---------|
| `/api/orchestrator/execute` | POST | Backend API | orchestrator_tasks | Execute rule-evaluation or weekly-calculation task |
| `/api/orchestrator/tasks/{id}` | GET | Backend API | orchestrator_tasks | Query task status |

## Payroll Integration Service (payroll:8080) — Service-to-Service / Future Admin UI

| Endpoint | Method | UI Consumer | Data Model | Event | Purpose |
|----------|--------|-------------|------------|-------|---------|
| `/api/payroll/export` | POST | — (S2S) | PayrollExportLine, WageTypeMapping | PayrollExportGenerated | Map calculation result to wage lines |
| `/api/payroll/export-period` | POST | — (S2S) | PeriodCalculationResult, PayrollExportLine | PayrollExportGenerated | Batch export multiple results |
| `/api/payroll/calculate-and-export` | POST | **None yet** | PeriodCalculationResult, PayrollExportLine | PeriodCalculationCompleted, PayrollExportGenerated | Full period calculate + export (approval-guarded) |
| `/api/payroll/recalculate` | POST | **None yet** | CorrectionExportLine | RetroactiveCorrectionRequested | Retroactive correction (idempotent) |

## External Integration Service (external:8080) — Service-to-Service Only

| Endpoint | Method | Caller | Data Model | Event | Purpose |
|----------|--------|--------|------------|-------|---------|
| `/api/external/send` | POST | Payroll service | DeliveryStatus | IntegrationDeliveryTracked | Send payload to external system |

---

## Gap Analysis

### Endpoints Needing Future UI

| Endpoint | Priority | Target Sprint | Notes |
|----------|----------|---------------|-------|
| `POST /api/payroll/calculate-and-export` | Medium | Phase 4 (Production) | Needs payroll admin page for manual period exports |
| `POST /api/payroll/recalculate` | Medium | S11 (Retroactive Corrections) | Needs retroactive correction UI for payroll admins |
| `GET /api/rules/available/{okVersion}` | Low | — | Informational; could surface in ConfigManagement |

### Legacy Endpoints — Deprecation Candidates

The 7 legacy time-entry/absence/flex/calculate endpoints are fully superseded by the Skema composite endpoints. Candidate for deprecation in Phase 4 after confirming no external consumers depend on them.

### Registry Drift (noted S65)

This registry's coverage is current for the sections it contains, but whole endpoint families added since ~S13 were never registered (compliance, overtime, audit-visibility, reporting-line, employee-profile admin, eligibility). The S65 close added only the Balance section its sprint touched. Full backfill is an entropy-scan candidate for a docs-debt pass — when touching an unregistered family, add its section per Maintenance Rule 1 rather than extending the gap.

### UI Pages — All Mapped

All 11 frontend routes have complete API backing:

| Route | Page | Endpoints Used | Status |
|-------|------|---------------|--------|
| `/login` | LoginPage | 1 | Complete |
| `/` | SkemaPage | 6 | Complete |
| `/health` | HealthDashboard | 5 (health) | Complete |
| `/approval/mine` | MyPeriods | 2 | Complete |
| `/approval` | ApprovalDashboard | 3 | Complete |
| `/admin/users` | UserManagement | 4 | Complete |
| `/admin/orgs` | OrgManagement | 2 | Complete |
| `/admin/roles` | RoleManagement | 5 | Complete |
| `/admin/projects` | ProjectManagement | 4 | Complete |
| `/config` | ConfigManagement | 5 | Complete |
| `*` | NotFoundPage | 0 | N/A |

---

## Maintenance Rules

1. **When adding a new endpoint**: Add a row to the appropriate table above. Specify UI consumer (or "S2S" / "None yet") and backing data model.
2. **When adding a new UI page**: Add a row to the UI Pages summary. All hooks must map to registered endpoints.
3. **When deprecating an endpoint**: Move to the Legacy section with a status note.
4. **Review cadence**: Orchestrator reviews this registry at sprint boundaries.

---

## Related Knowledge Base Entries

- **PAT-005**: PeriodCalculationService HTTP rule evaluation pattern (service boundary)
- **PAT-006**: Unified rule endpoint response format
- **DEP-002**: Payroll depends on Rule Engine output types
- **DEP-003**: EventSerializer must register all event types
- **ADR-012**: Two-step approval flow
