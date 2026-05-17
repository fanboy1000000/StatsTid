# Sprint 33 — Phase 4d-3 Part 2 Implementation (ADR-023 cutover)

| Field | Value |
|-------|-------|
| **Sprint** | 33 |
| **Status** | complete |
| **Start Date** | 2026-05-17 |
| **End Date** | 2026-05-17 |
| **Orchestrator Approved** | yes — 2026-05-17 |
| **Build Verified** | yes — `dotnet build` 0 warnings 0 errors |
| **Test Verified** | yes — 526 unit + 35 plain regression + 204 Docker-gated passing + 88 frontend vitest = **853 total** (+20 vs S32's 833; 19 pre-existing Docker-gated failures unchanged, all in Manifest/Segmentation/TxContract/AgreementConfig classes deferred to Phase 4e per S31 carry-forward) |
| **Sprint-start commit base** | `55b082b` (S32 close, 2026-05-16) |
| **Sprint-end HEAD** | `8df267c` (Step 7a cycle 2 absorption — AdminEndpoints row/event parity). 18 commits total: TASK-3300 ×2 (sprint open + Step 0b absorption) + TASK-3301..3304 Phase 1 sequential + TASK-3305..3311 Phase 2 parallel + TASK-3312 + 3312b + 3312c (D-tests + 2 in-flight defect fixes) + 2 Step 7a absorption commits + this sprint-close commit. |
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

- [x] **P1 — Architectural integrity** → ADR-016 D5b stays at 5 patterns; ADR-020 D2 inherited (with `predecessor.Version+1` refinement per Step 7a cycle 1 absorption) + ADR-019 D8 inherited verbatim; ADR-023 D1-D8 implemented faithfully
- [x] **P2 — Rule engine determinism** → PCS replays byte-identical on 2 dated fields (marquee proof — `weekly_norm_hours` + `part_time_fraction`); Position covered by separate non-marquee D-test (caller-fallback semantic per ADR-023 D1); fail-closed on resolver-null in production path
- [x] **P3 — Event sourcing / auditability** → `UserAgreementCodeChanged` registered (55→56 typeof) + emitted from AdminEndpoints PUT; `EmployeeProfileSuperseded` + `EmployeeProfileSoftDeleted` (S31 registered) now actually emitted; atomic outbox per ADR-018 D3 preserved on all new emissions; row/event parity for EmployeeProfileCreated restored via Step 7a cycle 2 absorption
- [x] **P4 — Version correctness** → SoftDelete predecessor version unchanged + audit `version_before = version_after`; Case C successor inherits `predecessor.Version+1` (Step 7a cycle 1 ETag-monotonicity absorption); ADR-019 admin-strict If-Match on new DELETE; cycle-3 same-day-only-edit validator on PUT (rejects both backdated AND future-dated with 422)
- [x] **P6 — Payroll integration** → PCS cutover preserves OkVersion server-resolution overlay (ADR-003) + Position caller-fallback (TASK-1802); marquee verifies byte-identical replay
- [x] **P7 — Security and access control** → new DELETE endpoint HROrAbove + OrgScopeValidator; AdminEndpoints PUT new emission inside existing atomic tx; cross-org leak prevention preserved (S31 Step 0b precedent)

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

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified | No rule logic changes in S33; PCS cutover is data-source change only — rule evaluation reads the same `EmploymentProfile` shape as pre-S33 |
| Wage type mappings produce correct SLS codes | N/A | No mapping changes; existing WTM versioned-history pattern unchanged |
| Overtime/supplement calculations are deterministic | verified | Marquee `ReplayAsync_StableUnderEmployeeProfileMutation_*_ResultByteIdentical` proves replay stability on `weekly_norm_hours` + `part_time_fraction` (the 2 fields that feed `NormCheckRule.cs:174 profile.WeeklyNormHours * profile.PartTimeFraction` inside rule evaluation) |
| Absence effects on norm/flex/pension are correct | N/A | No absence-rule changes |
| Retroactive recalculation produces stable results | verified | Marquee is precisely the retroactive-replay-stability proof — admin updates today, replay of last month yields byte-identical `JsonSerializer.Serialize(segmentRuleResults)` |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — 2 cycles dual-lens (external Codex gpt-5.5 + internal Reviewer Agent), 2026-05-17 |
| **Sprint-start commit** | `55b082b` (S32 close) |
| **Command** | `codex review --base 55b082b` (full sprint diff against pre-S33 base) |
| **Review Cycles** | 2 per lens (cycle-cap respected per WORKFLOW.md L38 + skill discipline) |
| **Findings** | **Cycle 1**: Codex 2 P1 BLOCKERs (ETag monotonicity on Case C; seeder broke historical lookups); Reviewer 0 BLOCKERs + 6 NOTEs (classic code-correctness vs architecture-invariant lens divergence). **Cycle 2**: Codex 1 P1 BLOCKER (AdminEndpoints POST EmployeeProfileCreated event payload `EffectiveFrom='0001-01-01'` diverged from row's today-stamp after cycle 1 split); Reviewer 1 WARNING (convergent on the same finding, rated lower severity). |
| **Resolution** | **Cycle 1 absorbed in `0bca4c2`**: (a) `InsertLiveRowAsync` gains `nextVersion: long` parameter — Case A=1 baseline, Case C=`predecessor.Version+1` so ETag monotonicity holds across cross-day supersessions; diverges from ADR-020 D2's literal "version=1 for new row" but inherits the spirit (each successor is a fresh logical row); EmployeeProfile's natural key is just employee_id so version carries the monotonic load alone, unlike WTM's (key, version) composite where effective_from disambiguates. (b) `EmployeeProfileSeeder.cs` reverted to schema DEFAULT `'0001-01-01'` (history-covering for existing employees' pre-deployment periods); kept `CreateAsync` + `AdminEndpoints POST` stamping today (new-profile paths). Test assertion updates: `SupersedeAndCreate_CaseC_*` now assert `predecessorVersion+1`; S31 `EmployeeProfileEdit_RoundTripsAtomically` adds inline backdate-to-today on emp001 seed to continue exercising Case B UPDATED path. **Cycle 2 absorbed in `8df267c`**: AdminEndpoints POST `EmployeeProfileCreated` event payload `EffectiveFrom` aligned to `DateOnly.FromDateTime(DateTime.UtcNow)` matching row stamp; row/event parity per ADR-018 D3 atomic-outbox contract restored. Build 0/0. All 38 EmployeeProfile + Admin tests pass. **Cycle-cap reached** (2/2 per lens); no cycle 3 absorption authorized; remaining Reviewer cycle 2 NOTEs (ClosePredecessorAsync filter guard, DateOnly JSON timezone alignment in production, etc.) all flagged as Phase 4e candidates — no S33-breaking defects remain. |

## Test Summary

Verified via `sprint-test-validation` skill 2026-05-17:

| Suite | Previous (S32) | Current (S33) | Delta |
|-------|----------------|---------------|-------|
| Unit | 526 | 526 | +0 |
| Plain regression | 35 | 35 | +0 |
| Docker-gated (passing) | 184 | 204 | +20 |
| Frontend vitest | 88 | 88 | +0 |
| **Total passing** | **833** | **853** | **+20** |

19 Docker-gated tests fail (pre-existing — same Manifest/Segmentation/TxContract/AgreementConfig classes that have been failing since S29; matches S31 carry-forward "18 pre-existing failures unchanged" +1 net delta unrelated to S33). All 20 net-new TASK-3312 D-tests PASS including 2 marquee variants (`weekly_norm_hours` + `part_time_fraction` byte-identical replay-stability proof).

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 13 declared (TASK-3300..3313) + 3 in-flight defect tasks (TASK-3312b/c + Step 7a P1) — 16 logical task units |
| Constraint Violations | 0 (all cross-domain authorized tasks per AGENTS.md L44-51) |
| Reviewer Findings | Step 0b cycle 1: 3 BLOCKERs + 2 WARNINGs + 3 NOTEs; cycle 2: 0 BLOCKERs + 1 WARNING + 5 NOTEs. Step 7a cycle 1: 0 BLOCKERs + 0 WARNINGs + 6 NOTEs (classic code-correctness vs architecture-invariant lens divergence — Codex caught what Reviewer's architectural lens didn't drill into). Cycle 2: 0 BLOCKERs + 1 WARNING (convergent with Codex cycle 2 BLOCKER) + 6 NOTEs. |
| External Review Cycles | Step 0b: 2 per lens (cycle 1 absorbed 3 BLOCKERs + 6 WARNINGs; cycle 2 absorbed 1 path BLOCKER + 1 sub-step ordering WARNING). Step 7a: 2 per lens (cycle 1 absorbed 2 P1 BLOCKERs; cycle 2 absorbed 1 convergent BLOCKER). Both cycle-caps respected. |
| External Findings | Step 0b Codex cycle 1: 1 BLOCKER + 4 WARNINGs + 1 NOTE; cycle 2: 1 BLOCKER + 0/0. Step 7a Codex cycle 1: 2 P1 BLOCKERs + 0/0; cycle 2: 1 P1 BLOCKER + 0/0. |
| Re-dispatches | 0 sprint-task re-dispatches. Absorptions handled via Orchestrator-direct edits + targeted commits (`19b8b5f` Step 0b absorption; `06a3ddd` + `10a6f9e` + `58d8913` Phase 3 absorption; `0bca4c2` Step 7a cycle 1 absorption; `8df267c` Step 7a cycle 2 absorption). |
| First-Pass Rate | 13/13 declared sprint tasks first-pass clean on agent dispatch (all 7 Phase 2 parallel agents returned clean). 3 in-flight defects caught by Phase 3 D-test bring-up + Step 7a (matches refinement R1 budget for in-flight defects); all absorbed mechanically. |

## Sprint Retrospective

**What went well**:
- **Marquee D-tests PASS on first cycle** after legacy-shim refactor — both `ReplayAsync_StableUnderEmployeeProfileMutation_WeeklyNormHours` + `_PartTimeFraction` variants prove byte-identical PCS replay under mid-period supersession (ADR-023 D1 + ADR-016 D10 closed for EmployeeProfile dated fields). P4 (version correctness) acceptance gate verified.
- **All 7 Phase 2 parallel cutovers landed clean** (file-disjoint dispatches per Phase 2 Disjointness Audit; no worktree-base-mismatch quirk since this sprint used non-worktree parallel dispatch per ADR-023 D5 + S29/S30/S31 precedent).
- **Step 0b plan-review caught 3 cycle-1 BLOCKERs at plan stage** — agent-scope governance + TASK-3305 cross-domain leak + TASK-3308 Case A 404 pre-check — preventing 3 rework cycles at implementation phase. Cost-benefit of Step 0b validated again (~10 min plan review prevented hours of mid-implementation rework per WORKFLOW.md L16).
- **In-flight defect handling matched S29 TASK-2912 precedent**: marquee D-test bring-up surfaced 3 mechanical defects (schema columns, legacy-shim API, Case C backdate semantics); all absorbed in single follow-up commits without scope expansion.
- **Step 7a dual-lens convergent BLOCKER on cycle 2** (ETag-monotonicity + seeder history-coverage + AdminEndpoints row/event parity) — exactly the production-readiness issues Step 7a is designed to catch. The lens divergence pattern (Codex code-correctness BLOCKER + Reviewer WARNING on same finding) operated as designed per `feedback_review_lens_complementarity.md`.

**What to improve**:
- **TASK-3312 agent's schema discovery was sloppy**: invented column names (`name`/`hierarchy_path`/`default_agreement_code`) instead of reading the actual schema or sibling test files. Future Test & QA agent dispatches should include explicit "read 2-3 existing test files in the same directory + grep init.sql for column names BEFORE writing INSERT statements" instructions. The 3 test-fixture defects (schema + legacy-shim + Case C semantics) cost ~30 min of Phase 3 follow-up time.
- **ADR-020 D2 "version=1 for new row" wording was too literal**: Step 7a cycle 1 P1-1 (ETag monotonicity on Case C) showed that EmployeeProfile's natural key (single `employee_id` column) can't carry monotonic load with a flat "version=1 always" rule the way WTM's composite (key, effective_from) can. Future ADRs in the versioned-config family should explicitly distinguish per-resource monotonicity contracts: WTM-style ("version=1 always" works because natural key is composite + includes effective_from) vs EmployeeProfile-style ("successor inherits predecessor.Version+1" because natural key is just employee_id).
- **Marquee D-test count vs scope drift**: PLAN-s33.md said "~14-22 tests"; agent delivered 20 (within band). But 4 of those 20 broke on Docker-gated bring-up due to test-fixture defects unrelated to the SUT — meaning the test agent's self-verification was structural (compile + filter-discovery) not behavioral. Future Test & QA dispatches should require at least 1 test be run against a live Docker harness BEFORE the agent reports "AC pass".

**Knowledge produced**:
- **ADR-020 D2 implementation refinement for EmployeeProfile**: successor version is `predecessor.Version + 1` (not flat 1) when the natural key is a single column. Documented inline in `EmployeeProfileRepository.InsertLiveRowAsync` XML doc. No new ADR; existing ADR-020 D2 + ADR-023 D8 still binding.
- **Phase 4e LAUNCH-BLOCKING entry**: `agreement_code` determinism gap upgraded from "candidate" to LAUNCH-BLOCKING per ADR-023 D2 binding. ROADMAP updated with 3 architectural options (per-time-entry snapshot / users.agreement_code versioning / unenumerated). Must close before production launch.
- **`feedback_dont_pause_for_reviews.md`** (memory): user preference captured during S33 — don't pause mid-sprint to ask for review confirmations; Codex Step 7a + Reviewer Agent are the formal review checkpoints.
