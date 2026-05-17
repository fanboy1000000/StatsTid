# Sprint 33 — Phase 4d-3 Part 2 Implementation (ADR-023 cutover)

| Field | Value |
|-------|-------|
| **Sprint** | 33 |
| **Status** | in-progress |
| **Start Date** | 2026-05-17 |
| **End Date** | — |
| **Orchestrator Approved** | no |
| **Build Verified** | no |
| **Test Verified** | no |
| **Sprint-start commit base** | `55b082b` (S32 close, 2026-05-16) |
| **Sprint type** | Implementation (against binding ADR-023 D1-D8) |
| **Refinement** | `.claude/refinements/REFINEMENT-s33-phase-4d3-part2-impl.md` (READY after 3-cycle dual-lens; gitignored) |
| **Plan** | `.claude/plans/PLAN-s33.md` (Step 0a) |

## Sprint Goal

Implement [ADR-023](../knowledge-base/decisions/ADR-023-employee-profile-versioning-emission-and-rule-engine-cutover.md) D1-D8: (1) make rule-engine replays deterministic on 3 dated employee-profile fields (`weekly_norm_hours`, `part_time_fraction`, `position`) via PCS cutover through new `EmploymentProfileResolver`; (2) lay a Phase 4e replay-data trail for `agreement_code` via new `UserAgreementCodeChanged` event emission; (3) dispose dead `/calculate*` endpoints per D6.

Marquee D-test `ReplayAsync_StableUnderEmployeeProfileMutation_ResultByteIdentical` (2 variants — `weekly_norm_hours` + `part_time_fraction`) is the load-bearing P4 (version correctness) acceptance gate.

## Entropy Scan Findings

Run 2026-05-17 at sprint open (per WORKFLOW.md Step 0a):

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-023 registered in `docs/knowledge-base/INDEX.md` at S32 close; no stale paths |
| Pattern compliance | CLEAN | No new anti-patterns observed pre-S33 |
| Orphan detection | DEBT (deferred) | 80+ stale locked agent worktrees under `.claude/worktrees/` (non-load-bearing for S33 — sprint uses non-worktree dispatch per ADR-023 D5 + S29/S30/S31 precedent); user-deferred per separate housekeeping decision |
| Documentation drift | CLEAN | MEMORY.md synced through S32 close per session context |
| Quality grade review | scheduled | Re-grade at TASK-3313 (Rule Engine + Payroll Integration domains affected by D1 PCS cutover) |
| Refinement disposition | RESOLVED | 3-cycle dual-lens reviewed clean; cycle-cap respected by Orchestrator-internal Step-4 discipline (cycles 1+2 absorbed BLOCKERs, cycle 3 verification-only) |

No DRIFT items requiring fix before sprint open. One DEBT item (worktree litter) flagged and deferred.

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1, P3, P4, P7 — all four MANDATORY rows touched: architectural integrity via ADR-020/019/023 inheritance, event sourcing via new `UserAgreementCodeChanged` + emission sites, version correctness via marquee replay-stability gate, security via new DELETE endpoint + AdminEndpoints PUT extension) |
| **External Codex** | invoked 2026-05-17 — 2 cycles, cycle 1: 2B/4W/1N + cycle 2: 1B/0W/0N (cycle-cap reached) |
| **Internal Reviewer** | invoked 2026-05-17 — 2 cycles, cycle 1: 3B/2W/3N + cycle 2: clean (0B/1W/5N — "clean-to-proceed") |
| **BLOCKERs resolved before Step 1** | yes — 3 cycle-1 BLOCKERs + 1 cycle-2 BLOCKER all absorbed (mechanical fixes only; no architectural reframe) |

### Findings (cycle 1)

