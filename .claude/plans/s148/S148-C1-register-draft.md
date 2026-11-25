# S148 close, plan step C-1: the register batch, exact draft

| | |
|---|---|
| **Sprint** | S148 (close, plan step C-1) |
| **Drafted by** | Planning Drafter (Opus 5.5). The Orchestrator rules and applies; nothing here is applied |
| **File** | `.claude/plans/s148/S148-C1-register-draft.md`. The brief asked for `.claude/reviews/S148-C1-register-draft.md`; the drafter may write only under `.claude/plans/**` and `.claude/refinements/**`, so it is here (the comment-sweep spec beside it took the same route) |
| **Base commit** | every code and register cite verified at `6ab643a7` (HEAD; that commit touched only `docs/sprints/SPRINT-148.md`). Sprint-log line numbers are at `6ab643a7`: the C-1 ledger commit moved the audit report's numbers by +61 after line 30 |
| **Sources** | `.claude/reviews/S148-C1-audit-report.md` (§ 1, § 2, § 4), `.claude/plans/PLAN-s148.md` (the O-items `:783-817` and "Records owed" `:838-860`), `.claude/plans/s148/ADDENDUM-A1-cell-clearing.md:442-450`, `docs/sprints/SPRINT-148.md`, the S148 review files named per block, and `.claude/plans/s148/C1-comment-sweep-spec.md` (to avoid double work) |

## What this is, in plain words

The four registers (quality, security, performance, HR follow-up) are the project's memory of every known weakness: one row per problem, with its state, where it lives in the code, and what would close it. During S148 the team fixed several registered problems and found new ones, but the paperwork lagged: closed rows still say "open", and findings the reviews asked to register at close exist only in the sprint log. This draft is that paperwork, ready to paste.

- **Part A** edits rows that already exist: closures with their evidence (the merge commit and the CI run that proved it), dated notes, refreshed code locations. Each edit is an exact text replacement, checked to match exactly one place in its file.
- **Part B** adds new rows, each with its proposed severity. The quality register only takes Medium and above ("would plausibly change behaviour, block or mislead a future change, or misinform a reader"); smaller items get a "below the floor" line instead, so they are recorded but do not clutter the register.
- **Below the floor** lists what is recorded, not registered, worded for the sprint log.
- **Part C** lists the choices that are the Orchestrator's or the owner's, not the drafter's.

A finding that turned out to be an existing row is not registered twice; where the existing row needed the S148 facts, Part A carries a dated note on it instead.

## Counts

| | Count | Ids |
|---|---|---|
| **Edits to existing rows (Part A)** | 22 blocks, 26 replacements | QUAL-202, -232, -246, -237, -240, -231, -239 (2), -236, -234, -123, -182, -016, -195, -118; SEC-046, -061 (2); HRP-013, -024; F10 (2), F9; the audit-projection catalog (3) |
| of which the brief named | 14 blocks | A-1 … A-14 |
| of which found while drafting | 8 blocks | A-15 (QUAL-182, owed by the plan's Records owed), A-16 … A-18 (dedupe notes), A-19 (F9), A-20 (HRP-024, optional), A-21 (F-3's catalog half), A-22 (the builder's F5, optional) |
| **New QUAL rows (Medium)** | 9 | QUAL-271 … QUAL-279 |
| **New QUAL-BF row** | 1 | QUAL-BF-011 |
| **New SEC rows** | 0 | (one pre-production ledger line, B-12) |
| **New F rows** | 1 | F11 |
| **New HRP rows** | 0 | |
| **Below the floor (recorded, not registered)** | 27 | the list after Part B |
| **Duplicates of an existing row** | 6 | QUAL-252, QUAL-251 (no edit); QUAL-016, QUAL-195, QUAL-118, QUAL-123 (a dated note each, Part A) |
| **Already fixed or already recorded** | 5 | the backfill tool's counters; the `ProjectionBackfillService.cs:133` summary; the truncated-chain month-edge limit; payroll's anonymous 500 for a record gap; the dead writer `AdjustUsedAsync` (already in the register's below-floor appendix) |

Highest ids at HEAD: QUAL-270, QUAL-BF-010, SEC-069, F10. New ids continue from there.

Line endings: the quality, security and HR registers and the catalog are LF; **the performance register is CRLF** (keep CRLF on any line inserted there).

---

## Part A — edits to existing rows

### A-1 · quality-finding-register · QUAL-202 (O-2)

**Plain words.** "The activity-type field is free text, so a typo is paid as ordinary hours." After S148 nothing can store a type (the old registration route is gone, and the Skema screen always saves "ordinary work"), and payroll refuses, by name, a month that still holds a typed row. Refinement ruling R-25 closes the row now; the larger design (a fixed list of activity types) stays open under QUAL-232.

**Evidence.** Retired route: `TimeEntryRouteRetiredTests.cs:64` (`Post_Answers404Or405_GetStill200`, AC-S0-1a). Refusal: `Payroll/Program.cs:1009-1054`, called at `:354` (`/calculate-and-export`) and `:713` (`/recalculate`), ordinal comparison at `:1037-1039`. Skema writes `NORMAL`: `SkemaEndpoints.cs:2991`. `git ls-files` finds neither `RequestValidator.cs` nor `TimeEntryForm.tsx`. Merges `e4e6b0ea` (TASK-14802 stack) and `fc91e554` (TASK-14803). CI-W1 run 43011040973 (`SPRINT-148.md:727-746`). R-25: `.claude/reviews/REFINEMENT-s148-adjudication-c1.md:66`; plan O-2 `PLAN-s148.md:783`.

**old_str**
````text
**REGISTERED (S145 TASK-14513 return, adjudicator NOTE, 2026-10-15).** Outside 14513 (Backend + frontend) | `RequestValidator.cs` (`ValidateTimeEntry`); `TimeEntryForm.tsx:121-128`; `SupplementRule.cs`, `OnCallDutyRule.cs`, `CallInWorkRule.cs`, `TravelTimeRule.cs` (the exact-code matches) |
````

**new_str**
````text
**CLOSED (S148, 2026-11-25; refinement ruling R-25; TASK-14802 `e2864079`, merged `e4e6b0ea`, with TASK-14803 merged `fc91e554`; CI-verified at CI-W1, run 43011040973: AC-S0-1a, -1b and -1c green).** No route accepts an activity type, and payroll refuses a stored one by name. `POST /api/time-entries` is retired with its form and its validator (AC-S0-1a: 404 or 405, nothing written); the Skema save, the only time writer left, stores every entry as `NORMAL`; both calculating payroll routes answer 422 `payroll-activity-type-refused` for a month whose calculation window holds an entry typed other than null or `NORMAL`, compared exactly, so a typo such as `normal` or `timer` is refused, never paid (AC-S0-1b). R-25 closes this row at S148 rather than with QUAL-232, which keeps the closed set of activity types (ADR-043 D11, stage 2 or later). Two neighbours stay registered: a stored typed row has no in-product remedy (QUAL-BF-003), and a projection rebuild could restore one after payroll's check (QUAL-273). *Before S148:* **REGISTERED (S145 TASK-14513 return, adjudicator NOTE, 2026-10-15).** Outside 14513 (Backend + frontend) | `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:1009-1054` (`RefuseTypedEntriesAsync`), called at `:354` and `:713`; `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:2991` (`ActivityType = "NORMAL"`); `tests/StatsTid.Tests.Regression/Hosting/TimeEntryRouteRetiredTests.cs:64`; `tests/StatsTid.Tests.Regression/Payroll/PayrollActivityTypeRefusalTests.cs` (at `6ab643a7`). As found: `RequestValidator.cs` (`ValidateTimeEntry`) and `TimeEntryForm.tsx:121-128`, both deleted in S148; `SupplementRule.cs`, `OnCallDutyRule.cs`, `CallInWorkRule.cs`, `TravelTimeRule.cs` (the exact-code matches) |
````

### A-2 · quality-finding-register · QUAL-232 (O-2, the interim)

**Plain words.** On-call, call-in and travel hours were paid twice. S148's stage 0 does not build the correct model yet; it stops the wrong payment: such hours can no longer be entered, and payroll refuses a month that holds one. The row stays open for the real model in stage 2. One fact differs from the row's own plan text: the old route was removed outright instead of being made to answer 422.

**Evidence.** As A-1; the engine's typed rules unchanged: `OnCallDutyRule.cs:38`, `CallInWorkRule.cs:39`, `TravelTimeRule.cs:37`.

**old_str**
````text
on-call's 0.33 payment and the 0.5 non-working travel rate retire. |
````

**new_str**
````text
on-call's 0.33 payment and the 0.5 non-working travel rate retire. **S148 interim, built (O-2, 2026-11-25; TASK-14802 merged `e4e6b0ea`; CI-verified at CI-W1, run 43011040973):** on-call, call-in and travel hours can no longer be registered or paid. The legacy route was retired rather than made to answer 422 (AC-S0-1a: 404 or 405), so no route accepts a type and the Skema save writes `NORMAL`; payroll refuses by name any month whose calculation window holds a stored entry typed other than null or `NORMAL` (422 `payroll-activity-type-refused`, `Payroll/Program.cs:1009-1054`; AC-S0-1b). The engine's typed rules are unchanged (`OnCallDutyRule.cs:38`, `CallInWorkRule.cs:39`, `TravelTimeRule.cs:37`) and no longer reachable from payroll. The row stays open for stage 2's sourced model (three facts per type and agreement). |
````

### A-3 · security-finding-register · SEC-046 (O-2, the note)

**Plain words.** SEC-046 was "a person who has left can still register time with a token that has not expired". It was fixed in S136 on two writers. One writer, the old time-entry route, is now gone, and its tests went with it; every guarantee is still pinned on the surviving writer, the Skema save. The note says which pins retired and where their twins are, so a later reader does not think coverage was lost.

**Evidence.** `.claude/reviews/S148-TASK-14802-T-report.md` § 6 (the retired pins) and Step 5a Fable W-1 (`S148-TASK-14802-5a-reviewer.md`); twins at `EmploymentWindowRaceTests.cs:78`, `:127`; `TerminatedEmployeeAccessTests.cs:613`; `DepartedSelfServiceTests.cs:74`; `EmploymentWindowRegistrationGateTests.cs:240`. The Skema save's subject read `SkemaEndpoints.cs:799` and in-lock re-check `:2490-2513`. `TimeEndpoints.cs` now maps only `GET` routes (`:15`, `:73`).

**old_str**
````text
`TerminatedEmployeeAccessTests.cs:653`; Step 0b P-12). |
````

**new_str**
````text
`TerminatedEmployeeAccessTests.cs:653`; Step 0b P-12). **S148 note (O-2, 2026-11-25): one writer left, and the route's pins retired with it.** `POST /api/time-entries` is retired (TASK-14802, merged `e4e6b0ea`; CI-verified at CI-W1, run 43011040973), so the Skema save is the only self-service time writer; its subject read and its in-lock re-check stand (`SkemaEndpoints.cs:799`, `:2490-2513`), and `TimeEndpoints.cs` keeps only its `GET` routes (`:15`, `:73`). TASK-14802-T retired the route's own SEC-046 pins, each beside a surviving Skema twin: the two time-entry races in `EmploymentWindowRaceTests` (twins `WindowEditWins_ThenSkemaSave_…` `:78` and `DeactivationWins_ThenSkemaSave_…` `:127`), `TerminatedEmployeeAccessTests` `:525` and `:557`, and `DepartedSelfServiceTests`' own-time-entry 403 (twin `Employee_OwnSkemaSave_403`, `:74`). The S147 re-decided pin above is now `SkemaSave_DeactivatedHrActor_OwnInWindowFact_Is403_ReDecidedS147` (`TerminatedEmployeeAccessTests.cs:613`), and the "same refusal body before the start and after the end" pin, this row's date half, was rebuilt on the Skema save (`EmploymentWindowRegistrationGateTests.SkemaSave_BeforeStartAndAfterEnd_RefusalBodiesAreByteIdentical`, `:240`; Step 5a Fable W-1) (all at `6ab643a7`). |
````

### A-4 · quality-finding-register · QUAL-246 (O-3)

**Plain words.** When payroll refused a month because one person had signed it twice, it gave the same generic "export conflict" as every other conflict, and only after calculating. Now it checks first and answers with a refusal of its own ("four-eyes"), so a future HR screen can tell the user what to do. The row offered two options; option (a) was built.

**Evidence.** `Payroll/Program.cs:341-349` (admission), `:570-576` (export-time catch); `PayrollExportService.cs:592-594` (the in-transaction re-check), `:1147-1151` (the fixed sentence). Facts `PayrollFourEyesRefusalTests.cs:66`, `:124`. Merge `c18d525b` (`SPRINT-148.md:461`); CI-W1 run 43011040973 (`:727-746`: "the four-eyes admission and race theories"). A1: `.claude/reviews/S148-wave1-briefs-adjudication.md`.

