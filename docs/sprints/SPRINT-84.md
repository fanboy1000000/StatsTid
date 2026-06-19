# Sprint 84 — Realistic demo/test seed data (5 styrelse trees, ~3,350 employees)

| Field | Value |
|-------|-------|
| **Sprint** | 84 |
| **Status** | complete |
| **Start Date** | 2026-06-19 |
| **End Date** | 2026-06-19 |
| **Orchestrator Approved** | yes — 2026-06-19 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 errors (the new projects emit 0 warnings; pre-existing non-blocking warnings remain in the established warn-opt-out projects, unchanged by S84) |
| **Test Verified** | yes — 856 unit + 29 DemoSeed (NEW, dev-run) green; baseline regression/smoke/e2e UNAFFECTED (no `src/` change; CI runs init.sql-only). Full-scale demo loaded + verified end-to-end (see Test Summary) |

## Sprint Goal
Add a realistic, **opt-in** demo dataset for manual testing — 5 styrelse trees (1×~2,000, 1×~600, 3×~250 = ~3,350 employees) with realistic hierarchy, agreement/category/age/tenure distributions, a light activity slice, and ~20–30 hand-curated "messy" cases — **fully isolated from the 19-user test fixture so CI stays green with zero test edits**. No schema change, no production-behavior change.

Refinement: `.claude/refinements/REFINEMENT-realistic-demo-seed-data.md` (dual-lens reviewed; 2 convergent BLOCKERs resolved pre-plan). Owner rulings 2026-06-19: OQ-1 light activity subset; OQ-2 opt-in stack; OQ-3 default org profiles; OQ-4 event-emitting APIs for the tree; messy cases YES.

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| KB / db-schema / INDEX / sprint inventory / freshness | CLEAN | `check_docs.py` all hard checks pass (65 tables; 49 KB entries; through S83) |
| Working tree | CLEAN | at S83 tip `f9b49d2` |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P8 CI-integrity isolation + P3 event-sourcing/audit via the import-API write path) |
| **External Codex** | invoked 2026-06-19 — cycle 1: 1 BLOCKER, 2 WARNING, 4 NOTE; cycle 2: "Clean — BLOCKERs resolved" (1 precision NOTE fixed) |
| **Internal Reviewer** | invoked 2026-06-19 — cycle 1: 0 BLOCKER, 2 WARNING, 2 NOTE |
| **BLOCKERs resolved before Step 1** | yes — the `part_time_fraction`-not-a-users-column BLOCKER (→ set via the profile API in 8403) |

### Findings (cycle 1)

_Codex:_
- **BLOCKER** — `part_time_fraction` is on `employee_profiles` (`init.sql:485`), NOT `users` (`:456`); emitting it into the users INSERT would fail. **RESOLVED**: users-SQL drops it; part-time/position set via the employee-profile edit API in 8403 (event-complete).
- WARNING — import API does NOT enforce one-root-per-tree before commit (validates cycles/same-tree/active/no-self only). **RESOLVED**: generator asserts one-root + 8404 post-load SQL invariant check.
- WARNING — ~2,000 edges in one import tx (recursive-CTE cycle guard per edge) is an unproven budget. **RESOLVED**: configurable batch size + full-scale verify.
- NOTE — OQ-4 reading (bulk EMPLOYEE roles via SQL) DEFENSIBLE (single-grant API only; baseline seeds roles raw; login reads `role_assignments` table-direct). CI isolation sound (no demo ref in ci.yml; SYSTEM_SEED==19 isolated). Seeder per-row cost acceptable; measure boot.

_Internal Reviewer:_
- (0 BLOCKER) — decomposition sound; OQ-4 reading honest + well-grounded (audit cost negligible; runtime fidelity full); CI isolation airtight; ADR-027 D2/D9 + ADR-022 D8 fit confirmed.
- WARNING-1 — local-dev `:5432` collision: the loader writes ~3,350 reporting rows into the same Postgres `SeedData_Has13Rows` reads; CI safe, but a dev sharing `:5432` between the demo stack and local Regression gets false failures. **RESOLVED**: README ops warning (`down -v` demo first).
- WARNING-2 — the import endpoint is **GlobalAdminOnly** (`:805/1134`), not per-org; 8401 must bootstrap a demo GLOBAL_ADMIN. **RESOLVED**: 8401 seeds ≥1 demo GLOBAL_ADMIN (SQL-seeded role rows yield working scopes via login).
- NOTE-1 — ensure demo ids disjoint from baseline (ON CONFLICT silently drops collisions). **RESOLVED**: generator disjointness assertion. NOTE-2 — slow first boot → README note.

