# Sprint 58 — Skema per-day footer rows + work-time 24h cap

| Field | Value |
|-------|-------|
| **Sprint** | 58 |
| **Status** | complete |
| **Start Date** | 2026-06-01 |
| **End Date** | 2026-06-01 |
| **Orchestrator Approved** | yes — 2026-06-01 |
| **Build Verified** | yes — `dotnet build` 0 errors |
| **Test Verified** | yes — 552 unit + 130→133 FE + 6 new Docker regression |

> **Note**: S58 was executed as two focused standalone tasks (Small Tasks Exception scale) committed directly to master before the formal sprint-log discipline was applied; this log is reconstructed for governance/CI completeness (doc-consistency gate).

## Sprint Goal
Two small, self-contained Skema improvements: (1) surface the per-day "Diff. fra normtid" and "Ikke fordelt" footer rows on every norm day; (2) cap registered work time at 24h/day (Arbejdstid).

## Task Log

### TASK-5801 — Per-day Diff/Ikke-fordelt on every norm day
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Orchestrator (Small Tasks Exception — frontend-only) |
| **Components** | frontend SkemaGrid |
| **Commit** | `5ea005a` |
**Description**: "Diff. fra normtid" and "Ikke fordelt" rows now render on every day that has a norm (norm>0) — a workday with no registered work surfaces its full −norm shortfall instead of blank; an empty norm day reads as balanced ✓. Blank for academic ANNUAL_ACTIVITY (null norm) and 0-norm/0-work days.
**Validation**: 2 new SkemaGrid vitest tests; FE 128→130.
**Files**: `frontend/src/components/SkemaGrid.tsx`, `frontend/src/components/__tests__/SkemaGrid.test.tsx`.

### TASK-5802 — Per-day work-time 24h cap (Arbejdstid)
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Orchestrator (direct, refine-requirements + dual-lens reviewed) |
| **Components** | Backend SkemaEndpoints, frontend SkemaGrid |
| **Commit** | `818d8d4` |
**Description**: Authoritative per-day work-time guard in the Skema POST (pre-tx, 422): reject `manualHours<0` (`work_time_negative_manual_hours`), overlapping intervals (`work_time_intervals_overlap`), and interval+manual > 24h (`work_time_exceeds_day`). Exactly 24,0 t allowed. Frontend mirrors (negative dropped, dialog error + disabled Gem on overlap/>24, over-24 cell flag). Codex review caught the negative-manual bypass (BLOCKER, fixed).
**Validation**: 6 new Docker regression tests (negative/overlap/manual>24/combined>24/exactly-24/touching) + 3 FE tests. Backend unit 552, FE 130→133, regression Skema/WorkTime green.
**Files**: `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs`, `frontend/src/components/SkemaGrid.tsx` (+`.module.css`, `__tests__`), `tests/StatsTid.Tests.Regression/Outbox/SkemaWorkTimeDayBoundsGuardTests.cs`.

## Test Summary
| Suite | Count | Status |
|-------|-------|--------|
| Unit | 552 | passing |
| Frontend | 133 | passing |
| Regression (new) | 6 | passing (Docker) |

## Sprint Retrospective
**What went well**: refine-requirements + Codex/Reviewer dual-lens caught a real BLOCKER (negative-manual-hours bypass) before commit.
**What to improve**: standalone tasks should still get a sprint-log stub at commit time to avoid the doc-gate gap reconciled here.
**Knowledge produced**: none (no new ADR/PAT).
