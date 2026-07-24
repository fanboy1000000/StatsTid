# Sprint 123 — UI fixes (rolling)

| Field | Value |
|-------|-------|
| **Sprint** | 123 |
| **Status** | CLOSED (2026-07-25) |
| **Start Date** | 2026-07-24 |
| **End Date** | 2026-07-25 |
| **Type** | Rolling UI-fixes sprint — tasks added incrementally by the owner; each is a small, mostly FE-only polish/feature with its own refinement + light review, implemented + verified as it lands |
| **Orchestrator Approved** | per-task |
| **Build Verified** | dotnet build green; FE tsc 0 / lint 0 |
| **Test Verified** | 868u + 1368r + 6s + 55demoseed + 679fe = 2976 (local unit+regression+FE green; smoke+demoseed carried, CI-verified at close) |

## Shape
A polish sprint for the frontend admin surfaces. Unlike the S111–S122 program sprints, this is **rolling**: the owner names UI fixes one at a time, each cleared through the Pre-Implementation Gate (`refine-requirements`) with a proportionate review (external Codex lens by default; internal Reviewer escalation only if a task turns structural), then implemented FE-only and pinned by vitest. Priority order unchanged (this is P9/UX work; nothing here touches rules/events/payroll/security). Closed when the owner is done adding tasks; validation + INDEX row + close-gates at close per the standing discipline.

---

### TASK-12300 — "Vis/Skjul ledere" toggle on the Organisation-og-medarbejdere page
| Field | Value |
|-------|-------|
| **ID** | TASK-12300 |
| **Status** | complete (2026-07-24) — FE-only, exactly the 2 in-scope files. tsc 0 / lint 0; StrukturPanel suite 25→27; full vitest 663→665. The peer-layers model landed (`showLeaders` default true; `toggleLeaders` mirrors `togglePeople` incl. the descendant reveal; the `toggle-leaders` ghost button ordered org→ledere→medarbejdere); medHeader count = VISIBLE people; leaders-on+employees-off → static leader report-count (no expand chevron); leaders-off+employees-on → non-leaders FLAT. **Both-layers-on reproduces today's exact nested view — pinned by a dedicated invariant test.** Tests: the `:300` renamed to the non-leaders-only semantics, `:322` adjusted, a new `toggle-leaders` test (OQ-3 flat), the both-on invariant test, and `toggle-leaders` added to the `:367` dead-button allowlist. |
| **Agent** | UX/Frontend |
| **Components** | frontend/src/pages/admin/enhedsspor/StrukturPanel.tsx (+ its `__tests__/StrukturPanel.test.tsx`) |
| **Refinement** | `.claude/refinements/REFINEMENT-s123-t1-leaders-toggle.md` — READY (owner-confirmed peer-layers model 2026-07-24; Step-4 Codex 0B/2W/2N absorbed) |

**Description**: Add a third visibility toggle "Vis/Skjul ledere" (testid `toggle-leaders`) peer to the existing "Vis/Skjul org." + "Vis/Skjul medarbejdere", so the org can be viewed with leaders only (or any layer combination). **Owner ruling: peer-layers model** — the existing "medarbejdere" toggle governs NON-LEADER employees; leaders get their own toggle; both-on reproduces today's exact nested view (leaders-as-parents, reports nested — the load-bearing regression invariant). `showLeaders` state (default true); `toggleLeaders` mirrors `togglePeople` (incl. the descendant-unit reveal); `walkUnit` gating with the medHeader count = VISIBLE people; leaders-on+employees-off → static leader report-count (no expand chevron); leaders-off+employees-on → non-leaders render flat. FE-only, single file + test; single-section render (no `RenderNode` shape change — the Codex-suggested simpler shape).

**Validation Criteria**:
- [ ] The `toggle-leaders` button renders peer to the other two; independently toggles leader visibility; "org + leaders only" works
- [ ] Both-layers-on reproduces today's exact nested output (regression-pinned); medHeader count honest per layer
- [ ] Tests: new `toggle-leaders` test + the 2 renamed medarbejdere-semantics tests + `toggle-leaders` added to the `:367` dead-button allowlist (S91); full vitest green; tsc 0 / lint 0


