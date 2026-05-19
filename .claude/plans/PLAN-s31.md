# Sprint 31 — Phase 4d-3 Part 1: Employee-Profile Authoritative Store

## Sprint Header

| Field | Value |
|-------|-------|
| **Sprint** | 31 |
| **Title** | Phase 4d-3 Part 1: Employee-Profile Authoritative Store (data-plane only; no consumer cutovers) |
| **Status** | READY (Step 0b cycle 1 complete; 2 Codex BLOCKERs + 2 Codex WARNINGs + 2 Reviewer WARNINGs absorbed; cycle 2 NOT required per `feedback_thrash_defer_real_world.md` — no thrash signals; mechanical absorption clean) |
| **Start Date** | 2026-05-16 |
| **Projected End Date** | 2026-05-18 (2–3 days; smaller scope than S30 since zero consumer-side cutovers — write-side only + admin endpoint + frontend page) |
| **Sprint-start base commit** | `68a6f07` (S30 sprint close) |
| **Sprint type** | IMPLEMENTATION — Phase 4d-3 Part 1; foundational data-plane work for S32's versioning emission + rule-engine cutover + planner-snapshot. New ADR-022 (filed in-sprint). |
| **Refinement** | `.claude/refinements/REFINEMENT-s31-phase-4d3.md` (Step 4 cycles 1+2 dual-lens; cycle 1 absorbed 5 BLOCKERs; cycle 2 absorbed 4 NEW BLOCKERs without cycle-cap waiver — user adjudication 2026-05-16 = "accept findings + mechanical absorption + proceed"). User adjudication on Q1–Q7 + deeper question 2026-05-16. |
| **Agents involved** | Data Model (extended into Database Schema, cross-domain authorized — AGENTS.md L50 form-1), Backend API (cross-domain authorized for endpoints + AdminEndpoints POST extension), Frontend, Test & QA, Orchestrator-direct (KB writes + sprint plumbing) |
| **KB entries planned** | New: ADR-022 "Employee-Profile Consolidation + Pre-Baked Versioning (Phase 4d-3 Part 1)". No ADR-016 / ADR-018 / ADR-020 / ADR-021 amendments (S31 inherits established patterns; no new patterns). Updates: INDEX.md, sprints/INDEX.md, ROADMAP Phase 4d-3 Part 1 entry → COMPLETE + Part 2 (S32) stub. |

## Sprint Goal

