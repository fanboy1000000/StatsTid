# [RES-003] Self-approval is a RECURRING defect class — the segregation-of-duties rule is enforced per-path, not structurally

| Field | Value |
|-------|-------|
| **ID** | RES-003 |
| **Category** | resolution |
| **Status** | **CLOSED (S130, 2026-08-14)** — the CLASS is now fail-closed structurally: the owner ruled SoD (block self on manager decisions, no exemption; Økonomistyrelsen funktionsadskillelse), and SEC-009 (the reachable org-scope self-approval) is fixed via the **single choke point** (item 2) + the per-leg differential test matrix + a direct choke-point contract pin (item 1). See the S130 close note at the foot of this file. *(Was PARTIALLY CLOSED — all 3 instances fixed but the class open pending the audit + choke-point ruling.)* |
| **Sprint** | Sprint 125 (raised) |
| **Date** | 2026-07-30 |
| **Domains** | Backend, Infrastructure, Security |
| **Tags** | access-control, approval, segregation-of-duties, self-approval, authorization, audit-required, p7, defect-class |

## The observation, and why it is worth its own entry
Two INDEPENDENT self-approval defects surfaced on the same day, in unrelated code paths, neither
found by looking for them:

| # | Where | How it would have shipped |
|---|---|---|
| 1 | **FAIL-004** — `ResolveDesignatedApproverAsync`'s escalation walk never compared a resolved manager against the employee it started from, so on a cyclic legacy graph a person resolved to themselves and `IsEffectiveApproverOrUnitLeaderAsync` ADMITTED the pair | Found by a review lens tracing an unrelated performance refinement's invariant list |
| 2 | **TASK-12501 step 3c** — the prefetched unit-leader classification would have dropped the `e.user_id <> @actorId` exclusion | Caught by a deliberate probe, and ONLY because self-pairs were deliberately included in the differential comparison set: `(perf_o3_l1 -> perf_o3_l1): sql=False prefetched=True` |

A third instance of the same family — **since RULED and FIXED, see "Instance 3" below** — was
FAIL-004's residual, approval-by-one's-own-delegate, and it was the most reachable of the three.
**Confirmed empirically 2026-07-30** at the real `POST /api/approval/{id}/approve` before the fix:

```
leader approving their OWN period            → 403 Forbidden   (the S105 rule holds)
their OWN appointed vikar, same period       → 200 OK, APPROVED
```

**A correction to how this was first framed.** It was described as a narrow leftover, on the
assumption it shared FAIL-004's cyclic-legacy-data precondition. **It does not.** A leader IS a member
of the unit they lead (the D3 member-invariant), so when they appoint a vikar and go away, that vikar
becomes a candidate approver for every member of the unit — including the appointing leader. Every row
involved is created through supported paths. This is the ordinary "I am on holiday, cover my
approvals" flow, not a legacy-import artefact — which makes it the ONLY one of the three instances
reachable in a healthy production database.

**Three instances, one rule.** That is a class, not a coincidence.

## The root cause is structural, not three separate oversights
The segregation-of-duties rule — *nobody approves their own period* — has **no single enforcement
point**. It is re-stated by hand wherever someone remembered:

- `ApprovalPeriodRepository.QueryUnitLeaderApproverCandidatesAsync`: `ul.user_id <> @employeeId`
  and `mv.vikar_user_id <> @employeeId`
- `DesignatedApproverAuthorizer.QueryUnitLeaderKindAsync`: `e.user_id <> @actorId`
- `ReportingLineRepository.ResolveDesignatedApproverAsync`: **nothing until FAIL-004 added it**
- `PrefetchedAuthorityFacts.GetUnitLeaderKindAsync`: a hand-written mirror of the SQL exclusion
- the vikar path: **nothing until instance 3 was ruled and fixed** (below) — and the fix then had
  to be applied at FOUR live sites plus a fifth, dead copy

Every new authorization path is therefore a fresh opportunity to omit it, and omission FAILS OPEN —
it grants authority rather than withholding it. Both instances above were caught by luck of coverage:
instance 1 by a reviewer reading an invariant list for a different task, instance 2 by a test-design
choice (including self-pairs) that could as easily not have been made.

