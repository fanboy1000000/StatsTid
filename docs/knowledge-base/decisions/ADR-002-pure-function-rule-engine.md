# [ADR-002] Pure function rule engine with no I/O

| Field | Value |
|-------|-------|
| **ID** | ADR-002 |
| **Category** | decision |
| **Status** | approved |
| **Sprint** | Sprint 1 |
| **Date** | 2026-01-15 |
| **Domains** | Rule Engine |
| **Tags** | rule-engine, determinism, pure-functions |

## Context
Danish state agreements (AC, HK, PROSA) have complex, legally binding rules for overtime, supplements, absence, and flex. The system must produce identical results when replaying historical events — determinism is non-negotiable.

## Decision
The rule engine consists exclusively of pure functions with no I/O, no database access, no HTTP calls, and no ambient state. All inputs are passed as parameters; all outputs are return values.

## Rationale
- Pure functions guarantee deterministic replay — same inputs always produce same outputs
- No I/O means rules can be tested in isolation without mocks or infrastructure
- Separation from infrastructure ensures rule logic is never coupled to persistence or network concerns
- This is Priority #2 in the system (after architectural integrity)

## Consequences
- Rule engine code (`src/RuleEngine/**`) must never import I/O libraries or infrastructure namespaces
- Calendar data, agreement configs, and OK versions must be passed as parameters, not fetched
- Any new rule must be provably deterministic via unit tests
- Rule engine cannot be modified by the Security agent or any infrastructure change

## Agent Guidance
- **Rule Engine Agent**: Every function must be a pure function. If you need external data, declare it as a parameter — never fetch it.
- **Test & QA Agent**: Every rule must have determinism proof tests (same input → same output across multiple invocations).
- **All agents**: Never modify `src/RuleEngine/**` for non-rule concerns (auth, logging, etc.).
