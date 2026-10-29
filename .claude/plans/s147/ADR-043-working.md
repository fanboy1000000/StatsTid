# ADR-043 — S147 working copy

| | |
|---|---|
| **What this is** | The S147 working copy of ADR-043, kept by the `planner` for the decide track (`.claude/plans/PLAN-s147.md` § "D — the decide track", step 4). As each owner ruling lands, the matching fork below is rewritten as a decision, and the ruling is recorded word for word in the table under this header. The knowledge-base file stays the authority (status PROPOSED) until the Orchestrator copies this text back at O-10, after one review by both lenses (NR-5). **At WC-1 (2026-10-29) the amended ADR text is written below the line marked "Amended ADR-043"**: everything between that marker and "End of the amended ADR-043 text" is what the Orchestrator copies to the knowledge base |
| **Seeded from** | `docs/knowledge-base/decisions/ADR-043-overtime-flex-monthly-norm.md` at `e7478f24` (master HEAD, after O-1's four drift fixes). Until WC-1 the text below the line was that file unchanged. **WC-1 replaced the verbatim seed with the amended text.** Nothing is lost: the knowledge-base file is still that seed at `c4eb6184` (unchanged since `e7478f24`), so a reviewer diffs the amended section against it |
| **Companion** | `S148-BUILD-INPUT.md` (WC-1): the amended ADR cut into S148's stages, with the census and retirement lists |
| **Rule for rulings** | Each answer is copied verbatim into the table: the same text as the `SPRINT-147.md` ledger row, with its date. It is never paraphrased. The rewritten fork cites its table row |
| **Briefs** | One per question, in `.claude/plans/s147/decide/` (index: `decide/README.md`) |

## Rulings (S147)

| D-id | Question | Owner's answer (verbatim) | Date |
|---|---|---|---|
| **D-1** (F-1) | *Should S148 move payroll to the new overtime/flex model in one cut-over, or in self-contained stages — where each stage is correct for ordinary (unordered) hours, and the sprint log names what the stage does not yet handle: until the stage that records a leader's order lands, ordered hours go to flex hour for hour and earn no supplement?* | "(b) Stages (Recommended)" | 2026-10-29 |
| **D-2** (QUAL-234 → new D10) | *How should the clock times an employee enters in Skema reach the supplement calculation and the 11-hour rest check: should payroll read them where Skema already keeps them (one more Backend table in payroll's read-only contract, frozen into the calculation's proof), should the Skema save stamp derived start/end times onto each project's hours, or should the employee enter clock times per project?* | "(a) Read work intervals (Recommended)" | 2026-10-29 |
| **D-3** (QUAL-232 → new D11) | *On-call duty, call-ins and travel time: S148's stage 0 will refuse them rather than pay them wrongly (already decided). Should StatsTid still be required to handle them afterwards, counting them as the state agreement does — on-call at home 1/3 of its hours, at the workplace 3/4; a call-in at least 3 hours; travel in full up to 13 hours per working day; for AC-agreement staff, travel only — built in a later stage, or should the refusal be the end state and the requirement dropped from the product definition?* | "(c) Keep, build later (Recommended)" | 2026-10-29 |
| **D-6** (QUAL-233 with QUAL-204; ADR-014) | *Should payroll calculate with the agreement rules a GlobalAdmin publishes in the editor and with each institution's local agreement profile, as the product specification already requires — a stage of its own in S148, with every configured value dated and frozen into the calculation's proof — or should those editors be declared not to affect pay, either for everything or for central agreements only?* | "(a) Editors reach pay (Recommended)" | 2026-10-29 |
| **D-9** (F-6) | *For staff on the AC agreement, should flex settle each calendar month (as for HK and PROSA; a local agreement may still set another settlement period under D-8), with extra work (merarbejde) judged claim by claim over the period each claim covers and reported at least quarterly — or should StatsTid impose a fixed assessment period per AC group (quarter for administrative AC, year for special consultants)?* | "(a) Monthly flex, per claim (Recommended)" | 2026-10-29 |
| **D-11** (F-8) | *Whether an ordered extra hour earns an HK or PROSA employee +50 % overtime, a discretionary extra-work claim (PROSA pay groups 3–4), or nothing (HK staff above the pay ceiling, who get a one-off chef-pay payment instead) depends on their pay — which StatsTid does not hold. How should StatsTid know a person's regime: should HR record it as a dated, audited fact on the employee; should StatsTid import base pay and compare it with the threshold; or should it infer the regime from the position?* | "(b) HR records regime (Recommended)" | 2026-10-29 |
| **D-10** (F-7) | *A part-timer whose leader orders extra hours that still keep them at or below a full-timer's hours sits in a "band" between their own norm and full time. […full wording: `decide/D-10-part-timer-default.md`, "The question as it will be asked"…] Until then, which rule is the provisional default — a value a qualified expert must confirm before any real use: the award's direction, under which ordered hours above the part-timer's own norm earn the same +50 % as a full-timer's overtime (a part-timer is simply a full-timer with a smaller norm), or the printed rule, under which ordered hours up to full time are compensated hour for hour and +50 % applies only above full time?* | "(ii) Award: +50 % (Recommended)" — recorded as **F-7 = (c), default (ii)**, provisional pending a qualified expert | 2026-10-29 |
| **D-4** (F-2, with SEC-056) | *Under the new model an hour above the month's norm becomes overtime, or counts toward an extra-work claim, only when a leader ordered it or accepted afterwards that it was necessary — and an evening or weekend hour earns its supplement only when its placement was ordered or rostered. StatsTid must therefore record those decisions hour by hour; a budget of "up to N hours" cannot say which hours, so it is not offered. How should the record be made: should a leader be able to record an order, or a roster span, beforehand — naming the date and clock span — with work nobody ordered in advance marked by the employee and accepted or rejected by the leader afterwards; or should every decision be made afterwards, the employee marking the hours and the leader deciding them when approving the month, with no separate record of an order given in advance?* | "(c) Both (Recommended)" | 2026-10-29 |
| **D-7** (F-4) | *Some state institutions' flex agreements fix office hours for each weekday (PAV's "kontortid" model). Inside those hours even work a leader ordered stays in flex hour for hour, and a day of paid absence — a sick day, a holiday day — is credited with that weekday's office hours instead of the flat 7.4 h. Should StatsTid let an institution switch this on, as per-weekday office hours in its dated local settings, off unless set; or should office hours be left out, so that every ordered or accepted hour leaves flex and every paid-absence day is credited 7.4 h × fraction?* | "(b) Opt-in, off by default (Recommended)" | 2026-10-29 |
| **D-8a** (F-5) | *Every sampled local flex agreement leaves some staff out of flex — special and chief consultants, managers, flex-job staff — and one gives a staff group its own cap. Should an institution's flex rules in StatsTid be able to treat groups of its staff differently, and how far: one set of rules per institution and agreement, everyone on it; one set of rules plus a dated fact per person, recorded by HR, saying whether that person is on the flex scheme at all; or full rule sets per staff group, each with its own caps?* | "(b) Rule set + HR yes/no (Recommended)" | 2026-10-29 |
| **D-8b** (Q-1) | *For staff on the AC agreement no central text says how a paid sick day counts in the flex account; for HK and PROSA the state working-time agreement credits the day's planned hours, else 7.4 h × fraction. Which default should StatsTid apply where an institution's local agreement has not set one: the HK/PROSA rule, labelled as a default by analogy that a local agreement can change; or no default, so that an AC month with a sick day cannot be calculated until the institution sets the rule?* | "(a) HK/PROSA rule, labelled (Recommended)" | 2026-10-29 |
| **D-5** (F-3, with SEC-056) | *Told, no owner question* (batch-3 ruling 1): the movement ledger (F-3 (b)); the § 10 clock from the worked period; the employer's choice, time off first; the saved-days balance designed (P10) after research cell R-A | told 2026-10-29 — "this one has no question; say so if you want any of it asked"; the owner asked for nothing to be asked | 2026-10-29 |
| **D-12** (F-9) | *Some state institutions give their staff a paid day or afternoon off on 5 June, 24 December, 31 December or 1 May by local custom; others close on those days and staff cover them with their own leave or flex. None of these days is a public holiday, so none lowers the month's norm; a paid customary day is a day of paid absence the system credits, so flex does not move. Should an institution be able to record both kinds of customary day in StatsTid — each dated, and marked either as a paid day off (credited like a paid-absence day, whole or part; given to everyone or on request) or as a closure day (shown in Skema; the employee covers it with leave, time off in lieu or flex) — or paid days off only, with closure days left unrecorded?* | "(c) Both kinds (Recommended)" | 2026-10-29 |

*(The decide track is complete on 2026-10-29: D-1 … D-12 answered (D-8 as D-8a and D-8b; D-5 told). Next: the planner writes the amended ADR-043 text from the rulings table and the one list; then one dual-lens review of the amended ADR; then ADR-043 ACCEPTED (O-10) and the S148 build input.)* *(WC-1, 2026-10-29: the amended text is written below, and the S148 build input beside it; next is the dual-lens review.)* *(WC-2, 2026-10-29: cycle 1's adjudication applied, `.claude/reviews/S147-ADR-043-adjudication-c1.md`; next is the narrow cycle 2 on both lenses.)*

*(D-1 answered 2026-10-29: stages, each with its own evaluation-model marker. The interim limits were told with the question: ordered hours go to flex 1:1 with no supplement until stage 2; AC extra work is flex only until stage 3; reseed per stage; stage 1 moves every HK/PROSA demo payslip. D-1.4 "no supplement in stage 1" stays a recorded proposal and becomes ADR text with this ruling. Adjudication: `.claude/reviews/S147-D-batch1-adjudication.md` § D-1.)*

*(D-2 answered 2026-10-29: the rest-check half in S148 stage 0 (Backend compliance, no payroll marker; T7's notice retires); the supplement half in stage 2 (ADR-016 D10 amended; intervals in the digest and manifest); one source of time of day per hour. D-2.4 "manual hours retired" becomes ADR text with this ruling. D-3 answered the same day: refusal first (stage 0), the sourced three-column model in a later stage (stage 2 or later), SYSTEM_TARGET §C's travel line reworded to the sourced rule. D-6 answered the same day: a config stage of its own, before the first stage that needs a local value; D-6.5's stage-0 label stays a recorded proposal; QUAL-204 answered (a local profile changes pay). D-9 answered the same day after the owner asked for, and got, an expansion of why the lean departs from C5's quarter; D-9.4 "AC's month norm uses D1's function" becomes ADR text as a labelled default. Next asked: D-11, then D-10.)*

## To tell the owner at acceptance (O-10)

*What this is.* The dual-lens review of the amended ADR (cycle 1) was adjudicated in `.claude/reviews/S147-ADR-043-adjudication-c1.md`. It altered no owner decision, but two of its rulings, R-4 and R-6, change provisional or staging text the owner was told with the questions. The Orchestrator reads these two sentences to the owner at acceptance, as information, not as questions (the adjudication's "Owner decisions altered?", verbatim):

1. *(R-4)* "For AC staff the regime HR records has the three values ADR-024 already seeds — contractual claim / discretionary / none — all provisional; 'discretionary' means no claim in StatsTid and any one-off payment outside it."
2. *(R-6)* "The config stage changes no pay figure; the central caps go away and the demo caps bite in stage 1, with forfeiture, as D6's table already said."

## Provisional items and recorded proposals (not owner rulings)

*What this list is, in plain words.* When the adjudicator reviewed the question briefs (the first six together, then D-10 on its own, then D-4, D-7 and D-8 together as batch 2, then D-5 and D-12 as batch 3), it settled some smaller points itself instead of turning each into one more owner question. Each is either a sensible default the owner is *told* about in the question's prose but not *asked*, or a point held for a domain expert or for the S148 build. None of them is an owner ruling, and none goes in the rulings table above. This is the one list for such items (D-9.4 asks for exactly one). The text of the ADR below does not change until the related question is answered. *(WC-1: every related question is now answered or told, and every item is folded into the amended text below; the fold map after this list says where. In the amended ADR itself, the provisional values among them form its one list "Provisional items (owner defaults pending a qualified expert)".)*

1. **No supplement in stage 1.** If the model is built in stages, stage 1 pays no unsocial-hours supplement, because the +25 % needs the "ordered or rostered placement" fact that only stage 2 records; `SUPPLEMENT_CALC` stays evaluated and returns no lines. *Anchor:* `.claude/reviews/S147-D-batch1-adjudication.md` § D-1 (D-1.4); brief `decide/D-01-order-of-work.md`. *Related question:* D-1 (F-1). *Status:* recorded proposal; becomes text of the ADR when the related question is answered.
2. **Manual hours are retired.** The leftover "manual hours" figure in Skema is retired (reseed), so every worked hour has clock times; the census of its readers is in the D-2 brief for the build input. *Anchor:* § D-2 (D-2.4); brief `decide/D-02-clock-times.md`. *Related question:* D-2 (QUAL-234). *Status:* recorded proposal; becomes text of the ADR when the related question is answered.
3. **AC's month norm uses D1's function.** AC's monthly flex uses the same month-norm function as HK and PROSA (7.4 h a working day, pro rata), with the labelled default that the norm falls on søgnehelligdage (public holidays on a weekday), held for the domain expert; how flex settles for the annual-norm variants (`AC_RESEARCH`, `AC_TEACHING`) is a gap for the build input. *Anchor:* § D-9 (D-9.4); brief `decide/D-09-ac-period.md`. *Related question:* D-9 (F-6). *Status:* recorded proposal; becomes text of the ADR when the related question is answered.
4. **One regime attribute also covers AC.** The HR-recorded overtime regime also decides whether an AC employee can raise a merarbejde claim at all, on three conditions: the header gains "amends ADR-024 D1/D2" and ADR-024's category overrides become the seed, not a second source; one value set for the three agreements, the AC values PROVISIONAL; for AC the attribute decides whether a claim can be raised. *Anchor:* § D-11 (D-11.4); brief `decide/D-11-pay-ceiling.md`. *Related question:* D-11 (F-8). *Status:* recorded proposal; becomes text of the ADR when the related question is answered.
5. **A stage-0 label on the editors.** Until payroll reads the editors' configs, the agreement and local-profile editors say "affects screens and booking; does not yet affect pay", so nobody believes an edit changed pay. *Anchor:* § D-6 (D-6.5); brief `decide/D-06-config-source.md`. *Related question:* D-6 (QUAL-233, QUAL-204). *Status:* recorded proposal; becomes text of the ADR when the related question is answered.
6. **Home on-call's rest flag is a domain-expert item.** Whether home on-call counts as rest for the 11-hour rule is decided case by case (rest unless it defeats rest's purpose), so its default is held for a domain expert and never hard-coded as always rest. *Anchor:* § D-3 (D-3.5); brief `decide/D-03-worked-time-classes.md`. *Related question:* D-3 (QUAL-232). *Status:* recorded proposal; becomes text of the ADR when the related question is answered.

7. **"Comparable full-timer" is held as met for every part-timer.** The 4 Feb 2026 award covers part-timers who have comparable full-timers; StatsTid treats that condition as met for every part-timer and checks nothing (no data field, no check). The reason: where no comparable full-timer exists, the part-time directive's own definition falls back to the applicable collective agreement (clause 3(2) of the framework agreement annexed to Directive 97/81/EC — adjudicator's knowledge, to be verified before it is quoted in the ADR); a crude match on position and category would not be the legal test. Flagged for the domain expert, together with the note that AC-agreement part-timers were not examined. *Anchor:* `.claude/reviews/S147-D-10-adjudication.md` (D-10.4, with D-10.2 items 7 and 8); brief `decide/D-10-part-timer-default.md`. *Related question:* D-10 (F-7). *Status:* recorded proposal; becomes text of the ADR when the related question is answered.
   - *Not on this list, but related:* the value itself, (ii) the award's direction or (i) the printed rule, is **asked** (lean (ii), confirmed by D-10.3). Once answered it is a provisional owner default, pending a qualified expert: the ruling goes in the rulings table above, and its provisional label joins this list (D-10.3).
   - *ADR text owed at acceptance (D-10.3):* D4's part-timers bullet (`:103-105` in the knowledge-base file) must say that § 8 stk. 2's 1:1 band is not applied, by owner ruling of the date, provisional until the state's implementing text, citing FV2025-795. (That is the sentence under default (ii); under (i) the bullet records the printed rule as the provisional default, with the same date and label.)
   - *Orchestrator docs items at acceptance (D-10.3; listed here, not done by the planner):* (1) the source-register row names the award and the ruling (QUAL-215's lesson); (2) `docs/references/danish-agreements.md:64` gets a dated note (the ADR's documents-owed list, `:219-223` in the knowledge-base file).

*Batch 2 (`.claude/reviews/S147-D-batch2-adjudication.md`, 2026-10-29).* Items 8–26 are adjudicated recorded proposals. Each becomes ADR text when its related question is answered: items 8–13 with D-4, items 14–19 with D-7, and items 20–26 with D-8a and D-8b. The briefs carry the detail: `decide/D-04-marking-workflow.md`, `decide/D-07-office-hours.md`, and `decide/D-08-local-flex-parameters.md` (the parent of `D-08a-flex-membership.md` and `D-08b-ac-sick-day-default.md`).

8. **D-4.P1, the cut-off and the decider.**
   - A mark is decided before its month is approved, and the month cannot be approved while one is undecided.
   - The decider is whoever holds the month's approval authority: the designated approver, a stand-in under Q-B = (c), or HR for a leaver's final month under OQ-2. It is never the mark's creator or the employee.
   - *Anchor:* § D-4 (D-4.4; D-4.2 item 6).
9. **D-4.P2, a late decision is a dated movement in the decision month, never a reopening** (ADR-013 and ADR-034 hold).
   - The 3-month time-off clock still runs from the end of the norm period in which the hours were worked. That fact is handed to D-5.
   - *Anchor:* § D-4 (D-4.4; D-4.2 item 7).
10. **D-4.P3, "placement ordered or rostered" is its own field,** separate from "surplus ordered or necessary".
    - It gates § 13's +25 % and § 14's +50 %, for exempt staff too.
    - *Anchor:* § D-4 (D-4.4).
11. **D-4.P4, four-eyes on ruling 1's four conditions** (SEC-056).
    - A leader's order is a decision at creation, and its creator is its decider.
    - A decision is never on the creator's own hours: T9's rule is carried to every record, with a 403.
    - Creator-not-approver applies to every record that awaits a decision.
    - The authority to order or decide is the authority to approve the employee's month, not bare org scope.
    - *Anchor:* § D-4 (D-4.4) and ruling 1.
12. **D-4.P5, room for typed time:** the record carries an activity type, `NORMAL` until D-3's later stage.
    - *Anchor:* § D-4 (D-4.4).
13. **D-4.P6, the pre-approval switch and the advisory governance check retire.**
    - `MaxOvertimeHoursPerPeriod` becomes an optional warning over decided hours, in D-8's list, with no default.
    - SYSTEM_TARGET §L is rewritten at acceptance.
    - *Anchor:* § D-4 (D-4.4; D-4.2 item 4).
14. **D-7.P1, office hours are a clock window per weekday.** The editor warns, but does not refuse, when the five windows do not add up to the full-time week's norm (37 h; the profile's own `weekly_norm_hours` where one is set, Prov. 18); how a part-timer's windows scale is Prov. 14 (D-7.P1 as corrected).
    - *Anchor:* § D-7 (D-7.4; D-7.2 item 3), as corrected by `.claude/reviews/S147-ADR-043-adjudication-c1.md` R-5: the windows are set per institution and the fraction is a person's, so the warning compares with the full-time week.
