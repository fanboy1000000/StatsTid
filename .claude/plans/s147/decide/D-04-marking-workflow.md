# D-4 — F-2: how does StatsTid record that a leader ordered extra hours, or accepted afterwards that they were necessary?

*Brief, draft 2, 2026-10-29: adjudicated (`.claude/reviews/S147-D-batch2-adjudication.md` § D-4). It is being asked now, and the Orchestrator records the answer. Drafted by `planner` (Opus 5.5). Code facts were verified at `61d6a98a` and re-checked at `26b70a08`; no file under `src/`, `docker/`, `frontend/` or `tools/` changed in between. It builds on seven answers given 2026-10-29: D-1 = (b), D-2 = (a), D-3 = (c), D-6 = (a), D-9 = (a), D-10 = F-7 (c) default (ii), D-11 = (b).*

**The question in one sentence.** Under the new model an hour is paid as overtime only when a leader ordered it, or accepted afterwards that it was necessary (ADR-043 D3). An evening or weekend hour earns its supplement only when its *placement* was ordered or rostered (D7; § 14, RR-12). How should StatsTid record those decisions?

**The question as it will be asked** (settled by the adjudicator, D-4.1; verbatim).

> *Under the new model an hour above the month's norm becomes overtime, or counts toward an extra-work claim, only when a leader ordered it or accepted afterwards that it was necessary — and an evening or weekend hour earns its supplement only when its placement was ordered or rostered. StatsTid must therefore record those decisions hour by hour; a budget of "up to N hours" cannot say which hours, so it is not offered. How should the record be made: should a leader be able to record an order, or a roster span, beforehand — naming the date and clock span — with work nobody ordered in advance marked by the employee and accepted or rejected by the leader afterwards; or should every decision be made afterwards, the employee marking the hours and the leader deciding them when approving the month, with no separate record of an order given in advance?*

- **(c) Both: an order beforehand and an acceptance afterwards, each naming its hours (recommended)**
- **(a) Afterwards only: the employee marks, the leader decides at month approval**

*Not offered* (D-4.1): **(b) a budget beforehand.**
- **It names no hours.** So it cannot carry the placement fact that § 13's +25 % and § 14's +50 % both require. Both remarks say the supplement is due for work done *"efter ordre eller godkendt tjenestefordeling"* (`docs/references/sick-day-credit-and-agreement-texts-research.md:824`, `:854`).
- **It can pay hours nobody ordered.** Under a monthly norm, "the first N hours above the norm" can be unordered flex hours paid as overtime. That is the automatic pattern ADR-043 retires. It breaks domain correctness, so it is not a valid path.
- **The budget idea survives** as `MaxOvertimeHoursPerPeriod`, a warning over decided hours (P6). The prose says so.

**Why it matters.**
- **Stage 2 turns on this record.** D-1 = (b) put the work in stages. In stage 1 every hour above the norm goes to flex, hour for hour. From stage 2, three things read this record:
  - **the +50 % overtime:** an hour leaves flex and earns +50 % (HK, PROSA) only if a recorded decision covers it;
  - **the +25 % supplement** for evenings, nights and weekends (§ 13);
  - **§ 14's weekend +50 %,** which also keeps the hours out of the month's count.
  The supplement and § 14 are due only when the work's placement was "ordered or rostered".
- **A record that cannot say which hours it covers fails both ways.** It either pays hours nobody ordered, or fails to pay the evening supplement on hours that were ordered.
- **A worked example**, taken from ADR-043's own (`ADR-043:43-53`): a release the manager ordered, Tuesday 17:00–23:00.
  - **Under (c):** the manager records the order beforehand, naming Tuesday 17:00–23:00. The employee's Skema intervals show 17:00–23:00. The order covers them, so 6 hours leave flex at +50 %, and the evening placement earns +25 %.
  - **Under (a):** the employee marks 17:00–23:00 as ordered in Skema. The manager accepts the mark while approving the month. The pay is the same as under (c).
  - **Under (b), not offered:** the manager grants "up to 6 hours of overtime in June". At month end the first 6 hours above the norm become overtime, whichever hours they were. Nothing says the 17:00–23:00 hours were ordered, so no supplement is due.
- **Who notices:**
  - employees who work ordered evenings, weekends or long days;
  - leaders, who get a decision step in their month approval;
  - payroll.