## Why this is filed as OPEN rather than closed with the two fixes
Both known instances are fixed and tripwired. But the fixes are per-path, so the class remains live:
the next authorization path added — or the next in-memory mirror of an existing predicate — can omit
the rule exactly as these did, and nothing structural will catch it.

## Proposed follow-up (needs an owner ruling on scope)
1. **Audit every path that can grant approval authority** and assert the rule holds on each: the edge
   leg, the unit-leader leg, the vikar leg, the org-scope/HR fallback, and both prefetched mirrors.
   The audit's OUTPUT should be a test matrix, not a document.
2. **A single structural choke point.** Every current path funnels through
   `IsEffectiveApproverOrUnitLeaderAsync`. A guard there — deny when `actorId == employeeId` unless
   an explicit, named exemption applies — would make the rule fail CLOSED by default and turn each
   per-path exclusion into a defence-in-depth layer rather than the only line.
   ⚠ Needs a ruling first: is there ANY legitimate self-approval case (e.g. an HR/GlobalAdmin acting
   on their own period, which today routes to the org-scope branch)? If yes, the choke point needs
   that exemption to be explicit and tested rather than implicit.

   **CONFIRMED REACHABLE, and more so than instance 3** (retrospective internal review, 2026-07-31).
   `OrgScopeValidator.ValidateEmployeeAccessAsync` self-excludes only `IsEmployeeOnly` actors; for
   LocalHR+ the actor's own id resolves their own org and returns allowed. `ApprovalEndpoints` wraps
   BOTH the pre-tx gate and the in-lock re-eval in `if (!orgScopeAllowed)`, so an HR/GlobalAdmin can
   submit and then approve their OWN period, audited as `ORG_SCOPE_FALLBACK`.

   Instance 3 was reclassified from trade-off to DEFECT *because it needed no legacy data*. **This path
   needs no delegation row, no unit-leader row and no cycle** — strictly more reachable than the
   instance that was fixed. That makes this item the highest-value part of closing the class.
3. ~~**Rule on the FAIL-004 residual**~~ — ✅ **RULED AND FIXED 2026-07-30. See below.**
4. **A convention for in-memory mirrors of SQL predicates**: step 3c showed that hand-mirroring a
   `WHERE` clause into C# silently drops guards. The differential-test pattern used there is the
   mitigation and should be required for any future mirror, with self-pairs mandatory in the
   comparison set.

## Instance 3 — RULED AND FIXED (2026-07-30)

**The owner's ruling, and the reasoning that reframed it:**

> *"I don't see why we would change who approves Anna. Anna is on vacation, not her approver."*

That reframing is what turns this from a trade-off into a defect. A vikar exists to cover the
approvals an absent leader **OWES** their unit. Who approves the LEADER is a separate question with a
separate answer — their own edge manager, or a peer unit leader — and **that answer is entirely
unaffected by the leader being away**. The delegate was never needed for the leader's own period.

**This corrects an error in how the question was first put.** It was presented as a balance between
segregation of duties and availability — "block it and their month waits until they are back". That
cost does not exist: the leader's approver is unchanged whether they are at their desk or on a beach.
The availability argument was invented, and it was the only argument for keeping the behaviour.

**The fix**: a vikar covering leader L grants authority over L's unit MEMBERS, never over L. One
predicate — `mv.absent_approver_id <> e.user_id` — applied at every site that grants
vikar-of-unit-leader authority:

| Site | |
|---|---|
| `ApprovalPeriodRepository` — dashboard candidate CTE (`unit_led_members` path-3) | what a vikar SEES |
| `ApprovalPeriodRepository` — batched candidate enumeration | what the tiles COUNT |
| `DesignatedApproverAuthorizer` — the gate's `LEFT JOIN` | what a vikar may ACT on |
| `PrefetchedAuthorityFacts` — the in-memory mirror | the projection's fast path |
| *(a fifth, unreferenced copy — DELETED)* | orphaned by TASK-12501 step 3c |

