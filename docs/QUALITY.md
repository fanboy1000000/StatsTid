# StatsTid Quality Grading

> **Governance**: Updated by the Orchestrator at sprint end or during entropy scan. See CLAUDE.md "Quality Grading" section for grade definitions.

## Domain Quality Matrix

Last updated: Sprint 30 (2026-05-16)

| Domain | Test Coverage | Pattern Compliance | Documentation | Tech Debt | Grade | Trend |
|--------|-------------|-------------------|---------------|-----------|-------|-------|
| Rule Engine | High — 16 segmentation-classified rules + 1 out-of-scope, multi-mode decomposition tests, determinism proofs | Full — pure functions, no I/O (ADR-002, now enforced at assembly graph level post-S19) | Strong — ADR-002, ADR-003, PAT-002, PAT-003, PAT-006, RES-001, ADR-015, ADR-016 | Low | **A** | ● |
| SharedKernel (Models) | High — immutability tests, config tests, balance tests; additive ManifestId fields (S20); LocalAgreementProfile + AlignmentPolicies + LegacyKeyToColumn (S21) | Full — init-only properties (PAT-001) | Good — PAT-001, ADR-017 | Low | **A** | ● |
| SharedKernel (Events) | Medium-High — registered in EventSerializer (45 types after S21), reflection-based coverage test, manifest creation/replay tests exercise SegmentManifestCreated + LocalAgreementProfileChanged end-to-end | Full — DomainEventBase pattern (PAT-004, DEP-003) | Good — ADR-016 D10 (manifest persistence), ADR-017 D6 (profile event shape) | Low | **A-** | ● |
| SharedKernel (Segmentation) | High — 6 cell tests (5 populated `(span × split-behavior)` pairs), 4 D9 invariant negatives, 8 boundary scenarios, perf budget; LocalProfileActivation cause + hydration test (S21) | Full — internal ctor + InternalsVisibleTo gate, PlannerInvariantViolation pattern, geometric invariants in ctor (ADR-016 D9), additive BoundarySources extension (S21) | Strong — ADR-016 (D1–D11), ADR-017 D9 (boundary integration) | Low | **A** | ● |
| Infrastructure | Medium-High — repositories not directly unit-tested (integration-level), SegmentManifestProjectionRebuilder (S20), LocalAgreementProfileRepository + LocalAgreementProfileMigrator + ConfigResolutionService rewrite (S21) — exercised via 18 Docker-gated profile tests | Full — Npgsql pattern, seeder pattern (ADR-014), audit middleware stamps manifest_id (S20), partial-unique-index lifecycle pattern (ADR-017 D1) | Good — ADR-001, ADR-004, ADR-008, ADR-016 D10, ADR-017 | Low — Step-7a residual close-then-insert window math edge case → Phase-4 carry-forward | **B+** | ▲ |
| Security | Low — no dedicated security unit tests; coverage via integration paths | Full — JWT, RBAC, scope validation (ADR-007, ADR-009); StatsTid.Auth assembly post-S19; ETag/If-Match D2.1 concurrency added on profile PUT (S21) | Good — ADR-007, ADR-009, FAIL-001, ADR-017 D2.1 | Medium — FindAll fix was late-caught; ETag pattern is on profiles only, propagation deferred (D2.2 carry-forward) | **B-** | ● |
| Backend API | Medium — endpoint logic tested indirectly via smoke tests; ProfileAlignmentValidator unit tests + ConfigEndpoints PUT/GET/history covered indirectly via 18 Docker-gated profile tests (S21) | Mostly — PAT-005 violation fixed in S15, ProfileAlignmentValidator pulls static maps from SharedKernel (ADR-017 D9a) keeping endpoint thin | Partial — endpoint groups documented in MEMORY, ADR-017 D5 documents profile API surface | Medium — some pages still use local fetch patterns; tests #11/#12/#16 hand-roll PUT pipeline (Phase-4 carry-forward) | **B** | ▲ |
| Payroll Integration | High — mapping tests, SLS format tests, correction tests, compensation mapping, mixed-version export, manifest creation/replay/projection-rebuild tests, boundary scenarios; LocalProfileActivation hydration test (S21); WTM marquee replay-determinism test (S29) | Full — traceability chain (PAT-005), correction format (ADR-013), compensation-aware mapping, planner-driven calculation (ADR-016 D1, D8), per-line OK stamping (S20), profile-activation hydration (ADR-017 D9c); WTM versioned history + export-time effective-date lookup closes ADR-016 D10 for WTM (ADR-018 D14, S29) | Strong — PAT-005, PAT-006, DEP-002, ADR-016, ADR-017 D9c, ADR-018 D14, ADR-020 | Low — `/calculate-and-export` is last `[Obsolete]` shim customer | **A** | ▲ |
| Frontend | Low-Medium — 48 vitest tests (was 41 pre-S21; +7 from MondayDatePicker + ProfileEditor), no E2E, no visual regression | Partial — some pages use local fetch instead of shared hooks; ProfileEditor uses shared `useConfig` hook | Sparse — ADR-011 covers design system, no component docs; ADR-017 documents profile editor scope | Medium — CORS fixes were reactive, some pages inconsistent; ProfileEditor explicitly basic (Phase-5 polish deferral) | **C+** | ● |
| PostgreSQL Schema | N/A (schema, not code) | Full — unique constraints, indexes, seed data; segment_manifests + GIN index (S20); local_agreement_profiles partial-unique-index `WHERE effective_to IS NULL` + local_agreement_profile_audit (S21) | Partial — init.sql is self-documenting, no ER diagram; migration plan documented in SPRINT-21.md | Low | **B+** | ● |
| Docker/Infrastructure | N/A (config, not code) | Full — 8-service compose (ADR-006) | Good — ADR-006 | Low | **B+** | ● |

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

