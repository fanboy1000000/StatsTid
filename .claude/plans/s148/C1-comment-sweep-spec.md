# S148 close, plan step C-1: the comment-only sweep, exact specification

| | |
|---|---|
| **Sprint** | S148 (close, plan step C-1) |
| **Drafted by** | Planning Drafter (Opus); the Orchestrator rules, the `sweep` agent applies |
| **File** | `.claude/plans/s148/C1-comment-sweep-spec.md`. The brief asked for `.claude/reviews/S148-C1-comment-sweep-spec.md`; the drafter may write only under `.claude/plans/**` and `.claude/refinements/**`, so it is here, ready to be copied |
| **Base commit** | code verified at `7b19f9b4` and re-checked at `6ab643a7` (that commit touched only `docs/sprints/SPRINT-148.md`); every line number below is at `6ab643a7` |
| **Sources** | `docs/sprints/SPRINT-148.md` (line numbers at `6ab643a7`; the Orchestrator's C-1 ledger commit moved the brief's numbers by +61), `.claude/reviews/S148-wave1-briefs-adjudication.md` (A9), `.claude/plans/s148/TASK-14802.md` (census (b)), `.claude/reviews/S148-TASK-14802-report.md:38`, `.claude/reviews/S148-C1-audit-report.md` § 4, and the grep the brief named over the sprint log and `.claude/reviews/S148-*.md` |

## What this is, in plain words

A code comment is the one piece of project knowledge an agent is sure to read, because it sits in the file being changed. When the code changes and the comment does not, the next reader is told something false. PAT-032 (the S148 trial pattern "a comment says what is true now, and why") turns that into a rule, and adjudication A9 applied it to wave 1: retiring `POST /api/time-entries` (TASK-14802) left comments all over the backend saying that route still exists, still shares a lock, still runs a check. They were not to be left as "found, not fixed": the Orchestrator owes a comment-only sweep before close.

This document is that sweep, made exact so a cheap agent can apply it without judgement. It changes no behaviour: every edit is inside a comment, nothing that runs, builds or is generated moves. Where a fix would change a test name, a golden or a generated file, the item is excluded and the reason is given.

PAT-032 also says history is not rewritten in a bulk sweep. So these edits are deliberately narrow: they correct the false sentence and keep the reason around it. Where a comment records what S136 or S128 did, the history stays and gains "(retired in S148)" rather than being rewritten.

## Counts

**The log's 23** (`SPRINT-148.md:596`: the adjudication's eleven, the two A9 items, the ten the census pattern missed). Each was re-found at HEAD by its anchor text; the cites at the census base `0ed6f150` were checked first (all 23 matched).

| Status | Count | Items |
|---|---|---|
| Still stale (apply) | **21** | blocks S-1 to S-7 and S-9 to S-24 (23 blocks: S-10 and S-11 are two more sentences in `EmploymentWindowGate.cs` making the same false claim as its `:10`) |
| Already fixed | **2** | `SkemaEndpoints.cs:797` and `:804` (the two `RequestValidator` mentions), rewritten by `af247ca7` (TASK-14840) |
| Gone | 0 | |
| Excluded | 0 | |

