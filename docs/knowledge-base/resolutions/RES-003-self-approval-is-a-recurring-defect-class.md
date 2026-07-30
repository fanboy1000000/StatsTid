# [RES-003] Self-approval is a RECURRING defect class — the segregation-of-duties rule is enforced per-path, not structurally

| Field | Value |
|-------|-------|
| **ID** | RES-003 |
| **Category** | resolution |
| **Status** | **OPEN — follow-up required.** Both known instances are FIXED; the CLASS is not closed |
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

A third instance of the same family is recorded and **unruled — and it is the most reachable of all
three**: FAIL-004's residual, approval-by-one's-own-delegate. **Confirmed empirically 2026-07-30**
(tripwire `S105UnitLeaderApprovalTests.RES_003_TRIPWIRE_OwnDelegate_CanApprove_TheAppointingLeadersOwnPeriod`,
driving the real `POST /api/approval/{id}/approve`):

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
- the vikar path: **still nothing** (the unruled residual)

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
3. **Rule on the FAIL-004 residual** (approval-by-one's-own-delegate) — the one known-open instance,
   and the only one reachable without legacy data. The operational question is narrow: when a leader
   goes on holiday and appoints a stand-in, may that stand-in sign off the holidaying leader's OWN
   timesheet? Note that denying it does not generally strand the period — a peer unit leader can
   approve it (verified: `peerOverLeader=True`), as can the leader's own edge manager; it falls to
   HR/org-scope only for a single-leader unit whose leader has no edge manager.
4. **A convention for in-memory mirrors of SQL predicates**: step 3c showed that hand-mirroring a
   `WHERE` clause into C# silently drops guards. The differential-test pattern used there is the
   mitigation and should be required for any future mirror, with self-pairs mandatory in the
   comparison set.

## Agent Guidance
- **Any agent adding or modifying an approval-authority path**: the self-exclusion is NOT optional and
  NOT implied by the surrounding checks. State explicitly in the change how the rule is enforced on
  the new path.
- **Any agent writing a differential/parity test over an authorization predicate**: include
  `actor == subject` pairs. Both probes that validated the S125 tests depended on coverage that a
  reasonable person would have omitted.
- Do NOT implement the choke point (item 2) opportunistically while doing unrelated work — it changes
  who may approve and needs the ruling in item 2 first.

## Related
- `FAIL-004` — instance 1, plus the unruled own-vikar residual
- `SPRINT-125.md` TASK-12502 (the fix) and TASK-12501 step 3c (instance 2)
- S105 Step-7a originally introduced `e.user_id <> @actorId` after an external lens caught the same
  class in the unit-leader path — so this is arguably the FOURTH occurrence, and the earliest.
