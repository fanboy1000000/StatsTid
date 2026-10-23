# Sprint 146 — payroll correctness (QUAL-205, QUAL-188, QUAL-198) and the overtime/flex decision track

| | |
|---|---|
| **Status** | OPEN — plan READY at Step 0b (2026-10-23); wave 1 not yet dispatched |
| **Theme** | Every paid month becomes provable: its proof is written in one transaction through the outbox, or payroll refuses (QUAL-205). Flex gets one recorded balance per paid month, chained, serialized, corrected and visible (QUAL-188). QUAL-201 closes by construction. Part-timers stop seeing a full-time norm (QUAL-198). Three S145 tidy-ups. Beside the code, the Orchestrator's decision track (D1–D7) turns the verified flex/overtime research into tracked docs, register rows, a re-verification and a PROPOSED model ADR, so S147 opens the model program on verified ground |
| **Predecessor** | S145: close `13a40e68` (CI green `39571040922`) · C-9 docs-only `6e3ba7ef` · post-close `6c29efc2` … `0f02b484` (the close guard's S145 exception deleted, owner R-104; T26 across eight review cycles, R-141..R-144) · CI green `39755579330` on `0f02b484`, all seven jobs |
| **Base commit (Step 7a)** | **`13a40e68`**, the S145 close commit, not HEAD (`docs/WORKFLOW.md` step 7a), so the post-close hook commits fall inside S146's Step-7a diff |
| **Orchestrator model** | Opus 5.5 seat throughout (client 2.1.301), never switched (owner ruling 2026-09-29). Drafting: `planner` (Opus 5.5). Judgment: `adjudicator` and `reviewer` (Fable 5.1). Research: `general-purpose` (Opus 5.5) with web search, plus an independent verifier. Implementation by role name. External lens: Codex. The session restarted once on 2026-10-23 (machine reboot) after the owner picked the theme; the refinement was restarted from the same brief, with nothing lost. The planner was interrupted once by an API session limit and resumed |
| **Plan** | `.claude/plans/PLAN-s146.md` draft 2.1 and 15 briefs in `.claude/plans/s146/`, **READY at Step 0b on both lenses** (record below) |
| **Refinement** | `.claude/refinements/REFINEMENT-s146-payroll-correctness.md` rev 3.2, READY: three Step-4 cycles on both lenses, an owner-decided fold (no cycle 4) and an Orchestrator-found fold (R-21..R-23). Record below |
| **Owner rulings in force** | **S146 OQ-1 = (iii)** (2026-10-23): the certain fixes plus decision track D · **OQ-2 = (a)**: S145's Q2 = A, Q3 = F1 and Q7 (b) superseded in principle by the verified sources, still in force in code until the model program replaces them; no new work on F1 machinery; QUAL-209/210/212/213 frozen · **OQ-3 = (b)**: flex by default, overtime/merarbejde only for ordered or accepted hours, never both; the program's detail forks are S147's · **OQ-4**: informed, veto not used: an employed zero-line month on the calculating route writes its record (R-3) · **OQ-5 = (b)**: a leader's team overview keeps flex withheld until the month is submitted (the strict reading of the S124 ruling of 2026-07-30) · **no fourth refinement cycle**: the cycle-3 folds are verified at Step 0b · **S145 Q-1** (2026-10-14): QUAL-198 in S146 · standing: reseed, don't migrate; review cap 5 per lens |
| **Entropy scan (Step 0a)** | 2026-10-23 at `0f02b484`. Tree clean except the untracked `.claude/plans/PLAN-s145.md` and `.claude/plans/s145/` (**DEBT**, settled at this open commit: the S145 plan and its `TASK-*.md` briefs are committed, not the evidence logs). 0 extra worktrees; no local branches but master; remote branches dependabot/actions only. No `FindFirst("scopes")`; no hard-coded localhost; no PAT-005 direct rule calls. Endpoint authorization CLEAN (only login and `/health` are anonymous). KB paths: 42 cited, 2 benign non-resolving. Orphans (`FlexBalanceCard`, `useFlexBalance`, `/api/flex-balance`, `flex_balance_snapshots`, `FlexBalanceUpdated`, `BuildPlanForLegacyCallersAsync`) are all in S146 scope. **DRIFT:** `danish-agreements.md` "Weekly Settlement Rules" asserts "the sources are silent", which the verified research contradicts; fixed first by D1–D3. `check_docs.py` is not runnable locally (no Python); the CI docs job is green. Grades: Payroll A− stands; the close re-grade states the contradicted weekly premise. **Orchestrator finding during the scan:** the refinement's R-17 would have added a second atomic-event path beside ADR-018's outbox; the adjudicator upheld it (R-21..R-23) |
| **Baseline** | S145 final (`SPRINT-145.md:6`): build 0 errors / 145 warnings · CA2100 115 · Unit 1512 · DemoSeed 170 · Regression 2150 in CI / non-Docker 128 locally · Smoke 7 · vitest 1005. Re-measured at the first gate, never carried forward |
| **Pre-merge gate G-0** | SATISFIED: `39755579330` green on `0f02b484` |

## Plain-language summary

**What was asked.** The owner picked "payroll correctness" for S146 (2026-10-22): the four registered findings that hold the Payroll grade at A−. QUAL-189 is the same worked hours counted both as overtime and in flex. QUAL-188 is a month's opening flex balance being whatever the caller sends. QUAL-201 is a correction racing a reopen of the month before. QUAL-205 is a correction proceeding when its audit proof could not be saved.

**What the refinement found first: the overtime model itself is wrong.** QUAL-189 needed a domain answer the agreement reference did not source, so a research agent read the Danish state rules, and an independent verifier then re-fetched every primary source to try to break it. The main conclusions held:
- For HK and PROSA the norm is measured over **a calendar month** (state working-time agreement § 4 stk. 2), not a week.
- Overtime exists only for **ordered or necessary** work, at a flat **+50%**. Every other extra hour is **flex**, and flex is a separate account.
- AC has **no overtime**; extra work (merarbejde) is a claim the employer decides on.

StatsTid pays overtime automatically on every hour above a *weekly* norm, in two tiers no source supports. The rule S145 built on said the sources were "silent"; the register row behind it cites a paragraph about form of employment. So QUAL-189 is a symptom, and the model needs rebuilding.

**What the owner decided (2026-10-23, one question at a time).**
1. S146 builds the fixes that do not depend on the model, and designs the rebuild properly: verify the agreement cells, draft the decision, correct the docs. The rebuild itself starts in S147 (OQ-1).
2. S145's weekly rulings are reopened provisionally. They stay in code until replaced, and no new work goes onto that machinery (OQ-2).
3. The rebuild's direction: flex by default, overtime only when ordered (OQ-3).
4. An empty employed month becomes corrections-only (OQ-4, veto not used).
5. A leader's team overview keeps flex hidden until the month is submitted (OQ-5).

**What S146 builds.**
- **QUAL-205.** A calculation's proof is written in one transaction through the project's existing outbox (ADR-018), or payroll refuses and writes nothing.
- **QUAL-188.** Each paid month records its opening and closing flex balance and where the opening came from; the next month opens from the previous month's recorded closing through one function that covers every case (first paid month, raw history, re-runs, rehires); all payroll writes for one employee queue behind one lock.
- **The flex screens.** Today they show 0 for everyone; they will show the real settled balance, labelled with its month.
- **QUAL-198:** part-timers see their own norm.
- **The S145 leftovers.**
- **QUAL-201 is not built.** Once months chain from recorded balances, the race cannot happen.

## Refinement record (Steps 1–4)

| Step | Outcome |
|---|---|
| Research | First pass: `.claude/refinements/RESEARCH-s146-flex-vs-overtime.md`. Independent adversarial verification: `…-VERIFICATION.md` (V1–V12; main conclusions CONFIRMED; it corrected the first pass on the HK ladders, the part-timer renegotiation (4 Feb 2026 award, OK26 item 28), the arbitration attribution and the softness of forfeiture; it found the citation root cause). Both are to be tracked as a dossier in D1 |
| Rev 1 | Planner draft. Cycle 1: Codex NOT-READY (2 B: the first-month bootstrap was not serialized; the empty month contradicted ADR-034 D3), Fable reviewer NOT-READY (B1: the first-month rule was keyed on operator behaviour) |
| Adjudication c1 | R-1..R-13; owner questions OQ-1..OQ-4, all ruled as leaned |
| Rev 2 | Cycle 2: both NOT-READY, converging on the opening-balance derivation (raw-route history dead-ends; the F1 predicate was cited for employment; provenance was missing from the under-lock check) |
| Adjudication c2 | R-14..R-20: `DeriveOpening` as one total function, the ledger lock on all four routes, the triple no-op, the Payroll-local manifest seam |
| Rev 3 | Cycle 3: **Codex READY, reviewer READY, no BLOCKER**. Owner: OQ-5 = (b); fold the warnings without a cycle 4 |
| Rev 3.1 / 3.2 | The cycle-3 fold; then R-21..R-23 (the Orchestrator's Step-0a finding: the manifest travels through the ADR-018 outbox, with no new Infrastructure method); verification venue Step 0b |

Artifacts: `.claude/reviews/REFINEMENT-s146-{codex,reviewer}-c{1,2,3}.md`, `REFINEMENT-s146-adjudication-c1.md`, `-c2.md`, `-c3-outbox.md`.

## Step 0b — plan review (both lenses)

| Cycle | External (Codex) | Internal (Fable reviewer) | Adjudication |
|---|---|---|---|
| 1 (draft 1) | NOT-READY. 2 B: the Regression harnesses cannot enqueue (no `OutboxServiceContext`; no `outbox_events` in the harness DDL); four facts rebuild or replay from `events` with no publisher. 4 W | NOT-READY. B1 (the same harness hole, plus the facts beyond R-22's three); W1–W9 (incl. a census of 22 construction files, not 19; an atomicity fact that passed today; a CI checkpoint after wave 2c) | R-24..R-34: new TASK-14609 (harness outbox readiness, wave 1); the census rule; the F/B/R label rule; the refusal predicate; `route` derived; the windows read covers P; scoped grep gates; KB entries per task; CI-W1, CI-W2 and C-2; wave 3 stacked |
| 2 (draft 2) | NOT-READY. B: census hits outside the authorized lists would force known STOPs. W: the in-tx employment resolver has only a per-day read | **READY**. W-A, W-B as brief edits; the label question answered N | R-35..R-39: classify every census hit (authorized / expected-unchanged / generated); the in-tx range overload in TASK-14605 as a declared cross-domain change; implicit employment on resolver-less fixtures; the label N. Venue: a scoped Codex cycle 3 |
| 3 (draft 2.1, scoped to R-35/R-36) | NOT-READY: R-36 RESOLVED (the overload is feasible through `ReadWindowRowAsync`; `EmploymentWindowResolver` is the only implementer); the nits RESOLVED; R-35 PARTIAL on three unclassified census hits (`employeeTypedWire.test.ts:200, :236`, `EmploymentWindowPayrollTests.cs:539, :600`, `ManifestProjectionRebuildTests.cs:11, :21`) | (stays READY) | Factual, within R-35: the Orchestrator classified the three hits in `TASK-14608.md:53`, `TASK-14605-T.md:29` and `TASK-14609.md:32` |
| 4 (confirmation, one item) | **READY**: every census hit in the three briefs classified | — | — |

**Step 0b outcome: READY on both lenses** (internal at cycle 2, external at cycle 4). Artifacts: `.claude/reviews/PLAN-s146-codex-c{1,2,3,4}.md`, `PLAN-s146-reviewer-c{1,2}.md`, `PLAN-s146-adjudication-c{1,2}.md`.

**An adjudicator self-correction (R-34).** At refinement cycle 1, the adjudicator's Part 4 listed `ApprovalEndpoints.cs:1775` as naming a test file wrongly. The plan-cycle-1 reviewer showed `PriorMonthExportRaceHostTests` is a real class (`PriorMonthExportRaceTests.cs:368`), so the comment is correct and no fold-in was made.

## Task ledger

Transcribed from the plan (draft 2.1). Dispositions are updated at each wave gate; the close guard requires every task to have one.

| Task | Disposition | Note |
|---|---|---|
| TASK-14600 | PLANNED | wave 1 · `payroll-integration` · E2: delete `BuildPlanForLegacyCallersAsync` |
| TASK-14601 | PLANNED | wave 1 · `data-model` · the schema (balances, provenance, CHECK, `flex_balance_snapshots` dropped) and the optional shapes; `init.sql` Orchestrator-approved in the plan |
| TASK-14601-T | PLANNED | wave 1, paired · `test-qa` · the harness copy, the R51 guard row, the schema facts |
| TASK-14602 | PLANNED | wave 1 · `backend-infrastructure` · QUAL-198; the injectable profile read (R-31) |
| TASK-14602-T | PLANNED | wave 1, paired · `test-qa` · part-time pins; the fault-isolation pin (N) |
| TASK-14603 | PLANNED | wave 1 · `test-qa` · E3: `JsonAssert.RequiredMember` in the five helpers |
| TASK-14609 | PLANNED | wave 1, after 14601-T · `test-qa` · harness outbox readiness (R-24) |
| TASK-14604 | PLANNED | wave 2a · `payroll-integration` · QUAL-205 through the outbox; `ISegmentManifestStore`; the refusal predicate |
| TASK-14604-T | PLANNED | wave 2a, paired · `test-qa` · in-memory store; 23-file census; AC-A1..A3 |
| TASK-14605 | PLANNED | wave 2b · `payroll-integration` (cross-domain: two audit mappers; the in-tx range overload, R-36) · QUAL-188 C1–C4 |
| TASK-14605-T | PLANNED | wave 2b, paired · `test-qa` · the site migration under the census rule |
| TASK-14606 | PLANNED | wave 2c · `test-qa` · the QUAL-188 pins |
| TASK-14607 | PLANNED | wave 3, stacked · `backend-infrastructure` · C5 backend; OQ-5; `flexBalanceState`; route, response and event deleted |
| TASK-14607-T | PLANNED | wave 3, stacked, paired · `test-qa` · AC-C9 incl. OQ-5 |
| TASK-14608 | PLANNED | wave 3, stacked · `ux` · C5 frontend |

Orchestrator items, not tasks: T7 (the hook comment, with its own Step 7a), D1–D7, O-1..O-7, CI-W1, CI-W2, C-1..C-5.

## Wave 1 — Orchestrator items (T7, D1–D3)

- **T7 (E1), the hook comment: DONE at `68e4ca01`.** T26's comment now matches its check:
  - the floor compare is "`-ne` in any letter case; a typographic dash, `-cne`, `-ine` or any other form fails";
  - the "does not see" list names the right-hand side of the one `$reviewedBy =`;
  - R-143 joins the provenance line.

  *Probe before the claim:* `Test-FloorBlock` on the live hook gives `ok` for `-ne`, `-NE` and `-Ne`, and `floor-compare` for `-cne`, `-ine`, an en dash, an em dash and `-gt`. The harness shows **24/24**.

  *Its own Step 7a cycle (post-Step-7a coverage for hook files):* Codex **APPROVE** (three confirming NOTEs) and the Fable reviewer **APPROVED**, no BLOCKER and no WARNING, with three record-only NOTEs:
  1. `-in`/`-contains` in the compare position fail as `model-list`, not `floor-compare` (same outcome);
  2. the right-hand side's tokens still pass the general checks, so the comment under-claims (the safe direction);
  3. two lines overrun the wrap width.

  The owner's standing preference applies: no further cycle on wording. Artifacts: `.claude/reviews/S146-T7-step7a-{codex,reviewer}.md`.
- **D1: DONE.** `docs/references/overtime-flex-model-research.md` holds the verified dossier. The verification governs as Part 1, with the first pass verbatim as Part 2. A CLAUDE.md dossier row is added.
- **D2: DONE.** QUAL-215..QUAL-225 are registered, root cause first:
  - QUAL-215 — the source register's load-bearing citations are unreliable;
  - QUAL-216 — the monthly norm period;
  - QUAL-217 — automatic overtime;
  - QUAL-218 — the 37/40 h tiers;
  - QUAL-219 — payout versus forfeiture;
  - QUAL-220 — AC merarbejde;
  - QUAL-221 — the compensation model;
  - QUAL-222 — holidays;
  - QUAL-223 — the watch row;
  - QUAL-224 — the flex screens;
  - QUAL-225 — the explicit opening balance.

  QUAL-189 is widened to point at QUAL-216/217 as its root cause. Drift corrected: QUAL-205's false "surfaces a degraded state to the HTTP caller", and the QUAL-188 and QUAL-189 line numbers. ADR-013 gains a footnote that its items 1–2 do not hold today; the amendment itself lands in D7.
- **D3: DONE.** The `disputed?` definition is amended first (R-19: a party dispute **or** a contradiction by a verified primary source). Nine rows are set `disputed? = true` with a dated note: SR-AC-OK24-020, SR-HK-OK24-001, SR-AC-OK24-005/007/021, SR-HK-OK24-006/007, SR-HK-OK24-020 and SR-PROSA-OK24-001. `confidence_level` is untouched; D4 overwrites both fields. `danish-agreements.md` gains dated notes on Overtime Thresholds, Weekly Settlement Rules (recording OQ-2) and Compensation Model. This settles the Step-0a DRIFT.

## Handoff block (written at every gate — the state lives here, not in the conversation)

| | |
|---|---|
| **As of** | 2026-10-23, master `0f02b484` + the S146 open commit |
| **Phase** | wave 1 running; T7 DONE (`68e4ca01`, Step 7a APPROVED on both lenses); D1–D3 DONE |
| **Tasks** | 14600 (`payroll-integration`), 14601 (`data-model`), 14602 (`backend-infrastructure`) and 14603 (`test-qa`) dispatched 2026-10-23 in worktrees, awaiting reports. Next on return: 14601-T and 14602-T on their branches; 14609 after 14601-T merges |
| **Track D** | D1, D2, D3 DONE; D4 not started; D5–D7 later |
| **Pending gates** | wave-1 dispatch |
| **CI** | G-0 `39755579330` · CI-W1 — · CI-W2 — · C-2 — · close — |
| **Open rulings** | none |
| **Next action** | Dispatch wave 1: 14600, 14601, 14602 and 14603 in worktrees; 14601-T and 14602-T on return; 14609 after 14601-T merges. Start D1–D3 and T7 |
