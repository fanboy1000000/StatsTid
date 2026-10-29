# D-4 — F-2: how does StatsTid record that a leader ordered extra hours, or accepted afterwards that they were necessary?

*Brief, draft 1, 2026-10-29: not adjudicated. The owner has not been asked. Drafted by `planner` (Opus 5.5). Code facts verified at `61d6a98a` (master HEAD when the work began); HEAD `f37d7cd3` at writing changed no file under `src/`, `docker/` or `frontend/`. It builds on seven answers given 2026-10-29: D-1 = (b), D-2 = (a), D-3 = (c), D-6 = (a), D-9 = (a), D-10 = F-7 (c) default (ii), D-11 = (b).*

**The question in one sentence.** Under the new model an hour is paid as overtime only when a leader ordered it, or accepted afterwards that it was necessary (ADR-043 D3). An evening or weekend hour earns its supplement only when its *placement* was ordered or rostered (D7; § 14, RR-12). How should StatsTid record those decisions?
- **(a)** As a mark on the specific hours, made afterwards: the employee marks them, and the leader accepts or rejects the mark.
- **(b)** As an hours budget given beforehand, which hours above the norm then use up.
- **(c)** Both: an order beforehand and an acceptance afterwards, each naming its hours.

**Why it matters.**
- **Stage 2 turns on this record.** D-1 = (b) put the work in stages. In stage 1 every hour above the norm goes to flex, hour for hour. From stage 2 three things read this record:
  - **the +50 % overtime:** an hour leaves flex and earns +50 % (HK, PROSA) only if a recorded decision covers it;
  - **the +25 % supplement** for evenings, nights and weekends (§ 13);
  - **§ 14's weekend +50 %,** which also keeps the hours out of the month's count.
  The supplement and § 14 are both due only when the work's placement was "ordered or rostered".
- **A record that cannot say which hours it covers fails both ways.** It either pays hours nobody ordered, or fails to pay the evening supplement on hours that were ordered.
- **A worked example**, from ADR-043's own (`ADR-043:43-53`): a release the manager ordered, Tuesday 17:00–23:00.
  - **Under (c):** the manager records the order beforehand, naming Tuesday 17:00–23:00. The employee's Skema intervals show 17:00–23:00. The order covers them, so 6 hours leave flex at +50 %, and the evening placement earns +25 %.
  - **Under (a):** the employee marks 17:00–23:00 as ordered in Skema, and the manager accepts the mark while approving the month. The pay is the same as under (c).
  - **Under (b):** the manager grants "up to 6 hours of overtime in June". At month end, the first 6 hours above the norm become overtime, whichever hours they were. Nothing says the 17:00–23:00 hours were ordered, so no supplement is due.
- **Who notices:**
  - employees who work ordered evenings, weekends or long days;
  - leaders, who get a decision step in their month approval;
  - payroll.
- **It closes a security finding for good (SEC-056, High).** Today a leader can approve their own overtime and pay it out. S147's T9 blocks the self part (Q-A = (a)). The owner left the rule that "whoever raises a request cannot also decide it" to this redesign.

**What StatsTid does today** (verified).
- **The record is an hours budget that names no hours.** `overtime_pre_approvals` holds an employee, a `period_start`/`period_end`, a `max_hours` and a status (`docker/postgres/init.sql:2027-2038`).
  - ADR-024 D7's columns were added in S40 (`authorization_mode`, `necessity_reason`, `acknowledged_at/by`; `:2551-2555`), with an audit table (`:2557-2568`). They are dormant (`docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md:554`).
  - **There is no creator column** (`docs/generated/db-schema.md:927-944`). The creating leader appears only in the event's actor fields and its audit row (`src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs:196-197`, `:212-221`), never on the row itself.
