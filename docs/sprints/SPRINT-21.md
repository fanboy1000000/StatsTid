# Sprint 21 — Local Agreement Configuration Rework

| Field | Value |
|-------|-------|
| **Sprint** | 21 |
| **Status** | complete (analysis + 5 implementation phases + Step 7a Codex 10-cycle review + revert-to-cycle-1-only + sprint close) |
| **Start Date** | 2026-05-02 |
| **End Date** | 2026-05-03 |
| **Orchestrator Approved** | yes — 2026-05-03 |
| **Build Verified** | yes (`dotnet build` 0 errors, 19 expected obsolete-warning breadcrumbs from S21 deprecations) |
| **Test Verified** | yes (517 unit + 35 plain regression + 18 Docker-gated profile + 48 frontend = 618; pre-existing 6 Docker-gated PlannerInvariantViolation failures unchanged from pre-S21 master, separate carry-forward) |

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

## ADR Review (Cycle 1, 2026-05-02)

ADR-017 dispatched for plan-mode review (internal Reviewer + external Codex) immediately after drafting. Both lenses returned within minutes.

### Findings — internal Reviewer (5 BLOCKER, 10 WARNING, 5 NOTE)

**BLOCKERs:**
- **R-B1** — D9c hydration query reads from `local_agreement_profiles` but doesn't filter on `is_active = TRUE`. Deactivated profiles would still hydrate planner boundaries.
- **R-B2** — D1's `is_active` boolean and `effective_to IS NULL` partial-unique are redundant lifecycle dimensions. Could drift independently (`is_active=FALSE` AND `effective_to=NULL` would be open by index but inactive by boolean — silent inconsistency).
- **R-B3** — D9a wires `IRuleClassificationProvider` into Backend.Api, but the provider isn't DI-registered there (lives in Payroll Integration). Either register it (expands dependency graph) or inline the alignment policy as static data (recommended — only `WeeklyNormHours` has alignment today).
- **R-B4** — D9a's `Func<DateOnly, ValidationResult>` shape can't express timestamp-aligned constraints. ADR claims architectural extensibility it doesn't have.
- **R-B5** (effectively the same as R-B2) — `is_active` redundancy ripples into D2, D9c, and migration tie-break.

**WARNINGs (selected):** D2 race story underspecified (two-admin interleaved supersession); D5 authorization unnamed; D9c crosses Payroll → Infrastructure layering seam without naming the WTM precedent; D9c `OrgId` contract undocumented; D6+D8 dual-persistence transactional contract missing; D9a `FlexCarryoverMax → FlexBalanceRule` mapping forward-looking; D10 rollback path hand-waved; D11 missing tests for D9c hydration, D2 no-scheduled-future, D7 no-emit-legacy; migration unknown-key whitelist source-of-truth; Q12 (b) rejection rationale weaker than the ADR makes it.

**NOTEs:** D9b tie-break rationale missing; `Augments` field omits ADR-002; References don't cite seed-row line range; D9a error message language convention undefined; ADR-014 partial-unique predicate divergence (`status='ACTIVE'` vs `effective_to IS NULL`).

### Findings — external Codex (3 BLOCKER, 4 WARNING, 2 NOTE)

Strong convergence with internal Reviewer. Same 3 BLOCKERs (D1+D9c filter mismatch; D6+D8 transactional contract; D9c layering seam). 4 WARNINGs convergent on D2 race, D11 hydration test, D5 authorization, ADR-014 predicate. 2 NOTEs on Q12-(b) softening + tie-break order verification.

### Convergence Summary

| Finding | Severity (Reviewer / Codex) | Resolution applied (cycle 1) |
|---------|----------------------------|------------------------------|
| D1 `is_active` redundancy + D9c filter mismatch | BLOCKER / BLOCKER | **D1 simplified — `is_active` column dropped.** Lifecycle = `effective_to` only. Partial-unique-index `WHERE effective_to IS NULL` is the single authoritative signal. |
| D6+D8 dual-persistence transactional contract | WARNING / BLOCKER | **D6 + D8 paragraphs added** stating event-store, audit-projection, and profile UPDATE+INSERT all run in the same DB transaction. No partial-failure outcome model needed (no two-phase commit across stores). |
| D9c layering seam | WARNING / BLOCKER | **D9c paragraph added** citing `WageTypeMappingRepository` precedent (S20 wave 1). |
| D9a `IRuleClassificationProvider` plumbing not in Backend.Api | BLOCKER (Reviewer-only) | **D9a redesigned** — alignment policy is a static const map in `StatsTid.SharedKernel.Models.LocalAgreementProfile`. No provider DI dependency. |
| D9a `Func<DateOnly, ValidationResult>` scope | BLOCKER (Reviewer-only) | **D9a paragraph added** acknowledging DateOnly-only scope; timestamp alignment is forward-looking. |
| D2 two-admin race | WARNING (both) | **D2.1 added — ETag/If-Match optimistic concurrency** (user decision 2026-05-02). PUT requires `If-Match: "<currentProfileId>"` for supersession or `If-None-Match: *` for first creation; 412 Precondition Failed on stale state. **D2.2 added — Phase-4 followup** to propagate the pattern to `agreement_configs`, `position_overrides`, `wage_type_mappings`, `entitlement_configs` (5+ admin-write surfaces with same race today). |
| D5 authorization | WARNING (both) | **D5 endpoint list extended** with explicit `RequireAuthorization` policies (`LocalAdminOrAbove` for PUT; `EmployeeOrAbove` for GET). |
| D9c `OrgId` contract | WARNING (Reviewer-only) | **D9c paragraph added** documenting the contract (profile-less callers see no boundaries). |
| D9a `FlexCarryoverMax` mapping | WARNING (Reviewer-only) | **D9a table extended** with explicit "consumer rule (documentation only)" column header + forward-looking note. The static map is the runtime data; the table is design-rationale. |
| D10 rollback hand-waved | WARNING (Reviewer-only) | **D10 reframed as forward-only migration**. Pre-cutover backup is the rollback source; post-cutover writes are not reversibly transformable. Honest for a pre-production system. |
| Migration unknown-key whitelist | WARNING (Reviewer-only) | **D4 paragraph added** committing to whitelist-from-schema-source-of-truth. Implementation mechanism deferred to deliverable #3 (migration plan). |
| D11 missing tests (4 categories) | WARNING (both) | **D11 floor 13 → 17.** Added: hydration shim test (D9c), no-scheduled-future negative (D2), no-emit-legacy negative (D7), ETag/If-Match conflict (D2.1). |
| Q12 (b) rationale weak | WARNING (Reviewer) / NOTE (Codex) | **Alternatives Rejected for Q12 reworded** to acknowledge (b) was a coherent alternative; (d)'s win is UX-driven, not architecture-driven. |
| Tie-break rationale | NOTE (both) | **D9b paragraph added** with rationale (org-wide vs per-position scope). |
| `Augments` omits ADR-002 | NOTE (Reviewer-only) | **Header updated.** |
| References don't cite seed-row range | NOTE (Reviewer-only) | **References extended** with `init.sql:629-633`, `BoundaryDetector.cs`, and `PeriodCalculationService.cs`. |
| Error message language convention | NOTE (Reviewer-only) | **D9a paragraph added** specifying English error codes + ISO-8601 dates; frontend translates. |
| ADR-014 predicate divergence | NOTE (Reviewer-only) | **D1 paragraph added** explaining the divergence (no DRAFT workflow → no `status` enum needed). |