**Five sites for one rule is the RES-003 argument made concrete**, and the fifth being dead code that
still contained the predicate is precisely the rot this entry warns about: an unused duplicate of an
authorization predicate is the thing that later gets copied without its guards.

**Verified, both directions:**
- the leader still cannot approve their own period (403, unchanged);
- their appointed vikar now cannot either (403 — was **200 OK / APPROVED**);
- **the period is NOT stranded**: a peer unit leader approves it exactly as they would if the leader
  were present. This arm is asserted in the test, because without it the ruling would look like it
  costs availability;
- **the vikar still covers everything the absent leader OWED** — approving a unit MEMBER still
  succeeds. Narrowing a rule must not break the feature it narrows.
- the combined differential test moved from **58 to 57 admitted pairs** — exactly one removed,
  `(vikar → their appointing leader)`, and nothing else.

Tests: `S105UnitLeaderApprovalTests.RES_003_OwnDelegate_CannotApprove_TheAppointingLeadersOwnPeriod`
and `…_StillApproves_TheAbsentLeadersUnitMembers`.

Blast radius 464/464 (3 FAIL-002-class container-socket drops in `DockerHarness.StartAsync()` —
identical stack, no assertion involved — isolation-cleared 35/35).

## Agent Guidance
- **Any agent adding or modifying an approval-authority path**: the self-exclusion is NOT optional and
  NOT implied by the surrounding checks. State explicitly in the change how the rule is enforced on
  the new path.
- **Any agent writing a differential/parity test over an authorization predicate**: include
  `actor == subject` pairs. Both probes that validated the S125 tests depended on coverage that a
  reasonable person would have omitted.
- Do NOT implement the choke point (item 2) opportunistically while doing unrelated work — it changes
  who may approve and needs the ruling in item 2 first.
- The instance-3 fix narrows the vikar path only. It does NOT close the class: items 1, 2 and 4
  remain open, and a new authorization path can still omit the rule and fail OPEN.

## Related
- `FAIL-004` — instance 1, plus the unruled own-vikar residual
- `SPRINT-125.md` TASK-12502 (the fix) and TASK-12501 step 3c (instance 2)
- S105 Step-7a originally introduced `e.user_id <> @actorId` after an external lens caught the same
  class in the unit-leader path — so this is arguably the FOURTH occurrence, and the earliest.

## S130 close note (2026-08-14) — the class is now fail-closed
Surfaced again as **SEC-009** by the S129 security sweep (the org-scope/HR-fallback self-approval,
confirmed reachable + mis-audited). Owner ruled the SoD line (block self on manager DECISIONS —
approve / reject / reopen-of-`APPROVED`; **no exemption**; allow the pre-approval self-undo of one's
own `EMPLOYEE_APPROVED`). Fixed structurally, satisfying the two proposals above:
- **Item 2 (the choke point):** a fail-closed self-guard as the first statement of the terminal
  `DesignatedApproverAuthorizer.IsEffectiveApproverOrUnitLeaderAsync` (all public overloads funnel
  here) → every edge / unit-leader / vikar path self-excludes by default; the per-path SQL exclusions
  become defence-in-depth. The one leg that bypasses the predicate — the org-scope/HR fallback — is
  covered by the shared `ApprovalSelfGuard.IsSelf` at the three manager-decision endpoints.
- **Item 1 (the audit as a test matrix):** `tests/StatsTid.Tests.Regression/Approval/
  SelfApprovalGuardTests.cs` — a per-leg self-pair + other-actor differential (the org-scope self case
  is a genuine 200→403), positive-self-match, guard-ordering (self on ineligible status → 403 not 409),
  the reopen split, no-over-block (send/employee-approve), and a **direct choke-point contract pin**
  (`IsEffectiveApproverOrUnitLeaderAsync(x, x) == false` while `x` holds real authority over another).
- Dual-lens: refinement approved (Codex cycle-2) + implementation reviewed (Codex APPROVED-WITH-
  WARNINGS / internal 0-BLOCKER), the one warning (pin the choke point) closed by the direct test.
The SoD rule now has a single structural enforcement point; a future authority path inherits the
fail-closed default. **CLASS CLOSED.**