### TASK-12301 — Search result → reveal the person + open their edit drawer
| Field | Value |
|-------|-------|
| **ID** | TASK-12301 |
| **Status** | complete (2026-07-24) — FE-only, 3 components + their 3 tests. tsc 0 / lint 0; full vitest 665→669 (+4). Clicking a person result navigates to their org, REVEALS their row in place (both people-layers un-hidden + the leader/med chain un-collapsed via `medClosed[row.unitId ?? selectedNode.id]` — the org-homed-safe key; scroll+flash), and opens their edit drawer — the S107-deferred flow (drawers landed S108). Transient host `pendingFocus` (off nav history) → panel focus effect `[roster, focusPersonId]` (per-org roster OBJECT, before the early return; `rowById` O(1); minimal dep-array avoids the unmemoized-open re-fire loop; a `focusReqRef` latest-request guard drops stale `fetchUser` settles). Invariants pinned: Back/Forward no-reopen; same-person re-fire (host null→id edge); not-found/roster-error/cross-org strand gracefully. **A post-implementation Codex code-review caught the org-homed-`unitId===null` reveal BLOCKER the internal-Reviewer refinement lens missed (drawer opened but row stayed hidden) + a stale-fetch race — both fixed + regression-pinned (the org-homed reveal test fails-under-bug).** No new button → S91 allowlist unchanged. OQ-1=(a) reveal-in-place / OQ-2 units-out-of-scope. Clicking a person search result now navigates to their org, REVEALS their row in place (org stays selected; both people-layers un-hidden + the leader/med chain un-collapsed; scroll+flash), and opens their edit drawer — completing the S107-deferred flow (drawers landed S108). Transient host `pendingFocus` (off the nav history) → StrukturPanel focus effect keyed `[roster, focusPersonId]` (per-org roster OBJECT, before the early return; `rosterIndex.rowById` O(1); dep-array kept minimal to avoid the unmemoized-`openEditPerson`/`fetchUser` re-fire loop). Invariants confirmed: Back/Forward does NOT re-open; a repeat search of the SAME person re-fires (host null→id edge); not-found/roster-error/cross-org strands gracefully (no drawer, no throw). No new button → S91 allowlist unchanged. Owner rulings OQ-1=(a) reveal-in-place / OQ-2 units-out-of-scope. |
| **Agent** | UX/Frontend |
| **Components** | SearchOverlay.tsx, OrganisationOgMedarbejdere.tsx, StrukturPanel.tsx (+ their 3 `__tests__`) |
| **Refinement** | `.claude/refinements/REFINEMENT-s123-t2-search-open-person.md` — READY; Step-4 internal Reviewer 0B/3W/5N absorbed (the effect-dep loop, the navigate() de-dup → transient state, reveal completeness, the roster-object signal, stranded-intent guard). External Codex hung on infra mid-review → re-run as a code-review of the diff post-implementation. |

---

