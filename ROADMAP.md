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

## Phase Roadmap

This roadmap uses a **rolling detail** pattern: only the next sprint has task-level planning. Future phases have milestone-level descriptions. After each sprint completes, the next sprint is promoted to detailed planning.

### Phase 1 — Rule Engine Completion + Payroll Chain (Sprints 4–5)

**Priority focus**: P2 (Deterministic rule engine), P3 (Event sourcing), P6 (Payroll integration)

The critical gap is payroll integration — infrastructure exists but the end-to-end traceability chain is disconnected. Phase 1 connects rules to payroll export and completes the absence type inventory.

- **Sprint 4** (complete): Absence completion, flex payout, PeriodCalculationService "glue", payroll export endpoint, traceability regression tests
- **Sprint 5** (detailed below): Flex endpoint unification, on-call duty basics, PeriodCalculationCompleted emission, retroactive correction foundation, SLS export format

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

| Requirement Area | S1–S3 | S4 | After Phase 1 | After Phase 2 | After Phase 3 | After Phase 4 |
|------------------|-------|-----|---------------|---------------|---------------|---------------|
| A. Basic Time Registration | 80% | 80% | 85% | 90% | 95% | 100% |
| B. Working Time Rules | 70% | 72% | 75% | 95% | 95% | 100% |
| C. Time Types & Supplements | 60% | 60% | 70% | 95% | 95% | 100% |
| D. Absence Types | 65% | 80% | 85% | 90% | 95% | 100% |
| AC-Specific Requirements | 40% | 42% | 45% | 85% | 90% | 100% |
| Payroll Integration | 50% | 80% | 85% | 95% | 95% | 100% |
| External Integrations | 60% | 60% | 60% | 65% | 90% | 100% |
| **Overall** | **~61%** | **~68%** | **~72%** | **~88%** | **~94%** | **100%** |

## Sprint 5 Detailed Plan

**Goal**: Complete Phase 1 by adding on-call duty basics, unifying the flex endpoint, laying retroactive correction foundations, and producing SLS-formatted payroll export. Also address Sprint 4 backlog (event emission, HTTP parallelization).

**Test target**: ~160–170 (133 existing + 25–35 new)

**Execution phases**:
1. Rule Engine + Data Model (parallel): TASK-501, TASK-502, TASK-504
2. Payroll Integration (depends on Phase 1): TASK-503, TASK-505, TASK-506
3. Test & QA: TASK-507
4. Orchestrator validates build + test

| Task | Agent(s) | Description | Est. Tests |
|------|----------|-------------|------------|
| TASK-501 | Rule Engine | Unify flex endpoint: wrap FlexBalanceResult response with CalculationResult-compatible fields (ruleId + lineItems including FLEX_PAYOUT when excess > 0). Simplify PeriodCalculationService.CallFlexRuleAsync to use standard CalculationResult deserialization instead of JsonDocument workaround. | 3–4 |
| TASK-502 | Data Model + Rule Engine | On-call duty basics: add OnCallDutyEnabled/OnCallDutyRate to AgreementRuleConfig, create OnCallDutyRule pure function (ON_CALL_DUTY time type at reduced rate, e.g. 1/3), register in RuleRegistry, update AgreementConfigProvider (HK/PROSA: enabled, AC: disabled by default). | 6–8 |
| TASK-503 | Payroll Integration | Emit PeriodCalculationCompleted event to event store after successful calculation. Register IEventStore + PostgresEventStore in Payroll DI. Parallelize independent rule HTTP calls (NORM_CHECK, SUPPLEMENT, OVERTIME + ABSENCE via Task.WhenAll, then FLEX sequentially). | 3–4 |
| TASK-504 | Data Model | Retroactive correction models: RetroactiveCorrectionRequested event (extends DomainEventBase — with OriginalPeriodStart/End, Reason, CorrectedByActorId), CorrectionExportLine model (original vs corrected amounts), register in EventSerializer. | 2–3 |
| TASK-505 | Payroll Integration | Retroactive correction foundation: POST /api/payroll/recalculate endpoint that re-runs PeriodCalculationService for a past period, compares with previous export lines, and produces correction PayrollExportLines (diff: new amount - previous amount). Correction lines carry SourceRuleId traceability. | 4–6 |
| TASK-506 | Payroll Integration | Wage type mappings for ON_CALL_DUTY time type (init.sql, all agreements × both OK versions). SLS export formatter service: converts PayrollExportLines to SLS-compatible text format (pipe-delimited, with header/trailer records, employee ID, SLS code, hours, amount, period). | 3–5 |
| TASK-507 | Test & QA | Unit + regression tests: OnCallDutyRule (AC disabled, HK/PROSA enabled, rate calculation), flex endpoint unified response, retroactive correction diff calculation, SLS format output, PeriodCalculationCompleted event emission (model test), correction event round-trip. | 8–10 |

