# StatsTid Quality Grading

> **Governance**: Updated by the Orchestrator at sprint end or during entropy scan. See CLAUDE.md "Quality Grading" section for grade definitions.

## Domain Quality Matrix

Last updated: Sprint 17 (2026-03-11)

| Domain | Test Coverage | Pattern Compliance | Documentation | Tech Debt | Grade | Trend |
|--------|-------------|-------------------|---------------|-----------|-------|-------|
| Rule Engine | High — 8 rules with dedicated tests, determinism proofs | Full — pure functions, no I/O (ADR-002) | Strong — ADR-002, ADR-003, PAT-002, PAT-003, PAT-006, RES-001 | Low | **A** | ● |
| SharedKernel (Models) | High — immutability tests, config tests, balance tests | Full — init-only properties (PAT-001) | Good — PAT-001 | Low | **A** | ● |
| SharedKernel (Events) | Medium — registered in EventSerializer, but no dedicated event tests | Full — DomainEventBase pattern (PAT-004, DEP-003) | Good | Low | **B+** | ● |
| Infrastructure | Medium — repositories not directly unit-tested (integration-level) | Full — Npgsql pattern, seeder pattern (ADR-014) | Good — ADR-001, ADR-004, ADR-008 | Low | **B** | ● |
| Security | Low — no dedicated security unit tests; coverage via integration paths | Full — JWT, RBAC, scope validation (ADR-007, ADR-009) | Good — ADR-007, ADR-009, FAIL-001 | Medium — FindAll fix was late-caught | **B-** | ● |
| Backend API | Medium — endpoint logic tested indirectly via smoke tests | Mostly — PAT-005 violation fixed in S15, some inline logic remains | Partial — endpoint groups documented in MEMORY, no dedicated docs | Medium — some pages still use local fetch patterns | **B-** | ▲ |
| Payroll Integration | Medium — mapping tests, SLS format tests, correction tests, compensation mapping | Full — traceability chain (PAT-005), correction format (ADR-013), compensation-aware mapping | Good — PAT-005, PAT-006, DEP-002 | Low — position-aware PeriodCalc deferred | **B+** | ▲ |
| Frontend | Low — 41 vitest tests, no E2E, no visual regression | Partial — some pages use local fetch instead of shared hooks | Sparse — ADR-011 covers design system, no component docs | Medium — CORS fixes were reactive, some pages inconsistent | **C+** | ▲ |
| PostgreSQL Schema | N/A (schema, not code) | Full — unique constraints, indexes, seed data | Partial — init.sql is self-documenting, no ER diagram | Low | **B** | ● |
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

| Domain | S14 | S15 | S17 |
|--------|-----|-----|-----|
| Rule Engine | A | A | A |
| SharedKernel (Models) | A | A | A |
| SharedKernel (Events) | B+ | B+ | B+ |
| Infrastructure | B | B | B |
| Security | B- | B- | B- |
| Backend API | C+ | B- | B- |
| Payroll Integration | B | B | B+ |
| Frontend | C | C+ | C+ |
| PostgreSQL Schema | B | B | B |
| Docker/Infrastructure | B+ | B+ | B+ |
