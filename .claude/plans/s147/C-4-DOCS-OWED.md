# C-4 — the close commit: apply-ready records

## Values to verify before applying

The draft is **conditional on C-2 on K‴ being green**. Check these against run `42036154930`'s log before applying. If any differs, edit the draft: each value's parts are listed, so it can be changed in one pass.

1. **The run's conclusion:** success, all seven jobs green (build-and-test, frontend-build, E2E, smoke, docs, gitleaks, lizard). Written in 5a, 6b, 6e, 6f, 7c, 7k, 7n. Its identity: `headSha` = `a3ce6f7f`, branch `s147-ci-kppp`, draft PR #51, 2026-11-05 (7b).
2. **Unit 1722.** Written in 5a, 7c, 7n.
3. **DemoSeed 170.** Written in 5a, 7c, 7n. (The "DemoSeed 170" in 6e and 6f is the tidy tasks' own gate figure, already there.)
4. **Smoke 7.** Written in 5a, 7c, 7n.
5. **Regression 2436/2436** (Failed 0, Skipped 0). Written in 1c, 2b, 5a, 6b, 6i, 7c, 7g, 7k, 7n.

Two more values are not in the brief:
- **Frontend 84 files / 1089 tests, and E2E 7.** These are run `42013444380`'s figures on K′ (its log: "Test Files 84 passed", "Tests 1089 passed", "7 passed"). No frontend file changed from K′ to K‴ (`git diff --stat 076ac29e a3ce6f7f -- frontend` is empty), so the same figures are expected. Written in 7c and 7n. The INDEX row's 1089fe (5a) and 6h's "vitest 84 files / 1089" were already there.
- **PR #51 closed unmerged and `s147-ci-kppp` deleted** before the close commit (the brief: "the evidence branches already deleted with their PRs"). Written in 7b and 7o.

| | |
|---|---|
| **What this is** | The exact text for the records of S147's close commit (checkpoint C-4). They fill every placeholder in `docs/**` and turn every "CI-pending (C-2)" claim into CI-verified with run `42036154930`. They mark SEC-058 CLOSED, record Step 7a cycle 4 and fold in its notes, and move the sprint log's current-state cells to the state after the close commit is pushed. Sources:<br>• the cycle-4 reviews (`.claude/reviews/SPRINT-147-step7a-{codex,reviewer}-c4.md`, byte-identical to the final `SPRINT-147-step7a-{codex,reviewer}.md`);<br>• the K′ run's log (`ci-kprime.log`) and the K‴ PR body (`pr-kppp.md`), both in the session scratchpad;<br>• `git` at `a3ce6f7f`;<br>• the Orchestrator's brief.<br>The Orchestrator applies each block as a string replacement, into the close commit, a docs-only child of K‴. The `planner` drafts; the Orchestrator applies. Every target is under `docs/**` |
| **Status** | DRAFT 1 (`planner`, Opus 5.5), 2026-11-05 |
| **Base commit** | `a3ce6f7f` (K‴, local master). Every Old text is copied from it. A script counted each Old text as an exact substring of its whole target file, in the `a3ce6f7f` blob and in the working copy (clean for `docs/**`): each count is 1. It then applied all replacements in order, on a copy, and counted each Old text again just before its own replacement: each count is 1. No two replacements in one file overlap. Line numbers are at `a3ce6f7f` |
| **Orchestrator model** | S147: Opus 5.5 seat; drafting `planner` (Opus 5.5); judgment `adjudicator` / `reviewer` on Fable 5.1 |
| **Timing** | Apply after checking "Values to verify", and after filling `<K″ run result>` (see "Not drafted here"). Then commit the close, push, and go on to C-5 |
| **Owner approval** | None needed. No block touches `SYSTEM_TARGET.md` |

## What this is, in plain words

S147's work is finished and reviewed. Two things were still open, and both are now settled:
- **C-2 on K‴ ran green.** C-2 is the CI run that executes the Docker tests the owner's machine cannot run. K‴ (`a3ce6f7f`) is the final candidate. Run `42036154930` passed all seven jobs, with 2436 of 2436 regression tests. That is the proof the records were waiting for. The leaver fix (TASK-14725) and the tidy pass's tests now count as verified in CI. **SEC-058**, the manager's day-6 cut-off, closes with them: the fix was in the code, and the register closes a row only when its tests have run green in CI.
- **Step 7a passed at cycle 4.** Neither lens found a defect. Codex left one note and Fable one warning with five notes. The warning was the condition that C-2 on K‴ be green, and it is met. Four of the notes are record fixes, made here. The fifth is a possible extra test, left as a candidate for S148.

The close commit changes documents only, so it needs no further review cycle (`docs/WORKFLOW.md:38`: "A docs-only sprint-close commit (no code changes) is exempt").

The records:
- **Placeholders.** Every angle-bracket placeholder in `docs/**` is filled, except `<K″ run result>`, which the Orchestrator fills.
- **CI-pending → CI-verified.** Every "CI-pending (C-2)" claim becomes CI-verified with run `42036154930`: in the sprint log, the INDEX row and ADR-040. Earlier records keep their words and gain a dated note; current-state cells are updated in place.
- **SEC-058 → CLOSED.** The register's own row mirrors how SEC-053 was closed this sprint. SEC-057's note, `docs/SECURITY.md`, ADR-040, `QUALITY.md` and the sprint log follow.
- **Step 7a cycle 4** is recorded after cycle 3, in the same shape. The Step 7a header table names the final artifacts.
- **The sprint log's current-state cells** (Status, Phase, As of, Next action) describe the state after the close commit is pushed: closed at C-4, with C-5 left.

Rules kept:
- CI-verified only with a run id.
- Nothing is called done before it is. C-5 (the Test Verified line, the model-routing register's row, the handoff "closed", the teardown) is named as remaining, not drafted.
- The close commit has no sha before it is made, so the records call it "the close commit, a docs-only child of K‴ `a3ce6f7f`".

## How to apply

- Each block names its **Target**, the **Old text** (one exact match at `a3ce6f7f`) and the **New text**. Apply each part as its own replacement, in order.
- **Line endings:** every target is LF, in the blob and in the working copy (a carriage-return count of 0). The targets are ADR-040, `docs/operations/security-finding-register.md`, `docs/SECURITY.md`, `docs/QUALITY.md`, `docs/sprints/INDEX.md` and `docs/sprints/SPRINT-147.md`. The one multi-line New text (7k) is LF.
- **Non-overlap:** several parts share a line, and none overlaps another:
  - 2b, 2c and 2d all sit in SEC-058's row (register line 218);
  - 6a and 6b are the head and the end of the Status cell (log line 5).
- **The sprint log is split into two blocks with the same target:** Block 6 (lines 5–478) and Block 7 (lines 530–640). Apply Block 6 before Block 7. The order does not change any count, because no Old text appears in another part's New text.
- **Placeholders left:** `<K″ run result>` (`SPRINT-147.md:535`), the Orchestrator's to fill. After this draft, a grep for `<[^<>]{1,40}>` over the six targets finds only it, and the legitimate angle brackets (HTML `<br>`, generics, template text).

## Map: each owed item → its block

| # | Owed item (source) | Block | State at `a3ce6f7f` |
|---|---|---|---|
| 1 | Placeholders in `docs/**` (brief item 1) | `<K sha>`: 7a (Fable NOTE-2), 7d, 7e, 7l, 7n, 7o, 5a. `<C-2 run id>`: 1c, 2b, 2c, 5a, 6e–6j, 6m, 7b, 7o. `<C-2 date>`: 7b, 7o. `<C-2 Regression n>`: 7c, 5a. `<7a …>`: 7d, 7e, 7f, 7k, 5a. `<close date>`: 5a | 33 occurrences, counted with the grep in "How to apply": the log 25, INDEX 5, register 2, ADR-040 1. All but `<K″ run result>` are filled |
| 2 | CI-pending (C-2) → CI-verified (brief item 2) | the tidy pass: 6d, 6e–6h, 6j, 6m; TASK-14725: 6i, 6l; SEC-058's pins: 2c, 2d; ADR-040: 1b, 1c; INDEX: 5a; C-2's table: 7c; cycle 2's W-1 condition: 7h | "CI-pending (C-2)" |
| 3 | SEC-058 → CLOSED (brief item 3) | the register: 2a, 2b; `SECURITY.md`: 3a; ADR-040: 1a; `QUALITY.md`: 4a; the log: 6b, 6c, 6i, 6k, 7g; INDEX: 5a | "fixed in code; closes at C-2" |
| 4 | The log's current-state cells (brief item 4) | Status 6a, 6b; As of 7l; Phase 7n; Next action 7o | K‴ is the next candidate |
| 5 | The INDEX row (brief item 5) | 5a | placeholders; "close candidate"; CI-pending |
| 6 | Codex c4 NOTE = Fable NOTE-4 (the Decide track) | 7m | "**Next:** … `O-10-DOCS-OWED.md` (in progress)" (`:633`) |
| 7 | Fable c4 NOTE-1 (cycle 3's W-1 row, the lines' commit) | 7j | no commit named (`:608`) |
| 8 | Fable c4 NOTE-2 (C-2's Commit row) | 7a | "K = `<K sha>`" (`:530`) |
| 9 | Fable c4 NOTE-3 (Next action (3), overtaken) | 7o | "is today a copy of cycle 3's" (`:640`) |
| 10 | Fable c4 WARNING-1 (green C-2) and NOTE-5 (reject's D+5); cycle 4's record | 7k (the record, both findings' rows); 7c (the result) | absent |
| 11 | Cycle 2's N-5 (the header table's placeholders, carried to C-4) | 7d, 7e, and a dated note at 7i | "At the close (C-4)" (`:590`) |

**Count: 38 replacements** (Block 1: 3; Block 2: 4; Block 3: 1; Block 4: 1; Block 5: 1; Block 6: 13; Block 7: 15).

---

## Block 1 — ADR-040's Step 7a note: SEC-058 closed; the Docker facts CI-verified

**Target:** `docs/knowledge-base/decisions/ADR-040-employee-timeline-and-as-of-resolution.md` (LF; the note "S147 Step 7a, 2026-11-05 (TASK-14725)", lines 509–648). Three parts.

*Why these are flipped in place:* each states a current status ("closes when", "CI-pending until"), not what happened at a past moment. The red run on K′ stays recorded as it is (lines 629–631).

### 1a — SEC-058's status (line 515)

Old text:

````text
it is registered as SEC-058 (fixed in code; it closes when C-2 runs its Docker pins green).
````

New text:

````text
it is registered as SEC-058 (fixed in code, and CLOSED on 2026-11-05, when C-2 on the final candidate, run `42036154930`, ran its Docker pins green).
````

### 1b — the Skema fact's correction (line 632)

The line before ends "Fix-up 4's correction is", so the sentence reads on.

Old text:

````text
CI-pending until C-2 on K‴.
````

New text:

````text
CI-verified: C-2 on K‴ ran it green (run `42036154930`, 2026-11-05).
````

### 1c — the note's closing line (line 648)

Old text:

````text
The Docker facts are CI-pending until C-2 (`<C-2 run id>`).
````

New text:

````text
The Docker facts are CI-verified: C-2 on the final candidate, K‴ (`a3ce6f7f`), ran them all green (run `42036154930`, 2026-11-05; Regression 2436/2436).
````

## Block 2 — The security register: SEC-058 CLOSED, and SEC-057's pointer

**Target:** `docs/operations/security-finding-register.md` (LF). Four parts:
- 2a is in SEC-057's row (line 217);
- 2b, 2c and 2d are in SEC-058's row (line 218): 2b and 2c in the status cell, 2d in the AS-FOUND / AS-CLOSED cell.

*How the closed wording is mirrored:* SEC-053 was closed this sprint as "**CLOSED (S147, 2026-11-01): TASK-14704 merged `002787c6`; its Docker facts green in CI-W2b (run `41310143330`, Regression 2353/2353), after TASK-14719 fixed their seed (CI-W2 had failed them on untyped nulls).**" (register line 213). 2b takes the same shape: the merge, the run with its Regression total, and the earlier red run with its cause. SEC-058's AS-CLOSED evidence already exists ("AS-CLOSED (at `0a64c1eb`)"), so the cell is not reshaped.

### 2a — SEC-057's pointer to SEC-058 (line 217)

Old text:

````text
(SEC-058: fixed in code; it closes when C-2 runs its Docker pins green)
````

New text:

````text
(SEC-058: fixed in code, and CLOSED on 2026-11-05, when C-2 on the final candidate, run `42036154930`, ran its Docker pins green)
````

### 2b — SEC-058's status (line 218, the status cell's head)

The original's colon is replaced by a closing sentence, so its next word takes a capital.

Old text:

````text
**FIXED IN CODE (S147, TASK-14725, merged `2cfae13c`); closes when C-2 runs its Docker pins green (`<C-2 run id>`):** the manager loses authority at D+6 by date
````

New text:

````text
**CLOSED (S147, 2026-11-05): TASK-14725 merged `2cfae13c`, with fix-ups 2–3 (`ab77c475`) and -T fix-up 4 (`a0c67f4f`); its Docker pins green in C-2 on the final candidate, K‴ `a3ce6f7f` (run `42036154930`, Regression 2436/2436), after C-2 on K′ (run `42013444380`) had failed one of them for a fixture reason, not a product fault.** The manager loses authority at D+6 by date
````

### 2c — SEC-058's pins (line 218, the status cell's end)

Old text:

````text
the Docker pins are CI-pending until C-2 (`<C-2 run id>`, filled in when green)
````

New text:

````text
the Docker pins ran green in C-2 on K‴ (run `42036154930`, 2026-11-05)
````

### 2d — the dated note on the Skema pin (line 218, the evidence cell)

*Why a note inside the note:* the italic note is a Step 7a cycle 3 record, true when written. It keeps its words and gains the outcome.

Old text:

````text
The product was right; the pins are CI-pending until C-2 on K‴.)*
````

New text:

````text
The product was right; the pins are CI-pending until C-2 on K‴. C-4, 2026-11-05: C-2 on K‴, run `42036154930`, ran them green, the corrected Skema fact included.)*
````

## Block 3 — `docs/SECURITY.md`: SEC-058 closed

**Target:** `docs/SECURITY.md` (LF; "HR only from day 6", line 146). One part. `SECURITY.md` describes the current rules, so the status is updated in place.

### 3a — SEC-058's status in the day-6 paragraph (line 146)

Old text:

````text
(S147 Step 7a, SEC-058: fixed in code; it closes when C-2 runs its Docker pins green)
````

New text:

````text
(S147 Step 7a, SEC-058: fixed in code, and CLOSED on 2026-11-05, when C-2 on the final candidate, run `42036154930`, ran its Docker pins green)
````

## Block 4 — `docs/QUALITY.md`: a dated note on SEC-058

**Target:** `docs/QUALITY.md` (LF; the "S147 re-grade (2026-11-04, at C-1)" section, Security & Access Control row, line 21). One part.

*Why a note, not an edit:* the file is "a stack of DATED snapshots" whose sections are "never rewritten" (`QUALITY.md:6-8`). The S147 section is dated at C-1, so it keeps its words. The grade does not move: SEC-058 was listed under "Held" as Low, and its closing changes no grade.

### 4a — SEC-058 in the S147 snapshot (line 21)

Old text:

````text
SEC-058 (Low) registered, fixed in code (it closes when C-2 runs its Docker pins green)
````

New text:

````text
SEC-058 (Low) registered, fixed in code (it closes when C-2 runs its Docker pins green; *C-4, 2026-11-05: closed, C-2 on K‴ run `42036154930` green*)
````

## Block 5 — The sprint index: S147's row

**Target:** `docs/sprints/INDEX.md` (LF; S147's row, line 116). One part, from "Step 7a: `<7a outcome>`" to the row's end.

*What changes and why:*
- **The Step 7a outcome** in plain words, one sentence per cycle.
- **Status:** "complete", as S146's row has it (`INDEX.md:115`).
- **The dates:** 2026-10-27 → 2026-11-05.
- **The tests:** C-2's figures, in S146's form "(CI, all executed)". The row said **1715u**, the tidy pass's gate count before TASK-14725, and C-2 on K‴ reports **1722** (see "Brief contradictions" 2).
- **The last column:** "✅ CI GREEN" with the run, as S146's row has it, plus the red run on K′ among the earlier checkpoints. C-2 on K″ is left out, because its result is not yet filled.

### 5a — S147's row, from the Step 7a outcome to the end (line 116)

Old text:

````text
Step 7a: <7a outcome> | close candidate (complete at the close) | 2026-10-27 → <close date> | 1715u + 170 demoseed + **<C-2 Regression n>r** (CI-pending (C-2)) + 7s + **1089fe** | CI-pending (C-2): `<C-2 run id>` on `<K sha>`, filled in when C-2 on the final candidate is green (the close commit after it is docs only). Earlier checkpoints: CI-W1 `40839583193` green; CI-W2 `41060488976` red 8/2353 (test-side faults, TASK-14719), CI-W2b `41310143330` green; CI-W3 `41335270197`, CI-W3b `41641201066`, CI-W4 `41708962373`, CI-W4b `41825042200`, all green |
````

New text:

````text
Step 7a: four cycles on both lenses (Codex, and Fable 5.1 on every cycle). Cycle 1 found that the manager's day-6 cut-off waited for the leaver job's switch-off; TASK-14725 now decides it by date (owner rulings 2026-11-05; SEC-058, closed). Cycle 2 found that the recorded approval method still named the manager in that case (fix-ups 2–3). Cycle 3 traced C-2's red run on K′ to a test that never sent the month it read; the product was right (TASK-14725-T fix-up 4). Cycle 4 found records only, folded into the close commit | complete | 2026-10-27 → 2026-11-05 | 1722u + 170 demoseed + **2436r** (CI, all executed) + 7s + **1089fe** | ✅ CI GREEN `42036154930` (C-2, all 7 jobs, on K‴ `a3ce6f7f`, 2026-11-05; the close commit after it is docs only). Earlier checkpoints: CI-W1 `40839583193` green; CI-W2 `41060488976` red 8/2353 (test-side faults, TASK-14719), CI-W2b `41310143330` green; CI-W3 `41335270197`, CI-W3b `41641201066`, CI-W4 `41708962373`, CI-W4b `41825042200`, all green; C-2 on K′ `42013444380` red 1/2430 (a test-fixture fault, fixed by TASK-14725-T fix-up 4) |
````

## Block 6 — The sprint log, part 1: the header, the ledger, the dispositions, C-1

**Target:** `docs/sprints/SPRINT-147.md` (LF). Thirteen parts, lines 5–478. Apply before Block 7.

**How ledger rows and dispositions are treated.** The rule this log already follows is visible at the TASK-14719 dispositions:
- **Ledger rows** are current-state, so they are flipped in place, as rows 93–94 were when CI-W2b ran.
- **Disposition bullets** keep their words and gain a dated italic note, as `:362` does: "**CI-pending** until CI-W2b. *(C-1: CI-verified at CI-W2b, run `41310143330` …)*".

The rows below follow the same rule.

### 6a — the header Status, its head (line 5)

Old text:

````text
| **Status** | **OPEN 2026-10-27; close candidate K committed at C-1 (2026-11-04).**
````

New text:

````text
| **Status** | **CLOSED 2026-11-05 at C-4** (opened 2026-10-27; close candidate K committed at C-1, 2026-11-04; the final candidate is K‴ `a3ce6f7f`, and the close commit is a docs-only child of it).
````

### 6b — the header Status, its end (line 5)

Old text:

````text
Next: K‴, C-2 on K‴, Step 7a cycle 4 |
````

New text:

````text
C-2 on K‴ ran green (run `42036154930`, 2026-11-05: all seven jobs; Regression 2436/2436), which closes SEC-058. Step 7a cycle 4 found records only, folded into the close commit, and Step 7a passed at cycle 4 of the cap of 5 on both lenses. Remaining: C-5 (the Test Verified line, the model-routing register's row, the teardown) |
````

### 6c — the owner ruling W-1: SEC-058 (line 12)

*Why a note:* the ruling's record stays as given; the note adds what happened.

Old text:

````text
SEC-058 is registered, fixed in code, and closes when C-2 runs its Docker pins green
````

New text:

````text
SEC-058 is registered, fixed in code, and closes when C-2 runs its Docker pins green *(C-4, 2026-11-05: closed; C-2 on K‴, run `42036154930`, ran them green)*
````

### 6d — the ledger's opening note: the tidy pass's first CI run (line 86)

*Why a note:* "its first CI run is C-2, on K" was the plan at C-1. Step 7a then made three more candidates, so C-2's run of record is on K‴.

Old text:

````text
The tidy pass merged after CI-W4b, so its first CI run is C-2, on K.
````

New text:

````text
The tidy pass merged after CI-W4b, so its first CI run is C-2, on K. *(C-4, 2026-11-05: Step 7a's fixes made three more candidates, K′, K″ and K‴, so C-2's run of record is on the final one, K‴ `a3ce6f7f`: run `42036154930`, green. See "C-2 — the checkpoint on K".)*
````

### 6e — the ledger: TIDY-1 (line 108)

Old text:

````text
Unit 1708, DemoSeed 170, Regression 135); first CI run at C-2 (run `<C-2 run id>`) |
````

New text:

````text
Unit 1708, DemoSeed 170, Regression 135); **CI-verified at C-2** (run `42036154930` on K‴ `a3ce6f7f`, all seven jobs green) |
````

### 6f — the ledger: TIDY-2 (line 109)

Old text:

````text
Unit 1710, DemoSeed 170, Regression 135); first CI run at C-2 (run `<C-2 run id>`) |
````

New text:

````text
Unit 1710, DemoSeed 170, Regression 135); **CI-verified at C-2** (run `42036154930` on K‴ `a3ce6f7f`, all seven jobs green) |
````

