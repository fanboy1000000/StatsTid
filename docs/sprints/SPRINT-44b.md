# Sprint 44b — ADR-026 Sub-Sprint 2b (Config + Period + Overtime + UserAgreementCode mapper families + W1 fix)

| Field | Value |
|-------|-------|
| **Sprint** | 44b |
| **Status** | complete |
| **Start Date** | 2026-05-24 |
| **End Date** | 2026-05-24 |
| **Sprint-start commit base** | `f4b69ef` (S44 close) |
| **Sprint-close commit** | _this commit_ |
| **Build Verified** | 0 errors / 0 net-new warnings |
| **Test Verified** | 9 net new Docker-gated D-tests; 902 total |
| **Orchestrator Approved** | yes — 2026-05-24 |
| **Sprint type** | Implementation (cutover-class continuation) |
| **Phase** | 4e (Phase E audit visibility — ADR-026 Sub-Sprint 2b) |
| **Refinement** | `.claude/refinements/REFINEMENT-s44b-adr026-sub-sprint-2b.md` (gitignored; Step 4 cycle 1 absorbed) |

## Sprint Goal

Ship 16 mapper implementations across 4 families + 17 endpoint cutover sites + W1 fix + catalog updates + 9 D-tests.

## Tasks

| Task | Status | Commit | Notes |
|------|--------|--------|-------|
| TASK-44B-00 | complete | `ee260b6` | Sprint open |
| TASK-44B-01 | complete | `4967b90` | UserRepository.GetByIdAsync(conn, tx) overload + W1 fix |
| TASK-44B-02..05 | complete | `f0542ff` | 16 mappers + DI registrations |
| TASK-44B-06 | complete | `46b3028` | AgreementConfigEndpoints 6 cutover sites |
| TASK-44B-07 | complete | `cede6ec` | ApprovalEndpoints 5 cutover sites |
| TASK-44B-08 | complete | `6d866d7` | OvertimeEndpoints 3 cutover sites |
| TASK-44B-09 | complete | `ffca0a3` | AdminEndpoints UserAgreementCode 3 cutover sites |
| TASK-44B-10 | complete | `91bd84e` | Catalog updates (22 rows landed + gap closures) |
| TASK-44B-11 | complete | `cb91e7b` | 9 D-tests (4 happy + 4 rollback + 1 dual-emit) |
| TASK-44B-12 | complete | _this commit_ | Sprint close (Step 7a + artifacts) |

## Step 7a Outcome

Codex **APPROVED-AFTER-ABSORPTION** (1 P1 + 2 P2 + 5 N). Reviewer **APPROVED-WITH-WARNINGS** (0 B + 1 W + 3 N). Cycle-cap = 1 per lens.

**P1-1 (Codex BLOCKER — FIXED in `8f73e53`)**: Null-user lookup at 8 TENANT_TARGETED sites crashes against `chk_target_org_required_when_tenant` CHECK constraint if employee is soft-deleted. Fixed: null-coalescing throw at all 8 sites matching S44 RoleAssignmentRevoked pattern.

**Reviewer W1 (deferred)**: OvertimePreApprovalCreated uses `EmployeeId` as TargetResourceId (Approved/Rejected use `PreApprovalId`) — pre-existing event schema gap; no `PreApprovalId` property on the Created event class.

## Test Counts

| Suite | S44 | S44b | Delta |
|-------|-----|------|-------|
| Unit | 526 | 526 | 0 |
| Plain regression | 40 | 40 | 0 |
| Docker-gated | 237 | 246 | +9 |
| Frontend vitest | 90 | 90 | 0 |
| **Total** | **893** | **902** | **+9** |

## Forward Pointers

- **S44c** = remaining mapper families: EmployeeProfile* (4), PositionOverride* (4), WageTypeMapping* (4), EntitlementConfig* (4), LocalAgreementProfileChanged, LocalConfigurationChanged + new ADR-024/ADR-025 events (RoleConfigOverride* 4, MerarbejdeDiscretionary, OvertimeNecessityAcknowledged, ConfigBugCorrected) + Reviewer W1 fix (OvertimePreApprovalCreated.PreApprovalId schema gap)
- **S44f** = GET /api/admin/audit + AuditLogView.tsx + Phase E Test #1/#3/#4
