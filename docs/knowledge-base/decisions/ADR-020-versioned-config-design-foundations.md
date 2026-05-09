# ADR-020 — Versioned-Config Design Foundations for Phase 4d-1 (Wage-Type-Mapping)

| Field | Value |
|-------|-------|
| **Status** | ACCEPTED (S28 / TASK-2803 cycles 1-2 reviewed 2026-05-09 — Reviewer cycle 2 + Codex cycle 2 both APPROVE) |
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

### D1 — Snapshot trigger: planner-level enrollment + locked call-site + locked planner-side change + locked hydrator invocation seam

`PeriodPlanner` gains an enrollment seam for non-rule consumers to register `SnapshotContract` entries. ADR-020 D1 binds **five components** (cycle 1 TASK-2803 Reviewer B1 fix + user adjudication 2026-05-09 picked "keep hydrator + bind planner-side"; the "synthetic RuleClassification" escape hatch is REMOVED — it produced an incompatible implementation):

1. **The API**: `IPlannerEnrollment.RegisterSnapshotContract(string contractKey, Func<EmploymentProfile, object?> hydrator)`. The hydrator receives the segment's `EmploymentProfile` and returns the value stored in `SegmentSnapshot.NonDatedSourceValues[contractKey]` for that segment.

2. **The call-site**: `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:585` (`BuildPlanForLegacyCallers` `BoundarySources` construction) calls `IPlannerEnrollment.RegisterSnapshotContract("WtmNaturalKey", profile => new { OkVersion = profile.OkVersion, AgreementCode = profile.AgreementCode, Position = profile.Position })` once at construction time, before passing to `PeriodPlanner.Plan()`. PCS is the **only** production call-site of `Plan()`; test call-sites (Regression Segmentation/Payroll suites + Unit Planner suites; ~20 sites) construct `BoundarySources` directly and bypass enrollment by design — see Implications §1.

3. **The planner gate change**: `PeriodPlanner.cs:127` (`HasAnySnapshotContract` gate) MUST consult **both** the rule-set classifications AND the enrollment registrations when deciding whether `SegmentSnapshot` materializes. `PeriodPlanner.cs:275-281` (the private `HasAnySnapshotContract` method, ruleSet-only today) MUST iterate enrollment-plus-ruleSet.

4. **The planner signature change**: `PeriodPlanner.Plan()` (or `PeriodPlanner` constructor — implementer picks) gains an `EmploymentProfile? profile` parameter. PCS L585 passes the profile when calling `Plan()`. Without this, the planner has no `EmploymentProfile` to feed the hydrator; the seam is registered but uncallable.

5. **The hydrator invocation site**: After `SegmentSnapshot` construction at `PeriodPlanner.cs:128-130`, the planner iterates the enrollment list; for each enrolled contract, it invokes `hydrator(profile)` and stores the result at `SegmentSnapshot.NonDatedSourceValues[contractKey]`. **Today (S29 binding)**: the same `profile` parameter is passed to `Plan()` once and re-used across all segments — uniform per plan. **Forward-compat seam (NOT a today binding)**: the per-segment iteration shape permits future per-segment profile evolution if S29's successors adopt per-segment profile snapshots; today's S29 implementation does NOT need to thread per-segment profiles. If `profile` is null (test call-site or no profile in scope), the hydrator is skipped silently for that contract.

**Binding invariant**: "enrollment-registered contracts trigger `SegmentSnapshot` creation on the same code path that rule-declared contracts do, AND the hydrator runs against the supplied `EmploymentProfile` to populate `NonDatedSourceValues[contractKey]` for each segment."

**Why all 5 components**: the design refinement's cycles 1+2+2.5 each surfaced a deeper layer of the same Q1 binding gap. TASK-2803 cycle 1 Reviewer B1 surfaced the next layer (where does the hydrator run? how does `EmploymentProfile` reach the planner?). User adjudication 2026-05-09 chose the "keep hydrator + bind planner-side" path, formalizing components 4 + 5 explicitly.

### D2 — Soft-delete-then-create: gap-acknowledging 3-case routing + cited precedent (no SQL prescription)

