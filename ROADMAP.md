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
| Sprint 10 | Tech Debt Cleanup + Rule Engine Expansion | CentralAgreementConfigs dedup, idempotency guard, FlexEvaluationResponse DTO, call-in work, travel time, multi-week norm | 304 |
| Sprint 11 | Retroactive Corrections + AC Position Overrides + Academic Norms | OK version split, correction SLS export, position overrides (Option C), academic norm model, NORM_DEVIATION, ADR-013 | 306 |
| Sprint 12 | Database-Backed Agreement Configuration Management | DB-backed configs (ADR-014), DRAFT/ACTIVE/ARCHIVED lifecycle, GlobalAdmin UI, 8 config endpoints | 334 |

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
- Phase 3c (Agreement Config Management): Sprint 12 ← new (re-prioritized from Phase 4)
- Phase 3d (Employee Experience): Sprint 13
- Phase 3e (Position Override + Wage Type Mapping UI): Sprint 14
- Phase 4 (Production): Sprint 15+ (was 14+, then 12+, then 11+)

### Phase 3 — Advanced Rules + Retroactive Corrections (Sprints 10–11)

**Priority focus**: P2 (Deterministic rule engine), P4 (Version correctness), P6 (Payroll integration)

Depends on the connected payroll chain from Phase 1. These sprints tackle the most complex rule domains and prove the architecture works end-to-end across time.

- **Sprint 10** (complete): Tech debt cleanup (idempotency guard, FlexEvaluationResponse DTO, config dict dedup, smoke test fix, GetDescendantsAsync optimization) + Rule engine expansion (4-week norm periods, part-time pro rata, call-in work, travel time). See [docs/sprints/SPRINT-10.md](docs/sprints/SPRINT-10.md).
- **Sprint 11** (complete): Retroactive OK version split recalculation, delta/correction SLS export, AC position-based rule overrides with controlled position registry (Option C), academic/research annual norm model (ANNUAL_ACTIVITY), NormCheckRule cleanup, NORM_DEVIATION wage type, ADR-013 (no cascade), 35 new tests (306 total). See [docs/sprints/SPRINT-11.md](docs/sprints/SPRINT-11.md).

### Phase 3c — Agreement Configuration Management (Sprint 12)

**Priority focus**: P1 (Architectural integrity), P2 (Deterministic rule engine — preservation), P3 (Event sourcing), P7 (Security)

Moves agreement configs from static code to database, enabling GlobalAdmin self-service management through UI. The rule engine remains pure — only the config source changes.

- **Sprint 12** (complete): DB-backed agreement configs (ADR-014), agreement_configs table with Draft/Active/Archived lifecycle, seed migration from CentralAgreementConfigs, AgreementConfigRepository, ConfigResolutionService rewiring, GlobalAdmin API endpoints (CRUD + clone + publish + archive), agreement management frontend page (overview + editor + diff view), validation rules, comprehensive tests. See [docs/sprints/SPRINT-12.md](docs/sprints/SPRINT-12.md).

### Phase 3d — Employee Experience: Unified "Min Tid" (Sprint 13)

**Priority focus**: P9 (Usability), P7 (Security)

**Re-prioritized** (Tier 1): Sprint 13 was projected for "Position Override + Wage Type Mapping UI". Re-prioritized to employee experience consolidation — balance overview + time registration + month approval on one page. Position Override + Wage Type Mapping UI shifts to Sprint 14.

- **Sprint 13** (complete): Balance summary endpoint (flex, vacation, norm, overtime), BalanceSummary component with 4 responsive cards, SkemaPage integration, sidebar rename "Skema" → "Min Tid", "Mine perioder" removed from primary nav. See [docs/sprints/SPRINT-13.md](docs/sprints/SPRINT-13.md).

### Phase 3e — Position Override + Wage Type Mapping UI (Sprint 14)

**Priority focus**: P6 (Payroll integration), P7 (Security), P9 (Usability)

