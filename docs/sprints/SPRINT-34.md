# Sprint 34 — Phase 4e: `agreement_code` Versioned History (Launch-Blocker Close)

| Field | Value |
|-------|-------|
| **Sprint** | 34 |
| **Status** | in-progress |
| **Start Date** | 2026-05-17 |
| **End Date** | — |
| **Orchestrator Approved** | no |
| **Build Verified** | no |
| **Test Verified** | no |
| **Sprint-start commit base** | `f966c9e` (S33 close, 2026-05-17) |
| **Sprint type** | Implementation (against ADR-023 D2 option (b)) |
| **Refinement** | `.claude/refinements/REFINEMENT-s34-agreement-code-versioned-history.md` (READY after 2-cycle dual-lens; gitignored) |
| **Plan** | `.claude/plans/PLAN-s34.md` (Step 0a) |

## Sprint Goal

Close the `agreement_code` LAUNCH-BLOCKING determinism gap per [ADR-023](../knowledge-base/decisions/ADR-023-employee-profile-versioning-emission-and-rule-engine-cutover.md) D2 option (b) — version `users.agreement_code` via row-level history. 4th application of the established versioned-config pattern (after WTM/EntitlementConfig/EmployeeProfile in S29/S30/S33).

Marquee D-test `ReplayAsync_StableUnderAgreementCodeMutation_ResultByteIdentical` is the load-bearing P4 acceptance gate. **Closes ADR-016 D10 retroactive replay determinism for the entire rule-engine input surface** (4th and final dated input after `weekly_norm_hours`/`part_time_fraction`/`position` from S33).

**Cycle 1 review expanded scope**: not just PCS-replay (the original framing) — Balance/Skema/Overtime past-period HTTP endpoints have the same class of replay-determinism bug. All cut over to dated `UserAgreementCodeRepository.GetByUserIdAtAsync(employeeId, monthStart, ct)` lookups via TASK-3410/3411/3412.

## Entropy Scan Findings

Run 2026-05-17 at sprint open (per WORKFLOW.md Step 0a):

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-023 + S33 references resolve cleanly post-S33 close |
| Pattern compliance | CLEAN | 4th application of established versioned-config pattern; no anti-pattern introduction |
| Orphan detection | DEBT (carry-forward) | 80+ stale locked agent worktrees under `.claude/worktrees/`; S34 uses non-worktree dispatch so non-blocking |
| Documentation drift | CLEAN | MEMORY.md synced through S33 close |
| Quality grade review | scheduled | Re-grade at TASK-3415 (Rule Engine A+ → A++ candidate if ADR-016 D10 fully closed) |
| Refinement disposition | RESOLVED | 2-cycle Step 4 dual-lens reviewed clean; cycle-cap respected (2/2 per lens) |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1, P3, P4, P7 — all four MANDATORY rows touched) |
| **External Codex** | pending — dispatch after TASK-3400 commits |
| **Internal Reviewer** | pending — dispatch in parallel with Codex |
| **BLOCKERs resolved before Step 1** | pending |

### Findings (cycle 1)
_To be populated post-dispatch._

### Resolution
_To be populated post-dispatch._

## Architectural Constraints Verified

_Final assertion in TASK-3415._

- [ ] **P1 — Architectural integrity** → 4th application of ADR-020 D2 + ADR-018 D7 + ADR-019 D2 pattern; pattern landscape stable at 5
- [ ] **P2 — Rule engine determinism** → marquee PASSES; closes ADR-016 D10 for the 4th and final rule-engine input
- [ ] **P3 — Event sourcing** → 3 new event types registered (EventSerializer 56→59); 5-way atomic emission on Case C cross-day; backfill seeder atomic per-row
- [ ] **P4 — Version correctness** → ETag monotonicity preserved on Case C (successor = predecessor.Version+1 per S33 absorption); admin-strict If-Match on DELETE; cycle-3 validator on PUT
- [ ] **P7 — Security** → JWT mint reads through repository; AdminEndpoints PUT/POST scope unchanged (LocalAdminOrAbove); no new exposure

## Task Log

16 declared tasks (TASK-3400..3415) across 4 phases. Plan file `.claude/plans/PLAN-s34.md` is source-of-truth for per-task detail.

