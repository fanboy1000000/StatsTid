# D-5 — F-3: the overtime account, where decided hours wait until they are taken as time off or paid

*Brief, draft 2, 2026-10-29: adjudicated (`.claude/reviews/S147-D-batch3-adjudication.md` § D-5 and rulings 1, 3 and 4; RULED-WITH-QUESTIONS). **Told — no owner question** (ruling 1). Drafted by `planner` (Opus 5.5). Code facts verified at `26b70a08` and re-checked at `bd5a3d3f`: no file under `src/`, `frontend/`, `tools/`, `docker/` or `tests/` changed in between. It builds on D-1 = (b), D-2 = (a), D-4 = (c), D-6 = (a), D-7 = (b), D-8a = (b), D-8b = (a), D-9 = (a), D-10 = F-7 (c) default (ii) and D-11 = (b), all answered 2026-10-29. It also builds on batch 2's recorded proposals (D-4.P1–P6 with ruling 1; R-6 with ruling 4) and on R-7 as widened by batch-3 ruling 3. The Orchestrator tells it first, as its own prose block; D-12 is asked after it.*

**D-5 in plain words.** Under ADR-043 an hour a leader ordered or accepted (a *decided* hour) leaves flex and waits in an **overtime account** until the employer gives it as time off or pays it. This brief settles how that account works. There is nothing for the owner to choose:
- the agreement texts fix who decides (the employer) and the clocks (when time off is due, and what happens when it is not given);
- only one storage shape can apply those clocks;
- the one design choice inside the account, whether StatsTid also keeps the employee's *saved days* (*opsparingsdage*) as a balance of its own, is settled as "yes, designed", after a short piece of research. When it is built is S148's plan's call.

The owner is told all of it and can ask for any part to be put as a question.

## What is decided, and the saved-days design (P10)

**Why there is no owner question** (D-5.1, ruling 1).
- **The storage shape is told, not asked.** ADR-043 F-3 offers three shapes (`ADR-043:271-277`): (a) feed today's yearly `overtime_balances` row; (b) a movement ledger; (c) rails on `payroll_export_records`. Only (b) can apply the § 10 clock: time off is due 3 months after the end of the norm period the hours were worked in (D-4.2 item 7).
  - **(a) is not a valid path.** One total per employee and year cannot say which hours have reached their deadline, so it can never compute the deadline's default, which is pay. That is a wrong result, so it fails domain correctness (`ADR-043:272-273`).
  - **(c) is excluded by the ADR's own text:** "Does not fit: compensation is decided after the month is exported" (`:276`).
- **Where saved days live is a scope item, not an owner fork.** This is the same pattern as R-5's opening flex balance under batch-2 ruling 3.
  - **Either way, stage 2 records the employee's saved-days election as a movement out of the overtime account,** so no hour the employee chose to save is ever paid. That is a correctness requirement (the election is in the agreement; HIGH confidence), not a choice.
  - **The only difference is whether StatsTid also holds the resulting saved-days balance.** That cannot be priced or built until § 11, the saved-days paragraph, is researched (research cell R-A, below). Asking now would ask the owner to choose S148's scope before its cost is known; S148's refinement decides scope.
  - **The end state is not open.** Saved days are paid-absence days StatsTid must register and credit anyway (PAV 19.3.1.6, `docs/references/sick-day-credit-and-agreement-texts-research.md:531`; § 7 nr. 2), and the standing rule is to make hidden balances visible. A saved-days balance kept outside StatsTid as the end state would be a balance nobody checks against its cap.
- **"No election: the deadline always pays" is not offered** (confirmed). The employee's right to choose saved days instead of cash is in the agreement (022-26, remark to § 10: *"medmindre den ansatte vælger at få den tilgodehavende afspadsering konverteret til opsparingsdage"*; `docs/references/agreement-cells-reverification-research.md:667`). Paying hours the employee chose to save would be a wrong payroll result: domain correctness.

