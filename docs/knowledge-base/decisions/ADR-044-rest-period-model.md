# ADR-044 — The rest model: every work end is judged on the 24 hours before it

| Field | Value |
|-------|-------|
| **Status** | **accepted** — owner-ruled 2026-11-13 (S148 owner Q2, "S148, after wave 3 (Recommended)", which also approved the spec wording at `SYSTEM_TARGET.md:272-284`); built in S148 wave 3b (merged `2810ebeb`, 2026-11-16; CI-verified at CI-W3b, run `43851473962`). Written down at S148's close (2026-11-25); it records rulings already made and decides nothing new |
| **Sprint** | S148 |
| **Supersedes / amends** | **Supersedes, for the daily rest check,** ADR-039 D5 (rest measured between start-day stints) and D5b's one-day boundary fetch, and the wave-3 interim "a rest is judged in the month in which it ends". **Amends** ADR-043 D10 (its rest-check half, as built) and D11 (one rest flag per time type is not enough). **Closes** QUAL-253. **Leaves open, registered:** QUAL-254, QUAL-255, QUAL-256, QUAL-277 |
| **Domains** | Rule Engine, Backend, Frontend, Security, Test |
| **Tags** | rest-period, daily-rest, 11-hour-rule, 24-hour-look-back, 13-hour-span, derogation, weekly-rest, elapsed-time, time-zone, dst, compliance, manager-visibility, arbejdsmiljøloven |
| **Evidence** | `docs/references/rest-period-24h-research.md` (Part 1, the verification, governs: R1–R5, V-1 … V-78, corrections 1–19); the adjudication `.claude/reviews/S148-RESEARCH-rest-24h-adjudication.md` (RR-1 … RR-8, Fable 5.2); the wave-3b brief adjudication `.claude/reviews/S148-wave3b-briefs-adjudication.md` (items 1, 5–9, 16, 17); the wave-3b Step 5a adjudication `.claude/reviews/S148-WAVE3B-5a-adjudication-c1.md` (ruling 1); the TASK-14813 Step 5a adjudication `.claude/reviews/S148-TASK-14813-5a-adjudication-c1.md` (rulings 1–3). Code cited at `bda18b03` |

*How to read the labels.* **R1–R5** are the research questions, **V-n** the verification's claim rows and **Corr. n** its numbered corrections, all in the dossier. **RR-n** are the research adjudicator's rulings. **(a)–(f)** are the questions the sources leave silent (RR-6). **QUAL-n / SEC-n** are rows of the quality and security registers.

## The decision, in one sentence

**At the end of every piece of work, StatsTid looks back over the 24 elapsed hours before it, in Danish time; if the longest unbroken rest in that window is shorter than the agreement's minimum (11 hours), the day the work ends gets a finding.**

## What was wrong, in plain words

Until S148 the check grouped work by the day it started and measured only the gap between one day's work and the next. So it never noticed three real breaches:
- one shift over 13 hours (a 24-hour shift showed nothing at all);
- a day shift followed by a night shift on the same day;
- a split day longer than 13 hours from first start to last end, such as 08–12 plus 17–22.

When it did report, the number could be wrong. Tuesday 22:00–23:59, then Wednesday 06:00–14:00, showed "6.0 hours, Tuesday". The law's test gives "8.0 hours, Wednesday": the longest rest in the 24 hours before Wednesday 14:00 is 14:00–22:00 on Tuesday.

The model dates from S132. It became visible on real data in S148 wave 3, when the check started reading the clock times typed in Skema (TASK-14813). Both review lenses found the gap (Codex W-2, Fable N-1). The owner chose to register it and have the rule researched first (owner Q1, 2026-11-13, "No, register only"), then to build the fix in S148 (owner Q2).

## Why this rule: the law, verified

