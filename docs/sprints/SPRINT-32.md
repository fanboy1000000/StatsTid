# Sprint 32 — Phase 4d-3 Part 2 DESIGN-ONLY: ADR-023 Authorship

| Field | Value |
|-------|-------|
| **Sprint** | 32 |
| **Status** | **complete** (closed 2026-05-16) |
| **Start Date** | 2026-05-16 |
| **End Date** | 2026-05-16 |
| **Orchestrator Approved** | yes (2026-05-16) |
| **Build Verified** | N/A — design-only sprint; no code changes; no `dotnet build` verification needed |
| **Test Verified** | 526 unit + 35 plain regression + 184 Docker-gated passing + 88 frontend vitest = **833 total** (UNCHANGED from S31 close — design-only sprint contract). sprint-test-validation skill SKIP with rationale: no code surface; no test totals shift. |
| **Sprint-start commit base** | `b43de8b` (S31 sprint close, 2026-05-16) |
| **Sprint-end HEAD** | `86f97bd` (TASK-3202 cycle 1 ACCEPTED). 4 commits total: `2977394` (TASK-3200 sprint open) + `d35c377` (TASK-3201 ADR-023 DRAFT) + `86f97bd` (TASK-3202 cycle 1 → ACCEPTED) + sprint-close commit (this one). |
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

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (external Codex gpt-5.5 + internal Reviewer Agent) at TASK-3202 |
| **Sprint-start commit** | `b43de8b` (S31 close) |
| **Command** | `codex exec "<step-7a-equivalent prompt>"` (referencing ADR-023 DRAFT file path; design-ADR review, not diff-review). Reviewer Agent invoked via Agent tool with same scope. |
| **Review Cycles** | 1 cycle per lens (cycle 2 NOT requested — all absorptions mechanical, no new architectural forks). Cycle-cap = 2 respected. |
| **Findings (cycle 1)** | **Codex (gpt-5.5)**: 2 WARNINGs + 2 NOTEs. WARNING #1 (convergent with Reviewer BLOCKER): D2 understates determinism-gap exposure — AdminEndpoints.cs:466 PUT `/api/admin/users/{userId}` is LocalAdminOrAbove-scoped (not HROrAbove), persists `users.agreement_code` directly at L512 with no event emission, frontend UserManagement.tsx:274 actively sends agreementCode. Gap is REAL workflow, not hypothetical. WARNING #2 (Codex-only): D8 scope undercount — `EmploymentProfileResolver` type/method doesn't exist (S31 has only live readers). S33 needs resolver creation + DI wiring as a cross-project plumbing task. NOTE #1: D1 PCS site verified (PCS.cs:326 IS segmentProfile construction, BEFORE EvaluateSegmentAsync L344). NOTE #2: D6 dead-code verified (no live caller). **Reviewer (Agent)**: 1 BLOCKER P4 (convergent with Codex W1 — D2 understates exposure with LocalAdminOrAbove detail) + 3 confirmatory NOTEs. Lens convergence: Codex framed as WARNING + new D8 finding; Reviewer framed convergent finding as BLOCKER. Same substance, different severity threshold. |
| **Resolution** | **All findings absorbed mechanically in single edit pass** committed as part of TASK-3202 cycle 1 absorption (`86f97bd`). D2 strengthened to "real exposure under normal admin workflow" with cited evidence; Phase 4e binding (was "candidate") promoted to LAUNCH-BLOCKING; S33 emits new `UserAgreementCodeChanged` event (55 → 56 EventSerializer registration) as Phase 4e replay-data trail; D8 task estimate bumped ~10 → ~11 with explicit resolver creation + DI wiring task; SoftDeleteAsync wording tightened (predecessor row's `version` is unchanged; audit row records `version_before = version_after = predecessor.version`). **Cycle 2 NOT requested** — all absorptions are text strengthening + 1 new event + 1 new task; no new architectural forks introduced. ADR-023 status flipped DRAFT → ACCEPTED in same commit. |

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

| Metric | Value |
|--------|-------|
| Tasks | 4 declared (TASK-3200–3203); all first-pass clean |
| Constraint Violations | 0 |
| Reviewer Findings | TASK-3202 cycle 1: 1 BLOCKER (P4 — D2 understates exposure with LocalAdminOrAbove scope detail) + 3 confirmatory NOTEs. All absorbed in `86f97bd`. |
| External Review Cycles | Step 7a-equivalent at TASK-3202: 1 cycle Codex gpt-5.5 / 1 cycle Reviewer Agent. Cycle 2 NOT requested (mechanical absorption clean; no new architectural forks). Cycle-cap = 2 respected. |
| External Findings | Codex (gpt-5.5) cycle 1: 2 WARNINGs (W1 D2 understates gap exposure — convergent with Reviewer BLOCKER; W2 D8 scope undercount on resolver creation) + 2 confirmatory NOTEs. All absorbed. |
| Re-dispatches | 0 sprint-task re-dispatches. ADR-023 absorption produced via single Orchestrator-direct edit pass within TASK-3202 scope. |
| First-Pass Rate | 4/4 declared tasks first-pass clean. TASK-3202 dual-lens caught real exposure-framing issue + missed plumbing task — load-bearing review checkpoint validated the design-only-sprint pattern. Cycle 1 alone sufficient; no thrash signal. |

## Sprint Retrospective

**What went well**:
- **Third canonical thrash-defer case (per `feedback_thrash_defer_real_world.md`) handled cleanly**. S32 reopened as design-only after the cycle-2 thrash signal on the S32-implementation refinement; S28 → S29 split precedent applied with no rescope drift. Mirrors S28's deferred-design approach for ADR-020 → S29 WTM implementation.
- **D2 reversal saved the sprint**. Cycle 1 absorption had picked MIGRATE; cycle 2 revealed MIGRATE-cascade was unsustainable; cycle 2 absorption reversed to LIVE-read + documented determinism gap (Reviewer's cycle 1 option-a that I had failed to surface). The reversal exposes a process learning: cycle 1 should have surfaced "document gap" as a primary alternative rather than reflexively absorbing via MIGRATE. ADR-023 §Refinement Trail documents this for future deferred-design sprints.
- **TASK-3202 dual-lens cycle 1 caught the LocalAdminOrAbove scope detail**. Reviewer's BLOCKER (LocalAdminOrAbove vs HROrAbove on AdminEndpoints PUT /api/admin/users) was a ground-truth verification only an in-context lens with code-access could surface. Codex's convergent WARNING + Reviewer's RBAC-scope-detail-augmented BLOCKER together produced a stronger absorption than either lens alone — classic `feedback_review_lens_complementarity.md` pattern.
- **Cycle-cap = 1 sufficient**. All TASK-3202 cycle 1 findings were mechanical absorption (text strengthening + 1 new event + 1 new task); no architectural reframe needed. Cycle 2 explicitly NOT requested per cycle-cap discipline + thrash-defer protocol.

**What to improve**:
- **Cycle 1 of the S32-implementation refinement (pre-defer)** absorbed Codex's convergent MIGRATE BLOCKER reflexively without surfacing Reviewer's alternative option (a) "document gap as Phase 4e candidate." That reflexive absorption locked in the MIGRATE-cascade path that cycle 2 then unwound. **S33 takeaway**: when cycle 1 surfaces convergent BLOCKERs with multiple absorption-shape recommendations from one lens, treat the alternatives as a user-decision fork, not as a default-to-the-more-aggressive-option choice.
- **The ADR-023 D8 task count undercount (Codex W2)** would have been caught earlier by a Grep on `EmploymentProfileResolver` during the ADR drafting phase. Future ADRs that reference new types should self-verify type existence before claiming the implementation work scope.

**Knowledge produced**:
- **ADR-023 — Employee-Profile Versioning Emission + Rule-Engine Cutover Architecture (Phase 4d-3 Part 2 Design)** — 8 binding decisions for S33; D1 PCS consumption-site at PCS.cs:326-339 (verified); D2 LIVE-read with documented determinism gap (Phase 4e launch-blocking); D3 fail-closed for PCS-routed + fallback for non-rule-engine HTTP consumers; D4/D5/D7 NOT NEEDED under D2 reversal; D6 DELETE dead TimeEndpoints `/calculate*`; D8 ~11 task enumeration including new `EmploymentProfileResolver` creation + new `UserAgreementCodeChanged` event.
- **Process learning** at ADR-023 §Refinement Trail: cycle 1 alternative-surfacing discipline is load-bearing. Future deferred-design sprints should explicitly enumerate cycle-1-alternative options as forks before absorbing.
- **Third canonical thrash-defer case** (S28 = 1st; S31 cycle-2-converging-finite = 2nd control case; S32 = 3rd canonical thrash) — `feedback_thrash_defer_real_world.md` smoke alarm operates as designed. S32 design-only sprint pattern (mirrors S28 → S29 split) is a stable mitigation.
