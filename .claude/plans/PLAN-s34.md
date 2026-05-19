# PLAN — Sprint 34: Phase 4e — `agreement_code` Versioned History (Launch-Blocker Close)

| Field | Value |
|-------|-------|
| **Sprint** | 34 |
| **Phase** | 4e (general hardening — first targeted launch-blocker) |
| **Sprint type** | Implementation (against ADR-023 D2 option (b)) |
| **Base commit** | `f966c9e` (S33 close, 2026-05-17) |
| **Refinement** | `.claude/refinements/REFINEMENT-s34-agreement-code-versioned-history.md` (READY after 2-cycle dual-lens) |
| **Sprint open date** | 2026-05-17 |
| **Task count** | 16 (TASK-3400..3415) |

## Sprint Goal

Close the `agreement_code` LAUNCH-BLOCKING determinism gap per ADR-023 D2 option (b) — version `users.agreement_code` via row-level history. 4th application of the established versioned-config pattern (after WTM/EntitlementConfig/EmployeeProfile in S29/S30/S33). Closes ADR-016 D10 retroactive replay determinism for the **entire rule-engine input surface** (4th and final dated input).

**Scope expansion via cycle 1 review** (refinement convergent BLOCKER absorption): not just PCS-replay path — also Balance/Skema/Overtime past-period HTTP endpoints have the same class of bug. All cut over to dated `UserAgreementCodeRepository.GetByUserIdAtAsync(employeeId, monthStart, ct)` lookups.

## Phase Decomposition

Follows S29/S30/S31/S33 sprint shape. NO worktrees — Phase 2 cutovers are file-disjoint except TASK-3407 which bundles AdminEndpoints PUT + POST in a single file (intentional per cycle 2 dual-lens confirmation).

| Phase | Tasks | Dispatch model |
|-------|-------|---------------|
| 0 | TASK-3400 | Orchestrator-direct (this file + SPRINT-34.md + INDEX.md provisional + commit) |
| 1 | TASK-3401..3405 | **Sequential** — schema (3401) → repository+DI (3402) → events (3404) → backfill seeder (3403, needs Seeded event) → doc clarification (3405). Note: 3404 dispatches BEFORE 3403 per dependency closure (Step 0b Reviewer WARNING 2 absorption). |
| 2 | TASK-3406..3413 | **Parallel non-worktree** — 8 dispatch slots; 7 truly parallel (different files) + 1 single-agent serial (TASK-3407 PUT+POST in same file) |
| 3 | TASK-3414 | Sequential — D-test suite (~11 tests) |
| 4 | TASK-3415 | Orchestrator-direct (sprint close + ROADMAP Phase 4e RESOLVED upgrade) |

## Step 0a — Entropy Scan Findings

Run 2026-05-17 at sprint open:

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-023 + all S33 references resolve cleanly post-S33 close |
| Pattern compliance | CLEAN | S34 is 4th application of established versioned-config pattern; no anti-pattern introduction |
| Orphan detection | DEBT (carry-forward from S33) | 80+ stale locked agent worktrees under `.claude/worktrees/`; S34 uses non-worktree dispatch so non-blocking. Operational housekeeping deferred. |
| Documentation drift | CLEAN | MEMORY.md synced through S33 close |
| Quality grade review | scheduled | Re-grade at TASK-3415 close (Rule Engine A+ → A++ candidate if ADR-016 D10 fully closed; Backend API stays A-; Infrastructure A stays A) |
| Refinement disposition | RESOLVED | 2-cycle Step 4 dual-lens reviewed clean; cycle-cap respected (2/2 per lens); 3 cycle-1 BLOCKERs + 4 WARNINGs absorbed + 1 cycle-2 procedural BLOCKER + 3 WARNINGs absorbed |

## Step 0b — Plan Review Trigger

**MANDATORY** per trigger criteria — sprint touches:
- **P1** (Architectural integrity) — 4th application of ADR-020 D2 + ADR-018 D7; closes ADR-023 D2 launch-blocker
- **P3** (Event sourcing / auditability) — 3 net-new event types + 5-way atomic emission in AdminEndpoints PUT/POST
- **P4** (Version correctness) — marquee replay-stability is the load-bearing acceptance gate; closes ADR-016 D10 for the 4th/final rule-engine input
- **P7** (Security / access control) — JWT mint path routing change + AdminEndpoints PUT/POST scope unchanged (LocalAdminOrAbove)

Dispatch dual-lens on this PLAN file before Phase 1 dispatches. Cycle-cap = 2 per lens.

---

## Task Log

### Phase 0 — Sprint Open

