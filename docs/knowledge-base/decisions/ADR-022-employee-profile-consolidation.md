# ADR-022 — Employee-Profile Consolidation + Pre-Baked Versioning (Phase 4d-3 Part 1)

| Field | Value |
|-------|-------|
| **Status** | ACCEPTED (S31 / TASK-3111 reviewed 2026-05-16 — Step 7a cycle 1 absorbed 3 P2 BLOCKERs in `e9733d0`; cycle 2 clean on the absorbed diff; 2 production-readiness findings deferred to Phase 4e per pre-launch posture) |
| **Sprint** | S31 |
| **Domains** | Backend, Infrastructure, Frontend, Data Model |
| **Tags** | versioned-config, employee-profile, consolidation, surrogate-uuid-pk, atomic-outbox, audit, phase-4d, data-plane-only |
| **Supersedes** | none |
| **Amends** | none (S31 is data-plane only; consumer cutovers + planner-snapshot + versioning emission land in S32 — ADR-023, filed there) |

## Context

Phase 4d-3 closes the third sub-sprint of "versioned history for non-dated boundary sources" (ROADMAP §Phase 4d, ADR-016 D5b reconciliation). The `users` table holds five employment-profile fields today (`agreement_code`, `ok_version`, `employment_category`, `primary_org_id`, `is_active`), but THREE additional fields that the rule engine consumes have **no persisted source-of-truth**:

- `WeeklyNormHours` — supplied per-request in `TimeEndpoints.cs:395` `CalculateRequest.WeeklyNormHours`; hardcoded `37.0m` in `ComplianceEndpoints.cs:77`; fallback-chain to `CentralAgreementConfigs` then `37.0m` in `BalanceEndpoints.cs:66-68`
- `PartTimeFraction` — same client-supplied pattern at `TimeEndpoints.cs:397`
- `Position` — supplied per-request OR resolved via `PositionOverrideRepository` lookups; no canonical employee-level value

This "scattered profile state" is a P2 (deterministic rule engine) hole that ADR-016 D5b's fifth pattern (consumption-time effective-date lookup) cannot fix on its own — there's nothing to look up. **Phase 4d-3 must consolidate first, then version.**

The S30 refinement explicitly anticipated this split via Q1 ("split S31 + S32"; both lenses converged with 0 BLOCKERs at cycle 2). The deferred-refinement protocol from `feedback_thrash_defer_real_world.md` reinforced the choice — combining consolidation + versioning + rule-engine cutover into a single sprint would have produced ~25 tasks with two architectural decisions colliding, repeating the S28 thrash-defer failure mode.

ADR-022 settles the S31-side decisions. **S32 will file its own ADR-023** for the versioning emission + per-field bucketing matrix + planner-snapshot + consumer cutover decisions.

## Decision

### D1 — S31 scope: data-plane only; zero consumer cutovers

The new `employee_profiles` authoritative store exists in S31 but is **NOT** read by ANY production code path. ComplianceEndpoints stays on hardcoded `37.0m`; BalanceEndpoints fallback chain stays on AgreementConfig + Central; TimeEndpoints `CalculateRequest.WeeklyNormHours` + `WeeklyCalculateRequest.WeeklyNormHours` stay unchanged; RuleEngine.Api unchanged. Sprint Close Criterion enforces this via Grep verification at sprint close.

**Rationale**: The refinement cycle 2 absorbed a P4 (version correctness) BLOCKER that would have surfaced if S31 wired the rule-engine read to the new authoritative store without S32's planner-snapshot. Admin edits between S31-close and S32-close would silently flip replays of past periods to new values — violating ROADMAP L350 "no retroactive recomputation of past calculations." Keeping the rule-engine path on request-payload until S32's cutover lands atomically with planner-snapshot eliminates this window entirely.

**Implication**: S31 ships the store + admin CRUD + frontend page + atomic-outbox + audit-table, but no consumer benefits yet. The Compliance hardcoded-`37.0m` bug for part-time employees remains unfixed until S32 — acceptable trade-off because Compliance was already wrong for part-time pre-S30 and fixing it cleanly with versioning + planner-snapshot in S32 is preferable to fixing it half-rightly in S31 with a P4 window + re-fixing in S32.

### D2 — Surrogate UUID PK (`profile_id UUID PRIMARY KEY DEFAULT gen_random_uuid()`)

Mirrors S29's `wage_type_mappings.mapping_id UUID PRIMARY KEY` precedent. Allows multiple rows per `employee_id` in the history dimension when S32 starts emitting closed predecessors via the ADR-020 D2 3-case supersession routing.

**Rejected alternative**: `employee_id TEXT PRIMARY KEY` — would forbid multi-row history. Initial cycle-1 plan-mode proposal; Codex Step 0b cycle 2 BLOCKER absorbed the reversal. Without surrogate UUID PK, S32's pre-baked versioning columns would be unusable (history rows couldn't INSERT).

