# D-8 — F-5: each institution's own flex rules, and five related items

*Brief, draft 1, 2026-10-29: not adjudicated. The owner has not been asked. Drafted by `planner` (Opus 5.5). Code facts verified at `61d6a98a` (master HEAD when the work began); HEAD `f37d7cd3` at writing changed no file under `src/`, `docker/` or `frontend/`. It builds on D-6 = (a), D-1 = (b), D-9 = (a), D-3 = (c) and D-11 = (b), all answered 2026-10-29.*

**D-8 is more than one owner decision.** The index folds six things into it (`README.md`):
- F-5 itself;
- the leaver's last closing;
- QUAL-219;
- QUAL-225;
- the tenth parameter (RR-12);
- Q-1, and QUAL-231's partial-day residual.

Asked as one, it would break the one-question-at-a-time rule. **The planner proposes two owner questions, asked one at a time (D-8a, then D-8b), and seven recorded proposals.** The adjudicator rules the split.

| # | Item | Proposed as | Why |
|---|---|---|---|
| 1 | **F-5's per-group sub-question**: can an institution's rules treat groups of staff differently, starting with leaving some out of flex? | **Asked: D-8a** | A genuine product fork with real costs either way. ADR-043 itself names it "an S147 sub-question" (`:295`) |
| 2 | **Q-1**: AC's sick-day credit default | **Asked: D-8b** | The research adjudication made it the owner's (RR-1; `RESEARCH-s147-adjudication.md:24`). No central text exists, so only the owner can choose the default |
| 3 | F-5's storage home: options (a), (b), (c) | Recorded, R-1 | Clear inside the architecture: ADR-017 already moved local rules into the typed profile, and D-6 = (a) fixes how they reach pay |
| 4 | The parameter list and its defaults (C6's nine, the tenth, AC's travel cap from D-3) | Recorded, R-2 | A catalogue with sourced or labelled defaults. One point inside it needs SYSTEM_TARGET text (see R-2) |
| 5 | QUAL-219: forfeiture above the cap | Recorded, R-3 | Already ADR-043 D6's direction (V7), under the owner's OQ-3 = (b) |
| 6 | The leaver's last closing | Recorded, R-4 | The sources settle the shape. The defaults are labelled |
| 7 | QUAL-225: an explicit opening balance | Recorded, R-5, **flagged** | A scope addition the owner deferred in S147 (OQ-3 chose remedy (i) over (ii)). The adjudicator may make it a question |
| 8 | The tenth parameter: the sick-day credit basis | Recorded, R-6 | RR-12 directs it, and F-5 gains one sentence |
| 9 | QUAL-231's partial-day residual | Recorded, R-7 | A save-time check. The reading is MEDIUM confidence and labelled |

