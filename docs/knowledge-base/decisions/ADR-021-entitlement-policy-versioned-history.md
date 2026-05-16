# ADR-021 — Entitlement-Policy Versioned History for Phase 4d-2

| Field | Value |
|-------|-------|
| **Status** | ACCEPTED (S30 / TASK-3011 reviewed 2026-05-16 — Step 7a cycle 1 + cycle 2 both clean on the 2 P1 fix commits) |
| **Sprint** | S30 |
| **Domains** | Backend, Infrastructure, Frontend, Data Model |
| **Tags** | versioned-config, consumption-time-lookup, entitlement, supersession, soft-delete, seed-idempotency, phase-4d |
| **Supersedes** | none |
| **Amends** | none (ADR-016 D5b extension paragraph filed separately as additive — no Status bump) |

## Context

Phase 4d-2 implements versioned history for `entitlement_configs`. ADR-020 settled the foundational patterns for Phase 4d-1 (wage-type-mapping) and explicitly anticipated this sprint at §122:

> ADR-020 is scoped narrowly to the 3 architectural questions that thrashed in the deferred refinement, not a green-field design or a Phase 4d-2/3 preview. Phase 4d-2 and 4d-3 will produce their own ADRs when their sprints open; ADR-020 sets PATTERNS those ADRs may inherit, not bindings.

S30's TASK-3011 produces ADR-021 to settle the entitlement-specific decisions — including which ADR-020 patterns transfer verbatim, which need adaptation, and where the entitlement consumption shape introduces an entirely new pattern (consumption-time-lookup) that S29's WTM did not require.

The pattern transfer evaluation completed at refinement Step 4 (`.claude/refinements/REFINEMENT-s30-scope.md`, both lenses converged with 0 BLOCKERs at cycle 2):

- **D1 planner-enrollment from ADR-020 does NOT transfer**. Entitlement consumption is HTTP-endpoint-direct (Skema POST quota check + Balance summary GET), not planner-routed via `SegmentManifest`. There is no `PeriodCalculationService` segment expansion that needs a snapshot value for entitlements — consumers read live or dated rows directly from the repository at request time.
- **D2 3-case routing from ADR-020 transfers as-is** to `EntitlementConfigRepository.SupersedeAndCreateAsync`.
- **D3 seed idempotency from ADR-020 transfers as-is** (`ON CONFLICT (natural_key, effective_from) DO NOTHING`).
- **A new pattern emerges**: consumption-time-lookup. ADR-016 D5b reconciliation captures this as the "fifth pattern" (separate paragraph appended to ADR-016).

The 4 questions ADR-021 settles:

- **Q1 — Which fields are admin-editable vs frozen?** Five fields are editable via the new admin CRUD (`annual_quota`, `carryover_max`, `description`, `pro_rate_by_part_time`, `is_per_episode`, `min_age`). Two fields (`reset_month`, `accrual_model`) are agreement-defining and must NOT be edited via admin CRUD — they belong to the OK version + agreement contract.
- **Q2 — How do downstream consumers (Skema quota check + Balance summary) handle versioned history?** Both must resolve the entitlement-year-start config, not the live config — when an admin edits VACATION quota 25 → 27 today, an employee submitting a vacation absence today for the already-started entitlement year-Y must see year-Y-start's value (25), not the live value (27).
- **Q3 — What is the seed-idempotency contract under accumulated history?** Identical to ADR-020 D3 (`ON CONFLICT (entitlement_type, agreement_code, ok_version, effective_from) DO NOTHING`), but the natural-key triple differs.
- **Q4 — What is the soft-delete contract for downstream consumers?** Admin soft-delete leaves no live row for a (type, agreement, ok_version) tuple. Skema's quota check (`SkemaEndpoints.cs:335`) returns the live row's `ResetMonth` for year-start derivation — when null, the absence-save loop silently skips the quota check (`if (liveConfig is null) continue;`). This is a behavior continuation from pre-S30 (where seed-missing rows produced the same skip) but post-S30 admin action can now produce this state intentionally. Documented as expected behavior.

## Decision

### D1 — Planner-enrollment from ADR-020 does NOT transfer

Entitlement consumption is HTTP-endpoint-direct, not planner-routed. There is no `SegmentManifest` to populate with entitlement values, no `PeriodCalculationService.MapSegmentToExportLinesAsync` analog for entitlements. The `IPlannerEnrollment` seam introduced in ADR-020 D1 is consumed at planner-time by export-line construction; entitlement consumers (Skema POST quota check + Balance summary GET) run outside the PCS path entirely. Filing ADR-021 as a sibling to ADR-020 — rather than as a D4 amendment — keeps the architectural lineage honest.

