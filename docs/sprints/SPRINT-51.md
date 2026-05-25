# Sprint 51 — Self-Service Acting-Manager Delegation

| Field | Value |
|-------|-------|
| **Sprint** | 51 |
| **Status** | complete |
| **Start Date** | 2026-05-25 |
| **End Date** | 2026-05-25 |
| **Orchestrator Approved** | yes — 2026-05-25 |
| **Build Verified** | yes — 0 errors |
| **Test Verified** | yes — 20 unit + 37 Docker-gated + 4 FE vitest (S51 scope) |

## Sprint Goal

Self-service delegation for managers going on vacation. Creates ACTING lines for PRIMARY direct reports with auto-expiration via `DelegationExpiryService`. Resolves the two Codex BLOCKERs deferred from S50 (temporal auto-expiration + org-scope validation).

## Task Log

| Task | Description | Status |
|------|------------|--------|
| TASK-5101 | Schema: scheduled_expiry + SELF_DELEGATION source | complete |
| TASK-5102 | ReportingLine model + repo update (ScheduledExpiry property) | complete |
| TASK-5103 | DelegationExpiryService (BackgroundService, 5-min poll, atomic closure) | complete |
| TASK-5104 | POST/DELETE/GET /api/reporting-lines/delegate endpoints | complete |
| TASK-5105 | Scope validation (acting manager covers all direct reports) | complete |
| TASK-5106 | useDelegation hook | complete |
| TASK-5107 | DelegationPage.tsx + CSS module | complete |
| TASK-5108 | Routing (/delegation) + sidebar (Vikariering) | complete |
| TASK-5109 | Unit tests (+4) | complete |
| TASK-5110 | Docker-gated tests (+8) | complete |
| TASK-5111 | Frontend vitest tests (+4) | complete |
| TASK-5112 | ADR-027 D12 amendment | complete |

## Test Summary

| Category | Before (S50) | New | After |
|----------|:---:|:---:|:---:|
| Unit | 542 | +4 | 546 |
| Docker-gated | 289 | +8 | 297 |
| Frontend vitest | 106 | +4 | 110 |
| **Total** | **981** | **+16** | **997** |

## Sprint Retrospective

### What went well
- Both deferred BLOCKERs (B3 temporal, B4 scope) resolved cleanly without changing existing read queries
- `scheduled_expiry` + BackgroundService pattern is non-invasive — zero regression risk on existing ACTING semantics

### Decisions made
- `effectiveFrom` is server-derived (today) — no future-dated delegations
- One active delegation per manager (409 if duplicate)
- Admin ACTING lines take precedence over self-delegation (skipped during delegation)
- `ReportingLineSelfDelegated` batch event for audit trail
