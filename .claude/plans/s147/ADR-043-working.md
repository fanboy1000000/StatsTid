# ADR-043 — S147 working copy

| | |
|---|---|
| **What this is** | The S147 working copy of ADR-043, kept by the `planner` for the decide track (`.claude/plans/PLAN-s147.md` § "D — the decide track", step 4). As each owner ruling lands, the matching fork below is rewritten as a decision, and the ruling is recorded word for word in the table under this header. The knowledge-base file stays the authority (status PROPOSED) until the Orchestrator copies this text back at O-10, after one review by both lenses (NR-5) |
| **Seeded from** | `docs/knowledge-base/decisions/ADR-043-overtime-flex-monthly-norm.md` at `e7478f24` (master HEAD, after O-1's four drift fixes). The text below the line marked "Verbatim from the knowledge base" is that file unchanged |
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

*(The decide track is complete on 2026-10-29: D-1 … D-12 answered (D-8 as D-8a and D-8b; D-5 told). Next: the planner writes the amended ADR-043 text from the rulings table and the one list; then one dual-lens review of the amended ADR; then ADR-043 ACCEPTED (O-10) and the S148 build input.)*

*(D-1 answered 2026-10-29: stages, each with its own evaluation-model marker. The interim limits were told with the question: ordered hours go to flex 1:1 with no supplement until stage 2; AC extra work is flex only until stage 3; reseed per stage; stage 1 moves every HK/PROSA demo payslip. D-1.4 "no supplement in stage 1" stays a recorded proposal and becomes ADR text with this ruling. Adjudication: `.claude/reviews/S147-D-batch1-adjudication.md` § D-1.)*

*(D-2 answered 2026-10-29: the rest-check half in S148 stage 0 (Backend compliance, no payroll marker; T7's notice retires); the supplement half in stage 2 (ADR-016 D10 amended; intervals in the digest and manifest); one source of time of day per hour. D-2.4 "manual hours retired" becomes ADR text with this ruling. D-3 answered the same day: refusal first (stage 0), the sourced three-column model in a later stage (stage 2 or later), SYSTEM_TARGET §C's travel line reworded to the sourced rule. D-6 answered the same day: a config stage of its own, before the first stage that needs a local value; D-6.5's stage-0 label stays a recorded proposal; QUAL-204 answered (a local profile changes pay). D-9 answered the same day after the owner asked for, and got, an expansion of why the lean departs from C5's quarter; D-9.4 "AC's month norm uses D1's function" becomes ADR text as a labelled default. Next asked: D-11, then D-10.)*

## Provisional items and recorded proposals (not owner rulings)

*What this list is, in plain words.* When the adjudicator reviewed the question briefs (the first six together, then D-10 on its own, then D-4, D-7 and D-8 together as batch 2, then D-5 and D-12 as batch 3), it settled some smaller points itself instead of turning each into one more owner question. Each is either a sensible default the owner is *told* about in the question's prose but not *asked*, or a point held for a domain expert or for the S148 build. None of them is an owner ruling, and none goes in the rulings table above. This is the one list for such items (D-9.4 asks for exactly one). The text of the ADR below does not change until the related question is answered.

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
14. **D-7.P1, office hours are a clock window per weekday.** The editor warns, but does not refuse, when the five windows do not add up to 37 h × fraction.
    - *Anchor:* § D-7 (D-7.4; D-7.2 item 3).
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

## Planner's notes: pending, not applied to the text below

1. **Citation drift since the ADR's verification sha `fdb52d48`.** The ADR's citations were correct at `fdb52d48`. Later S146 commits moved some of them. The text below keeps the old numbers verbatim. Spot-checked at `e7478f24`:
   - `SYSTEM_TARGET.md` (S146's dated notes, `19cf4bae`, added lines): `:243` → `:247` (the `NormPeriodWeeks` rule); `:163` → `:165` (§ G item 1); `:371` → `:377`; `:383` → `:391`; `:396` → `:408`.
   - `PeriodCalculationService.cs:2380-2388` (the marker comparison) → `:2758-2767`; `ReplayRefusedException.cs:38` → `:41` (S146 TASK-14605 grew the file). The ADR's other `PeriodCalculationService.cs` line numbers (`:2220`, `:3170`, `:3505`, `:3548`, `:3584`) were not re-checked and are probably stale too.
   - **Coordinator's ruling (2026-10-28):** re-checking every citation becomes an Orchestrator docs item before the ADR's dual-lens review. This list stays here as its input.
2. **Folds the briefs propose:** moved (WC-0.2) into the one list above, "Provisional items and recorded proposals (not owner rulings)". The D-1, D-2, D-9 and D-11 folds are now adjudicated recorded proposals (items 1–4); D-10's comparable-full-timer fold is one too since its own adjudication (item 7, `.claude/reviews/S147-D-10-adjudication.md`).
3. **Proposals in the D-5 and D-12 briefs:** moved (WC-0.6) into the one list above as items 27–45, after the batch-3 adjudication (`.claude/reviews/S147-D-batch3-adjudication.md`). D-5's proposed reframing was accepted in a stronger form: D-5 has no owner question at all, and the saved-days lean became item 36, a recorded design proposal. D-12 is asked as (c)/(a); (b) and (d) are not offered. (The D-4, D-7 and D-8 proposals this note held at WC-0.4 are items 8–26.) This note now holds nothing pending.

---

<!-- ===== Verbatim from the knowledge base (e7478f24) — do not edit above a ruling's rewrite without citing its table row ===== -->

# ADR-043 — Overtime and flex: a monthly norm, flex by default, overtime only for ordered hours

| Field | Value |
|-------|-------|
| **Status** | **PROPOSED** — drafted in S146 (track D5) to the owner's direction OQ-3 = (b). Acceptance belongs to S147, after its own model refinement and the owner's rulings on the forks below. Until then nothing here binds code. S145's Q2 = A, Q3 = F1 and Q7 (b) are "superseded in principle by the verified sources; in force in code until the model program replaces them; no new work on F1 machinery" (OQ-2 = (a), S146 refinement "Decisions recorded"). Reviewed by the Fable seat in S146 (R-40..R-46); the S147 model refinement is its review on both lenses before acceptance |
| **Sprint** | S146 (draft) → S147 (refinement, rulings, acceptance) |
| **Supersedes / amends (on acceptance)** | Supersedes the S145 owner rulings Q3 = F1 and Q7 (b); keeps Q2 = A only as the month norm's building block (D1). Amends ADR-016 D10 (the evaluation-model marker), ADR-024 D7 (from dormant design to the payroll path) and ADR-032 D7 (the "weekday public holidays consume" limitation) |
| **Closes when built** | QUAL-189; QUAL-216 … QUAL-222; QUAL-227; QUAL-223 (resolved into QUAL-227, F-7 and F-8); QUAL-226 through D1's single month-norm function. QUAL-215 is the root cause; its lesson is recorded below |
| **Domains** | Rule Engine, Payroll Integration, Backend, Data Model, SharedKernel, Frontend, Test |
| **Tags** | overtime, merarbejde, flex, norm-period, monthly-norm, public-holidays, supplements, compensation, ordered-work, forfeiture |
| **Evidence** | `docs/references/overtime-flex-model-research.md` (V1–V12) and `docs/references/agreement-cells-reverification-research.md` (C1–C7), both verified claim by claim; Part 1 of each governs. Code citations verified at `fdb52d48` |

## The decision, in one sentence

**An hour worked above the month's norm is a flex hour unless a leader ordered or accepted it. Only ordered or accepted
hours become overtime (HK, PROSA pay groups 1–2) or merarbejde (AC, PROSA groups 3–4), and those hours leave the flex
account. One hour is never both.**

## What is wrong today

StatsTid judges overtime week by week and pays it automatically. The state's own texts do neither. Seven things are
wrong, and they share one root cause (next section).

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

**Worked example.** An HK full-timer in June 2025 works 7.4 h every weekday except 2. pinsedag (Monday 9 June, a
holiday), and on Tuesday 17 June also 17:00–23:00 on a release the manager ordered. Worked: 20 × 7.40 + 6 = 154.00 h.

| | Today | Under this ADR |
|---|---|---|
| The month's norm | 21 weekdays × 7.40 = **155.40 h** (2. pinsedag not subtracted) | 20 × 7.40 = **148.00 h** |
| Flex for the month | 154.00 − 155.40 = **−1.40 h** (the ordered 6 h inside it) | (154.00 − 6) − 148.00 = **0.00 h** |
| Overtime | week 16–22 June: 43 h → **3 h at 1.5 + 3 h at 2.0** | **6 h at +50 %** in the overtime account: 9 h off, or paid, at the employer's choice |

The 6 evening hours earn a supplement either way (1.25 today for hours registered with clock times, i.e. through the legacy time-entry API — Skema-registered hours earn none today because their clock times never reach payroll, QUAL-234; +25 % here); the tables part ways at night, at weekends
and on 5 June.

## Why it happened — the part worth remembering

S145 asked the owner to rule on the norm period because the agreement sources were "silent". They were not. The
source-register rows behind the weekly rule cite 043-19 § 2, which is "Ansættelsesform" (civil servant or contract), and
§ 4, "Basisløn" (base pay) (V12; QUAL-215); the 37/40 h thresholds were never sourced ("pending (Phase B)").
*A wrong citation was read as an absence of evidence, and an owner ruling filled a gap that did not exist.* The rule
this leaves for the rebuild: every encoded cell names the verified quote it rests on (a V-n or C-n), and "the sources
are silent" is itself a claim to verify before the owner is asked to fill the silence.

## Decision (direction only, per OQ-3)

Each item says what the model does, the verdict it rests on and the register row it closes. *How* is S147's (Forks).

### D1 — The norm is a calendar month for HK and PROSA; weekday søgnehelligdage lower it
- 7.4 h × the month's working days, pro rata for part-timers (§ 5; PAV: 18 × 20/37 × 7.4 = 72 h), minus søgnehelligdage
  (public holidays that fall on a weekday) on a weekday the person would work (V1: *"fratrukket fridage samt eventuelle
  søgnehelligdage"*).
- Built from today's per-day amount, `DayNorm.ForWeekday` (`DayNorm.cs:44-45`), summed over the month's weekdays that
  are not norm-lowering days (D9). The block is a day, so split months keep working.
- The per-day precision rule of S145 Q2 = A stands: the month norm is the sum of `round(weekly × fraction ÷ 5, 2)` over
  the norm-bearing weekdays, so the Skema screen and the payslip agree day by day; the domain expert confirms the rounding.
- One month-norm function serves payroll and the screens, which today take their inputs from different places: one
  function with one input resolution — the month's OK version and agreement, the fraction from one dated read (QUAL-226).
- AC: never weekly (V12); its period is fork F-6 (C5). `NormPeriodWeeks ∈ {1, 2, 4, 8, 12}` (`SYSTEM_TARGET.md:243`)
  cannot express a month; the month becomes the HK/PROSA default, with a local other period possible (*"medmindre
  andet er aftalt"*; F-5).
- **Closes** QUAL-216; QUAL-222 with D9.

### D2 — Surplus hours go to flex
- An hour above the norm that no leader ordered or accepted is a flex hour, carried hour for hour (V6: PAV 19.5.6 and
  the AC remark to § 13). Saved flex not taken by period end is not overtime (V11).
- Flex settles once over the month's own days, as today (`WeeklyTimelineCore.cs:295-335`); its opening and closing ride
  the S146 ledger (D6). **Closes** QUAL-189 with D3.

### D3 — Overtime and merarbejde only for ordered or accepted hours, outside flex
- An hour becomes overtime (HK, PROSA groups 1–2) or merarbejde (AC; PROSA groups 3–4 when they meet the conditions, V5)
  only when a recorded decision covers it: ordered beforehand, or accepted afterwards as necessary (V3).
- Such an hour leaves the flex account; it counts in neither flex's worked hours nor its surplus (V8: PAV 19.5.11).
- The decision is an audited decision by a leader, reaching the pure engine as a frozen input (ADR-002, ADR-016 D10);
  whether it names hours or a budget is F-2.
- ADR-024 D7's design (prior approval, post-hoc necessity acknowledgement, audit table, events; ADR-024:205-230) is the
  starting point. Its plumbing is dormant (ADR-024:554), and payroll reads none of it today. The workflow is F-2.
- The sources' own exception: in PAV's office-hours model, ordered work inside office hours stays in flex (V8; F-4).
- **Closes** QUAL-217.

### D4 — HK and PROSA overtime is a flat +50 %
- +50 %, as time off or pay (V2: § 8 stk. 1). No 37/40 h thresholds and no 100 % tier (V4): `OvertimeThreshold50/100`
  and `OVERTIME_100` retire. The narrow daily ladders (HK Bilag 9, 023-19, 069-06) are not modelled unless needed.
- **Part-timers' band** (own norm up to full time): § 8 stk. 2 prints 1:1, but the 4 Feb 2026 award FV2025-795 binds
  the state employer back to 20 Jan 2000, and no implementing state text exists (C4). A dated, switchable rule; its
  default is F-7. Under D3 it matters only for ordered or accepted hours.
- **Exempt groups:** HK base pay ≥ 441,037 kr. (2012 level; § 4 special posts) is exempt from overtime *and* merarbejde
  pay (C3); PROSA groups 3–4 are on merarbejde (V5). How StatsTid knows is F-8.
- **Closes** QUAL-218; with F-7/F-8, QUAL-223 items (2) and (3).

### D5 — AC merarbejde is a claim decided by the employer
- The employee claims, the leader attests, the employer decides. Conditions (V9): ordered or a precondition for the
  job, and of larger extent over normally at least 4 weeks (§ 14 stk. 2); reported when it ends and at least quarterly
  (stk. 3). Compensation +50 %, as time off where possible (stk. 5), postponable up to a year by agreement (OK26 item
  21). For fuldmægtige (case officers) on the standby allowance, only extra work well beyond 20 h a quarter counts.
- An accepted claim's hours leave flex; exported months are not reopened (ADR-013, ADR-034); whether the hours move as
  a correction of the worked month or as a dated movement in the decision month is F-2's sub-question (lean: the
  decision month). **Closes** QUAL-220.

### D6 — Flex above the cap is forfeited by default, and the forfeit is recorded
- Forfeited at the cap test (V7: PAV 19.5.8, *"bortfalder som udgangspunkt"*); the automatic `FLEX_PAYOUT`
  (`FlexBalanceRule.cs:68-82`) retires.
- Never silent: the month's calculation records the hours forfeited, visible to the employee and HR (standing rule:
  make hidden state visible).
- Local exceptions are dated local parameters: a manager's exception, transfer to the overtime account, 1:1 cash by
  agreement (V7, C6; F-5).
- The S146 ledger's rails (`opening_balance`, `closing_balance`, `balance_provenance` on `payroll_export_records`,
  `init.sql:4633-4640`) carry the new closing unchanged in shape. Status at the S146 close (`b510e45b`): the ledger is built (TASK-14605, `FlexLedgerOpening.Derive`); S147 TASK-14700 adds the full chain walk (QUAL-229). (Superseded status line, kept for the record: "at `fdb52d48`: the columns are merged; TASK-14604 is in progress; TASK-14605's DeriveOpening (refinement C2) has not landed.") **Closes** QUAL-219.

### D7 — One +25 % supplement; § 14's compensation outside the hours count
- HK/PROSA: one +25 % supplement for work 17:00–06:00, at weekends, on søgnehelligdage, on Grundlovsdag after 12:00
  and on 24 December after 14:00 (C2: § 13), paid regardless of overtime. It is due only for ordered or rostered work,
  and inside flex time only if the placement was ordered or necessary, so F-2's marking must be able to record an
  ordered or rostered placement independently of an ordered surplus hour; the supplement reads the placement fact, D3
  reads the surplus fact.
- Ordered weekend and søgnehelligdag work for day workers: +50 % as time off or pay (employer's choice), kept out of
  the month's hours count, the +25 % on top, no overtime on top (C2: § 14 stk. 1–3).
- No 1.50 night rate and no 2.0 Sunday or holiday rate for general HK/PROSA (only HK Bilag 9's narrow group has 100 %).
  AC stays without supplements (general profile; AC Bilag 6's area payments out of scope).
- **Closes** QUAL-227 and QUAL-223 item (1).

### D8 — The employer chooses time off or pay; overtime lives in its own account
- HK, PROSA and AC: the employer chooses time off, pay or a mix (V10, C7: PAV 19.2.7.1). Time off is due within 3 months
  of the norm period's end; after that, pay at the next payday unless the employee chooses saved days (§ 10–11; OK26
  floor 111 h). AC: time off where possible (§ 14 stk. 5). The *opsparing af frihed* election existed only 1 May 2025 –
  31 Mar 2026 (024-26) and is a dated rule if ever modelled.
- `employee_compensation_choice = TRUE` for HK/PROSA (seeds above) retires; PROSA's "1:1" becomes +50 %.
- Overtime hours wait in an account separate from flex (`SYSTEM_TARGET.md:396`). `overtime_balances` exists
  (`init.sql:1992-2008`) and has never been fed (`AdjustAccumulatedAsync`, `OvertimeBalanceRepository.cs:50`, has no
  production caller); its shape is F-3. **Closes** QUAL-221.

### D9 — One holiday calendar, split by what each day does
- Three kinds of day: (1) norm-lowering søgnehelligdage, the closed list of ten, skærtorsdag … nytårsdag (022-26,
  remark to § 4 stk. 2); (2) timed supplement windows, Grundlovsdag from 12:00 and 24 December from 14:00; (3) local
  customary days off (5 Jun, 24 Dec, 31 Dec, 1 May), per institution, dated and audited (F-9). Grundlovsdag,
  24 December, 31 December and 1 May are not holidays (C1).
- Keyed by date, not OK version: Store Bededag ended by law on 1 Jan 2024 (Act 214 § 5); today's OK-version key
  (`DanishPublicHolidays.cs:54-58`) is right by coincidence (C1).
- One authority. Today three lists have separate readers: the C# list (`DanishPublicHolidays.cs:36-62`) read by the
  supplement rule (`SupplementRule.cs:56`); the `danish_public_holidays` table (`init.sql:165-170`, rows `:358-400`)
  read only by the send-month check (`ApprovalEndpoints.cs:2110`); and the demo generator's own list
  (`tools/StatsTid.DemoSeed/Generation/DanishHolidays.cs`).
- **Order (C1's trap; fixed, not a question):** the split lands *before* the norm subtracts holidays. Otherwise every
  June with a weekday 5 June loses 7.4 h: June 2026 (5 June a Friday) would fall from 162.80 to 155.40.
- A vacation day booked on a søgnehelligdag would consume nothing, ending ADR-032 D7's accepted limitation
  (ADR-032:47). **Closes** QUAL-222 (with D1) and QUAL-227's calendar half.

## What it retires, and what survives

| Retires on acceptance (built in S147+) | Where it lives today |
|---|---|
| Weekly overtime and its tiers | `WeeklyTimelineCore.cs:152-193`; `OvertimeRule.cs:33-95` |
| F1's settlement-day machinery | `WeeklyContext.cs:42` (`SettlementDay`), `:57` (`SettledWeeks`), `:91` (`MidWeekAgreementChange`) |
| Context days; the prior-month approval wait | `PeriodCalculationService.cs:3170` (`EmployedContextDays`), `:3548`, `:3584` |
| Do 7: the export rechecks the previous month under `FOR SHARE` | `PayrollExportService.cs:334-342` |
| Q7 (b): a correction names M+1 | `RetroactiveCorrectionRequested.cs:56-63` |
| HRP-001b: the `PRIOR_MONTH_REOPENED` worklist row | `HrBackdateWorklistRepository.cs:80`; `ApprovalEndpoints.cs:1801`; HRP register `:98` |
| QUAL-209's mid-week refusal | `PeriodCalculationService.cs:3505`; `PeriodNotCalculableException.cs:46` |
| Frozen QUAL-209, -210, -212, -213 (closed as obsolete) | `quality-finding-register.md:433`, `:434`, `:436`, `:437` |
| Automatic `FLEX_PAYOUT` | `FlexBalanceRule.cs:44-48`, `:68-82` |
| The holiday 2.0 branch; evening, night and weekend rates | `SupplementRule.cs:54-117`; `AgreementRuleConfig.cs:40` |
| The employee's compensation choice (HK/PROSA) | `init.sql:1468`, `:1474`, `:1480`, `:1486` |

| Survives | Why |
|---|---|
| The per-day amount (`DayNorm.cs:44-52`) | The month norm's building block (D1); only the "holidays do NOT reduce the norm" sentence (`:28-29`) goes |
| Flex settled once over the month's own days (`WeeklyTimelineCore.cs:295-335`) | Already the monthly shape D2 needs |
| Payroll reading its own hours; split months paid part by part (QUAL-149/150) | Independent of the weekly basis |
| The frozen-input replay proof and its marker contract (ADR-016 D10; `PeriodCalculationService.cs:2220`, `:2380-2388`) | Decisions and movements become frozen inputs, so replay keeps proving them |
| The S146 flex ledger (`init.sql:4633-4640`, with TASK-14605's chain) | Its rails carry the new closings (D6) |
| ADR-024 D7's design and dormant plumbing (`init.sql:2027-2038`, `:2551-2570`) | The starting point for D3 and F-2 |
| The pay / time-off overtime wage types (`init.sql:2047-2050`) | D8 needs this split; only the 100 % rows retire |

## Consequences

- **Reseed, don't migrate.** There is no real data; the database is reseeded and no export record, manifest or balance
  is migrated. *A deployed system would need a dated switch by pay period and a plan for correcting months paid under
  the old model; this ADR would then be far more expensive.*
- **The evaluation-model marker changes.** `EvaluationModel = "weekly-timeline-v1"` (`WeeklyTimelineCore.cs:134`) names
  the arithmetic of every rule endpoint payroll calls (`:126-133`). The rebuild changes it once, or once per stage under
  F-1 (b). The new core is not weekly, so it gets a new name, not "v2".
- **Replaying an older manifest is refused by name.** The replay compares markers first and refuses with
  `replay-evaluation-model-changed` (`PeriodCalculationService.cs:2380-2388`; `ReplayRefusedException.cs:38`). The old
  manifest stays readable as the record of what was paid; it cannot be re-proved under arithmetic that did not produce
  it, which is the honest answer. After the reseed such manifests exist only in developer databases.
- **Not an OK-version transition.** The old encoding was wrong for every period, so the new model applies to OK24 and
  OK26 periods alike. Rules that change on a date stay dated (Store Bededag, the *opsparing* window, F-7's part-timer
  rule). §§ 4 and 8 are word for word the same in the circular in force through OK24 (CIR1H 9551 of 2021; the dossier's
  S1′), but § 13/§ 14 were verified in 022-26 (OK26) only, so
  S147 verifies the OK24 texts before encoding supplements for OK24 periods.
- **Payroll boundary.** StatsTid exports hours with wage types, never kroner; the pay-ceiling exemption is held as a
  classification, not as pay (F-8).
- **Tests asserting today's figures change.** A grep at `fdb52d48` for the retired identifiers (`OvertimeRule`,
  `WeeklyTimelineCore`, `WeeklyContext`, `FLEX_PAYOUT`, `OVERTIME_50/100`, `MERARBEJDE`, `HOLIDAY_SUPPLEMENT`,
  `weekly-timeline-v1`, the F1 refusal and worklist names) finds 46 files under `tests/` and `tools/` — an upper bound
  that S147's census settles. They include S145's September example (`WeeklyTimelineCore.cs:85-87`) and the demo's June
  deficits.
- **New hand-offs for leaders and HR.** Ordering and accepting hours, deciding AC claims and the 3-month time-off
  deadline each get an HRP register row when designed; HRP-001b retires with F1.
- **Documents owed at acceptance** (the Orchestrator's; `SYSTEM_TARGET.md` also needs the owner's approval):
  `SYSTEM_TARGET.md` § L (`:371` "currently calculates overtime correctly", `:374`, `:383` "Unapproved overtime must
  still be calculable … flagged in export"), § M (`:393`, the employee's choice), `:163` and `:243` (the norm period);
  `danish-agreements.md` `:48-60`, `:62-69`, `:71-92`, `:94-111`; ADR-024's implementation status; ADR-016 D10's
  marker; ADR-032 D7 (`:47`); the source-register rows (QUAL-215).

## Alternatives not chosen

**(a) Keep automatic overtime, and net it out of flex.** Every hour above the norm is still paid as overtime, and those
hours are subtracted from flex. That fixes the double count but not the trigger: it still pays +50 % for hours nobody
ordered, where the sources make "ordered or necessary" a precondition (V3) and say surplus flex hours *"udløser ikke
overarbejdsbetaling"* (V6). On the weekly basis it also needs flex settled net of a week judged after the month has
closed (QUAL-189's S145 addendum). It compromises domain correctness: not a valid path.

**(c) Keep both counts.** Today's behaviour: the hour is paid as overtime and stays in flex. The employee is compensated
twice for one hour; the sources keep ordered work outside the flex account (V8), and the spec keeps the overtime
balance separate from flex (`SYSTEM_TARGET.md:396`). Not a valid path.

*(Keeping the weekly basis with corrected rates was not a candidate: V1 and V12 remove its premise.)*

## Forks for S147 — owner questions, one at a time

S147's refinement asks these one at a time, in this order; each later fork assumes the earlier answers. Leans are the
drafter's, not decisions.

**F-1 — Order of work: one cut-over, or stages?** *Fixed, not asked:* D9's split lands before the norm subtracts holidays.
- (a) One cut-over behind one marker change — no interim state; but one very large change, waiting on its slowest
  part (AC claims).
- (b) Coherent stages, each changing the marker: 1 = calendar split, monthly norm, flex by default, recorded
  forfeiture; 2 = marking workflow, overtime account, supplements (they read F-2's placement fact); 3 = AC claims. Reviewable
  steps; but between 1 and 2 no hour can be overtime, so ordered work lands in flex at 1:1.
- (c) Stages that keep today's weekly overtime until stage 2 — keeps the double count alive (alternative (c)); not valid.
- **Lean (b).** Nothing is deployed, so the interim underpays no one; every stage is correct for unordered hours, which
  are most hours, and the sprint log names the interim limit.

**F-2 — The marking workflow: who marks which hours, and when?** Today a leader can create, approve and reject a
pre-approval (`OvertimeEndpoints.cs:147`, `:359`, `:439`), but it is an hours budget for a period (`period_start`,
`period_end`, `max_hours`; `init.sql:2027-2038`) that names no hours. Only those endpoints read it; the advisory
governance check takes a caller-supplied `hasPreApproval` flag instead (`:65`, `:122`). D7's post-hoc acknowledgement names entries only in its event
(`OvertimeNecessityAcknowledged.cs:23`), which nothing emits.
- (a) Per hour, afterwards: the employee marks hours ordered/necessary; the leader accepts or rejects before the month
  is approved. Precise, and a per-hour mark can also carry D7's placement fact; but a step per overtime episode.
- (b) A budget beforehand that hours above the norm consume. Light; but which hours consumed it is a guess, so neither
  the supplement nor the 3-month clock can be tied to hours.
- (c) Both: an order beforehand and a necessity acceptance afterwards, each naming its hours. Matches "beordret eller
  nødvendigt" exactly; the most to build.
- **Lean (c).** The audit events exist (`OvertimePreApprovalCreated/Approved/Rejected`, `OvertimeNecessityAcknowledged`),
  and the leader works from the approval page (ADR-024 D7 point 3). Sub-questions: the cut-off (lean: before month
  approval); an acceptance after export, and an accepted AC claim (D5) — a correction of the worked month or a dated
  movement in the decision month (lean: the decision month); recording an ordered or rostered placement (D7's
  supplement condition) independently of an ordered surplus hour.

**F-3 — The overtime account: where accepted hours wait to be compensated.**
- (a) Feed `overtime_balances` as it is (one row per employee and year). Exists; but a yearly total cannot age hours
  against the 3-month deadline.
- (b) A movement ledger: earned (month, hours, rate), taken as time off, paid, converted to saved days; each dated and
  audited, the balance derived. Ages, audits and replays; a new table and events.
- (c) Rails on `payroll_export_records`, like flex. Does not fit: compensation is decided after the month is exported.
- **Lean (b).**

**F-4 — PAV's office-hours (kontortid) model.**
- (a) Not modelled: every ordered or accepted hour leaves flex (PAV 19.5.11's general rule). Simple; but it pays +50 %
  for ordered work inside office hours at institutions on PAV's office-hours model.
- (b) An opt-in local parameter: per-weekday office hours. An hour then leaves flex only if it was ordered or necessary,
  fell outside office hours, and came after the first 7 h 24 min of that day (V8; DayNorm's per-day amount).
- **Lean (b), default off** — it is C6's fikstid/kontortid parameter.

**F-5 — The local flex parameters, and where they live.** C6's nine: settlement and cap-test periods; plus and minus
caps (pro rata, per subgroup); a daily credit cap; what happens above the cap (forfeit at period or year end, a
manager's exception, transfer to the overtime account, 1:1 cash); what happens at leaving; fikstid/kontortid; excluded
groups; fuldmægtige's standby interaction and suspension in peak periods.
- (a) Typed columns on `local_agreement_profiles`: organisation × agreement × OK version, dated, audited, and already
  holding the flex cap and carry-over (`init.sql:856-872`; audit `:885-895`). Typed, and resolved by `ConfigResolutionService` for the
  screens and booking only — not by the Rule Engine, so not into pay (QUAL-204, QUAL-233; D-6 decides whether payroll reads it); and per-subgroup caps do not fit one row per agreement.
- (b) `local_configurations` (key + JSON, area `FLEX_RULES`; `init.sql:803-822`). Flexible; untyped, validated only in code.
- (c) A new table — a third home for local rules.
- **Lean (a)**, per-subgroup caps as an S147 sub-question. Without a local agreement the central defaults apply
  (monthly settlement, forfeiture). QUAL-225 (an explicit opening balance) is revisited here.
- **Sub-question (S146 Step 7a, Fable N-6; R-61c):** the leaver's last closing. (a) Settled in the leaving month per the local parameter (payout, forfeiture or transfer to the overtime account), recorded as a dated movement; or (b) carried to a rehire. Today a rehire never inherits (R-14 case 4c). Asked with QUAL-219 and QUAL-225.

**F-6 — AC's period.**
- (a) Flex monthly; merarbejde without a fixed period — each claim states its own (normally ≥ 4 weeks), reported at
  least quarterly. Follows the agreement, which deliberately sets none (C5); "larger extent" stays the employer's call.
- (b) A configured period: quarterly for administrative AC, annual for special consultants. Rests on union guidance
  (MEDIUM), not the agreement.
- (c) Per local agreement.
- **Lean (a).** The annual-norm group keeps its model (`AC_RESEARCH`, `ANNUAL_ACTIVITY`; `init.sql:1492`, `:1498`).

**F-7 — The part-timer default until the state text exists (needs legal input).**
- (a) The printed rule (1:1 up to full time) — knowingly applies a rule an award found to breach the part-time directive.
- (b) The award (+50 % from the part-timer's own norm) — may differ from the implementing text when it arrives.
- (c) A dated, switchable rule: a default now, switched on the implementing text's date.
- **Lean (c) for the shape.** The default *value* is the one answer the sources cannot give.

**F-8 — The HK pay ceiling and PROSA groups 3–4.** StatsTid holds no pay (`employee_profiles` has a part-time
fraction, position and category; `db-schema.md:258-270`), and pay belongs to the payroll system.
- (a) Import base pay and compare with 441,037 kr. — crosses the payroll boundary and needs indexation.
- (b) A dated employee attribute, the overtime regime (overtime / merarbejde / exempt), set by HR under ADR-040's as-of
  rule and audited.
- (c) Derived from position configuration — positions do not map one-to-one to pay groups.
- **Lean (b).** PROSA groups 3–4 who do not meet § 8 stk. 3 literally fall back to overtime (C3) — a domain-expert
  question; under (b) either answer is a value.

**F-9 — Local customary days off (5 Jun, 24 Dec, 31 Dec, 1 May).** Local, negotiated, withdrawn and sometimes
reinstated (FV2016.0107; SKAT 2025).
- (a) A paid day off, credited like a norm-credit absence. Keeps the agreement's norm intact and the grant visible;
  needs a rule for an absence booked the same day.
- (b) A closure day: the employee uses flex or leave.
- (c) Both, chosen per institution and day.
- **Lean (c)**, stored per institution with dates and an audit trail beside F-5's parameters; partial days in hours.

## What this ADR does not decide

The mechanisms (the forks); the narrow daily overtime ladders; shift workers (chapter 4, CO II's evening/night
agreement); AC Bilag 6's area payments; the 48-hour weekly average (Bilag 1 § 5), a ceiling rather than an overtime
trigger; an explicit opening flex balance (QUAL-225, backlog, revisited under F-5); storing contracted weekly hours
instead of a rounded fraction; and fixed days off for part-timers (the V1 exception): not modelled while the fraction
is spread over five weekdays; a domain-expert question for the local parameters (F-5).

## References

- Dossiers: `docs/references/overtime-flex-model-research.md` (V1–V12); `docs/references/agreement-cells-reverification-research.md`
  (C1–C7, and its § 5 code-impact table).
- Refinement: `.claude/refinements/REFINEMENT-s146-payroll-correctness.md` rev 3.2 ("Decisions recorded"; "Forks left
  to the S147 model refinement"). Owner rulings 2026-10-23: OQ-1 = (iii), OQ-2 = (a), OQ-3 = (b).
- ADRs: ADR-002 (pure engine), ADR-013 (no-cascade corrections), ADR-016 D10 (manifest and replay), ADR-024 D7
  (overtime authorization), ADR-032 D7, ADR-034 (export lock, corrections-only), ADR-040 (as-of rule).
- Registers: `docs/operations/quality-finding-register.md` QUAL-189 (`:413`), QUAL-215 … QUAL-227 (`:439-451`);
  `docs/operations/hr-follow-up-process-register.md` HRP-001b (`:98`).

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

<!-- ===== End of the verbatim knowledge-base text ===== -->

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
