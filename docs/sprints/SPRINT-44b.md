# Sprint 44b — ADR-026 Sub-Sprint 2b (Config + Period + Overtime + UserAgreementCode mapper families + W1 fix)

| Field | Value |
|-------|-------|
| **Sprint** | 44b |
| **Status** | provisional (sprint-open) |
| **Start Date** | 2026-05-24 |
| **End Date** | _pending_ |
| **Sprint-start commit base** | `f4b69ef` (S44 close) |
| **Sprint type** | Implementation (cutover-class continuation) |
| **Phase** | 4e (Phase E audit visibility — ADR-026 Sub-Sprint 2b) |
| **Refinement** | `.claude/refinements/REFINEMENT-s44b-adr026-sub-sprint-2b.md` (gitignored; Step 4 cycle 1 absorbed) |

## Sprint Goal

Ship 16 mapper implementations across 4 families (AgreementConfig 5 + Period 5 + Overtime 3 + UserAgreementCode 3) with 17 endpoint cutover sites across 4 endpoint files. Fix S44 convergent W1 (UserRepository `(conn, tx)` overload). Close `UserAgreementCodeSoftDeleted` catalog gap. ~9 representative D-tests.

## Tasks

| Task | Status | Owner | Notes |
|------|--------|-------|-------|
| TASK-44B-00 | in_progress | Orchestrator | Sprint open |
| TASK-44B-01 | pending | Orchestrator | UserRepository.GetByIdAsync (conn, tx) overload + AdminEndpoints W1 fix |
| TASK-44B-02 | pending | Builder | 5 AgreementConfig mappers + DI |
| TASK-44B-03 | pending | Builder | 5 Period mappers + DI |
| TASK-44B-04 | pending | Builder | 3 Overtime mappers + DI |
| TASK-44B-05 | pending | Builder | 3 UserAgreementCode mappers + DI |
| TASK-44B-06 | pending | Builder | AgreementConfigEndpoints 6 cutover sites |
| TASK-44B-07 | pending | Builder | ApprovalEndpoints 5 cutover sites |
| TASK-44B-08 | pending | Builder | OvertimeEndpoints 3 cutover sites |
| TASK-44B-09 | pending | Builder | AdminEndpoints UserAgreementCode 3 cutover sites |
| TASK-44B-10 | pending | Orchestrator | Catalog updates (16 rows + gap closure) |
| TASK-44B-11 | pending | Builder | ~9 Docker-gated D-tests |
| TASK-44B-12 | pending | Orchestrator | Sprint close |

## Forward Pointers

- **S44c** = remaining mapper families: EmployeeProfile* (4 events, HR-sensitive payload-redaction check), PositionOverride* (4 events), WageTypeMapping* (4 events), EntitlementConfig* (4 events), LocalAgreementProfileChanged, LocalConfigurationChanged + new ADR-024/ADR-025 events (RoleConfigOverride* 4, MerarbejdeDiscretionary, OvertimeNecessityAcknowledged, ConfigBugCorrected)
- **S44f** = GET /api/admin/audit endpoint + AuditLogView.tsx + Phase E Test #1/#3/#4
