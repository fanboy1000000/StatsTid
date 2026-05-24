# Sprint 44c — ADR-026 Sub-Sprint 2c (Catalog close — remaining mapper families)

| Field | Value |
|-------|-------|
| **Sprint** | 44c |
| **Status** | provisional (sprint-open) |
| **Start Date** | 2026-05-24 |
| **End Date** | _pending_ |
| **Sprint-start commit base** | `2070e1d` (S44b close) |
| **Sprint type** | Implementation (cutover-class completion) |
| **Phase** | 4e (Phase E audit visibility — ADR-026 Sub-Sprint 2c, catalog close) |
| **Refinement** | `.claude/refinements/REFINEMENT-s44c-adr026-sub-sprint-2c.md` (gitignored; Step 4 cycle 1 absorbed) |

## Sprint Goal

Ship 25 mappers + ~16 endpoint cutovers to close the audit-projection catalog. After S44c, only 6 explicitly deferred TBD-* rows remain.

## Tasks

| Task | Status | Owner | Notes |
|------|--------|-------|-------|
| TASK-44C-00 | in_progress | Orchestrator | Sprint open |
| TASK-44C-01 | pending | Builder | 4 PositionOverride mappers |
| TASK-44C-02 | pending | Builder | 4 WageTypeMapping mappers |
| TASK-44C-03 | pending | Builder | 4 EntitlementConfig mappers (incl Seeded — mapper-only) |
| TASK-44C-04 | pending | Builder | 4 EmployeeProfile mappers |
| TASK-44C-05 | pending | Builder | 2 mappers (LocalAgreementProfileChanged + LocalConfigurationChanged) |
| TASK-44C-06 | pending | Builder | 7 ADR-024 mappers (mapper-only, no emit sites) |
| TASK-44C-07 | pending | Builder | PositionOverrideEndpoints 4 cutover sites |
| TASK-44C-08 | pending | Builder | WageTypeMappingEndpoints cutover sites |
| TASK-44C-09 | pending | Builder | EntitlementConfigEndpoints cutover sites |
| TASK-44C-10 | pending | Builder | EmployeeProfileEndpoints 3 cutover sites |
| TASK-44C-11 | pending | Builder | AdminEndpoints EmployeeProfileCreated 1 cutover site |
| TASK-44C-12 | pending | Builder | ConfigEndpoints LocalAgreementProfileChanged 1 cutover site |
| TASK-44C-13 | pending | Orchestrator | Catalog close (25 rows → interface) |
| TASK-44C-14 | pending | Builder | ~10 D-tests |
| TASK-44C-15 | pending | Orchestrator | Sprint close |
