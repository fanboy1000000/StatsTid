# Sprint 146 — payroll correctness (QUAL-205, QUAL-188, QUAL-198) and the overtime/flex decision track

| | |
|---|---|
| **Status** | OPEN — plan READY at Step 0b (2026-10-23); wave 1 not yet dispatched |
| **Theme** | Every paid month becomes provable: its proof is written in one transaction through the outbox, or payroll refuses (QUAL-205). Flex gets one recorded balance per paid month, chained, serialized, corrected and visible (QUAL-188). QUAL-201 closes by construction (a bounded residual remains for months at or before the ledger start, R-57). Part-timers stop seeing a full-time norm (QUAL-198). Three S145 tidy-ups. Beside the code, the Orchestrator's decision track (D1–D7) turns the verified flex/overtime research into tracked docs, register rows, a re-verification and a PROPOSED model ADR, so S147 opens the model program on verified ground |
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
- **QUAL-201 is not built.** Once months chain from recorded balances, the race cannot happen for any month after the ledger start. A bounded residual remains for the ledger-start month and any month before it (Step 7a, R-57).

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
| TASK-14600 | DONE (merged `92ecaa13`) | wave 1 · `payroll-integration` · E2: delete `BuildPlanForLegacyCallersAsync` |
| TASK-14601 | DONE (merged `cda60da5`) | wave 1 · `data-model` · the schema (balances, provenance, CHECK, `flex_balance_snapshots` dropped) and the optional shapes; `init.sql` Orchestrator-approved in the plan |
| TASK-14601-T | DONE (merged `cda60da5`, fix-up `c67881f9`) | wave 1, paired · `test-qa` · the harness copy, the R51 guard row, the schema facts |
| TASK-14602 | DONE (merged `b104c4d7`) | wave 1 · `backend-infrastructure` · QUAL-198; the injectable profile read (R-31) |
| TASK-14602-T | DONE (merged `b104c4d7`) | wave 1, paired · `test-qa` · part-time pins; the fault-isolation pin (N) |
| TASK-14603 | DONE (merged `a173ace3`, follow-up `900df51f`) | wave 1 · `test-qa` · E3: `JsonAssert.RequiredMember` in the five helpers |
| TASK-14609 | DONE (merged `0a291582`, fix-up `ac56c72e`) | wave 1, after 14601-T · `test-qa` · harness outbox readiness (R-24) |
| TASK-14604 | DONE (merged `384484cb`) | wave 2a · `payroll-integration` · QUAL-205 through the outbox; `ISegmentManifestStore`; the refusal predicate |
| TASK-14604-T | DONE (merged `384484cb`) | wave 2a, paired · `test-qa` · in-memory store; 23-file census; AC-A1..A3 |
| TASK-14605 | DONE (merged `8f366802`) | wave 2b · `payroll-integration` (cross-domain: two audit mappers; the in-tx range overload, R-36) · QUAL-188 C1–C4; 5a fix-ups R-47/R-48/R-50/R-51 |
| TASK-14605-T | DONE (merged `8f366802`) | wave 2b, paired · `test-qa` · the site migration under the census rule; H14 re-seed |
| TASK-14606 | DONE (merged `a9a677f1`) | wave 2c · `test-qa` · the QUAL-188 pins; M01–M26 and N1–N3 mutations killed; Docker facts at CI-W2 |
| TASK-14607 | DONE (merged with the stack, `88eb3da6`) | wave 3, stacked · `backend-infrastructure` · C5 backend; OQ-5; `flexBalanceState`; route, response and event deleted |
| TASK-14607-T | DONE (merged with the stack, `88eb3da6`) | wave 3, stacked, paired · `test-qa` · AC-C9 incl. OQ-5 |
| TASK-14608 | DONE (merged with the stack, `88eb3da6`) | wave 3, stacked · `ux` · C5 frontend |
| TASK-14610 | DONE (merged `e395c4ec`) | Step 7a fix · `payroll-integration` (cross-domain: the event member, the mapper key) · R-54, R-56, R-61(a) |
| TASK-14610-T | DONE (merged `e395c4ec`) | paired · `test-qa` · R-55's pins |

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

## Wave 1 — tasks (merge checklist)

| Task | Branch @ sha | Build | 5α | 5a Fable | 5a Codex | Merged |
|---|---|---|---|---|---|---|
| TASK-14600 (`payroll-integration`) | `worktree-agent-a1d308c8b658cfbee` @ `370d10cb` | 0 errors / 145 warnings; Unit 1512, DemoSeed 170, Regression non-Docker 128 | PASS | APPROVED-WITH-WARNINGS | APPROVE | `92ecaa13` |
| TASK-14601 + 14601-T (`data-model`, `test-qa`) | `worktree-agent-a84e58f3b84c8b514` @ `7af3625f`, `dece4112`, fix-up `c67881f9` | 0 / 145; 1512 / 170 / 128 | PASS | APPROVED-WITH-WARNINGS → fix-up APPROVED (no artifact; see note under Step 7a) | APPROVE-WITH-WARNINGS → fix-up APPROVE | `cda60da5` |
| TASK-14602 + 14602-T (`backend-infrastructure`, `test-qa`) | `worktree-agent-a1d9823947d197303` @ `3505b620`, `2429016b` | 0 / 144; 1512 / 170 / 128 | PASS | APPROVED | — (display only) | `b104c4d7` |
| TASK-14603 (`test-qa`) | `worktree-agent-aead3545374bb4a42` @ `41e7992f`, follow-up `900df51f` | 0 errors; 1512 / — / 130 | PASS (both commits) | APPROVED, follow-up APPROVED | — (tests only) | `a173ace3` |
| TASK-14609 (`test-qa`) | `worktree-agent-aa258d3371368d250` @ `bb9e733a`, fix-up `ac56c72e` | 0 / 144; 1512 / 170 / 130 | PASS | APPROVED-WITH-WARNINGS → fix-up APPROVED | — (tests only) | `0a291582` |

**CI-W1 (2026-10-23): GREEN** on `0a291582`, run `39992507686`, all seven jobs (build-and-test incl. every Docker-gated fact, smoke, e2e, docs incl. the O-3 hand-sync, frontend, gitleaks, lizard). Wave 1's Docker facts are **CI-verified** by that run: 14600's `ProfileBoundaryHydrationTests`; 14601-T's `PayrollExportBalancesSchemaTests` (incl. `Segment_OnPreS146Database_DropsSnapshotsAndKeepsRowsAllNull`) and its guard row; 14602-T's `Summary_PartTime0811_NormExpected132`, `TeamOverview_PartTime0811_NormExpected132`, `TeamOverview_ProfileReadFails_FallsBackToFullTime_No500`; 14603's 16 host facts through the eight helpers; 14609's guard row, `PayrollOutboxDrain_EnqueuedPayrollRow_DrainedToEvents_SameEventId` and the four drained census facts. Draft PR #39 closed unmerged; branch `s146-ci-w1` deleted.

