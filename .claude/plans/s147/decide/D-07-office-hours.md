# D-7 — F-4: should an institution be able to switch on fixed office hours (PAV's *kontortid* model)?

*Brief, draft 1, 2026-10-29: not adjudicated. The owner has not been asked. Drafted by `planner` (Opus 5.5). Code facts verified at `61d6a98a` (master HEAD when the work began); HEAD `f37d7cd3` at writing changed no file under `src/`, `docker/` or `frontend/`. It builds on D-6 = (a), D-1 = (b) and D-2 = (a), all answered 2026-10-29. The ordered-hour half also leans on D-4, which is not yet asked.*

**The question in one sentence.** Some state flex agreements fix *office hours* for each weekday (*kontortid*). Inside them, even work a leader ordered stays in flex hour for hour, and a sick day can be credited with that weekday's office hours instead of 7.4 h. Should StatsTid let an institution switch this on, as a dated local setting that is off unless set? Or should office hours be left out, so that every ordered or accepted hour leaves flex and a sick day is always credited 7.4 h × fraction?

**Why it matters.**
- **An institution on the office-hours model is overpaid today.** Its agreement keeps ordered work inside office hours in flex, at 1:1, but StatsTid would pay it at +50 %.
  - *Example:* Monday's office hours are 08:00–16:00 (8 h). A leader asks an employee who usually starts at 09:00 to work the whole office day. Under the office-hours model, the 0.6 h above 7 h 24 min stay in flex hour for hour, because they fall inside office hours. Without the model, they leave flex and earn +50 %.
- **Its sick days are credited by weekday.**
  - *Example:* Friday's office hours are 08:00–13:00 (5 h) and Monday's 8 h. A sick Friday is credited 5 h and a sick Monday 8 h, not 7.4 h each. Over a whole week of illness it nets out to the week's norm. A single sick day moves flex by the difference.
- **Who notices:** employees and leaders at institutions that use office hours, and payroll. Everyone else sees nothing, because the setting is off unless an institution sets it.

**What StatsTid does today** (verified).
- **No concept of office hours or core hours exists.** A search for *kontortid*, *fikstid*, office hours or core hours across `src/`, `frontend/src`, `docker/` and `tools/` finds nothing.
  - The local agreement profile holds five overridable values, none of them office hours (`docker/postgres/init.sql:856-872`).
