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

A third instance of the same family is already recorded and **unruled**: FAIL-004's residual — a
person's OWN vikar can still be their approver (approval-by-one's-own-delegate).

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
3. **Rule on the FAIL-004 residual** (a subject's own vikar), which is the one known-open instance.
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