**O-1 on master `bc4ad0d7`** (after 14600, 14601, 14602 and 14603 merged): build 0 errors / **145** warnings (at the ceiling) · CA2100 **115** · Unit **1512** · DemoSeed **170** · Regression non-Docker **130** (+2, 14603's `JsonAssertTests`). **O-2:** no drift in `openapi.json` or `api-types.ts` (QUAL-198 changed values only). **O-3:** `db-schema.md` hand-synced at `bc4ad0d7`, transcribed in the generator's format and cross-checked against the merged `init.sql`; CI's `check_docs.py --check` arbitrates at CI-W1.

**Findings and decisions at the wave-1 merges:**
- **TASK-14600, declared deviation accepted:** the wrapper's 71-line doc block documented planning itself (boundary sources, ADR-040 D1's off-by-one rule, null-resolver byte-identity, D9c). Only the wrapper-specific parts were deleted; the rest was merged into `BuildPlanWithHistoriesAsync`'s summary. Both lenses agreed this is the better reading of the contract.
  - Fable **W2**, the lost "tests reach this method by name" sentence, and **W1**, the planless `CalculateAsync` shim whose banner and `[Obsolete]` text still name a production caller it lost in S134, are **carried into TASK-14604** as text-only edits. 14604 is the next task to touch the same file, under full dual-lens review.
  - The shims themselves are test-only; that is recorded for a later tidy, below the register's Medium floor.
- **TASK-14601, the frozen inputs (the owner's audit question, answered):** the frozen-inputs column `plan_inputs_jsonb` is written from the event serialization (`EventSerializer`, `WhenWritingNull`) and omits the null `OpeningFlexProvenance` member, so bytes are unchanged for every current writer, since none sets it yet. No hash or fingerprint covers that JSON. *(The implementing agent's report said the opposite; the Fable reviewer and Codex both showed it was wrong, and this sentence replaces it.)*
- **TASK-14601-T fix-up:** both lenses found test gaps. Fable W2: the sixth partial shape `(false, true, true)`, without which a CHECK that lost its `opening_balance IS NOT NULL` term would pass. Codex: the snapshots-dropped fact started from a schema where the table was already absent. The fix-up adds the shape and `Segment_OnPreS146Database_DropsSnapshotsAndKeepsRowsAllNull`, which restores the pre-S146 shape and runs the segment. Both lenses verified that removing the DROP TABLE, an ADD COLUMN or the ADD CONSTRAINT now fails a fact.
  - Record-only notes: the `outbox_messages` line-cite at `PayrollExportRecordsTestSchema.cs:66` is stale; "upgrade half" in a fact label could read as a supported upgrade path (pre-S146 databases are reseeded).
- **TASK-14601-T renamed the guard theory** to `HarnessDdl_Copies_MatchInitSqlSchema`; QUAL-195 updated at `bc4ad0d7`.
- **TASK-14602:** five declared deviations accepted:
  1. a comment correction;
  2. `DayNorm.IsWeekday` in both loops;
  3. an `ILoggerFactory` parameter;
  4. a cancellation filter on the catch;
  5. a local log-category const.

  The reviewer's notes are routed:
  - **(1) to the register:** a part-timer's hours above their own norm now show as "overtime" on the screens, where the agreements call them merarbejde (added to QUAL-217's row);
  - **(3) to the register:** the two screens read the fraction differently when an agreement-code row is missing (QUAL-226);
  - **(2) the screens' different weekly-norm anchors, and (4) the month-end fraction versus the engine's per-day sum:** to the S147 program's input list (QUAL-226).
- **TASK-14603, scope widened by Orchestrator decision** on the reviewer's recommendation: three more refusal-body helpers with the same crash risk, `AssertRulesUnavailable`, `AssertEntriesNotAccepted` and `AssertPriorMonthNotApproved` (follow-up `900df51f`), so the whole class is closed rather than left for 14605-T's census. The N-fact's RED uses the literal mutation in `JsonAssert.cs`: `if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var member))` becomes `if (true)`, and `return member;` becomes `return root.GetProperty(name);`. Result: `Failed: 1, Passed: 1`, actual `KeyNotFoundException` against expected `XunitException`. The reviewer re-ran it outside the repository after the dead `IsNotType` line was removed.


## Wave 2a — TASK-14604 + 14604-T (QUAL-205)

| Task | Branch @ sha | Build | 5α | 5a Fable | 5a Codex | Merged |
|---|---|---|---|---|---|---|
| TASK-14604 + 14604-T (`payroll-integration`, `test-qa`) | `worktree-agent-a25a64ba2d5d72f4e` @ `8a818499`, `f9ab1043`, `2c2fb456` | 0 / 144; Unit 1518, DemoSeed 170, Regression non-Docker 130 | PASS | APPROVED-WITH-WARNINGS | APPROVE-WITH-WARNINGS | `384484cb` |

**O-1 on master after the merge:** 0 errors / 145 warnings · Unit **1518** (+6, the refusal facts) · DemoSeed 170 · Regression non-Docker 130 · the AC-A3 grep gate (`EventOnly|ProjectionOnly|AuditStateItemKey` over `src tests frontend/src`) is empty.

**What changed for the product.** Before, a payroll month could be exported, or a correction written, even when the record proving how it was calculated (the manifest) had failed to save, and the "degraded" flag meant to say so was read by nobody. Now the manifest, its projection row and the legacy summary event are written in one transaction through the project's outbox (ADR-018), or both calculating routes refuse with 503 `payroll-manifest-not-persisted` before any payroll write.

**Decisions at the merge:**
- **`StampAuditContext` stamps only a committed manifest.** This is the brief's requirement. The S145 comment claimed the refused 422 path stamped `Guid.Empty`; it actually stamped a dangling `plan.ManifestId`. This is now pinned by a Unit fact with a mutation and a control.
- **Two scenario-named tests renamed**, so the grep gate holds: `…_ProjectionOnly_StillReplays` → `…_ProjectionRowWithoutEvent_StillReplays`. Annotated in `SPRINT-145.md` BC-1/BC-2 (Fable W2).
- **`FrozenInputsSlotTests.cs:61-68` scope extension ratified** (Fable NOTE 1): without the drain, that fact would trip for the wrong reason.
- **Carried to TASK-14605**, which edits the same file next:
  - Codex W1: the "did not commit" diagnostic becomes "commit not confirmed", because a lost commit acknowledgement can leave all three records committed; payroll still refuses safely.
  - TASK-14604's doc note that `ReplayRefusedException.cs` says "thrown only by ReplayAsync".
