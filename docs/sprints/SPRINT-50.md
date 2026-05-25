# Sprint 50 — ADR-027 Phase 4: Enforcement Toggle

| Field | Value |
|-------|-------|
| **Sprint** | 50 |
| **Status** | complete |
| **Start Date** | 2026-05-25 |
| **End Date** | 2026-05-25 |
| **Orchestrator Approved** | yes — 2026-05-25 |
| **Build Verified** | yes — 0 errors |
| **Test Verified** | yes — 16 unit + 29 Docker-gated + 16 FE vitest (S50 scope) |

## Sprint Goal

Close the ADR-027 rollout with an opt-in enforcement toggle: per-tree REQUIRED mode where non-designated approvers get 428 + must confirm fallback. Population gate prevents enablement on unpopulated trees.

## Task Log

| Task | Description | Status |
|------|------------|--------|
| TASK-5001 | Schema: reporting_line_tree_settings + explicit_fallback_confirmation | complete |
| TASK-5002 | PeriodApproved/PeriodRejected event extension | complete |
| TASK-5003 | TreeSettingsRepository | complete |
| TASK-5004 | Population gate helper | complete |
| TASK-5005 | DI registration | complete |
| TASK-5006 | Settings GET/PUT endpoints | complete |
| TASK-5007 | Enforcement logic on approve/reject | complete |
| TASK-5008 | ApprovalDashboard enforcement confirmation dialog | complete |
| TASK-5009 | ReportingLineTree settings toggle | complete |
| TASK-5010 | Unit tests (+4) | complete |
| TASK-5011 | Docker-gated tests (+8) | complete |
| TASK-5012 | Frontend vitest tests (+4) | complete |
| TASK-5013 | ADR-027 amendment (D8+D11) | complete |
| TASK-5014 | SYSTEM_TARGET.md amendment | complete |

## Test Summary

| Category | Before (S49) | New | After |
|----------|:---:|:---:|:---:|
| Unit | 538 | +4 | 542 |
| Docker-gated | 281 | +8 | 289 |
| Frontend vitest | 102 | +4 | 106 |
| **Total** | **965** | **+16** | **981** |

## External Review (Step 7a)

Codex cycle 1: 0B/2W — (1) population gate excluded root-only trees, (2) settings endpoint accepted descendant org IDs. Both fixed.
