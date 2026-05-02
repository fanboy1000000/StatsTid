# Sprint 21 — Local Agreement Configuration Rework

| Field | Value |
|-------|-------|
| **Sprint** | 21 |
| **Status** | analysis-phase open (Step 0a + Step 0b + data audit + ADR-017 complete 2026-05-02; migration plan + task decomposition pending) |
| **Start Date** | 2026-05-02 |
| **End Date** | TBD |
| **Orchestrator Approved** | analysis-phase yet to begin |
| **Build Verified** | n/a (no implementation yet) |
| **Test Verified** | n/a (no implementation yet) |

## Entropy Scan Findings

_Sprint 21 Step 0a, 2026-05-02._

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | The S20-retired symbols (`OkVersionBoundary`, `RecalculateWithVersionSplitAsync`) appear in `ADR-016-temporal-period-handling.md` as historical context recording their retirement — not stale references. No KB entry references a moved/deleted production file. |
| Pattern compliance spot-check | CLEAN | (a) PAT-005: 0 `using StatsTid.RuleEngine` from `src/Backend/`, `src/Integrations/`, `src/Infrastructure/`. (b) FAIL-001: 0 `FindFirst("scopes")`. (c) Hardcoded `http://localhost` / `http://rule-engine`: 0 outside `ServiceUrls:*` config-fallback defaults. (d) `RequireAuthorization` coverage: 93 endpoints, 88 calls — 5-endpoint gap matches expected unauthenticated set (5 `/health` endpoints; the `/login` endpoint is now authenticated as well per post-S6 hardening). |
| Orphan detection | CLEAN | S20 + post-S20 cleanup additions all referenced: `TestFixtures.DockerHarness` consumed by 6 test classes; `PayrollMappingService.BuildLine` consumed by both per-line-date and per-segment mappers; `AuditState.NoManifest` referenced by 2 PCS code paths (total-failure short-circuit + replay no-emit branch); `emitAuditEvents` parameter wired into the richer `ReplayAsync`. 27 grep hits across `src/` + `tests/`. |
| Documentation drift | CLEAN | `MEMORY.md` refreshed at S20 sprint close (post-S20 cleanup entry recorded; test counts current; deferred items list updated with 6 new S20-derived items). `docs/QUALITY.md` updated at S20 sprint close with S20 column and new SharedKernel (Segmentation) row. |
| Quality grade review | CLEAN | Grades current as of S20 sprint close: SharedKernel (Events) B+ → A-, SharedKernel (Segmentation) new at A, Payroll Integration B+ → A-, PostgreSQL Schema B → B+. No domain quality changes since. |

No DRIFT or DEBT findings. Analysis-phase opens.

## Pre-Sprint Anchoring Corrections

_2026-05-02. Two items in this sprint log were drafted before S20 completed; correcting them up front:_

