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

## Agent Guidance
- **Rule Engine Agent**: All agreement-specific values (rates, thresholds, flags) must come from the config dictionary. Never hardcode union-specific logic.
- **Test & QA Agent**: Test each agreement type separately. Verify that changing config values changes rule outputs.
- **Data Model Agent**: Config types should be in SharedKernel so both rule engine and tests can reference them.
