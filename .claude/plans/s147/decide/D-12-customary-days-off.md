# D-12 — F-9: how do an institution's customary days off (5 June, 24 December, 31 December, 1 May) work in StatsTid?

*Brief, draft 2, 2026-10-29: adjudicated (`.claude/reviews/S147-D-batch3-adjudication.md` § D-12 and rulings 2 and 3; RULED-WITH-QUESTIONS). **Being asked**, after D-5 is told; the owner's answer is recorded by the Orchestrator, not here. Drafted by `planner` (Opus 5.5). Code facts verified at `26b70a08` and re-checked at `bd5a3d3f`: no file under `src/`, `frontend/`, `tools/`, `docker/` or `tests/` changed in between. It builds on D-1 = (b), D-2 = (a), D-4 = (c), D-6 = (a), D-7 = (b), D-8a = (b) and D-8b = (a), all answered 2026-10-29. It also builds on three adjudicated recorded proposals: R-1, where local rules live (customary days are "a dated table beside the profile", `.claude/reviews/S147-D-batch2-adjudication.md` § D-8.4); R-6 as widened by batch-2 ruling 4 (the paid-absence credit basis); and R-7 as widened by batch-3 ruling 3 (a day's paid-absence hours never exceed the credit basis minus the hours worked).*

**The question in one sentence.** Some institutions give staff a paid day or afternoon off on 5 June, 24 December, 31 December or 1 May by local custom; others close on such days and staff use their own leave. Should StatsTid let an institution record both kinds of day, or only the paid ones?

**The question as it will be asked** — *settled wording (D-12.1), verbatim.*

> *Some state institutions give their staff a paid day or afternoon off on 5 June, 24 December, 31 December or 1 May by local custom; others close on those days and staff cover them with their own leave or flex. None of these days is a public holiday, so none lowers the month's norm; a paid customary day is a day of paid absence the system credits, so flex does not move. Should an institution be able to record both kinds of customary day in StatsTid — each dated, and marked either as a paid day off (credited like a paid-absence day, whole or part; given to everyone or on request) or as a closure day (shown in Skema; the employee covers it with leave, time off in lieu or flex) — or paid days off only, with closure days left unrecorded?*

- **(c) Both kinds, per institution and date (recommended)**
- **(a) Paid days off only**

*Not offered* (ruling 2; the Orchestrator says both in the prose):
- **(b) closure days only.** It cannot represent the paid custom the arbitration found on most state workplaces: *"langt de fleste statslige arbejdspladser"* (FV2016.0107; `docs/references/agreement-cells-reverification-research.md:414`). Its only gain is a Skema notice, which (c) already contains.
- **(d) not modelled.** ADR-043 D9 already names per-institution customary days, dated and audited, as the third kind of day in the one calendar (`ADR-043:152-155`), a direction the owner accepted (OQ-3 = (b)). Without a record, the system debits flex for a day the employer paid whenever an employee forgets to register it themselves, and the custom's grant and withdrawal are recorded nowhere.

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

**What StatsTid does today** (verified at `26b70a08`, re-checked at `bd5a3d3f`).
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
- **1 May** (PAV 26.2.8.2): time off *"på begæring"* (on request), *"i den udstrækning tjenesten tillader det"* (insofar as the work allows). It is unpaid unless the institution's custom says otherwise (`:413`).
- **Withdrawal and its scope.**
  - FV2016.0107 (2017) found the custom on most state workplaces and held it withdrawable collectively with reasonable notice (`:414`).
  - Its scope is narrower than general: it concerned AC staff at SKAT. It found no uniform practice on whether the day off was paid, and it left the *"mellemløsning"* undecided (`:93-99`).
