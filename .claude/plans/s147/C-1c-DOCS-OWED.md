# C-1c — TASK-14725's records (Step 7a W-1, the manager's day 6 by date): apply-ready text

| | |
|---|---|
| **What this is** | The exact text for the records of TASK-14725, the fix for S147 Step 7a cycle 1's W-1. Sources: the owner's rulings of 2026-11-05, "(a) Fix it now" and then "(a) Extend now"; the records Fable's review lists as owed (NOTE 9 in `.claude/reviews/S147-TASK-14725-5a-reviewer.md`); and the Orchestrator's brief. The Orchestrator applies each block as a string replacement, into K′. The `planner` drafts; the Orchestrator applies. Every target is under `docs/**` |
| **Status** | DRAFT 1 (`planner`, Opus 5.5), 2026-11-05 |
| **Base commit** | `bf3235b1` (local master). Every Old text below is copied from `bf3235b1`. A script counted each one as an exact substring over the whole target file, in the commit's blob and in the working copy, and each count is 1. Every docs line number is at `bf3235b1`. Code lines in the New texts are at `0a64c1eb`, the TASK-14725-T head in the worktree; the -T fix-up still to come changes tests only, so the `src` lines hold at the merge (re-check if it touches `src`) |
| **Orchestrator model** | S147: Opus 5.5 seat; drafting `planner` (Opus 5.5); judgment `adjudicator` / `reviewer` on Fable 5.1 |
| **Timing** | Apply after TASK-14725 merges, with K′, before C-2 runs on K′. Fill `<14725 merge>`, `<14725 GATE>`, `<14725 tip>` and the cycle-2 tokens when applying. The C-2 placeholders stay literal until C-2 on the final candidate is green |
| **Owner approval** | None needed. No block touches `SYSTEM_TARGET.md` |

## What this is, in plain words

The owner ruled (OQ-2) that a leaver's own manager may finish the final month for 5 days after the last working day, and that from day 6 only HR may act. Step 7a's first cycle found that "from day 6" was not decided by the date. It waited for the leaver job, which switches the person off on its next poll, every 5 minutes, or never if the job fails. Until then the manager could still approve, reject or reopen the month and read the leaver's data. Both lenses found it (Fable W-1, Codex WARNING 1).

The owner ruled "(a) Fix it now". The first fix closed the manager's own paths. The implementer then found that a leader whose organisation scope covers the leaver still reached the leaver's reads and overtime decisions the same way. The owner ruled "(a) Extend now". TASK-14725 therefore decides the cut-off by the date on two layers:
- the shared approver check refuses every manager path from day 6;
- the general organisation check treats a person past the grace as switched off.

The leaver job's switch-off still runs, as a second guard and for its events. HR keeps every route it uses to finish the month.

The records follow:
- **ADR-040's amendment** gains a dated note, plus pointers from the four places that tied day 6 to the switch-off.
- **`docs/SECURITY.md`** says the date decides.
- **The security register** gets SEC-058, registered and closed in one step. SEC-057's citations are refreshed.
- **HRP-011** says the date decides.
- **The performance register** records the one extra read per manager–employee pair (F7).
- **`QUALITY.md`'s S147 grades** gain one clause naming SEC-058.
- **The sprint log** gets the task's ledger row, its merge-record row and its plain-words record, Step 7a's cycle-1 record, the two rulings, and the next steps.

Rules kept:
- **Nothing is described as built unless it is merged.** Everything here is applied after TASK-14725 merges, at `<14725 merge>`.
- **CI-verified only with a run id.** TASK-14725's Docker facts say "CI-pending (C-2)" until the run lands, by Step 7a's own W-2.
- **Earlier records get dated notes; they are not rewritten.** ADR-040's amendment of 2026-11-03 keeps its text; it gains pointers and one dated note. `SECURITY.md`, the HRP row and the handoff block describe the current state, so they are updated, each with an S147 Step 7a marker.

## How to apply

- Each block names its **Target**, the **Old text** (one exact match at `bf3235b1`) and the **New text**. Where a block has parts, apply each part as its own replacement.
- No two replacements in one file overlap. Several share a line as disjoint substrings: 3b and 3c in SEC-057's row (3a inserts after that row's end).
- **Line endings.** The blobs are LF everywhere. In the working copy every target is LF (a carriage-return count of 0) except **`docs/operations/performance-finding-register.md`, which is CRLF** (292 carriage returns in 292 lines; `core.autocrlf = true`). Its Old texts are single lines and match either way; write Block 5's multi-line New texts with CRLF. The LF targets: ADR-040, `docs/SECURITY.md`, `security-finding-register.md`, `hr-follow-up-process-register.md`, `docs/QUALITY.md`, `docs/sprints/SPRINT-147.md`.
- **Placeholders.**

| Token | Filled with | Occurs in |
|---|---|---|
| `<14725 merge>` | the TASK-14725 merge commit's short sha | 1d, 3a, 3b, 3c, 4, 6a, 6b, 6d, 6f, 6g, 6h, 6j, 6l |
| `<14725 GATE>` | the GATE after the merge, in the log's form ("build 0 / 144 / CA2100 114; Unit …; DemoSeed 170; Regression non-Docker …") | 3a, 6a, 6b |
| `<14725 tip>` | the branch tip that merged (after the -T fix-up) | 6b |
| `<14725 c2 Fable>`, `<14725 c2 Codex>` | each lens's cycle-2 verdict on the fix-up | 6b |
| `<C-2 run id>`, `<C-2 date>` | as in C-1 and C-1b: filled only when C-2 on the final candidate is green | `<C-2 run id>`: 1d, 3a, 6a, 6g; `<C-2 date>`: 6g |
| `<K sha>` | as in C-1: K′'s sha | 6g, 6h |
| `<7a cycles>` | as in C-1: Step 7a's final cycle count | 6d |

- **Blocks 1–7 are owed.** None needs the owner.

## Map: each owed item → its block

