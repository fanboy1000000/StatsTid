# [RES-002] Manager draft-visibility rule: surface withholding (S124) + endpoint gates on 3 of 12 reads (S128); 9-read remainder open

| Field | Value |
|-------|-------|
| **ID** | RES-002 |
| **Category** | resolution |
| **Status** | approved — PARTIALLY ENFORCED (S128/TASK-12804: 3 of 12 reads gated); 9-read remainder OPEN |
| **Sprint** | Sprint 124, 128 |
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

## The read surface — CORRECTED CENSUS (S128/TASK-12805; the original 6-row census was understated)
The S128 Step-0b investigation verified the true surface at **12 endpoints across 6 files** — the
original table below omitted `flex-balance`, `balance/series`, `compliance/compensatory-rest`, and
all three `overtime/{employeeId}/*` reads. **Arithmetic, stated so the tables reconcile
(Step-7a internal N2): 12 = the 3 gated in S128 + the 9-read remainder.** The Skema month GET
appears in the gated table for completeness but sits OUTSIDE the 12-count — it was closed in S124,
before this census existed. **One deliberate exclusion (Step-7a internal N3):**
`GET /api/overtime/{employeeId}/governance` is month-keyed and same-population but is NOT counted —
its response is a rule verdict over **caller-supplied** hour inputs, not the employee's stored
figures, so there is nothing of the employee's to withhold. Every open read authorizes through
org-scope or `DesignatedApproverAuthorizer.IsEffectiveApproverOrUnitLeaderAsync` and applies **no
`approval_periods.status` filter**. Vikar / acting managers inherit reachability through the same
predicates.

**Gated in S128/TASK-12804** (tiered per R1, narrow-only per R5, 403 per R6 — see the ruling block
below; the shared gate lives in `ApprovalReadTier.cs`):

| Read | Status |
|------|--------|
| `GET /api/skema/{employeeId}/month` | CLOSED for the leader tier (S124/TASK-12405); HR-or-above deliberately exempt (corrective tier) |
| `GET /api/approval/{employeeId}/allocation-breakdown` | **CLOSED S128** — leader tier 403 on non-submitted months (incl. REJECTED and no-row, fail-closed) |
| `GET /api/compliance/{employeeId}/period` | **CLOSED S128** — same gate |
| `GET /api/balance/{employeeId}/summary` | **CLOSED S128** — same gate |

**Still open (the 9-read remainder — 7 of these carry NO month parameter, so gating them needs
contract changes or per-row `approval_periods` joins, a materially larger change than a 403 guard):**

| Read | What it still serves | Month param? |
|------|----------------------|--------------|
| `GET /api/balance/{employeeId}/year-overview` | Per-month actuals, all 12 months | year only |
| `GET /api/balance/{employeeId}/series` | Entitlement series across months | no |
| `GET /api/time-entries/{employeeId}` | Raw entry rows, unbounded by date | no |
| `GET /api/absences/{employeeId}` | Absence rows (the `ferieUsed` source) | no |
| `GET /api/flex-balance/{employeeId}` | The unwithheld twin of withheld `flexBalance` | no (latest event) |
| `GET /api/compliance/{employeeId}/compensatory-rest` | All compensatory-rest rows, unbounded | no |
| `GET /api/overtime/{employeeId}/balance` | Year overtime figures | year only |
| `GET /api/overtime/{employeeId}/pre-approvals` | Pre-approval rows | no |
| `GET /api/overtime/{employeeId}/compensation-choice` | Balance-derived choice state | year only |

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

## The ACTOR MODEL — RULED (S128, owner ruling R1, 2026-08-11)
The S124 inconsistency (team-overview withholding actor-blind; month GET tiered) is resolved:
**TIERED is authoritative for endpoint gates** — self exempt, HR-or-above exempt (the corrective
tier: you cannot correct what you cannot read), leader tier gated by
`ApprovalVisibility.IsSubmittedToManager`. Companion rulings: **R5 narrow-only composition** (a
gate is applied WITHIN each endpoint's existing access population and may only subtract access —
the 3 gated endpoints have three different auth shapes and none was widened) and **R6 withhold
shape = 403** (the month-GET precedent; the shared construction site is
`ApprovalReadTier.MonthNotSubmittedForbidden`). The team-overview's actor-blind DISPLAY withholding
was deliberately NOT retrofitted in S128 — it stays as shipped; the tiered model governs endpoint
gates. Implementation: `ApprovalReadTier.cs` (S128/TASK-12804), the third lift-pattern instance
after `ApprovalVisibility` (S124) and `ApprovalPeriodSaveLock` (S128).

The **reopen read-fork remains open** (S128 ruling R4): a leader-reopened month reverts to DRAFT
and is withheld from the leader who approved it; `PeriodReopened.PreviousStatus` is the future
discriminator if the owner ever rules to distinguish leader-reopen from self-reopen.

## Agent Guidance
- **Backend Agent**: when touching any read in the table above, treat this entry as live scope — do
  not add a new employee-data read for a manager audience without a period-status gate.
- **UX Agent**: never re-derive the withheld rule client-side from `status`; key off the NULL the
  server sends. `normExpected` and `ferieTotal` deliberately survive as denominators (contract norm
  and standing quota — no registration can move them).
- **Security Agent**: this is the entry to start from when the gate is implemented; the table above
  is the caller census, verified 2026-07-30 by both review lenses (convergent finding).
