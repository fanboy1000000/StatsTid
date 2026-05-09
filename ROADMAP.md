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
| Sprint 13 | Employee Experience: Unified "Min Tid" | Balance summary endpoint, BalanceSummary component, SkemaPage integration | 387 |
| Sprint 14 | Position Override + Wage Type Mapping UI | DB-backed position overrides, WageTypeMapping CRUD, 2 admin pages, 12 endpoints | 406 |
| Sprint 15 | Entitlement & Balance Management | 5 entitlement types, quota validation, atomic balance adjustment, progress bars | 422 |
| Sprint 16 | Working Time Compliance (EU WTD) | RestPeriodRule (4 checks), compliance config chain, ComplianceWarnings UI, ADR-015 | 436 |
| Sprint 17 | Overtime Governance & Compensation Model | OvertimeGovernanceRule, OvertimeBalance, pre-approval workflow, compensation-aware payroll mapping, 9 endpoints, 4 frontend changes | 446 |
| Sprint 18 | Codex BLOCKER Remediation (Round 1) | OK-version enforcement at write + payroll boundary, wage-type `position = ''` convention, role-scoped orchestrator/payroll/recalculate endpoints, dev JWT fallback fail-fast, UserUpdated EventSerializer registration + reflection coverage test, 9 OK-version runtime regression tests, 6 Testcontainers wage-type tests | 474 |
| Sprint 19 | Codex BLOCKER Remediation (Round 2) | Orchestrator `/execute` resource-scope with task-type-aware ExtractEmployeeId, payroll `/calculate-and-export` per-org scope via OrgScopeValidator, OkVersionCanonicalization helper for retroactive audit, JWT honors DOTNET_ENVIRONMENT; TASK-1903 absorbed into S20 | 493 |
| Sprint 20 | Temporal Period Handling | ADR-016 D1-D11, SharedKernel.Segmentation bounded context, planner-driven PCS, segment_manifests projection, classifications endpoint, retired OkVersionBoundary + RecalculateWithVersionSplitAsync, per-line OK stamping at export boundary | 562 |
| Sprint 21 | Local Agreement Configuration Rework | ADR-017 D1-D11 profile model, partial-unique-index lifecycle, big-bang migration, ConfigEndpoints rewrite with ETag/If-Match, ProfileAlignmentValidator, BoundaryCause.LocalProfileActivation hydration, ProfileEditor frontend, Step 7a 10-cycle review | 618 |
| Sprint 22 | Transactional Outbox + Row-Version Optimistic Concurrency | ADR-018 D1-D12, `outbox_events` + `schema_migrations` ledger + `version BIGINT` on profiles + end-exclusive `effective_to` + audit-action MODIFIED, `IOutboxEnqueue` split-interface (SharedKernel stays Npgsql-free), `PostgresEventStore : IEventStore, IOutboxEnqueue` dual-binding, `OutboxPublisher` BackgroundService with per-stream FIFO + 4-way cross-stream parallelism, `LocalAgreementProfileRepository` UPDATE-in-place same-day routing, `ConfigEndpoints` PUT atomic exemplar (version-as-ETag + three-way audit + in-tx EnqueueAsync), frontend ETag helpers, Option C scope narrowing (TASK-2206 deferred), TASK-2208 D12 16 Docker-gated scenarios, Step 7a 3-cycle review (P2 same-day response + P1 FIFO fixed; cycle-3 cascade halted per discipline) | 650 |
| Sprint 23 | Phase 4b Publisher Hardening | Outbox `MaxAttempts=10` cap + first-crossing warn-log; `OutboxCorrelationParser` three-state pure helper replacing inline `Guid.TryParse` ternary, log-on-fail with full breadcrumb; `resolveEtag(headerValue, body)` with strict runtime guard (`typeof === 'number' && Number.isSafeInteger && >= 1`) wired in `getCurrentProfile` + `saveProfile`; same-day no-op short-circuit relocated INSIDE `LocalAgreementProfileRepository.SupersedeAndCreateAsync` AFTER lock + `ValidatePrecondition` per Codex Step-0b BLOCKER (`SaveProfileResult` record with backward-compat 2-arg `Deconstruct` overload); 412 recovery `GetCurrentOpenAsync` wrapped in narrow try/catch (PostgresException + NpgsqlException only); case-insensitive `[Ww]/` weak-ETag prefix strip; 3 deferred D12 tests (concurrent same-stream FIFO, sustained-load 50 rows × 4 streams, migration backfill idempotency); `InternalsVisibleTo("StatsTid.Tests.Unit")` added to Infrastructure.csproj for unit-testable helpers. Step 0b Codex plan-mode review (1 cycle, 1B/3W/2N — BLOCKER on Item 4 fixed before code). 525 unit + 35 plain regression + 61 Docker-gated + 76 frontend vitest. | 697 |
| Sprint 24 | Phase 4c Part 1: Atomic Outbox Site Propagation | TASK-2206 redo — 23 `(NpgsqlConnection, NpgsqlTransaction)` overloads added across 7 in-scope state-change repositories (`ApprovalPeriod`, `AgreementConfig`, `PositionOverride`, `WageTypeMapping`, `OvertimePreApproval`, `OvertimeBalance`, `Timer`), with `AppendAuditAsync(conn, tx, ...)` overloads on the 4 audit-bearing (Pattern B) repos. `AgreementConfigRepository.PublishAsync` refactored: self-managed entry routes through `(conn, tx)` overload via `BeginTransactionAsync`; in-tx overload returns `(Guid? ArchivedId, bool Published)` tuple (Step 7a cycle 1 P1 fix — disambiguates "published with no prior ACTIVE" from "concurrent change moved target out of DRAFT"). 21 endpoint sites converted to atomic in-tx pattern across 6 endpoint files (5 Approval + 5 AgreementConfig + 4 PositionOverride + 3 WageTypeMapping + 2 Overtime + 2 Timer). AGENTS.md gained Cross-Domain Authorization sub-section formalizing the convention S22 used (TASK-2205, TASK-2206) — closes the systematic governance gap Codex flagged in Step 0b. ForcedRollbackHarness + 21 forced-rollback Docker-gated regression tests via TASK-2408 + 23 Docker-gated tx-contract tests via TASK-2401. Step 0b Codex 2 cycles (1 cycle 2B/3W/1N → cycle 2 B2 fixed + B1 governance edit). Step 7a Codex 2 cycles (cycle 1 P1 publish-race fix + cycle 2 P2 post-commit-tolerance regression fix). 525 unit + 35 plain regression + 105 Docker-gated + 76 frontend vitest. | 741 |
| Sprint 25 | Phase 4c Part 2: D2.2 ETag/If-Match Propagation | ADR-019 D1-D8 propagates ADR-018 D7's row-version + If-Match contract from `LocalAgreementProfileRepository` exemplar onto 3 admin-strict mutating resources: `agreement_configs` (DRAFT update + publish + archive lifecycle), `position_override_configs` (update + activate/deactivate flat-CRUD + 23505 vs 412 distinction), `wage_type_mappings` (update + DELETE-204 with composite key). `entitlement_configs` schema-only forward-compatibility. Schema migration `s25-d2-2-version` (Parts A+B) adds `version BIGINT NOT NULL DEFAULT 1` on 4 state tables + `version_before/version_after BIGINT NULL` on 3 audit tables. Per-surface SaveResult records (`SaveAgreementConfigResult` + `ArchivedId/ArchivedVersion` for publish-supersession, `SavePositionOverrideResult`, `SaveWageTypeMappingResult`); shared `EtagHeaderHelper.TryParseIfMatch` admin-strict mode (rejects `If-None-Match: *`); 412 stale + 428 missing + 409 disjoint per RFC 6585/7232. Frontend `apiFetchWithEtag<T>` extension on `frontend/src/lib/api.ts` + 3 admin hooks migrated to `WithEtag<T>` per-row shape + 4 admin pages with banner-with-retry mirroring `ProfileEditor.tsx` precedent (D5 coexistence with `profileApi.ts` legacy sibling-module pattern preserved). 23 Docker-gated D-tests (8 stale + 8 missing + 3 ETag-cycle + 3 audit version-transition + 1 migration idempotency); ForcedRollbackHarness DDL inlined post-2508 (closes latent runtime gap surfaced by TASK-2508 deviation). Step 7a Codex 2 cycles (cycle 1 B1 publish-supersession dual-emission fix per ADR-019 D1 + Codex P2 412-before-409 in publish/archive endpoints + Reviewer W1 catch-block ordering swap; cycle 2 verified clean). 525 unit + 35 plain regression + 129 Docker-gated + 88 frontend vitest. | 777 |
| Sprint 26 | Phase 4c.5: Atomic Outbox Final Sweep (with Phase 4c.6 carry-forward) | Sweeps remaining state-change-emitting endpoints into ADR-018 D3 atomic single-tx pattern. Phase 1 plumbing (TASK-2601 ADR-018 D6 stream-naming retabulate matching code reality + comment cleanup; TASK-2602 net-new `OvertimePreApprovalApproved` + `OvertimePreApprovalRejected` event types per PAT-004; EventSerializer 45→47; TASK-2603 (a) `EntitlementBalanceRepository.CheckAndAdjustAsync(conn, tx) + GetByEmployeeAndTypeAsync(conn, tx)` overloads; TASK-2603 (b) `OvertimePreApprovalRepository.UpdateStatusAsync(conn, tx)` overload — convergent BLOCKER fix from refinement cycle 2). Phase 2 (TASK-2605a Admin prototype on OrganizationCreated L143 with mandatory Reviewer signoff before TASK-2605b; TASK-2605b 5 remaining Admin sites in 2 sub-shapes — full-tx-wrap on org/user create-update + narrower variant for role grant/revoke moving emission inside existing tx; TASK-2607 Overtime approve/reject Pattern C atomic + new-event emission). **TASK-2604 (Skema) + TASK-2606 (Time) REVERTED at Step 7a cycle 1** — atomic-tx broke read-your-write on event-stream-backed GETs; pre-implementation reviews missed the GET-path constraint. B3 fix at cycle 1: `CheckAndAdjustAsync` two-statement pattern (ensure-row INSERT + TOCTOU-safe quota-checked UPDATE) distinguishes missing-row from breach. TASK-2608 D-test suite (~7 D-tests; 2 removed in revert; +2 B3 D-tests). **Cycle 2 finding deferred to Phase 4c.6**: pre-existing post-validation quota race silently swallowed at `SkemaEndpoints.cs:414-418` (200 OK + inconsistent state) is the load-bearing reason Phase 4c.6 sequences BEFORE Phase 4d; projection-table redesign satisfies the read-your-write + atomic-rollback + clean-state trichotomy. 525 unit + 35 plain regression + 134 Docker-gated + 88 frontend vitest. | 782 |

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
- Phase 3f (Compliance, Entitlements & Overtime Governance): Sprints 15–17
- Phase 3g (UI/UX Refinements): Sprint 18
- Phase 4 (Production): Sprint 19+ (was 18+, then 15+, then 14+, then 12+, then 11+)

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

