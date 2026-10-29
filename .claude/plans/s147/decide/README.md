# S147 decide track: the question briefs

*Kept by the `planner`. Started 2026-10-28 at `e7478f24`; updated at `c29764ce`.*

## What this is

S146's research found that StatsTid's overtime/flex model is wrong in kind. ADR-043 sets the new direction: flex by default, overtime only for ordered hours, a monthly norm. Twelve design questions remain. S147 settles them; S148 builds the model.

The questions are put to the owner one at a time, in a fixed order. Each one goes through four steps:

1. **A brief.** One page in this folder: the question, the options and what each gives up, the lean, what it waits on, and the ADR-043 text it rewrites.
2. **Adjudication.** The `adjudicator` confirms or replaces the lean and settles the wording. The plan names one file per question, `.claude/reviews/S147-D-NN-adjudication.md`; the first six (D-1, D-2, D-3, D-6, D-9, D-11) were adjudicated together in **one sectioned file, `.claude/reviews/S147-D-batch1-adjudication.md`** (Fable, verdict RULED-WITH-QUESTIONS), and each brief points at its section (§ D-NN). D-10 has its own file, `.claude/reviews/S147-D-10-adjudication.md` (Fable, RULED-WITH-QUESTIONS).
3. **The ask.** The Orchestrator explains the question in prose, then asks it on its own (`PLAN-s147.md:269-274`). Each adjudicated brief carries the settled wording under "The question as it will be asked".
4. **The record.** The answer goes verbatim into the `SPRINT-147.md` ledger and into the rulings table of `../ADR-043-working.md`.

A brief whose question depends on an earlier answer is written only after that answer is given, so that it can state what it builds on.

## The order, what each waits on, and status

| # | Question | ADR-043 text | Lean | Waits on (earlier answers) | Research gate | Status | Brief |
|---|---|---|---|---|---|---|---|
| D-1 | **F-1** One cut-over or stages | `:244-252` | (b), confirmed; asked as (a)/(b), (c) not offered | nothing | — | **ANSWERED (b), 2026-10-29** (§ D-1) | `D-01-order-of-work.md` |
| D-2 | **QUAL-234** How Skema's clock times reach payroll and the rest check (sub-question: manual hours) | new D10 | (a), confirmed; manual hours retired as a recorded proposal (not asked) | nothing | R5 ✓ | **adjudicated — asked next** (§ D-2) | `D-02-clock-times.md` |
| D-3 | **QUAL-232** Which time counts as worked time | new D11 | (c), confirmed; asked as (c)/(b), (a) not offered | nothing | R3 ✓ | **adjudicated — queued (order O-1)** (§ D-3) | `D-03-worked-time-classes.md` |
| D-4 | **F-2** The marking workflow. Includes SEC-056's creator-not-approver rule (a creator column, reseed) and keeps T9's self-decision guard; also RR-12: § 14 applies to ordered or rostered work | `:254-269` | (c) | D-2, D-3 | — | waits on D-2, D-3 | — |
| D-5 | **F-3** The overtime account. Includes SEC-056: compensation only behind an approved movement | `:271-277` | (b) | D-4 | — | waits on D-4 | — |
| D-6 | **QUAL-233** (with QUAL-204) Does payroll read what the admin editors write | ADR-014; ADR-016 D10; F-5 (a) | (a), confirmed; all three options asked | nothing | — | **adjudicated — queued (order O-1)** (§ D-6) | `D-06-config-source.md` |
| D-7 | **F-4** Office hours. RR-2/RR-12: the sick-day credit follows the hours fixed for the day | `:279-284` | (b), off by default | D-6 | — | waits on D-6 | — |
| D-8 | **F-5** Local flex parameters, plus five related items: the leaver's last closing; QUAL-219; QUAL-225; a tenth parameter, the sick-day credit basis (RR-12); **Q-1**, AC's sick-day default, lean (a), already built as a labelled default; QUAL-231's partial-day residual (RR-3) | `:286-297` | (a) | D-6 | — | waits on D-6 | — |
| D-9 | **F-6** AC's period | `:299-305` | (a), confirmed; asked as (a)/(b), (c) not offered | nothing | R1 corroborates (RR-12) | **adjudicated — queued (order O-1)** (§ D-9) | `D-09-ac-period.md` |
| D-10 | **F-7** The part-timer default: a provisional owner default, pending a qualified expert. RR-8 adds the evidence and two citation fixes | `:307-311` | (c) for the shape (told, not asked); the value (ii), the award, confirmed; asked as (ii)/(i); "comparable full-timer" held as met as a recorded proposal | nothing (built in stage 2; building waits on D-4) | R4 ✓ | **adjudicated — queued after D-11** (own file, `S147-D-10-adjudication.md`) | `D-10-part-timer-default.md` |
| D-11 | **F-8** The HK pay ceiling; PROSA pay groups 3–4 | `:313-320` | (b), confirmed; all three options asked, (b) first | nothing | — | **adjudicated — queued (order O-1)** (§ D-11) | `D-11-pay-ceiling.md` |
| D-12 | **F-9** Customary days off | `:322-328` | (c) | D-8 | — | waits on D-8 | — |

