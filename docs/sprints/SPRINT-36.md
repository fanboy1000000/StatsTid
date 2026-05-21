# Sprint 36 — Phase A Inventory Sprint 1 (DESIGN-ONLY)

| Field | Value |
|-------|-------|
| **Sprint** | 36 |
| **Status** | in-progress |
| **Start Date** | 2026-05-21 |
| **End Date** | _pending_ |
| **Orchestrator Approved** | _pending TASK-3610_ |
| **Build Verified** | N/A — design-only sprint; no code changes; no `dotnet build` verification needed (S28 / S32 precedent) |
| **Test Verified** | N/A — design-only sprint; test totals unchanged from S35 close (869 total). `sprint-test-validation` skill SKIP with rationale at sprint close. |
| **Sprint-start commit base** | `a094630` (post-S35 governance commit, 2026-05-20 — cap-fires-after-verification + post-7a-coverage governance edits) |
| **Sprint type** | **DESIGN-ONLY** — produces 3 NEW reference docs (`agreement-source-register.md` + `role-dimension-audit.md` + `agreement-ruleset-audit.md`) + 1 UPDATE (`danish-agreements.md` cross-reference) + 1 NEW handoff doc (`phase-b-handoff-package.md`). No code changes; no test changes. Mirrors S28 / S32 design-only pattern. |
| **Refinement** | Not filed. `.claude/plans/PROGRAM-s36-s41-domain-correctness.md` is the de facto refinement artifact — its design absorbed dual-lens scrutiny during S35 cycle-1 absorption (committed 2026-05-18). |
| **Plan** | `.claude/plans/PLAN-s36.md` (Step 0a CLEAN + Step 0b SKIP per design-only precedent) |
| **Phase** | 4e (general hardening — pre-launch domain-correctness sweep, sprint 1 of 7 per PROGRAM-s36-s41) |

## Sprint Goal

Produce a **comprehensive agreement source register** + 2 supporting audit docs + Phase B handoff package for the 5 production agreement codes (AC / HK / PROSA / AC_RESEARCH / AC_TEACHING) across OK24 + OK26.

**Strategic context**: pre-launch posture is load-bearing. Every bug found here ships as a free seed correction (continuing the policy first applied in S35 TASK-3503 AC=UDBETALING → AFSPADSERING). Every bug missed becomes a post-launch supersession event with workflow overhead per ROADMAP rule correction policy committed 2026-05-18. The source register makes every cell in the agreement / role / OK matrix traceable to a cited paragraph in an authoritative cirkulær, plus confidence + decider + verification date — closing the systemic gap the AC seed bug exposed (encoding drift from cirkulærer with no process catching it).

**Three systemic gaps addressed across S36–S41 program** (PROGRAM L29–34):
1. AC seed correction was a symptom, not the root cause — source-of-truth is inverted (code/DB authoritative; cirkulærer consulted only ad-hoc); no process catches encoding drift
2. Within-OK role distinction is unmodeled — AC chefkonsulent loses contractual merarbejde right per overenskomst but rules don't read `User.EmploymentCategory` (vestigial field)
3. Multi-tenant operational gaps for 150-institution deployment — per-tenant SLS endpoint, customer onboarding runbook, GDPR per-tenant erasure, noisy-neighbor fairness, etc.

S36 addresses gap (1) inventory phase. Gaps (2) + (3) ADRs land in S38 (ADR-024 + ADR-025). Schema + cutover land in S39 + S40. Exhaustive D-tests + governance bake-in land in S41.

**Out-of-scope for S36**: ADR authorship (S38), schema migration (S39), cutover (S40), D-test matrix + danish-agreements.md rewrite + WORKFLOW.md OK-transition checklist (S41), bug correction workflow ADR-027 (post-launch).

## Entropy Scan Findings (Step 0a)

