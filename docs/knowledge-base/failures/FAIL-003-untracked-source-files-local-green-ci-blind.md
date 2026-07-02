# FAIL-003 — Untracked Source Files Pass Local Verification but Are Invisible to CI

| Field | Value |
|-------|-------|
| **ID** | FAIL-003 |
| **Category** | failure |
| **Status** | resolved (fix-forward `2571550` + a mechanical close gate) |
| **Sprint** | S111 (detected 2026-07-02, post-close) |
| **Domains** | Test, CI/Tooling |
| **Tags** | git-untracked, false-green, ci-verification, close-protocol, sprint-close-guard |

## Failure

The S111 close commit `35bcdf4` shipped the Typed API Contract Phase 0 and claimed all 4 CI gates, including gate #1 — the PER-ROUTE spec≡runtime gate (`OpenApiSpecRuntimeTests` + `SpecRuntimeMatcher` + `SpecRuntimeMatcherTests`). The three files implementing that gate were never staged: they sat **untracked** in the working tree while the close commit (32 files, nothing under `tests/`) landed, was pushed, and was CI-verified green (`28473854257`).

Every local verification was honestly green — build 0/0, "21 Contracts + 6 matcher" passing — because an SDK-style csproj globs **all** sources on disk, tracked or not. CI meanwhile built only the committed tree: its regression count was **1155, byte-identical to S110's**, the tell that nothing new ran. The sprint log, sprints INDEX, and memory all recorded "the 4 NEW gates all ran in CI + passed" — false for the most important gate. Detected two days later only because a `git status` at session start showed test files as `??`.

This is the project's recurring false-green class one level up: S97→S100 was *a test asserting the wrong reality*; this was *the enforcing artifact never entering the enforced tree*.

## Root cause

Three mechanisms compose:
1. **Local build ≠ committed tree.** `dotnet build`/`dotnet test` (and vitest) operate on the working directory; untracked files participate fully. Local green proves the *disk* state, not the *git* state.
2. **Staging asymmetry.** `git commit -a` and habitual selective `git add` pick up *modifications* to tracked files but never *untracked* files — new test files are exactly the class that gets missed.
3. **No gate compared the two.** The close protocol verified suites locally and CI remotely, but nothing asserted "the tree that passed locally is the tree that was committed."

## Resolution / Mitigation

1. **Fix-forward `2571550` (2026-07-02):** the three reviewed files committed verbatim; CI GREEN `28567810051` with regression 1155→**1167** (+12 = the 6 spec≡runtime + 6 matcher methods — the delta verified against both runs' runner summaries, not assumed).
2. **Mechanical close gate:** `sprint-close-guard.ps1` gained an **UNTRACKED-SOURCE gate** — a sprint-close commit is BLOCKED while `git status --porcelain` reports `??` entries under `src/`, `tests/`, `tools/`, or `frontend/`. Waiver: `.claude/reviews/SPRINT-<N>-untracked-WAIVED.md` (document why the files legitimately stay uncommitted). Fail-open on git/infrastructure errors, per the guard's convention.
3. **Close-protocol rule:** before declaring any commit CI-verified for a *specific artifact*, confirm the artifact is IN the commit (`git show --stat`) — and treat an unchanged CI test count where growth was expected as a red flag, not a coincidence.

## Agent Guidance

- **Orchestrator:** at close, `git status --porcelain` must be part of the evidence; `??` under a source root means the local green is unrepresentative. An expected-to-grow CI test count that matches the previous sprint exactly is the cheap detection signal.
- **Test & QA agents:** when adding test files, report the paths explicitly so the Orchestrator stages them; never assume a green local run implies the files are tracked.
