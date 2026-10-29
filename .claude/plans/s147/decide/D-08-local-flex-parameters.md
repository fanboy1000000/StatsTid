# D-8 — F-5: each institution's own flex rules, and five related items (the parent brief)

*Brief, draft 2, 2026-10-29: adjudicated (`.claude/reviews/S147-D-batch2-adjudication.md` § D-8.4, § D-8.5 and rulings 2–5). Drafted by `planner` (Opus 5.5). Code facts were verified at `61d6a98a` and re-checked at `26b70a08`; no file under `src/`, `docker/`, `frontend/` or `tools/` changed in between. It builds on D-6 = (a), D-1 = (b), D-9 = (a), D-3 = (c) and D-11 = (b), all answered 2026-10-29.*

**What this file is.** D-8 bundles F-5 with five related items. Asked as one, it would break the one-question-at-a-time rule, so the adjudicator confirmed a split (ruling 5):
- **Two owner questions, each in its own brief, asked one at a time:**
  - **D-8a**, `D-08a-flex-membership.md`: can an institution's flex rules treat groups of its staff differently? Lean (b): an HR-recorded, dated "on the flex scheme: yes / no" per person.
  - **D-8b**, `D-08b-ac-sick-day-default.md` (Q-1): AC's sick-day credit default. Lean (a): the HK/PROSA rule as a labelled default.
- **Seven recorded proposals, R-1..R-7, held here.** They are adjudicated, told to the owner, and not asked. Four of them were amended by the adjudicator.

| # | Item | Disposition | Where |
|---|---|---|---|
| 1 | F-5's per-group sub-question (ADR-043 calls it "an S147 sub-question", `:295`) | **Asked: D-8a** | `D-08a-flex-membership.md` |
| 2 | **Q-1**: AC's sick-day credit default (RR-1; `RESEARCH-s147-adjudication.md:24`) | **Asked: D-8b** | `D-08b-ac-sick-day-default.md` |
| 3 | F-5's storage home | R-1, accepted | below |
| 4 | The parameter list and its defaults | R-2, accepted with five amendments (ruling 2) | below |
| 5 | QUAL-219: forfeiture above the cap | R-3, accepted | below |
| 6 | The leaver's last closing | R-4, accepted | below |
| 7 | QUAL-225: an explicit opening balance | R-5, a recorded design proposal (ruling 3) | below |
| 8 | The tenth parameter | R-6, widened to the **paid-absence credit basis** (ruling 4) | below |
| 9 | QUAL-231's partial-day residual | R-7, accepted with QUAL-237's coupling and a second direction | below |

**Why it matters.**
- **There is no central state flex agreement.** Every institution's flex scheme is its own local agreement (PAV 19.5.1; `docs/references/agreement-cells-reverification-research.md:624`). The schemes differ in:
  - plus caps, from 37 h to 100 h, and 200 h for field staff;
  - minus caps, from 7.4 h to 37 h;
  - whether the cap is tested monthly or yearly;
  - what happens above the cap: forfeited, or kept by a manager's exception;
  - how a leaver's balance ends;
  - who is outside flex.
- **StatsTid models almost none of it.** Today it has one plus cap per institution and agreement and no minus cap. It pays out everything above the cap automatically, and it does nothing with a leaver's balance.
- **The local rules now reach pay.** D-6 = (a) made local values reach pay; D-8 decides what those values are.
- **Who notices:** every employee on flex (their balance), leaders (exceptions), HR (leavers, opening balances, who is on flex), and payroll.

**What StatsTid does today** (verified).
- **One typed local profile per institution, agreement and OK version.**
  - It is dated and audited, with one active row per key (`docker/postgres/init.sql:856-872`; the unique active index `:874-876`; audit `:885-895`).
  - It holds five overridable values (`:864-868`): `weekly_norm_hours`, `max_flex_balance`, `flex_carryover_max`, `max_overtime_hours_per_period`, `overtime_requires_pre_approval`.
  - It replaced the older key + JSON table `local_configurations` (`:803-822`; the comment at `:851-852`, ADR-017).
  - The Backend reads the *current* profile, not the one in force on a date (`src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs:185`), and only for screens and booking (QUAL-204). D-6 = (a) changes that.
  - **`flex_carryover_max` has no reader** in the Rule Engine or Payroll (a search of `src/RuleEngine` and `src/Integrations` finds none). It is dormant (D-8a.2 item 2).
  - **The demo seed writes no `local_agreement_profiles` row** (a search of `tools/` finds none) (D-8a.2 item 2).
