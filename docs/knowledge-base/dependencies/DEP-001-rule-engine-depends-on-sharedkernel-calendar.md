# [DEP-001] Rule Engine depends on SharedKernel Calendar

| Field | Value |
|-------|-------|
| **ID** | DEP-001 |
| **Category** | dependency |
| **Status** | approved |
| **Sprint** | Sprint 2 |
| **Date** | 2026-02-01 |
| **Domains** | Rule Engine, SharedKernel |
| **Tags** | calendar, holidays, cross-domain, dependency |

## Context
The rule engine must evaluate supplements and overtime based on whether a given date is a holiday, weekend, or regular workday. This requires calendar data including Danish public holidays, which vary by OK version (e.g., Store Bededag removed from OK24 onwards).

## Dependency
**From**: Rule Engine (`src/RuleEngine/**`)
**To**: SharedKernel Calendar (`src/SharedKernel/**/Calendar/**`)

The Rule Engine consumes calendar data (holiday lists, workday checks) but does not own it. Calendar types are defined in SharedKernel and shared across domains.

## Impact
- Changes to calendar types or holiday definitions affect rule engine behavior
- Adding/removing holidays requires coordinated testing of rule engine outputs
- OK version transitions may change the holiday calendar (Store Bededag precedent)

## Coordination Protocol
1. Calendar changes must be made by the Rule Engine Agent (calendar is in its scope)
2. After calendar changes, Test & QA Agent must re-run supplement and overtime tests
3. Payroll Agent must verify wage type mappings still align with updated supplement outputs

## Agent Guidance
- **Rule Engine Agent**: You own both the rule functions and the calendar data. Changes to either may affect the other — test both.
- **Test & QA Agent**: Holiday-related test failures may indicate calendar data changes — check calendar first before investigating rule logic.
- **Payroll Agent**: If supplement outputs change due to calendar updates, verify payroll mappings.