- **Known test-fake gap, recorded:** Codex W2, the in-memory store ignores cancellation and overwrites a duplicate manifest id, where the Postgres store propagates cancellation and keeps the existing row. The refusal facts stay meaningful.
- **D7 part 1 (R-23), with the merge:**
  - ADR-018 D6 names Payroll's real streams (`segment-manifest-{manifestId}`, `period-calc-{employeeId}-{periodStart}`);
  - an ADR-016 D10 footnote (the manifest reaches `events` through the outbox);
  - the rebuild script and the `SegmentManifestProjectionRebuilder` comment require a **drained** Payroll outbox before a rebuild, not only quiesced writers (Fable W1: a rebuild run between a commit and the drain would drop that manifest's row);
  - QUAL-205 is marked BUILT. Its Docker facts are CI-pending until CI-W2.

## Wave 2b — TASK-14605 + 14605-T (QUAL-188, the flex ledger)

**What changes for the product.** A month's opening flex balance is no longer a number the caller sends. Each paid month's export record stores what the month opened at, what it closed at, and where the opening came from (`LEDGER_START`, `CHAINED` or `REHIRE`). The next month opens at the previous month's recorded closing. When the chain cannot be followed, payroll refuses with 409 and names the month to fix, instead of quietly starting at 0. One per-employee database lock serializes the export, the correction and the recheck, which is how QUAL-201 closes by construction (R-5).

**TASK-14605 returned** (`worktree-agent-a7d25c1f742b46097` @ `1992e4d5`). The Orchestrator rebuilt every `src/**` project independently: 0 errors, the one documented CS0618, 0 CA2100. The test projects stop compiling (65 errors, all on the retired parameter, so no call silently shifts arguments) until 14605-T migrates them. The names as built are in the agent's report and in the 14605-T and 14606 prompts.

**Decisions at its return:**
- **Deviations accepted:**
  - (1) A sibling `TryReadRecordForUpdateAsync` returns the balances, and the old method delegates to it. Changing the old return type would have broken three un-censused facts.
  - (2) PCS owns the under-lock recheck, and the export calls it through a callback on `FlexLedgerWrite`. This keeps the windows semantics identical to admission.
  - (3) A newly recorded zero-line month skips only the empty post-commit delivery. The Orchestrator checked that it still writes `PayrollExportGenerated` and its audit row inside the transaction (`PayrollExportService.cs:164-165, :250-279, :333-338`).
- **The "M = S without `LEDGER_START`" throw accepted.** It is an invariant throw for a damaged ledger, not a month a user can reach.
- **Brief contradiction "the event names S" — accepted as built.** The correction event carries `FlexProvenance = "UNTRACKED"` with null balances and no S. S (the ledger start month) can be read back from the employee's `LEDGER_START` record, which is never deleted and whose provenance never changes. The event's property doc now lists the fourth value (fix-up `f6d3f780`).
- **Fix-up `f6d3f780`** (doc comments only): the `UNTRACKED` doc above; `ManifestNotPersistedException.cs` and `ISegmentManifestStore.cs` now say "the commit was not confirmed" (Codex W1 from 14604, the two sites outside 14605's first pass).
- **N7, the agent's choice, goes to the 5a reviewers as a named question.** When a correction of month M finds M lost every employed day, the agent leaves M's recorded balances unchanged rather than refusing. The question for the reviewers: does a stale closing on M, with the C4 hint then not naming M+1, keep the ledger true?
- **Consequences of C2, recorded, no check built:**
  - Reviewer c3 N5: M+2 can still be exported for the first time from M+1's stale closing, because case 4a reads only P.
  - A raw month before the ledger start whose lines carry `FLEX_PAYOUT` is never a same-hash no-op on `/calculate-and-export`; it answers 409 "use a correction".
  - A raw export after the ledger start makes the next month refuse (case 4b) until that month is corrected.
  - An employed zero-line month is now locked against reopen (OQ-4, informed).
  - A `payroll-ledger-changed` at export leaves the calculation's manifest behind, like the existing export-time rechecks.
- **Routed onward:**
  - `frontend/src/pages/admin/opfoelgning/WorklistList.tsx:425` still names `PreviousFlexBalance` as a request field. It goes to TASK-14608 (wave 3, UX) as an authorized edit.
  - `openapi.json` and `api-types.ts` drift (the nullable field, the new result members) is regenerated in O-2.
  - The audit-catalog rows for the new mapper keys are the Orchestrator's, at D7.
- **TASK-14605-T dispatched** in the same worktree. The Orchestrator's corrections to its brief:
  - `RegressionTests.cs:44-54` added to the LEAVE list: it is the rule-engine core's input.
  - Two more bodies authorized: `ManifestPersistenceHostTests.cs:339, :354`.
  - The compiler's 30-file break list authorized for the drop-and-reader edit.
  - Fake resolvers on correction paths may gain the in-tx surface.
  - New: a **static chain census** of the Docker facts that export, correct or reopen several months for one employee, so CI-W2 holds no surprise. A changed asserted value is a STOP.

**TASK-14605-T returned** (`3add5847`, then `57b011cf`).
- **Census:**
  - Constructions: 22 files, found by `priorPeriodApprovalReader:`. The brief's command finds only 20, because two files use target-typed `new(`.
  - Bodies: 16 in 8 files. Direct-call sites: 65.
  - The LEAVE hits all stay, `RegressionTests.cs:44, :54` included. Nothing is unclassified.
  - A seventh non-zero opening, `ReplayFrozenInputsTests.cs:96`, is served by seeding inside `ReplayTestKit`.
- **The static chain census** found no Docker fact whose status or asserted value changes, so nothing was re-seeded for the chain.
- **One STOP, ruled.** `EmploymentWindowSegmentSkipTests.Shim_PreHireSegment_…` asserted a single window read. The second read is R-28's `[P.start, M.end]` derivation read, which is intended, so the fact now pins exactly the two reads.
- **One offer, accepted.** `FrozenInputsSlotTests` is seeded at 12.5 again, so its "0 would be a wrong substitute" check is not vacuous.
- **Gate:** 0 errors / 145 warnings; Unit 1518, DemoSeed 170, Regression non-Docker 130.

**Step 5a on the production code** (in parallel with 14605-T, against a detached review checkout at `f6d3f780`):
- 5α PASS.
- Fable APPROVED-WITH-WARNINGS.
- Codex REQUEST-CHANGES, on one BLOCKER. Neither calculating route required a whole calendar month: `/recalculate` accepts any range, and `/calculate-and-export` accepts any APPROVED range, including a legacy `WEEKLY` row. So a week's closing would be stored as the month's closing, and the next month would chain from it.

**Rulings R-47 … R-53** (adjudicator, `.claude/reviews/S146-TASK-14605-5a-adjudication.md`):
- **R-47:** the BLOCKER stands. Both calculating routes accept only one whole calendar month (400 `payroll-period-not-a-month`, before any read), with invariant throws at the two writers (`RecalculateAsync`, `FlexLedgerWriteFor`). The calculation service itself still calculates partial periods, which tested direct calls use; it writes no ledger.
  - **Owner informed:** a legacy `WEEKLY` approval can no longer be sent through `/calculate-and-export`. It was already unusable there (the second week answered 409 and locked the month). The approval flow has produced only MONTHLY periods since S127.
  - The pre-existing case where a partial `/recalculate` replaced the month's diff baseline closes with it.
  - The Orchestrator re-ran the census on `57b011cf`: no partial-period date in the tests reaches either writer.
- **R-48 (N7):** kept as built. A month that lost every employed day keeps the triple it was paid at. The hint is widened: M+1 is named when exported, and an ordinary correction of M+1 derives `REHIRE 0`. The parameter is renamed `nextMonthOpeningStale`.
- **R-49:** the event stays structured; Codex W3's prose-on-the-event is declined. The catalog row gets the two reading rules at D7.
- **R-50:** `EmptyLineSetContentHash` is deleted; `TryReadCurrentEffectiveLinesForUpdateAsync` is kept, because three facts read it.
- **R-51:** one comment sentence says why the `UNTRACKED` set-aside is safe: M < S is permanent.
- **R-52:** records and the D7 list.
  - **Replay residual** (below the floor; recorded, not built): `PlanInputs.OpeningFlexProvenance` is outside `IsComplete`. A damaged manifest that lost the member replays as if it were pre-S146, so an `UNTRACKED` month would replay with `FLEX_PAYOUT` instead of refusing.
- **R-53:** the fix-up goes to the same agent on the branch. Both lenses re-review the combined branch. TASK-14606 gains the R-47/R-48/R-49 pins (brief updated, Do 6b), and TASK-14608's help text also says "one whole calendar month".

**Fix-up dispatched** (R-47, R-48, R-50, R-51; `src/**` only).

**The re-review and the merge.**

| Task | Branch @ sha | Build | 5α | 5a Fable | 5a Codex | Merged |
|---|---|---|---|---|---|---|
| TASK-14605 + 14605-T (`payroll-integration`, `test-qa`) | `worktree-agent-a7d25c1f742b46097` @ `1992e4d5`, `f6d3f780`, `3add5847`, `57b011cf`, `50321d2c`, `69e54d88`, `f476763b` | 0 / 145; Unit 1518, DemoSeed 170, Regression non-Docker 130 | PASS (`f6d3f780`); PASS (the union to `50321d2c`) | c1 APPROVED-WITH-WARNINGS → c2 APPROVED-WITH-WARNINGS → scoped APPROVED (`f476763b`) | c1 REQUEST-CHANGES → c2 REQUEST-CHANGES → c3 APPROVE (`f476763b`) | `8f366802` |

- **Codex cycle 2** confirmed that R-47 and R-48 close its first-cycle findings, and found two new ones.
  - **BLOCKER (a miss in the static chain census):** H14 (`PayrollHostWeeklyTests` `Recalculate_ExportedMay2026_MonthEndsSunday_NamesNothing`) corrected a raw May. Under R-14 case 2 that month becomes the ledger start, its closing goes from null to 0, and so June is named. The fact's unchanged "names nothing" assertion would have failed at CI-W2.
    - The fix (`f476763b`, test-qa) seeds May as `LEDGER_START` 0→0, so the correction recomputes the same closing: the stub engine's `newBalance = 0m`, quoted at `TestFixtures.cs:432`. Only the Sunday test then keeps June out, which is the fact's stated purpose.
    - The census was re-run under four rules (a raw-month correction names an exported M+1; the new response and audit members; the 400 for a partial period; the zero-line record). It found no other changed assertion, and both lenses spot-checked that result.
    - The "closing changed → June named" case is TASK-14606's AC-C5 pin.
  - **WARNING:** `IsWholeCalendarMonth` threw for December 9999. It is now a field compare (`69e54d88`, by the Orchestrator under the Small Tasks Exception), which is also textually the Backend's predicate.
- **Fable cycle 2** confirmed the fix-up and the migration, with one new WARNING (W-1): a real-engine pin's only non-vacuity guard ("one export record") is now also satisfied by the zero-line record.
  - W-1, its docstring note N1, and the test-support tidy-ups in N4 are **routed to TASK-14606** (brief Do 6c), which owns the zero-line pins.
  - N2 (two "whole calendar month" predicates in two bounded contexts, now textually identical) is a later SharedKernel sweep item.
- **O-1 on master after the merge:** 0 errors / 145 warnings, CA2100 115, Unit **1518**, DemoSeed 170, Regression non-Docker 130. The Docker facts are CI-pending until CI-W2.
- **D7 part 2 (with the merge):**
  - ADR-034 D3 is amended: the flex triple on the calculating routes, the zero-line record (R-3), the whole-month rule (R-47) and the triple no-op (R-16).
  - ADR-034 D4 gains a note: the ledger advisory lock comes first, then the approval rows, then the record. QUAL-201 closes by construction, with a bounded residual (R-57).
  - ADR-013 is amended: item 2 now holds, and the next-month hint is widened (R-10, R-48).
  - The audit catalog gains the new keys on both rows and R-49's two reading rules.
  - QUAL-188 is BUILT and QUAL-201 is CLOSED BY CONSTRUCTION, with R-5's text (its residual widened at Step 7a, R-57).
  - QUAL-205's status said "Docker facts CI-verified at CI-W2", which anticipated a checkpoint that has not run. It is corrected to CI-pending.

## Wave 2c — TASK-14606 (the QUAL-188 pins)

**What it adds.** These are the tests that define "done" for the flex ledger.
- **Pure function (Unit).** One test per case of the function that decides a month's opening, plus "employed in the previous month" read correctly for a Monday-start month and for a spell that ends inside the previous month. Also covered: the whole-month check, damaged-ledger inputs, the opening comparison, and the never-silent reader.
- **Old event payloads (Unit).** They still deserialize after the event change.
- **Audit-row shapes (Unit).** The audit rows carry the new keys only when set, including R-49's two reading shapes.
- **Docker host facts.** The month's record holds what the engine closed at. The 400 and 409 refusals are named. A correction keeps the balance right and names the next month. A re-run whose balance moved refuses. A replay reproduces the recorded closing.
- **Race facts.** Each forces the ordering by holding the ledger lock on a second connection.

| Task | Branch @ sha | Build | 5α | 5a Fable | 5a Codex | Merged |
|---|---|---|---|---|---|---|
| TASK-14606 (`test-qa`) | `worktree-agent-aa99696e76b27d7bc` @ `a88c8c2f`, `68c9a75a`, `3e5e08c4` | 0 / 145; Unit 1564, DemoSeed 170, Regression non-Docker 130 | PASS (`a88c8c2f`); PASS (`68c9a75a`) | CHANGES-REQUESTED → APPROVED-WITH-WARNINGS → APPROVED (`3e5e08c4`) | REQUEST-CHANGES → APPROVE-WITH-WARNINGS → APPROVE (`3e5e08c4`) | `a9a677f1` |

**The review rounds, and what they changed:**
- **The mutation proof.** A background run was reaped for low memory after M06, and the agent was also cut off by an API session limit. M07–M26 were then run one at a time in the foreground.
  - Every mutation was killed: M01–M26, plus N1–N3 for the new pins.
  - Results are in the scratchpad, `mutation-results.txt` and `mutation-results2.txt`.
- **Codex's cycle-1 BLOCKER:** the only replay pin could not fail, because the stub's closing was a constant. It is now opening-dependent for that employee, and the replay's own engine call must carry the frozen 12.5.
- **"Nothing is read before the refusal".** Both lenses agreed a production code hook is out of scope for a tests-only task, so the facts prove it by behaviour instead:
  - a correction request reuses an already-delivered idempotency key, so a refusal moved after that check would answer 200 "already processed";
  - an export request has no approval, so a moved refusal would answer 403;
  - a positive control proves the pre-mark is live.
- **The opening comparison under the lock.** Fable's W-1: the comparison of where the opening came from (its provenance) was pinned nowhere. Race (ii) now differs only by provenance, and a Unit fact pins it.
- **Smaller fixes:**
  - case 4a now rejects "use the latest closing";
  - the damaged input now includes "previous record without a closing";
  - race (iv)'s claim is narrowed;
  - race (i)'s naming is relabelled R;
  - two other labels and four docstrings are corrected.
- **Accepted deviations, recorded:**
  - `FlexLedgerHostHarness.cs`: a harness beyond the brief's "stub". Consolidating it with `PayrollHostRealEngineTests` helpers, and moving to a collection fixture, are later items.
  - The extra `LEDGER_START` assertion in `ManifestProjectionRebuildPlanInputsTests.cs`.
  - `FlexLedgerSeed` now throws on an existing record. No caller seeds a month twice; verified.
  - The FIFO assumption: PostgreSQL grants a contended single-key advisory lock in queue order. It is disclosed, and both lenses judged it sound for these facts.
- **TASK-14605's Fable W-1, N1 and N4** are closed here (brief Do 6c).

**O-1 on master after the merge:** 0 errors / 145 warnings, CA2100 115, Unit **1564** (+46), DemoSeed 170, Regression non-Docker 130.

**CI-W2** (R-32): branch `s146-ci-w2`, draft PR #40 "EVIDENCE ONLY". Run `40049157171` on `a9a677f1` was RED on 1 fact of 2,216; six of seven jobs were green. The one red fact was a test-expectation defect, fixed and merged as `1af567fe`, and re-run `40055382782` on `1af567fe` is in progress. See § "CI-W2".

## Wave 3 (stacked) — TASK-14607 + 14607-T, O-2, TASK-14608 (C5, the flex screens)

**What changes for the product.** Three screens have shown a flex balance of 0 for everyone, because they read an event, `FlexBalanceUpdated`, that nothing ever wrote. The screens are the employee's card on `/summary`, the year-overview tile and the leader's team overview. They now show the employee's latest *paid* month's closing balance, labelled with its month, or "no settled month".
- **Team overview (OQ-5):** the figure stays withheld on rows whose month is unsubmitted, as the owner ruled. A closed state field (`flexBalanceState`: `withheld`, `none` or `recorded`) tells the screen which of the three cases a row is in, so a null never has to be guessed at.
- **Deleted:** the dead route `GET /api/flex-balance/{employeeId}`, its response type and the never-written event.

**TASK-14607** (`backend-infrastructure`), `62b4c340`, with the comments-only fix-up `c9a37a76`:
- **The read:** a new Infrastructure `FlexLedgerReadRepository` reads the Payroll-owned `payroll_export_records`, read-only (the ADR-034 D4 precedent), and picks the latest ledger row per employee. The production build is clean.
- **Declared deviations, accepted by both lenses:**
  - `/summary`'s `flexDelta` keeps its type, but its value is now the flex movement in the month named by `flexBalanceMonth`, and 0 when there is none. The deleted event always gave 0. The frontend never renders it.
  - The two `BalanceEndpoints` handlers swap `IEventStore` for the repository.
  - The read record also carries the opening balance.

**TASK-14607-T** (`test-qa`), `d55531e3`:
- **Pins:**
  - three OQ-5 team-overview facts (withheld R+F, recorded F, none F);
  - the new `Balance/FlexBalanceReadTests.cs`: the latest ledger record and its month, a later raw record ignored, no record gives null and not 0, raw-only gives null, and the scope 403s (R);
  - extended Draft, Rejected and Theory facts, with the seeds moved to ledger records;
  - `S120BalanceSpecRuntimeTests` now pins 15 `/summary` keys. That file was added to the authorized list by the Orchestrator at dispatch; Fable ratified it (N-1).
- **Retired** (in `S120TimeSpecRuntimeTests`), with the route:
  - `Flex_Get200_NoHistoryBranch_NormalizedOneShape_Ruling1DeltaPin`;
  - `Flex_Get200_WithHistoryBranch_LatestEventValues_ByteFaithful`;
  - `Gate_IsRed_OnInjectedPhantomRequiredMember_AndGreenOnTheCommittedTruth`. Its technique is still pinned in the S117–S119 spec tests, verified by both lenses.
  - `ForeignEmployee_403_OnAllFourOps_SelfGatePins` was renamed `…OnTheThreeRemainingOps…`.
  - `FlexBalance_ComesFromLatestFlexEvent` was replaced by the three OQ-5 facts.
  - S120 ruling #1's first instance retires with the route; the class rule stands.
- **Gate:** 0 errors / 145; Unit 1564, DemoSeed 170, Regression non-Docker 130; `PayrollExportRecordsWriteGuardTests` green. No live `FlexBalanceUpdated` use remains.

**Step 5a.** 5α PASS twice (`62b4c340`, then the union to `c9a37a76`). Codex APPROVE-WITH-WARNINGS → APPROVE. Fable APPROVED-WITH-WARNINGS on both cycles.
- **The OQ-5 question** (raised by the implementing agent, and weighed by both lenses):
  - **What the agent raised:** the team overview withholds the figure for an unsubmitted month, but the same leader sees the same latest recorded closing on the employee's year overview, which has no month gate. In the normal case that closing comes from an exported month, which is approved and therefore submitted. The edge case is a GlobalAdmin raw export, which skips approval, followed by a correction; that can put a closing on a month the employee never submitted.
  - **Ruling (Orchestrator, with Fable): an accepted consequence, no code change.** OQ-5 binds the team overview by its own words. The year overview is in RES-002's owner-accepted ungated remainder: S128 gated 3 of 12 manager reads and left 9 open as deferred hardening, and the year overview already shows a leader every month's actuals. The edge path is the GlobalAdmin approval bypass, a known-accepted hole. Codex's fix (withhold the figure when its source month is unsubmitted) was conditional on that stricter intent, which RES-002's ruled posture does not carry.
  - **For the owner (a neutral notice, no decision asked):** the year overview, and `/summary` for the HR tier, now show a leader a real last-paid flex figure without a submission gate, where they used to show a meaningless 0. This is RES-002's posture carrying a real value for the first time. Extending OQ-5's strict reading to the year tile is a possible later ruling. RES-002's census row is updated at the docs pass.
- **Recorded for later:**
  - A "boundary pin — holds today" fact for a leader reading the year tile would make that posture visible in the suite (Fable N-2). It is a candidate for the RES-002 revisit.
  - One team-overview fixture seeds two `LEDGER_START` months, a state a real ledger never holds; the Backend read ignores provenance. Align it when next touched (Fable N-3).
  - `ReadLatestOfTypeAsync`, `ReadLatestOfTypePerStreamAsync` and `ReadAllAsync` have no production caller left, and the `init.sql:36-48` index rationale is written around the deleted event. This is a quality-register candidate.
- **Sequencing (Fable W-1):** the S120 balance spec facts are red against the committed `openapi.json` until O-2. They are CI-verifiable only on the post-O-2 branch, which is how the stack merges.

**O-2 on 14607's branch** (`cd9bdd23`, Orchestrator): `openapi.json` and `api-types.ts` were regenerated. The drift is exactly the reported wire change:
- the route and `FlexBalanceResponse` are gone;
- `flexBalance` is nullable on `/summary` and the year tile;
- `flexBalanceMonth` is added on all three responses;
- `flexBalanceState` is required on the team row, with the enum `withheld | none | recorded`.

**TASK-14608** (`ux`), `48386e4f`, with fix-up `20a2968a`:
- **Card:** "pr. <måned år>", or "ingen afregnet måned" with no number.
- **Year tile:** "afregnet flexsaldo pr. …", replacing the misleading "optjent overtid" (flex shown as overtime, the QUAL-217 confusion on screen).
- **Team overview:** three visibly different states. Withheld shows "—". None shows "ingen afregnet måned". Recorded shows the value with its month as visible text.
- **Deletions:** the dead card, hook and `FlexBalanceInfo`.
- **Operator help text:** WorklistList names the refused fields (`Entries`, `Absences`, `PreviousFlexBalance`) and the whole-month rule.

Step 5a: 5α PASS ×2; Fable CHANGES-REQUESTED → APPROVED (`20a2968a`). Fable's cycle 1 had two required findings and two the Orchestrator folded in:
- **W1 (required):** the leader screen's test fixtures lacked the now-required state, so they rendered withheld rows as "ingen afregnet måned", the text OQ-5 forbids. The `row()` factory is now typed against the generated `TeamOverviewRow` (QUAL-121), so the next contract change fails `tsc`.
- **W2 (required):** the cell now fails closed. Only an explicit `none` prints words; any unrecognised state, or a `recorded` row with a missing or malformed month, shows the dash.
- **W3 (folded in):** the month is visible text in the table cell, no longer a hover-only `title`.
- **W4 (folded in):** the operator text.
- **N1 (folded in):** one guarded month formatter (`formatYearMonthWord` in `lib/locale.ts`) replaces three copies, two of which would have thrown on a malformed month. `lib/locale.ts` and its test file were added to the authorized list by the Orchestrator in the fix-up message (Fable N-a).

Recorded, not built: the card's double divider and the column width (visual checks); the "Flex saldo" vs "flexsaldo" wording pass; a screen-level pin for a malformed month on the card and tile.

**The stack merge:** `88eb3da6` (TASK-14607 + 14607-T + O-2 + TASK-14608), merged together so master's `tsc` is never red between the backend and frontend changes (R-25). **O-1 on master:** 0 errors / 145 warnings, CA2100 115, Unit 1564, DemoSeed 170, Regression non-Docker 130, `npx tsc --noEmit` 0, vitest 80 files / 1010 tests.

**Owner notice (neutral; recorded in RES-002).** The year overview, and `/summary` for the HR tier, now show a leader a real last-paid flex figure without a submission gate, where they used to show a meaningless 0. The team overview keeps it withheld on unsubmitted rows (OQ-5). This is the RES-002 posture carrying a real value for the first time. Extending OQ-5's strict reading to the year tile is a possible later ruling.

**D7 part 3 (with the merge):**
- ADR-018 D13 `:478`: the example is retired; the rule stands.
- ADR-026 `:189` and the audit catalog `:170`: the event is noted as deleted.
- DEP-004: the route row is marked deleted.
- RES-002: the census row, the "8 since S146" count and the S146 note.
- The performance register's F5: a note that the four consumers are gone and that the latest-of-type reads and their index have no production workload.
- QUAL-224: BUILT.

**Recorded tidy items:**
- Retire `ReadLatestOfTypeAsync`, `ReadLatestOfTypePerStreamAsync` and `ReadAllAsync`, which have no production caller, and re-justify or drop the `init.sql:36-48` index.
- Consolidate `FlexLedgerHostHarness` helpers with `PayrollHostRealEngineTests`, and move to a collection fixture.
- Align the double-`LEDGER_START` team-overview fixture.
- Fix the stale "hand-written `TeamOverviewRow`" docstring at `useTeamOverview.ts:10`.

## CI-W2 (R-32) — the first CI run of the wave-2 Docker facts

- **Run `40049157171` on `a9a677f1` (2026-10-24): RED on exactly 1 of 2,216 Regression facts.** The other six jobs were green: lizard, gitleaks, `check_docs` (so the hand-synced `db-schema.md` holds), E2E (Playwright on the compose stack), smoke and the frontend build.
- **What passed:** every QUAL-205 manifest fact and every QUAL-188 flex-ledger fact, including the host, race and replay facts.
- **The red fact:** `PayrollHostRealEngineTests.CalculateAndExport_September_FullTimeAC_AtNorm_NoMerarbejde_RealEngine`. That is the non-vacuity guard TASK-14606 added at Fable's TASK-14605 cycle-2 W-1.
  - **What it assumed:** that the export record's `NORMAL_HOURS` lines are dated per weekday.
  - **What the code does:** the calculating route dates every exported line with its **segment range** (`PeriodCalculationService.MapSegmentToExportLinesAsync` → `BuildLine` with the planned segment's start and end). Only the raw `/export` and `/export-period` date per entry.
  - **Verdict:** a `trace` agent confirmed a test-expectation defect, not a product one.
- **The fix (`1bcf93b2`):** the guard asserts 22 `NORMAL_HOURS` lines summing to 162.80 h. It is still RED for a host that saw no hours (0 lines, 0.00 h).
  - The Orchestrator qualified two older docstring sentences (`5e2a0450`, Small Tasks Exception).
  - Codex APPROVE-WITH-WARNINGS (folded in), Fable APPROVED; merged as `1af567fe`.
  - The agent's grep found no other fact expecting per-entry dates on calculating-route lines.
- **Re-run `40055382782` on `1af567fe`:** pushed to the same evidence branch. Pending.


## Step 7a (sprint end, dual-lens) — cycle 1 and the TASK-14610 fix

**Cycle 1 on `3c23f708`** (the base is `13a40e68`, S145's close commit):
- **Codex: REQUEST-CHANGES** (`.claude/reviews/S146-step7a-codex-c1.md`).
  - **BLOCKER:** the employed zero-line month's record and its `PayrollExportGenerated` named no manifest. ADR-016 D10's durable link rides on the export lines, and such a month has none, so its recorded closing was provable only by searching the month's manifests.
  - **WARNING:** flex-ledger refusals were not logged.
- **Fable: APPROVED-WITH-WARNINGS** (`.claude/reviews/S146-step7a-reviewer-c1.md`).
  - **W-1:** QUAL-201's closure sentence missed the UNTRACKED case.
  - **W-2:** six tasks' internal 5a artifacts were not saved.
  - **W-3:** CI evidence for wave 3, and the QUAL-188/205 flips.
  - **W-4:** register the M+2 stale-chain gap.
  - Plus notes. The internal lens traced both writers end to end and found the chain holds.

**Adjudication R-54 … R-62** (`.claude/reviews/S146-step7a-adjudication-c1.md`):
- **R-54:** the BLOCKER stands. `PayrollExportGenerated` gains an optional `ManifestId`, set on `/calculate-and-export`, plus the audit key `manifestId`. There is no new column: the correction route's link is event-only already (S145 R-19).
- **R-55:** the fix is built as TASK-14610 and 14610-T.
- **R-56:** the refusal is logged, with no durable refusal record.
- **R-57:** the QUAL-201 residual is corrected.
- **R-58:** recover the missing 5a texts, and add a WORKFLOW 5a sentence.
- **R-59:** the CI flips and the C-2 gate.
- **R-60:** register QUAL-229, with the owner deciding the timing.
- **R-61:** the N-2 wording, the N-3 refusal order, and the N-6 F-5 sub-question.
- **R-62:** the venue.

**Owner, 2026-10-26: Q-1 = (b).** The M+2 stale-chain guard (QUAL-229) is built in **S147, as the first payroll item**, before the model program touches the same chain.

**W-2: eight of nine recovered.** Eight of the nine missing internal 5a reports were recovered verbatim from the session transcript and saved under `.claude/reviews/` with a provenance header. The ninth, TASK-14609's fix-up review, was not found (corrected at Step 7a cycle 2, W-1). The eight:
- 14600; 14601 + its fix-up; 14602 (+T); 14603 + its follow-up; 14604 (+T); 14609.
- No verdict rests on memory.
- Their verdicts: 14600 AwW, 14601 AwW, 14601 fix-up A, 14602 A, 14603 A, 14603 follow-up A, 14604 AwW, 14609 AwW.
- **Internal-lens 5a artifact not persisted for TASK-14609's fix-up (`ac56c72e`):** verdict APPROVED, as in the merge table; the findings acted on are those recorded above; no other finding from that review is known to be outstanding (R-58(2)). The recovered TASK-14609 text has no `reviewed-by-model` line.

**The refusal order, as built (R-61(b)).** The org-scope 403 comes first, then the three fixed 400s (hours, opening balance, period not a month), then the approval 403. After that the plan-time and admission-time refusals come in the order the calculation reaches them: the rules-unavailable 503 and the planner's 422s while the plan is built, admission's 409s, the calculation's own 422, the manifest 503, and the export-time 409s (order corrected at Step 7a cycle 2, N-4). Never "400 before 403".

**TASK-14610** (`payroll-integration`), `aa00fb96` and fix-up `df4e79e9`:
- the event member and the appended audit key;
- the context member;
- a guard that refuses a calculating-route export naming no manifest (null or empty) before its transaction;
- `Program.cs` sets the committed `outcome.ManifestId`;
- the three refusal sites log a warning; the 409 body is unchanged;
- the lock comment, corrected three times (`df4e79e9`, then `753e0cc8` for Fable W-1 and `a6fcd6f6` for Codex W).

**TASK-14610-T** (`test-qa`), `f77ac71c`: Unit pins for the key and the round trip, with the mutations run and killed; four service-level Docker facts (the calculating context carries the id, the raw context omits it, a null or empty id refuses with nothing written); and a host fact proving that the zero-line month's event and audit row name the one stored manifest.

**Step 5a:**
- 5α PASS.
- Codex APPROVE on the production change, then APPROVE-WITH-WARNINGS on the tests and comment (the warning is fixed in `a6fcd6f6`).
- Fable APPROVED-WITH-WARNINGS on the production change (the W-1 comment, fixed).
- **Fable's scoped check of 14610-T was cut off by the Fable usage limit (2026-10-26)**, and the Opus stand-in did it before the merge (see below).

**CI-W2 GREEN** on the re-run: `40055382782` on `1af567fe`, all seven jobs; every wave-2 Docker fact CI-verified.

**CI-W3:** `a6fcd6f6` (master `3c23f708` plus TASK-14610 and 14610-T) pushed to `s146-ci-w2`, run `40294555527`. Pending.

**CI-W3 red on the frontend (2026-10-26).** `frontend-build` failed at the "No-`as` / typed-client lint" step, because the frontend lint script names its files one by one and still listed `src/hooks/useFlexBalance.ts`, which TASK-14608 had deleted. ESLint stops on a missing named file. Two gaps let this through:
- TASK-14608's deletion census searched `frontend/src` only, so it missed `package.json`;
- the local O-1 ran `tsc` and vitest but not `npm run lint`.

**Fixed** in `dc606dc7` (Orchestrator, Small Tasks Exception). `npm run lint`, `typecheck:e2e` and `build` all exit 0 locally. **From here on, the frontend part of O-1 runs `npm run lint` and `npm run typecheck:e2e` as well.** The other CI-W3 jobs were green, including the end-to-end tests (Playwright on the compose stack); the Docker Regression job was still running at this record.

**The owner's instruction on review models (2026-10-26): "Lets just use opus for now."** The Fable model hit its usage limit during the close, and the owner chose to run the internal review lens on Opus 5.5 rather than wait for it.
- **How it runs:** the `reviewer` agent is locked to Fable by the routing guard, so the stand-in runs as a `general-purpose` agent on Opus with the same brief.
- **How it is labelled:** its artifacts say `reviewed-by-model: claude-opus-5-5` and state the instruction. No Opus review is labelled as Fable.
- **External lens:** Codex is unchanged.
- **Consequence at close:** the close guard requires the Step 7a internal artifact on the Fable floor, so it needs a documented owner exception (see the close record).

**TASK-14610-T scoped review (cycle 2): Opus stand-in APPROVED** (`.claude/reviews/S146-TASK-14610-5a-reviewer-c2.md`). Its five notes are recorded as tidy items:
- **N-1:** the refusal facts assert only the exception type; pin the "names no manifest" message.
- **N-2:** the unit golden's Guid has no hex letters, so it doesn't test letter case.
- **N-3:** a stale "three keys" docstring.
- **N-4:** a redundant `DoesNotContain`.
- **N-5:** two loose red-condition docstrings.

**Merged as `e395c4ec`.** O-1 on master: 0 errors / 145 warnings, CA2100 115, Unit **1566**, DemoSeed 170, Regression non-Docker 130; `tsc` 0, `npm run lint` 0, `typecheck:e2e` 0, vitest 80 files / 1010 tests.

**D7 part 4 (with the merge):**
- the audit catalog: the `manifestId` key, with its note;
- ADR-016 D10: a footnote (R-54);
- ADR-034:
  - D3 item 1: the event now names the manifest;
  - D4: the collision wording corrected, including the reopen's two arms (R-61(a)), plus the QUAL-201 residual (R-57);
- the register:
  - QUAL-188 and QUAL-205 flipped to CI-verified (`40055382782`);
  - QUAL-201's residual widened (R-57);
  - QUAL-219 points to ADR-043 F-5;
  - new **QUAL-228**, FIXED (TASK-14610);
  - new **QUAL-229**, REGISTERED, built in S147 by the owner's Q-1;
- ADR-043 F-5: the leaver sub-question (R-61(c));
- WORKFLOW step 5a: save the reviewer's report before acting on it (R-58).


**Step 7a cycle 2 on `c532f004`** (scope `3c23f708..c532f004`):
- **Codex: APPROVE** (`.claude/reviews/S146-step7a-codex-c2.md`). Its cycle-1 BLOCKER and WARNING are closed, it found no new defect, and the docs match the code.
- **Internal lens on Opus (owner's instruction): APPROVED-WITH-WARNINGS** (`.claude/reviews/S146-step7a-reviewer-c2.md`).
  - **W-1:** the claim that every missing 5a text had been recovered was wrong; it is eight of nine. Corrected above.
  - **N-1:** two ADR sentences overstated. Corrected in ADR-016 D10 and ADR-034 D4.
  - **N-2:** stale handoff and QUAL-201 wording. Corrected.
  - **N-3:**
    - `docs/FRONTEND.md` still listed the deleted card and hook; corrected.
    - The stronger frontend gate is now written in WORKFLOW (Phase 4).
    - The leftover `useFlexBalance.ts` entry in `frontend/eslint.config.mjs:111` is harmless (an unmatched pattern) and recorded as a tidy item.
  - **N-4:** the recorded refusal order was approximate; corrected.
  - **N-5:** the close guard's model check needs an owner decision. See the next paragraph.
  - **N-6, a real behaviour that predates S146.** Each export line carries its calculation's manifest id, and the content hash covers it, so ADR-034 D3's same-hash no-op only ever fires for an employed zero-line month. A re-run of a month with lines always answers the "use a correction" 409. This is safe (corrections-only). It is recorded in ADR-034 D3 item 3 and not changed; it falls below the register's Medium floor.

**The owner's decision on the close guard (2026-10-26).** The guard accepts the Step 7a internal artifact only on Fable, and S146's ran on Opus.
- **First answer, withdrawn:** the owner first chose S145's route, a one-sprint exception in the guard.
- **Why it was withdrawn:** the Orchestrator then found that S145's post-close review cycles had added harness test T26, which pins that the model check carries no sprint-number exception. That route would have meant weakening T26, so the Orchestrator went back to the owner.
- **Final choice: a narrow review-floor waiver.** A new `SPRINT-<N>-review-floor-WAIVED.md`, beside the guard's other narrow waivers, lifts only the reviewer artifact's model check. Every other close gate stays enforced, and T26 stays untouched.
- **Who builds it:** the Orchestrator, through a `general-purpose` Opus agent working to an exact spec, with two harness tests. It gets its own Step 7a review (cycle 3), as T26's comment requires.

## Handoff block (written at every gate — the state lives here, not in the conversation)

| | |
|---|---|
| **As of** | 2026-10-26, master `c532f004` plus the Step 7a cycle-2 record, local only; origin/master `0f02b484` |
| **Phase** | The close. TASK-14610 is merged (`e395c4ec`) and D7 part 4 has landed. O-1 is green on `e395c4ec`. Next: Step 7a cycle 2 (Codex, plus the internal lens on Opus by the owner's instruction), CI, the close docs, then the close |
| **Tasks** | Every task DONE, including the Step 7a fix TASK-14610 + 14610-T |
| **Track D** | D1–D7 DONE. D7 part 1 `5087ab88`; part 2 with the 14605 record; part 3 with the wave-3 record (ADR-018:478, ADR-026:189, the catalog, DEP-004, RES-002's census and S146 note, the performance-register F5 note, QUAL-224 BUILT) |
| **Pending gates** | (1) Step 7a cycle 2 on both lenses over `3c23f708..HEAD`; the internal lens runs on Opus by the owner's instruction (Fable at its usage limit), so the close guard's Fable-floor check needs a documented owner exception. (2) CI on the final head (CI-W3 `40294555527` is still running; frontend-build was red on the lint list, fixed `dc606dc7`). (3) The close docs: QUALITY.md, the routing register row, INDEX, Test Verified. (4) Worktree teardown, C-2 and the close |
| **CI** | G-0 `39755579330` · **CI-W1** `39992507686` GREEN (PR #39 closed) · **CI-W2** `40049157171` RED 1/2216 (a test-expectation defect, fixed `1af567fe`), re-run **`40055382782` GREEN** (all seven jobs) · **CI-W3** `40294555527` on `a6fcd6f6` running (PR #40) · C-2 — · close — |
| **Open rulings** | none. Owner Q-1 (2026-10-26) = S147 first payroll item for QUAL-229. For S147 (ADR-043's forks): the order of work, the local days off, the part-timer default, PROSA groups 3–4, the AC office-staff period, and the leaver's last closing (R-61c) |
| **Next action** | Run Step 7a cycle 2: Codex, plus the Opus stand-in for the internal lens |