**old_str**
````text
**REGISTERED (S147 C-1, 2026-11-04; TASK-14704 reviews, tidy item 12, second bullet; tidy item 12b, class D).** Not fixed in S147: a design fork on the payroll problem contract (a high-risk export change), with Docker-only pins. Meanwhile the shared kind is documented at the handler (`Program.cs:495-502`) |
````

**new_str**
````text
**FIXED (S148, 2026-11-10; TASK-14801 `e7fcb023` with -T `11172d86`, merged `c18d525b`; CI-verified at CI-W1, run 43011040973: `PayrollFourEyesRefusalTests`, AC-12b-a and -b).** This row's option (a): `/calculate-and-export` checks both equalities on the approval row it reads at admission, before anything is calculated or written, and answers its own 409 `payroll-four-eyes` (`Payroll/Program.cs:341-349`); the export transaction's row-locked re-check calls the same rule (`PayrollFourEyesException.FailedClause`, `PayrollExportService.cs:592-594`) and gives the same answer (`Program.cs:570-576`). A caller can now tell "a second person must approve this month" from an export conflict. The body is fixed and claims only what is true at both sites ("Nothing was exported to payroll", `PayrollExportService.cs:1147-1151`) (at `6ab643a7`). `/recalculate` has no approval read by ADR-034's design, so it carries no four-eyes refusal (wave-1 brief adjudication A1). *Before S148:* **REGISTERED (S147 C-1, 2026-11-04; TASK-14704 reviews, tidy item 12, second bullet; tidy item 12b, class D).** Not fixed in S147: a design fork on the payroll problem contract (a high-risk export change), with Docker-only pins. Meanwhile the shared kind is documented at the handler (`Program.cs:495-502`) |
````

### A-5 · quality-finding-register · QUAL-237 (O-4, with the R-23 note)

**Plain words.** The server's daily absence limit looked only at the cells in one save, so absences saved one by one could exceed the day. Now the save judges the day as it will stand (stored rows plus the new ones, both directions, inside the employee's lock): paid absence may not exceed planned hours minus worked hours. Until stage 1, "planned" is the agreement's standard daily norm, not an institution's own setting (ruling R-23), and that stand-in is registered as QUAL-BF-001.

**Evidence.** `SkemaEndpoints.cs:2065` (`FindPaidAbsenceDayBoundBreachAsync`), called at `:2410` (advisory) and `:2857` (in the lock); `kind` at `:2128`; the shortcut at `:2110-2111`. Facts `PaidAbsenceDayBoundTests.cs:71`, `:93`, `:124`, `:184`; `PaidAbsenceDayBoundRaceTests.cs:46`, `:82`. Merge `72f286fe` (`SPRINT-148.md:653`); CI-W1 run 43011040973. R-23: `REFINEMENT-s148-adjudication-c1.md:64`; plan O-4 `PLAN-s148.md:785`.

**old_str**
````text
**REGISTERED (owner UI testing, 2026-10-28).** Owner: backlog, built with the absence day rules (ROADMAP, Correctness / domain), not the next sprint. Raise to H if the server's norm or flex figures count the excess (not traced yet) |
````

**new_str**
````text
**FIXED (S148, 2026-11-10; TASK-14804 `cf0d4aef` with fix-up `8415a2a2`, -T `c01758bd` and `2f1d3a57`, merged `72f286fe`; CI-verified at CI-W1, run 43011040973: `PaidAbsenceDayBoundTests`, AC-S0-7a … -7d, and `PaidAbsenceDayBoundRaceTests`, AC-S0-7e).** The Skema save now judges the day as it will stand, not the request: a day's credit-class paid absence may not exceed max(0, planned − worked), counting the stored rows the save does not replace, in both directions, as an advisory check before the transaction and authoritatively inside the per-employee lock (`SkemaEndpoints.cs:2065`, called at `:2410` and `:2857`); a breach answers 422 with `kind` `paid-absence-day-bound` (`:2128`). The `admin01` shape (Ferie 7,4 then Sygdom 7,4) is AC-S0-7a. Owner Q1 (2026-11-10): a care or senior day goes only on a day with no work. A day a mistaken cell had locked can be emptied since TASK-14840 (QUAL-248). **Note (R-23):** until stage 1 the bound reads the § 7 nr. 2 per-day norm (`FullDayHoursAsync`), not the institution's configured credit basis; the two are equal wherever no office hours are set; registered as QUAL-BF-001 (`SkemaEndpoints.cs:2110-2111`) (at `6ab643a7`). *Before S148:* **REGISTERED (owner UI testing, 2026-10-28).** Owner: backlog, built with the absence day rules (ROADMAP, Correctness / domain), not the next sprint. Raise to H if the server's norm or flex figures count the excess (not traced yet) |
````

### A-6 · quality-finding-register · QUAL-240 (O-4)

**Plain words.** A sick day or paid leave entered on a weekend was credited up to 7.4 hours of flex for a day the person was never planned to work. The same bound as A-5 applies with "planned" = 0 on such a day, so the save now refuses it and says why.

**Evidence.** `PaidAbsenceDayBoundTests.cs:184` (`SaturdaySickAndSaturdayPaidLeave_422`, AC-S0-7d); the re-pin `Adr032ConsumptionPinTests.cs:246` (`Guard_WeekendSickDay_Rejected422_PaidAbsenceDayBound`). Merge `72f286fe`; CI-W1 run 43011040973. AC-S0-7g's alert: TASK-14810, merged `540ac9a9`.

**old_str**
````text
**REGISTERED (S147 TASK-14708 report, 2026-10-29; the implementer's brief challenge).** Closes with R-7 as widened (ADR-043 batch-3 ruling 3, S148 stage 0): a day's paid-absence hours may not exceed the credit basis minus the hours worked that day, judged at the Skema save in both directions. Its credit basis on a weekend is 0. Not built in S147 (the cap is outside TASK-14708's authority) |
````

**new_str**
````text
**FIXED (S148, 2026-11-10; TASK-14804, merged `72f286fe`; CI-verified at CI-W1, run 43011040973: `PaidAbsenceDayBoundTests.SaturdaySickAndSaturdayPaidLeave_422`, AC-S0-7d).** QUAL-237's bound applies with a planned figure of 0 on a zero-norm day, so a weekend sick day or paid-leave day is refused (422, `kind` `paid-absence-day-bound`) instead of credited up to 7.4 h; the alert names the day's planned hours in ruled Danish (AC-S0-7g, TASK-14810). The old weekend sick-day pin was re-pinned to the refusal (`Adr032ConsumptionPinTests.Guard_WeekendSickDay_Rejected422_PaidAbsenceDayBound`, TASK-14804-T). The planned figure is § 7 nr. 2's until stage 1 (QUAL-BF-001). *Before S148:* **REGISTERED (S147 TASK-14708 report, 2026-10-29; the implementer's brief challenge).** Closes with R-7 as widened (ADR-043 batch-3 ruling 3, S148 stage 0): a day's paid-absence hours may not exceed the credit basis minus the hours worked that day, judged at the Skema save in both directions. Its credit basis on a weekend is 0. Not built in S147 (the cap is outside TASK-14708's authority) |
````

### A-7 · quality-finding-register · QUAL-231, the residual (O-4)

**Plain words.** QUAL-231 itself closed in S147. Its leftover was a partly worked sick day: worked hours plus sick hours could exceed the day's plan and be over-credited. The A-5 bound makes that impossible to store.

**Evidence.** `PaidAbsenceDayBoundTests.cs:124` (`Worked30Sick44_Accepted_Sick45_422`, AC-S0-7c). Merge `72f286fe`; CI-W1 run 43011040973.

**old_str**
````text
an S148 candidate beside D-8 and ADR-043 R-7). Defect D4 of the analysis |
````

**new_str**
````text
an S148 candidate beside D-8 and ADR-043 R-7). Defect D4 of the analysis. **Residual closed (S148, 2026-11-10; TASK-14804, merged `72f286fe`; CI-verified at CI-W1, run 43011040973: `PaidAbsenceDayBoundTests.Worked30Sick44_Accepted_Sick45_422`, AC-S0-7c).** A partly worked sick day can no longer be stored above the day's plan: worked hours plus credit-class paid absence may not exceed the planned hours (QUAL-237's bound, judged in both directions), so the day is never credited above max(worked, planned), the sources' reading; recording fewer sick hours than the remainder credits what was recorded. |
````

### A-8 · quality-finding-register · QUAL-239 (O-5, and F-3's register half)

**Plain words.** The overtime "compensate" route could record "hours paid out" even when its own balance update had refused. Stage 0's interim fix reads the update's result and refuses (409) before anything is recorded. The row stays open because stage 2 retires the route. Its code locations had drifted (finding F-3 of the TASK-14805 report), so the location cell is refreshed too.

**Evidence.** `OvertimeEndpoints.cs:651-789` (the route), `:745-762` (guarded updates and the 409), `:764-769` (the enqueue); `OvertimeBalanceRepository.cs:76-129`. Fact `OvertimeCompensateRaceTests.cs:229`. Merge `14521382` (`SPRINT-148.md:603`); CI-W1 run 43011040973 ("the compensate race"). F-1/F-2/F-3: `S148-TASK-14805-report.md:47-50`; the untested AFSPADSERING race: `SPRINT-148.md:519`.

**old_str (A-8a)**
````text
S148 stage 0 makes the route read its guarded update's result and refuse (409) before enqueueing (the interim); stage 2 retires the route with the yearly balance. |
````

**new_str (A-8a)**
````text
S148 stage 0 makes the route read its guarded update's result and refuse (409) before enqueueing (the interim); stage 2 retires the route with the yearly balance. **S148 interim, built (O-5, 2026-11-25; TASK-14805 `aede6e63` with -T `0acde975`, merged `14521382` on 2026-11-10; CI-verified at CI-W1, run 43011040973: `OvertimeCompensateRaceTests.TwoConcurrentCompensationsOfSix_One200_One409_OneOutboxEvent`).** The route now reads the guarded update's `Success` and, when it refused, rolls back and answers 409 before anything is enqueued, so no event is recorded for hours that were not settled (`OvertimeEndpoints.cs:745-762`, the enqueue `:764-769`); the 200 path is unchanged. A race on the time-off (AFSPADSERING) branch was not built (both Step 5a lenses: NOTE). Two neighbouring defects of the compensation-choice PUT are QUAL-271 and QUAL-272. The row stays open until stage 2 retires the route. |
````

**old_str (A-8b)**
````text
`OvertimeEndpoints.cs:567-583` (the update and the enqueue); `OvertimeBalanceRepository.cs:76-99` (the guarded updates and their result);
````

**new_str (A-8b)**
````text
`src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs:651-789` (the route), `:745-762` (the guarded updates and the 409), `:764-769` (the enqueue); `src/Infrastructure/StatsTid.Infrastructure/OvertimeBalanceRepository.cs:76-129` (the guarded updates and their result) (at `6ab643a7`; re-cited 2026-11-25, TASK-14805 report F-3; as found `:512`, `:567-583`, `:76-99`);
````

### A-9 · hr-follow-up-process-register · HRP-013 (O-6)

**Plain words.** HRP-013 is HR's "employee with no approver" list. S147 found that the list and the roll-up across organisations counted a manager who had left as cover. TASK-14806 gave both one shared rule: an employee is covered when the approval routing names an approver for that day. The note uses the wording the evidence supports ("a designated approver is named", not "someone can approve", because approving also needs a leader role in the same organisation), records how the open `(unproven)` question R-19 was settled, and says why no performance row was needed.

**Evidence.** `ApproverCoverage.cs:5-56` (the rule and its doc; root at `:44-56`), callers `ApprovalPeriodRepository.cs:1233`, `HrFollowUpApprovalReadRepository.cs:786`; facts `OrphanCoverageTests.cs:89`, `:122`, `:152`, `:197`, `:264`. The wording: `S148-TASK-14806-report.md:114`; reading (i): `:42-43`; AC-21a-d's table and the R-19 note: `S148-TASK-14806-T-report.md:19-30`; the fourth reading: `S148-TASK-14804-14806-adjudication.md:45-50`. Merge `81705338` (`SPRINT-148.md:605`); CI-W1 run 43011040973 ("all `OrphanCoverageTests`, including the shape-F fact; … the S106 cost guard `Roster_IsOneScopedLoad…`, still at ≤ 5 commands"). The wording "D9's root that also has a report" follows the comment sweep's S-28 (N-1). Plan O-6 `PLAN-s148.md:787`.

**old_str**
````text
so "covered" needs a stated rule. An S148 stage-0 input (ROADMAP § 1). |
````

