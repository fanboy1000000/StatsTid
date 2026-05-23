# PLAN — Sprint 39: Tooling Debt Sprint — Quality Gate Lift

> **STATUS — SUPERSEDED 2026-05-23 by PLAN-s39a + PLAN-s39b**
>
> Step 0b cycle 1 dual-lens review surfaced 3 same-area BLOCKERs (refinement cycles 1-5 trail of ADR-amendment scope / find-and-replace mechanics / sprint-shift binding completeness). Plan's own escalation criterion at L64 fired. User decision 2026-05-23: split per `feedback_thrash_defer_real_world.md` discipline.
>
> - **PLAN-s39a.md** = ADR-024+025+026 amendment + PROGRAM rename + ROADMAP renumber (design-only-style, ~5 tasks, mirrors S38b shape)
> - **PLAN-s39b.md** = tooling gates (Phase 1 additive + Phase 2 cleanup-triggering + QUALITY re-grade — same content as Phases 1+2 of this superseded plan, scope-narrowed by removal of ADR-amendment work)
>
> **This file kept for cycle-trail audit**. Step 0b cycle 1 findings + escalation outcome recorded at bottom.

---


| Field | Value |
|-------|-------|
| **Sprint** | 39 |
| **Phase** | 4e (general hardening — pre-launch tooling lift before audit-visibility implementation lands) |
| **Sprint type** | Implementation (tooling debt + 3-ADR amendment) |
| **Base commit** | `d5c6a87` (S38b post-close governance hook extension, 2026-05-23) |
| **Refinement** | `.claude/refinements/REFINEMENT-s39-tooling-debt.md` (READY post-cycle-4 absorption — 4 BLOCKERs across 4 review cycles: cycle 1 ADR-026 binding, cycle 2 ADR-024/025 + reference-doc scope expansion, cycle 3 line-enumeration over-specification → simplified to find-and-replace, cycle 4 verification-grep contradiction → filter-expression fix; same-area smoke-alarm pattern resolved per user-chosen pragmatic absorption at cycle 4) |
| **Sprint open date** | 2026-05-23 (projected) |
| **Task count** | 17 (TASK-3900..3916) |
| **Customer-go-live impact** | +1 sprint slip per ROADMAP L25, formalised by TASK-3912 ADR-026 amendment ("cannot defer past S39" → "cannot defer past customer-go-live") |

## Sprint Goal

Close long-deferred Codex Rec #7 (CI expansion: smoke + vitest) + Rec #9 (governance drift-check CI step) gaps plus lift seven quality gates from `larshansen1/dotnet-template` into the StatsTid CI/build posture, **before** audit-visibility implementation lands at S40-S42. Phase 1 zero-friction additive gates (global.json, Dependabot, gitleaks, vulnerable-package CI step, smoke + vitest wiring) ship guaranteed. Phase 2 cleanup-triggering gates (warn-as-error rollout, in-box .NET Analyzers, coverage baseline, lizard CCN report) ship with dry-run-discovered per-project escape hatches so noisy projects don't slip the sprint. Phase 3 architectural amendments (ADR-024 + ADR-025 + ADR-026 sprint-binding shifts via programmatic find-and-replace, PROGRAM rename, reference-doc sprint-shift, QUALITY.md re-grade, sprint close).

**Strategic context**: pre-launch posture means tooling debt is cheaper to close before more audit-visibility code lands. User decision 2026-05-23 (refinement cycle 1) authorised the +1-sprint slide of audit-visibility implementation; ADR-026 amendment at TASK-3912 makes the slide architecturally compliant.

**Out-of-scope for S39** (deferred to follow-up sprints):
- Coverage gating strategy (this sprint records baseline only; gating-vs-no-regression decision deferred to post-S42)
- Frontend tooling parity beyond vitest-in-CI (no `npm audit` gate, no frontend warn-as-equivalent — frontend grade may move on vitest CI but full frontend tooling sweep is Phase 5 polish backlog)
- Dependabot auto-merge policy (manual review only this sprint; auto-merge rule to be decided after first month of PR volume)
- StyleCop, jscpd duplicate gate, SecurityCodeScan.VS2019, Makefile, .pre-commit-config.yaml Python framework (explicitly skipped per refinement scope)

## Phase Decomposition

**No worktrees** — all 16 work tasks touch repo-root config files or do cross-doc sprint-number shifts where parallel worktrees would merge-conflict (per S24/S27 worktree-base-mismatch history). All tasks sequential.