**External Codex (gpt-5.5)**:
- **BLOCKER** — Agent assignments violate declared scopes at PLAN-s33.md:93/132/159/275/316/400 — Data Model assigned Infrastructure/Backend/Payroll files; API Integration assigned Backend endpoint work. Per AGENTS.md L14/L24 those scopes don't match. Use explicit cross-domain labels per AGENTS.md L44.
- **BLOCKER** — TASK-3305 puts `EmploymentProfile` record refactor under Payroll Integration scope at PLAN-s33.md:234/257/262; type lives in `src/SharedKernel/Models/`. Move refactor to Data Model task.
- **WARNING** — Phase 2 parallelism conflicts with TASK-3311 dependency on TASK-3308 (PLAN-s33.md:31/226 say all 7 parallel; 3311 deps on 3308 at L425).
- **WARNING** — Marquee gate wording overclaims `Position` (sprint goal claims 3 dated fields with marquee proof; D-tests make marquee on 2 only).
- **WARNING** — TASK-3301 validation doesn't pin full resolver hydration shape.
- **NOTE** — `PAT-007 (if exists)` invalid KB ref at PLAN-s33.md:426.

**Internal Reviewer Agent**:
- **BLOCKER** — Agent-scope governance (convergent with Codex BLOCKER 1) — TASK-3301/3302/3303 assign Data Model Agent to Infrastructure files outside its declared scope; needs cross-domain-authorized labeling per AGENTS.md L50.
- **BLOCKER** — TASK-3305 cross-domain leak into SharedKernel/Models (convergent with Codex BLOCKER 2) — `EmploymentProfile` refactor + `EmployeeProfileNotFoundException` creation belong to Data Model, not Payroll Integration.
- **BLOCKER** — TASK-3308 Case A contract gap: `SupersedeAndCreateAsync` Case A (no live row → INSERT) reachable from PUT silently flips PUT-on-missing into CREATE, breaking S31 contract that 404s at `EmployeeProfileEndpoints.cs:150-154`.
- **WARNING** — Frontend `effectiveFrom` ISO wire-shape + DateOnly JSON binding + UTC timezone alignment unpinned in plan.
- **WARNING** — TASK-3312 marquee assertion target unpinned (per-line shape vs JSON serialized form?).
- **NOTE** — TASK-3310 test-migration discretion may drop orchestrator-scope coverage; pre-deletion verification recommended.
- **NOTE** — Phase 2 file-disjointness claim worth explicitly enumerating per S24 cautionary precedent.
- **NOTE** — `EmploymentProfile` record-type cascade verified clean (today `sealed class` with init properties; conversion 1-line; no Dictionary/HashSet/reference-equality usage).

### Resolution

All 3 BLOCKERs absorbed in PLAN-s33.md edits (single edit pass, no rescope):

1. **Agent-scope governance (convergent B1+B1)** — re-labeled TASK-3301 → `Data Model (extended into Infrastructure + Backend.Api/Program.cs + Payroll.Integrations/Program.cs, cross-domain authorized)`; TASK-3302/3303 → `Data Model (extended into Infrastructure, cross-domain authorized)`; TASK-3306..3310 → `Backend API (cross-domain authorized)`. TASK-3304 stays plain `Data Model` (EventSerializer.cs IS in declared scope). TASK-3305 stays plain `Payroll Integration`. TASK-3311 stays plain `UX`. TASK-3312 stays plain `Test & QA`.

2. **TASK-3305 cross-domain leak (convergent B2+B2)** — `EmploymentProfile` record refactor + `IEmploymentProfileResolver` interface (SharedKernel) + `EmployeeProfileNotFoundException` (SharedKernel) all moved UPSTREAM to TASK-3301 (absorbed under its cross-domain-authorized label). TASK-3305 now touches only `PeriodCalculationService.cs` and stays within Payroll Integration declared scope.

3. **TASK-3308 Case A contract gap (Reviewer B3)** — added pre-check: `repository.GetByEmployeeIdAsync(conn, tx, employeeId, ct)`-null returns 404 BEFORE routing through `SupersedeAndCreateAsync`. Case A unreachable from PUT after pre-check; preserves S31 contract.