Build the authoritative `employee_profiles` data store for Phase 4d-3 — consolidating 3 employment-profile fields (`weekly_norm_hours`, `part_time_fraction`, `position`) that today have no persisted source-of-truth (TimeEndpoints accepts them in request payload; ComplianceEndpoints hardcodes `WeeklyNormHours = 37.0m`). Build the admin CRUD endpoint pair + frontend admin page + extend the existing user-create POST for 4-way atomicity. **NO consumer-side cutovers in S31** — ComplianceEndpoints / BalanceEndpoints / TimeEndpoints / RuleEngine all stay on their current sources. S32 will cut over consumer paths atomically with versioning emission + planner-snapshot (closes ADR-016 D5b's last open slot).

**Marquee invariant**: `EmployeeProfileEdit_RoundTripsAtomically_WithVersionedAuditAndEvent` — admin PUT to `/api/admin/employee-profiles/{employeeId}` returns 200 + new ETag; the row, audit row (`UPDATED`, version_before=1, version_after=2), and outbox event ride a single transaction. Verifies 3-way atomicity on the write path; load-bearing because S32 will extend this surface with versioning emission + supersession routing (ADR-020 D2 inheritance).

**Convergent BLOCKER fix from refinement cycle 2**: `POST /api/admin/users` (AdminEndpoints.cs:292) is extended for 4-way atomicity — `users` INSERT + `employee_profiles` INSERT + `UserCreated` outbox + `EmployeeProfileCreated` outbox, all in one tx. New invariant: "every user has exactly one live employee_profiles row." Distinct D-test `AdminUserCreate_AtomicallyCreatesProfileRowAndEmitsEvent` exercises this.

**Architectural decisions settled in refinement (Step 4 cycles 1+2)**:

1. **Q1 SPLIT confirmed** — S31 = data-plane setup; S32 = versioning emission + rule-engine cutover + planner-snapshot. Both lenses concurred; ROADMAP L356 + ADR-020 §124 anticipate multi-sprint Phase 4d-3.
2. **Q2 NEW ENDPOINT PAIR confirmed** — `/api/admin/employee-profiles/{employeeId}` GET + PUT under `HROrAbove`. Mirrors S29/S30 per-resource endpoint pattern.
3. **Q3 LEAVE confirmed** — `agreement_code`/`ok_version`/`employment_category` stay in `users` table. Migration to employee_profiles deferred to S32 where versioning is already touching all profile reads. JWT/auth path untouched.
4. **Q4 REVERSE confirmed** — `CalculateRequest.WeeklyNormHours` + `WeeklyCalculateRequest.WeeklyNormHours` stay in request contracts; TimeEndpoints rule-engine path unchanged. Hard-cut moves to S32 atomic with planner-snapshot.
5. **Q5 DEFERRED TO S32** — "as-of-date" UI semantic is meaningful only after versioning emission; S31 has no history surface.
6. **Q6 REGISTER-4-EMIT-2 confirmed** — 4 event types registered in S31 (`EmployeeProfileCreated/Updated/Superseded/SoftDeleted`); 2 emit in S31 (CREATED on backfill + admin-create; UPDATED on admin-edit); 2 reserved for S32 emission. EventSerializer 51 → 55 in S31; stable through S32.
7. **Q7 PRE-BAKE + SURROGATE UUID PK confirmed** — `CREATE TABLE employee_profiles` includes `profile_id UUID PRIMARY KEY DEFAULT gen_random_uuid()` (S29 WTM precedent — multi-row-per-employee forward-compat for S32 history rows), effective_from/effective_to/partial-unique-index/history-unique-index/version columns all present in S31. S31 reads/writes only live rows (effective_to IS NULL); S32 starts emitting closed predecessors. Saves a schema migration in S32.
8. **`is_part_time` DROPPED** (cycle 2 absorption) — column not present in schema; `EmployeeProfileRepository.GetByEmployeeIdAsync` computes `IsPartTime = part_time_fraction < 1.0m` when constructing `EmploymentProfile`. Eliminates Assumption #12 invariant-burden.
9. **Init.sql ordering (deeper question)** — separate post-table-create idempotent INSERT block at the bottom of init.sql (S29/S30 pattern). CREATE TABLE lands at the schema-natural position (near users); backfill INSERTs land at the bottom after the migration ledger block. ON CONFLICT (employee_id) WHERE effective_to IS NULL DO NOTHING for idempotency under `docker compose down -v && up`.

## Phase Decomposition

Replicates S29/S30 pattern: Phase 0 plumbing → Phase 1 sequential plumbing → Phase 2 parallel endpoint+frontend → Phase 3 D-tests → Phase 4 validation → Phase 5 docs + sprint close.

### Phase 0 — Sprint-Open Plumbing (1 commit)

**TASK-3100** creates `SPRINT-31.md` from TEMPLATE.md + INDEX.md provisional row. Mirrors S30 TASK-3000. No TASK-3101 prerequisite gate this sprint (TASK-3001 was the WAF<Program> diagnosis, which is already fixed; no new Phase-4e mini-task folds in).

### Phase 1 — Plumbing (sequential, 5 commits)

Schema, audit-table CREATE, repository, new event types, fixture DDL drift, seed-INSERT block. Mirrors S30 Phase 1 task count.

**Critical**: Commit each Phase 1 task before dispatching the next that depends on it. **Commit ALL Phase 1 tasks before dispatching Phase 2 parallel agents** to prevent the S24/S26 worktree-base-mismatch pattern. S29/S30 followed this discipline cleanly; carry forward verbatim.

### Phase 2 — Endpoint + AdminEndpoints Extension + Frontend (parallel-dispatchable after Phase 1 commit, 3 commits)

Three file-disjoint tasks **dispatched in parallel without worktree isolation** (NOT `isolation: "worktree"`) per S29/S30 Phase 2 precedent — file-disjoint scopes verified by the Orchestrator at dispatch time. (a) admin-CRUD endpoints (TASK-3107), (b) AdminEndpoints POST extension for 4-way atomicity (TASK-3108), (c) frontend admin page (TASK-3109). S24+S26 worktree-base-mismatch lessons apply: commit Phase 1 ledger fully before this dispatch.

### Phase 3 — D-tests (sequential after Phase 2 commit, 1 commit)

Marquee + AdminEndpoints user-create 4-way atomicity + admin-CRUD shape + ETag/If-Match + RBAC matrix.

### Phase 4 — Validation (Orchestrator-direct, no new commit unless fixes)

Per WORKFLOW.md Step 4: `dotnet build` + run full test suites (unit + plain regression + Docker-gated + frontend vitest). Apply the **sprint-test-validation** skill at sprint validation time (previous + delta = current arithmetic). If failures surface beyond expected delta, dispatch fix agents and re-run. If clean, proceed to Step 7a per "Step 7a Strategy" section.

### Phase 5 — Documentation + sprint close (Orchestrator-direct, 2 commits)

New ADR-022 + INDEX update + sprint plumbing.

---

## Task Decomposition (13 declared tasks; TASK-3105/3106 fork resolved to seeder route at Step 0b)

| ID | Name | Owner | Files in scope | Depends on | Parallel-dispatchable |
|----|------|-------|----------------|------------|-------------------|
| **TASK-3100** | Sprint-open plumbing — create `SPRINT-31.md` from `TEMPLATE.md` + INDEX.md provisional row | Orchestrator-direct | `docs/sprints/SPRINT-31.md` (new), `docs/sprints/INDEX.md` (provisional row) | none | no (Phase 0 root) |
| **TASK-3101** | Schema migration `s31-d3-employee-profile-store` + `employee_profile_audit` CREATE | Data Model (extended into Database Schema, cross-domain authorized) — AGENTS.md L50 form-1 | `docker/postgres/init.sql` (new `CREATE TABLE employee_profiles` at schema-natural position near users L470; new `CREATE TABLE employee_profile_audit`; `schema_migrations` guarded ALTER block — though S31 is greenfield-only so ALTER block is forward-compat for future production) | TASK-3100 | no (Phase 1 root) |
| **TASK-3102** | `EmployeeProfileRepository` with atomic-outbox `(conn, tx)` overloads | Data Model (extended into Infrastructure, cross-domain authorized) — AGENTS.md L50 form-1; S29 WTM + S30 EntitlementConfig precedent | `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileRepository.cs` (new) — methods: `GetByEmployeeIdAsync(id)` (returns `EmploymentProfile` with `IsPartTime` computed), `UpsertAsync(conn, tx, profile, expectedVersion?)` with ADR-019 admin-strict If-Match + ADR-018 D5 `(conn, tx)` overloads | TASK-3101 | no (sequential plumbing) |
| **TASK-3103** | 4 new events: `EmployeeProfileCreated/Updated/Superseded/SoftDeleted` + EventSerializer registration | Data Model | `src/SharedKernel/StatsTid.SharedKernel/Events/EmployeeProfileCreated.cs` + `Updated.cs` + `Superseded.cs` + `SoftDeleted.cs` (new); `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` (4 new entries; 51 → 55) | none (parallel-eligible with TASK-3101/3102 in principle, sequenced for simplicity) | no |
| **TASK-3104** | Test fixture DDL drift coordinated with TASK-3101 | Test & QA | Mirror sites for users/EmploymentProfile in fixtures — Grep at decompose-time; likely 0–2 sites; if 0 sites, task collapses to assertion (S30 TASK-3005 precedent) | TASK-3101 | no |
| **TASK-3105** | Init.sql schema bootstrap for `employee_profiles` (NO seed INSERTs in init.sql) | Data Model (extended into Database Schema, cross-domain authorized) — AGENTS.md L50 form-1 | `docker/postgres/init.sql` — CREATE TABLE / audit / indexes already in TASK-3101; TASK-3105 only verifies that NO seed INSERTs are written into init.sql for `employee_profiles` (seeding happens via the runtime seeder at TASK-3106). This keeps init.sql clean of event-emission concerns (events cannot be reliably emitted from raw SQL — they need IOutboxEnqueue serialization). Step 0b Codex WARNING absorption: SQL-vs-seeder fork **resolved to seeder**. | TASK-3101 | no |
| **TASK-3106** | EmployeeProfileSeeder (committed seeder route per Step 0b absorption) | Data Model (extended into Infrastructure, cross-domain authorized) | `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileSeeder.cs` (new) — runs at Program.cs startup AFTER schema migrations + AFTER existing seeders (if any wired); idempotent (reads `users` + existing `employee_profiles`; creates ONLY missing rows); emits `EmployeeProfileCreated` events via `IOutboxEnqueue` for each row created. Wire registration into `Program.cs` host build. Mirrors S26+ outbox-event-emission patterns. **No longer conditional** — TASK-3105/3106 fork resolved at Step 0b. | TASK-3102, TASK-3103, TASK-3105 | no |
| **TASK-3107** | `EmployeeProfileEndpoints.cs` new admin CRUD pair | Backend API (cross-domain authorized) | `src/Backend/StatsTid.Backend.Api/Endpoints/EmployeeProfileEndpoints.cs` (new) — GET + PUT under `HROrAbove` **AND `OrgScopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct)`** on BOTH endpoints (Step 0b Codex BLOCKER fix: HROrAbove alone proves role + scope claim but doesn't bind to target employee's org — a cross-org HR data leak without scope validation; mirrors `BalanceEndpoints.cs:48-53` pattern); ADR-019 admin-strict If-Match (412/428/409); ADR-018 D5 atomic outbox (`EmployeeProfileUpdated` event in same tx); `Program.cs` registration | TASK-3102, TASK-3103, TASK-3104 | parallel-dispatchable (file-disjoint from TASK-3108 + TASK-3109; non-worktree dispatch) |
| **TASK-3108** | AdminEndpoints POST extension for 4-way atomicity | Backend API (cross-domain authorized) | `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` POST `/api/admin/users` handler (approximately L292–L387; re-locate at dispatch time via Grep on `MapPost("/api/admin/users"` — exact internal tx-block lines drift across edits, so dispatch-time location is more reliable than pinning sub-line offsets) | TASK-3102, TASK-3103 | parallel-dispatchable (file-disjoint from TASK-3107 + TASK-3109; non-worktree dispatch) |
| **TASK-3109** | Frontend admin page + hook + sidebar entry | Frontend | `frontend/src/pages/admin/EmployeeProfileEditor.tsx` (new), `frontend/src/hooks/useEmployeeProfile.ts` (new), sidebar registration, TypeScript types. Edits 3 fields (weekly_norm_hours / part_time_fraction / position; `is_part_time` derived if shown). HROrAbove RBAC. Banner-with-retry on 412. NO history view (deferred S32+). | TASK-3107 (HTTP shape locks first) | parallel-dispatchable (file-disjoint from TASK-3107 + TASK-3108; non-worktree dispatch) |
| **TASK-3110** | D-tests suite | Test & QA | `tests/StatsTid.Tests.Regression/Config/EmployeeProfileEndpointTests.cs` (new — HTTP-level CRUD shape tests via WAF<Program>) + `tests/StatsTid.Tests.Regression/Outbox/AdminUserCreateAtomicTests.cs` (new — 4-way atomicity for the AdminEndpoints POST extension) + `tests/StatsTid.Tests.Regression/Outbox/EmployeeProfileAtomicTests.cs` (new — marquee `EmployeeProfileEdit_RoundTripsAtomically_WithVersionedAuditAndEvent`; mirrors S25/S26 atomic-test naming under `Outbox/`) | TASK-3101..3109 | no |
| **TASK-3111** | New ADR-022 + INDEX.md | Orchestrator-direct (KB writes per WORKFLOW.md L48) | `docs/knowledge-base/decisions/ADR-022-employee-profile-consolidation.md` (new) + `docs/knowledge-base/INDEX.md` | Phase 1 + Phase 2 + Phase 3 complete | no |
| **TASK-3112** | Sprint-close plumbing | Orchestrator-direct | `docs/sprints/SPRINT-31.md` (close sections), `docs/sprints/INDEX.md` (final row), `ROADMAP.md` (Phase 4d-3 Part 1 → COMPLETE + Part 2 stub for S32), `docs/QUALITY.md` (Infrastructure / Backend API re-grade), `~/.claude/projects/C--StatsTid/memory/MEMORY.md` (S31 line) | TASK-3111 | no |

**Net new D-tests target**: 14–18 (+1 from cycle 2 absorption for the user-create 4-way atomicity; +1 from Step 0b BLOCKER #1 for cross-org scope validation). Breakdown:
- Marquee (1)
- Admin CRUD shape — GET + PUT (3: 200 happy-path, 404 not-found, 401 unauthorized)
- ADR-019 If-Match contract on PUT (3: 412 stale, 428 missing, 23505-vs-412 distinction if applicable)
- RBAC matrix (3–4: HR allowed PUT, LocalLeader 403, Employee 403, LocalAdmin 403)
- **Cross-org scope validation (1 — NEW from Step 0b BLOCKER): HR user from org X attempts GET/PUT for employee in org Y → 403 via OrgScopeValidator**
- User-create 4-way atomicity (1 — TASK-3110b)
- Validation (1–2: percentage bounds, position max length)
- Backfill on bootstrap (1: assert 7 events emitted post-`docker compose up`)

### Validation criteria per task

Deferred to the SPRINT-31.md sprint log (created at sprint open by TASK-3100; close sections filled by TASK-3112) — mirror S30's validation-criteria structure verbatim.

## Critical-Path Callouts

1. **S31 has ZERO consumer cutovers** — ComplianceEndpoints / BalanceEndpoints / TimeEndpoints / RuleEngine.Api ALL unchanged. The authoritative store exists but is read by NO production code path until S32. This is the load-bearing scope invariant from refinement cycle 2 absorption; eliminates the P4 retroactive replay window. Sprint Close Criterion: Grep verification at sprint close — `EmployeeProfileRepository` referenced only from `EmployeeProfileEndpoints.cs` + `AdminEndpoints.cs:292` + `EmployeeProfileSeeder.cs` (if seeder route).

2. **Surrogate UUID PK is load-bearing for S32** — `employee_profiles.profile_id UUID PRIMARY KEY DEFAULT gen_random_uuid()` allows multi-row-per-employee history when S32 starts emitting closed predecessors. S29 WTM precedent (`mapping_id UUID PRIMARY KEY`) carries verbatim. The original cycle-1 spec (`employee_id TEXT PRIMARY KEY`) would have forbidden S32's history INSERTs — Codex cycle 2 caught this.

3. **AdminEndpoints POST extension is the cross-cutting concern** — currently the only S31 site that mutates a non-S31 file (`AdminEndpoints.cs` exists since S26). The 4-way atomicity invariant (users INSERT + employee_profiles INSERT + UserCreated outbox + EmployeeProfileCreated outbox in one tx) requires careful threading of `(conn, tx)` overloads. TASK-3108 must verify ALL existing AdminEndpoints POST tests still pass after the extension.

4. **Pre-baked versioning columns are dormant in S31** — `effective_from` defaults to `'0001-01-01'`, `effective_to` stays NULL on every S31-written row, `version` stays at 1 (or increments by 1 on UPDATE). S31 reads ONLY live rows. S32 activates the multi-row history path. The pre-baking saves S32 a schema migration.

5. **EmployeeProfileSeeder vs SQL-side outbox INSERT** (TASK-3105/3106 fork) — refinement deferred to task-dispatch time. Both patterns are precedented (AgreementConfigSeeder = startup-runtime seeder; entitlement_configs = SQL-side INSERT). Recommend seeder pattern because `EmployeeProfileCreated` event emission needs to go through `IOutboxEnqueue` for consistency — SQL-side INSERT into `outbox_events` would bypass the event-serialization layer and break replay determinism. Final call at TASK-3105 dispatch.

6. **`EmploymentProfile.IsPartTime` consumers** — PCS L335 reads `profile.IsPartTime`. `EmployeeProfileRepository.GetByEmployeeIdAsync` computes it as `part_time_fraction < 1.0m` when constructing the EmploymentProfile object. SharedKernel `EmploymentProfile.cs` shape UNCHANGED — `IsPartTime` stays as a get-only property (or an init-only with the computation in repository). No drift between schema-side and SharedKernel-side.

7. **JWT/auth path untouched** — Q3 LEAVE means `users.agreement_code` stays. AuthEndpoints.cs:34-35 + JwtTokenService.cs:31 read from users unchanged. Smoke test verifies login flow at sprint close.

8. **ADR-022 commits to S32 deferred work** — explicit list in ADR-022 of what S32 must do: (a) versioning emission for SUPERSEDED + SOFT_DELETED events; (b) ComplianceEndpoints + BalanceEndpoints cutover atomic with planner-snapshot; (c) TimeEndpoints + CalculateRequest hard-cut atomic with planner-snapshot; (d) per-field bucketing decision (ADR-023, S32-filed); (e) "as-of-date" UI toggle. If S31 ships clean, S32 has a stable target.

## Test-Count Projection

S30 ended at **815** (526 unit + 35 plain regression + 166 Docker-gated passing + 88 frontend; 18 pre-existing Docker-gated failures unchanged).

S31 projected end-of-sprint:

| Suite | S30 close | S31 close | Notes |
|-------|-----------|-----------|-------|
| Unit | 526 | 526 | No SharedKernel signature change; no new unit test required |
| Plain regression | 35 | 35 | |
| Docker-gated (passing) | 166 | 179–183 (+13..+17) | Breakdown: +1 marquee + +3 admin-CRUD shape + +3 If-Match contract + +3–4 RBAC matrix + +1 user-create 4-way atomicity + +1–2 validation + +1 backfill bootstrap |
| Docker-gated (pre-existing failures, unchanged) | 18 | 18 | Out of scope; date-sensitive `OkStraddleSources` + atomic/concurrency state issues |
| Frontend vitest | 88 | 88 (+0..4 stretch) | Optional: a handful of vitest cases for `useEmployeeProfile` + EmployeeProfileEditor page render; if scope-bloating, defer to S32 polish |
| **Total** | **815** | **828–832** (+13..+17 floor; +0..+4 frontend stretch) | Floor +13 = 828; stretch +17 = 832; +4 frontend = 836 |

**Acceptance floor**: total passing ≥ **828** by sprint close (+13 net new D-tests).
**Stretch**: 832 if test count hits +17; 836 if frontend vitest also lands.

## Risk Register

| # | Risk | Likelihood | Mitigation |
|---|------|-----------|------------|
| R1 | **Frontend admin page diverges from new endpoint shape mid-sprint** | low | Phase 2 dispatch order: TASK-3107 (endpoint) → TASK-3109 (frontend) is dependency-ordered; smoke-test frontend dev server before sprint close. Worktree-base-mismatch prevention discipline carried from S29/S30 (commit Phase 1 before Phase 2 dispatch). |
| R2 | **AdminEndpoints POST extension breaks existing user-create flow** | low-medium | TASK-3108 verifies ALL existing AdminEndpoints POST tests still pass after extension. 4-way atomicity D-test (TASK-3110) verifies the new contract; existing 1-way (UserCreated only) regression tests verify no regression. |
| R3 | **TASK-3105/3106 SQL-vs-seeder fork generates implementation inconsistency** | low | Recommended seeder route (TASK-3106) per Critical-Path Callout 5 — keeps event emission consistent with all other S22+ outbox paths. Decide at TASK-3105 dispatch with explicit justification logged in commit message. |
| R4 | **Surrogate UUID PK breaks any code that assumed `employee_id` PK** | low | No existing code references `employee_profiles` (greenfield table). Grep at sprint close verifies no `employee_profile_id` confusion with `employee_id`. |
| R5 | **EmployeeProfileSeeder ordering issue at Program.cs startup (conditional on seeder route)** | medium | Sequence at Program.cs startup must be: (a) schema migrations apply → (b) any existing seeders (e.g., AgreementConfigSeeder if still wired in S26+) run → (c) EmployeeProfileSeeder runs (depends on users table being seeded; uses `users.user_id` FK). If users seeding happens via init.sql and not via a seeder, EmployeeProfileSeeder runs after init.sql applies. Verify at TASK-3106 dispatch. **Conditional**: If TASK-3105 chooses the SQL-side INSERT route (post-table-create idempotent block in init.sql per deeper-question recommendation) and NO seeder is created, this risk is MOOT — init.sql ordering handles it deterministically. R5 fires only on the seeder route. |
| R6 | **Worktree-base-mismatch from dispatching Phase 2 before Phase 1 fully committed (historical S24/S26 pattern)** | low | Carry S29/S30 discipline forward: explicit "commit ALL Phase 1 tasks before dispatching Phase 2 parallel agents" callout; verified at Phase 1 close before Phase 2 dispatch. Phase 2 uses non-worktree parallel dispatch (no `isolation: "worktree"`). |
| R7 | **Test-fixture DDL drift** — pre-baking `employee_profiles` columns means any test fixture that builds DDL by hand will be missing the new table | medium | TASK-3104 Grep-verifies at decompose-time; if 0 sites today, task collapses to assertion. ForcedRollbackHarness DDL inlining precedent from S25 TASK-2508 / ef9ec91 carries forward. |
| R8 | **AdminEndpoints POST extension breaks the existing AdminUserUpdate tests due to schema dependency** | low | TASK-3104 covers any test-fixture DDL drift. Pre-existing AdminEndpoints tests (S26+ AdminAtomicTests) verify via the existing outbox pattern; new test adds 4-way assertion without removing 1-way. |
| R9 | **ADR-022 framing collides with ADR-020 / ADR-021** — three sibling ADRs for Phase 4d (D1 → D2 → D3) might confuse readers | low | ADR-022 explicitly cross-references ADR-020 §122 + ADR-021 §25 (the §122 anticipation chain); positions itself as "Phase 4d-3 Part 1" with explicit Part-2 commitment to S32. The pattern landscape (ADR-016 D5b "fifth pattern") stays at 5 — S32 inherits ADR-020 D1 + (optionally) ADR-021 D4 without filing a new pattern. |
| R10 | **`EmploymentProfile.IsPartTime` consumers see drift between in-memory and DB shape** | low | SharedKernel `EmploymentProfile.cs` shape UNCHANGED. Repository computes `IsPartTime` on read. Critical-Path Callout 6 documents the convention; D-test asserts `IsPartTime = true` when `part_time_fraction = 0.5m` round-trips correctly. |

## Explicit Exclusions (deferred-out to S32)

| Item | Disposition | Why deferred |
|------|-------------|--------------|
| ComplianceEndpoints cutover (read `weekly_norm_hours` from store) | S32 | Compliance is rule-engine input (cycle 2 BLOCKER); cutover without versioning + planner-snapshot re-introduces the P4 window. S32 lands cutover atomic with planner-snapshot. |
| BalanceEndpoints fallback chain cutover | S32 | For consistency with Compliance defer + cleaner cutover surface. |
| TimeEndpoints `CalculateRequest.WeeklyNormHours` hard-cut | S32 | Rule-engine path stays on request-payload until S32's planner-snapshot makes it safe (Q4 reverse). |
| `agreement_code` / `ok_version` / `employment_category` migration from `users` | S32 | Q3 LEAVE in S31 per both lenses' BLOCKERs on Q3 MIGRATE; S32 may version inline or migrate. |
| `SUPERSEDED` / `SOFT_DELETED` event emission | S32 | 2 events registered in EventSerializer but not emitted in S31 (Q6); S32 starts emitting on cross-day admin edit + soft-delete. |
| "As-of-date" UI toggle | S32 | No history surface in S31 (Q5 deferred). |
| Per-field bucketing matrix (ADR-023) | S32 | Decides which fields planner-snapshotted vs consumption-time vs last-write-wins; S31 doesn't need this because no cutovers. |
| Soft-delete DELETE endpoint on `/api/admin/employee-profiles/{employeeId}` | S32 | S31 only has GET + PUT (no rows are ever closed in S31). |
| LocalLeader position-edit capability | S32 or Phase 5+ | S31 RBAC is HROrAbove only; Leader is below in hierarchy. Position editing for Leaders is a UX decision that needs design work. |
| Frontend vitest for `useEmployeeProfile` + EmployeeProfileEditor | S32+ polish | Optional in S31 stretch; lower priority than D-tests. |
| `EmploymentProfile.OrgId` field changes | not in S31/S32 | Added in S21 / TASK-2108; unchanged in Phase 4d-3. |
| Employee-self-read endpoint (`GET /api/employee-profile/me`) | S32 or later | `/api/admin/employee-profiles/{employeeId}` is HROrAbove-only in S31; Employee users have no direct read surface. Acceptable in S31 because consumers (MinTid page etc.) are UNCHANGED and still read fields via existing paths. After S32 consumer cutovers, this gap becomes felt — Phase 5 or S32+1 candidate. |
| S30 cycle-2 deferrals (init.sql legacy upgrade, parseInt truncation) | Phase 4e | S30 carry-forward; not Phase 4d-3 work. |
| ADR-016 D5b pattern landscape extension | not in S31 | Five patterns stable; S31 inherits ADR-020 D1 pattern for the (deferred) S32 cutover. No new pattern. |
| Step 7a external Codex/Reviewer review | Required pre-commit by default per WORKFLOW.md L38; user may grant documented exit when marquee substantively serves Step-7a-equivalent purpose (S29/S30 precedent) | Not presumed for S31 — user decides at sprint close based on marquee outcome. Given the data-plane-only scope, marquee alone may be sufficient; alternatively, dual-lens review of the diff is genuinely useful. |

## Worktree-Base-Mismatch Prevention

**Explicit discipline** (S24 + S26 burned us; S29 + S30 cleanly avoided): **Commit all 5 Phase 1 tasks (TASK-3101 through TASK-3105/3106) to `master` BEFORE dispatching the Phase 2 parallel agents.** TASK-3100 (sprint open) runs separately at Phase 0 root.

The Phase 2 dispatch in a single message contains 3 file-disjoint tasks (TASK-3107 endpoint, TASK-3108 AdminEndpoints extension, TASK-3109 frontend) **dispatched as parallel general-purpose agents WITHOUT `isolation: "worktree"`** per S29/S30 precedent — file-disjoint scopes verified before dispatch:

- TASK-3107 → `src/Backend/StatsTid.Backend.Api/Endpoints/EmployeeProfileEndpoints.cs` (new) + `Program.cs` (1 registration line)
- TASK-3108 → `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` (existing; edits L292-387 POST handler)
- TASK-3109 → `frontend/src/pages/admin/EmployeeProfileEditor.tsx` (new) + hooks + sidebar

`Program.cs` is the only file that *could* see a multi-agent edit (TASK-3107 adds 1 registration line). TASK-3108 has no `Program.cs` writes; TASK-3109 has no `Program.cs` writes. Single-edit on `Program.cs` by TASK-3107 only — no merge conflict possible.

## Step 7a Strategy

Per WORKFLOW.md, Step 7a (external dual-lens review at sprint close) is required pre-commit. **User may grant a documented exit** when the marquee D-test substantively serves the Step-7a-equivalent purpose (S29/S30 precedent). The plan does NOT presume the exit; the Orchestrator dispatches Step 7a at sprint close UNLESS the user explicitly waives it citing the marquee outcome.

S31 has a more focused scope than S30 (zero consumer cutovers; data-plane only). The marquee is straightforward (atomic 3-way write); dual-lens review value is in catching the AdminEndpoints POST extension subtleties + the seeder ordering at startup. **Recommend running Step 7a** at sprint close for assurance on the cross-cutting AdminEndpoints change.

If Step 7a runs, cycle-cap = 2 per lens per `feedback_step7a_cycle_cap_discipline.md`.

## Sprint Close Criteria

- [ ] `dotnet build` clean (0 errors; preserve existing CS0618 warnings unchanged)
- [ ] All Phase 1 tasks committed before Phase 2 dispatch (worktree-base-mismatch prevention)
- [ ] Schema migration `s31-d3-employee-profile-store` ledger-tracked + idempotent under `docker compose down -v && up`
- [ ] `employee_profiles` table exists with PK = surrogate UUID `profile_id`; pre-baked versioning columns present (`effective_from` DEFAULT `'0001-01-01'`, `effective_to` NULL, partial-unique-index `(employee_id) WHERE effective_to IS NULL`, history-unique-index `(employee_id, effective_from)`, `version BIGINT NOT NULL DEFAULT 1`); **`is_part_time` column NOT present**
- [ ] `employee_profile_audit` table created with ADR-019 D8 version-transition columns + CHECK including all 4 actions up-front
- [ ] `EmployeeProfileRepository` exposes `GetByEmployeeIdAsync(id)` (computes `IsPartTime`), `UpsertAsync(conn, tx, profile, expectedVersion?)` with atomic-outbox `(conn, tx)` overloads
- [ ] Backfill: all 7 seeded users get a corresponding `employee_profiles` row + each backfill emits `EmployeeProfileCreated` atomically (verified by D-test)
- [ ] `POST /api/admin/users` 4-way atomicity D-test passes (`AdminUserCreate_AtomicallyCreatesProfileRowAndEmitsEvent`)
- [ ] `EmployeeProfileEndpoints.cs` exists + registered in `Program.cs` + `HROrAbove` RBAC **AND `OrgScopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct)` on both GET and PUT** (Step 0b BLOCKER fix); ADR-019 If-Match contract on PUT
- [ ] **Cross-org scope D-test**: HR user from org X attempts GET/PUT on employee in org Y → 403; verifies OrgScopeValidator binding works on the new endpoint pair
- [ ] `EventSerializer` count 51 → 55; S18 reflection-coverage test passes
- [ ] **NO consumer cutovers** — `ComplianceEndpoints.cs:72-77` UNCHANGED; `BalanceEndpoints.cs:66-68` fallback chain UNCHANGED; `TimeEndpoints.Calculate` + `WeeklyCalculate` UNCHANGED; `CalculateRequest.cs` + `WeeklyCalculateRequest.cs` UNCHANGED; `RuleEngine.Api` UNCHANGED — Grep verification at sprint close
- [ ] **`/api/admin/users/{userId}` PUT UNCHANGED** — JWT/auth path untouched; smoke test verifies login flow
- [ ] Marquee D-test `EmployeeProfileEdit_RoundTripsAtomically_WithVersionedAuditAndEvent` passes
- [ ] +13..17 net new D-tests passing (admin CRUD shape, user-create atomicity, ETag, RBAC, validation, backfill)
- [ ] Frontend `EmployeeProfileEditor.tsx` + `useEmployeeProfile` hook + admin sidebar entry; banner-with-retry on 412; passes vitest if frontend tests added
- [ ] New **ADR-022 "Employee-Profile Consolidation + Pre-Baked Versioning (Phase 4d-3 Part 1)"** filed at `docs/knowledge-base/decisions/ADR-022-...md` with explicit S32 commitment list
- [ ] Total tests ≥ **828** passing (815 baseline + 13 floor; stretch 832 or 836)
- [ ] SPRINT-31.md status = complete; sprint-end HEAD recorded; Sprint Retrospective written; INDEX.md updated; ROADMAP Phase 4d-3 Part 1 → COMPLETE + Part 2 (S32) stub
- [ ] MEMORY.md S31 line added (separate edit outside repo)
- [ ] No regression in S30's 815-test baseline; 18 pre-existing failures remain unchanged (out of scope)

---

## Step 0b Plan Review

Both lenses dispatched 2026-05-16 against the initial plan draft (pre-absorption).

### Findings (cycle 1)

*External (Codex):* — 2 BLOCKERs + 2 WARNINGs + 1 NOTE

- **BLOCKER (P7)**: TASK-3107 / Sprint Close Criteria encoded `HROrAbove` as the gate but did not require target-employee scope validation. `HROrAbove` proves role + some scope claim but NOT the target binding — that's done by `OrgScopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct)` (mirrors `BalanceEndpoints.cs:48-53` pattern). A policy-only GET/PUT on `/api/admin/employee-profiles/{employeeId}` would be a cross-org HR data leak. → **Absorbed**: TASK-3107 + Sprint Close Criteria updated to require OrgScopeValidator on both GET and PUT; new cross-org D-test added to TASK-3110 D-tests target (+1; 14–18 net new D-tests).
- **BLOCKER (P1 traceability)**: Sprint-start base commit mismatch — plan said S30 close/base was `68a6f07` (correct: actual git HEAD post-TASK-3012), but `docs/sprints/SPRINT-30.md` L13 recorded sprint-end HEAD as `e425b0d` (TASK-3011's commit; missed update when TASK-3012 landed). Makes base-commit metadata unreliable for S31 Step 7a + sprint traceability. → **Absorbed**: SPRINT-30.md L13 corrected to `68a6f07` (actual git HEAD post-sprint-close); plan's base commit `68a6f07` is the real one. S30 sprint-log defect noted inline for audit trail.
- WARNING: TASK-3105/TASK-3106 SQL-vs-seeder fork was "decide at dispatch time" — Step 0b should usually collapse this. → **Absorbed**: fork resolved to **seeder route** per refinement Critical-Path Callout 5 (event emission needs `IOutboxEnqueue` serialization; SQL-side `outbox_events` INSERT would bypass the event-serialization layer and break replay determinism). TASK-3106 no longer conditional.
- WARNING: TASK-3110 marquee test placement "TBD"; existing taxonomy is `Config/*EndpointTests.cs` for WAF HTTP + `Outbox/*AtomicTests.cs` for atomic-rollback. → **Absorbed**: marquee pinned to `tests/StatsTid.Tests.Regression/Outbox/EmployeeProfileAtomicTests.cs`; CRUD shape tests in `Config/EmployeeProfileEndpointTests.cs`; 4-way atomicity in `Outbox/AdminUserCreateAtomicTests.cs`. Convergent with Reviewer NOTE.
- NOTE: Agent labels match AGENTS.md L48-51 form-1 verbatim (S30 precedent); KB references (ADR-016 D5b / ADR-018 D5 / ADR-019 / ADR-020 D2 / ADR-021 §25) still live post-S30; "NO consumer cutovers" close criterion is Grep-verifiable as written. Confirmatory.

*Internal (Reviewer Agent):* — 0 BLOCKERs + 2 WARNINGs + 3 NOTEs

- WARNING (P3): Task count mismatch — section heading "11 declared tasks" vs table content (13 rows). → **Absorbed**: heading corrected to "13 declared tasks (TASK-3100–3112, TASK-3105/3106 fork resolved to seeder at Step 0b)."
- WARNING (P3): TASK-3108 AdminEndpoints POST line-range references stale offsets (cited `292-387` + internal `333-369`). → **Absorbed**: replaced sub-line offsets with Grep-at-dispatch guidance ("re-locate at dispatch time via Grep on `MapPost(\"/api/admin/users\"`"); dispatch-time location is more reliable than pinning offsets.
- NOTE (P4): Marquee D-test placement underspecified. → **Convergent with Codex WARNING above; absorbed via pinning to `Outbox/EmployeeProfileAtomicTests.cs`**.
- NOTE (P4): Employee self-read endpoint (e.g., `GET /api/employee-profile/me`) absent from S31 scope. → **Absorbed**: added to "Explicit Exclusions" table with rationale (HROrAbove-only is fine for S31 because consumers UNCHANGED; gap becomes felt only after S32 cutovers; Phase 5 or S32+1 candidate).
- NOTE (P4): R5 EmployeeProfileSeeder ordering may be moot if init.sql wins the TASK-3105/3106 fork. → **Resolved upstream**: Codex WARNING absorbed by committing to seeder route; R5 now correctly fires for the seeder route (no longer conditional language).

**Cycle 1 lens convergence**: Both lenses BLOCK on TASK-3105/3106 fork being open (Codex WARNING-tier, Reviewer NOTE-tier) → upgraded to forced resolution at Step 0b. Both lenses BLOCK on marquee placement (convergent) → pinned. Codex caught the HROrAbove cross-org leak that Reviewer missed (Reviewer marked HROrAbove as "matches between TASK-3107 backend and TASK-3109 frontend" without checking target-employee binding). Codex caught the SPRINT-30.md HEAD defect that Reviewer wouldn't catch (file-level state). Reviewer caught the line-range stale-offsets and task-count mismatch that Codex didn't surface. **Complementary lens coverage, not contradictory** — standard `feedback_review_lens_complementarity.md` pattern.

### Resolution

All cycle 1 BLOCKERs absorbed (2 Codex BLOCKERs fixed in-plan; 1 cross-doc fix to SPRINT-30.md L13). All cycle 1 WARNINGs absorbed (Codex 2 + Reviewer 2). 4 NOTEs absorbed (Reviewer 3 + Codex 1 confirmatory).

**Cycle 2 NOT requested** — all BLOCKERs converged on coherent fixes; no same-area-deeper-layer thrash signals (per `feedback_thrash_defer_real_world.md`); 2 BLOCKERs were structural defects (cross-org leak + cross-doc HEAD mismatch) cleanly closed by mechanical edits. The refinement was previously reviewed for 2 cycles with materially deeper findings; plan-review cycle 1 surfaces shallower findings, consistent with the refinement having de-risked the architectural decisions.

**Plan READY for Step 1 (sprint open)**. Final task count: 13 (TASK-3100–3112; TASK-3105/3106 fork resolved to seeder route).