| Phase | Tasks | Dispatch model | Rationale |
|-------|-------|---------------|-----------|
| 0 | TASK-3900 | Orchestrator-direct | Sprint open plumbing (PLAN + SPRINT doc + INDEX). |
| 1 | TASK-3901..3906 | **Sequential** | Additive zero-friction gates touching repo-root config + CI YAML. No src/ changes. T-3901 (global.json) → T-3902 (dependabot) → T-3903 (gitleaks + .gitleaks.toml) → T-3904 (vulnerable-package CI step) → T-3905 (smoke into CI) → T-3906 (vitest into CI). |
| 2 | TASK-3907..3911 | **Sequential** | Cleanup-triggering gates. T-3907 first sub-step is the dry-run that determines per-project threshold; T-3908 + T-3909 + T-3910 + T-3911 land after the baseline is recorded. Per-project escape hatches structured in Directory.Build.props (Reviewer NOTE #1). |
| 3 | TASK-3912..3916 | **Sequential** | Sprint admin + ADR amendment. T-3912 (3-ADR amendment via find-and-replace) → T-3913 (ROADMAP renumber) → T-3914 (PROGRAM rename + reference-doc shift) → T-3915 (QUALITY.md re-grade) → T-3916 (sprint close). T-3912 requires interim Reviewer Agent invocation on the commit diff before merge (refinement TASK-3912 verification step). |

## Step 0a — Entropy Scan Findings

Run 2026-05-23 at sprint open:

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-024 + ADR-025 + ADR-026 paths resolve cleanly post-S38b close; TASK-3912 amendment is in-scope edit. ADR-001 (event immutability), ADR-002 (RuleEngine purity), ADR-014 (DB-backed configs), ADR-017 (local config), ADR-018 (outbox + D7 row-version + D13 projection pattern), ADR-019 (admin-strict If-Match), ADR-020 (versioning), ADR-023 (employee profile) cross-references all unaffected by this sprint's scope. |
| Pattern compliance | CLEAN | This sprint adds no new code patterns; it lifts established external patterns (analyzer pack, secret scanning, vulnerable-package check, dependency automation) into existing CI. Find-and-replace pattern for sprint-shift amendments follows the S38 TASK-3803 ADR-013 amendment precedent (governed by "edit ACCEPTED ADR with explicit amendment block" convention). |
| Orphan detection | DEBT (carry-forward from S34/S35) | 80+ stale locked agent worktrees under `.claude/worktrees/`; S39 uses no worktrees so non-blocking. Operational housekeeping deferred to Phase 4e backlog. |
| Documentation drift | DRIFT-IDENTIFIED | `docs/QUALITY.md` "Last updated: Sprint 35 (2026-05-20)" stale by 4 sprints (S36 / S37 / S38 / S38b all closed). TASK-3915 absorbs. PROGRAM-s36-s41 filename will be stale post-amendment (TASK-3914 renames to PROGRAM-s36-s42). |
| Quality grade review | SCHEDULED | Re-grade at TASK-3915 close. Frontend C+ → B- candidate (vitest now CI-enforced — closes long-standing testing-as-only-build-check gap). Backend API A → A candidate (no change expected; tooling lift doesn't alter pattern compliance). CI/Tooling new category — partial credit for additive gates landing this sprint; full grading deferred to post-S42 (after coverage gating strategy decided). |
| Refinement disposition | READY | 4-cycle Step 4 dual-lens absorbed: cycle 1 = 4 BLOCKERs + 3 WARNINGs + 1 NOTE; cycle 2 = 2 NEW BLOCKERs (same area; missed-facts) + 1 WARNING + 2 NOTEs; cycle 3 = 1 NEW BLOCKER (same area; over-specification) → halt-and-prompt user adjudication → simplification absorption; cycle 4 = 1 NEW BLOCKER (mechanical contradiction in simplified spec) → halt-and-prompt → one-filter-expression patch. Cycle trail recorded as lesson in refinement Cycle 4 Readiness section: "refine the operation, not the enumeration." |
| Smoke-alarm assessment | NOTED | 4 cycles same-area is genuinely the smoke-alarm pattern per `feedback_thrash_defer_real_world.md`. User chose pragmatic-fix at cycle 4 per `feedback_dont_pause_for_reviews.md` rationale (cycle-4 fix is one filter expression, not architectural). Step 0b plan review serves as additional safety net before Phase 1 dispatch — if Step 0b cycle 1 surfaces ANOTHER same-area BLOCKER, escalate to S39a/S39b split. |

## Step 0b — Plan Review Trigger

**MANDATORY** per trigger criteria — sprint touches:

- **P1 (Architectural integrity)**: amends 3 ACCEPTED ADRs (ADR-024 + ADR-025 + ADR-026) in lockstep. First time the project amends 3 ADRs in a single commit.
- **P3 (Event sourcing / auditability)**: TASK-3912 amends ADR-026 (audit-visibility surface, just-ACCEPTED at S38b close 2 days ago). ADR-001 (event immutability) untouched; D13 projection pattern referenced from amended ADR-026 sections.
- **P7 (Security / access control)**: gitleaks (TASK-3903) + vulnerable-package CI step (TASK-3904) + SecurityCodeScan replaced with in-box .NET Analyzers CA3xxx/CA5xxx (TASK-3909) — three new security-posture gates.
- **P8 (CI/CD enforcement)**: every task in scope is P8 by definition.
- **NEW: First +1-sprint customer-go-live slip** under ADR-026's launch-blocking commitment. ADR-026 amendment formalises the slip; ROADMAP Impact Assessment block (TASK-3913) records the rationale.
- **NEW: First 4-cycle refinement absorption with smoke-alarm trail**. Plan review serves as additional review layer beyond the refinement's own Step 4 cycles.

Dispatch dual-lens (Codex external + Reviewer Agent internal) on this PLAN file before Phase 1 dispatches. Cycle-cap = 2 per lens per Step 0b discipline.

**Escalation criterion**: if Step 0b cycle 1 surfaces a BLOCKER in the same area as refinement cycles 1-4 (ADR amendment scope, find-and-replace edge case, or sprint-shift binding completeness), HALT and escalate to user for S39a/S39b split decision. Cycle 5 of same-area thrash would confirm the design is wider than a single sprint can carry cleanly.

---

## Architectural Constraints

_Checked at sprint close (TASK-3916)._

- [ ] **P1 — Architectural integrity** → 3-ADR amendment lands cleanly with no broken cross-references (verified via TASK-3914 grep audit returning ZERO matches outside amendment blocks); PROGRAM rename consistent across all consumers (reference docs, ROADMAP, sprint INDEX)
- [ ] **P3 — Event sourcing / auditability** → No event schema changes; no projection-table changes; ADR-026 amendment preserves D13 sync-in-tx projection commitment for audit-visibility
- [ ] **P4 — Version correctness** → No OK-version handling changes; SDK pin in global.json (T-3901) does not alter compiled IL
- [ ] **P5 — Integration isolation / delivery guarantees** → No outbox / publisher / consumer changes
- [ ] **P6 — Payroll integration correctness** → No payroll-side code changes; vulnerable-package check (T-3904) may surface CVEs in Npgsql or transitive deps — High+Critical force upgrade, Medium logged for follow-up
- [ ] **P7 — Security / access control** → gitleaks allowlist covers dev bcrypt hashes + appsettings + .claude/ + tests/Fixtures (no live secrets in repo confirmed by pre-task audit); vulnerable-package gate adds CVE defence-in-depth; .NET Analyzers CA3xxx/CA5xxx catch injection / weak-crypto / hardcoded-creds patterns going forward
- [ ] **P8 — CI/CD enforcement** → 9 new CI steps + 8 new build-time gates landed (or escape-hatch documented); Dependabot active across 4 ecosystems
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
| **Dependencies** | none |
| **KB Refs** | REFINEMENT-s39-tooling-debt.md (READY); ROADMAP L25 customer-go-live commitment; ADR-026 L367 (to be amended at TASK-3912) |

**Validation Criteria**:
- [ ] PLAN-s39.md filed with full task log + Step 0a + Step 0b sections
- [ ] SPRINT-39.md initial sprint-doc filed with provisional task slots
- [ ] INDEX.md provisional Sprint 39 entry added
- [ ] Sprint-open commit through hook (`d5c6a87` extended sprint-close-guard's reviewed-against-commit staleness check — sprint open does not trigger it)

---

### Phase 1 — Additive Zero-Friction Gates (TASK-3901..3906)

All 6 tasks sequential. No src/ changes; touches repo-root config files + .github/workflows/ + .github/dependabot.yml + .gitleaks.toml.

#### TASK-3901 — global.json SDK pin

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct (one-file change) |
| **Components** | `global.json` (new) |
| **Validation** | (a) `dotnet --version` in CI returns 8.0.x; (b) `dotnet build StatsTid.sln` succeeds; (c) all existing test suites pass unchanged |

Content: `{"sdk": {"version": "8.0.0", "rollForward": "latestFeature", "allowPrerelease": false}}`. Actual minor version pinned to whatever the CI runner ships; `latestFeature` allows minor upgrades within 8.0.x.

#### TASK-3902 — Dependabot config

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.github/dependabot.yml` (new) |
| **Validation** | (a) `gh api /repos/<owner>/<repo>/dependabot/secrets` or equivalent — Dependabot active for 4 ecosystems; (b) first cron fires after merge; (c) no auto-merge enabled |

4 ecosystems: nuget, npm (scoped to `frontend/`), github-actions, docker (scoped to compose + 3 dockerfiles). **Staggered cron** to avoid initial PR firehose: nuget on Mondays; npm + docker + github-actions on Thursdays. Manual review only this sprint.

#### TASK-3903 — gitleaks CI step + allowlist

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Security agent (per AGENTS.md scope) |
| **Components** | `.gitleaks.toml` (new), `.github/workflows/ci.yml` (gitleaks step added) |
| **Validation** | (a) `gitleaks detect --config .gitleaks.toml` returns 0 findings on master HEAD; (b) CI step runs on push + PR; (c) allowlist documented |

**Pre-task audit** per refinement: `grep -ri "password\|secret\|api[_-]key\|token" .claude/ src/**/appsettings*.json tests/**/Fixtures/ 2>/dev/null` to size the allowlist. Known allowlist entries:
- bcrypt dev hashes at `docker/postgres/init.sql:831-837` (regex matches `$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te`)
- `.claude/` artifact tree (review transcripts may quote secret-shaped strings from absorption commits)
- any password-shaped strings in `tests/**/Fixtures/` (audit at task time)

#### TASK-3904 — `dotnet list package --vulnerable` CI step

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct (one-file CI change) |
| **Components** | `.github/workflows/ci.yml` (vulnerable-package step added) |
| **Validation** | (a) step runs on push + PR; (b) passes on master HEAD with no High/Critical CVEs (Medium logged but not failing); (c) emergency `--ignore-vulnerable PKG_NAME` flag documented as last-resort with commit-message rationale required |

CI step:
```yaml
- name: Check for vulnerable packages
  run: |
    dotnet list package --vulnerable --include-transitive 2>&1 | tee vulnerable.log
    if grep -E '> (High|Critical)' vulnerable.log; then
      echo "::error::High or Critical severity CVE found"; exit 1
    fi
```

#### TASK-3905 — Wire smoke tests into CI

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.github/workflows/ci.yml` (new test step) |
| **Validation** | (a) `dotnet test tests/StatsTid.Tests.Smoke` runs in CI; (b) passes on master HEAD; (c) test-results.trx artifact captured |

Closes half of long-deferred Codex Rec #7. Smoke project already exists (`tests/StatsTid.Tests.Smoke/SmokeTests.cs`); just needs wiring.

#### TASK-3906 — Wire frontend vitest into CI

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct or UX agent (file scope: ci.yml) |
| **Components** | `.github/workflows/ci.yml` (new step in frontend-build job, after `npm run build`) |
| **Validation** | (a) `npm run test` runs 90 vitest tests; (b) passes on master HEAD; (c) test output captured |

Closes the other half of Codex Rec #7. Test count baseline locked at 90 per S38b close.

---

### Phase 2 — Code-Cleanup-Triggering Gates (TASK-3907..3911)

All 5 tasks sequential. Touches build infrastructure + per-project csprojs.

#### TASK-3907 — `Directory.Build.props` introduction + dry-run

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct (root-level config) |
| **Components** | `Directory.Build.props` (new at repo root); `tests/Directory.Build.props` (new — overrides warn-as-error to false); `tools/Directory.Build.props` (new); `docker/mock-payroll/Directory.Build.props` + `docker/mock-external/Directory.Build.props` (new each) |
| **Validation** | (a) all 14 csprojs build clean with new props; (b) dry-run warning baseline recorded in `docs/QUALITY.md` "Pre-S39 Warning Baseline" section; (c) per-project warning counts captured |

**First sub-step (T-3907.1)**: dry-run with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` temporarily set, run `dotnet build StatsTid.sln 2>&1 | tee warn-baseline.log`, count warnings per project. Record table in QUALITY.md. **Second sub-step (T-3907.2)**: revert temp setting; commit Directory.Build.props with `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` default + structured Condition= or subdirectory props for tests/tools/mocks exclusion.

**Threshold for per-project warn-as-error opt-out** (set after T-3907.1 baseline): whatever value clears ≥5 of 8 production csprojs. Discovered, not pre-committed (Reviewer NOTE #4 absorbed).

#### TASK-3908 — Per-project warn-as-error rollout

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | per-project Builder Agent dispatch (8 production csprojs; Orchestrator-direct works too since each project is a single-line csproj edit) |
| **Components** | 8 production csprojs: `src/Auth/StatsTid.Auth/StatsTid.Auth.csproj`, `src/Backend/StatsTid.Backend.Api/StatsTid.Backend.Api.csproj`, `src/Infrastructure/StatsTid.Infrastructure/StatsTid.Infrastructure.csproj`, `src/Integrations/StatsTid.Integrations.External/StatsTid.Integrations.External.csproj`, `src/Integrations/StatsTid.Integrations.Payroll/StatsTid.Integrations.Payroll.csproj`, `src/Orchestrator/StatsTid.Orchestrator/StatsTid.Orchestrator.csproj`, `src/RuleEngine/StatsTid.RuleEngine.Api/StatsTid.RuleEngine.Api.csproj`, `src/SharedKernel/StatsTid.SharedKernel/StatsTid.SharedKernel.csproj` |
| **Validation** | (a) at least 5 of 8 production csprojs successfully gated with `TreatWarningsAsErrors=true`; (b) escape-hatch projects keep their pre-T-3907.1 baseline warning count or lower; (c) per-project decision + baseline recorded in `Directory.Build.props` structured comment block |

**Escape hatch shape** (Reviewer NOTE #1 absorbed): for each opt-out project, add to `Directory.Build.props`:
```xml
<PropertyGroup Condition="'$(MSBuildProjectName)' == 'StatsTid.Infrastructure'">
  <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  <!-- Baseline: 73 warnings as of S39 T-3907.1 dry-run; cleanup deferred to follow-up tooling sprint -->
</PropertyGroup>
```

#### TASK-3909 — .NET Analyzers (in-box) enablement

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Security agent (analyzer scope is security-leaning) |
| **Components** | `Directory.Build.props` (add `<EnableNETAnalyzers>true</EnableNETAnalyzers>` + `<AnalysisMode>AllEnabledByDefault</AnalysisMode>`) |
| **Validation** | (a) all 8 production csprojs build with .NET Analyzers active; (b) at least 5 of 8 successfully gated with strict CA3xxx + CA5xxx mode; (c) per-project `<NoWarn>CAxxxx</NoWarn>` allowlists for known false positives captured with rationale |

**SecurityCodeScan.VS2019 NOT USED** per Reviewer cycle-1 WARNING #4 (last shipped 2022, unmaintained on .NET 8 / Roslyn ≥4.x). In-box `Microsoft.CodeAnalysis.NetAnalyzers` ships with .NET 8 SDK and covers the same CA3xxx (injection) + CA5xxx (cryptography) territory. Same per-project escape-hatch policy as T-3908.

#### TASK-3910 — Coverage measurement (baseline-recording only)

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Test & QA agent |
| **Components** | `coverlet.runsettings` (new), `.github/workflows/ci.yml` (coverage step added), `docs/QUALITY.md` (new "Coverage Baseline" section) |
| **Validation** | (a) `dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings` runs in CI; (b) cobertura XML uploaded as artifact; (c) per-assembly coverage baseline table in QUALITY.md |

**No `≥80%` gate this sprint** — strategy decision deferred to post-S42 per refinement Open Question 3 (lean toward "no regression below baseline" recorded in MEMORY/feedback). Baseline-recording mode only.

#### TASK-3911 — lizard CCN report (report-only)

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct (one CI step) |
| **Components** | `.github/workflows/ci.yml` (lizard step + `actions/setup-python@v5` prerequisite) |
| **Validation** | (a) lizard runs in CI on src/**/*.cs; (b) threshold 15 logged as warning, NOT failing; (c) report uploaded as artifact |

Report-only this sprint. Output reviewed at sprint close to inform whether to enable as a gate in a later sprint and whether the Rule Engine needs CCN exemptions for pure-function switch statements over enums (per ADR-002 RuleEngine purity).

---

### Phase 3 — Sprint Admin + ADR Amendment (TASK-3912..3916)

All 5 tasks sequential. T-3912 is the load-bearing architectural commit; T-3914 cross-references it (PROGRAM rename + reference doc sprint-shift must be consistent with ADR amendments).

#### TASK-3912 — ADR-024 + ADR-025 + ADR-026 amendment (programmatic find-and-replace)

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct (KB writes are Orchestrator-only per CLAUDE.md §Agent Architecture) |
| **Components** | `docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md`, `docs/knowledge-base/decisions/ADR-025-multi-tenant-operational-concerns.md`, `docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md` |
| **Validation** | per refinement TASK-3912 verification steps (see below) |

**Operation** (per refinement; executed in this order):
1. **Reverse-order sprint-number shift** (whole-word `\bS39\b`, S41→S42→S40→S41→S39→S40) across the 3 ADRs.
2. **Reverse-order TASK-number shift** (TASK-41XX → TASK-42XX → TASK-41XX → TASK-40XX → TASK-39XX → TASK-40XX) across same doc set.
3. **Manual edit** on customer-go-live commitment text in ADR-026 L365-367 + ADR-025 L268: "cannot defer past S39" → "cannot defer past customer-go-live".
4. **Amendment block** at top of each of the 3 ADRs (verbatim per refinement step 4 spec).

**Verification** (executed before commit):
- `grep -nE '\bS39\b|TASK-39[0-9]{2}|cannot defer past S39' docs/knowledge-base/decisions/ADR-024*.md docs/knowledge-base/decisions/ADR-025*.md docs/knowledge-base/decisions/ADR-026*.md | grep -vE '^[^:]+:[0-9]+:>\s'` returns ZERO matches outside amendment blockquotes
- `grep -nE 'cannot defer past customer-go-live' docs/knowledge-base/decisions/ADR-025*.md docs/knowledge-base/decisions/ADR-026*.md` returns at least 2 matches
- Each of the 3 ADRs starts with verbatim amendment block
- **Reviewer Agent invocation on the commit diff before merge** — pattern-replace can produce spurious matches in code samples / source-register row IDs / cross-reference IDs (e.g., `SR-AC-OK24-039` would NOT match `\bS39\b` due to boundaries, but adjacent context might surprise)

**Reviewer Agent dispatch criteria for this task** (interim — separate from Step 7a at sprint close): review the diff for (a) cross-reference invariants (every `[[ADR-XXX]]` style link still resolves), (b) section-numbering integrity (no orphan section refs), (c) intent preservation (the find-and-replace shouldn't silently change semantic claims beyond sprint-number context).

#### TASK-3913 — ROADMAP.md sprint renumber + Impact Assessment block

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct (ROADMAP.md is Orchestrator-only) |
| **Components** | `ROADMAP.md` |
| **Validation** | (a) S39 row in completed-sprints table reflects tooling debt; (b) Phase Roadmap section adds Impact Assessment block; (c) S40/S41/S42 references reflect audit-visibility shift |

Impact Assessment block per existing ROADMAP convention (S18/S19/S21 precedents in the same file). Cites `docs/WORKFLOW.md:127 §Sprint Numbering & Re-prioritization` as governing convention (not CLAUDE.md per Reviewer WARNING #3). Rationale: pre-launch tooling debt is cheaper to close before more code lands; customer-go-live slides by +1 sprint, formalised by ADR-026 amendment at TASK-3912.

#### TASK-3914 — PROGRAM rename + reference-doc sprint shift

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PROGRAM-s36-s41-domain-correctness.md` → `.claude/plans/PROGRAM-s36-s42-domain-correctness.md` (rename + content shift); 5 reference docs; ROADMAP cross-references |
| **Validation** | per refinement TASK-3914 verification (zero `\bS39\b` matches outside completed-sprints historical table + legitimate new S39 tooling sprint references) |

**Operation** (per refinement):
1. `git mv .claude/plans/PROGRAM-s36-s41-domain-correctness.md .claude/plans/PROGRAM-s36-s42-domain-correctness.md`
2. Reverse-order sprint-number shift across renamed PROGRAM + 5 reference docs + ROADMAP Phase Roadmap section.
3. Reverse-order TASK-number shift across same doc set.

**Verification grep** (post-pass):
```
grep -nrE '\bS39\b|TASK-39[0-9]{2}|Sprint 39|PROGRAM-s36-s41' \
  .claude/plans/PROGRAM-s36-s42-domain-correctness.md \
  docs/references/ \
  ROADMAP.md \
  | grep -vE 'Tooling Debt|tooling debt'
```
returns ZERO matches outside the completed-sprints historical table.

**Reviewer Agent dispatch criteria**: same as TASK-3912 — check (a) cross-reference invariants, (b) section integrity, (c) intent preservation.

#### TASK-3915 — QUALITY.md re-grade

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct (docs/QUALITY.md is Orchestrator-only) |
| **Components** | `docs/QUALITY.md` |
| **Validation** | (a) "Last updated" header reflects Sprint 39 (2026-05-XX); (b) stale Priority Improvement Areas resolved; (c) Coverage Baseline section per T-3910 absorbed; (d) Pre-S39 Warning Baseline section per T-3907.1 absorbed; (e) CI/Tooling row added; (f) Frontend grade reassessed against vitest-in-CI delta |

Cycle-4-recorded lesson appended to a new "Refinement / Plan Lessons" section if appropriate: "refine the operation, not the enumeration" — for future wide-surface tooling sprints.

#### TASK-3916 — Sprint close

| Field | Value |
|-------|-------|
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/sprints/SPRINT-39.md` (close sections), `docs/sprints/INDEX.md`, `MEMORY.md` entry |
| **Validation** | (a) Step 7a Codex + Reviewer artifacts at `.claude/reviews/SPRINT-39-step7a-{codex,reviewer}.md` per `sprint-close-guard.ps1`; (b) sprint-close-guard hook passes; (c) all 16 prior tasks marked complete; (d) sprint-end HEAD commit hash backfilled; (e) ROADMAP S39 entry status flipped to "complete"; (f) **override rationale recorded in commit message if staleness check fires on T-3912 ADR amendments** (per Risk #10) |

Step 7a dual-lens (Codex + Reviewer Agent) on full S39 diff vs `d5c6a87`. Review focus: did the cycle-1-through-cycle-4 absorption discipline hold across implementation (any commits that diverge from the refinement's READY spec)? Did the find-and-replace amendments preserve all cross-reference invariants? Did Phase 2 escape-hatches stay within the >5-of-8 acceptance criteria?

---

## Forward Pointers

- **S40 = audit_projection schema migration** (was S39 pre-amendment). Per amended ADR-026 + ADR-024 + ADR-025: schema for `role_within_agreement_configs` + `institutions` + `audit_projection` tables; `RoleWithinAgreementConfigRepository`; Phase E continuous-validation tests (was S39 TASK-3905, now TASK-4005 post-cascade-renumber).
- **S41 = ADR-026 cutover** (was S40). Per amended ADRs: `ConfigResolutionService` role-layer extension; `AuditProjectionRepository` + `IAuditProjectionMapper<T>` interface + ~53 mappers; `GET /api/admin/audit` endpoint + `AuditLogView.tsx`.
- **S42 = audit-visibility D-tests** (was S41). Per amended ADRs: 5 D-tests per ADR-026 D7 + agreement × role × OK-version matrix per ADR-024.
- **Post-S42 tooling sprint candidate**: coverage gating strategy decision (refinement Open Question 3), Phase 2 escape-hatch cleanup for projects that opted out at T-3908/3909, Dependabot auto-merge policy revisit after first month of PR volume.
- **Phase 4e backlog**: stale worktree housekeeping (carry-forward from S34/S35); frontend tooling parity sweep (`npm audit`, warn-as-equivalent — Phase 5 polish).

---

## Lessons from Refinement Cycle Trail (for future sprints)

1. **Refine the operation, not the enumeration**. Pre-enumerating mechanical work at refinement-time creates over-specification traps when the surface is wide (4-cycle absorption proved this on the ADR-amendment scope).
2. **Reverse-order pattern-replace** is the canonical mechanism for cascading sprint-number / version shifts (S41→S42→S40→S41→S39→S40, never S39→S40→S40→S41 which double-applies).
3. **Whole-word match boundaries** (`\bS39\b`) are critical when sprint numbers can substring-match other identifiers (e.g., `SR-AC-OK24-039` would match `S39` without word boundaries).
4. **Amendment blocks intentionally contain stale-numbered references** (e.g., "S39 sprint slot re-allocated") — verification greps must exclude blockquote-prefixed lines.
5. **Smoke-alarm pattern is real but pragmatic-fix path exists** when cycle-N findings are mechanical not architectural. `feedback_dont_pause_for_reviews.md` discipline trumps `feedback_thrash_defer_real_world.md` when cycle-N substance is genuinely one-filter-expression sized. **CORRECTED at Step 0b cycle 1**: this is NOT a "trumps" rule — when cycle-N+1 fixes are *not* one-filter-expression sized (3 same-area BLOCKERs at once at Step 0b), the smoke-alarm discipline wins. User-adjudication, not rule-of-thumb (per Reviewer NOTE #1).

---

## Step 0b Cycle 1 Review — 2026-05-23 (ESCALATION FIRED)

**Outcome**: User chose S39a/S39b split per `feedback_thrash_defer_real_world.md` discipline. Plan superseded by PLAN-s39a.md (ADR amendment + sprint shift) + PLAN-s39b.md (tooling gates).

### Findings

*External (Codex) Step 0b cycle 1:*

- **BLOCKER #1 [SAME AREA as refinement cycles 3-4 → escalation fires]** — TASK-3912 find-and-replace spec ambiguous: chained transform vs three ordered source→target replacements. The arrow notation `S41→S42→S40→S41→S39→S40` reads as either (cite plan L271-273, L359).
- **BLOCKER #2** — P2 (Deterministic rule engine) missing from architectural constraints checklist (L72-79) despite CLAUDE.md priority order including P2.
- **BLOCKER #3** — Several agent assignments outside declared AGENTS.md scopes: Security agent on repo-root `.gitleaks.toml` / CI / `Directory.Build.props` (L137-139); Test & QA on CI/docs/config (L228-240).
- WARNING — TASK-3903 task body omits appsettings from known allowlist entries while P7 checklist references appsettings coverage (L141-144 vs L77).
- NOTE — Step 0a worktree count stale: plan says 80+, actual is 77.
- NOTE — Phase/task count wording inconsistent: 17 declared, "all 16 work tasks" elsewhere.

*Internal (Reviewer Agent) Step 0b cycle 1:*

- **BLOCKER #1 [SAME AREA as refinement cycle 2 → escalation fires]** — TASK-3914 scope omits PROGRAM filename references inside the 3 ADRs (ADR-024:14,288; ADR-025:16,298; ADR-026:371). After TASK-3914 renames `PROGRAM-s36-s41-domain-correctness.md` → `PROGRAM-s36-s42-domain-correctness.md`, those 3 ADR references become dangling links. The TASK-3914 verification grep scope (5 ref docs + ROADMAP) misses this. Also sprint docs SPRINT-35..38b + PLAN-s36/37/38 have PROGRAM filename references — but per WORKFLOW.md L161 "completed sprints are never renumbered, historical records" those *should* stay (filename refs become dangling but sprint-number context inside historical sprint docs is correct as-is). The split between "rename target" vs "historical context preserved" is the wide-surface ambiguity.
- **BLOCKER #2 [non-same-area]** — TASK-3905 silently requires CI docker-compose orchestration: `tests/StatsTid.Tests.Smoke/SmokeTests.cs:12-25` hardcodes `http://localhost:5100-5700` against running compose services. Current `ci.yml` runs `dotnet test` only — no compose step. TASK-3905 is 2 bundled tasks (CI docker harness + smoke wiring), not one.
- **BLOCKER #3 [SAME AREA as refinement cycle 4 → escalation fires]** — TASK-3912 amendment-block format diverges from cited S38 TASK-3803 precedent. Actual precedent at `ADR-013-retroactive-corrections-single-period-no-cascade.md:46` is a `## Amendment — ADR-024 cross-reference (S38 TASK-3803, 2026-05-21)` SECTION at the BOTTOM, NOT a `> **Amended...**` blockquote at the TOP. Verification grep filter (excludes blockquote lines) would fail if section-at-bottom precedent is followed.
- WARNING — Sprint sizing: 17 tasks vs S35's 11; realistic sizing closer to S35+S25 combined (~22 task-equivalents).
- WARNING — Step 0b cycle-cap discipline not explicitly captured (resolved by this escalation).
- WARNING — Architectural Constraints checklist missing P2 line (convergent with Codex BLOCKER #2).
- WARNING — TASK-3915 grade-decision rubric not pre-committed (how many escape-hatches before backend grade movement is suppressed).
- WARNING — Customer-go-live commitment is 3 surfaces not 2: ADR-025 L204 ("ADR-026 cannot defer past S39") missed in TASK-3912 step 3.
- NOTE — Lessons section #5 framing "trumps" risk of misreading; reframe as "user-adjudication."
- NOTE — Phase 1 reorder candidate (gitleaks before global.json).
- NOTE — Coverage strategy decision: post-S42 (plan) vs post-S43 (refinement) inconsistency.

### Same-area BLOCKER count by cycle

| Lens | Cycle | Same-area BLOCKERs | Total BLOCKERs |
|---|---|---|---|
| Refinement Step 4 | 1 | 1 (ADR-026 scope) | 4 |
| Refinement Step 4 | 2 | 1 (ADR-024+025+ref docs scope) | 2 |
| Refinement Step 4 | 3 | 1 (line enumeration over-spec) | 1 |
| Refinement Step 4 | 4 | 1 (verification grep contradiction) | 1 |
| **PLAN Step 0b** | **1** | **3** (Codex find-and-replace ambiguity + Reviewer TASK-3914 scope + Reviewer amendment-block precedent) | 6 |

Five consecutive review cycles, same area, increasingly fine-grained. Smoke-alarm definitively confirmed. Split executed.
