# D-3 — QUAL-232: which kinds of registered time count as worked time, and how much?

*Brief, draft 2, 2026-10-28: adjudicated (`.claude/reviews/S147-D-batch1-adjudication.md` § D-3). The owner has not been asked yet. Drafted by `planner` (Opus 5.5); facts verified at `e7478f24`, draft 2's added citations at `b2ece3a1` (no code changed between the two; the research dossier was filed in `b2ece3a1`).*

**The question in one sentence.** S148 will refuse on-call duty, call-ins and travel rather than pay them wrongly; that is already decided. Should StatsTid still be required to count them the way the state agreement says afterwards? That means on-call at home counting as a third of its hours, on-call at the workplace as three quarters, a call-in as at least 3 hours, and travel in full up to 13 hours a day. Or should the refusal be the end state?

**The question as it will be asked** (settled by the adjudicator, D-3.1; verbatim).

> *On-call duty, call-ins and travel time: S148's stage 0 will refuse them rather than pay them wrongly (already decided). Should StatsTid still be required to handle them afterwards, counting them as the state agreement does — on-call at home 1/3 of its hours, at the workplace 3/4; a call-in at least 3 hours; travel in full up to 13 hours per working day; for AC-agreement staff, travel only — built in a later stage, or should the refusal be the end state and the requirement dropped from the product definition?*

- **(c) Keep the requirement — refuse now, build the sourced model in a later stage (recommended)**
- **(b) Drop the requirement — the refusal is the end state (SYSTEM_TARGET §C changes)**

*Narrowed to two options.* The interim refusal is already ruled: OQ-4 = (b) placed it in S148 stage 0, and R-18 settled its mechanism. So the brief's original second half ("until that is built, should those kinds of time be refused?") is no longer open, and option (a) on its own, building the model with no interim refusal, would contradict that ruling. What is still open is whether the requirement survives. Option (a) stays in the list below, marked not offered; its content is the later stage of (c).

*A wording note for the prose:* "per working day" in the question is the agreement's working *døgn*, the 24 hours from the start of that working day (§ 7's remark).

**Why it matters.**
- **On-call is paid twice today:** once at a reduced on-call rate, and again as a full worked hour toward flex and overtime.
- **Call-ins and non-working travel are paid as ordinary hours,** because payroll never runs their rules.
- **Who meets it:** only the legacy API can register these types (Skema always writes ordinary work), so no screen user meets this yet, but any integration does.
- **The spec requires all three** (`SYSTEM_TARGET.md:79-81`).

**What StatsTid does today** (verified).
- **Every hour counts as worked.** The engine counts every entry's hours as worked, whatever its activity type (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/WeeklyTimelineCore.cs:172`, `:305-309`).
- **On-call adds a second payment.** `OnCallDutyRule` also pays an `ON_CALL` entry at a 0.33 rate (`OnCallDutyRule.cs:36-48`; `src/SharedKernel/StatsTid.SharedKernel/Models/AgreementRuleConfig.cs:47-48`; HK OK24 in `…/Config/CentralAgreementConfigs.cs:56-57`). There is one on-call type, with no home/workplace distinction.
- **Call-in and travel rules never run in pay.** Payroll's rule list omits them (`src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:2155-2163`), although the rules exist (`CallInWorkRule.cs:39`; `TravelTimeRule.cs:37`). The dormant travel rule counts non-working travel at 0.5 (`NonWorkingTravelRate`, `AgreementRuleConfig.cs:58`), a rate the sources do not support (below).
- **The spec splits travel in two.** SYSTEM_TARGET §C lists "Travel time (working vs non-working)" (`SYSTEM_TARGET.md:81`), with a working and a non-working travel rate (`:229`). The sources have no such split (below).
- **The type is unchecked free text.** Nothing validates the activity type (QUAL-202; `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs:157`); Skema writes `NORMAL` (`SkemaEndpoints.cs:2057`).
- **Already decided:** the interim refusal is S148 stage 0 (OQ-4 = (b)), and its mechanism is settled (R-18, `REFINEMENT-s147…:446-449`):
  - `POST /api/time-entries` answers 422 for any type other than `NORMAL`;
  - payroll refuses, loudly, any stored non-`NORMAL` entry.

**The sourced rule** (R3, verified; RR-7; now filed as `docs/references/sick-day-credit-and-agreement-texts-research.md:262-300`, Part 1, which governs; first cited from `.claude/refinements/RESEARCH-s147-VERIFICATION.md:228-266`).
- **HK and PROSA** (Statens arbejdstidsaftale § 7 nr. 3–5):
  - **Travel** on an official trip counts **in full**, capped at 13 h per working døgn (24 h from the start of that working day). It is never halved.
  - **On-call** counts 1/3 at home and 3/4 at the workplace.
  - **A call-in** at under 24 h notice counts as at least 3 h. The minimum does not apply when the work adjoins normal hours or is part of on-call, and it applies only when the person must come to the workplace.
