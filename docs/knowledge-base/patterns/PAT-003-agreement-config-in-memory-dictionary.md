# [PAT-003] Agreement config as in-memory dictionary

| Field | Value |
|-------|-------|
| **ID** | PAT-003 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | Sprint 2 |
| **Date** | 2026-02-01 |
| **Domains** | Rule Engine |
| **Tags** | agreement-config, configuration, rule-engine, ac, hk, prosa |

## Context
The rule engine needs access to agreement-specific configuration (overtime thresholds, supplement rates, flex rules) but cannot perform I/O. Configuration must be available as pure data.

## Decision
Agreement configurations are stored as an in-memory dictionary keyed by agreement type (AC, HK, PROSA). Each configuration is a data object with all rule parameters for that agreement. The dictionary is constructed at startup and passed into rule functions as a parameter.

## Rationale
- In-memory dictionary preserves rule engine purity — no database queries during rule evaluation
- Configuration is version-aware (different configs per OK version)
- Centralized configuration makes it easy to audit all agreement differences
- Adding a new agreement means adding a new dictionary entry, not modifying rule logic

## Consequences
- Agreement configs must be comprehensive — every rule parameter must be present
- Adding a new OK version requires new config entries for all agreements
- Config objects must be immutable (follows PAT-001)
- The dictionary is the single source of truth for agreement-specific behavior

## Update (Sprint 10)
As of Sprint 10 (TASK-1003), the config dictionary is centralized in `CentralAgreementConfigs` (SharedKernel). Both `AgreementConfigProvider` (Rule Engine) and `ConfigResolutionService` (Infrastructure) delegate to it. This eliminates the sync hazard identified in Sprint 7 where two identical dictionaries could silently diverge.

## Agent Guidance
- **Rule Engine Agent**: All agreement-specific values (rates, thresholds, flags) must come from the config dictionary. Never hardcode union-specific logic. `AgreementConfigProvider` delegates to `CentralAgreementConfigs` — add new config properties to `AgreementRuleConfig` and `CentralAgreementConfigs` only.
- **Test & QA Agent**: Test each agreement type separately. Verify that changing config values changes rule outputs.
- **Data Model Agent**: Config types and central configs live in SharedKernel (`CentralAgreementConfigs`, `AgreementRuleConfig`) so both rule engine and infrastructure can reference them.
