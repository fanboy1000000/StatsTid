# O-10 — The documents owed at ADR-043's acceptance: apply-ready text

| | |
|---|---|
| **What this is** | The exact text for every line of ADR-043's "Documents owed at acceptance" (`.claude/plans/s147/ADR-043-working.md:1025-1052`), so the Orchestrator can apply each as a string replacement. The `planner` drafts; the Orchestrator applies. The targets (`docs/**`, `SYSTEM_TARGET.md`, `ROADMAP.md`) are the Orchestrator's |
| **Status** | DRAFT 1 (`planner`, Opus 5.5), 2026-10-29 |
| **Base commit** | `3c00fc88` (master, the main checkout; the work began at `93698d1f`, and the Orchestrator's `615f3ee5` and `3c00fc88` landed during it, changing only QUAL-234's row among the targets). Every "Old text" below is copied byte for byte from `3c00fc88`, and every line number is at that commit |
| **Orchestrator model** | S147: Opus 5.5 seat; drafting `planner` (Opus 5.5); judgment `adjudicator` / `reviewer` on Fable 5.1 while available |
| **Owner approval** | 2026-10-29: "May I update SYSTEM_TARGET.md (the product definition) as listed, so it matches your ADR-043 answers?" → "Yes, update as listed (Recommended)". It covers Blocks 1–8 only. Lines outside the list are in "Beyond the owed list" and need their own approval |
| **Companion edit** | `.claude/plans/s147/S148-BUILD-INPUT.md` draft 2.3: the Fable cycle-3 NOTE applied to S1-7 |

## What this is, in plain words

ADR-043 changes what StatsTid's overtime and flex rules *are*. Once it is accepted, every document that still states the old rule gives readers a second answer, so the product definition, the reference notes, the registers and the other decisions must point at the new rule. Four rules shaped every block:

