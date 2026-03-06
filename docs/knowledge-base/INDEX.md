# StatsTid Knowledge Base

> **Governance**: Only the Orchestrator may create, modify, or delete entries in this knowledge base. Agents may propose new entries in their output, but the Orchestrator reviews and approves all additions.

## Architectural Decision Records (ADR)

| ID | Title | Status | Sprint | Domains | Tags |
|----|-------|--------|--------|---------|------|
| [ADR-001](decisions/ADR-001-event-sourcing-postgresql-npgsql.md) | Event sourcing with PostgreSQL via Npgsql | approved | S1 | Infrastructure, Data Model | event-sourcing, postgresql, npgsql |
| [ADR-002](decisions/ADR-002-pure-function-rule-engine.md) | Pure function rule engine with no I/O | approved | S1 | Rule Engine | rule-engine, determinism, pure-functions |
| [ADR-003](decisions/ADR-003-ok-version-resolved-by-entry-date.md) | OK version resolved by entry date, not current date | approved | S2 | Rule Engine | ok-version, determinism, version-resolution |
| [ADR-004](decisions/ADR-004-outbox-pattern-guaranteed-delivery.md) | Outbox pattern for guaranteed delivery | approved | S1 | Infrastructure, API Integration | outbox-pattern, delivery-guarantee, integration |
| [ADR-005](decisions/ADR-005-explicit-type-map-polymorphic-serialization.md) | Explicit type map for polymorphic event serialization | approved | S1 | Data Model, Infrastructure | serialization, events, type-map, system-text-json |
| [ADR-006](decisions/ADR-006-eight-service-docker-compose.md) | 8-service Docker Compose architecture | approved | S1 | Infrastructure | docker, microservices, architecture |
| [ADR-007](decisions/ADR-007-jwt-auth-rbac-correlation-ids.md) | JWT auth with RBAC and correlation IDs | approved | S3 | Security, Infrastructure | jwt, rbac, authentication, authorization, correlation-id, audit |
| [ADR-008](decisions/ADR-008-materialized-path-org-hierarchy.md) | Materialized path for organizational hierarchy | approved | S6 | Infrastructure, Security | organization, hierarchy, materialized-path, postgresql |
| [ADR-009](decisions/ADR-009-scope-embedded-jwt.md) | Role scopes embedded in JWT token | approved | S6 | Security, Infrastructure | jwt, rbac, scopes, authorization, stateless |
| [ADR-010](decisions/ADR-010-local-config-merge-at-service-layer.md) | Local config merged at service layer, not in rule engine | approved | S6 | Rule Engine, Payroll, Infrastructure | local-config, rule-engine, determinism, configuration, merge |
| [ADR-011](decisions/ADR-011-frontend-design-system-and-component-strategy.md) | Frontend design system and component strategy | approved | S8 (pre) | Frontend | frontend, design-system, shadcn, css-modules, react, accessibility |
| [ADR-012](decisions/ADR-012-two-step-approval-flow.md) | Two-step approval flow (Employee → Manager) | approved | S9 | Backend | ADR-012 |
| Frontend | ADR-011, ADR-012 | approval, workflow, state-machine, period, two-step |

## Validated Patterns (PAT)

| ID | Title | Status | Sprint | Domains | Tags |
|----|-------|--------|--------|---------|------|
| [PAT-001](patterns/PAT-001-immutable-models-init-only.md) | Immutable models with init-only properties | approved | S1 | Data Model | immutability, models, value-objects, c-sharp |
| [PAT-002](patterns/PAT-002-supplement-precedence-no-double-dipping.md) | Supplement precedence — no double-dipping | approved | S2 | Rule Engine | supplements, precedence, overtime, rule-engine |
| [PAT-003](patterns/PAT-003-agreement-config-in-memory-dictionary.md) | Agreement config as in-memory dictionary | approved | S2 | Rule Engine | agreement-config, configuration, rule-engine, ac, hk, prosa |
| [PAT-004](patterns/PAT-004-domain-events-extend-base-with-actor-tracking.md) | Domain events extend DomainEventBase with actor tracking | approved | S1+S3 | Data Model | events, domain-events, actor-tracking, audit |
| [PAT-005](patterns/PAT-005-period-calculation-service-http-rule-evaluation.md) | PeriodCalculationService HTTP rule evaluation pattern | approved | S4 | Payroll, Rule Engine | service-boundary, HTTP, traceability, payroll-chain |
| [PAT-006](patterns/PAT-006-unified-rule-endpoint-response-format.md) | Unified rule endpoint response format | approved | S5 | Rule Engine, Payroll | rule-engine, endpoint-response, deserialization, flex, payroll-chain |

## Cross-Domain Dependencies (DEP)

| ID | Title | Status | Sprint | From → To | Tags |
|----|-------|--------|--------|-----------|------|
| [DEP-001](dependencies/DEP-001-rule-engine-depends-on-sharedkernel-calendar.md) | Rule Engine depends on SharedKernel Calendar | approved | S2 | Rule Engine → SharedKernel | calendar, holidays, cross-domain, dependency |
| [DEP-002](dependencies/DEP-002-payroll-depends-on-rule-engine-outputs.md) | Payroll depends on Rule Engine output types | approved | S2 | Payroll → Rule Engine | payroll, wage-types, cross-domain, dependency |
| [DEP-003](dependencies/DEP-003-event-serializer-must-register-all-types.md) | EventSerializer must register all event types | approved | S1 | Infrastructure → Data Model | serialization, events, type-map, cross-domain, dependency |
| [DEP-004](dependencies/DEP-004-endpoint-registry-ui-api-data-alignment.md) | Endpoint registry — UI / API / Data Model alignment | approved | S10 | Frontend → Backend → All | endpoint-registry, alignment, api, frontend, data-model, traceability |

