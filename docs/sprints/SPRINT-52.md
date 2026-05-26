# Sprint 52 — ADR-027 Cleanup: Transitive My Reports, CSV Import, ManagerDeactivated

| Field | Value |
|-------|-------|
| **Sprint** | 52 |
| **Status** | complete |
| **Start Date** | 2026-05-25 |
| **End Date** | 2026-05-25 |
| **Orchestrator Approved** | yes — 2026-05-25 |
| **Build Verified** | yes — 0 errors |
| **Test Verified** | yes — 997 total (unchanged from S51) |

## Sprint Goal

Close three deferred items from the ADR-027 reporting-line hierarchy rollout, plus seed enforcement and self-delegation examples for testing.

## Task Log

| Task | Description | Status |
|------|------------|--------|
| TASK-5201 | Transitive "My Reports" — recursive CTE in GetPendingForDesignatedReportsAsync walks the reporting chain so department heads see all subordinates' periods | complete |
| TASK-5202 | CSV import adapter — frontend parses .csv files (header + comma-separated rows) and feeds the existing JSON import endpoint | complete |
| TASK-5203 | ReportingLineManagerDeactivated event — fires when admin deactivates a user who is a manager, emitting per-employee events inside the existing atomic transaction | complete |
| TASK-5204 | Seed data: STY02 tree with enforcement_mode=REQUIRED, emp005 SELF_DELEGATION ACTING line with scheduled_expiry | complete |

## Commits

| Commit | Description |
|--------|------------|
| `01ce2e5` | S52 ADR-027 cleanup: transitive My Reports, CSV import, ManagerDeactivated event |
| `646a345` | S52 seed data: enforcement + self-delegation examples |

## Test Summary

| Category | Before (S51) | New | After |
|----------|:---:|:---:|:---:|
| Unit | 546 | 0 | 546 |
| Plain regression | 44 | 0 | 44 |
| Docker-gated | 297 | 0 | 297 |
| Frontend vitest | 110 | 0 | 110 |
| **Total** | **997** | **0** | **997** |

## Files Changed

- `frontend/src/pages/admin/ReportingLineTree.tsx` — CSV import adapter
- `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` — ManagerDeactivated event emission
- `src/Infrastructure/StatsTid.Infrastructure/ApprovalPeriodRepository.cs` — recursive CTE for transitive reports
- `src/Infrastructure/StatsTid.Infrastructure/ReportingLineRepository.cs` — transitive query support
- `docker/postgres/init.sql` — seed data (enforcement + self-delegation)