Mechanical WARNINGs also absorbed:
- Phase 2 parallelism vs TASK-3311 dependency: clarified as contract-level (not file-level) — both tasks dispatch in parallel and synchronize on PLAN's binding DTO contract.
- Marquee gate wording: sprint goal updated to say "marquee proof on 2 of 3 dated fields; Position covered by separate non-marquee D-test".
- TASK-3301 validation: 8-field hydration shape pinned in acceptance criteria.
- Marquee assertion target: pinned to `JsonSerializer.Serialize(segmentRuleResults)` (rule-engine output, pre-mapping).
- Frontend ISO wire-shape: `DateTime.UtcNow` server-side aligns with frontend `toISOString()` UTC extraction; validation criterion added for Program.cs DateOnly converter check.
- TASK-3310 test-migration: pre-deletion coverage audit added to validation criteria.
- Invalid `PAT-007 (if exists)` ref: removed from TASK-3311 KB Refs.
- Phase 2 disjointness audit: explicit table added to PLAN-s33.md.

### Findings (cycle 2)

**External Codex (gpt-5.5)**:
- **BLOCKER (net-new)** — TASK-3310 references nonexistent `src/SharedKernel/.../Contracts/CalculateRequest.cs` + `WeeklyCalculateRequest.cs` paths; actual files live at `src/Backend/StatsTid.Backend.Api/Contracts/*`. Wrong path would mislead the deletion agent and leave dead DTOs behind. **Absorbed: PLAN-s33.md TASK-3310 Components + Phase 2 Disjointness Audit table updated to correct Backend.Api/Contracts/ paths.**

**Internal Reviewer Agent**:
- **WARNING** — TASK-3301 expansion bundles 5 sub-components without explicit internal sub-step ordering text (interface → record-refactor → exception → impl → DI). Non-load-bearing since `dotnet build` enforces it, but tightens the contract. **Absorbed: explicit "Internal sub-step order" block added to TASK-3301 description.**
- **NOTE** — Phase 1 "Sequential" classification still holds with expanded TASK-3301.
- **NOTE** — TASK-3308 Case A pre-check TOCTOU race is benign under same-`(conn, tx)` semantics + Postgres `READ COMMITTED` + `SELECT ... FOR UPDATE` in `SupersedeAndCreateAsync`. Verified clean.
- **NOTE** — TASK-3305 scope-purity verified clean.
- **NOTE** — Phase 2 contract-level dependency between TASK-3308 and TASK-3311 is workable (S25 admin-endpoints precedent).
- **NOTE** — Agent label asymmetry between TASK-3304 (plain Data Model) and TASK-3309 (Backend API cross-domain) is mechanically correct.

### Resolution (cycle 2)

Cycle 2 absorption complete:
1. **Codex BLOCKER (path fix)** — Plan + Phase 2 Disjointness Audit updated to `src/Backend/StatsTid.Backend.Api/Contracts/` paths (single-string replace, no semantic change).
2. **Reviewer WARNING (sub-step ordering)** — TASK-3301 description now pins the 5-step internal order (interface → record-refactor → exception → impl → DI). Non-load-bearing but tightens agent dispatch contract.

**Cycle-cap reached** (2 BLOCKER-fix cycles per lens used). No cycle-3 absorption authorized; plan is READY for Phase 1 dispatch.

## Architectural Constraints Verified

_To be checked off as the sprint progresses; final assertion in TASK-3313._

- [ ] **P1 — Architectural integrity** → ADR-016 D5b stays at 5 patterns; ADR-020 D2 + ADR-019 D8 inherited verbatim; ADR-023 D1-D8 implemented faithfully
- [ ] **P2 — Rule engine determinism** → PCS replays byte-identical on 3 dated fields under mid-period supersession (marquee proof); fail-closed on resolver-null in production path
- [ ] **P3 — Event sourcing / auditability** → `UserAgreementCodeChanged` registered (55→56 typeof) + emitted; `EmployeeProfileSuperseded` + `EmployeeProfileSoftDeleted` (S31 registered) now actually emitted; atomic outbox per ADR-018 D3 preserved on all new emissions
- [ ] **P4 — Version correctness** → SoftDelete predecessor version unchanged + audit `version_before = version_after`; ADR-019 admin-strict If-Match on new DELETE; cycle-3 same-day-only-edit validator on PUT
- [ ] **P6 — Payroll integration** → PCS cutover preserves OkVersion server-resolution overlay (ADR-003) + Position caller-fallback (TASK-1802); marquee verifies byte-identical replay
- [ ] **P7 — Security and access control** → new DELETE endpoint HROrAbove + OrgScopeValidator; AdminEndpoints PUT new emission inside existing atomic tx; cross-org leak prevention preserved (S31 Step 0b precedent)

