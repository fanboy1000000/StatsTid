# [FAIL-004] The inactive-manager escalation walk can return an employee as their OWN designated approver (cyclic legacy graph)

| Field | Value |
|-------|-------|
| **ID** | FAIL-004 |
| **Category** | failure |
| **Status** | OPEN — confirmed, tripwired, awaiting an owner ruling on the intended behaviour |
| **Sprint** | S125 (found) |
| **Date** | 2026-07-30 |
| **Domains** | Backend, Infrastructure, Security |
| **Tags** | authorization, reporting-line, designated-approver, escalation, cyclic-graph, segregation-of-duties, p7, pre-existing |

## Summary
`ReportingLineRepository.ResolveDesignatedApproverAsync` walks up the reporting chain when a manager
is inactive. The walk has **no check that the resolved manager differs from the employee it started
from**, so on a cyclic legacy graph it can return the employee as their own approver — and the
authorization gate then admits them over their own period.

## How it was found
Not by looking for it. During the S125 performance analysis of F1 (the period-status projection's
N+1), the Step-4 internal review lens traced the tally loop's authorization path and flagged that the
refinement's stated invariant *"self-exclusion: a leader never tallies their OWN period"* is **not
what the code does** — it holds for the unit-leader legs only. Verified from code, then **confirmed
empirically** rather than accepted on the reviewer's word.

## Confirmation (empirical, not inferred)
Tripwire: `PeriodStatusAndPersonSearchReadsTests
.FINDING_12502_TRIPWIRE_EscalationWalk_ReturnsEmployeeAsTheirOwnApprover_OnTwoCycle`.

Setup: `A → B` and `B → A` (raw-inserted — `AssignAsync`'s cycle guard rejects this, so only LEGACY
data can produce it), with `B` inactive and holding no usable vikar.

Result: `ResolveDesignatedApproverAsync(A)` returns **`(A, "DESIGNATED_MANAGER", depth: 1)`**.

The test was additionally proven non-vacuous by inverting its assertion, which reported
`Expected: "PROBE_EXPECT_FAIL" / Actual: "t7404_cyc_a"` — i.e. the resolver really does return A.

## Mechanism
`ReportingLineRepository.ResolveDesignatedApproverAsync` (~:956-999):

1. Iteration 0 for employee `A`: no ACTING; PRIMARY manager is `B`; `B` has no usable vikar; `B` is
   inactive → step 4 sets `currentEmployeeId = B`, `depth++`.
2. Iteration 1 for `B`: PRIMARY manager is `A`; `A` is **active** → returns
   `(A, "DESIGNATED_MANAGER", 1)`.

Nothing compares the candidate against the ORIGINAL `employeeId`. The `while (depth < 10)` ceiling
bounds the walk but does not prevent returning to the start.

## Why it reaches authorization, not just resolution
`DesignatedApproverAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(actor: A, employee: A)` passes:
- `IsActiveLeaderOrAboveAsync(A)` — satisfied when A is an active LeaderOrAbove;
- the resolved designated approver of `A` **is** `A`, so the edge leg matches the actor;
- `ValidateSameOrganisationAsync(A, A)` — the `= ANY(@ids)` collapses to one row, so both org values
  are trivially equal.

Consequences: `GetPeriodStatusProjectionForTreeAsync` would tally A's own pending period onto A's own
tile, and — more seriously — the same predicate gates the **approve / reject / reopen** action
endpoints, so the segregation-of-duties rule those flows rely on does not hold for this shape.

## The asymmetry that makes it a defect rather than a choice
The unit-leader legs carry **explicit** self-exclusion:
- enumeration: `ul.user_id <> @employeeId` and `mv.vikar_user_id <> @employeeId`
  (`ApprovalPeriodRepository.QueryUnitLeaderApproverCandidatesAsync`);
- gate: `e.user_id <> @actorId` (`DesignatedApproverAuthorizer.QueryUnitLeaderKindAsync`).

The S105 segregation-of-duties rule is therefore clearly intended. The **edge leg has no equivalent**.
That inconsistency — the same rule enforced on one path and absent on the other — is the finding.

## Reachability
Requires a cyclic PRIMARY graph, which `AssignAsync`'s cycle guard prevents going forward. It is
reachable only via legacy/imported data — and such data is **known to be possible here**: the sibling
test `GetDescendantIds_TerminatesOnCyclicLegacyGraph_AndReturnsFiniteSet` exists precisely because
cyclic legacy graphs can reach this system. No production instance has been checked; nobody has
looked. A detection query is the obvious first step (see below).

## NOT fixed — deliberately
Owner-directed 2026-07-30: raise as its own finding rather than fold into the F1 performance task.
Correct call — "fixing" this changes **who may approve**, which is a P7 behaviour decision, not a
refactor. Folding it into a performance change would have altered authorization under cover of an
optimisation, and the characterisation baseline for that work would have silently encoded the new
behaviour as the reference.

## Open questions for the ruling
1. **Intended behaviour?** Options: (a) exclude the original employee from the walk and continue
   escalating past them; (b) treat self-resolution as "no approver" (`(null, null, depth)`) and fall
   back to org-scope; (c) accept it as unreachable-in-practice and leave it documented.
   (a) and (b) differ observably when the cycle is the ONLY path upward.
2. **Does any production data contain a cyclic PRIMARY graph?** A detection query should run before
   choosing — if the answer is "none", the urgency drops sharply. **The demo world was checked
   (2026-07-30): ZERO cyclic PRIMARY paths, zero employees on a cycle.** So the defect is currently
   unreachable in the demo dataset; production has NOT been checked. The query, for reuse:

   ```sql
   WITH RECURSIVE walk(start_id, cur_id, path, depth) AS (
     SELECT rl.employee_id, rl.manager_id, ARRAY[rl.employee_id, rl.manager_id], 1
     FROM reporting_lines rl
     WHERE rl.relationship='PRIMARY' AND rl.effective_to IS NULL
     UNION ALL
     SELECT w.start_id, rl.manager_id, w.path || rl.manager_id, w.depth + 1
     FROM walk w
     JOIN reporting_lines rl ON rl.employee_id = w.cur_id
      AND rl.relationship='PRIMARY' AND rl.effective_to IS NULL
     WHERE w.depth < 12 AND NOT rl.manager_id = ANY(w.path)
   )
   SELECT count(*) AS cyclic_primary_paths, count(DISTINCT start_id) AS employees_on_a_cycle
   FROM walk WHERE cur_id = start_id;
   ```

   Run this against each real instance before ruling. A non-zero result makes option (c)
   ("accept as unreachable") untenable.
3. **Is the depth-10 ceiling's `(null, null)` return the intended fallback** for this shape too?

## Tripwire contract
The tripwire asserts **current** behaviour and **will go RED when this is fixed — that is its job**.
Replace its assertion with the ruled behaviour; do not delete it.

## Agent Guidance
- **Backend / Security Agent**: do NOT add a uniform "self-exclusion" helper across both legs while
  doing unrelated work — that silently implements option (a) without a ruling.
- Any refactor of `ResolveDesignatedApproverAsync` must preserve this behaviour until ruled, or the
  tripwire will fail and should be treated as a real signal, not noise.
