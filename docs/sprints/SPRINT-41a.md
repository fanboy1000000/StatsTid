# Sprint 41a — ADR-024 Amendment (Cutover Seams)

| Field | Value |
|-------|-------|
| **Sprint** | 41a (design-only sub-sprint; S38b precedent — preserves S42 for the deferred D1+D2 cutover) |
| **Status** | **in-progress** |
| **Start Date** | 2026-05-23 |
| **End Date** | _filled by close_ |
| **Orchestrator Approved** | _filled by close_ |
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

_To be filled by TASK-41A-03._
