# Sprint 44f — ADR-026 Sub-Sprint 2f (GET endpoint + frontend + Phase E validation tests)

| Field | Value |
|-------|-------|
| **Sprint** | 44f |
| **Status** | provisional (sprint-open) |
| **Start Date** | 2026-05-24 |
| **End Date** | _pending_ |
| **Sprint-start commit base** | `aa5cf5e` (S44c close) |
| **Sprint type** | Implementation (endpoint + frontend + validation) |
| **Phase** | 4e (Phase E audit visibility — ADR-026 Sub-Sprint 2f, closing) |
| **Refinement** | `.claude/refinements/REFINEMENT-s44f-adr026-sub-sprint-2f.md` (gitignored; Step 4 absorbed) |

## Sprint Goal

Ship the audit read surface (GET endpoint + frontend page) and 3 programmatic validation tests. Closes ADR-026 Sub-Sprint 2.

## Tasks

| Task | Status | Owner | Notes |
|------|--------|-------|-------|
| TASK-44F-00 | in_progress | Orchestrator | Sprint open |
| TASK-44F-01 | pending | Builder | AuditEndpoints.cs (GET /api/admin/audit) |
| TASK-44F-02 | pending | UX | AuditLogView.tsx + sidebar wiring |
| TASK-44F-03 | pending | Builder | Phase E Test #1 (catalog ↔ DI ↔ EventSerializer parity) |
| TASK-44F-04 | pending | Builder | Phase E Test #3 (sync-in-tx outbox_id linkage) |
| TASK-44F-05 | pending | Builder | Phase E Test #4 (per-class visibility enforcement) |
| TASK-44F-06 | pending | Orchestrator | PayrollExportGenerated catalog note |
| TASK-44F-07 | pending | Orchestrator | Sprint close |