### TASK-12302 — Årsoversigt: Flex rename + rest→saldo + eligibility gating + days/hours
| Field | Value |
|-------|-------|
| **ID** | TASK-12302 |
| **Status** | complete (2026-07-24; grid-cell layout polish 2026-07-25) — Backend + FE + regen. Backend: SENIOR_DAY category EXCLUDED when senior-ineligible (the eligibility block RELOCATED above the loop; eligible-senior path byte-unchanged — reproduced the admin01 null-birthdate bug + fixed); new additive `YearOverviewHeader.fullDayNormHours` (authoritative weekday norm via `ComputeWeekdayNormAtAsync`); regen sha-idempotent, convention 134/3/9, drift green; S120:354 seed fix + new eligible/ineligible-senior + part-time-norm `YearOverviewTests` (scoped 36 green). FE: Flex→"Difference fra norm tid - år"; rest→saldo EVERYWHERE; ineligible entitlements SHOW NOTHING (no tile, no grid row — CSS `.statRow` reflow to `auto-fit/minmax` so the shorter strip packs); days/hours display keyed off type — HOURS-FIRST `H (D dage)` for ferie/barns-sygedag/flex, DAYS-ONLY for omsorg/senior/særlige-feriedage; divide-by-zero guard (`norm > 0`, covers 0%-part-time AND null); the disposition row follows the category unit. **Grid-cell layout polish (2026-07-25, owner): the hours-first GRID cells STACK — hours on the top line, the day-equivalent below in a smaller muted font (`.cellHours`/`.cellDays`); the inline `H (D dage)` was too cramped in the dense 12-column matrix. TILES keep the inline form (they have room); days-only cells stay single-line. The 3 grid-cell test assertions updated to check the two spans separately.** Integrated FE tsc 0 / lint 0 / vitest 677. |
| **Refinement** | `.claude/refinements/REFINEMENT-s123-t3-arsoversigt.md` — READY; owner OQ-1 show-nothing / OQ-2 rest→saldo-all / OQ-4 profile=separate; Decisions 2&3 (hours-first / days-only split); Step-4 dual-lens: Codex (cap-scope, CSS, divide-by-zero, S120 count) + Reviewer (relocate-don't-duplicate, the exact S120:354 seed break, divide-by-zero=0-not-null, disposition-row pin) — all absorbed. |

### TASK-12303 — Skema absence-cell registration: no prefill, fill-to-norm, absence-only cap
| Field | Value |
|-------|-------|
| **ID** | TASK-12303 |
| **Status** | complete (2026-07-24; delete-again fix + Step-7a WARNING absorption 2026-07-25) — FE-only (SkemaGrid + SkemaPage + useSkema + 2 tests). The empty-cell full-day PREFILL removed (all categories); full_day_only categories FILL-TO-NORM ON INPUT (not blur — the 1s-autosave race would 422 a partial; D-A whole-day PRESERVED); null-basis full-day rows BLOCKED (no partial emitted → no guaranteed 422); the ≤-daily-norm cap surfaced inline as ABSENCE-ONLY (work time excluded — the backend D3 cap is absence-only; 4h work + 4h ferie legal, test-pinned) + a shape-discriminated inline 422 alert in useSkema (was swallowed by the page-level error). Backend UNTOUCHED. **DELETE-AGAIN FIX (2026-07-25, owner-reported): a full_day_only cell whose value was added COULD NOT BE DELETED — the on-input fill-to-norm refilled the DISPLAY on every keystroke, so backspacing "7,4"→"7," refilled to "7,4". Fixed: the display fill-to-norm now fires ONLY on the empty→value transition (`snapDisplay = canSnap && wasEmpty`, `wasEmpty` = committed null/0); an existing day edits down to empty freely. Persistence is UNCHANGED (a non-zero full-day entry still always emits the whole-day basis → the no-partial-autosave/422 invariant holds). New incremental-deletion regression test (old code would refill).** **STEP-7a WARNING ABSORBED: the S123 absence-cap 422 branch discriminated purely on SHAPE (maxHours+totalHours, no absenceType) — but the pre-existing >24h `work_time_exceeds_day` 422 carries the SAME shape, so a work-time reject mis-surfaced the absence-cap alert. Re-anchored on the specific error string `'Total absence hours exceed norm day'`; work-time 422 falls through to its prior raw-text branch. Negative regression test added.** vitest (integrated) 677→678. |
| **Refinement** | `.claude/refinements/REFINEMENT-s123-t4-skema-cell-registration.md` — READY; owner Decision 1 (+ clarification: full-day cats fill-to-norm on value-add, no D-A reversal); Step-4 dual-lens (2 BLOCKERs: cap=absence-only, fill-on-input; + null-basis guard, shape-not-string discrimination) absorbed. |

### TASK-12304 — Profile-page entitlement display gated by eligibility (DEFERRED → next UI-polish sprint)
| Field | Value |
|-------|-------|
| **ID** | TASK-12304 |
| **Status** | DEFERRED (2026-07-25, owner: "we will take any follow ups in the next UI polish sprint") — grounding found the described "profile-page barns-sygedag saldo" surface does NOT exist as a rendered screen; the live divergence is that `GET /balance/{id}/summary` (BalanceEndpoints.cs:221-384) is NOT eligibility-gated (emits CHILD_SICK/SENIOR_DAY rows regardless of opt-in/age), whereas S123 made `/overview` gate SENIOR_DAY — the two balance endpoints now diverge (Step-7a Reviewer N3). Carried to the next UI-polish sprint as a `/summary` eligibility-consistency pass (pre-existing; not an S123 regression). |

---

## Test Progression (per-task; consolidated at close)
| Task | FE tests before | after | Δ |
|------|-----|-----|---|
| 12300 | 663 | 665 | +2 |
| 12301 | 665 | 669 | +4 |
| 12302 (incl. grid two-line polish) | 669 | 677 | +8 |
| 12303 (incl. delete-again fix + WARNING absorption negative test) | 677 | 679 | +2 |
| **FE total** | **663** | **679** | **+16** |

Backend: Unit 868→868 (no unit delta); Regression 1364→1368 (+4: the new eligible/ineligible-senior + part-time-norm `YearOverviewTests`; the `S120BalanceSpecRuntimeTests` birth_date seed fix is count-neutral). Smoke 6 + demoseed 55 carried (unchanged surfaces; CI-verified at close).

## Test Summary
| Suite | Previous (S122) | Current (S123) | Δ |
|-------|----------|---------|---|
| Unit | 868 | 868 | 0 |
| Regression | 1364 | 1368 | +4 |
| Smoke | 6 | 6 | 0 (not re-run; CI-verified) |
| DemoSeed | 55 | 55 | 0 (not re-run; CI-verified) |
| Frontend | 663 | 679 | +16 |
| **Total** | **2956** | **2976** | **+20** |

## Step 7a — dual-lens review (against 23605cd)
- **External (Codex):** APPROVED — "Clean — no findings." Traced the deletion fix, the eligibility hoist, the days/hours guards, the two-line grid render, and the tests. (`.claude/reviews/SPRINT-123-step7a-codex.md`)
- **Internal (Reviewer Agent):** APPROVED — 0 BLOCKER / 1 WARNING / 3 NOTE. **W1** (absence-cap 422 branch collided with the `work_time_exceeds_day` 422 — same shape) ABSORBED (re-anchored on the error string + negative test). **N1** (stale `formatCategoryValue` JSDoc) ABSORBED. **N2** (SENIOR_DAY gating test coverage) verified-contained. **N3** (`/summary` not eligibility-gated) DEFERRED → next sprint (pre-existing). Converged cycle 1. (`.claude/reviews/SPRINT-123-step7a-reviewer.md`)
- Complementary-lens: the internal lens caught the self-inflicted 422-branch shape collision the external lens passed — [[review-lens-complementarity]].

## Close notes
- Rolling sprint: 4 tasks landed (TASK-12300/12301/12302/12303), TASK-12304 deferred. Accumulated to ONE close commit per owner.
- FAIL-002 ops: the demo stack (holding :5432) was town down (`down -v`) before the fixed-port regression run; regression ran against a fresh compose Postgres. Demo reseeded post-close for the owner's continued UI testing.