### D3 — Pre-baked versioning columns (S29/S30 lesson applied)

The S31 `CREATE TABLE` block includes `effective_from DATE NOT NULL DEFAULT '0001-01-01'`, `effective_to DATE NULL`, partial-unique-index `(employee_id) WHERE effective_to IS NULL`, history-unique-index `(employee_id, effective_from)`, and `version BIGINT NOT NULL DEFAULT 1` — all dormant in S31 (we only write live rows; only `version` is actively used for ADR-019 If-Match enforcement).

**Rationale**: S29 TASK-2912 init.sql ordering defect (3 commits to fix) demonstrated the cost of post-hoc schema migration. Pre-baking is ~5 lines of DDL with zero behavior change in S31; S32 needs **zero schema migration** to activate the history path. Codex Step 0b cycle 1 BLOCKER absorbed the reversal from initial cycle-1 "no effective_from in S31" proposal.

### D4 — `is_part_time` column DROPPED (refinement cycle 2 absorption)

The schema does **NOT** carry an `is_part_time` column. `EmploymentProfile.IsPartTime` (consumed by PCS at L335-336) is computed as `part_time_fraction < 1.0m` inside `EmployeeProfileRepository.GetByEmployeeIdAsync` when constructing the in-memory profile.

**Rationale**: Cycle 1 refinement carried `is_part_time` as a separate column for forward-compat with existing `EmploymentProfile.IsPartTime` consumers. Cycle 2 Codex WARNING surfaced the latent invariant burden — nothing in DB enforces `is_part_time == (part_time_fraction < 1.0)`. Dropping the column kills the drift-class entirely (single source of truth: `part_time_fraction`).

### D5 — Admin CRUD endpoint pair + AdminEndpoints POST 4-way atomicity

Two endpoints land in S31:

- `GET /api/admin/employee-profiles/{employeeId}` — `RequireAuthorization("HROrAbove")` + `OrgScopeValidator.ValidateEmployeeAccessAsync(actor, employeeId)`; returns the live profile + ETag `"<version>"` header
- `PUT /api/admin/employee-profiles/{employeeId}` — same RBAC + scope binding; admin-strict If-Match contract per ADR-019 D2/D5/D6 (412 stale / 428 missing / 409 disjoint); atomic-outbox 3-way tx (row UPDATE + audit INSERT + outbox enqueue) per ADR-018 D3/D5

ALSO: `POST /api/admin/users` (`AdminEndpoints.cs:292`) extends from the existing 2-way atomic tx (users INSERT + UserCreated outbox) to **5-way atomicity**:

1. INSERT INTO users (existing)
2. INSERT INTO employee_profiles (new — defaults `weekly_norm_hours=37.0m, part_time_fraction=1.000m, position=NULL`)
3. INSERT INTO employee_profile_audit action='CREATED' (Step 7a P2 fix — added after cycle 1 surfaced this as a BLOCKER)
4. outbox.EnqueueAsync stream `user-{userId}` UserCreated event (existing)
5. outbox.EnqueueAsync stream `employee-profile-{userId}` EmployeeProfileCreated event (new)

**New invariant**: every active user has exactly one live `employee_profiles` row.

### D6 — `OrgScopeValidator.ValidateEmployeeAccessAsync` on BOTH GET and PUT (Step 0b BLOCKER fix)

`RequireAuthorization("HROrAbove")` proves the actor has the HR role + a scope claim, but does NOT bind that scope to the target employee's org. Without `OrgScopeValidator.ValidateEmployeeAccessAsync`, an HR user from org X could read/edit profiles of employees in org Y — cross-org HR data leak. The fix mirrors `BalanceEndpoints.cs:48-53` pattern.

Step 0b cycle 1 Codex BLOCKER caught this; absorbed before any code landed.

### D7 — 4 event types registered up-front; 2 emitted in S31

`EventSerializer.cs` registers all 4 lifecycle events in S31 (51 → 55 typeof):

- `EmployeeProfileCreated` — emitted in S31 (backfill + admin-create)
- `EmployeeProfileUpdated` — emitted in S31 (admin PUT same-day edit)
- `EmployeeProfileSuperseded` — reserved for S32 (cross-day supersession close)
- `EmployeeProfileSoftDeleted` — reserved for S32 (admin DELETE soft-close)

**Rationale**: refinement cycle 1 Reviewer P3 BLOCKER absorbed. Registering vocabulary up-front avoids carry-forward debt; S32 emits SUPERSEDED + SOFT_DELETED without an EventSerializer migration. Stream-naming `employee-profile-{employeeId}` per ADR-018 D6 — one stream per employee lineage.

