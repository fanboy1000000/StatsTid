# Sprint 144 — split the verb (QUAL-165) and the payroll guard

| | |
|---|---|
| **Status** | **CLOSED (C-5)** — 15 tasks (8 planned, 2 Step-5a fix-ups, 5 Step-7a fixes), all merged. Step 7a closed at **cycle 4 of 5** on K‴ `4936998`: external **APPROVE**, internal **CLOSE-WITH-WARNINGS**. The two evidence runs and the CI line are recorded by the C-9 follow-up commit. Step 7a, the evidence runs and the CI line are recorded below as they happen |
| **Test Verified** | CI-pending — local: build 0 errors / 145 warnings · Unit 1309 · DemoSeed 170 · Regression non-Docker 128 (2016 facts discovered, the rest Docker-gated) · Smoke 7 discovered · frontend 988 · `tsc --noEmit` clean. The Docker-gated facts are watched in CI at C-5 and the result backfilled here at C-9 |
| **Theme** | A worklist row the system says cannot be recalculated can no longer be *recorded* as recalculated — and an operator who fixed it by hand can now say so truthfully ("Håndteret manuelt"). The real payroll recalculation stops writing wrong wage-type lines after a mid-month agreement-code change, and refuses with a readable reason instead of a bare 500 |
| **Predecessor** | S143 — close `2e7d5b1`; post-close fix `5e78941`; CI backfill `b49f781`; governance chain `fe0bdb1`..`4348731` (model routing refined, the `planner` role, registers QUAL-181/182, the RED-by-mutation method) |
| **Base commit (Step 7a)** | **`2e7d5b1`** — the S143 close commit, not HEAD, so `5e78941` and the whole governance chain fall inside the S144 Step-7a diff (`docs/WORKFLOW.md` step 7a) |
| **Orchestrator model** | Refinement Steps 1–4 and Steps 0a/0b on **Fable 5.1** — from rev 6 of the refinement and draft 1 of the plan, an Opus `planner` DRAFTED and the Fable seat judged (owner ruling 2026-09-29); dispatch, wave merges, contract regeneration and the CI watch planned on **Opus 5.5**; review absorption, Step-5a/7a and every ruling on **Fable**. Running client 2.1.281 (installed 2.1.282; restart due before the first implementer dispatch — step zero). **Open question raised by the owner at dispatch time (2026-09-29): switching the session model clears context in the owner's experience; the switch-point rule is under review — see the sprint log's "Model switch" section once ruled** |
| **Plan** | `.claude/plans/PLAN-s144.md` draft 4 (+ rulings R1–R10) — READY: external lens cycle 3; internal lens cycle 1 APPROVED-WITH-WARNINGS, its warnings absorbed in draft 2 and verified by the external lens (a token-conscious close, stated) |
| **Refinement** | `.claude/refinements/REFINEMENT-s144-qual165-split-the-verb.md` rev 6.1 — READY after two external cycles to NOT READY, a third to READY, an internal pass and confirmation |
| **Owner rulings in force** | QUAL-165 (c) split the verb (2026-09-22) · migration → **"Reseed, keep the cheap segment"** (2026-09-28: *"There is no actual data. The system is not live and we have only test data."*) · OQ-1 `HANDLED_MANUALLY` GlobalAdmin-only on EXPORTED_MONTH (2026-09-29) · OQ-2 *"Defer it, register the follow-up"* → QUAL-181 (2026-09-29) · OQ-4b withdrawn → QUAL-182 · **"Fable judges — Opus drafts"** (2026-09-29): the `planner` role |
| **Entropy scan (Step 0a)** | 2026-09-29 at `4348731`: clean tree except the untracked plan; 0 worktrees; 0 non-master branches; 0 untracked source files |
| **Baseline** | S143 final: build 145 warnings / 0 errors · unit 1290 · demo-seed 170 · regression non-Docker 128 · frontend 976. **Re-measured at the wave-1 gate** by `sprint-test-validation` — never carried forward |

## Plain-language summary

The HR "backdate worklist" lists months that were already sent to payroll and then changed underneath. For some of those months the system *knows* it cannot recalculate correctly — the change falls mid-month, and the calculation engine cannot yet split a month for the rules that look at whole weeks. The screen already hides the "Recalculated" button on those rows; the server never checked. Anyone calling the API directly could record "recalculated" on a month the system itself says cannot be recalculated.

The owner ruled (2026-09-22) not just to block that but to **split the verb**: a third outcome, **"Håndteret manuelt"**, for when a person really did sort the month out by hand. With that available, refusing the false "recalculated" costs nobody anything, so the server refuses it — on the locked row, for every actor the role gate admits. Each resolution's audit event now carries the set of reasons the month was blocked at the moment of the claim; the row projects it. There is no migration of history: no database anywhere holds a pre-S144 row (CI rebuilds from `init.sql` every run; tests seed their own; the owner's machine runs no Docker), so the reseed *is* the migration, and the "ambiguous history" apparatus three review cycles had designed was removed entirely.

While tracing this, the refinement found a live payroll defect: when an employee's **agreement code changes mid-month**, the recalculation and the everyday export compute the days after the change under the old agreement and write the result as if nothing happened — the audit manifest shows an empty cause list. S144 makes the calculation *see* that change and refuse with a 422 that says how many segments and which causes (counts and causes, never dates) instead of today's bare 500. **Operational change HR and payroll must know:** until QUAL-149/150 land, a month with a mid-month agreement-code change cannot be exported through any payroll route: the two calculating endpoints (`/api/payroll/calculate-and-export`, `/api/payroll/recalculate`) refuse it, and so do the two low-level routes that take caller-calculated lines (`/api/payroll/export`, `/export-period`), which re-plan the period since TASK-14410 (owner ruling Q1 = A at the Step-7a close; QUAL-183). When the Rule Engine cannot be reached, all four routes answer 503 and write no payroll data rather than guess (the request itself is still audited) (TASK-14412, QUAL-185). The month goes to the manual path. A refusal beats a wrong payslip. Today's "blocked" badge was correct advice, unenforced; S144 enforces it.

## Step 0b — plan review (both lenses)

| Cycle | External (Codex, inlined source — its sandbox blocks its shell) | Internal (Fable Reviewer) |
|---|---|---|
| 1 (draft 1 + R1–R5) | **NOT READY** — 1 B: the RED check for pins that name new types was specified as "revert the implementation", which after the merge yields compile errors, not a behavioural RED; 4 W: the `Reject` refusal site untested; DISMISSED stamp never asserted; the hook's error-body passthrough implicit; R4 not in the pasted brief | **APPROVED-WITH-WARNINGS** — every citation verified; 5 W: `rule-engine` sent into `Segmentation/**` without a cross-domain label; TASK-14403's brief told the agent to mirror an `effective_to` filter the model read deliberately lacks (under-detection = wrong lines); `db-schema.md` hand-sync placed the column where the generator would not; DISMISSED stamp unassertable; RED evidence unproducible; 7 N |
| 2 (draft 3 + R1–R10) | **NOT READY** — cycle-1 items all RESOLVED; 1 B: pushing both evidence commits publishes only the tip, so run 1 never happens; 3 W: the green spot-check lived in the same test fact as the mutated leg; the close-time ordering of evidence vs Step 7a; cross-domain labels only in the ledger | (absorbed in draft 2; verified by the external lens) |
| 3 (draft 4) | **READY** — all cycle-2 items RESOLVED; close sequence executable and consistent with the guard; 2 N (historical one-run wording; local-branch cleanup on a repeated C-3), both applied | — |

**The converging finding.** Both lenses, by different routes, found the same plan-encoded impossibility: "RED first" for a pin that cannot compile before the implementation exists. The method is now **mutation at the wave gate** (a named, API-preserving mutation per pin, an expected-red list frozen in advance, run in an isolated worktree by a re-spawned `test-qa`) — written into `docs/AGENTS.md` and the `test-qa` definition at `ba3dd76`, since the S141 convention had stopped one step short.

**Rulings by the Fable seat (in the plan, R1–R10).** R1 `AgreementCodeChange` before `EmployeeProfileChange` in the tie-break; R2 null-tolerant optional ctor param, guarded by the host pin that asserts `employedSegmentCount = 2`; R3 base `CREATE` edited plus the S144 segment (the generator reads only `CREATE`); R4 TASK-14407 confirmed, item 4 added; R5 the first `planner` spawn ran as `general-purpose` on `opus` with the definition inlined (resolved `claude-opus-5-5`; the registry loads definitions at session start); R6 the four Docker-gated mutations are *watched* in CI on a throwaway draft PR; R7 the stamp display on resolved rows dropped as unreachable UI (backlog: a "show resolved rows" toggle, `4348731`); R8 two evidence runs, because M-1 and M-3 cancel — a premise of R6 the drafter refuted; R9 "exactly the tests on the frozen expected-red list, nothing else"; R10 Step 7a reads the lists and the diffs, the Fable seat compares the actual runs before the close is sealed. The drafter also corrected the Orchestrator's description of the S143 close: the lenses reviewed `89d3daa`, the parent of the close commit, which is the shape the guard enforces.

## Task ledger

Transcribed from the plan (draft 4). Dispositions are updated at each wave gate; the close guard requires every task accounted for.

