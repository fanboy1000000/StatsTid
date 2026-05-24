# Sprint 45 — ADR-026 Cross-Process Sprint (RetroactiveCorrectionRequested)

| Field | Value |
|-------|-------|
| **Sprint** | 45 |
| **Status** | provisional (sprint-open) |
| **Start Date** | 2026-05-24 |
| **End Date** | _pending_ |
| **Sprint-start commit base** | `00ecaea` (S44f close) |
| **Sprint type** | Implementation (cross-process audit bridge) |
| **Phase** | 4e (Phase E audit visibility — cross-process deferral close) |
| **Refinement** | `.claude/refinements/REFINEMENT-s45-cross-process.md` (gitignored; Step 4 2 BLOCKERs absorbed) |

## Sprint Goal

Convert RetroactiveCorrectionService from IEventStore.AppendAsync to IOutboxEnqueue.EnqueueAndReturnIdAsync + audit projection. Closes TBD-cross-process-deferred catalog marker.

## Tasks

| Task | Status | Owner | Notes |
|------|--------|-------|-------|
| TASK-45-00 | in_progress | Orchestrator | Sprint open |
| TASK-45-01 | pending | Builder | RetroactiveCorrectionRequestedAuditMapper in Infrastructure |
| TASK-45-02 | pending | Builder | DI registrations (both Program.cs files) |
| TASK-45-03 | pending | Builder | RetroactiveCorrectionService conversion |
| TASK-45-04 | pending | Orchestrator | Catalog + Test #1 update |
| TASK-45-05 | pending | Builder | 2 D-tests |
| TASK-45-06 | pending | Orchestrator | Sprint close |