- **The central caps have no source.** HK is 100 h, PROSA 120 h and AC 150 h, in OK24 and OK26 alike (`src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs:46`, `:77`, `:23`; `:131`, `:162`, `:108`). PAV 19.5.1 says there is no central flex agreement.
  - SYSTEM_TARGET §G says a local cap must not exceed the central one (`SYSTEM_TARGET.md:174`), and that the maximum flex balance is set "within central limit" (`:168`).
  - The only code that checks that is `ConfigResolutionService.ValidateLocalOverride`, marked `[Obsolete]` (`:324-326`). It has no production caller; only `tests/StatsTid.Tests.Unit/Sprint12AgreementConfigTests.cs` calls it. The profile PUT checks no ceiling either.
- **The flex settlement.**
  - One plus cap, and no minus cap anywhere.
  - Everything above the cap becomes an automatic `FLEX_PAYOUT` line (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/FlexBalanceRule.cs:44-48`, `:68-82`; QUAL-219, `docs/operations/quality-finding-register.md:443`).
- **A leaver's balance just stops.**
  - The leaving month settles like any other, and its closing stays on the ledger.
  - A stretch of employment that begins after a month with no employed day opens at 0 (`REHIRE`; `src/Integrations/StatsTid.Integrations.Payroll/Services/FlexLedgerOpening.cs:44`, `:91-94`).
  - Nothing records what became of the leaver's balance.
- **Opening balances.**
  - An employee's first ledger month opens at 0 (`LEDGER_START`; `FlexLedgerOpening.cs:35-36`). No one can enter an opening balance (QUAL-225, `quality-finding-register.md:449`).
  - Under S147's T6 (OQ-3 = (a) + (i)), a month before the ledger start is paid with flex "not tracked" (`.claude/refinements/REFINEMENT-s147-defects-and-model-rulings.md:411-416`).
- **Sick days earn no credit yet.** At `26b70a08` a `SICK_DAY` gets no flex credit (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/AbsenceRule.cs:34-45`; QUAL-231, `quality-finding-register.md:455`).
  - S147's TASK-14708 (wave 2a, **not on master** at `26b70a08`) credits it for every agreement. For AC it is a labelled default pending Q-1 (`.claude/plans/s147/TASK-14708.md:17`, `:38`).
- **A partly worked sick day can be over-credited.** The Skema save caps a day's *absence* hours at the day's amount, but it never counts the hours worked that day (`src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:924-1003`, the sum at `:968`; in-lock `:1940-1989`, the sum at `:1954`).
  - So 5 h worked plus 7.4 h sick passes, and the day is credited 12.4 h.
  - The same check counts only the cells in one save (QUAL-237, `quality-finding-register.md:461`).
- **Everyone is on flex.** No employee can be marked as outside the institution's flex scheme.

**What the sources say.**
- **C6, a sample of seven state agreements plus PAV's model** (`agreement-cells-reverification-research.md:622-648`). Confidence is MEDIUM: the sample is small and university-heavy, and two of the seven are historical.
  - SDU 2026 (`:629`), DTU (`:630`) and the AU template (`:631`) each exclude groups: special and chief consultants, managers, flex-job staff, fuldmægtige, fixed-roster staff.
  - SDU and DTU both cap plus hours at **37 h** (`:629`, `:630`).
  - AU Foulum (2014; status after 2017 unknown) gives field and stable staff 200 h instead of 100 h (`:632`).
  - Several let a manager agree an individual exception: SDU 3.4, AU "unless agreed with the manager", and DTU "case by case".
  - The verifier's nine parameters are listed at `:638-646`. For leaving (`:642`) the sample ranges over:
    - taken during notice, else forfeited (DTU);
    - no payout unless agreed (SDU, AU);
    - cash at 1/1924 (SKAT 2008, historical);
    - transfer to a new state employer (SKAT 2008).
    - On a deficit at leaving, AU allows a pay deduction.