- **AC-agreement staff:** travel only (CIR1H 9333 § 13 stk. 6), at most 13 h per døgn *"medmindre andet aftales lokalt"*, unless agreed locally (dossier `:287`).
- **Each type has a supplement basis too, not only a worked-time fraction** (D-3.2; PAV 19.2.9.1, dossier `:280`; PAV 19.2.8.1, `:279`):
  - the § 13 +25 % is paid on the hours **actually worked** for travel and call-ins (ignoring the 13 h cap and the 3 h minimum);
  - for **workplace on-call**, on the counted 3/4;
  - for **home on-call**, none;
  - § 14's weekend compensation for home on-call is paid on the **counted third** (a 9-hour home on-call at the weekend gives 3 hours plus 50 %).
- **The rest flag is a third, separate fact.** For the 11-hour rest rule, home on-call is rest unless it defeats rest's purpose (PAV 19.7.1, dossier `:299-300`), so the rest check needs its own flag.
- **The model is therefore three columns per type × agreement:** the worked-time fraction, the supplement basis, and the rest flag. It is not `OnCallDutyRule`'s pay rate.

**The options** (refinement `:507`; plan `:282`).
- **(c) Keep the requirement: the refusal now, the sourced model later.** (b)'s refusal is S148 stage 0 (already placed), and the model below is the target, built in a later stage.
  - *Gives up:* the most to build, later: a type catalogue, a way to register the types, rule changes and a marker move.
  - *Gains:* the requirement is kept on a sourced rule, and nothing is paid wrong meanwhile.
  - It resolves the two recorded gaps (D-3.4): the dormant 0.5 travel rate retires, and on-call gains its home/workplace split.
- **(b) The interim refusal as the end state.**
  - *Gives up:* the capability. Nobody can register on-call, call-in or travel, although nothing is paid wrong.
  - As an end state it changes SYSTEM_TARGET §C, which needs the owner's approval.
- **(a) A closed, sourced set of activity types, built with no interim refusal.** **Not offered** (D-3.1): the interim refusal is already ruled (OQ-4 = (b), R-18), so building the model *instead of* refusing is no longer open. Its content, the typed catalogue on the sourced rule, is the later stage of (c).

**The lean: (c), confirmed by the adjudicator** (D-3.3; refinement `:507`). The adjudicator adds no new reason beyond resolving the two recorded gaps (D-3.4); the brief's reasons stand:
- (b) stops the double count at once, and it is already placed.
- The sourced model restores the requirement on a sourced rule. R3 shows it can be built from sourced text, so it stays open.
- *What the research changes (RR-7):* the mechanism, not the lean. Time counts as a fraction of hours, not at a pay rate, and the rest-check flag is its own column.

**What the owner should know** (D-3.5).
- **"A later stage" may mean after S148.** The sourced model needs clock times for its call-in conditions (notice under 24 hours; not adjoining normal hours), so it depends on D-2 = (a) and sits with D-4's marking workflow in stage 2 or later. S148 may not reach it.
- **Registering these types needs a Skema change.** Today Skema writes only ordinary work, so the screen must gain a way to register on-call, call-ins and travel. That is work for the `ux` agent.
- **Home on-call's rest-flag default is a question for a domain expert.** The agreement guide says home on-call is rest *unless it defeats rest's purpose*, decided case by case under EU case law. StatsTid must not hard-code it as always rest; the default value is held for the expert.

**What the S148 build gets from each answer.**
- **(c):** stage 0's refusal (already placed), then, in stage 2 or later with D-4, a typed catalogue (QUAL-202's closed set) with:
  - per type × agreement: the worked-time fraction, the supplement basis and the rest flag;
  - the 13 h travel cap and the 3 h call-in minimum, with the call-in conditions read from D-2's clock times;
  - the 0.33 on-call rate and the 0.5 travel rate retired (travel counts in full);
  - a Skema change to register the types (`ux`);
  - a marker move.

  AC's "unless agreed locally" on travel is a D-8 candidate (a local parameter). Where the catalogue sits relative to the marking workflow is a D-4 input, because a leader assigns on-call duty and call-ins by their nature.
- **(b):** stage 0 only (already placed). On-call, call-in and travel stay impossible to register, and SYSTEM_TARGET §C text is owed to the owner.
- **(a):** none (not offered).

**The ADR-043 text it rewrites.**
- A new decision, D11 (QUAL-232).
- "What it retires" (`:167-181`) gains the on-call rate and the travel rate.
- QUAL-232's and QUAL-202's register rows get their route.
- Outside ADR-043, under (c): SYSTEM_TARGET §C's working/non-working travel split (`SYSTEM_TARGET.md:81`, `:229`) has no basis in the sources (D-3.2), so its rewording is owed with the owner's approval (an Orchestrator docs item).

**Waits on.** Nothing (R3 ✓). The later stage of (c) waits on D-2 = (a) and on D-4.

---
*Revision history.*
- Draft 1 (2026-10-28): first brief.
- Draft 2 (2026-10-28): the adjudication applied (D-3.1–.5). Added "The question as it will be asked", narrowed to (c)/(b) with (a) marked not offered; folded D-3.2 into the sourced rule and the options (travel in full and never halved; the supplement basis per type; three columns per type × agreement; the dependency on D-2 (a); AC travel as a D-8 candidate); D-3.4's gaps noted as resolved by (c); the lean confirmed; "What the owner should know" added; the sourced rule now also cites the filed dossier.
