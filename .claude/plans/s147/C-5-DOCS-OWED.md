# C-5 — after the close commit: apply-ready records

## Values to verify before applying

The draft is **conditional on the close commit's own CI run being green**. That run is `42065541572`: `headSha` = `e553777e`, branch `master`, event `push`, started 2026-11-05T16:32:46Z. At drafting time (17:18Z) six of its seven jobs were green (lizard, frontend-build, gitleaks, docs, E2E, smoke) and `build-and-test` was still running. Check these against its log before applying. If any differs, edit the draft: each value's parts are listed, so it can be changed in one pass.

1. **The run's conclusion:** success, all seven jobs green (build-and-test, frontend-build, E2E, smoke, docs, gitleaks, lizard). Written in 1a, 1b, 1c, 1d, 1f and 2b (the register's CI cell and its Notes).
2. **Unit 1722.** Written in 1a.
3. **DemoSeed 170.** Written in 1a.
4. **Regression 2436/2436** (Failed 0, Skipped 0). Written in 1a and 2b.
5. **Smoke 7.** Written in 1a.
6. **Frontend 84 files / 1089 tests.** Written in 1a.
7. **E2E 7.** Written in 1a.
8. **Post-close defects: 0** at the moment of applying. Written in 2b.

*Why these values are expected:* the close commit changes documents only (`git diff --stat a3ce6f7f e553777e` lists only `docs/**`), so the run repeats C-2 on K‴ (run `42036154930`, the same seven jobs and the same counts, `SPRINT-147.md:532`).

| | |
|---|---|
| **What this is** | The exact text for the records of C-5, the last step after S147's close commit. It writes the header's **Test Verified** line, adds the **model-routing register's S147 row**, and marks the sprint log's **handoff closed** (Status, As of, Phase, Model substitution, Next action). Two extras (X1, X2) correct two records gaps found while gathering the facts; they are the Orchestrator's to apply or drop. Sources:<br>• the sprint log and the register at `e553777e`;<br>• the review artifacts in `.claude/reviews/` (`S147-*`, `SPRINT-147-*`, `REFINEMENT-s147-*`, `PLAN-s147-*`, `RESEARCH-s147-*`), their `reviewed-by-model:` / `adjudicated-by-model:` lines;<br>• the telemetry log `.claude/telemetry/model-routing.log`, lines 680–831;<br>• the session transcript `0175149f-….jsonl` and its 276 subagent transcripts (`subagents/agent-*.jsonl` with `*.meta.json`);<br>• GitHub, read-only: `gh run view`/`gh run list` for every CI run since 2026-10-26, `gh pr view 48`, and the repository's branch activity log;<br>• `git` at `e553777e`;<br>• the Orchestrator's brief.<br>The Orchestrator applies each block as a string replacement. The `planner` drafts; the Orchestrator applies. Every target is under `docs/**` |
| **Status** | DRAFT 1 (`planner`, Opus 5.5), 2026-11-05 |
| **Base commit** | `e553777e` (the close commit, origin/master and local master). Every Old text is copied from it. A script counted each Old text as an exact substring of its whole target file, in the `e553777e` blob and in the working copy (clean for `docs/**`): each count is 1. It then applied all replacements in order, extras included, on a copy, and counted each Old text again just before its own replacement: each count is 1. No two replacements in one file overlap. Line numbers are at `e553777e` |
| **Orchestrator model** | S147: Opus 5.5 seat; drafting `planner` (Opus 5.5); judgment `adjudicator` / `reviewer` on Fable 5.1 |
| **Timing** | Apply after run `42065541572` is green and "Values to verify" are checked. Then commit C-5 |
| **Owner approval** | None needed. No block touches `SYSTEM_TARGET.md` |

## What this is, in plain words

S147 is closed: the close commit `e553777e` is on origin/master. Three records were left for this step, C-5, because they describe things that happen after the close commit exists:
- **Test Verified.** The header line that names the CI run proving the final code. It stays "—" in the close commit, because that commit's own run starts only when it is pushed. The run is `42065541572`. The close commit changes only documents, so it is expected to repeat C-2 on K‴ exactly.
- **The model-routing register's row.** The register is the owner's thin monitor of the rule "Fable plans and reviews, Opus implements": one row per sprint on which models did the work and how the reviews and CI went, so a trend shows over three sprints. S147's row records, among other things, the two windows when Fable was at its usage limit and Opus stood in (refinement cycle 1 on 2026-10-27, and thirteen Step-5a reviews from 2026-10-30 to 11-01), and that the close itself passed on Fable.
- **The handoff closed.** The sprint log's current-state cells now say there is nothing left to do in S147 and that S148 opens next.

Two facts surfaced while gathering the row's data, and each gets an optional extra:
- **X1. A green CI run on K is not in the log.** C-2's first run, on the close candidate K (`c13024cd`), ran green on 2026-11-04 (`41878946063`, Regression 2417/2417), and the tidy pass's Docker tests ran there for the first time. Step 7a's fix then replaced K with K′, and the log's C-2 table only records the runs from K′ on.
- **X2. One of the two "lingering" remote branches was already gone.** The Step 0a scan listed `origin/s144-red-mutations` as debt, but GitHub's activity log shows it was deleted on 2026-10-01. The scan read a local copy that had never been pruned.

