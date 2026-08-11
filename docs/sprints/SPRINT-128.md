# Sprint 128 — Post-send integrity: prove the send, stop the drift, gate the reads

**Opened**: 2026-08-11 · **Status**: OPEN · **Base**: `d7528d0` (S127 merge)
**Refinement**: `.claude/refinements/REFINEMENT-s128-open.md` (rev 2, code-grounded, READY)

## Sprint Goal

S127 shipped one validated send command; this sprint makes that claim *whole*. Four ways a sent month
is still weaker than it looks, each closed or honestly re-scoped:

1. **The headline E2E is unproven** — written, typechecked by hand, never executed (FU-A), and the CI
   hole that allowed that is still open (FU-B).
2. **A sent month's figures can still drift** — `POST /api/time-entries` writes into
   `EMPLOYEE_APPROVED`/`APPROVED` months with no status check (FU-D1).
3. **A rejected/un-submitted month's figures are still readable** through sibling endpoints the
   Teamoversigt expander itself calls (FU-D2, the RES-002 follow-up — sliced, see R2).
4. **The demo-seed rerun is not write-free** for REJECTED months (FU-C).

Priority order: FU-D1/FU-D2 are P7 (security/access control); FU-A/FU-B are P8 (CI/CD enforcement);
FU-C is tooling. P7 work therefore cannot be dropped to protect the P8 items if the sprint tightens.

## Owner Rulings (2026-08-11)

- **R1 — Actor model for the read gate: TIERED.** Self exempt; HR-or-above exempt (the corrective
  tier — you cannot correct what you cannot read; TASK-12404/12405 precedent); leaders gated by
  `ApprovalVisibility.IsSubmittedToManager`. This resolves the RES-002 §OPEN QUESTION (two coexisting
  actor models) in favor of the month-GET precedent. The team-overview's actor-blind withholding is
  NOT retrofitted this sprint — it stays as shipped; the ruling governs the NEW gates.
- **R2 — RES-002 scope: SLICE + RECORDED DEFERRAL** *(corrected at Step 0b cycle 1)*. S128 gates the
  **3** endpoints that already carry year+month (`approval/{id}/allocation-breakdown`,
  `compliance/{id}/period`, `balance/{id}/summary`). The plan's original 4th,
  `overtime/{id}/balance`, takes **year only** (`OvertimeEndpoints.cs:20-23,41` reads
  `GetByEmployeeAndYearAsync`) — no month to resolve a period against, so it moves to the remainder.
  Corrected census: **12 verified total, 3 gateable now, 9 remainder (7 without month parameters)**.
  The remainder is deferred WITH this census written into the KB entry — a documented scope, not a
  fourth drift.
- **R3 — `/api/time-entries` guard semantics: MIRROR SKEMA BYTE-IDENTICALLY.** Locked set is
  `{EMPLOYEE_APPROVED, APPROVED}` → **409** (not 403 — the S127 contract sweep standardized 409;
  not "except-REJECTED" — that would newly block `SUBMITTED` and silently reopen R6). Status-only,
  all actors including HR, same as Skema's `IsPeriodLockedForSave` today.
- **R4 — Reopen read-fork stays OPEN.** The gates apply the S124 owner ruling literally (a reopened
  month is DRAFT ⇒ withheld from leaders). Distinguishing leader-reopen from self-reopen via
  `PeriodReopened.PreviousStatus` remains a recorded, un-ruled follow-up.
- **R5 — NARROW-ONLY composition** *(added at Step 0b cycle 1 — Reviewer B2)*. The 3 target endpoints
  have THREE different pre-existing auth shapes (allocation-breakdown: designated-edge only under
  `LeaderOrAbove`, no self branch — pinned by `Breakdown_Employee_IsForbidden_LeaderOrAbovePolicy`;
  compliance: self + org-scope + designated-edge; balance/summary: self + org-scope, NO
  designated-edge). Applying R1's tiering uniformly would therefore **widen** access on endpoints
  where self or vikar have none today — the S124 unratified-widening defect class. Ruling: **each
  endpoint's existing access population is untouched**; the R1 tiering decides only *who among the
  already-authorized is exempt from withholding* (self where self already passes; HR-or-above where
  org-scope already admits them). The gate may only subtract, never add.