**D-5.P10, the saved-days design: a recorded design proposal** (D-5.1, D-5.3).
- **The design:** StatsTid holds a saved-days balance:
  - an election screen for the employee;
  - a Skema "saved day" type in the credit class (see P5 for what the credit class is);
  - the cap as a local parameter (D-8's list), with a dated central floor: 74 h before OK26, 111 h from 1 April 2026. That is an OK-version transition StatsTid is built to hold.
- **First,** a research cell on § 11 (R-A, below).
- **Its sprint** is S148's refinement's.
- **Until it lands,** the election is recorded as a movement out of the overtime account, and a saved day taken is registered as paid leave (`LEAVE_WITH_PAY`). The sprint log names this as the interim limit.
- **The owner is told,** with "say so if you want it asked".

**How it is told** (the adjudicator's asking order). D-5 is told first, as its own prose block: the points under "What the owner should know", below. It ends: *"this one has no question — say so if you want any of it asked."* D-12 is asked after it.

## Why it matters

- **Today an ordered hour is paid automatically, in the month it is worked.** Under ADR-043 a decided hour leaves flex and waits in an overtime account, separate from flex (D8, `ADR-043:141-149`). It stays there until the employer gives it as time off or pays it.
- **The account has a clock.** Time off must be given within 3 months of the end of the norm period in which the hours were worked. After that the hours are paid at the next pay date, unless the employee has agreed to postpone (up to a year) or chooses saved days.
- **A worked example**, continuing ADR-043's own (`ADR-043:43-53`): a release the manager ordered, Tuesday 17 June 2025, 17:00–23:00.
  - **Earned:** 6 hours at +50 %. That is 9 hours off, or 6 hours of pay plus 50 %; the employer chooses (V10).
  - **The employer's choice is time off,** the default. The 9 hours wait in the account, with a deadline of 30 September 2025: 3 months after June ends.
  - **If the employee takes 9 hours off in August,** the account is empty, and those days off leave flex unchanged.
  - **If nothing is taken by 30 September,** the 6 hours plus 50 % go on the October 2025 payslip: the pay run of the month after the deadline month (P4).
  - **If the employee instead chooses saved days by the deadline,** the 9 hours leave the overtime account as saved days and are never paid. StatsTid records that from stage 2; once P10 is built, it also holds the saved days.
- **It closes the compensation half of SEC-056 in design** (ruling 4(b)). Today's compensate route lets a leader adjust their own overtime balance (`docs/operations/security-finding-register.md:216`). That is a *latent shape*, not a live capability: no production write ever puts hours in the balance (below), so the route has nothing to pay. Under the account, compensation only ever draws on decided hours, and never by a person on their own hours (P2).
- **Who notices:** employees with ordered hours (their account and their payslip), leaders (granting time off, the deadline list), HR (postponements, saved days) and payroll.

## What StatsTid does today (verified at `26b70a08`, re-checked at `bd5a3d3f`)

- **The account exists in name only.**
  - `overtime_balances` is one row per employee and year: `accumulated`, `paid_out`, `afspadsering_used` and a compensation model (`docker/postgres/init.sql:1992-2008`). It is unique on (employee, year), with no agreement in the key (`:2002`).
  - **Nothing in production ever feeds `accumulated`.** `AdjustAccumulatedAsync` (`src/Infrastructure/StatsTid.Infrastructure/OvertimeBalanceRepository.cs:50`) has callers only in tests (`tests/StatsTid.Tests.Regression/Config/S122CompensationVocabularyTests.cs:133`), and no seed writes the table. The compensation-choice PUT creates a row at zero (`src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs:721`) or keeps an existing row's figure (`:737`). So every balance is absent or zero.
- **Overtime is paid automatically in the month worked.** The overtime rule emits `OVERTIME_50`, `OVERTIME_100` and `MERARBEJDE` lines (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/OvertimeRule.cs:55`, `:75`, `:86`).
  - The time-off/pay split types (`OVERTIME_50_PAYOUT`, `OVERTIME_50_AFSPADSERING`, …; `src/SharedKernel/StatsTid.SharedKernel/Models/OvertimeResult.cs:17-22`) exist, and so do their wage-type rows (`init.sql:2044-2061`). But no rule emits them.
- **The compensate route draws on that unfed balance** (`OvertimeEndpoints.cs:512-602`, `LeaderOrAbove`).
  - It checks org scope (`:524`), then the remaining hours, outside the transaction (`:535-541`). Remaining is `Accumulated − PaidOut − AfspadseringUsed` (`src/SharedKernel/StatsTid.SharedKernel/Models/OvertimeBalance.cs:12`), always 0 today, so the route answers 404 or 400 before any update: **it is unreachable with hours by any production path.**
  - It adjusts `paid_out` or `afspadsering_used` (`:567-576`), and emits `OvertimeCompensationApplied` with `ConvertedHours = Hours`: no +50 % conversion (`:549-560`).
  - It writes no audit row (Pattern C, `:543`).
  - **It discards the guarded update's result** (`:569-576`; the guard is the `WHERE` in `OvertimeBalanceRepository.cs:76-99`). Under a race the event would report a compensation the row refused. That is now registered as **QUAL-239** (Medium; ruling 4(c)): it retires with the route under D-5; if the route survived any stage, the fix would be to check `Success` and return 409 before the enqueue.
  - It has no approved movement behind it and no self check. T9 adds a self check (TASK-14705, wave 2a, not on master at `bd5a3d3f`); **T9 stands as briefed** (ruling 4(a)): defence in depth until D-5 retires the route.
- **The employee chooses time off or pay, against the sources** (QUAL-221, `docs/operations/quality-finding-register.md:445`).
  - `PUT /api/overtime/{employeeId}/compensation-choice` (`OvertimeEndpoints.cs:670-751`) lets employees set their own year's model where `EmployeeCompensationChoice` is true (`:707-708`).
  - That is HK and PROSA, both in the seeds (`init.sql:1468`, `:1474`, `:1480`, `:1486`) and in the compiled configs (`src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs:67`, `:98`, `:152`, `:183`).
  - The frontend hook `useCompensationChoice` (`frontend/src/hooks/useCompensationChoice.ts:28`) is imported by no page. **One test imports it** (`frontend/src/hooks/__tests__/employeeTypedWire.test.ts:29`, its describe block `:295-317`), so the test retires with the hook (D-5.2 item 2).
- **The balance is served, not shown.** The balance summary returns an overtime block (`src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs:227`, `:461-467`). No frontend component reads it; only `frontend/src/hooks/useBalanceSummary.ts` types it.
- **There is no absence type for time off in lieu.** Neither Skema's catalogue (`src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:34-38`) nor `AbsenceTypes` (`src/SharedKernel/StatsTid.SharedKernel/Models/AbsenceEntry.cs:13-25`) has one, though SYSTEM_TARGET §M says taking time off "creates an absence entry" (`SYSTEM_TARGET.md:410`).
- **No deadline exists.** No expiry field exists anywhere. HRP-021 records the 3-month conversion as a hand-off with "no code counterpart". It is NOT READY because "who arranges afspadsering" was unknown (`docs/operations/hr-follow-up-process-register.md:131`). P7 answers that.
- **No saved days exist.** There is no saved-days (*opsparingsdage*) concept in `src/` or `init.sql`, and SYSTEM_TARGET §M names none (`SYSTEM_TARGET.md:395-421`).

## What the sources say

- **The employer chooses time off, pay, or a mix** (V10, PAV 19.2.7.1, `docs/references/overtime-flex-model-research.md:40`; C7, `agreement-cells-reverification-research.md:667`, `:675-678`).
  - Time off is the primary path. The employer may pay early *"hvis ansættelsesmyndigheden vurderer, at afspadsering ikke vil være mulig eller hensigtsmæssig"*: if it judges time off impossible or inappropriate (`:667`).
  - Confidence is HIGH that the employee does not choose (`:692`).
- **§ 8 overtime: the clock** (§ 10 stk. 1 and its remark; V10 `:40`; C7 `:667`).
  - *"Afspadsering skal gives senest 3 måneder efter opgørelsesperiodens udløb"*: time off within 3 months of the norm period's end.
  - It *"kan efter aftale med den ansatte udskydes i op til 1 år"*: postponable by up to 1 year, by agreement with the employee.
  - After the deadline, *"skal der ske betaling ved den følgende lønudbetaling, medmindre den ansatte vælger … opsparingsdage"*: payment at the following pay date, unless the employee chooses saved days.
- **The employee's only two choices** are agreeing to postpone, and saved days at the deadline. The verifier recommends `CompensationChooser = EMPLOYER` plus *"the two explicit employee elections, each audited"* (`:690`; the table `:682-688`).
- **Saved days: the cap.**
  - OK26 changed only § 11 stk. 2, to a floor of 111 hours: no maximum below 111 h may be set (`:239`; `overtime-flex-model-research.md:196`; 022-26's introduction, `docs/references/sick-day-credit-and-agreement-texts-research.md:744`). PAV still says 74 h, the pre-OK26 floor (`sick-day…research.md:393`).
  - *Not in the verified dossiers:* who sets the maximum, how saved days are taken, whether they lapse, and what happens at leaving. That is research cell R-A.
  - PAV 19.3.1.6 lists saved days among the days credited in the working-time account (`sick-day…research.md:531`).
- **§ 14 weekend compensation has its own clock** (`sick-day…research.md:858-864`; C7 `:670`).
  - Time off may be given before or after the weekend.
  - It should be given in whole or half days where possible.
  - If not taken by the end of the **following** norm period, it is paid in cash at the next pay date.
  - If the employee is sick when planned time off begins, they need not start it (§ 14 stk. 4, `:851`).
- **Time off taken counts like other paid absence.** § 7 nr. 2's remark lists *afspadsering* among paid-absence days (`sick-day…research.md:104-106`). § 14's remark: time off *"medregnes på afviklingstidspunktet i arbejdstidsopgørelsen som anden afspadsering, jf. § 7, nr. 2"*, that is, counted when taken (`:144`, `:862`).
- **AC (stage 3).**
  - Merarbejde (extra work) is compensated as time off with +50 % *"så vidt muligt"* (where possible), otherwise as pay (§ 14 stk. 5–6, `agreement-cells…:673`).
  - OK26 lets time off be postponed up to a year by agreement (V9, `overtime-flex-model-research.md:39`).
  - The employee may choose saved days instead of merarbejde pay (AC § 15, `agreement-cells…:226`).
  - An AC deadline is not stated in the dossiers. That is research cell R-B.
- **The *opsparing af frihed* election** existed only from 1 May 2025 to 31 March 2026. 024-26 abolished it, and no new time can be saved from 1 April 2026 (`agreement-cells…:51-55`, `:232-238`; ADR-043 D8, `:144-145`).

## Research owed (ruling 1)

*Named with letters to keep them apart from batch 2's recorded proposals R-1..R-7 and S147's research gates R1..R5.* **None blocks telling D-5, building stage 2's ledger, or the ADR's acceptance;** the ADR lists them as owed research.

| Cell | What it must find | What it blocks |
|---|---|---|
| **R-A** | § 11 in full: who sets the saved-days maximum; how saved days are taken; whether they lapse; what happens at leaving; whether § 14 weekend-compensation time off may be converted too; whether AC § 15 mirrors it. Also whether the saved-days election may be made before the deadline (D-5.2 item 5) | **Building P10 only.** Not the telling, and not stage 2's ledger |
| **R-B** | The AC merarbejde time-off deadline under § 14 stk. 5, if there is one | **Stage 3 only** (AC claims) |
| **R-C** | A leaver's owed time off and saved days: when they are settled, and in what form | **An expert item for S148's refinement** (P9 (ii)) |

## The recorded proposals (adjudicated, told to the owner, not asked; D-5.4)

All ten are listed in `../ADR-043-working.md`, "Provisional items and recorded proposals", items 27–36.

**D-5.P1, the account is a movement ledger: F-3 (b), told. Accepted, with D-5.2 item 3.**
- **Every movement is dated and audited,** and frozen into the manifest of the month it lands in. A movement never edits an exported month (ADR-034; D-4.P2).
- **The movements:**
  - **earned:** the norm period worked, the hours, the rate, and the kind (§ 8 overtime; § 14 weekend compensation; AC merarbejde in stage 3);
  - **taken as time off;**
  - **paid** (by decision, or by the deadline's default);
  - **postponed;**
  - **converted to saved days.**
- **The constraint:** *one writer per table; the account's balance and each earning's deadline are derivations the pure engine recomputes from frozen inputs (ADR-002), never a stored total anyone adjusts.* That is why replay can re-prove them.
- **Where the movements are stored is S148's refinement's call:** one table, or a Backend fact table plus Payroll's own movement table, read under the read-only contract on Backend tables (ADR-016 D10). D-2 = (a) is the precedent: payroll reads a Backend table it does not write.
- **Not (a) or (c),** for the reasons above.

**D-5.P2, compensation only behind an approved movement (SEC-056), on ruling 1's four conditions. Accepted.**
- Every taken, paid, postponed or converted movement draws on decided earned movements, never on a bare balance.
- **Every decision about another person's account** (granting time off, paying early, recording a postponement) is made by whoever holds that employee's month-approval authority (ruling 1 (d)).
- It is **never on the decider's own hours** (1 (b): 403).
- **A request that awaits a decision,** such as the employee's time-off booking, is decided by someone else (1 (c)).
- **The employee's two elections, postponing and saving, are the employee's own acts,** recorded under their identity. They are not compensation decisions, so 1 (b) does not bar them.
- **What retires:** today's compensate route (`OvertimeEndpoints.cs:512-602`) and the compensation-choice routes (`:606-751`), with the yearly row.

**D-5.P3, the employer chooses, and time off comes first (V10, C7; QUAL-221). Accepted.**
- The default form is time off at +50 %: 6 decided hours become 9 hours off.
- The decider may choose pay instead when time off is judged impossible or inappropriate (§ 10 remark), recorded with a reason.
- `EmployeeCompensationChoice` and the choice routes retire.
- Payroll gets hours and wage types, never kroner (the payroll boundary). The time-off/pay wage-type rows survive (`ADR-043:191`); the 100 % rows retire (D4).

**D-5.P4, the clocks: facts, not questions (D-4.2 item 7). Accepted, with D-5.2 items 4 and 5.**
- **§ 8 overtime (HK, PROSA):** time off is due within 3 months of the end of the norm period in which the hours were worked. If it is not given, the hours are paid at the following pay date, unless postponed by agreement or converted to saved days.
- **When the default pay lands** (item 4). Under the monthly norm (D1) a norm period is a calendar month, so the § 10 deadline always falls on a month end. *"Ved den følgende lønudbetaling"* (at the following pay date) is therefore the pay run of the month after the deadline month: a 30 September deadline pays in the October pay run. The same holds for § 14. A build input; the domain expert confirms.
- **The saved-days election is recorded before the calculation that would pay the default runs** (item 5). Whether it may be made earlier than the deadline is for R-A. A build input.
- **A late decision** (D-4.P2) dates its earned movement in the decision month, but its clock still runs from the worked period. If that clock has already run out, the movement defaults to pay at once, or to an agreed deferral.
- **§ 14 weekend compensation:** time off by the end of the following norm period, else cash at the following pay date. Time off may come before the weekend, and § 14 stk. 4 (sick at the start of planned time off) applies.
- **AC merarbejde (stage 3):** time off where possible, and postponable up to a year by agreement. Whether AC has a deadline is research cell R-B.
- **The part-timer band (D-10, default (ii))** earns at +50 % from the part-timer's own norm, like a full-timer's overtime.

**D-5.P5, taking time off is a Skema absence. Accepted.**
- **A new "time off in lieu" (*afspadsering*) type in the credit class.** *The credit class* is TASK-14708's classification: every absence code is exactly one of credit, reduction or neutral, in one place (`.claude/plans/s147/TASK-14708.md:37`; the new `AbsenceClassification.cs` it creates, not on master at `bd5a3d3f`). A credit-class day is credited by the paid-absence credit basis (R-6, ruling 4): the weekday's office hours where an institution sets them (D-7 = (b)), else 7.4 h × fraction. A whole day off leaves flex unchanged.
- It draws on the earning with the earliest deadline first, and cannot exceed the time off owed.
- Like every credit-class type, a day's time-off hours may not exceed the credit basis minus the hours worked that day (R-7 as widened, batch-3 ruling 3). *Build input, the planner's reading of ruling 3's quota note:* time off in lieu draws on the account, so a backstop that clips its credit must also clip the draw on the account, or refuse the month.
- It is approved with the month, under OQ-1's two signatures.
- The employer's right to place time off with notice (AC: 14 days, `agreement-cells…:673`) is a build detail.

**D-5.P6, a postponement is a record both parties sign. Accepted.** It is agreed between the decider and the employee (§ 10 stk. 1, *"efter aftale med den ansatte"*), dated and audited, and moves one earning's deadline by up to a year.

**D-5.P7, the deadline is a visible hand-off: HRP-021 becomes decision-ready. Accepted.** This is the HRP row ADR-043 promises for the 3-month deadline (`ADR-043:217-218`; today NOT READY, `hr-follow-up-process-register.md:131`).
- **Trigger:** an earning's deadline approaching or passed.
- **Accountable:** the employee's month approver. The employer arranges time off (V10, C7).
- **Surfacing:** the leaders' "time off due" list and the employee's account screen, each earning with its deadline. Today's served-but-unshown block (`BalanceEndpoints.cs:461-467`) gets a screen.
- **Resolution:** time off granted, pay by default, a postponement, or an election, each audited.
- **The deadline's default (pay) is computed in the month's calculation** (its timing: P4) and shown on the payslip and in the account, never silently.

**D-5.P8, the dated *opsparing af frihed* election is not modelled. Accepted.** Its window closed on 31 March 2026, and after the reseed no balance exists.

**D-5.P9, a leaver's owed time off. Accepted, split in two** (D-5.2 item 6).
- **(i) Owed time off is paid, never forfeited, at leaving.** This is derived from § 8 and § 10: pay is the fallback whenever time off is not given, and time off cannot be given after the employment ends.
- **(ii) The timing ("the leaving month") and what happens to saved days at leaving are unsourced.** "Paid in the leaving month" is a labelled default and an expert item (research cell R-C).
- It sits beside R-4's flex settlement, in HRP-011's leaver lifecycle.

**D-5.P10, the saved-days design.** Above, under "What is decided".

## What it builds on (the owner's answers, and how they narrow it)

- **D-1 = (b), stages:** the account is **stage 2**, with the marking workflow and the supplements. Before stage 2 no hour is overtime, so nothing is earned. AC's earnings arrive with stage 3.
- **D-4 = (c):** the account receives decided hours from two sources: an order or roster span recorded in advance, matched to the worked intervals; or an employee's mark the leader accepted afterwards, inside month approval. Each record names its hours, which is what lets every earning carry its own clock. D-4.P2's decision-month movement and D-4.2 item 7's clock are folded into P4; ruling 1 (D-4.P4) shapes P2.
- **D-6 = (a):** the saved-days cap (P10) is a dated local value, frozen into the manifest.
- **D-7 = (b):** where an institution sets office hours, a day of time off in lieu is credited with that weekday's office hours (P5).
- **D-8a = (b):** a person outside the flex scheme still has their ordered hours routed by their D-11 regime, so they can still earn into the account.
- **D-8's R-6 (ruling 4) and R-7 (as widened, batch-3 ruling 3):** time off in lieu, and saved days under P10, are paid-absence days read by the paid-absence credit basis, and bounded by the hours worked that day.
- **D-9 = (a):** AC claims are decided claim by claim. An accepted claim's hours earn into the same ledger in stage 3.
- **D-10 = F-7 (c), default (ii):** a part-timer's ordered hours earn +50 % from their own norm.
- **D-11 = (b):** the regime decides whether an hour earns at all. Exempt HK staff earn no § 8 overtime but may earn § 14 compensation on ordered or rostered weekend work (`sick-day…research.md:870`).
- **Not proposed here:** an opening overtime balance at go-live. It is the same design question as R-5's opening flex balance (ruling 3). Its sprint is S148's refinement's, and it is noted there.

## What the owner should know (D-5.5; told, no question)

- **Decided hours wait in an overtime account,** from stage 2. The employer chooses, time off first: 6 ordered hours become 9 hours off.
- **Every earning has a clock: 3 months after the month it was worked.** Untaken time off is then paid on the next payslip automatically, unless the employee agreed to postpone it (up to a year) or chose saved days. Weekend compensation has a shorter clock.
- **No one can grant time off or pay out on their own hours,** and nothing is ever paid from hours nobody decided. SEC-056's compensation half closes.
- **The employee's "choose pay or time off" setting goes away.** It contradicted the agreements. Their two real choices, postponing and saving, are recorded instead.
- **Taking time off in lieu becomes a Skema absence.** A whole day off leaves flex unchanged.
- **Overtime pay lines appear when the employer pays, or at the deadline,** not automatically in the month worked, so demo payslips move in stage 2.
- **Leaders get a "time off due" list.**
- **The one design choice inside this,** whether StatsTid also holds saved days as its own balance, is settled as "yes, designed", after a short piece of research on § 11. When it is built is S148's plan's call. Until then the choice itself is recorded, so no hour an employee chose to save is ever paid.
- **A leaver's owed time off is paid, never forfeited.** When, exactly, is an expert item.
- *Closing line:* "this one has no question — say so if you want any of it asked."

## What the S148 build gets (stage 2 unless named; reseed)

- **The movement ledger** (P1), with its derived balance and per-earning deadlines, frozen into each month's manifest. One writer per table; where the movements are stored is S148's refinement's call.
- **Earnings from decided hours** (§ 8 and § 14), fed by D-4 = (c)'s orders and accepted marks.
- **The decider's form choice,** with time off at +50 % as the default and pay with a reason (P3).
- **The clocks** (P4): the § 10 and § 14 deadlines; the deadline's default to pay in the pay run of the month after the deadline month (the expert confirms); a late decision's clock from the worked period.
- **The employee's two elections** (P2, P4): a postponement both parties sign (P6), and the saved-days election, recorded as a movement out of the account before the calculation that would pay the default runs.
- **The time-off-in-lieu absence type** in the credit class (P5), bounded by widened R-7, with its draw on the account clipped or refused alongside its credit.
- **SEC-056's guards on ruling 1's terms** (P2).
- **HRP-021 designed** (P7): the leaders' due list and the employee's account screen (`ux`).
- **A leaver's owed time off paid** (P9 (i)); "in the leaving month" as a labelled default until R-C.
- **The saved-days interim** (P10): a saved day taken is registered as `LEAVE_WITH_PAY`, named in the sprint log as the interim limit.
- **P10 itself** (the saved-days balance, its cap parameter with the dated floor, the Skema "saved day" type, the election screen) when S148's refinement schedules it, after R-A.
- **Retirements:** the yearly `overtime_balances` row; the compensate and compensation-choice routes (with QUAL-239's defect); the unused `useCompensationChoice` hook and its test (`frontend/src/hooks/__tests__/employeeTypedWire.test.ts:29`, `:295-317`); `EmployeeCompensationChoice`; the 100 % wage-type rows.
- **Stage 3:** AC merarbejde earnings into the same ledger; its deadline after R-B.
- **Documents owed to the owner:** SYSTEM_TARGET §M (below).

## The ADR-043 text it rewrites

- **F-3** (`:271-277`) becomes a decision: (b), told (P1), with P10 as the saved-days design.
- **D8** (`:141-149`): the account's shape, the clocks (P4), the employee's two elections and P10. "Its shape is F-3" (`:149`) is closed.
- **D5** (`:110-117`): an accepted AC claim earns into the same ledger (stage 3); AC's deadline is R-B.
- **"What it retires"** (`:169-181`) gains the yearly `overtime_balances` row (`init.sql:1992-2008`) and the compensate and compensation-choice routes. The employee's compensation-choice row (`:181`) is already listed.
- **Consequences, "New hand-offs"** (`:217-218`): the 3-month deadline row is designed (HRP-021, P7).
- **Owed research:** the ADR lists R-A, R-B and R-C, none blocking its acceptance (ruling 1), beside "What this ADR does not decide" (`:330-336`).
- **Outside ADR-043:**
  - SYSTEM_TARGET §M (`SYSTEM_TARGET.md:395-421`): "Employee choice" (`:403`), "Overtime Balance" (`:407-412`), and saved days (P10). All of it is owed to the owner;
  - **SEC-056's row, corrected at close** (ruling 4(b), the Orchestrator's): the compensate half is a latent shape, not a live capability; the High label rests on approve/reject; the "→" cell gains "closed in design by D-4 = (c) with ruling 1 and by D-5.P2; T9 closes the self part in S147";
  - QUAL-221's route;
  - QUAL-239, registered 2026-10-29, retires with the route; SEC-056's row cross-references it at close;
  - HRP-021, decision-ready (P7).
- The recorded proposals become ADR text once D-5 has been told, unless the owner asks for any of it to be asked.

## Waits on

**Nothing for the telling.** Building P10 waits on R-A; stage 3's AC deadline waits on R-B; the leaver's timing is R-C, an expert item for S148's refinement. The rest is built in stage 2.

---
*Revision history.*
- Draft 1 (2026-10-29): first brief, at `26b70a08`, drafted before D-4's answer on the adjudicator's ruling that it may be.
  - **The reframing, for adjudication:** F-3's storage shape is proposed as told (P1), because only (b) can apply the § 10 clock. The owner question proposed is where saved days live, (i) or (ii), with (iii) "no election" proposed as not offered.
  - **The § 10 clock** is recorded as a fact (P4; D-4.2 item 7).
  - **SEC-056's compensation half** is put on ruling 1's terms (P2).
  - **Nine recorded proposals** are proposed.
  - **Nothing depends on D-4's (c) or (a).**
- Draft 2 (2026-10-29, re-checked at `bd5a3d3f`): the batch-3 adjudication applied (`.claude/reviews/S147-D-batch3-adjudication.md`, RULED-WITH-QUESTIONS). **D-5 is told, with no owner question** (ruling 1).
  - **D-5.1 / ruling 1:** the question, options and lean sections collapse into one told section, "What is decided, and the saved-days design (P10)": the storage told in one sentence with its reason; the saved-days home a scope item; (iii) not offered, confirmed. The lean (i) becomes **P10, a recorded design proposal**. The asking-order closing line is added.
  - **D-5.2 item 1:** D-4 = (c) is written as a fact in the header, in "What it builds on" (the old "Independent of D-4's answer" paragraph is gone) and in "Waits on" (nothing for the telling; P10's build waits on R-A).
  - **Item 2:** the `useCompensationChoice` test import (`employeeTypedWire.test.ts:29`, `:295-317`) is added to "today" and to the retirements.
  - **Item 3:** P1 carries the full constraint (one writer per table; derived, never adjusted) and the storage-home choice for S148's refinement (the D-2 = (a) precedent).
  - **Item 4:** the default pay lands in the pay run of the month after the deadline month, the same for § 14; placed in **P4**, as D-5.4 attaches it (D-5.2 labels it "P7"); P7 points to it; the worked example now says October 2025.
  - **Item 5:** the election is recorded before the calculation that would pay the default runs; early election is for R-A (P4, R-A).
  - **Item 6:** P9 split into (i) derived and (ii) a labelled default, R-C.
  - **Item 7:** "credit class" glossed as TASK-14708's classification (P5).
  - **Item 8:** verified facts kept; `OvertimeBalance.cs:12` and the zero-row writes (`OvertimeEndpoints.cs:721`, `:737`) added to show the route is unreachable with hours.
  - **D-5.4:** P1–P9 marked accepted (P2 on ruling 1's four conditions; P4 with items 4 and 5; P7 with HRP-021's trigger, accountable party, surfacing and resolution; P9 split); P10 added.
  - **D-5.5:** "What the owner should know" replaced by D-5.5.
  - **Research cells R-A, R-B and R-C** named, each with what it blocks; none blocks the ADR's acceptance.
  - **"What the S148 build gets"** is one list.
  - **Ruling 3:** P5 notes widened R-7 and, as the planner's reading, its quota note applied to time off in lieu.
  - **Ruling 4:** the SEC-056 wording in "Why it matters" becomes a latent shape (4(b)); T9 stands as defence in depth (4(a)); the discarded result is QUAL-239 (4(c)); HRP-021 confirmed (4(d)).
