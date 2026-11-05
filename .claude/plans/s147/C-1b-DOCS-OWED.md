# C-1b — Step 7a cycle 1: the record fixes (for K′): apply-ready text

| | |
|---|---|
| **What this is** | The exact text for the record findings of S147's Step 7a, cycle 1: Fable's W-2, W-3, N-1 and N-3 (`.claude/reviews/SPRINT-147-step7a-reviewer-c1.md`), and Codex's WARNING 2 and its QUALITY.md NOTE (`.claude/reviews/SPRINT-147-step7a-codex-c1.md`), which agree with W-2 and N-1. The Orchestrator applies each block as a string replacement into the next candidate, K′. The `planner` drafts; the Orchestrator applies. Every target is under `docs/**` |
| **Status** | DRAFT 1 (`planner`, Opus 5.5), 2026-11-05 |
| **Base commit** | `c13024cd` (K, local master). Every Old text below is copied from `c13024cd`. A script counted each one as an exact substring over the whole target file, in the commit's blob and in the working copy, and each count is 1. Every line number is at that commit; so is every test file:line in Block 3 (`tests/` and `frontend/src` in the working copy equal K) |
| **Orchestrator model** | S147: Opus 5.5 seat; drafting `planner` (Opus 5.5); judgment `adjudicator` / `reviewer` on Fable 5.1 |
| **Timing** | Apply with the other K′ changes, before C-2 runs on K′. The angle-bracket placeholders stay literal, as in K; the Orchestrator fills them only after C-2 on the final candidate is green |
| **Owner approval** | None needed. No block touches `SYSTEM_TARGET.md` |

## What this is, in plain words

Step 7a's first review cycle on K found four things in the records to fix:
- **W-2 (both lenses): the log called the tidy pass's Docker tests "CI-verified" before CI had run them.** C-1 wrote that with a placeholder for the run id. A placeholder is not evidence: until the run lands, the honest word is "CI-pending". The plan says so too: the log flips a fact to CI-verified only once it records the run id (`PLAN-s147.md:418`). Four places in the sprint log and one in the sprint index now say "CI-pending (C-2)". The placeholders stay, so the Orchestrator can fill them in when C-2 on the final candidate is green.
- **W-3 (Fable): the tests exist, but often not under the names the plan promised.** The plan names, for each acceptance criterion, the test that proves it. Nineteen were built under another name, or as several tests. A reader following the plan's name cannot find them. One table in the sprint log now maps each one: the plan's name, the built name or names with file and line, and the kind.
- **N-1 (both lenses): QUALITY.md's "two different signers" overstates.** Payroll lets a month with no recorded sender pass the sender check. The text now uses SEC-054's wording.
- **N-3 (Fable): three counts from the plan's own review moved during the build, as intended, but the log never said to what.** The log's Step 0b record now gives the after values.

Not in this draft, as the brief says: W-1 (the day-6 cut-off; the owner ruled "fix it now", and TASK-14725 is being built) and W-4 (the Orchestrator commits the plan files and the two `.claude` edits).

## How to apply

- Each block names its **Target**, the **Old text** (one exact match at `c13024cd`) and the **New text**. Where a block has parts, apply each part as its own replacement.
- No two replacements in one file overlap. In `docs/sprints/INDEX.md`, Block 2 and X2 are disjoint substrings of line 116.
- **Line endings: every target is LF** (a carriage-return count of 0 in the blob and the working copy): `docs/sprints/SPRINT-147.md`, `docs/sprints/INDEX.md`, `docs/QUALITY.md`, `docs/references/tender-requirements-analysis.md`. Multi-line New texts (3, 5) are written with LF.
- **Placeholders** stay as in K: `<C-2 run id>`, `<C-2 Regression n>`, `<K sha>`. This draft adds none. When C-2 on the final candidate is green, the Orchestrator flips the W-2 places back to CI-verified with the real run id: 1a, 1b, 1c, 1e and Block 2.
- **Blocks 1–5 are owed.** X1 and X2 are optional, each with its lean.

## Map: each finding → its block