- **It closes a security finding for good (SEC-056, High).** Today a leader can approve their own overtime and pay it out. S147's T9 blocks the self part (Q-A = (a)). The owner left "whoever raises a request cannot also decide it" to this redesign.

**What StatsTid does today** (verified).
- **The record is an hours budget that names no hours.** `overtime_pre_approvals` holds an employee, a `period_start` and `period_end`, a `max_hours` and a status (`docker/postgres/init.sql:2027-2038`).
  - ADR-024 D7's columns were added in S40 (`authorization_mode`, `necessity_reason`, `acknowledged_at/by`; `:2551-2555`), with an audit table (`:2557-2568`). They are dormant (`docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md:554`).
  - **There is no creator column** (`docs/generated/db-schema.md:927-944`). The creating leader appears only in the event's actor fields and its audit row (`src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs:196-197`, `:212-221`), never on the row itself.
- **The routes** (all `LeaderOrAbove`):
  - **create** (`OvertimeEndpoints.cs:147-241`) checks org scope only (`:162-164`), so the employee may be the leader;
  - **approve** (`:359-436`) checks scope (`:379-381`) and PENDING status (`:383-384`), and stamps the caller as approver (`:398`, `:403`);
  - **reject** (`:439-509`) has the same shape;
  - **compensate** (`:512-603`) moves hours out of the yearly `overtime_balances` row (`:567-576`) with no approved movement behind it.
- **No self guard on master yet.** SEC-056 is in `docs/operations/security-finding-register.md:216`. T9 (`.claude/plans/s147/TASK-14705.md`, S147 wave 2a) adds the guard to approve, reject and compensate. It is **not on master** at `26b70a08`; the routes still have no self check (`OvertimeEndpoints.cs:379-384`, `:524-526`).
- **Nothing in pay reads any of it.** No file under `src/Integrations/StatsTid.Integrations.Payroll` refers to a pre-approval.
  - Payroll evaluates six rules, none of which reads a pre-approval (`src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:2167-2175`).
  - The overtime rule pays every hour above the week's norm, ordered or not (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/OvertimeRule.cs:45-48`).
  - The supplement rule pays every timed hour in a window, with no ordered-or-rostered test (`SupplementRule.cs:30-31`). It pays weekend work as a supplement rate (`:68-73`), not as § 14's +50 % kept outside the hour count.
