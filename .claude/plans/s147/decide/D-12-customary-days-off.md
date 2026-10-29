# D-12 — F-9: how do an institution's customary days off (5 June, 24 December, 31 December, 1 May) work in StatsTid?

*Brief, draft 1, 2026-10-29: not adjudicated. The owner has not been asked. Drafted by `planner` (Opus 5.5). Code facts verified at `26b70a08` (master HEAD). It builds on D-1 = (b), D-2 = (a), D-6 = (a), all answered 2026-10-29. It also builds on two adjudicated recorded proposals: R-1, where local rules live (customary days are "a dated table beside the profile", `.claude/reviews/S147-D-batch2-adjudication.md` § D-8.4), and R-6 as widened by ruling 4 (the paid-absence credit basis). The adjudicator ruled it draftable now, because its dependency was R-1. Asking order: after D-5.*

**The question in one sentence.** Some institutions give staff a paid day or afternoon off on 5 June, 24 December, 31 December or 1 May by local custom; others close on such days and staff use their own leave. Should StatsTid let an institution record these days, and in which forms?

**The question as it will be asked** — *proposed wording; the adjudicator settles it.*

> *Some state institutions give their staff a paid day or afternoon off on 5 June, 24 December, 31 December or 1 May by local custom; others close on such days and staff cover them with their own leave or flex. None of these days is a public holiday, so none lowers the month's norm. Should an institution be able to record its customary days in StatsTid — dated, each marked as a paid day off (credited like a paid-absence day, whole or part) or as a closure day (the employee covers it) — or record paid days off only; or should StatsTid leave them out, so that an employee given a paid day off registers it as paid leave themselves?*

- **(c) Both kinds, per institution and date (recommended)**
- **(a) Paid days off only**
- **(d) Not modelled: employees register paid leave themselves**

*Proposed as not offered:* **(b) closure days only.**
- **It cannot represent a paid custom.** The arbitration found paid customs on *"langt de fleste statslige arbejdspladser"*, most state workplaces (FV2016.0107; `docs/references/agreement-cells-reverification-research.md:414`).
- **Its only gain is a notice in Skema.** A closure day changes no figure: the employee covers it with leave or flex, as on any working day. (c) already contains it.

*For the adjudicator:* (d) is valid but weak (below), and offering it is a product choice; it could also be dropped. ADR-043 F-9 lists (a), (b) and (c) only (`ADR-043:322-328`).

**Why it matters.**
- **These days are customs, not holidays.**
  - The state's list of holidays that lower the norm is closed: ten days. Grundlovsdag, 24 December and 31 December are named as *not* holidays (022-26, remark to § 4 stk. 2, `agreement-cells…:399-404`), and PAV adds 1 May (`:405`).
  - A paid day off on any of them is a local custom (PAV 26.2.8.3, `:408-412`).
  - An institution can withdraw a custom with reasonable common notice (FV2016.0107, `:414`) and reinstate it later: the tax ministry reinstated Grundlovsdag and New Year's Eve in 2025 (`:100-104`).
- **Each institution differs.**
  - The Danish Defence gives civilian staff all of 5 June off with pay, may give 1 May and 24 December, and gives nothing on 31 December (`:415`).
  - SKAT, after withdrawing its customs, made 24 and 31 December closure days on which staff use holiday or other days off (`:414`).
  - 1 May is special: staff get time off *on request*, insofar as the work allows, and it is unpaid unless the institution's custom pays it (PAV 26.2.8.2, `:413`).
- **Without the custom recorded, the system debits flex for a day the employer pays.**
  - *Example:* a full-timer at an institution that gives 24 December off with pay registers nothing that day. StatsTid expects the day's 7.4 h, so flex drops by 7.4 h for a day the employer gave.
  - Today the only fix is for every employee to remember to register "leave with pay" that day.
- **Who notices:** employees at institutions with customs, the institution's admin who records them, and payroll (the flex balance).

