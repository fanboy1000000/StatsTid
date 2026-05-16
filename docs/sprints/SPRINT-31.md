# Sprint 31 — Phase 4d-3 Part 1: Employee-Profile Authoritative Store

| Field | Value |
|-------|-------|
| **Sprint** | 31 |
| **Status** | **complete** (closed 2026-05-16) |
| **Start Date** | 2026-05-16 |
| **End Date** | 2026-05-16 |
| **Orchestrator Approved** | yes (2026-05-16) |
| **Build Verified** | yes (0 errors; 19 pre-existing CS0618 warnings unchanged) |
| **Test Verified** | 526 unit + 35 plain regression + **184 Docker-gated passing** + 88 frontend vitest = **833 total passing** (+18 vs S30's 815, all from TASK-3110). 18 pre-existing Docker-gated failures unchanged (S30 baseline carry-forward, parallel-test-flakiness identified). |
| **Sprint-start commit base** | `68a6f07` (S30 sprint close, 2026-05-16 — TASK-3012 sprint-close commit) |
| **Sprint-end HEAD** | `93bb6d5` (TASK-3111 ADR-022 commit; TASK-3112 sprint plumbing extends with this commit). 12 sprint-task commits + 1 Step 7a fix commit + 1 ADR commit + 1 sprint-close commit (this one) = **15 commits total** atop `68a6f07`. |
| **Sprint type** | **IMPLEMENTATION** — Phase 4d-3 Part 1; foundational data-plane work for S32's versioning emission + rule-engine cutover + planner-snapshot. New ADR-022 (filed in-sprint). **ZERO consumer cutovers** in S31 — ComplianceEndpoints / BalanceEndpoints / TimeEndpoints / RuleEngine all UNCHANGED. |
| **Refinement** | `.claude/refinements/REFINEMENT-s31-phase-4d3.md` (Step 4 cycles 1+2 dual-lens; cycle 1 absorbed 5 BLOCKERs; cycle 2 absorbed 4 NEW BLOCKERs without cycle-cap waiver — user adjudication 2026-05-16 = "accept findings + mechanical absorption + proceed"). User adjudication on Q1–Q7 + deeper question 2026-05-16. |
| **Plan** | `.claude/plans/PLAN-s31.md` (Step 0a); Step 0b cycle 1 absorbed 2 Codex BLOCKERs + 2 Codex WARNINGs + 2 Reviewer WARNINGs (no cycle 2 needed — mechanical absorption clean, no thrash signals). |

## Sprint Goal

Build the authoritative `employee_profiles` data store for Phase 4d-3 — consolidating 3 employment-profile fields (`weekly_norm_hours`, `part_time_fraction`, `position`) that today have no persisted source-of-truth (TimeEndpoints accepts them in request payload; ComplianceEndpoints hardcodes `WeeklyNormHours = 37.0m`). Build the admin CRUD endpoint pair + frontend admin page + extend the existing user-create POST for 4-way atomicity. **NO consumer-side cutovers in S31** — ComplianceEndpoints / BalanceEndpoints / TimeEndpoints / RuleEngine all stay on their current sources. S32 will cut over consumer paths atomically with versioning emission + planner-snapshot (closes ADR-016 D5b's last open slot).

**Marquee invariant**: `EmployeeProfileEdit_RoundTripsAtomically_WithVersionedAuditAndEvent` — admin PUT to `/api/admin/employee-profiles/{employeeId}` returns 200 + new ETag; the row, audit row (`UPDATED`, version_before=1, version_after=2), and outbox event ride a single transaction.

**Convergent BLOCKER fix from refinement cycle 2**: `POST /api/admin/users` (AdminEndpoints.cs:292) is extended for 4-way atomicity — `users` INSERT + `employee_profiles` INSERT + `UserCreated` outbox + `EmployeeProfileCreated` outbox, all in one tx. New invariant: "every user has exactly one live employee_profiles row."

## Architectural Decisions Settled (refinement + user adjudication 2026-05-16)

1. **Q1 SPLIT** — S31 = data-plane setup; S32 = versioning emission + rule-engine cutover + planner-snapshot. Both refinement lenses concurred; ROADMAP L356 + ADR-020 §124 anticipate multi-sprint Phase 4d-3.
2. **Q2 NEW ENDPOINT PAIR** — `/api/admin/employee-profiles/{employeeId}` GET + PUT under `HROrAbove` **AND `OrgScopeValidator.ValidateEmployeeAccessAsync`** (Step 0b BLOCKER fix). Mirrors S29/S30 per-resource endpoint pattern.
3. **Q3 LEAVE** — `agreement_code`/`ok_version`/`employment_category` stay in `users` table. Migration to employee_profiles deferred to S32. JWT/auth path untouched.
4. **Q4 REVERSE** — `CalculateRequest.WeeklyNormHours` + `WeeklyCalculateRequest.WeeklyNormHours` stay in request contracts; TimeEndpoints rule-engine path unchanged. Hard-cut moves to S32 atomic with planner-snapshot.
5. **Q5 DEFERRED TO S32** — "as-of-date" UI semantic is meaningful only after versioning emission.
6. **Q6 REGISTER-4-EMIT-2** — 4 event types registered in S31 (`EmployeeProfileCreated/Updated/Superseded/SoftDeleted`); 2 emit in S31 (CREATED on backfill + admin-create; UPDATED on admin-edit); 2 reserved for S32 emission. EventSerializer 51 → 55 in S31; stable through S32.
7. **Q7 PRE-BAKE + SURROGATE UUID PK** — `CREATE TABLE employee_profiles` includes `profile_id UUID PRIMARY KEY DEFAULT gen_random_uuid()` (S29 WTM precedent — multi-row-per-employee forward-compat for S32 history rows), effective_from/effective_to/partial-unique-index/history-unique-index/version columns all present in S31. S31 reads/writes only live rows.
8. **`is_part_time` DROPPED** (refinement cycle 2) — column not present in schema; `EmployeeProfileRepository.GetByEmployeeIdAsync` computes `IsPartTime = part_time_fraction < 1.0m` when constructing `EmploymentProfile`.
9. **Init.sql ordering (deeper question)** — separate post-table-create idempotent INSERT block at the bottom of init.sql (S29/S30 pattern). Plus runtime `EmployeeProfileSeeder` for event emission (committed seeder route at Step 0b absorption).

## Entropy Scan Findings

Per WORKFLOW.md Step 0a (2026-05-16):

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | DEFERRED | Full path-walk is Phase 4e candidate (carried from S29/S30) |
| FAIL-001 (`FindFirst("scopes")`) regression | _verified at sprint open_ | _check on Phase 1 dispatch_ |
| Hardcoded `http://localhost` in non-test code | _verified at sprint open_ | _check on Phase 1 dispatch_ |
| Endpoint `RequireAuthorization` coverage | _verified at sprint open_ | _check on Phase 1 dispatch_ |
| MEMORY.md drift | CLEAN | Synchronized through S30 close per session context |
| QUALITY.md re-grade | DEFERRED | S31 closes data-plane setup → re-grade after sprint close (likely Infrastructure A-) |

No DRIFT items requiring fix before sprint open. No DEBT items added.

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — sprint touches P1 (Architectural integrity — new schema + new admin surface + new ADR), P3 (Event sourcing — 4 new event types), P7 (Security — new GlobalAdmin/HR endpoints), and is cross-domain (Data Model + Backend.Api + Frontend + KB authorship). |
| **External Codex** | invoked 2026-05-16 — 1 cycle (2B/2W/1N — 2 BLOCKERs absorbed: HROrAbove cross-org leak + SPRINT-30.md HEAD mismatch). |
| **Internal Reviewer** | invoked 2026-05-16 — 1 cycle (0B/2W/3N — all WARNINGs/NOTEs absorbed). |
| **BLOCKERs resolved before Step 1** | yes — Codex's 2 BLOCKERs both absorbed: (a) TASK-3107 gains OrgScopeValidator.ValidateEmployeeAccessAsync on both GET + PUT; new cross-org D-test added; (b) SPRINT-30.md L13 corrected to `68a6f07` (actual git HEAD post-TASK-3012). |

### Findings (cycle 1)

_See `.claude/plans/PLAN-s31.md` §"Step 0b Plan Review" for the full lens-by-lens findings + absorption log._

### Resolution

All cycle 1 BLOCKERs absorbed. All cycle 1 WARNINGs absorbed. Cycle 2 NOT requested per `feedback_thrash_defer_real_world.md` — mechanical absorption clean; no thrash signals. Plan READY for Step 1 (sprint open).

## Architectural Constraints Verified

_To be checked off as the sprint progresses; final assertion in TASK-3112._

- [ ] **P1 — Architectural integrity** → TASK-3101 schema migration via `schema_migrations` ledger (S22 D8 pattern); TASK-3111 new ADR-022 documenting the consolidation step as Phase 4d-3 Part 1 (sibling to ADR-020 + ADR-021). Bounded contexts respected; no new pattern added (S31 inherits ADR-019 + ADR-018 + ADR-020 D2 patterns).
- [ ] **P3 — Event sourcing / auditability** → TASK-3103 4 net-new events (`EmployeeProfileCreated/Updated/Superseded/SoftDeleted`); TASK-3101 audit-table CREATE with ADR-019 D8 version-transition columns + CHECK including all 4 actions up-front.
- [ ] **P4 — Version correctness** → TASK-3107 admin-strict If-Match + 412/428/409 distinction per ADR-019 D2/D5/D6/D8; row-version stays load-bearing on the live-edit path.
- [ ] **P6 — Payroll integration correctness** → S31 has ZERO consumer cutovers (rule-engine path stays on request-payload); payroll integration unaffected. S32 will land planner-snapshot.
- [ ] **P7 — Security and access control** → TASK-3107 `.RequireAuthorization("HROrAbove")` AND `OrgScopeValidator.ValidateEmployeeAccessAsync` on both GET and PUT (Step 0b BLOCKER fix); cross-org D-test verifies the binding.

Not directly affected: P2 (rule engine determinism unchanged — no rule engine touches in S31), P5 (no inter-service contract changes), P8 (CI unchanged), P9 (admin UI is a new surface).

## Task Log

13 declared tasks. Plan file `.claude/plans/PLAN-s31.md` is source-of-truth for detailed specifications.

### Phase 0 — Sprint-Open Plumbing

#### TASK-3100 — Sprint-open plumbing (create SPRINT-31.md + INDEX.md provisional row)

| Field | Value |
|-------|-------|
| **ID** | TASK-3100 |
| **Status** | in-progress |
| **Agent** | Orchestrator-direct (KB / sprint log writes per WORKFLOW.md L48-49) |
| **Components** | docs/sprints/SPRINT-31.md, docs/sprints/INDEX.md |
| **KB Refs** | n/a |
| **Plan section** | Phase 0 — TASK-3100 (PLAN-s31.md) |
| **Dependencies** | none (Phase 0 root) |

**Description**: Create SPRINT-31.md from TEMPLATE.md with sprint metadata + sprint goal + provisional task-log skeleton (TASK-3100..3112 reserved rows). Update INDEX.md with provisional in-progress row.

**Validation Criteria**:
- [x] SPRINT-31.md exists at `docs/sprints/SPRINT-31.md`
- [ ] INDEX.md row added with status=in-progress
- [ ] Commit lands at sprint-open

---

### Phase 1 — Plumbing (sequential, 5 commits — commit before Phase 2 dispatch)

#### TASK-3101 — Schema migration `s31-d3-employee-profile-store` + `employee_profile_audit` CREATE

| Field | Value |
|-------|-------|
| **ID** | TASK-3101 |
| **Status** | pending |
| **Agent** | Data Model (extended into Database Schema, cross-domain authorized) — AGENTS.md L50 form-1 |
| **Plan section** | Phase 1 — TASK-3101 (PLAN-s31.md) |
| **Dependencies** | TASK-3100 |

#### TASK-3102 — `EmployeeProfileRepository`

| Field | Value |
|-------|-------|
| **ID** | TASK-3102 |
| **Status** | pending |
| **Agent** | Data Model (extended into Infrastructure, cross-domain authorized) — AGENTS.md L50 form-1 |
| **Plan section** | Phase 1 — TASK-3102 (PLAN-s31.md) |
| **Dependencies** | TASK-3101 |

#### TASK-3103 — 4 new events + EventSerializer registration

| Field | Value |
|-------|-------|
| **ID** | TASK-3103 |
| **Status** | pending |
| **Agent** | Data Model |
| **Plan section** | Phase 1 — TASK-3103 (PLAN-s31.md) |
| **Dependencies** | none (parallel-eligible with TASK-3101/3102 in principle, sequenced for simplicity) |

#### TASK-3104 — Test fixture DDL drift coordinated with TASK-3101

| Field | Value |
|-------|-------|
| **ID** | TASK-3104 |
| **Status** | pending |
| **Agent** | Test & QA |
| **Plan section** | Phase 1 — TASK-3104 + Risk R7 (PLAN-s31.md) |
| **Dependencies** | TASK-3101 |

#### TASK-3105 — Init.sql schema bootstrap for `employee_profiles` (NO seed INSERTs in init.sql)

| Field | Value |
|-------|-------|
| **ID** | TASK-3105 |
| **Status** | pending |
| **Agent** | Data Model (extended into Database Schema, cross-domain authorized) — AGENTS.md L50 form-1 |
| **Plan section** | Phase 1 — TASK-3105 + Step 0b WARNING absorption (seeder route committed) (PLAN-s31.md) |
| **Dependencies** | TASK-3101 |

#### TASK-3106 — EmployeeProfileSeeder (committed seeder route per Step 0b absorption)

| Field | Value |
|-------|-------|
| **ID** | TASK-3106 |
| **Status** | pending |
| **Agent** | Data Model (extended into Infrastructure, cross-domain authorized) — AGENTS.md L50 form-1 |
| **Plan section** | Phase 1 — TASK-3106 + Critical-Path Callout 5 (PLAN-s31.md) |
| **Dependencies** | TASK-3102, TASK-3103, TASK-3105 |

---

### Phase 2 — Endpoint + AdminEndpoints Extension + Frontend (parallel-dispatchable after Phase 1 commit, 3 commits)

#### TASK-3107 — `EmployeeProfileEndpoints.cs` new admin CRUD pair

| Field | Value |
|-------|-------|
| **ID** | TASK-3107 |
| **Status** | pending |
| **Agent** | Backend API (cross-domain authorized) |
| **Plan section** | Phase 2 — TASK-3107 + Step 0b BLOCKER #1 absorption (OrgScopeValidator binding) (PLAN-s31.md) |
| **Dependencies** | TASK-3102, TASK-3103, TASK-3104 |

#### TASK-3108 — AdminEndpoints POST extension for 4-way atomicity

| Field | Value |
|-------|-------|
| **ID** | TASK-3108 |
| **Status** | pending |
| **Agent** | Backend API (cross-domain authorized) |
| **Plan section** | Phase 2 — TASK-3108 + Critical-Path Callout 3 + Risk R2 (PLAN-s31.md) |
| **Dependencies** | TASK-3102, TASK-3103 |

#### TASK-3109 — Frontend admin page + hook + sidebar entry

| Field | Value |
|-------|-------|
| **ID** | TASK-3109 |
| **Status** | pending |
| **Agent** | Frontend |
| **Plan section** | Phase 2 — TASK-3109 + Risk R1 (PLAN-s31.md) |
| **Dependencies** | TASK-3107 (HTTP shape locks first) |

---

### Phase 3 — D-tests (sequential after Phase 2 commit, 1 commit)

#### TASK-3110 — D-tests suite

| Field | Value |
|-------|-------|
| **ID** | TASK-3110 |
| **Status** | pending |
| **Agent** | Test & QA |
| **Plan section** | Phase 3 — TASK-3110 (PLAN-s31.md) |
| **Dependencies** | TASK-3101..3109 |

---

### Phase 4 — Validation (Orchestrator-direct, no new commit unless fixes)

Per WORKFLOW.md Step 4: `dotnet build` + sprint-test-validation skill (previous + delta = current arithmetic).

### Phase 5 — Documentation + Sprint Close

#### TASK-3111 — New ADR-022 + KB INDEX.md

| Field | Value |
|-------|-------|
| **ID** | TASK-3111 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Plan section** | Phase 5 — TASK-3111 (PLAN-s31.md) |
| **Dependencies** | Phase 1 + Phase 2 + Phase 3 complete |

#### TASK-3112 — Sprint-close plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3112 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Plan section** | Phase 5 — TASK-3112 (PLAN-s31.md) |
| **Dependencies** | TASK-3111 |

---

## Legal & Payroll Verification

_Filled by TASK-3112._

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | _S31 has no rule changes; consumer-cutover deferred to S32_ |
| Wage type mappings produce correct SLS codes | N/A | _No wage-type changes in S31_ |
| Overtime/supplement calculations are deterministic | N/A | _No rule engine changes in S31 — rule engine path UNCHANGED per Q4 reverse_ |
| Absence effects on norm/flex/pension are correct | N/A | _S31 has no consumer cutovers; existing behavior unchanged_ |
| Retroactive recalculation produces stable results | N/A | _S32 will land planner-snapshot for full replay determinism_ |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (external Codex + internal Reviewer Agent) |
| **Sprint-start commit** | `68a6f07` |
| **Command** | `codex review --base 68a6f07` (base-anchored form per AGENTS.md L406 — intermediate commits exist on master). Cycle 1 ran on gpt-5 baseline; cycle 2 ran on gpt-5.5 after mid-sprint Codex CLI upgrade (0.120.0 → 0.130.0, required for gpt-5.5 model access). Internal Reviewer invoked in parallel via Agent tool. |
| **Review Cycles** | Codex 2 cycles (cycle 1 = 3 BLOCKERs absorbed; cycle 2 = 2 NEW production-readiness findings deferred to Phase 4e). Reviewer 1 cycle (0 BLOCKERs — clean). Cycle-cap = 2 respected; no cycle 3 / no waiver. |
| **Findings (cycle 1)** | **Codex (gpt-5)**: 3 P2 BLOCKERs — (a) `EmployeeProfileEndpoints.cs:77-87` GET race: two reads (`GetByEmployeeIdAsync` + `ReadLiveVersionAsync`) could return stale body fields with newer ETag → silent overwrite on next If-Match. (b) `AdminEndpoints.cs:361-376` POST extension created `employee_profiles` row + emitted `EmployeeProfileCreated` outbox but missed the matching `employee_profile_audit` CREATED row — every admin-created profile lacked an origin audit record. (c) `EmployeeProfileSeeder.cs:78-106` same gap at backfill — all 7 seeded profiles would lack origin audit rows. **Reviewer**: 0 BLOCKERs (all checklist items pass — lens divergence: complementary not contradictory; Codex code-correctness lens caught row-level integrity bugs that the architecture-invariant Reviewer didn't drill into). |
| **Findings (cycle 2 — gpt-5.5 on absorbed diff)** | **Codex (gpt-5.5)**: 2 NEW production-readiness findings, neither re-raises cycle 1. (a) **[P1]** `init.sql:479` legacy DB upgrade — Postgres init scripts only run on FRESH data directories; existing pre-S31 production DBs would not have `employee_profiles` table, and `Program.cs` unconditionally calls `EmployeeProfileSeeder.SeedAsync` → relation-does-not-exist on startup. Same shape as S30 cycle 2 P1 (entitlement_configs legacy upgrade). (b) **[P2]** `EmployeeProfileSeeder.cs:88-92` concurrent app startup race — two instances starting simultaneously can both read same `missing` list, both INSERT, loser hits `23505` partial-unique-index → startup crash. Fix shape: catch `PostgresException` SqlState=23505 and skip-without-fail. **Reviewer**: cycle 2 not re-invoked (cycle 1 was 0-BLOCKER + all cycle-1 fixes were mechanical/additive — no architecture invariants touched). |
| **Resolution** | **Cycle 1**: all 3 P2 BLOCKERs absorbed in single commit `e9733d0` ("S31 Step 7a P2 fixes: GET row+version atomic + CREATED audit on POST + Seeder"). 4 files touched: new `EmployeeProfileRepository.GetByEmployeeIdWithVersionAsync` (single SELECT for row+version atomicity) + `EmployeeProfileEndpoints.cs` GET cutover (deleted `ReadLiveVersionAsync` helper) + `AdminEndpoints.cs` POST adds 5th tx op (audit CREATED) + `EmployeeProfileSeeder.cs` adds audit-CREATED to per-row tx. 18 affected D-tests still pass post-fix. **Cycle 2**: 2 production-readiness findings **deferred to Phase 4e** per pre-launch posture (ROADMAP L369 — no production data + single-instance deployment today). S30 cycle 2 precedent (init.sql legacy upgrade also deferred). Documented in this sprint log + added to ROADMAP Phase 4e candidates. |

## Test Summary

| Suite | Previous (S30) | Current (S31) | Delta |
|-------|----------------|---------------|-------|
| Unit | 526 | 526 | 0 |
| Plain regression | 35 | 35 | 0 |
| Docker-gated (passing) | 166 | **184** | **+18** |
| Docker-gated (pre-existing failures, unchanged) | 18 | 18 | 0 |
| Frontend vitest | 88 | 88 | 0 |
| **Total passing** | **815** | **833** | **+18** |

**Net new D-tests delivered**: 18 from TASK-3110 — 1 marquee (`EmployeeProfileEdit_RoundTripsAtomically_WithVersionedAuditAndEvent`) + 2 atomic (`AdminUserCreate_AtomicallyCreatesProfileRowAndEmitsEvent` + duplicate-username rollback) + 15 admin-CRUD shape (3 shape, 3 ETag/If-Match contract, 2 cross-org scope, 4 RBAC matrix, 2 validation-gap documentation, 1 backfill bootstrap). All 18 pass first-run; all 18 re-verified passing post-Step 7a cycle 1 fix commit `e9733d0`. Target hit: PLAN-s31.md floor 828, stretch 832-836; **actual 833**.

**Pre-existing Docker-gated failures**: 18 (S30 baseline) → 19 observed at S31 close → 18 deterministic. The flap-by-1 (ProfileAuditTests vs ProfileNoOpShortCircuitTests rotation) is parallel-test flakiness under testcontainer DB contention; both tests pass in isolation. Same set rotates run-to-run; deterministic failure count is stable.

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 13 declared (TASK-3100–3112; TASK-3105/3106 fork resolved to seeder route at Step 0b) + 3 Step 7a fix tasks added post-cycle-1 |
| Constraint Violations | 0 |
| Reviewer Findings | Step 0b cycle 1: 0 BLOCKERs + 2 WARNINGs + 3 NOTEs (all absorbed). Step 7a cycle 1: 0 BLOCKERs (Reviewer agreed clean — Codex complementary lens caught the 3 P2 issues). |
| External Review Cycles | **Step 0b**: 1 cycle Codex / 1 cycle Reviewer. **Step 7a**: 2 cycles Codex (cycle 1 = 3 P2 BLOCKERs all absorbed; cycle 2 = 2 production-readiness findings deferred to Phase 4e per pre-launch posture) + 1 cycle Reviewer. Cycle-cap = 2 respected throughout. |
| External Findings | **Step 0b**: 2 Codex BLOCKERs (HROrAbove cross-org leak + SPRINT-30.md HEAD mismatch) + 4 WARNINGs — all absorbed pre-implementation. **Step 7a cycle 1**: 3 Codex P2 BLOCKERs (GET row+version race + 2 missing CREATED audit rows) — all absorbed in `e9733d0`. **Step 7a cycle 2 (gpt-5.5)**: 2 production-readiness findings (P1 legacy DB migration + P2 concurrent startup race) — deferred to Phase 4e. |
| Re-dispatches | 0 sprint-task re-dispatches; 3 Step 7a fixes produced by Orchestrator-direct edits (Small Tasks Exception — all <30 LOC mechanical fixes, single-file scope each). |
| First-Pass Rate | 13/13 declared tasks first-pass clean. Step 7a cycle 1 caught 3 row-level integrity defects that the 18-test D-test suite did not surface — load-bearing review checkpoint. Codex's code-correctness lens complementary to Reviewer's architecture-invariant lens (consistent with `feedback_review_lens_complementarity.md`). |

## Sprint Retrospective

**What went well**:
- **Phase 1 commit-before-Phase-2-dispatch discipline (S29/S30 carry-forward) worked again.** Phase 2's 3 parallel non-worktree agents (TASK-3107 / TASK-3108 / TASK-3109) committed cleanly with zero merge conflicts — file-disjoint scopes verified at dispatch, only TASK-3107 touched `Program.cs` (2 lines).
- **Refinement Step 4 dual-cycle absorption was load-bearing.** 5 BLOCKERs in cycle 1 + 4 NEW BLOCKERs in cycle 2 = 9 architecture defects caught before any code landed. Plan was materially safer than the initial Step 3 proposal (Q3/Q4/Q6/Q7 all reversed; PK structural defect + Compliance category error both caught + absorbed). Cycle-cap = 2 respected throughout; no waiver needed.
- **Step 7a dual-lens caught 3 P2 BLOCKERs the marquee + 17 supplementary D-tests didn't surface.** Codex's row-level correctness lens (GET row+version race + 2 missing CREATED audit rows) is the lens divergence pattern from `feedback_review_lens_complementarity.md` in action — Reviewer's architecture-invariant lens validated the cycle-1 reframe stood; Codex caught the code-level integrity gaps. Both useful, complementary.
- **Mid-sprint Codex CLI + model upgrade** (0.120.0 → 0.130.0 to unlock gpt-5.5; pinned `model = "gpt-5.5"` in `~/.codex/config.toml`) succeeded with a single `npm install -g @openai/codex@latest`. Memory saved (`reference_codex_cli_model.md`) so future sessions don't re-diagnose.
- **18 D-tests passed first-run** (TASK-3110 single dispatch). Marquee 3-way atomic round-trip + 4-way POST atomicity + cross-org RBAC + admin CRUD shape + validation-gap honest documentation + backfill bootstrap. Validation gap (parseInt-equivalent for decimal fields) honestly flagged in test comments rather than wished away.

**What to improve**:
- **Step 7a cycle 1 caught 3 P2 defects the D-test suite should have caught.** The marquee tested PUT atomicity (3-way: row + audit + outbox) but did NOT test GET row+version-same-snapshot, nor did it test POST creating an audit row. **S32 takeaway**: when a sprint extends an existing atomic-tx path (e.g., AdminEndpoints POST 4-way → 5-way audit), the D-test suite must explicitly assert the FULL post-fix invariant (not just the original happy-path); the negative tests (rollback on duplicate-username) only assert the rollback, not the audit row's presence on success. Adding an `AdminUserCreate_EmitsCreatedAuditRow_InSameTx` D-test would have caught Fix #2 + Fix #3 at TASK-3110 dispatch time, not Step 7a cycle 1.
- **Plan-mode and Step 7a both surfaced "Codex cross-org leak" + "row-level audit gap" patterns** — these are systematic blind spots for sprints that follow the S25/S29/S30 admin-CRUD-with-If-Match precedent. **S32 takeaway**: when filing PLAN-s32.md, the Step 0a draft must explicitly enumerate the OrgScopeValidator-on-both-verbs invariant + audit-row-on-every-write invariant as Phase 1 ACs — not wait for Step 0b to catch them.
- **The 2 Step 7a cycle 2 findings (legacy DB migration + concurrent startup race) are S30 cycle-2 pattern repeat.** Pre-launch posture mitigates today, but the production-readiness Phase 4e candidate list now has 4 items spanning S30+S31. **Phase 4e takeaway**: the production-deploy sprint must explicitly enumerate "guarded init.sql migrations for every new table since pre-launch" + "concurrent-startup hardening for every seeder" as separate work-items.

**Knowledge produced**:
- **ADR-022 — Employee-Profile Consolidation + Pre-Baked Versioning (Phase 4d-3 Part 1)** filed as a sibling to ADR-020 + ADR-021; 9 decisions (D1 data-plane-only scope; D2 surrogate UUID PK; D3 pre-baked versioning columns dormant; D4 is_part_time dropped; D5 admin CRUD + 5-way AdminEndpoints POST atomicity; D6 OrgScopeValidator on both verbs; D7 register-4-emit-2 vocabulary; D8 seeder route; D9 frontend LocalHR+ only). Explicit S32 commitment list enumerated for ADR-023.
- **Memory: `reference_codex_cli_model.md`** — codex-cli 0.130.0 + gpt-5.5 pin in `~/.codex/config.toml`; future sessions won't re-diagnose the model selection if a `codex review` returns a 400 "requires newer version" error.
- **Code-level lesson** (informal, no ADR): when a repository has a "rule-engine-consumable shape" public API (`EmploymentProfile` here) AND a row-version column the API doesn't carry, the GET endpoint MUST either (a) extend the API to carry the version, or (b) provide a tuple-returning sibling method that returns both in a single SELECT. Two-call reads open a same-row concurrency window between the body fetch and the version probe — observable as silent overwrites under concurrent admin edits. Step 7a cycle 1 caught this; future repository designs should pre-empt.
