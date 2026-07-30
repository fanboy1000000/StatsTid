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

### TASK-12502 — FAIL-004: nobody is their own designated approver (a PRE-EXISTING P7 defect)
| Field | Value |
|-------|-------|
| **ID** | TASK-12502 (was FINDING-12502) → `docs/knowledge-base/failures/FAIL-004-...md` |
| **Status** | complete (2026-07-30) — owner ruled option **(a)** "skip the subject and keep looking"; fix shipped with RED-on-old proof on BOTH routes |
| **Agent** | Orchestrator (small-task exception; the fix is one predicate applied at three sites) |
| **Components** | `Infrastructure/ReportingLineRepository.cs` (`ResolveDesignatedApproverAsync`); `Tests.Regression/Approval/PeriodStatusAndPersonSearchReadsTests.cs` |
| **Found** | While scoping TASK-12501 (the F1 performance work), by the Step-4 internal review lens |

`ResolveDesignatedApproverAsync` never compared a resolved manager against the employee it started
from, so it could return the employee as their own approver — and `DesignatedApproverAuthorizer` then
ADMITTED that pair. The same predicate gates approve/reject/reopen, so the S105 segregation-of-duties
rule did not hold for those shapes.

**The asymmetry is what made it a defect, not a choice**: the unit-leader legs carry explicit
self-exclusion (`ul.user_id <> @employeeId`, `mv.vikar_user_id <> @employeeId`, `e.user_id <> @actorId`);
the edge leg carried none.

**Raised separately rather than folded into the performance task** (owner-directed): fixing it changes
WHO MAY APPROVE — a P7 behaviour decision, not a refactor. Folding it in would have altered
authorization under cover of an optimisation, and TASK-12501's characterisation baseline would have
silently encoded the defect as the reference. **Sequenced BEFORE F1 for the same reason** — a
characterisation captured first would have made this fix read as an F1 regression.

