# C-1 — The close candidate's records (commit K): apply-ready text

| | |
|---|---|
| **What this is** | The exact text for C-1 (`.claude/plans/PLAN-s147.md:425`): "the records (O-2 … O-12) with the Step-7a outcome and CI ids as marked placeholders", plus every close-time item the sprint log promised. The items come from a sweep of `docs/sprints/SPRINT-147.md` for "at close", "C-1", "owed", "CI-pending" and "record at close". The Orchestrator applies each block as a string replacement and commits the result as **K** (message `S147 close candidate: …`). The `planner` drafts; the Orchestrator applies. Every target is under `docs/**` or is `ROADMAP.md` |
| **Status** | DRAFT 1 (`planner`, Opus 5.5), 2026-11-04 |
| **Base commit** | `9ca60740` (local master, the main checkout; the working copy of every target equals the commit). Every Old text below is copied from `9ca60740`. A script counted each one as an exact substring over the whole target file, in the commit's blob and in the working copy, and each count is 1. Every line number is at that commit |
| **Orchestrator model** | S147: Opus 5.5 seat; drafting `planner` (Opus 5.5); judgment `adjudicator` / `reviewer` on Fable 5.1 |
| **Timing** | Apply at C-1, before C-2. The angle-bracket placeholders stay literal in K, because C-2 and Step 7a run on K and their results cannot be inside it. The Orchestrator fills them in the close commit (C-4); see "How to apply" |
| **Owner approval** | None needed. No block touches `SYSTEM_TARGET.md` |

## What this is, in plain words

