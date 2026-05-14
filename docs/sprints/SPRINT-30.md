# Sprint 30 — Phase 4d-2: Entitlement-Policy Versioned History + S29 Test-Harness Unblock

| Field | Value |
|-------|-------|
| **Sprint** | 30 |
| **Status** | **in-progress** (opened 2026-05-14) |
| **Start Date** | 2026-05-14 |
| **End Date** | _filled by TASK-3012_ |
| **Orchestrator Approved** | no (sprint open) |
| **Build Verified** | _filled by TASK-3012_ |
| **Test Verified** | _filled by TASK-3012_ |
| **Sprint-start commit base** | `41b6e89` (S29 sprint close, 2026-05-11) |
| **Sprint-end HEAD** | _filled by TASK-3012_ |
| **Sprint type** | **IMPLEMENTATION** — implements Phase 4d-2 backend versioned history + new admin CRUD + new admin UI page, against new ADR-021 (drafted in-sprint). Folds gated Phase-4e mini-task TASK-3001 (WebApplicationFactory<Program> diagnosis, 1-day timebox). |
| **Refinement** | `.claude/refinements/REFINEMENT-s30-scope.md` (Step 3 proposal + Step 4 cycles 1+2 dual-lens; both lenses converged with 0 BLOCKERs at cycle 2 — second cycle-2-converging-finite case after S29) |
| **Plan** | `.claude/plans/PLAN-s30.md` (Step 0a) — passed Step 0b cycles 1–3 (cycle-3 user-granted waiver after wording-only BLOCKER in cycle 2; 0 BLOCKERs at cycle 3; lens convergence reached) |

## Sprint Goal

Implement Phase 4d-2 — entitlement-policy versioned history — extending the ADR-020 patterns proven in S29 (planner-enrollment ✗ does not apply; D2 3-case routing ✓ applies; D3 seed idempotency ✓ applies) onto the entitlement-config surface, with a *consumption-time-lookup* variant (NOT export-time-lookup like S29) because entitlements are read by HTTP endpoints (Skema POST + Balance summary), not by `PeriodCalculationService` segment expansion. Build admin CRUD endpoints + admin UI page (no admin surface exists today; S15 left this as schema-only forward-compat per ROADMAP L312 / init.sql L1419). File new **ADR-021** as a sibling to ADR-020 — NOT a D4 amendment — per ADR-020 §122 explicit anticipation.

**Marquee invariant**: `EntitlementQuotaCheck_UsesYearStartConfig_NotCurrentConfig` — admin edits VACATION quota 25 → 27 today; employee submits absence today for an already-started entitlement year (year-Y); both the Skema quota validation AND the Balance summary report year-Y-start's value (25), not the live value (27). Fails without versioned history; passes with it.

**Folded prerequisite (gated mini-task)**: TASK-3001 — diagnose `WebApplicationFactory<Program>` connection-override timing defect blocking 8 deferred S29 HTTP-level D-tests + `PublisherStallReadYourWriteTests`. 1-day timebox; split outcome per refinement Step 5 / plan Phase 0.

## Architectural Decisions Settled (in refinement + ADR-020 transfer evaluation)

1. **D1 planner-enrollment from ADR-020 does NOT transfer**. Entitlement consumption is HTTP-endpoint-direct (Skema POST + Balance summary), not planner-routed via `SegmentManifest`. ADR-020 §122 + ADR-016 §83 explicitly anticipated this.
2. **D2 3-case routing from ADR-020 transfers as-is** to `EntitlementConfigRepository.SupersedeAndCreateAsync`.
3. **D3 seed idempotency from ADR-020 transfers as-is** (`ON CONFLICT (natural_key, effective_from) DO NOTHING`).
4. **Consumption-time-lookup pattern is new** — to be documented in **ADR-021** (sibling ADR, not a D4 amendment to ADR-020); ADR-016 D5b extension paragraph adds the "fifth pattern."
5. **`reset_month` + `accrual_model` frozen from admin scope** (Q1 sub-fork (i)) — agreement-defining fields; admins edit via DB superuser only. Endpoint enforces with 422.
6. **Two-step consumption pattern**: live-row read for frozen `ResetMonth` → derive `entitlementYearStart` → dated read `GetByTypeAtAsync(asOfDate=entitlementYearStart)`. No version-skew because `ResetMonth` is immutable per natural-key.
7. **Stream naming = natural-key**: `entitlement-config-{entitlement_type}-{agreement_code}-{ok_version}` (S29 WTM precedent). One stream per supersession lineage.
8. **Cycle-3 same-day-only-edit validator** (S29 precedent) — POST/PUT reject `effective_from != today` with 422.

## Entropy Scan Findings

Per WORKFLOW.md Step 0a (2026-05-14):

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | DEFERRED | Full path-walk is Phase 4e candidate (carried from S29) |
| FAIL-001 (`FindFirst("scopes")`) regression | _verified at sprint open_ | _check on Phase 1 dispatch_ |
| Hardcoded `http://localhost` in non-test code | _verified at sprint open_ | _check on Phase 1 dispatch_ |
| Endpoint `RequireAuthorization` coverage | _verified at sprint open_ | _check on Phase 1 dispatch_ |
| MEMORY.md drift | CLEAN | Synchronized through S29 close per session context |
| QUALITY.md re-grade | DEFERRED | S30 closes Entitlement domain admin-CRUD gap → re-grade after sprint close |