**A SECOND route surfaced while implementing, and it needs no cycle at all.** The finding was written
up as cyclic-legacy-data-only; the vikar leg can hand back the subject with a perfectly acyclic graph —
`A → B`, B inactive, and a `manager_vikar` row naming A as B's stand-in returned `(A, ACTING_MANAGER, 0)`,
i.e. self-approval at depth ZERO. The DB permits it (`CHECK (absent_approver_id <> vikar_user_id)` only
forbids being one's own stand-in). This is why the fix went to ALL THREE candidate legs rather than just
the escalation, and why the invariant is enforced at the READ rather than assumed from the write-path
guards.

**Correction owed on how the options were sold**: (a) was pitched as able to find a valid approver where
(b) gives up. True for the vikar route; NOT for the cyclic one — a walk's decisions are a pure function
of `currentEmployeeId`, so returning to the subject re-derives the same non-answer and both options end
at org-scope, differing only in reported depth. The ruling is vindicated by the vikar route, where there
genuinely is somewhere further to look. Recorded in FAIL-004 rather than left as a nicer story.

**Deliberately NOT added**: a visited-set to terminate cycles early. It would lower the reported depth
and so flip `FallbackTraversalWarning` (depth > 3) from firing to silent — a second behaviour change
nobody ruled on. Keeping depth 10 on the degenerate shape is what keeps a broken graph VISIBLE as a
data-quality signal instead of a silent permanent detour to org-scope; that also settles the finding's
third open question without inventing a new return state.

**Proof (both directions)**: two tests replace the tripwire, both proven RED by neutralising the
predicate and rebuilding — `Expected: Not "t7404_cyc_a" / Actual: "t7404_cyc_a"` (2-cycle) and
`Expected: "t7404_cyc_c" / Actual: "t7404_cyc_a"` (planted vikar). Restored → 15/15; reporting-line +
vikar + designated-approver + delegate 162/162.

**A latent test-ORDER bug fixed alongside**: `uq_reporting_line_active_primary` is a partial unique
index and three tests in the class plant an active PRIMARY for `CycA` (the pre-existing
cyclic-descendant test points it at `CycC`, both FAIL-004 tests at `CycB`). The original tripwire did
not clear its edges, so it passed only on xUnit's method ordering. `ClearCycEdgesAsync` now runs at
entry AND in the finally.

**Residual, flagged not fixed**: a subject's OWN vikar can still be their approver
(approval-by-one's-own-delegate). Distinct and weaker concern; needs its own ruling.

**Demo world**: ZERO cyclic PRIMARY paths. Production unchecked — no longer gating (the fix is
unconditional), but FAIL-004 carries detection queries for BOTH routes, worth running once to learn
whether any live instance carried the shape.

---

### TASK-12501 — F1: the period-status projection's per-pending-employee authorization storm
| Field | Value |
|-------|-------|
| **ID** | TASK-12501 |
| **Status** | **BLOCKED on an owner ruling** — Phase 0 (measure + characterise) COMPLETE; Phase A blocked on the staleness fork (Q1) |
| **Agent** | Orchestrator |
| **Components** | `Tests.Regression/Performance/S106SeedScalePerfFixture.cs` + `S106SeedScalePerfTests.cs` (Phase 0); `Infrastructure/ApprovalPeriodRepository.cs` + `DesignatedApproverAuthorizer.cs` (Phase A, not yet touched) |
| **Refinement** | `.claude/refinements/REFINEMENT-f1-period-status-n-plus-1.md` rev 2 (absorbs 5 internal-lens BLOCKERs; external lens re-run in flight) |

**The originally-reported cause was WRONG.** F1 was reported as "the roster read is unpaginated —
665 KB". Measured: the roster SQL is **12ms**, serialisation ~40ms, and the reused period-status
projection is **483ms of the 523ms**. The proposed pagination fix would have bought ~nothing.

**MEASURED AT MONTH-END LOAD: 27,001 commands and a median of 13.8 SECONDS at K=1000**
(`runs=[13731,13812,13850]` — under 1% spread, so the wall-clock is real, not noise). Taken on the S106
**testcontainer**, not the demo world — which means the "month-end seed mutates demo state" risk this
task carried is GONE, and nothing the owner clicks on was touched. STYX1's real ~1,925 pending is roughly
double. The earlier ~9.6s was an extrapolation; the measured number is worse. It also exceeds the
repo's EXISTING `TileBudgetMs = 8000`, so the high-K guard can be falsifiable against a budget that
already exists rather than an invented one — it lands WITH the fix rather than committed red.

**And per employee: 27.0 SQL commands** (rev 1's figure was ~5× too low),
identical at K=10 and K=20, while 0 pending costs exactly 1 command at BOTH 2,000 and 253 users. The
probe this needed turned out to already exist and already print the number — the existing
`TileCount_ScalesWithPendingSet_NotOrgSize_AtSeedScale` output. **At month-end STYX1 has ~1,925
pending ⇒ ~52,000 SQL round-trips for ONE page load** (~9.6s projected, on the page a manager opens
precisely then).

**≈16–17 of the 27 are re-asking answered questions**, all caller-side: the gate re-resolves the
employee's edge once per candidate (12 statements, 44%) having been handed it a line earlier; the role
floor is asked per (candidate, employee) PAIR **and twice** for a unit-leader candidate (once per leg);
same-Organisation is asked per pair; and the unit-kind query re-derives membership the candidate
enumeration already established. So the fix is redundancy deletion, not a SQL rewrite.

**Correction (external lens, 2026-07-30)**: an earlier revision said same-Organisation was ALSO checked
twice. It is not — on an edge mismatch the edge leg returns at step (3) *before* step (4)'s same-Org
check. With that fixed the derivation is 15 connection opens and 5+6+8+8 = **27 statements**, reproducing
the measurement exactly rather than merely landing near it.

**Correction (external lens)**: this entry originally cited "Σ tiles = 30 > 10 pending (invariant 6)".
**`Σ(tiles) ≥ pending` is FALSE** — an employee with no resolvable edge and no unit leaders, or whose
every candidate fails the floors, contributes ZERO tiles, and an existing test already exhibits that.
30 is a fact about the K=10 fixture, not a bound. The characterisation test's comment and assertion
framing were corrected; the assertion itself (30) was always factually right for that fixture.

**The N+1 is a DOCUMENTED, deliberately-accepted trade-off** (S106 / TASK-10605) whose premise fails
at month-end: it was accepted because cost tracks PENDING rather than org size — but at month-end
pending → org size and the two converge. The existing guard **structurally cannot catch it**: K tops
out at 20, the budget is 8s, and it asserts the multiplier is a small constant and linear in K — both
TRUE, and both exactly why K=1,925 is catastrophic. **It asserts strictly monotonic growth in K
(`count20 > count10 && count10 > small0`), so any successful fix BREAKS it** — editing it is part of
the deliverable, not collateral damage.

**Phase 0 delivered — the characterisation net** (2 tests, both green, added to the existing perf class
so they share its container rather than rebuilding a 2,500-user one):
1. `F1Characterisation_HappyPath_K10_…` — the EXACT `pendingCountByManager` map, the multi-tally
   property (Σ = 30 for this fixture, which a "count-once" rewrite would break), the status histogram,
   the exact SUBMITTED id set, and the documented `ORDER BY display_name, user_id` contract.
2. `F1Characterisation_ShapeMatrix_…` — four pending employees with structurally DIFFERENT candidate
   sets, pinning the invariants a prefetch rewrite would have to re-implement: NULL-unit → edge only;
   orphan → unit leaders only; a vikar-of-a-unit-leader IS a candidate; and an active, resolvable
   manager **without** LeaderOrAbove is REJECTED and must be absent from the map entirely.

**The map came out exactly as predicted from reading the code** (`em=2; l1=3; l2=3; xv=3`, role-revoked
manager absent) — independent confirmation that the 11-invariant list in the refinement is right,
rather than a plausible-looking list.

**Ordering note**: the baseline was captured AFTER TASK-12502 landed, deliberately. Captured before,
it would have encoded the self-approval defect as the reference and made that P7 fix read as an F1
regression.

**Isolation**: the shape matrix uses its own `perf_o3_x` prefix with its own add/clear pair, because
adding a vikar to the SHARED base scenario would change the candidate fan-out and move the 27.0
multiplier the existing perf assertions depend on. Verified: full perf class 6/6 green, multiplier
still 27.0.

---

### TASK-12501 step 1 — the authorizer adopts the repo's connection-reusing overload pattern
| Field | Value |
|-------|-------|
| **Status** | complete (2026-07-30) — SEMANTICALLY INERT, proven by the multiplier staying at exactly 27.0 |
| **Components** | `Infrastructure/DesignatedApproverAuthorizer.cs`, `ReportingLineRepository.cs`, `ApprovalPeriodRepository.cs` |

**The root cause, found by asking why the plan needed three separate mitigations.** Rev 3 of the
refinement had a blocking owner question (is a stale count acceptable?), a differential test for
Phase A, and another for Phase B. Three mitigations for one problem is a smell. The actual cause:

`ReportingLineRepository` is built throughout on the repo's **overload-pair pattern** — a
connection-reusing primitive `(conn, tx, …)` plus a self-contained overload that opens a connection and
DELEGATES to it (`ValidateSameOrganisationAsync` at `:397`/`:448`, rationale at `:405-412`).
**`DesignatedApproverAuthorizer` never adopted it**: all four primitives existed only in self-contained
form, each opening its own connection. Every symptom follows from that one gap — 15 connection opens per
pending employee; a gate that must RE-RESOLVE what its caller already computed (44% of round-trips)
because nothing can be handed in; and no way for two reads to share a snapshot.

**So the owner question dissolved rather than being answered.** With the pattern in place, step 2 can
make the pass a single snapshot, which makes step 3's redundancy deletion a provable equivalence instead
of a judgement call about acceptable staleness. Three mitigations collapse into one structural property.

**What landed**: `(conn, tx)` siblings for all four authorizer predicates and both private primitives,
plus one on `ResolveDesignatedApproverAsync`; `QueryUnitLeaderApproverCandidatesAsync` reuses the
caller's connection; and the projection's tally pass threads ONE connection. Delegation direction
matters — self-contained → reusing — so each rule has exactly ONE implementation, which makes
ADR-027/038's one-encoding requirement structural rather than something reviewers must police.

**Self-caught during review of the diff**: the first cut opened a *new* connection for the tally pass,
but step (1)'s `conn` is method-scoped and still open (its reader disposed, so the session is idle) —
two connections held where one does. The loop now reuses `conn`, so the whole projection runs on a
single connection. Semantically identical either way (both autocommit), which is why the blast-radius
run started before the edit remains valid evidence.

**Why it is semantically inert, and the proof.** `tx` is deliberately null, so every statement still
autocommits and each read observes the latest committed state exactly as before (Postgres transactions
are session-scoped; sharing a connection without one changes only who pays for the handshake). Proof:
the characterisation output is byte-identical (`em=2;l1=3;l2=3;xv=3`; `OPEN=253,SUBMITTED=10`) **and the
per-pending multiplier is still exactly 27.0 at both K=10 and K=20** — an unchanged statement count is
what distinguishes "changed who opens connections" from "changed what is asked". Wall-clock at K=20:
~310ms → 273ms.

**Verification**: characterisation byte-identical and multiplier 27.0 after the edit (21/21 on the
projection suites); blast radius across every caller of the authority predicate — reporting-line, vikar,
designated-approver, delegate, approval, approve, reopen, team-overview, compliance, skema —
**433/433 green**.

⚠ **Unresolved loose end, recorded rather than waved through**: the FIRST blast-radius run reported
432/433 with one failure that could NOT be identified, because that run was piped through `tail -6` and
the failure detail lives mid-output. The re-run was 433/433. "Passed on re-run" is weaker than
"identified and cleared" — the failing test is unknown, so it cannot be confirmed as an environmental
FAIL-002-class shed rather than a real intermittent. Lesson saved: never pipe a verification run through
`tail`; redirect to a file (the `launch-demo-system` skill already warned this for the DemoSeed loader —
the rule is general, and this cost a 25-minute rerun).

**Found while mapping this step — a step-2 BLOCKER, recorded before it could bite**:
`ValidateSameOrganisationAsync(conn, tx, …)` issues `SELECT … FOR UPDATE` (it is shared with the
S74-7403 write path, where pinning both user rows is the point). Under `tx: null` those locks release at
each implicit commit, so step 1 is unaffected — but inside a projection-long REPEATABLE READ transaction
they would be HELD for the whole pass, blocking writers to most of the Organisation's user rows. **Step 2
therefore needs a non-locking read variant of the same-Org check, not merely a transaction.**

---

### TASK-12501 steps 2+3a — the snapshot, and the memo it makes legitimate
| Field | Value |
|-------|-------|
| **Status** | complete (2026-07-30) — **27.0 → 10.2 statements/employee; K=1000: 13.8s → 6.2s (2.23×)**; characterisation byte-identical; 441/441 blast radius |
| **Components** | `Infrastructure/ApprovalAuthorityContext.cs` (new), `DesignatedApproverAuthorizer.cs`, `ReportingLineRepository.cs`, `ApprovalPeriodRepository.cs` |

**Step 2 — one snapshot for the whole projection.** A REPEATABLE READ transaction spanning BOTH phases.
Its purpose is not speed: it is what makes step 3's memoization *sound*. Inside a snapshot the rows
cannot change, so "ask once and remember" and "ask every time" are provably the SAME answer — caching
becomes an optimisation rather than an accepted staleness, which is why the owner's blocking question
about stale counts stopped needing an answer.

It also closes a skew that **already existed and that nobody chose**: phase (1) scanned employees on one
read, phase (2) evaluated authority on separate reads, so a reassignment landing mid-projection could
leave the page matching no instant in time. The old behaviour was not "fresher", it was arbitrary.

**The FOR UPDATE blocker, found while mapping step 1 and fixed here.**
`ValidateSameOrganisationAsync` issues `SELECT … FOR UPDATE` — load-bearing on the S74-7403 WRITE path,
where pinning both homes between validation and edge-insert is the point. Inside a projection-long
transaction those locks stop being released per statement and are held for the whole pass, blocking
writers to most of the Organisation's user rows. Fixed with a `lockRows` flag rather than a read-only
copy: **the null-checks, fail-closed semantics and equality comparison ARE the ADR-027 D2 rule**, and
duplicating them into a sibling would fork it. The flag changes the lock mode only, and defaults to the
write path's behaviour so no existing caller silently loses its pin.

**Step 3a — the memo.** `ApprovalAuthorityContext` caches the employee's resolved edge, the candidate's
role floor, and the same-Organisation verdict, for the life of ONE projection call. The design property
that matters: **every value is produced BY the authorizer's own code path and merely remembered** — the
caller cannot hand in an answer it computed some other way. It is a cache in front of one
implementation, never a parallel one, so ADR-027/038's one-encoding rule still holds.

**Why 10 and not the predicted ~6 — a deliberate choice, stated rather than glossed.** Only the SAFE
memoizations were taken. The residual same-Org and unit-kind queries could also be served from per-user
/ per-unit maps, but only by re-deriving predicates the SQL owns ("deny when the user is missing,
inactive, or their home Organisation is inactive"; the `unit_id IS NOT NULL` and `e.user_id <> @actorId`
bounds). That is the second encoding this design exists to avoid. Buying the last statements requires
the prefetch — and the differential test that comes with it.

**⚠ 6.2s is still far too slow, and the `<1s` criterion is NOT met.** At ~1,925 pending this is still
~12s. **The prefetch (step 3b) is therefore LOAD-BEARING, not optional** — exactly what the external
lens argued, now confirmed by measurement rather than by argument.

**The existing guard test still passes** (`204 > 104 > 1` — still linear in K, just a smaller constant).
Its strict-monotonic assertion breaks only when FLATNESS is achieved, so the "must be edited"
prediction stands but belongs to step 3b.

**Verification**: characterisation byte-identical (`em=2;l1=3;l2=3;xv=3`; `OPEN=253,SUBMITTED=10`);
blast radius 441/441 across reporting-line, vikar, designated-approver, delegate, approval, approve,
reopen, team-overview, compliance, skema, period-status and the perf class. Full output captured to a
file this time, not piped through `tail`.

---

### TASK-12501 step 3b — prefetch the resolver's INPUTS (not the decision), with a differential test
| Field | Value |
|-------|-------|
| **Status** | complete (2026-07-30) — **27.0 → 6.3 stmts/employee; K=1000: 13.8s → 3.8s (3.67×)**; characterisation byte-identical; 442/442 |
| **Components** | `IReportingLineDataSource.cs` + `PrefetchedReportingLineDataSource.cs` (new), `ReportingLineRepository.cs`, `DesignatedApproverAuthorizer.cs`, `ApprovalPeriodRepository.cs`, `ManagerVikarRepository.cs`, perf fixture + tests |

**The design that keeps this off the P7 surface.** Rather than batching the DECISION, the resolver's
four data questions moved behind `IReportingLineDataSource`. The R3 precedence, the FAIL-004
self-exclusion invariant and the depth-10 ceiling still live in ONE method and execute the same
branches in the same order — only where the facts come from changes: live SQL (one round-trip per
question) or three set-based reads over the whole Organisation. That is the difference between
"prefetch the inputs" and "fork authorization", and it is why the rule was never re-expressed in SQL.

Loading the whole Organisation rather than just the pending set is deliberate: the escalation walk
climbs through up to ten managers that are not known until it reaches them, so a pending-set-only
prefetch would miss them and silently change resolutions.

**The differential test is what makes it defensible** — `F1Differential_PrefetchedSource_MatchesSqlSource_ForEveryUser_TripleByTriple`
resolves EVERY user in the Organisation through BOTH sources and compares the full
`(ManagerId, ApprovalMethod, Depth)` triple. Pair-by-pair, not totals: totals can agree by luck while
individual resolutions are wrong in offsetting directions. Depth is compared too, since it drives
`FallbackTraversalWarning` — the same approver reached by a different route is still a behavioural
difference.

**Proven discriminating, not assumed.** Breaking the prefetched source (`IsUserActive` → always true)
produced exactly one divergence, named:
`perf_o3_x5: sql=(, , 1) prefetched=(perf_o3_xim, DESIGNATED_MANAGER, 0)`.

**THREE catches during the work, each by something doing its job:**
1. **The existing S106 guard caught a real regression I introduced**: building the prefetch
   unconditionally took the ZERO-pending case from 1 command to 4 (`Expected: 1, Actual: 4`). A
   2,000-user Organisation with nothing pending must still cost one query. Fixed the code — guarded the
   prefetch on a non-empty pending set — rather than relaxing the assertion.
2. **My own test documentation was wrong**: it claimed the differential test covered "a manager who is
   INACTIVE (the escalation walk)" when the fixture had none. Adding that shape turned out to be
   decisive — it is the ONLY shape that caught the deliberate break above. Without it the differential
   test would have passed a broken implementation and been decorative.
3. **A stale whole-file backup destroyed work**: restoring a snapshot to remove a temporary measurement
   probe silently reverted the differential test and the updated expectations that had landed since.
   Recovered by re-running the generating scripts; verified green. Same class as the `git checkout --`
   lesson — a whole-file backup goes stale the moment more work lands in that file.

**⚠ STILL NOT AT TARGET.** 3.8s at K=1000 is ~7s at real STYX1 month-end, against `<1s`. The residual
~6 statements/employee are the candidate ENUMERATION, the same-Organisation check and the unit-kind
lookup — none of which the prefetch touched. Flatness requires batching those three as well, with the
same differential-test obligation. The existing guard's strict-monotonic assertion therefore STILL
passes (`127 > 67 > 1`) and still awaits that step.

**Cumulative**: 27.0 → 6.3 statements/employee (4.3×); K=1000 13.8s → 3.8s (3.67×); zero-pending still
exactly 1 command; characterisation byte-identical at every step.

---

### TASK-12501 step 3c — FLAT. 27,001 commands / 13.8s → **9 commands / 79ms** at K=1000
| Field | Value |
|-------|-------|
| **Status** | complete (2026-07-30) — **the projection no longer scales with the pending count at all**; 462/462 |
| **Components** | `IAuthorityFactsSource.cs` (new), `DesignatedApproverAuthorizer.cs`, `ReportingLineRepository.cs`, `ApprovalPeriodRepository.cs`, perf tests |

| | Original | After 2+3a | After 3b | **After 3c** |
|---|---|---|---|---|
| Commands at K=1000 | 27,001 | 10,004 | 6,007 | **9** |
| Median wall-clock at K=1000 | 13.8s | 6.2s | 3.8s | **79ms** |
| Stmts per pending employee | 27.0 | 10.2 | 6.3 | **~0 (flat)** |
| K=10 vs K=20 commands | 271 / 541 | — | — | **9 / 9** |

**175× faster, ~3,000× fewer round-trips**, `<1s` met with a 12× margin, and 9 commands whether one
person has submitted or a thousand.

**What moved**: the last three per-employee lookups — the role floor, the home-Organisation lookup and
the unit-leader classification — went behind `IAuthorityFactsSource` (live-SQL and prefetched
implementations), and the candidate enumeration was batched with a plain `= @id` → `= ANY(@ids)`
widening of the identical predicate. The same-Organisation DECISION was extracted to
`ReportingLineRepository.DecideSameOrganisation` so the write path, the authority path and the
prefetched path all apply one set of null-checks, one equality and one pair of exception types.

**The discipline that held across all four steps: the DECISIONS never moved.** Precedence, floors,
fail-closed comparisons and self-exclusions still live in one place and execute identically — only
where the facts come from changed. That is why a 175× speedup could touch the approve/reject/reopen
authority path without forking it.

**The guard test broke exactly as predicted three refinement revisions ago**, and inverting it is the
deliverable rather than collateral damage. It asserted `count20 > count10 && count10 > small0` — a
faithful characterisation of the N+1 that S106 accepted on the premise that cost tracked the PENDING
set rather than org size. At month-end those converge, which is what made 13.8s possible. It now
asserts flatness DIRECTLY (`Assert.Equal(count10, count20)`), with the superseded expectation recorded
inline, because "smaller" and "independent of K" are different claims and only the second survives
month-end.

**Both differential tests were proven to discriminate, and both catches were pointed:**
1. Resolver probe (`IsUserActive` → always true) — caught ONLY by the inactive-manager shape added
   after noticing the test's own documentation claimed coverage the fixture did not have:
   `perf_o3_x5: sql=(, , 1) prefetched=(perf_o3_xim, DESIGNATED_MANAGER, 0)`.
2. Combined probe (segregation-of-duties exclusion dropped from the prefetched path) — caught ONLY
   because self-pairs were deliberately included in the comparison set:
   `(perf_o3_l1 -> perf_o3_l1): sql=False prefetched=True` — a leader admitted over their OWN period.

Both times the deliberate over-coverage is what made the test real rather than decorative. Note that
(2) is the same defect class as FAIL-004 found earlier the same day — a useful signal about where this
system's authorization is structurally fragile. **Raised as a tracked follow-up rather than left as an
observation: see FU-1 / RES-003 above.**

**Verification**: characterisation byte-identical at every step (`em=2;l1=4;l2=4;xv=4`;
`OPEN=253,SUBMITTED=10`); combined differential 126 pairs / 58 admitted / 0 divergences, with
non-vacuity asserted in BOTH directions (some admitted, not all); blast radius 462/462 across
reporting-line, vikar, designated-approver, delegate, approval, approve, reopen, team-overview,
compliance, skema, period-status, unit-leader, organisation and the perf class.

**Zero-pending still costs exactly 1 command** — the prefetch is skipped for an empty pending set.

---

## OPEN FOLLOW-UPS raised in S125 (for S126)

### FU-1 — **RES-003: self-approval is a recurring DEFECT CLASS, not three separate bugs** ⚠ owner ruling needed
→ `docs/knowledge-base/resolutions/RES-003-self-approval-is-a-recurring-defect-class.md`

**Both known instances are fixed. The CLASS is not closed.** Three instances of the same rule failing,
in unrelated paths, none found by looking for it:

1. **FAIL-004** (TASK-12502) — the escalation walk resolved a person to themselves; the gate ADMITTED
   the pair. Found by a review lens tracing an unrelated performance refinement's invariant list.
2. **TASK-12501 step 3c** — the prefetched unit-leader classification would have dropped the
   `e.user_id <> @actorId` exclusion. Caught by a deliberate probe, and ONLY because self-pairs were
   deliberately included in the differential comparison set
   (`(perf_o3_l1 -> perf_o3_l1): sql=False prefetched=True`).
3. **FAIL-004's residual — STILL UNRULED, and now CONFIRMED as the most reachable of the three.**
   Tripwired at the real endpoint (`RES_003_TRIPWIRE_OwnDelegate_CanApprove_TheAppointingLeadersOwnPeriod`):
   a leader gets **403** on their own period, while **the vikar they themselves appointed gets 200 OK
   and the period goes APPROVED**. Correction to the earlier framing: this does NOT share FAIL-004's
   cyclic-legacy-data precondition. A leader is a member of the unit they lead (D3), so appointing a
   stand-in makes that stand-in a candidate approver for every unit member — including the appointer.
   Ordinary supported flows only; the only instance reachable in a healthy production database.

Arguably a fourth and earliest: S105's Step-7a added `e.user_id <> @actorId` after an external lens
caught the same class in the unit-leader path.

**Root cause is structural**: the segregation-of-duties rule has NO single enforcement point. It is
re-stated by hand in five places and absent from a sixth, so every new authorization path — and every
in-memory mirror of an existing predicate — is a fresh chance to omit it. **Omission FAILS OPEN**: it
grants authority rather than withholding it. Both catches above were luck of coverage.

**Proposed** (needs an owner ruling on scope): audit every authority-granting path into a test matrix;
consider a single choke point in `IsEffectiveApproverOrUnitLeaderAsync` denying `actor == subject` by
default so the rule fails CLOSED — **but first rule whether any legitimate self-approval case exists**
(e.g. HR/GlobalAdmin acting on their own period via the org-scope branch); rule the FAIL-004 residual;
and require the differential-test pattern, with self-pairs mandatory, for any future in-memory mirror
of a SQL predicate.

### FU-2 — the rest of the performance analysis (F2–F6), untouched
F1 is done (27,001 commands / 13.8s → 9 / 79ms). Not started: **F3 code splitting** (594KB single
chunk — user-visible on EVERY page load, so probably the highest-value next one if page-load feel is
the goal), F2 StrictMode double-fetch (dev-only), F4, F5 latent flex-read scale risk, F6 loading-flash
perception.

### FU-3 — carried from S124, unchanged
See the section below.

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

**Phase A is blocked on one owner question (rev 3).** The external lens caught that rev 2's claim
"Phase A is equivalent by construction" is FALSE: one `today` fixes the date, not the snapshot. The
caller's resolution and the gate's re-resolution are separate reads, so a reassignment landing between
them is currently seen by the SECOND one; reusing the first uses the older view. That is a real P7
behaviour fork, not a refactor, and a static characterisation baseline cannot detect it. Recommendation
recorded in the refinement: accept it, because (a) the projection is already not a consistent snapshot
(step 1 and step 2 are separate reads) and (b) nothing can be AUTHORIZED by a stale tile — the
approve/reject/reopen endpoints re-evaluate in-lock at action time. Alternative if not acceptable: run
step (2) in one REPEATABLE READ transaction, which also removes the existing step-1/step-2 skew.

**Also corrected in rev 3**: Phase B is **load-bearing, not optional**, if the "O(1) in K" criterion
stands — Phase A still issues ~6 statements × K, which is 4.5× better but still linear. The criteria are
now split (Phase A: a per-employee ceiling; Phase B: flatness in K), the unverifiable
connection-open criterion was dropped (`DbCommandCounter` has no connection counter), and the latency
checks now specify layer, environment, warm-up and median-of-3.
