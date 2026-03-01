# [PAT-002] Supplement precedence — no double-dipping

| Field | Value |
|-------|-------|
| **ID** | PAT-002 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | Sprint 2 |
| **Date** | 2026-02-01 |
| **Domains** | Rule Engine |
| **Tags** | supplements, precedence, overtime, rule-engine |

## Context
Danish state agreements define multiple supplement types (evening, night, weekend, holiday) that can overlap temporally. An employee working a holiday evening could theoretically qualify for both holiday and evening supplements. The system must apply a clear precedence to avoid double-counting.

## Decision
Supplement precedence order: Holiday > Weekend > Evening/Night. Only the highest-priority applicable supplement is applied — no stacking or double-dipping.

## Rationale
- Danish state agreement practice applies the most favorable single supplement, not cumulative supplements
- Clear precedence eliminates ambiguity in rule evaluation
- Prevents payroll errors from duplicate supplement charges
- Simplifies rule engine logic — evaluate in priority order, stop at first match

## Consequences
- Supplement evaluation must check in order: holiday, weekend, evening/night
- Once a higher-priority supplement applies, lower-priority supplements are skipped
- Calendar context is required to determine holidays and weekends
- Different agreements may have different supplement rates but follow the same precedence

## Agent Guidance
- **Rule Engine Agent**: Always evaluate supplements in precedence order. Use early return/break when a higher supplement applies.
- **Test & QA Agent**: Include tests for overlapping supplement scenarios (e.g., holiday that falls on a weekend, weekend evening).
- **Payroll Agent**: Each work period produces at most one supplement wage type.
