# StatsTid Sprint Log

> **Governance**: The Sprint Log is a formal governance artifact. Only the Orchestrator may create, modify, or approve sprint log entries. Agents report task completion to the Orchestrator, who validates and records them here.

## Sprint Index

| Sprint | Title | Status | Dates | Tests | Orchestrator Approved |
|--------|-------|--------|-------|-------|----------------------|
| [Sprint 1](SPRINT-1.md) | Foundation: Event Sourcing, Docker Skeleton, First Rule | complete | 2026-01-13 → 2026-01-17 | 12 | yes |
| [Sprint 2](SPRINT-2.md) | Rule Engine Expansion, OK Versions, Frontend Scaffold | complete | 2026-01-20 → 2026-01-31 | 74 | yes |
| [Sprint 3](SPRINT-3.md) | Security, Audit, Validation, CI/CD | complete | 2026-02-10 → 2026-02-21 | 103 | yes |
| [Sprint 4](SPRINT-4.md) | Payroll Traceability Chain, Absence Completion | complete | 2026-03-02 → 2026-03-02 | 133 | yes |
| [Sprint 5](SPRINT-5.md) | On-Call Duty, Flex Unification, Retroactive Corrections, SLS Export | complete | 2026-03-02 → 2026-03-02 | 158 | yes |
| [Sprint 6](SPRINT-6.md) | RBAC with Organizational Hierarchy | complete | 2026-03-03 → 2026-03-03 | 179 | yes |
| [Sprint 7](SPRINT-7.md) | Local Config, Period Approval, Org-Scope Enforcement | complete | 2026-03-04 → 2026-03-04 | 217 | yes |
| [Sprint 8](SPRINT-8.md) | Frontend: Design System + Role-Based UI | complete | 2026-03-04 → 2026-03-04 | 242 | yes |
| [Sprint 9](SPRINT-9.md) | Skema: Monthly Spreadsheet + Timer + Two-Step Approval | complete | 2026-03-05 → 2026-03-05 | 275 | yes |
| [Sprint 10](SPRINT-10.md) | Tech Debt Cleanup + Rule Engine Expansion | complete | 2026-03-06 → 2026-03-06 | 304 | yes |
| [Sprint 11](SPRINT-11.md) | Retroactive Corrections + AC Position Overrides + Academic Norms | complete | 2026-03-08 → 2026-03-08 | 306 | yes |
| [Sprint 12](SPRINT-12.md) | Database-Backed Agreement Configuration Management | complete | 2026-03-08 → 2026-03-08 | 334 | yes |
| [Sprint 13](SPRINT-13.md) | Employee Experience: Unified "Min Tid" Page | complete | 2026-03-08 → 2026-03-08 | 387 | yes |
| [Sprint 14](SPRINT-14.md) | Position Override + Wage Type Mapping UI | complete | 2026-03-08 → 2026-03-08 | 406 | yes |
| [Sprint 15](SPRINT-15.md) | Entitlement & Balance Management | complete | 2026-03-09 → 2026-03-09 | 422 | yes |
| [Sprint 16](SPRINT-16.md) | Working Time Compliance (EU WTD) | complete | 2026-03-11 → 2026-03-11 | 436 | yes |
| [Sprint 17](SPRINT-17.md) | Overtime Governance & Compensation Model | complete | 2026-03-11 → 2026-03-11 | 446 | yes |

## Cumulative Task Summary

