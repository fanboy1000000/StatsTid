# Sprint 1 — Foundation: Event Sourcing, Docker Skeleton, First Rule

| Field | Value |
|-------|-------|
| **Sprint** | 1 |
| **Status** | complete |
| **Start Date** | 2026-01-13 |
| **End Date** | 2026-01-17 |
| **Orchestrator Approved** | yes — 2026-01-17 |
| **Build Verified** | yes — `dotnet build` 0 warnings 0 errors |
| **Test Verified** | yes — 12 unit + 0 regression = 12 total passing |

## Sprint Goal
Establish the full technical foundation: event sourcing with PostgreSQL, 8-service Docker Compose architecture, SharedKernel domain models, first rule (NormCheck), polymorphic event serialization, outbox pattern, and initial integration scaffolding.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved
- [x] P2 — Rule engine determinism maintained (no I/O, no side effects)
- [x] P3 — Event sourcing append-only semantics respected
- [ ] P4 — OK version correctness (entry-date resolution) — _deferred to Sprint 2_
- [x] P5 — Integration isolation and delivery guarantees
- [x] P6 — Payroll integration correctness (traceability chain)
- [ ] P7 — Security and access control — _deferred to Sprint 3_
- [ ] P8 — CI/CD enforcement — _deferred to Sprint 3_
- [ ] P9 — Usability and UX — _deferred to Sprint 2_

## Task Log

### TASK-101 — Establish 8-service Docker Compose architecture

| Field | Value |
|-------|-------|
| **ID** | TASK-101 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Components** | Infrastructure |
| **KB Refs** | ADR-006 |
| **Orchestrator Approved** | yes — 2026-01-15 |

**Description**: Created Docker Compose configuration with 8 services (postgres, backend-api, rule-engine, orchestrator, payroll, external, mock-payroll, mock-external). Each service has its own Dockerfile, health checks, and startup dependencies.

**Validation Criteria**:
- [x] All 8 services defined in docker-compose.yml
- [x] Health checks configured per service
- [x] Mock services simulate external dependencies
- [x] PostgreSQL initializes via init.sql

**Files Changed**:
- `docker/docker-compose.yml` — 8-service orchestration with health checks
- `docker/postgres/init.sql` — Schema for event store, outbox, projections, wage type mappings
- `docker/mock-payroll/` — Mock payroll service (Dockerfile, Program.cs, config)
- `docker/mock-external/` — Mock external API service (Dockerfile, Program.cs, config)

---

### TASK-102 — Implement event sourcing with PostgreSQL via Npgsql

| Field | Value |
|-------|-------|
| **ID** | TASK-102 |
| **Status** | complete |
| **Agent** | Data Model, Infrastructure |
| **Components** | SharedKernel, Infrastructure |
| **KB Refs** | ADR-001, ADR-005, PAT-001, PAT-004, DEP-003 |
| **Orchestrator Approved** | yes — 2026-01-15 |

**Description**: Implemented append-only event store using PostgreSQL and raw Npgsql. Created domain event base class, event interfaces, immutable domain models, and explicit type map serialization for polymorphic JSON deserialization.

**Validation Criteria**:
- [x] Events stored as append-only rows in PostgreSQL
- [x] DomainEventBase provides EventId and Timestamp
- [x] All models use init-only properties
- [x] EventSerializer type map covers all event types
- [x] No EF Core dependency

**Files Changed**:
- `src/SharedKernel/**/Events/` — DomainEventBase, IDomainEvent, TimeEntryRegistered, NormCheckCompleted, PayrollExportGenerated, IntegrationDeliveryTracked
- `src/SharedKernel/**/Models/` — TimeEntry, EmploymentProfile, CalculationResult, PayrollExportLine, WageTypeMapping, DeliveryStatus, FlexBalance
- `src/SharedKernel/**/Interfaces/` — IEventStore, IRuleEngine, IPayrollMapper, IIntegrationGateway
- `src/Infrastructure/PostgresEventStore.cs` — Npgsql-based event persistence
- `src/Infrastructure/EventSerializer.cs` — Explicit type discriminator map
- `src/Infrastructure/DbConnectionFactory.cs` — Connection management

---

### TASK-103 — Implement NormCheck rule (first pure function rule)

| Field | Value |
|-------|-------|
| **ID** | TASK-103 |
| **Status** | complete |
| **Agent** | Rule Engine |
| **Components** | Rule Engine |
| **KB Refs** | ADR-002 |
| **Orchestrator Approved** | yes — 2026-01-15 |

**Description**: Implemented the first rule engine function — NormCheckRule — as a pure function with no I/O. Validates that weekly hours meet the 37-hour norm. Established the pattern for all future rules.

**Validation Criteria**:
- [x] Pure function — no I/O, no database access
- [x] Deterministic — same inputs always produce same output
- [x] Returns CalculationResult with norm compliance status