Run 2026-05-21 at sprint open:

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ROADMAP Deployment Model + Phase 4e bullets + rule correction policy + PROGRAM-s36-s41-domain-correctness.md all resolve cleanly post-S35. |
| Pattern compliance | CLEAN | Design-only sprint follows S28 / S32 precedent: docs-only output, no code surface, no schema surface, no test surface. |
| Orphan detection | DEBT (carry-forward) | 80+ stale locked agent worktrees under `.claude/worktrees/`; S36 uses Orchestrator-direct dispatch only so non-blocking. Operational housekeeping remains Phase 4e backlog (deferred from S35). |
| Documentation drift | NONE | `docs/references/danish-agreements.md` already cited as the doc requiring TASK-3608 cross-reference update; no other drift detected. |
| Quality grade review | DEFERRED to S41 | New "Domain Correctness" category lands at S41 TASK-4108 per PROGRAM L215. S36 close emits no QUALITY.md change. |
| Refinement disposition | N/A | PROGRAM-s36-s41-domain-correctness.md is the de facto refinement; no fresh refinement file required. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | **SKIP** per S28 / S32 design-only precedent (AGENTS.md L307 + SPRINT-32.md L52). No implementation surface. No schema change. No test change. Architectural decisions settled by PROGRAM-s36-s41-domain-correctness.md, already absorbed dual-lens scrutiny during S35 cycle-1 absorption. |
| **External Codex** | not invoked |
| **Internal Reviewer** | not invoked |
| **BLOCKERs resolved before Phase 1** | n/a — Step 0b SKIP |

### Resolution

The relevant review checkpoint for design-only inventory work is **Phase B domain-expert validation** (PROGRAM L88–101 + S37 absorption tasks), not Codex / Reviewer dual-lens. S37 is where the inventory's correctness is adjudicated. S36 produces the artifacts; S37 reconciles them against expert feedback.

## Architectural Constraints Verified

_Checked off as the sprint progresses; final assertion in TASK-3610._

- [ ] **P1 — Architectural integrity** → No code touched; no schema touched. Reference docs follow existing `docs/references/` convention. PROGRAM file cross-references stay sound.
- [ ] **P2 — Rule engine determinism** → No rule changes. Source register documents intended encoding for downstream verification in S39 Phase E seed-parity tests.
- [ ] **P3 — Event sourcing / auditability** → No event surface changes. Source register becomes the audit-trail artifact for "what cell value was authoritative at what date."
- [ ] **P4 — Version correctness** → Each register cell carries `ok_version` discriminator + `supersession_history` column; multi-OK reasoning baked in from row one.
- [ ] **P5–P9** → Not applicable in design-only sprint.

## Task Log

11 declared tasks (TASK-3600..3610) across 5 phases. Plan file `.claude/plans/PLAN-s36.md` is source-of-truth for per-task detail.

### Phase 0 — Sprint Open

