# PLAN — Sprint 39: Tooling Debt Sprint — Quality Gate Lift

| Field | Value |
|-------|-------|
| **Sprint** | 39 |
| **Phase** | 4e (general hardening — pre-launch tooling lift before audit-visibility implementation lands at S40+) |
| **Sprint type** | Implementation (tooling debt; no src/ logic changes; no test count changes) |
| **Base commit** | `a0e30ed` (governance: ADRs bind to architectural events, not projected sprint numbers, 2026-05-23) |
| **Refinement** | `.claude/refinements/REFINEMENT-s39-tooling-debt.md` (READY for tooling scope; ADR-amendment portion obviated by `a0e30ed`) |
| **Predecessor plans** | `PLAN-s39-superseded-cycle-trail.md` + `PLAN-s39a-superseded-cycle-trail.md` (cycle-trail artifacts from the 6-cycle thrash that surfaced the governance fix) |
| **Sprint open date** | 2026-05-23 |
| **Task count** | 15 total (TASK-3900..3914): 1 sprint-open + 13 work tasks + 1 sprint-close |
| **Customer-go-live impact** | +1 sprint slip per ROADMAP L25 (audit-visibility implementation moves to the next sprint after S39 closes); no ADR amendment required — ADRs bind to architectural events not sprint numbers per `a0e30ed` |

## Sprint Goal

Close long-deferred Codex Rec #7 (CI expansion: smoke + vitest) plus lift seven quality gates from `larshansen1/dotnet-template` into the StatsTid CI/build posture, **before** audit-visibility implementation lands. Phase 1 zero-friction additive gates (gitleaks, global.json, Dependabot, vulnerable-package CI step, smoke harness + smoke wiring, vitest wiring) ship guaranteed. Phase 2 cleanup-triggering gates (Directory.Build.props + warn-as-error rollout, in-box .NET Analyzers, coverage baseline, lizard CCN report) ship with dry-run-discovered per-project escape hatches. Phase 3 QUALITY.md re-grade + sprint close.

**Codex Rec #9 ("governance drift-check CI step") is NOT in scope** for S39 (Codex Step 0b cycle 1 BLOCKER absorption — earlier framing wrongly claimed closure; Rec #9 needs its own task and is deferred to a follow-up tooling sprint).

**Strategic context**: pre-launch posture means tooling debt is cheaper to close before more audit-visibility code lands. The 6-cycle thrash that preceded this sprint (REFINEMENT cycles 1-4 + PLAN-s39 Step 0b cycle 1 + PLAN-s39a Step 0b cycle 1) surfaced the structural fix — ADRs bind to architectural events not sprint numbers — landed in `a0e30ed`. With the governance fix in place, the sprint scope collapses to just the tooling work.

