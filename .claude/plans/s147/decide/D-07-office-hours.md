# D-7 — F-4: should an institution be able to switch on fixed office hours (PAV's *kontortid* model)?

*Brief, draft 2, 2026-10-29: adjudicated (`.claude/reviews/S147-D-batch2-adjudication.md` § D-7). Queued: asked after D-4. Drafted by `planner` (Opus 5.5). Code facts were verified at `61d6a98a` and re-checked at `26b70a08`; no file under `src/`, `docker/`, `frontend/` or `tools/` changed in between. It builds on D-6 = (a), D-1 = (b) and D-2 = (a), all answered 2026-10-29. The ordered-hour half also leans on D-4, which is being asked now.*

**The question in one sentence.** Some state flex agreements fix *office hours* for each weekday (*kontortid*). Inside those hours, even work a leader ordered stays in flex hour for hour. A day of paid absence (a sick day, a holiday day, a day of time off in lieu) is credited with that weekday's office hours instead of the flat 7.4 h. Should StatsTid let an institution switch this on, as a dated local setting that is off unless set? Or should office hours be left out, so that every ordered or accepted hour leaves flex and every paid-absence day is credited 7.4 h × fraction?

**The question as it will be asked** (settled by the adjudicator, D-7.1; verbatim).

> *Some state institutions' flex agreements fix office hours for each weekday (PAV's "kontortid" model). Inside those hours even work a leader ordered stays in flex hour for hour, and a day of paid absence — a sick day, a holiday day — is credited with that weekday's office hours instead of the flat 7.4 h. Should StatsTid let an institution switch this on, as per-weekday office hours in its dated local settings, off unless set; or should office hours be left out, so that every ordered or accepted hour leaves flex and every paid-absence day is credited 7.4 h × fraction?*

- **(b) An opt-in local setting, off by default (recommended)**
- **(a) Not modelled**

**Why it matters.**
- **Without the setting, an institution on the office-hours model is overpaid.** Its agreement keeps ordered work inside office hours in flex at 1:1, but StatsTid would pay it at +50 %.
  - *Example:* Monday's office hours are 08:00–16:00 (8 h). A leader asks an employee who usually starts at 09:00 to work the whole office day. Under the office-hours model, the 0.6 h above 7 h 24 min stay in flex hour for hour, because they fall inside office hours. Without the model, they leave flex and earn +50 %.
- **Its paid-absence days are credited by weekday** (ruling 4: every paid-absence day, not only sick days).
  - *Example:* Friday's office hours are 08:00–13:00 (5 h) and Monday's 8 h. A sick Friday and a Friday taken as a holiday day are each credited 5 h. A Monday of either is credited 8 h. Neither is credited 7.4 h.
  - Over a whole week of absence it nets out to the week's norm. A single day moves flex by the difference.
- **Who notices:** employees and leaders at institutions that use office hours, and payroll. Everyone else sees nothing, because the setting is off unless an institution sets it.

**What StatsTid does today** (verified).
- **No concept of office hours or core hours exists.** A search for *kontortid*, *fikstid*, office hours or core hours across `src/`, `frontend/src`, `docker/` and `tools/` finds nothing.
  - The local agreement profile holds five overridable values, none of them office hours (`docker/postgres/init.sql:856-872`; the values `:864-868`).
- **Every weekday has the same amount.** The per-day amount is 7.4 h × fraction (37 h ÷ 5, `src/SharedKernel/StatsTid.SharedKernel/Calendar/DayNorm.cs:44-45`). The screen serves the same figure (`src/Backend/StatsTid.Backend.Api/Services/DailyNormCalculator.cs:166`).
- **That one figure does three jobs on the screen side** (D-7.2 item 4).
  - `ConsumptionCalculator.FullDayHoursAsync` delegates to `DailyNormCalculator` (`src/Backend/StatsTid.Backend.Api/Services/ConsumptionCalculator.cs:87-99`). The result is then used for three things:
    - **the absence cap**, the most absence hours a day can hold (`src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:946-977`; in-lock `:1941-1963`);
    - **the vacation divisor**, which turns booked hours into vacation days (`ConsumptionCalculator.cs:184-194`);
    - the per-day norm the screen compares worked hours against.