**Files Changed**:
- `src/RuleEngine/**/Rules/NormCheckRule.cs` — Pure norm check evaluation
- `src/RuleEngine/**/Rules/RuleRegistry.cs` — Rule registration infrastructure

---

### TASK-104 — Implement outbox pattern and integration scaffolding

| Field | Value |
|-------|-------|
| **ID** | TASK-104 |
| **Status** | complete |
| **Agent** | API Integration, Payroll |
| **Components** | Infrastructure, Integrations (External), Integrations (Payroll) |
| **KB Refs** | ADR-004, DEP-002 |
| **Orchestrator Approved** | yes — 2026-01-15 |

**Description**: Implemented transactional outbox pattern for guaranteed event delivery. Created external integration scaffolding (event consumer, delivery tracker, circuit breaker, exponential backoff, idempotency guard) and payroll integration scaffolding (wage type mapper, period export).

**Validation Criteria**:
- [x] Outbox table created in PostgreSQL schema
- [x] Events written to outbox within same transaction
- [x] Circuit breaker, backoff, and idempotency patterns in place
- [x] Payroll mapper reads rule engine outputs and maps to wage types
- [x] Delivery tracker records status per outbound message

**Files Changed**:
- `src/Infrastructure/Resilience/` — CircuitBreaker, ExponentialBackoff, IdempotencyGuard
- `src/Integrations/External/Services/` — EventConsumerService, ExternalApiClient, DeliveryTracker
- `src/Integrations/Payroll/` — Program.cs with wage type mapping and period export endpoints

---

### TASK-105 — Implement Backend API and Orchestrator services

| Field | Value |
|-------|-------|
| **ID** | TASK-105 |
| **Status** | complete |
| **Agent** | Data Model, Orchestrator |
| **Components** | Backend API, Orchestrator |
| **KB Refs** | ADR-006 |
| **Orchestrator Approved** | yes — 2026-01-15 |

**Description**: Created the Backend API service (time entry registration, retrieval, calculation endpoints) and the Orchestrator service (task dispatch, multi-service coordination). Established API contract types.

**Validation Criteria**:
- [x] POST/GET endpoints for time entries
- [x] Calculate endpoint calls rule engine
- [x] Orchestrator dispatches to downstream services
- [x] Request/response contracts defined

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Program.cs` — API endpoints
- `src/Backend/StatsTid.Backend.Api/Contracts/` — RegisterTimeEntryRequest, CalculateRequest, WeeklyCalculateRequest
- `src/Orchestrator/StatsTid.Orchestrator/` — Task dispatcher service

---

### TASK-106 — Write initial unit tests

| Field | Value |
|-------|-------|
| **ID** | TASK-106 |
| **Status** | complete |
| **Agent** | Test & QA |
| **Components** | Tests |
| **KB Refs** | ADR-002 |
| **Orchestrator Approved** | yes — 2026-01-17 |

**Description**: Created unit test project with 12 initial tests covering NormCheckRule, CircuitBreaker, PayrollMapping, and TaskDispatcher.

**Validation Criteria**:
- [x] NormCheckRule determinism tests pass
- [x] CircuitBreaker state transition tests pass
- [x] Payroll mapping tests pass
- [x] All 12 tests green

**Files Changed**:
- `tests/StatsTid.Tests.Unit/NormCheckRuleTests.cs` — Norm check rule tests
- `tests/StatsTid.Tests.Unit/CircuitBreakerTests.cs` — Resilience pattern tests
- `tests/StatsTid.Tests.Unit/PayrollMappingTests.cs` — Wage type mapping tests
- `tests/StatsTid.Tests.Unit/TaskDispatcherTests.cs` — Orchestrator dispatch tests

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified | NormCheck validates 37h/week standard norm |
| Wage type mappings produce correct SLS codes | verified | Initial mappings for NORMAL_HOURS, OVERTIME_50, OVERTIME_100, MERARBEJDE seeded |
| Overtime/supplement calculations are deterministic | N/A | Deferred to Sprint 2 |
| Absence effects on norm/flex/pension are correct | N/A | Deferred to Sprint 2 |
| Retroactive recalculation produces stable results | N/A | Deferred to Sprint 2 |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 12 | all passing |
| Regression tests | 0 | N/A |
| Smoke tests | 4 | all passing (with Docker) |
| **Total** | 12 | — |

## Sprint Retrospective

**What went well**: Full 8-service architecture established in one sprint. Event sourcing, outbox pattern, and first rule working end-to-end. Clean separation of concerns from day one.

**What to improve**: No OK version awareness yet. No security. Only one rule implemented — rule engine needs significant expansion.

**Knowledge produced**: ADR-001, ADR-002, ADR-004, ADR-005, ADR-006, PAT-001, PAT-004 (initial), DEP-003