No DRIFT items requiring fix before sprint open. No DEBT items added.

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — task touches P1 (Architectural integrity — schema migration + new admin surface + new ADR), P3 (Event sourcing — 3 new event types), P4 (Version correctness — ADR-019 D8 audit-version transitions), P6 (Payroll integration — entitlement quota check feeds into payroll-adjacent flows), P7 (Security — new GlobalAdminOnly endpoints), and is cross-domain (Data Model + Backend.Api + Frontend + KB authorship). |
| **External Codex** | invoked 2026-05-14 — 3 cycles; cycle 1 (1B/3W/4N), cycle 2 (1B/2W/2N — mechanical wording BLOCKER), cycle 3 (0B/2W/2N — residual cascade WARNINGs all absorbed). User-granted cycle-cap waiver before cycle 3 (S21 precedent). |
| **Internal Reviewer** | invoked 2026-05-14 — 1 cycle (0B/4W/9N). All WARNINGs absorbed. No re-invocation required (0 BLOCKER throughout). |
| **BLOCKERs resolved before Step 1** | yes — cycle 1 Codex BLOCKER (domain-ownership labels) fixed; cycle 2 Codex BLOCKER (form-1 wording strict compliance) fixed; cycle 3 verified clean. |

### Findings (cycle 1)

_Codex findings:_
- BLOCKER — Task table — TASK-3002/3003/3006 owner labels "Data Model" outside AGENTS.md L14-16 scope — fixed via form-1 relabel "Data Model (extended into ... cross-domain authorized)"
- WARNING — Callout 5 — Audit-table mirror claim wrong (`wage_type_mapping_audit` base DDL lacks version columns + correlation_id) — fixed to "post-S25 migration shape"
- WARNING — TASK-3007 — RBAC name "GlobalAdmin" wrong; policy is `GlobalAdminOnly` — fixed
- WARNING — Test-count math — `+15` floor + `+8` unblocked = 830 not 822; admin-CRUD bucket undercounted GET-by-key — fixed via projection-table rewrite (823 floor; 831 stretch; 835 stretch+frontend)

_Internal Reviewer findings:_
- WARNING — Phase decomposition — Phase 4 (Validation) missing — fixed by adding Phase 4 between Phase 3 and Phase 5
- WARNING — Task table — "Worktree-eligible" column misleading vs S29 non-worktree precedent — renamed to "Parallel-dispatchable"; Phase 2 wording tightened
- WARNING — TASK-3010 — conditional dispatch rule needs explicit resolution — fixed via "agent reads TASK-3001 disposition from SPRINT-30.md"
- WARNING — Risk register — fixture-DDL-drift risk implicit; should be explicit — fixed via R9 row
- NOTE-8 absorbed (entitlement_balances non-recomputation close criterion added)
- 8 confirmatory NOTEs (PeriodPlanner unchanged, EventSerializer count 48 verified, consumption sites verified, ADR-021 framing defensible, CS0618=19 verified)

### Findings (cycle 2)

_Codex findings:_
- BLOCKER — Owner relabel still nonconforming — needed AGENTS.md L50 form-1 strict `<primary agent> (extended into <other scope>, cross-domain authorized)` — fixed via TASK-3002/3003/3006 relabel to strict form-1
- WARNING — Step 7a described as optional/skippable, conflicts with WORKFLOW.md L38 — fixed via "Step 7a Strategy" rewrite + Exclusions row reword
- WARNING — SPRINT-30.md creation-vs-read timing contradiction — fixed by adding TASK-3000 at Phase 0 root + leaving TASK-3012 as close-only

### Findings (cycle 3, user-waiver)

_Codex findings:_
- 0 BLOCKERs (relabels confirmed form-1 compliant)
- WARNING — residual "Step 7a optional" references at L52 / L140 — fixed inline
- WARNING — stale "created at sprint open as TASK-3012's first edit" reference at L79 — fixed inline
- NOTE — confirmatory verifications (test-count math now adds; audit-table claim materially correct)

### Resolution

All BLOCKERs resolved through cycle 2 + cycle 3 verification. All WARNINGs absorbed. Plan is READY for Step 1 dispatch.

## Architectural Constraints Verified

_To be checked off as the sprint progresses; final assertion in TASK-3012._

- [ ] **P1 — Architectural integrity** → TASK-3002 schema migration via `schema_migrations` ledger (S22 D8 pattern); TASK-3011 new ADR-021 documenting the consumption-time-lookup variant as a sibling to ADR-020 (NOT a D4 amendment). Bounded contexts respected.
- [ ] **P3 — Event sourcing / auditability** → TASK-3004 3 net-new events (`EntitlementConfigCreated/Superseded/SoftDeleted`); TASK-3002 audit-table CREATE with ADR-019 D8 version-transition columns + CHECK widening for `CREATED/UPDATED/DELETED/SUPERSEDED`.
- [ ] **P4 — Version correctness** → TASK-3007 admin-strict If-Match + 412/428/409 distinction per ADR-019 D2/D5/D6/D8; row-version stays load-bearing on the live-edit path.
- [ ] **P6 — Payroll integration correctness** → TASK-3008 two-step consumption pattern preserves entitlement-year-start invariant; existing `entitlement_balances` rows are NOT retroactively recomputed (forward-only).
- [ ] **P7 — Security and access control** → TASK-3007 `.RequireAuthorization("GlobalAdminOnly")` per S29 WageTypeMappingEndpoints precedent.