**What StatsTid does today** (verified at `26b70a08`).
- **There is no customary-day or closure-day concept.** A search of `src/`, `docker/`, `frontend/src` and `tools/` for *kutyme*, customary, *lukkedag* or "closure day" finds none. The word "closure" appears only in unrelated code.
- **Grundlovsdag is treated as a public holiday in all three holiday lists.**
  - The lists: the C# list (`src/SharedKernel/StatsTid.SharedKernel/Calendar/DanishPublicHolidays.cs:49`); the table (`docker/postgres/init.sql:165-170`, Grundlovsdag rows `:367`, `:380`, `:397`); and the demo generator (`tools/StatsTid.DemoSeed/Generation/DanishHolidays.cs:68`). No list holds 24 December, 31 December or 1 May.
  - *What it does today:*
    - the send-month check leaves it out of the expected working days (`src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs:2116-2127`, the query at `:2119`);
    - the supplement rule pays its holiday rate (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/SupplementRule.cs:56`);
    - the norm ignores every holiday (`src/SharedKernel/StatsTid.SharedKernel/Calendar/DayNorm.cs:28`);
    - a vacation day booked on it consumes a day (`docs/knowledge-base/decisions/ADR-032-vacation-consumption-correctness.md:47`).
  - **ADR-043 D9 already fixes Grundlovsdag:** it becomes a supplement window from 12:00, not a holiday. That is fixed, not asked (`ADR-043:151-165`; C1's trap, `:162-163`). This question is only about the *custom*.
- **A paid custom can be registered only by each employee, as paid leave.** `LEAVE_WITH_PAY` is in Skema's catalogue (`src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:34-38`) and is a norm-credit type (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/AbsenceRule.cs:44`). Nothing records the custom itself: not who grants it, not from when, not when it was withdrawn.
- **The nearest existing pattern.**
  - An institution's admin can hide an absence type for the organisation (`absence_type_visibility`, `init.sql:1125-1134`; `POST /api/config/{orgId}/absence-types/visibility`, `LocalAdminOrAbove`, `src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs:525-546`).
  - S147's QUAL-235 work serves the særlige feriedage row in Skema when a config is in force, and lets an admin hide it (merged `6bf4e6e4`).
- **The local profile is one row per institution, agreement and OK version** (`init.sql:856-876`; edited under `LocalAdminOrAbove`, `ConfigEndpoints.cs:119`, `:430`). A list of dated days does not fit one row, hence R-1's "a dated table beside the profile".

**What the sources say** (C1, verified in S146; `docs/references/agreement-cells-reverification-research.md:389-438`, per-cell notes `:78-104`; confidence HIGH on the agreement facts).
- **Not holidays:** 022-26's closed list and its named exclusions (`:399-404`); PAV 19.2.4.1 adds 1 May (`:405`). AC staff follow the same list (AC § 13 stk. 8, `:407`).
- **A custom, not a rule.** PAV 26.2.8.3: *"Der er ikke fastsat generelle regler om frihed på grundlovsdag, juleaftensdag og nytårsaftensdag … Der kan dog på den enkelte institution være praksis/kutyme for at give frihed med løn"*: there are no general rules, but an institution may have a practice of giving paid time off (`:408-412`).
- **1 May** (PAV 26.2.8.2): time off *"på begæring"* (on request), insofar as the work allows. It is unpaid unless the institution's custom says otherwise (`:413`).
- **Withdrawal and its scope.**
  - FV2016.0107 (2017) found the custom on most state workplaces and held it withdrawable collectively with reasonable notice (`:414`).
  - Its scope is narrower than general: it concerned AC staff at SKAT. It found no uniform practice on whether the day off was paid, and it left the *"mellemløsning"* undecided (`:93-99`).
