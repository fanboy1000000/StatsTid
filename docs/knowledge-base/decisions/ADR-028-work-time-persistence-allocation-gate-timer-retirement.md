# ADR-028 — Self-recorded work-time persistence + allocation reconciliation gate + timer retirement

| Field | Value |
|-------|-------|
| **Status** | accepted |
| **Sprint** | S56 |
| **Domains** | Backend, Infrastructure, Data Model, Frontend |
| **Tags** | work-time, projection, event-sourcing, allocation-gate, approval, timer-retirement, norm, latest-wins |
| **Supersedes / amends** | Retires the timer feature shipped in S9 (ADR-012 era); amends ADR-026 (timer events retired-but-retained); builds on ADR-018 D13 (sync-in-tx projection) and ADR-023 (EmploymentProfileResolver dated reads) |

## Context

In Min Tid, employees record working time under "Arbejdstid" via "Tilføj periode" (start/end
intervals). This data was **never persisted** — it lived only in React state, synced from the live
timer, and was wiped on any refetch. The reported bug: approving a month (which triggers a refetch)
made the entered hours disappear. The frontend read a `data.arrivalDepartures` field the backend never
returned, and `saveMonth` only persisted project entries + absences.

Two further problems were in scope: (a) the check-in/out timer added complexity for little value, and
(b) there was no enforcement that recorded worked hours are actually distributed across projects.

## Decision

### D1 — Event-sourced work-time persistence (latest-wins projection)
A new `WorkTimeRegistered` domain event carries the full per-(employee, date) state: a list of
`WorkInterval{Start,End}` plus a `ManualHours` scalar. Re-saving a day emits a NEW superseding event
(append-only; no mutation). A new `work_time_projection` table (PK `(employee_id, date)`) holds the
**latest-wins** state, guarded by `outbox_id` (`ON CONFLICT ... DO UPDATE ... WHERE
work_time_projection.outbox_id <= EXCLUDED.outbox_id`) so an out-of-order replay never clobbers a newer
row. The Skema save enqueues the event via `IOutboxEnqueue` and upserts the projection in the SAME
transaction (ADR-018 D3/D13 — read-your-write without waiting for the publisher). `ProjectionBackfillService`
replays `WorkTimeRegistered` so a deploy against an existing event log rebuilds the projection.

**D1 note 2026-11-25 (S148).** Two changes to the work-time record:
- **Manual hours are gone** (TASK-14809, merged `540ac9a9`; ADR-043 D10, D-2.4). `WorkTimeRegistered` and `work_time_projection` carry the day's intervals only: the `ManualHours` member and the `manual_hours` column were removed, with a reseed (reseed, don't migrate). A stray `manualHours` member from a legacy client is ignored. D2's S72 note that `manual_hours` stays is history.
- **A cleared day is a kept row with its marker set** (owner Q2, 2026-11-10; addendum A1, D-4; TASK-14840, merged `a331e452`). When every work period of a day is removed, the row is not deleted. The clear sets `cleared_by_outbox_id` to the `WorkTimeCleared` event's outbox id and advances the row's `outbox_id` to the same value, so an older registration replayed later cannot pass the latest-wins guard and bring the day back (`docker/postgres/init.sql:2181-2207`; `src/Infrastructure/StatsTid.Infrastructure/WorkTimeProjectionRepository.cs:79-86`, one statement shared by the live clear and the rebuild). A later registration of the same day clears the marker. **Every reader counts only rows whose marker is null**, and the rebuild replays clears with the same update. "Latest-wins" therefore now means: the latest event for the day, a registration or a clear, decides.

### D2 — Two entry methods, additive — **SUPERSEDED in the UI (S72, owner ruling D-B 2026-06-12)**
"Arbejdstid" is restructured into two entry rows: **Tilføj periode** (intervals) and **Tilføj timer**
(direct daily hours, Danish comma `7,4`). A day's worked hours = interval hours + manual hours
(additive). Project rows and absence rows are unchanged.

> **S72 supersession (SPRINT-72 D-B):** the Skema redesign's premise was that three competing "add"
> models confused registration. The owner consciously ruled the **manual lump-hours ENTRY UI dropped**:
> the day panel registers clock periods only. The PERSISTENCE model is unchanged — `manual_hours`
> stays on `work_time_projection` and in `WorkTimeRegistered`, existing values keep counting in every
> worked total, render read-only in the panel ("Manuelt registreret: X t"), and every write PRESERVES
> the day's existing value through the latest-wins write (the S72 R7 pin, tested in `useSkema`'s
> `buildWorkTimePayload`). D2's additive ARITHMETIC therefore still holds; only the entry affordance
> is gone.

