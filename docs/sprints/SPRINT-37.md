# Sprint 37 — Phase A Pass 2: Interim-Expert Absorption of S36 Candidate Bugs

| Field | Value |
|-------|-------|
| **Sprint** | 37 |
| **Status** | **complete** |
| **Start Date** | 2026-05-21 |
| **End Date** | 2026-05-21 |
| **Orchestrator Approved** | yes — 2026-05-21 |
| **Build Verified** | yes — 0 errors, 19 pre-existing CS0618 warnings (unchanged) |
| **Test Verified** | yes — 526 unit tests pass; Docker-gated deferred to next greenfield bootstrap; total = 869 unchanged from S36 |
| **Sprint-start commit base** | `ec40d45` (S36 Step 7a absorption, 2026-05-21) |
| **Sprint-end HEAD** | `03f63d7` (TASK-3708 sprint close) |
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

## Sprint Close (TASK-3708)

### Outcomes Summary

3 of 4 candidate bugs fully resolved via mechanical seed corrections; 1 bug direction-recorded with implementation deferred to S40.

| Bug | Resolution | Files changed | Commit |
|-----|------------|---------------|--------|
| #1 | AC variants entitlement_configs mirror AC base (20 new rows × 5 entitlements × 2 OK × 2 variants) | init.sql + agreement-source-register.md | `3eea4f5` |
| #2 | AC variants wage_type_mappings mirror AC base (rename CHILD_SICK_1→CHILD_SICK_DAY + chain + 5 SLS code remaps); FIXES pre-existing pre-launch production-broken state | init.sql + agreement-source-register.md | `ce1bf68` |
| #3 | SENIOR_DAY paired-bug Path B (quota=2, min_age=62 user-corrected, pro_rate=false) uniform across 5 agreements | init.sql + agreement-source-register.md | `2eaa021` |
| #4 | HK/PROSA pre-approval Path A direction recorded; seed flip deferred to S40 alongside ADR-024 D7 workflow extension | agreement-source-register.md + PROGRAM-s36-s41 (D7 added) | `fa00d97` |

Plus S36 Step 7a deferred cosmetics absorbed (Codex P2.1 numbering + Reviewer W2 enum drift): `65f9866`.

### Step 7a Dual-Lens (TASK-3707)

Mandatory per `sprint-close-guard.ps1` hook (commit `297fdee` from session governance fix). Both lenses returned APPROVED-WITH-WARNINGS, 0 BLOCKERs. Cycle-1 sufficient.

| Lens | Verdict | Findings |
|------|---------|----------|
| Codex external (gpt-5.5, read-only) | APPROVED-WITH-WARNINGS | 0 BLOCKERs + 2 WARNINGs (P2.1+P2.2 convergent on stale OK26 text + AC_TEACHING resolved-but-still-labeled-candidate) + 4 confirmatory NOTEs (seed corrections verified; production-broken-state claim verified vs AbsenceRule.cs; no code refs to variant-only SLS codes outside seed; Bug #4 split routing sound; no P1-P9 violations) |
| Reviewer Agent (internal) | APPROVED-WITH-WARNINGS | 0 BLOCKERs + 2 WARNINGs (W1 stale text — convergent with Codex P2.1+P2.2; W2 bug_correction_history schema drift — new `action` field + PENDING_S40 value not enumerated in schema row) + 4 NOTEs (deferred) |

Artifacts at `.claude/reviews/SPRINT-37-step7a-{codex,reviewer}.md` (gitignored). W1+W2 absorbed in commit `e4c6517` before close.

### Bug-Correction-History Convention Extended

This sprint introduces 4 net-new `bug_correction_history` entries across the source register following S35 AC=AFSPADSERING's template. Pattern now extends to two action variants:

- `action: "bug-fix-without-recompute"` (Bugs #1, #2, #3) — seed change shipped same commit as decision
- `action: "decision-recorded-fix-deferred"` (Bug #4) — direction recorded; implementation deferred due to workflow prerequisite

Schema row 13 updated to enumerate both action values + the expanded `materially_wrong` value space (`NO_PRE_LAUNCH`, `YES_PRE_LAUNCH_BUT_BROKEN`, `PENDING_S<NN>`, `YES_WITH_PAST_IMPACT`). Documented as S38 ADR-024 D8 candidate for formal schema rev.

### Source Register Status

Stays **DRAFT** post-S37. Full APPROVED status awaits real Phase B engagement covering the broader ~80 MATCH-PENDING-SOURCE cells. This sprint is a **partial** Phase A pass 2 absorption — scoped to the 4 candidate-bug subset under interim-expert posture.

### Commit List (10 commits across S37)

```
e878e11 S37 TASK-3700: sprint open — Phase A pass 2 interim-expert absorption
3eea4f5 S37 TASK-3701: Bug #1 absorption — AC variants entitlement_configs mirror AC base (20 new rows)
ce1bf68 S37 TASK-3702: Bug #2 absorption — AC variants wage_type_mappings mirror AC base
2eaa021 S37 TASK-3703: Bug #3 absorption — SENIOR_DAY paired bug, Path B seed-side fix
fa00d97 S37 TASK-3704: Bug #4 absorption — HK/PROSA pre-approval split routing (SR annotation + ADR-024 D7 registration)
65f9866 S37 TASK-3705: S36 Step 7a deferred cosmetics absorption
e4c6517 S37 TASK-3707: Step 7a dual-lens absorption (W1 stale OK26 text + W2 schema enum drift)
[this commit] S37 TASK-3708: sprint close
```

(TASK-3706 build+test verification was inline — no separate commit since no file changes; results documented above and in the Build Verified / Test Verified fields.)

### Forward Pointers

- **S38 ADR authorship** — ADR-024 gains D7 (overtime authorization model) + candidate D8 (bug_correction_history schema rev). Existing D1–D6 unchanged.
- **S39 schema migration** — `role_within_agreement_configs` table creation; Phase E continuous-validation tests; seed-parity tests must filter `N/A-for-agreement` cells per S36 TASK-3705 documentation.
- **S40 cutover** — Bug #4 workflow extension + seed flip lands here together.
- **Future sprint when real Phase B engages** — bulk-refresh OK26 placeholder bundle text + absorb remaining ~80 MATCH-PENDING-SOURCE cells.

### Phase B Engagement Status (unchanged)

| Field | Status at S37 close |
|-------|---------------------|
| Candidate identification | _pending — to be filled before real Phase B engagement_ |
| Engagement window | _pending_ |
| Interim verifier this sprint | Orchestrator (with user-confirmed decisions per-bug, 2026-05-21) |
| Source register status | DRAFT (partial post-S37; full APPROVED awaits real Phase B) |

