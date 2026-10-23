# PLAN — Sprint 146: payroll correctness (QUAL-205, QUAL-188, carry-overs, QUAL-198) + the overtime/flex decision track

*Draft 2.1, 2026-10-23: brief edits applying R-35 … R-39 (Step 0b cycle 2, `.claude/reviews/PLAN-s146-adjudication-c2.md`), for a scoped Codex cycle 3 on R-35 and R-36. Draft 2 applies the Fable seat's rulings S146 R-24 … R-34 on Step 0b cycle 1 (both lenses NOT-READY; `.claude/reviews/PLAN-s146-adjudication-c1.md`, verdict RULED, no owner questions). Drafted by the `planner` (Opus 5.5) under "Fable judges — Opus drafts". Source of truth: `.claude/refinements/REFINEMENT-s146-payroll-correctness.md` **rev 3.2, READY** (535 lines). That refinement and its rulings are gitignored, so every brief under `.claude/plans/s146/` is written to be pasted whole, with the refinement lines it names pasted verbatim beside it (`docs/AGENTS.md` § "A gitignored artifact does NOT reach a worktree"). Every file:line below was re-opened at master `0f02b484`.*

| | |
|---|---|
| **Status** | **READY at Step 0b** (2026-10-23): internal lens READY at cycle 2; external lens READY at cycle 4 (cycle 3 scoped to R-35/R-36: R-36 RESOLVED, R-35 PARTIAL on three unclassified census hits, which the Orchestrator classified in TASK-14608:53, TASK-14605-T:29 and TASK-14609:32; cycle 4 confirmed). Draft 2.1 = draft 2 + R-35 … R-39 |
| **Theme** | Every paid month becomes provable: its proof is written in one transaction through the outbox, or payroll refuses (QUAL-205). Flex gets one recorded balance per paid month, chained, serialized, corrected and visible (QUAL-188). QUAL-201 closes by construction. Part-timers stop seeing a full-time norm (QUAL-198). Three S145 tidy-ups. Beside the code, the Orchestrator's decision track (D1–D7) turns the verified flex/overtime research into tracked docs, register rows, a re-verification and a PROPOSED model ADR, so S147 opens the model program on verified ground |
| **Predecessor** | S145: close `13a40e68` (CI green `39571040922`) · C-9 docs-only `6e3ba7ef` · post-close `6c29efc2` … `0f02b484` (hook commits under owner rulings R-104 and R-141..R-144, plus their records) · CI green `39755579330` on `0f02b484`, all seven jobs |
| **Base commit (Step 7a)** | **`13a40e68`**, the S145 close commit, not HEAD (`docs/WORKFLOW.md` step 7a: post-close commits fall inside the next Step-7a diff). So the eight post-close hook commits and their records are inside S146's Step-7a diff |
| **Master at draft** | `0f02b484` |
| **Orchestrator model** | Opus 5.5 seat throughout (client 2.1.301), never switched. Drafting: `planner` (Opus). Judgment: `adjudicator` and `reviewer` (Fable 5.1). Implementation by role name with no `model` override: `payroll-integration`, `backend-infrastructure` (Opus); `data-model`, `test-qa`, `ux`, `constraint-validator` (Sonnet). External lens: Codex |
| **Refinement** | rev 3.2 — READY after three Step-4 cycles (both lenses READY at cycle 3, no BLOCKER), the owner-decided rev 3.1 fold (no cycle 4) and the R-21..R-23 Step-0a fold (verification at Step 0b) |
| **Owner rulings in force** | **S146 OQ-1 = (iii)** the certain fixes + decision track D · **OQ-2 = (a)** S145's Q2 = A, Q3 = F1 and Q7 (b) superseded in principle, in force in code; no new work on F1 machinery; QUAL-209/210/212/213 frozen · **OQ-3 = (b)** flex by default, overtime/merarbejde only for ordered or accepted hours; the model's forks are S147's · **OQ-4** informed, no veto: an employed zero-line month writes its record (R-3) · **OQ-5 = (b)** the team overview keeps flex withheld until the month is submitted · **no fourth refinement cycle** (cycle-3 folds verified at Step 0b) · **S145 Q-1:** QUAL-198 in S146 · standing: reseed, don't migrate; review cap 5 per lens |
| **Entropy scan (Step 0a)** | Done 2026-10-23 at `0f02b484` (`.claude/refinements/S146-HANDOFF.md`): tree clean except untracked `.claude/plans/PLAN-s145.md` and `.claude/plans/s145/` (**DEBT:** commit the plan and the `TASK-*.md` briefs, not the evidence logs, with the S146 open commit, S144 precedent `a2785c7f`) · 0 extra worktrees · no local branches but master; remote dependabot only · no `FindFirst("scopes")`, no hard-coded localhost, no PAT-005 direct rule calls · endpoint authorization CLEAN · KB paths: 42 cited, 2 benign non-resolving · orphans `FlexBalanceCard`, `useFlexBalance`, `/api/flex-balance`, `flex_balance_snapshots`, `FlexBalanceUpdated`, `BuildPlanForLegacyCallersAsync` — all in scope (T4, E2) · **DRIFT:** `danish-agreements.md` "Weekly Settlement Rules" asserts "the sources are silent"; fixed first by D1–D3 · `check_docs.py` not runnable locally (no Python); CI docs job green · grades: Payroll A− stands; the close re-grade states the contradicted weekly premise |
| **Baseline** | S145 final (`docs/sprints/SPRINT-145.md:6`): build 0 errors / **145** warnings · CA2100 **115** · Unit **1512** · DemoSeed **170** · Regression **2150** in CI / non-Docker **128** locally · Smoke **7** · vitest **1005**. Re-measured at the first gate by `sprint-test-validation`, never carried forward |
| **Pre-merge gate G-0** | **SATISFIED:** CI run `39755579330` green on `0f02b484` |

---

## Plain-language summary (read this first)

**What S146 builds.**
1. **Every paid month becomes provable (QUAL-205, TASK-14604).** Today a calculation writes its proof (the *manifest*) in two separate steps; if both fail, payroll still exports or still corrects, and nobody is told. S146 writes the manifest, its projection row and the legacy summary event in **one transaction through the project's existing outbox** (ADR-018), the way Payroll's export and correction events already travel. If that transaction fails, both routes answer 503 and write nothing. The test databases are made ready for this first, in wave 1 (TASK-14609).
2. **Flex gets one recorded balance per paid month (QUAL-188, TASK-14605/14606).** Today the next month's opening balance is whatever the caller types, the closing balance is computed and thrown away, and the employee's "Flex saldo" card shows 0 for everyone. After S146 each paid month's export record carries its opening, closing and where the opening came from; the next month opens from the previous month's recorded closing through one function (`DeriveOpening`, R-14); every writer of the record serializes on a per-employee lock; a correction keeps the balance right and names the next month; and the screens show the real balance with its month (TASK-14607/14608), except that a leader's team overview keeps it hidden until the month is submitted (OQ-5).
3. **QUAL-201 is not built.** The race needs a previous month that is approved but not exported; once months chain from recorded balances, that month must be exported, and an exported month cannot be reopened (R-5). The residual (the first month the system pays) is recorded.
4. **Small items.** QUAL-198: part-timers see their own monthly norm (TASK-14602). A dead method deleted (TASK-14600). Five test helpers that fail with a crash instead of an assertion (TASK-14603). A hook comment made exact (T7, Orchestrator).
5. **The decision track (Orchestrator, D1–D7).** The verified research becomes a tracked dossier; its findings are registered (root cause first: the agreement-source register cites the wrong sections); the reference docs are annotated; research agents with an independent verifier re-check the open agreement cells; a PROPOSED model ADR is drafted to direction (b); and the ROADMAP opens the model program in S147. No code changes the weekly model this sprint (OQ-2).