#### TASK-3400 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3400 |
| **Status** | in-progress |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s34.md` (this file), `docs/sprints/SPRINT-34.md`, `docs/sprints/INDEX.md` |
| **Dependencies** | none |
| **KB Refs** | ADR-023 D2 (binding for option (b) launch-blocker close) |

---

### Phase 1 — Sequential Foundation (5 tasks)

#### TASK-3401 — Schema migration: `user_agreement_codes` + `user_agreement_codes_audit`

| Field | Value |
|-------|-------|
| **ID** | TASK-3401 |
| **Status** | pending |
| **Agent** | **Data Model (extended into Infrastructure schema, cross-domain authorized)** — schema lives in `docker/postgres/init.sql`; greenfield-baked into base CREATE TABLE per S29/S30/S31 precedent |
| **Components** | `docker/postgres/init.sql` (new CREATE TABLE blocks + ALTER ledger entry `s34-d1-user-agreement-codes`); test schemas under `tests/StatsTid.Tests.Regression/TestFixtures/DockerHarness.cs` |
| **Dependencies** | TASK-3400 |
| **KB Refs** | ADR-018 D7 (row-version + If-Match), ADR-019 D8 (audit version-transition), ADR-020 D2 (versioned-config foundations), ADR-023 D2 (binding) |

**Schema contract** (mirrors `employee_profiles` shape):
```sql
CREATE TABLE IF NOT EXISTS user_agreement_codes (
    assignment_id    UUID         PRIMARY KEY,
    user_id          TEXT         NOT NULL REFERENCES users(user_id),
    agreement_code   TEXT         NOT NULL,
    effective_from   DATE         NOT NULL DEFAULT '0001-01-01',
    effective_to     DATE         NULL,
    version          BIGINT       NOT NULL DEFAULT 1,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_user_agreement_codes_live
    ON user_agreement_codes(user_id) WHERE effective_to IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS idx_user_agreement_codes_history
    ON user_agreement_codes(user_id, effective_from);

CREATE TABLE IF NOT EXISTS user_agreement_codes_audit (
    audit_id          BIGSERIAL    PRIMARY KEY,
    assignment_id     UUID         NOT NULL,
    user_id           TEXT         NOT NULL,
    action            TEXT         NOT NULL CHECK (action IN ('CREATED','UPDATED','DELETED','SUPERSEDED')),
    previous_data     JSONB        NULL,
    new_data          JSONB        NULL,
    version_before    BIGINT       NULL,
    version_after     BIGINT       NULL,
    actor_id          TEXT         NOT NULL,
    actor_role        TEXT         NOT NULL,
    audit_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
```

**Validation Criteria**:
- [ ] `user_agreement_codes` + `user_agreement_codes_audit` exist in init.sql with shape above
- [ ] Partial-unique-index + history-unique-index both created
- [ ] CHECK constraint on action enum includes all 4 values
- [ ] ALTER ledger entry `s34-d1-user-agreement-codes` registered
- [ ] Test harness `DockerHarness.cs` schema includes both tables
- [ ] `dotnet build` clean; Docker harness starts cleanly with new tables

---

#### TASK-3402 — `UserAgreementCodeRepository` + DI registration

| Field | Value |
|-------|-------|
| **ID** | TASK-3402 |
| **Status** | pending |
| **Agent** | **Data Model (extended into Infrastructure + Backend.Api/Program.cs, cross-domain authorized)** — S31 TASK-3102 + S33 TASK-3301/3302 precedent |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/UserAgreementCodeRepository.cs` (new), `src/SharedKernel/StatsTid.SharedKernel/Models/UserAgreementCode.cs` (new — simple record carrying assignment + audit metadata), `src/Backend/StatsTid.Backend.Api/Program.cs` (Step 0b Codex BLOCKER 2 absorption: explicit `AddSingleton<UserAgreementCodeRepository>()` registration matching project pattern at Program.cs:40+) |
| **Dependencies** | TASK-3401 |
| **KB Refs** | ADR-020 D2 (3-case routing), ADR-018 D5 ((conn, tx) overloads), ADR-019 D2 (admin-strict If-Match) |

**API surface** (Step 0b Codex BLOCKER 1 absorption — SoftDelete DROPPED from scope; see rationale below):
- `Task<string?> GetByUserIdAtAsync(string userId, DateOnly asOfDate, CancellationToken ct)` — dated lookup; end-exclusive predicate
- `Task<string?> GetCurrentAsync(string userId, CancellationToken ct)` — live row's agreement_code; for JWT mint
- `Task<(string? Current, long Version)?> GetCurrentWithVersionAsync(string userId, CancellationToken ct)` — for ETag stamping
- `Task<SaveUserAgreementCodeResult> SupersedeAndCreateAsync(NpgsqlConnection conn, NpgsqlTransaction tx, UserAgreementCodeSupersedeRequest req, long? expectedVersion, CancellationToken ct)` — ADR-020 D2 3-case routing under `SELECT ... FOR UPDATE`; Case C successor v=`predecessor.Version+1` per S33 Step 7a P1 absorption pattern

**SoftDeleteAsync DROPPED** (Step 0b Codex BLOCKER 1 absorption): for `agreement_code`, soft-delete is semantically meaningless — every user must have an agreement code at all times (admin changes it, never NULLs it). Unlike WTM/EntitlementConfig/EmployeeProfile where retirement is a meaningful admin action, there is no "retire a user's agreement" lifecycle. Pattern-symmetry-without-purpose carries ~200 LOC of unused method + 1 unused event type + 2 unused D-tests. Drop the SoftDelete machinery from S34 scope. The bitemporal `effective_to` column stays in the schema (for the supersession close path); the public repository method does NOT.

**Canonical-write contract documented in class XML doc** (refinement cycle 1 Reviewer WARNING 2 absorption): "All writes to `user_agreement_codes` flow through this repository. `users.agreement_code` is a denormalized cache for live-only consumers (JWT mint, current-row reads); cache write happens in the same atomic tx as the repository call. Past-period readers route through `GetByUserIdAtAsync`; never read `users.agreement_code` for replay-sensitive paths."

**Validation Criteria**:
- [ ] Repository has 4 methods above (GetByUserIdAtAsync + GetCurrentAsync + GetCurrentWithVersionAsync + SupersedeAndCreateAsync); no SoftDeleteAsync per Step 0b BLOCKER 1 absorption
- [ ] SQL JOIN includes end-exclusive predicate `effective_from <= @asOfDate AND (effective_to IS NULL OR effective_to > @asOfDate)`
- [ ] `SupersedeAndCreateAsync` 3-case routing under `SELECT ... FOR UPDATE`; Case C successor inherits `predecessor.Version+1`
- [ ] Class XML doc documents canonical-write contract per refinement cycle 1 Reviewer WARNING 2 absorption
- [ ] `AddSingleton<UserAgreementCodeRepository>()` registered in `Backend.Api/Program.cs` (Step 0b Codex BLOCKER 2 absorption)
- [ ] `dotnet build` clean

---

#### TASK-3403 — `UserAgreementCodeBackfillSeeder`

| Field | Value |
|-------|-------|
| **ID** | TASK-3403 |
| **Status** | pending |
| **Agent** | **Data Model (extended into Infrastructure + Backend.Api/Program.cs, cross-domain authorized)** |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/UserAgreementCodeBackfillSeeder.cs` (new), `src/Backend/StatsTid.Backend.Api/Program.cs` (registration) |
| **Dependencies** | TASK-3402, TASK-3404 (needs UserAgreementCodeSeeded event registered first) |
| **KB Refs** | ADR-018 D3 (atomic outbox), S31 TASK-3106 (EmployeeProfileSeeder precedent), S33 Step 7a cycle 1 absorption (history-covering default for seeders) |

**Description**: Bootstrap per-user backfill reading current `users.agreement_code` and inserting `user_agreement_codes` rows at **`effective_from = '0001-01-01'`** (history-covering — matches the S33 Step 7a cycle 1 absorption: seeders backfill with history-covering default; only `AdminEndpoints POST` stamps today for net-new users). Idempotent NOT-EXISTS guard. Per-row atomic tx (INSERT + audit `'CREATED'` row + outbox `UserAgreementCodeSeeded` event).

**Concurrent-startup race awareness** (cycle 1 Reviewer R4 risk absorption): catches `PostgresException` SqlState=23505 on partial-unique-index violation + skips-without-fail (idempotent retry semantic; same fix shape as the S31 EmployeeProfileSeeder deferred Phase 4e item; ship the fix here so we don't carry the same defect through S34).

**Validation Criteria**:
- [ ] `UserAgreementCodeBackfillSeeder` registered in `Backend.Api/Program.cs`
- [ ] Backfill reads from `users` table, writes to `user_agreement_codes` with `effective_from = '0001-01-01'`
- [ ] Per-row atomic tx: INSERT + audit CREATED + outbox UserAgreementCodeSeeded
- [ ] Idempotent NOT-EXISTS guard prevents duplicates on second startup
- [ ] Concurrent-startup race handled (catches 23505, skips-without-fail)
- [ ] `dotnet build` clean

---

#### TASK-3404 — New event types + EventSerializer registration

| Field | Value |
|-------|-------|
| **ID** | TASK-3404 |
| **Status** | pending |
| **Agent** | **Data Model Agent** (in-scope per AGENTS.md L15 — both `SharedKernel/Events/` + `EventSerializer.cs` declared) |
| **Components** | `src/SharedKernel/StatsTid.SharedKernel/Events/UserAgreementCodeSeeded.cs` (new), `UserAgreementCodeSuperseded.cs` (new), `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` (2 new dictionary entries) |
| **Dependencies** | TASK-3400 |
| **KB Refs** | ADR-023 D2 (Phase 4e replay-data trail), PAT-004 (event-sourcing pattern), DEP-003 (reflection-coverage test auto-catches forgotten registrations) |

**Event types** (Step 0b BLOCKER 1 absorption — SoftDeleted DROPPED; SoftDelete is semantically meaningless for agreement_code):
- `UserAgreementCodeSeeded` — payload: `UserId: string`, `AgreementCode: string`, `EffectiveFrom: DateOnly`, `RowVersion: long`. No `OldAgreementCode` (no predecessor). Emitted by backfill seeder + AdminEndpoints POST.
- `UserAgreementCodeSuperseded` — payload mirrors `EmployeeProfileSuperseded`: `PredecessorAssignmentId`, `NewAssignmentId`, `UserId`, `PredecessorEffectiveFrom`, `PredecessorEffectiveTo`, `NewEffectiveFrom`, `OldAgreementCode`, `NewAgreementCode`, `VersionBefore`, `VersionAfter`. Emitted on Case C cross-day supersession.

EventSerializer typeof count: **56 → 58** (+2).

DEP-003 reflection-coverage test auto-validates both registrations (S18/TASK-1809 precedent).

**Validation Criteria**:
- [ ] 2 new event files exist in `SharedKernel/Events/`
- [ ] Both extend `DomainEventBase` (matches S33 UserAgreementCodeChanged + UserUpdated convention; sealed class with EventType override; actor/correlation provided by base class)
- [ ] EventSerializer.cs has 2 new dictionary entries; typeof count 56 → 58
- [ ] DEP-003 reflection-coverage test passes
- [ ] `dotnet build` clean

---

#### TASK-3405 — `EmploymentProfile` model doc clarification

| Field | Value |
|-------|-------|
| **ID** | TASK-3405 |
| **Status** | pending |
| **Agent** | Data Model Agent |
| **Components** | `src/SharedKernel/StatsTid.SharedKernel/Models/EmploymentProfile.cs` (xmldoc edit only; no shape change) |
| **Dependencies** | TASK-3400 |
| **KB Refs** | ADR-023 D2 |

**Description**: Single-line xmldoc edit on `AgreementCode` property — notes it's now sourced from `UserAgreementCodeRepository.GetByUserIdAtAsync` at consumption sites (not `users.agreement_code` live cache). Cross-reference ADR-023 D2 + S34. NO model shape change.

**Validation Criteria**:
- [ ] xmldoc updated; no property shape change
- [ ] `dotnet build` clean (no semantic change)

---

### Phase 2 — Parallel Cutovers (8 dispatch slots, 7 truly parallel + 1 single-agent serial)

**Dispatch model**: After TASK-3404 commits land on master (R7 commit-before-dispatch), dispatch all 8 in parallel. Files disjoint EXCEPT TASK-3407 which bundles AdminEndpoints PUT + POST in a single file (cycle 2 dual-lens confirmed: should stay combined; splitting creates same-file contention with no parallelism benefit).

**Phase 2 Disjointness Audit**:

| Task | Files touched |
|------|---------------|
| TASK-3406 | `src/Infrastructure/StatsTid.Infrastructure/EmploymentProfileResolver.cs` |
| TASK-3407 | `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` (BOTH PUT L466-578 + POST L290-460 — single-agent serial) |
| TASK-3408 | `src/Backend/StatsTid.Backend.Api/Endpoints/AuthEndpoints.cs` |
| TASK-3409 | `frontend/src/pages/admin/UserManagement.tsx` + `frontend/src/hooks/useAdmin.ts` |
| TASK-3410 | `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs` |
| TASK-3411 | `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` |
| TASK-3412 | `src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs` |
| TASK-3413 | `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileRepository.cs` (audit/doc-only most likely; or small code change if cutover needed) |

#### TASK-3406 — PCS resolver agreement_code cutover

| Field | Value |
|-------|-------|
| **ID** | TASK-3406 |
| **Status** | pending |
| **Agent** | **Data Model (extended into Infrastructure, cross-domain authorized)** — single file |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/EmploymentProfileResolver.cs` |
| **Dependencies** | TASK-3402 |
| **KB Refs** | ADR-023 D1 (PCS consumption-site), ADR-023 D2 (closes the documented gap) |

**Description**: Change agreement_code source from `u.agreement_code` JOIN read in the existing SQL to `UserAgreementCodeRepository.GetByUserIdAtAsync(userId, asOfDate, ct)` — dropped from the JOIN; called separately and merged into the returned `EmploymentProfile`. Inject `UserAgreementCodeRepository` via constructor.

**Read-consistency note** (Step 0b Codex cycle 2 WARNING absorption): the cutover changes the resolver from a single-statement snapshot (one JOIN) to two queries on the same `asOfDate` (employee_profiles JOIN + UserAgreementCodeRepository.GetByUserIdAtAsync). Under PostgreSQL READ COMMITTED isolation (the project default), the two reads could see different snapshots if a writer commits between them. Acceptable for S34 because: (a) writes flow through atomic `(conn, tx)` paths that update both `users.agreement_code` cache AND `user_agreement_codes` rows in the same tx; (b) the resolver returns from a single point-in-time view per call — a concurrent commit during a single resolver call is no different than the same commit happening 1ms later for the next call; (c) PCS retroactive replay reads frozen historical rows that don't race with current writes. If post-launch perf measurement surfaces consistency edge cases, route the resolver through a single read-only transaction wrapping both queries — out of S34 scope.

**Validation Criteria** (Step 0b Codex WARNING 1 absorption — full hydration coverage):
- [ ] Resolver no longer JOINs `u.agreement_code`
- [ ] Resolver injects + calls `UserAgreementCodeRepository.GetByUserIdAtAsync(userId, asOfDate, ct)` for agreement_code
- [ ] Returned `EmploymentProfile.AgreementCode` is the dated value
- [ ] **All 8 hydrated fields still correct after refactor**: `EmployeeId` (param), `WeeklyNormHours` + `PartTimeFraction` + `Position` (from employee_profiles dated JOIN — unchanged), `AgreementCode` (now dated via separate UserAgreementCodeRepository call), `OkVersion` + `EmploymentCategory` + `OrgId` (still live from users), `IsPartTime` (computed). D-test asserts all 8 fields populate per ADR-023 D2 hydration contract.
- [ ] `dotnet build` clean

---

#### TASK-3407 — AdminEndpoints PUT + POST (single-agent serial; cross-domain authorized)

| Field | Value |
|-------|-------|
| **ID** | TASK-3407 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** — `src/Backend/**/Endpoints/*.cs` not declared in any single agent scope per AGENTS.md L51; S22 TASK-2205 precedent |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` (PUT L466-578 + POST L290-460 — serial within one dispatch) |
| **Dependencies** | TASK-3402 (repository) + TASK-3404 (events) |
| **KB Refs** | ADR-020 D2 (3-case routing on PUT), ADR-018 D3 (atomic outbox), ADR-019 D2 (admin-strict If-Match) |

**Description** (two sub-cutovers in one file):

**Sub-cutover 1: PUT `/api/admin/users/{userId}`** routes agreement_code changes through `UserAgreementCodeRepository.SupersedeAndCreateAsync` (in same atomic tx as existing emissions). Updates `users.agreement_code` denormalized cache in same UPDATE statement. PUT body extended with required `EffectiveFrom: DateOnly` + cycle-3 validator rejects backdated/future-dated with 422. Outcome-based event emission:
- Case B (same-day): emits UserUpdated + UserAgreementCodeChanged (S33 existing) + audit action UPDATED
- Case C (cross-day): emits UserUpdated + UserAgreementCodeChanged + UserAgreementCodeSuperseded (dual-emission per S25 publish-supersession precedent) + audit action SUPERSEDED

**Sub-cutover 2: POST `/api/admin/users`** extended (Codex BLOCKER 2 absorption) — net-new admin-created user gets BOTH `users` INSERT AND `user_agreement_codes` Case A INSERT atomically; emits `UserAgreementCodeSeeded` (matching backfill semantic — no predecessor). Audit action CREATED.

**Validation Criteria**:
- [ ] PUT DTO extended with required `EffectiveFrom: DateOnly` (named-record syntax)
- [ ] PUT validator returns 422 for both backdated AND future-dated `EffectiveFrom`
- [ ] PUT routes through `SupersedeAndCreateAsync`; Case B emits Updated + Changed; Case C emits Updated + Changed + Superseded (dual)
- [ ] PUT audit row action UPDATED (Case B) or SUPERSEDED (Case C)
- [ ] PUT users.agreement_code cache updated in same UPDATE statement
- [ ] POST atomic tx: users INSERT + user_agreement_codes Case A INSERT + audit CREATED + UserCreated + UserAgreementCodeSeeded
- [ ] `dotnet build` clean

---

#### TASK-3408 — AuthEndpoints JWT mint reads through repository

| Field | Value |
|-------|-------|
| **ID** | TASK-3408 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/AuthEndpoints.cs` (login path L30-65) |
| **Dependencies** | TASK-3402 |
| **KB Refs** | ADR-023 D2 (canonical source-of-truth) |

**Description**: JWT mint path login reads through `UserAgreementCodeRepository.GetCurrentAsync(userId, ct)` instead of `dbUser.AgreementCode` denormalized cache. Defense-in-depth canonical source-of-truth.

**Performance contract** (Step 0b Codex WARNING 2 absorption): adds 1 additional SELECT per login (PostgreSQL connection pooling absorbs; login is rare relative to general traffic; pre-launch perf budget unaffected). NOT cached at app-layer — the new table is the canonical source and per-login lookup matches the cache+canonical pattern for non-replay-sensitive consumers. If post-launch perf measurement shows login latency regression, consider caching at the AuthEndpoints layer (memory cache with TTL) — out of S34 scope.

**Validation Criteria**:
- [ ] Login reads agreement_code via `UserAgreementCodeRepository.GetCurrentAsync`
- [ ] JWT claim value matches live row from new table
- [ ] `dotnet build` clean

---

#### TASK-3409 — Frontend PUT-body wire shape sync

| Field | Value |
|-------|-------|
| **ID** | TASK-3409 |
| **Status** | pending |
| **Agent** | UX Agent |
| **Components** | `frontend/src/pages/admin/UserManagement.tsx`, `frontend/src/hooks/useAdmin.ts` |
| **Dependencies** | TASK-3407 (backend DTO contract; same-commit-group sync per S33 TASK-3311 precedent) |
| **KB Refs** | ADR-023 D2 (S34 binding contract; D8 not applicable since SoftDelete dropped per Step 0b BLOCKER 1) |

**Description**: PUT body wire shape gains `effectiveFrom: string` (ISO `YYYY-MM-DD` matching `DateTime.UtcNow.Date`). Mirrors S33 TASK-3311 effectiveFrom sync. ETag/If-Match support preserved.

**Validation Criteria**:
- [ ] `useAdmin.ts` PUT interface adds `effectiveFrom: string`
- [ ] `UserManagement.tsx` PUT body injects `effectiveFrom: new Date().toISOString().slice(0,10)` on save
- [ ] `npm run build` clean
- [ ] Existing vitest tests still pass

---

#### TASK-3410 — BalanceEndpoints past-month determinism

| Field | Value |
|-------|-------|
| **ID** | TASK-3410 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs` (L75 + L173-174 + L225 lookups) |
| **Dependencies** | TASK-3402 |
| **KB Refs** | ADR-023 D2 (HTTP-surface determinism gap closure per cycle 1 convergent BLOCKER absorption), ADR-023 D3 (graceful fallback per Balance pattern) |

**Description**: AgreementConfig + entitlement lookups read agreement_code from `UserAgreementCodeRepository.GetByUserIdAtAsync(employeeId, monthStart, ct)` instead of live `user.AgreementCode`. Graceful fallback preserved: resolver-null falls through to live cache → CentralAgreementConfigs → 37.0m chain (Balance is informational; not load-bearing).

**Validation Criteria**:
- [ ] AgreementConfig lookup uses dated agreement_code for past-month queries
- [ ] entitlement lookups use dated agreement_code
- [ ] Graceful fallback chain preserved (resolver-null → live cache → Central → 37.0m)
- [ ] D-test asserts past-month summary stability under historical supersession
- [ ] `dotnet build` clean

---

#### TASK-3411 — SkemaEndpoints past-month determinism

| Field | Value |
|-------|-------|
| **ID** | TASK-3411 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` (L158 + L353-354 + L451 + L477) |
| **Dependencies** | TASK-3402 |
| **KB Refs** | ADR-023 D2 |

**Description**: Absence-type derivation + entitlement-config quota lookups use dated agreement_code for past-month saves (employee can edit past entries pre-period-approval). Quota-breach 422 trichotomy preserved per S26 / ADR-018 D13.

**Validation Criteria**:
- [ ] Past-month Skema save uses `UserAgreementCodeRepository.GetByUserIdAtAsync(employeeId, monthStart, ct)`
- [ ] Quota validation uses dated agreement_code
- [ ] 422 quota-breach trichotomy preserved
- [ ] D-test asserts past-month Skema save uses period-effective agreement
- [ ] `dotnet build` clean

---

#### TASK-3412 — OvertimeEndpoints past-period determinism

| Field | Value |
|-------|-------|
| **ID** | TASK-3412 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs` (L490 + L527 `GetActiveConfigAsync`) |
| **Dependencies** | TASK-3402 |
| **KB Refs** | ADR-023 D2 |

**Description**: Compensation-model lookup uses dated agreement_code for past-period balance reads. AC vs HK vs PROSA compensation rules differ; past-period reads must use period-effective agreement.

**Validation Criteria**:
- [ ] Compensation-model selection uses dated agreement_code via `UserAgreementCodeRepository.GetByUserIdAtAsync`
- [ ] D-test asserts past-period overtime uses period-effective compensation rules
- [ ] `dotnet build` clean

---

#### TASK-3413 — EmployeeProfileRepository audit (likely doc-only)

| Field | Value |
|-------|-------|
| **ID** | TASK-3413 |
| **Status** | pending |
| **Agent** | **Data Model (extended into Infrastructure, cross-domain authorized)** |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileRepository.cs` (xmldoc on `ExecuteGetByEmployeeIdAsync` likely; or small code change) |
| **Dependencies** | TASK-3402 |
| **KB Refs** | ADR-023 D2 |

**Description** (cycle 1 Reviewer WARNING 3 + cycle 2 confirmation): `ExecuteGetByEmployeeIdAsync` at L128 is LIVE-only (`WHERE ep.effective_to IS NULL`) — single-purpose live read, NOT dual-purposed for asOf. Most likely outcome: xmldoc update marking return value as live-only-semantic + cross-reference to `EmploymentProfileResolver.GetByEmployeeIdAtAsync` for dated reads. May produce code change if audit surfaces unexpected dual-use.

**Validation Criteria**:
- [ ] `ExecuteGetByEmployeeIdAsync` xmldoc explicitly notes live-only semantic + cross-references resolver for dated reads
- [ ] OR (if code change needed) live JOIN with `u.agreement_code` replaced with explicit `UserAgreementCodeRepository.GetCurrentAsync` call
- [ ] D-test asserts no historical-replay consumer uses `EmployeeProfileRepository.GetByEmployeeIdAsync` live return path
- [ ] `dotnet build` clean

---

### Phase 3 — D-Tests

#### TASK-3414 — Docker-gated D-test suite (~11 tests)

| Field | Value |
|-------|-------|
| **ID** | TASK-3414 |
| **Status** | pending |
| **Agent** | Test & QA Agent |
| **Components** | `tests/StatsTid.Tests.Regression/UserAgreementCode/*.cs` (new directory + files), `tests/StatsTid.Tests.Regression/Payroll/AgreementCodeMarqueeTests.cs` (new) |
| **Dependencies** | All Phase 2 tasks |
| **KB Refs** | ADR-023 D2 (marquee load-bearing — closes ADR-016 D10 for the 4th/final rule-engine input), S33 TASK-3312 precedent |

**Test enumeration**:

**Marquee** (load-bearing P4 — closes ADR-016 D10):
- `ReplayAsync_StableUnderAgreementCodeMutation_ResultByteIdentical` — admin changes agreement_code today (AC → HK); replay of last month's PCS-routed calc uses predecessor `'AC'` agreement. **Seeds AC + HK with materially-different `HasMerarbejde` flag and/or `NormModel`** (Step 0b Reviewer WARNING 1 absorption — `WeeklyNormHours` alone is INSUFFICIENT as discriminator because `EmploymentProfile.WeeklyNormHours` is sourced from dated `employee_profiles` not from `AgreementConfig` lookup keyed by agreement_code; the agreement_code-keyed differences that actually flow into rule output are `HasMerarbejde` + `NormModel`-type config fields). Byte-identical `JsonSerializer.Serialize(segmentRuleResults)` between baseline + replay.

**SupersedeAndCreate 3-case routing**:
- `SupersedeAndCreate_CaseA_NoLiveRow_Inserts`
- `SupersedeAndCreate_CaseB_SameDayEdit_UpdatesInPlace_BumpsVersion`
- `SupersedeAndCreate_CaseC_CrossDayEdit_SuccessorInheritsPredecessorVersionPlus1` (per S33 Step 7a P1 absorption)

**PUT validator** (S33 precedent):
- `PUT_BackdatedEffectiveFrom_Returns422`
- `PUT_FutureDatedEffectiveFrom_Returns422`

(SoftDelete + DELETE endpoint D-tests DROPPED per Step 0b BLOCKER 1 absorption — SoftDelete is out of S34 scope.)

**AdminEndpoints POST Case A** (Codex BLOCKER 2 absorption):
- `AdminPostUser_NewUserGetsBothUsersRowAndUserAgreementCodesCaseAInsert_EmitsSeededEvent` — new user → login JWT + EmploymentProfileResolver.GetByEmployeeIdAtAsync(today) both succeed

**HTTP-surface determinism** (cycle 1 convergent BLOCKER 1 absorption):
- `Balance_PastMonthSummary_UsesPeriodEffectiveAgreementCode_NotLive`
- `Skema_PastMonthSave_UsesPeriodEffectiveAgreementCode_NotLive`
- `Overtime_PastPeriodBalance_UsesPeriodEffectiveAgreementCode_NotLive`

**Dual-emission ordering** (cycle 1 Reviewer WARNING 4 absorption):
- `AdminPutUserCrossDayAgreementCodeChange_EmitsBothChangedAndSupersededEvents_AndAuditActionSUPERSEDED` — Case C cross-day PUT emits BOTH UserAgreementCodeChanged AND UserAgreementCodeSuperseded with audit action SUPERSEDED; D-test docstring documents consumer dedupe contract per cycle 2 Reviewer WARNING 2 absorption

**Cache-canonical contract** (cycle 1 Reviewer WARNING 2 absorption):
- `AdminPutUser_UsersAgreementCodeCacheAgreesWithUserAgreementCodesLiveRow_AfterPUT`

**Backfill seeder idempotency**:
- `Backfill_SecondStartupSkipsAlreadyBackfilledUsers_Idempotent`

**Validation Criteria**:
- [ ] ~11 D-tests defined; all `[Trait("Category","Docker")]`
- [ ] Marquee variant PASSES — byte-identical replay
- [ ] AC + HK config seeds in marquee are materially different (cycle 2 WARNING 1 spec)
- [ ] Dual-emission D-test asserts both events + audit action SUPERSEDED for Case C
- [ ] HTTP-surface determinism asserted for Balance/Skema/Overtime past-period
- [ ] `dotnet test --filter "Category=Docker"` discovers + executes all new tests; all PASS

---

### Phase 4 — Sprint Close

#### TASK-3415 — Sprint close

| Field | Value |
|-------|-------|
| **ID** | TASK-3415 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/sprints/SPRINT-34.md` (close), `docs/sprints/INDEX.md` (final row), `ROADMAP.md` (Phase 4d-3 carry-forward + Phase 4e LAUNCH-BLOCKING → RESOLVED), `docs/QUALITY.md`, `~/.claude/projects/C--StatsTid/memory/MEMORY.md` |
| **Dependencies** | TASK-3414 + Step 7a clean |
| **KB Refs** | — |

**Description**: `sprint-test-validation` skill run; SPRINT-34.md close sections; INDEX.md final row; ROADMAP updates:
- Phase 4e `agreement_code` LAUNCH-BLOCKING → **RESOLVED** with citation to S34 close commit
- Phase 4d-3 Part 2 carry-forward "Phase 4e launch-blocking commitment" updated to "RESOLVED in S34"

QUALITY.md re-grade:
- Rule Engine A+ → **A++** if ADR-016 D10 fully closed (else stays A+ with clarifying note)
- Backend API stays **A-** (4 more cutovers; same pattern)
- Infrastructure A stays **A**

MEMORY.md S34 entry.

**Validation Criteria**:
- [ ] All test totals verified via skill
- [ ] ROADMAP Phase 4e LAUNCH-BLOCKING row marked RESOLVED with commit citation
- [ ] QUALITY.md re-grade applied
- [ ] MEMORY.md S34 entry added
- [ ] Sprint-close commit lands on master + pushed

**ADR-016 D5b reframing DEFERRED to S35** per cycle 1 Reviewer WARNING 1 absorption — sprint close stays mechanical.

---

## Architectural Constraints Verified

_To be checked off as the sprint progresses; final assertion in TASK-3415._

- [ ] **P1 — Architectural integrity** → 4th application of ADR-020 D2 + ADR-018 D7 + ADR-019 D2 pattern; pattern landscape stable at 5 (D5b reframing deferred to S35)
- [ ] **P2 — Rule engine determinism** → marquee PASSES; closes ADR-016 D10 for the 4th and final rule-engine input
- [ ] **P3 — Event sourcing** → 2 new event types registered (56→58: UserAgreementCodeSeeded + UserAgreementCodeSuperseded); 5 event/audit emissions across 7-op cross-table atomic tx on Case C cross-day PUT (users UPDATE cache + user_agreement_codes predecessor UPDATE + successor INSERT + audit row + UserUpdated + UserAgreementCodeChanged + UserAgreementCodeSuperseded outboxes); backfill seeder atomic per-row
- [ ] **P4 — Version correctness** → ETag monotonicity preserved on Case C (successor = predecessor.Version+1 per S33 absorption); cycle-3 validator on PUT (rejects backdated AND future-dated with 422). NOTE: no DELETE endpoint in S34 (SoftDelete dropped per Step 0b BLOCKER 1 absorption — semantically meaningless for agreement_code)
- [ ] **P7 — Security** → JWT mint reads through repository; AdminEndpoints PUT/POST scope unchanged (LocalAdminOrAbove); no new exposure

## Step 7a External Review (Plan)

Sprint-start commit: `<TASK-3400 commit SHA>`. Codex review command: `codex review --base 55b082b f966c9e <S34-close>` (S33 base → S34 close diff per WORKFLOW.md). Cycle-cap = 2.