### Phase 3f — Compliance, Entitlements & Overtime Governance (Sprints 15–17)

**Priority focus**: P2 (Deterministic rule engine), P4 (Version correctness), P6 (Payroll integration)

Addresses gaps identified in ontology analysis (2026-03-09). These are correctness requirements — without them the system can produce results that violate legal constraints or lack necessary balance tracking for accurate payroll export.

**New SYSTEM_TARGET.md sections**: J (Working Time Compliance), K (Entitlement & Balance Management), L (Overtime Governance), M (Compensation Model)

- **Sprint 15** — Entitlement & Balance Management
  - Entitlement model: annual quotas for vacation (25 days, ferieår Sep–Aug), special holiday days, care days (2/year), senior days (age-based), child sick days (per-episode)
  - Entitlement configuration per agreement (quota, accrual model, reset date, carryover max, part-time pro-rate)
  - Balance tracking: entitlement used/remaining/planned, carryover from previous year
  - Validation: absence registration rejected or warned when quota exceeded
  - Balance summary endpoint extended with entitlement data
  - Norm reduction: vacation days reduce period norm correctly (days × daily norm hours)
  - DB tables: `entitlements`, `entitlement_balances`, `entitlement_config`

- **Sprint 16** — Working Time Compliance
  - Rest period validation rule: 11-hour daily rest, weekly rest day
  - `MaxDailyHours`, `MinimumRestHours`, `RestPeriodDerogationAllowed` on AgreementRuleConfig
  - `WeeklyMaxHoursReferencePeriod` for 48h/week EU directive ceiling
  - NormCheckRule extended with daily limit validation
  - Compliance warnings surfaced in Skema UI (employee) and approval dashboard (leader)
  - Compensatory rest tracking when derogation is used

- **Sprint 17** — Overtime Governance & Compensation Model
  - Afspadsering as explicit concept: separate from flex, with conversion rates
  - Overtime balance (separate from flex balance): accumulated, reduced by afspadsering or payout
  - `DefaultCompensationModel`, `EmployeeCompensationChoice`, `MaxOvertimeHoursPerPeriod` on config
  - `OvertimeRequiresPreApproval` flag (workflow gate, not rule engine)
  - New wage type mappings: OVERTIME_50_PAYOUT, OVERTIME_50_AFSPADSERING, OVERTIME_100_PAYOUT, OVERTIME_100_AFSPADSERING, MERARBEJDE_PAYOUT, MERARBEJDE_AFSPADSERING
  - Leader dashboard: overtime exceeded warnings, pre-approval tracking