1. **Question 2 ("S20's framework — does S21 inherit or precede?")** — S20 (Temporal Period Handling) is **complete**, committed `12b75f9` on 2026-05-02. S21 inherits its framework. Specifically: the `PlannedCalculation` + `SegmentManifest` + `SnapshotContract` types in `StatsTid.SharedKernel.Segmentation` are available; effective-date boundaries on local agreement profiles can plug into `BoundarySources.AgreementConfigPromotions` if the design wants the planner to detect transitions across profile activation dates. (Whether that wiring is actually useful for S21's resolution model is itself one of the architectural questions to settle in the ADR.)
2. **ADR numbering** — Question 11 hints at "a new ADR-016". ADR-016 is now taken (S20 Temporal Period Handling). S21's new ADR, if produced, will be **ADR-017**. Amending ADR-010 remains the alternative.

## Sprint Goal

Reshape local agreement configuration from a flat per-key patch bag into a **profile model**: one local agreement profile per `(orgId, agreementCode, OkVersion)` with the centrally-overridable subset of fields exposed as editable inputs and the remainder pinned read-only to the central config. Today's `local_configurations` table allows unbounded active rows per `(org, key)` with no uniqueness constraint, no parent identity, and gives admins no UX signal about which fields they may even adjust. The intended product behaviour — visible in `SYSTEM_TARGET.md` § G "Local Configuration" — is a single editable local version of the agreement per org, not a bag of overrides.

**This sprint begins with architectural analysis. No implementation tasks are listed. The first sprint activity is to produce an ADR (or amend ADR-010) and a task decomposition; implementation tasks are drafted only after that analysis is Orchestrator-approved.**

## Problem Statement

### What exists today

- **Schema** (`docker/postgres/init.sql:449`): `local_configurations` is row-per-key with PK on `config_id` and indexes on `org_id` and `config_area`. The schema **does** have a 6-tuple uniqueness constraint at `init.sql:467`: `UNIQUE (org_id, config_area, config_key, effective_from, agreement_code, ok_version)` — but it includes `effective_from`, so the design intent is "one row per key per effective date", not "one row per key". The duplicate-row drift in failure mode 1 below operates by exploiting this: admins create new rows with new `effective_from` rather than updating, `effective_to` is rarely set so old rows don't close, and `is_active` is independently mutable.
- **Resolution** (`src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs:159–268`): walks `_localConfigRepo.GetActiveByOrgAsync(orgId, agreementCode, okVersion)` and applies each row through a `switch` on `ConfigKey`. Iteration order wins on duplicates. Five keys are real overlays (`MaxFlexBalance`, `FlexCarryoverMax`, `WeeklyNormHours`, `MaxOvertimeHoursPerPeriod`, `OvertimeRequiresPreApproval`); two are silently informational (`PlanningStartDay`, `ApprovalCutoffDay`); ~20 keys are hard-rejected as `ProtectedKeys`; everything else logs a warning and is skipped.
- **Write API** (`src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs:134–209` `POST /api/config/{orgId}`): one row per call, `(configArea, configKey, configValue, agreementCode, okVersion, effectiveFrom, effectiveTo)`. Validates the proposed value against central constraints via `ValidateLocalOverride` but does not check whether another active row for the same key already exists.
- **UI** (`frontend/src/pages/config/ConfigManagement.tsx`): "Opret tilpasning" dialog mirrors the API — admin picks an area, types a key as a free-text input, types a value, picks an agreement and OK-version. The form gives no hint which keys are valid, which are protected, or which are informational. Admins must hold the whitelist in their head.

### Failure modes today

1. **Duplicate-row drift**: an admin who edits `MaxFlexBalance` from 60 to 50 and then to 45 produces three active rows. Resolution picks one based on iteration order; deactivating "the override" requires deactivating each row individually.
2. **Silent no-op overrides**: typing `EveningRate` (a protected key) returns 400 with a clear error, but typing `MaxOvetimeHoursPerPeriod` (typo of a real key) succeeds — the row persists, applies nothing at runtime, and the only signal is a debug-level log line.
3. **No "this is *the* local OK26" identity**: there is no document an admin can point to and say "this is our local version of OK26." The closest approximation is "the union of all active rows in `local_configurations` for this org, agreement, and OK-version, applied left-to-right."
4. **Drift on central change**: when central OK26 changes a non-overlaid field (e.g. raises `OvertimeThreshold50`), local rows for unrelated keys still apply correctly, but the org has no opportunity to re-confirm or re-acknowledge the new central value. The profile model would surface "central changed; here are the new defaults" naturally.
5. **Effective-dating per row, not per profile**: `effective_from` / `effective_to` are per-row. An admin could in principle have `MaxFlexBalance = 50` from 2026-01-01 to 2026-06-30 and `WeeklyNormHours = 36` from 2026-04-01 to 2026-12-31 — overlapping but not identical windows. Whether this expressiveness is useful or just a footgun is one of the open questions below.

### Why "patch bag" was chosen originally

ADR-010 (Sprint 6/7) established the merge-at-service-layer pattern to keep the rule engine pure. The flat row-per-key shape was the path of least resistance: each override is independent, the validation logic is per-key, and the audit trail (`local_configuration_audit`) is per-row. The shape was never deliberately chosen as a profile-vs-patch decision — the product-level "local version of an agreement" framing did not surface until admin UX testing.

**Merits of the patch shape worth weighing in the ADR** (so the rewrite deliberately rejects them rather than ignoring them): (1) per-row events (`LocalConfigurationChanged`) give granular event-sourcing replay — a profile-level event with a delta payload either grows the payload size or loses per-field precision; (2) per-row effective-dating allows time-shifted overrides for unrelated fields (e.g. a planned `MaxFlexBalance` increase scheduled differently from a `WeeklyNormHours` reduction). Q5 / Q8 each touch one of these — answering them is implicitly answering whether to preserve the merit.

### What "right" looks like (proposed direction, subject to ADR)

- A new logical entity: `local_agreement_profile` keyed `(org_id, agreement_code, ok_version)`, **unique**.
- The overridable fields are physical columns on the profile (the columns *are* the whitelist) — `weekly_norm_hours`, `max_flex_balance`, `flex_carryover_max`, `max_overtime_hours_per_period`, `overtime_requires_pre_approval`. NULL = "inherit central."
- Effective-dating happens at the profile level: one active profile per `(org, agreement, OkVersion)` at any point in time, with `effective_from` / `effective_to` and historical predecessors retained.
- The admin UI renders the *full* central agreement, with editable inputs on the overridable columns and read-only renderings on everything else. Save persists only the deltas (NULL-out unchanged columns).
- Existing `local_configurations` rows migrate into profile columns; legacy rows for unknown keys (informational/typo) are dropped with an audit-log emission.
- ADR-010's "merge at service layer" stays — the resolution chain remains `central → position override → local profile` (closed pre-commit; see "Closed Pre-Commits" below for rationale).
- **Effect on ADR-016**: local-config remains an effective-dated source; the unit of effective-dating shifts from per-row to per-profile. Local-config does NOT enter ADR-016 D5b's snapshot-at-calculation carve-out (which stays scoped to wage-type-mapping, entitlement-policy, and employee-profile per Phase 4's "Versioned History" sub-sprint trio). `BoundarySources` interaction with profile activations is the subject of Q12 below.