15. **D-7.P2, the third condition uses PAV's daily form:** only hours after the day's first 7 h 24 min can leave flex.
    - Ordered weekend and søgnehelligdag work is § 14's first, whatever the office hours. The expert confirms this.
    - The AU monthly variant is recorded, not built.
    - *Anchor:* § D-7 (D-7.4; D-7.2 item 2).
16. **D-7.P3, when office hours are set, every paid-absence day is credited with the weekday's office hours.**
    - One served "planned hours" figure is read by the credit, the absence cap and the vacation divisor.
    - *Anchor:* § D-7 (D-7.4; D-7.2 items 1 and 4) and ruling 4.
17. **D-7.P4, the month norm does not change.** Only the credit, and which ordered hours leave flex, change.
    - *Anchor:* § D-7 (D-7.4).
18. **D-7.P5, part-timers under office hours are a domain-expert item.**
    - *Anchor:* § D-7 (D-7.4).
19. **D-7.P6, core hours (*fikstid*) are not modelled for pay.** They are listed in D-8's parameter list.
    - *Anchor:* § D-7 (D-7.4).
20. **R-1, local flex rules are typed, dated columns on `local_agreement_profiles`.** D-12's customary days are a dated table beside the profile.
    - *Anchor:* § D-8.4.
21. **R-2, the parameter list, with five amendments:**
    - (i) there is no central flex cap (ruling 2);
    - (ii) S148 builds the settlement period with the one value `month`;
    - (iii) `MaxOvertimeHoursPerPeriod` is an optional warning;
    - (iv) `flex_carryover_max` retires, or becomes the cap-test period's form;
    - (v) a manager's cap exception is a dated record for one person, decided by someone with approval authority, never on the decider's own balance, with an HRP row.
    - *Anchor:* § D-8.4; D-8a.2 items 2–3.
22. **R-3, QUAL-219: hours above the cap are forfeited at the cap test,** recorded and visible.
    - `FLEX_PAYOUT` survives only as "1:1 cash by agreement".
    - Nothing is forfeited until the institution sets its cap.
    - *Anchor:* § D-8.4.
23. **R-4, a leaver's flex balance is settled in the leaving month,** never carried to a rehire.
    - The labelled defaults: plus hours are forfeited unless taken during notice; minus hours are recorded, not deducted. A pay deduction is a local option.
    - *Anchor:* § D-8.4.
24. **R-5, QUAL-225: an HR-entered, audited opening flex balance** is a recorded design proposal. Its sprint is S148's refinement's.
    - *Anchor:* § D-8.4 and ruling 3.
25. **R-6, the paid-absence credit basis** is a local parameter.
    - Its default is § 7 nr. 2. The weekday's office hours apply only under D-7 = (b).
    - It covers every paid-absence day.
    - *Anchor:* § D-8.4 and ruling 4.
26. **R-7, QUAL-231's residual: a day's sick hours may not exceed the credit basis minus the hours worked that day.** *Widened to every credit-class type (batch-3 ruling 3):* "a day's paid-absence hours, of any credit-class type, may not exceed the credit basis minus the hours worked that day; judged at the Skema save in both directions (QUAL-237)".
    - It is judged in both save directions, against the day's stored state (QUAL-237), or the engine computes it.
    - Stage 0; the MEDIUM label is kept.
    - *Build-input note (ruling 3):* for quota types (absence codes that draw on an entitlement balance), a backstop that clips the credit must also clip the consumption, or refuse the month.
    - *Orchestrator item at close (ruling 3):* QUAL-231's and QUAL-237's register rows gain the sentence.
    - *Anchor:* § D-8.4; `.claude/reviews/S147-D-batch3-adjudication.md` ruling 3.

*The adjudicator's five batch-2 rulings: told to the owner by the Orchestrator in the question prose, not asked* (`.claude/reviews/S147-D-batch2-adjudication.md`, "The five rulings"). The items above carry them; they are listed here so that no told point is lost.
- **Ruling 1 (D-4.P4):** a leader's own order is a decision, on four conditions (item 11). It is told in one sentence in D-4's prose.
- **Ruling 2 (R-2):** there is no central flex cap; the 100/120/150 h were never sourced. SYSTEM_TARGET §G's "central limit" (`SYSTEM_TARGET.md:168`, `:174`) is corrected at acceptance (item 21). It is told in D-8a's prose.
- **Ruling 3 (R-5):** QUAL-225's opening balance is a recorded design proposal, and its sprint is S148's refinement's (item 24). The owner is told.
- **Ruling 4 (D-7.P3 / R-6):** the credit basis covers every paid-absence day. RR-12 is refined, not reversed (items 16 and 25). It is told in D-7's and D-8's prose.
- **Ruling 5:** the D-8 split stands (D-8a, D-8b), and D-4's (b) and D-8b's (c) are not offered. Each is told as "not offered", with its reason, in its question's prose.

*Batch 3 (`.claude/reviews/S147-D-batch3-adjudication.md`, 2026-10-29).* Items 27–45 are adjudicated recorded proposals; the batch also widened item 26 (ruling 3). In their anchors, "§ D-5" and "§ D-12" are sections of that file. The briefs carry the detail: `decide/D-05-overtime-account.md` and `decide/D-12-customary-days-off.md`.
- **Items 27–36 (D-5.P1–P10).** D-5 has **no owner question** (ruling 1): it is told in full. These items become ADR text once the Orchestrator has told D-5, unless the owner asks for any of it to be asked.
- **Items 37–45 (D-12.P1–P9).** They become ADR text when D-12 is answered. Item 42 (closure days) applies under (c) only; under (a) it lapses.