### D3 — "Diff. fra normtid" uses the real per-employee norm
The renamed difference row = `(worked) − normtid`. Norm is resolved **server-side** in the Skema GET
(pure read, no rule-engine call — P2) as `WeeklyNormHours × part_time_fraction / 5` (weekends 0), via
the dated `IEmploymentProfileResolver.GetByEmployeeIdAtAsync` + `ConfigResolutionService`, with okVersion
from `OkVersionResolver.ResolveVersion(day)` (the OK24→OK26 switch is **2026-04-01**, not calendar-year).
Academic `ANNUAL_ACTIVITY` employees get a **null/blank** norm (a weekday split would be wrong; annual
norm is calendar-day prorated). Known scoped limitation: a mid-month *local-profile* config change is
not reflected (`ConfigResolutionService` is current-only); mid-month *fraction* changes are (dated
resolver). Full dated local-profile resolution is a follow-up.

### D4 — Hard allocation-reconciliation gate at employee-approve
A month cannot be employee-approved unless every day's project allocation matches its worked hours.
A second deterministic validation in `ApprovalEndpoints` employee-approve (an approval precondition,
NOT rule-engine logic — P2): per day, `worked` = interval hours + manual hours (work_time_projection);
`allocated` = Σ time entries WHERE `ActivityType='NORMAL' AND TaskId IS NOT NULL` (allowlist matching
the grid's predicate — excludes historical timer-origin and null-TaskId entries). Absences are excluded
entirely. Round both to 2 decimals; balanced iff `|worked−allocated| < 0.005` (one shared tolerance,
mirrored on the frontend so the "Ikke fordelt" green state equals the gate-accept set). Blocks in BOTH
directions with a **discriminated 422** `{kind:"allocation", unbalancedDays:[{date,worked,allocated,
direction}]}` — distinguishable from the existing coverage 422 (`missingDays`). Coexists with the
coverage check (both must pass). Surfaced in the UI by an "Ikke fordelt" row + a "Fordeling af
arbejdstid" summary card.

**S88 amendment — the read-side surfaces of this gate must mirror the SAME per-day predicate.**
Two read endpoints expose the gate's allocation signal and MUST agree with the approve gate bit-for-bit
(no month-vs-day drift):
- `GET /api/approval/team-overview` (S87) — the per-employee `hasWarning` is the per-day ANY check:
  `true` iff ANY day has `|round(worked_d,2) − round(allocated_d,2)| > 0.005` (`ApprovalEndpoints.cs`
  team-overview handler).
- `GET /api/approval/{employeeId}/allocation-breakdown` (S88, the leder-oversigt expandable detail's
  Fordeling) — returns `hasAllocationImbalance` computed by the **identical per-day ANY check**, so it
  equals the table row's `hasWarning` exactly. `underAllocated`/`overAllocated` (Σ per-rounded-day
  directional deltas) and `allocations[]` (NORMAL+non-null-TaskId hours by TaskId, summing to
  `allocated`) are **display-only** — `hasAllocationImbalance` is NEVER derived from those sums (summing
  sub-tolerance daily deltas could trip a month total past `0.005` where the per-day ANY check would
  not, re-introducing drift in the opposite direction). Authorized via the designated-approver predicate
  (`IsEffectiveDesignatedApproverAsync`), so it cannot 403 a row the leader sees on the team-overview.
  The detail's "Manglende fordeling"/"Overfordeling" alerts gate behind `hasAllocationImbalance` so the
  opened detail never contradicts the row chip.

**D4 amendment 2026-11-25 (S148 TASK-14809 + -T, merged `540ac9a9`; ADR-043 D10, D-2.4): the worked side is interval hours alone.** Clock periods are now a day's only work-time record (D1's note), so the gate's `worked` is the sum of the day's interval hours from `work_time_projection`, current days only; "+ manual hours" above is history. The gate is the send command's check (`src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs:2603-2618`), and the S88 read sides use the same sum (the team overview `:1228-1251`, the allocation breakdown `:1631-1653`), both skipping a cleared day. Pinned by `tests/StatsTid.Tests.Regression/Outbox/SendCheckIntervalOnlyTests.cs` (AC-S0-5b): a stray `manualHours` member that a legacy client posts is ignored, so hours sent as manual are not counted, and the equality catches them as lost hours.

