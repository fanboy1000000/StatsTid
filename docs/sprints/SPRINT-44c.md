# Sprint 44c — ADR-026 Sub-Sprint 2c (Catalog close — remaining mapper families)

| Field | Value |
|-------|-------|
| **Sprint** | 44c |
| **Status** | complete |
| **Start Date** | 2026-05-24 |
| **End Date** | 2026-05-24 |
| **Sprint-start commit base** | `2070e1d` (S44b close) |
| **Sprint-close commit** | _this commit_ |
| **Build Verified** | 0 errors / 0 net-new warnings |
| **Test Verified** | 10 net new D-tests (5 happy + 4 rollback + 1 mapper-only); 912 total |
| **Orchestrator Approved** | yes — 2026-05-24 |
| **Sprint type** | Implementation (cutover-class completion) |
| **Phase** | 4e (Phase E audit visibility — ADR-026 Sub-Sprint 2c, catalog close) |
| **Refinement** | `.claude/refinements/REFINEMENT-s44c-adr026-sub-sprint-2c.md` (gitignored; Step 4 cycle 1 absorbed) |

## Sprint Goal

Ship 25 mappers + ~16 endpoint cutovers to close the audit-projection catalog. After S44c, 47 of 53 catalog rows have `mapper_kind=interface`; only 6 explicitly deferred TBD-* rows remain.

## Tasks

| Task | Status | Commit | Notes |
|------|--------|--------|-------|
| TASK-44C-00 | complete | `2797ae1` | Sprint open |
| TASK-44C-01..06 | complete | `2f08d64` | 25 mappers + 25 DI registrations |
| TASK-44C-07 | complete | `43d18c4` | PositionOverrideEndpoints 4 cutover sites |
| TASK-44C-08 | complete | `3272a6d` | WageTypeMappingEndpoints cutover sites |
| TASK-44C-09 | complete | `df8828b` | EntitlementConfigEndpoints cutover sites |
| TASK-44C-10 | complete | `2e15850` | EmployeeProfileEndpoints 3 cutover sites |
| TASK-44C-11 | complete | `88caa81` | AdminEndpoints EmployeeProfileCreated 1 cutover site |
| TASK-44C-12 | complete | `ca12be1` | ConfigEndpoints LocalAgreementProfileChanged 1 cutover site |
| TASK-44C-13 | complete | `d43da90` | Catalog close (47 of 53 rows landed) |
| TASK-44C-14 | complete | `fbeb9b2` | 10 D-tests |
| TASK-44C-15 | complete | _this commit_ | Sprint close |

## Step 7a Outcome

Codex **APPROVED-WITH-WARNINGS** (0B/1W/2N). Reviewer **APPROVED-AFTER-ABSORPTION** (0B/2W/2N). Cycle-cap = 1 per lens.

**Reviewer W1 (FIXED in `e62fac3`)**: EmployeeProfileEndpoints Update/Supersede used `DateTimeOffset.UtcNow` instead of `new DateTimeOffset(@event.OccurredAt)` — microsecond drift from event timestamp. Fixed via record `with` expression.

**Codex W1 (deferred)**: EmployeeProfileSoftDeleted outbox enqueue before user fetch — architecturally fragile but unreachable since SoftDeleteAsync doesn't set `users.is_active=FALSE`.

**Reviewer W2 (noted)**: AdminEndpoints 4th-sprint coupling growth — no structural action needed.

## Test Counts

| Suite | S44b | S44c | Delta |
|-------|------|------|-------|
| Unit | 526 | 526 | 0 |
| Plain regression | 40 | 40 | 0 |
| Docker-gated | 246 | 256 | +10 |
| Frontend vitest | 90 | 90 | 0 |
| **Total** | **902** | **912** | **+10** |

## Cumulative ADR-026 Sub-Sprint 2 progress

| Sprint | Mappers | Endpoint cutovers | Catalog rows landed | Total |
|--------|---------|-------------------|---------------------|-------|
| S44 | 6 | 6 | 6 | 6 |
| S44b | 16 | 17 | 22 | 22 |
| S44c | 25 | ~16 | 47 | 47 |
| **Remaining** | — | — | **6 deferred TBD-*** | |

## Forward Pointers

- **S44f** = GET /api/admin/audit endpoint + AuditLogView.tsx + Phase E Test #1 (catalog ↔ DI ↔ EventSerializer parity) + Test #3 (sync-in-tx assertion) + Test #4 (per-class visibility enforcement)
- **S44-cross-process** = dedicated 1-event sprint for RetroactiveCorrectionRequested
