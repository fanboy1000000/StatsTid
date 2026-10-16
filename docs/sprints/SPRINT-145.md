# Sprint 145 — payroll works week by week (QUAL-186), then months with a mid-month change become payable (QUAL-149/150)

| | |
|---|---|
| **Status** | **in progress — Stage A. The A.5 gate closed on 2026-10-15** (every Stage A task merged; the three Unit mutation runs caught 27 of 27, 14 of 14 and 3 of 3). **Gate A-CLOSE:** the evidence lists frozen (R-33..R-46) and `K_A` committed; next, the evidence runs in CI (CA-3..CA-5). Planning: refinement READY (rev 3.4, Step 4, five cycles), plan READY (`.claude/plans/PLAN-s145.md` draft 5 plus 30 task briefs in `.claude/plans/s145/`; Step 0b, five cycles, both lenses READY at cycle 5) |
| **Test Verified** | **Locally only** (O-1 at `07f7c78`, 2026-10-15; tsc and vitest at `9d950ec`, the frontend unchanged since): build 0 errors / 145 warnings · CA2100 115 · Unit 1477 (baseline 1309; +168) · DemoSeed 170 · Regression non-Docker 128 · tsc 0 · vitest 1002. **CI has not yet run any S145 code.** That is by design: this project pushes master only at sprint close (`PLAN-s145.md` § "Standing constraints"), and CI first sees Stage A on the evidence branch at the Stage A gate (CA-4, `s145-evidence-a`) |
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

**Results (marked placeholders; filled at CA-7 by a docs-only commit):**
- `⟨CA-3⟩` EA-1..EA-5 shas on `s145-evidence-a`, each `git diff K_A <EA-k>`.
- `⟨CA-4⟩` V-A: the run id, `headSha == K_A`, all seven jobs green.
- `⟨CA-5⟩` EA-1..EA-5: each run id, head sha, and its red set against its frozen list ("matched").
- `⟨CA-6⟩` the PR number, "closed unmerged, branch deleted" (both sides).
- `⟨CA-7⟩` QUAL-186 and QUAL-150 → BUILT (plan § "Records owed"); Stage A closed.

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

## Handoff block (written at every gate — the state lives here, not in the conversation)

| | |
|---|---|
| **As of** | 2026-10-15, Gate A-CLOSE CA-2 done. Local master is this log commit, **`K_A`** (message `S145 Stage A: …`), on top of `07f7c78` (the R-33 merge). Not pushed to master, by design |
| **Evidence** | `K_A` = `923f7fb` · **CA-4 started 2026-10-15:** `K_A` pushed to `origin/s145-evidence-a`; draft PR **#37** ("EVIDENCE ONLY — S145 Stage A (never merge)"); **V-A = CI run `38747749039`, head `923f7fb` = `K_A`: all seven jobs green.** From the TRX artifact: Unit 1477 · DemoSeed 170 · Regression **2127** (Docker included), all passed, 0 failed; smoke and e2e green. This was the first execution of every Stage A Docker pin. Docs-consistency, CI's first check of O-3's hand-transcribed `db-schema.md`, passes · **CA-3 done:** EA-1 `de005a5` → EA-2 `5cd11b7` → EA-3 `e199a3a` → EA-4 `b804d00` → EA-5 `4dc474f`, each `K_A` plus one run's mutations (production files only). Each builds with 0 errors and keeps CA2100 at 115, Unit 1477, DemoSeed 170 and Regression non-Docker 128. The diffs and logs are in `.claude/plans/s145/ca3/` · **CA-5** (each run compared mechanically, from its TRX artifact plus the smoke job's log, against the frozen list; the Fable seat confirms all five at the end):
  - **EA-1:** run `38763657423`, head `de005a5`. **Matched:** 59 red = the 59 listed, none missing, none extra; Regression 2127 executed, 59 failed; Unit 1477 and DemoSeed 170 green; smoke and e2e green.
  - **EA-2:** run `38778726507`, head `5cd11b7`. **Matched:** 38 red keys = the 38 listed (40 failed rows; S8 is four rows), none missing, none extra; Unit and DemoSeed green. The smoke job's only failure is SMK (1 failed, 6 passed); e2e is green.
  - **EA-3:** run `38793589235`, head `e199a3a`. **Mismatch.** The 19 listed facts are red, none missing, plus FB-1..FB-6, whose cause is that MD-18 as worded reached first bookings (R-47). `frontend-build` was also red, on the unrelated flaky QUAL-207 (R-49). **Superseded by EA-3′** (`b1857fd`, `K_A` + the corrected set), which runs after EA-5: ⟨run, matched?⟩.
  - **EA-4:** run `38871341441`, head `b804d00` (`git diff --stat 923f7fb`: 3 files, as built). **Matched:** 20 red = the 20 listed, none missing, none extra. Regression 2127 executed, 20 failed. Unit 1477 and DemoSeed 170 green. Smoke, e2e and `frontend-build` green. TRM and SHO green, as R-48 expected. The R-51 census check before judging found no case-(b) fact for MD-12 and no seedable re-hire or gap fact for MD-46.
  - **EA-5:** run `38876775857`, head `4dc474f` (3 files). **Matched:** 12 red = the 12 listed, none missing, none extra. Regression 2127 executed, 12 failed. Unit and DemoSeed green. Smoke, e2e and `frontend-build` green. This confirms in CI the re-formed MD-55 and FS-2 under MD-59 (R-38, R-39).
  - **EA-3′:** run `38876840945`, head `b1857fd` (8 files; differs from EA-3 in exactly the two R-47 lines). **Matched:** 19 red = the 19 listed, none missing, none extra. FB-1..FB-6 green (R-47, R-48). Regression 2127 executed, 19 failed. Unit and DemoSeed green. `frontend-build` green on this tree, which satisfies R-49 item 2. Smoke and e2e green.
  - **All five lists matched** (EA-3 through EA-3′). Sent to the Fable seat for the CA-5 confirmation (2026-10-16).