- **R6 — Withhold shape: 403 — RATIFIED (owner, 2026-08-11; discharges Codex B1)**. The two S124
  precedents differ observably: the Skema month GET returns 403 to the leader tier; the
  team-overview returns 200-with-null-figures. For the 3 sliced endpoints the whole response IS the
  figures — a 200 with every field nulled is a 403 that lies about itself, and nullable fields
  invite the recorded RES-002 trap (a null "fixed" to 0). The existence of an un-submitted month is
  not a secret from its own designated approver (the team-overview's job is showing exactly that),
  so the 200-shape protects nothing here. **403**, matching the month-GET precedent; PAT-012
  surface change is `.Produces(403)` only, no nullable contract rework. The FE error branches this
  requires are already owed under the PAT-016 both-arms check (TASK-12804).

## Task Decomposition

### TASK-12800 — FU-A: read the `d7528d0` `e2e-tests` verdict (Orchestrator, FIRST — sprint gate)
The run exists (master push gates `e2e-tests`, `ci.yml:265`); `gh` is not installed on this machine,
so the verdict is read via the GitHub UI/API. **Green** → AC-16 (S127) is proven; record the run id
here and in the S127 log's FU-A. **Red** → the E2E is a genuine RED and becomes this sprint's first
bug-fix task before anything else dispatches.
**AC-1**: verdict + run id recorded; AC-16 declared proven or converted to a fix task.

### TASK-12801 — FU-B: the e2e typecheck gate (UX agent scope: `frontend/**`, `.github/workflows/ci.yml`)
New `frontend/tsconfig.e2e.json` extending the base: `include: ["e2e", "playwright.config.ts"]`,
`types: ["node"]` (override — NOT additive; the base pins `vitest/globals`). Add `@types/node` to
devDependencies (missing; `lazy-routes.spec.ts` imports `node:fs`/`node:path` + `__dirname`,
`playwright.config.ts` uses `process.env`). New script `typecheck:e2e: tsc -p tsconfig.e2e.json`.
One CI step in the **frontend-build** job (not `e2e-tests` — no compose stack needed; keeps the
type gate independent of browser flake). Expect pre-existing type errors to surface across ~1.1k
never-typechecked spec lines (`strict` + `noUnusedLocals` newly apply) — fixing those is in scope.
**AC-2**: CI typechecks `frontend/e2e/**`; proven RED with a deliberately-broken spec, then restored.

### TASK-12802 — FU-C: write-free demo-seed rerun (Integration/tooling agent scope: `tools/StatsTid.DemoSeed/**`, `tests/StatsTid.Tests.DemoSeed/**`)
Probe-first on the period stage, matching the loader's own unit-stage discipline
(`tools/StatsTid.DemoSeed/Loading/DemoLoader.cs:212-215`): all manifest activity rows share ONE
month (`tools/StatsTid.DemoSeed/Generation/DemoGenerator.cs:487-489`), so a single
`GET /api/approval/by-month` under the demo GLOBAL_ADMIN yields `employeeId → status` for all ~375
periods in one call. New `ApiClient.GetPeriodsByMonthAsync`; skip send/approve/reject when observed
status already equals the target; new `PeriodsAlreadyInTargetState` counter. Extract the decision as
a pure `PeriodLoadPlanner` and single-source the outcome→status mapping (today it lives only in
`DemoVerifier.cs:319-327`). Keep the 409-tolerance branch — it still covers the genuine
already-past-sendable race.
**AC-3** *(both arms — Step 0b cycle 1, Reviewer W4: a planner test alone is satisfiable without
wiring the loader; denominator pinned by Codex N5)*: (a) planner unit test in the existing
pure-planner style pins that a rerun over target statuses plans zero period writes, including
REJECTED; **and** (b) loader evidence — a rerun against a loaded stack reports
`PeriodsAlreadyInTargetState` == the count of **outcome-bearing** periods (`PeriodOutcome != NONE`;
NONE rows never enter the send stage, `DemoLoader.cs:592`) and zero send/approve/reject calls issued.

### TASK-12803 — FU-D1: the post-send write guard (Backend agent scope: `src/Backend/**`, `docs/api/openapi.json` via PAT-012, `frontend/src/lib/api-types.ts` generated, `frontend/src/hooks/useTimeEntries.ts`, tests)
Lift `IsPeriodLockedForSave` + the 409 conflict shape out of `SkemaEndpoints.cs:143-153` (private
scope) into shared code — the `ApprovalVisibility.cs` precedent exists for exactly this drift hazard;
Skema's call sites converted to the shared symbol (empty behavior diff there). In `TimeEndpoints`,
insert the authoritative check between lock-acquire (`:158-159`) and enqueue (`:161`) via the in-tx
`ApprovalPeriodRepository.GetByEmployeeAndPeriodAsync(conn, tx, …)` overload (PAT-015; the
self-managed overload must NOT be used). Month derived from `request.Date`. The endpoint already
holds the S127 lock + ReadCommitted pin — only the status read is missing, as its own comment says
(`TimeEndpoints.cs:154-157`).
PAT-012 pipeline: `.Produces(409)` → regenerate openapi.json → `gen:api` → spec≡runtime
(`S120TimeSpecRuntimeTests`). Frontend: 409 handling in `useTimeEntries`/`TimeRegistration`.
Tests (RED-on-old, restore-from-backup falsification per S127 practice): (a) plain post-send 409 on
both locked statuses + still-201 on DRAFT/SUBMITTED/REJECTED/no-row; (b) a send-wins-then-POST
concurrency arm beside AC-7e in `SendConcurrencyTests` (third-connection advisory-lock forcing, not
`Task.WhenAll`). The S124 write-floor trio (`S91TreePageHrAccessTests`) must stay green unmodified.
**Known residual, carried not fixed**: the natural-key probe matches whole-month
`period_start/period_end` exactly; non-whole-month rows can be missed ("no row ⇒ allow"). Shared
with Skema; pre-existing; out of scope.
**AC-4**: guard live per R3; single predicate definition with both call sites; RED-on-old proven.

### TASK-12804 — FU-D2: the RES-002 slice (Security agent scope: `src/Backend/**` endpoints named in R2, `docs/api/openapi.json` via PAT-012, `frontend/src/lib/api-types.ts` generated, `frontend/src/hooks/useAllocationBreakdown.ts` + `useCompliance.ts` error branches, `tests/**`; NEEDS R1+R2+R5+R6 — dispatch blocked until TASK-12803's shared-predicate lift lands, to avoid two agents moving the same file)
First extract the tiering logic from `SkemaEndpoints` (the `leaderTierRead` flag — **re-locate by
symbol, not by line**: TASK-12803 shifts every line in that file) into a shared helper; without
that, every future gate is a hand-copy, the drift RES-002 warns about (`RES-002:79-80`). Then gate
the **3** sliced endpoints per R1 tiering × **R5 narrow-only composition** ×
`IsSubmittedToManager` × **R6 (403)**: within each endpoint's EXISTING access population, actors
below the exemption tier get 403 on non-submitted months. The gate may only subtract access, never
add — the three endpoints' differing auth shapes (see R5) are preserved as-is. **Terminated-leaver
posture (Codex B2 correction)**: NONE of the 3 sliced endpoints admits leavers today — balance/summary
uses the active-only validator, and the S70 leaver allowlist covers other surfaces entirely. The AC
is therefore to PRESERVE the existing denials unchanged (`TerminatedEmployeeAccessTests` stays green
unmodified), not to pin a leaver-access arm that has never existed here.
**PAT-012 pipeline, named** (Step 0b cycle 1, Reviewer W6; suites corrected by Codex W4):
`.Produces(403)` on all 3 routes → regenerate `docs/api/openapi.json` → `gen:api` +
`git diff --exit-code` → the spec≡runtime suites for **all three surfaces** re-verified:
`S116ApprovalSpecRuntimeTests` (allocation-breakdown) **plus** `S120ComplianceSpecRuntimeTests` and
`S120BalanceSpecRuntimeTests` (compliance/period and balance/summary are S120 surfaces, not S116) —
the convention gate "can lie" without the spec≡runtime closer (PAT-012).
**PAT-016 both-arms check, owned here** (Step 0b cycle 1, Reviewer W7): `TeamOversigt.tsx` keys
`canExpand` on `row.normRegistered !== null` — the withheld figure — so the expander never fires
the gated fetch for a non-submitted month and the new 403 path is UNREACHABLE from the UI by
construction. Per PAT-016, both arms must be falsified: (a) the 403 pinned at the HTTP level
(RED-on-old), and (b) the FE consumers (`useAllocationBreakdown`, `useCompliance`) given a real
403 branch (not a dead one) — verified by unit test, since no UI path can exercise it.
**AC-5**: 3 endpoints withhold per R1×R5×R6; `_R5Gap` flipped; the legitimate arms pinned (self
where self already passes, HR-or-above, submitted-month leader, submitted-month vikar on the
endpoints where the designated-edge branch exists); zero access widenings — pinned by keeping the
existing forbidden-arm tests green unmodified (`Breakdown_Employee_IsForbidden_LeaderOrAbovePolicy`,
the `TerminatedEmployeeAccessTests` denials, et al.).

### TASK-12805 — Docs (Orchestrator-only)
(a) Correct the RES-002 KB census: 6 listed → **12 verified** (adds `flex-balance`, `balance/series`,
`compliance/compensatory-rest`, `overtime/{id}/balance|pre-approvals|compensation-choice`); record
R1+R5+R6 as the ruled actor/composition/shape model; record the R2 deferral as the remainder's
scope — **3 gated this sprint, 9 remaining, of which 7 lack month parameters** (`overtime/{id}/balance`
included: year-only); flip the entry's follow-up status to PARTIALLY CLOSED (slice) + remainder OPEN.
(b) Stale-citation sweep found during investigation: `ApprovalVisibility.cs:27`,
`ApprovalEndpoints.cs:1048`, `SkemaEndpoints.cs:234` (targets moved; per the S127 drift warning,
verify at fix time — do not trust these line numbers either).
(b2) **ADR-012 Locking Behavior amendment (Codex W3) — PULLED FORWARD, executed at TASK-12803's
close, not held for this task's slot** *(cycle-2 sequencing fix: 12805 runs last, but the amendment
must not trail the code)*: `ADR-012:49` still mandates "Batch save returns 403" while the shipped
contract is 409 (S127 sweep flagged it; the ADR text was never amended) — and 12803 extends the 409
contract to a second endpoint. Docs are Orchestrator-only, so the Orchestrator lands the ADR
amendment (403→409, with an S127/S128 note) in the same close commit as 12803's acceptance.
Listed here for accounting; executed there.
(c) S127 log: mark FU-A/FU-B/FU-C/FU-D dispositions with this sprint's task ids.
(d) Sprint close per `sprint-test-validation` (run the suites; previous + delta = current).