Extends the DB-backed config pattern to position overrides and wage type mappings. Reuses the architecture established in Sprint 12.

- **Sprint 14** (complete): 3 new DB tables (position_override_configs, audit tables), PositionOverrideConfigEntity + 4 domain events, WageTypeMapping Position property + 3 domain events, PositionOverrideRepository + WageTypeMappingRepository, ConfigResolutionService DB-first position override lookup with static fallback, 12 GlobalAdmin CRUD endpoints (7 position override + 5 wage type mapping), 2 admin pages (Positionstilpasninger + Lønartstilknytninger), 22 new tests (406 total). See [docs/sprints/SPRINT-14.md](docs/sprints/SPRINT-14.md).

### Phase 4 — Production Hardening (Sprint 15+)

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

| Requirement Area | S1–S3 | S4 | S5 (Phase 1) | S6 | S7 | S8 (Phase 2) | S9 (Phase 2b) | S10–S11 (Phase 3) | S12 (Phase 3c) | S13 (Phase 3d) | S14 (Phase 3e) | After Phase 4 |
|------------------|-------|-----|--------------|-----|-----|---------------------|---------------|-------------------|-----------------|----------------|----------------|---------------|
| A. Basic Time Registration | 80% | 80% | 85% | 85% | 85% | 95% | 98% | 98% | 98% | 99% | 99% | 100% |
| B. Working Time Rules | 70% | 72% | 75% | 75% | 75% | 95% | 95% | 98% | 98% | 98% | 98% | 100% |
| C. Time Types & Supplements | 60% | 60% | 70% | 70% | 70% | 95% | 95% | 97% | 97% | 97% | 97% | 100% |
| D. Absence Types | 65% | 80% | 85% | 85% | 85% | 95% | 97% | 97% | 97% | 97% | 97% | 100% |
| E. Organizational Structure | 0% | 0% | 0% | 70% | 85% | 90% | 92% | 95% | 95% | 95% | 95% | 100% |
| F. Roles and Authorization | 0% | 0% | 0% | 50% | 85% | 90% | 90% | 92% | 95% | 95% | 95% | 100% |
| G. Local Configuration | 0% | 0% | 0% | 10% | 75% | 80% | 85% | 90% | 95% | 95% | 98% | 100% |
| H. Period Approval Workflow | 0% | 0% | 0% | 10% | 80% | 85% | 95% | 95% | 95% | 95% | 95% | 100% |
| I. Agreement Config Mgmt | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 85% | 85% | 95% | 100% |
| AC-Specific Requirements | 40% | 42% | 45% | 45% | 45% | 90% | 90% | 97% | 98% | 98% | 99% | 100% |
| Payroll Integration | 50% | 80% | 88% | 88% | 90% | 95% | 95% | 98% | 98% | 98% | 99% | 100% |
| External Integrations | 60% | 60% | 60% | 60% | 60% | 90% | 90% | 90% | 90% | 90% | 90% | 100% |
| **Overall** | **~39%** | **~43%** | **~46%** | **~55%** | **~67%** | **~91%** | **~93%** | **~97%** | **~95→97%** | **~96→97%** | **~97→99%** | **100%** |

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
- ~~Add idempotency tokens for retroactive correction events~~ (done in S10)
- ~~Define explicit FlexEvaluationResponse DTO in SharedKernel~~ (done in S10)
- ~~Call-in work (CALL_IN_WORK), complex on-call scenarios~~ (done in S10)
- ~~4-week norm periods, part-time pro rata~~ (done in S10)
- ~~AC position-based rule overrides, academic/research norm systems~~ (done in S11)

## Sprint 10 — Completed

Sprint 10 completed Phase 3a (Tech Debt + Rule Engine Expansion). See [docs/sprints/SPRINT-10.md](docs/sprints/SPRINT-10.md) for full task log.