Not directly affected: P2 (rule engine determinism unchanged), P5 (no inter-service contract changes), P8 (CI unchanged), P9 (admin UI is a new surface, not an existing-UX refactor — Phase 5 polish unaffected).

## Task Log

13 declared tasks + 1 conditional TASK-3001b. Plan file `.claude/plans/PLAN-s30.md` is source-of-truth for detailed specifications.

### Phase 0 — Sprint-Open + Harness Diagnosis

#### TASK-3000 — Sprint-open plumbing (create SPRINT-30.md + INDEX.md provisional row)

| Field | Value |
|-------|-------|
| **ID** | TASK-3000 |
| **Status** | in-progress |
| **Agent** | Orchestrator-direct (KB / sprint log writes per WORKFLOW.md L48-49) |
| **Components** | docs/sprints/SPRINT-30.md, docs/sprints/INDEX.md |
| **KB Refs** | n/a |
| **Plan section** | Phase 0 — TASK-3000 (PLAN-s30.md) |
| **Dependencies** | none (Phase 0 root) |

**Description**: Create SPRINT-30.md from TEMPLATE.md with sprint metadata + sprint goal + provisional task-log skeleton (TASK-3000..3012 reserved rows). Update INDEX.md with provisional in-progress row.

**Validation Criteria**:
- [x] SPRINT-30.md exists at `docs/sprints/SPRINT-30.md`
- [x] INDEX.md row added with status=in-progress
- [ ] Commit lands at sprint-open

---

#### TASK-3001 — WebApplicationFactory<Program> diagnosis (1-day timebox)

| Field | Value |
|-------|-------|
| **ID** | TASK-3001 |
| **Status** | complete (TRIVIAL — 2026-05-14) |
| **Agent** | Test & QA |
| **Components** | tests/StatsTid.Tests.Regression/Hosting/ (or wherever the WAF fixture lives) — diagnosis only, no code change |
| **KB Refs** | n/a (diagnostic) |
| **Plan section** | Phase 0 — TASK-3001 (PLAN-s30.md) |
| **Dependencies** | TASK-3000 |

**Description**: Diagnose root cause of `WebApplicationFactory<Program>` connection-override defect. Currently blocks 8 S29 HTTP-level D-tests + `PublisherStallReadYourWriteTests` (S27). Disposition outcomes: **trivial** → TASK-3001b fix in-sprint; **non-trivial** → split to S31, S30 D-tests use direct-orchestration harness. **1-day timebox**.

**Validation Criteria**:
- [x] Root cause hypothesis written to this row (trivial classification or non-trivial classification with rationale)
- [x] If trivial: TASK-3001b conditional task created with file-scope + 1-line description — see "Recommended TASK-3001b shape" section above
- [ ] If non-trivial: S31 carry-forward note added to ROADMAP Phase 4e candidates — N/A (TRIVIAL)

**Files Changed**: none (read-only diagnostic). Diagnosis run on 2026-05-14 against base `41b6e89`.

##### Root cause hypothesis

`StatsTidWebApplicationFactory.ConfigureWebHost` adds the test container's connection string via `IWebHostBuilder.ConfigureAppConfiguration(...)` — but `Program.cs:11-12` reads `builder.Configuration.GetConnectionString("EventStore")` **at builder construction time**, BEFORE any `ConfigureWebHost` callback has fired. The local variable `connectionString` therefore resolves to the production default `Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev`, and `Program.cs:15`'s `new DbConnectionFactory(connectionString)` captures that default into the DI singleton. Every repository constructed downstream (in the test) tries `127.0.0.1:5432`, ignoring the testcontainer.

Verified by running `StatsTidWebApplicationFactoryTests.Harness_BootsBackendApi_AndStopsPublisherCleanly` against Docker (test logger shows the testcontainer DID start successfully — log line `[testcontainers.org 00:00:08.28] Delete Docker container 5407338b2116`). Failure: `Npgsql.NpgsqlException: Failed to connect to 127.0.0.1:5432` raised from `AgreementConfigSeeder.SeedAsync` at `Program.cs:75`. The 127.0.0.1:5432 target is the production default fallback in Program.cs:12, NOT the Testcontainers-published port — confirming that the `ConfigureAppConfiguration` override was applied too late in the WAF lifecycle (after `builder.Configuration` was already consulted).

This is a well-known .NET 8 `WebApplicationFactory<Program>` + top-level-statements + `WebApplication.CreateBuilder` pitfall: app-configuration callbacks layered via `IWebHostBuilder.ConfigureAppConfiguration` only affect configuration read **after** the host is built, NOT configuration read at builder time inside the top-level statements of Program.cs. The same failure mode reproduces against `PublisherStallReadYourWriteTests` (S27) and against the 8 S29 deferred HTTP-level D-tests in `WageTypeMappingEndpointTests` + `WageTypeMappingIdempotencyTests` (`WageTypeMappingIdempotencyTests.SeedInsertIdempotency_...` calls `ApplyFullSchemaAsync` only — which succeeds — but the harness-using sibling tests share the failure mode).