| Task | Disposition | Note |
|---|---|---|
| TASK-14400 | DONE — merged `5e2f50b` | wave 1 · `data-model` (Sonnet) · the S144 schema (named CHECK with `HANDLED_MANUALLY`, `resolution_blocked_by`, widened paired constraint, base CREATE + segment) and the event's `BlockedBy` member |
| TASK-14401 | DONE — merged `bffc469` | wave 1 · `test-qa` (Sonnet) · the pins that compile today (endpoint, repository re-fixture to the 1st, schema facts, the S138 migration line) |
| TASK-14402 | DONE — merged `0bdd5a5` | wave 2a · `rule-engine` (Opus, cross-domain authorized: `SharedKernel/**/Segmentation/**`) · `AgreementCodeChange` boundary, tie-break (R1), structured members on the planner's refusal at both sites |
| TASK-14403 | DONE — merged `807f75f` | wave 2b · `payroll-integration` (Opus, cross-domain: Infrastructure repository + tests) · dates-in-period read (no `effective_to` filter), hydration + ctor param (R2), pure 422 mapping, both handlers, Payroll host factory (marker type) |
| TASK-14404 | DONE — merged `01e5bea` | wave 2a · `backend-infrastructure` (Opus, Infrastructure + Backend) · the verb, the refusal on the locked snapshot, the stamp, 409 mapping, gate for both verbs |
| TASK-14405 | DONE — merged `bd4df92`; re-spawn 2 done; re-spawn 3 at close | authored wave 1, merged at the 2b gate, re-spawned at close · `test-qa` (Sonnet) · the pins that need the new API, mutations M-1…M-14, the expected-red lists, the evidence commits E1/E2 |
| TASK-14406 | DONE — merged `24fa0d2` | wave 3 · `ux` (Sonnet) · the screen (third verb, label/toast, 409-blocked and 403 branches reading the parsed error body, fixture aligned) |
| TASK-14407 | DONE — merged `52d577d` | wave 1 · `test-qa` (Sonnet) · S143 carry-over comment fixes (items 4, 5, 7, 8) and item 9 verified (R4) |
| TASK-14408 | DONE — merged `756e050` | Step-5a fix-up · `rule-engine` (Opus) · culture-invariant ISO period in the planner's refusal messages |
| TASK-14409 | DONE — merged `a7426c4` | Step-5a fix-up · `test-qa` (Sonnet) · fencepost facts for `GetEffectiveFromDatesAsync`, real redaction pins, the 403 reason names `HANDLED_MANUALLY`, docstring corrections |
| TASK-14410 | DONE — merged `577afd7` | Step-7a cycle 1 fix, **owner ruling Q1 = A** · `payroll-integration` (Opus) · the raw `/export` and `/export-period` routes re-plan and refuse split months (QUAL-183) |
| TASK-14411 | DONE — merged `f413e9c` | Step-7a cycle 1 fix (A3) · `test-qa` (Sonnet) · Copenhagen/UTC midnight-ordering comments corrected at three sites; non-enumerating repository description |
| TASK-14412 | DONE — merged `f34a353` | Step-7a cycle 2 fix (B1) · `payroll-integration` (Opus) · the rule-list provider throws when unavailable; four routes answer 503 (QUAL-185) |
| TASK-14413 | DONE — merged `ca966c8` | Step-7a cycle 3 fix (C2) · `payroll-integration` (Opus) · provider and interface doc comments corrected |
| TASK-14414 | DONE — merged `52179b5` | Step-7a cycle 3 fix (C2) · `test-qa` (Sonnet) · red-condition comments honest for the in-process harness; the 503 pins assert the fixed body shape (plus the seventh site, a small task at `4936998`) |
| TASK-13701 | DROPPED — not an S144 task | An S137 task, cited in this log only as the precedent for a cross-domain authorization into `SharedKernel/**/Segmentation/**`. It is listed here so that the ledger accounts for every id the log names |

### The plan's ledger, verbatim

