# D-5 — F-3: the overtime account, where decided hours wait until they are taken as time off or paid

*Brief, draft 1, 2026-10-29: not adjudicated. The owner has not been asked. Drafted by `planner` (Opus 5.5). Code facts verified at `26b70a08` (master HEAD). It builds on D-1 = (b), D-2 = (a), D-6 = (a), D-9 = (a), D-10 = F-7 (c) default (ii) and D-11 = (b), all answered 2026-10-29. It also builds on the batch-2 adjudication's recorded proposals (D-4.P1–P6, with ruling 1, and D-8's R-6, with ruling 4). The adjudicator ruled it draftable before D-4 is answered (`.claude/reviews/S147-D-batch2-adjudication.md`, "Asking order"). Its asking waits on D-4's answer.*

**The question in one sentence (proposed).** At the deadline for time off owed, an employee may convert the time off into saved days (*opsparingsdage*) instead of being paid. Should StatsTid keep those saved days as a balance of its own, or only record that the hours left the overtime account, leaving HR to keep the saved days outside StatsTid?

**For the adjudicator: the asked question is reframed.** ADR-043 F-3 asks where decided hours wait. It offers three shapes (`ADR-043:271-277`; working copy `../ADR-043-working.md`, F-3 in the verbatim body):
- (a) feed today's yearly `overtime_balances` row;
- (b) a movement ledger;
- (c) rails on `payroll_export_records`.

Only (b) is a valid path, given the fact D-4.2 item 7 records: the 3-month time-off clock runs from the end of the norm period in which the hours were worked.
- **(a) cannot apply that clock.** One total per employee and year cannot say which hours have reached their deadline. So it could never compute the deadline's default, which is pay at the next pay date. That is a wrong result, so (a) fails domain correctness.
- **(c) cannot apply it either.** Rails on `payroll_export_records` hold a month's totals, not one entry per earning. And compensation is decided after the worked month is exported (ADR-034's lock); F-3 itself says (c) "does not fit".

So the planner proposes that the storage shape be **told, not asked** (D-5.P1, below), as R-1 was for F-5. The genuinely open choice inside the account is what StatsTid holds after the employee's saved-days election. That is the question drafted here.
- *If the adjudicator judges the saved-days home a scope item,* like QUAL-225's opening balance under ruling 3, then D-5 has no owner question and is told in full.
- *If the adjudicator prefers to ask F-3's storage as the plan names it,* (b) would be the only option offered, so it would be told anyway.

**Independent of D-4's answer** (the Orchestrator's brief). The account receives decided hours. Under D-4 (c) those come from an order matched to worked intervals, or from an accepted mark. Under D-4 (a) they come from accepted marks only. Either way the account gets the same thing: hours, the norm period they were worked in, the decision month, the kind (§ 8 overtime or § 14 weekend compensation) and the decider. **No part of this question, and no recorded proposal below, depends on which of (c) or (a) the owner picks.** Nothing is flagged.

**The question as it will be asked** — *proposed wording; the adjudicator settles it.*

> *Hours a leader has ordered or accepted will wait in an overtime account until the employer gives them as time off or pays them. Time off owed and not given within three months of the end of the month the hours were worked is paid on the next pay date — unless the employee chooses instead to convert it into saved days (opsparingsdage), which may be capped, but not below 111 hours since OK26. Should StatsTid hold those saved days as a balance of its own — shown to the employee, taken in Skema like other paid absence and checked against the cap — or record only that the hours left the overtime account as saved days, with the saved-days balance kept outside StatsTid by HR?*

- **(i) A saved-days balance in StatsTid (recommended)**
- **(ii) Record the conversion only; HR keeps the saved days outside StatsTid**

*Proposed as not offered:* **(iii) no election: the deadline always pays.**
- The employee's right to choose saved days instead of cash is in the agreement (022-26 remark to § 10: *"medmindre den ansatte vælger at få den tilgodehavende afspadsering konverteret til opsparingsdage"*; `docs/references/agreement-cells-reverification-research.md:667`).
- The verifier recommends modelling both employee elections, each audited (`:690`).
- A product that pays hours the employee chose to save computes a wrong payroll result. That breaks domain correctness, so it is not a valid path.