##### Classification (trivial / non-trivial)

**TRIVIAL** — single-file fix inside `tests/StatsTid.Tests.Regression/Hosting/StatsTidWebApplicationFactory.cs`. Estimable <1h. No production code change required. No architectural rework. Two equally well-known fix shapes (either works; choose by preference):

- **Option A (preferred — minimal & idiomatic):** override `ConfigureWebHost` to use `ConfigureHostConfiguration(...)` instead of (or in addition to) `ConfigureAppConfiguration(...)`. Host-configuration is materialized BEFORE the WebApplicationBuilder's app-configuration chain reads it at `builder.Configuration.GetConnectionString("EventStore")`. Code shape:
  ```csharp
  protected override IHost CreateHost(IHostBuilder builder)
  {
      builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
          new Dictionary<string, string?> { ["ConnectionStrings:EventStore"] = _connectionString }));
      return base.CreateHost(builder);
  }
  ```
  Plus delete or keep-as-belt-and-braces the existing `ConfigureWebHost` override.

- **Option B (alternative, slightly more code):** in `ConfigureWebHost`, also `ConfigureTestServices(svc => { svc.RemoveAll<DbConnectionFactory>(); svc.AddSingleton(new DbConnectionFactory(_connectionString)); })`. This swaps the DI registration AFTER Program.cs's wiring runs, replacing the singleton that captured the production-default string. Repositories built lazily downstream will see the new factory.

Both are 5-15 LOC; both are textbook patterns. Option A is preferred because it fixes the cause (config-source layering) rather than papering over it via DI replacement; Option B works defensively if other Program.cs sites read configuration at builder time for non-connection-string values.

##### Recommended TASK-3001b shape

Create conditional `TASK-3001b` in Phase 0 with Backend API / Test & QA shared scope (Test & QA preferred — single-file edit in `tests/`):

> Add `protected override IHost CreateHost(IHostBuilder builder)` to `StatsTidWebApplicationFactory` that calls `builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:EventStore"] = _connectionString }))` then `return base.CreateHost(builder)`. Re-run `StatsTidWebApplicationFactoryTests.Harness_BootsBackendApi_AndStopsPublisherCleanly` + `PublisherStallReadYourWriteTests.*` + `WageTypeMappingEndpointTests.*` (the 8 S29 deferred HTTP-level D-tests) and confirm all pass. Expected ≥10 newly-passing Docker-gated tests.

This unblocks **all** WAF-based tests in one stroke. If TASK-3001b is taken, TASK-3010's admin-CRUD D-tests can be written HTTP-level via `WebApplicationFactory<Program>` per the S25/S29 pattern, matching the +8 stretch test target.

##### Reproduction command

```
docker ps   # ensure Docker daemon is up
dotnet build tests/StatsTid.Tests.Regression/StatsTid.Tests.Regression.csproj
dotnet test tests/StatsTid.Tests.Regression/StatsTid.Tests.Regression.csproj --filter "FullyQualifiedName~StatsTidWebApplicationFactoryTests" --no-build --logger "console;verbosity=detailed"
```

Expected exit code 1; stack trace pointing at `AgreementConfigSeeder.SeedAsync` (`Program.cs:75`) with `Failed to connect to 127.0.0.1:5432` as the inner exception. Run time ~9 s (most of which is Testcontainers Postgres cold start). The 127.0.0.1:5432 target — NOT the dynamic Testcontainers port — is the smoking gun: the test container did start successfully (visible in test log immediately above the failure), but Program.cs read the production-default connection string before WAF's `ConfigureAppConfiguration` override took effect.

---

### Phase 1 — Plumbing (sequential, 5 commits — commit before Phase 2 dispatch)

#### TASK-3002 — Schema migration `s30-d2-ec-effective-dating` + `entitlement_config_audit` CREATE

| Field | Value |
|-------|-------|
| **ID** | TASK-3002 |
| **Status** | pending |
| **Agent** | Data Model (extended into Database Schema, cross-domain authorized) — AGENTS.md L50 form-1 |
| **Components** | docker/postgres/init.sql |
| **KB Refs** | ADR-020 D3 (seed idempotency), ADR-018 D8 (ledger pattern), ADR-019 D8 (audit version transitions), ADR-017 D2.1 (partial-unique-index pattern) |
| **Plan section** | Phase 1 — TASK-3002 + Callout 5 |
| **Dependencies** | TASK-3000 (sprint log exists) |

**Description**: One-shot guarded migration `s30-d2-ec-effective-dating`. (a) ALTER `entitlement_configs` add `effective_from DATE NOT NULL DEFAULT '0001-01-01'` + `effective_to DATE NULL`; add partial-unique-index `(entitlement_type, agreement_code, ok_version) WHERE effective_to IS NULL`; add history-unique-index on `(entitlement_type, agreement_code, ok_version, effective_from)`; drop the existing 3-column UNIQUE constraint. Greenfield-friendly: pre-bake the new shape into the base `CREATE TABLE` at L1109-1124 (S29 TASK-2912 lesson) so fresh `docker compose down -v && up` produces the migrated schema without traversing the ALTER ledger block. (b) `CREATE TABLE entitlement_config_audit` (singular, mirroring `wage_type_mapping_audit` post-S25 shape): `audit_id BIGSERIAL PK`, `config_id UUID NOT NULL`, `entitlement_type/agreement_code/ok_version` denormalized, `action TEXT CHECK (action IN ('CREATED','UPDATED','DELETED','SUPERSEDED'))`, `previous_data JSONB`, `new_data JSONB`, `version_before BIGINT NULL`, `version_after BIGINT NULL`, `actor_id TEXT NOT NULL`, `actor_role TEXT NOT NULL`, `timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW()`. No `correlation_id` (verified absent from `wage_type_mapping_audit`). Migration ledger entry `s30-d2-ec-effective-dating` in `schema_migrations`.

