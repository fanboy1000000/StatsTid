# Sprint 97 — Structured Enhed table + multi-tag UX (replaces the free-text `enhed_label`)

| Field | Value |
|-------|-------|
| **Sprint** | 97 |
| **Status** | complete |
| **Start Date** | 2026-06-23 |
| **End Date** | 2026-06-23 |
| **Orchestrator Approved** | yes — 2026-06-23 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 errors |
| **Test Verified** | yes (local, fresh-greenfield): 850 unit + 1094 regression + 6 smoke + 29 demoseed + 518 fe; CI GREEN `28078021803` (all 7 jobs) |

## Sprint Goal
Replace the single free-text `employee_profiles.enhed_label` with a structured, deduplicated, per-Organisation **`enheder`** entity table + a **`user_enheder`** multi-tag membership link — managed-entities-first, flat/untyped, LocalHR-or-above per-Organisation, **zero authority/scope/approval meaning** (ADR-035). Migrate the existing labels; keep `enhed_label` as a read-only display fallback. The Organisation-PAGE Enhed-tree management is DEFERRED. Refinement: `.claude/refinements/REFINEMENT-enhed-structured-multitag.md` (owner-resolved + Step-4 dual-lens, 2 BLOCKERs resolved).

## Scope (in / out)
**IN:**
- **`enheder`** (new table): `enhed_id` PK, `organisation_id` TEXT NOT NULL FK→`organizations(org_id)`, `name` TEXT NOT NULL, `deleted_at` TIMESTAMPTZ NULL (soft-delete), `created_at`, `version` (optimistic). **Partial unique** `(organisation_id, lower(name)) WHERE deleted_at IS NULL` (dedup over ACTIVE rows). CHECK the org is an ORGANISATION? — by FK + the create endpoint's org-type guard (an Enhed belongs to an ORGANISATION, not a MAO).
- **`user_enheder`** (new junction): `user_id` FK→`users`, `enhed_id` FK→`enheder`, PK `(user_id, enhed_id)`. The same-Organisation invariant (the enhed's `organisation_id` == the user's `primary_org_id`) enforced at the COMMAND layer (set-tags validates) — NOT a DB trigger (the transfer path clears tags; see below).
- **Events** (P3, dedicated; latest-wins non-temporal projection — model after `work_time_projection`/ADR-028 D1, NOT the ADR-022 temporal profile): `EnhedCreated`, `EnhedRenamed`, `EnhedDeleted` (soft), `UserEnhederChanged` (carries the FULL tag-id set for the user — idempotent overwrite).
- **Endpoints** (all org-scope-floored `ValidateOrgAccessAsync(actor, organisationId, StatsTidRoles.LocalHR)` — the S76/S91 per-scope floor; org-scope containment preserved):
  - `GET /api/admin/enheder?organisationId=…` — list ACTIVE enheder for one Organisation (the Organisation must be ∈ `GetAccessibleOrgsAsync(actor, LocalHR)`).
  - `POST /api/admin/enheder` `{organisationId, name}` — create (409 on active-name dup; the org must be ORGANISATION-typed).
  - `PUT /api/admin/enheder/{id}` `{name}` (If-Match) — rename (409 on active-name dup).
  - `DELETE /api/admin/enheder/{id}` (If-Match) — soft-delete (sets `deleted_at`; memberships are projection-filtered, NO fan-out untag write).
  - **Set-user-tags**: a new PUT in the `EditPersonDrawer` save path (or folded into the profile save) — sets `user_enheder` to a list of `enhed_id`s; validates EACH `enhed_id` ∈ the ACTIVE enheder of the **person's** `primary_org_id` (a dead/foreign enhed → 400). Emits `UserEnhederChanged`.