| # | Owed item (source) | Block | State at `bf3235b1` |
|---|---|---|---|
| 1 | ADR-040's amendment: the D+6 row; the `:430` sentence; HR at D+6; PAT-028 widened; the employed-month bound on the flag; `/remove` only to D+5 (brief item 1; Fable NOTE 1–4, NOTE 9) | 1a (the table, `:361`), 1b (`:430`), 1c (`:483`), 1e (`:376`), 1d (the dated note, after `:507`) | the cut-off described as the switch-off's |
| 2 | `docs/SECURITY.md` `:120`, `:141-146`, `:153-161`, `:169` (brief item 2; Fable NOTE 1, 3, 5) | 2a–2e | as above |
| 3 | A new SEC row, Low, registered and closed by TASK-14725; SEC-057's citations and its subject/actor note (brief item 3; Fable NOTE 4, NOTE 9) | 3a (SEC-058), 3b, 3c | no row; SEC-057 cites `:409-420, :689-699, :1913-1925` |
| 4 | HRP-011 (`:117`) by date (brief item 4; Fable NOTE 4) | 4 | "the leaver job switches the person off" |
| 5 | A performance F-row (brief item 5; Fable NOTE 6) | 5a (the table), 5b (the section) | none |
| 6 | The sprint log: ledger row; merge-record row; "In plain words (TASK-14725)"; Step 7a cycle 1; the owner rulings; Phase and Next action (brief item 6) | 6a, 6b, 6c, 6d, 6e, 6f, 6g; with 6h (As of), 6i (Open rulings), 6j (header Status), 6k (C-2's first-run row), 6l (the acceptance table's renamed facts) | none |
| 7 | `QUALITY.md`, only if the S147 security text needs a line (brief item 7) | 7 | it lists SEC-055 and SEC-057 as registered; SEC-058 is missing |

**Count: 29 replacements** (1a–1e, 5; 2a–2e, 5; 3a–3c, 3; 4; 5a, 5b; 6a–6l, 12; 7).

**New ids: SEC-058 and F7.** How checked:
- **SEC-058.** The SEC register's last row is SEC-057 (`security-finding-register.md:217`). `git grep "SEC-05[89]\|SEC-06[0-9]"` at `bf3235b1` finds SEC-058 only in `.claude/plans/s147/O-7-DOCS-OWED.md:74`, `:791`, where it was a declined alternative; no row was ever written.
- **F7.** The performance register's table ends at F6 (`performance-finding-register.md:30`). The other "F7" strings in the tree are review-finding labels in old sprint logs (`SPRINT-127.md:585`, `SPRINT-131-consolidated-findings.md`, `SPRINT-133.md:106`), not performance ids.

No new QUAL or HRP id.

---

## Block 1 — ADR-040: pointers, and the dated note "S147 Step 7a, 2026-11-05 (TASK-14725)"

**Target:** `docs/knowledge-base/decisions/ADR-040-employee-timeline-and-as-of-resolution.md` (LF; the "Amendment 2026-11-03", lines 344–507). Five parts. 1a, 1b, 1c and 1e add short pointers where the amendment ties day 6 to the switch-off; 1d appends the note after the amendment's last line.

### 1a — the table's D+6 row (line 361)

Old text:

````text
| **D+6 on** | no access | switched off by Step A (the leaver job's switch-off step) | refused | HR approves, rejects, reopens and views the month; payroll's LocalAdmin pays it |
````

New text:

````text
| **D+6 on** | no access | switched off by Step A (the leaver job's switch-off step) | refused *(by date since S147 Step 7a, whether or not Step A has run; see the note at the end)* | HR approves, rejects, reopens and views the month; payroll's LocalAdmin pays it *(from D+6 by date; see the note)* |
````

### 1b — "The manager's edge fails closed" (line 430)

Old text:

````text
The manager's edge fails closed for a switched-off subject, so the manager is refused from D+6.
````

New text:

````text
The manager's edge fails closed for a switched-off subject, so the manager is refused from D+6. *(S147 Step 7a, 2026-11-05: as built here, that rested on Step A's switch-off; since TASK-14725 the manager is refused from D+6 by date, on two layers. See the note at the end of this amendment.)*
````

### 1c — the PAT-028 residual (line 483)

Old text:

````text
self-service sites re-check in-lock.
````

New text:

````text
self-service sites re-check in-lock. *(Widened by TASK-14725: the validator now also reads the date for every subject with a recorded end date; see the note at the end of this amendment.)*
````

### 1e — "from D+6, who may act for them" (line 376)

Old text:

````text
from D+6, who may act for them.
````

New text:

````text
from D+6, who may act for them *(decided by date since S147 Step 7a; see the note at the end of this amendment)*.
````

### 1d — the dated note (appended after line 507, the amendment's and the file's last line)

Old text:

````text
`DepartedApproverTests` and `RecalculateDepartedSelfTests`; and the screen's `FollowUpLists.leaverActions.test.tsx`.
````

New text:

````text
`DepartedApproverTests` and `RecalculateDepartedSelfTests`; and the screen's `FollowUpLists.leaverActions.test.tsx`.

**S147 Step 7a, 2026-11-05 (TASK-14725): the manager's day 6 is decided by date.**

*Why, in plain words.* The table above says the manager is refused from D+6. As built by TASK-14713, that refusal
rested on Step A. The manager's paths and every leader-tier org-scope check read `is_active`, and only Step A,
polled every 5 minutes, switches the leaver off. So between Copenhagen midnight on D+6 and the next poll, or for as
long as the job failed, the leaver's own manager could still approve, reject or reopen the month and read the leaver's
data. Both Step 7a lenses found it (cycle 1: Fable W-1, Codex WARNING 1); it is registered as SEC-058 (fixed in code; it closes when C-2 runs its Docker pins green).
The owner ruled "(a) Fix it now". When the first commit showed that the leader-tier org-scope reads had the same
window, the owner ruled "(a) Extend now" (both 2026-11-05). TASK-14725 (merged `<14725 merge>`) decides the cut-off
by the date on two layers. Step A's switch-off stays, as a second guard and as the source of the switch-off events
(K-4). Code lines below are at `0a64c1eb`.
- **Layer 1, the shared approver predicate.** Once a manager path has admitted the actor (the designated edge, the
  admin-assigned acting manager, the edge manager's stand-in, the unit leader or the unit leader's stand-in),
  `DesignatedApproverAuthorizer` refuses when the subject's last day employed lies before the authority date minus 5,
  that is from D+6 (`DesignatedApproverAuthorizer.cs:401`; the rule `IsPastManagerGrace`, `:422`, over
  `EmploymentEndDateLifecycleWriter.IsPastGrace`, the one grace constant). Every production authority read calls
  this predicate: approve, reject and reopen's leader arm, the allocation breakdown, the compliance and Skema edge
  arms, my-reports, Teamoversigt and HR's tree counts. So the pending lists and the actions still agree (see = act).
  The refusal is date-free; the server log names the clause `manager-grace-ended`.
- **Layer 2, the org-scope validator.** A still-active subject past the grace by date is treated as switched off
  (`OrgScopeValidator.cs:600-602`).
  - The active-only overload answers "Target employee not found" to every actor (`:160-161`).
  - The terminated-inclusive overload admits it only through a LocalHR-or-above scope (`:349-350`, `:356`, `:385`),
    exactly as a deactivated subject (TASK-14713's rule).
  - The departed-self clause still runs first (`:150-151`, `:339-340`). The log names the clause
    `past-grace-as-switched-off`.
  - Without this layer, a leader whose scope covers the leaver's organisation would still reach the leaver's reads
    and the overtime decisions until the switch-off. On approve, reject and reopen the validator's arm is floored at
    LocalHR, so there the shared predicate alone decides a manager's case.
- **The D+6 row, as built now.** The manager is refused from Copenhagen midnight on D+6, on both layers, whether or
  not Step A has run.
  - HR keeps every route it uses to finish the month: they are terminated-inclusive (TASK-14713's five surfaces and
    the R9c inventory), and payroll's `/calculate-and-export` still admits LocalAdmin (`PayrollPastGraceLeaverScopeTests`).
  - On the active-only routes (33 by the task's census, verified by Fable's review), HR gets at D+6 the answer it
    gets after the switch-off, "Target employee not found". Nothing changes for HR once the job has run; only the
    moment moves from the poll to the date.
  - The leader year-overview reads stay null-floored by design, but a switched-off target is admitted only through
    a LocalHR-or-above scope (`OrgScopeValidator.cs:385`). From D+6 a leader therefore no longer sees a leaver's
    year overview; HR still does.
- **Residuals recorded, not fixed.**
  - **PAT-028, widened.** The validator reads the Copenhagen date at most once per call, and now does so for every
    subject with a recorded end date, not only when the identity matches (`OrgScopeValidator.cs:145`, `:332`; the
    class remark `:48-54`). The handler may read the date again. Traced: for a request that straddles midnight,
    the outcome equals that of a request wholly on one of the two days, so a straddle cannot admit an actor whom
    neither day admits.
  - **The employed-month bound still keys on the flag** (`ApprovalEndpoints.cs:71`; Payroll `Program.cs:876`).
    Between D+6 and the switch-off it does not fire. That is not exploitable: the send already refuses a month with
    no employed day (`ApprovalEndpoints.cs:2473-2480`), so no such month reaches approval or payroll. It is a
    consistency residual.
  - **`/remove` of a leaver whose switch-off was missed works only to D+5.** `/remove` reads the person through the
    active-only check, so from D+6 by date it answers "not found", as it does for a switched-off person. HR reassigns
    the leaver's reports one by one through `POST /api/admin/reporting-lines`, whose check reads the report, not the
    leaver (`ReportingLineEndpoints.cs:87`, `:108`). Step A closes the leaver's own edges when it runs.
  - **One extra read per admitted manager–employee pair** on my-reports and Teamoversigt: performance register F7.
- **Pinned by** the Unit facts `ManagerGracePredicateTests` (7; mutation M-26 trips 3 of them) and the Docker facts
  `LeaverPastGraceByDateTests`, the four D+5 and D+6 facts in `DepartedSelfValidatorTests`, and
  `PayrollPastGraceLeaverScopeTests`. Each Docker fact keeps the leaver active at D+6, with the leaver job off, and
  has D+5 boundary pins. The Docker facts are CI-pending until C-2 (`<C-2 run id>`).
````

## Block 2 — `docs/SECURITY.md`: the date decides day 6

**Target:** `docs/SECURITY.md` (LF; lines 120, 141, 146, 154, 169). Five parts.

### 2a — the validator reads the date for two clauses (line 120)

Old text:

````text
The validator reads the date from the injected `TimeProvider` (`CopenhagenBusinessDate.Today`).
````

New text:

````text
The validator reads the date from the injected `TimeProvider` (`CopenhagenBusinessDate.Today`), at most once per call. *(S147 Step 7a, TASK-14725: it reads it for two clauses, this departed-self clause and the rule that treats a subject past the manager grace by date as switched off (see "HR only from day 6" below). So it now reads the date for every subject with a recorded end date. A request that straddles Copenhagen midnight can see two dates, the PAT-028 residual in ADR-040's amendment; the outcome then equals a request wholly on one of the two days.)*
````

### 2b — "Once Step A has switched the leaver off on day 6" (line 141)

Old text:

````text
Once Step A has switched the leaver off on day 6, five surfaces admit the switched-off subject through the terminated-inclusive validator (`ValidateEmployeeAccessIncludingTerminatedAsync`):
````

New text:

````text
From day 6, decided by date (S147 Step 7a, TASK-14725: whether or not Step A has switched the leaver off yet; Step A's switch-off stays as a second guard), five surfaces admit the leaver through the terminated-inclusive validator (`ValidateEmployeeAccessIncludingTerminatedAsync`), which treats a subject past the grace by date as switched off (`OrgScopeValidator.cs:349-350`):
````

### 2c — who is refused at day 6 (line 146)

Old text:

````text
The manager's own edge and any leader-scope token are refused.
````

New text:

````text
The manager's own edge and any leader-scope token are refused, from Copenhagen midnight on day 6 by date. The shared approver predicate refuses every manager path (edge, acting manager, stand-in, unit leader; `DesignatedApproverAuthorizer.cs:401`), and the validator admits only a LocalHR-or-above scope (S147 Step 7a, SEC-058, CLOSED). HR keeps every route it uses to finish the month; on the active-only routes HR gets from day 6 the answer it gets after the switch-off, "Target employee not found".
````

### 2d — the poll lag is now only the leaver's own exposure (line 154)

Old text:

````text
That is D+1 to D+5 by design (the manager's grace), plus any poll lag.
````

New text:

````text
That is D+1 to D+5 by design (the manager's grace), plus any poll lag. *(Since S147 Step 7a (TASK-14725), the poll lag matters only for the leaver's own exposure, below. The manager's authority and every leader-tier org-scope check end at day 6 by date, whatever the flag says (SEC-058).)*
````

### 2e — the leader year overview (line 169)

Old text:

````text
**Exception, deliberately null-floored:** the leader year-overview reads (`BalanceEndpoints`) — a Leader-in-scope SHOULD see their active report's overview (ADR-027 R9c).
````

New text:

````text
**Exception, deliberately null-floored:** the leader year-overview reads (`BalanceEndpoints`) — a Leader-in-scope SHOULD see their active report's overview (ADR-027 R9c). *(A leaver past the grace is not an active report. From day 6 by date the validator treats them as switched off, and a switched-off target is admitted only through a LocalHR-or-above scope (`OrgScopeValidator.cs:385`), so a leader no longer sees a leaver's year overview; HR still does. S147 Step 7a, TASK-14725.)*
````

## Block 3 — The security register: SEC-058, and SEC-057 refreshed

**Target:** `docs/operations/security-finding-register.md` (LF; line 217, the SEC-057 row). Three parts. 3a inserts SEC-058 after SEC-057; 3b and 3c are disjoint substrings of SEC-057's row.

*Why a row, registered and closed in one step:* the brief asks for one, and Fable's review agrees (NOTE 9). Registering it keeps the finding findable by a register sweep, and it sits beside SEC-057, its sibling on the actor side. The CLOSED status follows the brief; the open point below gives the alternative wording "fixed in code; closes at C-2".

### 3a — SEC-058 (after SEC-057, the end of line 217)

Old text:

````text
Flip the B pins as intentional. |
````

New text:

````text
Flip the B pins as intentional. |
| SEC-058 | **A leaver's manager kept their authority past day 5 until the leaver job switched the leaver off.** From D+6 only HR may act on a leaver's final month (owner OQ-2; TASK-14713), but the refusal rested on `is_active`. The shared approver predicate admitted the manager's paths (the designated edge, the acting manager, a stand-in, a unit leader) for any active subject, and every leader-tier org-scope check read the active flag. Only Step A of the leaver job flips the flag on D+6, and it is polled every 5 minutes (`SettlementCloseService.cs:458-548` at `c13024cd`; `docs/SECURITY.md`, "The active-but-departed window"). So from Copenhagen midnight on D+6 until the next poll, or for as long as the job failed, the leaver's own manager could still approve, reject or reopen the final month (`ApprovalEndpoints.cs:409-420`, `:689-699`, `:1913-1925` at `c13024cd`) and read the leaver's data. The D+6 facts ran Step A first (`LeaverHrOnlyTests.cs:23`, `:67-68`), so the window was unpinned. Found by both lenses at S147 Step 7a, cycle 1 (Fable W-1, Codex WARNING 1). | Manager's day-6 cut-off enforced by the switch-off job, not by date | S147 Step 7a cycle 1 (Fable W-1 = Codex WARNING 1), 2026-11-04 | Low (only the leaver's own manager or unit leader, only for one poll interval or while the job fails; four-eyes still applied; the grace itself is owner-ruled) | A01 Broken Access Control / Elevation of privilege | **FIXED IN CODE (S147, TASK-14725, merged `<14725 merge>`); closes when C-2 runs its Docker pins green (`<C-2 run id>`):** the manager loses authority at D+6 by date, on two layers. The shared approver predicate refuses every manager path over a subject past the grace (`DesignatedApproverAuthorizer.cs:401`, the rule `:422`). `OrgScopeValidator` treats such a subject as switched off: the active-only check answers "Target employee not found", and the terminated-inclusive check admits only LocalHR or above (`OrgScopeValidator.cs:160-161`, `:349-350`, `:600-602`). Step A's switch-off stays as a second guard. Owner rulings 2026-11-05: "(a) Fix it now", then "(a) Extend now". The Unit pin ran at the merge gate (`<14725 GATE>`); the Docker pins are CI-pending until C-2 (`<C-2 run id>`, filled in when green) | AS-FOUND (at `c13024cd`): `ApprovalEndpoints.cs:409-420, :689-699, :1913-1925`; `SettlementCloseService.cs:458-548`; `docs/SECURITY.md:153-154`; `LeaverHrOnlyTests.cs:23`. AS-CLOSED (at `0a64c1eb`): `DesignatedApproverAuthorizer.cs:401`, `:422`, `:443-458`; `OrgScopeValidator.cs:160-161`, `:349-350`, `:385`, `:600-602`; pins `ManagerGracePredicateTests` (Unit, mutation M-26), `LeaverPastGraceByDateTests`, `DepartedSelfValidatorTests` (the four D+5/D+6 facts), `PayrollPastGraceLeaverScopeTests` | → SPRINT-147 (Step 7a cycle 1; the TASK-14725 merge record); ADR-040's amendment, the S147 Step 7a note; `docs/SECURITY.md` § "HR only from day 6". Residuals, recorded there: the employed-month bound still keys on the flag (consistency only; the send refuses a month with no employed day); `/remove` of a leaver whose switch-off was missed works only to D+5; one extra read per admitted pair (performance F7). Sibling: SEC-057, the actor side, unchanged |
````

### 3b — SEC-057's status: the subject side is date-driven, the actor side unchanged

Old text:

````text
REGISTERED (S147, 2026-11-01); revisit, not shield: the B pins record today's behaviour, not a protection
````

New text:

````text
REGISTERED (S147, 2026-11-01); revisit, not shield: the B pins record today's behaviour, not a protection. **S147 Step 7a note (TASK-14725, merged `<14725 merge>`):** the *subject* side is now date-driven: from D+6 a leaver past the grace is refused to every leader-tier path by date (SEC-058, CLOSED). The *actor* side, this row's subject (a departed HR or admin user acting on others with a token minted before they left), is unchanged
````

### 3c — SEC-057's source of truth: the leader arms' lines after TASK-14725

Old text:

````text
`ApprovalEndpoints.cs:409-420, :689-699, :1913-1925`; `OvertimeEndpoints.cs:360`, `:458`; `JwtValidationSetup.cs:60`;
````

New text:

````text
`ApprovalEndpoints.cs:409-420, :689-699, :1913-1925` (as registered; since TASK-14725, merged `<14725 merge>`: `:417-428`, `:699-709`, `:1924-1936`); `OvertimeEndpoints.cs:360`, `:458`; `JwtValidationSetup.cs:60`;
````

## Block 4 — HRP-011: from day 6 by date

**Target:** `docs/operations/hr-follow-up-process-register.md` (LF; line 117, the HRP-011 row). The hand-over text ("manager until D+5, HR only from D+6") is unchanged.

Old text:

````text
**From D+6** the leaver job switches the person off and only HR may act:
````

New text:

````text
**From D+6** only HR may act, decided by the date (S147 Step 7a, TASK-14725, merged `<14725 merge>`: whether or not the leaver job has switched the person off yet; the job's switch-off stays as a second guard. Before TASK-14725 the hand-over waited for the job's poll, SEC-058. If the job missed the switch-off, `/remove` of the leaver works only to D+5; from D+6 HR reassigns the reports one by one):
````

## Block 5 — The performance register: F7

**Target:** `docs/operations/performance-finding-register.md` (blob LF; **working copy CRLF**). Two parts. 5a adds the row after F6 (line 30); 5b adds the detail section before "## Standing convention" (line 288).

### 5a — the row (after line 30)

Old text:

````text
| F6 | Loading-flash perception | **OPEN — the S126 measurement was RETRACTED** (harness measured full page loads, and its stop condition was true before the route mounted). Chunk sizes valid; durations and the 250 ms threshold are not. |
````

New text:

````text
| F6 | Loading-flash perception | **OPEN — the S126 measurement was RETRACTED** (harness measured full page loads, and its stop condition was true before the route mounted). Chunk sizes valid; durations and the 250 ms threshold are not. |
| F7 | One extra single-row read per admitted manager–employee pair on the my-reports list and Teamoversigt (S147, TASK-14725) | **OPEN — accepted, revisit** (S147 Step 7a fix; Fable's TASK-14725 review, NOTE 6). See below |
````

### 5b — the section (before line 288)

Old text:

````text
## Standing convention
````

New text:

````text
## F7 — the manager-grace read per admitted pair (S147, recorded 2026-11-05)

**What it is.** TASK-14725 made a leaver's manager lose authority at D+6 by date (S147 Step 7a; SEC-058). Once a
manager path has admitted the actor, the shared approver predicate asks one more question: is the subject past the
grace? On the live paths that is one more single-row read by primary key, `users` by `user_id` joined to its
organisation (`DesignatedApproverAuthorizer.cs:443-458`, `QueryPastManagerGraceAsync`, at `0a64c1eb`). Two list
reads call the predicate once per admitted pair: the manager's my-reports list (`ApprovalPeriodRepository.cs:464`)
and Teamoversigt (`:581`). Each therefore gains one read per employee the manager may act on. HR's tree counts
(`:915`) use the prefetched authority facts, which now carry the end date inside the existing four queries
(`IAuthorityFactsSource.cs`), so they are unchanged.

**Size.** Linear in the manager's own team, and each read is a primary-key lookup. Not the F1 class, which was an
organisation-wide fan-out. Not measured.

**Disposition: accepted, revisit.** The tidy option: let the pair's existing read, `SameOrganisationAsync`
(`DesignatedApproverAuthorizer.cs:706`), carry the end date, so the pair costs one read instead of two. Revisit if a
team grows large, or if one of these lists moves onto a hot path.

## Standing convention
````

## Block 6 — The sprint log

**Target:** `docs/sprints/SPRINT-147.md` (LF). Twelve parts. 6a–6g are the brief's items; 6h–6l keep the rest of the log consistent with them.

### 6a — the ledger row (before line 112, the conditional-tasks row)

Old text:

````text
| TASK-14701-F, TASK-14709-N, TASK-14710-F, TASK-14712-F | — | — |
````

New text:

````text
| TASK-14725 / 14725-T | Step 7a fix (C-3 cycle 1; owner 2026-11-05) | backend-infrastructure / test-qa | Step 7a W-1 (both lenses): a leaver's manager loses authority at D+6 by date, in the shared approver predicate and in `OrgScopeValidator` (fix-up 1, owner "(a) Extend now"); SEC-058 | **DONE** (merged `<14725 merge>`; GATE `<14725 GATE>`); Docker facts **CI-pending (C-2)**; the run id `<C-2 run id>` is filled in when C-2 on the final candidate is green |
| TASK-14701-F, TASK-14709-N, TASK-14710-F, TASK-14712-F | — | — |
````

### 6b — the merge-record row (after line 178, TIDY-4's row)

Old text:

````text
| c1 2 N → c2 1 W (a cancelled refetch strands the list) → c3 CLEAN → c4 CLEAN | `46f3f82a` |
````

New text:

````text
| c1 2 N → c2 1 W (a cancelled refetch strands the list) → c3 CLEAN → c4 CLEAN | `46f3f82a` |
| 14725 + 14725-T (+ fix-up 1, owner "(a) Extend now"; -T fix-up 1) | `<14725 tip>` | `<14725 GATE>`. The branch's own gate at `0a64c1eb`: build 0 / 144 / CA2100 114; Unit 1722; DemoSeed 170; Regression non-Docker 135. REGEN: not needed (no route or DTO shape change; one doc comment in `HrFollowUpApprovalResponses.cs`) | PASS | **Fable** c1 READY-TO-MERGE (APPROVED-WITH-WARNINGS: 1 W, the docstrings' "both layers off"; 9 N) → c2 `<14725 c2 Fable>` | c1 0 B / 1 W (the same docstrings) → c2 `<14725 c2 Codex>` | `<14725 merge>` |
````

### 6c — "In plain words (TASK-14725)" (before line 245)

Old text:

````text
*In plain words (14716):* SEC-054's second step, as the owner ruled on OQ-3 (a).
````

New text:

````text
*In plain words (TASK-14725, the Step 7a fix):* a leaver's own manager may finish the final month for 5 days after the last working day; from day 6 only HR may act (owner OQ-2). Step 7a found that "from day 6" was not decided by the date. It waited for the leaver job to switch the person off, which happens on its next poll, every 5 minutes, or not at all if the job fails. Until then the manager could still approve, reject or reopen the month and read the leaver's data. Now the date decides, on both layers that admit a manager. The shared approver check refuses every manager path from day 6, and the general organisation check treats a person past the grace as switched off. The job's switch-off still runs, as a second guard and for its events. HR loses nothing it needs: every route it uses to finish a leaver's month admits a switched-off person, and on the other routes it gets at day 6 the answer it gets after the switch-off. **Dispositions:**
- **Owner, 2026-11-05: W-1 = "(a) Fix it now"** (Step 7a cycle 1, Fable W-1 = Codex WARNING 1). The internal reviewer's lean had been to record it now and enforce it in S148 stage 0.
- **Owner, 2026-11-05: "(a) Extend now."** The first commit (`527b7c41`) put the date rule in the shared approver predicate only. The implementer stopped and reported that a leader whose organisation scope covers the leaver still reached the leaver's reads and the overtime decisions through the general check until the switch-off. Fix-up 1 (`872ba261`) extends the rule into `OrgScopeValidator`.
- **Registered:** SEC-058 (Low), fixed in code; it closes when C-2 runs its Docker pins green. SEC-057's citations are refreshed; its actor side is unchanged.
- **Fable c1 WARNING 1 = Codex c1 WARNING.** The new Docker facts' docstrings and the report said the approve and Skema facts turn red only with both layers off. In fact each turns red with either layer off. On approve, the LocalHR-floored validator never admits a leader, so the predicate alone decides; on the Skema month view, either arm alone admits. Fixed in -T fix-up 1.
- **Fable NOTE 1–4** are recorded in ADR-040's amendment (the S147 Step 7a note), `docs/SECURITY.md` and HRP-011: the PAT-028 residual widened, the employed-month bound still on the flag, HR's routes at day 6, and `/remove` only to day 5.
- **Fable NOTE 5:** -T fix-up 1 adds a day-6 reject fact and a day-6 year-overview fact to `LeaverPastGraceByDateTests`.
- **Fable NOTE 6:** performance register F7 (accepted, revisit).
- **Fable NOTE 7:** the authority-facts test double (`Regression/Approval/DesignatedApproverCopenhagenDayTests.cs:276-289`) inherits the new member's fail-closed default. It stays green; a one-line override would make it honest. A follow-up, not done.
- **5α observations.** The interface's default member returns true, so it fails closed by design. `ApprovalPeriodRepository.cs:70` builds the authorizer without a logger: the decision is identical, but that fallback path writes no `manager-grace-ended` line. The validator logs `past-grace-as-switched-off` at WARNING, which may be noisy. All three recorded, none changed.
- **One existing fact re-dated:** `Remove_ActorRemovesGraceLeaver_AsToday` used the real clock, so its leaver was far past the grace. It is pinned to D+2 (`WithFixedToday(2026-03-06)`), with its assertions unchanged.
- **C-1b's three day-1 test names are renamed** (TASK-14725-T): `R1_Boundary_EndDateToday_NoFlip_Day1ToDay5_NoFlip_Day6_Flips`, `IsDeactivationDue_BoundaryDayAndGraceDays1To5_NotDue_Day6_Due`, `Exclusions_ManualInactive_NullEndDate_BoundaryDay_GraceDays_NotFlipped_Day6_Flipped`.
- **Docker facts CI-pending (C-2).**

*In plain words (14716):* SEC-054's second step, as the owner ruled on OQ-3 (a).
````

### 6d — Step 7a's cycle-1 record (before line 528, the Handoff block's heading)

Old text:

````text
## Handoff block (written at every gate — the state lives here, not in the conversation)
````

New text:

````text
### Cycle 1 — on K = `c13024cd` (2026-11-04)

| Lens | Model | Verdict | Artifact |
|---|---|---|---|
| External: Codex, base `b510e45b` | Codex (`gpt-6-astra`) | FINDINGS: 0 BLOCKER, 2 WARNING, 2 NOTE | `.claude/reviews/SPRINT-147-step7a-codex-c1.md` |
| Internal: `reviewer` | `claude-fable-5-1` | APPROVED-WITH-WARNINGS: 0 BLOCKER, 4 WARNING, 8 NOTE | `.claude/reviews/SPRINT-147-step7a-reviewer-c1.md` |

*In plain words:* neither lens found a blocker. Both found the same real defect (W-1) and the same record fault (W-2); the rest were record gaps and observations.

| Finding | What | Disposition |
|---|---|---|
| Fable W-1 = Codex WARNING 1 | The manager's day-6 cut-off was enforced by the leaver job's switch-off, not by the date | Owner, 2026-11-05: "(a) Fix it now", then "(a) Extend now" → TASK-14725 (merged `<14725 merge>`); SEC-058 registered (fixed in code; closes at C-2) |
| Fable W-2 = Codex WARNING 2 | "CI-verified" was written before C-2 ran | Fixed in `bf3235b1` (C-1b Blocks 1–2): "CI-pending (C-2)" until the run lands |
| Fable W-3 | The acceptance pins exist, but not under the plan's names | Fixed in `bf3235b1`: "Acceptance → pin" in the C-1 section (C-1b Block 3) |
| Fable W-4 | Governance files the log cites were untracked or modified after K | Fixed in `bf3235b1`: the plan files and the two `.claude` edits committed |
| Fable N-1 = Codex NOTE 1 | `QUALITY.md` said "two different signers"; a missing sender stamp passes payroll's check | Fixed in `bf3235b1` (C-1b Block 4, and the tender analysis's VD 35, X1) |
| Fable N-2 = Codex NOTE 2 | The census re-run on K: 88 routes, 80 in the three hosts; only the four retirements removed | No action: it confirms the C-1 records |
| Fable N-3 | Step 0b's counts moved; their post-build values were unrecorded | Fixed in `bf3235b1` (C-1b Block 5, in the Step 0b record) |
| Fable N-4, N-6, N-7, N-8 | No lock-order cycle; the pins can fail; the records verified against git and GitHub; later tasks' effects on earlier ones recorded | No action |
| Fable N-5 | `S147-TASK-14715-5a-reviewer.md:1` named the lens, not the model id | Fixed in the reviews folder: the header now reads `claude-fable-5-1` |

Cycle 2 runs on K′, which carries TASK-14725 and these records. **Cycles:** `<7a cycles>`.

## Handoff block (written at every gate — the state lives here, not in the conversation)
````

### 6e — the owner rulings in force (line 12, the cell's start)

Old text:

````text
| **Owner rulings in force** | **S147 tidy C-1 = (a)** (2026-11-04, tidy item 7):
````

New text:

````text
| **Owner rulings in force** | **S147 Step 7a W-1, extended = (a)** (2026-11-05, during TASK-14725): "(a) Extend now". The first commit put the date rule only in the shared approver predicate. The implementer stopped and reported that a leader whose organisation scope covers the leaver still reached the leaver's reads and the overtime decisions through the general org-scope check until the switch-off. The owner chose to extend the rule now: `OrgScopeValidator` treats a subject past the grace by date as switched off (TASK-14725 fix-up 1). Offered and not chosen: (b) keep the month fix, register the wider leader-scope gap now and build it in S148 stage 0 with tidy item 21a. HR keeps every route it uses to finish the month; on the active-only routes it gets at day 6 what it got after the switch-off · **S147 Step 7a W-1 = (a)** (2026-11-05): "(a) Fix it now". The manager's day-6 cut-off is enforced by date in S147 (TASK-14725), rather than recorded now and enforced in S148 stage 0, which had been the internal reviewer's lean. SEC-058 is registered, fixed in code, and closes when C-2 runs its Docker pins green · **S147 tidy C-1 = (a)** (2026-11-04, tidy item 7):
````

### 6f — Phase, the head (line 539)

Old text:

````text
| **Phase** | **C-1 committed as K = `<K sha>`** (2026-11-04; see "C-1 — the close candidate's records"). **Next: C-2 on K, then Step 7a (C-3).**
````

New text:

````text
| **Phase** | **Step 7a cycle 1 done on K = `c13024cd`** (2026-11-04; see "Step 7a (C-3)"). Its record fixes are in `bf3235b1`; its W-1 fix, TASK-14725, merged `<14725 merge>` (owner rulings 2026-11-05). **Next: K′ (TASK-14725 and its records), C-2 on K′, Step 7a cycle 2, then the close.** **C-1 committed as K** (2026-11-04; see "C-1 — the close candidate's records").
````

### 6g — Next action (line 542, the whole cell)

Old text:

````text
| **Next action** | (1) **C-2 on K = `<K sha>`** (`PLAN-s147.md:426`): push to `s147-ci-k`; a draft PR whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2; never merge"; one background watch; `headSha` equals K; all seven jobs green. Record `<C-2 run id>`, `<C-2 date>` and the counts in "C-2 — the checkpoint on K"; close the PR unmerged; delete the branch. Red → a fix task → K′ → C-2 again. (2) **C-3, Step 7a**, both lenses, `codex review --base b510e45b`, `reviewed-against-commit: <K sha>`; the reviewer on Fable 5.1, or an Opus stand-in with `.claude/reviews/SPRINT-147-review-floor-WAIVED.md`; each artifact saved before acting; the verdicts into "Step 7a (C-3)". (3) **C-4, the close commit**, a docs-only child of K: every angle-bracket placeholder filled; `git diff --stat K HEAD` lists only `docs/**`; the guard's gates (`docs/WORKFLOW.md:41-47`); the commit message written through a file, never inside the Bash command; push; watch CI to green. (4) **C-5**: the Test Verified line with the close run id; the model-routing register's S147 row (Opus stand-in cycles: 14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2; Fable from 14713 onward); the handoff "closed"; teardown. The Step-0a DEBT branches stay the owner's call. **Outside the docs, before C-4** (not done at `9ca60740`): the tidy pass's own `.claude` edits: the demo skill's `demo_admin` line (`.claude/skills/launch-demo-system/SKILL.md:22`; tidy O-b), the refinement's erratum on T7's skip site (O-c), and in the S148 build input the S0-2 re-pin list and the class D stage-0 inputs (O-f, O-g) |
````

New text:

````text
| **Next action** | (1) **K′**: the TASK-14725 merge (`<14725 merge>`) plus these records (`.claude/plans/s147/C-1c-DOCS-OWED.md`), committed on master; not pushed. (2) **C-2 on K′** (`PLAN-s147.md:426`): push to `s147-ci-k`; a draft PR whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2; never merge"; one background watch; `headSha` equals K′; all seven jobs green. Then fill `<C-2 run id>`, `<C-2 date>` and the counts in "C-2 — the checkpoint on K", and flip every "CI-pending (C-2)" to CI-verified with the run id: the tidy pass's facts, TASK-14725's facts, SEC-058's Docker pins and the index row. Close the PR unmerged and delete the branch. Red → a fix task → K″ → C-2 again. (3) **C-3, Step 7a cycle 2** on K′, both lenses: `codex review --base b510e45b`; `reviewed-against-commit: <K sha>` (K′'s sha); the reviewer on Fable 5.1, or an Opus stand-in with `.claude/reviews/SPRINT-147-review-floor-WAIVED.md`; each artifact saved before acting; the verdicts into "Step 7a (C-3)". (4) **C-4, the close commit**, a docs-only child of the final candidate: every angle-bracket placeholder filled; `git diff --stat` from the final candidate lists only `docs/**`; the guard's gates (`docs/WORKFLOW.md:41-47`); the commit message written through a file, never inside the Bash command; push; watch CI to green. (5) **C-5**: the Test Verified line with the close run id; the model-routing register's S147 row (Opus stand-in cycles: 14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2; Fable from 14713 onward, TASK-14725 and Step 7a included); the handoff "closed"; teardown, TASK-14725's worktree included. The Step-0a DEBT branches stay the owner's call |
````

### 6h — As of (line 532)

Old text:

````text
| **As of** | 2026-11-04, K = `<K sha>` (C-1, the close candidate; local master, not pushed; origin/master `7cc70be8` was merged in at `c29764ce`) |
````

New text:

````text
| **As of** | 2026-11-05, K′ = `<K sha>`: K `c13024cd`, its Step 7a record fixes `bf3235b1`, the TASK-14725 merge `<14725 merge>` and these records (local master, not pushed; origin/master `7cc70be8` was merged in at `c29764ce`) |
````

### 6i — Open rulings (line 540, the cell's start)

Old text:

````text
| **Open rulings** | None owed by the owner. OQ-1..OQ-4, Q-A and the decide track (D-1..D-12) are all answered.
````

New text:

````text
| **Open rulings** | None owed by the owner. OQ-1..OQ-4, Q-A, the decide track (D-1..D-12) and Step 7a W-1 (two rulings, 2026-11-05) are all answered.
````

### 6j — the header Status (line 5, the cell's end)

Old text:

````text
Next: C-2 on K, then Step 7a (C-3) |
````

New text:

````text
Step 7a cycle 1 found one defect: the manager's day 6 waited for the leaver job's switch-off. TASK-14725 (merged `<14725 merge>`, owner 2026-11-05) decides it by date. Next: K′, C-2 on K′, Step 7a cycle 2 |
````

### 6k — C-2's first-run row: TASK-14725's facts (line 515)

Old text:

````text
and the strengthened facts of 12d and 18a |
````

New text:

````text
and the strengthened facts of 12d and 18a. Also TASK-14725's Docker facts: `LeaverPastGraceByDateTests`, the four D+5/D+6 facts in `DepartedSelfValidatorTests`, `PayrollPastGraceLeaverScopeTests`, and the re-dated `Remove_ActorRemovesGraceLeaver_AsToday` |
````

### 6l — the acceptance table's AC-230-3 row: the names are renamed (line 497)

Old text:

````text
their bodies and summaries pin the new rule, that day 1 after the last working day is not due and day 6 is | N (M-8) · F (CI) |
````

New text:

````text
their bodies and summaries pin the new rule, that day 1 after the last working day is not due and day 6 is. *(Renamed by TASK-14725-T, merged `<14725 merge>`: `…Day1ToDay5_NoFlip_Day6_Flips`, `…BoundaryDayAndGraceDays1To5_NotDue_Day6_Due`, `…BoundaryDay_GraceDays_NotFlipped_Day6_Flipped`.)* | N (M-8) · F (CI) |
````

## Block 7 — `QUALITY.md`: the S147 security grade names SEC-058

**Target:** `docs/QUALITY.md` (LF; line 21, the S147 section's Security & Access Control row).

*Why it is needed:* the row lists the SEC rows S147 registered ("SEC-055 and SEC-057 (Low) registered"), so without SEC-058 it would be incomplete. The grade (A−, held, ▲) does not move: the defect was found and closed inside the sprint. This is the S147 section, still K's own snapshot, so the edit does not rewrite an earlier sprint's record.

Old text:

````text
SEC-055 and SEC-057 (Low) registered;
````

New text:

````text
SEC-055 and SEC-057 (Low) registered; SEC-058 (Low) registered, fixed in code (it closes when C-2 runs its Docker pins green): Step 7a found that the manager's day-6 cut-off waited for the leaver job's switch-off, and TASK-14725 decides it by date;
````

---

## Not drafted here

- **The model-routing register's S147 row** (Fable's NOTE 9 lists it): it belongs to C-5. TASK-14725's internal review ran on Fable (`claude-fable-5-1`), and Block 6g says so.
- **The sprint index row** (`docs/sprints/INDEX.md:116`): its "Step 7a: <7a outcome>" placeholder covers W-1's fix at the close.
- **Fable's NOTE 7** (the test double's override) and the 5α observations: recorded in 6c as follow-ups, not drafted as register rows. The test double is a test-honesty point below the QUAL floor, and the logger and log-level points are observations.
- **The ADR-040 amendment's earlier code citations** (for example `OrgScopeValidator.cs:115`, `:286`, `:524`): they describe the code of 2026-11-03 and stay as written. The new note gives the lines at `0a64c1eb`.

## Brief contradictions, deviations and open points

**Claims about the existing code (checked; the code governs):**
1. **SEC-057's new lines:** confirmed. At `0a64c1eb` the three leader arms are `ApprovalEndpoints.cs:417-428`, `:699-709` and `:1924-1936` (each runs from the terminated-inclusive validator call to the predicate's 403).
2. **The report's line numbers for the predicate** (`IsPastManagerGrace :412`, `QueryPastManagerGraceAsync :433`) are from `527b7c41`. At `0a64c1eb`, after fix-up 1's comment lines, they are `:422` and `:443`, and the check itself is `:401`. The drafts use `0a64c1eb`. Fable's `:409` is not the check line at `0a64c1eb` either.
3. **The PAT-028 class remark** is `OrgScopeValidator.cs:48-54` at `0a64c1eb`; Fable cites `:51-57`.
4. **The employed-month bound's flag check in Payroll** is the helper at `Program.cs:876`; `:304`, which Fable cites, is its call site. Both lines are confirmed.
5. **The SECURITY.md lines the brief gives** are confirmed at `bf3235b1`: `:120`, `:141`, `:146`, `:153-161` (the edit lands on `:154`) and `:169`. The year-overview exception is the end of line 169.
6. **HRP-011 is at `:117`:** confirmed.
7. **The -T fix-up is still uncommitted in the worktree.** It already adds the reject and year-overview facts to `LeaverPastGraceByDateTests.cs`, which is why 6c names them. If the committed fix-up differs, adjust 6c's NOTE 5 line.

**Claims about required behaviour:** none contradicted.

**Needs a ruling (the Orchestrator's):**
- **R-1, SEC-058's status: CLOSED now (as briefed, drafted) or "fixed in code; closes at C-2".**
  - *For CLOSED now:* the fix is merged, its Unit pin and mutation ran at the gate, and the Docker pins are named and marked CI-pending in the same cell.
  - *For the alternative:* this sprint closed SEC-053 only after its Docker facts ran green in CI (`SPRINT-147.md`, the 14704 dispositions: "fixed in code; closes when CI-W2 runs the Docker facts green"), and Step 7a's W-2 asks that no result be claimed before the run.
  - The alternative text for 3a's status cell: "**FIXED IN CODE (S147, TASK-14725, merged `<14725 merge>`); closes when C-2 runs its Docker pins green** (`<C-2 run id>`)". At the flip, "FIXED IN CODE … closes when" becomes "CLOSED … CI-verified at".
  - Lean: the alternative, for consistency with SEC-053. Drafted as briefed.
- **R-2, whether 6c names the reject and year-overview facts.** It does, because they are in the worktree. Confirm them at the merge (point 7 above).

## Revision history

| Draft | Date | By | What |
|---|---|---|---|
| 1 | 2026-11-05 | `planner` (Opus 5.5) | First draft at `bf3235b1`, code at `0a64c1eb`. Blocks 1–7, 29 replacements: ADR-040's dated S147 Step 7a note with four pointers; `SECURITY.md` in five places; SEC-058 (registered and closed) and SEC-057 refreshed; HRP-011; performance F7; the sprint log (ledger, merge record, plain words, Step 7a cycle 1, the two owner rulings, Phase, Next action, and five consistency parts); `QUALITY.md`'s security row. Seven code-claim notes; two Orchestrator rulings proposed |
