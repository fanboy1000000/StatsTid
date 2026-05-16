# Sprint 31 — Phase 4d-3 Part 1: Employee-Profile Authoritative Store

| Field | Value |
|-------|-------|
| **Sprint** | 31 |
| **Status** | **in-progress** (opened 2026-05-16) |
| **Start Date** | 2026-05-16 |
| **End Date** | _filled by TASK-3112_ |
| **Orchestrator Approved** | no (sprint open) |
| **Build Verified** | _filled by TASK-3112_ |
| **Test Verified** | _filled by TASK-3112_ |
| **Sprint-start commit base** | `68a6f07` (S30 sprint close, 2026-05-16 — TASK-3012 sprint-close commit) |
| **Sprint-end HEAD** | _filled by TASK-3112_ |
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

_Filled by Phase 4 + TASK-3112. Per plan: required pre-commit by default; user may grant documented exit when marquee D-test substantively serves the Step-7a-equivalent purpose (S29/S30 precedent)._

| Field | Value |
|-------|-------|
| **Invoked** | pending |
| **Sprint-start commit** | `68a6f07` |
| **Command** | _filled at sprint close_ |
| **Review Cycles** | _filled at sprint close_ |
| **Findings** | _filled at sprint close_ |
| **Resolution** | _filled at sprint close_ |

## Test Summary

_Filled by Phase 4 / TASK-3112. Target floor: 828 (815 baseline + 13 net new). Stretch: 832 (+17 if all RBAC + atomicity D-tests land) or 836 (+frontend vitest)._

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | _pending_ | _pending_ |
| Plain regression | _pending_ | _pending_ |
| Docker-gated | _pending_ | _pending_ |
| Frontend vitest | _pending_ | _pending_ |
| **Total** | _pending_ | _pending_ |

## Agent Effectiveness

_Filled by TASK-3112._

| Metric | Value |
|--------|-------|
| Tasks | 13 declared (TASK-3100–3112; TASK-3105/3106 fork resolved to seeder route at Step 0b) |
| Constraint Violations | _pending_ |
| Reviewer Findings | _pending_ |
| External Review Cycles | _Step 0b: 1 cycle Codex / 1 cycle Reviewer; Step 7a: pending_ |
| External Findings | _Step 0b: 2 BLOCKERs + 2 WARNINGs Codex + 2 WARNINGs Reviewer; all absorbed; Step 7a: pending_ |
| Re-dispatches | _pending_ |
| First-Pass Rate | _pending_ |

## Sprint Retrospective

_Filled by TASK-3112._

**What went well**: _pending_

**What to improve**: _pending_

**Knowledge produced**: _pending — ADR-022 expected_