S147's build is finished. Every task is merged, and the five defects the tender analysis confirmed are fixed and closed in the registers: a leaver's final month that could not be paid, sick days that cost flex, særlige feriedage that could not be registered, one person signing a month twice, and two payroll routes that skipped approval. C-1 is the paperwork that had to wait for the build:
- **The task ledger** gets a final disposition for every task. Each "CI-pending" becomes "CI-verified", naming the CI run that proved it. The close guard refuses to close while any task id in the log lacks a disposition (`docs/WORKFLOW.md:47`).
- **Register rows promised "at close".** SEC-056 (a leader deciding their own overtime): the self part is closed, and the rest is designed for S148. QUAL-231 and QUAL-237 gain ADR-043's bound on a partly worked day of paid absence. QUAL-214 (a Skema save-queue defect) was largely fixed by TASK-14718. QUAL-236 records that the new refusal of unknown absence codes does not check the agreement's own list.
- **O-12 re-rates the tender analysis** where S147's build changed a rating. Two sub-rows rise from Partly to Met: the four-eyes rule (EM A1 (e)) and the vacation account (EM A9 (b)). The leaver rows (EM A33, VD 33) stay Partly, for gaps S147 did not touch. The fixed defects carry their evidence.
- **The tidy pass's ten "beyond tidy" items (class D)** go where the Orchestrator ruled: three new QUAL rows, one below-floor note, two ROADMAP backlog lines, a pointer for S148 stage 0, and a note on HRP-013.
- **`QUALITY.md`** gets an S147 section (O-10's X6). The file is a stack of dated snapshots, so S146's "ADR-043 PROPOSED" line is superseded by a new section, not edited.
- **The sprint log** gets three new sections: C-1's record, and C-2 and Step 7a with placeholders. Its handoff rows say "C-1 committed as K; C-2 next".

Four rules shaped every block:
- **Nothing is described as built unless it is merged.** ADR-043's model is "decided", and "built from S148".
- **CI-verified only with a run id**, or with the `<C-2 run id>` placeholder for the tidy pass, which merged after the last checkpoint.
- **Earlier records get dated notes; they are not rewritten.** The exceptions are the living tables, which hold state and are rewritten at every gate: the task ledger's Status column and the handoff block.
- **Plain words first, then the mechanism**, with a `file:line` opened at `9ca60740`.

## How to apply

- Each block names its **Target**, the **Old text** (one exact match at `9ca60740`) and the **New text**. Where a block has parts (a, b, …), apply each part as its own replacement.
- No two replacements in one file overlap. Several land in one line as disjoint substrings: 5b and 5c in the O-items row; 6a–6d in SEC-056's row; 13j and 13k in EM A1 (e)'s row; 13m and 13n in EM A9 (b)'s row.
- **Line endings: every target is LF**, in the blob and in the working copy (a carriage-return count of 0 for each): `docs/sprints/SPRINT-147.md`, `docs/operations/security-finding-register.md`, `docs/operations/quality-finding-register.md`, `docs/operations/hr-follow-up-process-register.md`, `docs/references/tender-requirements-analysis.md`, `docs/QUALITY.md`, `docs/sprints/INDEX.md`, `ROADMAP.md`. Multi-line New texts (2a, 2v, 2w, 4, 10a, 12a, 12b, 12c, 14b, X1, X6) are written with LF.
- **Placeholders.** They stay literal in K and are filled in the close commit (C-4). Replace each token where it stands: a token inside backticks keeps its backticks around the value (run ids and shas, as these docs write them); a bare token is replaced bare.

| Token | Filled with | Occurs in |
|---|---|---|
| `<K sha>` | K's short sha. If Step 7a forces a new candidate, that candidate's (K′) | 4, 5a, 5d, 5e, X1 |
| `<C-2 run id>` | the C-2 run's id | 2s, 2t, 2u, 2v, 3a, 4, X1 |
| `<C-2 date>` | the date C-2 went green | 4 |
| `<C-2 Regression n>` | C-2's Regression total, Docker facts included (CI-W4b had 2413) | 4, X1 |
| `<7a cycles>`, `<7a Codex verdict>`, `<7a reviewer verdict>`, `<7a reviewer model>` | Step 7a's final cycle number, the two verdicts, and the internal artifact's `reviewed-by-model` value | 4 |
| `<7a outcome>` | one sentence: the Step 7a result | X1 |
| `<close date>` | the close commit's date | X1 |

- **If C-2 is red, or Step 7a forces K′,** the ledger's tidy rows (2s–2v) and Block 3a say "CI-verified at C-2" too early. Set them back to "CI-pending (C-2)" in K′, and fill them when K′ is green.
- **Blocks 1–14 are owed.** X1–X8 are beyond the owed list, each with its lean. None needs the owner. Two blocks summarise the others (Block 4's record table and Block 5b): if a block is not applied, trim its line there.

## Map: each owed item → its block

| # | Owed item (source) | Block | State at `9ca60740` |
|---|---|---|---|
| 1 | A disposition for every TASK id (plan `:179`, `:425`; `docs/WORKFLOW.md:47`). The guard wants DONE, CUT, DEFERRED or DROPPED after the id, on the same line, inside the "Task ledger" section (`.claude/hooks/sprint-close-guard.ps1:654-656`, `:684`) | 2a–2v | rows say "merged". Four ids pass today, but only by accident for three of them: TASK-14717 (DROPPED); TASK-14716 through "O-7 done"; TASK-14718 and TASK-14720 through the word "dropped" in TASK-14720's item text ("dropped-save notices"). After 2a–2v, each of the 25 ids passes on its own row (simulated; see "Brief contradictions", point 10) |
| 2 | Each "Docker facts CI-pending (CI-W…)" becomes "CI-verified at CI-W… (run …)" | 2e–2i, 2k, 2l, 2q | eight rows CI-pending |
| 3 | The tidy pass's facts, first seen at C-2 | 2s–2v, 3a | "CI-pending (C-2)" (`SPRINT-147.md:101`, `:231`) |
| 4 | No TASK id the plan names lacks a row (plan `:183-218`) | 2v (one row for the four conditional tasks) | TASK-14701-F, -14709-N, -14710-F and -14712-F have none; never needed (`PLAN-s147.md:215-218`) |
| 5 | TASK-14719's "CI-pending until CI-W2b" (`SPRINT-147.md:332`) | 3b | open |
| 6 | O-10 Block 21: SEC-056, "T9 merged" (`SPRINT-147.md:219`, `:440`; O-11's row half) | 6a–6d | REGISTERED, "the S147 self part pending owner question Q-A" (`security-finding-register.md:216`) |
| 7 | O-10 Block 22: QUAL-231, QUAL-237 (`SPRINT-147.md:440`) | 7a, 7b | no R-7 sentence (`quality-finding-register.md:455`, `:461`) |
| 8 | O-10 X6: `QUALITY.md` (`SPRINT-147.md:440`) | 14a, 14b | S146's snapshot says "ADR-043 PROPOSED with nine forks" (`QUALITY.md:26`) |
| 9 | QUAL-214's cross-reference (`SPRINT-147.md:318`) | 8 | no S147 note (`quality-finding-register.md:438`) |
| 10 | O-8 (plan `:316`) | — ("Already done") | done; never recorded as done |
| 11 | O-11 (plan `:319`) | 6 (the row); the `SECURITY.md` half is "Already done" | half done |
| 12 | O-12, the tender analysis (plan `:320`; `SPRINT-147.md:195`) | 13a–13r | ratings of `67809348` |
| 13 | The tidy pass's class D items (`SPRINT-147.md:229`; `TIDY-PASS.md` § "Class D" and its O-g) | 4 (the table), 10a (QUAL-245–247), 10b (23d), 11 (21a), 12a–12c | not filed |
| 14 | X-6's row also carries the twelve stale directives and `AuditLogView.tsx` (`SPRINT-147.md:230`) | 10a (QUAL-247) | not filed |
| 15 | QUAL-236: the new 422 checks the vocabulary, not the agreement's list (`SPRINT-147.md:205`, `:362`; `TIDY-PASS.md` O-a) | 9 | not written |
| 16 | Phase, Handoff block and Next action: "C-1 committed as K; C-2 next" (the brief) | 5a–5e | "Next: C-1" |
| 17 | The Step-7a and C-2 placeholders (plan `:425`) | 4 | no sections |
| 18 | *(sweep)* The header Status still reads "OPEN 2026-10-27 — refinement (Steps 1–4)" (`SPRINT-147.md:5`) | 1 | stale since the refinement |
| 19 | *(sweep)* Tidy item 13 asks the test record to name the fact behind AC-234i-1's "payroll lines identical" (`SPRINT-147.md:379`; `TIDY-PASS.md` O-d) | 4 (inside the C-1 section) | not recorded |

**Count: 66 owed replacements** (1; 2a–2w, 23; 3a, 3b; 4; 5a–5e; 6a–6d; 7a, 7b; 8; 9; 10a, 10b; 11; 12a–12c; 13a–13r, 18; 14a, 14b) **and 19 optional ones** (X1; X2a–X2e; X3a–X3c; X4a–X4f; X5; X6; X7; X8). **85 in all.**

**New ids: QUAL-245, QUAL-246 and QUAL-247. No new SEC or HRP id.** How checked: the QUAL register's last row is QUAL-244, on the file's last line (`quality-finding-register.md:468`). `git grep -n "QUAL-24[5-9]\|QUAL-25[0-9]" 9ca60740` over the whole tree finds nothing, and neither does a grep over `.claude/plans`, `.claude/refinements` and `.claude/reviews`. Class D item 23d falls below the register's floor and gets no id (Block 10b).

**The Medium+ floor** ("would plausibly change behavior, block or mislead a future change, or misinform a reader", `quality-finding-register.md:6-8`):
- **QUAL-245 (11b) clears it, narrowly (M).** It is duplication, but of a security refusal whose copies have already drifted apart in their logging, so a change to it would plausibly miss a copy.
- **QUAL-246 (12b) clears it (M).** The 409's kind misinforms the caller: "export conflict" for "a second person must approve".
- **QUAL-247 (X-6) clears it (M).** The twelve directives misinform a reader (they say a hooks rule runs; none does), and the four files outside the typed-call lint can take hand-written API types unchecked, which would mislead a future change. QUAL-178, the same territory, is M.
- **23d does not clear it (L).** Only a data fault reaches the path: `approval_periods.employee_id` has no foreign key (`docker/postgres/init.sql:902`), but no product path deletes a users row. It goes to the inventory appendix, as the register's rule says (`quality-finding-register.md:8`).

## Already done (no block; checked at `9ca60740`)

| Owed item | Where it is done | Evidence |
|---|---|---|
| O-8, QUAL-234's half: "the interim is in place, the real fix pending D-2" (plan `:316`) | QUAL-234's row: "S147 interim (T7, owner OQ-4 = (b)), merged", its two residuals, and ADR-043 D10's route | `quality-finding-register.md:458`; written in `3c00fc88` (the 14706/14707 merge record) |
| O-8, QUAL-232's half: "the interim mechanism recorded for S148 stage 0" | QUAL-232's row: "S148 stage 0 refuses on-call, call-ins and travel (the legacy route answers 422 for any type other than `NORMAL`; payroll refuses any stored entry of another type by name)" | `quality-finding-register.md:456`; written at O-10 (`760bc767`) |
| O-11's `SECURITY.md` half: "names the overtime guard beside SEC-009 and SEC-053" | `docs/SECURITY.md:191`, rule 3 of § "Segregation of duties". The plan named § "Flat approval authority"; O-5 had introduced the new section instead (Fable's 14705 W2: "introduce the rule, not append") | `014eb187` |
| `FRONTEND.md:113`, `:291` "at O-4/O-7" (`SPRINT-147.md:174`; tidy item 14's third bullet; `TIDY-PASS.md` O-e) | O-7 Blocks 11a, 11b, and O-7's X6 (ProjectPicker) | `96391fe1` |
| The midnight-crossing retry, "owed at O-6" (`SPRINT-147.md:177`) | ADR-040's amendment of 2026-11-03 | `ADR-040-employee-timeline-and-as-of-resolution.md:482-484` |
| ADR-034 D3's sentence on a null-balance record from case 3a, "at O-7" (`SPRINT-147.md:262`) | ADR-034's S147 amendment, D3 item 3, and the pointer on the S146 D3 item | `96391fe1` |
| QUAL-241 closes at CI-W2b; QUAL-242 gains the cycle-5 residuals (`SPRINT-147.md:316-317`) | QUAL-241 CLOSED (run `41310143330`); QUAL-242's cycle-5 residual | `quality-finding-register.md:465`, `:466` |
| The O-7 bundle and R-G7 (`SPRINT-147.md:245-246`) | QUAL-243, QUAL-244 | `quality-finding-register.md:467-468` |
| Tidy item 25's comments (`SPRINT-147.md:247`, `:423-430`) | TIDY-1 (items 25a-c, 25e-f) and TIDY-2 (25d), per the ledger | `c1e1edfd`, `bf63e746` |

---

## Block 1 — The sprint log's header: Status

**Target:** `docs/sprints/SPRINT-147.md` (line 5, the header's Status row).

*Why:* the row still says the sprint is in refinement. It is the first thing a reader sees.

Old text:

````text
| **Status** | **OPEN 2026-10-27** — refinement (Steps 1–4) |
````

New text:

````text
| **Status** | **OPEN 2026-10-27; close candidate K committed at C-1 (2026-11-04).** Everything planned is merged: the defect fixes with their paired test passes, the three tasks added during the sprint, and the tidy pass; one planned pair was dropped as unneeded (see "Task ledger"). Closed: QUAL-229, QUAL-230, QUAL-231, QUAL-235, QUAL-241, SEC-053, SEC-054, and SEC-056's self part. Decided: ADR-043 ACCEPTED (2026-10-29), built from S148. Next: C-2 on K, then Step 7a (C-3) |
````

## Block 2 — The task ledger: final dispositions and CI evidence

**Target:** `docs/sprints/SPRINT-147.md` (lines 77–104). 2a adds a paragraph under the heading; 2b–2v rewrite each row's Status cell (2v also adds one row); 2w adds a line after the table. The TASK-14717 row (line 95) already reads **DROPPED** and is not touched.

*Why this shape:* the close guard matches a disposition word after the task id on the same line, inside the "Task ledger" section, which runs from the heading to the next `#`/`##` heading (`.claude/hooks/sprint-close-guard.ps1:654-656`, `:684`). "merged" does not match; "DONE (merged `sha`)" does, and is the form S146's ledger used (`SPRINT-146.md`, "Task ledger"). The paragraph in 2a and the line in 2w name no task id, so neither can stand in for a row.

**The CI runs, as the log records them:** CI-W1 `40839583193` on `2e445df8` (`SPRINT-147.md:81`, `:110`, `:437`); CI-W2 `41060488976`, red (`:439`); CI-W2b `41310143330` on `0984570f` (`:439`); CI-W3 `41335270197` on `c460dcb7` (`:442`); CI-W3b `41641201066` on `808d4297` (`:92`, `:442`); CI-W4 `41708962373` on `eaf90ed4` (`:93`, `:266`, `:442`); CI-W4b `41825042200` on `94bd32be` (`:94`, `:248`, `:440`, `:442`). Each merge cited below was checked with `git merge-base --is-ancestor` to be inside the commit of the run named for it. The four tidy merges are not inside `94bd32be`, so their first CI run is C-2.

### 2a — the paragraph under the heading (line 77)

Old text:

````text
## Task ledger
````

New text:

````text
## Task ledger

*Final dispositions, set at C-1 (2026-11-04).* Every task the plan named, the three it added and the tidy pass's four are **DONE**: merged on master, each with the CI run that first ran its Docker facts or frontend job green. One planned pair and the plan's four conditional tasks were **DROPPED**, never needed. No task was cut or deferred. The tidy pass merged after CI-W4b, so its first CI run is C-2, on K.
````

### 2b — TASK-14700 / 14700-T (line 81)

Old text:

````text
**merged** `f78ddba8`; GATE green; Docker facts **CI-verified** (CI-W1, run `40839583193` on `2e445df8`, all seven jobs green) |
````

New text:

````text
**DONE** (merged `f78ddba8`); GATE green; Docker facts **CI-verified** (CI-W1, run `40839583193` on `2e445df8`, all seven jobs green) |
````

### 2c — TASK-14701 / 14701-T (line 82)

Old text:

````text
**merged** `6bf4e6e4`; GATE green after the CA2100 fix `2e445df8`; Docker facts **CI-verified** (CI-W1, run `40839583193`) |
````

New text:

````text
**DONE** (merged `6bf4e6e4`); GATE green after the CA2100 fix `2e445df8`; Docker facts **CI-verified** (CI-W1, run `40839583193`) |
````

### 2d — TASK-14702 (line 83)

Old text:

````text
**merged** `61d6a98a` (with fix-ups 1 and 2); frontend GATE green |
````

New text:

````text
**DONE** (merged `61d6a98a`, with fix-ups 1 and 2); frontend GATE green; the frontend job green at CI-W1 (run `40839583193`) |
````

### 2e — TASK-14703 / 14703-T (line 84)

Old text:

````text
**merged** `8d2b5716` (GATE green; REGEN no drift); Docker facts CI-pending (CI-W2) |
````

New text:

````text
**DONE** (merged `8d2b5716`; GATE green; REGEN no drift); Docker facts **CI-verified at CI-W2b** (run `41310143330` on `0984570f`), after TASK-14719's test-side fixes (CI-W2, run `41060488976`, was red; no failure was a product fault) |
````

### 2f — TASK-14704 / 14704-T (line 85)

Old text:

````text
**merged** `002787c6` (GATE green; REGEN no drift); Docker facts CI-pending (CI-W2) |
````

New text:

````text
**DONE** (merged `002787c6`; GATE green; REGEN no drift); Docker facts **CI-verified at CI-W2b** (run `41310143330` on `0984570f`), after TASK-14719 fixed their seed (CI-W2, run `41060488976`, failed them on untyped nulls); SEC-053 CLOSED |
````

### 2g — TASK-14705 / 14705-T (line 86)

Old text:

````text
**merged** `b2a65fb8` with fix-up 1 (GATE green; REGEN no drift); Docker facts CI-pending (CI-W2) |
````

New text:

````text
**DONE** (merged `b2a65fb8` with fix-up 1; GATE green; REGEN no drift); Docker facts **CI-verified at CI-W2b** (run `41310143330` on `0984570f`) |
````

### 2h — TASK-14706 / 14706-T, TASK-14707 (line 87)

Old text:

````text
**merged**: 14706 `48a1ba25` (GATE green), REGEN `93698d1f`, 14707 `615f3ee5` (with fix-up 1; frontend GATE green); Docker facts CI-pending (CI-W2) |
````

New text:

````text
**DONE** (merged: 14706 `48a1ba25`, GATE green; REGEN `93698d1f`; 14707 `615f3ee5` with fix-up 1, frontend GATE green); Docker facts and the frontend job **CI-verified at CI-W2b** (run `41310143330` on `0984570f`) |
````

### 2i — TASK-14708 / 14708-T (line 88)

Old text:

````text
**merged** `7cc6e65e` + merge fix-up `4653e5ed` (GATE green; REGEN no drift; marker `weekly-timeline-v2`); Docker facts CI-pending (CI-W2) |
````

New text:

````text
**DONE** (merged `7cc6e65e` + merge fix-up `4653e5ed`; GATE green; REGEN no drift; marker `weekly-timeline-v2`); Docker facts **CI-verified at CI-W2b** (run `41310143330` on `0984570f`); QUAL-231 CLOSED |
````

### 2j — TASK-14709 (line 89)

Old text:

````text
**merged** `9de1ae79` (with fix-up 1); frontend GATE green (vitest 81 files / 1013 tests) |
````

New text:

````text
**DONE** (merged `9de1ae79`, with fix-up 1); frontend GATE green (vitest 81 files / 1013 tests); the frontend job green at CI-W2b (run `41310143330`) |
````

### 2k — TASK-14710 / 14710-T, TASK-14711 (line 90)

Old text:

````text
**merged as one stack** `9c7866f0` (GATE green: 144 / 114, Unit 1706, DemoSeed 170, Regression 135; frontend 1060; REGEN `065e39cc` on the branch, no drift after merge); Docker facts CI-pending (CI-W3) |
````

New text:

````text
**DONE** (merged as one stack `9c7866f0`; GATE green: 144 / 114, Unit 1706, DemoSeed 170, Regression 135; frontend 1060; REGEN `065e39cc` on the branch, no drift after merge); Docker facts and the frontend job **CI-verified at CI-W3** (run `41335270197` on `c460dcb7`) |
````

### 2l — TASK-14712 / 14712-T (line 91)

Old text:

````text
**merged** `52ab15c8` (GATE green; REGEN no drift); 16 Docker facts CI-pending (CI-W3) |
````

New text:

````text
**DONE** (merged `52ab15c8`; GATE green; REGEN no drift); 16 Docker facts **CI-verified at CI-W3** (run `41335270197` on `c460dcb7`) |
````

### 2m — TASK-14713 / 14713-T, TASK-14714 (line 92)

Old text:

````text
**merged as one stack** `652f26aa` (GATE green: 144 / 114, Unit 1706, DemoSeed 170, Regression 135; frontend 83 / 1074; REGEN `f65bf849` on the branch, no drift after merge); 32 Docker facts **CI-verified at CI-W3b** (run `41641201066`; Regression 2414/2414) |
````

New text:

````text
**DONE** (merged as one stack `652f26aa`; GATE green: 144 / 114, Unit 1706, DemoSeed 170, Regression 135; frontend 83 / 1074; REGEN `f65bf849` on the branch, no drift after merge); 32 Docker facts **CI-verified at CI-W3b** (run `41641201066`; Regression 2414/2414); QUAL-230 CLOSED |
````

### 2n — TASK-14715 / 14715-T (line 93)

Old text:

````text
**merged** `9cbf164b` (GATE green: 144 / 114, Unit 1706, DemoSeed 170, Regression 135; fix-up 1 each after Fable c1's WARNING); Docker facts **CI-verified at CI-W4** (run `41708962373`; Regression 2421/2421) |
````

New text:

````text
**DONE** (merged `9cbf164b`; GATE green: 144 / 114, Unit 1706, DemoSeed 170, Regression 135; fix-up 1 each after Fable c1's WARNING); Docker facts **CI-verified at CI-W4** (run `41708962373`; Regression 2421/2421) |
````

### 2o — TASK-14716 / 14716-T (line 94)

Old text:

````text
**merged** `169e9980` (GATE green: 144 / 114, Unit 1710, DemoSeed 170, Regression 135; 14716 fix-ups 1–3 incl. owner R-G9, -T fix-ups 1–2; Fable c3 READY, Codex c3 1 N folded); Docker facts **CI-verified at CI-W4b** (run `41825042200`; Regression 2413/2413); O-7 done |
````

New text:

````text
**DONE** (merged `169e9980`; GATE green: 144 / 114, Unit 1710, DemoSeed 170, Regression 135; 14716 fix-ups 1–3 incl. owner R-G9, -T fix-ups 1–2; Fable c3 READY, Codex c3 1 N folded); Docker facts **CI-verified at CI-W4b** (run `41825042200`; Regression 2413/2413); O-7 done; SEC-054 CLOSED |
````

### 2p — TASK-14718 (line 96)

Old text:

````text
**merged** `782e2285` with fix-ups 1–4 (frontend gate green, vitest 82 files / 1049); owner ruling at the five-cycle cap (2026-11-01): "Merge + small follow-up" → TASK-14720 |
````

New text:

````text
**DONE** (merged `782e2285` with fix-ups 1–4; frontend gate green, vitest 82 files / 1049); the frontend job green at CI-W2b (run `41310143330`); QUAL-241 CLOSED; owner ruling at the five-cycle cap (2026-11-01): "Merge + small follow-up" → TASK-14720 |
````

### 2q — TASK-14719 (line 97)

Old text:

````text
**merged** `f21422d6` with fix-up 1 (GATE green); Docker facts CI-pending (CI-W2b) |
````

New text:

````text
**DONE** (merged `f21422d6` with fix-up 1; GATE green); Docker facts **CI-verified at CI-W2b** (run `41310143330` on `0984570f`; Regression 2353/2353) |
````

### 2r — TASK-14720 (line 98)

Old text:

````text
**merged** `3ee2a8c9` with fix-ups 1–2 (frontend gate green, vitest 82 files / 1060) |
````

New text:

````text
**DONE** (merged `3ee2a8c9` with fix-ups 1–2; frontend gate green, vitest 82 files / 1060); the frontend job green at CI-W3 (run `41335270197`, frontend 82 files / 1060) |
````

### 2s — TASK-14721 / 14721-T, TIDY-1 (line 99)

Old text:

````text
**merged** `c1e1edfd` (GATE green: 144 / 114, Unit 1708, DemoSeed 170, Regression 135) |
````

New text:

````text
**DONE** (merged `c1e1edfd`; GATE green: 144 / 114, Unit 1708, DemoSeed 170, Regression 135); first CI run at C-2 (run `<C-2 run id>`) |
````

### 2t — TASK-14722, TIDY-2 (line 100)

Old text:

````text
**merged** `bf63e746` (GATE green: 144 / 114, Unit 1710, DemoSeed 170, Regression 135) |
````

New text:

````text
**DONE** (merged `bf63e746`; GATE green: 144 / 114, Unit 1710, DemoSeed 170, Regression 135); first CI run at C-2 (run `<C-2 run id>`) |
````

### 2u — TASK-14723 / 14723-T, TIDY-3 (line 101)

Old text:

````text
**merged** `80dd34b2` (GATE green: 144 / 114, Unit 1715, DemoSeed 170, Regression 135); Docker facts CI-pending (C-2) |
````

New text:

````text
**DONE** (merged `80dd34b2`; GATE green: 144 / 114, Unit 1715, DemoSeed 170, Regression 135); Docker facts **CI-verified at C-2** (run `<C-2 run id>`) |
````

### 2v — TASK-14724, TIDY-4 (line 102), and one row for the plan's conditional tasks

*Why the new row:* the plan names four conditional tasks and says each gets a disposition "once it exists" (`PLAN-s147.md:215-218`). None was ever needed, so none exists, and the log does not name them. One DROPPED row still accounts for every id the plan names, as the brief asks.

Old text:

````text
**merged** `46f3f82a` (frontend gate green: tsc / lint / typecheck:e2e / build 0, vitest 84 files / 1089) |
````

New text:

````text
**DONE** (merged `46f3f82a`; frontend gate green: tsc / lint / typecheck:e2e / build 0, vitest 84 files / 1089); first CI run at C-2 (run `<C-2 run id>`) |
| TASK-14701-F, TASK-14709-N, TASK-14710-F, TASK-14712-F | — | — | The plan's conditional tasks (`PLAN-s147.md:215-218`) | **DROPPED**, never needed: research R2 confirmed ADR-032 D3 (RR-6), so no 14701-F; RR-4 removed 14709-N's premise; owner Q-B = (c) needed neither stand-in variant (14710-F, 14712-F) |
````

### 2w — the Orchestrator's own items, after the table (before line 104)

Old text:

````text
### Wave 1 — the merge record (2026-10-29)
````

New text:

````text
*Orchestrator items, not tasks, so they need no ledger row: GATE after every merge; REGEN; R, the research pass; D-1 … D-12, the decide track (complete 2026-10-29); O-1 … O-12, the docs items (all done by C-1); the CI checkpoints CI-W1, CI-W2 (red) and CI-W2b, CI-W3, CI-W3b, CI-W4, CI-W4b; C-1 … C-5.*

### Wave 1 — the merge record (2026-10-29)
````

## Block 3 — The two CI-pending dispositions in the merge records

**Target:** `docs/sprints/SPRINT-147.md` (lines 231 and 332). Both are dispositions inside merge records, so each gets a dated note rather than a rewrite. The ledger rows above carry the same evidence.

### 3a — the tidy pass's Docker facts (line 231)

Old text:

````text
- **Docker facts CI-pending (C-2):** the 8a body fact, the 22c in-grace and future-date facts, the 20d body fact, and the 12d / 18a strengthenings.
````

New text:

````text
- **Docker facts CI-pending (C-2):** the 8a body fact, the 22c in-grace and future-date facts, the 20d body fact, and the 12d / 18a strengthenings. *(C-1: CI-verified at C-2, run `<C-2 run id>`.)*
````

### 3b — TASK-14719's facts (line 332)

Old text:

````text
- **CI-pending** until CI-W2b.
````

New text:

````text
- **CI-pending** until CI-W2b. *(C-1: CI-verified at CI-W2b, run `41310143330` on `0984570f`; Regression 2353/2353.)*
````

## Block 4 — Three new sections: C-1's record, C-2 and Step 7a (before the Handoff block)

**Target:** `docs/sprints/SPRINT-147.md` (line 431, the Handoff block's heading; the sections go before it). Line 430, the last line of tidy item 25, is followed directly by the heading, so the New text opens with the new heading and ends with the old one.

*Why here:* the plan's close sequence records C-2's run and Step 7a's outcome in the log (`PLAN-s147.md:418`, `:427`), and the tidy pass promised its class D reasons "in the C-1 records" (`SPRINT-147.md:229`). The sections sit after the build record and before the handoff block, so they end the "Task ledger" section for the close guard's match without changing what it contains.

Old text:

````text
## Handoff block (written at every gate — the state lives here, not in the conversation)
````

New text:

````text
## C-1 — the close candidate's records (2026-11-04)

*In plain words:* S147's build is finished: every task is merged, and the defects it set out to fix are closed in the registers. C-1 writes down what had to wait for the build, so the whole sprint can be checked as one fixed commit, **K**. K then runs in CI once more (C-2) and is reviewed as a whole by both lenses (Step 7a, C-3). Only then comes the close itself: a docs-only commit on top of K (C-4). Results that only those steps can produce are held here as placeholders in angle brackets, filled at C-4.

**What C-1 recorded, and where.**

| Owed at close (where the log promised it) | Where it is now |
|---|---|
| A final disposition for every task (the plan's "Task ledger"; the close guard) | "Task ledger" above: every task DONE, with its merge and the CI run that first ran it green, or DROPPED with the reason; none was cut or deferred |
| SEC-056's row, "T9 merged" (the 14705 dispositions; O-10 Block 21; O-11) | `security-finding-register.md`: the self part CLOSED by T9; creator-not-approver and compensation closed in design (ADR-043 D3, D8), built in S148 stage 2 |
| QUAL-231 and QUAL-237 (O-10 Block 22) | `quality-finding-register.md`: both gain ADR-043 D2's bound on a partly worked day of paid absence, scheduled for S148 stage 0 |
| `QUALITY.md` (O-10 X6) | a new "S147 re-grade" section. S146's section stays as written: the file is a stack of dated snapshots |
| QUAL-214, largely closed by TASK-14718 (the 14718 dispositions) | QUAL-214's row: the save queues are fixed; the Danish text of two date-range refusals is still open |
| QUAL-236: the save's new 422 checks the vocabulary, not the agreement's list (the 14708 dispositions; tidy item 10) | QUAL-236's row |
| O-8 and O-11 | O-8 was already done (QUAL-234's interim at `3c00fc88`; QUAL-232's stage-0 refusal at O-10, `760bc767`). O-11's `SECURITY.md` half is rule 3 of "Segregation of duties" (`014eb187`); its register half is SEC-056's row above |
| O-12, the tender analysis (the 14704 dispositions) | `tender-requirements-analysis.md`: EM A1 (e) and EM A9 (b) re-rated Partly → Met; EM A33 and VD 33 stay Partly, with dated notes; the five fixed defects and R1's summary carry their evidence |
| The tidy pass's class D items (the tidy-pass dispositions) | the table below |
| The tidy pass's Docker facts (the tidy-pass dispositions) | CI-verified at C-2, run `<C-2 run id>` |
| AC-234i-1's "payroll lines identical" (tidy item 13, fourth bullet) | recorded below the table |

**The tidy pass's class D items.** Each was more than tidying: a design choice, new coverage, or a refactor across hosts. Destinations as the Orchestrator ruled for the tidy pass (`.claude/plans/s147/TIDY-PASS.md` § "Class D"):

| Item | What | Where it went | Why not in S147 |
|---|---|---|---|
| 3 | The payroll race tests' lock-holding helper and waiter observation exist twice: `FlexLedgerStaleChainRaceTests.cs:48` copies about 100 lines of `FlexLedgerRaceTests.cs:52` | ROADMAP § 2, "Tooling / infra / environment" | optional; a Docker-only refactor of race tests, which only CI can check, at the last step before C-1 |
| 5 | Two optional Docker pins (Codex's 14701 NOTE): a særlige feriedage config that ends mid-month, and the April 2026 OK-transition month | S148 stage 0's test wave (pointer in ROADMAP § 1) | new coverage, not drift |
| 11b | The refusal a departed person gets is written five times, with two log shapes | QUAL-245 (M) | not mechanical: the Payroll host cannot use the Backend's helpers, and merging the two shapes changes the log line of a security refusal |
| 12a | A repository-level Docker pin for the four-eyes SQL predicate's legacy `submitted_by` half (`ApprovalPeriodRepository.cs:1955`; RES-003 item 4) | S148 stage 0's test wave (ROADMAP § 1) | new coverage of a path the C# guard already makes unreachable; CI-only |
| 12b | Payroll answers its four-eyes refusal with the generic `payroll-export-conflict`, after calculating the month | QUAL-246 (M), for S148 stage 0 (ROADMAP § 1) | a design fork on the payroll problem contract (a high-risk export change), with Docker-only pins |
| 12e | Teamoversigt says only "Kunne ikke godkende …" when an approval is refused for segregation of duties | ROADMAP § 2, "Usability / accessibility" | a new behaviour: the server's reasons are English and need a Danish mapping |
| 14a, 14b | Which compliance items are announced to screen readers (only the notice has `role="status"`, `ComplianceWarnings.tsx:110`), and a neutral style for the "not checked" notice | S148 stage 0, with D10's rest-check half (ROADMAP § 1) | that stage replaces the notice, so both are design choices for the component it rebuilds; 14b only if the notice outlives it |
| 21a | The HRP-013 orphan roll-up counts a manager in the 5-day grace as covering their reports | S148 stage 0 (ROADMAP § 1), and a note on HRP-013 | two reads must change together, and a stand-in appointed before the manager left still covers (owner Q-B = (c)), so "covered" needs a stated rule, with Docker facts |
| 23d | A missing users row in the employed-month bound's handler overload goes on to the lock, which throws (500) | below the QUAL floor (L): a line in the register's below-floor inventory | only a data fault reaches it; no product path deletes a users row |
| X-6 | Four files that call the API are outside the typed-call lint, three of them with `as` expressions. With Fable's TIDY-4 review (NOTE 3): twelve directives in ten files switch off a hooks rule that no lint config loads. `AuditLogView.tsx` stays outside the lint slice until its directive goes | QUAL-247 (M) | clearing the `as` expressions is beyond tidying, and the directives belong with QUAL-178's baseline-ruleset decision |

**AC-234i-1's "payroll lines identical", recorded rather than named.** No golden fact pins it by name; the guarantee is structural. The TASK-14706 merge (`48a1ba25`) changed eight files (`git diff --stat 48a1ba25^1 48a1ba25`): the rest rule, the compliance result and its merger, and five test files. None is in the Payroll service, which never references the rest rule. The evaluation-model marker did not move for it: TASK-14706-T's pin held `weekly-timeline-v1` until TASK-14708 moved the marker once, on purpose, and the pin was re-pointed to v2 (`4653e5ed`).

## C-2 — the checkpoint on K

*In plain words:* K runs in CI once, on a throwaway branch, before anyone reviews it. Docker does not run on the owner's machine, so this is where the tidy pass's Docker facts run for the first time.

| | |
|---|---|
| **Commit** | K = `<K sha>` (C-1, 2026-11-04) |
| **Run** | `<C-2 run id>`, `<C-2 date>`, on branch `s147-ci-k`, through a draft PR whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2; never merge". The PR is closed unmerged and the branch deleted afterwards |
| **Result** | all seven jobs green: Unit 1715, DemoSeed 170, Regression `<C-2 Regression n>`/`<C-2 Regression n>` (Docker included), frontend 84 files / 1089, E2E, smoke, docs |
| **First run in CI here** | the four tidy merges (TIDY-1 … TIDY-4), and with them the tidy pass's Docker facts: the full-day refusal's decimal comma (item 8a); the settlement reversal's "retry from day 6", with its in-grace and future-date facts (22c); the payout refusal without the end date (20d); and the strengthened facts of 12d and 18a |

## Step 7a (C-3) — both lenses on K

*In plain words:* the sprint-end review reads everything S147 changed since S146's close, on the fixed commit K. Codex runs `codex review --base b510e45b`, because the merges are commits on master. A fix that touches code makes a new candidate, K′, which runs C-2 again and gets a verification cycle (`docs/WORKFLOW.md`, step 7a; cap 5 per lens).

| Lens | Model | Verdict | Artifact |
|---|---|---|---|
| External: Codex, base `b510e45b` | Codex | `<7a Codex verdict>` | `.claude/reviews/SPRINT-147-step7a-codex.md`, `reviewed-against-commit: <K sha>` |
| Internal: `reviewer` | `<7a reviewer model>`. The floor is `claude-fable-5-1`; an Opus stand-in also needs `.claude/reviews/SPRINT-147-review-floor-WAIVED.md`, naming the model and the owner's direction (`docs/WORKFLOW.md:42`) | `<7a reviewer verdict>` | `.claude/reviews/SPRINT-147-step7a-reviewer.md`, `reviewed-against-commit: <K sha>` |

**Cycles:** `<7a cycles>`. **Carried into the review** (`PLAN-s147.md:431`): (1) the post-close docs commits `1e105f61` … `02ef91f1`, inside the diff from `b510e45b`; (2) the Step-0b verification list, re-checked against the code as built; (3) the departed-self census re-run on K with the same command, with any write route added since the draft classified; (4) the ADR-043 review record. Each artifact is saved before acting on it; findings and rulings are recorded here, cycle by cycle.

## Handoff block (written at every gate — the state lives here, not in the conversation)
````

## Block 5 — The handoff block: As of, O-items, Phase, Next action

**Target:** `docs/sprints/SPRINT-147.md` (lines 435, 440, 442, 445). The handoff block holds the current state and is rewritten at every gate, so these are replacements, not notes. Five parts; 5b and 5c are two disjoint substrings of the O-items row.

### 5a — As of (line 435)

*Why:* the row still names 2026-11-03 and `808d4297`, two gates back.

Old text:

````text
| **As of** | 2026-11-03, on `808d4297` + this docs commit (local master; origin/master `7cc70be8` was merged in at `c29764ce`) |
````

New text:

````text
| **As of** | 2026-11-04, K = `<K sha>` (C-1, the close candidate; local master, not pushed; origin/master `7cc70be8` was merged in at `c29764ce`) |
````

### 5b — O-items, the head (line 440)

Old text:

````text
| **O-items** | **O-7 done (applied 2026-11-04, after CI-W4b run `41825042200`): SEC-054 CLOSED.**
````

New text:

````text
| **O-items** | **All twelve done (C-1, 2026-11-04).** **At C-1:** O-8 was found already done (QUAL-234's interim at `3c00fc88`; QUAL-232's stage-0 refusal at O-10, `760bc767`). O-11 done: `SECURITY.md` § "Segregation of duties" rule 3 at `014eb187`, and SEC-056's row now (O-10 Block 21, "T9 merged"). **O-12 done:** the tender analysis re-rated: EM A1 (e) and EM A9 (b) Partly → Met; EM A33 and VD 33 stay Partly, with dated notes; the five fixed defects and R1's summary carry their evidence. O-10's close half applied (Block 21; Block 22, QUAL-231 / QUAL-237), and its X6 as `QUALITY.md`'s S147 section. **O-7 done (applied 2026-11-04, after CI-W4b run `41825042200`): SEC-054 CLOSED.**
````

### 5c — O-items, the tail (line 440)

Old text:

````text
**Owed at close:** Blocks 21 (SEC-056, "T9 merged" variant) and 22 (QUAL-231 / QUAL-237). Registered this sprint so far: QUAL-236, QUAL-237 (owner, renumbered from QUAL-230), QUAL-238, QUAL-239, QUAL-240; SEC-055, SEC-056 |
````

New text:

````text
**Owed at close:** Blocks 21 (SEC-056, "T9 merged" variant) and 22 (QUAL-231 / QUAL-237), applied at C-1. Registered this sprint: QUAL-236, QUAL-237 (owner, renumbered from QUAL-230), QUAL-238, QUAL-239, QUAL-240, QUAL-241, QUAL-242, QUAL-243, QUAL-244, and at C-1 QUAL-245, QUAL-246, QUAL-247; SEC-055, SEC-056, SEC-057 |
````

### 5d — Phase, the head (line 442)

*Why only the head:* the rest of the cell is the sprint's gate history, which stays.

Old text:

````text
| **Phase** | **The tidy pass is merged** (TIDY-2 `bf63e746`, the TIDY-1 pair `c1e1edfd`, the TIDY-3 pair `80dd34b2`, TIDY-4 `46f3f82a`; GATE green after each: 144 / 114, Unit 1715, DemoSeed 170, Regression 135; frontend 84 files / 1089). **Next: C-1.**
````

New text:

````text
| **Phase** | **C-1 committed as K = `<K sha>`** (2026-11-04; see "C-1 — the close candidate's records"). **Next: C-2 on K, then Step 7a (C-3).** **The tidy pass is merged** (TIDY-2 `bf63e746`, the TIDY-1 pair `c1e1edfd`, the TIDY-3 pair `80dd34b2`, TIDY-4 `46f3f82a`; GATE green after each: 144 / 114, Unit 1715, DemoSeed 170, Regression 135; frontend 84 files / 1089).
````

### 5e — Next action (line 445)

*Why the whole cell:* every step in it moves by one. The text avoids the guarded close phrase.

Old text:

````text
| **Next action** | (1) **C-1, the records** (`PLAN-s147.md:425`), drafted by the planner as `.claude/plans/s147/C-1-DOCS-OWED.md` and applied by the Orchestrator: the task ledger's final dispositions and CI statuses; O-10 Blocks 21–22; X6 (`QUALITY.md`); QUAL-214's cross-reference; O-8 and O-11 if still owed; O-12 (the tender analysis re-rated); the tidy pass's class D rows (register, ROADMAP, S148 stage 0); the Step-7a and C-2 placeholders. Commit K, `S147 close candidate: …` (never the close phrase in the Bash command). (2) **C-2** on K. (3) **C-3**, Step 7a, both lenses, base `b510e45b`. (4) **C-4**, the close commit; push. (5) **C-5**: the Test Verified line, the model-routing register row (Opus stand-in cycles: 14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2; Fable from 14713 onward), the handoff "closed", teardown |
````

New text:

````text
| **Next action** | (1) **C-2 on K = `<K sha>`** (`PLAN-s147.md:426`): push to `s147-ci-k`; a draft PR whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2; never merge"; one background watch; `headSha` equals K; all seven jobs green. Record `<C-2 run id>`, `<C-2 date>` and the counts in "C-2 — the checkpoint on K"; close the PR unmerged; delete the branch. Red → a fix task → K′ → C-2 again. (2) **C-3, Step 7a**, both lenses, `codex review --base b510e45b`, `reviewed-against-commit: <K sha>`; the reviewer on Fable 5.1, or an Opus stand-in with `.claude/reviews/SPRINT-147-review-floor-WAIVED.md`; each artifact saved before acting; the verdicts into "Step 7a (C-3)". (3) **C-4, the close commit**, a docs-only child of K: every angle-bracket placeholder filled; `git diff --stat K HEAD` lists only `docs/**`; the guard's gates (`docs/WORKFLOW.md:41-47`); the commit message written through a file, never inside the Bash command; push; watch CI to green. (4) **C-5**: the Test Verified line with the close run id; the model-routing register's S147 row (Opus stand-in cycles: 14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2; Fable from 14713 onward); the handoff "closed"; teardown. The Step-0a DEBT branches stay the owner's call. **Outside the docs, before C-4** (not done at `9ca60740`): the tidy pass's own `.claude` edits: the demo skill's `demo_admin` line (`.claude/skills/launch-demo-system/SKILL.md:22`; tidy O-b), the refinement's erratum on T7's skip site (O-c), and in the S148 build input the S0-2 re-pin list and the class D stage-0 inputs (O-f, O-g) |
````

## Block 6 — SEC-056: the self part closed by T9 (O-10 Block 21, "T9 merged"; O-11's row)

**Target:** `docs/operations/security-finding-register.md` (line 216, the SEC-056 row; the same line as at O-10's base, `3c00fc88`). Four cell-level parts, as O-10 drafted them. **Drift since O-10:** none in the anchors; all four of O-10's Old texts are still unique at `9ca60740`. Two deviations, both reported below: 6c's Old text is the whole source cell (O-10's was its second half), and 6b and 6d are in the past tense with T9's merge and CI run filled in.

*In plain words:* SEC-056 was a leader being able to approve, reject and pay out their own overtime. T9 (TASK-14705) closed the self part: all three now refuse when the leader acts on themselves. Two halves remain, both designed in ADR-043 for S148 stage 2: a rule that whoever created a record may not approve it, and compensation only behind decided hours.

### 6a — "What it means", the last sentence (O-10's 21a, unchanged)

Old text:

````text
ADR-043 F-2/F-3 (S147's rulings round) redesign this workflow for S148.
````

New text:

````text
ADR-043 D3 and D8 (ACCEPTED 2026-10-29) redesign this workflow for S148. **S147 correction (batch-3 ruling 4(b)):** the compensate half is a latent shape, not a live capability. Nothing in production feeds the yearly balance (`AdjustAccumulatedAsync`, `OvertimeBalanceRepository.cs:50`, has no production caller), so compensate refuses (404 or 400) before it touches a balance; its discarded update result is QUAL-239. The High label rests on approve and reject.
````

### 6b — Status, the "T9 merged" variant (O-10's 21b, with the merge and the CI run filled in)

Old text:

````text
REGISTERED (S147 refinement, 2026-10-27); known — should-be-revisited; the S147 self part pending owner question Q-A
````

New text:

````text
REGISTERED (S147 refinement, 2026-10-27). **Self part CLOSED in S147 by T9** (TASK-14705, merged `b2a65fb8`; Docker facts CI-verified at CI-W2b, run `41310143330`; owner Q-A = (a), 2026-10-28): approve, reject and compensate refuse when the actor acts on themselves, with a 403 and a date-free reason. **Creator-not-approver and compensation halves closed in design** by ADR-043 (ACCEPTED 2026-10-29), built in S148 stage 2; known — should-be-revisited until built
````

### 6c — Source of truth: the registered lines marked, the closing lines added (O-10's 21c, extended)

*Why the whole cell:* the cell's code lines describe the routes before T9. The pattern SEC-054 used at O-7 keeps them as AS-REGISTERED and adds AS-CLOSED lines, so a reader can find both the defect and its fix.

Old text:

````text
`src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs:147-240, :359-435, :439-508, :512-602`; SEC-009; SEC-053; SEC-054; ADR-043 F-2 (`:255-270`), F-3 (`:272-278`); `.claude/reviews/REFINEMENT-s147-adjudication-c4.md` R-36
````

New text:

````text
AS-REGISTERED (2026-10-27, before T9): `src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs:147-240, :359-435, :439-508, :512-602`; SEC-009; SEC-053; SEC-054. AS-CLOSED, the self part (at `9ca60740`): `OvertimeEndpoints.cs:387` (approve), `:483` (reject), `:566` (compensate), each through `Endpoints/Helpers/ApprovalSelfGuard.cs:49`; `tests/StatsTid.Tests.Regression/Overtime/OvertimeSelfDecisionTests.cs`; `docs/SECURITY.md` § "Segregation of duties", rule 3. ADR-043 D3 (four-eyes on four conditions), D8 (compensation only behind decided hours); `.claude/reviews/REFINEMENT-s147-adjudication-c4.md` R-36; `.claude/reviews/S147-D-batch2-adjudication.md` ruling 1; `.claude/reviews/S147-D-batch3-adjudication.md` ruling 4; QUAL-239
````

### 6d — Adjudication (O-10's 21d, in the past tense)

Old text:

````text
→ S148's redesign (ADR-043 F-2/F-3) builds four-eyes in: a creator-not-approver rule (a creator column; reseed), a self-decision guard, and compensation only behind an approved movement. If the owner chooses Q-A (a), S147 adds the self guards on approve, reject and compensate (T9) meanwhile.
````

New text:

````text
→ Closed in design by D-4 = (c) with ruling 1 and by D-5.P2; T9 closed the self part in S147 (TASK-14705). S148 stage 2 builds four-eyes in: a creator column with creator-not-approver on every record that awaits a decision (reseed); no decision on the decider's own hours (403) on orders, mark decisions, compensations and cap exceptions; the authority to order or decide is the month-approval authority, not bare organisation scope; compensation only behind decided earned movements. Cross-reference: QUAL-239 (the compensate route commits an event its guarded update refused; interim refusal in S148 stage 0, retires with the route in stage 2).
````

## Block 7 — QUAL-231 and QUAL-237: ADR-043 D2's bound (O-10 Block 22)

**Target:** `docs/operations/quality-finding-register.md` (lines 455 and 461; the same lines as at O-10's base). **Drift since O-10:** none in the anchors. QUAL-231's row was closed at O-4 in between; its Adjudication cell still ends with the anchor, as O-10 foresaw. One deviation: 7a says "is scheduled to close … not built in S147" where O-10 said "closes", so the sentence cannot be read as built.

*In plain words:* a day can hold some worked hours and some paid absence. ADR-043 D2 caps the paid absence at the day's credit basis minus the hours worked, checked whichever is saved first. That closes QUAL-231's last residual and QUAL-237 (a day's absence limit that counted only the cells in one save), in S148 stage 0.

### 7a — QUAL-231 (line 455, the end of the Adjudication cell)

Old text:

````text
Nearest precedent: QUAL-222. |
````

New text:

````text
Nearest precedent: QUAL-222. **S147 close (ADR-043 D2, batch-3 ruling 3):** the residual, a partly worked day of paid absence, is scheduled to close in S148 stage 0 with R-7, ADR-043's bound on such a day, as widened (not built in S147): *"A day's paid-absence hours, of any credit-class type, may not exceed the credit basis minus the hours worked that day; judged at the Skema save in both directions (QUAL-237)"*. |
````

### 7b — QUAL-237 (line 461, the end of the Adjudication cell; O-10's 22b, unchanged)

Old text:

````text
Pin it with two saves in a row (the `admin01` shape). |
````

New text:

````text
Pin it with two saves in a row (the `admin01` shape). **S147 close (ADR-043 D2, batch-3 ruling 3):** scheduled for S148 stage 0 (no longer backlog), in R-7's widened form: *"A day's paid-absence hours, of any credit-class type, may not exceed the credit basis minus the hours worked that day; judged at the Skema save in both directions (QUAL-237)"*. Both directions means a paid-absence save on a day that already holds worked hours, and a later work save on a day that already holds paid-absence hours, each judged against the day's stored state. |
````

## Block 8 — QUAL-214: largely closed by TASK-14718; the Danish-text half stays open

**Target:** `docs/operations/quality-finding-register.md` (line 438, the QUAL-214 row; the end of its "→" cell).

*In plain words:* QUAL-214 was a Skema defect: moving to another month could send an edit under the wrong month, or leave a work-time save stuck until the page reloaded. TASK-14718, fixing a related race (QUAL-241), made navigation send every pending edit first and made a month's load clear both queues. What is left is the row's smaller half: two refusals still show English server text.

Old text:

````text
A small UX task for S146. |
````

New text:

````text
A small UX task for S146. **S147 note (C-1, 2026-11-04): largely closed by TASK-14718** (QUAL-241, merged `782e2285`; the frontend job green at CI-W2b, run `41310143330`). Navigation now sends every pending save before the month changes, each under the month on screen (`SkemaPage.tsx:1005-1007`; fix-up 1, `ed884d6f`), and a month's load clears both queues, `workTimeDirtyRef` included (`:498-507`), so neither the wrong-month send nor the stuck work-time queue remains. TASK-14720 then bounded the whole flush, so a save that never answers cannot hold the page (`:709-713`; merged `3ee2a8c9`; CI-W3, run `41335270197`). **Still open, so the row stays REGISTERED:** the two date-range refusals still carry English server text (`SkemaEndpoints.cs:860`, `:869`, `:888`, at `9ca60740`), and the frontend has no Danish mapping for them. |
````

## Block 9 — QUAL-236: the new refusal checks the vocabulary, not the agreement's list

**Target:** `docs/operations/quality-finding-register.md` (line 460, the QUAL-236 row; the end of its "→" cell). The tidy pass gave this to the Orchestrator (`TIDY-PASS.md` O-a; tidy item 10, third bullet).

*In plain words:* S147 made Skema refuse an absence code that does not exist. It does not refuse a code that exists but belongs to another agreement, so a request built by hand can save HK's third child's sick day, which HK does not grant. The screen cannot do it, because it offers only the codes it was given. S148 stage 0, which fixes QUAL-236, is the place to close it.

Old text:

````text
the per-episode limit, OK26's third day and the call-home day. |
````

New text:

````text
the per-episode limit, OK26's third day and the call-home day. **S147 note (C-1, 2026-11-04; TASK-14708 review, Fable NOTE 2; tidy item 10):** S147's new Skema refusal of an unknown absence code (422 `absence_type_unknown`, TASK-14708) checks the code against the shared vocabulary only (`AbsenceClassification.IsKnown`, `SkemaEndpoints.cs:970-976`), not against the agreement's own list (`GetAbsenceTypesForAgreement`, `:50-63`, which only the month GET reads, `:2684`). All three child codes draw on one `CHILD_SICK` quota (`Services/EntitlementMapping.cs:61-63`), so a hand-built HK request can still save `CHILD_SICK_DAY_3` within HK's quota; the grid offers only the codes it was served, so the screen cannot. Stage 0's fix should check each saved code against the agreement's list as of that day. |
````

## Block 10 — Three new QUAL rows (class D), and one below-floor note

**Target:** `docs/operations/quality-finding-register.md`. Two parts. 10a: the rows follow QUAL-244 (line 468, the file's last line), in the same continuation table, with its columns: QUAL · what it means · title · Dim · Sev · status · source of truth · adjudication. 10b: item 23d joins the below-floor inventory's "Also recorded" sentence (line 385).

### 10a — QUAL-245 (tidy 11b), QUAL-246 (12b), QUAL-247 (X-6)

Old text:

````text
The first is smaller. |
````

New text:

````text
The first is smaller. |
| QUAL-245 | **The refusal a person who has left gets on an admin route is written five times, and logged in two shapes.** S147's departed-self rule refuses a person who has left from changing their own record (TASK-14703). Three Backend endpoint files each carry a private copy of one helper, `DepartedSelfRefusal` (`AdminEndpoints.cs:3815-3821`, `UnitEndpoints.cs:1082-1088`, `ReportingLineEndpoints.cs:2965-2971`); Payroll's `/recalculate` writes the same refusal inline (`Payroll/Program.cs:591-601`); and the self-service floor has its own shared helper, `DepartedSubjectWriteFloor.Refusal` (`Endpoints/Helpers/DepartedSubjectWriteFloor.cs:145-158`). The 403 body is the same everywhere, but the WARNING line is not: the four copies log actor, role and route under each route's own logger, while the floor adds the subject (`Subject=`) under its own category (`:69`, `:153-155`). So a change to this refusal, its wording or a log field, has five places to find, and an operator searching the log for departed-self refusals meets two shapes. | Departed-self refusal written five times, two log shapes | D-maintainability | M | **REGISTERED (S147 C-1, 2026-11-04; TASK-14703 review, Fable NOTE 1; tidy item 11b, class D).** Not fixed in S147: one helper cannot simply replace them. The Payroll host cannot use the Backend's `Endpoints/Helpers/`, and merging the two shapes changes the log line of a security refusal | `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs:3815-3821`; `UnitEndpoints.cs:1082-1088`; `ReportingLineEndpoints.cs:2965-2971`; `Endpoints/Helpers/DepartedSubjectWriteFloor.cs:69, :145-158`; `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:591-601` (all at `9ca60740`); `docs/SECURITY.md` (the departed-self rule) | → decide one log shape for the refusal (with or without the subject; one category or each route's own), then one helper per host, the Backend's beside the floor's in `Endpoints/Helpers/`, with a test that pins the log line, so the next guarded route copies nothing. M, narrowly: it is duplication, but of a security refusal whose copies have already drifted apart in their logging. |
| QUAL-246 | **When payroll refuses a month because one person signed it twice, it answers with the same "export conflict" as any other conflict, and only after calculating the month.** Since SEC-053, payroll re-checks four-eyes inside the export transaction, under the period's row lock: the approver may be neither the sender nor the employee (`PayrollExportService.cs:544-553`, inside the re-assert `:487`). Each check throws `PayrollExportConflictException`, which `/calculate-and-export` answers as 409 with `kind = "payroll-export-conflict"` (`Payroll/Program.cs:493-511`), the kind it also gives a month already exported with other content, a line spanning two months, or a period no longer APPROVED. The month has been calculated by then, and its calculation proof stays (`:488-490`). So a caller cannot tell "a second person must approve this month" from "the month changed under you", and a future HR export screen (HRP-022) could not show the right remedy. | Payroll four-eyes refusal has no kind of its own | D-contract | M | **REGISTERED (S147 C-1, 2026-11-04; TASK-14704 reviews, tidy item 12, second bullet; tidy item 12b, class D).** Not fixed in S147: a design fork on the payroll problem contract (a high-risk export change), with Docker-only pins. Meanwhile the shared kind is documented at the handler (`Program.cs:495-502`) | `src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollExportService.cs:487, :544-553`; `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:488-490, :493-511` (all at `9ca60740`); SEC-053; `docs/SECURITY.md` § "Segregation of duties" | → choose: (a) pre-screen the two equalities in the admission read, before the calculation, with a kind of its own, and keep the in-transaction check as the race guard; or (b) give only the in-transaction refusal its own kind. Either changes the payroll problem contract, so it needs a refinement line and Docker pins. S148 stage 0 is the earliest home (ROADMAP § 1). |
| QUAL-247 | **The lint rule that keeps hand-written API types out of the frontend misses four files that call the API, and twelve comments switch off a rule no lint config loads.** Since S111 the typed-call rules (no `as`, no hand-written type argument on an API call) apply only to the files `eslint.config.mjs` lists (`TYPED_SLICE_FILES`, in the two `files:` blocks at `:178` and `:191`), linted through `npm run lint`'s own file list (`package.json:13`). S147 added TASK-14714's `useHrFollowUp.ts` to both (tidy item 23a). Four more files call the API and are in neither: `useCalendarBootstrap.ts` (`:243`), `useHrBackdateWorklist.ts` (`:45`, `:65`), `useTermination.ts` (five calls, `:227-295`) and `AuditLogView.tsx` (`:65`). Three contain `as` expressions the rule forbids (`useCalendarBootstrap.ts:91`, `:105`; `useHrBackdateWorklist.ts:33`, `:40`; `useTermination.ts:105`, `:126`, `:139`, `:151`, `:164`), so they must be rewritten before they can join. `AuditLogView.tsx` has none, but it stays out for another reason (`eslint.config.mjs:117-118`): its `eslint-disable-line react-hooks/exhaustive-deps` (`:98`) names a rule from a plugin the config never loads, so linting the file fails. That comment is one of twelve, in ten files (Fable's TIDY-4 review, NOTE 3): `AuditLogView.tsx:98`, `OpfoelgningPage.tsx:69`, `:113`, `AuthContext.tsx:94`, `useCalendarBootstrap.ts:381`, `SkemaDayPanel.tsx:153`, `MondayDatePicker.tsx:130`, `InlineApproverControl.tsx:60`, `PersonDrawer.tsx:339`, `:462`, `:478`, `StrukturPanel.tsx:467`. Each tells a reader that a hooks rule runs there; none does (QUAL-178). | Typed-call lint misses four API files; twelve directives for an unloaded rule | D-quality | M | **REGISTERED (S147 C-1, 2026-11-04; the TASK-14713 stack review and tidy item 23a; Fable's TIDY-4 review, NOTE 3; tidy X-6, class D).** Not fixed in S147: the `as` rewrites in three files are beyond tidying. `AuditLogView.tsx` stays outside the typed-call slice until its directive goes (recorded at `eslint.config.mjs:117-118`) | `frontend/eslint.config.mjs:117-118, :178, :191`; `frontend/package.json:13`; the files and lines above, under `frontend/src/` (all at `9ca60740`); `.claude/reviews/S147-TASK-14724-5a-reviewer.md:8` (NOTE 3); QUAL-178 | → (1) replace the `as` expressions in the three files with runtime type guards, as the rule's own message advises, then add all four files to both lists; (2) for the twelve directives, load `eslint-plugin-react-hooks` and keep them, or delete them. Decide (2) with QUAL-178's baseline ruleset, whose report-only count would show what the hooks rules flag; it may be the same change. |
````

### 10b — item 23d, below the floor (line 385, the inventory's "Also recorded" sentence)

Old text:

````text
`SkemaEndpoints.cs:1490-1491` stale anchors (unverified).
````

New text:

````text
`SkemaEndpoints.cs:1490-1491` stale anchors (unverified). **S147 (C-1, 2026-11-04), below the floor (L):** a missing users row in the employed-month bound's handler overload (`ApprovalEndpoints.cs:93-108`; its summary, `:91`, leaves that case to the handler's downstream reads) goes on to the lock, which throws (500), as Fable's TASK-14713 review traced (`.claude/reviews/S147-TASK-14713-5a-reviewer.md:11`). Only a data fault reaches it: `approval_periods.employee_id` has no foreign key to `users` (`init.sql:902`), but no product path deletes a users row (tidy item 23d).
````

## Block 11 — HRP-013: the orphan roll-up and the 5-day grace (class D, 21a)

**Target:** `docs/operations/hr-follow-up-process-register.md` (line 119, the HRP-013 row; the end of its last cell).

*In plain words:* HR's "employees with no approver" list counts an employee as covered while their manager is in the 5 days after leaving. Since TASK-14712 that manager can no longer approve, so the list can say "covered" when, at the top of an organisation, nobody above can approve. Fixing it needs a rule for what "covered" means, because a stand-in appointed before the manager left still counts.

Old text:

````text
keep; add a cross-org roll-up |
````

New text:

````text
keep; add a cross-org roll-up. **S147 note (C-1, 2026-11-04; tidy item 21a):** since TASK-14712 a departed manager stops counting as an approver the day after their last working day, while the 5-day grace keeps them switched on, and their reports' months go to the next manager up. This row's cross-organisation roll-up (`HrFollowUpApprovalReadRepository.cs:410-436`) and the per-organisation card (`ApprovalPeriodRepository.cs:1237`) still count any open PRIMARY reporting line as cover. So from day 1 to day 5 such an employee reads as covered by the departed manager, whether or not anyone above can approve. Not fixed in S147: the roll-up promises to equal the card (`HrFollowUpApprovalReadRepository.cs:398-409`), so both reads must change together, and a stand-in appointed before the manager left still covers (owner Q-B = (c)), so "covered" needs a stated rule. An S148 stage-0 input (ROADMAP § 1). |
````

## Block 12 — ROADMAP: the S148 stage-0 pointer and two backlog lines (class D)

**Target:** `ROADMAP.md`. Three parts. The S148 build input lives under `.claude/`, which is not a docs target, so § 1 can only hold a pointer to the stage-0 items.

### 12a — § 1 item 4: the tidy pass's inputs to S148 stage 0 (line 59)

Old text:

````text
The source register's unreliable citations (QUAL-215) make the Phase-B engagement above more urgent.
````

New text:

````text
The source register's unreliable citations (QUAL-215) make the Phase-B engagement above more urgent.
   **S147's tidy pass handed S148 stage 0 six small inputs** (recorded at S147's C-1; detail in
   `docs/sprints/SPRINT-147.md`, "C-1 — the close candidate's records"): two optional Docker pins, a særlige
   feriedage config ending mid-month and the April 2026 OK-transition month; a repository pin for the four-eyes SQL
   predicate's legacy `submitted_by` half; payroll's four-eyes refusal getting a kind of its own (QUAL-246); which
   compliance items are announced to screen readers, and a neutral style for the "not checked" notice, both with
   D10's rest-check half, which replaces the notice; and the HRP-013 orphan roll-up counting a manager in the 5-day
   grace as covering. S148's refinement places each.
````

### 12b — § 2 "Tooling / infra / environment": tidy item 3 (after line 552)

Old text:

````text
`Skema/SkemaAccrualCapTests`, `Skema/SkemaFullDayOnlyGuardTests`). A mechanical `sweep` task; tests only.
````

New text:

````text
`Skema/SkemaAccrualCapTests`, `Skema/SkemaFullDayOnlyGuardTests`). A mechanical `sweep` task; tests only.
- **Backlog — one copy of the payroll race-test machinery (S147 tidy item 3, from the TASK-14700-T review).**
  `FlexLedgerStaleChainRaceTests.cs:48` copies about 100 lines of `FlexLedgerRaceTests.cs:52`: the `HeldLock`
  helper that holds the ledger lock, and the observation that a waiter is blocked on it. Lift both into the
  flex-ledger harness. Docker-only, so only CI can check it; do it with the next task that edits either race file.
  Tests only.
````

### 12c — § 2 "Usability / accessibility": tidy item 12e (after line 497)

Old text:

````text
(§2 "Governance / docs", owner-raised 2026-10-26).
````

New text:

````text
(§2 "Governance / docs", owner-raised 2026-10-26).
- **Teamoversigt: say why an approval was refused for segregation of duties** (S147, the TASK-14704 review;
  tidy item 12e). Today a refused approval reads only "Kunne ikke godkende <navn>."
  (`frontend/src/pages/approval/TeamOversigt.tsx:603`), whether a leader tried to approve their own month (SEC-009)
  or an HR user a month they sent on the employee's behalf (SEC-053, since S147). The server's reasons are English,
  so this needs a Danish mapping, as HR's leaver list already has for the four-eyes case
  (`frontend/src/pages/admin/opfoelgning/FollowUpLists.tsx:191`). A small UX task.
````

## Block 13 — O-12: the tender analysis re-rated where S147's build changed a rating

**Target:** `docs/references/tender-requirements-analysis.md` (LF). Eighteen parts.

*In plain words:* the tender analysis of 2026-10-26 measured StatsTid at commit `67809348` against two real state tenders. S147 fixed five of the eight defects it found, and that changes two ratings. Both are sub-rows of a larger requirement:
- **EM A1 (e), the four-eyes rule: Partly → Met.** Whoever sent a month can no longer approve it.
- **EM A9 (b), the vacation account: Partly → Met.** The verification had lowered it only because særlige feriedage could not be registered, and now they can.

The leaver rows (EM A33, VD 33) stay Partly: the dead end is gone, but deletion and on-screen editing are still missing. The tallies count only primary rows, so they do not change.

*The rule applied:* a row is re-rated only where the build changed the rating; a row the build did not change is not touched (plan `:320`). Rows whose stated facts S147 changed without changing their rating are X2–X5, optional. Each note cites the closed register row, the merge and the CI run.

### 13a — Status (line 7): the re-check, so a reader knows which commit each row describes

Old text:

````text
**Status:** verified, row by row. Where the verification changed a rating or corrected a text, that is marked in the row, and the verification governs.
````

New text:

````text
**Status:** verified, row by row. Where the verification changed a rating or corrected a text, that is marked in the row, and the verification governs. **S147 re-check (2026-11-04, at S147's C-1):** S147 fixed five of the eight defects below (D1, D2, D3, D4, D8) and built an interim for a sixth (D7). Where that changed a rating, the row says so with its evidence: EM A1 (e) and EM A9 (b) rise from Partly to Met. Both are sub-rows, so the tallies, which count primary rows, do not change. Rows without an S147 note still describe `67809348`.
````

### 13b — The defects' introduction (line 85)

Old text:

````text
All eight are now in the registers as **REGISTERED**: recorded, not yet adjudicated or fixed, with the trace's proposed severity.
````

New text:

````text
All eight are now in the registers as **REGISTERED**: recorded, not yet adjudicated or fixed, with the trace's proposed severity. *(S147, 2026-11-04: five are now fixed and CLOSED: D1 SEC-053, D2 SEC-054, D3 QUAL-230, D4 QUAL-231 and D8 QUAL-235, each with its evidence in its row. D7 QUAL-234 has an interim. It, D5 QUAL-232 and D6 QUAL-233 are decided in ADR-043, ACCEPTED 2026-10-29, and are built from S148.)*
````

### 13c — D7, QUAL-234 (line 91): an interim only

Old text:

````text
It bears directly on S147: the new supplement window in ADR-043 changes nothing for Skema users unless clock times reach payroll. |
````

New text:

````text
It bears directly on S147: the new supplement window in ADR-043 changes nothing for Skema users unless clock times reach payroll. *(S147: an interim only, so the row stays open. The rest check now lists the days it could not check, and the employee's compliance panel and Teamoversigt say "Hviletid ikke kontrolleret for N dage", so a manager no longer reads "no breach" for those days (TASK-14706, TASK-14707, merged `48a1ba25`, `615f3ee5`; CI-verified at CI-W2b, run `41310143330`). Supplements are still not computed from Skema's times. Decided in ADR-043 D10: payroll and the rest check read Skema's intervals, the rest half in S148 stage 0.)* |
````

### 13d — D1, SEC-053 (line 92)

Old text:

````text
Both tenders ask for this four-eyes rule, as does the ØS guide. |
````

New text:

````text
Both tenders ask for this four-eyes rule, as does the ØS guide. *(S147: fixed; SEC-053 CLOSED 2026-11-01. Whoever sent a month can neither approve nor reject it, checked at the door, inside the transaction and again at payroll (owner OQ-1 = (a); TASK-14704, merged `002787c6`; CI-verified at CI-W2b, run `41310143330`).)* |
````

### 13e — D3, QUAL-230 (line 93)

Old text:

````text
and payroll's export refuses them too, so the final month cannot be paid through the normal path. |
````

New text:

````text
and payroll's export refuses them too, so the final month cannot be paid through the normal path. *(S147: fixed; QUAL-230 CLOSED 2026-11-03. The leaver's own manager can approve, reject or reopen the final month for 5 calendar days after the last working day; from day 6 HR can, from its follow-up page, and payroll exports the month (owner OQ-2; the TASK-14710, TASK-14712 and TASK-14713 merges `9c7866f0`, `52ab15c8`, `652f26aa`, on TASK-14703's departed-self rule; closing run CI-W3b, `41641201066`).)* |
````

### 13f — D4, QUAL-231 (line 94)

Old text:

````text
Skema and the calculation engine spell the sickness codes differently, so the engine never credits the day against the norm. |
````

New text:

````text
Skema and the calculation engine spell the sickness codes differently, so the engine never credits the day against the norm. *(S147: fixed; QUAL-231 CLOSED 2026-11-01. One vocabulary in Skema's spellings; a paid sick day or child's sick day is credited with the day's planned hours, for HK and PROSA by the agreement and for AC-agreement staff as a labelled default (TASK-14708, merged `7cc6e65e`; CI-verified at CI-W2b, run `41310143330`).)* |
````

### 13g — D8, QUAL-235 (line 97)

Old text:

````text
The year-end settlement, switched off today, would value every earned day as untaken. |
````

New text:

````text
The year-end settlement, switched off today, would value every earned day as untaken. *(S147: fixed; QUAL-235 CLOSED 2026-10-29. Skema shows a "Særlig feriedag" row where the agreement grants the days, part days allowed, and a day taken shows as used (TASK-14701, TASK-14702, merged `6bf4e6e4`, `61d6a98a`; CI-verified at CI-W1, run `40839583193`).)* |
````

### 13h — D2, SEC-054 (line 98)

Old text:

````text
This is documented as "by design" but was never weighed against segregation of duties. |
````

New text:

````text
This is documented as "by design" but was never weighed against segregation of duties. *(S147: fixed; SEC-054 CLOSED 2026-11-04. Both routes are retired and answer 404, and every export made since then comes from the employee's own approved month, approved by someone other than the employee and the sender (owner OQ-3 = (a) + (i); TASK-14715, TASK-14716, merged `9cbf164b`, `169e9980`; CI-verified at CI-W4 and CI-W4b, runs `41708962373`, `41825042200`). A correction still needs only an existing export record.)* |
````

### 13i — EM A1, the parent row (line 140): its gap cites sub-row (e)

Old text:

````text
no start/end-date absence; four-eyes only half-enforced (sub-row e). | ADR-035, ADR-038 |
````

New text:

````text
no start/end-date absence; four-eyes only half-enforced (sub-row e). *(S147: four-eyes is now enforced for the monthly sign-off, so sub-row (e) is Met. HR also has one on-behalf screen now, for a leaver's final month (send, approve, reject from day 6). The parent stays Partly for its other gaps.)* | ADR-035, ADR-038 |
````

### 13j — EM A1 (e), the rating (line 145)

Old text:

````text
| EM A1 (e) | Must | Four-eyes / SoD | *4-øjne-princippet*: one person (e.g. an administrator) must not be able to approve both as employee and as chef. | Partly |
````

New text:

````text
| EM A1 (e) | Must | Four-eyes / SoD | *4-øjne-princippet*: one person (e.g. an administrator) must not be able to approve both as employee and as chef. | Met |
````

### 13k — EM A1 (e), the gap and the link (line 145)

Old text:

````text
A LocalLeader cannot do both legs, because the S127 R4 floor stops them sending for others. | SEC-009 (fixed); R5 / VD 35 |
````

New text:

````text
A LocalLeader cannot do both legs, because the S127 R4 floor stops them sending for others.<br>**S147: rated Partly → Met.** Whoever sent a month, the employee or an HR user on their behalf, can now neither approve nor reject it. It is refused at the door (403), again inside the transaction that writes the decision, and at payroll, where the approver may be neither the sender nor the employee (SEC-053 CLOSED; owner OQ-1 = (a), strict, no exception; TASK-14704, merged `002787c6`; CI-verified at CI-W2b, run `41310143330`; `docs/SECURITY.md` § "Segregation of duties"). A leader also cannot decide their own overtime (SEC-056's self part; TASK-14705, merged `b2a65fb8`). Outside this requirement and still open: Teamoversigt shows only "Kunne ikke godkende" when the 403 fires (ROADMAP, "Usability / accessibility"), and overtime records get a creator-not-approver rule only in S148 (ADR-043 D3). | SEC-009 (fixed); SEC-053 (closed S147); SEC-056; R5 / VD 35 |
````

### 13l — EM A9, the parent row (line 241): its verification note cites QUAL-235

Old text:

````text
**Verification:** Add: *særlige feriedage* accrue and display, but no screen can register taking one (QUAL-235). | ADR-030, ADR-031, ADR-032, ADR-033;
````

New text:

````text
**Verification:** Add: *særlige feriedage* accrue and display, but no screen can register taking one (QUAL-235). *(S147: they can now be registered; QUAL-235 CLOSED, see EM A9 (b). The parent stays Partly for the year close, the taking period and the special-day transfer.)* | ADR-030, ADR-031, ADR-032, ADR-033;
````

### 13m — EM A9 (b), the rating (line 243)

Old text:

````text
| EM A9 (b) | Must | Vacation — account | Each employee's vacation account (*ferieregnskab*). | Partly |
````

New text:

````text
| EM A9 (b) | Must | Vacation — account | Each employee's vacation account (*ferieregnskab*). | Met |
````

### 13n — EM A9 (b), the gap (line 243)

Old text:

````text
Leaders also see vacation used/total per team member in Teamoversigt (`Contracts/ApprovalResponses.cs:193-194`). | — |
````

New text:

````text
Leaders also see vacation used/total per team member in Teamoversigt (`Contracts/ApprovalResponses.cs:193-194`).<br>**S147: rated Partly → Met,** undoing the verification's downgrade, whose one reason is gone. Skema registers *særlige feriedage* since S147, part days included, and a day taken shows as used on the balance and the year's snapshot (QUAL-235 CLOSED; TASK-14701 and TASK-14702, merged `6bf4e6e4`, `61d6a98a`; CI-verified at CI-W1, run `40839583193`). The first gap above, a screen only for the logged-in user, stood when the research first rated the row Met. | — |
````

### 13o — EM A33, leavers (line 163): still Partly

Old text:

````text
also refuses a deactivated leaver (QUAL-230). | ADR-025 D3; HRP-011 |
````

New text:

````text
also refuses a deactivated leaver (QUAL-230).<br>**S147: still Partly; the dead end is gone** (QUAL-230 CLOSED 2026-11-03; closing run CI-W3b, `41641201066`). The system now switches a leaver off automatically on day 6 after the last working day; their own login still ends on day 1. Until then the leaver's own manager can approve, reject or reopen the final month; from day 6 HR can, and payroll exports the month. HR's follow-up page lists each leaver's unapproved final month with "Åbn måned" (the read-only month grid a manager sees), "Send måned", "Godkend" and "Afvis" (`frontend/src/pages/admin/opfoelgning/FollowUpLists.tsx:314-339`; TASK-14714). Still missing: reopening a leaver's month from a screen (the API allows HR from day 6), and GDPR deletion. | ADR-025 D3; HRP-011 |
````

### 13p — VD 33, leavers (line 181): still Partly

Old text:

````text
also refuses a deactivated leaver (QUAL-230). | ADR-025 D3 |
````

New text:

````text
also refuses a deactivated leaver (QUAL-230).<br>**S147: still Partly.** The leaver dead end is gone (QUAL-230 CLOSED; see EM A33): HR can see a leaver's final month on screen, read-only, and send, approve or reject it there. Of the weighted items, editing a leaver's registrations on a screen and deletion are still missing. | ADR-025 D3 |
````

### 13q — R1's summary: four-eyes (line 189)

Old text:

````text
An independent trace confirmed it; it is registered as SEC-053.
````

New text:

````text
An independent trace confirmed it; it is registered as SEC-053. *(S147: fixed. Whoever sent a month can now neither approve nor reject it, and payroll checks the two signers again (SEC-053 CLOSED); EM A1 (e) is re-rated Met.)*
````

### 13r — R1's summary: leavers (line 190)

Old text:

````text
so the final month cannot be paid through the normal path (registered as QUAL-230).
````

New text:

````text
so the final month cannot be paid through the normal path (registered as QUAL-230). *(S147: fixed. The leaver's own manager has 5 days after the last working day, then HR acts from a list with the actions on screen, and payroll pays the month (QUAL-230 CLOSED). EM A33 and VD 33 stay Partly for their other gaps.)*
````

## Block 14 — `QUALITY.md`: the S147 re-grade (O-10's X6)

**Target:** `docs/QUALITY.md` (LF; line 3, the freshness anchor; line 11, the S146 section's heading). Two parts.

*Why a new section, not O-10's one-line fix:* O-10's X6 pointed at `QUALITY.md:26`, the S146 snapshot's "ADR-043 PROPOSED with nine forks". The file's own rule says each section is a dated snapshot that is "never rewritten", superseded by a new section (`QUALITY.md:6-8`). The sprint log already deferred X6 to the close for this reason (`SPRINT-147.md:440`). The grades below are the planner's proposal; the Orchestrator grades.

### 14a — the freshness anchor (line 3)

Old text:

````text
<!-- anchor-sprint: 146 -->
````

New text:

````text
<!-- anchor-sprint: 147 -->
````

### 14b — the S147 section, above S146's (line 11)

Old text:

````text
## S146 re-grade (2026-10-26) — payroll correctness: every paid month provable, one flex balance per month
````

New text:

````text
## S147 re-grade (2026-11-04, at C-1) — confirmed defects closed; the overtime/flex model decided

**The sprint that fixed what the tender analysis confirmed, and decided the overtime model before anyone builds it.**
- **What changed:** five confirmed defects are closed and CI-verified. A leaver's final month can be approved and paid (QUAL-230); a sick day no longer costs flex (QUAL-231); særlige feriedage can be registered (QUAL-235); one person can no longer sign a month twice (SEC-053); and the two "raw" payroll routes are retired, so every export starts from an approved month (SEC-054). Also closed: the stale-chain guard (QUAL-229), a leader deciding their own overtime (SEC-056's self part), and a real Skema race that had passed for a flaky test (QUAL-241). A person who has left can no longer change their own record through any of the 84 write routes in the three hosts.
- **What was decided, not built:** ADR-043 ACCEPTED (2026-10-29): a monthly norm, flex by default, overtime only for decided hours, built in stages from S148. The S146 snapshot's "ADR-043 PROPOSED with nine forks" is superseded here, not edited.

| Domain | Grade | Evidence / what changed |
|--------|-------|-------------------------|
| **Payroll Integration** | A− → **A−** (held, ▲) | QUAL-229, one of S146's named caps, is closed: payroll refuses a month whose opening balance rests on a month corrected later, and names the earliest month to fix. SEC-054: `ExportAsync` refuses an export with no approval period, the period must be the exported employee's own whole month, and an export is one (employee, month) (owner R-G9). A month before the ledger start is payable with its flex "not tracked", and a leaver's final month from day 6. **Held below A** by QUAL-189 and QUAL-211, by the overtime-model findings S148 owns, by the raw routes' leftovers (QUAL-243) and the undated wage-type lookup (QUAL-244), and by corrections staying single-signer |
| **Domain Correctness** | B+ → **B+** (held, ▲) | Sick days and child's sick days are credited with the day's planned hours, through one vocabulary and one classification (QUAL-231; marker `weekly-timeline-v2`); særlige feriedage can be taken in part days (QUAL-235). The overtime model is decided (ADR-043) but the code is still weekly and automatic. **Held** by QUAL-232 (on-call hours paid twice), QUAL-234 (no supplements from Skema's times; the rest check now says "not checked" instead of nothing), QUAL-236 (the child's-sick-day quotas, found this sprint, High) and QUAL-237 and QUAL-240 (the paid-absence day limits, S148 stage 0) |
| **Security & Access Control** | A− → **A−** (held, ▲) | Strict four-eyes on the monthly sign-off, at the door, in the transaction and at payroll (SEC-053, owner OQ-1 = (a)); no approval-free export route (SEC-054); a leader cannot decide their own overtime (SEC-056's self part). A departed person's own record is closed on every write route: the census counts 84 in the three hosts (32 through the shared check, 19 guarded otherwise, 33 classified out), and login is refused from the day after the last working day. **Held:** SEC-056's creator-not-approver half is designed, not built; SEC-055 and SEC-057 (Low) registered; a departing HR user's or manager's live token on other people's data (at most 8 hours) stays SEC-003's accepted residual by owner ruling Q-C = (b) |
| **Auditability** | A → **A** (held) | Every first export since TASK-14716 comes from an approved month with two different signers, re-asserted under the row lock, and names its period. Held at A, not raised: a correction needs only an existing export record, so a record the raw routes wrote before S147 can still be corrected until the database is reseeded (SEC-054's residual) |
| **Backend API** | A− → **A−** (held) | The leaver timeline (the manager's 5-day grace, HR only from day 6, the employed-month bound with date-free refusals); the four-eyes and overtime self 403s; an unknown absence code refused (422); the særlige feriedage row; two unused project routes retired. Held: QUAL-238, QUAL-245 (the departed-self refusal written five times), and QUAL-236's agreement check at the save |
| **Frontend** | A → **A** (held) | HR can act on a leaver's final month on screen (open it read-only, send, approve, reject, with Danish refusals); the "not checked" notice; one map for the entitlement names (owner C-1 = (a)); a Skema race behind a "flaky" test fixed after five review cycles (QUAL-241). Held: QUAL-242, QUAL-247 (four API files outside the typed-call lint), QUAL-178 |
| **Test Suite (cross-cutting)** | A → **A** (held) | Seven CI checkpoint runs, each running every Docker fact before the next wave built on it. CI-W2 went red on eight facts (five from one seeding bug, two re-decided expectations, one timing fault in the test host), none a product fault, fixed by TASK-14719. A test that had looked flaky since S121 was catching a real race (QUAL-241): the lesson is to treat a "flake" as a finding first |
| **Documentation (canon)** | materially recovered → **materially recovered** (held) | ADR-043 ACCEPTED with its documents (`SYSTEM_TARGET.md` updated with the owner's approval); ADR-034 and ADR-040 amended; one more verified research dossier (the sick-day credit); the tender analysis re-rated at C-1; the HR register's leaver rows updated and five ADR-043 hand-offs designed (HRP-025 … HRP-029). Held: comments the sprint's own changes made false needed a tidy pass of about forty edits |
| **Governance / review effectiveness** | — | Observations, not conclusions. Fable was at its usage limit from 2026-10-30 to 2026-11-01; the internal lens ran on Opus as a labelled stand-in for five reviews (TASK-14719, TASK-14718 cycles 2–5, TASK-14720, TASK-14712, the TASK-14710 stack), and on Fable again from 2026-11-03. TASK-14718 reached the five-cycle cap with one blocker left, and the owner chose "merge + small follow-up" (TASK-14720). The refinement took five Codex cycles; at the cap the owner chose "fold and sweep", which produced the 84-route census. Step 7a's outcome is in the sprint log |

*(Domains not listed — Rule Engine, SharedKernel, Infrastructure, PostgreSQL Schema, Docker, CI/Tooling — are unchanged in grade by S147. The rest rule now reports the days it could not check; SharedKernel gained the absence classification; Infrastructure gained the departed-approver reads and the terminated-inclusive tree lock; the schema changed by one comment line.)*

**PM reading.** S147 is the sprint in which the defects a careful outside reading found were fixed, not just filed. Four of the five were about who may do what: a leaver's month nobody could sign, one person signing twice, a leader approving their own overtime, a route that paid without approval. The grades hold rather than rise, because the larger correctness work, the overtime model, is decided but not built; S148 starts it on settled ground.

## S146 re-grade (2026-10-26) — payroll correctness: every paid month provable, one flex balance per month
````

---

## Beyond the owed list (optional; each with a lean)

### X1 — `docs/sprints/INDEX.md`: the S147 row · lean: apply at C-1, so Step 7a reads it

**Why:** the sprint index has no S147 row. S145 wrote its row in K; S146 in the close commit. In K it carries the placeholders, filled at C-4.

**Target:** `docs/sprints/INDEX.md` (line 115, the end of the S146 row; the new row follows it).

Old text:

````text
Step 7a: Codex APPROVE at cycles 2 and 4; internal lens Fable cycle 1, then Opus cycles 2–4 (owner direction) |
````

New text:

````text
Step 7a: Codex APPROVE at cycles 2 and 4; internal lens Fable cycle 1, then Opus cycles 2–4 (owner direction) |
| [Sprint 147](SPRINT-147.md) | **Confirmed defects fixed, and the overtime/flex model decided before it is built (QUAL-229, QUAL-230, QUAL-231, QUAL-235, SEC-053, SEC-054; ADR-043 ACCEPTED).** What shipped:<br>• A leaver's final month can be approved and paid: the manager has 5 calendar days after the last working day, then HR, which can act on it from its follow-up page.<br>• A person who has left can no longer change their own record through any write route (84 counted in the three hosts).<br>• A sick day no longer costs flex in payroll; særlige feriedage can be registered in Skema.<br>• Strict four-eyes: whoever sent a month can neither approve nor reject it, and payroll checks again; a leader cannot decide their own overtime.<br>• The two raw payroll routes are retired, so every export starts from an approved month.<br>• Payroll refuses a month whose opening balance rests on a month corrected later (QUAL-229).<br>Decided: all twelve of ADR-043's questions, each on the recommended option; S148 builds the model in stages. Found and fixed: a real Skema race behind a test that looked flaky (QUAL-241). CI-W2 went red on eight test-side faults, fixed by TASK-14719. Step 7a: <7a outcome> | complete | 2026-10-27 → <close date> | 1715u + 170 demoseed + **<C-2 Regression n>r** (CI, all executed) + 7s + **1089fe** | ✅ CI GREEN `<C-2 run id>` (all 7 jobs, C-2 on K `<K sha>`; the close commit after it is docs only). Earlier checkpoints: CI-W1 `40839583193` green; CI-W2 `41060488976` red 8/2353 (test-side faults, TASK-14719), CI-W2b `41310143330` green; CI-W3 `41335270197`, CI-W3b `41641201066`, CI-W4 `41708962373`, CI-W4b `41825042200`, all green |
````

### X2 — Tender analysis: the rows that still say særlige feriedage cannot be registered · lean: apply

**Why:** five rows carry the verification's QUAL-235 note, now untrue; their ratings stay (each is Partly for other gaps). Each gets a dated pointer.

**Target:** `docs/references/tender-requirements-analysis.md` (lines 141, 170, 148, 175, 255). Five parts.

**X2a — EM A1 (a) (line 141).** Old text:

````text
*omsorgsorlov*, §56.<br>**Verification:** Add: *særlige feriedage* cannot be registered, because `SPECIAL_HOLIDAY_ALLOWANCE` is not in Skema's catalogue (`SkemaEndpoints.cs:41-57`; QUAL-235). | — |
````

New text:

````text
*omsorgsorlov*, §56.<br>**Verification:** Add: *særlige feriedage* cannot be registered, because `SPECIAL_HOLIDAY_ALLOWANCE` is not in Skema's catalogue (`SkemaEndpoints.cs:41-57`; QUAL-235). *(S147: they can now; QUAL-235 CLOSED, see EM A9 (b).)* | — |
````

**X2b — VD 8 (a) (line 170).** Old text:

````text
| As EM A1 (a).<br>**Verification:** Add: *særlige feriedage* cannot be registered, because `SPECIAL_HOLIDAY_ALLOWANCE` is not in Skema's catalogue (`SkemaEndpoints.cs:41-57`; QUAL-235). | — |
````

New text:

````text
| As EM A1 (a).<br>**Verification:** Add: *særlige feriedage* cannot be registered, because `SPECIAL_HOLIDAY_ALLOWANCE` is not in Skema's catalogue (`SkemaEndpoints.cs:41-57`; QUAL-235). *(S147: they can now; QUAL-235 CLOSED, see EM A9 (b).)* | — |
````

**X2c — EM A4 (line 148).** Old text:

````text
because Skema cannot record them (QUAL-235). | QUAL-224 |
````

New text:

````text
because Skema cannot record them (QUAL-235). *(S147: Skema records them now; QUAL-235 CLOSED.)* | QUAL-224 |
````

**X2d — VD 9 (line 175).** Old text:

````text
because Skema cannot record them (QUAL-235). | R2 |
````

New text:

````text
because Skema cannot record them (QUAL-235). *(S147: Skema records them now; QUAL-235 CLOSED.)* | R2 |
````

**X2e — VD 13 (line 255).** Old text:

````text
but no screen can register taking one (QUAL-235). | ADR-030, ADR-033 |
````

New text:

````text
but no screen can register taking one (QUAL-235). *(S147: Skema registers them now; QUAL-235 CLOSED, see EM A9 (b).)* | ADR-030, ADR-033 |
````

### X3 — Tender analysis: the rows that still describe the sick-day flex defect · lean: apply

**Why:** three places describe QUAL-231 as live. The ratings stay (EM A8 is Partly for the overtime model; VD 25 is Not met for the night supplement).

**Target:** `docs/references/tender-requirements-analysis.md` (lines 199, 210, 232). Three parts.

**X3a — EM A8 (line 199).** Old text:

````text
so it lowers paid flex while the Skema screen shows 0.<br>**Verification:**
````

New text:

````text
so it lowers paid flex while the Skema screen shows 0. *(S147: fixed; QUAL-231 CLOSED, TASK-14708, merged `7cc6e65e`, CI-verified at CI-W2b, run `41310143330`.)*<br>**Verification:**
````

**X3b — VD 25 (line 210).** Old text:

````text
Sick days also lose flex credit in payroll (QUAL-231). | — |
````

New text:

````text
Sick days also lose flex credit in payroll (QUAL-231). *(S147: fixed, QUAL-231 CLOSED: a sick day is credited with the day's planned hours. The rating stands: the lost night supplement this row asks for is still not paid.)* | — |
````

**X3c — R2's summary (line 232).** Old text:

````text
so no unsocial-hours supplement is computed for Skema work at all (QUAL-234, Critical).
````

New text:

````text
so no unsocial-hours supplement is computed for Skema work at all (QUAL-234, Critical). *(S147: (1) is fixed, QUAL-231 CLOSED. (2) and QUAL-234 are decided in ADR-043 (D11, D10) and built from S148; QUAL-234's rest check has an interim "not checked" notice.)*
````

### X4 — Tender analysis: the segregation-of-duties rows · lean: apply

**Why:** six places describe SEC-053 and SEC-054 as open. The ratings stay: EM A38 and EM §8.3 are Partly for backup, MFA and log protection; VD 35 is Partly because corrections, pay-affecting configuration and settlement payouts still need no second person.

**Target:** `docs/references/tender-requirements-analysis.md` (lines 412, 376, 403, 454, 56, 280). Six parts.

**X4a — VD 35 (line 412).** Old text:

````text
SEC-054 (the raw GlobalAdmin export, documented but never weighed against segregation of duties). | ROADMAP "Self-approval class + ORG_SCOPE_FALLBACK ruling"; SEC-009 (fixed: self only); ADR-012 |
````

New text:

````text
SEC-054 (the raw GlobalAdmin export, documented but never weighed against segregation of duties).<br>**S147: still Partly; both paths are closed.** Two different people now sign every month: the sender can neither approve nor reject it (SEC-053 CLOSED), and payroll re-checks it on every export, which now always starts from the employee's own approved month (the raw routes retired, SEC-054 CLOSED; `docs/SECURITY.md` § "Segregation of duties"). Still single-signer: corrections (`/recalculate`, GlobalAdmin), pay-affecting configuration (agreement publish, wage-type mappings) and settlement payouts; four-eyes is not configurable. | ROADMAP "Self-approval class + ORG_SCOPE_FALLBACK ruling"; SEC-009 (fixed: self only); SEC-053, SEC-054 (closed S147); ADR-012 |
````

**X4b — EM A38 (line 376).** Old text:

````text
The segregation-of-duties gaps behind this row are registered as SEC-053 and SEC-054. | ROADMAP "Standards and requirements conformance"; ROADMAP "Self-approval class + ORG_SCOPE_FALLBACK ruling"; ADR-034; SEC-048 |
````

New text:

````text
The segregation-of-duties gaps behind this row are registered as SEC-053 and SEC-054. *(S147: both are closed: one person can no longer sign a month twice, and no route exports without an approved month (see VD 35). The other gaps stand.)* | ROADMAP "Standards and requirements conformance"; ROADMAP "Self-approval class + ORG_SCOPE_FALLBACK ruling"; ADR-034; SEC-048 |
````

**X4c — EM §8.3 (line 403).** Old text:

````text
The segregation-of-duties gaps behind this row are registered as SEC-053 and SEC-054. | EM A38; ROADMAP "Standards and requirements conformance" |
````

New text:

````text
The segregation-of-duties gaps behind this row are registered as SEC-053 and SEC-054. *(S147: both are closed; see EM A38 and VD 35.)* | EM A38; ROADMAP "Standards and requirements conformance" |
````

**X4d — R5's summary (line 454).** Old text:

````text
Both paths are confirmed by an independent trace and registered (SEC-053, SEC-054).
````

New text:

````text
Both paths are confirmed by an independent trace and registered (SEC-053, SEC-054). *(S147: both are fixed and closed. Whoever sent a month can neither approve nor reject it, and payroll checks it again; the two routes are retired, so every export starts from an approved month (see VD 35).)*
````

**X4e — "What the picture shows", item 3 (line 56).** Old text:

````text
and because of the segregation-of-duties defects below.
````

New text:

````text
and because of the segregation-of-duties defects below. *(S147: those two defects are fixed; see VD 35.)*
````

**X4f — the payroll baseline paragraph (line 280).** Old text:

````text
The two raw routes `/export` and `/export-period` (`:213`, `:265`, GlobalAdminOnly) skip approval by design and take a single employee's profile.
````

New text:

````text
The two raw routes `/export` and `/export-period` (`:213`, `:265`, GlobalAdminOnly) skip approval by design and take a single employee's profile. *(S147: both retired by TASK-14716, SEC-054 CLOSED; `/calculate-and-export` is now the only route that exports.)*
````

### X5 — Tender analysis: EM A1 (c), "No UI for any of it" · lean: apply

**Why:** HR now has a screen for one on-behalf case, a leaver's final month. The rating stays Partly.

**Target:** `docs/references/tender-requirements-analysis.md` (line 143).

Old text:

````text
No UI for any of it: Skema edits only the logged-in user (`SkemaPage.tsx:211`);
````

New text:

````text
No UI for any of it *(S147: except one case, a leaver's final month, which HR can open read-only, send, approve or reject from its follow-up page from day 6; TASK-14714)*: Skema edits only the logged-in user (`SkemaPage.tsx:211`);
````

### X6 — ROADMAP: the "7203-pin vitest flake" was a real race · lean: apply

**Why:** the backlog line still calls it a flake that "graduates to a finding on recurrence". It did: TASK-14718 found a real race (QUAL-241), now fixed and closed. The precedent for a done row that keeps its provenance is "Fixed clock — ✅ DONE (S140)" in the same section.

**Target:** `ROADMAP.md` (line 569).

Old text:

````text
- **`SkemaPage` 7203-pin vitest flake** — one absorbed CI flake; graduates to a finding on recurrence. [S128 FU-D]
````

New text:

````text
- **`SkemaPage` 7203-pin vitest flake — ✅ RESOLVED (S147): it was a real race, not a flake.** A day panel opened before
  the page had copied the month's data started with no work periods, and an edit there could wipe the day's stored
  times. Registered as QUAL-241 and fixed by TASK-14718 and TASK-14720 (merged `782e2285`, `3ee2a8c9`; the frontend
  job green at CI-W2b and CI-W3); QUAL-241 CLOSED. [S128 FU-D]
````

### X7 — ROADMAP: the self-approval class · lean: apply

**Why:** two members of the recurring self-approval class closed in S147. S147 did not rule on the `ORG_SCOPE_FALLBACK` itself, so the item stays.

**Target:** `ROADMAP.md` (line 204).

Old text:

````text
recurring self-approval defect class. [carried since S125; RES-003]
````

New text:

````text
recurring self-approval defect class. [carried since S125; RES-003] *(S147: two members of the class are closed: SEC-053, the sender who approves (strict four-eyes, owner OQ-1 = (a)), and SEC-056's self part, a leader deciding their own overtime (owner Q-A = (a)). The fallback itself was not ruled on.)*
````

### X8 — The sprint log: the merge record's heading still says "in progress" · lean: apply

**Why:** the table under "Wave 2a — the merge record (in progress)" now holds every merge from wave 2a to the tidy pass.

**Target:** `docs/sprints/SPRINT-147.md` (line 144). A `###` heading does not end the "Task ledger" section for the close guard (`sprint-close-guard.ps1:656` ends it at `#` or `##`), so the gate is unaffected.

Old text:

````text
### Wave 2a — the merge record (in progress)
````

New text:

````text
### Waves 2a to 4b and the tidy pass — the merge record
````

---

## Not drafted here

- **C-5's items:** the `**Test Verified**` line (the plan gives it to C-5, with the close commit's run id, `PLAN-s147.md:429`; "—" at the close does not trip the consecutive-CI-pending gate, which looks for "CI-pending" on that line, `sprint-close-guard.ps1:433-434`), the model-routing register's S147 row (`SPRINT-147.md:444`), the handoff "closed", and teardown.
- **The tidy pass's own `.claude` edits, not done at `9ca60740`:** O-b, the demo skill's `demo_admin` line (`.claude/skills/launch-demo-system/SKILL.md:22` has no SEC-053 note); O-c, the refinement's erratum (no "ReconstructStints" in `.claude/refinements/REFINEMENT-s147-defects-and-model-rulings.md`); O-f and O-g's build-input half (`.claude/plans/s147/S148-BUILD-INPUT.md` has neither S0-2's re-pin list nor the class D stage-0 inputs). They are outside `docs/**`, so not in K. The build input and the refinement are in the planner's scope; the brief limited this draft to one file, so they are not edited. Block 5e lists them.
- **Tender rows whose facts S147 did not change**, D5 and D6 among them (decided, not built). EM A32 (error messages) is also left alone: S147 made several refusals Danish, but the row's gap, "elsewhere the raw backend body (often English JSON) is shown", still holds.
- **The refinement record's duplicate "Artifacts:" line** (`SPRINT-147.md:64`, a stale copy of `:62` that names only cycles 1–2). Historical and harmless.
- **The sprint index's per-sprint tables** (task summary, test progression, effectiveness metrics): S146 added no rows there either.

## Brief contradictions, deviations and open points

**Claims about the existing code (checked; the code governs):**
1. **"QUAL-214's cross-reference (sprint log ~:298: largely closed by TASK-14720 …)".** The line is `SPRINT-147.md:318`, under "In plain words (14718)", and it says "largely closed by **this task**", which is TASK-14718. Its fix-up 1 (`ed884d6f`) added the flush before month navigation (`SkemaPage.tsx:1005-1007`), and the queues are reset when a month loads (`:498-507`). TASK-14720 then bounded the whole flush. Block 8 credits TASK-14718 and names TASK-14720's part.
2. **"The sprint log's O-items row ~:420".** It is `SPRINT-147.md:440`.
3. **O-8 and O-11.** O-8 is fully applied: QUAL-234's half in `3c00fc88`, QUAL-232's at O-10 (`760bc767`). The O-items row never recorded it; Block 5b does. O-11's `SECURITY.md` half is applied (`014eb187`), in § "Segregation of duties", rule 3 (`docs/SECURITY.md:191`), not the § "Flat approval authority" the plan names (`PLAN-s147.md:319`). Its register half is Block 6.
4. **O-12's row list** (plan `:320`; the brief's item 5). EM A33 and VD 33 do not change rating: both stay Partly, for on-screen reopen or edit and deletion. They get dated notes (13o, 13p). EM A9 (b), not on the list, does change: the verification's only reason for its downgrade was QUAL-235, which S147 closed. It is re-rated in 13m and 13n, as the plan's "any row the build moved" requires.
5. **O-10's Blocks 21 and 22 have not drifted.** All six Old texts are unique at `9ca60740`, on the same lines O-10 gave (`security-finding-register.md:216`; `quality-finding-register.md:455`, `:461`). Three deviations from O-10's drafts:
   - 6c's Old text is the whole source cell, so the pre-T9 line citations are marked AS-REGISTERED and the T9 guards added, as SEC-054's closing entry did at O-7.
   - 6b and 6d are in the past tense, with the merge `b2a65fb8` and the CI-W2b run filled in. O-10's 6b condition, "T9 merged", holds.
   - 7a says "is scheduled to close … (not built in S147)" where O-10 said "closes", and glosses R-7.
6. **Line numbers in `TIDY-PASS.md` that moved by `9ca60740`.**
   - 12a's predicate is at `ApprovalPeriodRepository.cs:1955` (not `:1946`).
   - 12b's handler is `Payroll/Program.cs:493-511` (not `:491-503`).
   - 11b's Payroll copy is `:591-601` (not `:589-593`).
   - 21a's card computes the orphan at `ApprovalPeriodRepository.cs:1237` and sets it at `:1248` (not `:1247`).
   - Fable's NOTE 3 lines for `OpfoelgningPage.tsx` are now `:69`, `:113` (it gave `:67`, `:94`, before TIDY-4 merged).
7. **X-6's "rough count: three of them hold `as` casts, and one an explicit type argument"** (`TIDY-PASS.md:161`). Grep at `9ca60740` finds `as` expressions in three files but no explicit type argument on any API call in the four files. QUAL-247 states only what grep shows.
8. **Item 7's claim, "a hand-built HK request can still save `CHILD_SICK_DAY_3`".** The premise is confirmed: the 422 tests `AbsenceClassification.IsKnown` only (`SkemaEndpoints.cs:970-976`), and the agreement list is read only by the month GET (`:2684`). The quota does not stop it, because all three child codes map to one `CHILD_SICK` entitlement (`EntitlementMapping.cs:61-63`). Block 9 says "within HK's quota".
9. **The header Status row (`SPRINT-147.md:5`)** still read "refinement (Steps 1–4)". It was not on the brief's list; Block 1 fixes it.
10. **An observation on the close guard, not a docs item.** The ledger gate matches DONE, CUT, DEFERRED or DROPPED case-insensitively anywhere after the id on the same line (`.claude/hooks/sprint-close-guard.ps1:684`, under PowerShell's case-insensitive `-notmatch`). At `9ca60740` it therefore already accepts TASK-14716 through the prose "O-7 done", and TASK-14718 and TASK-14720 through "dropped-save notices" in TASK-14720's item text. After Block 2 each id passes on its own row, which a simulation of the gate's two regexes over the applied log confirms (25 of 25). The hook itself is outside this draft's scope; the Orchestrator may want a narrower match (the disposition in a cell of its own) at the next hook edit.

**Claims about required behaviour:** none contradicted. Every destination follows the Orchestrator's class D ruling (`SPRINT-147.md:221`, `:229`), and every closing statement follows the owner's rulings as the log records them.

**Needs a ruling (the Orchestrator's; none for the owner):**
- **R-1, EM A1 (e) Partly → Met. Lean: Met.** The sentence asks that one person cannot approve both as employee and as chef, and since SEC-053 that holds at three points.
  - *For:* both legs are refused for the same person, legacy `submitted_by` rows included, and a forced reopen-and-resend never ends approved by the sender (AC-053-3).
  - *Against:* the research picks the lower rating when in doubt (`tender-requirements-analysis.md:476`). Teamoversigt shows no reason when the 403 fires, and overtime has no creator-not-approver rule yet. Neither is in this requirement's sentence, which is why the lean is Met; both are named in 13k.
- **R-2, EM A9 (b) Partly → Met. Lean: Met.** It reverses the verification's only stated reason. If the Orchestrator prefers to keep the verification's mark, 13m and 13n become a note only.
- **R-3, X-6 as a new row (QUAL-247) or a note on QUAL-178. Lean: a new row, as ruled.**
  - *For the row:* it names a concrete list, four files and twelve lines, that QUAL-178 does not.
  - *For the note:* QUAL-178 ("no hooks rules, nothing") already covers the directives' cause, and its fix may be the same change.
  - A cross-reference both ways keeps the choice cheap.
- **R-4, severities.**
  - QUAL-245 at M is the most arguable: duplication alone sits below the floor in the S131 inventory (D2). The alternative is L, which moves it to the appendix beside 23d.
  - QUAL-246 and QUAL-247 at M.
  - 23d at L, in the appendix rather than a row. The alternative is M, reading "a 500 instead of a refusal" as plausibly changing behaviour.
- **R-5, `QUALITY.md`'s grades.** Every grade is held, three with ▲. The planner proposes; the Orchestrator grades.
- **R-6, the scope of X2–X5.** Lean: apply. These rows' ratings did not move, but each still says a closed defect is open, in a document that also carries the S147 re-rating. The alternative is the strict reading of "a row the build did not change is not touched", where "change" means the rating; 13a's status line then tells the reader that unnoted rows describe `67809348`.

## Revision history

| Draft | Date | By | What |
|---|---|---|---|
| 1 | 2026-11-04 | `planner` (Opus 5.5) | First draft at `9ca60740`. Blocks 1–14, 66 owed replacements: the header Status; the ledger's final dispositions with every CI checkpoint's run id, and one DROPPED row for the plan's conditional tasks; the two CI-pending dispositions noted; the C-1, C-2 and Step 7a sections with placeholders; the handoff rows; O-10 Blocks 21 and 22 (no drift; three deviations); QUAL-214 and QUAL-236 notes; QUAL-245–247 and the 23d appendix line; HRP-013; ROADMAP's stage-0 pointer and two backlog lines; O-12 (EM A1 (e) and EM A9 (b) Partly → Met; EM A33, VD 33 noted; the defects and R1's summary); `QUALITY.md`'s S147 section (O-10 X6). X1–X8, 19 optional replacements. Every Old text counted by script (85 of 85 unique in blob and working copy, no overlaps); the close guard's ledger gate simulated on the applied log (25 of 25 ids). Ten code-claim reports and observations; six Orchestrator rulings proposed (R-1..R-6) |
