# Sprint 29 — Phase 4d-1 Implementation: Wage-Type-Mapping Versioned History

| Field | Value |
|-------|-------|
| **Sprint** | 29 |
| **Status** | open |
| **Start Date** | 2026-05-10 |
| **End Date** | TBD |
| **Orchestrator Approved** | n/a — sprint open |
| **Build Verified** | TBD |
| **Test Verified** | TBD (target: 526 unit + 35 plain regression + 159 Docker-gated + 88 frontend vitest = **808 total**, +13 net new from S28 close at 795) |
| **Sprint-start commit base** | `8950893` (S28 sprint close, 2026-05-09) |
| **Sprint-end HEAD** | TBD |
| **Sprint type** | **IMPLEMENTATION** — implements Phase 4d-1 backend versioned history against ADR-020 binding contract; no frontend changes (deferred to Phase 5 per ADR-020 Open). |
| **Refinement** | `.claude/refinements/REFINEMENT-s29-phase-4d1.md` (Step 4 cycles 1+2+3 absorbed; lens convergence at cycle 3 = converging-finite trail). User adjudications 2026-05-10: cycle 1 position-fallback replicate-at-dated-read + D1 thread-through via Plan() parameter; cycle 2 backdate-forbid + cycle-3-waiver-grant; cycle 3 symmetric-forbid (same-day-only-edit) + absorb R3-W1/W3 inline. |

## Sprint Goal

Implement Phase 4d-1 — wage-type-mapping versioned history — against ADR-020's settled architecture (`docs/knowledge-base/decisions/ADR-020-versioned-config-design-foundations.md`, ACCEPTED at S28 close `8950893`). Closes the S20 carry-forward where `PeriodCalculationService.MapSegmentToExportLinesAsync` reads live DB at export-line time (`PeriodCalculationService.cs:1175-1177`) and breaks ADR-016 D10 replay determinism — the existing deferred-WTM caveat at `tests/StatsTid.Tests.Regression/Segmentation/ReplayDeterminismTests.cs:14-23` is closed by D-test #6 (marquee).

ADR-020 binds 8 Implications-for-S29 constraints. S29 implementation refinement settled all sub-decisions across 3 cycles per lens; the refinement is the source-of-truth for detailed task specs (this sprint log cross-cites refinement line numbers).

**Architectural decisions settled** (during refinement):
1. `GetByKeyAtAsync` replicates `(position = @position OR position = '')` fallback at dated read — replay-stable for fallback-resolved lookups.
2. `EmploymentProfile? profile` reaches `PeriodPlanner.Plan()` via parameter (NOT `BoundarySources` extension).
3. **Same-day-only-edit validator** at endpoint — POST/PUT reject `body.EffectiveFrom != today` with 422 (rejects BOTH past and future). Future-dated PUT support deferred to Phase 4d-2.

## Entropy Scan Findings

Per WORKFLOW.md Step 0a (2026-05-10):

| Check | Result |
|-------|--------|
| KB Path Validation | DEFERRED (full path-walk is Phase 4e candidate) |
| FAIL-001 (`FindFirst("scopes")`) regression | CLEAN — 0 hits in `src/` |
| Hardcoded `http://localhost` in non-test code | CLEAN — 5 hits, all in `Properties/launchSettings.json` (dev-only IIS Express launch profiles, expected) |
| Endpoint `RequireAuthorization` coverage | CLEAN — 71 of 72 endpoints authorized; the 1 gap is `AuthEndpoints` login (intentionally unauthenticated) |
| MEMORY.md drift | CLEAN — synchronized through S28 close per session context |
| QUALITY.md re-grade | DEFERRED to sprint close (S29 closes the WTM replay-determinism gap → Payroll integration domain re-graded after sprint close) |

