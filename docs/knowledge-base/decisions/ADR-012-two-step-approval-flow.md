# ADR-012 — Two-Step Approval Flow (Employee → Manager)

| Field | Value |
|-------|-------|
| **ID** | ADR-012 |
| **Category** | decision |
| **Status** | approved |
| **Sprint** | S9 |
| **Domains** | Backend, Infrastructure, Frontend |
| **Tags** | approval, workflow, state-machine, period, two-step |

## Context

Sprint 7 introduced a single-step approval workflow: DRAFT → SUBMITTED → APPROVED | REJECTED. Sprint 9 requires a two-step flow where the employee self-approves before the manager reviews, with calendar-based deadlines.

## Decision

Replace the single-step approval with a two-step state machine:

```
DRAFT → EMPLOYEE_APPROVED → APPROVED
                          → REJECTED → DRAFT (employee re-edits)
EMPLOYEE_APPROVED → DRAFT (manager reopens with reason)
```

### State Transitions

| From | To | Actor | Action |
|------|----|-------|--------|
| DRAFT | EMPLOYEE_APPROVED | Employee | Self-approve (`POST /api/approval/{id}/employee-approve`) |
| EMPLOYEE_APPROVED | APPROVED | Leader+ | Manager approve (`POST /api/approval/{id}/approve`) |
| EMPLOYEE_APPROVED | REJECTED | Leader+ | Manager reject (`POST /api/approval/{id}/reject`) |
| EMPLOYEE_APPROVED | DRAFT | Leader+ | Manager reopen (`POST /api/approval/{id}/reopen`) |
| REJECTED | DRAFT | Employee | Employee re-edits (implicit on next save) |

### Manager-transition authorization (S74 / ADR-027 D13 amendment)

The three **manager** transitions (`approve`, `reject`, `reopen` — the Leader+ arm) originally authorized on RBAC org-scope alone (LocalLeader+ whose scope covers the period's org). S74 (ADR-027 D13, owner ruling OQ-3a) adds an **additive OR-branch**: a manager transition is authorized if `(actor's RoleScope covers period.OrgId) OR (actor is the single resolved effective designated approver of the employee, within the same tree_root_org_id, at today)`. This lets a cross-afdeling designated approver (incl. one acting through an approver-owned vikar, ADR-027 D14) act on a report whose org their RBAC scope does not reach — bounded so cross-styrelse remains impossible (ADR-027 D2). The edge OR-branch lives ONLY in the Leader+ arm of `reopen` (its `EmployeeOrAbove`/`isEmployee` split); the **employee** transitions (`employee-approve`, and `submit`) are UNCHANGED — they do NOT inherit edge authority. The existing S50 REQUIRED-mode `428`-confirm-fallback flow is preserved. (Carries a mandatory Step-5a security review; the check-then-act revocation window is a deferred in-lock-hardening follow-up.)

### Deadlines

- **Employee deadline**: Last day of month + 2 calendar days
- **Manager deadline**: Last day of month + 5 calendar days
- Deadlines are set when the employee self-approves and stored on the `approval_periods` row

### Locking Behavior

- **DRAFT / REJECTED**: Employee can edit Skema cells freely
- **EMPLOYEE_APPROVED / APPROVED**: Skema cells are read-only. Batch save returns 403.
- **Manager reopen (→ DRAFT)**: Unlocks for employee editing again

### Backward Compatibility

- The existing `SUBMITTED` status remains valid in the CHECK constraint for historical data
- `approve` and `reject` endpoints accept both `SUBMITTED` and `EMPLOYEE_APPROVED` as input states
- `GET /api/approval/pending` returns both `SUBMITTED` and `EMPLOYEE_APPROVED` periods

## Alternatives Considered

1. **Three-step flow (DRAFT → SUBMITTED → EMPLOYEE_APPROVED → APPROVED)**: Rejected — adds unnecessary complexity. The employee's self-approval is the submission act.
2. **Reuse SUBMITTED for employee approval**: Rejected — semantically different. SUBMITTED (Sprint 7) meant "sent to manager". EMPLOYEE_APPROVED means "employee certifies correctness".

## Consequences

- `approval_periods` table gains 4 columns: `employee_approved_at`, `employee_approved_by`, `employee_deadline`, `manager_deadline`
- Status CHECK constraint expanded to include `EMPLOYEE_APPROVED`
- New events: `PeriodEmployeeApproved`, `PeriodReopened`
- Frontend Skema page enforces read-only mode based on period status
- ApprovalEndpoints gains 2 new routes (employee-approve, reopen)