## Sequencing

TASK-12800 (gate) → TASK-12801 ∥ TASK-12802 (independent) → TASK-12803 **(+ Orchestrator lands the
ADR-012 409 amendment in the same close, per 12805(b2))** → TASK-12804 (needs 12803's lift) →
TASK-12805 (close). If 12800 returns RED, the fix task takes priority over 12801/12802 and the P7
tasks are re-scoped before dispatch, not silently squeezed.

## Expected-RED window — CORRECTED at Step 0b cycle 1 (Reviewer B3)

The plan originally declared exactly one flip; the review's census found **six confirmed**, because
`IsSubmittedToManager(null)` is fail-closed — a leader-tier actor on an employee with NO
`approval_periods` row is withheld, and five existing tests assert 200/figures in exactly that
state:

1. `RejectedMonthVisibilityTests.RejectedMonth_StillDisclosedByAllocationBreakdown_R5Gap` — flips
   BY DESIGN; rewritten to pin the gate.
2. `S116ApprovalSpecRuntimeTests.AllocationBreakdown_Get200_…` (LocalLeader on `s116a_e3`, which the
   file itself documents as having no period row) — an auth flip with an UNCHANGED wire shape; the
   "only if wire shape changes" trigger was wrong.
3. `AllocationBreakdownEndpointTests.Breakdown_DesignatedApprover_CrossAfdeling_Is200`
4. `AllocationBreakdownEndpointTests.Breakdown_CrossAfdelingVikarApprover_Is200`
5. `AllocationBreakdownEndpointTests.Compliance_CrossAfdelingVikarApprover_PassesAuth_NotForbidden`
6. `AllocationBreakdownEndpointTests.Compliance_CrossAfdelingEscalationApprover_PassesAuth_NotForbidden`

Tests 3–6 verify AUTH reachability (vikar/escalation/cross-afdeling edges), not disclosure — the
fix is to seed a SUBMITTED period in their arrange, preserving what they actually test, NOT to
loosen the gate. Cleared as inert by the same census (do not re-flag): `YearOverviewTests.Auth_LeaderInScope_Returns200`
(targets `year-overview`, not a sliced endpoint), `TeamOverviewAggregateTests` (comment-only hit),
`TerminatedEmployeeAccessTests` leader arms (exercise the validator, not the 3 endpoints).

Standing instruction (the S127 lesson, now bitten twice): the dispatched agent must run its OWN
census and bring any flip outside this list back to the Orchestrator — absorbing one silently or
loosening the gate to stay green are both defects.

## Known-accepted holes (unchanged by this sprint — do not "fix")

| Hole | Ruling | Note |
|------|--------|------|
| Legacy `SUBMITTED` rows manager-approvable without validation | R6 (S127) | R3 deliberately keeps `SUBMITTED` writable |
| `ProjectionBackfillService` writes projections unlocked | §3.4 exception (S127) | runbook-gated |
| Non-whole-month period rows missed by the natural-key probe | pre-existing, shared with Skema | recorded in TASK-12803 |
| Reopen read-fork (leader blinded after reopen) | R4 — stays open | `PeriodReopened.PreviousStatus` is the future discriminator |
| RES-002 remainder: 9 endpoints (7 without month parameters) | R2 deferral | scope recorded in the corrected KB census |

## Review posture

Step 0b plan review is MANDATORY (P7 authorization work). The external lens (`codex` CLI) was
missing at sprint open; per owner instruction it was installed (0.147.0) and authenticated
mid-review, and the full dual-lens Step 0b ran — the trail below. Standing rule reaffirmed: a
missing external lens is a HALT-AND-ASK, never a proceed-single-lens-with-a-footnote.
Step 5/7a per WORKFLOW.md as usual, dual-lens.

**Step 0b cycle 1 (internal lens, 2026-08-11): 3 BLOCKER / 4 WARNING / 3 NOTE — all absorbed.**
- **B1** `overtime/{id}/balance` is year-only → slice corrected 4→3, census corrected 3/9 (R2).
- **B2** uniform tiering would WIDEN access on endpoints with narrower auth today → R5 narrow-only
  composition added; zero-widening pinned in AC-5.
- **B3** expected-RED census was wrong (1 declared, 6 confirmed) → section rewritten with the
  verified list + fix guidance (seed a SUBMITTED period in the auth-reachability tests' arrange).
- **W4** AC-3 was satisfiable without wiring the loader → both arms restored.
- **W5** "withhold" shape was unruled → R6 (403) proposed; owner may override before 12804 dispatch.
- **W6** PAT-012 steps unnamed for 12804 → named in the task.
- **W7** the new 403 path is UI-unreachable (`canExpand` keys on the withheld figure) → PAT-016
  both-arms check added to 12804 with frontend scope.
- **N8** line citations into SkemaEndpoints go stale after 12803 → relocate-by-symbol instruction.
- **N9/N10** path fixes + inert candidates trimmed from the census.

**Step 0b cycle 1 (external lens, Codex, 2026-08-11): 2 BLOCKER / 2 WARNING / 1 NOTE — lens
divergence confirmed; none of these were internal-lens findings.**
- **B1** R6 was simultaneously "ruling" and "owner may override" — not dispatch-ready while
  ambiguous → owner ratification obtained (see R6, ratified below).
- **B2** AC-5 demanded a terminated-leaver arm that CANNOT exist — none of the 3 sliced endpoints
  admits leavers today (balance/summary uses the active-only validator; the S70 allowlist covers
  other surfaces); pinning it would have required an out-of-scope access widening → AC rewritten to
  preserve the existing denials unchanged.
- **W3** `ADR-012:49` still mandates 403 for locked saves while the shipped contract is 409, and
  12803 extends 409 to a second endpoint → ADR amendment added to TASK-12805(b2), due before
  12803's close.
- **W4** the plan named only the S116 spec≡runtime suite for 12804, but compliance and balance are
  S120 surfaces — two of three changed routes would have shipped runtime-unverified → both S120
  suites named in the pipeline.
- **N5** AC-3's counter denominator was ambiguous (`PeriodOutcome == NONE` rows never enter the
  send stage) → denominator pinned to outcome-bearing periods.

**Step 0b cycle 2 (external lens, Codex, 2026-08-11): 2 BLOCKER — both absorption defects from
cycle 1's edits, both fixed mechanically without a cycle 3 (finite missed-facts in the same defect
family; S34 precedent per `feedback_thrash_defer_real_world.md`). Cycle cap (2/2 external) reached.**
- **B1** AC-5's parenthetical still listed "terminated-leaver" as a legitimate arm after the task
  body was rewritten to preserve-denials → arm removed; denials moved to the zero-widening pin.
