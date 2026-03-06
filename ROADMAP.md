# StatsTid Roadmap

> Technology stack, phased milestones, and detailed next-sprint planning (rolling detail). See [SYSTEM_TARGET.md](SYSTEM_TARGET.md) for product definition, [CLAUDE.md](CLAUDE.md) for governance.

## Technology Stack

- **Backend**: C# / .NET 8 (Minimal APIs)
- **Frontend**: React 18 + TypeScript + Vite + ShadCN/ui (Radix primitives for complex interactions) + CSS Modules + IBM Plex Sans. Visual language inspired by Det Fælles Designsystem (designsystem.dk). See ADR-011.
- **Event Store**: PostgreSQL with custom event tables (via Npgsql, no EF Core)
- **Containerization**: Docker Compose (8 services)
- **Testing**: xUnit
- **Serialization**: System.Text.Json with polymorphic type handling
- **Architecture**: Event sourcing, outbox pattern, CQRS-lite
- **Rule Engine**: Pure functions, no I/O, deterministic, version-aware (OK24+)

## Completed Sprints

| Sprint | Title | Key Deliverables | Tests |
|--------|-------|------------------|-------|
| Sprint 1 | Foundation | 8-service Docker skeleton, event sourcing, first rule | 12 |
| Sprint 2 | Rule Engine Expansion | Absence/flex/supplement logic, OK version transitions, frontend scaffold | 74 |
| Sprint 3 | Security & Compliance | JWT auth, RBAC, audit logging, correlation IDs, input validation, CI/CD | 103 |
| Sprint 4 | Payroll Traceability | Absence completion, flex payout, PeriodCalculationService, payroll export chain, traceability | 133 |
| Sprint 5 | On-Call Duty + SLS Export | Flex unification, on-call duty basics, event emission, HTTP parallelization, retroactive corrections, SLS export formatter | 158 |
| Sprint 6 | RBAC + Org Hierarchy | 5-role RBAC, materialized path org hierarchy, scope-embedded JWT, DB-backed auth, 8 new events | 179 |
| Sprint 7 | Local Config + Org-Scope Enforcement | OrgScopeValidator, ConfigResolutionService, admin CRUD, period approval, local config endpoints, approval guard | 217 |
| Sprint 8 | Frontend: Design System + Role-Based UI | Design tokens, 20 UI components, auth context, API client, layout shell, role-based navigation, 6 admin/approval/config pages, route guards, 25 frontend tests | 242 |
| Sprint 9 | Skema: Monthly Spreadsheet + Timer + Two-Step Approval | Skema monthly grid (replaces 3 pages), backend-persisted timer, two-step approval (employee → manager), project CRUD, 3 new DB tables, 4 new events, 25 BE + 8 FE tests, JWT claim fix | 275 |

## Phase Roadmap

This roadmap uses a **rolling detail** pattern: only the next sprint has task-level planning. Future phases have milestone-level descriptions. After each sprint completes, the next sprint is promoted to detailed planning.

> **Sprint numbering rule**: Sprint numbers are strictly sequential (see CLAUDE.md § Sprint Numbering & Re-prioritization). Phase-to-sprint mappings below are projections. When execution order changes, the Orchestrator replans affected sprints and updates these mappings — sprint numbers are never skipped or reordered.

### Phase 1 — Rule Engine Completion + Payroll Chain (Sprints 4–5)

**Priority focus**: P2 (Deterministic rule engine), P3 (Event sourcing), P6 (Payroll integration)

The critical gap is payroll integration — infrastructure exists but the end-to-end traceability chain is disconnected. Phase 1 connects rules to payroll export and completes the absence type inventory.

- **Sprint 4** (complete): Absence completion, flex payout, PeriodCalculationService "glue", payroll export endpoint, traceability regression tests
- **Sprint 5** (complete): Flex endpoint unification, on-call duty basics, event emission + HTTP parallelization, retroactive correction foundation, SLS export format, 158 tests

### Phase 2 — RBAC, Local Config, Period Approval + Frontend (Sprints 6–8)

**Priority focus**: P7 (Security), P9 (Usability)

Does not affect the deterministic core. Focuses on organizational hierarchy, local configuration, period approval, and user-facing completeness.

- **Sprint 6** (complete): 5-role RBAC foundation, organizational hierarchy (materialized path), scope-embedded JWT, DB-backed auth, 8 new domain events, 21 new tests (179 total). See [docs/sprints/SPRINT-6.md](docs/sprints/SPRINT-6.md).
- **Sprint 7** (complete): Org-scope enforcement, local config + period approval + admin CRUD + config endpoints, approval guard on payroll export. See [docs/sprints/SPRINT-7.md](docs/sprints/SPRINT-7.md).
- **Sprint 8** (complete): Frontend design system, 20 UI components, auth context, API client, role-based navigation, 6 admin/approval/config pages, route guards, 25 frontend tests. See [docs/sprints/SPRINT-8.md](docs/sprints/SPRINT-8.md).

### Phase 2b — Skema (Sprint 9)

**Priority focus**: P3 (Event sourcing), P7 (Security), P9 (Usability)

