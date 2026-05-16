# Sprint 32 — Phase 4d-3 Part 2 DESIGN-ONLY: ADR-023 Authorship

| Field | Value |
|-------|-------|
| **Sprint** | 32 |
| **Status** | **in-progress** (opened 2026-05-16) |
| **Start Date** | 2026-05-16 |
| **End Date** | _filled by TASK-3203_ |
| **Orchestrator Approved** | no (sprint open) |
| **Build Verified** | _filled by TASK-3203_ — design-only sprint; no `dotnet build` change expected |
| **Test Verified** | _filled by TASK-3203_ — design-only sprint; test totals unchanged from S31 close (833) |
| **Sprint-start commit base** | `b43de8b` (S31 sprint close, 2026-05-16) |
| **Sprint-end HEAD** | _filled by TASK-3203_ |
| **Sprint type** | **DESIGN-ONLY** — produces ADR-023 settling 7 enumerated questions from the deferred S32-implementation refinement at `.claude/refinements/REFINEMENT-s32-phase-4d3-part2.md`. NO code changes; NO test changes. Mirrors S28's deferred-design pattern that produced ADR-020 before S29 implementation. |
| **Refinement** | `.claude/refinements/REFINEMENT-s32-phase-4d3-part2.md` (deferral artifact-of-record). Step 4 cycle 1 + cycle 2 dual-lens thrash signal per `feedback_thrash_defer_real_world.md` (third canonical thrash-defer case after S28 and the S31 cycle-2-converging-finite control). The 7 questions enumerated at the top of the deferral verdict are the binding ADR-023 scope. No fresh refinement file. |
| **Plan** | `.claude/plans/PLAN-s32-design.md` (Step 0a) |

## Sprint Goal

Settle the 7 architectural questions that the deferred S32-implementation refinement exposed. Produce **ADR-023 "Employee-Profile Versioning Emission + Rule-Engine Cutover Architecture (Phase 4d-3 Part 2 Design)"** as binding contract for the S33 implementation sprint that follows. Mirrors S28 → S29 split for ADR-020 → WTM implementation.

The 7 questions ADR-023 must settle:

1. **PCS consumption-site location** — where the EmployeeProfile snapshot is re-resolved (NOT `MapSegmentToExportLinesAsync` per Codex cycle 2 BLOCKER; the rule-engine route needs a different site, likely inside `BuildPlanForLegacyCallersAsync` or before `EvaluateSegmentAsync`)
2. **MIGRATE vs alternative determinism strategy** for `agreement_code` + `employment_category` — re-adjudicate with full architectural awareness
3. **Soft-delete consumption semantic post-decision** — fail-closed vs new pattern (fallback-to-users unviable post-MIGRATE per cycle 2 finding)
4. **Backfill ledger pattern** for UPDATE-with-backfill (S22 D8 INSERT pattern doesn't transfer)
5. **Multi-commit ordering enforcement** — refinement cycle 2 surfaced "no admin uses /api/admin/users PUT during sprint days" is unenforceable; ADR-023 must specify mechanical (not procedural) ordering safety
6. **User-model surface cascade** — `User.AgreementCode` consumed at 25+ Backend.Api call sites; column drop propagates everywhere
7. **JWT-drift admin-visible UI affordance** — drift between live JWT and dated rule-engine reads needs admin-facing surface

## Architectural Decisions to Settle (in ADR-023)

The 7 above. Each must be precise enough that S33 implementation refinement has zero "what does ADR-023 mean here?" questions.

## Entropy Scan Findings

Per WORKFLOW.md Step 0a (2026-05-16):

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | DEFERRED | Phase 4e candidate (carried from S29-S31) |
| MEMORY.md drift | CLEAN | Synchronized through S31 close per session context |
| QUALITY.md re-grade | N/A | Design-only sprint — no code-grade shift |
| Refinement disposition | RESOLVED | S32-implementation refinement DEFERRED 2026-05-16 with full thrash-defer audit trail at `.claude/refinements/REFINEMENT-s32-phase-4d3-part2.md` |

No DRIFT items requiring fix before sprint open. No DEBT items added.

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | **SKIP** per AGENTS.md L307: design-only sprint; no implementation surface; refinement-stage already absorbed both architectural defect classes through cycle 1 + cycle 2 dual-lens; ADR-023 review happens at TASK-3202 dual-lens which is Step-7a-equivalent for a design-only sprint. |
| **External Codex** | not invoked at Step 0b (will invoke at TASK-3202 cycle 1 against ADR-023 DRAFT) |
| **Internal Reviewer** | not invoked at Step 0b (will invoke at TASK-3202 cycle 1 against ADR-023 DRAFT) |
| **BLOCKERs resolved before Step 1** | n/a — Step 0b SKIP |

### Resolution

Step 0b SKIP rationale documented. Plan READY for Step 1 (sprint open).

## Architectural Constraints Verified

_To be checked off as the sprint progresses; final assertion in TASK-3203._

- [ ] **P1 — Architectural integrity** → TASK-3201 ADR-023 settles 7 questions with binding language; ADR-016 D5b extension paragraph filed IF Q1 adds a sixth pattern
- [ ] **P2 — Rule engine determinism** → ADR-023 D1 (PCS consumption-site) specifies exactly where snapshot reaches rule-engine payload — replay-stable
- [ ] **P3 — Event sourcing / auditability** → ADR-023 D5 (multi-commit ordering) specifies mechanical safety not procedural; backfill ledger pattern documented
- [ ] **P4 — Version correctness** → ADR-023 D3 (soft-delete semantic) settled definitively
- [ ] **P7 — Security and access control** → ADR-023 D7 (JWT-drift admin affordance) decided

Not directly affected: P5/P6/P8/P9 (no code surface in this sprint).

## Task Log

4 declared tasks; Plan file `.claude/plans/PLAN-s32-design.md` is source-of-truth for detail.

### Phase 0 — Sprint-Open

#### TASK-3200 — Sprint-open plumbing (design-only sprint declaration)

| Field | Value |
|-------|-------|
| **ID** | TASK-3200 |
| **Status** | in-progress |
| **Agent** | Orchestrator-direct |
| **Components** | docs/sprints/SPRINT-32.md (this file), docs/sprints/INDEX.md (provisional row), .claude/plans/PLAN-s32-design.md |
| **Plan section** | Phase 0 — TASK-3200 (PLAN-s32-design.md) |
| **Dependencies** | none |

**Description**: Create SPRINT-32.md from TEMPLATE.md with DESIGN-ONLY sprint type tag + sprint goal + 7 enumerated questions + provisional task-log skeleton (TASK-3200..3203 reserved). Update INDEX.md with provisional in-progress row. Plan file already authored at PLAN-s32-design.md.

**Validation Criteria**:
- [x] SPRINT-32.md exists at `docs/sprints/SPRINT-32.md`
- [x] PLAN-s32-design.md exists at `.claude/plans/PLAN-s32-design.md`
- [ ] INDEX.md row added with status=in-progress + DESIGN-ONLY note
- [ ] Commit lands at sprint-open

---

### Phase 1 — ADR-023 Authorship

#### TASK-3201 — Draft ADR-023 settling 7 questions

| Field | Value |
|-------|-------|
| **ID** | TASK-3201 |
| **Status** | pending |
| **Agent** | Orchestrator-direct (KB writes per WORKFLOW.md L48) |
| **Components** | docs/knowledge-base/decisions/ADR-023-employee-profile-versioning-emission-and-rule-engine-cutover.md (new). Optional: docs/knowledge-base/decisions/ADR-016-temporal-period-handling.md D5b extension if Q1 decision adds a sixth pattern. |
| **Plan section** | Phase 1 — TASK-3201 |
| **Dependencies** | TASK-3200 |

**Description**: Author ADR-023 settling all 7 enumerated questions from the refinement deferral verdict. Each decision must be precise enough that S33 implementation refinement has zero "what does ADR-023 mean here?" questions. Specifically D1 (PCS consumption-site) names exact method + line range. Mirrors S28 TASK-2801 (ADR-020 write).

---

### Phase 2 — Dual-Lens ADR Review

#### TASK-3202 — Dual-lens ADR review (Codex gpt-5.5 + Reviewer Agent)

| Field | Value |
|-------|-------|
| **ID** | TASK-3202 |
| **Status** | pending |
| **Agent** | Orchestrator dispatches both lenses; Reviewer Agent + external Codex |
| **Components** | Read-only review of ADR-023. Cycle 1 + optional cycle 2 fixes via ADR-023 edits. Final commit flips DRAFT → ACCEPTED status. |
| **Plan section** | Phase 2 — TASK-3202 |
| **Dependencies** | TASK-3201 |

**Description**: Step 7a-equivalent dual-lens review on ADR-023 DRAFT. Both lenses dispatched. Cycle-cap = 2 per lens per `feedback_step7a_cycle_cap_discipline.md`. If cycle 2 surfaces NEW BLOCKERs in same area as cycle 1, halt + prompt user (second consecutive thrash → rescope per the deferred refinement's option 3 — split into 3 sub-sprints).

---

### Phase 3 — Validation (n/a, design-only sprint)

Per WORKFLOW.md Step 4: `dotnet build` change unexpected; sprint-test-validation skill SKIP with rationale.

### Phase 4 — Sprint Close

#### TASK-3203 — Sprint close + INDEX + ROADMAP + MEMORY

| Field | Value |
|-------|-------|
| **ID** | TASK-3203 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | docs/sprints/SPRINT-32.md (close sections), docs/sprints/INDEX.md (final row), docs/knowledge-base/INDEX.md (final ADR-023 entry), ROADMAP.md (Phase 4d-3 Part 2 — S32 design-only COMPLETE; S33 implementation stub), ~/.claude/projects/C--StatsTid/memory/MEMORY.md (S32 line) |
| **Plan section** | Phase 4 — TASK-3203 |
| **Dependencies** | TASK-3202 |

---

## Legal & Payroll Verification

Design-only sprint; no rule changes; no payroll changes. SKIP per sprint type. Final assertion at TASK-3203.

## External Review (Step 7a-equivalent at TASK-3202)

_Filled by TASK-3202._

| Field | Value |
|-------|-------|
| **Invoked** | pending (at TASK-3202) |
| **Sprint-start commit** | `b43de8b` |
| **Command** | _filled at TASK-3202_ |
| **Review Cycles** | _filled at TASK-3202_ |
| **Findings** | _filled at TASK-3202_ |
| **Resolution** | _filled at TASK-3202_ |

## Test Summary

| Suite | Previous (S31) | Current (S32) | Delta |
|-------|----------------|---------------|-------|
| Unit | 526 | 526 | 0 |
| Plain regression | 35 | 35 | 0 |
| Docker-gated | 184 | 184 | 0 |
| Frontend vitest | 88 | 88 | 0 |
| **Total** | **833** | **833** | **0** (design-only sprint; no code change) |

`sprint-test-validation` skill SKIP with rationale: design-only sprint; no code surface; no test totals shift expected. Final assertion at TASK-3203.

## Agent Effectiveness

_Filled by TASK-3203._

| Metric | Value |
|--------|-------|
| Tasks | 4 declared (TASK-3200–3203) |
| Constraint Violations | _pending_ |
| Reviewer Findings | _pending_ |
| External Review Cycles | _Step 7a-equivalent at TASK-3202: pending_ |
| External Findings | _pending_ |
| Re-dispatches | _pending_ |
| First-Pass Rate | _pending_ |

## Sprint Retrospective

_Filled by TASK-3203._

**What went well**: _pending_

**What to improve**: _pending_

**Knowledge produced**: _pending — ADR-023 expected_
