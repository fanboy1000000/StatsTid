# PAT-006 — Unified Rule Endpoint Response Format

| Field | Value |
|-------|-------|
| **ID** | PAT-006 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S5 |
| **Domains** | Rule Engine, Payroll |
| **Tags** | rule-engine, endpoint-response, deserialization, flex, payroll-chain |

## Context

Prior to Sprint 5, the `/api/rules/evaluate-flex` endpoint returned `FlexBalanceResult` directly, which was incompatible with the standard `CalculationResult` type used by all other rule endpoints. This forced `PeriodCalculationService.CallFlexRuleAsync` to use a `JsonDocument` workaround for deserialization, adding complexity and fragility.

## Pattern

All rule evaluation endpoints should return responses that are deserializable as `CalculationResult` (with `ruleId`, `success`, and `lineItems` fields). Domain-specific fields (like `FlexBalanceResult.NewBalance`, `Delta`, etc.) may be included alongside these standard fields.

The `/api/rules/evaluate-flex` endpoint now returns an anonymous object containing:
- Standard fields: `ruleId = "FLEX_BALANCE"`, `success`, `lineItems` (FLEX_PAYOUT item when ExcessForPayout > 0, empty array otherwise)
- Domain-specific fields: `previousBalance`, `newBalance`, `delta`, `workedHours`, `absenceNormCredits`, `normHours`, `excessForPayout`

This allows `PeriodCalculationService` to use standard `CalculationResult` deserialization for all rule calls, including flex.

## Rationale

Unified response format eliminates special-case parsing code, reduces fragility from anonymous type changes, and enables the HTTP parallelization pattern (all calls return the same type). Supports P1 (architectural consistency) and P5 (integration simplicity).

## Agent Guidance

- New rule endpoints MUST include `ruleId`, `success`, and `lineItems` in their response
- Domain-specific fields are allowed alongside standard fields (backward compatibility)
- `PeriodCalculationService` should use `CalculationResult` deserialization for all rule calls
- Do NOT use `JsonDocument` workarounds for rule endpoint responses
- Sprint 5 Reviewer WARNING: Consider replacing the anonymous type with an explicit DTO in SharedKernel for compile-time safety (deferred to Phase 2)
