# ADR-010: Local Configuration Merged at Service Layer, Not in Rule Engine

| Field | Value |
|-------|-------|
| **Status** | approved |
| **Sprint** | S8 (design), S9 (implementation) |
| **Domains** | Rule Engine, Payroll, Infrastructure |
| **Tags** | local-config, rule-engine, determinism, configuration, merge |

## Context
Local administrators can configure parameters (flex limits, norm periods, etc.) within central agreement constraints. The rule engine must remain pure and deterministic (P2). Local configurations are stored in PostgreSQL.

## Decision
The rule engine never loads local configs. A `ConfigResolutionService` in the Payroll integration merges central `AgreementRuleConfig` (from in-memory `AgreementConfigProvider`) with local overrides (from DB), validates against central constraints, and passes the merged config to the rule engine as a plain `AgreementRuleConfig`.

## Rationale
- Preserves P2: rule engine has zero knowledge of local configs, DB, or organizations
- The merge logic is testable independently
- The rule engine function signature does not change
- Central constraints are enforced at the merge point, not in the rule engine

## Consequences
- `ConfigResolutionService` must validate local values against central min/max
- Local config changes are only effective after re-evaluation (not retroactive by default)
- The rule engine receives one config object — it cannot distinguish central vs local values

## Agent Guidance
- Never add I/O to the rule engine for local config loading
- The merge pattern: `Central + Local Overrides = Effective Config`
- Validation examples: local maxFlexBalance must be <= central MaxFlexBalance
- Implementation target: Sprint 9 (ConfigResolutionService)