| Finding | What | Block |
|---|---|---|
| Fable W-1 = Codex WARNING 1 | The manager's day-6 cut-off is enforced by the switch-off job, not by the authorization rule | not drafted: owner ruling "fix it now" (2026-11-05), TASK-14725; its records follow its merge |
| Fable W-2 = Codex WARNING 2 | "CI-verified" written before C-2 exists | 1a (`SPRINT-147.md:103`), 1b (`:236`), 1c (`:453`), 1d (`:479`), 1e (`:481`), 2 (`INDEX.md:116`) |
| Fable W-3 | The acceptance pins exist, but not under the contract names, and there is no old → new record | 3 (one table, before `SPRINT-147.md:473`) |
| Fable W-4 | Governance files the log cites are untracked or modified after K | not drafted: the Orchestrator commits them |
| Fable N-1 = Codex NOTE 1 | `QUALITY.md:22` "two different signers" overstates | 4; X1 for the same phrase in the tender analysis |
| Fable N-2 = Codex NOTE 2 | The census re-run on K is clean (88 routes; 80 in the three hosts) | no action: confirms the C-1 records |
| Fable N-3 | Step 0b's counts moved; the post-build values are unrecorded | 5 (before `SPRINT-147.md:75`) |
| Fable N-4 … N-8 | Observations: lock order, model routing, the pins can fail, the records verified, later tasks' effects recorded | no action. N-5 (`S147-TASK-14715-5a-reviewer.md:1` reads "Fable (reviewer agent…)" rather than the model id) is an artifact under `.claude/reviews/`, outside this draft |

**Count: 9 owed replacements** (1a–1e; 2; 3; 4; 5) **and 2 optional ones** (X1, X2). **11 in all.**

---

## Block 1 — W-2 in the sprint log: "CI-pending (C-2)" until the run lands

**Target:** `docs/sprints/SPRINT-147.md` (lines 103, 236, 453, 479, 481). Five parts.

*Why:* C-1 wrote "CI-verified at C-2" with a placeholder run id, and the C-2 section stated the result before the run (Codex: "line 481 predeclares all seven jobs green, while line 509 still lists C-2 as next"). The plan flips a fact only after the run id is recorded (`PLAN-s147.md:418`). Each place now says CI-pending (C-2) and keeps its placeholder for the flip. Lines 101, 102 and 104 ("first CI run at C-2 (run `<C-2 run id>`)") claim no result and are left as they are.

### 1a — the TIDY-3 ledger row (line 103)

Old text:

````text
Docker facts **CI-verified at C-2** (run `<C-2 run id>`) |
````

New text:

````text
Docker facts **CI-pending (C-2)**; the run id `<C-2 run id>` is filled in when C-2 on the final candidate is green |
````

### 1b — the tidy pass's dispositions (line 236)

Old text:

````text
*(C-1: CI-verified at C-2, run `<C-2 run id>`.)*
````

New text:

````text
*(C-1: still CI-pending (C-2); the run id `<C-2 run id>` is filled in when C-2 on the final candidate is green.)*
````

### 1c — the C-1 record table (line 453)

Old text:

````text
| The tidy pass's Docker facts (the tidy-pass dispositions) | CI-verified at C-2, run `<C-2 run id>` |
````

New text:

````text
| The tidy pass's Docker facts (the tidy-pass dispositions) | CI-pending (C-2): the run id `<C-2 run id>` is filled in when C-2 on the final candidate is green |
````

### 1d — the C-2 section: which commit (line 479)

*Why:* Step 7a's fixes make a K′ (TASK-14725 and this draft), and C-2 must run on the candidate that is closed. A run on an earlier candidate is evidence for that candidate only.

Old text:

````text
| **Commit** | K = `<K sha>` (C-1, 2026-11-04) |
````

New text:

````text
| **Commit** | the final candidate: K = `<K sha>` (C-1, 2026-11-04), or the K′ that Step 7a's fixes make. A run on an earlier candidate is evidence for that candidate only |
````

### 1e — the C-2 section: the result (line 481)

*Why:* the row stated the result before the run, including counts that TASK-14725 will change.

Old text:

````text
| **Result** | all seven jobs green: Unit 1715, DemoSeed 170, Regression `<C-2 Regression n>`/`<C-2 Regression n>` (Docker included), frontend 84 files / 1089, E2E, smoke, docs |
````

New text:

````text
| **Result** | **CI-pending (C-2).** Recorded here only once C-2 on the final candidate is green, in the form the earlier checkpoints use: the seven jobs; Unit; DemoSeed; Regression `<C-2 Regression n>`/`<C-2 Regression n>` with the Docker facts; frontend files / tests; E2E; smoke; docs |
````

## Block 2 — W-2 in the sprint index: no "CI GREEN" before the run

