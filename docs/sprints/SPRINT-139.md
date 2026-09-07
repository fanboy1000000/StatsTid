# Sprint 139 — HR follow-up process inventory + QUAL-153 fixed clock

| Field | Value |
|-------|-------|
| **Sprint** | 139 |
| **Status** | complete |
| **Start Date** | 2026-09-07 |
| **End Date** | 2026-09-07 |
| **Orchestrator Approved** | yes — 2026-09-07 (plan: the owner agreed the shape and ruled OQ-1 (a) / OQ-2 (b) / OQ-3 (a), refinement rev 3 · close: Step-5a dual-lens terminal — Codex CLEAN ×2, Reviewer 0 B / 2 W + 2 W all absorbed and verified; Step-7a Codex **CLEAN at cycle 3**, Reviewer **APPROVED-WITH-WARNINGS** with every W and N absorbed — the cycle-2 verification is the artifact `.claude/reviews/SPRINT-139-step7a-reviewer.md`; the HR register is review-complete, the owner's shape ruling is pending and will be recorded post-close) |
| **Build Verified** | yes — `dotnet build StatsTid.sln -c Release --no-incremental` **0 errors** on the final tree (145 warnings = baseline; CA2100 distinct sites **115** = baseline, recounted three times) |
| **Test Verified** | local, final tree: unit **1235/1235** · DemoSeed **165/165** · regression non-Docker **102/102** · frontend **735/735** + `tsc` clean — all green; Docker-gated pins (the probe, two repository-guard tests, eight anchor facts, 65 converted pins) + smoke: **CI-pending** — watched close run, backfilled here when green (Docker unavailable on the owner's machine, standing instruction) |
| **Orchestrator model** | Open — Steps 0a / 0b / 1, this log: **Fable 5.1** ✓ · Dispatch, monitoring, acceptance bookkeeping, CI watch (Steps 2–4, 6): **Fable** — the owner chose NOT to switch at dispatch ("dispatch wave 1" on Fable, 2026-09-07); the switch point was offered and declined, recorded for the retrospective and the model-routing register · Step-5a / 7a absorption and every ruling on an agent's declared deviation: **Fable** · Close bookkeeping + CI backfill: **Opus**. First live run of `docs/WORKFLOW.md` § Model Routing; agents are spawned by role name, no `model` override. |
| **Sprint-start commit** | `6e8d2a7` (S138 post-close CI-green backfill) — the `codex review --base` fallback anchor for Step 7a |

## Sprint Goal

Two deliverables, one bounded code task and one analysis.

**Part A — the HR follow-up register (analysis; docs + read-only agents).** The system increasingly hands HR a
list and walks away: the S138 backdate worklist, settlement rows waiting for review, leavers whose last month
must still be approved and sent, window-edit refusals HR resolves by hand, retroactive recalculations somebody
must request and confirm, config drafts awaiting promotion, compliance warnings nobody is assigned to. Nobody has
looked at those hand-offs TOGETHER. This sprint produces one register (`docs/operations/hr-follow-up-process-
register.md`, `HRP-NNN` rows) that makes every hand-off comparable — who is accountable, by when and from which
source, how an open item surfaces today (screen / API only / log / nothing), what happens when it ages, how its
resolution is audited, and whether the process is DECISION-READY (deadline source AND accountable role known) or
goes to Phase B with the missing fact named. Then a cross-cutting ruling on the shape — one HR to-do surface,
per-process lists, or a hybrid — plus an aging/escalation model for the decision-ready rows only (owner ruling
OQ-2 (b)). Today HR has no follow-up surface at all (the S138 worklist is an API and a TypeScript type; the HR
pages are `admin/organisation-medarbejdere` and `admin/auditlog`), so the register will mostly find "API only" or
"nothing" — that IS the finding, stated per process with evidence. Nothing is built: the register feeds the
Increment-4 lifecycle-UX design (S140).

**Part B — QUAL-153, a fixed clock for the date-sensitive regression suites (bounded code task).** S138 lost two
CI iterations to pins whose dates float with the calendar: a seeded absence at "today − 60" lands on a weekend two
days in seven, the weekend norm is zero, and the pin proves nothing on those days. The nudge S138 applied is a
guard, not a fix. The durable fix has three parts: (1) promote the existing `FixedTimeProvider` to a shared test
fixture with a `WithFixedToday(DateOnly)` opt-in on the WebApplicationFactory; (2) convert every suite the census
marks hazardous to CONSTANT anchored dates whose weekday and OK-version side are asserted once; (3) make the
product read the same clock the tests fix — on the exercised paths only (owner ruling OQ-1 (a)) — because today
one request reads the real clock in two places (`EmployeeProfileRepository.cs:502` decides future-dating from
`DateTime.UtcNow`; `SoftDeleteAsync` stamps `effective_to = NOW()::date` from the DATABASE clock at `:816`) while
the worklist row the same request writes already reads the injectable clock. A fixed test clock the product does
not share would make the halves of one request disagree about "today". Day-derivation is unchanged: these paths
keep the UTC day (owner ruling OQ-3 (a)); the UTC-vs-Copenhagen split is registered as its own QUAL row.
Production behaviour is unchanged and pinned (`TimeProvider.System` stays the registered default).

**Inputs:** refinement `.claude/refinements/REFINEMENT-s139-hr-followup-inventory-qual153.md` **rev 3** (dual-lens
converged: Codex cycle 1 1B/5W/1N → cycle 2 0B/1W; Reviewer cycle 1 1B/6W/7N → cycle 2 1B/2W/6N — every item
absorbed; the shared cycle-1 BLOCKER was the UTC-day vs Copenhagen-day ambiguity, now OQ-3; the Reviewer's cycle-2
BLOCKER was a case-sensitive SQL census regex that would have missed the very `NOW()::date` write the sprint
converts) · owner rulings 2026-09-07: **OQ-1 (a)** seam on exercised paths only · **OQ-2 (b)** shape +
aging/escalation for DECISION-READY rows only · **OQ-3 (a)** keep UTC-day, route only the clock SOURCE · ROADMAP
"HR operations — the follow-up processes" (owner-raised 2026-09-03) and "A fixed clock … (`QUAL-153`)" ·
`docs/operations/quality-finding-register.md` QUAL-153 · `docs/WORKFLOW.md` § Model Routing (first live run).
S138 CI green (`34094447389`, backfilled `6e8d2a7`) — the CI-health close gate is satisfied at open (latest master
run `34102571607` success).

## Entropy Scan Findings (Step 0a)

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | **DRIFT — 2 fixed** | 58 unique `src/ tests/ tools/ frontend/src/ docker/` paths cited in KB entries checked for existence. Two stale: `ADR-023:48` cites `frontend/src/pages/admin/UserManagement.tsx` (S32 name; the page is `OrganisationOgMedarbejdere.tsx`) — annotated; `ADR-018:449` cites a hypothetical `tests/StatsTid.Tests.Unit/Outbox/EventStoreInterfaceTests.cs` "if needed" — never created, coverage lives in `EventStoreInTxTests.cs` — annotated. `tests/.../Contracts/ContractAssert.cs` is an ellipsis placeholder (false positive). |
| Pattern compliance spot-check | CLEAN | `FindFirst("scopes")` 0 hits · `http://localhost` in non-test `src/` 0 hits · 140 `Map(Get\|Post\|Put\|Delete)` definitions vs 141 `RequireAuthorization` calls in the endpoint files; the only two route files without one are `ApiEndpoints.cs` (`/health`) and `AuthEndpoints.cs` (`/api/auth/login`), anonymous by design. |
| Orphan detection | CLEAN | 24 non-doc files added between the S137 and S138 closes: the three `src/` files are wired (`MapBackdateWorklistEndpoints` at `ApiEndpoints.cs:37`; all six contract records have consumers outside `Contracts/`); the rest are test classes, discovered by the runner. |
| Documentation drift | CLEAN (after owner action) | The deferred-items list lives in ROADMAP (no MEMORY.md). The "prune the two closed-sweep worktrees" tooling row was DELETED by the owner 2026-09-07 after pruning (`git worktree list` = main only) — the deletion is uncommitted and rides this sprint's commits. Every S138 deferral is present: QUAL-152 and QUAL-153 register rows; the gap-fill (cases E/G) revaluation deferral; future-dating → Increment 4 under the "current ≠ live" precondition; the HR inventory item this sprint consumes. |
| Quality grade review | CLEAN | `docs/QUALITY.md` re-grounded at the S138 close (anchor 138); `ANCHOR_SLACK` is 3, so no freshness warning at S139. `tools/check_docs.py` is CI-only from this machine (no Python). |
| Session preconditions | CLEAN | `codex` on PATH (`%APPDATA%\npm`) · the twelve role definitions under `.claude/agents/` resolve in this session (refinement assumption 7 — the restart happened) · both PreToolUse hooks registered in `.claude/settings.json` · `.claude/telemetry/model-routing.log` present (four ALLOW lines from the refinement session). |

## Step 0b (plan review)

**Trigger: MANDATORY** — Part B touches domain correctness on the payroll-adjacent temporal-write path (the three
repositories, the profile and agreement-code endpoints, one SQL write + one SQL compare). **Served by the
refinement's Step-4 dual-lens** (the S134 / S137 / S138 precedent): two lenses × two cycles on the plan content,
converged at rev 3 with the owner's three rulings recorded. This decomposition is a one-to-one restatement of rev
3's *Proposed Approach* into tasks and waves; agent assignment follows rev 3 assumption 7 and the WORKFLOW routing
table, with no scope added. Load-bearing absorptions the implementation must honour:

- **One clock SOURCE, existing day-derivation.** Every converted path reads the injected `TimeProvider`; the
  profile/agreement paths keep the UTC day via the seam form at `SkemaEndpoints.cs:2052`
  (`DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)`); settlement and the worklist keep the
  Copenhagen day. The Postgres session is UTC (no TZ override anywhere), so binding a UTC-day `@today` parameter
  in place of `NOW()::date` / `CURRENT_DATE` is behaviour-preserving — a refactor, not a change.
- **The census is reproducible or it is not a census.** Regexes and universes are PINNED (below); the SQL regex is
  case-insensitive (this codebase writes `NOW()`); every grep excludes `.claude/worktrees/` (the stale copies
  contaminated a count during refinement — a live instance of the S138 TASK-13809 trap, since pruned).
- **The probe must be able to fail.** With fixed today `F = 2025-03-12` (Wednesday, OK24 side) and a fixture
  employee hired ≤ F or undated, `PUT` profile at `F+1` → 422 and at `F` → 200; the agreement-code twin the same;
  `DELETE` profile → row `effective_to` == event `EffectiveTo` == `F`, read back by `profile_id` (never by
  `effective_to IS NULL` — a same-day create-then-delete leaves a zero-width row). Each leg states in its comment
  what an unconverted product would do instead.
- **Constructor coupling stays compile-neutral.** All three repositories gain an OPTIONAL trailing
  `TimeProvider? timeProvider = null` resolved to `TimeProvider.System`; the 33 live direct test constructions
  (19 + 14) compile unchanged; DI passes the singleton.
- **Security timestamps stay on the real clock BY DESIGN** (token minting, audit `created_at`); the census marks
  them H-none. A fixed provider under token minting would mint "in the past" against a real-clock validator.
- **Part A's inclusion rule bounds the sweep.** IN: hand-offs whose RESOLVER is Local HR / Local Admin / Global
  Admin, or that BECOME theirs by aging. OUT: employee and leader steps inside the designed §H approval flow —
  listed once as a boundary row-group ("B-rows"), not analysed. Deadlines are recorded only where code, an ADR or
  `danish-agreements.md` states one; otherwise `UNKNOWN → Phase B`, never invented.
- **Analysis, not build.** No HR UI, no endpoint or event-contract change in Part A; Increment 4 (S140) owns the
  build against a decided lifecycle for the rows it will actually show.

## Scope & Task Decomposition (Step 1)

Task ids `TASK-139NN`. Agents are spawned by ROLE NAME (`subagent_type`) with no `model` parameter — the
definition fixes the model and `model-routing-guard.ps1` blocks a wrong one. Every agent prompt carries
`docs/CONVENTIONS.md` verbatim. Acceptance criteria are the refinement's, quoted per task at dispatch.

| Task | Wave | Agent (model) | Deliverable |
|------|------|---------------|-------------|
| **TASK-13901** | 1 | `trace` (Sonnet, read-only) | **HR hand-off sweep, universe (i) — Backend `Endpoints/*.cs`, EXHAUSTIVE over the 25 endpoint files** (27 `.cs` minus the two exception types). Seeds: "HR must/should", "manual", "by hand" comments; 409/422 sites whose resolution needs a LATER human action rather than a corrected request; `HROrAbove` mutations that are the RESOLUTION step of some hand-off; the nine ROADMAP-named members. Output: candidate rows — trigger · resolver role (SYSTEM_TARGET §F) · surfacing evidence · aging · resolution action + audit trail · classification · `file:line` for every claim · a coverage declaration (files visited / files total). |
| **TASK-13902** | 1 | `trace` (Sonnet, read-only) | **Universe (ii) — Infrastructure services INCLUDING the three hosted services** (`OutboxPublisher`, `DelegationExpiryService`, `SettlementCloseService` — the poller that creates the settlement `PENDING_REVIEW` hand-off; `Program.cs:44-46`) + outbox event types + every DB status/timestamp column that models open→resolved, EXHAUSTIVE over `docs/generated/db-schema.md` (`hr_backdate_worklist`, `vacation_settlements` `PENDING_REVIEW` / `payout_reconciled_at`, `approval_periods` status, config DRAFT, compliance warnings, delegation expiry, …). Same output shape and evidence bar as 13901. |
| **TASK-13903** | 1 | `trace` (Sonnet, read-only) | **Universe (iii) — declared SAMPLE:** RuleEngine compliance (Advarsel warnings, EU-WTD compensatory rest) + the Orchestrator control loop + frontend pages/routes (what HR can actually SEE today: `App.tsx` routes gated `minRole="LocalHR"`) + SYSTEM_TARGET §H/§K/§N + ADR-013/033/040. Same output shape; coverage declared as sampled, with what was and was not read. |
| **TASK-13905** | 1 | `trace` (Sonnet, read-only) | **Clock census, PINNED.** C#: `DateTime\.(Today\|UtcNow\|Now)\b\|DateOnly\.FromDateTime\(DateTime\.` over `tests/**/*.cs` and `src/**/*.cs`. SQL, case-insensitive: `CURRENT_DATE\|NOW\(\)\|CURRENT_TIMESTAMP\|LOCALTIMESTAMP` in ANY position over `src/**/*.cs` + `docker/postgres/*.sql`. Worktrees excluded. Per TEST file: hazard class(es) — H-weekday (offset can land on Sat/Sun, `DailyNormCalculator.cs:171` → 0) · H-month · H-ferieår (1 Sep / 31 Dec transfer / 1 May særlige) · H-year · H-okversion (straddles the OK24→OK26 cutover `OkVersionResolver.cs:18-19`, 2026-03-31/04-01) · H-none (timestamps, uniqueness suffixes, audit stamps); authorization as-of reads (`SkemaEndpoints.cs:218`, `ApprovalEndpoints.cs:319/505/1592`) classified BUSINESS-DATE. Per `src/` C# site: per-file counts. Per SQL site: class (compare / business-date WRITE / timestamp WRITE / default-timestamp) and REACH (request path of a converted suite / host-background / unreachable). Output: the census table for this log + the CONVERSION SET (hazardous suites) + the register material for 13910. Indicative sizes from refinement: ~91–100 test files with raw reads; ~117–121 `src/` C# hits in ~49–51 files; 115 SQL-clock lines in 39 `src/` files; business-date WRITES at `EmployeeProfileRepository.cs:816` (live, converted path) and `RoleConfigOverrideRepository.cs:444` (dead code). |
| **TASK-13906** | 2 | `test-qa` (Sonnet) | **Fixture + probe (RED-first from spec).** Promote `FixedTimeProvider` (today `internal`, namespace `…Regression.Balance`, `YearOverviewTests.cs:2469`) to `tests/StatsTid.Tests.Regression/Hosting/` with BOTH constructors — `DateOnly` (UTC midnight, today's behaviour) and `DateTimeOffset` (verbatim) — so PAT-008's sample and the 15 dependents both stay true (the dependents' `using` lines are 13909's). Add `StatsTidWebApplicationFactory.WithFixedToday(DateOnly)` as an opt-in mirroring `WithThrowingOutbox()` (`StatsTidWebApplicationFactory.cs:183`). A DI assertion pinning `TimeProvider.System` as the registered default (the "production unchanged" claim is a DI fact, not a real-calendar comparison). The falsifiability probe: fixed `F = 2025-03-12`; fixture employee hired ≤ F or NULL, seeded THROUGH the fixed host or by SQL AFTER boot (PAT-008:37 — a derived host re-runs the seeders, `StatsTidWebApplicationFactory.cs:171-175`); (1) profile PUT `F+1` → 422, `F` → 200; (2) the agreement-code PUT twin (`AdminEndpoints.cs:2463`); (3) soft-delete leg — `DELETE` profile, then row `effective_to` == event `EffectiveTo` == `F`, row read back BY `profile_id` (event read-back precedent `EmployeeProfileLifecycleTests.cs:478`; served-`today` byte-pin precedent `Contracts/S120BalanceSpecRuntimeTests.cs:334`). RED condition in each test comment. Docker-gated: goes green in CI only after 13907. |
| **TASK-13907** | 2 | `backend-infrastructure` (Opus) — **Step-5a dual-lens MANDATORY** (payroll-adjacent temporal-write path; S138's two Step-5a BLOCKERs lived here) | **Product seam on the CONVERTED suites' exercised paths only (OQ-1 (a)); day-derivation unchanged (OQ-3 (a)).** Optional trailing `TimeProvider? timeProvider = null` → `TimeProvider.System` on `EmployeeProfileRepository` (`:83`), `UserAgreementCodeRepository` (`:50`), `ApprovalPeriodRepository` (`:30-36`, already carries optional trailing test-compat parameters); DI passes the registered singleton (`Program.cs:390`). C# sites: `EmployeeProfileRepository.cs:375/502/689`, `EmployeeProfileEndpoints.cs:271/945`, `UserAgreementCodeRepository.cs:248`, `AdminEndpoints.cs:927/1091/1324/1578/2463` — UTC-day via the `SkemaEndpoints.cs:2052` form. SQL sites: `EmployeeProfileRepository.cs:816` `SET effective_to = NOW()::date` → `effective_to = @today` bound from the app clock (`updated_at = NOW()` stays a DB timestamp); `ApprovalPeriodRepository.cs:658` `CURRENT_DATE` compare → parameterised from the app clock UNCONDITIONALLY. Doc comments quoting the old SQL updated (`:707/:741/:747/:811`, `EmployeeProfileEndpoints.cs:115`). The router stays PURE with `today` as a parameter (`EmployeeProfileRepository.cs:505`, PAT-025). All 33 live direct constructions compile unchanged. Token minting and audit stamps NOT touched. |
| **TASK-13909** | 2 (sequenced after 13906) | `sweep` (Haiku) | **The `using` sweep:** the 15 compile-time dependents of `FixedTimeProvider` (12 files via `using StatsTid.Tests.Regression.Balance;` + 3 in the namespace itself; 2 further token hits are comments) re-pointed at the `Hosting` namespace from an exact file list produced by 13906; the test projects build. Nothing else. |
| **TASK-13908** | 3 | `test-qa` (Sonnet) | **Convert the hazardous suites** (the census's conversion set; at minimum `EmployeeProfile/ProfileBackdatingEndpointTests.cs`, `EmployeeProfile/Adr032RevaluationTests.cs`, `Admin/AgreementCodeBackdatingEndpointTests.cs` — `Today` at `:71`, offsets −120/−90/−60/−30, `Today.AddMonths(-3)` at `:266` is H-month) to CONSTANT anchored dates through `WithFixedToday`, asserting the anchor's weekday AND OK-version side ONCE per suite; exemplar of the target style `Skema/Adr032ConsumptionPinTests.cs:69-72` (`HalfTimeMonday = 2025-01-06`, weekday in the name). Delete the two nudge helpers (`OnWeekday` `ProfileBackdatingEndpointTests.cs:119`, `NextWeekday` `Adr032RevaluationTests.cs:78`). Each converted pin's RED condition stated in its comment. The C# census regex over the converted files returns ZERO. Harmless (H-none) suites untouched. |
| **TASK-13904** | 2–3 | Orchestrator (docs are Orchestrator-only) | **The register** `docs/operations/hr-follow-up-process-register.md`, pointer-index style like the SEC/QUAL registers: PM-readable summary · method section (universes, seeds, evidence bar, inclusion rule, declared coverage per universe) · one `HRP-NNN` row per process with every column populated or an explicit `NONE` / `UNKNOWN → Phase B`, each citing `file:line`: trigger · accountable role · deadline + SOURCE · surfacing today · aging · resolution + audit trail · **decision-readiness** · classification (designed workflow / API-only list / silent state / fail-loud dead-end) · Increment-4 relevance · the B-row boundary group · all nine ROADMAP-named members present or explicitly merged/split with reason · cross-cutting section: classification counts, the three shape options with trade-offs, an aging/escalation proposal for READY rows only with the missing fact named per NOT-READY row, a recommendation, and the Increment-4 design inputs as a citable checklist (worklist UI, termination screen, admin-create hire-date fix). Then **Reviewer REFUTE pass** (`reviewer`, Fable — opens every row's evidence; failed rows struck or corrected) and **Codex on the dossier**; both recorded below. Owner ruling (OQ-2 (b) scope) recorded at close. |
| **TASK-13910** | close | Orchestrator (docs) | **Governance.** PAT-008 → the shared helper + `WithFixedToday` + a sample matching the shared signature + the boot-order rule. Quality register: three new rows — (a) remaining raw `src/` C# clock reads, per-file counts; (b) unparameterised SQL clock sites with class + reach: `RoleConfigOverrideRepository.cs:444` (business-date WRITE, **unreachable** — not DI-registered, never constructed), `ReportingLineRepository.cs:1287` (WRITE `effective_to = CURRENT_DATE`), `docker/postgres/init.sql:4311-4315` (the same write inside a SQL function — invisible to any C# grep), `LocalAgreementProfileMigrator.cs:385-386/430-431` (compare, startup), `DelegationExpiryService.cs:86` (compare, host-background), the four `expires_at > NOW()` authorization compares (`RoleAssignmentRepository.cs:24`, `IAuthorityFactsSource.cs:130`, `DesignatedApproverAuthorizer.cs:557`, `ReportingLineEndpoints.cs:2682`), `LocalConfigurationRepository.GetActiveByOrgAsync` (`:46-57`, **unreachable** — no callers); (c) the UTC-day vs Copenhagen-day split (OQ-3) for a ruling in its own task; **QUAL-153 → FIXED**. The census table into this log. ROADMAP: the HR item leaves the backlog, the Increment-4 entry gains the inputs, the QUAL-153 tooling row goes. CLAUDE.md doc map: the register row (Operations — durable). `docs/operations/model-routing-register.md`: the S139 row. `docs/sprints/INDEX.md`: the S139 row + anchor. `check_docs.py` green in CI. |

### Waves and dependencies

- **Wave 1 — four read-only `trace` agents in parallel:** 13901 ‖ 13902 ‖ 13903 ‖ 13905. No file writes; no
  worktrees needed. The Orchestrator (Opus) consolidates the HR output into the 13904 draft while wave 2 runs.
- **Wave 2 — disjoint scopes, parallel:** 13906 (`tests/**`) ‖ 13907 (`src/**`); then 13909 sequenced after
  13906 (it needs the exact dependents list and the new namespace). Orchestrator builds after all three:
  `dotnet build StatsTid.sln -c Release --no-incremental` 0 errors; Unit + non-Docker Regression green locally.
- **Wave 3:** 13908 (needs 13905's conversion set + 13906's fixture; its pins and the probe go green in CI only
  with 13907 in place). **Step 5a** dual-lens on 13907's product diff (Reviewer: architecture / invariants / the
  "tests can fail" check across 13906 + 13908; Codex `codex review` prompt-alone: domain correctness of the seam,
  the SQL parameter binding, the UTC-day claim). Reviewer REFUTE pass + Codex on the 13904 register.
- **Close:** 13910; the full local suites (Unit, DemoSeed, non-Docker Regression, frontend + `tsc`); CA2100 at
  baseline 115; **Step 7a** dual-lens on the whole uncommitted sprint diff; commit + push; ONE background
  `gh run watch` and its notification — never polled; CI-green backfill on the `**Test Verified**` line.

### Risks carried from the refinement (short form — the full list is rev 3 § Risks & Conflicts)

Two clocks, one database (exactly two SQL statements on the converted request paths — both parameterised);
case-sensitive tooling (regex pinned `-i`); day-boundary semantics (OQ-3 (a) written into 13907's spec; the fixed
provider pins UTC midnight, so the tests CANNOT detect a wrong helper — the decision is written down, not tested);
seam over-reach into security timestamps (excluded by definition); a converted pin proving something different
(RED condition per pin + the Reviewer's "tests can fail" check); boot order (PAT-008:37 in both task specs); sweep
completeness for Part A (three seed families + status columns + hosted services + the ROADMAP list + REFUTE pass;
coverage declared per universe); scope creep into UI design (inputs named, design is S140's); the first live run
of the routing hooks (fail-open on internal error; blocks logged to `.claude/telemetry/model-routing.log`).

## Wave 1 acceptance (Orchestrator, 2026-09-07)

All four spawns logged by `model-routing-guard.ps1` as `trace | ALLOW | sonnet-tier role` — the first live
evidence for the model-routing register. No agent modified a project file; each wrote one report under
`.claude/sweeps/S139/`.

### TASK-13901 — HR hand-off sweep, universe (i): the endpoint layer — **complete**

| Field | Value |
|-------|-------|
| **Agent** | `trace` (Sonnet) · 104 tool uses · ~255k tokens |
| **Constraint Validator** | n/a — read-only; one report file, no `src/` / `tests/` / `docs/` change |
| **Reviewer Audit** | deferred to the TASK-13904 REFUTE pass (every row's evidence is re-opened there) |
| **Coverage** | EXHAUSTIVE: 25 endpoint files (26 347 lines) + `ApiEndpoints.cs` + 4 `Helpers/` (plumbing only). Seeds: 169 comment hits / ~180 409-422 sites / 259 role-gated mutations. Confidence caveat recorded: ~15–20 org-hierarchy 409s in `AdminEndpoints.cs:1394-2537` classified by pattern |
| **Output** | 10 candidate rows + 1 not-a-row + 11 B-rows; exactly ONE 409/422 site classified LATER-HUMAN-ACTION (`EmploymentDateEndpoints.cs:559-591`) |
| **Files** | `.claude/sweeps/S139/TASK-13901-endpoints-sweep.md` |

### TASK-13902 — universe (ii): Infrastructure, hosted services, events, DB lifecycle columns — **complete**

| Field | Value |
|-------|-------|
| **Agent** | `trace` (Sonnet) · 49 tool uses · ~271k tokens |
| **Constraint Validator** | n/a — read-only |
| **Reviewer Audit** | deferred to the REFUTE pass |
| **Coverage** | EXHAUSTIVE: 87 Infrastructure source files (9 IN) · 94 event types classified · 68 tables walked (6 IN) · the three hosted services described (poll cadence, what each creates, what each resolves alone) |
| **Output** | 9 rows; 3 silent states (`role_assignments.expires_at` has 6 readers and 0 writers; the outbox quarantine's promised ops query does not exist; the payroll-cutoff aging rule for `approval_periods` has no poller, column or writer anywhere) |
| **Files** | `.claude/sweeps/S139/TASK-13902-infrastructure-sweep.md` |

### TASK-13903 — universe (iii): rule engine, Orchestrator loop, frontend surface, spec sources — **complete**

| Field | Value |
|-------|-------|
| **Agent** | `trace` (Sonnet) · 104 tool uses · ~226k tokens |
| **Constraint Validator** | n/a — read-only |
| **Reviewer Audit** | deferred to the REFUTE pass |
| **Coverage** | SAMPLED as declared: every `App.tsx` route + gate (exhaustive over routes; 6 admin config pages grep-checked only); `RestPeriodRule.cs`, `ComplianceEndpoints.cs`, `OrchestratorControlLoop.cs`, Orchestrator `Program.cs` in full; SYSTEM_TARGET §F/§H/§K/§N; ADR-013/033/040; `danish-agreements.md` deadline grep |
| **Output** | frontend verdict (the refinement claim CONFIRMED for the worklist + the whole settlement family, NUANCED by the organisation page's embedded tiles, WORSE in one place: a finished overtime pre-approval page never routed); 8 spec obligations (7 with backend, 0 with frontend, 1 with no code); the compensatory-rest feature is dead (`CreateAsync` / `GrantAsync` have no callers) |
| **Files** | `.claude/sweeps/S139/TASK-13903-rules-frontend-spec-sweep.md` |

### Orchestrator spot-checks that settled the sweeps' open doubts (greps, 2026-09-07)

- Termination payout request OPEN → LINE_STAGED is promoted by the Payroll host's `SettlementExportEmitter.cs:597-598`
  when it consumes the settlement event; a failed consumption lands in the settlement inbox `RETRY_PENDING` /
  `DEAD_LETTER`, which no endpoint exposes (register HRP-008, O-rows).
- No layer censuses "profile row without a covering agreement row": every `EmployeeProfileNotFoundException`
  consumer catches or fails closed (`BalanceEndpoints.cs:139/754`, `DailyNormCalculator.cs:210`,
  `ComplianceEndpoints.cs:114`, `PeriodCalculationService.cs:458`) — HRP-015 stands.
- `SettlementReversalService.cs` writes no worklist row after a bare reversal — HRP-009 stands.
- `compensatory_rest` GRANTED has exactly one writer (`CompensatoryRestRepository.cs:71`, `GrantAsync`) and it has
  no caller — HRP-020 confirmed dead.
- Approval deadlines are hard-coded at period creation (`SkemaEndpoints.cs:524-525`: month-end + 2 / + 5 days) and
  stored (`ApprovalPeriodRepository.cs:1768`); nothing reads them except the Skema DTO. The organisation page's
  "godkendere efter frist" tile counts every manager with a pending SUBMITTED month
  (`StrukturPanel.tsx:941-943`) and never compares against a deadline — HRP-012's label overclaims.
- `VacationSettlementEndpoints.cs` has one `MapGet` (payout-pending); §21 transfer agreements are POST + PUT only —
  HRP-010 is write-only.

### TASK-13904 — the register — **DRAFT written** (`docs/operations/hr-follow-up-process-register.md`)

21 `HRP` rows (3 designed workflow · 4 API-only · 5 silent state · 4 fail-loud dead-end · 3 computed / dead /
spec-only · 2 ruled not-a-hand-off) + B-rows + O-rows + ruled-OUT list; decision-readiness: 6 READY, 13 NOT READY
(12 for the same missing fact: no stated deadline); shape options with trade-offs; recommendation: shape 3
(hybrid landing tiles → per-process lists); aging proposals for READY rows only; Increment-4 checklist.
**Pending:** Reviewer REFUTE pass · Codex on the dossier · owner ruling at close. **QUAL candidates for
TASK-13910:** the dead compensatory-rest feature (HRP-020); `reconcile-payout` emits no outbox event (HRP-006);
`role_assignments.expires_at` has no writer, list or audit (HRP-014 b).

### TASK-13905 — clock census (QUAL-153) — **complete**

| Field | Value |
|-------|-------|
| **Agent** | `trace` (Sonnet) · 121 tool uses · ~251k tokens |
| **Constraint Validator** | n/a — read-only |
| **Reproducibility** | the three pinned `rg` commands and raw counts head the report: C# clock reads **385** (268 tests / 117 src) · SQL clock reads, case-insensitive **204** (115 src / 89 `init.sql`) · `TimeProvider` / `GetUtcNow()` **123** (43 src / 80 tests); no worktree contamination |
| **Confirmed** | product sites `EmployeeProfileRepository.cs:375/502/689` (`:505` passes `today` INTO the pure router), `EmployeeProfileEndpoints.cs:271/945`, `UserAgreementCodeRepository.cs:248`, `AdminEndpoints.cs:927/1091/1324/1578/2463` (+9 H-none audit timestamps); SQL business-date sites `EmployeeProfileRepository.cs:816` (live WRITE) and `ApprovalPeriodRepository.cs:658` (compare, reachable from production `AdminEndpoints.cs:3190` and four suites); `RoleConfigOverrideRepository.cs:444` dead; `LocalConfigurationRepository.GetActiveByOrgAsync` (`:46-64`) zero callers; `init.sql:4312` is the one business-date write there; **no time-zone override anywhere** → the Postgres session is UTC; constructors 7 + 12 + 14 = **33** direct constructions, none take a `TimeProvider`; `FixedTimeProvider` dependents 12 + 3 = **15** (+2 comment-only) |
| **Corrected / new** | `EmployeeEntitlementEligibilityRepository.cs:421` is a timestamp write only (regex near-miss); `Program.cs:387-389` comment ("year-overview ONLY") is stale — the seam already serves `SkemaEndpoints.cs:2052`, `BalanceEndpoints.cs:735`, the worklist repo and the 7 `CopenhagenBusinessDate` callers; `SkemaEndpoints.cs:218` is raw while `:2052` in the same file is on the seam; `DesignatedApproverAuthorizer.cs:147/322` fall back to raw `DateTime.UtcNow` with no `TimeProvider` field; `ConfigEndpoints.cs:196` is self-declared known debt; **`EntitlementQuotaCheckUsesYearStartTests.cs` is a hazard the refinement never named** — no date arithmetic, a `today.Month >= ResetMonth` branch that hard-fails every 1 September; found only by a secondary `.Month/.Year` scan, so the offset proxy is known-incomplete over ~65 UNVERIFIED files |
| **Conversion set** | 14 suite files marked hazardous — 13 census entries, one covering two roster suites (see the ruling below); H-none confirmed: `EntitlementConfigFullDayOnlyAdminTests`, `EntitlementConfigEndpointTests`, `TerminatedEmployeeAccessTests`, `S105UnitLeaderApprovalTests` (seeds `expires_at = NOW() - INTERVAL '1 day'` in SQL — self-consistent by construction, the counter-example to keep), `JwtTokenServiceTests` |
| **Files** | `.claude/sweeps/S139/TASK-13905-clock-census.md` |

### Orchestrator ruling — the conversion-set scope (Fable, 2026-09-07)

**What the census changed.** The refinement expected three hazardous suites "plus whatever the census marks";
the census marks fourteen suite files (13 census entries — one entry covers the two roster suites). Their exercised product paths fall into three groups, and only the first sits
inside the seam the owner ruled (OQ-1 (a): profile path incl. the soft-delete SQL write, the agreement-code
path, and the one reachable SQL compare `ApprovalPeriodRepository.cs:658`):

| Group | Suites | Product clock on their path | Ruling |
|-------|--------|-----------------------------|--------|
| **1 — inside the ruled seam** | `ProfileBackdatingEndpointTests`, `Adr032RevaluationTests`, `EmployeeProfileLifecycleTests` (profile path) · `AgreementCodeBackdatingEndpointTests` (agreement-code path) · `PeriodStatusAndPersonSearchReadsTests`, `S106RosterUnitTagTests`, `MedarbejderRosterReadTests`, `S106SeedScalePerfTests` (all call `GetPeriodStatusProjectionForTreeAsync` → `:658`) | the TASK-13907 sites | **CONVERT in TASK-13908 (8 suites)** |
| **2 — C#-side hazards on paths the census did not trace for product raw reads** | `AdminVikarOnBehalfTests`, `DesignatedApproverAuthorityTests`, `EmploymentDateGuardTests`, `EmploymentEndDateLifecycleTests`, `EntitlementQuotaCheckUsesYearStartTests` | probably `SkemaEndpoints.cs:218`, `ApprovalEndpoints.cs:319/505/1592`, `DesignatedApproverAuthorizer.cs:147/322`, `ReportingLineEndpoints` validation — a fixed PAST test today with `+30`-day windows would be "in the past" to any raw product read on the path | **DEFER** — each needs a per-suite product-path trace first; convert in a second tranche with its seam |
| **3 — needs a new seam the ruling did not name** | `ManagerVikarEngineTests` (calls `DelegationExpiryService.CloseExpiredDelegationsAsync` directly; its SQL `until_date < CURRENT_DATE` at `:86` is the real DB clock — a fixed past today would make BOTH seeded rows expire) | `DelegationExpiryService.cs:86` (a hosted service — the refinement registered it as host-background, not parameterised) | **DEFER** with the dependency named |

**Why not widen now.** OQ-1 (a) was ruled hours ago with option (b) (product-wide seam) on the table, and the
refinement's own text names `DelegationExpiryService` as registered-not-parameterised. Widening into a hosted
service and the designated-approver authorizer (the security-adjacent as-of reads) would roughly double the
Step-5a surface of an analysis sprint. The eight Group-1 suites include every suite that has actually flaked
(S138) and the OK-version straddle. **Default: groups 2–3 → one QUAL row ("QUAL-153 second tranche") with the
per-suite dependency named; the owner may widen to all 13 by saying so — TASK-13907 would then gain
`DelegationExpiryService.cs:86`, `DesignatedApproverAuthorizer.cs:147/322`, `SkemaEndpoints.cs:218` and the
`ApprovalEndpoints` as-of reads, and TASK-13908 the five suites.**

**Also ruled into TASK-13907 (cheap, in the area):** the stale `Program.cs:387-389` comment. **Registered, not
fixed:** `DesignatedApproverAuthorizer` raw fallback (open doubt: does any production caller omit `asOf`?),
`ConfigEndpoints.cs:196`, `ReportingLineRepository.cs:1287` (reach unverified), the ~65 UNVERIFIED test files
(secondary `.Month|.Year|.DayOfWeek` scan recommended; `FeriehindringResolutionTests`, `SiblingReadMonthGateTests`
named), the Payroll host's missing `TimeProvider` registration (checked below).

## Wave 2 acceptance (Orchestrator, 2026-09-07)

### TASK-13906 — fixed-clock fixture + `WithFixedToday` + the falsifiability probe — **complete, with one declared deviation (Orchestrator's spec error)**

| Field | Value |
|-------|-------|
| **Agent** | `test-qa` (Sonnet) · 68 tool uses · ~238k tokens |
| **Constraint Validator** | runs once over the whole wave-2 output after TASK-13909 (below) |
| **Reviewer Audit** | the "tests can fail" check rides the Step-5a review of TASK-13907 (the probe is that review's evidence) |
| **Build** | `dotnet build tests/StatsTid.Tests.Regression -c Release` → 0 errors (126 pre-existing warnings); the census regex over the three files → 0 hits. Docker-gated: the probe is RED-by-design until TASK-13907 lands and is verified only in CI |
| **Files** | NEW `tests/StatsTid.Tests.Regression/Hosting/SharedFixedTimeProvider.cs` (both constructors) · `Hosting/StatsTidWebApplicationFactory.cs` (`WithFixedToday(DateOnly)` mirroring `WithThrowingOutbox()`, PAT-008 boot-order rule in the doc comment) · NEW `Hosting/FixedClockProbeTests.cs` (`[Trait("Category","Docker")]`) |

**The deviation, and whose error it was.** The task spec said the new `FixedTimeProvider` could coexist with the old
`internal` one "because nothing imports both namespaces". That premise was **false** — twelve suites already carry
`using …Regression.Balance; // FixedTimeProvider` AND `using …Regression.Hosting;` (for the factory), and the first
build produced 16 `CS0104` ambiguities. Barred from touching other files, the agent named its class
`SharedFixedTimeProvider` and declared the choice instead of adapting silently — exactly the behaviour the
prompt asked for. **Ruling:** TASK-13909 deletes the old class, re-points the fifteen dependents, and renames
`SharedFixedTimeProvider` → `FixedTimeProvider` so the end state matches the refinement (one class, in `Hosting/`).
The spec error is the Orchestrator's, recorded for the retrospective. Secondary consequence: doc comments that
quoted `DateTime.UtcNow` literally tripped the plain-text census regex and were rephrased.

**The probe, as written (RED conditions in the code):** Leg 1 profile PUT `F+1` → 422 / `F` → 200 (RED if the
guard reads the real clock: `F+1` is deep in the past → 200); Leg 2 the same on the dedicated agreement-code
PUT; Leg 3 soft-delete — row `effective_to` == event `EffectiveTo` == `F`, two independent RED conditions (row =
real today if `EmployeeProfileRepository.cs:816` is unconverted; event = real today if
`EmployeeProfileEndpoints.cs:945` is); Leg 4 a plain host resolves `TimeProvider` to `TimeProvider.System`
(`Assert.Same` — a DI fact); **Leg 5 deliberately omitted**: the PUT/GET bodies are date-free by design (ADR-040
D7), so there is no served `today` to byte-pin — explained in the class doc. **Fixture employee** seeded by direct
SQL after the fixed host's first `CreateClient()` with hire date `F − 100` (mirroring
`ProfileBackdatingEndpointTests.SeedEmployeeAsync`), NOT through the admin-create POST — because that POST's
"hired today" default is itself a converted site, and seeding through it would make every leg depend on two
conversions at once. **Declared, not covered:** `ApprovalPeriodRepository.cs:658` has no probe leg (the converted
roster suites in TASK-13908 exercise it); the admin-create default has none (a future leg).

## Wave 3 acceptance (Orchestrator, 2026-09-07)

### TASK-13908 — the eight Group-1 suites converted to constant anchored dates — **complete, four declared deviations accepted**

| Field | Value |
|-------|-------|
| **Agent** | `test-qa` (Sonnet) · 267 tool uses · ~581k tokens (the sprint's largest task) |
| **Constraint Validator** | runs once over TASK-13908 + the W2 follow-up files at W2's acceptance |
| **Reviewer Audit** | the "tests can fail" check over the nine files rides the Step-5a cycle-2 Reviewer run |
| **Build / local suites** | Regression project 0 errors; census regex over the nine files → 0; `OnWeekday` / `NextWeekday` → 0 calls (three historical doc mentions remain, explaining what was removed); Unit 1235 · DemoSeed 165 · non-Docker Regression 102 green. The eight suites themselves are Docker-gated → CI-verified at close |
| **Files** | `EmployeeProfile/ProfileBackdatingEndpointTests.cs`, `EmployeeProfile/Adr032RevaluationTests.cs`, `EmployeeProfile/EmployeeProfileLifecycleTests.cs`, `Admin/AgreementCodeBackdatingEndpointTests.cs`, `Approval/PeriodStatusAndPersonSearchReadsTests.cs`, `Approval/S106RosterUnitTagTests.cs`, `Approval/MedarbejderRosterReadTests.cs`, `Performance/S106SeedScalePerfTests.cs` + `Performance/S106SeedScalePerfFixture.cs` (its dates feed the suite) |

**Anchors.** Seven suites on `F = 2025-03-12` (Wednesday, OK24 side — for the H-okversion suite, over a year clear of
the 2026-04-01 cutover in either direction, so no −120-day offset can straddle it); the perf suite on `F = 2026-02-11`
(Wednesday, OK24 side) because its fixture hard-codes `reporting_lines.effective_from = '2026-01-01'` at several seed
sites and rebasing that literal would be a wider change for a suite that asserts command counts, not dates. Every
suite asserts its anchor's weekday and OK side once. Pins converted with a `// RED:` comment: 20 + 4 + 11 + 15 + 5 + 2 + 3 + 5 = **65** (the Step-7a Reviewer recounted the
agreement-code suite at 15).

**Both clocks fixed where both exist.** WAF hosts via `WithFixedToday(F)`; direct constructions via the new optional
parameter (`new EmployeeProfileRepository(_harness.Factory, new FixedTimeProvider(F))`,
`new ApprovalPeriodRepository(_dbFactory, authorizer, reportingRepo, new FixedTimeProvider(F))`). The roster suites'
HTTP path was confirmed to reuse `GetPeriodStatusProjectionForTreeAsync` (`ApprovalPeriodRepository.cs:1079`), so the
tile counts needed the fixed clock even over HTTP.

**A pin got STRONGER.** `Adr032RevaluationTests.FractionChange_RevaluationPastCap_Succeeds_NoClampNo500` had an
`if (booked >= 13)` branch whose truth depended on which real day the ferieår tail was measured from; under `F` the tail
(2025-03-13 → 2025-09-01) is always ~120 weekdays, `booked` deterministically reaches 24, and the strict past-cap
assertion now fires unconditionally. This is the S138 lesson in reverse: a floating date had been hiding a conditional
assertion.

**Declared deviations — all four ACCEPTED:** (1) `ProfileBackdatingEndpointTests`: two seeded offsets moved a few days
to land on weekdays under `F` (−25 → −20, −60 → −58; weekday named inline) — the pins' meaning is unchanged, only the
literal day; (2) `Adr032RevaluationTests`: the rule-engine stub and the fixed clock registered in ONE
`ConfigureTestServices` (both must land in the same derived container) instead of chaining `WithFixedToday`; the
class doc's stale claim that "FixedTimeProvider does NOT help here" corrected (it was true before S138 — the endpoint
now reads the provider); (3) suites 5–7: `MakeLine`'s literal `EffectiveFrom = 2026-01-01` rebased to `F.AddYears(-1)`,
because the projection's phase-2 approver resolution now resolves lines as of `F` (`:748` → `asOf: today`), so a line
starting after `F` would never resolve and the per-manager tallies would silently break — a genuine consequence of
fixing the clock, correctly caught; (4) the perf suite's distinct anchor (above).

**Product raw reads found ON a converted suite's path (the deliverable the spec asked for):**
`ApprovalPeriodRepository.cs:287` (`GetPendingForDesignatedReportsAsync`) is called directly by
`PeriodStatusAndPersonSearchReadsTests.PerManagerPendingCount_RoleRevokedResolvedApprover…` and still read
`DateTime.UtcNow`. The agent judged that pin not hollow (it asserts a role-floor denial, not a date boundary) — but the
read IS on a converted path, so under OQ-1 (a) it belongs to the seam. **Converted by the Orchestrator under the Small
Tasks Exception** (second one-liner in the same file, same already-reviewed form; the file's remaining raw reads are
`:238` and `:488`, off every converted path → the QUAL row). Also hit by the `FAIL_004_*` tests, with no `asOf`
supplied: the fallbacks at `ReportingLineRepository.cs:1043` and `DesignatedApproverAuthorizer.cs:147/322` — those
tests assert structural properties (never-self, depth, resolves-to-C), not dates → not hollow → QUAL row material.
**Noticed, not fixed (routed to the W2 follow-up):** two stale doc comments in `EmployeeProfileLifecycleTests` claim
the backfilled row sits at `effective_from = today`; the seeder stamps `'0001-01-01'`.

**W2 follow-up — landed (same agent, resumed; the first resume was cut off by a rate limit before any edit — verified
clean, restarted).** `EmployeeProfileLifecycleTests.SupersedeAndCreateAsync_RepositoryGuard_FutureDated_ThrowsTemporalWriteRejectedException_ThenSameDateSucceeds`
(direct `EmployeeProfileRepository` with `FixedTimeProvider(F)`, hire date `F − 100`; `F+1` → `TemporalWriteRejectedException`
with `Reason == TemporalWriteRejection.FutureDated`, thrown at `EmployeeProfileRepository.cs:521-522` off `:518`; `F` →
`SaveEmployeeProfileOutcome.Created`; RED condition: on the real clock `F+1` is an ordinary past date, `IsFutureDated`
is false and nothing throws — "the exact gap the endpoint-level probe cannot see") and its twin in
`AgreementCodeBackdatingEndpointTests` § "C. Repository-level guard" (chosen over `UserAgreementCodeRepositoryTests`
because it is already anchored on `F` and seeds a user with an agreement-code row — zero new infrastructure; guard
`UserAgreementCodeRepository.cs:260/:263-264`). The probe's Leg 1–2 docs now say they pin the ENDPOINT guards and name
the two repository tests. The two stale `emp001 … effective_from = today` comments corrected: the seeder omits the
column (`EmployeeProfileSeeder.cs:100-104`), so the row takes the schema default `'0001-01-01'` (`:85` says why). Build
0 errors; census regex 0 over the three files. Both new tests Docker-gated → CI.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved: the clock seam is the existing DI `TimeProvider` (optional trailing
      constructor parameters defaulting to `TimeProvider.System`; plain type registrations, no lambdas — verified by a DI
      probe); the register is docs; Constraint Validator pass on waves 2 and 3.
- [x] P2 — Rule engine determinism: no rule-engine change; `TemporalWriteRouter.cs` diff empty (`today` still a
      parameter, PAT-025).
- [x] P3 — Event sourcing: no event-contract change (`EventSerializer.cs` diff empty); soft-delete stays a row-state
      change (ADR-023 D8) and its row and event now carry the same date BY CONSTRUCTION (W1).
- [x] P4 — OK-version correctness: the H-okversion census class; every converted anchor asserts its OK side once
      (`F = 2025-03-12`, over a year clear of the 2026-04-01 cutover); the agreement-code suite can no longer straddle it.
- [x] P5 — Integration isolation & delivery: untouched (no outbox / consumer change); two auditability gaps FOUND and
      registered, not introduced (QUAL-159, QUAL-160).
- [x] P6 — Payroll boundary: `ApprovalPeriodRepository` compare parameterised, semantics unchanged (UTC session);
      Step-5a dual-lens on the seam — Codex CLEAN, Reviewer 0 B; no export or wage-type code touched.
- [x] P7 — Security & access control: token minting and audit stamps stay on the real clock BY DESIGN (nine
      `AdminEndpoints.cs` sites + `OccurredAt` untouched, verified by both lenses); `RequireAuthorization` unchanged on
      every changed endpoint; authorization as-of reads classified business-date and left for QUAL-154.
- [ ] P8 — CI/CD enforcement: local gates green (build 0 errors / 145 warnings = baseline; CA2100 115 = baseline; Unit
      1235 · DemoSeed 165 · non-Docker Regression 102); the ~30 converted / new Docker-gated pins and `check_docs.py`
      verify in the watched CI run at close — **ticked at CI green**.
- [x] P9 — Usability & UX: Part A's register names the Increment-4 inputs and recommends the surface shape; no UI
      built (analysis sprint by ruling).

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | no rule change; the register RECORDS the statutory deadlines it found (Ferieloven §21 31 Dec; §24/§25 boundary; HK Stat 3-month afspadsering) as deadline SOURCES, ruling nothing |
| Wage type mappings produce correct SLS codes | N/A | untouched |
| Overtime/supplement calculations are deterministic | verified | `TemporalWriteRouter` pure and byte-unchanged; the rule engine untouched |
| Absence effects on norm/flex/pension are correct | N/A | the ADR-032 weekend rule untouched; the converted pins now seed absences on weekdays BY CONSTANT, not by nudge |
| Retroactive recalculation produces stable results | N/A | untouched; the register's HRP-003 documents the manual ADR-013 path and its `GlobalAdminOnly` floor |

## Test Summary

| Suite | S138 baseline | S139 local (final tree) | S139 CI | Status |
|-------|---------------|-------------------------|---------|--------|
| Unit | 1235 | **1235** | — | green locally |
| DemoSeed | 165 | **165** | — | green locally |
| Regression, non-Docker | 102 | **102** | — | green locally |
| Regression, Docker-gated | 1697 (of 1799) | not runnable here | pending | new: the probe (4 legs) + 2 repository-guard tests + 8 anchor facts; converted: 65 pins across 8 suites |
| Smoke | 7 | not runnable here | pending | |
| Frontend | 735 | **735** (59 files) + `tsc --noEmit` clean — no FE change, run for the record | pending | green locally |
| **Total** | **3941** | | **pending CI** | |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 10 (13901–13910) + 2 follow-ups (W1, W2) |
| Agent spawns by role (model) | 4 `trace` (Sonnet) · 2 `test-qa` (Sonnet, one resumed twice) · 1 `backend-infrastructure` (Opus, resumed once) · 1 `sweep` (Haiku) · 2 `constraint-validator` (Sonnet) · 5 `reviewer` (Fable) — **zero guard blocks**, zero reviewer refusals |
| Constraint Violations | 0 (two validator passes) |
| Reviewer Findings | register: cycle 1 0B/5W/5N, cycle 2 0B/1W/4N, cycle 3 0B/0W/1N · seam Step 5a: 0B/2W/7N (cycle 2 pending) |
| External Review Cycles | register 3 (BLOCKED → BLOCKED → APPROVED) · seam Step 5a 1 (CLEAN) + cycle 2 pending · sprint-end pending |
| External Findings | register: 2B/4W/2N → 2 residuals → 0 · seam: 0 |
| Re-dispatches | 2 (W1 to backend-infrastructure, W2 to test-qa — both by resuming the original agent with its context) + 1 rate-limit restart |
| Orchestrator direct edits under `src/` | 2 one-liners (Small Tasks Exception, both the same already-reviewed conversion form, both disclosed) |
| First-Pass Rate | 8 of 10 tasks accepted without a code re-dispatch (13906's naming deviation and 13907's W1 were the two) |

## Task Log

The per-task records live in the acceptance sections above (Wave 1 / Wave 2 / Wave 3 acceptance) — one block per task
with agent, validator, reviewer, files and evidence — so each task's evidence sits next to its ruling.

## Review (Step 5a / 7a — both lenses)

_Step 5a on TASK-13907's product diff; the REFUTE pass + Codex on the TASK-13904 register; Step 7a on the whole sprint diff. Artifacts: `.claude/reviews/SPRINT-139-step7a-{codex,reviewer}.md` (the reviewer artifact must open with `reviewed-by-model: claude-fable-5-1`)._

### TASK-13904 register — cycle 1 (2026-09-07): Codex **BLOCKED** (2 B / 4 W / 2 N) · Reviewer **APPROVED-WITH-WARNINGS** (0 B / 5 W / 5 N, `reviewed-by-model: claude-fable-5-1`)

The lenses diverged as designed. **Codex** held the register to its own decision-readiness rule and found it
applied loosely; **the Reviewer** opened every citation, corrected six cells, and found three hand-offs all three
sweeps had missed. Artifacts: `.claude/reviews/SPRINT-139-task13904-codex.md`; the Reviewer's output is absorbed
below (it writes no file).

**Codex BLOCKER 1 — readiness rule applied loosely — ACCEPTED (Orchestrator's own error).** READY requires a
stated deadline SOURCE (law / agreement / institutional) AND a known role. I had counted a developer default
(`SkemaEndpoints.cs:524-525`, month-end + 2 / + 5) as "institutional", "immediate by consequence" as a deadline,
and "no deadline by nature" as satisfying the rule. Ruling: HRP-012, HRP-015, HRP-013 → NOT READY with the ruling
that would unlock them named; HRP-017 (config drafts) → ruled OUT of the strict definition (same-actor work in
progress, kept as the pattern to copy); **READY = HRP-010 only**. Aging proposals confined to it (OQ-2 (b)); what
each missing ruling would unlock is listed as information, explicitly not proposed for ruling.
**Codex BLOCKER 2 — HRP-018 is a leader B-row — ACCEPTED.** Overtime pre-approval's resolver is the leader
(`OvertimeEndpoints.cs:355/435/508` are `LeaderOrAbove` — Reviewer); moved to B-rows. The finished-but-unrouted
page stays as a UX/QUAL finding and an Increment-4 input.
**Codex W — HRP-014 "0 write sites" was false (Reviewer concurred):** `expires_at` is written at assignment
(`AdminEndpoints.cs:2922-2932`) and manually revocable (`:3082`); the true gap is that nothing closes, lists,
alerts on or audits the expiry transition. **Codex W — HRP-010 is write-only, not an API-only list** → "silent /
write-only state"; the Reviewer added: no outbox event exists for a transfer agreement at all (repo audit table
only, `VacationTransferAgreementRepository.cs:213-222`) → QUAL candidate. **Codex W — a failed ordinary payroll
delivery leaves a `failed` outbox envelope "for ops"** (`PayrollExportService.cs:232-243`) → new O-row; the Reviewer
added the External host's `DEAD_LETTER` (`DeliveryTracker.cs:52`) → new O-row. **Codex W + Reviewer W —
arithmetic:** the headline said nineteen processes over 21 rows and double-counted HRP-013; everything recounted
from the rows.
**Reviewer W — MISSED HAND-OFF, the strongest finding of the cycle: every approved month is exported to payroll
by a manual per-employee call** (`POST /api/payroll/calculate-and-export`, `LocalAdminOrAbove`, Payroll
`Program.cs:248/:388`); no hosted service exports, no frontend caller (`grep api/payroll frontend/src` excluding
generated types: 0), no Backend GET lists APPROVED periods lacking a `payroll_export_records` row; SYSTEM_TARGET
§H:178 states the duty and §G:167 names "cutoff dates" as configuration that does not exist → **HRP-022**, silent
state, NOT READY (missing: the institutional cutoff). **Reviewer W — HRP-005 sub-case:** the R7b refused
termination (`VacationSettlementService.cs:180-196`) emits `SettlementManualReviewFlagged` + audit + log and
writes NO row, and the R3 anti-join (`SettlementCloseService.cs:392-400`) never re-fires it — the per-employee
balance flag cannot show it → **HRP-005b**. **Reviewer W — pre-go-live manual settlement is spec-stated
(SYSTEM_TARGET:365; `SettlementCloseService.cs:49-58` dormant when unconfigured) with no endpoint to record it**
→ **HRP-023**, spec-only. **Reviewer W — READY-only:** HRP-013 sat in the aging table while NOT READY → removed.
**Reviewer NOTEs absorbed:** HRP-003's ADR-013 quote was a paraphrase presented as a quote → rewritten, endpoint
cite added (Payroll `Program.cs:393`, `GlobalAdminOnly` `:475` — the role is now KNOWN); `/api/approval/pending`
lists SUBMITTED **and** EMPLOYEE_APPROVED (`ApprovalPeriodRepository.cs:128/:141`); api-types cites 3448/3481;
"0 of 21 compute aging" → "0 compute escalation; 1 computes expiry and tells nobody"; ADR-034 D4 actually PERMITS a
Backend read-only read of `payroll_export_records` (the Backend does so at `ApprovalEndpoints.cs:1628`), so the
case for shape 3 over shape 1 rests on the differing resolution VERBS, not on context ownership — the register's
argument corrected; HRP-012 "Ikke indsendt" also excludes orphans (`StrukturPanel.tsx:942`); HRP-017 route cites
corrected (POST `:98`, clone `:179`, publish `:435`).
**Both lenses PASS:** ADR-034 boundary; no auto-action (ADR-013 / ADR-033 D10) in any proposal.
**Cycle 2 (verification of the absorption) runs on both lenses next.**

### TASK-13904 register — cycle 2 (2026-09-07): Codex **BLOCKED** (2 residuals + 2 cite fixes) · Reviewer **APPROVED-WITH-WARNINGS** (0 B / 1 W / 4 N)

Both lenses confirmed every cycle-1 absorption RESOLVED except two residuals, which they described from
different angles and which are now fixed in rev 3:

- **OQ-2 (b) confinement** — Codex FAIL, Reviewer "faithful with two soft spots": the NOT-READY column "what a
  ruling would unlock" still pre-designed behaviour ("escalation to HR at manager deadline + N days"), and shape
  3's "oldest age" on every tile was unlabelled. **Fixed:** the column now names capability only (a computable
  age, a truthful overdue count), the section header says no rule is proposed, and "oldest age" is stated to be an
  age FACT, not an aging rule.
- **HRP-005b counted inconsistently** (Codex) — a row in the table, folded into HRP-005 in the counts. **Fixed:**
  counted as the row it is → **15 IN rows, 7 silent, 14 NOT READY**; every headline recomputed.
- **Cite drift** (both): `role_assignments` INSERT is `AdminEndpoints.cs:2946-2956` (grant route `:2852`), revoke
  `:3035`; `/api/approval/pending` filters at `ApprovalPeriodRepository.cs:141/154/293`; HRP-018 spans
  `OvertimeEndpoints.cs:147-240 / 300-355 / 359-435 / 439-508`. Root cause of the drift: TASK-13907 was editing
  `AdminEndpoints.cs` and `ApprovalPeriodRepository.cs` while the reviews ran; rev 3 cites the post-13907 tree and
  says so in its header.
- **HRP-022 "the Backend reads that table only at…"** overclaimed (Codex W, Reviewer N): the worklist repository also
  reads `payroll_export_records` (`HrBackdateWorklistRepository.cs:514, 611`). Narrowed; the conclusion (no
  approved-but-unexported list exists) holds.
- **Reviewer W — the one aging proposal (HRP-010) rests on a projection**: before the 31 Dec close there is no exact
  §21 residual (S65 research, `ferie-transfer-timing-research.md:9`); the candidate set would come from the
  beyond-cap projection at `BalanceEndpoints.cs:1162-1167`. **Fixed:** caveat written into the proposal; the owner is
  asked to accept a projection-based list knowingly.
- **Reviewer N — role mismatch on the worklist's recalculate action**: the worklist is `HROrAbove`, the recalculate
  endpoint `GlobalAdminOnly` → an HR user would see a button they cannot press. Recorded in the Increment-4 checklist
  as a decision to make first (the register does not propose lowering the gate).
- **Reviewer N — the two Codex cycle-1 NOTEs** (negative-claim verification; ruled-out consistency) were absorbed with
  no edit needed — now listed here so the record is complete.
- **Reviewer N — wording tension** between "not proposed for ruling now" and the owner-ruling section inviting the
  cutoff ruling → reworded: missing-FACT rulings are welcome; aging PROPOSALS wait until a row is READY.

**Cycle 3 (verification of the cycle-2 fixes) runs on both lenses next; halt-and-prompt fires only if cycle 3
surfaces a NEW BLOCKER.**

### TASK-13904 register — cycle 3 (2026-09-07): Codex **APPROVED** · Reviewer **APPROVED** (0 B / 0 W / 1 N) — **terminal**

Both lenses verified every cycle-2 residual RESOLVED in rev 3: OQ-2 (b) READY-only holds (one aging proposal, for
the one READY row; the NOT-READY column names capabilities only; "oldest age" labelled an age FACT); arithmetic
recomputes from the rows (15 IN = 2 + 3 + 7 + 3; surfacing 2 + 3 + 1 + 9; READY 1 / NOT READY 14; gap 4; relocated
5); every cite re-opened in the post-13907 tree; the HRP-022 reader list is now exhaustive (a fresh grep finds
exactly the three cited readers); ADR-034 / no-auto-action hold. **Reviewer NOTE, applied as rev 3.1:** the
HRP-014 and HRP-015 missing-fact cells pre-shaped the owner's answer ("within N days", "must be fixed
immediately") → reworded neutrally ("must be fixed, and by when"). Artifacts: `.claude/reviews/SPRINT-139-task13904-codex.md`,
`…-codex-c2.md`, `…-codex-c3.md`; the Reviewer's three cycles are absorbed in this section. **The register is
review-complete and awaits the owner's ruling at close.**

### TASK-13909 — the `FixedTimeProvider` using-sweep — **complete**

| Field | Value |
|-------|-------|
| **Agent** | `sweep` (Haiku) · 26 tool uses · ~63k tokens |
| **Result** | old `internal` class deleted from `Balance/YearOverviewTests.cs` (was `:2462-2482`); `Hosting/SharedFixedTimeProvider.cs` → `Hosting/FixedTimeProvider.cs`, class renamed, references in the factory and the probe updated; the 12 `using …Balance; // FixedTimeProvider` lines removed (all 12 already imported `Hosting`; no restore needed); the 3 in-namespace files already imported `Hosting`. Final checks: exactly one `class FixedTimeProvider` (Hosting), zero `SharedFixedTimeProvider`, 18 consuming files |
| **Build** | Regression project 0 errors / 126 warnings (pre-existing) |

### Wave-2 validation (Orchestrator, 2026-09-07) — the tree with TASK-13906 + 13907 + 13909 + the `:748` one-liner

`dotnet build StatsTid.sln -c Release --no-incremental` → **0 errors, 145 warnings** (= baseline) · Unit **1235/1235** · non-Docker
Regression **102/102**. Docker-gated (the probe; later the converted suites): CI-verified at close. Constraint
Validator (Step 5α) and the Step-5a dual-lens on TASK-13907 dispatched on this tree.

### Wave-2 Constraint Validator (Step 5α, 2026-09-07) — **pass, no violations**

`constraint-validator` (Sonnet) · 37 tool uses · ~108k tokens. Nine checks, all pass: file scope per agent (13906
`tests/**` only; 13907 the six declared `src/` files; 13909 the 12 using-sweep files); no agent `docs/` edits (the
docs in the diff are the Orchestrator's — the validator flagged `PAT-008` as undisclosed in its brief; it is the
Orchestrator's S139 update, confirmed here); no new RuleEngine call; no `FindFirst("scopes")` / `http://localhost`;
every changed endpoint still `RequireAuthorization` (`EmployeeProfileEndpoints.cs:806/:999`, `AdminEndpoints.cs:1463/
2399/2805`); **CA2100 recounted directly: 230 raw lines → 115 distinct sites = baseline**; `TemporalWriteRouter.cs`
and `EventSerializer.cs` diffs empty; exactly one `FixedTimeProvider`, zero `SharedFixedTimeProvider` in code. It also
saw TASK-13908's first converted file appear mid-run (`ProfileBackdatingEndpointTests.cs`, `F = 2025-03-12`,
`WithFixedToday(F)`) — out of its scope by design; **a second validator pass runs over TASK-13908's files at its
acceptance.** Line numbers in this paragraph are as at wave 2 (the W1 fix and the Step-7a hoist shifted them; the Step-7a Reviewer confirmed `RequireAuthorization` unchanged at the final positions `:806/:1010` and `:829/1477/2413/2819`).

### TASK-13907 — Step 5a, external lens (2026-09-07): Codex **CLEAN** (0 B / 0 W / 0 N)

`codex review` prompt-alone on the uncommitted diff, focus narrowed to the six `src/` files + the fixture, factory
and probe (artifact `.claude/reviews/SPRINT-139-task13907-codex-5a.md`; first attempt failed on an unsupported `-s`
flag — `codex review` takes no sandbox option — and was re-run). Codex's closing statement: the clock substitutions
preserve UTC-day semantics, `DateOnly` parameters map to PostgreSQL `date`, DI resolves the optional `TimeProvider`
parameters, and the probe exercises both soft-delete clock reads by `profile_id`. Internal lens below.

### TASK-13907 — Step 5a, internal lens (2026-09-07): Reviewer **APPROVED-WITH-WARNINGS** (0 B / 2 W / 7 N, `reviewed-by-model: claude-fable-5-1`)

The Reviewer traced all four converted request paths end to end and inventoried every clock read on them
(converted vs raw); behaviour preservation, binding, DI, auditability and probe-can-fail all hold. Two WARNINGs,
both accepted:

- **W1 — the soft-delete reads the provider TWICE.** `SoftDeleteAsync` binds `@today` from the repository's
  provider (`EmployeeProfileRepository.cs:854`) and the DELETE handler computes the event's `EffectiveTo` from its own
  read (`EmployeeProfileEndpoints.cs:964`). One clock, two instants: at 23:59:59.9 UTC the row could say the 8th and
  the replayable event the 7th — and the new comment claims "they cannot disagree now". Narrower than before (two
  CLOCKS), so not a new risk, but a comment claiming an invariant the code does not hold is drift. ADR-023 D8 already
  names the structural answer (`SoftDeleteAsync(..., closeDate, ct)`). **Ruling: fix now** — compute `today` once in
  the handler, pass it as an optional trailing `DateOnly? closeDate = null` (direct constructions keep compiling), use
  it for both the SQL bind and the event; re-word both comments. Same "compute once" rule S137 wrote at
  `AdminEndpoints.cs:908-915`. Routed back to the `backend-infrastructure` agent with its context intact.
- **W2 — probe Legs 1–2 pin the ENDPOINT guards only.** For `F+1` the endpoint's guard 422s first; for `F` the
  repository's `today` feeds only `IsFutureDated` and the "row covering today" cache refresh, which answer identically
  for `F` and the real today because the freshly split open row covers both. RED against the pre-change product, but
  GREEN even with the repositories unconverted — the leg comments overclaim. **Ruling: fix now** — one
  direct-construction test per repository (`new …Repository(factory, new FixedTimeProvider(F))` →
  `SupersedeAndCreateAsync` at `F+1` → `TemporalWriteRejectedException(FutureDated)`), and the leg comments corrected.
  Routed to the `test-qa` agent after TASK-13908 lands (natural home: the lifecycle suites it is converting).
- **NOTEs absorbed:** behaviour preservation is conditional on a UTC database session (verified: no TZ override
  anywhere) — the assumption goes into the legacy-DB upgrade runbook at close · the remaining DB-clock dates on
  ADJACENT admin paths (`ReportingLineRepository.cs:1287` closes the old approver line on the DB clock while `:1598`
  in the same users PUT uses the app clock; `:1043` fallback; the census list) → the QUAL "unparameterised SQL clock
  sites" row · `AdminEndpoints.cs:935/1100/1336` read the provider three times on the create POST despite the S137
  "computed ONCE" comment → **folded into the W1 fix** (reuse `effectiveFrom`; zero cost, honours the stated design) ·
  `EmployeeProfileRepository.cs:388` (`CreateAsync`) and `:705` (`UpsertAsync`) have no `src/` caller (test-only) —
  recorded · `ApprovalPeriodRepository.cs:698/:748` are not probed; the Group-1 roster suites construct the repository
  directly and must pass `timeProvider:` (already in TASK-13908's spec) · Leg 4 is a regression guard, not a
  falsifier (as its doc says) · the Payroll host registers the three repositories with no `TimeProvider` → the default
  applies (fine).

**Cycle 2 (verification of the W1/W2 fixes) runs on both lenses after they land.**

#### W1 fix landed (backend-infrastructure, resumed with context; 25 tool uses, ~187k tokens) — ACCEPTED

`SoftDeleteAsync(conn, tx, employeeId, expectedVersion, DateOnly? closeDate = null, CancellationToken ct = default)`
(`EmployeeProfileRepository.cs:827-831`; `var today = closeDate ?? …` at `:844`, bound at `:876`); the DELETE handler
computes `today` ONCE before the connection opens (`EmployeeProfileEndpoints.cs:876`), passes it (`:905`) and uses the
same variable for the event (`:975`) — row and event now carry the same date **by construction**, and both comments
say so with the why (the midnight straddle). **The folded NOTE turned out to be a pre-existing defect:** the admin
create POST's three dated cells were three independent `DateTime.UtcNow` reads sitting under S137's comment
"Computed ONCE here so the three can never disagree" — the rule was written but enforced for only the first three
values. A straddle would have dated the profile row the 7th and the agreement-code row the 8th, leaving the
employee's first day with no covering agreement code, or the new manager edge a day after the hire (no approver on
day one). Now `agreementToday = effectiveFrom` (`AdminEndpoints.cs:1111`) and the reporting line's
`EffectiveFrom = effectiveFrom` (`:1350`); `:1238` already reused it. Build 0 errors / 145 warnings (warning SETS
byte-identical to baseline, line numbers stripped); unit 1235/1235. The agent's closing remark that `:746` is still
raw is stale — the Orchestrator converted that read (now `:748`) before the agent was resumed; verified after its
second pass: the file's only raw reads are `:238/:287/:488`, off the converted paths.

### Wave-3 Constraint Validator (Step 5α, 2026-09-07) — **pass, no violations**

`constraint-validator` (Sonnet) · 26 tool uses · ~142k tokens. The eight mechanical checks + the ten sprint-specific
points all pass: test-qa's diff entirely under `tests/` (exactly the nine converted suites + the probe's comment edits,
plus the already-validated wave-2 set); the `src/` diff since wave 2 = W1 + the two disclosed one-liners; no agent
`docs/` edit; `RequireAuthorization` lines absent from every diff hunk (only trailing `TimeProvider` parameters
added to six handler signatures); **CA2100 recounted: 115 = baseline** on a fresh `--no-incremental` build (0 errors
/ 145 warnings); `TemporalWriteRouter.cs` and `EventSerializer.cs` diffs empty; census regex 0 over the ten files;
`OnWeekday` / `NextWeekday` zero call sites; one `FixedTimeProvider`; **every converted suite still carries
`[Trait("Category","Docker")]`** at an untouched line — no test silently moved out of the Docker-gated set. Observed
for the record: `ApprovalPeriodRepository.cs:238/:488` remain raw — disclosed and deliberate (QUAL-155).

### Step 5a cycle 2, external lens (2026-09-07): Codex **CLEAN — terminal**

`codex review` prompt-alone on the whole uncommitted code diff (artifact `.claude/reviews/SPRINT-139-step5a-c2-codex.md`):
**W1 RESOLVED** (one UTC date flows to each row and event; no broken caller), **W2 RESOLVED** (both direct repository
tests use `F`, seed hire dates before `F`, reject `F+1` for the expected reason, and would fail against the real clock),
the two Orchestrator one-liners behaviour-preserving, and the nine-suite census plus sampled RED assertions revealed no
defect. Internal lens cycle 2 below.

### Step 5a cycle 2, internal lens (2026-09-07): Reviewer **APPROVED-WITH-WARNINGS** (0 B / 2 W / 4 N, `reviewed-by-model: claude-fable-5-1`)

**W1 RESOLVED** (one date to row and event; the only production caller updated; the one test caller compiles through
the defaults; the create POST's single read at `AdminEndpoints.cs:940` reused at `:1111` and `:1350`) · **W2 RESOLVED**
(both repository-guard tests can fail: on the real clock `IsFutureDated(2025-03-13, today)` is false and `ThrowsAsync`
fails; hire dates `F − 100`; `Created` correct on the `F` leg because both fixtures leave the timeline empty) ·
**one-liners RESOLVED** (`:748` threads `today` into the authority context and both prefetches; `:287` reaches
`FilterByEffectiveApproverAsync`). **Tests can fail — no pin passes for the wrong reason across the nine files**: every
seeded absence lands on a weekday clear of Danish holidays, every 2025 date is OK24, entitlement configs are seeded at
`'0001-01-01'` so early-2025 dates resolve, every direct construction receives `FixedTimeProvider(F)`; the offset moves
keep each pin's meaning; the `MakeLine` rebase is correct and sufficient; the perf anchor is sound.

Two new WARNINGs, both test-side, both ACCEPTED and routed to the `test-qa` agent (resumed): **W1' — about ten `// RED:`
comments overstate clock-sensitivity**: for requests dated `F` or earlier an unconverted product accepts them too, so
those pins are valid, deterministic pins of their real subject but NOT seam coverage (the genuine clock pins are the four
`F+1` tests, the two repository-guard tests, the greatest-`period_end` projection test, and the probe) — reword to the
real RED condition. **W2' — the "strengthened" past-cap pin still reads `if (booked >= 13)`** with only `booked >= 1`
asserted, so a regression booking fewer days would skip the check silently → `Assert.Equal(24, booked)` and delete the
`if`; the vacuous `NotEqual(500)` after `Equal(OK)` goes too. **NOTEs routed with it:** a false "own ferieår" comment
(both dates are ferieår 2024); a mis-attribution of the endpoint conversion to S138; three literals and four
default-`asOf` calls in the person-search suite not derived from `F` (rebase; keep the omission only where the fallback
is deliberately the subject). **NOTE 4** (`ApprovalPeriodRepository.cs:238/:488` one class, two clocks) is QUAL-155.
**Verification of these test-side fixes folds into Step 7a's scope** (both lenses are told to verify them first) rather
than a separate cycle 3 — every fix is still reviewed before the close commit.

#### Cycle-2 fixes landed (test-qa, resumed; 58 tool uses) — ACCEPTED

W1': ten `// RED:` comments reworded from a false clock claim to the pin's real subject (e.g. `PeriodStatus…:309` now
"fails if the raw-status → FE-3-state mapping is wrong — F−1 is also before the real today"; `S106SeedScalePerfTests
:254` "fails if the tile-count mechanism itself is broken — clock-insensitive"); the genuine clock pins left as they were
(the four `F+1` tests, the two repository-guard tests, the greatest-`period_end` projection test, the probe, and the
perf differential tests). W2': `Assert.Equal(24, booked)` (the loop deterministically books 24 weekdays, 2025-03-13 →
2025-04-15, stopping on the count cap before the ferieår tail), the `if` deleted so the past-cap assertion always runs,
the vacuous `NotEqual(500)` removed. N1: the ferieår comment corrected (both dates in ferieår 2024; the `used == 2.0` pin
is MORE discriminating than claimed). N2: attribution → S139 / TASK-13907; the "future-only save path" narrative softened
at all three sites (the Skema save path has no wall-clock date guard). N3: `'2099-12-31'` → `FarFutureVikarCoverage =
F.AddYears(75)` and `'2026-01-01'` → `F.AddYears(-1)`, both bound as parameters; the two `FAIL_004_*` calls keep the
omitted `asOf` with a comment naming the fallback as the subject. Build 0 errors; census 0 over the six files.

### TASK-13907 — the product clock seam — **complete; Step 5a terminal (Codex CLEAN ×2; Reviewer 0 B, all W absorbed)**

| Field | Value |
|-------|-------|
| **Agent** | `backend-infrastructure` (Opus) · 98 tool uses · ~160k tokens |
| **Build / tests** | `dotnet build StatsTid.sln -c Release --no-incremental` → 0 errors; distinct warning sites **145 → 145**; **CA2100 distinct sites 115 → 115** (parameterising removed SQL literals, added no concatenation); `dotnet test tests/StatsTid.Tests.Unit` → **1235 / 1235** |
| **Sites converted** | *(line numbers as at TASK-13907 acceptance — the W1 fix and the Step-7a hoist shifted them; final positions in § External Review)* `EmployeeProfileRepository.cs` `:388` (`CreateAsync` effectiveFrom), `:518` (`SupersedeAndCreateAsync` today — still PASSED INTO the pure router at `:521`, PAT-025), `:705` (`UpsertAsync` shim), **`:848/:854` the soft-delete SQL `SET effective_to = @today`** (UTC `DateOnly` bound; `updated_at = NOW()` stays) · `UserAgreementCodeRepository.cs:260` · **`ApprovalPeriodRepository.cs:673` `ap.period_end < @today`** (bound `:698`) · `EmployeeProfileEndpoints.cs:284` (the 422 validator), `:964` (soft-delete event `EffectiveTo`) · `AdminEndpoints.cs:935` ("hired today" default), `:1100`, `:1336`, `:1598`, `:2487` (both future-dating guards). The nine audit/token stamps in `AdminEndpoints.cs` (`:206, 338, 438, 555, 883, 1341, 1617, 2935, 3098`) and `EmployeeProfileEndpoints.cs:591` `OccurredAt` untouched by design |
| **Constructors** | `EmployeeProfileRepository(DbConnectionFactory, TimeProvider? = null)` · `UserAgreementCodeRepository(DbConnectionFactory, TimeProvider? = null)` · `ApprovalPeriodRepository(DbConnectionFactory, DesignatedApproverAuthorizer? = null, ReportingLineRepository? = null, TimeProvider? = null)`; each stores `?? TimeProvider.System`; handlers take `TimeProvider timeProvider` (precedent `SkemaEndpoints.cs:2020`, `BalanceEndpoints.cs:666`) |
| **DI — verified, not assumed** | plain type registrations (`Program.cs:112, 120, 121`; `TimeProvider.System` at `:395`), no factory lambdas; a throwaway DI probe (scratchpad) confirmed: a registered `TimeProvider` wins over the optional default regardless of order; a test host's appended singleton reaches the repository; with none registered the default applies — which is what keeps the 33 direct test constructions compiling and behaving |
| **Doc comments** | `EmployeeProfileRepository.cs:660, 692, 722-724, 758, 764-769, 832-843`; `EmployeeProfileEndpoints.cs:83-90, 119-123`; `Program.cs:387-394` (the stale "year-overview ONLY" claim rewritten) |
| **Declared deviations — ACCEPTED** | two extra doc-comment fixes inside scope files, zero behaviour: `AdminEndpoints.cs:3619-3624` (described the retired pre-S138 "== today" rule and quoted `DateTime.UtcNow`), `ApprovalPeriodRepository.cs:573` (quoted the retired `CURRENT_DATE`) — the task's rule was "docs match code" |
| **Listed, not fixed → RULED** | **HIGH:** `GetPeriodStatusProjectionForTreeAsync` — the very method whose SQL was converted — still read `DateTime.UtcNow` for its phase-2 approver resolution (`ApprovalPeriodRepository.cs:748`), so under a fixed clock the projection's two halves would disagree. **Fixed by the Orchestrator under the Small Tasks Exception** (one line, one file, `_timeProvider` already a field; self-checked against the Constraint Validator list: no scope change, no behaviour change, UTC-day form). The other three raw reads in the file (`:238` `GetByMonthForDesignatedReportsAsync`, `:287` `GetPendingForDesignatedReportsAsync`, `:488` `GetTeamOverviewRosterAsync`) are NOT on a Group-1 suite's path → the QUAL "remaining `src/` reads" row; TASK-13908 reports if a converted suite reaches one |
| **Environment note** | mid-task the tree briefly showed the 16 `CS0104` collisions from TASK-13906's first attempt (resolved by that agent's rename); the final build is clean |

## Sprint Retrospective

**What shipped, in one paragraph a non-engineer can use.** Two things. First, a register that puts every job the
system leaves for HR to finish into one table — what creates it, who owns it, by when and on whose authority, whether
anyone can see it, what happens when it ages. Fifteen such hand-offs exist; almost none are visible on any screen,
none escalate, and only one has a deadline anyone has actually stated. The biggest gap sat outside every sweep's map:
every approved month reaches payroll only when an administrator calls the export by hand, and no list shows the months
that have not gone. The register recommends one HR landing page with a tile per process and hands Increment 4 a
checklist. Second, the test suites that failed in S138 because their dates floated with the calendar now use fixed,
named dates — and the product reads the same clock the tests fix, so a pin's meaning no longer depends on the day CI
runs. Along the way the reviews found and fixed two real defects nobody had registered: the admin create path read
"today" three times under a comment claiming once, and the profile soft-delete stamped its row and its event from two
separate clock reads.

### What went well

- **Spawning by role name worked on its first live run.** Seventeen agent spawns, every one routed to the tier the
  definition names (Sonnet for sweeps, census, tests and validators; Opus for the product seam; Haiku for the using
  sweep; Fable for every review), **zero guard blocks, zero reviewer refusals**. The owner declined the Opus switch for
  the Orchestrator seat, so the seat cost is the one number the routing could not lower this sprint.
- **A reproducible census beats an estimate.** Pinning the regexes and re-running them found a hazard the refinement's
  own method would have missed (`EntitlementQuotaCheckUsesYearStartTests` hard-fails every 1 September and does no
  date arithmetic), and proved the offset proxy incomplete — which is now a registered fact (QUAL-154), not a hunch.
- **The lenses diverged the way they are supposed to.** Codex held the register to its own readiness rule and blocked
  twice until the rule was applied honestly; the Reviewer opened every citation and found three hand-offs all three
  sweeps had missed, including the payroll-export gap. Neither lens would have produced the other's findings.
- **The probe found its own blind spot.** Designing the fixed-clock probe to be falsifiable made it reviewable — the
  Reviewer showed Legs 1–2 could not see the repository guards, and two direct-construction tests now pin what the
  HTTP legs cannot.
- **Resuming an agent beats respawning it.** Both follow-ups (W1 to the seam agent, W2 and the cycle-2 fixes to the
  conversion agent) went back to the original agent with its context intact — smaller prompts, no re-reading, and a
  rate-limit interruption was recovered by verifying the tree before resuming rather than by guessing.

### What to do differently

- **Check a spec's premise before an agent has to.** TASK-13906's brief said "nothing imports both namespaces" — false,
  and the agent had to deviate (renaming the class) to keep the build green. One grep would have caught it. The
  agent's declared deviation was the right behaviour; the premise was the Orchestrator's error.
- **Do not cite line numbers from a file another agent is editing.** The register's `AdminEndpoints.cs` cites drifted
  twice while the seam agent worked in the same file; both lenses flagged the drift. Rule for next time: run the
  analysis sweeps and the product edits on disjoint files, or re-cite after the last edit lands (as rev 3 did).
- **Apply a rule as written, or change the rule.** The Orchestrator counted a developer default as an "institutional"
  deadline and "immediate by consequence" as a deadline source; Codex was right to block. Six rows became one.
- **A RED comment must name the defect, not the clock.** Ten converted pins claimed clock-sensitivity they did not
  have — valid pins, wrong claims. The check "would this pin fail under the stated condition?" belongs in the
  implementer's own pass, not only the Reviewer's.
- **Two one-liners under the Small Tasks Exception in the same file are still two.** Both were disclosed and both
  copied an already-reviewed form; the third would have been a task.
- **`codex review` takes no sandbox flag.** One wasted invocation; the prompt-alone form is the whole interface.

### Carried forward

- **Owner rulings pending at the register:** the shape (recommendation 3), the HRP-010 aging proposal (projection
  caveat), and — optionally — the institutional payroll cutoff, which would make HRP-011/012/022 READY at once.
- **QUAL-154 — the second tranche** of the fixed-clock conversion: six suites whose paths need seams the OQ-1 (a) ruling
  did not name, plus the secondary `.Month/.Year/.DayOfWeek` scan over ~65 files the offset proxy classified blind.
- **QUAL-155 / 156 / 157** — the remaining raw business-date reads, the SQL clock sites (UTC-session assumption in the
  runbook), and the UTC-vs-Copenhagen "today" split awaiting a domain ruling.
- **From the HR sweep — QUAL-158 … 163**: the dead compensatory-rest feature, two missing outbox events, the silent
  role-assignment expiry, the unrouted overtime page, and the "past deadline" tile that never reads the deadline.
- **Increment 4 (S140)** takes the register's checklist as its design input; the worklist's recalculate action has a
  role mismatch (`HROrAbove` list, `GlobalAdminOnly` endpoint) to decide first.
- **Docker-gated verification** — the probe, the two repository-guard tests, eight anchor facts and 65 converted pins
  run only in the watched CI run at close; the gap between "locally green" and "actually green" is measured there.

**Knowledge produced:** **PAT-028** (compute "today" once per operation and pass it down — the defect shape the
reviews found twice); **PAT-008** rewritten around the shared fixture, the `WithFixedToday` opt-in, the falsifiability
probe and the rule that the fixed clock must reach the product; the **HRP register** as a new durable operations
document; ten **QUAL** rows; the runbook's UTC-session section.

**Step 7a and the model-routing register row follow below once both lenses report.**

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — both lenses on the whole uncommitted diff (code + docs) |
| **Sprint-start commit** | `6e8d2a7` (= HEAD; no intermediate commits) |
| **Command** | `codex review -` (prompt-alone via stdin; auto-targets the uncommitted diff) |
| **Review Cycles** | Codex: **3** (1 → fix → 2 → one-token fix → 3 CLEAN) · Reviewer: 2 (1 → fixes → 2, below) |
| **Findings** | Codex cycle 1: **0 BLOCKER, 1 WARNING (P2), 0 NOTE** (the projection's two reads) · cycle 2: P2 RESOLVED, one malformed register cite (`:-2`, an Orchestrator script slip) · cycle 3: **CLEAN** · Reviewer cycle 1: **0 BLOCKER, 2 WARNING, 4 NOTE** (all doc-vs-code) |
| **Resolution** | **all resolved** — every fix verified by the lens that raised it; artifacts `.claude/reviews/SPRINT-139-step7a-codex.md` (`verdict: CLEAN`, cycles 1–3 recorded) and `SPRINT-139-step7a-reviewer.md` (the cycle-2 verification output, verbatim) |

### Codex cycle 1 (artifact `.claude/reviews/SPRINT-139-step7a-codex-c1.md`)

Codex verified the Step-5a cycle-2 test fixes first ("the reviewed Step-5a assertions are substantively sound,
including the 24-weekday pin") and found the seam "generally wired correctly". **One WARNING (P2), accepted — and it is
PAT-028's shape a third time, in the Orchestrator's own one-liner:** `ApprovalPeriodRepository.GetPeriodStatusProjectionForTreeAsync`
binds `@today` for the status query at `:698` from one provider read and reads the provider AGAIN at `:748` for the
phase-2 approver resolution. Both reads are now on the seam (the converted source), but they are two reads: across UTC
midnight the employee status badges and the manager tile counts in ONE response would describe different effective
dates. The `:748` conversion removed the two-CLOCK problem and left the two-READ problem — exactly what PAT-028 says to
check ("count the reads in the operation"). **Fix routed to the `backend-infrastructure` agent (resumed):** hoist one
`today` above the SQL, bind from it, delete the second read; confirm `GetPendingForDesignatedReportsAsync` reads once.
Cycle 2 on both lenses verifies the fix.

**Fix landed (backend-infrastructure, resumed; 13 tool uses):** `var today = …` hoisted to the top of
`GetPeriodStatusProjectionForTreeAsync` (`:623`, before the connection opens); the SQL bind at `:708` and every
date-consuming call in phase 2 — `ApprovalAuthorityContext(today)` `:783`, the three prefetches `:800/:809/:815`, the
per-employee tally `:826`, `asOf: today` `:839` — use the one local; the phase-2 read deleted; two comments cite PAT-028.
`GetPendingForDesignatedReportsAsync` confirmed a SINGLE read (`:287` → SQL `:299` and `FilterByEffectiveApproverAsync`
`:308`) — the agent noted it had not made that conversion itself: correct, it was the Orchestrator's disclosed one-liner.
`:238` / `:488` left raw (QUAL-155; each a single read within its method). Build 0 errors / 145 warnings, CA2100 115,
warning set byte-identical to baseline; unit 1235/1235. **The agent's own first-pass HIGH ("one method, two clocks") is
now closed** — Codex reached the same finding independently.

### Reviewer cycle 1 (`reviewed-by-model: claude-fable-5-1`, `reviewed-against-commit: 6e8d2a7`): **APPROVED-WITH-WARNINGS** (0 B / 2 W / 4 N)

Scope: the whole uncommitted tree — six `src/` files, 31 test files, every doc in the diff. The tree moved once during
the review (the Codex-7a hoist); the Reviewer re-verified against the moved tree and **confirmed the hoist** (`:623` one
`today`, bound `:708`, threaded `:783/:800/:809/:815/:826/:839`; provider reads only at `:287` and `:623`).
**Step-5a cycle-2 fixes verified** — all four items hold (each reworded RED claim true; 24 weekdays 2025-03-13 → 04-15,
2 + 5 + 5 + 5 + 5 + 2, no Danish holiday in range, Easter starts 04-17; ferieår comment and attribution correct; N3
parameters bound; the two omitted-`asOf` calls acceptable — "tolerated there, not the subject"). **Code:** no behaviour
change beyond the three straddle removals; auditability intact; `RequireAuthorization` unchanged at
`EmployeeProfileEndpoints.cs:195/806/1010`, `AdminEndpoints.cs:829/1477/2413/2819`; the nine stamps remain; every suite
Docker-gated; census 0; both repository-guard tests can fail. **Doc-vs-code — two WARNINGs, both the SAME drift class the
retrospective names, fixed before the close commit:** (W1) the register's and QUAL-161/163's `AdminEndpoints.cs` /
`ApprovalPeriodRepository.cs` cites were pre-W1 (the W1 fix added 13 lines) while the header claimed currency →
**re-cited from the tree by script** (worklist writers `:2188-2190` / `:2783-2785`, grant `:2866`, INSERT `:2960-2970`,
revoke `:3049`, hire-date flag `:941` / `:991-1002`, deadline write `:1804`) and the header now says "re-cited against
the S139 close tree"; (W2) this log's TASK-13907 "Sites converted" table and the wave-2 validator paragraph carried
pre-W1 / pre-hoist numbers → **annotated "as at acceptance; final positions in § External Review"** rather than
rewritten (three tree states are now labelled, not silently mixed). **NOTEs fixed:** five "S138 / TASK-13906"
attributions in the probe and the factory → S139, `Program.cs:390` → `:395`; the agreement-code suite has **15** RED
comments, not 14 → **65** converted pins (three places corrected); "thirteen suites" → fourteen suite files across 13
census entries (log + QUAL-154); PAT-008's direct-construction sample used a non-existent `factory.ConnectionFactory`
→ `dbFactory` with a comment; the duplicate empty Test Summary template and the empty Task Log placeholder removed.
**NOTE left as is:** three `2099-12-31` vikar literals in the roster suites (harmless under both clocks; the person-search
suite's rebase was the one that mattered). **Verified true by the Reviewer:** ROADMAP's 15 / 2+3+1+9 / 1 / 14 match the
register; QUALITY.md's S139 block claims nothing beyond the log; QUAL-155's 108 / 47 reproduces; the runbook's UTC
section matches code and harness; PAT-028 exists, is INDEX-linked, its signature matches `EmployeeProfileRepository.cs:827-831`.

**Cycle 2 (verification of the 7a fixes — the hoist for Codex; the doc re-cites for both) runs on both lenses next.**

### Cycle 2 → terminal (2026-09-07)

- **Codex cycle 2:** P2 hoist **RESOLVED**; doc re-cites PARTIAL — one malformed HRP-001 cite (`:-2`): the re-cite
  script mis-indexed a shell positional (an Orchestrator slip). Fixed to `:2783-2785`. **Codex cycle 3: CLEAN** — both
  ranges verified to be the `worklistRepo.WriteFor…` calls. Artifact `.claude/reviews/SPRINT-139-step7a-codex.md`.
- **Reviewer cycle 2** (`reviewed-by-model: claude-fable-5-1`): W2 RESOLVED (annotations present; `RequireAuthorization`
  verified at the final positions); Codex P2 RESOLVED (provider reads only at `:287` / `:623`; one `today` in the
  projection); W1 PARTIAL on the same `:-2` cite (its read predated the fix — now fixed and Codex-verified); NOTEs PARTIAL
  on three unspaced `S138/TASK-1390x` strings left in the probe (a comment and two seed labels) → fixed by `sed`,
  grep-verified zero, no identifier or assertion touched. Verdict **APPROVED-WITH-WARNINGS**, `reviewed-against-commit:
  6e8d2a7`. Artifact `.claude/reviews/SPRINT-139-step7a-reviewer.md` (the cycle-2 output verbatim + the absorption note).
- **No finding remains open on either lens.** Every fix made after a review was itself reviewed: the hoist by both
  lenses, the doc re-cites by both, the one-token cite fix by Codex cycle 3, the three probe strings by grep (comment
  and label text only).
