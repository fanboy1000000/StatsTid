# D-11 — F-8: how does StatsTid know who is above the HK pay ceiling, and who is in PROSA pay groups 3–4?

*Brief, draft 2, 2026-10-28: adjudicated (`.claude/reviews/S147-D-batch1-adjudication.md` § D-11). The owner has not been asked yet. Drafted by `planner` (Opus 5.5); facts verified at `e7478f24`, draft 2's added citations at `b2ece3a1` (no code changed between the two; the research dossier was filed in `b2ece3a1`).*

**The question in one sentence.** Pay decides how some HK and PROSA employees are compensated for ordered extra hours. Since StatsTid holds no pay, how should it know who they are?
- HR records each person's "overtime regime" as a dated fact,
- StatsTid imports pay, or
- StatsTid infers the regime from the position.

**The question as it will be asked** (settled by the adjudicator, D-11.1; verbatim).

> *Whether an ordered extra hour earns an HK or PROSA employee +50 % overtime, a discretionary extra-work claim (PROSA pay groups 3–4), or nothing (HK staff above the pay ceiling, who get a one-off chef-pay payment instead) depends on their pay — which StatsTid does not hold. How should StatsTid know a person's regime: should HR record it as a dated, audited fact on the employee; should StatsTid import base pay and compare it with the threshold; or should it infer the regime from the position?*

- **(b) HR records the person's overtime regime, dated and audited (recommended)**
- **(a) Import base pay and compare with the 2012-level threshold (crosses the payroll boundary — an architectural exception only the owner can grant)**
- **(c) Infer it from the position**

All three options are offered; the recommended one is listed first. (a) is the only option in the adjudicated batch that would breach an invariant (the payroll boundary); it stays on the list because granting that exception is the owner's alone.

**Why it matters.**
- **Three outcomes for an ordered hour:**
  - **+50 % overtime:** the general HK and PROSA rule;
  - **a discretionary merarbejde (extra-work) claim:** PROSA pay groups 3–4;
  - **nothing:** HK staff in special posts with base pay of 441,037 kr. or more at the 2012 level, who get a one-off chef-pay payment instead.
- **Getting it wrong is costly either way.** It pays an exempt chief consultant overtime, or it denies a group-2 PROSA employee theirs.
- **Today everyone is on overtime.** StatsTid pays them all as ordinary HK or PROSA overtime.
- **The scope is narrow.** Under ADR-043 the regime matters only for ordered or accepted hours (D3). The unsocial-hours supplement still applies to exempt staff when the placement was ordered.