**new_str**
````text
so "covered" needs a stated rule. An S148 stage-0 input (ROADMAP § 1). **S148 note (O-6, 2026-11-25; TASK-14806 `5c76266f` with fix-up `38b800f6`, -T `ae3968a0` and `fee20add`, merged `81705338`; CI-verified at CI-W1, run 43011040973: the five `OrphanCoverageTests`, AC-21a-a … -d and the shape-F fact):** the card and the roll-up now share one rule, `ApproverCoverage.ClassifyAsync` (`ApproverCoverage.cs`; called at `ApprovalPeriodRepository.cs:1233` and `HrFollowUpApprovalReadRepository.cs:786`), so they agree by construction. An employee is **covered** when the approval walk names a designated approver for that day (`ReportingLineRepository.ResolveDesignatedApproverAsync`, called as it is): it skips a manager who has left from the day after their last day, keeps a stand-in appointed before the manager left, honours an acting line and climbs the chain, so the S147 gap above (a departed manager counting as cover from day 1 to day 5) is closed. The wording is "a designated approver is named", not "someone can approve": the approve routes also require a leader role in the same organisation, and a unit leader may approve where the walk names nobody (TASK-14806 report). An uncovered person is a **root**, kept off the list, only when nobody is named, they hold no open PRIMARY line of their own and someone in the read reports to them (ADR-027 D9's root that also has a report; the fourth reading, adjudication item 2a, which moves shape F's middle manager onto the list); every other uncovered person is an **orphan**. R-19's `(unproven)` NOTE is disposed: reading (i) ("someone's approver" = another person in the read reports to them) was settled against (ii) by AC-21a-d green at CI-W1, then narrowed by the fourth reading; lines from people outside the read do not count (the code's choice, `ApproverCoverage.cs:52-53`; no fact discriminates it). No F-row: the roster card's S106 cost guard stayed within 5 commands at CI-W1; an HR scope over several organisations costs three prefetch reads per organisation (QUAL-BF-002). The row stays NOT READY: whether an orphan is tolerable is still unruled. |
````

### A-10 · quality-finding-register · QUAL-236 (O-8, the register part)

**Plain words.** The child's-sick-day rules were wrong for every agreement (wrong number of days, counted per year instead of per illness, no "call-home" day). S148 built the sourced rules: days per illness from a dated setting (2 under OK24, 3 under OK26), consecutive days, the OK26 call-home day, no yearly cap. What is still provisional or not modelled is registered separately and named in the note.

**Evidence.** `SkemaEndpoints.cs:1144-1164` (`child-sick-not-offered`), `:2236-2341` (the illness chain); tests in `tests/StatsTid.Tests.Regression/Skema/ChildSick*.cs`, `Payroll/ChildSickCallHome*.cs`, `Balance/ChildSickAllowanceTileTests.cs`, `tests/StatsTid.Tests.Unit/ChildSickEntitlementConfigTests.cs`. Merge `26065e1a`; CI-W2 run 43233683529 (`SPRINT-148.md:1427`: "the child's-sick facts: 42 Regression and 10 Unit, including Rules A and B"); CI-M2b run 43238051149 (M-22, M-23 red as frozen). Plan O-8 `PLAN-s148.md:789`. O-8's other parts (`danish-agreements.md:234`, the source register's wage rows, ADR-016 D10's v3 footnote) are documents, not registers, and are not drafted here.

**old_str**
````text
**REGISTERED (S147 research pass, verified 2026-10-28, found in passing; Fable adjudication RR-10).** The docs rows are corrected in S147 (O-9); the code fix is S148 stage 0 unless the owner pulls it into S147 (Q-2) |
````