### 6g — the ledger: TIDY-3 (line 110)

Old text:

````text
Docker facts **CI-pending (C-2)**; the run id `<C-2 run id>` is filled in when C-2 on the final candidate is green |
````

New text:

````text
Docker facts **CI-verified at C-2** (run `42036154930` on K‴ `a3ce6f7f`, 2026-11-05) |
````

### 6h — the ledger: TIDY-4 (line 111)

The phrasing mirrors the frontend-only rows (TASK-14709, `:98`: "the frontend job green at CI-W2b (run …)").

Old text:

````text
vitest 84 files / 1089); first CI run at C-2 (run `<C-2 run id>`) |
````

New text:

````text
vitest 84 files / 1089); the frontend job green at C-2 (run `42036154930` on K‴ `a3ce6f7f`) |
````

### 6i — the ledger: TASK-14725 (line 112, the status cell's end)

The closed register row is named as in TASK-14704's row (`:94`, "SEC-053 CLOSED").

Old text:

````text
Docker facts **CI-pending (C-2)**. C-2 on K′ (run `42013444380`) failed one of them, the Skema month-view fact, for a fixture reason, not a product fault; -T fix-up 4 corrects it; the run id `<C-2 run id>` is filled in when C-2 on the final candidate is green |
````

New text:

````text
Docker facts **CI-verified at C-2** (run `42036154930` on K‴ `a3ce6f7f`, 2026-11-05; Regression 2436/2436; fix-ups 2–4 included). C-2 on K′ (run `42013444380`) had failed one of them, the Skema month-view fact, for a fixture reason, not a product fault; -T fix-up 4 corrected it; SEC-058 CLOSED |
````