P5/P8/P9 indirectly affected (frontend toggle = P9; CI/CD invariants preserved; integration isolation untouched).

## Task Log

13 declared tasks (TASK-3300..3313) across 4 phases. Plan file `.claude/plans/PLAN-s33.md` is source-of-truth for per-task detail; this file records execution status + validation evidence as tasks complete.

### Phase 0 — Sprint Open

#### TASK-3300 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3300 |
| **Status** | in-progress |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s33.md`, `docs/sprints/SPRINT-33.md` (this file), `docs/sprints/INDEX.md` |
| **Plan section** | Phase 0 — TASK-3300 |
| **Dependencies** | none |

**Description**: Create PLAN-s33.md + SPRINT-33.md (from TEMPLATE.md) + INDEX.md provisional row. Commit as sprint-open.

**Validation Criteria**:
- [x] `.claude/plans/PLAN-s33.md` exists with 13-task decomposition
- [x] `docs/sprints/SPRINT-33.md` exists (this file)
- [x] `docs/sprints/INDEX.md` has Sprint 33 row (status=in-progress)
- [x] Sprint-open commit lands on master (`1da0b73`, 2026-05-17)

---

### Phase 1 — Sequential Foundation

#### TASK-3301 — `EmploymentProfileResolver` service + DI wiring

| Field | Value |
|-------|-------|
| **ID** | TASK-3301 |
| **Status** | pending |
| **Agent** | Data Model (extended into Infrastructure + Backend.Api/Program.cs + Payroll.Integrations/Program.cs, cross-domain authorized) |
| **Components** | `src/SharedKernel/StatsTid.SharedKernel/Interfaces/IEmploymentProfileResolver.cs` (new), `src/SharedKernel/StatsTid.SharedKernel/Models/EmploymentProfile.cs` (refactor `sealed class` → `sealed record class`), `src/SharedKernel/StatsTid.SharedKernel/Exceptions/EmployeeProfileNotFoundException.cs` (new), `src/Infrastructure/StatsTid.Infrastructure/EmploymentProfileResolver.cs` (new), `src/Backend/StatsTid.Backend.Api/Program.cs` (DI), `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` (DI) |
| **Plan section** | Phase 1 — TASK-3301 |
| **Dependencies** | TASK-3300 |
| **KB Refs** | ADR-023 D1/D2/D3, ADR-016 D5b, ADR-018 D5 |

See plan for binding SQL contract + interface shape.

---

#### TASK-3302 — `EmployeeProfileRepository.SupersedeAndCreateAsync` (ADR-020 D2 3-case routing)

| Field | Value |
|-------|-------|
| **ID** | TASK-3302 |
| **Status** | pending |
| **Agent** | Data Model (extended into Infrastructure, cross-domain authorized) |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileRepository.cs` |
| **Plan section** | Phase 1 — TASK-3302 |
| **Dependencies** | TASK-3300 |
| **KB Refs** | ADR-020 D2, ADR-018 D5, ADR-019 D8 |

---

#### TASK-3303 — `EmployeeProfileRepository.SoftDeleteAsync`