- **Transfer-clears-tags (BLOCKER 1)**: the users/stamdata PUT (`AdminEndpoints.cs:1216`, the `primary_org_id` writer) CLEARS the user's `user_enheder` rows **in the same transaction** + emits `UserEnhederChanged(empty)` atomically with `UserUpdated` (Enhed is throwaway metadata; CLEAR not block).
- **Migration**: backfill `enheder` from the DISTINCT `employee_profiles.enhed_label` per Organisation (via `EnhedCreated` events) + tag each user (via `UserEnhederChanged`) — in the bootstrap/seeder (event-sourced; no raw projection INSERT). KEEP `enhed_label` (read-only display fallback; frozen `EnhedLabel` field stays on the `EmployeeProfile*` events + audit mappers — do NOT remove).
- **FE**: an "Enheder" management panel on the medarbejder-administration page (org-scoped; the Organisation selector = the actor's accessible orgs; create/rename/delete) + a multi-select tag picker in `EditPersonDrawer` (from the person's `primary_org` active enheder; single-save-path preserved) + search/filter the medarbejder list by Enhed.
- init.sql (2 tables) + db-schema regen; demo seed (emit enheder + tags); tests; ADR/INDEX/QUALITY/ROADMAP.

**OUT:** the Organisation-PAGE Enhed-tree (MAO/Organisation/Enhed) management (the page sprint); Enhed typing/categories (flat); Enhed nesting; effective-dated enhed history (latest-wins); dropping `enhed_label` (transitional, drop later); merge-by-rename.

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (new tables + new events [P3] + P7 access-control + a cross-domain transfer coupling) |
| **External Codex** | done — 2 WARNING / 3 NOTE (spine sound) |
| **Internal Reviewer** | done — 2 BLOCKER / 2 WARNING / 3 NOTE (spine sound; all plan-text sharpenings) |