### 6j — the tidy pass's dispositions: the Docker facts (line 247)

Old text:

````text
*(C-1: still CI-pending (C-2); the run id `<C-2 run id>` is filled in when C-2 on the final candidate is green.)*
````

New text:

````text
*(C-1: still CI-pending (C-2). C-4, 2026-11-05: CI-verified at C-2, run `42036154930` on K‴ `a3ce6f7f`.)*
````

### 6k — TASK-14725's dispositions: SEC-058 registered (line 252)

Old text:

````text
SEC-058 (Low), fixed in code; it closes when C-2 runs its Docker pins green.
````

New text:

````text
SEC-058 (Low), fixed in code; it closes when C-2 runs its Docker pins green. *(C-4, 2026-11-05: CLOSED; C-2 on K‴, run `42036154930`, ran them green.)*
````

### 6l — TASK-14725's dispositions: the Docker facts (line 261)

Old text:

````text
- **Docker facts CI-pending (C-2).**
````

New text:

````text
- **Docker facts CI-pending (C-2).** *(C-4, 2026-11-05: CI-verified at C-2, run `42036154930` on K‴ `a3ce6f7f`, fix-ups 2–4 included. C-2 on K′, run `42013444380`, had failed the Skema month-view fact for a fixture reason; -T fix-up 4 corrected it.)*
````