| Field | Value |
|-------|-------|
| **ID** | TASK-3303 |
| **Status** | pending |
| **Agent** | Data Model (extended into Infrastructure, cross-domain authorized) |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileRepository.cs` |
| **Plan section** | Phase 1 — TASK-3303 |
| **Dependencies** | TASK-3300 |
| **KB Refs** | ADR-023 D8, ADR-018 D5 |

SQL shape pinned in plan (no `version + 1`); audit action `DELETED` (not `SOFT_DELETED` — schema CHECK constraint).

---

#### TASK-3304 — `UserAgreementCodeChanged` event type + EventSerializer registration

| Field | Value |
|-------|-------|
| **ID** | TASK-3304 |
| **Status** | pending |
| **Agent** | Data Model Agent (in-scope — `Events/` + `EventSerializer.cs` both declared per AGENTS.md L15) |
| **Components** | `src/SharedKernel/StatsTid.SharedKernel/Events/UserAgreementCodeChanged.cs` (new), `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` |
| **Plan section** | Phase 1 — TASK-3304 |
| **Dependencies** | TASK-3300 |
| **KB Refs** | ADR-023 D2, PAT-004 |

Typeof count 55 → 56. `EffectiveFrom: DateOnly`. No S33 consumer.

---

### Phase 2 — Parallel Cutovers (7 file-disjoint tasks)

Dispatch AFTER Phase 1 commits land (R7 commit-before-dispatch discipline). NO worktrees.

#### TASK-3305 — PCS segmentProfile cutover

| Field | Value |
|-------|-------|
| **ID** | TASK-3305 |
| **Status** | pending |
| **Agent** | Payroll Integration Agent |
| **Components** | `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` |
| **Plan section** | Phase 2 — TASK-3305 |
| **Dependencies** | TASK-3301 |
| **KB Refs** | ADR-023 D1/D3, ADR-003 |

Trailing-optional `IEmploymentProfileResolver? = null` for test-fixture preservation. `EmploymentProfile` record refactor + `EmployeeProfileNotFoundException` + `IEmploymentProfileResolver` interface all delivered UPSTREAM in TASK-3301 (Step 0b BLOCKER absorption — keeps PCS task within Payroll Integration declared scope).

---

#### TASK-3306 — ComplianceEndpoints cutover (fail-closed)

| Field | Value |
|-------|-------|
| **ID** | TASK-3306 |
| **Status** | pending |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs` |
| **Plan section** | Phase 2 — TASK-3306 |
| **Dependencies** | TASK-3301 |
| **KB Refs** | ADR-023 D3/D8 |

---

#### TASK-3307 — BalanceEndpoints cutover (graceful fallback)

| Field | Value |
|-------|-------|
| **ID** | TASK-3307 |
| **Status** | pending |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs` |
| **Plan section** | Phase 2 — TASK-3307 |
| **Dependencies** | TASK-3301 |
| **KB Refs** | ADR-023 D3 |

---

#### TASK-3308 — `EmployeeProfileEndpoints` PUT extension + new DELETE

| Field | Value |
|-------|-------|
| **ID** | TASK-3308 |
| **Status** | pending |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/EmployeeProfileEndpoints.cs` |
| **Plan section** | Phase 2 — TASK-3308 |
| **Dependencies** | TASK-3302, TASK-3303 |
| **KB Refs** | ADR-023 D8, ADR-019 D2/D8, ADR-018 D3 |

DTO extended with required `EffectiveFrom: DateOnly`; validator rejects `!= today` with 422. DELETE endpoint emits `EmployeeProfileSoftDeleted` event + audit action `DELETED`.

---

#### TASK-3309 — AdminEndpoints PUT emits `UserAgreementCodeChanged`

| Field | Value |
|-------|-------|
| **ID** | TASK-3309 |
| **Status** | pending |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` |
| **Plan section** | Phase 2 — TASK-3309 |
| **Dependencies** | TASK-3304 |
| **KB Refs** | ADR-023 D2, ADR-018 D3 |

Inside existing atomic tx at L502-539. Predicate null-safe + Ordinal compare.

---

#### TASK-3310 — DELETE dead `/calculate*` endpoints

| Field | Value |
|-------|-------|
| **ID** | TASK-3310 |
| **Status** | pending |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs`, `src/SharedKernel/.../Contracts/CalculateRequest.cs`, `src/SharedKernel/.../Contracts/WeeklyCalculateRequest.cs`, 2 test files |
| **Plan section** | Phase 2 — TASK-3310 |
| **Dependencies** | TASK-3300 |
| **KB Refs** | ADR-023 D6 |

---

#### TASK-3311 — Frontend EmployeeProfileEditor as-of-date toggle + PUT-body sync