## Context and Existing Partial Solutions

Work the new design must build on or reconcile with:

- **`src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs`** — current per-key switch; will become per-column merge against the profile entity.
- **`src/Infrastructure/StatsTid.Infrastructure/LocalConfigurationRepository.cs`** — current repository; either reshaped or supplemented by a new `LocalAgreementProfileRepository`.
- **`src/SharedKernel/StatsTid.SharedKernel/Models/LocalConfiguration.cs`** — current per-row model; profile model is additive.
- **`src/SharedKernel/StatsTid.SharedKernel/Events/LocalConfigurationChanged.cs`** — append-only event, must continue to be emitted; profile-level "changed" events likely want their own event type.
- **`docker/postgres/init.sql:473-483` (`local_configuration_audit`)** — per-row append-only audit projection (`config_id`-keyed). If writes become profile-shaped, the audit table's row format becomes awkward — what does `previous_value` mean for a profile delta with multiple field changes? Q14 below asks this explicitly.
- **`src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs`** — `POST /api/config/{orgId}`, `GET /api/config/{orgId}/local`, `DELETE /api/config/{orgId}/{configId}` need profile-shaped equivalents (and a deprecation/migration story for the per-row endpoints).
- **`frontend/src/pages/config/ConfigManagement.tsx` + `frontend/src/hooks/useConfig.ts`** — UI rebuilt around a single profile editor view per `(org, agreement, OkVersion)`.
- **ADR-010 (Local Configuration Merged at Service Layer, Not in Rule Engine)** — keep the architectural invariant; amend the ADR (or add a successor) to record the profile-vs-patch decision.
- **ADR-014 (DB-Backed Agreement Configs)** — same DRAFT/ACTIVE/ARCHIVED-style lifecycle pattern is a candidate fit for profile state transitions.
- **PAT-005 (PeriodCalculationService HTTP)** — unchanged.
- **PAT-006 (Unified CalculationResult)** — unchanged.

## Legal & Correctness Constraints (must not regress)

1. **P1 — Architectural integrity.** Rule engine remains pure; merge stays at the service layer (ADR-010).
2. **P2 — Rule engine determinism.** Same `(events, profile, central)` tuple at timestamp T must always produce the same output. Profile resolution is deterministic.
3. **P3 — Auditability.** Every profile mutation is auditable end-to-end: what changed, who changed it, when, and what the previous value was. `local_configuration_audit` (or a successor table) retains its append-only semantics.
4. **P4 — OK version correctness.** A profile is scoped to a single OK version. Cross-version overrides are physically impossible by schema.
5. **P7 — Security.** Org-scope enforcement on every read and write of the profile (existing `OrgScopeValidator` patterns apply unchanged).
6. **Backwards compatibility.** Existing local-config rows must migrate to the new shape without losing semantics. Where migration is ambiguous (multiple active rows for the same key), the migration writes the most-recently-effective row and audit-logs the discarded predecessors.
7. **Constraint enforcement** (from `SYSTEM_TARGET.md` § G): "Local values must respect central min/max boundaries" and "Centrally negotiated rates cannot be overridden locally" — enforced by schema (only overridable columns exist) plus per-column validation on save.

## Open Architectural Questions (to answer at sprint start)

These must be resolved — and documented in an ADR (ADR-017, or amendment to ADR-010) — **before** a task decomposition is drafted. Step 0b plan review (2026-05-02) closed Q9 as a pre-commit and added Q12-Q14:

1. **Profile uniqueness scope.** Three options:
   - (a) Strict `(org_id, agreement_code, ok_version)` — clean but loses planned-future profiles AND historical predecessors.
   - (b) `(org_id, agreement_code, ok_version, effective_from)` — allows history but loses the at-most-one-active-now invariant unless extra resolution logic disambiguates at read time.
   - (c) Partial-unique-index pattern: `UNIQUE (org_id, agreement_code, ok_version) WHERE effective_to IS NULL` (or `WHERE is_active`). Gives "exactly one currently-active profile" while preserving closed predecessors. Same shape ADR-014 uses for `agreement_configs` ACTIVE rows.
   - Resolve before schema design begins.