**Re-prioritized**: Sprint 9 was originally projected for Phase 3 (Advanced Rules). The Skema monthly spreadsheet feature was re-prioritized as Sprint 9 (Tier 2 re-prioritization) because employee-facing time registration UX is prerequisite to meaningful testing of advanced rules. Phase 3 shifts to Sprints 10–11.

- **Sprint 9** (complete): Skema monthly grid replaces 3 separate pages, backend-persisted timer (Tjek ind/Tjek ud), two-step approval flow (employee self-approve → manager approve), org-scoped project management, agreement-driven absence type rows with LocalAdmin visibility control, JWT claim remapping fix. See [docs/sprints/SPRINT-9.md](docs/sprints/SPRINT-9.md).

#### Impact Assessment (Tier 2 Re-prioritization)

**Affected sprints**:
- S9 (was: Advanced Rules Phase 3 start) → Now: Skema feature
- S10-S11 (was: Advanced Rules Phase 3 completion) → Now: Phase 3 start, shifted +1 sprint

**Scope changes**:
- Phase 3 unchanged in content — only shifted forward by one sprint
- No sprint needs splitting or merging
- No new prerequisites introduced (Skema consumes existing events/models)

**Updated phase-sprint ranges**:
- Phase 2b (Skema): Sprint 9 ← new
- Phase 3 (Advanced Rules): Sprints 10–11 (was 9–10)
- Phase 4 (Production): Sprint 12+ (was 11+)

### Phase 3 — Advanced Rules + Retroactive Corrections (Sprints 10–11)

**Priority focus**: P2 (Deterministic rule engine), P4 (Version correctness), P6 (Payroll integration)

Depends on the connected payroll chain from Phase 1. These sprints tackle the most complex rule domains and prove the architecture works end-to-end across time.

- **Sprint 10** (in-progress): Tech debt cleanup (idempotency guard, FlexEvaluationResponse DTO, config dict dedup, smoke test fix, GetDescendantsAsync optimization) + Rule engine expansion (4-week norm periods, part-time pro rata, call-in work, travel time). See [docs/sprints/SPRINT-10.md](docs/sprints/SPRINT-10.md).
- **Sprint 11** (planned): Retroactive recalculation across OK version transitions, payroll re-export with delta tracking, AC position-based rule overrides, academic/research norm systems

### Phase 4 — Production Hardening (Sprint 12+)

**Priority focus**: All priorities — cross-cutting production readiness

Only makes sense once functional completeness is achieved.

- Performance profiling and optimization
- Monitoring, alerting, health checks
- Real SLS integration (replacing mock)
- Load testing, stress testing
- Security audit, penetration testing
- Documentation and operational runbooks

## SYSTEM_TARGET.md Coverage Tracker

Projected functional coverage by requirement area. Percentages are cumulative.

| Requirement Area | S1–S3 | S4 | S5 (Phase 1) | S6 | S7 | S8 (Phase 2) | S9 (Phase 2b) | After Phase 3 | After Phase 4 |
|------------------|-------|-----|--------------|-----|-----|---------------------|---------------|---------------|---------------|
| A. Basic Time Registration | 80% | 80% | 85% | 85% | 85% | 95% | 98% | 98% | 100% |
| B. Working Time Rules | 70% | 72% | 75% | 75% | 75% | 95% | 95% | 95% | 100% |
| C. Time Types & Supplements | 60% | 60% | 70% | 70% | 70% | 95% | 95% | 95% | 100% |
| D. Absence Types | 65% | 80% | 85% | 85% | 85% | 95% | 97% | 97% | 100% |
| E. Organizational Structure | 0% | 0% | 0% | 70% | 85% | 90% | 92% | 92% | 100% |
| F. Roles and Authorization | 0% | 0% | 0% | 50% | 85% | 90% | 90% | 90% | 100% |
| G. Local Configuration | 0% | 0% | 0% | 10% | 75% | 80% | 85% | 85% | 100% |
| H. Period Approval Workflow | 0% | 0% | 0% | 10% | 80% | 85% | 95% | 95% | 100% |
| AC-Specific Requirements | 40% | 42% | 45% | 45% | 45% | 90% | 90% | 90% | 100% |
| Payroll Integration | 50% | 80% | 88% | 88% | 90% | 95% | 95% | 95% | 100% |
| External Integrations | 60% | 60% | 60% | 60% | 60% | 90% | 90% | 90% | 100% |
| **Overall** | **~39%** | **~43%** | **~46%** | **~55%** | **~67%** | **~91%** | **~93%** | **~93%** | **100%** |

## Sprint 5 — Completed

Sprint 5 completed Phase 1 (Sprints 4–5). See [docs/sprints/SPRINT-5.md](docs/sprints/SPRINT-5.md) for full task log.

**Key deliverables**: Flex endpoint unification (PAT-006), on-call duty basics (AC disabled, HK/PROSA at 1/3 rate), PeriodCalculationCompleted event emission, HTTP rule call parallelization (Task.WhenAll), retroactive correction foundation (models + service + endpoint), SLS pipe-delimited export formatter, 25 new tests (158 total).

## Sprint 6 — Completed

