# PAT-016 — A predicate that gates a CONTAINER silently gates everything inside it

| Field | Value |
|-------|-------|
| **ID** | PAT-016 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S127 |
| **Domains** | Frontend, Backend, Security |
| **Tags** | visibility-predicate, dead-branch, ruling-scope, false-green, dead-affordance, falsification-probe |
| **Origin** | TASK-12707 (found) / TASK-12713 (fixed + generalised), S127 |

## What happened

Owner ruling **R1** withheld five month-derived figures from a manager viewing a `REJECTED` month. The
team-overview's expandable panel was gated by `canExpand = row.normRegistered !== null` — one of the
withheld figures. Tightening the *content* rule therefore closed the *container*.

Inside that container sat `rejectionReason` — a field the ruling **explicitly did not withhold**, because
it is the leader's own past decision rather than in-progress content. So the change withheld something no
ruling ever withheld, and did it **invisibly**: the branch stayed in the source, type-checked, passed
lint, and the suite was green — while being unreachable in production.

Nothing in the toolchain reports *"this JSX branch can no longer execute."*

## The rule

**When you tighten a visibility predicate, enumerate every field rendered inside its scope and check each
one against the ruling. Fields the ruling does not cover need a new home outside the gate — not silence.**

## The detection tell (cheap, and worth grepping for)

A render branch whose own guard is **mutually exclusive** with its container's guard is dead by
construction:

```tsx
canExpand = row.normRegistered !== null     // container: false for REJECTED after R1
  └── {row.status === 'REJECTED' && row.rejectionReason && …}   // content: only for REJECTED
```

Read the two conditions together and the contradiction is obvious. Read either alone and it is invisible.

## Why the tests did not catch it

The test that "covered" the branch stayed green for two sprints because its **fixture kept supplying a
field the server had stopped sending**. The code was dead and the test could not see it — the
FE-mock-masks-backend-shape class (S97→S99→S100) that motivated PAT-012, in a new medium.

## The falsification recipe this produced

For a change of this shape, **one arm proves only half of it**:

| Change | Probe | Must |
|---|---|---|
| A **promotion** (move a field out of a dead container) | revert the promotion | go RED |
| A **de-duplication** (one field, one place) | re-add the second render site | go RED |

Run **both**. Arm 1 alone permits two live render sites; arm 2 alone permits zero.

Note also that a *negative* assertion ("no strip on a non-REJECTED row") **cannot** detect a missing
promotion and will correctly stay green under arm 1 — count it as coverage of a different property, not
as evidence the promotion works. S127's implementing agent reported 3-of-4 red and said so explicitly
rather than claiming 4-of-4.

## Related

- S91 dead-affordance discipline — remove it or promote it; do not leave a branch that cannot fire.
- [PAT-014](PAT-014-characterization-baseline-one-inversion-per-encoding.md) — the same "prove the test
  can go red" discipline for duplicated predicates.
- [FAIL-005](../failures/FAIL-005-probe-restore-timestamp-stale-build.md) — restore hygiene for the
  probes above.
