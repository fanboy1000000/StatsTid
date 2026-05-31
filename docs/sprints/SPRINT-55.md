# Sprint 55 — Skema Approval Flow: locked grid, reopen, expandable detail, month navigation

> **⚠ RETROACTIVE RECONSTRUCTION (2026-05-31).** No sprint log was written when S55
> shipped (commit `df036c3`, 2026-05-27) — this is the "S55 governance black hole" the
> 2026-05-31 documentation audit surfaced. This file is reconstructed **solely from the
> git commit** to restore the auditability chain (priority P3). Fields that depend on
> contemporaneous records that never existed (Step 0b/7a review cycles, exact test
> counts, formal approval) are marked **not recorded**. Do not treat the validation
> evidence below as if it were captured at sprint close — it was not.

| Field | Value |
|-------|-------|
| **Sprint** | 55 |
| **Status** | complete (log reconstructed 2026-05-31) |
| **Start Date** | 2026-05-27 |
| **End Date** | 2026-05-27 |
| **Orchestrator Approved** | reconstructed 2026-05-31 (no contemporaneous approval record) |
| **Build Verified** | not recorded at close (work shipped on master as `df036c3`) |
| **Test Verified** | not recorded at close; reconstructed estimate ~700 (S54 non-Docker methodology baseline: 546 unit + 44 regression + 110 FE). The commit touched `ApprovalDashboard.test.tsx` (+29 lines). |
| **Sprint-start commit** | `f13eaed` (S54) |
| **Sprint-end commit** | `df036c3` |

## Sprint Goal
Complete the Skema month-approval flow on top of the S54 two-level-navigation restructure:
make an approved month read-only, allow role-gated reopening, give managers an expandable
per-period detail view, and add month navigation to the approval dashboard.

## Reconstruction Basis
Derived from commit `df036c3` ("S55 Skema Approval Flow: locked grid, reopen, expandable
detail, month navigation"), author 2026-05-27, 13 files / +780 / −149. The commit message
documents three work areas (SkemaPage, ApprovalDashboard, Backend) reproduced below.

## Architectural Constraints Verified
_Reconstructed from the diff; not contemporaneously checklisted._
- [x] P3 — Event sourcing append-only semantics respected (`PeriodReopened` gains a
      `PreviousStatus` field; no event rewrite)
- [x] P7 — Security and access control (role-branched reopen: employee self-reopen limited
      to `EMPLOYEE_APPROVED`; `LocalHR+` required to reopen `APPROVED`)
- [ ] P1–P6, P8, P9 — not separately recorded

## Task Log (reconstructed — single narrative, original task decomposition unknown)

### TASK-5501 — Skema locked grid + reopen + save-format fix (frontend)

| Field | Value |
|-------|-------|
| **ID** | TASK-5501 (reconstructed id) |
| **Status** | complete |
| **Agent** | UX (reconstructed) |
| **Components** | Frontend — SkemaPage, SkemaGrid, useSkema |
| **KB Refs** | ADR-012 (two-step approval) |
| **Reviewer Audit** | not recorded |
| **External Review (Codex)** | not recorded |

**Description**: SkemaPage save-format fix (cells → entries/absences); flush pending saves
before approve; greyed-out read-only grid when the period is approved; role-gated reopen
(employee self-reopen for `EMPLOYEE_APPROVED`, `LocalHR+` for `APPROVED`); 422 validation
error display.

**Files Changed**:
- `frontend/src/pages/SkemaPage.tsx` — save-format + flush-before-approve + read-only state
- `frontend/src/components/SkemaGrid.tsx`, `SkemaGrid.module.css` — read-only/greyed rendering
- `frontend/src/hooks/useSkema.ts` — reopen + status plumbing
- `frontend/src/lib/locale.ts` — Danish status helpers

### TASK-5502 — Approval dashboard: expandable detail + month navigation (frontend)

| Field | Value |
|-------|-------|
| **ID** | TASK-5502 (reconstructed id) |
| **Status** | complete |
| **Agent** | UX (reconstructed) |
| **Components** | Frontend — ApprovalDashboard, ApprovalDetailPanel, useApprovals |

**Description**: Expandable detail panel (read-only `SkemaGrid` + `BalanceSummary`); month
navigation with calendar view; status column with Danish badges; conditional action buttons
per status; `Genåbn` for `LocalHR+` on approved periods.

**Files Changed**:
- `frontend/src/pages/approval/ApprovalDashboard.tsx`, `ApprovalDashboard.module.css`
- `frontend/src/pages/approval/ApprovalDetailPanel.tsx` (new)
- `frontend/src/hooks/useApprovals.ts`
- `frontend/src/pages/approval/__tests__/ApprovalDashboard.test.tsx` (+29)

### TASK-5503 — Approval backend: by-month endpoint + role-branched reopen (backend)

| Field | Value |
|-------|-------|
| **ID** | TASK-5503 (reconstructed id) |
| **Status** | complete |
| **Agent** | Backend / API Integration (reconstructed) |
| **Components** | Backend.Api — ApprovalEndpoints; Infrastructure — ApprovalPeriodRepository; SharedKernel — PeriodReopened |
| **KB Refs** | ADR-012 |

**Description**: By-month endpoint with overlap date filter; role-branched reopen logic;
`PreviousStatus` on the `PeriodReopened` event; `agreementCode` in the pending response.

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs` (+139)
- `src/Infrastructure/StatsTid.Infrastructure/.../ApprovalPeriodRepository.cs` (+139)
- `src/SharedKernel/StatsTid.SharedKernel/Events/PeriodReopened.cs` (+1)

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | not recorded (no Step 7a artifacts exist for S55 under `.claude/reviews/`) |
| **Sprint-start commit** | `f13eaed` |

_No contemporaneous Step 7a review is on record. This is part of the governance gap this
reconstruction documents; it is not a claim that review was performed._

## Sprint Retrospective

**What happened**: S55 shipped as a single commit on master without a sprint log or recorded
Step 7a review — the sprint-close governance steps were skipped. The work itself
(approval-flow completion) is coherent and was followed immediately by S56.

**Process lesson**: this is the canonical case motivating the mechanical doc-consistency gate
(`tools/check_docs.py` sprint-inventory check) and the broadened sprint-close hook added in
the 2026-05-31 doc-health reconciliation — both would have flagged the missing S55 log.
