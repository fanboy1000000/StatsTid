# Sprint 36 — Phase A Inventory Sprint 1 (DESIGN-ONLY)

| Field | Value |
|-------|-------|
| **Sprint** | 36 |
| **Status** | **complete** |
| **Start Date** | 2026-05-21 |
| **End Date** | 2026-05-21 |
| **Orchestrator Approved** | yes — 2026-05-21 |
| **Build Verified** | N/A — design-only sprint; no code changes; no `dotnet build` verification needed (S28 / S32 precedent) |
| **Test Verified** | N/A — design-only sprint; test totals unchanged from S35 close (869 total = 526 unit + 35 plain regression + 218 Docker-gated + 90 frontend). `sprint-test-validation` skill SKIP with rationale per design-only sprint contract. |
| **Sprint-end HEAD** | _filled by close commit_ |
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

Final assertion (TASK-3610):

- [x] **P1 — Architectural integrity** → No code touched; no schema touched. Reference docs follow existing `docs/references/` convention. PROGRAM file cross-references stay sound across all 5 produced/updated docs.
- [x] **P2 — Rule engine determinism** → No rule changes. Source register documents intended encoding for downstream verification in S39 Phase E seed-parity tests. AC chefkonsulent gap explicitly flagged as production-incorrect — fix lands S38–S40 per ROADMAP rule correction policy.
- [x] **P3 — Event sourcing / auditability** → No event surface changes. Source register becomes the audit-trail artifact for "what cell value was authoritative at what date." `bug_correction_history` + `supersession_history` columns provide per-cell audit trail (S35 AC=AFSPADSERING recorded as first entry).
- [x] **P4 — Version correctness** → Each register cell carries `ok_version` discriminator. OK26 placeholder bundle pattern preserves traceability across OK transitions; per-cell divergence rows added when OK26 cirkulær publishes.
- [x] **P5–P9** → Not applicable in design-only sprint (confirmed at close).

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
- **TASK-3604** — Complete PROSA OK24 + OK26 source register entries. **completed** (this commit). 9 new PROSA OK24 rows (SR-PROSA-OK24-001..009) using compact "mirrors HK" bundle form + 4 PROSA OK26 placeholder bundles (SR-PROSA-OK26-001..004). PROSA is structurally HK-cloned per init.sql:1158 vs L1146 with 3 specific divergences: MaxFlexBalance=120h (vs HK's 100h), FlexCarryoverMax=120h, CHILD_SICK quota=3 (vs HK's 2). New documentation pattern: **cross-agreement mirror bundle with exclusion list** — bundle covers "all cells except [enumerated divergent / candidate-bug list]" with explicit cross-row-ID references to HK counterparts. Both inherited candidate bugs (SENIOR_DAY paired + OvertimeRequiresPreApproval) now confirmed cross-agreement across all 3 base agreements; AC variants in TASK-3605 likely inherit. Per-task detail in PLAN-s36.md.
- **TASK-3605** — Complete AC_RESEARCH + AC_TEACHING source register entries. **completed** (this commit). 8 AC_RESEARCH OK24 rows (SR-AC_RESEARCH-OK24-001..008) + 7 AC_TEACHING OK24 rows (SR-AC_TEACHING-OK24-001..007) + 4 AC_RESEARCH OK26 placeholders + 1 compound AC_TEACHING OK26 placeholder (further compaction). Both variants AC-cloned with norm-model divergence: `NormModel=ANNUAL_ACTIVITY` + AC_RESEARCH `AnnualNormHours=1924` / AC_TEACHING `AnnualNormHours=1680`. **TWO MAJOR new candidate bugs surfaced**: (a) AC variants have NO `entitlement_configs` rows (init.sql:1343-1378 seeds only AC/HK/PROSA — likely structural gap), (b) AC variants' wage_type_mappings use divergent SLS codes on 6 of 11 time types from AC base (notably MERARBEJDE→SLS_0210 collides with HK/PROSA OVERTIME_50). Both flagged Phase B HIGH priority. Per-task detail in PLAN-s36.md.

Per-task detail in PLAN-s36.md. Each commit lands one agreement's full OK24 + OK26 cells. Parallel dispatch deferred — domain-knowledge correctness > throughput in the last-free-correction window.

