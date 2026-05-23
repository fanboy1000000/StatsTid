# Sprint 41a — ADR-024 Amendment (Cutover Seams)

| Field | Value |
|-------|-------|
| **Sprint** | 41a (design-only sub-sprint; S38b precedent — preserves S42 for the deferred D1+D2 cutover) |
| **Status** | **complete** |
| **Start Date** | 2026-05-23 |
| **End Date** | 2026-05-23 |
| **Orchestrator Approved** | yes — 2026-05-23 |
| **Build Verified** | N/A — design-only sprint; no code changes |
| **Test Verified** | N/A — test totals unchanged from S40 (874) |
| **Sprint-start commit base** | `dfe5efa` (S40 close, 2026-05-23) |
| **Sprint-end HEAD** | _filled by close commit_ |
| **Sprint type** | **DESIGN-ONLY** — amends ADR-024 settling 4 cutover seams |
| **Plan** | `.claude/plans/PLAN-s41a.md` |
| **Phase** | 4e |

## Sprint Goal

Author a `## Amendment 2026-05-23 — Cutover Seams (S41a)` section at the bottom of `docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md` settling the 4 architectural seams that S41 refinement cycle 1 surfaced as load-bearing BLOCKERs. Implementation refinement re-drafts against the amended ADR at S42 sprint open.

## The 4 Seams (per cycle-1 dual-lens findings)

1. **Seam A** — Rule-engine consumer mis-identified (ADR L69 says OvertimeGovernanceRule; actual is OvertimeRule.cs)
2. **Seam B** — DISCRETIONARY event-emit seam wrong (BuildLine is static/pure; needs PCS upstream)
3. **Seam C** — ConfigResolutionService signature mismatch (no employeeId/date in current code)
4. **Seam D** — employment_category determinism gap (live-read; dated lookup KEY required for replay)

## Step 7a Dual-Lens (TASK-41A-02)

MANDATORY per `sprint-close-guard.ps1` hook. Cycle-cap 2 per lens.

## Forward Pointers

- **S42 = ADR-024 D1+D2 cutover** (was S41 pre-amendment) — re-draft refinement against amended ADR
- **S43 = ADR-024 Sub-Sprint 2b** — D7 + D6 + Bug #4
- **S44 = ADR-024 Sub-Sprint 3** — D-tests + Phase E completion
- **Phase 4e launch-blocking candidate** — employment_category dating (Seam D resolution)

---

## Sprint Close

### Outcome

**ADR-024 amendment ACCEPTED + appended to ADR.** 4-seam settlement landed cleanly with Step 7a cycle 1 absorption (Seam B citation defect + DI gap + DISCRETIONARY asymmetry + sprint sequencing all absorbed inline). S42 implementation refinement can converge against the amended ADR with mechanical task derivation.

**Smoke-alarm response**: this sub-sprint was the user-adjudicated response to 4 cycles of same-area ADR-024 thrash (S38b + S40 cycles 1+2 + S41 cycle 1). Per `feedback_thrash_defer_real_world.md` discipline, the smoke alarm was honored by authoring an ADR amendment rather than running yet another refinement cycle on the same defect topography.

### Step 7a-equivalent Dual-Lens Trail

| Lens | Verdict | Cycles | Artifact |
|------|---------|--------|----------|
| Codex external | APPROVED-WITH-WARNINGS (4 WARNINGs + 2 NOTEs; 0 BLOCKER) | 1 (absorbed) | `.claude/reviews/SPRINT-41a-step7a-codex.md` |
| Reviewer Agent internal | APPROVED-AFTER-ABSORPTION (1 BLOCKER + 3 WARNINGs + NOTEs at cycle 1; all absorbed) | 1 (absorbed) | `.claude/reviews/SPRINT-41a-step7a-reviewer.md` |

Both lenses convergent on Seam B citation defect + PCS DI gap. Cycle 1 absorption (`1027194`) addressed all BLOCKERs + 3 of 4 WARNINGs. Remaining WARNING (Reviewer's Seam D SnapshotContract alternative path) deferred to Phase 4e sprint refinement — discretionary choice between dated-table OR SnapshotContract.

### Commit List

4 commits:
```
349e4b9 S41a TASK-41A-00 sprint open
32b00f3 S41a TASK-41A-01 ADR-024 amendment — Cutover Seams
1027194 S41a TASK-41A-02 absorption: Step 7a cycle 1 fixes
[this commit]  S41a TASK-41A-03 sprint close
```

### Forward Pointers

- **S42** = ADR-024 D1+D2 cutover (re-drafted refinement against amended ADR; ConfigResolutionService 4-layer + role-override merge + PCS dated overload + PCS IOutboxEnqueue extension + admin endpoints + frontend)
- **S43** = ADR-024 D7+D6+Bug #4 (necessity-ack endpoint + Approval UI + HK/PROSA seed flip + D6 generalized correct-as-bug endpoint)
- **S44** = ADR-024 Sub-Sprint 3 (D-test matrix including chefkonsulent marquee + Phase E completion + WORKFLOW.md governance bake-in)
- **Phase 4e launch-blocking candidate** = employment_category dating (Seam D); refinement chooses (a) extend employee_profiles OR (b) new user_employment_categories table OR (c) SnapshotContract path per ADR-016 D5b

### Architectural Constraints Verified

- [x] P1 — ADR amendment cross-references preserved (ADR-001/014/016 D10/018 D3/019 D2 D8/020 D2/023 D1 D3 D8 all referenced consistently)
- [x] P2 — Seam A decision matches OvertimeRule + boolean-disabler-via-merge path; rule code untouched (cutover is S42)
- [x] P3 — Seam B PCS-as-event-emit-seam honors PAT-004 emit-from-orchestration pattern
- [x] P4 — Seam C dated-overload signature preserves S33 ADR-023 D1 + ADR-016 D10 replay determinism contract
- [x] P5 — Seam B IOutboxEnqueue addition preserves ADR-018 D3 atomic-tx contract (acknowledged as S42 work)
- [x] P7 — No security-surface changes (design only)
