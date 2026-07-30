# Sprint 125 — UI/testing (rolling), third of the kind

| Field | Value |
|-------|-------|
| **Sprint** | 125 |
| **Status** | OPEN (started 2026-07-30) |
| **Start Date** | 2026-07-30 |
| **End Date** | — |
| **Type** | Rolling UI/testing sprint (the third, after S123 and S124) — the owner drives the demo system by hand and names fixes one at a time; each clears the Pre-Implementation Gate with a review proportionate to its size, is implemented, and is verified against the RUNNING stack |
| **Orchestrator Approved** | per-task |
| **Build Verified** | FE tsc 0 / lint 0 |
| **Test Verified** | 711fe (+4) at TASK-12500; backend untouched so far |

## Shape
Opened because S124 was already CLOSED, PUSHED and CI-VERIFIED (`1e8bd27`, run `30516602046`, all 7
jobs) when the owner named these changes. Adding a task to S124 would have made its Step 7a artifacts
lie: both declare `reviewed-against-commit: b955020` and were run against that exact staged diff, so
appending work would leave the recorded dual-lens review no longer covering the sprint it claims to —
precisely the staleness the guard's `reviewed-against-commit` field exists to prevent (post-S38).

The repo's boundary: **docs-only backfill after close is fine** (S122/S123/S124 all did one); **new
behaviour is a new sprint**.

---

### TASK-12500 — "Overblik" rename + tighter panel spacing + foldable Overblik/Skema
| Field | Value |
|-------|-------|
| **ID** | TASK-12500 |
| **Status** | complete (2026-07-30) — FE-only. `SALDI` → **Overblik**; the ~52px dead gap between the summary and the grid cut to **12px** (measured live); both sections independently foldable, **session-sticky**, default OPEN. tsc 0 / lint 0; approval suites 64→68, full vitest 707→711. |
| **Agent** | Orchestrator (small-task exception; FE-only) |
| **Components** | `approval/TeamOversigt.tsx` + `.module.css`; `approval/ManagerSkemaGrid.tsx` + `.module.css`; `__tests__/TeamRowDetail.test.tsx` |
| **Refinement** | Inline (calibration: small-UI tier). **Step 4 dual-lens review SKIPPED per the skill's own calibration** — a label rename, a spacing change and a local fold toggle, no backend/contract/authority surface. Rationale recorded here rather than left implicit. |

**Owner requests (2026-07-30)**, verbatim in substance:
1. `SALDI` should be renamed to "Overblik".
2. Too much unused space between Saldi/Overblik and Skema.
3. Both should be foldable/expandable so one can be hidden when not needed — but **open by default
   when you press an employee**.

**The one fork, owner-ruled**: asked whether a fold should persist across employees. Initial reading
was per-row-reset (literally "open by default when you press an employee"); the owner ruled
**session-sticky** — *"Hm you are right. Session-sticky."*

**Why the state lives on the PAGE, not the row** — this is the whole mechanism, and getting it wrong
is the obvious trap: the detail panel UNMOUNTS when a row collapses (the accordion keeps exactly one
row open), so per-row state would reset on every expand and session-stickiness would be impossible.
Held one level up in `TeamOversigt`, a fold survives moving between employees and resets only on
reload — which is exactly "session-sticky" with no persistence machinery. Pinned by a test that folds
Overblik on one employee, opens a DIFFERENT employee, and asserts it is still folded.

**Interpretation recorded** (flagged to the owner, not silently chosen): the panel's top row is
`Saldi` + `Fordeling af arbejdstid` side by side. Since the owner described TWO foldable things and
had earlier called this "the summary at top", the WHOLE top row became the "Overblik" section — so
folding it hides the balances *and* the allocation split. The old `SALDI` column label is retired
because the section title now carries the name; keeping both would say it twice.

**The spacing was three rules stacking**, not one: `.detailInner`'s 20px flex gap + the skema block's
own 18px `margin-top` + its 14px `padding-top` ≈ 52px. The skema block's heading, border and offsets
moved out to the shared section header, and the flex gap dropped to 12px. Measured live at 12px.

**A11y**: the section headers are real `<button>`s carrying `aria-expanded`, not clickable divs —
keyboard- and screen-reader-operable, and pinned by a test that folds via `{Enter}`. Styled to match
the `.detailLabel` they replace (same size/weight/tracking/uppercase) so the panel's typography did
not change; only the caret and pointer are new.

**Validation** (live, against the running stack): default Overblik `aria-expanded=true` and Skema
`true`; old `SALDI` label gone (0 occurrences); folding Overblik leaves Skema open and vice versa;
the fold survives switching Kasper Olsen → Charlotte Schmidt; summary→grid gap 12px.

---

### FINDING-12502 / FAIL-004 — a PRE-EXISTING P7 defect, raised rather than folded in
| Field | Value |
|-------|-------|
| **ID** | FINDING-12502 → `docs/knowledge-base/failures/FAIL-004-…md` |
| **Status** | OPEN — confirmed empirically, tripwired, awaiting an owner ruling. NOT fixed. |
| **Found** | While scoping TASK-12501 (the F1 performance work), by the Step-4 internal review lens |

`ReportingLineRepository.ResolveDesignatedApproverAsync`'s inactive-manager escalation walk never
compares the candidate against the ORIGINAL employee, so on a cyclic legacy graph it returns the
employee as their own approver — and `IsEffectiveApproverOrUnitLeaderAsync` then ADMITS them over
their own period. That predicate also gates approve/reject/reopen, so the S105 segregation-of-duties
rule does not hold for this shape.

**The asymmetry is what makes it a defect, not a choice**: the unit-leader legs carry explicit
self-exclusion (`ul.user_id <> @employeeId`, `mv.vikar_user_id <> @employeeId`, `e.user_id <> @actorId`);
the edge leg carries none.

**Confirmed, not inferred.** The review flagged it from code; it was then proven with a tripwire —
`A → B`, `B → A`, B inactive → `ResolveDesignatedApproverAsync(A)` returns `(A, "DESIGNATED_MANAGER", 1)`
— and the tripwire was itself proven non-vacuous by inverting its assertion
(`Expected "PROBE_EXPECT_FAIL" / Actual "t7404_cyc_a"`). Suite 13→14 green.

**Owner ruling 2026-07-30: "Raise it as its own finding."** Correct: fixing it changes WHO MAY
APPROVE — a P7 behaviour decision, not a refactor. Folding it into a performance task would have
altered authorization under cover of an optimisation, and TASK-12501's characterisation baseline
would have silently encoded the new behaviour as the reference.

**Demo world checked**: ZERO cyclic PRIMARY paths (detection query recorded in FAIL-004 for reuse
against real instances). Production unchecked — nobody has looked.

---

## Carried in from S124 (not yet started)
1. **RES-002** — the deferred endpoint-level read gate (~6 reads). Must be period-status-based, must
   settle the recorded ACTOR-MODEL question (actor-blind withholding vs the HR-exempt month read),
   and should reuse `ApprovalVisibility.IsSubmittedToManager` rather than re-deriving the status set.
2. **The reopen fork** — a leader-reopened month reverts to un-submitted and therefore hides figures
   the leader already approved. Owner has not ruled.
3. **DemoSeed time registrations** — the demo world ships with ZERO of them, which is what made a
   correct review surface look broken in S124. Fold into the generator so it survives a reseed.
4. **The P4 arm on the write class** — a save against a SUBMITTED period does not transition status,
   so content can change after submission and the approval binds to content the employee never sent.
