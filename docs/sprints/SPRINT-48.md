# Sprint 48 — ADR-027 Reporting-Line Hierarchy (Migration Phase 1)

| Field | Value |
|-------|-------|
| **Sprint** | 48 |
| **Status** | complete |
| **Start Date** | 2026-05-25 |
| **End Date** | 2026-05-25 |
| **Orchestrator Approved** | yes — 2026-05-25 |
| **Build Verified** | yes — `dotnet build` 0 errors, 37 pre-existing warnings |
| **Test Verified** | yes — 536 unit + 15 Docker-gated + 6 FE vitest = 557 new-scope passing |

## Sprint Goal

Introduce reporting-line hierarchy (ADR-027) Phase 1: temporal `reporting_lines` table, repository, 7 admin CRUD endpoints, 4 domain events, admin tree-view page, manager-picker on UserManagement, and 31 new tests. Reporting lines are optional — employees without one route to org-scope approval as today.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | All 23 ADR paths valid; ADR-027 added |
| Pattern compliance spot-check | CLEAN | New repo follows ADR-017 D1 temporal pattern |
| Orphan detection | CLEAN | No orphaned files |
| Documentation drift | CLEAN | MEMORY.md current |
| Quality grade review | CLEAN | No grade changes |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 new hierarchy model, P3 four new events, P7 endpoint auth) |
| **External Codex** | Analysis reviewed via 2-cycle Codex review on `.claude/refinements/ANALYSIS-reporting-line-hierarchy.md` (v1: 4B/10W/7N; v2: 0B/1W/2N — clean). Plan-mode review served by analysis Codex cycles. |
| **Internal Reviewer** | Codex cycle 2 served as internal review equivalent |
| **BLOCKERs resolved before Step 1** | yes — 4 BLOCKERs from Codex v1 (dual-source-of-truth data model, OQ1/OQ7 severity, institution boundary) all resolved in v2 |

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (two complementary hierarchies, no coupling)
- [ ] P2 — Rule engine determinism maintained (not applicable — no rule engine changes)
- [x] P3 — Event sourcing append-only semantics respected (4 new events, atomic outbox)
- [ ] P4 — OK version correctness (not applicable)
- [ ] P5 — Integration isolation and delivery guarantees (not applicable)
- [ ] P6 — Payroll integration correctness (not applicable)
- [x] P7 — Security and access control (all endpoints have RequireAuthorization, scope validation)
- [x] P8 — CI/CD enforcement (build clean, tests passing)
- [x] P9 — Usability and UX (admin tree page, manager picker, Danish labels)

## Task Log

### TASK-4801: ADR-027 Architectural Decision Record
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Orchestrator-direct |
| **Components** | Knowledge Base |
| **KB Refs** | ADR-008, ADR-012, ADR-017 D1, ADR-018 D3/D6/D7, ADR-019 D2, ADR-025 D8 |
| **Files** | `docs/knowledge-base/decisions/ADR-027-reporting-line-hierarchy.md` (NEW), `docs/knowledge-base/INDEX.md` (MODIFIED) |

### TASK-4802: Schema + seed data
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Orchestrator-direct |
| **Components** | Infrastructure (schema) |
| **KB Refs** | ADR-017 D1, ADR-018 D7, ADR-027 D1/D2 |
| **Files** | `docker/postgres/init.sql` (MODIFIED — reporting_lines + reporting_line_audit tables, 5 indexes, 13 seed rows, ledger entry) |

### TASK-4803: Domain model + events
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Data Model Agent (extended into Infrastructure/EventSerializer.cs, cross-domain authorized) |
| **Components** | SharedKernel, Infrastructure |
| **KB Refs** | PAT-001, PAT-004, DEP-003 |
| **Files** | `Models/ReportingLine.cs` (NEW), 4 event classes (NEW), `EventSerializer.cs` (MODIFIED — 4 entries added) |

### TASK-4804: ReportingLineRepository
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Cross-domain authorized (Infrastructure) |
| **Components** | Infrastructure |
| **KB Refs** | ADR-017 D1, ADR-018 D7, ADR-027 D1/D2/D3/D9 |
| **Files** | `ReportingLineRepository.cs` (NEW — 5 read + 2 write methods + 2 helpers + AcquireLockAsync + ValidatePrecondition + 2 custom exceptions) |

