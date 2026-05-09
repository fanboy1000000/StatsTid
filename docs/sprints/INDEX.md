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
| [Sprint 18](SPRINT-18.md) | Codex BLOCKER Remediation (Round 1: OK version, wage mapping, endpoint auth, serializer coverage) | complete (5 deferred findings → S19) | 2026-04-18 → 2026-04-23 | 474 | yes — 2026-04-23 |
| [Sprint 19](SPRINT-19.md) | Codex BLOCKER Remediation (Round 2: `/execute` and `/calculate-and-export` resource-scope, retroactive audit canonicalization, JWT env-var) | complete | 2026-04-25 → 2026-04-28 | 493 | yes — 2026-04-28 |
| [Sprint 20](SPRINT-20.md) | Temporal Period Handling (ADR-016 D1-D11 + Segmentation bounded context + planner-driven PCS + manifest projection + per-line OK-version stamping) | complete | 2026-04-29 → 2026-05-02 | 562 | yes — 2026-05-02 |
| [Sprint 21](SPRINT-21.md) | Local Agreement Configuration Rework (ADR-017 D1-D11 + profile schema + repository + migration runner + ConfigEndpoints rewrite + PCS hydration + UX profile editor + 18-scenario D11 test matrix + Step 7a 10-cycle review) | complete | 2026-05-02 → 2026-05-03 | 618 | yes — 2026-05-03 |
| [Sprint 22](SPRINT-22.md) | Transactional Outbox + Row-Version Optimistic Concurrency (Phase 4a — atomic exemplar in `ConfigEndpoints` PUT; ADR-018 D1-D12) | complete | 2026-05-03 → 2026-05-05 | 650 | yes — 2026-05-05 |
| [Sprint 23](SPRINT-23.md) | Phase 4b Publisher Hardening (outbox max-attempts cap + correlation_id parser + frontend ETag fallback + repo-level no-op short-circuit + 412 try/catch + weak ETag + 3 deferred D12 tests + Step-7a cycle-2 FIFO fix) | complete | 2026-05-06 → 2026-05-06 | 697 | yes — 2026-05-06 |
| [Sprint 24](SPRINT-24.md) | Phase 4c Part 1: Atomic Outbox Site Propagation (TASK-2206 redo — 7 repo `(conn, tx)` overloads + 21 endpoint sites converted across 6 endpoint files + 21-test ForcedRollbackHarness D-test suite + AGENTS.md cross-domain authorization governance edit + Step 7a cycle 1 P1 publish-race fix + cycle 2 P2 post-commit-tolerance fix) | complete | 2026-05-07 → 2026-05-07 | 741 | yes — 2026-05-07 |
| [Sprint 25](SPRINT-25.md) | Phase 4c Part 2 D2.2 ETag/If-Match Propagation (ADR-019 D1-D8 — admin-strict resources gain row-version + If-Match optimistic concurrency; AgreementConfig DRAFT update + publish + archive lifecycle; PositionOverride update + activate/deactivate; WageTypeMapping update + DELETE-204 with composite key; entitlement_configs schema-only forward-compat; per-surface SaveResult records; v3 mutating overloads; audit version-transition columns; frontend apiFetchWithEtag + 4 admin pages + 12 frontend vitest; 23 Docker-gated D-tests + Step 7a cycle 1 B1 ArchivedVersion fix) | complete | 2026-05-08 → 2026-05-08 | 777 | yes — 2026-05-08 |
| [Sprint 26](SPRINT-26.md) | Phase 4c.5 Atomic Outbox Final Sweep (ADR-018 D6 stream-naming retabulate + 2 net-new event types `OvertimePreApprovalApproved/Rejected` + EntitlementBalanceRepository (conn, tx) overload + OvertimePreApprovalRepository (conn, tx) overload + Admin atomic prototype + 5 Admin sites + Overtime atomic w/ new-event emission + Step 7a cycle 1 reverted Skema/Time atomic w/o projection + B3 CheckAndAdjustAsync auto-create-row fix; cycle-2 P1 SkemaEndpoints quota race deferred to Phase 4c.6) | complete (with Phase 4c.6 carry-forward) | 2026-05-08 → 2026-05-08 | 782 | yes — 2026-05-08 |
| [Sprint 27](SPRINT-27.md) | Phase 4c.6 Read-Path Projection Tables for Skema/Time/Balance/Compliance + Atomic-Outbox Re-attempt (ADR-018 D13 sync-in-tx canonical pattern + `time_entries_projection` + `absences_projection` + projection repos + ProjectionBackfillService + WebApplicationFactory test harness + IOutboxEnqueue.EnqueueAndReturnIdAsync overload + Skema/Time atomic POST + Skema/Time/Balance/Compliance GET migration + SkemaQuotaBreachException 422 with whole-bundle-rollback + 13 D-tests including marquee publisher-stall RYW + Step 7a cycle 1 P1 #1 auto-backfill on startup fix + cycle 2 P2 hours NUMERIC(8,4) precision fix; P1 #2 pre-S22 ordering deferred to Phase 4e) | complete | 2026-05-09 → 2026-05-09 | 795 | yes — 2026-05-09 |
| [Sprint 28](SPRINT-28.md) | ADR-020 Design Sprint (Phase 4d-1 Pre-Work) — design-only sprint produced ADR-020 settling 3 architectural questions (D1 planner-level enrollment for non-rule replay inputs with 5 binding components; D2 soft-delete-then-create gap-acknowledging 3-case routing; D3 seed idempotency via ON CONFLICT (natural_key, effective_from)) + ADR-019 D3 amendment for KB consistency + 2-cycle in-sprint dual-lens ADR review per Step 7a-equivalent. Splitting precedent flagged: design-only-as-separate-sprint-number is one-off justification (deferred Phase 4d-1 thrash demanded architectural reset before implementation), NOT new convention. Second clean application of `feedback_thrash_defer_real_world.md` (lens divergence in inverse direction). | complete | 2026-05-09 → 2026-05-09 | 795 (unchanged from S27 — design-only sprint) | yes — 2026-05-09 |

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
| S18 | 8 | SharedKernel, Infrastructure, Backend API, Auth, Payroll Integration, Tests | — (carry-forward to S19) |
| S19 | 4 | Orchestrator, Payroll Integration, Auth, Tests | — (TASK-1903 absorbed into S20) |
| S20 | 17 | SharedKernel (Segmentation, new context), Infrastructure, Backend API, Payroll Integration, PostgreSQL, Tests | ADR-016 |
| S21 | 10 | SharedKernel, Infrastructure, Backend API, Payroll Integration, PostgreSQL, Frontend, Tests | ADR-017 |
| S22 | 8 | SharedKernel, Infrastructure (outbox), Backend API, PostgreSQL, Frontend, Tests | ADR-018 |
| S23 | 5 | Infrastructure (outbox + repo), Backend API, Frontend (lib + api), Tests, Sprint log | — (Step 7a cycle 1 P1 absorbed in cycle 2 fix; no new ADR/PAT) |
| S24 | 8 | Infrastructure (7 repos: conn-tx overloads), Backend API (6 endpoint files atomic outbox conversion), Tests (TxContractTests + ForcedRollbackHarness + 6 atomic test classes), Governance (AGENTS.md cross-domain authorization), Sprint log | — (no new ADR; propagates ADR-018 D2/D3/D5 only) |
| **Total** | **228** | — | **30 entries** |

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
| S18 | 459 + 41 FE | 15 | 4 | 474 |
| S19 | 478 + 41 FE | 15 | 4 | 493 |
| S20 | 513 + 41 FE | 32 plain + 17 Docker | 4 | 562 (without Docker: 545) |
| S21 | 517 + 48 FE | 35 plain + 18 Docker (profile) | 4 | 618 (without Docker: 600) |
| S22 | 517 + 48 FE | 35 plain + 50 Docker (18 S21 + 16 S22 + 17 S20 segmentation) | 4 | 650 (without Docker: 600) |
| S23 | 525 + 76 FE | 35 plain + 61 Docker (50 S22 + 11 S23) | 4 | 697 (without Docker: 636) |
| S24 | 525 + 76 FE | 35 plain + 105 Docker (61 S23 + 23 S24 TxContractTests + 21 S24 ForcedRollback) | 4 | 741 (without Docker: 636) |

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

| Sprint | Tasks | Constraint Violations | Reviewer Findings | External Findings | Re-dispatches | First-Pass Rate |
|--------|-------|-----------------------|-------------------|-------------------|---------------|-----------------|
| S1 | 6 | N/A (pre-validator) | N/A (pre-reviewer) | N/A (pre-external) | 0 | 100% |
| S2 | 8 | N/A | N/A | N/A | 0 | 100% |
| S3 | 7 | N/A | N/A | N/A | 0 | 100% |
| S4 | 7 | N/A | N/A | N/A | 0 | 100% |
| S5 | 7 | N/A | N/A | N/A | 0 | 100% |
| S6 | 8 | N/A | N/A | N/A | 0 | 100% |
| S7 | 9 | N/A | 2B | N/A | 2 | 78% |
| S8 | 17 | N/A | N/A | N/A | 0 | 100% |
| S9 | 10 | N/A | N/A | N/A | 0 | 100% |
| S10 | 10 | N/A | N/A | N/A | 0 | 100% |
| S11 | 10 | N/A | 1W | N/A | 1 | 90% |
| S12 | 16 | N/A | 1W, 1N | N/A | 0 | 100% |
| S13 | 5 | N/A | N/A | N/A | 0 | 100% |
| S14 | 12 | N/A | 1W, 1N | N/A | 0 | 100% |
| S15 | 10 | N/A | 2W | N/A | 1 | 90% |
| S16 | 13 | N/A | 1N | N/A | 0 | 100% |
| S17 | 13 | N/A | N/A | N/A | 0 | 100% |
| S22 | 8 | 0 | 0B/0W/0N | Step-7a 3 cycles: cycle 1 P2 + cycle 2 P1 + cycle 3 cap-halt with 2 P2 routed forward | 0 | 100% |
| S23 | 5 | 0 (Small Tasks Exception) | n/a (Reviewer skipped — pure tech-debt) | Step-0b 1 cycle: 1B/3W/2N (BLOCKER fixed before code); Step-7a 2 cycles: cycle 1 P1 BLOCKER (FIFO regression) + cycle 2 clean | 0 | 100% |
| S24 | 8 | 0 | TASK-2401 Reviewer 0B/0W/5N; Phase 2 bundled Reviewer 0B/1W/6N (W absorbed: explicit tx.RollbackAsync consistency; 4 NOTEs deferred to Phase 4d as TOCTOU hardening) | Step 0b 2 cycles: cycle 1 2B/3W/1N + cycle 2 B1 re-flagged → fixed via AGENTS.md cross-domain governance edit (B2 fixed cleanly cycle 1); Step 7a 2 cycles: cycle 1 P1 publish-race + cycle 2 P2 post-commit-tolerance regression from cycle 1's defensive overreach, both fixed in-sprint | 0 | 100% |

**Notes**: Constraint Validator introduced in governance update after S15. Historical data marked N/A. Reviewer introduced in S7. External Review (Codex) introduced in governance update after S17 — active from S18 onward. S7 had 2 BLOCKERs (Backend→Payroll ref, seed data constraint) both requiring re-dispatch. S11 had 1 WARNING (missing config fields) requiring fix. S15 had 2 WARNINGs (PAT-005 violation, TOCTOU race) with 1 re-dispatch. S16 had 1 NOTE (ADR-015 pattern — non-blocking). S17: all agents produced buildable output; Orchestrator fixed API signature mismatches during merge (no re-dispatch needed). S22 Step-7a hit cycle-cap discipline at cycle 3 with 2 P2 cascade findings routed to Phase 4b (S23). S23 absorbed all 4 cycle-3 + 3 NOTE carry-forwards from S22; Step-7a cycle 1 caught a load-bearing P1 (FIFO regression in TASK-2301) that the Step-0b plan-mode review missed — fixed cycle 2, verified clean.

**Cumulative First-Pass Rate**: 178/189 = 94.2% (S22 + S23 + S24 added with 100% first-pass — no agent re-dispatches; all three hit Step 7a findings instead, which are external-review signals not first-pass-rate signals). S24 lesson: forgot to commit Phase 1 before dispatching Phase 2 worktrees; 5 of 6 worktrees correctly halted or auto-recovered, 1 halted cleanly. Recovery via `git checkout <branch> -- <file>` cherry-pick of endpoint-only changes worked cleanly. **Future sprints**: commit Phase 1 BEFORE dispatching Phase 2 worktrees.

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
