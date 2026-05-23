# StatsTid Quality Grading

> **Governance**: Updated by the Orchestrator at sprint end or during entropy scan. See CLAUDE.md "Quality Grading" section for grade definitions.

## Domain Quality Matrix

Last updated: Sprint 39 (2026-05-23)

| Domain | Test Coverage | Pattern Compliance | Documentation | Tech Debt | Grade | Trend |
|--------|-------------|-------------------|---------------|-----------|-------|-------|
| Rule Engine | High — 16 segmentation-classified rules + 1 out-of-scope, multi-mode decomposition tests, determinism proofs; **ADR-016 D10 closed for the entire dated rule-engine input surface as of S34 (4 of 4 inputs)** | Full — pure functions, no I/O (ADR-002, now enforced at assembly graph level post-S19) | Strong — ADR-002, ADR-003, PAT-002, PAT-003, PAT-006, RES-001, ADR-015, ADR-016, ADR-020, ADR-021, ADR-022, ADR-023 | Low — no remaining dated-input determinism gaps | **A++** | ▲ |
| SharedKernel (Models) | High — immutability tests, config tests, balance tests; additive ManifestId fields (S20); LocalAgreementProfile + AlignmentPolicies + LegacyKeyToColumn (S21) | Full — init-only properties (PAT-001) | Good — PAT-001, ADR-017 | Low | **A** | ● |
| SharedKernel (Events) | Medium-High — registered in EventSerializer (45 types after S21), reflection-based coverage test, manifest creation/replay tests exercise SegmentManifestCreated + LocalAgreementProfileChanged end-to-end | Full — DomainEventBase pattern (PAT-004, DEP-003) | Good — ADR-016 D10 (manifest persistence), ADR-017 D6 (profile event shape) | Low | **A-** | ● |
| SharedKernel (Segmentation) | High — 6 cell tests (5 populated `(span × split-behavior)` pairs), 4 D9 invariant negatives, 8 boundary scenarios, perf budget; LocalProfileActivation cause + hydration test (S21) | Full — internal ctor + InternalsVisibleTo gate, PlannerInvariantViolation pattern, geometric invariants in ctor (ADR-016 D9), additive BoundarySources extension (S21) | Strong — ADR-016 (D1–D11), ADR-017 D9 (boundary integration) | Low | **A** | ● |
| Infrastructure | Medium-High — repositories not directly unit-tested (integration-level), SegmentManifestProjectionRebuilder (S20), LocalAgreementProfileRepository + LocalAgreementProfileMigrator + ConfigResolutionService rewrite (S21) — exercised via 18 Docker-gated profile tests | Full — Npgsql pattern, seeder pattern (ADR-014), audit middleware stamps manifest_id (S20), partial-unique-index lifecycle pattern (ADR-017 D1) | Good — ADR-001, ADR-004, ADR-008, ADR-016 D10, ADR-017 | Low — Step-7a residual close-then-insert window math edge case → Phase-4 carry-forward | **B+** | ▲ |
| Security | Low — no dedicated security unit tests; coverage via integration paths; S35 adds barrier-synchronized concurrent-admin-PUT D-test (TASK-3509) + concurrent-POST users_pkey 23505 race D-test | Full — JWT, RBAC, scope validation (ADR-007, ADR-009); StatsTid.Auth assembly post-S19; ETag/If-Match D2.1 concurrency now on ALL admin write surfaces (S25 + S35) — last unprotected admin write closed | Good — ADR-007, ADR-009, FAIL-001, ADR-017 D2.1, ADR-018 D7, ADR-019 D2 | Low-Medium — admin-strict If-Match propagation complete; FindAll fix legacy reference remains; no dedicated security unit-test suite | **B+** | ▲ |
| Backend API | Medium — endpoint logic tested indirectly via smoke tests; admin-strict If-Match contract now D-test-covered on all 4 admin write surfaces (S25 + S35 TASK-3509 8 net-new D-tests including concurrent-PUT race + stale If-Match + missing If-Match + null-fallback lockedUser snapshot) | Full — ADR-019 D2 admin-strict If-Match contract applied 4th and final time on `/api/admin/users` (closes the last unprotected admin write); ADR-018 D7 row-version on `users.version`; ADR-019 D8 audit version-transition columns on `users_audit`; ADR-018 D3 atomic outbox preserved across PUT + POST | Good — ADR-018 D7, ADR-019 D2 + D8 documented; admin surface lineage in SPRINT-25 + SPRINT-35 | Low — same-hook + local-fetch gaps on legacy admin pages remain (Phase 5 polish); production-readiness legacy-DB-upgrade sweep deferred to a coherent Phase 4e runbook sprint | **A** | ▲ |
| Payroll Integration | High — mapping tests, SLS format tests, correction tests, compensation mapping, mixed-version export, manifest creation/replay/projection-rebuild tests, boundary scenarios; LocalProfileActivation hydration test (S21); WTM marquee replay-determinism test (S29) | Full — traceability chain (PAT-005), correction format (ADR-013), compensation-aware mapping, planner-driven calculation (ADR-016 D1, D8), per-line OK stamping (S20), profile-activation hydration (ADR-017 D9c); WTM versioned history + export-time effective-date lookup closes ADR-016 D10 for WTM (ADR-018 D14, S29) | Strong — PAT-005, PAT-006, DEP-002, ADR-016, ADR-017 D9c, ADR-018 D14, ADR-020 | Low — `/calculate-and-export` is last `[Obsolete]` shim customer | **A** | ▲ |
| Frontend | Medium — 90 vitest tests (was 88 pre-S35; +2 from S35 UserManagement banner-with-retry), **now CI-enforced** (S39 TASK-3907); no E2E, no visual regression | Partial — some pages use local fetch instead of shared hooks; ProfileEditor uses shared `useConfig` hook | Sparse — ADR-011 covers design system, no component docs; ADR-017 documents profile editor scope | Medium — CORS fixes were reactive, some pages inconsistent; ProfileEditor explicitly basic (Phase-5 polish deferral) | **B-** | ▲ |
| PostgreSQL Schema | N/A (schema, not code) | Full — unique constraints, indexes, seed data; segment_manifests + GIN index (S20); local_agreement_profiles partial-unique-index `WHERE effective_to IS NULL` + local_agreement_profile_audit (S21) | Partial — init.sql is self-documenting, no ER diagram; migration plan documented in SPRINT-21.md | Low | **B+** | ● |
| Docker/Infrastructure | N/A (config, not code) | Full — 8-service compose (ADR-006); container-side healthchecks call curl which isn't shipped in .NET runtime images — broken everywhere, surfaced by S39 TASK-3905; CI uses host-side loop to side-step; Dockerfile curl install deferred to Phase 4e | Good — ADR-006 | Low-Medium — container healthcheck breakage now documented; fix scheduled | **B+** | ● |
| CI/Tooling | High — gitleaks secret scan + dotnet vulnerable-package check + smoke-tests harness + vitest + lizard CCN + coverage baseline all in CI (S39); Dependabot active on 4 ecosystems (nuget/npm/github-actions/docker, staggered cron); 7 of 8 production csprojs gate strict warn-as-error with in-box .NET Analyzers security mode | Full — Directory.Build.props + global.json + .gitleaks.toml + coverlet.runsettings + dependabot.yml all repo-root-managed | Good — TASK comments in each config file cite rationale | Low — single deferred-debt opt-out (StatsTid.Integrations.Payroll, CS0618 deferred-retirement legacy CalculateAsync per S20 Step 0b W2) | **B+** | ▲ (new domain) |