Sprint 6 started Phase 2 (RBAC + Org Hierarchy). See [docs/sprints/SPRINT-6.md](docs/sprints/SPRINT-6.md) for full task log.

**Key deliverables**: 5-role RBAC system (GlobalAdmin, LocalAdmin, LocalHR, LocalLeader, Employee), organizational hierarchy with materialized path (ADR-008), scope-embedded JWT authorization (ADR-009), local config merge design (ADR-010), DB-backed dual-mode login, 8 new domain events, 3 new repositories, 21 new tests (179 total).

**Reviewer findings addressed**: BLOCKER (JWT scope serialization case mismatch) fixed. WARNINGs (role ordering, expiry filter, seed passwords) fixed. Deferred: endpoint-level org-scope enforcement (Sprint 7), GetDescendantsAsync double connection.

**Sprint 7 backlog (from Sprint 6)**:
- ConfigResolutionService: merge central + local config, validate constraints (ADR-010)
- Org management, user management, role assignment API endpoints
- Period approval API endpoints (submit/approve/reject/pending)
- Approval guard on payroll export (only APPROVED periods)
- Endpoint-level org-scope enforcement in ScopeAuthorizationHandler

## Sprint 7 — Completed

Sprint 7 completed Phase 2 backend work (Local Config + Period Approval + Org-Scope Enforcement). See [docs/sprints/SPRINT-7.md](docs/sprints/SPRINT-7.md) for full task log.

**Key deliverables**: OrgScopeValidator service, ConfigResolutionService (in Infrastructure, per Reviewer audit), Backend refactored into endpoint groups, AdminEndpoints (8 CRUD endpoints), ApprovalEndpoints (5 approval workflow endpoints), ConfigEndpoints (5 local config endpoints), approval guard on payroll export, 2 new repositories, 38 new tests (217 total).

**Reviewer findings addressed**: 2 BLOCKERs (Backend→Payroll boundary violation fixed by moving ConfigResolutionService to Infrastructure; seed data constraint violation fixed). 3 WARNINGs (UserUpdated event added; central config dict sync hazard documented; low-level export endpoints intentionally unguarded).

**Backlog (from Sprint 5 retrospective, deferred to Phase 3)**:
- Add idempotency tokens for retroactive correction events (Reviewer WARNING)
- Define explicit FlexEvaluationResponse DTO in SharedKernel (Reviewer WARNING)
- Call-in work (CALL_IN_WORK), complex on-call scenarios
- 4-week norm periods, part-time pro rata

## Sprint 8 — Completed

Sprint 8 completed Phase 2 (Frontend: Design System + Role-Based UI). See [docs/sprints/SPRINT-8.md](docs/sprints/SPRINT-8.md) for full task log.

**Key deliverables**: Design token system (designsystem.dk-inspired), 14 scratch-built UI components + 6 Radix-wrapped components, AuthContext with JWT decode, centralized API client, role-based sidebar navigation, RequireAuth/RequireRole guards, 6 admin/approval/config pages (OrgManagement, UserManagement, RoleManagement, ApprovalDashboard, MyPeriods, ConfigManagement), 5 existing pages restyled with CSS Modules, vitest test infrastructure with 25 frontend tests. 17 tasks completed across 4 phases using parallel UX agents in worktrees.

**Phase 2 complete**: Sprints 6-8 delivered full RBAC, org hierarchy, local config, period approval, and frontend covering all 30 backend endpoints. Overall functional coverage: ~91%.

## Sprint 9 — Completed

Sprint 9 delivered the Skema feature (Phase 2b re-prioritization). See [docs/sprints/SPRINT-9.md](docs/sprints/SPRINT-9.md) for full task log.

**Key deliverables**: Skema monthly spreadsheet replacing 3 separate pages (Ugeoversigt, Tidsregistrering, Fraværsregistrering), backend-persisted timer (Tjek ind/Tjek ud), two-step approval flow (employee self-approve → manager approve, ADR-012), org-scoped project CRUD, agreement-driven absence type rows with LocalAdmin visibility control, 3 new DB tables, 4 new domain events, JWT claim remapping fix (FAIL-001), 25 new backend tests + 8 new frontend tests (275 total).

**Phase 2b complete**: Sprint 9 delivers employee-facing monthly registration UX prerequisite for Phase 3 advanced rule testing. Overall functional coverage: ~93%.

**Backlog (from Sprint 5/7 retrospective, deferred to Phase 3)**:
- Add idempotency tokens for retroactive correction events (Reviewer WARNING)
- Define explicit FlexEvaluationResponse DTO in SharedKernel (Reviewer WARNING)
- Call-in work (CALL_IN_WORK), complex on-call scenarios
- 4-week norm periods, part-time pro rata
- AC position-based rule overrides, academic/research norm systems

## Architecture Decisions

See [docs/knowledge-base/INDEX.md](docs/knowledge-base/INDEX.md) for the full structured decision log (ADR, PAT, DEP, RES entries).

## Sprint Execution Logs

See [docs/sprints/INDEX.md](docs/sprints/INDEX.md) for the formal sprint log with validation evidence and traceability.