- **Sick days earn no credit yet at this HEAD** (D-7.2 item 5). A `SICK_DAY` gets no norm credit at all (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/AbsenceRule.cs:34-45`; QUAL-231).
  - S147's TASK-14708 (wave 2a, **not on master** at `26b70a08`) adds the credit for every agreement.
  - The credit is the stored sick hours, which the Skema save caps at the per-day amount (`SkemaEndpoints.cs:924-1003`; RR-2).
  - Other paid-absence types (vacation, særlige feriedage, paid leave, care days) are already norm-credit types (`AbsenceRule.cs:34-45`). They are credited with their stored hours, capped the same way.
- **No hour is "ordered" yet.** The idea arrives with D-4.

**What the sources say.**
- **The office-hours exception** (V8, PAV 19.5.11, `docs/references/overtime-flex-model-research.md:38`). In PAV's model, work inside office hours stays in flex *"uanset om placeringen … er sket efter … konkret pålæg fra ledelsen"*: even when a leader specifically ordered the placement. An hour leaves flex only if all three conditions hold:
  1. it was ordered or necessary;
  2. it fell outside office hours;
  3. it came after the day's first 7 h 24 min.
- **A local variant of the third condition.** The Aarhus University template sets it as "above 160.33 h a month", not per day (`docs/references/agreement-cells-reverification-research.md:631`; per-cell note `:214`). AU Foulum uses the three-condition test (`:632`).
- **It is optional, not universal.**
  - PAV's model offers office hours as an option (`:628`).
  - SDU's 2026 agreement has *"ingen regler om fast arbejdstid"* (no fixed hours, `:629`).
  - DTU makes core hours optional per unit (`:630`).
- **The credit covers every paid-absence day** (ruling 4).
  - § 7 nr. 2 credits *"dage med ret til fravær med løn"* with *"det antal timer, den ansatte skulle have arbejdet den pågældende dag"*: the hours fixed for that day. It falls back to 7.4 h × fraction only when no hours are fixed. Its remark lists sick days, time off in lieu, holiday days and paid leave (`docs/references/sick-day-credit-and-agreement-texts-research.md:104-106`).
  - PAV 19.5.13: office hours *"kan"* (can) be the basis for the hours credited on *"ferie- og sygedage"* (holiday and sick days, `:132`). The word is "can": it is the local agreement's option.
  - 022-26 § 3 lets a local agreement depart from § 7 altogether (`:135`).
- **RR-2/RR-12 (the research adjudication), as refined by ruling 4.** Per-weekday office hours become "the hours fixed for the day", and the credit for every paid-absence day follows them. So F-4 changes what a day of paid absence is worth. Narrowing it to sick days has no source, and would make a Friday holiday and a Friday sick day worth different flex.

**The options** (ADR-043 F-4, `:279-284`; working copy `../ADR-043-working.md`, F-4 in the verbatim body).
- **(b) An opt-in local setting: per-weekday office hours, dated and off by default.** An ordered or accepted hour then leaves flex only when all three of V8's conditions hold. The credit for every paid-absence day follows the weekday's office hours.
  - *Gains:*
    - it matches the agreements that use the model (PAV's model, the AU template, AU Foulum);
    - it changes nothing for anyone else.
  - *Gives up (build cost):*
    - a per-weekday clock window in the local profile;
    - the three-condition filter;
    - **one served figure, "the day's planned hours", beside the norm share** (D-7.2 item 4). The absence cap, the vacation divisor and the paid-absence credit read the planned hours. The month norm reads the norm share (P4). With no office hours set the two figures are equal, so nothing changes for anyone who does not set them;
    - the screen.
- **(a) Not modelled.** Every ordered or accepted hour leaves flex (PAV 19.5.11's general rule). Every paid-absence day is credited by § 7 nr. 2's fallback, 7.4 h × fraction.
  - *Gains:* nothing to build, and one meaning of "the day's hours".
  - *Gives up:*
    - an institution on the office-hours model is paid +50 % for ordered hours its agreement keeps in flex;
    - its paid-absence days are credited 7.4 h whatever the weekday;
    - it cannot be configured at all, so the answer to such an institution is "use another product, or accept the difference".

**The lean: (b), off by default — confirmed by the adjudicator** (D-7.3; ADR `:284`; refinement `REFINEMENT-s147…:511`; plan `PLAN-s147.md:286`). The adjudicator's reasons:
- **(a) knowingly computes the wrong result for a configured agreement.** It pays +50 % where that agreement says flex, and credits 7.4 h where it says the day's hours.
- **(b) costs nothing for anyone who does not set it.**
- **D-6 = (a) already built the path.** Office hours are one more dated local value, resolved per day and frozen into the calculation's proof.

**Recorded proposals** — *adjudicated, told to the owner, not asked* (D-7.4; all accepted). They are listed in `../ADR-043-working.md`, "Provisional items and recorded proposals".
- **D-7.P1, office hours are a clock window per weekday — accepted, with a warning rule** (D-7.2 item 3).
  - Each weekday has a start and an end, not just a number of hours. The "outside office hours" test needs times, and the window's length gives the credit.
  - **The warning rule (D-7.P1 as corrected, `.claude/reviews/S147-ADR-043-adjudication-c1.md` R-5):** the five windows normally add up to the full-time week's norm (37 h; the profile's own `weekly_norm_hours` where one is set, ADR-043 provisional item 18). When they do not, the editor warns; it does not refuse. How a part-timer's windows scale is ADR-043 provisional item 14 (D-7.P5). *(Draft 2 said "37 h × fraction"; the windows are set per institution and the fraction is a person's, so the check is against the full-time week.)*
- **D-7.P2, the third condition uses PAV's daily form — accepted, with § 14's precedence** (D-7.2 item 2).
  - An ordered hour leaves flex only beyond the day's first 7 h 24 min, at the person's per-day amount (`DayNorm`), as F-4 (b) already says.
  - **§ 14 comes first.** Ordered work at the weekend or on a søgnehelligdag (a public holiday on a weekday) is § 14's, whatever the office hours. It is kept out of the hours count at +50 % (D-4.P3). The domain expert confirms this for office-hours institutions (022-26 § 3 lets a local agreement depart).
  - The AU template's monthly variant (160.33 h) is recorded, not built.
- **D-7.P3, the credit follows office hours when the setting is on — accepted, widened to every paid-absence day** (ruling 4).
  - It covers sick days, holiday days, særlige feriedage, time off in lieu, paid leave and care days: § 7 nr. 2's "hours fixed for the day".
  - **One served figure.** Every credit-class absence code (TASK-14708's classification) reads one served "planned hours" figure, and ADR-032's vacation divisor reads the same figure. That is a build item.
  - **D-8's parameter can keep the 7.4 h fallback instead.** D-8's parameter is the **paid-absence credit basis** (R-6), because PAV says *kan* and § 3 allows it.
  - If S148 builds this in steps, the sprint log names which absence classes read the basis.
- **D-7.P4, the month norm does not change — accepted.** The norm is § 4's 7.4 h × working days, and PAV 19.2.4.1 says how hours fall across days does not matter (`overtime-flex-model-research.md:31`).
  - Only two things change: the paid-absence credit, and which ordered hours leave flex.
  - The norm share stays the month norm's building block. The planned hours are a second figure, read only by the credit, the cap and the divisor.
  - The screen shows the office-hours credit on an absence day, so screen and payslip agree (D1's rule).
- **D-7.P5, part-timers under office hours — accepted as an expert item.** Does a part-timer's window scale, and is the third condition their own per-day amount? It joins part-timers' fixed days off, already a "does not decide" item (`ADR-043:334-336`).
- **D-7.P6, core hours (*fikstid*) are not part of this question — accepted.** Core hours govern presence, not pay. They are listed in D-8's parameter list (R-2) as "not modelled for pay".

**What it builds on** (the owner's answers, and how they narrow it).
- **D-6 = (a), the editors reach pay:** office hours are a dated value in the institution's local profile, resolved for each day and frozen into the manifest (the record of what a calculation ran with). This settles the "local parameter" premise F-4 (b) depended on: under QUAL-204 it would never have reached pay.
- **D-1 = (b), stages:** the two halves land in different stages.
  - *The credit half* changes flex, so it lands with D-6's config stage and stage 1.
  - *The ordered-hour filter* needs D-4's marks, so it lands in stage 2. Before stage 2 no hour leaves flex, so there is nothing to filter.
- **D-2 = (a), Skema's intervals:** "outside office hours" is tested on the intervals' clock times. With manual hours retired (D-2.4), every worked hour has times.
- **D-4 (being asked):** the filter reads the *surplus* mark ("ordered or necessary"), not the *placement* mark. A placement inside office hours is daytime, so normally no supplement window applies. Office hours that run past 17:00 are an edge case for the expert. Whichever of D-4's (c) or (a) is chosen, the filter reads decided hours, so this question does not depend on D-4's answer.
- **D-8:** the paid-absence credit basis (R-6) can override the credit (P3).

**What the owner should know** (D-7.5).
- **Off by default.** No institution changes unless its agreement uses office hours and someone sets them.
- **At an institution that sets office hours, a day of paid absence is worth that weekday's hours.** A Friday holiday and a Friday sick day are worth the same, and a week of either nets to zero.
- **Ordered work inside office hours stays flex there.** That is their agreement's own rule.
- **Two halves in two stages:** the credit with stage 1, the ordered-hour filter with stage 2.

**What the S148 build gets from each answer.**
- **(b):**
  - *The setting:* the local profile gains an optional clock window per weekday (Monday–Friday), dated, audited and frozen into the manifest. It is off unless set (D-6's config stage). The editor warns, and does not refuse, when the windows do not add up to the full-time week's norm (37 h; the profile's own `weekly_norm_hours` where one is set, ADR-043 provisional item 18); how a part-timer's windows scale is ADR-043 provisional item 14 (P1 as corrected).
  - *One served figure, the day's planned hours* (P3, P4): the window's length where set, otherwise the norm share. The absence cap (`SkemaEndpoints.cs:946-977`, `:1941-1963`), the vacation divisor (`ConsumptionCalculator.cs:184-194`) and the paid-absence credit read it. The month norm keeps reading the norm share.
  - *Stage 1:* a paid-absence day on a weekday with office hours is credited with that window's length, unless D-8's paid-absence credit basis says otherwise. The screen's per-day diff shows the same figure.
  - *Stage 2:* an ordered or accepted hour stays in flex when it falls inside office hours or within the day's first per-day amount. Only hours outside both leave flex as overtime. § 14's weekend and holiday work comes first (P2).
  - *Recorded, not built:* the AU monthly variant, and part-timers under office hours.
- **(a):** nothing is built. F-4 records "not modelled", and "What this ADR does not decide" names office hours, with the consequence: an institution on the model is paid +50 % for ordered hours inside its office hours, and its paid-absence days are credited 7.4 h × fraction.

**The ADR-043 text it rewrites.**
- F-4 (`:279-284`) becomes a decision.
- D3's "the sources' own exception" bullet (`:97`) names the mechanism.
- F-5's parameter list (`:286-289`): "fikstid/kontortid" splits. Office hours go here; core hours go to D-8 (P6).
- F-5's sentence on the credit reads "paid-absence credit basis" (ruling 4).
- Under (a), "What this ADR does not decide" (`:330-336`) gains office hours.
- Outside ADR-043: ADR-032 (the vacation divisor reads the planned hours under (b)).
- The recorded proposals become ADR text when this question is answered.

**Waits on.** Nothing for the asking (D-6 is answered). Asked after D-4. Building the ordered-hour half waits on D-4.

---
*Revision history.*
- Draft 1 (2026-10-29): first brief, after D-6 = (a). It folds in RR-2/RR-12: per-weekday office hours become the hours fixed for the day, and the sick-day credit follows them. Six recorded proposals are proposed, with the scope of P3 (sick days only, or every paid-absence day) flagged for the adjudicator.
- Draft 2 (2026-10-29): the adjudication applied (`.claude/reviews/S147-D-batch2-adjudication.md` § D-7).
  - **D-7.1:** added "The question as it will be asked", verbatim, with (b) first.
  - **Ruling 4 and D-7.2 item 1:** the credit covers every paid-absence day. This changes the one-sentence question, the example, the sources, P3, the owner prose and "Stage 1".
  - **D-7.2 items 2–5:**
    - item 2: § 14's precedence, in P2;
    - item 3: the windows' warning rule, in P1;
    - item 4: the one served "planned hours" figure, in the facts, (b)'s cost line, P3, P4 and the build;
    - item 5: TASK-14708 is not on master at `26b70a08`.
  - **D-7.3–D-7.5:** the lean is confirmed with the adjudicator's reasons. P1–P6 are marked accepted (P5 as an expert item). "What the owner should know" is replaced by D-7.5.
  - **Other:** the working-copy F-4 citation (`:332-337`, which was off) now cites the section, because the working copy's line numbers move as its lists grow. D-4 is now "being asked", and the filter does not depend on its answer.
- Draft 2.1 (2026-10-29): `.claude/reviews/S147-ADR-043-adjudication-c1.md` R-5 applied (the adjudicator's D-7.2 item 3 corrected). P1's warning rule (`:78`) and the build line (`:112`) now compare the five windows with the full-time week's norm (37 h, or the profile's own `weekly_norm_hours`), not "37 h × fraction"; a part-timer's windows stay ADR-043 provisional item 14. Nothing else changed; the owner's D-7 answer is untouched.
