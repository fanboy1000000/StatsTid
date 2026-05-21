# Sprint 37 — Phase A Pass 2: Interim-Expert Absorption of S36 Candidate Bugs

| Field | Value |
|-------|-------|
| **Sprint** | 37 |
| **Status** | in-progress |
| **Start Date** | 2026-05-21 |
| **End Date** | _pending_ |
| **Orchestrator Approved** | _pending TASK-3708_ |
| **Build Verified** | _pending_ |
| **Test Verified** | _pending_ — test count unchanged from S36 (869 total) projected since seed-only changes |
| **Sprint-start commit base** | `ec40d45` (S36 Step 7a absorption, 2026-05-21) |
| **Sprint-end HEAD** | _filled by close commit_ |
| **Sprint type** | **IMPLEMENTATION** — seed corrections + source register annotations + bug_correction_history entries |
| **Plan** | `.claude/plans/PLAN-s37.md` |
| **Phase** | 4e (Phase A pass 2 absorption per PROGRAM L105–117; scoped to S36 candidate-bug subset under interim-expert posture) |

## Sprint Goal

Absorb the 4 candidate-bug decisions surfaced in S36 Phase A inventory under **interim-expert posture** (user as verifier; external Phase B engagement remains pending).

| Bug | Resolution | S37 work | Notes |
|-----|------------|----------|-------|
| **#1** AC variants missing `entitlement_configs` rows | AC_base for all 5 entitlements (20 new rows) | Mechanical seed correction | Bug-with-no-past-impact |
| **#2** AC variants `wage_type_mappings` divergent SLS codes | S11 authoring bug → mirror AC base (rename CHILD_SICK_1→CHILD_SICK_DAY + add chain + remap 5 SLS codes) | Mechanical seed correction | Fixes pre-existing pre-launch production-broken state (rule engine emits CHILD_SICK_DAY; AC variants had only CHILD_SICK_1 phantom) |
| **#3** SENIOR_DAY paired-bug (quota=0 + min_age=60) | Path B: quota=2, min_age=62, pro_rate=false; description text "alder 62+" | Mechanical seed correction (10 rows) | min_age user-corrected from 60 to 62 |
| **#4** HK + PROSA `OvertimeRequiresPreApproval=false` | Path A (flip to true) BUT split routing | **SR annotation only** + ADR-024 D7 registration | Seed flip GATED on workflow extension; lands S40 alongside post-hoc necessity-acknowledgment workflow |

Plus S36 Step 7a deferred cosmetics:
- Codex P2.1: candidate-bug numbering normalization
- Reviewer W2: confidence_level enum drift documentation

## Interim-Expert Posture (Phase B scope-limit)

This sprint operates with the user acting as interim verifier for the 4 candidate-bug decisions only. **External Phase B engagement remains pending** for the ~80 broader MATCH-PENDING-SOURCE cells. Sprint produces a **partial** Phase A pass 2 absorption:

- Affected SR rows: cells touched by Bugs #1–#4 get `last_verified_by = "Orchestrator (interim, user-confirmed decision)"` + `decision_date = 2026-05-21`.
- ~80 remaining MATCH-PENDING-SOURCE cells stay unchanged.
- Source register status stays **DRAFT**. Full APPROVED status awaits real Phase B engagement.
- ADR-024 D7 added to S38 backlog per Bug #4 routing.

This matches PROGRAM L117 spillover-bugs pattern: real Phase B feedback (when it arrives) absorbs the remaining ~80 cells in a follow-up sprint (S37b / fold into S38 if expert engages quickly).

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | **SKIP** — implementation work scoped to mechanical seed corrections with pre-decided expert direction. No architectural decisions surface in S37 (those route to S38 ADR-024 D7). Step 7a at close is the formal review gate. |
| **External Codex** | not invoked at Step 0b |
| **Internal Reviewer** | not invoked at Step 0b |

## Architectural Constraints

_Checked off at TASK-3708 close._

- [ ] **P1 — Architectural integrity** → No architecture touched. Seed corrections only.
- [ ] **P2 — Rule engine determinism** → No rule-engine code changes. Bug #2 seed alignment with `AbsenceRule.cs:112-114` `CHILD_SICK_DAY` emission FIXES pre-existing pre-launch broken state.
- [ ] **P3 — Event sourcing / auditability** → bug_correction_history entries on affected SR rows preserve audit trail per ROADMAP rule correction policy. First multi-bug absorption since S35 AC=AFSPADSERING set the precedent.
- [ ] **P4 — Version correctness** → SENIOR_DAY + AC variants entitlement migrations follow ADR-021 effective-dating pattern (effective_from='0001-01-01' for pre-launch seed).
- [ ] **P5–P9** → N/A in seed-only sprint with no rule/auth/CI/UX touches.

## Task Log

9 declared tasks (TASK-3700..3708) across 6 phases. Plan file `.claude/plans/PLAN-s37.md` is source-of-truth for per-task detail.

### Phase 0 — Sprint Open (TASK-3700)
Sprint plumbing: PLAN-s37 + SPRINT-37 + INDEX provisional row.

### Phase 1 — Seed Corrections (TASK-3701, 3702, 3703 — sequential)
- TASK-3701: Bug #1 (20 new entitlement_configs rows for AC variants × 5 types × 2 OK)
- TASK-3702: Bug #2 (CHILD_SICK rename + chain + 5 SLS code remaps, ~44 row deltas)
- TASK-3703: Bug #3 (SENIOR_DAY quota=2 + min_age=62 + description text, 10 rows)

### Phase 2 — SR Annotation Only (TASK-3704)
Bug #4: source register annotation + ADR-024 D7 registration in PROGRAM. NO seed change.

### Phase 3 — Cosmetic Absorption (TASK-3705)
S36 Step 7a deferred: Codex P2.1 numbering + Reviewer W2 enum-drift docs.

### Phase 4 — Verification (TASK-3706)
`dotnet build` + plain-regression test suite. Docker-gated deferred to next greenfield bootstrap.

### Phase 5 — Step 7a Dual-Lens (TASK-3707)
**Mandatory per new sprint-close-guard.ps1 hook** (commit `297fdee`). Codex external + Reviewer Agent in parallel. Artifacts at `.claude/reviews/SPRINT-37-step7a-{codex,reviewer}.md` with `verdict:` lines. Cycle-cap 2 per lens.

### Phase 6 — Sprint Close (TASK-3708)
Close sections + INDEX + ROADMAP + MEMORY. Sprint-close commit gated by hook.

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | _at TASK-3707, mandatory per hook_ |
| **Sprint diff range** | `ec40d45..HEAD` |
| **Cycle-cap** | 2 per lens |

## Test Summary

Per `sprint-test-validation` skill at sprint close. Projected: test count unchanged from S36 (869 total) since no test code touched.

## Forward Pointers

- **S38 ADR authorship**: ADR-024 gains D7 (overtime authorization model with necessity-acknowledgment); existing D1–D6 unchanged
- **S39 schema migration**: `role_within_agreement_configs` table creation still pending per PROGRAM L173
- **S40 cutover**: Bug #4 seed flip + workflow extension lands here together
- **Future sprint when real Phase B engages**: absorb remaining ~80 MATCH-PENDING-SOURCE cells

---

_Updated at sprint close (TASK-3708): outcomes summary, commit list, sprint duration, MEMORY.md entry._
