# [FAIL-004] The designated-approver resolution could return an employee as their OWN approver

| Field | Value |
|-------|-------|
| **ID** | FAIL-004 |
| **Category** | failure |
| **Status** | **RESOLVED** — owner ruled option (a) 2026-07-30; fix shipped S125 with RED-on-old proof. The residual (own-delegate) was ALSO ruled and fixed the same day — see RES-003. Nothing here awaits a ruling. |
| **Sprint** | S125 (found + fixed) |
| **Date** | 2026-07-30 |
| **Domains** | Backend, Infrastructure, Security |
| **Tags** | authorization, reporting-line, designated-approver, escalation, cyclic-graph, vikar, segregation-of-duties, p7, pre-existing |

## Summary
`ReportingLineRepository.ResolveDesignatedApproverAsync` never compared a resolved manager against the
employee it started from, so it could return the employee as their own approver — and
`DesignatedApproverAuthorizer` then ADMITTED that pair. Since the same predicate gates
**approve / reject / reopen**, the S105 segregation-of-duties rule did not hold for those shapes.

Fixed by enforcing a self-exclusion invariant at **all three** candidate-returning legs.

## How it was found
Not by looking for it. During the S125 performance analysis of F1 (the period-status projection's
N+1), the Step-4 internal review lens traced the tally loop's authorization path and flagged that the
refinement's stated invariant *"self-exclusion: a leader never tallies their OWN period"* is **not
what the code does** — it held for the unit-leader legs only. Verified from code, then **confirmed
empirically** rather than accepted on the reviewer's word.

## TWO routes, not one — the second needs no cycle at all
The finding was originally written up as cyclic-legacy-data-only. Implementing the fix surfaced a
second, **structurally different** route through the vikar leg, and it is the more reachable of the two:

| Route | Shape | Pre-fix result |
|---|---|---|
| **Cyclic PRIMARY** | `A → B`, `B → A`, B inactive | `(A, DESIGNATED_MANAGER, 1)` |
| **Planted vikar** | `A → B`, B inactive, a `manager_vikar` row naming **A** as B's stand-in | `(A, ACTING_MANAGER, 0)` — **depth ZERO, no cycle anywhere** |

The DB permits the vikar row: `CHECK (absent_approver_id <> vikar_user_id)` only forbids someone
being their own stand-in, not a subordinate being their manager's stand-in. Both routes are
write-path-guarded going forward (`GuardNoCycleAsync`, anchored on the absent approver for vikar
creation, rejects any descendant as stand-in), so both are legacy/planted-data-only — **which is
exactly why the invariant now lives at the READ instead of being assumed from write-path history.**

## Mechanism (pre-fix)
`ResolveDesignatedApproverAsync` walks up while managers are inactive, tracking only
`currentEmployeeId`. Three legs can return a person: (1) ACTING line, (2b) the manager's vikar,
(3) the manager if active. None compared its candidate against the original `employeeId`. The
`while (depth < 10)` ceiling bounded the walk but did not prevent returning to the start.

## Why it reached authorization, not just resolution
`DesignatedApproverAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(actor: A, employee: A)` passed:
- `IsActiveLeaderOrAboveAsync(A)` — satisfied when A is an active LeaderOrAbove;
- the resolved designated approver of `A` **was** `A`, so the edge leg matched the actor;
- `ValidateSameOrganisationAsync(A, A)` — the `= ANY(@ids)` collapses to one row, so both org values
  are trivially equal.

## The asymmetry that made it a defect rather than a choice
The unit-leader legs carried **explicit** self-exclusion:
- enumeration: `ul.user_id <> @employeeId` and `mv.vikar_user_id <> @employeeId`
  (`ApprovalPeriodRepository.QueryUnitLeaderApproverCandidatesAsync`);
- gate: `e.user_id <> @actorId` (`DesignatedApproverAuthorizer.QueryUnitLeaderKindAsync`).

The S105 rule was therefore clearly intended. The **edge leg had no equivalent**. That inconsistency
— the same rule enforced on one path and absent on the other — was the finding.

