# [RES-001] AC has no overtime/supplements (agreement fidelity over feature parity)

| Field | Value |
|-------|-------|
| **ID** | RES-001 |
| **Category** | resolution |
| **Status** | approved |
| **Sprint** | Sprint 2 |
| **Date** | 2026-02-01 |
| **Domains** | Rule Engine |
| **Tags** | ac, overtime, supplements, priority-conflict |

## Context
During Sprint 2 implementation of overtime and supplement logic, a priority conflict arose: should AC employees have overtime/supplement calculations for feature parity with HK/PROSA (Priority #9 — usability), or should they have these features disabled to match the actual AC agreement terms (Priority #2 — deterministic rule engine correctness)?

## Conflict
- **Priority #2** (Deterministic rule engine): Rules must faithfully represent each agreement. AC agreements do not include traditional overtime or time-based supplements.
- **Priority #9** (Usability and UX): Feature parity across agreements would simplify the UI and user experience.

## Resolution
Agreement fidelity wins. AC employees have:
- `HasMerarbejde = true` (excess hours tracked as merarbejde at 1.0x)
- `HasOvertime = false` (no traditional overtime calculation)
- All supplement flags disabled (evening, night, weekend, holiday)

HK/PROSA employees have:
- `HasOvertime = true` (37-40h at 50%, >40h at 100%)
- `HasMerarbejde = false`
- All supplement flags enabled

## Rationale
The system's legal defensibility depends on accurately representing each agreement. Providing overtime calculations for AC employees would produce incorrect payroll outputs and undermine the system's core purpose. UX accommodations (e.g., showing "N/A" for overtime) can be made without compromising rule correctness.

## Consequences
- The UI must gracefully handle agreements where certain features are disabled
- Rule engine tests must verify that AC and HK/PROSA produce fundamentally different outputs for the same work patterns
- Future agreements must be evaluated individually — no assumption of feature parity

## Agent Guidance
- **Rule Engine Agent**: Never add overtime or supplement logic to AC configurations. If a new feature doesn't apply to AC, set the corresponding flag to disabled.
- **UX Agent**: Design the UI to handle missing/disabled features gracefully. Show "Not applicable" rather than hiding sections.
- **Test & QA Agent**: Always test AC separately from HK/PROSA. The same input should produce different outputs based on agreement type.