**Validation Criteria**:
- [ ] `docker compose down -v && docker compose up postgres -d --force-recreate` succeeds idempotently across multiple re-runs
- [ ] `\d entitlement_configs` shows new shape (effective_from + effective_to + partial-unique-index + history-unique-index)
- [ ] `\d entitlement_config_audit` shows new audit table
- [ ] Audit CHECK includes `SUPERSEDED` in allowed set
- [ ] All 30 existing seed rows backfilled with `effective_from = '0001-01-01'`, `effective_to = NULL`

---

#### TASK-3003 — `EntitlementConfigRepository` extensions

| Field | Value |
|-------|-------|
| **ID** | TASK-3003 |
| **Status** | pending |
| **Agent** | Data Model (extended into Infrastructure, cross-domain authorized) — AGENTS.md L50 form-1; S29 precedent on `WageTypeMappingRepository` |
| **Components** | src/Infrastructure/StatsTid.Infrastructure/EntitlementConfigRepository.cs |
| **KB Refs** | ADR-020 D2 (3-case routing), ADR-018 D5 (atomic outbox `(conn, tx)` overload), ADR-019 D5 (admin-strict If-Match precondition surface), ADR-017 D2.1 (lock-then-validate pattern) |
| **Plan section** | Phase 1 — TASK-3003 |
| **Dependencies** | TASK-3002 |

**Description**: Add 5 methods (S29 pattern): `GetByTypeAtAsync(type, agreement, okVersion, asOfDate, ct)` dated read; `GetByAgreementAtAsync(agreement, okVersion, asOfDate, ct)` dated bulk read; `(conn, tx)` overloads for atomic-outbox + audit insertion; `SupersedeAndCreateAsync(conn, tx, ...)` implementing ADR-020 D2 3-case routing under `SELECT ... FOR UPDATE`; `SoftDeleteAsync(conn, tx, ...)`. Existing `GetByTypeAsync` + `GetByAgreementAsync` migrated to dated-read internals (live = today as `asOfDate`) for backward compat with non-Skema/Balance call sites.

**Validation Criteria**:
- [ ] 5 new methods added with `(conn, tx)` overloads matching S29 shape
- [ ] All existing call sites compile unchanged
- [ ] Unit-level test for `GetByTypeAtAsync` resolving across effective_from boundary (lightweight; primary D-test coverage at Phase 3)

---

#### TASK-3004 — New events: `EntitlementConfigCreated/Superseded/SoftDeleted` + EventSerializer registration

| Field | Value |
|-------|-------|
| **ID** | TASK-3004 |
| **Status** | pending |
| **Agent** | Data Model |
| **Components** | src/SharedKernel/StatsTid.SharedKernel/Events/EntitlementConfigCreated.cs, EntitlementConfigSuperseded.cs, EntitlementConfigSoftDeleted.cs (new); src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs (3 new entries) |
| **KB Refs** | PAT-004 (event registration), DEP-003 (event versioning) |
| **Plan section** | Phase 1 — TASK-3004 |
| **Dependencies** | none (parallel-eligible with TASK-3002/3003 in principle, sequenced for simplicity) |

**Description**: 3 new event records inheriting `DomainEventBase` (S29 WTM precedent). Stream-naming on emission side: natural-key `entitlement-config-{entitlement_type}-{agreement_code}-{ok_version}`. EventSerializer count 48 → 51 (verified at plan-mode).

**Validation Criteria**:
- [ ] 3 new event types compile + serialize round-trip clean
- [ ] EventSerializer count: 48 (pre-S30) → 51 (post-S30)
- [ ] S18 reflection-coverage test passes (catches typeof misses)

---

#### TASK-3005 — Test fixture DDL drift coordinated with TASK-3002

| Field | Value |
|-------|-------|
| **ID** | TASK-3005 |
| **Status** | pending |
| **Agent** | Test & QA |
| **Components** | tests/StatsTid.Tests.Regression/Outbox/ForcedRollbackHarness.cs (if it references entitlement_configs); tests/StatsTid.Tests.Regression/Segmentation/TestFixtures.cs (if it references entitlement_configs); tests/StatsTid.Tests.Regression/Infrastructure/TxContractTests.cs (if it references entitlement_configs); any other entitlement-touching fixture (Grep-verified at decompose) |
| **KB Refs** | none (mechanical fixture update mirroring init.sql) |
| **Plan section** | Phase 1 — TASK-3005 + Risk R9 |
| **Dependencies** | TASK-3002 |

**Description**: Mirror the post-S30 `entitlement_configs` shape + new `entitlement_config_audit` table in any test fixture that hand-rolls DDL. Grep-verify before edit; if 0 sites today, task collapses to assertion. S25 TASK-2508 / ef9ec91 precedent for ForcedRollbackHarness DDL inlining carries forward.

