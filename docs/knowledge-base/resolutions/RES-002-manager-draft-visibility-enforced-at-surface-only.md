# [RES-002] Manager draft-visibility rule enforced at the Teamoversigt surface only (deferred endpoint-level gate)

| Field | Value |
|-------|-------|
| **ID** | RES-002 |
| **Category** | resolution |
| **Status** | approved — enforcement deliberately INCOMPLETE, follow-up OPEN |
| **Sprint** | Sprint 124 |
| **Date** | 2026-07-30 |
| **Domains** | Backend, Frontend, Security |
| **Tags** | access-control, approval, draft-visibility, manager, priority-conflict, deferred-hardening, p7 |

## Context
S124 / TASK-12402 established a new visibility rule, owner-ruled 2026-07-30:

> **A manager cannot see anything about an employee's month before that month is submitted.**

The trigger was that the leader Teamoversigt (`GET /api/approval/team-overview`) computed its
numeric fields over the WHOLE roster irrespective of period status, so a leader could read an
employee's un-submitted registered hours, the derived overtime/warning, the flex balance, and the
ferie **used** count. (`entitlement_balances.used` was the sharpest of these: it is incremented
inside the employee's OWN Skema save transaction — `SkemaEndpoints.cs` →
`EntitlementBalanceRepository.CheckAndAdjustAsync` — with no approval gate whatsoever, so a
*drafted* day off moved the manager's Ferie column.)

TASK-12402 withheld all five fields server-side on non-submitted rows, kept the row itself (so the
leader can still chase a missing submission before the `lederfrist` and the "Fravær i dag" tile
still covers the team), and relabelled the leader-facing badge from "Kladde" to "Ikke indsendt".

## Conflict
- **Priority #7 (Security and access control):** a visibility RULE is only real when it is enforced
  at every read that can serve the protected content. Enforcing it on one screen leaves the rule
  aspirational.
- **Priority #9 (Usability) + delivery scope:** the same content is served by a family of
  employee-facing reads that the EMPLOYEE'S OWN views consume. Gating them per period-status is a
  cross-cutting change with its own design fork (self vs. other), well beyond a UI-polish task.

## Resolution
**The Teamoversigt surface is hardened now; the endpoint-level gate is DEFERRED as a named
follow-up.** Owner ruling 2026-07-30: *"Record this as a follow up for another day."*

This entry exists so the gap is a KNOWN, RECORDED deferral rather than an implied guarantee. The
honest statement of today's posture:

> The Teamoversigt no longer DISPLAYS un-submitted content, and the month GET is status-gated for
> the LEADER tier (TASK-12405). The RULE is still not fully ENFORCED — the remaining sibling reads
> below serve the same data to the same actor.

## The unenforced surface (the follow-up's scope)
Every read below authorizes through `DesignatedApproverAuthorizer.IsEffectiveApproverOrUnitLeaderAsync`
or an equivalent authority predicate, and applies **no `approval_periods.status` filter**. Because
that is the SAME predicate the team-overview roster is built from, reachability is not incidental —
it is guaranteed for exactly the population whose row was withheld. Vikar / acting managers inherit
all of it through the same predicate.

| Read | What it still serves | Note |
|------|----------------------|------|
| ~~`GET /api/skema/{employeeId}/month`~~ | ~~The ENTIRE un-submitted grid, cell by cell~~ | **CLOSED for the LEADER tier in S124 / TASK-12405** — a leader gets 403 unless the period was sent. STILL OPEN for HR-or-above, deliberately: HR is the corrective tier (TASK-12404 permits HR to edit, and you cannot correct what you cannot read) |
| `GET /api/balance/{employeeId}/summary` | `normHoursActual` | The unwithheld twin of the withheld `normRegistered` |
| `GET /api/balance/{employeeId}/year-overview` | Per-month actuals | |
| `GET /api/time-entries/{employeeId}` | Raw entry rows, unbounded by date | |
| `GET /api/absences/{employeeId}` | Absence rows | Related to the `ferieUsed` leak above |
| `GET /api/approval/{employeeId}/allocation-breakdown` | Per-`task_id` hour split, worked, allocated, under/over | The Teamoversigt expander's own fetch — the FE affordance was removed, the endpoint was not |
| the compliance read | Warnings/violations for the month | Same expander |

## Rationale
Withholding on the display surface removes the leak from the place a manager actually works, at UI
cost only, and it can ship immediately. A correct endpoint-level gate needs one shared predicate
(actor-is-not-self AND period-not-submitted) applied consistently across the reads above, plus a
decision on how the employee's own access is distinguished from a manager's on the *same* endpoint.
Rushing that risks breaking the employee's own Skema/Årsoversigt — a worse outcome than a recorded,
scoped gap.

## Consequences
- **Do NOT describe TASK-12402 as "the access-control fix".** It is a display-surface fix plus a
  contract change. Any summary that claims the rule is enforced is wrong.
- The withheld set is defined SERVER-side in `ApprovalEndpoints.cs` (the `submittedToManager`
  allowlist — `SUBMITTED`/`EMPLOYEE_APPROVED`/`APPROVED`/`REJECTED`); a future sixth status defaults
  to WITHHELD (fail-closed). The follow-up should lift that predicate into a shared gate rather than
  re-deriving it per endpoint.
- The wire contract for `TeamOverviewEmployeeRow` now has 5 nullable fields
  (`normRegistered`, `overtime`, `hasWarning`, `flexBalance`, `ferieUsed`). Null means WITHHELD, not
  zero — never "fix" a null by defaulting it to 0, which is the lie the change removed.
- **Open sub-fork, recorded with the deferral:** a leader-reopened month reverts to `DRAFT`, so the
  rule currently blinds a leader to figures they had already lawfully approved (every persistent
  `DRAFT` period is a reopened one — the create path submits in the same transaction). Applied
  literally per the owner ruling; the alternative is to distinguish leader-reopen (visible) from an
  employee self-reopen of `EMPLOYEE_APPROVED` (withheld) via `PeriodReopened.PreviousStatus`.

## Related: the WRITE class — CLOSED (S124 / TASK-12404), unlike these reads
This entry's census covers READS. A sibling hole existed on the WRITE side and is **fixed**, so do
not conflate them: `POST /api/skema/{employeeId}/save` and `POST /api/time-entries` both admitted
ANY non-Employee actor whose org-scope covered the target — LocalLeader included — so a manager
could write an employee's registrations (and the time-entries endpoint has no approval-period check
at all, so in any period state).

