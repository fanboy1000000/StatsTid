# Sprint 28 — ADR-020 Design Sprint (Phase 4d-1 Pre-Work)

| Field | Value |
|-------|-------|
| **Sprint** | 28 |
| **Status** | complete |
| **Start Date** | 2026-05-09 |
| **End Date** | 2026-05-09 |
| **Orchestrator Approved** | yes — 2026-05-09 |
| **Build Verified** | yes (`dotnet build` 0/0 — no code changes; ADR + INDEX edits only) |
| **Test Verified** | N/A (design-only sprint; no test changes; 525 unit + 35 plain regression + 147 Docker-gated + 88 frontend vitest unchanged from S27 baseline) |
| **Sprint-start commit base** | `b29c224` (S28-deferral commit; preceded by S27 close `7404366`) |
| **Sprint-end HEAD** | `d4b358d` (TASK-2803 cycle 2 absorption + ADR-020 DRAFT → ACCEPTED). 4 commits total: `ee109ae` TASK-2801 ADR-020 write + `7e37d5a` TASK-2802 ADR-019 D3 amendment + `a4fa470` TASK-2803 cycle 1 absorption + `d4b358d` TASK-2803 cycle 2 absorption + ACCEPTED flip. |
| **Sprint type** | **DESIGN-ONLY** — produces ADR-020; no code changes; no test changes. |
| **Refinement** | `.claude/refinements/REFINEMENT-s28-adr-020-design.md` (cycle 1 + cycle 2 + cycle 2.5 absorbed; user-approved skip cycle 3 because BLOCKER fixes were small + concrete despite showing thrash topography) |

## Sprint Goal

Produce ADR-020 settling 3 architectural questions surfaced (and thrashed) by the deferred Phase 4d-1 implementation refinement at `.claude/refinements/REFINEMENT-s28-phase-4d1.md`. ADR-020 is the binding contract S29 implementation refinement reads as input, NOT as suggestions. Original Phase 4d-1 implementation (effective-dating + supersession + replay-determinism marquee) deferred to S29.

**Sprint-precedent flag**: design-only-as-separate-sprint-number is a **one-off justification** (the deferred Phase 4d-1 thrash demanded an architectural reset before implementation), NOT a new convention. Comparable design passes (S20's ADR-016 D1-D11; S22's ADR-018 D1-D11) were folded into their implementation sprints. Future Orchestrator should NOT read this as "design sprints get their own sprint number by default" — only when the implementation refinement has demonstrably thrashed.

## Entropy Scan Findings

_Skipped per WORKFLOW.md Step 0a calibration — design-only sprint with no code changes; entropy scan runs at the next implementation sprint (S29) when code surface re-opens._

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | OPTIONAL — design-only sprint produces an ADR; reviewed via TASK-2803 in-sprint dual-lens (Step 7a-equivalent). Step 0b plan-review at sprint open is redundant with the refinement-level dual-lens that produced the 4-task plan. |
| **External Codex** | invoked at REFINEMENT level (1 cycle 2026-05-09: 2B/4W/3N → 2 cycles 0B/1W/4N absorbed). |
| **Internal Reviewer** | invoked at REFINEMENT level (1 cycle 2026-05-09: 0B/3W/6N → 2 cycles 1B/2W/2N → user-approved skip cycle 3). |
| **Plan-level review** | SKIP — refinement-level review covered the architectural surface; TASK-2803 in-sprint dual-lens reviews the ADR text directly. **READY for Step 1 dispatch**. |

## Architectural Constraints Verified