### D8 — Seeder route over SQL-side INSERTs (Step 0b cycle 1 absorption)

Bootstrap-time `EmployeeProfileSeeder` (registered in `Program.cs` after `EntitlementConfigSeeder`) creates one live `employee_profiles` row per existing user that lacks one. Per-row tx writes: profile row + audit-CREATED row (Step 7a P2 fix in `e9733d0`) + EmployeeProfileCreated outbox event.

**Rejected alternative**: SQL-side `INSERT INTO outbox_events` block in init.sql. Bypasses `IOutboxEnqueue` serialization → breaks replay determinism (event payload JSON not produced by EventSerializer's registered shape).

### D9 — Frontend admin page (LocalHR+ only); NO history view in S31

`EmployeeProfileEditor.tsx` mirrors S30's `EntitlementConfigEditor.tsx` shape:

- Two-level dropdown (org → user) — RBAC-gated to LocalHR+ via `RequireRole`
- Three editable fields: `weekly_norm_hours` (step 0.25, range 0-50), `part_time_fraction` (step 0.01, range 0.1-1.0), `position` (text)
- `parseFloat` (NOT `parseInt`) for the two DECIMAL fields — explicit inline comment prevents regression of S30 cycle 2 truncation bug
- Banner-with-retry on 412 stale-ETag (mirrors ProfileEditor pattern)
- NO history / "as-of-date" UI — deferred to S32 + Phase 5

## Implications

### S32 commitment list (must land atomically)

S32 (ADR-023, filed there) MUST deliver these together in one logical sprint commit-group to preserve the P4 window-safety property:

1. **Per-field bucketing matrix** — decide which `employee_profiles` fields are planner-snapshotted (ADR-020 D1 pattern) vs consumption-time-lookup (ADR-021 D4 pattern) vs last-write-wins. Strong candidates for planner-snapshot: `weekly_norm_hours`, `part_time_fraction`, `position` (the rule-engine inputs). Last-write-wins candidates: none in the S31 schema (`is_part_time` already dropped).
2. **PCS planner-enrollment** for the snapshotted fields via `IPlannerEnrollment.RegisterSnapshotContract` (ADR-020 D1 inheritance — no new pattern).
3. **ComplianceEndpoints + BalanceEndpoints cutover** atomic with planner-snapshot — read from `employee_profiles` via dated lookup, not hardcoded `37.0m`.
4. **TimeEndpoints + CalculateRequest hard-cut** — stop accepting `WeeklyNormHours` / `PartTimeFraction` in the request body; rule-engine receives them via segment snapshot. Frontend updates to omit the fields.
5. **`SUPERSEDED` + `SOFT_DELETED` event emission** — supersession routing inside `EmployeeProfileRepository.SupersedeAndCreateAsync` per ADR-020 D2 3-case routing; admin DELETE endpoint adds soft-close + emits `EmployeeProfileSoftDeleted`.
6. **Marquee D-test**: `ReplayAsync_StableUnderEmployeeProfileMutation_ResultByteIdentical` — admin edits `weekly_norm_hours` from 37→32 today; replay of last month's calculation uses 37 (snapshot), not 32 (current live). Mirrors S29's WTM marquee.
7. **"As-of-date" UI toggle** on the frontend editor — single date picker; deeper per-field timeline view is Phase 5 polish.
8. **Q3 LEAVE re-adjudication**: S32 decides whether to migrate `agreement_code` / `ok_version` / `employment_category` from `users` to `employee_profiles`, or version them in-place on `users`. S31 left them on `users` to keep JWT/auth path untouched; S32's versioning touches all profile reads anyway.

### Production-readiness gaps deferred to Phase 4e

Step 7a cycle 2 surfaced 2 findings that pre-launch posture (ROADMAP L369 — no production data, single-instance deployment) mitigates today but production deploy must address:

- **[P1] Legacy DB upgrade path** — Postgres init scripts only run on FRESH data directories; existing pre-S31 DBs need an explicit ALTER block + version detection before `EmployeeProfileSeeder` queries `employee_profiles`. Same shape as S30 cycle 2 P1 (entitlement_configs legacy upgrade) which is already on the Phase 4e list.
- **[P2] Concurrent app startup race** — two instances starting simultaneously can both read the same `missing` users list, both INSERT, and the loser hits `23505` partial-unique-index violation → startup crash. Fix: catch `PostgresException` SqlState=23505 and continue (idempotent skip-without-fail). Hardens multi-instance deployments.

Both findings documented in SPRINT-31.md External Review section + added to ROADMAP Phase 4e candidates per S30 precedent.

## Alternatives Considered

### A1 — Combine S31 + S32 into one sprint

Rejected at refinement Step 4 cycle 1 (both lenses concurred). Combining produces ~25 tasks with two architectural decisions colliding (consolidation + versioning + rule-engine cutover + planner-snapshot simultaneously) — repeats the S28 thrash-defer failure mode. Split lets each sprint ship a clean marquee.

### A2 — Skip consolidation; jump straight to versioning the 5 fields already on `users`

Rejected. Versioning `agreement_code` / `ok_version` / `employment_category` without persisting the 3 rule-engine-consumed fields (`weekly_norm_hours` / `part_time_fraction` / `position`) leaves the rule engine non-deterministic on the consolidated subset. ADR-016 D5b's purpose is closed only when ALL profile fields are replay-stable.

### A3 — Migrate the 3 rule-engine fields onto `users` instead of a new table

Rejected. `users` is the auth-keyed table — JWT claim construction reads `users.agreement_code` directly. Adding versioning columns (`effective_from` / `effective_to` / `version` / multi-row history) to `users` would either (a) require row-level versioning on the auth path itself or (b) force the auth path to filter `effective_to IS NULL` on every JWT issue. Separate table keeps auth and profile-versioning concerns orthogonal. Q3 LEAVE in S31 + S32 decides whether the existing `users` fields migrate then.

### A4 — Use SQL-side INSERT INTO outbox_events in init.sql for backfill

Rejected. Bypasses EventSerializer registration → event payload JSON shape diverges from runtime serialization → replay determinism breaks. Seeder route (D8) keeps the outbox path consistent with all S22+ event emissions.

## Refinement Trail

`.claude/refinements/REFINEMENT-s31-phase-4d3.md` (2 cycles + 0 cycle-cap waivers):

- **Refinement Step 4 cycle 1**: 5 BLOCKERs (Q3 MIGRATE under-scoped per Codex P7 + Reviewer P7; P4 retroactive replay window per Reviewer P4; Q6 event vocabulary mis-shaped per Reviewer P3; Q7 no-effective_from contradicts S29/S30 lesson per Reviewer P1; AdminEndpoints POST silent on `EmployeeProfileCreated` per Reviewer + Codex convergent). All absorbed.
- **Refinement Step 4 cycle 2**: 4 NEW BLOCKERs surfaced (PK structural defect — `employee_id` forbids history rows per Codex; ComplianceEndpoints same-area-deeper-layer rule-engine input per Reviewer; AdminEndpoints user-create POST silent on profile-INSERT per Codex + Reviewer convergent; `is_part_time` redundancy invariant burden per Codex). All absorbed via mechanical fixes. Lens convergence: complementary not contradictory.

Plan Review Step 0b cycle 1 (1 cycle per lens; no waiver):

- **Codex Step 0b cycle 1**: 2 NEW BLOCKERs (TASK-3107 cross-org `OrgScopeValidator` binding missing under HROrAbove — convergent with no other lens; SPRINT-30.md sprint-end HEAD `e425b0d` vs actual `68a6f07` cross-doc defect). Both absorbed.
- **Reviewer Step 0b cycle 1**: 0 BLOCKERs (task count mismatch + line-range stale offsets + 3 NOTEs — all absorbed as cleanup).

Step 7a cycle 1 (gpt-5 baseline) + cycle 2 (gpt-5.5 post-upgrade):

- **Step 7a cycle 1**: 3 P2 BLOCKERs from Codex (GET row+version race; AdminEndpoints POST missing CREATED audit; Seeder missing CREATED audit). 0 from Reviewer. All absorbed in commit `e9733d0`.
- **Step 7a cycle 2**: 2 production-readiness findings (legacy DB upgrade P1; concurrent startup race P2). Deferred to Phase 4e per pre-launch posture (S30 precedent).

## Status History

- **2026-05-16**: Sprint open atop `68a6f07`. Plan filed (PLAN-s31.md). Codex CLI upgraded 0.120.0 → 0.130.0 mid-sprint to unlock `gpt-5.5` for Step 7a cycle 2.
- **2026-05-16**: Sprint close. ADR-022 filed as ACCEPTED. ADR-023 reserved for S32.

## Related ADRs

- **ADR-016** — Temporal Period Handling (D5b reconciled with 5 patterns through S30; S32's planner-snapshot will be 6th-pattern-equivalent or inherit D1 verbatim — adjudicated in ADR-023)
- **ADR-018** — Transactional Outbox + Row-Version Optimistic Concurrency (D3/D5/D6 inherited verbatim)
- **ADR-019** — Optimistic Concurrency via Row-Version (D2/D5/D6/D8 admin-strict If-Match inherited)
- **ADR-020** — Versioned-Config Design Foundations for Phase 4d-1 (D2 3-case routing pattern S32 will inherit for `SupersedeAndCreateAsync`)
- **ADR-021** — Entitlement-Policy Versioned History (D4 consumption-time-lookup pattern — S32 candidate for the non-rule-engine reads if any emerge)
