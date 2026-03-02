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

## Phase Roadmap

This roadmap uses a **rolling detail** pattern: only the next sprint has task-level planning. Future phases have milestone-level descriptions. After each sprint completes, the next sprint is promoted to detailed planning.

### Phase 1 — Rule Engine Completion + Payroll Chain (Sprints 4–5)

**Priority focus**: P2 (Deterministic rule engine), P3 (Event sourcing), P6 (Payroll integration)

The critical gap is payroll integration — infrastructure exists but the end-to-end traceability chain is disconnected. Phase 1 connects rules to payroll export and completes the absence type inventory.

- **Sprint 4** (complete): Absence completion, flex payout, PeriodCalculationService "glue", payroll export endpoint, traceability regression tests
- **Sprint 5** (complete): Flex endpoint unification, on-call duty basics, event emission + HTTP parallelization, retroactive correction foundation, SLS export format, 158 tests

### Phase 2 — Advanced Rules + Retroactive Corrections (Sprints 6–7)

**Priority focus**: P2 (Deterministic rule engine), P4 (Version correctness), P6 (Payroll integration)

Depends on the connected payroll chain from Phase 1. These sprints tackle the most complex rule domains and prove the architecture works end-to-end across time.

- On-call duty (rådighedsvagt), call-in work, travel time (working vs non-working)
- 4-week norm periods, part-time pro rata
- AC position-based rule overrides, academic/research norm systems
- Retroactive recalculation across OK version transitions
- Payroll re-export after retroactive corrections

### Phase 3 — Contract Versioning + User Management + Frontend (Sprints 8–9)

**Priority focus**: P5 (Integration isolation), P7 (Security), P9 (Usability)

Does not affect the deterministic core. Focuses on operational readiness and user-facing completeness.

- Outbound API versioned contracts
- Real user management (replace hardcoded test users with DB-backed identity)
- Frontend: registration forms, absence requests, flex dashboard, admin panels
- Calendar integration for public holidays

### Phase 4 — Production Hardening (Sprint 10+)

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

| Requirement Area | S1–S3 | S4 | S5 (Phase 1 done) | After Phase 2 | After Phase 3 | After Phase 4 |
|------------------|-------|-----|---------------------|---------------|---------------|---------------|
| A. Basic Time Registration | 80% | 80% | 85% | 90% | 95% | 100% |
| B. Working Time Rules | 70% | 72% | 75% | 95% | 95% | 100% |
| C. Time Types & Supplements | 60% | 60% | 70% | 95% | 95% | 100% |
| D. Absence Types | 65% | 80% | 85% | 90% | 95% | 100% |
| AC-Specific Requirements | 40% | 42% | 45% | 85% | 90% | 100% |
| Payroll Integration | 50% | 80% | 88% | 95% | 95% | 100% |
| External Integrations | 60% | 60% | 60% | 65% | 90% | 100% |
| **Overall** | **~61%** | **~68%** | **~73%** | **~88%** | **~94%** | **100%** |

## Sprint 5 — Completed

Sprint 5 completed Phase 1 (Sprints 4–5). See [docs/sprints/SPRINT-5.md](docs/sprints/SPRINT-5.md) for full task log.

**Key deliverables**: Flex endpoint unification (PAT-006), on-call duty basics (AC disabled, HK/PROSA at 1/3 rate), PeriodCalculationCompleted event emission, HTTP rule call parallelization (Task.WhenAll), retroactive correction foundation (models + service + endpoint), SLS pipe-delimited export formatter, 25 new tests (158 total).

**Sprint 5 backlog addressed**: All 3 Sprint 4 backlog items resolved (flex unification, event emission, HTTP parallelization).

**Sprint 6 backlog (from Sprint 5 retrospective)**:
- Add idempotency tokens for retroactive correction events (Reviewer WARNING)
- Define explicit FlexEvaluationResponse DTO in SharedKernel (Reviewer WARNING)
- Call-in work (CALL_IN_WORK), complex on-call scenarios
- 4-week norm periods, part-time pro rata

## Sprint 6 Detailed Plan

_To be planned when Sprint 6 execution is requested. Phase 2 focus: Advanced Rules + Retroactive Corrections._

## Architecture Decisions

See [docs/knowledge-base/INDEX.md](docs/knowledge-base/INDEX.md) for the full structured decision log (ADR, PAT, DEP, RES entries).

## Sprint Execution Logs

See [docs/sprints/INDEX.md](docs/sprints/INDEX.md) for the formal sprint log with validation evidence and traceability.
