# PAT-027 — Date-range predicates live on the value type, never at call sites

| Field | Value |
|-------|-------|
| **ID** | PAT-027 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S138 (TASK-13806), motivated by the S137 Step-5a Reviewer NOTE |
| **Domains** | SharedKernel, Infrastructure, Backend, Payroll |
| **Tags** | fencepost, inclusive-end, unbounded, employment-window, adr-040, d1, d2, single-source |
| **Origin** | `src/SharedKernel/StatsTid.SharedKernel/Models/EmploymentWindow.cs` (`Overlaps`, `ClipTo`, `FirstNotEmployedDay`, `FirstEmployedDayWithin`) |

## The problem (plain language)

An employment window is "[first employed day, LAST employed day]": the end is INCLUSIVE and a missing side
means "no limit". Three production sites — the window resolver's range filter, the planner's segment typing,
the compliance check's first-employed-day — each did that comparison by hand. Every hand-rolled copy is a
fresh chance to be off by one day, and at the payroll boundary one day is a paid day silently dropped or
added. The codebase had already named this its recurring hazard (Sprint 137 documented the End+1 fencepost in
three places to defend against it).

## The pattern

Put the arithmetic ONCE on the value type, with one fencepost matrix, and make every consumer call it:
`Overlaps(from, to)` (both inclusive; null sides unbounded), `ClipTo(from, to)` (the closed intersection or
null), `FirstNotEmployedDay` (End + 1, guarded for `DateOnly.MaxValue`), `FirstEmployedDayWithin(from, to)`
and its static union overload over a spells-proof list. The matrix (`EmploymentWindowTests`, 26 rows) asserts
the helpers TOGETHER on every edge: a start ON the last range day, an end ON the first range day, the day
before/after, single-day ranges, MaxValue. Inverted ranges fail loud rather than reading as "no employed
day". A refactor of this kind is proven byte-identical by leaving the existing pins untouched and green —
in S138, all 59 Sprint-137 planner/parity/hydration pins.

## What was given up, and why

- Widening visibility so a test could reach an endpoint's private helper — a private production seam is
  pinned by reflection in the test project that already references both sides, not by making an endpoints
  class's helper public.

## Agent Guidance

- A new consumer of a window (or any inclusive-end / unbounded-side interval type) calls the helper; a new
  hand-rolled `Start <= x && End >= y` on such a type is a review finding.
- When adding a helper, extend the ONE matrix rather than writing a sibling test class per consumer.
- The remaining hand-rolled End+1 in `PeriodCalculationService.BuildPlanForLegacyCallersAsync` can become
  `window.FirstNotEmployedDay` in a Payroll-scoped task (pinned by `EmploymentWindowHydrationTests`).