- **QUAL-207 fix, prepared alongside CA-5 and merged after CA-7 (R-49):**
  - **The commits:** `ux` agent, branch `s145/qual-207`, `015f680`, plus the Orchestrator's `77f6ae5` (Small Tasks Exception).
  - **The fix:** the drawer's baselines are seeded where their values are published. The profile baseline is seeded in `hydrate()`, in the same step as the loaded profile. The agreement-code baseline is seeded in the open effect, beside `setStamdata`.
  - **The test:** a new fact lands an edit deterministically between the hydrated commit and the re-baseline effects, through a layout effect of the wrapped `ScheduledChangeNotice`. It was observed RED 3 times on the unfixed component, with CI's error.
  - **Checks:** `tsc` 0; vitest 81 files / **1003** (+1); the formerly flaky fact passed 20 of 20 in a loop.
  - **5α PASS; Fable 5a cycle 1 APPROVED-WITH-WARNINGS** (`015f680`). WARNING 1: the agreement baseline was seeded later than its value, so a direct person switch could keep the first person's code. That path is unreachable through today's host, but was closed in `77f6ae5` and verified by the reviewer's own scenario. NOTEs 1–3 applied. NOTE 4: the "edits during loading are kept" claim is moot, because fields are disabled while loading, so it is not a behaviour change.
  - **Cycle 2 APPROVED** (`77f6ae5`). Codex not required (UI only).
  - *Why they ran at once (Orchestrator, 2026-10-16).* The Regression step takes about 1 h 44 min in CI on every run, V-A included. `ci.yml` has no concurrency group, so a push to the branch does not cancel a running run, and each push starts its own run on its own head. EA-5 and EA-3′ were therefore pushed while EA-4 was still running: the same branch, the same PR, the same fast-forward pushes. Each run is still judged only against its own head's tree (`git diff 923f7fb <head>`, R-50), and only the waiting is dropped. This saves about 3½ hours on a memory-constrained machine.
  - The chain: `K_A` → EA-1 `de005a5` → EA-2 `5cd11b7` → EA-3 `e199a3a` → EA-4 `b804d00` → EA-5 `4dc474f` → EA-3′ `b1857fd`. · K ⟨close⟩ · EB ⟨Stage B⟩ |