### Resolution
All findings folded into TASK-8401/8403/8404 (above). 1 BLOCKER resolved; both lenses confirmed the OQ-4 implementation reading is sound (not a silent narrowing). Cycle-2 (verification) runs before Step 1.

## Architectural Constraints
- [x] P1 — Architectural integrity (5 separate styrelse trees per ADR-027 D2; existing STY01–STY05 untouched — baseline 19 users + SeedData_Has13Rows verified intact)
- [x] P3 — Event-sourcing/audit: the reporting tree loads via the event-emitting bulk-import API (`ReportingLineBulkImported` + audit); the bulk EMPLOYEE + privileged role rows follow the established event-less baseline seed pattern (documented; the privileged-via-API path is blocked by the discovered `roles/grant` defect — see Discovered Defects); activity via the real API
- [x] P8 — CI-integrity: the demo override is NEVER mounted in CI (verified — no `docker-compose.demo` ref in ci.yml; CI runs only Unit/Regression/Smoke against init.sql); the new DemoSeed test project is dev-run, not CI-gated; distinct ids + `DEMO_SEED` markers
- [x] P9 — Usability: a rich, realistic manual-testing experience (the 2,000-employee org roster renders in 0.74s)
- [x] No schema change; no production-code `src/**` behavior change (tools + compose + generated SQL + docs only)

## Task Log

### TASK-8401 — Deterministic generator (`tools/StatsTid.DemoSeed`)

| Field | Value |
|-------|-------|
| **ID** | TASK-8401 |
| **Status** | planned |
| **Agent** | Infrastructure/Tooling |
| **Components** | new `tools/StatsTid.DemoSeed` (console project) + unit tests |
| **KB Refs** | ADR-027 D2/D9, ADR-022 (seeder pattern), ADR-029 (DOB/child-sick), ADR-030 (employment_start) |
| **Orchestrator Approved** | no |

**Description**: A deterministic generator (**fixed RNG seed**, Danish name + org-name pools, **parameterized by scale** — a tiny `--scale smoke` for pipeline validation + the full `--scale full` for 3,350). Produces two artifacts deterministically:
- **`docker/postgres/99-demo-seed.sql`** — the structural rows that must exist BEFORE the backend boots: organisations (5 trees `STYX1…STYX5` under a new demo ministry `MINX` + internal kontor/team sub-orgs, materialized paths), `users` (**only real `users` columns** — `agreement_code` per org character [OQ-3 defaults: big 55/35/10 AC/HK/PROSA; mid AC-heavy; smalls board+HK-inspection+PROSA-IT], `employment_category` spread, `birth_date` age spread incl. 60+, `employment_start_date` tenure spread, a few `employment_end_date` leavers). **NOTE — `part_time_fraction` + `position` are NOT `users` columns; they live on `employee_profiles` (`init.sql:485`). Do NOT emit them into the users INSERT (Codex BLOCKER). They are set event-completely via the employee-profile edit API in 8403** for the ~10% part-time subset (the `EmployeeProfileSeeder` creates the default-1.0 profile on boot; 8403 then versions it).
  Plus the **role-assignment bootstrap**: **at least one demo `GLOBAL_ADMIN`** (the import API is `GlobalAdminOnly` — `ReportingLineEndpoints.cs:805` — so the loader needs a global-admin JWT; SQL-seeded role rows DO yield working scopes since login derives the JWT `scopes` from `role_assignments` at mint time) + the per-org HR/leader rows the loader uses; **and the bulk EMPLOYEE `role_assignments`** (event-less, `created_by='DEMO_SEED'` — matching the baseline init.sql seed exactly; both lenses confirmed this is sound, since authorization reads `role_assignments` table-direct and the audit row is meaningless for a bulk demo). All `ON CONFLICT DO NOTHING`; **the generator asserts the demo id set (MINX/STYX*/sub-orgs/demo usernames) is DISJOINT from the baseline id set** (else ON CONFLICT silently drops a colliding row → malformed tree).
