# Sprint 21 — Local Agreement Configuration Rework

| Field | Value |
|-------|-------|
| **Sprint** | 21 |
| **Status** | planned (analysis-first — no task log until Step 0 analysis is complete) |
| **Start Date** | TBD |
| **End Date** | TBD |
| **Orchestrator Approved** | no |
| **Build Verified** | n/a |
| **Test Verified** | n/a |

## Sprint Goal

Reshape local agreement configuration from a flat per-key patch bag into a **profile model**: one local agreement profile per `(orgId, agreementCode, OkVersion)` with the centrally-overridable subset of fields exposed as editable inputs and the remainder pinned read-only to the central config. Today's `local_configurations` table allows unbounded active rows per `(org, key)` with no uniqueness constraint, no parent identity, and gives admins no UX signal about which fields they may even adjust. The intended product behaviour — visible in `SYSTEM_TARGET.md` § G "Local Configuration" — is a single editable local version of the agreement per org, not a bag of overrides.

**This sprint begins with architectural analysis. No implementation tasks are listed. The first sprint activity is to produce an ADR (or amend ADR-010) and a task decomposition; implementation tasks are drafted only after that analysis is Orchestrator-approved.**

## Problem Statement

### What exists today

- **Schema** (`docker/postgres/init.sql:449`): `local_configurations` is row-per-key with PK on `config_id` and indexes on `org_id` and `config_area`. **No uniqueness** on `(org_id, agreement_code, ok_version, config_key)`. Multiple active rows for the same key are physically allowed.
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

### What "right" looks like (proposed direction, subject to ADR)

- A new logical entity: `local_agreement_profile` keyed `(org_id, agreement_code, ok_version)`, **unique**.
- The overridable fields are physical columns on the profile (the columns *are* the whitelist) — `weekly_norm_hours`, `max_flex_balance`, `flex_carryover_max`, `max_overtime_hours_per_period`, `overtime_requires_pre_approval`. NULL = "inherit central."
- Effective-dating happens at the profile level: one active profile per `(org, agreement, OkVersion)` at any point in time, with `effective_from` / `effective_to` and historical predecessors retained.
- The admin UI renders the *full* central agreement, with editable inputs on the overridable columns and read-only renderings on everything else. Save persists only the deltas (NULL-out unchanged columns).
- Existing `local_configurations` rows migrate into profile columns; legacy rows for unknown keys (informational/typo) are dropped with an audit-log emission.
- ADR-010's "merge at service layer" stays — the resolution chain becomes "central → position override → local profile" with the same `AgreementRuleConfig` returned to the rule engine.

## Context and Existing Partial Solutions

Work the new design must build on or reconcile with:

- **`src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs`** — current per-key switch; will become per-column merge against the profile entity.
- **`src/Infrastructure/StatsTid.Infrastructure/LocalConfigurationRepository.cs`** — current repository; either reshaped or supplemented by a new `LocalAgreementProfileRepository`.
- **`src/SharedKernel/StatsTid.SharedKernel/Models/LocalConfiguration.cs`** — current per-row model; profile model is additive.
- **`src/SharedKernel/StatsTid.SharedKernel/Events/LocalConfigurationChanged.cs`** — append-only event, must continue to be emitted; profile-level "changed" events likely want their own event type.
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

These must be resolved — and documented in an ADR (likely an amendment to ADR-010 or a new ADR-016) — **before** a task decomposition is drafted:

1. **Profile uniqueness scope.** Is the unique key strictly `(org_id, agreement_code, ok_version)`, or `(org_id, agreement_code, ok_version, effective_from)` to allow planned-future profiles? If the latter, how is "the active profile right now" resolved deterministically?
2. **Effective-dating model.** One active profile at a time with explicit `effective_to` on supersession, or overlapping ranges with date-window selection at resolve time? (S20's Temporal Period Handling sprint is queued and may give us the framework — does S21 inherit its decision or precede it?)
3. **Lifecycle states.** Does the profile need DRAFT/ACTIVE/ARCHIVED like agreement configs (ADR-014), or is "active vs deactivated" sufficient given local profiles change rarely and rarely need preview?
4. **Migration strategy.** Big-bang migration of all existing `local_configurations` rows into profiles, or coexistence period where both tables resolve and conflicts are surfaced? Big-bang is simpler; coexistence is safer if there's hidden production data we don't fully understand.
5. **Schema choice for the override fields.** Nullable columns on a wide profile table (each NULL = inherit), or a JSONB blob with a known schema, or a normalized child table per overrideable field? Wide-nullable is admin-readable and easy to query; JSONB is flexible if the overridable set grows; normalized is over-engineering for ~5 fields.
6. **What about `PlanningStartDay` / `ApprovalCutoffDay` (the informational keys)?** They're stored in `local_configurations` today but never read by the rule engine. Are they (a) part of the profile, (b) a separate `org_operational_settings` table, or (c) deleted entirely?
7. **API shape.** Replace the existing per-row endpoints (`POST /api/config/{orgId}`, etc.) with profile-shaped equivalents (`PUT /api/config/{orgId}/profile/{agreement}/{okVersion}`, `GET /api/config/{orgId}/profile/{agreement}/{okVersion}`)? Add new endpoints alongside, then deprecate the old ones in a later sprint? This is a P9/P7 trade-off (clean break vs deprecation runway for any external callers).
8. **Event shape.** One `LocalAgreementProfileChanged` event with the full delta payload, or per-field events for granular replay? `LocalConfigurationChanged` (existing) is per-key — does it stay for legacy and add a new event type, or evolve via versioned event payloads?
9. **Position override interaction.** Position overrides (S11/S14, `PositionOverrideRepository`) currently apply between central and local in the resolution chain. Does the profile sit at the same level (overrides position overrides), beside it, or stacks predictably? Document the precedence in the ADR.
10. **Backwards compatibility for existing API consumers.** The current `POST /api/config/{orgId}` is callable today by any LocalAdmin. Are there known external integrations that would break if it changes shape? If yes, redirect-with-shim; if no, replace.
11. **Test strategy.** Migration test (existing rows → profile values), uniqueness enforcement test, NULL-as-inherit resolution test, audit-trail test. What subset is the sprint-committed regression coverage and what's deferred?

## Scope Boundary

### In scope
- Architecture & ADR for the profile model (or amendment to ADR-010).
- Schema migration: new `local_agreement_profiles` table, migration of existing `local_configurations` rows, audit-log handling for the migration itself.
- `ConfigResolutionService` refactored to consume profiles instead of per-key rows.
- New profile-shaped endpoints (or replacement of existing per-row endpoints — to be decided by ADR).
- Rebuilt admin UI: single profile editor per `(org, agreement, OkVersion)` showing all fields with editable inputs on the overridable subset.
- Regression coverage: profile uniqueness, migration correctness, NULL-as-inherit resolution, audit emission.

### Out of scope
- Changing what is overridable (the `MaxFlexBalance`, `WeeklyNormHours`, etc. set stays the same — discussion of widening or narrowing it is a separate product question).
- Touching position overrides (S11/S14) beyond documenting the precedence interaction.
- Touching agreement-config management (S12, GlobalAdmin self-service) — that's the central side.
- Frontend polish beyond the profile editor screen.
- Any rule-engine changes — this sprint is configuration plumbing, not calculation logic.

## Planning Entrypoint

No implementation tasks are defined yet. The sprint begins with the following **analysis-phase deliverables**, produced by the Orchestrator in collaboration with the user before any domain agent is spawned:

1. **Architectural ADR** — answering the eleven open questions above. Likely an amendment to ADR-010 or a new ADR-016 ("Local Agreement Configuration as a Profile").
2. **Migration plan** — explicit handling of multi-row-per-key cases, informational keys, and unknown keys in the existing data.
3. **Task decomposition** — the ADR translated into `TASK-21NN` entries with domain agents, file scopes, and validation criteria, added to this sprint log under "## Task Log".
4. **Entropy scan (Step 0a)** — run at the actual sprint start date, recorded in the header above.

Only after items 1–3 are Orchestrator-approved does Step 2 (Delegate) begin.

## References

- [CLAUDE.md](../../CLAUDE.md) — priority order (P1, P7, P9 driving this sprint)
- [ROADMAP.md](../../ROADMAP.md) — Phase 3i placement
- [SYSTEM_TARGET.md § G](../../SYSTEM_TARGET.md) — Local Configuration product spec
- [docs/knowledge-base/decisions/ADR-010-local-config-merge-at-service-layer.md](../knowledge-base/decisions/ADR-010-local-config-merge-at-service-layer.md) — current local config architecture
- [docs/knowledge-base/decisions/ADR-014-db-backed-agreement-configs.md](../knowledge-base/decisions/ADR-014-db-backed-agreement-configs.md) — DRAFT/ACTIVE/ARCHIVED lifecycle precedent
- [SPRINT-20.md](SPRINT-20.md) — sibling analysis-first sprint, structurally similar
- [docker/postgres/init.sql](../../docker/postgres/init.sql) — current `local_configurations` table (line 449)
- [src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs](../../src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs) — current resolution (line 159 onward)
- [src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs](../../src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs) — current API (line 134 onward)
- [frontend/src/pages/config/ConfigManagement.tsx](../../frontend/src/pages/config/ConfigManagement.tsx) — current admin UI