- **Reinstatement:** the tax-staff union's 2025 newsletter reports Grundlovsdag and New Year's Eve reinstated as customary days off, paid for locally (`:100-104`).
- **How a paid day off enters the time account** (the verifier's recommendation, `:434`): *"as paid absence credited with that day's hours under § 7 nr. 2 … not … a cut to the norm, because the circular does not count these days as holidays."* For a full-timer the balance comes out the same either way. Keeping it as an absence keeps the norm faithful to § 4 and makes the custom auditable.
- **The supplement is separate.** Grundlovsdag from 12:00 and 24 December from 14:00 are § 13 supplement windows (`:416`; ADR-043 D9 kind 2), whatever the custom.
- **Open for the expert:** which institutions still grant these days is unknown (`:704`).

**The options** (ADR-043 F-9, `:322-328`; working copy `../ADR-043-working.md`, F-9 in the verbatim body; plus (d)).
- **(c) Both kinds, chosen per institution and date** (the ADR's lean, `:328`). An institution's admin records each customary day as one of two kinds:
  - **a paid day off:** credited like a paid-absence day, whole or part;
  - **a closure day:** no credit; shown in Skema, and the employee covers it with vacation, særlige feriedage, time off in lieu or flex.
  - *Gains:* it represents every pattern in the sources: the Defence's paid 5 June, SKAT's closure days, a paid 24 December afternoon. Employees are told when the office is closed.
  - *Gives up:* the most to build: a dated table and its editor, a Skema row or notice, the credit, and the rules for other absences on the same day.
- **(a) Paid days off only.**
  - *Gains:* the credit, which is the part that changes a figure, at less cost.
  - *Gives up:* closure days are invisible. At SKAT-style institutions employees must know to register leave, as on any working day.
- **(d) Not modelled.** An employee given a paid day off registers it as `LEAVE_WITH_PAY` themselves.
  - *Gains:* nothing to build.
  - *Gives up:*
    - every employee must remember every year, and anyone who forgets loses flex for a day the employer paid;
    - the custom itself (who grants it, from when, when it was withdrawn) is recorded nowhere, a hidden state the standing rule asks us to make visible;
    - nothing tells part-time pro rata apart from a full day.
- **(b) Closure days only.** Not offered (above).

**The lean: (c), the ADR's** (`:328`; plan `PLAN-s147.md:291`). The reasons:
- **The sources show both kinds in practice:** paid customs on most workplaces, and closure days where a custom was withdrawn (`:414`, `:415`).
- **The credit is the verifier's own recommendation** (`:434`). It keeps the norm faithful to § 4 and avoids C1's trap: a customary day must never lower the norm.
- **A closure day costs little once the table exists.** It is one more kind value, and it tells employees what their institution decided.
- **(a) is valid and cheaper,** offered as a product choice. **(d) is valid but leaves the custom unrecorded** and puts the burden on each employee.

**Proposed as recorded proposals** (told to the owner in the prose, not asked; the adjudicator confirms or converts each).
- **D-12.P1, storage: a dated, audited table beside the local profile** (R-1).
  - **Its owner:** each row belongs to an institution and is edited by its admin (`LocalAdminOrAbove`, as the profile and absence-type visibility are).
  - **What a row says:**
    - **the day:** a calendar day (5 June, 24 December, 31 December, 1 May, or a one-off date);
    - **the kind:** a paid day off or a closure day;
    - **the part:** the whole day, or from a clock time;
    - **for paid days, the mode** (P4);
    - **a validity period,** so that withdrawal and reinstatement are dated facts, not deletions.
  - **It is keyed by date, not by OK version** (ADR-043 D9, `:156-157`). Each change is audited and frozen into the manifest under D-6 = (a).
  - *Left to the build input:* whether a custom can be narrowed to one agreement or staff group. FV2016.0107's case concerned AC staff only (`:93-95`).
- **D-12.P2, a paid customary day off is a credit, not a norm cut.**
  - It is a paid-absence day read by the paid-absence credit basis (R-6, ruling 4): the day's planned hours, which are D-7's office hours where set, else 7.4 h × fraction.
  - D1's month norm and D9's closed list of ten are untouched (C1, `:434`).
  - It joins the credit class of TASK-14708's classification.
- **D-12.P3, part days are stated as a clock time or in hours.**
  - "From 12:00" credits the part of the day's planned hours after 12:00. With D-7's office hours set, that is the window's part after 12:00. Without them, the institution states the part in full-time hours (for example 3.7 h), scaled by the part-time fraction.
  - The domain expert confirms the scaling. The ADR's lean already says "partial days in hours" (`:328`).
- **D-12.P4, a paid day has one of two modes.**
  - **Given to everyone:** credited automatically and shown in Skema as the day's absence row.
  - **On request:** a bookable Skema row on that date, credited only when booked. This is PAV 26.2.8.2's 1 May.
  - *Why two modes:* an automatic credit on an on-request custom would add a day of flex for anyone who worked that day.
- **D-12.P5, one credit per day.** On a paid customary day, or its credited part:
  - **a vacation or særlige feriedage booking consumes nothing** for that part. This is the same rule D9 sets for søgnehelligdage, ending ADR-032 D7's limitation (`ADR-043:164-165`; `ADR-032:47`);
  - **a sick day earns no second credit;**
  - **hours worked go to flex,** or, when ordered, follow D-4's decision.
  - The domain expert confirms the worked-day case.
- **D-12.P6, a closure day earns nothing.** Skema shows it. The employee chooses how to cover it: vacation, særlige feriedage, time off in lieu (D-5) or flex. If nothing is registered, the day's norm is unmet and flex covers it, as on any working day. Nothing is booked automatically.
- **D-12.P7, the § 13 windows are separate.** Grundlovsdag from 12:00 and 24 December from 14:00 (D9 kind 2) are unchanged by any custom. Ordered work in them still earns +25 % (D-4.P3); a custom changes the credit, not the supplement.
- **D-12.P8, a configuration, not a hand-off.** No HRP row is proposed: the register already treats configuration acts this way (HRP-016 ruled not a hand-off; HRP-017 same-actor work in progress, `hr-follow-up-process-register.md:141-145`).
- **D-12.P9, the demo seed sets example customs** for the demo institutions, so the feature can be seen (ruling 2's pattern for flex caps).

**Dependencies, and what does not depend on unanswered questions.**
- **R-1 (adjudicated):** the table's home. This is the dependency the adjudicator named.
- **R-6 with ruling 4 (adjudicated):** the credit's basis.
- **D-7 (queued):** under D-7 = (b) the planned hours, and a part day's "after 12:00" share, come from the office-hours window. Under D-7 = (a) they are 7.4 h × fraction, and the institution states a part day in hours. **The question does not depend on D-7's answer;** only P3's arithmetic does, and it is written for both.
- **D-5 (drafted):** a closure day can be covered by time off in lieu. That is a use of D-5's account, not a dependency.
- **D-4 (being asked):** only P5's "ordered work on a customary day" reads D-4's decided hours, under either offered answer.
- **D-1 = (b):** the credit changes flex, so it lands in **stage 1**, with D9's calendar split and D1's monthly norm. The table lands in D-6's config stage.

**What the owner should know** (proposed prose points).
- **These days are customs, not holidays,** so none lowers the month's norm. A paid customary day is a day of paid absence the system credits, so flex does not move.
- **Each institution records its own,** dated: withdrawn and reinstated customs are both just dates.
- **1 May is "on request":** credited only for those who take it.
- **On a paid customary day a booked vacation day is not used up.**
- **The supplement windows on Grundlovsdag and 24 December stay,** whatever the custom.
- **Grundlovsdag stops being a public holiday in StatsTid** (ADR-043 D9, already decided). Without a recorded custom, 5 June becomes an ordinary working day.

**What the S148 build gets from each answer.**
- **(c):**
  - the dated table and its editor (LocalAdmin), audited and frozen into the manifest (D-6's config stage);
  - the credit for paid days in stage 1, in both modes;
  - the closure-day notice in Skema (`ux`);
  - the same-day rules (P5);
  - demo customs;
  - the ADR-032 consumption change for the credited part.
- **(a):** the same without closure days.
- **(d):** nothing. ADR-043 "does not decide" names customary days, and the limitation is that an employee registers paid leave themselves.

**The ADR-043 text it rewrites.**
- F-9 (`:322-328`) becomes a decision.
- D9's kind (3) (`:152-155`) names the table and the credit.
- D9's vacation-on-a-holiday sentence (`:164-165`) extends to paid customary days.
- Under (d), "What this ADR does not decide" (`:330-336`) gains customary days.
- **Outside ADR-043:**
  - ADR-032 D7 (`ADR-032:47`): the consumption rule extended;
  - ADR-017: the table beside the profile;
  - SYSTEM_TARGET §G item 4 (local agreements, `SYSTEM_TARGET.md:170`) needs no change. Calendar context (`:86`) gains customary days only if the owner wants it named.
- The recorded proposals become ADR text when this question is answered.

**Waits on.** Nothing for the asking: R-1 is adjudicated. Asking order: after D-5 (the adjudicator's order, "D-12 after D-8b"; the README places it last).

---
*Revision history.*
- Draft 1 (2026-10-29): first brief, at `26b70a08`, on the adjudicator's ruling that its dependency (R-1) is settled.
  - **Options:** the ADR's three are kept, plus (d) "not modelled". (b) is proposed as not offered.
  - **Nine recorded proposals** are proposed: storage beside the profile; credit not norm cut; part days; the given-to-everyone and on-request modes; one credit per day; closure days; the § 13 windows; configuration, not a hand-off; demo customs.
  - **No dependency on D-4, D-5 or D-7's answers** for the question itself; P3's arithmetic is written for both D-7 answers.