| **Phase** | Step 1, Gate A-CLOSE: CA-1 done, the lists frozen (R-33..R-46), R-33's split merged, CA-2 done, `K_A` committed; CA-3 (the EA chain) next (2026-10-15). CA-1's full report and mutation patches are kept, untracked, in `.claude/plans/s145/ca1/` (the CA-3 builder works from them, amended by TASK-14518's "Amended at the freeze" section). (On 2026-10-14 the log was first caught up after a twelve-day pause: the 2026-10-02 session had ended right after the 14507 merge and TASK-14506's rebase. The six ledger rows for 14501–14504 and 14507 were written then from the merge commits, the branches and a fresh O-1.) The briefs go to the agents by absolute path (`.claude/plans/s145/TASK-*.md`, untracked, so absent from worktrees), read in full as the contract. `docs/CONVENTIONS.md` and the plan's standing constraints are pasted verbatim into every prompt |
| **Dispatched tasks / worktrees** | **Merged:** 14500 (`1b6160c`), 14505+T (`ce51a38`), 14501 (`5c35ffb`), 14502 (`25eb8eb`), 14503a (`32b668a`, `6e4592c` only), 14503b (`510619e`), 14504 (`630bf89`), 14507 (`94dea75`), 14506 (`379567e`). O-9 done (`9b75848`), O-2 done (no drift). 14506 re-spawn 2 done: 27 of 27 mutations tripped their pins, and its throwaway checkout is removed. **Merged in A.2:** 14510 (`d928cf5`), 14516 (`46cecd8`), 14509+T (`755cb14`), 14508+T (`f0a863b`). **Merged in A.3:** 14511+T (`2d14003`). O-10 merged (`a59825b`). **Merged in A.4:** 14512+T (`250fcdf`), 14514 (`49c00e5`); 14514 re-spawn 2 done: 14 of 14 mutations tripped, its throwaway checkout removed. **Merged in A.5:** 14513+T (`07337ea`), 14515 (`eb78b8e`), 14519+T (`8367862`); 14515 re-spawn 2 done: 3 of 3 mutations tripped, its throwaway checkout removed. **Gate A-CLOSE:** TASK-14518 CA-1 returned (its throwaway checkout removed); the freeze R-33..R-46; the R-33 split merged (`07f7c78`). **To clear at teardown:** `s145-task-14518-rs` (merged); the A.5 worktrees `s145-task-14513`, `s145-task-14515`, `s145-task-14519` (all merged); the merged worktrees `agent-ac1278752784ecede`, `agent-ad9053a02a651ddfb`, `agent-a1e2cddcd1ccda107`, `agent-a219bb4679ce74dae`, `s145-task-14503b`, `s145-task-14504`, `s145-task-14507`; `agent-a0fe62b0b3b73d252` (14503a; its `c3543f1` file is now merged byte-identical), `agent-a97b8efd3e8cd8e90` (14506, merged), and the A.2 worktrees `s145-task-14508`, `s145-task-14509`, `s145-task-14510`, `s145-task-14516` (all merged); the A.3/A.4 worktrees `s145-task-14511`, `s145-task-14512`, `s145-task-14514` and O-10's `s145-o10` (all merged); and the two unregistered directories `agent-a9efbf24c4db49318` and `agent-aa8d09f81f61764a1` |
| **Pending gates** | **Gate A-CLOSE:** CA-3 (TASK-14518 re-spawn: EA-1 → EA-5 on `s145-evidence-a` from `K_A`, not pushed) → CA-4 (V-A: push `K_A` to `s145-evidence-a`, a draft PR, all seven jobs green) → CA-5 (fast-forward to each EA-k in turn; the Fable seat compares each red set with its frozen list) → CA-6 (close the PR unmerged, delete the branch on both sides) → CA-7 (docs-only record; QUAL-186 and QUAL-150 → BUILT). The Docker side of everything merged so far is unobserved until CA-4 |
| **Open rulings** | None for the owner. The reopen hook's `EffectiveFrom` ruling was confirmed at the batched Fable 5a |
| **Next action** | CA-3: dispatch the TASK-14518 re-spawn (`test-qa`) to build EA-1 → EA-5 from `K_A` (the brief's CA-3 text as amended at the freeze; the sets and lists in § "Gate A-CLOSE" → "The frozen lists"). It reports every sha and `git diff K_A <EA-k>`. Then CA-4. Gate builds on this machine run in the foreground with `-m:2` (the host stops a backgrounded build when memory is low) |