| Sprint | Tasks | Components Touched | KB Entries Produced |
|--------|-------|--------------------|---------------------|
| S1 | 6 | Infrastructure, SharedKernel, Rule Engine, Integrations, Backend API, Orchestrator, Tests | ADR-001, ADR-002, ADR-004, ADR-005, ADR-006, PAT-001, PAT-004, DEP-003 |
| S2 | 8 | Rule Engine, SharedKernel, Infrastructure, Frontend, Tests | ADR-003, PAT-002, PAT-003, DEP-001, DEP-002, RES-001 |
| S3 | 7 | Security, Infrastructure, Backend API, Frontend, Tests, CI/CD | ADR-007, PAT-004 (extended) |
| S4 | 7 | Rule Engine, SharedKernel, Payroll Integration, Infrastructure, Tests | PAT-005 |
| S5 | 7 | Rule Engine, SharedKernel, Payroll Integration, Infrastructure, Tests | PAT-006 |
| S6 | 8 | SharedKernel, Infrastructure, Security, Backend API, PostgreSQL, Tests | ADR-008, ADR-009, ADR-010 |
| S7 | 9 | Infrastructure, Security, Backend API, Payroll Integration, PostgreSQL, Tests | — (used existing ADR-008/009/010) |
| S8 | 17 | Frontend (styles, components, contexts, lib, hooks, pages, guards, routing, tests) | — (consumed ADR-011) |
| S9 | 10 | SharedKernel, Infrastructure, Backend API, PostgreSQL, Frontend, Tests | ADR-012, FAIL-001 |
| S10 | 10 | SharedKernel, Rule Engine, Infrastructure, Payroll Integration, PostgreSQL, Tests | PAT-003 (updated) |
| S11 | 10 | SharedKernel, Rule Engine, Infrastructure, Payroll Integration, Backend API, PostgreSQL, Knowledge Base, Tests | ADR-013 |
| S12 | 16 | SharedKernel, Infrastructure, Backend API, PostgreSQL, Frontend, Tests | ADR-014 |
| S13 | 5 | Backend API, Frontend, Tests | — |
| S14 | 12 | SharedKernel, Infrastructure, Backend API, Payroll Integration, PostgreSQL, Frontend, Tests | — |
| S15 | 10 | SharedKernel, Rule Engine, Infrastructure, Backend API, PostgreSQL, Frontend, Tests | — |
| S16 | 13 | SharedKernel, Rule Engine, Infrastructure, Backend API, PostgreSQL, Frontend, Tests | ADR-015 |
| S17 | 13 | SharedKernel, Rule Engine, Infrastructure, Backend API, Payroll Integration, PostgreSQL, Frontend, Tests | — |
| **Total** | **168** | — | **27 entries** |

## Test Progression

| Sprint | Unit | Regression | Smoke | Total |
|--------|------|------------|-------|-------|
| S1 | 12 | 0 | 4 | 12 |
| S2 | 74 | 0 | 4 | 74 |
| S3 | 97 | 6 | 4 | 103 |
| S4 | 122 | 11 | 4 | 133 |
| S5 | 143 | 15 | 4 | 158 |
| S6 | 164 | 15 | 4 | 179 |
| S7 | 202 | 15 | 4 | 217 |
| S8 | 202 + 25 FE | 15 | 4 | 242 |
| S9 | 227 + 33 FE | 15 | 4 | 275 |
| S10 | 256 + 33 FE | 15 | 4 | 304 |
| S11 | 258 + 33 FE | 15 | 4 | 306 |
| S12 | 286 + 33 FE | 15 | 4 | 334 |
| S13 | 296 + 38 FE | 15 | 4 | 387 |
| S14 | 353 + 41 FE | 15 | 4 | 406 |
| S15 | 407 + 41 FE | 15 | 4 | 422 |
| S16 | 421 + 41 FE | 15 | 4 | 436 |
| S17 | 431 + 41 FE | 15 | 4 | 446 |

## Architectural Constraint Coverage

Shows which priorities were verified in each sprint.

| Priority | Description | S1 | S2 | S3 | S4 | S5 | S6 | S7 | S8 | S9 | S10 | S11 | S12 | S13 | S14 | S15 | S16 |
|----------|-------------|----|----|-----|-----|-----|-----|-----|-----|-----|------|------|------|------|------|------|------|
| P1 | Architectural integrity | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| P2 | Deterministic rule engine | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | — | — | — | ✓ | ✓ | ✓ | — | — | ✓ | ✓ | ✓ |
| P3 | Event sourcing auditability | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | — | ✓ | ✓ | ✓ | ✓ | — | ✓ | ✓ | ✓ | ✓ |
| P4 | OK version correctness | — | ✓ | ✓ | ✓ | ✓ | — | — | — | — | ✓ | ✓ | ✓ | — | — | — | ✓ | ✓ |
| P5 | Integration isolation | ✓ | ✓ | ✓ | ✓ | ✓ | — | ✓ | — | — | ✓ | ✓ | ✓ | — | — | ✓ | ✓ | ✓ |
| P6 | Payroll correctness | ✓ | ✓ | ✓ | ✓ | ✓ | — | ✓ | — | — | ✓ | ✓ | ✓ | — | ✓ | — | — | ✓ |
| P7 | Security and access control | — | — | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| P8 | CI/CD enforcement | — | — | ✓ | — | — | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| P9 | Usability and UX | — | ✓ | ✓ | — | — | — | — | ✓ | ✓ | — | — | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

