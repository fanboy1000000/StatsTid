# Sprint 35 — Phase 4e: S34 Hardening Sweep + AC Family Compensation Seed Bug Fix

| Field | Value |
|-------|-------|
| **Sprint** | 35 |
| **Status** | in-progress |
| **Start Date** | 2026-05-18 |
| **End Date** | _pending TASK-3510_ |
| **Orchestrator Approved** | _pending_ |
| **Build Verified** | _pending_ |
| **Test Verified** | _pending_ |
| **Sprint-start commit base** | `328a027` (S34 close, 2026-05-17) |
| **Sprint type** | Implementation (against ROADMAP rule correction policy committed 2026-05-18) |
| **Refinement** | `.claude/refinements/REFINEMENT-s35-s34-hardening.md` (READY post 2-cycle dual-lens absorption + Step 0b cycle 1) |
| **Plan** | `.claude/plans/PLAN-s35.md` (Step 0a + Step 0b dual-lens) |

## Sprint Goal

Close 6 deferred items from S34 (admin surface ETag, Case A 23505, Overtime D-test, outer-users-UPDATE stale-snapshot, concurrent admin PUT D-test, ADR-016 D5b reframe) PLUS fix the AC family compensation seed bug discovered during S35 cycle-1 refinement (classified as **bug-with-no-past-impact** under ROADMAP rule correction policy committed 2026-05-18 — pre-launch posture, no past periods exist, no retroactive recompute needed).

**S34-deferred closure status (target)**:
- Items #1 (admin ETag), #2 (Case A 23505 → 409), #3 (Overtime D-test discriminator), #4 (outer-users-UPDATE stale-snapshot), #5 (concurrent admin PUT D-test) → **closing in S35**
- Item #6 (ADR-016 D5b reframe) → **DROPPED** per Reviewer cycle-1 BLOCKER (semantic-shift-not-aggregation; D5b's current 5-pattern enumeration is correct; reframe introduced new errors)
- Net: 5/6 CLOSED + 1/6 DROPPED with documented rationale + 1 net-new pre-launch bug-with-no-past-impact correction (AC family `DefaultCompensationModel`)

**Strategic context**: S35 operates under ROADMAP "Deployment Model" section + Phase 4e bullets committed 2026-05-18 — single logical deployment (150 institutions), glocal rule encoding (global interpretation, local-only on rule-delegated parameters), supersession-by-default + bug-correction-when-classified rule correction policy with NO per-institution opt-in/out. AC seed correction is the first concrete application of the bug-correction-when-classified policy under pre-launch posture (free correction window).

**Out-of-scope for S35** (deferred to S36+ multi-sprint program per `.claude/plans/PROGRAM-s36-s41-domain-correctness.md`): full source register + role-within-agreement modeling + ADR-024 + ADR-025 + ADR-027 (post-launch).

## Entropy Scan Findings

Run 2026-05-18 at sprint open (per WORKFLOW.md Step 0a):

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-018 + ADR-019 + ADR-020 + ADR-017 + ADR-013 + ROADMAP Deployment Model + Phase 4e bullets all resolve cleanly post-S34 |
| Pattern compliance | CLEAN | S35 is established admin-strict If-Match pattern application (4th surface after S25 agreement_configs / position_override_configs / wage_type_mappings; users is the last unprotected admin write) |
| Orphan detection | DEBT (carry-forward from S34) | 80+ stale locked agent worktrees under `.claude/worktrees/`; S35 uses non-worktree dispatch so non-blocking |
| Documentation drift | DRIFT-IDENTIFIED | SPRINT-34.md INDEX miscount: claimed 209 Docker-gated passing; true count is 208 (Overtime D-test failing at S34 close — Expected AFSPADSERING / Actual UDBETALING). Correction folded into TASK-3510 close. `danish-agreements.md` missing Compensation Model section (added by TASK-3504). |
| Quality grade review | SCHEDULED | Re-grade at TASK-3510. Backend API **A- → A** candidate (last unprotected admin write closed); Infrastructure stays A; Rule Engine stays A++ (unchanged); Security **B → B+** candidate; Domain Correctness new category — partial credit for AC seed fix; full grading deferred to S41 |
| Refinement disposition | READY | 2-cycle Step 4 dual-lens absorbed + Step 0b cycle 1 dual-lens absorbed; cycle-cap respected (2/2 per lens for refinement; 1/2 for Step 0b — mechanical-only absorptions per `feedback_dont_pause_for_reviews.md`) |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1, P3, P7 — three rows touched plus first concrete application of ROADMAP rule correction policy) |
| **External Codex** | invoked 2026-05-18 — cycle 1: 3B/2W/2N |
| **Internal Reviewer** | invoked 2026-05-18 — cycle 1: 1B/2W/4N (divergent from Codex BLOCKERs — healthy dual-lens behavior, complementary not redundant) |
| **BLOCKERs resolved before Phase 1** | yes — 4 cycle-1 BLOCKERs (3 Codex + 1 Reviewer) all absorbed mechanically; no architectural forks; no user decisions required |

