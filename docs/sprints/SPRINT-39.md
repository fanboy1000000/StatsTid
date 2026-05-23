# Sprint 39 — Tooling Debt Sprint: Quality Gate Lift

| Field | Value |
|-------|-------|
| **Sprint** | 39 |
| **Status** | **complete** |
| **Start Date** | 2026-05-23 |
| **End Date** | 2026-05-23 |
| **Orchestrator Approved** | yes — 2026-05-23 |
| **Build Verified** | yes — `dotnet build StatsTid.sln -c Release` returns 0 errors + 0 warnings (clean rebuild) |
| **Test Verified** | yes — 869 unchanged (526 unit + 35 plain regression + 218 Docker-gated + 90 frontend vitest); plain regression spot-verified at 35/35 passing |
| **Sprint-start commit base** | `a0e30ed` (governance: ADRs bind to architectural events, not projected sprint numbers, 2026-05-23) |
| **Sprint-end HEAD** | _filled by sprint-close commit_ |
| **Sprint type** | Implementation (tooling debt) — lifts quality gates from `larshansen1/dotnet-template` into StatsTid CI/build posture. No src/ logic changes. |
| **Plan** | `.claude/plans/PLAN-s39.md` |
| **Phase** | 4e (general hardening — pre-launch tooling lift before audit-visibility implementation lands at S40+) |

## Sprint Goal

Close long-deferred Codex Rec #7 (CI expansion: smoke + vitest) plus lift seven quality gates from `larshansen1/dotnet-template`. Phase 1 zero-friction additive gates (gitleaks, global.json, Dependabot, vulnerable-package CI step, docker-compose CI harness, smoke wiring, vitest wiring) ship guaranteed. Phase 2 cleanup-triggering gates (Directory.Build.props + per-project warn-as-error rollout, in-box .NET Analyzers, coverage baseline, lizard CCN report) ship with dry-run-discovered per-project escape hatches. Phase 3 QUALITY.md re-grade + sprint close.

**Codex Rec #9 ("governance drift-check CI step") is NOT in scope** for S39 (Step 0b cycle 1 BLOCKER absorption — earlier framing wrongly claimed closure; Rec #9 needs its own task and is deferred to a follow-up tooling sprint).

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

### Outcome

**Tooling Debt Sprint — Quality Gate Lift completed cleanly.** 13 work tasks landed across 3 phases (Phase 1 additive zero-friction gates: 7 tasks; Phase 2 cleanup-triggering gates: 5 tasks; Phase 3 sprint admin: 2 tasks). All Orchestrator-direct dispatch; no agent dispatch; no worktree parallelism. No same-area thrash recurrence — the governance fix in `a0e30ed` ("ADRs bind to architectural events, not projected sprint numbers") held throughout the sprint.

**Real findings closed pre-merge during execution**:
1. **2 High-severity CVEs** in transitive test dependencies (`System.Net.Http 4.3.0`, `System.Text.RegularExpressions 4.3.0` pulled in by `Microsoft.NET.Test.Sdk 17.8.0` across all 3 test csprojs). Fixed by explicit PackageReference overrides to patched versions (4.3.4 + 4.3.1) at TASK-3904.
2. **1 CA5394 security analyzer false positive** at `ExponentialBackoff.CalculateWithJitter` — jitter doesn't need crypto-secure randomness; suppressed with localized `#pragma` + rationale at TASK-3910.
3. **Docker container healthcheck broken across all 7 .NET services** — surfaced by TASK-3905 design phase: container-side healthchecks invoke `curl` which isn't shipped in `mcr.microsoft.com/dotnet/aspnet:8.0`. CI works around via host-side healthcheck loop. Full Dockerfile fix deferred to Phase 4e (documented in QUALITY.md Docker row + Priority Improvement Areas #5).

### Step 7a Dual-Lens Trail

Both lenses ran against `a0e30ed..HEAD` (13 work commits) at sprint close 2026-05-23.

| Lens | Verdict | Cycles | Artifact |
|------|---------|--------|----------|
| Codex external | APPROVED-WITH-WARNINGS (1 WARNING + 1 NOTE; 0 BLOCKER) | 1 | `.claude/reviews/SPRINT-39-step7a-codex.md` |
| Reviewer Agent internal | APPROVED — clean | 1 | `.claude/reviews/SPRINT-39-step7a-reviewer.md` |