No DRIFT items requiring fix before sprint open. No DEBT items added.

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — task touches P1 (Architectural integrity — schema migration + planner enrollment seam), P3 (Event sourcing — new `WageTypeMappingSuperseded` event), P4 (Version correctness — ETag/If-Match preserved), **P5 (Integration isolation — `GetMappingAsync` parameter addition + SharedKernel stays Npgsql-free)**, P6 (Payroll integration — replay determinism), and is cross-domain (Data Model + Backend.Api + Payroll Integration + SharedKernel). |
| **Step 0b cycle 1 review** | Codex 1B/2W/1N + Reviewer 0B/4W/10N. C-B1 (AC weakening) absorbed via TASK-2908/2909/Documentation+build AC restoration; C-W1 (P5 omission) absorbed; C-W2 (priority→task mapping implicit) absorbed via per-priority task IDs in Architectural Constraints; R-W1/W2 (agent assignment) absorbed via cross-domain-authorized labels; R-W3 (TASK-2904 dep trim) absorbed; R-W4 (audit-CHECK preservation) absorbed; R-N1 (ADR-020 `NonDatedSourceValues` wording vs actual `Values` property) noted in TASK-2902 description. **Cycle 1 absorption complete; Step 0b READY for Step 1 dispatch.** |
| **External Codex** | TBD (run after this plan is committed; this sprint log + refinement file together comprise the plan) |
| **Internal Reviewer** | TBD (same trigger; spawned after Codex) |
| **Cycle cap** | 2 cycles per lens at Step 0b (same discipline as Step 7a) |

Plan Review will run on this SPRINT-29.md + the refinement file (`.claude/refinements/REFINEMENT-s29-phase-4d1.md`) together. The refinement already had 3 cycles of dual-lens review at refinement level; Step 0b plan-review is a check on whether the sprint-log mapping (this file) faithfully encodes the refinement's task decomposition + acceptance criteria, not a re-litigation of the refinement-level decisions.

## Architectural Constraints Verified