## Legal & Payroll Verification Status

| Check | S1 | S2 | S3 | S4 | S5 | S6 | S7 | S8 | S9 | S10 | S11 | S12 | S13 | S14 | S15 | S16 |
|-------|----|----|-----|-----|-----|-----|-----|-----|-----|------|------|------|------|------|------|------|
| Agreement rules match legal requirements | ✓ | ✓ | ✓ | ✓ | ✓ | N/A | N/A | N/A | N/A | ✓ | ✓ | ✓ | N/A | N/A | ✓ | ✓ | ✓ |
| Wage type mappings correct | ✓ | ✓ | ✓ | ✓ | ✓ | N/A | N/A | N/A | Partial | ✓ | ✓ | N/A | N/A | N/A | N/A | N/A | ✓ |
| Overtime/supplement determinism | — | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | N/A | N/A | ✓ | ✓ | ✓ | N/A | N/A | N/A | N/A | ✓ |
| Absence effects correct | — | ✓ | ✓ | ✓ | ✓ | N/A | N/A | N/A | N/A | ✓ | ✓ | N/A | N/A | N/A | ✓ | N/A | N/A |
| Retroactive recalculation stable | — | ✓ | ✓ | ✓ | ✓ | N/A | N/A | N/A | N/A | ✓ | ✓ | N/A | N/A | N/A | N/A | N/A | N/A |

## Agent Effectiveness Metrics

Tracks agent quality signals to enable data-driven improvement of prompts and governance. See CLAUDE.md "Agent Effectiveness Metrics" for definitions.

| Sprint | Tasks | Constraint Violations | Reviewer Findings | Re-dispatches | First-Pass Rate |
|--------|-------|-----------------------|-------------------|---------------|-----------------|
| S1 | 6 | N/A (pre-validator) | N/A (pre-reviewer) | 0 | 100% |
| S2 | 8 | N/A | N/A | 0 | 100% |
| S3 | 7 | N/A | N/A | 0 | 100% |
| S4 | 7 | N/A | N/A | 0 | 100% |
| S5 | 7 | N/A | N/A | 0 | 100% |
| S6 | 8 | N/A | N/A | 0 | 100% |
| S7 | 9 | N/A | 2B | 2 | 78% |
| S8 | 17 | N/A | N/A | 0 | 100% |
| S9 | 10 | N/A | N/A | 0 | 100% |
| S10 | 10 | N/A | N/A | 0 | 100% |
| S11 | 10 | N/A | 1W | 1 | 90% |
| S12 | 16 | N/A | 1W, 1N | 0 | 100% |
| S13 | 5 | N/A | N/A | 0 | 100% |
| S14 | 12 | N/A | 1W, 1N | 0 | 100% |
| S15 | 10 | N/A | 2W | 1 | 90% |
| S16 | 13 | N/A | 1N | 0 | 100% |
| S17 | 13 | N/A | N/A | 0 | 100% |

**Notes**: Constraint Validator introduced in governance update after S15. Historical data marked N/A. Reviewer introduced in S7. S7 had 2 BLOCKERs (Backend→Payroll ref, seed data constraint) both requiring re-dispatch. S11 had 1 WARNING (missing config fields) requiring fix. S15 had 2 WARNINGs (PAT-005 violation, TOCTOU race) with 1 re-dispatch. S16 had 1 NOTE (ADR-015 pattern — non-blocking). S17: all agents produced buildable output; Orchestrator fixed API signature mismatches during merge (no re-dispatch needed).

**Cumulative First-Pass Rate**: 157/168 = 93.5%

## How to Use This Log

### For the Orchestrator
1. At sprint start, copy `TEMPLATE.md` to `SPRINT-N.md`
2. Fill in sprint metadata and goal
3. As agents complete tasks, add TASK entries with validation criteria
4. At sprint end, verify all constraints, run build/test, mark as approved
5. Update this INDEX.md with the new sprint row

### For Agents
- Agents do not write to sprint logs directly
- Agents report task completion to the Orchestrator with:
  - Files changed
  - Validation evidence (test results, build output)
  - Any proposed KB entries
- The Orchestrator records the task in the sprint log after validation
