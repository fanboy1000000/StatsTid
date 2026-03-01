# [ADR-003] OK version resolved by entry date, not current date

| Field | Value |
|-------|-------|
| **ID** | ADR-003 |
| **Category** | decision |
| **Status** | approved |
| **Sprint** | Sprint 2 |
| **Date** | 2026-02-01 |
| **Domains** | Rule Engine |
| **Tags** | ok-version, determinism, version-resolution |

## Context
Danish state collective agreements operate in "OK" periods (e.g., OK24 covers 2024-04-01 to 2026-03-31, OK26 starts 2026-04-01). When recalculating historical entries, the system must know which OK version's rules to apply.

## Decision
The OK version is resolved based on the time entry's date, not the current system date. This ensures that replaying events from 2024 always uses OK24 rules, even if the replay happens in 2027.

## Rationale
- Using the current date would produce different results when replaying the same events at different times — violating determinism
- Entry-date resolution ensures historical recalculation is stable and legally defensible
- This aligns with Priority #2 (deterministic rule engine) and Priority #4 (version correctness)

## Consequences
- `OkVersionResolver` must accept a date parameter and return the applicable OK version
- Rules must never call `DateTime.Now` or `DateTime.UtcNow`
- OK version boundaries must be configurable (not hardcoded dates)
- All rule evaluation paths must pass the entry date through to version resolution

## Agent Guidance
- **Rule Engine Agent**: Always use the entry date parameter for version resolution. Never use current time.
- **Test & QA Agent**: Include tests that prove the same entry evaluated at different "current times" produces identical results.
- **All agents**: If you need a "now" timestamp for non-rule purposes (e.g., audit logs), use it — but never pass it into rule evaluation.
