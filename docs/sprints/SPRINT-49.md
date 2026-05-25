# Sprint 49 — ADR-027 Phase 2+3: HR Import + Approval Routing

| Field | Value |
|-------|-------|
| **Sprint** | 49 |
| **Status** | complete |
| **Start Date** | 2026-05-25 |
| **End Date** | 2026-05-25 |
| **Orchestrator Approved** | yes — 2026-05-25 |
| **Build Verified** | yes — `dotnet build` 0 errors |
| **Test Verified** | yes — 538 unit + 21 Docker-gated + 12 FE vitest (S49 scope) |

## Sprint Goal

Make the S48 reporting-line infrastructure operational: (1) HR bulk import endpoint for tree population at scale, (2) designated-approver resolution with ACTING-precedence, (3) "My Reports" tab on ApprovalDashboard, (4) `designated_approver_id` + `approval_method` audit trail on approval_periods.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (reporting line = routing, not authorization)
- [ ] P2 — Rule engine determinism (not applicable)
- [x] P3 — Event sourcing (5th event FallbackTraversalWarning, bulk import event)
- [ ] P4 — OK version correctness (not applicable)
- [ ] P5 — Integration isolation (not applicable)
- [ ] P6 — Payroll integration (not applicable)
- [x] P7 — Security (GLOBAL_ADMIN on import, org-scope intersection on my-reports)
- [x] P8 — CI/CD (build clean, tests passing)
- [x] P9 — UX (tabs, import preview, Danish labels)

## Task Log

### TASK-4901: Schema — approval_periods routing columns
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Orchestrator-direct |
| **Files** | `docker/postgres/init.sql` — ADD designated_approver_id + approval_method columns, ledger entry |

### TASK-4902: FallbackTraversalWarning event
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Data Model Agent (cross-domain authorized) |
| **Files** | `Events/FallbackTraversalWarning.cs` (NEW), `EventSerializer.cs` (MODIFIED) |

### TASK-4903: ResolveDesignatedApproverAsync
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Cross-domain authorized (Infrastructure) |
| **Files** | `ReportingLineRepository.cs` (MODIFIED — new method + 2 private helpers) |

### TASK-4904: GetPendingForDesignatedReportsAsync
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Cross-domain authorized (Infrastructure) |
| **Files** | `ApprovalPeriodRepository.cs` (MODIFIED — new query with ACTING-precedence + org-scope intersection) |

### TASK-4905: POST /api/admin/reporting-lines/import
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Cross-domain authorized (Backend API) |
| **Files** | `ReportingLineEndpoints.cs` (MODIFIED — 8th endpoint, pre-validation + atomic batch) |

### TASK-4906: Approval routing on approve/reject
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Cross-domain authorized (Backend API) |
| **Files** | `ApprovalEndpoints.cs` (MODIFIED — designated-approver resolution + FallbackTraversalWarning emission), `ApprovalPeriodRepository.cs` (MODIFIED — UpdateStatusAsync + BuildUpdateStatusCommand gain routing params) |

### TASK-4907: GET /pending?my-reports=true
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Cross-domain authorized (Backend API) |
| **Files** | `ApprovalEndpoints.cs` (MODIFIED — my-reports query param + designated-reports branch) |

### TASK-4908: ReportingLineBulkImportedAuditMapper
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Cross-domain authorized (Backend + Infrastructure) |
| **Files** | `ReportingLineBulkImportedAuditMapper.cs` (NEW), `Program.cs` (MODIFIED) |

### TASK-4909: ApprovalDashboard tabs
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | UX Agent |
| **Files** | `ApprovalDashboard.tsx` (MODIFIED — Tabs component, PendingTable extract), `useApprovals.ts` (MODIFIED — usePendingMyReports) |

### TASK-4910: Bulk import UI
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | UX Agent |
| **Files** | `ReportingLineTree.tsx` (MODIFIED — import dialog), `useReportingLines.ts` (MODIFIED — importLines) |

### TASK-4911+4912: Backend tests
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Test & QA Agent |
| **Files** | `ReportingLineTests.cs` (+2 unit), `ReportingLineRepositoryTests.cs` (+6 Docker-gated), `ApprovalPeriod.cs` + `ApprovalPeriodRepository.cs` (model+mapper for new columns) |

### TASK-4913: Frontend vitest tests
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | UX Agent |
| **Files** | `ApprovalDashboard.test.tsx` (NEW — 4 tests), `ReportingLineTree.test.tsx` (+2 import tests) |

### TASK-4914: ADR-027 amendment
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Orchestrator-direct |
| **Files** | `ADR-027-reporting-line-hierarchy.md` (MODIFIED — D8 Phase 2+3 merged, D10 5th event) |

## Test Summary

| Category | Before (S48) | New | After |
|----------|:---:|:---:|:---:|
| Unit | 536 | +2 | 538 |
| Docker-gated | 275 | +6 | 281 |
| Plain regression | 44 | +0 | 44 |
| Frontend vitest | 96 | +6 | 102 |
| **Total** | **951** | **+14** | **965** |

## External Review (Step 7a)

_Pending_

## Sprint Retrospective

### What went well
- Refinement Codex review caught 3 BLOCKERs (ACTING-precedence, org-scope intersection, missing FallbackTraversalWarning event) before any code was written
- Phase 2+3 combined cleanly into one sprint — the import and routing features complement each other

### Decisions made
- ACTING-precedence strict: PRIMARY manager doesn't see employee in "My Reports" while ACTING is active
- `designated_approver_id` promoted from Phase 4 to S49 (audit trail for routing)
- FallbackTraversalWarning added as 5th reporting-line event type

### Deferred to Phase 4
- Per-tree manager-only enforcement toggle
- Self-service acting-manager delegation UI
- Transitive "My Reports" (reports of reports)
- CSV import adapter