- **B2** 12805(b2) required the ADR-012 amendment BEFORE 12803's close while 12805 was sequenced
  last — impossible as written → amendment pulled forward into 12803's close (Orchestrator-only
  docs write), 12805 keeps it for accounting; sequencing line updated.

**Step 0b verdict: CONVERGED — dual-lens, internal 1 cycle (3B/4W/3N absorbed) + external 2 cycles
(2B/2W/1N absorbed; cycle-2 2B absorption defects fixed). R1–R6 owner-ratified. Plan is
dispatch-ready.**

## Tasks Completed

### TASK-12803 — FU-D1: the post-send write guard ✅ 2026-08-11 · **owns AC-4 (guard live; regression proof CI-pending)** · Orchestrator-reverified
**Agent**: Backend (main tree) + Orchestrator checkpoint. **Backend files**: NEW
`ApprovalPeriodSaveLock.cs` — the lifted shared pair (`IsPeriodLockedForSave` +
`PeriodLockedForSaveConflict`), bodies byte-identical to the deleted SkemaEndpoints privates,
sibling of `ApprovalVisibility.cs` with the same drift-hazard rationale; SkemaEndpoints both call
sites converted (**behavior-empty diff** — Orchestrator-verified: no predicate/409/ordering change,
zero remaining references to the old privates); TimeEndpoints gains the R3 guard in-lock, in-tx,
via the `(conn, tx)` overload, after `EmployeeConsumptionLock.AcquireAsync` and before the enqueue,
explicit rollback + the shared 409, known-residual comment (whole-month natural-key probe) at the
call site, `.Produces(409)` metadata.
**Tests**: NEW `TimeEntryPeriodLockTests.cs` (409 on EMPLOYEE_APPROVED + APPROVED with ZERO-delta
TOTAL projection/outbox counts per the S127 F6 lesson; 201 preserved on DRAFT/SUBMITTED/REJECTED/
no-row) + `S128_SendWinsThenTimeEntry_PostObservesNewStatusInLock_And409s` beside AC-7e (real
forcing: third-connection advisory lock + pg_locks waiter-poll). **Posture: WRITTEN-BUT-NOT-EXECUTED
— no docker/postgres on this machine**; compile + discovery proven (`--list-tests` names all 7),
falsification documented at code-review level (the guard block is the revert target; both 409 arms
+ both zero-delta counts catch it; the pre-lock/self-managed-overload/RepeatableRead mutants die on
the concurrency test; a SUBMITTED-widening mutant dies on the 201 arm). **Proof lands in CI's
services-postgres regression step.** S124 write-floor trio untouched and unaffected (no-row⇒allow).
Unit suite 868/868 (Orchestrator re-ran); solution build 0 errors, 0 new warnings.
**Orchestrator checkpoint (C1, S127 precedent)**: openapi.json regenerated via the no-DB
`--openapi` entrypoint — diff exactly the expected +3 lines (the 409 on POST /api/time-entries);
`gen:api` regenerated `api-types.ts` (+7). ⚠ Python is ALSO missing on this machine, so
`check_openapi_sync/convention` could not run locally — the sync gate's substance (regen+compare)
was performed manually; both gates run in CI's docs job. **Frontend (PAT-016 both-arms, done
here)**: `TimeEntryForm`'s `try/finally` had NO catch — a 409 was an unhandled rejection, the user
saw NOTHING (a dead error path by construction). Added catch + JSON-body message parse + the
`styles.alert`/`role="alert"` convention display; NEW `TimeEntryForm.test.tsx` pins both arms
(parsed-message refusal render; success-clears-refusal + field reset) — vitest 730 → **733**.
E2E comment naming the predicate's old home updated (`skema-registration.spec.ts`).
**ADR-012 amended at this close per Step-0b Codex W3 (12805(b2) pulled forward)**: the stale
"Batch save returns 403" → 409, single-source + second-endpoint note added.
**Frontend gates after checkpoint**: typecheck:e2e 0 · lint 0 · build 0 · vitest 733/733.

