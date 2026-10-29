# S147 decide track: the question briefs

*Kept by the `planner`. Started 2026-10-28 at `e7478f24`; updated at `26b70a08`.*

## What this is

S146's research found that StatsTid's overtime/flex model is wrong in kind. ADR-043 sets the new direction: flex by default, overtime only for ordered hours, a monthly norm. Twelve design questions remain. S147 settles them; S148 builds the model.

The questions are put to the owner one at a time, in a fixed order. Each one goes through four steps:

1. **A brief.** One page in this folder: the question, the options and what each gives up, the lean, what it waits on, and the ADR-043 text it rewrites.
2. **Adjudication.** The `adjudicator` confirms or replaces the lean and settles the wording. The plan names one file per question, `.claude/reviews/S147-D-NN-adjudication.md`. In practice there are three files so far:
   - **Batch 1**, `.claude/reviews/S147-D-batch1-adjudication.md` (Fable, RULED-WITH-QUESTIONS): D-1, D-2, D-3, D-6, D-9 and D-11, in one sectioned file. Each brief points at its section (§ D-NN).
   - **D-10**, on its own: `.claude/reviews/S147-D-10-adjudication.md` (Fable, RULED-WITH-QUESTIONS).
   - **Batch 2**, `.claude/reviews/S147-D-batch2-adjudication.md` (Fable, RULED-WITH-QUESTIONS): D-4, D-7 and D-8, with D-8 split into D-8a and D-8b, plus five rulings.
3. **The ask.** The Orchestrator explains the question in prose, then asks it on its own (`PLAN-s147.md:269-274`). Each adjudicated brief carries the settled wording under "The question as it will be asked".
4. **The record.** The answer goes verbatim into the `SPRINT-147.md` ledger and into the rulings table of `../ADR-043-working.md`.

A brief whose question depends on an earlier answer is normally written only after that answer is given, so that it can state what it builds on. The adjudicator ruled that D-5 and D-12 may be drafted before D-4 and D-8b are answered (batch 2, "Asking order").

## The order, what each waits on, and status