**Target:** `docs/sprints/INDEX.md` (line 116, the S147 row; its Tests and Orchestrator Approved cells).

Old text:

````text
**<C-2 Regression n>r** (CI, all executed) + 7s + **1089fe** | ✅ CI GREEN `<C-2 run id>` (all 7 jobs, C-2 on K `<K sha>`; the close commit after it is docs only).
````

New text:

````text
**<C-2 Regression n>r** (CI-pending (C-2)) + 7s + **1089fe** | CI-pending (C-2): `<C-2 run id>` on `<K sha>`, filled in when C-2 on the final candidate is green (the close commit after it is docs only).
````

## Block 3 — W-3: one table from the plan's test names to the built ones

**Target:** `docs/sprints/SPRINT-147.md` (line 473, the heading "## C-2 — the checkpoint on K"; the table goes at the end of the C-1 section, just before it).

*Why here:* the C-1 section is where the close candidate's records live, and Step 7a reads it. *How the rows were found:* every test name in the plan's acceptance table (`PLAN-s147.md:330-391`) was looked up in `tests/` and `frontend/src` at `c13024cd`. A name not found was traced through the task's test file, its header and its AC references. *Counts:* the table has 62 rows. 32 kept their contract name (a contract `Class.*` counts as kept when the class exists). 11 name no test: a grep, a review, a CI run id, or existing or re-pinned facts. The 19 below changed.

Old text:

````text
## C-2 — the checkpoint on K
````

New text:

````text
### Acceptance → pin (contract name → built name)

*Recorded at C-1b (Step 7a cycle 1, Fable W-3).* The plan names, for each acceptance criterion, the test that proves it (`PLAN-s147.md:330-391`). Nineteen were built under another name, or became several tests; this table maps them, so a reader following the plan's name finds the test. Of the plan's 62 acceptance rows, 32 kept their contract name verbatim (a contract `Class.*` counts as kept when the class exists), and 11 name no test (a grep, a review, a CI run id, or existing or re-pinned facts). Paths: `Regression/…` is `tests/StatsTid.Tests.Regression/…`, `Unit/…` is `tests/StatsTid.Tests.Unit/…`; lines at `c13024cd`. Kind: the plan's kind, then the built one where it changed (F fails on the code before the change, N needs the new API, B boundary pin that holds today, R regression pin).