### Sprint 5 Task Details

**TASK-501 — Flex Endpoint Unification** (Sprint 4 backlog)
The evaluate-flex endpoint currently returns `FlexBalanceResult` directly. PeriodCalculationService uses a `JsonDocument` workaround to parse it. Fix: modify the endpoint to return an anonymous object containing all FlexBalanceResult fields PLUS `ruleId = "FLEX_BALANCE"` and `lineItems` (FLEX_PAYOUT item if excess > 0, otherwise empty array). Then update PeriodCalculationService.CallFlexRuleAsync to use standard `CalculationResult` deserialization. The endpoint response remains backward-compatible (existing fields preserved).

**TASK-502 — On-Call Duty Basics**
SYSTEM_TARGET.md Section C requires on-call duty (rådighedsvagt). Sprint 5 covers the basic rule: an employee declared "on call" earns ON_CALL_DUTY hours at a reduced rate (configurable per agreement, typically 1/3 of normal). Requires:
- Add `OnCallDutyEnabled` (bool) and `OnCallDutyRate` (decimal, default 0.33m) to `AgreementRuleConfig`
- Update all 6 entries in `AgreementConfigProvider` (HK/PROSA: enabled, AC: disabled)
- Create `OnCallDutyRule` pure function in `src/RuleEngine/Rules/`
- Register in `RuleRegistry`
- Defer call-in work (CALL_IN_WORK) and complex on-call scenarios to Phase 2

**TASK-503 — Event Emission + HTTP Parallelization** (Sprint 4 backlog)
Two improvements to PeriodCalculationService:
1. After successful calculation, emit a `PeriodCalculationCompleted` event to the event store (requires registering `PostgresEventStore` and `IEventStore` in Payroll DI)
2. Parallelize the 4 independent rule calls (norm, supplement, overtime, absence) using `Task.WhenAll`, then call flex sequentially (it depends on absence results for norm credit)

**TASK-504 — Retroactive Correction Models**
Foundation for payroll re-export. A `RetroactiveCorrectionRequested` event records that a past period was re-evaluated. A `CorrectionExportLine` model extends `PayrollExportLine` semantics with `OriginalAmount`, `CorrectedAmount`, `DifferenceAmount` for the correction diff.

**TASK-505 — Retroactive Correction Service**
The recalculate endpoint re-runs `PeriodCalculationService` for a past period and produces correction lines by diffing against the previous export. This is the foundation for SYSTEM_TARGET.md's "retroactive recalculation" requirement. The correction lines carry full traceability (SourceRuleId, SourceTimeType) and can be exported via the existing PayrollExportService.

**TASK-506 — On-Call Wage Mappings + SLS Formatter**
Add `ON_CALL_DUTY` → `SLS_0710` wage type mappings (3 agreements × 2 OK versions = 6 rows). Create a `SlsExportFormatter` service that converts `IReadOnlyList<PayrollExportLine>` to SLS text format (pipe-delimited lines with header record, data records, and trailer record with checksum).

## Architecture Decisions

See [docs/knowledge-base/INDEX.md](docs/knowledge-base/INDEX.md) for the full structured decision log (ADR, PAT, DEP, RES entries).

## Sprint Execution Logs

See [docs/sprints/INDEX.md](docs/sprints/INDEX.md) for the formal sprint log with validation evidence and traceability.
