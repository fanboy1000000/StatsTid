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

### D5 — Timer retirement (write path removed, events retained)
The `/api/timer/*` endpoints, `TimerSessionRepository`, the `TimerSession` model, and the
`timer_sessions` table are removed. The `timer_sessions` DROP co-lands with the code removal after a
grep-zero check. CRITICALLY, the `TimerCheckedIn`/`TimerCheckedOut` event classes + their
EventSerializer registrations are **RETAINED** (deserialize-only) — the event store is append-only and
must still replay historical timer events. Historical timer-origin `TimeEntryRegistered` rows
(`ActivityType="timer"`) are left untouched and continue to behave as ordinary time entries; verified
that no payroll/wage-type or rule-engine logic branches on `ActivityType="timer"` (P6).

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
