# D-10 — F-7: until the state publishes its text, how are a part-timer's ordered hours up to full time paid?

*Brief, draft 2, 2026-10-29: adjudicated (`.claude/reviews/S147-D-10-adjudication.md`). The owner has not been asked yet. Drafted by `planner` (Opus 5.5); code facts verified at `b2ece3a1` (the cited files are unchanged `e7478f24..b2ece3a1`, and again to `c29764ce`). **The value this question sets is a provisional owner default, pending a qualified expert.***

**The question in one sentence.** A part-timer's leader orders extra hours that still leave them at or below a full-timer's hours. Until the state publishes the text that implements the February 2026 arbitration award, should those hours earn +50 % (the award's direction) or be paid hour for hour (the printed agreement)?

**The question as it will be asked** (settled by the adjudicator, D-10.1; verbatim).

> *A part-timer whose leader orders extra hours that still keep them at or below a full-timer's hours sits in a "band" between their own norm and full time. The printed state agreement (§ 8 stk. 2) pays band hours hour for hour; a binding arbitration award of 4 February 2026 found that rule in breach of the EU part-time directive and says these hours must be compensated as overtime — but the state has not yet published the agreement that implements the award (checked 28 October 2026). StatsTid will hold this rule as a dated, named variant, so the state's text can be switched in on its date and every month's calculation records which variant paid it. Until then, which rule is the provisional default — a value a qualified expert must confirm before any real use: the award's direction, under which ordered hours above the part-timer's own norm earn the same +50 % as a full-timer's overtime (a part-timer is simply a full-timer with a smaller norm), or the printed rule, under which ordered hours up to full time are compensated hour for hour and +50 % applies only above full time?*

- **(ii) The award's direction — +50 % from the part-timer's own norm (recommended)**
- **(i) The printed rule — hour for hour up to full time, +50 % only above it**

Recorded as "F-7 = (c), default (ii)" / "(c), default (i)".

*The shape is settled; only the value is asked.* ADR-043's option (c), a dated, named rule variant, is told to the owner inside the question's wording, not asked.

*Not offered, and why* (D-10.1):
- **ADR-043's (a) and (b) "alone", with no dated slot.** This is a build-shape detail, not a business fork. The slot is a named variant plus a from-date frozen into the manifest (the record of what a calculation ran with), which ADR-043 already prescribes for rules that change on a date (`ADR-043:205-207`). So the owner is told, not asked.
- **Refuse the band until the text arrives** (the OQ-4 pattern, under which on-call, call-ins and travel are refused for now). Unlike on-call, there is a sourced, binding direction here. Refusing would hold up the month of every part-timer with an ordered hour, for an unknown time: a usability cost with no correctness gain.
- **A per-institution choice.** A local agreement that already pays the award's rule is D-8's territory (the local parameters), and it is moot under (ii).

**Why it matters.** Take a 30-hour part-timer whose leader orders 3 extra hours, all inside the band.
- **Under (i), the printed rule:** 3 h off, or 3 h at ordinary pay.
- **Under (ii), the award's direction:** 4.5 h off, or 3 h plus 50 %. The employer chooses which (ADR-043 D8).
- **Only ordered or accepted hours are touched** (ADR-043 D3; OQ-3 = (b)). An unordered extra hour is flex, hour for hour, under both answers.
- **The award binds the state employer back to 20 January 2000**, but the exact rule is still unknown, because the state's implementing agreement has not appeared. This answer sets what part-timers' ordered hours are worth until it does.
- **Who notices:** part-timers, the leaders who order their hours, and payroll.

**What StatsTid does today** (verified).
- **The weekly engine scales the overtime thresholds by the part-time fraction** (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/WeeklyTimelineCore.cs:172-175`; `src/SharedKernel/StatsTid.SharedKernel/Calendar/DayNorm.cs:44-45`).
  - HK and PROSA run on the default thresholds of 37 h and 40 h (`src/SharedKernel/StatsTid.SharedKernel/Models/AgreementRuleConfig.cs:43-44`); no agreement in `CentralAgreementConfigs.cs` overrides them.
  - Every hour above the norm counts, with no "ordered" test (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/OvertimeRule.cs:45-48`). The tiers are `:61-91`: +50 % up to the upper threshold, and +100 % above 40 h × fraction (the 100 % line, `:82-90`).
  - So a part-timer is paid +50 % from their own norm, ordered or not.
- **This answers the check S146's C4 left open** (`docs/references/agreement-cells-reverification-research.md:533`). Today's code follows the award's direction, not the printed text, and it skips the "ordered" precondition. ADR-043 retires both the tiers and the automatic trigger (D3, D4). Because the engine already pays +50 % from the own norm, answer (ii) moves no demo payslip on this point (D-10.3).
- **No dated switch for this band exists.** The § 14 weekend +50 % applies to part-timers' extra hours whatever the band rule (C4, `:561`; ADR-043 D7).

**What the sources say** (R4 verified 28 Oct 2026; `docs/references/sick-day-credit-and-agreement-texts-research.md:12`, `:307-356`; C4, `agreement-cells-reverification-research.md:531-568`).
- **The printed rule is unchanged.** 022-26 § 8 stk. 2: *"… godtgøres dog kun med afspadsering af samme varighed eller med almindelig timeløn"* (hours up to full time: equal time off or ordinary hourly pay). PAV 19.2.7.1 and 19.3.1.6 say the same.
- **The award.** FV2025-795 (4 Feb 2026): MedSt (the state employer agency) must recognise that paying part-timers *who have comparable full-timers* (*"for hvem der findes sammenlignelige fuldtidsansatte"*, `agreement-cells…:538`) overtime only above the full-time norm breaches directive 97/81/EF, from 20 Jan 2000.
- **The +50 % in (ii) is derived, not quoted** (D-10.2 item 3).
  - The award's operative part is a declaration that MedSt must acknowledge (R4-9, `sick-day…research.md:354`).
  - The "rule" is in its remedy reasoning: compliance must come from paying part-timers *overtidshonorering*, overtime pay, for work in the band between their own norm and the full-time norm (`agreement-cells…:541`).
  - The award names no rate. The +50 % is § 8 stk. 1's overtime rate (V2; ADR-043 D4) applied to the band.
- **No implementing text yet** (as of 28 Oct 2026).
  - MedSt's circular list was read on pages 1–32, through 068-26 of 27 Oct 2026; its news archive was read as well.
  - Union pages say *"ikke på plads endnu"* (not yet in place).
  - OK26 settlement item 28 (CFU) and item 16 (AC) fund the negotiation, which was due to open by 1 May 2026. The CFU item sets aside 12 million kroner for it (`sick-day…research.md:325`).
- **The two citation fixes (RR-8).**
  1. Annex A names *"Statens arbejdstidsaftale … § 8, stk. 2"*, the agreement. It does not name circular 022-26, which was issued on 18 Jun 2026, after the award. The provision is the same one (`…research.md:23`, `:355`).
  2. FV2026.573 (4 Sep 2026) is a private-sector award, so do not cite it as a state rule. But the five-year limitation it applied to back-pay claims is general law (`:26`, `:348-351`). Back pay is outside StatsTid's scope either way.

**The options** (ADR-043 F-7, `:307-311`).

*The shape, told and not asked: (c), a dated, named rule variant* (D-10.3).
- **One encoding now, behind a named, dated slot** (C4's `PartTimeBandRule`, `agreement-cells…:557-559`). Only the chosen variant is built. The other arithmetic is built when the state's text and its date are known (D-10.2 item 5).
- **Every month's manifest names the variant that paid it,** so a replay can prove it (auditability).
- *Gives up:* a little more than an unnamed rule: a variant name and a from-date carried into the manifest.

*The value, asked:*
- **(ii) The award's direction: +50 % from the part-timer's own norm.** A part-timer is simply a full-timer with a smaller norm.
  - *Costs to build:* the least. There is no band: overtime starts at the own norm, the same arithmetic as a full-timer's.
  - *Gives up:* certainty. The implementing text may differ when it arrives, for example in its conditions, or in how it reads "comparable full-timer". The dated slot absorbs that on its date.
- **(i) The printed rule: hour for hour up to full time, +50 % only above it.**
  - *Gives up:* it knowingly computes a result the binding award has declared in breach; also more to build (D-10.2 item 6).
  - *Costs to build:* more than (ii) (D-10.2 item 4). The engine needs the full-time month norm as a second threshold, plus a 1:1 band wage type or time-off movement.
  - *Why (i) is still a valid answer:* which rule applies when a printed agreement in force and a binding award conflict is exactly the owner ruling the invariant model prescribes. This question is that ruling (the adjudication: no invariant escalation).

*Not offered:* ADR-043's (a) and (b) are (i) and (ii) without the dated slot (see "Not offered, and why" above).

**The lean: (c) for the shape, (ii) for the value — confirmed by the adjudicator** (D-10.3; ADR-043 `:311`). The reasons:
- It is the binding legal position (HIGH confidence; C4 `:566`; R4 `sick-day…research.md:309`).
- It is the simpler encoding.
- Today's engine already pays +50 % from the own norm (`WeeklyTimelineCore.cs:173-175`; `OvertimeRule.cs:61-80`), so (ii) moves no demo payslip on this point.
- The implementing text can only refine conditions, and the slot absorbs that on its date.
- (i) knowingly computes the result the award calls a breach, and it costs more to build.
- *No separate provisional declaration from the owner is needed:* the question's own wording carries the label "a value a qualified expert must confirm before any real use" (refinement `REFINEMENT-s147…:818`, `:917`).

**"Comparable full-timer" held as met for every part-timer** — *recorded proposal — adjudicated, told to the owner, not asked* (D-10.4).
- **The proposal:** the award covers part-timers who have comparable full-timers. StatsTid treats that condition as met for every part-timer and checks nothing.
- **Why, corrected** (D-10.2 item 7). Draft 1 said StatsTid "has no data to test it". That overstated it: `employee_profiles` holds position and employment category beside the part-time fraction (`docs/generated/db-schema.md:258-271`). But a crude match on those would not be the legal test. The better reason: where no comparable full-timer exists, the directive's own definition falls back to the applicable collective agreement (clause 3(2) of the framework agreement annexed to Directive 97/81/EC). *Adjudicator's knowledge — verify before quoting in the ADR.*
- **In the owner prose, one sentence:** nothing about comparable full-timers is checked — the agreement itself supplies the comparison.
- **Build input:** no data field and no check. Flagged for the expert, with the AC note below.
- It lives in the one list of provisional items (`../ADR-043-working.md`, "Provisional items and recorded proposals", item 7).

**A gap, not a blocker: AC-agreement part-timers** (D-10.2 item 8). This brief is silent on part-timers under the AC agreement, and they were not examined. The AC case is a note for the expert and, in the adjudicator's words, "a check in D-5's build".

**What the owner should know** (D-10.5, with D-10.4).
- **Only ordered or accepted hours are touched.** An unordered extra hour is flex, hour for hour, under both answers.
- **Under (ii) a part-timer is a full-timer with a smaller norm.** It is the simplest rule, and it is what the engine already does today on this point.
- **The example:** a 30-hour part-timer with 3 ordered hours gets 3 h off or 3 h at ordinary pay under (i), and 4.5 h off or 3 h plus 50 % under (ii). The employer chooses which (D8).
- **The state's text will come.** The printed circular of June 2026 still carries the old rule; the award is from February 2026; the unions say the state's text is "not yet in place"; and 12 million kroner is set aside for the negotiation. When the text comes, StatsTid encodes it with its date, and earlier months are reseeded.
- **Every month's calculation records which variant paid it.**
- **Back pay is outside StatsTid's scope.**
- **Nothing about comparable full-timers is checked;** the agreement itself supplies the comparison.
- **AC-agreement part-timers were not examined.**
- **A qualified expert should confirm the default and the comparable-full-timer assumption before any real use.**

**When it is asked** (the adjudication's order). After D-11, as the seventh independent question, and before D-4. Its wording rests only on ADR-043 D3's "ordered or accepted" direction (OQ-3 = (b)), not on D-4.

**What the S148 build gets from each answer.**
- **Placement:** D-1 = (b) Stages (2026-10-29), so D-10 is built in **stage 2**, where ordered hours become overtime. Before stage 2 no hour is overtime, so the band never arises. Building it still waits on D-4, the marking workflow that records a leader's order.
- **(c), default (ii):** the `PartTimeBandRule` slot with one variant built, the award's direction: overtime from the part-timer's own norm, the same arithmetic as a full-timer's. The variant's name and from-date are frozen into every manifest. No comparable-full-timer field or check.
- **(c), default (i):** the same slot with the printed rule's variant: the full-time month norm as a second threshold, a 1:1 band wage type or time-off movement, and +50 % only above full time. Same manifest recording.
- **Under either answer,** the state's text, when it arrives, becomes a new dated variant in the same slot.

**The ADR-043 text it rewrites.**
- F-7 (`:307-311`) becomes a decision.
- D4's part-timers bullet (`:103-105`). Under (ii) it must say that § 8 stk. 2's 1:1 band is not applied, by owner ruling of the date, provisional until the state's implementing text, citing FV2025-795 (D-10.3). Under (i) it records the printed rule as the provisional default, with the same date and label.
- The provisional default joins the ADR's single list of provisional items (D-9.4; `../ADR-043-working.md`).
- The Consequences sentence on dated rules (`:205-207`) names the slot.
- QUAL-223 item (3) gets its route.
- The recorded proposal above becomes ADR text when this question is answered.
- *Orchestrator docs items at acceptance (D-10.3; listed, not the planner's to do):* the source-register row names the award and the ruling (QUAL-215's lesson); `danish-agreements.md:64` gets a dated note (ADR-043's documents-owed list, `:219-223`).

**Waits on.** Nothing, for the asking (R4 ✓). Building waits on D-4; D-1 is answered (stages; built in stage 2).

---
*Revision history.*
- Draft 1 (2026-10-28): first brief, on the coordinator's ruling (1).
- Draft 2 (2026-10-29): the adjudication applied (`.claude/reviews/S147-D-10-adjudication.md`, D-10.1–.5). Added "The question as it will be asked" ((ii) first) with the three options not offered and why; folded D-10.2's eight corrections (verification sha `b2ece3a1`; full paths and `OvertimeRule.cs` lines; the +50 % derived, not quoted; (i)'s higher build cost; one encoding behind a named, dated slot; (i)'s cost line reworded; the comparable-full-timer rationale, marked "verify before quoting"; the AC part-timer gap); the lean confirmed for shape and value; the comparable-full-timer item marked as a recorded proposal; "What the owner should know" added; the asking order added (after D-11, before D-4); the build placement filled from D-1 = (b): stage 2.