**Validation Criteria**:
- [ ] Grep for "entitlement_configs" + "CREATE TABLE" across tests/ — 0 hits or all updated
- [ ] All Docker-gated tests against post-Phase-1 base still compile

---

#### TASK-3006 — Init.sql seed rewrite (ADR-020 D3 pattern — 30 entitlement seed rows)

| Field | Value |
|-------|-------|
| **ID** | TASK-3006 |
| **Status** | pending |
| **Agent** | Data Model (extended into Database Schema, cross-domain authorized) — AGENTS.md L50 form-1 |
| **Components** | docker/postgres/init.sql (the 30 INSERT VALUES tuple block at L1140-1170) |
| **KB Refs** | ADR-020 D3 (seed idempotency via `ON CONFLICT (natural_key, effective_from) DO NOTHING`) |
| **Plan section** | Phase 1 — TASK-3006 + Risk R3 |
| **Dependencies** | TASK-3002 |

**Description**: Rewrite the 30-row INSERT block to: (a) include `effective_from = '0001-01-01'` in the column list + VALUES tuples; (b) use `ON CONFLICT (entitlement_type, agreement_code, ok_version, effective_from) DO NOTHING` (S29 TASK-2906 pattern). S29 lesson: rewrite ALL VALUES tuples fully, not partially. Verify by `docker compose down -v && up` bootstrap + COUNT(*) assertion.

**Validation Criteria**:
- [ ] All 30 INSERT VALUES tuples include the new `effective_from` column
- [ ] `ON CONFLICT ... DO NOTHING` uses the post-S30 natural-key+effective_from index
- [ ] Fresh-bootstrap COUNT(*) = 30 across multiple `down -v && up` runs

---

### Phase 2 — Endpoint + Consumer + Frontend (parallel-dispatchable after Phase 1 commit, non-worktree, 3 commits)

#### TASK-3007 — `EntitlementConfigEndpoints.cs` new admin CRUD

| Field | Value |
|-------|-------|
| **ID** | TASK-3007 |
| **Status** | pending |
| **Agent** | Backend API (cross-domain authorized) — `src/Backend/**/Endpoints/*.cs` is in scope paths "no single domain agent declares as its scope" per AGENTS.md L46-51 |
| **Components** | src/Backend/StatsTid.Backend.Api/Endpoints/EntitlementConfigEndpoints.cs (new); src/Backend/StatsTid.Backend.Api/Program.cs (1 registration line); src/Backend/StatsTid.Backend.Api/Endpoints/Helpers/SaveEntitlementConfigResult.cs (or inline) |
| **KB Refs** | ADR-019 D2/D5/D6/D8 (admin-strict If-Match contract), ADR-020 D2 (3-case routing), ADR-018 D2/D3/D5 (atomic outbox) |
| **Plan section** | Phase 2 — TASK-3007 + Callouts 6, 7, 8 |
| **Dependencies** | TASK-3002, TASK-3003, TASK-3004 |

**Description**: New file `EntitlementConfigEndpoints.cs` exposing POST/PUT/DELETE/GET-list/GET-by-key under `/admin/entitlement-configs`. ADR-019 admin-strict If-Match enforced via `EtagHeaderHelper.TryParseIfMatch` → 412 stale / 428 missing / 409 disjoint. ADR-020 D2 3-case routing inside `SupersedeAndCreateAsync` (Case A no predecessor → fresh INSERT; Case B `effective_from < today` predecessor stays closed honestly + fresh INSERT; Case C zero-width `effective_from = today` predecessor → UPDATE-and-reopen). Cycle-3 same-day-only-edit validator (rejects `effective_from != today` with 422). **`reset_month` + `accrual_model` 422 guard**: if request payload changes either field from predecessor live-row value, return 422 with structured error. RBAC: `.RequireAuthorization("GlobalAdminOnly")` per S29 WageTypeMappingEndpoints precedent.

**Validation Criteria**:
- [ ] 5 endpoints registered (POST/PUT/DELETE/GET-list/GET-by-key)
- [ ] All 5 use `.RequireAuthorization("GlobalAdminOnly")`
- [ ] If-Match contract returns 412/428/409 per ADR-019 D2/D5/D6
- [ ] ADR-020 D2 3-case routing inside `SupersedeAndCreateAsync`
- [ ] `reset_month` / `accrual_model` change returns 422 with structured error body
- [ ] Atomic outbox: every mutation emits exactly one event + one audit row in a single tx

---

#### TASK-3008 — Consumption-site migration (Skema + Balance two-step pattern)

| Field | Value |
|-------|-------|
| **ID** | TASK-3008 |
| **Status** | pending |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs (around L313); src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs (around L120) |
| **KB Refs** | ADR-021 (in-flight, drafted at TASK-3011), ADR-016 D5b "fifth pattern" |
| **Plan section** | Phase 2 — TASK-3008 + Callout 7 |
| **Dependencies** | TASK-3003 |

**Description**: Migrate both consumption sites to the two-step pattern: (a) read live (open) row to obtain `ResetMonth` (frozen by sub-fork (i)); (b) derive `entitlementYearStart` from `ResetMonth` + relevant date; (c) issue dated read `GetByTypeAtAsync(asOfDate=entitlementYearStart)` for the quota fields. Under sub-fork (i), `ResetMonth` is immutable per natural-key → no version-skew. Skema uses `asOfDate=entitlementYearStartFor(absenceDate)`; Balance summary uses `asOfDate=entitlementYearStartFor(month-being-summarized)`.