*"§ D-NN" in the status column is the section of `.claude/reviews/S147-D-batch1-adjudication.md`; D-10's adjudication is its own file.*

**The order and the asking (the adjudication's O-1 and O-2).**
- **O-1 — the order stands:** D-1, D-2, D-3, D-6, D-9, D-11. D-1 frames the stages every later answer lands in; D-2 and D-3 gate D-4 and D-5; D-6 gates D-7, D-8 and D-12.
- **D-10 follows D-11** (its own adjudication's order): the seventh independent question, asked before D-4. Its wording rests only on ADR-043 D3's "ordered or accepted" direction, not on D-4.
- **D-1 answered (b) Stages, 2026-10-29;** D-2 is asked next.
- **O-2 — one at a time, confirmed; no batching.** Each question is explained in prose and asked on its own, even though the six were adjudicated together.

**What the adjudication settled without asking the owner.** Four of the planner's additions became *recorded proposals*: adjudicated, told to the owner in the prose, not asked. They are D-1.4 (no supplement in stage 1), D-2.4 (manual hours retired), D-9.4 (AC's month norm uses D1's function) and D-11.4 (one regime attribute also covers AC). Two further items are recorded for the build input: D-6.5's stage-0 label and D-3.5's home on-call rest flag as a domain-expert item. All six sit in one list in `../ADR-043-working.md`, "Provisional items and recorded proposals (not owner rulings)". None is an owner ruling; the rulings table holds only the owner's answers (D-1's, so far).

**Note on D-10.** The plan (`PLAN-s147.md:289`) and the refinement (`REFINEMENT-s147…:514`) give R4 as D-10's only gate, and R4 is answered. The first pass grouped it with the dependent questions. The coordinator then ruled (2026-10-28) that it should be drafted now. The batch adjudication declined it as outside its six ("draftable now"); its own adjudication followed (`.claude/reviews/S147-D-10-adjudication.md`, 2026-10-29). It confirmed the lean, (c) for the shape and (ii) for the value, and made "comparable full-timer held as met" a recorded proposal (D-10.4; the working copy's one list, item 7). With D-1 answered (b), D-10 is built in stage 2; building still waits on D-4.

**The coordinator's other rulings (2026-10-28).**
- **The line-number re-check.** Re-checking ADR-043's line numbers becomes an Orchestrator docs item, run before the ADR's dual-lens review. The drift list stays in the working copy's notes. (The batch adjudication also declined the working copy's citation drift as "scheduled".)
- **The planner's additions went to the adjudicator with the briefs.** Outcome:
  - D-1: no supplements in stage 1 — **recorded proposal** (D-1.4);
  - D-2: manual hours retired — **recorded proposal** (D-2.4);
  - D-9: AC's month norm — **recorded proposal** (D-9.4);
  - D-11: one home for the overtime regime — **recorded proposal on three conditions** (D-11.4);
  - D-10: the planner's lean on the value — **confirmed**, (ii) (D-10.3); the "comparable full-timer" proposal — **recorded proposal** with a corrected rationale (D-10.4).

**Orchestrator docs items from the adjudication** (not the planner's to do; recorded here so they are not lost).
- **D-11.5 — the HK circular.** Before ADR-043's dual-lens review, re-cite HK § 4 stk. 2 and § 3 stk. 4 against circular 087-25 (which superseded 085-25, the one C3 quotes). If the texts differ, append a dated correction note.
- **D-2.2 — ADR-043's new D10.** When D-2 is answered and D10 is written, it must state **one source of time of day per hour**: the legacy `POST /api/time-entries` times and `VoluntaryUnsocialHours` are either routed through the interval table or retired with the route.
- **D-10.3 — at ADR-043's acceptance.** The source-register row names the award and the ruling (QUAL-215's lesson); `docs/references/danish-agreements.md:64` gets a dated note.

**Also recorded with the track** (RR-6, RR-12):
- **F-5's text gains one sentence:** the sick-day credit basis is a local parameter, defaulting to § 7 nr. 2.
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