**The two questions, one sentence each.**
- **D-8a.** Every sampled local flex agreement leaves some staff out of flex: special and chief consultants, managers, flex-job staff. One also gives a staff group its own cap. Should an institution's flex rules in StatsTid be able to treat groups of its staff differently, or is one set of rules per institution and agreement enough?
- **D-8b (Q-1).** No central text says how a paid sick day counts in flex for staff on the AC agreement. Which default should StatsTid use?
  - credit it like HK and PROSA (the day's planned hours, else 7.4 h × fraction), labelled as a default a local agreement can change;
  - refuse to calculate the month until the institution sets a rule; or
  - credit nothing?

**Why it matters.**
- **There is no central state flex agreement.** Every institution's flex scheme is its own local agreement (PAV 19.5.1; `docs/references/agreement-cells-reverification-research.md:624`). The schemes differ:
  - plus caps from 37 h to 100 h, and 200 h for field staff;
  - minus caps from 7.4 h to 37 h;
  - the cap tested monthly or yearly;
  - above the cap: forfeited, or kept by a manager's exception;
  - how a leaver's balance ends;
  - who is outside flex.
- **StatsTid models almost none of it.** Today it has one plus cap per institution and agreement and no minus cap. It pays out everything above the cap automatically, and it does nothing with a leaver's balance.
- **The local rules now reach pay.** D-6 = (a) made local values reach pay, and this question decides what those values are.
- **Who notices:** every employee on flex (their balance), leaders (exceptions), HR (leavers, opening balances, who is on flex), and payroll.

**What StatsTid does today** (verified).
- **One typed local profile per institution, agreement and OK version.**
  - It is dated and audited, with one active row per key (`docker/postgres/init.sql:856-872`; the unique active index `:874-876`; audit `:885-895`).
  - It holds five overridable values (`:864-868`): `weekly_norm_hours`, `max_flex_balance`, `flex_carryover_max`, `max_overtime_hours_per_period`, `overtime_requires_pre_approval`.
  - It replaced the older key + JSON table `local_configurations` (`:803-822`; the comment at `:851-852`, ADR-017).
  - The Backend reads the *current* profile, not the one in force on a date (`src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs:185`), and only for screens and booking (QUAL-204). D-6 = (a) changes that.
- **The central caps have no source.** HK is 100 h, PROSA 120 h and AC 150 h, in OK24 and OK26 alike (`src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs:46`, `:77`, `:23`; `:131`, `:162`, `:108`). PAV 19.5.1 says there is no central flex agreement.
  - SYSTEM_TARGET §G says a local cap must not exceed the central one (`SYSTEM_TARGET.md:174`).
  - The only code that checks that is `ConfigResolutionService.ValidateLocalOverride` (`:327-343`), which is marked obsolete and has no production caller. Only `tests/StatsTid.Tests.Unit/Sprint12AgreementConfigTests.cs` calls it.
- **The flex settlement.**
  - One plus cap, and no minus cap anywhere.
  - Everything above the cap becomes an automatic `FLEX_PAYOUT` line (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/FlexBalanceRule.cs:44-48`, `:68-82`; QUAL-219, `docs/operations/quality-finding-register.md:443`).
- **A leaver's balance just stops.**
  - The leaving month settles like any other, and its closing stays on the ledger.
  - A stretch of employment that begins after a month with no employed day opens at 0 (REHIRE) (`src/Integrations/StatsTid.Integrations.Payroll/Services/FlexLedgerOpening.cs:44`, `:91-94`).
  - Nothing records what became of the leaver's balance.
- **Opening balances.**
  - An employee's first ledger month opens at 0 (`FlexLedgerOpening.cs:35-36`). No one can enter an opening balance (QUAL-225, `quality-finding-register.md:449`).
  - Under S147's T6 (OQ-3 = (a) + (i)), a month before the ledger start is paid with flex "not tracked" (`.claude/refinements/REFINEMENT-s147-defects-and-model-rulings.md:411-416`).
- **Sick days.** At this HEAD a `SICK_DAY` gets no flex credit (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/AbsenceRule.cs:34-45`; QUAL-231, `quality-finding-register.md:455`).
  - S147's TASK-14708 (wave 2a, **not yet on master**) credits it for every agreement. For AC it is a labelled default pending Q-1 (`.claude/plans/s147/TASK-14708.md:17`, `:38`).
- **A partly-worked sick day can be over-credited.** The Skema save caps a day's *absence* hours at the day's amount, but it never counts the hours worked that day (`src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:924-1003`, the sum at `:968`; in-lock `:1940-1989`, the sum at `:1954`).
  - So 5 h worked plus 7.4 h sick passes, and the day is credited 12.4 h.
  - The same check counts only the cells in one save (QUAL-237, `quality-finding-register.md:461`).
- **Everyone is on flex.** No employee can be marked as outside the institution's flex scheme.

**What the sources say.**
- **C6, a sample of seven state agreements plus PAV's model** (`agreement-cells-reverification-research.md:622-648`; MEDIUM confidence, small and university-heavy, two historical).
  - SDU 2026 (`:629`), DTU (`:630`) and the AU template (`:631`) each exclude groups: special and chief consultants, managers, flex-job staff, fuldmægtige, fixed-roster staff.
  - AU Foulum, 2014, status after 2017 unknown, gives field and stable staff 200 h instead of 100 h (`:632`).
  - Several let a manager agree an individual exception: SDU 3.4, AU "unless agreed with the manager", and DTU "case by case".
  - The verifier's nine parameters are listed at `:638-646`. The leaving action (`:642`) ranges over: taken during notice, else forfeited (DTU); no payout unless agreed (SDU, AU); cash at 1/1924 (SKAT 2008, historical); transfer to a new state employer (SKAT 2008). On a deficit at leaving, AU allows a pay deduction.
- **Forfeiture is the starting point:** surplus above the cap *"bortfalder som udgangspunkt"* (V7, PAV 19.5.8, `docs/references/overtime-flex-model-research.md:37`). PAV names one exception (transfer to the overtime account); local agreements add others.
- **The sick-day credit** (R1, `docs/references/sick-day-credit-and-agreement-texts-research.md`):
  - **HK and PROSA:** § 7 nr. 2 (`:104-106`). The day's planned hours, else 7.4 h × fraction.
  - **Local agreements may depart from it:** § 3 (`:135`) and PAV 19.5.13 *kan* (`:132`). Hence a per-local-agreement parameter with § 7 nr. 2 as the default (`:73`, `:140`, `:398`).
  - **AC-agreement staff:** no central text (PAV 19.3.4, `:159`). Sick days must still be counted into the flex account (PAV 19.5.9, `:163`), and the AC agreement gives full pay in sickness (§ 17, `:153`). The amount is left to the local agreement (`:164`). So 7.4 h × fraction is a default by analogy, *"an owner-ruled default, labelled as such"* (`:168`).
  - **A partly-worked sick day:** max(worked, planned), MEDIUM confidence, an analogy that a local agreement may change (`:204-209`).

## D-8a — can an institution's flex rules treat groups of its staff differently?

*The options.*
- **(a) One rule set per institution, agreement and OK version,** keyed as the profile is today.
  - *Gains:* the least to build, and no new step for HR.
  - *Gives up:* every group the local agreement treats differently. Staff the agreement leaves out of flex run on its flex rules anyway: they get a balance they do not have, and a deficit if they register little. AU Foulum's field-staff cap cannot be held.
- **(b) One rule set, plus a dated fact per person, recorded by HR: "on the institution's flex scheme: yes / no".**
  - Someone off the scheme keeps no flex balance, and no settlement or forfeiture runs for them. Their hours are still registered, and any ordered hours follow D-11's overtime regime.
  - *Gains:* it represents every *current* sampled agreement (SDU, DTU, the AU template).
  - *Gives up:* per-group values (AU Foulum's 200 h), recorded as a limitation. It also adds an HR step, with its own HRP row.
- **(c) Rule sets per staff group.** HR records a dated "flex group" for each person, seeded from position. Each group has its own values, and "not on flex" is one group's setting.
  - *Gains:* it also holds per-group caps.
  - *Gives up:* the most to build. Groups become part of the profile's key, and HR maintains a group for every person.
- *Considered, not proposed:* deriving the group from position or employment category alone. Flex-job status, and fuldmægtige on the standby allowance (a pay element StatsTid does not hold), are not positions. So HR records it, seeded from position: the pattern of D-11.4.

*The lean: (b).*
- Exclusion appears in every current sample. A per-group cap appears once, in a 2014 agreement whose status is unknown.
- (b) is the smallest answer that represents what institutions actually agree today.
- It follows the owner's own D-11 = (b) pattern: HR records a dated, audited fact on the employee, frozen into the manifest.
- (c) can come later without undoing (b): the yes/no fact becomes a group.
- *For the domain expert:* what "off the scheme" means for a deficit (nothing carried, nothing deducted is the proposal).
- *A related gap:* AC's annual-norm variants (D-9.4's gap) may belong here.

## D-8b — Q-1: AC's sick-day credit default

*The options* (RR-1; the research adjudication's wording, `.claude/reviews/RESEARCH-s147-adjudication.md:24`).
- **(a) The HK/PROSA rule as a labelled default:** the day's planned hours, else 7.4 h × fraction. S147 builds this (TASK-14708). A local agreement can change it through R-6's parameter.
  - *Gains:*
    - screen and payroll agree;
    - a whole sick day leaves an AC employee's flex unchanged;
    - it is the same analogy the owner accepted for AC's month norm (D-9.4).
  - *Gives up:* it applies a rule no AC text states.
- **(b) No default.** Payroll refuses any AC month with a sick day until the institution sets the parameter.
  - *Gains:* StatsTid never applies an unsourced rule.
  - *Gives up:* usability. No AC month with a sick day can be paid until every AC institution configures the parameter.
- **(c) Credit nothing.**
  - *Gives up:*
    - a sick AC employee loses a day's flex for being sick;
    - it runs against PAV 19.5.9 (sick days are counted into the flex account, `:163`) and AC § 17's full pay (`:153`);
    - QUAL-231's screen/payroll split returns for AC.
  - *Planner's note:* likely not a valid path (domain correctness). The adjudicator may drop it.

*The lean: (a)* (RR-1; plan `PLAN-s147.md:287`). It is the default by analogy the verifier recommends, it is already being built, and the local parameter keeps every institution free.

## Proposed recorded proposals (told, not asked; the adjudicator confirms or converts each)

**R-1. Where the rules live: F-5's option (a).**
- **The proposal:** typed, dated columns on `local_agreement_profiles`, resolved for each day and frozen into the manifest under D-6 = (a).
- **Not (b), `local_configurations`:** ADR-017 moved local rules out of that key + JSON table into the typed profile (`init.sql:851-852`).
- **Not (c), a new table:** it would be a third home for local rules.
- **What D-6 = (a) changed:** F-5 (a)'s own caveat, "screens and booking only, not into pay" (`ADR-043:291-292`), no longer applies.
- **What remains:** per-group values do not fit one row per key. That is D-8a.
- *For D-12:* customary days off are a dated list of days per institution, not a single value, so this "no new table" applies to the flex parameters, not to D-12's list.

**R-2. The parameter list, each with a central default and a stage.**

| Parameter (C6 item) | Default when no local value | Its basis | Stage (D-1 = (b)) |
|---|---|---|---|
| Settlement period (1) | calendar month | § 4 stk. 2 remark, *"1 måned, medmindre andet er aftalt"* (V1); D-9 = (a) for AC | D-6's config stage, then stage 1 |
| Cap-test period (1) | the settlement period | PAV 19.5 "typically a month"; AU Foulum tests at year end | stage 1 |
| Plus cap (2) | today's 100 / 120 / 150 h, **labelled unsourced** | none: no central flex agreement (PAV 19.5.1) | stage 1 |
| Minus cap (2) | none, as today; **labelled** | the sample gives values, not what happens on a breach | stage 1; a breach is recorded and shown to the leader, never deducted automatically |
| Daily credit cap (3) | none | AU template 13 h | stage 1 |
| Above-cap action (4) | forfeit at the cap test, recorded (R-3) | V7 | stage 1. Its "transfer to the overtime account" value waits for D-5 (stage 2). "1:1 cash by agreement" is the only remaining source of a `FLEX_PAYOUT` line. "A manager's exception" is a leader's dated, recorded decision for one person (an HRP row) |
| Leaving action (5) | R-4 | C6 `:642` | stage 1 |
| Office hours (6) | off | D-7 | the credit in stage 1; the filter in stage 2 |
| Core hours, *fikstid* (6) | **not modelled for pay** | a presence rule, no arithmetic | — |
| Excluded groups (7) | D-8a | C6 `:644` | D-6's config stage |
| Fuldmægtige's standby interaction (8) | — | V9: 20 h a quarter; SKAT's 50/50 split | stage 3, with AC claims |
| Suspension in peak periods (9) | none | AU § 14; SKAT § 12 | last; its mechanics go to the expert |
| **The sick-day credit basis (10th)** | § 7 nr. 2 | R1; RR-12 | D-6's config stage, then stage 1 (R-6) |
| AC travel cap, *"medmindre andet aftales lokalt"* (from D-3's brief) | 13 h per døgn | CIR1H 9333 § 13 stk. 6 | with D-3's later stage |

- **One point needs the owner, as SYSTEM_TARGET text.** With no central flex agreement, today's central caps are defaults, not ceilings. The planner proposes they become labelled defaults, and that §G's "local maxFlexBalance must be ≤ central" (`SYSTEM_TARGET.md:174`) be dropped for flex caps. As it stands, that rule would refuse a sourced local value (AU Foulum's 200 h against HK's 100 h).
  - The code does not enforce the rule today (its only check is obsolete and uncalled; see the facts).
  - This is a SYSTEM_TARGET change, so it is told to the owner and needs their approval, as D-3's travel line was. **The adjudicator may prefer to ask it.**

**R-3. QUAL-219: forfeiture is the default, and it is recorded.**
- At the cap test, hours above the cap are forfeited, recorded in the month's calculation, and visible to the employee and HR (ADR-043 D6, `:119-127`; V7).
- The automatic `FLEX_PAYOUT` retires in stage 1. The exceptions are values of the above-cap action (R-2).
- This is the ADR's direction under OQ-3 = (b), so it is told, not asked.

**R-4. The leaver's last closing is settled in the leaving month, never carried to a rehire** (the F-5 sub-question, `ADR-043:297`; option (a)).
- **The mechanism:** the leaving action settles the balance as a dated, recorded movement in the leaving month. A later rehire still opens at 0 (REHIRE), as today. Carrying the balance to a rehire, option (b), would let one balance be both settled and carried. No sampled agreement carries a balance across a gap; SKAT 2008's "transfer to a new state employer" is the receiving end of R-5.
- **The defaults when no local value is set,** both labelled:
  - **A positive balance not taken during notice is forfeited**, recorded and visible, consistent with D6 and with every current sample's "no payout unless agreed".
  - **A negative balance is recorded and shown to HR, not deducted.** AU's pay deduction is a "may" and becomes a local option.
- **It never happens silently** (the standing rule: make hidden state visible).
- **A new HR hand-off,** with an HRP row, next to HRP-011's leaver lifecycle.

**R-5. QUAL-225: an explicit, audited opening balance.** *Flagged: a scope addition the adjudicator may make a question.*
- **The proposal:** HR enters a dated opening flex balance on an employee's ledger-start month, recorded with its reason and audited, as a new ledger provenance. Built in stage 1, with the ledger.
- **Why:**
  - At the design target's go-live, every employee arrives with a balance from the previous system. Without this, everyone starts at 0, which is a wrong result.
  - It is the receiving end of "transfer to a new state employer" (C6 `:642`).
  - It closes T6's residual, where a month before the ledger start is paid "not tracked".
- **Cost:** an HR form, an event and an audit row, a provenance value, and an HRP row.
- **The tension:** the owner chose not to build it in S147 (OQ-3 = (i) over (ii), `REFINEMENT…:617`). That choice was about S147's scope, but S148's scope is the owner's too.

**R-6. The tenth parameter: the sick-day credit basis.**
- **The values:** § 7 nr. 2, the default (the day's planned hours, else 7.4 h × fraction), and the weekday's office hours (only with D-7 = (b)).
- **The ADR change:** F-5's text gains the sentence RR-12 directs.
- *Scope, flagged as in D-7.P3:* RR-12 says "sick-day". § 7 nr. 2 and PAV 19.5.13 cover every paid-absence day, holiday days included. This brief keeps RR-12's wording.

**R-7. QUAL-231's partial-day residual: a check when Skema saves.**
- **The rule:** a day's sick hours may not exceed the day's credit basis minus the hours worked that day, floored at 0. That is the max(worked, planned) reading (RR-3), labelled MEDIUM, and a local agreement may differ.
- **Where it lands:** stage 0, in the Backend with no payroll marker, on the central basis. It reads R-6's parameter once D-6's config stage lands.
- **Built with QUAL-237's lesson:** it judges the day's stored state plus the posted cells, not the posted cells alone. The two are best fixed together.

## What it builds on (the owner's answers, and how they narrow it)

- **D-6 = (a):** every local value is dated, resolved for each day and frozen into the manifest. This settles the "not into pay" premise of F-5 (a) and QUAL-204. Storage (R-1) becomes a mechanism, not a question.
- **D-1 = (b):** each parameter lands in the stage that first needs it (R-2's table). The config stage comes first.
- **D-9 = (a):** the owner was told that "a local agreement may still set another settlement period under D-8". So the settlement period is a local parameter for AC as well as HK and PROSA, and AC's sick-day default (D-8b) uses the same analogy as D-9.4.
- **D-3 = (c):** AC's travel cap "unless agreed locally" joins the list and lands with D-3's later stage.
- **D-11 = (b):** D-8a's lean copies its pattern (a dated, HR-recorded fact). Staff off flex still have their ordered hours routed by the regime.
- **D-7 (not yet asked):** office hours are parameter 6, and the credit basis (R-6) can override them.

## What the owner should know

- **Flex rules become each institution's own, with honest defaults.** Where no local agreement says otherwise, StatsTid uses defaults, and the unsourced ones (the caps) are labelled.
- **Hours above the cap are forfeited and shown, not paid.** Paying them out becomes an institution's explicit choice.
- **A leaver's balance is settled, and shown, in the leaving month.** By default unused plus hours lapse and minus hours are not deducted.
- **An HR-entered opening balance** is proposed (R-5) for go-live and for transfers.
- **New HR hand-offs, each with an HRP row:** who is on flex (D-8a (b)), a leaver's settlement, the opening balance, and a manager's cap exception.

## What the S148 build gets from each answer

- **D-8a (b):**
  - a dated, audited "on the flex scheme" fact on the employee, with an HR editor and an HRP row;
  - as-of resolution, frozen in the manifest beside fraction, position and regime;
  - off the scheme: no flex settlement, no forfeiture, no balance on screen;
  - the profile keeps one row per key.
- **D-8a (a):** nothing new; the limitation is recorded in the ADR.
- **D-8a (c):** the same fact as a group, and profile rows keyed by group.
- **D-8b (a):** TASK-14708's labelled default stays, and the label is recorded with R-6's default. **(b):** the config stage adds a refusal for AC months with a sick day and no configured basis. **(c):** the AC credit is removed, and the marker moves.
- **The recorded proposals:**
  - the profile gains R-2's columns (the config stage);
  - stage 1: forfeiture recorded and `FLEX_PAYOUT` retired except as a configured action; the minus cap and the daily credit cap; the leaver's settlement movement; the opening balance (if R-5 stands); the credit basis;
  - stage 2: the transfer to the overtime account, and D-7's filter;
  - stage 3: the standby interaction;
  - stage 0: R-7's check, with QUAL-237;
  - SYSTEM_TARGET §G text (`:168`, `:174`) owed to the owner.

## The ADR-043 text it rewrites

- F-5 (`:286-297`) becomes a decision. It gains RR-12's sentence on the credit basis, and the leaver sub-question is closed.
- D6's local-exceptions bullet (`:124-125`) names the parameter.
- D1's AC bullet, "a local other period possible (F-5)" (`:78-80`), names the settlement-period parameter.
- "What this ADR does not decide" (`:334`): QUAL-225 leaves the backlog, if R-5 stands. Part-timers' fixed days off stay an expert item (`:335-336`).
- Consequences, "New hand-offs" (`:217-218`): the leaver, opening-balance, flex-scheme and exception rows.
- **Outside ADR-043:**
  - ADR-017: the profile gains columns;
  - SYSTEM_TARGET §G;
  - the routes for QUAL-219, QUAL-225, QUAL-231 (its residual) and QUAL-237;
  - HRP rows.

## Premises made moot by earlier answers

- **F-5 (a)'s "not into pay" caveat** (`:291-292`) is moot under D-6 = (a).
- **The profile's `weekly_norm_hours` override, and its Monday alignment rule** (ADR-017 D9a; `src/Backend/StatsTid.Backend.Api/Validators/ProfileAlignmentValidator.cs:10-11`, `:22`), lose their premise under D1's monthly norm. § 4 lets a local agreement change the norm *period*; nothing in the sample changes its *hours*. Retiring the override is proposed for the build input, and it is an expert item.
- **The profile's two overtime columns:** `overtime_requires_pre_approval` retires under D-4.P6. `max_overtime_hours_per_period` becomes a warning over decided hours.
- **D-12's dependency** is on R-1 (where local rules live), which is settled when this brief is adjudicated, not when D-8a is answered. So D-12's brief can be drafted after D-8's adjudication.

**Waits on.** Nothing for the asking (D-6 is answered). Asking order: D-8a, then D-8b, one at a time.

---
*Revision history.*
- Draft 1 (2026-10-29): first brief, after D-6 = (a). The bundle is split into two owner questions (D-8a: groups; D-8b: Q-1) and seven recorded proposals: R-1 storage, R-2 the parameter list, R-3 QUAL-219, R-4 the leaver, R-5 QUAL-225 (flagged), R-6 the tenth parameter, R-7 QUAL-231's residual. One SYSTEM_TARGET point is flagged: the central flex caps become defaults, not ceilings.