### Step-0b Findings + Resolutions (both lenses confirmed the data-model spine is sound + ADR-035-consistent)
- **BLOCKER A (Reviewer) — migration source + the CI-baseline-NULL reality.** In the init.sql greenfield baseline (what CI reseeds) `enhed_label` is universally NULL (`EmployeeProfileSeeder` inserts NULL; S92 deliberately didn't pre-seed). Real labels exist ONLY in the demo seed (a raw projection INSERT, no `EmployeeProfileCreated` event). **RESOLUTION (TASK-9704):** the migration READS the `enhed_label` **projection column** (`SELECT DISTINCT u.primary_org_id, ep.enhed_label FROM employee_profiles ep JOIN users u … WHERE ep.effective_to IS NULL AND ep.enhed_label IS NOT NULL`) — NOT a replay of `EmployeeProfileCreated.EnhedLabel` (the demo path never emitted it) — and emits `EnhedCreated` then `UserEnhederChanged`. The CI baseline migrates ZERO by design (all NULL); the migration test seeds ≥1 labeled profile (or runs against the demo dataset) so "no user loses metadata" is non-vacuous.
- **BLOCKER B (Reviewer) — the transfer-clear must be GATED + in-tx.** The users PUT fires `UserUpdated` on EVERY PUT (`AdminEndpoints.cs:1286`), so an unconditional `UserEnhederChanged(empty)` would wipe tags on any unrelated edit. **RESOLUTION (TASK-9703):** gate the tag-clear on the EXISTING org-change predicate (`:1083-1084`, `request.PrimaryOrgId is not null && != existingUser.PrimaryOrgId` — reuse, don't re-derive); place `DELETE FROM user_enheder WHERE user_id=@u` + the `UserEnhederChanged(empty)` outbox enqueue + audit-projection INSIDE the existing tx AFTER the users UPDATE (:1235), following the :1304-1312 pattern. RED-on-old: a NON-transfer PUT does NOT clear tags (the spurious-clear guard) + the transfer-clears assertion.
- **WARNING C (Reviewer) — set-tags TOCTOU vs concurrent transfer.** **RESOLUTION (TASK-9703):** set-tags does `SELECT primary_org_id FROM users WHERE user_id=@u FOR UPDATE` (lock the user row) in its tx, validates each `enhed.organisation_id == the locked org AND enhed.deleted_at IS NULL`, then inserts — so a concurrent transfer serializes before (the new org's enheder fail validation) or after (the transfer's clear wins). Concurrency interleave test (set-tags vs transfer) → TASK-9707.
- **WARNING D (Reviewer) — ORGANISATION-typed create guard.** **RESOLUTION (TASK-9703):** `POST /api/admin/enheder` rejects a `organisationId` whose `org_type != 'ORGANISATION'` (mirror the existing primary_org guard `AdminEndpoints.cs:1001-1002`, same 400). RED: create-enhed-under-MAO → 400. (The migration only creates under ORGANISATION parents — it joins through `users.primary_org_id`, already ORGANISATION-constrained.)
- **WARNING E (Codex) — read-path conversion is under-assigned.** The existing scalar `enhed_label` consumers — `ApprovalPeriodRepository` roster (`:670`) + search (`:882`) returning only `enhedLabel`, and the `EditPersonDrawer` `ProfileSection.tsx:85` PUTting free-text `enhedLabel` — need explicit conversion. **RESOLUTION:** TASK-9702/9703 additively project + return the active multi-tag set (display text = the tag names, fallback to `enhed_label` then org name) in the roster/search payloads; TASK-9705 replaces the drawer's free-text field with the multi-select tag picker + adds the enhed-id filter input. `enhed_label` stays read-only/frozen for transition.
- **NOTE (both) — confirmed**: transfer-clears correctly owned by 9703 (cross-domain, the users PUT); the migration ordering (EnhedCreated before UserEnhederChanged); soft-delete keeps the `user_enheder→enheder` FK valid; the partial-unique allows delete-then-recreate-same-name (TASK-9707 tests it); the P7 search JOIN must sit INSIDE the `accessibleOrgs` bound (`:2125`/`:2138`) — TASK-9707 adds a cross-org name-equal-enhed scope-leak test (HR covering STY01 filtering by a name that also exists in STY04 → only STY01 users).

## Architectural Constraints
- [x] P1 — Architectural integrity (Enhed is metadata under an Organisation; the managed-list is org-scoped via `GetAccessibleOrgsAsync`, not single-org; the Organisation page stays deferred)
- [x] P3 — Event sourcing (dedicated `Enhed*` + `UserEnhederChanged` events; latest-wins projection; the backfill reads the projection column; `EnhedLabel` kept frozen on the profile events; replay-safe greenfield — both lenses confirmed)
- [x] P4 — Concurrency (soft-delete + projection-filter [no fan-out untag race]; the set-tags `FOR UPDATE` + active-only guard; **rename/delete optimistic concurrency — the version predicate INSIDE the UPDATE + affected-row check** [Step-7a BLOCKER, fixed])
- [x] P7 — Security (LocalHR floor + org-scope containment on every enhed endpoint; the same-Organisation tag invariant; transfer-clears-tags atomic + gated; set-tags re-floors against the locked org [Step-7a TOCTOU BLOCKER, fixed]; **Enhed grants NO authority** — both lenses verified it absent from `OrgScopeValidator`/`CoversOrg`/`CanApprove`/`ValidateEmployeeAccessAsync`)
- [x] P8 — CI/CD (greenfield reseed; 1094 regression green locally — 4 central failures resolved [1 real CASCADE fix + 3 FAIL-002 sheds isolation-cleared]; CI confirmation pending)

## Task Log (planned decomposition)
- **TASK-9701 — Data model** (init.sql `enheder` + `user_enheder` + the partial unique index + FKs; db-schema regen).
- **TASK-9702 — Domain events + projection** (`EnhedCreated/Renamed/Deleted`, `UserEnhederChanged`; the latest-wins projection writers; KEEP `EnhedLabel` on the profile events).
- **TASK-9703 — Endpoints + repository** (enhed CRUD org-scope-floored; set-user-tags active-only-validated; the 409 dedup; **the transfer-clears-tags in the users/stamdata PUT, same tx**).
- **TASK-9704 — Migration/seed** (backfill enheder + tags from `enhed_label` via events; keep `enhed_label` read-only).
- **TASK-9705 — FE** (the org-scoped Enheder management panel + the multi-select tag picker in `EditPersonDrawer` + search/filter by Enhed).
- **TASK-9706 — Demo seed** (emit enheder + user_enheder; verifier).
- **TASK-9707 — Tests** (the P7 no-authority RED [(a) cross-org tag rejected, (b) shared-enhed grants no `CanApprove`/`ValidateEmployeeAccessAsync`]; transfer-clears RED-on-old; out-of-scope-HR 403; dedup 409; soft-delete projection-filter; multi-tag; the migration "no user loses metadata").
- **TASK-9708 — Docs + close** (a new ADR or an ADR-035 addendum for the Enhed metadata model; INDEX/QUALITY/ROADMAP).

## Risks
- **P7 authority leakage** (the #1 risk): Enhed must never become a scope/approval dimension → the concrete RED test (cross-org reject + shared-enhed-no-edge); no Enhed param in `OrgScopeValidator`/`CoversOrg`/`CanApprove`.
- **Transfer-clears-tags atomicity** (BLOCKER 1): in the users/stamdata PUT tx, not async; RED-on-old.
- **Multi-org managed-list scope** (BLOCKER 2): org-scoped via `GetAccessibleOrgsAsync`/`ValidateOrgAccessAsync(LocalHR)`, not single-org; out-of-scope-HR 403 RED.
- **P3 migration**: event-sourced backfill (no raw INSERT — the S92 lesson); greenfield reseed.
- **Delete vs concurrent tag-add**: soft-delete + projection-filter + active-only set-tags guard (no fan-out write).
- **review-lens-complementarity**: Step-0b + Step-7a dual-lens (the transfer coupling + the authority-leakage seam are the adversarial targets).

## Execution Outcome
All 8 tasks complete (Backend 9701/9702/9703 + read-path, Migration 9704, FE 9705, Demo 9706, Tests 9707, Docs 9708). The feature: 2 new tables (67 total), 4 events, `EnhedRepository` (latest-wins projection), 5 endpoints (CRUD + set-tags) + the transfer-clear in the users PUT + the additive roster/search read-path, `EnhedBackfillSeeder`, the demo emission, and the FE (org-scoped management panel + multi-select tag picker + Enhed search filter). Full build 0/0; fresh-greenfield reseed verified (67 tables, both new tables); FE vitest 518 (+26), Unit 850.

**Post-central-run fixes (3):**
1. **`@enhedId` typing** — the new search filter bound an UNTYPED `DBNull` → `42P08` → 500 on every filter-less search (also broke the existing S91 picker tests); fixed to `NpgsqlDbType.Uuid`. (In the central regression run → the S91 picker + S97 search tests passed.)
2. **`user_enheder` FKs `ON DELETE CASCADE`** — the backfill tags `enhed_label`-seeding tests' users, so their `DELETE FROM users` cleanups FK-violated; CASCADE (idiomatic for a junction; never fires in prod — both parents soft-delete) fixes it generically. Resolved the 1 real central-run failure (`MedarbejderRosterReadTests` cleanup); the other 3 central failures were FAIL-002 testcontainer sheds (`Exception while writing to stream` in `DockerHarness.StartAsync`, 1ms) — isolation-cleared 38/38.
3. **Step-7a BLOCKER fixes (2)** — see below.

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex + internal Reviewer), adversarial P7 + concurrency |
| **Sprint-start commit** | `5bbc3585151f03f5ae32d4bfc7e0df3e624ca36e` |
| **Review Cycles** | 1 review + 1 fix pass (both BLOCKERs fixed + pinned) |
| **Findings** | 2 BLOCKER (Codex; both fixed) / 2 WARNING (fixed) / 4 NOTE (accepted) |

### Findings
- **The P7 no-authority core HOLDS** (both lenses tried hard + verified): Enhed is structurally absent from `OrgScopeValidator`/`RoleScope.CoversOrg`/`DesignatedApproverAuthorizer`/`ValidateEmployeeAccessAsync`; the roster/search aggregation + `?enhedId` filter stay inside the users-first `accessibleOrgs` bound (keyed by enhed_id) → no cross-org leak.
- **[[review-lens-complementarity]] — Codex caught 2 concurrency BLOCKERs the internal lens cleared**: (1) **set-tags TOCTOU floor-escape** — the LocalHR floor was checked before the `FOR UPDATE` lock (pre-transfer org); an HR scoped to the old org could tag a user transferred out of scope → fixed by re-validating `ValidateOrgAccessAsync(actor, lockedOrg, LocalHR)` after the lock. (2) **enhed rename/delete lost-update** — the If-Match version was checked outside the write tx with no `version=@expected` predicate / affected-row check → two concurrent `If-Match:"1"` renames both commit, and a rename could emit `EnhedRenamed` on a 0-row (already-deleted) update → fixed by moving the version predicate INSIDE the UPDATE + the affected-row check (0-row → 404/412, no event). Both pinned by 5 new concurrency tests.
- **WARNING (fixed)** — the `NullEnhedIdSearch` test's stale "documents a defect" narrative (now a plain positive regression since the `@enhedId` fix); the multi-tag joined-display now asserted on the UNFILTERED `?q=` path (the common path), not only the `?enhedId=` filter.
- **NOTEs (accepted)** — rename/delete 404-before-403 for an out-of-scope GUID (negligible oracle); the demo-seed raw-INSERTs enheder (consistent with the dev-only demo-seed tradeoff; the greenfield path stays event-sourced); set-tags unreachable for a soft-deactivated user (benign analog of the S95 inactive-home note).
- Artifacts: `.claude/reviews/SPRINT-97-step7a-{codex,reviewer}.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 850 | all passing |
| Regression | 1094 | central 1085 pass + 4 resolved (1 CASCADE fix + 3 FAIL-002 sheds isolation-cleared) + 5 new Step-7a concurrency pins; `S97EnhedTests` 21/21 + `S97EnhedBackfillSeederTests` in isolation |
| Smoke | 6 | all passing |
| DemoSeed | 29 | all passing |
| Frontend (vitest) | 518 | all passing (+26 Enhed tests; 8 integration-broken tests fixed) |
| **Total** | **2497** | CI GREEN `28078021803` (all 7 jobs) |

## Sprint Retrospective
**What went well**: the refinement (owner-resolved 4 forks + Step-4 dual-lens, 2 BLOCKERs) + the plan Step-0b (2 BLOCKERs) + Step-7a (2 BLOCKERs) — **6 BLOCKERs caught across 3 review gates before they reached production**, each a real defect (the transfer-clear cross-domain coupling, the multi-org scope model, the migration baseline-NULL reality, the transfer-clear gating, the set-tags TOCTOU, the rename/delete lost-update). [[review-lens-complementarity]] decisive at every gate — Codex's concurrency-adversarial lens caught the 2 Step-7a optimistic-concurrency BLOCKERs the internal lens cleared. The tests agent caught a live 500-on-every-search defect (untyped `@enhedId`). The P7 no-authority invariant held under hard adversarial probing.
**What to improve**: the new search-filter param shipped untyped (42P08) — a Postgres-null-param typing footgun; and the new FK silently broke existing tests' cleanups (the backfill-tags-test-users interaction) — a new-table-vs-existing-cleanup hazard worth a checklist item. The FE agent hit a transient API 522 mid-integration (resumed cleanly).
**Knowledge produced**: ADR-036 (the structured Enhed model). Recorded follow-ups: drop `enhed_label` once consumers fully cut over; the Organisation-PAGE Enhed-tree (move/nesting) management.
