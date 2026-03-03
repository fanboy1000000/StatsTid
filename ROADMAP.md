# StatsTid Roadmap

> Technology stack, phased milestones, and detailed next-sprint planning (rolling detail). See [SYSTEM_TARGET.md](SYSTEM_TARGET.md) for product definition, [CLAUDE.md](CLAUDE.md) for governance.

## Technology Stack

- **Backend**: C# / .NET 8 (Minimal APIs)
- **Frontend**: React + TypeScript (stub in Sprint 1)
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
- **Sprint 7** (next): Local config + period approval + API endpoints (ConfigResolutionService, org/user/role management APIs, period approval APIs, approval guard on payroll export)
- **Sprint 8**: Frontend (role-based navigation, approval dashboard, admin panels, HR views, local config admin)

### Phase 3 — Advanced Rules + Retroactive Corrections (Sprints 9–10)

**Priority focus**: P2 (Deterministic rule engine), P4 (Version correctness), P6 (Payroll integration)

Depends on the connected payroll chain from Phase 1. These sprints tackle the most complex rule domains and prove the architecture works end-to-end across time.

- On-call duty (rådighedsvagt), call-in work, travel time (working vs non-working)
- 4-week norm periods, part-time pro rata
- AC position-based rule overrides, academic/research norm systems
- Retroactive recalculation across OK version transitions
- Payroll re-export after retroactive corrections

### Phase 4 — Production Hardening (Sprint 11+)

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

| Requirement Area | S1–S3 | S4 | S5 (Phase 1) | S6 (Phase 2 start) | After Phase 2 | After Phase 3 | After Phase 4 |
|------------------|-------|-----|--------------|---------------------|---------------|---------------|---------------|
| A. Basic Time Registration | 80% | 80% | 85% | 85% | 95% | 95% | 100% |
| B. Working Time Rules | 70% | 72% | 75% | 75% | 95% | 95% | 100% |
| C. Time Types & Supplements | 60% | 60% | 70% | 70% | 95% | 95% | 100% |
| D. Absence Types | 65% | 80% | 85% | 85% | 95% | 95% | 100% |
| E. Organizational Structure | 0% | 0% | 0% | 70% | 90% | 90% | 100% |
| F. Roles and Authorization | 0% | 0% | 0% | 60% | 90% | 90% | 100% |
| G. Local Configuration | 0% | 0% | 0% | 10% | 80% | 80% | 100% |
| H. Period Approval Workflow | 0% | 0% | 0% | 10% | 85% | 85% | 100% |
| AC-Specific Requirements | 40% | 42% | 45% | 45% | 90% | 90% | 100% |
| Payroll Integration | 50% | 80% | 88% | 88% | 95% | 95% | 100% |
| External Integrations | 60% | 60% | 60% | 60% | 90% | 90% | 100% |
| **Overall** | **~39%** | **~43%** | **~46%** | **~55%** | **~91%** | **~91%** | **100%** |

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

## Sprint 7 Detailed Plan

_To be planned when Sprint 7 execution is requested. Phase 2 focus: Local Config + Period Approval + API Endpoints._

**Backlog (from Sprint 5 retrospective, deferred to Phase 3)**:
- Add idempotency tokens for retroactive correction events (Reviewer WARNING)
- Define explicit FlexEvaluationResponse DTO in SharedKernel (Reviewer WARNING)
- Call-in work (CALL_IN_WORK), complex on-call scenarios
- 4-week norm periods, part-time pro rata

## Architecture Decisions

See [docs/knowledge-base/INDEX.md](docs/knowledge-base/INDEX.md) for the full structured decision log (ADR, PAT, DEP, RES entries).

## Sprint Execution Logs

See [docs/sprints/INDEX.md](docs/sprints/INDEX.md) for the formal sprint log with validation evidence and traceability.