- [x] **P1 — Architectural integrity** → **TASK-2902 + TASK-2901**: ADR-020 D1 planner-enrollment seam preserves `RuleRegistry` purity (non-rule replay inputs go through `IPlannerEnrollment`, not `RuleClassification`). SharedKernel/Segmentation gains no Npgsql dependency. Schema migration via `schema_migrations` ledger pattern (S22 D8). Bounded contexts respected.
- [x] **P3 — Event sourcing / auditability** → **TASK-2901 (audit-CHECK widening) + TASK-2903 (event registration) + TASK-2908 (audit + outbox emission per D2 case)**: `WageTypeMappingSuperseded` added per PAT-004 (EventSerializer typeof count 47 → 48). Audit-action CHECK widened to add `SUPERSEDED`. Outbox emission per case (D2 routing): A → CREATED+Created; B → CREATED+Created (predecessor's prior DELETE already audited at its own request); C → UPDATED+Updated (collapsed same-request DELETE+CREATE intent). Soft-delete preserves history chain.
- [x] **P4 — Version correctness** → **TASK-2904 (repo `SupersedeAndCreateAsync` preserves expectedVersion check) + TASK-2908 (endpoint 412/428 unchanged)**: S25 admin-strict ETag/If-Match contract preserved unchanged on the live-edit path. Supersession adds a new history-row creation path orthogonal to the version contract per ADR-019 D3 amendment.
- [x] **P5 — Integration isolation** → **TASK-2907 (GetMappingAsync optional `asOfDate`) + TASK-2902 (`IPlannerEnrollment` interface in SharedKernel/Segmentation, no Npgsql dependency)**: `PayrollMappingService.GetMappingAsync` parameter addition (`DateOnly? asOfDate = null`) is backward-compatible. SharedKernel stays Npgsql-free.
- [x] **P6 — Payroll integration correctness** → **TASK-2907 (PCS L585 enrollment + L1175 snapshot read + asOfDate=segmentStart) + TASK-2910 (ADR-018 D14 + ADR-016 D5b reconciliation)**: `MapSegmentToExportLinesAsync` reads via `Snapshot.Values["WtmNaturalKey"]` + `asOfDate=segmentStart` → replay-stable. ADR-016 D10 closed for WTM via the fourth-pattern export-time effective-date lookup.

Not directly affected: P2 (rule engine determinism unchanged), P7 (no new auth paths), P8 (CI unchanged), P9 (no UX changes per ADR-020 Open §Phase-4d-1-frontend-view-history).

## Task Log

11 tasks across 3 phases. Refinement file is source-of-truth for detailed specifications.

### Phase 1 — Plumbing (sequential; commit-before-dispatch per S26 R7 / S27 precedent)

#### TASK-2901 — Schema migration (`init.sql` + `schema_migrations` ledger entry `s29-d1-wtm-effective-dating`)

| Field | Value |
|-------|-------|
| **ID** | TASK-2901 |
| **Status** | pending |
| **Agent** | Data Model Agent |
| **Components** | docker/postgres/init.sql |
| **KB Refs** | ADR-020 D3, ADR-018 D8 (ledger pattern), ADR-017 D2.1 (partial-unique-index pattern) |
| **Refinement section** | TASK-2901 (refinement L29-43) |
| **Dependencies** | none (Phase 1 root) |

**Description**: Three-step migration wrapped in `schema_migrations` ledger DO block: (a) ALTER TABLE adds `mapping_id UUID`, `effective_from DATE`, `effective_to DATE`; backfills `mapping_id = gen_random_uuid()`, `effective_from = '2020-01-01'`; sets NOT NULL. (b) DROP existing composite PK; ADD PK on `mapping_id`; CREATE `idx_wtm_natural_key_open` (partial unique WHERE `effective_to IS NULL`) + `idx_wtm_natural_key_history` (full unique on `(natural_key, effective_from)`). (c) Audit-action CHECK widened to add `SUPERSEDED`.

**Validation**:
- [ ] `docker compose down -v && docker compose up postgres -d --force-recreate` succeeds idempotently across multiple re-runs
- [ ] `\d wage_type_mappings` shows new shape; row count matches pre-S29
- [ ] Audit CHECK includes `SUPERSEDED` in allowed set
- [ ] DROP CONSTRAINT + ADD CONSTRAINT swap on `wage_type_mapping_audit.action` preserves all existing audit rows (Step 0b R-W4 absorption — assert `SELECT COUNT(*) FROM wage_type_mapping_audit` is unchanged across the migration; the CHECK is on schema not data so this is mostly mechanical, but explicit assertion keeps the AC tight).

#### TASK-2902 — `IPlannerEnrollment` + `PeriodPlanner` signature change (ADR-020 D1)

| Field | Value |
|-------|-------|
| **ID** | TASK-2902 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** — `src/SharedKernel/**/Segmentation/**` is not in any single domain agent's declared scope; per AGENTS.md L46-51 the cross-domain authorized convention applies (S22 TASK-2205 / S24 TASK-2206 precedent). Orchestrator-direct fallback if surface stays small. |
| **Components** | src/SharedKernel/StatsTid.SharedKernel/Segmentation/ |
| **KB Refs** | ADR-020 D1 (5 components); ADR-016 D5b/D10 |
| **Refinement section** | TASK-2902 (refinement L45-54) |
| **Dependencies** | none (parallel-eligible with TASK-2901) |

**Description**: New seam `IPlannerEnrollment.RegisterSnapshotContract(string contractKey, Func<EmploymentProfile, object?> hydrator)`. `PeriodPlanner.Plan(...)` gains `IPlannerEnrollment? enrollment = null` + `EmploymentProfile? profile = null` parameters (LOCKED — `Plan()` parameter, NOT `BoundarySources` extension per cycle 1 R-B2 absorption). `HasAnySnapshotContract` consults BOTH `ruleSet` AND `enrollment`. `sharedSnapshot` allocation at `PeriodPlanner.cs:128-130` fires when EITHER `anyContract` OR `enrollment != null && profile != null` (per cycle 3 R3-W3 absorption — without this, the throw at TASK-2907 fires on the first production replay). Hydrator iteration after construction; null-profile = silent skip per ADR-020 D1.5. Plus +1 unit test asserting `Plan(..., enrollment: null, profile: null)` produces a valid `PlannedCalculation` for backward-compat.

**Implementer note** (Step 0b R-N1 absorption): ADR-020 D1 component 5 wording uses `SegmentSnapshot.NonDatedSourceValues[contractKey]`, but the actual `SegmentSnapshot` record (`src/SharedKernel/StatsTid.SharedKernel/Segmentation/SegmentSnapshot.cs:22`) only has a `Values` property. **Follow the sprint log + refinement wording (`Snapshot.Values["WtmNaturalKey"]`) — NOT the ADR-020 wording.** ADR-020 has a stale property-name reference; an ADR-020 cleanup edit is a Phase 3 candidate but not blocking S29 implementation.

**Validation**:
- [ ] `IPlannerEnrollment` interface added; default in-memory implementation
- [ ] `PeriodPlanner.Plan(...)` accepts the two new optional parameters
- [ ] `HasAnySnapshotContract` consults enrollment-plus-ruleSet
- [ ] `sharedSnapshot` allocation gate at `PeriodPlanner.cs:128-130` includes enrollment branch
- [ ] +1 unit test for direct-planner backward-compat
- [ ] All ~20 existing direct-planner test call-sites compile unchanged

#### TASK-2903 — New outbox event `WageTypeMappingSuperseded` (PAT-004 / DEP-003)

| Field | Value |
|-------|-------|
| **ID** | TASK-2903 |
| **Status** | pending |
| **Agent** | Data Model Agent (event records own SharedKernel.Events; registry is in Infrastructure) |
| **Components** | src/SharedKernel/StatsTid.SharedKernel/Events/, src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs |
| **KB Refs** | PAT-004; DEP-003; ADR-018 D14 (planned in TASK-2910) |
| **Refinement section** | TASK-2903 (refinement L56-60) |
| **Dependencies** | none (parallel-eligible) |

**Description**: Add `WageTypeMappingSuperseded : IEvent` record under `SharedKernel/Events/`. Register in `Infrastructure/EventSerializer.cs` (NOT SharedKernel/Events/ — that contains the records, not the registry; cycle 2 R2-W1 absorption). EventSerializer typeof count 47 → 48. S18 reflection-coverage test catches misses.

**Validation**:
- [ ] `WageTypeMappingSuperseded` record added with required fields (TimeType, OkVersion, AgreementCode, Position, ActorId, ActorRole, CorrelationId, plus version metadata)
- [ ] Registered in `EventSerializer._eventTypeMap`; typeof count = 48
- [ ] S18 reflection-coverage test passes

#### TASK-2904 — `WageTypeMappingRepository` extensions

| Field | Value |
|-------|-------|
| **ID** | TASK-2904 |
| **Status** | pending |
| **Agent** | Data Model Agent |
| **Components** | src/Infrastructure/StatsTid.Infrastructure/WageTypeMappingRepository.cs |
| **KB Refs** | ADR-020 D2 (invariant + recommended pattern); ADR-017 D2 (S22 SupersedeAndCreateAsync precedent at LocalAgreementProfileRepository.cs:247-338); ADR-018 D7 (row-version optimistic concurrency preserved) |
| **Refinement section** | TASK-2904 (refinement L62-86) |
| **Dependencies** | TASK-2901 (schema). NOT TASK-2903 — repo only emits SQL; the new event type is consumed by TASK-2908 endpoint, not by the repo. TASK-2904 + TASK-2903 are parallel-eligible after TASK-2901 (Step 0b R-W3 absorption). |

**Description**: New read method `GetByKeyAtAsync` with LOCKED SQL shape (replicates `GetMappingAsync:39-67` position-fallback at the dated read per cycle 1 R-B1). New mutating overload `SupersedeAndCreateAsync(conn, tx, newMapping, expectedCurrentVersion?, ct)` mirroring S22 precedent — same-day vs cross-day routing per `newMapping.EffectiveFrom == predecessor.EffectiveFrom` comparison; NO clock dependency in the repo (clock at endpoint per Assumption #14). New `SoftDeleteAsync(conn, tx, naturalKey, expectedVersion, closeDate, ct)` setting `effective_to = closeDate`. v3 `UpdateAsync` becomes thin shim delegating to `SupersedeAndCreateAsync` (cycle 2 R2-W3 / Assumption #15 reconciled). Existing `GetByKeyAsync`, `GetAllAsync`, `GetByAgreementAsync` add `WHERE effective_to IS NULL` filter (ADR-020 Implications §8 admin-list current-row only). Existing v3 hard `DeleteAsync` REMOVED.

**Validation**:
- [ ] `GetByKeyAtAsync` SQL matches refinement L62-76 exactly (fallback + dated predicates combined)
- [ ] `SupersedeAndCreateAsync` routes same-day → in-place UPDATE; cross-day → close + INSERT (single tx)
- [ ] `SoftDeleteAsync` added; closes via `effective_to = closeDate`
- [ ] 3 admin-list reads filtered to current-row only
- [ ] v3 hard `DeleteAsync` removed

#### TASK-2905 — Test fixture DDL drift coordinated with TASK-2901

| Field | Value |
|-------|-------|
| **ID** | TASK-2905 |
| **Status** | pending |
| **Agent** | Test & QA Agent |
| **Components** | tests/StatsTid.Tests.Regression/Outbox/, tests/StatsTid.Tests.Regression/Segmentation/, tests/StatsTid.Tests.Regression/Infrastructure/ |
| **KB Refs** | none (mechanical fixture update mirroring init.sql) |
| **Refinement section** | TASK-2905 (refinement L88-99) |
| **Dependencies** | TASK-2901 |

**Description**: Update DDL in 4 production-DDL-mirror fixtures to match the new `wage_type_mappings` schema + `wage_type_mapping_audit` CHECK widening (cycle 1 C-B2 + cycle 3 C3-W1 absorption pinned at 4 sites): (i) `Outbox/ForcedRollbackHarness.cs:265, 317`; (ii) `WageTypeMappingRegressionTests.cs:38, 107`; (iii) `Segmentation/TestFixtures.cs:114, 330`; (iv) `Infrastructure/TxContractTests.cs:208`. `S25VersionMigrationTests.cs` excluded (testing S25 migration in isolation; intentionally pre-S25 baseline).

**Validation**:
- [ ] All 4 fixtures updated in step
- [ ] Greps for `<2020-` date literals in the 4 affected fixtures = 0
- [ ] `dotnet test` passes the regression suite against the new schema

#### TASK-2906 — Init.sql seed rewrite (ADR-020 D3, 10 statements)

| Field | Value |
|-------|-------|
| **ID** | TASK-2906 |
| **Status** | pending |
| **Agent** | Data Model Agent |
| **Components** | docker/postgres/init.sql (lines L198, L272, L746, L789, L804, L819, L834, L1020, L1262, L1282) |
| **KB Refs** | ADR-020 D3 |
| **Refinement section** | TASK-2906 (refinement L101-114) |
| **Dependencies** | TASK-2901 |

**Description**: Each of the 10 unguarded `INSERT INTO wage_type_mappings` statements gains explicit `effective_from = '2020-01-01'` columns + `ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING` clauses. Idempotent under fresh bootstrap, immediate re-run, intervening admin edits, and admin-soft-deleted seeds (admin-delete-stays-deleted per ADR-020 D3 pre-launch posture).

**Validation**:
- [ ] All 10 INSERTs rewritten with explicit `effective_from = '2020-01-01'` + ON CONFLICT
- [ ] `docker compose down -v && docker compose up postgres -d --force-recreate` idempotent
- [ ] D-test #10 verifies (TASK-2909)

### Phase 2 — Endpoint + payroll integration (after Phase 1 commit; worktree-parallel-eligible)

#### TASK-2907 — `PCS.MapSegmentToExportLinesAsync` + enrollment wire-up (ADR-020 D1 component 2 + Implications §7)

| Field | Value |
|-------|-------|
| **ID** | TASK-2907 |
| **Status** | pending |
| **Agent** | Payroll Integration Agent |
| **Components** | src/Integrations/StatsTid.Integrations.Payroll/Services/ |
| **KB Refs** | ADR-020 D1 (planner enrollment); ADR-016 D5b (fourth-pattern export-time effective-date lookup); ADR-016 D10 (replay determinism) |
| **Refinement section** | TASK-2907 (refinement L116-122) |
| **Dependencies** | TASK-2901, TASK-2902, TASK-2904 |

**Description**: PCS L585 instantiates an `IPlannerEnrollment` and registers `WtmNaturalKey` with hydrator returning `(OkVersion, AgreementCode, Position)` from `EmploymentProfile`; passes both to `Plan(...)`. `PayrollMappingService.GetMappingAsync` adds optional `DateOnly? asOfDate = null`; null → current-row (existing behavior, used by `MapCalculationResultAsync` per Implications §7); supplied → routes to `GetByKeyAtAsync`. PCS L1175-1177 reads natural-key triple from `plannedSegment.Snapshot.Values["WtmNaturalKey"]` (LOCKED — cycle 1 C-B1 + cycle 2 R2-B2); throws `InvalidOperationException` on snapshot-missing (NO fallback to `segmentProfile`); passes `asOfDate: segmentStart`. NO `IsTestDirect` discriminator (cycle 2 R2-B3). Inline comment at `PayrollMappingService.cs:143` documents `MapCalculationResultAsync` non-change with cross-cite to ADR-020 §7 + ROADMAP L350.

**Validation**:
- [ ] PCS L585 enrollment instantiation + `RegisterSnapshotContract("WtmNaturalKey", ...)`
- [ ] `Plan(...)` call passes `enrollment` + `profile`
- [ ] `PayrollMappingService.GetMappingAsync` accepts `DateOnly? asOfDate = null`
- [ ] PCS L1175-1177 reads from `Snapshot.Values["WtmNaturalKey"]`; throws on missing
- [ ] No `IsTestDirect` field on `BoundarySources`
- [ ] `MapCalculationResultAsync:143` unchanged + inline comment added

#### TASK-2908 — `WageTypeMappingEndpoints` rewrite (ADR-020 D2 + cycle 3 same-day-only-edit validator)

| Field | Value |
|-------|-------|
| **ID** | TASK-2908 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** — `src/Backend/**/Endpoints/*.cs` is in scope paths "no single domain agent declares as its scope" per AGENTS.md L46; cross-domain authorized per S22 TASK-2205 / S24 TASK-2206 / S25 TASK-2503-2505 precedent. |
| **Components** | src/Backend/StatsTid.Backend.Api/Endpoints/WageTypeMappingEndpoints.cs |
| **KB Refs** | ADR-020 D2 (3-case routing); ADR-018 D7+D9 (ETag/If-Match preserved); ADR-017 D2 (S22 SupersedeAndCreateAsync precedent) |
| **Refinement section** | TASK-2908 (refinement L124-141) |
| **Dependencies** | TASK-2901, TASK-2903, TASK-2904 |

**Description**: **Same-day-only-edit validator** (cycle 3 user adjudication — symmetric forbid). Both POST and PUT validate `body.EffectiveFrom == today` BEFORE opening tx; `body.EffectiveFrom != today` → 422 (RFC 4918 + explicit predicate-shape contrast with S22). DELETE unaffected. POST handler implements ADR-020 D2 3-case routing under same-tx natural-key `SELECT ... FOR UPDATE WHERE effective_to = today AND natural_key = ...` lock. PUT handler routes through `SupersedeAndCreateAsync` (same-day → in-place UPDATE + UPDATED audit; cross-day → close + INSERT + SUPERSEDED audit). DELETE routes through `SoftDeleteAsync` (`effective_to = today`; DELETED audit; existing `WageTypeMappingDeleted` outbox event). HTTP shape unchanged from S25.

**Validation** (Step 0b C-B1 absorption — explicit AC restoration from refinement L256-264):
- [ ] Same-day-only-edit validator at POST + PUT runs **BEFORE opening the tx** (rejects `body.EffectiveFrom != today` with 422)
- [ ] 422 response body shape: `{ error: "effective_from must equal today (same-day-only edits permitted in S29)", suppliedEffectiveFrom: <body.EffectiveFrom>, today: <today> }`
- [ ] DELETE handler unaffected by validator (no caller-supplied effective_from)
- [ ] POST handler implements ADR-020 D2 3-case routing under same-tx natural-key `SELECT ... FOR UPDATE WHERE effective_to = today AND natural_key = ...` lock. Audit emission per case: A → CREATED; B → CREATED; C → UPDATED. Outbox emission per case: A → Created; B → Created; C → Updated.
- [ ] PUT handler routes through `SupersedeAndCreateAsync`. The validator forces `body.EffectiveFrom == today`, but the predecessor's `effective_from` can be any past day (e.g., `'2020-01-01'` for seeds), so the repo still routes between two branches based on `newMapping.EffectiveFrom == predecessor.EffectiveFrom`: same-day edit (predecessor created today) → in-place UPDATE + UPDATED audit + `WageTypeMappingUpdated` outbox event; cross-day edit (predecessor `effective_from < today`) → close predecessor (`effective_to = today`) + INSERT new row (`effective_from = today, version = 1`) + SUPERSEDED audit + `WageTypeMappingSuperseded` outbox event. The cycle 3 symmetric-forbid validator only restricts the *requested* `effective_from` (to today); the predecessor's age determines the routing.
- [ ] DELETE handler routes through `SoftDeleteAsync`. Sets `effective_to = today`. DELETED audit. WageTypeMappingDeleted event. 204 on success preserved.
- [ ] ETag/If-Match 412 (stale) / 428 (missing) preserved
- [ ] HTTP shape unchanged from S25 — clients send the same payloads, get back same responses + ETags

#### TASK-2909 — D-test suite (~12 D-tests + 1 unit test)

| Field | Value |
|-------|-------|
| **ID** | TASK-2909 |
| **Status** | pending |
| **Agent** | Test & QA Agent |
| **Components** | tests/StatsTid.Tests.Regression/Config/, tests/StatsTid.Tests.Regression/Segmentation/ReplayDeterminismTests.cs, tests/StatsTid.Tests.Unit/Segmentation/ |
| **KB Refs** | ADR-020 D1+D2+D3; ADR-016 D10 (replay determinism); ADR-018 D7 (ETag/If-Match) |
| **Refinement section** | TASK-2909 (refinement L143-167) |
| **Dependencies** | TASK-2901..2908 (Phase 1 + Phase 2 implementation) |

**Description**: 12 D-tests under `Config/` (mirroring S21 ProfileSupersessionTests location) + extension to existing `Segmentation/ReplayDeterminismTests.cs` for the marquee + 1 unit test under `Unit/Segmentation/` for direct-planner backward-compat:

1. Same-day in-place UPDATE preserves natural-key + bumps version
2. Cross-day supersession (closed predecessor + new row + SUPERSEDED audit + Superseded event)
3. `GetByKeyAtAsync(asOfDate)` correctness across 3 cases
4. D2 Case B (DELETE-then-CREATE-same-day, predecessor `effective_from < today`)
5. D2 Case C (CREATE-DELETE-CREATE-same-day, predecessor `effective_from == today`)
6. **MARQUEE replay determinism**: extends `ReplayDeterminismTests.cs:34`; closes the deferred caveat at L14-23
7. ETag/If-Match contract preserved (412 stale, 428 missing, 204 soft-delete)
8. Backfill idempotency
9. Partial-unique-index enforcement (concurrent same-day INSERTs → 23505)
10. Seed-INSERT idempotency under accumulated history
11. **Race-safety** (cycle 1 C-B3 / R-W1): direct-repo orchestration. (a) DELETE↔POST race; (b) POST↔POST on empty natural key
12. **Same-day-only-edit validator** (cycle 3): past + future + same-day boundary cases

Plus +1 unit test for `Plan(enrollment: null, profile: null)` direct-planner backward-compat (cycle 1 C-W2).

**Validation** (Step 0b C-B1 absorption — explicit AC restoration from refinement L284-301):
- [ ] All 12 D-tests added under `[Trait("Category", "Docker")]` and placed under `tests/StatsTid.Tests.Regression/Config/` (D-tests #1-5, #7-12 mirroring `ProfileSupersessionTests.cs` location); D-test #6 (marquee) extends `tests/StatsTid.Tests.Regression/Segmentation/ReplayDeterminismTests.cs` in-place.
- [ ] D-test #6 marquee: seed manifest at `[2026-01-01, 2026-01-31]`, edit WTM mid-month, replay against old manifest, **assert byte-identical `ExportLines` (hours sum AND wage-type column)**.
- [ ] D-test #6: `ReplayDeterminismTests.cs:14-23` deferred-caveat block REMOVED + assertion tightened (per refinement L289 — "compare `forward1.ExportLines.Sum(l => l.Hours)` against `replay.ExportLines.Sum(l => l.Hours)` after WTM mutation").
- [ ] D-test #11a + #11b use **direct-repo orchestration** (NOT HTTP-level): two `NpgsqlConnection`s + per-thread tx; thread A acquires row lock via `SELECT ... FOR UPDATE`; thread B blocks; assertion fires after thread A commits. S22 `ProfileSupersessionTests` precedent.
- [ ] D-test #12 has 3 sub-tests covering past, future, and same-day boundary cases: (i) POST with `effective_from = 2025-06-01` → 422; (ii) POST + PUT with `effective_from = today + 30` → 422; (iii) `effective_from == today` → 201 / 200 (routes to D2 case routing). Verify error body shape includes `suppliedEffectiveFrom` + `today` for both rejection cases.
- [ ] +1 unit test under `tests/StatsTid.Tests.Unit/Segmentation/` for `Plan(enrollment: null, profile: null)` direct-planner backward-compat (cycle 1 C-W2).
- [ ] Total Docker-gated: 147 → 159 (+12); Total unit: 525 → 526 (+1).

### Phase 3 — Documentation + sprint plumbing

#### TASK-2910 — ADR consequence updates (3 ADRs touched)

| Field | Value |
|-------|-------|
| **ID** | TASK-2910 |
| **Status** | pending |
| **Agent** | Orchestrator-direct (KB writes are Orchestrator-only per WORKFLOW.md L48) |
| **Components** | docs/knowledge-base/decisions/ADR-018-...md, docs/knowledge-base/decisions/ADR-016-...md, docs/knowledge-base/INDEX.md |
| **KB Refs** | ADR-018 (D14 NEW); ADR-016 (D5b reconciliation); ADR-019 (already amended at S28) |
| **Refinement section** | TASK-2910 (refinement L169-176) |
| **Dependencies** | Phase 1 + Phase 2 implementation complete |

**Description**: ADR-018 D14 NEW (additive within accepted family — no Status bump; cycle 8 entry in Review History). ADR-016 D5b reconciliation paragraph (S29 chooses fourth pattern: export-time effective-date lookup). ADR-019 amendment already landed at S28 TASK-2802.

**Validation**:
- [ ] ADR-018 D14 added with full Decision/Rationale/Consequences format
- [ ] ADR-016 D5b reconciliation paragraph appended
- [ ] INDEX.md updated

#### TASK-2911 — Sprint plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-2911 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | docs/sprints/SPRINT-29.md, docs/sprints/INDEX.md, ROADMAP.md, docs/QUALITY.md |
| **KB Refs** | n/a |
| **Refinement section** | TASK-2911 (refinement L180-186) |
| **Dependencies** | Phase 1 + Phase 2 + Phase 3 (TASK-2910) complete |

**Description**: Update this SPRINT-29.md sprint log (Test Verified, Sprint-end HEAD, Status flip to complete, Sprint Retrospective, External Review Step 7a outcomes). Update INDEX.md S29 row. ROADMAP.md Phase 4d-1 entry → COMPLETE. QUALITY.md Payroll integration domain re-graded.

**Validation** (Step 0b C-B1 absorption — sprint-end build/test gates restored from refinement L302-307):
- [ ] `dotnet build` clean (0 errors; 19 pre-existing CS0618 warnings unchanged)
- [ ] **Step 7a Codex review on full diff vs `8950893` clean** (≤2 cycles per cycle-cap discipline; if cycle 2 surfaces same-area-deeper-layer findings, halt + prompt user per `feedback_thrash_defer_real_world.md`)
- [ ] 526 unit tests pass (525 baseline + 1 new from TASK-2902)
- [ ] 35 plain regression tests still pass
- [ ] 159 Docker-gated regression tests pass (147 baseline + 12 new from TASK-2909)
- [ ] 88 frontend vitest tests still pass without modification (frontend-unchanged AC per ADR-020 Open §Phase-4d-1-frontend-view-history)
- [ ] **Total: 526 + 35 + 159 + 88 = 808 tests** (was 795 at S29 open; +13 net new)
- [ ] SPRINT-29.md status = complete; sprint-end HEAD recorded; Sprint Retrospective written
- [ ] INDEX.md S29 row added
- [ ] ROADMAP Phase 4d-1 marked COMPLETE; Phase 4d-2 promoted to "next"
- [ ] QUALITY.md re-graded (Payroll integration domain re-graded after WTM replay-determinism gap closes)
- [ ] MEMORY.md sprint log line for S29 added (separate edit; outside repo at `~/.claude/projects/C--StatsTid/memory/`)

## Legal & Payroll Verification

- **OK version transitions**: unchanged (P4 preserved per Architectural Constraints)
- **Wage-type mapping**: per-line-date (`MapCalculationResultAsync`) stays current-row per ROADMAP L350 "no retroactive recomputation"; per-segment (`MapSegmentToExportLinesAsync`) becomes replay-stable via `asOfDate=segmentStart`
- **SLS export**: file format unchanged; mapping resolution becomes deterministic for past segments
- **Audit chain**: D2 routing preserves audit honesty (Case B [DELETE, reopen] gap reflected; Case C collapsed-intent semantic)

## External Review (Step 7a)

_Pending — runs at sprint end on full diff vs `8950893`._

## Test Summary

_Pending — target: 526 unit + 35 plain regression + 159 Docker-gated + 88 frontend vitest = 808 total. Baseline: 525 + 35 + 147 + 88 = 795 at S28 close. Net delta: +1 unit + +12 Docker-gated = +13._

## Agent Effectiveness

_Pending — tracked at sprint close per WORKFLOW.md L227 metrics._

## Sprint Retrospective

_Pending — written at sprint close._
