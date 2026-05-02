# ADR-017: Local Agreement Configuration as a Profile

| Field | Value |
|-------|-------|
| **Status** | Accepted |
| **Date** | 2026-05-02 |
| **Sprint** | S21 |
| **Domains** | Infrastructure, Backend API, SharedKernel, Frontend, Data Model |
| **Tags** | local-config, profile, configuration, effective-dating, schema, migration |
| **Amends** | ADR-010 (merge-at-service-layer) |
| **Augments** | ADR-014 (partial-unique-index pattern reused), ADR-016 (BoundarySources extended with `LocalProfileActivation`) |
| **Decisions** | D1–D12 below |

## Context

Sprint 6/7's ADR-010 established that local agreement overrides merge into the central `AgreementRuleConfig` at the service layer (`ConfigResolutionService`), keeping the rule engine pure. The data shape it adopted — a flat `local_configurations` table with one row per `(org_id, agreement_code, ok_version, config_key)` — was the path of least resistance. Each override was independent, validation per-key, audit per-row.

Three product-level failure modes have surfaced since:

1. **Duplicate-row drift.** The schema's 6-tuple uniqueness constraint at `init.sql:467` includes `effective_from`, so admins editing a key over time create new rows rather than updating in place. `effective_to` is rarely set; `is_active` is independently mutable; resolution picks one row by iteration order. Three "active" rows for the same key are physically possible.

2. **Silent no-op overrides.** Typing a typo (`MaxOvetimeHoursPerPeriod`) succeeds at the API; the row persists but resolution skips it with a `LogDebug` warning. Admins have no compile-time, save-time, or runtime feedback that their override applied nothing.

3. **No "this is *the* local OK26" identity.** The product framing — "our local version of an agreement" — is not surfaced anywhere. The closest analog is "the union of all active rows in `local_configurations` for this org/agreement/OK", applied left-to-right.

S21's task is to reshape the underlying entity from a patch bag into a **profile**: one row per `(org_id, agreement_code, ok_version)`, columns are the whitelist of overridable fields, NULL means inherit central. ADR-010's "merge at service layer" invariant stays; the data shape changes.

## Decision

S21 introduces `local_agreement_profiles` as the canonical entity. Existing `local_configurations` rows migrate big-bang (Q4 → option i). The profile entity has wide-nullable columns (Q5), partial-unique-index uniqueness (Q1 → option c), and `is_active` boolean lifecycle (Q3). Profile activations become first-class boundary sources in S20's planner (Q12 → option d), with save-time alignment validation enforcing aligned-window rules' constraints. The patch-bag merits identified at plan time (per-row event granularity, per-field effective-dating independence) are deliberately rejected in favor of profile-level atomicity for events, audit, and admin UX.

The merits the rewrite preserves:
- ADR-010's merge-at-service-layer invariant (rule engine purity).
- ADR-014's DRAFT/ACTIVE/ARCHIVED-style partial-unique-index pattern, narrowed to active-vs-deactivated.
- ADR-016's `BoundarySources` planner seam, extended additively.

The merits the rewrite rejects:
- Per-row event granularity: profile-wide saves emit one `LocalAgreementProfileChanged` event with a delta payload (Q8a). Per-field decomposition is artificial post-hoc — the writer's atomic action becomes one event.
- Per-field effective-dating independence: the profile is a single effective-dated unit. Time-shifted overrides for unrelated fields are not supported; admins schedule them as separate profile saves (Q2 → no scheduled-future).

## Detailed Decisions

### D1 — Profile entity, schema, and uniqueness scope (Q1, Q3, Q5)

New table `local_agreement_profiles`:

```sql
CREATE TABLE local_agreement_profiles (
    profile_id                          UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id                              TEXT         NOT NULL REFERENCES organizations(org_id),
    agreement_code                      TEXT         NOT NULL,
    ok_version                          TEXT         NOT NULL,
    effective_from                      DATE         NOT NULL,
    effective_to                        DATE,
    is_active                           BOOLEAN      NOT NULL DEFAULT TRUE,

    -- Overridable fields (5). NULL = inherit central.
    weekly_norm_hours                   NUMERIC(5,2),
    max_flex_balance                    NUMERIC(6,2),
    flex_carryover_max                  NUMERIC(6,2),
    max_overtime_hours_per_period       NUMERIC(6,2),
    overtime_requires_pre_approval      BOOLEAN,

    created_by                          TEXT         NOT NULL,
    created_at                          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    -- Q1 option (c): partial-unique-index. At most one open-ended profile per (org, agreement, OkVersion).
    -- Closed predecessors (effective_to NOT NULL) are unconstrained — they're history.
    -- Pattern reused from ADR-014 (agreement_configs ACTIVE rows).
    CONSTRAINT uq_local_agreement_profile_active
        UNIQUE NULLS NOT DISTINCT (org_id, agreement_code, ok_version, effective_to)
);
```

Note: Postgres 15+ supports `UNIQUE NULLS NOT DISTINCT`; for the partial-index variant: `CREATE UNIQUE INDEX uq_local_agreement_profile_active_partial ON local_agreement_profiles (org_id, agreement_code, ok_version) WHERE effective_to IS NULL`. The index variant is preferred; the table-level constraint above is illustrative.

The 5 overridable fields are exactly those classified as overlays in `ConfigResolutionService.cs` (`MaxFlexBalance`, `FlexCarryoverMax`, `WeeklyNormHours`, `MaxOvertimeHoursPerPeriod`, `OvertimeRequiresPreApproval`). The 21 protected keys are absent from the schema by construction — they cannot be locally overridden because there is no column to store them. The 2 informational keys (`PlanningStartDay`, `ApprovalCutoffDay`) are absent per D4 (deleted entirely; see Q6 sub-question 6a).

Lifecycle (Q3): `is_active` boolean. Deactivation means setting `is_active = FALSE`; the partial-unique-index's `WHERE effective_to IS NULL` predicate captures the "currently-open" row, which is the only relevant invariant. ADR-014's DRAFT/ACTIVE/ARCHIVED tri-state is overkill — local profiles change rarely, no preview workflow.

Schema choice (Q5): wide-nullable columns. JSONB is over-engineering for 5 fields; normalized child tables are over-engineering for non-list values; wide-nullable is admin-readable and queryable.

### D2 — Effective-dating model: supersession via close-then-insert (Q2)

Activation is atomic, transactional, and has no scheduled-future support:

```sql
BEGIN;
UPDATE local_agreement_profiles SET effective_to = '2026-05-01' WHERE profile_id = @currentProfileId;
INSERT INTO local_agreement_profiles (profile_id=..., effective_from='2026-05-02', effective_to=NULL, ...);
COMMIT;
```

The partial-unique-index never fires during the transaction because the old row's `effective_to` is non-NULL by the time the new row is inserted. Closed predecessors are retained for history.

**No scheduled-future profiles.** An admin who wants a change effective 2026-07-01 sets a calendar reminder and edits on the day. The schema *technically* supports it (close-then-insert with effective_from in the future), but the second-admin race scenario (two admins overlapping with future-pending changes) cannot be schema-enforced — partial-unique only fires when both candidates have NULL `effective_to`, and a future-pending row has NULL `effective_to` while the current row has a closed `effective_to`.

If scheduled-future ever becomes a product requirement, the escape hatch is to migrate to a `daterange` exclusion constraint (PostgreSQL `btree_gist` extension); the per-profile data is unchanged. Documented for posterity; not implemented in S21.

### D3 — Resolution chain: closed pre-commit (Q9)

The resolution chain stays `central → position override → local profile`, matching `ConfigResolutionService.cs:69-129` and ADR-014's documented order. Profile overrides position overrides; position overrides override central. This is **not** an open question in S21.

`ConfigResolutionService` reads the profile by date: select the row where `effective_from <= @asOfDate AND (effective_to IS NULL OR effective_to >= @asOfDate)`. For each overridable field, NULL means "inherit central"; non-NULL replaces central. Position overrides are applied between central and profile lookup as today.