### TASK-12802 — FU-C: the write-free demo-seed rerun ✅ 2026-08-11 · **owns AC-3 (arm (a) proven; arm (b) written-but-not-executed)** · Orchestrator-reverified
**Agent**: Integration/Tooling (main tree). **Files**: NEW `Loading/PeriodLoadPlanner.cs` (pure
static planner + the single-sourced outcome→status mapping); `ApiClient.GetPeriodsByMonthAsync`;
`DemoLoader` probe-first period stage (one by-month GET per distinct outcome-bearing month, planner-
driven steps, new `PeriodsAlreadyInTargetState` counter, 409 branch kept as the race safety net);
`DemoVerifier.ExpectedPeriodStatus` now a delegating shim to the planner's single source;
`Program.cs` summary prints `alreadyInTargetState=` (the arm-(b) evidence line). NEW
`PeriodLoadPlannerTests.cs`: **94 → 122 (+28), all green, Orchestrator re-ran: 122/122.**
Planner semantics better than asked: the probe supplies the periodId, so partial states
(observed `EMPLOYEE_APPROVED`, target `APPROVED`/`REJECTED`) plan only the remaining manager act —
both endpoints verified to accept `EMPLOYEE_APPROVED` as source. Locked-`APPROVED` drift plans the
full sequence and 409s into `PeriodsAlreadySent`, byte-identical to pre-S128 behavior.
**RED-proof**: the FU-C bug reintroduced (`&& target != "REJECTED"`) → exactly the 3 guarding tests
failed → restored from scratchpad backup (a Copy-Item mtime gotcha caused one stale-DLL rerun,
caught and re-verified — the FAIL-005 class).
**Arm (b) WRITTEN-BUT-NOT-EXECUTED, reason correct**: this machine has NO container runtime (no
docker CLI, no WSL) and no general internet — a PAT-017 isolated stack cannot exist here. The
evidence line is implemented and obtainable on any docker-capable machine: a rerun prints
`periods: sent=0 alreadyInTargetState=<N> alreadySent(409)=0 approved=0 rejected=0`.
**⚠ ENVIRONMENT FINDING (Orchestrator-verified)**: SDK 8 is GONE from this machine — only
10.0.302 installed (net8.0 runtimes 8.0.29 remain), so `global.json` (`8.0.0`/`latestFeature`)
cannot resolve and `dotnet build`/`test` FAIL from inside the repo. Workaround in use: invoke
dotnet from OUTSIDE the repo root (bypasses global.json, SDK 10 targets net8.0 fine). Owner
decision pending: reinstall SDK 8 vs relax the S39 pin. Docker absence additionally means
Docker-gated regression tests can only be proven in CI from this machine.