| Task | Wave | Role (model by definition) | What | Authorized files | Depends on |
| **TASK-14400** | 1 | `data-model` (Sonnet) | S144 schema: named `hr_backdate_worklist_resolution_check` with `HANDLED_MANUALLY` (placed before `…_resolution_paired`), `resolution_blocked_by TEXT[] NULL` after `resolution_reason`, widened paired constraint — in the base CREATE **and** a marker-bounded S144 segment (R3); event `BlockedBy` member; **reports the final CREATE body verbatim** | `docker/postgres/init.sql` (Orchestrator-approved here); `src/SharedKernel/StatsTid.SharedKernel/Events/BackdateWorklistRowResolved.cs` | — |
| **TASK-14401** | 1 | `test-qa` (Sonnet) | RED pins that compile today (JSON/SQL only), **with the fact names the brief fixes**: endpoint (flip leg 4, leg 5 as its **own fact**, `HANDLED_MANUALLY`, mixed-role incl. leg (b) stamp, stale-412, **DISMISSED-ladder stamp in row + event + audit + resolved GET**, open-row GET `resolutionBlockedBy = null`); repository re-fixture to the 1st (fencepost) + stale-version already-resolved leg; S144 schema fact; S138 legacy fact's stamped `UPDATE` (R3) | `tests/StatsTid.Tests.Regression/Worklist/BackdateWorklistEndpointTests.cs`, `…/Worklist/HrBackdateWorklistRepositoryTests.cs`, `…/Migrations/BackdateWorklistMigrationTests.cs`, new `…/Migrations/BackdateWorklistS144SchemaTests.cs` | — |
| **TASK-14402** | 2a | `rule-engine` (Opus) — **cross-domain authorized: `src/SharedKernel/**/Segmentation/**`** (outside the role's declared scope; S137 TASK-13701 precedent) | `BoundaryCause.AgreementCodeChange` (appended); `BoundarySources.AgreementCodeEffectiveDates`; detector loop **before** `EmployeeProfileChange` (R1, ruled); structured members on `PlannerInvariantViolation` at **both** D4 sites (Reject and AlignedWindow) | `src/SharedKernel/StatsTid.SharedKernel/Segmentation/BoundaryCause.cs`, `…/Segmentation/PeriodPlanner.cs`, `…/Segmentation/BoundaryDetector.cs`, `…/Segmentation/PlannerInvariantViolation.cs` | 14400 merged (wave order only) |
| **TASK-14403** | 2b | `payroll-integration` (Opus) | Dates-in-period read (**no `effective_to` filter**); `PeriodCalculationService` optional null-tolerant ctor param + hydration (R2, ruled); pure mapping `PayrollPlanRefusalProblem.TryCreate`; both handlers → 422; Payroll host factory (marker type) + host-level pins, `/recalculate` pin **not cuttable** | `src/Infrastructure/StatsTid.Infrastructure/UserAgreementCodeRepository.cs` (**cross-domain authorized**), `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs`, `…/Payroll/Program.cs`, new `…/Payroll/Services/PayrollPlanRefusalProblem.cs`, new `tests/StatsTid.Tests.Regression/Payroll/PayrollHostFactory.cs` + `…/Payroll/PayrollHostRecalcBlockedTests.cs` (**cross-domain authorized: tests/**); fallback new `tests/StatsTid.Tests.Smoke/PayrollRecalcBlockedSmokeTests.cs` | 14402 merged |
| **TASK-14404** | 2a | `backend-infrastructure` (Opus) | The verb, the lock-snapshot refusal, the stamp: `WorklistResolutions`, triggers on the locked row, shared derivation core, `BackdateWorklistRecalcBlockedException`, 409 mapping, gate + 403 text for both verbs, `BlockedBy` on the event, `resolution_blocked_by` read/write, DTO member + `ToDto` mapping, audit details | `src/Infrastructure/StatsTid.Infrastructure/HrBackdateWorklistRepository.cs`, `src/Backend/StatsTid.Backend.Api/Endpoints/BackdateWorklistEndpoints.cs`, `…/Contracts/BackdateWorklistResponses.cs`, `…/AuditMappers/BackdateWorklistRowResolvedAuditMapper.cs` (**cross-domain authorized**: Infrastructure + Backend) | 14400 merged |
| **TASK-14405** | authored 1; re-spawned at 2b (second rebase, reconcile; then unit mutation run + expected-list resolution, C-1); merged 2b; re-spawned at close before O-5 (evidence commits E1/E2 from the candidate K, C-3; R6/R8) | `test-qa` (Sonnet) | RED pins that need S144's new API, **each naming its mutation**: repository blocked-`RECALCULATED` / stale-version / `HANDLED_MANUALLY` legs as **three separate facts** (so each Docker mutation is attributable); unit derivation core; `BlockedBy` serialization; audit mapper; planner detector / tie-break / structured members at **Reject and AlignedWindow** / geometric null; mapping function + redaction; service-level live-set payroll pins + the straddle-safe manifest pin. At the 2b gate the re-spawn runs the unit mutations, captures their failures and resolves both runs' expected lists to test names (C-1); at close, before Step 7a, a third spawn commits E1 (M-1 + M-2 + M-14) and E2 (M-3 alone) to the throwaway branch `s144-red-mutations` for the two R6/R8 evidence runs (C-3) | `tests/StatsTid.Tests.Regression/Worklist/HrBackdateWorklistRepositoryTests.cs` (**second touch — merged after 14401**, see note), `tests/StatsTid.Tests.Unit/Worklist/BackdateWorklistDerivationTests.cs`, `…/Unit/Worklist/BackdateWorklistSerializationTests.cs`, `…/Unit/Worklist/BackdateWorklistAuditMapperTests.cs`, new `…/Unit/Segmentation/AgreementCodeBoundaryTests.cs`, new `…/Unit/Payroll/PayrollPlanRefusalProblemTests.cs`, new `tests/StatsTid.Tests.Regression/Payroll/RecalcBlockedLiveRulesetTests.cs`; at the 2b gate only: a throwaway detached worktree (production files mutated there, never committed — `docs/AGENTS.md:171-177`); at close only (R6/R8): the production files named by M-1, M-2, M-3 and M-14, mutated in a throwaway worktree on branch `s144-red-mutations`, committed **to that branch only** (E1, E2), never to master | spec only; completes at the wave-2b gate (re-spawn 3 is evidence, not completion) |
| **TASK-14406** | 3 | `ux` (Sonnet) | The screen: third verb button under the gate, label + toast, dedicated 409-blocked and 403 branches **reading `kind`/`blockedBy` from the parsed error body the hook passes through**, tests driven through the stubbed `fetch` with real server JSON; SETTLED_YEAR fixture aligned. **No stamp display on resolved rows (ruled R7 — the page never shows resolved rows)** | `frontend/src/pages/admin/opfoelgning/WorklistList.tsx`, `frontend/src/hooks/useHrBackdateWorklist.ts`, `frontend/src/pages/admin/opfoelgning/__tests__/WorklistList.test.tsx` | O-2 |
| **TASK-14407** | 1 | `test-qa` (Sonnet) — **confirmed (R4)** | S143 post-close carry-over, comments only: Step-7a items **4** (the "the one instant" comment), **5**, **7**, **8** (fix the false ones), **9** (verify) | `tests/StatsTid.Tests.Regression/Infrastructure/TemporalWriteZeroWidthReopenTests.cs`, `tests/StatsTid.Tests.Regression/**/ProfileCategoryDatingTests.cs`, `…/UserAgreementCodeRepositoryTests.cs`, `…/ProfileUniquenessTests.cs`, `…/WageTypeMappingIdempotencyTests.cs`, `…/WageTypeMappingRaceTests.cs`, `…/TxContractTests.cs` — comments only unless a defect is found and **reported** | — |
| *O-1* | every gate | Orchestrator (Opus) | `dotnet build` 0 errors, warnings ≤ 145; non-Docker unit + regression; from wave 3, `npx tsc --noEmit` + vitest | — | — |
| *O-2* | after 2a | Orchestrator (Opus) | Contract regeneration: `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi` → `docs/api/openapi.json`; `npm run gen:api` → `frontend/src/lib/api-types.ts` (PAT-012) | the two generated files | 14404 merged |
| *O-3* | after 1 | Orchestrator (Opus) | `docs/generated/db-schema.md` hand-synced — a **transcription** of TASK-14400's reported CREATE body in the generator's format (column after `resolution_reason`, before `version`; constraints in body order; whitespace collapsed); CI `--check` arbitrates | `docs/generated/db-schema.md` | 14400 merged |
| *O-4* | close | Orchestrator (Fable for wording, Opus for mechanics) | Registers and docs: QUAL-165 → BUILT; HRP-003 row; legacy-DB runbook row; sprint log `SPRINT-144.md` (header names base `2e7d5b1`; any cut recorded as a **known unpinned change**, N6; the wave-2b gate section carries the frozen expected lists and the unit-mutation table, and **marked placeholders** for the two evidence runs, which C-9's docs-only follow-up commit fills — run ids, head shas, failing test names and assertion messages, and, if `claude-code-review.yml` fired on the draft PR, the sentence that its output was ignored); written before the candidate commit K (C-2); QUAL-181/182, the QUAL-150 correction and the QUAL-165 amendment **already landed at `53f5ebb`**; the R7 backlog line (ROADMAP: a "show resolved rows" toggle for the worklist, UX follow-up) **already landed at `4348731`** — O-4 only confirms it | `docs/**`, `ROADMAP.md` (confirm only) | all merged |
| *O-5* | close | Orchestrator (Fable) | Step 7a, both lenses, base `2e7d5b1`, **reviewed against the candidate commit K** (C-4), with the carried checklist below; the lenses also read the frozen expected lists and the E1/E2 diffs | — | O-4; K (C-2); E1/E2 prepared (C-3) |
| *O-6* | close | Orchestrator (Opus; Fable seat for the comparisons) | **Close sequence C-5..C-8, in that order:** the close commit (docs-only child of K), push, watched CI run on the close sha — the only place the Docker pins go green; then **evidence run 1** with the branch tip at E1 and **run 2** after advancing the same branch to E2, each matched to its commit by `headSha` and compared with its frozen list (**exactly** that list red, nothing else — a red outside it means a mutation was not minimal, a red missing from it means the pin cannot detect it; either way fix and **repeat**, never interpret, R9); then the PR closed unmerged and the branch deleted on origin and locally. `claude-code-review.yml` output on the draft PR, if any, is ignored and said to be ignored | — | O-5; C-3 |
| *O-7* | close | Orchestrator | `docs/operations/model-routing-register.md` row for S144, recording R5 (the first planner spawn ran as `general-purpose` on `opus` with the definition inlined, resolved `claude-opus-5-5`; later planner spawns by role name) — lands in C-9's follow-up commit, since the row records the CI outcome | `docs/**` | O-6 |

## Close sequence (C-1 … C-9), verbatim from the plan

1. **C-1 (wave-2b gate).** TASK-14405 re-spawn 2, after its unit-mutation run, resolves both runs' expected-red and expected-green lists (TASK-14405, "Expected lists") to `FullyQualifiedName`s from master with `dotnet test <project> --list-tests`, and reports any name that does not resolve. **Fable seat:** reviews and freezes the lists (R9). Any later change to them is a Fable ruling.

2. **C-2 (after wave 3's O-1 gate).** O-4 docs written, with the Step-7a outcome and the evidence results left as marked placeholders. Commit **K** on master, message `S144 close: <summary>` — deliberately *without* the phrase "sprint close", so the guard treats it as an ordinary commit (`.claude/hooks/sprint-close-guard.ps1:80-92`; S143's `89d3daa` "S143 close: …" is the precedent). Not pushed.

3. **C-3.** TASK-14405 re-spawn 3 from **K** — if C-3 is being repeated for a replacement K, first remove the previous unpublished evidence worktree and branch (`git worktree remove <path>`; `git branch -D s144-red-mutations`) and recreate them from the new K; once the branch has been published, corrections are fast-forwards on the tip instead (C-6): **E1** = K + M-1 + M-2 + M-14; **E2** = a child of E1 whose tree is K + M-3 alone. `dotnet build` 0 errors and the non-Docker suites green **at E1, and again at E2**. Reports both shas and `git diff K E1` / `git diff K E2` verbatim. Nothing pushed.

4. **C-4 — O-5 Step 7a.** Both lenses, base `2e7d5b1`, `reviewed-against-commit: <K>`. The lenses read the frozen lists (C-1), re-spawn 2's mutation table, and both evidence diffs (C-3). A code-touching Step-7a fix makes a new **K** → repeat C-3 on it and run the verification cycle `docs/WORKFLOW.md` step 7a requires.

5. **C-5 — close commit + O-6.** `S144 sprint close — …`: a docs-only child of the reviewed K (the Step-7a outcome in `SPRINT-144.md`), which is what the guard checks (`reviewed-against-commit` must prefix HEAD, `sprint-close-guard.ps1:170-183, 273`). Confirm `git diff --stat K HEAD` lists only `docs/**`. Push master; watch the CI run on the close sha to green. If it is red: a post-close fix (with its own Step-7a cycle, `docs/WORKFLOW.md` step 7a "Post-Step-7a coverage"), and C-3 is repeated on the fix sha before C-6 (the fix sha then takes K's role in C-6..C-9, and the evidence diffs are re-read against it by that fix's Step-7a cycle).

6. **C-6 — evidence run 1.** `git push origin <E1>:refs/heads/s144-red-mutations` (E1 is the branch tip; E2 is not on origin). Open the **draft** PR to `master`, body first line "EVIDENCE ONLY — deliberate mutations for S144 RED evidence; never merge; closed and deleted when both runs report". Watch to completion; confirm the run's `headSha` equals E1 (`gh run list --branch s144-red-mutations --json databaseId,headSha,event,conclusion`). *Two things to know:* CI on a pull request tests the PR merged into `master`, which here adds only the close commit's `docs/**` change, so the tested code is K + the mutations; and these are `pull_request` runs, so their deliberate red cannot reach the guard's CI-health check, which reads only `push` runs on `master` (`sprint-close-guard.ps1:316`). **Fable seat (R10):** compare the red set to the frozen run-1 list. Mismatch → re-spawn 3 adds a corrected commit on the tip (tree = K + corrected M-1/M-2/M-14; C-3 checks), push as a fast-forward, repeat run 1 — never interpret; E2 is then re-made as its child (same tree, K + M-3).

7. **C-7 — evidence run 2.** Only after run 1 matches: `git push origin <E2>:refs/heads/s144-red-mutations` — a fast-forward, which fires the PR's `synchronize` event (`.github/workflows/ci.yml:6-7`). Watch; confirm `headSha` equals E2; **Fable seat:** compare to the frozen run-2 list; a mismatch is handled as in C-6.

8. **C-8.** Close the PR unmerged; `git push origin --delete s144-red-mutations`; `git branch -D s144-red-mutations`; confirm the branch is gone on both sides.

9. **C-9 — docs-only follow-up commit (seals the close, R10).** Never an amend of the close commit. It records: the CI-green line for the close sha; per run, the run id, the head sha (E1 / E2), each red test with its assertion message against the frozen list, "matched"; the PR number and "closed unmerged, branch deleted"; that `claude-code-review.yml` output, if any, was ignored; the O-7 routing row. *How it relates to what was tested:* it touches only `docs/**`, so the code it sits on is exactly the close sha's; E1 and E2 are K plus the mutations only, and K differs from the close commit only in `docs/**` (checked in C-5). Push; watch green; teardown.

## Step 7a — carried checklist, verbatim from the plan


From the external verdict:
- [ ] (1) the recreation assertions in `TemporalWriteZeroWidthReopenTests` are non-vacuous — each can fail;
- [ ] (2) `EmployeeProfileRepository` actually consumes the injected `FixedTimeProvider` on the write path the test exercises (the review saw the injection, not the consumption);
- [ ] (3) Danish-calendar boundary coverage for this write path exists (cite it) or is added;
- [ ] (4) the "the one instant" comment is corrected. *(Fixed in TASK-14407 per R4 — Step 7a verifies the fix.)*

From the internal (Fable) floor review of 2026-09-24, unlanded:
- [ ] (5) `TemporalWriteZeroWidthReopenTests.cs:144-150` justifies the agreement-code fact by a counterfactual about `SoftDeleteAsync` on the agreement-code repository, which has no such method — rewrite the comment; *(TASK-14407)*
- [ ] (6) this log's row at `:596` classified the same test as "no oracle" — corrected in this commit (see the appendix row); *[Orchestrator gloss: "this log" is S143's wording, carried verbatim — the row is `SPRINT-143.md:597` ("corrected 2026-09-25"); `:596` is the `TxContractTests` row above it.]*
- [ ] (7) `ProfileCategoryDatingTests.cs:91-93, 185-186` claim the date is "never compared against an independently-computed server clock"; the claim is false — the comparison exists — with no live failure; correct the comment (the false clause is about *comparison*, not where the date comes from). *(TASK-14407)*

From the Opus comparison arm, **not floor-verified**:
- [ ] (8) the same "never compared against an independently-computed server clock" comment stands at nine sites in five files (re-counted 2026-09-25: `ProfileUniquenessTests.cs:93`, `WageTypeMappingIdempotencyTests.cs:105`, `WageTypeMappingRaceTests.cs:103,221,306`, `ProfileCategoryDatingTests.cs:93,186`, `UserAgreementCodeRepositoryTests.cs:131,206`); the arm's census — not floor-verified — calls four of them false: the two in item 7 and `UserAgreementCodeRepositoryTests.cs:131,206`, which is the only new file name here; verify the four, fix or refute; *(TASK-14407)*
- [ ] (9) `TxContractTests.cs:766` passes `today = 2025-03-12` as the close date; the Opus arm read the resulting interval as backwards. Verify the interval the soft-delete actually produces — the mapping comes from `NewWageTypeMapping`, which never sets `EffectiveFrom`, so the cycle-4 reviewer expects `[0001-01-01, 2025-03-12)`, forward, and the item is likely closable as "no defect". *(TASK-14407)*

Plus the S144 scope itself (the diff from `2e7d5b1`): the worklist refusal and verb, the schema segment, the payroll guard, the screen, and the governance chain `fe0bdb1`..`53f5ebb` (docs-only, already dual-lens reviewed per `SPRINT-143.md` post-close section — Step 7a confirms the S144 record does not contradict it). Step 7a also reads TASK-14405 re-spawn 2's mutation table, the **frozen expected lists** (C-1) and the **E1/E2 diffs against K** (C-3). *Sequencing (ruled R10, made executable in draft 4):* Step 7a reviews the candidate commit **K** (C-4); by then every input it reads exists, because the lists are frozen at the wave-2b gate and the evidence commits are prepared from K before Step 7a starts. The runs' actual red sets do not exist yet and are not Step-7a inputs: the Fable seat compares them with the frozen lists at C-6/C-7, and C-9's docs-only follow-up commit records the comparison — that commit, not the close commit, seals the close. See the close sequence for the full order.


## Orchestrator-owned docs at close (O-3, O-4, O-7)

- `docs/generated/db-schema.md` — a transcription of TASK-14400's reported `CREATE` body in the generator's format (no Python on this machine; CI `--check` arbitrates; S138 precedent).
- `docs/operations/legacy-db-upgrade-runbook.md` — S144 row: the segment; "pre-S144 databases are reseeded, not upgraded (ADR-038 D9)"; the widened paired constraint refuses to add on a DB holding a resolved pre-S144 row.
- `docs/operations/hr-follow-up-process-register.md` HRP-003 — the new outcome, the Global Admin gate on both verbs, the export refusal for mid-month agreement-code months (quoting the 422 body).
- `docs/operations/quality-finding-register.md` — QUAL-165 → BUILT (amendment already at `53f5ebb`); QUAL-181/182 and the QUAL-150 correction already landed.
- `docs/operations/model-routing-register.md` — the S144 row (R5; resolved ids per tier; the planner's effect on Fable spend).
- Known unpinned change if the cut order's item 1 is taken: the `/calculate-and-export` handler's 422 mapping (covered at service level only) — recorded here, not described as covered.
- The impossible frontend fixture (a blocked SETTLED_YEAR row, `WorklistList.test.tsx:225-241`) is replaced by one the backend can produce — a premise change, said so.

## Routing note (for the register row)

`planner` spawns: the first as `general-purpose` on `opus` with the definition inlined (registry not yet loaded), resolved `claude-opus-5-5`; drafts 2–4 by role name, guard `ALLOW opus-tier role`. Reviewer spawns on `claude-fable-5-1` throughout. The refinement's five revisions before the planner existed were drafted on Fable (634 Fable messages to 97 Opus in that stretch) — the number that prompted the owner's question and the ruling.

## Model switch — ruled at dispatch time (2026-09-29)

At the first dispatch the owner raised that switching the session model clears their context, and ruled: *"I want a setup so I dont have to switch models."* The seat therefore never switches: future sessions start on Opus; the Fable work is done by agents — the `reviewer` (unchanged) and the new `adjudicator` (judgment: rulings and the owner's questions, read-only, floor-enforced by the guard). The `planner` (Opus) drafts. `docs/WORKFLOW.md` § Model Routing, "the seat never switches"; `docs/AGENTS.md` roster. This session stays on Fable to its end. *It did: that Fable session ended after the plan reached READY at Step 0b and the handoff block was written. A new session on Opus 5.5 (client 2.1.284) ran from the first wave-1 dispatch through the close, per the ruling. Every section below marked "(Opus seat)" is that session; its Fable judgment came from the `reviewer` and `adjudicator` agents (C-1, Step 5a, Step 7a).* The `**Orchestrator model**` row above is read accordingly: no switch happened or will happen in this session.

## Wave-1 gate (2026-09-29, Opus seat)

**In plain language.** The database now accepts the third outcome and requires every resolution to record its block set; the tests that define "done" for the worklist half are on master (they fail against today's code by design, and can only run in CI because they need a database); four false comments from S143's review are corrected; the tests that need wave 2's new code are written and waiting in their own branch. Nothing is broken on master: it builds, and every test that can run on this machine passes at exactly S143's counts.

| Task | Merged | Result |
|---|---|---|
| TASK-14400 | `2629098` via `5e2f50b` | Base CREATE + S144 segment as briefed; event `BlockedBy`. Reported the CREATE body verbatim → O-3 transcribed at `4611fd6`. No brief contradictions |
| TASK-14407 | `581c762` via `52d577d` | Items 4, 5 and the four false site-comments of 7/8 fixed (`ProfileCategoryDatingTests.cs:93,186`, `UserAgreementCodeRepositoryTests.cs:131,206`); five sites true, left; item 9 no defect (`[0001-01-01, 2025-03-12)`, forward). Also corrected an adjacent false clause: `TemporalWriteRouter.Decide` takes `today` but no branch reads it (`TemporalWriteRouter.cs:160`) |
| TASK-14401 | `c74a6f0` + `16bd382` (step 3b, re-spawned — the first return left it undone) via `bffc469` | All seven steps; fact names exactly as briefed (one rename, as briefed). Orchestrator addition: the migration test's unknown-verb `UPDATE` (`:185`) also stamps `'{}'`, so only the verb CHECK can refuse it — without it the widened paired CHECK would also fire and the 23514 would no longer isolate the verb check. Four schema facts in `BackdateWorklistS144SchemaTests` |
| TASK-14405 | not merged (by design) | 24 pins authored at `8565842`; rebased onto `bffc469` cleanly (now `6f9676d`); remaining compile errors (Unit 35, Regression 16) all attributed to missing S144 members. Derivation facts use `PROFILE_CHANGE` triggers because `AGREEMENT_CODE_CHANGE` maps to `QUAL-150`, not `QUAL-149` |

**O-1:** `dotnet build` 0 errors / **145** warnings (ceiling 145) · Unit **1290** · DemoSeed **170** · Regression non-Docker **128** — all green, identical to S143 final (the new pins are all Docker-gated) · frontend 976 (not re-run; no frontend change yet). Step zero: the Sonnet tier resolved **`claude-sonnet-5-5`** (all three wave-1 agents' self-reports).

**Observations for Step 7a (raised by TASK-14407, outside its scope — not fixed):**
- The four re-worded tests do not pin the test date and the repository's clock together: the repository uses the real Copenhagen "today", the test the UTC day, and they differ in the last 1–2 hours of every UTC day — a latent CI flake window, now stated honestly in the comments but not closed.
- `tests/StatsTid.Tests.Regression/Hosting/FixedTimeProvider.cs:18-27` says UTC midnight keeps the derivations in agreement "for every hour of the calendar day" — true of the pinned instant, imprecise as written.

## Wave-2a gate (2026-09-29, Opus seat)

**In plain language.** The planner now treats a mid-month agreement-code change as a place where the month would have to be split — which, for the live rules, means it refuses rather than paying the month under the wrong agreement — and its refusal carries machine-readable facts (which rule, how many pieces, which kinds of change) instead of only a sentence. The worklist server now refuses "recalculated" on a month it knows it cannot recalculate, accepts the honest "handled manually", and records the block set on every resolution. The API contract the screen is built against was regenerated.

| Task | Merged | Result |
|---|---|---|
| TASK-14402 | `c433f35` via `0bdd5a5` | As briefed; `DescribeInteriorBoundaryCauses` (string) replaced by `InteriorBoundaryCauses` (list) with the message text unchanged. TASK-14405's six `AgreementCodeBoundaryTests` ran green against it (copied in temporarily, not committed). Not-named site, no change needed: `init.sql:2064-2066` comment lists example cause values (could mention `AgreementCodeChange`) |
| TASK-14404 | `ff95d7e` via `01e5bea` | As briefed. **Brief contradiction 1, resolved by the pin:** the resolve 200 response also carries `resolutionBlockedBy` (`string[]`), because TASK-14401's `Resolve_SettledYear_AsHandledManually_Hr200_StampsEmptySet` reads it from the POST body — an additive contract change beyond step 6, flagged for Step 7a. **Contradiction 2:** `ResolutionBlockedBy` is a trailing optional parameter on `HrBackdateWorklistRow` so TASK-14405's positional builders compile. Verb-list enumerations reported, not edited: the frontend (TASK-14406's scope), `api-typed-overloads.test.ts:463-464` and `EventSerializer.cs:218` (comments). TASK-14405's worklist unit pins ran 75/75 against it in a throwaway worktree |
| O-2 | `0ec35d3` | `openapi.json` + `api-types.ts` regenerated: `BackdateWorklistRow.resolutionBlockedBy: string[] \| null`, resolve response `resolutionBlockedBy: string[]`. `npx tsc --noEmit` exit 0 |

**O-1:** build 0 errors / **145** warnings · Unit **1290** · DemoSeed **170** · Regression non-Docker **128** — all green. Step zero: the Opus implementer tier resolved **`claude-opus-5-5`** (14402 and 14404 self-reports).

## Wave-3 gate and TASK-14403 merge (2026-09-29, Opus seat)

**In plain language.** HR's screen now offers "Håndteret manuelt" where the server allows it, labels and toasts all three outcomes, and tells the truth when the server refuses — naming the blocking register ids on a 409-blocked, and giving a permissions message on a 403 instead of a raw error. The payroll recalculation and the everyday export now load agreement-code change dates into the plan, so a mid-month change is refused with a readable 422 rather than paid under the wrong agreement or failed with a bare 500.

| Task | Merged | Result |
|---|---|---|
| TASK-14406 | `dcddfdc` via `24fa0d2` | As briefed. `WorklistList.test.tsx` 8 → 20 (one impossible fixture replaced, 12 added); the 409/403 tests drive the real server body through the stubbed `fetch`. **Red condition observed**, not only argued: the hook returning `{ ok, error, status }` without `body` failed the 409-blocked test at `WorklistList.test.tsx:348` (`expected 'Sagen er allerede løst af en anden. L…' to contain 'QUAL-149, QUAL-150'`), 19/20 green. Choices beyond the brief: a 409-blocked reloads the list; the 403 message is fixed text (does not show `body.reason`) |
| TASK-14403 | `c54da00` via `807f75f` | As briefed; the Regression host factory landed (no Smoke fallback) — the host boots in-process, confirmed by a local probe (`/health` 200 with an unreachable DB; since deleted). TASK-14405's `PayrollPlanRefusalProblemTests` ran 3/3 against it (copied in temporarily, not committed) |

**The 422 body (both endpoints) — for HRP-003 and the register:**
```json
{"success":false,"error":"This period cannot be calculated automatically: a change inside the period splits it into segments, and a whole-period rule cannot be evaluated in separate segments. The period must be handled manually.","kind":"payroll-recalc-blocked","employedSegmentCount":2,"interiorBoundaryCauses":["AgreementCodeChange"],"ruleId":"OVERTIME_CALC"}
```

**O-1 (wave 3, after both merges):** build 0 / **145** · Unit **1290** · DemoSeed **170** · Regression non-Docker **128** · `npx tsc --noEmit` exit 0 · vitest **988** (976 → +12), 81 files — all green.

**Observations for Step 7a (from TASK-14403, not fixed):**
- **Payroll host DI gap (pre-existing):** `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:112` registers `ConfigResolutionService` without `AgreementConfigRepository` / `PositionOverrideRepository`, which its constructors need. Nothing in the host resolves it, so Production hides it; Development-mode startup validation refuses to build the host. `PayrollHostFactory` therefore runs the host in `Environments.Production` (which is also what compose runs). Needs a ruling: remove the unused registration, or register the two repositories — a quality-register candidate.
- **Zero-width agreement-code closes count as boundaries** (the "no exclusion" contract): a month holding one is refused — the safe direction, but visible behaviour.

## Wave-2b gate — TASK-14405 merged, unit mutations observed (2026-09-29, Opus seat)

**In plain language.** The tests that describe S144's new behaviour are merged, and each one was proved able to fail. For every test that runs without a database, a tester broke exactly the one behaviour it guards — in a throwaway copy, never committed — and watched it turn red. All ten did. The four database-backed breakages can only be watched in CI; they wait for the close.

- **Re-spawn 1:** rebased onto `807f75f` with no conflicts and **no name reconciliation needed** (empty marker commit `53c07f4` on authoring `ec46741`); merged `bd4df92`. O-1: build 0 / **145** · Unit **1308** (1290 + 18 pins) · DemoSeed **170** · Regression non-Docker **128**.
- **Re-spawn 2 — unit mutation table** (detached worktree from master, each mutation alone, restored, removed; un-mutated Unit 1308/1308 in that checkout). **Every mutation tripped its named pin.** The "also red" column is the whole-project run, so the reader can judge minimality:

| Id | Pin | Captured failure | Also red (whole Unit run) |
|---|---|---|---|
| M-4 | `RecalcBlockedBy_FactoredOverload_SettledYearKind_Empty_EvenWithYearMonthAndInteriorTrigger` | `Assert.Empty() Failure: Collection was not empty` | — |
| M-5 | `EventSerializer_RoundTrip_BackdateWorklistRowResolved_BlockedBy_SetEmptyAndNull` | `Assert.NotNull() Failure: Value is null` | `…_JsonWithoutBlockedBy_YieldsNull` (sibling pin) |
| M-6 | `RowResolved_DetailsCarryBlockedBy_EqualToTheEventsSet` | `Assert.True() Failure` (`TryGetProperty("blockedBy")` false) | `…_EmptySetIsAnEmptyArray` (sibling pin) |
| M-7 | `Detect_AgreementCodeDate_OnPeriodStart_IsNotABoundary` | `Assert.Empty() Failure: Collection was not empty` | — |
| M-8 | `Detect_AgreementCodeAndProfileChangeShareADate_RecordsAgreementCodeChange` | `Assert.Equal() Failure — Expected: AgreementCodeChange, Actual: EmployeeProfileChange` | — |
| M-9 | `Plan_MidPeriodAgreementCodeDate_AlignedWindowRule_ThrowsWithStructuredSplitRefusalMembers` | `Assert.True() Failure` (`IsSplitRefusal`) | — |
| M-10 | `Plan_MidPeriodAgreementCodeDate_RejectRule_ThrowsWithStructuredSplitRefusalMembers` | `Assert.True() Failure` (`IsSplitRefusal`) | two `PayrollPlanRefusalProblemTests` split pins (they build their exception at the same Reject site) |
| M-11 | `Plan_GeometricViolation_LeavesStructuredMembersNullOrEmpty_NotASplitRefusal` | `Assert.False() Failure` (`IsSplitRefusal`) | `TryCreate_NonSplitViolation_ReturnsNull` (same behaviour) |
| M-12 | `TryCreate_SplitRefusal_MapsCountCausesAndRuleId` | `Assert.NotNull() Failure: Value is null` | `…_ContainsNoDateAndNoEmployeeId` (its own NotNull precondition) |
| M-13 | `TryCreate_SplitRefusal_SerializedProblem_ContainsNoDateAndNoEmployeeId` | `Assert.DoesNotMatch() Failure: Match found` | — |

Applied as: M-11 `EmployedSegmentCount = 0;` in the message-only ctor body; M-13 `Error` made `init` and set to `ex.Message` in `TryCreate`.

- **Expected lists (C-1):** every entry resolved to a `FullyQualifiedName` on `bd4df92`. The resolver mapped the plan's run-2 spot check "the DISMISSED ladder" to the two facts with "Dismiss" in their names. The Orchestrator reads TASK-14401 step 3a as naming `Resolve_MissingIfMatch_428_Stale_412_…` instead, and put that question to the Fable `adjudicator`. **The frozen lists: see "C-1 — expected lists frozen" below.**

## C-1 — expected lists frozen (Fable `adjudicator`, `claude-fable-5-1`, 2026-09-29)

**In plain language.** The two close-time evidence runs are a controlled experiment: break the code in a known way and check that exactly the tests meant to notice do notice, and nothing else moves. The adjudicator read every listed test end to end against master and traced each run's breakage through it. Every red trips at a reachable assertion, and every listed green survives. No unlisted test can be reached. The payroll breakage only bites where the agreement-code repository is wired in, and the only places that happens are the two S144 payroll classes. The worklist breakages live in one repository method called only by the two worklist test classes. There was one correction: "the DISMISSED ladder" in the plan (`PLAN-s144.md:156, :367`) is `Resolve_MissingIfMatch_428_Stale_412_…`, not the two facts with "Dismiss" in their names. It is added to run 2's greens, where it proves the moved block check never touches the verb it must not refuse.

Rulings: **R9-1** Run1-RED frozen as given · **R9-2** Run1-GREEN frozen as given · **R9-3** Run2-RED frozen as given · **R9-4** Run2-GREEN frozen **with the DISMISSED ladder added** (the two "Dismiss" facts stay; the wildcards are expanded to six FQNs) · **R9-5** completeness: no additions (only `RecalcBlockedLiveRulesetTests.cs:492` passes `userAgreementCodeRepo:`; the compose employee's agreement row starts `0001-01-01`, so the Smoke job adds no red) · **R9-6** mutation precision for re-spawn 3:
- **M-1:** delete `HrBackdateWorklistRepository.cs:1088-1089` only.
- **M-2:** replace `blockedBy` with `Array.Empty<string>()` at `:1092` and `:1109`.
- **M-3:** move `:1082-1089` to immediately after `:1073`, a pure swap with the version guard.
- **M-14:** pass `AgreementCodeEffectiveDates: null` at `PeriodCalculationService.cs:1029` at `14ab629` (`:1070` at K′ `203fefd`; `:1072` at K″, after TASK-14412's doc edits above it; re-spawn 3 re-reads the line per commit).

No owner questions. **Any change to these lists from here on is a Fable ruling.**

All names are prefixed `StatsTid.Tests.Regression.`.

**Run 1 — E1 (M-1 + M-2 + M-14) — RED, exactly these:**
```
Worklist.HrBackdateWorklistRepositoryTests.Resolve_Recalculated_OnBlockedRow_ThrowsRecalcBlocked_WritesNothing  [M-1]
Worklist.BackdateWorklistEndpointTests.Resolve_ExportedMonth_AsRecalculated_Hr403_LocalAdmin403_GlobalAdmin409Blocked_ForeignHrStillScope403  [M-1]
Worklist.HrBackdateWorklistRepositoryTests.Resolve_HandledManually_OnBlockedRow_StampsBlockSetOnRowAndEvent  [M-2]
Worklist.BackdateWorklistEndpointTests.Resolve_ExportedMonth_AsHandledManually_Hr403_LocalAdmin403_GlobalAdmin200_StampsBlockSet  [M-2]
Worklist.BackdateWorklistEndpointTests.Resolve_MissingIfMatch_428_Stale_412_Fresh_200WithNewEtag_Repeat_409_OpenFilterHonoured  [M-2]
Worklist.BackdateWorklistEndpointTests.Resolve_ExportedMonth_AsRecalculated_MixedRoleHrWithGlobalAdminScope_Is403_ButMayStillDismiss  [M-2]
Payroll.RecalcBlockedLiveRulesetTests.Recalculate_MidMonthAgreementCodeChange_LiveSet_RefusesWithAgreementCodeCause_WritesNothing  [M-14]
Payroll.RecalcBlockedLiveRulesetTests.CalculateWithOutcome_Planless_MidMonthAgreementCodeChange_LiveSet_Refuses_NoManifest  [M-14]
Payroll.RecalcBlockedLiveRulesetTests.Calculate_MidMonthAgreementCodeChange_StraddleSafe_ManifestRecordsAgreementCodeChange  [M-14]
Payroll.PayrollHostRecalcBlockedTests.Recalculate_MidMonthAgreementCodeChange_Returns422_RedactedProblem_LinesUnchanged  [M-14]
Payroll.PayrollHostRecalcBlockedTests.CalculateAndExport_MidMonthAgreementCodeChange_Returns422_NoExportRecord_NoManifest  [M-14]
Payroll.PayrollHostRecalcBlockedTests.Export_MidMonthAgreementCodeChange_Returns422_RedactedProblem_NoExportRecord  [M-14; ADDED at Step 7a, A5 — TASK-14410]
Payroll.PayrollHostRecalcBlockedTests.ExportPeriod_MidMonthAgreementCodeChange_Returns422_RedactedProblem_NoExportRecord  [M-14; ADDED at Step 7a, A5 — TASK-14410]
```
**Run 1 — GREEN spot checks** (everything not on RED must pass; these are read deliberately):
```
Worklist.HrBackdateWorklistRepositoryTests.Resolve_Recalculated_OnBlockedRow_StaleVersion_ThrowsConcurrencyBeforeBlock
Worklist.BackdateWorklistEndpointTests.Resolve_ExportedMonth_Blocked_AsRecalculated_StaleIfMatch_Is412_Not409
Worklist.BackdateWorklistEndpointTests.Resolve_ExportedMonth_TriggerOnFirstOfMonth_AsRecalculated_GlobalAdmin200_StampsEmptySet
Worklist.BackdateWorklistEndpointTests.Resolve_SettledYear_AsHandledManually_Hr200_StampsEmptySet
Worklist.HrBackdateWorklistRepositoryTests.Resolve_VersionGuard_StaleThrows_FreshBumpsVersion_EmitsResolvedEvent_RepeatRefused_UnknownNotFound
Worklist.BackdateWorklistEndpointTests.Get_WithEmployeeId_GlobalAdmin_ReturnsTypedRow_WithKeysTriggersDerivedFieldsAndVersion
Payroll.RecalcBlockedLiveRulesetTests.Recalculate_MidMonthProfileChange_LiveSet_StillRefuses_NoRegression
Migrations.BackdateWorklistS144SchemaTests.OpenRow_WithAStamp_IsRefused_23514_NonEmptyAndEmpty
Migrations.BackdateWorklistS144SchemaTests.ResolvedRow_HandledManually_WithStamp_IsAccepted_NonEmptyAndEmpty
Migrations.BackdateWorklistS144SchemaTests.ResolvedRow_UnknownVerb_IsRefused_23514_ByTheNamedVerbCheck
Migrations.BackdateWorklistS144SchemaTests.ResolvedRow_WithNullStamp_IsRefused_23514_OnEveryVerb
Payroll.PayrollHostRecalcBlockedTests.Export_MonthWithoutInteriorChange_StillExports_200   [ADDED at Step 7a, A5 — TASK-14410 no-regression]
Payroll.PayrollHostRecalcBlockedTests.Export_RulesUnavailable_Returns503_NoExportRecord   [ADDED at Step 7a, B1 — TASK-14412]
Payroll.PayrollHostRecalcBlockedTests.ExportPeriod_RulesUnavailable_Returns503_NoExportRecord   [ADDED at Step 7a, B1 — TASK-14412]
Payroll.PayrollHostRecalcBlockedTests.CalculateAndExport_RulesUnavailable_Returns503_NoExportRecord_NoManifest   [ADDED at Step 7a, B1 — TASK-14412]
Payroll.PayrollHostRecalcBlockedTests.Recalculate_RulesUnavailable_Returns503_LinesUnchanged   [ADDED at Step 7a, B1 — TASK-14412]
```
**Run 2 — E2 (M-3 alone) — RED, exactly these:**
```
Worklist.HrBackdateWorklistRepositoryTests.Resolve_Recalculated_OnBlockedRow_StaleVersion_ThrowsConcurrencyBeforeBlock  [M-3]
Worklist.BackdateWorklistEndpointTests.Resolve_ExportedMonth_Blocked_AsRecalculated_StaleIfMatch_Is412_Not409  [M-3]
```
**Run 2 — GREEN spot checks:**
```
Worklist.HrBackdateWorklistRepositoryTests.Resolve_Recalculated_OnBlockedRow_ThrowsRecalcBlocked_WritesNothing
Worklist.BackdateWorklistEndpointTests.Resolve_ExportedMonth_AsRecalculated_Hr403_LocalAdmin403_GlobalAdmin409Blocked_ForeignHrStillScope403
Worklist.HrBackdateWorklistRepositoryTests.Resolve_HandledManually_OnBlockedRow_StampsBlockSetOnRowAndEvent
Worklist.BackdateWorklistEndpointTests.Resolve_ExportedMonth_AsHandledManually_Hr403_LocalAdmin403_GlobalAdmin200_StampsBlockSet
Worklist.BackdateWorklistEndpointTests.Resolve_MissingIfMatch_428_Stale_412_Fresh_200WithNewEtag_Repeat_409_OpenFilterHonoured   [ADDED at C-1 — the DISMISSED ladder]
Worklist.BackdateWorklistEndpointTests.Resolve_ExportedMonth_AsRecalculated_MixedRoleHrWithGlobalAdminScope_Is403_ButMayStillDismiss
Worklist.BackdateWorklistEndpointTests.Resolve_Hr_MayDismissExportedMonth_AndMayRecalculateSettledYear
Worklist.HrBackdateWorklistRepositoryTests.Resolve_VersionGuard_StaleThrows_FreshBumpsVersion_EmitsResolvedEvent_RepeatRefused_UnknownNotFound
Payroll.RecalcBlockedLiveRulesetTests.Recalculate_MidMonthAgreementCodeChange_LiveSet_RefusesWithAgreementCodeCause_WritesNothing
Payroll.RecalcBlockedLiveRulesetTests.Recalculate_MidMonthProfileChange_LiveSet_StillRefuses_NoRegression
Payroll.RecalcBlockedLiveRulesetTests.CalculateWithOutcome_Planless_MidMonthAgreementCodeChange_LiveSet_Refuses_NoManifest
Payroll.RecalcBlockedLiveRulesetTests.Calculate_MidMonthAgreementCodeChange_StraddleSafe_ManifestRecordsAgreementCodeChange
Payroll.PayrollHostRecalcBlockedTests.Recalculate_MidMonthAgreementCodeChange_Returns422_RedactedProblem_LinesUnchanged
Payroll.PayrollHostRecalcBlockedTests.CalculateAndExport_MidMonthAgreementCodeChange_Returns422_NoExportRecord_NoManifest
Payroll.PayrollHostRecalcBlockedTests.Export_MidMonthAgreementCodeChange_Returns422_RedactedProblem_NoExportRecord   [ADDED at Step 7a, A5 — TASK-14410]
Payroll.PayrollHostRecalcBlockedTests.ExportPeriod_MidMonthAgreementCodeChange_Returns422_RedactedProblem_NoExportRecord   [ADDED at Step 7a, A5 — TASK-14410]
Payroll.PayrollHostRecalcBlockedTests.Export_MonthWithoutInteriorChange_StillExports_200   [ADDED at Step 7a, A5 — TASK-14410 no-regression]
Payroll.PayrollHostRecalcBlockedTests.Export_RulesUnavailable_Returns503_NoExportRecord   [ADDED at Step 7a, B1 — TASK-14412]
Payroll.PayrollHostRecalcBlockedTests.ExportPeriod_RulesUnavailable_Returns503_NoExportRecord   [ADDED at Step 7a, B1 — TASK-14412]
Payroll.PayrollHostRecalcBlockedTests.CalculateAndExport_RulesUnavailable_Returns503_NoExportRecord_NoManifest   [ADDED at Step 7a, B1 — TASK-14412]
Payroll.PayrollHostRecalcBlockedTests.Recalculate_RulesUnavailable_Returns503_LinesUnchanged   [ADDED at Step 7a, B1 — TASK-14412]
```

## Step 5α / 5a — run after merge (a process miss, corrected before the candidate commit)

**What happened, plainly.** `docs/WORKFLOW.md` requires two checks on each agent's output before it is accepted. Step 5α runs the Constraint Validator on every output. Step 5a runs a Fable Reviewer on every task touching an invariant, plus Codex on the high-risk ones: schema, payroll export, rule logic and access control. The Opus seat merged all seven S144 tasks on build and test green alone, and ran neither step. The miss was found while preparing the close, before anything was pushed. Both steps were then run on each task's merge diff, and every fix they asked for landed before the candidate commit K. Step 7a reviews the whole sprint again on K, and that includes the fixes.

| Check | Scope | Outcome |
|---|---|---|
| **5α Constraint Validator** | all 8 tasks, per merge diff | **0 violations.** One unverifiable item: the hand-synced `db-schema.md`; the CI `docs` job arbitrates |
| **5a Reviewer (Fable `claude-fable-5-1`), worklist half** | 14400, 14404, 14401, 14405 (worklist), 14406 contract check | **APPROVED, no BLOCKER or WARNING.** The stamp is lock-time and comes from a single source; the gate for both verbs runs after scope; no 403 names an employee; UI and server bodies agree. NOTEs are fixed by TASK-14409 and the small fixes below. One NOTE is recorded rather than fixed: the S144 segment's **upgrade path** (the DROP of the auto-named old CHECK) is exercised by no test, because every migration test builds the table fresh. It is a **known unpinned path**, acceptable under the reseed ruling |
| **5a Reviewer (Fable), planner/payroll half** | 14402, 14403, 14405 (segmentation/payroll) | **APPROVED-WITH-WARNINGS.** W 14403-1: the new date read had no test of its own edges (1st excluded, last day included) → TASK-14409. W 14405-1: the "no date leaks" pins passed only because the planner message quotes a ruling date. The period rendered in machine culture (`01-03-2026` here, `03/01/2026` on CI), which the ISO regex never saw → TASK-14408 (ISO, culture-invariant period in the refusal text; adopts the reviewer's recommendation) + TASK-14409 (the pins assert the actual period dates) |
| **5a Codex, worklist (14400, 14404)** | **performed on inlined source.** The first attempt read nothing (`-s read-only` blocked its shell), so it was re-run on a bundle per `docs/AGENTS.md`; a clean verdict covers the bundle, not the whole repository | **No BLOCKER or WARNING.** Two NOTEs on test evidence: nothing pins that the stamp is derived *after* the lock (the code is correct by inspection); "nothing written" is observed as "nothing committed" (wording fixed) |
| **5a Codex, planner/payroll (14402, 14403)** | performed on inlined source | **No BLOCKER.** W: the raw `/export` and `/export-period` routes take caller-computed lines and never plan, so the refusal covers `/recalculate` and `/calculate-and-export` only. The docs name only those two, and this is registered as a follow-up. W: the straddle-safe fact proves the boundary is recorded, not that the second segment is mapped under the new agreement — that is QUAL-150 itself (comment corrected). The culture concern was checked, and it is not a CI flake (the ruling-date literal satisfies the regex in every culture), but it is the same vacuity the Fable reviewer found |

**Fix-ups dispatched:** TASK-14408 (`rule-engine`: ISO period in the three planner messages; `BoundaryDetector` summary). TASK-14409 (`test-qa`: fencepost facts for `GetEffectiveFromDatesAsync`; real redaction pins; the 403 reason names `HANDLED_MANUALLY`; four docstring corrections; **no test renamed**, so the frozen C-1 lists stand). Orchestrator small fixes, comments only, at `bff063b`: `EventSerializer.cs` lists three verbs; the `PayrollPlanRefusalProblem` redaction rationale is corrected (ADR-040 D7 concerns employment and change dates); the `ResolveAsync` doc now says "before any write".

**Recorded, not fixed (quality-register candidates for a ruling):** the Payroll host's dormant `ConfigResolutionService` registration (see the wave-3 section); the raw export routes bypass the planner; ADR-040 D7's precision note is stale (S144's handlers now echo cause names, possibly including `EmploymentStarted`/`EmploymentEnded`, with no dates) → updated at O-4.

## Fix-ups merged (TASK-14408, TASK-14409) and the final local gate

| Task | Merged | Result |
|---|---|---|
| TASK-14408 (`rule-engine`, `claude-opus-5-5`) | `bfa9ee5` via `756e050` | All three planner messages render the period culture-invariant ISO through a private `Iso(DateOnly)` helper using `InvariantCulture`. The house `{x:yyyy-MM-dd}` idiom was **not** copied, because it still uses the current culture's calendar. The `BoundaryDetector` summary names the agreement-code source. **Brief contradiction:** one test did assert the old culture format (`EmploymentTruncationAlignmentTests.cs:285-286`, `Mar01.ToString()`), which the brief's grep could not find. Fixed as a small task at `1de5f96`: it now asserts ISO. Not fixed, one word: the summary also omits local-profile activation |
| TASK-14409 (`test-qa`, `claude-sonnet-5-5`) | `e9a59c9` via `a7426c4` | Four Docker facts for `GetEffectiveFromDatesAsync` (1st excluded, 2nd and last day included, closed and zero-width rows returned, ascending and scoped; DISTINCT is not independently falsifiable, because a unique index already forbids duplicates). Redaction pins now assert the actual ISO and culture period strings. The 403 reason asserts `HANDLED_MANUALLY`. Four docstrings corrected. **No test renamed**, so the C-1 lists stand. **Limit, recorded:** the 403 reason always names both verbs, so the new assertion catches the verb being dropped from the text, not which verb was refused |

**Final local gate (O-1, on `a7426c4`):** build 0 / **145** · Unit **1308** · DemoSeed **170** · Regression non-Docker **128** of 2009 discovered · Smoke 7 discovered · frontend **988** · `tsc --noEmit` clean.


## Step-7a fixes merged (TASK-14411, TASK-14410) — the K′ gate

| Task | Merged | Result |
|---|---|---|
| TASK-14411 (`test-qa`, `claude-sonnet-5-5`) | `a61136a` via `f413e9c` | The four comment sites, in the adjudicator's A3 wording. The diff is comments only (filtered line by line). No test name changed |
| TASK-14410 (`payroll-integration`, `claude-opus-5-5`) | `267d5e7` via `577afd7` | **Owner ruling Q1 = A, built.** `PeriodCalculationService.EnsurePeriodPlannableAsync` runs the unchanged builder and discards the plan. There is no second copy of the boundary logic, so M-14 blinds this guard as well. Both raw routes plan **every calendar month their lines fall in** before anything is mapped, written, locked or recorded. `CalculationResult` has no period fields, and the export locks per calendar month, so whole months are planned: a change on the 16th refuses March even when the lines stop on the 15th. `/export-period` is all-or-nothing. Three Docker facts are added, **added to the frozen lists per A5**: two 422 facts (Run-1 RED under M-14, Run-2 GREEN) and one no-regression 200 (GREEN in both runs). **Census (before this task):** nothing in the product or in the existing tests called either route over HTTP; at K′ the only HTTP callers were this task's three new facts; TASK-14412 (K″) added two more, (d1) and (d2). **Behaviour change, recorded:** an employee with no `users` row now gets a 500 from the raw routes instead of an export, the same as `/calculate-and-export`. **Brief contradiction:** the shared host factory's stub answered the post-commit delivery call with 404, which makes any raw export report `Success=false` (422) even though the record is committed. The fix is a `DeliveringFactory()` local to the test file; `PayrollHostFactory.cs` is not modified. **Cycle 2 found** that the guard is blind while the Rule Engine is unreachable: the provider answered "no rules" when it meant "could not fetch". This is fixed at the source in TASK-14412 (K″); see § Step 7a, B1 |
| TASK-14412 (`payroll-integration`, `claude-opus-5-5`) | `22b3afa` via `f34a353` | **Step 7a cycle 2, ruling B1.** `HttpRuleClassificationProvider.GetClassifications()` now **throws `RuleClassificationsUnavailableException`** when the fetch fails (`:119` at K″, `:126` at K‴; still uncached, so it retries). A null or unparseable body also throws, and the doc says so. A shared `RulesUnavailable()` (`Program.cs:618`) returns **503** `{ success: false, error: <fixed sentence>, kind: "payroll-rules-unavailable" }`, with no id, date or upstream detail. It is caught at `RefuseUnplannableMonthsAsync` (`:601`, both raw routes), `/calculate-and-export` (`:355`) and `/recalculate` (`:517`; that route plans before its transaction opens, which the implementer verified). The builder body is untouched, so M-14 still blinds the guard, and the interface and fallback docs are rewritten. **RED observed:** the three unit pins failed on K′ with "No exception was thrown". Two unit facts were flipped from the old empty-on-failure contract, and one was added. There are four Docker (d) facts, run against the **real** provider inside the real host. They are GREEN in both evidence runs (R9-7). The three B8 comment fixes landed too, and the delivery matcher is now exact (`POST /api/payroll/receive`). `ReplayAsync` inherits the throw as a 500 on an outage; that fails closed, and no route exposes it |

**O-1 on `577afd7`:** a clean Release build gives 0 errors and **145** warnings. The **CA2100 ratchet is at 115** (baseline 115). Unit **1308**, DemoSeed **170** and Regression non-Docker **128** all pass, with 2012 facts discovered. `openapi.json` shows no drift when regenerated. Frontend is unchanged at 988.
## Test summary (sprint-test-validation: previous + delta = current)

| Suite | S143 | S144 | Delta |
|---|---|---|---|
| Unit | 1290 | 1309 | +19 (TASK-14405's 18 unit pins; TASK-14412 +1) |
| DemoSeed | 170 | 170 | 0 |
| Regression, non-Docker (local) | 128 | 128 | 0 (every new Regression fact is Docker-gated) |
| Regression, discovered | 1988 | 2016 | +28 (CI runs them; TASK-14410 +3, TASK-14412 +4) |
| Frontend (vitest) | 976 | 988 | +12 (TASK-14406) |

## Step 7a (C-4)

### Cycle 1 — on K = `c586944`, base `2e7d5b1`

| Lens | Verdict | Artifact |
|---|---|---|
| External (Codex, **performed on inlined source**: the S144 code diff, this log, E1/E2 and five full files; its sandbox blocked its shell) | **REQUEST-CHANGES** | `.claude/reviews/SPRINT-144-step7a-codex.md` |
| Internal (Fable `reviewer`, `claude-fable-5-1`, full repository) | **CLOSE-WITH-WARNINGS** | `.claude/reviews/SPRINT-144-step7a-reviewer.md` |

**In plain language.** The internal lens found no invariant break. The external lens rated the unguarded raw export routes a BLOCKER on payroll correctness, which is an invariant claim and not a scope note, and a Fable `adjudicator` (`claude-fable-5-1`) ruled on it.

The external lens rated the two **old low-level payroll export routes** (`/api/payroll/export`, `/export-period`) a BLOCKER. They accept lines someone has already calculated and never ran the planner. S144 never touched those routes and never promised to, so they are outside its scope. But this log had claimed that a mid-month agreement-code month "cannot be exported at all", which overstated the sprint. The adjudicator declined the finding as a reason to hold the close, corrected the record, registered it (QUAL-183), and put the design question to the owner. **The owner ruled "A — Guard now in S144"** (2026-09-29), so the routes now run the same check (TASK-14410).

The internal lens's warnings were all in comments or in this record:
- the ADR note's audience claim;
- the clock comment's midnight ordering, which was backwards, inherited from the fixture's own doc;
- a "only methods" list that went stale when this sprint added a method;
- a stale handoff block.

The adjudicator ruled every fix **in now, in a new candidate commit K′**, rather than carried to S145, and overruled the reviewer's carry recommendation. Its reason: the S143 checklist item should not survive a fourth review pass.

**Adjudication (A1–A5):**
- **A1.** Raw export: declined as a close blocker, the record corrected, QUAL-183 registered, owner Q1 asked. Ruled A, and built as TASK-14410.
- **A2.** Checklist item (3) is DONE, cited: `EmployeeProfileCopenhagenBoundaryTests.cs:286-317, :334-355` drive the product's only soft-delete path at disagreeing instants (CET and CEST) and assert stored literals. Residual: the `closeDate ?? Today()` fallback at `EmployeeProfileRepository.cs:1213` has no production caller and is pinned only at an agreeing instant. The follow-up is to make `closeDate` required, so the fallback disappears.
- **A3.** W2 and W3 fixed now: TASK-14411, four comment sites.
- **A4.** W1 (ADR-040 audience), W4 (handoff), N2 (the session switch), the item-(6) gloss and N6 (`:490` → `:492`) all land in K′, so cycle 2 verifies them.
- **A5.** K′ is code-touching, so cycle 2 runs on both lenses before the close commit. **Frozen-list amendment, pre-ruled by the adjudicator for Q1 = A:** TASK-14410's two raw-route facts join **Run-1 RED under M-14** (the re-plan goes blind when the hydration is nulled) and **Run-2 GREEN**. Its no-regression fact, if added, joins both runs' GREEN.

**Carried checklist, final dispositions:**

| Item | Disposition |
|---|---|
| (1) | DONE |
| (2) | DONE |
| (3) | DONE, cited (A2). The external lens marked it NOT RESOLVED at cycles 2 and 3 for the same bundle-limitation reason as row (9): the cited test bodies were not in its bundle |
| (4) | DONE after TASK-14411 (the S144 fix had inherited the fixture doc's inversion) |
| (5) | DONE after TASK-14411 |
| (6) | DONE (`SPRINT-143.md:597`) |
| (7) | DONE |
| (8) | DONE |
| (9) | DONE on the full-repository lens (Codex's NOT DONE was a bundle limitation) |

**Evidence confirmation.** The internal lens (full repository) confirmed E1 `54f8fc1` = K + M-1 + M-2 + M-14 exactly and E2 `bd4bf75` = K + M-3 exactly. E2 moves the version guard below the block check, which gives the same resulting order (N1). It also confirmed that every RED entry trips, every GREEN entry holds, and no unlisted test goes red. The external lens confirmed the same for the test bodies it was given, and expressly withheld repository-wide certification because its bundle omitted unchanged tests and fixtures. The actual red sets are compared at C-6/C-7. The evidence is re-cut on each new candidate (C-3 repeated).

### Cycle 2 — on K′ = `203fefd`

| Lens | Verdict | Artifact (the cycle-1 verdict is kept below the current one) |
|---|---|---|
| External (Codex, performed on inlined source) | **REQUEST-CHANGES** | `.claude/reviews/SPRINT-144-step7a-codex.md` |
| Internal (Fable `reviewer`, `claude-fable-5-1`) | **CLOSE-WITH-WARNINGS** | `.claude/reviews/SPRINT-144-step7a-reviewer.md` |

**In plain language.** Both lenses confirmed that the guard the owner asked for is built correctly. It runs first, uses the one plan builder, plans exactly the months the export locks, and returns the same redacted 422. They also confirmed that the three new tests can fail for the right reason, that E1′ `b56533a` and E2′ `1a54cc6` are exactly the named mutations. The internal lens found every cycle-1 finding resolved. The external lens did not: it kept the raw-export BLOCKER open (the outage bypass below), checklist item (3) open as independent verification (the same bundle limitation as row (9); A2), and the handoff contradiction open (fixed in K″, B4).

Both lenses then found the same remaining hole. The rule-list provider answered "no rules" when it meant "I could not find out" (`HttpRuleClassificationProvider.cs:104-107`). So during a Rule Engine outage the new guard planned blind, and a raw export of a split month went through with wrong lines. The lenses disagreed on the disposition: the external lens called it a BLOCKER to fix now, the internal lens a WARNING to register.

The Fable `adjudicator` (`claude-fable-5-1`) ruled **B1: fix now, at the source**, and asked no owner question. Its reasoning: the owner chose "guard now" at Q1, a guard with a known bypass is not what they chose, and the review cap the owner raised to 5 on 2026-09-29 leaves room. The adjudicator also found that the calculating routes were only protected **by coincidence**. If the Rule Engine came back between the classification fetch and the rule calls, they too planned blind and exported. So the fix covers all four routes: the provider throws `RuleClassificationsUnavailableException`, and each route answers **503 `payroll-rules-unavailable`**, writing no payroll data (the audit middleware still records the refused request). This retires the S20 premise that an empty rule list is "degraded but correct". That was true when classifications only chose merge strategies. Since S137/S144 they decide payroll correctness.

**Rulings B1–B11.**

- **B1:** TASK-14412 → K″ → cycle 3. The builder body is untouched, so M-14 still blinds the guard exactly as before. The four new outage facts are GREEN in both runs (**R9-7**) and no RED entry changes.
- **B2, B3:** the lens-agreement and evidence-attribution sentences are corrected.
- **B4:** the handoff and Status rows are rewritten.
- **B5:** the TASK-14410 row's census and residual are corrected.
- **B6:** M-14 is cited per commit.
- **B7:** the HRP-003 citations are recomputed at K″.
- **B8:** the three test-comment NOTEs are fixed in TASK-14412, not carried to a later sprint.
- **B9:** the summary gains the outage clause.
- **B10:** QUAL-185 is registered as FIXED, QUAL-183 points to it, HRP-003 gains one sentence, and the unknown-employee 500 is added to the follow-ups.
- **B11:** cycle 3 verifies the K′ → K″ diff, the seven new or flipped pins, E1″/E2″, the amended lists and these corrections. This is **cycle 3 of 5**.

**Declined:** Codex's NOTE on the unknown-employee 500 (routed to follow-ups); reviewer N-c2-5 to N-c2-7 (confirmations, not defects); the reviewer's WARNING-only disposition (overruled by B1).

### Cycle 3 — on K″ = `934f681`

| Lens | Verdict | Artifact |
|---|---|---|
| External (Codex, performed on inlined source) | **APPROVE-WITH-WARNINGS** | `.claude/reviews/SPRINT-144-step7a-codex.md` |
| Internal (Fable `reviewer`, `claude-fable-5-1`) | **CLOSE-WITH-WARNINGS** | `.claude/reviews/SPRINT-144-step7a-reviewer.md` |

**In plain language.** Both lenses confirmed the outage fix:
- every failure branch of the rule-list fetch throws;
- nothing is cached on failure;
- all four routes answer the fixed 503 before any payroll write;
- E1″ `50bc1ad` and E2″ `58bc170` are exactly the named mutations.

The internal lens, reading the full repository, also confirmed that no blind path remains. The classification callers are `:429`, `:1105` and `:1145`/`:1186`, and there is one DI registration.

The external lens kept three items open, each as a bundle limitation or a deferral:
- checklist item (3);
- the unknown-employee 500 (in the follow-ups);
- the cycle-2 attribution sentence (corrected, D1).

**Neither lens found a BLOCKER.** The Fable `adjudicator` (`claude-fable-5-1`) ruled C1–C6.

**Rulings C1–C6.**
- **C1: the `[]` case is documented, not guarded.** The live `RuleRegistry` cannot answer `[]`: it registers 16 classifications in its constructor (`RuleRegistry.cs:47-100`), and a test already pins the real endpoint non-empty. A count check would not catch the skew that matters, a set missing the Reject/AlignedWindow rules. That is the registry's contract with the planner.
- **C2: four false or stale code comments are fixed now, in K‴, verified by cycle 4 of 5, not carried to a later sprint.**
  - the provider's "only" sentence;
  - the S20 "cross-domain wiring" paragraph;
  - the "→ 500" red conditions, which in this in-process harness actually escape before the status assertion;
  - "ExportResult body".
  One assertion is added so the 503 pins check the fixed body's exact property set. Done as TASK-14413 (`src/` comments) and TASK-14414 (test comments plus that assertion). No test was renamed, so the C-1 lists stand.
- **C3: docs-only corrections (D1–D9) ride the close commit, not K‴.** This keeps K‴ a pure code-comment diff.
- **C4: Smoke is a C-5 watch item, expected green.** If it is red on `payroll-rules-unavailable`, the fix is a code-touching post-close commit with its own scoped Step-7a cycle (counter reset), not a docs-only exemption.
- **C5: cycle 4 is required before the close commit.** Only a BLOCKER at cycle 4 earns a code fix. Every WARNING and NOTE is recorded or carried.
- **C6: the 503 sentence stays.** "Nothing was calculated, written or exported" is about payroll data. The qualification belongs in the docs, not the response.

### Cycle 4 — on K‴ = `4936998` (final)

| Lens | Verdict | Artifact |
|---|---|---|
| External (Codex, performed on inlined source) | **APPROVE** | `.claude/reviews/SPRINT-144-step7a-codex.md` |
| Internal (Fable `reviewer`, `claude-fable-5-1`, full repository) | **CLOSE-WITH-WARNINGS** | `.claude/reviews/SPRINT-144-step7a-reviewer.md` |

**In plain language.** Both lenses confirmed three things:
- K‴ changes no behaviour. It is comments plus one assertion.
- Every corrected comment now matches the code it describes.
- The new assertion pins the 503's exact three-field shape and cannot turn an outage test red, either on K‴ or under the evidence mutations.

E1‴ `0b898d2` = K‴ + M-1 + M-2 + M-14 and E2‴ `f33252f` = K‴ + M-3. Both are the same edits as every previous cut, and all 30 frozen-list names resolve. **Neither lens found a BLOCKER.** Under the pre-ruling (C5), nothing more lands before the close commit.

The only WARNING is the one carried from cycle 3: the CI smoke job now depends on a live rule-list fetch in the compose stack. It is expected green; only the C-5 run can settle it, and C4 pre-ruled what happens if it is red.

**Cycle count: 4 of 5 (owner cap raised to 5 on 2026-09-29).** The halt-and-prompt was never reached.

**Carried to S145, recorded rather than fixed (per C5):**
- N-c4-1: the 503's `error` sentence is still verified by inspection only. The fix is to assert the exact sentence, or to give the stub a sentinel body and assert that the sentinel is absent.
- N-c4-2: the (a) red-condition hedge "200, or …" is wider than the code; only "escapes" is reachable.
- N-c4-4: the response sentence "Nothing was calculated, written or exported" is true of payroll data, and the refused request is still audited. This is a wording question only.
- Codex's cycle-3 NOTEs on the exact 503 property set, the "ExportResult" wording and the "→ 500" mechanism were fixed in K‴.

## Evidence runs (C-6, C-7) — PLACEHOLDER, filled by the docs-only follow-up commit (C-9)

*Per run: run id, head sha (E1 / E2), each red test with its assertion message against the frozen list, "matched"; the PR number, "closed unmerged, branch deleted"; whether `claude-code-review.yml` fired on the draft PR, with its output ignored.*

## Open follow-ups (routed, not lost)

- **Payroll host DI — QUAL-184 (registered).** `ConfigResolutionService` is registered without its repositories (`Program.cs:112`). Delete the registration, then run `PayrollHostFactory` in Development so `ValidateOnBuild` guards the host.
- **Raw export routes bypassed the planner — QUAL-183, FIXED in S144 (TASK-14410).** Rated WARNING at 5a and BLOCKER by the Step-7a external lens; the adjudicator declined it as a close blocker and put the design to the owner, who ruled **A — guard now** (2026-09-29). `/api/payroll/export` and `/export-period` now re-plan each period through the same builder as the calculating endpoints and refuse with the same redacted 422.
- **The lock-time stamp is correct by inspection, not by a pin:** nothing proves the block set is derived after the lock (it would take a coordinated concurrent append). A backlog candidate.
- **The 403 reason names both verbs:** echoing the refused verb would make the refusal (and its pin) precise. A small UX and API follow-up.
- **Unpinned clock in four Regression tests** and the `FixedTimeProvider` doc wording (wave-1 observations).
- **The S144 segment's upgrade path** is exercised by no test: a known unpinned path, acceptable under the reseed ruling.
- **The `closeDate ?? Today()` fallback** (`EmployeeProfileRepository.cs:1213`) has no production caller and is pinned only at an agreeing instant (Step 7a A2). The premise is removable: make `closeDate` required. Same shape as QUAL-177 (a branch only tests execute).
- **`EmploymentWindowLiveRulesetTests.cs:235-237`** has the same vacuous date-absence shape that Step 5a W 14405-1 fixed; it is pre-existing from S137 and a register candidate (Step 7a reviewer N4).
- **Do the raw export routes need to exist at all?** They have no caller, and `/recalculate` has superseded their correction use. They are now guarded (QUAL-183); retiring them is its own refinement.
- **The raw export routes answer 500 for an employee with no `users` row** (TASK-14410; Codex cycle-2 NOTE). Refusing is correct, and it matches `/calculate-and-export`, but a deliberate 4xx would be clearer. This is API quality, not correctness.
- **The compose `payroll` service sets no `ServiceUrls__RuleEngine`** (`docker/docker-compose.yml:92-113`). It works only because the per-rule client and the classification client share the code default `http://rule-engine:8080`. Set it explicitly (hygiene; found at Step 7a cycle 3 while checking W-c3-1).
- **(d4) does not observe that the same idempotency token can be retried after a 503.** That is true by construction, because the plan runs before the token is marked (`RetroactiveCorrectionService.cs:210` vs `:343-353`). Strengthening the test is optional.
- **The 503 `error` sentence is not pinned word for word** (Step 7a cycle 4, N-c4-1). Assert the exact sentence, or give `UnavailableRulesFactory()`'s 503 a sentinel body and assert that the sentinel is absent from the response.
- **Two wording tidy-ups carried from cycle 4:** the (a) DI hedge in `PayrollHostRecalcBlockedTests.cs:139-141` (N-c4-2), and the 503 sentence's "nothing … written", which is true of payroll data while the refused request is still audited (N-c4-4).
- **QUAL-149 / QUAL-150** remain the route to making mid-month agreement-code months exportable again.

## Handoff block (written at every wave gate — the state lives here, not in the conversation)

| | |
|---|---|
| **As of** | 2026-09-29, the S144 close commit, a docs-only child of K‴ `4936998`; session on Opus 5.5 (client 2.1.284) |
| **Phase** | **Step 7a closed at cycle 4 of 5** (external APPROVE, internal CLOSE-WITH-WARNINGS, both `reviewed-against-commit: 4936998`). Close commit made; C-5 push and CI watch next |
| **Dispatched tasks / worktrees** | None in flight. The evidence branch `s144-red-mutations` is local only: **E1‴ `0b898d2`**, **E2‴ `f33252f`**. There are 18 agent worktrees, kept for teardown at C-9. **Still uncommitted, deliberately:** the owner's review-cap change (3 → 5) in `docs/WORKFLOW.md`, `docs/AGENTS.md` and `.claude/skills/refine-requirements/SKILL.md`. It lands as its own commit after the close push, because C-5 requires that only `docs/**` changes between K‴ and the close commit, and the skill file is outside `docs/**` |
| **Merged** | 14400 `5e2f50b`, 14407 `52d577d`, 14401 `bffc469`, 14402 `0bdd5a5`, 14404 `01e5bea`, 14406 `24fa0d2`, 14403 `807f75f`, 14405 `bd4df92`, 14408 `756e050`, 14409 `a7426c4`, 14411 `f413e9c`, 14410 `577afd7`, 14412 `f34a353`, 14413 `ca966c8`, 14414 `52179b5`, small task `4936998` (= K‴) |
| **Pending gates** | **C-5:** push master and watch CI on the close sha, reading the `smoke-tests` job in particular (W-c3-1; a red there is handled per C4). **C-6:** push E1‴ as `s144-red-mutations`, open the draft PR, watch run 1, and compare it with the frozen Run-1 lists. **C-7:** fast-forward the branch to E2‴, watch run 2, and compare. **C-8:** close the PR unmerged and delete the branch on both sides. **C-9:** a docs-only follow-up recording the CI line, the evidence runs, the O-7 routing row and the INDEX CI line. Then teardown, and the governance commit for the review cap |
| **Open rulings** | None. A red Smoke job at C-5 is pre-ruled (C4). A red set in either evidence run that does not match its frozen list is handled per C-6: repair and repeat, never interpret |
| **Next action** | Push master (C-5) and start the watched CI run |
| **Step zero** | `planner` → `claude-opus-5-5`; `reviewer` → `claude-fable-5-1`; Sonnet tier → `claude-sonnet-5-5`; Opus implementer tier → `claude-opus-5-5` (all verified from self-reports) |