- **The routes** (all `LeaderOrAbove`):
  - **create** (`OvertimeEndpoints.cs:147-241`) checks org scope only (`:162-164`), so the employee may be the leader;
  - **approve** (`:359-436`) checks scope (`:379-381`) and PENDING status (`:383-384`), and stamps the caller as approver (`:398`, `:403`);
  - **reject** (`:439-509`) has the same shape;
  - **compensate** (`:512-603`) moves hours out of the yearly `overtime_balances` row (`:567-576`), with no approved movement behind it.
- **No self guard yet.** SEC-056 is in `docs/operations/security-finding-register.md:216`. T9 (`.claude/plans/s147/TASK-14705.md`, wave 2a) adds the guard to approve, reject and compensate. It is **not on master** at this HEAD.
- **Nothing in pay reads any of it.** No file under `src/Integrations/StatsTid.Integrations.Payroll` refers to a pre-approval.
  - Payroll evaluates six rules (`src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:2167-2175`).
  - The overtime rule pays every hour above the week's norm, ordered or not (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/OvertimeRule.cs:45-48`).
  - The supplement rule pays every timed hour in a window, with no ordered/rostered test (`SupplementRule.cs:30-31`). It pays weekend work as a supplement rate (`:68-73`), not as § 14's +50 % kept outside the hour count.
