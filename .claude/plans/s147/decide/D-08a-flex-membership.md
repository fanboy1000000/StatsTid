# D-8a — F-5's per-group sub-question: can an institution's flex rules treat groups of its staff differently?

*Brief, draft 2, 2026-10-29: adjudicated (`.claude/reviews/S147-D-batch2-adjudication.md` § D-8a). Queued: asked after D-4 and D-7, before D-8b. Split out of `D-08-local-flex-parameters.md` (draft 1's D-8a section) by the adjudicator's ruling 5. The parent brief holds the shared facts, the sources and the recorded proposals R-1..R-7. Drafted by `planner` (Opus 5.5). Code facts were verified at `61d6a98a` and re-checked at `26b70a08`; no file under `src/`, `docker/`, `frontend/` or `tools/` changed in between. It builds on D-6 = (a) and D-11 = (b), both answered 2026-10-29.*

**The question in one sentence.** Every sampled local flex agreement leaves some staff out of flex. Should StatsTid let an institution's flex rules treat groups of its staff differently, and if so how far?

**The question as it will be asked** (settled by the adjudicator, D-8a.1; verbatim).

> *Every sampled local flex agreement leaves some staff out of flex — special and chief consultants, managers, flex-job staff — and one gives a staff group its own cap. Should an institution's flex rules in StatsTid be able to treat groups of its staff differently, and how far: one set of rules per institution and agreement, everyone on it; one set of rules plus a dated fact per person, recorded by HR, saying whether that person is on the flex scheme at all; or full rule sets per staff group, each with its own caps?*

- **(b) One rule set, plus an HR-recorded "on the flex scheme: yes / no" per person (recommended)**
- **(a) One rule set per institution and agreement; everyone on it**
- **(c) Rule sets per staff group**

All three options are offered, the recommended one first.

**Told with this question, not asked** (the parent's D-8.5 and ruling 2; see `D-08-local-flex-parameters.md`).
- **The no-central-cap correction** (ruling 2), which the adjudicator placed in D-8a's prose. There is no central flex cap: the 100/120/150 h were never sourced. Until an institution sets its cap, nothing is forfeited, and the month's calculation says "no flex cap configured". Once a cap is set, hours above it are forfeited and shown, never paid out unless the institution's agreement says so.
- **The rest of D-8.5:**
  - a leaver's balance is settled, and shown, in the leaving month;
  - an HR-entered opening balance is designed (its sprint is S148's plan's);
  - the AC sick-day rule is a labelled analogy (D-8b, asked next);
  - new HR hand-offs, each with an HRP row: flex membership, the leaver's settlement, the opening balance and the cap exception.

**Why it matters.**
- **Every current sampled agreement leaves someone out of flex.** SDU 2026, DTU and the AU template each exclude groups: special and chief consultants, managers, flex-job staff, fuldmægtige, fixed-roster staff (`docs/references/agreement-cells-reverification-research.md:629-631`).
- **Today StatsTid puts everyone on flex.** Someone the agreement leaves out still gets a flex balance they do not have, and a deficit if they register few hours.
- **One sampled agreement gives a group its own cap.** AU Foulum gives field and stable staff 200 h instead of 100 h (`:632`). It dates from 2014, and its status after 2017 is unknown.
- **Who notices:** the excluded staff (their screen and their month's calculation), HR (who records the fact) and payroll.

**What StatsTid does today** (verified; the full list is in the parent brief).
- **One typed local profile per institution, agreement and OK version,** with one active row per key (`docker/postgres/init.sql:856-876`). Per-group values do not fit one row per key.
- **No employee can be marked as outside the institution's flex scheme.** There is no flex-membership attribute. The employee profile holds part-time fraction, position and employment category, each dated (`docs/generated/db-schema.md:258-271`).
- **The pattern this question copies already exists.** D-11 = (b) adds an HR-recorded, dated overtime regime on the employee, frozen into the manifest beside fraction and position.

**The options.**
- **(b) One rule set, plus a dated fact per person, recorded by HR: "on the institution's flex scheme: yes / no".**
  - Someone off the scheme keeps no flex balance, and no settlement or forfeiture runs for them. Their hours are still registered.
  - *Gains:* it represents every *current* sampled agreement (SDU, DTU, the AU template).
  - *Gives up* (D-8a.2 item 4):
    - **a second person-level attribute** beside D-11's regime, with one HR editor and a value seeded from the position;
    - **per-group values** such as AU Foulum's 200 h, recorded as a limitation;
    - **an HR step,** with its own HRP row.
  - *What still applies to someone off the scheme:*
    - an ordered hour still follows D-11's overtime regime;
    - § 13's supplement and § 14's weekend compensation still apply to ordered or rostered placements;
    - what a deficit means for them is the domain expert's question. The proposal is that nothing is carried and nothing is deducted.
- **(a) One rule set per institution, agreement and OK version,** keyed as the profile is today.
  - *Gains:* the least to build, and no new step for HR.
  - *Gives up:* every group the local agreement treats differently. Staff the agreement leaves out of flex run on its flex rules anyway: they get a balance they do not have, and a deficit if they register little. AU Foulum's field-staff cap cannot be held either.
- **(c) Rule sets per staff group.** HR records a dated "flex group" for each person, seeded from position. Each group has its own values, and "not on flex" is one group's setting.
  - *Gains:* it also holds per-group caps.
  - *Gives up:* the most to build. Groups become part of the profile's key, and HR maintains a group for every person.
- *Considered, not proposed:* deriving the group from position or employment category alone. Flex-job status is not a position, and neither is being a fuldmægtig on the standby allowance (a pay element StatsTid does not hold). So HR records it, seeded from position: the pattern of D-11.4.

**The lean: (b), confirmed by the adjudicator** (D-8a.3). The adjudicator's reasons:
- **Exclusion appears in every current sample.** A per-group cap appears once, in a 2014 agreement whose status is unknown.
- **(b) is the smallest answer that represents what institutions actually agree.**
- **It follows D-11's pattern:** HR records a dated, audited fact on the employee, frozen into the manifest.
- **(c) can grow out of (b)** without undoing it: the yes/no fact becomes a group.

**What the owner should know.**
- **Who is on flex becomes a dated fact HR records.** Someone off the scheme has no flex balance and no settlement.
- **Their ordered hours are still paid by their regime** (D-11), and evening and weekend placements still earn § 13 and § 14 when ordered or rostered.
- **What a deficit means for someone off the scheme goes to the domain expert.** The proposal is that nothing is carried and nothing is deducted.
- **Per-group caps (AU Foulum's 200 h) are not held under (b).** That is recorded as a limitation; (c) can come later.
- **There is no central flex cap** (ruling 2; above).

**What it builds on.**
- **D-6 = (a):** the fact is dated, resolved for each day and frozen into the manifest, as every local value is.
- **D-11 = (b):** the same shape as the regime. The off-scheme person's ordered hours are routed by it.
- **D-1 = (b):** the fact is read from D-6's config stage. The settlement it switches off is stage 1's.
- **The parent's R-1:** the profile keeps one row per key under (b), so storage is unchanged.

**What the S148 build gets from each answer.**
- **(b):**
  - a dated, audited "on the flex scheme" fact on the employee, with an HR editor, a value seeded from position, and an HRP row;
  - as-of resolution, frozen in the manifest beside fraction, position and regime;
  - off the scheme: no flex settlement, no forfeiture, no balance on screen;
  - the profile keeps one row per key.
- **(a):** nothing new. The limitation is recorded in the ADR.
- **(c):** the same fact as a group, with profile rows keyed by group.

**The ADR-043 text it rewrites.**
- F-5's "per subgroup" (`:287`, `:292`, `:295`): the sub-question is closed with the answer.
- Consequences, "New hand-offs" (`:217-218`): the flex-membership row.
- The parent's recorded proposals become ADR text when D-8a and D-8b are answered.

**Waits on.** Nothing for the asking. Asked after D-4 and D-7, and before D-8b.

---
*Revision history.*
- Draft 2 (2026-10-29): split out of `D-08-local-flex-parameters.md` (draft 1's D-8a section) by the adjudicator's ruling 5 (`.claude/reviews/S147-D-batch2-adjudication.md`).
  - **D-8a.1:** added "The question as it will be asked", verbatim, with all three options and (b) first.
  - **D-8a.2:** item 4 is folded into (b)'s cost line (a second person-level attribute, one HR editor, seeded from position; ordered hours still follow the regime and § 13/§ 14 still apply; the deficit question goes to the expert). Item 5 removed the AC annual-norm line, which is now in the parent's build-input list. Items 1–3 are recorded in the parent.
  - **D-8a.3:** the lean is confirmed with the adjudicator's reasons.
  - **Ruling 2 and D-8.5:** the no-central-cap correction and the parent's owner points are added under "Told with this question".