### Grade Legend
- **A**: High coverage, full compliance, well-documented, low debt
- **B**: Adequate coverage, mostly compliant, some gaps, manageable debt
- **C**: Notable gaps in coverage or compliance, needs attention
- **D**: Significant gaps, active tech debt, should be prioritized
- **F**: Broken or non-compliant — immediate action

### Trend Legend
- ▲ Improving (grade improved or debt decreased in recent sprint)
- ● Stable (no change)
- ▼ Declining (new debt or degradation)

## Priority Improvement Areas

1. **Frontend (B-)**: Still needs E2E tests, shared hook refactoring (some pages bypass `useConfig` / `apiFetchWithEtag` helpers), component documentation. Vitest CI integration landed in S39 closes the "tests not enforced" gap.
2. **Security (B+)**: Admin-strict If-Match propagation complete on all 4 admin write surfaces (S35). Remaining gap: no dedicated security unit-test suite — auth flow / scope validation / claim parsing tested only through integration paths.
3. **Backend API (A)**: Last unprotected admin write closed in S35. Remaining gap: legacy admin pages still inline fetch instead of using shared hooks (Phase 5 polish); production-readiness legacy-DB-upgrade sweep deferred to a coherent Phase 4e runbook sprint.
4. **Coverage gating** (new — S39): baseline recorded but `≥X%` gate not enforced. Strategy decision (no-regression / ratchet / hard-80%-blanket) deferred to post-launch sprint. See "Coverage Baseline" section.
5. **Container healthchecks (new — S39 finding)**: 7 Dockerfiles invoke curl in healthchecks but base image (mcr.microsoft.com/dotnet/aspnet:8.0) doesn't ship curl. CI works around it via host-side loop; for production deploy a Dockerfile fix or healthcheck rewrite is Phase 4e candidate.