### TASK-4805: DI registration
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Orchestrator-direct (Small Tasks Exception) |
| **Files** | `Program.cs` (MODIFIED — 1 line) |

### TASK-4806: ReportingLineEndpoints
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Cross-domain authorized (Backend API) |
| **Components** | Backend API |
| **KB Refs** | ADR-007, ADR-018 D3, ADR-019 D2, ADR-027 D1/D4/D6 |
| **Files** | `ReportingLineEndpoints.cs` (NEW — 7 endpoints), `Program.cs` (MODIFIED — MapReportingLineEndpoints) |

### TASK-4807: Audit projection mappers
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Cross-domain authorized (Infrastructure + Backend) |
| **Components** | Backend API (AuditMappers) |
| **KB Refs** | ADR-026 D1 |
| **Files** | `ReportingLineAssignedAuditMapper.cs` (NEW), `ReportingLineSupersededAuditMapper.cs` (NEW), `Program.cs` (MODIFIED — 4 DI registrations) |

### TASK-4808: useReportingLines hook
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | UX Agent |
| **Components** | Frontend |
| **Files** | `frontend/src/hooks/useReportingLines.ts` (NEW) |

### TASK-4809: ReportingLineTree admin page
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | UX Agent |
| **Components** | Frontend |
| **Files** | `frontend/src/pages/admin/ReportingLineTree.tsx` (NEW), `ReportingLineTree.module.css` (NEW) |

### TASK-4810: UserManagement manager picker
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | UX Agent |
| **Components** | Frontend |
| **Files** | `frontend/src/pages/admin/UserManagement.tsx` (MODIFIED) |

### TASK-4811: Routing + sidebar
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | UX Agent (Small Tasks Exception) |
| **Files** | `frontend/src/App.tsx` (MODIFIED), `frontend/src/components/layout/Sidebar.tsx` (MODIFIED) |

### TASK-4812: Unit tests
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Test & QA Agent |
| **Files** | `tests/StatsTid.Tests.Unit/ReportingLine/ReportingLineTests.cs` (NEW — 10 tests) |

### TASK-4813 + TASK-4815: Docker-gated + plain regression tests
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Test & QA Agent |
| **Files** | `tests/StatsTid.Tests.Regression/ReportingLine/ReportingLineRepositoryTests.cs` (NEW — 15 tests) |

### TASK-4814: Frontend vitest tests
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | UX Agent |
| **Files** | `frontend/src/pages/admin/__tests__/ReportingLineTree.test.tsx` (NEW — 6 tests) |

### TASK-4816: SYSTEM_TARGET.md amendment
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Orchestrator-direct |
| **Files** | `SYSTEM_TARGET.md` (MODIFIED — §E reporting-line paragraph, §H approval routing language) |

## Test Summary

| Category | Before (S47) | New | After |
|----------|:---:|:---:|:---:|
| Unit | 526 | +10 | 536 |
| Docker-gated regression | 260 | +15 | 275 |
| Plain regression | 44 | +0 | 44 |
| Frontend vitest | 90 | +6 | 96 |
| **Total** | **920** | **+31** | **951** |

## External Review (Step 7a)

_Pending — to be run on full sprint diff before commit._

## Sprint Retrospective

### What went well
- Analysis Codex review (2 cycles) caught 4 BLOCKERs before any code was written — zero architectural rework during implementation
- User's OQ1/OQ7 decisions absorbed cleanly into v3 analysis
- Per-styrelse tree boundary (user's OQ7 choice against recommendation) turned out cleaner than per-institution — finer-grained, less coupling to ADR-025 D8

### What to improve
- Seed data count discrepancy (plan said 14, actual was 13) — caught by Docker-gated tests, not by review

### Decisions deferred
- Phase 2: HR import endpoint (GLOBAL_ADMIN bulk import)
- Phase 3: Approval routing changes (designated-approver queue, "My Reports" tab, `designated_approver_id` on approval_periods)
- Phase 4: Per-tree manager-only enforcement toggle