ADR-017 cycle 1 closed within the 2-cycle cap. Plan-review pattern matches S20's Step 0b shape. Cycle 2 not invoked at this stage — no BLOCKERs remain after cycle 1 resolutions on the ADR text itself. (Phase-3 implementation cycle 2 invoked separately; see "Phase 3 review (cycle 1 + 2)" below.)

### Phase 3 review (cycle 1 + 2, 2026-05-02)

**Cycle 1 — Reviewer + Codex on Phase 3 outputs (TASK-2106 + TASK-2107):**
- Internal Reviewer: 2 BLOCKER, 7 WARNING, 9 NOTE.
- Codex high-risk override: 2 BLOCKER (P1+P1).
- **Convergent BLOCKER**: D6 transactional contract violated — event-store append happened AFTER `tx.CommitAsync` AND `PostgresEventStore.AppendAsync` opened its own connection internally; partial-failure mode possible.
- **Codex-only BLOCKER**: migrator silently discarded recoverable overrides when newest-active value was unparseable (collapsed entire key group to `DROPPED_UNKNOWN_KEY` instead of falling through to next parseable winner).
- **Reviewer-only BLOCKER**: future-effective_from rejection missing — D2's "no scheduled-future" + D11 fixture #15 not enforced in PUT.

**Cycle 1 fixes applied:**
- D6: added `IEventStore.AppendAsync(DbConnection, DbTransaction, …)` in-tx overload; PUT calls it BEFORE `tx.CommitAsync`.
- Migrator: walks ordered (newest-first) rows, picks first parseable winner; older losers → `DROPPED_DUPLICATE_AT_MIGRATION`; newer-than-winner unparseables → `DROPPED_UNKNOWN_KEY`; only collapses to all-`DROPPED_UNKNOWN_KEY` when NO row in group is parseable.
- Future rejection: PUT checks `candidate.EffectiveFrom > UTC today` before alignment validation; returns 400 with `code: "EFFECTIVE_FROM_NOT_TODAY_OR_PAST"`.
- Plus WARNING: post-migration assertions tightened from `>=` to `==`; new drop-count exact-match assertion querying `actor_id='system'`.

**Cycle 2 — Codex re-verification:**
- Future-rejection + migrator fixes: verified clean.
- D6 fix: **NEW [P1]** — under `RepeatableRead`, the in-tx `AppendInternalAsync` reads `MAX(stream_version)` from the caller's snapshot (taken before concurrent commits become visible). Two admins racing the same profile both compute the same `stream_version`, second commit fails with UNIQUE violation on `(stream_id, stream_version)`, whole save rolls back. The pre-cycle-1 self-contained `AppendAsync` (fresh transaction, fresh snapshot) avoided this.

**Cycle 2 decision: Option C — revert D6 cycle-1 fix; Phase-4 carry-forward (user-approved 2026-05-02):**
- 2-cycle cap reached per WORKFLOW.md; halted and prompted user.
- Three options surfaced: (A) cycle-3 deeper fix (FOR UPDATE on event_streams + retry on UNIQUE violation, ~30 LOC), (B) accept cycle-2 finding as known limitation (rare admin contention, retries on 500), (C) revert cycle-1 D6 fix to pre-fix shape with Phase-4 transactional-outbox redesign.
- User chose **Option C**. The architectural-honest answer: the proper fix is the transactional-outbox pattern (insert into `outbox_events` inside profile tx; separate publisher drains to canonical event store), which is sized correctly for a focused future sprint task — not a cycle-3 patch.

**Cycle 2 reverts applied:**
- `ConfigEndpoints.cs` PUT: event append moved back AFTER `tx.CommitAsync`, calling self-contained `eventStore.AppendAsync(streamId, @event, ct)`.
- `IEventStore.AppendAsync(DbConnection, DbTransaction, …)` overload removed (dead code; will be re-added cleanly when the outbox task lands).
- `PostgresEventStore.AppendInternalAsync` private helper removed (was only consumed by the in-tx overload).
- Future-rejection fix and migrator fix (cycle-1 BLOCKERs 2 + 3) **preserved** — they passed cycle-2 verification cleanly.

**ADR-017 D6 footnote** added: documents the residual partial-failure risk and the Phase-4 outbox commitment.

**Phase-4 hardening commitments now on ROADMAP.md:**
- D2.2 sibling — ETag/If-Match pattern propagation across `agreement_configs` / `position_overrides` / `wage_type_mappings` / `entitlement_configs`.
- D6 sibling — Transactional outbox for event-store + state-change atomicity. Insert events into `outbox_events` inside the profile/state-change transaction; separate publisher drains to canonical event store. Same atomic guarantee without MVCC snapshot conflicts.

Phase 3 review closed at cycle 2. 0 BLOCKERs remain in the to-be-committed diff. Phase 4 (TASK-2108 + TASK-2109) ready to dispatch on confirmation.

## Migration Plan (Deliverable #3, 2026-05-02)

Per ADR-017 D4, migration is big-bang cutover in a single transaction. This section enumerates each step, the failure modes the transaction handles, and the synthesized fixture set for D11's 17-test floor.

### Migration sequence

The migration runs as one transaction inside the post-S20 deployment. PostgreSQL's transactional DDL guarantees all-or-nothing semantics: any failure rolls back every CREATE TABLE, INDEX, and INSERT.

```sql
BEGIN;

-- Step 1: Create new schema.
CREATE TABLE local_agreement_profiles (
    profile_id                          UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id                              TEXT         NOT NULL REFERENCES organizations(org_id),
    agreement_code                      TEXT         NOT NULL,
    ok_version                          TEXT         NOT NULL,
    effective_from                      DATE         NOT NULL,
    effective_to                        DATE,
    weekly_norm_hours                   NUMERIC(5,2),
    max_flex_balance                    NUMERIC(6,2),
    flex_carryover_max                  NUMERIC(6,2),
    max_overtime_hours_per_period       NUMERIC(6,2),
    overtime_requires_pre_approval      BOOLEAN,
    created_by                          TEXT         NOT NULL,
    created_at                          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX uq_local_agreement_profile_active
    ON local_agreement_profiles (org_id, agreement_code, ok_version)
    WHERE effective_to IS NULL;

CREATE INDEX idx_local_agreement_profile_org ON local_agreement_profiles(org_id);
CREATE INDEX idx_local_agreement_profile_history
    ON local_agreement_profiles (org_id, agreement_code, ok_version, effective_from DESC);

CREATE TABLE local_agreement_profile_audit (
    audit_id      BIGSERIAL    PRIMARY KEY,
    profile_id    UUID         NOT NULL,
    action        TEXT         NOT NULL CHECK (action IN ('CREATED', 'SUPERSEDED', 'DEACTIVATED', 'MIGRATED_FROM_LEGACY')),
    delta_jsonb   JSONB        NOT NULL,
    actor_id      TEXT         NOT NULL,
    actor_role    TEXT         NOT NULL,
    timestamp     TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_local_profile_audit_profile ON local_agreement_profile_audit(profile_id);

-- Step 2: Migrate per-row data into profile rows.
-- See per-tuple migration logic below.

-- Step 3: Emit `local_configuration_audit` rows for dropped per-row entries.
-- See classification rules below.

-- Step 4: Validation: row counts, key sets, expected drops.
-- See post-migration assertions below.

COMMIT;
```