**What StatsTid does today** (verified).
- **Every HK and PROSA employee is on overtime,** none on merarbejde (`src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs:44-45`, `:75-76`, `:129-130`, `:160-161`). There is no HK or PROSA position override; the existing ones are AC only (`…/Config/PositionOverrideConfigs.cs:27-43`).
- **No pay ceiling and no base pay exist anywhere** in `src/` or `init.sql` (S146 C3's search; re-checked).
- **The profile holds no pay.** The employee profile holds part-time fraction, position and employment category, each dated (`docs/generated/db-schema.md:258-271`; `docker/postgres/init.sql:563-593`, ADR-040 D4). StatsTid exports hours with wage types, never kroner (`ADR-043:210-211`).
- **A similar mechanism exists but is unread.** ADR-024's role overrides already carry a dated, audited merarbejde right per employment category for AC (`init.sql:2448-2470`, seeds `:2516-2526`). That right is ADR-024 D2's three-state value, `CONTRACTUAL / DISCRETIONARY / NONE` (`docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md:57`), on D1's employment-category dimension (`:28`). Neither payroll nor the engine reads them: payroll passes the engine an empty category (`src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:2678`).

**What the sources say** (C3, verified in S146; `docs/references/agreement-cells-reverification-research.md:492-525`).
- **HK:** HK § 4 stk. 2 and its remark: base pay ≥ 441,037 kr. a year at the 31 March 2012 level means the chef-pay agreement's §§ 5–6 apply, and the person is exempt from overtime and merarbejde pay. The test uses base pay, not total pay, and not today's kroner; the level date is § 3 stk. 4 (`:502`). Confidence HIGH.
- **PROSA:** there is no ceiling. § 12 stk. 2 routes pay groups 1–2 to overtime, and groups 3–4 to merarbejde "when they meet the state agreement's conditions" (§ 8 stk. 3). Confidence MEDIUM.
- **What exempt staff keep:** the § 13 +25 % and § 14's weekend compensation still apply when the placement was ordered or necessary (`:508`, `:523`).
- *Citation note:* C3 quotes HK circular 085-25, which S147's verification found superseded by 087-25 (`.claude/refinements/RESEARCH-s147-VERIFICATION.md:31`, `:365`; now filed at `docs/references/sick-day-credit-and-agreement-texts-research.md:65`). The verifier checked 087-25's § 38 only. **Re-citing § 4 stk. 2 and § 3 stk. 4 against 087-25 is an Orchestrator docs item, run before ADR-043's dual-lens review**, with a dated correction note appended if they differ (D-11.5). Under (b) the number is never encoded, so the answer does not wait on it.

**The options** (ADR-043 F-8, `:313-320`).
- **(b) A dated employee attribute, the overtime regime.** HR sets it under ADR-040's as-of rule (every fact has a date it applies from), and it is audited.
  - *Its values differ per agreement* (D-11.2): HK, overtime or exempt; PROSA, overtime, or merarbejde for groups 3–4; AC, if folded in (below), contractual claim or none. S148 designs **one enum**, one closed list of values, covering all of them.
  - *Gives up:* a manual HR step whenever someone crosses the ceiling or changes pay group. StatsTid trusts HR's classification rather than checking pay. It is a hand-off with no system check, so it gets a row in the HR follow-up register (HRP).
- **(a) Import base pay and compare it with 441,037 kr.**
  - *Gives up:* the payroll boundary. StatsTid would hold salaries, personal data it does not hold today. And the rule moves: the threshold is stated at the 31 March 2012 level, so the comparison needs indexation (D-11.2).
  - Moving that boundary is an architectural exception, and only the owner can grant it.
- **(c) Derived from position configuration.**
  - *Gives up:* correctness for some people, because positions do not map one-to-one to pay groups.
- *Considered, not proposed:* deriving the regime from the employment category through ADR-024's overrides.
  - *For it:* it reuses dated, audited plumbing.
  - *Against it:* a category is a group, but the HK ceiling is a per-person pay fact, so HR would have to invent a category per regime. The overrides are also configuration, so they would reach pay only through D-6 (a).

**The lean: (b), confirmed by the adjudicator** (D-11.3; ADR-043 `:319-320`; plan `:290`). The adjudicator's reasons:
- It keeps kroner out of StatsTid.
- The regime is dated, audited and frozen in the manifest (the record of what a calculation ran with), so replay proves it, like the part-time fraction.
- Exempt staff still earn the +25 % and § 14's compensation for ordered placements, so the regime only decides the overtime/merarbejde part.
- The brief's further points stand: it puts the decision with the people who know the pay; and PROSA groups 3–4 who do not meet § 8 stk. 3 fall back to overtime on a literal reading, a domain-expert point that under (b) is just a value HR sets.
- The research changes nothing else.

**One regime attribute also covers AC** — *recorded proposal — adjudicated, told to the owner, not asked* (D-11.4). Accepted in principle on three conditions:
1. **ADR-043's header gains "amends ADR-024 D1/D2"**, and ADR-024's category overrides become the seed (the default HR starts from), not a second source pay reads.
2. **One value set for the three agreements,** with the AC values marked PROVISIONAL.
3. **For AC, the attribute decides whether a claim can be raised at all.**

The owner prose carries it in one sentence.

**What the owner should know.** *D-11.5 is an Orchestrator docs item (the citation note above), not an owner point; these are the owner-facing points the adjudication makes for D-11.*
- **The one-sentence AC point** (D-11.4): the same HR-recorded regime also says whether an AC employee can claim merarbejde at all, starting from today's per-category values; the AC values are provisional.
- **(b) adds a hand-off to HR with no system check** (D-11.2). StatsTid cannot tell when someone crosses the ceiling or changes pay group; HR must record it, and the HR follow-up register gets a row for it.
- **Why (a) is on the list at all** (the adjudication's closing note): importing pay would breach the payroll boundary, one of the invariants. It is listed only because granting that exception is the owner's decision alone, and it would bring salaries (personal data) and an indexed threshold into StatsTid.

**What the S148 build gets from each answer.**
- **(b):** a dated `overtime_regime` on the employee profile:
  - one enum covering HK, PROSA and (provisionally) AC values;
  - an HR editor, an audit trail and as-of resolution;
  - ADR-024's category values as the seed, not a second source;
  - the value carried in the manifest's frozen pieces beside fraction and position (`ADR-016:171`);
  - read by the engine when it routes ordered hours, and for AC when it decides whether a claim can be raised;
  - an HRP row for the HR step;
  - a reseed.
- **(a):** a pay import from the payroll system, an indexed threshold, a comparison rule, and the boundary exception.
- **(c):** a regime column on positions, which misclassifies wherever position and pay group diverge.

**The ADR-043 text it rewrites.**
- F-8 (`:313-320`) becomes a decision.
- D4's exempt-groups bullet (`:106-107`).
- Consequences, "Payroll boundary" (`:210-211`).
- Under the recorded proposal: the header's "Supersedes / amends" gains ADR-024 D1/D2.
- The recorded proposal becomes ADR text when this question is answered (listed in `../ADR-043-working.md`, "Provisional items and recorded proposals").

**Waits on.** Nothing.

---
*Revision history.*
- Draft 1 (2026-10-28): first brief.
- Draft 2 (2026-10-28): the adjudication applied (D-11.1–.5). Added "The question as it will be asked" ((b) listed first); folded D-11.2 into the options ((b)'s per-agreement values and one enum; (b) as an HR hand-off with an HRP row; (a)'s indexation and personal data); the lean confirmed with the adjudicator's reasons; the AC fold marked as a recorded proposal with its three conditions; the citation note made an Orchestrator docs item (D-11.5); "What the owner should know" added.
