# ADR-020 — Versioned-Config Design Foundations for Phase 4d-1 (Wage-Type-Mapping)

| Field | Value |
|-------|-------|
| **Status** | DRAFT (will flip to ACCEPTED after S28 / TASK-2803 dual-lens review clean) |
| **Sprint** | S28 |
| **Domains** | SharedKernel, Backend, Infrastructure, Payroll, Data Model |
| **Tags** | versioned-config, snapshot-contract, planner-enrollment, supersession, soft-delete, seed-idempotency, replay-determinism, phase-4d |
| **Supersedes** | none |
| **Amends** | ADR-019 D3 (the "flat-CRUD ... no effective-dating either" framing for `wage_type_mappings` is superseded for that resource, by S29) |

## Context

Phase 4d ("Versioned History for Non-Dated Boundary Sources") is Phase 4 of the production-hardening track. It splits into 3 sub-sprints:
- **Phase 4d-1 — Wage-type-mapping versioned history** (`wage_type_mappings`).
- **Phase 4d-2 — Entitlement-policy versioned history** (`entitlement_configs`).
- **Phase 4d-3 — Employee-profile versioned history** (`employees`).

Phase 4d-1 was originally scoped as Sprint 28 (implementation). Implementation refinement at `.claude/refinements/REFINEMENT-s28-phase-4d1.md` ran 3 review cycles per lens and converged on thrash (each cycle's BLOCKER fix surfaced a deeper architectural fact in the same area; lens divergence widened — captured in the new feedback memory `feedback_thrash_defer_real_world.md`). User chose option (c) split: design-only S28 producing this ADR-020, then implementation S29 against the settled design.

ADR-020 is **scoped narrowly to the 3 architectural questions that thrashed in the deferred refinement**, not a green-field design or a Phase 4d-2/3 preview. Phase 4d-2 and 4d-3 will produce their own ADRs when their sprints open; ADR-020 sets PATTERNS those ADRs may inherit, not bindings.

The 3 questions ADR-020 settles:
- **Q1 — Snapshot trigger mechanism for non-rule replay inputs**: today `PeriodPlanner.HasAnySnapshotContract` (`src/SharedKernel/StatsTid.SharedKernel/Segmentation/PeriodPlanner.cs:122-129,275-281`) gates `SegmentSnapshot` creation on at least one rule declaring a `SnapshotContract`. Today `RuleRegistry` (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RuleRegistry.cs:126`) registers no rule with a `SnapshotContract`, so `SegmentSnapshot` is null in every manifest produced today. Phase 4d-1 needs the manifest to carry replay-stable inputs (the wage-type-mapping natural-key triple `(OkVersion, AgreementCode, Position)`) that are **not rule-driven** — they're payroll-export concerns.
- **Q2 — Soft-delete-then-create endpoint contract under S25 If-Match**: `WageTypeMappingEndpoints` (`src/Backend/StatsTid.Backend.Api/Endpoints/WageTypeMappingEndpoints.cs:54-59,139-180,257-297`) ships POST + PUT + DELETE as separate committed transactions. PUT/DELETE require `If-Match`; POST has no `If-Match`. Cross-request "reopen" semantics (admin deletes a mapping, then immediately re-creates with the same natural key) cannot legitimately reopen the predecessor under the existing optimistic-concurrency contract — and would also collide on the proposed `(natural_key, effective_from)` history-unique-index when `effective_from = today` matches the predecessor's close-day.
- **Q3 — Seed idempotency under accumulated wage-type-mapping history**: `init.sql` has 10 unguarded `INSERT INTO wage_type_mappings` statements (`docker/postgres/init.sql:198,272,746,789,804,819,834,1020,1262,1282`). After S29's schema migration, the proposed partial-unique-index `WHERE effective_to IS NULL` enforces at-most-one-open per natural key, but the `(natural_key, effective_from)` history-unique-index would conflict on init.sql re-run with accumulated history (where no open row exists for a key, but a prior closed row at `effective_from = '2020-01-01'` exists).

## Decision

### D1 — Snapshot trigger: planner-level enrollment + locked call-site + locked planner-side change

`PeriodPlanner` gains an enrollment seam for non-rule consumers to register `SnapshotContract` entries. ADR-020 D1 binds **three components**, not just one:

1. **The API**: `IPlannerEnrollment` (or extension to `PeriodPlanner` constructor) with `RegisterSnapshotContract(string contractKey, Func<EmploymentProfile, object?> hydrator)` (or equivalent shape per S29 implementation). The hydrator returns the value to be stored in `SegmentSnapshot.NonDatedSourceValues[contractKey]` for each segment.

2. **The call-site**: `PeriodCalculationService.cs:585` (`BuildPlanForLegacyCallers` `BoundarySources` construction) calls `IPlannerEnrollment.RegisterSnapshotContract("WtmNaturalKey", profile => new { OkVersion = profile.OkVersion, AgreementCode = profile.AgreementCode, Position = profile.Position })` (or equivalent shape) once at construction time, before passing to `PeriodPlanner.Plan()`. PCS is the **only** production call-site of `Plan()`; test call-sites (Regression Segmentation/Payroll suites + Unit Planner suites; ~20 sites) construct `BoundarySources` directly and bypass enrollment by design — see Implications §1 for the test-side guidance.

3. **The planner-side change**: `PeriodPlanner.cs:127` (`HasAnySnapshotContract` gate) MUST consult **both** the rule-set classifications AND the enrollment list when deciding whether `SegmentSnapshot` materializes. `PeriodPlanner.cs:275-281` (the private `HasAnySnapshotContract` method, ruleSet-only today) MUST iterate enrollment-list-plus-ruleSet. Equivalent shape: enrollment contracts may be materialized as synthetic `RuleClassification` entries that flow through the existing path. Implementer picks the cleaner shape under the binding invariant: **"enrollment-registered contracts trigger SegmentSnapshot creation on the same code path that rule-declared contracts do."** Without all 3 components, enrollment registration is a registered-but-never-consulted hook (the cycle-2 thrash signal).

**Why all 3 components**: cycle 1 of the deferred refinement bound only the API; cycle 2 added the call-site lock; cycle 2.5 (this ADR) adds the planner-side lock. Each prior cycle's incomplete binding surfaced as a "deeper layer" BLOCKER in the same area — formalizing all 3 components in a single Decision prevents recurrence at S29 implementation time.

### D2 — Soft-delete-then-create: invariant + cited precedent (no SQL prescription)

WageTypeMappingEndpoints DELETE becomes truly soft (sets `effective_to = today` on the current row; emits `DELETED` audit + `WageTypeMappingDeleted` outbox event). POST gains a CREATE-side closed-predecessor check: if a closed-on-today predecessor exists at the natural key (i.e., a row with `effective_to = today` and `effective_from = today`), POST routes as **UPDATE-and-reopen on the predecessor** (sets `effective_to = NULL`, version-bumps, applies new field values) **inside the single CREATE request transaction**. Otherwise POST proceeds as a fresh INSERT.

**Binding invariant** (this is what S29 must satisfy):

> CREATE atomically locks any closed-on-today predecessor on the natural key, then routes as reopen-or-create within a single transaction. The lock is held for the lifetime of the CREATE request transaction. The decision between "reopen" and "fresh create" is made on the lock-held snapshot, not on a pre-lock read.

**Recommended implementation** (S29 may follow this pattern unless it has good reason to diverge):

> Pattern: `LocalAgreementProfileRepository.SupersedeAndCreateAsync` precedent (`src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs:247-338`) — open a single tx; `SELECT ... FOR UPDATE WHERE effective_to = today AND natural_key = ...` to lock the closed-on-today predecessor (if any); branch on row count (0 → fresh insert; 1 → UPDATE-and-reopen on the predecessor with version-bump + new field values); commit. SQL primitive `SELECT ... FOR UPDATE` is the recommended race-safety primitive. Audit emission: same-day DELETE-then-CREATE collapses into a single `UPDATED` audit + single `WageTypeMappingUpdated` outbox event (the close + reopen are not separately audited because they are not separately committed).

**Why invariant + cited precedent (not SQL prescription)**: cycle 2 of the design refinement saw a fork between Codex (no SQL — keep architectural latitude) and Reviewer (lock SQL — race-safety is unavoidably implementation-shaped). The synthesis path binds the invariant tightly enough to prevent S29 from re-discovering the cycle-3 race-safety subtleties of the deferred implementation refinement, while keeping S29 free to follow the S22 precedent or pick a different lock primitive if the invariant is preserved.

**Cross-request semantics resolved**: this decision treats the close + reopen as a **single-request** semantic (the reopening request's CREATE handler does the lock + reopen). There is no cross-request reopen — the predecessor was DELETED by a prior request that already consumed its `If-Match`; the CURRENT CREATE request takes ownership of the natural key by locking + reopening within its own tx. This is meaningfully different from "POST tries to reopen a predecessor closed by an earlier request without authorization" (which would violate S25 If-Match) — the CURRENT request's CREATE is the authorized writer of the natural key, and the reopen is its prerogative, not a continuation of the prior DELETE's authorization.

### D3 — Seed idempotency: `ON CONFLICT (natural_key, effective_from) DO NOTHING` + admin-delete-stays-deleted

The 10 unguarded `INSERT INTO wage_type_mappings` statements in `init.sql` (lines L198, L272, L746, L789, L804, L819, L834, L1020, L1262, L1282) are rewritten by S29 to:
1. Add explicit `effective_from = '2020-01-01'` columns (the system-epoch backfill date — pre-launch posture, no event predates this).
2. Add `ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING` clauses. The conflict target matches the proposed `(natural_key, effective_from)` history-unique-index that S29 introduces.

**Re-running `init.sql` is idempotent under all of**:
- Fresh bootstrap (no rows exist) — INSERTs proceed.
- Immediate re-run (rows exist from the prior run, all `effective_to = NULL`) — INSERTs no-op via ON CONFLICT.
- Re-run with intervening admin edits (the natural key has a SUPERSEDED predecessor at `effective_from = '2020-01-01'` AND a current row at `effective_from = '2026-XX-YY'`) — the seed INSERT at `effective_from = '2020-01-01'` no-ops via ON CONFLICT against the SUPERSEDED predecessor; the current row is unaffected.
- Re-run with admin-soft-deleted seeds (the natural key has the seed row with `effective_to` set, no current row) — the seed INSERT at `effective_from = '2020-01-01'` no-ops via ON CONFLICT against the soft-closed seed.

**Admin-delete semantic** (pre-launch chosen): re-running `init.sql` does NOT resurrect admin-soft-deleted seeds. The ON CONFLICT correctly does nothing on the existing closed row; admin's intent (deletion) is preserved. **Production stance** (Phase 4e candidate): production may want a different stance — e.g., a separate "seed reconciliation" path that distinguishes "row was never seeded" from "row was seeded then admin-deleted." Out of scope for S29.

## Rationale

**Why these 3 questions in one ADR**: they are the load-bearing architectural decisions S29 implementation must satisfy. The deferred Phase 4d-1 refinement thrashed precisely because these 3 were under-bound — each cycle's "fix" surfaced a deeper layer of the same architectural concern. Binding all 3 in a single ADR-020 produces a pre-S29 contract that prevents the same thrash in the implementation refinement.

**Why D1 binds 3 components**: cycle-1 of the design refinement bound only the API; cycle-2 added call-site; cycle-2.5 added planner-side. The pattern-recognition lesson (per `feedback_thrash_defer_real_world.md`) is that "the API exists" is insufficient binding for an ADR meant to prevent implementation rediscovery — the ADR must say WHO calls it, WHEN, and what downstream code change makes the registration consequential. D1 binds all three so S29's implementing agent reads a complete contract.

**Why D2 invariant + cited precedent (not SQL prescription)**: ADR text is read by future-Orchestrators and future-Reviewers years after the original decision. Embedding `SELECT ... FOR UPDATE` in the ADR locks the architecture to Postgres-specific shape; if the project ever migrates DB engines or refactors `SupersedeAndCreateAsync`, the ADR becomes contradictory. Binding the invariant ("atomic lock + same-tx routing") + citing the existing precedent shape keeps the ADR portable while preserving enough specificity for S29 to inherit the proven pattern.

**Why D3 admin-delete-stays-deleted (pre-launch posture)**: the chosen behavior matches admin intent (deletion sticks) and is semantically simple. Production may want the inverse semantic (seeds reconcile to live state); that's a Phase 4e production-readiness decision when production data exists. For pre-launch dev environments, "admin delete sticks until next manual seed" is the right default.

## Implications for S29 (BINDING — S29 implementation refinement MUST satisfy these)

S29 implementing agent reads this section as the binding contract. S29's own refinement may add detail but cannot weaken these constraints.

1. **Planner-level enrollment** (D1 components a + b + c): S29 adds `IPlannerEnrollment` with `RegisterSnapshotContract` method, wires PCS L585 to call it for `WtmNaturalKey`, AND modifies `PeriodPlanner.cs:127,275-281` to consult enrollment-plus-ruleSet (or materialize enrollment as synthetic `RuleClassification`). Failure to bind any one of the 3 components → registered-but-never-consulted hook → S29 ships TASK with broken replay determinism and discovers it at integration-test time. **Test fixtures**: WTM-touching tests that construct `BoundarySources` directly without going through PCS bypass enrollment by design (deliberate test-isolation pattern); WTM-replay-determinism tests must register enrollment explicitly via `IPlannerEnrollment` or use a PCS-level fixture.

2. **DELETE-soft + CREATE-side closed-predecessor check** (D2 invariant): S29's `WageTypeMappingEndpoints` DELETE handler becomes soft (sets `effective_to = today`); POST handler adds a CREATE-side closed-predecessor check that locks the natural key under the proposed history-unique-index, then branches between fresh-create and reopen-via-UPDATE within a single tx. The recommended implementation is the S22 `LocalAgreementProfileRepository.SupersedeAndCreateAsync` pattern; S29 may pick a different lock primitive only if the binding invariant ("atomic lock + same-tx routing") is preserved.

3. **Seed INSERT rewrite** (D3): S29 modifies all 10 `INSERT INTO wage_type_mappings` statements in `init.sql` (lines L198, L272, L746, L789, L804, L819, L834, L1020, L1262, L1282) to add explicit `effective_from = '2020-01-01'` columns + `ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING` clauses. Validation: `docker compose down -v && docker compose up postgres -d --force-recreate` must succeed idempotently across multiple re-runs.

4. **Schema-migration shape** (carried forward from deferred refinement Approach 1, NOT re-litigated by S28): S29's schema migration adds `mapping_id UUID PRIMARY KEY`, `effective_from DATE NOT NULL`, `effective_to DATE NULL`, partial-unique on natural-key WHERE `effective_to IS NULL`, AND the new `(natural_key, effective_from)` history-unique-index that D3 conflicts against. Wrapped in S22 `schema_migrations` ledger pattern.

5. **Audit-action enum widening** (carried forward from deferred refinement): `wage_type_mapping_audit` CHECK constraint widens via ALTER CONSTRAINT to add `SUPERSEDED`. Same-day edits emit `UPDATED`; cross-day edits emit `SUPERSEDED`; soft-delete emits `DELETED`. Same-day DELETE-then-CREATE collapses into single `UPDATED` per D2.

6. **EventSerializer typeof() count** (carried forward from deferred refinement): S29 adds `WageTypeMappingSuperseded` event for cross-day supersession. Existing `WageTypeMappingUpdated` reused for same-day edits + the D2 reopen path. EventSerializer typeof count goes 47 → 48. DEP-003 reflection-coverage test catches misses.

7. **MapCalculationResultAsync stays current-row** (carried forward from deferred refinement Open Q5): only `MapSegmentToExportLinesAsync` (the export-time path) gets `asOfDate=segmentStart`; `MapCalculationResultAsync` (per-line-date path) stays current-row per ROADMAP L350 "no retroactive recomputation."

8. **Admin-list read filter** (carried forward from deferred refinement Approach 9): S29 adds `WHERE effective_to IS NULL` to `WageTypeMappingRepository.GetAllAsync`, `GetByAgreementAsync`, AND `GetByKeyAsync` (3 methods). Without this, history rows leak to admin UI.

## Open (deferred to Phase 4d-2 / Phase 4d-3 / Phase 4e)

These are explicitly NOT settled by ADR-020:

- **(Phase 4d-2)** Whether `entitlement_configs` versioned history follows the same D1+D2+D3 pattern as wage_type_mappings, or diverges. Entitlement configs have a similar shape (admin-managed, infrequent writes, payroll-consumed) and likely benefit from the planner-enrollment + soft-delete + seed-idempotency patterns — but Phase 4d-2's own ADR will settle that explicitly when its sprint opens.

- **(Phase 4d-3)** Whether `employees` (employee-profile versioned history) follows the same patterns. Employees are a much larger surface (many fields; self-service + HR + leader edit paths; ADR-018 D13 deferred Open question on projection-only enrichment fields). Phase 4d-3 may need a fundamentally different shape — ADR-020 does NOT pre-bind 4d-3.

- **(Phase 4e production-readiness)** Production seed-reconciliation semantic — pre-launch (D3) chose "admin-delete-stays-deleted on init.sql re-run." Production may need "seed reconciliation distinguishes never-seeded vs admin-deleted." Out of S29 scope.

- **(Phase 4e production-readiness)** Pre-S22 ordering for `ProjectionBackfill` (S27 Step 7a P1 #2 carry-forward) — composite ordering scheme bridging the S22 boundary. Unrelated to S28/S29 but flagged for completeness.

- **(Phase 4e clock abstraction)** S21 `LocalAgreementProfileRepository.cs:584` uses `DateOnly.FromDateTime(DateTime.UtcNow.Date)` directly inside `ArchiveProfileAsync`; S29 inherits this pattern for soft-close DELETE write site. Phase 4e candidate to extract a clock abstraction across the codebase.

- **(Phase 4d-1 frontend "view history" UX)** Deferred to Phase 5 per the deferred-refinement Open Q4. S29 ships backend-only versioned history; admin UI continues with current-row payloads (admin-list reads filtered to current-row only per Implications §8).

## Alternatives Rejected

- **Q1 Alternative (a) — Sentinel rule** (declare a no-op rule with a `SnapshotContract` for `WtmNaturalKey`): rejected because rule-shaped abstraction for a non-rule concern pollutes `RuleRegistry`; future rules-only audits include the sentinel; doesn't decouple snapshot from rule (the actual architectural goal).

- **Q1 Alternative (c) — New `ExportContract` type** parallel to `SnapshotContract`: rejected because it doubles the contract surface; `PeriodPlanner` would need to enumerate two types; Phase 4d-2/3 may need a third. Single `SnapshotContract` family with the enrollment seam is leaner.

- **Q1 Alternative (d) — Unconditional snapshot** (drop `HasAnySnapshotContract` gate; always create `SegmentSnapshot`): rejected because every manifest pays the snapshot allocation cost even when no consumer reads it. Defensible but wasteful; planner-level enrollment is the principled mechanism.

- **Q2 Alternative (i) — Unified PUT-with-effective-date endpoint** (collapse POST + PUT + DELETE into one): rejected because of the large frontend churn and REST-shape break. The S25 admin UI already speaks POST/PUT/DELETE; collapsing to a single PUT endpoint requires admin UI rewrite — out of S29 scope per Phase 5 deferral.

- **Q2 Alternative (iii) — DELETE removes from natural-key index entirely** (close + retire): rejected because it loses history (defeats the point of versioning); replay determinism for past DELETEs becomes ambiguous (events log is the only remaining history). Soft-delete preserves the history chain.

- **Q3 Alternative (a) — Drop history-unique-index** entirely: rejected because multiple history rows at the same `effective_from` become possible; replay reads ambiguous; defeats versioning correctness.

- **Q3 Alternative (b) — Derived stable conflict key** (deterministic UUID from natural-key triple, used as ON CONFLICT target): rejected as over-engineered; introduces a new column; conflict semantics opaque to readers.

- **Q3 Alternative (d) — Move seeds to startup hook** (mirror S27 `ProjectionBackfillService` pattern): rejected because it introduces a new service; init.sql becomes incomplete; seed-as-config drift; the ON CONFLICT pattern is a 1-line-per-INSERT change, no new service needed.

## References

- [ADR-016](ADR-016-temporal-period-handling.md) D5b — non-dated boundary source patterns (snapshot at calc / point-in-time / manifest-snapshot). ADR-020 chooses a fourth pattern (export-time effective-date lookup) — see ADR-016 D5b reconciliation note in S29 implementation.
- [ADR-016](ADR-016-temporal-period-handling.md) D10 — replay determinism (the rule ADR-020 D1 + D2 satisfy for WTM).
- [ADR-017](ADR-017-local-agreement-configuration-as-a-profile.md) D2 — the S21 `local_agreement_profiles` effective-dating pattern that ADR-020 mirrors for WTM.
- [ADR-018](ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md) D7 — row-version optimistic concurrency (preserved unchanged for WTM by S29).
- [ADR-018](ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md) D9 — same-day in-place edit + `MODIFIED` audit-action precedent.
- [ADR-018](ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md) D11 — replay-determinism mechanism (manifest bypasses live DB). ADR-020 D1 chooses a DIFFERENT mechanism (re-read live DB with date predicate); both produce determinism.
- [ADR-018](ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md) D13 — sync-in-tx projection canonical pattern. Distinct from ADR-020 D1; both are non-rule replay-input patterns at different layers.
- [ADR-019](ADR-019-optimistic-concurrency-via-row-version.md) D3 — the "flat-CRUD with composite key … no effective-dating either" framing for `wage_type_mappings`. **Amended by ADR-020 / S29** (see TASK-2802 in-S28 amendment commit). The S25-introduced row-version + ETag/If-Match contract is preserved unchanged on the live-edit path; supersession adds a new history-row creation path orthogonal to the version contract.
- `feedback_thrash_defer_real_world.md` — the discipline-application memory written during S28's deferral; ADR-020 is the design-only sprint outcome.
- `.claude/refinements/REFINEMENT-s28-phase-4d1.md` — the deferred implementation refinement (3 cycles per lens; archived as historical reference).
- `.claude/refinements/REFINEMENT-s28-adr-020-design.md` — this ADR's scoping refinement (cycle 1 + cycle 2 + cycle 2.5 absorbed; user-approved skip cycle 3).

## Review History

### Cycle 1 (S28 / TASK-2803, 2026-05-09 — DRAFT → ACCEPTED gate)

_Pending TASK-2803 dispatch. Will record dual-lens findings + resolutions here._
