# Sprint 145 — payroll works week by week (QUAL-186), then months with a mid-month change become payable (QUAL-149/150)

| | |
|---|---|
| **Status** | **CLOSED 2026-10-21** at `13a40e68` (on K8 `d129468f`), CI green on the first run; the Stage B evidence matched in all four runs (§ "C-5..C-8"), so QUAL-149 is BUILT. Step 7a ran eight cycles on both lenses, the last three past the five-cycle cap by the owner's rulings R-138..R-140; the final cycle on K8: the Opus lens CLOSE, Codex APPROVE-WITH-WARNINGS (wording only; § "C-3 — Step 7a"). Stage A closed on 2026-10-16 (Gate A-CLOSE, CA-1..CA-7); QUAL-186 and QUAL-150 are BUILT. Stage B is fully merged: B.1 (`53a7812`, `fe928dde`), B.2 (`f6fac97b`), B.3 (`8efdfffd`, `8a54484d`); its Unit mutation run caught 12 of 12 and 5 of 5. C-1 is frozen (R-127). Owed after the close: the deletion of the S145-only review-model exception from the close guard, with its own review (handoff). Planning: refinement READY (rev 3.4, Step 4, five cycles); plan READY (`.claude/plans/PLAN-s145.md` plus the task briefs in `.claude/plans/s145/`; Step 0b, five cycles; the Stage B briefs signed at B.0, R-113). Model routing: reviews, rulings and sign-off ran on Opus from 2026-10-19 by the owner's direction (Fable on its usage limit); the close review runs on Opus under the owner's R-104 ruling |
| **Test Verified** | **At the close: CI GREEN `39571040922`** on `13a40e68`, all seven jobs, the first run: Unit 1512 · DemoSeed 170 · **Regression 2150** (the Docker-gated facts included, all executed) · Smoke 7 · frontend · e2e · docs · gitleaks · lizard. **Stage B evidence:** EB-1, EB-2, EA-R1 and EA-R2 each matched its frozen list (§ "C-5..C-8"). **Locally**, O-1 at `8a54484d`, the last code merge: build 0 errors / 145 warnings · CA2100 115 · Unit **1512** · DemoSeed 170 · Regression non-Docker 128 · tsc 0 · vitest **1005**. **Stage A, in CI at `K_A`** (V-A, run `38747749039`, 2026-10-15): all seven jobs green. Unit 1477 · DemoSeed 170 · Regression **2127** (Docker included) · smoke · e2e · frontend · docs · gitleaks · lizard. Locally (O-1 at `07f7c78`): build 0 errors / 145 warnings · CA2100 115 · Unit 1477 (baseline 1309; +168) · DemoSeed 170 · Regression non-Docker 128 · tsc 0 · vitest 1002. `origin/master` is still `cdd8253`: this project pushes master only at sprint close, and CI saw Stage A on the throwaway evidence branch (`s145-evidence-a`, PR #37, now closed and deleted) |
| **Theme** | Payroll stops judging a whole month against one week's norm, which today makes every ordinary month's overtime, merarbejde and flex wrong (QUAL-186). Then a month with a mid-month change to hours, position or agreement becomes payable, with each part under its own wage-type codes (QUAL-149/150). Along the way, a Skema correction stops being counted twice (QUAL-192..194) |
| **Predecessor** | S144: close `cbaa5e0` (CI red: nine test-setup failures) · governance `6b0d982` (review cap 3 → 5; the close guard's CI-health query fix) · post-close fix `8a87d16` (CI green `36610948637`) · docs-only `018ef2a` (QUAL-186), `6849b84` (QUAL-187..191), `de6c228` (the S144 C-9 record), `62fcdc3` (QUAL-192..194) |
| **Base commit (Step 7a)** | **`cbaa5e0`**, the S144 close commit, not HEAD. So `6b0d982` (which touches `.claude/hooks/sprint-close-guard.ps1`, never reviewed on its own), `8a87d16` and the docs commits all fall inside S145's Step-7a diff (`docs/WORKFLOW.md` step 7a) |
| **Orchestrator model** | Opus 5.5 seat throughout (client 2.1.285), under the 2026-09-29 ruling that the seat never switches. Drafting: `planner` (Opus). Judgment: `adjudicator` and `reviewer` (Fable 5.1). External lens: Codex (`gpt-6`, full repository read with its sandbox bypassed, per the owner's standing authorisation) |
| **Refinement** | `.claude/refinements/REFINEMENT-s145-qual149-150-genuine-splits.md` rev 3.4 — READY after five Step-4 cycles (see below) |
| **Owner rulings in force** | **Q1** both stages, with a hard gate between (2026-10-01) · **Q2 = A**, a mid-week hours change is counted per working day (each weekday carries that day's contract; the payslip equals the employee's screen) · **Q3 = F1**, a week that crosses a month boundary is settled by the month containing its Sunday, dated that Sunday · **Q7 = (b)**, a recalculation that changes a week the next month already paid names that month in its response and audit row · **OQ1 = "Keep for S145"** (2026-10-02, about this sprint's evidence method only): the verified 51-mutation design stays, lightened by R66a (CA-1 re-derives only what changed), rather than switching mid-plan to RED-first in CI. The lighter method is noted as an observation for the governance review; no governance decision was made · reseed, don't migrate (standing) |
| **Owner informed, no objection** | Q4: replay freezes what is data and fingerprints what is code (Fable seat R4) · R14: payroll reads the recorded hours itself instead of taking them from the request · a Skema save replaces the cell, fixed in Stage A before payroll reads the hours · R24: clearing a Skema cell stays out of S145 (ROADMAP backlog) |
| **Entropy scan (Step 0a)** | 2026-10-01 at `62fcdc3`: tree clean except an uncommitted ROADMAP line; 0 worktrees; no non-master branches except pre-existing dependabot/actions remotes; 0 untracked source files; no `FindFirst("scopes")`; no hard-coded `http://localhost` in non-test `src/` |

## Plain-language summary

**What was asked.** Months in which something about an employee changes partway through (hours, position, agreement) are refused by payroll today and must be handled by hand. The owner asked for them to become payable.

**What the refinement found first.** Payroll gets *every* month wrong, not only split months. Its overtime, merarbejde and flex rules are written for one week, and payroll hands them a whole month as if it were one week. A full-time employee working a normal September is exported with about 126 hours of overtime and a flex payout (QUAL-186). Opening split months on top of that arithmetic would pay them wrong just as confidently. So the sprint has two stages, with a hard gate between them:
- **Stage A** makes every ordinary month correct. Payroll works week by week, takes the wage-type codes from the employee's dated record, reads the recorded hours itself, and records a replayable proof of each calculation. Split months stay refused exactly as today.
- **Stage B** opens split months, each part paid under its own codes. If Stage B cannot pass review within the cap, Stage A ships alone.

**What else surfaced.** Re-saving a Skema cell stores a second row, and every server total counts both. The send gate then refuses the month, and the employee cannot repair it. A re-saved ferie day consumes quota twice. Registered as QUAL-192..194, this is fixed first in Stage A, because payroll is about to read those same rows.

## What changed for the people who use it (O-4, the operational changes)

1. **Ordinary months are paid correctly** (QUAL-186). Payroll now settles overtime, merarbejde and flex week by week instead of treating a whole month as one week.
   - A week that crosses a month boundary is paid by the month holding its Sunday, dated that Sunday (Q3 F1).
   - A week with a mid-week hours change is counted per working day, as the employee's screen shows it (Q2).
   - A full-time HK employee who works exactly 37 hours every week of September 2026 is no longer exported with about 126 hours of overtime and a flex payout.
2. **Months with a mid-month change are paid** (QUAL-149/150). A change in part-time hours, position, category, or a Monday agreement change is paid part by part, each part under its own dated wage-type codes. Before S145 the month was refused and handled by hand.
3. **One case stays manual: an agreement change in the middle of a week.** A week cannot be settled under two agreements (Q5), so every payroll route refuses such a month with `reason: agreement-change-mid-week`. The HR worklist marks it `QUAL-209` with a Danish explanation ("overenskomstskift midt i en uge — ugen kan ikke afregnes efter to overenskomster"), and a Global Admin resolves it as "Håndteret manuelt" (handled manually).
4. **The raw export routes refuse split months** (Q6 (a)). `/export` and `/export-period` file every line under one profile, so they answer a two-part month with `reason: split-month-single-profile-route` and point to the calculating routes.
5. **Months are approved in order.**
   - A month whose first week reaches back into the previous month is calculated only once that previous month is APPROVED; otherwise the answer is 409 `payroll-prior-month-not-approved` (R5(b)).
   - Reopening a month flags the next month on the HR worklist (R5(c)).
   - Recalculating a month whose last week the next month already paid says so (Q7 (b)).
6. **A Skema save replaces the cell** (QUAL-192..194). Re-saving a cell no longer stores a second row, so totals, the send check and vacation quota count it once. Zero or negative hours are refused at entry.
   - Payroll reads the recorded hours itself, rather than taking them from the request.
   - Each calculation records a replayable proof (its manifest), from which a replay reproduces the result byte for byte.

**A criterion note (O-4).** The plan expected refinement criterion `:618` (an absence day after a mid-month fraction change credits 6.00) to be met at Unit level only. Its branch needs a zero-hour absence, which R24 makes unreachable from the screen (contradiction 12). B.0 restored FX3 (R-62), which seeds that absence directly and pins the 6.00 credit in a Docker fact, CI-verified at the close's runs. It is also met at Unit level.

**No task was cut.** Every planned task is DONE (§ "Task ledger" → "Dispositions"), so there is no known unpinned change.

## Refinement — Step 4, five cycles (both lenses each cycle)

| Cycle | Draft | External (Codex) | Internal (Fable reviewer) | What it turned on |
|---|---|---|---|---|
| 1 | rev 1 (`8a87d16`) | BLOCKER | NOT-READY | Both lenses confirmed the planner's discovery, QUAL-186 (registered at once, `018ef2a`). The replay promise contradicted the ruled replay contract, the month-boundary weeks were unsound, a month-start change was exempted, and flex was underspecified |
| 2 | rev 2 | NOT-READY | NOT-READY (1 B) | The "safe stop point" was contradicted by Stage A's own text. The manifest could not reproduce a calculation. F1's correction paths were incomplete. The month-vs-week window was unspecified |
| 3 | rev 3.1 (after Q1, Q2, Q3, Q7) | NOT-READY (2 B) | NOT-READY (1 B) | **Convergent:** payroll had no source for the previous month's last days of hours, and a replay could not recover its exact inputs. Ruled R14: payroll reads its own hours. Lossless digest (R15) |
| 4 | rev 3.3 (after the Skema ruling) | NOT-READY (1 B) | READY-WITH-WARNINGS | Convergent: the vacation-settlement reader was missing from the Skema fix's census. Quota pre-validation; fixture preconditions |
| 5 | rev 3.4 | **READY-WITH-WARNINGS** | **READY-WITH-WARNINGS** | No blocker under the final-cycle definition. Carried to the plan: the pre-check must let a reduction through even when `used` is above the cap; a stale test rationale; the host preconditions for the months-in-order facts; wording that separates "positive hours" from "≤ 0" |

Each cycle's findings were adjudicated by the Fable seat (rulings R1–R24) before the `planner` revised. The halt-and-prompt never fired. Register rows opened during the refinement, each committed at once because the refinement file is gitignored: QUAL-186 (`018ef2a`), QUAL-187..191 (`6849b84`), QUAL-192..194 (`62fcdc3`).

## Carried from S144 — the CI-health gate waiver, recorded as the waiver directs

At the S144 close the sprint-close guard's CI-health check queried `gh run list --branch master --event push --status completed --limit 1`. It received a stale answer from GitHub's filtered listing: an old S138 red run instead of the newest green one. On that basis it refused to close S144 "on top of a red sprint". The close went ahead under a written waiver (`.claude/reviews/SPRINT-144-ci-health-WAIVED.md`), because master's latest completed push run was in fact green. The fix landed in the governance commit `6b0d982`: the guard now lists ten push runs and takes the newest completed one client-side, with its harness at 22/22. That hook change falls inside this sprint's Step-7a diff, and Step 7a reviews it.

## Step 0b — plan review (both lenses), five cycles: READY

| Cycle | Draft | External (Codex, full repository read) | Internal (Fable reviewer) | What it turned on |
|---|---|---|---|---|
| 1 | draft 2 (1419 lines) | NOT-READY (2 B, 6 W) | READY-WITH-WARNINGS (4 W) | Frozen inputs lacked the leaver's settlement cutoff (R41). Mutation coverage had gaps (R46). Replay ran live admission checks (R42). A hire-dated agreement row read as a change (R43) |
| 2 | draft 3 (1631) | NOT-READY (2 B) | READY-WITH-WARNINGS (4 W) | The evidence partition: mutations masking each other or turning Unit red (R52–R55). A weak replay sentinel (R56). The settlement leg could not pass on correct code (R57) |
| 3 | draft 4 (1913) | NOT-READY (2 B) | READY-WITH-WARNINGS (1 W) | The replay stub could not observe the claimed differences (R60). One mutation site reached the raw export routes (R61). Proportionality: **owner OQ1, "Keep for S145"** (R66) |
| 4 | draft 5 (887 + 30 briefs, R66b split) | **READY** | READY-WITH-WARNINGS (2 W) | The Not-guards table was incomplete. RP7/RP8 should change the engine fingerprint, not edit the stored record (refinement rev 3.4 `:662`). The Orchestrator absorbed both with four short edits. The two lenses had disagreed on the RP7/RP8 method, and the refinement's wording decided it |
| 5 | draft 5 + the four edits | **READY** | **READY** | Verification of the four edits only; three notes (wording, citation ranges, this log entry) |

**In plain language.** Five cycles, every one in the plan's proof machinery rather than the payroll design:
- **What the machinery is.** The plan proves each new test can fail by breaking the code deliberately in 51 known ways on a throwaway branch, grouped into four CI runs.
- **Where the cycles went.** Getting that grouping and the test stand-ins right took the cycles. Each blocker the external lens found was real, and each was verified by the Fable seat before it was fixed.
- **The size question.** At cycle 3 the owner was asked whether to switch to a lighter method mid-plan, and chose to keep this one for S145.
- **The split.** The plan was then split: the per-task briefs moved into their own files, and the planning history moved into this log. The part re-read at each gate went from 1913 lines to 887.

These cycles are recorded as observations for the governance review the owner asked for (ROADMAP § Governance / docs). They are not conclusions.

## Planning record (Step 0b)

*Moved from `.claude/plans/PLAN-s145.md` under R66b (plan draft 5, 2026-10-02), with the Fable seat's ruling texts for R41–R66 copied from its ruling files so that they outlive the session scratchpad. The plan keeps the one-line rulings-in-force tables. This section keeps the full ruling wording, the brief contradictions, the draft-to-draft absorption tables and the revision history. Headings are one level lower than in their source; the text is otherwise verbatim.*

### Rulings on draft 1 — R25–R40 (the drafting proposals P1–P15 and N-1, as ruled)

*Ruled by the Fable seat (`adjudicator`, `claude-fable-5-1`) on draft 1, at master `299c3dd`; verdict RULED; no owner question. The rulings continue the refinement's sequence, so "R" is one series. The old P-numbers are kept in brackets because the Step-0b lenses may cite them. Each ruling is folded into the briefs and tables below.*

- **R25 (P1) — ACCEPTED, with an addition. A payroll-local seam for the hours read (within R14).**
  - *Why it is needed:* R14 reads "through the existing Infrastructure repositories". The unit-level PCS suites run with no database (`EmploymentWindowPcsFixture.cs:201-216`; `EmploymentWindowSegmentSkipTests.cs:10`). Those repositories are concrete classes over a `DbConnectionFactory` (`TimeEntryProjectionRepository.cs:18-22`), so a unit test could not otherwise feed a calculation.
  - *The seam:* a payroll-local interface `IRecordedHoursReader`, on the `IRuleClassificationProvider` precedent (`PeriodCalculationService.cs:2058`). Its production implementation `ProjectionHoursReader` uses exactly those two repositories.
  - *Never silent:* PCS takes it as an optional constructor dependency. A calculation or replay with no reader throws `InvalidOperationException` naming the registration, **before any read**.
  - *One input path:* **every** public PCS calculate entry point drops its `entries`/`absences` parameters: `:345`, `:368`, `[Obsolete] :781`, `[Obsolete] :845`, and the replay pair `:1138`, `:1171`. The correction service's `RecalculateAsync` drops them too. Keeping an hours-taking overload "for tests" would keep the defect class R14 removes.
  - *Census:* **`ProjectionHoursReader` is the only Payroll type touching the two repositories**, so the Step-7a census (item 8) lists it by name.
  - *Cost:* the call sites listed in TASK-14508-T.
- **R26 (N-1) — RULED (B); amends R21.** *Plain language:* the previous-month check must never be skipped silently, and every test that calculates a month reaching into the previous month must say how it satisfies the check. Option (A) would have moved tests to other months, which hides the precondition and changes what the tests say. (B) states it in each test with one small interface; nothing of R21's behaviour is given up.
  - *The mechanism:* PCS takes `IPriorPeriodApprovalReader? = null`, payroll-local, with one method answering M−1's approval status for `(employeeId, periodStart, periodEnd)`. Its production adapter wraps `ApprovalPeriodRepository` (a sealed concrete class, `ApprovalPeriodRepository.cs:9, :53`; `GetByEmployeeAndPeriodAsync` `:101, :137`). Both are registered in the Payroll host.
  - *Never silent, exactly as R21:* the host 409 facts pin against a dropped adapter registration (MD-21).
  - ***The fixture rule (replaces refinement `:552-556`).*** These facts use the **real adapter with M−1 seeded APPROVED**:
    - a fact *about* the check;
    - a host fact;
    - `WeeklyPayrollTests`;
    - the smoke test.

    **Every other PCS-level fact** (segmentation, replay, marquee, unit) injects a **fake answering APPROVED**, declared in its fixture with one comment. `BuildPcs` therefore defaults to the fake, so the wiring is one line again. Full `init.sql` is applied to the 4-table harness only where a suite needs the real table.
  - *Files:* TASK-14512 gains `IPriorPeriodApprovalReader.cs` and the adapter; TASK-14512-T shrinks to fake injection plus seeding in the named suites.
- **R27 (P2) — ACCEPTED, amended. Paired test passes.**
  - *What they are:* when an implementer's change intentionally breaks existing tests (a removed method, a removed parameter, a new precondition), a `test-qa` spawn works **on the implementer's branch**, after the implementer returns and before the merge. It migrates exactly the listed files.
  - *Expected values:* it may change one only where the brief lists the change as intentional (from the refinement). Every other changed value is a STOP-and-report.
  - *Amendments:*
    - **5α on a paired branch takes the union of both file lists.**
    - **The paired `test-qa` never edits `src/**`.**
    - **R35's rounding change is labelled `// S145 R9:`** (R9 is the refinement ruling that put absence credits on the helper).
  - The merge checklist runs once, on the combined branch. Master never carries a red or non-compiling suite.
  - *Declined alternative:* the implementer edits tests itself. That puts expectation changes in the hands of the agent whose code they judge.
- **R28 (P3) — ACCEPTED.** `source` gets `DEFAULT 'TIME_POST'` and `CHECK (source IN ('SKEMA','TIME_POST'))` (reasons in § "Carried warnings", N4).
- **R29 (P4) — ACCEPTED.** `segment_manifests.plan_inputs_jsonb JSONB NULL`. The refusal `replay-manifest-without-frozen-inputs` must be reachable in a test; reseed means no real row lacks the slot.
- **R30 (P5) — ACCEPTED; cut first.** Reader-matrix pins for the census readers the A0 criteria leave unpinned: the allocation breakdown (`ApprovalEndpoints.cs:1288-1344`), the balance summary (`BalanceEndpoints.cs:177-179`) and the strand check (`EmploymentWindowStrandCheck.cs:64-70`). The pins are `Resave_Entry_AllocationBreakdownCountsOnce`, `Resave_Entry_BalanceSummaryCountsOnce` and `Resave_StrandCheckCountsCellOnce`. The `awayToday` read (`ApprovalEndpoints.cs:996-1001`) is `SELECT DISTINCT employee_id` and cannot double-count; it gets the predicate for uniformity only.
- **R31 (P6) — ACCEPTED, all four legs.**
  - (a) The N1 pin `CalculateAndExport_AgreementChangeTue1Sep_AugustNotApproved_AnswersWeekCheck422First`.
  - (b) The Q7 (b) negatives `Recalculate_ExportedSeptember_OctoberNotExported_NamesNothing` and `Recalculate_ExportedMay2026_MonthEndsSunday_NamesNothing` (31 May 2026 is a Sunday).
  - (c) The R5(c) legs `Reopen_…_OctoberAndNovemberExported_WritesOctoberOnly` (R36) and `Reopen_ApprovedNotExportedMay2026_JuneExported_WritesNothing` (1 June 2026 is a Monday).
  - (d) The planner-bypass guard (`PlannerBypassGuardTests.cs:63`, regex `/api/rules/evaluate(?![/\-\w])`) also covers `/api/rules/evaluate-timeline`, with the same allowlist.
- **R32 (P7) — ACCEPTED. Stage A is verified in CI before Stage B, on a throwaway branch.**
  - CI runs only on a push or pull request to master (`.github/workflows/ci.yml:3-7`). So the Stage A tip `K_A` is pushed as the tip of a throwaway branch with a draft pull request, and its first run (V-A) must be green on all seven jobs.
  - The same branch is then fast-forwarded through the evidence commits, extending the S144 mechanism (`PLAN-s144.md:56-58`).
  - `origin/master` is never touched. A `pull_request` run cannot reach the close guard's CI-health check, which lists push runs on master only (`sprint-close-guard.ps1:322`).
- **R33 (P8) — ACCEPTED, amended. Abandoning Stage B is a revert, never a reset.**
  - `K_A` is recorded at the gate. The house merge is squash/file-copy (`docs/AGENTS.md:583`). So a Stage B landing is reverted newest first with plain `git revert <sha>`, and `git revert -m 1 <sha>` is used only where the landing is an actual merge commit.
  - **The contract is the empty `git diff K_A HEAD -- src tests frontend docker tools`.** Nothing is rewritten.
- **R34 (P9) — ACCEPTED, amended (a real omission). Every norm entry point runs the core, with the mode it forces today.**
  - Today the three decomposed ids **force** a mode and tag their own id: `NORM_CHECK_WEEKLY` → `EvaluateWeeksCore(…, 1, …)`, `NORM_CHECK_MULTIWEEK` → `NormPeriodWeeks`, `NORM_CHECK_ANNUAL` → annual (`NormCheckRule.cs:64-98`). `Evaluate` dispatches on the config's `NormModel` and tags `NORM_CHECK_37H` (`:45-57`).
  - The legacy dispatch therefore carries a forced-mode input into the core (**`TimelineCoreInput.ForcedNormMode`**, null = dispatch on the config) and tags the result with the requested id. So `NormCheckRuleTests` for those ids move only by the four labelled changes.
  - All four public `NormCheckRule` evaluate methods are retired.
- **R35 (P10) — ACCEPTED, amended wording. The 0-hour default credit goes through the helper; the constant stays.**
  - Today a 0-hour absence is credited `7.4 × fraction`, unrounded (`AbsenceRule.cs:46-48, :81-83, :98-100`). It becomes `DayNorm.ForWeekday(37.0m, fraction)`, that is 7.4 × 5, rounded per the precision rule.
  - Replacing the constant with the config's `WeeklyNormHours` stays the deferred ROADMAP item (`ROADMAP.md:268-276`, item 4).
  - `AbsenceRule.Evaluate` is **not retired**; it changes only in that default credit.
- **R36 (P11) — ACCEPTED. The R5(c) reopen hook does not inherit R5(a)'s widening.**
  - R5(a) extends `hi` inside `WriteForExportedMonthsAsync` (`HrBackdateWorklistRepository.cs:742-743`). A reopen of September writing October's row with `[1 Oct, 1 Nov)` would widen to Monday 2 November: 31 October 2026 is a Saturday, and its week ends Sunday 1 November.
  - The hook writes exactly M+1's row, unwidened. *Hint (HOW):* a `widenToSettlingMonth` flag, or a single-month writer beside it.
- **R37 (P12) — ACCEPTED. Q7 (b) spans the event and the audit mapper.**
  - The event `RetroactiveCorrectionRequested` gains optional `NextPeriodToRecalculateStart` / `…End` (`DateOnly?`, pay-period dates). There is **no new event type**. This lands in TASK-14500.
  - The mapper change rides in TASK-14512 under `Payroll Integration (extended into Infrastructure/AuditMappers, cross-domain authorized)`.
  - *The rule:* name M+1 if and only if M's last day is not a Sunday **and** a `payroll_export_records` row exists for M+1.
  - **Over-naming M+1 for a leaver** (whose last week was settled inside M) is advisory and harmless. The response only tells the admin to look at M+1, and nothing is written to M+1.
- **R38 (P13) — ACCEPTED.** S144's N-c4-1, N-c4-2 and N-c4-4 (`SPRINT-144.md:460-464`) are fixed in TASK-14503b, which edits `PayrollHostRecalcBlockedTests.cs` anyway.
- **R39 (P14) — ACCEPTED. Stage A evidence is re-observed at the close where its code changed.**
  - **The unit of comparison is the mutated method's hunk**, frozen at C-1.
  - For every Stage A Docker mutation whose hunk differs between `K_A` and `K`, the mutation is re-applied from `K` and re-observed in a close evidence run. A mutation whose hunk is unchanged keeps its A-gate evidence.
- **R40 (P15) — ACCEPTED, extended.** The contract names for the new Rule Engine surface are fixed in the TASK-14505 brief, so that TASK-14506 can author its pins in parallel. The contract now also carries R34's `ForcedNormMode` member and the payroll exception names: `PeriodNotCalculableException` (the week-check 422) and `PriorMonthNotApprovedException` (the 409), TASK-14512. Renames are reported, never silently reconciled.

### Rulings R41–R51 (Step 0b cycle 1)

*Copied verbatim from the Fable seat's ruling file `rulings-s145-step0b-c1.md`, §§ 1–2 (its § 3, the planner instructions, and § 4, the next cycle's scope, are process notes and are not copied).*

#### 1. Rulings (R41–R51), verified at `299c3dd`

**R41 — Codex B1: ABSORB. Freeze `SettlementCutoff`; it is not recoverable.**
*Why (PM):* a person leaving on Wednesday 30 September and a person staying leave identical traces in September's frozen record — same days, same hours, same segment — yet F1 pays the leaver's last week in September and the stayer's in October. If the record does not say which, a replay cannot prove the payslip.
*Verified:* `PeriodCalculationService.cs:1030-1034` turns the inclusive window end into a boundary at End+1; `BoundaryDetector.cs:203-204` drops any boundary > `periodEnd`, so 1 October never reaches the plan; `PlannedSegment.cs:46-52` carries no window end; the plan's `PlanInputs` (TASK-14500 Do 5) has no cutoff. Pieces for both employees are `[31 Aug, 30 Sep]`. Not recoverable — it must be frozen.
*Edits:* TASK-14500 Do 5: `PlanInputs` gains `DateOnly? SettlementCutoff` after `Pieces`. TASK-14511 Do 2, line `:838`, replace with: "`SettlementCutoff` = the `End` of the employment window (the inclusive last employed day, from the same `IEmploymentWindowResolver` read that typed the segments) when `End ≤ periodEnd`; otherwise null. *Not* 'the last EMPLOYED day in the period' — that is `periodEnd` for everyone." TASK-14513 Do 1 freezes it; replay feeds it to the core. Pins: 14514 `WeeklyPayrollTests.Leaver_LastDayWed30Sep_SeptemberPaysClosingWeek_Dated30Sep` (same hours as `TwoMonths_ClosingWeek_SeptemberPaysNothing…`); 14515 `Replay_LeaverEndDateRemovedAfterCalc_ByteIdentical_FreshCalcDiffers`; 14506's `SegmentManifestCreated_PlanInputs_RoundTrips…` covers the field (MU-23). Mutations: **MD-43** "cutoff = last piece day" → both `TwoMonths_*`, `AugustToOctober…` red (not in EA-1: shares facts with MD-20); **MD-44** "replay re-derives the cutoff from the live window" → the replay fact red.

**R42 — Codex W4: ABSORB. Replay never runs admission checks.**
*Why:* replay proves what *was* calculated; whether September is approved *today* cannot change whether October's result then is reproducible. Verified: `ReplayAsync` re-enters `CalculateWithOutcomeAsync` (`:1194-1197`), where TASK-14512 places the prior-month check.
*Edits:* TASK-14512 Do 2 and TASK-14513 Do 3: split `CalculateWithOutcomeAsync` into *admission* (planner refusal, week check, prior-month check, the range hours read) and *evaluation*; `ReplayAsync(manifestId)` enters evaluation only and never consults `IPriorPeriodApprovalReader`, `IUserAgreementCodeRepository`, `IEmploymentWindowResolver` or `ReadCurrentAsync`. The R21/R26 never-silent throw is a calculation rule; replay with a null approval reader succeeds. Pin (14515): `Replay_AfterPriorMonthReopened_ByteIdentical_FreshCalcRefused409` — forward-calc October with the fake answering APPROVED, then swap in a *sentinel* fake that throws if consulted; replay → byte-identical; fresh `CalculateAsync` → `PriorMonthNotApprovedException`. R26 clarification (not amendment): a sentinel fake is the only instrument that proves "not consulted". Unit (14514): `PcsNeverSilentTests.NoApprovalReader_Replay_Succeeds`, MU-38 "replay throws on null reader". **MD-45** "replay runs admission" → the replay fact red.

**R43 — Fable W1: ABSORB. A hire-dated first row is not a change.**
Verified: `UserAgreementCodeRepository.cs:293-304` returns dates with no exclusion; `:669-681` inserts a version-1 row at any caller date; today `PeriodPlanner.cs:355-356` absorbs it (`employedCount < 2`); the date-only predicate would not.
*Edits:* TASK-14512 Do 1: before calling `MidWeekAgreementChange`, PCS drops every date `d` whose `d − 1` is not an EMPLOYED day of the resolver's windows. The predicate stays date-only and pure; its contract doc in 14505 says "callers filter hire-dated rows". TASK-14503: `…StarterThursday3Sep…_200` seeds a version-1 row dated 3 September (not the `'0001-01-01'` default). **MD-46** "filter dropped" → that fact red.

**R44 — Fable W2 + Codex W3 (merged): ABSORB.** Verified `SkemaEndpoints.cs:1918-1924` → guard `EntitlementBalanceRepository.cs:369-372`. MD-18 moves to EA-3 (disjoint from MD-08's ≤0 branch). CA-1 rule gains: "mutually exclusive per run when they edit the same code path, not only when they share facts."

**R45 — Fable W4: ABSORB.** Add the two R31b negatives to MD-21 (both months' opening weeks reach M−1: 31 Aug; 27–30 Apr) and `…AnswersWeekCheck422First` to MD-26. TASK-14518 publishes resolved `FullyQualifiedName` lists before `K_A`.

**R46 — Codex B2: ABSORB (coverage and MU-32).**
(a) Rewrite `:1054` as "every *non-guard* Docker pin", and tag each pass-today fact "(guard)" (the no-prior-approval 200s, the raw-route 200, the two week-check guards, the four reopen/Q7 `WritesNothing`/`NamesNothing` negatives). Non-guard pins without a mutation get one: **MD-47** "R5(a) widening removed" → the 14504 backdate fact; **MD-48** "reopen hook absent" → `Reopen_…WritesOctoberRow…`, `…WritesOctoberOnly`; **MD-49** "hook through the widened writer" (R36) → `…WritesOctoberOnly` red (not with MD-48); **MD-50** "Q7 names M+1 unconditionally" → both R31b negatives (not with MD-28).
(b) MU-32 as written cannot trip: `PlannerBypassGuardTests.cs:116-117` flags a file only when it carries the path *and* a POST, so on a valid tree both regexes find nothing. MU-32 becomes: in the throwaway worktree, add a `PostAsJsonAsync("/api/rules/evaluate-timeline", …)` literal to a non-allowlisted `src/Integrations/**` file. Same shape for N3's new guard (MU-39).

**R47 — Codex W5: ABSORB.** Verified `ManifestProjectionRebuildTests.cs:113-119` reads `segments_jsonb` only. 14515 gains `Rebuild_PreservesPlanInputs_ReplaySucceeds` in a **new** file `ManifestProjectionRebuildPlanInputsTests.cs` (14508-T edits the existing file in A.2); **MD-51** "rebuilder drops `plan_inputs_jsonb`" → red (replay refuses `…without-frozen-inputs`).

**R48 — Codex W6: ABSORB, narrowed.** Verified: backfill reads `ORDER BY stream_id, stream_version` (`ProjectionBackfillService.cs:121`) and inserts at once (`:377`). Both Skema and Time-POST write to `employee-{id}` (`SkemaEndpoints.cs:1438`; `TimeEndpoints.cs:181`), so one employee's cells are one stream and stream order *is* outbox order for every cell — the cross-stream point is moot. The real hazard is insert-then-mark against the partial unique index. TASK-14507 Do 4 becomes: "per event, in stream order: for SKEMA, the idempotent UPDATE (`… AND superseded_by_outbox_id IS NULL AND outbox_id < @id`) first, then `INSERT … ON CONFLICT DO NOTHING` — the same mark-then-insert as Do 2; never insert first."

**R49 — Codex W7 + Fable N4 (merged): ABSORB.** A RED Unit file cannot merge in A.1a under O-1. `MonthAsWeekCharacterizationTests.cs` is authored and observed RED on 14503's branch (the report's figures are the before-record), excluded from 14503's merge, and moved to TASK-14506's file list; re-spawn 1 verifies it green, no value change. Pre-split 14503 per N4 (14503a: factory + `_RealEngine` facts; 14503b: stub facts) — recommended, Orchestrator's call.

**R50 — Codex W8: ABSORB.** Rule: no task authors into a file another open-wave task edits. 14515's marquee fact → new `EmployeeProfileForwardCalcAsOfTests.cs`; 14514's `EmploymentEdges_*` → new `EmploymentEdgesTimelineTests.cs` (if the fixture is file-private, the edit happens only in re-spawn 1 after 14511-T merged). TASK-14521 → `Backend + Infrastructure (cross-domain authorized)`. TASK-14524 names its Unit files now: `tests/StatsTid.Tests.Unit/Segmentation/SinglePlanEvaluationMergerTests.cs`, `…/Unit/Rules/RuleRegistryStageBClassificationTests.cs` (rename by reporting at B.0).

**R51 — Fable W3: ABSORB.** TASK-14501 gains `HarnessDdl_HoursTablesAndManifests_MatchInitSqlSchema`: harness copy, then `StatsTidWebApplicationFactory.ApplyFullSchemaAsync` (`EmployeeProfileMarqueeTests.cs:110`), assert `information_schema.columns` and `pg_indexes` for the three tables unchanged. A guard; no MD.

**Questions for the owner:** none. No ruling (R1–R40, Q1–Q7) is touched; R26 is clarified, not amended.

**Declined:** Fable N6 (process-level mitigation adequate this sprint); Codex NOTE (praise, no action).

#### 2. Notes absorbed
- N1: TASK-14508 Do 3 names the slot "between the org-scope 403 and the approval read at `Program.cs:304`"; 14503's 400 facts seed the month APPROVED.
- N2: the 409 body gains `success:false` and `error`.
- N3: R25 becomes a Unit guard in 14514 (`PlannerBypassGuardTests` shape; MU-39 by injection).
- N4: merged into R49.
- N5: fix the three citation drifts.

### Rulings R52–R59 (Step 0b cycle 2)

*Copied verbatim from the Fable seat's ruling file `rulings-s145-step0b-c2.md`, §§ 1–2 (its § 3, the planner instructions, and § 4, the next cycle's scope, are process notes and are not copied).*

#### 1. Rulings (R52–R59), every code claim re-opened at `299c3dd`

**R52 — Codex B1: ABSORB. MD-22 leaves EA-3 for EA-1; its list gains the sentinel pin.**
*Why (PM):* the sentinel pin's second half says "a fresh October calculation with September merely EMPLOYEE_APPROVED must be refused" (plan `:1092`). MD-22 makes that status acceptable, so it reddens the same test MD-45 claims — two breakages, one red, no proof. MD-22 and MD-23 also both touch the prior-month check (one its predicate, one its position), which `:1112-1113` forbids in one run.
*Edit:* MD-22 row: facts `…SeptemberEmployeeApproved_Returns409`, `Replay_AfterPriorMonthReopened_…` (fresh-calc half); run **EA-1**. Checked against EA-1: no EA-1 mutation reddens either fact (MD-20/31/32/35/52 leave forward and replay identical, the 409 still throws; MD-26 removes the week-check guard, a different guard from the prior-month predicate — that distinction is the code-path rule's unit).

**R53 — Fable W2(a) + Codex B2: ABSORB. MD-45 is null-tolerant.**
Verified `ci.yml:165` Unit → `:176` DemoSeed → `:187` Regression, no continue-on-error. Wording: "replay consults `IPriorPeriodApprovalReader` **when one is registered**, and applies the prior-month predicate." The sentinel reader still throws (red); `NoApprovalReader_Replay_Succeeds` keeps a null reader (green); MU-38 keeps the null case. Add MD-45 to CA-1 step 4's placement list (`:1116-1120`).

**R54 — Fable W2(b): ABSORB, ruled now, not at CA-1. MD-40 becomes MU-40.**
*Why:* "pieces include NOT_EMPLOYED days" can only sit in PCS's piece builder, and 14511-T's rewritten Unit assertions (`:894`) sit on hire-dated and end-dated fixtures — `EmploymentWindowSegmentSkipTests.cs:51/:68` (hire 10 March, calls start at hire), `:79/:102/:124` (last day), `:236-237` (two spells). Any PCS-level form reddens Unit; an endpoint-level form never reaches `EmploymentEdges_Starter3Sep_…`, which asserts the request through a fake engine (`:1040-1047`). Mutation evidence cannot satisfy all three constraints, so fact 3 (`:226`) applies.
*Edit:* delete MD-40; add **MU-40** "PCS's piece builder includes NOT_EMPLOYED days" → 14511-T's piece assertions on the hire-dated fixtures, run by 14514 re-spawn 2 beside MU-36 (A.4 gate). In the CA-1 matrix, `EmploymentEdges_Starter3Sep_…` is marked "covered by MU-40 (fact 3, R54)"; R46a reads "every non-guard Docker pin has a mutation — Docker, or Unit under fact 3 with a recorded ruling." The leaver twin keeps MD-52.

**R55 — Fable W3: ABSORB. MD-21 → EA-4; MD-42's list grows.**
MD-42 reaches every PCS calculation; `…September_FullTimeHK_…_RealEngine` asserts no NORMAL_HOURS dated 31 August (`:514`), so it is red under MD-42 while listed only under MD-21. MD-21 cannot go to EA-1 (October fact with MD-20) or EA-3 (Q7 negatives with MD-50); EA-4 is clean: `WeeklyPayrollTests` build the adapter directly (`:1023`), so a host registration drop never reaches MD-38/39 facts; MD-46's starter has no EMPLOYED M−1 day (`:928` tolerates null).
*Edits:* MD-42 facts += the two September `_RealEngine` facts and `…October_HoursFromProjections_…_RealEngine`, each confirmed against its fixture's assertions (the AC twin only if its assertion sees 31 August). MD-24 row note: "with MD-21 in EA-4, `SplitMonth…` is red as the never-silent 500 rather than 409; MD-21 alone leaves it green (planner refuses first)." MD-21's list is enumerated **by effect** at CA-1: every host-route calculating fact whose opening week reaches an EMPLOYED M−1 day, including the S144 facts 14512-T migrates under R26 — otherwise EA-4 shows unlisted reds.

**R56 — Fable W1 + Codex W4 (merged): ABSORB. The fourth source gets a real sentinel.**
Verified `PeriodCalculationService.cs:1056` guards every access, so null proves nothing. `UserAgreementCodeRepository.cs:60/:74` is sealed over `DbConnectionFactory` (a plain string, `DbConnectionFactory.cs:9`), and the read opens the connection at `:306-307`.
*Edit `:1092` and `:1540`:* the replay PCS gets `new UserAgreementCodeRepository(new DbConnectionFactory("Host=127.0.0.1;Port=1;…;Timeout=1"))` — a refused port fails at once. Delete "a null shows replay does not need it." The fresh `CalculateAsync` runs on the **non-sentinel** PCS with its fake swapped to EMPLOYEE_APPROVED (else the resolver sentinel throws before the 409). Codex's "keep the null build too": declined as redundant.

**R57 — Fable W4: ABSORB. The Termination leg is restructured.**
Verified: `SeedAbsenceAsync` is raw SQL with no `source` (`TerminationSettlementTests.cs:1108-1125`) → TIME_POST after 14500, never superseded (`:745`); the fact asserts `used` 2, crystallized 10.5 after one `SettleAsync` (`:246-254`).
*Edit TASK-14501 Do 4:* before `MarkLeaverAsync` (`:244`) and the single `SettleAsync`, Skema-save a new weekday VACATION cell dated before the end date inside ferieår 2025, then re-save it with the same hours; expect `used` 3 and crystallized 9.5 (an unfiltered read gives 4 / 8.5). The planner names the date and confirms the Skema preconditions (profile, balance row, unlocked period) are met by the existing seeds. Same "save, then re-save" wording for `SpecialHoliday_Resaved…`. Drop `:407`'s "one Skema save" conclusion.

**R58 — Codex W3: ABSORB. Registration lives in the allow-listed file.**
Verified the host pattern is bare `AddSingleton<Repo>()` in `Program.cs:62/:80/:105` — and that file also hosts the endpoints (`:304`, `:493`), so allow-listing it would let an endpoint read the repositories, exactly what R25 forbids. *Edit TASK-14508 Do 1 (`:789`):* `ProjectionHoursReader.cs` exposes `AddProjectionHoursReader(this IServiceCollection)` registering both repositories and `IRecordedHoursReader`; `Program.cs` calls it. The guard's allowlist stays one file.

**R59 — Procedural: the plan carries a mechanical partition table (see § 4).**

#### 2. Notes absorbed
- N1: tag `ForwardCalc_ResolverAsOf_…` "(guard)" (`PCS:570` already resolves at `segment.StartDate`).
- N2: `Flex_TwoMonths` states every context day carries exactly the day norm or no hours.
- N3: the full-day guard asserts `error == "absence_full_day_only"` (`SkemaEndpoints.cs:985`; the quota 422 is `:1424`).
- N4: R51's guard compares the harness snapshot to a fresh `init.sql`-only snapshot (all three CREATEs are `IF NOT EXISTS`, `init.sql:1880/:1903/:2056`, so apply-on-top misses type drift).
- N5: ledger: 14503a's branch is kept until 14506 re-spawn 1 has taken the file (`AGENTS.md:583` squash merges).
- N6: MD-12 names the Skema endpoint's replacement path.
- N7: `ReplayDeterminismTests.cs:30` Trait / `:33` field; `PositionOverrideConfigs.cs` is `src/SharedKernel/StatsTid.SharedKernel/Config/`. Contradiction 17 closed: planner right, both lenses agree.

**Questions for the owner:** none. **Declined:** Codex's null-build retention (R56); Codex NOTE tables (informational).

### Rulings R60–R66 (Step 0b cycle 3) and the owner question OQ1

*Copied verbatim from the Fable seat's ruling file `rulings-s145-step0b-c3.md`, §§ 1–2 (its § 3, the planner instructions, and § 4, the next cycle's scope, are process notes and are not copied).*

#### 1. Rulings R60–R66 (every code claim re-opened at HEAD `c022e1c`, code-identical to `299c3dd`)

**R60 — Codex B1: ABSORB. Verified.** `TestFixtures.BuildPcs` (`TestFixtures.cs:246-272`) takes an optional handler; the default (`:280-347`) emits one NORMAL_HOURS line per request entry, reading only `date`/`hours`/`profile.employeeId` (`:313-331`) — fractions, cutoff and context ignored; 14511-T's timeline stub is specified the same way, "per in-month entry" (plan `:920`). So on correct code RC1/RC2's fresh half (28/29 September are context days of October), RL's (cutoff) and RP1/RP2's (fractions, `PCS:570-571`) produce an identical result, and MD-20/31/36/43/44/52's RL/RC/RP reds (`:1329, :1340, :1345, :1351-1352, :1360`) are unobservable.
*For the owner:* the replay tests were going to compare two outputs that a toy engine makes identical whatever payroll fed it, so they could neither pass nor prove anything.
*Edit (C-R4 and 14515 header `:1107`):* `ReplayFrozenInputsTests` builds both PCS instances with a **test-local echo handler**: on `/api/rules/evaluate-timeline` it returns deterministic lines that encode every request entry **including context days**, each piece's `PartTimeFraction`, and `SettlementCutoff` (null encoded distinctly), fingerprints `["stub"]`, marker `weekly-timeline-v1`; on `/api/rules/evaluate` it echoes the request profile's fraction, agreement code and position. "Byte-identical" and "differs" compare the full `PeriodCalculationResult` (all rule results' line items), not export lines. Precedent: the marquee stub reads the fraction from the pieces (plan `:1043`). Re-derive rows MD-03, 15, 20, 31, 36, 38, 43, 44, 52 against that handler (I checked each holds: e.g. MD-43 gives the 30 September leaver `periodEnd` = 30 September before and after the end-date removal, so "differs" fails → red). Codex's real-engine alternative declined: it would put MD-39/41/42 in reach of the replay facts and force a repartition.

**R61 — Codex B2: ABSORB. Verified.** `EnsurePeriodPlannableAsync` (`PCS:903-911`) calls the shared builder at `:910`, whose last statement is `PeriodPlanner.Plan(` (`:1100`); the raw-route guard calls it at `Payroll/Program.cs:593` and catches only `PlannerInvariantViolation` (`:595`) and `RuleClassificationsUnavailableException` (`:601`); 14512 keeps the seam shared (`:944-945`). MD-24 as written therefore 500s H11 and H5's raw-route setup (`:551`, `:557`), neither listed.
*Edit (`:1333`, `:1772`, `:1167`, contradiction 18 `:1726`):* MD-24's site is "the per-plan lane's admission step, before its call to `BuildPlanForLegacyCallersAsync`; the mutation reads the employment windows itself; the builder and `EnsurePeriodCalculableAsync` are untouched". X1–X4, H9, H10 keep their reds (planner 422 and classification 503 are raised inside the builder call, `:1100-1105`). Not list gains H11, H5 ("raw routes never enter the lane"). Codex's "list H11/H5 and repartition" declined: H11 is the guard for "the raw routes are unaffected" (`:959`); a mutation that reddens it is a different mutation.

**R62 — Codex W1: ABSORB. Verified.** `TerminationSettlementTests.cs:84-88` boots the plain factory; `StatsTidWebApplicationFactory.cs:106-115` sets only the connection string; a Skema absence save posts to `/api/rules/validate-entitlement` (`SkemaEndpoints.cs:1410-1411`) and fails closed (`:1413-1418`). *Edit:* every fixture that Skema-saves an entitlement-typed absence installs the stub factory in `ConfigureTestServices` (`Adr032RevaluationTests.cs:76-83`; handler `:686-706` delegates to `EntitlementValidationRule.Evaluate`) and asserts each save's 200 before settling — TASK-14501 Do 4 (new precondition bullet at `:433-438`, both fixtures) and TASK-14502 "Fixture rules" (`:468-472`, Q1–Q7, S6, S9).

**R63 — Fable W1 + N1 (merged): ABSORB.** Verified: MD-27 keeps `afterExclusive: periodStart` (`PCS:1058-1059`), so AR1/AR2/AR5/AR6/H10 pass the week check and reach MD-21's null reader → 500. *Edits:* MD-27 row (`:1336`) and EA-2 line (`:1375`): "in EA-2 these five surface as the never-silent 500, not 200/409"; MD-21's Not entry adds "(outside EA-2)". Derivation rule for "relies on" (legend `:1281`, CA-1 step 3): *hard* — every guard on the route between the request and the asserted value (check (c)); *soft* — a "Not-guards" note naming the guard that keeps each Not fact green; an MD editing another's Not-guard in the same run is not a partition failure while (a) holds, but the row must state the surfaced status (check (c′)). *Why not hard:* MD-21 has no other run, and the proof holds because every affected fact is on exactly one red list.

**R64 — N3: ABSORB.** Cite the forskud branch (`SkemaEndpoints.cs:1353-1364`) in Do 4/C-A2; the SHO branch threads `EmploymentEndDate` (`:1373-1375`), so the SHO fixture gives the employee no end date before the cell.

**R65 — N4, N5: ABSORB** (CA-3: MD-39/MD-42 patch one method; R39 re-observes both if that handler changes; three citation nits).

**Declined — N2 (MD-01a).** R46a is met by MD-02; the absence stamp is one line beside STAMP-E, whose mechanic MD-01 proves. A 52nd row is weight without proof value; revisit only if CA-1 finds S6 tautological.

#### 2. Proportionality — R66 (procedural)

**(a) Proportionate in kind, not in uniformity.** S145 rewrites the payroll arithmetic for every month, the replay contract and the quota — the deepest payroll-boundary and auditability change the project has made. Heavy proof for the WP/RE/RP/Q families is right. What is heavy beyond need is that a 400-on-body or worklist-row pin gets the same machinery, and that the *proof of the proof* (full exclusivity, a full re-derivation at CA-1) is where the three cycles went. Note cycle 3's blockers were test-design defects (an unobservable stub, a mis-sited mutation) that any evidence design would need the review to catch.

**(b) A materially lighter design exists, but not for S145 from here.** `docs/AGENTS.md:164-165` already sanctions RED-first for compile-today pins; the plan substituted mutations only because Docker does not run locally. One CI run on the throwaway branch after 14501–14504 merge and before 14507 merges would show ~30 compile-today Docker pins red for the right reason and retire ~29 mutations; mutations would remain only for the new-API family (~22 rows, 2–3 runs). Tiering alone does not help: the PM/H10 family forces four runs and is money-critical. A witness rule (one exclusive red per mutation) saves at most one run. From this point the heavy planning is sunk; what remains is five CI runs (cheap) and CA-1's re-derivation, which I lighten now:

- **R66a (amends R59):** CA-1 re-derives by effect only rows whose site, edited path or relied-on path lies in a hunk changed by a reported task deviation, or whose cited code no longer matches; otherwise two spot checks per run. The run itself (CA-5, S144 R9: mismatch → correction + re-run, never a re-list) is the by-effect check of record.
- **R66b — slim now, mechanically.** Draft 5 keeps the ~500 true-plan lines; briefs move to `.claude/plans/s145/TASK-xxxxx.md` (one paste each); R25–R40 text (`:155-222`), contradictions (`:1690-1730`) and the draft tables/history (`:1791-1913`) move to `SPRINT-145.md` § "Planning record" (Orchestrator writes docs). This also makes the rulings durable: `SPRINT-145.md` carries no R41–R59 text today and the rulings files live in the session scratchpad. Cycle 4 verifies by move map plus content diff: no sentence changes beyond R60–R66.

**Owner question OQ1 (one fork; draft 5's fixes proceed either way).**
*Problem:* the deliberate-breakage evidence (51 mutations, 4 CI runs) has cost three review cycles; you asked whether governance is bloated.
*Why now:* two review cycles remain; a lighter mechanic exists.
*Keep (lean):* run the verified design, lightened by R66a/b; feed S145's measured cost into the governance review, where "RED-first in CI for compile-today Docker pins" becomes the default. Gives up nothing in evidence; costs five Stage A CI runs.
*Switch now:* add the RED-first run, cut to ~22 mutations in 2–3 runs. Gives up: one of two remaining cycles on redesign (cap risk), and the per-mutation proof for the Skema/host pins (red-before/green-after stands in).
*Lean:* **Keep** — the sprint whose product change most needs the review cycles should not spend them on evidence machinery, and a standing mechanic is decided once in the rule book, not improvised in the sprint that first uses it.
Labels: **Keep for S145** · **Switch now**.

**Owner ruling OQ1, 2026-10-02, in session: "Keep for S145".** The verified 51-mutation design stays, lightened by R66a (CA-1 re-derives by effect only rows touched by a reported deviation or whose cited code changed; otherwise two spot checks per run; the run itself is the check of record). The "conditional on Switch" instructions are not applied.

### Brief contradictions (claim · what the code shows · file:line)

*All fifteen confirmed by the Fable seat on draft 1 (verified at `299c3dd`). Entry 2 is corrected, and the counts in entries 2 and 11 are replaced by site lists; the list, not a count, is the contract.*

1. **CONFIRMED — W1's "equivalently: `requestedDays = max(delta, 0)` with `used = balance.Used`".** With `used` above the cap and `RequestedDays = 0`, the rule still rejects: `remainingAfter = totalAvailable − (Used + Planned) − 0 < 0` (`EntitlementValidationRule.cs:69-74`). The Fable seat withdrew the clause; the skip form is the ruling. The quota request carries no `minAge` (`SkemaEndpoints.cs:1392-1408`), and the SENIOR_DAY gate is the separate call at `:1055-1071`, so nothing is lost.
2. **CONFIRMED in substance, two entries CORRECTED — R8's criterion names three rewritten suites.**
   - The retired members are called in **seven** test files, at the sites listed in § TASK-14505-T: `OvertimeRuleTests`, `NormCheckRuleTests`, `FlexBalanceRuleTests`, `Sprint11Tests`, `Validation/NormPeriodWeeksConstantTests`, `Regression/RegressionTests.cs` and `Regression/OkVersionRuntimeRegressionTests.cs`. In production they are called by the registry dispatch (`RuleRegistry.cs:215-220, :259`). `RuleEngine.Api/Program.cs:75` calls `GetPayoutLineItem`, a kept pure helper (N5).
   - *Correction 1:* `AbsenceRuleTests` calls only members that are not retired (`:50-257`). It belongs to 14505-T for R35's rounding only, and no value change is expected.
   - *Correction 2:* `Sprint17OvertimeGovernanceTests` names `OvertimeRule.Evaluate` only in its docstring (`:10-17`). It has no call site and is dropped.
   - The two Regression files carry no `Trait`, so they are non-Docker and O-1 runs them.
3. **CONFIRMED — the fact; the fix replaced by R26.** The Segmentation Docker harness builds only `event_streams`, `events`, `segment_manifests` and `wage_type_mappings` (`TestFixtures.cs:413-458`). Under R26, `BuildPcs` defaults to the fake prior-period reader, so no suite on that harness needs `approval_periods`. Full `init.sql` is applied only where a suite needs the real table (TASK-14512-T).
4. **CONFIRMED → ruled R26 — R21's concrete repository at unit level.** The unit fixture passes `connectionFactory: null!` (`EmploymentWindowPcsFixture.cs:201-216`). Its facts calculate March 2026 with open-start windows (`EmploymentWindowSegmentSkipTests.cs:79, :102, :234`; `EmploymentWindowAlignedWindowMergeTests.cs:37`). `ApprovalPeriodRepository` is sealed and concrete (`ApprovalPeriodRepository.cs:9, :53`).
5. **CONFIRMED — the A0/A7 file lists omit the harness copies** (`ProjectionSchemaTestFixture.cs:26, :49`; `TxContractTests.cs:283, :302`; `TestFixtures.cs:435`). → TASK-14501.
6. **CONFIRMED — the stub answers 404 to unknown endpoints** (`TestFixtures.cs:282-299`, the 404 at `:297-300`). So does the unit `RecordingRuleEngine`, and three more Regression fakes route the same way. → TASK-14511-T.
7. **CONFIRMED — Q7 (b) is not payroll-only** (`RetroactiveCorrectionRequested.cs:7-37` has no second-period field; the mapper lives in `Infrastructure/AuditMappers`). → R37.
8. **CONFIRMED — R5(c) through the widened writer reaches November.** 1 October 2026 is a Thursday, so 31 October is a Saturday, its week ends Sunday 1 November, and "the Monday after" is 2 November (`HrBackdateWorklistRepository.cs:742-743`). → R36.
9. **CONFIRMED — ADR-034 is precedent and premise only.** The refinement amends ADR-002 (note), 016, 020, 028 and 040, the "five ADRs" of its Risks (`:737`). ADR-034 appears as precedent (D4, `:293`) and premise (D5, `:502, :523`). The Fable seat recommends a one-line cross-reference note in ADR-034 (O-4).
10. **CONFIRMED — CI ordering.** `build-and-test` runs Unit (`ci.yml:165-166`), DemoSeed (`:176`) and Regression (`:187-188`) as successive steps, and a Unit red stops the job. So the evidence commits keep Unit and DemoSeed green, and MD-30, MD-39 and MD-41 are placed outside the core and the planner (§ "RED evidence").
11. **CONFIRMED, count replaced by a list — PCS takes hours at six public entry points** (`PeriodCalculationService.cs:345, :368, :781, :845, :1138, :1171`). The test call sites, and the correction service's `RecalculateAsync` sites, are listed in § TASK-14508-T. The refinement lists only the 10 `.ReplayAsync(` calls and "host and PCS facts that send hours in the body". → R25.
12. **CONFIRMED — the 6.00-vs-6.0014 criterion (`:618`) is a 0-hour branch.** `AbsenceRule.cs:46-48, :81-83, :98-100` use `7.4 × fraction` only when `Hours ≤ 0`. Skema is the only absence writer, and R24 refuses ≤ 0. The criterion is met at unit level (TASK-14506); the sprint log says so (O-4).
13. **CONFIRMED as drafted — the two 422 variants.** No `reason` is added to the planner's body, which A8 keeps unchanged (`PayrollPlanRefusalProblem.cs:88-99`). An absent `reason` means the planner variant.
14. **CONFIRMED — the seam to extend is `EnsurePeriodPlannableAsync`** (`PeriodCalculationService.cs:903-911`; called at `Payroll/Program.cs:593`).
15. **CONFIRMED — the stale demo-loader comment** (`tools/StatsTid.DemoSeed/Loading/ApiClient.cs:70-76`). → O-9.

*New in draft 2:* none. Every draft-2 change applied a ruling.

*New in draft 3* (each verified at `299c3dd`):

16. **R42 names "`IUserAgreementCodeRepository`".** No such interface exists. PCS takes the concrete `UserAgreementCodeRepository?` (`PeriodCalculationService.cs:116, :195`), and `git grep "interface IUserAgreementCodeRepository" -- src` is empty. Drafted as "the agreement-code repository (`UserAgreementCodeRepository`)". The intent ("replay never consults it") is unchanged.
    - **Updated by R56:** draft 3 built the replay PCS with no repository and claimed "a null shows replay does not need it". That claim is withdrawn: PCS skips a null repository silently (`PeriodCalculationService.cs:1056`), so a null proves nothing.
    - The sentinel pin now gives the replay PCS a real repository over an unreachable connection (TASK-14515).
17. **CLOSED (N7) — Fable N5's second drift ("`TimeEntryProjectionRow.cs:29` for `OutboxId` is `:30`").** Both lenses now agree that line 29 is `OutboxId` and line 28 is `CorrelationId` (`TimeEntryProjectionRow.cs:28-29`). Nothing to fix.

*New in draft 4* (each verified at `299c3dd`):

18. **R55: "MD-24's split month refuses at the planner before the reader is consulted", and EA-4 "is clean" for MD-21.** MD-24 *is* the mutation that moves the prior-month check before planning. So with MD-21 (no approval reader registered) in the same run, the check runs first with a null reader.
    - The facts that seed M−1 APPROVED and expect a 422 then answer the never-silent 500. These are AR1, AR2, AR5 and AR6 (the week check) and AR10 (the planner), and they are on neither list.
    - In code: the planner's refusal and its classification fetch both happen inside the `PeriodPlanner.Plan(` call (`PeriodCalculationService.cs:1100-1112`, fetch at `:1105`; the 503 is thrown at `HttpRuleClassificationProvider.cs:126`). A check placed before that call precedes both.
    - **Updated by R61:** MD-24 now sits before the per-plan lane's call to `BuildPlanForLegacyCallersAsync` (`:797`, `:861`), not inside the shared builder. That call still precedes the planner and the fetch, so the D1 argument is unchanged.
    - R55's own note on `SplitMonth…` (red as a 500 rather than a 409) is right but incomplete. Five more facts go red unlisted.
    - → deviation D1 ("Needs a ruling" item 6). In the R59 table it is check (c): MD-21 relies on PM, and MD-24 edits PM.
19. **N2's "(or no hours)" for `Flex_TwoMonths`.** Under MD-42, a covered context day is evaluated as an in-period day. With no hours, it then subtracts its day norm from the flex delta (TASK-14505 Do 2's flex rule: delta over in-period covered days), so the September payout would move under MD-42.
    - Only "exactly the day norm" keeps the fact out of MD-42's reach. Applied that way (C-W2); the alternative is dropped.

### Draft 4 → draft 5: Step 0b cycle 3, absorbed (rulings R60–R66; owner OQ1)

*Reviews:*
- the internal lens (Fable `reviewer`): READY-WITH-WARNINGS, 1 W + 5 N;
- the external lens (Codex): NOT-READY, 2 B + 1 W + NOTEs. The Fable seat verified both blockers.

Every BLOCKER and WARNING is mapped below, then the notes.

| Finding | Ruling | What changed, and where |
|---|---|---|
| **Codex B1** — the replay stub cannot observe the claimed differences (RC1/RC2, RL, RP1/RP2) | **R60** | TASK-14515's header: both PCS instances use a test-local echo handler. It encodes entries (context days included), piece fractions and the cutoff, and on `/api/rules/evaluate` echoes the profile; comparisons are over the full `PeriodCalculationResult`. C-R4 rewritten. Rows MD-03, 15, 20, 31, 36, 38, 43, 44 and 52 re-derived against it: no list membership changes; each row now says why its red or its Not is observable; MD-20 and MD-43 gain Not entries (RL; the stayer replays). The real-engine alternative is declined (R60) |
| **Codex B2** — MD-24's site also reaches the raw routes | **R61** | MD-24 sits in the per-plan lane's admission step, before its call to `BuildPlanForLegacyCallersAsync` (`PeriodCalculationService.cs:797`, `:861`); H11 and H5 join its Not list. Also CA-1 step 4, "Needs a ruling" item 9, and contradiction 18 (updated). Repartitioning is declined (R61) |
| **Codex W1** — the settlement fixtures need an entitlement-validation handler | **R62** | TASK-14501 Do 4 gains a "Both fixtures" bullet: the stub factory in `ConfigureTestServices` (`Adr032RevaluationTests.cs:76-83`), with each save's 200 asserted. TASK-14502's fixture rules get the same for Q1–Q7, S6 and S9 |
| **Fable W1 + N1** — MD-27's five reds surface as 500 in EA-2, and "relies on" is written by judgment | **R63** | The MD-27 row and the EA-2 check line state the 500. MD-21's Not entry gains "(outside EA-2)". The path-key legend gets the hard/soft derivation rule. New: a Not-guards table, check (c′) per run (all PASS), and the PLAN · FETCH guard ids. CA-1 step 3 and Gate A-CLOSE CA-1 updated |
| Fable N2 — STAMP-A has no mutation (MD-01a) | Declined (R46a is met by MD-02) | — |
| Fable N3 — the headroom is right, but cited for the wrong reason | **R64** | TASK-14501 Do 4 and C-A2 cite the forskud branch (`SkemaEndpoints.cs:1353-1364`); the SHO employee has no end date before the cell (`:1373-1375`) |
| Fable N4 — MD-39 and MD-42 edit one method | **R65** | TASK-14518 CA-3 gains a bullet: apply both patches together; R39 re-observes both if the handler changes |
| Fable N5 — three citation nits | **R65** | `ApprovalEndpoints.cs:1293-1294` (TASK-14507 Facts; MD-17 row); `TestFixtures.cs:280-297` (TASK-14511-T); the raw `/export` at `:191` / `:212` (TASK-14503a/b Facts) |
| — (procedural) | **R66a** | CA-1 step 3, Gate A-CLOSE CA-1 and the RED-evidence closing paragraph: by-effect re-derivation is limited to rows that can have moved, plus two spot checks per run; the run is the check of record |
| — (procedural) | **R66b** | The split, by move only: briefs → `.claude/plans/s145/TASK-*.md`; this record; a move map for cycle 4 |
| OQ1 (owner question) | **Owner: "Keep for S145"** (2026-10-02, in session) | The header's owner-rulings row and the rulings-in-force list. The "Switch now" instructions are not applied |
| Codex NOTEs (cycle-2 dispositions; D1, E1, N2, the MD-42 site) | Informational | D1, E1, the N2 correction and the MD-42 site are judged sound by both lenses |

**Instruction list (draft 5, unconditional items 1–7)**, item by item:
1. R60: applied (the handler spec, C-R4, nine rows).
2. R61: applied (the MD-24 site and Not list, CA-1 step 4, contradiction 18; also "Needs a ruling" item 9).
3. R62: applied (TASK-14501 Do 4, TASK-14502 fixture rules).
4. R63: applied (the annotations, the derivation rule, the Not-guards table, check (c′)).
5. R64, R65: applied.
6. R66a: applied (CA-1 step 3; also Gate A-CLOSE CA-1 and the RED closing paragraph, which restated the old rule).
7. R66b: applied. The revision row is below.

### Draft 3 → draft 4: Step 0b cycle 2, absorbed (rulings R52–R59)

*Reviews:*
- the internal lens (Fable `reviewer`): READY-WITH-WARNINGS, 4 W + 7 N;
- the external lens (Codex): NOT-READY, 2 B + 2 W + NOTEs.

Every BLOCKER and WARNING is mapped below, then the notes.

| Finding | Ruling | What changed, and where |
|---|---|---|
| **Codex B1** — MD-22 shares the sentinel pin with MD-45, and edits the check MD-23 moves, in EA-3 | **R52** | MD-22 → EA-1; its list gains RS (fresh half). In the R59 table, MD-22, MD-23 and MD-24 all edit PM, so check (b) keeps them in separate runs |
| **Codex B2** — MD-45 reddens the Unit pin `NoApprovalReader_Replay_Succeeds`, so EA-3 stops at its Unit step | **R53** | MD-45 reworded as null-tolerant; added to CA-1 step 4's placement list; "Needs a ruling" item 2 rewritten |
| **Codex W3** — the guard's one-file allowlist conflicts with host registration | **R58** | TASK-14508 Do 1: `AddProjectionHoursReader` in `ProjectionHoursReader.cs`; TASK-14514's guard notes why the allowlist stays one file |
| **Codex W4** — a null repository proves optionality, not that replay never reads it | **R56** (merged with Fable W1) | As Fable W1. The null build is not kept (declined by R56 as redundant) |
| **Fable W1** — the fourth "never consulted" claim rests on a null that PCS tolerates | **R56** | TASK-14515's sentinel pin: a real `UserAgreementCodeRepository` over an unreachable connection; the fresh half runs on the non-sentinel PCS; no profile resolver (C-R3). "Needs a ruling" item 3 and contradiction 16 updated |
| **Fable W2** — MD-45 and MD-40 each redden a Unit test | **R53**, **R54** | MD-45 as Codex B2. MD-40 withdrawn and replaced by **MU-40** (Unit, 14514 re-spawn 2, A.4 gate). `EmploymentEdges_Starter3Sep_…` is marked "covered by MU-40". R46a reworded in fact 2, the table header and CA-1 step 2 |
| **Fable W3** — MD-42 reddens a fact on MD-21's list in EA-2 | **R55** | MD-42's list is now built by effect from an exact site (the endpoint handler): RE1, RE3 (with C-RE2), WP8, WP10; RE2 checked and not reached (C-RE1). MD-21's list is enumerated by effect (RE1–RE3, H3–H5, H12–H14, SMK). **Placement: D1**: MD-21 stays EA-2 and MD-42 moves to EA-4, because R55's EA-4 placement for MD-21 fails check (c) against MD-24 (contradiction 18) |
| **Fable W4** — the termination leg cannot pass on correct code | **R57** | TASK-14501 Do 4: Skema save and re-save on Wednesday 5 November 2025, before `MarkLeaverAsync` and the single settle; `used` 3, crystallized 9.5 (unfiltered 4 / 8.5); preconditions confirmed from the seeds. The same "save, then re-save" wording for `SpecialHoliday_…`. The Facts bullet's "one Skema save" conclusion is dropped |
| Fable N1 — the as-of pin has no mutation | N1 | `ForwardCalc_ResolverAsOf_…` is tagged "(guard; no MD)" (`PeriodCalculationService.cs:570`) |
| Fable N2 — `Flex_TwoMonths` and MD-42 | N2, applied with a correction | "Exactly the day norm" (C-W2); "or no hours" dropped (contradiction 19) |
| Fable N3 — the full-day-only guard must assert the reason | N3 | TASK-14502: `error == "absence_full_day_only"` (`SkemaEndpoints.cs:985`; the quota 422 is `:1424`); C-S4 |
| Fable N4 — the R51 guard misses type drift | N4 | TASK-14501 Do 6 compares with a fresh `init.sql`-only snapshot (`init.sql:1880, :1903, :2056` are `IF NOT EXISTS`) |
| Fable N5 — R49's hand-over needs 14503a's branch | N5 | Ledger row 14503a: the branch is kept until 14506 re-spawn 1 has taken the file (`docs/AGENTS.md:583`) |
| Fable N6 — MD-12 names no site | N6 | MD-12's row: the Skema endpoint's entry replacement path; RC2 and RP4 re-save through `InsertAsync`, out of its reach |
| Fable N7 — citation nits; contradiction 17 | N7 | `ReplayDeterminismTests.cs:33` (field) / `:30` (trait) in TASK-14515; `PositionOverrideConfigs.cs` given its full path (`src/SharedKernel/StatsTid.SharedKernel/Config/`) at both sites; contradiction 17 closed |
| Codex NOTEs (cycle-1 dispositions, the six applications, contradiction 17) | Declined (informational) | Applications 2, 3 and 6 are rewritten by R53, R56 and R59 regardless |
| — | **R59** | § "RED evidence" now carries the fact key, the path key, the partition table (51 rows, built by effect), four check lines per run, the run table and 27 fixture conditions. The forcing-pairs prose is kept as rationale. TASK-14518 CA-1 step 3 re-derives the table and reports the diff; Gate A-CLOSE CA-1 says so. Extension E1 adds the "relies on" column and check (c) |

**Instruction list (draft 4)**, item by item:
1. R52: applied.
2. R53: applied (wording; placement list).
3. R54: applied (MU-40 at the A.4 gate; matrix mark; R46a wording in fact 2, the table header, CA-1 step 2 and Gate CA-1).
4. R55: applied to MD-42's list (by assertion), MD-21's enumeration and the MD-24 row. **The MD-21 → EA-4 placement is not applied: deviation D1.** R55's MD-24 note becomes moot, because MD-21 is not in EA-4.
5. R56: applied (TASK-14515; "Needs a ruling" item 3; contradiction 16).
6. R57: applied (TASK-14501 Do 4 and Facts).
7. R58: applied (TASK-14508 Do 1).
8. Runs: EA-1 18, EA-2 14, EA-3 12, EA-4 7 (51 Docker + MU-40), as targeted, with MD-21 and MD-42 swapped against R55 (D1).
9. N1–N7: applied (N2 with the contradiction-19 correction).
10. R59: applied, with extension E1.
11. Revision row 4: below.

---

### Draft 2 → draft 3: Step 0b cycle 1, absorbed (rulings R41–R51)

*Reviews:*
- the internal lens (Fable `reviewer`): READY-WITH-WARNINGS, 4 W + 6 N;
- the external lens (Codex): NOT-READY, 2 B + 6 W + 1 NOTE.

Every BLOCKER and WARNING is mapped below, then the notes.

| Finding | Ruling | What changed, and where |
|---|---|---|
| **Codex B1** — the frozen inputs omit the settlement cutoff | **R41** | `PlanInputs.SettlementCutoff` (TASK-14500 Do 5); TASK-14511 Do 2 rewritten (the window's inclusive `End` when ≤ `periodEnd`, else null; not "the last EMPLOYED day"); TASK-14513 Do 1 freezes it, Do 3 feeds it to the core; pins `Leaver_LastDayWed30Sep_…` (14514) and `Replay_LeaverEndDateRemovedAfterCalc_…` (14515); round trip covered (14506); MD-43, MD-44 (+ MD-52, application 1) |
| **Codex B2** — evidence coverage incomplete; MU-32 cannot trip | **R46** | (a) "every **non-guard** Docker pin" (fact 2, the MD-table header); "(guard)" tags in 14503b and 14504; MD-47..MD-50; TASK-14518's coverage matrix; (b) MU-32 by injection; MU-39 likewise |
| **Codex W3 + Fable W2** — MD-06 and MD-18 can mask each other | **R44** | MD-18 → EA-3; the code-path exclusivity rule in TASK-14518 CA-1 step 3, Gate A-CLOSE CA-1, and the forcing-pairs text |
| **Codex W4** — replay vs the live calculability checks | **R42** | Admission split from evaluation (TASK-14512 Do 2); replay enters evaluation only (TASK-14513 Do 3); pins `Replay_AfterPriorMonthReopened_…` (sentinel fakes, 14515) and `NoApprovalReader_Replay_Succeeds` (14514); MU-38, MD-45 |
| **Codex W5** — no pin that a rebuild keeps `PlanInputs` | **R47** | New `ManifestProjectionRebuildPlanInputsTests.cs`, `Rebuild_PreservesPlanInputs_ReplaySucceeds` (14515); MD-51; TASK-14513 Do 4 |
| **Codex W6** — backfill ordering not executable | **R48** (narrowed) | TASK-14507 Do 4: per event in stream order (stream order = outbox order per employee), the SKEMA mark first, then `INSERT … ON CONFLICT DO NOTHING`; never insert first |
| **Codex W7 + Fable N4** — a RED Unit file vs the green merge gate; pre-split 14503 | **R49** | `MonthAsWeekCharacterizationTests` authored and observed RED on 14503a's branch, excluded from its merge, taken unchanged by TASK-14506 re-spawn 1; **14503 pre-split** into 14503a (real engine) and 14503b (stub) — taken; ledger, wave table, 5a table, size table and acceptance rows |
| **Codex W8** — shared-file scheduling; 14521's label; 14524's files | **R50** | The rule "no task authors into a file another open-wave task edits" (fact 1); new `EmploymentEdgesTimelineTests.cs` and `EmployeeProfileForwardCalcAsOfTests.cs` (+ `ReplayFrozenInputsTests.cs`, application 4); TASK-14521 `Backend + Infrastructure (cross-domain authorized)`; TASK-14524 names `SinglePlanEvaluationMergerTests.cs` and `RuleRegistryStageBClassificationTests.cs` |
| **Fable W1** — a hire-dated first row looks like a mid-week change | **R43** | TASK-14512 Do 1: PCS drops a date whose previous day is not EMPLOYED before the predicate; the TASK-14505 predicate contract says "callers filter hire-dated rows"; the 14503b starter fact seeds a version-1 row dated 3 September; MD-46 |
| **Fable W3** — harness drift mitigated by inspection only | **R51** | TASK-14501 Do 6: `HarnessDdl_HoursTablesAndManifests_MatchInitSqlSchema` (guard; new `HarnessDdlMatchesInitSqlTests.cs`) |
| **Fable W4** — three facts missing from two lists | **R45** | MD-21 gains the two R31b negatives; MD-26 gains `…AnswersWeekCheck422First`; TASK-14518 publishes resolved names before `K_A` (CA-1) |
| Fable N1 — the 400's insertion point | N1 | TASK-14508 Do 3: between the org-scope 403 and the approval read at `Program.cs:304`; the 14503b 400 facts seed the month APPROVED |
| Fable N2 — the 409 body | N2 | TASK-14512 Do 2: `success:false` and `error` added; the 14503b 409 fact asserts them |
| Fable N3 — R25 as a Unit guard | N3 | TASK-14514: `HoursRepositoryAccessGuardTests` (MU-39 by injection); Step-7a item 8 points at it |
| Fable N4 | → R49 | as above |
| Fable N5 — three citation drifts | N5 | Two fixed (TASK-14505-T: `GetPayoutLineItem` is kept; the site lists drop its sites); the third is disputed (contradiction 17) |
| Fable N6 — process failure (a) at the process level | Declined (adequate this sprint) | — |
| Codex NOTE | Declined (praise; no action) | — |

**Instruction list (draft 3)**, item by item:
1. R41: applied (above).
2. R42: applied, with sentinels on three sources (application 3).
3. R43: applied.
4. R44: applied.
5. R45: applied.
6. R46: applied; ":1054" is now the MD-table header ("every non-guard Docker pin…").
7. R47 and R48: applied.
8. —
9. R49: applied, with the split taken.
10. R50: applied (+ application 4).
11. R51: applied.
12. N1, N2, N3, N5: applied (N5 in part, contradiction 17).
13. The partition is re-made, with ten new MDs (application 1). The ledger, size table, mutation tables, the 5a table, the acceptance table and revision row 3 are updated.

---

### Draft 1 → draft 2: the Fable seat's rulings, absorbed

| Ruling item | Disposition | Where in draft 2 |
|---|---|---|
| § 1, contradictions 1–15 | Confirmed; #2 corrected (`AbsenceRuleTests` reason; `Sprint17` dropped); counts replaced by site lists | § "Brief contradictions"; § TASK-14505-T and § TASK-14508-T site lists; ledger rows 14505-T, 14508-T; size table |
| § 2, N-1 → **R26** | Applied: `IPriorPeriodApprovalReader` + `ApprovalPeriodPriorPeriodReader` in 14512; the fixture rule; 14512-T reclassified and resized M; contradiction 3's fix narrowed | Rulings section (R26); TASK-14512 Do 2; § TASK-14512-T; ledger 14512/14512-T; MD-21; `PcsNeverSilentTests` names; MU-28/29; carried-warnings N2 row; rulings table R21 row |
| **R25** (P1) | Applied: throw before any read; `ProjectionHoursReader` the only Payroll type touching the two repositories; `RecalculateAsync` included | TASK-14508 Do 1–2; § TASK-14508-T; Step-7a item 8 |
| **R27** (P2) | Applied: 5α on the union of both file lists; paired `test-qa` never edits `src/**`; `// S145 R9:` label | Merge checklist step 3; standing constraints; every `-T` brief |
| **R28–R32** (P3–P7) | Applied as drafted (accepted) | Rulings section; TASK-14500; TASK-14502–14504; CA-4 |
| **R33** (P8) | Applied: plain `git revert <sha>` newest first, `-m 1` only for an actual merge commit; the empty-diff contract | Rulings section; Stage B "if it does not converge" |
| **R34** (P9) | Applied — the real omission: `TimelineCoreInput.ForcedNormMode` + `NormRuleId`; the dispatch passes the forced mode and tags its own id | TASK-14505 contract + Do 4; § TASK-14505-T; TASK-14506 pins; MU-37 |
| **R35** (P10) | Applied: `AbsenceRule.Evaluate` "not retired" | TASK-14505 Do 4 |
| **R36** (P11) | Applied as drafted | TASK-14509 Do 2 |
| **R37** (P12) | Applied: optional `DateOnly?` fields, no new event type, mapper by label; the leaver over-naming note | TASK-14512 Do 6 |
| **R38–R40** (P13–P15) | Applied; R40 gains R34's member and the payroll exception names | TASK-14503 Do 5; C-1/C-3; TASK-14505 contract; TASK-14512 Do 1–2 |
| § 3, evidence design | Partition accepted. MD-30 placed in PCS's hydrator, MD-41 in the endpoint handler, MD-39 the named endpoint-layer substitute; CA-1's non-Docker dry run per EA set stays the check, before `K_A` | § "RED evidence" table + forcing-pairs text; TASK-14514 pins (`OkBoundary_April2026_…`, `WindowContract_PositionChangeTue1Sep_…`); MD-42 list |
| § 4.1, header / G-0 | Applied: master `299c3dd`; G-0 satisfied by `36842927335`; "verified at `62fcdc3`, code-identical" kept | Header rows "Master at draft 2", "Pre-merge gate G-0", base commit |
| § 4.2, 14509 label | Applied: `Backend + Infrastructure (cross-domain authorized)` | Ledger row; TASK-14509 Scope |
| § 4.3, 14505 cut line | Applied: the A.1 gate requires 14505b merged; two arithmetic paths only intra-wave, never at a gate | TASK-14505 Acceptance; ledger size cell; size table |
| § 4.4, the 409's exception | Applied: `PriorMonthNotApprovedException`; `PeriodNotCalculableException` kept | TASK-14512 Do 1–2; ledger 14512 files; R40 |
| § 4.5, validate before any write | Applied: one bad cell fails the whole save with 400 and nothing is written | TASK-14507 Do 6 |
| § 5.4, rulings table | Applied: R25–R40 with "lands in"; the W1 row cites the ruling, not "equivalently" | § "Rulings in force"; carried-warnings W1 row |
| § 5.5 | Revision row 2; nothing else reopened | below |

### Revision history

| Draft | Date | By | Change |
|---|---|---|---|
| 1 | 2026-10-01 | `planner` (Opus 5.5) | First draft from refinement rev 3.4 (READY) and the Orchestrator's brief, at master `62fcdc3` (code-identical to `8a87d16`); every file:line re-opened at `62fcdc3`. Stage A in six waves with a named gate (A-CLOSE: merged, 5α/5a per task, V-A green on all jobs, EA-1..EA-4 matched), Stage B outlined for Step 0b and briefed after the gate. Absorbs the cycle-5 carried warnings W1–W3 and N1–N4, plus the Codex wording. Designs out the S144 5α/5a miss as a merge precondition with a gated column. Introduces paired test passes and the payroll-local hours seam. RED evidence: 36 Unit mutations, 42 Docker mutations in four non-cancelling Stage A runs. 15 drafting proposals, 15 brief contradictions, 1 ruling request (N-1) |
| 2 | 2026-10-01 | `planner` (Opus 5.5) | Applies the Fable seat's rulings on draft 1 at master `299c3dd` (docs-only since `018ef2a`; G-0 satisfied, run `36842927335`). **R26** (N-1 ruled (B)): a payroll-local `IPriorPeriodApprovalReader` with a production adapter, the fixture rule, and 14512-T shrunk to fake injection plus named seeding. **R25, R27–R40** (P1–P15 accepted), with the amendments: R25's throw before any read and the census name; R27's union scope, `src/**` ban and R9 label; R33's revert form; **R34's forced norm mode** through the legacy dispatch (+ a pin and MU-37); R35's wording; R37's leaver note; R40's names. All fifteen contradictions confirmed, #2 corrected, counts replaced by site lists. Evidence: MD-30 and MD-41 placed in PCS/the endpoint, MD-39 the named substitute (+ a fingerprint assertion and an AC fixture), MD-42 widened. Plan defects fixed: header/G-0, the 14509 label, the 14505b gate rule, the 409 exception name, validate-before-write. No deviation declared |
| 3 | 2026-10-01 | `planner` (Opus 5.5) | Applies the Step-0b cycle-1 rulings R41–R51 at master `299c3dd`. **R41:** `PlanInputs.SettlementCutoff` is frozen, derived from the employment window's inclusive `End`; two leaver pins. **R42:** admission is split from evaluation, replay enters evaluation only, and a sentinel-fake pin proves it. **R43:** hire-dated agreement rows are filtered before the week predicate. **R44:** MD-18 moves, and the code-path exclusivity rule is added. **R45:** three list entries; resolved names before `K_A`. **R46:** "(guard)" tags, MD-47..MD-50, MU-32 and MU-39 by injection. **R47:** the rebuild pin + MD-51. **R48:** mark-then-insert in the backfill. **R49:** the RED characterization file moves to 14506, and 14503 is pre-split into 14503a/b. **R50:** new files for shared-file conflicts, 14521's label, 14524's file names. **R51:** the harness-DDL guard. Notes N1, N2, N3 and N5 (one drift disputed). The partition is re-made with ten new mutations (MD-43..MD-52) and six named applications; contradictions 16–17 added. No deviation declared |
| 4 | 2026-10-01 | `planner` (Opus 5.5) | Applies the Step-0b cycle-2 rulings R52–R59 at master `299c3dd`. **R59:** § "RED evidence" becomes a mechanical partition table: a fact key, a path key, 51 rows built by effect (site · reds · edited path · relied-on paths · Unit/DemoSeed reach · run), four check lines per run (all PASS), and 27 fixture conditions that make each row determinate. Each test brief pastes its conditions. **R52:** MD-22 → EA-1. **R53:** MD-45 null-tolerant. **R54:** MD-40 → MU-40 at the A.4 gate, and R46a reworded. **R55:** MD-21 and MD-42 lists by effect; MD-42 sited in the endpoint handler. **R56:** the sentinel pin's fourth source is a real repository over an unreachable connection. **R57:** the termination leg is a save and re-save on 5 November 2025 (`used` 3, crystallized 9.5). **R58:** `AddProjectionHoursReader`. Notes N1–N7; N2 applied as "exactly the day norm" (contradiction 19). **Declared:** deviation D1 (MD-21 stays EA-2, MD-42 → EA-4, because R55's placement fails against MD-24, contradiction 18) and extension E1 (a "relies on" column and check (c)). Runs 18 / 14 / 12 / 7. Contradictions 18–19 added; 16 updated; 17 closed |
| 5 | 2026-10-02 | `planner` (Opus 5.5) | Applies the Step-0b cycle-3 rulings R60–R66 and the owner's OQ1 ("Keep for S145") at HEAD `c022e1c` (code-identical to `299c3dd`). **R60:** the replay facts get a test-local echo handler, and nine rows are re-derived against it, with no list membership changes. **R61:** MD-24 moves to the per-plan lane's admission step, and H11 and H5 join its Not list. **R62:** the entitlement-validation stub in the settlement and Skema fixtures. **R63:** the EA-2 500 annotations, the hard/soft "relies on" rule, a Not-guards table and check (c′); all runs PASS. **R64, R65:** citations and the CA-3 note. **R66a:** CA-1 re-derives only rows that can have moved. **R66b:** the plan is slimmed by move: the briefs go to `.claude/plans/s145/TASK-*.md`, and this record goes to the sprint log, with a move map. Runs unchanged: 18 / 14 / 12 / 7 |

## Task ledger (merge checklist record, `PLAN-s145.md` § "The merge checklist")

A row is written when a task merges. **A row with an empty 5α or 5a cell is not merged.**

### Dispositions (one line per task; the close guard's ledger gate reads these)

*Plain language:* the table further down records how each task was merged and reviewed. This short list states each task's outcome in the one word the close check looks for, so no planned task can be silently forgotten (the S141 failure). Every S145 task was built; none was cut.

| Task | Disposition | Where |
|---|---|---|
| TASK-14500 | DONE | merged `1b6160c` (A.1) |
| TASK-14501 | DONE | merged `5c35ffb` (A.1) |
| TASK-14502 | DONE | merged `25eb8eb` (A.1) |
| TASK-14503 | DONE | split into 14503a (merged `32b668a`) and 14503b (merged `510619e`) at Step 0b (R49) |
| TASK-14504 | DONE | merged `630bf89` (A.1) |
| TASK-14505 | DONE | with 14505-T, merged `ce51a38` (A.1) |
| TASK-14506 | DONE | merged `379567e` (A.1), with the R49 file |
| TASK-14507 | DONE | merged `94dea75` (A.1) |
| TASK-14508 | DONE | with 14508-T, merged `f0a863b` (A.2) |
| TASK-14509 | DONE | with 14509-T, merged `755cb14` (A.2) |
| TASK-14510 | DONE | merged `d928cf5` (A.2) |
| TASK-14511 | DONE | with 14511-T, merged `2d14003` (A.3) |
| TASK-14512 | DONE | with 14512-T, merged `250fcdf` (A.4) |
| TASK-14513 | DONE | with 14513-T, merged `07337ea` (A.5) |
| TASK-14514 | DONE | merged `49c00e5` (A.4) |
| TASK-14515 | DONE | merged `eb78b8e` (A.5) |
| TASK-14516 | DONE | merged `46cecd8` (A.2) |
| TASK-14518 | DONE | CA-1 and CA-3 at Gate A-CLOSE; the R-33 split merged `07f7c78` |
| TASK-14519 | DONE | with 14519-T, merged `8367862` (A.5) |
| TASK-14520 | DONE | with 14520-T, merged `53a7812` (B.1) |
| TASK-14521 | DONE | with 14521-T, merged `fe928dde` (B.1) |
| TASK-14522 | DONE | with 14522-T, merged `f6fac97b` (B.2) |
| TASK-14523 | DONE | merged `8efdfffd` (B.3) |
| TASK-14524 | DONE | authored at B.1, merged `8a54484d` (B.3); re-spawn 2's Unit mutation run done |
| TASK-14528 | DONE | C-1 done and frozen (R-127); C-5 built the four evidence commits on the close sha, each byte-identical to its frozen patch; all four runs matched (§ "C-5..C-8") |
| TASK-1902 | DONE | not an S145 task: a prior sprint's task (the payroll host's resource-scope guard), named in § "C-3" only as the origin of the check W-1 floored. Listed so the close guard's ledger gate, which reads every TASK id in this log, sees an outcome |

| Task | Branch | Build | 5α | 5a Reviewer (Fable) | 5a Codex | Merged |
|---|---|---|---|---|---|---|
| TASK-14500 schema + shapes (`data-model`) | `worktree-agent-ac1278752784ecede` (`cf110d9`, `adedc48`, fix-up `ab3b6d9`) | 0 errors / 145 warnings; Unit 1309 · DemoSeed 170 · Regression non-Docker 128 (O-1 on merged master) | PASS | APPROVED-WITH-WARNINGS (`adedc48`); fix-up verification APPROVED-WITH-WARNINGS (`ab3b6d9`) | APPROVE-WITH-WARNINGS; fix-up APPROVE | `1b6160c` (+ O-3: `db-schema.md` hand-transcribed in the generator's format; no Python on this machine, so CI `--check` arbitrates) |
| TASK-14505 week-by-week core (`rule-engine`) + 14505-T paired migration (`test-qa`) | `worktree-agent-ad9053a02a651ddfb` (`b30ae64`, `3ee7e3a`, fix-up `2259cc1`, comments `c81349c`) | 0 errors / 145 warnings (full rebuild); Unit 1309 · DemoSeed 170 · Regression non-Docker 128 (O-1 on merged master). CA2100 not re-counted: no SQL-bearing file is in the merge and the total is unchanged | PASS (19 files, all on the union list; 14505-T touches no `src/**`) | APPROVED-WITH-WARNINGS (`3ee7e3a`, W1); cycle 2 APPROVED (`2259cc1`) | APPROVE-WITH-WARNINGS (`3ee7e3a`); cycle 2 APPROVE (`2259cc1`) | `ce51a38` |
| TASK-14501 harness DDL + A0 backfill, parity and settlement pins (`test-qa`) | `worktree-agent-a1e2cddcd1ccda107` (`167f4a3`, follow-ups `1a8cd42` and `57f227c` (B3), note `1e64c78`) | O-1 at `9b75848` (see below the table) | PASS | APPROVED-WITH-WARNINGS (batched 5a of 14501–14504) | not required (pin task, not high-risk) | `5c35ffb` |
| TASK-14502 Skema A0 endpoint pins (`test-qa`) | `worktree-agent-a219bb4679ce74dae` (`6b633d1`) | O-1 at `9b75848` | PASS | APPROVED-WITH-WARNINGS (batched) | not required | `25eb8eb` |
| TASK-14503a real-engine host option + RE1–RE3 (`test-qa`) | `worktree-agent-a0fe62b0b3b73d252` (`6e4592c` only; `c3543f1` excluded under R49, it merges with TASK-14506) | O-1 at `9b75848` | PASS | APPROVED (batched) | not required | `32b668a` |
| TASK-14503b stub-engine host pins + R38 wording (`test-qa`) | `s145/task-14503b` (`9f3e8bc`, comments `488dcfc`) | O-1 at `9b75848` | PASS | APPROVED-WITH-WARNINGS (batched) | not required | `510619e` |
| TASK-14504 A6 backend pins (`test-qa`) | `s145/task-14504` (`5f0fd3d`) | O-1 at `9b75848` | PASS | APPROVED-WITH-WARNINGS (batched) | not required | `630bf89` |
| TASK-14507 Skema supersession (`backend-infrastructure`) | `s145/task-14507` (`56fdd8c`, fix-ups `93a9370` and `d744ba3`, note `79f18e7`) | O-1 at `9b75848`. O-2: no drift in `docs/api/openapi.json` or `frontend/src/lib/api-types.ts` (run 2026-10-14) | PASS | APPROVED-WITH-WARNINGS (`56fdd8c`); cycle 2 APPROVED-WITH-WARNINGS (`93a9370`); cycle 3 APPROVED (`d744ba3`) | REQUEST-CHANGES (`56fdd8c`, the pre-check BLOCKER, adjudicated); cycle 2 APPROVE-WITH-WARNINGS; cycle 3 APPROVE-WITH-WARNINGS | `94dea75`. Merged after 14501 and 14502 (Fable W1: the harness DDL had to know the new columns first), without a rebase, so the reviewed SHAs stand. No 14507-T. Closes QUAL-192, QUAL-193, QUAL-194. O-9 followed as `9b75848` |
| TASK-14506 new-API Unit pins for the core, the helpers, the fingerprint, the legacy route and the shapes (`test-qa`) + the R49 file | `worktree-agent-a97b8efd3e8cd8e90` (`7dfc9d5`, `574bc5b`; R49 `67b17a0`; re-spawn 1 `80966e8`; fix-up `08ad021`; Orchestrator `2699609`) | O-1 on merged master `379567e`: 0 errors / 145 warnings · CA2100 115 · Unit **1421** (+112: the eight new files) · DemoSeed 170 · Regression non-Docker 128 | PASS; fix-up PASS; `2699609` PASS | APPROVED-WITH-WARNINGS (`80966e8`); cycle 2 APPROVED (`08ad021`); cycle 3 APPROVED (`2699609`) | not required (not high-risk) | `379567e`. The R49 file is byte-identical to `c3543f1`, so `worktree-agent-a0fe62b0b3b73d252` can now be cleared. Unit mutation run (re-spawn 2): 27 of 27 tripped, no findings (§ "A.1 gate") |
| TASK-14510 label for `PRIOR_MONTH_REOPENED` (`ux`) | `s145/task-14510` (`735cba3`) | Branch: tsc 0 · vitest 989. O-1 on merged master `d928cf5`: frontend only, so the .NET side is unchanged since `379567e`; tsc exit 0 · vitest **989** (baseline 988) | PASS (note: the "no `as` cast" item was over-broad, see § Wave A.2) | **SKIP** per the plan's 5a table: pure UI label (`docs/AGENTS.md:353`) | not required | `d928cf5` |
| TASK-14516 the Skema grid refuses a negative at entry (`ux`) | `s145/task-14516` (`83a49a6`, styling `05753a4`, fix-up `330a966`) | Branch: tsc 0 · vitest 1001. O-1 on merged master `46cecd8`: frontend only; tsc exit 0 · vitest **1002** (988 + 1 from 14510 + 13 from 14516) | PASS; fix-up PASS | APPROVED-WITH-WARNINGS (`05753a4`; WARNING = the recovery path unpinned); cycle 2 APPROVED (`330a966`) | not required (not high-risk) | `46cecd8` |
| TASK-14509 next-month honesty + rebuilder + `DayNorm` (`backend-infrastructure`) + 14509-T paired pass (`test-qa`) | `s145/task-14509` (`d61dc3a`; 14509-T `d46112d`, `e2055a2`; Orchestrator `7108c78` (R-3 docs), `826b64b` and `99bf061` (comment and test tweaks)) | O-1 on merged master `755cb14`: 0 errors / 145 warnings · CA2100 115 · Unit **1430** (+8 rows from 14509-T, +1 fact from cycle 3) · DemoSeed 170 · Regression non-Docker 128 · the norm grep is empty. **O-2:** no drift in `openapi.json` or `api-types.ts` | PASS (each cycle) | APPROVED-WITH-WARNINGS (`7108c78`); cycle 2 APPROVED-WITH-WARNINGS (`e2055a2`); cycle 3 APPROVED (`99bf061`) | APPROVE-WITH-WARNINGS (`7108c78`); cycle 2 APPROVE; cycle 3 APPROVE | `755cb14`. One accepted deviation (adjudicator R-1, bounded `hi` only). The reopen-vs-export race is closed by TASK-14512 Do 7. Spun off: QUAL-198 (S146) and HRP-001b |
| TASK-14508 payroll reads its own hours (`payroll-integration`) + 14508-T paired pass (`test-qa`) | `s145/task-14508` (`4cf072c`, guard retired `37ab341`, snapshot `d387b38` (scope widened, QUAL-200); 14508-T `66ec0d1`, pin `0c36ed6`, `0293a71`; Orchestrator `b842233`) | O-1 on merged master `f0a863b`: 0 errors / 145 warnings · CA2100 115 · Unit **1436** (+6: `MonthEndCrossingTests`) · DemoSeed 170 · Regression non-Docker 128 · tsc 0 · vitest 1002 · the R25 grep finds only `ProjectionHoursReader.cs`. **O-2:** no drift | PASS (each cycle) | APPROVED-WITH-WARNINGS (`37ab341`); cycle 2 APPROVED-WITH-WARNINGS (`d387b38`); cycle 3 APPROVED (`b842233`) | APPROVE-WITH-WARNINGS (`37ab341`); cycle 2 APPROVE; cycle 3 APPROVE | `f0a863b`. Adjudicator rulings: the read after planning accepted; the guard retired (QUAL-144 RESOLVED, ADR-039 D5b/D8 amended, `docs/QUALITY.md` updated); the one-snapshot read (QUAL-200 FIXED). Spun off: QUAL-199 |
| TASK-14511 the per-plan lane, the QUAL-186 fix (`payroll-integration`) + 14511-T paired pass (`test-qa`) | `s145/task-14511` (`c775e92`, R-6 `0281d5d`, fix-up 2 `af02f09`; 14511-T `646b132`, `1c4ab78`; merges of master `48a8a7c`, `add9b5b`; Orchestrator `b5f29eb`, `be61c60`) | O-1 on merged master `2d14003`: 0 errors / 145 warnings · CA2100 115 · Unit **1443** (+7: the R-6, R-10 and reply-validation pins) · DemoSeed 170 · Regression non-Docker 128 · OpenAPI no drift | PASS (each cycle) | APPROVED-WITH-WARNINGS (`b5f29eb`); cycle 2 APPROVED-WITH-WARNINGS (`af02f09`); cycle-2 comment NOTEs applied in `be61c60`, not re-reviewed | APPROVE-WITH-WARNINGS (`b5f29eb`); cycle 2 APPROVE (`af02f09`) | `2d14003`. Adjudicator: deviations accepted; R-6 (any failed engine call refuses; MU-44); R-10 (the caller-position fallback retired; MU-45; ADR-023 D1 amended); R-7 (the double count is QUAL-189). Spun off: the QUAL-150 note, B.0 inputs, 14512/14513 brief lines |
| TASK-14512 calculability checks, the dated key, Q7, the race closed (`payroll-integration`) + 14512-T R26 fixture rule (`test-qa`) | `s145/task-14512` (`bd63dd1`; 14512-T `d307220`, `20627b1`; Orchestrator `b6ef74e`, `6b62635`) | O-1 on merged master `250fcdf`: 0 errors / 145 warnings · CA2100 115 · Unit **1443** · DemoSeed 170 · Regression non-Docker 128 · O-2: no drift (the payroll host is not in the published spec) | PASS (each cycle; the race-test file authorized under Do 7's pin clause as the Orchestrator's addition) | APPROVED-WITH-WARNINGS (`d307220`); cycle 2 APPROVED (`b6ef74e`); wording NOTEs applied in `6b62635`, not re-reviewed | APPROVE-WITH-WARNINGS (`d307220`); cycle 2 APPROVE (`b6ef74e`) | `250fcdf`. Replay interim ruled (R-3 consistent). Records: ADR-020 D1 amended (dated key per segment; monthly-period assumption), audit-projection catalog row, QUAL-150 planner half CLOSED, QUAL-201 registered. Post-merge comments `398a002` (the reopen hook states the closed race; `WtmNaturalKey` doc) |
| TASK-14514 new-API calculation pins for A.3/A.4 (`test-qa`): WP1–WP12, EE1–EE2 (Docker) and the Unit pins for the dated key, the refusal body, the never-silent readers, screen parity and the two guards | `s145/task-14514` (authoring `10e3068`, rebased from `1905950`; re-spawn 1 `df1498f`; Orchestrator `8d21893`, `32f0d3e`) | O-1 on merged master `49c00e5`: 0 errors / 145 warnings · CA2100 115 · Unit **1453** (+10) · DemoSeed 170 · Regression non-Docker 128 · O-2: no drift. (On `df1498f` the branch had 146 / CA2100 116: the new SQL helper; justified under QUAL-073 in `8d21893`) | PASS (new files plus the two R50 files, all under `tests/**`; fake hosts only; no wall clock in asserted paths) | APPROVED-WITH-WARNINGS (`8d21893`): every figure recomputed from the spec and matched; W1 (the 14 Docker pins first execute in CI) recorded for CA-4; NOTEs 3–4 applied in `32f0d3e`, not re-reviewed | not required (not high-risk) | `49c00e5`. Unit mutation run (re-spawn 2): see § "Wave A.4" |
| TASK-14513 the replay contract (`payroll-integration`) + 14513-T paired pass (`test-qa`) | `s145/task-14513` (`21fa447`, fix-up `b25bf24`, cycle-2 fix-up `cb8d5e0`; 14513-T `e5e05cb`, `41fd80c`; Orchestrator `9984173`, `702c894`, `fc7658b`) | O-1 on merged master `07337ea`: 0 errors / 145 warnings · CA2100 115 · Unit **1473** (+20) · DemoSeed 170 · Regression non-Docker 128 · O-2: no drift | PASS (cycle 1, 14 files; cycle 2, the delta; the R-11/R-14(ii)/R-18 scope extensions authorized by the adjudicator) | APPROVED-WITH-WARNINGS (`b25bf24`); cycle 2 APPROVED (`cb8d5e0`); NOTEs 1, 2, 4 applied in `702c894`, not re-reviewed | REQUEST-CHANGES (`b25bf24`, three blockers); cycle 2 APPROVE (`cb8d5e0`) | `07337ea`. Rulings R-11..R-25. Registered: QUAL-202, QUAL-203 (routed to TASK-14519), QUAL-204 (product question), QUAL-205, SEC-048. Records owed at close: ADR-016 D10 (the slot, verify-and-refuse, the read-only contract, the marker convention R-21, a found row never substituted R-17), ADR-017 D10's stale replay sentence, the marker doc in `WeeklyTimelineCore.cs` (R-21) |
| TASK-14515 new-API replay pins (`test-qa`): RP1–9, RC1–2, RL, RS (five sentinels), RB, FA (Docker) and the digest facts (Unit) | `s145/task-14515` (authoring `af163e0`, rebased `2734bc4`; re-spawn 1 `a156e9e`; Orchestrator `3fbc8d5`) | O-1 on merged master `eb78b8e`: 0 errors / 145 warnings · CA2100 115 · Unit **1477** (+4) · DemoSeed 170 · Regression non-Docker 128 · O-2: no drift | PASS (five new files plus appends to `HoursDigestTests.cs`) | APPROVED-WITH-WARNINGS (`a156e9e`); W1 and NOTE 1 applied in `3fbc8d5`, not re-reviewed | not required (not high-risk) | `eb78b8e`. Unit mutation run (re-spawn 2, MU-33..35): see § "Wave A.5" |
| TASK-14519 the local-profile activation key from the dated record (`payroll-integration`) + 14519-T pins (`test-qa`) | `s145/task-14519` (`c625445`; 14519-T `6d65234`, re-spawn 1 `d7177d6`, `227351e`; Orchestrator `1e8ae1f`, `e84b65c`) | O-1 on master `9d950ec` (docs-only on top of the merge `8367862`): 0 errors / 145 warnings · CA2100 115 · Unit **1477** · DemoSeed 170 · Regression non-Docker 128 · tsc 0 · vitest 1002 · O-2: no drift. The six pins and the re-seeded Variant A are Docker facts, so no local count moves | PASS (`227351e`, with the Fable 5a) | REQUEST-CHANGES (`227351e`, the pin 6 BLOCKER); cycle 2 APPROVED (`1e8ae1f`); its NOTE applied at merge in `e84b65c`, not re-reviewed | REQUEST-CHANGES (`d7177d6`, the pin 5 BLOCKER); cycle 2 APPROVE (`227351e`); cycle 3 APPROVE (`1e8ae1f`) | `8367862`. Adjudicator R-26..R-32. QUAL-203 residual (R-27, the detector's precedence); QUAL-206 registered (R-30, the live org). Record owed at close: ADR-017 D9c's end-exclusive note (R-31) |
| TASK-14518 R-33, the RS sentinel split (`test-qa`; Gate A-CLOSE) | `s145/task-14518-rs` (`24edd37`; Orchestrator `218b923`) | O-1 on merged master `07f7c78`: 0 errors / 145 warnings · CA2100 115 · Unit 1477 · DemoSeed 170 · Regression non-Docker 128 · `--list-tests` lists RS-P. Test file only, so tsc, vitest and the API contract are unchanged since `9d950ec` | PASS (one file, the authorized one) | APPROVED (`24edd37`); NOTEs 3–4 applied in `218b923` (comments only), not re-reviewed | not required (test-only; R-33) | `07f7c78`. RS-P is a Docker fact, CI-verified at CA-4/CA-5 |
| QUAL-207, the person drawer's baseline race (`ux`; after Gate A-CLOSE, R-49) | `s145/qual-207` (`015f680`; Orchestrator `77f6ae5`) | Frontend only: tsc 0 · vitest **1003** (81 files; +1, the RED-first race fact) on merged master `499771c`. The .NET side is unchanged since `K_A` (O-1 at `07f7c78`) | PASS (cycle 1 and cycle 2; two files, the authorized ones) | APPROVED-WITH-WARNINGS (`015f680`; WARNING 1, the agreement baseline seeded late); cycle 2 APPROVED (`77f6ae5`) | not required (UI only) | `499771c`, the first master commit after CA-7. Outside every Stage A/B file set: the Stage B revert contract allows these two files (see § "Stage A gate — `K_A`") |
| TASK-14520 the weekly rules become timeline-aware (`rule-engine`) + 14520-T paired pass (`test-qa`; B.1) | `s145/task-14520` (`029cd7e`; 14520-T `7692387`; Orchestrator `5715f7e`, comments) | O-1 on merged master `53a7812`: 0 errors / 145 warnings · CA2100 115 · Unit **1478** (+1, from 14520-T) · DemoSeed 170 · Regression non-Docker 128. O-2 runs once, after TASK-14521 merges (this task changes no API shape) | PASS (14520: five `src/**` files, all on its list; 14520-T: four test files, all on its list, every changed value labelled `// S145 B1:`) | **On Opus** (owner direction 2026-10-19; Fable on its usage limit): APPROVED-WITH-WARNINGS (`7692387`; W-1, two comments still said flex merges by a custom rule; N1–N6, stale comments and the inventory count). All comment-only, applied in `5715f7e`, not re-reviewed | APPROVE (`7692387`; NOTE: stale comments, applied in `5715f7e`) | `53a7812`. Split months stay refused until B.2; the refusal now names `NORM_CHECK_WEEKLY` (R-73). 5a NOTE-3 pinned on TASK-14524's branch (`ce269f3`). Owed: Opus NOTE-5 to TASK-14522-T; the ADR-016 D2/D3/D4 amendments at O-4 |
| TASK-14522 split months payable; raw routes refuse them by their own reason (`payroll-integration`) + 14522-T flipped refusal pins (`test-qa`; B.2) | `s145/task-14522` (`39d60f86`; 14522-T `61edc9dc`; Orchestrator `8992a4ce`, comments + two dead helpers removed) | O-1 on merged master `f6fac97b`: full rebuild 0 errors / 145 warnings · CA2100 115 · Unit 1478 · DemoSeed 170 · Regression non-Docker 128 · OpenAPI no drift | PASS (14 files, all authorized; SharedKernel comments only; 14522-T touches no `src/**`) | **On Opus** (owner direction): APPROVED-WITH-WARNINGS (`61edc9dc`; W-1, W-2 stale prose; N-1..N-9). Applied in `8992a4ce`, not re-reviewed | APPROVE-WITH-WARNINGS (`61edc9dc`; WARNING IB1's MB-4 narrative → R-119; NOTE failed merge rows under Success, recorded) | `f6fac97b`. Rulings R-118 (the XB1/XB2 hours seed) and R-119 (TASK-14528 MB-4 (d)). QUAL-149 to be marked BUILT after the EB runs |
| TASK-14524 the Stage B pins (`test-qa`; authored B.1, merged B.3) | `s145/task-14524` (authored `5ee23e4` → rebased `6f6ce59b`; Orchestrator `ce269f3` → `ca7a9c5c`; re-spawn 1 `767d4631`; fix-up `25f224ef`; Orchestrator `d28a97b8`, comments) | O-1 on merged master `8a54484d`: full rebuild 0 errors / 145 warnings · CA2100 115 · Unit **1512** (+34) · DemoSeed 170 · Regression non-Docker 128. Docker facts CI-verified at the close | PASS (9 files: 7 new, 2 end-appends; no `src/**`; all 36 names match TASK-14528's keys) | **On Opus** (owner direction): REQUEST-CHANGES (`767d4631`; B-1 QA1/QP1 setup) → cycle 2 APPROVED (`25f224ef`); the NOTEs applied in `d28a97b8` | **Added beyond the plan** (the internal lens is below the usual review model): REQUEST-CHANGES (`767d4631`, the same B-1) → cycle 2 APPROVE (`25f224ef`) | `8a54484d`. R-120 (B-1's fix; docstring corrections; TASK-14528 MB-12 amended; N-6 accepted). Re-spawn 2's Unit mutation table follows |
| TASK-14523 the blocked notice explains QUAL-209 (`ux`; B.3) | `s145/task-14523` (`d93aee6a`; owner follow-up `820434ab`) | Frontend only: tsc 0 · vitest **1005** (+2) on merged master `8efdfffd`. The .NET side is unchanged since `f6fac97b` | PASS (two files, both authorized) | **SKIP** per the plan's 5a table (pure UI text; `docs/AGENTS.md:353`); the copy was shown to the owner at return (R-85), and the owner ruled to also explain the 409 message | not required | `8efdfffd` |
| Step 7a fix batch, C-3 (R-128..R-133): B-1 the Skema month bound (`backend-infrastructure`); W-1 the LocalAdmin scope floor (Orchestrator); the RED-first pins, the item-10 pin and the R-132 re-shapes (`test-qa`) | `s145/step7a-fix` (`c9f87124` pins; `69b80585` B-1; `a57bb057` re-shape; `06caca3e` W-1; `d4d10d38` fix-up R-133; Orchestrator `f7ac0854`, comments) | O-1 on merged master `8fda8415`: full rebuild 0 errors / 145 warnings · CA2100 115 · Unit 1512 · DemoSeed 170 · Regression non-Docker 128. The new and re-shaped facts are Docker, CI-verified at the close | PASS (each commit in its author's scope; tests only from `test-qa`; one call from the Orchestrator) | **On Opus** (owner direction): APPROVED (`06caca3e`); cycle 2 APPROVED-WITH-WARNINGS (`d4d10d38`; notes, comments applied in `f7ac0854`) | APPROVE-WITH-WARNINGS (`06caca3e`; the seam → R-133); cycle 2 APPROVE (`d4d10d38`) | `8fda8415`. SEC-049..052 and QUAL-214 registered; the C-3 cycle 2 (Step 7a scoped to the fix) follows on the new K |
| TASK-14521 the worklist's mark follows payroll's week rule (`backend-infrastructure`) + 14521-T re-seeded pins (`test-qa`; B.1) | `s145/task-14521` (`1f084e1`; 14521-T `f563dbf`; Orchestrator `4563abc`, comments) | O-1 on merged master `fe928dde`: full rebuild 0 errors / 145 warnings · CA2100 115 · Unit **1478** · DemoSeed 170 · Regression non-Docker 128. **O-2:** no drift in `openapi.json` or `api-types.ts` | **FAIL on labels only** (three changed stamp values unlabelled, five labelled only in prose; all authorized by brief item 3). Labelled in `4563abc`, comments only | **On Opus** (owner direction): APPROVED-WITH-WARNINGS (`f563dbf`; W-1 labels, W-2 red conditions, W-3 → R-117, W-4 → QUAL-212, W-5 stale prose; N-1..N-6). Applied in `4563abc`, not re-reviewed | APPROVE-WITH-WARNINGS (`f563dbf`; W1 the interim state: no change, see § "Wave B.1"; W2 applied in `4563abc`) | `fe928dde`. Rulings R-114..R-117; QUAL-212 registered. The PROFILE and CATEGORY blocks are gone and the residual is QUAL-209. Carried to TASK-14523: `WorklistList.tsx:47-48` |

**O-1 for the six merges `5c35ffb`…`94dea75`.** All six were merged on 2026-10-02 within one minute, with no O-1 between them. O-1 was run once afterwards, on 2026-10-14, at `9b75848`. That commit is the six merges plus O-9, which changes comments and one console line, so it is code-identical to `94dea75` for every test. Result: build 0 errors / 145 warnings (full `--no-incremental` Release rebuild) · CA2100 115 distinct sites · Unit 1309 · DemoSeed 170 · Regression `Category!=Docker` 128, all passing and all equal to the S144 baseline. The counts do not move because every new A.1a pin is a Docker fact, and Docker does not run on this machine; CI runs them. So nothing here verifies the Docker side of the known-red list below.

**Dispositions at the 14500 merge.**
- **Accepted deviation:** the two partial unique indexes live only in the S145 segment, not beside the base CREATEs. On an existing database, a base-position index would fail before the segment's `ADD COLUMN` runs. Both lenses called the brief wrong on this point.
- **Codex W, NULL `task_id` (Fable N7 concurred):** fixed by making the time-entry index `NULLS NOT DISTINCT`. TASK-14507 also gains a 400 for a null or empty project code, and TASK-14501 gains the guard fact `CurrentSkemaCellIndex_NullTaskId_TwoCurrentRows_Collide23505` (verification W1).
- **Fable W1, the ops rebuild script `docker/postgres/scripts/rebuild_segment_manifests.sql`:** added to TASK-14509's authorized files.
- **Fable W2 and N2, comment corrections:** fixed in the fix-up.
- **N3:** recorded in the 14500 brief.
- **N4:** TASK-14513's brief now pins the ordinal `D`-string sort of event ids.
- **Verification N1:** the generator does not print `NULLS NOT DISTINCT`; `init.sql` is the record.

**Dispositions at the 14505 merge.**
- **W1, both lenses: a day after the month could govern the closing week.**
  - The problem: pieces reaching past `PeriodEnd`, or a cutoff past it, let a day outside the month decide the closing week's norm mode and reported `NormPeriodWeeks`.
  - Fixed in `2259cc1` by bounding evaluation at `PeriodEnd` in `PieceIndexOn`. Over-wide input stays valid; TASK-14511 may send an employment piece running past the month.
  - Reading adopted: "a week's governing day is its latest evaluated day, bounded by the period end". Under it, R6 holds automatically.
  - Codex's literal alternative (report the last in-period day's own config) was not adopted. It would report a config other than the one that judged the week, and under the bound the two readings coincide.
  - Both lenses verified at cycle 2 that no in-scope figure can change.
- **How the fix was made.** The rule-engine agent's resume was refused twice ("worktree could not be verified"). The Orchestrator made the two-line fix under the Small Tasks Exception, then sent it to both lenses for cycle 2. The cycle-2 wording notes (both lenses) were applied in `c81349c` exactly as suggested; comments only, not re-reviewed.
- **The pin for the bound:** `Core_PiecesBeyondPeriodEnd_DoNotGovern` goes to TASK-14506, with mutation MU-41. Its shape comes from Fable cycle-2 N3: no cutoff, a weekday `PeriodEnd`, a second piece whose config differs observably, called on the core directly. Today's suite cannot reach the bound.
- **N1, the fingerprint's canonical text** is richer than the refinement states. The ADR-016 D10 amendment transcribes `ConfigFingerprint.cs:21-38` (Records owed).
- **N3, the timeline endpoint's 400 body** (`{success, error}`, also the 5α observation) and **cycle-2 N4** (`weekly-timeline-v1` includes the bound): both recorded in TASK-14511's brief.
- **N4, the year-overview header** computes `Math.Round(WeeklyNormHours × fraction, 2)` for the week (`BalanceEndpoints.cs:796`). That gives 30.01 for fraction 0.811, where the engine's per-day sum is 30.00.
  - Ruled an R11 completion: TASK-14509 Do 5 puts it on `DayNorm`, adding that one line to its authorized files.
  - `ConsumptionCalculator.cs:108, :141` is ADR-032 vacation valuation, not a norm, and stays as it is.
- **N2** (a mixed-mode week, unreachable under Q5 (c); the refinement reserves its test) and **N5** (style): no action.

**At TASK-14504's return (not yet merged; batched 5α/5a pending).**
- **Finding 1, an existing fact turns red under R5(a).** `WriteForExportedMonths_SelectsMonthsIntersectingInterval_…` will fail once 14509 lands, and no task owned it.
  - A new paired task, TASK-14509-T, re-expects it to February, March and April (W1-EX, labelled `// S145 R5(a):`).
  - MD-47's row now lists W1-EX, and EA-1's frozen list grows to 54 facts. MD-47 is the only EA-1 site in that writer.
- **Finding 5:** the guard W1-G (a backdate bounded at Monday 28 Sep flags no October) goes to 14509-T.
- **Finding 3, the reopen hook's `EffectiveFrom`.** The spec was silent. The Orchestrator ruled it is the Monday of M+1's opening week: the earliest day whose change can alter M+1, 28 Sep in the September case.
  - Recorded in 14509 Do 2 and pinned by one added RO1 assertion in 14509-T.
  - Put to the batched Fable 5a for confirmation.
- **Finding 4:** the extra assertions (baseline hash, Created event and audit row) are kept.
- **Finding 6:** the stray detached worktree is cleared at teardown.

**At TASK-14507's return (Step 5a in progress; not merged).**
- **5α: PASS.** Fable: APPROVED-WITH-WARNINGS. Codex: REQUEST-CHANGES (one BLOCKER). The lenses split, so the adjudicator ruled.
- **BLOCKER (Codex), a false quota refusal.** Fixed in a 14507 fix-up; reviewed, not pinned (a race).
  - **The problem.** The pre-check read "what this save replaces" and "what the employee has used" at two moments. A concurrent save of the same cell in between could make it refuse, with a final 422, a save that fit either before or after. Before S145, `used` only ever rose, so a stale read could only be lenient. Reductions now exist, so it can be strict.
  - **The fix.** Both reads share one read-only REPEATABLE READ snapshot, which holds no lock, so the rule-engine call stays outside the lock.
  - **Rejected alternative.** Codex's "re-validate under the lock" would either put the HTTP call under the lock or move the quota decision from the Rule Engine into SQL. An architectural-integrity regression either way.
  - **Accepted residual.** A coherent-but-stale refusal remains possible: a reduction lands between the snapshot and the lock. This class already existed through D4 revaluation. The client can retry, and it is never a wrong write.
- **Codex WARNING, `used = 0` seeding.** A missing balance row is seeded at `used = 0`. Recorded as **QUAL-196** (M), with a comment-only note in the code. No clamp: a floor would hide the inconsistency. The live booking paths always create the row; only the no-config skip, a from-events rebuild or SQL-seeded fixtures reach the state.
- **Codex WARNING / Fable NOTE 5, the backfill's "re-runnable in any state".** The claim is narrowed in a code comment, and the recovery is in the legacy-DB runbook's S145 row. A hand-deleted older row fails loudly; the cure is to truncate the projections and restart, or reseed.
- **Fable W1, merge order.** 14507 merges after 14501 and 14502, because the harness DDL must know the new columns first. Then a rebase and the cycle-2 review.
- **Fable W2, the `awayToday` filter has no pin.** It cannot change any answer until a retraction exists, so no fact and no MD. The pin is owed by the "clear a cell" task (ROADMAP).
- **Deviation (4).** The whitespace project code is now refused too (`IsNullOrWhiteSpace`, mirroring `RequestValidator.cs`). Deviations 1–3 and 5–7 are accepted.
- **Smaller items.**
  - The two 422 bodies use different baselines; that note goes to TASK-14516.
  - The `projectCode` echo in the 400 body stays (sibling convention; SEC-039 governs logs, not response bodies).
  - The backfill tool's new counters (`tools/ProjectionBackfill/Program.cs:82-83`) join O-9.
- **The grid accepts a negative number** (14507's UX finding). The negative now gets a 400 and is re-queued with every later save. New TASK-14516 (`ux`, A.2) refuses it at entry. Zero and empty cells never reach the server (`useSkema.ts:203`).
- **Stale comments** in `DemoLoader.cs`, `SmokeTests.cs` and `ProjectionBackfillService.cs` are added to O-9.
- **No 14507-T:** no existing test pinned the double count.

**At TASK-14501's return (not yet merged; batched 5α/5a pending).**
- **Its pin gap, the absence side of the rebuild.** A follow-up adds B3 (`Backfill_AfterSkemaAbsenceResave_…`), with RED by MD-11. Its fixture conditions keep MD-01, MD-02 and MD-06 off it, and EA-1 grows to 55 facts.
- **The original agent's resume was refused** ("worktree could not be verified"; git ran at about 3 s per call). A fresh `test-qa` agent was pinned to the same worktree.
- **Its drift finding:** harness DDL copies outside the S145 tables differ from `init.sql`. Registered as **QUAL-195** (M), not S145 scope.
- **Its brief contradictions are accepted:** harness `hours NUMERIC(6,2)` corrected to production's `(8,4)`, and the "byte-for-byte" claims corrected. `TxContractTests.SchemaDdl` may become `internal`, removing the reflection.
- **5α of 14502, 14503a and 14504: PASS.** The checklist's "no hard-coded service URLs" item is read as applying to `src/**`. The rule-engine stub's base address in two test files follows eleven existing test files.

**Known-red on master (Docker facts merged ahead of their production task).**

*Plain language:* RED-first pins merge before the code that makes them pass. Until that code lands, CI on master is red on exactly the facts below, and on nothing else. The batched Fable 5a required the list before the merges (WARNING 1). It lets an expected red be told apart from a regression in this window.

| Pinned by | Facts red on master | Turned green by |
|---|---|---|
| TASK-14501 | B1, B2, B3, TRM, SHO | TASK-14507 |
| TASK-14502 | S1–S4, S6, S7, S8 (4 rows), S11, S12, S13, Q1–Q4, Q6, Q7 | TASK-14507 |
| TASK-14503a | RE1, RE2, RE3 | TASK-14511 (A.3) |
| TASK-14503b | H1, H2 | TASK-14508 (A.2) |
| TASK-14503b | H3, H4, H5, H10, H12, AR1–AR8 | TASK-14512 (A.4) |
| existing | X1–X4 (go red at 14512 by design) | re-expected by TASK-14512-T |
| TASK-14504 | W1, RO1, RO3 | TASK-14509 (A.2) |

Green today and expected to stay green:
- PAR, HDL (3 rows) and the null-task collision guard (14501);
- S5, S9, S10, Q5 (14502);
- H6–H9, H11, H13, H14, AR9, AR10 (14503b);
- RO2, RO4 (14504).

**Status after the 14507 merge (`94dea75`), as of 2026-10-14.** The first two rows (B1–B3, TRM, SHO; S1–S13, Q1–Q7) are now expected green, but not yet observed. By design CI has seen none of S145: master is pushed only at sprint close, and the first CI run is the Stage A gate's evidence branch (CA-4). Still expected red until their tasks land: RE1–RE3, H1–H5, H10, H12, AR1–AR8, W1, RO1, RO3. In any CI run before then, anything else red is a regression.

**At the batched Fable 5a of 14501–14504.**
- **Verdicts.** 14501, 14502, 14503b and 14504: APPROVED-WITH-WARNINGS. 14503a: APPROVED.
- **What the reviewer did.** It recomputed every hours and quota figure. It checked each "red today" claim against master's code, and confirmed each guard is green today and each fixture condition is present.
- **The reopen hook's `EffectiveFrom` ruling is confirmed.** It is the Monday of M+1's opening week, the R5(a) widening read in the other direction. Condition: TASK-14509 widens the field's documented meaning (`HrBackdateWorklistRepository.cs:35` and `BackdateWorklistRowCreated.TriggerEffectiveFrom`) for `PRIOR_MONTH_REOPENED`.
- **WARNING 2, a rebuild/live `feriedage` drift.** The backfill stamps hours ÷ 7.4 on any absence event that has none, where the live save writes NULL for a non-entitlement type. Registered as **QUAL-197**. TASK-14513's brief now says the lossless digest must not trip on it.
- **Notes taken.**
  - Two stale "this compiles against today's code" sentences in 14501's files, the `ok_version` label on 14503b's approval rows, the missing weekday-premise assert in the new AR facts, and RE3's docstring on the 26 Oct–1 Nov week: a later comment pass, O-10 (the plan's post-A.1 test tidy; first recorded here as "O-9", corrected 2026-10-14, because the O-9 commit `9b75848` does not cover these).
  - The 14501 5α's "S29 block" is actually S25. Fixed at merge.
  - The entitlement-validation stub now has four copies (the price of R50). Consolidate after A.1.
  - Stale "byte-for-byte" claims in `OutboxTestSchema.cs:11` and `AuditProjectionTestSchema.cs:17` are added to QUAL-195's pointers.
- **No Codex lens for these five.** Pin tasks are not high-risk under the plan's merge checklist.

**Wave A.1a dispositions so far.** These are adjudicator rulings, recorded for the A.1 gate:
- **TASK-14505's March value change** (`OkVersionRuntimeRegressionTests`: one `OVERTIME_50 1.20` dated 2026-03-31) is labelled change 4.
- **The A.1→A.3 intermediate state:**
  - from the 14505 merge until TASK-14511, payroll's per-segment legacy calls run week by week, with the calendar month as the settlement window and no context days;
  - a closing partial week over its in-month norm is paid in-month, dated the month end, until TASK-14511 applies F1;
  - RE1–RE3 stay known-red on local master until A.2/A.3;
  - TASK-14511 is named as the task that closes this.
- **TASK-14503a's extra RE2 assertion** (exactly one `payroll_export_records` row) is confirmed as a non-vacuity guard.
- **TASK-14506's re-spawn 1 pins the fingerprint's exact canonical text** (a contract; recorded at the ADR-016 D10 amendment) and adds 37.00 totals to the 4-week legacy legs.
- **QUAL-190 is widened** to seven dropped config fields (found by TASK-14505, re-checked by the Orchestrator).

**Tooling note.**
- **Failure:** isolation worktrees for four A.1a agents were refused at creation ("git could not be run" / "could not read the repository git config"). Eight simultaneous creations collided, then git ran slowly under build load: 1–6 s per trivial call while VS Code, a VS Code update and the builds held the CPU at 100 %.
- **Workaround:** the Orchestrator created those worktrees by hand (`.claude/worktrees/s145-task-*`) and launched agents pinned to them by absolute path. TASK-14505-T ran inside 14505's existing worktree.
- **Interruption:** a usage limit then cut off six agents mid-work. They were resumed with their context.

**At TASK-14506's re-spawn 1 return (2026-10-14; not yet merged; 5α PASS, 5a APPROVED-WITH-WARNINGS, a fix-up dispatched).**

*Plain language:* these are the Unit tests that define "done" for the new week-by-week core, written from the spec before the core existed. This spawn made them compile against the merged core and added three pins that review had asked for. All 1420 Unit tests pass on the branch, and nothing needed a stop.
- **Branch** `worktree-agent-a97b8efd3e8cd8e90`, rebased onto local master `3f86972` with no conflict: `7dfc9d5`, `574bc5b` (the authored pins, rebased), `67b17a0` (the R49 file), `80966e8` (this spawn). Eight new files, all on the task's list; nothing under `src/**`.
- **R49:** `MonthAsWeekCharacterizationTests.cs` is byte-identical to `c3543f1` (checked by `cmp`) and committed unchanged. Its four facts pass on the merged core with no value changed.
- **No renames and no signature changes** were needed: the pins compiled against the merged 14500/14505 code as authored. One unneeded `using static` was removed.
- **`Core_PiecesBeyondPeriodEnd_DoNotGovern` (MU-41)** added as briefed. The second piece (Thu 1–Sun 4 Oct) differs in `NormPeriodWeeks` (4) and in `NormModel` (`ANNUAL_ACTIVITY`, read by `ModeFor`). Both legs, cutoff null and cutoff 4 Oct, must equal the one-piece reference: `NormPeriodWeeks` 1, norm 162.80, the same `NORMAL_HOURS` lines, no overtime and no flex payout.
- **The fingerprint's canonical text is pinned exactly.** The guessed spellings (`CanonicalForms`) are gone. `Timeline_ConfigFingerprint_StableAcrossPropertyOrder` keeps its name and asserts equality with the ruled ordinal-order form. One extra fact, `Timeline_ConfigFingerprint_MultipleBlocks_SortedByKey_JoinedByNewline_PositionInHeader`, covers a non-null position, the block sort and the `\n` join. The engine matched the ruled form on the first run.
- **37.00 totals** on the `NORM_CHECK_MULTIWEEK` and `NORM_CHECK_37H` legs: both came out at 37.00, as the docstring claimed.
- **Count:** Unit 1420 on the branch, against 1309 on master. The 111 extra facts are the eight new files, including the per-property fingerprint theory rows.
- **Build (Orchestrator, at `80966e8`, unpiped):** 0 errors / 145 warnings (full `--no-incremental` Release rebuild) · CA2100 115 · Unit 1420 · DemoSeed 170 · Regression non-Docker 128, all passing, exit 0 on each.
- **5α: PASS.** Eight files, matching the authorized list one for one; nothing under `src/**`; the R49 file identical to `c3543f1`; all 33 contract-named facts present; no wall clock, randomness or real URLs. Sixteen extra facts were listed and are allowed.
- **5a (Fable): APPROVED-WITH-WARNINGS.** The reviewer recomputed every figure from the refinement and the rulings, not from the code, and found none wrong. It traced each named mutation through the merged core and confirmed the pin moves. For MU-41: with the bound removed, the norm becomes 156.41 instead of 162.80 and `NormPeriodWeeks` null instead of 1. All four Orchestrator rulings are applied as ruled.
- **No Codex lens:** the plan's 5α/5a table marks 14506 as not high-risk.

**Dispositions of the 14506 Step 5a findings.**
- **WARNING, the "a per-day credit uses that day's fraction" criterion (refinement :616) had no pin.**
  - The problem: the flex-credit pin runs one fraction all month. A regression to "the settlement day's fraction" or "the first piece's fraction" would stay green. For a user: someone who drops to 30 h mid-month and books a 0-hour vacation day on each side of the change must be credited 7.40 h for the old-contract day and 6.00 h for the new one.
  - **Disposition: a fix-up, not a deferral.** One new fact, `Core_Flex_MidMonthFractionChange_ZeroHourCredits_UseEachDaysFraction_1340`: fraction 1.0 to Tue 15 Sep and 0.811 from Wed 16 Sep, 0-hour vacation on 9 and 23 Sep, credits 7.40 + 6.00 = 13.40. Either one-fraction reading gives 12.00 or 14.80.
  - **New mutation MU-42** (the credit's fraction taken from the period end's piece) is added to the plan's catalogue and to the brief, and is run by re-spawn 2.
  - The fix-up goes through the merge checklist itself: build, 5α, then a Fable cycle-2 verification.
- **NOTE, a culture-sensitive date format** (`WeeklyPipelineOnCoreTests.cs:96`): fixed in the same fix-up (`CultureInfo.InvariantCulture`).
- **NOTE, no weekday premise on the MU-41 pin:** fixed in the same fix-up.
- **NOTE, `NormPeriodWeeks = 4` on the October piece is masked by `ANNUAL_ACTIVITY`:** recorded only. The pin still trips on `NormPeriodWeeks` and on the norm total.
- **NOTE, the pin's canonical renderer has no `long` arm:** no action. No config property is a `long`; add one when a `long` property appears.
- **NOTE, the "then by block text" tiebreak and "null string → empty" are not exercised:** no action. No live config reaches either.

**The 14506 fix-up, cycle 2.**
- **Fix-up `08ad021`** (the same `test-qa` agent): the new fact, MU-42 in the class's mutation list, and the two NOTE fixes. The core matched every spec-derived figure on the first run: credits 13.40, norm 147.40, worked 134.00, delta 0, no payout.
- **Build (Orchestrator, unpiped):** 0 errors / 145 warnings · CA2100 115 · Unit 1421 · DemoSeed 170 · Regression non-Docker 128.
- **5α: PASS.** Two files, both authorized. The weekday premise sits only in the MU-41 pin. No existing expected value moved.
- **5a cycle 2 (Fable): APPROVED.** It recomputed the figures from the spec and traced MU-42 through `WeeklyTimelineCore.cs:314`: credits become 12.00. The first-piece variant gives 14.80.
- **Cycle-2 NOTE, applied by the Orchestrator under the Small Tasks Exception (one file, about ten lines, test inputs only).**
  - The problem: both absences sat in weeks lying wholly inside one piece, so a regression to "the fraction of the piece governing the week" would still give 13.40.
  - The change: the absences move to Mon 14 and Thu 17 Sep, both in the straddle week Mon 14 – Sun 20 Sep. Every expected figure stays the same. The straddle week is worked 19.40 against a norm of 32.80, so it creates no overtime.
  - What it now also catches: a week-level regression (12.00 at the Sunday's piece, 14.80 at the Monday's).
  - The brief and the MU-42 catalogue row are updated to match.
- **Cycle-2 NOTE, the premise comment over-states the boundary** (strictly, only a Sunday period end makes the MU-41 pin vacuous): cosmetic, no action.
- **Cycle 3, the Orchestrator commit `2699609`.** Build 0 errors / 145 warnings · CA2100 115 · Unit 1421, with the fact also passing alone. **5α: PASS**: one fact touched, its six expected values unchanged. **5a (Fable): APPROVED.** It confirmed that under the old dates the week-level regression reproduced 13.40 by luck (the governing Sundays were 13 and 27 Sep, one in each piece), and that it now gives 12.00.
- **Cycle-3 NOTE:** the docstring says "no overtime arises", but the fact does not assert it. The claim is true, and overtime cannot reach the flex figures. Added to O-10 (the plan's post-A.1 test tidy) as `Assert.Empty(result.Overtime.LineItems)`, rather than a fourth review cycle.
- **Merged `379567e`**: code-identical to `2699609`.

## A.1 gate — closed 2026-10-14

*Plain language:* wave A.1 put the new week-by-week calculation core, the database changes and the Skema "a save replaces its cell" fix on master, together with the tests that define them. The gate checks three things: every task passed its checks before merging, master still builds and passes, and the new tests can actually fail. The last check breaks the production code on purpose, one behaviour at a time in a throwaway copy, and confirms the matching test goes red. All 27 deliberate breakages were caught.

**The checklist** (`PLAN-s145.md` § wave table, row A.1b):
- **All A.1 tasks merged:** 14500 `1b6160c`, 14501 `5c35ffb`, 14502 `25eb8eb`, 14503a `32b668a`, 14503b `510619e`, 14504 `630bf89`, 14505+T `ce51a38`, 14506 `379567e` (with 14503a's characterization file, R49), 14507 `94dea75`. No 14507-T was needed. 14505b was not needed: 14505 landed whole.
- **Every A.1 ledger row has its 5α and 5a cells,** and Codex where the plan marks a task high-risk (14500, 14505, 14507).
- **O-1** on `379567e`: 0 errors / 145 warnings · CA2100 115 · Unit 1421 · DemoSeed 170 · Regression non-Docker 128.
- **O-2:** no drift (after 14507).
- **O-3:** `db-schema.md` was hand-transcribed at the 14500 merge, because there is no Python on this machine. It is arbitrated by CI's `--check` at the Stage A gate's run.
- **14506 re-spawn 2, the Unit mutation run:** 27 of 27 tripped their named pin, with no findings (table below).
- **Not observed yet:** the Docker side of A.1. That covers the known-red list, and 14507's 24 facts expected green. It is seen first at the Stage A gate's CI run (CA-4).

**The Unit mutation table (TASK-14506 re-spawn 2).** Each mutation was applied alone in a detached throwaway checkout at `379567e`, outside the repository, then restored to a clean status before the next. The checkout was removed and pruned at the end, and `src/**` on master is untouched. Every run exited 1 with the named pin failing, read from an unpiped `dotnet test`. "core" is `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/WeeklyTimelineCore.cs`.

| Id | Mutation as applied | Captured failure |
|---|---|---|
| MU-01 | core:153-154, 170-172: one week = the whole period, against one week's norm and thresholds | `Assert.Empty()`: overtime lines present |
| MU-02 | core:180: week lines dated on the Monday | expected 20-09-2026, actual 14-09-2026 |
| MU-03 | `WeeklyContext.cs:69`: a week is settled by the month of its Monday | both pins fail; September holds the closing week's lines dated 4 Oct |
| MU-04 | core:135: `SettlementCutoff` forced to null | expected `[OVERTIME_100 2.00, OVERTIME_50 1.20 @ 29 Sep]`, actual `[]` |
| MU-05 | core:431-433: uncovered weekdays counted on piece 0 | expected 148.00, actual 162.80 |
| MU-06 | core:170-172: the week's norm and thresholds from the Sunday's config | expected 3.60, actual 4.00 |
| MU-07 | core:170-172: the Sunday's fraction for the whole week | HK expected 6.04 / 2.56, actual 7.55 / 2.45; AC expected 8.60, actual 10.00 |
| MU-08 | core:223-224: `NORMAL_HOURS` emitted for context entries | `DoesNotContain()`: the 31 Aug line present |
| MU-09 | core:248, 265: mode and `NormPeriodWeeks` from the week's first covered day | expected 4, actual 1 |
| MU-10 | core:203: annual branch disabled | expected 158.136986, actual 162.80 |
| MU-11 | core:322-329: flex clamped at each week's end | expected 97.00, actual 92.00 |
| MU-12 | core:329: cap from the first in-period day | expected 100.00, actual 110.00 |
| MU-13 | `AbsenceRule.cs` default credit unrounded | expected 10.00, actual 10.0014 |
| MU-14 | `DayNorm.cs:45`: `AwayFromZero` | 4 of 5 rows, e.g. expected 4.62, actual 4.63 |
| MU-15 | `DayNorm.cs:52`: weekends get the weekday norm | expected 0, actual 7.40 |
| MU-16 | `WeeklyContext.cs:101`: context days ignored | the "Tue 29 Sep is a context day of October" row: expected True, got False |
| MU-17 | `WeeklyContext.cs:100-101`: Monday exemption deleted | four Monday rows: expected False, got True |
| MU-18 | `ConfigFingerprint.cs:44`: `WeeklyNormHours` omitted | the `WeeklyNormHours` row: "did not change the fingerprint" |
| MU-19 | `ConfigFingerprint.cs:45`: declaration order | `NotEqual()`: equals the declaration-order hash |
| MU-20 | `RuleRegistry.cs:323`: legacy opening week judged against 37 | `Single()`: collection empty |
| MU-21 | `RuleRegistry.cs:282-283`: legacy lines dated `periodStart` | expected 2026-09-20, actual 2026-09-14 |
| MU-22 | `[JsonIgnore]` on `Source` (`TimeEntryRegistered.cs:26`, `AbsenceRegistered.cs:30`) | both Source facts: expected "SKEMA", actual null |
| MU-23 | `[JsonIgnore]` on `PlanInputs` (`SegmentManifestCreated.cs:82`) | `NotNull()`: null |
| MU-24 | `[JsonIgnore]` on the next-period fields (`RetroactiveCorrectionRequested.cs:47, 54`) | expected 01-10-2026, actual null |
| MU-37 | `RuleRegistry.cs:327`: `ForcedNormMode` null for every norm id | weekly: expected 1, actual 4; annual: expected null, actual 1 |
| MU-41 | core:467-468: the `day > _periodEnd` bound removed | expected 1, actual null (October's annual mode governed the closing week) |
| MU-42 | core:314: credit fraction from the settlement day's piece | expected 13.40, actual 12.00 |

**Readings the agent chose where a catalogue row was loose (not findings).**
- **MU-01:** "one week's norm" is `WeeklyNormHours × fraction`, assigned. A sum of per-day norms over the window would produce no overtime and test nothing.
- **MU-16:** "context days ignored" is expressed through the month of the first settled week's end, because the helper does not receive the period start.
- **MU-22:** applied to both `Source` properties at once. Each Source fact trips on its own event.
- **Extra reds, information only:** MU-21 also turned `Legacy_…ClosingPartialWeekSettledOnPeriodEnd_Dated30Sep` red.

## Wave A.2 (dispatched 2026-10-14)

Four tasks were dispatched at once, each in a worktree the Orchestrator created by hand from `b210a1d`: 14508 (`payroll-integration`), 14509 (`backend-infrastructure`), 14510 (`ux`) and 14516 (`ux`). The authoring of 14514 and 14515, and O-10, are held until the first build-heavy agents return. On 2026-10-02 simultaneous builds held the CPU at 100 % and made git calls fail.

**At TASK-14516's return (not yet merged).**

*Plain language:* the Skema grid now refuses a negative number at entry. Nothing is sent to the server, the cell is marked invalid, and the user sees "Timer kan ikke være negative". Before this, the negative was sent, the save failed, and every later save in that session failed too, with no explanation.
- **Branch** `s145/task-14516` at `83a49a6`. Four files: `SkemaGrid.tsx`, `SkemaDayPanel.tsx` and their two test files.
- **RED first:** six of the new cases failed against the old code. The `7,4` and `-0` cases passed already, as they should. **Green:** `tsc --noEmit` exit 0; vitest 996 passed (baseline 988; +7 grid cases, +1 panel case).
- **Census, accepted:** the day panel's allocation input (`SkemaDayPanel.tsx:212`) feeds the same save (`SkemaPage.tsx:769` → `handleCellChange`), so the same rule applies there. Its test in `SkemaDayPanel.test.tsx` lies a step outside the brief's "its test file" wording. **Accepted:** a changed input without its test would be the real defect.
- **Declared deviation, overruled by an authorization.** With no CSS file authorized, the agent styled the message with existing grey secondary classes (`hiddenNote`, `formLead`). The text was visible but easy to miss as an error, and usability is the first-ranked trade-off. The Orchestrator authorized `SkemaGrid.module.css` and `SkemaDayPanel.module.css`, one error class each, using `--color-error` and mirroring the house error look (`ui/Alert`, `ui/FormField`). It was sent back to the same agent before the review, so the review sees the finished presentation.
- **Accessibility as built:** `aria-invalid="true"`, `aria-describedby` → a `role="alert"` message, plus a `title` on the cell (the `noBasisCell` pattern).
- **Review lenses:** 5α and Fable 5a, because entry behaviour changes. No Codex: not a high-risk category.
- **Styling added** (`05753a4`): `.negativeNote` in each module, `--color-error` text; the grid note also takes the Alert error background and left border.
- **5α: PASS. 5a (Fable): APPROVED-WITH-WARNINGS.**
  - The reviewer confirmed every path that can carry a negative is guarded: typing, paste, blur, the panel input, the page's re-queue from local state (it only holds guarded values), and the read-only manager grid.
  - It confirmed six of the eight new cases fail on master for the stated reasons.
  - It confirmed `#cc0000` on `#fee2e2` is about 4.8:1, which passes AA.
- **Its WARNING, the recovery path (type −2, then 2) is untested:** sent back as a fix-up to the same agent, with grid and panel cases plus panel blur-clears and `-0`.
- **Two NOTEs ride along in the same fix-up (usability):**
  - an `aria-invalid` error outline on the refused cell, which also makes the existing no-basis and over-cap flags visible at the cell;
  - no success-green "changed" flash after a refused negative.
- **NOTEs recorded, no action:**
  - The grid note is a hybrid: Alert's background and border, FormField's red text. It is not a pure mirror of `ui/Alert`.
  - The static DOM ids assume one editable grid per page.
  - Negative hours stored before S145 are covered by reseed-not-migrate.

**At TASK-14510's return; merged `d928cf5`.**
- **What it does:** `triggerKindLabel` renders `PRIOR_MONTH_REOPENED` as "Forrige måned genåbnet". A test renders it.
- **RED:** with the case deleted, the raw kind renders and the test fails (`Unable to find an element with the text: /Forrige måned genåbnet — effektiv fra/`).
- **5α: PASS with a note.** Its "no `as` cast" item came from the Orchestrator's over-broad wording. The CI lint rule covers only the files listed in `frontend/package.json`'s `lint` script, and neither file is listed. The test's `as object` spread follows the file's existing casts.
- **5a: SKIP** per the plan's 5a table: a pure UI label with no backend change (`docs/AGENTS.md:353`).
- **Checks:** tsc exit 0 · vitest 989 (baseline 988), on the branch by the Orchestrator.

**At TASK-14509's return (not yet merged; 14509-T and the adjudicator running).**

*Plain language:* the HR worklist now flags the next month when payroll's month-boundary weeks make it depend on a corrected or reopened month.
- A backdate to Tue 29 Sep flags an exported October too.
- Reopening September under an exported October writes one non-blocking `PRIOR_MONTH_REOPENED` row for October, dated Mon 28 Sep. Nothing is written when the next month opens on a Monday.

The followers:
- the manifest rebuilder and its SQL twin restore `plan_inputs_jsonb`;
- the Backend's daily norm and the year-overview header now use `DayNorm`, so a 30-hour header shows 30.00, not 30.01.
- **Branch** `s145/task-14509` at `d61dc3a`. Build 0 errors / 145 warnings · Unit 1421 · DemoSeed 170 · Regression non-Docker 128. The norm-arithmetic grep under `src/Backend/**` is empty.
- **Declared deviation, put to the adjudicator:** R5(a) widens a *bounded* upper bound only, and leaves the open-ended clip (the current Copenhagen month) alone.
  - Argument: no month after the current one can have been exported.
  - It also keeps the guard `WriteForExportedMonths_OpenEnded_SelectsThroughCurrentCopenhagenMonth_NotBeyond` able to fail.
- **Brief contradictions (factual, accepted):**
  - The range loop does no norm arithmetic; only `:153` does.
  - The header comment spans `:777-778`.
  - No test asserts a part-time header value, so nothing moved.
- **Put to the adjudicator as scope questions:**
  - two full-time monthly norms computed as `(weekdays / 5) × weeklyNorm` (`BalanceEndpoints.cs:173`, `ApprovalEndpoints.cs:1085`);
  - the stale trigger-kind doc comments in `BackdateWorklistRowCreated.cs` (`:4-6`, `:23-24`, `:48`), which lie outside the docstring-only authorization.
- **For TASK-14513:** the live manifest insert does not write `plan_inputs_jsonb` yet. Its JSON must match the event's camelCase `planInputs`, which the rebuilder copies verbatim. That is now written into TASK-14513's brief (Do 2).
- **Recorded, no action:** the hook runs on both reopen arms, employee and leader (Do 2 sets no status condition). Legacy WEEKLY periods take M from `PeriodStart`.
- **Review lenses:** 5α, Fable and **Codex** (retroactive-correction paths), on the combined branch after 14509-T.

**The adjudicator's rulings on 14509 (Fable seat, 2026-10-14; verdict RULED-WITH-QUESTIONS).**
- **R-1, the open-ended clip: ACCEPTED.**
  - The sprint's new reach matters only where a correction stops. An open-ended correction already reaches every later month.
  - The open-ended branch's cut-off marks the last month that can have gone to payroll, so widening it adds nothing payable. It would also mask the UTC-slip guard: under a UTC regression, `hi` = Sat 1 Aug would widen to Mon 3 Aug and still select August.
  - **Changes:** the brief's Do 1 now says "a **bounded** `hi`". 14509-T gains Do 4, the guard `WriteForExportedMonths_OpenEnded_ClipIsNotWidenedIntoNextMonth`: July–September seeded, open-ended from 5 Jul at Copenhagen 1 Aug, exactly July and August. It goes red only if the widening is applied to the open-ended branch.
  - **Declined, recorded here:** nothing enforces "no month after the current Copenhagen month can be exported". The raw export routes take any month. That is pre-existing, GlobalAdmin-only, and costs only an advisory flag, so it falls below the register's floor.
- **R-2, the two `(weekdays / 5) × W` sites: not owed under R11. Registered as QUAL-198 (M).**
  - R11's "one implementation" counted the Rule Engine's and the Skema screen's per-day norms. These are an older monthly denominator.
  - No value differs today: every seeded norm is a multiple of 0.05.
  - The real defect is the missing part-time fraction: a 30-hour employee is shown 162.80 where the engine judges 132.00. It is display only.
  - Fixing it means both sites in one task, plus a per-employee dated-profile read on the roster and a contract-note change. Owner question **Q-1** asks when.
- **R-3, the stale doc comments in `BackdateWorklistRowCreated.cs`: authorization widened** to the file's doc comments (no member, type or namespace change). The Orchestrator applies them as a comment-only fix-up after 14509-T, before the 5a lenses.
- **The combined branch** is now `d61dc3a` (implementer) + `d46112d` (14509-T) + `7108c78` (R-3 doc comments) + `826b64b` (comment narrowed, Codex WARNING). Build at `7108c78`: 0 errors / 145 warnings · CA2100 115 · Unit 1421 · DemoSeed 170 · Regression non-Docker 128 · the norm grep is empty.
- **14509-T** (`d46112d`): W1-EX is re-expected to February, March and April (labelled `// S145 R5(a):`). Also added: W1-G, the open-ended no-widen guard (adjudicator R-1), and RO1's `effectiveFrom = 2026-09-28`.
- **5α: PASS.**
  - Every file is in scope (union per R27).
  - The reopen-handler and header edits are confined to their stated lines.
  - SQL is parameterised.
  - The new row reuses the shared upsert, event and audit tail.
  - A note on the label wording: the R27 text lists R8/R9/R14/R26 only, and `// S145 R5(a):` is 14509-T's own brief-sanctioned label.
- **5a Codex: APPROVE-WITH-WARNINGS.**
  - It verified bounded widening, Monday boundaries and the year rollover.
  - It verified that December 2026 → January 2027 gives Mon 28 Dec, and that May 2026 writes nothing.
  - It verified the twin rebuilders are in sync, and that DayNorm changes only the cent.
  - **WARNING:** the R-4 race is accepted as advisory for 14509, but the hook comment "never behind the state" overstated the guarantee. Narrowed in `826b64b`; the closure is in TASK-14512 Do 7.
- **5a Fable: APPROVED-WITH-WARNINGS.**
  - **WARNING 1:** the R-4 race. The closure stays with TASK-14512 Do 7, which is amended with Fable's condition: the export's read of M must be a row lock (`FOR UPDATE` / `FOR SHARE`), not a plain SELECT.
  - **WARNING 2:** the two new pure date helpers had no locally runnable test, and nothing crossed a year boundary. Sent to 14509-T as two Unit Theories in `BackdateWorklistDerivationTests.cs`, with the rollovers Fri 1 Jan 2027 → Mon 4 Jan 2027 and December 2026 → (2027, 1, Mon 28 Dec).
  - **NOTEs** verified the guard order, the hook end to end, R36, the `effectiveFrom` wire format, non-vacuity, and that no other Docker fact moves.
  - **NOTE 9:** the HR follow-up process register gains **HRP-001b** (the `PRIOR_MONTH_REOPENED` hand-off).
- **Cycle 2 on `7108c78..e2055a2`:** the narrowed comment `826b64b` and 14509-T's Unit Theories `e2055a2` (8 rows; Unit 1429).
  - **Codex cycle 2: APPROVE.** It confirmed that 14512 Do 7's specification closes the race: reopen-first makes the export reject the withdrawn approval, and export-first makes the reopen see the committed export.
  - **Fable cycle 2: APPROVED-WITH-WARNINGS.** Its WARNING: the narrowed comment claimed the next month's export "checks this month's approval before its own transaction". At this commit the export does not look at the prior month at all; that is 14512 Do 2.
  - Its NOTEs: assert row 1's input weekday (the row that catches widening from `toExclusive`), and add a mid-month period-start row.
- **Cycle 3, `99bf061`** (Orchestrator, Small Tasks Exception): the comment now says the export "today does not lock this month's approval at all", that Do 2 adds the check, and that Do 7 locks it. The theory carries and asserts each input weekday. New fact: `NextMonthReachOfReopen_MidMonthPeriodStart_NormalisesToTheMonth`.
  - **Both lenses: APPROVE(D)**, with 5α PASS.
  - Fable NOTE B corrected TASK-14512's "gap" paragraph to today's state. It also added the wider sequential gap: before Do 2, a reopen that commits before the export leaves an export on an unapproved M, which Do 2's 409 closes.
- **Merged `755cb14`** (O-1 and O-2 below the ledger).
- **R-4, handed to the 5a lenses:** M's row lock does not serialize against an export of M+1. In a millisecond interleaving, an October export reads September APPROVED while September's reopen reads no October export, and both commit with no worklist row. It is narrow and advisory. If closed, it is two-sided and belongs with TASK-14512's 409.

**At TASK-14508's return (not yet merged; 14508-T and the adjudicator running).**

*Plain language:* payroll now reads each employee's current recorded hours itself, through one payroll-only reader (`IRecordedHoursReader`, implemented by `ProjectionHoursReader`, R25/R58). A request that still carries hours gets 400 `payroll-entries-not-accepted`. No pay figure changes yet. The previous month's days in the opening week are read and kept, but filtered out until TASK-14511.
- **Branch** `s145/task-14508` at `4cf072c`. `src` builds with 0 errors. The test projects do not compile (57 errors), which is expected: TASK-14508-T migrates them from the implementer's site-by-site work order.
- **Where the checks sit:**
  - The 400 sits after the org-scope 403 and before the approval read on `/calculate-and-export` (N1).
  - On `/recalculate`, which has neither, it is the first statement.
  - The never-silent throw comes before planning in the planless shims, first in the plan-taking path, and before the manifest load in both replay overloads.
- **Declared deviation, put to the adjudicator:** the hours are read after planning, not before. Plan-taking entry points arrive with a plan, one read site serves them all, and the order matches TASK-14512's refusal order.
- **Accepted, small:**
  - `SourceStintId` = the row's event id and `RegisteredAt` = the row's `OccurredAt` in the row-to-input mapping, so the mapped input depends on the row, not the clock;
  - `TryAddSingleton` in the registration;
  - argument checks in the reader.
- **Unnamed finding, put to the adjudicator: a month-end night shift now makes a month fail with a 500.**
  - `AssertNoDroppedBoundaryCrossing` refuses a last-day shift whose after-midnight hours fall in the next month. Payroll now sees real rows, and the Backend accepts such shifts.
  - The check's own comment names the cure, "the caller widening its read", which 14508 delivers.
  - The question is whether it should now drop those hours in M (they are counted in M+1), who owns that, and which pin.
- **Recorded:**
  - A full replay re-reads current rows until TASK-14513 binds the frozen ones (documented on the method).
  - Entries and absences are read as two reads, not one snapshot; the rows are kept as received.
  - The correction service's own export-record read precedes PCS's check, and lies outside PCS.
  - A stale `[Obsolete]` text, which predates the task.
- **Review lenses:** 5α, Fable and **Codex** (payroll export), on the combined branch after 14508-T.

**The adjudicator's rulings on 14508 (Fable seat, 2026-10-14; verdict RULED, no owner question).**
- **Q1, the hours read after planning: ACCEPTED as built.** The brief's "before planning" was stale. Three entry points are handed a finished plan, and R42 already makes the hours read the last admission step. The planner takes no hours (`PeriodPlanner.cs:83-93`). The order on the branch is `RequireHoursReader` → plan invariants → `ReadCurrentAsync` → normalize → classification fetch. TASK-14508 Do 2 is amended.
- **Q2, the month-end night shift: the guard is retired in a 14508 fix-up; the post-midnight hours are paid by the month they fall in.**
  - *Plain language:* a night shift from 23:00 on 30 September to 02:00 on 1 October now counts its three hours across the two months, split at midnight. Before, it would have stopped September's calculation with a server error.
  - *Why it is right:* the guard was interim by design. SPRINT-132:315-320, QUAL-144 and ADR-039 D5b/D8 say it must be removed in lockstep with payroll's widened read, and TASK-14508 is that read.
  - *Verified attribution:*
    - the next month's read starts at `WeekStart(1st) − 1 ≤ this month's last day`;
    - the normalizer conserves the hours across the split;
    - the post-midnight half is in-period for M+1;
    - the pre-midnight half is either an F1 context day of M+1 or, when M's last day is a Sunday, settled by M itself;
    - there is no double count.
  - *Rejected alternatives:*
    - a named 422 would refuse every legitimate month-end night shift, and push HR to re-register shifts in the two-row form ADR-039 D8 rejected;
    - paying the post-midnight half in M would break ADR-039 D3 and F1, and double-count.
  - *Owed:*
    - the 14508 implementer retires the method and leaves a comment that names the pin;
    - 14508-T adds `MonthEndCrossingTests.MonthEndNightShift_Wed30Sep2300To0200_SeptemberNotRefused_PostMidnightHoursReachOctober`, written first and observed RED against the guard;
    - mutation **MU-43** ("restore the throw") is added to the catalogue, run by 14514 re-spawn 2 at A.4;
    - at merge: QUAL-144 → RESOLVED, plus ADR-039 D5b/D8 and `docs/QUALITY.md:220` amended.
  - *The owner is informed* (2026-10-14, in session).
- **Q3, brief contradictions: confirmed.**
  - The org-scope 403 reads user and org rows itself, which is accepted; the 403-first order stands.
  - `/recalculate`'s 400 comes first, before the idempotency read.
  - `ReplayAsync(Guid)` never had hours.
  - The never-silent check on the by-id replay is consistent with R25.
  - Brief Do 3 is amended.
- **Declined and registered:** a leaver whose last shift crosses midnight past the end date is paid by no month for the post-midnight hours. It predates S145 and is a domain question. **QUAL-199** (M) is registered, and a ROADMAP backlog line added.

**Owner ruling Q-1 (2026-10-14, in session): QUAL-198 is done in S146**, as its own task, with the person's own norm (the fraction at month end). It is recorded in the register row and the ROADMAP.

**At TASK-14508-T's return.**
- **Branch:** `66ec0d1` on `s145/task-14508`. Every listed site is migrated through an in-memory `IRecordedHoursReader`, kept as two copies because the test projects share no support project.
- **No expected value changed.** The `// S145 R14:` tag appears only in explanatory comments.
- **Checks:** build 0 errors / 126 warnings · Unit 1421 · DemoSeed 170 · Regression non-Docker 128.
- **One unlisted adjustment:** `ReplayDeterminismTests` forward3 gets its own February-only reader, so its inputs keep their meaning.
- **Scope addition (adjudicator Q2), written RED-first:** `MonthEndCrossingTests.MonthEndNightShift_Wed30Sep2300To0200_SeptemberNotRefused_PostMidnightHoursReachOctober` (`0c36ed6`).
  - The fixture uses a window-honouring reader and a fake engine that captures entries.
  - **RED observed** against the guard: `PlannerInvariantViolation … post-midnight hours dated 2026-10-01, AFTER PeriodEnd 2026-09-30` at `AssertNoDroppedBoundaryCrossing` (`PeriodCalculationService.cs:2059`).
  - Unit was 1421 passed + 1 failed; the full build had 145 warnings. The earlier "126" came from an incremental build.
  - Assertions 2–4 run first after the implementer's fix-up.
- **The guard retired** (`37ab341`, implementer, `PeriodCalculationService.cs` only, +12/−52). The method and its call are gone, and a comment at the normalization site names the attribution and the pin. **The pin is green on all four assertions**; assertion 4, the October 2.00 h half, ran for the first time and passed. Build (Orchestrator, unpiped): 0 errors / 145 warnings · CA2100 115 · Unit 1422 · DemoSeed 170 · Regression non-Docker 128. The R25 grep finds only `ProjectionHoursReader.cs` in source.
- **5α: PASS** on the union scope.
  - No expected value changed; `ProjectionHoursReader.cs` is the only file naming the repositories; the 400 body is fixed text.
  - **Noted for the merge records:** ADR-016 `:180` and ADR-039 `:153` still describe the guard as live. `MonthEndCrossingTests.cs:39` still says "RED until retired", which is added to O-10.
- **5a Codex: APPROVE-WITH-WARNINGS.** No production blocker. The guards, registration, current-row filter, mapping and correction service match the rulings, and month-end attribution works by inspection for weekday and Sunday endings and year boundaries.
  - **WARNING:** a Sunday month-end is unpinned. If the extra day were removed from `HoursReadStart`, a Sun 31 Jan 2027 shift's post-midnight hours would be lost with the Wednesday pin still green.
  - **WARNING:** the pin proves delivery but not conservation or uniqueness. `Distinct()` could hide a duplicated half.
  - **NOTE:** the in-memory reader turns a null `SourceStintId` into an event id, so its "same input" doc should say so.
- **5a Fable: APPROVED-WITH-WARNINGS.**
  - **WARNING 1:** entries and absences are two reads on two connections. `/recalculate` has no lock precondition, so a recalculation during a Skema save could see the save half-applied. The lenses diverged on this one, so it went to the adjudicator.
  - **WARNING 2:** the read window has no exact local pin. Hand-checked correct: October 2026 → 27 Sep; June 2026 → 31 May; January 2027 → 27 Dec 2026.
  - **NOTEs:**
    - ADR-039 D5b/D8 and QUAL-144 are owed at merge.
    - `IRecordedHoursReader.cs:19-21` cites an ADR-016 amendment not yet written; it is in the plan's Records owed.
    - The 400 facts are CI-only.
    - Replay re-reads current rows until 14513.
    - A stale `[Obsolete]` text.
    - The smoke probe now runs on the live DB's rows.
- **Fix-up dispatched to 14508-T** (the test findings of both lenses):
  - the exact-window theory `HoursReadStart_IsTheDayBeforeTheOpeningMonday` (October 2026, June 2026, January 2027, February 2027);
  - assertion 3 tightened to `== 27 Sep`;
  - the Sunday fact `MonthEndNightShift_Sun31Jan2027_2300To0200_FebruaryReadsItAndPays200On1Feb`;
  - per-request capture without `Distinct()`, asserting 1.00 h and 2.00 h halves exactly once;
  - the reader doc qualified.
- **Returned `0293a71`:** Unit 1427 (+5). The agent proved non-vacuity in a throwaway checkout: dropping the `− 1` from `HoursReadStart` turns all six `MonthEndCrossingTests` cases red. Shape found: PCS sends a segment's entries to each of the four time rules, so the halves are asserted once per request, never twice within one.
- **The adjudicator on Fable WARNING 1, the two-snapshot read (Fable seat, 2026-10-14; RULED, no owner question).**
  - **The defect class is real; the stated route is not.** An exported month cannot be reopened (ADR-034 D5), and APPROVED locks both write paths.
  - **Two narrower routes are reachable:**
    - **A,** a raw-exported month, which stays DRAFT, so it is recalculable and saveable;
    - **B,** the previous month's days inside the opening week, which are locked only by that month's own approval.
  - **Fix (a):** one read-only REPEATABLE READ snapshot in `ProjectionHoursReader`. It takes no lock and cannot deadlock with 14512 Do 7.
  - **Fix (b) rejected:** requiring APPROVED on `/recalculate` would strand raw-exported months, whose only correction path it is, and would not close B. Because it is rejected on the merits, no owner question arises.
  - **Scope widened** for the 14508 implementer's fix-up 2 to `TimeEntryProjectionRepository.cs` and `AbsenceProjectionRepository.cs`, one `(conn, tx)` overload of `GetByEmployeeAndDateRangeAsync` each, the SQL existing once. As built, both public overloads call one private query method per repository; that is not literal delegation, which would have forced a transaction onto every Backend read or a `null!` transaction (Fable cycle 2 judged the helper the better shape).
  - **Reviewed, not pinned** (a race). Registered as **QUAL-200** (M, FIXED).
  - **Pointer for TASK-14513:** the digest line forms must never include `superseded_by_outbox_id`, the one mutable column, or a later re-save would fail an untouched row's replay. The refinement's forms already exclude it, and 14513's brief now says so explicitly.
- **Fix-up 2 returned** (`d387b38`, implementer, three files, +104/−13):
  - one read-only REPEATABLE READ snapshot, with `SET TRANSACTION READ ONLY` first and nothing else inside;
  - CA2100 still 115.
  - Build (Orchestrator, unpiped): 0 errors / 145 warnings · Unit 1427 · DemoSeed 170 · Regression non-Docker 128.
  - **It flagged for 14514 and 14515:** the PCS parameter is `hoursReader`, and `ProjectionHoursReader` now takes `DbConnectionFactory`. Both briefs carry a re-spawn-1 reconciliation note.
- **Cycle 2 on `37ab341..d387b38`:**
  - **Codex: APPROVE.** It confirmed the snapshot, the unchanged parameterised SQL, and the `ReadByEventIdsAsync` justification against every UPDATE of the two tables.
  - **Fable: APPROVED-WITH-WARNINGS; 5α: PASS.** It confirmed the snapshot line by line (isolation and READ ONLY before the first SELECT; ACCESS SHARE only; disposal; DI) and the four exact read starts.
  - **Its WARNING:** the next-month requests were bounded only from above, so a leaked previous-month half, a double-paid hour, would have passed.
  - **Its NOTEs:** the conservation sum added two literals; the comment said "four" requests where there are five (four time rules plus flex); the reader comment leaned on TASK-14513 as if landed; and the log wording here, now corrected.
- **Cycle 3, `b842233`** (Orchestrator, Small Tasks Exception):
  - every request in the one-row scenarios must carry **exactly one** entry, which pins both leak directions;
  - the sum adds observed hours;
  - the comments are corrected;
  - `MonthEndCrossingTests`: 6 passed under the stricter assertion.

**At TASK-14516's cycle 2; merged `46cecd8`.**
- **Fix-up `330a966`:** five recovery cases, the `aria-invalid` cell outline, and no flash after a refused negative. tsc 0 · vitest 1001.
- **Fable cycle 2: APPROVED; 5α: PASS** (folded into the verification for a four-file fix-up).
- **Cycle-2 NOTE, recorded as a visible change:** the outline also marks the S123 over-cap absence flag. That flag is data-derived and persistent, so absence cells on an over-norm day now show a red outline where before the flag was tooltip and screen-reader only. This is intended, an accessibility improvement, and not a regression.
- **Cycle-2 NOTE, backlog:** the no-basis block (S123) still flashes success-green after a refused partial. A one-token follow-up (`&& noBasisCell !== editing.key`) would make the two refusals symmetric. It predates this task.

**At TASK-14514's and TASK-14515's authoring returns (merged later, at A.4 and A.5).**
- **14514 (`1905950`):**
  - six new files plus the two R50 files; every fact name exactly as briefed;
  - the three pins that compile today pass (`ScreenParityTests`, the R31d guard, `HoursRepositoryAccessGuardTests`), and each tripped under MU-31, MU-32 or MU-39 in a throwaway checkout;
  - **brief contradictions:**
    - `EmploymentWindowSegmentSkipTests.cs:51…:237`, MU-40's anchor lines, refer to the post-14511-T file and must be re-checked at A.3;
    - `ScreenParityTests` already passes on master because the A.1 core is merged, so its RED is MU-31's;
    - EE2's "October evaluates nothing" is asserted in a form that holds whether or not PCS calls the engine for an all-NOT_EMPLOYED month.
- **14515 (`af163e0`):**
  - five Docker files and one Unit file, including `ReplayEchoRuleEngine` and `ReplayTestKit`;
  - the digest facts rehearsed against a throwaway spec implementation: each tripped under its MU;
  - **QUAL-197 check:** the absences digest line form has no `Feriedage`, so the backfill drift cannot trip it; there is a guard fact for that.
  - **Brief contradictions, accepted:**
    - `TestFixtures.BuildPcs` cannot take the sentinels, so `ReplayTestKit.BuildPcs` builds PCS by named arguments;
    - "differs" is compared with manifest ids stripped, because a fresh calculation always mints a new id.
- **Both carry "assumed names"** that differ from 14508's actual names (for example `recordedHoursReader` against the merged `hoursReader`). Re-spawn 1 of each reconciles them; that is expected.

## A.2 gate — closed 2026-10-14

*Plain language:* wave A.2 made payroll read the recorded hours itself, as one consistent snapshot, and refuse hours sent in the request. It made the HR worklist honest about weeks that cross a month boundary. It moved the Backend's norm figures onto the same per-day helper payroll uses. It refused negative hours at entry in the Skema grid, with a visible and announced error. And a month-end night shift no longer stops a month being calculated. Every task passed a mechanical check and its review lenses before merging. Master is green on everything that runs locally.

**The checklist** (`PLAN-s145.md`, wave table row A.2):
- **Merged:** 14510 `d928cf5`, 14516 `46cecd8`, 14509+T `755cb14`, 14508+T `f0a863b`. Every row has its 5α and 5a cells, and Codex where high-risk (14508, 14509).
- **O-1** on `f0a863b`: 0 errors / 145 warnings · CA2100 115 · Unit 1436 · DemoSeed 170 · Regression non-Docker 128 · `tsc --noEmit` exit 0 · vitest 1002.
- **The norm grep under `src/Backend/**` is empty**, as 14509's acceptance requires.
- **O-2:** no drift (run after 14509 and again after 14508).
- **Authored for later gates:** 14514 (`1905950`, merges at A.4) and 14515 (`af163e0`, merges at A.5). Both carry the re-spawn-1 name reconciliation note.
- **O-10** (the post-A.1 test tidy) was dispatched after 14508-T merged. It is tests only and not a gate item.
- **Records written at the merges:**
  - QUAL-144 RESOLVED; ADR-039 D5b/D8 amended; `docs/QUALITY.md` updated;
  - QUAL-198 (S146, owner Q-1), QUAL-199 and QUAL-200 (FIXED) registered;
  - HRP-001b added; the ROADMAP gained two lines.
- **Brief amendments carried forward:**
  - 14512 Do 7: close the reopen-vs-export race with a row lock;
  - 14513: the column's shape, and no mutable column in the digest;
  - 14514: MU-43.
- **Still owed:** the ADR-016 read-contract amendment for `IRecordedHoursReader` is in the plan's Records owed, at close.
- **Not observed yet:** the Docker side of A.1 and A.2. It is first seen at the Stage A gate's CI run (CA-4).

## After the A.2 gate

**At O-10's return (the post-A.1 test tidy); merged `a59825b`.**
- **Branch** `s145/o10` at `ee0ce4d`: 11 files, tests only, no expected value changed. Build 0 errors / 145 warnings · Unit 1436 · DemoSeed 170 · Regression non-Docker 128, all unchanged.
- **All seven items done:**
  1. Four stub copies moved onto `TestSupport/EntitlementValidationStubHandler.cs`.
  2. `SeedMonthApprovalAsync` takes `okVersion`.
  3. A weekday-premise fact for the AR dates.
  4. Two stale sentences reworded.
  5. RE3's docstring corrected: 28 Sep–4 Oct is October's opening week under F1.
  6. `Assert.Empty(result.Overtime.LineItems)` in the MU-42 pin.
  7. `MonthEndCrossingTests`' docstrings updated for the retired guard.
- **Brief contradictions, accepted** (the O-10 list was written from review notes, not the files):
  - nine further identical private stubs exist (the implementer counted eight; the reviewer listed nine); they go to a ROADMAP sweep line rather than this tidy;
  - the `HarnessDdl` stale claim was "AHEAD of init.sql", not "compiles against today's code";
  - `MonthEndCrossingTests` never held the quoted phrase; the stale claims were reworded as found;
  - RE3's real mis-assignment was calling 28 Sep–4 Oct "September's closing week". Its test name still says `ClosingWeek`; test names are contract (the evidence lists), so it is left as is and noted here.
- **NOTE:** the new weekday fact sits in a Docker-gated class, so it runs only in CI. It is cheap date arithmetic, and moving it is optional.
- **Review:** one combined pass. **5α: PASS; Fable: APPROVED-WITH-WARNINGS.** It verified the stub bodies are verbatim, no product query reads `approval_periods.ok_version`, every weekday, the harness DDL against `init.sql`, RE3 under F1, and the commit history behind the `MonthEndCrossingTests` wording. **WARNING, recorded:** eight of the eleven files are Docker-gated, so the local runs never executed them; the stub move and the label change are equivalent by inspection, and the first push is their first execution. **NOTEs applied** in `2cd2a83` (comments and usings only, as suggested, not re-reviewed): the RE3 banner and two unused `using System.Text`.
- **Merged `a59825b`.** O-1: 0 errors / 145 warnings · CA2100 115 · Unit 1436 · DemoSeed 170 · Regression non-Docker 128.

## Wave A.3 (dispatched 2026-10-14)

**At TASK-14511's return (the per-plan lane, the QUAL-186 fix; not yet merged).**

*Plain language:* payroll no longer judges a month as one giant week. It sends each month to the Rule Engine once, and the engine judges it Monday to Sunday.
- **A normal 37-hour September** (full-time HK, 7.40 h every weekday) now exports **no overtime and no flex payout**, with a norm of 22 × 7.40 = 162.80. Before, it exported 3.0 h at 50 % plus 122.8 h at 100 %, and a 25.8 h flex payout.
- **A week crossing a month boundary is paid by the month holding its Sunday.** 9 h on 28–30 Sep plus 7.40 h on 1–2 Oct is paid in October as 3.00 h at 50 % plus 1.80 h at 100 %, both dated 4 Oct. September pays nothing for that week.
- **A leaver whose last day is 30 Sep** has the week settled in September, dated 30 Sep.
- These figures come from a throwaway in-process run by the implementer. The binding evidence is RE1–RE3 (Docker, CI).

The detail:
- **Branch** `s145/task-14511` at `c775e92` (one file, `PeriodCalculationService.cs`, +915/−324). Build 0 errors / 145 warnings · CA2100 115 · DemoSeed 170 · Regression non-Docker 128 · Unit 1428/1436.
- **The 8 Unit reds** are 7 per-segment call-count and request-shape assertions (14511-T's work), plus `MonthEndCrossingTests`. Under F1, October's timeline request now correctly also carries 30 Sep's pre-midnight half as a context entry.
- **Built:**
  - pieces: the context days only when employed on the 1st, then one per employed segment;
  - `SettlementCutoff` = the employment window's end when ≤ `periodEnd`;
  - the profile and agreement-code reads widened to the opening Monday;
  - one timeline call; the three results joined; the flex carry and `CallFlexRuleAsync` retired;
  - the attempt budget counts real calls;
  - a non-success or incomplete timeline answer is a failed call, never read as a result;
  - four outcome members kept for TASK-14513 (`TimelinePieces`, `SettlementCutoff`, `ConfigFingerprints`, `EvaluationModel`);
  - `PlannerBypassGuardTests` stays green.
- **Brief contradictions:** the brief's line numbers are pre-A.2, and the actual ones are listed in the return.
- **14511-T's list was widened by the Orchestrator** to `MonthEndCrossingTests.cs` and `RetroactiveCorrectionWindowTests.cs` (`:165-169`; the timeline call carries the period end). Both were briefed with exact assertion shapes; the second does not assert the cutoff (C-T1).
- **Put to the adjudicator:**
  - five declared deviations: the window read not widened; several employed segments split by line date (fixtures only); caller-built plans read histories; only covered days sent; and the timeline 400's sentence logged with the employee id;
  - **the partial-failure rule:** a failed timeline call drops overtime, norm and flex together while the month still reports Success;
  - **a spec question:** the same hours over norm count in September's flex and in October's overtime.

**The adjudicator's rulings on 14511 (Fable seat, 2026-10-14; verdict RULED, no owner question).**
- **R-1 to R-4, deviations 1–4: ACCEPTED.**
  - The window read stays: the resolver returns the whole overlapping spell, and a spell that ended before the 1st was settled by its own month.
  - The split by line date is accepted for Stage A. Its Stage B planning input is written into TASK-14524's brief: the merger must accept the per-segment rows.
  - Caller-built plans read histories as the A.3→A.5 intermediate state. TASK-14513's brief now says replay must not re-derive pieces or cutoff.
  - Only covered days are sent; this matches A2 `:233`.
- **R-5, the 400 sentence plus the employee id in the log: KEPT.** It is the shape SEC-039's own remediation ruled acceptable. The sentence is a literal with no interpolation, and status-only would lose the diagnosis.
  - **Owed at close:** one sentence on SEC-039's row.
  - The engine-side "value-free" promise is unpinned, so it gets a ROADMAP guard line.
- **R-6, the partial-failure rule: CHANGED.**
  - *Plain language:* until now, if the one call that computes overtime, norm and flex failed but the smaller calls succeeded, the month reported Success and exported without overtime, norm lines or flex. That is wrong money labelled Success.
  - **Now any failed rule-engine call refuses the whole calculation:** `Success = false`, no export lines, no manifest, a fixed sentence, and the host's existing 422. A plan with no employed segment still succeeds.
  - It is strictly safer, so there is no owner question.
  - *Why it was never caught:* the pre-S145 pins covered only "all calls fail" and "all succeed". The three Regression fakes answering 404 to the timeline would have run green with three rules missing.
  - **Owed:** a 14511 fix-up (implementer, after 14511-T), plus two RED-first pins in 14511-T:
    - `TimelineCallFails_PerSegmentCallsSucceed_CalculationRefused_NothingWritten`;
    - `OnePerSegmentCallFails_TimelineSucceeds_CalculationRefused_NothingWritten`.
  - The two existing message asserts take the new sentence, labelled. Mutation **MU-44** is catalogued, run by 14514 re-spawn 2 at A.4.
- **R-7, the double count: SPECIFIED and already registered as QUAL-189** (pre-existing: an hour paid as overtime also raises flex). New under F1: the two counts can fall in different months. That is appended to QUAL-189 as a design constraint on any future fix. Not a 14511 defect.
  - *Plain language:* 4.8 h over norm on 28–30 September raise September's flex balance and are also paid as overtime in October.
- **R-8 and R-9, the two widened test files: CONFIRMED,** with additions sent to 14511-T:
  - `MonthEndCrossingTests`' engine must answer the timeline path with the complete shape, or under R-6 the month is refused;
  - the exact piece shapes for `RetroactiveCorrectionWindowTests`.
- **Declined, recorded:**
  - (1) the engine-sentence guard, now a ROADMAP line;
  - (2) the spells cutoff, for the deferred spells increment;
  - (3) 422 vs 503 for an engine outage, which 14512 may take;
  - (4) **a watch item for CA-4.** A context piece is now profile-resolved fail-closed, so a Docker fixture whose `employee_profiles` row starts on the 1st while its window covers the opening week will refuse with `employment_record_gap`. This is correct behaviour, but it may surface as unlisted reds at the Stage A gate.

**At TASK-14511-T's return** (`646b132` on `s145/task-14511`, on top of `48a8a7c`, a merge of master that brings O-10's test tidy; the implementer's `c775e92` is untouched).
- **Every stub and fake answers `/api/rules/evaluate-timeline`** with a complete reply: three results, the full flex block, `["stub"]` and `weekly-timeline-v1`. The shared default emits `NORMAL_HOURS` only for in-month entries.
- **Per-segment call counts and request shapes are rewritten.** The timeline's pieces and entries are asserted. No `SettlementCutoff` is asserted anywhere (C-T1), and no money or hours figure changed.
- **Two facts are re-expressed** because the flex carry is retired: `FlexCarry_PassesThroughNotEmployedMiddleSegment_Unchanged` (unit) and `TwoSpells_FlexDeltaCarriesThroughNotEmployedMiddleSegment` (Docker). Now the opening balance reaches the one timeline call unchanged, and the gap days are left out of the pieces and entries.
- **`MonthEndCrossingTests`** tells timeline requests from per-segment ones: October's timeline carries the 30 Sep context half, and no per-segment request does. **`RetroactiveCorrectionWindowTests`** asserts the pieces end on the leaver's last day, with no cutoff.
- **The R-6 pins are added RED-first.** Four Unit facts are RED on `c775e92` by design: the two new refusal facts (`Assert.False` failure: a failed call still reports Success) and the two re-messaged asserts (the old sentence).
- **Checks:** build 0 errors / 145 warnings · Unit 1434 + 4 RED of 1438 · DemoSeed 170 · Regression non-Docker 128.
- **The R-6 fix-up returned** (`0281d5d`, implementer, +37/−30): any failed call refuses, with the fixed sentence and the refusal log line, and the doc wording updated. **One change beyond the list:** a refused result now carries **no rule results**; before, it carried the successful rows, which the host's 422 body would have sent while saying no figure was produced. The four RED facts went green: Unit **1438**, build 0 errors / 145 warnings, CA2100 115.
- **Orchestrator `b5f29eb`:** `Payroll/Program.cs`'s audit-stamp comment names the R-6 refusal instead of the retired "total-rule-failure short-circuit" (comment only).
- **Review in flight:** the build check, 5α, Fable and Codex, on `master...s145/task-14511`.
- **Build (Orchestrator, at `b5f29eb`, unpiped):** 0 errors / 145 warnings · CA2100 115 · Unit 1438 · DemoSeed 170 · Regression non-Docker 128.
- **5α: PASS** (union scope, labels, no money or hours figure changed, C-T1 holds, the 400 logging is safe, `PlannerBypassGuardTests`' literal path is intact).
- **5a Fable: APPROVED-WITH-WARNINGS.** It recomputed every figure from the spec by hand, and all match: September 162.80 with no overtime; the closing week 3.00 + 1.80 dated 4 Oct; the leaver 1.20 + 2.00 dated 29 Sep; the starter's norm 14.80. It verified the R-6 refusal is complete and that each stub's reply matches the real response contract.
  - **WARNING 1:** five Docker files first execute in CI. Recorded; inspection says green.
  - **WARNING 2:** in the deferred spells case, a line can be dated on a NOT_EMPLOYED gap day and exported under the preceding segment. Written into ROADMAP's re-hire-spells design input and TASK-14524's B.0 inputs.
  - **WARNING 3:** a re-expressed flex-carry pin could pass vacuously. `Assert.NotEmpty` was sent to 14511-T.
  - **NOTEs:**
    - replay re-derives until 14513 (ruled);
    - a zero-call plan leaves `EvaluationModel` null, written into TASK-14513's brief;
    - an asymmetric absence filter, to get a comment;
    - the exact-three coupling;
    - stale class-doc wording, sent to 14511-T;
    - hoisting the history read into 14512's admission, written into TASK-14512's brief.
- **5a Codex: APPROVE-WITH-WARNINGS.**
  - **WARNING 1:** a context piece can inherit the caller's position. → the adjudicator.
  - **WARNING 2:** the timeline reply is validated for presence, not success: a complete 200 with a failed row or empty fingerprints passes. → a fix-up plus RED-first pins.
  - **NOTE:** the R-6 pins don't assert empty rule results; sent to 14511-T.
- **R-10, the adjudicator on Codex WARNING 1 (RULED, no owner question): the caller-position fallback is retired for every piece.**
  - *Plain language:* a blank position on an employee's dated record means "no position, base rules apply". It was never "use what the request says".
  - The fallback was an S33 cutover leftover (ADR-023 D1), already superseded by ADR-040 D4.
  - No money moves today: the seeded position overrides touch only the flex cap and the norm-period length. But the wrong position would be frozen into the calculation's record and fingerprint.
  - Fixing only the context piece would leave two rules inside one timeline.
  - **Owed:**
    - two RED-first pins in 14511-T (`ContextPiece_PromotedToDepartmentHead1Sep2026_…`, `InMonthPieces_DatedRowHasNoPosition_…`);
    - the implementer deletes the fallback;
    - mutation **MU-45**;
    - at merge, an ADR-023 D1 amendment line.
  - **Declined, to TASK-14513's brief:** the correction event records the caller's position.
- **The Step 5a batch was dispatched,** test-qa first (RED-first), then the implementer: R-10, Codex WARNING 2's validation, and Fable's comment notes.
- **14511-T's batch returned** (`1c4ab78`, on top of a docs-only master merge `add9b5b`). It added five RED-first pins: the two R-10 position pins (the 31 Aug piece carries `DEPARTMENT_HEAD`) and three malformed-reply pins (a failed row, a failed flex block, empty fingerprints; each still calculates as Success). It also added `Assert.Empty(RuleResults)` to both R-6 pins and `Assert.NotEmpty` to both flex-carry facts, reworded the class doc, and reworded the marquee comments. Unit was 1438 + 5 RED of 1443. The implementer's fix-up 2 was then dispatched.

## A.3 gate — closed 2026-10-14

*Plain language:* **QUAL-186 is fixed.** Payroll judges each month week by week through one Rule Engine call, with the previous month's opening-week days as context.
- A normal 37-hour September now exports no overtime and no flex payout. Before, it exported about 126 h of overtime and a 25.8 h flex payout.
- A week crossing a month boundary is paid by the month holding its Sunday.
- A leaver's last week is settled on their last day.

Along the way, two silent-failure paths were closed:
- **a failed engine call now refuses the month** instead of exporting it incomplete;
- **an employee's dated record is the only source of their position** in the calculation.

**The checklist** (`PLAN-s145.md`, wave table row A.3):
- **Merged:** 14511+T `2d14003`.
- **O-1:** 0 errors / 145 warnings · CA2100 115 · Unit 1443 · DemoSeed 170 · Regression non-Docker 128.
- **OpenAPI:** no drift.
- **Records at the merge:**
  - ADR-023 D1 amended (R-10);
  - QUAL-150 note (the export key's position until A4);
  - SEC-039 sentence (R-5);
  - QUAL-189 addendum (R-7, earlier);
  - TASK-14512 brief: A4 includes the position; the history read is hoisted into admission; Do 7 closes the race;
  - TASK-14513 brief: the zero-call plan, the correction event's position, no re-derivation;
  - TASK-14514 brief: MU-40 re-anchored; MU-44 and MU-45 added;
  - TASK-14524 B.0 inputs: the per-segment split rows, the spells refusal.
- **Watch items for the Stage A gate's CI run (CA-4):**
  - five Docker files first execute there;
  - a fixture whose profile row starts on the 1st while its window covers the opening week will refuse with `employment_record_gap`;
  - a caller position no longer reaches the engine, so stored fingerprints differ from pre-R-10 runs. No literal fingerprint is pinned.

## Wave A.4 (dispatched 2026-10-14)

**At TASK-14512's return (calculability checks, the dated key, Q7; not yet merged).**

*Plain language:*
1. **An agreement change in the middle of a week** (for example HK→AC on Tue 1 Sep, or on 29 Sep inside October's first week) refuses the month with a 422 on all four routes: "a week cannot be judged under two agreements". A new hire's first row is not a change, and a Monday change is fine.
2. **Months in order.** October is calculated only once September is APPROVED, if October's first week holds September days the employee worked. Employee-signed or no row (a raw-exported month) is not enough, and the answer is a 409 naming September. June 2026 and hires on the 1st are exempt; the raw routes are unaffected.
3. **Replay never runs these checks** and never consults the approval reader.
4. **The export's wage-type key comes from the employee's dated record,** OK version, agreement and position alike, using the same lookup as the engine. The two can no longer disagree.
5. **Recalculating a month that does not end on a Sunday** while the next is exported names the next month in the response and the audit row.

The refusal order is the planner's 422 → the week-check 422 → the prior-month 409 → the rules, and no refusal writes anything.

The detail:
- **Branch** `s145/task-14512` at `bd63dd1`: twelve files, all authorized.
- **Checks:** build 0 errors / 145 warnings · CA2100 115 · Unit 1427/1443 (16 expected "no approval reader" reds, cleared by 14512-T) · DemoSeed 170 · Regression non-Docker 128. In a throwaway run with the check satisfied, Unit was 1443/1443: the dated key and the restructure change no Unit-visible value.
- **Deviations (accepted):**
  - A4 resolves the dated record once, in planning, at every possible segment start, and evaluation reuses it. That is what guarantees engine and export agree.
  - `BuildPlanForLegacyCallersAsync` keeps its name, because tests reach it by reflection.
  - Do 7 uses `FOR SHARE`, since the export only reads the row.
- **Do 7, the reopen-vs-export race, is closed.**
  - Inside its transaction the export takes the previous month's row lock (`FOR SHARE`) before the current month's (`FOR UPDATE`) and rechecks APPROVED.
  - If the reopen commits first, the export answers the 409. If the export commits first, the reopen sees the export record and writes the worklist row.
  - There is no deadlock: the reopen takes one approval row after its advisory locks.
  - The implementer says a deterministic Docker pin is feasible, so it was added to 14512-T's scope as `PriorMonthExportRaceTests`.
- **Ruled by the Orchestrator (consistent with adjudicator R-3 at 14511): the replay interim.** Replay skips admission and never consults the approval reader. It still reads the window, the agreement history, the hours **and the dated profiles** (the last added at Step 5a, Fable WARNING 1) through `ReadReplayInputsUntilFrozenAsync`, since nothing frozen exists until TASK-14513. That task's brief now says it replaces that method; R42's sentinels (14515) prove it.
- **For 14514's re-spawn 1:** its replay pin calls `ReplayAsync(Guid)`, which evaluates nothing until 14513, so it is to be retargeted (written into its brief). The other 9 of 14514's Unit pins passed against 14512 as built.
- **14512-T was dispatched** with `MonthEndCrossingTests` added to its list, the race pin, and the H12–H14 tightening to the as-built Q7 member names.
- **Owed at the merge** (Orchestrator):
  - narrow 14509's comment in `ApprovalEndpoints.cs:1770-1774` now that Do 7 landed (comment only);
  - `WtmNaturalKey.cs:8-11` still says "once per plan" (doc only);
  - an ADR-020 D1 component 5 / D1.5 amendment (the dated key);
  - an audit-projection catalog row (two new detail keys, `NextPeriodToRecalculateStart/End`);
  - QUAL-150's planner half closed.
- **Watch items for CA-4:**
  - a Docker fact whose request body has a stale OK version, agreement or position now maps under the dated values;
  - the raw-route guard now makes one profile-resolver read per month;
  - ~~under R43 as specified, an agreement row dated the day after a leaver's last day still counts as mid-week~~. *Corrected at Step 5a (Fable NOTE 1):* not reachable through the production resolver. The month after the leave gets an empty window list, so R43 drops every date. It is reachable only windowless.

**At TASK-14512-T's return** (`d307220` on `s145/task-14512`).
- **The R26 rule is applied** per file: a fake `AlwaysApprovedPriorPeriods` in both test fixtures (the unit `EmploymentWindowPcsFixture` and the Regression `TestFixtures.BuildPcs` default) and at every PCS construction site, with the R26 comment. The smoke test uses the real adapter with February 2026 seeded APPROVED (March 2026 starts on a Sunday). `PayrollHostRecalcBlockedTests` needs no edit: its only positive calculating fact is June 2026, which starts on a Monday.
- **H12–H14 are tightened** to the as-built Q7 member names (`nextPeriodToRecalculateStart/End`, present on H12, absent or null on H13 and H14). They live in `PayrollHostWeeklyTests.cs`, not the file the brief named, a brief contradiction.
- **A new Docker race pin,** `PriorMonthExportRaceTests.Export_PriorMonthReopenedWhileExportWaits_RefusedPriorMonthNotApproved_NothingWritten`.
  - How it works: it holds September's row `FOR UPDATE`, starts October's export, and waits on a condition (`pg_stat_activity` showing the export's `FOR SHARE` lock wait, bounded at 30 s, no sleeps). Then it reopens September and expects `PriorMonthNotApprovedException` with no export record, event, outbox envelope or audit row.
  - Its red conditions: without the recheck or the lock, the export never blocks; without the status comparison, `ThrowsAsync` trips.
- **No assertion value changed.** Build 0 errors / 145 warnings · Unit 1443 (the 16 reds green) · DemoSeed 170 · Regression non-Docker 128.
- **Review in flight:** the build check, then Fable with 5α, and Codex.
- **Build (Orchestrator, at `d307220`, unpiped):** 0 errors / 145 warnings · CA2100 115 · Unit 1443 · DemoSeed 170 · Regression non-Docker 128.
- **5a Fable: APPROVED-WITH-WARNINGS; 5α: PASS.** It verified every load-bearing claim against the code:
  - the week check's weeks;
  - R43 with real windows;
  - all four routes' bodies;
  - the prior-month order and nothing written;
  - replay never admitting;
  - the A4 candidate starts matching every boundary source;
  - Q7's names;
  - Do 7's lock order against the reopen handler's actual statements;
  - the R26 rule per file.
  
  The findings:
  - **WARNING 1:** the replay-interim record undercounted; replay also reads the dated profiles. Corrected above, and written into TASK-14513's brief: `SegmentProfiles` come from frozen pieces, never `_profileResolver`. TASK-14515's brief now has a profile-resolver sentinel note.
  - **NOTE 1:** the leaver End+1 watch item is unreachable in production. Corrected above.
  - **NOTE 2:** `/recalculate`'s prior-month check is not under the Do 7 lock. It is bounded, because the reopen hook still writes M's worklist row. Registered as **QUAL-201**.
  - **NOTE 3 and NOTE 4:** the race test's faulted-export diagnostic and its busy loop. Sent to 14512-T.
  - **NOTE 5:** the prior-month lookup assumes monthly approval periods. A line is owed in the ADR amendment at merge.
  - **NOTE 6:** the owed comments are confirmed.
- **5a Codex: APPROVE-WITH-WARNINGS.**
  - **WARNING:** the race pin supplies `PriorMonthToRecheck` directly, so dropping the host's assignment would reopen the race unseen. Sent to 14512-T: an endpoint-level race fact, `CalculateAndExport_PriorMonthReopenedWhileExportWaits_Returns409_CalculationKept_NoExportRecord`, with the service fact's claim narrowed.
  - **NOTE:** a race-refused export keeps the calculation's manifest and its legacy calculation-completed event, as with B2. The Orchestrator qualifies "refusals write nothing" to *admission* refusals (comment), and the endpoint fact pins the retained artifacts.
  - **NOTE:** the reopen comment is owed at merge.
- **At TASK-14512-T's Step 5a batch** (`20627b1`):
  - an **endpoint-level race fact**, `PriorMonthExportRaceHostTests.CalculateAndExport_PriorMonthReopenedWhileExportWaits_Returns409_CalculationKept_NoExportRecord`, through `PayrollHostFactory`. It asserts the 409, nothing exported, and the retained manifest plus the legacy event, so it fails if the host stops setting `PriorMonthToRecheck`;
  - the service fact's claim is narrowed;
  - the faulted-task diagnostic, and the `Task.Delay(25)` polls.
  - **Orchestrator `b6ef74e`:** "admission refusals write nothing", noting what the export race recheck keeps.
- **Cycle 2: both lenses APPROVED, 5α PASS.** Fable confirmed the endpoint fact reaches the export for real and goes red for the right reason. Its wording NOTEs were applied in `6b62635`.
- **Merged `250fcdf`.** Post-merge comment fixes on master in `398a002`.

**At TASK-14514's re-spawn 1 return; merged `49c00e5`.**

*Plain language:* these are the tests written up front to define "done" for waves A.3 and A.4. They describe, in hours, amounts and dates, what a September, an October, a leaver, a starter, an OK-boundary week, a part-time change, a flex settlement and the dated wage-type key must produce. Re-spawn 1 only rebased them onto the merged code and matched three names; no expected figure changed. The ten Unit pins pass, and the fourteen Docker pins compile and run for the first time in CI.

- **Re-spawn 1** (`df1498f`): `recordedHoursReader` → `hoursReader`; `ProjectionHoursReader(_db, entriesRepo, absencesRepo)`; the replay pin is retargeted to `ReplayAsync(manifestId, Profile(Emp), previousFlexBalance: 0m)`. No STOP.
- **Orchestrator `8d21893`:** the branch build showed 146 warnings and CA2100 116, one over each cap. The new site was `WeeklyPayrollTests.ExecuteAsync`, a SQL helper whose two callers pass test constants. It is justified under the QUAL-073 ratchet, the same way as the other test helpers, and the counts are back to 145 / 115.
- **5α: PASS.**
- **5a Fable: APPROVED-WITH-WARNINGS.** It recomputed every figure by hand from the refinement and the rulings, and all match: WP1 3.00 + 1.80 dated 4 Oct; WP3 20.00 / 9.80 over 13 weeks; WP4 1.80 + 3.00 dated 30 Sep; WP5 3.00 dated 5 Apr; WP6/7 2.56 + 6.04 and 8.60; WP8 162.80; WP10 158.136986; WP11 5.00; WP12 the dated position. It walked each pin through the merged code and found no reason any Docker pin would be red on correct code.
  - **WARNING 1:** the 14 Docker pins first execute in CI. Recorded; on the CA-4 watch list (the A.3 precedent).
  - **NOTE 1:** WP4's and EE2's October halves are structural guards (a leaver's October makes no call). Already recorded at authoring; no change.
  - **NOTE 2:** `ExecuteAsync` does not assert rows affected. **Declined:** a missing row already reddens the fact, and an exact-count assert adds a harness-red risk to a CI-first file without strengthening it.
  - **NOTE 3, NOTE 4:** the stale class doc and WP9's reporting-day wording. Applied in `32f0d3e` (comments only).
  - **NOTE 5:** a culture-dependent `DoesNotContain` beside the ISO assertion that carries the pin. Cannot pass or fail wrongly; no change.
  - **NOTE 6:** the replay pin is the first Unit fact to notice a frozen-input gap. Written into TASK-14513's brief, and the pin is added to TASK-14513-T's call-site list (an 11th site, `PcsNeverSilentTests.cs:165`).
- **Merged `49c00e5`.** O-1 green, no drift.

## A.4 gate — closed 2026-10-14

*Plain language:* **payroll now refuses a month it cannot judge correctly, instead of guessing.** An agreement change in the middle of a week refuses the month with a 422 ("a week cannot be judged under two agreements"). A month whose opening week reaches into a previous month that is not APPROVED refuses with a 409 naming that month. Each part of a split month maps its wage lines by the employee's record on that part's first day (the dated key), including the position. The overlap where September could be reopened while October's export was running is closed. The tests that define "done" for A.3 and A.4 are merged, and a deliberate-breakage run proved each Unit test fails when the behaviour it guards is broken.

**The checklist** (`PLAN-s145.md`, wave table row A.4):
- **Merged:** 14512+T `250fcdf` (post-merge comments `398a002`); 14514 `49c00e5`.
- **O-1 on `49c00e5`:** 0 errors / 145 warnings · CA2100 115 · Unit **1453** · DemoSeed 170 · Regression non-Docker 128.
- **OpenAPI:** no drift.
- **14514 re-spawn 2, the Unit mutation run:** 14 of 14 tripped their named pins, with no finding (table below).
- **Records at the merges:** ADR-020 D1 amended (the dated key per segment); the audit-projection catalog row; QUAL-150 planner half CLOSED; QUAL-201 registered; TASK-14513's brief (the Unit replay pin) and TASK-14513-T's brief (an 11th `.ReplayAsync(` call site, `PcsNeverSilentTests.cs:165`).
- **Watch items for the Stage A gate's CI run (CA-4), added at A.4:**
  - 14514's two Docker files (WP1–WP12, EE1–EE2) and 14512-T's race facts first execute there;
  - WP5 and WP12 change `wage_type_mappings` rows inside their own fixture database; a shared-database harness change would make them interfere.

**The Unit mutation table (TASK-14514 re-spawn 2).** Each mutation was applied alone in a detached throwaway checkout at `49c00e5`, outside the repository, then restored to a clean status before the next. The baseline was green first: the 8 pin classes, 34 of 34. Every run exited 1 with the named pin failing, read from an unpiped `dotnet test`. At the end the checkout was deleted and pruned (`git worktree remove` hit "Filename too long" on the scratchpad path, so the directory was removed by its extended path, then pruned), and `src/**` on master is untouched. "PCS" is `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs`.

| Id | Mutation as applied | Captured failure |
|---|---|---|
| MU-25 | `PeriodPlanner.cs:198`: the key hydrated with the first range's start for every segment | `Plan_TwoSegments_EachCarriesOwnDatedWtmKey`: `Assert.Equal`, expected OK26 / SENIOR, actual OK24 / CLERK |
| MU-26 | `PayrollPlanRefusalProblem.cs`: the week variant passes no `reason` | `WeekVariant_HasReasonAgreementChangeMidWeek_NoRuleId`: `reason` key absent |
| MU-27 | `PayrollPlanRefusalProblem.cs`: the change date appended to `error` | `WeekVariant_SerializedJson_HasNoDateNoEmployeeId`: `Assert.DoesNotMatch` on a `yyyy-MM-dd` pattern |
| MU-28 | PCS `EnsurePriorMonthApprovedAsync`: a null reader skips the check | `NoApprovalReader_OctoberWithEmployedSeptemberDays_Throws_NamesRegistration`: no exception thrown |
| MU-29 | the same method: throw whenever the reader is null, before applicability | `NoApprovalReader_June2026_Succeeds`: `InvalidOperationException` |
| MU-30 | PCS `RequireHoursReader`: a null reader becomes an empty one | `NoHoursReader_Calculation_Throws_NamesRegistration`: no exception thrown |
| MU-31 | `WeeklyTimelineCore.cs:206`: day norm `weekly × fraction / 5`, unrounded | `Fraction0811_RuleWeekNorm3000_EqualsSumOfDailyNormCalculator`: expected 30.00, actual 30.0070 |
| MU-32 | injected `Services/MutBypass.cs` posting to `/api/rules/evaluate-timeline` | `NoIntegrationFile_OtherThanAllowlist_PostsToApiRulesEvaluateTimeline`: the guard names the file |
| MU-36 | PCS `:711`: context pieces dropped (the timeline starts at `periodStart`) | `ContextPiece_PromotedToDepartmentHead…`, `LeaverPlan_EmployedEvaluationsSucceed_…`: first piece expected 31 Aug, actual 1 Sep (also `FlexCarry_…`) |
| MU-38 | PCS `ReplayAsync`: throw on a null approval reader | `NoApprovalReader_Replay_Succeeds`: `InvalidOperationException` |
| MU-39 | injected `Services/MutRepo.cs` naming `TimeEntryProjectionRepository` | `OnlyProjectionHoursReader_NamesTheProjectionRepositories_UnderIntegrations`: the guard names the file |
| MU-40 | PCS `:738`: a NOT_EMPLOYED segment adds a piece | `Segment0NotEmployed_…`, `Shim_PreHireSegment_…` (expected 10 Mar, actual 1 Mar), `LeaverPlan_…` (expected 15 Mar, actual 31 Mar) |
| MU-43 | PCS after `Normalize`: throw when a normalized entry falls after `PeriodEnd` (the retired guard) | `MonthEndNightShift_Wed30Sep2300To0200_…` and its January 2027 twin: the restored throw |
| MU-44 | the refusal gate: `callsAttempted > 0 && callsFailed >= callsAttempted` | both R-6 pins: `Assert.False`, actual True |
| MU-45 | `TryResolveDatedProfileAsync`: a null position replaced by the caller's | `ContextPiece_PromotedToDepartmentHead…` (`Assert.Null`, actual DEPARTMENT_HEAD) and `InMonthPieces_…` (2 of 2) |

*Brief contradictions, accepted:* MU-31's site is the rule-side `WeeklyTimelineCore` (the pin's own docstring names it; the screen-side calculator only yields per-day values). MU-45's site is `TryResolveDatedProfileAsync`, which `ResolveProfileAtAsync` and `SegmentProfileAsync` both call since R-10. Four trips are exceptions inside the test rather than an `Assert` line (MU-26, MU-29, MU-38, MU-43); each fails on the behaviour it guards.

## Wave A.5 (dispatched 2026-10-14)

**At TASK-14513's return (the replay contract; not yet merged; ruled R-11..R-16; 14513-T dispatched).**

*Plain language:* a payroll figure must be provable later. Every successful calculation now stores what it ran on:
- **the data:** the employment pieces, the leaver's cutoff, the opening flex balance, the event ids of the hours rows it read, and a lossless fingerprint (digest) of those rows exactly as read;
- **the code:** the Rule Engine's config fingerprints and its arithmetic marker.

A replay takes only the manifest id. It re-runs from those stored inputs, re-reads the hours rows by id (a row the employee later re-saved is still found), and refuses by name if anything differs. It never looks at today's approval state, employment window, profile or agreement history.

- **Commit `21fa447`** (`payroll-integration`; PCS plus a new `HoursDigest.cs`). Every `src/**` project builds with 0 errors; warnings unchanged (145, CA2100 115). The 12 test call-site errors are expected; they belong to 14513-T.
- **The named refusals:** `replay-manifest-without-frozen-inputs`, `replay-entries-missing`, `replay-entries-digest-mismatch`, `replay-absences-digest-mismatch`, `replay-evaluation-model-changed`, `replay-config-changed` (a new `ReplayRefusedException` carrying the reason).
- **A zero-call plan** (no employed segment, so no engine call) freezes no fingerprints and a dedicated marker, `no-engine-call`, rather than a copy of the engine's own marker, which payroll would otherwise have to hard-code across the context boundary.
- **QUAL-197:** the absence digest line has no `Feriedage`, so no STOP; `superseded_by_outbox_id` is in neither line.
- **`plan_inputs_jsonb`** is filled from the event's own serialized `planInputs`, so the rebuilder's copy is identical by construction (R47).
- **Local evidence:** the agent ran the tests in a throwaway checkout with compile-only retargets (deleted, nothing committed). Unit 1469 (1453, 14515's 4 `HoursDigestTests`, and 12 in-memory probes of every refusal), Regression non-Docker 128, DemoSeed 170. Two sanity mutations each reddened only their own probes. 14515's six files compile against the branch once its two pre-14508 names are reconciled (its re-spawn 1).
- **Brief contradictions:** line numbers predate A.2 (re-anchored in the report); `CalculatePlanAsync` does not exist (the shared entry is `EvaluateAsync`); the correction event's position needs `RetroactiveCorrectionService.cs`, which the ledger does not authorize.
- **Sent to the adjudicator:**
  - the correction event's position and agreement code;
  - the result-level `AgreementCode`, which is not frozen;
  - the digest line form's `|` ambiguity on free-text `ActivityType`;
  - ten declared interpretations.
- **Orchestrator `9984173`:** `ProjectionHoursReader.cs`'s doc says the binding has landed (comment only).
- **The adjudicator on 14513's return: R-11..R-16, all RULED, no owner question.** One principle runs through R-11 and R-12: **the result and the audit trail report the agreement and position the month was evaluated under, never what the request body said.** It is R-10's rule, applied to the two remaining labels.
  - **R-11, the correction event records what ran.** A correction's audit event wrote the request body's agreement and position, under a comment promising "what actually ran". Since R-10 and A4 the calculation ignores those body fields, so a stale body could label an AC month "HK" in the audit log. Both fields now come from the frozen first EMPLOYED piece. `RetroactiveCorrectionService.cs` is authorized for 14513 (a scope extension); a missing `PlanInputs` after success throws, never falling back to the caller. The audit mapper already copies the event's agreement, so the fixed event fixes the row. Pin (14513-T, Docker, RED-first): `RetroactiveCorrectionTrailTests.Recalculate_RequestProfileDiffersFromDatedRecord_EventRecordsDatedAgreementAndPosition`.
  - **R-12, the result's `AgreementCode` is the agreement the month was paid under.** It echoed the request body on a forward calculation but reported the frozen piece on replay, so "replay reproduces the result byte for byte" held only by luck.
    - *Rejected:* freezing the caller's value, which would faithfully reproduce a wrong label; and accepting the mismatch, a latent flake in the 14515 pins, which compare the whole result.
    - *Ruled:* forward, refusal and replay share one derivation, the first EMPLOYED piece's agreement; `EvaluateAsync` stops reading the caller's agreement and position.
    - *The field reaches* only the `/calculate-and-export` 200 and 422 bodies: no export column, no frontend use, no OpenAPI change.
    - *Information for the owner, no question:* the calculating route's `agreementCode` now names the agreement the month was actually paid under, not the one the request said.
    - *Pins* (14513-T, Unit, RED-first): `ForwardCalc_RequestAgreementDiffersFromDatedRecord_ResultReportsDatedAgreement`, plus an empty-agreement assert on the no-EMPLOYED fact.
  - **R-13, typed text in the hours digest is hex-encoded.** The digest proves the hours rows behind a payslip are unchanged. A `|` or a newline typed into the free-text activity field could make two different row lists hash alike; the adjudicator built such a pair.
    - *Rejected:* validating the field at registration, because Payroll's proof would then rest on another context's input checks, and it would not cover existing rows.
    - *Ruled:* `ActivityType` and `AbsenceType` are written as the hex of their UTF-8 bytes, so the line form is unambiguous by construction. Refinement A7's line forms are amended. 14515's digest facts are property facts, so nothing is re-expected.
    - *Pin* (14513-T, Unit, RED-first): `HoursDigestTests.ActivityTypeContainingSeparatorAndNewline_CannotMergeTwoRows`, in a new file that 14515 re-spawn 1 then adds its facts to.
  - **R-14, the ten declared interpretations are confirmed, with one correction.** `ReplayRefusedException` moves to its own file (the one-file-per-exception convention). The claim that the replay profile's empty `EmploymentCategory` and null `OrgId` are read by nothing was verified against the Rule Engine and Payroll. Confirmed as declared:
    - an absent absence id refuses as entries-missing;
    - a damaged slot refuses as without-frozen-inputs;
    - a failed engine call at replay gets the R-6 shape;
    - the marker is checked before the fingerprints;
    - a NULL slot does not fall back to the event;
    - `AsOfOutboxId` is recorded but not verified, because a rebuild re-stamps it;
    - a zero-call plan freezes the `no-engine-call` marker;
    - the brief's `CalculatePlanAsync` means `EvaluateAsync`.
  - **R-15:** the stale `ProjectionHoursReader` comment, done in `9984173`.
  - **R-16:** amended:
    - TASK-14513's brief (the fix-up's contract);
    - TASK-14513-T's brief (the three pins, the `BoundaryCauseEncodingTests` relabel);
    - TASK-14515's brief (adds its facts to the existing `HoursDigestTests.cs`; checks per-segment fingerprint coverage);
    - the plan's ledger rows;
    - refinement A7's line forms.
  - **Registered from the adjudicator's NOTEs:**
    - **QUAL-202:** activity type is free text nothing validates, so a typo is silently paid as normal hours. Medium.
    - **SEC-048:** the payroll routes' audit row takes its target org from the request body. Low; a new Group 12 in the security register.
    - *Not registered at the return:* an unparseable `plan_inputs_jsonb` falls back to the event. **Reversed at Step 5a cycle 1 on Codex BLOCKER 2 (R-17):** faithfulness was the wrong test; consistency with R-14(vii) is the right one. The per-segment lane's fingerprint coverage is a check item for 14515 re-spawn 1.
- **Order from here:**
  1. 14513-T, the paired pass: the 12 call sites and the three RED-first pins. Dispatched.
  2. The `payroll-integration` fix-up: R-11, R-12, R-13 and R-14(ii).
  3. 5α, then Fable and Codex (high-risk).
- **At TASK-14513-T's return** (`e5e05cb`):
  - **The call sites.** All 12 moved to `ReplayAsync(manifestId)`. No new seeding was needed: the Docker facts already seed hours through the in-memory reader, which re-reads by id, and the marquee stub already reads the fraction from the request's pieces. The marquee docstrings now describe pins of the freeze.
  - **The labelled changes.** The three hand-built-manifest facts in `BoundaryCauseEncodingTests` now expect `replay-manifest-without-frozen-inputs` (labelled `// S145 R14:`).
  - **The RED-first pins.** Four Unit pins were observed RED as intended:
    - R-12: `Expected "AC", Actual "HK"`, and the empty-agreement assert;
    - R-13: both separator facts, `Strings are equal`.

    R-11's Docker trail test argues its RED in the docstring.
  - **Checks:** 0 errors / 145 / 115 · Unit 1452 + 4 RED of 1456 · DemoSeed 170 · Regression non-Docker 128.
- **At the fix-up's return** (`b25bf24`, `payroll-integration`):
  - **R-11:** the correction service calls `CalculateWithOutcomeAsync`, and a success without frozen inputs throws before its transaction opens. The event's agreement and position come from the frozen first EMPLOYED piece.
  - **R-12:** `EvaluateAsync` takes the employee id, and `ReplayResultProfile` is deleted. One `evaluatedAgreementCode` feeds the success result, the refusal and the legacy event.
  - **R-13:** a `TypedText` helper hex-encodes the two types.
  - **R-14(ii):** the exception has its own file.
  - **Checks:** the four pins went green. Unit **1456** · DemoSeed 170 · Regression non-Docker 128 · 0 errors / 145 / 115.
  - **Reported, not changed:** (A) local-profile planning still keys on the request's agreement and OK version; (B) a correction for a month with no EMPLOYED segment records `Guid.Empty` as its manifest id. Both are sent to the review lenses.
- **Build (Orchestrator, at `b25bf24`, unpiped):** 0 errors / 145 warnings · CA2100 115 · Unit 1456 · DemoSeed 170 · Regression non-Docker 128.
- **5α: PASS.** Each commit stays in its agent's authorized files (14 files, exactly the authorized set). No generated file is touched, the digest is culture-invariant, and the new SQL is parameterized constants. One observation: a fourth `// S145 R14:` change in `BoundaryCauseEncodingTests`, beyond the three named. The live-write fact now asserts each rule result's manifest id, because the result no longer carries one; the meaning is unchanged.
- **5a Fable: APPROVED-WITH-WARNINGS.**
  - **What it verified:** it traced every input evaluation reads, and each is frozen, fingerprinted, or a pure function of a frozen value. So a replay of an untouched manifest reproduces the forward result. Each refusal fires for its own cause only, in the ruled order, and writes nothing. The five live sources sit only in planning and admission code, which replay never enters. 14515's 13 replay facts are consistent with the implementation.
  - **WARNING 1:** a zero-EMPLOYED correction records `Guid.Empty` as its manifest id. Fix now: `outcome.ManifestId`.
  - **WARNING 2:** planning's local-agreement-profile lookup keys on the request body. A forward-planning residual; Fable recommends registering it (QUAL, Medium).
  - **NOTEs:**
    - the marker covers the per-segment rule arithmetic only by convention; the ADR-016 amendment should say so;
    - the event-fallback load of a populated `planInputs` has no pin;
    - `HoursDigestTests` pins the separator collision but not field coverage;
    - the R-12 pin should assert the pieces' agreement.
- **5a Codex: REQUEST-CHANGES.**
  - **BLOCKER 1:** a projection slot missing `openingFlexBalance` deserializes as 0 and passes the completeness check, so a replay could evaluate the wrong balance with every digest still matching. The same holds for missing piece fields.
  - **BLOCKER 2:** a found projection row with malformed inputs falls back to the event through the old catch-all, while a NULL slot refuses.
  - **BLOCKER 3:** the zero-EMPLOYED correction's empty manifest id, with a reachable scenario: a month exported with lines, whose employment dates are later corrected so nothing is EMPLOYED.
  - **WARNING:** the planning lookup, as Fable's WARNING 2.
- **Orchestrator disposition:**
  - Codex B3 / Fable W1, and Codex B1 (R-14(iv) not fully delivered): fix-up cycle 2, with RED-first pins.
  - Codex B2: it contradicts the adjudicator's declined NOTE at the return. Sent to the adjudicator.
  - The planning lookup: the lenses differ on now or later. Sent to the adjudicator.
- **The adjudicator on the cycle-1 findings: R-17..R-25, all RULED, no owner question.** Two facts frame them:
  - payroll's freeze never writes a null list or an empty digest, so the only members that may legitimately be absent are the two nullable ones (the cutoff and a piece's position);
  - the Rule Engine applies no local agreement profile, so a local profile can split a month but cannot change a figure.

  The rulings:
  - **R-17, a found projection row with undecodable stored inputs refuses by name (it upholds Codex B2 and reverses the adjudicator's return NOTE).** *Why the reversal:* "the event is the record of truth, so the replay stays faithful" was the wrong test. R-14(vii) already refuses a row whose slot is NULL, and a row holding garbage is more damaged than a NULL one. Refusing the lesser damage while quietly succeeding on the greater is incoherent, and it hides a damaged projection from the operator who should rebuild it. Nothing is lost by refusing: the replay is one projection rebuild away, and the refusal says so.
    - The catch-all still covers "the projection could not be consulted" and "no row"; both fall back to the event.
    - A damaged `segments_jsonb` fails as a damaged row, not as an event substitution.
  - **R-18, a missing required member refuses.** `[JsonRequired]` goes on every non-nullable member of `PlanInputs` and `FrozenPiece`, but not on the cutoff or the position. `PlanInputs.cs` (SharedKernel) is authorized as a scope extension. A Unit pin proves the serializer honours the attribute.
  - **R-19, the correction event links its manifest:** `outcome.ManifestId`. Codex's scenario is real and R-11's note had missed it.
  - **R-20, the local-profile activation key is fixed in Stage A, as its own task:** TASK-14519, then 14519-T, in wave A.5 after 14513 merges and before Gate A-CLOSE.
    - *Why not in this fix-up:* it lies outside the replay contract, changes no paid figure, and would widen a high-risk branch already in review.
    - *Why not later:* it is a hole in A4's claim ("each month's codes come from the dated record"), which Gate A-CLOSE checks, and the owner prefers the complete fix.
    - *The key:* per (agreement, OK-version) interval on the dated record, including its `OrgId`, which the resolver fills from the live `users.primary_org_id` (organisation history is ADR-040's named follow-up, QUAL-206): dated in agreement and OK version, live in organisation (R-30). "The record at `periodStart`" alone would miss an interior agreement change or OK transition.
    - Registered as QUAL-203; SEC-048 notes the same dated `OrgId` as its fix source.
  - **R-21:** at close, ADR-016 D10 states that the model marker names the arithmetic of every rule endpoint payroll calls, and the marker's doc says the same.
  - **R-22:** the event-fallback load of a populated `planInputs` is pinned in this cycle, as a guard beside R-17's refusals.
  - **R-23:** a digest field-coverage Theory is added in this cycle. 14515's facts do not cover the typed fields, so the Orchestrator's premise was wrong.
  - **R-24:** the R-12 pin asserts the pieces' agreement; the fixture learns the field.
  - **R-25:** amended:
    - TASK-14513's brief (the cycle-2 fix-up);
    - TASK-14513-T's (the pins);
    - TASK-14515's;
    - the plan (TASK-14519 and 14519-T rows, and the A.5 wave row);
    - TASK-14518's (CA-1 must classify the post-draft-5 Docker pins);
    - QUAL-203, and QUAL-204 (the product question, below);
    - SEC-048.
  - **Registered from the declined findings: QUAL-204.** A local agreement profile never reaches payroll's arithmetic, although ADR-017 reasons as if it shapes pay. It is an open product question, "should a local profile change pay?", not a defect.
- **Order from here:**
  1. 14513-T cycle 2, the pins (dispatched).
  2. The `payroll-integration` cycle-2 fix-up (R-17, R-18, R-19).
  3. 5a cycle 2, both lenses.
  4. The merge and O-1.
  5. Then 14515 re-spawn 1, and 14519 with 14519-T.
- **At 14513-T cycle 2's return** (`41fd80c`, on a docs-only merge of master, `713ccb7`):
  - **New Docker file `FrozenInputsSlotTests.cs`:**
    - R-17: a wrong-shape slot refuses despite an intact event;
    - R-18: a slot missing the opening balance refuses;
    - R-22: the event-fallback guard;
    - the optional damaged-segments guard.
  - **New Unit file `PlanInputsRequiredMembersTests.cs`:** three RED facts (a missing opening balance, a missing piece fraction, and the same through `EventSerializer`), plus three guards.
  - **R-19:** a second fact in `RetroactiveCorrectionTrailTests`.
  - **R-23:** two field-coverage Theories in `HoursDigestTests` (7 entry cases and 4 absence cases).
  - **R-24:** `TimelinePiece.AgreementCode` in the fixture, plus the assert.
  - **The R-17 comment edits** in `BoundaryCauseEncodingTests`.
  - **Checks:** Unit 1470 + 3 RED of 1473 · DemoSeed 170 · Regression non-Docker 128 · 0 errors / 145 / 115.
- **At the cycle-2 fix-up's return** (`cb8d5e0`, on a second docs-only merge, `6f1055e`):
  - **R-17:** `LoadManifestAsync`'s catch-all now wraps only the SQL read. A found row is decoded afterwards: undecodable segments throw `InvalidOperationException`, and an undecodable slot is logged at Warning and refused by name.
  - **R-18:** `[JsonRequired]` on the nine `PlanInputs` and four `FrozenPiece` members. System.Text.Json honours it on net8.0 through both read paths, so no fallback check was needed.
  - **R-19:** `outcome.ManifestId`.
  - **Checks:** the three RED pins went green. Unit **1473** · DemoSeed 170 · Regression non-Docker 128 · 0 errors / 145 / 115.
  - **Residuals it reported:** a null reaching a required member at freeze would write but not read back (no source today can be null); the R-19 link when both manifest writes fail (pre-existing); both slots damaged fail as a damaged row; a JSON `null` in `segments_jsonb` still becomes an empty list; the catch-all still catches cancellation.
- **Build (Orchestrator, at `cb8d5e0`, unpiped):** 0 errors / 145 warnings · CA2100 115 · Unit 1473 · DemoSeed 170 · Regression non-Docker 128.
- **5α cycle 2: PASS.** Each commit stays in its authorized files; `PlanInputs.cs` gains only BCL serialization attributes and SharedKernel no new reference; the new decode-failure log carries the manifest id and the exception only, never the row's contents.
- **5a Codex cycle 2: APPROVE.** B1, B2 and B3 are closed; no new finding. The local-profile warning stays with TASK-14519.
- **5a Fable cycle 2: APPROVED.** Each cycle-1 finding is closed as ruled, with no regression to the forward path, RP9, the rebuild round trip, the event fallback or the three refusals. The new pins are non-vacuous. Of the fix-up's five residuals, three are not findings, and two are register items.
  - **NOTE 1:** the read's catch reset an already-read row to null, so a fault while closing the connection could still hand a found row to the event. **NOTE 2:** both load catches swallowed cancellation, so a cancelled replay read "manifest not found". **NOTE 4:** the R-22 guard's equality could compare two empty lists. All three applied by the Orchestrator in `702c894` (Small Tasks Exception: drop the reset, `when (ex is not OperationCanceledException)` on both catches, `Assert.NotEmpty`). Build and tests re-run green (145 / 115 · Unit 1473 · Regression 128); not re-reviewed.
  - **NOTE 3:** a correction ignores its calculation's `AuditState`, so when both manifest writes fail its event links a manifest that exists nowhere. It predates S145. Registered as **QUAL-205**, with the ruling question (refuse before the transaction?).
  - **NOTE 5:** the Unit pin carries a private copy of the reader's JSON options. `[JsonRequired]` does not depend on the options, so no change.
- **Orchestrator `fc7658b`:** the correction event's `AgreementCode` and `Position` docs say what they record (R-11 e; comments only).
- **Merged `07337ea`.** O-1 green (Unit 1473), no drift. Next: 14515 re-spawn 1 (rebase, reconcile names, its digest facts into the existing `HoursDigestTests.cs`) and TASK-14519 (the local-profile activation key), dispatched in parallel (no shared file).

**At TASK-14515's re-spawn 1 return (the replay pins; not yet merged).**

*Plain language:* these are the tests written up front to define "done" for the replay contract. Re-spawn 1 rebased them onto the merged code and matched the names; they now compile and run against it. The Unit ones pass, and the Docker ones run first in CI.

- **The commits.** The branch was rebased onto `e889661` (`2734bc4`); re-spawn 1 is `a156e9e`. The one conflict was `HoursDigestTests.cs`, which both sides had added; it was resolved by keeping master's file and appending this task's facts.
- **Names:** `hoursReader:`, and `new ProjectionHoursReader(harness.Factory, timeRepo, absenceRepo)`.
- **The digest facts.** The three facts (MU-33..35) and the QUAL-197 guard moved into the existing `HoursDigestTests.cs`.
  - The guard's expected line now writes `AbsenceType` as hex, per R-13. It is the one expected-text change, and it comes from the ruling, not from the code; the pinned property ("Feriedage is not in the line") is unchanged.
  - The test's spec helper hexes the activity type to match.
- **The profile-resolver sentinel joins R42's four,** making five. Replay never reaches `_profileResolver`, so the sentinel costs nothing and now backs the claim. The constructor's window-sentinel requirement is met.
- **The per-segment fingerprint check is covered.** Each segment's agreement, OK version and position are in the timeline's resolved and fingerprinted set, because the segment's days belong to a frozen piece. This was a static trace, not a run.
- **CA2100:** the kit's `ExecAsync` helper took the count to 116; the QUAL-073 justification brought it back to 115.
- **Checks:** 0 errors / 145 / 115 · Unit **1477** (+4 appended digest facts) · DemoSeed 170 · Regression non-Docker 128.
- **Build (Orchestrator, at `a156e9e`, unpiped):** 0 errors / 145 warnings · CA2100 115 · Unit 1477 · DemoSeed 170 · Regression non-Docker 128.
- **5a Fable with 5α: APPROVED-WITH-WARNINGS; 5α PASS.** Every pin was read against the merged code:
  - each refusal name is asserted in the order the service raises it;
  - the byte-identity comparisons compare a result with no clock or fresh id, so they cannot pass by accident;
  - the "differs" comparisons strip only the manifest id, so they can fail;
  - the five-sentinel replay is legally constructed and proves "not consulted";
  - the digest facts hit the merged `HoursDigest`.
  - **WARNING 1:** three stale docstrings ("does not compile", "assumed names"). Fixed in `3fbc8d5`.
  - **NOTE 1:** the refusal helper found `Reason` by reflection with a message fallback. `3fbc8d5` makes it assert the typed `ReplayRefusedException`.
  - **NOTE 2:** MU-35's mutation must be applied inside `HoursDigest`. Written into re-spawn 2's prompt.
  - **NOTEs 3–5, recorded:** FA's segment start coincides with the period start; every replay fact's export lines are empty, so identity is proven over rule results only, as designed; and a latent decimal-scale hazard in the echo kit, worth a `Fmt` when it is next touched.
  - **NOTE 6:** per-segment fingerprint coverage confirmed.
- **Merged `eb78b8e`.** O-1 green: Unit 1477 · DemoSeed 170 · Regression 128 · 145 / 115 · no API drift. Re-spawn 2, the mutation run for MU-33..35, was dispatched.
- **14515 re-spawn 2, the Unit mutation run: 3 of 3 tripped, no finding.**
  - **Setup.** Each mutation was applied alone in a detached throwaway checkout at `eb78b8e`, on a short path (`C:/mt15`) to avoid the earlier "Filename too long" teardown. The checkout was removed and pruned at the end, and `src/**` on master is untouched.
  - **Baseline:** `HoursDigestTests` 17 of 17.

| Id | Mutation as applied | Captured failure |
|---|---|---|
| MU-33 | `HoursDigest.cs:149`: `Number()` rounds to four decimals | `HoursDifferingInFifthDecimal_HashDifferently`: the hashes differ |
| MU-34 | `HoursDigest.cs:132`: times written as `HH:mm:ss` | `TimesDifferingBy100ns_HashDifferently`: the hashes differ |
| MU-35 | `ComputeEntriesDigest`: rows split at midnight before hashing. A private `SplitAtMidnight` mirrors the normalizer, whose API takes `TimeEntry`, not the projection row (the prompt's allowed fallback) | `HashesRowsAsRead_BeforeNormalization`: the hashes differ; the other 16 `HoursDigestTests` stay green |

**At TASK-14519's return (the local-profile activation key, R-20; not yet merged).**

*Plain language:* when payroll plans a month, it asks whether the organisation switched on a local agreement profile partway through. The question is now asked per stretch of the month where the employee's agreement and OK version stay the same, using the employee's dated record. It is no longer asked once, using the request body. No paid figure changes (the Rule Engine applies no local profile), and replay is unaffected (it never plans).

- **Commit `c625445`** (`payroll-integration`; PCS only). Build 0 errors / 145 / 115 · Unit 1473 · DemoSeed 170 · Regression non-Docker 128.
- **How it works:**
  - The employee's history is now read first. The month is cut at interior agreement-code dates and OK transitions.
  - Each piece's key is the dated record's org and agreement, plus that day's OK version. When a piece's first day is not employed, the key is read at the first employment start inside it.
  - Consecutive pieces with the same key make one lookup, so a month with no key change makes one lookup, as before.
  - Dated resolutions are cached per plan, so no extra resolver read is made.
- **STOP and report: one existing Docker fact will flip.** `ProfileBoundaryHydrationTests.ProfileEffectiveFromInsidePeriod_ProducesBoundary` Variant A seeds an **OK24** profile for April 2026, an OK26 month, with a body saying OK24. That is exactly the stale case R-20 removes. The fixture is fixed in 14519-T: the seed becomes OK26 and the body stays OK24, so the fact also proves the body's OK version is ignored.
- **Brief contradictions:**
  - **(1)** The boundary detector ranks a local-profile activation **above** an agreement change on a shared date; the brief's "S144 R1" claim was wrong. The implementer kept the agreement change's label by bounding each run exclusively at its first day.
  - **(2)** The dated record's `OrgId` is the live `users.primary_org_id` joined onto the dated row, not a dated column. Organisation history is a named follow-up.
- **Declared deviations:** (a) the key is read at the first employment start in a piece; (b) same-key pieces are merged into one lookup.
- **Not named by the brief:**
  - ADR-017 D9c's `>=` against the end-exclusive repository;
  - the `EmploymentProfile.OrgId` doc comment;
  - in the wired service, the lookup now runs whenever the record has an org, where before it ran only when the request carried one.
- **In flight:** 14519-T (the three pins and the fixture fix), and the adjudicator on (1), the deviations, (2) and the doc drift. 14519 is reviewed with Codex as well as Fable: it changes payroll planning, which decides whether a month is refused (WORKFLOW Step 5a's high-risk override, payroll export).
- **The adjudicator on 14519's return: R-26..R-32, all RULED, no owner question; the code stands at `c625445`.**
  - **R-26, the same-date tie-break.** The brief's "the agreement cause wins (S144 R1)" was the adjudicator's own factual error. The detector processes local-profile activations before agreement changes, first write wins, and S144 R1 only ranked the agreement change above an employee-profile change.
    - The implementer's instruction was right on its own merits. A run's lower bound is exclusive, so the profile in force on a run's first day is its starting state. That is ADR-017 D9c's convention at the period start, and it keeps a real agreement change from hiding behind a same-day activation.
    - All four placements are verified: the period's first day; a key-changing cut; a same-code cut; a non-cut day.
  - **R-27, the detector's precedence does not change in this task.** From payroll the two causes can now meet only where the agreement code did not really change, and there the activation is the truer label. The latent hazard (another hydrator passing an activation on a real agreement change) is recorded as a QUAL-203 residual and a comment at merge, not fixed.
  - **R-28, deviation (a) confirmed.** The key is read at the first employment start in a piece. A mid-month starter has no record on the 1st, so reading there would miss a later activation, the very defect R-20 fixes. The read is free, because the segment-start resolution reads that day anyway.
  - **R-29, deviation (b) confirmed.** Same-key pieces are merged into one run. Without the merge, a re-dated same-code agreement row would drop an activation on that date that the old single lookup found. Consequence: on that date the manifest reads `LocalProfileActivation`.
  - **R-30, the organisation is live, not dated.** The resolver joins the live `users.primary_org_id` onto the dated row. The key is therefore dated in agreement and OK version, and live in organisation. Organisation history is already an owner-ruled follow-up (ADR-040 OQ-3 ii), so nothing is re-asked; **QUAL-206** registers the consumers that now rest on the live org. R-20's "including its `OrgId`" means the live org (corrected above).
  - **R-31, doc drift.** ADR-017 D9c's predicate is end-exclusive since ADR-018; that note goes in at close, with the D10 record. `EmploymentProfile.OrgId`'s doc is fixed at merge.
  - **R-32, the pins.** Pin 1 is strengthened (the body carries an unseeded `OrgId`), and three pins are added: an activation on the agreement-change date (R-26), a mid-month starter (R-28) and the merged run (R-29). 14519-T re-spawn 1 was dispatched for the delta.
- **TASK-14519, Step 5a cycle 1:**
  - **Fable (with 5α):** stopped at the usage limit before its verdict; it is re-run below, on the fixed head.
  - **Codex: REQUEST-CHANGES.** It found the production change consistent with R-20 and R-26..R-30: the interval bounds, the full-key merging, the date-keyed cache and the refusal precedence. It raised one **BLOCKER:** pin 5 (the mid-month starter, R-28) seeded the agreement row from the hire date, which cut the month there, so R-28's fallback was never used and the fact would pass without it.
  - **Fixed in `227351e`** (`test-qa`, test file only):
    - the AC agreement row now starts 1 March, while the profile and the window start 10 April;
    - April is therefore one interval whose first day is not employed, and the key must be read at 10 April;
    - the RED was re-argued and the path traced.
    - **Build:** 0 errors / 145 / 115. Unit 1473 (the branch predates 14515's four Unit facts) · Regression non-Docker 128.
- **Build (Orchestrator, at `227351e`, unpiped):** 0 errors / 145 / 115 · Unit 1473 · Regression non-Docker 128.
- **5a Codex, cycle 2: APPROVE.** Its BLOCKER is closed: pin 5 now reaches the fallback and fails for the stated reason under the mutation.
- **5a Fable with 5α, cycle 1 (on `227351e`): REQUEST-CHANGES; 5α PASS.**
  - **What it verified:** the production change is right. The cut and run bounds, the key, the R-29 merge, the cache, the four activation placements and the unchanged refusal order all hold against R-20 and R-26..R-30. No other test or outcome changes, which confirms O-8.
  - **BLOCKER, which Codex missed:** pin 6 dated the same-code re-dated agreement row on **Thursday** 16 April. The month plans, but the week check that runs right after planning is date-only: it refuses any non-Monday agreement-code date, a same-code re-date included. So the pin would error before its assertions. The sibling suite had chosen a Monday for exactly this reason.
  - **NOTE:** pins 3 and 4 also use Thursday 16 April, and are correct only because the planner refuses first.
  - **NOTE:** the lookup now runs on every wired calculation. This is the intended R-20 effect; no existing test is affected.
- **Orchestrator `1e8ae1f`** (Small Tasks Exception, test file only): pin 6 moves to **Monday 20 April**, with its RED re-argued. Pins 3 and 4 note that they rest on the planner-first refusal order.
- **5a cycle 2 (Fable) and cycle 3 (Codex), on `1e8ae1f`: both APPROVE.** Pin 6 now reaches its assertions and trips under its mutation, and no other fact fails for a foreign reason. Fable's NOTE (the pin 3/4 notes cited O-8 instead of `PayrollHostWeeklyTests` H9/H10) was fixed at merge.
- **Orchestrator `e84b65c` at merge** (comments only): `BoundaryDetector` notes R-27's ranking; `EmploymentProfile.OrgId` doc (R-31); the pin 3/4 citation.
- **Merged `8367862`.** The A.5 gate's O-1 run (build, suites, tsc, vitest, O-2) was **stopped by the host for low system memory** partway through the build (MSBuild child node exited; not a compile error). It is to be re-run on request.

- **O-8 stop-point check: HOLDS** (R17, R1; `cdd8253..HEAD`; a static read by a `trace` agent at the A.5 gate).
  - **The planner's split refusal is unchanged.** `ApplyAlignmentPolicy` and `InteriorBoundaryCauses` (`PeriodPlanner.cs:344-431`) are untouched. The three `PeriodPlanner.cs` hunks are doc comments, plus 14512's dated per-segment hydrator, which runs after the policy at `:158`.
  - **The handed-in set is unchanged.** PCS `:1679` still passes `_classificationProvider.GetClassifications()`. `RuleClassification.cs`, `SplitBehavior.cs`, `HttpRuleClassificationProvider.cs`, the `RuleRegistry` classifications and `RuleRegistryClassificationTests` (Unit, green) have no diff.
  - **The A8 pins are unchanged.** Every A8 pin's refusal assertion is byte-identical. The pin edits are the hours reader, bodies without hours (R14) and the prior-month stub (R26).
  - **The refusal order holds:** planner 422 → week-check 422 → prior-month 409 → hours read.
  - **Also changed (ruled, R14 and R25):** a body carrying hours answers 400 before planning, and a PCS without an hours reader throws before planning.
  - **Not affected:** TASK-14519 does not touch the refusal or the handed-in set. Re-check in CA-4 that no A8 pin's cause list gains a local-profile boundary.
  - **CI-only:** the Regression pins are Docker-gated.
- **Paused here (usage limit, 2026-10-15).**
  - TASK-14519+T is built and green: `d7177d6`, 145 / 115, Unit 1473, Regression 128.
  - Its Fable 5a with 5α was stopped before its verdict and must be re-run.
  - Its Codex review was launched; its verdict lands in the scratchpad (`codex14519-verdict.md`).
  - *Resolved on resume:* both verdicts and their fixes are recorded above, under TASK-14519's Step 5a.

## A.5 gate — closed 2026-10-15

*Plain language:* every payroll calculation now stores what it ran on, and a replay recomputes from those frozen inputs. When something replay needs is missing or damaged, it refuses by name instead of guessing. Planning now asks whether a local agreement profile was switched on mid-month using the employee's dated record, stretch by stretch, not the request body. The tests that define "done" for replay are merged, and a deliberate-breakage run proved each new Unit test fails when the behaviour it guards is broken. The stop-point check confirms Stage A has not opened split months: they are refused exactly as before.

**The checklist** (`PLAN-s145.md`, wave table row A.5):
- **Merged:** 14513+T `07337ea`; 14515 `eb78b8e`; 14519+T `8367862`.
- **14515 re-spawn 2, the Unit mutation run:** 3 of 3 tripped (MU-33..35; the table is in § "Wave A.5").
- **O-1 on `9d950ec`** (docs-only on top of `8367862`, so code-identical): 0 errors / 145 warnings (full `--no-incremental` Release rebuild) · CA2100 115 distinct sites · Unit **1477** · DemoSeed 170 · Regression non-Docker 128 · `tsc --noEmit` exit 0 · vitest 1002 (81 files). Every step exited 0.
  - The first run was stopped by the host for low system memory partway through the build (`MSB4166`, a killed worker, not a compile error). The re-run ran in the foreground with two MSBuild workers (`-m:2`).
- **O-2:** `docs/api/openapi.json` and `frontend/src/lib/api-types.ts` regenerated; no drift.
- **O-8 stop-point check: HOLDS** (recorded in § "Wave A.5").
- **Watch items for CA-4, added at A.5:**
  - 14513-T's, 14515's and 14519-T's Docker facts first execute there;
  - `ProfileBoundaryHydrationTests` Variant A, re-seeded under OK26, is expected green;
  - no A8 pin's cause list gains a local-profile boundary (from the O-8 check).
- **Records owed at close, added at A.5:** see the 14513 and 14519 ledger rows.

**Next: Gate A-CLOSE.** TASK-14518 CA-1 (the census by diff since `cdd8253`, as amended at this gate), then the Fable freeze, CA-2, `K_A`, CA-3 and CA-4.

## Gate A-CLOSE (opened 2026-10-15)

*Plain language:* before Stage A is declared closed, CI must show two things. First, the whole Stage A suite is green. Second, each planned deliberate breakage of the code (a "mutation") turns red exactly the tests written to catch it, and nothing else. Most of those tests need a database (Docker), which this machine cannot run, so the breakages are applied on a throwaway branch and CI runs them. CA-1 is the paper check before that: does every test named on the lists still exist; does every test added since the plan was written have a breakage that would catch a regression; and does each run's set of breakages still leave the locally runnable suites green? CI stops at the first red step, so a breakage that turns a Unit test red would hide every Docker result behind it.

**CA-1 (TASK-14518, `test-qa`, at `c0eda11`; no repository file changed).** The full report and its patches stay in the session scratchpad. The frozen outcome is published below, after the Fable seat's rulings.
- **What holds:**
  - **Names.** All 129 list entries (95 distinct keys) resolve to a real test name on master. Nothing is missing or renamed. SMK is in the Smoke project; HDL resolves to three rows (one per harness copy) and S8 to four.
  - **Coverage of the plan's own keys.** Every non-guard key has a Docker mutation, a guard tag, or MU-40 (EE1, under R54). W1-G is a guard the plan's "Guards with no MD" sentence leaves out. MU-40's site is now `PeriodCalculationService.cs:772-773` (was `:738`).
  - **Placements confirmed:**
    - MD-20 is in `ProjectionHoursReader`.
    - MD-30 is in PCS's hydrator lambda (`:1677`), so MU-25's planner pin stays green.
    - MD-39, MD-41 and MD-42 are in `RuleRegistry.EvaluateTimeline` (`RuleRegistry.cs:399`), which no test calls.
    - MD-43 and MD-52 sit at `DeriveEvaluationInputsAsync` `:794-795`.
    - MD-45 is null-tolerant.
    - MD-24 is confirmed only in a reader-first form (F3).
- **The dry run** (one detached throwaway checkout, `-m:2`, foreground; removed and pruned at the end):

| Run | Build | Unit | DemoSeed | Regression non-Docker |
|---|---|---|---|---|
| EA-1 (18 mutations) | 0 errors | 1477/1477 | 170/170 | 128/128 |
| EA-2 (14) | 0 errors | **1476/1477** (MD-29) | 170/170 | 128/128 |
| EA-2 without MD-29 (13) | 0 errors | 1477/1477 | 170/170 | 128/128 |
| EA-3 (12) | 0 errors | 1477/1477 | 170/170 | 128/128 |
| EA-4 (7), MD-24 as worded | 0 errors | **1476/1477** (MD-24) | 170/170 | 128/128 |
| EA-4 (7), MD-24 reader-first | 0 errors | 1477/1477 | 170/170 | 128/128 |
| The nine proposed mutations, together | 0 errors | 1477/1477 | 170/170 | 128/128 |

- **Findings (each for the Fable seat; none was re-listed by the agent):**
  - **F1, two owners for one test.** RS (the replay sentinel fact) gained a fifth sentinel, a profile resolver that throws if called, at 14515 re-spawn 1. MD-31 (EA-1) and MD-36 (EA-2) both call that resolver in replay, so RS turns red under them as well as under its listed owners, MD-22 and MD-44. The plan's fixture condition C-R3 ("RS's replay has no profile resolver") is no longer true. A red RS could not then be traced to one breakage. In EA-1, MD-31's red would also mask MD-22's half of the fact.
  - **F2, a breakage that turns a Unit test red.** MD-29 (the per-plan lane sends an opening flex balance of 0) turns `EmploymentWindowSegmentSkipTests.FlexCarry_PassesThroughNotEmployedMiddleSegment_Unchanged` red (expected 12.5, actual 0). CI would stop at its Unit step. The proposal is MU-46, the same breakage observed in a Unit run instead, with WP11 and the Docker twin `TwoSpells_FlexDeltaCarriesThroughNotEmployedMiddleSegment` resting on it.
  - **F3, the same for MD-24.** As worded, the lane reads the employment windows itself, and that second read turns `Shim_PreHireSegment_NeverResolvesProfile_EmployedSegmentDoes` red. A reader-first form (ask the approval reader first, read the windows only when the previous month is not APPROVED) gives the same Docker reds and keeps Unit green.
  - **F4, reds the lists do not name:**
    - MD-35 also reddens three `BoundaryCauseEncodingTests` facts;
    - MD-51 also reddens `ForwardCalcWrite_And_Rebuild_BothStringEncode_CorrectBoundaryCause`;
    - MD-21 also reddens the host race fact `PriorMonthExportRaceHostTests.CalculateAndExport_PriorMonthReopenedWhileExportWaits_…`.
  - **F5, Docker tests added after the plan with no breakage on any list:**
    - both prior-month race facts (14512-T);
    - `LocalProfileActivationKeyTests` pins 4–6 (the brief's one mutation reaches pins 1–3 and `ProfileBoundaryHydrationTests` Variant A, not these);
    - the R-17 wrong-shape slot fact;
    - the two `RetroactiveCorrectionTrailTests` facts (R-11, R-19);
    - the R-18 slot fact, which only a Unit run can cover.

    Proposed: MD-53 and MD-55..MD-61 in a fifth, clean run EA-5, and MU-47 as a Unit mutation for R-18. The A.3 re-expressed Docker facts need a ruling: they are migrated pins whose evidence is MU-36 and MU-40, unless they are given a breakage of their own.
  - **F6, stale citations.** Most PCS rows cite lines or method names that 14508, 14511, 14512, 14513 and 14519 moved or renamed. Each site was re-found and applied in the dry run. Three traps are recorded for the CA-3 builder:
    - `SettlementCutoffFrom` is shared with the week check;
    - `MarkSupersededSkemaCellSql` is shared with the backfill;
    - MD-02 and MD-03 edit the query that payroll's hours read now shares.
- **The check lines, by effect:**

| Run | (a) every fact once | (b) | (c) | (c′) | (d) Unit/DemoSeed reach |
|---|---|---|---|---|---|
| EA-1 | **fails** (F1, F4) | pass | pass | pass | pass |
| EA-2 | **fails** (F1, F4) | pass | pass | pass | **fails** (F2) |
| EA-3 | pass | pass | pass | pass | pass |
| EA-4 | pass | pass | pass | pass | **fails** as worded (F3); passes reader-first |

- **Sent to the Fable seat (`adjudicator`) for rulings and the freeze** (2026-10-15).

### The Fable seat's rulings on CA-1 (R-33..R-46; `adjudicated-by-model: claude-fable-5-1`; verdict RULED, no owner question)

*Plain language:* every finding was settled inside the plan's own authority, at the cost of one small test change and one extra CI run.
- **One test with two owners (F1).** Rather than re-listing around the problem, the profile-resolver sentinel moves into a test of its own. Each red then names one cause again.
- **Breakages that turned a Unit test red (F2, F3).** One is observed in a Unit run instead (MU-46). The other is reworded so the Unit suite stays green (MD-24, reader-first).
- **Reds the lists did not name (F4).** They are added to the lists.
- **The new Docker tests (F5).** They get breakages in a fifth run, EA-5.
  - The seat found a flaw the report missed. As proposed, MD-55 returned early, before the code that MD-56, MD-57 and MD-58 break. MD-56's shifted bound would also have cancelled MD-58's red. Run together, CI would have shown three of the tests green and the gate would have had to re-run.
  - MD-55 is therefore rewritten as the smaller "key from the request body" edit, and MD-56 moves to EA-3.
- **The seat also found that MD-59 reaches the R-18 test (FS-2),** not just the R-17 one.

- **R-33 (F1): split the fifth sentinel out of RS.**
  - A new fact, RS-P (`Replay_NeverConsultsTheProfileResolver_ByteIdentical`), replays with the throwing profile resolver and no other sentinel. RS goes back to R42/R56's four.
  - C-R3 now reads: "RS's replay PCS has no profile resolver. RS-P's replay PCS has the throwing profile resolver and no other sentinel. RP1 and RP2 build theirs with a dated profile resolver."
  - MD-31 → RP1 + RS-P (EA-1); MD-36 → RP2 + RS-P (EA-2). RS keeps one owner per run: MD-22, MD-44, MD-45, MD-38.
  - Route: `test-qa`, one file → 5α → 5a Fable (no Codex: test-only) → merge → O-1 → `--list-tests`.
- **R-34 (F2): MD-29 is withdrawn.** MU-46 replaces it under fact 3, as R54 did for MU-40. WP11 and TS rest on MU-46, and EA-2 becomes thirteen mutations. The observation of record is CA-1's EA-2 pair: 1476/1477 with MD-29, 1477/1477 without.
- **R-35 (F3): MD-24 is frozen in the reader-first form.**
  - In `CalculatePlanlessAsync` (`:1305`), before `BuildPlanWithHistoriesAsync`, the prior-month check runs first and consults the approval reader.
  - Only when M−1 is not APPROVED, or no reader is registered, does it read the employment windows. It then throws `PriorMonthNotApprovedException`; a null reader throws `InvalidOperationException`.
  - `BuildPlanWithHistoriesAsync` and the raw guard `EnsurePeriodCalculableAsync` (`:1433-1442`) are untouched.
  - The reds are unchanged: H9, H10, X1–X4.
- **R-36 (F4): the by-effect additions are listed (R55):**
  - MD-35 adds BC-1..BC-3;
  - MD-51 adds BC-4;
  - MD-21 adds RR-H.
- **R-37: MD-53 approved; MD-54 declined.** MD-53: in `PayrollExportService.cs:143`, the `PriorMonthToRecheck` recheck is never taken. Reds: RR-S, RR-H.
- **R-38: the local-profile family is re-partitioned.**
  - **MD-55, re-formed:** `IntervalActivationKeyAsync` `:1959-1962` returns the request body's org, agreement and OK version. Reds: LP1–LP3, PB-A.
  - **MD-56:** each run's exclusive lower bound one day early (`:1890`). Red: LP4. Placed in **EA-3**.
  - **MD-57:** the employment-start fallback skipped (`:1936`). Red: LP5.
  - **MD-58:** the same-key merge never taken (`:1887`). Red: LP6.
  - Under R65, a change to either method between `K_A` and K re-observes all four.
- **R-39: MD-59, MD-60 and MD-61 approved; MU-47 recorded.**
  - **MD-59** (`LoadManifestAsync` `:2744-2745`; an undecodable slot falls back to the event): reds FS-1 **and FS-2**.
  - **MD-60** (`RetroactiveCorrectionService.cs:343, :352`; the correction event takes the request's agreement and position): red RT-1.
  - **MD-61** (`:269`; the manifest id comes from the first rule result): red RT-2.
  - **MU-47** (drop `[JsonRequired]` from `PlanInputs.OpeningFlexBalance`) gives FS-2's evidence at the attribute.
- **R-40: EA-5 is a clean fifth run; folding is declined.** Line (c) is mechanical by design, and RR-H relies hard on PM and on the host's approval-reader registration (H-DI-APR), which the other runs edit.
- **R-41: the six A.3 re-expressed Docker facts (A3-1..A3-6)** are migrated existing pins. Their mutation evidence is MU-36 and MU-40, which trip the same request-shape assertions at Unit level. No Docker MD reaches them.
- **R-42: the guard list is confirmed:**
  - HDL, H6, H7, H11, AR9, AR10, RO2, RO4, FA and W1-G (the plan's "Guards with no MD" sentence gains W1-G);
  - FS-G1 (R-22) and FS-G2 (R-17);
  - `PayrollHostRecalcBlockedTests.AgreementChangeDates_AreTheWeekdaysTheArFactsRelyOn` (O-10);
  - `HarnessDdlMatchesInitSqlTests.CurrentSkemaCellIndex_NullTaskId_TwoCurrentRows_Collide23505`;
  - `HrBackdateWorklistRepositoryTests.WriteForExportedMonths_OpenEnded_ClipIsNotWidenedIntoNextMonth` (R-1). The optional clip mutation is declined.

  EE1 is covered by MU-40, not a guard.
- **R-43: stale citations.** The CA-3 builder works from CA-1's re-found sites (the `mut/*.diff` files, amended by R-38 and R-39). No mutation's meaning changed.
  - Three binding traps:
    - for MD-43 and MD-52, edit only the argument at `:794-795`;
    - MD-13 uses a local `const` SQL string;
    - MD-02 and MD-03's predicate sits in the query payroll's hours read now shares.
  - The plan's line numbers are refreshed at CA-7.
- **R-44: the MD-30 watch item becomes an expected green in EA-1.** RT-1's event label comes from the frozen piece, which MD-30 does not touch, and a mapping miss only skips a line.
- **R-45: the check lines are frozen.** Every run passes all five lines: (a) each fact once, (b) each edited path once, (c) no hard relied-on path edited, (c′) soft Not-guards, (d) Unit/DemoSeed reach N.
- **R-46: where the freeze lives:**
  - this section;
  - a banner and four substantive edits in the plan's § "RED evidence" (MD-29 withdrawn, MD-24's wording, C-R3, W1-G; the runs table gains EA-5 and MD-56);
  - TASK-14518's CA-3 text (five commits; MD-55's form; MD-56 in EA-3; FS-2 on MD-59).

### The frozen lists (R-33..R-46)

Fact keys resolve to full names in the table at the end of this section. Each list is the exact red set CI must show for that run; "expected green" names the nearest facts that must stay green in the same build (plan `:600`). A key outside a run's red list and green list is not asserted for that run. Any change from here on is a Fable ruling (R45).

| Run | Mutations | Expected red (exactly these) | Expected green (spot checks) |
|---|---|---|---|
| **EA-1** (18; 59 facts) | MD-01, 05, 06, 11, 14, 19, 20, 22, 25, 26, 28, 30, 31, 32, 35, 47, 48, 52 | S1, S2, S3, S4, S7, S11, S12, S13, TRM, SHO, Q1, Q2, Q3, Q4, Q6, Q7, B1, B2, B3, S9, H1, H2, RE3, WP1, WP2, WP3, WP5, WP6, WP7, RC1, RC2, H4, RS, AR3, AR4, AR7, AR8, AR1, AR2, AR5, AR6, H10, H12, WP12, RP1, RP5, RP6, RP9, W1, W1-EX, RO1, RO3, WP4, RL, EE2, **RS-P, BC-1, BC-2, BC-3** | S5, S6, S8, S10, PAR, Q5, RE1, RE2, WP8, WP9, WP11, H6, H7, H8, H9, H11, H13, H14, X1, X3, AR9, AR10, EE1, RP2, RP3, RP4, RP7, RP8, W1-G, RO2, RO4, FA, HDL, **RT-1, FS-1, FS-2, FS-G1, FS-G2, BC-4, LP1–LP6, PB-A, RR-S, RR-H, TS** |
| **EA-2** (13; 38 facts) | MD-02, 03, 04, 07, 10, 15, 21, 27, 34, 36, 44, 49, 51 (MD-29 withdrawn, R-34) | S6, S1, S2, S3, S12, S4, Q7, S8, RP4, RC2, RE1, RE2, RE3, H3, H4, H5, H12, H13, H14, SMK, AR1, AR2, AR3, AR4, AR5, AR6, AR7, AR8, H10, RP8, RP2, RL, RS, RO3, RB, **RS-P, RR-H, BC-4** (WP11 removed) | S5, S7, S9, S10, S11, S13, Q1–Q6, TRM, SHO, B1–B3, H1, H2, H6–H9, H11, X1–X4, AR9, AR10, WP1–WP10, WP12, RP1, RP3, RP5–RP7, RP9, EE1, EE2, W1, W1-EX, RO1, FA, **WP11, TS, RR-S, BC-1–BC-3, FS-1, FS-2, FS-G1, FS-G2, LP1–LP6, PB-A, RT-1, RT-2** |
| **EA-3** (13; 19 facts) | MD-08, 13, 16, 17, 18 (**corrected at CA-5, R-47:** re-save increases only), 23, 33, 37, 41, 43, 45, 50, **56** | Q6, S10, PAR, S13, S11, Q5, H10, RP7, RP3, WP6, WP7, WP1, WP2, WP3, RL, RS, H13, H14, **LP4** | S1–S4, S6–S8, S12, Q1–Q4, Q7, H3–H5, H9, H12, AR1–AR8, RP1, RP2, RP4–RP6, RP8, RP9, RC1, RC2, WP4, WP5, WP8–WP12, EE1, EE2, RE1–RE3, **LP1, LP2, LP3, LP5, LP6, PB-A, RS-P, RR-S, RR-H, FS-1, FS-2, FS-G1, FS-G2, BC-1–BC-4, RT-1, RT-2**, **FB-1–FB-6** (R-48) |
| **EA-4** (7; 20 facts) | MD-09, 12, **24 (reader-first, R-35)**, 38, 39, 42, 46 | Q6, S5, PAR, H9, H10, X1, X2, X3, X4, RC1, RC2, RP4, RS, WP5, WP9, RE1, RE3, WP8, WP10, H8 (unchanged) | S1–S4, S6–S8, S11–S13, Q1–Q5, Q7, H3–H7, H11–H14, AR1–AR10, RP1–RP3, RP5–RP9, RL, WP1–WP4, WP6, WP7, WP11, WP12, RE2, EE1, EE2, B1–B3, W1, RO1–RO4, **RS-P, LP1–LP6, PB-A, FS-1, FS-2, FS-G1, FS-G2, BC-1–BC-4, RT-1, RT-2, RR-S, RR-H**, **TRM, SHO** (R-48) |
| **EA-5** (7; 12 facts; new, R-40) | MD-53, **55 (body-key form, R-38)**, 57, 58, 59, 60, 61 | RR-S, RR-H (MD-53); LP1, LP2, LP3, PB-A (MD-55); LP5 (MD-57); LP6 (MD-58); FS-1, FS-2 (MD-59); RT-1 (MD-60); RT-2 (MD-61) | LP4, FS-G1, FS-G2, BC-1–BC-4, A3-6, H3, H4, H5, H11, RO1–RO4, RS, RS-P, RL, RP1–RP9, the marquee replays (`AgreementCodeMarqueeTests`, `EmployeeProfileMarqueeTests`), FA, EE1, EE2, WP1–WP12 |

**Unit-mutation coverage (fact 3), as ruled.** These observations are already made; they need no CI run.

| Id | Mutation | Unit facts it trips (observed) | Docker pins resting on it | Ruling |
|---|---|---|---|---|
| MU-40 | PCS `:772-773`: a NOT_EMPLOYED segment adds a piece | `Segment0NotEmployed_…`, `Shim_PreHireSegment_…`, `LeaverPlan_…` (§ "A.4 gate") | EE1; A3-1..A3-6 (with MU-36) | R54; R-41 |
| MU-36 | PCS: context pieces dropped (§ "A.4 gate") | `ContextPiece_PromotedToDepartmentHead…`, `LeaverPlan_…`, `FlexCarry_…` | A3-1..A3-6 (with MU-40) | R-41 |
| **MU-46** | PCS `:945-947`: the per-plan lane passes `0m` as the opening flex balance | `EmploymentWindowSegmentSkipTests.FlexCarry_PassesThroughNotEmployedMiddleSegment_Unchanged` (expected 12.5, actual 0; CA-1's EA-2 dry run, 1476/1477) | WP11, TS | R-34 |
| **MU-47** | `PlanInputs.cs`: `[property: JsonRequired]` dropped from `OpeningFlexBalance` | `PlanInputsRequiredMembersTests.Deserialize_MissingOpeningFlexBalance_Throws`, `…_ThroughTheEventSerializer_Throws` (CA-1, 1475/1477) | FS-2, at the attribute (FS-2 is also MD-59's, at the load path) | R-39 |

**Work these rulings create before `K_A`:**
- (1) the RS split (R-33): merged `07f7c78` (record below);
- (2) this section, with the plan and brief edits of R-46 (done, Orchestrator);
- (3) RS-P's full name confirmed by `--list-tests` on `07f7c78` (done).

**The fact keys** (128). Names are from `--list-tests` on `c0eda11`; RS-P's is from `07f7c78`. FB-1..FB-6 were added at CA-5 (R-48), from EA-3's TRX.

| Key | Full name |
|---|---|
| S1 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Resave_Entry_SkemaGetReturnsOneEntry_8` |
| S2 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Resave_Entry_TimeEntriesGetReturnsOneRow` |
| S3 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Resave_Entry_SendGateBalancedAt8` |
| S4 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Resave_Entry_TeamOverviewShows8` |
| S5 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Resave_Entry_EventsTableHoldsTwoEvents` |
| S6 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Resave_Absence_SkemaGetAndAbsencesGetReturnOneRow` |
| S7 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Retry_IdenticalPayload_LeavesOneCurrentRow` |
| S8 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Resave_ZeroOrNegativeHours_Returns400_WritesNothing` (4 rows: entry/absence × 0/−1 hours) |
| S9 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Resave_FullDayOnlyAbsence_PositivePartialHours_Keeps422` |
| S10 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.TimePost_TwoRegistrationsSameDayAndTask_BothCounted` |
| S11 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Resave_Entry_AllocationBreakdownCountsOnce` |
| S12 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Resave_Entry_BalanceSummaryCountsOnce` |
| S13 | `StatsTid.Tests.Regression.Skema.SkemaCellSupersessionTests.Resave_StrandCheckCountsCellOnce` |
| Q1 | `StatsTid.Tests.Regression.Skema.SkemaQuotaResaveTests.Resave_Vacation_SameHours_UsedUnchanged` |
| Q2 | `StatsTid.Tests.Regression.Skema.SkemaQuotaResaveTests.Resave_Vacation_FewerHours_UsedFallsBySignedDelta` |
| Q3 | `StatsTid.Tests.Regression.Skema.SkemaQuotaResaveTests.ExhaustedQuota_UnchangedReplacement_Returns200` |
| Q4 | `StatsTid.Tests.Regression.Skema.SkemaQuotaResaveTests.ExhaustedQuota_ReducedReplacement_Returns200` |
| Q5 | `StatsTid.Tests.Regression.Skema.SkemaQuotaResaveTests.ExhaustedQuota_IncreaseBeyondCap_Returns422` |
| Q6 | `StatsTid.Tests.Regression.Skema.SkemaQuotaResaveTests.UsedAboveCapAfterRevaluation_Reduction_Succeeds` |
| Q7 | `StatsTid.Tests.Regression.Skema.SkemaQuotaResaveTests.NearCap_ResaveIncreaseWithinCap_Returns200` |
| TRM | `StatsTid.Tests.Regression.Settlement.TerminationSettlementTests.Termination_PostEndDateBooking_DoesNotConsume` |
| SHO | `StatsTid.Tests.Regression.Settlement.SpecialHolidaySettlementTests.SpecialHoliday_ResavedAllowanceDayInTakingWindow_UsedCountedOnce` |
| B1 | `StatsTid.Tests.Regression.Outbox.ProjectionBackfillTests.Backfill_AfterSkemaResave_RebuildsCurrentSet_SupersessionInOutboxOrder` |
| B2 | `StatsTid.Tests.Regression.Outbox.ProjectionBackfillTests.Backfill_AfterSkemaResave_ReRun_LeavesSupersessionMarksUnchanged` |
| B3 | `StatsTid.Tests.Regression.Outbox.ProjectionBackfillTests.Backfill_AfterSkemaAbsenceResave_RebuildsCurrentSet_SupersessionInOutboxOrder` |
| PAR | `StatsTid.Tests.Regression.Outbox.ProjectionParityTests.Parity_AfterSkemaResaveAndTimePost_LiveCurrentSetEqualsFoldOfEvents` |
| HDL | `StatsTid.Tests.Regression.Migrations.HarnessDdlMatchesInitSqlTests.HarnessDdl_HoursTablesAndManifests_MatchInitSqlSchema` (3 rows, one per harness copy) |
| H1 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.CalculateAndExport_BodyCarriesEntry_Returns400_WritesNothing` |
| H2 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.Recalculate_BodyCarriesEntry_Returns400_WritesNothing` |
| H3 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.CalculateAndExport_October_SeptemberNotApproved_Returns409_NamesSeptember_WritesNothing` |
| H4 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.CalculateAndExport_October_SeptemberEmployeeApproved_Returns409` |
| H5 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.Recalculate_October_SeptemberNotApproved_Returns409` |
| H6 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.CalculateAndExport_June2026_StartsMonday_NoPriorApprovalNeeded_200` |
| H7 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.CalculateAndExport_StarterOnFirst_NoPriorApprovalNeeded_200` |
| H8 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.CalculateAndExport_StarterThursday3Sep_NoAugustApprovalNeeded_200` |
| H9 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.CalculateAndExport_SplitMonth_PriorNotApproved_AnswersPlanner422First` |
| H10 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.CalculateAndExport_AgreementChangeTue1Sep_AugustNotApproved_AnswersWeekCheck422First` |
| H11 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.Export_October_SeptemberNotApproved_RawRouteUnaffected_200` |
| H12 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.Recalculate_ExportedSeptember_OctoberExported_ResponseAndAuditNameOctober` |
| H13 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.Recalculate_ExportedSeptember_OctoberNotExported_NamesNothing` |
| H14 | `StatsTid.Tests.Regression.Payroll.PayrollHostWeeklyTests.Recalculate_ExportedMay2026_MonthEndsSunday_NamesNothing` |
| AR1 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.Recalculate_AgreementChangeTue1Sep_Returns422_AgreementChangeMidWeek_WritesNothing` |
| AR2 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.CalculateAndExport_AgreementChangeTue1Sep_Returns422_AgreementChangeMidWeek_WritesNothing` |
| AR3 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.Export_AgreementChangeTue1Sep_Returns422_AgreementChangeMidWeek_WritesNothing` |
| AR4 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.ExportPeriod_AgreementChangeTue1Sep_Returns422_AgreementChangeMidWeek_WritesNothing` |
| AR5 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.Recalculate_AgreementChangeTue29Sep_OctoberReturns422_AgreementChangeMidWeek_WritesNothing` |
| AR6 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.CalculateAndExport_AgreementChangeTue29Sep_OctoberReturns422_AgreementChangeMidWeek_WritesNothing` |
| AR7 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.Export_AgreementChangeTue29Sep_OctoberReturns422_AgreementChangeMidWeek_WritesNothing` |
| AR8 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.ExportPeriod_AgreementChangeTue29Sep_OctoberReturns422_AgreementChangeMidWeek_WritesNothing` |
| AR9 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.CalculateAndExport_AgreementChangeMonday1June_Pays_200` |
| AR10 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.CalculateAndExport_AgreementChangeWed18Mar_StillPlanner422_WithRuleId` |
| X1 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.Recalculate_MidMonthAgreementCodeChange_Returns422_RedactedProblem_LinesUnchanged` |
| X2 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.CalculateAndExport_MidMonthAgreementCodeChange_Returns422_NoExportRecord_NoManifest` |
| X3 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.CalculateAndExport_RulesUnavailable_Returns503_NoExportRecord_NoManifest` |
| X4 | `StatsTid.Tests.Regression.Payroll.PayrollHostRecalcBlockedTests.Recalculate_RulesUnavailable_Returns503_LinesUnchanged` |
| SMK | `StatsTid.Tests.Smoke.PayrollCalcAuditSmokeTests.PayrollCalculateAndExport_WritesExactlyOneManifestLinkedAuditRow_ComposedStack` |
| RE1 | `StatsTid.Tests.Regression.Payroll.PayrollHostRealEngineTests.CalculateAndExport_September_FullTimeHK_AtNorm_NoOvertimeNoFlexPayout_RealEngine` |
| RE2 | `StatsTid.Tests.Regression.Payroll.PayrollHostRealEngineTests.CalculateAndExport_September_FullTimeAC_AtNorm_NoMerarbejde_RealEngine` |
| RE3 | `StatsTid.Tests.Regression.Payroll.PayrollHostRealEngineTests.CalculateAndExport_October_HoursFromProjections_ClosingWeekPaidDated4Oct_RealEngine` |
| WP1 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.TwoMonths_ClosingWeek_SeptemberPaysNothing_OctoberPays300And180_Dated4Oct` |
| WP2 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.TwoMonths_TailCrossing_OctoberPays300_Dated4Oct` |
| WP3 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.AugustToOctober_NoDuplicateNoGap_OvertimeEqualsWeeklyExcessOfSettledWeeks` |
| WP4 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.Leaver_LastDayWed30Sep_SeptemberPaysClosingWeek_Dated30Sep` |
| WP5 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.OkBoundary_April2026_OvertimeDated5April_OK26Key_ContextCarries30And31March` |
| WP6 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.OpeningWeekFractionChange_Tue1Sep_OneSegmentMonth_Pays256And604_Dated6Sep` |
| WP7 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.OpeningWeekFractionChange_Tue1Sep_AC_Merarbejde860` |
| WP8 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.WindowContract_September_NormTotal16280_NoNormalHoursFor31Aug` |
| WP9 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.WindowContract_PositionChangeTue1Sep_SundayModeReported_NoExportLineChange` |
| WP10 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.WindowContract_AcResearch_AnnualTotalKept` |
| WP11 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.Flex_TwoMonths_SeptemberPaysOut500Dated30Sep_OctoberFed100_NoPayout` |
| WP12 | `StatsTid.Tests.Regression.Payroll.WeeklyPayrollTests.DatedKey_StaleBodyPosition_MapsWithDatedRecordPosition` |
| EE1 | `StatsTid.Tests.Regression.Payroll.EmploymentEdgesTimelineTests.EmploymentEdges_Starter3Sep_TimelineCoversEmployedDaysOnly` |
| EE2 | `StatsTid.Tests.Regression.Payroll.EmploymentEdgesTimelineTests.EmploymentEdges_LeaverTue29Sep_SeptemberSettlesWeek_OctoberEvaluatesNothing` |
| RP1 | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_AfterBackdateAtFrozenStart1Sep_ByteIdentical` |
| RP2 | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_After31AugContextFractionChange_ByteIdentical` |
| RP3 | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_RecordedEntryIdAbsent_RefusesReplayEntriesMissing` |
| RP4 | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_SupersededBoundRow_IsNotAbsent_ReplaysByteIdentical` |
| RP5 | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_TamperedEntryRow_RefusesEntriesDigestMismatch` |
| RP6 | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_TamperedAbsenceRow_RefusesAbsencesDigestMismatch` |
| RP7 | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_ConfigFingerprintChanged_RefusesReplayConfigChanged` |
| RP8 | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_EvaluationModelChanged_RefusesReplayEvaluationModelChanged` |
| RP9 | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_ManifestWithoutPlanInputs_RefusesManifestWithoutFrozenInputs` |
| RC1 | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Reconstruction_NewEntryAfterCalc_ReplayByManifestIdOnly_ByteIdentical_FreshCalcDiffers` |
| RC2 | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Reconstruction_BoundCellResaved_ReplayByteIdentical_FreshCalcDiffers` |
| RL | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_LeaverEndDateRemovedAfterCalc_ByteIdentical_FreshCalcDiffers` |
| RS | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_AfterPriorMonthReopened_ByteIdentical_FreshCalcRefused409` |
| RS-P | `StatsTid.Tests.Regression.Segmentation.ReplayFrozenInputsTests.Replay_NeverConsultsTheProfileResolver_ByteIdentical` (new, R-33) |
| RB | `StatsTid.Tests.Regression.Segmentation.ManifestProjectionRebuildPlanInputsTests.Rebuild_PreservesPlanInputs_ReplaySucceeds` |
| FA | `StatsTid.Tests.Regression.Payroll.EmployeeProfileForwardCalcAsOfTests.ForwardCalc_ResolverAsOf_UsesRowInForceAtSegmentStart` |
| W1 | `StatsTid.Tests.Regression.Worklist.HrBackdateWorklistRepositoryTests.WriteForExportedMonths_BackdateTue29Sep_BoundedAt1Oct_FlagsSeptemberAndOctober` |
| W1-EX | `StatsTid.Tests.Regression.Worklist.HrBackdateWorklistRepositoryTests.WriteForExportedMonths_SelectsMonthsIntersectingInterval_OneRowPerMonth_BaselineHashPerRow` |
| W1-G | `StatsTid.Tests.Regression.Worklist.HrBackdateWorklistRepositoryTests.WriteForExportedMonths_BackdateBoundedAtMonday28Sep_DoesNotFlagOctober` |
| RO1 | `StatsTid.Tests.Regression.Approval.ReopenFlagsNextMonthTests.Reopen_ApprovedNotExportedSeptember_OctoberExported_WritesOctoberRow_PriorMonthReopened_Unblocked` |
| RO2 | `StatsTid.Tests.Regression.Approval.ReopenFlagsNextMonthTests.Reopen_ApprovedNotExportedSeptember_OctoberNotExported_WritesNothing` |
| RO3 | `StatsTid.Tests.Regression.Approval.ReopenFlagsNextMonthTests.Reopen_ApprovedNotExportedSeptember_OctoberAndNovemberExported_WritesOctoberOnly` |
| RO4 | `StatsTid.Tests.Regression.Approval.ReopenFlagsNextMonthTests.Reopen_ApprovedNotExportedMay2026_JuneExported_WritesNothing` |
| RR-S | `StatsTid.Tests.Regression.Payroll.PriorMonthExportRaceTests.Export_PriorMonthReopenedWhileExportWaits_RefusedPriorMonthNotApproved_NothingWritten` |
| RR-H | `StatsTid.Tests.Regression.Payroll.PriorMonthExportRaceHostTests.CalculateAndExport_PriorMonthReopenedWhileExportWaits_Returns409_CalculationKept_NoExportRecord` |
| LP1 | `StatsTid.Tests.Regression.Payroll.LocalProfileActivationKeyTests.BodySaysHkOk24_RecordSaysAcOk26_ActivationFoundUnderTheRecordsKey_ManifestRecordsLocalProfileActivation` |
| LP2 | `StatsTid.Tests.Regression.Payroll.LocalProfileActivationKeyTests.BodySaysHkOk24_RecordSaysAcOk26_ActivationOfTheBodysScopeInventsNoBoundary` |
| LP3 | `StatsTid.Tests.Regression.Payroll.LocalProfileActivationKeyTests.AgreementChangeThenHkActivation_SecondIntervalIsAsked_RefusalNamesBothCauses` |
| LP4 | `StatsTid.Tests.Regression.Payroll.LocalProfileActivationKeyTests.ActivationOnTheAgreementChangeDate_DoesNotHideTheAgreementChange_RefusalNamesAgreementCodeChangeOnly` |
| LP5 | `StatsTid.Tests.Regression.Payroll.LocalProfileActivationKeyTests.MidMonthStarter_KeyIsReadWhereEmployed_ActivationAfterTheStartIsFound` |
| LP6 | `StatsTid.Tests.Regression.Payroll.LocalProfileActivationKeyTests.SameKeyAcrossAReDatedAgreementRow_AskedAsOneRun_ActivationOnTheSeamIsFound` |
| PB-A | `StatsTid.Tests.Regression.Segmentation.ProfileBoundaryHydrationTests.ProfileEffectiveFromInsidePeriod_ProducesBoundary` |
| FS-1 | `StatsTid.Tests.Regression.Segmentation.FrozenInputsSlotTests.Replay_PlanInputsSlotWrongShape_RefusesWithoutFrozenInputs_DespiteIntactEvent` |
| FS-2 | `StatsTid.Tests.Regression.Segmentation.FrozenInputsSlotTests.Replay_PlanInputsSlotMissingOpeningBalance_RefusesWithoutFrozenInputs` |
| FS-G1 | `StatsTid.Tests.Regression.Segmentation.FrozenInputsSlotTests.Replay_ProjectionRowAbsent_LoadsFrozenInputsFromEvent_Succeeds` (guard, R-22) |
| FS-G2 | `StatsTid.Tests.Regression.Segmentation.FrozenInputsSlotTests.Replay_SegmentsSlotMalformed_FailsAsDamagedRow_NotEventFallback` (guard, R-17) |
| RT-1 | `StatsTid.Tests.Regression.Payroll.RetroactiveCorrectionTrailTests.Recalculate_RequestProfileDiffersFromDatedRecord_EventRecordsDatedAgreementAndPosition` |
| RT-2 | `StatsTid.Tests.Regression.Payroll.RetroactiveCorrectionTrailTests.Recalculate_NoEmployedSegment_EventLinksPersistedManifest` |
| BC-1 | `StatsTid.Tests.Regression.Segmentation.BoundaryCauseEncodingTests.Replay_LegacyNumericBoundaryCauseRow_ProjectionOnly_StillReplays` |
| BC-2 | `StatsTid.Tests.Regression.Segmentation.BoundaryCauseEncodingTests.Replay_StringBoundaryCauseRow_ProjectionOnly_StillReplays` |
| BC-3 | `StatsTid.Tests.Regression.Segmentation.BoundaryCauseEncodingTests.NullSnapshot_Rebuild_OmitsKey_YetDeserializesToNull_AndReplays` |
| BC-4 | `StatsTid.Tests.Regression.Segmentation.BoundaryCauseEncodingTests.ForwardCalcWrite_And_Rebuild_BothStringEncode_CorrectBoundaryCause` |
| TS | `StatsTid.Tests.Regression.Payroll.EmploymentWindowPayrollTests.TwoSpells_FlexDeltaCarriesThroughNotEmployedMiddleSegment` |
| A3-1 | `StatsTid.Tests.Regression.Payroll.EmploymentWindowPayrollTests.MidMonthStarter_NotEmployedPrefixInManifest_NoCallsNoLinesBeforeStart_DayStartPays` |
| A3-2 | `StatsTid.Tests.Regression.Payroll.EmploymentWindowPayrollTests.MidMonthLeaver_NotEmployedSuffixInManifest_NoCallsNoLinesAfterEnd_LastEmployedDayPays` |
| A3-3 | `StatsTid.Tests.Regression.Payroll.EmploymentWindowPayrollTests.MidMonthFractionChange_EmployeeProfileChangeBoundary_NormScalesPerSpan_VacationDayCountFlat` |
| A3-4 | `StatsTid.Tests.Regression.Payroll.EmploymentWindowLiveRulesetTests.MidMonthStarter_LiveRuleSet_CalculationSucceeds_DayStartPays_NoCallsBeforeStart` |
| A3-5 | `StatsTid.Tests.Regression.Payroll.EmploymentWindowLiveRulesetTests.MidMonthLeaver_LiveRuleSet_CalculationSucceeds_LastEmployedDayPays_NoCallsAfterEnd` |
| A3-6 | `StatsTid.Tests.Regression.Payroll.RetroactiveCorrectionWindowTests.EndDateRecordedAfterExport_CorrectionClawsBackOnlyPostEndDays_ManifestCarriesEmploymentEndedSuffix` |
| FB-1 | `StatsTid.Tests.Regression.Balance.DatedQuotaAnchorTests.R6a_OkVersion_ValidationUsesYearStartOk_NotLiveOk` (added R-48) |
| FB-2 | `StatsTid.Tests.Regression.Balance.DatedQuotaAnchorTests.R6b_Agreement_ValidationUsesYearStartAgreement_NotMonthStartAgreement` (added R-48) |
| FB-3 | `StatsTid.Tests.Regression.Outbox.SkemaMonthlyAccrualGuardTests.SpecialHoliday_BeyondQuota_Rejected422_NothingPersisted_KeyedToAccrualYear` (added R-48) |
| FB-4 | `StatsTid.Tests.Regression.Outbox.SkemaMonthlyAccrualGuardTests.Vacation_BeyondDynamicCap_Rejected422_NothingPersisted` (added R-48) |
| FB-5 | `StatsTid.Tests.Regression.Skema.SkemaAccrualCapTests.Vacation_BeyondFlatForskudCap_Rejected422_NothingPersisted` (added R-48) |
| FB-6 | `StatsTid.Tests.Regression.Hosting.RuleEngineAuthForwardingTests.SkemaSave_QuotaValidateEntitlementSite_ForwardsBearerAndCorrelationId` (added R-48) |


**R-33's RS split: merged `07f7c78`** (ledger row above).
- **The work.** `test-qa` made `24edd37` on `s145/task-14518-rs`, one file, the authorized one. The throwing profile resolver leaves RS, which has R42/R56's four sentinels again. It moves into its own fact, RS-P. RS-P's replay PCS gets the kit's unbounded, non-throwing window (`ReplayTestKit.cs:73-74`), which is what lets the PCS constructor accept a profile resolver.
- **Build (Orchestrator, at `24edd37`, unpiped):** 0 errors / 145 warnings · CA2100 115 · Unit 1477 · DemoSeed 170 · Regression non-Docker 128.
- **5α: PASS. 5a Fable: APPROVED** (no Codex: test-only, R-33). Each point was verified by reading the merged code:
  - **RS-P is red under MD-31.** October's frozen plan has an employed segment, and MD-31 consults the profile sentinel there.
  - **RS-P is red under MD-36.** October freezes a context piece from 28 September; it starts before the period, so MD-36 re-resolves it through the sentinel.
  - **RS-P is green on correct code.** `_profileResolver` is read only on planning and forward-derivation paths, never in replay.
  - **MD-31 and MD-36 no longer reach RS.** Both keep the original expression when there is no resolver, and replay has no request profile to fall back to.
  - **RS's four owners (MD-22, 38, 44, 45) cannot reach RS-P.**
  - NOTEs 3 and 4 were applied in `218b923` (comments only: "errors before its first assertion"; the null-repository citation is now `:3018`).
- **O-1 on `07f7c78`:** 0 errors / 145 warnings · CA2100 115 · Unit 1477 · DemoSeed 170 · Regression non-Docker 128. `--list-tests` lists RS-P, and RS's name is unchanged.

## Stage A gate — `K_A` (2026-10-15)

*Plain language:* this commit is `K_A`, the fixed point Stage A is verified at.
- **What it holds.** All of Stage A's code: payroll works week by week, reads its own hours, maps each part of a month by the employee's dated record, refuses what it cannot judge, and stores a replayable proof of each calculation. With it come the frozen lists of deliberate breakages that CI must confirm.
- **What happens next.** Three things are pushed to a throwaway branch: `K_A` itself (every test must be green), then five commits that each add one run's breakages (exactly the listed tests must go red).
- **What it is for afterwards.** `K_A` is also where Stage B starts from, and the point it reverts to if Stage B cannot pass review.

**CA-1:** done. The lists are frozen (§ "Gate A-CLOSE" → "The frozen lists", R-33..R-46), and the one piece of work the freeze created, R-33's RS split, is merged.

**CA-2 (the Orchestrator's checks):**
- **The 5α/5a column is filled for every Stage A row:**
  - the 20 ledger rows (the 19 for 14500–14519, with every paired pass and fix-up, plus the R-33 split);
  - O-10 (§ "After the A.2 gate": 5α PASS, Fable APPROVED-WITH-WARNINGS).

  Every row ends in an approval, or in the recorded SKIP for 14510 (a pure UI label).
- **O-8's stop-point check is recorded and HOLDS** (§ "Wave A.5", R17). `RuleRegistryClassificationTests` and every A8 refusal pin are unchanged in the diff and green at Unit level. The planner's split refusal and the handed-in classification set are unchanged in behaviour. The Docker A8 pins are CI-verified at CA-4.

**Results (filled at CA-7, 2026-10-16; the detail is in § "CA-5: the evidence runs"):**
- **CA-3:** the chain on `s145-evidence-a`, each tree `K_A` plus one run's set, with the diffs in `.claude/plans/s145/ca3/EA-k.diff`. EA-1 `de005a5` → EA-2 `5cd11b7` → EA-3 `e199a3a` → EA-4 `b804d00` → EA-5 `4dc474f` → EA-3′ `b1857fd` (R-47's correction, on the tip).
- **CA-4, V-A:** run `38747749039`, `headSha == K_A` (`923f7fb`), all seven jobs green. Unit 1477, DemoSeed 170, Regression 2127, 0 failed.
- **CA-5, all five lists matched; confirmed by the Fable seat** (an independent re-derivation from the raw TRX, every expected green checked as passed; R-52):

| Run | CI run | Head | Red | Result |
|---|---|---|---|---|
| EA-1 | `38763657423` | `de005a5` | 59 | matched |
| EA-2 | `38778726507` | `5cd11b7` | 38 (incl. SMK) | matched |
| EA-3 | `38793589235` | `e199a3a` | 25 | mismatch: MD-18 as worded reached first bookings (R-47); `frontend-build` red, outside the evidence (QUAL-207, R-49) |
| EA-4 | `38871341441` | `b804d00` | 20 | matched |
| EA-5 | `38876775857` | `4dc474f` | 12 | matched |
| EA-3′ | `38876840945` | `b1857fd` | 19 | matched; the run of record for EA-3's list |

- **CA-6:** PR **#37** closed unmerged (2026-10-16 11:08 UTC). `s145-evidence-a` deleted on origin and locally, its worktree removed. `claude-code-review.yml` never fired on the branch.
- **CA-7:** QUAL-186 and QUAL-150 → BUILT in the quality register (plan § "Records owed"). QUAL-150's row says what Stage A built and what stays for Stage B. **Stage A is closed.** `K_A` is the Stage B base and the revert target (R33).
- **After CA-7:** QUAL-207 merged as `499771c` (R-49). Its two files, `frontend/src/pages/admin/enhedsspor/PersonDrawer.tsx` and its `__tests__/PersonDrawer.scheduledChange.test.tsx`, lie outside every Stage A/B file set. So if Stage B must be reverted, R33's contract reads: `git diff K_A HEAD -- src tests frontend docker tools` lists only these two files. A revert of the Stage B landings must not take QUAL-207 with it.

## CA-5: the evidence runs (2026-10-15/16)

*Plain language:* `K_A` was pushed alone first (V-A), and CI was all green. Then the throwaway branch was moved forward one evidence commit at a time. Each commit adds one run's deliberate breakages, and CI must turn red exactly that run's frozen list. Each result is compared mechanically:
- the TRX artifact gives every failed test by full name, mapped to its fact key;
- the smoke job's log covers SMK;
- a run matches only if nothing is missing, nothing extra is red, and every test executed.

The tool is `.claude/plans/s145/ca3/compare.sh`. The Fable seat confirms all five comparisons at the end. A mismatch is never interpreted (S144 R9): the Fable seat words a correction, a corrected commit goes on the tip, and that run is repeated.

**EA-3 did not match. The Fable seat's rulings R-47..R-51** (`adjudicated-by-model: claude-fable-5-1`; verdict RULED, no owner question):
- **What happened.** All 19 listed facts went red, and so did six unlisted ones (FB-1..FB-6). All six book holiday for the first time beyond the quota and expect a 422 refusal; they got a 200.
- **Why.** MD-18 was meant to stop a *re-save increase* from being held to the quota (Q5). As worded, "ungate the delta > 0 branch", it also ungated *first bookings*, because a first booking replaces nothing and its delta is the whole new value. The code's own comment on the mutated line names both cases (`SkemaEndpoints.cs:2162`: "An INCREASE (or a first booking)"). CA-1's census searched for re-save facts and never looked at first bookings. It was neither a CI fault nor a builder fault.
- **R-47: MD-18 is narrowed to re-saves, at both of its sites.**
  - The pre-check (`:1596`) becomes `signedDelta > 0m && replacedDays <= 0m`.
  - The in-lock branch shared with MD-08 (`:2160`) becomes `deltaDays <= 0m || replacedDays <= 0m`.
  - A first booking keeps master's path; MD-08 is unchanged.
  - EA-3's red list stays the same 19.
  - The corrected commit is **EA-3′ `b1857fd`** on the tip, after EA-5. Its diff against `K_A` differs from EA-3's in exactly those two lines (`ca3/EA-3prime.diff`). Local checks: 0 errors, CA2100 115, Unit 1477, DemoSeed 170, Regression non-Docker 128.
- **R-48: FB-1..FB-6 become expected greens of EA-3′.**
  - No other quota mutation reaches a first booking: MD-06, MD-07, MD-09 and MD-14 were checked against the code, and so was MD-12, which only acts on re-saves.
  - MD-09 newly reaches unchanged re-saves, so TRM and SHO join EA-4's expected greens.
- **R-49: the `frontend-build` red in EA-3 is outside the evidence, so it is not a mismatch.** But it is a real defect.
  - **Why it is outside.** The job reads only `frontend/**` and `docs/api/openapi.json`, and EA-3 changes eight C# files. The same frontend tree passed on V-A, EA-1, EA-2 and EA-4.
  - **The defect.** The person drawer's baseline effect can overwrite an edit made right after loading (**QUAL-207**). The fix is owed on master after CA-7 and before B.0. It cannot ride the evidence branch.
  - **The rule from here on.** A job the run's diff cannot reach is outside that run's evidence. If it fails again on the same tree, the count stops until the cause is known.
- **R-50: the order.** Running EA-4 and EA-5 before EA-3′ is sound. CI judges the tree at the head, and each tree is checked by `git diff 923f7fb <head>`. The record keeps both EA-3 runs.
- **R-51: the lesson.** A census must enumerate the cases of the *predicate a mutation edits*, not the scenario the plan narrates.
  - It was applied before judging EA-4 (a `trace` read). MD-12's second case (two same-cell entries in one save) is sent by no fact. MD-46's re-hire and gap cases cannot be seeded, because storage holds one employment spell per user (`EmploymentWindowResolver.cs:104`). EA-4 is not at risk.
  - The read surfaced one unpinned master behaviour: "a cell posted twice in one save ends at the later value" (**QUAL-208**).
  - TASK-14518's CA-1 step 3 gains the rule.

**The Fable seat's CA-5 confirmation and R-52** (`adjudicated-by-model: claude-fable-5-1`; **CA-5 CONFIRMED**, no owner question).
- **What was checked, independently of the Orchestrator's script.** For each run:
  - the red set, re-derived from the raw TRX and mapped through this log's fact table;
  - every expected green, checked as `Passed` in that run's TRX;
  - each head's tree, against `K_A` with `git diff`;
  - each run's `headSha` and job conclusions, from GitHub;
  - EA-2's smoke log (SMK is the only failure).

  All agree.
- **Running three at once did not affect validity.** Every job runs on a fresh hosted VM, artifacts were read by run id, and the three red sets are distinct and each equals its own list.
- **R-52: a `pull_request` run tests the head merged into the PR's base, not the head itself.** That equals the head's tree only while the base is an ancestor of the head.
  - It held throughout. The base `cdd8253` (origin/master, unchanged since 2026-10-02) is an ancestor of `K_A`, and the test-merge commit's tree equals `b1857fd`'s.
  - For the Stage B gate: confirm the base is an ancestor of K before the first evidence run, and never push origin/master while an evidence run is in flight.


**Also recorded during CA-5** (moved here from the handoff block at CA-7):
- **QUAL-207 fix, prepared alongside CA-5 and merged after CA-7 (R-49):**
  - **The commits:** `ux` agent, branch `s145/qual-207`, `015f680`, plus the Orchestrator's `77f6ae5` (Small Tasks Exception).
  - **The fix:** the drawer's baselines are seeded where their values are published. The profile baseline is seeded in `hydrate()`, in the same step as the loaded profile. The agreement-code baseline is seeded in the open effect, beside `setStamdata`.
  - **The test:** a new fact lands an edit deterministically between the hydrated commit and the re-baseline effects, through a layout effect of the wrapped `ScheduledChangeNotice`. It was observed RED 3 times on the unfixed component, with CI's error.
  - **Checks:** `tsc` 0; vitest 81 files / **1003** (+1); the formerly flaky fact passed 20 of 20 in a loop.
  - **5α PASS; Fable 5a cycle 1 APPROVED-WITH-WARNINGS** (`015f680`). WARNING 1: the agreement baseline was seeded later than its value, so a direct person switch could keep the first person's code. That path is unreachable through today's host, but was closed in `77f6ae5` and verified by the reviewer's own scenario. NOTEs 1–3 applied. NOTE 4: the "edits during loading are kept" claim is moot, because fields are disabled while loading, so it is not a behaviour change.
  - **Cycle 2 APPROVED** (`77f6ae5`). Codex not required (UI only).
- *Why EA-4, EA-5 and EA-3′ ran at once (Orchestrator, 2026-10-16).* The Regression step takes about 1 h 44 min in CI on every run, V-A included. `ci.yml` has no concurrency group, so a push to the branch does not cancel a running run, and each push starts its own run on its own head. EA-5 and EA-3′ were therefore pushed while EA-4 was still running: the same branch, the same PR, the same fast-forward pushes. Each run is still judged only against its own head's tree (`git diff 923f7fb <head>`, R-50), and only the waiting is dropped. This saves about 3½ hours on a memory-constrained machine.

## Stage B — B.0 (opened 2026-10-16)

*Plain language:* Stage B makes split months payable (QUAL-149). Its briefs were outlined at planning time against `62fcdc3`, before Stage A moved most of the code. B.0 rewrites them in full against `K_A`. The Fable seat then reviews them, with a scoped Step-0b cycle (both lenses) if any criterion or file list changes beyond line numbers.

**The planner's draft** (`planner`, 2026-10-16). Nine briefs were written in `.claude/plans/s145/`: TASK-14520, -14520-T (new), -14521, -14521-T (new), -14522, -14522-T, -14523, -14524, -14528. The full report is in `.claude/plans/s145/b0/B0-report.md`.
- **Headline: much of Stage B is already built by Stage A.**
  - The per-plan lane already sends a split month as one call covering every EMPLOYED part, and maps each part's lines under that part's dated key (C-1).
  - The week check already judges interior agreement changes; the planner's earlier refusal only shadows it (C-2).
- **What remains:**
  - the reclassification to "timeline-aware";
  - payroll hands the planner only the rules it runs;
  - the merge takes the timeline's single answer;
  - the raw routes' own Q6 (a) refusal;
  - the worklist on the shared week predicate.
- **Missed by the outline:**
  - the new label would make payroll's classification reader throw, so every route would answer 503 (C-7);
  - two changes break existing tests no task owned, so there are two new paired passes;
  - four more existing tests flip (C-8: AR10, H9, LP3, LP4);
  - two criteria cannot hold as written (C-3, C-4).
- **The change list goes beyond line numbers**, so a scoped Step-0b cycle is triggered. The mutation set and evidence runs are proposed: EB-1 {MB-2, MB-4, MB-5}, EB-2 {MB-7}, EA-R1 {MD-25, MD-30}, plus Unit MUs needing fact-3 rulings.
- **Open questions for the Fable seat:** Q-A (the line fallback), Q-B (split-month labels), Q-C (R39 for edited facts), Q-D (LP3/LP4), Q-E (the worklist drift), Q-F (the fact-3 rulings). None for the owner.
- **The build-safe order** for the 8 GB machine: at most one building agent at a time.

**Scoped Step-0b, cycle 1 (2026-10-16), both lenses:**
- **Fable reviewer: READY (with warnings W-1..W-6, notes N-1..N-13).** Full text in `.claude/plans/s145/b0/fable-step0b-review.md`.
  - It confirms C-1/C-2, C-3, C-7/C-9, the C-8 census (no fifth flip) and the EB partition.
  - It confirms that a split month replays safely by construction (`FromManifest` never re-asserts the split refusal; `FrozenEvaluationInputs` maps every EMPLOYED segment). But no brief pins it.
  - Warnings:
    - W-1: 14521-T misses two asserted values;
    - W-2: the known-red on master is avoidable;
    - W-3: the close-sequence order;
    - W-4: no split-month replay pin;
    - W-5: FX3 should stay a Docker fact;
    - W-6: a split month's audit row names only its first part.
- **Codex: NOT READY.** Full text in `.claude/plans/s145/b0/codex-step0b-verdict.md`.
  - BLOCKER 1: C-6's "build the evidence chain on the close sha" is sound, but the plan builds the chain before the close commit exists.
  - BLOCKER 2: XB2's promised 200 needs `DeliveringFactory()`; the shared stub delivery answers 404, so the route answers 422.
  - Warnings:
    - W3: no split-month replay pin;
    - W4: Q-E is a real exception to "one predicate, three consumers";
    - W5: Q-A/Q-B need concrete obligations;
    - W6: SP1's +7.20 flex needs a recorded timeline response, and WR1 needs all three Monday-safe causes.
  - Notes: it confirms C-1, C-2, C-4, C-7 and C-9, and the partition's structure.
- **The lenses overlap on:** the close order, the replay pin, Q-E, and Q-A/Q-B.
- **Sent to the adjudicator** with Q-A..Q-F and the planner's proposals (rulings R-53 onward).

**The Fable seat's rulings on cycle 1: R-53..R-79** (`adjudicated-by-model: claude-fable-5-1`; RULED; no owner question). The full text is in `.claude/plans/s145/b0/adjudication-cycle1.md`.
- **R-53, the close sequence becomes one linear order:**
  - C-1 freezes the EB lists *and the mutation patches* (`cb1/mut/*.diff`);
  - C-2 is K;
  - C-3 is Step 7a, which reviews the patches;
  - C-4 is the close commit, pushed and watched green. That run is Stage B's "criteria green in CI", and `origin/master` is not pushed again until C-9;
  - C-5 builds the evidence chain on the close sha by applying the frozen patches byte for byte;
  - C-6..C-8 are the evidence runs, then closing the PR and deleting the branch;
  - C-9 is the sealing commit.
- **R-54, a split-month replay pin.** RP-B1 replays WS1's September after the live state changes: byte-identical result, frozen pieces, no new audit events. Its mutation is MB-13 (replay truncates the frozen pieces), in EB-1.
- **R-55, the worklist's hire/leaver gap is accepted and registered.**
  - Criterion `:699` is narrowed to "one predicate, three consumers — *for an employee employed throughout the month's settled weeks*".
  - Two counterexample facts pin the divergence.
  - Registered as **QUAL-210**.
- **R-56 (Q-A), a timeline line dated in no EMPLOYED part now refuses** (`InvalidOperationException` → 500, nothing persisted). It is unreachable today. Its mutation is MB-12 (Unit).
- **R-57 (Q-B), a split month's summary labels keep naming the first part.**
  - The meaning goes in the ADR-016 D10 amendment.
  - QB1 asserts that the correction event links the two-part manifest.
  - Registered as **QUAL-211**.
- **R-58, the delivering host.** XB2 runs on `DeliveringFactory()` (a labelled fixture change). `SplitMonthHostTests` uses the real-engine host, whose delivery answers 200 (C-B9). IB1's outcome under MB-4 is corrected to "anonymous 422".
- **R-59, where each check is observed.** SP1's +7.20 flex is observed in WS1 through the recorder. WR1 becomes a three-case Theory: PROFILE, CATEGORY, and a Monday AGREEMENT_CODE.
- **R-60..R-63, smaller fixes:**
  - R-60: 14521-T's two missed values;
  - R-61: no known-red on master; 14520-T re-expects `EmploymentWindowLiveRulesetTests.cs:253` under B1;
  - R-62: FX3 stays a Docker fact (a 0-hour VACATION row seeded directly; the criterion is unchanged);
  - R-63: the stale comments.
- **R-64 to R-79, records and confirmations:**
  - R-64: HRP-003's rewrite is owed;
  - R-65: the two paired passes and the labels B1/B2/B3 are accepted. The plan's standing constraints and Step 7a item 7 list every label in force (R8, R9, R14, R21, R26, B1, B2, B3);
  - R-66: the Stage A Unit mutations whose method 14522 changes are re-run;
  - R-68: the compliance family has no default;
  - R-70: MD-24 is re-observed as EA-R2;
  - R-71: LP3/LP4 move to the fixture rule set;
  - R-72: the fact-3 rulings;
  - R-73: criterion `:713` becomes "unchanged except the refusing rule's name, now `NORM_CHECK_WEEKLY`";
  - R-74: the raw-route order is planner → week check → Q6 (a);
  - R-75: the merge input;
  - R-76: Q6 (a)'s reason `split-month-single-profile-route` and its body;
  - R-77: the Danish text, "overenskomstskift midt i en uge — ugen kan ikke afregnes efter to overenskomster";
  - R-78: the build-safe order;
  - R-79: cycle 2 re-reviews with both lenses, scoped.
- **Registered now** (the register, 2026-10-16): **QUAL-209** (the Stage B residual: a mid-week agreement change stays manual; the id the worklist shows), **QUAL-210** and **QUAL-211**.
- **The planner's revision 1** (absorbing R-53..R-79 into the briefs and the plan) was dispatched on 2026-10-16. Cycle 2 follows, with both lenses.

**The planner's revision 1: done** (report in `.claude/plans/s145/b0/B0-revision1-report.md`). All 27 rulings were absorbed into the plan and the nine briefs. It flagged three items for the seat:
- F-1: MB-13 also reddens two existing straddle replays, so EB-1 would have 14 facts, not 12;
- a fifth `EmploymentTruncationAlignmentTests` fact (`:313`) also names `OVERTIME_CALC`;
- R-65's label list does not match the labels in use.

**Scoped Step-0b, cycle 2 (2026-10-16):**
- **Codex: NOT READY, narrowly** (`.claude/plans/s145/b0/codex-step0b-c2-verdict.md`). All six cycle-1 findings are **CLOSED**: both BLOCKERs and WARNINGs 3–6. It confirmed independently FX3's 6.00 credit and 8.00 flex balance, and SP1/WS1's 7.20 flex. Open:
  - F-1 is confirmed, so EB-1 must list the two straddle replays (14 facts);
  - the Step 7a label whitelist must be reconciled with the labels in use (`F1`, `R5(a)`, `TASK-14505-T`, `R-10`…), without blanket-authorizing every "S145" comment;
  - TASK-14520-T must own the class-doc correction of `EmploymentTruncationAlignmentTests` (`:24-27`, "faithful to production wiring");
  - smaller items: C-4's no-push interval begins after the *successful* close push; the Stage-A-only fallback must reconcile QUAL-209/210/211; and TASK-14523's stale "adjust at 5a" reference (its 5a is a SKIP).
- **Fable reviewer: NOT RUN.** The agent stopped at the start of cycle 2 on the account's **Fable usage limit** (HTTP 429, request `req_011Cg61RPugkyVaKphCJFhC9`). The review floor is Fable, under the 2026-09-29 routing ruling, and dual-lens review is mandatory. So cycle 2's internal lens, the adjudicator's rulings and the sign-off all wait until Fable is available again. **Halted, 2026-10-16.** A resume on 2026-10-18 hit the same limit at once (`req_011Cg9nDgz1xs9wuwhUw1imc`).
- **Owner direction, 2026-10-19: "Run the review with opus."** The cycle-2 internal lens was therefore run on Opus, as owner-directed, following WORKFLOW § "Model Routing" (the sanctioned route): a `general-purpose` agent with `model: opus`, told it is read-only, stating its model as `opus-lens-model:` (not `reviewed-by-model:`). A content baseline was taken outside the workspace before it started (`git diff --binary HEAD` empty at `b351494`; 120 untracked files checksummed). The model-routing guard allowed the spawn. This round's internal lens is therefore not a Fable floor review. The adjudicator's rulings and the sign-off are separate Fable steps; the owner's direction names only the review.
- **Owner answer, 2026-10-19.** Asked "After the Opus review returns, should the adjudication and the sign-off of the Stage B briefs also run on Opus, or wait for Fable?", the owner chose **"Opus, same as review"**. The other options offered were "Wait for Fable" and "Opus only this round" (later reviews back on Fable). The Orchestrator's working reading, stated to the owner for correction: while Fable is unavailable, S145's reviews and rulings run on Opus through the sanctioned `general-purpose` route; once Fable is available again, they return to Fable. Each such step is recorded here.
- **Cycle 2, the internal lens on Opus** (`opus-lens-model: claude-opus-5-5`; the full text is in `.claude/plans/s145/b0/opus-step0b-c2-review.md`): **NOT READY, no BLOCKER.**
  - **Read-only, verified.** The tracked files and the 120 untracked files are byte-identical to the baseline.
  - **What it confirmed:**
    - all six cycle-1 internal warnings and the thirteen notes are CLOSED;
    - contract 7 is right, and its Fact 12 holds: no existing stub engine turns red;
    - replaying a split month is safe, and RP-B1 can be built as specified;
    - FX3 is re-derived: 6.00 credit and 8.00 flex balance.
  - **Open:**
    - WARNING 1: F-1 is confirmed, so EB-1 = 14;
    - WARNING 2: the Step 7a whitelist must become a per-pass table of the labels each pass was authorized to use;
    - WARNING 3, which Codex missed: C-4's red-CI path breaks C-5's "`git diff K <close>` is empty" check and the no-push rule; and any edit to a file a frozen patch touches invalidates the byte-for-byte check, not only an edit to a mutated method;
    - NOTEs 1–9.
  - **Where it differs from Codex:** the class doc of `EmploymentTruncationAlignmentTests` goes to TASK-14522-T, as a comment-only edit at B.2; and the pasted standing-constraints block can stay.
- **The adjudication of cycle 2 on Opus** (owner-directed; rulings R-80 onward) was dispatched on 2026-10-19, with a content baseline taken.
- **The cycle-2 rulings, R-80..R-92** (`adjudicated-by-model: claude-opus-5-5`, owner-directed; read-only, verified; no owner question). The full text is in `.claude/plans/s145/b0/adjudication-cycle2.md`.
  - **R-80:** F-1 is accepted, so EB-1 = **14 facts**. SR1 = `ManifestReplayTests.ReplayAsync_ReturnsResult_WithOriginalManifestId_AndMatchingShape` and SR2 = `FrozenInputsSlotTests.Replay_ProjectionRowAbsent_LoadsFrozenInputsFromEvent_Succeeds` are added. Two success-only straddle replays become EB-1 expected greens.
  - **R-81:** Step 7a item 7 becomes a **per-pass table** of the labels each paired pass was authorized to use on a changed *existing* value, built from this log. It replaces R-65's list: `R21` was never a label, and `F1`, `R5(a)`, `R-6`, `R-10`, `R37`, `R-11..R-30` and the rest are now in their passes' rows. No blanket approval and no relabelling. The pasted standing-constraints block stays unchanged.
  - **R-82:** the fifth truncation fact (`:313`) falls under R-73, so R-73's class covers five facts. TASK-14522-T owns the two production-wiring sentences of `EmploymentTruncationAlignmentTests`, as a comment-only edit at B.2, where they become untrue.
  - **R-83 (amends R-53): the close sequence's failure path.**
    - C-1 records the patch base `B` and saves the patches as raw bytes, with a round trip.
    - C-2 checks patch validity at every new K (`git diff --name-only B K -- src tests frontend docker tools`). If anything changed, the dry runs are redone and the patches re-cut, then re-frozen if they differ.
    - C-4 gets three red branches: a re-run for a non-build failure; a docs-only fix; a code fix, which goes back through the merge checklist, a new K and a scoped Step 7a.
    - **The no-push freeze starts at the green push.**
    - C-5 checks byte-identity with `cmp`.
  - **R-84:** if Stage A ships alone, QUAL-209 and QUAL-211 are WITHDRAWN, and QUAL-210 is re-worded to the A8 drift.
  - **R-85..R-91:**
    - R-85: 14523's copy is adjusted at its return (its 5a is a SKIP), and the dead "another id" hedges are removed;
    - R-86: the MU-43 re-run is certain;
    - R-87: RP-B1 uses one PCS and one recorder;
    - R-88: FX3's flex is settled month-wide (149.40 + 6.00 − 147.40 = 8.00);
    - R-89: the QUAL-210 hire fact's payroll half is illustrative;
    - R-90: Step 7a item 6 names all four patches;
    - R-91: two more comment-only edits.
  - **R-92: cycle 3 is a scoped confirmation by both lenses** (Codex and the Opus internal lens), checking:
    - absorption fidelity;
    - R-83's close sequence;
    - R-81's table against the tests;
    - the comment-only additions;
    - R-87 and R-89.
- **The planner's revision 2: done** (report in `.claude/plans/s145/b0/B0-revision2-report.md`; the rev 1 → rev 2 diff in `b0/rev1-to-rev2.diff`). All 13 rulings were applied, with the verbatim texts copied by script, and the standing-constraints block is byte-identical everywhere. **Two items need a ruling:**
  - **The hash length.** A patch's `index` lines carry abbreviated hashes, and Git lengthens them from 7 to 8 characters at 16,384 packed objects. The repository is at 16,076, and packing its 523 loose objects would cross that. So R-83's byte-for-byte `cmp` could fail on correct patches. The proposal is `--full-index` on every save and compare.
  - **A forward pointer.** R-82(a)'s comment, written at B.2, points to `SplitMonthPayrollTests`, which only exists from B.3. The proposal is to point to 14522-T's own B.2 tests.

  Also noted: three spots outside the checklist; the plan and brief wording on expected greens differ; and R-83's trigger looks only at `src tests frontend docker tools`.
- **Scoped Step-0b, cycle 3 (R-92), dispatched 2026-10-19:**
  - Codex (the external lens);
  - the internal lens on Opus (owner-directed; read-only, with a content baseline taken).

  The adjudication on Opus follows.
- **Cycle 3, both lenses:**
  - **Codex: NOT READY, narrowly** (`b0/codex-step0b-c3-verdict.md`).
    - Passes: absorption fidelity, R-81's table, and R-87/R-89.
    - Open: `--full-index` (confirmed by experiment: the same commit pair gave 7- and 8-character `index` hashes); the patch-validity path set must cover `.github/`, `Directory.Build.props` and `StatsTid.sln`; the forward pointer, option (a); and TASK-14528:139's wording.
  - **The internal lens on Opus: NOT READY, no BLOCKER** (`b0/opus-step0b-c3-review.md`; read-only, verified).
    - W1: `--full-index`, confirmed.
    - W2: red branch (c) would be refused by the sprint-close guard while master's last push run is red. The fix: push the fix and the new K green first, then the close commit, and define "the green push" as the close commit's.
    - W3: one "code set" pathspec, excluding only `docs`, `.claude` and `*.md`.
    - W4: a re-cut must not overwrite the frozen patch; sha256 records.
    - W5: new Docker facts added by a fix enter the census.
    - W6: **RP-B1's replay sends no login token**, so it would get a 401 from the real Rule Engine and fail on correct code. It must pass a minted bearer token, as the forward call does.
    - N1–N7. **N7, for later:** the close guard requires the Step 7a review artifact to show the Fable model id. If Step 7a also runs on Opus, the close will need a waiver or an owner decision.
- **The cycle-3 adjudication on Opus** (owner-directed; rulings R-93 onward) was dispatched on 2026-10-19, with a content baseline taken.
- **The cycle-3 rulings, R-93..R-105** (`adjudicated-by-model: claude-opus-5-5`, owner-directed; read-only, verified; the full text is in `.claude/plans/s145/b0/adjudication-cycle3.md`).
  - **R-93:** every patch is saved and compared with `--full-index --default-prefix`.
  - **R-94:** "the code set" is every tracked path except `docs/`, `.claude/` and the root-level `*.md` files, as one pathspec. It replaces the close sequence's path list everywhere: C-2's validity check, C-3's routing, C-4's split and C-5. It is checked to catch a past CI-workflow change. R33's revert contract is unchanged.
  - **R-95:** the B.2 class-doc comment names the B.2 classes that pin split months (`EmploymentWindowLiveRulesetTests`, `RecalcBlockedLiveRulesetTests`), with no forward reference.
  - **R-96:** any Docker fact added or changed by a code-set fix after the freeze enters the census.
  - **R-97 (amends R-83's C-4).** These were walked against `sprint-close-guard.ps1`:
    - in red branch (c), the fix and its new K are pushed and must come back green before a new close commit is made;
    - "the green push" is the close commit's (or a docs-only (b) commit's above it), never a K's;
    - only the close commit's command may contain the words "sprint close" (not a K, a fix, an evidence commit or C-9);
    - a new branch (d), anything else, halts for a ruling;
    - no waiver.
  - **R-98 (amends R-83's re-freeze):**
    - a re-cut is saved beside the frozen patch, never over it;
    - each frozen patch's SHA-256 is recorded here, and re-checked at C-3 and C-5;
    - `B` becomes K mechanically when every re-cut is byte-identical and nothing else changed; otherwise the seat re-freezes;
    - C-3's rescope covers re-frozen lists.
  - **R-99:** RP-B1 passes a minted bearer token on both the forward call and the replay.
  - **R-100:** a fallback after the green push replaces the close of record; the close re-runs in its Stage-A-only form.
  - **R-101:** a label is the ids it names, and qualifiers add none. 14513-T's relocated assertion (`BoundaryCauseEncodingTests.cs:231-232`) is outside item 7.
  - **R-102, R-103:** the line range and method name, and the stale spots.
  - **R-104 (recorded neutrally).** The close guard accepts the Step 7a internal review only if it ran on Fable (`claude-fable-5-1`). If Fable is still on its limit at C-1 (the B.3 gate), the owner is asked then, with three options worded in the ruling: wait for Fable; run it on Opus with a guard change for S145, which is itself reviewed; or run it on Opus under the all-gates waiver. Not decided now.
  - **R-105:** after revision 3, a scoped cycle 4 by both lenses checks only:
    - absorption;
    - R-97's C-4, walked against the guard;
    - R-98's flow;
    - R-94's pathspec on `0016f33`.
  - **Observed on 2026-10-19 (it confirms R-97):** an Orchestrator docs commit was blocked by the guard. The command wrote this summary inline, so its text held the quoted phrase next to "S145", and the guard reads the whole command text. The text now goes in through a file.
- **The planner's revision 3: done, with no contradiction needing a ruling.**
  - All 13 rulings (R-93..R-105) were applied by a Node script, which wrote nothing until every old text had been found exactly once.
  - The consolidated close-sequence block, the new "C-1 again" section and R-100's bullet are byte-identical to the ruling's fenced text.
  - The code-set pathspec and the `--full-index --default-prefix` flags read identically at every site: 4 and 3 sites in the plan, 4 and 4 in TASK-14528.
  - The pathspec, run read-only on `0016f33`, lists `.github/workflows/ci.yml`.
  - The diff is in `.claude/plans/s145/b0/rev2-to-rev3.diff`.
- **Scoped cycle 4 (R-105), dispatched 2026-10-19:**
  - Codex (the external lens);
  - the internal lens on Opus (owner-directed; read-only, with a content baseline taken).

  They check only four things: absorption; R-97's C-4, walked against the close guard; R-98's re-cut and SHA-256 flow; and R-94's pathspec. If both are READY, or have only mechanical findings, the seat signs at cycle 4. A BLOCKER would use cycle 5, the cap.
- **Cycle 4, both lenses READY:**
  - **Codex: READY** (`b0/codex-step0b-c4-verdict.md`).
    - No new findings; all nine briefs READY; every cycle-3 item CLOSED.
    - Absorption passes (all 81 added diff lines present).
    - R-97's C-4, walked against the guard, passes.
    - R-98's flow passes.
    - R-94's pathspec lists `ci.yml`.
  - **The internal lens on Opus: READY** (`b0/opus-step0b-c4-review.md`; read-only, verified).
    - All nine briefs READY, and all of cycle 3's W1–W6 and N1–N6 are CLOSED.
    - It walked every prescribed commit of C-4 through `sprint-close-guard.ps1`. The guard passes the normal close and the (c) and R-100 closes, and does not see the K, fix, (b), evidence or C-9 commits. The no-push window holds.
    - It left one mechanical WARNING, W1: the records written at C-2 after K must ride in the close commit, or the guard refuses the close as stale.
    - It left five mechanical NOTEs: create the `cb1/mut/` directories before saving; record all four SHA-256 values at a re-freeze; R-100 moves the old patches aside; name the guard's other gates (worktrees, untracked files, the ledger); and wording. Each has verbatim fix text.
- **The cycle-4 sign-off, R-106..R-113** (`adjudicated-by-model: claude-opus-5-5`, owner-directed; the full text is in `.claude/plans/s145/b0/adjudication-cycle4.md`). Every lens finding was accepted, some with adjusted text:
  - **R-106:** the records written at C-2 after K ride in the close commit, or in the next K, so K stays the close commit's parent.
  - **R-107:** `mkdir -p` before every patch save.
  - **R-108:** a re-freeze records every patch's SHA-256.
  - **R-109:** the Stage-A-only form first moves the Stage B freeze to `cb1/mut/stage-b/`.
  - **R-110:** C-4 names the guard's other gates. No worktree may stand beyond the main checkout, and no untracked file may sit under `src`, `tests`, `tools` or `frontend`. Every TASK id needs a ledger disposition, including a bare `TASK-14503` line, because the guard reads `14503a`/`b` as `14503`.
  - **R-111:** five wording fixes.
  - **R-112:** R33's revert contract at plan `:686` now reads as it stands after CA-7: the diff against `K_A` lists exactly QUAL-207's two files, and a revert must not take QUAL-207 with it.
  - **R-113: SIGNED.** This covers the nine Stage B briefs (TASK-14520, -14520-T, -14521, -14521-T, -14522, -14522-T, -14523, -14524, -14528) and the plan's Stage B and close sections. No cycle 5.
- **Applied verbatim by the Orchestrator** with a script that replaces each old text only if it occurs exactly once, and writes nothing unless every edit matches. All 23 edits matched, and two revision rows were added. The diff is in `.claude/plans/s145/b0/rev3-to-rev4.diff`.
- **B.0 is closed: 2026-10-19, after four Step-0b cycles.**
  - Cycles 2–4's internal lens, the adjudications and the sign-off ran on Opus by the owner's direction, because Fable was on its usage limit. Codex was the external lens in every cycle.
  - Still open: R-104's close-guard question (asked at C-1, only if Fable is still on its limit), and the O-7 routing row at C-9.


## Wave B.1 (dispatched 2026-10-19; the build-safe order, R-78: at most one building agent at a time)

**TASK-14524, authored** (`test-qa`; branch `s145/task-14524`, `5ee23e4`; not merged, by design: it merges at B.3).

*Plain language:* these are the tests that define "done" for Stage B, written from the specification before the code exists.

- **Compiles today, observed RED:** `BackdateWorklistResidualDerivationTests` (Unit) fails 17 of its 19 cases. The two that pass are Monday context days that are null today and stay null.
  - The failures are the worklist's current derivation: QUAL-149/QUAL-150 instead of the residual QUAL-209, or null.
  - The QUAL-210 drift pair fails as the brief expects.
  - The matrix's hand-derived expected blocks hold on all 11 rows, which confirms the reading of Q5 (c).
- **Compiles today, Docker** (CI-verified, not locally green; the Regression build has 0 errors):
  - `SplitMonthPayrollTests`: WS1, WS2, QA1, QP1, FX1, FX2, FX3, RP-B1;
  - `SplitMonthHostTests`: SP1, IB3, RB3, RB4;
  - `WorklistResidualBlockTests`: WR1 (3 cases), WR2, WR3.
- **Written blind** (they name members Stage B creates): `SinglePlanEvaluationMergerTests`, `RuleRegistryStageBClassificationTests`, `SplitMonthPcsTests` (MB-3, MB-8, MB-10c, and MB-12's two-case Theory), and the appends to `PayrollPlanRefusalProblemTests` and `HttpRuleClassificationProviderTests`. They are proven RED by Unit mutation at B.3.
- **The spec figures were re-derived:** the Q2 week 2.67/4.53 and 7.20; WS1 flex +7.20; FX3 6.00 and 8.00; FX1; FX2 (170 + 20 = 190, under the DEPARTMENT_HEAD cap of 200 but over AC's 150).
- **Deviations reported:**
  - a second, type-check-only Regression build;
  - the Unit PCS fixture cannot map lines, so MB-8's merged-row fact uses line-free answers, and Docker covers the lines;
  - the Q-A Theory observes the in-memory store directly;
  - the leaver fact builds PCS with a capturing logger;
  - the `PeriodNotCalculableException` constructor shape is assumed, and reconciled at B.3;
  - RP-B1's live-state change is an in-place update;
  - stream ids are literals, because `ManifestStreamId` is internal;
  - test-only wage-type rows are seeded inside the facts.

  No brief contradiction.

**TASK-14520 + 14520-T, merged** (`53a7812`; the ledger row has the review record).

*Plain language:* overtime, the 37-hour norm check and flex now carry a new label, "timeline-aware". It means "evaluated once across the whole month, with each day's own contract; never split into parts". The merge step for such a rule passes its one answer through unchanged. Two answers would mean the design broke. The merger then throws a bug error, never a split refusal. *(Corrected at the close, from the O-4 drafting: payroll catches that error and records a failed rule row while the calculation still reports `Success`, rather than answering a 500. It is unreachable with the live rules; whether such a failure should refuse the whole calculation is recorded as an open question in ADR-016 D10.)* Nothing pays differently yet. Split months are still refused until B.2; the refusal now names the weekly norm check (`NORM_CHECK_WEEKLY`) instead of overtime.

- **Review.** 5α PASS. Codex APPROVE. The internal lens ran on Opus by the owner's direction and returned APPROVED-WITH-WARNINGS. Every finding was a comment that the change had made false: W-1 (two comments still said flex merges by a custom rule), N1 and N2 (comments elsewhere naming overtime as a split-sensitive rule, or "four" such rules), and N6 (a re-wrap). They were applied at merge, comments only, in `5715f7e` (build 0 / 145).
- **5a NOTE-3:** the two-answers failure must not look like a split refusal. Pinned on TASK-14524's branch (`ce269f3`), as two asserts in `SinglePlanEvaluation_TwoResults_Throws_NamesRuleAndCount_NoDate`: `IsSplitRefusal` is false and `SplitRefusalRuleId` is null.
- **Owed to TASK-14522-T (Opus NOTE-5):** hand-built test rule sets still model flex as Mergeable/Custom. 14522-T's R-63 comments may say so; it is not a value change.

**TASK-14521 returned** (`backend-infrastructure`; `s145/task-14521`, `1f084e1`, on `6416f6f`).

*Plain language:* the worklist's "cannot recalculate" mark now follows payroll's own week rule. Only one case is marked: an agreement change on a Tuesday to Sunday inside a Monday–Sunday week the month pays (`QUAL-209`). Profile, category and Monday agreement changes no longer block, because Stage B lets payroll pay those months part by part. The old rule missed two cases that are now caught: a change on a weekday 1st, and a change in the previous month's last days, which shows on the next month's row. The server's 409 and the Global-Admin gate (QUAL-165) are unchanged; they call the same function. Hires and leavers are still judged without their employment dates (QUAL-210).

- **Build and tests:** 0 errors / 145 warnings · CA2100 115 · Unit 1467 of 1477 (10 red, all in `BackdateWorklistDerivationTests`, TASK-14521-T's file, as designed) · DemoSeed 170 · Regression non-Docker 128.
- **Docker reds predicted from the code:** five repository and endpoint facts. Two more pass vacuously until 14521-T re-seeds them. All are in 14521-T's files.
- **Cross-check:** TASK-14524's residual matrix (`5ee23e4`), run against `1f084e1` in a throwaway checkout, passes 19 of 19, including both QUAL-210 drift facts. Against master it was RED 17 of 19.
- **Departures from the hints:** `IsStrictlyInsideMonth` is kept, because its fencepost fact still calls it; its docstring now says the derivation no longer uses it. The predicate call sits in a private helper method.
- **Reported, no change needed:** the refinement's line numbers are stale; the brief's facts all matched. Text that names the old ids:
  - `WorklistList.tsx:48`: TASK-14523's file.
  - `BackdateWorklistRowResolved.cs:44` and `EmployeeProfileEndpoints.cs:909`: Fact 6.
  - `init.sql:4916, :4980`: `{QUAL-149}` as an example stamp, still a valid historical stamp.

**Orchestrator rulings on the report (R-114..R-116), given to TASK-14521-T at dispatch:**
- **R-114, a correction to TASK-14521-T item 1.** The brief put 30 April 2026 "inside weeks April settles". It is a Thursday in the week Monday 27 April – Sunday 3 May. Under Q3 F1 (the month containing the week's Sunday settles it), that week belongs to May, so April's AGREEMENT row on the 30th expects `null`, not the residual. TASK-14524's spec-derived matrix already says so. The rows on the 15th and the 1st (both Wednesdays) expect the residual.
- **R-115, re-seed the two SETTLED_YEAR facts.** Both seeded a profile change, which no longer blocks, so they passed whether or not the kind check exists: a test that cannot fail proves nothing. Each moves to a mid-week agreement change, labelled `// S145 B3:`:
  - the factored-overload fact moves to Wed 19 Aug 2026. Its mutation M-4 must again trip it, demonstrated in a throwaway checkout;
  - the row-overload fact moves to Wed 12 Mar 2025, with a report on whether that overload can be tripped at all.
- **R-116.** The docstring of the repository's version-guard fact (`HrBackdateWorklistRepositoryTests.cs:719-731`) names a mutation in a method the derivation no longer calls. It may be reworded, docstring text only.
- **Noted, no change:** the profile halves of `…OnlyEdgeTriggers_Empty` and `…DateBeforeTheMonth_…` now pass trivially. Their agreement halves still test the rule.

**TASK-14521-T, dispatched** (`test-qa`, on `s145/task-14521`; told not to take master's newer commits, because the branches merge separately).

**TASK-14521-T returned** (`f563dbf`; only its three test files changed). On the branch: build 0 errors / 145 warnings · CA2100 115 · Unit 1477 · DemoSeed 170 · Regression non-Docker 128. The Docker worklist facts compile and are CI-verified. Every re-seed and re-expectation follows the brief and R-114..R-116.
- **R-115's mutation, demonstrated** in a throwaway checkout: dropping the kind check (M-4) makes the factored SETTLED_YEAR fact fail with `["QUAL-209"]`.
- **The row-overload SETTLED_YEAR fact cannot trip.** Its row has no year or month, which is the stored shape of every SETTLED_YEAR row (the schema requires it), so the overload answers empty before it reaches the kind check. Recorded, and not forced.

**Review of TASK-14521 + 14521-T** (5α and 5a on the Opus lens, by owner direction; 5a Codex for the correction paths). The baseline taken before the Opus lens was unchanged afterwards.

*Plain language:* both reviewers found the production change correct. They re-derived every expected date by hand from the calendar. The GET, the 409, the stamp, the event and the audit row all use one function, and no other code works out the block a second way. Every finding was about labels, comments or something to register.

- **5α FAIL, on labels only.** Three changed stamp values in the endpoint tests had no `// S145 B3:` label, and five were labelled only in passing prose. All eight changes were authorized by brief item 3. Labelled at merge in `4563abc`; the Orchestrator re-checked that no code line changed.
- **Codex W1 = Opus N-1, the interim between B.1 and B.2. No change.** Until TASK-14522 merges, a month with a mid-month profile or category change is no longer marked blocked, but payroll still refuses it. So a Global Admin could record RECALCULATED on a month that cannot yet be recalculated. Codex: harmless on a branch, not harmless if this ran live. It never runs live: master is pushed only at the sprint's close, so B.1 and B.2 reach `origin` together, and if Stage B is abandoned, TASK-14521 is reverted with the rest (R33). Nothing becomes wrong after B.2: those months become payable. Nobody tests the worklist by hand on split months until B.2 merges.
- **Codex W2 + Opus W-1 and W-2 (labels; red conditions).** The endpoint gate, stamp and GET facts now say that MB-6r trips them. The stale-If-Match fact says MB-6r does not trip it, because the 412 comes first. Applied in `4563abc`.
- **Opus W-3 → R-117 (Orchestrator), what MB-6p means.** The briefs define it as "PROFILE and CATEGORY return QUAL-149 *again*" (TASK-14521, TASK-14528). That is Stage A's profile arm restored: QUAL-149 only strictly inside the month, never on the 1st. The other reading, "block on every date", is a different mutation that nothing ruled.
  - So the two Docker facts seeded with a PROFILE change on the 1st do not rest on MB-6p. Their docstrings said they did, and are corrected.
  - TASK-14528's MB-6p row is unchanged.
  - For TASK-14524 re-spawn 2, which runs MB-6p: of the 14521-T matrix rows, only PROFILE 15, PROFILE 30 and CATEGORY 15 trip. The rows on the 1st derive null either way; 14521-T's report said five rows. The August fencepost trips on the 2nd and the 31st, as its docstring says.
- **Opus W-4 → QUAL-212, registered.** The worklist judges only the open row's own triggers. Payroll judges every agreement change in the month, including one already resolved on an earlier row of the same month. It is not new in S145, and the fix belongs with QUAL-210's. The derivation's doc now says the two agree "on each trigger's own date".
- **Stale prose, applied in `4563abc`:** Opus W-5 (the derivation tests' class doc, the fencepost pin's summary and body comment, the TriggerOnFirstOfMonth docstring), N-2, N-5 (`ReopenFlagsNextMonthTests.cs`) and N-6 ("before S145 Stage B" for the retired ids).
- **Not applied:**
  - N-3, a branch in the August theory no row now takes: harmless, and it keeps the theory's shape;
  - N-4, `IsStrictlyInsideMonth`, now called only by its own fact: retire both together later;
  - Codex NOTE, `WorklistList.tsx:47-48` and its old-id fixtures: TASK-14523's file, carried to its dispatch.

## B.1 gate — closed 2026-10-19

- **Merged:** TASK-14520 + 14520-T (`53a7812`) and TASK-14521 + 14521-T (`fe928dde`). TASK-14524 is authored (`5ee23e4`, `ce269f3`) and merges at B.3, by design.
- **O-1 at `fe928dde`:** full rebuild, 0 errors / 145 warnings · CA2100 115 · Unit 1478 · DemoSeed 170 · Regression non-Docker 128.
  - A comment-only build of the 14521 branch had shown 144. The one missing warning is the existing CS0618 in `Payroll/Program.cs`: an incremental build skips an unchanged project and does not re-print its warnings. So O-1 ran with `--no-incremental`.
- **O-2:** `openapi.json` regenerated with no drift, and `npm run gen:api` with no drift.
- **The handoff into B.2.** TASK-14522 (`payroll-integration`) makes payroll hand the planner only the rules it runs, which is what makes split months payable. Its paired pass TASK-14522-T follows on the same branch and carries Opus NOTE-5 from TASK-14520 (hand-built test rule sets still model flex as Mergeable/Custom).

## Wave B.2 (dispatched 2026-10-19)

**TASK-14522 returned** (`payroll-integration`; `s145/task-14522`, `39d60f86`, on `1a1d3cb8`).

*Plain language:* a month in which an employee's hours, position or agreement changes partway through can now be paid by the calculating routes, each part under its own wage-type codes. Payroll used to hand the planner the Rule Engine's whole rule list. That list includes three rules payroll never runs (the weekly norm and two rest rules), and they still say "never cut a month in two". Payroll now hands over only the six rules it runs, none of which refuses a split.

Three smaller changes come with it:
- the rule report shows overtime, norm and flex as the engine's one answer for the month;
- the raw export routes, which map every line under one profile, refuse a two-part month under their own reason, `split-month-single-profile-route`;
- a line dated in no employed part now fails with a 500 and nothing written, instead of being quietly filed under a neighbouring part's codes.

- **Build and tests:** 0 errors / 145 warnings · CA2100 115 · Unit 1478 · DemoSeed 170 · Regression non-Docker 128, all green. Every flip TASK-14522-T owns is in a Docker-gated suite.
- **Cross-check:** TASK-14524's nine authored files compile against the branch in a throwaway checkout (Unit 1512, all green, +34). The refusal exception takes the shape the pins assume (R-76). No shape disagreement was found.
- **Departures from the hints:**
  - `ABSENCE_CALC` is a literal in the evaluated set;
  - the timeline-row map is built at the merge call;
  - the boundary causes are collected first-seen, in date order;
  - Q6 (a) is written inline in the raw-route method;
  - one comment dash line just outside an authorized range was removed.
- **No contract deviation and no brief contradiction.** Two line drifts were reported.
- **Reported for the review and the merge:**
  - stale comments outside the file list: `HttpRuleClassificationProvider.cs:50-68`, and the inline catch at `Payroll/Program.cs:~697`;
  - a pre-existing SharedKernel doc mismatch on segment causes, `PeriodPlanner.cs:119-124`;
  - methods changed that Fact 10 names for no mutation, for C-1's method diff to check: `MergePerSegmentRuleResults`, `SplitBySegment`, `SegmentIndexForLine`;
  - a SinglePlanEvaluation rule with no timeline row becomes a failed rule row under `Success`. That is the existing behaviour (Fact 4), and the live rule set cannot reach it.

**TASK-14522-T, dispatched** (`test-qa`, on `s145/task-14522`), carrying Opus NOTE-5 for its R-63 comment rewording.

**TASK-14522-T returned** (`61edc9dc`). Eight test files changed, none under `src/**`. The Orchestrator's full rebuild on the branch: 0 errors / 145 warnings · CA2100 115 · Unit 1478 · DemoSeed 170 · Regression non-Docker 128.

*Plain language:* the tests that pinned "payroll refuses a split month" now pin what Stage B ships.
- A mid-month hours change calculates, with one timeline call across both parts.
- A Monday agreement change recalculates with HK codes on 1–15 March and AC codes on 16–31 March, and exports under them.
- A mid-week agreement change answers the week-check refusal.
- The raw routes refuse a split month with their own reason.

Every flipped fact is Docker-gated, so it is CI-verified. The fixture conditions C-B1..C-B4 hold, as reported, and every flip names its TASK-14528 mutation.

- **R-118 (Orchestrator), a fixture addition accepted.** 14522-T added recorded March weekday hours to XB1 and XB2: 7.4 h on each weekday from 2 to 31 March. The reason: `/calculate-and-export` writes an export record only when the calculation has lines, and the host reads its own recorded hours (R14). On the brief's seed alone, the asserted "one export record" could not happen. No expected value changed. Both review lenses are asked to verify:
  - that the table is the one the host reads;
  - that C-B1 still holds (no per-line wage-type assertion);
  - that the mutations tripping these facts are unchanged.
- **Not a latent bug.** 14522-T flagged that the old `SeedSuccessorWageTypeMappingAsync` omitted `effective_from`, which is `NOT NULL`. The Regression harness's own DDL runs first and gives the column a default (`TestFixtures.cs:583`, `DEFAULT '2020-01-01'`), so the old seed worked, which matches CI being green at `K_A`. The new explicit date is harmless.
- **Left for the review:**
  - stale prose in `PayrollHostRecalcBlockedTests.cs`: the (d) header, the (e) note, the (c3) docstring;
  - two assert helpers now without callers;
  - an unused import in `LocalProfileActivationKeyTests.cs`.

**Review dispatched:** 5α and 5a on the Opus lens (owner direction), and 5a Codex (payroll export), on `1a1d3cb8..61edc9dc`. A content baseline was taken first.

**Review of TASK-14522 + 14522-T.** Codex: APPROVE-WITH-WARNINGS. Opus lens: APPROVED-WITH-WARNINGS, 5α PASS. Neither found a blocker. The baseline was unchanged afterwards, apart from the Orchestrator's own docs commit.

*Plain language:* both reviewers traced the money path end to end and found it correct.
- Every line is filed under the part of the month that holds its date, and mapped with that part's own wage-type key.
- A split week's overtime is dated on its Sunday, so it maps under the Sunday part's codes (Q3 F1).
- Flex is settled once for the whole month.
- Replay neither refuses a split month nor drifts from the forward run.
- The Opus lens recomputed the worked example exactly. At fraction 0.811 the per-day norm is 6.00 and the per-day upper threshold 6.49. The week's norm is 2×7.40 + 3×6.00 = 32.80 and its threshold 2×8.00 + 3×6.49 = 35.47. HK pays `OVERTIME_50` 2.67 and `OVERTIME_100` 4.53, dated Sunday 20 September; AC pays 7.20.

Both lenses confirmed R-118: the seed targets `time_entries_projection`, which `ProjectionHoursReader` reads; C-B1 holds; the mutation reach is unchanged. The Opus lens also refuted the `effective_from` "latent bug", agreeing with the Orchestrator.

- **Opus W-1 (production comments this merge made false, outside the file lists).** Applied at merge in `8992a4ce`, comments only:
  - the calculating handlers' planner-refusal catches no longer cite a mid-month agreement change;
  - the raw guard's catch names both 422 variants;
  - "unavailable is not empty" gives the Stage B reason (R-91), in `Program.cs` and `HttpRuleClassificationProvider.cs`.
- **Opus W-2 + Codex WARNING (stale test prose and red conditions).** Applied in `8992a4ce`:
  - in `PayrollHostRecalcBlockedTests.cs`: the (c3) docstring, the (d) block and red conditions d1/d2 (an empty set now answers the Q6 (a) 422, not a 200 export), the (e) note, and IB1;
  - in `RecalcBlockedLiveRulesetTests.cs`: the HK-only sentence.
- **R-119 (Orchestrator; Codex WARNING = Opus N-2), a correction to TASK-14528's MB-4 row, case (d).** R-58 predicted that IB1 under MB-4 "calculates and exports; delivery 404 → anonymous 422". IB1 seeds no recorded hours, so the calculation has no lines and the route skips the export: it answers 200, with a manifest and no export record, and the 422 status assertion trips. The red set is unchanged. The brief was amended (revision row "B.2"), and IB1's docstring says the same.
- **Opus N-1.** The two assert helpers left without callers (`AssertRedactedProblem`, `AssertPlannerRefusal`) are removed in `8992a4ce`. They are the only non-comment lines in that commit.
- **Opus N-3.** The planner's own doc on segment causes was wrong before this sprint. It said every segment names its leading edge; the code gives every segment but the last its trailing edge, and the last its leading edge. Corrected in `PeriodPlanner.cs`, comment only. The Q6 (a) cause list is equal to the planner's by construction.
- **Opus N-4.** A `// S145 B2:` line label was added on the successor-mapping SQL.
- **Opus N-7.** `EvaluatedRuleIds`' doc names a latent asymmetry. Planning decides on snapshots from the six-rule set, while replay re-checks snapshot contracts against the whole list. No rule declares a contract today.
- **Recorded, no change:**
  - **Codex NOTE = Opus N-6.** When the merger fails a rule, the result is a failed report row under `Success`. This is existing behaviour, kept by contract 2, and the live registry cannot reach it: a missing timeline row is refused earlier (`TimelineReplyDefect`), and the export lines are mapped before the merge. It is below the register's Medium floor, so it is noted for the ADR-016 D10 amendment at O-4: should a merger invariant failure refuse the whole calculation?
  - **Opus N-5.** XB1 also asserts the correction-event count, which does not bring it onto MB-2's list.
  - **Opus N-8.** LB1's "no per-segment timeline-rule call" assertions lack a positive control. They are not vacuous today, because the body's `ruleId` is captured. The control is a Docker-fact change that cannot be run locally, so it is left for a later tidy.
  - **Opus N-9.** A mid-month local-profile activation month is now paid on the calculating routes. No figure changes, because a local profile never reaches payroll's arithmetic (QUAL-204). The raw routes refuse it under Q6 (a), whose sentence is slightly loose for this cause; no fact seeds one.
  - The unused import in `LocalProfileActivationKeyTests.cs` was left alone.

## B.2 gate — closed 2026-10-19

- **Merged:** TASK-14522 + 14522-T (`f6fac97b`). The merge fixes are in `8992a4ce`; the branch rebuild was 0 / 145, CA2100 115, Unit 1478, Regression non-Docker 128.
- **O-1 at `f6fac97b`:** full rebuild, 0 errors / 145 warnings · CA2100 115 · Unit 1478 · DemoSeed 170 · Regression non-Docker 128. `openapi.json` regenerated with no drift; the payroll host is not in the published spec.
- *Plain language:* **split months are now payable on master.** QUAL-149 stays REGISTERED until the close. Like QUAL-186 at Stage A, it is marked BUILT after the Stage B evidence runs match.
- **Next, B.3, in the plan's order:**
  1. TASK-14523 (`ux`, the residual badge), frontend only, never alongside a `dotnet` build;
  2. TASK-14524 re-spawn 1 (reconcile the authored pins), then its merge;
  3. re-spawn 2 (the Unit mutations, one at a time; R-117's reading of MB-6p);
  4. C-1.

## Wave B.3 (dispatched 2026-10-19)

**TASK-14523 returned** (`ux`; `s145/task-14523`, `d93aee6a`, on `f0fb1e65`).

*Plain language:* the worklist's "Genberegning blokeret" notice now explains, in Danish, the one reason left since Stage B, and keeps the register id beside it. Behaviour is unchanged: the notice, the suppressed card and the Dismiss-only rule still follow `recalcBlockedBy`.

The rendered notice, quoted for the owner (R-85):
- **For a QUAL-209 row:** `Genberegning blokeret — QUAL-209: overenskomstskift midt i en uge — ugen kan ikke afregnes efter to overenskomster`.
- **For an unknown id:** the id alone, `Genberegning blokeret — QUAL-999`.

- **Re-seeds:** the old-id fixtures in `WorklistList.test.tsx` now use `QUAL-209`, labelled `// S145 B3:`. One new test was RED first: before the change it received `Genberegning blokeret — QUAL-209`.
- **5α PASS (Orchestrator):** two files, both authorized. On the branch: tsc exit 0 · vitest **1004** (81 files; +1). 5a SKIP, by the plan's table: pure UI text.
- **Checked by the Orchestrator:** the UI never displays `resolutionBlockedBy` outside the generated types, so a stamp made before Stage B (QUAL-149/150) cannot surface unexplained.
- **Copy question, put to the owner at return (R-85):** the "recalc blocked" message after a refused resolve (the 409) still shows ids only. Neither the brief nor R-77 covers it.
- **The merge is held** until the owner has seen the copy.

**TASK-14524 re-spawn 1, dispatched** (`test-qa`): rebase onto master `f0fb1e65`, reconcile names with the as-built members, and change no expected value. It must check R-117 (MB-6p seeds strictly inside the month) and the R-118 lesson (a host fact asserting an export record needs recorded hours), and report a STOP rather than seed.

**Owner ruling on TASK-14523's copy (2026-10-20, at the task's return, R-85).** The owner chose to **also explain the 409**. The "recalc blocked" message after a refused resolve renders each id through the same helper as the row notice, so QUAL-209 carries the same Danish explanation and an unknown id shows alone. One RED-first test is added. The agent was resumed on the same branch for this follow-up, frontend only.

**TASK-14524 re-spawn 1 returned** (`s145/task-14524`, rebased onto `f0fb1e65` with no conflicts: `5ee23e4` → `6f6ce59b`, `ce269f3` → `ca7a9c5c`; plus `767d4631`, three docstrings that cite `PeriodCalculationService` by member name instead of drifted lines).
- **Names and values.** No name needed reconciling: every member and shape the pins assumed matches what was built, including R-76's exception constructor. No expected value changed, and there was no STOP.
- **R-117 holds.** WR1's PROFILE and CATEGORY cases seed Wed 15 April, strictly inside the month, so they rest on MB-6p as the brief says. The Monday case on 13 April rests on MB-6.
- **The hours check holds.** SP1 seeds weekday hours. RB3 and RB4 post raw-route lines and assert a 422 with no record. IB3 persists its own record. No host fact asserts lines or a record on a seed without hours.
- **The Orchestrator's full rebuild on the branch:** 0 errors / 145 warnings · CA2100 115 · Unit **1512** (1478 + 34) · DemoSeed 170 · Regression non-Docker 128.
- **5a dispatched.** The internal lens is on Opus by owner direction, and Codex is added as a second lens: the plan routes pin tasks to the internal lens only, but that lens is below the usual review model this sprint. Scope is `f0fb1e65..767d4631`. A content baseline was taken first.

**TASK-14523 merged** (`8efdfffd`). The follow-up `820434ab` explains the 409 message too, as the owner ruled: `Genberegning er blokeret (QUAL-209: overenskomstskift midt i en uge — ugen kan ikke afregnes efter to overenskomster) og kan ikke markeres som løst. Brug "Håndteret manuelt", hvis sagen er rettet på anden vis.` Its new test was RED first.
- 5α PASS: two files, both authorized, and one rendering line changed.
- O-1 at `8efdfffd`, frontend only: tsc exit 0 · vitest **1005**. The .NET side is unchanged since `f6fac97b`.

**TASK-14524, 5a cycle 1.** Both lenses returned **REQUEST-CHANGES** on the same blocker, which makes it convergent. The baseline was unchanged afterwards, apart from the Orchestrator's own commits.

*Plain language:* every figure in the Stage B tests matches the specification. Both lenses re-derived each one from the refinement and the agreement reference, not from the code: the Q2 week, WS1's +7.20, FX1 97, FX2 190, FX3 6.00/8.00, QA1's 3.00 + 3.00, and the worklist matrix. One test setup mistake would have made two Docker facts fail in CI on correct code.

- **B-1 (BLOCKER, both lenses).** QA1 and QP1 seed the AC `MERARBEJDE → SLS_0310` mapping and assert that exactly one row was inserted. The class applies the full `init.sql`, which already seeds that open row (`init.sql:242`), so the insert does nothing and the setup assert throws before any payroll assertion runs. The brief's premise ("the fixture lacks the AC rows") holds for the HK-only test fixture but not for `init.sql`.
- **R-120 (Orchestrator), the routing:**
  - **B-1 fix:** the seed helper becomes idempotent and asserts that the open row has exactly the expected wage type. QP1's DEPARTMENT_HEAD row stays. QA1's docstring is corrected. No assertion in QA1 or QP1 changes, and MB-2 and MD-30 still discriminate.
  - **Docstring corrections:** Opus N-1 (MB-12 trips by exact-type mismatch, `NullReferenceException`, and TASK-14528's MB-12 row is amended to match); Opus N-2 = Codex NOTE (FX2 under MB-3 closes at 150.00 with a 20.00 payout, not 170); N-3 (QA1's MB-2 site); N-4 (the fact count).
  - **N-5:** RP-B1 asserts that its live profile update touched one row.
  - **Accepted, no change:** Opus N-6 = Codex WARNING. The MB-8 Unit pin carries no lines, a deviation declared at B.1; MB-8 still trips, and SP1 and QA1 (Docker) carry the lines. N-7, a duplicated count, stays.
- **The fix-up is dispatched** to the same agent, on the same branch. Cycle 2 re-reviews it with both lenses, scoped to the fix-up's diff.

**TASK-14524, 5a cycle 2** (the fix-up `25f224ef`): **Codex APPROVE; Opus lens APPROVED, 5α PASS.** The baseline was unchanged.
- **B-1 is resolved.** Every seed call agrees with `init.sql`, and QA1 and QP1 now reach their payroll assertions.
- **MB-2, MD-30, MB-7 and MB-13 still discriminate.** The docstring corrections are true against master.
- **Two Opus NOTEs, applied at merge in `d28a97b8`, comments only:**
  - the fix-up's comments said "S145 B3 (R-120)". R-65 reserves "S145 B3:" for the refinement's section B3 (the worklist re-expectations), so they now say "S145 R-120";
  - the seed helper's summary now says it ensures the open row.

**TASK-14524 merged** (`8a54484d`). O-1 at `8a54484d`: full rebuild, 0 errors / 145 warnings · CA2100 115 · Unit **1512** (+34) · DemoSeed 170 · Regression non-Docker 128. The Docker facts are CI-verified, at the close's evidence runs.

*Plain language:* every Stage B task is now merged. The tests that define "done" for Stage B were written from the specification before the code. They now sit on master beside the code they check, and every figure they assert was re-derived from the specification by both review lenses.

**Owner ruling on R-104 (2026-10-20): option 2, "Opus + an S145-only guard change".** A probe at the B.3 gate found Fable still on its usage limit (HTTP 429, request `req_011CgD3bBiF9dj4nb9tsZLnS`), so R-104's question was put to the owner.

*Plain language:* the script that guards the close commit accepts the end-of-sprint internal review only if it ran on Fable. The owner chose to run that review on Opus, and to change the script to accept Opus for S145 alone, with the direction recorded. Every other close check stays switched on, and the script change is itself reviewed in S145's Step 7a.

The alternatives were:
- waiting for Fable, an open-ended delay;
- the existing waiver file, which would have switched off every close check at once.

- **The change** (Orchestrator, governance tooling): in `.claude/hooks/sprint-close-guard.ps1`, the reviewer-artifact model check accepts `claude-opus-5-5` when the sprint is 145, and the review floor otherwise. The telemetry line marks an exception pass as one. The comment cites this ruling and says to delete the exception after the S145 close; that removal is owed after the close.
- **Its tests** (`.claude/hooks/test-sprint-close-guard.ps1`):
  - **T23:** S145 accepts Opus at the model check.
  - **T24:** S145 still refuses a cheaper model.
  - **T21** already pins that any other sprint refuses Opus.
  - **Safety:** the two S145 tests write SPRINT-145 artifacts into the real reviews folder. Any real artifact is saved first and restored byte for byte.
- **A harness gap found and fixed.** The S143 worktree-teardown gate brought a test seam for the reserved sprint S99, but the harness never set it. So every test that expects the hook to *allow* a close passed only on a repository with no worktrees, and failed mid-sprint: T5, T7, T9, T11, T12, T14 and T16–T20 all blocked on the 28 real worktrees. The harness now sets that seam to `clean`, as it does the others. **Recorded neutrally, not fixed here:** the worktree gate still has no test of its own in the harness.
- **Result:** all 24 harness tests pass.

**TASK-14524 re-spawn 2, the Unit mutation run: 12 of 12 Stage B mutations and 5 of 5 re-run Stage A mutations tripped their pins.** The run was in a throwaway checkout at `c488b2d3` (baseline Unit 1512 green), with the whole Unit suite run under each mutation. No compile error, no reworded mutation, no test edited; the checkout was removed and pruned.

*Plain language:* each Stage B test was shown to fail when the behaviour it guards is broken on purpose, and each break turned red only the tests expected, plus a few related Unit tests named below for C-1.

| Id | Site, re-found on master | Mutation as applied | Captured failure (named pin) | Red set |
|---|---|---|---|---|
| MB-1 | `CalculationResultMerger.MergeBySinglePlanEvaluation` | `Count == 1` → `Count >= 1` | `SinglePlanEvaluation_TwoResults_Throws_…`: "No exception was thrown" | 1 (the pin) |
| MB-3 | PCS `DeriveEvaluationInputsAsync` piece loop | a piece is added only for the first EMPLOYED segment | `TwoEmployedSegments_TimelinePiecesCoverBothSegments`: "Filter not matched" (only 23–28 Feb and 1–15 Mar) | 3: both named pins + **collateral** `EmploymentWindowSegmentSkipTests.FlexCarry_…` |
| MB-6 | `RecalcBlockedByForTrigger`, agreement arm | → `IsStrictlyInsideMonth` | weekday-1st and context-day facts expect `QUAL-209`, get null; Monday and leaver facts the reverse | 13: 11 residual-file facts + the 14521-T April matrix AGREEMENT rows for day 1 and day 30 |
| MB-6r | same | the residual arm returns null | "Expected QUAL-209, Actual null" | 14: 11 residual-file facts (the hire fact included) + 14521-T's AGREEMENT day 15 and day 1 rows, `…IsASet_SortedDistinct`, `…EqualsRowOverload…` |
| MB-6p (R-117) | same, PROFILE and CATEGORY arms | `QUAL-149` when strictly inside the month | `ProfileAndCategoryChangeInsideExportedMonth_DeriveNoBlock`: "Expected null, Actual QUAL-149" | 9: the Theory's 2 cases + PROFILE 15, PROFILE 30, CATEGORY 15, August days 2 and 31, the set and row-overload facts. **The rows on the 1st stay green, as R-117 predicted** |
| MB-8 | PCS `MergePerSegmentRuleResults` | the merge gets the per-segment rows | `…MergedRowIsTheTimelineRow`: "OVERTIME_CALC produced 2 results but … requires exactly one" | 1 |
| MB-9 | `HttpRuleClassificationProvider` converter | the `SinglePlanEvaluation` arm deleted | both Theory cases: `RuleClassificationsUnavailableException` | 2 |
| MB-10a | `RuleRegistry.ResolveDefault` | TimelineAware → `RejectIfMultipleSegments` | "Expected SinglePlanEvaluation, Actual RejectIfMultipleSegments" | 5 |
| MB-10b | `RuleRegistry` constructor | `OVERTIME_CALC` registered AlignedWindow | rows read "Actual AlignedWindow"; refusal lacks `NORM_CHECK_WEEKLY` | 9: incl. **collateral** `RuleRegistryStageBClassificationTests.ShippedRegistry_PayrollTimelineRules_…` |
| MB-10c | `RuleRegistry` constructor | the `NORM_CHECK_37H` registration removed | "Registry inventory drift. Missing: [NORM_CHECK_37H]"; "No RuleClassification registered…" | 4: incl. the same **collateral** fact (`KeyNotFoundException`) |
| MB-11 | `PayrollPlanRefusalProblem.TryCreate`, the Q6 (a) arm | (i) `reason` omitted; (ii) a date appended to `error` | (i) `…HasReasonCountsAndCauses_NoRuleId`: no `reason` key; (ii) `…SerializedJson_HasNoDateNoEmployeeId`: "Match found 2026-03-16" | (i) 1; (ii) 2 |
| MB-12 (R-56, R-120) | PCS `SegmentIndexForLine` | the throw → the old fallback | both Theory cases: "Exception type was not an exact match … NullReferenceException", **as R-120 predicted** | 2 |
| MU-43 (re-run) | PCS `EvaluateAsync`, after `Normalize` | throw on an entry after `PeriodEnd` | both `MonthEndCrossingTests` night-shift pins | 2 |
| MU-46 (re-run) | `EvaluateAsync` | `previousFlexBalance` → `0m` | `FlexCarry_…`: expected 12.5, actual 0 | 1 |
| MU-44 (re-run; found by the method diff) | `EvaluateAsync` refusal gate | the R-6 gate reverted | both R-6 pins: "Assert.False() Failure" | 5 (the R-6 pins + three timeline-reply facts with the same assertion) |
| MU-26, MU-27 (re-run; found by the method diff) | `PayrollPlanRefusalProblem.TryCreate`, week arm (rewritten from a switch into `if` blocks by TASK-14522) | `reason` omitted; a date appended | `WeekVariant_…NoRuleId`: no `reason`; `WeekVariant_SerializedJson_…`: "Match found" | 1 each |

- **Excluded by method diff, which showed zero lines changed:** MU-36, MU-40, MU-45, MU-28, MU-29, MU-30 and MU-38. Also excluded: MU-25, whose `PeriodPlanner` change is comments only, and MU-20, MU-21 and MU-37, whose `RuleRegistry` hunks touch only the constructor, `ResolveDefault` and comments. The other MUs' sites are not in `923f7fb..master`'s changed set.
- **MB-11's wording.** TASK-14528 words MB-11 as "reason omitted (or the date appended to `error`)". Each form trips one of its two pins, so both forms were run, as MU-26/MU-27 were split at Stage A.
- **For C-1:** the collateral Unit reds above (MB-3, MB-10b, MB-10c, MB-6r) are Unit-level and touch no Docker run. The Docker rows resting on these mutations (WR1–WR3, SP1) are CI-verified only.

## B.3 gate — closed 2026-10-20

- **Merged:** TASK-14523 (`8efdfffd`), TASK-14524 (`8a54484d`). Re-spawn 2 is complete, as above.
- **O-1 at `8a54484d`:** 0 / 145 · CA2100 115 · Unit 1512 · DemoSeed 170 · Regression non-Docker 128 · vitest 1005. Since then master carries only docs and the `.claude/hooks` guard change (`e2ce7dfc`), both outside the code set (R-94).
- **Next: C-1** (TASK-14528, `test-qa`):
  - resolve EB-1, EB-2, EA-R1 and EA-R2;
  - the R-51 census on the merged code;
  - re-find MB-5, MB-7 and MB-12;
  - dry-run each set and save the four patches as raw bytes against `B`;
  - the R39 set;
  - attach the table above.

  Then the seat freezes the lists, the patches and `B`. By owner direction the seat runs on Opus: the `adjudicator` cannot run below Fable, so it goes through the sanctioned general-purpose route.

**TASK-14528 C-1, dispatched** (`test-qa`).

## C-1 — the Stage B evidence, resolved and saved (2026-10-20)

*Plain language:* most of Stage B's new tests need a database, which this machine cannot run. So after the close commit is pushed green, CI runs four throwaway commits, each breaking the code in known ways, and must turn red exactly the tests listed below. C-1 settled those lists on the merged code and rehearsed each run locally.
- **The rehearsal:** every run built cleanly, and Unit, DemoSeed and the non-Docker Regression tests stayed green, as they must: CI stops at the first red step.
- **The patches:** each run's breakages are saved as a patch file. The end-of-sprint review reads those files, and CI applies them byte for byte.
- **The full report** is kept, untracked, at `.claude/plans/s145/cb1/C1-report.md`.

**`B`** = `2a981597dedb45add37c60e817f40cba5eee4b96` (local master at C-1; master moved only by docs after it).

**The four patches**, each a diff of `PeriodCalculationService.cs` only, saved with `--full-index --default-prefix`. Each round-trips byte-identical (`cmp` exit 0). The Orchestrator recomputed the SHA-256 independently, and it matches:

| Run | Mutations | Patch (`.claude/plans/s145/cb1/mut/`) | Bytes | SHA-256 |
|---|---|---|---|---|
| EB-1 | MB-2, MB-4, MB-5, MB-13 | `EB-1.diff` | 2846 | `632b8749ce5d52094eba2271d94e4b0a38169d907b16f3d483e2a342027a3f92` |
| EB-2 | MB-7 | `EB-2.diff` | 964 | `4830beb362b26ceae34c3c4d5e8c6e158f172c6818f14d7fe76e4088d6a337cd` |
| EA-R1 | MD-25, MD-30 | `EA-R1.diff` | 1692 | `ba047098fad92e3dd9ca5a6174b08556e452e437f74c07878398e65864f3174e` |
| EA-R2 | MD-24 (as frozen in R-35) | `EA-R2.diff` | 2782 | `d25cb78f15743b3a58e0984417cc6d2bbd9a883ed3e617e7cc2445b79d2304a7` |

**The dry runs** (one throwaway checkout at `B`): each run 0 errors / 145 warnings · CA2100 115 · Unit 1512 · DemoSeed 170 · Regression non-Docker 128.

**The red lists** (keys; each key's FullyQualifiedName is in `cb1/lists/<run>.red.fqn` and `cb1/keys-all.tsv`):
- **EB-1 (14):** QB1, QB2, QA1, QP1 (MB-2) · IB1, IB2, IB3, RB4 (MB-4) · RB1, RB2, RB3 (MB-5) · RP-B1, SR1, SR2 (MB-13).
- **EB-2 (22):** LB1, QB1, QB2, QB3, XB1, XB2, RB1, RB2, RB3, RB4, IB1, IB2, IB3, SP1, WS1, WS2, QA1, QP1, FX1, FX2, FX3, RP-B1.
- **EA-R1 (10):** AR3, AR4, AR7, AR8, RB4 (MD-25) · WP12, QB1, QB2, QA1, QP1 (MD-30).
- **EA-R2 (4):** IB2, H10, X3, X4.

**The expected-green lists** are EB-1 61, EB-2 65, EA-R1 66 and EA-R2 80 facts, in `cb1/lists/<run>.green.fqn`. 155 of 159 keys resolve; the four that do not are 14522-T's renames, and the lists use the new names (H9 → IB2, AR10 → IB1, X1 → XB1, X2 → XB2).

**The census and the check lines.**
- The census was by diff since `923f7fb` and by predicate: each mutation's edited condition, its cases, and the facts per case.
- The R-69 facts and F-1 are confirmed on the merged code.
- All five check lines PASS on all four runs. There is no unlisted red, no mutation that misses its assertion, and no Unit or DemoSeed red.

**The R39 set.**
- MD-25 and MD-30 changed method, and MD-24 is in by ruling. Their lists were re-derived on the merged code.
- The MUs whose method changed (MU-43, MU-44, MU-46, MU-26, MU-27) were all re-run by TASK-14524 re-spawn 2.
- The Unit mutation table above is attached.

**Findings, none a red** (put to the seat):
- **F-C1:** one worklist Docker fact rests on MB-6r and MB-6p, but its docstring does not name them (R-72's first condition).
- **F-C2:** SR2 and Stage A's guard FS-G1 are one FullyQualifiedName, which C-5's compare keys must map.
- **F-C3:** `TASK-14524.md:63` still has the pre-R-120 wording of MB-12.
- **Brief corrections:** the stale line cites; XB1/XB2 belong to MB-2 case (d), not (c); 7D is missing from MB-2's case lists.

**The seat's rulings R-121..R-127 and the freeze** (2026-10-20). The seat ran on Opus by the owner's direction (`opus-seat-model: claude-opus-5-5`), read-only; the content baseline was unchanged afterwards.

*Plain language:* the seat checked each saved patch against the code. Each does exactly the breakages the plan assigns to its run, on the right lines, and nothing else; the seat applied each one in memory and matched the patch's own file hash. It re-checked the riskiest predictions itself (IB1, RB4, SR1/SR2, XB1/XB2, 7D) and found the report right. Then it froze everything, with no change to any list or patch. Its findings were paperwork; one of them, R-121, adds a step before the close.

- **Patch verification:** each hunk matches the brief's wording on the as-built site.
  - EA-R1's and EA-R2's replacement lines are byte-identical to Stage A's `ca3/EA-1.diff` and `ca3/EA-4.diff`.
  - MB-5 appears as `< int.MaxValue` because the as-built check is an early return; it is the same mutation.
  - All eight list files resolve. Red and green lists do not overlap.
- **R-121 (F-C1). Add the missing docstring now, before K.** `HrBackdateWorklistRepositoryTests.WriteForExportedMonths_AppendOnOpenRow_…` rests on MB-6r (which gives `[]`) and MB-6p (which gives `["QUAL-149","QUAL-209"]`), and R-72 requires its docstring to say so.
  - The seat chose this over a tidy after the close: that would leave a rule broken at the close and a commit that no review covers.
  - The cost: a test file changes, so "C-1 again" runs at C-2. Because `PeriodCalculationService.cs` is untouched, the re-cut patches are expected to be byte-identical.
  - Applied in `e68077da` (comment only). TASK-14528's MB-6r and MB-6p rows name the fact.
- **R-122 (F-C2, plus a second case the seat found).** C-5's keys file has one key per test name:
  - the shared FrozenInputsSlotTests name maps to `SR2` only (Stage A's `FS-G1` is the same test);
  - `BoundaryCauseEncodingTests.ForwardCalcWrite_And_Rebuild_…` maps to `BC-4` only;
  - there are no rows for renamed-away names;
  - a `uniq -d` check must print nothing before the first comparison, because `compare.sh` keeps the last row for a repeated name.
- **R-123 (F-C3).** `TASK-14524.md`'s MB-12 wording follows R-120.
- **R-124 (the brief corrections).** TASK-14528 is amended, not superseded, because "C-1 again" starts from its table:
  - the sites at `B`;
  - MB-2 case (c) gains A3-3 and loses XB1/XB2;
  - case (d) names XB1, XB2, 7D and FX2 as reached but green;
  - the R39 MD-30 line names them as EA-R1 greens.
- **R-125.** The four renames are confirmed in the lists; no edit.
- **R-126 (the seat's own finding).** IB1's stale "(guard)" tag now says it is a RED pin. Applied with R-121, in `e68077da`. Recorded only: C-1's census listed `Resolve_ForeignHr_…` as reached by MB-6r, but it is a plain expected green.
- **R-127, the freeze.** **FROZEN:** `B` = `2a981597dedb45add37c60e817f40cba5eee4b96`; the four patches by the SHA-256 in the table above; and the lists by these SHA-256 (untracked files in `.claude/plans/s145/cb1/lists/`):

| List | Facts | SHA-256 |
|---|---|---|
| `EB-1.red.fqn` | 14 | `55df9f349f26c5c03c96049929a047c817bfa37af7ff402aca36da04f6a17d17` |
| `EB-2.red.fqn` | 22 | `9c43f99d10c4a782c29a49d05b35635b1078f0da2543a130bab3eb8af3106943` |
| `EA-R1.red.fqn` | 10 | `1fe9c65ae368ea8484c11215f022c947b6be8d674639baf8d9856ce7db82baa5` |
| `EA-R2.red.fqn` | 4 | `a0c451123f7486d89f104af3f03c31583778b6ab7cba0cff34cb864302ae7c41` |
| `EB-1.green.fqn` | 61 | `7603dbcd04c60211bceb9d07a9f61c497a1891cebeef2f3cf9f24a3e06427938` |
| `EB-2.green.fqn` | 65 | `6e29a84316e15c6763d25113e49582f95089b1fc0c540a6d83c342421559b679` |
| `EA-R1.green.fqn` | 66 | `bd0826d03f72e04cddabc25c549f6142bb2b382faa9c407da304200f9798d2cd` |
| `EA-R2.green.fqn` | 80 | `df0358e49f42041822ecabd0de0653398c94ff11fc2dfd1e20930d3c44fe49fa` |

The Orchestrator re-computed all twelve SHA-256 values; they match. The brief edits (R-121's two table cells, R-122..R-124) are applied in `.claude/plans/s145/TASK-14528.md` and `TASK-14524.md` (untracked), with a "C-1" revision row in each.

**C-2 outcome (2026-10-20): the freeze stands, and `B` becomes K = `a9beb01fcfa7aaac0c8bf569fe7982ad5afa68fa`** (R-98; no seat ruling needed).
- **What changed.** The code-set diff `B..K` was exactly three files: R-21's marker doc, R-126's IB1 tag and R-121's docstring. All three are doc comments, with no executable line changed and no test name moved.
- **"C-1 again" (TASK-14528).** It re-classified IB1 (still red under MB-4 and MB-7) and the AppendOnOpenRow fact (Unit-resting, on MB-6r and MB-6p; no PCS reach). No census entry, list, expected-green set or check line changed. The Regression test-name inventory is identical (2148).
- **The re-cuts on K.** Each frozen patch applied cleanly on K. All four dry runs were green: 0 / 145 · Unit 1512 · DemoSeed 170 · Regression non-Docker 128. Each re-cut, in `.claude/plans/s145/cb1/mut/a9beb01fcfa7/`, is **byte-identical** to its frozen patch (`cmp` exit 0; same SHA-256), and each round trip exits 0. The frozen files are untouched.
- *(Recorded in the working tree after K, as C-2 says; it rides in the close commit.)*

*(The paragraph below is the plan as written at C-1, kept for the record.)* **Next: C-2.** K is made. "C-1 again" re-cuts the patches on K, because R-121 changed a test file (R-96: steps 2–3 for every Docker row). If the re-cuts are byte-identical, `B` becomes K with no seat ruling (R-98). Then C-3, the Step 7a review on K: Codex, and the internal lens on Opus under the owner's R-104 ruling.

## C-3 — Step 7a, the whole-sprint review (2026-10-20)

**Before dispatch:** each frozen patch's SHA-256 was checked against the latest freeze; all four match. Base `cbaa5e0`; K = `a9beb01f`.

**Cycle 1 (on K = `a9beb01f`).** The artifacts are `.claude/reviews/SPRINT-145-step7a-{codex,reviewer}.md`. The content baseline was unchanged after the internal lens; it disclosed one stray write outside the repository, deleted at once.

- **Codex: APPROVE-WITH-WARNINGS.** Checklist items 1–9 PASS; item 10 is a WARNING (a pin is owed). It independently re-verified the four frozen patches (hashes, in-memory application to K, scope). Its findings:
  - **W1:** T23 could pass without reaching the model check.
  - **W2:** item 10's refusal-order pin is owed.
  - **N1:** the relocated replay assertion is vacuous on an empty list.
  - **N2:** an unused PCS wrapper, `BuildPlanForLegacyCallersAsync`.
- **The internal lens (Opus, by the owner's R-104 ruling): DO-NOT-CLOSE.** Items 1–8 PASS; item 9 PASS WITH WARNING; on item 10, "no pin owed".
  - **B-1 (BLOCKER):** a Skema save for an open month can carry `Entries`/`Absences` cells dated in a locked month, and since TASK-14507 it replaces them. Only WorkTime was month-bounded (S56), and the supersede SQL has no period condition.
  - **W-1:** `/calculate-and-export` uses the unfloored scope check, against the S76 invariant. It predates S145.
  - **W-2:** three ADRs overstate what replay reads; it does read the dated wage-type mapping.
  - **N-1..N-10.**

*Plain language:* both reviewers found the sprint's payroll logic sound end to end, and both independently confirmed the frozen evidence patches. The internal reviewer found one real hole, which the Orchestrator verified in the code: a Skema save for an open month could change a cell in a month that is already approved and locked. Before S145 such a cell was added as a duplicate; since S145 it replaces the locked value, and payroll now reads those values itself. Codex did not see it. The fix is small and mirrors the rule WorkTime has had since S56.

**The Orchestrator's rulings on cycle 1** (by owner direction, rulings run on Opus while Fable is unavailable; each fix is reviewed again at cycle 2 by both lenses):
- **R-128 (B-1): fix now. Not a known-accepted hole, because it breaks the approval lock, an invariant.**
  - **The fix:** before the transaction, refuse any `Entries` or `Absences` date outside the requested month, with 400 `skema_cell_date_out_of_range`, exactly as WorkTime's S56 check does.
  - **The pin:** a RED-first Docker fact, `SkemaCellDateRangeGuardTests`.
  - **The register:** a SEC row, FIXED.
  - **No legitimate flow is lost:** the grid posts only in-month dates, and DemoSeed saves per month.
- **R-129 (W-1): fix now, as a one-line role floor (`StatsTidRoles.LocalAdmin`), with a mixed-role 403 Docker fact and a SEC row, FIXED.** It rides R-128's new K. It predates S145 (TASK-1902), but since R14 the route reads and returns the target's recorded hours itself.
- **R-130 (item 10, where the lenses differ): add the pin.** Codex holds it owed. The internal lens holds a wrong order harmless, since both answers are 422s that write nothing. The pin is cheap in this batch and settles the question.
- **R-131 (the rest):**
  - **Ride the fix batch:** N-1, `Assert.NotEmpty` before the `Assert.All` (BC-4 is an EB-1 expected green, so C-1 again re-classifies it); N-6, a stale test comment; N-7, `IsStrictlyInsideMonth`'s doc says it is kept as MB-6's substitute.
  - **Docs:** W-2, N-5, N-8 (two SEC rows, REGISTERED) and N-9 are drafted for the Orchestrator to apply.
  - **The harness:** Codex W1 = Opus N-2. T23 now requires positive evidence: a temporary S145 CI-health waiver whose line proves the run passed the model check. A skipped test is reported as such, not counted in "ALL 24". The hook gains a telemetry seam, `STATSTID_TELEMETRY_LOG_MOCK`, honoured for any sprint because telemetry is not a gate. The three stray `close | S145` telemetry lines are annotated in the local log.
    - **Result:** 24 of 24 pass. The real S145 artifacts were restored byte-identical, and the live log did not grow.
  - **N-3:** deleting the S145 guard exception is added to the handoff's next actions.
  - **Accepted, no change:** N-4 (a pre-S145 CI-health gap).
  - **Noted for C-5:** N-10 (build the keys file per R-122).
  - **Deferred to S146: Codex N2, the unused `BuildPlanForLegacyCallersAsync`.** Removing it changes `PeriodCalculationService.cs`, the file every frozen patch edits, so the patches' context and line numbers would shift and need a re-freeze, for dead code. It is recorded here for S146's first tidy, below the register's Medium floor.

**The fix batch so far.** The `test-qa` pins are `c9f87124`:
- `SkemaCellDateRangeGuardTests`: three facts, the entry case, the absence case and an in-month control;
- `PayrollCalculateScopeFloorTests`: two facts, the 403 and the control;
- `SkemaQuotaRefusalOrderTests`, the item-10 pin. It uses a test-only resolver wrapper that serves exactly one null to the endpoint's Phase-1 read, and asserts that it did, so a wrong guess fails visibly. The cycle-2 lenses are asked to judge that seam.
- N-1 and N-6 (N-6's file is in the Smoke project).

B-1's fix is `69b80585` (`backend-infrastructure`):
- the month bound for `Entries` and `Absences`, beside the S56 WorkTime check;
- the comments it made false;
- N-7's doc comment.

**R-132 (Orchestrator): the month bound stays, even though seven existing Docker facts posted cross-month absence batches.**
- **The consequence.** `backend-infrastructure` found seven facts in four files that post cross-month batches: quota-exhaustion bookings of 25–27 days, plus two facts whose subject was the cross-month batch itself (`Vacation_BoundarySpanningBatch_AnchorsOnFirstAbsenceDate`, `OkStamping_BatchStraddlesOk24Ok26Boundary_…`). Under the fix they get the 400.
- **Why the bound still stands.** The only product caller of the save, `useSkema.saveMonth`, builds every save from one month's grid. DemoSeed saves per month, and no other endpoint writes Skema cells. So no product capability is lost; only test scaffolding used cross-month batches. The alternative, checking the lock of every month a save touches, would keep those batches but rewrite the in-lock authoritative check in a security-critical path at the close. That was rejected.
- **The re-shape (`test-qa`).** The quota facts become one save per month, in date order, with the same asserted outcome and name. The two boundary facts are re-shaped as in-month saves where their behaviour is still reachable, or converted to pin the refusal and renamed, with the reason recorded.

**Registered for S146 (from `backend-infrastructure`'s report): QUAL-214, a Skema month-navigation race.** Navigating months does not flush the pending save queues.
- **Cells:** since the fix, an old-month cell sent after the 1-second debounce fires is refused (400). The edit is dropped with an error banner, where before it was written under the wrong month's lock.
- **Work time:** a pre-S56 risk. `workTimeDirtyRef` is never cleared on load, so a refused old date re-queues and blocks later work-time saves until the page reloads.
- **The fix:** flush or drop the queues on month navigation, in the frontend.

**Recorded, no change:** the supersede SQL in both projection repositories still has no period condition. It is safe only because of this endpoint check, a defence-in-depth point recorded in B-1's SEC row.

**The re-shape** is `a57bb057` (`test-qa`, R-132):
- **The five quota facts** keep their names and outcomes, now as one save per month through a helper that requires every earlier save to be 200. Their "nothing written" check moves to the first date of the refused save.
- **Two boundary facts are renamed:**
  - `OkStamping_BatchStraddlesOk24Ok26Boundary_PerRowEntryDateResolved` → `OkStamping_AcrossOk24Ok26Boundary_PerRowEntryDateResolved`: two in-month saves, the same stamp assertions;
  - `Vacation_BoundarySpanningBatch_AnchorsOnFirstAbsenceDate` → `Vacation_CrossFerieaarBatch_Refused400_NothingWritten`: it now pins the refusal.

**W-1's fix** is `06caca3e` (Orchestrator, one call): `ValidateEmployeeAccessAsync(…, StatsTidRoles.LocalAdmin, ct)`. The branch's full rebuild: 0 / 145 · CA2100 115 · Unit 1512 · DemoSeed 170 · Regression non-Docker 128.

**The fix batch, 5α and 5a cycle 1** (`a9beb01f..06caca3e`):
- **5α PASS** (Orchestrator, then confirmed by the Opus lens).
- **The Opus lens: APPROVED.** It traced B-1's 200 before the fix and its 400 after, recounted the re-shape splits, and grepped all 61 files that call the save to confirm none posts a cross-month batch.
- **Codex: APPROVE-WITH-WARNINGS.** Both confirmed that B-1 and W-1 are complete, that the pins discriminate, and that the re-shapes keep their purpose. The baseline was unchanged.
- **The item-10 seam, where the lenses differ.** Codex flags its stack inspection as fragile and recommends explicit DI. Opus traced it as deterministic under .NET 8 Release (async `MoveNext` frames are never inlined) and would keep it, noting DI as the stronger form.

**R-133 (Orchestrator): take the complete fix now, as a test-only fix-up.**
- The seam moves to explicit DI: the real resolver, the calculators built with it, and only `IEmploymentProfileResolver` armed.
- N-a: the in-month control's dates move to the 1st and the 31st, pinning the inclusive bounds, and the 400 message is checked for the offending date.
- Codex's NOTE: three docstrings now say "nothing from the refused monthly save persists".
- N-c: the stale header of `CalculateAndExportScopeTests.cs`.
- **To QUAL-214:** N-e (the new 400 message is English, as S56's is, and the UI shows it raw).
- **Noted for C-2's "C-1 again":** N-d (the two renames are on the Regression inventory lists).

Cycle 2 re-reviews the fix-up, scoped to its diff.

**Merged and K2.** The fix batch merged at `8fda8415`; its 5a ran two cycles on both lenses, and its ledger row is above. **K2 = `64456c7b`** carries the fix, the doc fixes (W-2, N-5, N-8 as SEC-051/052, N-9, SEC-049/050, QUAL-214), the harness fix and the C-2 record of K1.

**C-2 on K2 (2026-10-20): the freeze stands, and `B` becomes K2 = `64456c7b2c1036eaffe1107a522bac2967e8fe58`** (R-98; no seat ruling needed).
- **What changed.** The code-set diff `B..K2` is 16 files, and `PeriodCalculationService.cs`, the one file every patch edits, is not among them.
- **The census.** "C-1 again" re-checked every Docker row under R-96:
  - the six new Docker facts are guards outside every mutation's reach;
  - the seven re-shaped facts are Skema and balance facts with no PCS reach;
  - every payroll host fact on a list uses a GlobalAdmin GLOBAL token, which W-1's LocalAdmin floor still admits;
  - BC-4 stays green under every run (under MB-13 its replay still has segment 1's rows);
  - the two renames appear only in inventory files, not on any red or expected-green list.
- **The re-cuts.** All four re-cuts on K2, in `.claude/plans/s145/cb1/mut/64456c7b2c10/`, are byte-identical; the dry runs are green and the round trips exit 0.

**C-3 cycle 2 (Step 7a scoped to the fix, `a9beb01f..64456c7b`).**
- **The Opus lens: CLOSE.** Every cycle-1 finding of both lenses is resolved or recorded as deferred; notes N-A..N-F.
- **Codex: REQUEST-CHANGES, on one new BLOCKER in the Orchestrator's own harness fix (B2-1).** Its fix table otherwise agrees with the Opus lens.
  - **The problem.** The telemetry seam `STATSTID_TELEMETRY_LOG_MOCK` took a *path*, and `Add-Content` created any file there. Pointed at `SPRINT-<N>-ci-health-WAIVED.md`, it would have created the waiver, and the CI-health gate, which runs just after, would have been skipped.
  - **Why the Opus lens missed it.** It reasoned that no gate *reads* the telemetry log, and missed that the path seam could *create* a file that a gate reads. The Orchestrator verified the control flow, so the blocker stands.

*Plain language:* while fixing a small problem in the close guard's own tests (they wrote fake "close passed" lines into a log), the Orchestrator added a test switch that took a file path. Codex saw that the path could name a waiver file, and that writing it would switch off one of the close checks. The switch now only turns the logging off; it can no longer write any file.

**R-134 (Orchestrator): the seam becomes a switch.**
- **The guard.** `STATSTID_TELEMETRY_SUPPRESS=1` writes nothing and echoes the would-be line to stderr. The old path variable is gone from the guard.
- **The harness.**
  - **T23** now takes that echo as its positive evidence. The echo is reached only after the model check passes, so the temporary ci-health waiver is gone, and with it Opus N-D's leftover-waiver risk.
  - **The new T25** pins the negative case: the hook no longer reads the old variable, and with the old variable pointed at the S99 waiver path, no waiver appears and no waiver line is printed. *(Corrected at cycle 3: this line first said "and the CI-health gate runs", which K3's T25 did not show. An absence of the waiver line would also hold if the hook stopped earlier; Codex's cycle-3 WARNING and Opus N3-1. R-135 adds the positive evidence.)*
  - **The banner** requires all 25 PASS lines and no skipped test. Its skip check is case-sensitive, so only the explicit `SKIPPED` marker counts. *(Corrected at cycle 3, N3-2: the reason first given here, that T25's own label contains "skipped", was true only of a draft label, `ci-gate-not-skipped`, renamed before K3.)*
- **Result:** 25 of 25 pass; the real S145 Step 7a artifacts were restored byte-identical; no S99 or S145 waiver was left; the live telemetry log did not grow.
- **The cycle-2 notes, applied:**
  - **N-A:** the SEC-048/049/051/052 and QUAL-214 cites at K2;
  - **N-B:** QUAL-214 names the more common silent-loss path and says "since S56";
  - **N-C:** ADR-017's sentence points to ADR-016 D10's exception;
  - **N-E and N-F:** the handoff below.

**K3 = `664763d1`** carries R-134 and these records. The code set is unchanged from `B` (= K2) to K3, so C-2's check is empty and the frozen patches stand.

**C-3 cycle 3 (Step 7a scoped to R-134, `64456c7b..664763d1`).**
- **Codex: APPROVE-WITH-WARNINGS.** B2-1 is closed; no new gate bypass and no new file write that a gate reads.
  - **The WARNING:** T25's run-time half asserted only an absence (no waiver line), which an earlier refusal would also give.
  - **Two NOTEs:** a killed run can still leave the fake S145 artifacts; the banner's stated reason above is stale.
- **The Opus lens: CLOSE-WITH-WARNINGS.** It confirms B2-1 closed. It traced every environment variable the hook reads and every file it can write: only the fixed telemetry log, which no gate reads. Nothing parses the hook's stderr but the harness.
  - **W3-1, older than K3:** from T23's first write until T24's, the two fake S145 artifacts named HEAD and the accepted Opus id, so the pair passed every artifact check. The real ones were saved only in memory. T23's live `gh run list` call sits inside that window, so a tool timeout there would have killed the run before its `finally`, leaving fakes that pass the gate and the real artifacts lost.
  - **N3-1** is Codex's WARNING. **N3-2** is Codex's second NOTE.
  - **N3-3:** `docs/WORKFLOW.md` still says "14 cases" and that every seam is S99-only, and does not name the any-sprint switch.
  - **N3-4:** the harness leaves `%TEMP%\statstid-guard-test-neutral-log.md` behind on every run (since S141).
  - **N3-5:** deleting T23/T24 after the close also needs the banner's 25 changed to 23 and the hook comment that names T23 reworded.
  - **N3-6:** its cycle-2 report was never written into the artifact.

*Plain language:* both reviewers agree the earlier problem is fixed. They found that the close guard's own tests were weaker than described in two places. One test proved something had not happened, but not that the check it was meant to reach had actually run. And if the test run were killed at the wrong moment, it could leave fake "review approved" files behind that the close check would accept. Both are fixed, and the fake files would now be refused. *(Qualified at cycle 4, Codex NOTE 1: true of the complete fake pair, but a run killed between the two fake writes could still leave a half pair that passed. R-136 closes that window.)*

**R-135 (Orchestrator): take every item, as K4.** It touches the close guard's test harness and the docs only, so the code set is unchanged.
- **W3-1.**
  - The fake *reviewer* artifact now carries a stale `reviewed-against-commit`, so a left-behind pair fails the close gate.
  - The fake Codex artifact still carries HEAD. The hook checks the Codex artifact's staleness before it reads the reviewer's model line, so a stale Codex fake would refuse before T23 or T24 tested anything (the Orchestrator found this on the first run). Within the reviewer's pass, the model check and the telemetry echo come before its staleness check.
  - T23 now asserts the staleness refusal (exit 2, "Artifact is STALE") as well as the echo, and no longer reaches the CI-health gate's live `gh` call.
  - The real artifacts are also copied to `%TEMP%` before any overwrite, and the copies are removed afterwards.
- **N3-1:** T25 also requires the S99 telemetry echo (the run passed the model check) and the mocked CI-health line (it reached that gate). Its label is now `ci-gate-reached`.
- **N3-2:** the two sentences above are corrected in place, marked as cycle-3 corrections.
- **N3-3:** `docs/WORKFLOW.md`'s close-gate list now names the any-sprint switch and why it is safe: it writes nothing, so it can neither create a file another gate reads nor skip a gate. The list also gains the worktree-teardown (S143) and task-ledger (S141) gates, which the hook has enforced since those sprints but the list never named. Layer 4 of § Model Routing names the S145 exception.
- **N3-4:** the harness's cleanup removes the temp file.
- **N3-5:** the handoff's post-close item is extended.
- **N3-6:** both Step 7a artifacts now hold cycles 2 and 3 as the lenses wrote them. Their headers name the latest completed cycle.
- **Result:** 25 of 25 pass. The real S145 artifacts were restored byte-identical (SHA-256 checked before and after), no waiver or temp file was left behind, and the live telemetry log did not grow.

**K4 = `fe91d5b2`** carries R-135 and this record. The code set is unchanged from `B` (= K2) to K4, so C-2's check is empty and the frozen patches stand.

**C-3 cycle 4 (Step 7a scoped to R-135, `664763d1..fe91d5b2`).**
- **Codex: REQUEST-CHANGES**, on one BLOCKER in the harness.
  - **The BLOCKER:** the HEAD-valid Codex fake was written *before* the stale reviewer fake. A run killed between the two writes would leave a fake Codex "APPROVED" beside the real reviewer artifact. At close time the real one names HEAD and an accepted model, so that pair would pass the gate.
  - **A WARNING:** the recovery copies have fixed names. A run after a killed one would read the left-behind fakes as the originals, overwrite the genuine copies with them, and then delete the copies. The close gate would still refuse (the fakes are stale), but the review record would be lost.
  - **Two NOTEs:** cycle 3's "Both are fixed" was premature; and WORKFLOW.md's harness item is not itself a gate, yet sat in the list "enforced by the hook".
  - Codex otherwise confirms the HEAD-Codex / stale-reviewer split, T23 and T25, and every WORKFLOW.md statement.
- **The Opus lens: CLOSE, no BLOCKER and no WARNING.** It confirms the same points, line by line.
  - **N4-1 and N4-2** are Codex's BLOCKER and WARNING. The lens rated the first a NOTE because the window is one `Set-Content` call with no child process in it, so the risk is close to zero. Codex rated it by what the gate would accept if the run were killed there.
  - **N4-3:** two harness comments are stale or will go stale: "every seam is S99-only", and the switch's comment naming only T23.
  - **N4-4:** the transcribed cycle-2 report says "the new telemetry switch cannot hide a gate". At K2 the seam was the path variable, which B2-1 showed could; the artifact should point to the correction.
  - **N4-5:** the handoff's "headers name the final K" is ambiguous. The headers must name the commit the last cycle actually reviewed, with that cycle's record riding in the close commit itself; writing a later, unreviewed sha is the narrative edit the S38 staleness check exists to catch.

*Plain language:* the reviewers disagreed on how serious the remaining gap was, but agreed it was real. The test still wrote the two fake files in an order that left one moment when a crash would leave behind a pair the close check accepts. The fix is to write them in the other order, so that every state a crash can leave is refused. The Orchestrator takes the stricter reading (Codex's): the fix is free, and a sprint close should not rest on "close to zero".

**R-136 (Orchestrator): take every item, as K5.** It touches the close guard's test harness, one comment line in the guard, and the docs only; the code set is unchanged.
- **The BLOCKER / N4-1:** the stale reviewer fake is written first, then the HEAD-valid Codex fake. Every state a kill can now leave refuses: the stale reviewer alone; the pair; T24's cheaper-model reviewer; and, while `finally` restores Codex first, a real Codex beside either fake reviewer. *(Qualified at cycle 5, Codex NOTE: this holds for every fake pair and every fake beside a real artifact. Untouched or restored real artifacts can still pass, as they should; so can a real reviewer artifact cut short while being restored, if its header lines survived. R-137 removes the question, because the tests no longer touch the real artifacts.)*
- **The WARNING / N4-2:**
  - If any `statstid-guard-test-backup-SPRINT-145-*` copy already exists, that means an interrupted run. T23-T24 then do not touch the artifacts at all, and print a SKIPPED line naming the copies and the hand recovery; the banner fails.
  - A copy is deleted only once its artifact is verified restored byte for byte. Otherwise it is kept and the run FAILs. *(Corrected at cycle 5, both lenses: the check compared Base64 text with `-eq`, which ignores letter case, so it was not byte-exact. R-137 removed it.)*
  - **Proved:** with a probe copy placed in `%TEMP%`, the run printed the SKIPPED line, the banner reported "23 OF 25 PASS LINES, 1 SKIPPED", it exited 1, and the S145 artifacts were untouched (same SHA-256 and modification time).
- **Codex NOTE 1:** the cycle-3 sentence is qualified in place.
- **Codex NOTE 2:** in WORKFLOW.md, the harness is now an unnumbered paragraph below the six gates, marked as a practice the hook does not check.
- **N4-3:** the harness header now says every *gate* seam is S99-only, and names the telemetry switch as the one any-sprint seam. The switch's comment names T23 and T25, and says the switch stays after T23/T24 are deleted, because the S99 tests still need it.
- **Opus's note outside the delta:** the guard's header comment now lists the task-ledger waiver (`SPRINT-{N}-ledger-WAIVED.md`). This is a comment only.
- **N4-4:** the reviewer artifact's cycle-2 section carries a bracketed pointer to this log's "Why the Opus lens missed it".
- **N4-5:** the handoff's item (1) now says it exactly.
- **Result:** 25 of 25 pass, the real S145 artifacts were restored byte-identical, no waiver or temp file was left behind, and the live telemetry log did not grow.

**K5 = `664dd210`** carries R-136 and this record. The code set is unchanged from `B` (= K2) to K5, so C-2's check is empty and the frozen patches stand.

**C-3 cycle 5 (Step 7a scoped to R-136, `fe91d5b2..664dd210`), the cap.** Neither lens found a new BLOCKER, so the cap's halt does not fire (`docs/WORKFLOW.md` Step 7a: it fires only when cycle 5 surfaces a new BLOCKER).
- **Codex: APPROVE-WITH-WARNINGS.** The cycle-4 BLOCKER is fixed: no interrupted fake-write leaves the HEAD-valid Codex fake beside the real reviewer. It traced every kill point, from the first backup write to the last restore.
  - **WARNING 1:** the "byte for byte" check compared Base64 text with PowerShell's `-eq`, which ignores letter case. `AA==` (byte 0) and `aA==` (byte 104) compare equal, so a recovery copy could be deleted after a wrong restore.
  - **WARNING 2:** the SKIPPED line's "restore them from the copies by hand" is unsafe as stated. A kill *during* a copy write leaves a partial copy beside untouched real artifacts, and restoring from it would truncate a real file.
  - **A NOTE:** R-136's "every state a kill can leave refuses" is too broad (above, qualified).
- **The Opus lens: CLOSE**, no BLOCKER and no WARNING. Its own kill-point table agrees with Codex's.
  - **N5-1:** when a real artifact did not exist beforehand, the fake is removed with `SilentlyContinue`. A failed removal would leave the HEAD-valid Codex fake beside a restored real reviewer, which passes. This cannot arise while both real files exist.
  - **N5-2:** the recovery instruction misses that case too, and the partial-copy hazard.
  - **N5-3:** Codex's WARNING 1.
  - **N5-4, outside the delta:** the guard's refusal message still says "cycle-cap 2 per lens".

*Plain language:* by cycle 5 the reviewers were finding problems in the *safety net*: the backups, the recovery instructions and the check that a backup matched. The safety net existed only because the test overwrote the real review files and put them back afterwards. So instead of patching the safety net again, the Orchestrator removed the need for it: the test now runs the guard in a throwaway copy of a repository, with its own fake review files, and never touches the real ones. Every problem from cycles 3 to 5 about leftover or lost files goes away with it.

**R-137 (Orchestrator): remove the premise rather than patch it again.**
- **Why this is possible.** The guard finds `.claude/reviews`, `docs/sprints`, the telemetry log and HEAD from its working directory. A sandbox repository (`git init`, one empty commit, its own `.claude/reviews`) therefore gives T23 and T24 the same test, with nothing real to overwrite. A real close runs in the real repository, so nothing left in a sandbox can ever reach one.
- **The harness.**
  - `Invoke-Hook` takes an explicit working directory, which defaults to the repo root. The other 23 tests are unchanged.
  - T23-T24 build the sandbox under `%TEMP%`, write their two fakes there and remove it in `finally`. A sandbox left by a killed run is harmless, and the next run clears it. *(Corrected at cycle 7, Opus N7-2: from R-138 a later run cleared it only once it was an hour old, and a failed removal was reported. R-139 then deleted T23-T24 and the sandbox.)*
  - The backups, the interrupted-run check, the byte compare and the recovery instruction are all gone: Codex's WARNINGs 1-2 and NOTE, and N5-1..N5-3, resolve by deletion.
  - T23 still asserts the Opus id is accepted at the model check, the telemetry echo, exit 2 and the reviewer's staleness refusal. That refusal comes before every gate that reads the repository (CI health, consecutive CI-pending, untracked files, worktrees, the task ledger; named at cycle 6, Opus N6-4), so the sandbox never meets a gate it cannot answer. T24 is unchanged.
- **N5-4:** the guard's message now says "cycle cap 5 per lens; docs/WORKFLOW.md Step 7a".
- **Result:** 25 of 25 pass. The real S145 artifacts were not touched at all (the same SHA-256 and modification time before and after), no sandbox, waiver or temp file was left, and the live telemetry log did not grow.
- **The mutation check.** T23's exact steps were run against two mutated copies of the guard, in a sandbox:
  - **S145 exception removed:** T23 fails.
  - **Suppress switch ignored:** T23 fails, and the line is written into the *sandbox's* telemetry log, not the live one.
  - **The unmutated control:** T23 passes.

**K6 = `bbd3d378`** carries R-137 and this record. The code set is unchanged from `B` (= K2) to K6, so C-2's check is empty and the frozen patches stand.

**C-3 cycle 6 (verifying R-137, `664dd210..bbd3d378`).** Neither lens found a BLOCKER. Both confirm that the sandbox is a faithful test of the S145 exception, and that nothing in it can make a real close pass.
- **The Opus lens: CLOSE.** No WARNING; four NOTEs, none of which it thought needed a change before the close.
  - **N6-1:** nothing confirms the sandbox's commit landed in the sandbox.
  - **N6-2:** the harness's own HEAD lookup still depends on the folder it is launched from.
  - **N6-3:** two concurrent runs would interfere.
  - **N6-4:** a list of later gates omits the consecutive-CI-pending gate.
- **Codex: APPROVE-WITH-WARNINGS.**
  - **WARNING 1 (= N6-1):** git's isolation depends on the inherited environment. With `GIT_DIR` or `GIT_WORK_TREE` pointing at the real repository, the sandbox's `init` and `commit` would act on the real repository, and machine-wide git hooks could run during the sandbox commit.
  - **WARNING 2 (= N6-3):** the leftover cleanup deletes any matching sandbox, including a concurrent run's live one.
  - **A NOTE:** a failed cleanup is silent.
- **To the owner.** The cap was passed and these findings needed a change, so they went to the owner before anything was changed, as cycle 5's record said they would. The Orchestrator set out both paths:
  - close on K6, record the warnings, and carry the two lasting items into the post-close cleanup commit;
  - fix everything and run a cycle 7.

  The decision turned on the fact that the sandbox code is deleted right after the close (R-104).

*Plain language:* the reviewers agreed the new approach works. The remaining warnings were about unusual conditions, like a developer's git settings pointing somewhere unexpected, or two test runs at once. The owner chose to harden against them now, rather than close with them on record.

**R-138 (owner, 2026-10-20): "Fix all, run cycle 7".** It is applied as K7, again with the harness, one sprint-log line and the records only; the code set is unchanged.
- **Codex W1 / N6-1:**
  - The git variables that route a command to another repository (`GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_OBJECT_DIRECTORY`, `GIT_ALTERNATE_OBJECT_DIRECTORIES`, `GIT_COMMON_DIR`, `GIT_NAMESPACE`) are cleared for the sandbox steps and the hook runs they start, and restored afterwards.
  - The sandbox is made from an empty template and committed with `core.hooksPath` pointed at an empty folder and `--no-verify`.
  - Every git step's exit code is checked.
  - The sandbox must answer `rev-parse --git-dir` with `.git` (its own repository) before anything is committed; otherwise T23-T24 FAIL with the failing step named.
- **Codex W2 / N6-3:** only sandboxes over an hour old are cleared. A run takes about two minutes.
- **Codex's NOTE:** a sandbox that cannot be removed is reported in the output (as harmless), not ignored.
- **N6-2:** the harness's HEAD lookup is `git -C $repoRoot rev-parse HEAD`.
- **N6-4:** the harness comment and R-137's line above name all five later gates.
- **Result:** 25 of 25 pass. The real S145 artifacts were untouched (the same SHA-256 and modification time), no sandbox commit exists in the real repository, nothing was left in `%TEMP%`, and the live telemetry log did not grow.
- **The isolation, proved.** The harness was run with `GIT_DIR` set to the real repository's `.git`. It passed 25 of 25, and the real repository's refs and reflog were unchanged. Under K6, that run's `git init` and `commit` would have acted on the real repository.
- **The cleanup, proved.** With a two-hour-old sandbox and a fresh one placed in `%TEMP%`, the run cleared the old one and left the fresh one alone.

**K7 = `d4d11c76`** carries R-138 and this record. The code set is unchanged from `B` (= K2) to K7, so C-2's check is empty and the frozen patches stand.

**C-3 cycle 7 (verifying R-138, `bbd3d378..d4d11c76`).** Neither lens found a BLOCKER. Both confirm that R-138 holds: no inherited git variable can now route the sandbox, or the child hook, to another repository, and no failure path reports PASS.
- **The Opus lens: CLOSE.** No WARNING; three NOTEs:
  - **N7-1:** "hooks off" is narrower than the comment says.
    - Git 2.55 also supports hooks defined in its settings (`hook.<name>.command`), which `core.hooksPath` does not govern.
    - The `git init` step lacks the override.
    - No such setting exists on this machine; one that did would run inside the sandbox.
  - **N7-2:** one sentence of R-137's record is stale (corrected above).
  - **N7-3:** the age check compares local times, not UTC.
- **Codex: APPROVE-WITH-WARNINGS.**
  - **WARNING 1 (= N7-1):** a configured post-commit hook could even fail while git reports success.
  - **WARNING 2:** a folder's age is not proof that nobody is using it; a run paused for over an hour could lose its sandbox.
  - **A NOTE:** a cleanup message whose path contains "fail", in any case, would trip the banner's failure check.
- **To the owner.** The cap was passed, so the findings went to the owner. The Orchestrator set out three paths:
  - fix all and run a cycle 8;
  - close on K7 with the warnings recorded;
  - delete T23-T24 now.

  The owner asked for a recommendation. The Orchestrator recommended deleting them.
  - **The tests' proof is complete:** every run passed, the mutation check showed T23 fails without the exception, and seven review cycles examined them. *(Corrected at cycle 8, Opus NOTE 2 / Codex NOTE: this first said "they fail". The mutation check ran T23 only, and T24, which pinned that the exception admits Opus and nothing cheaper, would still pass with the exception removed.)*
  - **Nothing is left for them to protect:** nothing in the guard changes before the close, and they were to be deleted right after it (R-104).
  - **The findings had moved off the exception:** since cycle 5, no finding concerned the exception itself; nearly all concerned the scaffolding the tests needed (a throwaway git repository, with its isolation and cleanup). *(Corrected at cycle 8, Opus NOTE 2: this first said "every finding concerned the scaffolding", but N5-4 concerned the guard's cap message and N6-4 a list of gates.)* Running real git in a temporary folder has an open-ended supply of such edge cases, so a cycle 8 of hardening would likely find the next, narrower one.
  - **Closing on K7 would be safe, but it would close over known-wrong statements** in the reviewed tree, such as "no hook installed on this machine runs".

*Plain language:* the last three review rounds had stopped finding problems with the rule being tested. They were finding problems with the temporary test setup around it, one narrower than the last. The test had already proved what it was for, and it was due to be deleted the moment the sprint closed. So the owner chose to delete it one step early, rather than keep polishing it.

**R-139 (owner, 2026-10-21): "Delete T23/T24 now (Recommended)".** It is applied as K8: the harness, one guard comment, and the records; the code set is unchanged.
- **The deletions.** T23, T24 and their sandbox repository are removed from the harness. A short note in their place says what they were, why they went, and that T25 keeps its number so the records citing it stay valid.
- **The banner** requires 23 PASS lines: T1-T22 and T25.
- **What stays.** `Invoke-Hook`'s explicit working directory (the repo root) and the `git -C $repoRoot` HEAD lookup stay: both keep the hook and the harness on the same repository wherever the harness is launched from.
- **Comments reworded.** The telemetry switch's comment in the harness, and the guard's own comment beside the switch, now name T25 as the echo's user.
- **What does not change.** The S145 exception itself stays in the guard for the close, and is deleted after it (R-104).
- **The cycle-7 findings** (N7-1, N7-3, and Codex's WARNINGs 1-2 and NOTE) concerned only the deleted code, so they resolve by deletion. N7-2 is corrected above.
- **Result:** 23 of 23 pass. The real S145 artifacts, the refs and the live telemetry log are unchanged, and nothing was left in `%TEMP%`.

**K8 = `d129468f`** carries R-139 and this record. The code set is unchanged from `B` (= K2) to K8, so C-2's check is empty and the frozen patches stand.

**C-3 cycle 8 (verifying R-139, `d4d11c76..d129468f`), the last.** Neither lens found a BLOCKER or a defect in the reviewed state. Both confirm:
- the deletion is complete and clean, with no variable, function, environment variable or reference left behind;
- the banner's 23 is exact (T1-T22 and T25, each printed once and never conditionally), and a missing or skipped test still fails it;
- the guard's only change is a comment;
- the code set is unchanged from `B`.

The Opus lens returned **CLOSE**; Codex returned **APPROVE-WITH-WARNINGS**. What remained was wording:
- **NOTE 1 = Codex's WARNING:** the handoff's post-close list omitted one line.
  - The exception's commit `e2ce7dfc` also changed the model comparison (`sprint-close-guard.ps1:234`) from `$reviewedBy -ne $reviewFloor` to `$acceptedReviewModels -notcontains $reviewedBy`.
  - Deleting only the two lines the list named would leave that comparison reading a variable that no longer exists, so every future close would be refused, Fable included.
  - That fails safe and loudly: twelve harness tests would FAIL at once.
- **NOTE 2 = Codex's NOTE:** "the mutation check showed they fail without the exception" is true of T23 only. "Every finding concerned the scaffolding" is loose. Both are corrected above, marked as cycle-8 corrections. The harness note at `test-sprint-close-guard.ps1:341-344` carries the same wording.
- **NOTE 3:** T21 is now the only harness test that would catch the exception spreading beyond S145, since it pins that the reserved S99 still refuses Opus. Its comment calls it a mere control. One clause in the harness note should say what is still pinned.
- **NOTE 4:** "harness back to 23 tests" is inaccurate, because the harness was never at 23 (22, then 24, then 25). Corrected in the Phase row.

**R-140 (owner, 2026-10-21): "Close on K8 (Recommended)".** Every item is fixed in the commit it belongs to; nothing changes between the reviewed commit and the close.
- **The sprint-log wording** (NOTE 1's checklist line, NOTE 2's two sentences, NOTE 4) rides in the close commit, with this record. That commit is the documentation bundle the close is designed to carry.
- **The two harness comment fixes** (NOTE 2's wording in the T23-T24 note, NOTE 3's clause about T21) go into the post-close cleanup commit. That commit rewrites that same note when it deletes the exception, and it gets its own review cycle.

*Plain language:* the eighth review found nothing wrong with the code, only four places where the notes said slightly more, or slightly less, than was true. The owner chose to close now. The fixes go into the close itself and the cleanup right after it, rather than into another review round about comments.

**The close state.** Step 7a ran eight cycles on both lenses, from cycle 6 past the five-cycle cap by the owner's rulings (R-138, R-139, R-140). Both Step 7a artifacts' headers name `d129468f`, the commit cycle 8 reviewed: the Opus lens `CLOSE` (`reviewed-by-model: claude-opus-5-5`, accepted for S145 under R-104) and Codex `APPROVE-WITH-WARNINGS`. The close commit's parent is `d129468f`.

**The fix batch** runs on `s145/step7a-fix` (worktree `s145-step7a-fix`, at K):
1. `test-qa` writes the RED-first pins.
2. `backend-infrastructure` makes B-1's fix.
3. The Orchestrator makes W-1's one-line fix.
4. 5α, then 5a on both lenses.
5. The merge, then a new K, then C-2 ("C-1 again", with the census for the new Docker facts), then a C-3 cycle 2 scoped to the fix.

## C-4..C-9 — the close and the Stage B evidence (2026-10-21)

**C-4, the close.**
- The close commit `13a40e68` passed the close guard. A dry run of the guard against the real state passed every gate beforehand, with its telemetry suppressed.
- The close commit was pushed to master (`cdd82539..13a40e68`).
- CI run `39571040922` was **green on the first run**, all seven jobs: Unit 1512 · DemoSeed 170 · Regression **2150** (Docker included, all executed) · Smoke 7.
- This is the first CI verification of the Docker facts that S145's fix batch and its re-shapes added (R-128..R-133); every one passed.

**C-5, the evidence commits** (TASK-14528, `test-qa`):
- The four frozen patches and eight lists were re-checked against R-127's SHA-256 by both the Orchestrator and the agent. All matched.
- On `s145-evidence-b`, built from the close sha:
  - EB-1 `d970d726` (MB-2, MB-4, MB-5, MB-13);
  - EB-2 `a070adf0` (MB-7);
  - EA-R1 `75cd1ad0` (MD-25, MD-30);
  - EA-R2 `68d81dfe` (MD-24).
- Each `git diff <close> <EB-k>` is byte-identical to its frozen patch (`cmp` exit 0). The Orchestrator verified this independently.
- Each commit built with 0 errors / 145 warnings, and Unit 1512, DemoSeed 170 and Regression non-Docker 128 were green on it.
- The code set is unchanged from both the close's K and `B` to the close, and `origin/master` is an ancestor of EB-1.
- The keys file in `cb5/` has one key per name (R-122): `uniq -d` printed nothing.
- `compare.sh`'s only change reads the `eb1`/`eb2`/`ear1`/`ear2` key files, because its `ea<k>` naming could not take them.

**C-6/C-7, the runs** (draft PR #38). The branch was fast-forwarded one commit at a time, and each run was compared before the next push:

| Run | Head | CI run | Red (expected) | MISSING · EXTRA | Expected greens passed | Unit · DemoSeed | Regression passed / failed |
|---|---|---|---|---|---|---|---|
| EB-1 | `d970d726` | `39582837187` | 14 (14) | none · none | 61 / 61 | 1512 · 170 | 2136 / 14 |
| EB-2 | `a070adf0` | `39596060905` | 22 (22) | none · none | 65 / 65 | 1512 · 170 | 2128 / 22 |
| EA-R1 | `75cd1ad0` | `39611267512` | 10 (10) | none · none | 66 / 66 | 1512 · 170 | 2140 / 10 |
| EA-R2 | `68d81dfe` | `39622141134` | 4 (4) | none · none | 80 / 80 | 1512 · 170 | 2146 / 4 |

- In every run, only `build-and-test` was red; smoke, e2e, frontend, docs, gitleaks and lizard were green. No list holds the smoke key.
- All 2150 Regression facts executed every time, with 0 errors and 0 not executed.
- **All four matched on the first attempt.** Stage A needed a correction run (EA-3′).

**The seat: CONFIRMED** (Opus by the owner's direction, `opus-seat-model: claude-opus-5-5`, read-only; its content baseline was unchanged afterwards).
- It re-derived every run from the raw TRX and git, without `compare.sh`. Failed set, expected greens, Unit and DemoSeed, counters, and each patch byte-identical: all as above.
- It read the failure message of **all 50 reds**. Each fails on the check its mutation's census predicts, for example:
  - MB-2's pay codes come out under the first part's agreement;
  - MB-4's IB2 returns 409 where 422 was expected;
  - MB-13's SR1 sums 37 hours where 74 were expected;
  - MB-7's reds carry the planner's `NORM_CHECK_WEEKLY` refusal;
  - MD-24's X3/X4 return 409 where 503 was expected.
- None fails on the database, a timeout or a fixture; every red lasted 0.13 s or less.
- **Its NOTEs:**
  - **NOTE 1:** the run-to-commit link rests on the Orchestrator's GitHub record, because the seat ran without network access. The local reflog's push times, about two minutes before each run's first TRX, corroborate it.
  - **NOTE 2:** seven EB-2 reds fail through a missing `reason` field (`KeyNotFoundException` at `root.GetProperty("reason")`) rather than an `Assert`: `SplitMonthHostTests.cs:317, :336`, `PayrollHostRecalcBlockedTests.cs:687, :1353`, `PayrollHostWeeklyTests.cs:740`. The failure is still the mutation's, and checks the right thing. A clearer `TryGetProperty` plus `Assert` is an S146 pickup.
  - **NOTE 3:** the C-1 test inventory differs from the TRX by exactly the six fix-batch facts and the two R-132 renames, already recorded at C-2 on K2.
  - **NOTE 4:** one Unit theory row is duplicated by display truncation; both rows pass.

*Plain language:* the sprint's new database tests were shown able to catch the breakages they were written for. Four times, the code was broken on purpose in known ways. Each time, CI failed exactly the tests the plan named, for the reason the plan predicted, and nothing else. An independent reviewer re-checked every failure from the raw results.

**C-8.** PR #38 closed unmerged; `s145-evidence-b` deleted on origin and locally; only the main worktree stands.

**C-9.** This docs-only commit:
- the run ids above;
- QUAL-149 BUILT (with QUAL-150's split-month half), in the quality register, ADR-016, ADR-040 and ROADMAP;
- the QUALITY.md Payroll Integration condition, met;
- the INDEX row;
- the S145 row of the model-routing register.

## Post-close — the S145-only review-model exception deleted (2026-10-21)

As owner ruling R-104 required, the close guard's one-sprint exception (accept `claude-opus-5-5` for S145's Step 7a reviewer artifact) is deleted after the close.
- **`.claude/hooks/sprint-close-guard.ps1`.** The exception's comment block and its two lines (`$acceptedReviewModels`, `if ($sprintNum -eq '145')`) are removed. The model comparison is restored to `if ($reviewedBy -ne $reviewFloor)` (cycle 8 NOTE 1 / Codex WARNING), and the telemetry note's "owner exception" branch is removed.
  - Against the guard before the exception (`c488b2d3`), the model check is now byte-identical.
  - The only remaining differences are the three changes S145's Step 7a reviewed: the ledger-waiver header line, the cycle-cap message, and the telemetry switch.
- **`.claude/hooks/test-sprint-close-guard.ps1`.**
  - The T23-T24 note says the exception is gone and that the mutation check showed T23 fails without it (cycle 8 NOTE 2). It also said that T21 now pins, for every sprint, that the floor admits no other model. *(Wrong, corrected by the review below: T21 runs S99 only.)*
  - T21's comment said the same and asked to keep it (cycle 8 NOTE 3).
  - 23 of 23 pass; the S145 artifacts and the live telemetry log are unchanged.
- **`docs/WORKFLOW.md`** § Model Routing, layer 4 records in one past-tense sentence that S145 alone closed on an Opus review, and that the exception was deleted after the close.
- **Review:** a post-Step-7a review cycle on both lenses (WORKFLOW Step 7a, "post-Step-7a coverage"), scoped to the commit `6c29efc2`. The cycle counter starts again for this scope.

**Post-close review, cycle 1 (on `6c29efc2`).** The internal lens ran on **Fable** again (`reviewed-by-model: claude-fable-5-1`), the first Fable review since 2026-10-19.
- **Both lenses confirm the code.** The exception is gone from every executable line. The model-check block is byte-identical to `c488b2d3`. Fable also probed the real hook against throwaway artifacts outside the repository: Opus is refused for S145 and for S146 with the floor message, and Fable for S145 passes the compare.
- **Fable: BLOCKED, on one claim; Codex: APPROVE-WITH-WARNINGS, the same point as its WARNING.** "T21 pins, for every sprint, that the floor admits no other model" is false.
  - T21 runs one close, on the reserved S99. An exception scoped to any other sprint number leaves it green.
  - S145's own history proves it: with the exception in the hook, the harness passed 25/25 (K7) and 23/23 (K8) with T21 green. Only T23 ever discriminated on the exception.
  - The sentence would tell the next editor that a re-introduced exception is caught by the existing tests, which is exactly the gap T23 filled.
  - Cycle 8's own NOTE 3 was worded correctly ("spreading beyond S145"); the post-close rewording widened it.
- **Fable's NOTE 1:** "from cycle 5 on no finding concerned the exception" holds only at the time of R-139, since cycle 8's NOTE 1 concerned the exception's comparison line.

*Plain language:* the cleanup itself was right. But a comment claimed the remaining test protects every sprint from an exception like S145's, when it only checks one test sprint. The S145 exception had sat in the code while that very test kept passing. So instead of just rewording the comment, a new test now checks the guard's code directly.

**The fix (Orchestrator; the owner's standing preference for the complete fix, which Fable's report names).** Fable offered two fixes: reword the claim, or make it true. This makes it true.
- **The new T26** is a static pin in the style of T25's static half. It reads the guard's model-check block, from its `# Model-routing check` comment to its `# Staleness check` comment. It requires:
  - no comparison on the sprint number;
  - no list of accepted models (`acceptedReviewModels`, `-contains`, `-in`);
  - exactly one floor literal;
  - exactly one `if ($reviewedBy -ne $reviewFloor)`.

  The guard compares `$sprintNum` with `'99'` in five gate seams, all outside that block.
- **The comments.** T21's comment and the T23-T24 note now say what each test pins: T21 at run time on S99, and T26 statically for every sprint. The note adds Fable's "at the time of R-139".
- **The banner** requires 24 PASS lines.
- **Result:** 24 of 24 pass.
- **The mutation probe.** T26's exact function, extracted from the harness, was run against real guard versions and three made-up versions:
  - the current file and the pre-exception `c488b2d3` give `ok`;
  - the exception's own commit `e2ce7dfc` and K8 `d129468f` give `sprint-branch`;
  - a made-up `-in @('146')` exception gives `sprint-branch`, a second floor literal gives `floor-literal`, and the compare removed gives `floor-compare`.

  So T26 fails on both historical exception versions and all three synthetic mutants; S145's exception would have been caught. *(Wording corrected at cycle 2, Codex NOTE: this first said "fails on every one of these", which included the two `ok` versions.)*
- **The commit message of `6c29efc2`** repeats the wrong T21 sentence. It is left as written, since commits are not rewritten; this record corrects it.

**Post-close review, cycle 2 (on `30be8017`).** The Fable lens returned **APPROVED-WITH-WARNINGS** and Codex **REQUEST-CHANGES**, with the same substance: T26 is real for the exception that existed, but its wording promised a proof.
- **The overclaim.** "Applies to EVERY sprint", "no comparison on the sprint number" and "This makes it true" claim more than T26 shows. T26 was a list of three forbidden code shapes.
  - Fable wrote seven sprint-scoped exceptions in ordinary PowerShell, six inside the block, and T26 returned `ok` for all seven: a reversed `-eq`, a double-quoted floor, a lookup table keyed by sprint, `.Equals`, an interpolated `"S$sprintNum"`, and a helper call.
  - Codex wrote more: a truthy lookup table, an array on the left of `-eq`, `[regex]::IsMatch`, and an unconditional regex rewrite of `$reviewedBy`.
- **Fable's W2 / Codex's BLOCKER: a bounded limitation.** No pin scoped to the block can see code before it in the same loop (for example, rewriting `$content`) or a helper defined elsewhere. Reading the whole hook for `$sprintNum` would be unbounded, because it has about forty legitimate uses. So this is recorded as a limitation, not answered with a further test.
- **Codex's WARNING:** the block markers were not checked for uniqueness. A duplicate end marker could hide later code.
- **The NOTEs:** T26 can false-fail on harmless edits, loudly and with a reason (both lenses); and this record's "fails on every one of these" wording (corrected above).

*Plain language:* both reviewers showed that a test which forbids certain ways of writing an exception can be dodged by writing it another way. So the test now works the other way round: it lists the few places the sprint number is allowed to appear, and fails on any other use. It also says plainly what it cannot check.

**The fix (cycle-2 absorption).**
- **T26 is now an allow-list, and calls itself a regression pin, not a proof.**
  - Each marker must occur exactly once, as a comment line, in order.
  - Inside the block, the sprint number may appear only in its two message strings ("BLOCKING sprint S$sprintNum close commit." and the telemetry line's "| close | S$sprintNum |"). Any other use, in any syntax, fails as `sprint-used`. *(Wrong, corrected at cycle 3: the check matched the token `$sprintNum`, not the sprint number. Brace syntax, scope prefixes, `Get-Variable`, the artifact-path variables and a copied message fragment all passed it.)*
  - There may be no list of accepted models (`acceptedReviewModels`, or a ` -contains ` / ` -in ` operator).
  - There must be exactly one assignment to `$reviewFloor`, to a model-id literal, and exactly one to `$reviewedBy`, plus exactly one floor compare.
- **What T26 cannot see is written in its comment:** code before the block in the same loop, and helpers elsewhere in the hook. A change there is caught by the Step 7a review of that change, and an unconditional exception by T21 at run time. *(Incomplete, corrected at cycle 3: exceptions written wholly inside the block could also escape. "Is caught by" also overstated a review process, and T21 catches only its own fixture.)* The comment also says that T26 can false-fail on a harmless edit inside the block, naming its reason.
- **T21's comment and the T23-T24 note** say the floor is now guarded by three things: T21 at run time on S99, T26 as a static regression pin on the block, and the review of any change to the hook.
- **Result:** 24 of 24 pass.
- **The mutation probe**, run with T26's exact function extracted from the harness:
  - the current file and `c488b2d3` give `ok`;
  - `e2ce7dfc` and `d129468f` give `sprint-used`;
  - every exception the two lenses wrote inside the block fails: F1-F6 and C1, C2, C4 give `sprint-used`, C3 (the unconditional rewrite) gives `reviewedBy-assign`, and a decoy duplicate end marker gives `markers-not-unique`;
  - the earlier `-in @('146')` mutant gives `sprint-used`, and the removed compare gives `floor-compare`;
  - the exception placed **outside** the block gives `ok`, the recorded limitation.
- **The commit message of `30be8017`** ("so the floor holds for every sprint") is corrected by this record, as `6c29efc2`'s was.

**Post-close review, cycle 3 (on `ff607881`).** Fable returned **BLOCKED**, and Codex **REQUEST-CHANGES**, on the same claim. "The block cannot branch on the sprint number in any syntax" is false: the check matches the token `$sprintNum`, not the sprint number.
- **The bypasses.**
  - Fable wrote in-block exceptions that pass T26 with `ok`: brace syntax `${sprintNum}`, `$script:sprintNum`, `Get-Variable sprintNum`, and the artifact path, which carries the number (`$artifact -like '*SPRINT-146-*'`). It ran two of them end to end through the real guard in a sandbox: an Opus artifact closed S146 while S99 was still refused. T21 and T26 both stayed green, the S145 blind spot again.
  - **Fable's warnings:** case-modified list operators (`-cin`, `-icontains`) passed; the body of the floor compare was unpinned (a conditional `exit 2`); and "an allow-list" was only half true.
  - **Codex's examples:** the allowed message fragment copied into an `if`; a `.IndexOf` list; and `${reviewedBy} = $reviewFloor`.
- **Both offered the same bounded resolution.** Say only what is checked, record the in-block bypasses, and leave the rest to review: "requires review", not "is caught by review". T21 covers only its own S99 Opus fixture. Fable also proposed three cheap tightenings.
- **Recorded, neutrally.** The Fable lens wrote its probe scripts (`mut-*.ps1`, `e2e.ps1`, `run.ps1`) to the Orchestrator's scratchpad, outside the repository. That goes beyond its read-only brief. The repository was unchanged (HEAD, status and worktrees as before), and the probe files were reused below.

*Plain language:* the test was still being described as stronger than it is. It checks the text of the guard for certain patterns, and an exception written in a different style can still slip past. The test now catches more styles. Above all, its comment now says exactly which patterns it checks, lists the known ways around it, and says that any change to the guard must be reviewed. The review is the real safeguard.

**The fix (cycle-3 absorption).**
- **The wording claims only the literal checks.**
  - The T26 comment lists the text patterns the pin looks for, and the classes it does not see.
  - **Not seen, inside the block:** variables that already embed the sprint number (`$artifact`, `$reviewer`, `$codex`, `$waiver`, `$tline`); the body of the floor compare; list tests by other means (`-match`, `-like`, `.IndexOf`, a regex or a wildcard); and any other syntax or indirection not listed.
  - **Not seen, outside the block:** code before it in the same loop, and helpers elsewhere.
  - The comment ends: "any change to the hook requires the Step 7a review of that change; that review, not this pin, is where such a change is meant to be caught."
  - T21's comment names its one fixture. The T23-T24 note lists the three guards in those terms.
- **The tightenings** (Fable's three, plus one for Codex's fragment; the first bullet carries two of the four):
  - The allowed messages are stripped only as their **entire lines**, with fixed counts (two BLOCKING lines and one `$tline` line, or `sprint-lines`). Then the bare word `sprintNum` anywhere else fails as `sprint-used`.
  - Variable indirection fails as `indirect-access`: `${`, a scope prefix, or `Get-/Set-/New-/Remove-/Clear-Variable`. `$env:` is allowed, because the block reads the telemetry switch.
  - List operators in any case form fail as `model-list`.
- **The PASS label** says "shows none of the pinned exception patterns".
- **Result:** 24 of 24 pass.
- **The probe** (`t26-probe3.ps1` and `t26-probe4.ps1`, T26's exact function):
  - The current file gives `ok`.
  - **S145's exception re-inserted into today's guard** (both parts of `e2ce7dfc`) gives `sprint-used`, as does a sprint branch alone.
  - **Caught:**
    - Fable's 25 mutant files: everything the comment says is checked fails (`sprint-used`, `indirect-access`, `model-list`, `reviewedBy-assign`, `markers-not-unique`, `floor-assign`, `floor-compare`);
    - Codex's fragment-in-an-`if` fails as `sprint-used`, and `${reviewedBy}` as `indirect-access`.
  - **Pass, as the comment lists:** E4 (the artifact path), E9 (the conditional `exit` inside the compare), Codex's `.IndexOf` list, and L3 (outside the block).
  - **A changed result:** the pre-exception `c488b2d3` and the two exception versions now give `sprint-lines`. The pin matches today's exact lines, and their telemetry line differs (the S145 switch, R-134, changed it). So the historical exception is tested by re-inserting it into today's file, as above.
- **The commit message of `ff607881`** ("the sprint number may appear only in its two message strings") is corrected by this record (Fable NOTE 2).

**Post-close review, cycle 4 (on `9576c4fd`).** Codex returned **APPROVE**, with no finding. Fable returned **APPROVED-WITH-WARNINGS**. Both found **no overclaim left**: every sentence now describes a check the code performs. Fable confirmed every probe result to the reason code, in memory, writing nothing.
- **W1:** the list-operator check needed a space after the operator, but PowerShell also accepts `-in@(...)`, `-in(...)` and `-contains$x`.
- **W2:** the "does not see" list covered only sprint-number and model-list exceptions. An exception keyed on something else (another environment variable, the artifact's text) passes both tests unrecorded. The `$env:` allowance is the kind of gate-disabling seam the hook confines to S99 everywhere else.
- **The NOTEs:**
  - `$command` also carries the sprint number;
  - "only T23 ever caught it" inverts T23's sense, since T23 went red when the exception was *removed*;
  - `gv` and `PSVariable` are indirections within the recorded class;
  - this handoff block was three commits behind;
  - two record nits.

**The fix (cycle-4 absorption).**
- **The operator check** now ends at a word boundary, so it catches the operator with or without a following space (`model-list`).
- **The `$env:` allowance** is narrowed to the one switch the block reads, `$env:STATSTID_TELEMETRY_SUPPRESS`. Any other environment variable fails as `indirect-access`, as do `gv`, `sv` and `PSVariable`.
- **The "does not see" list** adds exceptions keyed on something other than the sprint number or the model id (the artifact's text, the date, a file), and gives the sprint-number carriers "for example", with `$command` first.
- **The T26 comment** says T23 "discriminated on" the exception, going red when it was removed.
- **The record nit** is fixed above, and the handoff block is refreshed.
- **Result:** 24 of 24 pass.
- **The probe** (`t26-probe5.ps1`, T26's exact function):
  - today's guard gives `ok`, and S145's exception re-inserted gives `sprint-used`;
  - Fable's `-in@(...)`, `-in(...)` and `-contains$reviewedBy` give `model-list`;
  - the environment-variable waiver, `gv` and `PSVariable` give `indirect-access`;
  - the artifact-text and `$command` exceptions give `ok`, now recorded as residuals;
  - all 25 cycle-3 mutant files give the same results as before, with no regression.
- **Next:** cycle 5, the last cycle for this scope (the cap), verifies this fix on both lenses. A new BLOCKER there goes to the owner before any further change.

## Handoff block (written at every gate — the state lives here, not in the conversation)

| | |
|---|---|
| **As of** | 2026-10-21, **S145 CLOSED; the post-close deletion of the S145-only exception is in its review** (§ "Post-close"): the deletion `6c29efc2`, then the review fixes `30be8017` (cycle 1), `ff607881` (cycle 2), `9576c4fd` (cycle 3) and this commit (cycle 4), all local and unpushed, on top of the C-9 docs-only record `6e3ba7ef` (= `origin/master`). The close commit `13a40e68` (CI green `39571040922`) sits on K8 `d129468f`, the commit Step 7a cycle 8 reviewed, on top of K7 `d4d11c76` (R-138), K6 `bbd3d378` (R-137), K5 `664dd210` (R-136), K4 `fe91d5b2` (R-135), K3 `664763d1` (R-134), K2 `64456c7b` (C-2 on K2: the freeze stood, `B` = K2) and the fix merge `8fda8415`. `origin/master` was `cdd8253` (CI green, run `36980160503`) until the close push. Stage A closed on 2026-10-16 at `K_A` (`923f7fb`) |
| **Evidence** | `K_A` = `923f7fb` · V-A run `38747749039` (all seven jobs green) · EA-1..EA-5 all matched (EA-3 through EA-3′); runs and heads in § "Stage A gate — `K_A`" → Results and § "CA-5"; confirmed by the Fable seat (R-52) · PR #37 closed unmerged, `s145-evidence-a` deleted on both sides · **the close** `13a40e68`, CI green `39571040922` (Regression 2150) · **Stage B:** EB-1 `39582837187`, EB-2 `39596060905`, EA-R1 `39611267512`, EA-R2 `39622141134`, all matched on the first attempt; confirmed by the seat on Opus (all 50 reds read) · PR #38 closed unmerged, `s145-evidence-b` deleted on both sides |
| **Phase** | The close sequence, C-3 (2026-10-20). Cycle 1 (on K1) found B-1, fixed by the batch merged at `8fda8415` (R-128..R-133). Cycle 2 (on K2): the Opus lens CLOSE; Codex REQUEST-CHANGES on B2-1, the harness telemetry seam that could create a waiver, fixed by R-134 (a switch; T23 positive; T25; 25/25). K3 carries R-134 and the cycle-2 notes (N-A..N-F). Cycle 3 (on K3): Codex APPROVE-WITH-WARNINGS, the Opus lens CLOSE-WITH-WARNINGS (W3-1, N3-1..N3-6), all taken by R-135 in K4 (harness and docs only). Cycle 4 (on K4): Codex REQUEST-CHANGES on the harness's fake-write order (a half pair could pass) plus the recovery copies' fixed names; the Opus lens CLOSE (N4-1..N4-5, the first two the same items); all taken by R-136 in K5 (harness, one guard comment line, docs). Cycle 5, the cap (on K5): Codex APPROVE-WITH-WARNINGS, the Opus lens CLOSE, no new BLOCKER, so no halt; R-137 in K6 runs T23-T24 in a sandbox repository, so the harness no longer touches the real artifacts and the recovery machinery is deleted. Cycle 6 (on K6): the Opus lens CLOSE (N6-1..N6-4), Codex APPROVE-WITH-WARNINGS (git-environment isolation; concurrent-run cleanup); no BLOCKER; past the cap, so to the owner, who ruled R-138 "Fix all, run cycle 7" (K7). Cycle 7 (on K7): the Opus lens CLOSE (N7-1..N7-3), Codex APPROVE-WITH-WARNINGS (config-defined hooks; age is not proof of abandonment); no BLOCKER; to the owner, who ruled R-139 on the Orchestrator's recommendation: delete T23-T24 now (K8; the harness now has 23 tests, T1-T22 and T25). Cycle 8 (on K8): the Opus lens CLOSE, Codex APPROVE-WITH-WARNINGS, wording only; the owner ruled R-140 "Close on K8". The code set is unchanged K2..K8, so the frozen patches stand. **C-4..C-9 done (2026-10-21):** the close `13a40e68`, CI green first run; the four Stage B evidence runs matched and the seat confirmed; PR #38 closed unmerged; this C-9 record. **The sprint is closed** |
| **Dispatched tasks / worktrees** | **Merged:** 14500 (`1b6160c`), 14505+T (`ce51a38`), 14501 (`5c35ffb`), 14502 (`25eb8eb`), 14503a (`32b668a`, `6e4592c` only), 14503b (`510619e`), 14504 (`630bf89`), 14507 (`94dea75`), 14506 (`379567e`). O-9 done (`9b75848`), O-2 done (no drift). 14506 re-spawn 2 done: 27 of 27 mutations tripped their pins, and its throwaway checkout is removed. **Merged in A.2:** 14510 (`d928cf5`), 14516 (`46cecd8`), 14509+T (`755cb14`), 14508+T (`f0a863b`). **Merged in A.3:** 14511+T (`2d14003`). O-10 merged (`a59825b`). **Merged in A.4:** 14512+T (`250fcdf`), 14514 (`49c00e5`); 14514 re-spawn 2 done: 14 of 14 mutations tripped, its throwaway checkout removed. **Merged in A.5:** 14513+T (`07337ea`), 14515 (`eb78b8e`), 14519+T (`8367862`); 14515 re-spawn 2 done: 3 of 3 mutations tripped, its throwaway checkout removed. **Gate A-CLOSE (closed 2026-10-16):** TASK-14518 CA-1 and CA-3 done; the freeze R-33..R-46; the R-33 split merged (`07f7c78`); `K_A` `923f7fb`; the evidence branch and PR #37 closed and deleted (CA-6); the CA-5 rulings R-47..R-52. **After CA-7:** QUAL-207 merged (`499771c`); its worktree `s145-qual-207` to clear at teardown. **To clear at teardown:** `s145-task-14518-rs` (merged); the A.5 worktrees `s145-task-14513`, `s145-task-14515`, `s145-task-14519` (all merged); the merged worktrees `agent-ac1278752784ecede`, `agent-ad9053a02a651ddfb`, `agent-a1e2cddcd1ccda107`, `agent-a219bb4679ce74dae`, `s145-task-14503b`, `s145-task-14504`, `s145-task-14507`; `agent-a0fe62b0b3b73d252` (14503a; its `c3543f1` file is now merged byte-identical), `agent-a97b8efd3e8cd8e90` (14506, merged), and the A.2 worktrees `s145-task-14508`, `s145-task-14509`, `s145-task-14510`, `s145-task-14516` (all merged); the A.3/A.4 worktrees `s145-task-14511`, `s145-task-14512`, `s145-task-14514` and O-10's `s145-o10` (all merged); and the two unregistered directories `agent-a9efbf24c4db49318` and `agent-aa8d09f81f61764a1`. **B.1:** `s145-task-14520` (merged `53a7812`) and `s145-task-14521` (merged `fe928dde`) are to clear at teardown. Still open: `s145-task-14524` (merges at B.3). **B.2:** `s145-task-14522` (merged `f6fac97b`) is to clear at teardown. **B.3:** `s145-task-14523` (merged `8efdfffd`; its `frontend/node_modules` is a junction to the main checkout, so remove the junction before the worktree at teardown) and `s145-task-14524` (merged `8a54484d`; re-spawn 2 works in throwaway checkouts outside the repository) |
| **Pending gates** | None for S145. The post-close review's cycle 5 (both lenses), the cap for that scope, on this commit; then the push of the five post-close commits. A new BLOCKER at cycle 5 goes to the owner first. This machine is memory-constrained (8 GB): one build-heavy agent at a time, `-m:2`, foreground; `dotnet build-server shutdown` after builds |
| **Open rulings** | None for the owner. R-104 was answered on 2026-10-20 (option 2); R-138 ("Fix all, run cycle 7") on 2026-10-20 after cycle 6; R-139 ("Delete T23/T24 now") on 2026-10-21 after cycle 7. The reopen hook's `EffectiveFrom` ruling was confirmed at the batched Fable 5a |
| **Next action** | (C-4..C-9 are done; § "C-4..C-9".) (1) **Post-close review cycle 5 on this commit (both lenses), then push the five post-close commits** (§ "Post-close" records cycles 1-4). The deletion was made from this checklist. **The checklist, as it stood: delete the S145-only Opus exception from `.claude/hooks/sprint-close-guard.ps1` — owner ruling R-104; Opus N-3.** T23/T24, the banner count (now 23) and the comments that named T23 were already handled before the close by R-139. What remains: (a) the exception's two lines and its comment block (`$acceptedReviewModels`, `if ($sprintNum -eq '145')`); (b) **the model comparison restored to `if ($reviewedBy -ne $reviewFloor)`** (cycle 8 NOTE 1 / Codex WARNING: `e2ce7dfc` changed it to `$acceptedReviewModels -notcontains $reviewedBy`, and deleting the variable alone would refuse every close); (c) the "owner exception" branch of the telemetry note; (d) the harness's T23-T24 note, reworded to say the exception is gone, that "the mutation check showed T23 fails" (cycle 8 NOTE 2), and that T21 pins the floor for every sprint (cycle 8 NOTE 3); (e) `docs/WORKFLOW.md`'s note on the S145 exception (§ Model Routing, layer 4). The harness must show 23 of 23 afterwards. That commit gets its own review cycle (WORKFLOW Step 7a, post-Step-7a coverage). (2) **S146 pickups:** the unused `BuildPlanForLegacyCallersAsync` (Codex N2, deferred to keep the frozen patches valid; now free, since the evidence is complete); QUAL-214 (the Skema month-navigation race and the Danish date-range messages); SEC-051/052 (input bounds); the seat's NOTE 2 (seven Docker facts read `root.GetProperty("reason")` without an `Assert`, so a missing field fails as `KeyNotFoundException`; § "C-4..C-9") |