### TASK-12801 — FU-B: the e2e typecheck gate ✅ 2026-08-11 · **owns AC-2** · Orchestrator-reverified
**Agent**: UX/Frontend (main tree). **Files**: NEW `frontend/tsconfig.e2e.json` (extends base;
`include: ["e2e", "playwright.config.ts"]`; `types: ["node"]` overriding the base's pinned
`vitest/globals`); `package.json` +`typecheck:e2e` script +`@types/node ^20.19.43` (was MISSING —
`lazy-routes.spec.ts` needs `node:fs`/`__dirname`); one CI step in `frontend-build` after lint,
before build (`ci.yml:257-263`) — deliberately NOT in `e2e-tests`, so the type gate is independent
of the compose stack and browser flake.
**Pre-existing errors surfaced**: exactly ONE in ~1.1k never-typechecked lines —
`organisation.spec.ts(50)` TS6133 dead `STY02` const; removed, doc comment preserved, zero runtime
change.
**AC-2 RED-proof**: deliberate `string`→`number` tamper in `helpers/dates.ts` → gate FAILS with
TS2322 (exit 2) → restored from scratchpad backup (not `git checkout`), SHA256-identical pre/post,
absent from `git status`.
**Gates**: `typecheck:e2e` 0 · lint 0 · build clean (src tsc unaffected) · vitest 730/730.
Orchestrator re-ran `typecheck:e2e` (exit 0) and inspected the CI step placement. No stale
citations, no cross-domain dependencies.