**Validation Criteria**:
- [ ] `SkemaEndpoints.cs:~313` reads via two-step pattern
- [ ] `BalanceEndpoints.cs:~120` reads via two-step pattern
- [ ] Existing regression tests still pass (semantic change is forward-only)

---

#### TASK-3009 — Frontend admin page + hook + sidebar entry

| Field | Value |
|-------|-------|
| **ID** | TASK-3009 |
| **Status** | pending |
| **Agent** | Frontend |
| **Components** | frontend/src/pages/admin/EntitlementConfigEditor.tsx (new); frontend/src/hooks/useEntitlementConfig.ts (new); frontend/src/components/admin/AdminSidebar.tsx (or App.tsx — verify at decompose); frontend/src/types/ TypeScript types (or co-located) |
| **KB Refs** | ADR-019 D7 (frontend `apiFetchWithEtag` extension + banner-with-retry on 412) |
| **Plan section** | Phase 2 — TASK-3009 + Risk R4 |
| **Dependencies** | TASK-3007 (HTTP shape locks first) |

**Description**: New admin page mirroring S25 admin pages + S29 same-day-edit validator UX. `apiFetchWithEtag<EntitlementConfig>` extension on `frontend/src/lib/api.ts` already exists (S25). Banner-with-retry on 412 mirrors `ProfileEditor.tsx`. Plain DatePicker (no MondayDatePicker — entitlements are annual not weekly-norm). Scope-trim: edit `annual_quota` + `carryover_max` + `description` + `pro_rate_by_part_time` + `is_per_episode` + `min_age` only; `reset_month` + `accrual_model` shown read-only (server-side 422 if attempted edit).

**Validation Criteria**:
- [ ] EntitlementConfigEditor.tsx renders 5 endpoints (list / view / create / edit / soft-delete)
- [ ] useEntitlementConfig hook handles ETag round-trip
- [ ] Banner-with-retry on 412 mirrors ProfileEditor
- [ ] reset_month + accrual_model shown read-only
- [ ] Sidebar entry under Admin section
- [ ] Existing 88 frontend vitest tests still pass

---

### Phase 3 — D-tests (sequential after Phase 2 commit, 1 commit)

#### TASK-3010 — D-tests suite (marquee + 3-case + ETag + seed-idempotency + admin-CRUD)

| Field | Value |
|-------|-------|
| **ID** | TASK-3010 |
| **Status** | pending |
| **Agent** | Test & QA |
| **Components** | tests/StatsTid.Tests.Regression/Config/EntitlementConfigSupersessionTests.cs (new); tests/StatsTid.Tests.Regression/Config/EntitlementConfigEndpointTests.cs (new — HTTP-level, conditional on TASK-3001 outcome); tests/StatsTid.Tests.Regression/Config/EntitlementQuotaCheckUsesYearStartTests.cs (new — marquee) |
| **KB Refs** | ADR-021 (in-flight); ADR-020 D2/D3; ADR-019 D6/D8 |
| **Plan section** | Phase 3 — TASK-3010 |
| **Dependencies** | TASK-3001 (disposition read from this row), TASK-3002..3009 (implementation complete) |

**Description**: 5 D-test groupings (16 [Fact] floor): (a) **Marquee** (1 [Fact]) — `EntitlementQuotaCheck_UsesYearStartConfig_NotCurrentConfig` — direct-orchestration; admin edit 25→27 today + employee absence today in already-started year-Y → both quota check + balance summary report 25 (year-Y-start), not 27. (b) **ADR-020 D2 3-case** (3 [Fact]) — Case A/B/C routing. (c) **ETag/If-Match contract** (3 [Fact]) — 412 stale, 428 missing, 23505-vs-412 distinction. (d) **Seed-idempotency 4-case** (4 [Fact]) — fresh / re-run / post-admin-edit / post-admin-soft-delete (S29 TASK-2906 lesson; uses `docker compose down -v && up` between cases). (e) **Admin-CRUD shape** (5 [Fact]) — POST/PUT/DELETE/GET-list/GET-by-key smoke tests. Dispatch rule: **agent reads TASK-3001 disposition from this SPRINT-30.md file**; trivial → use `WebApplicationFactory<Program>` per S25 pattern; split → direct-orchestration harness mirroring `ProfileSupersessionTests.cs` (S29 pattern).

**Validation Criteria**:
- [ ] Marquee D-test passes (load-bearing Step-7a-equivalent harness)
- [ ] 3 D2-3case D-tests pass
- [ ] 3 ETag/If-Match D-tests pass
- [ ] 4 seed-idempotency D-tests pass
- [ ] 5 admin-CRUD shape D-tests pass (HTTP-level if TASK-3001 trivial; direct-orchestration if split)
- [ ] Net new D-tests = 16 (floor); +8 unblocked if TASK-3001 trivial = 24 stretch

---

### Phase 4 — Validation (Orchestrator-direct, no new commit unless fixes)

Per WORKFLOW.md Step 4: `dotnet build` + sprint-test-validation skill (previous + delta = current arithmetic).