### D4 — Migration strategy: big-bang cutover (Q4)

The data audit (SPRINT-21.md "Data Audit (Deliverable #1)") confirmed:
- 3 seed rows, no failure modes exercised.
- No DB-backed test fixtures.
- No production data (system pre-production).

Big-bang cutover is therefore safe. Migration runs in a single transaction:

1. For each `(org_id, agreement_code, ok_version)` tuple in `local_configurations`, materialize a profile row populated from the most-recently-effective per-key row.
2. Drop `local_configurations` rows for `PlanningStartDay` and `ApprovalCutoffDay` with audit-log emission `{action: "DROPPED_INFORMATIONAL", reason: "Q6 deletion per ADR-017"}`.
3. Drop unknown-key rows with audit-log emission `{action: "DROPPED_UNKNOWN_KEY", reason: "key not in overridable schema"}`.
4. After migration, `local_configurations` table becomes read-only (no writes; reads continue to deserialize for historical event-store replay until a separate cleanup sprint removes the table).

The migration test fixtures (Q11 / D11 below) synthesize the failure modes that seed data lacks: row collision, typo'd key, informational key, expired-but-active row, multi-row-per-key.

### D5 — API shape: replace existing per-row endpoints (Q7, Q10)

System is pre-production; no external consumers exist. The React frontend is the only consumer of `POST /api/config/{orgId}` and is being rebuilt anyway (D9 + frontend task). Clean break:

- **Removed:** `POST /api/config/{orgId}` (per-row write), `GET /api/config/{orgId}/local` (per-row list), `DELETE /api/config/{orgId}/{configId}` (per-row delete).
- **Added:** `PUT /api/config/{orgId}/profile/{agreementCode}/{okVersion}` (profile-shaped write — full or partial delta), `GET /api/config/{orgId}/profile/{agreementCode}/{okVersion}` (current-active profile read), `GET /api/config/{orgId}/profile/{agreementCode}/{okVersion}/history` (closed predecessors).

The PUT endpoint accepts a request body shaped as the profile's overridable subset; fields omitted from the request preserve their current value, fields explicitly set to `null` revert to inherit-central. Save-time validation runs (D9 alignment policy) and either creates a new profile via close-then-insert or returns 400 with structured per-field errors.

### D6 — Domain event shape: one event per save with delta payload (Q8a)

New event `LocalAgreementProfileChanged extends DomainEventBase`:

```csharp
public sealed class LocalAgreementProfileChanged : DomainEventBase
{
    public override string EventType => "LocalAgreementProfileChanged";

    public required Guid ProfileId { get; init; }
    public required string OrgId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public required IReadOnlyDictionary<string, FieldChange> ChangedFields { get; init; }
    public required string ActorId { get; init; }
    public required string ActorRole { get; init; }
    public Guid? PrecedingProfileId { get; init; }  // null on first profile creation
}

public sealed record FieldChange(JsonElement Old, JsonElement New);
```

One event per save. `ChangedFields` carries per-field old/new pairs. `PrecedingProfileId` enables walking the supersession chain. `EventSerializer` registers the new type (event count 44 → 45).

Per-field decomposition into multiple events is rejected: profile-wide saves are atomic at the API; the audit unit should match the action unit.

### D7 — Legacy event compatibility (Q8b)

`LocalConfigurationChanged` (existing per-row event):
- **Stops being emitted** after S21. The per-row API that emitted it is removed.
- **Stays registered** in `EventSerializer` indefinitely. Pre-S21 events in the event store remain readable by all consumers (replay, audit query, projection rebuild).
- **Stays present** in `src/SharedKernel/Events/`. Not deleted. Marked with an XML doc comment "post-S21: emission retired; deserialization preserved for pre-S21 event-store history."

ADR-002's append-only event-store invariant forbids rewriting existing events. Old events are historical artifacts; they describe the world as it was when the per-row API was the active write path.

### D8 — Audit projection shape: profile-shaped (Q14)

New table `local_agreement_profile_audit`:

```sql
CREATE TABLE local_agreement_profile_audit (
    audit_id      BIGSERIAL    PRIMARY KEY,
    profile_id    UUID         NOT NULL,
    action        TEXT         NOT NULL CHECK (action IN ('CREATED', 'SUPERSEDED', 'DEACTIVATED')),
    delta_jsonb   JSONB        NOT NULL,  -- mirrors LocalAgreementProfileChanged.ChangedFields
    actor_id      TEXT         NOT NULL,
    actor_role    TEXT         NOT NULL,
    timestamp     TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_local_profile_audit_profile ON local_agreement_profile_audit(profile_id);
```

One audit row per save. `delta_jsonb` mirrors the event's `ChangedFields` shape — same data, two persistence sites (event store + projection), identical semantics.

The legacy `local_configuration_audit` table (`init.sql:473-483`) stays for pre-S21 historical audit queries; after S21 it stops receiving new rows. No migration of old audit rows — they remain queryable in their original `(config_id, previous_value, new_value)` shape.

### D9 — Profile activation as first-class boundary source + save-time alignment validation (Q12)

The full Q12 option (d) decision: combine save-time validation with planner integration so every successful save produces a calculation-segment boundary that the planner can handle.

#### D9a — Save-time alignment validation

A new validator runs on the PUT endpoint before the profile is written. For each changed field, the validator looks up the field's consumer rule's `(span, splitBehavior)` triple via `IRuleClassificationProvider` (S20 wave 1b) and applies the field's alignment policy:

| Field | Consumer rule | Alignment policy |
|-------|---------------|------------------|
| `WeeklyNormHours` | `NormCheckRule.WEEKLY` (window, aligned-window) | `effective_from` must be a Monday |
| `MaxFlexBalance` | `FlexBalanceRule` (cross-period, mergeable) | none |
| `FlexCarryoverMax` | `FlexBalanceRule` (cross-period, ferieår) | none |
| `MaxOvertimeHoursPerPeriod` | `OvertimeGovernanceRule.MaxOvertime` (period, mergeable) | none |
| `OvertimeRequiresPreApproval` | `OvertimeGovernanceRule.PreApproval` (period, mergeable) | none |

The alignment policy is encoded as a `Dictionary<string, Func<DateOnly, ValidationResult>>` keyed by overridable field name. Today only `WeeklyNormHours` is populated; future fields with alignment requirements extend the dictionary without architectural change. Misaligned saves return 400 with a structured per-field error naming the nearest valid dates.

#### D9b — Planner integration

`StatsTid.SharedKernel.Segmentation` gains:
- `BoundaryCause.LocalProfileActivation` enum value.
- `BoundarySources.LocalProfileActivations` field (a list of `(DateOnly EffectiveFrom, Guid ProfileId)` records).

`PeriodPlanner.Plan` detects boundaries from `LocalProfileActivations` exactly as it does for `OkTransitions`. The new `BoundaryCause` slots into `BoundaryDetector.OrderedCauses` with priority lower than `OkTransition` and `AgreementConfigPromotion` (a same-day OK transition or agreement-config promotion subsumes a profile activation in the cause-tie-break).

Because D9a guarantees that any profile changing `WeeklyNormHours` has `effective_from` on a Monday, the planner-produced segments around such a boundary always align with `NormCheckRule.WEEKLY`'s window. The aligned-window rule's `RejectIfMultipleSegments` merge strategy is never violated by a profile activation. Mergeable fields' rules merge their per-segment results via the strategies they already declare.

#### D9c — Hydration shim extension

`PeriodCalculationService.BuildPlanForLegacyCallers` (the S20 `[Obsolete]` shim that hydrates `BoundarySources` from production data) is extended:
- Pre-existing: hydrates `OkTransitions` from `OkVersionResolver`.
- New: hydrates `LocalProfileActivations` from `local_agreement_profiles` via a SELECT keyed on `(org_id, agreement_code, ok_version)` constrained to `effective_from BETWEEN @periodStart AND @periodEnd`.

