# Sprint 40 — ADR-024 Sub-Sprint 1 (Schema + Repository + Events)

| Field | Value |
|-------|-------|
| **Sprint** | 40 |
| **Status** | **in-progress** |
| **Start Date** | 2026-05-23 |
| **End Date** | _filled by close_ |
| **Orchestrator Approved** | _filled by close_ |
| **Build Verified** | _filled by close_ |
| **Test Verified** | _filled by close (target: 869 baseline + ~5 Phase E tests = ~874)_ |
| **Sprint-start commit base** | `3a6f41a` (S39 close, 2026-05-23) |
| **Sprint-end HEAD** | _filled by close_ |
| **Sprint type** | Implementation (schema + plumbing only; no cutover code) |
| **Plan** | `.claude/plans/PLAN-s40.md` |
| **Phase** | 4e (Phase D Implementation Sub-Sprint 1 per ADR-024 L234) |

## Sprint Goal

Lay the architectural foundation for ADR-024 (role-within-agreement modeling + correction policy + overtime authorization) without any rule-engine / payroll / endpoint / frontend changes. Schema tables + repository + event registrations + corrected seed values + Phase E `bug_correction_history` schema validation test. Subsequent ADR-024 cutover sprint (S41) dispatches against stable plumbing surface.

**Out of scope**: ConfigResolutionService 4-layer extension, OvertimeGovernanceRule cutover, D6 ConfigBugCorrected endpoint pattern, D2 DISCRETIONARY workflow, admin endpoints, frontend, HK/PROSA seed flip (all S41); D-tests beyond bug_correction_history schema, full Phase E continuous-validation (S42).

## Cycle-Trail Context

Refinement Step 4 ran 3 cycles to converge:
- Cycle 1 (7 BLOCKERs): originally bundled 3 ADRs in S40; misread binding ADRs. User adjudication: split per-ADR.
- Cycle 2 (4 BLOCKERs): even ADR-024-full was too big; ADR-024's own Consequences section splits implementation across 3 sub-sprints. User adjudication: honor ADR-author's sub-sprint split.
- Cycle 3 (1 mechanical SQL syntax WARNING): clean otherwise; absorbed inline.

Step 0b plan review then surfaced 4 plan-vs-codebase BLOCKERs (audit column convention + FK target + event class shape + 6-vs-5 boolean count) all absorbed at cycle 1 of Step 0b; cycle 2 verified clean.

Both superseded refinements + the cycle-trail markdown live as `*-cycle-trail.md` artifacts under `.claude/refinements/` for future-sprint reference.

## Phase Decomposition

All tasks Orchestrator-direct sequential. No worktrees. Init.sql is single-file so schema tasks must be sequential.

| Phase | Tasks | Dispatch |
|-------|-------|----------|
| 0 | TASK-4000 | Sprint open plumbing |
| 1 | TASK-4001..4002 | Schema (role_config_overrides + audit; overtime_pre_approvals extension + overtime_authorization_audit) |
| 2 | TASK-4003 | RoleConfigOverrideRepository (5th versioned-config pattern) |
| 3 | TASK-4004 | EventSerializer wiring 7 new types (58 → 65) |
| 4 | TASK-4005 | Greenfield seed: 8 rows (4 AC strata × 2 OK versions) |
| 5 | TASK-4006 | Phase E `bug_correction_history` schema validation test |
| 6 | TASK-4007 | Sprint close |

## Step 7a Dual-Lens (TASK-4007 at close)

**MANDATORY** per `sprint-close-guard.ps1` hook. Codex + Reviewer Agent against S40 diff vs `3a6f41a`. Cycle-cap 2 per lens. Review focus:
- Schema column types + indices match ADR-024 D1 + D7
- Repository follows AgreementConfigRepository pattern verbatim (audit-bearing Pattern B)
- EventSerializer count 58 → 65; reflection coverage test passes
- 8 seed rows with correct tri-state values per ADR-024 L46-50
- bug_correction_history schema test passes

## Test Summary

Target: 869 baseline + ~5 new Phase E tests in plain regression = ~874 total. `sprint-test-validation` SKIP per design-light-implementation (mostly plumbing). Tooling sprint contract: no test count regression.

## Forward Pointers

- **S41 = ADR-024 Sub-Sprint 2 (cutover)** per ADR-024 L245-254: ConfigResolutionService 4-layer + OvertimeGovernanceRule + PayrollMappingService + admin endpoints + frontend + necessity-ack endpoint + HK/PROSA seed flip (Bug #4 final) + D6 ConfigBugCorrected endpoint pattern. ~15-18 tasks.
- **S42 = ADR-024 Sub-Sprint 3** (D-tests + Phase E completion). ~10-12 tasks.
- **S43+** = ADR-025 sub-sprints. **S46+** = ADR-026 sub-sprints.

---

## Sprint Close

_To be filled by TASK-4007._

### Outcome

_TBD._

### Step 7a Dual-Lens Trail

_TBD._

### Commit List

_TBD._
