# Sprint 27 — Phase 4c.6: Read-Path Projection Tables for Skema/Time/Balance/Compliance + Atomic-Outbox Re-attempt

| Field | Value |
|-------|-------|
| **Sprint** | 27 |
| **Status** | planned |
| **Start Date** | 2026-05-09 |
| **End Date** | _pending_ |
| **Orchestrator Approved** | no |
| **Build Verified** | no |
| **Test Verified** | no |
| **Sprint-start commit base** | `c5edf52` (S26 sprint close) |
| **Refinement** | `.claude/refinements/REFINEMENT-s27-phase-4c6.md` (3 cycles per lens; cycle-cap waiver granted for cycle 3 on missed-facts grounds — see `feedback_missed_facts_vs_thrash.md`) |

## Sprint Goal

Build the synchronous-in-transaction projection-table layer for `TimeEntryRegistered` + `AbsenceRegistered` so that read-path GETs no longer hit `IEventStore.ReadStreamAsync` (publisher-drained snapshot — bounded lag), then re-apply the S26-reverted atomic-outbox migration on Skema/Time POST sites with projection writes inside the same `(conn, tx)`. This satisfies the trichotomy that S26's revert restored to a broken state: **atomic-rollback + 422 + clean state + read-your-write**. Also restores `SkemaQuotaBreachException → 422` at `SkemaEndpoints.cs:414-418` (the deferred S26 Step 7a cycle 2 P1 finding).

**Cycle-cap discipline** (per AGENTS.md L371/L455 + `feedback_step7a_cycle_cap_discipline.md`): 2 BLOCKER-fix cycles per lens at Step 0b and Step 7a. After cycle 2 on either lens, halt and prompt user. Refinement already burned cycle 3 with user waiver; Step 7a cycle 1 is expected to find ≥1 BLOCKER per Reviewer W1 (sprint sizing).

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-018 D6 stream-ownership table at `docs/knowledge-base/decisions/ADR-018-...md:265` confirmed (S26 TASK-2601 retabulate); ADR-016 D10 (replay determinism) cited in refinement; PAT-005 (PeriodCalculationService HTTP) governs Compliance read migration. No orphan KB references in scope. |
| Pattern compliance spot-check | CLEAN | No `FindFirst("scopes")` regressions (FAIL-001); no hardcoded `http://localhost` in non-launchSettings code. ADR-018 atomic-outbox pattern spot-checked at `LocalAgreementProfileRepository.SupersedeAndCreateAsync` (S22 exemplar) for shape parity. |
| Orphan detection | CLEAN | S26 carry-forward complete; no leftover S26 worktree files. SPRINT-27.md is net-new. |
| Documentation drift | CLEAN | S26 sprint close (`c5edf52`) recorded ROADMAP + MEMORY. ROADMAP Phase 4c.6 entry at L324 captures the carry-forward; this sprint executes it. |
| S26 lessons absorbed | applied | S26 R7 ("commit Phase 1 BEFORE dispatching Phase 2 worktrees") — Phase 1 here is sequential, each task committed before next dispatches. S26 cycle 2 architectural lesson ("verify GET-side read paths before atomic-outbox migration") — refinement Approach 4+5+9 explicitly addresses; AC includes grep-zero-hits enforcement on migrated handlers. |
| Quality grade review | deferred | Will update at sprint-end. |

**Test baseline (post-S26)**: 525 unit + 35 plain regression + 134 Docker-gated + 88 frontend vitest = 782 total. S27 expected addition: ~12–14 new D-tests (publisher-stall RYW ×2, parity-with-drain-sync ×2, Skema bundle-rollback ×1, atomic forced-rollback ×2, atomic quota-breach ×2, backfill idempotency ×1, TxContractTests ×2). Target ~795 total. No frontend changes (88 vitest unchanged AC).

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — P1 (architectural integrity, new D12); P3 (event sourcing/auditability, projection table is new write surface); P5 (integration isolation, atomic-outbox re-attempt); P8 (CI/CD enforcement, ~12-14 net new D-tests + new test harness). |
| **External Codex** | invoked at REFINEMENT level (3 cycles 2026-05-09: 2B/5W/3N → 1B/1W/4N → 0B/2W/3N). All BLOCKERs absorbed inline; cycle-cap waiver granted for cycle 3 on missed-facts grounds. |
| **Internal Reviewer** | invoked at REFINEMENT level (3 cycles 2026-05-09: 3B/5W/6N → 1B/3W/2N → 1B/2W/2N). All BLOCKERs absorbed inline. |
| **Plan-level review** | SKIP — refinement-level dual-lens review (3 cycles per lens, all BLOCKERs absorbed) covered the architectural surface; the SPRINT-27.md task decomposition is mechanically derived from refinement Approaches 1-9 with explicit per-task agent assignment + file scope. Each task's Validation Criteria are lifted verbatim from the refinement's AC checklist. User-approved skip rationale 2026-05-09. **READY for Step 1 dispatch**. |

### Refinement absorption summary

Cycle 1 BLOCKERs (cycle 2 fixes): PK shape revoted to `(event_id)` UUID (Codex B1 + Reviewer B3 convergent); publisher-stall D-test added as negative control + parity D-test gains drain-sync (Codex B2 + Reviewer B2 convergent); Skema bundle-rollback semantic pinned (Reviewer B1).

Cycle 2 BLOCKERs (cycle 3 fixes): ordering column switched from `stream_version` (publisher-assigned at drain) to `outbox_id BIGSERIAL` from `outbox_events` (write-time available; Reviewer cycle-2 B1-NEW); TASK-2701 added as Phase 1 prerequisite for `WebApplicationFactory<Program>` test harness (Codex BLOCKER + Reviewer W3-NEW convergent).

Cycle 3 BLOCKER (absorbed inline 2026-05-09): TASK-2701 prerequisites pinned (Mvc.Testing package + `public partial class Program {}` declaration). `IOutboxEnqueue.EnqueueAsync` change reframed as new overload `EnqueueAndReturnIdAsync` instead of breaking signature change (preserves all 31 S22-S26 callers + `ForcedRollbackHarness.cs:72` test-double untouched).

