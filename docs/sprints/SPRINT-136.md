# Sprint 136 — Time-control Increment 1: the enforcement core

| Field | Value |
|-------|-------|
| **Sprint** | 136 |
| **Status** | complete |
| **Start Date** | 2026-08-26 |
| **End Date** | 2026-08-26 |
| **Orchestrator Approved** | yes — 2026-08-26 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` Release **0 errors** on the final merged tree (post-Step-7a absorptions) |
| **Test Verified** | **✅ CI GREEN `32974809403`** (all 7 jobs, 2026-08-26, watched to verdict per the S134 lesson): unit **1005/1005** (+10) · DemoSeed **165/165** (+12) · regression **1672/1672** (+74 — every guard/gate/race/migration/reversal/coverage pin passed on FIRST CI execution) · smoke + E2E + frontend + docs green. One remediation on the way: the close push failed the CA2100 ratchet (3 new test-helper sites) → 4 justified suppressions + baseline ratcheted DOWN 116→115 (`0016f33`). |

## Sprint Goal

Implement ADR-040 D1–D3 (+ the D6 pin): the employment window becomes ENFORCED, not just stored.
Registration outside the window is refused (date-free 422s), SEC-046 closes (a terminated
employee's live token can no longer write), the employment-date endpoints gain the cross-field /
re-hire / strand guards under a common lock regime, user-create carries the start date, the
approval send-gate becomes window-aware (the D6 enabler), and the demo seed is reconciled first so
enforcement lands green. First increment of the four-increment program in `SPRINT-135.md`
§Program Plan.

**Inputs:** ADR-040 (accepted 2026-08-25) · refinement
`.claude/refinements/REFINEMENT-s136-increment1-enforcement-core.md` rev 2 — recon-grounded,
dual-lens converged (Codex 1 BLOCKER → cycle-2 clean; Reviewer 1 BLOCKER / 3 WARNING / 5 NOTE →
cycle-2 clean) · owner rulings 2026-08-26: **OQ-1 (a)** payout-listing confirmed by-design ·
**OQ-2 (a)** demo seed reconciled, windows stay.

## Step 0b (plan review)

Served by the refinement's Step 4 dual-lens (2 lenses × 2 cycles, both terminal-clean — the S134
precedent of counting refinement cycles as the plan review). Load-bearing findings absorbed into
the plan: the `EmployeeConsumptionLock` serialization regime (Codex B1); TASK-13607's rescope to
the REAL demo-seed collision cohorts + resequencing before 13601 AND 13603 (Reviewer B1); the
windowless characterization pin STAYS GREEN as parity evidence (Reviewer W1); THREE terminated-
inclusive allowlist surfaces, not two (Reviewer W2); the start-date self-target 403 extends
symmetrically (Reviewer W3); vacuous fully-out-of-window months REFUSE at send (Reviewer N1);
migration census FAILS LOUD on `end < start` — no auto-repair (Codex W1).

## Scope & Task Decomposition

Dispatch waves respect the ruled sequencing (13607 lands before 13601/13603 merge; 13602 before
its consumers). All `docs/**` deliverables (runbook row, db-schema regen, SPRINT-135 amendment,
register updates) are Orchestrator-only; the init.sql segment is drafted by the Data Model agent
and merged only on Orchestrator approval; cross-domain touches carry explicit authorization.

| Task | Wave | Agent | Deliverable |
|------|------|-------|-------------|
| TASK-13607 | 1 | DemoSeed | Generator reconcile: leaver `endDate >= startDate` clamp; active users' start dates clamped below the activity month (no pre-hire activity); full-scale invariant test (deterministic RNG — one run conclusive) |
| TASK-13602 | 1 | Infrastructure | `IEmploymentWindowResolver` (SharedKernel) + `EmploymentWindowResolver` (Infrastructure): NULL-unbounded, end-inclusive, is_active-IGNORING (the window is a fact regardless of login state), self-managed + `(conn,tx)` overloads with the deviation comment; Backend DI registration (cross-domain authorized); unit tests (boundaries, NULLs, missing-user fail-loud) |
| TASK-13605 | 1 | Backend | User-create: optional `employmentStartDate` on the inline DTO + INSERT column + the hand-listed audit `new_data` extension; tests. (OpenAPI + frontend `gen:api` regens run by the Orchestrator at merge — generated `docs/**`/frontend artifacts) |
| TASK-13601 | 2 | Data Model | The `users` CHECK migration segment (S73 pattern, census FAILS LOUD, NULL disjuncts), marker-extracted replay test incl. the violating-row path. Orchestrator approves the init.sql diff + does runbook row and db-schema regen |
| TASK-13603 | 2 | Backend + Security review | `EmploymentWindowGate` (ApprovalPeriodSaveLock shape, ONE date-free-422 builder); both writers gain the gate (skema: all three arrays) + the THREE terminated-inclusive surfaces (validator swap + subject reads, R9c floors, self-exemption yields) under the lock regime; SEC-046 + boundary/NULL/race pins |
| TASK-13604 | 2 | Backend + Security review | Both employment-date PUTs: cross-field 422, re-hire 409 (settlement-independent), strand 409 (+affected-month list, R13 contract), placement before `lifecycleWriter.ApplyAsync`; start-date PUT → terminated-inclusive pair + `EmployeeConsumptionLock` + the symmetric self-target 403 |
| TASK-13606 | 2 | Backend | Window-aware `expectedWorkdays` in `ExecuteSendAsync`; vacuous-empty-month REFUSE; new pins in `SendCommandBehaviourTests`; the windowless characterization pin untouched-and-green |
| TASK-13608 | 2 | Orchestrator (docs) | OQ-1(a) disposition: endpoint by-design comment + S70 test-pin upgrade + register row |
| TASK-13609 | close | Orchestrator | SPRINT-135 posture amendment (S136-dated), follow-up registration (the leaver-send dead-end → Increment 2/3 scoping), sprint-test-validation counts, Step 5a on 13603/13604 (security-review tasks), Step 7a dual-lens, close |

## Architectural Constraints Verified

- [x] Architectural integrity — gate/resolver copy existing house shapes (ApprovalPeriodSaveLock, IOutboxEnqueue split — the SharedKernel stayed driver-free by Orchestrator rework ruling); three gates keep one job each (D3); one-predicate discipline Reviewer-verified at close
- [x] Domain correctness — boundary semantics (end inclusive, NULL unbounded) pinned; windowless behaviour byte-identical (the standing characterization pin untouched-and-green); vacuous out-of-window months REFUSE
- [x] Auditability — user-create audits the new field (the hand-enumerated payload extended); migration never rewrites history (fail-loud census rolling back its own ledger row); the re-hire guard protects completed spells settlement-independently; allowlist inventories truthful (Step-5a W2 fixed)
- [x] Integration isolation & delivery — no event-contract changes; projection backfill (replay) explicitly unguarded; the reversal's strand refusal rides the existing failure-mapping convention
- [x] Security & access control — SEC-046 closed AND race-hardened (Step-5a Codex B1 → in-lock re-check, pg_locks-pinned); FOUR R9c allowlist extensions ruled+reviewed; self-target 403 symmetric; date-free error bodies pinned by property-set + date-regex sweep; SEC-047 adjudicated by-design
- [x] CI/CD enforcement — both drift gates green (OpenAPI byte-identical for 13603; regenerated for 13605); DemoSeed 165/165 at full scale locally; ~60 Docker-gated pins verify in the close push's CI run (established posture)

## External Review (Step 5a / 7a)

### Step 5a — mandatory security review (TASK-13603 + 13604), 2026-08-26

- **Codex: 2 BLOCKER / 1 WARNING / 3 NOTE.** B1 — SEC-046 remained RACE-open: the D3 role floor
  was decided from the unlocked pre-tx subject read; the in-lock section rechecked the window but
  not `is_active` → **fixed** (in-lock subject-state re-read + floor re-enforcement on both
  writers, advisory-then-authoritative; race-pinned per writer). B2 — the settlement-reversal
  end-date write bypassed the strand guard → **owner-ruled 2026-08-26: EXTEND THE GUARD**
  (narrowing-only — a widening cannot strand; ADR-040 D8 clarified in place: the exclusion covers
  the worklist POLICY only, D3's strand invariant is universal) → **fixed** (shared
  `EmploymentWindowStrandCheck` lifted to Infrastructure; reversal checks in its own locked tx;
  both directions pinned). W — skema fast-path 409 precedence comment → truth-up applied.
- **Internal Reviewer: 0 BLOCKER / 2 WARNING / 4 NOTE, all absorbed.** W1 — the strand guard
  missed `work_time_projection` while the write side gates all three arrays → third UNION arm +
  count + pin. W2 — the repository R9c allowlist inventory was stale for the 13603 callers →
  amended; the production-dead `SetEmploymentStartDateAsync` DELETED (the review's "fixture
  caller" turned out to be a same-named private raw-SQL test helper — verified by grep + clean
  post-deletion build; a name-coincidence note added). NOTEs: ReadCommitted pinned explicitly on
  both PUTs; the coverage `missingDays` window-derivation recorded as bounded-by-audience;
  SEC-046 register flip at close (done).
- **The five flagged behaviors, adjudicated (both lenses convergent):** (a) deactivated-HROrAbove
  self-registration ACCEPT per D3's letter — now PINNED as a decision on record; (b) writers'
  refusal-precedence divergence ACCEPT + comment truth-up; (c) start-date GET stays active-only —
  REGISTERED follow-up (HR corrects a leaver's start date with the If-Match fetched via the
  end-date GET; extending the GET is its own R9c ruling); (d) the D3-vs-D8 tension → the owner
  ruling above; (e) the dead setter → deleted.

### Step 7a — sprint close, dual-lens (2026-08-26)

- **Codex (folding the 5a cycle-2): NO BLOCKERS — "both Step-5a blockers are genuinely resolved,
  including both race pins and narrowing/widening reversal pins."** 1 WARNING: the SEC-046
  register row's source-of-truth column still described the PRE-fix code → **absorbed**
  (AS-FOUND/AS-FIXED split with durable grep anchors).
- **Internal Reviewer: CLOSE-WITH-WARNINGS — 0 BLOCKER / 1 WARNING / 3 NOTE.** Verified clean
  across the seams: the lock regime coherent on all FIVE surfaces (same advisory key, first in-tx
  statement, pinned ReadCommitted, identical writer ordering — no deadlock edge); one-predicate
  discipline holds (gate 2 callers, strand check 3, the deleted setter tombstoned); every
  ledger/register/ADR touch consistent with the tree; payroll boundary untouched. **W1 —
  absorbed:** the send gate's inlined window predicate bakes in the single-spell assumption the
  Skema gate's comment forbids → a SPELLS-INCREMENT REVISIT marker now sits on the ApprovalEndpoints
  comment (mirroring the Skema rationale) + the follow-up below. **N1 — absorbed:** the reversal
  failure-mapping comment now enumerates `StrandedRegistrations`. **N2** = this close fill.
  **N3 — recorded, reasoned:** the "boundary + NULL on both paths" AC's Skema single-sided-NULL
  cell is covered by the SHARED SEAM rather than a direct pin — both writers evaluate per-date
  through the ONE gate/resolver whose single-NULL matrix is directly pinned (resolver tests +
  the time-entry writer); a per-endpoint duplicate would pin the same code path twice. Accepted
  as reasoned coverage.

## Open follow-ups (registered at close)

- **Send-gate spells revisit** (Step-7a Reviewer W1): the coverage intersection inlines the
  single-spell predicate — replace with per-date resolver calls when spells storage lands (the
  revisit marker sits on the code; rides the re-hire increment).
- **Start-date GET terminated-inclusive extension** (Step-5a adjudication (c)) — UX/consistency,
  fail-closed today; its own R9c ruling when picked up.
- **Leaver-send dead-end** (13606 named deferral): the send's subject read is active-only, so a
  deactivated leaver's month cannot be sent even by HR — routed to Increment 2/3 scoping.
- **403 reason-string status oracle** (Reviewer N1, ACCEPTED): in-scope sub-HR actors can infer
  termination STATUS (never dates) from the terminated-floor reason string on the writers —
  accepted as bounded; harmonize only with a deliberate ruling.
- **init.sql/EmploymentDateEndpoints "whole-row audit" comment drift** (13605 observation): the
  init.sql claim that `employment_start_date` is captured automatically is false for the
  hand-enumerated CREATED path — comment sweep item.

## Test Summary

| Suite | Count | Delta | Status |
|-------|-------|-------|--------|
| Unit | 1005 | +10 (995 → 1005: the employment-date guard logic matrix) | green locally |
| DemoSeed | 165 | +12 (153 → 165: full-scale window-clamp invariants, RED-proven 11+4) | green locally |
| Regression, non-Docker subset | 102 | ±0 | green locally |
| Regression, Docker-gated | — | ~60 new facts (gates, guards, races via `pg_locks`, migration replay, reversal strand directions, window-aware coverage, SEC-046/SEC-047 pins) | **CI-pending — verifies in the watched close run** |
| Frontend | — | unchanged (generated types only) | CI |
| Full solution build | — | Release, 0 errors, final merged tree | ✅ |

## Sprint Retrospective

**What went well:** the review machinery caught real holes at every altitude — the refinement's
lenses redirected the demo-seed task from a phantom cohort to the two real ones (RED-proven 11+4
at full scale, invisible at smoke scale); Step-5a's external lens caught the SEC-046 fix being
RACE-open (the D3 floor decided outside the lock) and the reversal path's strand bypass, while the
internal lens caught the strand guard missing a whole projection family and the stale allowlist
inventory; Step-7a's cross-task view caught the send gate baking in the single-spell assumption
its sibling comment forbids. Zero of these were visible to the task that created them — the
lens-per-altitude structure is what found them. The Orchestrator rework ruling (Npgsql out of
SharedKernel via the IOutboxEnqueue split precedent) kept the kernel driver-free at the cost of
one agent round-trip. The session-limit termination of all four wave-2 agents cost nothing —
transcript-resume recovered every task, two of them mid-edit.

**What to improve:** two agent-report claims were confidently wrong and survived until a lens or
the Orchestrator checked them — the "one test fixture still calls it" premise (a same-named
private helper) and my own refinement's leaver-cohort framing. The lesson stands: a finding's
blast radius needs the same verification as its existence. The D3-vs-D8 textual tension inside
freshly-ratified ADR-040 shows even a twice-reviewed decision record can carry a latent internal
conflict — caught only when an implementation forced the two sentences into the same transaction.

**Knowledge produced:** the D8 clarification (strand invariant universal, narrowing-only on
settlement rails — owner-ruled); SEC-047 closed by-design with a falsifiable guard pin; the
`EmploymentWindowStrandCheck`/`EmploymentWindowGate` seams; four registered follow-ups. Increment
1 of the ADR-040 program is COMPLETE pending the watched CI run; Increment 2 (calculation
correctness — typed segments, accrual end-cap, QUAL-147) is next.