- **Every weekday has the same amount.** The per-day amount is 7.4 h × fraction (37 h ÷ 5, `src/SharedKernel/StatsTid.SharedKernel/Calendar/DayNorm.cs:44-45`). The screen serves the same figure (`src/Backend/StatsTid.Backend.Api/Services/DailyNormCalculator.cs:166`).
- **Sick days earn no credit yet.** At this HEAD a `SICK_DAY` gets no norm credit at all (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/AbsenceRule.cs:34-45`; QUAL-231).
  - S147's TASK-14708 (wave 2a, not yet on master) adds the credit for every agreement.
  - The credit is the stored sick hours, which the Skema save caps at the per-day amount (`src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:924-1003`; RR-2).
- **No hour is "ordered" yet.** The idea arrives with D-4.

**What the sources say.**
- **The office-hours exception** (V8, PAV 19.5.11, `docs/references/overtime-flex-model-research.md:38`). In PAV's model, work inside office hours stays in flex *"uanset om placeringen … er sket efter … konkret pålæg fra ledelsen"* (even when a leader specifically ordered the placement). An hour leaves flex only if all three conditions hold:
  1. it was ordered or necessary;
  2. it fell outside office hours;
  3. it came after the day's first 7 h 24 min.
- **A local variant of the third condition.** The Aarhus University template sets it as "above 160.33 h a month", not per day (`docs/references/agreement-cells-reverification-research.md:631`; per-cell note `:214`). AU Foulum uses the three-condition test (`:632`).
- **It is optional, not universal.**
  - PAV's model offers office hours as an option (`:628`).
  - SDU's 2026 agreement has *"ingen regler om fast arbejdstid"* (no fixed hours, `:629`).
  - DTU makes core hours optional per unit (`:630`).
- **The credit.**
  - § 7 nr. 2 credits a paid-absence day with *"det antal timer, den ansatte skulle have arbejdet den pågældende dag"*, the hours fixed for that day, and 7.4 h × fraction only when none are fixed (`docs/references/sick-day-credit-and-agreement-texts-research.md:104-106`).
  - PAV 19.5.13: office hours *"kan"* (can) be the basis for the hours credited on *"ferie- og sygedage"* (`:132`). The word is "can": it is the local agreement's option.
  - 022-26 § 3 lets a local agreement depart from § 7 altogether (`:135`).
- **RR-2/RR-12 (the research adjudication):** per-weekday office hours become "the hours fixed for the day", and the sick-day credit follows them. So F-4 changes what a sick day is worth.

**The options** (ADR-043 F-4, `:279-284`; working copy `../ADR-043-working.md:332-337`).
- **(a) Not modelled.** Every ordered or accepted hour leaves flex (PAV 19.5.11's general rule). A sick day is credited by § 7 nr. 2's fallback, 7.4 h × fraction.
  - *Gains:* nothing to build; one meaning of "the day's hours".
  - *Gives up:*
    - an institution on the office-hours model is paid +50 % for ordered hours its agreement keeps in flex;
    - its sick days are credited 7.4 h whatever the weekday;
    - it cannot be configured at all, so the answer is "use another product, or accept the difference".
- **(b) An opt-in local setting: per-weekday office hours, dated and off by default.** An ordered or accepted hour then leaves flex only when all three of V8's conditions hold, and the sick-day credit follows the weekday's office hours.
  - *Gains:*
    - it matches the agreements that use the model (PAV's model, the AU template, AU Foulum);
    - it changes nothing for anyone else.
  - *Gives up:*
    - build cost: a per-weekday clock window in the local profile, the three-condition filter, the credit change and the screen;
    - a second notion of "the day's hours" beside the per-day amount, which the screen must show consistently.

**The lean: (b), off by default** (ADR `:284`; refinement `REFINEMENT-s147…:511`; plan `PLAN-s147.md:286`). The reasons:
- **It is the sources' own exception** (V8), and the institutions that use it are in the sample.
- **Off by default means no one is affected** unless their agreement says so. The cost lands only where it buys correctness.
- **D-6 = (a) already built the path.** Office hours are one more dated local value, resolved per day and frozen into the calculation's proof. No new mechanism.
- **(a) is not a free choice.** It knowingly pays +50 % where a configured agreement says flex.

**Proposed as recorded proposals** (told, not asked; the adjudicator confirms or converts each).
- **D-7.P1, office hours are a clock window per weekday** (a start and an end), not just a number of hours. The "outside office hours" test needs times, and the window's length gives the credit.
- **D-7.P2, the third condition uses PAV's daily form:** beyond the day's first 7 h 24 min, at the person's per-day amount (`DayNorm`), as F-4 (b) already says. The AU template's monthly variant (160.33 h) is recorded, not built.
- **D-7.P3, the credit follows office hours when the setting is on** (§ 7 nr. 2's "hours fixed for the day"; RR-2). D-8's tenth parameter (the credit basis) can keep the 7.4 h fallback instead, because PAV says *kan* and § 3 allows it.
  - *Scope, flagged for the adjudicator:* RR-12 and the plan speak of the **sick-day** credit. The sources are wider: § 7 nr. 2's remark covers every paid-absence day (sick days, time off in lieu, holiday days, paid leave; `sick-day…research.md:105`), and PAV 19.5.13 names *"ferie- og sygedage"*.
  - This brief keeps the sick-day wording. Widening it would touch vacation booking (ADR-032), whose per-day cap reads the day's norm (`SkemaEndpoints.cs:924-1003`).
- **D-7.P4, the month norm does not change.** The norm is § 4's 7.4 h × working days, and PAV 19.2.4.1 says how hours fall across days does not matter (`overtime-flex-model-research.md:31`). Only two things change: the absence credit, and which ordered hours leave flex. The screen shows the office-hours credit on an absence day, so screen and payslip agree (D1's rule).
- **D-7.P5, part-timers under office hours** go to a domain expert (does a part-timer's window scale, and is the third condition their own per-day amount?). So do part-timers' fixed days off, already a "does not decide" item (`ADR-043:334-336`).
- **D-7.P6, core hours (*fikstid*) are not part of this question.** Core hours govern presence, not pay. They are proposed in D-8's parameter list as "not modelled for pay".

**What it builds on** (the owner's answers, and how they narrow it).
- **D-6 = (a), the editors reach pay:** office hours are a dated value in the institution's local profile, resolved for each day and frozen into the manifest (the record of what a calculation ran with). This settles the "local parameter" premise F-4 (b) depended on: under QUAL-204 it would never have reached pay.
- **D-1 = (b), stages:** the two halves land in different stages.
  - *The credit half* changes flex, so it lands with D-6's config stage and stage 1.
  - *The ordered-hour filter* needs D-4's marks, so it lands in stage 2. Before stage 2 no hour leaves flex, so there is nothing to filter.
- **D-2 = (a), Skema's intervals:** "outside office hours" is tested on the intervals' clock times. With manual hours retired (D-2.4), every worked hour has times.
- **D-4 (not yet asked):** the filter reads the *surplus* mark ("ordered or necessary"), not the *placement* mark. A placement inside office hours is daytime, so normally no supplement window applies. Office hours that run past 17:00 are an edge case for the expert.
- **D-8:** the tenth parameter can override the credit (P3).

**What the owner should know.**
- **Off by default.** No institution changes unless its agreement uses office hours and someone sets them.
- **A sick day's value can then differ by weekday.** It nets out over a week; a single day moves flex by the difference.
- **Ordered work inside office hours stays flex, hour for hour, at those institutions.** That is their agreement's own rule.

**What the S148 build gets from each answer.**
- **(b):**
  - *The setting:* the local profile gains an optional clock window per weekday (Monday–Friday), dated, audited and frozen into the manifest. It is off unless set (D-6's config stage).
  - *Stage 1:* a paid-absence day on a weekday with office hours is credited with that window's length, unless D-8's credit basis says otherwise. The screen's per-day diff shows the same figure.
  - *Stage 2:* an ordered or accepted hour stays in flex when it falls inside office hours or within the day's first per-day amount. Only hours outside both leave flex as overtime.
  - *Recorded, not built:* the AU monthly variant, and part-timers under office hours.
- **(a):** nothing is built. F-4 records "not modelled", and "What this ADR does not decide" names office hours, with the consequence that an institution on the model is paid +50 % for ordered hours inside its office hours.

**The ADR-043 text it rewrites.**
- F-4 (`:279-284`) becomes a decision.
- D3's "the sources' own exception" bullet (`:97`) names the mechanism.
- F-5's parameter list (`:286-289`): "fikstid/kontortid" splits. Office hours go here; core hours go to D-8 (P6).
- Under (a), "What this ADR does not decide" (`:330-336`) gains office hours.
- The recorded proposals become ADR text when this question is answered.

**Waits on.** Nothing for the asking (D-6 is answered). Building the ordered-hour half waits on D-4.

---
*Revision history.*
- Draft 1 (2026-10-29): first brief, after D-6 = (a). It folds in RR-2/RR-12: per-weekday office hours become the hours fixed for the day, and the sick-day credit follows them. Six recorded proposals are proposed, with the scope of P3 (sick days only, or every paid-absence day) flagged for the adjudicator.
