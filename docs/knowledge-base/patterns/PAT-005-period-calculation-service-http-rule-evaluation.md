# PAT-005 — PeriodCalculationService HTTP Rule Evaluation Pattern

| Field | Value |
|-------|-------|
| **ID** | PAT-005 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S4 |
| **Domains** | Payroll, Rule Engine |
| **Tags** | service-boundary, HTTP, traceability, payroll-chain |

## Context

Building the "glue" between Rule Engine and Payroll Export required defining how the Payroll service invokes Rule Engine rules while maintaining service boundary isolation.

## Pattern

The `PeriodCalculationService` calls the Rule Engine via HTTP POST for each rule type:
- Time rules (`NORM_CHECK_37H`, `SUPPLEMENT_CALC`, `OVERTIME_CALC`) via `/api/rules/evaluate`
- Absence via `/api/rules/evaluate-absence`
- Flex balance via `/api/rules/evaluate-flex`

It uses camelCase JSON serialization with case-insensitive deserialization. The flex endpoint returns `FlexBalanceResult` (not `CalculationResult`), requiring special parsing via `JsonDocument` to extract `excessForPayout`.

Each `PayrollExportLine` includes `SourceRuleId` and `SourceTimeType` for end-to-end traceability:
```
Time Event → Rule Evaluation (SourceRuleId) → TimeType (SourceTimeType) → WageType → Export File
```

## Rationale

This pattern ensures the payroll service never directly invokes rule engine functions (P1 Architectural integrity, P5 Integration isolation) while maintaining the full traceability chain required by DEP-002.

## Agent Guidance

- Never import or reference Rule Engine assemblies from the Payroll project
- Always include `SourceRuleId` and `SourceTimeType` on `PayrollExportLine` for audit trail
- As of Sprint 5, the flex endpoint returns a unified response deserializable as `CalculationResult` (see PAT-006). No `JsonDocument` workaround needed.
- Independent rule calls (norm, supplement, overtime, on-call) + absence are parallelized via `Task.WhenAll`; flex remains sequential