Codex WARNING **absorbed at close**: SPRINT-39.md Sprint Goal section had stale Rec #9 closure claim contradicting PLAN-s39.md's explicit out-of-scope statement; corrected pre-close-commit.

No BLOCKER from either lens. Cycle-cap = 2 per lens per standard discipline; only cycle 1 needed.

### Commit List

13 work commits + sprint-open + sprint-close = 15 commits total:

```
dbabe65 S39 TASK-3900 sprint open
59e2d33 S39 TASK-3901 gitleaks CI step + allowlist
0d1bb02 S39 TASK-3902 global.json SDK pin
cc4efe1 S39 TASK-3903 Dependabot config (4 ecosystems, staggered cron)
24184e5 S39 TASK-3904 vulnerable-package CI step + transitive CVE pins
6e3b396 S39 TASK-3905+3906 docker-compose CI harness + smoke wiring
7ac4694 S39 TASK-3907 vitest into CI
8c28f44 S39 TASK-3908 Directory.Build.props + Pre-S39 warning baseline
7c4df6a S39 TASK-3909 per-project warn-as-error rollout
1e53f8e S39 TASK-3910 .NET Analyzers (in-box) security mode + CA5394 fix
6d6c87e S39 TASK-3911 coverage baseline measurement (baseline-recording only)
aed01ad S39 TASK-3912 lizard CCN report (report-only baseline)
dd99ec0 S39 TASK-3913 QUALITY.md re-grade post-S39
[this commit] S39 TASK-3914 sprint close
```

### Quality Re-grade

Per TASK-3913 (`dd99ec0`):
- **Frontend**: C+ → **B-** ▲ (vitest now CI-enforced; underlying E2E + shared-hook gaps remain)
- **NEW domain CI/Tooling**: **B+** ▲ (new) — 6 quality gates active in CI, Dependabot on 4 ecosystems, 7 of 8 production csprojs gate strict warn-as-error + .NET Analyzers security mode
- **Docker/Infrastructure**: B+ (unchanged grade; tech debt cell updated with container-healthcheck-curl-missing finding)
- **Priority Improvement Areas**: refreshed — Frontend B-, Security B+, Backend A, plus 2 new entries (coverage gating strategy deferred to post-launch; container healthcheck breakage as Phase 4e candidate)

### Architectural Constraints Verified

- [x] P1 — No architecture changes
- [x] P2 — No rule code touched (explicit checklist line per Step 0b cycle 1 BLOCKER #2 absorption)
- [x] P3 — No event/projection changes
- [x] P4 — SDK pin doesn't alter compiled IL
- [x] P5 — No outbox/publisher/consumer changes
- [x] P6 — Vulnerable-package gate now defends payroll code; no payroll logic touched
- [x] P7 — gitleaks + vulnerable-package + .NET Analyzers CA3xxx/CA5xxx active
- [x] P8 — 8 new CI steps + 4 new build-time gates landed; Dependabot active on 4 ecosystems
- [x] P9 — Frontend vitest now CI-enforced; no UX changes

### Forward Pointers

- **Next sprint (S40)** = audit_projection schema migration per `PROGRAM-s36-s41-domain-correctness.md`. ADR-024/025/026 still reference "S39 schema migration" in their text — those references are projections per the `a0e30ed` projection disclaimer; current sprint plan supersedes. **NO doc rename needed.**
- **S41** = ADR-026 cutover (was projected as S40 before re-prio)
- **S42** = audit-visibility D-tests
- **Post-S42 tooling sprint candidate**: coverage gating strategy decision, Phase 2 escape-hatch cleanup (StatsTid.Integrations.Payroll CS0618 if `/calculate-and-export` retires), Dependabot auto-merge policy revisit after first-month PR volume settles, Codex Rec #9 governance drift-check (NOT closed by S39 — explicitly deferred)
- **Phase 4e backlog** updates: Dockerfile curl-install for container-healthcheck restoration (new finding); stale worktree housekeeping (carry-forward from S34/S35).