## Architectural Constraints Verified

- [ ] P1 — Architectural integrity (ADR-018 D13 added as discrete decision; sync-in-tx projection becomes the canonical pattern for event-stream-backed-read endpoints; refinement's discipline-boundary clause on projection-only fields deferred to Phase 4d-3).
- [ ] P3 — Event sourcing append-only semantics respected; projections are derived state, not new event types (no EventSerializer typeof() count change). Backfill from `events` is the recovery path (idempotent rebuild). Per-event ordering inside the tx pinned: outbox enqueue FIRST, projection INSERT SECOND.
- [ ] P5 — Integration isolation and delivery guarantees: atomic-outbox re-attempt closes the read-your-write regression that caused S26 reverts; trichotomy fully satisfied.
- [ ] P8 — CI/CD enforcement: build clean, +12-14 D-tests, +1 test harness (TASK-2701), +1 backfill ops script (TASK-2705).
- [ ] P9 — Skema quota-race fix changes observable behavior (silent 200 → 422 on race); frontend already handles 422 from pre-validation; 88 frontend vitest unchanged.

Not directly affected: P2, P4, P6, P7.

## Task Log

### TASK-2701 — Test harness: `WebApplicationFactory<Program>` + publisher-stop hook

| Field | Value |
|-------|-------|
| **ID** | TASK-2701 |
| **Status** | planned |
| **Agent** | Test & QA |
| **Components** | tests/StatsTid.Tests.Regression/Hosting/, src/Backend/StatsTid.Backend.Api/Program.cs (one-line append) |
| **KB Refs** | ADR-018 D13 (canonical sync-in-tx pattern — this harness validates it) |
| **Phase** | Phase 1 (sequential — REQUIRED before Phase 2 endpoints can be tested for read-your-write under publisher stall; Phase 1 first because Phase 2 D-tests at TASK-2710 depend on it) |

**Description**: Build the minimal `WebApplicationFactory<Program>` integration harness that lets D-tests run same-host POST + GET against a real Postgres + a controllable `OutboxPublisher` BackgroundService. This is a Phase 1 prerequisite — the publisher-stall read-your-write D-tests (TASK-2710) cannot be executed without it. Refinement Approach 9 + Acceptance Criteria "Test harness" section.

**Sub-deliverables**:

(a) Add `<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.*" />` to `tests/StatsTid.Tests.Regression/StatsTid.Tests.Regression.csproj`.

(b) Append `public partial class Program { }` to the bottom of `src/Backend/StatsTid.Backend.Api/Program.cs` (industry-standard idiom enabling `WebApplicationFactory<Program>` to access the otherwise-internal top-level-statements `Program` class).

(c) Create `tests/StatsTid.Tests.Regression/Hosting/StatsTidWebApplicationFactory.cs` extending `WebApplicationFactory<Program>`. Configuration: real Postgres via existing `DockerHarness`; real `OutboxPublisher` registered as `IHostedService` per `Program.cs:29` (`AddHostedService<OutboxPublisher>()`). Override appropriate config keys to point at the `DockerHarness` connection string.

(d) Add `StopPublisherAsync()` extension method (or instance method) on `StatsTidWebApplicationFactory` that resolves `factory.Services.GetServices<IHostedService>().OfType<OutboxPublisher>().Single()` and calls `IHostedService.StopAsync(CancellationToken.None)` on it. Companion `StartPublisherAsync()` re-starts via `StartAsync(CancellationToken.None)`. **NOT** `Task.Delay`. **NOT** a config flag. **NOT** a derived `IOutboxPublisher` test-double — must be the real `OutboxPublisher` BackgroundService stopped via the standard hosted-service contract.

(e) Add a single smoke test under `tests/StatsTid.Tests.Regression/Hosting/StatsTidWebApplicationFactoryTests.cs` that boots the factory, makes a trivial GET request (e.g., `GET /api/balance/{employeeId}/summary` for a non-existent employee → 404), asserts the response, then calls `StopPublisherAsync()` and verifies via reflection or DI inspection that the publisher is stopped. Marks the harness as functioning before Phase 2 D-tests dispatch.

**Validation Criteria**:
- [ ] `Microsoft.AspNetCore.Mvc.Testing` 8.0.* package added to test csproj
- [ ] `public partial class Program { }` appended to Program.cs (one line; below the existing top-level statements)
- [ ] `StatsTidWebApplicationFactory` extends `WebApplicationFactory<Program>` and compiles clean
- [ ] `StopPublisherAsync` resolves `OutboxPublisher` via `factory.Services.GetServices<IHostedService>().OfType<OutboxPublisher>().Single()` and calls `IHostedService.StopAsync(CancellationToken.None)` — verbatim mechanism, no flaky-mechanism drift
- [ ] Smoke test passes; harness is callable from the regression suite
- [ ] `dotnet build` clean (0/0)

**Files Changed**:
- `tests/StatsTid.Tests.Regression/StatsTid.Tests.Regression.csproj` (package reference)
- `src/Backend/StatsTid.Backend.Api/Program.cs` (one-line append)
- `tests/StatsTid.Tests.Regression/Hosting/StatsTidWebApplicationFactory.cs` (new)
- `tests/StatsTid.Tests.Regression/Hosting/StatsTidWebApplicationFactoryTests.cs` (new — smoke)

---

### TASK-2702 — Schema: `time_entries_projection` + `absences_projection`

| Field | Value |
|-------|-------|
| **ID** | TASK-2702 |
| **Status** | planned |
| **Agent** | Data Model |
| **Components** | docker/postgres/init.sql, docs/generated/db-schema.md (Orchestrator-only update at sprint close) |
| **KB Refs** | ADR-001 (event sourcing PostgreSQL via Npgsql), ADR-018 D13 (new — sync-in-tx projection pattern) |
| **Phase** | Phase 1 (sequential) |

**Description**: Add two new projection tables to `init.sql`. Both are sync-written from inside the atomic POST tx; both are event-derived (rebuildable from `events` via TASK-2705 backfill). Refinement Approach 1.

**Schema** (verbatim from refinement):

```sql
CREATE TABLE IF NOT EXISTS time_entries_projection (
    event_id UUID PRIMARY KEY,
    employee_id TEXT NOT NULL,
    date DATE NOT NULL,
    hours NUMERIC(6,2) NOT NULL,
    task_id TEXT,
    activity_type TEXT,
    agreement_code TEXT NOT NULL,
    ok_version TEXT NOT NULL,
    start_time TIME,
    end_time TIME,
    voluntary_unsocial_hours NUMERIC(6,2),
    occurred_at TIMESTAMPTZ NOT NULL,
    actor_id TEXT,
    actor_role TEXT,
    correlation_id TEXT,
    outbox_id BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_time_entries_proj_emp_date_outbox ON time_entries_projection(employee_id, date, outbox_id);
CREATE INDEX IF NOT EXISTS idx_time_entries_proj_emp_outbox ON time_entries_projection(employee_id, outbox_id);

CREATE TABLE IF NOT EXISTS absences_projection (
    event_id UUID PRIMARY KEY,
    employee_id TEXT NOT NULL,
    date DATE NOT NULL,
    absence_type TEXT NOT NULL,
    hours NUMERIC(6,2) NOT NULL,
    agreement_code TEXT NOT NULL,
    ok_version TEXT NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL,
    actor_id TEXT,
    actor_role TEXT,
    correlation_id TEXT,
    outbox_id BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_absences_proj_emp_date_outbox ON absences_projection(employee_id, date, outbox_id);
CREATE INDEX IF NOT EXISTS idx_absences_proj_emp_outbox ON absences_projection(employee_id, outbox_id);
```

The exact field set should mirror what the GET responses currently materialize from `OfType<TimeEntryRegistered>().Select(...)` and `OfType<AbsenceRegistered>().Select(...)`. Implementing agent verifies against `BalanceEndpoints.cs:90-99`, `SkemaEndpoints.cs:147-167`, `TimeEndpoints.cs:95-107`, `TimeEndpoints.cs:186-194`, `ComplianceEndpoints.cs:55-70` to ensure no field is dropped.

**Validation Criteria**:
- [ ] Both tables added to `init.sql` with PK `event_id`, both indexes (`(employee_id, date, outbox_id)` for date-range scans + `(employee_id, outbox_id)` for full-stream sort)
- [ ] `outbox_id BIGINT NOT NULL` column present (required for ordering by write-time-available value per refinement cycle 3 fix)
- [ ] Field set matches GET-shape requirements verified across 5 GET handlers
- [ ] No schema changes to existing tables (`events`, `outbox_events`, `entitlement_balances`, etc.)
- [ ] `dotnet build` clean — note `init.sql` is not part of build but verify Docker postgres comes up cleanly via `docker compose up postgres -d` smoke

**Files Changed**:
- `docker/postgres/init.sql`

---

### TASK-2703 — `IOutboxEnqueue.EnqueueAndReturnIdAsync(...)` overload

| Field | Value |
|-------|-------|
| **ID** | TASK-2703 |
| **Status** | planned |
| **Agent** | Data Model |
| **Components** | src/Infrastructure/StatsTid.Infrastructure/Outbox/, src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs |
| **KB Refs** | ADR-018 D2 (split-interface design `IOutboxEnqueue` vs `OutboxPublisher`), ADR-018 D3 (atomic state-change in single tx) |
| **Phase** | Phase 1 (sequential — required by Phase 2 endpoints that consume the new overload) |

**Description**: Add a NEW overload `Task<long> EnqueueAndReturnIdAsync(NpgsqlConnection conn, NpgsqlTransaction tx, ...)` to `IOutboxEnqueue` returning the freshly-allocated `outbox_id BIGSERIAL` via `INSERT ... RETURNING outbox_id`. **Existing `Task EnqueueAsync(...)` signature is preserved unchanged** — this is an overload, NOT a breaking interface change (cycle 3 fix of convergent Codex W1 + Reviewer W1-NEW: `ForcedRollbackHarness.ThrowingOutboxEnqueue` at `tests/StatsTid.Tests.Regression/Outbox/ForcedRollbackHarness.cs:72` and all 31 awaiting callers stay untouched). Refinement Approach 5 sub-bullet + AC "NEW overload".

**Sub-deliverables**:

(a) Add `Task<long> EnqueueAndReturnIdAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string streamId, DomainEventBase @event, CancellationToken ct = default)` to `IOutboxEnqueue.cs:48` (alongside existing `EnqueueAsync`). Preserve existing signature.

(b) Implement in `PostgresEventStore.cs` (the production implementation): replace the existing `ExecuteNonQueryAsync` at `PostgresEventStore.cs:195` with `ExecuteScalarAsync` and `INSERT ... RETURNING outbox_id`. The existing `EnqueueAsync` can either delegate to the new overload (discarding the return value) or stay as a separate code path; either is acceptable as long as both share the same SQL shape.

(c) Update `ForcedRollbackHarness.ThrowingOutboxEnqueue` at `ForcedRollbackHarness.cs:72` to also implement the new overload (must throw the same `InvalidOperationException` to preserve forced-rollback semantics for Phase 2 atomic D-tests). Existing `EnqueueAsync` impl unchanged.

(d) Verify by grep that no other custom `IOutboxEnqueue` implementations exist beyond the two (`PostgresEventStore` + `ThrowingOutboxEnqueue`). If any are found, fold them into this task's scope.

**Validation Criteria**:
- [ ] `EnqueueAndReturnIdAsync` overload added to `IOutboxEnqueue` interface
- [ ] `PostgresEventStore` implementation uses `INSERT ... RETURNING outbox_id` via `ExecuteScalarAsync`
- [ ] `ThrowingOutboxEnqueue` test-double implements the new overload (throws to preserve rollback semantics)
- [ ] Existing `Task EnqueueAsync(...)` signature unchanged; all 31 S22-S26 callers compile without modification
- [ ] `dotnet build` clean (0/0)
- [ ] Existing 134 Docker-gated regression tests still pass (no behavior change for callers that don't use the new overload)

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/Outbox/IOutboxEnqueue.cs`
- `src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs`
- `tests/StatsTid.Tests.Regression/Outbox/ForcedRollbackHarness.cs`

---

### TASK-2704 — Projection repositories: `TimeEntryProjectionRepository` + `AbsenceProjectionRepository`

| Field | Value |
|-------|-------|
| **ID** | TASK-2704 |
| **Status** | planned |
| **Agent** | Data Model |
| **Components** | src/Infrastructure/StatsTid.Infrastructure/ |
| **KB Refs** | ADR-018 D3 (atomic state-change), ADR-018 D13 (sync-in-tx projection pattern), S24 TASK-2206 `(conn, tx)` overload convention |
| **Phase** | Phase 1 (sequential — requires TASK-2702 schema; required by Phase 2 endpoints) |

**Description**: Create two new repositories serving the projection tables. Each ships with a `(conn, tx) InsertAsync(..., outboxId)` overload (mirroring `EntitlementBalanceRepository.CheckAndAdjustAsync(conn, tx, ...)` shape per ADR-018 D3 + S24 convention) and read methods consumed by the 5 migrated GET endpoints. Self-managed v1 overloads NOT added — these projections only have one write-side caller (the atomic POST handler). Refinement Approach 2.

**Repository shapes**:

```csharp
public sealed class TimeEntryProjectionRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public Task InsertAsync(NpgsqlConnection conn, NpgsqlTransaction tx, TimeEntryRegistered @event, long outboxId, CancellationToken ct);

    public Task<IReadOnlyList<TimeEntryProjectionRow>> GetByEmployeeAndDateRangeAsync(string employeeId, DateOnly start, DateOnly end, CancellationToken ct);
    public Task<IReadOnlyList<TimeEntryProjectionRow>> GetByEmployeeAsync(string employeeId, CancellationToken ct);
}

public sealed class AbsenceProjectionRepository
{
    public Task InsertAsync(NpgsqlConnection conn, NpgsqlTransaction tx, AbsenceRegistered @event, long outboxId, CancellationToken ct);

    public Task<IReadOnlyList<AbsenceProjectionRow>> GetByEmployeeAndDateRangeAsync(string employeeId, DateOnly start, DateOnly end, CancellationToken ct);
    public Task<IReadOnlyList<AbsenceProjectionRow>> GetByEmployeeAsync(string employeeId, CancellationToken ct);
}
```

`TimeEntryProjectionRow` + `AbsenceProjectionRow` records mirror the projection table columns 1:1.

**Sub-deliverables**:

(a) `TimeEntryProjectionRepository.cs` with `InsertAsync(conn, tx, @event, outboxId, ct)` writing all event fields + `outboxId`. Read methods: `GetByEmployeeAndDateRangeAsync` (used by Skema GET, Balance summary, Compliance period) using index `(employee_id, date, outbox_id)`; `GetByEmployeeAsync` (used by Time GET full-stream) using index `(employee_id, outbox_id)`. Both read methods `ORDER BY outbox_id ASC`.

(b) `AbsenceProjectionRepository.cs` mirror shape.

(c) `TimeEntryProjectionRow` + `AbsenceProjectionRow` immutable record types (PAT-001 init-only properties).

(d) DI registration: scoped lifetimes, registered in the same place existing repositories are (Backend.Api `Program.cs` `builder.Services.AddScoped<...>()`).

(e) NO read-side caller migration in this task — that's Phase 2 (TASK-2706 through TASK-2709). This task only ships the repos.

**Validation Criteria**:
- [ ] `TimeEntryProjectionRepository` + `AbsenceProjectionRepository` created with the shapes above
- [ ] `(conn, tx)` `InsertAsync` overloads accept the source event + `outboxId` and INSERT into the projection
- [ ] Read methods use the correct indexes (`(employee_id, date, outbox_id)` for date-range; `(employee_id, outbox_id)` for full-stream)
- [ ] All read methods `ORDER BY outbox_id ASC`
- [ ] DI registration added to `Backend.Api/Program.cs`
- [ ] `dotnet build` clean (0/0)
- [ ] Existing 134 Docker-gated regression tests still pass

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/TimeEntryProjectionRepository.cs` (new)
- `src/Infrastructure/StatsTid.Infrastructure/AbsenceProjectionRepository.cs` (new)
- `src/SharedKernel/StatsTid.SharedKernel/Models/TimeEntryProjectionRow.cs` (new — or equivalent)
- `src/SharedKernel/StatsTid.SharedKernel/Models/AbsenceProjectionRow.cs` (new)
- `src/Backend/StatsTid.Backend.Api/Program.cs` (DI registration)

---

### TASK-2705 — `tools/ProjectionBackfill` ops script

| Field | Value |
|-------|-------|
| **ID** | TASK-2705 |
| **Status** | planned |
| **Agent** | Data Model |
| **Components** | tools/ProjectionBackfill/ (new directory + project) |
| **KB Refs** | ADR-016 D10 (replay determinism — backfill is the recovery path); S20 `SegmentManifestProjectionRebuilder` pattern at `src/Infrastructure/StatsTid.Infrastructure/SegmentManifestProjectionRebuilder.cs:51` |
| **Phase** | Phase 1 (sequential — independent of TASK-2706+ but ships in Phase 1 for cleanliness) |

**Description**: Build the one-shot backfill tool that rebuilds projection tables from `events`. Idempotent (`ON CONFLICT (event_id) DO NOTHING`). Mirrors S20 `SegmentManifestProjectionRebuilder` shape — a console app under `tools/` runnable via `dotnet run --project tools/ProjectionBackfill -- --connection <conn-string>`. Refinement Approach 3.

**Sub-deliverables**:

(a) Create `tools/ProjectionBackfill/ProjectionBackfill.csproj` (console app, references SharedKernel + Infrastructure).

(b) Create `tools/ProjectionBackfill/Program.cs`:
   - Parse `--connection` arg (default to env var `POSTGRES_CONNECTION_STRING`).
   - Open connection, BEGIN tx (RepeatableRead).
   - Stream `SELECT events.event_id, events.event_type, events.payload, events.stream_version, COALESCE(outbox_events.outbox_id, NULL) AS outbox_id, events.created_at FROM events LEFT JOIN outbox_events ON events.event_id = outbox_events.event_id WHERE events.event_type IN ('TimeEntryRegistered', 'AbsenceRegistered') ORDER BY events.stream_id, events.stream_version`.
   - For each row: deserialize payload via `EventSerializer`; resolve `outbox_id` (use the joined value; fallback to `stream_version` for genuine pre-S22 events with no outbox row — emit warn-log if the row's `created_at >= '2026-05-05'` (S22 deploy date placeholder; implementer pins to actual ADR-018 deploy commit date) since that should be zero in steady state per cycle 3 fix of Reviewer W2-NEW); INSERT into projection table with `ON CONFLICT (event_id) DO NOTHING`.
   - COMMIT.
   - Log row counts: events scanned, projection rows inserted, conflicts skipped (duplicates from re-run).

(c) Re-run idempotency: running twice produces zero new rows on the second run. Verified by D-test in TASK-2710.

**Validation Criteria**:
- [ ] `tools/ProjectionBackfill/Program.cs` runs from CLI against a live Postgres
- [ ] Idempotent: re-run produces zero new rows
- [ ] Logs row counts (scanned, inserted, conflicts)
- [ ] Warn-log fires when the `stream_version` fallback is used for any event with `created_at >= '<S22 deploy date>'`; should be zero in current dev DB
- [ ] `dotnet build` clean for the new project + solution build
- [ ] Solution file (`StatsTid.sln`) updated to include the new tool project

**Files Changed**:
- `tools/ProjectionBackfill/ProjectionBackfill.csproj` (new)
- `tools/ProjectionBackfill/Program.cs` (new)
- `StatsTid.sln` (project added)

---

### TASK-2706 — Skema endpoint migration: atomic POST + projection-read GET + quota fix

| Field | Value |
|-------|-------|
| **ID** | TASK-2706 |
| **Status** | planned |
| **Agent** | API Integration (Backend.Api/Endpoints scope) + cross-domain authorization for projection repository consumption (per AGENTS.md Cross-Domain Authorization sub-section, S22 convention) |
| **Components** | src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs |
| **KB Refs** | ADR-018 D3 (atomic single-tx), ADR-018 D13 (sync-in-tx projection); S22 ConfigEndpoints atomic exemplar |
| **Phase** | Phase 2 (parallel via worktrees — depends on Phase 1 commits) |

**Description**: Re-do S26 TASK-2604 (multi-event single-tx wrap) with projection writes added inside the same tx. Migrate Skema GET to read from projections. Restore `SkemaQuotaBreachException → 422` at `:414-418`. Refinement Approach 4 (Skema GET migration) + Approach 5 (Skema POST atomic re-attempt) + Approach 6 (quota-race fix).

**Sub-deliverables**:

(a) **GET migration** at `SkemaEndpoints.cs:144`: replace `var allEvents = await eventStore.ReadStreamAsync(streamId, ct);` + `OfType<TimeEntryRegistered>().Where(...)` + `OfType<AbsenceRegistered>().Where(...)` with parallel reads from `TimeEntryProjectionRepository.GetByEmployeeAndDateRangeAsync(employeeId, monthStart, monthEnd, ct)` + `AbsenceProjectionRepository.GetByEmployeeAndDateRangeAsync(...)`. The handler still reads `FlexBalanceUpdated` from `events` if present (out of scope; not removed).

(b) **POST atomic wrap** at `SkemaEndpoints.cs:351-440`: wrap the entire save handler in `BeginTransactionAsync`. Inside the loop bodies (L356-369 entries, L379-393 absences), per-event ordering is **(1) outbox enqueue FIRST** via `IOutboxEnqueue.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct)` returning `outbox_id`, **(2) projection INSERT SECOND** via `TimeEntryProjectionRepository.InsertAsync(conn, tx, @event, outboxId, ct)` (or `AbsenceProjectionRepository`). The `EntitlementBalanceAdjusted` event at L426-439 also goes through `EnqueueAndReturnIdAsync` (no projection write — `entitlement_balances` is write-back projection mutated by `CheckAndAdjustAsync`, not event-derived). `tx.CommitAsync` at end. `eventStore.AppendAsync` calls REMOVED — outbox enqueue replaces them.

(c) **Quota-race fix** at `SkemaEndpoints.cs:414-418`: replace `if (!success) continue;` with `throw new SkemaQuotaBreachException(entitlementType, deltaDays, currentUsed, data.EffectiveQuota);`. Endpoint catches via try/catch around the atomic-tx body → returns 422 with body shape `{ error, absenceType, remaining, requested, message }` matching the pre-validation 422 at `SkemaEndpoints.cs:332-341` exactly.

(d) **Skema bundle-rollback semantic**: the catch path lets the atomic tx roll back; ALL prior `TimeEntryRegistered` events from the same handler call are rolled back too. Documented in commit message body. Pinned by the dedicated D-test in TASK-2710.

(e) Add `SkemaQuotaBreachException` class if it doesn't already exist (S26 TASK-2604 introduced one but the revert may have removed it; check git history at `62cfb20` for the exact shape and re-introduce).

**Validation Criteria**:
- [ ] `SkemaEndpoints.cs:144` GET reads from `TimeEntryProjectionRepository` + `AbsenceProjectionRepository`; zero hits on `eventStore.ReadStreamAsync` in the GET handler body (grep-zero-hits AC)
- [ ] `SkemaEndpoints.cs:351-440` POST wraps state-change + outbox enqueue + projection INSERT in a single `BeginTransactionAsync`
- [ ] Per-event ordering: outbox enqueue FIRST (returning `outbox_id`), projection INSERT SECOND (consuming `outbox_id`)
- [ ] `eventStore.AppendAsync` calls removed from the POST handler (replaced by outbox enqueue)
- [ ] `SkemaQuotaBreachException → 422` at `:414-418` (no silent `continue`); 422 body shape matches pre-validation 422 exactly
- [ ] `dotnet build` clean (0/0)
- [ ] Existing 35 plain regression tests still pass
- [ ] Existing 134 Docker-gated regression tests still pass (after updating any Skema-touching tests to expect new tx shape)

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaQuotaBreachException.cs` (new or restored)

---

### TASK-2707 — Time endpoint migration: atomic POST + projection-read GET

| Field | Value |
|-------|-------|
| **ID** | TASK-2707 |
| **Status** | planned |
| **Agent** | API Integration (Backend.Api/Endpoints scope) |
| **Components** | src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs |
| **KB Refs** | ADR-018 D3, ADR-018 D13 |
| **Phase** | Phase 2 (parallel via worktrees) |

**Description**: Re-do S26 TASK-2606 (Time atomic) with projection writes added inside the same tx. Migrate Time GET endpoints to read from projections. Refinement Approach 4 (Time GET migration) + Approach 5 (Time POST atomic re-attempt). FlexBalance GET at `:217` and FlexBalanceUpdated read at `BalanceEndpoints.cs:102` are explicitly OUT OF SCOPE (per Approach 1 + 4 sub-clause).

**Sub-deliverables**:

(a) **POST atomic wrap** at `TimeEndpoints.cs:71` (`POST /api/time-entries`): wrap in `BeginTransactionAsync`; per-event ordering outbox enqueue (returning `outbox_id`) → projection INSERT (consuming `outbox_id`) → `tx.CommitAsync`. Remove `eventStore.AppendAsync`.

(b) **POST atomic wrap** at `TimeEndpoints.cs:163` (`POST /api/absences`): same pattern with `AbsenceProjectionRepository`.

(c) **GET migration** at `TimeEndpoints.cs:93` (`GET /api/time-entries/{employeeId}` — full-stream, no date filter): replace `OfType<TimeEntryRegistered>` filter on `eventStore.ReadStreamAsync` with `TimeEntryProjectionRepository.GetByEmployeeAsync(employeeId, ct)`.

(d) **GET migration** at `TimeEndpoints.cs:184` (`GET /api/absences/{employeeId}` — full-stream): replace with `AbsenceProjectionRepository.GetByEmployeeAsync(employeeId, ct)`.

(e) **OUT OF SCOPE** — `TimeEndpoints.cs:217` (`GET /api/flex-balance/{employeeId}`) continues to read `FlexBalanceUpdated` from event stream. Do NOT touch this handler. Grep-zero-hits AC is scoped to L93 + L184 only.

**Validation Criteria**:
- [ ] L71 + L163 POST sites wrapped in `BeginTransactionAsync` + per-event ordering pinned
- [ ] L93 + L184 GET handlers read from projections; zero hits on `eventStore.ReadStreamAsync` in their handler bodies
- [ ] L217 GET handler unchanged (out of scope)
- [ ] `dotnet build` clean (0/0)
- [ ] Existing 35 plain regression + 134 Docker-gated tests still pass

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs`

---

### TASK-2708 — Balance endpoint read-path migration

| Field | Value |
|-------|-------|
| **ID** | TASK-2708 |
| **Status** | planned |
| **Agent** | API Integration |
| **Components** | src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs |
| **KB Refs** | ADR-018 D13 |
| **Phase** | Phase 2 (parallel via worktrees — small task; could be folded with TASK-2707 if worktree sizing is too granular) |

**Description**: Migrate Balance summary GET to read time-entries + absences from projections. Verified read-only endpoint per Assumption #13 (no POST sites). Refinement Approach 4.

**Sub-deliverables**:

(a) **GET migration** at `BalanceEndpoints.cs:87`: replace `var allEvents = await eventStore.ReadStreamAsync(streamId, ct);` + `OfType<TimeEntryRegistered>().Where(...).Sum(...)` (sum of hours) and `OfType<AbsenceRegistered>().Where(... VACATION ...).Distinct().Count()` (vacation days used) with corresponding queries against `TimeEntryProjectionRepository.GetByEmployeeAndDateRangeAsync(employeeId, monthStart, monthEnd, ct)` and `AbsenceProjectionRepository.GetByEmployeeAsync(...)` filtered by year + VACATION type.

(b) **OUT OF SCOPE** — `BalanceEndpoints.cs:102` (`var latestFlex = allEvents.OfType<FlexBalanceUpdated>().LastOrDefault();`) continues to read from event stream. The `allEvents = await eventStore.ReadStreamAsync(streamId, ct)` call may need to be retained ONLY for the `FlexBalanceUpdated` read; the time-entries + absences sums move to projections. Implementer decides whether to keep the `allEvents` call as a local variable for flex only, or restructure — either is acceptable as long as the time-entries + absences reads come from projections.

**Validation Criteria**:
- [ ] L87 time-entries sum + absences vacation-count come from projections
- [ ] L102 FlexBalanceUpdated read continues from event stream (out of scope per Approach 1)
- [ ] `dotnet build` clean (0/0)
- [ ] Existing 35 plain regression + 134 Docker-gated tests still pass

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs`

---

### TASK-2709 — Compliance endpoint read-path migration

| Field | Value |
|-------|-------|
| **ID** | TASK-2709 |
| **Status** | planned |
| **Agent** | API Integration |
| **Components** | src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs |
| **KB Refs** | ADR-018 D13, PAT-005 (PeriodCalculationService HTTP — Compliance endpoint follows the HTTP-to-rule-engine pattern, projection-source change does not affect the HTTP boundary) |
| **Phase** | Phase 2 (parallel via worktrees — small task) |

**Description**: Migrate Compliance period GET to read time-entries from projection. The `/api/compliance/{employeeId}/compensatory-rest` endpoint at L105 reads from `compensatory_rest` (DB-backed since S16) and is unchanged. Refinement Approach 4 + Assumption #13.

**Sub-deliverables**:

(a) **GET migration** at `ComplianceEndpoints.cs:53`: replace `var allEvents = await eventStore.ReadStreamAsync(streamId, ct);` + `OfType<TimeEntryRegistered>().Where(...)` filter feeding the rule-engine compliance HTTP request (L86-95) with `TimeEntryProjectionRepository.GetByEmployeeAndDateRangeAsync(employeeId, monthStart, monthEnd, ct)`. The downstream HTTP call shape is unchanged (still posts `complianceRequest` to `/api/rules/check-compliance`).

(b) **L105 untouched** — `compensatory-rest` endpoint reads from `CompensatoryRestRepository` already.

**Validation Criteria**:
- [ ] L53 time-entries read comes from `TimeEntryProjectionRepository`
- [ ] L105 unchanged
- [ ] Rule engine HTTP request shape unchanged (PAT-005 boundary preserved)
- [ ] `dotnet build` clean (0/0)
- [ ] Existing 35 plain regression + 134 Docker-gated tests still pass

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs`

---

### TASK-2710 — D-test suite (~12–14 D-tests)

| Field | Value |
|-------|-------|
| **ID** | TASK-2710 |
| **Status** | planned |
| **Agent** | Test & QA |
| **Components** | tests/StatsTid.Tests.Regression/ |
| **KB Refs** | ADR-018 D3 (atomic), ADR-018 D5 (per-stream FIFO), ADR-018 D13 (sync-in-tx projection) |
| **Phase** | Phase 3 (sequential — runs after Phase 2 endpoints land + TASK-2701 harness ready) |

**Description**: Net-new D-test coverage for the projection layer + atomic re-attempt + bundle-rollback + read-your-write. All `[Trait("Category", "Docker")]`. Mirrors S24/S26 layout under `tests/StatsTid.Tests.Regression/Outbox/` + `tests/StatsTid.Tests.Regression/Hosting/` + `tests/StatsTid.Tests.Regression/Infrastructure/`. Refinement Approach 8 (cycle 3 tightened assertions).

**Test slots (~12–14 D-tests)**:

1. **Read-your-write under publisher stall** (~2 tests, the marquee architectural-fix proof): with `OutboxPublisher` BackgroundService **stopped via `StopPublisherAsync()`** from the new TASK-2701 harness, POST then immediate GET. Assert ALL of: (a) GET response contains the just-written record; (b) `events` table has zero matching rows; (c) **unpublished `outbox_events` row exists** for the write (guards "projection written, outbox missing" failure mode); (d) projection row's `outbox_id` matches the unpublished outbox row's `outbox_id`. Two tests covering both `time_entries_projection` (POST `/api/time-entries`) and `absences_projection` (POST `/api/absences`). **This test FAILS in the S26-revert baseline and PASSES post-S27 — pinning the architectural fix.**

2. **Skema bundle-rollback semantic** (1 test): POST Skema with mixed entries+absences where absence #N (with N > 0) breaches quota. Assert all `TimeEntryRegistered` events from the same call are rolled back too (zero rows in `time_entries_projection` for that month-employee).

3. **Atomic rollback under forced outbox failure** (2 tests): mirrors S24 `ForcedRollbackHarness` — Skema multi-event POST forces `ThrowingOutboxEnqueue` to throw; verify zero events in `events`/`outbox_events`/`time_entries_projection`/`absences_projection`. Time single-event POST same shape.

4. **Atomic rollback under quota breach** (2 tests): single-employee race + multi-absence batch. `SkemaQuotaBreachException → 422` + zero projection rows for the breached batch. Optional `pg_locks` snapshot inside the test (confirms no unexpected serialization) per cycle 3 fix of Reviewer N1-NEW.

5. **Projection-vs-event-stream parity** (drain-sync + row-count guard, 2 tests): write N events via POST, then (a) assert exactly N unpublished `outbox_events` rows exist (row-count guard against vacuous success), (b) wait for publisher to drain (poll `outbox_events.published_at IS NOT NULL` count → N), (c) assert resulting `events` table contains exactly N matching rows, (d) assert projection contents match `OfType<TimeEntryRegistered>` filter on `events` for the same employee (same row count, same field values, both ordering columns produce the same sequence — `outbox_id` and `stream_version` are co-monotonic for events from the same publisher).

6. **Projection backfill idempotency** (1 test): write N events via POST, run `ProjectionBackfill` ops script twice; assert second run produces zero new projection rows (`ON CONFLICT DO NOTHING` working as intended).

7. **TxContractTests** (2 tests): `TimeEntryProjectionRepository.InsertAsync(conn, tx, ...)` + `AbsenceProjectionRepository.InsertAsync(conn, tx, ...)` participate in caller tx (no projection rows persisted on rollback).

**Validation Criteria**:
- [ ] ~12–14 new D-tests added under `tests/StatsTid.Tests.Regression/Outbox/`, `Hosting/`, and `Infrastructure/`
- [ ] All `[Trait("Category", "Docker")]`
- [ ] Read-your-write tests use `StopPublisherAsync()` from TASK-2701 — NOT Task.Delay, NOT a config flag
- [ ] All assertions tightened per cycle 3: outbox-row-existence + outbox_id-match + row-count guards
- [ ] All compile clean (Docker runtime gating same as S24/S26)
- [ ] Existing 134 Docker-gated regression tests still pass (regression)
- [ ] Existing 88 frontend vitest tests still pass without modification (frontend-unchanged AC)

**Files Changed** (anticipated):
- `tests/StatsTid.Tests.Regression/Outbox/SkemaProjectionAtomicTests.cs` (new)
- `tests/StatsTid.Tests.Regression/Outbox/TimeProjectionAtomicTests.cs` (new)
- `tests/StatsTid.Tests.Regression/Hosting/PublisherStallReadYourWriteTests.cs` (new — uses TASK-2701 harness)
- `tests/StatsTid.Tests.Regression/Outbox/ProjectionParityTests.cs` (new)
- `tests/StatsTid.Tests.Regression/Outbox/ProjectionBackfillTests.cs` (new)
- `tests/StatsTid.Tests.Regression/Infrastructure/TxContractTests.cs` (extend)

---

### TASK-2711 — ADR-018 D13 documentation update (was D12 in plan; renumbered after collision check)

| Field | Value |
|-------|-------|
| **ID** | TASK-2711 |
| **Status** | planned |
| **Agent** | Orchestrator-direct (knowledge-base writes are Orchestrator-only per WORKFLOW.md L48) |
| **Components** | docs/knowledge-base/decisions/ADR-018-...md |
| **KB Refs** | ADR-018 (amended additively) |
| **Phase** | Phase 3 (parallel-with or after TASK-2710 — just docs) |

**Description**: Add D12 to ADR-018 in Decision/Rationale/Consequences format mirroring D7-D11. NOT a footnote (cycle 2 fix of Reviewer W4). The discipline-boundary clause on projection-only fields is moved to "Open (deferred to Phase 4d-3 refinement)" per cycle 3 convergent NOTE. No ADR-018 Status bump required (cycle 3 Codex N1: D12 is additive within already-accepted ADR family).

**Decision text** (verbatim from refinement Approach 7):

> **D12: Atomic-outbox migration requires a synchronous projection table for any GET that reads back the just-written state.**
>
> **Rationale**: Reads from `events` via `IEventStore.ReadStreamAsync` see the publisher-drained snapshot, which lags POST commit by up to ~1s in steady state and unbounded under publisher backpressure (`OutboxPublisher.cs:32-45`). Sync-in-tx projection (POST writes both the event/outbox AND the projection in the same transaction) is the only pattern that preserves read-your-write without serializing on the publisher loop.
>
> **Consequences**: Any future endpoint adding atomic-outbox MUST audit GET-side read paths first. If reads currently come from `events.ReadStreamAsync`, a projection table is a prerequisite. S22 ConfigEndpoints succeeded only because `local_agreement_profiles` already served as a projection. S26 TASK-2604 + TASK-2606 reverted because Skema/Time had no projection.
>
> **Open** (deferred to Phase 4d-3 refinement): whether projections may carry projection-only enrichment fields or must remain a strict view of event payload — adjudicated when Phase 4d-3 (employee-profile versioned history) makes the constraint concrete.

**Sub-deliverables**:

(a) Append D12 block to ADR-018 after the existing D11.

(b) Add a Review-Cycles-section footnote: "S27 / TASK-2711: D12 added documenting the sync-in-tx projection requirement that S26 TASK-2604+TASK-2606 violated. Reviewer W4 cycle 2 + Codex W4 cycle 1 convergent on promoting from a D6 footnote to a discrete decision."

(c) Update `docs/knowledge-base/INDEX.md` if needed (likely no update — ADR-018's row already exists; D12 is internal to that ADR).

**Validation Criteria**:
- [ ] ADR-018 D13 block added (Decision/Rationale/Consequences/Open format)
- [ ] Review-Cycles-section footnote added
- [ ] No Status bump on ADR-018 header (additive amendment)
- [ ] INDEX.md untouched (or trivial update only if needed)

**Files Changed**:
- `docs/knowledge-base/decisions/ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md`

---

## Phase Ordering

**Phase 1 (sequential — single-agent steps, commit between each, per S26 R7)**:
1. TASK-2701 (test harness) → commit
2. TASK-2702 (schema) → commit
3. TASK-2703 (IOutboxEnqueue overload) → commit
4. TASK-2704 (projection repos) → commit
5. TASK-2705 (backfill ops script) → commit

**Critical**: commit Phase 1 BEFORE dispatching Phase 2 worktrees (S26 R7 lesson absorbed).

**Phase 2 (parallel via worktrees — 4 simultaneously-active)**:
- TASK-2706 Skema (1 worktree — large, ~120 LOC handler wrap + GET migration + quota fix)
- TASK-2707 Time (1 worktree — 2 POST + 2 GET migrations)
- TASK-2708 Balance (1 worktree — 1 GET migration only; small)
- TASK-2709 Compliance (1 worktree — 1 GET migration only; small)

All 4 touch different endpoint files → no merge conflicts.

**Phase 3 (sequential, runs AFTER Phase 2 per AGENTS.md L37)**:
- TASK-2710 Test & QA (D-test suite)
- TASK-2711 ADR-018 D13 (Orchestrator-direct, parallel-with TASK-2710 if convenient)

**Phase 4 (Orchestrator)**:
- Build/test validation
- Constraint Validator on each agent output (every Phase 1 task + every Phase 2 worktree)
- Reviewer audit per task — MANDATORY for all Phase 2 atomic-tx endpoint work (P3 + P5 trigger)
- Step 7a Codex review on full sprint diff vs `c5edf52`

**Cycle cap discipline**: 2 BLOCKER-fix cycles per lens at Step 7a. After cycle 2 on either lens, halt and prompt user.

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule engine changes |
| Wage type mappings produce correct SLS codes | N/A | No payroll calculation changes |
| Overtime/supplement determinism | N/A | No rule engine changes |
| Absence effects correct | DOC-CHANGE | Skema TASK-2706 quota-race fix changes observable behavior on race: pre-S27 silent skip + 200 OK + inconsistent state → post-S27 atomic rollback + 422. Frontend already handles 422 from pre-validation; UX unchanged. Bundle-rollback semantic (whole save rolls back when one absence breaches): documented in commit message + Skema bundle-rollback D-test pins it. No legal exposure (the breaching absence wasn't recorded under either behavior). |
| Retroactive recalculation stable | N/A | Projection tables are derived state per ADR-016 D10; replay determinism preserved. Backfill from `events` is the recovery path. No retroactive logic changes. |

## External Review (Step 7a)

_Pending sprint-end._

| Field | Value |
|-------|-------|
| **Invoked** | _pending_ |
| **Sprint-start commit** | `c5edf52` (S26 sprint close) |
| **Command** | `codex review "<S27 Phase 4c.6 sprint review prompt>"` (prompt-alone, uncommitted) — fallback to `codex review --base c5edf52` if intermediate commits exist on master |
| **Review Cycles** | _pending_ |
| **Findings** | _pending_ |

## Test Summary

_Pending sprint-end. Target ~795 total: 525 unit (unchanged) + 35 plain regression (unchanged) + ~146-148 Docker-gated (134 pre-S27 + ~12-14 net new from TASK-2710) + 88 frontend vitest (unchanged) = ~795._

## Agent Effectiveness

_Pending sprint-end._

## Sprint Retrospective

_Pending sprint-end._
