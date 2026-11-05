# S147 TIDY PASS — the last build step before C-1 (draft 1)

| | |
|---|---|
| **Status** | DRAFT 1, 2026-11-04, for the Orchestrator's ruling. Nothing is dispatched until the rulings in § "Needs a ruling" are given |
| **Source** | `docs/sprints/SPRINT-147.md:316-410`, "Tidy list", items 1–25 (item 25 extended by TASK-14716) |
| **Master at draft** | `94bd32be` (local master). Every `file:line` below was re-opened at this commit; where the list's line has moved, the true line is given |
| **Base commit (Step 7a)** | `b510e45b` (unchanged, `PLAN-s147.md:10`). The tidy commits fall inside K's diff and are reviewed again at C-3 |
| **Orchestrator model** | Opus 5.5 seat throughout (owner ruling 2026-09-29, `docs/WORKFLOW.md:473-484`). Drafting: `planner` (Opus 5.5). Judgment: `reviewer`/`adjudicator` (Fable 5.1). Implementation by role name, never with a `model` override: `backend-infrastructure`, `payroll-integration` (Opus); `test-qa`, `ux`, `constraint-validator` (Sonnet). `sweep` (Haiku) is **not** used: the edits are many distinct exact replacements, not one pattern, which its own brief-challenge rule calls a contradiction (`.claude/agents/sweep.md:8`). External lens: Codex |
| **Baseline (GATE at `94bd32be`)** | build 0 errors / 144 warnings · CA2100 114 · Unit 1710 · DemoSeed 170 · Regression non-Docker 135 (commit message of `94bd32be`) · vitest 83 files / 1074 (CI-W4 run `41708962373`; `git diff --stat eaf90ed4 94bd32be -- frontend` is empty) |
| **Task ids** | TIDY-1 = TASK-14721 (+ -T), TIDY-2 = TASK-14722, TIDY-3 = TASK-14723 (+ -T), TIDY-4 = TASK-14724 (free: no `TASK-1472[1-9]` in the sprint log, the plan or `git log`). The ledger needs a disposition for each |

---

## In plain words (read this first)

**What the tidy pass is.** During the sprint, every review left small notes: a comment that no longer matches the code, a test whose description is out of date, a helper nobody calls any more. They were parked on one list so they would not interrupt the real work. This pass clears the list before the sprint is packaged for its final review (C-1).

**What it changes for a user.** Almost nothing, by design. Two small visible changes:
1. When Skema refuses a part-day absence, the server's sentence now writes hours the Danish way: "(7,4 timer)" instead of "(7.4 timer)".
2. On HR's follow-up page, the tile counts refresh after HR approves, rejects or sends a leaver's final month, instead of staying stale until reload.

One more visible change waits on an owner decision (item 7: which Danish word each screen uses for the five entitlement types).

**What it changes for the code.** About forty comments and test descriptions are made true again. Several describe the raw payroll routes as if they still existed, or describe a leaver as switched off on day 1 when the 5-day manager grace now keeps them active until day 6. Two dead helpers and one dead validator are deleted. Two absence-code strings become the shared constants. Three small behaviour fixes are made, each with a test:
- the Danish decimal comma in the refusal sentence;
- a fail-safe default for the absence classification. Today an entry someone forgets to fill in would count as "credit", which overstates flex; after the fix it counts as "neutral";
- a clearer instruction when a settlement reversal fails inside the manager grace.

**What it does not do.** Ten items are bigger than tidying: a design choice, a new screen behaviour, or a cross-host refactor. They go to S148 stage 0, the ROADMAP backlog or a register row, each with a reason (Class D).

**Counts.**

| Class | Items |
|---|---|
| A, mechanical | 42 list rows, three of them optional, plus 5 extra finds the list missed |
| B, small behavioural fixes | 6, two of them test-only and one optional, plus 1 conditional on a ruling |
| C, decision first | 2: one the owner's, one the Orchestrator's |
| D, beyond tidy | 10, plus 1 extra |
| Orchestrator's own edits | 7: `docs/**`, `.claude/**` and registers |
| Already done or no action | 7 |

---

## Class A — mechanical (comment, docstring, test prose, rename, dead code with a census, same-value literal → constant)

Old text is quoted from the file at `94bd32be`; New text is the target meaning. The agent keeps the file's line wrapping and line endings: the C# and SQL files here are committed CRLF, for example `git show HEAD:<file> | grep -c $'\r'` equals the line count for `PeriodCalculationService.cs`, `Program.cs`, `SkemaEndpoints.cs`, `init.sql` and `VacationSettlementService.cs`. In a `SharedKernel` file, a type from `Infrastructure` is named in `<c>…</c>`, never `<see cref>`: SharedKernel cannot reference Infrastructure, and warnings are errors in production projects (`Directory.Build.props:22`).