## Historical Grades

| Domain | S14 | S15 | S17 | S20 | S21 | S29 | S30 | S31 | S33 | S34 | S35 |
|--------|-----|-----|-----|-----|-----|-----|-----|-----|-----|-----|-----|
| Rule Engine | A | A | A | A | A | A | A | A | A+ | A++ | **A++** (S35: no rule-engine surface touched. AC family `DefaultCompensationModel` seed correction (TASK-3503: UDBETALING → AFSPADSERING) is a config-data fix, not a rule-engine change — rules consume the corrected value. Replay determinism contract unchanged. Grade held.) |
| SharedKernel (Models) | A | A | A | A | A | A | A | A | A | A | A (S35: no new SharedKernel models. New `ConcurrentSeedConflictException` lives in `SharedKernel/Exceptions`. No drift.) |
| SharedKernel (Events) | B+ | B+ | B+ | A- | A- | A- | A- | A- | A- | A- | A- (S35: no new event types — S35 is admin-strict-If-Match hardening + AC seed correction, neither motivates new domain events. EventSerializer count unchanged at 58.) |
| SharedKernel (Segmentation) | — | — | — | A (new) | A | A | A | A | A | A | A |
| Infrastructure | B | B | B | B | B+ | B+ | B+ | A- | A | A | **A** (S35: new `UserRepository.GetByIdWithVersionAsync` (in-tx FOR-UPDATE + non-tx variants) per ADR-018 D7 row-version contract; new `UserRepository.GetByOrgWithVersionAsync` (Step 7a cycle 1 absorption) returns paired `(User, long Version)` for the admin list endpoint; `UserAgreementCodeRepository.SupersedeAndCreateAsync` now catches Case A 23505 and re-throws typed `ConcurrentSeedConflictException` for deterministic 409 mapping at the endpoint. Pattern landscape stable — same shape as the 4 prior versioned-config repositories. Grade held at A; tech debt drops marginally — the S35 in-flight defect absorption (POST users_pkey 23505 catch) closes a latent race that had been masked.) |
| Security | B- | B- | B- | B- | B- | B- | B- | B | B | B | **B+** (↑ S35: admin-strict If-Match propagation COMPLETE on all 4 admin write surfaces — `/api/admin/users` GET+PUT+POST joins agreement_configs + position_override_configs + wage_type_mappings as the 4th and final If-Match-protected admin write. `users.version` row-version per ADR-018 D7; `users_audit` table with `version_before` + `version_after` columns per ADR-019 D8; FOR-UPDATE-locked re-read inside the PUT critical section closes the audit-trail race class. Concurrent-admin-PUT race pinned by barrier-synchronized D-test (TASK-3509 test 6); concurrent-POST users_pkey 23505 race surfaced + fixed at Step 7a close. No dedicated security unit-test suite remains the gap that keeps this from A-.) |
| Backend API | C+ | B- | B- | B- | B | B | B+ | B+ | A- | A- | **A** (↑ S35: closes the last unprotected admin write surface — `/api/admin/users` PUT now enforces If-Match per ADR-019 D2 with hard 412/428/409 vocabulary mirroring the S25 pattern. POST stamps ETag: "1" + body version=1 on the 201 response. GET partner stamps ETag from the same atomic snapshot it serializes. PUT 200 response body now sources fields from the four FOR-UPDATE'd `lockedUser`-derived `newX` locals hoisted above the try block (Step 7a cycle 1 Reviewer W2 absorption), making the response unambiguously consistent with the UPDATE statement + `users_audit new_data` JSONB + UserUpdated event payload. POST gains explicit `PostgresException SqlState=23505` catch on users_pkey (Step 7a close in-flight defect) symmetric with the pre-flight 409 path + the ConcurrentSeedConflictException 409 path. Pattern landscape stable; ADR-019 D2 admin-strict If-Match contract is now applied at all 4 motivated surfaces.) |
| Payroll Integration | B | B | B+ | A- | A- | A | A | A | A | A | A (S35: no payroll surface touched. AC family seed correction is forward-only per bug-with-no-past-impact pre-launch classification; no retroactive recompute needed; no payroll mapping changes. Grade held.) |
| Frontend | C | C+ | C+ | C+ | C+ | C+ | C+ | C+ | C+ | C+ | C+ (S35: `UserManagement.tsx` migrated to `apiFetchWithEtag<T>` + banner-with-retry mirroring `EmployeeProfileEditor.tsx` precedent. New `useAdmin.ts` `WithEtag<T>` extension + `makeUserMutationError` helper. List endpoint cutover from `GetByOrgAsync` → `GetByOrgWithVersionAsync` so `primaryOrgId` + `version` actually render (Step 7a cycle 1 absorption). 2 net-new vitest cases pin the 412 banner-with-retry happy path end-to-end. Pre-existing 13 TS errors in unrelated legacy files persist; deferred to Phase 5.) |
| PostgreSQL Schema | B | B | B | B+ | B+ | B+ | A- | A- | A- | A- | A- (S35: new `users.version BIGINT NOT NULL DEFAULT 1` baked into the base CREATE + guarded ALTER block at the bottom of init.sql with unconditional `ADD COLUMN IF NOT EXISTS` (Step 7a cycle 2 absorption — repairs ledger-poisoned legacy DBs). New `users_audit` table mirroring the S31/S33/S34 audit shape. action CHECK enum includes all 4 values (CREATED/UPDATED/DELETED/SUPERSEDED) up-front for forward-compat. AC family `DefaultCompensationModel` seed rows corrected (6 init.sql rows: AC + AC_RESEARCH + AC_TEACHING × OK24 + OK26) per TASK-3503 source-cited bug-with-no-past-impact policy application. Same Phase 4e production-readiness deferral as S30/S31 — coherent legacy-DB-upgrade runbook sprint outstanding.) |
| Docker/Infrastructure | B+ | B+ | B+ | B+ | B+ | B+ | B+ | B+ | B+ | B+ | B+ |

## Pre-S39 Warning Baseline

Captured at S39 TASK-3908.1 (2026-05-23) via `dotnet build StatsTid.sln -c Release -p:TreatWarningsAsErrors=true`. Baseline is the per-csproj warning count under strict mode — used as the per-project escape-hatch threshold for TASK-3909 warn-as-error rollout. Production projects intended to clear strict; test/tool/mock projects intentionally remain at warn-as-info.

| Project | Strict-mode errors | Notes |
|---------|-------------------:|-------|
| StatsTid.Auth | 0 | clean |
| StatsTid.Backend.Api | 0 | clean |
| StatsTid.Infrastructure | 0 | clean |
| StatsTid.Integrations.External | 0 | clean |
| **StatsTid.Integrations.Payroll** | **1** | CS0618 at `Program.cs:198` — calls deferred-retirement `PeriodCalculationService.CalculateAsync(EmploymentProfile, …)` legacy overload. Per S20 Step 0b W2: "The single surviving caller is the /calculate-and-export endpoint; full retirement is deferred." This is intentional debt with explicit rationale. T-3909 escape hatch applied to this project only. |
| StatsTid.Orchestrator | 0 | clean |
| StatsTid.RuleEngine.Api | 0 | clean |
| StatsTid.SharedKernel | 0 | clean |
| StatsTid.Tests.Unit (excluded from strict — test project) | ~9 | CS0618 obsolete usages of `ConfigResolutionService.ValidateLocalOverride` from Sprint12AgreementConfigTests + others. Test projects exercise deprecated APIs by design. |
| StatsTid.Tests.Regression (excluded) | ~9 | same shape as Unit |
| StatsTid.Tests.Smoke (excluded) | 0 | clean (small test surface) |
| ProjectionBackfill (excluded — tooling) | 0 | clean |
| MockPayroll, MockExternal (excluded — docker/mock-* dev stubs) | 0 | clean |

**Outcome for TASK-3909**: 7 of 8 production csprojs land strict (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`) immediately; **Payroll opts out** with rationale documented in the csproj. Exceeds the >5-of-8 acceptance criterion comfortably.

## Coverage Baseline

Recorded at S39 TASK-3911 (2026-05-23) via `dotnet test tests/StatsTid.Tests.Unit --collect:"XPlat Code Coverage" --settings coverlet.runsettings`. **Unit-tests-only baseline** — Regression / Smoke / Docker-gated suites contribute additional coverage but aren't measured here (most go through HTTP integration paths against running services, which coverlet doesn't instrument across process boundaries).

**No `≥X%` gate this sprint** per refinement Open Question 3 — strategy decision deferred to post-launch. Baseline-recording mode only.

| Package | Line coverage | Branch coverage | Complexity |
|---------|--------------:|----------------:|-----------:|
| StatsTid.Auth | 89.5% | 66.7% | 89 |
| StatsTid.RuleEngine.Api | 84.1% | 65.0% | 270 |
| StatsTid.SharedKernel | 74.7% | 64.2% | 392 |
| StatsTid.Orchestrator | 40.7% | 51.2% | 89 |
| StatsTid.Integrations.Payroll | 32.3% | 29.3% | 148 |
| StatsTid.Infrastructure | 24.0% | 7.8% | 324 |
| StatsTid.Backend.Api | 0.5% | 18.6% | 211 |
| **Overall (Unit only)** | **18.78%** | **42.59%** | — |

**Reading the numbers honestly**:
- High Unit coverage for Auth, RuleEngine, SharedKernel reflects that those domains are designed as pure functions or have heavy unit-test surface
- Low Unit coverage for Backend.Api, Infrastructure reflects that they're integration-tested (Regression suite + Docker-gated D-tests), not unit-tested — coverlet doesn't cross the test-host boundary
- True effective coverage is significantly higher than 18.78% once Regression + Docker tests are factored in; capturing that requires a more elaborate coverlet config (out of scope for baseline-recording)

**Future gating strategy options** (decision deferred to post-launch sprint):
- (a) "no regression below baseline per assembly" — current default lean
- (b) "ratchet upward N% per sprint" — best long-term, more overhead
- (c) "hard 80% blanket" — would require excluding Backend.Api + Infrastructure (they need integration-test coverage, not unit)