2. **Effective-dating model.** One active profile at a time with explicit `effective_to` on supersession, or overlapping ranges with date-window selection at resolve time? S20's Temporal Period Handling is **complete**; S21 inherits its framework. Q12 below addresses how profile activations interact with `BoundarySources`.
3. **Lifecycle states.** Does the profile need DRAFT/ACTIVE/ARCHIVED like agreement configs (ADR-014), or is "active vs deactivated" sufficient given local profiles change rarely and rarely need preview?
4. **Migration strategy.** Three options (the "coexistence" middle option needs concrete definition):
   - (i) **Big-bang cutover with backfill** — drop `local_configurations` after profile migration; `ConfigResolutionService` reads only profiles. Simpler; harder to roll back.
   - (ii) **Shadow-compare in lower environments before cutover** — both tables exist in dev/staging only, profiles computed alongside per-row reads, diffs asserted equal in a non-prod harness. Production switches when the shadow is clean. Safest operational pattern.
   - (iii) **Dual-path resolution with profile-precedence** — both tables exist in production, `ConfigResolutionService` checks for a profile first and falls back to per-row only if no profile exists. Forward path for organic migration but writes are split.
5. **Schema choice for the override fields.** Nullable columns on a wide profile table (each NULL = inherit), or a JSONB blob with a known schema, or a normalized child table per overrideable field? Wide-nullable is admin-readable and easy to query; JSONB is flexible if the overridable set grows; normalized is over-engineering for ~5 fields.
6. **What about `PlanningStartDay` / `ApprovalCutoffDay` (the informational keys)?** They're stored in `local_configurations` today but never read by the rule engine. Are they (a) part of the profile, (b) a separate `org_operational_settings` table, or (c) deleted entirely?
   - **Sub-question 6a (run grep before deciding):** Where are these keys read outside the rule engine? UI? Scheduled jobs? Approval workflows from S7/S9? The answer determines the choice. If they're read elsewhere, option (c) is a regression. If they aren't, option (a) pollutes the profile entity with non-rule-engine fields. Run the grep as part of the data-audit deliverable.
7. **API shape.** Three options (Q7 + Q8 are fundamentally one question — "evolve atomically or in two steps?"):
   - (a) **Replace** existing per-row endpoints with profile-shaped equivalents (clean break).
   - (b) **Add new endpoints alongside; deprecate per-row in S22+** (deprecation runway).
   - (c) **Versioned-payload evolution** — keep the same URL, evolve request/response shape via versioned event payloads; old per-row writes still emit `LocalConfigurationChanged`, new profile writes emit `LocalAgreementProfileChanged`. Resolve in tandem with Q8.
8. **Event shape (split into 8a + 8b per Step 0b NOTE):**
   - **8a — Domain event shape:** One `LocalAgreementProfileChanged` event with full delta payload, or per-field events for granular replay?
   - **8b — Legacy event compatibility / shim policy:** `LocalConfigurationChanged` (per-row) — does it stay emitted alongside the new event for legacy event-store consumers? For how long? Does it carry a `superseded_by_profile_id` field once profiles exist?
9. ~~**Position override interaction.**~~ **CLOSED PRE-COMMIT (Step 0b, 2026-05-02):** Resolution chain stays `central → position override → local profile`, matching today's `ConfigResolutionService.cs:69-129` and ADR-014's documented order. Profile overrides position overrides; position overrides override central. ADR-017 records this as a deliberate decision (not an open question) tied to ADR-014 precedent — relitigating it would invalidate ~10 sprints of position-override work for no product gain.
10. **Backwards compatibility for existing API consumers.** The current `POST /api/config/{orgId}` is callable today by any LocalAdmin. Are there known external integrations that would break if it changes shape? If yes, redirect-with-shim; if no, replace.
11. **Test strategy — committed minimum matrix** (matching ADR-016 D11 format). All four categories pre-committed as IN; sub-question is scenario depth per category:
    - **Migration** (regression, Docker-gated): minimum 3 scenarios — multi-row-per-key collision, informational-key drop, unknown-key (typo) drop. Counted floor: ≥ 3 tests in `tests/StatsTid.Tests.Regression/Config/ProfileMigrationTests.cs`.
    - **Uniqueness enforcement** (regression, Docker-gated): minimum 2 scenarios — concurrent-insert race surfaces a uniqueness violation; soft-delete reactivation does not violate the partial-unique constraint (if Q1 lands on option c). Counted floor: ≥ 2 tests in `ProfileUniquenessTests.cs`.
    - **NULL-as-inherit resolution** (unit): minimum 3 scenarios — NULL on every overridable column inherits central; non-NULL on one overridable column overrides central for that field only; NULL after a previous non-NULL save reverts to central. Counted floor: ≥ 3 tests in `tests/StatsTid.Tests.Unit/Config/ProfileResolutionTests.cs`.
    - **Audit emission** (regression, Docker-gated): minimum 2 scenarios — every profile mutation emits a `LocalAgreementProfileChanged` event with the field-level delta; the audit projection table records the same delta. Counted floor: ≥ 2 tests in `ProfileAuditTests.cs`.
    - **Floor: 10 new tests.** Cells beyond floor add 1:1 against ADR-017's resolved scenarios.