27. **D-5.P1, the overtime account is a movement ledger: F-3 (b), told, not asked.** Only a ledger can apply the § 10 clock; a yearly total (a) cannot say which hours reached their deadline, and rails on the export record (c) are excluded by F-3's own text.
    - The movements: earned (norm period, hours, rate, kind: § 8, § 14, AC merarbejde in stage 3), taken as time off, paid, postponed, converted to saved days. Each is dated, audited and frozen into the manifest of the month it lands in, and none edits an exported month.
    - One writer per table. The balance and each earning's deadline are derivations the pure engine recomputes from frozen inputs, never a stored total anyone adjusts.
    - Where the movements are stored (one table, or a Backend fact table plus Payroll's own movement table under the read-only contract, the D-2 = (a) precedent) is S148's refinement's call.
    - *Anchor:* § D-5 (D-5.1; D-5.4; D-5.2 item 3).
28. **D-5.P2, compensation only behind an approved movement (SEC-056), on batch-2 ruling 1's four conditions.**
    - A decision about another person's account (granting time off, paying early, recording a postponement) is made by whoever holds that employee's month-approval authority, never on the decider's own hours (403); a request that awaits a decision is decided by someone else.
    - The employee's two elections, postponing and saving, are the employee's own acts, not compensation decisions.
    - The compensate and compensation-choice routes retire with the yearly row.
    - *Anchor:* § D-5 (D-5.4).
29. **D-5.P3, the employer chooses, and time off comes first** (V10, C7; QUAL-221).
    - The default is time off at +50 % (6 decided hours become 9 hours off); pay when time off is judged impossible or inappropriate, recorded with a reason.
    - `EmployeeCompensationChoice` and the choice routes retire. Payroll gets hours and wage types, never kroner; the 100 % rows retire.
    - *Anchor:* § D-5 (D-5.4).
30. **D-5.P4, the clocks are facts, not questions.**
    - § 8 overtime: time off within 3 months of the end of the norm period worked; then pay in the pay run of the month after the deadline month (under the monthly norm the deadline is a month end), unless postponed by agreement or converted to saved days. The same timing for § 14. The domain expert confirms.
    - § 14 weekend compensation: time off by the end of the following norm period, else cash.
    - The saved-days election is recorded before the calculation that would pay the default runs; whether it may be made earlier is for R-A.
    - A late decision dates its movement in the decision month, but its clock runs from the worked period. AC's deadline is R-B (stage 3). The part-timer band earns +50 % (D-10).
    - *Anchor:* § D-5 (D-5.4; D-5.2 items 4 and 5; D-4.2 item 7).
31. **D-5.P5, taking time off in lieu is a Skema absence** in the credit class (TASK-14708's classification), credited by the paid-absence credit basis (item 25).
    - It draws on the earning with the earliest deadline first, cannot exceed the time off owed, and is approved with the month.
    - *Anchor:* § D-5 (D-5.4; D-5.2 item 7).
32. **D-5.P6, a postponement is a record both parties sign.** It is agreed between the decider and the employee, dated and audited, and moves one earning's deadline by up to a year.
    - *Anchor:* § D-5 (D-5.4).
33. **D-5.P7, the deadline is a visible hand-off: HRP-021 becomes decision-ready.**
    - Trigger: an earning's deadline approaching or passed. Accountable: the employee's month approver (V10, C7). Surfacing: the leaders' "time off due" list and the employee's account screen. Resolution: time off granted, pay by default, a postponement or an election, each audited.
    - The default (pay) is computed in the month's calculation and shown, never silent.
    - *Anchor:* § D-5 (D-5.4); ruling 4(d).
34. **D-5.P8, the dated *opsparing af frihed* election is not modelled.** Its window closed on 31 March 2026.
    - *Anchor:* § D-5 (D-5.4).
35. **D-5.P9, a leaver's owed time off is paid, never forfeited.**
    - (i) Derived from § 8 and § 10: pay is the fallback whenever time off is not given.
    - (ii) The timing ("the leaving month") and saved days at leaving are unsourced: a labelled default and an expert item (R-C). It sits beside R-4 (item 23), in HRP-011's leaver lifecycle.
    - *Anchor:* § D-5 (D-5.4; D-5.2 item 6).
36. **D-5.P10, the saved-days balance is designed: a recorded design proposal.**
    - The design: an election screen, a Skema "saved day" type in the credit class, and the cap as a local parameter with the dated central floor (74 h, then 111 h from 1 April 2026).
    - A research cell on § 11 (R-A) comes first. Its sprint is S148's refinement's.
    - Until it lands, the election is recorded as a movement and a saved day taken is registered as paid leave, named in the sprint log.
    - *Anchor:* § D-5 (D-5.1, D-5.3) and ruling 1.
37. **D-12.P1, customary days live in a dated, audited table beside the local profile** (item 20).
    - Each row: the day, the kind (paid day off or closure day), the part (whole, or from a clock time), the mode for paid days, and a validity period, so withdrawal and reinstatement are dated facts. Edited by the institution's admin; keyed by date, not OK version; frozen into the manifest.
    - An agreement scope fits the table; a staff-group scope has no machinery under D-8a = (b).
    - *Anchor:* § D-12 (D-12.4; D-12.2 item 4).
38. **D-12.P2, a paid customary day is a credit, not a norm cut.** It is a paid-absence day in the credit class, read by the paid-absence credit basis (item 25); D1's norm and D9's closed list of ten are untouched.
    - *Anchor:* § D-12 (D-12.4).
39. **D-12.P3, part days are a clock time or hours.** The office-hours form where an institution sets office hours (D-7 = (b)), else 7.4 h × fraction. The scaling is an expert item.
    - *Anchor:* § D-12 (D-12.4; D-12.2 item 1).
40. **D-12.P4, a paid day is given to everyone or on request.**
    - They are different institutional facts: granted to all, or asked for by the employee and refusable by the leader "i den udstrækning tjenesten tillader det" (PAV 26.2.8.2), a booking the month approval decides.
    - Under item 26 as widened, "given to everyone" can never double-pay someone who worked.
    - *Anchor:* § D-12 (D-12.4; D-12.2 item 2) and ruling 2.
41. **D-12.P5, one credit per day.** The credited part nets off hours worked; a vacation or særlige feriedage booking consumes nothing for the credited part (D9 extended; ADR-032 D7); a sick day earns no second credit; ordered hours follow D-4.
    - *Anchor:* § D-12 (D-12.4, P5 extended).
42. **D-12.P6, a closure day earns nothing** (under (c) only). Skema shows it; the employee covers it; nothing is booked automatically. The send-month check's expected-working-days query (`ApprovalEndpoints.cs:2116-2127`) keeps it a working day.
    - *Anchor:* § D-12 (D-12.4; D-12.2 item 3).
43. **D-12.P7, the § 13 windows are separate.** Grundlovsdag from 12:00 and 24 December from 14:00 keep their +25 % for ordered work, whatever the custom.
    - *Anchor:* § D-12 (D-12.4).
44. **D-12.P8, recording a custom is configuration, not an HR hand-off.** No HRP row.
    - *Anchor:* § D-12 (D-12.4).
45. **D-12.P9, the demo seed sets example customs.**
    - *Anchor:* § D-12 (D-12.4).

*Owed research named by batch 3 (ruling 1).* **None blocks the ADR's acceptance;** the ADR lists them as owed research. (Letters keep them apart from the recorded proposals R-1..R-7 and the research gates R1..R5.)
- **R-A, § 11 in full:** who sets the saved-days maximum; how saved days are taken; whether they lapse; at leaving; whether § 14 time off may be converted; whether AC § 15 mirrors it (and, per D-5.2 item 5, whether the election may be made early). *Blocks:* building item 36 only; not telling D-5, nor stage 2's ledger.
- **R-B, the AC merarbejde time-off deadline** under § 14 stk. 5, if any. *Blocks:* stage 3 only.
- **R-C, a leaver's owed time off and saved days: timing and form.** *An expert item for S148's refinement* (item 35 (ii)).

*The adjudicator's four batch-3 rulings* (`.claude/reviews/S147-D-batch3-adjudication.md`, "The four rulings"). The items above carry them; they are listed here so that no told point is lost.
- **Ruling 1 (D-5):** the storage is told; the saved-days home is a scope item; there is no owner question; research cells R-A, R-B, R-C are named; P9 is relabelled (items 27–36). The Orchestrator tells D-5 first, ending "this one has no question — say so if you want any of it asked".
- **Ruling 2 (D-12):** (d) "not modelled" is not offered, alongside (b); P4's two modes are kept (item 40). D-12 is asked as (c)/(a).
- **Ruling 3 (R-7):** widened to every credit-class type (item 26, with its quota-type build note).
- **Ruling 4 (SEC-056, T9, the discarded result, HRP-021):** see the Orchestrator items below; HRP-021 is confirmed (item 33).

*Orchestrator items from batch 3* (not the planner's to do; listed here so they are not lost).
- **Ruling 4(a), T9 stands as briefed.** The TASK-14705 merge record says: *"the compensate guard protects a path no production write reaches at `26b70a08` (the balance is never fed); defence in depth until D-5 retires the route."* (The brief carries a dated note, `../TASK-14705.md`.)
- **Ruling 4(b), SEC-056's row is corrected at close.** The compensate half is a latent shape, not a live capability; the High label rests on approve/reject; the "→" cell gains "closed in design by D-4 = (c) with ruling 1 and by D-5.P2; T9 closes the self part in S147".
- **Ruling 4(c), the discarded result** (`OvertimeEndpoints.cs:569-576`) is QUAL-239 (Medium; registered 2026-10-29). It retires with D-5; SEC-056's row cross-references it at close.
- **Ruling 3, at close:** QUAL-231's and QUAL-237's rows gain R-7's widened sentence.
- **D-12.2 item 5, at acceptance:** `SYSTEM_TARGET.md:86` gains the third kind of day (with the other SYSTEM_TARGET text owed to the owner).

## Fold map (WC-1): where each item above sits in the amended text

*So a reviewer can check that nothing was dropped.* "Prov. n" is an entry of the amended ADR's list "Provisional items (owner defaults pending a qualified expert)"; "R-A … R-E" are its "Owed research".

| Items | Folded into |
|---|---|
| 1 (no supplement in stage 1) | D7, last bullet; stage map, stage 1 |
| 2 (manual hours retired) | D10; stage map, stage 0 |
| 3 (AC month norm) | D1, AC bullet; Prov. 4 |
| 4 (one regime attribute incl. AC; amends ADR-024 D1/D2) | D4, "The overtime regime"; D5, "Who may claim"; header "Supersedes / amends"; Prov. 5 |
| 5 (stage-0 editor label) | D12; stage map, stage 0 |
| 6 (home on-call rest flag) | D11; Prov. 13 |
| 7 (comparable full-timer held as met) and its two sub-bullets | D4, part-timers bullet (with D-10.3's sentence citing FV2025-795); Prov. 1, 2; R-D; "Documents owed" (`danish-agreements.md:64`, source register) |
| 8–13 (D-4.P1–P6) | D3 (cut-off and decider; four-eyes on ruling 1's four conditions; late decision as a movement; placement field; activity type; what retires); D7 (placement gate); D6 parameter table (`MaxOvertimeHoursPerPeriod`); "Documents owed" (§L) |
| 14–19 (D-7.P1–P6) | D3, office-hours bullet (P1, P2, P6); D2, credit basis and the planned-hours figure (P3); D1, last bullet (P4); Prov. 12 (P2's § 14 precedence), Prov. 14 (P5) |
| 20–22 (R-1, R-2, R-3) | D6 (storage, the parameter table with its five amendments, no central cap, forfeiture) |
| 23 (R-4) | D6, leaver bullet; Prov. 8 |
| 24 (R-5) | D6, opening-balance bullet; stage map, "Later or unplaced" |
| 25 (R-6) | D2, credit-basis bullet |
| 26 (R-7, widened) | D2, partly-worked-day bullet; Prov. 7; "Documents owed" (QUAL-231/QUAL-237 at close) |
| 27–36 (D-5.P1–P10) | D8, one bullet each; Prov. 9, 10; R-A, R-B, R-C; "Documents owed" (HRP-021, SEC-056, QUAL-239) |
| 37–45 (D-12.P1–P9) | D9, customary-days bullets; Prov. 15; "Documents owed" (`SYSTEM_TARGET.md:86`) |
| Batch-2 rulings 1–5 | ruling 1: D3, D8; ruling 2: D6; ruling 3: D6; ruling 4: D2; ruling 5: "Alternatives not chosen" |
| Batch-3 rulings 1–4 and the Orchestrator items | ruling 1: D8, R-A..R-C; ruling 2: D9, "Alternatives not chosen"; ruling 3: D2; ruling 4: D8 and "Documents owed" |

## Planner's notes: pending, not applied to the text below

*(WC-1: note 1's re-check is done; see its last bullet. Notes 2 and 3 are history.)*

1. **Citation drift since the ADR's verification sha `fdb52d48`.** The ADR's citations were correct at `fdb52d48`. Later S146 commits moved some of them. The text below keeps the old numbers verbatim. Spot-checked at `e7478f24`:
   - `SYSTEM_TARGET.md` (S146's dated notes, `19cf4bae`, added lines): `:243` → `:247` (the `NormPeriodWeeks` rule); `:163` → `:165` (§ G item 1); `:371` → `:377`; `:383` → `:391`; `:396` → `:408`.
   - `PeriodCalculationService.cs:2380-2388` (the marker comparison) → `:2758-2767`; `ReplayRefusedException.cs:38` → `:41` (S146 TASK-14605 grew the file). The ADR's other `PeriodCalculationService.cs` line numbers (`:2220`, `:3170`, `:3505`, `:3548`, `:3584`) were not re-checked and are probably stale too.
   - **Coordinator's ruling (2026-10-28):** re-checking every citation becomes an Orchestrator docs item before the ADR's dual-lens review. This list stays here as its input.
   - **Done at WC-1 (2026-10-29), at `c4eb6184`, on the Orchestrator's instruction.** Every file:line in the amended text was opened at `c4eb6184`; the old → new list is in the WC-1 revision entry at the bottom of this file and in the planner's report.
2. **Folds the briefs propose:** moved (WC-0.2) into the one list above, "Provisional items and recorded proposals (not owner rulings)". The D-1, D-2, D-9 and D-11 folds are now adjudicated recorded proposals (items 1–4); D-10's comparable-full-timer fold is one too since its own adjudication (item 7, `.claude/reviews/S147-D-10-adjudication.md`).
3. **Proposals in the D-5 and D-12 briefs:** moved (WC-0.6) into the one list above as items 27–45, after the batch-3 adjudication (`.claude/reviews/S147-D-batch3-adjudication.md`). D-5's proposed reframing was accepted in a stronger form: D-5 has no owner question at all, and the saved-days lean became item 36, a recorded design proposal. D-12 is asked as (c)/(a); (b) and (d) are not offered. (The D-4, D-7 and D-8 proposals this note held at WC-0.4 are items 8–26.) This note now holds nothing pending.

---

<!-- ===== Amended ADR-043 (S147, WC-1, 2026-10-29): the Orchestrator copies everything from here to "End of the amended ADR-043 text" into docs/knowledge-base/decisions/ADR-043-overtime-flex-monthly-norm.md at O-10 ===== -->

# ADR-043 — Overtime and flex: a monthly norm, flex by default, overtime only for ordered hours

| Field | Value |
|-------|-------|
| **Status** | **PROPOSED — amended 2026-10-29 from the S147 decide track; ACCEPTED after the dual-lens review (O-10).** Drafted in S146 (track D5) to the owner's direction OQ-3 = (b). In S147 the nine forks this ADR left open (F-1 … F-9) and three tender findings (QUAL-232, QUAL-233, QUAL-234) were put to the owner one at a time, each explained first, and answered on 2026-10-29; one of them, the overtime account (D-5), was told with no question, and the owner asked for nothing to be asked. Until acceptance nothing here binds code. S145's Q2 = A, Q3 = F1 and Q7 (b) stay "superseded in principle by the verified sources; in force in code until the model program replaces them; no new work on F1 machinery" (OQ-2 = (a), S146 refinement "Decisions recorded") |
| **Sprint** | S146 (draft) → S147 (rulings, amendment, acceptance) → S148 onward (built in stages; see "How it is built") |
| **Supersedes / amends (on acceptance)** | **Supersedes** the S145 owner rulings Q3 = F1 and Q7 (b); keeps Q2 = A only as the month norm's building block (D1). **Amends ADR-016 D10** in four places: the evaluation-model marker moves once per stage (the stage map); the read-only contract on Backend tables (`ADR-016:179`) grows from two tables to three with `work_time_projection` (D10), and the decided records of hours (D3) join it in stage 2, the hours digest (`:185`) and the manifest recording both, by value; "freeze what is data, fingerprint what is code" (`:169`) and the fingerprint contract (`:181`) give way to configs frozen as inputs (D12); the frozen pieces (`:171`) gain the overtime regime and flex membership. **Amends ADR-024 D1/D2:** the per-category merarbejde overrides become the seed of the HR-recorded overtime regime, not a second source pay reads (D4). **Amends ADR-024 D7:** from dormant design to the payroll path, its records naming clock spans rather than time entries, with a creator column, decided in Teamoversigt (D3). **Amends ADR-032 D7:** a vacation day on a søgnehelligdag, or on the credited part of a paid customary day, consumes nothing, and the vacation divisor reads the planned-hours figure (D2, D9). **Amends ADR-017:** the local profile gains typed, dated flex columns, a customary-days table sits beside it, and `overtime_requires_pre_approval` retires (D3, D6, D9). **Confirms ADR-014** on the pay path, with an implementation note (D12) |
| **Closes when built** | QUAL-189; QUAL-216 … QUAL-222; QUAL-223 (resolved into QUAL-227, F-7 and F-8); QUAL-226 (D1's one month-norm function); QUAL-227; QUAL-232 and QUAL-202 (D11); QUAL-233 and QUAL-182 (D12); QUAL-234 (D10); QUAL-231's residual and QUAL-237 (D2, stage 0); QUAL-239 (its interim in stage 0; retires with the compensate route, D8). **Answers** QUAL-204: yes, a local profile changes pay (D12). **Designs** QUAL-225, an HR-entered opening flex balance (D6). **Closes in design** SEC-056's creator-not-approver and compensation halves (D3, D8); S147's T9 closes its self part. QUAL-215 is the root cause; its lesson is recorded below |
| **Domains** | Rule Engine, Payroll Integration, Backend, Data Model, SharedKernel, Frontend, Security, Test |
| **Tags** | overtime, merarbejde, flex, norm-period, monthly-norm, public-holidays, supplements, compensation, ordered-work, forfeiture, office-hours, customary-days, clock-times, worked-time-classes, local-configuration, staged-build |
| **Evidence** | `docs/references/overtime-flex-model-research.md` (V1–V12), `docs/references/agreement-cells-reverification-research.md` (C1–C7) and `docs/references/sick-day-credit-and-agreement-texts-research.md` (R1–R5), each verified claim by claim; Part 1 of each governs. The owner's answers, verbatim: `docs/sprints/SPRINT-147.md`, "Owner rulings in force". The adjudications that settled each question's wording and its recorded proposals: `.claude/reviews/S147-D-batch1-adjudication.md`, `S147-D-10-adjudication.md`, `S147-D-batch2-adjudication.md`, `S147-D-batch3-adjudication.md`. Code citations verified at `fdb52d48` (S146) and re-verified at `c4eb6184` (S147) |

*How to read the labels.* **Dn** (no hyphen) is a decision of this ADR. **D-n** (with a hyphen) is one of S147's twelve decide-track questions, and **F-n** one of the nine forks this ADR left open in S146. The two series are different: D-10 is the part-timer question, D10 the clock-times decision. **V-n, C-n and R1–R5** are verified findings in the three dossiers above. **R-1 … R-7** and **D-4.P1 … D-12.P9** are the adjudicator's recorded proposals: told to the owner in the question's prose, not asked. **R-A … R-E** are research still owed.

## The decision, in one sentence

**An hour worked above the month's norm is a flex hour unless a leader ordered or accepted it. Only ordered or accepted
hours become overtime (HK, PROSA pay groups 1–2) or merarbejde (AC, PROSA groups 3–4), and those hours leave the flex
account. One hour is never both.**

## What changes, in plain words

Once all of it is built:
- **Employees.** An hour above the month's norm is flex, hour for hour, unless a leader ordered it or accepted afterwards that it was necessary. Only those hours become overtime (+50 %) or count toward an extra-work (merarbejde) claim, and they leave flex. An evening or weekend hour earns its supplement only when its placement was ordered or rostered. Overtime waits in an account with a clock: time off is due within 3 months, and if it is not given, the hours are paid. The employer chooses time off or pay, time off first; the employee's "choose pay or time off" setting goes away. A sick day, a holiday day or any other day of paid absence is credited as the planned working day, so it costs no flex; where an institution sets office hours a single day may move flex by that weekday's difference from 7.4 h, and a week of them nets to zero.
- **Leaders.** A decision step inside month approval, in Teamoversigt (the team overview where months are approved): accept or reject hours an employee marks as ordered or necessary. Orders and rosters can be recorded in advance. A "time off due" list; they record a postponement the employee signs. No one can order or decide their own hours.
- **HR.** Two dated facts per person, the overtime regime and whether the person is on the flex scheme, plus a leaver's flex settlement, an opening balance and cap exceptions, each a registered hand-off.
- **Institutions.** Their own flex rules (caps, office hours, customary days off), in dated local settings that reach pay. There is no central flex cap.
- **Payroll.** Still hours and wage types, never kroner. Every value a month was calculated with, the configuration and the decisions about hours included, is frozen into that month's proof.

It is built in stages (F-1), so for a while some of this is not yet true. The stage map below names what each stage does not yet handle.

## What is wrong today

StatsTid judges overtime week by week and pays it automatically. The state's own texts do neither. Seven things are
wrong, and they share one root cause (next section). Three more, found by the 2026 tender analysis, stand between the
employee's registration and a correct payslip (8–10).

1. **The wrong period.** HK and PROSA measure the *norm* (the hours a person owes) over one calendar month: 7.4 h × the
   month's calendar days, minus days off and weekday public holidays (V1: 022-26 § 4 stk. 2). StatsTid judges each
   Monday–Sunday week (`WeeklyTimelineCore.cs:152-193`), and no public holiday lowers any norm (`DayNorm.cs:28-29`)
   (QUAL-216, QUAL-222). No source makes AC weekly either (V12, C5).
2. **The wrong trigger.** Overtime requires work that was "beordret eller nødvendigt", ordered or necessary (V3).
   StatsTid pays it on every hour above the week's norm, ordered or not (`OvertimeRule.cs:45-48`; QUAL-217).
3. **Counted twice.** The same hours also stay in flex (`WeeklyTimelineCore.cs:304-309`, `FlexBalanceRule.cs:39-42`);
   the sources keep ordered work outside the flex account (V8; QUAL-189).
4. **The wrong rates.** HK/PROSA get 50 % from 37 h a week and 100 % from 40 h (`OvertimeRule.cs:61-91`); the state rule
   is a flat +50 % (V2, V4; QUAL-218). AC gets merarbejde at 1:1 for every week above the norm (`:50-60`); AC
   merarbejde is a claim the employer decides, at +50 % (V9; QUAL-220).
5. **Flex above the cap is paid out** (`FlexBalanceRule.cs:44-48`, `:68-82`); the state default is forfeiture (V7; QUAL-219).
6. **The supplement table is contradicted.** The HK/PROSA seeds pay evening 1.25, night 1.50, Saturday 1.50, Sunday 2.0
   and holidays 2.0 (`init.sql:1468`, `:1474`, `:1480`, `:1486`), and Grundlovsdag is listed as a holiday
   (`DanishPublicHolidays.cs:49`), so every timed hour on 5 June is paid at 2.0 (`SupplementRule.cs:54-66`). The state
   has one +25 % supplement (C2; QUAL-227).
7. **The compensation choice is reversed.** The same seeds let the employee choose time off or pay
   (`employee_compensation_choice TRUE`); the employer chooses (V10, C7; QUAL-221).
8. **Skema's clock times never reach payroll.** Skema keeps a day's clock intervals in `work_time_projection` but writes
   each project's hours with no start or end time (`SkemaEndpoints.cs:2049-2066`, `:2082-2104`). Payroll reads only time
   entries and absences (`ProjectionHoursReader.cs:97-100`), and both the supplement rule and the 11-hour rest check skip
   hours without times (`SupplementRule.cs:30-31`; `RestPeriodRule.cs:222-223`). No Skema hour earns a supplement, and
   the server's rest check sees no Skema day (QUAL-234).
9. **On-call is counted twice; call-ins and travel count as ordinary hours.** Every entry counts as worked whatever its
   activity type (`WeeklyTimelineCore.cs:304-309`), on-call also earns a 0.33 payment (`OnCallDutyRule.cs:36-48`), and
   payroll never runs the call-in and travel rules (its evaluated set, `PeriodCalculationService.cs:2167-2175`) (QUAL-232).
10. **The configuration editors never reach pay.** The Rule Engine reads configs compiled into the code and refuses an
    agreement it has none for (`RuleRegistry.cs:446-447`); payroll registers the service that resolves database configs
    but never calls it (`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:147`); and the Backend reads an
    institution's current local profile, not the one in force on a date (`ConfigResolutionService.cs:185`) (QUAL-233, QUAL-204).

**Worked example.** An HK full-timer in June 2025 works 7.4 h every weekday except 2. pinsedag (Monday 9 June, a
holiday), and on Tuesday 17 June also 17:00–23:00 on a release the manager ordered. Worked: 20 × 7.40 + 6 = 154.00 h.

| | Today | Under this ADR |
|---|---|---|
| The month's norm | 21 weekdays × 7.40 = **155.40 h** (2. pinsedag not subtracted) | 20 × 7.40 = **148.00 h** (Grundlovsdag, Thursday 5 June, is not a holiday) |
| Flex for the month | 154.00 − 155.40 = **−1.40 h** (the ordered 6 h inside it) | (154.00 − 6) − 148.00 = **0.00 h** |
| Overtime | week 16–22 June: 43 h → **3 h at 1.5 + 3 h at 2.0** | **6 h at +50 %** in the overtime account: 9 h off, or paid, at the employer's choice |
| The overtime account | — | the 9 h wait with a deadline of 30 September 2025; untaken, they are paid in the October 2025 pay run (D8) |

The 6 evening hours earn a supplement either way: 1.25 today, but only for hours registered with clock times through the
legacy time-entry API, because Skema hours earn none (QUAL-234); +25 % here, because the manager ordered the work (D7).
The tables part ways at night, at weekends and on 5 June.

## Why it happened — the part worth remembering

S145 asked the owner to rule on the norm period because the agreement sources were "silent". They were not. The
source-register rows behind the weekly rule cite 043-19 § 2, which is "Ansættelsesform" (civil servant or contract), and
§ 4, "Basisløn" (base pay) (V12; QUAL-215); the 37/40 h thresholds were never sourced ("pending (Phase B)").
*A wrong citation was read as an absence of evidence, and an owner ruling filled a gap that did not exist.* The rule
this leaves for the rebuild: every encoded cell names the verified quote it rests on (a V-n or C-n), and "the sources
are silent" is itself a claim to verify before the owner is asked to fill the silence.

*S147 applied the lesson twice.* The central flex caps (HK 100 h, PROSA 120 h, AC 150 h) turned out to have no source,
because there is no central state flex agreement (PAV 19.5.1); StatsTid now forfeits nothing until an institution sets
its own cap (D6), rather than forfeiting at a number nobody agreed. And where the sources really are silent, as for an
AC employee's sick day, the value is ruled by the owner and labelled "a default by analogy" (D2), not presented as a rule.

## The forks and the three tender questions, decided (S147)

Each answer is the owner's, verbatim, given on 2026-10-29 after the question was explained in prose and asked on its own.
The reason is the one the owner was given. The detail is in the decision named in the last column.

| Question | The owner's answer | Why, in one or two sentences | Built into |
|---|---|---|---|
| **F-1** One cut-over, or stages? (D-1) | "(b) Stages (Recommended)" | Nothing is deployed, so the interim underpays no one; each stage is correct for unordered hours, which are most hours, has its own marker, and lands without waiting for the AC claims. Keeping today's weekly overtime until stage 2 was not offered: it counts one hour twice | The stage map |
| **F-2** The marking workflow (D-4, with SEC-056) | "(c) Both (Recommended)" | The sources know two routes, work ordered beforehand (*beordret*, *efter ordre eller godkendt tjenestefordeling*) and work accepted afterwards as necessary (*nødvendigt*), and a record must name its hours to carry the placement fact. A budget of "up to N hours" was not offered: it names no hours and can pay hours nobody ordered | D3, D7 |
| **F-3** The overtime account (D-5) | *Told, no question:* "this one has no question; say so if you want any of it asked"; the owner asked for nothing to be asked | Only a ledger of dated movements can apply § 10's 3-month clock to each earning; a yearly total cannot say which hours reached their deadline, and rails on the export record do not fit, because compensation is decided after the month is exported | D8 |
| **F-4** Office hours, PAV's *kontortid* model (D-7) | "(b) Opt-in, off by default (Recommended)" | Without it StatsTid would knowingly pay +50 % where an office-hours agreement keeps ordered work in flex, and credit 7.4 h where it credits the day's hours; off by default, it changes nothing for anyone else | D3, D2 |
| **F-5** Local flex rules: who is on flex (D-8a) | "(b) Rule set + HR yes/no (Recommended)" | Every sampled local agreement leaves some staff out of flex, and one rule set plus an HR-recorded fact per person represents every current one; per-group caps appear once, in a 2014 agreement of unknown status | D2, D6 |
| **F-5** Local flex rules: an AC employee's sick day (D-8b, Q-1) | "(a) HK/PROSA rule, labelled (Recommended)" | No AC text says how a paid sick day counts in flex; the HK/PROSA rule is the verifier's recommended default, labelled as an analogy that a local agreement can change. "Credit nothing" was not offered: it would cost a sick employee flex | D2 |
| **F-6** AC's period (D-9) | "(a) Monthly flex, per claim (Recommended)" | The AC agreement deliberately sets no period, and a fixed quarter rests on a union reading (MEDIUM confidence), not on the text; the owner asked for, and got, the reason the recommendation departs from the S146 researcher's quarter before ruling | D1, D5 |
| **F-7** The part-timer default (D-10) | "(ii) Award: +50 % (Recommended)", recorded as **F-7 = (c), default (ii)**, provisional pending a qualified expert | A binding award of 4 February 2026 (FV2025-795) found the printed 1:1 band in breach of the EU part-time directive; its direction is also the simpler encoding, and a dated, named variant lets the state's text switch in on its date | D4 |
| **F-8** The HK pay ceiling; PROSA pay groups 3–4 (D-11) | "(b) HR records regime (Recommended)" | StatsTid holds no pay and should not (the payroll boundary); a dated, audited HR fact keeps kroner out and is frozen into the proof like the part-time fraction. Importing base pay was offered only as the owner's own exception, and not chosen | D4 |
| **F-9** Customary days off (D-12) | "(c) Both kinds (Recommended)" | The sources show both kinds in practice (the Defence's paid 5 June; SKAT's closure days), and a paid day as a credit keeps the norm faithful to § 4. Closure days only, and "not modelled", were not offered | D9 |
| **QUAL-234** How Skema's clock times reach payroll and the rest check (D-2) | "(a) Read work intervals (Recommended)" | It uses the times the employee actually entered; payroll already reads two Backend tables under a designed read-only contract, and the proof grows to cover the third | D10 |
| **QUAL-232** Which time counts as worked time (D-3) | "(c) Keep, build later (Recommended)" | The refusal stops the double count at once; the requirement stays, on the sourced rule (fractions of hours, not a pay rate) | D11 |
| **QUAL-233**, with QUAL-204: does payroll read what the editors write? (D-6) | "(a) Editors reach pay (Recommended)" | The product specification and an approved ADR already require it; every local rule above needs the path; and freezing the values a calculation used into its proof is better auditability than fingerprinting code | D12 |

## Decision

Each item says what the model does, the verdict it rests on and the register row it closes.

### D1 — The norm is a calendar month; weekday søgnehelligdage lower it
- 7.4 h × the month's working days, pro rata for part-timers (§ 5; PAV: 18 × 20/37 × 7.4 = 72 h), minus søgnehelligdage
  (public holidays that fall on a weekday) on a weekday the person would work (V1: *"fratrukket fridage samt eventuelle
  søgnehelligdage"*).
- Built from today's per-day amount, `DayNorm.ForWeekday` (`DayNorm.cs:44-45`), summed over the month's weekdays that
  are not norm-lowering days (D9). The block is a day, so split months keep working.
- The per-day precision rule of S145 Q2 = A stands: the month norm is the sum of `round(weekly × fraction ÷ 5, 2)` over
  the norm-bearing weekdays, so the Skema screen and the payslip agree day by day; the domain expert confirms the rounding
  (provisional item 6).
- One month-norm function serves payroll and the screens, which today take their inputs from different places: one
  function with one input resolution — the month's OK version and agreement, the fraction from one dated read (QUAL-226).
- **AC (F-6 = (a)).** Never weekly (V12). AC flex settles each calendar month on the same month-norm function, 7.4 h a
  working day pro rata, as the sampled local flex agreements do; on søgnehelligdage the AC norm falls as it does for HK
  and PROSA. No AC text says either way, so this is a labelled default for the domain expert (D-9.4; provisional item 4).
  The annual-norm variants (`AC_RESEARCH`, `AC_TEACHING`, norm model `ANNUAL_ACTIVITY`; `init.sql:1492`, `:1498`) keep
  their norm model; how their flex settles is a design gap the build input carries.
- **The period setting changes.** `NormPeriodWeeks ∈ {1, 2, 4, 8, 12}` (`AgreementRuleConfig.cs:13`;
  `SYSTEM_TARGET.md:247`) cannot express a month. The month becomes the period for all three agreements (HK and PROSA by
  § 4 stk. 2, AC by F-6), and the AC `DEPARTMENT_HEAD` and `RESEARCHER` 4-week overrides (`PositionOverrideConfigs.cs:26-46`)
  retire or translate. A local agreement may set another settlement period (*"medmindre andet er aftalt"*) as a local
  parameter (D6); S148 builds the one value `month`.
- **Paid absence never changes the norm.** Office hours (D3) and paid customary days (D9) change what a day of paid
  absence is credited, not the norm (D-7.P4, D-12.P2).
- **Closes** QUAL-216; QUAL-222 with D9; QUAL-226.

### D2 — Surplus hours go to flex; a day of paid absence is credited as the day planned
- An hour above the norm that no leader ordered or accepted is a flex hour, carried hour for hour (V6: PAV 19.5.6 and
  the AC remark to § 13). Saved flex not taken by period end is not overtime (V11).
- Flex settles once over the month's own days, as today (`WeeklyTimelineCore.cs:295-335`); its opening and closing ride
  the S146 ledger (D6).
- **The paid-absence credit basis (F-5; RR-12 as widened by batch-2 ruling 4).** Every day of paid absence (a sick day,
  a child's sick day, a holiday day, særlige feriedage, time off in lieu, paid leave, a care day, a paid customary day)
  is credited by one local parameter, the *paid-absence credit basis*. Its default is § 7 nr. 2: the hours the person
  should have worked that day, else 7.4 h × fraction (R1; `sick-day-credit-and-agreement-texts-research.md:104-106`).
  Where an institution sets office hours, it is the weekday's office hours (D3). A local agreement may depart from it
  (022-26 § 3; PAV 19.5.13's *kan*). Narrowing the credit to sick days has no source, and would make a Friday holiday and
  a Friday sick day worth different flex.
- **AC (F-5 = D-8b (a)).** For AC-agreement staff no central text says how a paid sick day counts (PAV 19.3.4). StatsTid
  applies the HK/PROSA rule as a default by analogy, labelled so, which the institution's credit basis can change
  (provisional item 3). S147's TASK-14708 builds the label; it stays.
- **One served "planned hours" figure (D-7.P3, P4).** The credit, the Skema absence cap and ADR-032's vacation divisor
  read one figure, the day's planned hours; the month norm keeps reading the per-day norm share. Where no office hours
  are set the two are equal, so nothing changes for anyone who does not set them. If the build lands this in steps, the
  sprint log names which absence classes read the basis.
- **A partly worked day of paid absence is bounded (R-7 as widened by batch-3 ruling 3).** *"A day's paid-absence hours,
  of any credit-class type, may not exceed the credit basis minus the hours worked that day; judged at the Skema save in
  both directions (QUAL-237)"*: a paid-absence save on a day that already holds worked hours, and a later work save on a
  day that already holds paid-absence hours, each judged against the day's stored state. The alternative to the check is
  for the engine to compute the credit as min(stored paid-absence hours, max(0, planned − worked)). This is the
  max(worked, planned) reading, labelled MEDIUM, and a local agreement may differ (provisional item 7). For a type that
  also draws on an entitlement balance (a quota), a backstop that clips the credit must also clip the consumption, or
  refuse the month.
- **Who is on flex (F-5 = D-8a (b)).** One rule set per institution and agreement. HR records a dated, audited fact per
  person, "on the flex scheme: yes / no", seeded from position and resolved as of each day (ADR-040). Someone off the
  scheme keeps no flex balance, and no settlement or forfeiture runs for them; their hours are still registered, their
  ordered hours still follow their regime (D4), and § 13 and § 14 still apply to their ordered placements (D7). What a
  deficit means for them is an expert question (provisional item 16). Per-group caps, such as AU Foulum's 200 h, are not
  held; that is recorded as a limitation, and per-group rule sets can grow from this fact later.
- **Closes** QUAL-189 with D3; QUAL-231's residual and QUAL-237.

### D3 — Overtime and merarbejde only for decided hours, outside flex; how hours are decided
- An hour becomes overtime (HK, PROSA groups 1–2) or counts toward merarbejde (AC; PROSA groups 3–4 when they meet the
  conditions, V5) only when a recorded decision covers it: ordered beforehand, or accepted afterwards as necessary (V3).
- Such an hour leaves the flex account; it counts in neither flex's worked hours nor its surplus (V8: PAV 19.5.11).
- **The record names its hours (F-2 = (c)).**
  - *An order beforehand.* A leader records an order, or a roster span, naming the date and the clock span; a roster's
    recurrence lives on the order record. The ordered hours are the overlap of the span with the worked intervals (D10).
    Work beyond the span is flex unless it is marked afterwards; work short of the span is ordered only as worked.
  - *An acceptance afterwards.* Work nobody ordered in advance is marked by the employee in Skema as ordered or
    necessary, and the leader accepts or rejects the mark in month approval, in Teamoversigt.
  - *What every record holds.* A date and a clock span; its kind (ordered beforehand, or accepted afterwards); a
    **placement** fact, "placement ordered or rostered", kept separate from the **surplus** fact, "ordered or necessary",
    because § 13's +25 % and § 14's +50 % read the first and this decision reads the second (D-4.P3; D7); an activity
    type, `NORMAL` until D11's typed time arrives (D-4.P5); a creator, a decider and a status.
- **The cut-off and the decider (D-4.P1).** A mark is decided before its month is approved, and the month cannot be
  approved while one is undecided. The decider is whoever holds that month's approval authority at the time: the
  designated approver, a stand-in under Q-B = (c), or HR for a leaver's final month under OQ-2.
- **Four-eyes, on four conditions (D-4.P4, batch-2 ruling 1; SEC-056).**
  - (a) A leader's order is a decision at creation, and its creator is its decider.
  - (b) No decision is ever on the decider's own hours. S147's T9 rule (TASK-14705) is carried into every record that
    decides: an order, a mark decision, a compensation (D8), a cap exception (D6); each refuses with 403.
  - (c) Creator-not-approver applies to every record that awaits a decision: a mark raised by the employee, or by a
    leader on the employee's behalf, is decided by someone else.
  - (d) The authority to order or decide is the authority to approve that employee's month, not bare organisation scope,
    which is all today's create route checks (`OvertimeEndpoints.cs:162-164`).
  - OQ-1's strict four-eyes on the month's two signatures is untouched. *Given up:* a second leader approving a leader's
    order. No source asks for one (the sources know one authority for ordering work, the manager, V3); an institution
    that wants one could get a local parameter later (recorded, not promised).
- **A late decision is a movement, never a reopening (D-4.P2).** A decision made after the month is approved or exported
  lands as a dated movement in the month the decision is made: the hours leave flex there, and the overtime account is
  credited (D8). No exported month is reopened (ADR-013, ADR-034). The time-off clock still runs from the period the
  hours were worked in (D8). This also answers the sub-question this ADR left in D5, for AC claims.
- **The decided records reach the pure engine as a frozen input** (ADR-002). Payroll reads them under ADR-016 D10's
  read-only contract on Backend tables, which they join in stage 2; they are frozen into the manifest by value, with
  their state at calculation time; the digest covers them.
- **ADR-024 D7 is the starting point, now on the payroll path.** Its distinction between an approval given beforehand
  and one given afterwards is kept (`ADR-024:222`). Its acknowledgement endpoint (`:218`), never built, is built naming
  clock spans rather than time-entry ids (the dormant event names ids, `OvertimeNecessityAcknowledged.cs:23`). Its
  "Approval page" (`:220`) is Teamoversigt, where month approvals live since S87 (`frontend/src/App.tsx:98-100`).
- **What retires (D-4.P6).** Today's record is an hours budget for a period that names no hours
  (`init.sql:2027-2038`) and has no creator column (`docs/generated/db-schema.md:927-944`); it changes shape, with a
  reseed. The `OvertimeRequiresPreApproval` switch (`AgreementRuleConfig.cs:80`; the local column `init.sql:868`) and
  the advisory governance check that reads it (`OvertimeGovernanceRule.cs:106-114`; its route `OvertimeEndpoints.cs:60`,
  which takes a caller-supplied flag, `:65`, and which no screen calls) retire: every overtime hour now needs a decision,
  so an optional switch has no meaning, and ADR-024's "Bug #4 seed flip" (`ADR-024:228`, `:254`) resolves by retirement.
  `MaxOvertimeHoursPerPeriod` survives only as an optional warning over decided hours (D6).
- **The office-hours exception (F-4 = (b)).** An institution may set office hours, a clock window per weekday, in its
  dated local settings; they are off unless set. Where they are set:
  - a decided hour leaves flex only if all three of V8's conditions hold: it was ordered or necessary; it fell outside
    office hours; and it came after the day's first 7 h 24 min, at the person's per-day amount (PAV's daily form, D-7.P2);
  - ordered work at the weekend or on a søgnehelligdag stays § 14's whatever the office hours (D7; the expert confirms,
    provisional item 12);
  - the editor warns, and does not refuse, when the five windows do not add up to the full-time week's norm (37 h; the
    profile's own `weekly_norm_hours` where one is set, provisional item 18); how a part-timer's windows scale is
    provisional item 14 (D-7.P1 as corrected);
  - the Aarhus University template's monthly variant (160.33 h) is recorded, not built; core hours (*fikstid*) govern
    presence, not pay, and are not modelled for pay (D-7.P6); part-timers under office hours are an expert question
    (provisional item 14).
- **Closes** QUAL-217; SEC-056's creator-not-approver half, in design.

### D4 — HK and PROSA overtime is a flat +50 %; the HR-recorded regime decides who earns it
- +50 %, as time off or pay (V2: § 8 stk. 1). No 37/40 h thresholds and no 100 % tier (V4): `OvertimeThreshold50/100`
  (`AgreementRuleConfig.cs:43-44`) and `OVERTIME_100` retire. The narrow daily ladders (HK Bilag 9, 023-19, 069-06) are
  not modelled unless needed.
- **Part-timers' band (F-7 = (c), default (ii); provisional pending a qualified expert).** § 8 stk. 2's 1:1 band (from
  the part-timer's own norm up to full time) is not applied, by owner ruling of 2026-10-29, provisional until the state
  publishes the text implementing the award of 4 February 2026, FV2025-795 (none as of 28 October 2026, R4). An ordered or
  accepted hour above a part-timer's own norm earns +50 %, like a full-timer's: a part-timer is a full-timer with a smaller
  norm (provisional item 1). The +50 % is § 8 stk. 1's rate applied to the band, derived rather than quoted, because the
  award names no rate. The rule is held as a dated, named variant (`PartTimeBandRule`), with one encoding now, and its
  name and from-date are frozen into every manifest; the state's text becomes a new dated variant in the same slot.
  - The award covers part-timers who have comparable full-timers. StatsTid holds that condition as met for every
    part-timer and checks nothing (D-10.4; provisional item 2); the legal rationale for doing so is owed research R-D.
  - AC-agreement part-timers were not examined; their check sits with D5, in stage 3. Back pay is outside StatsTid.
- **The overtime regime (F-8 = (b)).** Whether an ordered hour earns +50 % overtime, counts toward a merarbejde claim, or
  earns nothing depends on pay, which StatsTid does not hold. HR records each person's *overtime regime* as a dated,
  audited attribute on the employee, resolved as of each day under ADR-040 and frozen into the manifest beside fraction
  and position (`ADR-016:171`). One value set covers the three agreements:
  - HK: overtime, or exempt (base pay ≥ 441,037 kr. a year at the 31 March 2012 level, a special post: § 4 stk. 2 and
    its remark, § 3 stk. 4; C3, re-cited against Medst. 087-25 in S147 and found word for word the same,
    `agreement-cells-reverification-research.md:529-539`);
  - PROSA: overtime (pay groups 1–2), or merarbejde (groups 3–4, V5);
  - AC: ADR-024 D2's three states, all provisional (provisional item 5), seeded per category (Fuldmægtig CONTRACTUAL,
    Specialkonsulent DISCRETIONARY, Chefkonsulent and Kontorchef NONE; `init.sql:2525-2532`); for AC the attribute
    decides whether a merarbejde claim can be raised at all (D5):
    - **CONTRACTUAL** — may raise a § 14 claim (D5).
    - **DISCRETIONARY** — no § 14 claim in StatsTid; hours recorded; any compensation a one-off kroner payment
      (*engangsvederlag*) decided and paid outside StatsTid (the payroll boundary); the claim gate reads it as NONE;
      whether StatsTid shows HR the surplus as information is stage 3's design, never a pay line.
    - **NONE** — no claim, no such report.
  - PROSA groups 3–4 are the same family (V5; provisional item 17). One closed value set with five meanings; the enum
    names are S148's.
  - ADR-024's per-category overrides (D1/D2; the table `init.sql:2448`, seeded at `:2524-2533`) become the seed HR starts
    from, not a second source pay reads (D-11.4).
  - StatsTid stores no salary: the employee profile holds the part-time fraction, position and employment category
    (`docs/generated/db-schema.md:258-271`), and pay belongs to the payroll system. The HR step has no system check, so
    it gets an HRP row.
- **What exempt staff keep.** The regime decides only the overtime/merarbejde part: exempt HK staff still earn § 13's
  +25 % and § 14's compensation for ordered or rostered placements
  (`sick-day-credit-and-agreement-texts-research.md:841`, `:870`; D7). PROSA groups 3–4 who do not literally meet § 8
  stk. 3 fall back to overtime on one reading (C3): an expert question that, under this decision, is just a value HR sets
  (provisional item 17).
- **Closes** QUAL-218; with F-7 and F-8, QUAL-223 items (2) and (3).

### D5 — AC merarbejde is a claim decided by the employer (stage 3)
- The employee claims, the leader attests, the employer decides. Conditions (V9): ordered or a precondition for the
  job, and of larger extent over normally at least 4 weeks (§ 14 stk. 2); reported when it ends and at least quarterly
  (stk. 3). Compensation +50 %, as time off where possible (stk. 5), postponable up to a year by agreement (OK26 item
  21). For fuldmægtige (case officers) on the standby allowance, only extra work substantially beyond 20 h a quarter
  counts: a condition on the claim, whose interaction with flex is a local parameter (D6).
- **No imposed period (F-6 = (a)).** Each claim states its own period (normally ≥ 4 weeks) and is judged over it;
  whether the work was of "larger extent" stays the employer's call; the quarterly reporting floor is enforced as a
  reminder. The S146 research proposed a calendar quarter as the default assessment period (C5); the agreement sets
  none, and a quarter is a union derivation (MEDIUM), so it was not chosen.
- **A decided claim.** An accepted claim's hours leave flex as a dated movement in the decision month (D3; exported
  months are not reopened) and earn into the same overtime ledger (D8) at +50 %. Whether AC time off has a deadline is
  owed research R-B.
- **Who may claim.** The overtime regime (D4) decides whether an AC employee can raise a claim at all (CONTRACTUAL
  only; D-11.4).
- Stage 3 designs who "the employer" is when it decides, and whether PROSA groups 3–4's merarbejde is a claim of this
  kind or a decided hour under D3.
- Until stage 3, AC extra work is flex only (the stage map).
- **Closes** QUAL-220.

### D6 — Flex rules are each institution's own; flex above a cap is forfeited, and the forfeit is recorded
- **There is no central flex cap (batch-2 ruling 2, told).** There is no central state flex agreement (PAV 19.5.1). The
  caps in the code, HK 100 h, PROSA 120 h and AC 150 h (`CentralAgreementConfigs.cs:46`, `:77`, `:23`; OK26 `:131`,
  `:162`, `:108`), were never sourced, and SYSTEM_TARGET §G's "within central limit" (`SYSTEM_TARGET.md:168`) and "local
  maxFlexBalance must be <= central MaxFlexBalance" (`:174`) rest on a false premise the code never enforced: the only
  checker, `ConfigResolutionService.ValidateLocalOverride` (`ConfigResolutionService.cs:324-327`), is `[Obsolete]` and
  has no production caller. So: caps live only in the local profile; the central fields become nullable and are seeded
  null (the central fields go null in stage 1, with the payout's retirement); a month with no cap records "no flex cap configured" and forfeits nothing; the profile editor shows the caps as
  unset; and the demo seed sets caps for the demo institutions (37 h, as SDU and DTU). *Given up:* an out-of-the-box
  number. SYSTEM_TARGET §G's principle that local rules never override a centrally negotiated rule stands, for +50 %,
  +25 % and § 14.
- **Forfeited at the cap test, never silently (R-3; V7: PAV 19.5.8, *"bortfalder som udgangspunkt"*).** Hours above a
  set cap are forfeited, recorded in the month's calculation and visible to the employee and HR. The automatic
  `FLEX_PAYOUT` (`FlexBalanceRule.cs:44-48`, `:68-82`) retires; a `FLEX_PAYOUT` line survives only as the configured
  "1:1 cash by agreement" action.
- **Where the rules live (F-5's storage, R-1).** Typed, dated, audited columns on `local_agreement_profiles`
  (`init.sql:856-872`; audit `:885-895`), resolved for each day and frozen into the manifest (D12). Not
  `local_configurations`, which ADR-017 retired (`init.sql:851-852`); not a third table. D9's customary days are a dated
  table beside the profile, because they are a list of days, not a value. The profile's `weekly_norm_hours` override
  (`init.sql:864`) and its Monday alignment rule (ADR-017 D9a) lose their premise under D1's monthly norm, since no
  sampled agreement changes the norm's hours; retiring them is an expert question (provisional item 18).
- **The parameters (R-2, with its five amendments).**

  | Parameter | When no local value is set | First stage |
  |---|---|---|
  | Settlement period | the calendar month (§ 4 stk. 2 remark, V1; F-6 for AC). S148 builds the one value `month`; other values are recorded for the expert | config stage, then 1 |
  | Cap-test period | the settlement period. The dormant `flex_carryover_max` (`init.sql:866`; no reader in the Rule Engine or Payroll) retires, or becomes this parameter's form | 1 |
  | Plus cap | none: "no flex cap configured" (ruling 2) | 1 |
  | Minus cap | none. A breach is recorded and shown to the leader, never deducted automatically (provisional item 11) | 1 |
  | Daily credit cap | none (the AU template has 13 h) | 1 |
  | Above-cap action | forfeit, recorded (V7). Alternatives: a manager's exception; transfer to the overtime account (from stage 2); 1:1 cash by agreement | 1 |
  | Leaving action | the leaver bullet below | 1 |
  | Office hours | off (D3) | the credit 1; the filter 2 |
  | Core hours (*fikstid*) | not modelled for pay | — |
  | Who is on flex | an HR fact per person (D2) | config stage |
  | Paid-absence credit basis | § 7 nr. 2 (D2) | config stage, then 1 |
  | `MaxOvertimeHoursPerPeriod` | none: an optional warning over decided hours (D3) | 2 |
  | Fuldmægtige's standby interaction | none (V9: 20 h a quarter) | 3 |
  | Suspension in peak periods | none; the mechanics are for the expert | last |
  | AC travel cap, *"medmindre andet aftales lokalt"* | 13 h per døgn (D11) | with D11 |

- **A manager's exception (R-2's amendment (v)).** A dated record for one person, decided by someone with approval
  authority over that person, never on the decider's own balance (D3's condition (b)), with an HRP row.
- **A leaver's balance is settled in the leaving month (R-4; this ADR's F-5 sub-question, option (a)).** The leaving
  action settles the balance as a dated, recorded movement in the leaving month, never carried to a rehire (a rehire
  opens at 0, `REHIRE`, `FlexLedgerOpening.cs:44`). Where no local value is set, the labelled defaults are: a positive
  balance not taken during notice is forfeited, recorded and visible; a negative balance is recorded and shown to HR, not
  deducted, a pay deduction being a local option (provisional item 8). It never happens silently, and it gets an HRP row
  beside HRP-011's leaver lifecycle.
- **An HR-entered opening balance is designed (R-5, batch-2 ruling 3; QUAL-225).** The ledger start (`LEDGER_START`,
  `FlexLedgerOpening.cs:35-36`) gains an opening balance HR enters with a reason, an event, an audit row and an HRP row.
  It is the receiving end of a balance transferred from another state employer, and it ends S147 T6's "flex not tracked"
  months. Its sprint is S148's refinement's.
- The S146 ledger's rails (`opening_balance`, `closing_balance`, `balance_provenance` on `payroll_export_records`,
  `init.sql:4633-4640`) carry the new closing unchanged in shape; S147's TASK-14700 (merged `f78ddba8`) walks the chain
  from the ledger start and refuses to open a month from a stale chain (QUAL-229).
- **Closes** QUAL-219; designs QUAL-225.

### D7 — One +25 % supplement and § 14's compensation, only for ordered or rostered placements
- HK/PROSA: one +25 % supplement for work 17:00–06:00, at weekends, on søgnehelligdage, on Grundlovsdag from 12:00 and
  on 24 December from 14:00 (C2: § 13), paid regardless of overtime. It is due only for work done *"efter ordre eller
  godkendt tjenestefordeling"*, by order or under an approved roster (`sick-day-credit-and-agreement-texts-research.md:824`),
  so it reads D3's placement fact, not its surplus fact. An evening or weekend hour the employee placed there under flex
  earns nothing extra. The placement fact is set by an order or roster beforehand, or by the leader's acceptance of a
  mark afterwards (D3's record holds it; `.claude/plans/s147/decide/D-04-marking-workflow.md:86`), so a necessary
  evening whose placement the leader confirms earns the supplement; one the employee chose under flex does not.
- Ordered or rostered weekend and søgnehelligdag work for day workers (`:854`): +50 % as time off or pay (employer's
  choice), kept out of the month's hours count, the +25 % on top, no overtime on top (C2: § 14 stk. 1–3). It earns into
  the overtime account with its own clock (D8).
- Both apply to staff exempt from overtime (`:841`, `:870`) and to part-timers' extra hours.
- **The time of day comes from Skema's intervals (D10),** which payroll reads from stage 2. §§ 13–14 are word for word
  the same in OK24 and OK26 (R5, `sick-day-credit-and-agreement-texts-research.md:360`), so there is one set of windows
  and one § 14 rule, with no split by OK version. For on-call, call-ins and travel, the supplement basis is set per type (D11).
- No 1.50 night rate and no 2.0 Sunday or holiday rate for general HK/PROSA (only HK Bilag 9's narrow group has 100 %).
  AC stays without supplements (general profile; AC Bilag 6's area payments out of scope). A local custom never changes
  the windows (D-12.P7).
- **No supplement until stage 2 (D-1.4).** `SUPPLEMENT_CALC` stays among the rules payroll evaluates
  (`PeriodCalculationService.cs:3217`; the set `:2167-2175`) and returns no lines in stage 1. Today's table is
  contradicted by the sources, and +25 % on every timed hour would pay placements nobody ordered; only stage 2 records
  the placement fact.
- **Closes** QUAL-227 and QUAL-223 item (1).

### D8 — The overtime account: a ledger of decided hours; the employer chooses, time off first
- **A movement ledger (F-3 (b), told; D-5.P1).** The movements: *earned* (the norm period worked, the hours, the rate,
  the kind: § 8 overtime, § 14 weekend compensation, AC merarbejde from stage 3); *taken* as time off; *paid* (by a
  decision, or by the deadline's default); *postponed*; *converted* to saved days. Each is dated, audited and frozen into
  the manifest of the month it lands in, and none edits an exported month (ADR-034; D3).
  - One writer per table. The balance and each earning's deadline are derivations the pure engine recomputes from frozen
    inputs (ADR-002), never a stored total anyone adjusts, which is why a replay can re-prove them.
  - Where the movements are stored, one table or a Backend fact table plus Payroll's own movement table under ADR-016
    D10's read-only contract (D10 is the precedent), is S148's refinement's call.
  - Today's yearly row (`overtime_balances`, `init.sql:1992-2008`, one per employee and year) cannot say which hours have
    reached their deadline and has never been fed (`AdjustAccumulatedAsync`, `OvertimeBalanceRepository.cs:50`, has no
    production caller); it retires. The account stays separate from flex (`SYSTEM_TARGET.md:408`).
- **The employer chooses, time off first (D-5.P3; V10, C7: PAV 19.2.7.1).** The default is time off at +50 %: 6 decided
  hours become 9 hours off. The decider may choose pay instead when time off is judged impossible or inappropriate (§ 10
  remark), recorded with a reason. The employee's choice (`EmployeeCompensationChoice`, true for HK and PROSA in the
  seeds `init.sql:1468`, `:1474`, `:1480`, `:1486` and in `CentralAgreementConfigs.cs:67`, `:98`, `:152`, `:183`) and its
  routes (`OvertimeEndpoints.cs:606-751`) retire; PROSA's printed "1:1" becomes +50 %. Payroll gets hours and wage types,
  never kroner; the time-off and pay wage types survive (`init.sql:2047-2050`, `:2066-2069`), and the 100 % rows retire.
- **The clocks are facts, not questions (D-5.P4).**
  - § 8 overtime: time off within 3 months of the end of the norm period worked (§ 10 stk. 1); otherwise paid at the
    following pay date, unless postponed by agreement or converted to saved days. Under D1's monthly norm the deadline
    is a month end, so the default pay lands in the pay run of the month after the deadline month: a 30 September
    deadline pays in the October run (a labelled default the expert confirms, provisional item 10).
  - § 14 weekend compensation: time off by the end of the following norm period, else cash at the following pay date,
    with the same timing; someone sick when planned time off begins need not start it (§ 14 stk. 4).
  - A late decision (D3) dates its earned movement in the decision month, but its clock runs from the worked period; if
    that clock has already run out, the movement defaults to pay at once, or to an agreed deferral.
  - The part-timer band earns from the part-timer's own norm (D4). AC's deadline is owed research R-B.
- **The employee's two elections are the employee's own acts (D-5.P2, P6).**
  - A postponement is a record both parties sign, the decider and the employee (§ 10 stk. 1, *"efter aftale med den
    ansatte"*): dated, audited, moving one earning's deadline by up to a year.
  - The saved-days (*opsparingsdage*) election is recorded as a movement out of the account before the calculation that
    would pay the default runs, so no hour the employee chose to save is ever paid. Whether it may be made earlier than
    the deadline is owed research R-A.
- **Compensation only behind decided hours (D-5.P2, on batch-2 ruling 1's conditions; SEC-056).** Every taken, paid,
  postponed or converted movement draws on decided earned movements, never on a bare balance. A decision about another
  person's account (granting time off, paying early, recording a postponement) is made by whoever holds that employee's
  month-approval authority, never on the decider's own hours (403); a request awaiting a decision, such as the
  employee's time-off booking, is decided by someone else. Today's compensate route (`OvertimeEndpoints.cs:512-603`)
  adjusts the unfed yearly row with no decided movement behind it, and discards its guarded update's result
  (`:567-576`; QUAL-239); until it retires with the yearly row it reads that result and refuses (409) before
  enqueueing (stage 0), and S147's T9 guard on it is defence in depth.
- **Taking time off in lieu is a Skema absence (D-5.P5).** A new *afspadsering* type in the credit class (S147
  TASK-14708's classification, in which every absence code is exactly one of credit, reduction or neutral), credited by
  the paid-absence credit basis (D2), so a whole day off is credited as a planned day (flex unchanged where no office
  hours are set). It draws on the earning with the earliest
  deadline first, cannot exceed the time off owed, is bounded by D2's rule for partly worked days (its draw on the
  account clipped with its credit, or the month refused), and is approved with the month.
- **The deadline is a visible hand-off: HRP-021 becomes decision-ready (D-5.P7).** *Trigger:* an earning's deadline
  approaching or passed. *Accountable:* the employee's month approver; the employer arranges time off (V10, C7).
  *Surfacing:* a leaders' "time off due" list and the employee's account screen, each earning with its deadline; today
  the balance is served (`BalanceEndpoints.cs:461-467`) and shown nowhere. *Resolution:* time off granted, pay by default,
  a postponement, or an election, each audited. The default pay is computed in the month's calculation and shown on the
  payslip and in the account, never silently.
- **Saved days are designed (D-5.P10, a recorded design proposal).** StatsTid holds a saved-days balance: an election
  screen, a Skema "saved day" type in the credit class, and the cap as a local parameter with the dated central floor
  (74 h, then 111 h from 1 April 2026 under OK26's § 11 stk. 2). Research cell R-A on § 11 comes first; its sprint is S148's
  refinement's. Until it lands, the election is recorded as a movement, and a saved day taken is registered as paid
  leave (`LEAVE_WITH_PAY`), named in the sprint log as the interim limit.
- **A leaver's owed time off is paid, never forfeited (D-5.P9).** This is derived from §§ 8 and 10: pay is the fallback
  whenever time off is not given, and time off cannot be given after the employment ends. That it is paid in the
  leaving month, and what happens to saved days at leaving, are a labelled default and owed research R-C (provisional
  item 9). It sits beside D6's flex settlement, in HRP-011's leaver lifecycle.
- The dated *opsparing af frihed* election (1 May 2025 – 31 March 2026, 024-26) is not modelled; its window has closed
  (D-5.P8).
- **Closes** QUAL-221; QUAL-239 with the route; SEC-056's compensation half, in design.

### D9 — One holiday calendar, split by what each day does; customary days are each institution's
- Three kinds of day: (1) norm-lowering søgnehelligdage, the closed list of ten, skærtorsdag … nytårsdag (022-26,
  remark to § 4 stk. 2); (2) timed supplement windows, Grundlovsdag from 12:00 and 24 December from 14:00; (3) local
  customary days off (5 June, 24 December, 31 December, 1 May), per institution, dated and audited (below). Grundlovsdag,
  24 December, 31 December and 1 May are not holidays (C1): without a recorded custom, 5 June is an ordinary working day.
- Keyed by date, not OK version: Store Bededag ended by law on 1 Jan 2024 (Act 214 § 5); today's OK-version key
  (`DanishPublicHolidays.cs:54-58`) is right by coincidence (C1).
- One authority. Today three lists have separate readers: the C# list (`DanishPublicHolidays.cs:36-62`) read by the
  supplement rule (`SupplementRule.cs:56`); the `danish_public_holidays` table (`init.sql:165-170`, rows `:358-400`)
  read only by the send-month check (`ApprovalEndpoints.cs:2119`); and the demo generator's own list
  (`tools/StatsTid.DemoSeed/Generation/DanishHolidays.cs:68`). All three carry Grundlovsdag.
- **Order (C1's trap; fixed, not a question):** the split lands *before* the norm subtracts holidays. Otherwise every
  June with a weekday 5 June loses 7.4 h: June 2026 (5 June a Friday) would fall from 162.80 to 155.40.
- A vacation day booked on a søgnehelligdag consumes nothing, ending ADR-032 D7's accepted limitation (`ADR-032:47`); so
  does one on the credited part of a paid customary day (below).
- **Customary days (F-9 = (c)).** An institution's admin records each custom in a dated, audited table beside the
  local profile (D-12.P1): the day; its kind, a paid day off or a closure day; the part: the whole day, from a clock
  time where office hours are set, or a number of full-time hours where they are not; for a paid day, its mode; and a validity period, so that a custom's withdrawal (possible with reasonable notice,
  FV2016.0107) and its reinstatement are dated facts, not deletions. Keyed by date, not OK version, and frozen into the
  manifest (D12). A custom may be narrowed to one agreement; a staff-group scope has no machinery (D2).
  - *A paid day off is a credit, not a norm cut (D-12.P2).* It is a day of paid absence read by D2's credit basis; D1's
    norm and the closed list of ten are untouched. A part day is the part of the weekday's office-hours window after the
    stated time where office hours are set, else a full-time figure scaled by the fraction (D-12.P3; the scaling is an
    expert question, provisional item 15).
  - *Two modes (D-12.P4).* Given to everyone (credited automatically, shown as the day's absence row), or on request (a
    bookable row, credited when booked and decided in month approval, as PAV 26.2.8.2 gives 1 May *"i den udstrækning
    tjenesten tillader det"*). Under D2's bound, "given to everyone" can never pay someone twice for a day they worked.
  - *One credit per day (D-12.P5).* The credited part nets off hours worked (D2); a vacation or særlige-feriedage booking
    consumes nothing for it; a sick day earns no second credit; ordered hours on the day follow D3. The expert confirms
    the worked-day case.
  - *A closure day earns nothing (D-12.P6).* Skema shows it, and the employee covers it with leave, time off in lieu or
    flex; nothing is booked automatically, and the send-month check (`ApprovalEndpoints.cs:2116-2127`) keeps it a working day.
  - Recording a custom is configuration, not an HR hand-off, so it has no HRP row (D-12.P8). The demo seed sets example
    customs (D-12.P9).
- **Closes** QUAL-222 (with D1) and QUAL-227's calendar half.

### D10 — Clock times: one source of time of day per hour (QUAL-234)
- **Payroll and the rest check read Skema's work intervals (D-2 (a)).** The intervals the employee enters
  (`work_time_projection`, `init.sql:1973-1984`) supply the time of day; flex and pay keep reading the hours from the
  project entries. A month cannot be sent unless, every day, the work-time hours equal the project hours (today interval
  hours plus manual hours, `ApprovalEndpoints.cs:2230`; intervals alone once manual hours retire) to the hundredth of an
  hour (`ApprovalEndpoints.cs:2225-2272`; `AllocationBalance.cs:7-11`), so for a sent month the intervals describe
  exactly the hours paid.
- **One source of time of day per hour (D-2.2).** The legacy `POST /api/time-entries` route's own start and end times
  and its "voluntary unsocial hours" flag (`TimeEndpoints.cs:24`, `:150-157`; `TimeEntryRegistered.cs:16`) are either
  routed through the interval table or retired with the route, so no hour has two competing clock times.
- **Manual hours are retired (D-2.4).** The leftover day total with no times (`init.sql:1977`), which no screen still
  creates, is retired with a reseed, so every worked hour has clock times.
- **Two halves, in two stages.**
  - *The rest check,* a Backend compliance endpoint that reads the time entries and calls the engine's rest rule
    (`ComplianceEndpoints.cs:179`, `:210-215`), reads the intervals from stage 0. Payroll never runs the rest rule, so no
    payroll marker moves, and S147's interim "not checked" notice (T7) retires.
  - *The supplement* reads them from stage 2, when D3's placement fact exists. Payroll's read-only contract on Backend
    tables (`ADR-016:179`) grows from two tables to three, the intervals are frozen by value into the manifest (a
    latest-wins table cannot be pinned by id) and join the digest (`:185`), and the marker moves.
- The 11-hour rule then lives in two places, the Skema screen's own check and the engine's; the build either proves
  that they agree or has the server serve its verdict to the screen. How an interval is assigned to a day follows
  ADR-039's rule for timed entries (S148's refinement). The send check's "ordinary hours that name a project" predicate
  (`ApprovalEndpoints.cs:2241`) is revisited when D11's typed time arrives.
- **Closes** QUAL-234.

### D11 — Which registered time counts as worked time, and how much (QUAL-232)
- **Refused first, then built on the sourced rule (D-3 (c)).** Stage 0 refuses on-call, call-ins and travel rather than
  pay them wrongly (OQ-4 = (b), with its mechanism R-18): `POST /api/time-entries`, which today stores any activity type
  as free text (`TimeEndpoints.cs:157`; QUAL-202), answers 422 for any type other than `NORMAL`, and payroll refuses, by
  name, any stored entry of another type. The requirement stays (`SYSTEM_TARGET.md:79-81`). The sourced model is built in
  a later stage: stage 2 or later, after D10's intervals and with D3's order record, possibly after S148.
- **Three facts per type and agreement,** not `OnCallDutyRule`'s pay rate: a worked-time fraction, a supplement basis and
  a rest flag (R3, `sick-day-credit-and-agreement-texts-research.md:262-306`; RR-7).
  - *HK and PROSA* (Statens arbejdstidsaftale § 7 nr. 3–5): travel on an official trip counts in full, capped at 13 h per
    working døgn (the 24 hours from the start of the working day), never halved; on-call counts 1/3 at home and 3/4 at
    the workplace; a call-in at under 24 hours' notice counts at least 3 h, except when it adjoins normal hours or is part
    of on-call, and only when the person must come to the workplace, conditions read from D10's clock times.
  - *AC-agreement staff:* travel only, at most 13 h per døgn *"medmindre andet aftales lokalt"* (a local parameter, D6).
  - *Supplement basis:* § 13's +25 % on the hours actually worked for travel and call-ins (ignoring the cap and the
    minimum); on the counted 3/4 for workplace on-call; none for home on-call; § 14 on the counted third for home on-call.
  - *Rest flag:* home on-call is rest unless it defeats rest's purpose (PAV 19.7.1), decided case by case; its default is
    held for a domain expert and never hard-coded as "always rest" (provisional item 13).
- **What retires.** On-call's second payment at 0.33 (`OnCallDutyRule.cs:36-48`; `AgreementRuleConfig.cs:47-48`) and
  the dormant non-working travel rate of 0.5 (`AgreementRuleConfig.cs:58`), which no source supports. SYSTEM_TARGET §C's
  "working vs non-working" travel split (`SYSTEM_TARGET.md:81`, `:229`) is reworded to the sourced rule.
- A leader assigns on-call and orders travel on D3's order record. Skema gains a way to register the types.
- **Closes** QUAL-232 and QUAL-202.

### D12 — Configured values reach pay: ADR-014 on the pay path (QUAL-233, QUAL-204)
- **The editors reach pay (D-6 (a)).** For each day of a month, payroll resolves the published agreement config, the
  position override and the institution's local profile in force that day (central → position → local), at the service
  layer, and passes them to the pure engine in the request; the engine never reads a database (ADR-002). This is
  ADR-014's approved design (`ADR-014:29-32`, `:41`) and SYSTEM_TARGET §I's self-service without a redeploy
  (`SYSTEM_TARGET.md:202`, `:205`, `:207`), implemented on the pay path for the first time.
- **Every configured value is dated and frozen.** Agreement configs and position overrides gain an "applies from" date
  (QUAL-182; a published config has a `published_at` time and no such date, `init.sql:1328`, `:1384`). Every value a
  calculation used is frozen into its manifest, replacing ADR-016 D10's "fingerprint what is code" (`ADR-016:169`,
  `:181`; `ConfigFingerprint.cs:13-17`); the refusal `replay-config-changed` (`ReplayRefusedException.cs:38`) changes
  meaning or retires.
- **A publish never changes an exported month.** Exported months change only by an explicit correction (ADR-013, ADR-034).
  An "applies from" date may lie in the past; it reaches an exported month only through an explicit correction
  (ADR-013), never by the publish.
- **One path for every configured value this ADR adds:** office hours (D3), the flex parameters (D6), the paid-absence
  credit basis (D2), customary days (D9). The two HR-recorded facts about a person, flex membership (D2) and the overtime
  regime (D4), are not configuration; they are resolved under ADR-040 and frozen the same way.
- **A stage of its own,** placed before the first stage that needs a local value. Until it lands, the agreement editor
  and the local-profile editor say "affects screens and booking; does not yet affect pay" (D-6.5), so nobody believes an
  edit changed pay.
- **Answers** QUAL-204: yes, a local profile changes pay. **Closes** QUAL-233 and QUAL-182, and with them two defects a
  split (central configs as code, local values as data) would have left: an agreement a GlobalAdmin creates could not be
  paid (`RuleRegistry.cs:446-447`), and the screen and the payslip would still disagree after a central edit.

## How it is built: the stage map (F-1 = (b))

*In plain words.* The model is built in self-contained stages. Each is correct for ordinary, unordered hours, which are
most hours, and its sprint log names what it does not yet handle. A stage that changes payroll's arithmetic moves the
**evaluation-model marker**, the label stored with every payroll calculation that names the arithmetic which produced
it (`WeeklyTimelineCore.cs:126-134`); a month calculated before the move is then refused on replay, by name, rather than
silently re-proved under arithmetic that did not produce it. Nothing real is lost, because there is no real data, and the
development database is reseeded at each stage ("reseed, don't migrate"). Marker names are S148's to fix; the names below
are proposals, and the new core gets a new name rather than a version of the weekly one. S148 will open on
`weekly-timeline-v2` once TASK-14708 merges: the one marker move S147 makes, for the sick-day credit.

| Stage | In plain words | Marker (proposed) |
|---|---|---|
| **0** | Stop paying what is wrong today, and let the rest check see Skema's times | none unless a stage-0 item changes the engine's arithmetic or the replay contract (R-7) |
| **Config** | Make the editors' values reach pay, dated and frozen | `weekly-timeline-v3` |
| **1** | The monthly norm and flex by default; the calendar split; forfeiture recorded | `monthly-norm-v1` |
| **2** | Decided hours: orders and marks, overtime, supplements, the overtime account | `monthly-norm-v2` |
| **3** | AC extra-work claims | `monthly-norm-v3` |

**Stage 0.**
- *Lands:* D11's refusal of non-`NORMAL` time; QUAL-236 fixed in full (owner Q-2 = (a), 2026-10-28: the child's-sick-day
  quotas per agreement and OK version, the per-episode limit, OK26's third day and call-home day); D10's rest-check half,
  retiring T7's notice; manual hours retired and one source of time of day per hour (both placed here by the build input,
  for S148's refinement to confirm); D2's bound on partly worked paid-absence days, in both save directions, on the
  central credit basis; D12's editor label; QUAL-239's interim (the compensate route reads its guarded update's result
  and refuses 409 before enqueueing).
- *Marker:* none for payroll unless a stage-0 item changes the engine's arithmetic or the replay contract — QUAL-236's
  call-home day joining the credit class, D2's bound computed engine-side, or the digest's line form changing when the
  voluntary flag is rerouted or retired; then the weekly core's marker moves once (ADR-016 D10, `:180`, `:185`), or
  S148's refinement names the refusal that does the same job.
- *Named interim limits:* on-call, call-ins and travel cannot be registered until D11's later stage; the editors do not
  reach pay (labelled); every pay figure is still today's weekly model.

**The config stage.**
- *Lands:* D12 in full; the local profile's new columns (D6's table; the cap columns exist; their pay effect, the null
  central caps and the live demo caps land with stage 1), the office-hours windows, the paid-absence credit basis with
  the AC label, the customary-days table and its editor; the two HR-recorded facts, flex membership and the overtime
  regime, with one HR editor (the build input's placement; S148's refinement confirms); demo profile rows, seeded with
  their cap values (37 h, as SDU and DTU), which have no pay effect until stage 1; and demo customs.
- *Marker:* the arithmetic is unchanged but the replay contract changes, since configs are frozen rather than
  fingerprinted; proposed: move the marker, so that a manifest written before the stage is refused by name. S148's
  refinement confirms this, or names the refusal that does the same job.
- *Named interim limits:* the values are dated and frozen, but the weekly formula still computes pay, on today's
  central caps; no pay figure changes, and the stage's pin is identical demo pay lines before and after.

**Stage 1.**
- *Lands:* D9's calendar split first, then D1's monthly norm for all three agreements; D2's flex by default, with the
  weekly overtime rule retired for every agreement; the paid-absence credit through the planned-hours figure, including
  the office-hours credit; flex membership switching settlement off; the central caps nulled and the demo caps live,
  with D6's forfeiture or "no flex cap configured", the
  minus and daily caps, the leaver's settlement (and the opening balance, if S148's refinement schedules it); D9's paid
  customary days in both modes, and closure days; F1's settlement machinery retired; `SUPPLEMENT_CALC` returning no lines.
- *Marker:* `monthly-norm-v1`.
- *Named interim limits:* ordered hours go to flex hour for hour, with no +50 % and no overtime account; no § 13 +25 %
  and no § 14 +50 %; AC extra work is flex only; every HK and PROSA demo payslip moves.

**Stage 2.**
- *Lands:* D3's records of decided hours (the acceptance half first if S148 so chooses; the order half may follow),
  with the cut-off, the four conditions, late decisions as movements, and the pre-approval switch retired; D4's +50 %,
  routed by the regime, with the part-timer variant; D7's supplement and § 14, reading D10's intervals (payroll's
  contract grows to three tables, then the decided records); D8's ledger, clocks, elections, time off in lieu, HRP-021,
  and the retirement of the yearly row and the compensate and choice routes; D3's office-hours filter; D6's
  transfer-to-account action and the `MaxOvertimeHoursPerPeriod` warning; D11's model, if S148 places it here.
- *Marker:* `monthly-norm-v2`.
- *Named interim limits:* AC merarbejde is still flex only; a saved day taken is registered as paid leave until the
  saved-days balance lands; typed time is still refused unless D11 lands here; if the order half lands later, rosters
  are marked afterwards each time.

**Stage 3.**
- *Lands:* D5's AC claims (the claim record, the quarterly reminder, the standby condition, the regime gate, the
  decision-month movement, earnings into D8's ledger (after research cell R-B; the claim record, reminder and regime
  gate may land first)), the AC part-timer check, and AC's time-off deadline once R-B is answered.
- *Marker:* `monthly-norm-v3`.
- *Named interim limits:* none of the model's own, except items S148's refinement places later.

**Later, or not yet placed.** D11's sourced model if not in stage 2; the saved-days balance (after R-A); the HR-entered
opening balance; the suspension of flex in peak periods; AC's annual-norm flex.

**If S148 cannot carry every stage.** The sprint log records which stages landed; each remaining stage carries its
marker, scope and named limits into the next sprint, and ROADMAP § 1 points there (the deferred-branch pattern).

## Provisional items (owner defaults pending a qualified expert)

*In plain words.* Where the sources leave a value open, StatsTid still has to compute something. A **default** is
applied now, labelled provisional, and recorded in the month's manifest where it affects pay, until a qualified domain
expert confirms or replaces it. An **expert question** applies nothing special until the expert answers. This is the
ADR's one list of such items.

1. *Default, owner-ruled 2026-10-29.* A part-timer's ordered hours above their own norm earn +50 % (FV2025-795's direction), not § 8 stk. 2's printed 1:1, until the state's implementing text; held as the `PartTimeBandRule` variant. — D4; F-7.
2. *Default.* The award's "comparable full-timer" condition is held as met for every part-timer, with nothing checked; AC-agreement part-timers were not examined and are checked with D5 in stage 3. — D4; D-10.4, D-10.2 item 8.
3. *Default, owner-ruled 2026-10-29.* For AC-agreement staff a paid sick day is credited by § 7 nr. 2 by analogy, changeable through the local credit basis. — D2; D-8b.
4. *Default.* AC's monthly flex uses D1's month-norm function, and the AC norm falls on søgnehelligdage. — D1; D-9.4.
5. *Default.* The AC values are ADR-024 D2's three states, CONTRACTUAL / DISCRETIONARY / NONE, provisional; whether special and chief consultants differ is the expert's (ADR-024 left it pending Phase B; C5 item 7 puts both under one-off payments). — D4; D-11.4.
6. *Default.* The per-day norm is `round(weekly × fraction ÷ 5, 2)`, summed over the month's norm-bearing weekdays (S145 Q2 = A's precision rule). — D1.
7. *Default, MEDIUM.* A partly worked day of paid absence is credited at most the credit basis minus the hours worked (the max(worked, planned) reading); a local agreement may differ. — D2; R-7.
8. *Default.* At leaving, a positive flex balance not taken during notice is forfeited, and a negative one is recorded, not deducted. — D6; R-4.
9. *Default.* A leaver's owed time off is paid in the leaving month (R-C is owed). — D8; D-5.P9 (ii).
10. *Default.* The default pay at the § 10 deadline lands in the pay run of the month after the deadline month, and the same holds for § 14. — D8; D-5.P4.
11. *Default.* A breach of a minus cap is recorded and shown to the leader, never deducted automatically. — D6; R-2.
12. *Default.* At an office-hours institution, ordered weekend and søgnehelligdag work stays § 14's whatever the office hours. — D3; D-7.P2.
13. *Expert question.* Home on-call's rest flag: rest unless it defeats rest's purpose, decided case by case; no default is hard-coded. — D11; D-3.5.
14. *Expert question.* Part-timers under office hours: whether a window scales, and whether the third condition uses their own per-day amount. — D3; D-7.P5.
15. *Expert question.* How a part-day customary credit scales by fraction, and what working on a paid customary day earns. — D9; D-12.P3, D-12.P5.
16. *Expert question.* What a deficit means for someone off the flex scheme (the proposal: nothing carried, nothing deducted). — D2; D-8a.
17. *Expert question.* Whether PROSA groups 3–4 who do not literally meet § 8 stk. 3 fall back to overtime; under D4 a value HR sets. — D4; C3.
18. *Expert question.* Retiring the local profile's `weekly_norm_hours` override and its Monday alignment rule (ADR-017 D9a), whose premise D1 removes. — D6; D-8 "Premises made moot".
19. *Expert question.* Settlement-period values other than `month`, and the mechanics of suspending flex in peak periods. — D6; R-2.
20. *Expert question.* Fixed days off for part-timers (V1's exception) while the fraction is spread over five weekdays. — "What this ADR does not decide".

## Owed research

*None blocks this ADR's acceptance.* Each names what it blocks.

- **R-A — § 11 in full.** Who sets the saved-days maximum; how saved days are taken; whether they lapse; what happens at leaving; whether § 14 time off may be converted too; whether AC § 15 mirrors it; and whether the saved-days election may be made before the deadline. *Blocks:* building the saved-days balance (D8) only.
- **R-B — The AC merarbejde time-off deadline** under § 14 stk. 5, if there is one. *Blocks:* a decided claim earning into D8's ledger (S3-3) and the deadline item (S3-4); the claim record, the reminder and the regime gate (S3-1, S3-2) may be built first. *Input:* C5 already records § 14 stk. 5's following-quarter rule (`agreement-cells-reverification-research.md:190`, `:589`); the cell confirms it, the quarter's start and the cash default (stk. 6).
- **R-C — A leaver's owed time off and saved days: when they are settled, and in what form.** *An expert item for S148's refinement;* until then, provisional item 9.
- **R-D — The rationale for holding "comparable full-timer" as met.** The adjudicator's reasoning, that where no comparable full-timer exists the directive's definition falls back to the applicable collective agreement (clause 3(2) of the framework agreement annexed to Directive 97/81/EC), is from the adjudicator's knowledge and must be verified before it is quoted. *Blocks:* quoting it; not the default (provisional item 2).
- **R-E — Watch for the state's text implementing FV2025-795.** None as of 28 October 2026 (R4; OK26 funds the negotiation). When it appears, it is encoded as a new dated variant of `PartTimeBandRule`. *Blocks:* nothing; it ends provisional item 1.
- *Done in S147, for the record:* the OK24 texts of §§ 13–14 were verified and are identical to OK26 (R5), which this ADR's draft had promised; the HK pay-ceiling provisions were re-cited against Medst. 087-25 and are identical (D-11.5, `agreement-cells-reverification-research.md:529-539`).

## What it retires, and what survives

| Retires (built in S148 onward) | Where it lives today | Stage |
|---|---|---|
| Weekly overtime and its tiers | `WeeklyTimelineCore.cs:152-193`; `OvertimeRule.cs:33-95`; `AgreementRuleConfig.cs:43-44` | 1 |
| F1's settlement-day machinery | `WeeklyContext.cs:42` (`SettlementDay`), `:57` (`SettledWeeks`), `:91` (`MidWeekAgreementChange`) | 1 |
| Context days; the prior-month approval wait | `PeriodCalculationService.cs:3313` (`EmployedContextDays`), `:3691`, `:3727` | 1 |
| Do 7: the export rechecks the previous month under `FOR SHARE` | `PayrollExportService.cs:450-459` | 1 |
| Q7 (b): a correction names M+1 | `RetroactiveCorrectionRequested.cs:56-63` | 1 |
| HRP-001b: the `PRIOR_MONTH_REOPENED` worklist row | `HrBackdateWorklistRepository.cs:80`; `ApprovalEndpoints.cs:1810`; HRP register `:98` | 1 |
| QUAL-209's mid-week refusal | `PeriodCalculationService.cs:3648`; `PeriodNotCalculableException.cs:46` | 1 |
| Frozen QUAL-209, -210, -212, -213 (closed as obsolete) | `quality-finding-register.md:433`, `:434`, `:436`, `:437` | 1 |
| The week-based period setting; AC's 4-week overrides | `AgreementRuleConfig.cs:13`, `:61`; `PositionOverrideConfigs.cs:26-46` | 1 |
| Grundlovsdag as a holiday, in three lists | `DanishPublicHolidays.cs:49`; `init.sql:367`, `:380`, `:397`; `DemoSeed/Generation/DanishHolidays.cs:68` | 1 |
| Automatic `FLEX_PAYOUT` | `FlexBalanceRule.cs:44-48`, `:68-82` | 1 |
| The central flex caps | `CentralAgreementConfigs.cs:23`, `:46`, `:77` (OK26 `:108`, `:131`, `:162`) | 1 |
| "Fingerprint what is code" | `ADR-016:169`, `:181`; `ConfigFingerprint.cs:13-17` | config |
| Manual hours | `init.sql:1977` (the readers are in the build input) | 0 |
| The holiday 2.0 branch; evening, night and weekend rates | `SupplementRule.cs:54-117`; `AgreementRuleConfig.cs:40` | no lines from 1; replaced in 2 |
| The budget pre-approval; the pre-approval switch; the governance check | `init.sql:2027-2038`; `AgreementRuleConfig.cs:80`, `init.sql:868`; `OvertimeGovernanceRule.cs:106-114`, `OvertimeEndpoints.cs:60` | 2 |
| The employee's compensation choice and its routes | `init.sql:1468`, `:1474`, `:1480`, `:1486`; `AgreementRuleConfig.cs:78`; `OvertimeEndpoints.cs:606-751` | 2 |
| The yearly overtime row and the compensate route | `init.sql:1992-2008`; `OvertimeEndpoints.cs:512-603` | 2 |
| The 100 % overtime wage types | `init.sql:2051-2054`, `:2070-2073` | 2 |
| On-call's 0.33 payment; the 0.5 non-working travel rate | `OnCallDutyRule.cs:36-48`; `AgreementRuleConfig.cs:47-48`, `:58` | D11's stage |

| Survives | Why |
|---|---|
| The per-day amount (`DayNorm.cs:44-52`) | The month norm's building block (D1); only the "holidays do NOT reduce the norm" sentence (`:28-29`) goes |
| Flex settled once over the month's own days (`WeeklyTimelineCore.cs:295-335`) | Already the monthly shape D2 needs |
| Payroll reading its own hours; split months paid part by part (QUAL-149/150) | Independent of the weekly basis |
| The frozen-input replay proof and its marker contract (ADR-016 D10; `PeriodCalculationService.cs:2610`, `:2770-2779`) | Decisions, movements, intervals and configs become frozen inputs, so replay keeps proving them |
| The read-only contract on Backend tables (`ADR-016:179`) | It grows (D10, D3); its shape is the precedent for the overtime movements (D8) |
| The S146 flex ledger (`init.sql:4633-4640`, with TASK-14605's chain and S147 TASK-14700's walk) | Its rails carry the new closings (D6) |
| The typed local profile (`init.sql:856-872`) | Home of the local flex rules (D6) |
| ADR-024 D7's design and dormant plumbing (`init.sql:2027-2038`, `:2551-2570`) | The starting point for D3, reshaped |
| ADR-024's per-category overrides (`init.sql:2448`, `:2524-2533`) | The regime's seed (D4) |
| The pay / time-off overtime wage types (`init.sql:2047-2050`, `:2066-2069`) | D8 needs this split; only the 100 % rows retire |

## Consequences

- **Reseed, don't migrate, once per stage.** There is no real data; at each stage the database is reseeded, and no export
  record, manifest or balance is migrated. *A deployed system would need a dated switch by pay period and a plan for
  correcting months paid under the old model; this ADR would then be far more expensive.*
- **The evaluation-model marker changes once per stage.** `EvaluationModel` (`WeeklyTimelineCore.cs:134`) names the
  arithmetic of every rule endpoint payroll calls (`:126-133`). The stage map lists each move. The new core is not
  weekly, so it gets a new name, not a version of the weekly one.
- **Replaying an older manifest is refused by name.** The replay compares markers first and refuses with
  `replay-evaluation-model-changed` (`PeriodCalculationService.cs:2770-2779`; `ReplayRefusedException.cs:41`). The old
  manifest stays readable as the record of what was paid; it cannot be re-proved under arithmetic that did not produce
  it, which is the honest answer. After each reseed such manifests exist only in developer databases.
- **Not an OK-version transition.** The old encoding was wrong for every period, so the new model applies to OK24 and
  OK26 periods alike. Rules that change on a date stay dated: Store Bededag, the *opsparing* window, the saved-days floor
  (74 h, then 111 h from 1 April 2026), the part-timer variant. §§ 4 and 8 are word for word the same in the circular in
  force through OK24 (CIR1H 9551 of 2021; the dossier's S1′), and S147 verified §§ 13–14 likewise (R5).
- **Payroll boundary.** StatsTid exports hours with wage types, never kroner. The pay-ceiling exemption is held as an
  HR-recorded classification, never as pay (D4).
- **Auditability.** Every new decision about hours is an audited record with a creator and a decider (D3); every
  movement of overtime is dated and frozen into the month it lands in (D8); every configured value a calculation used is
  frozen into its proof (D12). No exported month is ever edited.
- **Frozen by value (ADR-016 D10's rule).** Inputs that are data are frozen in the manifest or pinned by id; what cannot
  be pinned by id is frozen by value. So every new frozen input in a latest-wins or editable store — the intervals
  (D10; `work_time_projection` keeps one row per employee and date, overwritten on each save, `init.sql:1973-1984`,
  `WorkTimeProjectionRepository.cs:29-32`, `:93-101`), a decided record's state at calculation time (D3), the overtime
  movements (D8), configs (D12), customary days (D9) — is frozen **by value** into the manifest; event-id-keyed,
  superseded-not-removed inputs may stay pinned and digested; the shape is S148's.
- **Security and access.** Four-eyes now covers every decision about hours and compensation (D3's four conditions), and
  the authority to decide is the month approver's, not bare organisation scope. SEC-056 closes in design.
- **Usability.** Leaders gain a decision step inside the month approval they already do, and a "time off due" list;
  employees mark hours nobody ordered in advance. Institutions configure their own flex rules, office hours and customs;
  HR records two facts per person.
- **Tests asserting today's figures change.** A grep at `c4eb6184` of `tests/` and `tools/` for the retired identifiers
  (`OvertimeRule`, `WeeklyTimelineCore`, `WeeklyContext`, `FLEX_PAYOUT`, `OVERTIME_50`, `OVERTIME_100`, `MERARBEJDE`,
  `HOLIDAY_SUPPLEMENT`, `weekly-timeline-v1`, `agreement-change-mid-week`, `AgreementChangeMidWeek`,
  `PRIOR_MONTH_REOPENED`, `PriorMonthReopened`) finds 51 files; the same grep at `fdb52d48` finds 47, where this ADR's
  draft counted 46 with a narrower pattern. It is an upper bound that each stage's census settles. They include S145's
  September example (`WeeklyTimelineCore.cs:85-87`) and the demo's June deficits.
- **New hand-offs for leaders and HR, now designed.** Each gets an HRP register row: the leader's decision step on
  marked hours (D3); the overtime regime (D4); flex membership (D2); the leaver's flex settlement (D6); the opening
  balance (D6, when built); the cap exception (D6); the 3-month deadline, HRP-021 (D8); and the AC claims (D5, when stage
  3 designs them). HRP-001b retires with F1. Recording a customary day is configuration, not a hand-off (D9).

## Documents owed at acceptance

*The Orchestrator's; `docs/**` and `SYSTEM_TARGET.md` are not the planner's to edit, and SYSTEM_TARGET text also needs
the owner's approval.* Each line names its target.

- `SYSTEM_TARGET.md:44` and its S146 note `:46` (§B): "Irregular norm periods (e.g. 4-week norm)" → the calendar month (D1).
- `SYSTEM_TARGET.md:81` (§C): the travel line reworded to the sourced rule: travel counts in full, capped at 13 h per working døgn; on-call (`:79`) and call-in (`:80`) stay, on D11's model.
- `SYSTEM_TARGET.md:86` (§C): "Calendar context (public holidays)" gains the three kinds of day, including each institution's customary days (D9; D-12.2 item 5).
- `SYSTEM_TARGET.md:165` and its note `:167` (§G item 1): the norm period becomes a settlement-period parameter, month by default (D1, D6).
- `SYSTEM_TARGET.md:168` ("within central limit") and `:174` ("local maxFlexBalance must be <= central MaxFlexBalance") (§G): corrected; there is no central flex cap (D6; batch-2 ruling 2). The principle at `:175` stands for rates.
- `SYSTEM_TARGET.md:202-207` (§I): the self-service it describes is now honoured on the pay path, with an "applies from" date on publishes (D12); `:229` (the working and non-working travel rates) → D11's three facts; `:247` and its note `:249` (the week set) → the month.
- `SYSTEM_TARGET.md:377` and its note `:379` (§L, "currently calculates overtime correctly"); `:381-384` (the ceiling) → an optional warning over decided hours; `:386-391` (pre-approval) and its note `:393` → D3's decided records.
- `SYSTEM_TARGET.md:403` and its note `:405` (§M, the employee's choice) → the employer chooses, plus the employee's two elections; `:407-412` (the overtime balance) → D8's ledger, keeping `:410`'s "creates an absence entry" as time off in lieu; saved days added (D8).
- `docs/references/danish-agreements.md:48-60` (supplement windows, note `:50`) → D7.
- `docs/references/danish-agreements.md:62-69` (thresholds) and the S146 note `:64`, which still says part-timers' hours up to full time are 1:1 (§ 8): a dated note that § 8 stk. 2's 1:1 band is not applied, by owner ruling of 2026-10-29, provisional until the state's implementing text, citing FV2025-795 (D4; D-10.3).
- `docs/references/danish-agreements.md:71-92` (weekly settlement; notes `:73`, `:75`) → superseded by D1 and D2; `:94-111` (compensation model; note `:96`) → D8.
- `docs/references/agreement-source-register.md`: the row the part-timer cell rests on (today SR-HK-OK24-006, `:1090`, or a new row for the `PartTimeBandRule` slot) names FV2025-795 and the owner ruling (QUAL-215's lesson); the rows S146 flagged as disputed get this ADR's verdicts.
- `docs/ARCHITECTURE.md:61`, `:86`, `:150`, `:195-196`: `CentralAgreementConfigs` stops being the static source of truth (D12: seed and defence-in-depth fallback at most; the database configs are what payroll reads, passed in the engine's request); the read-only cross-context contract grows (D10, D3), `ProjectionHoursReader` staying the one Payroll type that names the Backend repositories.
- `docs/knowledge-base/decisions/ADR-014-agreement-configs-database-backed.md`: an implementation note (D12).
- `docs/knowledge-base/decisions/ADR-016-temporal-period-handling.md` D10: the marker per stage; the read contract (three tables, then the decided records); configs frozen in place of fingerprints; the frozen pieces gain regime and membership; the digest's canonical forms: an interval line form if intervals are digested; the entry line form without the voluntary flag if it retires.
- `docs/knowledge-base/decisions/ADR-017-local-agreement-configuration-as-a-profile.md`: the profile's flex columns, the customary-days table, the retired switch, the lapsed `weekly_norm_hours` premise (an expert item).
- `docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md`: D1/D2 (the overrides become the regime's seed); D7's implementation status (`:554`), point 3's "Approval page" (`:220`) → Teamoversigt, and the "Bug #4 seed flip" (`:228`, `:254`) resolved by retirement.
- `docs/knowledge-base/decisions/ADR-032-vacation-consumption-correctness.md:47`: the limitation ends; the credited part of a paid customary day consumes nothing; the divisor reads the planned-hours figure.
- `docs/knowledge-base/INDEX.md`: ADR-043's row → ACCEPTED.
- `docs/operations/hr-follow-up-process-register.md`: new rows for the leader's decision step (HRP-018, `:151-156`, describes the budget page that retires: closed or re-pointed), the overtime regime, flex membership, the leaver's flex settlement (beside HRP-011, `:117`), the opening balance, the cap exception, and later the AC claims; HRP-021 (`:131`) made decision-ready (D8); HRP-001b (`:98`) and HRP-024 (`:123`, "approve the previous month first", whose reason is Q3 = F1) marked to retire with F1.
- `docs/operations/security-finding-register.md:216` (SEC-056), at S147's close: refined per batch-2 ruling 1 and batch-3 ruling 4(b) (the compensate half a latent shape; the High label resting on approve/reject; "closed in design by D-4 = (c) with ruling 1 and by D-5.P2; T9 closes the self part in S147"), cross-referencing QUAL-239.
- `docs/operations/quality-finding-register.md`, at S147's close: QUAL-231 (`:455`) and QUAL-237 (`:461`) gain R-7's widened sentence. At acceptance: QUAL-204 (`:428`) answered (D12); routes for QUAL-182 (`:405`), QUAL-202 (`:426`), QUAL-219 (`:443`), QUAL-221 (`:445`), QUAL-223 item (3), QUAL-225 (`:449`), QUAL-232 (`:456`), QUAL-233 (`:457`), QUAL-234 (`:458`), QUAL-236 (`:460`, stage 0) and QUAL-239 (`:463`).
- `ROADMAP.md` § 1: S148 opens on the stage map.

## Alternatives not chosen

**(a) Keep automatic overtime, and net it out of flex.** Every hour above the norm is still paid as overtime, and those
hours are subtracted from flex. That fixes the double count but not the trigger: it still pays +50 % for hours nobody
ordered, where the sources make "ordered or necessary" a precondition (V3) and say surplus flex hours *"udløser ikke
overarbejdsbetaling"* (V6). On the weekly basis it also needs flex settled net of a week judged after the month has
closed (QUAL-189's S145 addendum). It compromises domain correctness: not a valid path.

**(c) Keep both counts.** Today's behaviour: the hour is paid as overtime and stays in flex. The employee is compensated
twice for one hour; the sources keep ordered work outside the flex account (V8), and the spec keeps the overtime
balance separate from flex (`SYSTEM_TARGET.md:408`). Not a valid path.

*(Keeping the weekly basis with corrected rates was not a candidate: V1 and V12 remove its premise.)*

**Offered to the owner on 2026-10-29, and not chosen.** F-1 (a) one cut-over; F-2 (a) every decision afterwards, with no
record of an order given in advance; F-4 (a) office hours not modelled; F-5 (a) one rule set with everyone on flex, and
(c) rule sets per staff group; F-6 (b) a fixed assessment period per AC group; the quarter as an institution-level
option; and an adjudicator check of that option before ruling — the last two offered to the owner and declined; F-7 (i) the printed 1:1 rule; F-8 (a)
importing base pay, which would cross the payroll boundary and could only be the owner's exception, and (c) inferring the
regime from position, which misclassifies wherever position and pay group diverge; F-9 (a) paid days only; QUAL-232 (b)
the refusal as the end state; QUAL-233 (b) configs are code, and (c) the split; QUAL-234 (b) derived times stamped on
project hours, an auditability smell, and (c) clock times per project, a Skema redesign; Q-1 (b) no AC default, refusing
the month until configured.

**Not offered, with the reason the owner was told.** F-1 (c) keeping weekly overtime until stage 2: one hour counted
twice. F-2 (b) a budget of "up to N hours": it names no hours, so it cannot carry the placement fact, and it can pay
unordered hours. F-3 (a) a yearly total: it cannot apply the deadline; F-3 (c) rails on the export record: compensation
is decided after export; "no election, the deadline always pays": it would pay hours the employee chose to save. Q-1 (c)
crediting an AC sick day with nothing: a sick employee would lose flex. F-9 (b) closure days only: it cannot hold the
paid custom found on most state workplaces; F-9 (d) not modelled: this ADR already names customary days as the third
kind of day. Manual hours treated as daytime hours: a rest check that passes when it could not look. For F-7, the dated
variant's shape was told rather than asked, and refusing the band until the state's text arrives was not offered: there
is a binding direction, and refusal would hold up every part-timer's month for an unknown time.

## What this ADR does not decide

The narrow daily overtime ladders; shift workers (chapter 4, CO II's evening/night agreement); AC Bilag 6's area
payments; the 48-hour weekly average (Bilag 1 § 5), a ceiling rather than an overtime trigger; storing contracted weekly
hours instead of a rounded fraction; fixed days off for part-timers (the V1 exception), not modelled while the fraction
is spread over five weekdays (provisional item 20); per-group flex caps such as AU Foulum's 200 h (D2's limitation); a
second leader approving a leader's own order (D3; a later local parameter at most); an opening overtime balance at go-live
(S148's refinement, with D6's opening flex balance); the AU template's monthly office-hours variant; core hours for pay;
settlement periods other than the month (recorded for the expert); which institutions grant which customs, which is
each institution's configuration.

## References

- Dossiers: `docs/references/overtime-flex-model-research.md` (V1–V12); `docs/references/agreement-cells-reverification-research.md`
  (C1–C7, and its § 5 code-impact table); `docs/references/sick-day-credit-and-agreement-texts-research.md` (R1–R5).
- Refinements: `.claude/refinements/REFINEMENT-s146-payroll-correctness.md` rev 3.2 ("Decisions recorded"; "Forks left
  to the S147 model refinement"), owner rulings 2026-10-23: OQ-1 = (iii), OQ-2 = (a), OQ-3 = (b);
  `.claude/refinements/REFINEMENT-s147-defects-and-model-rulings.md` rev 4.1 (OQ-1 … OQ-4, the decide track's agenda).
- S147's decide track: the briefs `.claude/plans/s147/decide/D-01-…` to `D-12-…`; the adjudications
  `.claude/reviews/S147-D-batch1-adjudication.md`, `S147-D-10-adjudication.md`, `S147-D-batch2-adjudication.md`,
  `S147-D-batch3-adjudication.md`; the owner's answers in `docs/sprints/SPRINT-147.md`, "Owner rulings in force"; the S148
  build input `.claude/plans/s147/S148-BUILD-INPUT.md`.
- ADRs: ADR-002 (pure engine), ADR-013 (no-cascade corrections), ADR-014 (configs in the database), ADR-016 D10
  (manifest and replay), ADR-017 (the local profile), ADR-024 D1/D2/D7 (role overrides; overtime authorization), ADR-032
  D7, ADR-034 (export lock, corrections only), ADR-039 (timed entries across midnight), ADR-040 (as-of rule).
- Registers: `docs/operations/quality-finding-register.md` QUAL-189 (`:413`), QUAL-215 … QUAL-227 (`:439-451`),
  QUAL-231 … QUAL-239 (`:455-463`); `docs/operations/security-finding-register.md` SEC-056 (`:216`);
  `docs/operations/hr-follow-up-process-register.md` HRP-001b (`:98`), HRP-021 (`:131`).

## Revision history

- **Draft 1 (2026-10-23, S146 D5).** Drafted by `planner` (Opus 5.5) from V1–V12, C1–C7, refinement rev 3.2 and the
  owner's OQ-1 … OQ-3; every code citation verified at `e5aba3a4`. Status PROPOSED, for the Orchestrator to copy to
  `docs/knowledge-base/decisions/` and index.
- **Draft 2 (2026-10-23, S146 D5).** Applies the Fable adjudication (`.claude/reviews/S146-D5-ADR043-adjudication.md`):
  - R-40: D3's decision bullet and D5's leave-flex bullet softened to F-2.
  - R-41: D7's placement-versus-surplus sentence, plus an F-2 sub-question. Two consequential wording fixes keep F-1 (b)
    and F-2 (a) consistent with it.
  - R-42: F-10 deleted. D1 gains S145 Q2 = A's precision sentence (the header's Q2 = A pointer moves from F-10 to D1),
    and "does not decide" gains stored weekly hours.
  - R-43: the V1 fixed-days-off exception under "does not decide".
  - R-44 (2): D1's one-input-resolution sentence.
  - R-45 (3), (4): every citation re-verified at `fdb52d48` (no cited `src/`, `docker/`, `tools/` or `docs/` file changed
    since `e5aba3a4`; the test-file count is still 46). D6 states the ledger's status at that sha.
  - R-45 (5): the Status field quotes the refinement's OQ-2 wording.
  - R-46: the two glosses and the Status field's review line.
- **S147 O-1 (2026-10-28, `e7478f24`).** The Orchestrator's drift fixes before the rulings round (three of the four touch this ADR): the worked
  example's supplement note (Skema hours earn none, QUAL-234); F-5 (a) no longer claims that local profiles reach the
  engine; D6's status line brought to the S146 close.
- **Amendment A1 (2026-10-29, S147 decide track).** Drafted by `planner` (Opus 5.5) from the twelve rulings and the
  adjudicated recorded proposals; status "PROPOSED — amended 2026-10-29; ACCEPTED after the dual-lens review (O-10)".
  - The nine forks F-1 … F-9 become decisions, each with the owner's verbatim answer, its date and its reason (the
    rulings table), folded into D1–D9. Three decisions are added: D10 (QUAL-234, clock times, "one source of time of day
    per hour"), D11 (QUAL-232, worked-time classes, three facts per type and agreement) and D12 (QUAL-233 with QUAL-204,
    ADR-014 on the pay path). The recorded proposals R-1 … R-7, D-1.4, D-2.4, D-3.5, D-4.P1–P6, D-5.P1–P10, D-6.5,
    D-7.P1–P6, D-9.4, D-10.4, D-11.4 and D-12.P1–P9 are folded into the decision each belongs to, with batch-2 rulings
    1–5 and batch-3 rulings 1–4.
  - Added: "What changes, in plain words"; items 8–10 under "What is wrong today"; the worked example's overtime-account
    row; S147's addendum to "Why it happened"; the stage map; one list of provisional items; one list of owed research
    (R-A … R-E); "Documents owed at acceptance" as its own section; the options offered and not chosen, and those not
    offered.
  - The header's "Supersedes / amends" gains ADR-024 D1/D2, ADR-017, ADR-014 (confirmed), and ADR-016 D10's read
    contract, fingerprint contract and frozen pieces; "Closes when built" gains QUAL-182, QUAL-202, QUAL-204 (answered),
    QUAL-225 (designed), QUAL-231's residual, QUAL-232 … QUAL-234, QUAL-237, QUAL-239 and SEC-056 (in design).
  - Every file:line citation re-verified at `c4eb6184`. Changed: `PeriodCalculationService.cs:2220` → `:2610`,
    `:2380-2388` → `:2770-2779`, `:3170` → `:3313`, `:3505` → `:3648`, `:3548` → `:3691`, `:3584` → `:3727`;
    `ReplayRefusedException.cs:38` → `:41` (the evaluation-model reason; `:38` is now cited for `replay-config-changed`);
    `PayrollExportService.cs:334-342` → `:450-459`; `ApprovalEndpoints.cs:1801` → `:1810`, `:2110` → `:2119`;
    `SYSTEM_TARGET.md:163` → `:165`, `:243` → `:247`, `:371` → `:377`, `:374` → `:382`, `:383` → `:391`, `:393` → `:403`,
    `:396` → `:408`; `db-schema.md:258-270` → `:258-271` (now cited in D4). The test-file count is
    re-taken (51 at `c4eb6184`).
- **Amendment A1, review cycle 1 (2026-10-29).** `planner` (Opus 5.5) applied the adjudication of the dual-lens review
  (`.claude/reviews/S147-ADR-043-adjudication-c1.md`, R-1 … R-11; no owner decision altered):
  - R-B gates a decided AC claim's earning and the deadline, not all of stage 3 (R-1); D9's part day is a clock time or
    full-time hours (R-2); QUAL-239's interim refusal in stage 0 (R-3); the AC regime is ADR-024 D2's three states, all
    provisional, DISCRETIONARY defined (R-4); the office-hours warning is against the full-time week (R-5).
  - The config stage changes no pay figure; the caps' pay effect moves to stage 1 (R-6); stage 0's marker exception
    covers every arithmetic or replay-contract change (R-7); a paid-absence day is "credited as the day planned" (R-8);
    new inputs in latest-wins or editable stores are frozen by value (R-9); `docs/ARCHITECTURE.md` joins the documents
    owed (R-10).
  - Notes: postponements are the leaders' (N1); the send gate's tense (N2); "will open" on `weekly-timeline-v2` (N3);
    D-9's two declined options (N4); a backdated "applies from" date (N5); how a placement is confirmed afterwards (N6);
    the regime's placement marked as the build input's (N9).

<!-- ===== End of the amended ADR-043 text ===== -->

---

## Working-copy revision history

- **WC-0 (2026-10-28).** `planner` (Opus 5.5) seeded this copy verbatim from the knowledge-base ADR at `e7478f24`. The rulings table is empty. Added the planner's notes: the citation drift since `fdb52d48`, and the folds the first six briefs propose.
- **WC-0.1 (2026-10-28).** Note 1 records the coordinator's ruling that re-checking the citations is an Orchestrator docs item. Note 2 adds D-10's two additions. The verbatim body is unchanged.
- **WC-0.2 (2026-10-28).** `planner` (Opus 5.5) applied the batch adjudication of D-1, D-2, D-3, D-6, D-9 and D-11 (`.claude/reviews/S147-D-batch1-adjudication.md`, RULED-WITH-QUESTIONS). Added the one list "Provisional items and recorded proposals (not owner rulings)": D-1.4, D-2.4, D-9.4, D-11.4, D-6.5's stage-0 label and D-3.5's home on-call rest flag, plus D-10's pending fold moved from note 2. The rulings table stays empty (no question answered yet). The verbatim body is unchanged.
- **WC-0.3 (2026-10-29).** `planner` (Opus 5.5) applied D-10's adjudication (`.claude/reviews/S147-D-10-adjudication.md`, RULED-WITH-QUESTIONS). Item 7 moved from "not yet adjudicated" to a recorded proposal: the comparable full-timer held as met, with the corrected rationale (D-10.2 item 7, marked "verify before quoting") and the AC note; it records that the value itself is asked and, once answered, joins this list as a provisional owner default; the ADR text owed (D4's part-timers bullet, `:103-105`) and two Orchestrator docs items at acceptance (the source-register row; `danish-agreements.md:64`) are listed, not done. Note 2 updated to match. The D-1 row and its note (entered on the owner's answer of 2026-10-29) are unchanged. The verbatim body is unchanged.
- **WC-0.4 (2026-10-29).** `planner` (Opus 5.5), at `f37d7cd3`, added note 3: the proposals in the D-4, D-7 and D-8 briefs (draft 1), pending adjudication. The rulings table (seven rows, entered by the Orchestrator) is unchanged, and so is the one list. The verbatim body is unchanged.
- **WC-0.5 (2026-10-29).** `planner` (Opus 5.5), at `26b70a08`, applied the batch-2 adjudication (`.claude/reviews/S147-D-batch2-adjudication.md`, RULED-WITH-QUESTIONS).
  - **Added to the one list:** items 8–26, one plain sentence each with its anchor: D-4.P1–P6, D-7.P1–P6, and D-8's R-1..R-7 as amended (R-2's five amendments, R-5 per ruling 3, R-6 widened per ruling 4, R-7 with QUAL-237's coupling).
  - **Also added:** the adjudicator's five rulings, as items the Orchestrator tells the owner, not asks.
  - **Note 3** now holds the D-5 and D-12 draft-1 proposals, pending adjudication.
  - **No D-4 answer is recorded:** D-4 is being asked. The rulings table (seven rows) and the verbatim body are unchanged.
- **WC-0.6 (2026-10-29).** `planner` (Opus 5.5), at `bd5a3d3f`, applied the batch-3 adjudication of D-5 and D-12 (`.claude/reviews/S147-D-batch3-adjudication.md`, RULED-WITH-QUESTIONS).
  - **Added to the one list:** items 27–36 (D-5.P1–P10; D-5 is told, with no owner question, ruling 1) and items 37–45 (D-12.P1–P9), each with its anchor; a batch-3 preamble saying when each group becomes ADR text.
  - **Item 26 (R-7) widened** to every credit-class type, with ruling 3's verbatim sentence, its quota-type build note and the close item for QUAL-231's and QUAL-237's rows.
  - **Also added:** the owed research R-A, R-B and R-C (none blocks acceptance); the four batch-3 rulings as told items; and the Orchestrator items from rulings 3 and 4 (T9's merge-record sentence, SEC-056's row at close, QUAL-239, QUAL-231/237's rows) and D-12.2 item 5 (`SYSTEM_TARGET.md:86`).
  - **Note 3** now points to items 27–45 and holds nothing pending.
  - **The rulings table** holds eleven rows; the D-4, D-7, D-8a and D-8b rows were entered by the Orchestrator at `bd5a3d3f`. No D-5 row (told, not asked) and no D-12 answer are added here. The verbatim body is unchanged.
- **WC-1 (2026-10-29).** `planner` (Opus 5.5), at `c4eb6184` (the Orchestrator's O-2/O-3 docs commit landed on master during the work, after `991a5890`; it moved no cited line), wrote the amended ADR from the rulings table (D-1 … D-12, D-8 as D-8a/D-8b, D-5 told), the one list (items 1–45), the four adjudications and the twelve briefs.
  - **The verbatim seed is replaced** by the amended text, between the markers "Amended ADR-043" and "End of the amended ADR-043 text". The seed is still the knowledge-base file at `c4eb6184`.
  - **In the amended text:** each fork F-1 … F-9 decided (the owner's verbatim answer, the date, the reason) and folded into D1–D9; D10 (QUAL-234), D11 (QUAL-232) and D12 (QUAL-233/QUAL-204, ADR-014 on the pay path) added; every recorded proposal folded into its decision; the stage map; one list of provisional items (20) and one of owed research (R-A … R-E, two of them new: R-D the directive clause behind "comparable full-timer", R-E the watch for FV2025-795's implementing text); "Documents owed at acceptance"; the header's "Supersedes / amends" and "Closes when built" widened.
  - **Apparatus:** the header rows say where the amended text is; the fold map maps items 1–45 and the adjudicators' rulings to the amended sections; note 1 records the re-check as done.
  - **The line-number re-check (the coordinator's item):** every file:line in the amended text opened at `c4eb6184`. Changed from the knowledge-base text: `PeriodCalculationService.cs:2220` → `:2610`, `:2380-2388` → `:2770-2779`, `:3170` → `:3313`, `:3505` → `:3648`, `:3548` → `:3691`, `:3584` → `:3727`; `ReplayRefusedException.cs:38` → `:41`; `PayrollExportService.cs:334-342` → `:450-459`; `ApprovalEndpoints.cs:1801` → `:1810`, `:2110` → `:2119`; `SYSTEM_TARGET.md:163` → `:165`, `:243` → `:247`, `:371` → `:377`, `:374` → `:382`, `:383` → `:391`, `:393` → `:403`, `:396` → `:408`; `db-schema.md:258-270` → `:258-271`. Changed from the briefs where they are carried: `PeriodCalculationService.cs:2155-2163` (D-1, D-2, D-3) → `:2167-2175`, `:3205`/`:3203` → `:3217`/`:3215`, `:2758-2767` → `:2770-2779`, `:2775-2784` (D-6) → `:2787-2796`, `:2678` (D-11) → `:2690`; `SkemaEndpoints.cs:2051-2067` → `:2049-2066`, `:2057` → `:2055`, `:2084-2106` → `:2082-2104` (D-2, D-3; TASK-14701 shifted the file); the manual-hours census in `SkemaEndpoints.cs` (D-2) shifted by −2, and `:2670` → `:2692`; `SkemaPage.tsx:335-379` (D-2) → 39 matching lines spanning `:25`–`:1090`; `init.sql:2516-2526` (D-11's seeds) → `:2524-2533`; C5 in `agreement-cells-reverification-research.md:572-605` (D-9) → `:583-621`. Not verifiable at this HEAD: the T7 notice's file:lines (TASK-14706/14707, not on master) and TASK-14708's `AbsenceClassification.cs` (not on master); both are cited by brief, not by line.
- **WC-2 (2026-10-29).** `planner` (Opus 5.5), at `9de1ae79`, applied the adjudication of the dual-lens review, cycle 1 (`.claude/reviews/S147-ADR-043-adjudication-c1.md`, RULED; Codex FINDINGS 3B/4W/1N, Fable READY-TO-ACCEPT 5W/11N). No owner answer and no rulings-table row changed.
  - **In the amended text:** R-1 (R-B gates S3-3's earning and S3-4; its input, C5's following-quarter rule); R-2 (D9's part: a clock time where office hours are set, else full-time hours); R-3 (D8: QUAL-239's interim 409 in stage 0; the header's "Closes when built"); R-4 (D4's AC values, ADR-024 D2's three states with their definitions; Prov. 5; D5 "CONTRACTUAL only"); R-5 (D3's office-hours warning against the full-time week); R-6 (the config stage's "Lands" and interim limits; stage 1's "Lands"; D6; the retire table's central caps → stage 1); R-7 (stage 0's marker exception); R-8 ("What changes" for employees; D2's title; D8's time off in lieu); R-9 (the rule "Frozen by value" stated once, in Consequences; D10, D3, the header, the ADR-016 line of "Documents owed"); R-10 (`docs/ARCHITECTURE.md` in "Documents owed"); R-11 N1 (postponements to the leaders), N2 (the send gate's tense), N3 ("will open"), N4 (D-9's two further declined options), N5 (a backdated "applies from" date), N6 (how a placement is confirmed afterwards, after D7's first bullet), N9 (the regime's placement marked as the build input's). The ADR's own revision history gains "Amendment A1, review cycle 1".
  - **Apparatus:** item 14 (D-7.P1) corrected per R-5; the new section "To tell the owner at acceptance (O-10)" holds the adjudication's two sentences (R-4, R-6) verbatim.
  - **Not applied (declined by the adjudication):** Fable N7, N10, N11; Codex W1's "adjudicated mapping before building"; Codex W2's sprint-ledger alignment; the event-id supersession alternative; R-B as a stage-wide gate.
  - **Placement notes:** N2's citation is `ApprovalEndpoints.cs:2230`, where the interval-plus-manual sum sits at both `68187917` and `9de1ae79` (the adjudication says `:2229`, the line before); N4 says "offered to the owner and declined", as `docs/sprints/SPRINT-147.md:12` records it ("offered and not chosen"), for the adjudication's "asked for and declined"; R-5's "item 18" is written "provisional item 18" in the ADR and "Prov. 18" here, because this file's own list has a different item 18; R-B's and N6's bare citations carry their file names (`agreement-cells-reverification-research.md`, `.claude/plans/s147/decide/D-04-marking-workflow.md`).
- **WC-2.1 (2026-10-29).** `planner` (Opus 5.5), on the Orchestrator's ruling: the four inconsistencies WC-2 left are fixed, each following a ruling already made (no new adjudication). In the stage map:
  - row 0's marker cell → "none unless a stage-0 item changes the engine's arithmetic or the replay contract (R-7)";
  - stage 0's "Lands" gains QUAL-239's interim (R-3);
  - stage 3's "Lands" marks the earnings into D8's ledger "after research cell R-B; the claim record, reminder and regime gate may land first" (R-1);
  - the config stage's "Lands" says the demo profile rows are seeded with their cap values, which have no pay effect until stage 1 (R-6). The build input's C-12 and S1-7 now say the same (its draft 2.1).
  - The batch-3 historical record of R-B ("Blocks: stage 3 only") is left as it is. The two factual corrections stand, confirmed by the Orchestrator: N2 cites `ApprovalEndpoints.cs:2230`; N4 says "offered to the owner and declined" (`docs/sprints/SPRINT-147.md:12`).