### Phase 5 — Documentation + Sprint Close

#### TASK-3011 — New ADR-021 + ADR-016 D5b extension paragraph + KB INDEX.md

| Field | Value |
|-------|-------|
| **ID** | TASK-3011 |
| **Status** | pending |
| **Agent** | Orchestrator-direct (KB writes per WORKFLOW.md L48) |
| **Components** | docs/knowledge-base/decisions/ADR-021-entitlement-policy-versioned-history.md (new); docs/knowledge-base/decisions/ADR-016-temporal-period-handling.md (D5b extension paragraph appended); docs/knowledge-base/INDEX.md |
| **KB Refs** | new ADR-021 (this task); ADR-020 §122 (anticipation); ADR-016 D5b "fifth pattern" |
| **Plan section** | Phase 5 — TASK-3011 + Callout 3 |
| **Dependencies** | Phase 1 + Phase 2 + Phase 3 complete |

**Description**: New ADR-021 documenting the consumption-time-lookup variant (HTTP-endpoint-direct, not planner-routed via SegmentManifest); `reset_month` + `accrual_model` freeze-from-admin rule; natural-key stream shape; year-start-derivation contract; `MONTHLY_ACCRUAL` enum value dead-code footnote (Phase 5+). Mirror S29 TASK-2910's ADR-018 D14 paragraph as a standalone ADR per ADR-020 §122 explicit anticipation. ADR-016 D5b extension paragraph adds the "fifth pattern" (consumption-time-lookup) building on S29's fourth-pattern (export-time-lookup) addition.

**Validation Criteria**:
- [ ] ADR-021 file created with full ACCEPTED-status decision rationale
- [ ] ADR-016 D5b paragraph extended with fifth-pattern reconciliation
- [ ] KB INDEX.md updated with ADR-021 entry

---

#### TASK-3012 — Sprint-close plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3012 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | docs/sprints/SPRINT-30.md (this file — close sections); docs/sprints/INDEX.md (final row); ROADMAP.md (Phase 4d-2 entry → COMPLETE); docs/QUALITY.md (Entitlement domain re-grade); ~/.claude/projects/C--StatsTid/memory/MEMORY.md (S30 line) |
| **KB Refs** | n/a (sprint plumbing) |
| **Plan section** | Phase 5 — TASK-3012 |
| **Dependencies** | TASK-3011 |

**Description**: Fill close sections of this SPRINT-30.md (sprint-end HEAD, retrospective, test totals, etc.); finalize INDEX.md row; mark ROADMAP Phase 4d-2 COMPLETE; re-grade QUALITY.md Entitlement domain; append MEMORY.md S30 line.

**Validation Criteria**:
- [ ] All metadata fields filled in this SPRINT-30.md
- [ ] INDEX.md row complete
- [ ] ROADMAP Phase 4d-2 → COMPLETE
- [ ] QUALITY.md re-graded (or unchanged with rationale)
- [ ] MEMORY.md S30 line appended

---

## Legal & Payroll Verification

_Filled by TASK-3012._

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | _Entitlement quotas: AC/HK/PROSA × OK24/OK26 × 5 types; freeze-from-admin on agreement-defining fields_ |
| Wage type mappings produce correct SLS codes | N/A | _No wage-type changes in S30_ |
| Overtime/supplement calculations are deterministic | N/A | _No rule engine changes in S30_ |
| Absence effects on norm/flex/pension are correct | pending | _Skema quota-check semantic change to year-start lookup_ |
| Retroactive recalculation produces stable results | pending | _Marquee D-test verifies year-start invariant_ |

## External Review (Step 7a)

_Filled by Phase 4 + TASK-3012. Per plan: required pre-commit by default; user may grant documented exit when marquee D-test substantively serves the Step-7a-equivalent purpose (S29 precedent)._

| Field | Value |
|-------|-------|
| **Invoked** | pending |
| **Sprint-start commit** | `41b6e89` |
| **Command** | _filled at sprint close_ |
| **Review Cycles** | _filled at sprint close_ |
| **Findings** | _filled at sprint close_ |
| **Resolution** | _filled at sprint close_ |

## Test Summary

_Filled by Phase 4 / TASK-3012. Target floor: 823 (807 baseline + 16 net new). Stretch: 831 (WAF trivial) or 835 (WAF trivial + frontend vitest)._

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | _pending_ | _pending_ |
| Plain regression | _pending_ | _pending_ |
| Docker-gated | _pending_ | _pending_ |
| Frontend vitest | _pending_ | _pending_ |
| **Total** | _pending_ | _pending_ |

## Agent Effectiveness

_Filled by TASK-3012._

| Metric | Value |
|--------|-------|
| Tasks | 13 declared (+ 1 conditional TASK-3001b) |
| Constraint Violations | _pending_ |
| Reviewer Findings | _pending_ |
| External Review Cycles | _Step 0b: 3 cycles Codex / 1 cycle Reviewer; Step 7a: pending_ |
| External Findings | _Step 0b: see Findings sections above; Step 7a: pending_ |
| Re-dispatches | _pending_ |
| First-Pass Rate | _pending_ |

## Sprint Retrospective

_Filled by TASK-3012._

**What went well**: _pending_

**What to improve**: _pending_

**Knowledge produced**: _pending — ADR-021 expected_