If any step fails (constraint violation, classification mismatch, validation assertion), the entire transaction rolls back. The pre-S21 schema and data remain intact. Operator re-runs after fixing the fault.

### Per-tuple migration logic (Step 2)

Pseudocode (translated to a C# migration runner or PL/pgSQL function in deliverable #4):

```
known_overridable_keys = {
    "MaxFlexBalance":              "max_flex_balance",
    "FlexCarryoverMax":            "flex_carryover_max",
    "WeeklyNormHours":             "weekly_norm_hours",
    "MaxOvertimeHoursPerPeriod":   "max_overtime_hours_per_period",
    "OvertimeRequiresPreApproval": "overtime_requires_pre_approval",
}
known_informational_keys = { "PlanningStartDay", "ApprovalCutoffDay" }

# Whitelist source-of-truth (D4 cycle 1): the migration runner reads the
# `known_overridable_keys` map from `LocalAgreementProfile`'s static metadata
# (a `[Column]`-attribute reflection, or a single `OverridableFieldNames` const list).
# If a future schema migration adds a column, the migration's known-keys set
# automatically picks it up — no manual sync.

for each (org_id, agreement_code, ok_version) tuple in local_configurations:
    rows = SELECT * FROM local_configurations
           WHERE org_id = @org AND agreement_code = @agreement AND ok_version = @ok_version
             AND is_active = TRUE
             AND effective_from <= today
             AND (effective_to IS NULL OR effective_to >= today)
           ORDER BY config_key, effective_from DESC;

    profile_columns = {}
    earliest_effective_from = MAX_DATE

    for each config_key, group in rows group by config_key:
        # Pick the most-recently-effective row for this key (largest effective_from).
        winner = group.first  # Already DESC-sorted

        if config_key in known_overridable_keys:
            column_name = known_overridable_keys[config_key]
            profile_columns[column_name] = parse(winner.config_value)
            earliest_effective_from = MIN(earliest_effective_from, winner.effective_from)

            # Audit-log all losers (non-winning rows for the same key).
            for loser in group.skip(1):
                INSERT INTO local_configuration_audit (config_id, action, previous_value, new_value, actor_id, actor_role)
                VALUES (loser.config_id, 'DROPPED_DUPLICATE_AT_MIGRATION', loser.config_value, NULL, 'system', 'GlobalAdmin');

        elif config_key in known_informational_keys:
            for row in group:
                INSERT INTO local_configuration_audit (config_id, action, previous_value, new_value, actor_id, actor_role)
                VALUES (row.config_id, 'DROPPED_INFORMATIONAL', row.config_value, NULL, 'system', 'GlobalAdmin');

        else:  # Unknown key (typo, deprecated, or future field)
            for row in group:
                INSERT INTO local_configuration_audit (config_id, action, previous_value, new_value, actor_id, actor_role)
                VALUES (row.config_id, 'DROPPED_UNKNOWN_KEY', row.config_value, NULL, 'system', 'GlobalAdmin');

    # Only insert a profile row if there's at least one overridable key.
    if profile_columns is not empty:
        new_profile_id = uuid_generate_v4()
        INSERT INTO local_agreement_profiles (
            profile_id, org_id, agreement_code, ok_version,
            effective_from, effective_to, ...profile_columns,
            created_by
        ) VALUES (
            @new_profile_id, @org, @agreement, @ok_version,
            @earliest_effective_from, NULL, ...profile_columns.values,
            'system'  # migration runs as the system actor
        );

        # Emit a profile-shaped audit row capturing the migration.
        INSERT INTO local_agreement_profile_audit (profile_id, action, delta_jsonb, actor_id, actor_role)
        VALUES (
            @new_profile_id,
            'MIGRATED_FROM_LEGACY',
            jsonb_build_object('migrated_from_count', @row_count, 'profile_columns', @profile_columns_json),
            'system',
            'GlobalAdmin'
        );
```

### Classification rules

| Row in `local_configurations` | Migration action | Audit action |
|-------------------------------|------------------|--------------|
| `config_key` ∈ overridable, sole row for key | merged into profile column | none (it's the chosen value) |
| `config_key` ∈ overridable, one of N rows | most-recently-effective wins | `DROPPED_DUPLICATE_AT_MIGRATION` for losers |
| `config_key` ∈ informational | dropped | `DROPPED_INFORMATIONAL` |
| `config_key` ∉ overridable ∪ informational | dropped (typo / deprecated / future) | `DROPPED_UNKNOWN_KEY` |
| `is_active = FALSE` row | ignored entirely (not read into the migration) | none — pre-S21 deactivated rows stay queryable in their original table |
| `effective_to < today` but `is_active = TRUE` | ignored (already-expired) | none — same reasoning |

### Post-migration validation (Step 4)

Inside the same transaction, before COMMIT, the migration runner asserts:

1. **Profile count matches expected**: `SELECT COUNT(*) FROM local_agreement_profiles WHERE effective_to IS NULL` equals the count of `(org_id, agreement_code, ok_version)` tuples that had at least one overridable-key row in pre-migration data. For seed data: 1 (only `STY02 / HK / OK24`; `AFD01 / HK / OK24` has only `ApprovalCutoffDay` which is informational, so it does NOT receive a profile).
2. **Drop counts match expected**: `SELECT COUNT(*) FROM local_configuration_audit WHERE action LIKE 'DROPPED_%' AND timestamp > @migration_start` equals the count of pre-migration rows that were not absorbed into profile columns. For seed: 2 (`PlanningStartDay`, `ApprovalCutoffDay`).
3. **No partial-unique-index violations**: `SELECT COUNT(*) FROM (SELECT org_id, agreement_code, ok_version, COUNT(*) AS open_count FROM local_agreement_profiles WHERE effective_to IS NULL GROUP BY org_id, agreement_code, ok_version HAVING COUNT(*) > 1) violations` returns 0.
4. **Profile NULL semantics correct**: every profile row has at least one non-NULL overridable column (otherwise no profile should exist — a fully-NULL profile is equivalent to no profile).

If any assertion fails, the transaction rolls back. The runner outputs a structured error naming the failed assertion + offending row IDs.

### Failure modes within migration

| Failure | Detection | Outcome |
|---------|-----------|---------|
| Schema constraint violation (e.g., `org_id` doesn't reference a valid `organizations` row) | PostgreSQL FK check at INSERT time | Transaction aborts; rolled back |
| Two rows with same `effective_from` for the same `(org, agreement, OkVersion)` after key resolution | Migration runner detects (would create two open-ended profiles for same key) — should be impossible because we pick a single winner per key, but assertion #3 catches the unexpected case | Transaction aborts; rolled back |
| `local_configurations` table modified mid-migration (admin saves while migration runs) | Migration runs at deployment time when API is offline; admin saves shouldn't happen | If they do somehow happen, the post-migration `local_configurations` table contains rows the migration didn't see; those rows stay in legacy table (read-only post-migration) but are NOT in `local_agreement_profiles`. Explicitly NOT a transactional concern — operational discipline of "deploy with API offline" handles this. |
| Insufficient permissions to CREATE TABLE | PostgreSQL DDL check | Transaction aborts |

### Whitelist source-of-truth implementation (cycle 1 D4)

Two implementation options for "the migration's known-overridable-keys set is generated from the same compile-time source as the schema":

**Option (a) — Reflection over `LocalAgreementProfile` C# model.** The model has 5 properties matching column names; the migration runner uses reflection to enumerate them and maps PascalCase column-aware property names back to legacy `config_key` strings via a small dictionary. New columns automatically picked up.

**Option (b) — Single shared const list.** A `static readonly IReadOnlyDictionary<string, string> LegacyKeyToColumn` declared in `StatsTid.SharedKernel.Models.LocalAgreementProfile`. The schema migration script references the same list (e.g., embeds them via a code-generation step). New columns added by editing the const + the schema together.

Recommend option (b) — simpler, no reflection magic, single grep-able location. Deliverable #4 (task decomposition) commits to option (b) as the data-model task's responsibility.

### Q11 / D11 Fixture Set

Synthesized data for the 18 named test scenarios. Each fixture lives in the test class's `IAsyncLifetime.InitializeAsync` (Docker-gated) or as in-memory test data (unit). Migration-test fixtures populate `local_configurations` BEFORE running the migration; assertion checks the post-migration state.

| # | Test class | Test name | Fixture seed | Assertion |
|---|------------|-----------|--------------|-----------|
| 1 | `ProfileMigrationTests` (Docker) | `MultiRowPerKeyCollision_KeepsMostRecentEffectiveFrom` | `local_configurations`: two rows for `(STY02, HK, OK24, MaxFlexBalance)` with `effective_from='2024-01-01'` (value=80) and `effective_from='2025-06-01'` (value=100), both `is_active=TRUE`, `effective_to=NULL` | post-migration profile has `max_flex_balance=100`; one `DROPPED_DUPLICATE_AT_MIGRATION` audit row for the 80-value losers |
| 2 | `ProfileMigrationTests` (Docker) | `InformationalKey_PlanningStartDay_DroppedWithAudit` | `local_configurations`: one row `(STY02, HK, OK24, PlanningStartDay='MONDAY')` | post-migration: no `local_agreement_profiles` row created for STY02 (no overridable keys); one `DROPPED_INFORMATIONAL` audit row |
| 3 | `ProfileMigrationTests` (Docker) | `TypoKey_MaxOvetimeHoursPerPeriod_DroppedWithAudit` | `local_configurations`: one row `(STY02, HK, OK24, MaxOvetimeHoursPerPeriod='150')` (typo) | post-migration: no profile row for STY02; one `DROPPED_UNKNOWN_KEY` audit row |
| 4 | `ProfileMigrationTests` (Docker) | `ExpiredButActiveRow_IgnoredEntirely` | `local_configurations`: one row `(STY02, HK, OK24, MaxFlexBalance=60)` with `effective_to='2024-12-31'` (expired) but `is_active=TRUE` | post-migration: no profile row (the row was filtered out by `effective_to >= today`); the legacy row stays in `local_configurations` for audit-history reads |
| 5 | `ProfileMigrationTests` (Docker) | `OneRowPerOverridableKey_HappyPath` | `local_configurations`: 5 rows, one per overridable key on `(STY02, HK, OK24)` with distinct `effective_from` dates | post-migration: one profile row with all 5 columns populated; `effective_from = MIN(source effective_froms)`; zero audit drops |
| 6 | `ProfileUniquenessTests` (Docker) | `ConcurrentInsert_RaceSurfacesUniqueViolation` | two transactions racing INSERT against `(STY02, HK, OK24, effective_to=NULL)` | first INSERT succeeds; second receives PostgreSQL unique-violation error; partial-unique-index enforced |
| 7 | `ProfileUniquenessTests` (Docker) | `DeactivationWithoutSupersession_AllowsFutureRecreation` | seed one open profile; UPDATE `effective_to=today`; later INSERT a new open profile for same triple | both operations succeed; the partial-unique-index is satisfied because the predecessor's `effective_to` is non-NULL |
| 8 | `ProfileResolutionTests` (Unit) | `AllNullColumns_InheritCentralForEveryField` | profile with all 5 columns NULL; central config has known values | resolution returns `AgreementRuleConfig` matching central byte-for-byte |
| 9 | `ProfileResolutionTests` (Unit) | `OneNonNullColumn_OverridesOnlyThatField` | profile with `max_flex_balance=100`, all other columns NULL; central has `max_flex_balance=80` | resolution returns `MaxFlexBalance=100`, `WeeklyNormHours=central`, etc. |
| 10 | `ProfileResolutionTests` (Unit) | `NullAfterNonNull_RevertsToCentral` | initial profile sets `max_flex_balance=100`; second profile (after supersession) sets `max_flex_balance=NULL` | second profile's resolution returns central value for `MaxFlexBalance` |
| 11 | `ProfileAuditTests` (Docker) | `Mutation_EmitsEventWithDelta` | PUT changes `weekly_norm_hours` 37→36 | one `LocalAgreementProfileChanged` event in event store with `ChangedFields={weekly_norm_hours: {Old:37, New:36}}`; `PrecedingProfileId` points at predecessor |
| 12 | `ProfileAuditTests` (Docker) | `Mutation_PersistsAuditProjectionRow` | same PUT as #11 | one `local_agreement_profile_audit` row with same `delta_jsonb` shape; `action='SUPERSEDED'` |
| 13 | `ProfileAlignmentValidatorTests` (Unit) | `WeeklyNormHoursMidWeek_Returns400WithStructuredError` | PUT with `effective_from='2026-05-06'` (Wednesday) and `weekly_norm_hours=36` | 400 with `{ field: "WeeklyNormHours", code: "NOT_MONDAY_ALIGNED", nearestValid: ["2026-05-04", "2026-05-11"] }` |
| 14 | `ProfileBoundaryHydrationTests` (Docker) | `ProfileEffectiveFromInsidePeriod_ProducesBoundary` | seed profile with `effective_from='2026-04-15'`; calculation period `2026-04-01` to `2026-04-30` | `BuildPlanForLegacyCallers` produces `BoundarySources` with one `LocalProfileActivation` boundary at `2026-04-15`; `BoundaryCause.LocalProfileActivation` is set; tie-break order matches D9b. Variant: profile-less calling shape (no `OrgId` on `EmploymentProfile`) → empty `LocalProfileActivations`. |
| 15 | `ProfileScheduledFutureRejectionTests` (Docker) | `EffectiveFromInFuture_Returns400` | PUT with `effective_from='2026-07-01'` (today is `2026-05-02`) | 400 with `code: "EFFECTIVE_FROM_NOT_TODAY_OR_PAST"` |
| 16 | `ProfileLegacyEventNonEmissionTests` (Docker) | `ProfilePut_EmitsExactlyOneProfileEvent_AndZeroLegacyEvents` | PUT against new profile API | event store contains exactly one `LocalAgreementProfileChanged` and zero `LocalConfigurationChanged` for the calling correlation id |
| 17 | `ProfileConcurrencyTokenTests` (Docker) | `PutWithoutIfMatchHeader_WhenCurrentExists_Returns412` | seed one open profile; PUT without `If-Match` header | 412 Precondition Failed; response body contains current profile state |
| 18 | `ProfileConcurrencyTokenTests` (Docker) | `PutWithStaleIfMatch_AfterRacingAdminCommitted_Returns412` | seed open profile P1; admin A1 PUT with `If-Match: P1` succeeds (creates P2); admin A2 PUT with `If-Match: P1` (still based on P1) | 412 Precondition Failed; response body contains current profile state (=P2) |

**Floor**: 17 tests committed (D11). 18 named scenarios documented; one above floor as headroom against last-minute removals. Each test class follows xUnit conventions and the post-S20 cleanup pattern (Docker-gated tests use `[Trait("Category", "Docker")]` + `TestFixtures.DockerHarness.StartAsync()`).

### Migration plan summary

- **Single transaction**, idempotent on rollback.
- **Whitelist** generated from compile-time schema via const list (option b).
- **Audit emissions** at every drop and every profile creation; both `local_configuration_audit` (legacy table for legacy-keyed entries) and `local_agreement_profile_audit` (new table for the migration-as-a-creation event).
- **Validation assertions** inside the transaction; failure rolls back.
- **Operational discipline** — deploy with API offline so no concurrent writes interfere with the migration.
- **Post-migration cleanup** (drop `local_configurations` table + the per-row audit table) is explicitly out of S21 scope; tracked as a future cleanup sprint candidate.

Migration plan closed. Next: deliverable #4 (TASK-21NN decomposition).

## Task Log (Deliverable #4, 2026-05-02)

_Drafted from ADR-017 + Migration Plan; user-approval pending. All tasks are `not-started` until Step 2 (Delegate)._

### Task Index

| TASK | Domain / Agent | Phase | Title |
|------|----------------|-------|-------|
| TASK-2101 | Data Model | 1 | Schema migration: `local_agreement_profiles` + audit table + partial-unique-index + audit-action enum extension |
| TASK-2102 | Data Model | 1 | SharedKernel `LocalAgreementProfile` model + `AlignmentPolicies` static map + `LegacyKeyToColumn` whitelist |
| TASK-2103 | Data Model | 1 | `LocalAgreementProfileChanged` event + `EventSerializer` registration (event count 44 → 45) |
| TASK-2104 | Rule Engine (SharedKernel.Segmentation) | 1 | `BoundaryCause.LocalProfileActivation` enum value + `BoundarySources.LocalProfileActivations` field + `BoundaryDetector.OrderedCauses` extension |
| TASK-2105 | Data Model (extended scope into Infrastructure) | 2 | `LocalAgreementProfileRepository` with `SELECT FOR UPDATE` for ETag transactions |
| TASK-2106 | Data Model (extended scope into Infrastructure) | 3 | `LocalAgreementProfileMigrator` runner + `ConfigResolutionService` rewrite to read profiles instead of per-key rows |
| TASK-2107 | Backend API (cross-domain authorized) | 3 | `ConfigEndpoints` rewrite (PUT/GET/history), `ProfileAlignmentValidator`, ETag/If-Match handling, OrgScopeValidator integration |
| TASK-2108 | Payroll Integration | 4 | `BuildPlanForLegacyCallers` extension: hydrate `LocalProfileActivations` from `LocalAgreementProfileRepository` (D9c) |
| TASK-2109 | UX | 4 | `ConfigManagement.tsx` rewrite as profile editor; WeeklyNormHours date-picker (Monday-only); ETag/If-Match save flow; read-only renderings for protected fields |
| TASK-2110 | Test & QA | 5 | D11 18-scenario test matrix per fixture set table (9 test classes; floor of 17) |

### Phase Ordering

- **Phase 1 (parallel-independent, worktree isolation)**: TASK-2101, 2102, 2103, 2104. All four touch different surfaces (schema, models, events, segmentation types). Phase 1 done when all four land.
- **Phase 2 (depends on Phase 1)**: TASK-2105 (repository — depends on 2101 schema + 2102 model). Single task, sequential.
- **Phase 3 (parallel within Phase, depends on Phase 2)**: TASK-2106 (migration + resolution — depends on 2105), TASK-2107 (endpoints + validator — depends on 2102 + 2105). Parallel via worktree isolation.
- **Phase 4 (parallel within Phase, depends on Phase 3)**: TASK-2108 (PCS extension — depends on 2104 + 2105), TASK-2109 (UX — depends on 2107 API surface). Parallel via worktree isolation.
- **Phase 5 (sequential, depends on all production code)**: TASK-2110 (Test & QA matrix).
- **Phase 6 (Orchestrator)**: build/test validation, Step 5α Constraint Validator over all outputs, Step 5a Internal Reviewer (P1 + P3 + P4 + cross-domain + new-abstraction triggers — MANDATORY). **High-risk Step 5a override** also applies (P3 auditability + P4 OK-version + schema migrations + new authorization paths = three of the high-risk domains): external Codex review per task in addition to internal Reviewer. Step 7a sprint-end Codex review on full S21 diff against `90ffa9b` (current pre-S21 master HEAD before S21 work began at `893ef83`).

### Task Detail

#### TASK-2101 — Schema migration: profile + audit + audit-action enum extension
**Agent**: Data Model
**Phase**: 1
**Files (write)**:
- `docker/postgres/init.sql` (additive — new `local_agreement_profiles` + `local_agreement_profile_audit` tables + indexes; extend `local_configuration_audit.action` CHECK constraint to include the four new action values: `DROPPED_DUPLICATE_AT_MIGRATION`, `DROPPED_INFORMATIONAL`, `DROPPED_UNKNOWN_KEY`, `MIGRATED_FROM_LEGACY`)

**Scope**:
- New `local_agreement_profiles` table per D1 schema.
- Partial-unique-index `WHERE effective_to IS NULL`.
- Two indexes: `(org_id)`, `(org_id, agreement_code, ok_version, effective_from DESC)` for history reads.
- New `local_agreement_profile_audit` table per D8 schema + `(profile_id)` index.
- Extend `local_configuration_audit.action` CHECK constraint (current values: `CREATED`, `MODIFIED`, `DEACTIVATED`, `APPROVED`; add the four migration-related values).

**Validation**: migration runs cleanly on fresh DB (`docker compose up postgres` succeeds); `dotnet build` clean.

**Cross-domain dependencies**: none — pure schema work. Subsequent tasks consume the schema.

#### TASK-2102 — SharedKernel `LocalAgreementProfile` model + alignment + whitelist
**Agent**: Data Model
**Phase**: 1
**Files (write)**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/LocalAgreementProfile.cs` (new — init-only properties per PAT-001; 5 nullable overridable columns + lifecycle metadata)
- Static read-only members on `LocalAgreementProfile`:
  - `LegacyKeyToColumn` — `IReadOnlyDictionary<string, string>` mapping pre-S21 `config_key` strings to schema column names. Used by migration runner (TASK-2106) and validator (TASK-2107).
  - `AlignmentPolicies` — `IReadOnlyDictionary<string, Func<DateOnly, ValidationResult>>` keyed by overridable field name (PascalCase). Today only `WeeklyNormHours` is populated with the Monday-only check. Used by `ProfileAlignmentValidator` (TASK-2107).
- Existing `src/SharedKernel/StatsTid.SharedKernel/Models/LocalConfiguration.cs` stays untouched (legacy reads continue).

**Scope**: Type definitions + static metadata. No I/O, no behavior beyond constants and validation predicates.

**Validation**: build clean; type compiles; PAT-001 immutability honored.

**Cross-domain dependencies**: TASK-2105 / TASK-2106 / TASK-2107 / TASK-2108 / TASK-2109 all consume `LocalAgreementProfile`; TASK-2106 + TASK-2107 consume the static maps.

#### TASK-2103 — `LocalAgreementProfileChanged` event + EventSerializer registration
**Agent**: Data Model
**Phase**: 1
**Files (write)**:
- `src/SharedKernel/StatsTid.SharedKernel/Events/LocalAgreementProfileChanged.cs` (new — extends `DomainEventBase`; carries `ProfileId`, `OrgId`, `AgreementCode`, `OkVersion`, `EffectiveFrom`, `ChangedFields: IReadOnlyDictionary<string, FieldChange>`, `ActorId`, `ActorRole`, `PrecedingProfileId?`)
- `src/SharedKernel/StatsTid.SharedKernel/Events/FieldChange.cs` (new — sealed record `FieldChange(JsonElement Old, JsonElement New)`)
- `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` (register the new type)

**Scope**: Event class + EventSerializer entry. Existing `LocalConfigurationChanged` stays registered (D7 — pre-S21 events remain deserializable).

**Validation**: build clean; `EventSerializerCoverageTests` reflection guard passes; event count 44 → 45; DEP-003 honored.

**Cross-domain dependencies**: TASK-2106 + TASK-2107 emit this event during writes.

#### TASK-2104 — `BoundaryCause.LocalProfileActivation` + `BoundarySources` extension + `OrderedCauses` update
**Agent**: Rule Engine (SharedKernel.Segmentation infrastructure — same boundary as S20 TASK-2003 and TASK-2004 used)
**Phase**: 1
**Files (write)**:
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/BoundaryCause.cs` (add `LocalProfileActivation` enum value)
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/PeriodPlanner.cs` or wherever `BoundarySources` record is defined (extend with `IReadOnlyList<(DateOnly EffectiveFrom, Guid ProfileId)> LocalProfileActivations` field; default empty list; `BoundarySources.Empty` includes empty list)
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/BoundaryDetector.cs` (extend `OrderedCauses` to insert `LocalProfileActivation` between `AgreementConfigPromotion` and `PositionOverrideEffective`; extend boundary-detection logic to emit boundaries from `BoundarySources.LocalProfileActivations`)

**Scope**: Pure additive extensions. Existing planner tests must pass without modification — empty `LocalProfileActivations` is the default for all existing tests.

**Validation**: build clean; existing 513 unit + 32 plain regression tests pass; D9b tie-break order matches the documented sequence.

**Cross-domain dependencies**: TASK-2108 hydrates the new field. TASK-2110 tests the extended detection.

#### TASK-2105 — `LocalAgreementProfileRepository`
**Agent**: Data Model (extended scope into `src/Infrastructure/`; cross-domain authorized — repositories adjacent to models per S20 / S6 precedent)
**Phase**: 2
**Files (write)**:
- `src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs` (new)

**Scope**: CRUD methods:
- `GetCurrentOpenAsync(orgId, agreementCode, okVersion, ct)` — SELECT WHERE `effective_to IS NULL`. Returns `LocalAgreementProfile?`.
- `GetActivationsInPeriodAsync(orgId, agreementCode, okVersion, periodStart, periodEnd, ct)` — for D9c hydration. SELECT WHERE `effective_from > periodStart AND effective_from <= periodEnd AND (effective_to IS NULL OR effective_to >= periodStart)`. Returns `IReadOnlyList<(DateOnly, Guid)>`.
- `GetHistoryAsync(orgId, agreementCode, okVersion, ct)` — SELECT WHERE `effective_to IS NOT NULL` ORDER BY `effective_from DESC`. Returns `IReadOnlyList<LocalAgreementProfile>`.
- `SupersedeAndCreateAsync(currentProfileId?, newProfile, ct)` — close-then-insert in a single transaction. Inside the transaction: `SELECT ... WHERE profile_id = @currentProfileId FOR UPDATE` first to acquire row lock. If `currentProfileId` is null, validates no current open profile exists (for first-creation). Returns new `Guid`.
- `DeactivateAsync(orgId, agreementCode, okVersion, ct)` — UPDATE current open profile SET `effective_to = today`. No follow-on insert. Returns affected-rows count.

All methods use the shared `DbConnectionFactory` pattern. `SELECT FOR UPDATE` ensures the partial-unique-index can't race during the close-then-insert window (required by D2.1).

**Validation**: build clean; integration test from TASK-2110 exercises each method.

**Cross-domain dependencies**: depends on TASK-2101 (schema) + TASK-2102 (model). Consumed by TASK-2106, TASK-2107, TASK-2108.

#### TASK-2106 — `LocalAgreementProfileMigrator` runner + `ConfigResolutionService` rewrite
**Agent**: Data Model (extended scope into `src/Infrastructure/`)
**Phase**: 3
**Files (write)**:
- `src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileMigrator.cs` (new — implements migration plan above; idempotent re-runnability for development; runs as single transaction)
- `src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs` (rewrite — replace per-key switch on `_localConfigRepo.GetActiveByOrgAsync(...)` with profile read via `_localAgreementProfileRepo.GetCurrentOpenAsync(...)`; per-column merge replacing per-key merge; resolution chain unchanged: central → position override → profile)
- (deprecate but don't delete) `src/Infrastructure/StatsTid.Infrastructure/LocalConfigurationRepository.cs` — stays for historical reads against `local_configurations` legacy table; mark with XML doc comment "post-S21: legacy repository for pre-migration audit-history queries; new write path is `LocalAgreementProfileRepository`".

**Scope**: Migration runs once on deployment; resolution service consumes the new profile shape on all paths.

**Validation**: build clean; existing config/resolution tests adapted to profile shape (TASK-2110 owns the test changes); `dotnet build` 0 errors. Migration runner exercised by TASK-2110's migration tests.

**Cross-domain dependencies**: depends on TASK-2101 (schema) + TASK-2102 (LegacyKeyToColumn map) + TASK-2105 (repository). Consumed by TASK-2107 (write path) + TASK-2108 (read path).

#### TASK-2107 — `ConfigEndpoints` rewrite + `ProfileAlignmentValidator`
**Agent**: Backend API (cross-domain authorized — `src/Backend/StatsTid.Backend.Api/` is not in any standard agent scope; TASK-2107 explicitly authorizes the Data Model agent or a Backend agent variant)
**Phase**: 3
**Files (write)**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs` (rewrite — remove the three per-row endpoints; add the three profile-shaped endpoints per D5 with `RequireAuthorization` policies, ETag/If-Match handling, OrgScopeValidator integration)
- `src/Backend/StatsTid.Backend.Api/Validators/ProfileAlignmentValidator.cs` (new — consumes `LocalAgreementProfile.AlignmentPolicies` static map; runs on PUT before persistence; returns structured error per D9a)
- `src/Backend/StatsTid.Backend.Api/Program.cs` (DI registration — `LocalAgreementProfileRepository`, `ProfileAlignmentValidator`)

**Scope**:
- PUT endpoint flow: parse body → run `ProfileAlignmentValidator` → check `If-Match` / `If-None-Match` precondition → SELECT FOR UPDATE current open row → emit `LocalAgreementProfileChanged` event + `local_agreement_profile_audit` row + UPDATE+INSERT in one transaction → return 200 with new profile (and `ETag: "<newProfileId>"`) or 412 / 400 on failure.
- GET endpoint: return current open profile + `ETag` header.
- History endpoint: return closed predecessors ordered most-recent-first.
- All endpoints wrapped in `RequireAuthorization` per D5.
- `OrgScopeValidator.ValidateOrgAccessAsync` runs before any repository call.

**Validation**: build clean; `RequireAuthorization` chained on all three endpoints (Step 5α Constraint Validator check); ETag/If-Match handling matches D2.1 (TASK-2110 verifies via tests 17 + 18).

**Cross-domain dependencies**: depends on TASK-2102 (alignment static map) + TASK-2103 (event) + TASK-2105 (repository). Consumed by TASK-2109 (frontend).

#### TASK-2108 — `BuildPlanForLegacyCallers` extension (D9c)
**Agent**: Payroll Integration
**Phase**: 4
**Files (write)**:
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` (extend the private `BuildPlanForLegacyCallers` method per D9c)

**Scope**: Inject `LocalAgreementProfileRepository` via DI (cross-domain — same shape as the existing wage-type-mapping repository injection). Inside `BuildPlanForLegacyCallers`:
- Compute `(orgId, agreementCode, okVersion)` from `EmploymentProfile`.
- Call `_localAgreementProfileRepo.GetActivationsInPeriodAsync(orgId, agreementCode, okVersion, periodStart, periodEnd, ct)`.
- Map the result to `BoundarySources.LocalProfileActivations`.
- Profile-less callers (`profile.OrgId` null/empty) get an empty list per D9c contract.

**Validation**: build clean; existing 513 unit + 32 plain regression tests pass; TASK-2110's hydration shim test exercises the new path.

**Cross-domain dependencies**: depends on TASK-2104 (`BoundarySources.LocalProfileActivations` field) + TASK-2105 (repository).

#### TASK-2109 — `ConfigManagement.tsx` rewrite as profile editor + date-picker
**Agent**: UX
**Phase**: 4
**Files (write)**:
- `frontend/src/pages/config/ConfigManagement.tsx` (rewrite — single profile editor view per `(org, agreement, OkVersion)`)
- `frontend/src/hooks/useConfig.ts` (reshape — fetch profile + history; PUT with `If-Match` / `If-None-Match`)
- `frontend/src/components/config/ProfileEditor.tsx` (new — profile editor with editable inputs on overridable subset and read-only renderings on protected fields)
- `frontend/src/components/config/MondayDatePicker.tsx` (new — date picker that disables non-Mondays for `WeeklyNormHours`)

**Scope**: Functional editor only — no visual / interaction polish (Phase 5 owns polish). The 5 overridable fields render as inputs (disabled when NULL = inherit-central; enabling lets admin override). The 21 protected fields render as read-only labels. Save flow handles 412 by re-fetching and presenting a "your data is stale" banner with retry option. WeeklyNormHours date-picker only allows Monday selection.

**Validation**: vitest passes (existing 41 tests + maybe 2-3 new for the date-picker behavior); editor renders without errors against a mocked backend.

**Cross-domain dependencies**: depends on TASK-2107 (API surface). UX agent's `frontend/**` scope covers all the new files.

#### TASK-2110 — D11 18-scenario test matrix
**Agent**: Test & QA
**Phase**: 5
**Files (write)**:
- `tests/StatsTid.Tests.Regression/Config/ProfileMigrationTests.cs` (5 Docker-gated tests)
- `tests/StatsTid.Tests.Regression/Config/ProfileUniquenessTests.cs` (2 Docker-gated tests)
- `tests/StatsTid.Tests.Unit/Config/ProfileResolutionTests.cs` (3 unit tests)
- `tests/StatsTid.Tests.Regression/Config/ProfileAuditTests.cs` (2 Docker-gated tests)
- `tests/StatsTid.Tests.Unit/Config/ProfileAlignmentValidatorTests.cs` (1 unit test)
- `tests/StatsTid.Tests.Regression/Segmentation/ProfileBoundaryHydrationTests.cs` (1 Docker-gated test)
- `tests/StatsTid.Tests.Regression/Config/ProfileScheduledFutureRejectionTests.cs` (1 Docker-gated test)
- `tests/StatsTid.Tests.Regression/Config/ProfileLegacyEventNonEmissionTests.cs` (1 Docker-gated test)
- `tests/StatsTid.Tests.Regression/Config/ProfileConcurrencyTokenTests.cs` (2 Docker-gated tests)

**Scope**: 18 named scenarios per the fixture set table above. All Docker-gated tests use `TestFixtures.DockerHarness.StartAsync()` (post-S20 cleanup pattern). Test class names + file paths match ADR-017 D11 verbatim. Floor: 17 (committed); 18 actual (one above floor).

**Validation**: `dotnet build` 0 errors; `dotnet test --filter "Category!=Docker"` shows the 4 unit tests pass (3 resolution + 1 alignment); Docker-gated tests properly trait-marked; existing 513 unit + 32 plain regression still pass (no regressions). After this task, total counts: 513 + 4 = 517 unit + 32 + 14 = 46 plain regression (the 14 includes the 5 migration + 2 uniqueness + 2 audit + 1 hydration + 1 scheduled-future + 1 no-emit-legacy + 2 concurrency tests, all Docker-gated, only counted if Docker daemon runs).

**Cross-domain dependencies**: depends on all production code (TASK-2101 through TASK-2109). Test & QA Agent's `tests/**` scope covers all new files.

### Risks & Watch-Points

- **Phase 1 throughput** — TASK-2104 (Segmentation extension) is the only Rule Engine task; the other three are Data Model. Worktree isolation handles parallel writes; Phase 1 done when all four tasks land.
- **Cross-domain authorization for TASK-2105 + TASK-2106 + TASK-2107** — Data Model agent's standard scope is `src/SharedKernel/**/Models/**` etc.; TASK-2105 + 2106 extend into `src/Infrastructure/`, TASK-2107 extends into `src/Backend/`. Each task's prompt MUST explicitly authorize the extended scope (matching S20 TASK-2010's PayrollExportLine.cs pattern).
- **Phase 3 parallel risk** — TASK-2106 and TASK-2107 both touch DI registration in their respective `Program.cs`. Worktree isolation prevents file-level conflicts; if conflicts arise on `Program.cs` files, sequential merge is acceptable.
- **Phase 4 parallel risk** — TASK-2108 (PCS) and TASK-2109 (frontend) touch entirely different surfaces; parallel execution is safe.
- **Frontend test coverage** — TASK-2109's vitest count goes from 41 to potentially ~43-45. UX agent should not aim for full E2E parity; basic functional correctness only (per scope-boundary "no polish").
- **High-risk Step 5a override** — S21 hits **P3 + P4 + new schema + new authorization paths** = 4 high-risk categories (more than S20's 3). External Codex review at Step 5a is mandatory per workflow; budget for one BLOCKER-fix cycle. Halt and prompt user after 2 BLOCKER cycles per workflow rule.
- **D9c hydration interaction with the S20 carry-forward** — S20's `BuildPlanForLegacyCallers` only hydrates `OkTransitions`. TASK-2108 adds `LocalProfileActivations`. Other non-OK boundary sources (agreement-config, position-override, EU-WTD) remain on the carry-forward list — they are explicitly NOT in S21 scope.
- **Migration runner timing** — runs at deployment time when API is offline (operational discipline). Not transactional vs. concurrent admin writes — that's an ops concern, not a code concern.

Task decomposition closed. Ready for user approval before Step 2 (Delegate).

## Implementation Commits

Phases 1-5 ran in sequence per the task decomposition above; each phase included the Reviewer + Codex review cycles documented in their respective phase logs.

| Phase | Commit | Tasks | Notes |
|-------|--------|-------|-------|
| Phase 1 | `fa676d6` | TASK-2101..2104 | Schema + model + event + segmentation extension. Worktree-isolated parallel execution. |
| Phase 2 | `6db3499` | TASK-2105 | `LocalAgreementProfileRepository` with `SELECT FOR UPDATE` for ETag transactions. |
| Phase 3 | `5eaca3b` | TASK-2106 + TASK-2107 | Migration runner + `ConfigEndpoints` + `ProfileAlignmentValidator`. Internal Reviewer + Codex high-risk override across cycle 1+2 (D6 outbox carry-forward + future-rejection + migrator-discard fixes). |
| Phase 4 | `7982643` | TASK-2108 + TASK-2109 | PCS hydration + UX profile editor. Reviewer cycle 1 clean. |
| Phase 5 | `2eaf7c3` | TASK-2110 | D11 18-scenario test matrix. Reviewer cycle 1 found tautological #15; cycle-2 fix extracted `ProfileAlignmentValidator.ValidateEffectiveFromTemporality` helper to pin production logic. |

## Step 7a — External Codex on Full Diff (cycle 1 + 10 verify cycles, 2026-05-03)

_Per WORKFLOW.md "high-risk Step 5a override" and the explicit S21 Phase-6 commitment, Step 7a ran `codex review --base 90ffa9b` against the full S21 diff after Phase 5 merge._

### Cycle 1 — 3 BLOCKERs (2 P1 + 1 P2)

- **C1 (P1)** — `LocalAgreementProfileRepository.SupersedeAndCreateAsync` closed predecessors at UTC `today` regardless of `newProfile.EffectiveFrom`. Backdated saves (allowed by D2; only future is rejected) produced overlapping windows for the (org, agreement, OkVersion) triple — historical reads, `GetActivationsInPeriodAsync` boundaries, and SLS export segments would have been wrong for the backdated period.
- **C2 (P1)** — Empty-slot first-creation race. `AcquireLockAsync` cannot `SELECT ... FOR UPDATE` a non-existent row, so two concurrent `If-None-Match: *` requests both pass `ValidatePrecondition`. The partial-unique-index `uq_local_agreement_profile_active` rejects the loser with `PostgresException` SqlState 23505 — bubbled to the PUT handler as a raw 500 instead of the advertised 412 D2.1 contract.
- **C3 (P2)** — `frontend/src/pages/config/ConfigManagement.tsx` `AGREEMENT_OPTIONS` hard-coded to `AC | HK | PROSA`, but the seed (`init.sql`) registers ACTIVE configs for `AC_RESEARCH` and `AC_TEACHING` as well. Admins on those orgs lost editor access after the S21 ConfigManagement rewrite.

### Cycles 2-9 — Iterative Exploration (all reverted)

A 9-cycle loop attempted to address a residual edge case that emerged from C1's fix: when `newProfile.EffectiveFrom <= predecessor.EffectiveFrom`, closing predecessor at `newProfile.EffectiveFrom - 1` produces `effective_to < effective_from` — an invalid history window. Each layered fix produced cascading regressions:

- **Cycle 3 (`<=` temporal-monotonicity guard)** — broke the everyday in-place edit flow (frontend submits unchanged loaded `effective_from`).
- **Cycle 6 (UPDATE-in-place for same-day)** — fixed the in-place flow but broke ETag/If-Match (profile_id stable across UPDATE → admins both pass `If-Match` → lost-update). Audit-action `MODIFIED` added without migration script for existing DBs.
- **Cycle 8 (PrecedingProfileId nulling + GetCurrentOpenAsync re-fetch)** — fixed cycle 6's payload bugs but didn't address the underlying ETag mechanism issue.

The architectural diagnosis (cycle 9): **ETag-via-profile_id + immutable history rows + close-then-insert do not compose for same-day re-saves.** Proper fix needs a row-version column (sibling to D2.2's ETag/If-Match propagation work) plus end-exclusive `effective_to` semantics (sibling to D6's transactional-outbox sub-sprint). Sized correctly for Phase-4 hardening, not for cycle-fix discipline.

### Cycle 10 — Reverted to Cycle 1 Only, Verified Clean

Cycles 3, 6, 8 reverted in commit `55a1aa8`. Cycle 4's frontend mapping for the cycle-3 error code also reverted (cycle 4 was companion polish to cycle 3). Cycle 10 Codex verify: **0 BLOCKERs, 0 WARNINGs**. Verbatim:

> "I did not identify any new, actionable defects in the current changeset that would clearly break existing behavior or violate the documented contracts. The backend changes address the backdated supersession overlap and the empty-slot concurrency race, and the frontend change restores the missing agreement-code options."

### Step 7a Carry-Forward (Phase-4)

`newProfile.EffectiveFrom <= predecessor.EffectiveFrom` produces an invalid-range predecessor row. Recorded in:

- **ADR-017** "For Phase 4 ROADMAP" section — D2 close-then-insert window math.
- **MEMORY.md** carry-forward list as sibling to D2.2 (ETag propagation) and D6 (transactional outbox) — those three concerns share the row-version + end-exclusive-effective_to redesign.

## Sprint Close Summary

- **Implementation phases:** 5 (Phase 1-5), 10 tasks (TASK-2101..2110).
- **Review cycles:** Phase 3 internal Reviewer + Codex cycle 1+2; Phase 5 internal Reviewer cycle 1+2; Step 7a external Codex cycle 1 + cycles 2-9 (iterative exploration, all reverted) + cycle 10 (verified clean).
- **Final test counts:** 517 unit (was 513 pre-S21; +4 from D11 unit tests), 35 plain regression (was 32 pre-S21; +3 from cycle-2 fix #15), 18 Docker-gated profile (was 0 pre-S21; +18 from D11 + Step 7a), 48 frontend (was 41 pre-S21; +7 from MondayDatePicker + ProfileEditor). Total **618 passing** (was 603).
- **Carry-forwards generated:** 2 from Phase 3 (D2.2 ETag propagation, D6 transactional outbox), 3 from Phase 5 (WebApplicationFactory hand-roll drift, reflection-on-private tests, ProfileTestSchema DDL drift), 1 from Step 7a (D2 close-then-insert window math). All recorded in MEMORY.md.

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