### Phase 3g — Codex BLOCKER Remediation (Sprints 18–19)

**Priority focus**: P2 (Deterministic rule engine), P4 (Version correctness), P6 (Payroll integration), P7 (Security), P3 (Event sourcing)

**Re-prioritized (Tier 2, 2026-04-18 and 2026-04-23)**: Sprint 18 was originally Phase 3g "UI/UX Refinements." The Codex external review ([`docs/reviews/codex-2026-04-18.md`](docs/reviews/codex-2026-04-18.md)) surfaced 4 BLOCKERs and 6 WARNINGs against core priorities P2–P7. Stabilizing correctness before a UX polish pass is the right order — P9 (Usability) must not compromise higher priorities. Sprint 18's own Step 7a Codex review (2026-04-23) then surfaced 2 new BLOCKERs and 3 WARNINGs — genuine scope-enforcement regressions in the S18 remediation (role-level auth treated as per-org scoping). Sprint 19 extends Phase 3g for Round 2 remediation ([docs/sprints/SPRINT-19.md](docs/sprints/SPRINT-19.md)). UI/UX Refinements + Production Hardening shift forward by one sprint each.

**Scope**: Five remediation tasks targeting the highest-impact Codex findings (Recs #1, #3, #4, #6, #8). No new features.

- **TASK-1801** — OK-version resolution enforcement at write + payroll boundary. Server-resolve `OkVersion` from entry/absence date; reject or override caller-supplied mismatches. Fixes Codex BLOCKER #4 (priorities 2–4). Agent: Rule Engine + API Integration. Effort: M.
- **TASK-1802** — Wage-type mapping lookup fix. Reconcile `position NOT NULL DEFAULT ''` schema with `IS NULL` query semantics; pass actual `profile.Position` through payroll mapping paths. Fixes Codex BLOCKER #6 (priorities 5–6). Agent: Payroll Integration + Data Model. Effort: M.
- **TASK-1803** — Role-scope orchestrator / payroll / recalculate endpoints. `recalculate` → admin-only; export → internal/admin-scoped; orchestrator execution not reachable by employee tokens. Fixes Codex BLOCKER #7 (priority 7). Agent: Security. Effort: S.
- **TASK-1804** — EventSerializer coverage test. Reflect over `DomainEventBase` descendants and fail the test if any are missing from `EventSerializer._eventTypeMap`. Add `UserUpdated` to the map (currently missing). Fixes Codex WARNING on dim. 3 (priorities 3, 8). Agent: Test & QA + Data Model. Effort: S.
- **TASK-1805** — OK-version runtime regression tests. Cover backend registration, weekly calculation, and payroll split/replay paths — not just the resolver utility. Validates TASK-1801. Recs #8 (priorities 2–4, 8). Agent: Test & QA. Effort: M.

**Explicitly deferred to S19 or later**:
- Codex BLOCKER #5 (Outbox delivery — payroll background consumer + external transactional claim). Effort L, 2–3 sprints. Plan in S19 detail.
- Codex Rec #2 (Remove Infrastructure dep from RuleEngine). Effort M. P1 concern but isolated; schedule S19 or S20.
- Codex Rec #7 (CI expansion — smoke + vitest). Effort S. Schedule S19.
- Codex Rec #9 (Governance drift-check CI step). Effort S. S19 or later.
- Codex Rec #10 (Split AdminEndpoints/TimeEndpoints). Effort L. Phase 4.

#### Impact Assessment (Tier 2 Re-prioritization, 2026-04-18)

**Affected sprints**:
- S18 (was: Phase 3g UI/UX Refinements) → Now: Phase 3g Codex BLOCKER Remediation
- S19 (was: Phase 4 start) → Now: Phase 3h UI/UX Refinements
- S20+ (was: continuation of Phase 4) → Now: Phase 4 Production Hardening start

**Scope changes**:
- UI/UX Refinements scope unchanged — shifted forward by one sprint (S18 → S19)
- Phase 4 shifted forward by one sprint
- No sprint needs splitting or merging
- No new prerequisites introduced beyond what Codex findings surfaced

**Updated phase-sprint ranges**:
- Phase 3f (Compliance, Entitlements & Overtime Governance): Sprints 15–17 (unchanged)
- Phase 3g (Codex BLOCKER Remediation): Sprint 18 ← new
- Phase 3h (UI/UX Refinements): Sprint 19 ← was Phase 3g S18
- Phase 4 (Production Hardening): Sprint 20+ ← was Sprint 19+

**Rationale**: CLAUDE.md priority order mandates lower priorities never compromise higher ones. Codex surfaced concrete P2–P7 gaps that would be written into production if shipped behind a UX polish sprint. Remediation must come first.

#### Impact Assessment (Tier 2 Re-prioritization, 2026-04-23 — S19 Round 2)

**Trigger**: S18 Step 7a Codex review (2026-04-23) surfaced 2 BLOCKERs + 3 WARNINGs. User-approved exit deferred all 5 findings to Sprint 19 rather than iterating Codex a third cycle.

**Affected sprints**:
- S18 (Phase 3g Round 1): complete with deferred findings recorded in the External Review section
- S19 (was: Phase 3h UI/UX Refinements) → Now: Phase 3g Round 2 Codex Remediation — the 5 deferred findings
- S20 (was: Phase 4 start) → unchanged content — an already-drafted "Temporal Period Handling" architectural sprint ([docs/sprints/SPRINT-20.md](docs/sprints/SPRINT-20.md)) remains in S20. This sprint generalizes the effective-date-boundary pattern surfaced by TASK-1801 + TASK-1903.
- S21 (was: Phase 4 continuation) → Now: Phase 3h UI/UX Refinements — shifted forward
- S22+ (was: Phase 4 continuation) → Now: Phase 4 Production Hardening — shifted forward

**Scope changes**:
- S19 scope: 5 remediation tasks (TASK-1901 … TASK-1905). No new architectural ground. Two P7 scope-enforcement fixes (TASK-1901, TASK-1902) dominate effort.
- UI/UX Refinements scope unchanged — shifted forward by one sprint (S19 → S21)
- Phase 4 shifted forward by one sprint (S20+ → S22+)
- Temporal Period Handling (S20) remains in place and is thematically continuous with the OK-version work in S18/S19

**Updated phase-sprint ranges**:
- Phase 3g (Codex BLOCKER Remediation): Sprints 18–19 ← was Sprint 18 only
- Phase 3h (Temporal Period Handling, architectural): Sprint 20 ← was planned standalone, now formally the next phase
- Phase 3i (UI/UX Refinements): Sprint 21 ← was Phase 3h S19
- Phase 4 (Production Hardening): Sprint 22+ ← was Sprint 20+

**Rationale**: Two of the 5 findings are genuine P7 scope-enforcement holes (not just UX polish). Deferring BLOCKERs through UX or architectural sprints would ship known scope-bypass endpoints to production. S19 closes them first; S20's architectural Temporal Period Handling then generalizes what S18/S19 tackled tactically; only then does S21 (UI/UX) fit the priority order.

#### Impact Assessment (Tier 2 Re-prioritization, 2026-04-25 — Local Config Rework + UX deferred to launch)

**Trigger**: User analysis of the local agreement configuration UX (2026-04-25) confirmed the implementation is patch-shaped (per-key rows with no uniqueness) where the intended model is profile-shaped (one local agreement profile per `(org, agreement, OkVersion)` with an editable subset of fields). User direction: add the rework as a dedicated sprint after S20, and push UI/UX Refinements to the absolute end so it polishes a stable post-hardening surface rather than a moving target.

**Affected sprints**:
- S21 (was: Phase 3i UI/UX Refinements) → Now: Phase 3i Local Agreement Configuration Rework — analysis-first, structurally similar to S20
- S22+ (was: Phase 4 Production Hardening) → unchanged in content and starting position
- UI/UX Refinements: moved to **Phase 5**, final pre-launch sprint, sprint number TBD once Phase 4's actual length is known

**Scope changes**:
- New Phase 3i sprint defined: drained the S21 slot of UI/UX work and replaced it with the local-config rework
- Phase 4 unchanged
- New Phase 5 introduced for the deferred UX polish — placed last in the rolling-detail roadmap with no fixed sprint number (assigned when Phase 4 closes)

**Updated phase-sprint ranges**:
- Phase 3g (Codex BLOCKER Remediation): Sprints 18–19 (unchanged)
- Phase 3h (Temporal Period Handling): Sprint 20 (unchanged)
- Phase 3i (Local Agreement Configuration Rework): Sprint 21 ← repurposed
- Phase 4 (Production Hardening): Sprint 22+ (unchanged)
- Phase 5 (UI/UX Refinements): final pre-launch sprint, TBD ← new, replaces the previous Phase 3i UX content

**Rationale**: The local-config implementation does not match the intended model (one profile per agreement with an editable field subset). Today's flat patch table allows multiple conflicting active rows per key and gives admins no UX signal about which fields are adjustable. This is an admin-correctness gap, not just polish — it belongs in Phase 3 alongside the other correctness sprints (S18/S19/S20). UX polish then naturally moves to Phase 5 where it operates on a stable, hardened surface; this also avoids re-doing UX work as Phase 4 introduces monitoring widgets, performance instrumentation, and other surface-affecting changes.

### Phase 3g Round 2 — Codex BLOCKER Remediation (Sprint 19)

**Priority focus**: P7 (Security) — resource scope enforcement, P4 (Version correctness) — export-boundary mixed-version handling, P3 (Auditability) — retroactive event canonicalization

**Scope**: Four remediation tasks targeting the 2026-04-23 Codex findings. No new features. The fifth finding (mixed-version export boundary, originally TASK-1903) was folded into S20 on 2026-04-25 — see decision note below. Full task detail in [docs/sprints/SPRINT-19.md](docs/sprints/SPRINT-19.md).

- **TASK-1901** — Orchestrator `/execute` resource-scope validation. Reject when caller's scope does not cover `parameters.employeeId`. Agent: Security + API Integration. Effort: M.
- **TASK-1902** — `/api/payroll/calculate-and-export` per-org scope validation (or escalate to GlobalAdminOnly). Plus policy-wiring test addressing internal-Reviewer WARNING. Agent: Security + Payroll. Effort: M.
- **TASK-1903** — *Absorbed into S20 (2026-04-25)*. The mixed-version export boundary symptom is one specific call site of the general temporal-segmentation problem S20 generalises. Pre-production status removes the live-exposure pressure that would have justified a tactical S19 patch.
- **TASK-1904** — Canonicalize OkVersion in single-version `RetroactiveCorrectionRequested` audit event. Agent: Payroll + Data Model. Effort: S.
- **TASK-1905** — JWT dev-fallback honors both `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT` via `IHostEnvironment.IsDevelopment()`. Agent: Security. Effort: S.

### Phase 3h — Temporal Period Handling (Sprint 20)

**Priority focus**: P1 (Architectural integrity), P2 (Deterministic rule engine), P4 (Version correctness)

Analysis-first architectural sprint (ADR + task decomposition before implementation). Generalizes "calculation periods that intersect effective-date boundaries" — OK version transitions, agreement-config promotion, position-override effective dates, wage-type revisions, compliance-rule versioning, employee-profile changes. S18's TASK-1801 and S19's TASK-1903 tackle this reactively for OK version only; S20 produces the general solution. See [docs/sprints/SPRINT-20.md](docs/sprints/SPRINT-20.md) for the problem framing.

### Phase 3i — Local Agreement Configuration Rework (Sprint 21)

**Priority focus**: P1 (Architectural integrity), P7 (Security — admin-facing correctness), P9 (Usability — admin form discoverability)

Restructures local configuration from a flat "one value at a time" patch bag into a profile model: **one local agreement profile per `(org, agreementCode, OkVersion)`**, with the overridable subset of fields exposed as editable inputs and the rest pinned to central. Today's `local_configurations` table allows multiple active rows per `(org, key)` with no uniqueness constraint and the UX gives admins no signal about which fields are adjustable. See [docs/sprints/SPRINT-21.md](docs/sprints/SPRINT-21.md) for the full problem statement and open architectural questions.

Analysis-first sprint (ADR + task decomposition before implementation), structurally similar to S20.

### Phase 4 — Production Hardening (Sprints 22–~29)

**Priority focus**: All priorities — cross-cutting production readiness.

Only makes sense once functional correctness is achieved. The final UX polish pass (Phase 5) follows production hardening so layout, performance, and instrumentation are settled before visual lockdown.

Functional coverage at Phase 4 entry: **~96%** (unchanged from end of S17 — S18-S22 added zero functional coverage; all five were correctness/hardening sprints). The Phase 4 backlog is thus structured as **pattern-propagation sub-sprints** rather than open-ended hardening work. Each sub-sprint has a named architectural exemplar in production already.

#### Phase 4a — Transactional Outbox + Row-Version Atomic Exemplar (Sprint 22) — COMPLETE

S22 (committed `a278f34` on 2026-05-05) delivered ADR-018: `IOutboxEnqueue`/`OutboxPublisher` split-interface design, row-version optimistic-concurrency token replacing S21's profile_id-as-ETag, end-exclusive `effective_to` semantics, UPDATE-in-place same-day routing with `MODIFIED` audit-action. `ConfigEndpoints` PUT is the atomic exemplar — single `(conn, tx)` carries profile mutation + audit row + outbox enqueue, committing together. The 12 other state-change endpoints + 8 repos still post-commit `AppendAsync` per S22's Option C scope narrowing — Phase 4c propagates the exemplar.

Step 7a Codex cycle 1 fixed P2 (same-day in-place response echoed wrong CreatedBy/CreatedAt) → cycle 2 fixed P1 (per-stream FIFO violated under transient publish failure) → cycle 3 hit the cycle-cap discipline with 2 P2 cascade findings routed to Phase 4b.

#### Phase 4b — Publisher Hardening (Sprint 23) — COMPLETE

S23 (committed across `427d2ed`/`672edff`/`cb5916d`/`950f2fe`/`5cddc41` on 2026-05-06) absorbed all S22 Step-7a cycle-3 cascade findings + Reviewer WARNINGs/NOTEs deferred per cycle-cap discipline. Key items closed:

- **Outbox max-attempts cap** (Reviewer WARN-2 / Codex P3) — `OutboxPublisher.ReadBatchAsync` lacks `WHERE attempts < @maxAttempts`, so permanently-broken rows hot-loop forever. The `idx_outbox_attempts` partial index already exists for the predicate. Add the cap + an ops-dashboard query for stuck rows.
- **Correlation_id parse robustness** (Reviewer WARN-4) — `OutboxPublisher.cs:343-346` `Guid.TryParse` on TEXT body silently returns `DBNull.Value` on bad input, losing the audit-chain link. Either log a warning or store TEXT consistently across `events` + `outbox_events`.
- **Frontend CORS ETag fallback** (Codex cycle-3 P2) — `frontend/src/api/profileApi.ts:73-75` `res.headers.get('ETag')` returns null in cross-origin deployments unless the API exposes the header. Fall back to `data.version` from the response body. Sibling backend fix: `Access-Control-Expose-Headers: ETag` when CORS middleware is fully configured.
- **Same-day no-op short-circuit** (Codex cycle-3 P2) — when `changedFields.Count == 0 && isInPlaceEdit`, `ConfigEndpoints` PUT or `LocalAgreementProfileRepository.SupersedeAndCreateAsync` should return the predecessor unchanged with the existing version + ETag, rather than bumping version + emitting MODIFIED audit/event with empty `changedFields`.
- **Reviewer NOTEs absorbed**: NOTE-1 (412 fallback robustness — wrap recovery `GetCurrentOpenAsync` in try/catch), NOTE-3 (weak ETag validators `W/"5"` parsed as null in `parseVersionFromETag`), NOTE-4 (D12 coverage gaps: concurrent enqueue across two endpoints competing for same stream_id; sustained publisher load above ~6 rows; backfill on already-shifted rows).

#### Phase 4c — Site Propagation: TASK-2206 redo + D2.2 ETag propagation (~2 sprints, sibling pair)

Both share S22's row-version + end-exclusive design — copy-and-adapt work, not new architecture.

- **TASK-2206 redo (Sprint 24) — COMPLETE**: 7-repo `(conn, tx)` overload refactor + 21 endpoint-site conversion to use S22's single-tx atomic pattern. Touched `ApprovalPeriodRepository`, `AgreementConfigRepository`, `PositionOverrideRepository`, `WageTypeMappingRepository`, `OvertimePreApprovalRepository`, `OvertimeBalanceRepository`, `TimerSessionRepository` plus the 6 endpoint files that consume them. (`EntitlementBalanceRepository` was originally the 8th repo but excluded — its only event-emitting consumer is `SkemaEndpoints.cs:439` deferred to Phase 4c.5; the overload travels with Skema there.) Atomic exemplar consumed; reference shape on `worktree-agent-a9b76f8d1f88717ff` was NOT used (rolled-back broken atomicity per S22 cycle 6).
- **D2.2 ETag/If-Match propagation (Sprint 25) — COMPLETE**: ADR-019 D1-D8 wired the row-version + If-Match contract on the 3 admin-strict mutating resources (`agreement_configs`, `position_override_configs`, `wage_type_mappings`); `entitlement_configs` schema-only forward-compatibility. Each mutating endpoint enforces admin-strict `If-Match` via `EtagHeaderHelper.TryParseIfMatch` → 412 stale / 428 missing / 409 disjoint (PositionOverride 23505 partial-unique-index distinct from 412 per D6). Per-surface SaveResult records carry the structured save outcome; `SaveAgreementConfigResult` carries `ArchivedId` + `ArchivedVersion` so the publish endpoint emits the second (ARCHIVED) audit row + `AgreementConfigArchived` outbox event for supersession (D1 — restored Step 7a cycle 1 BLOCKER fix). Audit tables gain `version_before` + `version_after` columns (D8). Frontend `apiFetchWithEtag<T>` extension + 3 hooks + 4 admin pages with banner-with-retry mirroring `ProfileEditor.tsx`. `profileApi.ts` legacy sibling-module pattern preserved per D5. Step 7a Codex 2 cycles: cycle 1 absorbed B1+P2+W1 fixes; cycle 2 verified clean.

#### Phase 4c.5 — Remaining Backend.Api Atomic Propagation (1 sprint, S24 carry-forward)

Captures the post-commit `eventStore.AppendAsync` sites that S24 explicitly excluded — same atomic pattern as S24, different surfaces. Sprint number TBD (assigned when Phase 4c closes).

- **`SkemaEndpoints.cs` L369/L393/L439** (3 sites) + **`EntitlementBalanceRepository` `(conn, tx)` overload** (Cycle 2 alignment). L369/L393 are event-only writes (`TimeEntryRegistered`, `AbsenceRegistered`); L439 is `EntitlementBalanceAdjusted` AND an `EntitlementBalanceRepository.CheckAndAdjustAsync` balance mutation in one flow. The mixed shape — pure event writes + balance-mutation in the same handler — is why S24 (Codex Cycle 1 BLOCKER on the S24 refinement) excluded Skema: a multi-event + balance-mutation atomic-boundary redesign needs its own sprint slot. The `EntitlementBalanceRepository` `(conn, tx)` overload travels here too (Cycle 2 WARNING) because Skema is its only event-emitting consumer; doing the overload during the Skema redesign keeps it test-covered in the same sprint.
- **`AdminEndpoints.cs`** (6 sites — L143/L211/L322/L401/L559/L669). Org and user state changes via `OrgRepository` and `UserRepository`. Same pattern as S24, just different repos that weren't enumerated in S22's original "8 repos" scoping.
- **`TimeEndpoints.cs`** (2 sites — L71, L163). `TimeEntryRepository`. Same as Admin.
- **`OvertimeEndpoints.cs:244/271` silent-state-change bug** (carry-forward, pre-existing). `UpdateStatusAsync` (APPROVED at L244, REJECTED at L271) mutates pre-approval state with NO follow-up event emission. Fixing requires net-new event types (`OvertimePreApprovalApproved`, `OvertimePreApprovalRejected`) plus the standard atomic pattern. Effort dominated by the new-event-type plumbing, not the pattern propagation.
- **Stream-naming drift adjudication** (Codex Cycle 1 WARNING on S24 refinement). ADR-018's stream-ownership table documents `timer-session-*`, `time-entry-*`, `skema-*`; current code writes `timer-*` (`TimerEndpoints.cs:56,112`), `employee-*` (`TimeEndpoints.cs:70,162`, `SkemaEndpoints.cs:143,348`). Renaming streams retroactively breaks replay determinism — resolution likely amends ADR-018 spec to match code, but adjudication belongs with the Time/Skema work.

#### Phase 4c.6 — Read-Path Projection Tables for Skema/Time/Balance/Compliance (Sprint 27) — COMPLETE

S27 (committed `aa6ad2d` on 2026-05-09 atop `c5edf52`) delivered ADR-018 D13 (sync-in-tx projection as canonical pattern for atomic-outbox migration on event-stream-backed-read endpoints) + the projection-table layer (`time_entries_projection` + `absences_projection`) + atomic-outbox re-attempt on Skema/Time POSTs (re-doing S26 TASK-2604 + TASK-2606 reverts) + `SkemaQuotaBreachException → 422` restoration with whole-bundle-rollback semantic (deferred from S26 Step 7a cycle 2 P1).

11 sprint tasks + 2 Step 7a fix commits = 17 commits. 525 unit + 35 plain regression + 147 Docker-gated (134 pre-S27 + 13 net new) + 88 frontend vitest = 795 total. Marquee architectural-fix proof: TASK-2710 publisher-stall RYW D-test (Slot 4) verified tight on all 4 cycle-3 invariants — FAILS on S26-revert baseline, PASSES post-S27.

Carried forward to Phase 4e: Step 7a P1 #2 (composite ordering for backfill bridging S22 boundary; doesn't fire under pre-launch posture; production-readiness fix).

#### Phase 4c.6 — Original entry (preserved for historical context — S26 Step 7a B1+B2+cycle-2-P1 carry-forward)

S26 reverted TASK-2604 (Skema atomic) + TASK-2606 (Time atomic) at Step 7a cycle 1 because the atomic-outbox migration broke read-your-write on event-stream-backed-read endpoints. Root cause: S22's `ConfigEndpoints` exemplar succeeded because reads come from the `local_agreement_profiles` projection table; for `SkemaEndpoints` / `TimeEndpoints` / `BalanceEndpoints` / `ComplianceEndpoints`, **there is no projection table — reads come straight from `events` via `IEventStore.ReadStreamAsync`**. After atomic-outbox migration, POST writes only to `outbox_events`; the `OutboxPublisher` BackgroundService drains to `events` asynchronously (typically <1s but unbounded). User saves and immediately refreshes → stale data until publisher catches up.

**Phase 4c.6 also fixes a pre-existing data-consistency bug surfaced by S26 Step 7a cycle 2** at `SkemaEndpoints.cs:414-418` (the `if (!success) continue;` branch). Pre-S26 behavior: when the Rule Engine HTTP pre-validation passes but a concurrent save consumes the entitlement before `CheckAndAdjustAsync` runs, the post-validation `(false, currentUsed)` returns silently `continue`s — the `AbsenceRegistered` events stay committed, the balance is not adjusted, no `EntitlementBalanceAdjusted` event is emitted, and the request returns 200 OK with inconsistent state. TASK-2604 attempted to fix this by wrapping events + balance + emit in a single tx with `SkemaQuotaBreachException` → 422, but the atomic-tx broke read-your-write. Phase 4c.6's projection-table redesign supports BOTH atomic-tx semantics AND synchronous read-your-write — the only correct resolution for the trichotomy.

Phase 4c.6 builds the prerequisite projection tables before re-attempting atomic outbox on those surfaces:
- New tables: `time_entries_projection` + `absences_projection` (consolidated `employee_*` stream consumers). `entitlement_balances` already exists as a projection.
- Repository pattern: read methods routed to projections; existing event-stream reads stay only for replay/audit paths.
- Skema GET reads from projections instead of `events.ReadStreamAsync`. BalanceEndpoints summary similarly. ComplianceEndpoints reads through projections.
- Once projections in place AND read-paths migrated, re-attempt atomic-outbox migration on the 5 sites (`SkemaEndpoints.cs:369/393/439`, `TimeEndpoints.cs:71/163`).
- Restore S26-style atomic-rollback-on-quota-breach (`SkemaQuotaBreachException` → 422) at `SkemaEndpoints.cs:414-418` — the post-validation race now produces 422 + clean state instead of silent 200 OK + inconsistent state.
- Document the canonical pattern: "atomic-outbox requires a synchronous projection table for any GET that reads back the just-written state." Add to ADR-018 D6 footnote.

Effort estimate: ~2 sprints (1 for projection tables + read-path migration; 1 for re-attempted atomic-outbox migration with the projections in place). Sprint number TBD; **sequenced BEFORE Phase 4d** because the post-validation quota race is a real data-consistency bug that should not ship to production unfixed (S26's revert restored it; Phase 4c.6 is the proper fix; Phase 4d's versioned-history work is independent and lower priority).

#### Phase 4d — Versioned History for Non-Dated Boundary Sources (3 sub-sprints, S20 Q5b carry-forward)

Completes the temporal-segmentation correctness story for the three sources S20 left as snapshot-at-calculation. Each source moves from "snapshot the current row at calculation time, embed in segment manifest" to "look up the row effective on segment date." Existing manifests remain replayable; new manifests use the historical lookup. No retroactive recomputation of past calculations. **Hard dependency**: S20's `SnapshotContract` framework + segment manifest must be in place (it is).

- **Phase 4d-1: Wage-type-mapping versioned history** — add `effective_from` / history-row semantics to `wage_type_mappings`. Originally projected as the simplest of the three sub-sprints; refinement at `.claude/refinements/REFINEMENT-s28-phase-4d1.md` exposed structural concerns (3-cycle review trail with same-area BLOCKERs at deeper architectural layers — recognized as thrash per `feedback_thrash_defer_real_world.md`). Split 2026-05-09 into:
  - **Sprint 28 (ADR-020 design-only)**: settles (1) the SnapshotContract trigger mechanism for non-rule replay inputs (today `PeriodPlanner` only creates `SegmentSnapshot` if any rule has a `SnapshotContract`; `RuleRegistry` registers none — wiring WTM natural-key into manifest needs a planner-level enrollment, fake/sentinel rule, or new contract type); (2) the soft-delete-then-create endpoint contract under S25 If-Match (DELETE and POST are separate committed transactions with different optimistic-concurrency stances; cross-request "reopen" semantics need a unified design); (3) seed idempotency under accumulated WTM history (the proposed `ON CONFLICT (natural_key) WHERE effective_to IS NULL DO NOTHING` doesn't catch conflicts against the `(natural_key, effective_from)` history-unique index when no open row exists). Pure design pass; produces ADR-020; minimal/zero code commits.
  - **Sprint 29 (Phase 4d-1 implementation)**: implements against ADR-020-settled design. Original Phase 4d-1 scope (effective-dating + supersession + replay-determinism marquee). Folds in the S20 carry-forward "WTM snapshot for full replay determinism" (`MapSegmentToExportLinesAsync` currently reads live DB during export-line stage).
- **Phase 4d-2: Entitlement-policy versioned history** — same pattern; admin-managed config table. Pairs with `entitlement_configs` from Phase 4c.
- **Phase 4d-3: Employee-profile versioned history** — hardest. The `employees` table is read by every rule, has many fields, has self-service + HR + leader edit paths. Versioning means every edit creates a history row; UI surfaces "as of date" semantics; per-field decision (e.g., display name → last-write-wins; agreement code, hours fraction, employment category → versioned). May itself decompose into multiple sprints.

#### Phase 4e — General Hardening (~1-2 sprints)

- Performance profiling and optimization
- Monitoring, alerting, health checks
- Real SLS integration (replacing mock)
- Load testing, stress testing
- Security audit, penetration testing
- Documentation and operational runbooks
- **S20 small-task absorbtion**: non-OK boundary-source hydration in `BuildPlanForLegacyCallers` (agreement-config / position-override / EU-WTD lists currently empty extension points); `StampAuditContext` wiring through Backend → Payroll proxy when audit chain reaches Payroll; retire the last `[Obsolete] CalculateAsync` call site at `/calculate-and-export`; `FlexBalanceRule` chained-carry custom delegate (currently `MergeStrategy.Custom` falls back to `FallbackCustomMerge`); `TestFixtures.RuleSet` drift-detection test against `HttpRuleClassificationProvider`.
- **S21 test-harness debt**: tests #11/#12/#16 hand-roll PUT pipeline (could use `WebApplicationFactory<Program>`); reflection on private methods in tests #8-10/#14 (future small task with `[InternalsVisibleTo]`); `ProfileTestSchema.cs` / `LegacyProfileSchema.cs` DDL drift vs `init.sql` is silent today.
- **S25/S26/S27 polish**: S25 W2/W3/N1-N4 deferred items; S26 NOTE follow-ups (`currentState` shape, `apiFetchWithEtag` body-parse symmetry, `[Obsolete]` markers, OCE reuse cross-cite comments, harness DDL drift assertion, frontend 13 pre-existing tsc errors, DI parameter naming `dbFactory` → `connectionFactory` in AdminEndpoints, reset_month carryover transfer in ensure-row INSERT); S27 W2 BalanceEndpoints 3-read consistency (split projection + flex reads into separate calls — minor regression vs single-snapshot semantics, acceptable per pre-launch); S27 TASK-2710 W1 (Slot 3 Skema bundle-rollback should also assert via HTTP-surface POST + 422); S27 TASK-2710 W2 (Slot 2 race test needs Task.Yield/Barrier to force interleaving); S27 TASK-2710 N1 (extract shared TestJwtFactory helper); S27 N2 (TimeEntry.RegisteredAt → projection OccurredAt mapping; pre-existing carry-forward).
- **S27 Step 7a P1 #2 (production-readiness)**: backfill `stream_version` fallback for pre-S22 events (when no matching `outbox_events` row) writes per-stream-monotonic into the global-per-service `outbox_id` column; subsequent `ORDER BY outbox_id` reads can interleave pre-S22 + post-S22 events out of true chronological order. Does NOT fire under S27 pre-launch posture (Assumption #1: no production data; all events post-deploy are post-S22). Will fire if system ever deploys against pre-existing pre-S22 data. Proper fix is a composite ordering scheme bridging the S22 boundary — add `replay_seq BIGSERIAL` column populated at backfill time using global event ordering, OR use `(events.stored_at, stream_version)` tuple sort. ADR-018 D13 documents the limitation.

### Phase 5 — UI/UX Refinements (final pre-launch sprint, projected ~Sprint 30)

**Priority focus**: P9 (Usability)

Final polish pass before launch — deferred from its original S18 / S21 slots so it lands on a stable, hardened backend rather than a moving target. Projected as ~S30 based on Phase 4 sub-sprint count (Phase 4a complete S22; Phase 4b ~1; Phase 4c ~2; Phase 4d ~3; Phase 4e ~1-2). Sprint number finalised when Phase 4d-3 (employee-profile versioned history) lands and its true sprint count is known. Scope unchanged from the original S18 plan.

- Visual consistency audit: token usage, spacing, alignment across all pages
- Responsive layout improvements (mobile/tablet breakpoints)
- Form validation UX: inline errors, field-level feedback, loading states
- Accessibility audit: keyboard navigation, ARIA labels, focus management, contrast ratios
- Error and empty states: meaningful messages, retry actions, skeleton loaders
- Navigation and information architecture review
- Toast/notification consolidation and consistency
- Data table improvements: sorting, filtering, pagination UX
- Skema grid usability refinements based on workflow testing
- Approval flow UX: clearer status indicators, action confirmations

## SYSTEM_TARGET.md Coverage Tracker

Projected functional coverage by requirement area. Percentages are cumulative.

| Requirement Area | S1–S3 | S4 | S5 (Phase 1) | S6 | S7 | S8 (Phase 2) | S9 (Phase 2b) | S10–S11 (Phase 3) | S12 (Phase 3c) | S13 (Phase 3d) | S14 (Phase 3e) | S15 (Phase 3f) | S16 | S17 | S18 (Phase 3g) | After Phase 4 |
|------------------|-------|-----|--------------|-----|-----|---------------------|---------------|-------------------|-----------------|----------------|----------------|----------------|-----|-----|----------------|---------------|
| A. Basic Time Registration | 80% | 80% | 85% | 85% | 85% | 95% | 98% | 98% | 98% | 99% | 99% | 99% | 100% | 100% | 100% | 100% |
| B. Working Time Rules | 70% | 72% | 75% | 75% | 75% | 95% | 95% | 98% | 98% | 98% | 98% | 98% | 100% | 100% | 100% | 100% |
| C. Time Types & Supplements | 60% | 60% | 70% | 70% | 70% | 95% | 95% | 97% | 97% | 97% | 97% | 97% | 97% | 100% | 100% | 100% |
| D. Absence Types | 65% | 80% | 85% | 85% | 85% | 95% | 97% | 97% | 97% | 97% | 97% | 100% | 100% | 100% | 100% | 100% |
| E. Organizational Structure | 0% | 0% | 0% | 70% | 85% | 90% | 92% | 95% | 95% | 95% | 95% | 95% | 95% | 95% | 95% | 100% |
| F. Roles and Authorization | 0% | 0% | 0% | 50% | 85% | 90% | 90% | 92% | 95% | 95% | 95% | 95% | 95% | 95% | 95% | 100% |
| G. Local Configuration | 0% | 0% | 0% | 10% | 75% | 80% | 85% | 90% | 95% | 95% | 98% | 98% | 98% | 98% | 98% | 100% |
| H. Period Approval Workflow | 0% | 0% | 0% | 10% | 80% | 85% | 95% | 95% | 95% | 95% | 95% | 95% | 95% | 98% | 98% | 100% |
| I. Agreement Config Mgmt | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 85% | 85% | 95% | 97% | 98% | 100% | 100% | 100% |
| J. Working Time Compliance | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 90% | 95% | 95% | 100% |
| K. Entitlement & Balances | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 85% | 90% | 95% | 95% | 100% |
| L. Overtime Governance | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 90% | 95% | 100% |
| M. Compensation Model | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 0% | 85% | 90% | 100% |
| AC-Specific Requirements | 40% | 42% | 45% | 45% | 45% | 90% | 90% | 97% | 98% | 98% | 99% | 99% | 99% | 100% | 100% | 100% |
| Payroll Integration | 50% | 80% | 88% | 88% | 90% | 95% | 95% | 98% | 98% | 98% | 99% | 99% | 99% | 100% | 100% | 100% |
| External Integrations | 60% | 60% | 60% | 60% | 60% | 90% | 90% | 90% | 90% | 90% | 90% | 90% | 90% | 90% | 90% | 100% |
| **Overall** | **~33%** | **~36%** | **~39%** | **~46%** | **~56%** | **~75%** | **~77%** | **~80%** | **~82%** | **~82%** | **~83%** | **~86%** | **~90%** | **~95%** | **~96%** | **100%** |

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

## Sprint 16 — Completed

Sprint 16 completed Phase 3f part 2 (Working Time Compliance). See [docs/sprints/SPRINT-16.md](docs/sprints/SPRINT-16.md) for full task log.

**Key deliverables**: EU Working Time Directive 2003/88/EC compliance — RestPeriodRule (pure static, 4 checks: 11h daily rest, weekly rest day, 48h/week ceiling, max daily hours), 5 compliance config fields on AgreementRuleConfig, VoluntaryUnsocialHours on TimeEntry/TimeEntryRegistered events, ComplianceCheckResult model (ADR-015), compensatory_rest table, config resolution chain propagation (DB→Repository→Seeder→ConfigResolution→Entity→Endpoints), ComplianceWarnings component on SkemaPage, compliance badges in ApprovalDashboard, 14 new unit tests (436 total).

**ADR-015**: ComplianceCheckResult is a separate return type from CalculationResult — justified divergence from PAT-006 because compliance results are validation outputs (violation/warning lists), not payroll calculations (wage lines).

## Sprint 17 — Completed

Sprint 17 completed Phase 3f part 3 (Overtime Governance & Compensation Model). See [docs/sprints/SPRINT-17.md](docs/sprints/SPRINT-17.md) for full task log.

**Key deliverables**: OvertimeGovernanceRule (pure static, 2 checks: max ceiling + pre-approval requirement, WARNINGs via ComplianceCheckResult/ADR-015), OvertimeBalance model + repository (follows EntitlementBalance pattern), OvertimePreApproval workflow (separate table, PENDING/APPROVED/REJECTED), 4 new config fields through full chain (DB→Seeder→ConfigResolution→Endpoints), compensation-aware PayrollMappingService (OVERTIME_50/100/MERARBEJDE → _PAYOUT/_AFSPADSERING suffix), 28 new wage type mapping rows, 9 new backend endpoints, 4 frontend changes (overtime balance card, governance warnings, pre-approval management page, compensation choice selector), 10 new unit tests (446 total).

**Phase 3f complete**: Sprints 15–17 delivered entitlements, working time compliance, and overtime governance. Overall functional coverage: ~95%.

## Sprint 22 — Completed

Sprint 22 completed Phase 4a (Transactional Outbox + Row-Version Atomic Exemplar). See [docs/sprints/SPRINT-22.md](docs/sprints/SPRINT-22.md) for full task log.

**Key deliverables**: ADR-018 D1-D12 (`outbox_events` + `schema_migrations` ledger + `version BIGINT` on profiles + end-exclusive `effective_to` + audit-action MODIFIED). `IOutboxEnqueue` split-interface lives in Infrastructure (cycle-6 design) preserves SharedKernel Npgsql purity. `PostgresEventStore : IEventStore, IOutboxEnqueue` dual-binding. `OutboxPublisher` BackgroundService — per-stream FIFO + 4-way cross-stream parallelism, ReadCommitted publisher tx, event_id correlation on 23505. `LocalAgreementProfileRepository` UPDATE-in-place same-day routing (CREATED / MODIFIED / SUPERSEDED audit-action). `ConfigEndpoints` PUT atomic exemplar — version-as-ETag (RFC 7232 quoted long), three-way audit, in-tx EnqueueAsync replaces post-commit AppendAsync. Frontend ETag helpers (`lib/etag.ts` parseVersionFromETag/formatVersionAsIfMatch). 16 D12 Docker-gated regression scenarios across `OutboxPublisherTests`/`ProfileRowVersionTests`/`EndExclusiveMigrationTests`/`EventStoreInTxTests`. Build 0/0; 517 unit + 35 plain regression + 50 Docker-gated total = 650.

**Option C scope narrowing (2026-05-04)**: TASK-2206 (8-repo `(conn, tx)` refactor + 10-file site swap) deferred to Phase 4c sub-sprint. ConfigEndpoints stands as the atomic exemplar; the other ~12 state-change endpoints still post-commit `AppendAsync` until 4c lands. Reference shape preserved on `worktree-agent-a9b76f8d1f88717ff` (NOT to be merged).

**Step 7a Codex (3 cycles)**: Cycle 1 P2 fix — `ConfigEndpoints` PUT response now uses `predecessor.CreatedBy/CreatedAt` on UPDATE-in-place branch (where the repo preserves them). Cycle 2 P1 fix — `OutboxPublisher` per-stream foreach `continue` → `break` to preserve ADR-018 D5 FIFO under transient publish failures. Cycle 3 cascade halted per `feedback_step7a_cycle_cap_discipline.md`: 2 new P2 findings (frontend CORS ETag fallback + same-day no-op short-circuit) routed to Phase 4b.

**Phase 4a complete**: Sprint 22 delivered the atomic state-change exemplar. Phase 4b-e propagate the pattern. Functional coverage unchanged at ~96% (all S18-S22 work was correctness/hardening; functional features were complete after S17). Committed `a278f34` on 2026-05-05.

## Architecture Decisions

See [docs/knowledge-base/INDEX.md](docs/knowledge-base/INDEX.md) for the full structured decision log (ADR, PAT, DEP, RES entries).

## Sprint Execution Logs

See [docs/sprints/INDEX.md](docs/sprints/INDEX.md) for the formal sprint log with validation evidence and traceability.