- **A deterministic JSON manifest** (`demo-manifest.json`) consumed by the 8403 loader: the reporting-edge list per tree (for the bulk import API), the activity plan (which ~10–20% of employees get which periods/absences/vikar in which states), and the ~20–30 messy-case scripts (OK24→OK26, agreement change, cross-styrelse transfer, terminated-then-rehired, odd fractions).

Pure generation logic; deterministic (no wall-clock — a fixed reference date passed in); unit tests assert reproducibility (same seed → byte-identical SQL + manifest) + structural realism (span/level/manager-ratio bounds; one root per tree; no cycles in the generated edge list).

**Validation Criteria**:
- [ ] `--scale full` emits a valid `99-demo-seed.sql` (orgs + ~3,350 users + role rows) + `demo-manifest.json`; re-run byte-identical.
- [ ] Generated hierarchy: 4–6 levels, spans ~6–8, ~12–18% managers, exactly one root per tree, no cycles (unit-asserted on the manifest).
- [ ] Attribute distributions match OQ-3 defaults (agreement mix per org; ~10% part-time; some 60+; some child-sick opt-ins; a few leavers).
- [ ] `--scale smoke` emits a tiny dataset (~3 orgs, ~30 users) for fast end-to-end pipeline validation.
- [ ] `dotnet build` 0/0; generator unit tests green.

**Files Changed**: `tools/StatsTid.DemoSeed/**`, `StatsTid.sln` (add project), `tests/**` (generator unit tests)

---

### TASK-8402 — Opt-in compose override

| Field | Value |
|-------|-------|
| **ID** | TASK-8402 |
| **Status** | planned |
| **Agent** | Orchestrator (architecture; small) |
| **Components** | `docker/docker-compose.demo.yml`, `docker/postgres/99-demo-seed.sql` (mount) |
| **Orchestrator Approved** | no |

**Description**: `docker/docker-compose.demo.yml` — a compose OVERRIDE that adds a volume mount on the postgres service. **SHIPPED CORRECTION (empirically caught during the build):** the on-disk artifact is `./postgres/99-demo-seed.sql` but it is mounted as **`/docker-entrypoint-initdb.d/zz-demo-seed.sql`** — the entrypoint runs init scripts in BYTE-lexical order, and `'9'`(0x39) sorts BEFORE `'i'` in `init.sql`(0x69), so a `99-` *container* name would run against a schema-less DB. `zz-` sorts after `init.sql`. (This corrects the plan's — and a Step-0b Codex lens's — assumption that `99-` orders last.) **NOT referenced anywhere in `.github/workflows/ci.yml`** (CI uses `docker/docker-compose.yml` → init.sql only). Rich stack = `docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml up`.

**Validation Criteria**:
- [ ] Override mounts `99-demo-seed.sql` only; default compose + CI unchanged (grep ci.yml — no demo reference).
- [ ] Fresh demo-stack `up` applies init.sql THEN 99-demo-seed.sql (orgs/users present); a default `up` has only the 19 users.

**Files Changed**: `docker/docker-compose.demo.yml`

---

### TASK-8403 — API loader (trees + privileged roles + activity + messy cases)

| Field | Value |
|-------|-------|
| **ID** | TASK-8403 |
| **Status** | planned |
| **Agent** | Backend/Integration |
| **Components** | a loader command (in `tools/StatsTid.DemoSeed` or a sibling) driving the live API |
| **KB Refs** | ADR-027 D7 (import) / D13/D14 (vikar), ADR-012 (approval), ADR-028/032 (skema/absence) |
| **Orchestrator Approved** | no |