### 6m — C-1's table: the tidy pass's Docker facts (line 478, "Where it is now")

Old text:

````text
| CI-pending (C-2): the run id `<C-2 run id>` is filled in when C-2 on the final candidate is green |
````

New text:

````text
| CI-verified at C-2 (run `42036154930` on K‴ `a3ce6f7f`, 2026-11-05); CI-pending at C-1 |
````

## Block 7 — The sprint log, part 2: C-2, Step 7a, the handoff

**Target:** `docs/sprints/SPRINT-147.md` (LF). Fifteen parts, lines 530–640. Apply after Block 6.

**The K″ row (`:535`) is not touched:** its `<K″ run result>` is the Orchestrator's (see "Not drafted here").

### 7a — C-2's Commit row (line 530; Fable c4 NOTE-2)

*Why reshaped:* the row named the final candidate "K" beside a placeholder for its sha. Filled literally, it would have said K = `a3ce6f7f`, which is false (K is `c13024cd`). The row now names each candidate by its own sha, and the final one first.

Old text:

````text
| **Commit** | the final candidate: K = `<K sha>` (C-1, 2026-11-04), or the candidate Step 7a's fixes make (K′ `076ac29e`, K″ `841a7020`, then K‴, the final one). A run on an earlier candidate is evidence for that candidate only |
````

