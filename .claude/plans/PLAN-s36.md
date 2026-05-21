# PLAN — Sprint 36: Phase A Inventory Sprint 1 (DESIGN-ONLY)

## Sprint Header

| Field | Value |
|-------|-------|
| **Sprint** | 36 |
| **Title** | Phase A Inventory Sprint 1 — Agreement Source Register + Role Dimension Audit + Ruleset Audit (AC / HK / PROSA / AC_RESEARCH / AC_TEACHING) |
| **Status** | DRAFT (Step 0b SKIP rationale below — design-only sprint following S28 / S32 precedent) |
| **Start Date** | 2026-05-21 |
| **Projected End Date** | 2026-05-25..28 (per-agreement audit cadence; Phase B candidate engagement kicks off week 2) |
| **Sprint-start base commit** | `a094630` (post-S35 governance commit, 2026-05-20 — cap-fires-after-verification + post-7a-coverage governance edits) |
| **Sprint type** | **DESIGN-ONLY** — produces 3 NEW reference docs + 1 doc UPDATE. No code changes. No test changes. Test counts unchanged from S35 close (869 total). Mirrors S28 / S32 design-only pattern (sprint produces ADRs / docs; implementation lives in subsequent sprint). |
| **Refinement** | Not filed. The PROGRAM file `.claude/plans/PROGRAM-s36-s41-domain-correctness.md` is the de facto refinement artifact — its design has already absorbed dual-lens scrutiny during S35 cycle-1 absorption (Codex BLOCKERs 1–3 + Reviewer BLOCKER 1 were absorbed into the program structure as committed 2026-05-18). |
| **Agents involved** | Orchestrator-direct (KB / docs writes per WORKFLOW.md L48 — docs are Orchestrator-only). No domain agents dispatched — the source-register fill is research + transcription work that requires architectural judgment (which authoritative source counts; how to phrase interpretation cells) and lives in scope-restricted reference docs (`docs/references/`). |
| **KB entries planned** | None. (Reference docs at `docs/references/` are not KB entries per `docs/knowledge-base/INDEX.md` taxonomy; KB candidates ADR-024 + ADR-025 + ADR-013 amendment land in S38 per PROGRAM file.) |
| **Phase B kickoff** | Parallel workstream — domain-expert candidate identification starts this sprint; engagement target = week 2 of S36 once register has draft AC OK24 entries to review. |

## Sprint Goal

Produce a **comprehensive source register** + 2 supporting audit docs for the 5 production agreement codes (AC / HK / PROSA / AC_RESEARCH / AC_TEACHING) across OK24 + OK26. This sprint is the **first concrete execution of the Phase A inventory commitment** the PROGRAM-s36-s41 file enumerates (PROGRAM L41–84).

**Strategic context**: pre-launch posture is load-bearing. Every bug we find here ships as a free seed correction (continuing the policy first applied in S35 AC=UDBETALING). Every bug we miss becomes a post-launch supersession event with workflow overhead per ROADMAP rule correction policy (committed 2026-05-18). The source register makes every cell in the agreement / role / OK matrix traceable to a cited paragraph in an authoritative cirkulær, plus a confidence + decider + verification date column — closing the systemic gap the AC seed bug exposed (encoding drift from cirkulærer with no process catching it).

**Deliverables (3 NEW docs + 1 UPDATE)**:

1. **`docs/references/agreement-source-register.md`** (NEW) — machine-readable traceability table; 13-column schema fixed by PROGRAM L51–67.
2. **`docs/references/role-dimension-audit.md`** (NEW) — within-OK role enumeration per agreement (fuldmægtig / specialkonsulent / chefkonsulent / department_head / researcher for AC; HK / PROSA strata; AC_RESEARCH / AC_TEACHING variants).
3. **`docs/references/agreement-ruleset-audit.md`** (NEW) — 3-column comparison (current code value | current init.sql seed value | authoritative source value) per active config cell.
4. **`docs/references/danish-agreements.md`** (UPDATE) — cite source register row IDs in existing cells; full prose rewrite deferred to S41 TASK-4106.

**Out-of-scope for S36** (deferred per PROGRAM file):
- ADR authorship (ADR-024 + ADR-025 + ADR-013 amendment) → S38
- Schema migration for `role_within_agreement_configs` → S39
- ConfigResolutionService / rule engine / payroll cutover → S40
- Exhaustive D-test matrix + `danish-agreements.md` rewrite → S41
- Bug correction workflow ADR-027 → post-launch (per ROADMAP L25)

