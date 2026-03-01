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

## Phase Roadmap

This roadmap uses a **rolling detail** pattern: only the next sprint has task-level planning. Future phases have milestone-level descriptions. After each sprint completes, the next sprint is promoted to detailed planning.

### Phase 1 — Rule Engine Completion + Payroll Chain (Sprints 4–5)

**Priority focus**: P2 (Deterministic rule engine), P3 (Event sourcing), P6 (Payroll integration)

The critical gap is payroll integration — infrastructure exists but the end-to-end traceability chain is disconnected. Phase 1 connects rules to payroll export and completes the absence type inventory.

- **Sprint 4** (detailed below): Absence completion, flex payout, PeriodCalculationService "glue", payroll export endpoint, traceability regression tests
- **Sprint 5** (milestone): On-call duty basics, weekend/holiday supplement refinement, payroll retroactive correction foundation, SLS export format

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

| Requirement Area | S1–S3 | After Phase 1 | After Phase 2 | After Phase 3 | After Phase 4 |
|------------------|-------|---------------|---------------|---------------|---------------|
| A. Basic Time Registration | 80% | 85% | 90% | 95% | 100% |
| B. Working Time Rules | 70% | 75% | 95% | 95% | 100% |
| C. Time Types & Supplements | 60% | 70% | 95% | 95% | 100% |
| D. Absence Types | 65% | 85% | 90% | 95% | 100% |
| AC-Specific Requirements | 40% | 45% | 85% | 90% | 100% |
| Payroll Integration | 50% | 85% | 95% | 95% | 100% |
| External Integrations | 60% | 60% | 65% | 90% | 100% |
| **Overall** | **~61%** | **~72%** | **~88%** | **~94%** | **100%** |

## Sprint 4 Detailed Plan

**Goal**: Connect the payroll traceability chain end-to-end and complete absence type coverage.

**Test target**: ~130–140 (103 existing + 25–35 new)

**Execution phases**:
1. Data Model + Rule Engine (parallel): TASK-401, TASK-402, TASK-406
2. Payroll Integration: TASK-403, TASK-404, TASK-405
3. Test & QA: TASK-407
4. Orchestrator validates build + test

| Task | Agent(s) | Description | Est. Tests |
|------|----------|-------------|------------|
| TASK-401 | Data Model + Rule Engine | Expand absence types: SPECIAL_HOLIDAY_ALLOWANCE, CHILD_SICK_2, CHILD_SICK_3, LEAVE_WITH_PAY distinction. Update AbsenceRule mappings and norm credit rules. | 8–10 |
| TASK-402 | Rule Engine | Automatic flex payout trigger: produce FLEX_PAYOUT CalculationLineItem when flex excess > 0 at period end. | 4–6 |
| TASK-403 | Payroll Integration | Wage type mapping seed data for new absence types (OK24+OK26, all agreements) in init.sql. | 2–3 |
| TASK-404 | Data Model + Payroll Integration | PeriodCalculationService — the missing "glue" that loads events for a period, runs all rules, maps results to wage types, and produces PayrollExportLines with full traceability (Event → Rule → WageType → ExportLine). | 5–8 |
| TASK-405 | Payroll Integration | POST /api/payroll/export endpoint triggering the full calculation-to-export chain for a given employee and period. | 2–3 |
| TASK-406 | Data Model | PeriodCalculationCompleted event + EventSerializer type map registration. | 1–2 |
| TASK-407 | Test & QA | Regression tests: full payroll chain for AC employee, HK employee, absence scenarios, flex payout, and traceability proof (every export line traces back to source event). | 5–8 |

## Architecture Decisions

See [docs/knowledge-base/INDEX.md](docs/knowledge-base/INDEX.md) for the full structured decision log (ADR, PAT, DEP, RES entries).

## Sprint Execution Logs

See [docs/sprints/INDEX.md](docs/sprints/INDEX.md) for the formal sprint log with validation evidence and traceability.