### TASK-12800 — FU-A: the `d7528d0` e2e-tests verdict ✅ 2026-08-11 (Orchestrator) · **AC-16 (S127) PROVEN**
**GREEN, and verified genuinely exercised — not assumed from the workflow badge.** Run
`31412597781` (StatsTid CI, master push 2026-08-10T17:09Z), job `93533977106` "E2E tests
(Playwright against the docker-compose stack)": success. The job's 2m52s wall-time triggered a
FAIL-006-class suspicion (green-but-didn't-run), so the job log was pulled and read rather than
trusted: **`Running 7 tests using 2 workers` → `1 flaky, 6 passed (31.9s)`**, and the S127 spec ran
and passed on its FIRST attempt, no retry:
`✓ e2e/approval.spec.ts:301 › emp001 registers and allocates a whole month in Skema, sends it, and
mgr03 rejects then approves it (20.6s)` — against the fresh-built compose stack (`down -v` teardown
confirmed in the same log). AC-16's "unproven until CI runs it" is discharged; S127's headline E2E
is real.
**Incidental observation, recorded not actioned**: the lazy-routes spec flaked on first attempt
(`LAZY ROUTES: 19 checked | failures: 11` immediately after webServer boot; retry #1 clean,
`failures: 0`) — absorbed by the owner-ruled `retries:2`. Same surface as the S125/W4 lazy-route
error-boundary history; if the flake recurs across runs it deserves a look at vite warm-up vs the
spec's post-boot timing, but one absorbed flake is not a finding.
**Verdict for sequencing**: no RED → TASK-12801/12802 dispatch as planned, no re-scope.