### Phase 3 — Supporting Audit Docs (2 sequential tasks)

- **TASK-3606** — Role dimension audit doc (`docs/references/role-dimension-audit.md`). **completed** (this commit). Per-agreement within-OK role enumeration with source-register cross-references + production-incorrectness call-out for AC chefkonsulent (no merarbejde per cirkulær; current encoding emits MERARBEJDE events) + AC kontorchef (managerial; same gap) + AC specialkonsulent (direction pending Phase B). Vestigial `User.EmploymentCategory` analysis confirmed: `grep -r EmploymentCategory src/RuleEngine/` returns 0 matches — field is set + surfaced in admin endpoints but never branches a rule. `PositionOverrideConfigs` 4-quantitative-field schema gap analysis enumerates 6 missing override capabilities (no entitlement-toggle, no compensation-model override, etc.). Forward pointers to S38 ADR-024 D1 (role placement: 3 options) + D2 (tri-state `MerarbejdeCompensationRight`) + S39 schema migration + S40 cutover + S41 D-test matrix. Phase B sign-off tracking table with 9 enumerated cells. Per-task detail in PLAN-s36.md.
- **TASK-3607** — Agreement ruleset audit doc (`docs/references/agreement-ruleset-audit.md`). **completed** (this commit). 3-column comparison across all 5 agreements × both OK versions with classification summary. Provisional counts: ~25 MATCH (HIGH-confidence cells — EU-derived + Ferieloven + S35-corrected AC=AFSPADSERING) + ~80 MATCH-PENDING-SOURCE (dominant case; flips MATCH on Phase B) + **0 DRIFT-IN-CODE** (code ↔ seed byte-equivalence verified) + **4 DRIFT-IN-SEED / DRIFT-IN-SOURCE candidates** + ~5 DRIFT-UNCLEAR. All 4 candidate bugs routed to S37 seed correction or S38 ADR-024 absorption depending on Phase B finding direction. Candidate Bug Routing Summary table maps each candidate to specific sprint + class. Per-task detail in PLAN-s36.md.

Per-task detail in PLAN-s36.md.

### Phase 4 — Existing Doc Cross-Reference (1 task)

- **TASK-3608** — `danish-agreements.md` cross-reference update. **completed** (this commit). SR row references added inline as "SR rows" columns to 7 tables (Key Behavioral Differences / Supplement Time Windows / Overtime Thresholds / Compensation Model / Entitlement Quotas / Position Overrides + per-agreement bundle SR refs above wage type mappings sections). Header note added cross-referencing the 3 S36-produced docs (source-register + role-dimension-audit + ruleset-audit). No prose rewriting beyond the row-ID insertion and brief candidate-bug call-outs (SENIOR_DAY paired-bug; AC variants entitlement gap; AC variants SLS code divergence; MERARBEJDE SLS_0210 collision). Compensation Model section gets SR rows column matching S35 TASK-3504 addition. Full prose rewrite deferred to S41 TASK-4106.

### Phase 5 — Phase B Kickoff + Sprint Close (2 tasks)

- **TASK-3609** — Phase B kickoff packaging (`docs/references/phase-b-handoff-package.md`). **completed** (this commit). Domain-expert handoff package with 4-tier priority sequencing (Tier 1 = 4 candidate bugs requiring direction-adjudication; Tier 2 = ~8 MEDIUM-HIGH baseline cells; Tier 3 = ~80 MATCH-PENDING-SOURCE cells; Tier 4 = ~5 DRIFT-UNCLEAR). Per-cell review-form template + bulk-review shortcut for MATCH cells. Candidate domain-expert profile table (6 expert types: internal customer HR/payroll + union consultants per agreement + retired Personalestyrelsen + external legal counsel). Recommended engagement model (primary customer reviewer + secondary union consultants in parallel + dispute resolution per ROADMAP employer-side default). Engagement protocol per PROGRAM L94-99. Tracking dashboard for Phase B status. Forward pointers to S37 absorption tasks (TASK-3700..3709). Per-task detail in PLAN-s36.md.
- **TASK-3610** — Sprint close. **completed** (this commit). SPRINT-36.md close sections filled with outcomes summary + candidate-bug inventory + documentation-patterns-surfaced list + 11-commit list + forward pointers + Phase B engagement-status tracker. INDEX.md S36 row flipped to complete. ROADMAP Phase 4e Phase A pass 1 marked COMPLETE. MEMORY.md S36 summary added. Per-task detail in PLAN-s36.md.

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