New text:

````text
| **Commit** | the final candidate, K‴ = `a3ce6f7f` (C-1e, 2026-11-05). The earlier candidates: K `c13024cd` (C-1, 2026-11-04), then K′ `076ac29e` and K″ `841a7020`, each made by Step 7a's fixes. A run on an earlier candidate is evidence for that candidate only *(Step 7a cycle 4, Fable NOTE-2: reshaped at C-4, so that each candidate is named by its own sha)* |
````

### 7b — C-2's Run row (line 531)

The PR line mirrors the K′ row (`:534`, "draft PR #49 … PR #49 closed unmerged, branch deleted").

Old text:

````text
| **Run** | `<C-2 run id>`, `<C-2 date>`, on branch `s147-ci-kppp` (K‴; Step 7a cycle 3, Codex NOTE 2: this row first named `s147-ci-k`, and the earlier runs used `s147-ci-kprime` for K′ and `s147-ci-kpp` for K″), through a draft PR whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2; never merge". The PR is closed unmerged and the branch deleted afterwards |
````

New text:

````text
| **Run** | `42036154930`, 2026-11-05, on `a3ce6f7f`, on branch `s147-ci-kppp` (K‴; Step 7a cycle 3, Codex NOTE 2: this row first named `s147-ci-k`, and the earlier runs used `s147-ci-kprime` for K′ and `s147-ci-kpp` for K″), through draft PR #51, whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2; never merge". PR #51 closed unmerged, branch deleted |
````

### 7c — C-2's Result row (line 532)

*In the form the earlier checkpoints use* (CI-W4b, `:637`: "all seven jobs green: Unit 1710, DemoSeed 170, Regression 2413/2413 (Docker included; …), frontend 83 files, E2E 7, smoke, docs"). The expected total of 2436 is Fable's (cycle 3, and fix-up 4 NOTE-5).

Old text:

````text
| **Result** | **CI-pending (C-2).** Recorded here only once C-2 on the final candidate is green, in the form the earlier checkpoints use: the seven jobs; Unit; DemoSeed; Regression `<C-2 Regression n>`/`<C-2 Regression n>` with the Docker facts; frontend files / tests; E2E; smoke; docs |
````

New text:

````text
| **Result** | **GREEN** (run `42036154930`, 2026-11-05): all seven jobs green (build-and-test, frontend-build, E2E, smoke, docs, gitleaks, lizard). Unit 1722; DemoSeed 170; Regression **2436/2436** with the Docker facts all executed: K′'s 2430 plus the six facts of fix-ups 2–3, the total Fable predicted at Step 7a cycle 3 (fix-up 4 added assertions, not facts); frontend 84 files / 1089 tests; E2E 7; Smoke 7. With every fact green, the corrected Skema month-view fact passed on both halves and no earlier D+6 fact was red. This is the evidence of record for K‴, and so for the close |
````

### 7d — the Step 7a header table: Codex (line 543)

Old text:

````text
| External: Codex, base `b510e45b` | Codex | `<7a Codex verdict>` | `.claude/reviews/SPRINT-147-step7a-codex.md`, `reviewed-against-commit: <K sha>` |
````

New text:

````text
| External: Codex, base `b510e45b` | Codex (`gpt-6-astra`) | FINDINGS: 0 BLOCKER, 0 WARNING, 1 NOTE (cycle 4, the final cycle) | `.claude/reviews/SPRINT-147-step7a-codex.md` (cycle 4's artifact), `reviewed-against-commit: a3ce6f7f` |
````

### 7e — the Step 7a header table: the internal reviewer (line 544)

All four internal cycles ran on `claude-fable-5-1`, the review floor: each artifact's `reviewed-by-model:` line says so, and no `SPRINT-147-review-floor-WAIVED.md` exists in `.claude/reviews/`.

Old text:

````text
| Internal: `reviewer` | `<7a reviewer model>`. The floor is `claude-fable-5-1`; an Opus stand-in also needs `.claude/reviews/SPRINT-147-review-floor-WAIVED.md`, naming the model and the owner's direction (`docs/WORKFLOW.md:42`) | `<7a reviewer verdict>` | `.claude/reviews/SPRINT-147-step7a-reviewer.md`, `reviewed-against-commit: <K sha>` |
````

New text:

````text
| Internal: `reviewer` | `claude-fable-5-1`, the review floor, on all four cycles, so no `SPRINT-147-review-floor-WAIVED.md` is needed (`docs/WORKFLOW.md:42`) | APPROVED-WITH-WARNINGS: 0 BLOCKER, 1 WARNING, 5 NOTE (cycle 4). The WARNING was the condition that C-2 on K‴ be green: met, run `42036154930` | `.claude/reviews/SPRINT-147-step7a-reviewer.md` (cycle 4's artifact), `reviewed-against-commit: a3ce6f7f` |
````

### 7f — the Step 7a cycle count (line 546)

Old text:

````text
**Cycles:** `<7a cycles>`. **Carried into the review**
````

New text:

````text
**Cycles:** 4 of the cap of 5, on both lenses; Step 7a passed at cycle 4 (see "Cycle 4"). **Carried into the review**
````

### 7g — cycle 1's W-1 row: SEC-058 (line 559)

Old text:

````text
SEC-058 registered (fixed in code; closes at C-2) |
````

New text:

````text
SEC-058 registered (fixed in code; closes at C-2) *(C-4, 2026-11-05: closed; C-2 on K‴, run `42036154930`, green, Regression 2436/2436)* |
````

### 7h — cycle 2's W-1 row: the condition met (line 585)

Old text:

````text
so this condition now resolves at C-2 on K‴)* |
````

New text:

````text
so this condition now resolves at C-2 on K‴)* *(C-4, 2026-11-05: resolved; C-2 on K‴, run `42036154930`, is green)* |
````

### 7i — cycle 2's N-5 row: done (line 590)

Old text:

````text
| At the close (C-4), with the other placeholders |
````

New text:

````text
| At the close (C-4), with the other placeholders *(done at C-4, 2026-11-05: the header table names cycle 4's artifacts and verdicts)* |
````

### 7j — cycle 3's W-1 row: the lines' commit (line 608; Fable c4 NOTE-1)

Old text:

````text
| Fable W-1 | The records said the K′ run was
````

New text:

````text
| Fable W-1 | The records (lines at `841a7020`) said the K′ run was
````

### 7k — Step 7a cycle 4's record (line 624)

Same shape as cycles 2 and 3: the lens table, the plain words, the findings with their dispositions. Every finding of both cycle-4 artifacts has a row. The record ends with the cycle count that replaces `<7a cycles>`. "Step 7a passed" rests on both final verdicts: Codex left a NOTE only, and Fable approved, with its one WARNING met by the green C-2. Fable also wrote "C-4 should fold NOTE-1..4; none needs a new candidate."

Old text:

````text
Cycle 4 runs on K‴, which carries TASK-14725-T fix-up 4 and these records; it is cycle 4 of the cap of 5. **Cycles:** `<7a cycles>`.
````

New text:

````text
Cycle 4 runs on K‴, which carries TASK-14725-T fix-up 4 and these records; it is cycle 4 of the cap of 5.

### Cycle 4 — on K‴ = `a3ce6f7f` (2026-11-05; final)

| Lens | Model | Verdict | Artifact |
|---|---|---|---|
| External: Codex, base `b510e45b` | Codex (`gpt-6-astra`) | FINDINGS: 0 BLOCKER, 0 WARNING, 1 NOTE | `.claude/reviews/SPRINT-147-step7a-codex-c4.md` |
| Internal: `reviewer` | `claude-fable-5-1` | APPROVED-WITH-WARNINGS: 0 BLOCKER, 1 WARNING, 5 NOTE | `.claude/reviews/SPRINT-147-step7a-reviewer-c4.md` |

*In plain words:* neither lens found a defect, in the product or in the tests. Both confirmed that cycle 3's fixes hold: the Skema test now sends the month first and checks why the manager is refused, so it can pass only for the right reason. Fable approved on one condition, carried since cycle 2: the Docker tests had to run green in CI on this exact commit. They did: C-2 on K‴ (run `42036154930`) passed 2436 of 2436. Four record fixes remained:
- the handoff still called finished work "in progress";
- one table quoted line numbers without saying which commit they belong to;
- one cell would have named the wrong commit once its placeholder was filled;
- one cell described the review files as they were before cycle 4.

All four are made in the close commit. It changes documents only, so it needs no further review cycle (`docs/WORKFLOW.md:38`).

| Finding | What | Disposition |
|---|---|---|
| Fable WARNING-1 (cycle 2's W-1, carried) | Approval is conditional on C-2 on K‴ being green, 2436/2436. Red would mean a new candidate and cycle 5, the last under the cap | Met: run `42036154930`, 2026-11-05, all seven jobs green, Regression 2436/2436 (see "C-2 — the checkpoint on K"). SEC-058 closes with it |
| Codex NOTE = Fable NOTE-4 | The handoff's Decide track (`:633`) still called `O-10-DOCS-OWED.md` "in progress" and ADR-043's acceptance "Next". That contradicts the O-items row (`:635`): O-10 applied, ADR-043 ACCEPTED | Fixed in the close commit: a dated note on the Decide track's "Next" (O-10 applied 2026-10-30 in `760bc767`; ADR-043 ACCEPTED, status date 2026-10-29) |
| Fable NOTE-1 | Cycle 3's W-1 row (`:608`) quotes `:582`, `:5`, `:604`, `:607` and `:530` without saying they are K″'s lines | Fixed in the close commit: "(lines at `841a7020`)" |
| Fable NOTE-2 | C-2's Commit row (`:530`) named the final candidate "K" beside a placeholder for its sha (C-1, 2026-11-04). A literal fill would have said K = `a3ce6f7f`, which is false (K is `c13024cd`) | Fixed in the close commit: the row names the final candidate, K‴ `a3ce6f7f`, and K `c13024cd`, K′ `076ac29e` and K″ `841a7020` each by its own sha |
| Fable NOTE-3 | Next action (3) (`:640`) said the final Codex artifact "is today a copy of cycle 3's" and that the reviewer file did not exist. Cycle 4 overtook both | Fixed in the close commit: Next action now gives the state after the close. The final artifacts are cycle 4's, named in the header table above |
| Fable NOTE-5 | Reject has no D+5 fact of its own: it shares approve's boundary fact, as ADR-040 records (ADR-040 `:614-615`) | No action (Fable: "a cheap S148 addition if wanted"). A candidate for S148's tests |
| Fable checks | Cycle 3's BLOCKER-1, W-1 and NOTE-1 to NOTE-3, and Codex's cycle-3 NOTEs, closed in code and records (traced on HEAD, on `c13024cd` and under M-25 and M-26). `a0447fe7` and `216f6721` are accurate. The records match the code. The ledger is complete (TASK-14700 … TASK-14725, each DONE or DROPPED). No "CI-verified" lacks a run id. The diff since K″ is tests and docs only | No action |
| Codex, the rest | Cycle 3's fixes closed by static inspection: the sent-month seed and the refusal-reason assertions, the year overview's mirror, the narrowed mutation claims. The C-1e records agree, SEC-058's residual included. Docker verification is honestly pending C-2 on K‴ | No action; C-2 on K‴ is green (above) |

**Step 7a passed at cycle 4** on both lenses. **Cycles:** 4.
````

### 7l — As of (line 630)

*The state after the push.* The close commit has no sha before it is made, so it is described, not named.

Old text:

````text
| **As of** | 2026-11-05, K‴ = `<K sha>`: K `c13024cd`; its Step 7a record fixes `bf3235b1`; K′ `076ac29e` (the TASK-14725 merge `2cfae13c` and its records); cycle 2's record fixes `c1934e78`; the fix-ups 2–3 merge `ab77c475`; K″ `841a7020` (their records); the TASK-14725-T fix-up 4 merge `a0c67f4f`; and these records (local master, not pushed; origin/master `7cc70be8` was merged in at `c29764ce`) |
````

New text:

````text
| **As of** | 2026-11-05, after C-4: the close commit, a docs-only child of K‴ = `a3ce6f7f`, pushed to origin/master. How K‴ was built: K `c13024cd`; its Step 7a record fixes `bf3235b1`; K′ `076ac29e` (the TASK-14725 merge `2cfae13c` and its records); cycle 2's record fixes `c1934e78`; the fix-ups 2–3 merge `ab77c475`; K″ `841a7020` (their records); the TASK-14725-T fix-up 4 merge `a0c67f4f`; K‴ `a3ce6f7f` (cycle 3's records, C-1e). origin/master `7cc70be8` was merged in at `c29764ce` |
````

### 7m — the Decide track: the stale "Next" (line 633; Codex c4 NOTE = Fable c4 NOTE-4)

*Why a dated note:* the brief asks for one. The sentence was true when written, so the note says what followed, with the dates. The dates were checked: ADR-043's status line reads "ACCEPTED 2026-10-29 (S147, O-10)", and `760bc767` ("S147 O-10: ADR-043 ACCEPTED …") is dated 2026-10-30, which matches the O-items row (`:635`, "O-10 done (applied 2026-10-30)").

Old text:

````text
then ADR-043 ACCEPTED (O-10) with the documents owed now.
````

New text:

````text
then ADR-043 ACCEPTED (O-10) with the documents owed now. *(Step 7a cycle 4, 2026-11-05, Codex NOTE = Fable NOTE-4: both are done, so this "Next" is history. O-10 was applied on 2026-10-30 (`760bc767`), and ADR-043 is ACCEPTED, status date 2026-10-29; see "O-items".)*
````

### 7n — Phase, its head (line 637)

The rest of the cell, from "**Step 7a cycle 2 done on K′ …" onward, stays as the phase history.

Old text:

````text
| **Phase** | **Step 7a cycle 3 done on K″ = `841a7020`** (2026-11-05; see "Step 7a (C-3)"). Its blocker was a test defect that C-2 on K′ had exposed (run `42013444380`, red on one Docker fact; the product was right). Its record fixes are these records; its test fix, TASK-14725-T fix-up 4, merged `a0c67f4f`. C-2 on K″ (run `42024818530`) is evidence for K″ only (see "C-2 — the checkpoint on K"). **Next: K‴ = `<K sha>` (these records), the final candidate; C-2 on K‴, Step 7a cycle 4, then the close.**
````

New text:

````text
| **Phase** | **CLOSED at C-4** (2026-11-05): the close commit, a docs-only child of K‴ = `a3ce6f7f`, is pushed; **C-5 remains** (see "Next action"). **C-2 on K‴ green:** run `42036154930`, 2026-11-05, all seven jobs: Unit 1722, DemoSeed 170, Regression 2436/2436 (Docker included), frontend 84 files / 1089, E2E 7, Smoke 7, docs; SEC-058 CLOSED with it. **Step 7a passed at cycle 4 on K‴** (2026-11-05; see "Step 7a (C-3)"): Codex FINDINGS (0 B / 0 W / 1 N), Fable APPROVED-WITH-WARNINGS (0 B / 1 W / 5 N; the WARNING was the green C-2, now met). Their record notes are folded into the close commit. **Step 7a cycle 3 done on K″ = `841a7020`** (2026-11-05). Its blocker was a test defect that C-2 on K′ had exposed (run `42013444380`, red on one Docker fact; the product was right). Its record fixes are in K‴ (C-1e); its test fix, TASK-14725-T fix-up 4, merged `a0c67f4f`. C-2 on K″ (run `42024818530`) is evidence for K″ only (see "C-2 — the checkpoint on K").
````

### 7o — Next action (line 640, the whole cell; Fable c4 NOTE-3)

C-5's items are as the brief lists them. Changed from C-1e's cell:
- only C-5 remains;
- the overtaken sentence on the review files is gone (NOTE-3);
- the teardown counts the 24 local `worktree-agent-*` branches, verified with `git branch` at `a3ce6f7f`: 24, and `git branch --no-merged master` lists none;
- the evidence branches are described as deleted with their PRs (the brief; see "Brief contradictions" 3);
- the sprint after this one is named, with its input file (`.claude/plans/s147/S148-BUILD-INPUT.md`, which exists).

Old text:

````text
| **Next action** | (1) **K‴**: these records (`.claude/plans/s147/C-1e-DOCS-OWED.md`) on top of the TASK-14725-T fix-up 4 merge (`a0c67f4f`), committed on master; not pushed. K‴ is the final candidate. (2) **C-2 on K‴** (`PLAN-s147.md:426`): push to `s147-ci-kppp`; a draft PR whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2; never merge"; one background watch; `headSha` equals K‴; all seven jobs green. Check: Unit 1722; Regression 2436 with the Docker facts all executed (K′'s 2430 plus the six facts of fix-ups 2–3; fix-up 4 adds assertions, not facts), the corrected Skema month-view fact included; no pre-existing D+6 fact red. Then fill `<C-2 run id>`, `<C-2 date>` and the counts in "C-2 — the checkpoint on K", and flip every "CI-pending (C-2)" to CI-verified with the run id: the tidy pass's facts, TASK-14725's facts (fix-ups 2–4 included), SEC-058's Docker pins and the index row. Close the PR unmerged and delete the branch. Red → a fix task → a new candidate → C-2 again. (3) **C-3, Step 7a cycle 4** on K‴ (cycle 4 of the cap of 5), both lenses: `codex review --base b510e45b`; `reviewed-against-commit: <K sha>` (K‴'s sha); the reviewer on Fable 5.1, or an Opus stand-in with `.claude/reviews/SPRINT-147-review-floor-WAIVED.md`; each artifact saved before acting; the verdicts into "Step 7a (C-3)". The final artifacts the Step 7a header table names come from cycle 4: `.claude/reviews/SPRINT-147-step7a-codex.md` is today a copy of cycle 3's, reviewed against `841a7020`, and `SPRINT-147-step7a-reviewer.md` does not exist yet. (4) **C-4, the close commit**, a docs-only child of K‴: every angle-bracket placeholder filled, the Step 7a header table's included (Fable c2 N-5); `git diff --stat` from K‴ lists only `docs/**`; the guard's gates (`docs/WORKFLOW.md:41-47`); the commit message written through a file, never inside the Bash command; push; watch CI to green. (5) **C-5**: the Test Verified line with the close run id; the model-routing register's S147 row (Opus stand-in cycles: 14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2; Fable from 14713 onward, TASK-14725, its fix-ups 2–4 and Step 7a included); the handoff "closed"; teardown: the 24 local `worktree-agent-*` branches, all merged into master at `a0c67f4f` (the agent worktrees are removed). The Step-0a DEBT branches stay the owner's call |
````

New text:

````text
| **Next action** | **C-5, the last step.** (1) Watch the push-triggered CI run on master for the close commit to green, and write its run id on the header's **Test Verified** line. (2) The model-routing register's S147 row (`docs/operations/model-routing-register.md`): the Opus stand-in cycles (14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2); Fable from 14713 onward, with TASK-14725, its fix-ups 2–4 and all four Step 7a cycles included. (3) Mark this handoff "closed". (4) Teardown: delete the 24 local `worktree-agent-*` branches, all merged into master (the agent worktrees are already removed, and the C-2 evidence branches were deleted with their PRs). The Step-0a DEBT branches (`origin/s144-red-mutations`, `origin/add-claude-github-actions-…`) stay the owner's call. Then S148 opens on ADR-043's staged build (`.claude/plans/s147/S148-BUILD-INPUT.md`) |
````

---

## Not drafted here

- **`<K″ run result>`** (`SPRINT-147.md:535`). It stays literal, the Orchestrator's to fill with run `42024818530`'s result in one sentence. Two words in the same row will need tense: "PR #50 is closed unmerged, and its branch deleted, when the run finishes". The INDEX row (5a) leaves out C-2 on K″ for the same reason; add it there if wanted once the result is known.
- **The C-5 records.** The Test Verified line, the model-routing register's S147 row, the handoff "closed" and the teardown are C-5's, as the brief says. Next action (7o) names them. The header's **Test Verified** therefore stays "—" in the close commit. The close guard's consecutive-CI-pending gate reads that line (`docs/WORKFLOW.md:44`), and "—" carries no "CI-pending".
- **Records that quote earlier wording, kept as history:**
  - cycle 1's W-2 row (`:560`: "CI-pending (C-2)" until the run lands);
  - cycle 2's Codex W2 row (`:583`: both read "fixed in code; closes at C-2"). It quotes the text a fix produced and is true as a quote.
- **The plain-words openings of C-1, C-2 and Step 7a** (`:463`, `:526`, `:539`). They describe the plan in terms of K, as it stood when each section was written. The tables beneath them now carry K‴.
- **The Model substitution cell** (`:639`, "Owed at close: the model-routing register row records the substitution"). Still true until C-5.
- **`QUALITY.md`'s Test Suite row** ("Seven CI checkpoint runs", S147 section, line 25). It is a C-1 snapshot, and the C-2 runs, the red one on K′ included, are in the sprint log. The Governance row of the same section already points there for Step 7a. A dated note could be added; not owed by the brief.
- **HRP-011 and performance F7.** Both name SEC-058 or TASK-14725, and neither carries a status that C-2 changes.
- **Fable NOTE-5 (reject's D+5 fact).** It is recorded in cycle 4's table only, as the brief says, with no ROADMAP or S148-input entry.

## Brief contradictions, deviations and open points

**Claims about the existing code and state (checked; the code governs):**
1. **The brief's line numbers match `a3ce6f7f`:** `:530` the Commit row, `:608` cycle 3's W-1 row, `:633` the Decide track, `:635` the O-items row, `:640` Next action, `INDEX.md:116` the S147 row. The Fable artifact's ADR-040 `:614-615` (reject shares approve's counterpart) also matches. ADR-043's status date and the O-10 commit were verified as written in 7m.
2. **The INDEX row's unit count (not in the brief).** The row said **1715u**, TIDY-3's gate count (`SPRINT-147.md:110`). The brief's C-2 figure is Unit 1722, the count since TASK-14725 (`:112`). 5a writes **1722u**, and the value is in "Values to verify".
3. **"The evidence branches already deleted with their PRs."** Not verifiable offline. This checkout still lists `origin/s147-ci-{k,kprime,kpp,kppp,w2,w2b,w3,w3b,w4,w4b}` as remote-tracking refs. That is consistent with branches deleted on GitHub and never pruned locally, since the log records the CI-W branches as deleted long ago. `git fetch --prune` would settle it. One more observation: `origin/s147-ci-k` points at `c13024cd` (K), pushed according to the reflog, while C-2's table records runs only on K′, K″ and K‴. If a run on K was started and abandoned, the log does not record it. No draft change; 7b and 7o state the brief's claim.
4. **Values not in the brief** (frontend 84 / 1089, E2E 7, PR #51 closed): see "Values to verify". They are taken from K′'s run log, and no frontend file changed between K′ and K‴.
5. **"Step 7a passed at cycle 4"** (6b, 7f, 7k, 7n) is this draft's reading, not a verdict either lens wrote. It rests on Codex's NOTE-only FINDINGS and on Fable's APPROVED-WITH-WARNINGS, whose one WARNING is met by the green run, with Fable's "none needs a new candidate". S146 used the same phrase (`SPRINT-146.md:5`, `:562`). If the Orchestrator prefers the verdicts alone, the phrase appears in those four parts only.

**Claims about required behaviour:** none contradicted. 2436 is Fable's expected total (cycle 3; fix-up 4 NOTE-5), and the brief's assumed figure agrees with it.

**Needs a ruling (the Orchestrator's):**
- **Is this draft committed with the close commit?** C-1e's draft was committed with K‴. If this one is too, `git diff --stat` from K‴ lists `.claude/plans/s147/C-4-DOCS-OWED.md` besides `docs/**`, and C-1e's own check (Next action (4): "lists only `docs/**`") would not hold to the letter. The commit would still be docs-only in the sense `docs/WORKFLOW.md:38` exempts ("no code changes").
  - Options: commit it with the close commit and read the check as "no code"; or commit it at C-5.
  - Lean: commit it with the close commit, as C-1e's was, so the records and their source land together.
- **4a (`QUALITY.md`'s dated note):** keep or drop. The brief asks for SEC-058 to read closed wherever it reads "closes at C-2", and the file's rule forbids rewriting a snapshot, so a note is the only form that does both. Lean: keep.

## Revision history

| Draft | Date | By | What |
|---|---|---|---|
| 1 | 2026-11-05 | `planner` (Opus 5.5) | First draft at `a3ce6f7f` (K‴). Blocks 1–7, 38 replacements:<br>• **ADR-040:** SEC-058 closed; the Skema fact's correction and the note's Docker facts CI-verified.<br>• **The register:** SEC-058 CLOSED in SEC-053's form; its pins CI-verified, with a dated note on the Skema pin; SEC-057's pointer.<br>• **`SECURITY.md`**, **`QUALITY.md`** (a dated note) and **the INDEX row** (the outcome, status, dates, counts, CI).<br>• **The sprint log:** Status, the owner ruling's note, the ledger (TIDY-1 … TIDY-4, TASK-14725), the dispositions' dated notes, C-1's table, C-2's Commit / Run / Result rows, the Step 7a header table and cycle count, dated notes on cycle 1's W-1, cycle 2's W-1 and N-5, cycle 3's W-1 line basis, Step 7a cycle 4's record, As of, the Decide track's note, Phase and Next action.<br>`<K″ run result>` is left literal |