**How it is checked.** There is no sprint-wide mutation catalogue this time (S145 kept one table of planned deliberate breakages); instead **each pin proves it can fail on its own**. Every acceptance criterion names its test and its kind: **F** a fact with an assertion that fails on today's code, named under "Red conditions"; **N** a fact that needs this sprint's new API, proven RED by one named local mutation (Unit only); **B** a boundary pin that holds today, with a discriminating assertion; **R** a regression pin. Docker is unavailable on the owner's machine: Docker-gated facts argue their RED in the docstring and are reported **"CI-pending"** until a CI checkpoint shows them green. There are three checkpoints: **CI-W1** after wave 1, **CI-W2** after wave 2c, and **C-2** before Step 7a (R-32), so a Docker problem is found in the wave that caused it.

---

## How the waves are built — read before the ledger

1. **`PeriodCalculationService.cs` is touched by three tasks** (14600 deletes a method, 14604 moves the manifest, 14605 retires the opening parameter and adds the ledger), so they run **in that order**, each branched from local master after the previous merged (refinement task table, R-13).
2. **Foundations land first and alone, and are seen green in CI before anything builds on them** (R-24, R-32). Wave 1 holds the schema and optional shapes (14601, `data-model`, with its harness copy 14601-T), and the **test-harness readiness for the outbox** (14609: the Regression harness gains `outbox_events`, the test event stores become enqueue-capable, and a drain helper waits in front of every fact that rebuilds or replays from `events`). All of it is additive and green against today's code (the manifest is not on the outbox yet, so a drain is a no-op). **CI-W1** then runs wave 1's Docker facts once. *Declared deviation:* the refinement folds the schema into T3; this plan splits it out by role, as S145's TASK-14500 did, so O-3 (the `db-schema.md` hand-sync) happens at 14601's merge.
3. **Tests that need the new API are written after it exists**, on master, and prove RED by one named local mutation (Unit) or an argued docstring (Docker). Tests that compile today and assert something today's code gets wrong are F.
4. **File overlap is designed out by order**, not by merge luck: 14603 (wave 1) and 14605-T (wave 2b) share three host-test files; 14601-T and 14609 (both wave 1) share `HarnessDdlMatchesInitSqlTests.cs`, so 14609 branches after 14601-T merged; 14609 (wave 1) and 14604-T/14605-T/14606 (wave 2) share `TestFixtures.cs`, `WeeklyPayrollTests.cs`, `SplitMonthPayrollTests.cs`, `FrozenInputsSlotTests.cs`; 14602 (wave 1) and 14607 (wave 3) share `BalanceEndpoints.cs`, `ApprovalEndpoints.cs`, `ApprovalResponses.cs`; 14600's comment edits exclude the files 14603 and 14609 own.
5. **Wave 3 is one stacked unit** (R-25): 14607 + 14607-T, then **O-2 regenerates `openapi.json` and `api-types.ts` on 14607's branch**, then 14608 branches from that branch; the stack merges together. Otherwise master's `tsc` is red between the backend and frontend merges (`frontend/src/types.ts:30` references the deleted `FlexBalanceResponse`).

| Wave | Tasks (parallel within a wave; `isolation: "worktree"`; each branched from LOCAL master) | Gate before the next wave |
|---|---|---|
| **1** | 14600 dead builder (payroll-integration) · 14601 schema + shapes (data-model) + 14601-T · 14602 QUAL-198 (backend-infrastructure) + 14602-T · 14603 test helpers (test-qa) · **14609 harness outbox readiness (test-qa; after 14601-T merged)** · **D1, D2, D3** · **T7** (Orchestrator) | merge checklist per task → O-1 → **O-3** hand-sync from 14601's reported DDL → O-2 check (QUAL-198: values only, expect no drift) → **CI-W1** |
| **2a** | 14604 QUAL-205 (payroll-integration) + 14604-T (test-qa; needs 14609 merged) | merge → O-1 |
| **2b** | 14605 QUAL-188 C1–C4 (payroll-integration) + 14605-T site migration (test-qa) | merge → O-1 |
| **2c** | 14606 QUAL-188 pins (test-qa, on master) | merge → O-1 → **CI-W2** · **D7 part 1** (R-23 docs after 14604; ADR-034 D3, ADR-013 after 14605) |
| **3 (stacked)** | 14607 + 14607-T (backend-infrastructure, test-qa) → **O-2 on 14607's branch** → 14608 (ux) from that branch | merge checklist per task → the stack merges together → O-1 incl. `tsc --noEmit` + vitest |
| **D (parallel)** | D4 re-verification (research agents + independent verifier) from wave 1 on; D5 after D4's verdicts; D6 after D2 and D5 | D4's deferred branch if it overruns (AC-D4) |
| **close** | C-1 … C-5 below | sprint-close guard at C-4 |

---

## The merge checklist — every task, paired pass and fix-up

1. **Report read.** Brief contradictions and declared deviations are dispositioned: factual ones by the Orchestrator; requirement, invariant or ruling questions to the `adjudicator`.
2. **Build.** `dotnet build` 0 errors; warnings ≤ 145; CA2100 ≤ 115.
3. **Step 5α — `constraint-validator`** on `git diff <base>...<branch>`, scope = the task's authorized files (a paired branch: the union; any `src/**` change in a `-T` pass's own commits is a violation).
4. **Step 5a — `reviewer` (Fable)** on the branch diff with the pasted brief; one verdict line per task.
5. **Step 5a — Codex** for the rows marked high-risk below (`docs/WORKFLOW.md` step 5a "High-Risk External Review Override": schema, payroll export, retroactive corrections, audit), `codex review "<prompt>"`; if the sandbox blocks, inlined, and the verdict says so.
6. **Merge**, then O-1 on master; the sprint-log row `task · branch · build · 5α · 5a-R · 5a-C · merged sha` and the handoff row are written. **An empty 5α or 5a cell blocks the merge.** Cap: 5 cycles per lens per task.

| Task(s) | 5α | 5a Reviewer (Fable) | 5a Codex (high-risk) |
|---|---|---|---|
| 14600 | ✓ | ✓ | ✓ payroll export (path rule, `docs/AGENTS.md:591`) |
| 14601 + 14601-T | ✓ | ✓ | ✓ schema migration + audit event shapes |
| 14602 + 14602-T | ✓ | ✓ | — (display only; never reaches payroll) |
| 14603 | ✓ | ✓ | — (tests only) |
| 14609 | ✓ | ✓ | — (tests only) |
| 14604 + 14604-T | ✓ | ✓ | ✓ payroll export, retroactive corrections, audit |
| 14605 + 14605-T | ✓ | ✓ | ✓ payroll export, retroactive corrections, audit |
| 14606 | ✓ | ✓ | ✓ (pins of the above; the lens checks they can fail) |
| 14607 + 14607-T | ✓ | ✓ | ✓ visibility rule (OQ-5) + cross-context read + an event type deleted |
| 14608 | ✓ | ✓ | — |
| Every fix-up | ✓ | ✓ | as its parent |

---

## Standing constraints — pasted verbatim into every agent prompt

