# Sprint 100 — Hierarchical Enhed (a `parent_enhed_id` tree + a derived level; purely metadata)

| Field | Value |
|-------|-------|
| **Sprint** | 100 |
| **Status** | complete |
| **Start Date** | 2026-06-24 |
| **End Date** | 2026-06-24 |
| **Orchestrator Approved** | yes — 2026-06-24 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 errors; `frontend` tsc 0 errors |
| **Test Verified** | yes (local): 850 unit + 1130 regression (+`S100EnhedHierarchyTests` 13) + 6 smoke + 29 demoseed + 566 fe (+19); CI-pending (backfilled at close-polish) |

## Sprint Goal
Make Enhed **hierarchical** — `enheder.parent_enhed_id` forms a tree WITHIN each Organisation, with the **level derived as the depth** — while it stays **PURE display metadata with ZERO authority** (ADR-036 invariant unchanged). Owner-resolved: keep S97 MULTI-tag membership; delete re-parents children up; one sprint (backend + FE). Refinement: `.claude/refinements/REFINEMENT-enhed-hierarchy.md` (owner-resolved 3 forks + Step-4 dual-lens, BLOCKERs resolved). Amends ADR-036.

## Scope (in / out)

**IN — Backend:**
- **Schema (init.sql):** `enheder.parent_enhed_id UUID NULL REFERENCES enheder(enhed_id)` (a root enhed = NULL, directly under the Organisation; a child's parent is in the SAME Organisation). Index `idx_enheder_parent`. (67 tables unchanged — a column add.) db-schema regen.
- **The per-Organisation in-tx advisory lock (Step-4 BLOCKER — the concurrency spine):** create-child / move / delete each `pg_advisory_xact_lock(hashtext('enhed-org-' || organisation_id))` (the enhed tree is wholly within one Organisation — the S95 "advisory domain = the Organisation" pattern), THEN run the cycle CTE on the held connection (sees committed state; concurrent enhed-tree mutations serialize).
- **Create:** `POST /api/admin/enheder` accepts an optional `parentEnhedId` — validate the parent is ACTIVE + same-Organisation (re-read in-tx under the lock); `EnhedCreated` gains an optional `ParentEnhedId`.
- **Move / re-parent (NEW):** `PUT /api/admin/enheder/{id}/move {newParentEnhedId|null}` (within the same Organisation; `organisation_id` IMMUTABLE — reject a cross-org parent 422). **If-Match** (version) + bump version. Under the org-advisory lock: a **cycle CTE** rejects moving under the enhed's own descendant (422); `null` newParent = make it a root. Emits `EnhedMoved(EnhedId, OldParentEnhedId, NewParentEnhedId)`.
- **Delete (the S97 soft-delete UNCHANGED — `deleted_at` + projection-filter, NOT untag) + RE-PARENT children up:** under the lock, `UPDATE enheder SET parent_enhed_id = <deleted.parent_enhed_id> WHERE parent_enhed_id = <deleted.id> AND deleted_at IS NULL` IN THE SAME TX, emitting a per-child `EnhedMoved` for each (P3 — NOT a silent SQL update); the deleted root case → children become roots (`parent_enhed_id := NULL`). The children SURVIVE (not soft-deleted). The deleted enhed's `user_enheder` rows stay (S97 projection-filter, unchanged).
- **Events (P3):** new `EnhedMoved`(EnhedId, OldParentEnhedId, NewParentEnhedId) + `EnhedCreated.ParentEnhedId` (optional — replay-safe, NULL=root=greenfield default) + the `EventSerializer` registration; plain-outbox (consistent — `EnhedCreated/Renamed/Deleted` are plain-outbox; only `UserEnhederChanged` is audit-registered). `EnhedRepository` projection writers updated.
- **Reads:** `GetActiveEnhederWithTagCountsAsync` + `ListActiveByOrgAsync` add `parent_enhed_id` to their SELECT; the `GET /organizations/tree` C# assembly NESTS the per-Organisation enhed sub-tree + computes `level` = depth (O(enheder), bounded per-org — NO stored path/level).

**IN — FE (the S99 page reversal — the S91 dead-button discipline IN REVERSE):**
- The Organisation page (`OrganisationPage.tsx`): the enhed leaves become a nested sub-tree under each Organisation (depth continues); **re-enable Tilføj-on-Enhed** (create a child enhed) + **Flyt-on-Enhed** (the move dialog — target = enheder in the same Organisation excluding self + descendants, or "→ root"); show the **level** (badge/number). The Enhed-delete copy updates (children re-parent up).
- The medarbejder-admin Enhed panel (`EnhederPanel.tsx`, S97, LocalHR): nest enheder + create-under-parent + the move (consistent enhed-tree management for LocalHR within their org).
- **INVERT the S99 vitest** that pinned "Enhed has no Tilføj/Flyt" → now Tilføj/Flyt ARE present on Enhed (RED-on-old); the controls wired to real endpoints BEFORE rendering.

**OUT:** single-membership (kept multi-tag); a stored level/path (derived); cross-org enhed move; surfacing the hierarchy in ANY authority path (it stays zero-authority); the org-structure (S98/S99 unchanged).

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (schema + new event [P3] + P4 concurrency [the multi-level cycle/lock] + P7 zero-authority) |
| **External Codex** | done — "Sound to plan"; 2 WARNING / 4 NOTE |
| **Internal Reviewer** | done — "Sound to plan after 2 WARNINGs"; the lock-disjointness + zero-authority confirmed against real code |

### Step-0b Findings + Resolutions (both lenses confirmed the spine: the advisory-lock disjointness, the zero-authority-by-construction, the events — all clean vs the real code)
- **WARNING (both) — the cycle guard is a NEW enhed CTE, NOT a `GuardNoCycleAsync` reuse.** `GuardNoCycleAsync` (`ReportingLineRepository.cs:816`) walks `reporting_lines` edges — structurally unusable for the enhed tree (it walks `parent_enhed_id` on `enheder`). **RESOLUTION (TASK-10002):** author a NEW `GuardNoEnhedCycleAsync` — a recursive CTE over `enheder.parent_enhed_id` (filtered `deleted_at IS NULL`, path-array visited-set + a depth backstop, mirroring the discipline), run on the HELD connection under the `enhed-org-` advisory. Walk direction PINNED: a move rejects (422) if `newParentEnhedId` == the enhed itself OR `newParentEnhedId` ∈ `descendants(enhed)` (walk down `parent_enhed_id = @enhedId`).
- **WARNING (Reviewer) — `parent_enhed_id` must be on the IN-TX read path.** The move needs the OLD parent (for `EnhedMoved`); the delete-reparent reads the deleted enhed's parent. **RESOLUTION (TASK-10001):** add `parent_enhed_id` to `EnhedFullRow` + `GetByIdInTxAsync`/`GetByIdAsync` SELECTs (not just the list reads).
- **WARNING (Codex) — `ListActiveByOrgAsync` stays FLAT.** It serves selectable enheder for the tag picker (`useEnheder` / `EnhedTagPicker`). **RESOLUTION (TASK-10003):** add `parentEnhedId`/`level` to the FLAT rows; only the `GET /tree` C# assembly + the MANAGEMENT panels (the Organisation page + `EnhederPanel`) NEST. The `EnhedTagPicker` (the multi-select) stays FLAT — tagging is set-membership, orthogonal to the management tree.
- **NOTE — the move's optimistic concurrency.** **RESOLUTION (TASK-10002):** the move mirrors rename/delete — the `version = @expectedVersion AND deleted_at IS NULL` predicate IN the UPDATE + a 0-row in-tx re-read → 412 (version) / 404 (gone), emit nothing on 0-row; the target parent's active+same-org status re-read IN-TX UNDER the lock.
- **NOTE — the delete-reparent details.** **RESOLUTION (TASK-10002/10005):** each re-parented child's `version` bumps (`ApplyEnhedMovedAsync` does the in-tx bump); a 0-children (leaf) delete emits ONLY `EnhedDeleted` (no `EnhedMoved`) — pin it alongside the root→roots + non-root→grandparent cases.
- **NOTE — the `createEnhed` FE signature change.** **RESOLUTION (TASK-10004):** `createEnhed(orgId, name)` → `createEnhed(orgId, name, parentEnhedId?)`; update `useEnheder.test.ts` + the S99 `OrganisationPage.test.tsx:260` assertion; add `moveEnhed` to the hook. The move dialog computes self+descendants from the GET /tree forest (the FE already has the nested tree — no server round-trip).
- **NOTE — the exact S99 vitest pins to INVERT** (RED-on-old): `OrganisationPage.test.tsx:186` (Tilføj absent on Enhed → now PRESENT), `:194` (Flyt absent → PRESENT), the Enhed-delete "NO underenheder slettes" → now the children-reparent-up copy. The inline-rename-on-Enhed (`:202`) STAYS.
- **NOTE (both) — P7 confirmed clean by construction** (the Reviewer verified `ApprovalPeriodRepository`'s enhed joins are display-only, keyed by `enhed_id`, never `parent_enhed_id`, never in a scope/`CanApprove`/`ValidateEmployeeAccess` decision). The "shared-ancestor grants nothing" RED test is the right guard. `EnhedBackfillSeeder` needs NO change (parentless `EnhedCreated` → roots). Same-org parent is app-layer-validated (a CHECK can't cross-ref `organisation_id`) — note + test it.

## Architectural Constraints
- [ ] P1 — Architectural integrity (an entity-only hierarchy on Enhed; derived level; the org-structure untouched)
- [ ] P3 — Event sourcing (`EnhedMoved` + `EnhedCreated.ParentEnhedId`; the re-parent-on-delete emits per-child `EnhedMoved`, not silent SQL; replay-safe greenfield)
- [ ] P4 — Concurrency (THE spine: the per-Organisation advisory lock + the cycle CTE under it; the move If-Match + version; held-lock interleaves — reciprocal moves can't cycle; create-child vs delete can't orphan)
- [ ] P7 — Security (Enhed stays ZERO authority — `parent_enhed_id` in NO scope/approval path; the concrete "shared-ancestor grants nothing" RED test)
- [ ] P8 — CI/CD (greenfield reseed; full pyramid green)

## Task Log (planned)
- **TASK-10001 — Schema + events** (`parent_enhed_id` + index; db-schema regen; `EnhedMoved` + `EnhedCreated.ParentEnhedId` + serializer + the projection writers).
- **TASK-10002 — The concurrency spine + CRUD** (the per-Organisation advisory lock; create-under-parent [same-org, in-tx]; the move endpoint [If-Match + version + the cycle CTE under the lock + `organisation_id`-immutable]; delete re-parents children up [per-child `EnhedMoved`, root→roots]).
- **TASK-10003 — Reads** (`parent_enhed_id` in the enhed SELECTs; the `GET /tree` nests + derives `level`; `ListActiveByOrgAsync` nests).
- **TASK-10004 — FE** (the Organisation page nesting + Tilføj/Flyt-on-Enhed + the level; the EnhederPanel nesting + create-child + move; the move dialog [exclude self+descendants]; the Enhed-delete copy).
- **TASK-10005 — Tests** (RED-on-old: the cycle guard [move-under-descendant 422]; the held-lock interleaves [reciprocal moves no cycle; create-child-vs-delete no orphan]; the delete re-parents children up [non-root → grandparent; root → roots; the children survive]; the move If-Match; **the P7 "shared-ancestor grants no `CanApprove`/`ValidateEmployeeAccess`" RED**; the derived level; the FE no-dead-button inversion).
- **TASK-10006 — Docs + close** (ADR-036 hierarchy amendment; SECURITY [Enhed hierarchy = still zero authority]; INDEX/QUALITY/ROADMAP).

## Risks
- **P7 authority leakage (the #1, both-lens-cleared by construction)** — the hierarchy must NEVER enter a scope path. The concrete "shared-ancestor grants nothing" RED test.
- **P4 concurrency (the real work)** — the enhed tree is MULTI-level (cycles reachable, unlike the org tree's leaves) → the per-Organisation advisory lock + the cycle CTE under it is the spine; held-lock interleave tests.
- **The re-parent-on-delete (P3)** — emit per-child `EnhedMoved`, not silent SQL; the root case.
- **The S99 page reversal** — re-enabling Tilføj/Flyt-on-Enhed; invert the S99 dead-button vitest; wire the controls before rendering.
- **review-lens-complementarity**: Step-0b + Step-7a dual-lens (the cycle/lock concurrency + the zero-authority + the delete-reparent-events are the adversarial targets).

## Execution Outcome
Enhed is now HIERARCHICAL — a `parent_enhed_id` tree within each Organisation + a derived level — while STAYING pure zero-authority metadata (ADR-036 invariant unchanged, both-lens-verified clean by construction). Backend (the `enhed-org-` advisory lock + the new `GuardNoEnhedCycleAsync` CTE; create-under-parent; the If-Match move; delete-re-parents-children via per-child `EnhedMoved`; the `GET /tree` nesting + derived level) + FE (the Organisation page nests + re-enables Tilføj/Flyt-on-Enhed + the level; the `EnhederPanel`; the inverted S99 dead-button vitests). Build 0/0; FE tsc 0; 67 tables (a column add). One sprint, as planned.

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex + internal Reviewer), adversarial P4/P7/P3 |
| **Sprint-start commit** | `1a64b4a948c674b1c75bb99b0811edc43b5728c3` |
| **Review Cycles** | 1 review + 1 fix pass |
| **Findings** | 1 BLOCKER (Codex; fixed) + 1 WARNING (fixed) + the internal lens: no findings |

### Findings
- **The internal Reviewer found NO findings** — the zero-authority invariant holds by construction (`parent_enhed_id` structurally absent from every scope/approval path; a shared ancestor/descendant grants nothing), the per-Organisation lock serializes all three tree mutators through the cycle check (the phantom-cycle gap closed; `enhed-org-` disjoint from `reporting-org-` → no deadlock), the delete-reparent is event-sourced + cycle-safe by construction, and the tests are real proofs (the held-lock `pg_locks⋈pg_stat_activity` waiter barrier; the P7 shared-ancestor RED).
- **[[review-lens-complementarity]] — Codex caught a BLOCKER the internal lens cleared (the S99 `fetchEnheder` pattern AGAIN):** `GET /api/admin/enheder` DROPPED `parentEnhedId` + `level` from its response (`AdminEndpoints.cs:2610`) even though `ListActiveByOrgAsync` SELECTs `parent_enhed_id` → `useEnheder.toEnhed` coerced it to null → the `EnhederPanel` treated every enhed as a root (broken nesting + move-picker exclusion). **The FE vitest mocked the CORRECT wire shape → green, while the endpoint served the WRONG one → prod broken.** FIXED at the source (the endpoint projects `parentEnhedId` + a server-derived `level`) + pinned RED-on-old by a NEW backend contract test. The Organisation page was unaffected (it uses `GET /tree`).
- **WARNING (fixed)** — the move success response omitted `name` (`moveEnhed` → `Enhed` with `name: undefined`); added `name = existing.Name`.
- Artifacts: `.claude/reviews/SPRINT-100-step7a-{codex,reviewer}.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 850 | all passing |
| Regression | 1130 | +`S100EnhedHierarchyTests` 13 (the cycle guard, the held-lock interleave, delete-reparent root/non-root/leaf, the move If-Match, create-under-parent, the derived level, the P7 shared-ancestor RED, the `GET /enheder` contract); clean re-run 0 sheds (the first run aborted at 174 on Docker contention from a concurrent isolated test run — re-run clean) |
| Smoke | 6 | all passing |
| DemoSeed | 29 | all passing |
| Frontend (vitest) | 566 | +19 (the nesting, Tilføj/Flyt-on-Enhed, the move-dialog self+descendant exclusion, the level, the inverted dead-button pins) |
| E2E (Playwright) | unchanged | (no new e2e) |
| **Total** | **2581** (+ the e2e suite) | CI confirmation pending |

## Sprint Retrospective
**What went well**: the Step-4 + Step-0b dual-lens settled the hard parts UP FRONT — the concurrency spine (a per-Organisation advisory lock with the cycle CTE run *under* it, because the enhed tree is genuinely multi-level unlike the org-tree leaves), the delete-as-projection-filter-not-untag correction, and the re-parent-via-events (P3). Implementation then had no design surprises; the internal Step-7a lens confirmed the spine clean by construction. The P7 zero-authority invariant held — a parent pointer can't leak without a reader joining it, and nothing does.
**What to improve**: the `GET /enheder` dropped-field BLOCKER is the **S99 `fetchEnheder` bug a THIRD time** — a FE hook test mocking the right envelope while the endpoint serves the wrong one. The standing lesson (a shared typed client / an endpoint-contract test) is now partly enforced by the new backend contract test; the broader fix (a generated client or a contract-test convention for every list endpoint) is the recorded follow-up. Also: never run an isolated Docker `dotnet test` concurrently with the central regression (the first run aborted at 174 on Docker contention).
**Knowledge produced**: ADR-036 S100 amendment (D7–D12) + docs/SECURITY.md (the hierarchy = still zero authority). Recorded follow-ups: the endpoint-contract-test convention; drop `enhed_label` once consumers cut over.

## Status: COMPLETE (close commit; push + CI-verify)