- **The governance check only warns, and nothing calls it.** It takes a `hasPreApproval` flag the caller supplies (`OvertimeEndpoints.cs:65`, `:122`) and only warns (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/OvertimeGovernanceRule.cs:106-114`). No screen calls it.
- **The "accepted afterwards" path was designed but never built.**
  - ADR-024 D7's acknowledge-necessity endpoint (`ADR-024:218`) does not exist. Searching `src/` for `acknowledge-necessity` finds nothing.
  - Its event names time-entry ids (`src/SharedKernel/StatsTid.SharedKernel/Events/OvertimeNecessityAcknowledged.cs:23`). Only its audit mapper refers to it, and nothing emits it.
- **The leader's page** `OvertimePreApprovalManagement` is routed at `/godkend/overtid` (`frontend/src/App.tsx:104`). It lists, approves and rejects (`frontend/src/pages/admin/OvertimePreApprovalManagement.tsx:50`, `:67`, `:82`). No screen creates a pre-approval. Month approvals live in Teamoversigt (`App.tsx:97-100`; the approve route is `ApprovalEndpoints.cs:199`).
- **A Skema hour has a time of day only in the intervals.** Project entries carry no clock times. The day's intervals are a JSON list per (employee, date), with no ids, replaced on each save (`init.sql:1973-1984`).
- **The `OvertimeRequiresPreApproval` switch exists, defaulting to false.** It is an agreement field (`src/SharedKernel/StatsTid.SharedKernel/Models/AgreementRuleConfig.cs:80`; no central config sets it) and a local-profile column (`init.sql:868`). Only the governance warning reads it. SYSTEM_TARGET §L describes it as an optional switch that turns a missing pre-approval into a warning (`SYSTEM_TARGET.md:386-391`).

**What the sources say.**
- **Ordered or necessary.** Overtime presupposes work that was *"beordret eller nødvendigt"* (V3, `docs/references/overtime-flex-model-research.md:33`). Ordered work is kept outside the flex account (V8, PAV 19.5.11, `:38`); the office-hours exception is D-7.
- **Ordered or rostered placement.** Both supplements are due for work *"der efter ordre eller godkendt tjenestefordeling udføres"*, that is, done by order or under an approved duty roster.
  - § 13's +25 % (remark, `docs/references/sick-day-credit-and-agreement-texts-research.md:824`);
  - § 14's weekend and holiday +50 % (remark, `:854`; RR-12).
- **What § 14 does to the hours.** The compensated hours are kept out of the § 7 working-time count (stk. 2, `:847`). It applies to ordinary duty, ordered overtime or extra work, and part-timers' extra hours alike (`:856`), and to staff exempt from overtime (`:870`; § 13 likewise, `:841`).
- **Local practice.** SDU's 2026 agreement: ordered over- or extra work *"håndteres uden for flekstidsaftalen"* (is handled outside flex) and must be ordered by the manager (`docs/references/agreement-cells-reverification-research.md:629`).
- **AC.** Merarbejde is a claim the employer decides (V9, `overtime-flex-model-research.md:39`). That is D5, stage 3.

**The options** (ADR-043 F-2, `:254-269`; working copy `../ADR-043-working.md:307-322`).
- **(a) Per hour, afterwards.** The employee marks a clock span as ordered or necessary, and as an ordered placement where that applies. The leader accepts or rejects each mark before approving the month.
  - *Gains:* one workflow, built into the month approval leaders already do. The mark names hours, so it can carry the placement fact.
  - *Gives up:*
    - a step for the employee on every episode;
    - an order given beforehand leaves no record of its own; it shows only as the employee's label, confirmed afterwards;
    - a standing roster (for example, every Tuesday evening) must be re-marked each time.
- **(b) A budget beforehand, which hours above the norm use up.**
  - *Gains:* the lightest to build, and closest to today's table.
  - *Gives up:*
    - **No placement fact.** Which hours used the budget is a guess, so the record cannot carry D7's and § 14's placement fact, and no supplement can be tied to hours.
    - **No dated hours,** so the 3-month time-off clock (D8) cannot be tied to hours either.
    - **It can pay unordered hours.** With a monthly norm, every hour above the norm counts the same. So "the first N hours above the norm" can be unordered flex hours paid as overtime: the automatic pattern ADR-043 retires.
- **(c) Both: an order beforehand and an acceptance afterwards, each naming its hours.** A leader can record an order or a roster span before the work. Work nobody ordered in advance is marked and accepted afterwards, as in (a).
  - *Gains:*
    - It follows the sources' two routes exactly: *"beordret eller nødvendigt"*, and *"efter ordre eller godkendt tjenestefordeling"*.
    - The order beforehand is where a roster lives.
    - It keeps ADR-024 D7's requirement that an approval given beforehand can be told apart from one given afterwards (`ADR-024:222`).
  - *Gives up:*
    - the most to build;
    - a matching rule between an order's planned span and the hours actually worked.

*For the adjudicator: a possible narrowing.* (b) on its own cannot record the placement fact that D7 and § 14 require, so it would need (a)'s per-hour mark anyway. The planner proposes asking (c)/(a), with (b) named as not offered and the prose explaining why. The adjudicator rules.

**The lean: (c), the ADR's** (`:265`). The reasons:
- **It is the sources' own shape.** A leader orders, and a roster is set beforehand by nature. Necessity is judged afterwards.
- **It is the natural home for the typed time D-3 = (c) keeps.** On-call duty is assigned, a call-in is called by the employer, and an official trip is ordered. So the later stage reuses this record instead of adding a second order record.
- **It satisfies ADR-024 D7's audit requirement.** An order beforehand and an acceptance afterwards stay distinguishable.
- **The events already exist** (`OvertimePreApprovalCreated/Approved/Rejected`, `OvertimeNecessityAcknowledged`). They change shape (below), but the audit vocabulary stands.
- *A build-sequencing note, not part of the question:* the acceptance-afterwards half alone makes stage 2 correct, because every ordered hour can be marked and accepted. So S148 could land that half in stage 2 and the order-beforehand half with D-3's typed time. That is the S148 plan's call.

**Proposed as recorded proposals** (told to the owner in the prose, not asked; the adjudicator confirms or converts each).
- **D-4.P1, the cut-off.** A mark is decided before the leader approves the month, and the month cannot be approved while a mark on it is undecided. This matches the ADR's lean (`:266-267`). A stand-in (vikar) who may approve the month may decide its marks (Q-B = (c)).
- **D-4.P2, a decision after the month is approved:** a dated movement in the month it is made, never a reopening. This covers an acceptance after approval or after export, and every accepted AC claim (D5).
  - **The mechanism:** the hours leave flex as a dated movement in the decision month, and the overtime account (D-5) is credited. The worked month is never reopened.
  - **Why not a correction of the worked month:** a correction changes that month's flex closing. Every later month's opening then goes stale, and QUAL-229's guard (S147 TASK-14700) refuses each later month until HR recalculates them one by one (HRP-003). The movement touches no exported month (ADR-013, ADR-034).
  - This is the ADR's lean (`:267-268`; D5 `:115-117`).
  - *For D-5:* from when the 3-month time-off deadline runs for a late-accepted hour, the worked month or the decision month.
- **D-4.P3, the placement fact is its own field.** A mark or order records "placement ordered or rostered" separately from "surplus hours ordered or necessary", as D7 already requires (`:131-134`). The placement fact gates **both** § 13's +25 % and § 14's +50 % (RR-12). It is also needed for staff exempt from overtime (D-11's regime), because § 13 and § 14 cover them (`sick-day…research.md:841`, `:870`).
- **D-4.P4, four-eyes (SEC-056; Q-A = (a)).** Every record carries a creator column (reseed). No one decides a record they raised (creator-not-approver), and no one decides on their own hours (T9's self-decision guard, kept). Compensation happens only behind an approved movement (D-5).
  - *One reading to confirm.* A leader's order beforehand is a decision, not a request. Its creator is its decider, by nature. The four-eyes pair for an ordered hour is the leader who orders and the employee who works and registers it.
  - *The alternative:* a leader's order needs a second leader's approval. That is stricter, and it is today's two-step create-then-approve shape. *The planner leans to the first reading; flagged for the adjudicator, who may turn it into an owner question.*
- **D-4.P5, room for typed time.** The record carries an activity type, `NORMAL` until D-3's later stage adds on-call, call-in and travel.
- **D-4.P6, the `OvertimeRequiresPreApproval` switch retires.** Under D3 every overtime hour needs a decision, so an optional switch has no meaning. ADR-024's "Bug #4 seed flip" (`ADR-024:254`) resolves by retirement. The advisory governance check retires, or reads the record. `MaxOvertimeHoursPerPeriod` can stay as a warning over decided hours (`SYSTEM_TARGET.md:381-384`).

**What it builds on** (the owner's answers, and how they narrow this question).
- **D-1 = (b), stages:** built in **stage 2**, with the overtime account (D-5) and the supplements. Before stage 2 no mark is read. The § 14 weekend +50 % waits for stage 2 too, the same interim class as the supplement, so the stage-1 sprint log should name it.
- **D-2 = (a), Skema's intervals:**
  - **A mark names a clock span** (date, start, end), because the intervals are the only place a Skema hour has a time of day. Naming time-entry ids, as the dormant event does, cannot carry a placement.
  - **Marks and intervals must stay consistent.** The intervals have no ids and are replaced on each save (`init.sql:1973-1984`), so a save that removes work under a decided mark must be refused or reopen the mark.
  - **Marks reach payroll the same way.** The supplement half (stage 2) reads the intervals, and the decided marks must reach the pure engine in the same way (ADR-043 D3, `:93-94`).
- **D-3 = (c), typed time later:** the record leaves room for a type (P5).
- **D-9 = (a), AC claims per claim:** an AC claim (stage 3) reuses the record's shape: hours, period, creator, decider. P2's decision-month movement serves it. Who "the employer" is when it decides a claim (D5: the employee claims, the leader attests, the employer decides) is stage 3's design.
- **D-10 = F-7 (c), default (ii):** the part-timer band reads which hours were ordered. Under (ii), an ordered hour above a part-timer's own norm is overtime, like a full-timer's.
- **D-11 = (b), the HR-recorded regime:** a mark is regime-neutral. It records "ordered or necessary" and the placement, and the regime then routes the hour:
  - +50 % overtime;
  - a merarbejde claim (PROSA pay groups 3–4, and AC per D-11.4);
  - nothing, for HK staff above the pay ceiling, who still earn § 13 and § 14 on ordered placements.
  - *A gap for stage 3:* whether PROSA groups 3–4's merarbejde is a D5-style claim or a stage-2 mark is not yet designed.
- **Q-A = (a) and SEC-056:** T9 is S147's (wave 2a). Creator-not-approver was left to this redesign (P4).

**What the owner should know.**
- **Leaders get a new step:** deciding marked hours as part of approving a month. A new HRP row records the hand-off (ADR-043's Consequences, `:217-218`).
- **A weekend hour the employee chooses under flex earns nothing extra.** It is flex, hour for hour, with neither +25 % nor § 14's +50 %. Only ordered or rostered weekend work earns them (RR-12). Today every timed weekend hour earns a weekend rate (`SupplementRule.cs:68-73`), so demo payslips move here in stage 2.
- **The "pre-approval required" switch goes away,** because every overtime hour now needs a decision.
- **A late decision lands in the month it is made.** A month already approved or exported is never reopened for it.

**What the S148 build gets from each answer** (all in stage 2; reseed).
- **(c):**
  - **The table changes shape.** `overtime_pre_approvals` changes from a budget into records that name hours. Each record holds:
    - a date and a clock span, from D-2's intervals;
    - a kind: ordered beforehand, or necessary and accepted afterwards;
    - a placement-only flag, separate from surplus;
    - an activity type, `NORMAL` for now;
    - a creator, a decider and a status.
  - **The acceptance-afterwards half:** ADR-024 D7's endpoint, built at last but naming spans instead of entries. Skema gains a way to mark a span (`ux`), and Teamoversigt shows the marks in month approval.
  - **The order-beforehand half:** a leader records an order or a roster span, and the matching rule against worked intervals.
  - **Guards and the cut-off:** T9's self guard kept; creator-not-approver; the month approval refuses while a mark is undecided; decisions after approval become dated movements (P2).
  - **Payroll reads the decided marks** as a frozen input. Its read-only contract on Backend tables grows again (ADR-016 D10, `ADR-016:179`; three tables after D-2), and the replay digest and manifest record the marks.
  - **What retires:** `OvertimeRequiresPreApproval` and the advisory check.
  - **Documents:** an HRP row; SYSTEM_TARGET §L's pre-approval text, owed to the owner.
- **(a):** the same, without the order-beforehand half. Rosters and, later, D-3's on-call assignments are marked afterwards each time.
- **(b):** today's table kept, plus a creator column, used up by surplus hours at month end. The placement fact still needs a per-span mark, so (b) alone cannot pay § 13 or § 14.

**The ADR-043 text it rewrites.**
- F-2 (`:254-269`) becomes a decision.
- D3's "whether it names hours or a budget is F-2" (`:93-94`), and its ADR-024 D7 bullet (`:95-96`): from dormant design to the payroll path, with the event naming spans.
- D5's sub-question sentence (`:115-117`): the decision-month movement (P2).
- D7's placement sentence (`:130-134`) names the record. Its § 14 bullet (`:135-136`) gains "ordered or rostered" (RR-12).
- "What it retires" gains the budget shape (`init.sql:2027-2038`) and `OvertimeRequiresPreApproval`.
- Consequences, "New hand-offs" (`:217-218`): the marking row is designed.
- **Outside ADR-043:**
  - SYSTEM_TARGET §L (`SYSTEM_TARGET.md:386-391`), owed to the owner;
  - SEC-056's register row gets its route;
  - HRP-018 (`hr-follow-up-process-register.md:151-156`);
  - ADR-016 D10's read contract;
  - ADR-024 D7.
- The recorded proposals become ADR text when this question is answered.

**Waits on.** Nothing for the asking: D-2 and D-3 are answered. D-5 (F-3, the overtime account) waits on this answer. Building it is stage 2.

---
*Revision history.*
- Draft 1 (2026-10-29): first brief, after D-2 = (a) and D-3 = (c). It folds in SEC-056 (a creator column, creator-not-approver, T9's guard kept) and RR-12 (§ 14 applies to ordered or rostered work). Six recorded proposals are proposed for adjudication, one with a reading flagged (P4). (b) is proposed as not offered.