**Key deliverables**: CentralAgreementConfigs single source of truth, idempotency guard on retroactive corrections, FlexEvaluationResponse DTO, call-in work rule, travel time rule, multi-week norm periods, part-time pro rata audit, NormCheck config-aware migration, 26 new tests (304 total).

## Sprint 11 — Completed

Sprint 11 completed Phase 3b (Retroactive Corrections + AC Position Overrides + Academic Norms). See [docs/sprints/SPRINT-11.md](docs/sprints/SPRINT-11.md) for full task log.

**Key deliverables**: OK version split recalculation (RetroactiveCorrectionService splits periods at transition date), delta/correction SLS export format (HC|/C|/TC| prefixes), AC position-based rule overrides with controlled position registry (PositionOverrideConfigs, positions table, Option C design), academic/research annual norm model (NormModel.ANNUAL_ACTIVITY with pro-rated annual hours), position-aware wage type mappings (COALESCE PK), NORM_DEVIATION wage type, ADR-013 (no cascade), 35 new tests (306 total).

**Reviewer findings addressed**: ConfigResolutionService missing 9 fields in merged config construction (BLOCKER — fixed).

**Phase 3 complete**: Sprints 10-11 delivered all advanced rules, retroactive corrections, position overrides, and academic norms. Overall functional coverage: ~97%.

## Sprint 12 — Completed

Sprint 12 completed Phase 3c (Agreement Configuration Management). See [docs/sprints/SPRINT-12.md](docs/sprints/SPRINT-12.md) for full task log.

**Key deliverables**: DB-backed agreement configs (ADR-014), agreement_configs + agreement_config_audit tables with DRAFT/ACTIVE/ARCHIVED lifecycle, AgreementConfigRepository with transactional publish, AgreementConfigSeeder (idempotent from CentralAgreementConfigs), ConfigResolutionService rewired to DB with static fallback, 8 GlobalAdmin CRUD+lifecycle endpoints, agreement overview + editor frontend pages with clone/publish/archive, comparison diff view, 28 new unit tests (334 total).

**Phase 3c complete**: Sprint 12 delivered full agreement config management. Overall functional coverage: ~97%.

## Sprint 13 — Completed

Sprint 13 completed Phase 3d (Employee Experience Consolidation). See [docs/sprints/SPRINT-13.md](docs/sprints/SPRINT-13.md) for full task log.

**Key deliverables**: Balance summary endpoint (flex, vacation, norm, overtime aggregation from events + config), BalanceSummary component with 4 responsive balance cards (Flex saldo, Ferie, Normtimer, Merarbejde/Overarbejde), SkemaPage integration, sidebar rename "Skema" → "Min Tid", 15 new backend test cases + 5 new frontend tests (387 total).

**Phase 3d complete**: Sprint 13 delivers unified employee experience. Overall functional coverage: ~97%.

## Sprint 14 — Completed

Sprint 14 completed Phase 3e (Position Override + Wage Type Mapping UI). See [docs/sprints/SPRINT-14.md](docs/sprints/SPRINT-14.md) for full task log.

**Key deliverables**: DB-backed position overrides (migrated from static PositionOverrideConfigs), ConfigResolutionService rewired with DB-first lookup + static fallback (Reviewer confirmed P1 compliance), WageTypeMapping CRUD with Position support, 12 GlobalAdmin API endpoints with audit trails + domain events, 2 admin pages (Positionstilpasninger, Lønartstilknytninger), PayrollMappingService now reads Position, 22 new tests (406 total).

**Phase 3e complete**: Sprint 14 delivers full admin management for position overrides and wage type mappings. Overall functional coverage: ~98%.

## Architecture Decisions

See [docs/knowledge-base/INDEX.md](docs/knowledge-base/INDEX.md) for the full structured decision log (ADR, PAT, DEP, RES entries).

## Sprint Execution Logs

See [docs/sprints/INDEX.md](docs/sprints/INDEX.md) for the formal sprint log with validation evidence and traceability.
