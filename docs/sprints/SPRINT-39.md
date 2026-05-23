# Sprint 39 — Tooling Debt Sprint: Quality Gate Lift

| Field | Value |
|-------|-------|
| **Sprint** | 39 |
| **Status** | **in-progress** |
| **Start Date** | 2026-05-23 |
| **End Date** | _filled by close_ |
| **Orchestrator Approved** | _filled by close_ |
| **Build Verified** | _filled by close_ |
| **Test Verified** | _filled by close (target: 869 unchanged — tooling sprint should not change test counts)_ |
| **Sprint-start commit base** | `a0e30ed` (governance: ADRs bind to architectural events, not projected sprint numbers, 2026-05-23) |
| **Sprint-end HEAD** | _filled by close_ |
| **Sprint type** | Implementation (tooling debt) — lifts quality gates from `larshansen1/dotnet-template` into StatsTid CI/build posture. No src/ logic changes. |
| **Plan** | `.claude/plans/PLAN-s39.md` |
| **Phase** | 4e (general hardening — pre-launch tooling lift before audit-visibility implementation lands at S40+) |

## Sprint Goal

Close long-deferred Codex Rec #7 (CI expansion: smoke + vitest) + Rec #9 (governance drift-check CI step) gaps plus lift seven quality gates from `larshansen1/dotnet-template`. Phase 1 zero-friction additive gates (gitleaks, global.json, Dependabot, vulnerable-package CI step, docker-compose CI harness, smoke wiring, vitest wiring) ship guaranteed. Phase 2 cleanup-triggering gates (Directory.Build.props + per-project warn-as-error rollout, in-box .NET Analyzers, coverage baseline, lizard CCN report) ship with dry-run-discovered per-project escape hatches. Phase 3 QUALITY.md re-grade + sprint close.

## Cycle-Trail Context

This sprint's planning was preceded by a **6-cycle thrash** on the originally-bundled scope (4 refinement cycles + 2 plan-review cycles across PLAN-s39 + PLAN-s39a). Root cause diagnosed by the user 2026-05-23: ADRs were authored with sprint-number-shaped bindings ("binding for S39 schema migration", "cannot defer past S39"). Sprint numbers shift on every Tier-2 re-prio; binding architectural docs to them creates cascade-rename surface for zero engineering value.

**Governance fix landed pre-sprint** as commit `a0e30ed` ("ADRs bind to architectural events, not projected sprint numbers"): new `docs/WORKFLOW.md` § "Binding to Architectural Events, Not Sprint Numbers" rule + pre-rule projection disclaimer added to ADR-024 / ADR-025 / ADR-026. With the structural fix in place, this sprint's tooling scope is clean.

Superseded plans preserved at `.claude/plans/PLAN-s39-superseded-cycle-trail.md` + `PLAN-s39a-superseded-cycle-trail.md`. Feedback memory: `feedback_adrs_bind_to_events_not_sprints.md`.

## Phase Decomposition

| Phase | Tasks | Dispatch |
|-------|-------|----------|
| 0 | TASK-3900 | Orchestrator-direct sprint open |
| 1 | TASK-3901..3907 | Sequential additive zero-friction gates (gitleaks first per Reviewer NOTE #2 — secret-baseline visibility) |
| 2 | TASK-3908..3912 | Sequential cleanup-triggering gates (dry-run baseline first; per-project escape hatches) |
| 3 | TASK-3913..3914 | QUALITY.md re-grade + sprint close |

All tasks Orchestrator-direct (Step 0b cycle 1 absorption — CI YAML + repo-root config + Directory.Build.props are all Orchestrator-only domains; no agent dispatch outside declared scopes per AGENTS.md).

## Step 7a Dual-Lens (TASK-3914 at close)

**MANDATORY per `sprint-close-guard.ps1` hook**. Codex + Reviewer Agent in parallel against S39 diff vs `a0e30ed`. Cycle-cap 2 per lens. Review focus:

- Did Phase 2 escape-hatches stay within the >5-of-8 acceptance criteria?
- Did the docker-compose CI harness (TASK-3905) land cleanly without flakiness?
- Did gitleaks allowlist scope (TASK-3901) avoid silently masking legitimate secrets?
- Coverage baseline (TASK-3911) recorded; no premature gating

## Test Summary

`sprint-test-validation` at close. Target: 869 unchanged (526 unit + 35 plain regression + 218 Docker-gated + 90 frontend). Tooling sprint should not change test counts; if smoke tests find runtime regressions in CI, those are pre-existing — surface but don't fix in S39.

## Forward Pointers

- **S40** = audit_projection schema migration per `PROGRAM-s36-s41-domain-correctness.md`. ADRs 024/025/026 reference S39 as projected slot; that's a projection per `a0e30ed` disclaimer — current sprint plan supersedes. No ADR or PROGRAM rename needed.
- **S41** = ADR-026 cutover
- **S42** = audit-visibility D-tests
- **Post-S42 tooling sprint candidate**: coverage gating strategy, Phase 2 escape-hatch cleanup, Dependabot auto-merge policy revisit
- **Phase 4e backlog**: stale worktree housekeeping (carry-forward); frontend tooling parity sweep (Phase 5 polish)

---

## Sprint Close

_To be filled by TASK-3914._

### Outcome

_TBD._

### Step 7a Dual-Lens Trail

_TBD._

### Commit List

_TBD._

### Quality Re-grade

_TBD per TASK-3913._