12. **Profile activation as a `BoundarySources` source (NEW — Step 0b BLOCKER convergent finding).** Profile `effective_from` introduces a NEW effective-dated boundary distinct from `AgreementConfigPromotion`. Three options:
    - (a) **First-class boundary source:** add `BoundaryCause.LocalProfileActivation` to `StatsTid.SharedKernel.Segmentation`, hydrate it from production data via DB lookup, plug into `BoundarySources`. Mid-period profile changes produce additional segments.
    - (b) **Profile-stability assumption:** the planner reads the profile active on `periodStart` and assumes it stays valid for the whole calculation period. Mid-period profile rollovers must be scheduled by admins at period boundaries; the planner does not detect them.
    - (c) **Document as future work** — ship S21 without planner integration; add it in a follow-up alongside the existing carry-forward to hydrate non-OK boundaries.
    - **Sub-question 12a:** How does the choice interact with ADR-016 D2's classification inventory? `NormCheckRule.WEEKLY` (`aligned-window`) and `FlexBalanceRule` (`cross-period, mergeable`) consume profile fields and would behave differently per option. Document the interaction in ADR-017.
13. **Replay back-compat for pre-S21 manifests (NEW — Step 0b BLOCKER from Codex).** S20's `SegmentManifest`s captured `local_configurations` snapshots in the per-row shape. Once S21 ships, the canonical shape becomes per-profile. Three options:
    - (a) **Migrate historical manifests** — rewrite snapshot data inside existing manifests to the new shape during the schema migration. Replay always sees current shape.
    - (b) **Two-shape replay** — replay code branches on manifest "schema version" and reads either old or new shape. Manifests stay byte-stable; replay code grows a backward-compat path.
    - (c) **Cutoff line** — declare a date before which manifests are non-replayable; document explicitly. P3 audit query still works (manifests are queryable as historical artifacts) but `ReplayAsync(manifestId)` throws on pre-cutoff ids. Honest about the trade-off.
14. **Audit projection shape (NEW — Step 0b NOTE).** `local_configuration_audit` (`init.sql:473-483`) is `config_id`-keyed and stores `previous_value` / `new_value` strings. After Q7's API shape lands, audit writes either:
    - (a) Stay per-field — every field changed in a profile-wide save emits N audit rows, one per changed column. Backward-compat with current shape; query path unchanged.
    - (b) Become profile-shaped — new `local_agreement_profile_audit` table with `profile_id` + JSONB delta payload. One row per save; query becomes "show me the change deltas for profile X".
    - (c) Both, transitionally — emit the per-field rows for legacy queries AND a profile-shaped row for the new event. Same trade-off as Q7's coexistence option.

## Scope Boundary

### In scope
- Architecture & ADR for the profile model (ADR-017, or amendment to ADR-010).
- Schema migration: new `local_agreement_profiles` table, migration of existing `local_configurations` rows, audit-log handling for the migration itself.
- `ConfigResolutionService` refactored to consume profiles instead of per-key rows.
- New profile-shaped endpoints (or replacement of existing per-row endpoints — Q7 decides).
- **Basic functional admin UI**: single profile editor per `(org, agreement, OkVersion)` showing all fields with editable inputs on the overridable subset and read-only renderings on everything else. **No visual / interaction polish** — that's deferred to Phase 5 per the existing UX-deferral commitment. Step 0b explicitly resolved: the editor must be functional (correctly saves, correctly displays, correctly partitions editable vs read-only), not polished.
- Regression coverage per Q11's committed minimum matrix (≥ 10 new tests across migration / uniqueness / NULL-as-inherit / audit).

### Out of scope
- Changing what is overridable (the `MaxFlexBalance`, `WeeklyNormHours`, etc. set stays the same — discussion of widening or narrowing it is a separate product question).
- Touching position overrides (S11/S14) beyond documenting the precedence interaction (Q9 closed pre-commit).
- Touching agreement-config management (S12, GlobalAdmin self-service) — that's the central side.
- **UI polish beyond basic functional correctness** — Phase 5 owns visual and interaction polish; S21 ships a working editor, not a finished one.
- Surfacing profile history / scheduled-future profiles in the UI (depends on Q1/Q3 outcomes; if those questions allow it, history rendering is a Phase 5 follow-up).
- Any rule-engine changes — this sprint is configuration plumbing, not calculation logic.
- Hydrating non-OK boundary sources into the production `BoundarySources` shim — separate carry-forward work tracked from S20.

## Planning Entrypoint

