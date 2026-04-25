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

### Phase 4 — Production Hardening (Sprint 22+)

**Priority focus**: All priorities — cross-cutting production readiness

Only makes sense once functional correctness is achieved. The final UX polish pass (Phase 5) follows production hardening so layout, performance, and instrumentation are settled before visual lockdown.

- Performance profiling and optimization
- Monitoring, alerting, health checks
- Real SLS integration (replacing mock)
- Load testing, stress testing
- Security audit, penetration testing
- Documentation and operational runbooks

### Phase 5 — UI/UX Refinements (final pre-launch sprint, sprint TBD after Phase 4)

**Priority focus**: P9 (Usability)

Final polish pass before launch — deferred from its original S18 / S21 slots so it lands on a stable, hardened backend rather than a moving target. Sprint number assigned once Phase 4's actual sprint count is known. Scope unchanged from the original S18 plan.

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

## Architecture Decisions

See [docs/knowledge-base/INDEX.md](docs/knowledge-base/INDEX.md) for the full structured decision log (ADR, PAT, DEP, RES entries).

## Sprint Execution Logs

See [docs/sprints/INDEX.md](docs/sprints/INDEX.md) for the formal sprint log with validation evidence and traceability.
