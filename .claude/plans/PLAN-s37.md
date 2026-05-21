# PLAN — Sprint 37: Phase A Pass 2 — Interim-Expert Absorption of S36 Candidate Bugs

## Sprint Header

| Field | Value |
|-------|-------|
| **Sprint** | 37 |
| **Title** | Phase A Pass 2 — Interim-Expert Absorption of S36 Candidate Bugs (#1 + #2 + #3 mechanical seed corrections; #4 SR annotation only) |
| **Status** | DRAFT (Step 0b SKIP per design-implementation hybrid precedent; rationale below) |
| **Start Date** | 2026-05-21 |
| **Projected End Date** | 2026-05-21 (single-day sprint; seed corrections are mechanical) |
| **Sprint-start base commit** | `ec40d45` (S36 Step 7a absorption, 2026-05-21) |
| **Sprint type** | **IMPLEMENTATION** — seed corrections in `docker/postgres/init.sql` + `src/SharedKernel/.../CentralAgreementConfigs.cs` + source register annotations + bug_correction_history entries |
| **Refinement** | Not filed. Interim-expert decisions logged in this session (user acting as expert; not external Phase B). Per-bug routing decisions documented inline in TASK-3701..3704. |
| **Plan** | this file |
| **Phase** | 4e (Phase A pass 2 absorption per PROGRAM L105–117) |

## Sprint Goal

Absorb the 4 candidate-bug decisions surfaced in S36 Phase A inventory:

| Bug | Resolution | Sprint work |
|-----|------------|-------------|
| **#1** AC variants missing `entitlement_configs` rows | AC_base values for all 5 entitlement types across AC_RESEARCH + AC_TEACHING × OK24 + OK26 | Mechanical seed correction: 20 new rows. Bug-with-no-past-impact. |
| **#2** AC variants `wage_type_mappings` divergent SLS codes | S11 seed authoring bug → mirror AC base | Mechanical seed correction: rename `CHILD_SICK_1` → `CHILD_SICK_DAY` + add `_DAY_2`/`_DAY_3` chain (12 row removes + 12 adds) + remap 5 SLS codes (20 row updates). Bug-with-no-past-impact. **Surfaces + fixes pre-launch production-broken state**: rule engine emits `CHILD_SICK_DAY` (per `AbsenceRule.cs:112-114`) but AC variants seed had `CHILD_SICK_1` — phantom mapping, current AC variant child-sick events drop silently. Cleanup re-aligns. |
| **#3** SENIOR_DAY paired-bug (`annual_quota=0` + `min_age=60`) | Path B (seed-side fix): `annual_quota=2`, `min_age=62`, `pro_rate=false`; description text updated to "62+" | Mechanical seed correction: 6 existing rows + 4 new rows from Bug #1 cascade = 10 rows. Bug-with-no-past-impact. |
| **#4** HK + PROSA `OvertimeRequiresPreApproval=false` | Path A (cirkulær-mandated → flip to `true`) BUT split routing | **S37 = SR annotation only** ("decision: Path A; seed flip gated on workflow extension"). **S38 = new ADR-024 D7 design** (workflow + post-hoc necessity-acknowledgment path). **S40 = workflow + seed flip lands together** to avoid intermediate-state regression. NO seed change in S37. |

Plus S36 Step 7a deferred cosmetics:
- Codex P2.1: candidate-bug numbering normalization across `danish-agreements.md` + source register cumulative table
- Reviewer W2: document `confidence_level` enum extension (HIGH/MEDIUM/LOW/N/A-for-agreement → 4 values, not PROGRAM's nominal 3) in source register schema section

## Interim-Expert Posture

This sprint operates with the **user as interim verifier** for the 4 candidate-bug decisions. External Phase B domain-expert engagement remains pending. Implications:

- The 4 specific cells touched (across SR rows enumerated below) get `last_verified_by = "Orchestrator (interim, with user-confirmed decisions)"` and `decision_date = 2026-05-21`. Confidence levels per-cell.
- The ~80 remaining MATCH-PENDING-SOURCE cells STAY MATCH-PENDING-SOURCE. They are not covered by interim-expert sign-off.
- Source register status stays **DRAFT** (not APPROVED). Full APPROVED status awaits real Phase B engagement covering the broader cell population. This sprint produces a **partial** absorption.
- New ADR candidate filed for S38: **ADR-024 D7 — Overtime authorization model (pre-approval + post-hoc necessity-acknowledgment)** per Bug #4 routing.

This matches the spirit of PROGRAM L105–117 (Phase A pass 2 absorption) but scopes to the 4 candidate-bug subset rather than full ~111-cell coverage. PROGRAM L117 noted "spillover-bugs file for S38" — same pattern applied here: Bug #4 spills to S38 ADR-024 D7.

## Phase Decomposition

| Phase | Tasks | Dispatch |
|-------|-------|----------|
| 0 | TASK-3700 | Sprint open (PLAN + SPRINT-37 + INDEX provisional) |
| 1 | TASK-3701, 3702, 3703 | Bug #1, #2, #3 seed corrections + SR annotations + bug_correction_history (sequential) |
| 2 | TASK-3704 | Bug #4 SR annotation + PROGRAM update for ADR-024 D7 |
| 3 | TASK-3705 | S36-deferred cosmetics absorption (numbering + enum drift docs) |
| 4 | TASK-3706 | Build + plain-regression-test verification (seed-only changes; no rule-engine logic touched but worth running) |
| 5 | TASK-3707 | Step 7a dual-lens review (Codex + Reviewer; mandatory per new hook) |
| 6 | TASK-3708 | Sprint close + Step 7a artifacts must be present with verdict lines for hook to allow close commit |

## Step 0a — Entropy Scan

| Check | Result |
|-------|--------|
| KB path validation | CLEAN |
| Pattern compliance | CLEAN — mechanical seed corrections follow pre-launch rule correction policy first applied in S35 TASK-3503 (AC=AFSPADSERING) |
| Mechanical-gate state | New `sprint-close-guard.ps1` hook ACTIVE (commit `297fdee`); S37 close commit MUST satisfy the gate |
| Documentation drift | NONE — S36 Step 7a absorption (`ec40d45`) cleared the cycle-1 warnings; S37 starts on a clean diff base |

## Step 0b — Plan Review

**SKIP** — implementation work scoped to mechanical seed corrections with pre-decided expert direction. No architectural decisions surface in S37 (those route to S38 ADR-024 D7). Step 7a at close is the formal review gate; Step 0b dual-lens would be duplicative.

## Architectural Constraints

- [ ] **P1 — Architectural integrity** → No architecture touched. Seed corrections only.
- [ ] **P2 — Rule engine determinism** → No rule-engine code changes. Seed alignment with rule-engine `CHILD_SICK_DAY` emission (Bug #2) FIXES a pre-existing pre-launch broken state.
- [ ] **P3 — Event sourcing / auditability** → bug_correction_history entries on affected SR rows preserve audit trail per ROADMAP rule correction policy. First multi-bug absorption since S35 AC=AFSPADSERING set the precedent.
- [ ] **P4 — Version correctness** → SENIOR_DAY + AC variants entitlement_configs migrations follow ADR-021 effective-dating pattern (effective_from='0001-01-01' for pre-launch seed; no past-period impact).
- [ ] **P5 — Integration isolation** → N/A in seed-only sprint.
- [ ] **P6 — Payroll integration correctness** → Bug #2 resolution restores AC variant child-sick event mapping (currently broken pre-launch); MERARBEJDE/SLS_0210 collision with HK/PROSA OVERTIME_50 resolves via remap to SLS_0310.
- [ ] **P7 — Security / access control** → N/A.
- [ ] **P8 — CI/CD** → N/A; seed migrations are init.sql-only (greenfield-baked).
- [ ] **P9 — UX** → N/A; no frontend changes.

## Task Log

### Phase 0 — Sprint Open

#### TASK-3700 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3700 |
| **Status** | in-progress |
| **Components** | `.claude/plans/PLAN-s37.md`, `docs/sprints/SPRINT-37.md`, `docs/sprints/INDEX.md` |
| **Dependencies** | none |

---

### Phase 1 — Seed Corrections (3 sequential tasks)

#### TASK-3701 — Bug #1 absorption (AC variants entitlement_configs)

**Scope**: add 20 new `entitlement_configs` rows mirroring AC base values for AC_RESEARCH + AC_TEACHING × OK24 + OK26 × 5 entitlement types.

**Files**: `docker/postgres/init.sql` (insert after line 1378), `docs/references/agreement-source-register.md` (annotate SR-AC_RESEARCH-OK24-005 + SR-AC_TEACHING-OK24-005 + OK26 inheritance rows with `last_verified_by`, `decision_date`, `confidence_level`, `bug_correction_history`).

**Verifier**: Orchestrator (interim, user-confirmed decision 2026-05-21).
**Classification**: bug-with-no-past-impact per ROADMAP rule correction policy. Pre-launch posture; forward-only correction.

#### TASK-3702 — Bug #2 absorption (AC variants wage_type_mappings)

**Scope**: 
1. Remove 4 phantom `CHILD_SICK_1 → SLS_0560` rows (AC_RESEARCH + AC_TEACHING × OK24 + OK26).
2. Add 12 new rows: `CHILD_SICK_DAY → SLS_0530`, `CHILD_SICK_DAY_2 → SLS_0531`, `CHILD_SICK_DAY_3 → SLS_0532` × 4 variant combinations.
3. Update 20 rows: 5 SLS codes (`MERARBEJDE`, `CARE_DAY`, `SENIOR_DAY`, `LEAVE_WITH_PAY`, `LEAVE_WITHOUT_PAY`) × 4 variant combinations to mirror AC base values.

**Files**: `docker/postgres/init.sql` (lines 965–1021 modified). `docs/references/agreement-source-register.md` SR-AC_RESEARCH-OK24-006 + SR-AC_TEACHING-OK24-006 annotated.

**Critical**: this resolution fixes a pre-existing pre-launch production-broken state (rule engine emits `CHILD_SICK_DAY` per AbsenceRule.cs:112-114; AC variants seed previously had only `CHILD_SICK_1` phantom). Confirmed via grep: variant-only SLS codes (SLS_0570/0580/0590) not referenced anywhere outside init.sql seed.

#### TASK-3703 — Bug #3 absorption (SENIOR_DAY paired-bug)

**Scope**: update 6 existing `SENIOR_DAY` rows (AC + HK + PROSA × OK24 + OK26) + the 4 new AC variants rows added by TASK-3701 to use:
- `annual_quota = 2` (was 0)
- `min_age = 62` (was 60; user-corrected)
- `pro_rate_by_part_time = false` (unchanged)
- description text: `'Seniordage – kræver alder 62+'` (was 'alder 60+')

Total: 10 row updates.

**Files**: `docker/postgres/init.sql` (lines 1373–1378 + 4 new AC variant rows from TASK-3701). `docs/references/agreement-source-register.md` SR rows SR-AC-OK24-015 + 035 + SR-HK-OK24-029 + SR-PROSA-OK24-006 + AC variant inheritance annotated.

### Phase 2 — SR Annotation Only (1 task)

#### TASK-3704 — Bug #4 absorption (HK/PROSA pre-approval, SR annotation + ADR-024 D7 registration)

**Scope**: 
1. Annotate SR-HK-OK24-022 + SR-PROSA-OK24-007 (+ OK26 inheritance) with verifier sign-off + classification: "decision = Path A (flip to TRUE), but **seed flip GATED on ADR-024 D7 workflow extension landing in S40**. SR cell records the verified direction; init.sql seed unchanged in S37".
2. Update `.claude/plans/PROGRAM-s36-s41-domain-correctness.md` ADR-024 section to add D7: "Overtime authorization model — pre-approval + post-hoc necessity-acknowledgment path. Required because flat-flip from `false → true` without necessity-acknowledgment would regress legitimate necessity-driven overtime (currently flows through under `false`)."
3. Update `docs/sprints/INDEX.md` Phase 4e routing: Bug #4 seed flip → S40 (not S37).

**NO seed change in S37.** S40 lands the workflow + seed flip together.

### Phase 3 — Cosmetic Absorption (1 task)

#### TASK-3705 — S36 Step 7a deferred cosmetics

**Scope**:
1. **Codex P2.1 — candidate-bug numbering normalization** across:
   - `docs/references/danish-agreements.md` (entitlement-quotas table — "candidate bug #3" → "candidate bug #1")
   - `docs/references/agreement-source-register.md` cumulative bug-discoveries table (reorder SENIOR_DAY from first to third position; match the canonical 1-2-3-4 order in `agreement-ruleset-audit.md`)
2. **Reviewer W2 — confidence_level enum drift documentation**:
   - Update `docs/references/agreement-source-register.md` schema section to explicitly document the 4-value enum (HIGH / MEDIUM / LOW / N/A-for-agreement) introduced in TASK-3601.
   - Add a forward-pointer note: S39 Phase E seed-parity tests must filter N/A-for-agreement cells to avoid spurious failures on inert supplement rates.

### Phase 4 — Verification (1 task)

#### TASK-3706 — Build + regression test verification

**Scope**: 
- `dotnet build` against the seed-only changes (no rule-engine code touched, build should be clean)
- Run plain-regression tests via `dotnet test` (no Docker required; covers EventSerializer + AbsenceRule basic flow)
- Skip Docker-gated tests (would require fresh DB rebuild; the seed correctness is verified at integration time when next greenfield bootstrap fires)
- Document test count delta in TASK-3706 close

### Phase 5 — Step 7a (1 task; mandatory per hook)

#### TASK-3707 — Step 7a dual-lens review

**Scope**: Codex external (read-only) + Reviewer Agent in parallel against the S37 diff (`ec40d45..HEAD`). Cycle-cap 2 per lens. Save artifacts at `.claude/reviews/SPRINT-37-step7a-codex.md` + `.claude/reviews/SPRINT-37-step7a-reviewer.md` with `verdict:` lines. The `sprint-close-guard.ps1` hook (commit `297fdee`) blocks sprint-close if either is missing or lacks verdict.

### Phase 6 — Sprint Close (1 task)

#### TASK-3708 — Sprint close

**Scope**: SPRINT-37.md close sections + INDEX.md finalization + ROADMAP Phase 4e Phase A pass 2 marked PARTIAL-COMPLETE (full APPROVED status awaits real Phase B engagement) + MEMORY.md S37 line. Sprint-close commit gated by hook + Step 7a artifacts present.

## Test Summary (TASK-3708)

Per `sprint-test-validation`: build clean expected; test count unchanged from S36 (869 total) since no test logic added. Plain-regression suite covers the rule-engine emission path that Bug #2 cleanup re-aligns with seed.

## Forward Pointers

- **S38 ADR authorship** — ADR-024 D7 added: "Overtime authorization model (pre-approval + post-hoc necessity-acknowledgment)". Existing D1-D6 unchanged.
- **S39 schema migration** — entitlement_configs + wage_type_mappings forward-compat schema unchanged (seed corrections only). `role_within_agreement_configs` table still needs S39 creation per PROGRAM.
- **S40 cutover** — Bug #4 seed flip + workflow extension lands here, together. NOT a separate sprint.
- **S37 → real Phase B**: when external domain expert engages, a follow-up sprint absorbs the remaining ~80 MATCH-PENDING-SOURCE cells. Could be S37b, S38b, or fold into S38 if expert engages quickly.