**Why it matters.**
- **Today an ordered hour is paid automatically, in the month it is worked.** Under ADR-043 a decided hour leaves flex and waits in an overtime account, separate from flex (D8, `ADR-043:147-149`). It stays there until the employer gives it as time off or pays it.
- **The account has a clock.** Time off must be given within 3 months of the end of the norm period in which the hours were worked. After that the hours are paid at the next pay date, unless the employee has agreed to postpone (up to a year) or chooses saved days.
- **A worked example**, continuing ADR-043's own (`ADR-043:43-53`): a release the manager ordered, Tuesday 17 June 2025, 17:00–23:00.
  - **Earned:** 6 hours at +50 %. That is 9 hours off, or 6 hours of pay plus 50 %; the employer chooses (V10).
  - **The employer's choice is time off,** the default. The 9 hours wait in the account, with a deadline of 30 September 2025: 3 months after June ends.
  - **If the employee takes 9 hours off in August,** the account is empty, and those days off leave flex unchanged.
  - **If nothing is taken by 30 September,** the 6 hours plus 50 % go on the next pay date's payslip.
  - **If the employee instead chooses saved days at the deadline,** the 9 hours become saved days. That is this question: does StatsTid hold them, or HR?
- **It closes the compensation half of SEC-056.** Today a leader can pay out their own overtime (`docs/operations/security-finding-register.md:216`). Under the account, compensation only ever draws on decided hours, and never by a person on their own hours (P2).
- **Who notices:** employees with ordered hours (their account and their payslip), leaders (granting time off, the deadline list), HR (postponements, saved days) and payroll.

**What StatsTid does today** (verified at `26b70a08`).
- **The account exists in name only.**
  - `overtime_balances` is one row per employee and year: `accumulated`, `paid_out`, `afspadsering_used` and a compensation model (`docker/postgres/init.sql:1992-2008`). It is unique on (employee, year), with no agreement in the key (`:2002`).
  - **Nothing ever feeds `accumulated`.** `AdjustAccumulatedAsync` (`src/Infrastructure/StatsTid.Infrastructure/OvertimeBalanceRepository.cs:50`) has no caller in `src/` or `tools/`, and no seed writes the table. So every balance is absent or zero.