## Phase Decomposition

Per-agreement sequential — register skeleton lands first so per-agreement entries share a stable shape; audit ordering follows PROGRAM L69 priority (AC first; HK + PROSA next; AC_RESEARCH + AC_TEACHING last). The 3 deliverable docs are largely independent after Phase 1 lands the schema; Phase 2 fills the register agreement-by-agreement; Phase 3 produces the 2 supporting audit docs from the register data.

| Phase | Tasks | Dispatch model |
|-------|-------|---------------|
| 0 | TASK-3600 | Orchestrator-direct — this file + SPRINT-36.md + INDEX.md provisional + commit |
| 1 | TASK-3601 | Orchestrator-direct — source register skeleton + first 20 cells from AC OK24 (proof-of-shape; validates 13-column schema works on real cells before per-agreement fill begins) |
| 2 | TASK-3602..3605 | Sequential per-agreement — AC complete (3602) → HK (3603) → PROSA (3604) → AC_RESEARCH + AC_TEACHING (3605). Each commit lands one agreement's full OK24 + OK26 cells. Parallel dispatch deferred — domain-knowledge correctness > throughput in this last-free-correction window. |
| 3 | TASK-3606, TASK-3607 | Orchestrator-direct sequential — role dimension audit (3606) then ruleset audit (3607). Both depend on register being complete. |
| 4 | TASK-3608 | Orchestrator-direct — `danish-agreements.md` cross-reference update (cite source register row IDs in existing cells; no prose rewrite — that's S41) |
| 5 | TASK-3609, TASK-3610 | Orchestrator-direct — Phase B kickoff packaging (3609) + sprint close (3610) |

**No worktree-base-mismatch risk** — all tasks Orchestrator-direct sequential; no parallel agent dispatches.

## Step 0a — Entropy Scan Findings

Run 2026-05-21 at sprint open per WORKFLOW.md Step 0a:

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ROADMAP Deployment Model + Phase 4e bullets + rule correction policy all resolve cleanly post-S35; PROGRAM-s36-s41-domain-correctness.md cross-references settle. |
| Pattern compliance | CLEAN | Design-only sprint follows S28 / S32 precedent: docs-only output, no code surface, no schema surface, no test surface. |
| Orphan detection | DEBT (carry-forward from S34 / S35) | 80+ stale locked agent worktrees under `.claude/worktrees/`; S36 uses Orchestrator-direct dispatch only so non-blocking. Operational housekeeping remains Phase 4e backlog (deferred from S35). |
| Documentation drift | NONE | `docs/references/danish-agreements.md` already cited as the doc requiring TASK-3608 cross-reference update; no other doc drift detected at sprint open. |
| Quality grade review | DEFERRED to S41 TASK-4108 | New "Domain Correctness" category lands at S41 close per PROGRAM L215. S36 close emits no QUALITY.md change (design-only sprint). |
| Refinement disposition | N/A | No fresh refinement file — PROGRAM-s36-s41-domain-correctness.md is the de facto refinement artifact, dual-lens-absorbed during S35. |

## Step 0b — Plan Review

| Field | Value |
|-------|-------|
| **Trigger** | **SKIP** per S28 / S32 design-only precedent (AGENTS.md L307 + SPRINT-32.md L52). No implementation surface. No schema change. No test change. The architectural decisions are settled by PROGRAM-s36-s41-domain-correctness.md, which already absorbed dual-lens scrutiny during S35 cycle-1 absorption. |
| **External Codex** | not invoked at Step 0b. (No Step 7a-equivalent at sprint close either — register quality is validated by Phase B domain-expert review in S37, not by code-review lenses. Source register entries are claims about cirkulær text — outside the architectural defect classes Codex / Reviewer cover.) |
| **Internal Reviewer** | not invoked at Step 0b for the same reason. |
| **BLOCKERs resolved before Phase 1** | n/a — Step 0b SKIP. |

### Resolution

Step 0b SKIP rationale documented. Plan READY for Phase 1 dispatch.

The relevant review checkpoint for design-only inventory work is **Phase B domain-expert validation** (PROGRAM L88–101 + S37 absorption tasks), not Codex / Reviewer dual-lens. S37 is where the inventory's correctness is adjudicated. S36 produces the artifacts; S37 reconciles them against expert feedback.

## Architectural Constraints Verified

_Checked off as the sprint progresses; final assertion in TASK-3610._

- [ ] **P1 — Architectural integrity** → No code touched; no schema touched. Reference doc additions follow existing `docs/references/` convention. PROGRAM file cross-references stay sound.
- [ ] **P2 — Rule engine determinism** → No rule changes. Source register documents the *intended* encoding for downstream verification in S39 Phase E seed-parity tests; no encoding shift in S36.
- [ ] **P3 — Event sourcing / auditability** → No event surface changes. Source register itself becomes the audit-trail artifact for "what cell value was authoritative at what date."
- [ ] **P4 — Version correctness** → Each register cell carries `ok_version` discriminator + `supersession_history` column; multi-OK reasoning is baked into the schema from row one.
- [ ] **P5 — Integration isolation** → Not applicable in design-only sprint.
- [ ] **P6 — Payroll integration correctness** → Not applicable in design-only sprint. (S37 may discover seed-vs-source mismatches that affect payroll mapping correctness; those file as bug-with-no-past-impact pre-launch corrections in S37 or S39.)
- [ ] **P7 — Security and access control** → Not applicable in design-only sprint.
- [ ] **P8 — CI/CD enforcement** → Not applicable in design-only sprint.
- [ ] **P9 — Usability and UX** → Not applicable in design-only sprint.

---

## Task Log

### Phase 0 — Sprint Open (1 task)

#### TASK-3600 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3600 |
| **Status** | completed (commit `f253646`) |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s36.md` (this file), `docs/sprints/SPRINT-36.md`, `docs/sprints/INDEX.md` (provisional row) |
| **Dependencies** | none |
| **KB Refs** | ROADMAP Deployment Model (L16–27) + Phase 4e bullets + rule correction policy + PROGRAM-s36-s41-domain-correctness.md |

**Validation Criteria**:
- [x] PLAN-s36.md filed with full task log + Step 0a + Step 0b SKIP sections (this file)
- [x] SPRINT-36.md provisional entry created
- [x] INDEX.md gains S36 row (status: in-progress; dates: 2026-05-21 → ?; tests: 869 baseline projected unchanged)
- [x] Sprint-open commit lands atop `a094630` with message "S36 TASK-3600: sprint open — Phase A inventory pass 1 (DESIGN-ONLY)"

---

### Phase 1 — Source Register Skeleton + Proof-of-Shape (1 task)

#### TASK-3601 — Source register skeleton + first 20 cells from AC OK24

| Field | Value |
|-------|-------|
| **ID** | TASK-3601 |
| **Status** | completed (this commit) |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/references/agreement-source-register.md` (NEW) |
| **Dependencies** | TASK-3600 |
| **KB Refs** | PROGRAM L51–84 (13-column schema + audit ordering) |

**Scope**:
1. Create `docs/references/agreement-source-register.md` with a header section documenting:
   - Schema (13 columns per PROGRAM L51–67)
   - Audit ordering (AC first; HK + PROSA next; AC_RESEARCH + AC_TEACHING last)
   - Source-citation conventions (PDF URL + paragraph format; e.g., `https://oes.dk/media/ik0hm2lr/043-19.pdf §4.2`)
   - Confidence-level definitions (HIGH = explicit cirkulær statement; MEDIUM = inferred-with-strong-precedent; LOW = ambiguous / contested)
   - Cross-reference to PROGRAM-s36-s41 + ROADMAP rule correction policy
2. Populate the first **20 cells** for AC OK24 to validate the 13-column schema works on real data before the per-agreement fill (TASK-3602..3605) begins. Cells to cover (proof-of-shape — distinct cell shapes, not depth):
   - 4 cells across quantitative numeric fields (`WeeklyNormHours`, `MaxOvertimeHoursPerPeriod`, `MinimumRestHours`, `WeeklyMaxHoursReferencePeriod`)
   - 4 cells across enum / categorical fields (`DefaultCompensationModel` — note this is the AC=AFSPADSERING corrected value per S35 TASK-3503; `EmployeeCompensationChoice` boolean; `HasMerarbejde` boolean; `OvertimeRequiresPreApproval` boolean)
   - 4 cells across rate / multiplier fields (overtime supplement rate 50%/100%; flex conversion ratio; norm-deviation tolerance)
   - 4 cells across entitlement fields (vacation days quota; care days quota; senior days quota; child sick days policy)
   - 4 cells across compliance / governance fields (rest period derogation flag; daily max hours; weekly rest day requirement; norm-period model annual vs weekly)

**Validation Criteria**:
- [ ] `docs/references/agreement-source-register.md` exists with header section + 13-column schema documented
- [ ] 20 AC OK24 cells populated with all 13 columns filled (entries with `last_verified_by = pending` + `decision_date = pending` are acceptable — Phase B fills these in S37)
- [ ] At least 12 of the 20 cells cite a specific cirkulær paragraph (HIGH confidence); the rest can be MEDIUM / LOW with explicit rationale
- [ ] At least 1 cell records a `disputed?` = true scenario if surfaced (per ROADMAP glocal principle, default to Personalestyrelsen-side interpretation; record dispute explicitly)
- [ ] AC=AFSPADSERING (corrected from UDBETALING per S35 TASK-3503) is one of the 20 cells, with `bug_correction_history` populated citing commit `5286152` (S35 close)
- [ ] Commit message: "S36 TASK-3601: source register skeleton + AC OK24 proof-of-shape (20 cells)"

**Failure mode**: if filling the 20 cells reveals the 13-column schema doesn't fit (e.g., a cell needs a column not enumerated), halt + propose schema extension before continuing. The schema is fixed by PROGRAM L51–67 but the program file authored that schema speculatively — TASK-3601 is the first real test.

---

### Phase 2 — Per-Agreement Fill (4 tasks, sequential)

#### TASK-3602 — Complete AC OK24 + OK26 source register entries

| Field | Value |
|-------|-------|
| **ID** | TASK-3602 |
| **Status** | completed (this commit) |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/references/agreement-source-register.md` (extend) |
| **Dependencies** | TASK-3601 |

**Scope**: Complete every active config cell for AC across both OK versions. Reference sources: `CentralAgreementConfigs.cs` (code) + `docker/postgres/init.sql` AC seed rows (DB seed) + AC overenskomst cirkulær PDFs.

**Validation Criteria**:
- [ ] All AC OK24 cells from `CentralAgreementConfigs.cs` lines covering AC have register rows
- [ ] All AC OK26 cells from `CentralAgreementConfigs.cs` + init.sql AC seed rows have register rows
- [ ] Any code-vs-seed-vs-source discrepancy beyond the AC=UDBETALING-resolved bug is **flagged in the row** with `disputed? = true` or `bug_correction_history = candidate-discovery-{date}` — these become candidate S37 bug-fix tasks
- [ ] Confidence levels assigned per cell (HIGH / MEDIUM / LOW)
- [ ] Commit message: "S36 TASK-3602: AC OK24 + OK26 source register entries complete"

**Expected discovery**: per PROGRAM L84, source register fill surfaces additional bugs beyond AC=UDBETALING. Any HIGH-confidence mismatch lands as a candidate bug-with-no-past-impact correction for S37 absorption. Do NOT correct seeds in S36 — surface them in the register and route through the classification governance in S37 / S38.

---

#### TASK-3603 — Complete HK OK24 + OK26 source register entries

| Field | Value |
|-------|-------|
| **ID** | TASK-3603 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/references/agreement-source-register.md` (extend) |
| **Dependencies** | TASK-3602 |

**Scope**: same shape as TASK-3602 but for HK. HK has its own cirkulær separate from AC; sources must reflect that.

**Validation Criteria**:
- [ ] All HK OK24 + OK26 cells from `CentralAgreementConfigs.cs` + init.sql HK seed rows have register rows
- [ ] HK-specific cells (different from AC) explicitly noted with cell-shape rationale
- [ ] Confidence levels assigned per cell
- [ ] Any HK-side bug candidates flagged in the row
- [ ] Commit message: "S36 TASK-3603: HK OK24 + OK26 source register entries complete"

---

#### TASK-3604 — Complete PROSA OK24 + OK26 source register entries

| Field | Value |
|-------|-------|
| **ID** | TASK-3604 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/references/agreement-source-register.md` (extend) |
| **Dependencies** | TASK-3603 |

**Scope**: same shape, for PROSA. PROSA covers IT-staff in state institutions — distinct cirkulær.

**Validation Criteria**:
- [ ] All PROSA OK24 + OK26 cells covered
- [ ] PROSA-specific cells explicitly noted (e.g., on-call rates may differ)
- [ ] Confidence levels assigned per cell
- [ ] Any PROSA-side bug candidates flagged
- [ ] Commit message: "S36 TASK-3604: PROSA OK24 + OK26 source register entries complete"

---

#### TASK-3605 — Complete AC_RESEARCH + AC_TEACHING source register entries

| Field | Value |
|-------|-------|
| **ID** | TASK-3605 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/references/agreement-source-register.md` (extend) |
| **Dependencies** | TASK-3604 |

**Scope**: AC_RESEARCH + AC_TEACHING share much of AC's cirkulær base but diverge on annual-norm + special supplements per S11 AnnualActivityRule precedent. Cover both variants.

**Validation Criteria**:
- [ ] All AC_RESEARCH OK24 + OK26 cells covered (annual-norm model + research-supplement-specific cells)
- [ ] All AC_TEACHING OK24 + OK26 cells covered (teaching-norm model + teaching-supplement-specific cells)
- [ ] Per-variant cells explicitly distinguish from base AC inheritance
- [ ] Confidence levels assigned per cell
- [ ] Any variant-side bug candidates flagged
- [ ] Commit message: "S36 TASK-3605: AC_RESEARCH + AC_TEACHING source register entries complete"

---

### Phase 3 — Supporting Audit Docs (2 tasks, sequential)

#### TASK-3606 — Role dimension audit doc

| Field | Value |
|-------|-------|
| **ID** | TASK-3606 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/references/role-dimension-audit.md` (NEW) |
| **Dependencies** | TASK-3605 |

**Scope**: enumerate within-OK roles per agreement, citing source-register row IDs. The doc must include:
- Per-agreement role list (e.g., AC: fuldmægtig / specialkonsulent / chefkonsulent / department_head / researcher)
- Per-role compensation-entitlement summary (current encoding vs cirkulær-stated entitlement)
- **Specific call-out for AC chefkonsulent**: contractually loses merarbejde compensation right per AC cirkulær — current code treats all AC employees identically (vestigial `User.EmploymentCategory` field) — this is the load-bearing finding the PROGRAM L31–32 framed.
- HK + PROSA strata (if any within-OK role distinctions exist)
- AC_RESEARCH + AC_TEACHING role variants (researcher levels; teaching strata)
- Cross-reference: how the existing `PositionOverrideConfigs` schema covers / fails to cover each enumerated role (the 4 quantitative fields can't express "no entitlement" — see PROGRAM L32)

**Validation Criteria**:
- [ ] Every role enumerated cites a source-register row ID
- [ ] AC chefkonsulent's no-merarbejde-entitlement is explicitly flagged as production-incorrect (would emit MERARBEJDE wage type today)
- [ ] PositionOverrideConfigs schema gap analysis included (4-field limitation)
- [ ] Cross-references to upcoming S38 ADR-024 D1 (role dimension placement) and D2 (compensation entitlement tri-state) decisions
- [ ] Commit message: "S36 TASK-3606: role dimension audit doc"

---

#### TASK-3607 — Agreement ruleset audit doc

| Field | Value |
|-------|-------|
| **ID** | TASK-3607 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/references/agreement-ruleset-audit.md` (NEW) |
| **Dependencies** | TASK-3606 |

**Scope**: 3-column comparison table per active config cell:
- Column A: current value in `CentralAgreementConfigs.cs` (in-code default)
- Column B: current value in `docker/postgres/init.sql` seed (DB seed)
- Column C: authoritative source value (per source register)
- Disagreement-row count summary at top
- Per-row classification: MATCH (A=B=C) / DRIFT-IN-CODE (A≠B but B=C) / DRIFT-IN-SEED (B≠C; the S35 AC=UDBETALING category) / DRIFT-IN-SOURCE (all three differ; needs Phase B adjudication) / DRIFT-UNCLEAR (LOW-confidence source)

**Validation Criteria**:
- [ ] Every cell from `CentralAgreementConfigs.cs` is represented as a row
- [ ] Every cell from active init.sql agreement_configs seeds is represented as a row (deduplicated against in-code values where they match)
- [ ] Classification summary at top shows counts per category
- [ ] DRIFT-IN-SEED rows enumerate candidate S37 / S39 bug-with-no-past-impact corrections
- [ ] DRIFT-IN-SOURCE rows flagged for Phase B adjudication
- [ ] Commit message: "S36 TASK-3607: agreement ruleset audit doc (code vs seed vs source comparison)"

---

### Phase 4 — Existing Doc Cross-Reference (1 task)

#### TASK-3608 — `danish-agreements.md` cross-reference update

| Field | Value |
|-------|-------|
| **ID** | TASK-3608 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/references/danish-agreements.md` (UPDATE — cross-reference only; no prose rewrite) |
| **Dependencies** | TASK-3607 |

**Scope**: add source-register row IDs to existing cells in `danish-agreements.md`. No prose rewrite — the full rewrite is S41 TASK-4106. This sprint only adds traceability links so a reader of `danish-agreements.md` can find the authoritative cirkulær citation in the source register.

**Validation Criteria**:
- [ ] Every numeric / categorical claim in `danish-agreements.md` carries a source-register row reference (e.g., `[SR-AC-OK24-001]`)
- [ ] No prose rewriting beyond the row-ID insertion
- [ ] Compensation Model section added in S35 TASK-3504 also gets row IDs
- [ ] Commit message: "S36 TASK-3608: danish-agreements.md cross-references to source register"

---

### Phase 5 — Phase B Kickoff + Sprint Close (2 tasks)

#### TASK-3609 — Phase B kickoff packaging

| Field | Value |
|-------|-------|
| **ID** | TASK-3609 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/references/phase-b-handoff-package.md` (NEW) |
| **Dependencies** | TASK-3608 |

**Scope**: produce a handoff package for the Phase B domain-expert review (per PROGRAM L88–101). The package should:
- Index the 3 new docs (source register + role dimension + ruleset audit)
- Highlight cells flagged `disputed?` = true or LOW confidence (where expert input is most valuable)
- Enumerate the DRIFT-IN-SEED candidate-bug rows from TASK-3607 audit for expert confirmation
- Provide a review-form template (per-cell sign-off shape: `agree / disagree / corrected-value / rationale`)
- Document the engagement protocol per PROGRAM L94–99

**Validation Criteria**:
- [ ] `docs/references/phase-b-handoff-package.md` exists with index + cell-summary + review-form-template + protocol section
- [ ] Candidate domain-expert list referenced (per PROGRAM L95–96 — to be identified during S35 close week; engagement starts this sprint)
- [ ] Phase B feedback expected by S37 sprint start
- [ ] Commit message: "S36 TASK-3609: Phase B kickoff packaging"

---

#### TASK-3610 — Sprint close

| Field | Value |
|-------|-------|
| **ID** | TASK-3610 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/sprints/SPRINT-36.md` (close sections), `docs/sprints/INDEX.md` (final row), `ROADMAP.md` (Phase 4e Phase A pass-1 entry — mark COMPLETE; pass-2 = S37 pending), `~/.claude/projects/C--StatsTid/memory/MEMORY.md` (S36 line) |
| **Dependencies** | TASK-3609 |

**Scope**: standard sprint-close plumbing. Design-only sprint contract:
- No `dotnet build` change verification (no code touched)
- No test count change (869 unchanged)
- No QUALITY.md re-grade (deferred to S41)
- ROADMAP Phase 4e Phase A pass 1 entry → COMPLETE; pass 2 = S37 pending + Phase B feedback ETA
- MEMORY.md S36 line updates: Sprint Status section + summary of finding counts per agreement (HIGH / MEDIUM / LOW confidence; DRIFT-IN-SEED bug candidates by count)

**Validation Criteria**:
- [ ] SPRINT-36.md close sections filled (architectural constraints checked; task statuses all = completed; commit list aligned)
- [ ] INDEX.md S36 row finalized (status: complete; dates: 2026-05-21 → close-date; tests: 869 unchanged)
- [ ] ROADMAP Phase 4e Phase A pass 1 marked COMPLETE
- [ ] MEMORY.md gains S36 summary line
- [ ] Commit message: "S36 TASK-3610: sprint close — Phase A inventory pass 1 complete (N candidate bug-with-no-past-impact corrections surfaced for S37)"

---

## Legal & Payroll Verification (TASK-3610)

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | INVENTORY-IN-PROGRESS | Source register fill is the first systematic comparison of code vs seed vs source. Phase B + S37 finalize sign-off. |
| Wage type mappings produce correct SLS codes | N/A | No mapping changes in S36 (deferred to S40 cutover). |
| Overtime/supplement calculations are deterministic | N/A | No rule changes. |
| Absence effects on norm/flex/pension are correct | N/A | No absence-rule changes. |
| Retroactive recalculation produces stable results | N/A | No rule-engine input surface changes. |

## External Review (Step 7a-equivalent)

| Field | Value |
|-------|-------|
| **Invoked** | **SKIP** per S28 / S32 design-only precedent. |
| **Rationale** | The relevant review checkpoint for source-register correctness is Phase B domain-expert validation (PROGRAM L88–101 + S37 absorption). Source-register entries are claims about cirkulær text — outside the architectural defect classes Codex / Reviewer cover. The pertinent expert sign-off lives in S37. |
| **Cycle-cap** | n/a — no review cycles in this sprint. |

## Test Summary (TASK-3610)

Per `sprint-test-validation` skill: **SKIP with rationale** — design-only sprint; no code surface; no test totals shift; running suites would burn CI time for zero signal change.

| Suite | S35 close | S36 projected | Delta |
|-------|-----------|---------------|-------|
| Unit | 526 | 526 | 0 |
| Plain regression | 35 | 35 | 0 |
| Docker-gated passing | 218 | 218 | 0 |
| Frontend vitest | 90 | 90 | 0 |
| **Total** | **869** | **869** | **0** |

## Critical-Path Callouts

1. **Sprint type is DESIGN-ONLY** — no code surface; many WORKFLOW.md checklist items don't apply. The Status field in SPRINT-36.md reflects this with explicit DESIGN-ONLY type tag (S28 / S32 precedent).

2. **PROGRAM-s36-s41-domain-correctness.md is the de facto refinement** — no separate refinement file. The PROGRAM doc absorbed dual-lens scrutiny during S35 cycle-1 absorption (Codex BLOCKERs 1–3 + Reviewer BLOCKER 1).

3. **Phase B is a parallel workstream** — domain-expert candidate identification starts S35 close week; engagement target = week 2 of S36 once register has draft AC OK24 entries. Phase B feedback is absorbed in S37, not S36. S36 produces the artifact; S37 reconciles it.

4. **S36 surfaces candidate bugs; S36 does NOT fix them** — any DRIFT-IN-SEED / DRIFT-IN-CODE discoveries are flagged in the register / ruleset-audit and route through S37 or S39 absorption per the rule correction policy classification governance. The exception is meta-corrections to the AC=AFSPADSERING / UDBETALING distinction already settled in S35 (just record the supersession + bug-correction history).

5. **Schema is fixed by PROGRAM L51–67 but speculatively-authored** — TASK-3601 is the first real test. If filling 20 cells reveals the 13-column schema doesn't fit, halt + propose extension.

6. **No worktree dispatch** — every task is Orchestrator-direct sequential. Closes the S24 / S33 / S34 worktree-base-mismatch class entirely for this sprint.

7. **Cycle-cap discipline not exercised** — no cycles in this sprint (Step 0b SKIP; Step 7a SKIP). The next cycle-cap exercise is S37 dual-lens (if invoked) or S38 ADR-024 + ADR-025 dual-lens (per PROGRAM L162).

---

## ROADMAP / MEMORY Cross-References

- ROADMAP "Deployment Model" (L16–27) — single logical deployment, 150 institutions, glocal rule encoding, rule correction policy
- ROADMAP Phase 4e bullets — S36 = Phase A inventory pass 1
- PROGRAM-s36-s41-domain-correctness.md — granular execution plan (this sprint = its TASK-3600..3610)
- `feedback_thrash_defer_real_world.md` — cycle-cap protocol (not exercised this sprint)
- `feedback_dont_pause_for_reviews.md` — mechanical absorption discipline (n/a for design-only)
- `feedback_step7a_cycle_cap_discipline.md` — Step 7a SKIP rationale (design-only precedent)

## Phase B Engagement Status (running tracker)

| Field | Status |
|-------|--------|
| Candidate identification | started 2026-05-20 (S35 close week per PROGRAM L101) |
| Candidate(s) selected | pending |
| Engagement window | targeted week 2 of S36 |
| Phase B feedback ETA | S37 sprint start |