This is a partial extension of the deferred S20 carry-forward (non-OK boundary hydration). Agreement-config promotions, position-override effective dates, and EU-WTD ruleset transitions remain on the carry-forward list — they don't enter S21 scope.

### D10 — Replay back-compat for pre-S21 manifests (Q13)

Pre-S21 `SegmentManifest`s do not carry `LocalProfileActivation` boundaries (the boundary cause didn't exist) and do not carry local-config snapshots (per ADR-016 D5b — local-config wasn't in the snapshot-at-calculation set; it was effective-dated at the per-row level).

ReplayAsync(manifestId) on a pre-S21 manifest:
1. Reconstructs the original segmentation from the manifest's stored segments.
2. Re-evaluates rules against the reconstructed plan.
3. Inside, `ConfigResolutionService` reads the post-migration `local_agreement_profiles` table by date.

For seed data (no row-collision), post-migration profile resolution returns the same effective values as pre-migration per-row resolution at the same date. **Replay determinism holds.**

For pre-S21 data with row-collision (the case where multiple `is_active=true` rows had the same key with different `effective_from`), migration picks the most-recently-effective row. Replay's new resolution sees only the picked row; if the original calculation resolved an earlier collision row, the replay diverges.

S21 ships with the documented contract: **pre-S21 manifests are best-effort replayable.** Forward calculations after S21 are fully deterministic. The system is pre-production; no audit-of-record manifests exist. If a future sprint requires stronger guarantees, the path is to snapshot local-config at calculation time during the migration window — option (b) extended in the original Q13 framing — not pursued in S21.

### D11 — Test strategy: committed minimum matrix (Q11)

All four categories pre-committed as IN. Counted floor: **11 new tests** (10 from Q11 plus 1 for D9a alignment validation).

| Category | Scenarios (counted floor) | Test file |
|----------|---------------------------|-----------|
| Migration (regression, Docker-gated) | 5: multi-row-per-key collision; informational-key drop (`PlanningStartDay`); typo'd-key drop; expired-but-active row; one-row-per-overridable-key happy path | `tests/StatsTid.Tests.Regression/Config/ProfileMigrationTests.cs` |
| Uniqueness enforcement (regression, Docker-gated) | 2: concurrent-insert race surfaces a uniqueness violation; soft-delete reactivation does not violate the partial-unique-index | `tests/StatsTid.Tests.Regression/Config/ProfileUniquenessTests.cs` |
| NULL-as-inherit resolution (unit) | 3: NULL on every overridable column inherits central; non-NULL on one overridable column overrides central for that field only; NULL after a previous non-NULL save reverts to central | `tests/StatsTid.Tests.Unit/Config/ProfileResolutionTests.cs` |
| Audit emission (regression, Docker-gated) | 2: every profile mutation emits a `LocalAgreementProfileChanged` event with field-level delta; the audit projection table records the same delta | `tests/StatsTid.Tests.Regression/Config/ProfileAuditTests.cs` |
| Save-time alignment validation (unit) | 1: mid-week `WeeklyNormHours` save returns 400 with structured per-field error naming nearest valid Monday dates | `tests/StatsTid.Tests.Unit/Config/ProfileAlignmentValidatorTests.cs` |