## Priority Conflict Resolutions (RES)

| ID | Title | Status | Sprint | Priorities | Tags |
|----|-------|--------|--------|------------|------|
| [RES-001](resolutions/RES-001-ac-no-overtime-supplements.md) | AC has no overtime/supplements (agreement fidelity over feature parity) | approved | S2 | P2 vs P9 | ac, overtime, supplements, priority-conflict |

## Failure/Pivot Log (FAIL)

| ID | Title | Status | Sprint | Domains | Tags |
|----|-------|--------|--------|---------|------|
| [FAIL-001](failures/FAIL-001-jwt-claim-remapping-dotnet8.md) | .NET 8 JWT claim remapping silently breaks custom claims | resolved | S9 | Security, Infrastructure | jwt, claims, dotnet8, authentication, debugging |

---

## Tag Index

| Tag | Entries |
|-----|---------|
| accessibility | ADR-011 |
| ac | PAT-003, RES-001 |
| approval | ADR-012 |
| authentication | ADR-007, FAIL-001 |
| agreement-config | PAT-003 |
| architecture | ADR-006 |
| actor-tracking | PAT-004 |
| alignment | DEP-004 |
| api | DEP-004 |
| audit | ADR-007, PAT-004 |
| authorization | ADR-007, ADR-009 |
| c-sharp | PAT-001 |
| claims | FAIL-001 |
| calendar | DEP-001 |
| configuration | PAT-003, ADR-010 |
| correlation-id | ADR-007 |
| css-modules | ADR-011 |
| debugging | FAIL-001 |
| dotnet8 | FAIL-001 |
| cross-domain | DEP-001, DEP-002, DEP-003, DEP-004 |
| delivery-guarantee | ADR-004 |
| dependency | DEP-001, DEP-002, DEP-003, DEP-004 |
| deserialization | PAT-006 |
| design-system | ADR-011 |
| determinism | ADR-002, ADR-003, ADR-010 |
| docker | ADR-006 |
| domain-events | PAT-004 |
| endpoint-registry | DEP-004 |
| endpoint-response | PAT-006 |
| event-sourcing | ADR-001 |
| frontend | ADR-011 |
| events | ADR-005, PAT-004, DEP-003 |
| flex | PAT-006 |
| hk | PAT-003 |
| HTTP | PAT-005 |
| holidays | DEP-001 |
| immutability | PAT-001 |
| integration | ADR-004 |
| jwt | ADR-007, ADR-009, FAIL-001 |
| microservices | ADR-006 |
| models | PAT-001 |
| npgsql | ADR-001 |
| ok-version | ADR-003 |
| outbox-pattern | ADR-004 |
| overtime | PAT-002, RES-001 |
| payroll | DEP-002, PAT-005 |
| payroll-chain | PAT-005, PAT-006 |
| local-config | ADR-010 |
| materialized-path | ADR-008 |
| merge | ADR-010 |
| organization | ADR-008 |
| postgresql | ADR-001, ADR-008 |
| precedence | PAT-002 |
| priority-conflict | RES-001 |
| prosa | PAT-003 |
| pure-functions | ADR-002 |
| rbac | ADR-007, ADR-009 |
| react | ADR-011 |
| rule-engine | ADR-002, PAT-002, PAT-003, PAT-006, ADR-010 |
| scopes | ADR-009 |
| stateless | ADR-009 |
| service-boundary | PAT-005 |
| serialization | ADR-005, DEP-003 |
| shadcn | ADR-011 |
| supplements | PAT-002, RES-001 |
| system-text-json | ADR-005 |
| traceability | PAT-005, DEP-004 |
| type-map | ADR-005, DEP-003 |
| value-objects | PAT-001 |
| period | ADR-012 |
| state-machine | ADR-012 |
| two-step | ADR-012 |
| version-resolution | ADR-003 |
| wage-types | DEP-002 |
| workflow | ADR-012 |

## Domain Index

| Domain | Entries |
|--------|---------|
| API Integration | ADR-004 |
| Backend | ADR-012, DEP-004 |
| Frontend | ADR-011, ADR-012, DEP-004 |
| Data Model | ADR-001, ADR-005, PAT-001, PAT-004, DEP-003 |
| Infrastructure | ADR-001, ADR-004, ADR-005, ADR-006, ADR-007, ADR-008, ADR-009, ADR-010, ADR-012, DEP-003, FAIL-001 |
| Payroll | DEP-002, PAT-005, PAT-006, ADR-010 |
| Rule Engine | ADR-002, ADR-003, PAT-002, PAT-003, PAT-005, PAT-006, DEP-001, DEP-002, RES-001, ADR-010 |
| Security | ADR-007, ADR-008, ADR-009, FAIL-001 |
| SharedKernel | DEP-001 |