- **Reinstatement:** the tax-staff union's 2025 newsletter reports Grundlovsdag and New Year's Eve reinstated as customary days off, paid for locally (`:100-104`).
- **How a paid day off enters the time account** (the verifier's recommendation, `:434`): *"as paid absence credited with that day's hours under § 7 nr. 2 … not … a cut to the norm, because the circular does not count these days as holidays."* For a full-timer the balance comes out the same either way. Keeping it as an absence keeps the norm faithful to § 4 and makes the custom auditable.
- **The supplement is separate.** Grundlovsdag from 12:00 and 24 December from 14:00 are § 13 supplement windows (`:416`; ADR-043 D9 kind 2), whatever the custom.
- **Open for the expert:** which institutions still grant these days is unknown (`:704`).

**The options** (ADR-043 F-9, `:322-328`; working copy `../ADR-043-working.md`, F-9 in the verbatim body).
- **(c) Both kinds, chosen per institution and date** (the ADR's lean, `:328`). An institution's admin records each customary day as one of two kinds:
  - **a paid day off:** credited like a paid-absence day, whole or part; given to everyone, or on request (P4);
  - **a closure day:** no credit; shown in Skema, and the employee covers it with vacation, særlige feriedage, time off in lieu or flex.
  - *Gains:* it represents every pattern in the sources: the Defence's paid 5 June, SKAT's closure days, a paid 24 December afternoon. Employees are told when the office is closed.
  - *Gives up:* the most to build: a dated table and its editor, a Skema row or notice, the credit, and the rules for other absences on the same day.
- **(a) Paid days off only.**
  - *Gains:* the credit, which is the part that changes a figure, at less cost.
  - *Gives up:* closure days are invisible. At SKAT-style institutions employees must know to register leave, as on any working day.
- **(b) Closure days only, and (d) not modelled:** not offered (above).

**The lean: (c), confirmed** (D-12.3; the ADR's lean, `:328`; plan `PLAN-s147.md:291`). The reasons:
- **The sources show both kinds in practice:** the Defence's paid 5 June, and SKAT's closure days (`:414`, `:415`).
- **The credit is the verifier's own recommendation** (`:434`). It keeps the norm faithful to § 4 and avoids C1's trap: a customary day must never lower the norm.
- **A closure day costs little once the table exists.** It is one more kind value, and it tells employees what their institution decided.
- **(a) is valid and cheaper,** offered as a product choice.

## The recorded proposals (adjudicated, told to the owner, not asked; D-12.4)

All nine are accepted. They are listed in `../ADR-043-working.md`, "Provisional items and recorded proposals", items 37–45, and become ADR text when D-12 is answered.

**D-12.P1, storage: a dated, audited table beside the local profile (R-1). Accepted, with D-12.2 item 4.**
- **Its owner:** each row belongs to an institution and is edited by its admin (`LocalAdminOrAbove`, as the profile and absence-type visibility are).
- **What a row says:**
  - **the day:** a calendar day (5 June, 24 December, 31 December, 1 May, or a one-off date);
  - **the kind:** a paid day off or a closure day;
  - **the part:** the whole day, or from a clock time;
  - **for paid days, the mode** (P4);
  - **a validity period,** so that withdrawal and reinstatement are dated facts, not deletions.
- **It is keyed by date, not by OK version** (ADR-043 D9, `:156-157`). Each change is audited and frozen into the manifest under D-6 = (a).
- **Scope** (item 4). A custom may be narrowed to one agreement: an agreement scope fits the table, as the profile beside it is keyed by agreement. FV2016.0107's case concerned AC staff only (`:93-95`). A staff-group scope has no machinery under D-8a = (b), which gives one rule set per institution and agreement plus a per-person yes/no on flex, so it is not built.

**D-12.P2, a paid customary day off is a credit, not a norm cut. Accepted.**
- It is a paid-absence day read by the paid-absence credit basis (R-6, ruling 4).
- D1's month norm and D9's closed list of ten are untouched (C1, `:434`).
- It joins the credit class of TASK-14708's classification: every absence code is exactly one of credit, reduction or neutral, in one place (`.claude/plans/s147/TASK-14708.md:37`).

**D-12.P3, part days are stated as a clock time or in hours. Accepted, with D-12.2 item 1.**
- With D-7 = (b) answered, the arithmetic is the office-hours form where an institution sets office hours, else 7.4 h × fraction:
  - **office hours set:** "from 12:00" credits the part of that weekday's office-hours window after 12:00;
  - **office hours not set:** the institution states the part in full-time hours (for example 3.7 h), scaled by the part-time fraction.
- **The scaling is an expert item.** The ADR's lean already says "partial days in hours" (`:328`).

**D-12.P4, a paid day has one of two modes. Accepted, with D-12.2 item 2 (its reason reworded).**
- **Given to everyone:** credited automatically and shown in Skema as the day's absence row.
- **On request:** a bookable Skema row on that date, credited only when booked. This is PAV 26.2.8.2's 1 May.
- *Why two modes:* they are different institutional facts. Either the day is granted to all, or the employee asks and the leader may refuse *"i den udstrækning tjenesten tillader det"* (insofar as the work allows; PAV 26.2.8.2, `agreement-cells…:413`): a booking that the month approval decides. Under R-7 as widened (batch-3 ruling 3), "given to everyone" can never double-pay someone who worked, because a day's paid-absence credit never exceeds the credit basis minus the hours worked.

**D-12.P5, one credit per day. Accepted, extended** (D-12.4). On a paid customary day, or its credited part:
- **the credited part nets off hours worked:** the credit is at most the day's credit basis minus the hours worked that day (R-7 as widened), so working on the day never earns both the credit and the hours;
- **a vacation or særlige feriedage booking consumes nothing** for the credited part. This extends D9's rule for søgnehelligdage (`ADR-043:164-165`), and so ends ADR-032 D7's limitation for these days too (`ADR-032:47`);
- **a sick day earns no second credit;**
- **ordered hours** on the day follow D-4 = (c)'s decision;
- the domain expert confirms the worked-day case.

**D-12.P6, a closure day earns nothing. Accepted, with D-12.2 item 3.**
- Skema shows it. The employee chooses how to cover it: vacation, særlige feriedage, time off in lieu (D-5) or flex. If nothing is registered, the day's norm is unmet and flex covers it, as on any working day. Nothing is booked automatically.
- *Build detail:* the send-month check's expected-working-days query (`ApprovalEndpoints.cs:2116-2127`) keeps a closure day as a working day.

**D-12.P7, the § 13 windows are separate. Accepted.** Grundlovsdag from 12:00 and 24 December from 14:00 (D9 kind 2) are unchanged by any custom. Ordered work in them still earns +25 % (D-4.P3); a custom changes the credit, not the supplement.

**D-12.P8, a configuration, not a hand-off. Accepted.** No HRP row is proposed: the register already treats configuration acts this way (HRP-016 ruled not a hand-off; HRP-017 same-actor work in progress, `hr-follow-up-process-register.md:141-145`).

**D-12.P9, the demo seed sets example customs. Accepted.** The demo institutions get example customs, so the feature can be seen (ruling 2's pattern for flex caps).

**Dependencies, all settled.**
- **R-1 (adjudicated):** the table's home.
- **R-6 with batch-2 ruling 4, and R-7 with batch-3 ruling 3 (adjudicated):** the credit's basis, and its bound by the hours worked.
- **D-7 = (b):** P3's arithmetic takes the office-hours form where set, else 7.4 h × fraction.
- **D-8a = (b):** a custom can be scoped to an agreement, not to a staff group (P1).
- **D-5 (told):** a closure day can be covered by time off in lieu. That is a use of D-5's account, not a dependency.
- **D-4 = (c):** ordered work on a customary day follows D-4's decision (P5).
- **D-1 = (b):** the credit changes flex, so it lands in **stage 1**, with D9's calendar split and D1's monthly norm. The table lands in D-6's config stage.

**What the owner should know** (D-12.5).
- **These days are customs, not holidays,** so none lowers the norm. A paid customary day is paid absence the system credits, so flex does not move.
- **Each institution records its own,** dated.
- **1 May is "on request".**
- **On a paid customary day a booked vacation day is not used up.**
- **The supplement windows on Grundlovsdag and 24 December stay,** whatever the custom.
- **Grundlovsdag stops being a public holiday in StatsTid** (D9, already decided): **without a recorded custom, 5 June is an ordinary working day.**
- **No HR hand-off:** recording a custom is configuration.

**What the S148 build gets from each answer.**
- **(c):**
  - the dated table and its editor (LocalAdmin), audited and frozen into the manifest (D-6's config stage), with an optional agreement scope;
  - the credit for paid days in stage 1, in both modes;
  - the closure-day notice in Skema (`ux`), and the send-month check keeping a closure day as a working day;
  - the same-day rules (P5), bounded by R-7 as widened;
  - demo customs;
  - the ADR-032 consumption change for the credited part.
- **(a):** the same without closure days.
- *(No build line for (b) or (d): neither is offered.)*

**The ADR-043 text it rewrites.**
- F-9 (`:322-328`) becomes a decision.
- D9's kind (3) (`:152-155`) names the table and the credit.
- D9's vacation-on-a-holiday sentence (`:164-165`) extends to paid customary days.
- **Outside ADR-043:**
  - ADR-032 D7 (`ADR-032:47`): the consumption rule extended;
  - ADR-017: the table beside the profile;
  - SYSTEM_TARGET §G item 4 (local agreements, `SYSTEM_TARGET.md:170`) needs no change;
  - **`SYSTEM_TARGET.md:86`** ("Calendar context (public holidays)") **gains the third kind of day at acceptance** (D-12.2 item 5), with the other SYSTEM_TARGET text owed to the owner.
- The recorded proposals become ADR text when this question is answered.

**Waits on.** Nothing: it is asked now, after D-5 is told (the adjudicator's order).

---
*Revision history.*
- Draft 1 (2026-10-29): first brief, at `26b70a08`, on the adjudicator's ruling that its dependency (R-1) is settled.
  - **Options:** the ADR's three are kept, plus (d) "not modelled". (b) is proposed as not offered.
  - **Nine recorded proposals** are proposed: storage beside the profile; credit not norm cut; part days; the given-to-everyone and on-request modes; one credit per day; closure days; the § 13 windows; configuration, not a hand-off; demo customs.
  - **No dependency on D-4, D-5 or D-7's answers** for the question itself; P3's arithmetic is written for both D-7 answers.
- Draft 2 (2026-10-29, re-checked at `bd5a3d3f`): the batch-3 adjudication applied (`.claude/reviews/S147-D-batch3-adjudication.md`, RULED-WITH-QUESTIONS). Status: adjudicated, being asked; no answer is recorded here.
  - **D-12.1 / ruling 2:** the settled wording and options are copied verbatim, (c) first, then (a). (b) and (d) are not offered, each with its reason, and the prose says both. The options list, the lean and the build list drop (d); the "(d) build line" and the "Under (d)" ADR line are removed.
  - **D-12.2 item 1:** D-4 = (c) and D-7 = (b) are written as facts. P3's arithmetic is the office-hours form where set, else 7.4 h × fraction. The header says "after D-5 is told".
  - **Item 2:** P4's two modes stay, with the reason reworded: two institutional facts (granted to all; on request, which the leader may refuse insofar as the work allows, decided in month approval); under widened R-7, "given to everyone" cannot double-pay.
  - **Item 3:** P6 gains the send-month check's build detail (`ApprovalEndpoints.cs:2116-2127`).
  - **Item 4:** P1 gains its scope: an agreement scope fits; a staff-group scope has no machinery under D-8a = (b).
  - **Item 5:** `SYSTEM_TARGET.md:86` gains the third kind of day at acceptance.
  - **D-12.3:** the lean (c) is confirmed.
  - **D-12.4:** P1–P9 are accepted. P4's reason is reworded, and P5 is extended (the credited part nets off hours worked; no vacation or særlige feriedage consumption for the credited part; D9 extended; ADR-032 D7).
  - **D-12.5:** "What the owner should know" is replaced by D-12.5.
  - **Ruling 3:** R-7 widened to every credit-class type is cited in P4 and P5.
