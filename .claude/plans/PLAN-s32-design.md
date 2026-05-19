# Sprint 32 — Phase 4d-3 Part 2 DESIGN-ONLY: ADR-023 Authorship

## Sprint Header

| Field | Value |
|-------|-------|
| **Sprint** | 32 |
| **Title** | Phase 4d-3 Part 2 DESIGN-ONLY: ADR-023 — Employee-Profile Versioning Emission + Rule-Engine Cutover Architecture |
| **Status** | DRAFT (Step 0b minimal — design-only sprint, no code surface for Step 0b checklist most items) |
| **Start Date** | 2026-05-16 |
| **Projected End Date** | 2026-05-17 (1-2 days; thin design-only sprint per S28 precedent — 4 tasks, 0 code, ADR drafting + dual-lens review only) |
| **Sprint-start base commit** | `b43de8b` (S31 sprint close, 2026-05-16) |
| **Sprint type** | **DESIGN-ONLY** — produces ADR-023 settling 7 enumerated questions from the deferred S32-implementation refinement at `.claude/refinements/REFINEMENT-s32-phase-4d3-part2.md`. NO code changes; NO test changes. Test counts unchanged from S31 close (833 total). Mirrors S28's deferred-design pattern that produced ADR-020 before S29 implementation. |
| **Refinement** | `.claude/refinements/REFINEMENT-s32-phase-4d3-part2.md` (deferral artifact-of-record). The deferral verdict at the top of that file enumerates the 7 questions ADR-023 must settle. No fresh refinement needed — the deferred refinement IS the design scope. |
| **Agents involved** | Orchestrator-direct (ADR authorship per WORKFLOW.md L48 — KB writes are Orchestrator-only). Reviewer Agent + Codex (gpt-5.5) for the dual-lens ADR review (TASK-3202). |
| **KB entries planned** | New: **ADR-023 "Employee-Profile Versioning Emission + Rule-Engine Cutover Architecture (Phase 4d-3 Part 2 Design)"** filed at `docs/knowledge-base/decisions/ADR-023-...md`. KB INDEX.md updated. No ADR-022 amendment (its §S32-commitment-list stands; this ADR fills in the architectural details S32 design must settle). Optional: ADR-016 D5b extension paragraph if the PCS consumption-site decision adds a sixth pattern (currently 5; depends on Q1 resolution). |

## Sprint Goal

Settle the 7 architectural questions that the deferred S32-implementation refinement (`.claude/refinements/REFINEMENT-s32-phase-4d3-part2.md`, Step 4 cycle 1+2 thrash signal per `feedback_thrash_defer_real_world.md`) exposed. Produce ADR-023 as a binding contract for the S33-implementation sprint that follows. Mirrors S28 → S29 split for ADR-020 → WTM implementation.

The 7 questions ADR-023 must settle (verbatim from refinement deferral verdict):