WageTypeMappingEndpoints DELETE becomes truly soft (sets `effective_to = today` on the current row; emits `DELETED` audit + `WageTypeMappingDeleted` outbox event). POST gains a CREATE-side closed-on-today-predecessor check that branches into **3 cases** (cycle 1 TASK-2803 Reviewer B2 fix + user adjudication 2026-05-09 picked "reset effective_from = today on reopen — gap-acknowledging"; the original 2-case routing from cycle 2 of the deferred refinement implicitly preserved predecessor's `effective_from`, which would have lied to the audit log about [DELETE, reopen] activity gaps):

**Case A — no closed-on-today predecessor at the natural key**: POST proceeds as a fresh INSERT with `effective_from = today, effective_to = NULL, version = 1`. Audit emits `CREATED` + `WageTypeMappingCreated` outbox event.

**Case B — closed-on-today predecessor with `effective_from < today`** (the production case: DELETE at 09:00 today against a row originally created days ago, POST at 14:00 today): predecessor STAYS closed at `(effective_from = original_day, effective_to = today)`; POST proceeds as a fresh INSERT with `effective_from = today, effective_to = NULL, version = 1`. NO collision under `(natural_key, effective_from)` history-unique-index because predecessor's `effective_from` is in the past. **Audit honestly reflects the [DELETE, reopen] gap** — `GetByKeyAtAsync(date in gap interval)` returns NULL (matching reality). Audit emits `CREATED` + `WageTypeMappingCreated` outbox event for the new row; the prior DELETE was already audited at its own request.

**Case C — closed-on-today predecessor with `effective_from = today`** (the same-day-CREATE-DELETE-CREATE case: row created earlier today, DELETED later today, CREATEd a third time today, all within minutes — separate request transactions): predecessor is a zero-width `[today, today)` history row. INSERT at `effective_from = today` would collide on `(natural_key, today)`. Routing fix: **UPDATE-and-reopen on the zero-width predecessor** (set `effective_to = NULL`, version-bump, apply new field values via UPDATE). The zero-width predecessor mutates back to an open row. Audit emits `UPDATED` + `WageTypeMappingUpdated` outbox event on the **CURRENT request's tx** — the prior DELETE was already audited at its own request when it ran; the current request's UPDATE collapses the prior-DELETE-plus-current-CREATE intent into a single `UPDATED` action on its own audit row (TASK-2803 cycle 2 Reviewer N2 wording fix from the misleading "collapsed same-request DELETE+CREATE" framing).

**Binding invariant** (this is what S29 must satisfy):

> CREATE atomically locks any closed-on-today predecessor on the natural key, then routes per the 3-case decision (A: fresh INSERT; B: fresh INSERT preserving predecessor closure; C: UPDATE-and-reopen on zero-width predecessor) within a single transaction. The lock is held for the lifetime of the CREATE request transaction. The 3-case decision is made on the lock-held snapshot, not on a pre-lock read. The Case B / Case C distinction is made by inspecting `predecessor.effective_from == today`, NOT by client signaling.

**Recommended implementation** (S29 may follow this pattern unless it has good reason to diverge):

> Pattern: `LocalAgreementProfileRepository.SupersedeAndCreateAsync` precedent (`src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs:247-338`), extended with the 3-case routing. Open a single tx; `SELECT ... FOR UPDATE WHERE effective_to = today AND natural_key = ...` to lock any closed-on-today predecessor; branch on (no row → Case A; row with `effective_from < today` → Case B; row with `effective_from = today` → Case C); commit. SQL primitive `SELECT ... FOR UPDATE` is the recommended race-safety primitive.

**Why gap-acknowledging (not gap-erasing)**: gap-erasing (preserve predecessor's `effective_from` on UPDATE-and-reopen) would mutate the predecessor's history to claim continuous activity through [DELETE, reopen], producing a lie in the audit log. Gap-acknowledging keeps the predecessor closed honestly + creates a fresh row; the audit chain is "row-A active [original, today), DELETED, row-B active [today, NULL)" — accurate. For payroll export determinism via `asOfDate=segmentStart` (Implications §7), `GetByKeyAtAsync(date in gap interval)` returns NULL, matching the actual deletion semantic.

**Why invariant + cited precedent (not SQL prescription)**: design refinement cycle 2 saw a fork between Codex (no SQL — keep architectural latitude) and Reviewer (lock SQL — race-safety is unavoidably implementation-shaped). User-resolved synthesis path: bind the invariant tightly + cite the precedent + leave SQL primitive as recommended-not-binding so S29 can follow S22's pattern or pick a different lock primitive if the invariant is preserved.

**Cross-request semantics resolved**: this decision treats the close + reopen as a **single-request** semantic for Case C (the reopening request's CREATE handler does the lock + reopen). There is no cross-request reopen — the predecessor was DELETED by a prior request that already consumed its `If-Match`; the CURRENT CREATE request takes ownership of the natural key by locking + routing per the 3-case decision within its own tx. Cases A + B are pure CREATE semantics — no authorization confusion. Case C's reopen is the CURRENT request's prerogative as the authorized writer of the natural key, not a continuation of the prior DELETE's authorization.

**Consistent with ADR-018 D8 end-exclusive `[effective_from, effective_to)`**: Case B's predecessor `(effective_from = original_day, effective_to = today)` represents activity through end of yesterday (active for `[original_day, today)`); inactive starting today. The new Case B row at `(effective_from = today, effective_to = NULL)` activates today onward. Case C's zero-width predecessor `(effective_from = today, effective_to = today)` represents activity for `[today, today)` = no time, consistent with end-exclusive semantics.

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

**Why D1 binds 5 components** (TASK-2803 cycle 2 Codex P3 fix from "3 components" mis-cite): cycle-1 of the design refinement bound only the API; cycle-2 added call-site; cycle-2.5 added planner-side gate; TASK-2803 cycle 1 Reviewer B1 + user adjudication added components 4 (planner signature with `EmploymentProfile`) + 5 (hydrator invocation site). The pattern-recognition lesson (per `feedback_thrash_defer_real_world.md`) is that "the API exists" is insufficient binding for an ADR meant to prevent implementation rediscovery — the ADR must say WHO calls it, WHEN, what downstream code changes make the registration consequential, AND where the data flows through the binding chain. D1 binds all five so S29's implementing agent reads a complete contract; treating any one as optional reverts to the under-specified state that recurred across the design refinement's cycles.

**Why D2 invariant + cited precedent (not SQL prescription)**: ADR text is read by future-Orchestrators and future-Reviewers years after the original decision. Embedding `SELECT ... FOR UPDATE` in the ADR locks the architecture to Postgres-specific shape; if the project ever migrates DB engines or refactors `SupersedeAndCreateAsync`, the ADR becomes contradictory. Binding the invariant ("atomic lock + same-tx routing") + citing the existing precedent shape keeps the ADR portable while preserving enough specificity for S29 to inherit the proven pattern.

**Why D3 admin-delete-stays-deleted (pre-launch posture)**: the chosen behavior matches admin intent (deletion sticks) and is semantically simple. Production may want the inverse semantic (seeds reconcile to live state); that's a Phase 4e production-readiness decision when production data exists. For pre-launch dev environments, "admin delete sticks until next manual seed" is the right default.

## Implications for S29 (BINDING — S29 implementation refinement MUST satisfy these)

S29 implementing agent reads this section as the binding contract. S29's own refinement may add detail but cannot weaken these constraints.

1. **Planner-level enrollment** (D1 components 1-5, all required): S29 adds `IPlannerEnrollment.RegisterSnapshotContract(string, Func<EmploymentProfile, object?>)`, wires PCS L585 to call it for `WtmNaturalKey`, modifies `PeriodPlanner.cs:127,275-281` to consult enrollment-plus-ruleSet, adds `EmploymentProfile? profile` parameter to `Plan()` (or planner constructor), AND wires the per-segment hydrator invocation site after `PeriodPlanner.cs:128-130` `SegmentSnapshot` construction. Failure to bind any one of the 5 components → registered-but-never-consulted hook OR uncallable hook OR un-invoked hydrator → S29 ships with broken replay determinism and discovers it at integration-test time. **Test fixtures**: WTM-touching tests that construct `BoundarySources` directly without going through PCS bypass enrollment by design (deliberate test-isolation pattern); WTM-replay-determinism tests must register enrollment explicitly via `IPlannerEnrollment` or use a PCS-level fixture. **Test-direct planner invocation passes `profile = null`**; the hydrator-invocation site at component 5 must skip silently when `profile == null` to keep test-direct call-sites ergonomic.

2. **DELETE-soft + CREATE-side 3-case routing** (D2 invariant): S29's `WageTypeMappingEndpoints` DELETE handler becomes soft (sets `effective_to = today`); POST handler adds a CREATE-side closed-on-today-predecessor check that locks the natural key, then branches per the 3-case decision (Case A: fresh INSERT no predecessor; Case B: fresh INSERT preserving the predecessor's closure for `effective_from < today` predecessor; Case C: UPDATE-and-reopen on the zero-width `effective_from = today` predecessor). Within a single tx. The recommended implementation extends the S22 `LocalAgreementProfileRepository.SupersedeAndCreateAsync` pattern with the 3-case branch; S29 may pick a different lock primitive only if the binding invariant ("atomic lock + same-tx 3-case routing") is preserved.

3. **Seed INSERT rewrite** (D3): S29 modifies all 10 `INSERT INTO wage_type_mappings` statements in `init.sql` (lines L198, L272, L746, L789, L804, L819, L834, L1020, L1262, L1282) to add explicit `effective_from = '2020-01-01'` columns + `ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING` clauses. Validation: `docker compose down -v && docker compose up postgres -d --force-recreate` must succeed idempotently across multiple re-runs.

4. **Schema-migration shape** (carried forward from deferred refinement Approach 1, NOT re-litigated by S28): S29's schema migration adds `mapping_id UUID PRIMARY KEY`, `effective_from DATE NOT NULL`, `effective_to DATE NULL`, partial-unique on natural-key WHERE `effective_to IS NULL`, AND the new `(natural_key, effective_from)` history-unique-index that D3 conflicts against. Wrapped in S22 `schema_migrations` ledger pattern.

5. **Audit-action enum widening** (carried forward from deferred refinement; cycle 1 TASK-2803 update for D2 3-case routing): `wage_type_mapping_audit` CHECK constraint widens via ALTER CONSTRAINT to add `SUPERSEDED`. Audit emission per D2 case: same-day in-place edit → `UPDATED`; cross-day supersession (PUT against a current row, new effective_from > today) → `SUPERSEDED`; soft-delete → `DELETED`; D2 Case A (fresh CREATE no predecessor) → `CREATED`; D2 Case B (fresh CREATE with predecessor at `effective_from < today` staying closed) → `CREATED` (predecessor's prior `DELETED` already audited); D2 Case C (zero-width predecessor reopened via UPDATE) → `UPDATED` (collapsed same-request DELETE+CREATE).

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

### Cycle 1 (S28 / TASK-2803, 2026-05-09 — DRAFT → DRAFT-with-cycle-2-pending)

**Codex (External)**: 0 BLOCKERs / 0 WARNINGs / 0 NOTEs (review calibrated for code diffs; ADR-text qualitative review returned clean — `"The diff only updates architecture documentation and index entries"`).

**Reviewer Agent (Internal)**: 2 BLOCKERs + 2 WARNINGs + 4 NOTEs. Recommended **stay DRAFT**.

**Lens divergence smoke alarm fired** per Risk #5 mechanical AC of `.claude/refinements/REFINEMENT-s28-adr-020-design.md` — Codex APPROVE while Reviewer found 2 BLOCKERs in same-area-deeper-layer pattern (the trail's recurring Q1 + Q2 binding gaps). Same shape as the deferred Phase 4d-1 implementation refinement's lens divergence (which triggered the original S28 split). Default escalation per Risk #5: freeze ADR-020 as DRAFT + reopen the 2 specific decisions for explicit user adjudication.

**Cycle 1 BLOCKERs absorbed inline 2026-05-09** (user adjudication on the 2 sub-questions):
- **B1 (D1 hydrator invocation seam binding gap)**: D1 originally bound 3 components (API + call-site + planner gate). User chose "keep hydrator API + bind planner-side EmploymentProfile + invocation site." D1 expanded to **5 components**: components 1-3 unchanged; component 4 NEW = `Plan()` signature gains `EmploymentProfile? profile` parameter; component 5 NEW = hydrator invocation site is in PeriodPlanner after SegmentSnapshot construction at L128-130, per-segment iteration over enrollment list. The "synthetic RuleClassification escape hatch" REMOVED (it produced incompatible implementation).
- **B2 (D2 `effective_from` preservation vs reset on reopen)**: D2 originally said "UPDATE-and-reopen on the predecessor" without specifying `effective_from` handling. User chose "reset effective_from = today on reopen — gap-acknowledging." D2 routing expanded from 2-case to **3-case**: Case A (no closed-on-today predecessor → fresh INSERT), Case B (closed-on-today predecessor with `effective_from < today` → fresh INSERT, predecessor stays closed, audit honestly reflects gap), Case C (closed-on-today predecessor with `effective_from = today` → UPDATE-and-reopen on the zero-width predecessor — only way to avoid `(natural_key, today)` collision under history-unique-index). Audit emission per case: A → CREATED; B → CREATED (predecessor's prior DELETE already audited); C → UPDATED (collapsed same-request DELETE+CREATE).

**Cycle 1 WARNINGs absorbed inline**:
- **W1 (D1 file-path)**: PCS path corrected to `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:585` (was `Backend.Api/Services/...`).
- **W2 (ADR-019 D2.3 cite doesn't exist)**: ADR-019 amendment block (TASK-2802) updated to cite "D1 / D2 / D7 above" instead of nonexistent "D2.3" — wage_type_mappings ETag stuff is established across D1+D2+D7.

**Cycle 1 NOTEs absorbed where load-bearing** (N1 end-exclusive consistency stated in D2 + ADR-019 amendment; N2 dual-mechanism ADR-016 D5b reconciliation kept implicit; N3 hard-DELETE handling out of scope; N4 test-fixture guidance kept in Implications §1).

**Pending**: TASK-2803 cycle 2 (re-review the absorption); cycle-cap respected (2 of 2 per lens for in-sprint TASK-2803). If cycle 2 surfaces new same-area-deeper-layer BLOCKERs, defer per `feedback_thrash_defer_real_world.md` — Phase 4d-1 itself may need re-scoping.

### Cycle 2 (S28 / TASK-2803, 2026-05-09 — DRAFT → ACCEPTED)

**Reviewer Agent (Internal)**: 0 BLOCKERs / 0 WARNINGs / 3 NOTEs. APPROVE — DRAFT → ACCEPTED with NOTEs absorbed. Verified cycle 1 absorption clean: D1 5-component expansion ABSORBED; D2 3-case routing ABSORBED; W1 file-path ABSORBED; W2 D2.3→D7 cite ABSORBED. Both case-taxonomy probes (DELETE-09:00 + CREATE-14:00 both today against an old row → Case B; SUPERSEDED-today + DELETE + CREATE all today → Case C) resolve unambiguously under the as-written D2 invariant — case dispatch is on observable row state at lock-acquisition time, not history-of-edits, which is the right primitive for race-safety.

**Codex (External)**: 1 P3 (NOTE-level) — internal contradiction at L92 ("D1 binds 3 components" rationale text contradicting the 5-component Decision body). Risk that S29 implementer misreads components 4-5 as optional, reverting to the under-specified state the ADR was written to prevent.

**Lens divergence smoke alarm did NOT re-fire**: both lenses converged on APPROVE-with-NOTE-fixes (no same-area-deeper-layer BLOCKERs). The trail terminated cleanly per `feedback_thrash_defer_real_world.md`'s converging-finite test.

**Cycle 2 NOTEs absorbed inline**:
- **Codex P3** (L92 "3 components" → "5 components"): rationale section corrected; explicit "treating any one as optional reverts to the under-specified state" framing added so future readers cannot misread the rationale as relaxing the Decision.
- **Reviewer N1** (D1 component 5 forward-compat clarity): one-sentence clarification added — "Today (S29 binding): the same `profile` parameter is passed to `Plan()` once and re-used across all segments — uniform per plan. Forward-compat seam (NOT a today binding): the per-segment iteration shape permits future per-segment profile evolution."
- **Reviewer N2** (D2 Case C "collapsed same-request" misleading): wording fixed — Case C now says "audit emits UPDATED on the CURRENT request's tx ... the current request's UPDATE collapses the prior-DELETE-plus-current-CREATE intent into a single UPDATED action on its own audit row." Eliminates the prior-vs-current request confusion.
- **Reviewer N3** (W2 fix breadcrumb housekeeping): KEPT inline in the ADR-019 amendment block as an annotation-of-correction; future-cleanup candidate but not blocking.

**Status flip**: DRAFT → **ACCEPTED**. ADR-020 is binding contract for S29 implementation refinement.

**Cycle-cap respected**: 2 of 2 cycles per lens for in-sprint TASK-2803. Total review trail across S28: 3 cycles per lens at refinement scoping + 2 cycles per lens at TASK-2803 ADR-text review. Convergence achieved.