| # | Site (true line at `94bd32be`) | Edit | Domain | Task |
|---|---|---|---|---|
| 1a | `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:131` (list said `:128-129`) | `employment windows over <c>[the previous month's first day, this month's last day]</c>)` → "employment windows over the range `<see cref="FlexLedgerOpening.WindowsReadRange(DateOnly, DateOnly?)"/>` gives: `[min(S, P).start, M.end]` with the ledger start S, so the S147 chain walk (TASK-14700, QUAL-229) can test every month from S to P; `[P.start, M.end]` while the ledger is empty)". The cref form is already used at `:880` | payroll-integration | TIDY-2 |
| 1d | `…/Payroll/Program.cs:683-689` (the `/recalculate` catch; list said `:721-727`) | After `or "correct it first" for a raw one)` insert: "; or, since S147 TASK-14700, `payroll-prior-month-stale`: an earlier recorded month is out of date, naming the earliest to recalculate first". The rest of the sentence is unchanged | payroll-integration | TIDY-2 |
| 1e | `tests/StatsTid.Tests.Regression/Payroll/FlexLedgerHostTests.cs:107-109` | After `<c>[P.start, M.end]</c> (<see cref="FlexLedgerOpening.WindowsReadRange"/>)` add: "the one-argument form this fact calls; production passes the ledger start S too and reads `[min(S, P).start, M.end]` (S147 R-13)". The fact calls `WindowsReadRange(Jun)` at `:122`, so the bracket stays true for this fact | test-qa | TIDY-1-T |
| 1f | `…/Payroll/Services/FlexLedgerOpening.cs:77-80` (list said `:66-68`; Fable 14700 NOTE, `.claude/reviews/S147-TASK-14700-5a-reviewer.md:19`) | Append after `exactly as before.`: "One shape is not surfaced: a hole followed by a month with no EMPLOYED day is never named, because the next ledger month then opens at REHIRE 0 and reads only its own P. That shape needs two employment spells, which single-spell storage cannot hold (ADR-040 D1), so it is unreachable today; multi-spell storage must revisit it." | payroll-integration | TIDY-2 |
| 2 | `tests/StatsTid.Tests.Unit/Payroll/FlexLedgerChainWalkTests.cs` | Rename M-7 … M-18 → M-229-7 … M-229-18. That is 18 occurrences on 17 lines: `:27, :101, :117, :133, :151, :168, :190, :208, :226, :247, :266, :323, :352, :411, :592, :617, :637`. Why: `PLAN-s147.md:404-410` owns M-7 … M-13 for other facts. Header `:23-28`: "Kind N for every fact" → "24 facts are kind N …; two are kind B boundary pins (`:519`, `:539`); one is kind F (case 3a, re-pinned by TASK-14715-T, `:132`)". `this file adds M-7 .. M-12` → "this file adds M-229-7 .. M-229-18". **The list's "25 N + 2 B" is out of date**: see Brief contradiction BC-1 | test-qa | TIDY-1-T |
| 4a | `tests/StatsTid.Tests.Regression/Skema/SkemaSpecialHolidayRowTests.cs:33` | `entitlementConstants.ts:14` → `:15` (the label is at `frontend/src/lib/entitlementConstants.ts:15`) | test-qa | TIDY-1-T |
| 4b | same file `:54` | `// "Særlig feriedage" label (escaped so source encoding can never change the asserted value).` → `// The "Særlig feriedag" label, written with a literal æ (the file is UTF-8, .editorconfig:7).` | test-qa | TIDY-1-T |
| 4c | same file `:245-247` | The class names are correct: `SkemaMonthlyAccrualGuardTests` exists at `tests/StatsTid.Tests.Regression/Outbox/SkemaMonthlyAccrualGuardTests.cs:56` and saves the code (BC-2). What is stale is `there is no absence-code check at the save`. It becomes: "the save checks only that the code is in the vocabulary (`AbsenceClassification.IsKnown`, `SkemaEndpoints.cs:967`, since S147 TASK-14708), which `SPECIAL_HOLIDAY_ALLOWANCE` is". Optionally add the folder: "`SkemaMonthlyAccrualGuardTests` (under `Outbox/`)" | test-qa | TIDY-1-T |
| 6a | `frontend/src/pages/__tests__/SkemaPage.specialHoliday.test.tsx:199` | `.toBeEnabled()` → `.toBeInTheDocument()`. Presence is the discriminator (14702 c2 NOTE 2) | ux | TIDY-4 |
| 6b | same file `:204` | `History (TASK-14702 fix-up, kind F, fails on that fix-up's pre-fix code)` → "History (TASK-14702 fix-up 1 added it and fix-up 2 re-pointed it at the full-day rule alert; kind F, failing on the code before fix-up 1's label key)" | ux | TIDY-4 |
| 8b | `frontend/src/pages/__tests__/SkemaPage.test.tsx:1879-1885` (list said `:958`; the fact is `R4: a 422 REVERTS…`, `:1848`) | Append to the comment. **If TIDY-3 merged:** "The mock's decimal comma is what the server sends since S147 tidy item 8 (da-DK formatting)". **If TIDY-3 was cut:** "the decimal comma is the da-DK target of tidy item 8; the container renders 7.4 until then" (14709 c2 NOTE 1) | ux | TIDY-4 |
| 10a | `docker/postgres/init.sql:1250` (the wage_type_mappings section, payroll-integration's scope, `docs/AGENTS.md:20`; **Orchestrator approval needed**, CLAUDE.md "init.sql") | `-- per AbsenceRule.cs:112-114, not CHILD_SICK_1,` → `-- (AbsenceRule.cs:76, where since S147 TASK-14708 the line's time type is the stored code), not CHILD_SICK_1,`. Comment only; `danish-agreements.md:278` already carries its S147 note (O-4) | payroll-integration | TIDY-2 |
| 10b | `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs:197`; `src/Infrastructure/StatsTid.Infrastructure/VacationSettlementService.cs:1910` | `e.AbsenceType == "VACATION"` → `AbsenceTypes.Vacation`; `"SPECIAL_HOLIDAY" => "SPECIAL_HOLIDAY_ALLOWANCE"` → `"SPECIAL_HOLIDAY" => AbsenceTypes.SpecialHolidayAllowance`. The left side is an entitlement type and stays. Constants at `src/SharedKernel/StatsTid.SharedKernel/Models/AbsenceEntry.cs:38`, `:45`; both files already `using StatsTid.SharedKernel.Models;`. **Only these two**: `BalanceEndpoints.cs:18, :432, :667, :1342` are entitlement-type literals, a different vocabulary | backend-infrastructure | TIDY-1 |
| 11a | `src/Infrastructure/StatsTid.Infrastructure/ApprovalPeriodRepository.cs:63-64` | `in the Payroll host (which registers no TimeProvider) every branch is TimeProvider.System.` → "in the Payroll host, which registers `TimeProvider.System` as a singleton since S147 TASK-14703 (Payroll `Program.cs:192`), every branch is that same system clock." | backend-infrastructure | TIDY-1 |
| 11c | `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs:1689` | `(agreement code, home Organisation, unit, active flag)` → `(agreement code, home Organisation, active flag)`. `UpdateUserRequest` (`:3934-3990`) has no unit member | backend-infrastructure | TIDY-1 |
| 11d *(optional)* | `tests/StatsTid.Tests.Unit/Security/DepartedSubjectTests.cs:64-66` | Add "It alone pins the narrower mutation 'the deactivated flag counts only when no end date is set' (`!IsActive && EmploymentEndDate is null`); M-12 also trips `DeactivatedNoEndDate_IsDeparted`." **Only with that mutation run** in a detached worktree and its failure quoted (standing constraint "N … prove RED by ONE named mutation") | test-qa | TIDY-1-T |
| 12c | `ApprovalPeriodRepository.cs:1813` and `:1826` (`UpdateStatusAsync`, both overloads) | Add `<b>Test-only.</b>` to the first summary: "No production code calls either overload. It writes any status unconditionally, without the four-eyes and departed checks; production transitions go through `<see cref="TryUpdateStatusConditionalAsync"/>`. Kept for tests that seed a status." Add "Test-only, as above." to `:1826`'s summary. Census: `git grep -n "UpdateStatusAsync(" -- src` → the definitions and docs, plus `OvertimeEndpoints.cs:417`, `:507`, which call `preApprovalRepo`, a different repository | backend-infrastructure | TIDY-1 |
| 13a | `tests/StatsTid.Tests.Unit/Rules/RestPeriodNotCheckedTests.cs:228` (list said `:229`) | `activity: "VACATION"` → `activity: "ON_CALL"`. A real activity, `OnCallDutyRule.cs:38`. "TRAVEL" is not one (BC-3); the fact's assertions are unchanged | test-qa | TIDY-1-T |
| 13b | `tests/StatsTid.Tests.Unit/Rules/ComplianceCheckResultMergerNotCheckedTests.cs` | `git mv` → `tests/StatsTid.Tests.Unit/Segmentation/`, and `:4` `namespace StatsTid.Tests.Unit.Rules;` → `namespace StatsTid.Tests.Unit.Segmentation;`. The Unit count is unchanged. No CI filter is namespace-keyed (`.github/workflows/ci.yml` has no `FullyQualifiedName`) | test-qa | TIDY-1-T |
| 13e | `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RestPeriodRule.cs:176` (`NormalizeEntries`, no summary today) | Add a summary: "Keeps the entries dated inside [periodStart, periodEnd], sorted by date then start time. It does NOT split an entry that crosses midnight. The Backend splits those before the call (`MidnightCrossingNormalizer`, `ComplianceEndpoints.cs:208`), so a caller passing raw entries would see an overnight shift only on its start date." | rule-engine (comment only) | TIDY-1 |
| 15b | `src/Backend/StatsTid.Backend.Api/Validation/RequestValidator.cs:27-45` + `tests/StatsTid.Tests.Unit/Validation/RequestValidatorTests.cs:61-77` | Delete `ValidateAbsence` and its two facts (`Absence_InvalidAbsenceType_ReturnsError`, `Absence_ValidRequest_NoError`). Rewrite the header note `:8-12` to say the private list went with TASK-14708 and the uncalled `ValidateAbsence` with the tidy pass. Drop `using StatsTid.SharedKernel.Models;` (`:1`) if nothing else uses it. This reverses TASK-14708 contract 6 ("kept, re-pointed") by the list's own ruling (Fable 14708 NOTE 7); recorded so the reviewer does not flag it. Unit −2 | backend-infrastructure + test-qa | TIDY-1 / TIDY-1-T |
| 15c | `tests/StatsTid.Tests.Regression/Payroll/SickCodeVocabularyPayrollTests.cs:266-268`, `:272-277`, `:489` | (i) Keep the derived factory `stubbed` in a field and dispose it in `DisposeAsync`. This is harmless even though the parent factory probably already disposes it (BC-9). (ii) `MintEmployeeToken` takes the employee's agreement (it hard-codes `"AC"` at `:489`) and the Skema-save helper (`:438`) passes it through. The claim is written but read nowhere in `src`: `git grep -n "StatsTidClaims.AgreementCode" -- src` → `JwtTokenService.cs:31` only. So nothing the facts assert can move | test-qa | TIDY-1-T |
| 16 | `tests/StatsTid.Tests.Regression/Security/DepartedSelfElevatedRouteTests.cs` | **`:54-55`** `(403 carrying the date-free <see cref="DepartedSubject.DenyReason"/>)` → "(403 with a date-free reason: `DepartedSubject.DenyReason`, or on the three overtime routes SEC-056's self-decision reason, which answers first by the Orchestrator's ruling (a) of 2026-10-29)". **`:78`** `(the production strings live on the TASK-14705 branch)` → "(the production strings are inline in `OvertimeEndpoints.cs`, at the three self-guards)". **`:119-120`** `today the validator admits the covered leader's own id and the route approves the row (200)` → "the route answers 200 (row approved) only if BOTH guards are absent: SEC-056's self-guard and the departed-self clause". **`:141`** `(today the row is rejected, 200)` → "(200, row rejected, only if both guards are absent)". **`:163`** `(today the balance is adjusted, 200)` → "(200, balance adjusted, only if both guards are absent)" | test-qa | TIDY-1-T |
| 17 | `src/Backend/StatsTid.Backend.Api/Endpoints/EmploymentDateEndpoints.cs:152-153`, `:450-451` | `the GET above stays self-readable.` (both) → "the GET above is self-readable only while the reader is employed: since S147 a departed self's read is refused too (consult R1; the validator's departed-self clause)." | backend-infrastructure | TIDY-1 |
| 18b *(optional)* | `DepartedSelfElevatedRouteTests.cs:59-64` ("Seeding") | Add: "The host runs without the leaver-flip job (`withoutLeaverJob: true`, `:33`; TASK-14719 fix-up 1), so no seeded active-but-departed subject is switched off during a fact; every `WithFixedToday` host derived from it inherits that." | test-qa | TIDY-1-T |
| 19 | `frontend/src/hooks/useSkema.ts:21-24`, `:133`; `frontend/src/pages/SkemaPage.tsx:1364-1366` | "dropped after their month left the screen" → "save warnings kept out of the clearable slot: saves that were DROPPED and are never resent (their month left the screen, or the data they were sent under was replaced, also in the same month), and warnings moved out of the slot by an 'uvist' report or a reload". The paths are at `useSkema.ts:253-257`, `:267-279`, `:316-324` | ux | TIDY-4 |
| 20a | `src/Infrastructure/StatsTid.Infrastructure/ProjectRepository.cs:143-187` | Delete `EnsureContainerAsync` (`:150`) and `ReindexSelectionsAsync` (`:169`) with their summaries. Census: `git grep -n -e EnsureContainerAsync -e ReindexSelectionsAsync -- src tests frontend/src tools` → 2 (the definitions) → 0 | backend-infrastructure | TIDY-1 |
| 20b | `ProjectRepository.cs:118-133` (`AcquireEmployeeLockAsync` summary); `src/Infrastructure/StatsTid.Infrastructure/SkemaRowPreferenceRepository.cs:85-87` | In the first, replace the sentence from `Without it, a row-preferences PUT's DELETE-and-reinsert can race a legacy add` to `latest committed state.` with: "It was added (S72) because a row-preferences PUT could race the legacy single-row add, whose MAX(sort_order) and reindex read the pre-delete snapshot. That add was retired with its route in S147 (TASK-14710, the note at `:112-117`), so today the lock serialises concurrent row-preferences PUTs for one employee, the only selection writer." In the second: `so this replacement and the R14-aligned legacy add/remove serialize per employee and can never interleave between each other's read-derive-write windows` → "so two replacements for the same employee serialize and can never interleave (the legacy add/remove it also guarded were retired in S147 TASK-14710)" | backend-infrastructure | TIDY-1 |
| 20c | `EmploymentDateEndpoints.cs:107-109`; `src/Infrastructure/StatsTid.Infrastructure/UserRepository.cs:520-522` (list said `~:490`; its `:255-266` banner was already fixed by TASK-14710); `src/SharedKernel/StatsTid.SharedKernel/Models/User.cs:51-53` | "a CLOSED spell … means the Step-A poller already flipped is_active=FALSE" (both) → "a CLOSED spell … is, from day 6 after the last day, a row the Step-A poller has switched to is_active=FALSE (S147 TASK-14710: during the manager's 5-day grace, D+1 … D+5, it is still active)". In User.cs: `(Copenhagen business date &gt; EmploymentEndDate ⇒ deactivate — same-tx in the admin end-date endpoint for an already-passed date, else the Step-A poller)` → "(deactivated from day 6, once the manager's 5-day grace has passed — `EmploymentEndDateLifecycleWriter.IsPastGrace`, S147 TASK-14710, owner OQ-2 — same-tx in the admin end-date endpoint for a date already past the grace, else the Step-A poller; the person's own login and writes end on day 1 by the end-date rule, `DepartedSubject`)". Use `<c>` for the Infrastructure type | backend-infrastructure (extended into SharedKernel/Models, comment only) | TIDY-1 |
| 21b | `tests/StatsTid.Tests.Regression/Approval/DesignatedApproverAuthorityTests.cs:1080-1081`; `tests/StatsTid.Tests.Regression/Contracts/S116ApprovalSpecRuntimeTests.cs:637` | `IsActiveLeaderOrAboveAsync` → `IsActingLeaderOrAboveAsync`, renamed by TASK-14712 (`IAuthorityFactsSource.cs:25-34`; implementation `DesignatedApproverAuthorizer.cs:637`). In the first, also `(the authorizer :131-145), which requires the actor to be an ACTIVE user` → "(`DesignatedApproverAuthorizer.cs:637`), which requires the actor to be an ACTING user (active and not past their last day)" | test-qa | TIDY-1-T |
| 22a | `ApprovalEndpoints.cs:2293-2295`; `SkemaEndpoints.cs:726-728`; `TimeEndpoints.cs:120-123` (list said `:2128`, `:698`, `:120`) | `can never deny an actor (that) validator admitted (… for a terminated subject)` → "can still refuse an actor the validator admitted: a mixed-role token passes the validator and is refused here by the primary-role arm, the outcome R9b intends (S147 TASK-14710 review N-3)" | backend-infrastructure | TIDY-1 |
| 22b | `tests/StatsTid.Tests.Regression/Leaver/LeaverGraceTests.cs:210-211` | `the selection step cannot be observed from a test without touching src, so that part is not pinned here.` → "the selection step is observable only through a logger: with the filter reverted, the in-lock guard writes its Debug line (`VacationSettlementService.cs:285-288`). This fact captures no logs, so that part is not pinned here." | test-qa | TIDY-1-T |
| 23a | `frontend/package.json:13` (`lint`) and `frontend/eslint.config.mjs` (`TYPED_SLICE_FILES`, the `files:` block at `:174`) | Add `src/hooks/useHrFollowUp.ts` (TASK-14714's typed-call file: eight `apiClient.get` calls, no `as`, no type argument) to both lists; `npm run lint` proves it. `FollowUpLists.tsx` makes no `apiClient` call, so the tier, which guards call sites, does not apply to it. Optional: `src/pages/admin/AuditLogView.tsx` (typed calls, no banned pattern). The wider gap is Class D, X-6 | ux | TIDY-4 |
| 23c | `src/Backend/StatsTid.Backend.Api/Endpoints/Helpers/TreeRootDriftRetry.cs:13-14` | After `<see cref="ReportingLineRepository.AcquireTreeLockForEmployeeAsync"/>` add ", its terminated-inclusive overload `<see cref="ReportingLineRepository.AcquireTreeLockForEmployeeIncludingTerminatedAsync"/>` (S147 TASK-14713, the leaver's HR-only routes)". The method is at `ReportingLineRepository.cs:787` | backend-infrastructure | TIDY-1 |
| 24a | `VacationSettlementService.cs:1138-1140` (X3) | Apply `.claude/plans/s147/O-6-DOCS-OWED.md:495-512` verbatim: Old text matches at `94bd32be`; keep CRLF | backend-infrastructure | TIDY-1 |
| 24c | `frontend/src/pages/admin/opfoelgning/__tests__/FollowUpLists.leaverActions.test.tsx:189` | `id('row'.replace('row', 'turn'))` → `id('turn')` | ux | TIDY-4 |
| 25a | `src/SharedKernel/StatsTid.SharedKernel/Events/PayrollExportGenerated.cs:30-31`, `:37-38`, `:44`, `:50`, `:56` | `:30-31` → "Every export carries one since S147: TASK-14716 retired the raw `/export` and `/export-period` routes, which bypassed approval. Null only on events those routes wrote before." `:37-38`, `:44`, `:50`: "null on raw exports (and pre-S146 events)" → "null on an export of a month before the ledger start (flex not tracked, case 3a, S147 TASK-14715), on events the raw export routes wrote before S147 retired them, and on pre-S146 events". `:56` `null on the raw <c>/export</c> + <c>/export-period</c> (their lines carry what their caller sent)` → "null on events the raw `/export` and `/export-period` wrote before S147 retired them (their lines carried what their caller sent)" | backend-infrastructure (extended into SharedKernel/Events, comment only) | TIDY-1 |
| 25b | `src/Infrastructure/StatsTid.Infrastructure/AuditMappers/PayrollExportGeneratedAuditMapper.cs:32`, `:38` | `:32` `Null on the raw exports and every pre-S146 event` → "Null on an untracked export (a month before the ledger start, S147 TASK-14715), on events the retired raw routes wrote, and on every pre-S146 event". `:38` `Null on the raw exports and every pre-fix event` → "Null on events the retired raw routes wrote and on every pre-fix event". A case-3a export does carry its manifest: `PayrollExportService.cs:360` | backend-infrastructure | TIDY-1 |
| 25c | `src/Infrastructure/StatsTid.Infrastructure/FlexLedgerReadRepository.cs:57`, `:76-77` | `:57` `(written by the raw export routes, or before the ledger existed)` → "(written by the raw export routes before S147 retired them, before the ledger existed, or, since S147 TASK-14715, by a calculating export of a month before the ledger start, flex not tracked)". `:76-77` `a month the raw export routes recorded — those bypass approval by design and are GlobalAdmin only too.` → "a month the raw export routes recorded before S147 retired them (they bypassed approval and were GlobalAdmin only)." | backend-infrastructure | TIDY-1 |
| 25d | `…/Payroll/Services/HttpRuleClassificationProvider.cs:58-59`; `…/IPriorPeriodApprovalReader.cs:34-35`; `…/PriorMonthNotApprovedException.cs:13` | `the raw routes refuse a split month by their own check` → "the raw routes, retired in S147 TASK-14716, refused a split month by their own check". `A month exported through the raw routes has no row (they write no approval period, R16)` → "A month the raw routes exported before S147 retired them has no row (they wrote no approval period, R16)". `(for example one exported through the raw routes, R16)` → "(for example one the raw routes exported before S147 retired them, R16)" | payroll-integration | TIDY-2 |
| 25e | `src/SharedKernel/StatsTid.SharedKernel/Segmentation/BoundaryCause.cs:105-106`; `…/Segmentation/PeriodPlanner.cs:328-329` | `and the raw export routes refuse every split month by their own reason` → "and, until S147 TASK-14716 retired them, the raw export routes refused every split month by their own reason". `(and the raw export routes refuse it by their own reason, after planning)` → "(the raw export routes, which refused it by their own reason after planning, were retired in S147 TASK-14716)" | backend-infrastructure (extended into SharedKernel/Segmentation, comment only) | TIDY-1 |
| 25f | `tests/StatsTid.Tests.Regression/Payroll/FlexLedgerCase3aHostTests.cs:293`, `:330-331` | `$"EMP-S147-C3A-SELF"` → `"EMP-S147-C3A-SELF"`; one blank line between `}` (`:330`) and the D1 banner (`:331`) | test-qa | TIDY-1-T |

**Extra finds (not on the list; same class, same tasks).**

| # | Site | Edit | Task |
|---|---|---|---|
| X-1 | `…/Payroll/Program.cs:677` | `(for a month exported through the raw routes it typically has no approval row at all)` → "(a month the raw routes exported before S147 retired them typically has no approval row at all)" | TIDY-2 |
| X-2 | `…/Payroll/Program.cs:492-494` (the `PayrollExportConflictException` catch) | Names only "already exported" and "no longer APPROVED". Since S147 the same exception also carries the approval re-assert's refusals (`PayrollExportService.cs:518-552`): not found, not the exported employee's whole month (R-G2/R-G8), approved by its sender (four-eyes, SEC-053), approved by its own employee. It also carries "same lines, different flex balance" (`:394-398`) and the month-spanning line (`:197`). List them, and add: "all answer this one kind; a distinct four-eyes kind is a recorded follow-up (tidy item 12)" | TIDY-2 |
| X-3 *(optional)* | `…/Payroll/Program.cs:474-477` (export-time recheck) | `(another export or a correction recorded the previous month meanwhile)` → "(another export or a correction changed the ledger meanwhile: the previous month's record or, since S147 TASK-14700, any earlier month in the chain, when the diagnostic names the inner `payroll-prior-month-stale`)" (`PeriodCalculationService.cs:929-933`) | TIDY-2 |
| X-4 *(optional)* | `TreeRootDriftRetry.cs:6-8` | The mutator list omits the approval routes, which have used the wrapper since S78 R1 (`ApprovalEndpoints.cs:327`, `:634`, `:1816`); add them | TIDY-1 |
| X-5 *(optional)* | `RequestValidator.cs:47-52` `ValidatePartTimeFraction` | Dead: `git grep -n ValidatePartTimeFraction -- src tests` → the definition only. Delete with 15b | TIDY-1 |

---

## Class B — small behavioural fixes

| # | Change | Agent | Pin (kind · Red conditions) | Why it is small |
|---|---|---|---|---|
| 8a | The full-day refusal sentence, emitted at `SkemaEndpoints.cs:1143` (pre-transaction) and `:2108` (in-lock), interpolates `{requiredHours}` under the server's culture. The container has no `LANG`, so a Danish user reads "(7.4 timer)". Build the sentence in ONE public static helper (hint: beside `AbsenceTypeLabels.LabelFor`, `src/Backend/StatsTid.Backend.Api/Services/AbsenceTypeLabels.cs`; the Backend has no `InternalsVisibleTo`, `:41`) that formats `requiredHours` with `CultureInfo.GetCultureInfo("da-DK")` and keeps `dd-MM-yyyy` for the date. Culture only: do not change the number's scale or format. ICU is present (aspnet:8.0, no `InvariantGlobalization`; `CopenhagenBusinessDate.cs:236-240`). Census after: `git grep -n "kan kun registreres som hel dag" -- src` → 1 hit, inside the helper (2 today) | backend-infrastructure | **N (Unit)** `FullDayOnlyMessage_WritesHoursWithADecimalComma_WhateverTheThreadCulture`: sets `CultureInfo.CurrentCulture` to `InvariantCulture` (and to `en-US`) in try/finally; asserts "(7,4 timer)", "(3,7 timer)" and "03-03-2026". Red: mutation "interpolate without the culture" gives "7.4". **F (Docker, CI)**: `SkemaFullDayOnlyGuardTests.FullDay_UnderBasis_Rejected422_TypedErrorCarriesRequiredHours` (`:103-119`) gains `Assert.Contains("(3,7 timer)", body.GetProperty("message").GetString())`, labelled `// S147 tidy item 8:`. Red argued: CI's runner has no Danish locale, so today's message reads "(3.7 timer)"; say in the docstring that on a da-DK machine it would pass today. TestServer does not flow the test's culture by default, so the Unit fact is the deterministic pin | Two call sites, one pure function. No contract change: `message` is free text and `requiredHours` is untouched; no REGEN |
| 12d *(test-only)* | `PayrollSenderSeparationHostTests.AssertNothingWrittenAsync` (`:243-251`) promises "no audit row" but checks only records and outbox events | test-qa | **R (Docker, CI)**: add `SELECT COUNT(*) FROM audit_projection WHERE target_resource_id = @e AND event_type = 'PayrollExportGenerated'` = 0. The precedent is `FlexLedgerHostHarness.cs:613`. Count `audit_projection`, never `audit_log`: the HTTP middleware logs the 409 request itself there. Red: an export whose audit projection is written outside the rolled-back transaction | One assertion in a helper three facts share (`:98`, `:116`, `:134`) |
| 15a | `AbsenceNormEffect`'s zero value is `Credit` (`src/SharedKernel/StatsTid.SharedKernel/Models/AbsenceClassification.cs:7-26`), so a forgotten value fails open (Fable 14708 NOTE 1). Number it explicitly: `Neutral = 0, Credit = 1, Reduction = 2`. Neutral already means "counts as neither", the same treatment an unknown code gets (`TryGetEffect` false, `:134-140`); an `Unknown` member would add a fourth state with no reader. Census: `git grep -n AbsenceNormEffect -- src tests` → one reader outside the file, by name (`AbsenceRule.cs:50`), plus tests by name. It is not serialized or served (RR-4, `:38-39`). Engine outputs are unchanged, so the evaluation-model marker does not move (ADR-016 D10) | backend-infrastructure (extended into SharedKernel/Models, cross-domain authorized; `docs/AGENTS.md:78`) | **F (Unit)** in `tests/StatsTid.Tests.Unit/Rules/AbsenceClassificationTests.cs`: `Assert.Equal(AbsenceNormEffect.Neutral, default(AbsenceNormEffect))`. Red on master: "Expected: Neutral, Actual: Credit" (run on master, quote it) | One enum; no output, wire or storage change |
| 18a *(optional, test-only)* | Prove the fixture switch is inherited by the payout fact's derived host (`DepartedSelfElevatedRouteTests.cs:259`) | test-qa | **B (Docker, CI)**, "boundary pin — holds today": `Assert.Empty(host.Services.GetServices<IHostedService>().OfType<SettlementCloseService>())` (`SettlementCloseService.cs:109`). Red: a `WithFixedToday` that drops `withoutLeaverJob` | One line |
| 22c | The reversal failure text at `src/Infrastructure/StatsTid.Infrastructure/SettlementReversalService.cs:388-391` (K-14) ends `use the explicit bare-reversal mode to park the tuple instead.` Append: "or, when the corrected end date falls inside the manager's 5-day grace (which reactivates the leaver), retry from day 6 after it." This wording is already in ADR-040's amendment (`ADR-040…md:487-490`). The `failure` code `SupersedeNotEligible` is unchanged | backend-infrastructure | **F (Docker, CI)**: `SettlementReversalTests.ReverseSupersede_IneligibleCorrectedState_FullRollback_NothingChanged` (`:410-440`) gains `Assert.Contains("day 6", result.FailureReason)`. Red: today's sentence has no "day 6" | One sentence; the 409's `error` text (`SettlementReversalEndpoints.cs:296-300`) is free text |
| 23b | HR's leaver list updates its own rows, but the landing tiles stay stale until reload. `LeaverFinalMonthList` (`FollowUpLists.tsx:386-388`) gains `onResolved: () => void`, called after each successful send, approve or reject (`:248`, `:260`, `:274`), mirroring `WorklistList` (`WorklistList.tsx:250`). `OpfoelgningPage.tsx:373` passes a callback that reloads `leaverSummary` (`:150`) and `approvedNotExported` (`:157`): an approve moves the month from HRP-011 to HRP-022. The agent reports any other tile an action changes | ux | **F (vitest)** in `FollowUpLists.leaverActions.test.tsx`: after a successful approve, the `onResolved` spy is called once. Red: today the prop is ignored, so 0 calls (vitest does not type-check). Optional page-level F in `OpfoelgningPage.test.tsx`: the leaver summary fetch is called a second time | One prop and one callback, on an existing pattern |
| 20d *(only if C-2 is ruled (b))* | Drop `employmentEndDate = user.EmploymentEndDate,` from the 422 at `src/Backend/StatsTid.Backend.Api/Endpoints/TerminationPayoutRequestEndpoints.cs:252` | backend-infrastructure | **F (Docker, CI)**, a new fact in `tests/StatsTid.Tests.Regression/Settlement/TerminationPayoutRequestEndpointTests.cs`: an end date not yet passed gives 422, and the body has no `employmentEndDate` property. Red: today it does | One field. No consumer (`git grep -n termination-payout-request -- frontend/src` → only the typed-overload test); the 422 is anonymous and absent from `api-types.ts`, so no REGEN |

---

## Class C — a decision first

**C-1 · Item 7, the entitlement names (owner's decision: user-facing Danish wording).**
- **What is at stake.** The same five entitlements are spelled three ways in the frontend, and a fourth table on the server matches one of them:
  - the Skema quota alert uses plural balance names ("Omsorgsdage", "Særlige feriedage", "Barns sygedag", "Seniordage"; `SkemaPage.tsx:88-94`);
  - admin configuration uses singular type names ("Omsorgsdag", "Særlig feriedag", "Barnets sygedag", "Seniordag"; `entitlementConstants.ts:13-19`);
  - the Årsoversigt tiles have their own set (`ArsoversigtPage.tsx:108-119`);
  - the server's balance labels (`BalanceEndpoints.cs:16-23`) equal the Skema set exactly.
- **Not just singular versus plural.** "Barns" versus "Barnets sygedag" is a word choice. The server elsewhere says "Barns" (`DefaultEntitlementConfigs.cs:146`; `AbsenceTypeLabels.cs:61` "Barns 1. sygedag").
- **Options.**
  - **(a) One wording per context.** Balance and quota surfaces use the plural balance names, which are the server's. Admin type pickers use the singular type names. "Barns sygedag" everywhere. The two frontend balance tables become one map in `entitlementConstants.ts`. Visible change: admin "Barnets sygedag" becomes "Barns sygedag".
  - **(b) One spelling everywhere.** Simplest, but a quota sentence in the singular reads oddly (14709 W1, `.claude/reviews/S147-TASK-14709-5a-reviewer.md:14`).
  - **(c) Defer to the ROADMAP backlog.**
- **Lean:** (a). It keeps every sentence that reads well today, removes the duplicate, and settles "Barns". If ruled (a) before TIDY-4 is dispatched, it is a small (B) in TIDY-4 with vitest pins on the visible strings. If not ruled, it takes (c) and the pass proceeds without it.

**C-2 · Item 20, the payout request's 422 echoes the end date (Orchestrator's decision: the reading of ADR-040 D3/D7).**
- **The facts.** `TerminationPayoutRequestEndpoints.cs:249-253` returns `employmentEndDate` in a 422 on an `HROrAbove` route (`:372`).
  - D7 bans dates from **employee-facing** bodies (`ADR-040…md:140`). D3 says rejection bodies are date-free so dates "never leak through error shapes" (`:87-90`).
  - The audience is HR, who may read the date anyway (the HR-gated end-date GET).
  - A departed HR self is refused before this point (TASK-14703; `DepartedSelfElevatedRouteTests.cs:247`).
- **Options.**
  - **(a) Within D7 as written.** Record the reading in `SECURITY.md`'s D7 census; no change.
  - **(b) Drop the field.** Row 20d: matches D3's "rejection bodies are date-free" and this sprint's norm (standing constraint: "Every 403 this sprint adds carries a date-free reason"). One line and one Docker fact.
- **Lean:** (b). No confidentiality is at stake: HR may read the date. But the house rule is simpler to keep true with no exceptions, and nothing consumes the field.

---

## Class D — beyond tidy

| # | Item | Goes to | Why not now |
|---|---|---|---|
| 3 | Lift `HeldLock` and the waiter observation into the harness, ending the duplicate at `FlexLedgerStaleChainRaceTests.cs:48-150` (the original is in `FlexLedgerRaceTests.cs:52`) | ROADMAP backlog (test hygiene), or the next task that edits either race file | Optional; a Docker-only refactor of race machinery, verifiable only in CI, at the last step before C-1 |
| 5 | Optional pins: a SPECIAL_HOLIDAY config ending mid-month; the April 2026 OK-transition month | S148 stage 0's test wave | Optional; new Docker coverage, not drift |
| 11b | One 403-and-log helper instead of four copies (`AdminEndpoints.cs:3815`, `UnitEndpoints.cs:1082`, `ReportingLineEndpoints.cs:2965`, Payroll inline `Program.cs:589-593`; the list said `:665-670`) | QUAL register candidate | Not mechanical. The Payroll host cannot use `Backend.Api/Endpoints/Helpers/`, and the existing shared `DepartedSubjectWriteFloor.Refusal` (`:145-160`) logs under another category with a `Subject=` field, so consolidating changes log output on security refusals |
| 12a | A repository-level Docker pin for the SQL predicate's legacy `submitted_by` half (`ApprovalPeriodRepository.cs:1946`; RES-003 item 4's pair) | S148 stage 0's test wave | New coverage of a path the C# guard makes unreachable (Fable 14704 NOTE 1); CI-only |
| 12b | Payroll's four-eyes refusal has no kind of its own. It is calculated, then refused inside the export transaction (`PayrollExportService.cs:544-547`) and answered as the generic `payroll-export-conflict` (`Program.cs:491-503`) | QUAL register candidate → S148 stage 0 | A design fork (pre-screen at admission, or a new kind), a payroll problem-contract change (high-risk payroll export) and Docker-only pins. Not small. X-2 documents the shared kind meanwhile |
| 12e | Teamoversigt shows "Kunne ikke godkende X." (`TeamOversigt.tsx:602`) instead of the server's reason, in Danish, for the SEC-009 and SEC-053 403s | ROADMAP backlog (UX) | A new behaviour; the reasons are English today and need a Danish mapping |
| 14a | Which `ComplianceWarnings` items are live-announced: only the notice has `role="status"` (`ComplianceWarnings.tsx:110`) | S148 stage 0, with T8 (QUAL-234's rest check proper replaces the interim notice) | An accessibility design choice for a component T8 rebuilds |
| 14b | A neutral style for the notice (it reuses `styles.warning`, `:110`) | Same; conditional on the notice outliving S148 stage 0 | As above |
| 21a | The HRP-013 orphan roll-up counts a grace-period manager as covering (`HrFollowUpApprovalReadRepository.cs:410-436`; the list said `:388-410`, which moved with TASK-14713) | S148 stage 0, plus a note on the HRP-013 register row | **Not small:** the roll-up promises to equal the per-organisation roster card's `IsOrphan` (`:398-409`; `ApprovalPeriodRepository.cs:1247`), so both reads must change together; and a stand-in appointed before the manager left keeps covering (owner Q-B = (c)), so "covered" is not simply "manager acting". Two reads, a ruling-shaped rule and Docker facts |
| 23d | A missing users row in the bound's handler overload goes to the lock and throws 500 (`ApprovalEndpoints.cs:93-106`, the summary `:91` says "left to the handler's own downstream reads") | QUAL register candidate | Pre-existing data-fault behaviour, outside the tidy pass's purpose |
| X-6 | The lint gap is wider than 14714: four more `src` files with typed calls are outside the lint list (`useCalendarBootstrap.ts`, `useHrBackdateWorklist.ts`, `useTermination.ts`, plus `AuditLogView.tsx`, which is clean). By a rough count three of them hold `as` casts, and one an explicit type argument | QUAL register candidate | Bringing them under the rule means removing casts in four files. "Lint `src/**`" alone would not help: the config's two `files:` blocks (`eslint.config.mjs:174`, `:187`) decide which files get the rules |

---

## Already done, or no action

| # | Item | Disposition |
|---|---|---|
| 1b | `PeriodCalculationService.cs:771-773` | Done by TASK-14715: `:773-774` now names `payroll-prior-month-not-recorded` or `payroll-prior-month-stale` |
| 1c | Payroll `Program.cs:459-462` | Done by TASK-14715: `:388-392` names the stale chain and case 3a |
| 9 | Third-person voice in the not-eligible alerts | Recorded at `SPRINT-147.md:338`; no action by design |
| 20e | The K-list residuals for O-6 | Done: both are in ADR-040's amendment (`ADR-040…md:487-493`) |
| 22e | "Tidy item 20's dead helpers and the 'closed spell' wording remain" | A duplicate of 20a and 20c |
| 24b | Three `as` casts in `FollowUpLists.tsx` (`:184`, `:199`, `:209`; the list said ~`:186`, `:197`, `:205`) | No action. They sit on `JSON.parse` output, which is `unknown`, and every field is re-checked with `typeof` or a fallback; the typed-call lint does not cover this file (row 23a). Recorded so nobody re-files them |
| 25g | Item 25's first-list payroll sites: `PeriodCalculationService.cs:1427`, `PayrollExportService.cs:330-334`, `:607-610` | Done by TASK-14715 (`:1427` reads "case 3a since S147 TASK-14715") and TASK-14716 (no live-route wording remains in `PayrollExportService.cs`) |

---

## The Orchestrator's own edits (no agent: `docs/**`, `.claude/**`, registers)

| # | Item | Edit |
|---|---|---|
| O-a | 10c | QUAL-236's row at close: the S147 422 checks the vocabulary, not the agreement's catalogue (`SkemaEndpoints.cs:965-974`), so a hand-built HK request can still save `CHILD_SICK_DAY_3` |
| O-b | 12f | `.claude/skills/launch-demo-system/SKILL.md:22` (`demo_admin`): add "cannot approve demo months by hand since S147 SEC-053: the DemoSeed sends every pending month as `demo_admin`, and a month's sender may not approve it; approve as `demo_styx1_0025` or `demo_styx1_0001`" |
| O-c | 13c | An erratum in the refinement's revision history (gitignored): T7/OQ-4's skip is the predicate in `ReconstructStints` (`RestPeriodRule.cs:504`), not `:222-226` or `:445`. The rest rule is `MinimumRestHours` (Directive 2003/88/EC, Arbejdstidsloven), not a `danish-agreements.md` rule |
| O-d | 13d | The test record names the fact behind AC-234i-1's "payroll lines identical". The planner found no payroll-lines golden by name; the structural argument holds (`git diff --stat 48a1ba25^1 48a1ba25` lists no payroll file). Name a fact, or record the argument |
| O-e | 14c, 22d | `docs/FRONTEND.md:113`, `:291`: the component shows the rest check's "not checked" notice as well as warnings, and the shared string lives in the component module by design. `:114`, `:292`: delete the `ProjectPicker` rows (no `ProjectPicker` file under `frontend/`) |
| O-f | 15d | `.claude/plans/s147/S148-BUILD-INPUT.md` S0-2 (`:41`): name the child-code rows to re-pin, `SickCodeVocabularyPayrollTests.cs:92-98` (the `CHILD_SICK_DAY*` `InlineData`), with `AbsenceClassificationTests.cs:120-122` |
| O-g | D rows | File the register candidates (11b, 12b, 23d, X-6), the ROADMAP lines (3, 12e; 7 if deferred), the S148 stage-0 inputs (5, 12a, 14a, 14b, 21a) and the HRP-013 note (21a) |

---

## Execution

### The tasks

**TIDY-1 = TASK-14721 · `backend-infrastructure` (Opus).** Label: *backend-infrastructure (extended into src/SharedKernel and src/RuleEngine, comment only; cross-domain authorized)*.
- **Rows:** 10b, 11a, 11c, 12c, 13e, 15b (src), 17, 20a, 20b, 20c, 22a, 23c, 24a, 25a, 25b, 25c, 25e; X-4 and X-5 optional. Class A only.
- **Files (exactly these):**
  - `src/Backend/StatsTid.Backend.Api/Endpoints/{AdminEndpoints,ApprovalEndpoints,SkemaEndpoints,TimeEndpoints,EmploymentDateEndpoints,BalanceEndpoints}.cs`
  - `src/Backend/StatsTid.Backend.Api/Endpoints/Helpers/TreeRootDriftRetry.cs`
  - `src/Backend/StatsTid.Backend.Api/Validation/RequestValidator.cs`
  - `src/Infrastructure/StatsTid.Infrastructure/{ApprovalPeriodRepository,UserRepository,ProjectRepository,SkemaRowPreferenceRepository,VacationSettlementService,FlexLedgerReadRepository}.cs`
  - `src/Infrastructure/StatsTid.Infrastructure/AuditMappers/PayrollExportGeneratedAuditMapper.cs`
  - `src/SharedKernel/StatsTid.SharedKernel/Events/PayrollExportGenerated.cs`
  - `src/SharedKernel/StatsTid.SharedKernel/Models/User.cs`
  - `src/SharedKernel/StatsTid.SharedKernel/Segmentation/{BoundaryCause,PeriodPlanner}.cs`
  - `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RestPeriodRule.cs`
- **Census (before → after, re-run and report):**
  - `git grep -n -e EnsureContainerAsync -e ReindexSelectionsAsync -e ValidateAbsence -- src tests frontend/src tools`: 6 → 0, together with TIDY-1-T. With X-5, add `-e ValidatePartTimeFraction` (+1 → 0).
  - `git grep -n -e "the GET above stays" -e "validator admitted" -e "registers no TimeProvider" -e "home Organisation, unit, active flag" -e "poller already flipped" -e "means the Step-A poller already" -- src`: 9 → 0.
  - `git grep -n -i -e "null on raw exports" -e "Null on the raw exports" -e "null on the raw exports" -e "which bypass approval\." -e "written by the raw export routes, or before" -e "those bypass approval by design" -e "raw export routes refuse" -- src`: 9 → 0 in this task's files.
  - `git grep -n "AbsenceType == \"VACATION\"" -- src/Backend`: 1 → 0.
- **Acceptance:**
  - Every hunk is one of: a comment or docstring; a deletion whose census is empty; a literal swapped for a constant of the same value.
  - `dotnet build` 0 errors.
  - Unit −2 is expected only once TIDY-1-T lands; the branch alone does not compile without the -T deletion, so the pair merges together.

**TIDY-1-T = TASK-14721-T · `test-qa` (Sonnet), paired on TIDY-1's branch.**
- **Rows:** 1e, 2, 4a, 4b, 4c, 11d (optional), 13a, 13b, 15b (tests), 15c, 16, 18b (optional), 21b, 22b, 25f.
- **Files:**
  - `tests/StatsTid.Tests.Unit/Payroll/FlexLedgerChainWalkTests.cs`
  - `tests/StatsTid.Tests.Unit/Security/DepartedSubjectTests.cs` (optional)
  - `tests/StatsTid.Tests.Unit/Rules/RestPeriodNotCheckedTests.cs`
  - `tests/StatsTid.Tests.Unit/Rules/ComplianceCheckResultMergerNotCheckedTests.cs` → `tests/StatsTid.Tests.Unit/Segmentation/`
  - `tests/StatsTid.Tests.Unit/Validation/RequestValidatorTests.cs`
  - `tests/StatsTid.Tests.Regression/Payroll/{FlexLedgerHostTests,SickCodeVocabularyPayrollTests,FlexLedgerCase3aHostTests}.cs`
  - `tests/StatsTid.Tests.Regression/Skema/SkemaSpecialHolidayRowTests.cs`
  - `tests/StatsTid.Tests.Regression/Security/DepartedSelfElevatedRouteTests.cs`
  - `tests/StatsTid.Tests.Regression/Approval/DesignatedApproverAuthorityTests.cs`
  - `tests/StatsTid.Tests.Regression/Contracts/S116ApprovalSpecRuntimeTests.cs`
  - `tests/StatsTid.Tests.Regression/Leaver/LeaverGraceTests.cs`
- **Census:**
  - `git grep -n -E "\bM-(7|8|9|1[0-8])\b" -- tests/StatsTid.Tests.Unit/Payroll/FlexLedgerChainWalkTests.cs`: 17 lines → 0. `M-229-` occurrences: 0 → 18.
  - `git grep -n IsActiveLeaderOrAboveAsync -- tests`: 2 → 0.
  - `git grep -n "the production strings live on the TASK-14705 branch" -- tests`: 1 → 0.
- **Acceptance:**
  - No assertion changes except 15c's token argument, which is inert (see the row).
  - No expected value changes.
  - Never `src/**`.
  - Unit −2 (the two `ValidateAbsence` facts); the moved file keeps its count.

**TIDY-2 = TASK-14722 · `payroll-integration` (Opus).**
- **Rows:** 1a, 1d, 1f, 10a, 25d; X-1, X-2, X-3 (optional). Class A only.
- **Files:**
  - `src/Integrations/StatsTid.Integrations.Payroll/Program.cs`
  - `src/Integrations/StatsTid.Integrations.Payroll/Services/{PeriodCalculationService,FlexLedgerOpening,HttpRuleClassificationProvider,IPriorPeriodApprovalReader,PriorMonthNotApprovedException}.cs`
  - `docker/postgres/init.sql` (line 1250 only)
- **Census:**
  - `git grep -n -e "previous month's first day, this month's last day" -e "raw routes refuse a split" -e "through the raw routes has no row" -e "exported through the raw routes, R16" -e "AbsenceRule.cs:112-114" -- src docker`: 5 → 0.
  - `python tools/check_docs.py` stays green (db-schema is generated from `init.sql`).
- **Acceptance:** comment-only hunks; build 0 errors.

**TIDY-3 = TASK-14723 · `backend-infrastructure` (Opus).** Label: *(extended into src/SharedKernel/Models/AbsenceClassification.cs; cross-domain authorized)*.
- **Rows:** 8a, 15a, 22c, plus 20d if C-2 is ruled (b).
- **Files:**
  - `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` (`:1143`, `:2108`)
  - the helper's home (hint: `src/Backend/StatsTid.Backend.Api/Services/AbsenceTypeLabels.cs`)
  - `src/SharedKernel/StatsTid.SharedKernel/Models/AbsenceClassification.cs`
  - `src/Infrastructure/StatsTid.Infrastructure/SettlementReversalService.cs`
  - (if ruled) `src/Backend/StatsTid.Backend.Api/Endpoints/TerminationPayoutRequestEndpoints.cs`
- **Census:** `git grep -n "kan kun registreres som hel dag" -- src`: 2 → 1. `git grep -n AbsenceNormEffect -- src tests`: every hit classified.

**TIDY-3-T = TASK-14723-T · `test-qa` (Sonnet), paired.**
- **Rows:** the pins in Class B for 8a, 15a and 22c; 12d; 18a (optional); 20d's fact if ruled.
- **Files:**
  - a new Unit file for the 8a helper (name it)
  - `tests/StatsTid.Tests.Unit/Rules/AbsenceClassificationTests.cs`
  - `tests/StatsTid.Tests.Regression/Skema/SkemaFullDayOnlyGuardTests.cs`
  - `tests/StatsTid.Tests.Regression/Settlement/SettlementReversalTests.cs`
  - `tests/StatsTid.Tests.Regression/Payroll/PayrollSenderSeparationHostTests.cs`
  - `tests/StatsTid.Tests.Regression/Security/DepartedSelfElevatedRouteTests.cs` (optional 18a)
  - (if ruled) `tests/StatsTid.Tests.Regression/Settlement/TerminationPayoutRequestEndpointTests.cs`
- **Acceptance:**
  - Each fact states its kind and Red conditions.
  - The N fact's mutation is run in a detached worktree and quoted; the F Unit fact is run on master and quoted.
  - The Docker facts are **CI-pending until C-2**.
  - Expected Unit: 1710 − 2 (TIDY-1-T) + 2 = 1710. Report the true count.

**TIDY-4 = TASK-14724 · `ux` (Sonnet).**
- **Rows:** 6a, 6b, 8b, 19, 23a, 23b, 24c, plus C-1 (a) if the owner rules it before dispatch.
- **Files:**
  - `frontend/src/pages/__tests__/{SkemaPage.specialHoliday,SkemaPage}.test.tsx`
  - `frontend/src/hooks/useSkema.ts`
  - `frontend/src/pages/SkemaPage.tsx`
  - `frontend/package.json`, `frontend/eslint.config.mjs`
  - `frontend/src/pages/admin/opfoelgning/{FollowUpLists,OpfoelgningPage}.tsx`
  - `frontend/src/pages/admin/opfoelgning/__tests__/{FollowUpLists.leaverActions,OpfoelgningPage}.test.tsx`
  - with C-1 (a): `frontend/src/lib/entitlementConstants.ts`, `frontend/src/pages/ArsoversigtPage.tsx` and their tests
- **Acceptance:**
  - `npx tsc --noEmit`, `npm run lint` (now covering `useHrFollowUp.ts`), `npm run typecheck:e2e`, `npm run build` and `npx vitest run` all green.
  - vitest grows by the 23b fact(s) from 83 files / 1074.
  - Never `src/**` or `tests/**`.

### Review per task (the Orchestrator rules; `PLAN-s147.md:92-110` is the checklist)

| Task | 5α | 5a internal (Fable) | 5a Codex | Note |
|---|---|---|---|---|
| TIDY-1 + 1-T | ✓, on the union. Checks each hunk is a comment, a deletion with an empty census, a same-value constant, the file move, or test prose; no `src/**` in the -T commits | ✓, one narrow cycle: "is each new sentence true at its file:line; is each census empty" | ✓, the same narrow prompt | **Lighter means one expected cycle and a narrow prompt, not fewer lenses.** Reviews are dual-lens (owner standing direction); the cap of 5 still applies. Step 7a reviews the whole diff again at C-3 |
| TIDY-2 | ✓ | ✓, narrow | ✓, narrow; payroll files | As above |
| TIDY-3 + 3-T | ✓ | ✓, full | ✓, full: legal rule logic (the classification), retroactive corrections (the reversal text) | The normal checklist |
| TIDY-4 | ✓ | ✓, full | Orchestrator's call | The plan's precedent ran no per-task Codex for frontend-only rows (14702, 14709); the dual-lens direction argues for it. Lean: run it, it is cheap |

### Order and merge plan

1. **Wave T1, in parallel.** TIDY-1 → TIDY-1-T on its branch; TIDY-2 from master.
   - The two tasks share no file: TIDY-1 touches Backend, Infrastructure, SharedKernel and RuleEngine; TIDY-2 touches Payroll and `init.sql`.
   - Merge the TIDY-1 pair (it must merge together: 15b's src deletion does not compile without the -T deletion), then GATE. Merge TIDY-2, then GATE + `python tools/check_docs.py`.
2. **Wave T2.** TIDY-3 → TIDY-3-T, from master **after T1**. They share `SkemaEndpoints.cs` with TIDY-1 (different hunks) and `DepartedSelfElevatedRouteTests.cs` with TIDY-1-T. Merge, then GATE.
3. **Wave T3.** TIDY-4. It may be dispatched in parallel with T1 (no shared file) but **merges after TIDY-3**, because row 8b's text depends on it. If TIDY-3 is cut, use 8b's "until then" text. Merge, then GATE + the frontend gate.
4. **Then C-1** (`PLAN-s147.md:425`). The new Docker facts (8a's F, 12d, 22c's F, 18a, 20d if ruled) are first seen green at **C-2**, on K. A red there is a fix and K′, per the plan.

### The gate (after every merge)

- Build: 0 errors, warnings ≤ 145, CA2100 ≤ 115 (144 / 114 at `94bd32be`; the pass should add none).
- Tests: Unit; DemoSeed (170); Regression `Category!=Docker` (135, unchanged: every Regression edit here is a Docker fact). All in the foreground with `-m:2`, exit status read from the unpiped command.
- Frontend gate after TIDY-4: tsc, lint, typecheck:e2e, build, vitest.
- **REGEN: not needed.** No route or DTO changes. Item 8's message is free text; 20d's 422 is anonymous and absent from `frontend/src/lib/api-types.ts`; 15a's enum is not served (RR-4).

### Cut order (if the pass must shrink before C-1)

1. The optional rows: 11d, 18a, 18b, X-3, X-4, X-5, and `AuditLogView.tsx` in 23a.
2. 20d. It becomes a SEC "revisit" row.
3. 23b. It goes to the ROADMAP backlog (UX).
4. TIDY-3 whole. Its three fixes go to S148 stage 0, and 8b takes the "until then" text.

**Not cut:** the comment corrections (TIDY-1, TIDY-1-T prose, TIDY-2). They are the pass's purpose. If time forces it, they move to S148 stage 0 as one task, never silently dropped.

---

## Needs a ruling

1. **C-1, item 7 wording (owner).** Lean (a). If unruled at TIDY-4's dispatch, it takes (c).
2. **C-2, the payout 422's end date (Orchestrator).** Lean (b).
3. **`init.sql:1250` comment (Orchestrator approval, CLAUDE.md "init.sql").** Comment only, in payroll-integration's section.
4. **Review weight (Orchestrator).** One narrow dual-lens cycle for the comment-only tasks, and whether TIDY-4 gets Codex.
5. **Two of the brief's (B) examples moved to (D) (Orchestrator to confirm).** Item 21's display read: two reads must agree, plus the stand-in rule. Item 12's four-eyes kind: a design fork in a payroll contract.
6. **Task ids TASK-14721 … 14724 (Orchestrator)**, for the ledger.

## Brief contradictions (claim · what the code shows · file:line)

- **BC-1.** Item 2, "25 N + 2 B". The file has 24 N, 2 B and 1 F: TASK-14715-T re-pinned case 3a as F (`FlexLedgerChainWalkTests.cs:132`). It holds 27 facts.
- **BC-2.** Item 4, "`SkemaMonthlyAccrualGuardTests`, which does not exist". It exists at `tests/StatsTid.Tests.Regression/Outbox/SkemaMonthlyAccrualGuardTests.cs:56` (since S60, `1e70874a`) and saves `SPECIAL_HOLIDAY_ALLOWANCE`. The stale part of `:245-246` is "no absence-code check at the save", false since TASK-14708 (`SkemaEndpoints.cs:965-974`).
- **BC-3.** Item 13, "use `ON_CALL` or `TRAVEL`", at `:229`. "TRAVEL" is not an activity: the travel rule matches `TRAVEL_WORK` and `TRAVEL_NON_WORK` (`TravelTimeRule.cs:37`). The line is `:228`.
- **BC-4.** Item 1, four sites. Two were already fixed by TASK-14715 (`PeriodCalculationService.cs:773-774`; `Program.cs:388-392`).
- **BC-5.** Item 8, "`SkemaPage.test.tsx:958`" and the reviewer's "three emitters". The comment is at `:1879-1885`; there are two emitters (`SkemaEndpoints.cs:1143`, `:2108`).
- **BC-6.** Item 21, "`:388-410`", given as a (B) example. The orphan roll-up is now `:398-436`, and the fix is not small (Class D, 21a).
- **BC-7.** Item 25's first list. `PeriodCalculationService.cs:1427` and the `PayrollExportService.cs` sites are already done (TASK-14715 and TASK-14716).
- **BC-8.** Item 23, "lint checks none of 14714's files". True, and wider: four more typed-call files sit outside the list, and "lint `src/**`" would not cover them, because the config's `files:` blocks decide (`eslint.config.mjs:174`, `:187`).
- **BC-9.** Item 15, "dispose the derived factory". The parent `StatsTidWebApplicationFactory` (no `Dispose` override, `StatsTidWebApplicationFactory.cs:58`) very probably already disposes the factories its `WithWebHostBuilder` creates. That framework behaviour is not visible in this repository, so it is stated as a belief. The explicit dispose is harmless and is kept.
- **BC-10.** Item 20, "`UserRepository.cs:~490`". The stale sentence is at `:520-522`; the R9c banner (`:255-266`) was already corrected by TASK-14710.

---

## Revision history

- **Draft 1, 2026-11-04.** The `planner` (Opus 5.5) drafted from `SPRINT-147.md:316-410` at `94bd32be`, re-opening every cited `file:line`. Contents: 42 Class A rows plus 5 extra finds; 6 Class B fixes plus 1 conditional; 2 Class C decisions; 10 Class D items plus 1; 7 Orchestrator edits; 7 already done or no action. Four tasks (two pairs and two singles) in three waves. Ten brief contradictions recorded.