## How to apply

- Each block names its **Target**, the **Old text** (one exact match at `e553777e`) and the **New text**. Apply each part as its own replacement, in order.
- **Line endings:** both targets are LF, in the blob and in the working copy (a carriage-return count of 0): `docs/sprints/SPRINT-147.md` and `docs/operations/model-routing-register.md`. The one multi-line New text (2b) is LF.
- **Non-overlap:** several parts share a line, and none overlaps another:
  - 1b sits at the end of the Status cell (log line 5), after the cell's head, which C-4 wrote and C-5 leaves as it is;
  - 1c, 1d and 1e take only the head (1c, 1d) or the end (1e) of their cells; the rest of each cell stays as history.
- **The sprint log has two blocks:** Block 1 (the core) and Block 3 (extras X1 and X2). Apply Block 1 first. The order changes no count: no Old text appears in another part's New text.
- **Placeholders:** none left. The brief's `<close run result>` is written out as the expected values above (see "Brief contradictions" 12).

## Map: each owed item → its block

| # | Owed item (source) | Block | State at `e553777e` |
|---|---|---|---|
| 1 | Test Verified, in S146's form (brief item 1; `SPRINT-146.md:16`) | 1a | "—" (`SPRINT-147.md:16`) |
| 2 | The model-routing register's S147 row (brief item 2) | 2a (the anchor), 2b (the row) | anchor 146; last row S146 (`model-routing-register.md:81`) |
| 3 | The handoff closed (brief item 3) | Status 1b; As of 1c; Phase 1d; Model substitution 1e; Next action 1f | "Remaining: C-5" (`:5`); "after C-4" (`:658`); "**C-5 remains**" (`:665`); "Owed at close: the model-routing register row" (`:667`); "**C-5, the last step.**" (`:668`) |
| 4 | `docs/sprints/INDEX.md:116`, only if it has a slot S146's row filled at C-5 (brief item 4) | none | No such slot (see "Not drafted here") |
| X1 | The C-2 run on K, unrecorded (found while gathering item 2) | X1 | absent from C-2's table (`:528-535`) |
| X2 | `origin/s144-red-mutations` already deleted (found while checking the teardown) | X2 | listed as DEBT (`:13`) |

**Count: 8 replacements in the core** (Block 1: 6; Block 2: 2), **plus 2 extras** (Block 3), so 10 if both extras are applied.

---

## Block 1 — The sprint log: Test Verified and the handoff closed

**Target:** `docs/sprints/SPRINT-147.md` (LF). Six parts, lines 5–668.

*How S146 marked its handoff closed, mirrored here:* its Phase cell reads "**CLOSED.** Step 7a passed at cycle 4 on both lenses" (`SPRINT-146.md:562`), and its Next action opens the next sprint ("S147 opens. First payroll item: …", `:568`). S146's log has no edit after its close commit (`git log -- docs/sprints/SPRINT-146.md` ends at `b510e45b`), so there is no S146 C-5 wording to copy. Here the C-5 facts (the run, the register row, the teardown) are named in the cells themselves, each with its evidence.

### 1a — Test Verified (line 16)

*In S146's form* (`SPRINT-146.md:16`): the run, the commit, the seven jobs with their counts, why the run stands for the product code, and the last local gate. The local figures are the GATE at the last merge, `a0c67f4f` (`SPRINT-147.md:182`), and the last frontend gate, TIDY-4 (`:179`).

Old text:

````text
| **Test Verified** | — |
````

New text:

````text
| **Test Verified** | **CI GREEN `42065541572`** on the close commit `e553777e` (the push to master, 2026-11-05), all seven jobs: Unit 1722 · DemoSeed 170 · **Regression 2436** (the Docker-gated facts included, all executed) · Smoke 7 · frontend build (lint, e2e typecheck, build, vitest 84 files / 1089) · e2e 7 · docs · gitleaks · lizard. The close commit changes documents only, so this run repeats C-2 on K‴ (run `42036154930` on `a3ce6f7f`, the same figures). **Locally**, the GATE at the last merge (`a0c67f4f`): build 0 errors / 144 warnings, CA2100 114, Unit 1722, DemoSeed 170, Regression non-Docker 135; the last frontend gate (TIDY-4, `46f3f82a`): `tsc`, lint, `typecheck:e2e` and build 0, vitest 84 files / 1089 |
````

### 1b — the header Status, its end (line 5)

Old text:

````text
Remaining: C-5 (the Test Verified line, the model-routing register's row, the teardown) |
````

New text:

````text
C-5 done (2026-11-05): the close commit's push ran green (run `42065541572`, see Test Verified), the model-routing register has S147's row, and the teardown is done. The handoff is closed |
````

### 1c — As of, its head (line 658)

The rest of the cell, from "How K‴ was built:" onward, stays.

Old text:

````text
| **As of** | 2026-11-05, after C-4: the close commit, a docs-only child of K‴ = `a3ce6f7f`, pushed to origin/master.
````

New text:

````text
| **As of** | 2026-11-05, after C-5: the close commit `e553777e`, a docs-only child of K‴ = `a3ce6f7f`, pushed to origin/master, and its CI run `42065541572` green; C-5's records are a docs-only commit on top of it.
````

### 1d — Phase, its head (line 665)

The rest of the cell, from "**C-2 on K‴ green:**" onward, stays as the phase history.

Old text:

````text
| **Phase** | **CLOSED at C-4** (2026-11-05): the close commit, a docs-only child of K‴ = `a3ce6f7f`, is pushed; **C-5 remains** (see "Next action").
````

New text:

````text
| **Phase** | **CLOSED** (2026-11-05), and this handoff with it: C-4 and C-5 are done. The close commit `e553777e`, a docs-only child of K‴ = `a3ce6f7f`, is pushed, and its CI run `42065541572` is green on all seven jobs (see Test Verified). C-5 recorded that run, the model-routing register's S147 row and the teardown (see "Next action").
````

### 1e — Model substitution, its end (line 667)

*Why a dated note:* "Owed at close" was true when written; the note says it is done. The cell lists only the 2026-10-30 → 11-01 window; the register row also records refinement cycle 1's stand-in on 2026-10-27 (`SPRINT-147.md:43-44`), so the note names it.

Old text:

````text
**Owed at close:** the model-routing register row records the substitution |
````

New text:

````text
**Owed at close:** the model-routing register row records the substitution *(C-5, 2026-11-05: recorded in the register's S147 row, together with refinement cycle 1's stand-in on 2026-10-27, which is in the refinement record above)* |
````

### 1f — Next action (line 668, the whole cell)

Changed from C-4's cell:
- C-5's four items are recorded as done, each with its evidence. The run: `gh run view 42065541572`. The teardown, checked at drafting time: `git branch` lists only `master`; `git worktree list` lists only the main checkout; `git ls-remote --heads origin` lists no `s147-ci-*` branch.
- The DEBT branches: only one is left on origin (see "Brief contradictions" 1).
- The next sprint and its input: `.claude/plans/s147/S148-BUILD-INPUT.md` is at draft 2.6 (its `:6`), and its order starts with stage 0 (`:22`).

Old text:

````text
| **Next action** | **C-5, the last step.** (1) Watch the push-triggered CI run on master for the close commit to green, and write its run id on the header's **Test Verified** line. (2) The model-routing register's S147 row (`docs/operations/model-routing-register.md`): the Opus stand-in cycles (14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2); Fable from 14713 onward, with TASK-14725, its fix-ups 2–4 and all four Step 7a cycles included. (3) Mark this handoff "closed". (4) Teardown: delete the 24 local `worktree-agent-*` branches, all merged into master (the agent worktrees are already removed, and the C-2 evidence branches were deleted with their PRs). The Step-0a DEBT branches (`origin/s144-red-mutations`, `origin/add-claude-github-actions-…`) stay the owner's call. Then S148 opens on ADR-043's staged build (`.claude/plans/s147/S148-BUILD-INPUT.md`) |
````

New text:

````text
| **Next action** | **S148 opens** on ADR-043's staged build (`.claude/plans/s147/S148-BUILD-INPUT.md`, draft 2.6), stage 0 first. **Nothing in S147 is left to do** *(C-5 done, 2026-11-05: (1) the header's Test Verified line names run `42065541572` on the close commit `e553777e`, green; (2) the model-routing register has S147's row; (3) this handoff is closed; (4) teardown: the 24 local `worktree-agent-*` branches are deleted, only master and the main worktree remain, and `git fetch --prune` leaves no `s147-ci-*` branch on origin)*. **Left to the owner:** the Step-0a DEBT branch `origin/add-claude-github-actions-1772139476170` (2026-02-26). The other branch the Step 0a scan named, `origin/s144-red-mutations`, had already been deleted on GitHub on 2026-10-01 |
````

## Block 2 — The model-routing register: S147's row

**Target:** `docs/operations/model-routing-register.md` (LF). Two parts.

*What the row follows.* The register's rules (`:5-68`): one row per sprint, filled at close from artifacts the close guard already requires; resolved model ids from the subagent transcripts, never from the telemetry log; review counts from the `.claude/reviews/` artifacts and the log's Step-5a subsections; CI and post-close defects from the log's CI sections. The columns and the density are S146's (`:81`), with the same "What this row says about the routing" note in plain words, then the four signals of "When to reopen the routing" (`:58-68`), each answered.

*Where each number comes from* (all checked at drafting time):
- **The seat:** one session, `0175149f`, carried over from S146 (S146's row names it, `:81`). Its transcript shows client `2.1.301` on every day from 2026-10-27 to 11-05, and every assistant message in that range on `claude-opus-5-5`. No other session transcript was written after 2026-10-22.
- **Spawns:** the 276 subagent transcripts of that session, filtered to a first timestamp from 2026-10-27 to the close line (2026-11-05T16:32:03Z): **200**. Their roles come from each `*.meta.json`'s `agentType`, their resolved ids from the `model` of their assistant messages. This draft's own spawn (16:34:34Z) is after the close line and is not counted.
- **Telemetry:** lines 680–829 of `model-routing.log` (2026-10-27T09:55Z → 11-05T13:30Z), **150 spawn lines**, no `S99` lines in range. Line 830 is the close: `close | S147 | claude-fable-5-1 | PASS`. Line 715 is the one BLOCK.
- **Review artifacts:** 265 files. They are:
  - 81 internal-lens or adjudication artifacts: 66 stamped `claude-fable-5-1` and 15 stamped `claude-opus-5-5` (13 Step-5a stand-ins, `REFINEMENT-s147-reviewer-c1.md` and `REFINEMENT-s147-adjudication-c1.md`, the second by the seat);
  - 68 Codex verdicts;
  - 42 Constraint Validator (5α) checks;
  - 2 security consults;
  - 72 Codex logs and raw outputs.

  The 7a verdicts and counts were read from the eight cycle artifacts. The final copies are byte-identical to cycle 4's.
- **CI:** `gh run list --created ">=2026-10-26"`, `gh run view` for each run, and the K run's job log for its counts.
- **The session events:**
  - the seat's usage limit: the session transcript, 2026-11-05T12:36Z, "Usage limit reached; a short grace allowance remains", then `TaskStop`;
  - the account's session limits: the "You've hit your session limit" messages in the three `test-qa` transcripts of 2026-10-29 and in Step 7a cycle 1's reviewer transcript of 2026-11-04, each followed by later answers;
  - the two reaped watchers: the seat's messages to the owner on 2026-11-05.

### 2a — the freshness anchor (line 3)

Each row so far moved the anchor to its own sprint (S145's row at `6e3ba7ef`, S146's at `b510e45b`).

Old text:

````text
<!-- anchor-sprint: 146 -->
````

New text:

````text
<!-- anchor-sprint: 147 -->
````

### 2b — S147's row, after S146's (line 81)

The Old text is the end of S146's row, which is the last line of the file. The New text repeats it and adds S147's row on a new line. The file's final newline stays after the new row. The new row has exactly eight cell separators (seven cells), and no `|` inside a cell.

Old text:

````text
• The lost per-task 5a artifacts show the review layer's evidence lived partly in the conversation until R-58's save-before-acting rule. |
````

New text:

````text
• The lost per-task 5a artifacts show the review layer's evidence lived partly in the conversation until R-58's save-before-acting rule. |
| **S147 (ninth live run — the review floor ran out mid-sprint for the third sprint running, but came back before Step 7a, so the close passed on the floor model with no exception and no waiver: the first such close since S144)** | **Opus 5.5 seat throughout** (one session, `0175149f`, carried over from S146; client 2.1.301 on every day of the sprint), never switched (the 2026-09-29 ruling).<br>**Judgment:** Fable 5.1 `reviewer` and `adjudicator` spawns, except in two windows when Fable was at its usage limit. Then the owner's S146 direction applied ("Lets just use opus for now"): an Opus stand-in, labelled `reviewed-by-model: claude-opus-5-5` in every artifact.<br>• **2026-10-27:** refinement cycle 1. Its internal lens ran as a `general-purpose` spawn on Opus, and the seat itself ruled adjudication 1 (R-1..R-10). Fable was back for cycle 2 the same day.<br>• **2026-10-30 → 11-01:** thirteen Step-5a reviews: TASK-14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2.<br>Fable from 2026-11-03 (the TASK-14713 stack) to the end, including TASK-14725, its fix-ups 2–4 and all four Step 7a cycles.<br>**The close** passed the guard on the floor model: one `close` line for S147 on `claude-fable-5-1`, PASS (2026-11-05T16:32:03Z). No `SPRINT-147-review-floor-WAIVED.md` exists, and none was needed.<br>**Drafting:** `planner` (Opus 5.5): the refinement, the plan and its briefs, the decide-track briefs, ADR-043's working draft, the S148 build input, the tidy-pass plan, and the docs-owed drafts O-6, O-7, O-10, C-1 … C-1e, C-4 and C-5.<br>**Research:** `general-purpose` on Opus 5.5 (the R1–R5 first pass and its independent verifier) and on Sonnet 5.5 (one citation check) | **200 subagent transcripts** from 2026-10-27 to the close line, against **150 spawn lines** in the telemetry (lines 680–829). The log missed one spawn in four, mostly `reviewer` (30 lines, 58 transcripts) and `constraint-validator` (30 lines, 42 transcripts), and it has no line for resumes. So the counts below come from the transcripts.<br>**By role:** 58 `reviewer`, 42 `constraint-validator`, 20 `test-qa`, 17 `planner`, 17 `general-purpose` (16 `opus`, 1 `sonnet`), 12 `ux`, 11 `adjudicator`, 11 `backend-infrastructure`, 4 `payroll-integration`, 3 `Explore`, 2 `security`, 2 `rule-engine`, 1 `trace`.<br>**Resolved ids, from the transcripts' assistant messages:**<br>• **`claude-fable-5-1` ×67:** 56 `reviewer`, 11 `adjudicator`.<br>• **`claude-opus-5-5` ×53:** 17 `planner`, 16 `general-purpose`, 11 `backend-infrastructure`, 4 `payroll-integration`, 3 `Explore`, 2 `rule-engine`.<br>• **`claude-sonnet-5-5` ×78:** 42 `constraint-validator`, 20 `test-qa`, 12 `ux`, 2 `security`, 1 `trace`, 1 `general-purpose`.<br>Two `reviewer` spawns got only the "Fable limit" message and no answer (2026-10-27 and 11-01), and a third stopped at the limit mid-review (2026-10-30, TASK-14719); an Opus stand-in replaced each. Every tier ran the newest id of its family.<br>**Guard blocks: 1, not a routing fault.** At 2026-10-29T07:57:48Z a `general-purpose` spawn had no model. The guard's rule is that a generic agent's model must be chosen, not inherited. The spawn was re-issued two minutes later with `model: sonnet` and allowed. No block on a reviewer spawn, and none by the reviewer-brief detector.<br>**Reviewer refusals: 0.**<br>The eight spawn lines of 2026-10-26 after S146's close (the tender analysis) belong to neither row | **5a.** Every task's reviews were saved before acting: 52 internal (39 Fable, 13 Opus stand-in), 50 Codex, and 42 Constraint Validator checks.<br>• **Internal blockers on 5 tasks**, one each: 14705 (Fable c1: a test asserted "no row" in a table that logs every request), 14718 (Fable c1: a month change within the 1-second save delay wiped that day's work time), 14712 (Opus c1: a departed stand-in's case had no test), the 14710 stack (Opus c1: a test's SQL had lost a parameter), the 14713 stack (Fable c2: the screen could never show the refusal wording). One NOT READY without a blocker (14716, Fable c2).<br>• **Codex blockers on 3 tasks:** 14718 (two at c1, one at c2, one at c5), and the same blockers as the internal lens on the 14710 and 14713 stacks.<br>• All were fixed before merge, except 14718's cycle-5 Codex blocker: a save fired by Godkend could hang with no time limit. The owner ruled at the five-cycle cap, "Merge + small follow-up", and TASK-14720 fixed it.<br>**7a internal (Fable, all four cycles):** 0 B / 4 W (c1, on K) → 0 B / 1 W (c2, K′) → **1 B** / 1 W (c3, K″, CHANGES-REQUIRED: a test that could pass for the wrong reason) → 0 B / 1 W (c4, K‴; the W was the condition that C-2 be green, met).<br>**7a external (Codex):** 0 B / 2 W → 0 B / 2 W → 0 B / 0 W / 2 N → 0 B / 0 W / 1 N.<br>Cycle 1's W-1 was one real product defect, found by both lenses: a leaver's manager kept access past day 5 until a background job switched the leaver off, instead of losing it by the date. TASK-14725 fixed it in-sprint on the owner's ruling (SEC-058) | **13 runs, 3 red.**<br>• CI-W1 `40839583193` green.<br>• CI-W2 `41060488976` red 8/2353 (test-side faults, TASK-14719) → CI-W2b `41310143330` green.<br>• CI-W3 `41335270197`, CI-W3b `41641201066`, CI-W4 `41708962373`, CI-W4b `41825042200`: each green on its first run.<br>• **C-2**, the checkpoint on the close candidate:<br>– on K `41878946063`, green, Regression 2417/2417. It is not in the sprint log: Step 7a's fix (TASK-14725) replaced K with K′.<br>– on K′ `42013444380`, red 1/2430 (a test-fixture fault).<br>– on K″ `42024818530`, started before K′'s result was in: red 2435/2436, on the same fact, not yet corrected; fix-ups 2–3's six facts passed there first.<br>– on K‴ `42036154930`, green 2436/2436.<br>• The close push `42065541572` on `e553777e`: green, all seven jobs (Regression 2436/2436).<br>Not counted: `40783740597`, the owner's push from home on 2026-10-28 (`7cc70be8`), green.<br>**One "CI to green" sequence** is the runs one checkpoint needs, on the candidates it gates, until it is green. Read that way, C-2 needed three runs after Step 7a re-opened it (K′, K″, K‴), and every other checkpoint one or two | **0** so far (C-5, 2026-11-05) | **What this row says about the routing.**<br>• **The floor came back in time.** Fable ran out mid-sprint for the third sprint running, but it returned on 2026-11-03, before Step 7a. So every Step 7a cycle ran on Fable, and the close passed the guard with no exception and no waiver. The stand-in carried fifteen judgment artifacts (13 Step-5a reviews, refinement cycle 1's lens and its adjudication), each labelled honestly.<br>• **What the stand-in found.** The Opus lens found blockers on 2 of its 5 tasks (14712, and the 14710 stack, where Codex found the same one). On TASK-14718 it answered READY in cycles 2–5, while Codex found a blocker in cycles 2 and 5 and five warnings in each of cycles 3 and 4. One task does not show that Opus judgment is weaker; it is the data point a three-sprint read should look at.<br>• **The sprint's CI surprise was a test that could pass for the wrong reason, and the floor-model lenses missed it too.** TASK-14725-T's Skema fact (written by `test-qa`, Sonnet tier) checked only the refusal's status code, and an unrelated rule produced that code. It passed three Step-5a cycles on both lenses and Step 7a cycle 2; only CI found it (C-2 on K′). Like S144's post-close defect, it sat in Docker-gated tests that no lens can run. Step 7a cycle 3 recorded the lesson: check why a request is refused, not only that it is.<br>• **Session events, none of which lost work.** Account session limits cut three `test-qa` passes (2026-10-29) and Step 7a cycle 1's Fable review (2026-11-04); each resumed after the reset. On 2026-11-05 the Orchestrator seat reached its own usage limit mid-review. It stopped one Fable Step-5a review of TASK-14725-T fix-up 4 to stay inside the grace allowance, and re-ran it in full after the reset (`S147-TASK-14725-fu4-5a-reviewer.md`). Twice the machine's low-memory protection stopped a background CI watcher. Per the owner's standing rule neither was restarted, and the runs were read by hand.<br>• **The telemetry log undercounts.** It missed a quarter of the spawns, so this row counts from the transcripts.<br>**Signals 1–4:** (1) no post-close defect; (2) trips once on a literal reading: C-2 needed three runs after Step 7a re-opened it, and neither red was a product fault. S145 and S146 needed at most two runs per checkpoint, so it is not sustained; (3) no reviewer refusal, no block on a reviewer spawn, no block by the detector; (4) every tier on the newest id, client 2.1.301 throughout |
````

## Block 3 — Extras: two records gaps (the Orchestrator's to apply or drop)

**Target:** `docs/sprints/SPRINT-147.md` (LF). Two parts, lines 13 and 534. Apply after Block 1. Neither is in the brief; both correct a record with a fact checked at drafting time.

### X1 — C-2's table: the run on K (line 534)

*Why:* C-2's table records the runs on K′, K″ and K‴ (`:534-535`, `:531-532`), but not the first one, on K. That run was green, and it was the tidy pass's first CI run (the ledger's "its first CI run is C-2, on K", `:86`, was true). Step 7a cycle 1's internal review saw it "in progress" (`SPRINT-147-step7a-reviewer-c1.md:8`). The facts, from GitHub: run `41878946063`, created 2026-11-04T17:36:41Z, finished 19:32:09Z, on `c13024cd`, branch `s147-ci-k`, all seven jobs success. Its `build-and-test` log reads Unit "Passed: 1715", DemoSeed "Passed: 170", Regression "Passed: 2417, Skipped: 0, Total: 2417". PR #48 is a draft, closed unmerged 2026-11-05T09:59:42Z, its first body line "EVIDENCE ONLY — S147 checkpoint C-2 on the close candidate K (c13024cd); never merge". The branch was deleted at 09:59:44Z. The new row goes before K′'s, in run order.

Old text:

````text
| **Earlier run: K′ (red)** |
````

New text:

````text
| **Earlier run: K (green)** | Run `41878946063`, 2026-11-04, on `c13024cd` (branch `s147-ci-k`, draft PR #48, whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2 on the close candidate K (c13024cd); never merge"). All seven jobs green: Unit 1715; DemoSeed 170; Regression **2417/2417**, the tidy pass's Docker facts among them, on their first run. Step 7a cycle 1's fix, TASK-14725, then made K′, so this run is evidence for K only. PR #48 closed unmerged, branch deleted (2026-11-05). *(Recorded at C-5, 2026-11-05: the run was not recorded when it finished.)* |
| **Earlier run: K′ (red)** |
````

### X2 — Step 0a: `origin/s144-red-mutations` was already gone (line 13)

*Why:* the scan of 2026-10-27 listed two remote branches as DEBT. GitHub's branch activity log for `refs/heads/s144-red-mutations` shows its creation on 2026-09-30, its last push on 2026-10-01T07:38Z and its deletion on 2026-10-01T09:26:11Z. That is before S147 opened. Today `git ls-remote --heads origin` lists `add-claude-github-actions-1772139476170` (`8eea2727`) and no `s144-red-mutations`. The scan's record stays; the note says what it missed.

Old text:

````text
`origin/add-claude-github-actions-…` (2026-02-26) linger (**DEBT**).
````

New text:

````text
`origin/add-claude-github-actions-…` (2026-02-26) linger (**DEBT**). *(C-5, 2026-11-05: `origin/s144-red-mutations` had already been deleted on GitHub on 2026-10-01, according to the repository's branch activity log; the scan read a local copy of it that was never pruned. Only `origin/add-claude-github-actions-1772139476170` lingers.)*
````

---

## Not drafted here

- **`docs/sprints/INDEX.md:116`, S147's row. Left unchanged (brief item 4).** S146's row (`INDEX.md:115`) carries its CI result in the last column, written in the close commit with the run before the close (`40300521014` on `c532f004`). It has no slot for the close push's run. S146's own close run, `40324407582`, was recorded only in S147's Predecessor row (`SPRINT-147.md:7`). S147's row already carries C-2 on K‴'s run, written at C-4. Its "Earlier checkpoints" list leaves out C-2 on K (green) and on K″ (red). Both are in the register row, and K″ is in the log; add them to INDEX if wanted.
- **The header's "Orchestrator model" row** (`SPRINT-147.md:9`). It keeps its plan-time wording: Fable "while it is available", and the stand-in rule "if it is limited again". What happened is recorded in "Model substitution" (`:667`, with 1e's note) and the register row. A dated note could be added; not owed by the brief.
- **The register's method text** (`model-routing-register.md:23-35`). It says spawn counts come from the telemetry log. This sprint, the log missed 51 of 200 spawns. The pattern: no line for one `general-purpose` stand-in (2026-11-01T17:04Z), none for the C-4 `planner` (2026-11-05T13:40Z), none for the three `Explore` spawns, and 28 fewer `reviewer` lines than transcripts. S146's row already called the log "a floor". Changing the method to count from the transcripts is a register-rule change, so it is not drafted; see "Needs a ruling" 4.
- **The Step 7a cycle 1 reviewer's "resumed once after a usage-limit stop"** (`SPRINT-147-step7a-reviewer-c1.md:5`). It is in the artifact and in the register row's Notes, not in the log's cycle-1 record. No log edit is drafted.
- **This draft and `C-4-DOCS-OWED.md` are untracked.** Whether they go into the C-5 commit is the Orchestrator's call (see "Needs a ruling" 3).

## Brief contradictions, deviations and open points

**Claims about the existing code and state (checked; the code and state govern):**
1. **"The Step-0a DEBT branches (`origin/s144-red-mutations`, `origin/add-claude-github-actions-…`) stay the owner's call."** Only the second still exists. `git ls-remote --heads origin` (2026-11-05) lists `add-claude-github-actions-1772139476170` and no `s144-red-mutations`. GitHub's activity log for that ref shows it deleted on 2026-10-01T09:26:11Z, by the repository owner's account, four weeks before S147's Step 0a. The scan (`SPRINT-147.md:13`) read a stale local remote-tracking ref. 1f names only the remaining branch and says the other was already deleted; X2 offers the dated note on the scan.
2. **"C-2 on K `41878946063`"** (listed in the brief as a known run). The sprint log does not record it anywhere: C-2's table (`:528-535`) names runs on K′, K″ and K‴ only, and no `docs/**` file contains the id. GitHub shows it **completed, success**, all seven jobs; its log shows Regression 2417/2417. The register row lists it, and X1 offers the log row.
3. **"Judgment on Fable 5.1 except the Opus stand-in cycles: TASK-14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2."** There was one more stand-in window. On 2026-10-27, refinement cycle 1's internal lens ran on Opus: `REFINEMENT-s147-reviewer-c1.md:1` reads `reviewed-by-model: claude-opus-5-5`, after the Fable spawn answered only "You've reached your Fable limit". Adjudication 1 was ruled by the Opus seat: `REFINEMENT-s147-adjudication-c1.md:1` reads `adjudicated-by-model: claude-opus-5-5 (Orchestrator seat…`. Both are recorded at `SPRINT-147.md:43-44`. The row records both windows: 15 Opus judgment artifacts, not 13.
4. **"Sessions include `0175149f` … check for others."** There are none. `0175149f` is the only session transcript written after 2026-10-22 (S146's row names it as carried over). Client `2.1.301` on every day from 2026-10-27 to 11-05.
5. **"The spawn telemetry the S146 row used … spawn lines."** Found: `.claude/telemetry/model-routing.log`. For S147 it is not a reliable count: 150 spawn lines (680–829) against 200 subagent transcripts. The row counts roles from the transcripts and gives the log's count beside it. For comparison, S146's "52 spawn lines" reproduces as the lines from 2026-10-22 to S146's close (the eight 2026-10-26 lines after `b510e45b` excluded).
6. **"Drafting `planner` — C-1, C-1b, C-1c, C-1d, C-1e, C-4, O-6, O-7, TIDY-PASS and this C-5."** The planner also drafted the refinement, the plan and its briefs, the decide-track briefs, ADR-043's working draft, the S148 build input and O-10. The evidence: the 18 `planner` transcripts' descriptions (from "Draft S147 refinement", 2026-10-27, to "Draft C-5 wrap-up records"), and `O-10-DOCS-OWED.md`'s Status, "DRAFT 1 (`planner`, Opus 5.5), 2026-10-29". C-1b … C-1d have no transcripts of their own: the C-1 planner's transcript names all three files, so they were resumes of it. The row lists the full set.
7. **The session events.** All three are verified:
   - the seat's limit: the transcript holds "Usage limit reached; a short grace allowance remains" and a `TaskStop` at 2026-11-05T12:36:52Z;
   - the re-run: the artifact exists, `reviewed-against-commit: c3d98208`;
   - the reaped watchers: the seat told the owner twice that a watcher was stopped by low-memory protection.

   Two more, not in the brief, are in the row: the account's session limits on 2026-10-29 (three `test-qa` passes) and 2026-11-04 (Step 7a cycle 1's Fable review). Each resumed and finished.
8. **"Step 7a: cycle 1 — read its artifact; cycle 2 0 B / 1 W; cycle 3 1 B / 1 W; cycle 4 0 B / 1 W; Codex 2 W, 2 W, 0/0/2 N, 0/0/1 N."** All match the artifacts. Cycle 1 internal is APPROVED-WITH-WARNINGS, 0 B / 4 W / 8 N (`SPRINT-147-step7a-reviewer-c1.md:2`, its `**W` and `**N` items).
9. **"The close passed the guard on the Fable-stamped final internal artifact, so no waiver was needed."** Confirmed. The telemetry's line 830 reads `close | S147 | claude-fable-5-1 | PASS | step-7a reviewer artifact on the floor model`, and no `SPRINT-147-review-floor-WAIVED.md` exists. It is also the first floor-model close line since S144's (2026-09-29). S145's lines are "on the S145 owner exception", and S146 wrote none (waived).
10. **The CI run list.** Every run in the brief exists with the brief's outcome, and `gh run list` shows no other StatsTid CI run in the window except `40783740597`. That is a green push run on `7cc70be8`, the owner's home commit of 2026-10-28, and the row excludes it, by name. One timing fact the log does not give: C-2 on K″ started at 12:09:55Z, before C-2 on K′ finished (12:14:57Z), so its red was not foreseen when it started; the log's "red as expected" (`:535`) is the reading after K′'s cause was known. The row says "started before K′'s result was in".
11. **Brief item 4 (INDEX).** As checked: no slot, so it is left unchanged (see "Not drafted here").
12. **`<close run result>` (a deviation in form, not in content).** The brief asks both for the placeholder and for the expected values in the line. The draft writes the expected values, in 1a, 1b, 1c, 1d, 1f and 2b, and lists each under "Values to verify" with the parts it is written in, as C-4 did. So no placeholder is left literal. If the run differs, those parts change.

**Claims about required behaviour:** none contradicted.

**Needs a ruling (the Orchestrator's):**
1. **How signal 2 is read ("CI needing more than two runs to go green", `model-routing-register.md:61`).** The register never defined a sequence, and its rows differ. S145's "1" and S144's "2" count the close's runs. S146's "5" counts every run of the sprint.
   - (a) Per checkpoint: the runs one checkpoint needs until it is green. Trade-off: it measures what the signal names. S147 trips once (C-2: K′, K″, K‴), and S146 and S145 do not.
   - (b) All runs of the sprint. Trade-off: a sprint with seven checkpoints always trips, so the signal stops carrying information.
   - Lean: (a). The draft is written that way. Under (b), the Signals sentence and the CI cell's last sentence change.
2. **X1 and X2: apply or drop.**
   - X1 adds a missing, green C-2 run with its evidence.
   - X2 corrects the Step 0a scan's debt list.
   - Lean: apply both. Each is a fact checked at drafting time, and dropping them leaves the register row naming a run the sprint log never mentions.
3. **Commit `.claude/plans/s147/C-4-DOCS-OWED.md` (untracked) and this draft with C-5?**
   - The C-4 draft asked the same question for itself, and it was not committed with the close.
   - Lean: commit both with C-5, as C-1e's was committed with K‴, so the records and their source land together.
4. **The register's spawn-count method.** It still points at the telemetry log, which missed a quarter of S147's spawns.
   - Options: (a) leave it, and keep noting the undercount row by row; (b) amend `:23-35` so spawn counts come from the subagent transcripts, with the log kept for guard blocks and close lines.
   - Lean: (b), in a later docs commit, because it is a change to the register's rules rather than a C-5 record.

## Revision history

| Draft | Date | By | What |
|---|---|---|---|
| 1 | 2026-11-05 | `planner` (Opus 5.5) | First draft at `e553777e` (the close commit). Blocks 1–3, 8 core replacements and 2 extras:<br>• **The sprint log:** Test Verified (run `42065541572`, in S146's form); the Status, As of and Phase cells closed; the Model substitution note; Next action rewritten, with S148 next.<br>• **The register:** anchor 147; S147's row, with spawns counted from the subagent transcripts, every CI run of the sprint, and the four signals answered.<br>• **Extras:** X1, C-2's unrecorded green run on K; X2, the Step 0a note on `origin/s144-red-mutations`.<br>No placeholder is left literal; the run's values are listed under "Values to verify" |