No implementation tasks are defined yet. The sprint begins with the following **analysis-phase deliverables**, produced by the Orchestrator in collaboration with the user before any domain agent is spawned. Reordered post-Step-0b (Reviewer recommendation: data audit must precede schema design):

1. **Data audit** — enumerate the actual `local_configurations` rows in seed data (`docker/postgres/init.sql:629-633`) plus any committed test fixtures. Identify each instance of duplicate-row drift, expired-but-active rows, typo'd keys, informational keys not read by the rule engine, and any production captures available. Run `grep` for `PlanningStartDay` / `ApprovalCutoffDay` to answer Q6 sub-question 6a. Output: a short summary of which row shapes the migration must round-trip and which can be dropped. Without this, Q1 / Q4 / Q5 are answered in a vacuum.
2. **Architectural ADR** (ADR-017, or amendment to ADR-010) — answering the fourteen open questions above (Q1–Q8, Q10–Q14; Q9 closed pre-commit). Includes the cross-reference to ADR-016 D5b (local-config exits the snapshot-at-calculation set; effective-dating moves from per-row to per-profile).
3. **Migration plan** — explicit handling of multi-row-per-key cases, informational keys, and unknown keys per the data audit. Migration test fixtures synthesise each failure mode for the regression matrix (Q11).
4. **Task decomposition** — the ADR translated into `TASK-21NN` entries with domain agents, file scopes, and validation criteria, added to this sprint log under "## Task Log". Includes a UX agent slot for the basic functional editor.
5. **Entropy scan (Step 0a)** — completed 2026-05-02; findings recorded above.
6. **Plan review (Step 0b)** — completed 2026-05-02; findings + resolution recorded below.

Only after items 1–4 are Orchestrator-approved does Step 2 (Delegate) begin.

## Plan Review (Step 0b)

_Completed 2026-05-02. Trigger: MANDATORY — P1 (Architectural integrity) + P3 (Auditability) + P4 (OK-version correctness) + cross-domain (Data Model + Backend API + UX + Test & QA) + introduces new abstraction (profile model)._

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY |
| **External Codex** | invoked 2026-05-02 — cycle 1, 2 BLOCKER + 4 WARNING + 2 NOTE |
| **Internal Reviewer** | invoked 2026-05-02 — cycle 1, 3 BLOCKER + 7 WARNING + 5 NOTE |
| **BLOCKERs resolved before Step 1** | yes (2026-05-02) |

### Convergent Findings (Both Lenses)

Strong correctness signal — both saw these:

- **C1 (BLOCKER)** — 11 questions don't address how effective-dated profiles hydrate into S20's `BoundarySources`/`BoundaryCause`. Reviewer goes deeper on mid-period profile changes interacting with `aligned-window` vs `mergeable` rules per ADR-016 D2 inventory. **Resolution:** added Q12 with sub-question 12a.
- **C2 (BLOCKER/NOTE-split)** — Q11 too vague; should match S20 D11's enumerated test floor with named test classes. **Resolution:** Q11 rewritten with 4 categories pre-committed as IN, scenario depth committed per category, named test files, ≥ 10 floor.
- **C3 (BLOCKER/WARNING-split)** — Q1 missing third option (partial-unique-index `WHERE effective_to IS NULL`, ADR-014 precedent). **Resolution:** Q1 expanded with three options.
- **C4 (WARNING)** — Q4 missing parallel-run/shadow-comparison option. **Resolution:** Q4 rewritten with three concretely-defined options.
- **C5 (WARNING)** — UI rebuild scope tension with Phase-5 deferral. **Resolution:** user decision (a) — basic functional editor in S21, no polish; "In scope" rewritten to make the boundary explicit; "Out of scope" adds polish + history rendering.
- **C6 (WARN/BLOCKER-split)** — Q9 (resolution chain) plan accidentally pre-commits in prose while asking the question. **Resolution:** user decision (a) — Q9 closed as pre-commit citing ADR-014; removed from open questions; "What 'right' looks like" line at SPRINT-21.md:68 reframed.
- **C7 (WARNING)** — Q6 informational keys: where else are they read? **Resolution:** sub-question 6a added; grep happens during data audit (deliverable #1).
- **C8 (NOTE)** — Event/audit shape framing. **Resolution:** Q8 split into 8a (domain event) + 8b (legacy compat); Q14 added for audit projection.

### Reviewer-Only High-Value Findings

- **Reorder analysis-phase deliverables: data audit must precede ADR.** **Resolution:** user agreed; deliverables reordered (data audit = #1).
- **"What exists today" framing factually wrong** — schema HAS a 6-tuple UNIQUE at `init.sql:467`. **Resolution:** "What exists today" rewritten with the actual constraint and the three mechanisms of duplicate-row drift (`effective_from` proliferation, unset `effective_to`, mutable `is_active`).
- **Phase-4 "Versioned History" alignment.** **Resolution:** "What 'right' looks like" gains a cross-reference statement: S21 retires local-config from ADR-016 D5b's snapshot-at-calculation set; the three Phase-4 sub-sprints (WTM, entitlement, employee-profile) stay at three.
- **Patch-bag merits not assessed.** **Resolution:** "Why patch bag was chosen originally" gains a paragraph naming the two real merits (per-row event granularity, per-field effective-dating) so the ADR deliberately rejects them rather than ignoring them.
- **`local_configuration_audit` absent from Context list.** **Resolution:** added to Context; Q14 asks the audit-projection shape question.

### Codex-Only Finding

- **Replay back-compat for pre-S21 manifests** — old manifests captured snapshot-based local-config. **Resolution:** Q13 added with three options (migrate historical manifests / two-shape replay / cutoff line).

Step 0b cycle 1 closed. Plan now has 14 open questions (Q9 closed, Q12-Q14 added), expanded option enumeration on Q1/Q4/Q7, sub-questions on Q6/Q11/Q12, and a corrected "What exists today" framing. Analysis-phase deliverables reordered to data-audit-first.

## Data Audit (Deliverable #1, 2026-05-02)

_Reviewer-recommended pre-ADR audit of actual `local_configurations` row shapes. Schema design (Q1, Q4, Q5) depends on what the migration must round-trip._

### Seed data (`docker/postgres/init.sql:629-633`)

Three rows total, all on `agreement_code='HK'`, `ok_version='OK24'`, `effective_from='2024-01-01'`, `is_active=true`, `effective_to=NULL`:

| # | org_id | config_area | config_key | config_value | classification |
|---|--------|-------------|------------|--------------|----------------|
| 1 | STY02 | FLEX_RULES | `MaxFlexBalance` | `"80.0"` | overridable (canonical) |
| 2 | STY02 | WORKING_TIME | `PlanningStartDay` | `"MONDAY"` | informational (Q6) |
| 3 | AFD01 | OPERATIONAL | `ApprovalCutoffDay` | `"25"` | informational (Q6) |

Two orgs covered (`STY02`, `AFD01`). No duplicate-row drift. No expired-but-active rows (`effective_to=NULL`). No typo'd keys. No multi-active overlapping windows. **The seed exercises zero failure modes** — Q11's migration test (≥ 3 scenarios) needs synthesized fixtures (recommendation moves to deliverable #3 migration plan).

### Test fixtures (`grep` over `tests/`)

No DB-backed test fixtures touch `local_configurations`. Three model/serialization-level references found:

- `tests/StatsTid.Tests.Unit/Security/Sprint6SecurityTests.cs:249-268` — constructs in-memory `LocalConfiguration` for round-trip serialization. No DB write.
- `tests/StatsTid.Tests.Unit/Sprint7ApprovalTests.cs:240-272` — constructs `LocalConfigurationChanged` event for round-trip serialization. No DB write.
- `tests/StatsTid.Tests.Unit/Sprint7ConfigTests.cs:245-250` — calls `ValidateLocalOverride("PlanningStartDay", "Monday", centralConfig)` to assert validator accepts the key. Not a DB fixture; tests behavior that goes away once `PlanningStartDay` is dropped (Q6 → option c).

### Production captures

None. System is not in production.

### Q6 sub-question 6a — where are `PlanningStartDay` / `ApprovalCutoffDay` read?

Grep over `src/` and `frontend/` returned **exactly one production file**:

- `src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs:255-258` (resolution): `LogDebug("informational only — does not affect AgreementRuleConfig"); break;` — log-and-skip, no behavior.
- `src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs:372-374` (validation): `case "PlanningStartDay": case "ApprovalCutoffDay": return (true, null);` — accepts unconditionally with no constraint check.

Plus one test (`Sprint7ConfigTests.cs:245`) that asserts the validator accepts the key — purely testing the validator's no-op path.

**Zero hits in `frontend/`. Zero hits in any approval workflow (`src/Backend/.../Endpoints/`), event handler, or scheduled job.** The keys are entirely inert.

→ **Q6 answer pre-committed: option (c) deleted entirely.** Migration drops both rows with `local_configuration_audit` emission. The validator switch cases at lines 372-374 are removed (the `default` branch will reject any future write — desirable). The test at `Sprint7ConfigTests.cs:245-250` is deleted as part of the migration commit. No regression risk: nothing reads them, nothing depends on them.

### Canonical key taxonomy (from `ConfigResolutionService.cs`)

For ADR-017's reference:

- **5 overridable keys** (become physical NULL-able columns on `local_agreement_profiles` per Q5):
  - `MaxFlexBalance`, `FlexCarryoverMax`, `WeeklyNormHours`, `MaxOvertimeHoursPerPeriod`, `OvertimeRequiresPreApproval`
- **21 protected keys** (`ProtectedKeys` set at `ConfigResolutionService.cs:24-47`; cannot be locally overridden — absence from the profile schema enforces this structurally):
  - `HasOvertime`, `HasMerarbejde`, `EveningSupplementEnabled`, `NightSupplementEnabled`, `WeekendSupplementEnabled`, `HolidaySupplementEnabled`, `EveningRate`, `NightRate`, `WeekendSaturdayRate`, `WeekendSundayRate`, `HolidayRate`, `EveningStart`, `EveningEnd`, `NightStart`, `NightEnd`, `OvertimeThreshold50`, `OvertimeThreshold100`, `OnCallDutyEnabled`, `OnCallDutyRate`, `DefaultCompensationModel`, `EmployeeCompensationChoice`
- **2 informational keys** (drop entirely per Q6 → c):
  - `PlanningStartDay`, `ApprovalCutoffDay`
- **Unknown keys**: rejected by `ValidateLocalOverride`'s `default` branch + accepted-but-skipped by resolution. Today's typo'd-key failure mode arises here. After S21, the profile column set IS the whitelist — typos become compile-time field-name errors at write time.

### Migration round-trip requirements

For the seed data, the migration produces:

- **Profile 1**: `(STY02, HK, OK24)`, `effective_from=2024-01-01`, `max_flex_balance=80.0`, all other overridable columns NULL.
- Drops seed row 2 (`PlanningStartDay`) with audit-log emission `{action: "DROPPED_INFORMATIONAL", reason: "Q6 deletion per ADR-017"}`.
- Drops seed row 3 (`ApprovalCutoffDay`) with the same audit shape.
- Profile for `(AFD01, HK, OK24)` is NOT created — the org has only an informational row, no overlay. After migration, `AFD01` has no profile row; resolution returns central-only.

### Implications for ADR-017

1. **Q1 (uniqueness)**: option (c) partial-unique-index `WHERE effective_to IS NULL` is consistent with the seed (all rows have `effective_to=NULL`). No precedent for closed predecessors yet — the constraint is schema-enforced from day one.
2. **Q4 (migration strategy)**: with zero failure-mode rows in seed and no production data, **big-bang cutover (option i)** is safe. Shadow-compare (option ii) buys nothing because there's no diff to compare against. Dual-path (option iii) is over-engineering. **Recommend option (i)** in ADR-017.
3. **Q5 (schema choice)**: 5 overridable fields × 2 orgs in seed = trivial. Wide-nullable columns easily admin-readable. No case for JSONB.
4. **Q11 (test strategy)**: migration test fixtures must be **synthesized** since seed doesn't exercise any failure mode. Fixtures: (a) one row per overridable key, (b) two-row collision on same `(org, agreement, OkVersion, key)` differing only in `effective_from`, (c) one typo'd key (`MaxOvetimeHoursPerPeriod`), (d) one informational key destined for drop, (e) one expired-but-active row. Migration plan (deliverable #3) commits to this fixture set.

Data audit closed. Findings inform ADR-017 (deliverable #2) and the migration plan (deliverable #3).

## References

- [CLAUDE.md](../../CLAUDE.md) — priority order (P1, P7, P9 driving this sprint)
- [ROADMAP.md](../../ROADMAP.md) — Phase 3i placement
- [SYSTEM_TARGET.md § G](../../SYSTEM_TARGET.md) — Local Configuration product spec
- [docs/knowledge-base/decisions/ADR-010-local-config-merge-at-service-layer.md](../knowledge-base/decisions/ADR-010-local-config-merge-at-service-layer.md) — current local config architecture
- [docs/knowledge-base/decisions/ADR-014-db-backed-agreement-configs.md](../knowledge-base/decisions/ADR-014-db-backed-agreement-configs.md) — DRAFT/ACTIVE/ARCHIVED lifecycle precedent + partial-unique-index pattern reused
- [docs/knowledge-base/decisions/ADR-016-temporal-period-handling.md](../knowledge-base/decisions/ADR-016-temporal-period-handling.md) — `BoundarySources` extended additively by ADR-017 D9
- [docs/knowledge-base/decisions/ADR-017-local-agreement-configuration-as-a-profile.md](../knowledge-base/decisions/ADR-017-local-agreement-configuration-as-a-profile.md) — S21's architectural decision (deliverable #2)
- [SPRINT-20.md](SPRINT-20.md) — sibling analysis-first sprint, structurally similar
- [docker/postgres/init.sql](../../docker/postgres/init.sql) — current `local_configurations` table (line 449)
- [src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs](../../src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs) — current resolution (line 159 onward)
- [src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs](../../src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs) — current API (line 134 onward)
- [frontend/src/pages/config/ConfigManagement.tsx](../../frontend/src/pages/config/ConfigManagement.tsx) — current admin UI