## The ruling and the fix
**Owner ruled option (a) on 2026-07-30: skip the subject and KEEP LOOKING.** (Options (b) "bail out
to org-scope on contact with self" and (c) "accept as unreachable" were rejected.)

Implemented as a local `IsSubject(candidateId)` predicate applied at all three candidate legs. A
candidate equal to the subject is skipped and the walk continues; for leg (3) that means escalating
THROUGH an active subject rather than resolving to them.

**Deliberately NOT added: a visited-set to terminate cycles early.** It would cut the walk short and
so LOWER the reported `depth`, flipping the existing `FallbackTraversalWarning` (fires at depth > 3)
from firing to silent — a second behaviour change nobody ruled on. The depth-10 ceiling already
terminates, and cycles already burned to it before this change.

### A CORRECTION to how option (a) was sold
When the options were put to the owner, (a) was described as being able to find a valid approver
further up where (b) would give up. **That is true for the vikar route but NOT for the cyclic route.**
A walk's decisions are a pure function of `currentEmployeeId`, so returning to the subject re-derives
the same non-answer; a cycle through the subject therefore always exhausts the ceiling and both
options end at org-scope fallback, differing only in the reported `depth`.

The ruling is nevertheless vindicated by the vikar route, where the graph is NOT cyclic and there
genuinely IS somewhere further to look: (b) would have returned `(null, null, 0)` and dumped the
approval on HR, whereas (a) finds B's own manager C. The discriminating test asserts C precisely so
that it fails under (b) as well as under the original defect.

## Post-fix behaviour
| Shape | Result | Meaning |
|---|---|---|
| Cyclic `A → B → A`, B inactive | `(null, null, 10)` | org-scope fallback, and depth 10 > 3 trips `FallbackTraversalWarning` |
| Planted vikar naming A, `B → C` | `(C, DESIGNATED_MANAGER, 1)` | the legitimate approver one level up |

The high depth on the degenerate shape is asserted deliberately: it is what keeps a broken graph
**visible** as a data-quality signal instead of becoming a silent permanent detour to org-scope. That
also settles open question 3 without inventing a new return state.

## Confirmation (empirical, both directions)
Tests (`PeriodStatusAndPersonSearchReadsTests`):
- `FAIL_004_SelfExclusion_TwoCycle_NeverResolvesToTheSubject_FallsBackToOrgScope`
- `FAIL_004_SelfExclusion_PlantedVikarNamingTheSubject_SkipsIt_AndKeepsLooking`

Both were proven **RED on old** by neutralising `IsSubject` and rebuilding:
```
Expected: Not "t7404_cyc_a"   Actual: "t7404_cyc_a"      (2-cycle)
Expected:     "t7404_cyc_c"   Actual: "t7404_cyc_a"      (planted vikar — self at depth 0)
```
Restored → 15/15 green; reporting-line + vikar + designated-approver + delegate suites 162/162 green.

## A latent test-ORDER bug fixed alongside
`uq_reporting_line_active_primary` is a partial unique index (one active PRIMARY per employee), and
three tests in this class plant an active PRIMARY for `CycA` — the pre-existing cyclic-descendant test
points it at `CycC`, both FAIL-004 tests at `CycB`. The original tripwire did not clear its edges, so
it passed only on xUnit's method ordering; run in the other order it would have died on a unique
violation. `ClearCycEdgesAsync` now runs at entry AND in the finally of both FAIL-004 tests.

## Production data
The demo world was checked (2026-07-30): **ZERO cyclic PRIMARY paths, zero employees on a cycle.** No
real instance has been checked. This no longer gates anything — the fix is unconditional — but the
query is worth running once to learn whether any live instance carries the shape, since a hit means
somebody's approvals have been routing to org-scope-or-worse:

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

The vikar route has its own, cheaper detection query — worth running for the same reason:

```sql
SELECT mv.absent_approver_id, mv.vikar_user_id
FROM manager_vikar mv
JOIN reporting_lines rl
  ON rl.employee_id = mv.vikar_user_id
 AND rl.manager_id  = mv.absent_approver_id
 AND rl.relationship = 'PRIMARY' AND rl.effective_to IS NULL
WHERE mv.effective_to IS NULL;
```

## Residual — RULED AND FIXED 2026-07-30 (was: NOT fixed, deliberately)
⚠ **This residual is now tracked as part of a wider defect CLASS — see `RES-003`.** A second,
independent instance of the same rule failing surfaced the same day (the S125 step-3c prefetch would
have dropped the unit-leader self-exclusion), which reframes this from "one leftover question" to
"the segregation-of-duties rule has no single enforcement point".

**A subject's OWN vikar could be their approver — and unlike the two routes fixed above, this one
needed NO cyclic or imported data.** Confirmed at the endpoint 2026-07-30: a leader got 403 on their
own period, while the vikar they themselves appointed got 200 OK.

**Owner-ruled the same day** (*"Anna is on vacation, not her approver"*) and fixed: a vikar covering
leader L now grants authority over L's unit MEMBERS but never over L. See **RES-003** for the ruling,
the five sites it was applied at, and the verification that the period is not stranded. If A's own stand-in is V, resolution can return
V for A's period. That is approval-by-one's-own-delegate, a weaker and distinct concern from
self-approval, and it was not part of this ruling. Flagged here rather than folded in silently.

## Agent Guidance
- The invariant is stated in the XML doc on `ResolveDesignatedApproverAsync`: **the returned ManagerId
  is NEVER `employeeId`**. Preserve it in any refactor; the two tests above will catch a regression.
- Do NOT "simplify" the walk by adding cycle-termination — see the visited-set note above; it silently
  disables `FallbackTraversalWarning` on exactly the graphs that need it.
- The residual (a subject's own vikar as approver) was **owner-ruled and fixed 2026-07-30** — see
  RES-003 for the ruling of record and its application sites; preserve it in any refactor. (The
  narrower "approval-by-one's-own-delegate" distinction noted above was recorded, not separately
  re-ruled.)