1. **Frontend (C+)**: Needs E2E tests, shared hook refactoring, component documentation
2. **Security (B-)**: Needs dedicated security unit tests (auth flow, scope validation, claim parsing)
3. **Backend API (B-)**: Should extract remaining inline logic to proper service layers

## Historical Grades

| Domain | S14 | S15 | S17 | S20 | S21 | S29 | S30 |
|--------|-----|-----|-----|-----|-----|-----|-----|
| Rule Engine | A | A | A | A | A | A | A |
| SharedKernel (Models) | A | A | A | A | A | A | A |
| SharedKernel (Events) | B+ | B+ | B+ | A- | A- | A- | A- |
| SharedKernel (Segmentation) | — | — | — | A (new) | A | A | A |
| Infrastructure | B | B | B | B | B+ | B+ | **B+** (EntitlementConfigRepository gains 5 versioned-history methods with ADR-018 D5 `(conn, tx)` atomic-outbox threading + ADR-019 D8 audit version-transition + ADR-020 D2 3-case routing inheritance) |
| Security | B- | B- | B- | B- | B- | B- | B- |
| Backend API | C+ | B- | B- | B- | B | B | **B+** (↑ S30 closes Entitlement domain admin-CRUD gap via new `EntitlementConfigEndpoints` — 5 admin endpoints with `GlobalAdminOnly` RBAC + ADR-019 admin-strict If-Match contract + ADR-020 D2 3-case routing; matches the S25/S29 admin-CRUD discipline. Step 7a cycle 1 caught 2 sprint-defects in this domain — BalanceEndpoints supersession filter + frontend PUT payload — both fixed in `374960a` + `a2e8d83`. Remaining gap: handful of pages still use local fetch instead of shared hooks; documented as Phase-4e / Phase-5 polish.) |
| Payroll Integration | B | B | B+ | A- | A- | A | A |
| Frontend | C | C+ | C+ | C+ | C+ | C+ | C+ (new EntitlementConfigEditor admin page mirrors S25 admin-page shape, banner-with-retry on 412; vitest unchanged at 88 — no new tests added per S30 scope-trim, deferred to S31 polish; pre-existing `parseInt` truncation bug on decimal fields flagged at Step 7a cycle 2, deferred to S31) |
| PostgreSQL Schema | B | B | B | B+ | B+ | B+ | **A-** (↑ S30 closes `entitlement_configs` versioned-history gap with schema migration `s30-d2-ec-effective-dating` — `effective_from` + `effective_to` + partial-unique-index `WHERE effective_to IS NULL` + history-unique-index + new `entitlement_config_audit` table mirroring `wage_type_mapping_audit` post-S25 shape + ADR-019 D8 version-transition columns. Greenfield-baked CREATE TABLE + ledger-guarded migration block. Step 7a cycle 2 flagged legacy-upgrade-path ordering concern — forward-compat issue under pre-launch posture, deferred to Phase 4e/production-deploy.) |
| Docker/Infrastructure | B+ | B+ | B+ | B+ | B+ | B+ | B+ |