### Findings (cycle 1)

**External Codex (gpt-5.5)**:
- **BLOCKER 1** — TASK-3503 bug classification internally inconsistent ("NO × NO" framing collided with "inverts the rule" wording). **→ Absorbed**: clarified to "was-agreed: NO + materially-wrong-past-impact: NO" with explicit binary framework reference per ROADMAP rule correction policy.
- **BLOCKER 2** — TASK-3503 cross-domain authorization conflicts with validation criteria (read-only audit but "AC-pinned tests updated"). **→ Absorbed**: TASK-3503 scope tightened to read-only inspection + audit list production; test edits (if any surface) folded into TASK-3508/3509; AGENTS.md L35-36 + L44-57 governance respected.
- **BLOCKER 3** — TASK-3508 dependency on TASK-3503 is false (`EmployeeCompensationChoice` discriminator holds independently of `DefaultCompensationModel` seed). **→ Absorbed**: TASK-3508 dependency on TASK-3503 REMOVED; baseline-failure criterion rewritten to "stash the S34 cutover code, not the seed fix"; verified via init.sql:1099 (AC=FALSE) + L1111 (HK=TRUE).
- **WARNING 1** — TASK-3506 missing PUT response ETag validation. **→ Absorbed**: validation criterion added for PUT 200 response carrying `ETag: "<newVersion>"` header + body `version` matching `version_after`.
- **WARNING 2** — TASK-3503 source proof too weak for first ROADMAP correction-policy application. **→ Absorbed**: commit body template requires exact URLs/titles/sections + classifier name + verification date.
- **NOTE 1+2** — Confirmatory; no action.

**Internal Reviewer Agent**:
- **BLOCKER 1** — TASK-3502 cites `AuthEndpoints.cs` as 3rd `SupersedeAndCreateAsync` call site but AuthEndpoints only calls `GetCurrentAsync` (verified at L20+L47). **→ Absorbed**: dropped AuthEndpoints from TASK-3502 scope; "3 endpoint sites" → "2 endpoint sites + 1 seeder".
- **WARNING 1** — TASK-3506 precedent cite improvement — should cite `EmployeeProfileEndpoints.cs:282-291` (FOR-UPDATE re-read + audit + OCE catch with explicit `await tx.RollbackAsync(ct)`) instead of `WageTypeMappingEndpoints.cs:497`. **→ Absorbed**: cite swapped + explicit rollback discipline added.
- **WARNING 2** — TASK-3501 column name `audit_at` vs `employee_profile_audit` precedent `timestamp` (S31 era vs S34 era convention). **→ Absorbed**: precedent cite swapped from `employee_profile_audit` to `user_agreement_codes_audit` (init.sql:584).
- **NOTE 1-4** — S25 line-number freshness check (non-blocking); cross-domain label precision (informational); column DEFAULT decision (kept `'UDBETALING'` as documentary legacy fallback per explicit choice); SUPERSEDED forward-compat confirmed.

### Resolution

All 4 BLOCKERs + 4 WARNINGs absorbed mechanically in PLAN-s35.md. No architectural forks. No user decisions required. Per `feedback_dont_pause_for_reviews.md`, mechanical-only absorptions don't gate dispatch on a verification cycle. **Cycle 2 NOT dispatched** — scope unchanged by absorptions (all were spec clarifications + cite corrections + 1 phantom-site removal).

## Architectural Constraints Verified

_Final assertion in TASK-3510 close._

- [ ] **P1 — Architectural integrity** → 4th surface application of admin-strict If-Match contract (ADR-019 D2), closes the last unprotected admin write; pattern landscape stable (no net-new pattern)
- [ ] **P3 — Event sourcing / auditability** → new `users_audit` table with version-transition columns per ADR-019 D8; admin PUT audit trail under concurrent admin now correctness-guaranteed via FOR-UPDATE + If-Match
- [ ] **P7 — Security / access control** → admin-strict If-Match on `/api/admin/users` PUT (was missing); concurrent PUT race correctness; cross-org binding preserved
- [ ] **NEW: ROADMAP rule correction policy first application** — AC family seed bug correction = first concrete classification + execution under the policy committed 2026-05-18; classifier (Orchestrator/Claude) + verification date (2026-05-18) + source URLs cited in TASK-3503 commit body

