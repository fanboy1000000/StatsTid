# Sprint 38b — ADR-026 Authorship (Audit Visibility Surface — Path C Event-Projection)

| Field | Value |
|-------|-------|
| **Sprint** | 38b |
| **Status** | in-progress |
| **Start Date** | 2026-05-21 |
| **End Date** | _pending_ |
| **Orchestrator Approved** | _pending TASK-38B-03_ |
| **Build Verified** | N/A — design-only sprint; no code changes |
| **Test Verified** | N/A — test totals unchanged from S38 (869 total) |
| **Sprint-start commit base** | `92cfbd6` (S38 close polish, 2026-05-21) |
| **Sprint-end HEAD** | _filled by close commit_ |
| **Sprint type** | **DESIGN-ONLY** — produces ADR-026 settling the S38 D7 deferral. Path (C) event-projection chosen per user adjudication. |
| **Plan** | `.claude/plans/PLAN-s38b.md` |
| **Phase** | 4e (Phase C continuation) |

## Sprint Goal

Author `docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md` from PLANNED placeholder to ACCEPTED design. Path (C) event-projection chosen — leverage ADR-018 D13 sync-in-tx pattern; new `audit_projection` table with explicit `target_org_id` columns; per-event projection declarations; event log stays immutable per ADR-001.

## Why Path C (vs A/B/D)

- **(A) scope-by-actor** rejected — operator/system actions invisible to tenant; explicit launch concern per cycle-3 review trail
- **(B) audit_log schema extension** rejected — middleware retrofit touching every state-changing endpoint with per-endpoint target_org_id declarations; out of proportion for the result
- **(C) event-projection per ADR-018 D13** ← CHOSEN — aligns with S27 ProjectionBackfillService established posture; per-event explicit declarations cleaner than per-endpoint middleware; preserves event-log immutability per ADR-001
- **(D) hybrid** rejected — unnecessary complexity; C alone sufficient

## Step 7a Dual-Lens (TASK-38B-02)

**MANDATORY per `sprint-close-guard.ps1` hook**. Codex + Reviewer Agent in parallel against S38b diff. Cycle-cap 2 per lens.

Review focus per S38 lessons: does the path (C) design substantively close the cycle-1/cycle-2/cycle-3 concerns from the prior D7 attempt, or does it surface new defects in the same architectural area? If the latter — the audit-visibility surface is wider than even ADR-026 can carry and needs further decomposition.

## Test Summary

`sprint-test-validation` SKIP — design-only contract; test totals unchanged at 869.

## Forward Pointers

- **S39 schema migration** — adds `audit_projection` table + indexes to the 6 ADR-024+ADR-025 schema entries; ADR-026 backfill seeder runs as part of greenfield migration
- **S40 cutover** — implements `GET /api/admin/audit` endpoint + `AuditProjectionRepository` + `AuditLogView.tsx` admin UI + per-event projection mappings for ADR-024's 7 events + ADR-025's 4 events + select pre-existing audit-relevant events
- **S41 D-tests** — cross-tenant audit leakage + projection backfill idempotency + event-coverage Phase E test

---

_Updated at sprint close (TASK-38B-03): outcomes summary, commit list, sprint duration, MEMORY.md entry._