## Sprint Close (TASK-3610)

### Outcomes Summary

5 docs produced or updated across `docs/references/`:

| Doc | Status | Cell count / scope |
|-----|--------|--------------------|
| `agreement-source-register.md` (NEW) | DRAFT — awaits Phase B sign-off | **111 cells** across 5 agreements × 2 OK versions: AC OK24=39, AC OK26=4 bundles, HK OK24=31, HK OK26=4 bundles, PROSA OK24=9 (mirrors-HK form), PROSA OK26=4 bundles, AC_RESEARCH OK24=8 (mirrors-AC form), AC_RESEARCH OK26=4 bundles, AC_TEACHING OK24=7 (mirrors-AC_RESEARCH chain), AC_TEACHING OK26=1 compound 4-domain bundle |
| `role-dimension-audit.md` (NEW) | DRAFT — awaits Phase B 9-cell sign-off | Per-agreement within-OK role enumeration; production-incorrectness call-out for AC chefkonsulent / kontorchef / specialkonsulent; `PositionOverrideConfigs` 6-field schema gap analysis; `User.EmploymentCategory` vestigial confirmation |
| `agreement-ruleset-audit.md` (NEW) | DRAFT — awaits Phase B | 3-column code/seed/source comparison; classification summary (~25 MATCH / ~80 MATCH-PENDING-SOURCE / 0 DRIFT-IN-CODE / 4 candidate DRIFT-IN-SEED-or-SOURCE / ~5 DRIFT-UNCLEAR / 1 RESOLVED S35); Candidate Bug Routing Summary |
| `danish-agreements.md` (UPDATE) | UPDATED | SR row references added to 7 tables (no prose rewrite); header gains cross-references block to 3 S36-produced docs; full prose rewrite deferred to S41 TASK-4106 |
| `phase-b-handoff-package.md` (NEW) | READY FOR DISPATCH | 4-tier priority sequencing (Tier 1 = 4 candidate bugs blocking S37 absorption); candidate domain-expert profile (6 types); recommended primary + secondary engagement model; per-cell review-form template (Sections A–D + bulk-review shortcut); tracking dashboard |

### Candidate-Bug Inventory (4 surfaced for S37 / S38 routing)

| # | Candidate bug | Affected SR rows | S37/S38 path |
|---|---------------|-------------------|----------------|
| 1 | AC variants missing entitlement_configs rows (20 missing rows for VACATION + SPECIAL_HOLIDAY + CARE_DAY + CHILD_SICK + SENIOR_DAY across AC_RESEARCH + AC_TEACHING × OK24 + OK26) | SR-AC_RESEARCH-OK24-005 + SR-AC_TEACHING-OK24-005 + OK26 inheritance | S37 mechanical seed correction if Phase B confirms verbatim inheritance from AC base |
| 2 | AC variants wage_type_mappings divergent SLS codes on 6 of 11 time types; MERARBEJDE → SLS_0210 **collides with HK/PROSA OVERTIME_50 → SLS_0210** | SR-AC_RESEARCH-OK24-006 + SR-AC_TEACHING-OK24-006 | S37 (if S11 authoring bug) or S38 ADR-024 D6 (if intentional separate code-block needing SLS reconciliation) |
| 3 | SENIOR_DAY paired bug — `annual_quota=0` + `min_age=60` structurally inconsistent; same encoding across 3 base agreements per init.sql:1373–1378 | SR-AC-OK24-015 + 035 + SR-HK-OK24-029 + SR-PROSA-OK24-006 + AC variant inheritance via SR-AC_RESEARCH-OK24-008 | S37 (seed quota fix), S39 (rule logic), or S40 cutover depending on Phase B path selection |
| 4 | HK + PROSA `OvertimeRequiresPreApproval=false` — seed default carried through S17 without per-agreement consideration | SR-HK-OK24-022 + SR-PROSA-OK24-007 | S37 (if cirkulær-mandated, flip to TRUE) or MATCH on no-op |

