# FAIL-006 — A tool whose failures are warnings is a tool that reports success

| Field | Value |
|-------|-------|
| **ID** | FAIL-006 |
| **Category** | failure |
| **Status** | recorded |
| **Sprint** | S127 |
| **Domains** | Tooling, Ops, Test |
| **Tags** | false-green, exit-code, verification, independent-oracle, falsification-probe |
| **Origin** | TASK-12701b (S127) |

## What happened

`tools/StatsTid.DemoSeed` recorded every failed period submit as a **warning**. Warnings were printed,
never counted, and never reached the exit code — the program returned **0** regardless. Combined with
S127's new allocation gate, the loader would have failed **374 of 375** sends and still exited clean.

A rerun made it worse: any `409` is treated as an idempotent skip, so a second run over a broken world
looked *identical* to a second run over a healthy one.

## Why the obvious fix is only half a fix

Routing the failure into the exit code catches a loader that *knows* it failed. It does nothing for a
loader that is wrong about what success means — wrong month, wrong status, row silently absent.

**Both halves are required:**

1. **Failures reach the exit code.** A distinct non-zero code, with the failing items listed.
2. **The outcome is asserted against an independent oracle** — here, the database, keyed on the natural
   key `(employee_id, period_start, period_end)`, with per-status counts derived from the manifest. Not
   the tool's own report of what it did.

Then **break the world on purpose and watch both go red.** In S127 that meant deleting one period row and
perturbing one day's hours so the loader's own repair path could not mask it: exit 6, verification FAILED,
and the failing row named. Under the old counters that run was byte-identical to a healthy one.

## The part worth remembering

**One of the four falsification attempts did not fire** — and that was the most useful result of the
exercise. It revealed that the "stray period" arm only sees employees the manifest names, a real scope
limit in a check written minutes earlier. It is now documented in the code rather than glossed.

A probe that fails to fire is not a wasted probe. It is the only way to discover that a check is narrower
than its name.

## Checklist for any load/migration/seed tool

- [ ] Every failure path increments a counter that reaches a **distinct non-zero exit code**
- [ ] Verification asserts against an **independent** source of truth, on a natural key — not the tool's
      own log
- [ ] Counts are **derived from the input manifest**, so "produced nothing" cannot pass
- [ ] Assert the **negative** too: no row where the manifest says there should be none
- [ ] Run the verification on a **fresh load AND a rerun** — idempotent-skip paths are where masking lives
- [ ] Break the world deliberately; confirm **every** arm fires; document any arm that does not

## Related

- The S125 standing lesson — *a test can assert on something that looks like evidence and is not*. This
  is its tooling twin.
- [FAIL-005](FAIL-005-probe-restore-timestamp-stale-build.md) — restore hygiene for these probes.
- [PAT-014](../patterns/PAT-014-characterization-baseline-one-inversion-per-encoding.md) — the same
  "prove it can go red" discipline for predicates.
