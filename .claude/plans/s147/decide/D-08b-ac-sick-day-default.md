# D-8b — Q-1: how should a paid sick day count in the flex account for staff on the AC agreement?

*Brief, draft 2, 2026-10-29: adjudicated (`.claude/reviews/S147-D-batch2-adjudication.md` § D-8b). Queued: asked after D-8a. Split out of `D-08-local-flex-parameters.md` (draft 1's D-8b section) by the adjudicator's ruling 5. The parent brief holds the shared facts and R-1..R-7. Drafted by `planner` (Opus 5.5). Code facts were verified at `61d6a98a` and re-checked at `26b70a08`; no file under `src/`, `docker/`, `frontend/` or `tools/` changed in between. It builds on D-9 = (a), answered 2026-10-29 (its D-9.4 analogy).*

**The question in one sentence.** No central text says how a paid sick day counts in flex for staff on the AC agreement. Should StatsTid use the HK/PROSA rule as a labelled default that a local agreement can change, or refuse to calculate such a month until the institution sets a rule?

**The question as it will be asked** (settled by the adjudicator, D-8b.1; verbatim).

> *For staff on the AC agreement no central text says how a paid sick day counts in the flex account; for HK and PROSA the state working-time agreement credits the day's planned hours, else 7.4 h × fraction. Which default should StatsTid apply where an institution's local agreement has not set one: the HK/PROSA rule, labelled as a default by analogy that a local agreement can change; or no default, so that an AC month with a sick day cannot be calculated until the institution sets the rule?*

- **(a) The HK/PROSA rule as a labelled default (recommended)**
- **(b) No default — refuse until configured**

*Not offered* (D-8b.1): **(c) credit nothing.**
- PAV 19.5.9 requires sick days to be counted into the flex account (`docs/references/sick-day-credit-and-agreement-texts-research.md:163`).
- The AC agreement's § 17 gives full pay in sickness (`:153`).
- Crediting nothing would bring QUAL-231's screen/payroll split back for AC: a sick AC employee would lose a day of flex for being sick. That breaks domain correctness, so it is not a valid path.

**Why it matters.**
- **A sick day must leave flex unchanged, or it costs the employee time for being ill.** For HK and PROSA the state working-time agreement settles this: the day counts as the hours the person should have worked (§ 7 nr. 2).
- **For AC staff the agreement is silent.** Its working-time chapter credits no absence at all, and PAV's AC section says working time is "not further defined" (`:150-160`). The sources settle only two things:
  - sick days must be counted into flex (PAV 19.5.9, `:163`);
  - the amount is left to the local flex agreement (`:164`).
- **So only the owner can pick the default.** The verifier recommends the HK/PROSA rule, held as "an owner-ruled default, labelled as such" (`:167-168`).
- **Who notices:** AC staff on flex who are ever ill, their leaders and HR, and payroll.

**What StatsTid does today** (verified).
- **At `26b70a08` a sick day earns no flex credit for anyone** (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/AbsenceRule.cs:34-45`; QUAL-231).
- **S147 is building option (a)'s rule** (D-8b.2). TASK-14708 (wave 2a) credits a sick day for every agreement. For AC it is a labelled default pending this question (`.claude/plans/s147/TASK-14708.md:17`, `:38`). It is **not merged** at `26b70a08`.
- **Other paid-absence days are already credited for AC** (vacation, særlige feriedage, paid leave, care days: `AbsenceRule.cs:34-45`). This question concerns only sick days, the one AC case that no central text and no code on master credits today.

**The options** (RR-1; the research adjudication's wording, `.claude/reviews/RESEARCH-s147-adjudication.md:24`).
- **(a) The HK/PROSA rule as a labelled default:** the day's planned hours, else 7.4 h × fraction. S147 is building this (TASK-14708, wave 2a, not yet merged). A local agreement can change it through the paid-absence credit basis (the parent's R-6).
  - *Gains:*
    - screen and payroll agree;
    - a whole sick day leaves an AC employee's flex unchanged;
    - it is the same analogy the owner accepted for AC's month norm (D-9.4).
  - *Gives up:* it applies a rule no AC text states. The label says so.
- **(b) No default.** Payroll refuses any AC month that holds a sick day until the institution sets the parameter.
  - *Gains:* StatsTid never applies an unsourced rule.
  - *Gives up:* usability (D-8b.2).
    - No AC month with a sick day can be paid until every AC institution configures the parameter.
    - **Every demo AC month with a sick day is blocked too,** until the demo seed configures it.
- **(c) Credit nothing.** Not offered (above).

**The lean: (a), confirmed by the adjudicator** (D-8b.3; plan `PLAN-s147.md:287`). The adjudicator's reasons:
- It is the verifier's own recommendation (`sick-day…research.md:168`).
- It is the same analogy the owner accepted for AC's month norm (D-9.4).
- The paid-absence credit basis (R-6) keeps every institution free to set its own rule.

**What the owner should know.**
- **The AC sick-day rule is a labelled analogy,** not an AC text.
- **Any institution can change it** through its local paid-absence credit basis (R-6).
- **S147 already builds (a).** Under (a) nothing changes from what S147 delivers. Under (b), S148's config stage adds a refusal, and AC months with a sick day (demo months included) wait for configuration.

**What it builds on.**
- **D-9 = (a), with D-9.4:** AC's month norm uses D1's function by analogy. This default is the same kind of analogy.
- **The parent's R-6:** the paid-absence credit basis is a local parameter. Its default is § 7 nr. 2, and for AC it is labelled "by analogy".

**What the S148 build gets from each answer.**
- **(a):** TASK-14708's labelled default stays. The label is recorded with R-6's default: § 7 nr. 2 for HK and PROSA, the same rule by analogy for AC.
- **(b):** the config stage adds a refusal for AC months that hold a sick day and have no configured basis. The demo seed configures the basis for the demo AC institutions.

**The ADR-043 text it rewrites.**
- F-5's paid-absence credit sentence records the AC default and its label (RR-1, RR-12).
- Q-1's answer is recorded at acceptance (plan O-10, `PLAN-s147.md:318`).

**Waits on.** Nothing for the asking. Asked after D-8a.

---
*Revision history.*
- Draft 2 (2026-10-29): split out of `D-08-local-flex-parameters.md` (draft 1's D-8b section) by the adjudicator's ruling 5 (`.claude/reviews/S147-D-batch2-adjudication.md`).
  - **D-8b.1:** added "The question as it will be asked", verbatim, with (a) first and (c) not offered, giving its reasons.
  - **D-8b.2:** "already built" corrected to "being built in S147" (TASK-14708, wave 2a, not merged at `26b70a08`; the adjudication checked `f37d7cd3`, and no code changed between the two). (b)'s cost line now names the blocked demo AC months.
  - **D-8b.3:** the lean is confirmed with the adjudicator's reasons. Added the fact that other paid-absence days are already credited for AC.