- **Forfeiture is the starting point.** Surplus above the cap *"bortfalder som udgangspunkt"* (V7, PAV 19.5.8, `docs/references/overtime-flex-model-research.md:37`). PAV names one exception, transfer to the overtime account; local agreements add others.
- **The paid-absence credit** (R1, `docs/references/sick-day-credit-and-agreement-texts-research.md`).
  - **HK and PROSA:** § 7 nr. 2 (`:104-106`): the day's planned hours, else 7.4 h × fraction. It covers every paid-absence day: its remark lists sick days, time off in lieu, holiday days and paid leave.
  - **Local agreements may depart from it:** § 3 (`:135`), and PAV 19.5.13's *kan* (`:132`). Hence a per-local-agreement parameter with § 7 nr. 2 as the default (`:73`, `:140`, `:398`).
  - **AC-agreement staff:** there is no central text (PAV 19.3.4, `:159`). That is D-8b.
  - **A partly worked sick day:** max(worked, planned), MEDIUM confidence. It is an analogy that a local agreement may change (`:204-209`).

## The recorded proposals (adjudicated, told to the owner, not asked; D-8.4)

All seven are listed in `../ADR-043-working.md`, "Provisional items and recorded proposals".

**R-1. Where the rules live: F-5's option (a). Accepted.**
- **The proposal:** typed, dated columns on `local_agreement_profiles`, resolved for each day and frozen into the manifest under D-6 = (a).
- **Not (b), `local_configurations`:** ADR-017 retired that key + JSON table in favour of the typed profile (`init.sql:851-852`).
- **Not (c), a new table for the flex parameters:** it would be a third home for local rules.
- **What D-6 = (a) changed:** F-5 (a)'s own caveat, "screens and booking only, not into pay" (`ADR-043:291-292`), no longer applies.
- **What remains:** per-group values do not fit one row per key. That is D-8a.
- **D-12's customary days off are a dated table beside the profile.** They are a list of days per institution, not a single value. This is what made D-12 draftable.

**R-2. The parameter list, each with a stage. Accepted with five amendments** (ruling 2 and D-8a.2 items 2–3).

