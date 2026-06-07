# StatsTid Quality Grading

<!-- anchor-sprint: 66 -->
> **Governance**: Updated by the Orchestrator at sprint end or during entropy scan. See CLAUDE.md "Quality Grading" section for grade definitions.

> **S66 (2026-06-07):** No grade changes; **Domain Correctness materially strengthens within B** — the consumption mechanism is now anchored to verified primary sources (LBK 230/2021 §6 stk.2 + Ferievejledning Ex. 3.5/§8.1/§8.4, read verbatim + 2 adversarial refute-lens verifications; `docs/references/vacation-consumption-mechanism-research.md`), the ADR-031 D6 LAUNCH GATE IS SATISFIED (ADR-032 D6 — with the gate's 5÷N premise refuted on the record rather than silently dropped), and consumption gained a single authoritative record (per-absence recorded feriedage; the six-site rounding seam Reviewer-verified consistent). **Backend API holds A** — the two-phase advisory-lock consumption contract closed two Codex-caught fail-opens in-lock (`ef81fd0`); one unguarded write path retired (POST /api/absences). **CI/Tooling holds A−** — the FAIL-002 flake class recurred at close (3× single disjoint container-sheds, signature verbatim); the owner-adjudicated 3-run + clean-consecutive close pattern is now precedent (SPRINT-66 § Test Summary). Test & QA: the new pin suite caught a LIVE wrong-stream defect (EntitlementBalanceRevalued) the same day it shipped — and refused to launder it; the S64 citation-gated discipline demonstrably holds.

> **S65 (2026-06-06):** No grade changes. **Backend API holds A** — the year-overview endpoint reuses every primitive (AccrualMath flat, ADR-028 work-time, per-day DailyNormCalculator extracted behavior-preservingly from the Skema seam — now ONE shared implementation, drift-proof) with a 4-cycle Step-7a hardening pass (per-ferieår dated agreement anchoring for historical reads incl. probe anchors; graceful never-500 paths pinned). **Frontend holds B** (the A-gap — no E2E/visual regression — is unchanged) but gains its strongest page-level suite yet: ArsoversigtPage 19 tests + hook race tests + 2 PATs (PAT-007 referentially-stable hook mocks; the server-today authority pattern test-pinned end-to-end); FE 173 vitest. **CI/Tooling holds A−** — the consecutive-run discipline caught a NEW environmental flake class, now KB-documented (FAIL-002: Docker Desktop sheds testcontainer starts under churn; close-protocol = fresh session + exclusive runs + full-log capture). Test & QA note: the S65 regression suite (+23) went through a Reviewer-driven fix-forward that strengthened under-asserting tests (raw-body determinism, non-trivial transferable operands, OK-discriminating local-profile straddle) — the "green suite that under-asserts" failure mode was caught at Step 5a, evidence the citation-gated discipline holds.

> **S57 (2026-05-31):** Frontend held at **B** but **pattern compliance improved materially** — the oes.dk re-skin completed the design-token system (defined ~14 previously-phantom tokens) and migrated all ~124 hardcoded hex colors to tokens, so ADR-011's "no hardcoded colors" mandate is now actually enforced (residual hardcoded-hex = 0). E2E/visual-regression remains the gap keeping it below A. New default palette is oes-derived + WCAG-AA (ADR-011 amended).

> **S58–S62 refresh (2026-06-03):** No grade changes. S58–S61 (per-day norm surfacing; child-sick/senior eligibility, ADR-029; MONTHLY_ACCRUAL activation, ADR-030; the Oversigt read-only dashboard + `AccrualMath` consolidation) and **S62** (piecewise per-month accrual, ADR-030 **D8**) all landed within established domains. **Rule Engine** holds **A++** — `AccrualMath.EarnedToDatePiecewise` is a pure SharedKernel function (no I/O/wall-clock), single-source-guarded, with the windowed-clamp + monotonicity + replay-determinism unit-proven (653 unit, +24). **Infrastructure** holds **B+** (new `GetFractionHistoryAsync` single-table read). **Backend API** holds **A** (3-site cutover; fail-closed + contracts preserved). The S62 integration tests are Docker-gated and **CI-pending** (Docker unavailable at close) — the one open verification item.

> **S63 (2026-06-04):** No grade changes; **Rule Engine A++ holds and SIMPLIFIES** — ADR-031 (supersedes ADR-030 D8) made the vacation day-count part-time-fraction-independent per Ferieloven §5, so the S62 piecewise surface (`EarnedToDatePiecewise`/`FractionPeriod`/`GetFractionHistoryAsync`, −602 lines, −24 unit tests) was deleted; the accrual is back to ONE flat pure fn (`EarnedToDate`, single-source-guarded, 629 unit green). **Backend API** holds **A** (3-site flat cutover; anchor-422 proven a strict superset of the removed S62 guard; contracts byte-preserved). **Infrastructure** holds **B+** (resolver shrank by the dead range-read). Step-7a clean cycle 1 both lenses. ~~Open verification item: the Docker-gated flat-cap/curve rewrites are CI-pending for the 3rd consecutive close (S61+S62+S63 sets)~~ → **CLEARED post-close 2026-06-04** (user-directed Docker-backlog run): the S61+S62+S63 sets are verified green locally — the flat 25-allow/26-reject bracket + unbent curve + fail-closed anchor-422 all proven against real DBs (one latent S62-authored boot-order test bug fixed; production code verified correct; see SPRINT-63 Post-Close Addendum). **NEW standing debt item (pre-existing, surfaced by the run): a ~47-test deterministic-failure cluster in pre-S60 Docker-gated Regression classes + a stale smoke RBAC assertion + CI's regression step RED on every master push since ≥ S57 (smoke job perpetually skipped behind it)** — candidate dedicated test-debt sprint; bisect-proven older than S60, NOT attributable to S58–S63 work.

## Domain Quality Matrix

Last updated: **Sprint 64 (2026-06-05)**.

> **S64 (2026-06-05):** **CI/Tooling B+ → A−** — the standing debt that capped the grade is cleared: the Docker-gated Regression suite is **424/424 green twice-consecutively** (pristine + consecutive; the pre-S60 deterministic cluster + flaky margin both resolved via the census at `docs/operations/s64-regression-debt-census.md`), Smoke is 5/5 (+1 deny-pin), and **master CI is whole-workflow green** for the first time since ≥ S57 (regression step now backed by a services-postgres; smoke job independent; the S63 mechanical close gates now operate against a green baseline). All fixes test-side with citation-gated assertions (zero laundering — 3 attempts caught); 2 product findings deferred with owner sign-off (OK-straddle export gap; segments_jsonb enum asymmetry — tracked in ROADMAP). Remaining gap to A: the ~19-min sequential suite runtime (accepted trade) and the deferred product follow-ups. All other grades hold. The per-cell detail in the matrix below reflects the **S35** assessment (most domains were not materially changed by the S36–S56 work — Rule Engine, SharedKernel Models/Segmentation, Payroll all held grade). The **"S36–S56 Refresh"** section immediately after the matrix records every grade change, the new domains, and corrected counts since S35. Where a grade changed, the **Grade** column below shows the current value with a `→` note.

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
| Frontend | Medium — 90 vitest tests (was 88 pre-S35; +2 from S35 UserManagement banner-with-retry), **now CI-enforced** (S39 TASK-3907); no E2E, no visual regression | Partial — some pages use local fetch instead of shared hooks; ProfileEditor uses shared `useConfig` hook | Sparse — ADR-011 covers design system, no component docs; ADR-017 documents profile editor scope | Medium — CORS fixes were reactive, some pages inconsistent; ProfileEditor explicitly basic (Phase-5 polish deferral) | **B** → (↑ from B- — S47 Phase-5 polish: 9 TS errors→0, shared-hook migration, toasts; S53 fixed 5 FE test failures; S54 two-level nav; 128 vitest tests, CI-enforced) | ▲ |
| PostgreSQL Schema | N/A (schema, not code) | Full — unique constraints, indexes, seed data; segment_manifests + GIN index (S20); local_agreement_profiles partial-unique-index `WHERE effective_to IS NULL` + local_agreement_profile_audit (S21) | Partial — init.sql is self-documenting, no ER diagram; migration plan documented in SPRINT-21.md | Low | **B+** | ● |
| Docker/Infrastructure | N/A (config, not code) | Full — 8-service compose (ADR-006); container healthchecks fixed in **S46** (curl installed in 7 Dockerfiles, closing the S39 TASK-3905 breakage) | Good — ADR-006 | Low | **A-** → (↑ from B+ — S46 healthcheck fix) | ▲ |
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

## S36–S56 Refresh (2026-05-31)

Delta since the S35 matrix freeze. This pass updates grades and counts; it does not re-author every cell's prose (most domains held).

**Held grade (no material change S36–S56):** Rule Engine (A++), SharedKernel Models (A), SharedKernel Segmentation (A), Payroll Integration (A), Security (B+), Backend API (A), Infrastructure (A). These domains saw additive work (ADR-026 audit-projection layer, ADR-027 reporting-line hierarchy, ADR-028 work-time persistence) that extended rather than re-shaped them.

**Grade changes:**
- **Frontend B- → B** (▲): S47 Phase-5 polish (9 TS errors → 0, shared-hook migration, toast notifications), S53 frontend-health fixes (5 failing tests), S54 two-level navigation restructure; **128 vitest tests** (was 90), CI-enforced since S39. Still no E2E / visual regression — keeps it below A.
- **Docker/Infrastructure B+ → A-** (▲): S46 installed curl in 7 Dockerfiles, fixing the container healthchecks that were broken everywhere (the S39 finding).

**New domains:**
- **Domain Correctness — B** (new): the S36–S37 Phase-A agreement audit (111-cell source register, 4 candidate bugs surfaced) + the S35/S37 seed corrections (AC `DefaultCompensationModel`, CHILD_SICK SLS remap, SENIOR_DAY quota). Held at B not higher because the **ADR-024 role-within-agreement rule-engine cutover (D1/D2) is SUSPENDED** since the S42a discipline-rollback (schema/repo shipped S40, dormant), and the source register is still **DRAFT** pending real Phase-B expert sign-off.
- **Reporting-Line & Approval Routing — A-** (new): ADR-027 shipped end-to-end across S48–S52 (temporal `reporting_lines` table, repository, 7 admin endpoints, HR bulk import, designated-approver routing with ACTING precedence, enforcement toggle, self-service delegation). Well-tested (D-tests through S52) and documented (ADR-027 D1–D12). A- not A pending the broader integration-test maturity the rest of Backend has.

**Corrected counts (were stale in the S35 matrix prose):**
- EventSerializer registered event types: **72** (was "58" at S35; grew via S40 +7 role-config, ADR-027 events S48–S52, ADR-028 S56).
- Frontend tests: **128** (was 90).
- Database tables: **53** (the schema doc had frozen at "32"; now generated by `tools/generate_db_schema.py`).
- KB entries: **40** (was tracked as 30 through S24).

**CI/Tooling (B+, held):** gained the doc-consistency gate this reconciliation added — `tools/check_docs.py` (db-schema sync, KB INDEX completeness, sprint-log inventory) + `tools/generate_db_schema.py`, wired into the CI `docs-consistency` job.

## Priority Improvement Areas

1. **Frontend (B)**: Still needs E2E / visual-regression tests and component documentation (the gap keeping it below A). S47/S53/S54 closed the shared-hook and TS-error gaps; vitest is CI-enforced (S39).
2. **Security (B+)**: Admin-strict If-Match propagation complete on all 4 admin write surfaces (S35). Remaining gap: no dedicated security unit-test suite — auth flow / scope validation / claim parsing tested only through integration paths.
3. **Backend API (A)**: Last unprotected admin write closed in S35. Remaining gap: legacy admin pages still inline fetch instead of using shared hooks (Phase 5 polish); production-readiness legacy-DB-upgrade sweep deferred to a coherent Phase 4e runbook sprint.
4. **Coverage gating** (new — S39): baseline recorded but `≥X%` gate not enforced. Strategy decision (no-regression / ratchet / hard-80%-blanket) deferred to post-launch sprint. See "Coverage Baseline" section.
5. **Container healthchecks** — ✅ **RESOLVED S46** (curl installed in 7 Dockerfiles; the S39 host-side CI workaround can be retired).
6. **Domain Correctness (B) — highest open correctness risk**: the ADR-024 role-within-agreement rule-engine cutover (D1/D2) is **SUSPENDED** since the S42a discipline-rollback — schema/repo shipped (S40) but no rule reads `employment_category`, so specialkonsulent/chefkonsulent merarbejde rules are not yet enforced. The agreement source register is **DRAFT** pending real Phase-B expert sign-off. Launch-blocking per ADR-024.
7. **QUALITY.md / metrics cadence**: this doc and the sprint INDEX cumulative-metrics matrices lapsed (S35 / S16–S24 respectively) before the 2026-05-31 refresh. Re-establish the sprint-end update step (WORKFLOW 5c) so they don't refreeze.

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

> **Note (2026-05-31):** the per-sprint historical columns above stop at **S35**; S36–S55 grades were not individually tracked (the assessment cadence lapsed). Rather than back-fill 20 fabricated columns, here is the current **S56 snapshot**:

| Domain | S56 Grade | vs S35 |
|--------|-----------|--------|
| Rule Engine | A++ | held |
| SharedKernel (Models) | A | held |
| SharedKernel (Events) | A- | held |
| SharedKernel (Segmentation) | A | held |
| Infrastructure | A | held |
| Security | B+ | held |
| Backend API | A | held |
| Payroll Integration | A | held |
| Frontend | B | ▲ from B- |
| PostgreSQL Schema | A- | held |
| Docker/Infrastructure | A- | ▲ from B+ |
| CI/Tooling | B+ | held |
| Domain Correctness | B | new |
| Reporting-Line & Approval Routing | A- | new |

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
