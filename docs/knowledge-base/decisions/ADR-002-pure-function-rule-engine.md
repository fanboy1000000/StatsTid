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

## Amendment 2026-10-20 (S145, ruling R11) — shared calendar arithmetic in `SharedKernel/Calendar`

**What and why, in plain terms.** S145 made payroll settle overtime, norm and flex week by week. The owner ruled that a week's norm is built from each weekday's own part-time fraction, exactly as the employee's screen shows it (Q2 = A), and that a week crossing a month boundary is settled by the month holding its Sunday (Q3 = F1). That arithmetic is needed in three bounded contexts: the Rule Engine (the rules), payroll (which weeks a month settles, and whether an agreement change splits one) and the Backend (the screen's daily norm and the HR worklist). Two copies had already disagreed: the screen computed per day, the rules per week. So it lives once, as pure functions, in `src/SharedKernel/StatsTid.SharedKernel/Calendar/`:
- `DayNorm`: a weekday's amount, `round(weekly × fraction ÷ 5, 2)` half-to-even, with 0 on Saturday and Sunday; week and month figures are sums of these (`DayNorm.cs:32-57`);
- `WeeklyContext`: the Monday–Sunday week, its settlement day (the Sunday, or a leaver's last day), the weeks a period settles, and the mid-week agreement-change predicate (`WeeklyContext.cs`).

**Ownership (R11).** `SharedKernel/**/Calendar/**` is the Rule Engine agent's scope (`docs/AGENTS.md:10`). The helpers are rule arithmetic, owned by rule-engine and consumed by payroll (`PeriodCalculationService`, `RetroactiveCorrectionService`), by the Backend (`DailyNormCalculator`, `BalanceEndpoints`) and by Infrastructure (the worklist derivation in `HrBackdateWorklistRepository`).

**How this keeps the decision above.** The helpers take every input as a parameter: no I/O, no clock, no ambient state; weekend detection reads only the date's weekday, and public holidays do not reduce the norm. Calling them from the Rule Engine therefore keeps it pure, and calling them from payroll or the Backend does not move rule logic out of the engine: agreement configs are still resolved only inside the Rule Engine, which returns a fingerprint of what it resolved (ADR-016 D10, S145 amendment). What was accepted: the screen and the engine now share one implementation, so a change to the per-day rule changes both at once, which is the point. Not covered: the older monthly denominator `(weekdays / 5) × weekly norm` at two Backend sites is different arithmetic and was ruled outside R11 (QUAL-198, scheduled for S146).