**Everything else the records send to the sweep, "the next touch" or the close** (the brief's list plus every grep hit). Counted by finding, not by block.

| Status | Count | Items |
|---|---|---|
| Still stale (apply) | **14** | the 14804 `:2137-2138` comment (S-8); N-3 (S-25, S-26); N-1 (S-27, S-28); the DemoSeed pointers (S-29 to S-31); the bounds docstring (S-32); tidiness items 1 and 2 (S-33; S-34 and S-35); the 14811 child's-sick stub comment (S-36); the two 14802-T comments (S-37; S-38 and S-39); the 14815 round-trip comment (S-40); the 14818 arm list (S-41a-c); the 14823 lock sentence (S-42); the 14840 backticks (S-43a-b) |
| Already fixed | **18** | see "Not applied" § A (its first two rows are the two of the 23) |
| Gone | 0 | |
| Excluded: needs regeneration or is a test name | **2** | the `SqlEmitter.cs` emitted comments; the phase-pin test title (X-1) |
| Excluded: not a comment change | **5** | see § C |
| Conditional on a doc owed at close | **1** | tidiness item 3, `SendCheckIntervalOnlyTests.cs:17` (X-2) |
| Not stale (the finding's premise does not hold) | **2** | `PaidAbsenceDayBoundRaceTests.cs:77`; `ComplianceEndpoints.cs:247` |
| Candidate found on verification (needs a ruling) | **1** | `TeamOverviewAggregateTests.cs:552` (C-1) |

Total blocks to apply: **46** (S-1 to S-43b), in **26 files**.

## How to apply (for the sweep agent)

- Apply the blocks in order, one `Edit` per block: `old_str` is the text between its fences, exclusive; `new_str` likewise. Indentation and trailing text are exact.
- Each `old_str` occurs exactly once in its file at `6ab643a7`, and still exactly once when the blocks before it in the same file have been applied (checked by script, below). If a count is not 1, stop and report; do not widen or guess.
- Line endings: every file is CRLF in the working tree except `src/Backend/StatsTid.Backend.Api/ApprovalPeriodSaveLock.cs` and `src/Backend/StatsTid.Backend.Api/Services/EmploymentWindowGate.cs`, which are LF (`git ls-files --eol`: `i/lf w/crlf` for the rest; none has a byte-order mark). The blocks are written with plain line breaks; the `Edit` tool matches across CRLF and keeps the file's endings. If you apply by script, convert `\n` to `\r\n` for the CRLF files first.
- Change nothing else: no reflow of neighbouring lines, no added `shortcut(...)` tag, no other "while I'm here" edit.
- `docker/postgres/init.sql` (S-24) is a protected file. Its comment-only edit is authorized by the Orchestrator's ruling 1 (`SPRINT-148.md:402`); apply it only because of that ruling.

**Verification method used for this spec.** A script (scratchpad `c1sweep/check.js`) read each file at `6ab643a7`, took each `old_str` from the cited line range, confirmed its anchor text, counted its occurrences in the line-ending-normalised file (1 for every block), applied the blocks in sequence and re-counted (1 for every block), and confirmed that every changed line is a comment line (S-33 inserts a comment line above an unchanged declaration; checked by hand).

## Blocks to apply

### S-1 · `src/Backend/StatsTid.Backend.Api/ApprovalPeriodSaveLock.cs` · class summary, "so POST /api/time-entries can enforce"

status: still stale
lines at `6ab643a7`: `:6-15` · line endings: LF
source: the eleven: `ApprovalPeriodSaveLock.cs:8` (and the same summary's `:14-15`, found on verification)
why: The summary says the retired POST "can enforce" this lock and "asks once"; the route was removed by TASK-14802 (`e2864079`; `TimeEndpoints.cs:15` and `:73` are the only routes left there), and the Skema save is now the only caller (`SkemaEndpoints.cs:871-872`, `:2631-2634`; `git grep IsPeriodLockedForSave -- src`).

old_str (exact, unique in the file):
```text
/// S128 / TASK-12803 — THE single spelling of "this month is closed to registration writes", lifted
/// VERBATIM out of <c>SkemaEndpoints</c> (where S127/TASK-12704 minted it as two private statics)
/// so <c>POST /api/time-entries</c> can enforce the SAME lock without a hand-copied status list.
///
/// <para>Precedent: <see cref="ApprovalVisibility"/> — the S124/TASK-12405 read-side lift that exists
/// because two hand-copied status literals bound only by a comment are a drift hazard. This is the
/// write-side sibling. PAT-015's checklist item ("the in-lock authoritative check shares ONE predicate
/// and ONE response-construction site with the fast path") now spans processes-of-asking in TWO
/// endpoints: Skema's save asks twice (pre-tx fast path + in-lock authoritative), and the time-entry
/// POST asks once (in-lock only). All askings route through here.</para>
```

new_str:
```text
/// S128 / TASK-12803 — THE single spelling of "this month is closed to registration writes", lifted
/// VERBATIM out of <c>SkemaEndpoints</c> (where S127/TASK-12704 minted it as two private statics)
/// so <c>POST /api/time-entries</c> (retired in S148) could enforce the SAME lock without a
/// hand-copied status list.
///
/// <para>Precedent: <see cref="ApprovalVisibility"/> — the S124/TASK-12405 read-side lift that exists
/// because two hand-copied status literals bound only by a comment are a drift hazard. This is the
/// write-side sibling. PAT-015's checklist item ("the in-lock authoritative check shares ONE predicate
/// and ONE response-construction site with the fast path") holds for Skema's save, which asks twice
/// (pre-tx fast path + in-lock authoritative) and is the only caller since the time-entry POST
/// retired. All askings route through here.</para>
```

### S-2 · `src/Backend/StatsTid.Backend.Api/ApprovalPeriodSaveLock.cs` · `PeriodLockedForSaveConflict` summary

status: still stale
lines at `6ab643a7`: `:33-35` · line endings: LF
source: the ten more: `ApprovalPeriodSaveLock.cs:34`
why: The 409 is built for two askings now, both in the Skema save (`SkemaEndpoints.cs:871-872`, `:2631-2634`); "the time-entry POST's in-lock check" no longer exists.

old_str (exact, unique in the file):
```text
    /// S127 / TASK-12704 — THE single construction site for the period-locked 409, so every asking
    /// (Skema's pre-transaction fast path, Skema's in-lock re-read, and the time-entry POST's in-lock
    /// check) returns byte-identically the same status code, body shape and message. Callers must
```

new_str:
```text
    /// S127 / TASK-12704 — THE single construction site for the period-locked 409, so every asking
    /// (Skema's pre-transaction fast path and Skema's in-lock re-read) returns byte-identically the
    /// same status code, body shape and message. Callers must
```

### S-3 · `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` · "S128 / TASK-12803 — the S127/TASK-12704 IsPeriodLockedForSave" block

status: still stale
lines at `6ab643a7`: `:151-153` · line endings: CRLF
source: the eleven: `SkemaEndpoints.cs:135`
why: It says the retired POST "enforces the SAME lock"; the route is gone (`TimeEndpoints.cs:15-62` is the GET) and the two call sites are this file's own (`:871`, `:2631`).

old_str (exact, unique in the file):
```text
    // POST /api/time-entries enforces the SAME lock without a hand-copied status list. The two
    // call sites below (pre-transaction fast path + in-lock authoritative re-read) are unchanged
    // in behavior — same predicate body, same 409 body, byte-identical wire.
```

new_str:
```text
    // POST /api/time-entries (retired in S148) could enforce the SAME lock without a hand-copied
    // status list. The two call sites below (pre-transaction fast path + in-lock authoritative
    // re-read) are unchanged in behavior — same predicate body, same 409 body, byte-identical wire.
```

### S-4 · `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` · "Surface 3 of the S70 R9c allowlist extension"

status: still stale
lines at `6ab643a7`: `:767` · line endings: CRLF
source: the eleven: `SkemaEndpoints.cs:688`
why: "surfaces 1+2 are on POST /api/time-entries" is present tense about a retired route (`TimeEndpoints.cs:37`: "The Skema save, the one route that registers time").

old_str (exact, unique in the file):
```text
                // Security-invariant review; surfaces 1+2 are on POST /api/time-entries). The shared
```

new_str:
```text
                // Security-invariant review; surfaces 1+2 were on POST /api/time-entries, retired
                // in S148). The shared
```

### S-5 · `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` · "THE EMPLOYMENT-WINDOW GATE … IN-LOCK" block

status: still stale
lines at `6ab643a7`: `:2549-2550` · line endings: CRLF
source: the eleven: `SkemaEndpoints.cs:1869`
why: `EmploymentWindowGate` is shared with no other writer: its only calls are this file's `:2598` and `:2603`.

old_str (exact, unique in the file):
```text
                    // the shared EmploymentWindowGate (ONE predicate + ONE 422 construction site with
                    // POST /api/time-entries — see that class for the do-not-add-date-fields rule).
```

new_str:
```text
                    // the shared EmploymentWindowGate (ONE predicate + ONE 422 construction site; see
                    // that class for the do-not-add-date-fields rule).
```

### S-6 · `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` · "IN-TX ORDER RULE"

status: still stale
lines at `6ab643a7`: `:2559-2560` · line endings: CRLF
source: the ten more: `SkemaEndpoints.cs:1878`
why: The order is no longer "shared verbatim with the time-entry POST so two writers can never deadlock" (that writer is gone, `TimeEndpoints.cs:37`); the order itself stands (lock `:2487`, subject re-check `:2531`, window gate `:2594-2605`, approval re-read `:2629-2635`).

old_str (exact, unique in the file):
```text
                    // IN-TX ORDER RULE (fixed total order, shared verbatim with the time-entry POST
                    // so two writers can never deadlock): EmployeeConsumptionLock acquisition FIRST,
```

new_str:
```text
                    // IN-TX ORDER RULE (fixed total order): EmployeeConsumptionLock acquisition FIRST,
```

### S-7 · `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` · "S136 Step-5a truth-up (Codex WARNING)"

status: still stale
lines at `6ab643a7`: `:2567` · line endings: CRLF
source: the ten more: `SkemaEndpoints.cs:1886`
why: "(unlike the time-entry POST)" contrasts with a retired route; the pre-transaction fast path it describes is `:870-872`.

old_str (exact, unique in the file):
```text
                    // tx. This endpoint (unlike the time-entry POST) also runs a PRE-TX month-lock
```

new_str:
```text
                    // tx. This endpoint also runs a PRE-TX month-lock
```

### S-8 · `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` · "S66 / TASK-6603 FIX-FORWARD — in-lock authoritative re-checks"

status: still stale
lines at `6ab643a7`: `:2699-2701` · line endings: CRLF
source: TASK-14804 report :126 (log :441), the comment its report called `:2137-2138` at `cf0d4aef`
why: `:552` and `:829` are S66-era line numbers (`ef81fd05`); the guards are now the "ADR-032 D3 per-day norm cap" block (`SkemaEndpoints.cs:1190`) and the "dated part-time fraction at the per-type batch anchor" block with its profile-missing 422 (`:1722`, `:1773-1779`).

old_str (exact, unique in the file):
```text
                    // The pre-tx guards (the D3 norm cap at :552 and the anchor profile-missing 422 at
                    // :829) ran BEFORE the advisory lock against a possibly-stale norm. A profile PUT
                    // (TASK-6604) that committed between those checks and THIS recompute lowered or
```

new_str:
```text
                    // The pre-tx guards (the D3 per-day norm cap and the per-type batch anchor's
                    // profile-missing 422, both further up) ran BEFORE the advisory lock against a
                    // possibly-stale norm. A profile PUT (TASK-6604) that committed between those
                    // checks and THIS recompute lowered or
```

### S-9 · `src/Backend/StatsTid.Backend.Api/Services/EmploymentWindowGate.cs` · class summary, "shared by BOTH registration writers"

status: still stale
lines at `6ab643a7`: `:8-12` · line endings: LF
source: the eleven: `Services/EmploymentWindowGate.cs:10`
why: The gate is no longer shared by two writers: its only caller is the Skema save (`SkemaEndpoints.cs:2598`, `:2603`).

old_str (exact, unique in the file):
```text
/// copying the <see cref="StatsTid.Backend.Api.ApprovalPeriodSaveLock"/> shape exactly: ONE
/// predicate + ONE response-construction site, shared by BOTH registration writers
/// (<c>POST /api/time-entries</c> and <c>POST /api/skema/{employeeId}/save</c>) so the refusal
/// can never drift between them — the same anti-drift rationale that lifted the approval-period
/// predicate out of <c>SkemaEndpoints</c> in S128 (and <c>ApprovalVisibility</c> before it).
```

new_str:
```text
/// copying the <see cref="StatsTid.Backend.Api.ApprovalPeriodSaveLock"/> shape exactly: ONE
/// predicate + ONE response-construction site, written for BOTH registration writers
/// (<c>POST /api/time-entries</c>, retired in S148, and <c>POST /api/skema/{employeeId}/save</c>,
/// now its one caller) so the refusal could never drift between them — the same anti-drift
/// rationale that lifted the approval-period predicate out of <c>SkemaEndpoints</c> in S128 (and
/// <c>ApprovalVisibility</c> before it).
```

### S-10 · `src/Backend/StatsTid.Backend.Api/Services/EmploymentWindowGate.cs` · `IsOutsideEmploymentWindow` summary

status: still stale
lines at `6ab643a7`: `:42` · line endings: LF
source: same file, found on verification (same claim as `:10`)
why: "once for both writers": one writer is left (`SkemaEndpoints.cs:2598`).

old_str (exact, unique in the file):
```text
    /// own, it only names the refusal condition once for both writers.
```

new_str:
```text
    /// own, it only names the refusal condition once.
```

### S-11 · `src/Backend/StatsTid.Backend.Api/Services/EmploymentWindowGate.cs` · `OutsideEmploymentPeriod` summary

status: still stale
lines at `6ab643a7`: `:48-49` · line endings: LF
source: same file, found on verification (same claim as `:10`)
why: "so both registration writers return": one writer is left (`SkemaEndpoints.cs:2603`).

old_str (exact, unique in the file):
```text
    /// THE single construction site for the outside-employment-period 422, so both registration
    /// writers return byte-identically the same status code, body shape, and message. Callers
```

new_str:
```text
    /// THE single construction site for the outside-employment-period 422, so every refusal
    /// returns byte-identically the same status code, body shape, and message. Callers
```

### S-12 · `src/Infrastructure/StatsTid.Infrastructure/UserRepository.cs` · "S136 / TASK-13603 (ADR-040 D3 / SEC-046) — DELIBERATE allowlist EXTENSION"

status: still stale
lines at `6ab643a7`: `:242-243` · line endings: CRLF
source: the eleven: `UserRepository.cs:243`
why: Reads as two live writers; the POST is retired (`TimeEndpoints.cs:37`). The S136 history is kept and marked; the Skema save still reads terminated-inclusive (`SkemaEndpoints.cs:781`, `:799`).

old_str (exact, unique in the file):
```text
    // the ratified ADR + the S136 refinement: the TWO REGISTRATION WRITERS join the
    // allowlist — POST /api/time-entries and POST /api/skema/{id}/save (terminated-
```

new_str:
```text
    // the ratified ADR + the S136 refinement: the TWO REGISTRATION WRITERS joined the
    // allowlist — POST /api/time-entries (retired in S148) and POST /api/skema/{id}/save (terminated-
```

### S-13 · `src/Infrastructure/StatsTid.Infrastructure/UserRepository.cs` · `GetByIdIncludingTerminatedAsync` summary

status: still stale
lines at `6ab643a7`: `:326` · line endings: CRLF
source: the eleven: `UserRepository.cs:307`
why: Same claim in the method's own summary; the surviving caller is `SkemaEndpoints.cs:799`.

old_str (exact, unique in the file):
```text
    /// pre-transaction subject reads</b> (<c>POST /api/time-entries</c> and the Skema save) — <b>and, since
```

new_str:
```text
    /// pre-transaction subject reads</b> (<c>POST /api/time-entries</c>, retired in S148, and the Skema save) — <b>and, since
```

### S-14 · `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs` · "AMENDING THE S124 REJECTED RULING (owner ruling R1)"

status: still stale
lines at `6ab643a7`: `:1415-1418` · line endings: CRLF
source: the eleven: `ApprovalEndpoints.cs:1406`
why: "`/api/time-entries` and Skema both accept writes": only the Skema save writes now (`TimeEndpoints.cs:37`); a REJECTED month stays writable because only EMPLOYEE_APPROVED and APPROVED lock (`ApprovalPeriodSaveLock.cs:30`).

old_str (exact, unique in the file):
```text
                // month — it is one the employee is actively editing again. `/api/time-entries` and
                // Skema both accept writes into a REJECTED period (only EMPLOYEE_APPROVED and APPROVED
                // lock the grid), so the figures a manager reads off a rejected row are NOT "these very
                // numbers" the decision was taken on; they are whatever the employee has typed since.
```

new_str:
```text
                // month — it is one the employee is actively editing again. The Skema save, the one
                // registration writer, accepts writes into a REJECTED period (only EMPLOYEE_APPROVED and
                // APPROVED lock the grid), so the figures a manager reads off a rejected row are NOT "these
                // very numbers" the decision was taken on; they are whatever the employee has typed since.
```

### S-15 · `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs` · `ExecuteSendAsync` summary, "The invariant it exists to hold"

status: still stale
lines at `6ab643a7`: `:2235-2240` · line endings: CRLF
source: the eleven: `ApprovalEndpoints.cs:2213`
why: The third qualification is about the retired POST (and was already loose: the POST gained the in-lock re-check in S128, TASK-12803). The one writer left re-reads the status under the lock the send takes (`SkemaEndpoints.cs:2607-2635`; the send's lock `ApprovalEndpoints.cs:2295`).

old_str (exact, unique in the file):
```text
    /// advisory lock that every request-path projection writer holds. Three qualifications, each
    /// stated because leaving one out would overclaim: <c>ProjectionBackfillService</c> is NOT
    /// enrolled (a deliberate carve-out, refinement §3.4); legacy <c>SUBMITTED</c> rows stay
    /// manager-approvable WITHOUT validation (owner ruling R6 — the hole is accepted, not closed);
    /// and <c>POST /api/time-entries</c> has no approval-status check, so the lock stops a write
    /// racing INSIDE a send but not one landing AFTER it.</para>
```

new_str:
```text
    /// advisory lock that every request-path projection writer holds. Two qualifications, each
    /// stated because leaving one out would overclaim: <c>ProjectionBackfillService</c> is NOT
    /// enrolled (a deliberate carve-out, refinement §3.4); and legacy <c>SUBMITTED</c> rows stay
    /// manager-approvable WITHOUT validation (owner ruling R6 — the hole is accepted, not closed).
    /// The one registration writer, the Skema save, re-reads the period's status under this same
    /// lock, so a save that lands AFTER a send is refused.</para>
```

### S-16 · `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs` · "S138 / TASK-13805 — TERMINATED-INCLUSIVE validator swap"

status: still stale
lines at `6ab643a7`: `:2343` · line endings: CRLF
source: the ten more: `ApprovalEndpoints.cs:2310`
why: `TimeEndpoints.cs` no longer makes this swap: its two GETs use the active-only `ValidateEmployeeAccessAsync` (`TimeEndpoints.cs:31`, `:88`); the swap lives in the Skema save (`SkemaEndpoints.cs:781`).

old_str (exact, unique in the file):
```text
            // reasoning, as TimeEndpoints.cs / SkemaEndpoints.cs (S136 / TASK-13603).
```

new_str:
```text
            // reasoning, as SkemaEndpoints.cs (S136 / TASK-13603).
```

### S-17 · `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs` · "WINDOW SOURCE — the in-tx subject row"

status: still stale
lines at `6ab643a7`: `:2531` · line endings: CRLF
source: the ten more: `ApprovalEndpoints.cs:2479`
why: "the TimeEndpoints PAT-015 race" pointed at the "Placement (PAT-015)" note in the deleted POST handler (`git show e2864079^:src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs`, `:308`); the rule itself is PAT-015.

old_str (exact, unique in the file):
```text
        //    self-managed read, which would sit outside the lock (the TimeEndpoints PAT-015 race).
```

new_str:
```text
        //    self-managed read, which would sit outside the lock (the PAT-015 race).
```

### S-18 · `src/Backend/StatsTid.Backend.Api/Endpoints/CalendarEndpoints.cs` · "Why a NEW endpoint class rather than a member of TimeEndpoints"

status: still stale
lines at `6ab643a7`: `:24-25` · line endings: CRLF
source: the eleven: `CalendarEndpoints.cs:24`
why: `TimeEndpoints` now holds two reads only, `GET /api/time-entries/{employeeId}` (`TimeEndpoints.cs:15`) and `GET /api/absences/{employeeId}` (`:73`); the flex GET was deleted in S146 (`:115-119`) and no write remains.

old_str (exact, unique in the file):
```text
/// <c>TimeEndpoints</c> is the time-ENTRIES family (<c>/api/time-entries</c>, absences, flex) — a
/// per-employee, DB-backed, write-bearing surface. A calendar bootstrap read has no employee, no
```

new_str:
```text
/// <c>TimeEndpoints</c> is the time-ENTRIES family (the <c>/api/time-entries</c> and absences
/// reads) — a per-employee, DB-backed surface. A calendar bootstrap read has no employee, no
```

### S-19 · `src/Backend/StatsTid.Backend.Api/Endpoints/Helpers/DepartedSubjectWriteFloor.cs` · "Where it runs — exactly the closed list"

status: still stale
lines at `6ab643a7`: `:40` · line endings: CRLF
source: the ten more: `Endpoints/Helpers/DepartedSubjectWriteFloor.cs:40`
why: The closed list names "the time-entry POST (both)"; the floor's callers are the Skema save (`SkemaEndpoints.cs:832`, `:2531`), the send (`ApprovalEndpoints.cs:2434`), the reopen (`:1931`), the row-preferences PUT (`SkemaEndpoints.cs:3362`) and the compensation-choice PUT (`OvertimeEndpoints.cs:899`).

old_str (exact, unique in the file):
```text
/// (PAT-028): the Skema save (advisory pre-check and in-lock re-check), the time-entry POST (both),
```

new_str:
```text
/// (PAT-028): the Skema save (advisory pre-check and in-lock re-check),
```

### S-20 · `src/Infrastructure/StatsTid.Infrastructure/IEmploymentWindowResolverInTx.cs` · "Why this surface exists — do NOT harmonize it away"

status: still stale
lines at `6ab643a7`: `:39-40` · line endings: CRLF
source: the ten more: `IEmploymentWindowResolverInTx.cs:39`
why: The race was "documented at TimeEndpoints.cs (Placement (PAT-015))", text that left with the POST (`e2864079`); the same reasoning stands at the Skema save's in-lock approval re-read (`SkemaEndpoints.cs:2607-2618`).

old_str (exact, unique in the file):
```text
/// That is the exact race documented at <c>TimeEndpoints.cs</c> ("Placement (PAT-015)":
/// the approval-status in-lock read) and pinned by
```

new_str:
```text
/// That is the exact race documented at the Skema save's in-lock approval re-read
/// (<c>SkemaEndpoints.cs</c>, "THE AUTHORITATIVE APPROVAL-STATUS RE-READ"; PAT-015) and pinned by
```

### S-21 · `src/Infrastructure/StatsTid.Infrastructure/EmploymentWindowResolver.cs` · "Two surfaces, one implementation"

status: still stale
lines at `6ab643a7`: `:43-44` · line endings: CRLF
source: the ten more: `EmploymentWindowResolver.cs:43`
why: As S-17: "the TimeEndpoints PAT-015 race" points at deleted text; the rule is PAT-015.

old_str (exact, unique in the file):
```text
/// would sit outside the lock and miss a concurrently-committed write (the TimeEndpoints
/// PAT-015 race). The full do-not-harmonize warning lives on
```

new_str:
```text
/// would sit outside the lock and miss a concurrently-committed write (the PAT-015
/// race). The full do-not-harmonize warning lives on
```

### S-22 · `src/Infrastructure/StatsTid.Infrastructure/EmploymentWindowStrandCheck.cs` · "Why the caller's (conn, tx)" (class summary)

status: still stale
lines at `6ab643a7`: `:32-35` · line endings: CRLF
source: the ten more: `EmploymentWindowStrandCheck.cs:34`
why: "the registration writers acquire the SAME key": one writer is left, the Skema save (`SkemaEndpoints.cs:2487`); "the TimeEndpoints race class" points at deleted text.

old_str (exact, unique in the file):
```text
/// advisory lock on that tx, and the registration writers acquire the SAME key — a self-managed
/// connection would read OUTSIDE the lock and miss a registration committed while the edit was
/// blocked on it (the S136 lock regime / the TimeEndpoints race class). Never "simplify" this
/// to a pooled read.</para>
```

new_str:
```text
/// advisory lock on that tx, and the registration writer, the Skema save, acquires the SAME
/// key — a self-managed connection would read OUTSIDE the lock and miss a registration
/// committed while the edit was blocked on it (the S136 lock regime / the PAT-015 race
/// class). Never "simplify" this to a pooled read.</para>
```

### S-23 · `src/Backend/StatsTid.Backend.Api/Endpoints/EmploymentDateEndpoints.cs` · `CheckStrandedRegistrationsAsync` summary, "Why the caller's (conn, tx)"

status: still stale
lines at `6ab643a7`: `:918-920` · line endings: CRLF
source: the ten more: `EmploymentDateEndpoints.cs:792`
why: As S-22 (the PUTs take the lock at `EmploymentDateEndpoints.cs:222` and `:587`; the Skema save at `SkemaEndpoints.cs:2487`).

old_str (exact, unique in the file):
```text
    /// advisory lock on that tx, and the registration writers acquire the SAME key — a
    /// self-managed connection would read OUTSIDE the lock and miss a registration committed
    /// while this edit was blocked on it (the S136 lock regime / the TimeEndpoints race class).
```

new_str:
```text
    /// advisory lock on that tx, and the registration writer, the Skema save, acquires the SAME
    /// key — a self-managed connection would read OUTSIDE the lock and miss a registration
    /// committed while this edit was blocked on it (the S136 lock regime / the PAT-015 race class).
```

### S-24 · `docker/postgres/init.sql` · `time_entries_projection.hours` trailing comment

status: still stale
lines at `6ab643a7`: `:2080` · line endings: CRLF
source: the two A9 items: `docker/postgres/init.sql:1868` (ruling 1, log :402)
why: `RequestValidator` was deleted with the POST (TASK-14802, ruling A4); the Skema save refuses an entry only for hours ≤ 0 (`SkemaEndpoints.cs:910-918`) and sets no upper bound (`SkemaEntry`, `:3722-3727`).

old_str (exact, unique in the file):
```text
    hours                       NUMERIC(8,4)    NOT NULL,  -- S27 Step 7a P2 fix: 4 decimal places preserves sub-centesimal hours (e.g., 7.375h = 22.5min); RequestValidator accepts arbitrary decimal in (0,24], canonical events keep full precision, projection must too
```

new_str:
```text
    hours                       NUMERIC(8,4)    NOT NULL,  -- S27 Step 7a P2 fix: 4 decimal places preserves sub-centesimal hours (e.g., 7.375h = 22.5min); the Skema save accepts any decimal above 0, canonical events keep full precision, projection must too
```

### S-25 · `tests/StatsTid.Tests.Regression/Approval/OrphanCoverageTests.cs` · "The four facts" banner

status: still stale
lines at `6ab643a7`: `:74` · line endings: CRLF
source: N-3 (log :611; `S148-TASK-14806-5a-reviewer-c2.md:37`)
why: The class has five facts (`OrphanCoverageTests.cs:88`, `:121`, `:151`, `:196`, `:263`).

old_str (exact, unique in the file):
```text
    //  The four facts
```

new_str:
```text
    //  The five facts
```

### S-26 · `tests/StatsTid.Tests.Regression/Approval/OrphanCoverageTests.cs` · "Does this fixture discriminate the readings?"

status: still stale
lines at `6ab643a7`: `:186-190` · line endings: CRLF
source: N-3 (`:187`, `:190`)
why: The chosen Root wording is the fourth reading (adjudication 2a; `ApproverCoverage.cs:109-112`), not reading (i); on shapes A, B, D and E it classifies as (i) did (`S148-TASK-14804-14806-adjudication.md:50`), and shape F is where they part (`OrphanCoverageTests.cs:254-261`).

old_str (exact, unique in the file):
```text
    /// <para><b>Does this fixture discriminate the readings?</b> Reading (ii), "the walk names them for someone",
    /// would classify Ma, Mb and Ae as Orphan and Vb and Ve as Root — so shapes A, B and E discriminate (i) from
    /// (ii). Shape D does not: every person is classified the same under all three. Reading (iii), "holds any open
    /// PRIMARY line", differs from (i) only for a line from a person OUTSIDE the read (a switched-off report whose line
    /// stays open); no such person is seeded, so this fixture does NOT discriminate (i) from (iii).</para>
```

new_str:
```text
    /// <para><b>Does this fixture discriminate the readings?</b> Reading (ii), "the walk names them for someone",
    /// would classify Ma, Mb and Ae as Orphan and Vb and Ve as Root — so shapes A, B and E discriminate the chosen
    /// wording above from (ii). Shape D does not: every person is classified the same under every reading named here.
    /// On this fixture, reading (iii), "holds any open PRIMARY line", could differ from the chosen wording only for a
    /// line from a person OUTSIDE the read (a switched-off report whose line stays open); no such person is seeded,
    /// so this fixture does NOT discriminate the chosen wording from (iii). On these four shapes the chosen wording
    /// classifies everyone as the earlier reading (i), "some PRIMARY line names them", did; shape F, pinned by
    /// <c>ManagerOfManagersLosesApprover_IsOrphan_CardEqualsRollUp</c>, is where they differ.</para>
```

### S-27 · `tests/StatsTid.Tests.Regression/Approval/OrphanCoverageTests.cs` · "The wording chosen for Root (R-19)"

status: still stale
lines at `6ab643a7`: `:182` · line endings: CRLF
source: N-1 (log :610, same Owed list as N-3), the test's copy of the phrase
why: ADR-027 D9 calls anyone with no active PRIMARY line a root (`ADR-027-reporting-line-hierarchy.md:92`); the code also requires a report in the read (`ApproverCoverage.cs:110`), so "literal" overstates.

old_str (exact, unique in the file):
```text
    /// <para><b>The wording chosen for Root (R-19), ADR-027 D9's literal root:</b> the walk names nobody, the person
```

new_str:
```text
    /// <para><b>The wording chosen for Root (R-19), ADR-027 D9's root that also has a report:</b> the walk names nobody, the person
```

### S-28 · `src/Infrastructure/StatsTid.Infrastructure/ApproverCoverage.cs` · "<b>Root</b> (R-19) is ADR-027 D9's literal root"

status: still stale
lines at `6ab643a7`: `:44` · line endings: CRLF
source: N-1 (log :610; `S148-TASK-14806-5a-reviewer-c2.md:33`)
why: As S-27; the log's disposition is the exact replacement phrase.

old_str (exact, unique in the file):
```text
/// <para><b>Root</b> (R-19) is ADR-027 D9's literal root: the walk names nobody, the person holds no
```

new_str:
```text
/// <para><b>Root</b> (R-19) is ADR-027 D9's root that also has a report: the walk names nobody, the person holds no
```

### S-29 · `tools/StatsTid.DemoSeed/Generation/AllocatedMonthBuilder.cs` · "The two gates it targets"

status: still stale
lines at `6ab643a7`: `:10` · line endings: CRLF
source: DemoSeed pointers (log :792; `S148-TASK-14809-report.md:132`)
why: `ApprovalEndpoints.cs:1387-1506` is a pre-S136 range; the gates are steps (5d) and (5e) of `ExecuteSendAsync` (`ApprovalEndpoints.cs:2251`, `:2487`, `:2603`). A named anchor replaces the line range so it cannot drift again.

old_str (exact, unique in the file):
```text
/// <para><b>The two gates it targets</b> (<c>ApprovalEndpoints.cs:1387-1506</c>):</para>
```

new_str:
```text
/// <para><b>The two gates it targets</b> (<c>ApprovalEndpoints.ExecuteSendAsync</c>, steps (5d)
/// coverage and (5e) allocation):</para>
```

### S-30 · `tools/StatsTid.DemoSeed/Model/DemoManifest.cs` · `Allocations` property summary

status: still stale
lines at `6ab643a7`: `:121` · line endings: CRLF
source: same file as the reported `:133`, found on verification
why: `ApprovalEndpoints.cs:1446-1506` is stale the same way; the allocation gate is step (5e) (`ApprovalEndpoints.cs:2603`).

old_str (exact, unique in the file):
```text
    /// what the submit-time allocation gate reconciles (<c>ApprovalEndpoints.cs:1446-1506</c>).
```

new_str:
```text
    /// what the submit-time allocation gate reconciles (<c>ApprovalEndpoints.ExecuteSendAsync</c>, step (5e)).
```

### S-31 · `tools/StatsTid.DemoSeed/Model/DemoManifest.cs` · `WorkTime` property summary

status: still stale
lines at `6ab643a7`: `:133` · line endings: CRLF
source: DemoSeed pointers (log :792; `S148-TASK-14809-report.md:132`)
why: `ApprovalEndpoints.cs:1387-1444` is stale; the coverage gate is step (5d) (`ApprovalEndpoints.cs:2487`).

old_str (exact, unique in the file):
```text
    /// (<c>ApprovalEndpoints.cs:1387-1444</c>). Null ⇒ absent, as for <see cref="Allocations"/>.
```

new_str:
```text
    /// (<c>ApprovalEndpoints.ExecuteSendAsync</c>, step (5d)). Null ⇒ absent, as for <see cref="Allocations"/>.
```

### S-32 · `tests/StatsTid.Tests.Regression/Outbox/SkemaWorkTimeDayBoundsGuardTests.cs` · class summary, "Two checks remain"

status: still stale
lines at `6ab643a7`: `:20-22` · line endings: CRLF
source: the bounds docstring (log :814; `S148-TASK-14809-5a-reviewer.md`, NOTE 2)
why: Production keeps the labels (2) and (3) (`SkemaEndpoints.cs:1041-1045`, `:1076`, `:1090`; (1) was "Negative manual hours" at `57624e6c`); the docstring renumbers them (1)/(2). The log rules "align it", so the docstring moves, not production.

old_str (exact, unique in the file):
```text
/// <c>work_time_projection</c> row is written. Two checks remain (manual hours, and with them the
/// negative-manual check, are retired): (1) overlapping intervals → 422
/// <c>work_time_intervals_overlap</c> (pinned here); (2) total worked hours (interval hours) &gt; 24 →
```

new_str:
```text
/// <c>work_time_projection</c> row is written. Two numbered checks remain, labelled (2) and (3) as in
/// the handler (check (1), negative manual hours, retired with manual hours): (2) overlapping intervals → 422
/// <c>work_time_intervals_overlap</c> (pinned here); (3) total worked hours (interval hours) &gt; 24 →
```

### S-33 · `tests/StatsTid.Tests.Regression/Approval/RejectedMonthVisibilityTests.cs` · `WorkedSentinel` constant

status: still stale
lines at `6ab643a7`: `:54` · line endings: CRLF
source: tidiness item 1 (log :816; `S148-TASK-14809-5a-reviewer.md`, "small test tidiness items")
why: `WorkedSentinel` is referenced by no code, only by a comment; the seed writes the interval literal (`RejectedMonthVisibilityTests.cs:264-266`), so the two can drift. Comment-only remedy: state the coupling where the constant is declared.

old_str (exact, unique in the file):
```text
    private const decimal WorkedSentinel = 6.5m;               // recognizable, non-zero, balanced
```

new_str:
```text
    // Seeded below as the one interval 08:00-14:30 (the work_time_projection insert): change both together.
    private const decimal WorkedSentinel = 6.5m;               // recognizable, non-zero, balanced
```

### S-34 · `tests/StatsTid.Tests.Regression/Contracts/S120SkemaSpecRuntimeTests.cs` · `WorkTimeDayKeys` summary

status: still stale
lines at `6ab643a7`: `:73-75` · line endings: CRLF
source: tidiness item 2 (log :816)
why: The kind-F label sits on the key-set array; census row 8 asked for it on the fact's docstring (`.claude/plans/s148/TASK-14809-T.md:178`), and the assertion is in the save fact (`S120SkemaSpecRuntimeTests.cs:269`).

old_str (exact, unique in the file):
```text
    /// <summary>The EXACT 2 members of a <c>SkemaWorkTimeDayRow</c>. S148 S0-5: <c>manualHours</c> is retired;
    /// this key-set assertion is kind F (CI): the served row has three keys until the member is removed
    /// (red condition: <c>SkemaWorkTimeDayRow</c> still serving <c>manualHours</c>).</summary>
```

new_str:
```text
    /// <summary>The EXACT 2 members of a <c>SkemaWorkTimeDayRow</c> (S148 S0-5: <c>manualHours</c> is
    /// retired; the kind-F label of the assertion that uses this set is on
    /// <c>Save_Post200_SavedCountReceipt_Unconditioned_ThenMonthServesTheRow</c>).</summary>
```

### S-35 · `tests/StatsTid.Tests.Regression/Contracts/S120SkemaSpecRuntimeTests.cs` · `Save_Post200_SavedCountReceipt_Unconditioned_ThenMonthServesTheRow` summary

status: still stale
lines at `6ab643a7`: `:236-237` · line endings: CRLF
source: tidiness item 2, the label's new home
why: Receives the label S-34 removes; the red condition is the one the old label stated.

old_str (exact, unique in the file):
```text
    /// choreography through the in-tx projection) — the save's 17-site error fan stays untyped
    /// and untouched (S120 Explicit exclusions).</summary>
```

new_str:
```text
    /// choreography through the in-tx projection) — the save's 17-site error fan stays untyped
    /// and untouched (S120 Explicit exclusions).
    /// <para>S148 S0-5: the workTime row's key-set assertion (<c>WorkTimeDayKeys</c>) is kind F (CI): it
    /// is red while <c>SkemaWorkTimeDayRow</c> still serves <c>manualHours</c> (three keys).</para></summary>
```

### S-36 · `tests/StatsTid.Tests.Regression/Outbox/SkemaEntitlementEligibilityGuardTests.cs` · "Rule-stubbed client: once the eligibility gate passes"

status: still stale
lines at `6ab643a7`: `:191-193` · line endings: CRLF
source: TASK-14811 report :146 / 14811-T report :159 (log :880, "three stale comments")
why: Child's-sick codes map to no quota type (`EntitlementMapping.cs:61-69`), so the quota pre-check skips them (`SkemaEndpoints.cs:1585-1586`) and the rule-engine call runs only for a quota type with delta > 0 (`:1639`); the save no longer "runs quota validation through the rule engine".

old_str (exact, unique in the file):
```text
        // Rule-stubbed client: once the eligibility gate passes, the CHILD_SICK POST still
        // runs quota validation through the rule engine (HTTP), so the stub is required for
        // the successful save. The eligibility 422 itself rejects pre-rule-engine.
```

new_str:
```text
        // Rule-stubbed client: kept, and harmless. A child's-sick save makes no rule-engine call: its
        // codes map to no quota type, so the quota pre-check skips them (QUAL-236). The eligibility
        // 422 itself rejects pre-rule-engine.
```

### S-37 · `tests/StatsTid.Tests.Regression/Outbox/ForcedRollbackHarness.cs` · "THE WIRE-DRIVEN SHAPE these convert to"

status: still stale
lines at `6ab643a7`: `:63-65` · line endings: CRLF
source: 14802-T report :47 ("stale comments", found not fixed)
why: `Hosting.TimeEntryRegisterAtomicHttpTests` was deleted with the route (`789b3ccf`); no file of that name exists (`git ls-files | grep -i TimeEntryRegisterAtomicHttpTests` is empty). "Pattern C" itself is still used later in the file (`:475`).

old_str (exact, unique in the file):
```text
/// <c>Approval.SendAtomicityTests</c>; QUAL-016 spike conversions:
/// <c>Hosting.AgreementConfigCreateAtomicHttpTests</c> (Pattern B) and
/// <c>Hosting.TimeEntryRegisterAtomicHttpTests</c> (Pattern C). The assertion helpers below are
```

new_str:
```text
/// <c>Approval.SendAtomicityTests</c>; the QUAL-016 spike conversion
/// <c>Hosting.AgreementConfigCreateAtomicHttpTests</c> (Pattern B). The assertion helpers below are
```

### S-38 · `tests/StatsTid.Tests.Regression/Outbox/TimeProjectionAtomicTests.cs` · class summary, "The time-entry path is reached via"

status: still stale
lines at `6ab643a7`: `:30-33` · line endings: CRLF
source: 14802-T report :47 (`TimeProjectionAtomicTests.cs:90`; this summary found on verification)
why: "and the legacy time-entry POST": retired (`TimeEndpoints.cs:37`).

old_str (exact, unique in the file):
```text
/// The time-entry path is reached via the Skema save seam (POST /api/skema/{id}/save) and
/// the legacy time-entry POST; the absence path is reached via the Skema save seam. This
/// test does not exercise any HTTP route — it pins the repository contract those write
/// seams depend on.
```

new_str:
```text
/// The time-entry and absence paths are both reached via the Skema save seam
/// (POST /api/skema/{id}/save), the one registration writer. This
/// test does not exercise any HTTP route — it pins the repository contract that write
/// seam depends on.
```

### S-39 · `tests/StatsTid.Tests.Regression/Outbox/TimeProjectionAtomicTests.cs` · "The throw happens BEFORE the projection INSERT"

status: still stale
lines at `6ab643a7`: `:90-91` · line endings: CRLF
source: 14802-T report :47 (`TimeProjectionAtomicTests.cs:90`)
why: "TimeEndpoints time-entry POST + the Skema save handler": one seam is left, and it enqueues first (`SkemaEndpoints.cs:3004-3005`).

old_str (exact, unique in the file):
```text
                // production write seams order enqueue FIRST (TimeEndpoints time-entry POST
                // + the Skema save handler), so the projection INSERT is the second step.)
```

new_str:
```text
                // production write seam orders enqueue FIRST (the Skema save handler), so the
                // projection INSERT is the second step.)
```

### S-40 · `tests/StatsTid.Tests.Unit/Events/S148ConfigStageEventRoundTripTests.cs` · `WHOLE_DAY with PartHours` fixture comment

status: still stale
lines at `6ab643a7`: `:176` · line endings: CRLF
source: `S148-TASK-14815-5a-reviewer-c2.md:64` (optional, "when … next touched")
why: This fact round-trips the event (`S148ConfigStageEventRoundTripTests.cs:182-226`, `PartHours` asserted at `:219`); only the mapper test pins details keys.

old_str (exact, unique in the file):
```text
            // WHOLE_DAY with PartHours is refused by `customary_days_part_hours_check`; filled here only to pin every details key. Not an example row.
```

new_str:
```text
            // WHOLE_DAY with PartHours is refused by `customary_days_part_hours_check`; filled here only to round-trip every member. Not an example row.
```

### S-41a · `tests/StatsTid.Tests.Regression/Config/ConfigDateValidationTests.cs` · AC-C2b summary, first line

status: still stale
lines at `6ab643a7`: `:388` · line endings: CRLF
source: `S148-TASK-14818-5a-reviewer-c2.md:109` ("name arm 7 … when the file is next touched")
why: The fact has seven arms (`ConfigDateValidationTests.cs:416`, `:423`, `:430`, `:435`, `:452`, `:462`, `:476`), not four.

old_str (exact, unique in the file):
```text
    /// AC-C2b (1), four arms over TASK-14818's closed Monday list. Clock Thursday 12 November 2026; v1 (37 h)
```

new_str:
```text
    /// AC-C2b (1), seven arms over TASK-14818's closed Monday list. Clock Thursday 12 November 2026; v1 (37 h)
```

### S-41b · `tests/StatsTid.Tests.Regression/Config/ConfigDateValidationTests.cs` · AC-C2b summary, end of arm 4

status: still stale
lines at `6ab643a7`: `:396` · line endings: CRLF
source: same item
why: Arms 5-7 (`:452-481`) are not described; the text is taken from their own comments.

old_str (exact, unique in the file):
```text
    /// (field <c>WeeklyNormHours</c>), and an edit changing <c>MaxFlexBalance</c> alone (norm still 37) is accepted.
```

new_str:
```text
    /// (field <c>WeeklyNormHours</c>), and an edit changing <c>MaxFlexBalance</c> alone (norm still 37) is accepted.
    /// Arms 5 and 6: an override create, and an override switched back on, that set a weekly norm dated
    /// Wednesday 11 November are refused the same way. Arm 7: a config's first version with a weekly norm,
    /// published dated that Wednesday, is accepted (no earlier version, so no in-force value changes).
```

### S-41c · `tests/StatsTid.Tests.Regression/Config/ConfigDateValidationTests.cs` · AC-C2b red conditions

status: still stale
lines at `6ab643a7`: `:401-402` · line endings: CRLF
source: same item (the reviewer's exact fix)
why: Arm 7 passes on the base like arms 3 and 4 (the base ignores the date, so a first-version publish is accepted) but the red conditions do not say so.

old_str (exact, unique in the file):
```text
    /// and for arm 4's edit and deactivation, so the 400 assertions fail first. Arms 3 and 4's last step pass on
    /// the base too and pin that the closed list is no wider than ruled. The ruled <c>field</c> for the date's
```

new_str:
```text
    /// and for arm 4's edit and deactivation, so the 400 assertions fail first. Arms 3 and 4's last step pass on
    /// the base too and pin that the closed list is no wider than ruled; arm 7 passes on the base too and guards
    /// the first-version exemption. The ruled <c>field</c> for the date's
```

### S-42 · `src/Infrastructure/StatsTid.Infrastructure/PayInputsLock.cs` · `AcquireExclusiveAsync` summary

status: still stale
lines at `6ab643a7`: `:38-39` · line endings: CRLF
source: `S148-TASK-14823-5a-reviewer.md:31` ("Correct the 'Blocks until granted' sentence when that file is next touched")
why: The lock statement runs under Npgsql's default 30-second command timeout; no `CommandTimeout` is set (`PayInputsLock.cs:44-45`; `git grep -i commandtimeout -- src` is empty); registered as F9 (`docs/operations/performance-finding-register.md:37`, `:337-338`).

old_str (exact, unique in the file):
```text
    /// first statement of a writer's READ COMMITTED transaction. Blocks until granted; released at
    /// commit or rollback.
```

new_str:
```text
    /// first statement of a writer's READ COMMITTED transaction. Waits for the lock within Npgsql's
    /// default 30-second command timeout (no <c>CommandTimeout</c> is set), so a writer queued behind a
    /// longer holder fails, writing nothing, rather than waiting on (F9); released at commit or rollback.
```

### S-43a · `tests/StatsTid.Tests.Regression/Skema/SkemaCellClearRaceTests.cs` · rollback fact, "Kind R" paragraph (1 of 2)

status: still stale
lines at `6ab643a7`: `:168` · line endings: CRLF
source: `S148-TASK-14840-5a-reviewer-c3.md:49` (optional, "at the next touch")
why: Cosmetic, not a false claim: Markdown backticks inside an XML doc comment where the file uses `<c>` (`SkemaCellClearRaceTests.cs:161`, `:163`). Line wrapping is left as is.

old_str (exact, unique in the file):
```text
against `used` 23,0
```

new_str:
```text
against <c>used</c> 23,0
```

### S-43b · `tests/StatsTid.Tests.Regression/Skema/SkemaCellClearRaceTests.cs` · rollback fact, "Kind R" paragraph (2 of 2)

status: still stale
lines at `6ab643a7`: `:168` · line endings: CRLF
source: same item
why: As S-43a.

old_str (exact, unique in the file):
```text
after `used` becomes 25,0
```

new_str:
```text
after <c>used</c> becomes 25,0
```


## Not applied

### A. Already fixed (evidence: the commit, and the text at HEAD)

| Finding (source) | Fixed by | Evidence at `6ab643a7` |
|---|---|---|
| `SkemaEndpoints.cs:797`, "Mirrors the `hours <= 0` half of RequestValidator.ValidateTimeEntry" (census (b), one of the eleven) | `af247ca7` (TASK-14840) | the cell-shape comment no longer names the validator (`SkemaEndpoints.cs:881-900`); `git grep -n RequestValidator -- src` finds only `init.sql:2080` (S-24) |
| `SkemaEndpoints.cs:804`, "(IsNullOrWhiteSpace, the RequestValidator idiom)" (A9; ruling 1) | `af247ca7` | now "(IsNullOrWhiteSpace)" (`SkemaEndpoints.cs:892-893`) |
| 14804's `:2550-2553`, the catch comment naming only B1/B2 (log :441) | `8415a2a2` (14804 fix-up 1, comments only) | the catch lists all five in-lock re-checks (`SkemaEndpoints.cs:3269-3278`) |
| 14806 Fable W-2's three comments (`HrFollowUpApprovalResponses.cs:173`, `:208-209`; `Program.cs:192-194`) (log :531, :538) | `38b800f6` | confirmed by `S148-TASK-14806-5a-reviewer-c2.md:12` |
| 14811: `EntitlementConfigSupersessionTests.cs:353` "annual_quota=1" (log :880) | `25e6bb88` (14811-T) | now "annual_quota=3 (OK26, days per illness)"; init.sql seeds 3 (`init.sql:2006`) |
| 14811: `VacationSettlementService.cs:1914` "3 variants" (log :880) | `73a395dc` (14842 fix-up 1) | `git grep -n "3 variants" -- src` is empty |
| 14812 c2 NOTE, the allowance test header's red conditions | `28ac4e89` | `ArsoversigtPage.childSickAllowance.test.tsx:6-7` |
| 14816 adjudication: ten stale comment lines at merge | `c2a31a6a` | commit subject: "stale counts made true-now in ten lines" |
| 14820: `AdminEndpoints.cs:1814`, "the future-dating validator above" | `ff082ceb` (14820 fix-up 1) | `git log -S "the future-dating validator above"` shows its removal there |
| 14821 adjudication: three stale comments (log :1856) | `d5466748` | commit subject: "three stale comments made true-now. Comment only." |
| 14823 c2 NOTE 2: the concurrency exception's doc; the census file's BOM and blank line | `8e74df3b` | `LocalAgreementProfileRepository.cs:286-288`; `PayInputsLockCensusTests.cs` starts with `using` (no BOM) |
| 14832 stack c2 N6: `LocalProfileValidationTests.cs:23-26`, `profileSettingsCopy.ts:30` | `cd7b0801` | `LocalProfileValidationTests.cs:23-25`; `profileSettingsCopy.ts:30-31` |
| 14834 stack c4 NOTE 3: the FU-3/FU-4 header | `db953d63` (FU-6) | `CustomaryDaysEditor.test.tsx:548-551` |
| 14836: `Contracts/EmployeeFieldResponses.cs:41-42`, "today UTC" (log :4184) | `319f8b68` (HR-facts stack merge) | now "the Copenhagen business day" (`:42`) |
| 14839 c1: AC-C12c's docstring; `ConfigStageSeedingTests.cs:145` "is not pinned" | `c186e2b1` | `DemoHrFactsAllowedSetTests.cs:24-25`; `ConfigStageSeedingTests.cs:145-147` |
| 14842 c4 N-10 and N-11 (`CarryoverBalanceSeedTests.cs:201`, `:68`) (log :1103) | `6bedcf2c` (stack merge) | `:201` "the 26 row is closed ON that day"; `:68` `SecondRaise = new(…)` |
| 14845 c2 NOTE 4: `OvertimeEndpoints.cs` "Pattern C — no audit row" | `07286b1f` | create and approve name the audit-projection row (`:207-208`, `:450-453`); the compensate handler's "no audit row" (`:704-705`) is true, it writes none |
| 14850 c1 N7 and c2 N1, N2: the drawer's dated-writes remarks, `useEditPerson.ts:254-255`, the `blockedReason` doc | `b2185654` (FU-1) and `65b4c340` (merge) | `PersonDrawer.tsx:786-790`; `EffectiveDatePicker.tsx:22-27`, `:140-142`; `useEditPerson.ts:255` |
| `ProjectionBackfillService.cs:133`, the rebuild summary naming two event types, and the tool's counters (log :1151; 14840 report :158) | `a331e452` (14840 stack merge) | summary lists all seven types (`ProjectionBackfillService.cs:132-139`); `tools/ProjectionBackfill/Program.cs:85` prints the cleared counters. **The C-1 audit report (§ 4) lists this as OPEN; it is done.** |
| `Sprint12AgreementConfigTests.cs:126` (log :2579) | `f1786631` (14826-T) | the log line itself records the correction |

### B. Excluded: needs regeneration, or changes a test name

**`tools/StatsTid.DemoSeed/Generation/SqlEmitter.cs:202` and `:230`** (log :4650; `S148-TASK-14839-5a-reviewer-c1.md:76`). These are `sb.AppendLine("-- …")` strings: the comment is emitted into SQL. "nothing reads customs yet" and "nothing reads these facts yet" are slightly broad (their editors read them); the exact form is "nothing reads them for pay yet". Changing either line changes the emitted text, so it would need `tests/StatsTid.Tests.DemoSeed/Golden/golden-legacy-smoke.sql` regenerated and the committed `docker/postgres/99-demo-seed.sql` regenerated (`git grep -c -i "nothing reads"` gives 2 in each), with PIN-1 and `CommittedDemoSeedTests` re-run. **EXCLUDED: needs regeneration.** The log's disposition stands: fix when the emitter is next touched. (I found one golden plus the committed file, not "two goldens"; see the report.)

**X-1 · `frontend/src/lib/__tests__/api-typed-overloads.test.ts:352` · the phase-pin title** (log :649; `S148-TASK-14803-5a-reviewer.md:20`). The title is the test's name, so the fix renames the test. **EXCLUDED under the brief's rule (a test-name change).** Nothing would need regenerating: `git grep -n -i "phase pin"` finds the title only here and in `.claude/plans/s148/TASK-14803.md`, and no filter, snapshot or CI step names it. If the Orchestrator rules it in, the ready block is below (lean: drop the counts, which went stale twice; the type-level unions are the pin).

### X-1 · `frontend/src/lib/__tests__/api-typed-overloads.test.ts` · the phase-pin `it(...)` title

status: still stale, EXCLUDED (test name); apply only if ruled in
lines at `6ab643a7`: `:352` · line endings: CRLF
source: log :649; `S148-TASK-14803-5a-reviewer.md:20`
why: The title says "36 POSTs, 21 PUTs, 13 DELETEs"; the unions it pins hold 41, 25 and 12 (`api-typed-overloads.test.ts:403-427`, `:430-441`, `:444-489`).

old_str (exact, unique in the file):
```text
  it('phase pin: after the S119 Pass-6 bucket-B drain — 36 POSTs, 21 PUTs, 13 DELETEs (retrofit updates this)', () => {
```

new_str:
```text
  it('phase pin: the typed POST, PUT and DELETE unions, exactly (a typed-route change updates this)', () => {
```


### C. Excluded: not a comment change

| Finding | Why excluded |
|---|---|
| `TransferPayrollBoundaryTests.cs`: parse the 409 body with `JsonDocument`, assert "different content" (log :2303) | test assertion changes |
| `S148-TASK-14822-5a-reviewer-c3.md:49`: parse `kind` with `JsonDocument` | test assertion change |
| `S148-TASK-14842-5a-reviewer.md:92` (N-5): pin `ReadCommitted` on two transactions | code change; done anyway in `73a395dc` |
| `S148-TASK-14850-5a-reviewer-c2.md:90` (N3): move `EffectiveDatePicker.test.tsx` into `__tests__/` | a file move; still beside its component at HEAD |
| `S148-WAVE3B-5a-reviewer.md:77`: align the engine and the screen on a tied end time | code change (`RestPeriodRule.cs:370`, `useSkema.ts:1002`) |

### D. Conditional: tidiness item 3, waiting on a doc owed at close

**X-2 · `tests/StatsTid.Tests.Regression/Outbox/SendCheckIntervalOnlyTests.cs:17`** cites "ADR-028 D4 as amended by S148". ADR-028 carries no S148 note yet; its one D4 note (`ADR-028-work-time-persistence-allocation-gate-timer-retirement.md:111`) is S145's. The log routes the fix through the amendment (`SPRINT-148.md:816`; the reviewer: "make sure the ADR-028 amendment lands"). **Not applied: the comment becomes true when the Orchestrator writes the ADR-028 D4 amendment at close.** If that amendment is not written, apply this fallback instead (cite: `SkemaEndpoints.cs:3729-3730`, "the day's only work-time record (ADR-043 D10, D-2.4)"):

### X-2 · `tests/StatsTid.Tests.Regression/Outbox/SendCheckIntervalOnlyTests.cs` · class summary, "ADR-028 D4 as amended by S148"

status: conditional fallback; apply only if the ADR-028 D4 amendment is not written
lines at `6ab643a7`: `:17` · line endings: CRLF
source: tidiness item 3 (log :816)
why: ADR-028 carries no S148 amendment yet (its only D4 note is `ADR-028-…:111`); the amendment is owed at close.

old_str (exact, unique in the file):
```text
/// check's worked side is Σ interval hours alone (ADR-028 D4 as amended by S148).
```

new_str:
```text
/// check's worked side is Σ interval hours alone (ADR-028 D4; clock periods are a day's only work-time record, ADR-043 D10).
```


### E. Not stale: the finding's premise does not hold

- **`PaidAbsenceDayBoundRaceTests.cs:77`** (`S148-TASK-14804-5a-reviewer-c3.md:20`, NOTE 2: "cites base `SkemaEndpoints.cs:982`; at `a03d61ae` the `if` is on `:981`"). At `a03d61ae`, `:981` is a comment line and `:982` is `if (request.Absences is not null && request.Absences.Length > 0)` (`git show a03d61ae:src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs`). The docstring's `:982` is right; nothing to change.
- **`ComplianceEndpoints.cs:247`** (`S148-TASK-14843-security-consult.md:69`: "begun the day before" → "begun before the month"). The comment already reads "a night shift begun before the month" (text from `62df2585`).

### F. Recorded nearby but not sent to the sweep

- **`SkemaEndpoints.cs:1043-1045`**, "There is no check (1): it guarded manual hours, which are retired; the labels are kept so existing cites stay valid" (log :815: "for TASK-14814 … to drop when it renumbers"). TASK-14814 did not renumber, so the sentence is still there. It is true now, and it is what keeps S-32's (2)/(3) alignment honest; PAT-032 rewrites history only where the code changes. Left as is.
- **Checked although the grep did not match them**, because a report or review asked for the same kind of edit: `SettlementReversalEndpoints.cs:62-68` (`S148-TASK-14821-fix1-report.md:66`: the 409 list lacked `StrandedRegistrations`) now lists it (`:65`); `RestPeriodRuleMidnightRestTests.cs:83-85` and `:126-127` (`S148-WAVE3B-5a-reviewer.md`: "reword both") carry the reviewer's wording at `:82-83` and `:124-125`. Both are already true; nothing to apply.

### G. Candidate found on verification (needs a ruling; not in the authorized list)

**C-1 · `tests/StatsTid.Tests.Regression/Approval/TeamOverviewAggregateTests.cs:552`.** It is the test-side twin of S-14 and is stale for the same reason, so it falls inside A9's class ("the comments the wave-1 changes leave stale"), but no record lists it. Ready block:

### C-1 · `tests/StatsTid.Tests.Regression/Approval/TeamOverviewAggregateTests.cs` · REJECTED-withholding docstring

status: still stale, candidate; apply only if ruled in
lines at `6ab643a7`: `:552` · line endings: CRLF
source: not on any S148 list; found on verification (same class as S-14)
why: "both <c>/api/time-entries</c> and Skema accept writes" — only the Skema save writes now (`TimeEndpoints.cs:37`).

old_str (exact, unique in the file):
```text
    /// editing again — both <c>/api/time-entries</c> and Skema accept writes into a REJECTED period,
```

new_str:
```text
    /// editing again — the Skema save, the one registration writer, accepts writes into a REJECTED period,
```


## Authorized file list for the sweep agent

Only these 26 files, only the blocks above, comments only:

- `src/Backend/StatsTid.Backend.Api/ApprovalPeriodSaveLock.cs` (LF): S-1, S-2
- `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` (CRLF): S-3, S-4, S-5, S-6, S-7, S-8
- `src/Backend/StatsTid.Backend.Api/Services/EmploymentWindowGate.cs` (LF): S-9, S-10, S-11
- `src/Infrastructure/StatsTid.Infrastructure/UserRepository.cs` (CRLF): S-12, S-13
- `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs` (CRLF): S-14, S-15, S-16, S-17
- `src/Backend/StatsTid.Backend.Api/Endpoints/CalendarEndpoints.cs` (CRLF): S-18
- `src/Backend/StatsTid.Backend.Api/Endpoints/Helpers/DepartedSubjectWriteFloor.cs` (CRLF): S-19
- `src/Infrastructure/StatsTid.Infrastructure/IEmploymentWindowResolverInTx.cs` (CRLF): S-20
- `src/Infrastructure/StatsTid.Infrastructure/EmploymentWindowResolver.cs` (CRLF): S-21
- `src/Infrastructure/StatsTid.Infrastructure/EmploymentWindowStrandCheck.cs` (CRLF): S-22
- `src/Backend/StatsTid.Backend.Api/Endpoints/EmploymentDateEndpoints.cs` (CRLF): S-23
- `docker/postgres/init.sql` (CRLF): S-24
- `tests/StatsTid.Tests.Regression/Approval/OrphanCoverageTests.cs` (CRLF): S-25, S-26, S-27
- `src/Infrastructure/StatsTid.Infrastructure/ApproverCoverage.cs` (CRLF): S-28
- `tools/StatsTid.DemoSeed/Generation/AllocatedMonthBuilder.cs` (CRLF): S-29
- `tools/StatsTid.DemoSeed/Model/DemoManifest.cs` (CRLF): S-30, S-31
- `tests/StatsTid.Tests.Regression/Outbox/SkemaWorkTimeDayBoundsGuardTests.cs` (CRLF): S-32
- `tests/StatsTid.Tests.Regression/Approval/RejectedMonthVisibilityTests.cs` (CRLF): S-33
- `tests/StatsTid.Tests.Regression/Contracts/S120SkemaSpecRuntimeTests.cs` (CRLF): S-34, S-35
- `tests/StatsTid.Tests.Regression/Outbox/SkemaEntitlementEligibilityGuardTests.cs` (CRLF): S-36
- `tests/StatsTid.Tests.Regression/Outbox/ForcedRollbackHarness.cs` (CRLF): S-37
- `tests/StatsTid.Tests.Regression/Outbox/TimeProjectionAtomicTests.cs` (CRLF): S-38, S-39
- `tests/StatsTid.Tests.Unit/Events/S148ConfigStageEventRoundTripTests.cs` (CRLF): S-40
- `tests/StatsTid.Tests.Regression/Config/ConfigDateValidationTests.cs` (CRLF): S-41a, S-41b, S-41c
- `src/Infrastructure/StatsTid.Infrastructure/PayInputsLock.cs` (CRLF): S-42
- `tests/StatsTid.Tests.Regression/Skema/SkemaCellClearRaceTests.cs` (CRLF): S-43a, S-43b

Not authorized: everything else, including `SqlEmitter.cs`, `api-typed-overloads.test.ts` (X-1), `SendCheckIntervalOnlyTests.cs` (X-2) and `TeamOverviewAggregateTests.cs` (C-1) unless the Orchestrator rules them in.

## Expected effect

- **Behaviour:** none. Every edit is inside `//`, `///` or `--` comment text; S-33 inserts a comment line and leaves the declaration byte-identical.
- **Build:** warnings and CA2100 unchanged. No project sets `GenerateDocumentationFile` (`git grep GenerateDocumentationFile` finds only planning text), so doc comments are not compiled; no block adds a `cref` (S-1, S-9 and S-31 keep the ones already there).
- **Tests:** counts unchanged, and no test is renamed (X-1 is excluded). Tests that read source text either strip comments (`BusinessDateCalendarGuardTests`, `ToleranceAllowListTests`, `AccrualMathSingleSourceTests`) or match strings no block touches (`RequireAuthorization("…")`, unit-table names, `payroll_export_records`, repository names under `src/Integrations`).
- **Generated and pinned outputs:** none move. The DemoSeed edits (S-29 to S-31) are C# doc comments on a builder and on manifest properties, not emitted text. The `init.sql` edit (S-24) is a trailing comment: `tools/generate_db_schema.py` strips comments before it builds `docs/generated/db-schema.md` (`:42-43`, `:92`, `:159`), and `HarnessDdlMatchesInitSqlTests` compares the catalog, not comments (`:42`). `docs/api/openapi.json` takes no text from comments.
- **CI checks:** no `shortcut(` text is added or removed, so `tools/check_docs.py`'s shortcut rule (`:190-191`) is unaffected.

## Revision history

| Version | Date | Change |
|---|---|---|
| 1 | 2026-11-25 | First draft (C-1), from the brief's sources and the brief's grep; every item re-found at `6ab643a7`. |

---

## Orchestrator rulings, 2026-11-25 (bind the sweep agent)

1. **X-1 (the phase-pin title): IN.** The title states counts that are false (36/21/13 against the 41/25/12 the unions hold). The count-free title is true. No filter, pin or golden names this title, so nothing regenerates. Apply X-1's block.
2. **X-2 (`SendCheckIntervalOnlyTests.cs:17`): NOT APPLIED.** The ADR-028 D4 amendment is written in the close's docs batch, which makes the current text true. Leave the file untouched.
3. **C-1 (`TeamOverviewAggregateTests.cs:552`): IN.** It is the test-side twin of S-14 and is false now. Apply C-1's block.
4. **N-1 (S-27, S-28): IN,** confirmed. It comes from the same "Owed" list as N-3.
5. **`init.sql` (S-24): IN.** A comment-only edit of a protected file, authorized by the Orchestrator (ruling 1 of the wave-1 briefs); it affects no generated output.
6. **`SkemaEndpoints.cs:1043-1045`: LEFT.** The current text is true now: it says there is no check (1), why, and why the labels are kept.
7. **`SqlEmitter.cs:202/230`: EXCLUDED,** confirmed. The fix waits until the emitter is next touched, because it regenerates the golden and the committed demo file.

**The sweep agent applies:** every S-block, plus X-1 and C-1. It applies no other X- or C-block and nothing in the not-applied lists.