- **The governance check only warns, and nothing calls it.** It takes a `hasPreApproval` flag the caller supplies (`OvertimeEndpoints.cs:65`, `:122`) and only warns (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/OvertimeGovernanceRule.cs:106-114`). No screen calls it.
- **The "accepted afterwards" path was designed but never built.**
  - ADR-024 D7's acknowledge-necessity endpoint (`ADR-024:218`) does not exist. Searching `src/` for `acknowledge-necessity` finds nothing.
  - Its event names time-entry ids (`src/SharedKernel/StatsTid.SharedKernel/Events/OvertimeNecessityAcknowledged.cs:23`). Only its audit mapper refers to it, and nothing emits it.
- **Where leaders work** (D-4.2 item 1).
  - **Month approvals live in Teamoversigt** since S87 (`frontend/src/App.tsx:97-100`: the old approvals route redirects to `/godkend/oversigt`; the approve route is `ApprovalEndpoints.cs:199`).
  - **The pre-approval page** `OvertimePreApprovalManagement` is routed at `/godkend/overtid` (`App.tsx:104`). It lists, approves and rejects (`frontend/src/pages/admin/OvertimePreApprovalManagement.tsx:50`, `:67`, `:82`). No screen creates a pre-approval.
  - ADR-043 F-2 (`:265-266`) and ADR-024 D7 point 3 (`ADR-024:220`) still say "the approval page". That page no longer exists as a separate page.
- **A Skema hour has a time of day only in the intervals.** Project entries carry no clock times. The day's intervals are a JSON list per (employee, date), with no ids, replaced on each save (`init.sql:1973-1984`).
- **Two switches exist, and no central config sets either** (D-4.2 item 4).
  - `OvertimeRequiresPreApproval` and `MaxOvertimeHoursPerPeriod` are agreement fields (`src/SharedKernel/StatsTid.SharedKernel/Models/AgreementRuleConfig.cs:79-80`). `CentralAgreementConfigs.cs` sets neither, so both keep their defaults: false and 0.
  - They are also local-profile columns (`init.sql:867-868`).
  - Only the governance warning reads them. SYSTEM_TARGET §L describes the pre-approval switch as optional, turning a missing pre-approval into a warning (`SYSTEM_TARGET.md:386-391`), and the ceiling as a flag (`:381-384`).

**What the sources say.**
- **Ordered or necessary.** Overtime presupposes work that was *"beordret eller nødvendigt"* (V3, `docs/references/overtime-flex-model-research.md:33`). Ordered work is kept outside the flex account (V8, PAV 19.5.11, `:38`); the office-hours exception is D-7.
- **Ordered or rostered placement.** Both supplements are due for work *"der efter ordre eller godkendt tjenestefordeling udføres"*: work done by order or under an approved duty roster.
  - § 13's +25 % (remark, `docs/references/sick-day-credit-and-agreement-texts-research.md:824`);
  - § 14's weekend and holiday +50 % (remark, `:854`; RR-12).
- **What § 14 does to the hours.** The compensated hours are kept out of the § 7 working-time count (stk. 2, `:847`). § 14 applies to ordinary duty, ordered overtime or extra work, and part-timers' extra hours alike (`:856`). It also covers staff exempt from overtime (`:870`), and § 13 likewise (`:841`).
- **Local practice.** SDU's 2026 agreement says ordered over- or extra work *"håndteres uden for flekstidsaftalen"* (is handled outside flex) and must be ordered by the manager (`docs/references/agreement-cells-reverification-research.md:629`).
- **AC.** Merarbejde (extra work) is a claim the employer decides (V9, `overtime-flex-model-research.md:39`). That is ADR-043 D5, stage 3.

**The options** (ADR-043 F-2, `:254-269`; working copy `../ADR-043-working.md`, F-2 in the verbatim body).
- **(c) Both: an order beforehand and an acceptance afterwards, each naming its hours.** A leader can record an order or a roster span before the work. Work nobody ordered in advance is marked by the employee and accepted afterwards, as in (a).
  - *Gains:*
    - It follows the sources' two routes exactly: *"beordret eller nødvendigt"* and *"efter ordre eller godkendt tjenestefordeling"*.
    - The order beforehand is where a roster lives.
    - It keeps ADR-024 D7's requirement that an approval given beforehand can be told apart from one given afterwards (`ADR-024:222`).
  - *Gives up:*
    - the most to build;
    - a matching rule between an order's planned span and the hours actually worked (D-4.2 item 5):
      - the **ordered hours are the overlap** of the order's span with the worked intervals;
      - **work beyond the span is flex**, unless it is marked afterwards;
      - **work short of the span is ordered only as worked**;
      - a **recurring roster span** is a recurrence on the order record, which is a build item.
- **(a) Per hour, afterwards.** The employee marks a clock span as ordered or necessary, and as an ordered placement where that applies. The leader accepts or rejects each mark before approving the month.
  - *Gains:* one workflow, built into the month approval leaders already do. The mark names hours, so it can carry the placement fact.
  - *Gives up:*
    - a step for the employee on every episode;
    - an order given beforehand leaves no record of its own; it shows only as the employee's label, confirmed afterwards;
    - a standing roster (for example, every Tuesday evening) must be re-marked each time.
- **(b) A budget beforehand.** Not offered (above).

**The lean: (c), confirmed by the adjudicator** (D-4.3; ADR `:265`). The adjudicator's reasons:
- **It is the sources' own shape:** *beordret eller nødvendigt*, and *efter ordre eller godkendt tjenestefordeling*.
- **The order record is the natural home for later kinds of time.** A roster lives there, and so, later, will D-3's assigned on-call duty and ordered travel. The later stage then reuses this record instead of adding a second order record.
- **It keeps ADR-024 D7's prior/post-hoc distinction** (`ADR-024:222`).
- **(a) is valid and cheaper.** It gives up the order's own record and re-marks every roster. It is offered as a product choice.
- *Sequencing is the S148 plan's call:* the acceptance half alone makes stage 2 correct, so S148 may land it first. Under (c) the order half may land after the acceptance half.

**Recorded proposals** — *adjudicated, told to the owner, not asked* (D-4.4). They are listed in `../ADR-043-working.md`, "Provisional items and recorded proposals".
- **D-4.P1, the cut-off — accepted, with the decider generalised** (D-4.2 item 6).
  - A mark is decided before the month is approved. The month cannot be approved while a mark on it is undecided.
  - **The decider** is whoever holds the month's approval authority at the time:
    - the designated approver;
    - a stand-in (vikar) under Q-B = (c);
    - HR, for a leaver's final month under OQ-2.
  - **Four-eyes holds.** The decider is never the mark's creator and never the employee.
- **D-4.P2, a decision after the month is approved — accepted.** It becomes a dated movement in the month the decision is made, never a reopening. ADR-013 and ADR-034 hold.
  - **The mechanism:** the hours leave flex as a dated movement in the decision month, and the overtime account (D-5) is credited. The worked month is never reopened.
  - **Why not a correction of the worked month:** a correction changes that month's flex closing. Every later month's opening then goes stale, and QUAL-229's guard (S147 TASK-14700) refuses each later month until HR recalculates them one by one (HRP-003). The movement touches no exported month.
  - It covers an acceptance after approval or after export, and every accepted AC claim (D5).
  - **A fact handed to D-5** (D-4.2 item 7): the 3-month time-off clock runs from the end of the norm period in which the hours were worked (V10, `overtime-flex-model-research.md:40`), not from the decision. If the clock has already run out, the movement defaults to pay, or to an agreed deferral. D-5 records this; it does not ask it again.
- **D-4.P3, the placement fact is its own field — accepted.**
  - A mark or order records "placement ordered or rostered" separately from "surplus hours ordered or necessary", as D7 already requires (`:131-134`).
  - The placement fact gates **both** § 13's +25 % and § 14's +50 % (RR-12).
  - It is needed for staff exempt from overtime too (D-11's regime), because § 13 and § 14 cover them (`sick-day…research.md:841`, `:870`).
- **D-4.P4, four-eyes (SEC-056; Q-A = (a)) — accepted, on four conditions** (the adjudicator's ruling 1).
  - **Every record carries a creator column** (reseed). Compensation happens only behind an approved movement (D-5).
  - **T9's rule (S147 wave 2a) is carried into the new record.** *Tense (D-4.2 item 2):* TASK-14705 is not on master at `26b70a08`; the Orchestrator sets the tense at asking time.
  - **Why a leader's own order needs no second leader.** The sources know one authority for ordering work: the manager (V3; SDU 2.4.2). No text asks a second leader to approve a manager's order. Today's create-then-approve was never a second-level approval either, since both steps are `LeaderOrAbove`. Four-eyes protects against a person deciding about themselves, and that risk exists only when a leader orders their own hours.
  - **The four conditions:**
    - **(a)** an order is a decision at creation, and its creator is its decider;
    - **(b)** a decision is never on the creator's own hours. T9's rule is carried to every record: an order, a mark decision, a compensation, a cap exception. Each refuses with 403;
    - **(c)** creator-not-approver applies to every record that awaits a decision. A mark raised by the employee, or by a leader on the employee's behalf, is decided by someone else;
    - **(d)** the authority to order or decide is the authority to approve that employee's month: the designated approver, a stand-in under Q-B = (c), or HR for a leaver's final month under OQ-2. It is not bare org scope, which is all today's create route checks (`OvertimeEndpoints.cs:162-164`).
  - **What stays untouched:** OQ-1's strict four-eyes is on the month's two signatures.
  - **The register:** SEC-056's row is refined at close. An order decides itself, is never on the creator's own hours, and is made by the month's approver.
  - **Given up:** a stricter second-leader approval. A later local parameter is possible if an institution requires one; it is recorded, not promised.
  - **In the owner prose, one sentence:** a leader's own order needs no second leader, but no one can ever order or decide their own hours, or decide a mark they raised themselves.
- **D-4.P5, room for typed time — accepted.** The record carries an activity type, `NORMAL` until D-3's later stage adds on-call, call-in and travel.
- **D-4.P6, the switch and the governance check retire — accepted, with D-4.2 item 4.**
  - **What retires:** the central `OvertimeRequiresPreApproval` field (`AgreementRuleConfig.cs:80`), the local column (`init.sql:868`), the governance warning, and the governance route nobody calls. Under D3 every overtime hour needs a decision, so an optional switch has no meaning.
  - **What that resolves:** ADR-024's "Bug #4 seed flip" (`ADR-024:228`, `:254`) resolves by retirement.
  - **SYSTEM_TARGET:** §L's pre-approval text (`SYSTEM_TARGET.md:386-391`) is rewritten at acceptance.
  - **`MaxOvertimeHoursPerPeriod`** becomes an optional parameter in D-8's list (R-2), with no default: a warning over decided hours (`SYSTEM_TARGET.md:381-384`). Neither field is set by any central config today.

**What it builds on** (the owner's answers, and how they narrow this question).
- **D-1 = (b), stages:** built in **stage 2**, with the overtime account (D-5) and the supplements. Before stage 2 no mark is read. The § 14 weekend +50 % waits for stage 2 too, the same interim class as the supplement, so the stage-1 sprint log should name it.
- **D-2 = (a), Skema's intervals:**
  - **A mark names a clock span** (date, start, end), because the intervals are the only place a Skema hour has a time of day. Naming time-entry ids, as the dormant event does, cannot carry a placement.
  - **Marks and intervals must stay consistent.** The intervals have no ids and are replaced on each save (`init.sql:1973-1984`). A save that removes work under a decided mark must therefore be refused, or must reopen the mark.
  - **Marks reach payroll the same way.** The supplement half (stage 2) reads the intervals, and the decided marks must reach the pure engine the same way (ADR-043 D3, `:93-94`).
- **D-3 = (c), typed time later:** the record leaves room for a type (P5).
- **D-9 = (a), AC claims per claim:** an AC claim (stage 3) reuses the record's shape: hours, period, creator, decider. P2's decision-month movement serves it. Who "the employer" is when it decides a claim (D5: the employee claims, the leader attests, the employer decides) is stage 3's design.
- **D-10 = F-7 (c), default (ii):** the part-timer band reads which hours were ordered. Under (ii), an ordered hour above a part-timer's own norm is overtime, like a full-timer's.
- **D-11 = (b), the HR-recorded regime:** a mark is regime-neutral. It records "ordered or necessary" and the placement, and the regime then routes the hour to one of three outcomes:
  - +50 % overtime;
  - a merarbejde claim (PROSA pay groups 3–4, and AC per D-11.4);
  - nothing, for HK staff above the pay ceiling, who still earn § 13 and § 14 on ordered placements.
- **Q-A = (a) and SEC-056:** T9 is S147's (wave 2a). Creator-not-approver was left to this redesign (P4).

**What the owner should know** (D-4.5).
- **Leaders get a decision step inside month approval.** A new HRP row records the hand-off.
- **A weekend or evening hour the employee chose under flex earns nothing extra.** It is flex, hour for hour. Only ordered or rostered placements earn § 13's +25 % or § 14's +50 % (RR-12). Today every timed weekend hour earns a weekend rate (`SupplementRule.cs:68-73`), so demo payslips move in stage 2.
- **The "pre-approval required" switch goes away,** because every overtime hour now needs a decision. **The hours ceiling becomes a warning over decided hours.** It is the only part of the budget idea that survives (above).
- **A decision made after a month is approved lands in the month it is made.** No month is reopened.
- **No one can ever decide their own hours, or a mark they raised themselves. A leader's own order needs no second leader** (ruling 1).
- **Under (c), the order half may land after the acceptance half.**

**What the S148 build gets from each answer** (all in stage 2; reseed).
- **(c):**
  - **The table changes shape.** `overtime_pre_approvals` changes from a budget into records that name hours. Each record holds:
    - a date and a clock span, from D-2's intervals;
    - a kind: ordered beforehand, or necessary and accepted afterwards;
    - a placement-only flag, separate from surplus;
    - an activity type, `NORMAL` for now;
    - a creator, a decider and a status.
  - **The acceptance-afterwards half:** ADR-024 D7's endpoint, built at last but naming spans instead of entries. Skema gains a way to mark a span (`ux`), and Teamoversigt shows the marks in month approval.
  - **The order-beforehand half:** a leader records an order or a roster span. The matching rule against worked intervals follows item 5's cases (overlap ordered; beyond the span flex unless marked; short of the span ordered only as worked). A roster's recurrence lives on the order record.
  - **Guards and the cut-off:**
    - T9's rule is carried into the record;
    - ruling 1's four conditions apply;
    - the month approval refuses while a mark is undecided;
    - decisions after approval become dated movements (P2).
  - **Payroll reads the decided marks** as a frozen input. Its read-only contract on Backend tables grows again (ADR-016 D10, `ADR-016:179`; three tables after D-2), and the replay digest and manifest record the marks.
  - **What retires:** `OvertimeRequiresPreApproval` and the advisory check. `MaxOvertimeHoursPerPeriod` moves to D-8's parameter list as an optional warning.
  - **Documents:** an HRP row; SYSTEM_TARGET §L's pre-approval text, owed to the owner.
  - *A build-input gap, not part of the question* (declined by the adjudicator as such): whether PROSA pay groups 3–4's merarbejde is a D5-style claim or a stage-2 mark.
- **(a):** the same, without the order-beforehand half. Rosters and, later, D-3's on-call assignments are marked afterwards each time.
- **(b):** none (not offered).

**The ADR-043 text it rewrites.**
- F-2 (`:254-269`) becomes a decision. **The rewrite says Teamoversigt,** not "the approval page" (D-4.2 item 1; `:265-266`).
- D3's "whether it names hours or a budget is F-2" (`:93-94`), and its ADR-024 D7 bullet (`:95-96`): from dormant design to the payroll path, with the event naming spans.
- D5's sub-question sentence (`:115-117`): the decision-month movement (P2).
- D7's placement sentence (`:130-134`) names the record. Its § 14 bullet (`:135-136`) gains "ordered or rostered" (RR-12).
- "What it retires" gains the budget shape (`init.sql:2027-2038`) and `OvertimeRequiresPreApproval`.
- Consequences, "New hand-offs" (`:217-218`): the marking row is designed.
- **Outside ADR-043:**
  - SYSTEM_TARGET §L (`SYSTEM_TARGET.md:381-391`), owed to the owner;
  - SEC-056's register row gets its route, refined per ruling 1;
  - HRP-018 (`hr-follow-up-process-register.md:151-156`);
  - ADR-016 D10's read contract;
  - ADR-024 D7, including point 3's "Approval page" (`ADR-024:220`), which becomes Teamoversigt.
- The recorded proposals become ADR text when this question is answered.

**Waits on.** Nothing for the asking: D-2 and D-3 are answered. Building it is stage 2. D-5 (F-3, the overtime account) is asked after this answer. Its brief is drafted already, and its question does not depend on which of (c) or (a) is chosen.

---
*Revision history.*
- Draft 1 (2026-10-29): first brief, after D-2 = (a) and D-3 = (c). It folds in SEC-056 (a creator column, creator-not-approver, T9's guard kept) and RR-12 (§ 14 applies to ordered or rostered work). Six recorded proposals are proposed for adjudication, one with a reading flagged (P4). (b) is proposed as not offered.
- Draft 2 (2026-10-29): the adjudication applied (`.claude/reviews/S147-D-batch2-adjudication.md` § D-4).
  - **D-4.1:** added "The question as it will be asked", verbatim, with (c) first and (b) not offered, giving its reasons and the budget idea's survival as a warning.
  - **D-4.2:** folded all seven corrections.
    1. Teamoversigt: the facts, and the ADR-043 and ADR-024 rewrites.
    2. T9's tense: "carried into the new record", with the tense set at asking time.
    3. The verified facts are kept.
    4. Neither switch is set centrally, and `MaxOvertimeHoursPerPeriod` moves to D-8's list with no default.
    5. The matching rule's cases are in (c)'s cost line and the build.
    6. P1's decider is generalised, with four-eyes.
    7. The § 10 clock is handed to D-5 as a fact.
  - **D-4.3–D-4.5:** the lean is confirmed with the adjudicator's reasons. P1–P6 are marked accepted, with P4 carrying ruling 1's four conditions. "What the owner should know" is replaced by D-4.5.
  - **Other:** the declined PROSA 3–4 point is kept as a build-input gap. The re-check sha is `26b70a08`. Two citations are widened: the Bug #4 seed flip now cites `ADR-024:228` beside `:254`, and the field citation is now `AgreementRuleConfig.cs:79-80`, covering both fields. The working-copy F-2 citation (`:307-322`, which was off) now cites the section, because the working copy's line numbers move as its lists grow.
