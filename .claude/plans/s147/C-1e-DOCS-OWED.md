# C-1e — Step 7a cycle 3 and TASK-14725-T fix-up 4: apply-ready records

| | |
|---|---|
| **What this is** | The exact text for the records of S147 Step 7a cycle 3, of the red C-2 run on K′, and of that run's fix, TASK-14725-T fix-up 4 (test-only, merged `a0c67f4f`). Sources:<br>• the cycle-3 reviews (`.claude/reviews/SPRINT-147-step7a-{codex,reviewer}-c3.md`);<br>• fix-up 4's reviews (`S147-TASK-14725-fu4-5alpha.md`, `S147-TASK-14725-fu4-5a-codex.md`, `-codex-c2.md`, `-codex-c3.md`, `S147-TASK-14725-fu4-5a-reviewer.md`);<br>• the task report's "Fix-up 4" section with its audit table;<br>• the merge GATE logs (`gate-fu4-*.log`) and the K′ run's log (`ci-kprime.log`), both in the session scratchpad;<br>• the commits `5e84766b`, `c3d98208`, `a0447fe7`, `216f6721` and the merge `a0c67f4f`;<br>• the Orchestrator's brief.<br>The Orchestrator applies each block as a string replacement, into K‴, the final candidate. The `planner` drafts; the Orchestrator applies. Every target is under `docs/**` |
| **Status** | DRAFT 1 (`planner`, Opus 5.5), 2026-11-05 |
| **Base commit** | `841a7020` (K″) for the Old texts, which are copied from it. Local master moved to `a0c67f4f` (fix-up 4's merge) while this was drafted. That merge changes one test file only: `git diff --stat 841a7020 a0c67f4f` lists `LeaverPastGraceByDateTests.cs` alone, so every docs target is byte-identical at both. A script counted each Old text as an exact substring over the whole target file, in the `841a7020` blob, the `a0c67f4f` blob and the working copy, and each count is 1. Docs line numbers are at `841a7020`. Code line numbers are at `841a7020`, identical at `a0c67f4f` for `src/**`. Test-file lines are at `a0c67f4f` where so marked |
| **Orchestrator model** | S147: Opus 5.5 seat; drafting `planner` (Opus 5.5); judgment `adjudicator` / `reviewer` on Fable 5.1 |
| **Timing** | Apply as K‴, on top of `a0c67f4f`, before C-2 runs on K‴. The C-2 placeholders stay literal until C-2 on the final candidate is green |
| **Owner approval** | None needed. No block touches `SYSTEM_TARGET.md` |

## What this is, in plain words

C-2 is the CI run that executes the Docker tests the owner's machine cannot run. Its run on K′ went red on one test out of 2430. The test checks that a leaver's manager can still open the leaver's month in Skema on day 5 and cannot on day 6. It never marked the month as sent. Since S124 a manager may open only a month the employee has sent for approval, so the manager was refused on day 5 for that unrelated reason, and that refusal is the red.

The same mistake hid a worse one. On day 6 the refusal the test expected also came from that unrelated rule, so that half would have passed even on the code before the fix: it proved nothing. Fable's Step 7a cycle 3 traced this independently and made it its blocker. The product was right throughout; the test was wrong.

**TASK-14725-T fix-up 4** (merged `a0c67f4f`) corrects the test:
- it sends the month first;
- it checks the refusal's reason, not only its status, so only the date rule can satisfy it;
- it gives the year-overview test the day-5 counterpart it lacked;
- it re-checked every other TASK-14725 test against its route's other gates, and none had the same trap.

Fix-up 4 changes tests only, so no user-facing behaviour changes. Because it is a change to the tested commit, it makes a new candidate, **K‴**, which needs C-2 again and a fourth Step 7a cycle.

The records follow:
- **ADR-040's "Pinned by"** says, route by route, what each day-5 counterpart can and cannot detect. It also records that the Skema test was red once for a fixture reason, and says "six facts" precisely (Fable NOTE-1, NOTE-3). The six facts' line numbers are refreshed to the merged file.
- **SEC-058's** residual matches ADR-040's retroactive-end-date case (Codex NOTE 1), and its pins carry a dated note on the Skema test.
- **The sprint log** gains:
  - the K′ run (red) and the K″ run in C-2's table, with the branch names corrected (Codex NOTE 2);
  - a dated note on cycle 2's W-1 and on the fix-up 1 claim that fix-up 4 made true;
  - fix-up 4's merge row and the ledger row;
  - Step 7a cycle 3's record, with every finding's disposition and a neutral lesson;
  - Status, Phase, As of and Next action, with K‴ as the final candidate.

Rules kept:
- Nothing is described as merged before it is, and fix-up 4 is merged (`a0c67f4f`).
- CI-verified only with a run id. Fix-up 4's Docker facts are CI-pending until C-2 on K‴, and no CI-pending claim is flipped.
- Earlier records get dated notes. Current-state cells are updated in place: Status, Phase, As of, Next action, and C-2's Commit and Run rows.

## How to apply

- Each block names its **Target**, the **Old text** (one exact match at `841a7020` and at `a0c67f4f`) and the **New text**. Apply each part as its own replacement.
- No two replacements in one file overlap. Every Old text is a single line or part of one. In ADR-040:
  - 1a is part of line 606 only, and the next line (`  2 and 3):`) completes the sentence its New text ends with;
  - 1c–1h are the six fact bullets below it, one line each.
- **Line endings:** every target is LF in the blob and in the working copy (a carriage-return count of 0): ADR-040, `docs/operations/security-finding-register.md`, `docs/sprints/SPRINT-147.md`. Multi-line New texts (1a, 3c, 3g, 3i) are LF.
- **Placeholders.**
  - **New:** `<K″ run result>`, in 3g only. It holds run `42024818530`'s result, one sentence, filled when the run finishes.
  - **The brief's fix-up 4 placeholders are filled with verified values,** because the merge landed while this was drafted:
    - `<fu4 merge>` = `a0c67f4f`;
    - `<fu4 tip>` (mine) = `216f6721`;
    - `<fu4 GATE>` = "build 0 / 144 / CA2100 114; Unit 1722; DemoSeed 170; Regression non-Docker 135", from `gate-fu4-*.log`, written 14:15–14:16, after the 14:13 merge;
    - `<fu4 Codex c2 verdict>` = "c2 0 B / 1 W", then "c3 CLEAN";
    - `<fu4 Fable verdict>` = "READY-TO-MERGE, 0 B / 0 W / 5 N".
    - Sources: the merge message of `a0c67f4f` and the four artifacts.
  - **Unchanged and left literal for C-4:** `<K sha>` (now K‴'s sha, C-1's rule), `<C-2 run id>`, `<C-2 date>`, `<C-2 Regression n>` and `<7a cycles>`. In these New texts they occur in 3i (`<7a cycles>`), 3j and 3k (`<K sha>`), and 3l (`<K sha>`, `<C-2 run id>`, `<C-2 date>`). `<C-2 Regression n>` occurs in none.
- **Blocks 1–3 are owed.** `docs/SECURITY.md` is not changed (see "Not drafted here").

## Map: each owed item → its block

| # | Owed item (source) | Block | State at `841a7020` |
|---|---|---|---|
| 1 | ADR-040: the D+5 sentence true after fix-up 4, with what each counterpart detects (Fable NOTE-3; Codex fu4 c1 and c2; Fable fu4 NOTE-4); "six facts" (Fable NOTE-1); the Skema pin's red run and its fix | 1a (line 606) | "each access fact … has D+5 boundary pins" |
| 1′ | ADR-040: the six facts' lines after fix-up 4, and the note's line basis | 1b (`:519`), 1c–1h (`:608-614`) | lines at `ab77c475` |
| 2 | SEC-058: the residual aligned with ADR-040 (Codex NOTE 1); the Skema pin's note; the six facts' lines | 2a, 2b (line 218) | "consistency only" |
| 3a | Sprint log (f): the header Status | 3a (`:5`) | names K″ final |
| 3b | Sprint log (e): the TASK-14725 ledger row | 3b (`:112`) | fix-ups 2–3 only |
| 3c | Sprint log (d): fix-up 4's merge row | 3c (after `:181`) | absent |
| 3d | Sprint log: the fix-up 1 claim fix-up 4 made true (not in the brief; "Brief contradictions" 4) | 3d (`:252`) | "either arm alone admits. Fixed in -T fix-up 1." |
| 3e | Sprint log (a): C-2's Commit row | 3e (`:529`) | "then K″, the final one" |
| 3f | Sprint log (a): C-2's Run row, the branch (Codex NOTE 2) | 3f (`:530`) | `s147-ci-k` |
| 3g | Sprint log (a), (g): C-2's first-run row ("six facts") and the two earlier runs | 3g (`:532`) | no run recorded |
| 3h | Sprint log (b): cycle 2's Fable W-1 row | 3h (`:582`) | "was in progress at review time" |
| 3i | Sprint log (c), (h): cycle 3's record and the lesson | 3i (`:591`) | absent |
| 3j | Sprint log (f): As of | 3j (`:597`) | K″ |
| 3k | Sprint log (f): Phase | 3k (`:604`) | cycle 2 done; K″ next |
| 3l | Sprint log (f): Next action | 3l (`:607`) | K″, cycle 3 |

**Count: 22 replacements** (1a–1h, 8; 2a, 2b, 2; 3a–3l, 12).

---

## Block 1 — ADR-040's Step 7a note: what the D+5 counterparts detect; the Skema pin's red run; "six facts"; the facts' lines

**Target:** `docs/knowledge-base/decisions/ADR-040-employee-timeline-and-as-of-resolution.md` (LF; the note "S147 Step 7a, 2026-11-05 (TASK-14725)", lines 509–616; its "Pinned by" bullet, lines 603–616). Eight parts.

The note's code-line basis is `ab77c475`. No `src/**` file changed between `ab77c475` and `a0c67f4f` (`git diff --stat` is empty). Every code line the New texts cite was opened at `841a7020`:
- `ApprovalEndpoints.cs:446-456`, approve's org-scope arm at the LocalHR floor, then the edge predicate;
- `SkemaEndpoints.cs:237` (the validator), `:246` (the edge arm), `:535-553` (the S124 month gate);
- `ApprovalReadTier.cs:116-121`, "The month has not been submitted for approval";
- `OrgScopeValidator.cs:602` and `DesignatedApproverAuthorizer.cs:423`, the two calls to `EmploymentEndDateLifecycleWriter.IsPastGrace`;
- `BalanceEndpoints.cs:710-712`, the year overview's only gate;
- `OvertimeEndpoints.cs` approve and reject, validator only.

The six facts' test lines move 48 down with fix-up 4. At `a0c67f4f` they are `:395`, `:420`, `:442`, `:466`, `:498` and `:548`, against `:347`, `:372`, `:394`, `:418`, `:450` and `:500` at `841a7020`.

### 1a — "Pinned by": the D+5 counterparts (line 606)

*Why a correction in place, with a marker:* the sentence was not true when written (Fable NOTE-3). The year-overview fact had no D+5 pin, and the Skema mirror could not do its job until fix-up 4. The marker names the cycle and the correction. The New text ends with "The six (fix-ups", which line 607 (`  2 and 3):`) completes.

Old text:

````text
has D+5 boundary pins. Six more facts in `LeaverPastGraceByDateTests` pin the recorded approval method (fix-ups
````

New text:

````text
each D+6 refusal has a D+5 counterpart that admits on the same seed. The counterpart shows that the refusal comes
  from the date, not from the fixture. What it can detect beyond that depends on how many paths admit on its route
  *(Step 7a cycle 3, Fable NOTE-3: as first written, this sentence said every access fact had D+5 boundary pins,
  but the year overview had none, and the Skema view's was the one that ran red; TASK-14725-T fix-up 4, merged
  `a0c67f4f`, fixes both)*:
  - **Approve, the manager and the unit leader** (`Day5_LeaverStillActive_ManagerAndUnitLeaderApprove_Is200_BoundaryPin`).
    It turns red on an off-by-one in the shared grace rule or in the approver predicate's use of it. It does not
    turn red on a validator-only error: approve's org-scope arm refuses these LocalLeader actors at its LocalHR floor
    anyway (`ApprovalEndpoints.cs:446-456`), so only the refusal's reason would change. Reject runs the same two arms
    and shares this counterpart; it has no D+5 fact of its own.
  - **Overtime approve** (`Day5_LeaverStillActive_LeaderOvertimePreApprovalApprove_Is200_BoundaryPin`) and **the
    validator itself** (the D+5 fact in `DepartedSelfValidatorTests`). The validator is the only admitting path
    there, so a validator off-by-one shows.
  - **The Skema month view** (the D+5 half of its own fact). Since fix-up 4 the fact sends the month first, so the
    S124 leader month gate (`SkemaEndpoints.cs:535-553`, "The month has not been submitted for approval") cannot be
    what refuses. The D+6 refusal must also carry the date rule's reason, "Target employee not found". The mirror
    needs only one admitting path (the covering scope at `:237`, or the live edge at `:246`). So it shows only an
    error both layers share, in `EmploymentEndDateLifecycleWriter.IsPastGrace` (`OrgScopeValidator.cs:602`,
    `DesignatedApproverAuthorizer.cs:423`), or a broken seed.
  - **The year overview** (a D+5 mirror added by fix-up 4). The route asks only the terminated-inclusive validator
    (`BalanceEndpoints.cs:710-712`), so a validator off-by-one shows: it is the validator's route-level D+5 pin.
  - **The stand-in** (the D+5 half of `Day6_LeaverStillActive_ManagerVikarStandIn_Is403_Day5Is200`).

  **The Skema fact ran red in CI once, for a fixture reason.** In C-2 on K′ (run `42013444380`, 2026-11-05) its D+5
  half got 403. The fact had not sent the month, so the month gate refused. The same gate let its D+6 half pass for
  the wrong reason, even on the code before TASK-14725 (`c13024cd`). The product was right. Fix-up 4's correction is
  CI-pending until C-2 on K‴.

  Fix-ups 2 and 3 added six facts in all to `LeaverPastGraceByDateTests`: five pin the recorded approval method,
  and the stand-in fact pins both the stand-in's D+6 refusal and its D+5 label. The predicate's other
  refusals (another organisation, no leader role in the database, an inactive home organisation) also record
  `ORG_SCOPE_FALLBACK`. That follows by construction, because the label takes the predicate's answer; no fact pins
  those cases by name (Step 7a cycle 3, Fable NOTE-1). The six (fix-ups
````

### 1b — the note's line basis (line 519)

Old text:

````text
Code lines below are at `ab77c475` (re-checked at Step 7a cycle 2).
````

New text:

````text
Code lines below are at `ab77c475` (re-checked at Step 7a cycle 2); the six test facts' lines in "Pinned by" are at `a0c67f4f`, after TASK-14725-T fix-up 4 (Step 7a cycle 3).
````

### 1c — the first fact's line (line 608)

Old text:

````text
(`:347`);
````

New text:

````text
(`:395`);
````

### 1d — the second fact's line (line 609)

Old text:

````text
(`:372`);
````

New text:

````text
(`:420`);
````

### 1e — the third fact's line (line 610)

Old text:

````text
(`:394`);
````

New text:

````text
(`:442`);
````

### 1f — the fourth fact's line (line 611)

Old text:

````text
(`:418`);
````

New text:

````text
(`:466`);
````

### 1g — the stand-in fact's line (line 612)

Old text:

````text
(`:450`: the stand-in refused at D+6, and at D+5
````

New text:

````text
(`:498`: the stand-in refused at D+6, and at D+5
````

### 1h — the sixth fact's line (line 614)

Old text:

````text
(`:500`).
````

New text:

````text
(`:548`).
````

## Block 2 — The security register: SEC-058's residual and its pins

**Target:** `docs/operations/security-finding-register.md` (LF). Two parts, both in SEC-058's row (line 218): 2a in the last cell, 2b in the AS-FOUND / AS-CLOSED cell.

### 2a — SEC-058's residual, aligned with ADR-040 (Codex NOTE 1)

*Why:* "consistency only" left out the case ADR-040 records at `:577-582`. A month sent first, whose end date is later set retroactively before it, can be approved and paid until the switch-off.

Old text:

````text
the employed-month bound still keys on the flag (consistency only; the send refuses a month with no employed day);
````

New text:

````text
the employed-month bound still keys on the flag: the send refuses a month with no employed day, but a month sent first, whose end date is later set retroactively before it, survives and can be approved and paid until the switch-off (S148 stage 0 keys the bound on "past the grace or switched off"; aligned with ADR-040 at Step 7a cycle 3, Codex NOTE 1);
````

### 2b — SEC-058's pins: "six facts", their lines after fix-up 4, and a dated note on the Skema pin

*Why:* SEC-058 closes when C-2 runs its Docker pins green, and `LeaverPastGraceByDateTests` is one of them. In C-2 on K′ one of its facts was red. A reader who meets that run should find the reason here. The note is a judgement call and can be dropped on its own; if it is, keep the refreshed lines (see "Brief contradictions" 3).

Old text:

````text
pinned by six facts in `LeaverPastGraceByDateTests` (`:347`, `:372`, `:394`, `:418`, `:450`, `:500`)
````

New text:

````text
pinned by six facts in `LeaverPastGraceByDateTests` (five label facts and the stand-in fact; at `a0c67f4f`, after TASK-14725-T fix-up 4: `:395`, `:420`, `:442`, `:466`, `:498`, `:548`). *(Step 7a cycle 3, 2026-11-05: in C-2 on K′, run `42013444380`, one of the access pins in `LeaverPastGraceByDateTests`, the Skema month-view fact, ran red for a fixture reason. The month was never sent, so a month gate refused instead of the date rule, and the fact's D+6 half could not fail. Fix-up 4, merged `a0c67f4f`, sends the month and asserts the refusal's reason. The product was right; the pins are CI-pending until C-2 on K‴.)*
````

## Block 3 — The sprint log

**Target:** `docs/sprints/SPRINT-147.md` (LF). Twelve parts. The brief's items: (a) is 3e, 3f and 3g; (b) is 3h; (c) and (h) are 3i; (d) is 3c; (e) is 3b; (f) is 3a, 3j, 3k and 3l; (g) is 3g. 3d is extra (see "Brief contradictions" 4).

**The lesson (h).** This log has no lessons or retrospective section; none of its headings is one, and the template's "Sprint Retrospective" was not used. Recent logs keep lessons inline, where the event is recorded: `SPRINT-141.md:678`, `SPRINT-142.md:110`, `SPRINT-145.md:1927`. So the lesson goes inline, at the end of cycle 3's record (3i).

**How earlier red runs are recorded (a).** CI-W2's red run has no section of its own. It appears:
- in the affected tasks' ledger rows, with run id and cause (`:93`, `:94`);
- in the handoff ("**CI-W2 went red** (run `41060488976`)", `:601`);
- in the fixing task's plain words, as counts and "None was a product defect" (`:350`).

3b, 3g and 3k mirror that: the ledger row, C-2's table and the Phase.

### 3a — the header Status (line 5, the cell's end)

Old text:

````text
Next: K″, C-2 on K″, Step 7a cycle 3 |
````

New text:

````text
Step 7a cycle 3 found that one of TASK-14725's Docker facts had run red in CI for a test reason (the month it read was never sent); TASK-14725-T fix-up 4 (merged `a0c67f4f`) corrects the fact. Next: K‴, C-2 on K‴, Step 7a cycle 4 |
````

### 3b — the TASK-14725 ledger row (line 112)

Old text:

````text
the recorded approval method follows the same predicate (fix-ups 2–3, Step 7a cycle 2) | **DONE** (merged `2cfae13c`, fix-ups 2–3 merged `ab77c475`; GATE after each `build 0 / 144 / CA2100 114; Unit 1722; DemoSeed 170; Regression non-Docker 135`); Docker facts **CI-pending (C-2)**;
````

New text:

````text
the recorded approval method follows the same predicate (fix-ups 2–3, Step 7a cycle 2); the Skema month-view fact corrected and the year overview's D+5 mirror added (-T fix-up 4, Step 7a cycle 3) | **DONE** (merged `2cfae13c`, fix-ups 2–3 merged `ab77c475`, -T fix-up 4 merged `a0c67f4f`; GATE after each `build 0 / 144 / CA2100 114; Unit 1722; DemoSeed 170; Regression non-Docker 135`); Docker facts **CI-pending (C-2)**. C-2 on K′ (run `42013444380`) failed one of them, the Skema month-view fact, for a fixture reason, not a product fault; -T fix-up 4 corrects it;
````

### 3c — the merge record: fix-up 4 (after line 181)

Old text:

````text
| fix-up 2 0 B / 0 W / 5 N → fix-up 3 0 B / 0 W / 6 N | `ab77c475` |
````

New text:

````text
| fix-up 2 0 B / 0 W / 5 N → fix-up 3 0 B / 0 W / 6 N | `ab77c475` |
| 14725-T fix-up 4 (Step 7a cycle 3, Fable BLOCKER-1, NOTE-2, NOTE-3; `5e84766b`), + Orchestrator comment commits `c3d98208`, `a0447fe7`, `216f6721` | `216f6721` | GATE on `5e84766b`, and again at `a0c67f4f`: build 0 / 144 / CA2100 114; Unit 1722; DemoSeed 170; Regression non-Docker 135. Test-only: one file, `LeaverPastGraceByDateTests.cs`; no new fact (the year overview's mirror is inside its fact). REGEN: not needed | PASS (on `5e84766b`) | **Fable** READY-TO-MERGE (on `c3d98208`; 0 B / 0 W / 5 N: NOTE-1, NOTE-2 and NOTE-4, docstring precision, applied in `216f6721`; NOTE-3 and NOTE-5, no action) | c1 0 B / 1 W (the Skema mirror's "an off-by-one in either layer makes it 403" was false: a single-layer error is masked by the other admitting path; narrowed in `c3d98208`) → c2 0 B / 1 W (the same overstatement on the D+5 approve boundary fact: a validator-only error changes only the reason there; narrowed in `a0447fe7`) → c3 CLEAN (on `a0447fe7`) | `a0c67f4f` |
````

### 3d — TASK-14725's dispositions: the fix-up 1 claim fix-up 4 made true (line 252)

*Why a note:* the line says each Docker fact turns red with either layer off, "Fixed in -T fix-up 1". For the Skema fact that was not true until fix-up 4. Without a sent month, the month gate also refused under either mutation, so the day-6 half could not turn red (Fable cycle 3, BLOCKER-1). It is a record, so it gains a dated note.

Old text:

````text
on the Skema month view, either arm alone admits. Fixed in -T fix-up 1.
````

New text:

````text
on the Skema month view, either arm alone admits. Fixed in -T fix-up 1. *(Step 7a cycle 3, 2026-11-05: for the Skema fact this held only from -T fix-up 4. The fact had not sent the month, so the S124 month gate refused under either mutation too, and its day-6 half could not turn red. Fix-up 4, merged `a0c67f4f`, sends the month and asserts the date rule's reason.)*
````

### 3e — C-2's Commit row (line 529)

Old text:

````text
or the candidate Step 7a's fixes make (K′ `076ac29e`, then K″, the final one).
````

New text:

````text
or the candidate Step 7a's fixes make (K′ `076ac29e`, K″ `841a7020`, then K‴, the final one).
````

### 3f — C-2's Run row: the branch (line 530; Codex NOTE 2)

Old text:

````text
on branch `s147-ci-k`, through
````

New text:

````text
on branch `s147-ci-kppp` (K‴; Step 7a cycle 3, Codex NOTE 2: this row first named `s147-ci-k`, and the earlier runs used `s147-ci-kprime` for K′ and `s147-ci-kpp` for K″), through
````

### 3g — C-2's first-run row ("six facts") and the two earlier runs (line 532, then two new rows)

*Why here:* this table is where the final run lands. The two earlier runs are evidence for their own candidates only, so they get rows of their own and are not shown as the final result. The Result row (`:531`) stays CI-pending.

Old text:

````text
the re-dated `Remove_ActorRemovesGraceLeaver_AsToday`, and the six approval-method facts of fix-ups 2 and 3 in `LeaverPastGraceByDateTests` |
````

New text:

````text
the re-dated `Remove_ActorRemovesGraceLeaver_AsToday`, and the six facts of fix-ups 2 and 3 in `LeaverPastGraceByDateTests` (five approval-method facts and the stand-in fact; Step 7a cycle 3, Fable NOTE-1). On K‴ also fix-up 4's corrected Skema month-view fact and the year overview's D+5 mirror |
| **Earlier run: K′ (red)** | Run `42013444380`, 2026-11-05, on `076ac29e` (branch `s147-ci-kprime`, draft PR #49). Six jobs green: docs, gitleaks, lizard, frontend-build, E2E, smoke. **build-and-test red:** Unit 1722; DemoSeed 170; Smoke 7; Regression **2429/2430, 1 failed**. The failing fact was `LeaverPastGraceByDateTests.Day6_LeaverStillActive_ManagerSkemaMonthView_Is403_HrIs200_Day5ManagerIs200`. Its D+5 mirror, the manager's Skema month view on the last grace day, expected 200 and got 403. **A test defect, not a product fault** (found by a trace and, independently, by Fable at Step 7a cycle 3). The fact seeded no May approval period, so the S124 leader-tier month gate refused the manager for a reason that is not the date (`SkemaEndpoints.cs:535-553`; `ApprovalReadTier.MonthNotSubmittedForbidden`, "The month has not been submitted for approval"). The same gate made the fact's D+6 half vacuous: it was 403 on the code before TASK-14725 (`c13024cd`) and under mutations M-25 and M-26. The product was right throughout. Fixed by TASK-14725-T fix-up 4 (merged `a0c67f4f`). PR #49 closed unmerged, branch deleted. Evidence for K′ only |
| **Earlier run: K″** | Run `42024818530` on `841a7020` (branch `s147-ci-kpp`, draft PR #50): `<K″ run result>`. The Skema fact is unchanged on K″, so the same red was expected; the run is evidence for the six facts of fix-ups 2–3. PR #50 is closed unmerged, and its branch deleted, when the run finishes. Evidence for K″ only |
````

### 3h — cycle 2's Fable W-1 row (line 582)

*Why a note:* "was in progress at review time" was true when written, so it stays, with a dated note on what followed.

Old text:

````text
A run on K′ (`42013444380`) was in progress at review time; it is evidence for K′ only, so C-2 runs again on K″ |
````

New text:

````text
A run on K′ (`42013444380`) was in progress at review time; it is evidence for K′ only, so C-2 runs again on K″ *(Step 7a cycle 3, 2026-11-05: that run finished red on one Docker fact, a test defect, not a product fault; see "C-2 — the checkpoint on K". Its fix, TASK-14725-T fix-up 4, makes K‴ the final candidate, so this condition now resolves at C-2 on K‴)* |
````

### 3i — Step 7a cycle 3's record, and the lesson (line 591)

Old text:

````text
Cycle 3 runs on K″, which carries fix-ups 2 and 3 and these records. **Cycles:** `<7a cycles>`.
````

New text:

````text
Cycle 3 runs on K″, which carries fix-ups 2 and 3 and these records.

### Cycle 3 — on K″ = `841a7020` (2026-11-05)

| Lens | Model | Verdict | Artifact |
|---|---|---|---|
| External: Codex, base `b510e45b` | Codex (`gpt-6-astra`) | FINDINGS: 0 BLOCKER, 0 WARNING, 2 NOTE | `.claude/reviews/SPRINT-147-step7a-codex-c3.md` |
| Internal: `reviewer` | `claude-fable-5-1` | CHANGES-REQUIRED: 1 BLOCKER, 1 WARNING, 4 NOTE | `.claude/reviews/SPRINT-147-step7a-reviewer-c3.md` |

*In plain words:* Codex found no defect, only two places where the records disagreed with each other. Fable found why C-2's run on K′ had gone red, and the cause was the test, not the product. The test checks that a leaver's manager can still open the leaver's month in Skema on day 5 and cannot on day 6. Since S124 a manager may open only a month the employee has sent for approval, and the test never sent it. So on day 5 the manager was refused for that unrelated reason: the red. On day 6 the refusal the test expected came from the same unrelated rule, so that half would have passed even on the code before the fix: it proved nothing. TASK-14725-T fix-up 4 sends the month first and checks the refusal's reason, not only its status. Every other TASK-14725 test was re-checked against its route's other gates for the same trap, and none had it. The rest were record corrections.

| Finding | What | Disposition |
|---|---|---|
| Fable BLOCKER-1 | C-2 on K′ (run `42013444380`) red, 1 of 2430: the Skema month-view fact's D+5 mirror got 403. The fact seeded no May period, so the S124 leader-tier month gate refused for a reason that is not the date (`SkemaEndpoints.cs:535-553`, `MonthNotSubmittedForbidden`). The same gate made the D+6 half vacuous (403 on `c13024cd` and under M-25 / M-26) | Fixed by TASK-14725-T fix-up 4 (merged `a0c67f4f`; test-only, see the merge record):<br>• the month is seeded as sent;<br>• the D+6 403's body must contain "Target employee not found" and not "has not been submitted";<br>• the Red conditions are rewritten, and are true: on `c13024cd`, under M-25 alone and under M-26 alone, the manager gets 200.<br>The audit of every other TASK-14725 fact against its route's other gates (the month gate, the employed-month bound) found them sound, and none changed; Fable's fix-up 4 review re-checked it independently. A new candidate, K‴; C-2 on it; cycle 4 |
| Fable W-1 | The records said the K′ run was "in progress" (`:582`), named K″ the final candidate (`:5`, `:604`, `:607`), and named branch `s147-ci-k` (`:530`) | Fixed in these records: a dated note at cycle 2's W-1 row; C-2's rows record the K′ and K″ runs and the branch names; Status, Phase and Next action name K‴. Carried: cycle 2's W-1 (approval conditional on a green C-2), now at C-2 on K‴; cycle 2's N-5 (the header table's placeholders), at C-4 |
| Fable NOTE-1 | `ab77c475`'s commit message says "six label pins and a stand-in pin", and implies that the cross-organisation, no-database-role and inactive-home-organisation cases are pinned. The file has six new facts in total, five label facts and the stand-in fact. Those three cases are covered by construction only | Recorded here; a commit message stays as written. The records say "six facts": C-2's first-run row, SEC-058's pins, and ADR-040's "Pinned by", which now also names the three cases as covered by construction |
| Fable NOTE-2 | The test class header put every manager under the P-6 setup rule (LocalLeader only), which contradicts section (g)'s HR-tier actors | Fixed in fix-up 4: the header scopes the rule to sections (a)–(f) and describes (g)'s actors and its `LeaverM` seed |
| Fable NOTE-3 | ADR-040 says "each access fact … has D+5 boundary pins", but the year-overview fact had none, and the Skema mirror was the red one | Fixed: fix-up 4 adds the year overview's D+5 mirror and makes the Skema mirror sound. ADR-040's sentence now says, route by route, what each counterpart can and cannot detect |
| Fable NOTE-4 | The Information-level `manager-grace-ended` line beside HR's 200; one extra predicate read inside the held advisories | No action (Fable: acceptable). Both are already in ADR-040's note ("Reading the log"; the label bullet's cost) |
| Fable checks 1–5 | Cycle 2's findings closed; the label correct, and each label pin red under its named mutation; no regression in approve, reject, reopen, four-eyes, lock order, audit shape or payroll; every cited line verified; the next green run's Regression total expected at 2436 | No action; 2436 is C-2's check on K‴ |
| Codex NOTE 1 | SEC-058's residual still said "consistency only; the send refuses a month with no employed day". That leaves out the retroactive-end-date case ADR-040 records: an already-sent month can be approved and paid until the switch-off | Fixed in these records: SEC-058's residual now says what ADR-040 says, with S148 stage 0's fix |
| Codex NOTE 2 | C-2's run row named branch `s147-ci-k` | Fixed in these records: the row names `s147-ci-kppp` for K‴, and the earlier runs' rows name `s147-ci-kprime` (K′) and `s147-ci-kpp` (K″) |
| Codex, the rest | Cycle 2's W1 and W2 confirmed closed; no fix-up regression; C-2 honestly pending, with K′'s run not claimed as K″ evidence | No action |

*An observation worth keeping, recorded neutrally (from Fable's cycle-3 review).* Both lenses reviewed this fact in three TASK-14725-T review cycles and again at Step 7a cycle 2, and none caught the month gate; the CI run did. Two mechanisms made that possible:
- **A status-only assertion can be satisfied by the wrong gate.** On a route with several gates, a test that asserts only a status (here, 403) passes with whichever gate refuses first, so it can pass for the wrong reason. Asserting the refusal's reason ties it to the one rule under test.
- **A Docker fact is unrun until C-2.** Its Red conditions are an argument that a reviewer checks against the code, not a failure anyone has watched happen.

Fix-up 4 applied the first point to every TASK-14725 fact. The second is why C-2 runs before the close.

Cycle 4 runs on K‴, which carries TASK-14725-T fix-up 4 and these records; it is cycle 4 of the cap of 5. **Cycles:** `<7a cycles>`.
````

### 3j — As of (line 597)

Old text:

````text
| **As of** | 2026-11-05, K″ = `<K sha>`: K `c13024cd`; its Step 7a record fixes `bf3235b1`; K′ `076ac29e` (the TASK-14725 merge `2cfae13c` and its records); cycle 2's record fixes `c1934e78`; the fix-ups 2–3 merge `ab77c475`; and these records (local master, not pushed; origin/master `7cc70be8` was merged in at `c29764ce`) |
````

New text:

````text
| **As of** | 2026-11-05, K‴ = `<K sha>`: K `c13024cd`; its Step 7a record fixes `bf3235b1`; K′ `076ac29e` (the TASK-14725 merge `2cfae13c` and its records); cycle 2's record fixes `c1934e78`; the fix-ups 2–3 merge `ab77c475`; K″ `841a7020` (their records); the TASK-14725-T fix-up 4 merge `a0c67f4f`; and these records (local master, not pushed; origin/master `7cc70be8` was merged in at `c29764ce`) |
````

### 3k — Phase, the head (line 604)

Old text:

````text
| **Phase** | **Step 7a cycle 2 done on K′ = `076ac29e`** (2026-11-05; see "Step 7a (C-3)"). Its record fixes are in `c1934e78`; its code fix, TASK-14725 fix-ups 2 and 3, merged `ab77c475`. **Next: K″ = `<K sha>` (these records), the final candidate; C-2 on K″, Step 7a cycle 3, then the close.**
````

New text:

````text
| **Phase** | **Step 7a cycle 3 done on K″ = `841a7020`** (2026-11-05; see "Step 7a (C-3)"). Its blocker was a test defect that C-2 on K′ had exposed (run `42013444380`, red on one Docker fact; the product was right). Its record fixes are these records; its test fix, TASK-14725-T fix-up 4, merged `a0c67f4f`. C-2 on K″ (run `42024818530`) is evidence for K″ only (see "C-2 — the checkpoint on K"). **Next: K‴ = `<K sha>` (these records), the final candidate; C-2 on K‴, Step 7a cycle 4, then the close.** **Step 7a cycle 2 done on K′ = `076ac29e`** (2026-11-05). Its record fixes are in `c1934e78`; its code fix, TASK-14725 fix-ups 2 and 3, merged `ab77c475`.
````

### 3l — Next action (line 607, the whole cell)

Changed from C-1d's cell:
- K‴ throughout;
- the expected Regression total 2436 (Fable cycle 3; Fable's fix-up 4 NOTE-5);
- "fix-ups 2–4";
- the final Step 7a artifacts come from cycle 4. `.claude/reviews/SPRINT-147-step7a-codex.md` is today a byte-identical copy of cycle 3's artifact, headed "cycle 3 (final)" and reviewed against `841a7020`. `SPRINT-147-step7a-reviewer.md` does not exist.
- teardown names the 24 local `worktree-agent-*` branches, all merged into master at `a0c67f4f`. C-1d's "the agent worktrees are already removed" is true again of the worktrees, but the branches remain.

Old text:

````text
| **Next action** | (1) **K″**: these records (`.claude/plans/s147/C-1d-DOCS-OWED.md`) on top of `ab77c475`, committed on master; not pushed. K″ is the final candidate. (2) **C-2 on K″** (`PLAN-s147.md:426`): push to `s147-ci-kpp`; a draft PR whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2; never merge"; one background watch; `headSha` equals K″; all seven jobs green. Check: Unit 1722; Regression with the Docker facts all executed, the six new label facts included; no pre-existing D+6 fact red. Then fill `<C-2 run id>`, `<C-2 date>` and the counts in "C-2 — the checkpoint on K", and flip every "CI-pending (C-2)" to CI-verified with the run id: the tidy pass's facts, TASK-14725's facts (fix-ups 2–3 included), SEC-058's Docker pins and the index row. Close the PR unmerged and delete the branch. Red → a fix task → a new candidate → C-2 again. (3) **C-3, Step 7a cycle 3** on K″, both lenses: `codex review --base b510e45b`; `reviewed-against-commit: <K sha>` (K″'s sha); the reviewer on Fable 5.1, or an Opus stand-in with `.claude/reviews/SPRINT-147-review-floor-WAIVED.md`; each artifact saved before acting; the verdicts into "Step 7a (C-3)". (4) **C-4, the close commit**, a docs-only child of K″: every angle-bracket placeholder filled, the Step 7a header table's included (Fable c2 N-5); `git diff --stat` from K″ lists only `docs/**`; the guard's gates (`docs/WORKFLOW.md:41-47`); the commit message written through a file, never inside the Bash command; push; watch CI to green. (5) **C-5**: the Test Verified line with the close run id; the model-routing register's S147 row (Opus stand-in cycles: 14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2; Fable from 14713 onward, TASK-14725, its fix-ups and Step 7a included); the handoff "closed"; teardown (the agent worktrees are already removed). The Step-0a DEBT branches stay the owner's call |
````

New text:

````text
| **Next action** | (1) **K‴**: these records (`.claude/plans/s147/C-1e-DOCS-OWED.md`) on top of the TASK-14725-T fix-up 4 merge (`a0c67f4f`), committed on master; not pushed. K‴ is the final candidate. (2) **C-2 on K‴** (`PLAN-s147.md:426`): push to `s147-ci-kppp`; a draft PR whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2; never merge"; one background watch; `headSha` equals K‴; all seven jobs green. Check: Unit 1722; Regression 2436 with the Docker facts all executed (K′'s 2430 plus the six facts of fix-ups 2–3; fix-up 4 adds assertions, not facts), the corrected Skema month-view fact included; no pre-existing D+6 fact red. Then fill `<C-2 run id>`, `<C-2 date>` and the counts in "C-2 — the checkpoint on K", and flip every "CI-pending (C-2)" to CI-verified with the run id: the tidy pass's facts, TASK-14725's facts (fix-ups 2–4 included), SEC-058's Docker pins and the index row. Close the PR unmerged and delete the branch. Red → a fix task → a new candidate → C-2 again. (3) **C-3, Step 7a cycle 4** on K‴ (cycle 4 of the cap of 5), both lenses: `codex review --base b510e45b`; `reviewed-against-commit: <K sha>` (K‴'s sha); the reviewer on Fable 5.1, or an Opus stand-in with `.claude/reviews/SPRINT-147-review-floor-WAIVED.md`; each artifact saved before acting; the verdicts into "Step 7a (C-3)". The final artifacts the Step 7a header table names come from cycle 4: `.claude/reviews/SPRINT-147-step7a-codex.md` is today a copy of cycle 3's, reviewed against `841a7020`, and `SPRINT-147-step7a-reviewer.md` does not exist yet. (4) **C-4, the close commit**, a docs-only child of K‴: every angle-bracket placeholder filled, the Step 7a header table's included (Fable c2 N-5); `git diff --stat` from K‴ lists only `docs/**`; the guard's gates (`docs/WORKFLOW.md:41-47`); the commit message written through a file, never inside the Bash command; push; watch CI to green. (5) **C-5**: the Test Verified line with the close run id; the model-routing register's S147 row (Opus stand-in cycles: 14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2; Fable from 14713 onward, TASK-14725, its fix-ups 2–4 and Step 7a included); the handoff "closed"; teardown: the 24 local `worktree-agent-*` branches, all merged into master at `a0c67f4f` (the agent worktrees are removed). The Step-0a DEBT branches stay the owner's call |
````

---

## Not drafted here

- **`docs/SECURITY.md:146`.** It names no individual pin and claims no detection. Its SEC-058 sentence ("fixed in code; it closes when C-2 runs its Docker pins green") stays true, and fix-up 4 changes no product behaviour. It also says "On every one of these surfaces an employed-month bound runs after the gate". That is about a switched-off subject's terminated-inclusive surfaces, and Codex did not flag it. Left as is.
- **TASK-14725's dispositions, "Docker facts CI-pending (C-2)."** (`:260`). Left as is. The ledger row (3b), C-2's rows (3g) and the Phase (3k) carry the K′ result, and the line stays true.
- **`ab77c475`'s commit message** (Fable NOTE-1). A commit message is not rewritten; cycle 3's table records the correction.
- **Fable NOTE-4 (cycle 3), and Fable's fix-up 4 NOTE-3 and NOTE-5.** No action, as Fable judged. NOTE-5's 2436 is in Next action (2).
- **HRP-011, `QUALITY.md`, performance F7.** Fix-up 4 changes tests only, and no HR process, grade or cost moves.
- **The model-routing register's S147 row.** At C-5, as Next action (5) says.
- **ADR-040's closing "The Docker facts are CI-pending until C-2 (`<C-2 run id>`)"** (`:616`). Left as is. `<C-2 run id>` already means the final candidate's run.

## Brief contradictions, deviations and open points

**Claims about the existing code and state (checked; the code governs):**
1. **"Codex cycle 2 and Fable's Step 5a review are running; merge: placeholder."** Both finished, and fix-up 4 merged while this was drafted. Local master is `a0c67f4f`, "Merge TASK-14725-T fix-up 4 …", 2026-11-05 14:13. The branch had five commits, not two: `5e84766b`, then `c3d98208`, `a0447fe7` and `216f6721` (all Orchestrator, comment only). The reviews:
   - **Codex c2** (`S147-TASK-14725-fu4-5a-codex-c2.md`, on `c3d98208`): 0 B / 1 W at `LeaverPastGraceByDateTests.cs:176`. The D+5 approve boundary's "either layer's use of it" was false. Narrowed in `a0447fe7`.
   - **Codex c3** (`-codex-c3.md`, on `a0447fe7`): CLEAN.
   - **Fable** (`S147-TASK-14725-fu4-5a-reviewer.md`, on `c3d98208`): READY-TO-MERGE, 0 B / 0 W / 5 N. NOTE-1, -2 and -4 were applied in `216f6721`.
   - **The merge GATE** (`gate-fu4-*.log`, 14:15–14:16): build 0 errors / 144 warnings / 114 distinct CA2100; Unit 1722; DemoSeed 170; Regression non-Docker 135.

   So I filled the brief's `<fu4 merge>`, `<fu4 GATE>`, `<fu4 Codex c2 verdict>` and `<fu4 Fable verdict>` with these verified values; the only new placeholder is `<K″ run result>`. If the Orchestrator prefers the fills at its own apply, each value is in one place per part (map in "How to apply").
2. **No lessons section in this log.** `SPRINT-147.md` has none; its headings are the summary, the refinement, Step 0b, the ledger, the merge records, the tidy list, C-1, C-2, Step 7a and the handoff. The lesson is placed inline, at the end of cycle 3's record, as `SPRINT-141.md:678`, `SPRINT-142.md:110` and `SPRINT-145.md:1927` do.
3. **"If SEC-058's pins mention the Skema fact".** They name the class, `LeaverPastGraceByDateTests`, not the fact. I drafted the dated note anyway (in 2b), because SEC-058's closure is defined by these Docker pins going green, and one of them was red in a C-2 run. 2b also refreshes the six facts' lines. If the note is unwanted, keep 2b's line refresh and drop the italic sentence.
4. **An extra record that fix-up 4 makes true (3d, not in the brief).** `SPRINT-147.md:252` records Fable c1 WARNING 1 as "Fixed in -T fix-up 1", saying each fact turns red with either layer off, "on the Skema month view, either arm alone admits". For the Skema fact that was not true until fix-up 4: without a sent month, the month gate refused under M-25 and M-26 too. 3d adds a dated note.
5. **NOTE-3, made fully true.** After fix-up 4 not every access fact has a D+5 pin of its own. The reject fact (`:323` at `a0c67f4f`) shares approve's boundary fact, since it runs the same two arms. The Skema mirror detects only an error in the shared `IsPastGrace`, or a broken seed. So 1a describes each counterpart's reach rather than claiming "each access fact has D+5 boundary pins".
   - A reject D+5 fact would be a test-side addition, below these records. Fix-up 4's audit and Fable's fix-up 4 review judged the reject fact sound.
6. **The six facts' lines (1b–1h, 2b, not in the brief).** Fix-up 4 moved them 48 lines down. ADR-040's note declared their basis as `ab77c475`, which would stay technically true, but a reader at K‴ would land on the wrong facts. The file is now merged and settled, so the lines are refreshed to `a0c67f4f` and the basis sentence says so.
7. **The teardown** (`SPRINT-147.md:607`, from C-1d, "the agent worktrees are already removed"). True of the worktrees at `a0c67f4f` (`git worktree list` shows the main checkout only). But 24 local `worktree-agent-*` branches remain, all merged into master, while Step 0a recorded "local branches: master only" at the open (`:13`). 3l's teardown names them.
8. **The final Codex artifact.** `.claude/reviews/SPRINT-147-step7a-codex.md` is byte-identical to `-codex-c3.md` (headed "cycle 3 (final)", reviewed against `841a7020`). Cycle 4 supersedes it, and the close guard may read it, so 3l says so.
9. **Verified as the brief states:**
   - Run `42013444380` (from its saved log): Unit 1722, DemoSeed 170, Smoke 7; Regression "Failed: 1, Passed: 2429, Total: 2430". The failing fact `…ManagerSkemaMonthView_Is403_HrIs200_Day5ManagerIs200` got "Expected: OK / Actual: Forbidden", and the log has one `##[error]`.
   - The month gate: `SkemaEndpoints.cs:535-553`, with `ApprovalReadTier.cs:116-121`'s text.
   - The fix-up 4 diff: one file, and 14 facts before and after.
   - The gate figures on `5e84766b`, and 5α PASS on `841a7020..5e84766b`.
   - Not verifiable offline: the PR numbers #49 and #50, and whether PR #49 and its branch are closed. These are taken from the brief.

**Claims about required behaviour:** none contradicted. The expected total of 2436 is Fable's (cycle 3, and fix-up 4 NOTE-5): 2430 plus six, with fix-up 4 adding assertions only. That fits the fact count of 14 before and after fix-up 4.

**Needs a ruling (the Orchestrator's):**
- 3d (the extra dated note at `:252`): keep or drop. My lean is keep, because the line was untrue for the Skema fact until fix-up 4.
- 2b's italic note on SEC-058: keep or drop. My lean is keep.
- Whether to take the filled fix-up 4 values (contradiction 1) or restore placeholders. My lean is take them: each is verified against an artifact or a log.

## Revision history

| Draft | Date | By | What |
|---|---|---|---|
| 1 | 2026-11-05 | `planner` (Opus 5.5) | First draft at `841a7020`, finished after fix-up 4's merge `a0c67f4f` landed (docs unchanged by it). Blocks 1–3, 22 replacements:<br>• **ADR-040's "Pinned by":** what each D+5 counterpart detects, the Skema pin's red run, "six facts", and the six facts' lines refreshed with their basis.<br>• **SEC-058:** the residual aligned with ADR-040; the pins' lines and a dated note.<br>• **The sprint log:** Status, the ledger row, fix-up 4's merge row, a dated note on the fix-up 1 claim, C-2's Commit, Run and first-run rows with the K′ and K″ runs, cycle 2's W-1 note, Step 7a cycle 3's record and the lesson, As of, Phase, Next action.<br>The brief's fix-up 4 placeholders are filled with verified values. `SECURITY.md` unchanged |
