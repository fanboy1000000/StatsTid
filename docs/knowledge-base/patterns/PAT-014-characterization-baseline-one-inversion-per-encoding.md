# PAT-014 — A characterization baseline of an inline predicate needs one inversion per ENCODING, and a fixture that isolates the varied input

| Field | Value |
|-------|-------|
| **ID** | PAT-014 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S127 |
| **Domains** | Test, Backend |
| **Tags** | characterization-test, falsification-probe, false-green, inline-predicate, duplication, refactor-safety |
| **Origin** | TASK-12700 (S127), proposed by the implementing agent |

## Context

Before collapsing N hand-written copies of a rule into one shared implementation, capture a
**characterization baseline** of today's behaviour. The baseline is only worth having if it can fail.
Two ways it silently cannot:

## Rule 1 — one inversion per ENCODING, not per predicate

"The baseline must go red if the production comparison is inverted" reads as satisfied by **one**
inversion. If the rule is duplicated across N sites, inverting one site leaves the other N−1 completely
unguarded — and the baseline still went red, so the criterion looks met.

**Require N independent inversions, one per encoding, each verified separately**, and require the
failures to appear *at that encoding's own assertion*. S127's baseline covered three inline copies:

| inversion | result |
|---|---|
| the approval gate `<` → `>` | 16 failed / 6 passed |
| team-overview `hasWarning` `>` → `<` | 16 failed / 6 passed, failing at the `hasWarning` assertion |
| allocation-breakdown `>` → `<` | 16 failed / 6 passed, failing at its own assertion |

Also require each inversion to fail in **both directions** — balanced rows must flip to imbalanced *and*
imbalanced rows to balanced. A one-directional failure usually means the fixture only exercises one side.

## Rule 2 — the fixture must isolate the varied input, or the probe passes for the wrong reason

An inline predicate typically sits behind an upstream precondition. If the fixture satisfies that
precondition using data the predicate *also* reads, an inverted predicate can still produce the expected
outcome by a different route, and a status-code-only assertion stays green.

S127's concrete case: the allocation gate only runs after a workday-**coverage** check passes. Filling the
other weekdays with *balanced* work would have satisfied coverage — but those days are also compared by
the predicate, so an inverted gate would still return 422, for the wrong reason.

**The fix: satisfy the upstream precondition through a channel the predicate does not read.** Full-day
absence rows satisfy coverage but live in a table neither side of the allocation comparison queries, so
exactly one day remained comparable. Then assert the **identified set** (`unbalancedDays` — which days,
which figures), not merely the status code.

## Checklist

- [ ] Enumerate every encoding of the rule first; the baseline's inversion count equals that number
- [ ] Run each inversion **separately**, restoring between runs **from a scratchpad copy — never
      `git checkout -- <file>`**, which restores HEAD and destroys uncommitted work
- [ ] Confirm each inversion fails at its **own** surface's assertion
- [ ] Confirm failures appear in **both** directions
- [ ] Satisfy upstream preconditions through a channel the predicate does not read
- [ ] Assert the identified set, not just the outcome code
- [ ] Verdicts in the value table are derived **by hand from the rule**, never read from the implementation
- [ ] The final diff contains **zero production files**

## Corollary — a re-implementing "spec" test is confidence-shaped non-evidence

A test that re-implements the algorithm it claims to pin passes with the production code **deleted**.
S127 found `AllocationGateTests` re-implementing the interval sum, the allowlist, the rounding, the
tolerance and both directions — all 7 of its tests survive deletion of the gate. Prefer a test that calls
the real surface; where a mirror is genuinely wanted, say so in the header and never count it as coverage.

## Relationship to other entries

The project's standing lesson is *a test written to demonstrate a result rather than falsify one goes
green on nothing*. PAT-014 is the refactor-safety instance; [PAT-013](PAT-013-on-conflict-vs-23505-transaction-liveness.md)
is the concurrency-primitive instance. Both were found the same way: **substitute the wrong
implementation and re-run** — the probe, not the review, is what proves a test discriminating.