Floor = 5 + 2 + 3 + 2 + 1 = **13 new tests**. (Original Q11 floor of 10 was set before D9a's validation test was added; the floor moves with the decision set.)

### D12 — Effect on adjacent ADRs

- **ADR-010 (merge-at-service-layer):** amended by ADR-017. The architectural invariant ("rule engine never loads local configs") stays. The data shape changes from per-key rows to a profile entity. ADR-010's "Consequences" section is implicitly superseded by ADR-017's D5 (API shape) and D6 (event shape).
- **ADR-014 (DB-backed agreement configs):** the partial-unique-index pattern (`UNIQUE WHERE effective_to IS NULL`) used here is the same pattern ADR-014 introduced for `agreement_configs` ACTIVE rows. Cross-reference for future readers.
- **ADR-016 (Temporal Period Handling):** augmented additively. `BoundaryCause.LocalProfileActivation` is a new value; `BoundarySources.LocalProfileActivations` is a new field. The classification inventory (D2) is unchanged — profile fields' consumer rules retain their existing `(span, splitBehavior, family)` triples; the planner just sees additional boundaries from a new source. ADR-016 D5b's snapshot-at-calculation set stays at three sources (wage-type-mapping, entitlement-policy, employee-profile); local-config is not added because it is now effective-dated at the profile level.

## Rationale

Priority alignment:

1. **P1 (Architectural integrity).** ADR-010's merge-at-service-layer invariant is preserved. The rule engine still receives a single `AgreementRuleConfig`; it has zero knowledge of profile vs. patch. The new entity lives in `Infrastructure` (`LocalAgreementProfileRepository`) and `SharedKernel.Models`.
2. **P3 (Auditability).** Profile-shaped events (D6) and audit projection (D8) match the unit of admin action. The event store is append-only; legacy events stay readable.
3. **P4 (OK version correctness).** Profile is scoped to a single OK version by schema; cross-version overrides are physically impossible. Q12's option (d) ensures profile activations interact with the planner's OK-transition handling correctly.
4. **P7 (Security).** Org-scope enforcement on the new endpoints (PUT/GET/`/history`) reuses `OrgScopeValidator` patterns (S6/S7); no change to authorization architecture.
5. **P9 (Usability).** The single-profile-editor admin UX (D5 + frontend task) replaces the patch-bag form. Save-time alignment validation (D9a) gives admins informative errors instead of silent runtime crashes.

The patch-bag merits considered and rejected:
- Per-row event granularity → rejected; profile-wide saves are atomic; one event matches one action.
- Per-field effective-dating independence → rejected; admin UX prefers "the profile changed on date X" over "MaxFlexBalance changed on X but WeeklyNormHours changed on Y"; no product driver exists for the latter.

## Implications

### For SharedKernel.Segmentation
- New `BoundaryCause.LocalProfileActivation` enum value.
- New `BoundarySources.LocalProfileActivations` field (`IReadOnlyList<(DateOnly EffectiveFrom, Guid ProfileId)>`).
- `BoundaryDetector` extended to detect this cause; tie-break order: `OkTransition > AgreementConfigPromotion > LocalProfileActivation > PositionOverrideEffective > EuWtdRulesetVersion`.

### For Backend.Api
- New endpoints under `/api/config/{orgId}/profile/{agreementCode}/{okVersion}`.
- Old per-row endpoints removed.
- New `ProfileAlignmentValidator` consumed by the PUT handler.

### For SharedKernel.Models
- New `LocalAgreementProfile` model (init-only, follows PAT-001).
- New `LocalAgreementProfileChanged` event (registered in `EventSerializer`).

### For Infrastructure
- New `LocalAgreementProfileRepository`.
- `ConfigResolutionService` rewritten to read profiles instead of per-key rows.
- `LocalConfigurationRepository` deprecated (kept for reads against pre-S21 historical data; no new writes).
- `BuildPlanForLegacyCallers` (in `PeriodCalculationService`) extended per D9c.

### For Frontend
- `ConfigManagement.tsx` rewritten as a profile editor view per `(org, agreement, OkVersion)`.
- Date-picker for `WeeklyNormHours` enforces Monday alignment.
- Read-only renderings for the 21 protected keys + central agreement fields.
- `useConfig.ts` hook reshaped to fetch profile + history.
- Visual / interaction polish remains a Phase-5 deferral.

### For Phase 4 ROADMAP
- "Versioned History for Non-Dated Boundary Sources" sub-sprints stay at three (wage-type-mapping, entitlement-policy, employee-profile). Local-config does NOT join the list — S21 retires it from ADR-016 D5b's snapshot-at-calculation carve-out and migrates it to per-profile effective-dating.
- The S20 carry-forward "non-OK boundary hydration in `BuildPlanForLegacyCallers`" is partially closed by D9c (profile activations now hydrate); agreement-config promotions, position-override effective dates, and EU-WTD ruleset transitions remain.

### For migration
- Migration runs in a single transaction over `local_configurations`. Rollback path: restore from event-store replay of `LocalConfigurationChanged` events into the pre-S21 schema. Documented as ops procedure in the migration plan (deliverable #3).
- Pre-S21 `SegmentManifest`s become best-effort replayable per D10.

## Alternatives Rejected

For each open question that produced a closed decision, the rejected options:

- **Q1 (uniqueness):** strict `(org_id, agreement_code, ok_version)` rejected — loses history. `(org_id, agreement_code, ok_version, effective_from)` rejected — loses at-most-one-active invariant unless extra resolution logic disambiguates at read time. Partial-unique-index chosen for the same reasons ADR-014 chose it.
- **Q2 (effective-dating):** overlapping ranges with date-window selection rejected — incompatible with Q1's partial-unique-index choice. Scheduled-future profiles rejected for S21 — admin UX complexity without product driver; escape hatch documented (`daterange` exclusion constraint via `btree_gist`).
- **Q3 (lifecycle):** DRAFT/ACTIVE/ARCHIVED rejected — local profiles change rarely, no preview workflow.
- **Q4 (migration):** shadow-compare rejected — no production data to compare against. Dual-path resolution rejected — over-engineering for pre-production system with 3 seed rows.
- **Q5 (schema):** JSONB rejected — over-engineering for 5 fields. Normalized child table rejected — over-engineering for non-list values.
- **Q6 (informational keys):** options (a) "fold into profile" and (b) "separate `org_operational_settings` table" rejected — data audit confirmed zero non-rule-engine consumers. Option (c) deletion is no-regression.
- **Q7 (API):** add-alongside rejected — no production runway need. Versioned-payload evolution rejected — same reason.
- **Q8a (event):** per-field events rejected — artificial post-hoc decomposition.
- **Q8b (legacy event):** rewrite old events rejected — violates ADR-002 append-only invariant. Drop event type rejected — breaks deserialization of pre-S21 events.
- **Q9 (resolution chain):** closed pre-commit — no rejected alternative; existing chain stays.
- **Q10 (backwards compat):** redirect-with-shim rejected — no external consumers.
- **Q12 (planner integration):** option (a) "first-class boundary source without save-time validation" rejected — would 4xx mid-week `WeeklyNormHours` saves at calculation time. Option (b) "profile-stability assumption" rejected — silent no-op-this-period behavior is admin-experience landmine. Option (c) "future work" rejected — same as (b) without commitment in writing.
- **Q13 (replay back-compat):** option (a) "migrate historical manifests" rejected — manifests don't carry local-config snapshots, so there's nothing to rewrite. Option (b) "two-shape replay" rejected — requires preserving old per-row table indefinitely; bloats schema.
- **Q14 (audit projection):** option (a) "per-field audit rows" rejected — atomic-write-with-N-audit-rows mismatch. Option (c) "both transitionally" rejected — over-engineering for pre-production system.

## References

- [ADR-002](ADR-002-pure-function-rule-engine.md) — rule engine purity invariant (preserved).
- [ADR-010](ADR-010-local-config-merge-at-service-layer.md) — merge-at-service-layer invariant (preserved); data shape (amended).
- [ADR-014](ADR-014-agreement-configs-database-backed.md) — partial-unique-index pattern reused.
- [ADR-016](ADR-016-temporal-period-handling.md) — `BoundarySources` extended additively; D2 classification inventory unchanged; D5b snapshot-at-calculation set unchanged.
- [SPRINT-21.md](../../sprints/SPRINT-21.md) — analysis-phase deliverables (data audit, this ADR, migration plan, task decomposition).
- [docker/postgres/init.sql](../../../docker/postgres/init.sql) — `local_configurations` table (`:449`), `local_configuration_audit` table (`:473`), seed rows (`:629-633`).
- [src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs](../../../src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs) — current resolution chain (lines 69-129); `ProtectedKeys` set (lines 24-47); informational-keys log-and-skip (lines 255-258).