- **The product definition (`SYSTEM_TARGET.md`) states the decided rule** as a requirement, followed by a short dated note: "(S147, 2026-10-29: ADR-043 D*n*; owner-approved spec update)". The note replaces the S146 "contradicted" note, which the S146 text itself said would be "rewritten at ADR-043's acceptance with the owner's approval".
- **Earlier decisions are not rewritten.** ADR-014, -016, -017, -024 and -032 each get an appended, dated "S147 note" saying what ADR-043 changes. The reference notes (`danish-agreements.md`) and the source register get dated notes beside the text they supersede; the tables stay as the record of what the code does today.
- **Nothing is described as built.** Not one line of ADR-043 is in the code yet. Every new paragraph says "decided; built in S148 stage *N*" and what the code does until then. The owed list's own wording "now honoured on the pay path" (§ I) and "stops being the static source of truth" (`ARCHITECTURE.md`) are written to this rule.
- **Plain words first.** Each new paragraph opens with what changes for a person, then gives the mechanism and the decision numbers (D1–D12 are ADR-043's decisions; D-*n* its decide-track questions; Prov. *n* its provisional items).

## How to apply

- Each block names its **Target**, its **Timing**, the **Old text** (a unique match at `3c00fc88`; checked with `grep -cF`) and the **New text**. Where a block has parts (a, b, …), apply each part as its own replacement.
- **Timing.** AT ACCEPTANCE (now): Blocks 0–20, 23 and 24. AT S147 CLOSE: Blocks 21 (SEC-056) and 22 (QUAL-231, QUAL-237), per the ADR's own "at S147's close".
- No two replacements in one file overlap, so the order inside a file does not matter.
- Block 0 replaces the ADR file first; the other blocks do not depend on it.

## Map: each owed line → its block

| # | Owed line (ADR-043 "Documents owed", working copy line) | Block | Timing |
|---|---|---|---|
| — | The ADR copy itself (the brief's first block) | 0 | now |
| 1 | `SYSTEM_TARGET.md:44`, note `:46` (§B) (`:1030`) | 1 | now |
| 2 | `SYSTEM_TARGET.md:81` (§C travel) (`:1031`) | 2 | now |
| 3 | `SYSTEM_TARGET.md:86` (§C calendar) (`:1032`) | 3 | now |
| 4 | `SYSTEM_TARGET.md:165`, note `:167` (§G item 1) (`:1033`) | 4 | now |
| 5 | `SYSTEM_TARGET.md:168`, `:174` (§G caps) (`:1034`) | 5a, 5b | now |
| 6 | `SYSTEM_TARGET.md:202-207`, `:229`, `:247`/`:249` (§I) (`:1035`) | 6a, 6b, 6c | now |
| 7 | `SYSTEM_TARGET.md:377`/`:379`, `:381-384`, `:386-391`/`:393` (§L) (`:1036`) | 7 | now |
| 8 | `SYSTEM_TARGET.md:403`/`:405`, `:407-412` (§M) (`:1037`) | 8a, 8b | now |
| 9 | `danish-agreements.md:48-60`, note `:50` (`:1038`) | 9 | now |
| 10 | `danish-agreements.md:62-69`, note `:64` (`:1039`) | 10 | now |
| 11 | `danish-agreements.md:71-92` (notes `:73`, `:75`), `:94-111` (note `:96`) (`:1040`) | 11a, 11b | now |
| 12 | `agreement-source-register.md`: the part-timer row; the S146 disputed rows (`:1041`) | 12 (14 parts) | now |
| 13 | `docs/ARCHITECTURE.md:61`, `:86`, `:150`, `:195-196`; the read contract (`:1042`) | 13a–13e | now |
| 14 | ADR-014 (`:1043`) | 14 | now |
| 15 | ADR-016 D10 (`:1044`) | 15 | now |
| 16 | ADR-017 (`:1045`) | 16 | now |
| 17 | ADR-024 (`:1046`) | 17 | now |
| 18 | ADR-032 `:47` (`:1047`) | 18 | now |
| 19 | `docs/knowledge-base/INDEX.md` (`:1048`) | 19 | now |
| 20 | `hr-follow-up-process-register.md` (`:1049`) | 20a–20g | now |
| 21 | `security-finding-register.md:216` SEC-056 (`:1050`) | 21a–21d | **close** |
| 22 | `quality-finding-register.md`, the close half: QUAL-231, QUAL-237 (`:1051`) | 22a, 22b | **close** |
| 22′ | `quality-finding-register.md`, the acceptance half: QUAL-204 and the routes (`:1051`) | 23a–23l | now |
| 23 | `ROADMAP.md` § 1 (`:1052`) | 24 | now |

**Count: 25 blocks — 23 at acceptance (0–20, 23, 24), 2 at S147's close (21, 22).**

---

## Block 0 — The ADR itself · AT ACCEPTANCE (now)

**Target:** `docs/knowledge-base/decisions/ADR-043-overtime-flex-monthly-norm.md` (today the S146 PROPOSED text; the working copy's seed).

**Step 1, the copy.** Replace the file's whole content with lines **259–1175** of `.claude/plans/s147/ADR-043-working.md` at `3c00fc88` (unchanged since `a4e52182`): from `# ADR-043 — Overtime and flex: a monthly norm, flex by default, overtime only for ordered hours` to the last line of the revision entry "Amendment A1, review cycle 1". That is everything between the two marker comments (`:257` and `:1177`), without the markers. The working copy's own apparatus (rulings table, one list, fold map, working-copy history) is not copied.

**Step 2, the status line.** In the copied file:

Old text (working copy line 263):

````text
| **Status** | **PROPOSED — amended 2026-10-29 from the S147 decide track; ACCEPTED after the dual-lens review (O-10).** Drafted in S146 (track D5) to the owner's direction OQ-3 = (b). In S147 the nine forks this ADR left open (F-1 … F-9) and three tender findings (QUAL-232, QUAL-233, QUAL-234) were put to the owner one at a time, each explained first, and answered on 2026-10-29; one of them, the overtime account (D-5), was told with no question, and the owner asked for nothing to be asked. Until acceptance nothing here binds code. S145's Q2 = A, Q3 = F1 and Q7 (b) stay "superseded in principle by the verified sources; in force in code until the model program replaces them; no new work on F1 machinery" (OQ-2 = (a), S146 refinement "Decisions recorded") |
````

New text:

````text
| **Status** | **ACCEPTED 2026-10-29 (S147, O-10) after the dual-lens review: Codex c1–c3 (`.claude/reviews/S147-ADR-043-codex*.md`), Fable c1–c3 (`.claude/reviews/S147-ADR-043-reviewer*.md`), adjudication `.claude/reviews/S147-ADR-043-adjudication-c1.md`.** Amended 2026-10-29 from the S147 decide track. Drafted in S146 (track D5) to the owner's direction OQ-3 = (b). In S147 the nine forks this ADR left open (F-1 … F-9) and three tender findings (QUAL-232, QUAL-233, QUAL-234) were put to the owner one at a time, each explained first, and answered on 2026-10-29; one of them, the overtime account (D-5), was told with no question, and the owner asked for nothing to be asked. It binds the build from S148 on; none of it is built yet, and each stage's sprint log says what has landed (see "How it is built"). S145's Q2 = A, Q3 = F1 and Q7 (b) stay "superseded in principle by the verified sources; in force in code until the model program replaces them; no new work on F1 machinery" (OQ-2 = (a), S146 refinement "Decisions recorded") |
````

**Step 3, the revision history.** Append one entry after the copied text's last lines.

Old text (working copy lines 1174–1175, the end of the copied text):

````text
    and `.claude/plans/s147/decide/D-04-marking-workflow.md:86`); so was cycle 2's one addition,
    `ConfigResolutionService.cs:237-239` (the config stage's demo caps).
````

New text:

````text
    and `.claude/plans/s147/decide/D-04-marking-workflow.md:86`); so was cycle 2's one addition,
    `ConfigResolutionService.cs:237-239` (the config stage's demo caps).
- **Accepted (2026-10-29, S147 O-10).** After three dual-lens review cycles (Codex c1 FINDINGS 3B/4W/1N, c2 CLEAN,
  c3 CLEAN; Fable c1, c2 and c3 READY-TO-ACCEPT), the Orchestrator copied the amended text from
  `.claude/plans/s147/ADR-043-working.md` (WC-2.2) to the knowledge base and set the status to ACCEPTED. Cycle 2's
  rulings are in the text above (WC-2.1, WC-2.2: the stage map's consistency fixes; the demo caps seeded into D6's new
  plus-cap column, the legacy `max_flex_balance` left null until stage 1; stage 3's R-B parenthetical). Cycle 3's one
  NOTE, the legacy column's retirement, went to the S148 build input's S1-7 (draft 2.3), not into this text. No owner
  decision was altered. The documents owed at acceptance were applied from `.claude/plans/s147/O-10-DOCS-OWED.md`;
  SEC-056's row and QUAL-231/QUAL-237's sentence follow at S147's close.
````

*Note for the Orchestrator.* The stage map says "S148 will open on `weekly-timeline-v2` once TASK-14708 merges". TASK-14708 is not merged at `3c00fc88`. If it does not merge in S147, that sentence needs a dated correction at close.

---

## Block 1 — SYSTEM_TARGET §B, the norm period · AT ACCEPTANCE (now)

**Target:** `SYSTEM_TARGET.md:44-46` (ADR-043 D1, D6).

Old text:

````text
Irregular norm periods (e.g. 4-week norm)

> The state norm period for HK/PROSA is one calendar month (V1); AC's is not weekly (C5). *2026-10-23 (S146): contradicted by the verified sources (`docs/references/overtime-flex-model-research.md`, `docs/references/agreement-cells-reverification-research.md`); direction ruled by the owner, OQ-3 = (b); ADR-043 PROPOSED; this text is rewritten at ADR-043's acceptance with the owner's approval.*
````

New text:

````text
Norm period: one calendar month for HK, PROSA and AC — 7.4 h × the month's working days, pro rata for part-timers, minus public holidays (søgnehelligdage) that fall on a weekday the person would work; a local agreement may set another settlement period (§ G)

> *(S147, 2026-10-29: ADR-043 D1, D6; owner-approved spec update.)* In plain words: the hours a person owes are counted per calendar month, not per week, and a weekday public holiday lowers them. This is the state's own rule for HK and PROSA (§ 4 stk. 2; `docs/references/overtime-flex-model-research.md` V1); for AC it is a labelled default, because the AC agreement sets no period (`docs/references/agreement-cells-reverification-research.md` C5). Replaces "Irregular norm periods (e.g. 4-week norm)" and its S146 contradiction note. Decided; built in S148 stage 1. Until then payroll still judges each Monday–Sunday week, and no public holiday lowers the norm.
````

---

## Block 2 — SYSTEM_TARGET §C, travel (on-call and call-in stay) · AT ACCEPTANCE (now)

**Target:** `SYSTEM_TARGET.md:79-82` (ADR-043 D11; owner D-3 = (c)). On-call (`:79`) and call-in (`:80`) keep their lines; the travel line is reworded; a note follows the list.

Old text:

````text
On-call duty (rådighedsvagt)
Call-in work
Travel time (working vs non-working)
Special ministry domains (e.g. defense)
````

New text:

````text
On-call duty (rådighedsvagt)
Call-in work
Travel time on official trips: counts in full as worked time, capped at 13 h per working døgn (the 24 hours from the start of the working day)
Special ministry domains (e.g. defense)

> *(S147, 2026-10-29: ADR-043 D11; owner-approved spec update.)* In plain words: on-call, call-ins and travel are not ordinary hours, and each counts as worked time in its own sourced way. For each type and agreement the system holds three facts: how much of the time counts as worked (a fraction), what the supplement is paid on, and whether the time counts as rest. HK and PROSA (Statens arbejdstidsaftale § 7 nr. 3–5): on-call counts 1/3 at home and 3/4 at the workplace; a call-in at under 24 hours' notice counts at least 3 h; travel counts in full up to 13 h per working døgn, never halved. AC-agreement staff: travel only, at most 13 h per døgn unless agreed locally. Replaces the "working vs non-working" travel split, which no source supports. Decided; S148 stage 0 refuses these three types (they can be neither registered nor paid), and a later stage (stage 2 or later) builds the model.
````

---

## Block 3 — SYSTEM_TARGET §C, the calendar · AT ACCEPTANCE (now)

**Target:** `SYSTEM_TARGET.md:86-89` (ADR-043 D9; D-12.2 item 5). Line 86 is rewritten; `:87-89` are unchanged; a note follows.

Old text:

````text
Calendar context (public holidays)
Employment profile
Agreement version
Calendar integration required.
````

New text:

````text
Calendar context: one date-keyed holiday calendar with three kinds of day — (1) the ten public holidays (søgnehelligdage), which lower the norm when they fall on a weekday; (2) timed supplement windows (Grundlovsdag from 12:00, 24 December from 14:00); (3) each institution's own customary days off, dated and audited: a paid day off (whole or part; given to everyone, or on request) or a closure day the employee covers with leave, time off in lieu or flex
Employment profile
Agreement version
Calendar integration required.

> *(S147, 2026-10-29: ADR-043 D9; owner-approved spec update.)* In plain words: not every special day is a public holiday. Grundlovsdag, 24 and 31 December and 1 May are not (`docs/references/agreement-cells-reverification-research.md` C1); without a recorded local custom they are ordinary working days. Holidays are keyed by date, not by agreement version (Store Bededag ended by law on 1 January 2024). A paid customary day is credited like a day of paid absence; it does not lower the norm. Decided; the customary-days table is built in S148's config stage, and the calendar split and its pay effect in stage 1. Until then the code keeps three separate holiday lists, and all three count Grundlovsdag as a holiday.
````

---

## Block 4 — SYSTEM_TARGET §G item 1, the settlement period · AT ACCEPTANCE (now)

**Target:** `SYSTEM_TARGET.md:165-167` (ADR-043 D1, D6).

Old text:

````text
1. **Working Time Planning** — Norm period length (1/4/8/12 weeks), planning start day, planning calendar

> A calendar-month norm period cannot be expressed in weeks (QUAL-216). *2026-10-23 (S146): contradicted by the verified sources (`docs/references/overtime-flex-model-research.md`, `docs/references/agreement-cells-reverification-research.md`); direction ruled by the owner, OQ-3 = (b); ADR-043 PROPOSED; this text is rewritten at ADR-043's acceptance with the owner's approval.*
````

New text:

````text
1. **Working Time Planning** — settlement period (the calendar month by default; another period only by local agreement), planning start day, planning calendar

> *(S147, 2026-10-29: ADR-043 D1, D6; owner-approved spec update.)* In plain words: the norm period is no longer a number of weeks an institution picks. The state's rule is the calendar month; a local agreement may set another settlement period (*"medmindre andet er aftalt"*). S148 builds the one value `month`; other values are recorded for the domain expert (ADR-043 provisional item 19). Replaces "Norm period length (1/4/8/12 weeks)" and its S146 contradiction note. Decided; built in S148's config stage (the setting) and stage 1 (its effect on pay).
````

---

## Block 5 — SYSTEM_TARGET §G, the flex caps · AT ACCEPTANCE (now)

**Target:** `SYSTEM_TARGET.md:168` and `:174` (ADR-043 D6, with D2, D3, D9; batch-2 ruling 2). The principle at `:175` stands, for rates.

**5a.** Old text:

````text
2. **Flex Rules** — Maximum flex balance (within central limit), warning thresholds, payout triggers
````

New text:

````text
2. **Flex Rules** — each institution's own, in dated local settings, with no central flex cap: the plus cap, the minus cap and the daily credit cap (each unset unless the institution sets it), the cap-test period, the action above the cap (forfeit and record by default; or a manager's exception, a transfer to the overtime account, 1:1 cash by agreement), the leaving action, the paid-absence credit basis, office hours (an optional clock window per weekday), customary days off (§ C), warning thresholds. Whether a person is on the flex scheme at all is a dated fact HR records per person. *(S147, 2026-10-29: ADR-043 D6, D2, D3, D9; owner-approved spec update. In plain words: there is no central state flex agreement (PAV 19.5.1), so the central caps had no source; "within central limit" and "payout triggers" are withdrawn, and flex above a cap is forfeited and shown, not paid. Decided; the settings are built in S148's config stage, their pay effect in stage 1. Until then pay still runs on today's central caps, and flex above them is paid out.)*
````

**5b.** Old text:

````text
Local values must respect central min/max boundaries (e.g. local maxFlexBalance must be <= central MaxFlexBalance)
````

New text:

````text
Local values must respect central min/max boundaries where a central agreement sets one; there is no central flex cap, so an institution's flex caps have no central bound *(S147, 2026-10-29: ADR-043 D6; owner-approved spec update)*
````

---

## Block 6 — SYSTEM_TARGET §I, configs reach pay; travel facts; the period setting · AT ACCEPTANCE (now)

**Target:** `SYSTEM_TARGET.md:207`, `:229`, `:247-249` (ADR-043 D12, D11, D1).

**6a.** A note after `:207` (the paragraph `:202-207` stands; the note says it is decided for the pay path, not yet built).

Old text:

````text
The rule engine remains pure: it receives `AgreementRuleConfig` as a parameter and never performs I/O. The service layer loads configs from the database and passes them to the rule engine.
````

New text:

````text
The rule engine remains pure: it receives `AgreementRuleConfig` as a parameter and never performs I/O. The service layer loads configs from the database and passes them to the rule engine.

> *(S147, 2026-10-29: ADR-043 D12; owner-approved spec update.)* In plain words: what a GlobalAdmin publishes here, and what an institution sets in its local profile, is what payroll calculates with. For each day of a month, payroll resolves the published agreement config, the position override and the institution's local profile in force that day (central → position → local) at the service layer, and passes them to the pure rule engine in the request; every value a calculation used is frozen into its proof. Each published agreement config and position override carries an "applies from" date. A publish never changes an exported month; an exported month changes only by an explicit correction. Decided; built in S148's config stage. Until then the rule engine reads configs compiled into the code, so these editors change screens and booking but not pay, and from S148 stage 0 they say so ("affects screens and booking; does not yet affect pay").
````

**6b.** Old text:

````text
- **Travel**: TravelTimeEnabled, WorkingTravelRate, NonWorkingTravelRate
````

New text:

````text
- **Travel, on-call and call-in**: TravelTimeEnabled; for each time type and agreement three facts, a worked-time fraction, a supplement basis and a rest flag, in place of the working and non-working travel rates *(S147, 2026-10-29: ADR-043 D11; owner-approved spec update. Decided; built in a later stage, S148 stage 2 or later)*
````

**6c.** Old text:

````text
- NormPeriodWeeks must be one of: 1, 2, 4, 8, 12

> The set {1, 2, 4, 8, 12} cannot express the state default of one calendar month (QUAL-216). *2026-10-23 (S146): contradicted by the verified sources (`docs/references/overtime-flex-model-research.md`, `docs/references/agreement-cells-reverification-research.md`); direction ruled by the owner, OQ-3 = (b); ADR-043 PROPOSED; this text is rewritten at ADR-043's acceptance with the owner's approval.*
````

New text:

````text
- The settlement period is the calendar month; S148 builds the one value `month`, and other values are recorded for the domain expert

> *(S147, 2026-10-29: ADR-043 D1, D6; owner-approved spec update.)* Replaces "NormPeriodWeeks must be one of: 1, 2, 4, 8, 12", a week set that cannot express a month (QUAL-216). Decided; built in S148's config stage and stage 1. Until then `NormPeriodWeeks` and the week set stay in code.
````

---

## Block 7 — SYSTEM_TARGET §L, overtime governance becomes decided hours · AT ACCEPTANCE (now)

**Target:** `SYSTEM_TARGET.md:377-393` (ADR-043 D3, D6; owner F-2 = (c), F-4 = (b)). The heading `### L. Overtime Governance` (`:375`) stays.

Old text:

````text
The system currently calculates overtime correctly but lacks governance controls. For a production system, overtime must be bounded and controllable.

> It does not: every hour above a weekly norm becomes overtime automatically, where the sources pay overtime only for ordered or necessary work and route every other surplus hour to flex (QUAL-217, QUAL-218). *2026-10-23 (S146): contradicted by the verified sources (`docs/references/overtime-flex-model-research.md`, `docs/references/agreement-cells-reverification-research.md`); direction ruled by the owner, OQ-3 = (b); ADR-043 PROPOSED; this text is rewritten at ADR-043's acceptance with the owner's approval.*

#### Maximum Overtime
- `MaxOvertimeHoursPerPeriod`: configurable ceiling per agreement (e.g., 10 hours/week, 40 hours/month)
- When exceeded, the system must flag but still record (overtime may be legitimate with approval)
- Exceeded overtime must be visible in leader dashboard and approval workflow

#### Overtime Pre-Approval
- Some organizations require pre-approval before overtime is worked
- `OvertimeRequiresPreApproval`: boolean per agreement or local config
- When enabled, overtime registrations without prior approval generate a warning
- Pre-approval is a workflow concept (not a rule engine concern) — modeled as a flag on TimeEntry or a separate approval record
- Unapproved overtime must still be calculable (for retroactive approval) but flagged in export

> Under the sources an unordered surplus hour is flex, not flagged overtime (V3, V6; QUAL-217). *2026-10-23 (S146): contradicted by the verified sources (`docs/references/overtime-flex-model-research.md`, `docs/references/agreement-cells-reverification-research.md`); direction ruled by the owner, OQ-3 = (b); ADR-043 PROPOSED; this text is rewritten at ADR-043's acceptance with the owner's approval.*
````

New text:

````text
In plain words: an hour worked above the month's norm is a flex hour unless a leader ordered it beforehand or accepted afterwards that it was necessary. Only those decided hours become overtime (HK, PROSA pay groups 1–2, +50 %) or count toward an AC merarbejde claim, and they leave the flex account, so one hour is never both. Governance is therefore a record of decisions about hours, not a flag on automatic overtime.

#### Decided Hours
- A leader may record an order, or a roster span, beforehand, naming the date and the clock span; the ordered hours are the overlap of the span with the hours actually worked
- Work nobody ordered in advance is marked by the employee in Skema as ordered or necessary; the leader accepts or rejects the mark in month approval (Teamoversigt). A mark is decided before its month is approved, by whoever holds that month's approval authority; the month cannot be approved while a mark is undecided
- Every record holds a date and a clock span; its kind (ordered beforehand, or accepted afterwards); a placement fact ("placement ordered or rostered", which the +25 % supplement and § 14's compensation read), kept apart from the surplus fact ("ordered or necessary"); an activity type; a creator, a decider and a status
- Four-eyes: a leader's order is decided at creation by its creator; no one decides their own hours (403); a record awaiting a decision is decided by someone other than its creator; the authority to order or decide is the authority to approve that employee's month, not bare organisation scope
- A decision made after the month is approved or exported lands as a dated movement in the month it is made; no exported month is reopened
- The records reach the pure rule engine as frozen inputs, frozen by value into each calculation's proof
- Where an institution sets office hours (§ G), a decided hour leaves flex only if it fell outside office hours and after the person's per-day norm amount (7 h 24 min for a full-timer); ordered weekend and public-holiday work stays under § 14 whatever the office hours

#### Maximum Overtime
- `MaxOvertimeHoursPerPeriod`: an optional warning over decided hours (per agreement or local setting); it never refuses or reclassifies an hour
- An exceeded warning is visible to the leader in the approval workflow

> *(S147, 2026-10-29: ADR-043 D3, D6; owner-approved spec update.)* Replaces "The system currently calculates overtime correctly but lacks governance controls", the overtime ceiling as a flag on automatic overtime, and the `OvertimeRequiresPreApproval` switch. Every overtime hour now needs a decision, so an optional pre-approval switch has no meaning and retires, with today's budget record, which names no hours. Decided; built in S148 stage 2. Until then: through stage 0 and the config stage, the code still pays overtime on every hour above a weekly norm; from stage 1 every hour above the month's norm goes to flex, ordered or not, with no +50 % until stage 2.
````

---

## Block 8 — SYSTEM_TARGET §M, the employer's choice and the overtime account · AT ACCEPTANCE (now)

**Target:** `SYSTEM_TARGET.md:403-405` and `:407-412` (ADR-043 D8; D-5 told). `:410`'s "creates an absence entry" is kept, as time off in lieu.

**8a.** Old text:

````text
- **Employee choice**: Some agreements allow employee to choose per period — configurable (`EmployeeCompensationChoice`: boolean)

> For HK, PROSA and AC the employer chooses time off vs pay (V10, C7; QUAL-221). *2026-10-23 (S146): contradicted by the verified sources (`docs/references/overtime-flex-model-research.md`, `docs/references/agreement-cells-reverification-research.md`); direction ruled by the owner, OQ-3 = (b); ADR-043 PROPOSED; this text is rewritten at ADR-043's acceptance with the owner's approval.*
````

New text:

````text
- **The employer chooses, time off first**: the default is time off at +50 % (6 decided hours = 9 hours off); the leader who decides may choose pay instead when time off is judged impossible or inappropriate, recorded with a reason. The employee has no general choice, and `EmployeeCompensationChoice` retires
- **The employee's two elections**, each the employee's own act: agreeing to postpone an earning's time off by up to a year (a record both the decider and the employee sign), and choosing saved days (*opsparingsdage*) instead of pay at the deadline

> *(S147, 2026-10-29: ADR-043 D8; owner-approved spec update.)* In plain words: for HK, PROSA and AC the employer, not the employee, chooses time off or pay (PAV 19.2.7.1; `docs/references/overtime-flex-model-research.md` V10; `docs/references/agreement-cells-reverification-research.md` C7; QUAL-221), and the employee keeps two narrower choices of their own. PROSA's printed "1:1" becomes +50 %. Decided; built in S148 stage 2. Until then the seeds still let HK and PROSA employees choose.
````

**8b.** Old text:

````text
#### Overtime Balance
- Separate from flex balance — overtime balance tracks accumulated overtime hours eligible for compensation
- `OvertimeBalance`: running total of uncompensated overtime hours
- Afspadsering reduces overtime balance and creates an absence entry
- Payout reduces overtime balance and creates a payroll export line
- Conversion rates must be agreement-aware (50% overtime = 1.5x for afspadsering, different SLS code for payout)
````

New text:

````text
#### Overtime Account
- Separate from flex balance — the overtime account holds decided hours only: § 8 overtime, § 14 weekend compensation, and AC merarbejde from the AC claims
- A ledger of dated movements, not a running total: *earned* (the period worked, the hours, the rate, the kind), *taken* as time off, *paid* (by a decision, or by the deadline's default), *postponed*, *converted* to saved days. Each movement is audited and frozen into the proof of the month it lands in; none edits an exported month
- The balance and each earning's deadline are derived by the pure rule engine from the movements, never a stored total anyone adjusts
- Every compensation draws on decided earnings, never on a bare balance; a decision about another person's account is made by whoever holds that person's month-approval authority, never on the decider's own hours
- Each earning has a clock: § 8 overtime is due as time off within 3 months of the end of the month worked, otherwise it is paid (a 30 September deadline pays in the October pay run); § 14 weekend compensation by the end of the following norm period. A leaders' "time off due" list and the employee's account screen show each earning with its deadline
- Afspadsering reduces the account and creates an absence entry: a Skema time-off-in-lieu day, credited as a planned day, drawing on the earning with the earliest deadline first
- Payout reduces the account and creates a payroll export line (hours and a wage type, never kroner)
- Conversion rates are agreement-aware (+50 % = 1.5 h off per decided hour; a different SLS code for payout)
- A leaver's owed time off is paid, never forfeited (in the leaving month: a labelled default)

#### Saved Days (Opsparingsdage)
- StatsTid holds a saved-days balance: the employee's election screen, a Skema "saved day" absence type, and the cap as a local parameter with the dated central floor (74 h, then 111 h from 1 April 2026, OK26 § 11 stk. 2)
- Designed; built after research on § 11 (ADR-043 R-A), in a sprint S148's refinement chooses. Until then an election is recorded as a movement, and a saved day taken is registered as paid leave

> *(S147, 2026-10-29: ADR-043 D8; owner-approved spec update.)* Replaces the yearly running total (`OvertimeBalance`). Decided; the account is built in S148 stage 2. Until then the yearly balance exists in code, and nothing in production feeds it.
````

---

## Block 9 — danish-agreements.md, the supplement windows · AT ACCEPTANCE (now)

**Target:** `docs/references/danish-agreements.md:50` (the S146 note above the table `:52-60`) — a dated note beside it (ADR-043 D7, D9, D10).

Old text:

````text
> **S146 D4 note (2026-10-23): contradicted by a verified primary source; still in force in code.** The state has one unsocial-hours supplement, +25 % for weekdays 17:00-06:00, weekends, public holidays, Grundlovsdag after 12:00 and 24 Dec after 14:00 (state working-time agreement § 13); ordered weekend or holiday work gets +50 % compensation on top and is left out of the monthly hours count (§ 14). No 1.50 night rate, no Saturday/Sunday difference and no 2.0 Sunday or holiday rate exist for general HK or PROSA. Grundlovsdag is not a public holiday, yet it is paid at 2.0 today. See `docs/references/agreement-cells-reverification-research.md` (C1, C2); register QUAL-227. The rebuild is the S147 model program.
````

New text:

````text
> **S146 D4 note (2026-10-23): contradicted by a verified primary source; still in force in code.** The state has one unsocial-hours supplement, +25 % for weekdays 17:00-06:00, weekends, public holidays, Grundlovsdag after 12:00 and 24 Dec after 14:00 (state working-time agreement § 13); ordered weekend or holiday work gets +50 % compensation on top and is left out of the monthly hours count (§ 14). No 1.50 night rate, no Saturday/Sunday difference and no 2.0 Sunday or holiday rate exist for general HK or PROSA. Grundlovsdag is not a public holiday, yet it is paid at 2.0 today. See `docs/references/agreement-cells-reverification-research.md` (C1, C2); register QUAL-227. The rebuild is the S147 model program.

> **S147 note (2026-10-29): ADR-043 D7 and D9 decide the rule (ACCEPTED 2026-10-29); built in S148.** In plain words: the table below is what the code pays today, not the rule. HK and PROSA have **one** +25 % supplement, for work 17:00–06:00, at weekends, on public holidays, on Grundlovsdag from 12:00 and on 24 December from 14:00 (§ 13), and it is due only for work placed there by order or under an approved roster: an evening an employee chose under flex earns nothing extra. Ordered or rostered weekend and public-holiday work for day workers also earns § 14's +50 % (as time off or pay, the employer's choice), kept out of the month's hours count, with no overtime on top. Both apply to staff exempt from overtime and to part-timers' extra hours. §§ 13–14 are word for word the same in OK24 and OK26, so there is one set of windows. Grundlovsdag is not a public holiday (D9). No 1.50 night rate and no 2.0 Sunday or holiday rate exist for general HK and PROSA. AC stays without supplements. **Timing:** S148 stage 1 pays no supplement at all, because the placement fact only exists from stage 2; stage 2 pays the new supplement, reading Skema's clock intervals (D10).
````

---

## Block 10 — danish-agreements.md, the thresholds and the part-timer band · AT ACCEPTANCE (now)

**Target:** `docs/references/danish-agreements.md:64` (the S146 note, which still says part-timers' hours up to full time are 1:1) — a dated note beside it (ADR-043 D4; D-10.3).

Old text:

````text
> **S146 note (2026-10-23): contradicted by a verified primary source; still in force in code.** Neither threshold was ever sourced (both rows read "pending (Phase B)"). The state working-time agreement pays HK and PROSA (pay groups 1-2) overtime at a **flat +50%** for hours above the period's norm, only for ordered or necessary work, with part-timers' hours up to full time at 1:1 (§ 8). The period is a calendar month, not a week (§ 4 stk. 2). No 100% tier exists for general HK or PROSA. See `docs/references/overtime-flex-model-research.md` (V2, V4); register QUAL-215, QUAL-218. The rebuild is the S147 model program (owner OQ-3 = (b)).
````

New text:

````text
> **S146 note (2026-10-23): contradicted by a verified primary source; still in force in code.** Neither threshold was ever sourced (both rows read "pending (Phase B)"). The state working-time agreement pays HK and PROSA (pay groups 1-2) overtime at a **flat +50%** for hours above the period's norm, only for ordered or necessary work, with part-timers' hours up to full time at 1:1 (§ 8). The period is a calendar month, not a week (§ 4 stk. 2). No 100% tier exists for general HK or PROSA. See `docs/references/overtime-flex-model-research.md` (V2, V4); register QUAL-215, QUAL-218. The rebuild is the S147 model program (owner OQ-3 = (b)).

> **S147 note (2026-10-29): ADR-043 D4 decides (ACCEPTED 2026-10-29); built in S148.** HK and PROSA (pay groups 1–2) overtime is a flat +50 % on decided hours above the month's norm, with no 37/40 h thresholds and no 100 % tier; `OvertimeThreshold50/100` retire (weekly overtime retires in stage 1; the +50 % arrives in stage 2). **The S146 note's "part-timers' hours up to full time at 1:1 (§ 8)" is the printed text, and it is not applied.** By owner ruling of 2026-10-29 (D-10: F-7 = (c), default (ii)), § 8 stk. 2's 1:1 band is not applied, provisional until the state publishes the text implementing the binding award of 4 February 2026, **FV2025-795** (none as of 28 October 2026). In plain words: a part-timer is a full-timer with a smaller norm, so an ordered or accepted hour above a part-timer's own norm earns +50 %, as a full-timer's does. The rule is held as a dated, named variant (`PartTimeBandRule`), so the state's text can switch in on its date (ADR-043 provisional items 1 and 2; owed research R-D, R-E). Who earns overtime at all is the overtime regime HR records per person: HK staff above the pay ceiling are exempt, PROSA pay groups 3–4 earn merarbejde.
````

---

## Block 11 — danish-agreements.md, weekly settlement and the compensation model · AT ACCEPTANCE (now)

**11a.** **Target:** `docs/references/danish-agreements.md:73` (the S146 note heading the weekly-settlement section `:71-92`; the S145 note `:75` stays as history) (ADR-043 D1, D2).

Old text:

````text
> **S146 note (2026-10-23): the premise below is contradicted; the rulings stay in force in code, provisionally.** The note below says the agreement sources are silent on the norm period. They are not: the state working-time agreement § 4 stk. 2 sets **one calendar month** for HK and PROSA ("Arbejdstiden beregnes for en periode på 1 måned"), and PAV 19.2.4.1 says how hours fall across days and weeks "does not matter". The register row cited for the silence (SR-AC-OK24-020) points at a section on form of employment. **Owner ruling S146 OQ-2 = (a):** Q2 = A, Q3 = F1 and Q7 (b) are superseded in principle by the verified sources, stay in force in code until the model program replaces them, and get no new work (QUAL-209/210/212/213 frozen). The per-day `DayNorm` sum is expected to survive in a new form (a month's norm is the per-day sum minus weekday holidays). See `docs/references/overtime-flex-model-research.md` (V1, V12); register QUAL-215, QUAL-216, QUAL-222.
````

New text:

````text
> **S146 note (2026-10-23): the premise below is contradicted; the rulings stay in force in code, provisionally.** The note below says the agreement sources are silent on the norm period. They are not: the state working-time agreement § 4 stk. 2 sets **one calendar month** for HK and PROSA ("Arbejdstiden beregnes for en periode på 1 måned"), and PAV 19.2.4.1 says how hours fall across days and weeks "does not matter". The register row cited for the silence (SR-AC-OK24-020) points at a section on form of employment. **Owner ruling S146 OQ-2 = (a):** Q2 = A, Q3 = F1 and Q7 (b) are superseded in principle by the verified sources, stay in force in code until the model program replaces them, and get no new work (QUAL-209/210/212/213 frozen). The per-day `DayNorm` sum is expected to survive in a new form (a month's norm is the per-day sum minus weekday holidays). See `docs/references/overtime-flex-model-research.md` (V1, V12); register QUAL-215, QUAL-216, QUAL-222.

> **S147 note (2026-10-29): this whole section is superseded by ADR-043 D1 and D2 (ACCEPTED 2026-10-29); it stays in force in code until S148 stage 1.** In plain words: there is no weekly settlement any more. The norm is the calendar month (7.4 h × the month's working days, pro rata, minus weekday søgnehelligdage), and an hour above it is flex unless a leader ordered or accepted it. Q2 = A survives only as the month norm's building block, the per-day amount `round(weekly × fraction ÷ 5, 2)` summed over the month's norm-bearing weekdays (ADR-043 provisional item 6). Q3 = F1's settle-when-the-week-closes machinery, the prior-month approval wait, the mid-week agreement refusal (Q5 (c), QUAL-209) and Q7 (b) retire in stage 1. The text below is kept as the record of what the code does until then.
````

**11b.** **Target:** `docs/references/danish-agreements.md:96` (the S146 note heading the compensation model `:94-111`) (ADR-043 D8).

Old text:

````text
> **S146 note (2026-10-23): two cells contradicted by a verified primary source.** (1) For HK, time off vs pay is **the employer's** choice (PAV 19.2.7.1), so `EmployeeCompensationChoice = true` is contradicted; the deadline is 3 months from the end of the **norm period** (§ 10). (2) PROSA is described below as "afspadsering or payment 1:1"; § 8 gives +50%. Payroll also ignores `DefaultCompensationModel` today. See `docs/references/overtime-flex-model-research.md` (V10); register QUAL-221. The AC row's citation (043-19 § 4) is "Basisløn" (base pay), not the compensation rule (AC § 14 stk. 5-6); QUAL-215.
````

New text:

````text
> **S146 note (2026-10-23): two cells contradicted by a verified primary source.** (1) For HK, time off vs pay is **the employer's** choice (PAV 19.2.7.1), so `EmployeeCompensationChoice = true` is contradicted; the deadline is 3 months from the end of the **norm period** (§ 10). (2) PROSA is described below as "afspadsering or payment 1:1"; § 8 gives +50%. Payroll also ignores `DefaultCompensationModel` today. See `docs/references/overtime-flex-model-research.md` (V10); register QUAL-221. The AC row's citation (043-19 § 4) is "Basisløn" (base pay), not the compensation rule (AC § 14 stk. 5-6); QUAL-215.

> **S147 note (2026-10-29): ADR-043 D8 decides (ACCEPTED 2026-10-29); built in S148 stage 2.** In plain words: the employer chooses for HK, PROSA and AC, time off first. The default is time off at +50 % (6 decided hours = 9 hours off); pay only when time off is judged impossible or inappropriate, recorded with a reason. `EmployeeCompensationChoice` and its routes retire, so the table's `true` cells are today's seeds, not the rule; PROSA's "1:1" becomes +50 %. The employee keeps two elections of their own: agreeing to postpone an earning's time off (up to a year), and choosing saved days at the deadline. Each earning sits in an overtime account of dated movements with § 10's 3-month clock, replacing the yearly balance (HRP-021). AC's time-off deadline is owed research (R-B).
````

---

## Block 12 — agreement-source-register.md, the part-timer cell and the disputed rows · AT ACCEPTANCE (now)

**Target:** `docs/references/agreement-source-register.md`. Each part appends an "S147 verdict" to the end of the row's `notes` cell (QUAL-215's lesson: each cell names what it rests on). `disputed?` stays `true` on every row: the code still contradicts the sources until S148 builds the change. Each Old text is the end of the `notes` cell, unique in the file (`grep -cF` = 1).

**12a. SR-HK-OK24-006 (`:1090`; notes `:1108`), the row the part-timer cell rests on today.** A new row for the `PartTimeBandRule` slot is created when S148 builds the variant; until then this row carries the source.

Old text:

````text
V2, V4; QUAL-218. `confidence_level` is left for S146 D4's verdict (D4 overwrites both fields when it lands). |
````

New text:

````text
V2, V4; QUAL-218. `confidence_level` is left for S146 D4's verdict (D4 overwrites both fields when it lands). **S147 verdict (2026-10-29, ADR-043 D4, ACCEPTED):** the threshold retires; HK and PROSA (pay groups 1–2) overtime is a flat +50 % on decided hours above the month's norm (weekly overtime retires in S148 stage 1; the +50 % arrives in stage 2). **The part-timer cell is not the printed § 8 stk. 2:** by owner ruling of 2026-10-29 (D-10, F-7 = (c), default (ii)), provisional pending a qualified expert and the state's text implementing the binding award of 4 February 2026, **FV2025-795** (none as of 28 October 2026), the 1:1 band is not applied, and an ordered or accepted hour above a part-timer's own norm earns +50 %, held as the dated, named variant `PartTimeBandRule` (ADR-043 provisional items 1–2; owed research R-D, R-E). Its source is that award and that ruling, not a cirkulære quote; the variant gets its own row when S148 builds it. |
````

**12b. SR-AC-OK24-007 HasMerarbejde (notes `:254`).** Old text:

````text
QUAL-215, QUAL-220. `confidence_level` is left for S146 D4's verdict (D4 overwrites both fields when it lands). |
````

New text:

````text
QUAL-215, QUAL-220. `confidence_level` is left for S146 D4's verdict (D4 overwrites both fields when it lands). **S147 verdict (2026-10-29, ADR-043 D4, D5, ACCEPTED):** AC merarbejde is a claim the employee raises and the employer decides (ordered or a precondition for the job, of larger extent over normally at least 4 weeks, +50 %; AC § 14), judged over the period each claim states (F-6 = (a)); whether a person may raise one is the overtime regime HR records per person (CONTRACTUAL only; the AC values are ADR-024 D2's three states, provisional). Built in S148 stage 3; until then AC extra work is flex only (from stage 1). |
````

**12c. SR-AC-OK24-020 the AC norm period (notes `:514`).** Old text:

````text
some teachers and special/chief consultants are on an annual norm. Never weekly. `docs/references/agreement-cells-reverification-research.md` C5; QUAL-216. |
````

New text:

````text
some teachers and special/chief consultants are on an annual norm. Never weekly. `docs/references/agreement-cells-reverification-research.md` C5; QUAL-216. **S147 verdict (2026-10-29, ADR-043 D1, ACCEPTED; owner F-6 = (a)):** AC flex settles each calendar month on the same month-norm function as HK and PROSA (7.4 h a working day, pro rata), and the AC norm falls on weekday søgnehelligdage: a labelled default for the domain expert (ADR-043 provisional item 4), because the AC agreement sets no period. The annual-norm variants keep `ANNUAL_ACTIVITY`; how their flex settles is an open design gap. `NormPeriodWeeks` retires (S148 config stage and stage 1). |
````

**12d. SR-AC-OK24-021 HasOvertime (notes `:534`).** Old text:

````text
V9, V12; QUAL-215. `confidence_level` is left for S146 D4's verdict (D4 overwrites both fields when it lands). |
````

New text:

````text
V9, V12; QUAL-215. `confidence_level` is left for S146 D4's verdict (D4 overwrites both fields when it lands). **S147 verdict (2026-10-29, ADR-043 D4, ACCEPTED):** the value stands (AC earns no overtime); whether an AC employee may raise a merarbejde claim is decided per person by the HR-recorded overtime regime (ADR-024 D2's three states, provisional), not by this agreement-level flag. |
````

**12e. SR-HK-OK24-001 the HK norm (notes `:1008`).** Old text:

````text
V1, V12; QUAL-215, QUAL-216. `confidence_level` is left for S146 D4's verdict (D4 overwrites both fields when it lands). |
````

New text:

````text
V1, V12; QUAL-215, QUAL-216. `confidence_level` is left for S146 D4's verdict (D4 overwrites both fields when it lands). **S147 verdict (2026-10-29, ADR-043 D1, ACCEPTED):** the norm is the calendar month (7.4 h × the month's working days, pro rata, minus weekday søgnehelligdage); `NormPeriodWeeks` retires and the month becomes the settlement period (S148 config stage and stage 1). The 37 h week survives only as the base of the per-day amount. |
````

**12f. SR-HK-OK24-007 OvertimeThreshold100 (notes `:1128`).** Old text:

````text
municipal-reform terms). `docs/references/overtime-flex-model-research.md` V4; QUAL-218. `confidence_level` is left for S146 D4's verdict (D4 overwrites both fields when it lands). |
````

New text:

````text
municipal-reform terms). `docs/references/overtime-flex-model-research.md` V4; QUAL-218. `confidence_level` is left for S146 D4's verdict (D4 overwrites both fields when it lands). **S147 verdict (2026-10-29, ADR-043 D4, ACCEPTED):** retires; there is no 100 % tier for general HK or PROSA. The weekly tiers retire in S148 stage 1, the 100 % wage types in stage 2. |
````

**12g. SR-HK-OK24-009 the supplement windows (notes `:1168`).** Old text:

````text
there is no separate evening/night split. `docs/references/agreement-cells-reverification-research.md` C2; QUAL-227. |
````

New text:

````text
there is no separate evening/night split. `docs/references/agreement-cells-reverification-research.md` C2; QUAL-227. **S147 verdict (2026-10-29, ADR-043 D7, D9, ACCEPTED):** one window set (17:00–06:00, weekends, søgnehelligdage, Grundlovsdag from 12:00, 24 December from 14:00), paid at +25 % only for placements that were ordered or rostered; the same in OK24 and OK26. No supplement from S148 stage 1; the new one in stage 2. |
````

**12h. SR-HK-OK24-010 EveningRate (notes `:1188`).** Old text:

````text
§ 13 runs 17:00-06:00 with no night step. `docs/references/agreement-cells-reverification-research.md` C2; QUAL-227. |
````

New text:

````text
§ 13 runs 17:00-06:00 with no night step. `docs/references/agreement-cells-reverification-research.md` C2; QUAL-227. **S147 verdict (2026-10-29, ADR-043 D7, ACCEPTED):** replaced by the single +25 % supplement over 17:00–06:00, for ordered or rostered placements only (no supplement from S148 stage 1; the new one in stage 2). |
````

**12i. SR-HK-OK24-011 NightRate (notes `:1208`).** Old text:

````text
§ 13 pays +25 % from 17:00 to 06:00. `docs/references/agreement-cells-reverification-research.md` C2; QUAL-227. |
````

New text:

````text
§ 13 pays +25 % from 17:00 to 06:00. `docs/references/agreement-cells-reverification-research.md` C2; QUAL-227. **S147 verdict (2026-10-29, ADR-043 D7, ACCEPTED):** the 1.50 rate retires; night hours fall under the single +25 % supplement, for ordered or rostered placements only (no supplement from S148 stage 1; the new one in stage 2). |
````

**12j. SR-HK-OK24-012 WeekendSaturdayRate (notes `:1228`).** Old text:

````text
No 1.50 Saturday rate. `docs/references/agreement-cells-reverification-research.md` C2; QUAL-227. |
````

New text:

````text
No 1.50 Saturday rate. `docs/references/agreement-cells-reverification-research.md` C2; QUAL-227. **S147 verdict (2026-10-29, ADR-043 D7, D8, ACCEPTED):** the 1.50 rate retires. Ordered or rostered weekend work earns § 13's +25 % and § 14's +50 % compensation (time off or pay, the employer's choice), kept out of the month's hours count and earning into the overtime account with its own clock. No lines from S148 stage 1; replaced in stage 2. |
````

**12k. SR-HK-OK24-013 WeekendSundayRate (notes `:1248`).** Old text:

````text
(laboratory technicians at sea/fieldwork). `docs/references/agreement-cells-reverification-research.md` C2; QUAL-227. |
````

New text:

````text
(laboratory technicians at sea/fieldwork). `docs/references/agreement-cells-reverification-research.md` C2; QUAL-227. **S147 verdict (2026-10-29, ADR-043 D7, D8, ACCEPTED):** the 2.0 rate retires; Sunday is treated as Saturday (§ 13's +25 % and § 14's +50 % for ordered or rostered work). No lines from S148 stage 1; replaced in stage 2. |
````

**12l. SR-HK-OK24-014 HolidayRate (notes `:1268`).** Old text:

````text
pays 5 June at 2.0 today. `docs/references/agreement-cells-reverification-research.md` C1, C2; QUAL-227. |
````

New text:

````text
pays 5 June at 2.0 today. `docs/references/agreement-cells-reverification-research.md` C1, C2; QUAL-227. **S147 verdict (2026-10-29, ADR-043 D7, D9, ACCEPTED):** the 2.0 rate retires; ordered or rostered work on a søgnehelligdag earns § 13's +25 % and § 14's +50 %. One date-keyed calendar: Grundlovsdag and 24 December are not public holidays, and keep only their +25 % windows from 12:00 and 14:00. No lines from S148 stage 1; replaced in stage 2. |
````

**12m. SR-HK-OK24-020 EmployeeCompensationChoice (notes `:1388`).** Old text:

````text
save-time-off agreement from 1 April 2026. `docs/references/agreement-cells-reverification-research.md` C7; QUAL-221. |
````

New text:

````text
save-time-off agreement from 1 April 2026. `docs/references/agreement-cells-reverification-research.md` C7; QUAL-221. **S147 verdict (2026-10-29, ADR-043 D8, ACCEPTED):** the field and its routes retire; the employer chooses, time off first, and the employee keeps two elections of their own (agreeing to postpone an earning's time off; choosing saved days at the deadline). Built in S148 stage 2. |
````

**12n. SR-PROSA-OK24-001 the "mirrors HK" bundle (notes `:1728`).** Old text:

````text
(replacing 079-25, same text in the relevant clauses). `docs/references/agreement-cells-reverification-research.md` C2, C7. |
````

New text:

````text
(replacing 079-25, same text in the relevant clauses). `docs/references/agreement-cells-reverification-research.md` C2, C7. **S147 verdict (2026-10-29, ADR-043 D4, D7, D8, ACCEPTED):** the bundle follows the HK verdicts above. PROSA pay groups 1–2 earn overtime at +50 % and groups 3–4 merarbejde, as the overtime regime HR records per person decides (whether groups 3–4 who do not literally meet § 8 stk. 3 fall back to overtime is an expert question, ADR-043 provisional item 17); PROSA's printed "1:1" becomes +50 %. |
````

---

## Block 13 — docs/ARCHITECTURE.md, where configs come from; payroll's read contract · AT ACCEPTANCE (now)

**Target:** `docs/ARCHITECTURE.md:61`, `:86`, after `:125`, after `:155` (the diagram holding `:150`), `:195-196` (ADR-043 D12, D10, D3). Today the Rule Engine reads `CentralAgreementConfigs` for every calculation (QUAL-233; `RuleRegistry.cs:446-449` refuses an agreement it holds no compiled config for; verified at `93698d1f`, the file unchanged at `3c00fc88`), so each correction says what is true today and what is decided. `ARCHITECTURE.md` has no sentence on payroll's read-only contract today (verified: no match for `ProjectionHoursReader`, "read-only" or "cross-context"), so 13c adds one rather than editing one.

**13a.** Old text:

````text
- **Config/** -- `CentralAgreementConfigs` (static source of truth), `PositionOverrideConfigs`
````

New text:

````text
- **Config/** -- `CentralAgreementConfigs`, `PositionOverrideConfigs`: the static configs that seed the database. Today the Rule Engine also reads them directly for every calculation (QUAL-233). Decided (ADR-043 D12, 2026-10-29): payroll passes the database configs in the engine's request, and the static configs stay a seed and a defence-in-depth fallback at most; built in S148's config stage
````

**13b.** Old text:

````text
- **Config/** -- `AgreementConfigProvider` (delegates to `CentralAgreementConfigs`)
````

New text:

````text
- **Config/** -- `AgreementConfigProvider` (delegates to `CentralAgreementConfigs`; today every rule evaluation takes its config from here. Decided, ADR-043 D12: the engine takes the configs payroll resolved and passed in the request; built in S148's config stage)
````

**13c.** A new bullet after `:125` (the read-only contract, ADR-016 D10 `:179`; `ProjectionHoursReader.cs:7-25`, verified).

Old text:

````text
- **RetroactiveCorrectionService** -- OK version split recalculation, correction export with HC|/C|/TC| prefixes ([ADR-013](knowledge-base/decisions/ADR-013-retroactive-corrections-single-period-no-cascade.md))
````

New text:

````text
- **RetroactiveCorrectionService** -- OK version split recalculation, correction export with HC|/C|/TC| prefixes ([ADR-013](knowledge-base/decisions/ADR-013-retroactive-corrections-single-period-no-cascade.md))
- **ProjectionHoursReader** -- the one Payroll type allowed to name the Backend-owned hours repositories: payroll reads `time_entries_projection` and `absences_projection` read-only and never writes them (ADR-016 D10's read-only cross-context contract, on the ADR-034 D4 precedent). Decided (ADR-043 D10, D3, 2026-10-29): the contract grows to a third table, Skema's work intervals (`work_time_projection`), and then to the records of decided hours, each frozen by value into the calculation's manifest, with `ProjectionHoursReader` staying the one Payroll type that names the Backend repositories; built in S148 stage 2
````

**13d.** A note under the dependency diagram (`:148-155`, which holds `:150`); the diagram is unchanged.

Old text:

````text
                      └── UI (Frontend)
```
````

New text:

````text
                      └── UI (Frontend)
```

*Note (S147, 2026-10-29).* The Config layer holds the static seed configs. Today it is also what the Rule Engine reads for every calculation (QUAL-233). Under ADR-043 D12 (decided; built in S148's config stage), payroll resolves the database configs for each day and passes them in the engine's request, so the static configs become seed data and a defence-in-depth fallback at most.
````

**13e.** Old text:

````text
- Static `CentralAgreementConfigs` used as seed data and defense-in-depth fallback
- Config merging happens at the service layer (`ConfigResolutionService`), never inside the rule engine
````

New text:

````text
- Static `CentralAgreementConfigs` seed the database. **Today the Rule Engine still reads them directly for every calculation, so the database configs reach screens and booking but not pay (QUAL-233).** Decided (ADR-043 D12, 2026-10-29; ADR-014 on the pay path): for each day of a month, payroll resolves the published agreement config, the position override and the institution's local profile in force that day, passes them to the pure engine in the request, and freezes every value it used into the calculation's manifest; the static configs remain a seed and a defence-in-depth fallback at most. Built in S148's config stage
- Config merging happens at the service layer (`ConfigResolutionService`), never inside the rule engine
````

---

## Block 14 — ADR-014, an implementation note · AT ACCEPTANCE (now)

**Target:** `docs/knowledge-base/decisions/ADR-014-agreement-configs-database-backed.md`, appended after the last line (`:74`) (ADR-043 D12; owner D-6 = (a)).

Old text:

````text
- **Test & QA Agent**: Verify seed migration produces identical configs to current static. Verify lifecycle transitions. Verify rule engine determinism unaffected.
````

New text:

````text
- **Test & QA Agent**: Verify seed migration produces identical configs to current static. Verify lifecycle transitions. Verify rule engine determinism unaffected.

## S147 note (2026-10-29) — ADR-043 D12 puts this decision on the pay path

**In plain words.** This ADR decided that the database, not the code, holds the agreement configs, and that the rule engine receives resolved configs as data. That was built for the screens and for booking, never for pay: the Rule Engine still reads the configs compiled into the code, and refuses an agreement it holds none for (`RuleRegistry.cs:446-449`), while payroll registers the database resolver (`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:147`) and never calls it (QUAL-233). ADR-043 (ACCEPTED 2026-10-29; owner ruling D-6 = (a), "Editors reach pay") confirms this ADR and implements it on the pay path. For each day of a month payroll resolves the published agreement config, the position override and the institution's local profile in force that day (central → position → local) at the service layer, and passes them to the pure engine in the request; the engine never reads a database (ADR-002).

**Implementation notes** (decided; built in S148's config stage):
- Agreement configs and position overrides gain an "applies from" date (QUAL-182). The lifecycle above records when a config was published, not from which day it applies.
- Every value a calculation used is frozen into its manifest, replacing ADR-016 D10's fingerprint of configs as code.
- A publish never changes an exported month. A past "applies from" date reaches an exported month only through an explicit correction (ADR-013, ADR-034).
- `CentralAgreementConfigs` stays a seed and a defence-in-depth fallback at most.
- Until the stage lands, the agreement editor and the local-profile editor say "affects screens and booking; does not yet affect pay" (S148 stage 0).
- The Agent Guidance line "Payroll Integration Agent: No changes if configs are resolved before reaching payroll services" did not hold: payroll resolves the configs itself, dated per day.
````

---

## Block 15 — ADR-016 D10, what the rebuild changes in the replay contract · AT ACCEPTANCE (now)

**Target:** `docs/knowledge-base/decisions/ADR-016-temporal-period-handling.md`, inside D10, before its closing "**Migration**" paragraph (`:194`) (ADR-043 D10, D3, D12, the stage map, "Frozen by value").

Old text:

````text
**Migration**: `CREATE TABLE segment_manifests` + indexes only. **No** `ALTER TABLE payroll_export_lines` — that table does not exist in the schema today. **No backfill** required (no per-line column was added).
````

New text:

````text
**D10 note 2026-10-29 (S147; ADR-043 ACCEPTED, its D3, D10, D12 and its stage map) — what the overtime/flex rebuild changes in this contract. Decided; built in S148 stage by stage; until each stage lands, the text above describes the code.** In plain words: the replay proof survives the rebuild and covers more. Payroll will read more of the Backend's records, and every one of them, with every configured value, must be stored with the calculation so that a replay can re-prove it.
- **The marker moves once per stage.** The evaluation-model marker moves at each stage that changes payroll's arithmetic or the replay contract. Proposed names: `weekly-timeline-v2` (S147's sick-day credit, TASK-14708), `weekly-timeline-v3` (the config stage), then `monthly-norm-v1`, `-v2`, `-v3` (stages 1–3); the new core is not weekly, so it gets a new name. S148's refinement fixes the names. A stage-0 item moves the marker only if it changes the engine's arithmetic or the replay contract.
- **The read-only contract grows.** Skema's work intervals (`work_time_projection`) join the two hours tables in stage 2 (ADR-043 D10), and the records of decided hours (D3) join in the same stage. `ProjectionHoursReader` stays the one Payroll type that names the Backend repositories.
- **Frozen by value where a row cannot be pinned by id.** `work_time_projection` keeps one row per employee and day, overwritten on each save, so its intervals are frozen by value into the manifest and join the digest; so are a decided record's state at calculation time, the overtime movements (D8), the configs (D12) and customary days (D9). Inputs keyed by an immutable event id and superseded rather than removed may stay pinned and digested.
- **Configs are frozen, not fingerprinted (D12).** "Freeze what is data, fingerprint what is code" gives way for configs: agreement configs, position overrides and local profiles become data payroll resolves per day, and every value a calculation used is frozen into its manifest. The fingerprint contract above and the refusal `replay-config-changed` change meaning or retire (S148's config stage).
- **The frozen pieces gain two facts:** the overtime regime and flex membership, both recorded by HR per person and resolved as of each day (ADR-040).
- **The digest's canonical forms:** if intervals are digested, an interval line form is added; if the legacy time-entry route's voluntary flag retires, the entry line form loses `VoluntaryUnsocialHours`. S148 fixes both shapes.

**Migration**: `CREATE TABLE segment_manifests` + indexes only. **No** `ALTER TABLE payroll_export_lines` — that table does not exist in the schema today. **No backfill** required (no per-line column was added).
````

---

## Block 16 — ADR-017, the profile becomes the home of local flex rules · AT ACCEPTANCE (now)

**Target:** `docs/knowledge-base/decisions/ADR-017-local-agreement-configuration-as-a-profile.md`, appended after the last line (`:409`) (ADR-043 D6, D9, D3, D1, D12).

Old text:

````text
- [src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs](../../../src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs) — `BuildPlanForLegacyCallers` (extended by D9c).
````

New text:

````text
- [src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs](../../../src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs) — `BuildPlanForLegacyCallers` (extended by D9c).

## S147 note (2026-10-29) — ADR-043 amends this ADR

**In plain words.** The local profile becomes the home of each institution's own flex rules, and it will change pay. ADR-043 (ACCEPTED 2026-10-29) decides, for S148 to build:
- **Typed, dated, audited flex columns on the profile** (ADR-043 D6): the settlement period, the cap-test period, the plus, minus and daily credit caps, the action above the cap, the leaving action, office hours (a clock window per weekday) and the paid-absence credit basis, resolved for each day and frozen into the calculation's manifest. Not `local_configurations`, which this ADR retired, and not a third table. The dormant `flex_carryover_max` retires or becomes the cap-test period's form. (The S148 build input proposes that the legacy `max_flex_balance` column and its resolver branch retire when the new plus-cap column is mapped; S148's refinement decides.)
- **A customary-days table beside the profile** (ADR-043 D9): an institution's paid days off and closure days, dated and audited, because they are a list of days, not a value.
- **`overtime_requires_pre_approval` retires** (ADR-043 D3): every overtime hour now needs a decision, so an optional switch has no meaning. `max_overtime_hours_per_period` survives only as an optional warning over decided hours.
- **The `weekly_norm_hours` override and D9a's Monday alignment rule lose their premise** under ADR-043 D1's monthly norm, since no sampled agreement changes the norm's hours. Retiring them is an expert question (ADR-043 provisional item 18), not decided here.
- **A local profile changes pay** (QUAL-204, answered by ADR-043 D12): payroll reads the profile in force on each day, not the current one.

Built in S148's config stage (the columns, the table, the editor) and stage 1 (their pay effect).
````

---

## Block 17 — ADR-024, D1/D2 seed the overtime regime; D7 goes to the payroll path · AT ACCEPTANCE (now)

**Target:** `docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md`, appended after the last line (`:608`) (ADR-043 D4, D3; owner F-8 = (b), F-2 = (c)). It covers D1/D2 (`:28-73`), D7's status (`:554`), point 3's "Approval page" (`:220`) and the "Bug #4 seed flip" (`:228`, `:254`).

Old text:

````text
| **Resumption** | Post-launch architectural sprint authoring complete caller census + cross-process config-delivery shape (3 options enumerated above). |
````

New text:

````text
| **Resumption** | Post-launch architectural sprint authoring complete caller census + cross-process config-delivery shape (3 options enumerated above). |

## S147 note (2026-10-29) — ADR-043 amends D1/D2 and D7

**In plain words.** Two designs this ADR left dormant become live parts of the overtime rebuild, in changed form. ADR-043 (ACCEPTED 2026-10-29) decides, for S148 to build:
- **D1/D2: the per-category overrides become the seed of the overtime regime** (ADR-043 D4; owner ruling F-8 = (b)). HR records each person's *overtime regime* as a dated, audited attribute on the employee, resolved as of each day (ADR-040) and frozen into the calculation's manifest. For AC its values are D2's three states, all still provisional: CONTRACTUAL (may raise a merarbejde claim), DISCRETIONARY (no claim in StatsTid; any one-off payment is decided and paid outside it), NONE. The seeded categories (`docker/postgres/init.sql:2525-2532`: Fuldmægtig CONTRACTUAL, Specialkonsulent DISCRETIONARY, Chefkonsulent and Kontorchef NONE) are what HR starts from, not a second source pay reads. From S148 stage 1 no AC hour earns automatic merarbejde (AC extra work is flex only until stage 3's claims, which the regime gates), so the chefkonsulent / kontorchef merarbejde-loss case deferred above no longer arises in its automatic form. How the per-person regime relates to the D1+D2 cutover's resumption criteria (a config-delivery shape for every rule-evaluate caller) is S148's refinement's to state.
- **D7: from dormant design to the payroll path, reshaped** (ADR-043 D3; owner ruling F-2 = (c)). The distinction between an approval given beforehand and one given afterwards is kept. The acknowledgement endpoint of point 2, never built, is built naming clock spans rather than time-entry ids; the records gain a creator column; the "Approval page" of point 3 is Teamoversigt, where month approvals live since S87. The `overtime_pre_approvals` budget record changes shape (an order or a mark names a date and a clock span), with a reseed. The dormant plumbing under "What stays in place" is the starting point.
- **The "Bug #4 seed flip" resolves by retirement.** Every overtime hour now needs a decision, so `OvertimeRequiresPreApproval` retires (ADR-043 D3) and there is nothing left to flip.

Built in S148: the regime in the config stage (the build input's placement; S148's refinement confirms), D7's successor in stage 2.
````

---

## Block 18 — ADR-032 D7, the holiday limitation ends · AT ACCEPTANCE (now)

**Target:** `docs/knowledge-base/decisions/ADR-032-vacation-consumption-correctness.md:47`, a nested note under the bullet (ADR-043 D2, D9).

Old text:

````text
- **Weekday public holidays consume.** The norm path has no holiday awareness (`DanishPublicHolidays` is used only in `SupplementRule`/`ApprovalEndpoints`); vacation booked on e.g. Grundlovsdag resolves a positive norm and consumes. Pre-existing blind spot, documented not silently inherited; future holiday-calendar norm feature.
````

New text:

````text
- **Weekday public holidays consume.** The norm path has no holiday awareness (`DanishPublicHolidays` is used only in `SupplementRule`/`ApprovalEndpoints`); vacation booked on e.g. Grundlovsdag resolves a positive norm and consumes. Pre-existing blind spot, documented not silently inherited; future holiday-calendar norm feature.
  - **S147 note (2026-10-29): this limitation ends under ADR-043 D2 and D9 (ACCEPTED 2026-10-29); built in S148 stage 1.** In plain words: a vacation day booked on a public holiday will no longer use up a vacation day. A vacation or særlige-feriedage day booked on a søgnehelligdag consumes nothing, and neither does one on the credited part of an institution's paid customary day. Grundlovsdag, this bullet's example, is not a public holiday (ADR-043 D9; `docs/references/agreement-cells-reverification-research.md` C1): without a recorded custom it is an ordinary working day, and a vacation day on it consumes as any other. The vacation divisor reads the one served "planned hours" figure for the day (ADR-043 D2), which equals today's per-day norm wherever an institution sets no office hours.
````

---

## Block 19 — docs/knowledge-base/INDEX.md, ADR-043's row → ACCEPTED · AT ACCEPTANCE (now)

**Target:** `docs/knowledge-base/INDEX.md:51`. The columns are ID · Title · Status · Sprint · Domains · Tags. (The Tag Index and Domain Index sections do not list ADR-040 … ADR-043 today; not touched here.)

Old text:

````text
| [ADR-043](decisions/ADR-043-overtime-flex-monthly-norm.md) | **Overtime and flex: a monthly norm, flex by default, overtime only for ordered hours (PROPOSED).** The direction the owner ruled at S146 OQ-3 = (b), grounded in two verified research dossiers (V1–V12, C1–C7). StatsTid pays overtime automatically on every hour above a *weekly* norm, in 50/100 % tiers no source supports, and adds the same hour to flex. The state sources give HK/PROSA a calendar-month norm with weekday public holidays subtracted, overtime only for ordered or necessary work at a flat +50 %, surplus hours to flex in a separate account, AC merarbejde as a claim the employer decides, above-cap flex forfeited by default, one +25 % unsocial-hours supplement and the employer's compensation choice. D1–D9 set the direction; it retires S145's weekly settlement (F1) machinery and keeps the per-day norm, payroll reading its own hours, the replay proof, split months and the S146 flex ledger. Nine forks (F-1..F-9) go to the owner in S147's model refinement, which is also its two-lens review; acceptance is S147's | **PROPOSED** — drafted S146 D5, reviewed by the Fable seat (R-40..R-46); acceptance at S147 | S146 (draft) → S147 | Rule Engine, Payroll Integration, Backend, Data Model, SharedKernel, Frontend, Test | overtime, merarbejde, flex, norm-period, monthly-norm, public-holidays, supplements, compensation, ordered-work, forfeiture |
````

New text:

````text
| [ADR-043](decisions/ADR-043-overtime-flex-monthly-norm.md) | **Overtime and flex: a monthly norm, flex by default, overtime only for ordered hours.** An hour worked above the month's norm is a flex hour unless a leader ordered or accepted it; only those hours become overtime (HK, PROSA pay groups 1–2, a flat +50 %) or count toward an AC merarbejde claim the employer decides, and they leave flex, so one hour is never both. Grounded in three verified dossiers (V1–V12, C1–C7, R1–R5). S146 set the direction (OQ-3 = (b)); on 2026-10-29 the owner decided its nine forks and three tender findings, each on the recommended option: **D1** a calendar-month norm, lowered by weekday søgnehelligdage; **D2** surplus hours to flex, a day of paid absence credited as the day planned, HR recording who is on flex; **D3** decided hours only (orders and rosters beforehand, employee marks accepted afterwards in Teamoversigt; four-eyes on four conditions; late decisions as movements; office hours opt-in); **D4** a flat +50 %, the HR-recorded overtime regime, part-timers +50 % above their own norm (provisional, FV2025-795); **D5** AC merarbejde as a claim (stage 3); **D6** no central flex cap, each institution's own flex rules, flex above a cap forfeited and recorded; **D7** one +25 % supplement and § 14, only for ordered or rostered placements; **D8** an overtime account of dated movements with § 10's 3-month clock, the employer choosing, time off first; **D9** one holiday calendar with three kinds of day (Grundlovsdag is not a holiday) and customary days per institution; **D10** payroll and the rest check read Skema's clock intervals; **D11** on-call, call-ins and travel refused first, then three facts per type; **D12** configured values reach pay, dated and frozen (ADR-014 on the pay path). Built in stages from S148 (0, config, 1, 2, 3), each with its own evaluation-model marker. Supersedes S145's Q3 = F1 and Q7 (b); amends ADR-016 D10, ADR-017, ADR-024 D1/D2/D7 and ADR-032 D7; confirms ADR-014. Twenty provisional items await a qualified expert; research R-A … R-E is owed | **ACCEPTED** 2026-10-29 (S147, O-10) — owner-ruled on the decide track; dual-lens review Codex c1–c3, Fable c1–c3 | S146 (draft) → S147 (rulings, acceptance) → S148+ (build) | Rule Engine, Payroll Integration, Backend, Data Model, SharedKernel, Frontend, Security, Test | overtime, merarbejde, flex, norm-period, monthly-norm, public-holidays, supplements, compensation, ordered-work, forfeiture, office-hours, customary-days, clock-times, worked-time-classes, local-configuration, staged-build |
````

---

## Block 20 — hr-follow-up-process-register.md, the new hand-offs · AT ACCEPTANCE (now)

**Target:** `docs/operations/hr-follow-up-process-register.md` (ADR-043 D2, D3, D4, D6, D8; its Consequences, "New hand-offs for leaders and HR, now designed"). The register asks for a row when a hand-off is *designed* (CLAUDE.md document map), so the rows are written now and say "not built". Two placements follow the register's own inclusion rule (`:55-62`): the leader's decision on marked hours is a step inside the approval flow, so it is listed with the B-rows (20g), not as a numbered row; HRP-021 stays a gap row (20e). The AC claims (D5) get their row when S148 stage 3 designs them.

**20a. The rows heading (`:91`).** Old text:

````text
## The rows — HR / admin hand-offs (15 at S139; S145 added HRP-001b and HRP-024)
````

New text:

````text
## The rows — HR / admin hand-offs (15 at S139; S145 added HRP-001b and HRP-024; S147 designed HRP-025 … HRP-029 for ADR-043, not yet built)
````

**20b. HRP-001b marked to retire with F1 (`:98`, Id cell).** Old text:

````text
| **HRP-001b** *(added S145, 2026-10-14)* |
````

New text:

````text
| **HRP-001b** *(added S145, 2026-10-14; retires with F1 in S148 stage 1, ADR-043)* |
````

**20c. HRP-024 marked to retire with F1 (`:123`, Id cell; its reason is Q3 = F1).** Old text:

````text
| **HRP-024** *(added S145, 2026-10-20)* |
````

New text:

````text
| **HRP-024** *(added S145, 2026-10-20; retires with F1 in S148 stage 1, ADR-043: no month waits for the previous one once the norm is monthly)* |
````

**20d. A new section before the gap rows (`:125`).** Old text:

````text
## Gap rows — outside the strict definition, kept so they are not lost (4)
````

New text:

````text
### Overtime and flex (ADR-043; designed S147, built from S148 — none of it is live)

*In plain words.* ADR-043 (ACCEPTED 2026-10-29) creates new things HR, or an approver, must do by hand. They are registered here as they are designed, so S148 builds each one with an owner and a place it shows up, rather than as a silent state. **None exists in code yet:** every "Surfacing today" cell reads *not built*, and each row names the S148 stage that builds it. The leader's decision on marked hours is listed with the B-rows; HRP-021, the overtime deadline, stays a gap row and is now decision-ready in design; the AC merarbejde claims (ADR-043 D5) get their row when S148 stage 3 designs them.

| Id | Process | Trigger | Accountable role | Deadline + source | Surfacing today | Aging | Resolution + audit | Class | Ready? | Inc-4 |
|----|---------|---------|------------------|-------------------|-----------------|-------|--------------------|-------|--------|-------|
| **HRP-025** *(designed S147, 2026-10-29)* | **Record a person's overtime regime** (ADR-043 D4; owner F-8 = (b)) | a hire, or a change that moves a person across a regime boundary: HK pay crossing the pay ceiling (441,037 kr. a year at the 31 March 2012 level) or a special post, a PROSA pay-group change (1–2 ↔ 3–4), an AC category change. StatsTid holds no pay, so it cannot detect the trigger | HR | **NONE stated** by any source; the value must be right before a month that depends on it is calculated | *not built* (S148 config stage; one HR editor with HRP-026 is the build input's placement) | none designed | a dated, audited attribute on the employee (HK: overtime or exempt; PROSA: overtime or merarbejde; AC: CONTRACTUAL / DISCRETIONARY / NONE, provisional), resolved as of each day (ADR-040) and frozen into the month's manifest; seeded from position, and for AC from ADR-024's per-category rows | designed workflow (not built) | **NOT READY**: no deadline source, and the trigger happens in the payroll system | the HR editor |
| **HRP-026** *(designed S147, 2026-10-29)* | **Record whether a person is on the flex scheme** (ADR-043 D2; owner F-5 = D-8a (b)) | a hire, or a change of position or local agreement that puts a person on or off flex (sampled local agreements leave out e.g. special and chief consultants, managers, flex-job staff) | HR | **NONE stated** | *not built* (S148 config stage) | none designed | a dated, audited yes/no per person, seeded from position and resolved as of each day; someone off the scheme keeps no flex balance, and no settlement or forfeiture runs for them | designed workflow (not built) | **NOT READY**: no deadline source | the same HR editor as HRP-025 |
| **HRP-027** *(designed S147, 2026-10-29)* | **A leaver's flex balance and owed overtime time off** (ADR-043 D6 R-4; D8 D-5.P9), beside HRP-011 | an employee leaves with a flex balance other than 0, or with overtime time off still owed | HR for the leaver's final month (OQ-2), as HRP-011 | **the leaving month**; its cutoff is HRP-011's manager deadline, month-end + 5 (owner ruling 2026-09-08, provisional) | *not built* (S148 stage 1 for flex; stage 2 for owed time off) | none designed | the leaving action settles flex as a dated, recorded movement in the leaving month, never silently: by default a positive balance not taken during notice is forfeited and shown, and a negative one is recorded and shown to HR, not deducted (a pay deduction is a local option; ADR-043 provisional item 8). Owed overtime time off is paid in the leaving month (a labelled default; research R-C) | designed workflow (not built) | **READY by inheritance** from HRP-011 (role and deadline known) | the termination checklist (HRP-011's input) gains "flex settled; owed time off paid" |
| **HRP-028** *(designed S147, 2026-10-29)* | **Enter an opening flex balance** (ADR-043 D6 R-5; QUAL-225) | a new employee brings a flex balance from another state employer, or a month whose flex was not tracked needs an opening | HR | **NONE stated** | *not built*; S148's refinement schedules it (stage 1 or later) | none designed | HR enters the balance at the ledger start with a reason; an event and an audit row | designed workflow (not built) | **NOT READY**: no deadline source | an HR action on the employee's flex view |
| **HRP-029** *(designed S147, 2026-10-29)* | **Grant a flex cap exception** (ADR-043 D6, R-2 amendment (v)) | an employee's flex is above the institution's cap, and the default (forfeit, recorded) should not apply | someone with approval authority over that person (the month approver, a stand-in, or HR), never on their own balance (403) | **NONE stated**; in effect before the month is calculated, since the calculation otherwise records the default forfeiture (an inference, not a source) | *not built* (S148 stage 1, with forfeiture) | none designed | a dated record for one person, with its decider and an audit row; the month's calculation records either the forfeiture or the exception | designed workflow (not built) | **NOT READY**: no deadline source | the forfeiture line on the employee's and HR's flex view |

## Gap rows — outside the strict definition, kept so they are not lost (4)
````

**20e. HRP-021 made decision-ready in design (`:131`).** Old text:

````text
| **HRP-021** | **HK Stat afspadsering 3-month conversion to payment** — comp-time-off not arranged within 3 months → right to cash payment; **no code counterpart**: no expiry field in `OvertimeBalanceRepository.cs`, `CompensationModel.cs`, `OvertimeBalance.cs` | `docs/references/danish-agreements.md:108` | deadline stated (agreement) but the accountable party is not named in the agreement text | **NOT READY** — Phase B: who "arranges" afspadsering under HK Stat |
````

New text:

````text
| **HRP-021** | **Overtime time off due within 3 months, else paid** (HK and PROSA § 10; § 14 weekend compensation by the end of the following norm period) — **no code counterpart yet**: no expiry field in `OvertimeBalanceRepository.cs`, `CompensationModel.cs`, `OvertimeBalance.cs`, and the yearly balance is never fed | `docs/references/danish-agreements.md:108`; ADR-043 D8 (D-5.P4, D-5.P7) | was: the accountable party is not named in the agreement text. **S147 (2026-10-29), ADR-043 D8, decided in design:** *trigger* an earning's deadline approaching or passed; *accountable* the employee's month approver (the employer arranges time off, PAV 19.2.7.1); *surfacing* a leaders' "time off due" list and the employee's account screen, each earning with its deadline; *resolution* time off granted, pay by default at the deadline, a postponement both parties sign, or a saved-days election, each audited; the default pay is computed in the month's calculation and shown on the payslip, never silently. Still outside the strict definition until built, because the resolver is the approver, not HR | **DECISION-READY in design** (deadline: § 10, 3 months from the end of the month worked; accountable: the month approver). Built in S148 stage 2 with the overtime account; re-read then, and moved to the main table if the built shape leaves an HR step |
````

**20f. HRP-018 re-pointed (`:151-156`).** Old text:

````text
- **HRP-018 — overtime pre-approval**: the resolver is the **leader** — request `OvertimeEndpoints.cs:147-240`,
  list `:300-355`, approve `:359-435`, reject `:439-508`, all `LeaderOrAbove` → **B-row** (Codex BLOCKER 2). The
  finding stands as a UX/QUAL defect: a finished, unit-tested worklist page (`OvertimePreApprovalManagement.tsx:1-167`,
  PENDING / APPROVED / REJECTED tabs, Godkend / Afvis actions) has **no route in `App.tsx`, no sidebar link, and never
  had one** (`git log -S OvertimePreApprovalManagement -- frontend/src/App.tsx`: empty) → QUAL row at close;
  Increment-4 input: route it under its intended tier.
````

New text:

````text
- **HRP-018 — overtime pre-approval**: the resolver is the **leader** — request `OvertimeEndpoints.cs:147-240`,
  list `:300-355`, approve `:359-435`, reject `:439-508`, all `LeaderOrAbove` → **B-row** (Codex BLOCKER 2). The
  finding stands as a UX/QUAL defect: a finished, unit-tested worklist page (`OvertimePreApprovalManagement.tsx:1-167`,
  PENDING / APPROVED / REJECTED tabs, Godkend / Afvis actions) has **no route in `App.tsx`, no sidebar link, and never
  had one** (`git log -S OvertimePreApprovalManagement -- frontend/src/App.tsx`: empty) → QUAL row at close;
  Increment-4 input: route it under its intended tier.
  **S147 (2026-10-29), ADR-043 D3:** the record this page manages, an hours budget for a period that names no hours,
  changes shape in S148 stage 2 (orders and roster spans naming a date and a clock span; employee marks decided in
  month approval), with a reseed, so routing the page as built is moot. Its successor on the leader side is the
  decision step on marked hours inside month approval (B-rows, below); whether the order record's leader screen
  reuses this page is S148's. HRP-018 stays relocated as a B-row.
````

**20g. The leader's decision step, listed with the B-rows (`:172-178`).** Old text:

````text
Employee registers (`TimeEndpoints.cs:24`, Skema saves) → submits (`ApprovalEndpoints.cs:159`, `employee-approve
:1411`) → leader approves / rejects / reopens (`:199, :421, :1478`; `TeamOversigt.tsx`); **overtime pre-approval**
request / list / approve / reject / compensate (`OvertimeEndpoints.cs:147, 300, 359, 439, 512` — `LeaderOrAbove`;
see HRP-018 above for the unrouted page); the leader's own vikariering self-service (`DelegationPage.tsx`);
`PeriodSubmitted / Approved / Rejected / EmployeeApproved / Reopened` events; `approval_periods.status` DRAFT →
SUBMITTED → APPROVED / REJECTED (`ApprovalPeriodRepository.cs`, the status writers); `compensatory_rest.status`
PENDING → GRANTED (`CompensatoryRestRepository.cs:71` — dead, HRP-020).
````

New text:

````text
Employee registers (`TimeEndpoints.cs:24`, Skema saves) → submits (`ApprovalEndpoints.cs:159`, `employee-approve
:1411`) → leader approves / rejects / reopens (`:199, :421, :1478`; `TeamOversigt.tsx`); **overtime pre-approval**
request / list / approve / reject / compensate (`OvertimeEndpoints.cs:147, 300, 359, 439, 512` — `LeaderOrAbove`;
see HRP-018 above for the unrouted page); the leader's own vikariering self-service (`DelegationPage.tsx`);
`PeriodSubmitted / Approved / Rejected / EmployeeApproved / Reopened` events; `approval_periods.status` DRAFT →
SUBMITTED → APPROVED / REJECTED (`ApprovalPeriodRepository.cs`, the status writers); `compensatory_rest.status`
PENDING → GRANTED (`CompensatoryRestRepository.cs:71` — dead, HRP-020).

**Added S147 (2026-10-29), ADR-043 D3; built in S148 stage 2.** The leader's **decision on marked hours**: accept or
reject the hours an employee marks in Skema as ordered or necessary, inside month approval in Teamoversigt, before the
month is approved (the month cannot be approved while a mark is undecided); and the leader's **order or roster span**,
recorded beforehand. Decided by whoever holds the month's approval authority, never on their own hours and never on a
mark they raised. A B-row by this register's rule (a leader step inside the §H flow): an undecided mark keeps the month
unapproved, so a late decision surfaces as HRP-012's month past its deadline. This is the listing ADR-043's
Consequences call "the leader's decision step on marked hours (D3)".
````

---

## Block 21 — security-finding-register.md, SEC-056 · AT S147 CLOSE

**Target:** `docs/operations/security-finding-register.md:216` (batch-3 ruling 4(b); batch-2 ruling 1; D-5.P2; QUAL-239). Four cell-level replacements, each unique (`grep -cF` = 1). **Condition:** 21b assumes T9 (TASK-14705) has merged; it is not merged at `3c00fc88`. If it has not merged by close, use 21b's fallback. If T9's merge moves `OvertimeEndpoints.cs` lines, re-point the row's line citations at the same time.

**21a. "What it means", the last sentence.** Old text:

````text
ADR-043 F-2/F-3 (S147's rulings round) redesign this workflow for S148.
````

New text:

````text
ADR-043 D3 and D8 (ACCEPTED 2026-10-29) redesign this workflow for S148. **S147 correction (batch-3 ruling 4(b)):** the compensate half is a latent shape, not a live capability. Nothing in production feeds the yearly balance (`AdjustAccumulatedAsync`, `OvertimeBalanceRepository.cs:50`, has no production caller), so compensate refuses (404 or 400) before it touches a balance; its discarded update result is QUAL-239. The High label rests on approve and reject.
````

**21b. Status.** Old text:

````text
REGISTERED (S147 refinement, 2026-10-27); known — should-be-revisited; the S147 self part pending owner question Q-A
````

New text (T9 merged):

````text
REGISTERED (S147 refinement, 2026-10-27). **Self part CLOSED in S147 by T9** (TASK-14705, merged `<sha>`; owner Q-A = (a), 2026-10-28): approve, reject and compensate refuse when the actor acts on themselves. **Creator-not-approver and compensation halves closed in design** by ADR-043 (ACCEPTED 2026-10-29), built in S148 stage 2; known — should-be-revisited until built
````

Fallback (T9 not merged by close):

````text
REGISTERED (S147 refinement, 2026-10-27). Self part: owner Q-A = (a) (2026-10-28), T9 (TASK-14705) not merged in S147, carried to S148. **Creator-not-approver and compensation halves closed in design** by ADR-043 (ACCEPTED 2026-10-29), built in S148 stage 2; known — should-be-revisited until built
````

**21c. Source of truth.** Old text:

````text
ADR-043 F-2 (`:255-270`), F-3 (`:272-278`); `.claude/reviews/REFINEMENT-s147-adjudication-c4.md` R-36
````

New text:

````text
ADR-043 D3 (four-eyes on four conditions), D8 (compensation only behind decided hours); `.claude/reviews/REFINEMENT-s147-adjudication-c4.md` R-36; `.claude/reviews/S147-D-batch2-adjudication.md` ruling 1; `.claude/reviews/S147-D-batch3-adjudication.md` ruling 4; QUAL-239
````

**21d. Adjudication.** Old text:

````text
→ S148's redesign (ADR-043 F-2/F-3) builds four-eyes in: a creator-not-approver rule (a creator column; reseed), a self-decision guard, and compensation only behind an approved movement. If the owner chooses Q-A (a), S147 adds the self guards on approve, reject and compensate (T9) meanwhile.
````

New text:

````text
→ Closed in design by D-4 = (c) with ruling 1 and by D-5.P2; T9 closes the self part in S147. S148 stage 2 builds four-eyes in: a creator column with creator-not-approver on every record that awaits a decision (reseed); no decision on the decider's own hours (403) on orders, mark decisions, compensations and cap exceptions; the authority to order or decide is the month-approval authority, not bare organisation scope; compensation only behind decided earned movements. Cross-reference: QUAL-239 (the compensate route commits an event its guarded update refused; interim refusal in S148 stage 0, retires with the route in stage 2).
````

---

## Block 22 — quality-finding-register.md, the close half: QUAL-231 and QUAL-237 · AT S147 CLOSE

**Target:** `docs/operations/quality-finding-register.md:455` (QUAL-231) and `:461` (QUAL-237), the Adjudication cell (batch-3 ruling 3; ADR-043 D2). If TASK-14708's merge record rewrites QUAL-231's row first, append to whatever its Adjudication cell then ends with.

**22a. QUAL-231.** Old text:

````text
Nearest precedent: QUAL-222. |
````

New text:

````text
Nearest precedent: QUAL-222. **S147 close (ADR-043 D2, batch-3 ruling 3):** the residual, a partly worked day of paid absence, closes with R-7 as widened, in S148 stage 0: *"A day's paid-absence hours, of any credit-class type, may not exceed the credit basis minus the hours worked that day; judged at the Skema save in both directions (QUAL-237)"*. |
````

**22b. QUAL-237.** Old text:

````text
Pin it with two saves in a row (the `admin01` shape). |
````

New text:

````text
Pin it with two saves in a row (the `admin01` shape). **S147 close (ADR-043 D2, batch-3 ruling 3):** scheduled for S148 stage 0 (no longer backlog), in R-7's widened form: *"A day's paid-absence hours, of any credit-class type, may not exceed the credit basis minus the hours worked that day; judged at the Skema save in both directions (QUAL-237)"*. Both directions means a paid-absence save on a day that already holds worked hours, and a later work save on a day that already holds paid-absence hours, each judged against the day's stored state. |
````

---

## Block 23 — quality-finding-register.md, the acceptance half: QUAL-204 answered; the routes · AT ACCEPTANCE (now)

**Target:** `docs/operations/quality-finding-register.md`. Each part appends to the row's Adjudication cell (QUAL-204 also its Status cell). Each Old text is unique (`grep -cF` = 1).

**23a. QUAL-182 (`:405`).** Old text:

````text
a no-write pin under the live rule set). Revisit, not shield |
````

New text:

````text
a no-write pin under the live rule set). Revisit, not shield. **ADR-043 (ACCEPTED 2026-10-29), D12:** that day is decided: agreement configs and position overrides gain an "applies from" date in S148's config stage, which must wire their boundary source as this row says; the row closes when it does. |
````

**23b. QUAL-202 (`:426`).** Old text:

````text
Revisit when the time-entry form is next touched |
````

New text:

````text
Revisit when the time-entry form is next touched. **ADR-043 (ACCEPTED 2026-10-29), D11:** S148 stage 0 makes `POST /api/time-entries` answer 422 for any activity type other than `NORMAL`, and payroll refuses any stored entry of another type by name; the closed set of types, with three facts per type and agreement (a worked-time fraction, a supplement basis, a rest flag), is built in a later stage (stage 2 or later). Closes with QUAL-232. |
````

**23c. QUAL-204 (`:428`), the Status cell.** Old text:

````text
A product question for a later refinement; no fix routed
````

New text:

````text
**ANSWERED 2026-10-29: yes, a local profile changes pay** (owner D-6 = (a), "Editors reach pay"; ADR-043 D12). Built in S148's config stage (payroll resolves the profile in force on each day and freezes its values into the manifest) and stage 1 (the flex values' pay effect)
````

**23d. QUAL-204 (`:428`), the Adjudication cell.** Old text:

````text
how payroll applies them dated and how replay freezes them. |
````

New text:

````text
how payroll applies them dated and how replay freezes them. **Answered by ADR-043 D12 (ACCEPTED 2026-10-29):** payroll resolves central → position → local for each day at the service layer, passes the result to the pure engine, and freezes every value it used into the manifest by value, not by fingerprint. Closes when S148's config stage lands. |
````

**23e. QUAL-219 (`:443`).** Old text:

````text
the leaver's last closing is an ADR-043 F-5 sub-question. |
````

New text:

````text
the leaver's last closing is an ADR-043 F-5 sub-question. **ADR-043 (ACCEPTED 2026-10-29), D6:** there is no central flex cap; hours above an institution's own cap are forfeited, recorded and shown (with no cap set, nothing is forfeited and the month says "no flex cap configured"); the automatic `FLEX_PAYOUT` retires, surviving only as the configured "1:1 cash by agreement" action; the leaver's balance is settled in the leaving month by the leaving action (the F-5 sub-question, option (a); HRP-027). Built in S148 stage 1. |
````

**23f. QUAL-221 (`:445`).** Old text:

````text
abolished the separate save-time-off agreement from 1 Apr 2026. `docs/references/agreement-cells-reverification-research.md` C7. |
````

New text:

````text
abolished the separate save-time-off agreement from 1 Apr 2026. `docs/references/agreement-cells-reverification-research.md` C7. **ADR-043 (ACCEPTED 2026-10-29), D8:** the employer chooses, time off first; `EmployeeCompensationChoice` and its routes retire; PROSA's "1:1" becomes +50 %; the yearly balance is replaced by a ledger of dated movements with § 10's 3-month clock (HRP-021). Built in S148 stage 2. |
````

**23g. QUAL-223 item (3) (`:447`).** Old text:

````text
a dated switchable rule whose default is an owner ruling with legal input. `docs/references/agreement-cells-reverification-research.md` C2-C4. |
````

New text:

````text
a dated switchable rule whose default is an owner ruling with legal input. `docs/references/agreement-cells-reverification-research.md` C2-C4. **ADR-043 (ACCEPTED 2026-10-29), item (3):** owner ruling D-10, F-7 = (c), default (ii), provisional pending a qualified expert: § 8 stk. 2's 1:1 band is not applied, and an ordered or accepted hour above a part-timer's own norm earns +50 % (FV2025-795's direction), held as the dated variant `PartTimeBandRule` (D4; provisional items 1–2; research R-D, R-E); built in S148 stage 2. Items (1) and (2) are routed by D7 (one +25 % supplement and § 14) and D4 (the HR-recorded overtime regime, F-8 = (b)). |
````

**23h. QUAL-225 (`:449`).** Old text:

````text
Revisit with the S147 model program's local flex parameters |
````

New text:

````text
Revisit with the S147 model program's local flex parameters. **ADR-043 (ACCEPTED 2026-10-29), D6 (R-5, batch-2 ruling 3): designed.** The ledger start gains an opening balance HR enters with a reason, an event, an audit row and an HRP row (HRP-028); it is also the receiving end of a balance transferred from another state employer. S148's refinement schedules it (stage 1 or later). |
````

**23i. QUAL-232 (`:456`).** Old text:

````text
verified against the on-call source (QUAL-215), plus a marker bump. |
````

New text:

````text
verified against the on-call source (QUAL-215), plus a marker bump. **Ruled 2026-10-29: (c) "Keep, build later" (owner D-3; ADR-043 D11, ACCEPTED).** S148 stage 0 refuses on-call, call-ins and travel (the legacy route answers 422 for any type other than `NORMAL`; payroll refuses any stored entry of another type by name). The sourced model, three facts per type and agreement (on-call 1/3 at home and 3/4 at the workplace; a call-in at least 3 h; travel in full up to 13 h per working døgn; for AC-agreement staff, travel only), is built in a later stage (stage 2 or later); on-call's 0.33 payment and the 0.5 non-working travel rate retire. |
````

**23j. QUAL-233 (`:457`).** Old text:

````text
Decide QUAL-204 and ADR-043 F-5 with it. |
````

New text:

````text
Decide QUAL-204 and ADR-043 F-5 with it. **Ruled 2026-10-29: (a) "Editors reach pay" (owner D-6; ADR-043 D12, ACCEPTED).** A config stage of its own in S148, before the first stage that needs a local value: configs resolved per day, passed in the engine's request, frozen by value into the manifest (replacing ADR-016 D10's fingerprint); agreement configs and position overrides gain an "applies from" date (QUAL-182). Until it lands the editors say "affects screens and booking; does not yet affect pay" (S148 stage 0). |
````

**23k. QUAL-234 (`:458`).** The Orchestrator's `3c00fc88` already added the T7 interim and "The row stays open until S148 stage 0 (S0-3, ADR-043 D-2 = (a))"; this part adds the decision and the supplement half. Old text:

````text
unreachable today (the only production path is the composite `Evaluate`). |
````

New text:

````text
unreachable today (the only production path is the composite `Evaluate`). **Ruled 2026-10-29: (a) "Read work intervals" (owner D-2; ADR-043 D10, ACCEPTED).** Payroll and the rest check read Skema's intervals (`work_time_projection`) for the time of day; flex and pay keep reading the hours. The rest-check half lands in S148 stage 0 (no payroll marker; the T7 interim above retires); the supplement half lands in stage 2, when payroll's read-only contract grows to three tables and the intervals are frozen by value into the manifest. One source of time of day per hour; manual hours retire (stage 0). |
````

**23l. QUAL-236 (`:460`) and QUAL-239 (`:463`).** Two replacements.

QUAL-236 Old text:

````text
the tests pinning 1/2/3 and "HK gets `_2` only" move. |
````

QUAL-236 New text:

````text
the tests pinning 1/2/3 and "HK gets `_2` only" move. **ADR-043's stage map (ACCEPTED 2026-10-29): S148 stage 0** fixes it in full (owner Q-2 = (a), 2026-10-28): the quotas per agreement and OK version, the per-episode limit, OK26's third day and the call-home day. |
````

QUAL-239 Old text:

````text
before enqueueing, and pin the race. |
````

QUAL-239 New text:

````text
before enqueueing, and pin the race. **ADR-043 (ACCEPTED 2026-10-29), D8 (R-3):** both, in order. S148 stage 0 makes the route read its guarded update's result and refuse (409) before enqueueing (the interim); stage 2 retires the route with the yearly balance. |
````

---

## Block 24 — ROADMAP.md § 1, S148 opens on the stage map · AT ACCEPTANCE (now)

**Target:** `ROADMAP.md:45-51` (arc item 4). Line `:52` (the source register's citations) stays. ROADMAP is owner-scoped architecture; this is the Orchestrator's edit at acceptance (or folded into the close, at the Orchestrator's choice).

Old text:

````text
   **Next: the overtime/flex model program, opening in S147** (owner direction S146 OQ-3 = (b), 2026-10-23).
   Two verified research dossiers (`docs/references/overtime-flex-model-research.md`,
   `docs/references/agreement-cells-reverification-research.md`) showed the overtime model wrong in kind:
   a weekly norm and automatic overtime where the state sources give a calendar-month norm, overtime only for
   ordered work at a flat +50 % and surplus hours to flex. **ADR-043 (PROPOSED)** sets the direction; S147's model
   refinement puts its forks F-1..F-9 to the owner, then accepts and builds it. Closes QUAL-189, QUAL-216..222,
   QUAL-226 and QUAL-227; retires S145's weekly settlement machinery (frozen by OQ-2, with QUAL-209/210/212/213).
````

New text:

````text
   **The overtime/flex model program: ADR-043 ACCEPTED 2026-10-29 (S147); S148 opens on its stage map.**
   Two verified research dossiers (`docs/references/overtime-flex-model-research.md`,
   `docs/references/agreement-cells-reverification-research.md`) showed the overtime model wrong in kind; in S147 the
   owner decided all nine of ADR-043's forks and three tender findings (QUAL-232..234), each on the recommended
   option. In plain words: an hour above the month's norm is flex unless a leader ordered or accepted it; only those
   hours become overtime (+50 %) or an AC claim, and they leave flex. It is built in self-contained stages, each with
   its own evaluation-model marker: **stage 0** stops paying what is wrong today (on-call, call-ins and travel refused;
   the child's-sick-day quotas fixed; the rest check reads Skema's clock times); the **config stage** makes the
   editors' values reach pay, dated and frozen; **stage 1** brings the monthly norm and flex by default, the holiday
   calendar split and recorded forfeiture; **stage 2** decided hours, +50 % overtime, the +25 % supplement and § 14,
   and the overtime account; **stage 3** AC extra-work claims. S148's refinement starts from the build input
   (`.claude/plans/s147/S148-BUILD-INPUT.md`) and decides how many stages S148 carries; a stage it cannot carry is
   pointed to here. Closes QUAL-189, QUAL-216..222, QUAL-226, QUAL-227, QUAL-232..234 and QUAL-237 when built;
   retires S145's weekly settlement machinery (frozen by OQ-2, with QUAL-209/210/212/213).
````

---

## Beyond the owed list (not applied by the blocks above)

*In plain words.* Checking each target turned up text that also contradicts ADR-043 but is not on its owed list. None is applied above. The `SYSTEM_TARGET.md` items are outside the owner's "as listed" approval, so each needs the owner's own yes; the others are the Orchestrator's call.

**X1. SYSTEM_TARGET.md lines the owed list did not foresee (owner approval needed).**
- `:47-51` (§B, "Flex Arrangements"): "Maximum saldo … Automatic payout rules". D6: no central cap; the automatic payout retires, surviving only as "1:1 cash by agreement". *Draft:* "Flex Arrangements: each institution's own caps, forfeiture above a cap (recorded), a leaver's settlement, and the exceptions a local agreement allows (§ G)".
- `:65` (§B, HK): "Overtime with 50% / 100% supplement". D4: a flat +50 %. *Draft:* "Overtime at a flat +50 %, only for decided hours (§ L)".
- `:62` (§B, AC: "Possible on-call obligations") and `:460` ("AC-Specific Requirements": "On-call obligations"). D11, as the owner was asked at D-3: for AC-agreement staff, travel only. *Draft:* strike both, or reword to "Travel time (AC: travel is the only special time type)".
- `:225-226`, `:228`, `:243` (§I's parameter list and validation): `MaxFlexBalance`, `FlexCarryoverMax` (central caps, D6), `OvertimeThreshold50/100` and "OvertimeThreshold100 must be ≥ OvertimeThreshold50" (D4), `OnCallDutyRate` (D11). *Draft:* mark each "(retires, ADR-043 D*n*; S148 stage *N*)".
- `:402` (§M): "Default model: Configurable per agreement". D8: the default is time off first; a per-agreement payout default would contradict it. *Draft:* "Default model: time off (afspadsering); pay only by the employer's recorded decision".
- `:417-418` (§M wage types): `OVERTIME_100_PAYOUT`, `OVERTIME_100_AFSPADSERING`. D8: the 100 % rows retire. *Draft:* strike, with a dated note.
- `:279-295` (§J, the voluntary flag) is a **conditional** conflict: D10 lets S148 either route the legacy route's `VoluntaryUnsocialHours` flag through the interval table or retire it with the route. Retiring it would drop a product requirement, so if S148's refinement leans "retire", that lean goes to the owner first.

**X2. danish-agreements.md `:35`, `:40-42`, `:46` (the agreement table).** `OnCallDutyRate` 0.33 and `NonWorkingTravelRate` 0.5 (D11 retires both), `MaxFlexBalance` / `FlexCarryoverMax` 100/120/150 h (D6: no central cap) and `NormPeriodWeeks` 1 (D1). *Draft:* one dated note after the table (`:46`): "S147 note (2026-10-29): ADR-043 retires four of these cells: the central flex caps (D6; there is no central state flex agreement, PAV 19.5.1), `NormPeriodWeeks` (D1, the month), on-call's 0.33 rate and the 0.5 non-working travel rate (D11, three facts per type). The values are today's code; built in S148 (stage 1; D11's later stage)."

**X3. agreement-source-register.md rows not flagged disputed in S146.** The central caps SR-AC-OK24-011/012 (`:316`, `:336`) and SR-HK-OK24-002/003 (`:1010`, `:1030`), whose notes still call the cap "the central baseline"; on-call SR-HK-OK24-016 (`:1290`); travel SR-HK-OK24-018 (`:1330`) and SR-AC-OK24-030 (`:696`); the pre-approval flip SR-HK-OK24-022 (`:1410`) and SR-PROSA-OK24-007 (`:1830`), which still read "seed flip GATED on ADR-024 D7". Under the register's own definition (`:37`, R-19), the caps are now contradicted by a verified source (PAV 19.5.1). Recommendation: an S147 verdict note on each, and `disputed?` → `true` on the cap rows.

**X4. QUAL-240 is in neither ADR-043 nor the build input.** Its row (`quality-finding-register.md:464`) says it "closes with R-7 as widened … S148 stage 0", but ADR-043's "Closes when built" names only QUAL-231's residual and QUAL-237, and the build input's stage 0 does not cite it. Options: (a) add "QUAL-240" beside QUAL-237 in the ADR copy's "Closes when built" row at Block 0 (a factual addition; no decision changes), plus a build-input pointer; (b) leave the ADR and let S148's refinement pick it up from the register. Lean (a): the row already routes itself there, and the ADR is the closing list S148 reads.

**X5. The "→ S147 model program" cells of QUAL-189, QUAL-216 … QUAL-222, QUAL-226 and QUAL-227** could gain "ADR-043 D*n*, S148 stage *N*". Not owed; the ADR's "Closes when built" already maps them.

**X6. `docs/QUALITY.md:26`** says "ADR-043 PROPOSED with nine forks": S147's close updates QUALITY.md anyway.

**X7. An observation for S148's census, not a docs item.** The Orchestrator service's `WeeklyCalculationPipeline` (`src/Orchestrator/StatsTid.Orchestrator/Services/WeeklyCalculationPipeline.cs:14`), reachable through `POST /api/orchestrator/execute` (`src/Orchestrator/StatsTid.Orchestrator/Program.cs:48`), calls `NORM_CHECK_37H`, `SUPPLEMENT_CALC`, `OVERTIME_CALC`, `ABSENCE_CALC` and `FLEX_BALANCE` (its summary, `:5-12`). ADR-043 and the build input never name it. It is not a pay path, but it is the rule-evaluate caller whose discovery suspended ADR-024's D1+D2 cutover (ADR-024 `:545`, `:568`), and stage 1 retires the weekly overtime it calls.

---

*Revision history.*
- Draft 1 (2026-10-29, begun at `93698d1f`, finished at `3c00fc88`): `planner` (Opus 5.5). Every owed line of ADR-043's "Documents owed at acceptance" (`ADR-043-working.md:1030-1052`) drafted as apply-ready blocks (25: 23 at acceptance, 2 at close), with the ADR copy as Block 0. Each Old text was expanded byte for byte from its file at `3c00fc88` (between the two commits only QUAL-234's row changed among the targets, and Block 23k follows it), and every single-line anchor was checked unique with `grep -cF`. The code lines cited in new text were opened at `93698d1f`; none of their files changed by `3c00fc88`: `RuleRegistry.cs:446-449`, Payroll `Program.cs:147`, `ConfigResolutionService.cs:185`, `:237-243`, `FlexBalanceRule.cs:44-48`, `init.sql:367`, `:380`, `:397`, `:865`, `:1468`, `:1474`, `:1480`, `:1486`, `:2525-2532`, `DanishPublicHolidays.cs:49`, `DemoSeed/Generation/DanishHolidays.cs:68`, `WeeklyTimelineCore.cs:134`, `ProjectionHoursReader.cs:7-25`, `LocalAgreementProfile.cs:24`, `ProfileEditor.tsx:40`, `OvertimeBalanceRepository.cs:50` (no production caller), `WeeklyCalculationPipeline.cs:5-14`, Orchestrator `Program.cs:48`. Companion: `S148-BUILD-INPUT.md` draft 2.3 (the Fable cycle-3 NOTE in S1-7).