**new_str**
````text
**FIXED (S148, 2026-11-12; TASK-14811 with -T and TASK-14812, merged `26065e1a`; CI-verified at CI-W2, run 43233683529: the child's-sick facts, 42 Regression and 10 Unit, Rules A and B included; mutation evidence at CI-M2b, run 43238051149, M-22 and M-23).** The dated CHILD_SICK configuration now says how many days one illness gives: 2 under OK24 and 3 under OK26, for all five agreements. The Skema catalogue offers day 1 up to that number per the day's OK version, and the save refuses a code the agreement does not offer that day (`kind` `child-sick-not-offered`, `SkemaEndpoints.cs:1144-1164`), which also closes the S147 note's hand-built `CHILD_SICK_DAY_3`. Days follow an illness chain (day 2 after day 1, day 3 after day 2, a day with no planned hours skipped; the illness keeps its first day's OK version; `kind` `child-sick-chain`, `:2236-2341`), and there is no yearly cap. The OK26 call-home day (`CHILD_SICK_CALL_HOME`) is credited like a sick day and exported (marker `weekly-timeline-v3`). Owner Q5 added two rules: a new illness cannot start on the day after a child's-sick day, and a call-home day needs work that day. Residuals registered at the merge: QUAL-249 (the provisional `SLS_0530`), QUAL-250 (the other parent), QUAL-251 (`/summary`'s yearly row), QUAL-BF-004 (AC's OK26 rule provisional), QUAL-BF-005 (the call-home table in code), QUAL-BF-006 (illnesses told apart by code and adjacency) (at `6ab643a7`). *Before S148:* **REGISTERED (S147 research pass, verified 2026-10-28, found in passing; Fable adjudication RR-10).** The docs rows are corrected in S147 (O-9); the code fix is S148 stage 0 unless the owner pulls it into S147 (Q-2) |
````

### A-11 · quality-finding-register · QUAL-234, the rest arm (O-9)

**Plain words.** QUAL-234 had two halves: the clock times typed in Skema never reached (1) payroll's evening and night supplements, nor (2) the 11-hour rest check. Half (2) is now built and corrected: the rest check reads the clock times. Half (1), the supplements, is stage 2's, so the row stays open. The "not checked" notice stays for days not fully on the clock (owner told, R-22). The row's own text rates the supplement half alone as High, so the severity may come down from Critical; that is the Orchestrator's call (Part C).

**Evidence.** Wave 3 merge `40d3e694`, CI-W3 run 43412751883 (`SPRINT-148.md:1654`, CI-W3's facts at the `**CI-W3**` entry); wave 3b merge `2810ebeb`, CI-W3b run 43851473962 (`:1718`); QUAL-253's FIXED row; `RestPeriodRule.cs:266-300` (the not-checked rule); `git grep EvaluateWeeklyRest -- src` is empty; TASK-14809 merge `540ac9a9`. Plan O-9 `PLAN-s148.md:790`.

**old_str**
````text
One source of time of day per hour; manual hours retire (stage 0). |
````

**new_str**
````text
One source of time of day per hour; manual hours retire (stage 0). **S148, the rest arm built (O-9, 2026-11-25): wave 3, merged `40d3e694` (the TASK-14813 stack and TASK-14814; CI-verified at CI-W3, run 43412751883), corrected by wave 3b, merged `2810ebeb` (QUAL-253 FIXED; CI-W3b, run 43851473962).** The compliance check now reads Skema's clock intervals (`work_time_projection`) from two days before the month, and both rest checks build their work periods from them, a night shift joined at midnight; the Skema screen runs the same algorithm, held to it by one shared scenario table. Manual hours are gone (TASK-14809, merged `540ac9a9`). The T7 interim's two residuals are closed: a day counts as checked only when every project hour is on the clock (`RestPeriodRule.cs:266-300`), and `EvaluateWeeklyRest` is retired (no match in `src`). The narrowed "not checked" notice stays for days not fully on the clock (R-22, told to the owner). What the rest check does not model is QUAL-254 … 256; the first day of a month is QUAL-277. **Still open: the supplement arm** (payroll's `SupplementRule` still sees no clock time for Skema hours), built in stage 2. By this row's own reckoning that arm alone is H; re-rating the severity is the Orchestrator's. |
````

### A-12 · security-finding-register · SEC-061 (O-30 (3))

**Plain words.** SEC-061 (the local-profile save accepted a negative norm) is fixed and CI-verified, but its location cell still pointed at the code as it was before the fix. Fresh cites, verified at the merge and at HEAD. The status cell's validator cite is also off by one line.

**Evidence.** `ConfigEndpoints.cs:110-124` (header comment), `:125` (`MapPut`), `:527-528` (its end); `:782-807` (`ProfileSaveRequest`); `ProfileAlignmentValidator.cs:83` (`AddValueErrors`), `:85-87` (the three checks); `init.sql:914-917`; `ConfigResolutionService.cs:202` (`ApplyProfileOverlay`) to `:283`, the four columns at `:210-236`. `git diff 1bba25a0 HEAD` leaves `ConfigEndpoints.cs`, `ProfileAlignmentValidator.cs` and `ConfigResolutionService.cs` unchanged, and `init.sql`'s lines 914–917 are the same at both. Plan O-30 `PLAN-s148.md:812`.

**old_str (A-12a, the location cell)**
````text
| `ConfigEndpoints.cs:119-430`, `:627-634`; `init.sql:913-916`; `ConfigResolutionService.cs:193-195` |
````

**new_str (A-12a)**
````text
| `src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs:125-528` (the PUT; its header comment `:110-124`), `:782-807` (`ProfileSaveRequest`); `src/Backend/StatsTid.Backend.Api/Validators/ProfileAlignmentValidator.cs:83-87` (the value check; these three at `:85-87`); `docker/postgres/init.sql:914-917` (the four legacy columns); `src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs:202-283` (the overlay; the four columns at `:210-236`) (re-cited 2026-11-25, O-30 (3): the same lines at the merge `1bba25a0` and at `6ab643a7`). As found (2026-11-16): `ConfigEndpoints.cs:119-430`, `:627-634`; `init.sql:913-916`; `ConfigResolutionService.cs:193-195` |
````

**old_str (A-12b, in the status cell)**
````text
(`ProfileAlignmentValidator.cs:84-86` for these three;
````

**new_str (A-12b)**
````text
(`ProfileAlignmentValidator.cs:85-87` for these three;
````

### A-13 · quality-finding-register · QUAL-123 (O-24)

**Plain words.** The plan asked for a new row if one grep confirmed that the "48-hour reference period" setting is read by nothing. The grep confirms it, but QUAL-123 already registers exactly that, so this is a dated note, not a second row, plus a refreshed location (the code moved).

**Evidence.** `git grep -n 'WeeklyMaxHoursReferencePeriod' -- src` at `6ab643a7`: 16 lines, listed in the note; `git grep -c … -- src/RuleEngine`: none. `RestPeriodRule.cs:33`, `:177-179`, `:623-657` (`> 48.0m` at `:641`, threshold `:649`). The parameter stopped being passed in TASK-14826 (`9f621abb`, the T14b stack). Routing: `ROADMAP.md:936`, `:980`. Plan O-24 `PLAN-s148.md:806`; the audit's § 2.

**old_str**
````text
| CONFIRMED(panel) | `RestPeriodRule.cs:345-375` | →adjudication; ⚖ semantics ruling before severity |
````

**new_str**
````text
| CONFIRMED(panel). **S148 note (O-24, 2026-11-25): confirmed by one grep, so no new row.** `git grep -n WeeklyMaxHoursReferencePeriod -- src` at `6ab643a7` gives 16 lines and none is a reader: declarations with the default 17 (`AgreementRuleConfig.cs:72`, `AgreementConfigEntity.cs:62`, the request `AgreementConfigEndpoints.cs:1204`, the responses `AgreementConfigResponses.cs:81`, `:154`), copies and mappings (`AgreementConfigEntity.cs:138`; `AgreementConfigEndpoints.cs:246`, `:839`, `:911`, `:1048`; `ConfigResolutionService.cs:276`; `PositionOverrideConfigs.cs:111`), the database write and read (`AgreementConfigRepository.cs:838`, `:895`) and the audit JSON (`AgreementConfigEndpoints.cs:1073`, `:1089`); nothing under `src/RuleEngine`. Since the T14b stack (TASK-14826) the rule says so itself: the 48-hour check is handed no configuration (`RestPeriodRule.cs:177-179`), and the class doc reads "The 48h/week ceiling reads no configured value" (`:33`). Still routed to the domain-semantics track (ROADMAP), severity held | `RestPeriodRule.cs:623-657` (`CheckWeeklyMaxHours`; the fixed `48.0m` at `:641`, `:649`) (at `6ab643a7`); as found `:345-375` | →adjudication; ⚖ semantics ruling before severity |
````

### A-14 · performance-finding-register · F10 (CRLF file)

**Plain words.** F10 was "every weekday looks up its agreement configuration again". It said it closes when TASK-14828 restored reuse and the figures fell. Both happened: reads fell from about 90 to 14 for a Skema month, and the year overview went from 728 ms to 386 ms on CI. Drafted as DONE, the register's own word for a finished F-row.

**Evidence.** `DailyNormCalculator.cs:43-60` (the stretch doc), `:137` (`RequestStretches()`); `git grep "shortcut(F10)"` over `src tests frontend tools docker` is empty. Merge `b5f167aa` (`SPRINT-148.md:2641-2660`: the reads before and after); CI-W5a run 44728481380 (728 ms, `:2558`); CI-W5 run 45280970629 (`:3558`, `:3577`: 958 ms, 386 ms, 113 ms, 47 ms). Both edits are single-line, so CRLF is untouched.

**old_str (A-14a, the table row's status cell)**
````text
| **OPEN — registered, revisit** (wave-5 batch-A/B brief adjudication B6; TASK-14825 Step 5a cycle-1 ruling R4). See below |
````

**new_str (A-14a)**
````text
| **DONE** S148 (TASK-14828, the T14b stack, merged `b5f167aa`, 2026-11-23) — reuse restored per stretch of unchanged days; CI figures: year overview median 728 ms (CI-W5a) → 386 ms (CI-W5); at CI-W5 a Skema month read 113 ms, a save 47 ms. See below |
````

**old_str (A-14b, the section's last line)**
````text
that lands and the figures fall.
````

**new_str (A-14b)**
````text
that lands and the figures fall. **Disposition (2026-11-25): DONE (plan O-16; the T14b stack, ruling R4).** TASK-14828 (`f68ed12a`, merged in the T14b stack `b5f167aa`, 2026-11-23) makes `DailyNormCalculator` resolve a configuration once per stretch of days whose key and every layer's applies-from date are unchanged, shared by every caller in one HTTP request (`DailyNormCalculator.cs:43-60`, `:137`), and removed the `shortcut(F10)` comment (no match at `6ab643a7`). A stretch never serves a day outside it, so the correctness that removed the old cache (ruling B6) holds. **Reads** (TASK-14828's local measurement, not CI): a Skema month read 90 (135 with a position) → 14, with a mid-month version change; a year overview about 526 → 30 (the "about 780" above was traced, not measured). **Time** (CI, GitHub's runner): the year overview's median 728 ms at CI-W5a (run 44728481380, before) → 386 ms at CI-W5 (run 45280970629, after); at CI-W5 a Skema month read took 113 ms and a Skema save 47 ms (no CI "before" figure exists for those two). Teamoversigt's 300-person March median moved 1,024 → 958 ms, which refuted N-3 (no new F-row).
````

### A-15 · quality-finding-register · QUAL-182 (not in the brief; owed by the plan's Records owed)

**Plain words.** QUAL-182 said three "a new version starts mid-month" signals could never reach payroll's month planner, because the configuration tables had no dates. S148 dated agreement configurations and position overrides, and the planner now uses both dates; the row said it "closes when it does". The third signal (an EU working-time ruleset version) still has no table, so it stays latent, as the row describes.

**Evidence.** `PeriodCalculationService.cs:2387-2389`; `AgreementConfigRepository.cs:77`; `PositionOverrideRepository.cs:109`; `DatedConfigBoundaryTests.cs:99`, `:153`. Wiring commit `d2f08749` (TASK-14818, merged `f56dc64e`, whose subject says "the planner's dated boundaries, dormant until TASK-14825"); TASK-14825 merged `7af9176b` (QUAL-184's FIXED row: "switches on payroll's month split at a published applies-from date"). CI-W4a run 43946317628 (`SPRINT-148.md:1798-1801`, `DatedConfigBoundaryTests` 2); M-39 red at CI-M4a run 44481246221, M-40 at CI-M4b run 44481256823 (`:2150-2157`). Owed: `PLAN-s148.md:840` ("QUAL-182 … closed").

**old_str**
````text
**REGISTERED (S144 refinement, 2026-09-29)** |
````

**new_str**
````text
**CLOSED (S148, 2026-11-25; TASK-14818 `d2f08749`, merged `f56dc64e`, live in payroll from TASK-14825, merged `7af9176b`; `DatedConfigBoundaryTests` CI-verified at CI-W4a, run 43946317628, with its mutations M-39 and M-40 red at CI-M4a and CI-M4b, runs 44481246221 and 44481256823).** Agreement configs and position overrides gained an applies-from date (ADR-043 D12), and payroll's plan builder now hands their version dates to the planner as boundary sources (`PeriodCalculationService.cs:2387-2388`, read by `AgreementConfigRepository.cs:77` and `PositionOverrideRepository.cs:109`), so a version starting inside a month splits it; the two boundary facts carry this row's no-write assertions (wave-4 brief adjudication B14). **Residual:** `EuWtdRulesetTransitions` is still handed empty (`:2389`) because no EU-WTD ruleset table exists, so that source stays latent by data model, as this row describes (at `6ab643a7`). *Was:* **REGISTERED (S144 refinement, 2026-09-29)** |
````

### A-16 · quality-finding-register · QUAL-016 (dedupe: the 14802-T atomicity test gap)

**Plain words.** The brief's "14802-T atomicity test-gap" is not a new problem: QUAL-016 already registers that the "an outbox failure rolls everything back" tests re-implement the endpoint instead of calling it, and S133 deferred exactly the Skema cluster. Retiring the old route deleted the one test that went through a real endpoint for a time registration, so the note records that the Skema save now has only the re-implemented kind.

**Evidence.** `S148-TASK-14802-T-report.md` § 8 ("Test-gap row (census row 4)"); `SkemaProjectionAtomicTests.cs:16-19` ("Direct-orchestration shape (no WebApplicationFactory)"), `:69`; QUAL-016's S133 deferral (the register's S133 block, "DEFERRED: QUAL-016 bespoke cluster (~16 tests: Skema/WorkTime/…)"). Log `SPRINT-148.md:398`.

**old_str**
````text
| `ForcedRollbackHarness.cs:49-58` + member list | →adjudication §QUAL-016 |
````

**new_str**
````text
| `ForcedRollbackHarness.cs:49-58` + member list. **S148 note (2026-11-25; TASK-14802-T report § 8, census row 4):** retiring `POST /api/time-entries` deleted the wire-level fact `RegisterTimeEntry_RealEndpoint_OutboxThrows_RollsBackWholeWrite`, the only proof through a real endpoint that an outbox fault rolls back a time registration. The Skema save, now the only registration writer, is proved only in the re-implemented shape this row describes (`tests/StatsTid.Tests.Regression/Outbox/SkemaProjectionAtomicTests.cs:69`, `SaveSkema_OutboxFails_RollsBack`, whose doc states its "direct-orchestration shape", `:16-19`, at `6ab643a7`), part of the bespoke cluster deferred in S133. Recorded here rather than as a new row (S148 C-1 dedupe) | →adjudication §QUAL-016 |
````

### A-17 · quality-finding-register · QUAL-195 (dedupe: the drift guard ignoring `work_time_projection`)

**Plain words.** The "schema-copy drift guard does not compare `work_time_projection`" candidate is inside QUAL-195, which already says the guard covers only some tables. The note records that S148 changed that table twice, that today the one test copy still matches by reading, and that the existing remedy covers it.

**Evidence.** `HarnessDdlMatchesInitSqlTests.cs:37-41` (scope stated), `:92` (`HoursTables`), `:113` and `:228` (the `ProjectionSchemaTestFixture` row); `ProjectionSchemaTestFixture.cs:110-125` (its copy says "NOT covered by the drift guard" at `:111-112`); `init.sql:2196-2207`. Log `SPRINT-148.md:800`.

**old_str**
````text
TASK-14609 adds the harness `outbox_events` copy. |
````

**new_str**
````text
TASK-14609 adds the harness `outbox_events` copy. **S148 note (2026-11-25; TASK-14809 and -T, found not fixed):** S148 changed `work_time_projection` twice (TASK-14809 dropped `manual_hours`; TASK-14840 added `cleared_by_outbox_id`), and the guard still does not compare that table (`HarnessDdlMatchesInitSqlTests.cs:37-41`). The one harness copy (`ProjectionSchemaTestFixture.cs:114-125`) matches `init.sql:2196-2207` by reading at `6ab643a7`, so nothing is wrong today, but a later change could drift unseen. The remedy above covers it: add `work_time_projection` to the `ProjectionSchemaTestFixture` row's tables. |
````

### A-18 · quality-finding-register · QUAL-118 (dedupe: the two Regression classes on `localhost:5432`)

**Plain words.** "Two test classes hard-code the developer's own database" is QUAL-118, already Medium and open. S148 adds a concrete incident: an agent's local run pointed them at the owner's own PostgreSQL. The note records it and the standing workaround.

**Evidence.** `ManagerVikarEngineTests.cs:49-50`, `:100`, `:149-186`; `ReportingLineRepositoryTests.cs:21-22`; `Tests.Smoke/PayrollCalcAuditSmokeTests.cs:53`; `.github/workflows/ci.yml` step "Apply init.sql to the services-postgres (schema + seeds for the :5432-coupled classes)". Log `SPRINT-148.md:4233-4237`, `:4357`.

**old_str**
````text
| `ManagerVikarEngineTests.cs:28-31,368` +1 | →adjudication |
````

**new_str**
````text
| `ManagerVikarEngineTests.cs:28-31,368` +1. **S148 note (2026-11-25; TASK-14836-T's incident, carried to the close):** the two suites are `tests/StatsTid.Tests.Regression/ReportingLine/ManagerVikarEngineTests.cs:49-50` and `tests/StatsTid.Tests.Regression/ReportingLine/ReportingLineRepositoryTests.cs:21-22`, both hard-coding `Host=localhost;Port=5432;Database=statstid` (at `6ab643a7`). CI gives them the services Postgres it seeds for them (`.github/workflows/ci.yml`, the step "Apply init.sql to the services-postgres (schema + seeds for the :5432-coupled classes)"); on a developer machine they reach whatever database listens on 5432, and they insert and delete rows there (`ManagerVikarEngineTests.cs:100`, `:149-186`). In S148 an agent's local Docker run included both and pointed them at the owner's own PostgreSQL service (the run was killed; whether it connected is unknown; the owner was given a read-only check). Every dispatch that allows a throwaway cluster now greps the tests for `5432` and excludes these two and `Tests.Smoke/PayrollCalcAuditSmokeTests.cs:53`. Remedy unchanged: move both onto the shared per-class harness | →adjudication |
````

### A-19 · performance-finding-register · F9 (found while drafting; CRLF file, single-line edit)

**Plain words.** F9's text says TASK-14831 would correct a misleading code comment ("Blocks until granted" — in fact it gives up after 30 seconds). TASK-14831 did not; the C-1 comment sweep does (its block S-42). A register that says a fix happened when it did not misleads, so the note corrects it and adds the CI lock figure.

**Evidence.** `PayInputsLock.cs:38` still reads "Blocks until granted"; `.claude/plans/s148/C1-comment-sweep-spec.md` S-42 (`:915-924`); carried to TASK-14831 by `S148-TASK-14823-5a-rulings-c1.md:60`; CI-W5 lock figure (1) at `SPRINT-148.md` (the `**CI-W5 judged**` entry, "The four lock figures").

**old_str**
````text
`PayInputsLock.cs:38-46` says "Blocks until granted"; TASK-14831 corrects that remark (carried).
````

**new_str**
````text
`PayInputsLock.cs:38-46` says "Blocks until granted"; TASK-14831 corrects that remark (carried). *2026-11-25:* TASK-14831 did not correct it: `PayInputsLock.cs:38` still says "Blocks until granted" (at `6ab643a7`); the S148 C-1 comment sweep corrects it to state the timeout (`.claude/plans/s148/C1-comment-sweep-spec.md`, block S-42). CI-W5 (run 45280970629) measured a 1,200-row publish answering 2,046 ms after the release, with five waiting exports answering at 2,137–2,157 ms, far from the 30-second cliff (lock figure (1); ADR-034's D4 note of 2026-11-23). Disposition unchanged.
````

### A-20 · hr-follow-up-process-register · HRP-024 (optional; found while drafting)

**Plain words.** HRP-024's resolution cell repeats a sentence that is only half true: a "previous month not approved" refusal at export time comes after the month's calculation proof was written. The sentence's code twin is recorded below the floor (item 1 of that list). Optional: the row retires in stage 1 anyway.

**Evidence.** `Payroll/Program.cs:560-569` (the export-time catch: "The calculation's own manifest … stays"); `PriorMonthNotApprovedException.cs:35-39` (the body), `:17-20` (the class doc's "Either way nothing has been written"). Log `SPRINT-148.md:419`.

**old_str**
````text
The refused request calculates, writes and exports nothing (`PriorMonthNotApprovedException.cs:34-38`).
````

**new_str**
````text
The refused request calculates, writes and exports nothing (`PriorMonthNotApprovedException.cs:34-38`). *Corrected 2026-11-25:* that holds for the admission refusal only; when the export transaction's re-check refuses, the month's calculation manifest is already written and stays (`Payroll/Program.cs:560-569`), while the 409 body still says "Nothing was calculated, written or exported" (`PriorMonthNotApprovedException.cs:35-39`; below the QUAL floor, S148 C-1).
````

### A-21 · audit-projection-catalog · `OvertimeCompensationApplied`'s label (F-3, the catalog half)

**Plain words.** The catalog lists `OvertimeCompensationApplied` among "rule-engine internals", but the Backend's compensate route emits it. Only the label is wrong; the event is deliberately kept out of the audit projection.

**Evidence.** `OvertimeEndpoints.cs:713` (the event built), `:704-705` ("ADR-018 D3, Pattern C — no audit row"); `audit-projection-catalog.md:178`; `S148-TASK-14805-report.md:50`.

**old_str**
````text
`PeriodCalculationCompleted`, `OvertimeCompensationApplied` (rule-engine internals)
````

**new_str**
````text
`PeriodCalculationCompleted` (rule-engine internals); `OvertimeCompensationApplied` (emitted by the Backend's compensate route, `OvertimeEndpoints.cs:713`, with no audit row by design, ADR-018 D3 Pattern C, `:704-705`; the "rule-engine internals" label was wrong, corrected 2026-11-25 from the TASK-14805 report's F-3)
````

### A-22 · audit-projection-catalog · the two employee-profile rows (optional; the builder's F5)

**Plain words.** The catalog says the profile audit rows carry `weeklyNormHours`, a field removed in S53; the mappers do not write it.

**Evidence.** `EmployeeProfileCreatedAuditMapper.cs:17-24`; `EmployeeProfileUpdatedAuditMapper.cs:17-25`; `audit-projection-catalog.md:123-124`; `S148-TASK-14851-report.md:251`. Whether the rows should also carry `employmentCategory` is a separate question (below the floor, item 25).

**old_str (A-22a)**
````text
`{profileId, employeeId, weeklyNormHours, partTimeFraction, position, effectiveFrom}`
````

**new_str (A-22a)**
````text
`{profileId, employeeId, partTimeFraction, position, effectiveFrom}`
````

**old_str (A-22b)**
````text
`{profileId, employeeId, weeklyNormHours, partTimeFraction, position, versionBefore, versionAfter}`
````

**new_str (A-22b)**
````text
`{profileId, employeeId, partTimeFraction, position, versionBefore, versionAfter}`
````

---

## Part B — new rows

Quality rows are appended after QUAL-270 (the file's last line, `:526`), in id order. Column order: id · description · short title · dimension · severity · status · location · next.

### B-1 · quality-finding-register · QUAL-271 · M

**Plain words.** When an employee saves "payout or time off" for their overtime, the save rewrites the whole balance row from a copy it read a moment earlier. A compensation recorded in between would be wiped from the balance while its event stays: the record and the balance disagree. Nothing feeds the balance today, so it cannot happen yet; it retires with the route in stage 2.

**Verified at `6ab643a7`:** the read `OvertimeEndpoints.cs:925`, the write-back `:926-958` (`UpsertAsync` at `:941`, `:957`), no transaction; `OvertimeBalanceRepository.cs:29-48` (the conflict branch sets all three figures, `:36-37` in the SQL); `AdjustAccumulatedAsync` (`:50`) has no caller under `src`. Still true. Not a duplicate: QUAL-239 is the compensate route's own defect; QUAL-221 is who chooses. Sources: `S148-TASK-14805-report.md:47`; Fable WARNING (`S148-TASK-14805-5a-reviewer.md:17-23`); Codex WARNING (`S148-TASK-14805-5a-codex.md`); log `SPRINT-148.md:491`, `:516-518` ("registered at Medium at close").

```text
| QUAL-271 | **Saving the employee's overtime compensation choice can undo a compensation committed a moment earlier.** `PUT /api/overtime/{employeeId}/compensation-choice` reads the year's balance row (`OvertimeEndpoints.cs:925`) and writes it back whole through `UpsertAsync` (`:926-958`), whose conflict branch sets `accumulated`, `paid_out` and `afspadsering_used` from that earlier read (`OvertimeBalanceRepository.cs:29-48`), with no transaction or lock. A compensation (`POST …/compensate`) that commits between the read and the write is overwritten in the balance while its `OvertimeCompensationApplied` event stays committed: the event store and the balance disagree, QUAL-239's mismatch by another path. Unreachable today: nothing in production adds hours to the balance (`AdjustAccumulatedAsync`, `OvertimeBalanceRepository.cs:50`, has no caller under `src`), so there is nothing to compensate. | Compensation-choice save can overwrite a committed compensation | D-audit | M | **OPEN (registered S148, 2026-11-25; TASK-14805 report F-1; Step 5a: Fable WARNING and Codex WARNING, both pre-existing and outside the task).** Retires with the choice route in stage 2 (ADR-043 D8, the overtime ledger) | `src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs:861-966` (the PUT; the read `:925`, the write-back `:926-958`); `src/Infrastructure/StatsTid.Infrastructure/OvertimeBalanceRepository.cs:29-48` (`UpsertAsync`), `:50` (no production caller) (at `6ab643a7`); `.claude/reviews/S148-TASK-14805-report.md` F-1; `.claude/reviews/S148-TASK-14805-5a-reviewer.md` WARNING | → If the route survives into a stage where hours accrue: let the conflict branch set only `compensation_model` and `updated_at` (the report's fix shape) and pin a race. Otherwise retire it with the route in stage 2. Re-read when anything first calls `AdjustAccumulatedAsync`. |
```

### B-2 · quality-finding-register · QUAL-272 · M

**Plain words.** The same save records no event and no audit row, although the choice matters for pay. So who chose payout or time off, and when, cannot be reconstructed. This one is reachable today: HK and PROSA employees may make the choice.

**Verified at `6ab643a7`:** the comment "matters for pay" `OvertimeEndpoints.cs:892`; the writes `:941`, `:957` through the connection-owning `UpsertAsync`; no outbox, event or audit call in `:861-966`. HK and PROSA allow the choice: `CentralAgreementConfigs.cs:68`, `:98`, `:150`, `:180` (`EmployeeCompensationChoice = true`), and the seeded `agreement_configs` rows `init.sql:1640`, `:1646`, `:1652`, `:1658` (`employee_compensation_choice` TRUE; AC is FALSE). Still true. Related, not duplicates: QUAL-221 (who chooses), QUAL-159 and QUAL-160 (other pay-relevant writes with no event). Sources: `S148-TASK-14805-report.md:48`; Codex WARNING (`S148-TASK-14805-5a-codex.md`); Fable (`S148-TASK-14805-5a-reviewer.md:32-34`, "register it alongside F-1, or fold it into QUAL-221"); log `SPRINT-148.md:492`, `:518`.

```text
| QUAL-272 | **The employee's overtime compensation choice is saved with no event, no audit row and outside the outbox, although the choice matters for pay.** The PUT's own comment says the model on `overtime_balances` "matters for pay" (`OvertimeEndpoints.cs:892`), yet it writes through the connection-owning `UpsertAsync` (`:941`, `:957`; `OvertimeBalanceRepository.cs:29-48`) and enqueues nothing: no event, no ADR-026 audit row, no transaction. So who chose payout or time off, and when, cannot be reconstructed afterwards. Reachable today: HK and PROSA allow the employee's choice (`CentralAgreementConfigs.cs:68`, `:98`, `:150`, `:180`; the seeded `agreement_configs` rows `init.sql:1640`, `:1646`, `:1652`, `:1658`). QUAL-221 holds the domain question (who chooses; for HK the employer's choice, PAV 19.2.7.1); this row is the mechanism. | Compensation-choice save has no event or audit row | D-audit | M | **OPEN (registered S148, 2026-11-25; TASK-14805 report F-2; Step 5a: Codex WARNING, Fable WARNING).** The family of QUAL-159 and QUAL-160 (pay-relevant writes with no event). Retires with the choice route in stage 2 (ADR-043 D8) | `src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs:861-966` (`:892` the "matters for pay" comment; `:941`, `:957` the writes); `src/Infrastructure/StatsTid.Infrastructure/OvertimeBalanceRepository.cs:29-48`; `src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs:68`, `:98`, `:150`, `:180` (at `6ab643a7`); QUAL-221 | → If the route survives stage 2: write the choice in one transaction with an event through the outbox and an audit mapper (ADR-018 D3, ADR-026). Otherwise retire it with the route; settle QUAL-221's "who chooses" first. |
```

### B-3 · quality-finding-register · QUAL-273 · M

**Plain words.** Payroll checks for typed hours (on-call and the like) once, just before calculating. A rebuild of the read tables running in that moment could put back an old typed row, which the calculation would then pay as ordinary hours. It cannot happen today, because the owner's "reseed, don't migrate" rule means no such old row exists anywhere. A complete fix would need the check inside the calculation's own snapshot, which S148 chose not to build.

**Verified at `6ab643a7`:** the check reads separately (`Payroll/Program.cs:1009-1054`, its own `ReadCurrentAsync` at `:1035-1036`), called before the calculation at `:354`, `:713`; the rebuild replays every `TimeEntryRegistered`, `ActivityType` included (`ProjectionBackfillService.cs:175-188`), at Backend start-up (`Backend.Api/Program.cs:602`) and from `tools/ProjectionBackfill`. Still true and unreachable. Not a duplicate: QUAL-BF-003 is "no remedy for a stored typed row", a different limit. Sources: Codex W (`S148-TASK-14802-5a-codex.md`); the Orchestrator's disposition, log `SPRINT-148.md:634-636` ("This becomes a QUAL row at close and a line in the legacy-database upgrade runbook").

```text
| QUAL-273 | **A projection rebuild can put back a historical typed hours row after payroll's typed-entry check has passed, and the month would then be paid on it.** Payroll refuses a month holding an entry typed other than ordinary work (QUAL-232's interim) by reading the month's current rows once, before the calculation and outside its snapshot (`Payroll/Program.cs:1009-1054`, called at `:354` and `:713`). The projection rebuild replays every stored `TimeEntryRegistered`, `ActivityType` included, into `time_entries_projection` (`ProjectionBackfillService.cs:175-188`; run at Backend start-up, `Backend.Api/Program.cs:602`, and by `tools/ProjectionBackfill`). If a database held a typed `TimeEntryRegistered` from before S148 whose projection row was missing, a rebuild between the check and the calculation would restore it, and the calculation would pay it as ordinary hours, the defect QUAL-232 describes. Unreachable today: the registering route is retired, the Skema save writes only `NORMAL`, and under the owner's "reseed, don't migrate" no historical typed event exists. | Rebuild can restore a typed row past payroll's check | D-domain | M | **OPEN, accepted (registered S148, 2026-11-25; TASK-14802 Step 5a, Codex WARNING; the Orchestrator's disposition "recorded, not fixed").** A complete fix needs the check inside the calculation's own snapshot, which wave-1 ruling A5 declined (option c). Paired with a legacy-database runbook line owed at close: carry no historical typed `TimeEntryRegistered` events into a live payroll; reseed instead | `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:1009-1054`; `src/Infrastructure/StatsTid.Infrastructure/ProjectionBackfillService.cs:175-188`; `src/Backend/StatsTid.Backend.Api/Program.cs:602` (at `6ab643a7`); `.claude/reviews/S148-TASK-14802-5a-codex.md`; QUAL-BF-003 | → Revisit if a database holding typed rows is ever carried forward instead of reseeded (QUAL-BF-003's trigger): then run the typed check inside the calculation's snapshot, or refuse typed rows in the rebuild. Re-read with QUAL-232's stage-2 model. |
```

### B-4 · quality-finding-register · QUAL-274 · M (Addendum A1's O-31)

**Plain words.** Skema writes each registration to an "outbox" first; a background publisher copies it to the permanent event log later. The rebuild of the read tables reads only the permanent log. So if the read tables were ever emptied and rebuilt, anything saved but not yet copied would be missing until a later restart, or for good if its stream is stuck. Tables that are not empty are safe. The gap is older than S148; cell clearing (S148) adds three more event types to it.

**Verified at `6ab643a7`:** the rebuild's query reads `events` only (`ProjectionBackfillService.cs:175-188`, left-joined to `outbox_events` for the id); the save writes `outbox_events` (`PostgresEventStore.cs:280`); a quarantined head-of-stream row stalls its stream (`OutboxPublisher.cs:192-202`); populated tables: `ON CONFLICT (event_id) DO NOTHING` (`ProjectionBackfillService.cs:20`, `:137`). Still true. Not a duplicate: QUAL-197 is a value difference in rebuilt rows. Sources: `.claude/plans/s148/ADDENDUM-A1-cell-clearing.md:450` (O-31), `:40`, `:159`; `.claude/reviews/ADDENDUM-A1-adjudication-c1.md` item 1; log `SPRINT-148.md:667` and the owner told (O-28).

```text
| QUAL-274 | **Rebuilding the Skema read tables from empty loses every registration or removal not yet copied to the permanent event log.** A Skema save writes its event to the outbox (`outbox_events`, `PostgresEventStore.cs:280`) and its read-table row in one transaction; a separate publisher copies the event to `events` later. The rebuild reads only `events` (`ProjectionBackfillService.cs:175-188`). So from empty read tables, a committed fact of any replayed type (a registration, a removal, a balance revaluation) that is not yet published is missing until the next start after it is published, and indefinitely if its stream is quarantined (a quarantined head-of-stream row stalls the stream, `OutboxPublisher.cs:192-202`). Recovering needs a manual truncate and re-run. Populated tables are safe: the start-up rebuild only adds missing rows, and a clear marks a row instead of deleting it (addendum A1 D-4). The gap predates S148 for registrations; S148's cell clearing adds three replayed types. | From-empty rebuild misses unpublished facts | D-domain | M | **OPEN (registered S148, 2026-11-25; addendum A1 O-31; Step 0b cycle-1 adjudication item 1, "the general gap"; told to the owner 2026-11-11).** No fact pins the gap (adjudication § 3: a fact would pin a registered gap). Its below-floor neighbour: a work-time clear names its registration by its `outbox_events` row (this register's below-floor appendix, S148 TASK-14840 note (2)) | `src/Infrastructure/StatsTid.Infrastructure/ProjectionBackfillService.cs:175-188`; `src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs:280`; `src/Infrastructure/StatsTid.Infrastructure/Outbox/OutboxPublisher.cs:192-202` (at `6ab643a7`); `.claude/plans/s148/ADDENDUM-A1-cell-clearing.md` § 7 O-31; `.claude/reviews/ADDENDUM-A1-adjudication-c1.md` item 1 | → The adjudication's option (a): the rebuild also reads the Backend's own unpublished `outbox_events` rows of the replayed types, in outbox-id order. It changes the rebuild's source for existing event types, so it is its own task with review. Revisit, not shield; revisit before any retention job prunes `outbox_events`. |
```

### B-5 · quality-finding-register · QUAL-275 · M

**Plain words.** A field called `SourceStintId` was built so the rest check could join the two halves of a night shift. Since S148 the rest check works from the clock times instead, and nothing reads the field. But the shared code's comments still say the rest check depends on it, which would mislead the next person who changes it.

**Verified at `6ab643a7`:** writers `ComplianceEndpoints.cs:211`, `PeriodCalculationService.cs:3781`, the normalizer `:184`, `:227`; no match under `src/RuleEngine`; the untrue comments `TimeEntry.cs:17-37` (":33-34 only the rest-check stint reconstruction does"), `MidnightCrossingNormalizer.cs:26-28`, `:129-131`, `:141-144`, `PeriodCalculationService.cs:3760-3762`; the true one `ComplianceEndpoints.cs:207-209`. Still true; not in the C-1 comment sweep (ruled a QUAL row). Sources: `.claude/reviews/S148-wave3-briefs-adjudication.md` item 11 and § 3 (retiring it declined: SharedKernel and a payroll file); `PLAN-s148.md:845`; log `SPRINT-148.md:1421`.

```text
| QUAL-275 | **A "continuity link" that shared code says the rest check reads is read by nothing.** `TimeEntry.SourceStintId` is set from each row's event id at two read boundaries (`ComplianceEndpoints.cs:211`, `PeriodCalculationService.cs:3781`) and copied to both halves of a midnight-crossing shift by the normalizer (`MidnightCrossingNormalizer.cs:184`, `:227`). The comments still say it exists "so a rest check can see them as one stint", that "only the rest-check stint reconstruction" needs it, and that "the continuous-stint / rest-check consumer reconstructs the absolute interval … keyed by the shared SourceStintId" (`TimeEntry.cs:17-37`; `MidnightCrossingNormalizer.cs:26-28`, `:129-131`, `:141-144`; `PeriodCalculationService.cs:3760-3762`). Since S148 wave 3 the rest checks build their stints from Skema's clock intervals alone (ADR-043 D10), no code under `src/RuleEngine` mentions the field, and the compliance read's own comment says so (`ComplianceEndpoints.cs:207-209`). A reader trusting the SharedKernel comments would believe the rest check depends on a field that is dead. | Dead `SourceStintId` link, documented as read | D7 | M | **OPEN (registered S148, 2026-11-25; wave-3 brief adjudication item 11, 14813 BC-6: "found-not-fixed, with one QUAL row"; plan "final + 7" Records owed).** Not retired in wave 3: it reaches SharedKernel and a payroll file, which AC-S0-3e excluded (adjudication § 3) | `src/SharedKernel/StatsTid.SharedKernel/Models/TimeEntry.cs:17-37`; `src/SharedKernel/StatsTid.SharedKernel/Normalization/MidnightCrossingNormalizer.cs:26-28`, `:129-131`, `:141-144`, `:184`, `:227`; `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:3760-3762`, `:3781`; `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs:207-211` (at `6ab643a7`); `.claude/reviews/S148-wave3-briefs-adjudication.md` item 11, § 3 | → Retire the field and its two mappings, or make the comments say no rest check reads it (a SharedKernel and payroll change; payroll files are high-risk). Revisit when the normalizer, ADR-039 D4 or stage 2's interval read is next opened. |
```

### B-6 · quality-finding-register · QUAL-276 · M

**Plain words.** The small piece of code that turns "08:30" into seconds exists four times, privately, in four files. Three copies are identical; the fourth (in the approval code) differs: it rounds differently and would crash on a malformed time instead of skipping it. Any change to the rule has to be made four times, and they already disagree.

**Verified at `6ab643a7`:** `SkemaEndpoints.cs:3476-3495`, `BalanceEndpoints.cs:1482-1514`, `ComplianceEndpoints.cs:535-552` (its comment names the other two), all three byte-identical; `ApprovalEndpoints.cs:165-193` (`long.Parse`, no 0–23 or 0–59 check, unrounded), whose callers are Teamoversigt (`:1251`), the allocation breakdown (`:1653`) and the send command's allocation gate (`:2618`). `(unproven)`: whether a malformed interval can be stored at all, and whether the rounding difference changes any shown or compared figure. Sources: `.claude/plans/s148/TASK-14813.md:293`, `:358`; wave-3 brief adjudication § 4; `PLAN-s148.md:845`; log `SPRINT-148.md:1422`.

```text
| QUAL-276 | **The Skema clock-time parse exists in four private copies, and one of them behaves differently.** Turning an "HH:mm" interval into seconds is written out three times, identically and privately (`SkemaEndpoints.cs:3476-3495`, `BalanceEndpoints.cs:1482-1514`, `ComplianceEndpoints.cs:535-552`; the third added in S148 wave 3, its comment naming the other two). A fourth copy, `ApprovalEndpoints.cs:165-193` (`SumIntervalHours` with `ParseToSeconds`), differs: it uses `long.Parse` with no hour or minute range check, so a malformed stored value would throw rather than be skipped, and it returns unrounded hours where `BalanceEndpoints` rounds to two decimals. Its callers are Teamoversigt (`:1251`), the allocation breakdown (`:1653`) and the send command's allocation gate (`:2618`). A change to the parsing rule (a future 24:00, say) must be made in four places, and the copies already disagree on rounding and on malformed input. `(unproven)`: whether a malformed interval can be stored, and whether the rounding difference changes a shown or compared figure. | Four private copies of the clock-time parse | D-maintainability | M | **OPEN (registered S148, 2026-11-25; TASK-14813 contract item 4, "one private parse copy in the Backend"; wave-3 brief adjudication; plan "final + 7" Records owed).** The fourth, divergent copy was found while drafting the register row | `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:3476-3495`; `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs:1482-1514`; `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs:535-552`; `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs:165-193` (at `6ab643a7`); `.claude/plans/s148/TASK-14813.md` contract item 4 | → One shared parse-and-sum helper (the Skema rule: hours 0–23, minutes and seconds 0–59, positive duration only, hours rounded to two decimals), used by all four; pin that the send gate and the screens agree. Revisit when any copy is next touched. |
```

### B-7 · quality-finding-register · QUAL-277 · M

**Plain words.** The rest check looks back 24 hours from the end of each piece of work. For work ending on the 1st, that window reaches into the previous month's last day. If that day had hours not on the clock, the check counts them as rest, so the new month can look compliant, and its "not checked" note does not mention the earlier day (the previous month's note does). One boundary day per month; the information exists on the previous month's screen.

**Verified at `6ab643a7`:** the read starts `monthStart − 2` (`ComplianceEndpoints.cs:188`); the not-checked list covers the period's own days only, said in the rule's own comment (`RestPeriodRule.cs:266-283`, `FindRestNotCheckedDays` `:284-300`). Still true. The sibling "truncated chain" limit of the same ruling is retired (wave-3b adjudication item 16), so it is not registered. Sources: `.claude/reviews/S148-wave3-briefs-adjudication.md` item 2; `.claude/reviews/S148-wave3b-briefs-adjudication.md` item 16 (`:120-123`); `PLAN-s148.md:845` and the "final + 10" bullet; log `SPRINT-148.md:1424`.

```text
| QUAL-277 | **A rest finding, or its absence, on the 1st of a month can depend on the last day of the previous month, but the month's "not checked" list does not say whether that day was fully on the clock.** Since S148 wave 3b every work end is judged on the 24 hours before it, so a work end on the 1st looks back into the previous month's last day (the read starts at `monthStart − 2`, `ComplianceEndpoints.cs:188`). The list of days the rest check could not fully measure covers the period's own days only (`RestPeriodRule.cs:266-300`, which says so). If 31 March holds project hours that are not on the clock, those hours count as rest when 1 April's work end is judged, so April can show no finding and no "not checked" note for that window; March's list names 31 March. It is the fail-open class owner ruling R-22 keeps the list against (QUAL-234), at one boundary day per month, with the information on the previous month's view. | First-of-month rest relies on an unlisted boundary day | D-domain | M | **OPEN, accepted (registered S148, 2026-11-25; wave-3 brief adjudication item 2, "recorded rather than built"; re-read after wave 3b, plan "final + 10": the same item's truncated-chain limit is retired, this one stands).** Documented in the rule's own comment | `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RestPeriodRule.cs:266-300`; `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs:188` (at `6ab643a7`); `.claude/reviews/S148-wave3-briefs-adjudication.md` item 2; `.claude/reviews/S148-wave3b-briefs-adjudication.md` item 16 | → List a previous-month day when a judged work end's 24 hours reach it and it is not fully on the clock (dated the 1st, as item 16 words it), within SEC-059's manager rule (a leader may not see an unsent previous month). Revisit with ADR-044 or when the not-checked list next changes. |
```

### B-8 · quality-finding-register · QUAL-278 · M (owed by the plan, "final + 9"; the audit missed it)

**Plain words.** When the compliance check fails (a network or server error), the employee's Skema screen shows no warnings and no message, so the month looks clean although it was never checked. The manager's Teamoversigt does say "Advarsler kunne ikke hentes". Saying "could not be checked" on Skema needs ruled Danish copy, which is why it was registered rather than built.

**Verified at `6ab643a7`:** `SkemaPage.tsx:332` takes only `result` and `loading` from `useCompliance` (which returns `error`, `useCompliance.ts:73`, `:108`); `:1540` passes the two to the warnings panel; Teamoversigt renders the error (`TeamOversigt.tsx:183-184`, `:320-323`). Still true for Skema; **the plan's draft text ("neither screen renders it") is wrong for Teamoversigt**, so the row is narrowed. Not registered before (no register match for `useCompliance` or "could not be checked"). Sources: `.claude/reviews/S148-TASK-14814-5a-adjudication-c1.md:77-80`, `:160-161`; `PLAN-s148.md:846` ("final + 9", "Medium"); log `SPRINT-148.md:1568-1570`.

```text
| QUAL-278 | **When the compliance check fails, Skema shows no rest or hours warnings and no message, so the month can look clean although it was never checked.** `SkemaPage.tsx:332` takes `result` and `loading` from `useCompliance` but not its `error` (the hook returns one, `useCompliance.ts:73`, `:108`), and the warnings panel is given only those two (`:1540`). Since S148 wave 3 a failed call clears the previous month's warnings instead of showing them under the new month (TASK-14814 fix-up), so on failure the panel is empty. Teamoversigt does say "Advarsler kunne ikke hentes" (`TeamOversigt.tsx:320-323`), so only the employee's own screen is silent. Saying "could not be checked" needs ruled Danish copy, which is why it was registered rather than built. | Skema hides a failed compliance check | D-usability | M | **OPEN (registered S148, 2026-11-25; TASK-14814 Step 5a, Fable NOTE; its adjudication's "given up" item; plan "final + 9" Records owed).** The plan's draft said neither screen renders the error; Teamoversigt has since S88, so the row is narrowed to Skema | `frontend/src/pages/SkemaPage.tsx:332`, `:1540`; `frontend/src/hooks/useCompliance.ts:73`, `:108`; `frontend/src/pages/approval/TeamOversigt.tsx:320-323` (the screen that does render it) (at `6ab643a7`); `.claude/reviews/S148-TASK-14814-5a-adjudication-c1.md` | → An informational "could not be checked" line in the warnings panel's live region, in ruled Danish copy (Teamoversigt's sentence is a candidate), with a vitest that a failed call renders it. |
```

### B-9 · quality-finding-register · QUAL-279 · M

**Plain words.** On the position-override page, two administrators can edit the same row without the second one being warned that the first changed it. The page keeps the row's "version stamp" (ETag) fresh while an edit is open, so a save always looks current to the server, and the other administrator's change is silently overwritten. The project's concurrency rule (ADR-019) exists to prevent exactly this.

**Verified at `6ab643a7`:** `startEdit` captures values, not the ETag (`PositionOverrideManagement.tsx:262-273`); `saveEdit` sends `item.etag` from the current list (`:294-317`, `:301`), `confirmSwitch` likewise (`:319-334`, `:326`); the list is re-read quietly after writes (`usePositionOverrides.ts:80-95`); the 412 banner `:361`. Still true. Not a duplicate: QUAL-028 is out-of-order re-reads. Sources: Fable c5 NOTE 5 (`S148-TASK-14838-5a-reviewer-c5.md:115-128`); log `SPRINT-148.md:4158`, `:4197-4201` ("Medium in engineering terms").

```text
| QUAL-279 | **On the position-override page, a row edit can overwrite another administrator's change without the conflict warning.** The edit form captures the row's values when it opens (`PositionOverrideManagement.tsx:262-273`) but not its version stamp (ETag); the save sends the ETag of the row as the list holds it at the moment of saving (`saveEdit`, `:294-317`, `item.etag` at `:301`), and a switch on or off does the same (`:319-334`). The list is re-read after writes and quietly after a late publish (`usePositionOverrides.ts:80-95`). So if the list is re-read while an edit is open and another administrator has changed the row meanwhile, the save carries the new ETag, passes the server's version check (ADR-019's optimistic concurrency) and replaces the other change with the values the form opened on, with no 412 banner (`:361`). Pre-existing; found by the TASK-14838 review. | Override edit saves with a refreshed ETag (lost update) | D-correctness | M | **OPEN (registered S148, 2026-11-25; TASK-14838 Step 5a cycle 5, Fable NOTE 5, "a QUAL candidate, with ADR-019 at stake").** | `frontend/src/pages/admin/PositionOverrideManagement.tsx:262-273`, `:294-334`, `:361`; `frontend/src/hooks/usePositionOverrides.ts:80-95` (at `6ab643a7`); `.claude/reviews/S148-TASK-14838-5a-reviewer-c5.md` NOTE 5 | → Capture the ETag when the edit or switch step opens and send that one, so a change made meanwhile answers 412 and the banner shows (or close the edit when the row's version changes under it); pin it with a vitest that re-reads the list under an open edit. Re-read with QUAL-028. |
```

### B-10 · quality-finding-register · QUAL-BF-011 · L (below the floor, an accepted limit)

**Plain words.** In the month a "særlige feriedage" configuration ends (say on the 15th), the Skema grid offers no "Særlig feriedag" row at all, even for the 1st–14th. The save itself values each day correctly, so nothing is paid wrongly; the regular OK24→OK26 change falls on a month edge and is unaffected. The adjudicator called it a known limit, not a defect, and the owner was told on 2026-11-10 that it is "pinned and registered". A below-floor shortcut row is the register's place for such a limit (Part C has the Medium alternative).

**Verified at `6ab643a7`:** the catalogue anchor is month end (`SkemaEndpoints.cs:337-345`, `ComputeAbsenceCatalogAsync` `:3549`); the pin `SkemaSpecialHolidayRowTests.cs:240` with its "Known limit, registered" docstring (`:216-238`). Still true. **Not yet registered**, although the test docstring and the owner note say it is. No `shortcut(QUAL-BF-011)` comment exists; adding one is a code edit (Part C). Sources: wave-1 brief adjudication C3; log `SPRINT-148.md:389`, `:397`; merge `b42b3b23`; CI-W1.

Insert after `QUAL-BF-010`'s row (`quality-finding-register.md:417`):

```text
| QUAL-BF-011 | `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` | In the month a særlige-feriedage (`SPECIAL_HOLIDAY`) configuration ends, Skema offers no "Særlig feriedag" row, even on the days the configuration still covers: the month view decides the row once, at the month's last day (`ComputeAbsenceCatalogAsync`, anchor = month end). The save values each day independently, so nothing is paid wrongly; the regular OK-version change (31 March to 1 April) falls on a month edge and is unaffected (S148 TASK-14808, AC-5a, pinned by `SkemaSpecialHolidayRowTests.ConfigEndingMidMonth_RowNotServedInTheEndingMonth_ServedTheMonthBefore`; wave-1 brief adjudication C3; told to the owner 2026-11-10) | a configuration is ended mid-month in a real setting, or the catalogue is decided per day; the pin is then re-pinned deliberately | open (registered S148, 2026-11-25; no `shortcut(QUAL-BF-011)` comment yet) |
```

### B-11 · performance-finding-register · F11 (CRLF file)

**Plain words.** The Regression test step on CI went from about 75 minutes before S148 to about 2 hours 15 minutes. The tests themselves take minutes; nearly all the time is starting a database container for each new test class, and the suite runs its classes one at a time on purpose (an S64 decision to stop flaky failures). It is not a correctness problem; it costs feedback time, since every evidence run blocks a wave for over two hours.

**Verified:** `tests/StatsTid.Tests.Regression/xunit.runner.json` (`parallelizeTestCollections: false`, the S64 override); 416 Docker-gated files at `6ab643a7`, 319 at the S147 close `e553777e` (99 added in S148, by `git grep` of the trait); `.github/workflows/ci.yml` sets no `timeout-minutes` (GitHub's default job limit applies, 6 hours by GitHub's documentation, not verified here). Figures from the log: CI-W1 `SPRINT-148.md:746-749`; CI-W1b `:785`; CI-W4 `:1993`; CI-W5 (the `**CI-W5 judged**` entry); CI-W6 `:4582`. Not a duplicate (F1–F10 are product paths).

Table row, inserted directly after F10's row (`performance-finding-register.md:38`), CRLF:

```text
| F11 | The Regression suite's CI step grew from about 75 minutes to about 2 h 15 min during S148, almost all of it container start-up for new Docker test classes run one at a time (S148, CI-W1 to CI-W6) | **OPEN — registered, revisit** (CI-W1 and CI-W1b; carried to the close). See below |
```

Section, inserted directly before `## Standing convention` (`:374`), CRLF:

```text
## F11 — the Regression suite's CI time (S148, recorded 2026-11-25)

**What it is.** Each Docker-gated Regression class starts its own PostgreSQL container (Testcontainers), and many
boot a whole Backend host. The Regression assembly runs its test collections one at a time
(`tests/StatsTid.Tests.Regression/xunit.runner.json`, `parallelizeTestCollections: false`, the S64 override recorded in
`docs/operations/s64-regression-debt-census.md`), so every new class adds its start-up cost in series. S148 added 99
Docker-gated test files (319 at the S147 close `e553777e`, 416 at `6ab643a7`).

**Size (CI figures, GitHub's runner).** Before S148 the regression step took about 75 minutes. CI-W1 (run 43011040973):
117 minutes, of which the tests themselves summed to 554 s. CI-W1b (run 43027618243, with `--blame-hang-timeout 15m`):
about 120 minutes (13:18 → 15:18 UTC), no hang dump, so the time is steady start-up, not a hang. CI-W4 (run 44352425811):
started 16:53 UTC, Regression finished 19:01 UTC. CI-W5 (run 45280970629): build-and-test 2 h 16 min. CI-W6 (run
45582399602): build-and-test 2 h 17 min, Regression alone 2 h 15 min, 2,969 facts (CI-W0b: 2,444).

**Disposition: registered, revisit.** Not a correctness defect: the cost is feedback time (each evidence run blocks a
wave for over two hours) and runner minutes. `.github/workflows/ci.yml` sets no `timeout-minutes`. Known options: one
container per collection shared across classes (an xUnit collection fixture); resource-isolated collections that can
run in parallel (the S64 override's own revisit condition); or the Docker suite split across jobs. Revisit when the step
passes 3 hours, or at S149's refinement if stage 1 adds a comparable number of Docker classes.
```

### B-12 · security-finding-register · pre-production revisit ledger line (not a SEC row)

**Plain words.** The HR-facts security consult accepted one small integrity gap and asked that it be recorded under "revisit before production": the save checks the person's agreement before its transaction and not again under the lock, so a simultaneous agreement change could leave a stored overtime regime the new agreement does not allow. It cannot reach pay as an allowed value, because the reader treats a disallowed value as "not recorded". The sprint log records it as accepted; no register line exists yet.

**Verified at `6ab643a7`:** the read `EmployeeHrFactsEndpoints.cs:152`, the transaction `:165`; the resolver `EmployeeHrFactsResolver.cs:95-98`. Still true. No ledger or SEC match. Sources: `.claude/reviews/S148-TASK-14836-security-consult.md:72-80` (§ 3.2, Low); log `SPRINT-148.md` (the TASK-14836 consult entry, "3.2: … Accepted; revisit before production").

Inserted after the `SEC-031` ledger row (`security-finding-register.md:275`):

```text
| **HR-facts PUT agreement read** (S148 TASK-14836 security consult § 3.2, Low; accepted 2026-11-24) | The PUT reads the person's agreement for the chosen date before its transaction opens and does not re-read it under the users-row lock (`EmployeeHrFactsEndpoints.cs:152`; the transaction `:165`), so an agreement change committed in between can leave a stored overtime regime the new agreement does not allow. Accepted: the resolver reads a disallowed stored value as "not recorded" (`EmployeeHrFactsResolver.cs:95-98`), so it never reaches pay as allowed, and no authorisation depends on it (integrity, not access). | Re-read the agreement under the users-row lock if the first pay reader trusts the stored regime without resolving it; first check whether the agreement-code PUT takes the users-row lock (the consult did not read it). |
```

---

## Below the registration floor (recorded, not registered)

Worded for the sprint-log paragraph of that name. Each is Low (L) unless marked; sprint-log lines at `6ab643a7`.

1. **Payroll's "previous month not approved" refusal says "Nothing was calculated, written or exported" also when the export transaction's re-check refuses, after the month's calculation proof was written** (`PriorMonthNotApprovedException.cs:35-39`; the export path `Payroll/Program.cs:560-569`; TASK-14801, log :419). The body is behaviour (a fixed response string), so it is not a comment-sweep item; the class doc's "Either way nothing has been written" (`:17-20`) is, and is not yet in the C-1 sweep spec. Optional register correction: A-20.
2. **Teamoversigt gives each warning line its own `role="status"` live region** (`TeamOversigt.tsx:309`, `:315`, `:321`, `:326`, `:332`), against the house pattern of one always-mounted region (wave-3 adjudication item 8; log :1423). Accessibility; see Part C (the plan says "register row").
3. **The agreement editor's publish dialog has `role="dialog"` but no `aria-modal`, focus trap or Escape** (`AgreementConfigEditor.tsx:707-714`; the kit's `Drawer.tsx:4-8` has all three). TASK-14838 adjudication § 3 asked that it be weighed against the floor at close (log :4001): judged Low (one GlobalAdmin dialog).
4. **The customary-days lock-time 403 writes no log line**, while the scope validator's identical refusal does (`CustomaryDayEndpoints.cs:490-494`; house logging, not the audit trail; log :4111).
5. **A late success after the date was retyped is not announced** on the customary-days editor (`CustomaryDaysEditor.tsx:395-401`; accepted as C2-R1(b) is written; log :4065).
6. **`confirm()`'s re-check on the customary-days editor is defence in depth and unpinned** (`CustomaryDaysEditor.tsx:379-381`; log :4076). Test gap.
7. **On the position-override page, after a failed list re-read "Opret" stays disabled with no retry control, and the error banner has no `role="alert"`** (`PositionOverrideManagement.tsx:127`, `:372`; pre-existing; log :4201).
8. **On the agreement editor, a failed quiet re-read after a late publish success leaves the old draft on screen** `(unproven)` (C4-R2; log :4144).
9. **FU-4's two accepted side effects** on the position-override page: a create form reopened during a slow create stays key-locked until the old answer arrives; "Opret" is not held back during a quiet re-read (log :4204-4206).
10. **Actions the list already knows the server refuses ("Aktiver"/"Opret" while another row of the key is ACTIVE) are not disabled**; the 409 is shown (TASK-14838 rulings c2; log :4048).
11. **The anchor date `0001-01-01` is shown in ISO form with no worded label** ("since always"); a copy question (log :4049).
12. **The HR-facts date input's `form="pd-hrf-detached"` names no form**, which does not conform though its behaviour is defined (`HrFactsSection.tsx:426`; log :4352). Found while verifying: the QUAL-268 create form uses the same pattern (`CreateProfilePanel.tsx:195`, `:230`, `:256`, `form="pd-cp-detached"`).
13. **No retry control behind "Prøv igen"** after a failed HR-facts read (`HrFactsSection.tsx:32`, `:295`); HR recovers by changing the date or reopening the drawer (log :4353).
14. **"gemt" (saved) stays while HR edits the regime or the flex choice**; only a date change clears it (`HrFactsSection.tsx:337-340`, `:396-399`; Fable c3 NOTE 4; log :4354).
15. **The HR-facts placeholder's loading arm has no test** (Fable c3 NOTE 1; log :4355).
16. **Not reachable through the drawer today:** a person change without a remount keeps the previous person's recorded block, and a flex refusal focuses "Ja" (Fable c3 NOTEs 2 and 6; log :4356).
17. **An emptied date field in the person drawer is not blocked** `(unproven: what the server answers to an empty `effectiveFrom`)` (pre-existing; log :4422).
18. **Every other 400 on the users PUT shows the raw response body**: the shared helper returns the body text as the error (`frontend/src/lib/api.ts:646-647`; pre-existing, usability; log :4423).
19. **A malformed `nearestValid` would print "Invalid Date"** `(unproven, pre-existing)` (log :4424).
20. **The QUAL-268 create form's "later of the two dates" floor is not pinned when both dates exist** (Fable c2 N2; log :4539). Test gap.
21. **A carry-forward into a zero-length row would raise a category notice HR never named** `(unproven)` (Fable c2 N3; the reviewer found no reachable state, `S148-QUAL268-stack-5a-reviewer-c2.md:56`, `:104`; log :4540).
22. **A reload of the same person during a pending create re-enables "Gem ændringer"** `(unproven)` (Fable c2 N4; log :4540).
23. **The employee-profile GET reads "today" twice** (the handler `EmployeeProfileEndpoints.cs:195`, the repository's own clock inside `:242`), so across a Copenhagen midnight the 404 body can describe the next day; at worst a create the PUT then refuses with 409 (negligible; builder and consult agree; log :4541).
24. **Stale remarks for the comment sweep, not in `.claude/plans/s148/C1-comment-sweep-spec.md` yet:** `EmployeeProfileRepository.cs:591` (also `EmployeeProfileSeeder.cs:147`, `EmployeeEntitlementEligibilityRepository.cs:190`) names `EmployeeProfileRepository.InsertLiveRowAsync`, which does not exist (the builder's F3); the `Today()` doc's stale line cites (`EmployeeProfileRepository.cs:555-577`; F4); `PositionOverrideManagement.tsx:152` and the hook's `hasRows` comment and name (`usePositionOverrides.ts:80-82`; log :4203); `ProjectionBackfillService.cs:17-18`, the file header still naming two tables (the class summary at `:132-139` is already true); `PriorMonthNotApprovedException.cs:17-20` (item 1).
25. **Whether the employee-profile audit rows should carry `employmentCategory`** `(unproven as a defect)`: the events carry it, the mappers do not project it (`EmployeeProfileCreatedAuditMapper.cs:17-24`); the catalog's stale `weeklyNormHours` is A-22.
26. **`OrganisationDriftGuardTests.ProfileClose_OrganisationChangedUnderTheLock` failed once in a loaded local run**; it passed in CI-W6 (7/7, run 45582399602). Recorded only; no defect found (log :4472, :4580).
27. **Not registered by choice, recorded neutrally:** whether the year-end close should try a dated configuration row before refusing when the employee's current key has no open row (Fable N-7 of TASK-14842, "a separate question for the register, if the owner wants it"; log :1095). The existing fail-closed choice stands (ADR-033 D10).

Already in the sprint log, so not repeated: `formatDanishNumber`'s half rounding and the engine's host-culture messages (log :1583-1585); the 48-hour average shown half up (log :1699, its cite `RestPeriodRule.cs:594` is now `:648`).

## Duplicates and already-handled candidates (no row)

| Candidate | Disposition | Evidence |
|---|---|---|
| The Skema save queue's 15-second ordering limit | **Duplicate of QUAL-252** (M, registered at the 14840 stack merge) | `quality-finding-register.md:508`; log :1315 |
| The `/summary` CHILD_SICK row nothing reads | **Duplicate of QUAL-251** (M) | `quality-finding-register.md:507`; log :1011 |
| The 14802-T atomicity test gap | **Inside QUAL-016** (its deferred bespoke cluster): note A-16 | above |
| The schema-copy drift guard ignoring `work_time_projection` | **Inside QUAL-195**: note A-17 | above |
| The two Regression classes on `localhost:5432` | **Duplicate of QUAL-118**: note A-18 | above |
| O-24, `WeeklyMaxHoursReferencePeriod` unread | **Duplicate of QUAL-123**: note A-13 | above |
| The backfill tool not printing the new counters | **Already fixed** at the 14840 stack merge `a331e452` | `tools/ProjectionBackfill/Program.cs:85` prints the cleared counters |
| The stale `ProjectionBackfillService.cs:133` comment | **Already fixed** at `a331e452`: the class summary names all seven replayed types (`:132-139`). The comment sweep spec records the same. The file header `:17-18` is still stale (below the floor, item 24) | `C1-comment-sweep-spec.md:994` |
| The month-edge "truncated chain" limit | **Retired** by wave 3b: with the read from `monthStart − 2` no finding is mis-dated | wave-3b adjudication item 16; `PLAN-s148.md:845` |
| Payroll's anonymous 500 for a record gap (log :2630, "whether a register row covers this is `(unproven)`") | **Already fixed** by TASK-14831 item 10: a named 422 `payroll-employment-record-gap` | `Payroll/Program.cs:957`; `PeriodCalculationService.cs:1238` |
| The dead writer `AdjustUsedAsync` | **Already recorded** below the floor in the register's appendix at the 14842 merge | `quality-finding-register.md:398` |
| The wave-7 emitter comments ("nothing reads customs yet") | Record only; excluded from the sweep because they regenerate the golden | `C1-comment-sweep-spec.md:999`, `:1142` |

---

## Part C — for the Orchestrator

### Needs a ruling (each a genuine fork; the draft follows the lean)

1. **Teamoversigt's per-line live regions (below-floor item 2).** The plan's Records owed (`PLAN-s148.md:845`) lists it among "register rows". By the register's rubric it is Low: an accessibility deviation on one screen that misleads no reader and blocks no change. *Options:* (a) below the floor, as drafted; (b) a Medium row, on the grounds that accessibility is rising to a requirement (CONVENTIONS, "Usability & UX"). *Lean:* (a); the register's floor is the owner's ruling, and nothing here changes a figure.
2. **AC-5a's month-level limit (B-10).** *Options:* (a) a QUAL-BF row, as drafted: the wave-1 adjudication called it "a known limit, not a defect"; (b) a Medium QUAL row, since an employee cannot register an entitled day for part of a month. Either way the owner's note ("registered") becomes true. With (a), PAT-032 expects a `shortcut(QUAL-BF-011)` comment at `SkemaEndpoints.cs:337-345`, a code edit for the comment sweep or the next touch. *Lean:* (a).
3. **QUAL-276's severity (the parse copies).** The owner's S131 systemic rule says "family-alone-reaches-High" for a copied family; three of the four copies are copied byte for byte. *Options:* (a) M, as drafted (a twelve-line helper, one divergence, no proven wrong figure); (b) H by the S131 rule. *Lean:* (a); the S131 rule ranked families whose repetition was the defect across many sites, and the "borderline consistency-clusters" stayed Medium.
4. **QUAL-272 (F-2) as its own row or folded into QUAL-221.** Fable offered both. *Lean:* its own row, as the sprint log already ruled ("both are registered at close", log :518): QUAL-221 is the domain question, this is the mechanism.
5. **QUAL-234's severity.** With the rest arm closed, the row's own text says the remaining supplement arm "alone would be H". *Options:* re-rate to H now, or keep Critical until stage 2 closes the row. *Lean:* H, with the re-rating recorded in the same dated note (A-11 leaves the cell to you).
6. **The HR-facts consult's § 3.2 (B-12).** *Options:* (a) a pre-production ledger line in the security register, as the consult asked; (b) the sprint log only, since it is Low and not an access matter. *Lean:* (a).

### For the Orchestrator's attention (not forks)

7. **The file's location.** Written under `.claude/plans/s148/`, not `.claude/reviews/`, because of the drafter's write scope.
8. **QUAL-182 (A-15)** was not in the brief or the audit, but the plan's Records owed lists it as closed at S148; drafted.
9. **Documents outside the registers that these rows lean on, not drafted here:** the legacy-database runbook line for QUAL-273 (carry no typed `TimeEntryRegistered` events into a live payroll); O-8's `danish-agreements.md:234` and the source register's wage rows; ADR-043 D11 now differs from what was built (the route was retired, not made to answer 422; A-2 records it).
10. **Comment-sweep additions** (below-floor item 24): five stale remarks are not in `C1-comment-sweep-spec.md`. They could join it before it is applied, or wait for their next touch.
11. **The C2-R4 census** (`PLAN-s148.md:857`, the audit's § 4) is still not run. It may produce new rows (the QUAL-257 and QUAL-261 shapes); none is drafted here.
12. **F10's closure** rests on the year overview's CI before/after and TASK-14828's local read counts; there is no CI "before" figure for the Skema month read. The row's own condition ("closes when that lands and the figures fall") is met as drafted.

## Brief contradictions (claims about existing code or records; reported, not acted on beyond the draft)

| Claim | What the code or record shows | Where |
|---|---|---|
| Write the draft to `.claude/reviews/S148-C1-register-draft.md` | The drafter's scope is `.claude/plans/**` and `.claude/refinements/**` | this file's header |
| "the stale `ProjectionBackfillService.cs:133` comment, which goes to the comment sweep" | Already made true at `a331e452`; the sweep spec lists it as already fixed. Only the file header `:17-18` is still stale | `ProjectionBackfillService.cs:132-139`; `C1-comment-sweep-spec.md:994` |
| "the backfill tool's counters" (a candidate) | Already printed since `a331e452` | `tools/ProjectionBackfill/Program.cs:85` |
| "the dead writer `AdjustUsedAsync`" (a candidate) | Already recorded below the floor in the register at the 14842 merge | `quality-finding-register.md:398` |
| "the two Regression classes hard-coding `localhost:5432`" (a new candidate) | Already QUAL-118 (M, open) | `quality-finding-register.md:296` |
| "the schema-copy drift guard ignoring `work_time_projection`" (a new candidate) | Inside QUAL-195's stated scope | `quality-finding-register.md:451` |
| "the 14802-T atomicity test-gap" (a new row) | Inside QUAL-016's deferred bespoke cluster | `quality-finding-register.md:190`, S133 block `:90` |
| "the two month-edge limits" | One is retired by wave 3b; one remains (QUAL-277) | wave-3b adjudication item 16 |
| The plan's O-24 cite `RestPeriodRule.cs:586-596` | The 48-hour check is `:623-657` | `RestPeriodRule.cs:623` |
| The plan's O-30 cites `:106-491` and `:685-698` | At the merge `1bba25a0` and at HEAD the PUT is `:125-528` and the request `:782-807` | `ConfigEndpoints.cs:125`, `:782` |
| SEC-061's status cell: `ProfileAlignmentValidator.cs:84-86` | The three checks are `:85-87` (`:83` is the method) | A-12b |
| The plan's "final + 9" row text and the 14814 adjudication: "neither screen renders" the compliance error | Teamoversigt renders "Advarsler kunne ikke hentes"; only Skema is silent | `TeamOversigt.tsx:320-323`; `SkemaPage.tsx:332` |
| F9's register text: "TASK-14831 corrects that remark" | It did not; the comment still says "Blocks until granted" | `PayInputsLock.cs:38`; A-19 |
| QUAL-232's row: "the legacy route answers 422 for any type other than `NORMAL`" | The route was retired (404 or 405) | `TimeEntryRouteRetiredTests.cs:64`; A-2 |
| The audit report's sprint-log line numbers | Shifted by +61 after line 30 at `6ab643a7` (the ledger commit) | `SPRINT-148.md:30` |

---

## Revision history

| Draft | Date | By | What |
|---|---|---|---|
| 1 | 2026-11-25 | Planning Drafter (Opus 5.5) | First draft at `6ab643a7`: 22 edit blocks (26 replacements), 12 new entries (QUAL-271 … 279, QUAL-BF-011, F11, one SEC ledger line), 27 below-floor items, 6 duplicates, 5 already handled; Part C with six forks |