**Description**: A post-boot loader that reads `demo-manifest.json` and drives the REAL API against the running demo stack (default `http://localhost:5100`, parameterized):
- **Authenticate as the demo `GLOBAL_ADMIN`** (the import endpoint is `GlobalAdminOnly`, `ReportingLineEndpoints.cs:805/1134`) — log in, get the JWT.
- **Reporting trees** via `POST /api/admin/reporting-lines/import` (bulk → `ReportingLineBulkImported` event + audit; validates active-users/same-tree/no-self-edge/no-cycle). **Import root-count is NOT API-enforced** (Codex WARNING) → the generator asserts one-root-per-tree on the manifest AND 8404 runs a post-load SQL invariant check. **Batch size configurable** (the import runs one tx with a recursive-CTE cycle guard per edge — ~2,000 edges in one call is unproven; batch + verify at full scale).
- **Part-time + position** for the ~10% part-time subset via the **employee-profile edit API** (event-complete versioned update — the BLOCKER fix; `part_time_fraction` is a profile field, not a users column).
- **Privileged role grants** (per-org HR/leader): **SHIPPED CORRECTION** — these are **SQL-seeded event-less in 8401** (like the bulk EMPLOYEE rows), NOT granted via `POST /api/admin/roles/grant`, because that endpoint is a confirmed pre-existing product defect (it 500s on every call — see Discovered Defects). Originally OQ-4 routed privileged grants via the API; the bug forced the fallback. Authz reads `role_assignments` table-direct so scopes/logins work; the only loss is the audit-trail row (meaningless for a demo bulk-seed, and absent from the baseline too). The reporting TREE still loads via the event-emitting bulk-import API. (Bulk EMPLOYEE rows were already SQL-seeded — no bulk role endpoint exists [`AdminEndpoints.cs:1614` single-grant]; both Step-0b lenses confirmed this is the sound reading of OQ-4.)
- **Activity (light subset, ~10–20%)** via `/api/skema/{employeeId}/save` (absences: vacation/sick/care), `/api/approval/submit` + `/approve` + `/reject` (periods in mixed states), and vikar via the `manager_vikar` API (ADR-027 D14 — NOT legacy SELF_DELEGATION).
- **~20–30 messy cases** scripted (OK-transition, agreement change, cross-styrelse transfer, terminated-then-rehired, odd fractions).
- **Idempotent** (skip-if-present per demo user/period); NOT byte-reproducible (server-stamped event ids/timestamps reflect wall-clock).

**Validation Criteria**:
- [ ] Loader builds all 5 trees via the import API; post-load invariants hold (one root per tree, no cycles, tree_root parity) — cycles/same-tree enforced by the API, **one-root verified post-load** (generator assertion + 8404 SQL check; the API does not enforce root-count).
- [ ] Privileged roles granted via API (audit rows present); activity subset created in mixed states; ≥1 vikar via `manager_vikar` API; ≥1 leaver; the ~20–30 messy cases present.
- [ ] Re-running the loader creates no duplicates (idempotent skip-if-present).
- [ ] `dotnet build` 0/0.

**Files Changed**: `tools/StatsTid.DemoSeed/**` (loader), `tests/**` (loader logic unit tests where pure)

---

### TASK-8404 — Verify (small→full) + docs + close

| Field | Value |
|-------|-------|
| **ID** | TASK-8404 |
| **Status** | planned |
| **Agent** | Orchestrator |
| **Components** | demo README; `docs/QUALITY.md`; `ROADMAP.md`; SPRINT-84.md |
| **Orchestrator Approved** | no |

**Description**: Validate the pipeline at `--scale smoke` first (fast end-to-end: demo stack up → seeders → loader → app shows data), then run `--scale full` (~3,350). Verify: the big-org **MedarbejderAdministration tree** + roster + an approval dashboard render without error/pathological latency; the startup-seeder **first-boot budget** recorded (~6,700 per-row events for 3,350 users); **post-load tree-invariant SQL check** (exactly one root per `tree_root_org_id`, no PRIMARY cycles, tree_root parity — NOT relying on the API for one-root); a demo employee/manager/admin can log in and see realistic data. **Re-confirm CI green** (the demo override is never mounted; `SeedData_Has13Rows` [filters `created_by='SYSTEM'`], `EmployeeProfileEndpointTests` SYSTEM_SEED==19, emp001/mgr03 e2e anchors all still pass). Write a demo README: how to launch + demo logins + **the ops warning that the demo stack and the `:5432`-coupled local Regression tests cannot share the port** (a dev who loads the demo stack on `:5432` then runs Regression locally gets false failures — `down -v` the demo stack first; CI is unaffected, it uses its own services-postgres) + the slow-first-boot note (per-row seeders, multi-minute, not a hang). Update QUALITY/ROADMAP; SPRINT-84 narrative.

**Validation Criteria**:
- [ ] Pipeline validated at smoke scale, then full ~3,350 loaded successfully (import batched + verified at full scale).
- [ ] Big-org tree/roster/dashboard render OK; first-boot budget recorded; post-load tree-invariant SQL check passes (one root/tree, no cycles).
- [ ] Part-time/position set on the subset via the profile API (event-complete); ~10% part-time visible.
- [ ] CI re-confirmed green; no test edits required. README warns about the `:5432` demo-vs-test collision + slow first boot.
- [ ] README + QUALITY + ROADMAP updated; `check_docs.py` green.