Plus 1 RESOLVED historical DRIFT-IN-SEED recorded (S35 AC=AFSPADSERING in SR-AC-OK24-005 `bug_correction_history`).

### Documentation Patterns Surfaced

Six register-formatting patterns emerged during the per-agreement fill, all schema-compatible without changes:

1. **15-column schema with `row_id` + `notes` first-class** (TASK-3601 enhancement over PROGRAM L51–67 13-column nominal)
2. **`confidence_level = N/A-for-agreement` for inert cells** (AC supplement-disabled rates; TASK-3601)
3. **Compound cells** via string-joined `field` (`NormModel + NormPeriodWeeks + AnnualNormHours` group; 11-column supplement bundles; 17-mapping wage-type bundles; TASK-3601 → TASK-3602)
4. **"DIVERGENT from X" annotation via cross-row-ID reference** + **"LOAD-BEARING in X (inert in Y)" pairing** (TASK-3603)
5. **Cross-agreement mirror bundle with exclusion list** for structurally-cloned agreements (PROSA "mirrors HK"; AC_RESEARCH "mirrors AC"; AC_TEACHING "mirrors AC_RESEARCH" chain; TASK-3604 → TASK-3605)
6. **Explicit-absence row** for "no rows in this domain — verified intentional, not missing by oversight" (TASK-3603 → TASK-3605)

These patterns extend the schema's reach without requiring per-task amendments. PROGRAM L51–67 13-column nominal stands; explicit `row_id` + `notes` made first-class in-file.

### Commit List (10 commits across S36)

```
f253646 S36 TASK-3600: sprint open — Phase A inventory pass 1 (DESIGN-ONLY)
f27b3f0 S36 TASK-3601: source register skeleton + AC OK24 proof-of-shape (20 cells)
dea9eb9 S36 TASK-3602: AC OK24 + OK26 source register entries complete
403038e S36 TASK-3603: HK OK24 + OK26 source register entries complete
e2113b9 S36 TASK-3604: PROSA OK24 + OK26 source register entries complete
b5f5aa6 S36 TASK-3605: AC_RESEARCH + AC_TEACHING OK24 + OK26 entries complete
94f2e41 S36 TASK-3606: role dimension audit doc
821f1f2 S36 TASK-3607: agreement ruleset audit doc (code vs seed vs source)
04c2f42 S36 TASK-3608: danish-agreements.md cross-references to source register
a049f7c S36 TASK-3609: Phase B kickoff packaging
[this commit] S36 TASK-3610: sprint close — Phase A inventory pass 1 complete
```

### Forward Pointers

- **S37 Phase A finalization (per PROGRAM L105–117)** — domain-expert sign-off absorption; 4 candidate bugs adjudicated; source register status → APPROVED; bug-with-no-past-impact seed corrections shipped if Phase B confirms direction
- **S38 Phase C ADR authorship (per PROGRAM L121–164)** — ADR-024 (role-within-agreement modeling + correction policy + classification governance, 6 decisions D1-D6) + ADR-025 (multi-tenant operational concerns, 8 decisions D1-D8) + ADR-013 amendment
- **S39 schema migration + Phase E continuous-validation tests** — `role_within_agreement_configs` table + seed-parity tests + "unknown unknown" tests + DRAFT-OK rule enforcement
- **S40 cutover** — `ConfigResolutionService` + rule engine + payroll mapping read role layer
- **S41 exhaustive D-test matrix + danish-agreements.md rewrite + WORKFLOW.md OK-transition checklist + QUALITY.md re-grade**

### Phase B Engagement Status (handoff to S37)

| Field | Status at S36 close |
|-------|---------------------|
| Candidate identification | started 2026-05-20 (S35 close week per PROGRAM L101) |
| Candidate(s) selected | _pending — to be filled before S37 sprint open_ |
| Engagement window | week 2 of S36 — package READY (commit `a049f7c`) |
| Phase B feedback ETA | S37 sprint start (per PROGRAM L100) |
| Source register status | DRAFT (target APPROVED at S37 close per PROGRAM L114) |