| # | Question | ADR-043 text | Lean | Waits on (earlier answers) | Research gate | Status | Brief |
|---|---|---|---|---|---|---|---|
| D-1 | **F-1** One cut-over or stages | `:244-252` | (b), confirmed; asked as (a)/(b), (c) not offered | nothing | — | **ANSWERED (b), 2026-10-29** (§ D-1) | `D-01-order-of-work.md` |
| D-2 | **QUAL-234** How Skema's clock times reach payroll and the rest check (sub-question: manual hours) | new D10 | (a), confirmed; manual hours retired as a recorded proposal (not asked) | nothing | R5 ✓ | **ANSWERED (a), 2026-10-29** (§ D-2) | `D-02-clock-times.md` |
| D-3 | **QUAL-232** Which time counts as worked time | new D11 | (c), confirmed; asked as (c)/(b), (a) not offered | nothing | R3 ✓ | **ANSWERED (c), 2026-10-29** (§ D-3) | `D-03-worked-time-classes.md` |
| D-4 | **F-2** The marking workflow, with SEC-056 (ruling 1's four conditions) and RR-12 | `:254-269` | (c), confirmed; asked as (c)/(a), (b) not offered; P1–P6 accepted | D-2, D-3 (both answered) | — | **adjudicated — being asked** (batch 2 § D-4) | `D-04-marking-workflow.md` (draft 2) |
| D-5 | **F-3** The overtime account. Includes SEC-056: compensation only behind an approved movement | `:271-277` | **Reframing proposed:** F-3's storage (b) told (P1); asked: where saved days live, (i) a balance in StatsTid (lean) or (ii) the conversion recorded, HR keeps the days; (iii) not offered; nine recorded proposals | D-4, for the asking order only; the question does not depend on D-4's answer | for building (i): § 11's text | **brief drafted (draft 1)** | `D-05-overtime-account.md` |
| D-6 | **QUAL-233** (with QUAL-204) Does payroll read what the admin editors write | ADR-014; ADR-016 D10; F-5 (a) | (a), confirmed; all three options asked | nothing | — | **ANSWERED (a), 2026-10-29** (§ D-6) | `D-06-config-source.md` |
| D-7 | **F-4** Office hours. The credit for every paid-absence day follows the hours fixed for the day (ruling 4) | `:279-284` | (b), off by default, confirmed; asked as (b)/(a); P1–P6 accepted | D-6 (answered) | — | **adjudicated — queued** (batch 2 § D-7) | `D-07-office-hours.md` (draft 2) |
| D-8 | **F-5** Local flex parameters: the parent brief, holding R-1..R-7 as amended (R-2's five amendments incl. "no central cap", ruling 2; R-5 per ruling 3; R-6 the paid-absence credit basis, ruling 4; R-7 with QUAL-237) | `:286-297` | split confirmed (ruling 5) | D-6 (answered) | — | **adjudicated** (batch 2 § D-8.4, § D-8.5) — its two questions below | `D-08-local-flex-parameters.md` (draft 2) |
| D-8a | **F-5's per-group sub-question**: can an institution's flex rules treat groups of staff differently? | `:287`, `:292`, `:295` | (b), confirmed; all three asked, (b) first | D-6 (answered) | — | **adjudicated — queued** (batch 2 § D-8a) | `D-08a-flex-membership.md` (draft 2) |
| D-8b | **Q-1**: AC's sick-day credit default | F-5 (RR-1, RR-12) | (a), confirmed; asked as (a)/(b), (c) not offered | nothing | R1 ✓ | **adjudicated — queued** (batch 2 § D-8b) | `D-08b-ac-sick-day-default.md` (draft 2) |
| D-9 | **F-6** AC's period | `:299-305` | (a), confirmed; asked as (a)/(b), (c) not offered | nothing | R1 corroborates (RR-12) | **ANSWERED (a), 2026-10-29** (§ D-9) | `D-09-ac-period.md` |
| D-10 | **F-7** The part-timer default: a provisional owner default, pending a qualified expert. RR-8 adds the evidence and two citation fixes | `:307-311` | (c) for the shape (told, not asked); the value (ii), the award, confirmed; asked as (ii)/(i); "comparable full-timer" held as met as a recorded proposal | nothing (built in stage 2; building waits on D-4) | R4 ✓ | **ANSWERED F-7 = (c), default (ii), 2026-10-29** (own file, `S147-D-10-adjudication.md`) | `D-10-part-timer-default.md` |
| D-11 | **F-8** The HK pay ceiling; PROSA pay groups 3–4 | `:313-320` | (b), confirmed; all three options asked, (b) first | nothing | — | **ANSWERED (b), 2026-10-29** (§ D-11) | `D-11-pay-ceiling.md` |
| D-12 | **F-9** Customary days off | `:322-328` | (c), both kinds per institution and date; also offered (a) paid only and (d) not modelled; (b) closure-only not offered; nine recorded proposals | R-1 (adjudicated) | — | **brief drafted (draft 1)** | `D-12-customary-days-off.md` |

*In the status column, "§ D-NN" is a section of `.claude/reviews/S147-D-batch1-adjudication.md`, and "batch 2 § …" a section of `.claude/reviews/S147-D-batch2-adjudication.md`. D-10's adjudication is its own file.*

**The order and the asking.**
- **O-1 (batch 1), the order stands:** D-1, D-2, D-3, D-6, D-9, D-11. D-1 frames the stages every later answer lands in; D-2 and D-3 gate D-4 and D-5; D-6 gates D-7, D-8 and D-12.
- **D-10 followed D-11** (its own adjudication's order), as the seventh independent question.
- **Seven answered, 2026-10-29, all on the recommended option:** D-1 (b), D-2 (a), D-3 (c), D-6 (a), D-9 (a), D-11 (b), and D-10, F-7 = (c) default (ii).
- **The asking order now: D-4, D-7, D-8a, D-8b, then D-5, then D-12.**
  - D-4 is being asked.
  - D-7, D-8a and D-8b are adjudicated and queued.
  - D-5 and D-12 have draft-1 briefs, which go to adjudication next.
  - This order meets the batch-2 adjudication's two constraints: D-5 after D-4's answer, and D-12 after D-8b.
- **O-2, one at a time, confirmed; no batching.** Each question is explained in prose and asked on its own, even when several were adjudicated together.

**Batch 2, adjudicated 2026-10-29** (`.claude/reviews/S147-D-batch2-adjudication.md`, RULED-WITH-QUESTIONS).
- **D-4, the marking workflow.** Asked as (c), an order beforehand and an acceptance afterwards, each naming its hours, against (a), afterwards only. (b), a budget, is not offered: it names no hours, and it can pay unordered hours.
  - P1–P6 are accepted. P1's decider is generalised to whoever holds the month's approval authority. P2 hands the § 10 clock to D-5 as a fact.
  - **P4 carries ruling 1:** a leader's own order is a decision. It is never on the leader's own hours, the creator never approves, and the authority is the month approver's.
- **D-7, office hours.** Asked as (b), opt-in and off by default, against (a), not modelled.
  - Ruling 4 widened the credit to every paid-absence day, through one served "planned hours" figure that the credit, the absence cap and the vacation divisor all read.
  - P1–P6 are accepted.
- **D-8, the local flex rules.** The split stands (ruling 5).
  - **D-8a**, flex membership: lean (b), all three options asked.
  - **D-8b**, Q-1: lean (a); (c) is not offered.
  - **R-1..R-7 are accepted**, four of them amended. Ruling 2: there is no central flex cap, and nothing is forfeited until an institution sets one. Ruling 3: QUAL-225 is a recorded design proposal. Ruling 4: the paid-absence credit basis. R-7 adds QUAL-237 and the second direction.
- **The five rulings** are listed as items the Orchestrator tells the owner, in `../ADR-043-working.md` ("Provisional items and recorded proposals", items 8–26 and the rulings block).

**The two briefs drafted 2026-10-29 (draft 1, not adjudicated).**
- **D-5, the overtime account.** *A reframing is proposed.*
  - F-3's storage is told, not asked: only (b), the movement ledger, can apply the § 10 clock the adjudicator recorded as a fact.
  - The owner question becomes where saved days (*opsparingsdage*) live:
    - **(i)** a balance in StatsTid (lean);
    - **(ii)** the conversion recorded, with HR keeping the days;
    - **(iii)**, no election, is proposed as not offered.
  - **Nine recorded proposals are proposed:**
    - the ledger;
    - SEC-056 on ruling 1's terms;
    - the employer chooses, with time off first;
    - the clocks as facts;
    - time off in lieu as a Skema absence;
    - a postponement signed by both parties;
    - the deadline as a visible hand-off (HRP-021);
    - *opsparing af frihed* not modelled;
    - a leaver's time off paid out.
  - Nothing in it depends on D-4's (c) or (a).
- **D-12, customary days off.**
  - *Lean (c):* both kinds, a paid day off credited like paid absence, and a closure day, per institution and date. Also offered: (a), paid only, and (d), not modelled. (b), closure only, is proposed as not offered.
  - **Nine recorded proposals are proposed:**
    - a dated table beside the profile (R-1);
    - a credit, not a norm cut;
    - part days;
    - two modes, given to everyone or on request (1 May);
    - one credit per day;
    - closure days earn nothing;
    - the § 13 windows are separate;
    - a configuration, not a hand-off;
    - demo customs.

**What the adjudication settled without asking the owner (batch 1).** Four of the planner's additions became *recorded proposals*: adjudicated, told to the owner in the prose, not asked. They are D-1.4 (no supplement in stage 1), D-2.4 (manual hours retired), D-9.4 (AC's month norm uses D1's function) and D-11.4 (one regime attribute also covers AC). Two further items are recorded for the build input: D-6.5's stage-0 label and D-3.5's home on-call rest flag as a domain-expert item. All of them, with D-10.4 and batch 2's items 8–26, sit in one list in `../ADR-043-working.md`, "Provisional items and recorded proposals (not owner rulings)". None is an owner ruling; the rulings table holds only the owner's answers (seven so far).

**Note on D-10.** The plan (`PLAN-s147.md:289`) and the refinement (`REFINEMENT-s147…:514`) give R4 as D-10's only gate, and R4 is answered. The first pass grouped it with the dependent questions. The coordinator then ruled (2026-10-28) that it should be drafted now. The batch adjudication declined it as outside its six ("draftable now"); its own adjudication followed (`.claude/reviews/S147-D-10-adjudication.md`, 2026-10-29). It confirmed the lean, (c) for the shape and (ii) for the value, and made "comparable full-timer held as met" a recorded proposal (D-10.4; the working copy's one list, item 7). With D-1 answered (b), D-10 is built in stage 2; building still waits on D-4.
- **Answered 2026-10-29:** F-7 = (c), default (ii).
- **Which "D-5" the adjudicator's AC note means** (D-10.2 item 8, "a check in D-5's build"). The Orchestrator ruled on 2026-10-29: it means **ADR-043's D5, the AC claims, built in stage 3**, not decide-track question D-5 (F-3, the overtime account). The brief records it (draft 2.1).

**The coordinator's other rulings (2026-10-28).**
- **The line-number re-check.** Re-checking ADR-043's line numbers becomes an Orchestrator docs item, run before the ADR's dual-lens review. The drift list stays in the working copy's notes. (The batch adjudications also declined citation drift as "scheduled".) The briefs cite the working copy by section (for example "F-2 in the verbatim body"), because its line numbers move as its lists grow. ADR-043 line numbers in the briefs are the knowledge-base file's.
- **The planner's additions went to the adjudicator with the briefs.** Outcome:
  - D-1: no supplements in stage 1, a **recorded proposal** (D-1.4);
  - D-2: manual hours retired, a **recorded proposal** (D-2.4);
  - D-9: AC's month norm, a **recorded proposal** (D-9.4);
  - D-11: one home for the overtime regime, a **recorded proposal on three conditions** (D-11.4);
  - D-10: the planner's lean on the value **confirmed**, (ii) (D-10.3), and the "comparable full-timer" proposal made a **recorded proposal** with a corrected rationale (D-10.4).

**Orchestrator docs items from the adjudications** (not the planner's to do; recorded here so they are not lost).
- **D-11.5, the HK circular.** Before ADR-043's dual-lens review, re-cite HK § 4 stk. 2 and § 3 stk. 4 against circular 087-25, which superseded 085-25, the circular C3 quotes. If the texts differ, append a dated correction note.
- **D-2.2, ADR-043's new D10.** When D10 is written, it must state **one source of time of day per hour**: the legacy `POST /api/time-entries` times and `VoluntaryUnsocialHours` are either routed through the interval table or retired with the route.
- **D-10.3, at ADR-043's acceptance.** The source-register row names the award and the ruling (QUAL-215's lesson); `docs/references/danish-agreements.md:64` gets a dated note.
- **Batch 2, at acceptance.**
  - ADR-024 D7 point 3 (`ADR-024:220`) and ADR-043 F-2 (`:265-266`) say "the approval page". It becomes Teamoversigt (D-4.2 item 1).
  - SYSTEM_TARGET §G's "central limit" (`SYSTEM_TARGET.md:168`, `:174`) is corrected (ruling 2).
  - SEC-056's row is refined (ruling 1).

**Also recorded with the track** (RR-6, RR-12, ruling 4):
- **F-5's text gains one sentence:** the **paid-absence** credit basis is a local parameter, defaulting to § 7 nr. 2 (ruling 4 widened RR-12's "sick-day").
- **The source for seniordage** (senior days off) is Medst. 035-24: whole days as the starting point, half days or single hours by agreement. It is kept here for the owner's later use.

## What the round produces

- **ADR-043 amended**, in `../ADR-043-working.md`.
- **One dual-lens review** of the amended ADR, with artifacts at `.claude/reviews/S147-ADR-043-{reviewer,codex}.md`.
- **ADR-043 ACCEPTED** (O-10).
- **The S148 build input**, at `../S148-BUILD-INPUT.md`.

If the round cannot finish before C-1, the deferred branch applies (AC-D3): the sprint log records the rulings given and the questions left.

---
*Revision history.*
- 2026-10-28: index created. Six briefs drafted (D-1, D-2, D-3, D-6, D-9, D-11) and the working copy seeded.
- 2026-10-28, later: D-10 drafted on the coordinator's ruling (1); rulings (2) and (3) recorded.
- 2026-10-28, after the batch adjudication: the six briefs moved to draft 2 (`.claude/reviews/S147-D-batch1-adjudication.md`, RULED-WITH-QUESTIONS); status column updated (D-1 asked next, the other five queued by O-1; D-10 adjudication requested); O-1 and O-2 recorded; the four recorded proposals and two build-input items noted, with their single list in the working copy; the Orchestrator docs items D-11.5 and D-2.2 recorded; step 2 now names the batch file.
- 2026-10-29: D-1 ANSWERED (b) Stages (the owner, 2026-10-29); D-2 adjudicated — asked next; D-10 adjudicated on its own (`.claude/reviews/S147-D-10-adjudication.md`, RULED-WITH-QUESTIONS), brief at draft 2, queued after D-11 and before D-4, built in stage 2; its recorded proposal (comparable full-timer) and the Orchestrator docs item D-10.3 noted.
- 2026-10-29, later (at `f37d7cd3`):
  - **Status updated.** D-1, D-2, D-3, D-6, D-9, D-10 and D-11 ANSWERED. D-4, D-7 and D-8 have briefs (draft 1). D-5 waits on D-4; D-12 waits on D-8, in substance on R-1.
  - **The three briefs** are summarised above, with D-8's proposed split into D-8a and D-8b.
  - **D-10's "D-5" note** is resolved by the Orchestrator's ruling: ADR-043 D5, stage 3. D-10 is at draft 2.1.
  - **D-8's Q-1 wording corrected.** It said "already built"; it is being built (TASK-14708, wave 2a, not on master at `f37d7cd3`).
- 2026-10-29, at `26b70a08`, after the batch-2 adjudication (`.claude/reviews/S147-D-batch2-adjudication.md`):
  - **Briefs at draft 2:** D-4, D-7 and D-8. D-8 is split into the parent (R-1..R-7 as amended), `D-08a-flex-membership.md` and `D-08b-ac-sick-day-default.md`.
  - **Status column:** D-4 is adjudicated and being asked. D-7, D-8a and D-8b are adjudicated and queued.
  - **Drafted (draft 1):** D-5, with a reframing proposed, and D-12.
  - **The asking order:** D-4, D-7, D-8a, D-8b, then D-5, then D-12.
  - **Step 2** names the batch-2 file. The batch-2 Orchestrator docs items are added.
  - **Working-copy citations** in the briefs are now by section, not line.