- Arbejdsmiljøloven § 50 and BEK nr. 606 af 29/06/2026 § 3 require "at least 11 consecutive hours of rest within each period of 24 hours", and neither says where the period starts.
- It is not the calendar day (the European Commission's 2023 Interpretative Communication), and since 29 December 1996 it is not "24 hours from the start of normal working hours" either: Parliament removed that anchor on purpose (LOV 1196/1996; the L 59 remarks).
- Arbejdstilsynet, which enforces the rule, gives one test, in the same words in 1997, 2016 and 2024 (MED 3503, VEJ 9576, VEJ 9666 § 2): from a point inside working time, count 24 hours back; that stretch must hold 11 unbroken hours of rest.
- Two elements are inferences, not quotations: the test applies at every point of working time, and only the part of a rest inside the window counts. Every reading without them passes a 24-hour shift, which the appeal board called a significant breach (KEN 9938/2006) and the Commission forbids ("no more than 13 consecutive hours"). So the sources support them by elimination (V-14, V-15).
- In practice: **no work may end more than 13 hours after the last full 11-hour rest ended** (V-75). "13 hours on, 11 off" is lawful; 13 hours and one minute is not.

## Decisions

### D1 — The rule: every work end, judged on the 24 hours before it (RR-1)
- **The ends judged.** A stint is a run of intervals joined across midnight; each stint's latest end is judged. Judging at the ends is enough, because the longest rest in the window only shrinks while work goes on (V-74).
- **The value.** The longest single unbroken rest inside the 24 elapsed hours before the end. A rest that began before the window counts from the window's start. Short rests never add up (the order allows summing only under home-work derogations, BEK 606 § 33 stk. 4). Exactly the minimum passes ("mindst 11").
- **The 13 hours is derived,** 24 minus `MinimumRestHours`, not a setting of its own. The check does not read `MaxDailyHours`; that separate hours check stays as a backstop for hours that are not on the clock.
- **One finding per day.** Among a day's failing ends, the lowest value; equal values name the earliest end.
- **The minimum of the end's own date.** When the minimum changes on a Monday, a Sunday end is judged by Sunday's value and a Monday end by Monday's, although their windows overlap (ADR-043 D12; RR-7; TASK-14829).
- Code: `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RestPeriodRule.cs:354-458` (`CheckDailyRest` and its remark), `:460-484` (`LongestRestHours`), `:76` (the 24-hour window).

### D2 — The true value decides; the shown value is rounded down (wave-3b brief adjudication, item 9)
- Both thresholds (the minimum, and the 8-hour floor of D5) compare the true value. The shown value is rounded down to one decimal: `Math.Floor(v × 10) / 10` on the server (`RestPeriodRule.cs:434`), and on the screen `restDisplayHours` (`frontend/src/hooks/useSkema.ts:1051-1053`).
- *Why:* rounding half up would show "11.0, minimum 11.0" as a breach, and "8.0" as both a warning and a violation. Rounding down makes the figure agree with the verdict at every threshold that is a multiple of 0.1.
- *Given up:* the figure can understate a rest by up to six minutes; 8 h 27 min shows 8,4. This supersedes the half-up direction of TASK-14814's Step 5a ruling 1.
- The finding keeps its shape (`DAILY_REST`). Its message names the judged end, in the final Danish copy (item 17; `RestPeriodRule.cs:435-441`).

### D3 — Dating, the month and the read window (RR-1; wave-3b Step 5a ruling 1)
- **A finding is dated the calendar day its work ends,** and belongs to that day's month. This replaces wave 3's interim, "a rest is judged in the month in which it ends", with its finding dated the earlier day.
- **The read starts two days before the month** (`monthStart − 2`; `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs:188`). A work end on the 1st looks back 24 elapsed hours, and the day before the 1st can be 23 hours long (the March clock change; 1 April 2030 is the next case). No window reaches further, so nothing else is needed. Skema serves the same two previous days to the screen (`src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:558-591`).
- **The month's last midnight.** The engine owns the cut: it builds the stints without the intervals dated after the period (`RestPeriodRule.cs:171`), so a night shift over the month's last midnight is judged as ending at 24:00 on the last day, and again in full by the next month. A chain already over the limit before a month edge can therefore show in both months, each with its own true value.

### D4 — Elapsed hours in Europe/Copenhagen, with the zone handed in (RR-1; wave-3b brief adjudication, items 5, 6, 8)
- All arithmetic is in **elapsed time** in Europe/Copenhagen, never a wall-clock difference and never the host's or the browser's own zone. The clock-change døgn are 23 and 25 hours long (PAV 19.9).
- **The zone is a parameter.** The engine host checks at boot that it can resolve the zone and passes `CopenhagenBusinessDate.Zone` into the rule (`src/RuleEngine/StatsTid.RuleEngine.Api/Program.cs:11`, `:174-176`). `Evaluate` refuses a null zone once an interval is kept (`RestPeriodRule.cs:130-155`), and nothing under `Rules/` reads a zone (`git grep -n -e CopenhagenBusinessDate -e FindSystemTimeZoneById -- src/RuleEngine/StatsTid.RuleEngine.Api/Rules` is empty). This keeps the engine pure: external data arrives as a parameter, never fetched (ADR-002).
- **Where the wall clock is not one-to-one, the defaults err toward reporting** (the sources say only that the rule is in hours): a time in October's repeated hour is the earlier instant for a start and the later one for an end; a time in March's missing hour is the changeover instant, so an interval wholly inside that hour has no length and breaks no rest, but its end is still judged; an end typed 23:59 is the end of the day, because Skema cannot hold 24:00 and the midnight join already counts that minute as work (`RestPeriodRule.cs:486-538`, `ToInstant` and `ChangeoverInstant`).
- **These instants are never stored.** They are built from the typed clock times inside one calculation and discarded. What is stored and shown is a business date, the Copenhagen day (ADR-041). ADR-041's "instants stay UTC" governs stored instants and is untouched.
- **The browser** computes the same way, in Europe/Copenhagen. A browser without Danish time-zone data shows "Hviletiden kan ikke kontrolleres i denne browser, fordi tidszonedata for Danmark mangler." instead of warnings, rather than looking compliant (`frontend/src/components/SkemaDayPanel.tsx:304`).

### D5 — The derogation covers no rest below 8 hours (RR-2)
- Where the agreement allows a derogation (`RestPeriodDerogationAllowed`; HK and PROSA), a short rest of **8.0 hours or more is a WARNING, and one below 8 hours a VIOLATION** (`RestPeriodRule.cs:73`, `DerogationFloorHours`; `:428-430`). The 8 is the order's number (BEK 606 § 19; Statens arbejdstidsaftale § 12, identical in OK21 and OK26), not an agreement setting. The AC agreement has no rest clause and no flag.
- The warning says what StatsTid does not check: a cut is allowed at most once per 7 days (twice by local agreement), and only with equal compensatory rest straight after (`RestPeriodRule.cs:82-83`).

### D6 — What counts as work, and as rest
- **Work** is every interval the employee typed in Skema (`work_time_projection`), the only source of time of day since S148 (ADR-043 D10). Overlapping or touching intervals are one block. The hours checks (maximum daily hours, the 48-hour average) still sum the project entries.
- **Unregistered time is rest,** the directive's own definition (Art. 2(2)); this defaults the silent question (f), what came before the first record (RR-6). A day whose project hours are not all on the clock is listed as "not checked" (R-22; `RestPeriodRule.cs:284`, `FindRestNotCheckedDays`), so it never looks falsely compliant. The list covers the period's own days only (QUAL-277).
- **Voluntary unsocial hours.** A wholly voluntary stint is neither judged nor counted as work in another end's window (`SYSTEM_TARGET.md:300`). **A stint cut at the read edge,** whose first interval starts at exactly 00:00 on a day before the period, is never voluntary, because its beginning is unknown; the rule errs toward reporting (`RestPeriodRule.cs:733`; the TASK-14813 Step 5a ruling 3; cross-referenced from QUAL-255). Nothing writes the voluntary flag today, so neither rule is observable yet.
- **On-call** cannot be registered today (ADR-043 D11), so no interval carries it. When it can, one rest flag per time type is not enough: on-call at home is daily rest but never weekly rest (ADR-043 D11's note; Open, item 4).

### D7 — A manager sees nothing of an unsent month, at the month edge too (TASK-14813 Step 5a ruling 1; wave-3b item 1; SEC-059)
- A leader-tier read uses the two days before the month only when the previous month is submitted, decided by the month gate's own predicate (`ApprovalVisibility.IsSubmittedToManager`; `ComplianceEndpoints.cs:243-249`); Skema serves boundary rows to a leader on the same condition (`SkemaEndpoints.cs:571-586`). The employee and HR-or-above get them always.
- Until then the month's first day is listed as "not checked", when the day before was an employed day and the 1st has a clock interval (`ComplianceEndpoints.cs:280-282`). A finding still shown for a work end on the 1st is built from the sent month alone, so it is true, possibly understated. Once both months are sent, the full finding appears: the answer is computed at each read and never stored.
- Residuals (SEC-059): a leader's time-entry read still starts two days back; on a withheld read a night shift begun before the month is read from 00:00 on the 1st. See `docs/SECURITY.md` and RES-002.

### D8 — One rule in two places, proved equal (ADR-043 D10)
- The server's check and Skema's instant warning implement the same rule (`analyzeRestPeriods`, `frontend/src/hooks/useSkema.ts:979`).
- One shared table of 33 scenarios, schema 2 (`tests/StatsTid.Tests.Unit/TestData/rest-parity-scenarios.json`), is read by both test runners (`tests/StatsTid.Tests.Unit/Rules/RestParityTableTests.cs`; `frontend/src/hooks/__tests__/restParity.test.ts`). Each side checks every finding's date and value, so a row cannot pass on one side only.
- The screen marks a finding on the days whose typing opened it (wave-3b item 7, rule (b′)), so one finding can show on two or three days.

## Open — not decided here

Each item is registered or recorded; none blocks the rule above.
1. **How a permitted cut is used (QUAL-254, Medium).** The derogation is one yes/no flag. StatsTid counts no uses per 7 days, does not apply "not two døgn in a row", checks no compensatory rest (equal to the cut and taken before the next work, Jaeger para. 97), records no decision, and models no postponement (BEK 606 § 16 stk. 3, § 19; CIR 9807/1983). Blocked on expert questions (a), (b), (d); needs an HRP row when designed.
2. **The per-person exemption (QUAL-255, Medium).** BEK 606 § 6 exempts staff who set their own hours or manage, assessed for each person, never for a whole group (PAV 19.8). StatsTid checks everyone, so exempt AC staff and managers can get findings, which errs toward reporting. The shape is a dated HR fact with the assessment recorded, never an agreement-level switch.
3. **The weekly rest day (QUAL-256, High).** `CheckWeeklyRest` still counts start days in fixed 7-day blocks from the month's first day (`RestPeriodRule.cs:560-616`). It misses multi-day chains and weeks across a month edge, and flags a week that holds a 40-hour rest. The law's test is 35 unbroken hours, at most 6 døgn apart by statute and 10 working days under the state agreement § 12 stk. 3, and on-call never counts toward it. Blocked on expert questions (a), (e); the read would need about two weeks back.
4. **On-call's two rest flags** (ADR-043 D11, note of 2026-11-25; provisional item 13).
5. **Postponement.** CIR 9807/1983 (central administration, signed by AC) allows the rest to be postponed, up to 22 hours of work in a row; whether it still works under BEK 606 is expert question (c).
6. **The boundary day's clocking (QUAL-277).** A work end on the 1st looks back into the previous month's last day, but the "not checked" list covers the period's own days only.
7. **Several employments.** Daily rest applies to a worker's several contracts with one employer taken together (CJEU C-585/19); StatsTid models one employment per person.
8. **The 48-hour average is outside this decision.** It ignores its configured reference period (QUAL-123), and its shown value still rounds half up against a maximum, so 48.04 reads "48.0" as a breach (recorded below the quality register's floor; `RestPeriodRule.cs:623-654`).

**The silent questions** (RR-6), for the expert hand-off package (`docs/references/phase-b-handoff-package.md`): (a) the 7-døgn anchor, and whether it rolls; (b) what "døgn" means in "not 2 døgn in a row"; (c) whether CIR 9807 still works under BEK 606, and how its "arbejdsdøgn" maps onto the look-back; (d) whether compensatory hours come on top of the next rest or inside it ("on top", the stricter reading, if built before an answer); (e) how a night shift counts toward § 12 stk. 3's "arbejdsdage". Question (f) is defaulted in D6.

## Consequences

- **Numbers and dates on screen moved** (told to the owner): the shown rest is the longest rest in the window, not always the gap between two shifts; it is dated the day the work ends; it is rounded down.
- **A fully clocked day over 13 hours gets two findings,** the daily-rest one and the maximum-daily-hours one; merging them was declined.
- **Pay does not change.** Payroll never runs the rest rule (ADR-039 D5's rationale of record); PIN-1 stayed green at CI-W3b.
- **Exempt staff can get findings** until QUAL-255 lands, deliberately: the check errs toward reporting.
- **The weekly check's errors stay** until QUAL-256.

## Alternatives not chosen

- **The chain span as the value** ("first start to last end ≤ 13 h"): the same verdicts (V-75), but it needs unbounded history, where the look-back needs two days.
- **An exemption switch on the AC agreement:** the sources forbid exempting whole groups (PAV 19.8).
- **A structured derogation model now:** it rests on three unanswered questions.
- **Rebuilding the weekly check now:** it needs two expert answers and a longer read.
- **An interim "not checked" marker for days over 13 hours:** declined by the owner (Q1, 2026-11-13, "No, register only").
- **Merging the maximum-daily-hours and daily-rest findings.**
- **Owner Q2's other options:** (b) building it by reopening wave 3, which would have redone a reviewed stack; (c) scheduling it for the next sprint, which would have left QUAL-253 open at High and had wave 5 build the served minimum rest on a calculation it then replaced.
- **The zone read inside the rule, or the EU clock-change rule hard-coded:** the first is ambient state (ADR-002), the second a second copy of clock-change knowledge (`CopenhagenBusinessDate.cs` keeps one).
- **Half-up rounding of the shown value** (D2).

## References

- The dossier `docs/references/rest-period-24h-research.md`, and its sources `.claude/reviews/S148-RESEARCH-rest-24h-pass1.md`, `…-verification.md` and `…-adjudication.md`.
- The owner's rulings: `docs/sprints/SPRINT-148.md`, "The 11-hour rest rule: verified research and owner Q2" (`:1588-1625`), and the TASK-14813 Step 5a section (`:1511-1543`).
- Adjudications: `.claude/reviews/S148-TASK-14813-5a-adjudication-c1.md`, `S148-wave3b-briefs-adjudication.md`, `S148-WAVE3B-5a-adjudication-c1.md`, `S148-TASK-14814-5a-adjudication-c1.md` (the rounding it set, superseded by D2).
- Code: `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RestPeriodRule.cs`; `src/RuleEngine/StatsTid.RuleEngine.Api/Program.cs`; `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs`; `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs`; `frontend/src/hooks/useSkema.ts`; `frontend/src/components/SkemaDayPanel.tsx`.
- Spec: `SYSTEM_TARGET.md:272-284`, `:300`, `:309`.
- ADRs: ADR-002 (pure engine), ADR-039 (midnight crossings), ADR-041 (business dates), ADR-043 D10, D11, D12.
- Registers: QUAL-253 (closed), QUAL-254, QUAL-255, QUAL-256, QUAL-275, QUAL-277; SEC-059.

## Revision history

- **2026-11-25 (S148 C-1).** Drafted by the Planning Drafter (Opus 5.5) from the three research files, the adjudications named in the header and the code at `bda18b03`; applied by the Orchestrator. It records the owner's rulings of 2026-11-13, the adjudications of 2026-11-12 … 2026-11-16 and the code merged as `40d3e694` and `2810ebeb`; it decides nothing new.
