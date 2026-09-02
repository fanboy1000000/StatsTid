# PAT-023 — Derive a cross-cutting invariant from the pipeline's OUTPUT, not from a hand-maintained mirror of its inputs

| Field | Value |
|-------|-------|
| **ID** | PAT-023 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S137 (TASK-13707, owner-ruled 2026-09-02) |
| **Domains** | SharedKernel (Segmentation), planner invariants, any pipeline with a pre-step guard |
| **Tags** | adr-016, d4, adr-040, d5, mirror-predicate, by-construction, completeness, period-planner, invariant-check |
| **Origin** | `PeriodPlanner.ApplyAlignmentPolicy` / the retired `HasAnyInteriorBoundary` |

## The problem (plain language)

The planner had a safety check ("refuse this month if a whole-window rule would be split") that ran
BEFORE the step that actually finds the splits. To answer early it kept its own list of every input
the split-finder reads — a **mirror**. Every time a new kind of boundary was added (Sprint 21, Sprint
137) someone had to remember to extend the mirror; Sprint 137's recon caught it lagging. A mirror is
a second source of truth with no compiler help, and its failure mode is silent: the safety check
simply does not fire for the source it forgot.

## The pattern

When an invariant is a property of **what the pipeline will produce**, compute it **from the produced
artifact** after the producing step — not from a parallel re-derivation of the inputs before it.
Completeness is then guaranteed by construction: a new source cannot bypass the check, because the
check reads the same output the new source feeds.

In S137 the refusal moved after boundary detection and range typing and now simply counts EMPLOYED
ranges (ADR-016 D4 as amended: refuse iff a whole-window rule would be EVALUATED in ≥ 2 employed
segments). The mirror was deleted. A bonus: the check has the real counts and causes at hand, so its
error message became diagnosable ("2 EMPLOYED segments; interior causes: EmployeeProfileChange").

## When the early placement was a stub

The check sat early "so the shrink step could adjust the period before detection" — but the S20
shrink was never implemented; the period was never adjusted. Before keeping an early check for the
sake of a downstream adjustment, verify the adjustment exists. Dead plumbing kept a live mirror alive.

## Agent Guidance

- Before writing a "MUST mirror X" comment, ask whether the check can run on X's output instead.
- If a check is placed before a step "so the step sees the adjusted input", confirm the adjustment is
  real; if it is a stub, move the check after the step and delete the mirror.
- When you move such a check, keep the exception's stable tokens (rule id, option name, ADR
  reference) so existing pins stay valid, and add the artifact-derived facts (counts, causes) — never
  raw dates that a response body must not carry (ADR-040 D7).
