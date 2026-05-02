# StatsTid Quality Grading

> **Governance**: Updated by the Orchestrator at sprint end or during entropy scan. See CLAUDE.md "Quality Grading" section for grade definitions.

## Domain Quality Matrix

Last updated: Sprint 20 (2026-05-02)

| Domain | Test Coverage | Pattern Compliance | Documentation | Tech Debt | Grade | Trend |
|--------|-------------|-------------------|---------------|-----------|-------|-------|
| Rule Engine | High — 16 segmentation-classified rules + 1 out-of-scope, multi-mode decomposition tests, determinism proofs | Full — pure functions, no I/O (ADR-002, now enforced at assembly graph level post-S19) | Strong — ADR-002, ADR-003, PAT-002, PAT-003, PAT-006, RES-001, ADR-015, ADR-016 | Low | **A** | ● |
| SharedKernel (Models) | High — immutability tests, config tests, balance tests; additive ManifestId fields (S20) | Full — init-only properties (PAT-001) | Good — PAT-001 | Low | **A** | ● |
| SharedKernel (Events) | Medium-High — registered in EventSerializer (44 types after S20), reflection-based coverage test, manifest creation/replay tests exercise SegmentManifestCreated end-to-end | Full — DomainEventBase pattern (PAT-004, DEP-003) | Good — ADR-016 D10 (manifest persistence) | Low | **A-** | ▲ |
| SharedKernel (Segmentation) | High — 6 cell tests (5 populated `(span × split-behavior)` pairs), 4 D9 invariant negatives, 8 boundary scenarios, perf budget | Full — internal ctor + InternalsVisibleTo gate, PlannerInvariantViolation pattern, geometric invariants in ctor (ADR-016 D9) | Strong — ADR-016 (D1–D11), full rule classification inventory | Low — perf budget covers cold-fast path only; SnapshotContract+boundary variant deferred | **A** | new |
| Infrastructure | Medium — repositories not directly unit-tested (integration-level), SegmentManifestProjectionRebuilder added (S20) | Full — Npgsql pattern, seeder pattern (ADR-014), audit middleware now stamps manifest_id (S20) | Good — ADR-001, ADR-004, ADR-008, ADR-016 D10 | Low | **B** | ● |
| Security | Low — no dedicated security unit tests; coverage via integration paths | Full — JWT, RBAC, scope validation (ADR-007, ADR-009); StatsTid.Auth assembly extracted post-S19 (b4fc670) — purity enforced by .NET assembly graph | Good — ADR-007, ADR-009, FAIL-001 | Medium — FindAll fix was late-caught | **B-** | ● |
| Backend API | Medium — endpoint logic tested indirectly via smoke tests | Mostly — PAT-005 violation fixed in S15, some inline logic remains | Partial — endpoint groups documented in MEMORY, no dedicated docs | Medium — some pages still use local fetch patterns | **B-** | ● |
| Payroll Integration | High — mapping tests, SLS format tests, correction tests, compensation mapping, mixed-version export, manifest creation/replay/projection-rebuild tests, boundary scenarios | Full — traceability chain (PAT-005), correction format (ADR-013), compensation-aware mapping, planner-driven calculation (ADR-016 D1, D8), per-line OK stamping at export boundary (S20) | Strong — PAT-005, PAT-006, DEP-002, ADR-016 (segmentation), retired OkVersionBoundary collapse + RecalculateWithVersionSplitAsync (S20) | Low — `/calculate-and-export` is last `[Obsolete]` shim customer (Step 0b W2 explicit out-of-scope); WTM snapshot deferred to Phase 4 | **A-** | ▲ |
| Frontend | Low — 41 vitest tests, no E2E, no visual regression | Partial — some pages use local fetch instead of shared hooks | Sparse — ADR-011 covers design system, no component docs | Medium — CORS fixes were reactive, some pages inconsistent | **C+** | ● |
| PostgreSQL Schema | N/A (schema, not code) | Full — unique constraints, indexes, seed data; segment_manifests table + GIN-indexed boundary_cause_summary (S20) | Partial — init.sql is self-documenting, no ER diagram; segment_manifests projection rebuild script added (S20) | Low | **B+** | ▲ |
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

| Domain | S14 | S15 | S17 | S20 |
|--------|-----|-----|-----|-----|
| Rule Engine | A | A | A | A |
| SharedKernel (Models) | A | A | A | A |
| SharedKernel (Events) | B+ | B+ | B+ | A- |
| SharedKernel (Segmentation) | — | — | — | A (new) |
| Infrastructure | B | B | B | B |
| Security | B- | B- | B- | B- |
| Backend API | C+ | B- | B- | B- |
| Payroll Integration | B | B | B+ | A- |
| Frontend | C | C+ | C+ | C+ |
| PostgreSQL Schema | B | B | B | B+ |
| Docker/Infrastructure | B+ | B+ | B+ | B+ |
