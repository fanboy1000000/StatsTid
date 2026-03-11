# Sprint 17: Overtime Governance & Compensation Model

**Status**: complete
**Dates**: 2026-03-11
**Orchestrator Approved**: yes
**Tests**: 446 (431 unit + 15 regression)

## Sprint Goal

Implement overtime governance (pre-approval workflow, ceiling checks) and compensation model (afspadsering vs. udbetaling disposition). Covers SYSTEM_TARGET.md sections L (Overtime Governance) and M (Compensation Model).

## Design Decisions

- OvertimeBalance follows EntitlementBalance pattern (S15): DB table + repository + atomic adjust
- Compensation choice is per-period per-employee (when EmployeeCompensationChoice=true on config)
- Pre-approval uses separate `overtime_pre_approvals` table (not a flag on TimeEntry — avoids polluting immutable events with mutable workflow state)
- OvertimeGovernanceRule is a pure static rule returning ComplianceCheckResult (ADR-015 pattern). Checks: max overtime ceiling + pre-approval requirement. Both produce WARNINGs (not blockers — overtime is legitimate with approval)
- Compensation disposition is service-layer only: OvertimeRule stays pure (OVERTIME_50/100/MERARBEJDE), PayrollMappingService appends _PAYOUT or _AFSPADSERING suffix based on employee's compensation choice
- DefaultCompensationModel + EmployeeCompensationChoice → ProtectedKeys (centrally negotiated)
- MaxOvertimeHoursPerPeriod + OvertimeRequiresPreApproval → locally overridable

## Tasks

### Phase 1 — Data Model + Rule Engine

| # | Task | Agent | Files | Status |
|---|------|-------|-------|--------|
| 1701 | Data Model: models, events, config fields | Data Model | OvertimeBalance.cs, CompensationModel.cs, OvertimePreApproval.cs, 3 events, AgreementRuleConfig +4 fields, AgreementConfigEntity +4 fields, OvertimeResult.cs +6 constants, EventSerializer +3 events | done |
| 1702 | OvertimeGovernanceRule | Rule Engine | OvertimeGovernanceRule.cs, CheckOvertimeGovernanceRequest.cs, RuleEngine Program.cs +1 endpoint | done |
| 1703 | ComplianceViolationType enum extension | Data Model | ComplianceCheckResult.cs +OVERTIME_EXCEEDED, +OVERTIME_UNAPPROVED | done |

### Phase 2 — Infrastructure + Payroll + Backend

| # | Task | Agent | Files | Status |
|---|------|-------|-------|--------|
| 1704 | DB schema + repositories | Orchestrator | init.sql +2 tables +4 columns +28 wage type rows, OvertimeBalanceRepository.cs, OvertimePreApprovalRepository.cs, AgreementConfigRepository.cs +4 fields | done |
| 1705 | Config chain propagation | Orchestrator | ConfigResolutionService.cs +2 ProtectedKeys +2 local overrides +4 merged fields, AgreementConfigSeeder.cs +4 fields, CentralAgreementConfigs.cs +compensation fields for HK/PROSA | done |
| 1706 | Payroll compensation-aware mapping | Payroll | PayrollMappingService.cs +MapCalculationResultWithCompensationAsync +ResolveCompensationTimeType, PeriodCalculationService.cs +compensationModel param, RetroactiveCorrectionService.cs caller fixups, Payroll Program.cs caller fixup | done |
| 1707 | Backend overtime endpoints + balance summary | Backend | OvertimeEndpoints.cs (9 endpoints), BalanceEndpoints.cs +overtimeBalance, Backend Program.cs +2 repos +1 endpoint group | done |

### Phase 3 — Frontend

| # | Task | Agent | Files | Status |
|---|------|-------|-------|--------|
| 1708 | OvertimeBalance card in BalanceSummary | UX | BalanceSummary.tsx +OvertimeBalanceCard, BalanceSummary.module.css, useBalanceSummary.ts +OvertimeBalance type | done |
| 1709 | Overtime governance warnings | UX | ComplianceWarnings.tsx +OVERTIME_EXCEEDED/OVERTIME_UNAPPROVED handling, useCompliance.ts +2 types | done |
| 1710 | Leader pre-approval management page | UX | OvertimePreApprovalManagement.tsx (new), OvertimePreApprovalManagement.module.css (new), App.tsx +route, Sidebar.tsx +nav item | done |
| 1711 | Employee compensation choice selector | UX | SkemaPage.tsx +CompensationChoiceSelector, SkemaPage.module.css +styles, useCompensationChoice.ts (new) | done |

### Phase 4 — Tests + Documentation

| # | Task | Agent | Files | Status |
|---|------|-------|-------|--------|
| 1712 | Sprint 17 test suite | Test & QA | Sprint17OvertimeGovernanceTests.cs (10 tests) | done |
| 1713 | Sprint docs + ROADMAP update | Orchestrator | SPRINT-17.md, INDEX.md, ROADMAP.md, QUALITY.md, MEMORY.md | done |

## Validation Evidence

```
Build: 0 warnings, 0 errors
Unit tests: 431 passed (421 existing + 10 new)
Regression tests: 15 passed
Total: 446 tests
```

## New Database Objects

- `overtime_balances` table (balance_id, employee_id, agreement_code, period_year, accumulated, paid_out, afspadsering_used, compensation_model, updated_at; UNIQUE employee_id+period_year)
- `overtime_pre_approvals` table (id, employee_id, period_start, period_end, max_hours, approved_by, approved_at, status CHECK PENDING/APPROVED/REJECTED, reason, created_at)
- 4 new columns on `agreement_configs`: default_compensation_model, employee_compensation_choice, max_overtime_hours_per_period, overtime_requires_pre_approval
- 28 new wage type mapping rows (6 compensation types × 5 agreements × OK24/OK26, minus AC types that don't have overtime)

## New Domain Events (3 → 34 total registered)

- OvertimeBalanceAdjusted
- OvertimeCompensationApplied
- OvertimePreApprovalCreated

## New API Endpoints (9)

1. GET /api/overtime/{employeeId}/balance
2. GET /api/overtime/{employeeId}/governance
3. POST /api/overtime/pre-approval
4. GET /api/overtime/{employeeId}/pre-approvals
5. PUT /api/overtime/pre-approval/{id}/approve
6. PUT /api/overtime/pre-approval/{id}/reject
7. POST /api/overtime/{employeeId}/compensate
8. GET /api/overtime/{employeeId}/compensation-choice
9. PUT /api/overtime/{employeeId}/compensation-choice

## Constraint Coverage

| Priority | Verified | Notes |
|----------|----------|-------|
| P1 Architectural integrity | ✓ | Multi-agent delegation, worktree isolation |
| P2 Deterministic rule engine | ✓ | OvertimeGovernanceRule is pure static, no I/O |
| P3 Event sourcing | ✓ | 3 new events, state-changing ops emit events |
| P4 Version correctness | ✓ | All configs per OK version |
| P5 Integration isolation | ✓ | Rule engine called via HTTP from backend |
| P6 Payroll correctness | ✓ | 28 new wage type mappings, compensation-aware mapping |
| P7 Security | ✓ | All endpoints have RequireAuthorization |
| P8 CI/CD enforcement | ✓ | Build + test pass |
| P9 Usability | ✓ | 4 frontend changes: balance card, warnings, pre-approval page, compensation choice |

## Agent Effectiveness

- 13 tasks, 0 constraint violations, 0 re-dispatches
- First-pass rate: 100% (all agents produced buildable output; Orchestrator fixed API signature mismatches during merge)
- Worktree isolation used for all parallel agents