| Parameter (C6 item) | When no local value is set | Its basis | Stage (D-1 = (b)) |
|---|---|---|---|
| Settlement period (1) | calendar month. **S148 builds one value, `month`** (amendment ii) | § 4 stk. 2 remark, *"1 måned, medmindre andet er aftalt"* (V1); D-9 = (a) for AC | D-6's config stage, then stage 1 |
| Cap-test period (1) | the settlement period. **`flex_carryover_max` retires, or becomes this parameter's form** (amendment iv) | PAV 19.5 "typically a month"; AU Foulum tests at year end | stage 1 |
| Plus cap (2) | **none: no central cap** (amendment i, ruling 2). Until the institution sets one, nothing is forfeited and the month's calculation records "no flex cap configured" | none: there is no central flex agreement (PAV 19.5.1) | stage 1 |
| Minus cap (2) | none, as today | the sample gives values, not what happens on a breach | stage 1. A breach is recorded and shown to the leader, never deducted automatically |
| Daily credit cap (3) | none | AU template: 13 h | stage 1 |
| Above-cap action (4) | forfeit at the cap test, recorded (R-3) | V7 | stage 1. "Transfer to the overtime account" waits for D-5 (stage 2). "1:1 cash by agreement" is the only remaining source of a `FLEX_PAYOUT` line. **"A manager's exception" is a dated record for one person, decided by someone with approval authority over that person and never on the decider's own balance; it gets an HRP row** (amendment v) |
| Leaving action (5) | R-4 | C6 `:642` | stage 1 |
| Office hours (6) | off | D-7 | the credit in stage 1; the filter in stage 2 |
| Core hours, *fikstid* (6) | **not modelled for pay** (D-7.P6) | a presence rule, with no arithmetic | — |
| Excluded groups (7) | D-8a | C6 `:644` | D-6's config stage |
| Fuldmægtige's standby interaction (8) | — | V9: 20 h a quarter; SKAT's 50/50 split | stage 3, with AC claims |
| Suspension in peak periods (9) | none | AU § 14; SKAT § 12 | last; its mechanics go to the expert |
| **The paid-absence credit basis (10th)** | § 7 nr. 2 (R-6) | R1; RR-12; ruling 4 | D-6's config stage, then stage 1 |
| **`MaxOvertimeHoursPerPeriod`** | none: **an optional warning over decided hours** (amendment iii; D-4.P6) | SYSTEM_TARGET §L's ceiling (`:381-384`), reduced to a warning | stage 2, with D-4's decisions |
| AC travel cap, *"medmindre andet aftales lokalt"* (from D-3's brief) | 13 h per døgn | CIR1H 9333 § 13 stk. 6 | with D-3's later stage |

- **Amendment (i), the central flex caps are absent, not defaults** (ruling 2). This is told to the owner, not asked.
  - **The principle that stands:** SYSTEM_TARGET §G says local rules never override a centrally negotiated rule. That stands for +50 %, +25 % and § 14.
  - **Why the cap is different:** there is no central flex agreement (PAV 19.5.1). So the "central MaxFlexBalance" at `SYSTEM_TARGET.md:174` and "(within central limit)" at `:168` rest on a false premise, and the code never enforced them. This is a factual correction. It is told in D-8a's prose and approved with the other SYSTEM_TARGET text at acceptance (ADR-043 `:219`).
  - **Why no default number:** forfeiting at a number nobody agreed is unsourced arithmetic with a consequence. "No cap until the institution sets one" has none.
  - **So:**
    - caps live only in the local profile;
    - the central fields become nullable and are seeded null;
    - a month with no cap records "no flex cap configured";
    - the profile editor shows the caps as unset;
    - **the demo seed sets caps for the demo institutions** (37 h, as SDU and DTU), because today it writes no profile row at all (D-8a.2 item 2).
  - **Given up:** an out-of-the-box number. The owner can ask for one back.
- **Amendment (ii), the settlement period** (D-8a.2 item 3). S148 builds it with the one value `month`. Other values are recorded for later, with the expert. D-9's "may set another settlement period under D-8" is honoured as a parameter, not as S148 arithmetic.
- **Amendment (iii):** `MaxOvertimeHoursPerPeriod` is an optional warning over decided hours, with no default (D-4.2 item 4).
- **Amendment (iv):** `flex_carryover_max` has no reader. It retires, or becomes the cap-test period's form.
- **Amendment (v):** the manager's exception is a dated record for one person. It is decided by someone with approval authority over that person, never on the decider's own balance (ruling 1's pattern), and it gets an HRP row.

**R-3. QUAL-219: forfeiture is the default, and it is recorded. Accepted.**
- At the cap test, hours above the cap are forfeited, recorded in the month's calculation, and visible to the employee and HR (ADR-043 D6, `:119-127`; V7).
- The automatic `FLEX_PAYOUT` retires in stage 1. It survives only as the configured "1:1 cash by agreement" action.
- **Under ruling 2, nothing is forfeited until the institution has set its cap.**

**R-4. The leaver's last closing is settled in the leaving month, never carried to a rehire. Accepted** (the F-5 sub-question, `ADR-043:297`; option (a)).
- **The mechanism:** the leaving action settles the balance as a dated, recorded movement in the leaving month. A later rehire still opens at 0 (`REHIRE`), as today.
- **Told in one sentence:** no sampled agreement carries a balance across a gap. SKAT 2008's "transfer to a new state employer" is the receiving end of R-5.
- **The labelled defaults when no local value is set:**
  - **a positive balance not taken during notice is forfeited,** recorded and visible;
  - **a negative balance is recorded and shown to HR, not deducted.** AU's pay deduction is a "may", so it becomes a local option.
- **It never happens silently** (the standing rule: make hidden state visible).
- **A new HR hand-off,** with an HRP row next to HRP-011's leaver lifecycle.

**R-5. QUAL-225: an explicit, audited opening balance. A recorded design proposal** (ruling 3).
- **The design:** the ledger-start provenance gains an opening balance entered by HR, with a reason, an event, an audit row and an HRP row.
- **Why:**
  - it is the receiving end of "transfer to a new state employer" (C6 `:642`);
  - it closes T6's "not tracked" residual.
- **Its sprint is S148's refinement's.** The owner's OQ-3 answer said remedy (ii) "stays on the backlog and is revisited at D-8" (refinement `:617`). OQ-3 chose S147's scope; S148's scope is decided at S148's refinement.
- **The owner is told,** not asked.

**R-6. The tenth parameter: the paid-absence credit basis. Accepted, widened** (ruling 4).
- **The values:**
  - § 7 nr. 2, the default: the day's planned hours, else 7.4 h × fraction;
  - the weekday's office hours, only under D-7 = (b).
- **It covers every paid-absence day,** not only sick days. § 7 nr. 2 credits *"dage med ret til fravær med løn"*. Its remark lists sick days, time off in lieu, holiday days and paid leave (`sick-day…research.md:104-106`), and PAV 19.5.13 says *"ferie- og sygedage"* (`:132`). Narrowing it has no source, and would make a Friday holiday and a Friday sick day worth different flex.
- **One served "planned hours" figure.** Every credit-class absence code (TASK-14708's classification) reads it, and ADR-032's divisor reads the same figure. That is a build item (D-7.2 item 4). If S148 builds it in steps, the sprint log names which classes read the basis.
- **The ADR change:** F-5's text gains RR-12's sentence, worded "paid-absence credit basis".

**R-7. QUAL-231's partial-day residual: a check when Skema saves. Accepted, with QUAL-237's coupling and a second direction.**
- **The rule:** a day's sick hours may not exceed the day's credit basis minus the hours worked that day, floored at 0. That is the max(worked, planned) reading (RR-3), labelled MEDIUM, and a local agreement may differ.
- **Both directions are judged:**
  - a sick-hours save on a day that already holds worked hours;
  - a later work save on a day that already holds sick hours.
  - *Or, instead of the check,* the engine computes the credit as min(stored sick hours, max(0, planned − worked)).
- **Built with QUAL-237's lesson:** it judges the day's stored state plus the posted cells, not the posted cells alone. The two are fixed together.
- **Where it lands:** stage 0, in the Backend with no payroll marker, on the central basis. It reads R-6's parameter once D-6's config stage lands.
- *An open point for the build input, not widened here:* the rule as ruled covers sick hours. Whether the same check covers every credit-class absence (for example, a vacation day partly worked) is left to the build input.

## What it builds on (the owner's answers, and how they narrow it)

- **D-6 = (a):** every local value is dated, resolved for each day and frozen into the manifest. This settles the "not into pay" premise of F-5 (a) and QUAL-204. Storage (R-1) becomes a mechanism, not a question.
- **D-1 = (b):** each parameter lands in the stage that first needs it (R-2's table). The config stage comes first.
- **D-9 = (a):** the owner was told that "a local agreement may still set another settlement period under D-8". That is honoured as a parameter, with `month` the only value S148 builds (amendment ii). AC's sick-day default (D-8b) uses the same analogy as D-9.4.
- **D-3 = (c):** AC's travel cap "unless agreed locally" joins the list and lands with D-3's later stage.
- **D-11 = (b):** D-8a's lean copies its pattern, a dated fact recorded by HR. Staff off flex still have their ordered hours routed by the regime.
- **D-7 (queued):** office hours are parameter 6, and the paid-absence credit basis (R-6) can override them.
- **D-4 (being asked):** `MaxOvertimeHoursPerPeriod` moves here as an optional warning over decided hours (D-4.P6). It reads decided hours under either of D-4's offered answers.

## What the owner should know (D-8.5)

*The Orchestrator tells these with D-8a, the first D-8 question. D-8b's own points are in its brief.*
- **Flex rules become each institution's own.**
- **There is no central flex cap.** The 100/120/150 h were never sourced.
  - Until an institution sets its cap, nothing is forfeited, and the month's calculation says "no flex cap configured".
  - Once a cap is set, hours above it are forfeited and shown. They are never paid out unless the institution's agreement says so.
- **A leaver's balance is settled, and shown, in the leaving month.**
- **Who is on flex becomes a dated fact that HR records** (D-8a (b)).
- **An HR-entered opening balance is designed;** its sprint is S148's plan's.
- **The AC sick-day rule is a labelled analogy** (D-8b).
- **New HR hand-offs, each with an HRP row:** flex membership, the leaver's settlement, the opening balance, and the cap exception.

## What the S148 build gets

- **From D-8a and D-8b:** see their briefs.
- **From the recorded proposals:**
  - *The config stage:*
    - the profile gains R-2's columns;
    - the central cap fields become nullable and are seeded null;
    - the profile editor shows unset caps;
    - the demo seed writes profile rows with caps.
  - *Stage 1:*
    - forfeiture recorded, or "no flex cap configured";
    - `FLEX_PAYOUT` retired except as a configured action;
    - the minus cap and the daily credit cap;
    - the leaver's settlement movement;
    - the opening balance, if S148's refinement schedules R-5;
    - the paid-absence credit basis, one served "planned hours" figure.
  - *Stage 2:* the transfer to the overtime account (D-5), D-7's filter, and `MaxOvertimeHoursPerPeriod`'s warning.
  - *Stage 3:* the standby interaction.
  - *Stage 0:* R-7's check in both directions, with QUAL-237.
  - SYSTEM_TARGET §G text (`:168`, `:174`), owed to the owner as a factual correction (ruling 2).

## The ADR-043 text it rewrites

- F-5 (`:286-297`) becomes a decision. It gains RR-12's sentence on the **paid-absence credit basis**, and the leaver sub-question is closed.
- D6's local-exceptions bullet (`:124-125`) names the parameter.
- D1's AC bullet, "a local other period possible (F-5)" (`:78-80`), names the settlement-period parameter.
- "What this ADR does not decide" (`:334`): QUAL-225 leaves the backlog as a recorded design proposal (R-5). Part-timers' fixed days off stay an expert item (`:335-336`).
- Consequences, "New hand-offs" (`:217-218`): the leaver, opening-balance, flex-scheme and exception rows.
- **Outside ADR-043:**
  - ADR-017: the profile gains columns;
  - SYSTEM_TARGET §G: the central-cap correction;
  - the routes for QUAL-219, QUAL-225, QUAL-231 (its residual) and QUAL-237;
  - HRP rows.

## Premises made moot by earlier answers

- **F-5 (a)'s "not into pay" caveat** (`:291-292`) is moot under D-6 = (a).
- **The profile's `weekly_norm_hours` override, and its Monday alignment rule** (ADR-017 D9a; `src/Backend/StatsTid.Backend.Api/Validators/ProfileAlignmentValidator.cs:10-11`, `:22`), lose their premise under D1's monthly norm. § 4 lets a local agreement change the norm *period*; nothing in the sample changes its *hours*. Retiring the override is proposed for the build input, as an expert item.
- **The profile's two overtime columns:** `overtime_requires_pre_approval` retires (D-4.P6). `max_overtime_hours_per_period` becomes an optional warning over decided hours (amendment iii).
- **AC's annual-norm variants** (D-9.4's gap) are a norm question. They stay in the build-input gap list, not in D-8a (D-8a.2 item 5).

**Waits on.** Nothing for the asking (D-6 is answered). Asking order: D-8a, then D-8b, one at a time, after D-4 and D-7.

---
*Revision history.*
- Draft 1 (2026-10-29): first brief, after D-6 = (a). The bundle is split into two owner questions (D-8a: groups; D-8b: Q-1) and seven recorded proposals: R-1 storage, R-2 the parameter list, R-3 QUAL-219, R-4 the leaver, R-5 QUAL-225 (flagged), R-6 the tenth parameter, R-7 QUAL-231's residual. One SYSTEM_TARGET point is flagged: the central flex caps become defaults, not ceilings.
- Draft 2 (2026-10-29): the adjudication applied (`.claude/reviews/S147-D-batch2-adjudication.md`).
  - **Ruling 5:** the split stands. D-8a and D-8b now have their own briefs (`D-08a-flex-membership.md`, `D-08b-ac-sick-day-default.md`), and this file becomes the parent, holding R-1..R-7.
  - **R-1:** accepted, with D-12's table noted.
  - **R-2:** accepted with five amendments:
    - (i) no central cap (ruling 2), with its consequences for the fields, the seed, the editor and the month's record;
    - (ii) `month` only in S148;
    - (iii) `MaxOvertimeHoursPerPeriod` added as an optional warning;
    - (iv) `flex_carryover_max` retires or becomes the cap-test form;
    - (v) the manager's exception as a dated, four-eyes record with an HRP row.
  - **R-3:** accepted, with "nothing forfeits until a cap is set". **R-4:** accepted, told in one sentence.
  - **R-5:** a recorded design proposal (ruling 3). **R-6:** widened to the paid-absence credit basis (ruling 4). **R-7:** accepted, with QUAL-237's coupling and the second direction.
  - **D-8a.2:** items 2, 3 and 5 are folded here (the dormant `flex_carryover_max`; the demo seed's missing profile rows; the settlement period; the AC annual-norm gap moved to the build input).
  - **D-8.5:** "What the owner should know" is replaced by D-8.5.
  - **Other:** the obsolete-validator citation is corrected to `ConfigResolutionService.cs:324-326`, the `[Obsolete]` attribute. The re-check sha is `26b70a08`.
