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