### D5 — Timer retirement (write path removed, events retained)
The `/api/timer/*` endpoints, `TimerSessionRepository`, the `TimerSession` model, and the
`timer_sessions` table are removed. The `timer_sessions` DROP co-lands with the code removal after a
grep-zero check. CRITICALLY, the `TimerCheckedIn`/`TimerCheckedOut` event classes + their
EventSerializer registrations are **RETAINED** (deserialize-only) — the event store is append-only and
must still replay historical timer events. Historical timer-origin `TimeEntryRegistered` rows
(`ActivityType="timer"`) are left untouched and continue to behave as ordinary time entries; verified
that no payroll/wage-type or rule-engine logic branches on `ActivityType="timer"` (P6).

**D5 note 2026-11-25 (S148 TASK-14802, merged `e4e6b0ea`; the wave-1 brief adjudication, ruling A6; told to the owner 2026-11-10):** the last sentence no longer holds. Payroll now refuses a month whose calculation window holds a stored time entry typed as anything but ordinary work (an `ActivityType` other than null or `NORMAL`), and a historical `"timer"` row is refused like any other: 422 `payroll-activity-type-refused`, with a fixed, date-free body, and nothing written (`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:1009-1054`). After a reseed no such row exists, so nothing observable changes. A database carried forward instead would meet the refusal with no in-product remedy (QUAL-BF-003); a line in the legacy-database upgrade runbook is owed at S148's close (`docs/sprints/SPRINT-148.md:395`). The send check already left `timer` rows out of the allocation (D4). The retained timer event classes are unchanged.

### D6 — A Skema save replaces its cell; `POST /api/time-entries` stays additive; replaced rows are kept (S145)

*Added at the S145 close: TASK-14507, merged `94dea75` (2026-10-14); the adjudicator's Skema ruling in refinement rev 3.3, owner informed 2026-10-01; fixes QUAL-192, QUAL-193 and QUAL-194.*

**In plain terms.** A Skema cell is one number: one employee, one day, one project, or one absence type. Before S145, saving a cell again stored a second row beside the first. Both grids showed the latest value, but every total the server computed added both rows. So the send-for-approval check refused a month the employee could not repair, a re-saved ferie day used up vacation quota twice, and a save retried after a lost network response stored the same day twice. The product had always meant "replace": the cell holds an absolute number, both grids show the last value written, the inability to clear a cell is recorded as a known limitation (SPRINT-72 R17), and the sibling work-time record is latest-wins by design (D1). D6 makes the stored data mean the same.

**The decision.**
- **Skema cells are latest-wins per cell.** A re-save still appends a new `TimeEntryRegistered` / `AbsenceRegistered` event, so events stay append-only; the event now carries `Source = SKEMA`. The new row becomes the cell's current value, and the cell's earlier Skema row is marked `superseded_by_outbox_id` = the new row's `outbox_id`: mark first, then insert, inside the save's existing per-employee lock (`TimeEntryProjectionRepository.cs:19-26, :47-55`). A cell is `(employee_id, date, task_id)` for time entries and `(employee_id, date, absence_type)` for absences. A partial unique index on each table admits one current Skema row per cell (`ux_time_entries_proj_current_skema_cell`, `NULLS NOT DISTINCT`, and `ux_absences_proj_current_skema_cell`; `init.sql`, the S145 segment). The index is a backstop; the mark is the mechanism.
- **`POST /api/time-entries` rows stay additive.** *(The route retired in S148; see the D6 note below.)* Each such registration is a separate piece of work: `source = TIME_POST`, never marked and never marking another row, even when it carries a task id. An event with no `Source`, which is every event written before S145, reads as `TIME_POST`.
- **Superseded rows are retained.** A replaced row is never deleted or overwritten, so the audit trail, and a payroll replay that re-reads rows by event id, still see exactly what a calculation used (ADR-016 D10, S145 amendment). Every total counts current rows only: both repositories' default reads, the raw-SQL readers (team overview, allocation breakdown, the vacation settlement, the employment-window strand check) and the vacation quota filter `superseded_by_outbox_id IS NULL`. `GetByEventIdsAsync` is the one read that does not.
- **The quota change is signed.** Re-saving an absence books the new feriedage minus the stored feriedage of the rows it replaces. An unchanged re-save leaves `used` unchanged; a reduction lowers it and is never refused, even above the cap after a revaluation; an increase goes through the guarded quota check, and the pre-check mirrors the same delta.
- **A cell cannot be cleared.** *(Superseded 2026-11-25: S148 built clearing; see the D6 note below.)* The save refuses hours ≤ 0, and an entry with an empty project code, with a 400 before anything is written (`SkemaEndpoints.cs:739-787`), so every current row carries hours > 0. SPRINT-72 R17 stays open. The storage leaves room for a later retraction (mark the current row superseded, insert nothing), which is a ROADMAP backlog item (ruling R24).
- **The rebuild agrees with the live path.** `ProjectionBackfillService` replays the events with the same mark, in `outbox_id` order, so a from-events rebuild reproduces the live current set.

