# Sprint 145 — payroll works week by week (QUAL-186), then months with a mid-month change become payable (QUAL-149/150)

| | |
|---|---|
| **Status** | **in progress — Stage A. The A.1 gate closed on 2026-10-14** (every A.1 task merged; the Unit mutation run caught 27 of 27). Next: wave A.2. Planning: refinement READY (rev 3.4, Step 4, five cycles), plan READY (`.claude/plans/PLAN-s145.md` draft 5 plus 30 task briefs in `.claude/plans/s145/`; Step 0b, five cycles, both lenses READY at cycle 5) |
| **Test Verified** | **Locally only** (O-1 at `379567e`, 2026-10-14): build 0 errors / 145 warnings · CA2100 115 · Unit 1421 (baseline 1309; +112 from TASK-14506) · DemoSeed 170 · Regression non-Docker 128. **CI has not yet run any S145 code.** That is by design: this project pushes master only at sprint close (`PLAN-s145.md` § "Standing constraints"), and CI first sees Stage A on the evidence branch at the Stage A gate (CA-4, `s145-evidence-a`) |
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

## Handoff block (written at every gate — the state lives here, not in the conversation)

| | |
|---|---|
| **As of** | 2026-10-14, the A.1 gate closed (see § "A.1 gate — closed 2026-10-14"). Local master is this log commit, on top of `379567e` (the TASK-14506 merge). It is not pushed, by design: master is pushed only at sprint close |
| **Phase** | Step 1, wave A.2 dispatched (2026-10-14). (On 2026-10-14 the log was first caught up after a twelve-day pause: the 2026-10-02 session had ended right after the 14507 merge and TASK-14506's rebase. The six ledger rows for 14501–14504 and 14507 were written then from the merge commits, the branches and a fresh O-1.) The briefs go to the agents by absolute path (`.claude/plans/s145/TASK-*.md`, untracked, so absent from worktrees), read in full as the contract. `docs/CONVENTIONS.md` and the plan's standing constraints are pasted verbatim into every prompt |
| **Dispatched tasks / worktrees** | **Merged:** 14500 (`1b6160c`), 14505+T (`ce51a38`), 14501 (`5c35ffb`), 14502 (`25eb8eb`), 14503a (`32b668a`, `6e4592c` only), 14503b (`510619e`), 14504 (`630bf89`), 14507 (`94dea75`), 14506 (`379567e`). O-9 done (`9b75848`), O-2 done (no drift). 14506 re-spawn 2 done: 27 of 27 mutations tripped their pins, and its throwaway checkout is removed. **Merged in A.2:** 14510 (`d928cf5`). **Running:** 14508-T (paired pass on `s145/task-14508`, implementer `4cf072c`) · 14509-T (paired pass on `s145/task-14509`, implementer `d61dc3a`) · the adjudicator on 14508's deviation and the month-end night-shift 500 · the adjudicator on 14509's deviation and two scope questions · the 14516 Step-5a fix-up (`s145/task-14516`) · 14514 authoring (`s145/task-14514`) · 14515 authoring (`s145/task-14515`). **Not yet dispatched:** O-10, after 14508-T merges, because both edit `PayrollHostRecalcBlockedTests.cs`. **To clear at teardown:** the merged worktrees `agent-ac1278752784ecede`, `agent-ad9053a02a651ddfb`, `agent-a1e2cddcd1ccda107`, `agent-a219bb4679ce74dae`, `s145-task-14503b`, `s145-task-14504`, `s145-task-14507`; `agent-a0fe62b0b3b73d252` (14503a; its `c3543f1` file is now merged byte-identical), `agent-a97b8efd3e8cd8e90` (14506, merged); and the two unregistered directories `agent-a9efbf24c4db49318` and `agent-aa8d09f81f61764a1` |
| **Pending gates** | Wave A.2 (`PLAN-s145.md` wave table): 14508 hours read + host contract (`payroll-integration`) → 14508-T · 14509 A6 + rebuilder + `DailyNormCalculator` (`backend-infrastructure`) → 14509-T · 14510 trigger label (`ux`) · 14516 negative-hours guard in the grid (`ux`) · 14514 and 14515 authored from the spec (`test-qa`) · O-10, the post-A.1 test tidy (`test-qa`, beside 14514's authoring). **A.2 gate:** 14508+T, 14509+T, 14510 and 14516 merged · O-1 including `tsc --noEmit` and vitest · O-2 check. The Docker side of everything merged so far, including 14507's turning 24 known-red facts green (B1–B3, TRM, SHO, S1–S13, Q1–Q7, counting S8's four rows), is unobserved until the Stage A gate's CI run (CA-4) |
| **Open rulings** | None for the owner. The reopen hook's `EffectiveFrom` ruling was confirmed at the batched Fable 5a |
| **Next action** | Process the adjudicator rulings; then, per combined branch, the build, 5α, Fable and Codex for 14508+T and 14509+T; the 14516 fix-up's cycle 2; O-10 after 14508-T merges |