### Phase 0 — Sprint Open

#### TASK-3400 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3400 |
| **Status** | in-progress |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s34.md`, `docs/sprints/SPRINT-34.md` (this file), `docs/sprints/INDEX.md` |
| **Dependencies** | none |

**Validation Criteria**:
- [x] `.claude/plans/PLAN-s34.md` exists with 16-task decomposition
- [x] `docs/sprints/SPRINT-34.md` exists (this file)
- [ ] `docs/sprints/INDEX.md` has Sprint 34 row (status=in-progress)
- [ ] Sprint-open commit lands on master

---

### Phase 1 — Sequential Foundation (5 tasks)

(Per-task detail in PLAN-s34.md.)

- TASK-3401 — Schema migration (`user_agreement_codes` + audit table)
- TASK-3402 — `UserAgreementCodeRepository` with `SupersedeAndCreateAsync` + `SoftDeleteAsync`
- TASK-3403 — `UserAgreementCodeBackfillSeeder` (history-covering `effective_from='0001-01-01'` default)
- TASK-3404 — 3 new event types (`UserAgreementCodeSeeded` + `UserAgreementCodeSuperseded` + `UserAgreementCodeSoftDeleted`); EventSerializer 56→59
- TASK-3405 — `EmploymentProfile.AgreementCode` xmldoc clarification

### Phase 2 — Parallel Cutovers (8 dispatch slots)

(Per-task detail in PLAN-s34.md. Phase 2 Disjointness Audit there.)

- TASK-3406 — PCS resolver agreement_code cutover
- TASK-3407 — AdminEndpoints PUT + POST (single-agent serial; same file)
- TASK-3408 — AuthEndpoints JWT mint through repository
- TASK-3409 — Frontend PUT-body sync
- TASK-3410 — BalanceEndpoints past-month determinism (cycle 1 BLOCKER 1 absorption)
- TASK-3411 — SkemaEndpoints past-month determinism (cycle 1 BLOCKER 1 absorption)
- TASK-3412 — OvertimeEndpoints past-period determinism (cycle 1 BLOCKER 1 absorption)
- TASK-3413 — EmployeeProfileRepository:146 audit (likely doc-only per cycle 2 confirmation)

### Phase 3 — D-Tests

#### TASK-3414 — Docker-gated D-test suite (~15 tests)

Per-task detail in PLAN-s34.md.

### Phase 4 — Sprint Close

#### TASK-3415 — Sprint close

Per-task detail in PLAN-s34.md. ROADMAP Phase 4e `agreement_code` LAUNCH-BLOCKING → **RESOLVED** with commit citation.

## Legal & Payroll Verification (TASK-3415)

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | No rule logic changes; agreement_code data-source change only |
| Wage type mappings produce correct SLS codes | N/A | No mapping changes |
| Overtime/supplement calculations are deterministic | pending | Marquee proves byte-identical replay on agreement_code mutation; Overtime past-period D-test pins same-class fix |
| Absence effects on norm/flex/pension are correct | N/A | No absence-rule changes |
| Retroactive recalculation produces stable results | pending | Marquee is precisely the proof; closes ADR-016 D10 for the entire rule-engine input surface |

## External Review (Step 7a)

_To be populated at sprint end._

| Field | Value |
|-------|-------|
| **Invoked** | pending |
| **Sprint-start commit** | `<TASK-3400 SHA>` (filled at close) |
| **Command** | `codex review --base f966c9e` |
| **Review Cycles** | — |
| **Findings** | — |
| **Resolution** | — |

## Test Summary

_Populated at TASK-3415 via `sprint-test-validation` skill._

| Suite | Previous (S33) | Current (S34) | Delta |
|-------|----------------|---------------|-------|
| Unit | 526 | — | — |
| Plain regression | 35 | — | — |
| Docker-gated (passing) | 204 | — | — |
| Frontend vitest | 88 | — | — |
| **Total passing** | **853** | **—** | **—** |

Target: ~868 total (~15 net new D-tests from TASK-3414).

## Agent Effectiveness

_Populated at TASK-3415._

## Sprint Retrospective

_Filled in at sprint close._