| Field | Value |
|-------|-------|
| **ID** | TASK-3311 |
| **Status** | pending |
| **Agent** | UX Agent |
| **Components** | `frontend/src/pages/admin/EmployeeProfileEditor.tsx`, `frontend/src/hooks/useEmployeeProfile.ts` |
| **Plan section** | Phase 2 — TASK-3311 |
| **Dependencies** | TASK-3308 (backend DTO change must land in same sprint commit) |
| **KB Refs** | ADR-023 D8 |

Pure-UI toggle + mandatory PUT-body `effectiveFrom: today` injection (refinement cycle 2 convergent BLOCKER absorption — keeps wire shape in sync with TASK-3308 backend DTO).

---

### Phase 3 — D-Tests

#### TASK-3312 — Docker-gated D-test suite (~19 tests)

| Field | Value |
|-------|-------|
| **ID** | TASK-3312 |
| **Status** | pending |
| **Agent** | Test & QA Agent |
| **Components** | `tests/StatsTid.Tests.Regression/EmployeeProfile/*.cs` (new), `tests/StatsTid.Tests.Regression/Payroll/EmployeeProfileMarqueeTests.cs` (new) |
| **Plan section** | Phase 3 — TASK-3312 |
| **Dependencies** | All Phase 2 tasks |
| **KB Refs** | ADR-023 D8 (marquee load-bearing), S29 TASK-2909 precedent, S31 TASK-3110 precedent |

Marquee 2 variants + Position non-marquee + SupersedeAndCreate 3 cases + SoftDelete 3 + PUT validator 2 + DELETE If-Match 2 + UserAgreementCodeChanged emission 2 + consumption fail-modes 2 + audit-action enum 1.

---

### Phase 4 — Sprint Close

#### TASK-3313 — Sprint close (validation + INDEX + ROADMAP + QUALITY + KB-INDEX + MEMORY)

| Field | Value |
|-------|-------|
| **ID** | TASK-3313 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/sprints/SPRINT-33.md` (close sections), `docs/sprints/INDEX.md` (final row), `ROADMAP.md`, `docs/QUALITY.md`, `~/.claude/projects/C--StatsTid/memory/MEMORY.md` |
| **Plan section** | Phase 4 — TASK-3313 |
| **Dependencies** | TASK-3312 + Step 7a clean |

ROADMAP Phase 4e `agreement_code` row upgraded "candidate" → **LAUNCH-BLOCKING** per ADR-023 D2 (refinement cycle 1 Reviewer W5 absorption — explicit ROADMAP edit).

---

## Legal & Payroll Verification

_To be checked off at TASK-3313._

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | No rule logic changes in S33; PCS cutover is data-source change |
| Wage type mappings produce correct SLS codes | N/A | No mapping changes |
| Overtime/supplement calculations are deterministic | pending | Marquee verifies replay-stability on profile-driven inputs |
| Absence effects on norm/flex/pension are correct | N/A | No absence-rule changes |
| Retroactive recalculation produces stable results | pending | Marquee is precisely the retroactive-replay-stability proof |

## External Review (Step 7a)

_To be populated at sprint end._

| Field | Value |
|-------|-------|
| **Invoked** | pending |
| **Sprint-start commit** | `<TASK-3300 SHA>` (filled at close) |
| **Command** | `codex review "..."` (prompt-alone form per WORKFLOW.md) |
| **Review Cycles** | — |
| **Findings** | — |
| **Resolution** | — |

## Test Summary

_To be populated at TASK-3313 via `sprint-test-validation` skill._

| Suite | Previous (S32) | Current (S33) | Delta |
|-------|----------------|---------------|-------|
| Unit | 526 | — | — |
| Plain regression | 35 | — | — |
| Docker-gated | 184 | — | — |
| Frontend vitest | 88 | — | — |
| **Total** | **833** | **—** | **—** |

Target: ~847 total (~14 net new Docker-gated D-tests from TASK-3312).

## Agent Effectiveness

_To be populated at TASK-3313._

## Sprint Retrospective

_To be filled in at sprint close._