**What it changes in D4.** D4's "Σ time entries" assumed one row per allocation. It now holds as written: the gate sums current rows only, so a re-saved cell counts once and the gate agrees with the grid.

**What was given up.** A payroll-only "latest per cell" read was rejected: payroll would then pay hours the approval gate never approved, and payroll would carry the Skema's cell rules behind a read-only contract. Keeping replaced rows costs storage; deleting or overwriting them would break replay. Evidence: the Docker pins S1–S13, Q1–Q7, B1–B3, PAR, TRM and SHO, green in CI at `K_A` (run `38747749039`), and their mutations observed red in the Stage A evidence runs (`SPRINT-145.md` § "Task ledger", TASK-14507, and § "CA-5").

**D6 note 2026-11-25 (S148; owner Q2, 2026-11-10, "build clearing in S148"; addendum A1, D-1 … D-14; TASK-14840 + -T and TASK-14841, merged `a331e452`; SPRINT-72 R17 closed).** In plain words: a Skema cell can now be emptied, and the removal is recorded with who made it, never as a silent delete and never as a 0-hour row.
- **A clear retires the cell's current row and inserts nothing.** For a project entry or an absence, the save enqueues `TimeEntryCleared` or `AbsenceCleared` and marks the cell's current Skema row `superseded_by_outbox_id` = the clear's outbox id, with this decision's mark SQL (`RetireSkemaCellAsync` in `src/Infrastructure/StatsTid.Infrastructure/TimeEntryProjectionRepository.cs` and `AbsenceProjectionRepository.cs`). No row is inserted, so the cell has no current row. A clear of a cell with no current row records nothing.
- **A cleared work-time day's row is kept and marked,** never deleted: `cleared_by_outbox_id` set and its `outbox_id` advanced to the clear's (D1's note; `WorkTimeProjectionRepository.cs:206`, `ClearDayAsync`).
- **One decision, in the lock.** What a clear removes is read once, inside the save's per-employee lock, before the first write, so two concurrent clears of one holiday cell give the day back once. A clear passes the save's gates unchanged: the approval lock, the month bound, the employment window, and who may write (the employee, or HR and above in scope).
- **The rebuild replays clears** with the same mark and the same guarded update, so a rebuild from the event log reproduces the live current set. One limit is registered: from empty tables, a fact not yet copied to `events` is missing until the next start after it is (QUAL-274).
- **A holiday-type clear gives the stored feriedage back,** through the ungated reduction, with the balance event (ADR-032, note of this date). A care day or senior day is cleared whole.
- **`POST /api/time-entries` retired in S148** (TASK-14802, merged `e4e6b0ea`). The source class `TIME_POST` stays for rows written before; such rows are never marked or cleared, and after a reseed none exist. The other bullets above stand, with "current" now meaning not superseded and, for work time, not cleared.

## Consequences

- The disappearing-hours bug is fixed; work time survives reload, month-switch, and approval.
- "All worked hours allocated" becomes a hard, enforced invariant at approval.
- The norm shown is correct per employee (full-time, part-time); academic annual-norm staff see blank
  pending a follow-up.
- One fewer feature surface (timer); event-store replay integrity preserved.
- Net DB table count unchanged (+work_time_projection, −timer_sessions).

## Follow-ups (not in S56)
- Dated local-profile resolution for the norm (mid-month local-profile config changes).
- Correct calendar-day annual-norm diff for ANNUAL_ACTIVITY employees.
- Optional reconciliation of historical timer-origin time entries.
- Export `formatApprovalValidationError`/`parseApprovalValidationError` for direct frontend testing
  (advisory from S56 Test & QA).