**Binding invariant**: `EntitlementConfigRepository` exposes dated-read methods (`GetByTypeAtAsync`, `GetByAgreementAtAsync`) directly to HTTP-endpoint consumers. There is no planner enrollment, no `SegmentSnapshot.Values["EntitlementXxx"]`, no S29-style snapshot contract.

### D2 — Supersession routing inherits ADR-020 D2 3-case verbatim

`EntitlementConfigRepository.SupersedeAndCreateAsync(conn, tx, ...)` implements the same 3-case routing as ADR-020 D2 under `SELECT ... FOR UPDATE`:

- **Case A — no predecessor at the natural key**: fresh INSERT with `effective_from = today, effective_to = NULL, version = 1`. Audit emits `CREATED` + `EntitlementConfigCreated` outbox event.
- **Case B — predecessor with `effective_from < today`**: predecessor STAYS closed at `(effective_from = predecessor_day, effective_to = today)`; fresh INSERT with `effective_from = today, effective_to = NULL, version = 1`. No history-unique-index collision. Audit emits `SUPERSEDED` (on predecessor) + `CREATED` (on new row) + `EntitlementConfigSuperseded` outbox event.
- **Case C — predecessor with `effective_from = today`** (same-day edit): UPDATE-and-reopen on the predecessor (apply new field values, version-bump, keep `effective_to = NULL`). Audit emits `UPDATED` + `EntitlementConfigSuperseded` outbox event.

The `(natural_key, effective_from)` history-unique-index enforces at-most-one-row-per-day per natural key. The partial-unique-index `WHERE effective_to IS NULL` enforces at-most-one-live-row per natural key.

**Binding invariant**: same as ADR-020 D2.

### D3 — Seed idempotency inherits ADR-020 D3 verbatim with adapted natural-key

`init.sql` seeds 30 entitlement_config rows with explicit `effective_from = '0001-01-01'` (pre-launch sentinel anchor) and `ON CONFLICT (entitlement_type, agreement_code, ok_version, effective_from) DO NOTHING`. The 4-column conflict target uses the history-unique-index, not the partial-unique-index, so post-admin-edit re-bootstrap is idempotent even when the natural-key's live row has moved to a later `effective_from`.

**Binding invariant**: `docker compose down -v && up` produces a deterministic post-seed state of exactly 30 rows with `effective_from = '0001-01-01'` regardless of prior admin activity in the dropped volume.

### D4 — Consumption-time lookup: two-step pattern (the new pattern)

Skema quota check (`SkemaEndpoints.cs:313`) and Balance summary (`BalanceEndpoints.cs:125`) BOTH resolve entitlement configs via a two-step pattern:

1. **Step 1 — live read**. Get the live (open) rows for the employee's (agreement, ok_version) pair to discover the natural keys + each row's `ResetMonth`. Skema uses `GetCurrentOpenAsync(type, agreement, ok)` (per-type, live-only). Balance uses `GetByAgreementAsync(agreement, ok).Where(c => c.EffectiveTo is null)` (per-bulk + inline live filter — see Step 7a fix #1 below).
2. **Step 2 — dated read at the entitlement-year-start**. Derive `entitlementYearStart` from the live `ResetMonth` + the relevant date (per-absence date in Skema; per-month date in Balance), then issue `GetByTypeAtAsync(type, agreement, ok, asOfDate=entitlementYearStart)` to get the config that was effective at the year boundary.

Because `ResetMonth` is frozen per natural key (D5), Step 1's live ResetMonth is safe to use for year-start derivation across the full history — there is no version skew where a closed predecessor had a different ResetMonth.

Fallback: if Step 2 returns null (no row was effective at year-start — e.g., the OK version came into existence mid-year), Balance summary falls back to the live row so the entitlement still appears with current quota values. Skema's quota check treats Step 2 null as "no quota enforcement" (silently skips per Q4 contract).

**Binding invariant**: post-supersession of VACATION/AC/OK24 from quota=25 to quota=27 today, an absence submitted today for the already-started entitlement year-Y reports quota=25 (year-Y-start's value), not quota=27 (live value). This is the marquee D-test `EntitlementQuotaCheck_UsesYearStartConfig_NotCurrentConfig`.

#### D4-supplement — Bulk-read live filter (Step 7a fix #1)

Balance summary's Step 1 must filter `EffectiveTo IS NULL` before iterating. `EntitlementConfigRepository.GetByAgreementAsync` returns ALL rows for the (agreement, ok_version) pair — open + closed predecessors. Without the live filter, the consumption loop iterates BOTH rows per natural key post-supersession, producing duplicate entitlement entries in the summary and overwriting `vacationDaysEntitlement` with whichever row is visited last. The inline `.Where(c => c.EffectiveTo is null)` at `BalanceEndpoints.cs:125-127` (Step 7a P1 fix #1, commit `374960a`) is load-bearing.

Skema's per-type live read (`GetCurrentOpenAsync`) is already live-filtered server-side — no consumer-side filter needed. The asymmetry between Skema (per-type live) and Balance (bulk + inline filter) is acceptable because their access patterns differ: Skema knows the type at request time (from the absence payload); Balance must enumerate all 5 types per employee.

### D5 — `reset_month` + `accrual_model` are frozen from admin scope (Q1 sub-fork (i))

These two fields are agreement-defining: `reset_month` is the calendar boundary the OK version + agreement uses for the entitlement year ("9" for academic-cycle agreements, "1" for calendar-year agreements); `accrual_model` is the legal accrual basis (`IMMEDIATE` for AC/HK; `MONTHLY_ACCRUAL` reserved for future activation per D6). Changing either field via admin CRUD would either (a) silently break the two-step consumption pattern's year-start derivation (a closed predecessor with a different `ResetMonth` would skew the historical lookup), or (b) require a cross-domain orchestrated migration (recompute existing `entitlement_balances`, re-emit prior payroll exports, re-derive carryover_in for every employee on the affected natural key) that is out of scope for an admin-CRUD workflow.

Enforcement: `EntitlementConfigEndpoints` admin POST + PUT validator inspects the request payload; if the predecessor live row exists and either `reset_month` or `accrual_model` differs from the predecessor's value, the endpoint returns 422 with structured error body:

```json
{
  "error": "reset_month and accrual_model are agreement-defining and cannot be edited via admin CRUD; create a new ok_version row instead",
  "supplied": { "reset_month": 1, "accrual_model": "MONTHLY_ACCRUAL" },
  "immutable": ["reset_month", "accrual_model"]
}
```

The frontend admin page displays both fields read-only. Admins who need to change them must do so via DB superuser (e.g., during an OK-version cutover).

**Binding invariant**: post-S30, no admin-scope mutation can produce a (natural_key, effective_from) row pair where the two rows disagree on `reset_month` or `accrual_model`. This invariant lets the D4 two-step pattern rely on the live row's `ResetMonth` for year-start derivation without re-checking the dated row.

### D6 — `MONTHLY_ACCRUAL` enum value remains dead code

`AccrualModel` enum at `src/SharedKernel/StatsTid.SharedKernel/Models/EntitlementConfig.cs:15` declares two values: `IMMEDIATE` and `MONTHLY_ACCRUAL`. Today (S30 close), only `IMMEDIATE` is used in seed data + admin CRUD; `MONTHLY_ACCRUAL` has no consuming code path (no rule applies proportional monthly grants). The enum value is kept for forward compatibility — activating it would require:

- A `MonthlyAccrualGrantRule` (or PCS extension) that grants `1/12 * annual_quota` at each month-start during the entitlement year
- Carryover semantics under mid-year activation (does the partial-year balance reset at the activation date, or accumulate from the start of the entitlement year?)
- A migration story for existing balances that were granted under `IMMEDIATE`

These are scoped out of Phase 4d-2 and tracked as Phase 5+ work. D5's freeze-from-admin rule prevents admins from accidentally setting an entitlement to `MONTHLY_ACCRUAL` during S30, so the dead enum value cannot cause runtime divergence.

### D7 — Soft-delete consumption contract (Q4)

When admin soft-deletes the live row for a (type, agreement, ok_version) tuple via DELETE `/api/admin/entitlement-configs/{id}`, the live row is closed (`effective_to = today`). Downstream consumers behave as follows:

- **Skema POST quota check** (`SkemaEndpoints.cs:335`): per-type `GetCurrentOpenAsync` returns null. The `if (liveConfig is null) continue;` short-circuit silently skips the quota check for this absence-type — the absence is saved without quota enforcement. This is a behavior continuation: pre-S30, the same skip fired when seed data was missing a (type, agreement, ok_version) row. Post-S30, admin action can produce this state.
- **Balance summary GET** (`BalanceEndpoints.cs:125`): per-bulk `GetByAgreementAsync().Where(c => c.EffectiveTo is null)` excludes the soft-deleted row. The entitlement entry simply does not appear in the summary's `entitlements` array for that type — the UI shows no card.

**Design intent**: soft-delete is interpreted as "this entitlement is being retired for this (agreement, ok_version)" — admins are responsible for either (a) creating a new ok_version row with the replacement entitlement, or (b) accepting that the entitlement is gone forward (e.g., legacy SENIOR_DAY phased out). The silent quota skip is acceptable under this interpretation because the next admin action will either restore an entitlement row (resuming enforcement) or the entitlement will remain absent (no enforcement needed). Documented here so future Phase 5+ work that wants stricter enforcement has a known starting point.

## Implications

### 1. `entitlement_balances` is unchanged

The `entitlement_balances` table remains the authoritative source of "consumed and remaining" for each (employee, entitlement_type, year). Admin edits to `entitlement_configs` are forward-only — pre-S30 balances are NOT retroactively recomputed when an admin edits an annual_quota. This is asserted by the marquee D-test's predecessor closure check + by sprint-close validation criterion ("No `entitlement_balances` rows retroactively recomputed by admin edit").

### 2. Audit table separates from outbox events

`entitlement_config_audit` mirrors `wage_type_mapping_audit` post-S25 shape (singular). Columns: `audit_id BIGSERIAL PK`, `config_id UUID NOT NULL`, denormalized natural-key, `action TEXT CHECK (action IN ('CREATED','UPDATED','DELETED','SUPERSEDED'))`, `previous_data JSONB`, `new_data JSONB`, `version_before BIGINT NULL` + `version_after BIGINT NULL` (ADR-019 D8), `actor_id TEXT NOT NULL`, `actor_role TEXT NOT NULL`, `timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW()`. NOT FK-constrained to `entitlement_configs.config_id` (supersession + soft-delete create FK-invalidating histories — mirrors `wage_type_mapping_audit`).

3 outbox event types: `EntitlementConfigCreated`, `EntitlementConfigSuperseded`, `EntitlementConfigSoftDeleted`. Stream-naming follows natural-key: `entitlement-config-{entitlement_type}-{agreement_code}-{ok_version}` (mirrors S29 WTM precedent). One stream per supersession lineage.

EventSerializer registration: 48 → 51 typeof. S18 reflection-coverage test (`EventSerializerCoverageTests`) auto-covers via reflection scan; no test edit needed.

### 3. Admin CRUD endpoints are GlobalAdminOnly

All 5 endpoints (POST/PUT/DELETE/GET-list/GET-by-key) under `/api/admin/entitlement-configs` carry `.RequireAuthorization("GlobalAdminOnly")` per ADR-019 admin-strict If-Match contract + S29 WageTypeMappingEndpoints precedent. ADR-019 D2/D5/D6/D8 contract enforced verbatim: 412 stale / 428 missing / 409 disjoint / version-transition columns on every audit insert.

### 4. Frontend admin page uses full update-request shape (Step 7a fix #2)

`useEntitlementConfig.updateConfig` accepts `EntitlementConfigUpdateRequest` (the full backend shape: natural-key + frozen fields + editable patch + `effectiveFrom`), not the narrower `EntitlementConfigPatch`. The page (`EntitlementConfigEditor.tsx`) sources the immutable fields from the `editing: WithEtag<EntitlementConfig>` state (displayed read-only) and assembles the full request body at submit time with `effectiveFrom = today`. The backend `UpdateEntitlementConfigRequest` requires all these fields; the patch-only shape would have produced a 400 at JSON binding on every PUT. The narrower `EntitlementConfigPatch` interface is kept for the form-state shape (`editFormToPatch`) — it represents the editable subset, not the wire shape.

### 5. ADR-016 D5b extended with "fifth pattern" paragraph

ADR-016 D5b reconciliation grows a fifth pattern paragraph documenting consumption-time-lookup. The first four patterns from prior ADRs are:

1. Planner-routed effective-date lookup via `SegmentManifest` (ADR-016 D5 — the original pattern for OK-version transitions and agreement-config promotions)
2. Local-profile activation via `BoundaryCause.LocalProfileActivation` (ADR-017)
3. Atomic outbox-bound effective-date lookup (ADR-018 D14)
4. Export-time effective-date lookup via `Snapshot.Values["WtmNaturalKey"]` (ADR-016 D5b S29 amendment, ADR-020 D1)

The fifth pattern — consumption-time-lookup — is the HTTP-endpoint-direct two-step pattern from ADR-021 D4 (live ResetMonth → derive year-start → dated read). It is the appropriate shape for any future versioned config whose consumers are HTTP endpoints rather than planner-routed export lines.

## Alternatives Considered

### A1 — Apply ADR-020 D1 planner-enrollment to entitlements anyway

Rejected. Entitlement consumption has no segment expansion. Forcing entitlements through `IPlannerEnrollment` would require either (a) inventing synthetic segments for HTTP-endpoint requests (architectural inversion), or (b) bypassing the seam in practice (the seam exists but is never invoked) — both worse than admitting consumption-time-lookup is a different pattern. Filed as ADR-021 D1 explicitly.

### A2 — Allow `reset_month` / `accrual_model` admin edits with full orchestrated migration

Rejected for S30 scope. Permitting these edits would require a multi-step admin workflow (preview impact → recompute balances → re-emit payroll exports → notify employees) that is out of scope for a single-screen CRUD. Frozen-from-admin (D5) is the pragmatic choice; admins who genuinely need to change these go to DB superuser (rare event tied to OK-version cutovers).

### A3 — Use the single-row `GetCurrentOpenAsync` pattern (Skema style) in Balance summary as well

Rejected. Balance summary needs to enumerate all 5 entitlement types per employee. Per-type live reads would require 5 round-trips to the DB; the existing per-bulk `GetByAgreementAsync` returns all types in one query. The asymmetry between Skema (per-type, live-filter server-side) and Balance (per-bulk, live-filter inline) is acceptable. Considered + rejected at refinement Step 4 cycle 2.

### A4 — Recompute existing `entitlement_balances` on admin edit

Rejected. Admin edits are forward-only by design. Recomputing balances would require re-reading every prior absence's effective-date config (to determine retro-effective quota), then re-running carryover_in derivation for affected employees — a workflow that is not appropriate for an admin-CRUD action. Documented in Implications §1.

## Refinement Trail

`.claude/refinements/REFINEMENT-s30-scope.md` (3 lens cycles + 2 plan-mode cycles + 3 plan-review cycles + 1 cycle-cap waiver per `feedback_step7a_cycle_cap_discipline.md` + `feedback_thrash_defer_real_world.md`):

- **Refinement Step 4 cycle 1**: 4 BLOCKERs (D2 routing transfer applicability, soft-delete consumption contract, fixture DDL drift, reset_month freeze framing). All fixed by cycle 2.
- **Refinement Step 4 cycle 2**: 0 BLOCKERs (both lenses). Lens convergence reached — second cycle-2-converging-finite case after S29 per `feedback_thrash_defer_real_world.md`.
- **Plan Review (Step 0b) cycles 1–3**: Codex went BLOCKER (owner-relabel form-1) → BLOCKER (mechanical wording strict compliance) → 0 BLOCKER. Reviewer: 0 BLOCKER throughout. Cycle 3 ran under user-granted cycle-cap waiver (S21 precedent); cycle 3 clean.
- **Step 7a (sprint end) cycles 1–2**: cycle 1 surfaced 2 fix-required BLOCKERs (convergent BalanceEndpoints duplicate-rows P6 + Codex-only frontend PUT payload P9) + 1 narrative BLOCKER (ADR-021 missing — addressed by this file). Both code BLOCKERs fixed via commits `374960a` (BalanceEndpoints filter + new D-test) and `a2e8d83` (frontend updateConfig shape). Cycle 2: clean re-verify on the fixed diff.

## Status History

- **2026-05-14**: Sprint open. Plan filed (PLAN-s30.md cycle 3 clean). ADR-021 reserved as TASK-3011.
- **2026-05-16**: Sprint close. ADR-021 filed as ACCEPTED with Step 7a cycle 1+2 review trail. ADR-016 D5b extended with fifth-pattern paragraph in separate edit.

## Related ADRs

- **ADR-016** — Temporal Period Handling (D5b extended with fifth pattern in S30)
- **ADR-018** — Transactional Outbox + Row-Version Optimistic Concurrency (D14 export-time effective-date lookup)
- **ADR-019** — Optimistic Concurrency via Row-Version (D2/D5/D6/D8 admin-strict If-Match contract — inherited)
- **ADR-020** — Versioned-Config Design Foundations for Phase 4d-1 (D2 + D3 inherited; D1 explicitly does NOT transfer)