**Step 0b cycle 1 BLOCKER absorptions** (from the superseded PLAN-s39 + PLAN-s39a):
- **P2 (Deterministic rule engine) added to architectural constraints checklist** — no rule code touched but priority order requires the explicit line
- **All tasks dispatched Orchestrator-direct** — Step 0b flagged Security/Test & QA agents being assigned to CI/repo-root work outside their declared AGENTS.md scopes. Fix: Orchestrator-direct for everything in this sprint (CI YAML + repo-root config + Directory.Build.props are all Orchestrator-only domains)
- **TASK-3905 split** into TASK-3905 (CI docker-compose harness) + TASK-3906 (smoke test wiring) — smoke tests at `tests/StatsTid.Tests.Smoke/SmokeTests.cs:12-25` hardcode `localhost:5100-5700` against the running compose stack, so CI needs `docker compose up -d` (NOT `--wait` — see TASK-3905 NOTE; container-side healthchecks invoke curl which isn't shipped in .NET runtime images) + host-side healthcheck loop + DB seed + smoke run + teardown before the smoke `dotnet test` step
- **TASK-3901 (gitleaks) moved to first position** per Reviewer NOTE #2 — secret-baseline visibility before other config changes ride atop
- **gitleaks task body explicitly enumerates appsettings + .claude/ + tests/Fixtures + init.sql bcrypt hashes** — was abridged in prior plan
- **Worktree count corrected to 77** (was stale 80+)

**Out-of-scope for S39** (deferred):
- Coverage gating strategy (this sprint records baseline only; gating decision deferred)
- Frontend tooling parity beyond vitest-in-CI (no `npm audit`, no frontend warn-as-equivalent — Phase 5 polish backlog)
- Dependabot auto-merge policy (manual review only this sprint)
- StyleCop, jscpd duplicate gate, SecurityCodeScan.VS2019, Makefile, .pre-commit-config.yaml Python framework (explicitly skipped per refinement)
- ADR amendment / sprint-shift rename work (obviated by `a0e30ed`)

## Phase Decomposition

**No worktrees** — all 14 work tasks touch repo-root config files or CI YAML where parallel worktrees would merge-conflict. All tasks sequential. All dispatch is Orchestrator-direct.

| Phase | Tasks | Dispatch model | Rationale |
|-------|-------|---------------|-----------|
| 0 | TASK-3900 (1 task) | Orchestrator-direct | Sprint open plumbing. |
| 1 | TASK-3901..3907 (7 tasks) | **Sequential** | Additive zero-friction gates: gitleaks → global.json → Dependabot → vulnerable-package CI → docker harness → smoke wiring → vitest wiring. No src/ changes. |
| 2 | TASK-3908..3912 (5 tasks) | **Sequential** | Cleanup-triggering gates: Directory.Build.props + dry-run baseline → per-project warn-as-error rollout → .NET Analyzers in-box → coverage baseline → lizard CCN. Per-project escape hatches structured in Directory.Build.props. |
| 3 | TASK-3913..3914 (2 tasks) | **Sequential** | QUALITY.md re-grade + sprint close. |

Total: 15 tasks (1 + 7 + 5 + 2).

## Step 0a — Entropy Scan Findings

Run 2026-05-23 at sprint open:

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | No KB writes this sprint. Governance commit `a0e30ed` already landed the ADR projection-disclaimer pattern. |
| Pattern compliance | CLEAN | This sprint adds no new code patterns; lifts established external patterns (in-box .NET Analyzers, secret scanning, vulnerable-package check, dependency automation) into existing CI. |
| Orphan detection | DEBT (carry-forward from S34/S35) | 77 stale locked agent worktrees under `.claude/worktrees/`; S39 uses no worktrees so non-blocking. Operational housekeeping deferred to Phase 4e backlog. |
| Documentation drift | DRIFT-IDENTIFIED | `docs/QUALITY.md` "Last updated: Sprint 35 (2026-05-20)" stale by 4 sprints (S36 / S37 / S38 / S38b all closed). TASK-3913 absorbs. |
| Quality grade review | SCHEDULED | Re-grade at TASK-3913 close. Frontend C+ → B- candidate (vitest now CI-enforced — closes long-standing testing-as-only-build-check gap). CI/Tooling new category — partial credit for additive gates landing this sprint; full grading deferred to post-launch when gating strategy decided. |
| Refinement disposition | READY | 4-cycle Step 4 dual-lens absorbed; ADR-amendment portion obviated by `a0e30ed`; tooling scope clean. |
| Cycle-trail recovery | NOTED | The 6-cycle thrash preceding this sprint (REFINEMENT-s39-tooling-debt.md cycles 1-4 + 2 superseded plans) is itself the artifact that produced the governance fix in `a0e30ed`. Cycle trail preserved at `.claude/plans/PLAN-s39-superseded-cycle-trail.md` + `PLAN-s39a-superseded-cycle-trail.md` for future-sprint reference. |

## Step 0b — Plan Review Trigger

**MANDATORY** per trigger criteria — sprint touches:

- **P7 (Security / access control)**: gitleaks (TASK-3901) + vulnerable-package CI step (TASK-3904) + in-box .NET Analyzers CA3xxx/CA5xxx (TASK-3910) — three new security-posture gates
- **P8 (CI/CD enforcement)**: every task in scope is P8 by definition
- **NEW: First docker-compose CI harness landing** in the project's CI (TASK-3905) — non-trivial new pattern

**No same-area-thrash risk this time** — the wide-surface ADR-amendment surface that triggered the 6-cycle thrash is removed from scope. Step 0b cycle-cap = 2 per lens per standard discipline (no extended cycle-3 verification needed).

Dispatch dual-lens (Codex external + Reviewer Agent internal) on this PLAN file before Phase 1 dispatches.

---

## Architectural Constraints

_Checked at sprint close (TASK-3914)._

- [ ] **P1 — Architectural integrity** → No architecture changes; CI gates + build infrastructure only
- [ ] **P2 — Deterministic rule engine** → No rule code touched (BLOCKER absorbed: explicit P2 line per Codex Step 0b cycle 1 BLOCKER #2)
- [ ] **P3 — Event sourcing / auditability** → No event/projection changes
- [ ] **P4 — Version correctness** → No version handling changes; SDK pin in global.json (TASK-3902) doesn't alter compiled IL
- [ ] **P5 — Integration isolation / delivery guarantees** → No outbox/publisher/consumer changes
- [ ] **P6 — Payroll integration correctness** → No payroll code changes; vulnerable-package check (TASK-3904) may surface CVEs in Npgsql or transitive deps — High+Critical force upgrade, Medium logged for follow-up
- [ ] **P7 — Security / access control** → gitleaks allowlist covers dev bcrypt + appsettings + .claude/ + tests/Fixtures; vulnerable-package gate adds CVE defence-in-depth; .NET Analyzers CA3xxx/CA5xxx catch injection / weak-crypto / hardcoded-creds patterns going forward
- [ ] **P8 — CI/CD enforcement** → 8 new CI steps + 4 new build-time gates landed (or escape-hatch documented); Dependabot active across 4 ecosystems
- [ ] **P9 — Usability / UX** → No UX changes; frontend vitest run added to CI is non-user-visible

---

## Task Log

### Phase 0 — Sprint Open

#### TASK-3900 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3900 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s39.md` (this file), `docs/sprints/SPRINT-39.md`, `docs/sprints/INDEX.md` provisional entry |

**Validation Criteria**:
- [ ] PLAN-s39.md filed with full task log + Step 0a + Step 0b sections
- [ ] SPRINT-39.md initial sprint-doc filed with provisional task slots
- [ ] INDEX.md provisional Sprint 39 entry added
- [ ] Sprint-open commit through hook

---

### Phase 1 — Additive Zero-Friction Gates (TASK-3901..3907)

7 tasks sequential. No src/ changes; all touch repo-root config files or `.github/workflows/ci.yml`.

#### TASK-3901 — gitleaks CI step + allowlist (runs FIRST for secret-baseline visibility)

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.gitleaks.toml` (new), `.github/workflows/ci.yml` (gitleaks step added) |

**Pre-task audit**: `grep -ri "password\|secret\|api[_-]key\|token" .claude/ src/**/appsettings*.json tests/**/Fixtures/ docker/postgres/init.sql 2>/dev/null` to size the allowlist before locking it.

**Known allowlist entries**:
- bcrypt dev hashes at `docker/postgres/init.sql:831-837` (regex matches `$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te`)
- `.claude/` artifact tree (review transcripts may quote secret-shaped strings from absorption commits — broad path exclusion since the content is volatile)
- any `appsettings.Development.json`-shaped files (JWT dev secrets per memory of S18 dev JWT fallback)
- password-shaped strings in `tests/**/Fixtures/` (audit at task time; specific patterns added as discovered)

**Validation**:
- [ ] `gitleaks detect --config .gitleaks.toml` returns 0 findings on master HEAD
- [ ] CI step runs on push + PR
- [ ] Allowlist documented in `.gitleaks.toml` with rationale for each entry

#### TASK-3902 — global.json SDK pin

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `global.json` (new) |

Content: `{"sdk": {"version": "8.0.0", "rollForward": "latestFeature", "allowPrerelease": false}}`. Actual minor version pinned to whatever the CI runner currently ships; `latestFeature` allows minor upgrades within 8.0.x.

**Validation**:
- [ ] `dotnet --version` in CI returns 8.0.x
- [ ] `dotnet build StatsTid.sln` succeeds
- [ ] All existing test suites pass unchanged

#### TASK-3903 — Dependabot config

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.github/dependabot.yml` (new) |

4 ecosystems: `nuget`, `npm` (scoped to `frontend/`), `github-actions`, `docker` (scoped to `docker/docker-compose.yml` + 3 dockerfiles). **Staggered cron** to avoid initial PR firehose: nuget Mondays; npm + docker + github-actions Thursdays. Manual review only this sprint.

**Validation**:
- [ ] `.github/dependabot.yml` parses (GitHub Dependabot UI shows 4 ecosystems active after merge)
- [ ] First weekly cron fires after merge
- [ ] No auto-merge enabled

#### TASK-3904 — `dotnet list package --vulnerable` CI step

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.github/workflows/ci.yml` (vulnerable-package step added) |

CI step:
```yaml
- name: Check for vulnerable packages
  run: |
    dotnet list package --vulnerable --include-transitive 2>&1 | tee vulnerable.log
    if grep -E '> (High|Critical)' vulnerable.log; then
      echo "::error::High or Critical severity CVE found"; exit 1
    fi
```

**Validation**:
- [ ] Step runs on push + PR
- [ ] Passes on master HEAD with no High/Critical CVEs (Medium logged but not failing)
- [ ] Emergency override pattern documented as last-resort: bump the vulnerable package via Dependabot PR, OR if no upgrade exists, add an explicit `# TODO: track CVE-YYYY-NNNN` skip in the CI grep predicate with required commit-message rationale and a follow-up task to revisit. (`--ignore-vulnerable` is NOT a valid `dotnet list package --vulnerable` flag — Codex Step 0b cycle 1 WARNING absorbed.)

#### TASK-3905 — CI docker-compose harness step (NEW — absorbed from Step 0b BLOCKER)

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.github/workflows/ci.yml` (new `smoke-tests` job with docker-compose lifecycle) |

**Why this task exists**: `tests/StatsTid.Tests.Smoke/SmokeTests.cs:12-25` hardcodes `http://localhost:5100-5700` against running compose services. Current CI runs `dotnet test` against StatsTid.Tests.Unit + StatsTid.Tests.Regression only — no service orchestration. To wire smoke tests (TASK-3906 below + Codex Rec #7 closure), CI needs a job that brings up the 8-service compose stack, waits for healthchecks, runs the smoke binary, and tears down cleanly.

**CI job shape**:
```yaml
smoke-tests:
  runs-on: ubuntu-latest
  needs: build-and-test
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-dotnet@v4
      with: { dotnet-version: '8.0.x' }
    - name: Bring up docker compose stack
      working-directory: docker
      # NOTE: --wait OMITTED intentionally. Container healthchecks at docker-compose.yml:43-162
      # all invoke `curl` inside containers built from mcr.microsoft.com/dotnet/aspnet:8.0,
      # which does NOT ship curl. Compose healthchecks therefore report unhealthy permanently
      # even though the apps respond fine from the host. --wait would block until per-service
      # start_period + (interval × retries) budget expires, then fail. The host-side loop below
      # is the actual wait gate. (Codex + Reviewer convergent Step 0b cycle 1 BLOCKER absorbed.)
      # Fixing the container-side healthchecks by adding curl install to the 7 Dockerfiles is
      # a separate Phase 4e candidate; explicitly out of scope for S39.
      run: docker compose up -d --build
    - name: Wait for service health (host-side loop — compose healthchecks are broken; see above)
      run: |
        for port in 5100 5200 5300 5400 5500 5600 5700; do
          timeout 60 bash -c "until curl -sf http://localhost:$port/health > /dev/null; do sleep 2; done"
        done
    - name: Run smoke tests
      run: dotnet test tests/StatsTid.Tests.Smoke --logger "trx"
    - name: Capture container logs on failure
      if: failure()
      working-directory: docker
      run: docker compose logs > ../smoke-logs.txt
    - name: Upload logs artifact
      if: failure()
      uses: actions/upload-artifact@v4
      with: { name: smoke-failure-logs, path: smoke-logs.txt }
    - name: Tear down
      if: always()
      working-directory: docker
      run: docker compose down -v
```

**Validation**:
- [ ] `smoke-tests` CI job defined in ci.yml
- [ ] Docker compose stack brings up cleanly in CI: postgres healthcheck passes (`pg_isready` is shipped in postgres image, works correctly); the 7 .NET app services ignore their compose-level healthchecks (broken — curl not shipped in .NET runtime images, see TASK-3905 NOTE) and the host-side `curl` loop verifies all 7 HTTP `/health` endpoints respond 200 from the runner
- [ ] Teardown is `always()`-conditional so container cleanup runs even on test failure
- [ ] Failure path captures container logs as artifact

#### TASK-3906 — Smoke tests into CI (consumes TASK-3905 harness)

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.github/workflows/ci.yml` (smoke-tests job — `dotnet test` step) |

Already covered by TASK-3905 CI job shape — this task is a hold-point to verify smoke tests actually pass in CI (no flakiness, no environmental drift). Closes half of long-deferred Codex Rec #7.

**Validation**:
- [ ] `dotnet test tests/StatsTid.Tests.Smoke` runs in CI inside the TASK-3905 harness
- [ ] Passes on master HEAD
- [ ] No flakiness over 3 consecutive CI runs

#### TASK-3907 — Wire frontend vitest into CI

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.github/workflows/ci.yml` (frontend-build job — new step after `npm run build`) |

Closes the other half of Codex Rec #7. Test count baseline locked at 90 per S38b close.

**Validation**:
- [ ] `npm run test` runs 90 vitest tests in CI
- [ ] Passes on master HEAD
- [ ] Test output captured (no flakiness)

---

### Phase 2 — Code-Cleanup-Triggering Gates (TASK-3908..3912)

5 tasks sequential. Touches build infrastructure + per-project csprojs.

#### TASK-3908 — `Directory.Build.props` introduction + dry-run baseline

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `Directory.Build.props` (new at repo root); `tests/Directory.Build.props` (new — overrides warn-as-error to false); `tools/Directory.Build.props` (new); `docker/mock-payroll/Directory.Build.props` + `docker/mock-external/Directory.Build.props` (new each) |

**First sub-step (T-3908.1)**: dry-run with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` temporarily set at the root level, run `dotnet build StatsTid.sln 2>&1 | tee warn-baseline.log`, count warnings per project. Record table in `docs/QUALITY.md` "Pre-S39 Warning Baseline" section.

**Second sub-step (T-3908.2)**: revert temp setting; commit Directory.Build.props with `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` default + subdirectory props files for tests/tools/mocks exclusion.

**Threshold for per-project warn-as-error opt-out** (set after T-3908.1 baseline): whatever value clears ≥5 of 8 production csprojs.

**Validation**:
- [ ] All 14 csprojs build clean with new props
- [ ] Dry-run warning baseline recorded in QUALITY.md "Pre-S39 Warning Baseline" section
- [ ] Per-project warning counts captured

#### TASK-3909 — Per-project warn-as-error rollout

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct (one-line csproj edits across 8 csprojs) |
| **Components** | 8 production csprojs: Auth, Backend.Api, Infrastructure, Integrations.External, Integrations.Payroll, Orchestrator, RuleEngine.Api, SharedKernel |

**Escape hatch shape**: for each opt-out project, add to `Directory.Build.props`:
```xml
<PropertyGroup Condition="'$(MSBuildProjectName)' == 'StatsTid.Infrastructure'">
  <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  <!-- Baseline: N warnings as of S39 T-3908.1 dry-run; cleanup deferred to follow-up tooling sprint -->
</PropertyGroup>
```

**Validation**:
- [ ] At least 5 of 8 production csprojs successfully gated with `TreatWarningsAsErrors=true`
- [ ] Escape-hatch projects keep their pre-T-3908.1 baseline warning count or lower
- [ ] Per-project decision + baseline recorded in `Directory.Build.props` structured comment block

#### TASK-3910 — .NET Analyzers (in-box) enablement

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `Directory.Build.props` (add `<EnableNETAnalyzers>true</EnableNETAnalyzers>` + `<AnalysisMode>AllEnabledByDefault</AnalysisMode>`) |

**SecurityCodeScan.VS2019 NOT used** per Reviewer cycle-1 WARNING #4 (last shipped 2022, unmaintained on .NET 8 / Roslyn ≥4.x). In-box `Microsoft.CodeAnalysis.NetAnalyzers` ships with .NET 8 SDK and covers CA3xxx (injection) + CA5xxx (cryptography). Same per-project escape-hatch policy as T-3909.

**Validation**:
- [ ] All 8 production csprojs build with .NET Analyzers active
- [ ] At least 5 of 8 successfully gated with strict CA3xxx + CA5xxx mode
- [ ] Per-project `<NoWarn>CAxxxx</NoWarn>` allowlists captured with rationale

#### TASK-3911 — Coverage baseline measurement

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `coverlet.runsettings` (new), `.github/workflows/ci.yml` (coverage step added), `docs/QUALITY.md` (new "Coverage Baseline" section) |

**No `≥80%` gate this sprint** — strategy decision deferred to a follow-up sprint after audit-visibility lands. Baseline-recording mode only.

**Validation**:
- [ ] `dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings` runs in CI
- [ ] Cobertura XML uploaded as artifact
- [ ] Per-assembly coverage baseline table in QUALITY.md

#### TASK-3912 — lizard CCN report

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.github/workflows/ci.yml` (lizard step + `actions/setup-python@v5` prerequisite) |

Report-only this sprint. Threshold 15 logged as warning, NOT failing. Report uploaded as artifact. Output reviewed at sprint close to inform whether to enable as a gate in a later sprint and whether the Rule Engine needs CCN exemptions for pure-function switch statements over enums (per ADR-002 RuleEngine purity).

**Validation**:
- [ ] lizard runs in CI on src/**/*.cs
- [ ] Threshold 15 logged as warning, NOT failing
- [ ] Report uploaded as artifact

---

### Phase 3 — Sprint Admin (TASK-3913..3914)

#### TASK-3913 — QUALITY.md re-grade

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct (docs/QUALITY.md is Orchestrator-only) |
| **Components** | `docs/QUALITY.md` |

**Operation**:
- Update "Last updated" header from "Sprint 35 (2026-05-20)" to "Sprint 39 (2026-05-23)"
- Resolve any stale Priority Improvement Areas rows (S36 / S37 / S38 / S38b deltas)
- Confirm "Coverage Baseline" section from T-3911 is in place
- Confirm "Pre-S39 Warning Baseline" section from T-3908 is in place
- Add new row or column for CI/Tooling grade — partial credit for additive gates landing this sprint
- Frontend grade reassessed against vitest-in-CI delta (C+ → B- candidate)

**Validation**:
- [ ] "Last updated" reflects Sprint 39 (2026-05-23)
- [ ] Stale rows resolved
- [ ] Two new baseline sections present
- [ ] CI/Tooling row added
- [ ] Frontend grade updated

#### TASK-3914 — Sprint close

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/sprints/SPRINT-39.md` (close sections), `docs/sprints/INDEX.md`, `MEMORY.md` entry |

**Operation**:
- Step 7a dual-lens (Codex + Reviewer Agent) on full S39 diff vs `a0e30ed`
- Review focus: did Phase 2 escape-hatches stay within the >5-of-8 acceptance criteria? Did the docker-compose CI harness (TASK-3905) land cleanly?
- Cycle-cap = 2 per lens per standard discipline
- All 14 prior tasks (TASK-3900..3913) marked complete
- Sprint-end HEAD commit hash backfilled
- ROADMAP S39 entry status flipped to "complete"
- INDEX.md Sprint 39 entry filled
- MEMORY.md entry with sprint summary

**Validation**:
- [ ] Step 7a Codex artifact at `.claude/reviews/SPRINT-39-step7a-codex.md` with verdict line
- [ ] Step 7a Reviewer artifact at `.claude/reviews/SPRINT-39-step7a-reviewer.md` with verdict line
- [ ] Both verdicts CLEAN or APPROVED-WITH-NOTES (no BLOCKER)
- [ ] Sprint-close-guard hook passes
- [ ] All architectural constraints P1-P9 checked off

---

## Forward Pointers

- **S40** = audit_projection schema migration per `PROGRAM-s36-s41-domain-correctness.md`. ADRs 024/025/026 reference S39 as the projected sprint slot; those are projections at time of authoring per the `a0e30ed` projection disclaimer — current sprint plan supersedes. No ADR or PROGRAM rename needed.
- **S41** = ADR-026 cutover (was projected as S40 before re-prio)
- **S42** = audit-visibility D-tests (was projected as S41)
- **Post-S42 tooling sprint candidate**: coverage gating strategy decision, Phase 2 escape-hatch cleanup for projects that opted out at T-3909/3910, Dependabot auto-merge policy revisit after first month of PR volume
- **Phase 4e backlog**: stale worktree housekeeping (carry-forward from S34/S35); frontend tooling parity sweep (`npm audit`, warn-as-equivalent — Phase 5 polish)

---

## Cycle Trail Note

This sprint's planning was preceded by a 6-cycle thrash on the originally-bundled scope (4 refinement cycles + PLAN-s39 Step 0b + PLAN-s39a Step 0b). The root cause — ADRs binding to projected sprint numbers — was diagnosed by the user 2026-05-23 with *"is all this back and forth because we can't number the sprints correctly?"* The governance fix landed as commit `a0e30ed` ("ADRs bind to architectural events, not projected sprint numbers"). With the structural fix in place, this sprint's tooling scope is clean.

Superseded plans preserved at:
- `.claude/plans/PLAN-s39-superseded-cycle-trail.md` (original bundled plan)
- `.claude/plans/PLAN-s39a-superseded-cycle-trail.md` (post-Step-0b-cycle-1 split attempt)

Feedback memory: `feedback_adrs_bind_to_events_not_sprints.md`