Owner ruling 2026-07-30: *"A manager can never edit an employee's registrations. Only HR and admins
can."* Both endpoints now pass `roleFloor: StatsTidRoles.LocalHR` to `ValidateEmployeeAccessAsync`
when the actor is NOT the target. **SELF is exempt and that exemption is load-bearing** — a
LocalLeader is also an employee registering their own time, and is not Employee-role, so an
unconditional floor would have locked every leader out of their own timesheet.

Both denials are proven RED-on-old (`S91TreePageHrAccessTests.SkemaSave_LeaderOnAnotherEmployee_Is403`,
`TimeEntryCreate_LeaderOnAnotherEmployee_Is403`), with HR-allowed, self-allowed and reads-still-allowed
guards alongside. **The LEADER read WAS narrowed** in TASK-12405 (status-gated); HR's corrective read
is deliberately ungated. A leader must still review a *submitted* month's grid (TASK-12403), which is
why the remaining deferred read gate must stay period-status-based, never a blanket denial.

## OPEN QUESTION for the follow-up: which ACTOR MODEL is authoritative?
Step-7a's internal lens flagged a genuine inconsistency this sprint created and did NOT resolve:

- The **team-overview withholding is actor-blind** — the predicate keys on period status only, so it
  withholds from LocalHR / LocalAdmin / GlobalAdmin exactly as from a leader.
- The **month GET is tiered** — HR-or-above is exempt, on the explicit ground that HR may correct.

So today HR sees `—` in the Teamoversigt Normtimer/Ferie columns while being able to open the full
grid and edit it. Both are individually defensible; together they are two actor models for one rule.
The follow-up must pick one: extend the corrective exemption to the withholding, or ratify the
withholding as intentionally blanket. Recorded so the next sprint need not re-derive which is
authoritative. (This entry's earlier sketch — "actor-is-not-self AND period-not-submitted" — does not
account for the HR dimension at all.)

## Agent Guidance
- **Backend Agent**: when touching any read in the table above, treat this entry as live scope — do
  not add a new employee-data read for a manager audience without a period-status gate.
- **UX Agent**: never re-derive the withheld rule client-side from `status`; key off the NULL the
  server sends. `normExpected` and `ferieTotal` deliberately survive as denominators (contract norm
  and standing quota — no registration can move them).
- **Security Agent**: this is the entry to start from when the gate is implemented; the table above
  is the caller census, verified 2026-07-30 by both review lenses (convergent finding).