```
★ FIRST, BEFORE ANY WORK: check your worktree against master.
Run `git log --oneline -1` and compare with the main checkout's LOCAL master — not `origin/master`,
which lags by design because this project pushes only at sprint close. Fast-forward if you are
behind, and say in your report what you found.
```
```
Do NOT use `git stash` — it is repository-global, not per-worktree, and will collide with
other agents running concurrently.

RED-FIRST (docs/AGENTS.md § "RED evidence"). Every new or changed fact says in its docstring which kind
it is, and its "Red conditions":
 F  an assertion that FAILS ON TODAY'S CODE. Name that assertion under "Red conditions". Unit: run it
    on master before the implementation is merged and quote the failure. Docker: you cannot run it
    locally; name the production behaviour that makes that assertion fail today. If no assertion
    of the fact fails today, it is NOT F: label it B or R and give it a discriminating assertion.
 N  names a type or member this sprint creates. Unit: prove RED by ONE named mutation at your gate,
    never by reverting: `git worktree add --detach <path outside the repo> master`, apply the
    mutation (keep the API, break the one behaviour), run the fact, capture the assertion failure,
    `git -C <path> checkout -- .`, then `git worktree remove --force <path>` and `git worktree prune`.
    A compile error never counts. Docker: argue it in the docstring as for F.
 B  a boundary pin that holds today. Label it "boundary pin — holds today" so nobody "fixes" it.
 R  a regression pin (an existing behaviour kept). Label it so.
TEST NAMES ARE CONTRACT where a brief gives them; if you must rename, report old → new.

CENSUS RULE (R-25, R-35). Where a brief changes a constructor or removes a parameter, it states the
census COMMAND (`git grep`, or `grep --include='*.cs'`, so bin/obj binaries never match) and classifies
EVERY hit as (a) authorized for edit (named, with what may change), (b) expected and unchanged (named,
with a one-line reason), or (c) generated (named; regenerated by O-2, never hand-edited). Re-run the
command. An unclassified hit is a STOP and report, never an edit.

GREP GATES (R-29). Every "grep is empty" gate runs over `src tests frontend/src` only. Hits under
docs/**, .claude/** and the sprint logs are expected history.
```
- **`docs/CONVENTIONS.md` verbatim** — the Orchestrator pastes it into every prompt (tracked, but the prompt is the channel guaranteed to arrive).
- **KB entries:** the Orchestrator also pastes the KB entries the ledger's "KB pasted" column names for the task (`docs/WORKFLOW.md:86`).
- **Docker is unavailable locally.** Report Docker facts as **"CI-pending"** (R-30); the sprint log flips a fact to "CI-verified" only with the checkpoint run id that showed it green. Never block on them. The local gate is build + Unit + DemoSeed + Regression `Category!=Docker` (+ `tsc --noEmit` + vitest for frontend).
- **Read exit status from the unpiped command** (S140).
- **Never edit a generated file by hand:** `docs/api/openapi.json`, `frontend/src/lib/api-types.ts`, `docs/generated/db-schema.md`. **No agent edits `docs/**`** (Orchestrator-only).
- **Report anything the brief does not name** (an enumeration site, a caller, a test, a harness copy).
- **Contract vs. hint.** A "*Hint (HOW)*" is one way; the contract is the WHAT. Deviate from a hint freely and say so; from a contract only by reporting.
- **Brief challenge:** if the code contradicts the brief, go by the code and report it (claim · what the code shows · file:line). A contradiction is a report, never a permission.
- **Paired passes (`-T`) only:** you are on another agent's branch. Edit only the listed files and **never `src/**`**; a production defect is reported, not fixed. Change an expected value only where the brief lists it as intentional, labelled `// S146 <ruling>:`; any other value that changes is a **STOP and report**.
- **Frozen by OQ-2:** no new work on F1 machinery (context days, the prior-month approval wait, Q7 (b)'s Sunday trigger, HRP-001b, Do 7, QUAL-209/210/212/213). Touch it only where a brief says so.

---

## Task ledger

*Each brief is `.claude/plans/s146/TASK-<id>.md`; paste it whole, with the standing constraints above, the refinement lines it names, and the KB entries in the "KB pasted" column (R-30). The sprint log carries a "## Task ledger" section with DONE / CUT / DEFERRED / DROPPED for every TASK id below (close guard, `docs/WORKFLOW.md:47`).*

| Task | Wave | Role (model by definition) | What | Authorized files (exactly these) | KB pasted | Depends on | Size |
|---|---|---|---|---|---|---|---|
| **[TASK-14600](s146/TASK-14600.md)** | 1 | `payroll-integration` (Opus) — **`Payroll Integration (extended into tests/**, the listed files only, cross-domain authorized)`** | E2: delete `BuildPlanForLegacyCallersAsync`; re-point its two reflection callers; fix the comments and the stale `[Obsolete]` text | `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs`, `…/Payroll/Program.cs` (comments `:65`, `:364` only); tests listed in the brief | — | G-0 | S |
| **[TASK-14601](s146/TASK-14601.md)** | 1 | `data-model` (Sonnet) — **`Data Model (extended into SharedKernel/Segmentation, cross-domain authorized)`**; the `init.sql` edit is **Orchestrator-approved here** | The one schema change: `payroll_export_records` gains `opening_balance`, `closing_balance`, `balance_provenance` (all-null-or-all-set CHECK); `flex_balance_snapshots` dropped; S146 segment. Optional members on `PayrollExportGenerated`, `RetroactiveCorrectionRequested`, `PeriodCalculationResult`; optional non-positional `PlanInputs.OpeningFlexProvenance` | `docker/postgres/init.sql`; `src/SharedKernel/StatsTid.SharedKernel/Events/PayrollExportGenerated.cs`, `…/Events/RetroactiveCorrectionRequested.cs`, `…/Models/PeriodCalculationResult.cs`, `…/Segmentation/PlanInputs.cs` | ADR-034 D1; ADR-018 D8 | G-0 | S–M |
| **[TASK-14601-T](s146/TASK-14601-T.md)** | 1 (paired) | `test-qa` (Sonnet) on 14601's branch | The `payroll_export_records` harness DDL copy follows `init.sql`; the R51 guard extended to it; the schema facts | `tests/StatsTid.Tests.Regression/Payroll/PayrollExportRecordsTestSchema.cs`, `…/Migrations/HarnessDdlMatchesInitSqlTests.cs`, new `…/Migrations/PayrollExportBalancesSchemaTests.cs` | ADR-034 D1 | 14601 returned | S |
| **[TASK-14602](s146/TASK-14602.md)** | 1 | `backend-infrastructure` (Opus) — **`Backend + Infrastructure (cross-domain authorized)`** | F: QUAL-198 — `/summary` and the team overview use `weekdays × DayNorm.ForWeekday(W, fraction)`, fraction at month end; the team overview's profile read behind an **injectable** repository (R-31) | listed in the brief | ADR-002 (S145 `DayNorm` amendment); register QUAL-198 | G-0 | S–M |
| **[TASK-14602-T](s146/TASK-14602-T.md)** | 1 (paired) | `test-qa` on 14602's branch | Part-time pins for both screens; the fault-isolation pin; full-time pins unchanged | listed in the brief | register QUAL-198 | 14602 returned | S |
| **[TASK-14603](s146/TASK-14603.md)** | 1 | `test-qa` | E3: five assertion helpers read members through one `RequiredMember` helper; the `PayrollHostWeeklyTests.cs:466` E2 comment | the three host files (helpers + `:466` only); new `…/TestSupport/JsonAssert.cs`, `…/TestSupport/JsonAssertTests.cs` | — | G-0 | S |
| **[TASK-14609](s146/TASK-14609.md)** | 1 | `test-qa` (Sonnet) — no paired pass | R-24: the Regression harness gains `outbox_events` (text-identical to `init.sql`); enqueue-capable stores in the harness and two Payroll suites; a `HarnessDdlMatchesInitSqlTests` row; `PayrollOutboxDrain` + its fact; census-driven drain calls before every rebuild/replay-from-`events` step | listed in the brief | ADR-018 D1, D2; register QUAL-195 | 14601 + 14601-T merged (they share `HarnessDdlMatchesInitSqlTests.cs`) | M |
| **[TASK-14604](s146/TASK-14604.md)** | 2a | `payroll-integration` — **no cross-domain change** (R-21) | A: QUAL-205 — `EmitManifestAsync` enqueues manifest + legacy events through `IOutboxEnqueue` in one transaction with the projection insert; seam `ISegmentManifestStore` (`PersistAsync`, `TryLoadAsync`); `AuditState = Complete \| NotPersisted \| NoManifest`; one PCS-public refusal predicate both routes call (R-27) | `…/Payroll/Services/PeriodCalculationService.cs`, `…/Services/RetroactiveCorrectionService.cs`, `…/Payroll/Program.cs`, new `…/Services/ISegmentManifestStore.cs`, new `…/Services/PostgresSegmentManifestStore.cs`, new `…/Services/ManifestNotPersistedException.cs` | ADR-018 D3, D4, D13; ADR-016 D10 | 14600 merged | M–L |
| **[TASK-14604-T](s146/TASK-14604-T.md)** | 2a (paired) | `test-qa` on 14604's branch | The in-memory store; the census rule over **23** files (22 PCS constructions + `EmploymentWindowSegmentSkipTests.cs`, an `InMemoryEventStore` reader); the three R-22 sites to `outbox_events`; AC-A1..A3 facts (trigger seam with a positive control; the drain fact via `PayrollOutboxDrain`) | listed in the brief | ADR-018 D13; ADR-016 D10 | 14604 returned, **14609 merged** | M |
| **[TASK-14605](s146/TASK-14605.md)** | 2b | `payroll-integration` — **`Payroll Integration (extended into Infrastructure/AuditMappers, two mappers, and the employment-window resolver's in-tx range overload, cross-domain authorized)`** | C1–C4: QUAL-188 — `FlexLedgerOpening.Derive` (R-14, `route` dropped per R-28), ledger reader (constructor dependency, never silent), its own windows read over `[P.start, M.end]`, opening parameter retired, ledger lock on all four routes (R-15), balances recorded and corrected, zero-line record (R-3), triple no-op (R-16), C4 widening, `UNTRACKED` diff exclusion | `…/Services/PeriodCalculationService.cs`, `…/Services/RetroactiveCorrectionService.cs`, `…/Services/PayrollExportService.cs`, `…/Services/PayrollExportRecordRepository.cs`, `…/Payroll/Program.cs`, new `…/Services/FlexLedgerOpening.cs`, `…/Services/IFlexLedgerReader.cs`, `…/Services/PostgresFlexLedgerReader.cs`, `…/Services/FlexLedgerRefusalException.cs`; **cross-domain:** `src/Infrastructure/StatsTid.Infrastructure/AuditMappers/PayrollExportGeneratedAuditMapper.cs`, `…/AuditMappers/RetroactiveCorrectionRequestedAuditMapper.cs`; `src/Infrastructure/StatsTid.Infrastructure/IEmploymentWindowResolverInTx.cs` and `…/EmploymentWindowResolver.cs` (the in-tx range overload only, R-36) | ADR-034 D3–D5; ADR-013; ADR-016 D10; ADR-032 D4; ADR-040 D5 | 14604 merged, 14601 merged | **L** |
| **[TASK-14605-T](s146/TASK-14605-T.md)** | 2b (paired) | `test-qa` on 14605's branch | The site migration under the census rule: ~45 direct sites, 14 payroll-route bodies in 7 files, a ledger-reader fake in the 22 construction files, the six non-zero openings re-seeded; the named "leave" sites untouched | listed in the brief | ADR-016 D10 | 14605 returned | **L** |
| **[TASK-14606](s146/TASK-14606.md)** | 2c | `test-qa` | QUAL-188 pins: `FlexLedgerOpeningTests` (cases 1–4c + the two `EmployedIn` facts, N), host facts AC-C1..C3, C5, C6, races AC-C4, replay and round-trip AC-C7 | listed in the brief | ADR-034 D3–D5; ADR-016 D10 | 14605 + T merged | M–L |
| **[TASK-14607](s146/TASK-14607.md)** | 3 (stacked) | `backend-infrastructure` — **`Backend + Infrastructure (extended into SharedKernel/Events and EventSerializer for one deletion, cross-domain authorized)`** | C5: three screens read the latest recorded closing with its month; OQ-5 withheld until submitted, `flexBalanceState` discriminator (R-33 item 5); `/api/flex-balance`, `FlexBalanceResponse`, `FlexBalanceUpdated` deleted | listed in the brief | ADR-034 D4; ADR-026; ADR-018 D13 (the `:478` carve-out, with D7's note) | 14605 + 14602 merged | M |
| **[TASK-14607-T](s146/TASK-14607-T.md)** | 3 (stacked, paired) | `test-qa` on 14607's branch | AC-C9 facts incl. OQ-5; the four files that seed `FlexBalanceUpdated`; the S120 route facts retired | listed in the brief | ADR-034 D4 | 14607 returned | M |
| **[TASK-14608](s146/TASK-14608.md)** | 3 (stacked) | `ux` (Sonnet) | C5 frontend: the Skema card, the year tile (label), the team overview's three states; dead card, hook and their tests deleted | listed in the brief | — (`docs/FRONTEND.md`) | O-2 on 14607's branch | S–M |

*Orchestrator rows (no agent):* **O-1** gate (build, warnings ≤ 145, CA2100 ≤ 115, Unit, DemoSeed, Regression `Category!=Docker`; for wave 3, `npx tsc --noEmit` + vitest). **O-2** contract regeneration after 14602 (expect no drift) and **on 14607's branch** (expect the drift 14607 reports): `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi`; `npm run gen:api`. **O-3** `docs/generated/db-schema.md` hand-synced at 14601's merge from its reported DDL (no Python locally; CI `check_docs.py --check` arbitrates, `SPRINT-145.md:720` precedent). **O-4** records (§ "Records owed"). **O-5** Step 7a. **O-6** CI checkpoints CI-W1, CI-W2, C-2 and the close CI watch. **O-7** routing-register row. **T7 (E1)** the hook comment, below. **D1–D7** the decision track, below.

---

## T7 — E1, the hook comment (Orchestrator; own Step 7a)

`.claude/hooks/test-sprint-close-guard.ps1`, comment only, three edits (refinement E1; owner R-144, `SPRINT-145.md:3201-3204`): the clause at `:391-392` ("must be the plain ASCII `-ne`: any other spelling of it fails") becomes "`-ne` in any letter case; a typographic dash, `-cne`, `-ine` or any other form fails" (`SPRINT-145.md:3202`); the "does not see" list (`:406-419`) gains "the right-hand side of the one `$reviewedBy =`"; R-143 joins the provenance line (`:382-383`). **AC-E1:** the harness shows 24/24, and one Step 7a cycle on both lenses approves the change (`docs/WORKFLOW.md` step 7a, "Post-Step-7a coverage" for hook files). The commit message must not contain the phrase "sprint close" together with `S146` (the guard reads the whole Bash command).

---

## D — the decision track (Orchestrator; `docs/**` is Orchestrator-only; research agents write only under `.claude/`)

*Plain language:* the code fixes do not depend on the research; the next program does. D turns the verified research into tracked, checkable docs, re-checks the few agreement cells still open, and drafts the design the S147 program will refine. Prerequisites: **D1 → D2 → D3**; **D1 → D4 → D5 → D6**; **D7 after 14604 (R-23 part) and 14605 (the rest)**. D1–D3 run in wave 1 because they fix the Step-0a DRIFT.

| # | Deliverable | Acceptance (AC-D…) | When |
|---|---|---|---|
| **D1** | `docs/references/overtime-flex-model-research.md`: the first pass plus the verification's corrections, status "verified claim by claim" (sources: `.claude/refinements/RESEARCH-s146-flex-vs-overtime.md`, `…-VERIFICATION.md`); a row in CLAUDE.md's "Historical & research dossiers" table (`CLAUDE.md:59`) | **AC-D1** tracked, CLAUDE.md row present, CI `check_docs.py` green | wave 1 |
| **D2** | Register rows, **root cause first** (the source register's citations are unreliable for load-bearing cells, H), then: monthly norm period / `NormPeriodWeeks ∈ {1,2,4,8,12}` cannot express it (H); automatic overtime where the sources require ordered work (H; **QUAL-189 widened to point at it**); 37/40 thresholds and the 100% tier unsourced (H); payout vs forfeiture above the cap (M); AC merarbejde (H); compensation choice, `DefaultCompensationModel` ignored, overtime balance never fed (M); holidays (M); a watch row (Sunday 2.0, HK pay ceiling, part-timer award); C5's finding (M); the explicit opening balance (R-1 backlog, L/M). Drift corrections (adjudication c1 Part 5 item 4): QUAL-205's "surfaces a degraded state" sentence, QUAL-188/189 line numbers, a footnote on ADR-013:22-23 | **AC-D2** every row registered with severity, root cause first; QUAL-189 points at it; drift corrected | wave 1 |
| **D3** | First amend the `disputed?` definition (`agreement-source-register.md:37`) to "true when the register records a dispute between the parties **or** a contradiction of the encoded value by a verified primary source; `notes` says which" (R-19). Then dated notes on `danish-agreements.md` Weekly Settlement Rules `:67-86`, Overtime Thresholds `:60-65`, Compensation Model `:88-103` (premise contradicted; OQ-2; D2 ids), and `disputed? = true` with a note on SR-AC-OK24-020 (`:496`), SR-HK-OK24-001 (`:990`), SR-AC-OK24-005/007/021 (`:196`, `:236`, `:516`), SR-HK-OK24-006/007 (`:1090`, `:1110`), SR-HK-OK24-020 (`:1370`), SR-PROSA-OK24-001 (`:1710`). **Never touches `confidence_level`** | **AC-D3** definition amended; three notes; nine rows flagged; `confidence_level` untouched | wave 1 |
| **D4** | Re-verification by research agents (`general-purpose`, Opus, web search) with an **independent adversarial verifier**, as the S146 RESEARCH files were made. **Re-uses V1–V12** for the cells they settle; **new work only on:** the holiday list (§ 4 stk. 2 vs `danish_public_holidays`); the Sunday 2.0 rate (§ 14: +50% both weekend days; ordered weekend/holiday work outside the hours count); the HK pay-ceiling exemption (085-25 § 4 stk. 2 remark); the part-timer rule (the 4 Feb 2026 award, OK26 item 28); AC's norm period (medium → firm); a sample of local flex-cap behaviours; and the compensation-choice cell. Output: a verified dossier under `docs/references/` with per-cell verdicts (CONFIRMED / CONTRADICTED / PARTLY / UNRESOLVED, each with a quoted primary source). **D4's verdicts are authoritative and overwrite `disputed?` and `confidence_level`** | **AC-D4** every new cell has a verdict, quote and the verifier's agreement or dissent; settled cells cite V1–V12; dossier filed; fields follow the verdicts. *Deferred branch:* if D4 runs past the close, the sprint log records the carry-over with each cell's interim verdict, D5 is drafted at S147's opening, and D6 points S147 at the pending verdicts and the ADR to come | from wave 1 |
| **D5** | Draft model ADR to direction (b), status **PROPOSED**: lists the S147 forks (refinement "Decisions recorded"), cites D4's verdicts, names the F1 machinery its acceptance retires | **AC-D5** as stated; *same deferred branch* | after D4 |
| **D6** | ROADMAP § 1 (`ROADMAP.md:26-50`): S147 opens the model program (refinement, then the ADR's acceptance); the frozen QUAL-209/210/212/213 point there | **AC-D6** as stated; *same deferred branch* | after D2, D5 |
| **D7** | *After 14604:* ADR-018 D6 gains `segment-manifest-{manifestId}` and corrects `period-calculation-*` to `period-calc-{employeeId}-{periodStart}`; ADR-016 D10 dated footnote (the manifest reaches `events` through the outbox since S146; the committed outbox row is the record of truth until the drain); `docker/postgres/scripts/rebuild_segment_manifests.sql:17-24` and the C# comment `SegmentManifestProjectionRebuilder.cs:24-31`: "quiesce writers and let the Payroll outbox drain (`published_at IS NOT NULL` for `segment-manifest-%`) before running" (both by the Orchestrator under the Small Tasks Exception, R-33 item 1); QUAL-205 closed. *After 14605:* ADR-034 D3 amended on its own rationale (R-3); ADR-013:22-23 amended; QUAL-188 closed; QUAL-201 closed with R-5's text. *After 14607:* **ADR-018:478** — annotate the example (`BalanceEndpoints.cs:102` reading `FlexBalanceUpdated`) as retired in S146; the carve-out rule itself stands (R-30) | **AC-D7** each doc landed; the three rows closed; ADR-018:478 annotated | wave 2c, wave 3, close |

---

## Acceptance criteria → verifying test → task

*Kind: F an assertion that fails on today's code · N needs the new API (Unit: one local mutation at the gate) · B boundary pin, holds today · R regression pin. "CI" = Docker-gated, "CI-pending" until a checkpoint run id shows it green.*

| Refinement criterion (rev 3.2) | Verified by | Kind | Task |
|---|---|---|---|
| **AC-A1** refusal on `NotPersisted` / `NoManifest`+success, both routes, no payroll write | Unit `NotPersisted_RefusesBeforeAnyPayrollWrite`, `NoManifestWithSuccess_Refuses` (both on the R-27 predicate; one mutation each: drop a clause); host `CalculateAndExport_ManifestNotPersisted_Returns503_NoPayrollWrite`, `Recalculate_ManifestNotPersisted_Returns503_NoPayrollWrite` (trigger seam; each route's call to the predicate) | N (local) · F (CI) | 14604-T |
| **AC-A2** one transaction: projection failure leaves no row for either event; in-memory throw → refusal; drain to `events` with the same `event_id` | `ProjectionInsertFails_NoOutboxOrEventRowForManifestOrLegacy` — asserts the 503, zero `outbox_events` **and** zero `events` rows for both streams for the call; **positive control** in the same class `ProjectionInsertSucceeds_OneOutboxRowPerStream` (R-26); Unit refusal facts; `ManifestOutboxRow_DrainsToEvents_SameEventId` (via `PayrollOutboxDrain`) | F (CI) · F (CI) · N · F (CI) | 14604-T |
| **AC-A3** three `AuditState` states; key gone; fixtures on the in-memory store; `PcsNeverSilentTests.cs:150-168` replays via `TryLoadAsync` without a DB | build + grep over `src tests frontend/src` (O-1); `PcsNeverSilentTests` unchanged in assertion | R | 14604, 14604-T |
| **AC-A4** folded into AC-A2 (R-22) | — | — | — |
| *(R-24)* the Regression harness's `outbox_events` matches `init.sql` | new `[InlineData]` row of the guard theory (`HarnessDdl_HoursTablesAndManifests_MatchInitSqlSchema`, as named after 14601-T, R-39 N6) for the `DockerHarness` copy | F (CI) — the table does not exist in the copy today | 14609 |
| *(R-24)* the drain helper drains a payroll row into `events` | `PayrollOutboxDrain_EnqueuedPayrollRow_DrainedToEvents_SameEventId` | N (CI) | 14609 |
| *(R-24)* rebuild/replay-from-events facts keep passing after the move | the four census facts (`ManifestProjectionRebuildTests`, `ManifestProjectionRebuildPlanInputsTests`, `FrozenInputsSlotTests` `:133-147`, `BoundaryCauseEncodingTests` `:195-215`) with a drain before the rebuild/replay; assertions unchanged | R (CI) | 14609 |
| **AC-C1** opening, closing, provenance recorded; closing = clamped `NewBalance`; payout, no-payout, **employed zero-line** month; no-EMPLOYED month writes none | `FlexLedgerHostTests.*` with a stub returning a **non-zero** closing + one `_RealEngine` fact; `NoEmployedDay_WritesNoRecord` | F (CI); last is **B** | 14606 |
| **AC-C2** body `previousFlexBalance` → 400 on both routes before any read | `CalculateAndExport_BodyCarriesOpeningBalance_Returns400`, `Recalculate_BodyCarriesOpeningBalance_Returns400` | F (CI) | 14606 |
| **AC-C3 (a)–(g)** R-14's cases on the host incl. raw history via `/recalculate`, Monday-start June 2026 `CHAINED`, `UNTRACKED` excludes `FLEX_PAYOUT` from both diff sides, never silent | `FlexLedgerHostTests.*` (one fact per bullet) | F (CI) | 14606 |
| **AC-C3 (h)** the pure function, one fact per case 1–4c, plus `EmployedIn_MondayStartMonth_UnderContinuousEmployment_IsTrue` and `EmployedIn_PreviousMonth_SpellEndsInP_IsTrue` (R-28); and the two window reads agree: `EmployedIn_InTxAndSelfManaged_Agree` (Docker, R-36) | `FlexLedgerOpeningTests.*`; the Docker fact in `FlexLedgerHostTests` | N (local mutation each) · N (CI) | 14606 |
| **AC-C4** races, orderings forced (`PriorMonthExportRaceTests.cs:50-57` pattern): (i) correction of M vs export of M+1, both orders; (ii) two first exports, both orders; (iii) same month concurrently (**regression pin**); (iv) raw `/export-period` waits on the ledger key, or dropped | `FlexLedgerRaceTests.*` | F (CI); (iii) R | 14606 |
| **AC-C5** correction updates balances in its transaction; event carries opening used + closing before/after; names M+1 on a closing change even when M ends on Sunday (May 2026) | `Recalculate_ClosingChanged_SundayEndingMay2026_NamesJune` + event-payload facts | F (CI) | 14606 |
| **AC-C6** same-hash re-run compares the triple; `UNTRACKED` vs null equal ignoring the closing; zero-line re-run after a fraction change → 409; balance-only correction records + emits | `ReRun_SameLines_OpeningChanged_Returns409`, `ReRun_ZeroLineMonth_AfterFractionChange_Returns409`, `Recalculate_BalanceOnly_RecordsAndEmits` (F); `ReRun_UntrackedRawMonth_IsNoOp` (**B**: `FlexProvenance == "UNTRACKED"` in the response, balances still null) | F (CI) · B (CI) | 14606 |
| **AC-C7** replay reproduces the recorded closing; old events deserialize; mappers carry the keys; `check_docs.py` passes | `FlexLedgerReplayTests.Replay_DerivedOpening_ReproducesRecordedClosing` (CI); `S146ShapeRoundTripTests.*` (old payloads); `AuditPayloadFormatPinTests` new facts; `ManifestProjectionRebuildPlanInputsTests.AssertPlanInputsEqual` compares `OpeningFlexProvenance` (R-33 item 2); CI docs job | F (CI) · R · N | **14606**, O-3 |
| **AC-C8** `flex_balance_snapshots` gone, dropped in the segment, unreferenced | `PayrollExportBalancesSchemaTests.SnapshotsTable_Absent_AfterSegment` (CI); grep over `src tests frontend/src` at O-1 (`init.sql` may hold only the segment's `DROP TABLE IF EXISTS`) | F (CI) | 14601-T |
| *(R51 class)* harness copy of `payroll_export_records` matches `init.sql` | a new `[InlineData("PayrollExportRecordsTestSchema")]` row of `HarnessDdl_HoursTablesAndManifests_MatchInitSqlSchema` | **R (CI)** — holds today; guards drift (R-26) | 14601-T |
| **AC-C9** three screens show the latest recorded closing with its month or "ingen afregnet måned"; scope unchanged; no Backend write; route/hook/card/event deleted; "optjent overtid" gone; pins retired in the log | `FlexBalanceReadTests.*` value/month/none facts (F, CI); the two unchanged 403 checks (**R**, CI); `PayrollExportRecordsWriteGuardTests.Backend_And_Infrastructure_NeverWriteTo_PayrollExportRecords` (R, local); frontend `BalanceSummary.test.tsx`, `ArsoversigtPage.test.tsx`, `TeamOversigt.test.tsx` (F, local); grep | F · R | 14607-T, 14608 |
| **AC-C9 (OQ-5)** unsubmitted team row: withheld; submitted: the labelled closing (`ApprovalEndpoints.cs:1197`) | `TeamOverviewAggregateTests.cs:712` rewritten into `FlexBalance_Unsubmitted_Withheld` (**R**), `FlexBalance_Submitted_ShowsLatestRecordedClosingWithMonth` (**F**, new response), `FlexBalance_Submitted_NoRecordedMonth_ShowsNone` (**F**) | R · F (CI) | 14607-T |
| **AC-E1** hook comment, 24/24, one Step 7a cycle | harness run | — | T7 |
| **AC-E2** method gone; reflection suites re-pointed; `[Obsolete]` names both callers | build; grep over `src tests frontend/src`; the two hydration suites | R | 14600 |
| **AC-E3** no unguarded member read in the five helpers; a removed `reason` fails as an assertion | the five helpers read through `JsonAssert.RequiredMember`; its non-Docker fact `RequiredMember_MissingMember_FailsAsAssertion_NotKeyNotFound` (the three host files are Docker-gated classes, e.g. `SplitMonthHostTests.cs:47`) | N (local) · R (CI) | 14603 |
| **AC-F** QUAL-198 as register `:422`, full-time pins unchanged | `Summary_PartTime0811_NormExpected132`, `TeamOverview_PartTime0811_NormExpected132`, `TeamOverview_ProfileReadFails_FallsBackToFullTime_No500` (unconditional, R-31; **N**, R-38 — mutation: remove the catch around the batch profile read, the `awayToday` idiom at `ApprovalEndpoints.cs:996-1015`, so the failure propagates to a 500); `Sprint13BalanceTests.cs:78-84`, `LeaverAccrualEndCapTests.cs:202-209` unchanged (R) | F (CI) · N (CI) · R | 14602-T |
| **AC-D1 … AC-D7** | § D | — | Orchestrator |

---

## CI checkpoints and the close sequence (Orchestrator on Opus; Fable-seat judgments marked)

*Plain language:* Docker cannot run here, so the Docker-only facts are first seen in CI. Rather than waiting until everything is built, each wave that lays a Docker-only foundation is run in CI once, on a throwaway branch, before the next wave builds on it (R-32). The sprint is then reviewed on a fixed commit **K** that has itself run green in CI, and the close commit (docs-only on top of K) is pushed and watched. Nothing reviewed is amended.

**The checkpoint mechanism (used by CI-W1, CI-W2 and C-2):** `git push origin <sha>:refs/heads/s146-ci-<name>`; open a **draft** PR to master, first body line "EVIDENCE ONLY — S146 checkpoint <name>; never merge". Watch to completion; confirm `headSha == <sha>`; **all seven jobs green**. Then close the PR unmerged, `git push origin --delete s146-ci-<name>`, `git branch -D` locally. Red → a fix task through the merge checklist → repeat. The sprint log records each run id, and only then flips the facts it showed green from "CI-pending" to "CI-verified".

1. **CI-W1 (after wave 1's O-1 and O-3).** On local master: wave 1's Docker facts (14601-T's schema facts and harness row, 14609's harness row, drain fact and the four census facts, 14602-T, 14603's helpers, and 14600's re-pointed `ProfileBoundaryHydrationTests`, R-39 N4).
2. **CI-W2 (after wave 2c's O-1).** On local master: 14604-T's and 14606's facts, 14605-T's migrated suites.
3. **C-1 (after wave 3's O-1).** O-4 records written with the Step-7a outcome and CI ids as marked placeholders. Commit **K** on master, message `S146 close candidate: <summary>`, without the phrase "sprint close" (`sprint-close-guard.ps1:80-92`). The sprint log's "## Task ledger" names every TASK id with a disposition. Not pushed.
4. **C-2 — the pre-review checkpoint on K.** As above, branch `s146-ci-k`. Red → fix → K′ → repeat C-2.
5. **C-3 — O-5 Step 7a.** Both lenses, base **`13a40e68`**, `reviewed-against-commit: <K>`; the carried items below. A code-touching fix makes K′ → repeat C-2, then the verification cycle step 7a requires.
6. **C-4 — the close.** `S146 sprint close — …`, a docs-only child of K (the Step-7a outcome, the checkpoint run ids). Confirm `git diff --stat K HEAD` lists only `docs/**`. Push master; watch CI on the close sha to green. Red: a post-close fix with its own Step-7a cycle.
7. **C-5 — docs-only follow-up.** The CI-green line for the close sha (`Test Verified`), the O-7 model-routing row, the handoff block "closed"; push; watch green; teardown (worktrees, branches).

**Carried into Step 7a:** (1) the post-close hook commits `6c29efc2` … `0f02b484` (inside the diff from `13a40e68`); (2) T7's own cycle record; (3) the Step 0b verification list below, re-checked against the code as built; (4) R-16/R-14 consequences recorded in the sprint log, not rediscovered.

---

## Step 0b — what the lenses must verify (cycle 2)

1. **The R-24 … R-34 edits** landed where the absorbed-findings table below says, and nowhere a ruling did not reach.
2. **For TASK-14604 (adjudicator c3-outbox, "Verification venue"):** (a) R-22's three test sites (`PriorMonthExportRaceTests.cs:523-525`, `FrozenInputsSlotTests.cs:194-198`, `SplitMonthPayrollTests.cs:413-418, :445-446`) are in 14604-T's Do; (b) `TryLoadAsync` is on the seam, so the Unit replay facts pass without a database.
3. **The cycle-3 folds** (owner: verified at Step 0b, not by a fourth refinement cycle): reviewer W1 / Codex NOTE 4 → 14604, 14604-T; W2 → 14605, 14605-T; W3 / Codex W2 → 14606; W4 → 14605, 14606; W5 / OQ-5 → 14607, 14607-T, 14608; N1 → 14604-T; N2 → 14605; N3 → 14606; N4 → 14605; N5, N7 → 14605's report; N6 → 14606; Codex NOTE 3, NOTE 5 → 14605. (Verified by the internal lens at cycle 1; re-check only what draft 2 changed.)
4. **Census commands** in 14604-T, 14605-T, 14609 and 14608 reproduce the stated hit counts at `0f02b484`.

---

## Records owed (O-4; Orchestrator)

- **Quality:** `docs/QUALITY.md` S146 re-grade — states plainly that S145's weekly settlement rests on a contradicted premise (OQ-2); Payroll Integration and Domain Correctness graded with that stated. Register: QUAL-188, QUAL-205, QUAL-198 → FIXED/BUILT; QUAL-201 → CLOSED (R-5 text); D2's rows; QUAL-189 widened.
- **ADRs:** D7's set (ADR-018 D6 and `:478`, ADR-016 D10 footnote, ADR-034 D3, ADR-013); D5's PROPOSED ADR.
- **Catalog:** `docs/operations/audit-projection-catalog.md:118-119` (the new keys) and `:170` (`FlexBalanceUpdated` removed).
- **HR register:** HRP-003's Q7 (b) sentence gains "or when M's closing flex balance changed"; HRP-001b unchanged (frozen).
- **Runbook:** `docs/operations/legacy-db-upgrade-runbook.md` S146 row ("pre-S146 databases are reseeded"; the segment).
- **`docs/generated/db-schema.md`** (O-3). **`ROADMAP.md`** (D6; QUAL-198's S146 line retired).
- **Sprint log** `docs/sprints/SPRINT-146.md` + **INDEX row**: header (base `13a40e68`), the task ledger, the 5α/5a table, each gate's O-1 line, the CI-W1/CI-W2/C-2 run ids and the facts each flipped to "CI-verified", the local mutation table (N-pins), the retired pins (`ArsoversigtPage.test.tsx:185`, the S120 `/api/flex-balance` shape, the `employeeTypedWire.test.ts` `useFlexBalance` facts), R-14's consequences, **the adjudicator's correction of its own cycle-1 Part 4** (R-34: `PriorMonthExportRaceHostTests` is a real class at `PriorMonthExportRaceTests.cs:368`, so the `ApprovalEndpoints.cs:1775` comment is correct and no fold-in was made), **that R-36's cross-domain addition (the in-tx range overload) was verified by the external lens at Step 0b cycle 3 and by Step 5a on 14605's branch** (adjudication c2, "Verification venue"), the **Test Verified** line at C-5. **Model-routing register** S146 row (O-7, C-5).
- **Housekeeping at the S146 open commit:** `.claude/plans/PLAN-s145.md` and `.claude/plans/s145/TASK-*.md` (not `b0/`, `ca1/`, `ca3/`, `cb1/`, `cb5/`), `PLAN-s146.md` and `s146/TASK-*.md`.

---

## Cut order (if the sprint must shrink)

1. **Wave 3 as a unit** (14607 + 14607-T + 14608, the screens) — recorded as a known gap: the card still shows 0.
2. **TASK-14603** (test helpers) — S145 carry-over, re-carried.

**Never cut:** 14601, **14609**, 14604, 14605 (and their paired passes and 14606's pins). **14602** only by the owner (Q-1). **D1–D3** are not cut (they fix a DRIFT); D4–D6 have their deferred branch.

---

## Handoff block (the Orchestrator writes it at every gate into `SPRINT-146.md`)

| | |
|---|---|
| **As of** | <date, master sha> |
| **Phase** | <wave / gate / C-n> |
| **Tasks** | `task · worktree/branch · build · 5α · 5a-R · 5a-C · merged sha` — one row per task, paired pass and fix-up |
| **Track D** | D1 … D7: <state; D4 verdict count> |
| **Pending gates** | <next gate and its open boxes> |
| **CI** | G-0 `39755579330` · **CI-W1** <run, headSha, result> · **CI-W2** <run, headSha, result> · **C-2 (CI-K)** <run, headSha, result> · close <run> |
| **Open rulings** | <id · question · who> |
| **Next action** | <one line> |

---

## Brief contradictions (claim · what the code shows · file:line) — dispositions stand (R-34)

1. **"`db-schema.md` is hand-synced at the T3 merge."** This plan moves the schema to TASK-14601 (wave 1, `data-model` scope), so O-3 runs at 14601's merge. Declared deviation.
2. **The refinement's `IsEmployedOn` citation `PeriodCalculationService.cs:3532`** is a call site; the definition is `:3481`. The briefs cite both.
3. **The refinement's T1 line lists "the rebuilder's C# comment" while R-21 says T1 "declares no cross-domain change".** Settled by R-33 item 1: the Orchestrator makes the comment edit at D7.
4. **A harness DDL copy of `payroll_export_records` exists** (`tests/StatsTid.Tests.Regression/Payroll/PayrollExportRecordsTestSchema.cs:21`, used by 4 files), not covered by the R51 guard. Handled by TASK-14601-T.
5. **`Payroll/Program.cs:70-72` says the host deliberately does not register `IEmploymentWindowResolverInTx`.** R-14's under-lock recheck needs it; 14605 registers it (from the same `EmploymentWindowResolver` singleton, as `Backend.Api/Program.cs:153` does) and rewrites that comment.
6. **"15 HTTP-body sites in 8 files"** counts `tests/StatsTid.Tests.Unit/Orchestrator/OrchestratorScopeEnforcementTests.cs:319`, the Orchestrator pipeline's `weekly-calculation` parameters, not a payroll-route body. The payroll-route bodies are **14 in 7 files**; 14605-T leaves the Orchestrator one.
7. **A non-Docker write guard already exists** for AC-C9's "the Backend never writes the table": `tests/StatsTid.Tests.Regression/HrFollowUp/PayrollExportRecordsWriteGuardTests.cs` (S140). 14607-T reuses it.

## Needs a ruling

None. Draft 2's label question is settled by R-38: `TeamOverview_ProfileReadFails_FallsBackToFullTime_No500` is **N**, unconditional (R-31 stands), with the mutation "remove the catch around the batch profile read (the `awayToday` idiom, `ApprovalEndpoints.cs:996-1015`), so the failure propagates to a 500".

The five drafter's proposals of draft 1 are settled by R-33 with conditions, now in the briefs: (1) the rebuilder comment is the Orchestrator's at D7; (2) `PlanInputs.OpeningFlexProvenance` is optional, non-positional, nullable, **not** in `IsComplete` (`PeriodCalculationService.cs:2253-2260`), compared by `AssertPlanInputsEqual` (`ManifestProjectionRebuildPlanInputsTests.cs:99-105`), and an `UNTRACKED` replay drops `FLEX_PAYOUT` as the forward run did; (3) `NUMERIC` without scale, `balance_provenance TEXT` with a CHECK on the three stored values, the harness copy text-identical; (4) the constructor replacement, with the census of 22 files; (5) the team-row discriminator, a closed string set (`flexBalanceState ∈ {withheld, none, recorded}`) plus the month, never an overloaded null.

---

## Step 0b cycle 1 findings absorbed

| Finding | Disposition |
|---|---|
| Codex BLOCKER — Regression stores cannot enqueue; harness has no `outbox_events` | **R-24:** TASK-14609 (wave 1) adds the table and enqueue-capable stores; 14604-T depends on it |
| Codex BLOCKER — four rebuild/replay-from-`events` facts missed | **R-24:** 14609's census-driven drain calls (command stated; four hits) |
| Codex WARNING — the atomicity F-pin passes today | **R-26:** also asserts zero `events` rows and the 503; positive control `ProjectionInsertSucceeds_OneOutboxRowPerStream` |
| Codex WARNING — labels/owners (harness-parity F; 403s F; submitted response labelled R; AC-C7 owner; `NoManifest`+success only generic) | **R-26** relabels (harness row R; 403s R; submitted F; AC-C7 → 14606); **R-27** predicate covers both route guards |
| Codex WARNING — the deletion grep gate is impossible literally | **R-29:** every grep gate scoped to `src tests frontend/src` |
| Codex WARNING — KB freshness (ADR-018:478) and KB entries per prompt | **R-30:** "KB pasted" column; D7 annotates ADR-018:478 |
| Codex NOTE — main contracts and ordering align | no action |
| Codex NOTE — accept the five proposals | **R-33** |
| Codex NOTE — say "CI-pending" until C-2 | **R-30** (standing constraints, every brief) |
| Reviewer B1 — 14604-T not executable as written (three fix points) | **R-24** (TASK-14609; 14604-T shrinks to M) |
| Reviewer W1 — construction census is 22 files | **R-25:** census command; 22 files authorized |
| Reviewer W2 — atomicity fact passes today | **R-26** |
| Reviewer W3 — drain fact has no host | **R-24:** `PayrollOutboxDrain`, no `PayrollHostFactory` change |
| Reviewer W4 — refusal seam unspecified; name mismatch | **R-27:** one PCS-public predicate; `NoManifestWithSuccess_Refuses` is the contract name |
| Reviewer W5 — how PCS learns `route` | **R-28:** `route` dropped; case 3a is "M ∉ Records" |
| Reviewer W6 — windows read must cover P | **R-28:** own read over `[P.start, M.end]`; `EmployedIn_PreviousMonth_SpellEndsInP_IsTrue` |
| Reviewer W7 — 14608 deletes a hook a test imports | **R-25:** `employeeTypedWire.test.ts` authorized; discriminator fixture ripple authorized |
| Reviewer W8 — fault-isolation pin conditional | **R-31:** 14602 builds the injectable seam; the pin is unconditional (its label is "Needs a ruling" 1) |
| Reviewer W9 — one CI checkpoint concentrates the risk | **R-32:** CI-W1 and CI-W2 plus C-2 |
| Reviewer N1 — the `:1775` fold-in is wrong | **R-34:** 14607 item deleted; the adjudicator's correction recorded in the sprint log |
| Reviewer N2 — "after the last segment" | **R-34:** 14601 names `S138-PROFILE-CATEGORY-NOTNULL-SEGMENT` (`init.sql:5090-5123`) |
| Reviewer N3 — partial-balance fact fails with 42703 today | **R-26** (14601-T Red conditions) |
| Reviewer N4 — `ReRun_UntrackedRawMonth_IsNoOp` holds today | **R-26:** B with the `UNTRACKED` discriminating assertion |
| Reviewer N5 — non-PCS `previousFlexBalance` sites | **R-25:** 14605-T "leave" list |
| Reviewer N6 — `AssertPlanInputsEqual` must compare the new member | **R-33 item 2** (into 14606) |
| Reviewer N7 — master `tsc` red between 3a and 3b | **R-25:** wave 3 stacked; O-2 on 14607's branch |
| Reviewer N8 — comment-only `FlexBalanceUpdated` mentions | **R-29:** reported by 14607 and left |
| Reviewer N9 — stale cite in `PcsNeverSilentTests.cs:145` | **R-34:** 14604-T rewords it |
| Reviewer N10 — "no mutation catalogue" undefined | **R-34:** defined in the summary |
| Reviewer's view on the five items | **R-33** |

---

## Step 0b cycle 2 findings absorbed

*Cycle 2: the internal lens READY (`.claude/reviews/PLAN-s146-reviewer-c2.md`), the external lens NOT-READY (`…-codex-c2.md`); adjudicated R-35 … R-39 (`…-adjudication-c2.md`, RULED, no owner questions). Brief edits only.*

| Finding | Disposition |
|---|---|
| Codex BLOCKER — census instructions force a known STOP (14608's hits; 14606's `AssertPlanInputsEqual` file; 14604-T's two R-22 files) | **R-35:** every census hit classified (a) authorized / (b) expected and unchanged / (c) generated; applied in 14608, 14606, 14604-T; the standing rule restated |
| Codex WARNING — the in-tx windows read has no range method | **R-36:** 14605 adds `GetWindowsAsync(conn, tx, …)` on `IEmploymentWindowResolverInTx` and implements it in `EmploymentWindowResolver` (declared cross-domain); 14606 adds `EmployedIn_InTxAndSelfManaged_Agree` |
| Codex (f) — agree the fault-isolation pin is N | **R-38** |
| Codex nits (14605 `:19` stale references; 14602-T `:24` "each F"; 14606 `:14` first CI evidence) | **R-39** |
| Codex (a), (b), (d), (e), (g) — PASS / verified | no action |
| Reviewer W-A — 14608's census stops on dispatch; two fixtures break `tsc` | **R-35** (the two fixtures authorized for shape edits; `types.ts:39` expected; `api-types.ts` generated) |
| Reviewer W-B — the chain on a PCS without a window resolver | **R-37:** implicit employment stated in 14605 contract 2 and 14605-T Do 4 |
| Reviewer (c) — the open label is N, mutation named | **R-38** |
| Reviewer N1 — `grep -rln` matches binaries | **R-39:** `git grep -l` / `--include='*.cs'` |
| Reviewer N2 — 14605-T's census token is removed by 14604 | **R-39:** token `manifestStore:`; the 22-file list governs |
| Reviewer N3 — LEAVE "lines"; `FrozenInputsSlotTests.cs:106-118` needs only its docstring | **R-39**, **R-37** |
| Reviewer N4 — 14600's grep "final after wave 1"; `ProfileBoundaryHydrationTests` in CI-W1 | **R-39** |
| Reviewer N5 — full interface path | **R-39** (14605) |
| Reviewer N6 — "as named after 14601-T" | **R-39** (plan, 14609) |
| Reviewer N7 — ten post-close commits | no action (consistent) |

## Revision history

| Rev | Date | By | What |
|---|---|---|---|
| draft 1 | 2026-10-23 | `planner` (Opus 5.5) | From refinement rev 3.2 READY: 9 tasks + 5 paired passes (14600–14608), T7 and D1–D7 as Orchestrator rows, the CI checkpoint before Step 7a, the Step 0b verification list, seven brief contradictions and five proposals for the seat |
| draft 2 | 2026-10-23 | `planner` (Opus 5.5) | Applies R-24 … R-34 (Step 0b cycle 1, both lenses NOT-READY): new TASK-14609 (harness outbox readiness, wave 1, never cut; 14604-T shrinks to M and depends on it); the census rule and the 22-file construction census; F/B/R relabels and the atomicity positive control; the R-27 refusal predicate; R-28 (`route` dropped, the `[P.start, M.end]` windows read, the spell-ends-in-P fact); scoped grep gates; the "KB pasted" column, ADR-018:478 in D7, "CI-pending"; 14602's injectable seam; CI-W1 and CI-W2; wave 3 stacked with O-2 on 14607's branch; the five settled items cleared from "Needs a ruling", one new label question (R-31 vs R-26); 14609 ordered after 14601-T (shared file); two 14600 comment sites handed to 14609; the absorbed-findings table |
| draft 2.1 | 2026-10-23 | `planner` (Opus 5.5) | Brief edits only, applying R-35 … R-39 (Step 0b cycle 2: internal READY, external NOT-READY): the census hit classification (a)/(b)/(c) in the standing rule and in 14604-T, 14606, 14608 (and mapped in 14605-T, 14609); 14605's in-tx range overload on the window resolver, its two Infrastructure files and the agreement fact in 14606; the implicit-employment sentence in 14605 and 14605-T; the fault-isolation pin labelled N with its mutation and "Needs a ruling" cleared; the R-39 notes and nits; the cycle-2 absorbed-findings table |
