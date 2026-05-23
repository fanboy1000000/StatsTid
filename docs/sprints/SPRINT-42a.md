# Sprint 42a — ADR-024 2nd Amendment (Cross-Process Boundary + Tx Envelope)

| Field | Value |
|-------|-------|
| **Sprint** | 42a (mirrors S41a precedent — 2nd ADR-024 amendment) |
| **Status** | **in-progress** |
| **Start Date** | 2026-05-23 |
| **Sprint-start commit base** | `6dc9008` (S41a close) |
| **Sprint type** | **DESIGN-ONLY** — 2nd amendment to ADR-024 settling 3 new seams |
| **Plan** | `.claude/plans/PLAN-s42a.md` |
| **Phase** | 4e |

## Sprint Goal

Append `## Amendment 2026-05-23 — Cross-Process Boundary + Tx Envelope (S42a)` section to ADR-024 settling Seam E (rule-engine HTTP boundary doesn't carry merged config), Seam F (tx envelope vs EmitManifestAsync degraded-audit), Seam G (audit-line entry contract location).

## Cycle-trail context

6th sprint slot on ADR-024 work. If S43 refinement cycle 1 finds ANOTHER seam, smoke-alarm discipline calls for rollback of D1+D2 cutover entirely.

## Forward Pointers

- S43 = ADR-024 D1+D2 cutover (re-drafted against doubly-amended ADR)
- S44 = D7+D6+Bug #4
- S45 = D-test matrix + Phase E

---

## Sprint Close

_To be filled by TASK-42A-03._