- [x] P1 — Architectural integrity (ADR-020 settles 3 binding architectural questions; sets pattern for Phase 4d-2/3 non-rule replay-input consumers; explicitly amends ADR-019 D3 via direct in-S28 commit per TASK-2802 to maintain KB consistency).
- [x] P3 — Event sourcing/auditability (D2 gap-acknowledging routing preserves audit honesty; admin-delete-stays-deleted semantic preserves admin intent).
- [x] P5 — Integration isolation (D1 planner-level enrollment seam decouples snapshot from rule; preserves SharedKernel's no-Npgsql guarantee).
- [x] P6 — Payroll integration correctness (D2 reopen routing's gap-acknowledging stance ensures `GetByKeyAtAsync(date)` returns NULL in [DELETE, reopen] interval — replay determinism per ADR-016 D10 satisfied for WTM via the export-time effective-date lookup pattern).

Not directly affected: P2, P4, P7, P8, P9.

## Task Log

### TASK-2801 — Write ADR-020 versioned-config design foundations

| Field | Value |
|-------|-------|
| **ID** | TASK-2801 |
| **Status** | complete |
| **Agent** | Orchestrator-direct (KB writes are Orchestrator-only per WORKFLOW.md L48) |
| **Components** | docs/knowledge-base/decisions/, docs/knowledge-base/INDEX.md |
| **KB Refs** | ADR-020 (NEW); ADR-016 D5b + D10 cross-ref; ADR-017 D2 cross-ref; ADR-018 D7+D8+D9+D11+D13 cross-ref; ADR-019 D3 amendment cross-ref |
| **Constraint Validator** | pass (no code changes) |
| **Reviewer Audit** | performed via TASK-2803 (in-sprint dual-lens) — 2 cycles per lens, ACCEPTED |
| **Orchestrator Approved** | yes — 2026-05-09 |
| **Commit** | `ee109ae` |

**Description**: Write ADR-020 with D1 (planner-level enrollment for non-rule replay inputs — initially 3 components, expanded to 5 after TASK-2803 cycle 1 Reviewer B1) + D2 (soft-delete-then-create routing — initially 2-case, expanded to 3-case after TASK-2803 cycle 1 Reviewer B2) + D3 (seed idempotency via ON CONFLICT (natural_key, effective_from)). Implications-for-S29 section enumerates 8 binding constraints. Open section defers Phase 4d-2/3 + Phase 4e candidates. References cross-cite the relevant ADRs.

**Validation Criteria**:
- [x] ADR-020 file created with Decision/Rationale/Consequences/Open format
- [x] D1 + D2 + D3 each state Decision + Rationale + Consequences
- [x] Implications-for-S29 enumerates all binding constraints
- [x] References cite ADR-016 D5b/D10, ADR-017 D2, ADR-018 D7/D8/D9/D11/D13, ADR-019 D3
- [x] Cycle-1 + Cycle-2 Review History entries appended after TASK-2803
- [x] INDEX.md updated with ADR-020 row + ADR-019 D3-amendment annotation

**Files Changed**:
- `docs/knowledge-base/decisions/ADR-020-versioned-config-design-foundations.md` (NEW)
- `docs/knowledge-base/INDEX.md` (ADR-020 row + ADR-019 row update)

---

### TASK-2802 — ADR-019 D3 amendment block (KB consistency)

| Field | Value |
|-------|-------|
| **ID** | TASK-2802 |
| **Status** | complete |
| **Agent** | Orchestrator-direct (KB writes are Orchestrator-only) |
| **Components** | docs/knowledge-base/decisions/ADR-019-...md |
| **KB Refs** | ADR-019 D3 (amended); ADR-020 (cross-ref) |
| **Constraint Validator** | pass (no code changes) |
| **Reviewer Audit** | covered by TASK-2803 cycle 1 (W2 D2.3→D7 cite fix applied in cycle 1 absorption) |
| **Orchestrator Approved** | yes — 2026-05-09 |
| **Commit** | `7e37d5a` (with W2 D2.3→D7 cite fix folded into TASK-2803 cycle 1 absorption commit `a4fa470`) |

**Description**: Direct in-S28 commit edits ADR-019 D3 with "Amended by ADR-020 / S29" block noting that the "flat-CRUD with composite key … no effective-dating either" framing for `wage_type_mappings` is superseded for that resource by S29. Resolves KB internal contradiction. The S25-introduced row-version + ETag/If-Match contract on `wage_type_mappings` (across D1 / D2 / D7 above — TASK-2803 cycle 1 W2 fix from "D2.3" mis-cite) is preserved unchanged on the live-edit path; supersession adds a new history-row creation path orthogonal to the version contract.

**Validation Criteria**:
- [x] Amendment block added directly to ADR-019 D3 paragraph (not just ADR-020 cross-cite)
- [x] Cross-references corrected to D1 / D2 / D7 (the actual locations of WTM ETag stuff in ADR-019)
- [x] `position_override_configs` framing UNCHANGED (still flat-CRUD; ADR-020 doesn't affect it)

**Files Changed**:
- `docs/knowledge-base/decisions/ADR-019-optimistic-concurrency-via-row-version.md` (D3 amendment block)

---

### TASK-2803 — Dual-lens ADR review (Step 7a-equivalent)

| Field | Value |
|-------|-------|
| **ID** | TASK-2803 |
| **Status** | complete |
| **Agent** | Orchestrator-direct (Codex CLI + Reviewer Agent dispatch) |
| **Components** | n/a (review of TASK-2801 + TASK-2802 outputs) |
| **KB Refs** | ADR-020 (under review); `feedback_thrash_defer_real_world.md` (lens-divergence smoke alarm rule) |
| **Constraint Validator** | pass |
| **Reviewer Audit** | self (this IS the audit) |
| **Orchestrator Approved** | yes — 2026-05-09 |
| **Commits** | `a4fa470` (cycle 1 absorption), `d4b358d` (cycle 2 absorption + ACCEPTED flip) |

**Description**: Step 7a-equivalent dual-lens review on the ADR-020 text (and ADR-019 D3 amendment). 2-cycle cap per lens. Lens-divergence DRAFT-freeze automatic per Risk #5 mechanical AC of the design refinement.

**Cycle 1**: Codex 0B/0W/0N (calibrated for code diffs; clean on ADR-text qualitative review) vs Reviewer 2B/2W/4N (B1 D1 hydrator invocation seam; B2 D2 `effective_from` preservation vs reset). **Lens divergence smoke alarm fired** — Codex APPROVE while Reviewer found 2 BLOCKERs in same-area-deeper-layer pattern (recurring Q1 + Q2 binding gaps from the design refinement's cycle trail). User adjudicated 2 sub-questions 2026-05-09:
- B1 sub-question: "keep hydrator API + bind planner-side EmploymentProfile + invocation site" → D1 expanded from 3 components to 5
- B2 sub-question: "reset effective_from = today on reopen — gap-acknowledging" → D2 routing expanded from 2-case to 3-case (Case A no predecessor; Case B predecessor.effective_from < today; Case C zero-width predecessor.effective_from = today)
- W1 (D1 PCS path) + W2 (ADR-019 D2.3→D7 cite) absorbed mechanically.

**Cycle 2**: Reviewer 0B/0W/3N + Codex 1 P3 (NOTE-level). **Lens divergence smoke alarm did NOT re-fire** — both lenses converged on APPROVE-with-NOTE-fixes. The TASK-2803 review trail terminated cleanly per `feedback_thrash_defer_real_world.md`'s converging-finite test. NOTEs absorbed: Codex P3 (rationale "3 components" → "5 components"); Reviewer N1 (forward-compat seam clarity); Reviewer N2 (Case C "collapsed same-request" wording fix); Reviewer N3 (W2 breadcrumb housekeeping deferred).

**Status flip**: ADR-020 DRAFT → ACCEPTED.

**Validation Criteria**:
- [x] Codex review dispatched (`codex review --base b29c224`) — 2 cycles
- [x] Reviewer Agent dispatched on ADR-020 text — 2 cycles
- [x] Both lenses APPROVE on cycle 2
- [x] Cycle-cap respected (2 of 2 per lens for in-sprint TASK-2803)
- [x] ADR-020 status flipped DRAFT → ACCEPTED in both file header + INDEX.md row
- [x] Cycle 1 + Cycle 2 entries appended to ADR-020 Review History

**Files Changed**:
- `docs/knowledge-base/decisions/ADR-020-versioned-config-design-foundations.md` (D1+D2 expansion + Cycle 1+2 Review History)
- `docs/knowledge-base/decisions/ADR-019-optimistic-concurrency-via-row-version.md` (W2 D2.3→D7 cite fix)
- `docs/knowledge-base/INDEX.md` (ADR-020 status: DRAFT → accepted)

---

### TASK-2804 — Sprint plumbing (SPRINT-28.md + ROADMAP + MEMORY)

| Field | Value |
|-------|-------|
| **ID** | TASK-2804 |
| **Status** | complete |
| **Agent** | Orchestrator-direct |
| **Components** | docs/sprints/, ROADMAP.md, memory/ |
| **KB Refs** | ADR-020 (referenced in ROADMAP Phase 4d-1 entry update) |
| **Constraint Validator** | pass |
| **Reviewer Audit** | skipped (mechanical sprint-close paperwork) |
| **Orchestrator Approved** | yes — 2026-05-09 |
| **Commit** | this commit |

**Description**: Sprint-close plumbing. Create `docs/sprints/SPRINT-28.md` (this file), update `docs/sprints/INDEX.md` with S28 row, update `ROADMAP.md` Phase 4d-1 entry to mark Sprint 28 (ADR-020 design) COMPLETE + reference ADR-020's binding decisions, update `MEMORY.md` sprint log line.

**Validation Criteria**:
- [x] SPRINT-28.md created with 4-task log + design-sprint metadata + sprint-precedent flag
- [x] `docs/sprints/INDEX.md` gains S28 row
- [x] ROADMAP Phase 4d-1 entry marks S28 COMPLETE + cites ADR-020 binding decisions
- [x] MEMORY.md sprint log line for S28 added
- [x] dotnet build still clean (no code changes since S27 close)

**Files Changed**:
- `docs/sprints/SPRINT-28.md` (NEW)
- `docs/sprints/INDEX.md` (S28 row)
- `ROADMAP.md` (Phase 4d-1 entry update)
- `C:\Users\lauge\.claude\projects\C--StatsTid\memory\MEMORY.md` (sprint log line — outside repo, separate edit)

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | Design-only; no rule engine changes |
| Wage type mappings produce correct SLS codes | N/A | Design-only; S29 implements |
| Overtime/supplement determinism | N/A | Design-only |
| Absence effects correct | N/A | Design-only |
| Retroactive recalculation stable | DESIGN-IMPACT | ADR-020 D1 + D2 set the binding constraints S29 must satisfy to preserve replay determinism (per ADR-016 D10) on WTM lookups during payroll export. The "no retroactive recomputation" ROADMAP commitment is preserved (Implications §7: `MapCalculationResultAsync` stays current-row). |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes (via TASK-2803 — Step 7a-equivalent for design-only sprint) |
| **Sprint-start commit** | `b29c224` (S28 deferral commit) |
| **Command** | `codex review --base b29c224` (per cycle) + Reviewer Agent dispatch |
| **Review Cycles** | 2 of 2 (cycle cap respected per `feedback_step7a_cycle_cap_discipline.md`) |
| **Cycle 1 — Codex** | 0B/0W/0N (clean on ADR-text qualitative review; calibrated for code diffs) |
| **Cycle 1 — Reviewer** | 2B/2W/4N (B1 + B2 + W1 + W2 + 4 NOTEs); recommend stay DRAFT |
| **Cycle 2 — Codex** | 1 P3 (rationale-text contradiction "3 components" vs Decision body "5 components") |
| **Cycle 2 — Reviewer** | 0B/0W/3N; APPROVE DRAFT → ACCEPTED |
| **Resolution** | All cycle 1 BLOCKERs absorbed via D1 5-component + D2 3-case expansion; cycle 2 NOTEs absorbed; ADR-020 status flipped DRAFT → ACCEPTED on commit `d4b358d`. |

### Cross-cycle observation: lens-divergence pattern

S28 was the SECOND clean application of `feedback_thrash_defer_real_world.md`:
- **First (S28-deferred refinement)**: Reviewer APPROVE / Codex BLOCKER → defer, split sprint (the trigger that produced S28 design + S29 implementation).
- **Now (S28-design TASK-2803 cycle 1)**: Codex APPROVE / Reviewer BLOCKER → freeze DRAFT + reopen specific decisions (the converse direction; same architectural-divergence signal). Successfully terminated at cycle 2 — neither lens found new BLOCKERs after the cycle-1 absorption.

This second case demonstrates the discipline works in BOTH directions of lens divergence, not just the canonical Reviewer-APPROVE-Codex-BLOCKER shape. Recorded in Review History of ADR-020 itself.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 525 | unchanged from S27 baseline (no code changes) |
| Plain regression tests | 35 | unchanged |
| Docker-gated regression tests | 147 | unchanged |
| Frontend vitest | 88 | unchanged |
| **Total** | **795** | unchanged from S27 close (S28 is design-only) |

## Agent Effectiveness

| Task | Agent | Worktree | Cycles | First-pass | Notes |
|---|---|---|---|---|---|
| TASK-2801 | Orchestrator-direct | n/a | 1 (in-sprint review at TASK-2803) | yes (with cycle 1 D1+D2 absorption applied) | ADR-020 write + INDEX update. The decisions were pre-resolved at refinement time; "investigation" was ADR-writing. |
| TASK-2802 | Orchestrator-direct | n/a | 1 (TASK-2803 cycle 1 W2 fix folded in) | yes (with W2 D2.3→D7 cite fix in cycle 1) | ADR-019 D3 amendment block. KB consistency. |
| TASK-2803 | Orchestrator-direct (Codex CLI + Reviewer Agent) | n/a | 2 cycles per lens | n/a (review task) | Cycle 1 surfaced 2 real BLOCKERs (B1+B2); user adjudicated; cycle 2 verified absorption clean; ADR-020 ACCEPTED. |
| TASK-2804 | Orchestrator-direct | n/a | 1 | yes | Sprint plumbing. |

4/4 first-pass clean (with cycle-1 absorption count for tasks where TASK-2803 review surfaced binding gaps). Zero re-dispatches (Orchestrator-direct work).

## Sprint Retrospective

**What worked well:**
- S28 is the design-only sprint shape that the deferred Phase 4d-1 thrash demanded. Splitting design from implementation set up ADR-020 as a binding contract for S29 BEFORE implementation refinement — preventing the cycle-3 thrash recurrence.
- Refinement-level dual-lens review (3 cycles per lens; user-approved skip cycle 3) caught the design questions BEFORE the ADR was written. Each subsequent cycle bound a deeper layer (cycle 1: snapshot trigger candidates; cycle 2: call-site lock; cycle 2.5: planner-side lock).
- TASK-2803 in-sprint dual-lens review (2 cycles per lens) caught the LAST layer of binding gaps (D1 hydrator invocation seam + D2 effective_from preservation/reset) — gaps that the refinement-level review missed because they only become visible when staring at the ADR text, not the scoping shape. The two review surfaces (refinement scoping + ADR text) are complementary.
- `feedback_thrash_defer_real_world.md` discipline applied cleanly TWICE in one sprint (refinement-deferral trigger + TASK-2803 lens-divergence DRAFT-freeze). The discipline works in both lens-divergence directions.
- ADR-019 D3 amendment as a separate task (TASK-2802) prevented the KB from staying internally contradictory between ADR-019's flat-CRUD claim and ADR-020's effective-dating decision. The Codex C-B2 cycle-1 finding from the refinement review was load-bearing — without TASK-2802, S29 implementer would've read ADR-019 and gotten conflicting guidance.

**What to improve:**
- The "design-only as separate sprint number" precedent flag is important — future Orchestrator should NOT default to splitting design from implementation unless the implementation refinement has demonstrably thrashed. S20's ADR-016 + S22's ADR-018 were folded into their implementation sprints; that's the default. S28's split was justified by the deferred Phase 4d-1 thrash, not by general policy.
- ADR-020 D1's binding expanded from 3 components (refinement cycle 2.5) to 5 components (TASK-2803 cycle 1) — the refinement-level review missed the hydrator invocation seam + planner signature change. Lesson: when an ADR binds an API + planner integration, the data flow needs to be bound from registration through invocation, not just at the gate. Worth a lightweight checklist for future ADRs that bind cross-component integration.
- Codex's review on a doc-only diff returned 0/0/0 in TASK-2803 cycle 1 — calibrated for code diffs, ADR-text qualitative review wasn't its strength. Reviewer Agent's pattern-match audit + cross-reference against KB caught what Codex missed. For future ADR-text reviews, prefer Reviewer-Agent-primary + Codex-secondary; flip the order from the implementation-sprint convention.

**Knowledge produced:**
- ADR-020 (NEW; ACCEPTED) — versioned-config design foundations for Phase 4d-1.
- ADR-019 D3 amendment block (TASK-2802 + TASK-2803 cycle 1 W2 fix).
- ADR-020 Cycle 1 + Cycle 2 Review History entries.
- Second clean application of `feedback_thrash_defer_real_world.md` recorded in ADR-020 Review History.
