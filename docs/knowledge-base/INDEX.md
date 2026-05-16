# StatsTid Knowledge Base

> **Governance**: Only the Orchestrator may create, modify, or delete entries in this knowledge base. Agents may propose new entries in their output, but the Orchestrator reviews and approves all additions.

## Architectural Decision Records (ADR)

| ID | Title | Status | Sprint | Domains | Tags |
|----|-------|--------|--------|---------|------|
| [ADR-001](decisions/ADR-001-event-sourcing-postgresql-npgsql.md) | Event sourcing with PostgreSQL via Npgsql | approved | S1 | Infrastructure, Data Model | event-sourcing, postgresql, npgsql |
| [ADR-002](decisions/ADR-002-pure-function-rule-engine.md) | Pure function rule engine with no I/O | approved | S1 | Rule Engine | rule-engine, determinism, pure-functions |
| [ADR-003](decisions/ADR-003-ok-version-resolved-by-entry-date.md) | OK version resolved by entry date, not current date | approved | S2 | Rule Engine | ok-version, determinism, version-resolution |
| [ADR-004](decisions/ADR-004-outbox-pattern-guaranteed-delivery.md) | Outbox pattern for guaranteed delivery | superseded by ADR-018 (S22) | S1 (never implemented) | Infrastructure, API Integration | outbox-pattern, delivery-guarantee, integration, superseded |
| [ADR-005](decisions/ADR-005-explicit-type-map-polymorphic-serialization.md) | Explicit type map for polymorphic event serialization | approved | S1 | Data Model, Infrastructure | serialization, events, type-map, system-text-json |
| [ADR-006](decisions/ADR-006-eight-service-docker-compose.md) | 8-service Docker Compose architecture | approved | S1 | Infrastructure | docker, microservices, architecture |
| [ADR-007](decisions/ADR-007-jwt-auth-rbac-correlation-ids.md) | JWT auth with RBAC and correlation IDs | approved | S3 | Security, Infrastructure | jwt, rbac, authentication, authorization, correlation-id, audit |
| [ADR-008](decisions/ADR-008-materialized-path-org-hierarchy.md) | Materialized path for organizational hierarchy | approved | S6 | Infrastructure, Security | organization, hierarchy, materialized-path, postgresql |
| [ADR-009](decisions/ADR-009-scope-embedded-jwt.md) | Role scopes embedded in JWT token | approved | S6 | Security, Infrastructure | jwt, rbac, scopes, authorization, stateless |
| [ADR-010](decisions/ADR-010-local-config-merge-at-service-layer.md) | Local config merged at service layer, not in rule engine | approved | S6 | Rule Engine, Payroll, Infrastructure | local-config, rule-engine, determinism, configuration, merge |
| [ADR-011](decisions/ADR-011-frontend-design-system-and-component-strategy.md) | Frontend design system and component strategy | approved | S8 (pre) | Frontend | frontend, design-system, shadcn, css-modules, react, accessibility |
| [ADR-012](decisions/ADR-012-two-step-approval-flow.md) | Two-step approval flow (Employee → Manager) | approved | S9 | Backend, Frontend | approval, workflow, state-machine, period, two-step |
| [ADR-013](decisions/ADR-013-retroactive-corrections-single-period-no-cascade.md) | Retroactive corrections are single-period (no cascade) | approved | S11 | Payroll Integration, Rule Engine | retroactive, corrections, flex, carryover, payroll, cascade |
| [ADR-014](decisions/ADR-014-agreement-configs-database-backed.md) | Agreement configs migrated from static code to database | approved | S12 (planned) | Infrastructure, SharedKernel, Rule Engine, Payroll, Frontend | agreement-config, database, migration, configuration, lifecycle, versioning |
| [ADR-015](decisions/ADR-015-compliance-check-result-pattern.md) | ComplianceCheckResult as separate return type from CalculationResult | approved | S16 | Rule Engine, SharedKernel | compliance, rule-engine, return-type, eu-working-time-directive |
| [ADR-016](decisions/ADR-016-temporal-period-handling.md) | Temporal period handling — PeriodPlanner + SegmentManifest + SnapshotContract (D5b reconciled S29 to add fourth pattern: export-time effective-date lookup for WTM; D5b further extended S30 to add fifth pattern: consumption-time effective-date lookup at HTTP-endpoint boundaries for entitlement-policy) | approved | S20 (D5b reconciled S29 + S30) | Rule Engine, SharedKernel, Payroll, Infrastructure, Backend | segmentation, period, planner, manifest, ok-version, replay, audit, consumption-time-lookup |
| [ADR-017](decisions/ADR-017-local-agreement-configuration-as-a-profile.md) | Local agreement configuration as a profile (replaces patch-bag) | approved | S21 | Infrastructure, Backend, SharedKernel, Frontend, Data Model | local-config, profile, configuration, effective-dating, schema, migration |
| [ADR-018](decisions/ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md) | Transactional outbox + row-version optimistic concurrency (supersedes ADR-004; amends ADR-017 D2 + D2.1; D13 added S27 / sync-in-tx projection; D14 added S29 / WTM versioned history) | approved (cycles 1-7 reviewed; D14 S29 / TASK-2910) | S22 (D13 S27, D14 S29) | Infrastructure, Backend, SharedKernel, Data Model, Payroll | outbox, transactional-outbox, row-version, optimistic-concurrency, etag, end-exclusive, migration, projection, versioned-history |
| [ADR-019](decisions/ADR-019-optimistic-concurrency-via-row-version.md) | Row-version optimistic concurrency propagated to admin-strict resources (amends ADR-018 D7; D3 amended by ADR-020 / S29) | accepted (cycles 1-3 reviewed 2026-05-07; D3 amended S28) | S25 | Infrastructure, Backend, SharedKernel, Frontend, Data Model | row-version, optimistic-concurrency, etag, if-match, admin-strict, audit-version-transition, propagation |
| [ADR-020](decisions/ADR-020-versioned-config-design-foundations.md) | Versioned-config design foundations for Phase 4d-1 — planner-level enrollment for non-rule replay inputs + soft-delete-then-create 3-case routing under If-Match + seed idempotency under accumulated history | accepted (cycles 1-2 reviewed 2026-05-09) | S28 | SharedKernel, Backend, Infrastructure, Payroll, Data Model | versioned-config, snapshot-contract, planner-enrollment, supersession, soft-delete, seed-idempotency, replay-determinism, phase-4d |
| [ADR-021](decisions/ADR-021-entitlement-policy-versioned-history.md) | Entitlement-policy versioned history for Phase 4d-2 — sibling to ADR-020 (D1 planner-enrollment does NOT transfer; D2 3-case routing + D3 seed idempotency inherit verbatim); new D4 consumption-time-lookup two-step pattern; D5 reset_month + accrual_model frozen from admin scope; D6 MONTHLY_ACCRUAL dead-code footnote; D7 soft-delete consumption contract | accepted (Step 7a cycle 1 + cycle 2 reviewed 2026-05-16) | S30 | Backend, Infrastructure, Frontend, Data Model | versioned-config, consumption-time-lookup, entitlement, supersession, soft-delete, seed-idempotency, phase-4d |
| [ADR-022](decisions/ADR-022-employee-profile-consolidation.md) | Employee-profile consolidation + pre-baked versioning for Phase 4d-3 Part 1 — sibling to ADR-020 + ADR-021 (S31 is data-plane only, zero consumer cutovers; rule-engine path stays on request-payload until S32 atomic cutover + planner-snapshot eliminates the P4 retroactive replay window). D1 data-plane-only scope; D2 surrogate UUID PK (S29 WTM precedent); D3 pre-baked versioning columns dormant in S31; D4 is_part_time column dropped (computed in repository); D5 admin CRUD + AdminEndpoints POST 4-way atomicity + audit-CREATED row; D6 OrgScopeValidator binding on both GET and PUT (Step 0b BLOCKER fix); D7 register-4-emit-2 event vocabulary; D8 seeder route over SQL-side INSERTs; D9 frontend admin page LocalHR+ only. Defers S32 commitment list to ADR-023 (per-field bucketing + planner-snapshot + ComplianceEndpoints/BalanceEndpoints/TimeEndpoints cutover + rule-engine hard-cut). | accepted (Step 7a cycle 1 + cycle 2 reviewed 2026-05-16 on gpt-5.5) | S31 | Backend, Infrastructure, Frontend, Data Model | versioned-config, employee-profile, consolidation, surrogate-uuid-pk, atomic-outbox, audit, data-plane-only, phase-4d |

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
| agreement-config | PAT-003, ADR-014 |
| architecture | ADR-006 |
| actor-tracking | PAT-004 |
| alignment | DEP-004 |
| api | DEP-004 |
| audit | ADR-007, PAT-004 |
| authorization | ADR-007, ADR-009 |
| c-sharp | PAT-001 |
| claims | FAIL-001 |
| calendar | DEP-001 |
| carryover | ADR-013 |
| cascade | ADR-013 |
| corrections | ADR-013 |
| configuration | PAT-003, ADR-010, ADR-014 |
| correlation-id | ADR-007 |
| css-modules | ADR-011 |
| database | ADR-014 |
| debugging | FAIL-001 |
| dotnet8 | FAIL-001 |
| cross-domain | DEP-001, DEP-002, DEP-003, DEP-004 |
| delivery-guarantee | ADR-004 |
| dependency | DEP-001, DEP-002, DEP-003, DEP-004 |
| deserialization | PAT-006 |
| design-system | ADR-011 |
| determinism | ADR-002, ADR-003, ADR-010 |
| docker | ADR-006 |
| lifecycle | ADR-014 |
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
| migration | ADR-014 |
| models | PAT-001 |
| npgsql | ADR-001 |
| ok-version | ADR-003 |
| outbox-pattern | ADR-004 |
| overtime | PAT-002, RES-001 |
| payroll | DEP-002, PAT-005, ADR-013 |
| payroll-chain | PAT-005, PAT-006 |
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
| retroactive | ADR-013 |
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
| versioning | ADR-014 |
| wage-types | DEP-002 |
| workflow | ADR-012 |
| compliance | ADR-015 |
| return-type | ADR-015 |
| eu-working-time-directive | ADR-015 |
| segmentation | ADR-016 |
| planner | ADR-016 |
| manifest | ADR-016 |
| replay | ADR-016 |
| profile | ADR-017 |
| effective-dating | ADR-017 |
| schema | ADR-017 |
| local-config | ADR-010, ADR-017 |

## Domain Index

| Domain | Entries |
|--------|---------|
| API Integration | ADR-004 |
| Backend | ADR-012, ADR-017, DEP-004 |
| Frontend | ADR-011, ADR-012, ADR-014, ADR-017, DEP-004 |
| Data Model | ADR-001, ADR-005, ADR-017, PAT-001, PAT-004, DEP-003 |
| Infrastructure | ADR-001, ADR-004, ADR-005, ADR-006, ADR-007, ADR-008, ADR-009, ADR-010, ADR-012, ADR-014, ADR-016, ADR-017, DEP-003, FAIL-001 |
| Payroll | DEP-002, PAT-005, PAT-006, ADR-010, ADR-013, ADR-014, ADR-016 |
| Rule Engine | ADR-002, ADR-003, PAT-002, PAT-003, PAT-005, PAT-006, DEP-001, DEP-002, RES-001, ADR-010, ADR-014, ADR-015, ADR-016 |
| SharedKernel | ADR-015, ADR-016, ADR-017 |
| Security | ADR-007, ADR-008, ADR-009, FAIL-001 |
| SharedKernel | DEP-001, ADR-014, ADR-015 |