## Task Log

11 declared tasks (TASK-3500..3510) across 5 phases. Plan file `.claude/plans/PLAN-s35.md` is source-of-truth for per-task detail.

### Phase 0 — Sprint Open

#### TASK-3500 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3500 |
| **Status** | in-progress |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s35.md`, `.claude/plans/PROGRAM-s36-s41-domain-correctness.md`, `docs/sprints/SPRINT-35.md` (this file), `docs/sprints/INDEX.md`, `.claude/refinements/REFINEMENT-s35-s34-hardening.md` (sync-debt housekeeping per Step 0b cycle-3 Reviewer advisory) |
| **Dependencies** | none |

**Validation Criteria**:
- [x] `.claude/plans/PLAN-s35.md` exists with 11-task decomposition
- [x] `.claude/plans/PROGRAM-s36-s41-domain-correctness.md` exists with 7-sprint program
- [x] `docs/sprints/SPRINT-35.md` exists (this file)
- [ ] `docs/sprints/INDEX.md` has Sprint 35 row (status=in-progress)
- [ ] REFINEMENT cycle-1 vestigial framing reconciled (3 sites: L56, L163, L214)
- [ ] Sprint-open commit lands atop `328a027`

---

### Phase 1 — Sequential Foundation (5 tasks)

(Per-task detail in PLAN-s35.md.)

- TASK-3501 — Schema migration `s35-d1-users-version` + `users_audit` table
- TASK-3502 — `UserAgreementCodeRepository.SupersedeAndCreateAsync` Case A 23505 catch + 409 mapping
- TASK-3503 — AC family compensation seed bug fix (pre-launch bug-with-no-past-impact)
- TASK-3504 — `docs/references/danish-agreements.md` Compensation Model section
- TASK-3505 — `UserRepository.GetByIdWithVersionAsync` methods (in-tx FOR-UPDATE + non-tx variants)

### Phase 2 — Parallel Cutovers (3 dispatch slots, non-worktree)

(Per-task detail in PLAN-s35.md.)

- TASK-3506 — AdminEndpoints `{GET, PUT, POST} /api/admin/users` — admin-strict If-Match + version bumping + users_audit (single-agent serial; same file)
- TASK-3507 — Frontend admin UI migration to `apiFetchWithEtag<T>` + banner-with-retry
- TASK-3508 — Overtime D-test rewrite to PUT compensation-choice (strong discriminator via 400/200)

### Phase 3 — D-Tests

#### TASK-3509 — Docker-gated D-test suite (~8 tests)

Per-task detail in PLAN-s35.md.

### Phase 4 — Sprint Close

#### TASK-3510 — Sprint close

Per-task detail in PLAN-s35.md. ROADMAP Phase 4e: 5 items RESOLVED + 1 DROPPED + AC seed correction documented as first concrete application of rule correction policy.

## Legal & Payroll Verification (TASK-3510)

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | AC family `DefaultCompensationModel` UDBETALING → AFSPADSERING corrects encoding to match AC overenskomst cirkulære §4. Sources cited in TASK-3503 commit body. |
| Wage type mappings produce correct SLS codes | N/A | No mapping changes |
| Overtime/supplement calculations are deterministic | pending | TASK-3508 rewrites the failing-at-S34-close Overtime D-test with a strong PUT-based discriminator; pinning the S34 dated-lookup cutover, not the AC seed. |
| Absence effects on norm/flex/pension are correct | N/A | No absence-rule changes |
| Retroactive recalculation produces stable results | N/A | No rule-engine input surface changes (AC seed correction is forward-only per bug-with-no-past-impact pre-launch classification) |

## External Review (Step 7a)

_Pending Phase 3 completion._

| Field | Value |
|-------|-------|
| **Invoked** | _pending_ |
| **Sprint-start commit base** | `328a027` |
| **Command** | _pending_ |
| **Review Cycles** | _pending — cycle-cap 2 per lens_ |

## Test Summary

_Populated at TASK-3510 via `sprint-test-validation` skill._

Provisional baselines (subject to validation at close):
- **S34 close (per INDEX.md row)**: 858 (526 unit + 35 plain regression + 209 Docker-gated + 88 frontend)
- **S34 close (per cycle-2 verification, true)**: 857 (Overtime D-test failing — Expected AFSPADSERING / Actual UDBETALING). Correction documented in TASK-3510 close + SPRINT-34.md.
- **S35 target**: ~867-868 (+9 Docker-gated + 1-2 frontend vitest) measured against corrected 857 baseline.

## Sprint Retrospective

_Populated at TASK-3510 close._