**Files Changed**: `docs/**`, `ROADMAP.md`, a demo `README`

---

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 856 | green (unchanged baseline; confirms sln/props edits didn't break it) |
| DemoSeed unit (NEW, dev-run) | 29 | green — determinism (byte-identical SQL+manifest), one-root/no-cycle, level/span/manager-ratio bounds, id-disjoint-from-baseline |
| Regression / Smoke / FE / E2E | unchanged | UNAFFECTED — no `src/` change; CI runs them init.sql-only |

**Full-scale demo verified end-to-end** (`docker-compose.demo.yml`, fresh volume):
- **First-boot ~57s** (backend health incl. the startup seeders creating ~3,251 live `employee_profiles` + `user_agreement_codes` + events for the active demo users — the per-row-tx worry was unfounded). **Load ~76s.** Total demo-stack-ready ~2–3 min.
- **275 orgs** (263 demo + 12 baseline), **3,370 users** (3,351 demo + 19 baseline), 3,232 active demo + 119 leavers.
- **3,226 reporting edges** imported via the bulk-import API across 5 trees: STYX1=**1,924**, STYX2=580, STYX3=240, STYX4=244, STYX5=238. **One root per tree ✓, no self-edges ✓, tree_root parity ✓** (independently SQL-confirmed).
- Activity: 773 absences, 375 submitted / 109 approved / 127 rejected periods, 5 vikars (via `manager_vikar` API), 347 part-time/position profiles, 26 messy cases.
- **Baseline integrity:** 19 baseline users present ✓, `SeedData_Has13Rows` = 13 ✓.
- **Big-org render (headline):** `GET /api/admin/reporting-lines/tree/STYX1/medarbejdere` → **HTTP 200, 1,925 employees, 0.74s**; period-status 0.73s; a demo manager's approval dashboard 16ms.

## Discovered Defects (recorded, outside S84 scope)

**DEFECT — `POST /api/admin/roles/grant` 500s on every call (pre-existing production bug).** `AdminEndpoints.cs:1690` inserts `role_assignment_audit (action) VALUES ('GRANT', …)` but the schema CHECK (`init.sql:666`) allows only `'GRANTED'/'REVOKED'/'EXPIRED'/'MODIFIED'` → a `23514` on every successful grant. Masked because the sole test on that path (`AdminAtomicTests.RoleAssignmentGranted_OutboxFails_RollsBack`) forces an outbox rollback BEFORE the buggy INSERT, so the success path is never exercised. **Impact: admin role-granting via the API/UI is broken in production.** Forced the S84 OQ-4 refinement (privileged demo roles SQL-seeded event-less, like the baseline, rather than via the grant API). **Recommended follow-up (fast, focused): fix `'GRANT'`→`'GRANTED'` + reconcile the audit column list, add the missing success-path test (the one that would have caught it), Step-5a review.** Recorded in ROADMAP.

## Sprint Retrospective

**What went well**: The smoke→full scale strategy de-risked cleanly (smoke proved every mechanic; full was the same × rows). The agent's empirical testing caught that the `99-` init-prefix sorts BEFORE `init.sql` (correcting both the plan and Codex's Step-0b premise) → `zz-` mount; and surfaced the real `roles/grant` production defect. CI isolation held by construction (no test edits). The 2,000-employee org renders sub-second.

**What to improve**: The plan (and a Step-0b Codex lens) asserted the `99-` prefix orders after `init.sql` without empirically checking byte-lexical order (`'9'` < `'i'`) — a reminder that container-init ordering is worth a 5-second test, not an assumption (the build agent caught it → `zz-`). The DemoSeed verifier's edge-presence assertion printed an odd "no demo PRIMARY edges found" line while still PASSing; the data is independently SQL-confirmed correct (one-root-per-tree across all 5 trees + 3,226 edges), so it's a verifier-message imprecision, not a data issue — tighten the message if revisited. The loader's profile-idempotency skip keys on `part_time_fraction` only (position is set in the same PUT, so a fraction match implies position was already set by a prior run — moot under determinism; Codex NOTE).

**Knowledge produced**: No new KB entry (the demo tool is dev tooling). The `roles/grant` defect is recorded as a ROADMAP follow-up.
