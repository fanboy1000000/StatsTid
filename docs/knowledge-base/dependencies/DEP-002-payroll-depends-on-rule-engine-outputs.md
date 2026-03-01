# [DEP-002] Payroll depends on Rule Engine output types

| Field | Value |
|-------|-------|
| **ID** | DEP-002 |
| **Category** | dependency |
| **Status** | approved |
| **Sprint** | Sprint 2 |
| **Date** | 2026-02-01 |
| **Domains** | Payroll, Rule Engine |
| **Tags** | payroll, wage-types, cross-domain, dependency |

## Dependency
**From**: Payroll Integration (`src/Integrations/**/Payroll/**`)
**To**: Rule Engine (`src/RuleEngine/**`) output types

The Payroll Integration maps rule engine outputs (overtime results, supplement calculations, absence evaluations) to SLS wage types. It depends on the structure and semantics of rule engine output types.

## Impact
- Changes to rule engine output types (new fields, renamed fields, new output categories) break payroll mappings
- New rule categories (e.g., a new supplement type) require new wage type mappings
- OK version transitions may introduce new rule outputs that need payroll mapping

## Traceability Chain
The full chain must be maintained: **Time Event → Rule Evaluation → Wage Type → Export File**. Breaking any link makes payroll output unverifiable.

## Coordination Protocol
1. Rule Engine Agent declares output type changes in its deliverable
2. Orchestrator reviews and dispatches payroll mapping updates to Payroll Agent
3. Test & QA Agent verifies end-to-end traceability after both agents complete

## Agent Guidance
- **Rule Engine Agent**: When adding/changing output types, document the change clearly so the Orchestrator can dispatch payroll updates.
- **Payroll Agent**: Always check rule engine output types before modifying mappings. Your input types must match exactly.
- **Test & QA Agent**: Include traceability tests that verify the full chain from time event to payroll export.