#### TASK-3600 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3600 |
| **Status** | **completed** (commit `f253646`) |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s36.md`, `docs/sprints/SPRINT-36.md` (this file), `docs/sprints/INDEX.md` |
| **Dependencies** | none |

**Validation Criteria**:
- [x] `.claude/plans/PLAN-s36.md` filed with full 11-task decomposition + Step 0a + Step 0b SKIP sections
- [x] `docs/sprints/SPRINT-36.md` exists (this file)
- [x] `docs/sprints/INDEX.md` has Sprint 36 row (status=in-progress)
- [x] Sprint-open commit lands atop `a094630`

---

### Phase 1 — Source Register Skeleton + Proof-of-Shape (1 task)

- **TASK-3601** — Source register skeleton + first 20 cells from AC OK24 (proof-of-shape). **completed** (this commit) — `docs/references/agreement-source-register.md` filed with 15-column schema (2 enhancements over PROGRAM L51-67 documented in-file: explicit `row_id` + explicit `notes`) + 20 AC OK24 cells (4 quantitative + 4 enum/categorical + 4 rate/multiplier + 4 entitlement + 4 compliance/governance). Schema validation summary at end of file: speculative PROGRAM schema works on real data; no BLOCKERs surfaced; TASK-3602 may dispatch. **1 candidate bug discovery** (SR-AC-OK24-015 SENIOR_DAY quota=0 — encoding semantics unclear, flagged for S37 / Phase B priority). Per-task detail in PLAN-s36.md.

### Phase 2 — Per-Agreement Fill (4 sequential tasks)

- **TASK-3602** — Complete AC OK24 + OK26 source register entries. **completed** (this commit). 19 new AC OK24 rows (SR-AC-OK24-021..039) covering all remaining `agreement_configs` columns + `entitlement_configs` sub-field bundles + `wage_type_mappings` bundle + 2 position overrides. 4 AC OK26 placeholder bundle rows (SR-AC-OK26-001..004) covering all data domains by inheritance from OK24 with explicit `LOW` confidence + `pending` Phase B verification. AC OK24 total = 39 cells; AC OK26 total = 4 bundles. Schema validated: no new BLOCKERs, compound-cell pattern from TASK-3601 generalises cleanly to 11-column bundles (supplement-disablement) + 17-mapping bundles (wage_type_mappings). Compound cells (SR-AC-OK24-020 / -024 / -027 / -032..036 / -037 / OK26 bundles) confirmed sufficient via string-joined `field` syntax — first-class `field_group` schema field still deferred (no second use-case where string-join is awkward). SENIOR_DAY candidate-bug discovery from TASK-3601 reinforced by SR-AC-OK24-035 sub-field row (`min_age = 60` populated alongside `annual_quota = 0` is a structural inconsistency); flagged for S37 Phase B HIGH priority. **Observation for TASK-3603**: HK has all supplements ENABLED — supplement-rate cells will be load-bearing individual rows, not compound inert bundles (schema unchanged, just more individual rows expected). Per-task detail in PLAN-s36.md.
- **TASK-3603** — Complete HK OK24 + OK26 source register entries. **completed** (this commit). 31 new HK OK24 rows (SR-HK-OK24-001..031) + 4 HK OK26 placeholder bundles (SR-HK-OK26-001..004). HK pattern inverts AC across ~12 cells: `HasOvertime=true` / `HasMerarbejde=false`, all 4 supplements enabled (load-bearing rates 1.25/1.50/1.50/2.0/2.0), on-call + call-in enabled, `EmployeeCompensationChoice=true`, `RestPeriodDerogationAllowed=true`, lower flex caps (100h vs AC's 150h). Three documentation patterns added in this task: (1) explicit "DIVERGENT from AC" annotation via cross-row-ID reference, (2) "LOAD-BEARING in HK" marker paired with AC-inert cross-ref, (3) **explicit-absence row** (SR-HK-OK24-031) documenting "no HK position overrides — verified intentional, not missing by oversight". **One new candidate bug** surfaced: `OvertimeRequiresPreApproval=false` for HK (SR-HK-OK24-022) — likely Phase B-priority because HK's real overtime regime may require pre-approval per cirkulær (seed default carried through without per-agreement consideration in S17). SENIOR_DAY paired bug from TASK-3601 inherits to HK (SR-HK-OK24-029); now confirmed cross-agreement (3 of 3 base agreements affected uniformly). Per-task detail in PLAN-s36.md.
- **TASK-3604** — Complete PROSA OK24 + OK26 source register entries
- **TASK-3605** — Complete AC_RESEARCH + AC_TEACHING source register entries

Per-task detail in PLAN-s36.md. Each commit lands one agreement's full OK24 + OK26 cells. Parallel dispatch deferred — domain-knowledge correctness > throughput in the last-free-correction window.

### Phase 3 — Supporting Audit Docs (2 sequential tasks)

- **TASK-3606** — Role dimension audit doc (`docs/references/role-dimension-audit.md`); per-agreement within-OK role enumeration; AC chefkonsulent's no-merarbejde-entitlement explicitly flagged as production-incorrect
- **TASK-3607** — Agreement ruleset audit doc (`docs/references/agreement-ruleset-audit.md`); 3-column comparison (code | seed | source) with classification: MATCH / DRIFT-IN-CODE / DRIFT-IN-SEED / DRIFT-IN-SOURCE / DRIFT-UNCLEAR

Per-task detail in PLAN-s36.md.

### Phase 4 — Existing Doc Cross-Reference (1 task)

- **TASK-3608** — `danish-agreements.md` cross-reference update. Add source-register row IDs to existing cells. No prose rewrite (deferred to S41 TASK-4106).

### Phase 5 — Phase B Kickoff + Sprint Close (2 tasks)

- **TASK-3609** — Phase B kickoff packaging (`docs/references/phase-b-handoff-package.md`); domain-expert handoff package with review-form template
- **TASK-3610** — Sprint close (SPRINT-36.md close sections + INDEX.md finalization + ROADMAP Phase 4e Phase A pass 1 marked COMPLETE + MEMORY.md S36 line)

Per-task detail in PLAN-s36.md.

## Legal & Payroll Verification (TASK-3610)

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | INVENTORY-IN-PROGRESS | Source register fill is the first systematic comparison of code vs seed vs source. Phase B + S37 finalize sign-off. |
| Wage type mappings produce correct SLS codes | N/A | No mapping changes in S36 (deferred to S40 cutover). |
| Overtime/supplement calculations are deterministic | N/A | No rule changes. |
| Absence effects on norm/flex/pension are correct | N/A | No absence-rule changes. |
| Retroactive recalculation produces stable results | N/A | No rule-engine input surface changes. |

## External Review (Step 7a-equivalent)

**SKIP** per S28 / S32 design-only precedent. The relevant review checkpoint for source-register correctness is Phase B domain-expert validation (PROGRAM L88–101 + S37 absorption). Source-register entries are claims about cirkulær text — outside the architectural defect classes Codex / Reviewer cover.

## Test Summary (TASK-3610)

Per `sprint-test-validation` skill: **SKIP with rationale** — design-only sprint; no code surface; no test totals shift.

| Suite | S35 close | S36 projected | Delta |
|-------|-----------|---------------|-------|
| Unit | 526 | 526 | 0 |
| Plain regression | 35 | 35 | 0 |
| Docker-gated passing | 218 | 218 | 0 |
| Frontend vitest | 90 | 90 | 0 |
| **Total** | **869** | **869** | **0** |

## Critical-Path Callouts

1. **Sprint type is DESIGN-ONLY** — no code surface; many WORKFLOW.md checklist items don't apply (S28 / S32 precedent).
2. **PROGRAM-s36-s41-domain-correctness.md is the de facto refinement** — no separate refinement file. The PROGRAM doc absorbed dual-lens scrutiny during S35 cycle-1 absorption.
3. **Phase B is a parallel workstream** — domain-expert candidate identification started S35 close week (per PROGRAM L101); engagement target = week 2 of S36 once register has draft AC OK24 entries. Phase B feedback is absorbed in S37, not S36.
4. **S36 surfaces candidate bugs; S36 does NOT fix them** — any DRIFT-IN-SEED / DRIFT-IN-CODE discoveries flagged in register / ruleset-audit; routed through S37 or S39 absorption per rule correction policy classification governance.
5. **Schema is fixed by PROGRAM L51–67 but speculatively-authored** — TASK-3601 is the first real test. If filling 20 cells reveals the 13-column schema doesn't fit, halt + propose extension.
6. **No worktree dispatch** — every task Orchestrator-direct sequential. Closes the S24 / S33 / S34 worktree-base-mismatch class entirely for this sprint.

## ROADMAP / Program Cross-References

- ROADMAP "Deployment Model" (L16–27): single logical deployment, 150 institutions, glocal rule encoding, rule correction policy
- ROADMAP Phase 4e bullets: S36 = Phase A inventory pass 1
- PROGRAM-s36-s41-domain-correctness.md: granular execution plan (S36 = its TASK-3600..3610; S37 = absorption; S38 = ADRs; S39 = schema; S40 = cutover; S41 = exhaustive tests + governance bake-in)

## Phase B Engagement Status (running tracker — updated by TASK-3609 and S37)

| Field | Status |
|-------|--------|
| Candidate identification | started 2026-05-20 (S35 close week per PROGRAM L101) |
| Candidate(s) selected | _pending_ |
| Engagement window | targeted week 2 of S36 |
| Phase B feedback ETA | S37 sprint start |

---

_Updated at sprint close (TASK-3610): outcomes summary, finding counts per agreement, candidate-bug list for S37, commit list, sprint duration, MEMORY.md entry._
