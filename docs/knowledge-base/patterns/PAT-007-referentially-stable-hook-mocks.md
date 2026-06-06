# PAT-007 — Referentially-Stable Hook Mocks for Components with Data-Identity Effects

| Field | Value |
|-------|-------|
| **ID** | PAT-007 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S65 |
| **Domains** | Frontend, Test |
| **Tags** | vitest, react-testing, mocking, useeffect, referential-stability, worker-oom |

## Context

S65 TASK-6503 (ArsoversigtPage vitest suite, proposed by the UX Agent and approved at Step 5b): a page that rehydrates local state from fetched data via `useEffect(..., [data])` was tested with a `vi.mock` factory whose mocked hook returned a **fresh object literal on every render** (`useYearOverview: () => ({ data: makeOverview(), ... })`). Every render produced a new `data` identity → the effect fired → `setState` → re-render → new `data` identity → infinite loop. The failure surfaced NOT as a test assertion failure but as a **vitest worker OOM** ("JavaScript heap out of memory") with all tests reported at 0ms — completely masking the root cause and initially looking like a runner/environment problem.

## Pattern

When mocking a data hook consumed by a component that keys any effect (or memo) on the data's **identity** (`useEffect([data])`, `useMemo([data])`):

1. **Hoist a referentially-stable return value** — build the mock payload ONCE via `vi.hoisted(() => ...)` (or a module-level constant inside the `vi.mock` factory) and have the mocked hook return that same object reference on every render. Use `mockReturnValue(stableObject)` rather than a factory that constructs literals per call.
2. **Vary by reassignment, not reconstruction** — tests that need different payloads swap the stable reference once per test (`mockUseX.mockReturnValue(otherStableObject)`), not per render.
3. **Stub heavy child components** (`vi.mock` them to trivial renders) so that if an identity loop does slip through, it exhausts the renderer fast and visibly instead of slowly inflating the heap.

## Rationale

The failure mode is disproportionately expensive: an identity-loop bug in a mock does not fail an assertion — it OOMs the whole worker, reports every test as 0ms, and points suspicion at vitest/jsdom infrastructure rather than the mock. The fix is mechanical and free. Supports P8 (CI stability — a worker OOM is a flaky-looking hard failure) and P9.

## Agent Guidance

- Before mocking any data hook, check whether the component under test keys an effect/memo on the returned object — if yes, the mock's return value MUST be referentially stable across renders.
- `vi.hoisted` + `mockReturnValue(constant)` is the house pattern; never return a fresh literal from the mock implementation body.
- If a vitest run dies with "heap out of memory" and tests report 0ms, suspect a data-identity render loop in a mocked hook FIRST, not the runner.
- Stub heavy children in page-level tests; they are not under test and they amplify loop cost.
