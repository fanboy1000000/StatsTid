# D-10 — F-7: until the state publishes its text, how are a part-timer's ordered hours up to full time paid?

*Brief, draft 1, 2026-10-28. Drafted by `planner` (Opus 5.5); code facts verified at `30e6228a` (the cited files are unchanged since `e7478f24`). Drafted on the coordinator's ruling: R4, its only gate, is answered. Next: the adjudicator (`.claude/reviews/S147-D-10-adjudication.md`). The owner has not been asked yet. **The value this question sets is a provisional owner default, pending a qualified expert.***

**The question in one sentence.** Take a part-timer whose leader orders extra hours, and those hours keep them at or below a full-timer's hours. Should StatsTid hold that rule as a dated switch? And until the state's implementing text appears, should the provisional default be:
- the printed rule (hour for hour), or
- the arbitration award's rule (+50 %)?

**Why it matters.** Take a 30-hour part-timer whose leader orders 3 extra hours.
- **The printed rule** (§ 8 stk. 2) pays those hours 1:1, because they do not exceed full time.
- **The 4 February 2026 award** pays +50 % on them. The award binds the state employer back to 20 January 2000, and it found the printed rule breaches the EU part-time directive.
- **The exact rule is still unknown,** because the state's implementing agreement has not appeared.

So this answer sets what part-timers' ordered hours are worth until it does. Who notices: part-timers, the leaders who order hours, and payroll. Under ADR-043 D3 it matters only for ordered or accepted hours; an unordered surplus hour is flex, 1:1, whatever the answer.

**What StatsTid does today** (verified).
- **The weekly engine scales the overtime thresholds by the part-time fraction** (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/WeeklyTimelineCore.cs:172-175`; `src/SharedKernel/StatsTid.SharedKernel/Calendar/DayNorm.cs:44-45`).
  - HK and PROSA run on the default thresholds of 37 h and 40 h (`…/Models/AgreementRuleConfig.cs:43-44`); no agreement in `CentralAgreementConfigs.cs` overrides them.
  - So a part-timer is paid +50 % from their own norm, and +100 % from 40 h × fraction, on every hour, ordered or not (`OvertimeRule.cs:61-82`).
  - This answers the check S146's C4 left open (`docs/references/agreement-cells-reverification-research.md:533`). Today's code follows the award's direction, not the printed text, and it skips the "ordered" precondition. ADR-043 retires both the tiers and the automatic trigger (D3, D4).
- **No dated switch for this band exists.** The § 14 weekend +50 % applies to part-timers' extra hours whatever the band rule (C4, `:561`; ADR-043 D7).

**What the sources say** (R4 verified 28 Oct 2026; `docs/references/sick-day-credit-and-agreement-texts-research.md:12`, `:307-356`; C4, `agreement-cells-reverification-research.md:531-568`).
- **The printed rule is unchanged.** 022-26 § 8 stk. 2: *"… godtgøres dog kun med afspadsering af samme varighed eller med almindelig timeløn"* (hours up to full time: equal time off or ordinary hourly pay). PAV 19.2.7.1 and 19.3.1.6 say the same.
- **The award.** FV2025-795 (4 Feb 2026): MedSt (the state employer agency) must recognise two things:
  - paying part-timers *who have comparable full-timers* overtime only above the full-time norm breaches directive 97/81/EF, from 20 Jan 2000;
  - the remedy is overtime pay for the band between their own norm and the full-time norm.
- **No implementing text yet** (as of 28 Oct 2026).
  - MedSt's circular list was read on pages 1–32, through 068-26 of 27 Oct 2026; its news archive was read as well.
  - Union pages say *"ikke på plads endnu"* (not yet in place).
  - OK26 settlement item 28 (CFU) and item 16 (AC) fund the negotiation, which was due to open by 1 May 2026.
- **The two citation fixes (RR-8).**
  1. Annex A names *"Statens arbejdstidsaftale … § 8, stk. 2"*, the agreement. It does not name circular 022-26, which was issued on 18 Jun 2026, after the award. The provision is the same one (`…research.md:23`, `:355`).
  2. FV2026.573 (4 Sep 2026) is a private-sector award, so do not cite it as a state rule. But the five-year limitation it applied to back-pay claims is general law (`:26`, `:348-351`). Back pay is outside StatsTid's scope either way.

**The options** (ADR-043 F-7, `:307-311`).
- **(a) The printed rule:** 1:1 up to full time.
  - *Gives up:* domain correctness. It knowingly applies a rule a binding award has found to breach the part-time directive.
- **(b) The award's rule:** +50 % from the part-timer's own norm.
  - *Gives up:* certainty. The implementing text may differ when it arrives, for example in its conditions, or in how it reads "comparable full-timer".
- **(c) A dated, switchable rule:** a default now, switched on the implementing text's date.
  - *Gives up:* a little more to build (two encodings and a date).
  - It still needs a default value now: **(i)** the printed rule or **(ii)** the award's rule. That value is the owner's provisional default, pending a qualified expert.

**The lean: (c) for the shape** (ADR-043 `:311`; plan `:289`).
- The rule will change on a date nobody knows yet, and ADR-043 already keeps date-bound rules dated (`:205-207`).
- **On the value, ADR-043 and the plan give no lean.** They call it "the one answer the sources cannot give".
- **Planner's lean on the value, for the adjudicator: (ii), the award.**
  - It is the binding legal position (HIGH confidence), and (i) knowingly applies a breach.
  - The implementing text can only refine *how* the award applies, and the switch absorbs that on its date.
  - The ADR and the code label it a provisional owner default, pending a qualified expert.
- RR-8 changes the evidence's citations, not the lean.

*Proposal folded in (not a question):* hold the award's "comparable full-timer" condition (C4, `:689`) as met for every part-timer. StatsTid has no data to test it, and the assumption is recorded for the expert.

**What the S148 build gets from each answer.**
- **(a) or (b) alone:** one encoding for ordered hours in the band, with no switch. When the text arrives, the band becomes a new decision.
- **(c) with (i) or (ii):** a dated band rule with two encodings:
  - the chosen default in force until a switch date that is not yet known;
  - each calculation records which variant paid it, so replay can prove it;
  - it lands in the stage that turns ordered hours into overtime (stage 2 under D-1 (b)). Before that stage no hour is overtime, so the band never arises.

**The ADR-043 text it rewrites.**
- F-7 (`:307-311`) becomes a decision.
- D4's part-timers bullet (`:103-105`).
- The Consequences sentence on dated rules (`:205-207`) names the switch.
- QUAL-223 item (3) gets its route.

**Waits on.** Nothing (R4 ✓). Where it is built follows D-1 and D-4.

---
*Revision history.* Draft 1 (2026-10-28): first brief, on the coordinator's ruling (1).