- **Overtime is paid automatically in the month worked.** The overtime rule emits `OVERTIME_50`, `OVERTIME_100` and `MERARBEJDE` lines (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/OvertimeRule.cs:55`, `:75`, `:86`).
  - The time-off/pay split types (`OVERTIME_50_PAYOUT`, `OVERTIME_50_AFSPADSERING`, …; `src/SharedKernel/StatsTid.SharedKernel/Models/OvertimeResult.cs:17-22`) exist, and so do their wage-type rows (`init.sql:2044-2061`). But no rule emits them.
- **The compensate route draws on that unfed balance** (`src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs:512-602`, `LeaderOrAbove`).
  - It checks org scope (`:524`), then the remaining hours, outside the transaction (`:535-541`).
  - It adjusts `paid_out` or `afspadsering_used` (`:567-576`), and emits `OvertimeCompensationApplied` with `ConvertedHours = Hours`: no +50 % conversion (`:549-560`).
  - It writes no audit row (Pattern C, `:543`).
  - It **discards the guarded update's result** (`:569-576`), so under a race the event would report a compensation the row refused. That is unreachable today, because no balance ever holds hours; it is a further reason the route retires rather than being patched.
  - It has no approved movement behind it and no self check. T9 adds a self check (TASK-14705, wave 2a, not on master at `26b70a08`).
- **The employee chooses time off or pay, against the sources** (QUAL-221, `docs/operations/quality-finding-register.md:445`).
  - `PUT /api/overtime/{employeeId}/compensation-choice` (`OvertimeEndpoints.cs:670-751`) lets employees set their own year's model where `EmployeeCompensationChoice` is true (`:707-708`).
  - That is HK and PROSA, both in the seeds (`init.sql:1468`, `:1474`, `:1480`, `:1486`) and in the compiled configs (`src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs:67`, `:98`, `:152`, `:183`).
  - The frontend hook `useCompensationChoice` (`frontend/src/hooks/useCompensationChoice.ts:28`) is imported by no page.
- **The balance is served, not shown.** The balance summary returns an overtime block (`src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs:227`, `:461-467`). No frontend component reads it; only `frontend/src/hooks/useBalanceSummary.ts` types it.
- **There is no absence type for time off in lieu.** Neither Skema's catalogue (`SkemaEndpoints.cs:34-38`) nor `AbsenceTypes` (`src/SharedKernel/StatsTid.SharedKernel/Models/AbsenceEntry.cs:13-25`) has one, though SYSTEM_TARGET §M says taking time off "creates an absence entry" (`SYSTEM_TARGET.md:410`).
- **No deadline exists.** No expiry field exists anywhere. HRP-021 records the 3-month conversion as a hand-off with "no code counterpart". It is NOT READY because "who arranges afspadsering" was unknown (`docs/operations/hr-follow-up-process-register.md:131`).
- **No saved days exist.** There is no saved-days (*opsparingsdage*) concept in `src/` or `init.sql`, and SYSTEM_TARGET §M names none (`SYSTEM_TARGET.md:395-421`).

**What the sources say.**
- **The employer chooses time off, pay, or a mix** (V10, PAV 19.2.7.1, `docs/references/overtime-flex-model-research.md:40`; C7, `agreement-cells-reverification-research.md:667`, `:675-678`).
  - Time off is the primary path. The employer may pay early *"hvis ansættelsesmyndigheden vurderer, at afspadsering ikke vil være mulig eller hensigtsmæssig"*: if it judges time off impossible or inappropriate (`:667`).
  - Confidence is HIGH that the employee does not choose (`:692`).
- **§ 8 overtime: the clock** (§ 10 stk. 1 and its remark; V10 `:40`; C7 `:667`).
  - *"Afspadsering skal gives senest 3 måneder efter opgørelsesperiodens udløb"*: time off within 3 months of the norm period's end.
  - It *"kan efter aftale med den ansatte udskydes i op til 1 år"*: postponable by up to 1 year, by agreement with the employee.
  - After the deadline, *"skal der ske betaling ved den følgende lønudbetaling, medmindre den ansatte vælger … opsparingsdage"*: payment at the next pay date, unless the employee chooses saved days.
- **The employee's only two choices** are agreeing to postpone, and saved days at the deadline. The verifier recommends `CompensationChooser = EMPLOYER` plus *"the two explicit employee elections, each audited"* (`:690`; the table `:682-688`).
- **Saved days: the cap.**
  - OK26 changed only § 11 stk. 2, to a floor of 111 hours: no maximum below 111 h may be set (`:239`; `overtime-flex-model-research.md:196`; 022-26's introduction, `docs/references/sick-day-credit-and-agreement-texts-research.md:744`). PAV still says 74 h, the pre-OK26 floor (`sick-day…research.md:393`).
  - *Not in the verified dossiers:* who sets the maximum, how saved days are taken, whether they lapse, and what happens at leaving. A research cell would be needed before building (i).
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
  - An AC deadline is not stated in the dossiers.
- **The *opsparing af frihed* election** existed only from 1 May 2025 to 31 March 2026. 024-26 abolished it, and no new time can be saved from 1 April 2026 (`agreement-cells…:51-55`, `:232-238`; ADR-043 D8, `:144-145`).

**The options** (for the proposed question).
- **(i) A saved-days balance in StatsTid.** At the deadline the employee's election moves the time off owed into a saved-days balance. The employee sees it and takes saved days in Skema, as a paid-absence type read by the paid-absence credit basis (R-6). A booking is checked against the balance, and the balance against the cap.
  - The cap is a local parameter (D-8's list) with a dated central floor: 74 h before OK26 and 111 h from 1 April 2026. That is an OK-version transition StatsTid is built to hold.
  - *Gains:*
    - one place for all of an employee's time balances;
    - no balance hidden outside the system (the standing rule: make hidden state visible);
    - the cap is enforced;
    - it matches the verifier's "each election audited" (`:690`).
  - *Gives up:*
    - the most to build: a third balance on screen, a Skema type, a cap parameter;
    - **a research cell on § 11** (who sets the cap, taking, any lapse, leaving) before it is built;
    - a SYSTEM_TARGET §M addition, owed to the owner.
- **(ii) Record the conversion only.** The election is recorded as a movement out of the overtime account, so StatsTid never pays those hours. HR holds the saved days outside StatsTid, and an employee taking a saved day registers it as paid leave (`LEAVE_WITH_PAY`), with no balance check.
  - *Gains:* little to build. The overtime account stays correct and no hour is paid that the employee saved.
  - *Gives up:*
    - a balance that lives outside the system: nothing checks a saved day against it;
    - the cap is HR's to police;
    - an HR hand-off, with an HRP row;
    - the trail is split between StatsTid and HR.
- **(iii) No election.** Not offered (above).

**The lean: (i).**
- **It follows the verifier's model:** the employer chooses, and the employee's two elections are each audited (`agreement-cells…:690`).
- **It keeps every time balance the employee owns in one audited place.** The audit trail is not split, and the cap is checked rather than trusted.
- **Saved days are days off StatsTid must register and credit anyway** (PAV 19.3.1.6, `sick-day…research.md:531`). Holding the balance beside the registrations is the coherent shape.
- **(ii) is valid and cheaper.** It gives up the balance check and splits the trail. It is offered as a product choice.
- *A build-sequencing note, not part of the question:* under (i), the conversion movement lands with the account in stage 2. The saved-days balance may follow after its research cell. Until it lands, (ii)'s recording is the interim, named in the sprint log. That is the S148 plan's call.

**Proposed as recorded proposals** (told to the owner in the prose, not asked; the adjudicator confirms or converts each).
- **D-5.P1, the account is a movement ledger: F-3 (b), told.**
  - **Every movement is dated and audited,** and frozen into the manifest of the month it lands in. The balance and each earning's deadline are derived by one pure function (ADR-002), so replay re-proves them. A movement never edits an exported month (ADR-034; D-4.P2).
  - **The movements:**
    - **earned:** the norm period worked, the hours, the rate, and the kind (§ 8 overtime; § 14 weekend compensation; AC merarbejde in stage 3);
    - **taken as time off;**
    - **paid** (by decision, or by the deadline's default);
    - **postponed;**
    - **converted to saved days.**
  - **Not (a) or (c),** for the reasons above.
  - **Where the movements are stored** is S148's refinement's call. The constraint is that Payroll writes only its own tables (ADR-016 D10's read-only contract on Backend tables).
- **D-5.P2, compensation only behind an approved movement (SEC-056), on ruling 1's terms.**
  - Every taken, paid, postponed or converted movement draws on decided earned movements, never on a bare balance.
  - **Every decision about another person's account** (granting time off, paying early, recording a postponement) is made by whoever holds that employee's month-approval authority (ruling 1 (d)).
  - It is **never on the decider's own hours** (1 (b): 403).
  - **A request that awaits a decision,** such as the employee's time-off booking, is decided by someone else (1 (c)).
  - The employee's two elections are the employee's own acts, recorded under their identity. They are not compensation decisions, so 1 (b) does not bar them.
  - **What retires:** today's compensate route (`OvertimeEndpoints.cs:512-602`) and the compensation-choice routes (`:606-751`), with the yearly row.
- **D-5.P3, the employer chooses, and time off comes first** (V10, C7; QUAL-221).
  - The default form is time off at +50 %: 6 decided hours become 9 hours off.
  - The decider may choose pay instead when time off is judged impossible or inappropriate (§ 10 remark), recorded with a reason.
  - `EmployeeCompensationChoice` and the employee's choice route retire.
  - Payroll gets hours and wage types, never kroner (the payroll boundary). The time-off/pay wage-type rows survive (`ADR-043:191`); the 100 % rows retire (D4).
- **D-5.P4, the clocks: facts, not questions** (D-4.2 item 7).
  - **§ 8 overtime (HK, PROSA):** time off is due within 3 months of the end of the norm period in which the hours were worked. If it is not given, the hours are paid at the next pay date, unless postponed by agreement or converted to saved days.
  - **A late decision** (D-4.P2) dates its earned movement in the decision month, but its clock still runs from the worked period. If that clock has already run out, the movement defaults to pay at once, or to an agreed deferral.
  - **§ 14 weekend compensation:** time off by the end of the following norm period, else cash at the next pay date. Time off may come before the weekend, and § 14 stk. 4 (sick at the start of planned time off) applies.
  - **AC merarbejde (stage 3):** time off where possible, and postponable up to a year by agreement. The AC deadline is a research item for stage 3.
  - **The part-timer band (D-10, default (ii))** earns at +50 % from the part-timer's own norm, like a full-timer's overtime.
- **D-5.P5, taking time off is a Skema absence.**
  - A new "time off in lieu" (*afspadsering*) type in the credit class (TASK-14708's classification), credited by the paid-absence credit basis (ruling 4; R-6). A whole day off leaves flex unchanged.
  - It draws on the earning with the earliest deadline first, and cannot exceed the time off owed.
  - It is approved with the month, under OQ-1's two signatures.
  - The employer's right to place time off with notice (AC: 14 days, `agreement-cells…:673`) is a build detail.
- **D-5.P6, a postponement is a record both parties sign.** It is agreed between the decider and the employee (§ 10 stk. 1, *"efter aftale med den ansatte"*), dated and audited, and moves one earning's deadline by up to a year.
- **D-5.P7, the deadline is a visible hand-off.**
  - **HRP-021 becomes decision-ready** (`hr-follow-up-process-register.md:131`). The employer arranges time off (V10, C7), so the accountable party is the employee's month approver. This is the HRP row ADR-043 promises for the 3-month deadline (`ADR-043:217-218`).
  - **Leaders get a "time off due soon" list.**
  - **The employee sees the account:** each earning with its deadline. Today's served-but-unshown block (`BalanceEndpoints.cs:461-467`) gets a screen.
  - **The deadline's default (pay) is computed in the month's calculation** and shown on the payslip and in the account, never silently.
- **D-5.P8, the dated *opsparing af frihed* election is not modelled.** Its window closed on 31 March 2026, and after the reseed no balance exists.
- **D-5.P9, a leaver's untaken time off is paid in the leaving month.** This is a labelled default: time off cannot be given after the employment ends.
  - It is **not in the verified dossiers**, so it is an item for the domain expert.
  - It sits beside R-4's flex settlement, in HRP-011's leaver lifecycle.

**What it builds on** (the owner's answers, and how they narrow it).
- **D-1 = (b), stages:** the account is **stage 2**, with the marking workflow and the supplements. Before stage 2 no hour is overtime, so nothing is earned. AC's earnings arrive with stage 3.
- **D-4 (being asked):** the account receives decided hours under either offered answer (above). D-4.P2's decision-month movement and D-4.2 item 7's clock are folded into P4. D-4.P4's ruling 1 shapes P2.
- **D-6 = (a):** the cap on saved days under (i) is a dated local value, frozen into the manifest.
- **D-8's R-6 (ruling 4):** time off in lieu, and saved days under (i), are paid-absence days read by the paid-absence credit basis.
- **D-9 = (a):** AC claims are decided claim by claim. An accepted claim's hours earn into the same ledger in stage 3.
- **D-10 = F-7 (c), default (ii):** a part-timer's ordered hours earn +50 % from their own norm.
- **D-11 = (b):** the regime decides whether an hour earns at all. Exempt HK staff earn no § 8 overtime but may earn § 14 compensation on ordered or rostered weekend work (`sick-day…research.md:870`).
- **Not proposed here:** an opening overtime balance at go-live. It is the same design question as R-5's opening flex balance (ruling 3). Its sprint is S148's refinement's, and it is noted there.

**What the owner should know** (proposed prose points).
- **Decided hours wait in an overtime account,** from stage 2. The employer chooses: time off is the default (9 hours off for 6 ordered hours), and pay is used when time off is not possible.
- **Every earning has a clock: 3 months after the month it was worked.** Untaken time off is then paid on the next payslip automatically, unless the employee agreed to postpone it (up to a year) or chose saved days. Weekend compensation (§ 14) has a shorter clock: the end of the following month.
- **No one can grant time off or pay out on their own hours,** and nothing is ever paid from hours nobody decided. SEC-056's compensation half closes.
- **The employee's "choose pay or time off" setting goes away.** It contradicted the agreements (QUAL-221). The employee's two real choices, postponing and saving, are recorded instead.
- **Taking time off in lieu becomes a Skema absence.** A whole day off leaves flex unchanged.
- **Overtime pay lines change.** They appear when the employer pays, or at the deadline, not automatically in the month worked. Demo payslips move in stage 2.
- **Leaders get a "time off due" list** (HRP-021).

**What the S148 build gets from each answer** (stage 2; reseed).
- **Under either answer (P1–P9):**
  - the movement ledger and its derived balance and deadlines, frozen into each month's manifest;
  - earnings from decided hours (§ 8 and § 14);
  - the time-off absence type, in the credit class;
  - the decider's form choice, with time off as the default;
  - postponement records signed by both parties;
  - the deadline's default to pay;
  - the leaders' due list and the employee's account screen (`ux`);
  - SEC-056's guards on ruling 1's terms;
  - retirements: the yearly `overtime_balances` row, the compensate and compensation-choice routes, the unused `useCompensationChoice` hook, `EmployeeCompensationChoice`, and the 100 % wage-type rows;
  - HRP-021 designed.
- **(i):**
  - the saved-days balance, its cap as a local parameter (D-8's list) with a dated central floor (74 h before OK26, 111 h from 1 April 2026), a Skema "saved day" type in the credit class, and the employee's election screen;
  - **a research cell on § 11 first;**
  - SYSTEM_TARGET §M gains saved days (owed to the owner).
- **(ii):** the conversion movement only. The ADR names the limitation: saved days are held outside StatsTid, and a saved day taken is registered as paid leave. HR gets a hand-off and an HRP row.

**The ADR-043 text it rewrites.**
- F-3 (`:271-277`) becomes a decision: (b), told (P1), with the saved-days answer.
- D8 (`:141-149`): the account's shape, the clocks (P4) and the employee's two elections. "Its shape is F-3" (`:149`) is closed.
- D5 (`:110-117`): an accepted AC claim earns into the same ledger (stage 3).
- "What it retires" (`:169-181`) gains the yearly `overtime_balances` row (`init.sql:1992-2008`) and the compensate and compensation-choice routes. The employee's compensation-choice row (`:181`) is already listed.
- Consequences, "New hand-offs" (`:217-218`): the 3-month deadline row is designed (HRP-021).
- **Outside ADR-043:**
  - SYSTEM_TARGET §M (`SYSTEM_TARGET.md:395-421`): "Employee choice" (`:403`) and "Overtime Balance" (`:407-412`), and under (i) saved days. All of it is owed to the owner;
  - SEC-056's row, the compensation half;
  - QUAL-221's route;
  - HRP-021.
- The recorded proposals become ADR text when this question is answered.

**Waits on.** D-4's answer, for the asking order only (the adjudicator's asking order); the question itself does not depend on it. Building (i)'s saved-days balance waits on a research cell on § 11. Building the rest is stage 2.

---
*Revision history.*
- Draft 1 (2026-10-29): first brief, at `26b70a08`, drafted before D-4's answer on the adjudicator's ruling that it may be.
  - **The reframing, for adjudication:** F-3's storage shape is proposed as told (P1), because only (b) can apply the § 10 clock. The owner question proposed is where saved days live, (i) or (ii), with (iii) "no election" proposed as not offered.
  - **The § 10 clock** is recorded as a fact (P4; D-4.2 item 7).
  - **SEC-056's compensation half** is put on ruling 1's terms (P2).
  - **Nine recorded proposals** are proposed.
  - **Nothing depends on D-4's (c) or (a).**
