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
| [ADR-023](decisions/ADR-023-employee-profile-versioning-emission-and-rule-engine-cutover.md) | Employee-profile versioning emission + rule-engine cutover architecture (Phase 4d-3 Part 2 Design). Binding contract for S33 implementation. D1 PCS consumption-site = PeriodCalculationService.cs:326-339 (per-segment segmentProfile construction inside existing loop, BEFORE EvaluateSegmentAsync); ADR-020 D1 seam inherited literally — no amendment. D2 REVERSES cycle-1 MIGRATE absorption: agreement_code + employment_category STAY on users (LIVE read in resolver); determinism gap documented as **Phase 4e LAUNCH-BLOCKING** (was "candidate" — strengthened post-cycle-1 review when LocalAdminOrAbove scope detail surfaced). D3 fail-closed for PCS-routed null snapshots + fallback-to-existing-chain for non-rule-engine HTTP consumers. D4/D5/D7 NOT NEEDED under D2 reversal (no backfill, no commit-gate, no User-model cascade). D6 DELETE dead TimeEndpoints `/calculate*` endpoints (no live caller). D8 enumerates ~11 S33 tasks: EmploymentProfileResolver creation (cross-project plumbing — Codex cycle 1 W2 absorbed) + SupersedeAndCreateAsync (ADR-020 D2 3-case) + SoftDeleteAsync (predecessor version UNCHANGED — Reviewer NOTE absorbed) + admin DELETE endpoint + PCS/Compliance/Balance cutovers + UserAgreementCodeChanged event (55→56; Phase 4e replay-data trail). **Third canonical thrash-defer case** per `feedback_thrash_defer_real_world.md` (S28 = 1st; S31 = 2nd control case; S32 = 3rd). S33 = implementation sprint. | accepted (TASK-3202 cycle 1 dual-lens reviewed 2026-05-16 on gpt-5.5; cycle 2 NOT requested — mechanical absorption clean) | S32 (design-only) | Backend, Infrastructure, Frontend, Data Model, Payroll Integration, SharedKernel | versioned-config, employee-profile, planner-snapshot, rule-engine-determinism, consumption-time-lookup, phase-4d, design-binding |
| [ADR-024](decisions/ADR-024-role-within-agreement-modeling.md) | Role-within-agreement modeling + correction policy + classification governance. D1 `employment_category` drives `role_config_overrides` (6 boolean disablers + tri-state `merarbejde_compensation_right` + quantitative overrides); D2 merarbejde right CONTRACTUAL/DISCRETIONARY/NONE; D6 generalized `ConfigBugCorrected`; D7 overtime authorization model (post-hoc necessity-acknowledgment). Companion to ADR-025; ADR-013 amended separately. | ACCEPTED | S38 | Backend, Infrastructure, Data Model, SharedKernel, Rule Engine, Payroll Integration | role-within-agreement, employment-category, role-config-override, merarbejde-compensation-right, bug-correction-policy, classification-governance, overtime-authorization, phase-4e |
| [ADR-025](decisions/ADR-025-multi-tenant-operational-concerns.md) | Multi-tenant operational concerns for the single-logical-deployment SaaS (~150 institutions). D1-D6 + D8 cover per-tenant SLS, customer onboarding, GDPR erasure, noisy-neighbor fairness, cross-tenant reporting, per-tenant feature flags, and the explicit Institution type. D7 (audit-visibility) DEFERRED to ADR-026 at the cycle-3 halt-and-prompt. | ACCEPTED-WITH-D7-DEFERRED (D7 → ADR-026) | S38 | Infrastructure, Backend, Frontend, Security, Payroll Integration | multi-tenant, saas-operations, per-tenant-sls, customer-onboarding, gdpr, noisy-neighbor-fairness, cross-tenant-reporting, feature-flags, institution-type, phase-4e |
| [ADR-026](decisions/ADR-026-audit-visibility-surface.md) | Audit visibility surface (Path C: event-projection per ADR-018 D13). New `audit_projection` table + 3-tier `visibility_scope`; endpoint-direct dispatch; ~53-mapper `IAuditProjectionMapper` family; scope-by-target per-event declaration. Settles the ADR-025 D7 deferral. | ACCEPTED (supersedes ADR-025 D7) | S38b | Backend, Infrastructure, Security, Data Model, Frontend | audit-visibility, tenant-scoping, event-projection, sync-in-tx-projection, audit_projection, scope-by-target, phase-4e |
| [ADR-027](decisions/ADR-027-reporting-line-hierarchy.md) | Reporting-line hierarchy complementing ADR-008 org hierarchy. D1 single temporal table (ADR-017 D1 pattern); D2 tree boundary per MINISTRY/STYRELSE; D3 PRIMARY + ACTING relationships; D4 manager-preferred routing (not authorization); D5 fallback traversal; D6 acting manager (vikarierende leder); D7 HR import (Phase 2); D8 four-phase migration; D9 root invariant; D10 event types; D11 enforcement model (S50); D12 self-service delegation (S51); **D13 designated-edge approve-AUTHORITY within the styrelse (S74 — amends D4, the routing→authority P7 expansion bounded by the single-effective-approver predicate + same tree-root); D14 approver-owned vikar + resolver vikar-consult (S74 — manager_vikar, amends D5/D12); D15 write-time integrity: cycle guard + atomic create+assign + no-orphan delete + concurrency model (S74 — extends D1/D9); D16 enhed_label + ManagerVikarCreated/Ended events + ReportingLineSelfDelegated retire-from-emission (S74 — amends D10)**. | accepted | S48 (Phase 1); S49–S52, S74 (Phase 5 — Medarbejder administration) amendments | Infrastructure, Backend, Frontend, Data Model, Security | reporting-line, manager, approval-routing, approve-authority, temporal, hierarchy, vikarierende-leder, acting-manager, vikar |
| [ADR-028](decisions/ADR-028-work-time-persistence-allocation-gate-timer-retirement.md) | Self-recorded work-time persistence + allocation reconciliation gate + timer retirement. D1 event-sourced `WorkTimeRegistered` + latest-wins `work_time_projection` (ADR-018 D13 sync-in-tx, outbox_id guard, backfill); D2 two additive entry rows (Tilføj periode intervals + Tilføj timer numeric, Danish comma); D3 "Diff. fra normtid" uses real per-employee norm (WeeklyNorm×fraction/5, dated resolver, OkVersionResolver, ANNUAL_ACTIVITY→blank, pure read no rule engine); D4 HARD allocation gate at employee-approve (discriminated 422, NORMAL+non-null-TaskId allowlist, absences excluded, <0.005 tolerance, both directions); D5 timer write path removed (endpoints/repo/model/timer_sessions table) but TimerCheckedIn/Out events RETAINED for replay. | accepted | S56 | Backend, Infrastructure, Data Model, Frontend | work-time, projection, event-sourcing, allocation-gate, approval, timer-retirement, norm, latest-wins |
| [ADR-029](decisions/ADR-029-per-employee-entitlement-eligibility-and-dob-senior-age-gate.md) | Per-employee entitlement eligibility (child-sick) + DOB-derived senior-day age gate. D1 two distinct mechanisms; D2 event-sourced `employee_entitlement_eligibility` (CHILD_SICK only, opt-in default, dated/versioned, ADR-026 audit, HR-set) + partial-unique live index; D3 senior age gate via new `users.birth_date` → rule-engine contract extension (`MinAge` + `EmployeeAgeAsOfAbsenceDate`, age-gate before per-episode/quota, per-absence-row, null-DOB fail-closed); D4 enforcement split (child-sick = Backend fact gate GET/POST; senior age = rule engine = agreement config); D5 GET(month-end)/POST(absence.Date) anchors differ by design; D6 precedence over dormant ADR-024 (per-employee can only further-restrict); D7 DOB GDPR (amends ADR-025 D3, erasure deferred-with-D3); D8 no production migration. Reconciles `DefaultEntitlementConfigs` senior drift 0/60→2/62. | accepted | S59 | Data Model, Infrastructure, Backend, Rule Engine, Frontend, Security | entitlement-eligibility, child-sick, senior-day, age-gate, date-of-birth, opt-in, event-sourcing, gdpr, rule-engine-determinism, adr-024-precedence, adr-025-amendment |
| [ADR-030](decisions/ADR-030-monthly-vacation-accrual-activation.md) | Activate monthly vacation accrual (MONTHLY_ACCRUAL). D1 compute earned-to-date as a PURE rule-engine fn at the ADR-021 D4 boundary (no scheduler/events/table/migration; total_quota stays "annual"); D2 scope VACATION + SPECIAL_HOLIDAY via sentinel-row reseed (all 5 agreement codes, preserves D5 invariant); D3 three quantities (earnedToDate/available/bookableLimit) + CheckAndAdjustAsync guardCap(carryover-excl)/seedQuota(annual) split; D4 forskudsferie — VACATION dynamic ferieår cap by manager-approval-as-§7-agreement, SPECIAL_HOLIDAY no-forskud (ferieaftale §13 stk.4), both enforcement points, warning/per-episode on annual; D5 determinism (pure fn of asOf — Skema firstAbsenceDate / Balance month-end); D6 new `users.employment_start_date` (HR-managed, non-dated pure input, null→full-ferieår fallback NOT fail-closed) for mid-year pro-ration; D7 §8 wage-deduction + §7 termination modregning OUT of scope (payroll follow-up) — **completed (designed) by ADR-033 (S67): §21 transfer / §24 auto-payout / §26+§7 termination / §34 forfeiture / §15 stk.2+§17 godtgørelse; the §8 label reframed onto §7 udligning; LBK 152/2024**. D8 (added S62) piecewise per-month fraction accrual — **superseded by ADR-031 (S63)**; D1–D7 stand. D9 (added S65; **AMENDED S66 post-close**) period-end disposition projection: boundary-computed flat formula off the CLOSED entitlement year, displayed at boundaryMonth 12 (owner OQ-1, Ferielov §21 stk.2 anchor) — **amended from "Kan overføres" (under-cap) to the complement `expiring = max(0, raw − carryoverMax)` with per-category labels: VACATION/CARE_DAY/SENIOR_DAY "Til udløb" (lapse; VACATION identity ≡ the Feriefonden-lost first-4-weeks figure), SPECIAL_HOLIDAY "Til udbetaling" (2½% godtgørelse)** — explicitly NON-equivalent to the deferred D7 settlement (§21 transfer / §24 auto-payout / §26+§7 termination — **designed in ADR-033, S67**); records the særlige-feriedage calendar-year/1 May–30 Apr model-vs-law gap (Cirkulære 021-24 §12 stk.2). Supersedes ADR-021 D6; annotates D5. | accepted (D8 superseded by ADR-031) | S60 (D8 S62, D9 S65 am. S66) | Rule Engine, Data Model, Infrastructure, Backend, Frontend, Security | vacation-accrual, monthly-accrual, ferieloven, samtidighedsferie, forskudsferie, earned-to-date, pure-rule-fn, employment-start-date, adr-021-d6-supersede, payroll-deferred |
| [ADR-031](decisions/ADR-031-vacation-daycount-parttime-independent.md) | Vacation day-count is part-time-fraction-independent (supersedes ADR-030 D8; preserves D1–D7). D1 flat 25/5 day-count per Ferieloven §5 stk.1 regardless of `part_time_fraction` (fraction affects §6 stk.2 consumption + monetary value only — never the day-count; state circular Medst. 021-24 §3 follows); D2 `ProRateByPartTime=false` (config factory + all 20 seed rows) + flat `EarnedToDate(…, 1.0m, …)` at all 3 call-sites — ADR-030 D6 mid-hire months-elapsed pro-ration PRESERVED via employmentStart threading; D3 the S62 piecewise surface (`EarnedToDatePiecewise`/`FractionPeriod`/`GetFractionHistoryAsync` + compile-bound tests) removed as dead code (S61 consolidation + `/summary` + `/series` stay); D4 anchor profile-missing 422 retained deliberately (accrual-window rationale — `fractionMatters` stays true via `isMonthlyAccrual`), the S62 empty-history 422 removed with its fetch (anchor is a strict superset); D5 bug-with-no-past-impact (ADR-024 D3) → sentinel reseed + type-keyed idempotent UPDATE (S60 pattern), no recompute; D6 §6 stk.2 consumption + `work_days_per_week` deferred to ADR-032 — **LAUNCH-BLOCKING, event-bound** (interim over-entitlement of <5-day/week workers is latent pre-launch, unrepresentable until ADR-032 adds the field; **gate disposition settled by ADR-032 D6 (S66): the 5÷N premise was refuted against primary sources — the gate is satisfied by ADR-032 D1–D3 landing, no field added**). | accepted (D6 gate → ADR-032) | S63 | Rule Engine, Data Model, Infrastructure, Backend, Test | vacation-accrual, ferieloven, part-time, day-count, flat-accrual, samtidighedsferie, adr-030-d8-supersede, bug-with-no-past-impact, launch-blocking-adr-032 |
| [ADR-032](decisions/ADR-032-vacation-consumption-correctness.md) | Vacation consumption correctness — satisfies the ADR-031 D6 launch gate with its premise corrected on the record (the "5÷N per day off" mechanism refuted against primary sources: LBK 230/2021 §6 stk.2 = week-mirroring; Ferievejledning Ex. 3.5 = hours ÷ that-day's-hours; NO `work_days_per_week` field — research: `docs/references/vacation-consumption-mechanism-research.md`, owner-ratified 2026-06-07). D1 canonical day basis `hours ÷ fullDayHours(e,d)` via DailyNormCalculator, ALL entitlement-consuming types, classified bug-with-no-past-impact (ADR-024 D3); D2 per-absence recorded feriedage as the authoritative consumption record (`AbsenceRegistered.Feriedage` + `absences_projection.feriedage`; single-valuation identity with two-phase pre-lock-advisory/in-lock-authoritative mechanics; entry-date OkVersion stamping; ProjectionBackfillService parity); D3 norm-based per-day guard with type scoping (weekend entitlement-422; ANNUAL_ACTIVITY `7.4×fraction` fallback; anchor-422 unchanged; FE empty-cell norm prefill); D4 profile-change revaluation (fraction OR position trigger; `EntitlementBalanceRevalued` per-absence replacement sets on the `employee-{id}` stream; pinned employee-scoped `pg_advisory_xact_lock` concurrency contract; ADR-026 audit mapper); D5 `POST /api/absences` bypass retired (GET retained — `WeeklyCalculationPipeline` cross-process caller; harness suites migrated per-scenario); D6 the §6 stk.2 adjudication + launch-gate satisfaction + binding schedule-shape forward-pointer; D7 scope fences (shape/holidays/SPECIAL_HOLIDAY-calendar-model/payroll untouched). | accepted | S66 | Data Model, Infrastructure, Backend, Frontend, Test | vacation-consumption, ferieloven, norm-based-day-basis, recorded-valuation, advisory-lock, bypass-retirement, launch-gate, adr-031-d6-satisfied |
| [ADR-033](decisions/ADR-033-vacation-settlement-architecture.md) | Vacation-settlement architecture — the period-end execution layer completing ADR-030 D7 (design sprint S67; implementation S68+ slices). D1 money stays out of StatsTid — day/hour-count wage-type lines, SLS owns kroner (the verified SLS day-count is særlige-godtgørelse-only; per-slice SLS-contract gates; the §7-cap unverifiable-as-bare-day-count); D2 the verified §-spine (transfer §21 / auto-payout §24 / feriehindring §22+§25 / termination §26+§7 modregning-capped / forfeiture §34 / særlige godtgørelse Cirk. §15 stk.2+§17; LBK 152/2024); D3 deterministic idempotent period-close BackgroundService (DelegationExpiryService shape; immutable input snapshot; boundary-timezone the only new P2/P4 surface); D4 service ownership — Backend closes+events / Payroll consumes via a new emitter with a consumer-checkpoint+line exactly-once keyed on (identity, sequence, bucket); reversal = compensating entry not rollback, ADR-013 no-auto-cascade; D5 ONE atomic settlement per (employee,type,year) as a STATE MACHINE (settlement_state PENDING_REVIEW/SETTLED/REVERSED + sequence + trigger; composite PK + partial-unique single-active; event vocabulary §21/§22/§25/§24/§34/godtgørelse/termination/reversal/manual-review); D6 the first non-zero carryover_in writer — provenance-keyed additive §21+§22 total, capped; D7 payouts/deductions-only lines (transfers balance-only) via a new period-close emitter, ADR-020-dated wage_type_mappings; D8 dedicated vacation_transfer_agreements record; D9 users.employment_end_date + §26/§7 termination + audit/GDPR/auth + the EarnedToDate whole-month granularity fork; D10 §34 forfeiture + §22 feriehindring fail-closed to PENDING_REVIEW (no wrongful forfeiture); D11 særlige calendar-year model correction in slice 2; D12 settlement GLOBAL (no per-institution override); D13 phased roadmap (slices 1 ferie-5th-week / 2 særlige / 3 termination / 4 feriehindring), pre-launch settlement is manual-fallback, none go-live-blocking. **Impl: S68 = slice 1a** (the Backend close machinery — `SettlementCloseService` + `vacation_settlements` state machine + the §21 first-non-zero-`carryover_in` writer + the 9-event family + D10 manual FORFEIT/DEFER; D3 go-live gate; D6 disposition-on-the-row; B1 enforces VACATION `reset_month=9`. §24 Payroll emitter + line = S69 slice 1b; B2 terminated-employee access deferred to slice 3). | accepted (impl S68 slice 1a) | S67 | Backend, Payroll Integration, Data Model, Infrastructure, Frontend, Security | vacation-settlement, period-close, ferieloven, §21-transfer, §24-payout, §26-§7-termination, godtgørelse, settlement-state-machine, exactly-once, wage-type-lines, money-free, adr-030-d7-completes |
| [ADR-034](decisions/ADR-034-payroll-export-lock.md) | Payroll-export lock + monthly-export idempotency (Phase 2 of "reopen until sent to payroll"; S89 = Phase 1 leader-reopen FE gate). D1 ONE Payroll-owned `payroll_export_records` table (UNIQUE (employee_id,year,month) + nullable period_id + original_lines/current_effective_lines JSONB + content_hash) = lock + idempotency + corrections-manifest; D2 atomic `ExportAsync` refactor (one (conn,tx): record + `PayrollExportGenerated` to `outbox_events` + ADR-026 audit row; lock = "durable handoff record written" at COMMIT [OQ-1], NOT "delivered" — no payroll `outbox_messages` dispatcher exists, the sync mock POST stays best-effort post-commit; PayrollExportGenerated REUSED [reshaped, replay-safe] + made a real audited event); D3 content-hash idempotency (same month+same hash → no-op, different → 409 "use a correction"; multi-period `/export-period` → one record per (employee,month); zero-line month doesn't lock); D4 cross-context READ contract — Backend reopen READS the Payroll table (never writes), the export↔reopen race closed by both taking the approval-row FOR UPDATE; D5 reopen post-lock → discriminated 409 for ALL roles (OQ-2 corrections-only, no recall), `/recalculate` reads+evolves `current_effective_lines` (sequential corrections diff against the latest, not the original). Disjoint from ADR-033 settlement tables. Step-0b dual-lens caught 4 BLOCKERs pre-code. | accepted | S90 | Payroll Integration, Backend, Data Model, Infrastructure, Frontend | payroll-export, export-lock, idempotency, reopen, corrections-manifest, cross-context-read, event-sourcing, monthly-time |
| [ADR-035](decisions/ADR-035-flat-authority-model.md) | Flat authority model — org-tree inheritance removed from BOTH role-scope and approval authority (owner pivot 2026-06-22; umbrella sliced S92→S95). D1 authority follows an EXPLICIT Organisation-SET, never the org tree (keep bounding, remove inheritance; P7 intact). D2 the org taxonomy flattens to **Organisation** (mandatory home, the single authority unit) + **Enhed** (flexible user-metadata tags, NOT an authority boundary) + **MAO** (ministry wrapper); map MINISTRY→MAO, STYRELSE→Organisation, AFDELING+TEAM→Enhed. D3 "same Organisation" = flat attribute equality is the ONE containment rule (vikar bound; HR/Admin fallback over the employee's Organisation; OQ4 retire the unfloored leader-by-org-scope approval branch; OQ8 explicit-grant-only new orgs; OQ6 retire REQUIRED-mode in S94). D4 phasing: S92 org-model flatten → S93 flat role-scope (drop ORG_AND_DESCENDANTS) → S94 flat approval (`CanApprove` edge OR HR/Admin-over-Org) → S95 retire tree machinery (re-key locks per the refinement's lock matrix) → later the Organisation admin page. D5 the S92 slice = the flatten + transitional tree-root re-point + the INTENDED afdeling→Organisation coarsening (Enhed holds no authority; rename identity-preserving, level-collapse widens-or-equal never narrows). D6 the ADR-027 D2/D9/D11/D13/D15/D18/D19 disposition table (transitional/kept/retired staged across S92–S95). Narrows ADR-008 (materialized path) + ADR-009 (scope-in-JWT). | accepted (umbrella; impl S92 slice 1) | S92 | Security, Infrastructure, Backend, Data Model, Frontend | flat-authority, org-taxonomy, organisation, enhed, role-scope, approval-authority, same-organisation, adr-027-supersede, tree-root-retirement, greenfield |

## Validated Patterns (PAT)

| ID | Title | Status | Sprint | Domains | Tags |
|----|-------|--------|--------|---------|------|
| [PAT-001](patterns/PAT-001-immutable-models-init-only.md) | Immutable models with init-only properties | approved | S1 | Data Model | immutability, models, value-objects, c-sharp |
| [PAT-002](patterns/PAT-002-supplement-precedence-no-double-dipping.md) | Supplement precedence — no double-dipping | approved | S2 | Rule Engine | supplements, precedence, overtime, rule-engine |
| [PAT-003](patterns/PAT-003-agreement-config-in-memory-dictionary.md) | Agreement config as in-memory dictionary | approved | S2 | Rule Engine | agreement-config, configuration, rule-engine, ac, hk, prosa |
| [PAT-004](patterns/PAT-004-domain-events-extend-base-with-actor-tracking.md) | Domain events extend DomainEventBase with actor tracking | approved | S1+S3 | Data Model | events, domain-events, actor-tracking, audit |
| [PAT-005](patterns/PAT-005-period-calculation-service-http-rule-evaluation.md) | PeriodCalculationService HTTP rule evaluation pattern | approved | S4 | Payroll, Rule Engine | service-boundary, HTTP, traceability, payroll-chain |
| [PAT-006](patterns/PAT-006-unified-rule-endpoint-response-format.md) | Unified rule endpoint response format | approved | S5 | Rule Engine, Payroll | rule-engine, endpoint-response, deserialization, flex, payroll-chain |
| [PAT-007](patterns/PAT-007-referentially-stable-hook-mocks.md) | Referentially-stable hook mocks for components with data-identity effects | approved | S65 | Frontend, Test | vitest, react-testing, mocking, useeffect, referential-stability, worker-oom |
| [PAT-008](patterns/PAT-008-fixed-timeprovider-waf-regression-tests.md) | FixedTimeProvider pattern for WAF-hosted today-dependent regression tests | approved | S65 | Test, Backend | timeprovider, regression-tests, webapplicationfactory, determinism, wall-clock, boot-order |
| [PAT-009](patterns/PAT-009-null-collapsing-resolver-presence-discriminator.md) | Null-collapsing shared resolver + presence discriminator — downstream consumers re-resolve only the cheap discriminator (never the collapsed value) when a shared seam intentionally merges two null-causes; guardrail: promote to a discriminated result type if the split recurs | approved | S66 | Backend, Infrastructure | shared-seam, resolver, null-semantics, drift-proof, consumption |

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
| [FAIL-002](failures/FAIL-002-docker-desktop-testcontainer-churn-flakes.md) | Docker Desktop sheds testcontainer starts under sustained churn | resolved | S65 | Test, CI/Tooling | docker-desktop, testcontainers, regression-suite, flake, consecutive-runs, close-protocol |

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