| AC | Contract name (the plan) | Built name(s), file:line | Kind |
|---|---|---|---|
| AC-053-1 | `SenderSeparationTests.Sender_Approves_403_AnotherHr_200` | `AC053_1_SenderApprove_Refused403_AnotherHrUser_Approves200` (`Regression/Approval/SenderSeparationTests.cs:61`) | F (CI) |
| AC-053-2 | `…Sender_Rejects_403`, `…Sender_ReopensEmployeeApproved_200`, `…LegacySubmittedBySender_Approves_403`, `…Sender_OnApprovedMonth_403_Not409` | In `SenderSeparationTests.cs`: `AC053_2_SenderReject_Refused403_AnotherHrUser_Rejects200` (`:104`), `AC053_2_SenderReopen_OfEmployeeApprovedMonth_Allowed200_ClearsBothStamps` (`:134`), `AC053_2_LegacyRow_OnlySubmittedBy_SenderApprove_Refused403_OtherHr200` (`:161`), `AC053_2_SenderOnApprovedMonth_Approve_And_Reject_Return403_Not409` (`:190`) | F; the reopen R |
| AC-053-3 | `SenderSeparationRaceTests.ForcedReopenAndResend_NeverEndsApprovedBySender` | Two facts: `AC053_3_ReopenAndResendWhileApproveIsParked_NeverEndsApprovedBySender` (`Regression/Approval/SenderSeparationRaceTests.cs:53`), `AC053_3_ReopenAndResendWhileRejectIsParked_NeverEndsRejectedBySender` (`:80`) | F (CI) |
| AC-056-2 | `…SelfOnNonPending_403_Not400`, `…SelfDenied_LogsWarning_NoAuditRow` | One fact: `SelfDecision_OnNonPendingRow_Is403Not400_LogsWarning_JournalsTwoRefusals_WritesNoDecisionTrail` (`Regression/Overtime/OvertimeSelfDecisionTests.cs:312`). "No audit row" became "two refusals journalled, no decision trail", because `audit_log` is the HTTP journal (Fable's c1 BLOCKER on TASK-14705) | F (CI) |
| AC-056-3 | `…CreateForSelf_StillAllowed` | `Create_ForOneself_StillSucceeds_RowIsPending` (`OvertimeSelfDecisionTests.cs:371`) | R (CI) |
| AC-231-2 | `AbsenceClassificationTests.Reflection_EqualsAcceptedSet_OneClassificationPerCode` | Four facts in `Unit/Rules/AbsenceClassificationTests.cs`: `AbsenceTypes_ReflectionEqualsTheAcceptedStoredVocabulary` (`:56`), `EveryVocabularyCode_HasExactlyOneClassification` (`:76`), `SickDay_IsClassifiedCredit` (`:97`), `EveryCode_HasTheSpecifiedEffect_AndTheEngineReadsTheSameTable` (`:128`) | N (M-7) → `:56` F, the others N (`:97`, `:128` with M-7) |
| AC-231-3 | `SkemaSaveUnknownAbsenceCodeTests.UnknownCode_Returns422` | `UnknownAbsenceCode_IsRefused422_NamesTheCode_NoDate_NothingWritten` (`Regression/Skema/SkemaSaveUnknownAbsenceCodeTests.cs:83`), `UnknownAbsenceCode_AmongValidOnes_RefusesTheWholeSave` (`:111`), and a control, `KnownAbsenceCode_SickDay_IsAccepted_Control` (`:129`) | "F or R, reported" → F ×2, the control B |
| AC-231-6 | `AbsenceLinesGoldenTests.AbsenceLines_UnchangedByVocabularyChange` | A Unit theory over every stored code: `AbsenceClassificationTests.AbsenceLine_ForEveryStoredCode_IsTheCodeItself_GoldenLine` (`Unit/Rules/AbsenceClassificationTests.cs:185`) | R |
| AC-231-7 | `FlexCapCrossingTests.ExtraCredit_CrossesCap_PayoutLineAppears_OnExport`, `…_OnCorrection` | `Export_StoredSickDayCreditCrossesTheFlexCap_PaysTheExcessAsFlexPayout` (`Regression/Payroll/FlexCapCrossingTests.cs:57`), `Correction_StoredSickDayCreditCrossesTheFlexCap_AddsTheFlexPayoutLine` (`:94`) | F (CI) |
| AC-230-1 | `LeaverGraceTests.ManagerOnDPlus5_Approves_Rejects_Reopens_200` | `OnDay5_StepAKeepsMetteActive_ManagerApproveRejectReopen_200` (`Regression/Leaver/LeaverGraceTests.cs:85`; a theory over approve, reject and reopen) | F (CI) |
| AC-230-2 | `LeaverHrOnlyTests.DPlus6_Manager403_Hr200_PayrollExports` | Three facts in `Regression/Leaver/LeaverHrOnlyTests.cs`: `OnDay6_StepAHasSwitchedMetteOff_ManagerApprove_Is403_WithTheBelowHrReason_MonthUnchanged` (`:122`), `OnDay6_StepAHasSwitchedMetteOff_HrApprove_Is200_ApprovedByHr` (`:154`), `OnDay6_StepAHasSwitchedMetteOff_LocalAdminExport_Is200_WithTheLedgerRecord` (`:191`) | F (CI) |
| AC-230-3 | `GraceConstantTests.*`, `SettlementCloseGraceTests.*`, `EndDateLifecycleGraceTests.*` | **The due predicate and the one constant:** `Unit/Settlement/SettlementCloseLeaverLogicTests.cs`, `IsDeactivationDue_BoundaryDay_NotDue_FirstFollowingDay_Due` (`:41`; M-8a, M-8b), `IsDeactivationDue_LastGraceDay_NotDue` (`:54`), `IsDeactivationDue_AgreesWithTheWritersHelper_AndTheConstantIsFive` (`:73`). **The poll:** `Regression/Settlement/SettlementCloseLeaverTests.cs`, `Exclusions_ManualInactive_NullEndDate_BoundaryDay_NotFlipped_DayAfter_Flipped` (`:171`); `Regression/Leaver/LeaverGraceTests.cs`, `StepA_Day1ThroughDay5_LeavesTheLeaverActive_Day6Flips` (`:124`). **The writer, 5 and 6 days back:** `Unit/Settlement/EndDateLifecycleWriterParityTests.cs`, `WriterWithinGrace_StoresOnly_ActiveNoProvenance` (`:84`), `WriterPastGrace_Deactivates_WithProvenance` (`:93`); `Unit/Endpoints/EmploymentEndDateLifecycleLogicTests.cs`, `R1_Boundary_EndDateToday_NoFlip_EndDateYesterday_Flips` (`:34`); `Regression/Settlement/EndDateLifecycleWriterEffectTests.cs`, `EndDateWithinGrace_StoresOnly_PersonStaysActive_NoDeactivationEffects` (`:234`). **A correction into the grace:** `WriterCorrectionIntoGrace_ReactivatesLifecycleDeactivatedRow` (`EndDateLifecycleWriterParityTests.cs:102`), `CorrectionIntoGrace_ReactivatesLifecycleDeactivatedRow` (`EndDateLifecycleWriterEffectTests.cs:264`). Three of these names still describe the rule before OQ-2 (`…FirstFollowingDay_Due`, `…DayAfter_Flipped`, `…EndDateYesterday_Flips`); their bodies and summaries pin the new rule, that day 1 after the last working day is not due and day 6 is | N (M-8) · F (CI) |
| AC-230-8 | `LeaverHrOnlyTests.ApprovedInGrace_ExportsAfterDPlus6` | `MonthApprovedByTheManagerInTheGrace_IsExportedAfterTheDay6SwitchOff` (`Regression/Leaver/LeaverGraceExportTests.cs:37`) | F (CI) |
| AC-230-9 | `LeaverHrScreenPathTests.*` and `FollowUpLists.leaverActions.test.tsx` | `Regression/Leaver/LeaverHrFollowUpWindowTests.cs`: `OnDay6_HrOpensMettesSkemaMonth_200_AndMetteHerselfIsStillRefused` (`:56`), `OnDay6_HrLeaverList_RowsCarryTheManagersWindow_AnchorUnchanged` (`:95`), `OnDay6_OtherLists_CarryTheWindowMembers_AsNull` (`:140`). The vitest file kept its name (`frontend/src/pages/admin/opfoelgning/__tests__/FollowUpLists.leaverActions.test.tsx`) | F (CI) + F (vitest) |
| AC-230-10 | `LeaverOrphanFlowTests.*` and a vitest | `OrphanLeaver_HrASends_HrAApprove403FourEyes_HrBApproves200_MonthExports` (`Regression/Leaver/LeaverOrphanSendApproveTests.cs:37`); the screen half is in `FollowUpLists.leaverActions.test.tsx` (its header `:1`; the worded four-eyes refusal `:109`) | F |
| AC-230-11 | `LeaverFourEyesTests.*` | `Regression/Leaver/LeaverFourEyesAcrossGraceTests.cs`: `InGrace_HrSendsOnDay2_ManagerApprovesOnDay3_200` (`:34`), `AfterGrace_HrASends_HrAApproveAndReject403_HrBApproves200` (`:63`) | F (CI) |
| AC-230-13 | `LeaverRecalculateCharacterizationTests.OtherAdmin_RecalculatesDeactivatedLeaverMonth_Works` | `AnotherGlobalAdmin_CanRecalculate_ASwitchedOffLeaversExportedMonth` (`Regression/Leaver/LeaverRecalculateGuardTests.cs:37`) | R (CI) |
| AC-230-14 | `SettlementCloseGraceTests.SpecialHolidayActivePass_SkipsGraceLeaver`; the K-2 re-pin | K-3: `K3_GraceLeaver_GetsNoSpecialHolidaySettlementRow` (`Regression/Leaver/LeaverGraceTests.cs:223`). K-2: `K2_TerminationSettlement_IsDueFromDay6_NotDay1` (`:250`) | F · R → K-3 B (relabelled: it holds today through the in-lock guard; TASK-14710-T fix-up 2, `bd1fbe3e`), K-2 F |
| AC-230-17 | `RecalculateDepartedSelfTests.DepartedGlobalAdmin_OwnMonth_403_NothingWritten`, `…_DeactivatedVariant_403`, `…_OtherEmployee_AsToday`, `…DepartedGlobalAdmin_OwnMonth_AlreadyDeliveredToken_Still403` | In `Regression/Payroll/RecalculateDepartedSelfTests.cs`: `DepartedGlobalAdmin_OwnMonth_Recalculate_403_NothingWritten` (`:101`), `DeactivatedGlobalAdmin_OwnMonth_Recalculate_403_NothingWritten` (`:122`), `DepartedGlobalAdmin_OtherEmployeesMonth_AsToday_200` (`:166`); the fourth kept its name (`:145`); and one more, `ActiveGlobalAdmin_NoEndDate_OwnMonth_AsToday_200` (`:188`) | F (CI) → F ×3 (`:101`, `:122`, `:145`), B ×2 (`:166`, `:188`) |

## C-2 — the checkpoint on K
````

## Block 4 — N-1: `QUALITY.md`, "two different signers"

**Target:** `docs/QUALITY.md` (line 22, the S147 section's Auditability row).

*Why:* payroll's sender check lets a month with no recorded sender pass. A null or empty id never matches (`src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollExportService.cs:555-557`), and a null sender is pinned as passing (`PayrollSenderSeparationHostTests.cs:143`, Codex). The new text reuses SEC-054's closing wording (`security-finding-register.md:214`). The S147 section is K's own draft snapshot, so correcting it before the close does not rewrite an earlier sprint's record.

Old text:

````text
Every first export since TASK-14716 comes from an approved month with two different signers, re-asserted under the row lock, and names its period.
````

New text:

````text
Every first export since TASK-14716 comes from an approved month, approved by someone other than the employee and, where one is recorded, the sender; the approval is re-asserted under the row lock, and the export names its period.
````

## Block 5 — N-3: the Step 0b counts after the build

**Target:** `docs/sprints/SPRINT-147.md` (line 75, the Step 0b record's "Artifacts:" line; the note goes just before it, after the cycle table).

*Why here:* these are the counts Step 0b verified at draft time (`PLAN-s147.md:439`, item 4; `:444`, item 8). Each was re-measured at `c13024cd` against the plan's base `02ef91f1`. The P-12 census is item 4, not item 8; Fable's N-3 lists it with item 8's.

Old text:

````text
Artifacts: `.claude/reviews/PLAN-s147-{codex,reviewer}-c{1,2,3}.md`, `PLAN-s147-codex-c{1,2,3}.log`, `PLAN-s147-adjudication-c{1,2}.md`, `RESEARCH-s147-adjudication.md`.
````

New text:

````text
*Step 0b's counts after the build* (recorded at C-1b, 2026-11-05, from Step 7a cycle 1, Fable N-3; re-measured at K = `c13024cd` against the plan's base `02ef91f1`). Step 0b checked these counts at draft time (`PLAN-s147.md:439`, `:444`). Three moved during the build, as the plan intended:
- **`new OrgScopeValidator(` in `tests/`: 7 → 8.** The eighth is `DepartedSelfValidatorTests` (TASK-14703-T, AC-230-16).
- **`weekly-timeline-v1` in `src/` and `tests/`: 14 files → 7 mentions in 6 test files, none in `src/`.** TASK-14708 moved the marker to `weekly-timeline-v2`. What remains: one replay fact that uses v1 as the recorded marker, so the replay is refused (`ReplayFrozenInputsTests.cs:389`); three shape tests that use it as a sample value; and three doc comments.
- **The P-12 census (item 4: `AdjudicatedAccepted`, "take it back to the owner" in `tests/`): 4 hits in 2 files → 2 hits, both doc comments** naming the two S136 pins' old names. The pins themselves were renamed `…_Is403_ReDecidedS147` (`SendCommandTerminatedSubjectTests.cs:309`, `TerminatedEmployeeAccessTests.cs:653`).

Item 8's other counts (16 `Derive(` and 3 `WindowsReadRange` calls in tests; 6 + 3 tree-lock sites) were not re-measured: the plan gives no command for them, and the obvious patterns do not reproduce the draft-time numbers.

Artifacts: `.claude/reviews/PLAN-s147-{codex,reviewer}-c{1,2,3}.md`, `PLAN-s147-codex-c{1,2,3}.log`, `PLAN-s147-adjudication-c{1,2}.md`, `RESEARCH-s147-adjudication.md`.
````

---

## Beyond the owed list (optional; each with a lean)

### X1 — N-1's overstatement in the tender analysis (VD 35) · lean: apply

**Why:** C-1's note on VD 35 says "Two different people now sign every month … and payroll re-checks it on every export". The same exception applies: payroll's check passes a month with no recorded sender. One clause keeps the two documents saying the same thing.

**Target:** `docs/references/tender-requirements-analysis.md` (line 412).

Old text:

````text
Two different people now sign every month: the sender can neither approve nor reject it (SEC-053 CLOSED), and payroll re-checks it on every export,
````

New text:

````text
Two different people now sign every month sent through the product: the sender can neither approve nor reject it (SEC-053 CLOSED), and payroll re-checks it on every export, against the sender where one is recorded,
````

### X2 — The sprint index's status cell · lean: apply

**Why:** W-2's rule applied to the status: the S147 row says "complete" while the sprint is a close candidate. The close commit sets "complete".

**Target:** `docs/sprints/INDEX.md` (line 116).

Old text:

````text
| complete | 2026-10-27 → <close date> |
````

New text:

````text
| close candidate (complete at the close) | 2026-10-27 → <close date> |
````

---

## Not drafted here

- **Lines 101, 102 and 104 of the sprint log** ("first CI run at C-2 (run `<C-2 run id>`)"): they name the run without claiming a result, so W-2 does not apply.
- **The Step 7a section's cycle-1 record** (the verdicts, W-1's ruling, TASK-14725): the Orchestrator's, cycle by cycle, before acting. When TASK-14725 is written into the log, it needs its own ledger row with a disposition, or the close guard refuses the close: every TASK id the log names must have one (`.claude/hooks/sprint-close-guard.ps1:684`).
- **The handoff rows** (Phase "Next: C-2 on K, then Step 7a (C-3)"; Next action): now one step behind, and the Orchestrator's at the next gate.
- **Three test names that describe the rule before OQ-2** (Block 3, AC-230-3): `IsDeactivationDue_BoundaryDay_NotDue_FirstFollowingDay_Due` (`Unit/Settlement/SettlementCloseLeaverLogicTests.cs:41`), `Exclusions_ManualInactive_NullEndDate_BoundaryDay_NotFlipped_DayAfter_Flipped` (`Regression/Settlement/SettlementCloseLeaverTests.cs:171`) and `R1_Boundary_EndDateToday_NoFlip_EndDateYesterday_Flips` (`Unit/Endpoints/EmploymentEndDateLifecycleLogicTests.cs:34`). The last one's name says the opposite of what it asserts. Renaming them is a `tests/` change: a tidy candidate, recorded in Block 3.
- **Fable's N-5:** an artifact header under `.claude/reviews/`.

## Brief contradictions, deviations and open points

**Claims about the existing code (checked; the code governs):**
1. **"14 `weekly-timeline-v1` files"** (`PLAN-s147.md:444`) reproduces only when counted over `src/` and `tests/` (14 at `02ef91f1`). Over the whole tree it is 21 files. Block 5 says which scope it uses.
2. **The P-12 census is item 4 of Step 0b** (`PLAN-s147.md:439`), not item 8. Fable's N-3 lists it with item 8's counts. Block 5 records it with them and names the item.
3. **Fable's AC examples were all confirmed, and three more were found:** AC-053-2 (four facts renamed), AC-053-3 (one fact became two) and AC-230-1 (renamed). AC-230-3's contract classes do not exist; the facts are in five files across the Unit and Regression projects (Block 3 lists them). Fable cited `SettlementCloseLeaverLogicTests (:46-80)`; the facts are at `:41`, `:54`, `:73`.
4. **Kinds that changed:** AC-230-14's K-3 was relabelled B; two of AC-230-17's "as today" facts are B; AC-231-2's reflection fact is F. Each is from the test's own summary, and Block 3 gives the built kind beside the plan's.

**Claims about required behaviour:** none contradicted.

**Needs a ruling (the Orchestrator's):**
- **R-1, whether X1 applies.** Lean: yes, because it carries N-1's correction to the one other place that says "two different people … sign every month".
- **R-2, the index row's status (X2).** Lean: "close candidate" until the close commit, by W-2's rule. If the Orchestrator reads "complete" as the build being complete, X2 is skipped.

## Revision history

| Draft | Date | By | What |
|---|---|---|---|
| 1 | 2026-11-05 | `planner` (Opus 5.5) | First draft at `c13024cd`, from Step 7a cycle 1 (Fable APPROVED-WITH-WARNINGS; Codex 2 W, 2 N). Blocks 1–5, 9 owed replacements: W-2 in five places in the sprint log and one in the index; W-3's table (19 rows; 32 contract names kept, 11 rows name no test); N-1 in `QUALITY.md`; N-3's post-build counts, re-measured. X1 (N-1 in the tender analysis) and X2 (the index status). W-1 and W-4 not drafted, as briefed |