1. **PCS consumption-site location** for the EmployeeProfile snapshot re-resolution — NOT `MapSegmentToExportLinesAsync` (payroll-only); the rule-engine path needs a different site, likely BEFORE `EvaluateSegmentAsync` builds the per-segment rule payload OR inside `BuildPlanForLegacyCallersAsync` during hydration. Decision-criterion: where does the segment-effective profile need to land so rules see segment-correct values?
2. **MIGRATE vs alternative determinism strategy** for `agreement_code` + `employment_category` — cycle 1 picked MIGRATE; cycle 2 revealed downstream consequence (fallback-to-users-on-soft-delete unviable post-MIGRATE). Re-adjudicate with full architectural awareness OR commit to MIGRATE with the soft-delete consequence accepted (Q3).
3. **Soft-delete consumption semantic post-decision** — fail-closed vs (now-unviable) fallback vs new pattern. Depends on Q2.
4. **Backfill ledger pattern** for UPDATE-with-backfill (S22 D8 INSERT-with-ON-CONFLICT-DO-NOTHING pattern doesn't transfer because the migration is "fill new columns from existing rows", not "insert new rows on conflict"). Needs explicit ledger-entry guard + reverification predicate.
5. **Multi-commit ordering enforcement** between schema migration / backfill / cutover / column-DROP — refinement cycle 2 surfaced that "no admin uses /api/admin/users PUT during S32 sprint days" is unenforceable. ADR-023 must specify either a hard sprint-day dispatch order with reverification gates OR a different protocol that makes intermediate-state safety mechanical, not procedural.
6. **User-model surface cascade** — `User.AgreementCode` is consumed at 25+ Backend.Api call sites. Drop of `users.agreement_code` column propagates to all of them. ADR-023 must decide: (a) full call-site cutover (multi-file scope per consumer); (b) `[Obsolete]` shim on `User.AgreementCode` that proxies through resolver; (c) keep `User.AgreementCode` as a denormalized cache (drift hazard).
7. **JWT-drift admin-visible UI affordance** — drift between live JWT (today's agreement_code) and dated rule-engine reads (yesterday's agreement_code) is invisible to admins. Decision: (a) add UI banner on EmployeeProfileEditor explaining drift; (b) ship without UI affordance (operationally documented only); (c) defer drift-visibility to Phase 5 polish.

## Phase Decomposition

S28 precedent — 4 tasks, sequential, single commit each. No worktree-base-mismatch risk because there are no parallel agent dispatches.

### Phase 0 — Sprint-open plumbing (1 commit)

**TASK-3200** creates `SPRINT-32.md` + `INDEX.md` provisional row + this plan file. No code touches.

### Phase 1 — ADR-023 authorship (1 commit, Orchestrator-direct per WORKFLOW.md L48)

**TASK-3201** drafts `docs/knowledge-base/decisions/ADR-023-employee-profile-versioning-emission-and-rule-engine-cutover.md` settling all 7 enumerated questions with explicit binding decisions. Each decision must be precise enough that S33 implementation can dispatch without follow-up architectural questions.

Mirrors S28 TASK-2801 (ADR-020 write).

### Phase 2 — Dual-lens ADR review (1 commit, Reviewer + Codex)

**TASK-3202** runs Step 7a-equivalent review on ADR-023 DRAFT. Both lenses dispatched against the file. Cycle-cap 2 per lens per `feedback_step7a_cycle_cap_discipline.md`. Goal: convergent clean → flip ADR-023 status DRAFT → ACCEPTED in same commit.

Mirrors S28 TASK-2803 (dual-lens ADR review).

Per `feedback_thrash_defer_real_world.md`, if cycle 2 surfaces NEW BLOCKERs in same architectural area as cycle 1, halt + prompt user. This is the canonical thrash-defer test — second consecutive deferral on Phase 4d-3 Part 2 would warrant rescope (option 3 from the prior AskUserQuestion: split into 3 sub-sprints).

### Phase 3 — Validation (n/a)

Design-only sprint: no `dotnet build` change, no test count change. `sprint-test-validation` skill SKIPPED with rationale documented at sprint close.

### Phase 4 — Documentation + sprint close (1 commit)

**TASK-3203** fills SPRINT-32.md close sections + finalises INDEX.md row + updates ROADMAP Phase 4d-3 Part 2 entry (replaces the "S32 candidate" stub with "S32 design-only COMPLETE; S33 implementation pending") + MEMORY.md S32 line. QUALITY.md unchanged (no code).

Mirrors S28 TASK-2804 (sprint plumbing).

## Task Decomposition (4 declared tasks)

| ID | Name | Owner | Files in scope | Depends on |
|----|------|-------|----------------|------------|
| **TASK-3200** | Sprint-open plumbing — design-only sprint declaration | Orchestrator-direct | `docs/sprints/SPRINT-32.md` (new), `docs/sprints/INDEX.md` (provisional row), `.claude/plans/PLAN-s32-design.md` (this file) | none |
| **TASK-3201** | Draft ADR-023 settling 7 questions | Orchestrator-direct (KB writes per WORKFLOW.md L48) | `docs/knowledge-base/decisions/ADR-023-employee-profile-versioning-emission-and-rule-engine-cutover.md` (new). Optional: `docs/knowledge-base/decisions/ADR-016-temporal-period-handling.md` D5b extension if Q1 decision adds a sixth pattern. Optional: `docs/knowledge-base/INDEX.md` (preliminary entry — final in TASK-3203). | TASK-3200 |
| **TASK-3202** | Dual-lens ADR review (Codex gpt-5.5 + Reviewer Agent) | Orchestrator dispatches both lenses | Read-only review of ADR-023. Cycle 1 + optional cycle 2 fixes via ADR-023 edits. Final commit flips DRAFT → ACCEPTED status. | TASK-3201 |
| **TASK-3203** | Sprint close + INDEX + ROADMAP + MEMORY | Orchestrator-direct | `docs/sprints/SPRINT-32.md` (close sections), `docs/sprints/INDEX.md` (final row), `docs/knowledge-base/INDEX.md` (final ADR-023 entry), `ROADMAP.md` (Phase 4d-3 Part 2 entry: S32 design-only COMPLETE; S33 implementation stub), `~/.claude/projects/C--StatsTid/memory/MEMORY.md` (S32 line) | TASK-3202 |

## Critical-Path Callouts

1. **Sprint type is DESIGN-ONLY** — no code surface; many WORKFLOW.md checklist items don't apply (no `dotnet build` change, no test counts, no QUALITY.md re-grade). The Status field in SPRINT-32.md reflects this with explicit DESIGN-ONLY type tag.

2. **Refinement is the deferral verdict** at `.claude/refinements/REFINEMENT-s32-phase-4d3-part2.md` (top of file). No fresh refinement file. The 7 questions there are the binding scope.

3. **Cycle-cap thrash test** at TASK-3202 — if cycle 2 surfaces NEW BLOCKERs in same area as cycle 1, halt + prompt user. Two consecutive deferrals on Phase 4d-3 Part 2 warrants rescope (split into 3 sub-sprints per the prior AskUserQuestion option 3).

4. **ADR-023 is binding contract for S33** — every architectural decision must be precise enough that S33 implementation refinement has zero "what does ADR-023 mean here?" questions. Specifically: D1 (PCS consumption site) must name the exact method + line range + how the segment-effective profile reaches the rule-engine payload.

5. **Q1 PCS consumption-site decision may add a sixth pattern to ADR-016 D5b** — currently 5 patterns. If ADR-023 D1 introduces a new pattern (e.g., "rule-engine-route consumption-time-lookup" distinct from "export-time consumption-time-lookup"), file as ADR-016 D5b extension paragraph in TASK-3201 alongside ADR-023.

## Test-Count Projection

| Suite | S31 close | S32 close | Notes |
|-------|-----------|-----------|-------|
| Unit | 526 | 526 | No code changes |
| Plain regression | 35 | 35 | No code changes |
| Docker-gated (passing) | 184 | 184 | No code changes |
| Frontend vitest | 88 | 88 | No code changes |
| **Total** | **833** | **833** | Design-only sprint — totals unchanged from S31 close |

## Risk Register

| # | Risk | Likelihood | Mitigation |
|---|------|-----------|------------|
| R1 | **TASK-3202 cycle 2 surfaces NEW BLOCKERs in same area as cycle 1** — second consecutive thrash on Phase 4d-3 Part 2 | medium | Cycle-cap discipline at TASK-3202; if cycle 2 thrash, halt + prompt user for rescope (split into 3 sub-sprints per the deferred refinement's option 3). |
| R2 | **ADR-023 too vague** — S33 implementation refinement reopens with ambiguity | medium | TASK-3201 acceptance criterion: each of 7 decisions has explicit binding language (file + line + method name where applicable). TASK-3202 reviewer lenses probe specifically for vagueness. |
| R3 | **Q1 PCS consumption-site decision may require source-code exploration** that the Orchestrator hasn't done in this session | low | Read `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` lines 320-360 + neighbouring `MapSegmentToExportLinesAsync` to ground D1 in real call-sites. Codex (gpt-5.5) cycle 1 will catch wrong-site claims. |
| R4 | **Q2 MIGRATE re-adjudication** may reverse cycle-1 absorption — could decide MIGRATE is wrong after all | medium | ADR-023 D2 explicitly weighs MIGRATE vs alternatives (per-time-entry agreement_code snapshot via time_entries.agreement_code already-stored; events-table replay-source). Each alternative gets a paragraph in §Alternatives Considered. |
| R5 | **Sprint slipping if cycle 2 fails** — S33 implementation cannot start until ADR-023 is ACCEPTED | low | S33 scope depends on ADR-023; that's by design. If ADR-023 takes longer, S33 starts later. Pre-launch posture (ROADMAP L369) tolerates this slip. |

## Sprint Close Criteria

- [ ] `dotnet build` clean — N/A (no code changes); SKIP rationale documented at sprint close
- [ ] ADR-023 filed at `docs/knowledge-base/decisions/ADR-023-employee-profile-versioning-emission-and-rule-engine-cutover.md`
- [ ] ADR-023 settles all 7 enumerated questions with explicit binding language
- [ ] ADR-023 status flipped DRAFT → ACCEPTED after dual-lens convergent clean
- [ ] ADR-016 D5b extension paragraph filed IF Q1 decision adds a sixth pattern (otherwise SKIP)
- [ ] KB INDEX.md updated with ADR-023 entry
- [ ] SPRINT-32.md status = complete; sprint type tagged DESIGN-ONLY
- [ ] sprints/INDEX.md row complete with design-only sprint note
- [ ] ROADMAP Phase 4d-3 Part 2 entry updated — S32 design-only COMPLETE; S33 implementation pending against ADR-023-settled design
- [ ] MEMORY.md S32 line appended
- [ ] Test totals unchanged from S31 close (833) — sprint-test-validation skill SKIPPED with rationale ("design-only sprint; no code surface; no test counts shift expected")
- [ ] Cycle-cap discipline respected at TASK-3202 (≤ 2 cycles per lens; if cycle 2 thrash, halt + prompt user)

---

## Step 0b Plan Review (light — design-only sprint)

Per AGENTS.md L307: "SKIP rationale" applies when sprint is documentation-only. This sprint is documentation-only (ADR drafting + sprint plumbing).

**Decision**: Step 0b SKIP — design-only sprint, no implementation surface. Cycle 1 thrash already caught architectural concerns at refinement level; ADR-023 review happens at TASK-3202 dual-lens (Step 7a-equivalent). Documenting this skip in SPRINT-32.md sprint log per AGENTS.md L378.

If S32 produces ADR-023 that S33 refinement Step 4 finds inadequate (third consecutive Phase 4d-3 Part 2 thrash), that's the rescope signal (split into 3 sub-sprints).
