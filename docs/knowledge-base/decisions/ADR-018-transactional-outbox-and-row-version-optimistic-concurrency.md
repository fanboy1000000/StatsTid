# ADR-018: Transactional Outbox + Row-Version Optimistic Concurrency

**Status**: ACCEPTED (cycle 4 fixes applied + cycle 5 verified clean; pending Orchestrator approval)
**Date**: 2026-05-03
**Sprint**: 22
**Augments**: [ADR-001](ADR-001-event-sourcing-postgresql-npgsql.md) (event sourcing topology — outbox is an additive layer between state-change writes and the canonical event store)
**Supersedes**: [ADR-004](ADR-004-outbox-pattern-guaranteed-delivery.md) (approved S1 but never implemented; ADR-018 fulfills the original commitment AND adds state-change-to-event-store atomicity, which ADR-004 did not address)
**Amends**: [ADR-017](ADR-017-local-agreement-configuration-as-a-profile.md) D2 (close-then-insert window math → end-exclusive `effective_to`) + D2.1 (`If-Match: <profile_id>` → `If-Match: <version>`)

## Context

Two architectural seams surfaced during S20 + S21 implementation that share a common boundary — the relationship between state-change writes and the canonical event store, and what "the current row" means for an optimistic-concurrency token.

**Seam 1 — Post-commit event-store append (S21 cycle-2 finding).**
S21's `ConfigEndpoints` PUT handler appends `LocalAgreementProfileChanged` to the canonical event store *after* the profile-and-audit transaction commits, because the in-tx alternative reads `MAX(stream_version)` from a stale `RepeatableRead` snapshot and concurrent saves collide on the `(stream_id, stream_version)` UNIQUE constraint. This is functionally correct under normal operation but leaves a residual partial-failure window: process crash between profile-tx commit and event-store append produces a state-change with no corresponding event. The same shape exists across ~12 state-change-emitting endpoint sites in Backend.Api + Payroll + External integrations.

**Seam 2 — Same-day re-saves on `local_agreement_profiles` (S21 Step 7a finding, cycles 3-9).**
S21 ADR-017 D2's close-then-insert path stamps predecessor `effective_to = newProfile.EffectiveFrom - 1`. When `newProfile.EffectiveFrom <= predecessor.EffectiveFrom` (the common in-place-edit case, where the frontend submits the unchanged loaded `effective_from`), this produces predecessor `effective_to <= effective_from` — an invalid history window. Step 7a cycles 3-9 explored half-fixes (temporal-monotonicity guard, UPDATE-in-place); each layered fix produced cascading regressions on profile_id-as-ETag (lost-update on concurrent in-place edits, audit-shape drift, self-cycle on `PrecedingProfileId`). Cycle 9's diagnosis: ETag-via-profile_id + close-then-insert + same-day saves are mutually unsatisfiable without a separate row-version token AND clean end-exclusive `effective_to` semantics.

Both seams share the architectural family of "atomic relationship between a domain mutation and a corresponding identifier/event." ADR-018 resolves both as one redesign:

- **Outbox** (D1-D6) — state-change writes commit to a single transaction including an `outbox_events` row; a per-service in-process publisher drains the outbox to the canonical event store with at-least-once semantics. Eliminates the post-commit-append partial-failure window.
- **Row-version + end-exclusive `effective_to`** (D7-D11) — `local_agreement_profiles` gains a `BIGINT version` column; the ETag becomes `<version>` instead of `<profile_id>`. End-exclusive semantics eliminate the off-by-one in the close-then-insert path. Same-day saves route to UPDATE-in-place (bumps version, no new row); supersession routes to close-then-insert (predecessor `effective_to = newProfile.EffectiveFrom`, no `-1`).

## Decision

We adopt the transactional outbox pattern for state-change-to-event-store atomicity AND a row-version column on `local_agreement_profiles` with end-exclusive `effective_to` semantics. The two changes ship together because ADR-018's outbox is the prerequisite for any in-tx event-emission path, and the row-version column needs the in-tx event-emission path to atomically record `LocalAgreementProfileChanged` events for in-place edits.

D2.2 (ETag/If-Match propagation across `agreement_configs`, `position_overrides`, `wage_type_mappings`, `entitlement_configs`) is deferred to S23 (ADR-019). Splitting was decided at S22 Step 0b plan review (2026-05-03) on the Reviewer's evidence that `agreement_configs` alone has three distinct race conditions (DRAFT in-place / DRAFT→ACTIVE publish / ACTIVE→ARCHIVED clone) and that the row-version pattern needs a published exemplar before propagation.

## Detailed Decisions

### D1 — Outbox table schema (Q1, Q4)

A single PostgreSQL table `outbox_events` resides in the existing `statstid` database alongside the projection tables. Single-DB choice is forced by the requirement that the outbox INSERT participate in the same transaction as state-change writes; cross-DB or two-phase commit is over-engineered for a service architecture where the canonical event store is already PostgreSQL.

```sql
CREATE TABLE outbox_events (
    outbox_id        BIGSERIAL    PRIMARY KEY,
    service_id       TEXT         NOT NULL,            -- 'backend-api' | 'payroll' | 'external' | 'orchestrator'
    stream_id        TEXT         NOT NULL,
    event_id         UUID         NOT NULL UNIQUE,     -- mirrors events.event_id; correlation key for at-least-once recovery (cycle 2 fix)
    event_type       TEXT         NOT NULL,
    event_payload    JSONB        NOT NULL,
    correlation_id   TEXT         NULL,
    actor_id         TEXT         NULL,
    actor_role       TEXT         NULL,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    published_at     TIMESTAMPTZ  NULL,                -- NULL = unpublished; doubles as consumed marker
    stream_version   INT          NULL,                -- assigned by publisher post-publish; debug join only
    attempts         INT          NOT NULL DEFAULT 0,
    last_error       TEXT         NULL,
    last_attempt_at  TIMESTAMPTZ  NULL
);

-- Polling-friendly index for unpublished rows scoped per service.
CREATE INDEX idx_outbox_unpublished
    ON outbox_events (service_id, outbox_id)
    WHERE published_at IS NULL;

-- Lookup for failed/retry queries.
CREATE INDEX idx_outbox_attempts
    ON outbox_events (service_id, attempts, last_attempt_at)
    WHERE published_at IS NULL AND attempts > 0;

-- Stream-locality index for FIFO ordering at publish time.
CREATE INDEX idx_outbox_stream
    ON outbox_events (stream_id, outbox_id)
    WHERE published_at IS NULL;
```

The `service_id` column enables Q2's per-service publisher topology without cross-service contention on row claims (each publisher polls its partition). `stream_version` is debug-only — the publisher populates it after a successful event-store INSERT for forward-traceable outbox→events joins.

The `event_id` column (added cycle 2) is the at-least-once correlation key. The state-change site generates a fresh `Guid` at enqueue time and stores it on both the outbox row AND the eventual `events.event_id` (which already exists as `UUID NOT NULL UNIQUE` per `init.sql:13`). On crash recovery, the publisher uses `event_id` to deterministically prove "this outbox row's event already exists in the canonical store" vs "a different outbox row won this stream_version slot." See D4 below for the protocol.

The `correlation_id`, `actor_id`, `actor_role` columns mirror the audit-context fields the canonical events already carry; capturing them at enqueue time keeps the publisher's INSERT stateless (no need to re-resolve `ActorContext` in the publisher loop).

### D2 — Outbox publisher topology (Q2)

Three options were enumerated at Step 0b cycle 1:
- (a) Single in-process publisher inside Backend.Api.
- (b) Per-service in-process publisher (Backend.Api, Payroll, External, Orchestrator each runs its own).
- (c) Dedicated `StatsTid.OutboxPublisher` worker (new 9th docker service).

**Decision: (b) per-service in-process publisher.** Each service hosts a `BackgroundService`-implementing `OutboxPublisher` that polls `WHERE service_id = @ownServiceId AND published_at IS NULL ORDER BY outbox_id` every poll interval (default 250ms with 1s backoff on quiet polls). Rationale (cycle-2 review correction — the original "Backend.Api can't deserialize cross-service events" rationale was factually wrong, since all event types live in `SharedKernel.Events` and `EventSerializer` registers them centrally; the real reasons are operational):

- **(a) — single in-process publisher inside Backend.Api — rejected.** Couples Backend.Api uptime to event delivery for Payroll AND External writes. If Backend.Api crashes or restarts during deployment, Payroll's `PeriodCalculationCompleted` and External's `IntegrationDeliveryTracked` events stop publishing. The single-point-of-failure shape contradicts the per-service Docker topology established in ADR-006.
- **(b) — per-service in-process publisher — chosen.** Stream-ownership is naturally per-service (D6 below codifies the invariant that each `stream_id` is owned by exactly one service); the per-service publisher partitions follow stream ownership without cross-publisher contention. Each service's outbox publishes its own writes; failures are isolated to that service.
- **(c) — dedicated worker service — rejected.** A 9th docker service for a polling loop is over-engineering pre-production. Worth revisiting in Phase 5 if measured publisher latency or contention warrants the operational footprint.

Cross-service event ordering: per-stream FIFO is preserved within a single service because stream_ids are service-local in practice (e.g., `local-agreement-profile-{org}-{agreement}-{ok_version}` is only written by Backend.Api). Cross-service streams would require a global publisher, but no such streams exist today; D6 enforces the assumption.

### D3 — Split: `IEventStore` (SharedKernel) + `IOutboxEnqueue` (Infrastructure) (Q3)

**Decision (cycle-6 amendment, 2026-05-03)**: split into two interfaces. The cycle-1 framing of "single interface, two overloads on `IEventStore`" was correct for the protocol but wrong for the assembly graph: the in-tx overload's parameter types (`NpgsqlConnection`, `NpgsqlTransaction`) live in the `Npgsql` package, and `IEventStore` is in `StatsTid.SharedKernel.Interfaces`. Adding the overload to `IEventStore` would require adding `<PackageReference Include="Npgsql" />` to `StatsTid.SharedKernel.csproj`, which transitively reaches `StatsTid.RuleEngine.Api` (via its existing `<ProjectReference>` to SharedKernel). The post-S19 `b4fc670` cleanup deliberately extracted `StatsTid.Auth` so RuleEngine.Api would reference SharedKernel + Auth only and be Npgsql-free; the cycle-1 framing regresses that.

The corrected design:

```csharp
// StatsTid.SharedKernel.Interfaces — UNCHANGED. No Npgsql types. Used by the
// publisher (post-S22) and historical readers. RuleEngine.Api transitively
// references this; the assembly graph stays Npgsql-free.
public interface IEventStore
{
    Task AppendAsync(string streamId, IDomainEvent @event, CancellationToken ct = default);
    Task<IReadOnlyList<IDomainEvent>> ReadStreamAsync(string streamId, CancellationToken ct = default);
    Task<IReadOnlyList<IDomainEvent>> ReadAllAsync(int fromPosition = 0, int maxCount = 1000, CancellationToken ct = default);
}
```

```csharp
// StatsTid.Infrastructure.Outbox — NEW. Lives in Infrastructure (which already
// references Npgsql). Single method; consumed only by state-change sites that
// already reference Infrastructure (Backend.Api, Payroll, External). RuleEngine
// does NOT reference Infrastructure (post-S19 b4fc670), so it never sees this
// interface.
namespace StatsTid.Infrastructure.Outbox;

public interface IOutboxEnqueue
{
    /// <summary>
    /// Enqueues an event into <c>outbox_events</c> within the caller-supplied
    /// transaction. The caller commits or rolls back; outbox visibility follows
    /// tx commit/rollback. A separate per-service <c>OutboxPublisher</c> drains
    /// outbox_events to the canonical event store with at-least-once semantics.
    /// </summary>
    Task EnqueueAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string streamId,
        IDomainEvent @event,
        CancellationToken ct = default);
}
```

The single concrete implementation `PostgresEventStore` (in `StatsTid.Infrastructure`) implements **both** interfaces — the read/append surface for publishers and historical readers (`IEventStore`) plus the in-tx enqueue surface for state-change sites (`IOutboxEnqueue`). DI registers it under both interface contracts:

```csharp
builder.Services.AddSingleton<PostgresEventStore>();
builder.Services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<PostgresEventStore>());
builder.Services.AddSingleton<IOutboxEnqueue>(sp => sp.GetRequiredService<PostgresEventStore>());
```

State-change sites inject `IOutboxEnqueue` (NOT `IEventStore`):

```csharp
// Pre-S22 (post-commit, residual crash window):
await tx.CommitAsync(ct);
await eventStore.AppendAsync(streamId, @event, ct);  // IEventStore

// Post-S22 (in-tx, atomic with state-change):
await outbox.EnqueueAsync(conn, tx, streamId, @event, ct);  // IOutboxEnqueue
await tx.CommitAsync(ct);
```

The cycle-6 split also resurrects the original Q3 alternative (a) — `IOutboxEnqueue` as a separate interface. The Q3 cycle-1 rationale ("doubles the type taxonomy admin code must navigate") is correct in narrow terms but underweighted the architectural cost; cycle-6's amendment finds the assembly-graph protection more valuable than the type-taxonomy unification. The `IEventEmitter` alternative (Q3 option (c)) remains rejected — it adds a third name without further benefit over (a).

**Naming**: `EnqueueAsync` (not `AppendAsync`) preserved on `IOutboxEnqueue` to make the call-site semantic visible — state-change sites are NOT writing to the canonical event store directly; they're enqueuing for the publisher to forward.

### D4 — At-least-once via publisher-time version assignment (Q5)

The publisher computes `stream_version` at publish time, not at enqueue time. The events table's existing `event_id UUID NOT NULL UNIQUE` column (`init.sql:13`) is the at-least-once correlation key — the outbox row carries the same `event_id` written at enqueue time, and the publisher uses it on crash recovery to deterministically prove "this outbox row's event is already in the canonical store" vs "a different outbox row won this stream_version slot."

Sequence per outbox row:

1. `BEGIN` (publisher's own self-contained tx).
2. **Ensure stream row + acquire stream lock**: unconditionally `INSERT INTO event_streams (stream_id) VALUES (@streamId) ON CONFLICT DO NOTHING` (idempotent — no-op if the row already exists). Then `SELECT 1 FROM event_streams WHERE stream_id = @streamId FOR UPDATE`. The `ON CONFLICT DO NOTHING` upsert mirrors the existing pattern in `PostgresEventStore.AppendAsync` so the publisher and the state-change path produce stream rows the same way. The FOR UPDATE locks the parent row in `event_streams` (which has only `(stream_id, created_at)` per `init.sql:5-8`), serializing concurrent publishers on the same stream. The lock is held until the tx commits at step 6.
3. **Compute next version**: `SELECT COALESCE(MAX(stream_version), 0) + 1 FROM events WHERE stream_id = @streamId`. Note: `stream_version` lives on `events`, NOT `event_streams` (cycle-2 review fix). The MAX read is safe under the FOR UPDATE held in step 2 because no other publisher can be writing to this stream concurrently.
4. **Insert canonical event**: `INSERT INTO events (event_id, stream_id, stream_version, event_type, data, occurred_at) VALUES (@outboxRow.event_id, @streamId, @newVersion, ...)`. The `event_id` is taken from the outbox row, not regenerated.
5. **Mark outbox published**: `UPDATE outbox_events SET published_at = NOW(), stream_version = @newVersion WHERE outbox_id = @outboxId`.
6. `COMMIT`.

**Isolation level: `READ COMMITTED`** (cycle-3 review fix). The publisher tx must NOT use `RepeatableRead` because that isolation level fixes the snapshot at `BEGIN` — meaning after `SELECT ... FOR UPDATE` waits for a concurrent publisher's commit, the subsequent `MAX(stream_version)` read still observes the pre-commit snapshot and picks the same next version as the prior publisher, producing 23505 on the INSERT. Under `READ COMMITTED`, each SELECT statement gets a fresh snapshot of the latest committed data, so the post-lock-wait `MAX` correctly observes the prior publisher's just-committed row. The publisher does not require consistent multi-statement reads — it requires "see latest committed data after lock acquisition," which is exactly what `READ COMMITTED` provides.

Note that this is the OPPOSITE choice from the state-change side: the state-change tx (in `ConfigEndpoints` PUT and similar handlers) uses `RepeatableRead` because it DOES need consistent reads of the profile row across the optimistic-concurrency check, the close-then-insert step, and the audit row insert. The two transactions have different consistency requirements; the publisher's lighter requirement justifies the lighter isolation level.

If the publisher crashes between step 4 and step 5, restart re-attempts the row. Step 4's INSERT fails with `23505` on either the `(stream_id, stream_version)` UNIQUE or the `event_id` UNIQUE — both are constraints on the events table. The recovery branch uses `event_id` lookup to determine which case fired:

```csharp
async Task PublishAsync(OutboxRow row, CancellationToken ct)
{
    await using var conn = _connectionFactory.Create();
    await conn.OpenAsync(ct);
    // READ COMMITTED — see comment above. The publisher's correctness invariant is
    // "see latest committed data after FOR UPDATE wait completes," which RepeatableRead
    // does NOT provide because its snapshot is frozen at BEGIN.
    await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
    try
    {
        await EnsureStreamRowAsync(conn, tx, row.StreamId, ct);  // INSERT ... ON CONFLICT DO NOTHING (unconditional; idempotent)
        await AcquireStreamLockAsync(conn, tx, row.StreamId, ct); // SELECT 1 ... FOR UPDATE
        var version = await ComputeNextStreamVersionAsync(conn, tx, row.StreamId, ct); // MAX from events — fresh snapshot under READ COMMITTED

        try
        {
            await InsertEventAsync(conn, tx, row, version, ct); // event_id from row.EventId
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // Recovery: lookup-by-event_id determines whether THIS outbox row's event
            // is already in the canonical store (crash-during-publish, idempotent retry)
            // OR a different outbox row collided on stream_version (impossible under
            // step-2's FOR UPDATE serialization, but defensive against future code that
            // bypasses the lock). The event_id UNIQUE constraint catches the former
            // case; the (stream_id, stream_version) UNIQUE constraint catches the
            // latter. We distinguish by querying the events row by event_id.
            var existingVersion = await TryGetExistingEventVersionAsync(
                conn, tx, row.EventId, ct);
            if (existingVersion is null)
            {
                // Different outbox row won the version slot — do NOT mark published.
                // Roll back, increment attempts, surface to operator dashboard.
                throw new PublisherCorrelationException(
                    $"23505 on stream {row.StreamId} for outbox {row.OutboxId} but " +
                    $"event_id {row.EventId} not found in events table; another writer " +
                    $"won the version slot. Manual reconcile required.", ex);
            }
            version = existingVersion.Value;
        }

        await MarkPublishedAsync(conn, tx, row.OutboxId, version, ct);
        await tx.CommitAsync(ct);
    }
    catch
    {
        await tx.RollbackAsync(ct);
        await IncrementAttemptsAsync(row.OutboxId, ct);
        throw;
    }
}
```

Q5 option (a) — outbox row carries `stream_version` at enqueue time — was the cycle-1-cycle-2 antipattern from S21. Listed in the sprint plan only for forward-readability; rejected here with the explicit rationale that in-tx `MAX(stream_version)` reads from the caller's `RepeatableRead` snapshot, producing UNIQUE violations on concurrent saves to the same stream. The publisher-time path above avoids this because the publisher's tx is self-contained (its own `BEGIN`) and uses an explicit FOR UPDATE on the parent stream row to serialize.

**Why `event_id` correlation works** (cycle-2 review B1/C2 fix): D6 codifies that each `stream_id` has exactly one writer service, AND that service's per-service publisher polls only its own outbox partition. So the only way a `(stream_id, stream_version)` slot can be taken by an event with a different `event_id` is if a DIFFERENT outbox row won — which means the current row should NOT be marked published. The `PublisherCorrelationException` surfaces this case loudly for manual reconciliation rather than silently marking the wrong row published. Under normal operation (no concurrent publishers, FOR UPDATE serialization holding), this branch is unreachable — but defending against it makes the protocol auditable.

### D5 — Per-stream FIFO ordering (Q6)

Per-stream FIFO is required (events on the same stream represent ordered state transitions; consumers depend on this order). Cross-stream ordering is NOT required (independent aggregates).

The publisher's poll query orders by `outbox_id ASC` (= enqueue order, BIGSERIAL). For per-stream FIFO, the publisher groups by `stream_id` and publishes within-stream rows sequentially. Cross-stream rows can publish concurrently up to the publisher's parallelism setting (default 4 concurrent streams).

```csharp
// Pseudocode for the publisher loop.
var batch = await ReadOutboxAsync(serviceId, batchSize: 100, ct);
var byStream = batch.GroupBy(r => r.StreamId);
await Parallel.ForEachAsync(
    byStream,
    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
    async (group, innerCt) =>
    {
        foreach (var row in group.OrderBy(r => r.OutboxId))
        {
            await PublishAsync(row, innerCt);
        }
    });
```

### D6 — Cross-service stream isolation invariant (Q2 + Q6)

ADR-018 establishes the invariant: **each `stream_id` is owned by exactly one service.** No cross-service writers on the same stream. Stream-ownership table (cycle-2 review W5 — Orchestrator added explicitly):

| Service | Streams owned (writes events to) |
|---------|----------------------------------|
| Backend.Api | `local-agreement-profile-{orgId}-{agreementCode}-{okVersion}` (ConfigEndpoints), `org-{orgId}` (AdminEndpoints), `user-{userId}` (AdminEndpoints — includes role-assignment events; role grants/revokes target the user's stream rather than a separate `role-*` stream), `agreement-config-{configId}` (AgreementConfigEndpoints), `position-override-{overrideId}` (PositionOverrideEndpoints), `wage-type-mapping-{agreementCode}-{okVersion}-{timeType}` (WageTypeMappingEndpoints), `approval-{employeeId}-{periodStart:yyyy-MM-dd}` (ApprovalEndpoints), `overtime-preapproval-{id}` (OvertimeEndpoints — pre-approval lifecycle), `overtime-balance-{employeeId}-{periodYear}` (OvertimeEndpoints — compensation/balance mutations), `timer-{employeeId}` (TimerEndpoints — check-in/out events), `employee-{employeeId}` (consolidated stream carrying TimeEntryRegistered + AbsenceRegistered + EntitlementBalanceAdjusted + compliance events from BalanceEndpoints / ComplianceEndpoints / SkemaEndpoints / TimeEndpoints — same employee scope, FIFO per employee preserved by single stream id) |
| Payroll | `period-calculation-*`, `payroll-export-*` (plus retroactive correction streams emitted from `RetroactiveCorrectionService`) |
| External | `integration-delivery-*` |
| Orchestrator | **MAY NOT write any stream.** Orchestrator coordinates rule-engine HTTP calls and dispatches tasks; it does not append domain events. If a future task type requires Orchestrator-emitted events, ADR-018's invariant requires re-architecting the outbox topology BEFORE adding the new event type (either move the emission to Backend.Api / Payroll, or introduce a global publisher per Q2 option (c)). |

**S26 / TASK-2601 cycle-6 amendment (2026-05-08, Phase 4c.5):** the original D6 stream-ownership table listed `role-*`, `entitlement-*`, `period-approval-*`, `overtime-pre-approval-*`, `compensatory-rest-*`, `timer-session-*`, `time-entry-*`, `skema-*`, `project-*` — a mix of (a) names that drifted vs the actual code emission and (b) names that were never emitted (forward-projection placeholders). Since stream_ids are first-write-wins and renaming retroactively would break replay determinism per ADR-016 D10 (every existing event in the stream would become unreachable under the new name), the canonical resolution is **doc matches code**, not the reverse. The amended table above reflects every `stream_id` literal grep'd from `src/` as of S26 sprint open (master `4c84b3c`); names that were forward-projected but never emitted (e.g. `role-*` standalone, `compensatory-rest-*`, `project-*`) are dropped — when a future feature needs them, it adds them then. The consolidated `employee-{employeeId}` stream replaces the originally-projected separate `time-entry-*` / `skema-*` / `entitlement-*` streams; consolidation was the de-facto choice from S2/S9/S15 Backend.Api implementations and is explicitly preserved here for FIFO-per-employee.

If a future feature requires cross-service writes to the same stream, it MUST first introduce a global publisher (Q2 option (c)) — the per-service topology cannot guarantee FIFO across service boundaries. This invariant is enforced by code review; no structural enforcement in the schema. Recommended code-review checklist item: any new endpoint or service that calls `IEventStore.EnqueueAsync` is approved only after the new stream pattern is added to the table above.

### D7 — Row-version column on `local_agreement_profiles` (Q7)

Schema migration adds a `version` column:

```sql
ALTER TABLE local_agreement_profiles
ADD COLUMN version BIGINT NOT NULL DEFAULT 1;
```

Existing rows get `version = 1` by the DEFAULT. Subsequent UPDATEs bump via app-side increment (not a trigger): `UPDATE local_agreement_profiles SET version = version + 1, ... WHERE profile_id = @id AND version = @expectedVersion`.

The optimistic-concurrency check happens **once, in `ValidatePrecondition`** (after `AcquireLockAsync` returns the locked row's current version). Once that check passes, the existing `SELECT ... FOR UPDATE` row-lock under `RepeatableRead` ensures no other writer can change the row before the UPDATE fires. The `WHERE version = @expectedVersion` clause on the UPDATE is therefore **defense-in-depth**, not the load-bearing check (cycle-2 review W3 clarification): it guards against a future code path that bypasses the lock (e.g., a hypothetical bulk-update endpoint that doesn't go through `AcquireLockAsync`) but is logically redundant under the current repository contract. We keep it because (a) the cost is negligible, (b) it makes the SQL self-documenting about the optimistic invariant, and (c) test coverage for "the UPDATE without FOR UPDATE" path becomes a one-line test addition rather than a multi-method refactor if the contract ever loosens.

App-side increment (not trigger) rationale:
- Repository code is the single source of truth for the bump; testable via unit tests.
- Trigger-based bump is invisible from C# code, hard to assert correctly in tests, and obscures the optimistic-concurrency contract.
- The increment is a single line in `LocalAgreementProfileRepository.UpdateInPlaceAsync`; complexity is minimal.

ETag header value becomes the quoted version per RFC 7232: `ETag: "<version>"` (e.g., the literal wire bytes `ETag: "5"`, with quotes). The frontend's `If-Match: "5"` header asserts "I read at version 5; only proceed if that's still current." Both ETag (response) and If-Match (request) carry the quoted form on the wire; the frontend's `parseVersionFromETag` / `formatVersionAsIfMatch` helpers (see "Implications → For Frontend") translate between the wire format and the in-memory numeric `version`. This replaces ADR-017 D2.1's `If-Match: <profile_id>` shape — `profile_id` was an immutable identifier per row, so ETag-via-profile_id worked only for the close-then-insert path; UPDATE-in-place needs a mutable token.

### D8 — End-exclusive `effective_to` semantics + migration (Q8)

ADR-018 amends ADR-017 D2: `effective_to` becomes end-exclusive. A row with `effective_from = X, effective_to = Y` is active for `[X, Y)` (inclusive lower, exclusive upper). Predecessor close becomes:

```csharp
// Pre-S22 (end-inclusive, off-by-one negative-range bug):
predecessor.effective_to = newProfile.EffectiveFrom.AddDays(-1);

// Post-S22 (end-exclusive, no -1):
predecessor.effective_to = newProfile.EffectiveFrom;
```

Migration converts existing closed rows in the same DDL block as the row-version column addition. Critically, the `+1 day` shift is **NOT idempotent** by itself — a second run would shift twice and corrupt the data. The migration is gated by a `schema_migrations` ledger table (cycle-2 review B2 fix) so that re-running `init.sql` (e.g., on `docker compose down -v && docker compose up`) does not double-shift:

```sql
-- Schema-migrations ledger (additive, idempotent self-creation).
-- Records which one-shot migrations have already run against this database.
CREATE TABLE IF NOT EXISTS schema_migrations (
    migration_id  TEXT         PRIMARY KEY,
    applied_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    notes         TEXT         NULL
);

-- D7 + D8 + D9 schema migration. Wrapped in DO block + INSERT ... ON CONFLICT
-- guard so the +1-day UPDATE only runs the first time. Idempotent on re-run.
DO $$
BEGIN
    -- Try to claim the migration. If it's already applied, INSERT does nothing
    -- and we exit early.
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s22-d7-d8-d9', 'ADR-018: row-version + end-exclusive + MODIFIED audit action')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        -- Already applied; nothing to do.
        RETURN;
    END IF;

    -- D7: add version column. Existing rows get DEFAULT 1.
    ALTER TABLE local_agreement_profiles
    ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

    -- D8: convert end-inclusive effective_to to end-exclusive (+1 day shift).
    -- A row that was active January through March (effective_to = '2026-03-31')
    -- becomes [2026-01-01, 2026-04-01) (effective_to = '2026-04-01'). Open
    -- rows (effective_to IS NULL) stay NULL.
    UPDATE local_agreement_profiles
    SET effective_to = effective_to + INTERVAL '1 day'
    WHERE effective_to IS NOT NULL;

    -- D9: extend audit-action enum to include MODIFIED for in-place edits.
    ALTER TABLE local_agreement_profile_audit
    DROP CONSTRAINT IF EXISTS local_agreement_profile_audit_action_check;

    ALTER TABLE local_agreement_profile_audit
    ADD CONSTRAINT local_agreement_profile_audit_action_check
    CHECK (action IN ('CREATED', 'MODIFIED', 'SUPERSEDED', 'DEACTIVATED', 'MIGRATED_FROM_LEGACY'));
END
$$;
```

The `INSERT ... ON CONFLICT DO NOTHING` + `IF NOT FOUND THEN RETURN` pattern is PostgreSQL's idiomatic one-shot guard: the first run claims the migration_id and proceeds; subsequent runs see the row already present, the INSERT writes nothing, `FOUND` is false, the migration is skipped. Open rows (`effective_to IS NULL`) need no conversion — open is open under either convention. The version backfill is the DEFAULT 1, applied automatically by the ALTER TABLE.

Future S22+ DDL migrations follow the same pattern with new `migration_id` values (e.g., `s23-d2-2-row-version-on-agreement-configs`).

**Ordering invariant** (cycle-3 review N-3): the `CREATE TABLE IF NOT EXISTS schema_migrations` MUST appear before any guarded `DO $$ ... $$` block in `init.sql`. The `IF NOT EXISTS` makes self-creation idempotent in any ordering, but a guarded block that runs before the ledger exists would fail with "relation does not exist." Tasks decomposing this DDL into deliverable #2's migration plan place the ledger CREATE at the top of the schema migration block.

### D9 — Automatic in-place edit detection + `MODIFIED` audit action (Q9)

`LocalAgreementProfileRepository.SupersedeAndCreateAsync` detects same-day saves automatically and routes to UPDATE-in-place:

```csharp
public async Task<(Guid ProfileId, long Version)> SupersedeAndCreateAsync(
    NpgsqlConnection conn, NpgsqlTransaction tx,
    long? expectedCurrentVersion,
    LocalAgreementProfile newProfile,
    CancellationToken ct = default)
{
    var current = await AcquireLockAsync(conn, tx, ...);
    ValidatePrecondition(current?.Version, expectedCurrentVersion);

    if (current is { } predecessor)
    {
        if (newProfile.EffectiveFrom < predecessor.EffectiveFrom)
        {
            throw new InvalidProfileSupersessionException(...);
        }

        if (newProfile.EffectiveFrom == predecessor.EffectiveFrom)
        {
            // Same-day save → UPDATE in place, bump version. profile_id stable.
            var newVersion = await UpdateInPlaceAsync(conn, tx, predecessor.ProfileId, newProfile, predecessor.Version, ct);
            return (predecessor.ProfileId, newVersion);
        }

        // Supersession (newProfile.EffectiveFrom > predecessor.EffectiveFrom):
        //   close predecessor at end-exclusive newProfile.EffectiveFrom.
        await CloseProfileAsync(conn, tx, predecessor.ProfileId, newProfile.EffectiveFrom, ct);
    }

    var newProfileId = newProfile.ProfileId == Guid.Empty ? Guid.NewGuid() : newProfile.ProfileId;
    await InsertProfileAsync(conn, tx, newProfile, newProfileId, version: 1, ct);
    return (newProfileId, Version: 1);
}
```

The PUT handler emits the audit row with `action`:
- `CREATED` — first profile for the (org, agreement, OkVersion).
- `MODIFIED` — same `effective_from` as predecessor; UPDATE-in-place (Step-7a-D2 path).
- `SUPERSEDED` — new `effective_from > predecessor.EffectiveFrom`; close-then-insert.

The audit-action enum CHECK constraint is extended in the same DDL block as the schema migration (see D8). Pre-S22 rows have no `MODIFIED` audit history; the post-S22 distinction begins at the migration commit.

### D10 — Read-site predicate updates under end-exclusive (cycle 1 R-N4)

Every read-site that filters on `effective_to` is enumerated and audited for the predicate change:

| Site | Pre-S22 predicate | Post-S22 predicate | Reason |
|------|------------------|-------------------|--------|
| `LocalAgreementProfileRepository.GetCurrentOpenAsync` | `effective_to IS NULL` | `effective_to IS NULL` (unchanged) | Open is open under either convention. |
| `LocalAgreementProfileRepository.GetActivationsInPeriodAsync` | `effective_to IS NULL OR effective_to >= @periodStart` | `effective_to IS NULL OR effective_to > @periodStart` | A row with end-exclusive `effective_to = periodStart` ended *before* periodStart; it does NOT overlap the period. |
| `LocalAgreementProfileRepository.GetHistoryAsync` | `effective_to IS NOT NULL` | `effective_to IS NOT NULL` (unchanged) | Closed-vs-open test, not a date comparison. |
| `ConfigResolutionService.ResolveAsync` (profile lookup branch) | calls `GetCurrentOpenAsync` | unchanged | Indirect via the repo. |
| `BuildPlanForLegacyCallers` D9c (PCS) | calls `GetActivationsInPeriodAsync` | propagates the predicate change | Indirect via the repo. |

`LocalAgreementProfileMigrator` (S21) reads the legacy `local_configurations` table, NOT `local_agreement_profiles`, so the end-exclusive change does not propagate there — the migrator's predicates are not affected by ADR-018 (cycle-2 review N5).

No production code outside the repository directly compares `effective_to` to a date — the predicate update is localized to the repository's three filter sites + the migrator's snapshot read.

### D11 — Replay-determinism proof for pre-S22 `SegmentManifest`s

Pre-S22 `SegmentManifest`s are replayed by `PCS.ReplayAsync(manifestId)` to reproduce historical calculations. ADR-016 D5b establishes that local-config (post-S21: profile data) is NOT in the snapshot-at-calculation set — manifests do NOT carry profile snapshots; they carry boundary dates and per-segment `SegmentSnapshot` values frozen at original calculation time.

Replay's interaction with end-exclusive migration (cycle-2 review W2 mechanism correction):

1. Manifest payload contains `LocalProfileActivations: [(date, profileId)]` (ADR-017 D9b) plus `SegmentSnapshot` values per segment (ADR-016 D5b).
2. `PCS.ReplayAsync(manifestId)` calls `PeriodPlanner.FromManifest(manifest, ruleSet)` (`PeriodPlanner.cs:178`) which **reuses `manifest.Segments` directly** — the planner does NOT re-walk `BoundarySources` against the live database; segment boundaries are reconstructed from manifest data.
3. Rule evaluation runs `CalculateWithOutcomeAsync` against the caller-supplied `EmploymentProfile` and the manifest's per-segment `SegmentSnapshot` values. **`local_agreement_profiles` is never re-read on the replay path.**
4. Therefore, post-S22 schema changes to `local_agreement_profiles` (the `+1 day` shift on `effective_to`, the new `version` column) cannot influence replay output — the replay path simply doesn't touch the migrated table.

This is a stronger guarantee than the original cycle-1 framing claimed: replay is invariant under migration NOT because "the live row's post-migration state happens to match the post-migration read predicates," but because the replay path is structurally decoupled from the live row entirely. The `manifest.Segments` snapshot is the canonical input.

**Test guarantee** (D12 below): regression test that pre-S22 manifests replay byte-identically post-migration. Floor: 1 dedicated test per the matrix. The test seeds a pre-S22-shape profile (end-inclusive `effective_to`), creates a manifest using S21-era `BuildPlanForLegacyCallers`, runs the migration, then replays the manifest and asserts byte-identical output — the assertion holds because the replay never reads the migrated profile row.

### D12 — Test strategy: committed minimum matrix (Q12)

Matching ADR-016 D11 + ADR-017 D11 format. Categories pre-committed as IN; scenario depth committed per category. Total floor: **15 new tests**.

| Category | Scenarios | Floor | File |
|----------|-----------|-------|------|
| Outbox enqueue + publish | Happy path; publisher restart resumes; per-stream FIFO; concurrent cross-stream parallelism; **rolled-back state-change → no outbox row → no publish** | 5 | `tests/StatsTid.Tests.Regression/Outbox/OutboxPublisherTests.cs` |
| Profile row-version + end-exclusive | In-place UPDATE bumps version; concurrent in-place 412 (asserts outbox row count delta = 0 from the failed save, cycle-2 N3); same-day routes to UPDATE; end-exclusive predecessor close; **same-day SUPERSESSION followed by in-place MODIFICATION on the new active row** (cycle-2 W4) — admin creates profile with effective_from = today, then immediately changes a value without re-touching effective_from; routing must be SUPERSEDED then MODIFIED, audit chain must show both | 5 | `tests/StatsTid.Tests.Regression/Config/ProfileRowVersionTests.cs` |
| End-exclusive migration | Closed row converts (`effective_to + 1`); open row stays NULL with version 1; pre-S22 SegmentManifest replays byte-identical | 3 | `tests/StatsTid.Tests.Regression/Config/EndExclusiveMigrationTests.cs` |
| `IEventStore` in-tx overload | In-tx `EnqueueAsync` writes outbox row in caller's tx; rollback removes outbox row; ETag/version backfill on existing profiles returns `1` | 3 | `tests/StatsTid.Tests.Regression/Outbox/EventStoreInTxTests.cs` (+ `tests/StatsTid.Tests.Unit/Outbox/EventStoreInterfaceTests.cs` if needed for unit coverage) |

The "rolled-back state-change → no publish" scenario (cycle 1 Reviewer R-B4) is the inverse-direction guarantee that defines transactional-outbox correctness. Without this scenario, the test matrix only proves "things that committed get published" and not "things that didn't commit don't get published" — which is the entire point of the outbox.

The "pre-S22 SegmentManifest replays byte-identical" scenario (cycle 1 Reviewer R-B3) provides the replay-determinism proof for D11. The test seeds a pre-S22-shape profile (end-inclusive `effective_to`), creates a manifest using S21-era `BuildPlanForLegacyCallers`, runs the migration, then replays the manifest and asserts byte-identical output.

**Test floor: 16 (was 15 in cycle 1)** — cycle 2 added the same-day-supersession-then-MODIFICATION scenario per W4. Categories total to (5 outbox) + (5 profile) + (3 migration) + (3 IEventStore) = 16.

**Test fixture update budget** (cycle-2 review W6): the `SupersedeAndCreateAsync` return-type change from `Guid` to `(Guid ProfileId, long Version)` breaks 10 call sites across 4 existing test fixtures (`ProfileSupersessionTests.cs`, `ProfileConcurrencyTokenTests.cs`, `ProfileAuditTests.cs`, `ProfileLegacyEventNonEmissionTests.cs`). Task decomposition (deliverable #3) must include a TASK line item: "destructure tuple at ~10 existing test call sites." Mechanical change; estimated < 30 LOC total.

### D13 — Atomic-outbox migration requires a synchronous projection table for any GET that reads back the just-written state (S27)

Added in S27 / TASK-2711 as the canonical pattern for any future endpoint adopting the ADR-018 D3 atomic-outbox shape. Promoted to a discrete decision (not a D6 footnote) per S27 refinement cycle 2 Reviewer W4 + cycle 3 convergent Codex/Reviewer NOTE.

**Decision**: Atomic-outbox migration on any endpoint X requires a synchronous projection table for any GET that reads back the just-written state. POST writes both the event/outbox AND the projection in the same `(conn, tx)`; GET reads the projection, NOT `events.ReadStreamAsync`.

**Rationale**: Reads from `events` via `IEventStore.ReadStreamAsync` see the publisher-drained snapshot, which lags POST commit by up to ~1s in steady state and unbounded under publisher backpressure (`OutboxPublisher.cs:32-45` — `QuietPollIntervalMs = 1000`, `ActivePollIntervalMs = 250`, `MaxStreamParallelism = 4`). Sync-in-tx projection (POST writes both the event/outbox AND the projection in the same transaction) is the only pattern that preserves read-your-write without serializing on the publisher loop.

**Consequences**:
- Any future endpoint adding atomic-outbox MUST audit GET-side read paths first. If reads currently come from `events.ReadStreamAsync`, a projection table is a prerequisite.
- S22 ConfigEndpoints succeeded only because `local_agreement_profiles` already served as a projection. S26 TASK-2604 (Skema) + TASK-2606 (Time) reverted because Skema/Time had no projection (read-your-write regression detected at Step 7a cycle 1).
- S27 Phase 4c.6 introduced `time_entries_projection` + `absences_projection` as the missing prerequisite, then re-attempted the atomic-outbox migration on Skema/Time POSTs (TASK-2706/2707) with the projection layer in place.
- Per-event ordering inside the atomic tx is pinned: **outbox enqueue FIRST** (via `IOutboxEnqueue.EnqueueAndReturnIdAsync` returning `outbox_id`), **projection INSERT SECOND** (consuming the returned `outbox_id` for the projection's ordering column). Both inside the same tx; both roll back together on crash.
- Projection ordering column: `outbox_id BIGINT NOT NULL` sourced from `outbox_events.outbox_id BIGSERIAL`. NOT `stream_version` — that's publisher-assigned at drain time (`OutboxPublisher.ComputeNextStreamVersionAsync` at L368-372) and unavailable at write commit.
- Backfill for existing pre-projection events: one-shot ops script (`tools/ProjectionBackfill`, S27 TASK-2705) AND auto-invoked at Backend.Api startup via `ProjectionBackfillService.RunAsync` (S27 Step 7a P1 #1 fix). Reads `events JOIN outbox_events`, INSERTs into projection with `ON CONFLICT (event_id) DO NOTHING`. Idempotent re-runs. Single SQL source of truth in `src/Infrastructure/StatsTid.Infrastructure/ProjectionBackfillService.cs`.

**Known limitation — pre-S22 ordering** (deferred to Phase 4e per S27 Step 7a cycle 1 P1 #2): the backfill's `stream_version` fallback for pre-S22 events (events with NO matching `outbox_events` row) writes a per-stream-monotonic value into the global-per-service `outbox_id` column. Subsequent reads `ORDER BY outbox_id ASC` may interleave pre-S22 and post-S22 events out of true chronological order — pre-S22 `stream_version=50` could sort BEFORE post-S22 `outbox_id=10`, producing a wrong sequence. **Does not fire under S27's pre-launch posture** (refinement Assumption #1: no production data; ALL events post-deploy are post-S22 with real outbox rows). **Will fire** if the system ever deploys to an environment with pre-existing pre-S22 event-store data. Phase 4e (production-readiness) will adjudicate the proper fix — likely a composite ordering scheme bridging the S22 boundary (e.g., add a `replay_seq BIGSERIAL` column populated at backfill time using global event ordering, or fall back to `(events.stored_at, stream_version)` as a tuple sort key).
- Read-your-write proof: D-test stops the `OutboxPublisher` BackgroundService via `IHostedService.StopAsync(CancellationToken.None)` (NOT `Task.Delay`, NOT a config flag — see S27 TASK-2701 `StatsTidWebApplicationFactory.StopPublisherAsync`), POSTs, then immediately GETs. Asserts: GET sees the write + `events` table empty + unpublished `outbox_events` row exists + projection.outbox_id matches outbox row.outbox_id.

**Carve-outs preserved in S27** (per refinement Assumption #4): not every event-stream-backed read needs a projection. Reads that don't participate in a read-your-write loop (e.g., `BalanceEndpoints.cs:102` reading `FlexBalanceUpdated` for a passive flex-balance display) may stay on `events.ReadStreamAsync`. The grep-zero-hits AC for D13 compliance is scoped to migrated handler bodies, not entire endpoint files.

**Open** (deferred to Phase 4d-3 refinement): whether projections may carry projection-only enrichment fields, or must remain a strict view of event payload — adjudicated when Phase 4d-3 (employee-profile versioned history) makes the constraint concrete. S27 leaves D13 as a discrete decision on the sync-in-tx pattern; the projection-only-fields rule is genuinely 4d-3 territory.

## Rationale

**Why outbox?** The post-commit-append shape S21 cycle 2 settled on is functionally correct under normal operation but leaves a residual partial-failure window that grows operationally (more emit sites = more crash-windows = more divergence opportunities). The transactional outbox pattern is the canonical industry solution for this exact problem (Hohpe + Woolf 2003, Kleppmann 2017). PostgreSQL's MVCC + UNIQUE constraints make it cheap to implement correctly; no message broker required.

**Why per-service publisher?** Backend.Api can't deserialize event types it doesn't reference (ADR-002 isolation). Each service owns its event types; each service publishes its own outbox. The trade-off is N polling loops instead of 1, but the polling cost is trivial (250ms intervals against a partial index on `WHERE published_at IS NULL` is microseconds).

**Why row-version (not XMIN, not optimistic-skipping)?** PostgreSQL's `xmin` system column works for optimistic concurrency without an explicit version column, but it's opaque to application code (you can't write `WHERE xmin = @expected` cleanly across all PG versions; the value changes on VACUUM operations in some configurations). An explicit BIGINT column is self-documenting, testable, and portable. The cost is one column per row.

**Why end-exclusive `effective_to`?** It eliminates the off-by-one arithmetic entirely. Same-day saves no longer produce negative-range rows under any path. The migration is a single `+1 day` UPDATE on closed rows; open rows are unaffected. The convention also matches PostgreSQL's `tsrange` and ISO 8601 interval semantics — a future migration to `daterange` (Phase 5+) would land cleanly.

**Why ship D6 + Step-7a-D2 together?** The row-version column needs the in-tx outbox path to atomically record `LocalAgreementProfileChanged` events for in-place edits. Without the outbox, in-place edits would still need post-commit append, leaving the same partial-failure window the outbox is designed to close — a half-fix.

**Why split D2.2 to S23?** `agreement_configs` has three distinct race conditions (DRAFT in-place / DRAFT→ACTIVE publish / ACTIVE→ARCHIVED clone) that the row-version pattern alone doesn't address; threading those through ADR-018 would more than double the ADR's scope. Sequencing gives the row-version pattern a published exemplar; S23 is largely mechanical replication once the exemplar is in place.

## Implications

### For SharedKernel
- `IEventStore` is **unchanged** (cycle-6 amendment). No new methods; no new package references. SharedKernel stays Npgsql-free, preserving the post-S19 `b4fc670` assembly-graph invariant that keeps `StatsTid.RuleEngine.Api` Npgsql-free transitively.

### For Infrastructure
- New interface `StatsTid.Infrastructure.Outbox.IOutboxEnqueue` with single `EnqueueAsync(NpgsqlConnection, NpgsqlTransaction, ...)` method.
- `PostgresEventStore` implements **both** `IEventStore` (existing) and `IOutboxEnqueue` (new). The in-tx `EnqueueAsync` writes to `outbox_events`; the self-contained `AppendAsync` writes to `events` + `event_streams` (publisher-only post-S22).
- DI registration: register the concrete `PostgresEventStore` once, then expose it under both interface contracts (see D3).
- New `OutboxPublisher : BackgroundService` per service. Polls its `service_id` partition; publishes in per-stream FIFO order with at-least-once semantics.
- `LocalAgreementProfileRepository` gains:
  - `Version` field on the model + read mappings.
  - `SupersedeAndCreateAsync` returns `(Guid ProfileId, long Version)` instead of just `Guid`.
  - `UpdateInPlaceAsync` private helper.
  - `AcquireLockAsync` returns `(Guid ProfileId, long Version, DateOnly EffectiveFrom)` (or null).
  - `OptimisticConcurrencyException` reshaped to carry expected/actual versions instead of profile_ids.
  - `InvalidProfileSupersessionException` re-introduced (cycle 3-9 of S21 Step 7a removed it; D9's strict-less-than guard re-introduces it).
- New exceptions in `StatsTid.Infrastructure.Outbox` namespace:
  - `PublisherCorrelationException` — thrown by the publisher when 23505 fires on `events` INSERT but the existing row's `event_id` does not match the outbox row's `event_id`. Surfaces as a manual-reconcile alert (operator dashboard) rather than a silent skip. Cycle-3 review N-1 fix.

### For Backend.Api
- `ConfigEndpoints.MapPut` flow:
  - Parse `If-Match: <version>` from header (was `If-Match: <profile_id>` pre-S22).
  - Build candidate profile, validate alignment.
  - Call `EnqueueAsync` for `LocalAgreementProfileChanged` event in-tx (replaces post-commit `AppendAsync`).
  - Call `SupersedeAndCreateAsync` returning `(profileId, newVersion)`.
  - Set `ETag: "<newVersion>"` on response.
  - Audit-action `MODIFIED` for in-place edits.
- All 12 state-change-emitting endpoint sites (across `ConfigEndpoints`, `AdminEndpoints`, `AgreementConfigEndpoints`, `PositionOverrideEndpoints`, `WageTypeMappingEndpoints`, `ApprovalEndpoints`, `OvertimeEndpoints`, `ComplianceEndpoints`, `SkemaEndpoints`, `TimerEndpoints`, `TimeEndpoints`, plus `Payroll` + `External` integrations) swap their event-emit line. They also switch DI parameter from `IEventStore` to `IOutboxEnqueue`:
```csharp
// Pre-S22:
async (..., IEventStore eventStore, ...) => {
    await tx.CommitAsync(ct);
    await eventStore.AppendAsync(streamId, @event, ct);
}

// Post-S22:
async (..., IOutboxEnqueue outbox, ...) => {
    await outbox.EnqueueAsync(conn, tx, streamId, @event, ct);
    await tx.CommitAsync(ct);
}
```

### For Frontend
- `useConfig.ts` ETag handling: parse `ETag: "<number>"` header on GET responses; send `If-Match: "<number>"` on PUT. Per HTTP/RFC 7232, ETag values are quoted opaque strings — even when the value happens to be numeric, the response header is literally `ETag: "5"` (quotes included). The frontend MUST strip quotes before treating the value numerically (cycle-2 review C3 fix):

```typescript
// In useConfig.ts (or shared etag helper)
function parseVersionFromETag(etag: string | null): number | null {
  if (!etag) return null
  // Strip surrounding double-quotes per RFC 7232; W/-prefixed weak ETags are
  // not used by S22 (we emit only strong ETags) but stripped defensively.
  const unquoted = etag.replace(/^W\//i, '').replace(/^"|"$/g, '')
  const n = parseInt(unquoted, 10)
  return Number.isNaN(n) ? null : n
}

function formatVersionAsIfMatch(version: number): string {
  return `"${version}"`
}
```

The ETag is treated as an opaque token end-to-end on the wire (quoted); the numeric `version` is the in-memory representation in TypeScript and the database column. The mismatch happens only at the HTTP boundary.

- 412 response handling unchanged from S21.

### For migration
- DDL migration (init.sql + production migration script): add `version` column with DEFAULT 1, convert `effective_to + 1 day` for closed rows, extend audit-action CHECK constraint, create `outbox_events` table + indexes.
- Pre-S22 events stay readable. Pre-S22 audit rows stay queryable. Pre-S22 profile rows convert to end-exclusive in the same migration; their version starts at 1.

### For Phase 4 ROADMAP
- **S23 (D2.2 propagation)** consumes ADR-018's row-version pattern; ADR-019 covers shared-helper-vs-replication + agreement_configs three-race resolution.
- **Phase 4 X-1/X-2/X-3 (Versioned History sub-sprints)** unchanged — those sprints are about effective-dated history lookup for non-dated boundary sources, orthogonal to S22's outbox + row-version concerns.

## Alternatives Rejected

For each open question that produced a closed decision, the rejected options:

- **Q1 (outbox shape):** separate event-store DB rejected — requires 2-PC across stores; over-engineered.
- **Q2 (publisher topology):** option (a) single in-process publisher rejected — Backend.Api can't deserialize cross-service event types. Option (c) dedicated worker service rejected — operational footprint not justified pre-launch.
- **Q3 (`IEventStore` evolution):** option (a) `IOutboxEnqueue` was the cycle-6 chosen path (cycle-1 rejected it citing "doubles the type taxonomy"; cycle-6 reversed that on the assembly-graph evidence — `IEventStore` in SharedKernel cannot take Npgsql-typed parameters without leaking Npgsql to RuleEngine.Api). Option (c) `IEventEmitter` remains rejected — adds a third name without further benefit over (a). Option (b) "single interface, two overloads on `IEventStore`" rejected for the assembly-graph reason recorded in D3.
- **Q5 (at-least-once idempotency):** option (a) outbox row carries `stream_version` at enqueue rejected — reproduces S21 cycle-2 MVCC snapshot conflict. Option (c) per-stream sequencer table rejected — mechanically equivalent to (b) without behavior gain.
- **Q7 (row-version bump):** trigger-based bump rejected — invisible from C# code, hard to assert correctly in tests, obscures the optimistic-concurrency contract. PostgreSQL `xmin` rejected — opaque, version-dependent, changes on VACUUM in some configurations.
- **Q9 (in-place routing):** explicit `EditMode.InPlace` / `EditMode.Supersede` parameter on the repo rejected — the repo can detect from `newProfile.EffectiveFrom` cleanly; explicit mode adds caller burden without disambiguation gain (caller would just compute the same condition).

## Review History

### Cycle 1 (2026-05-03)

Internal Reviewer: 2 BLOCKER + 6 WARNING + 5 NOTE. External Codex: 3 P1 BLOCKER. Convergent on D4's correlation-key gap (Reviewer B1, Codex C2). Codex independently flagged D4's wrong schema reference (C1) and frontend ETag parsing (C3); Reviewer independently flagged migration non-idempotency (B2), D2/D11 wrong rationales (W1/W2), redundant-but-load-bearing-implied D7 WHERE clause (W3), missing same-day-supersession-then-MODIFICATION test scenario (W4), missing Orchestrator from D6 stream-ownership table (W5), test-fixture update budget (W6), and the cross-reference / method-name NOTEs (N1-N5).

### Cycle 2 (2026-05-03) — fixes applied

| Finding | Resolution |
|---------|-----------|
| Reviewer B1 + Codex C2 (correlation-key gap) | Outbox row gains `event_id UUID NOT NULL UNIQUE`; mirrors `events.event_id` (existing UNIQUE constraint per `init.sql:13`). Publisher uses lookup-by-`event_id` on 23505 retry; mismatch surfaces `PublisherCorrelationException` for manual reconcile. No new column on `events` table. |
| Reviewer B2 (migration non-idempotency) | New `schema_migrations` ledger table; the `+1 day` UPDATE wrapped in `DO $$ ... INSERT INTO schema_migrations ON CONFLICT DO NOTHING; IF NOT FOUND THEN RETURN $$` block. Re-runs of `init.sql` are no-ops. |
| Codex C1 (wrong schema for `MAX(stream_version)`) | D4 protocol rewritten to query `MAX(stream_version) FROM events WHERE stream_id = @id`; serialization via `SELECT 1 FROM event_streams WHERE stream_id = @id FOR UPDATE`; `INSERT ... ON CONFLICT DO NOTHING` to ensure parent stream row exists. |
| Codex C3 (ETag parseInt on quoted string) | Frontend section adds `parseVersionFromETag` + `formatVersionAsIfMatch` helpers that strip/add quotes per RFC 7232. ETag stays opaque on the wire; numeric internally. |
| Reviewer W1 (D2 rationale wrong) | "Backend.Api can't deserialize cross-service events" replaced with the actual rationale (operational coupling + D6 stream-ownership). |
| Reviewer W2 (D11 mechanism wrong) | "Replay re-fetches live profile rows" replaced with "replay reuses `manifest.Segments` directly via `PeriodPlanner.FromManifest`; `local_agreement_profiles` is never re-read on the replay path." Stronger guarantee. |
| Reviewer W3 (D7 WHERE clause framing) | Clarified that the optimistic check happens in `ValidatePrecondition` post-`AcquireLockAsync`; the UPDATE's `WHERE version = @expected` is defense-in-depth, not load-bearing. |
| Reviewer W4 (missing test scenario) | D12 Profile category extended from 4 → 5 scenarios; "same-day SUPERSESSION followed by in-place MODIFICATION" added. Total floor 15 → 16. |
| Reviewer W5 (Orchestrator missing from D6) | Stream-ownership table extended to include Orchestrator with explicit "MAY NOT write any stream" entry; future addition gates on outbox-topology re-architecture. |
| Reviewer W6 (test-fixture update budget) | D12 closing paragraph notes ~10 call sites across 4 fixtures need destructuring updates; deliverable #3 (task decomposition) must include a TASK line item. |
| Reviewer N3 (concurrent in-place 412 outbox-rollback assertion) | D12 Profile #2 scenario gains "asserts outbox row count delta = 0 from the failed save." |
| Reviewer N5 (D10 method-name) | Migrator row removed from D10 table (it queries `local_configurations`, not `local_agreement_profiles`); explanatory paragraph added. |

Status flipped DRAFT → ACCEPTED pending cycle-3 verify. Cycle-3 review is mandatory before deliverable #2 (migration plan) + #3 (task decomposition) start.

### Cycle 3 (2026-05-03)

Internal Reviewer: APPROVED — all cycle-1 findings RESOLVED + 3 advisory NOTEs (N-1 PublisherCorrelationException not in Implications; N-2 D4 step-2 prose redundant with C# pseudocode; N-3 schema_migrations ledger ordering invariant). Recommendation: ACCEPTED.

External Codex: 1 NEW P1 BLOCKER — cycle-2's `IsolationLevel.RepeatableRead` choice for the publisher tx is wrong. Under RepeatableRead, the snapshot is fixed at BEGIN, so even after waiting on `SELECT ... FOR UPDATE` the second publisher reads the OLD `MAX(stream_version)` from its frozen snapshot and picks the same next version as the first publisher — reproducing the exact bug ADR-018 was designed to eliminate. The fix is `IsolationLevel.ReadCommitted` (each statement gets a fresh snapshot of latest committed data; post-lock-wait `MAX` correctly observes the prior publisher's row).

### Cycle 4 (2026-05-03) — fixes applied

| Finding | Resolution |
|---------|-----------|
| Codex cycle-3 P1 (RepeatableRead snapshot bug) | Publisher tx uses `IsolationLevel.ReadCommitted`. Added explanatory paragraph contrasting publisher's "see latest committed after lock-wait" requirement with the state-change tx's "consistent multi-statement reads" requirement. The two transactions deliberately use different isolation levels because they have different consistency contracts. |
| Reviewer cycle-3 N-1 (`PublisherCorrelationException` missing from Implications) | Added to Infrastructure implications list under new "exceptions in `StatsTid.Infrastructure.Outbox`" sub-bullet. |
| Reviewer cycle-3 N-2 (D4 step-2 prose redundancy) | Step 2 rewritten to match the C# pseudocode shape: unconditional `INSERT ... ON CONFLICT DO NOTHING` followed by `SELECT 1 ... FOR UPDATE`, with explicit reference to the existing `PostgresEventStore.AppendAsync` pattern. No "first ... then re-issue the FOR UPDATE" check-then-acquire phrasing. |
| Reviewer cycle-3 N-3 (schema_migrations ordering invariant) | Added "Ordering invariant" paragraph to D8 stating the ledger CREATE must precede any guarded `DO $$` block. |

ADR-018 now fully ACCEPTED. No further review cycles needed before deliverables #2 + #3.

### Cycle 5 (2026-05-03)

External Codex re-verify on cycle-4 fixes: 0 BLOCKERs. 1 P2 WARNING — internal inconsistency between D7's unquoted `If-Match: <version>` framing and the cycle-2 frontend section's correct quoted `If-Match: "<version>"` per RFC 7232. Mechanically fixed (D7 prose updated to specify quoted ETag throughout, with cross-reference to the frontend translation helpers). No new BLOCKERs. ADR-018 closed at cycle 5 within the cycle-cap-discipline boundary (4 BLOCKER-fix cycles total: 1 cycle of original BLOCKERs + 1 cycle 4 of Codex P1 fix + 0 BLOCKERs in cycle 5). Internal Reviewer cycle-3 approval still stands (cycle-4 + cycle-5 changes did not alter architectural shape).

### Cycle 6 (2026-05-03) — assembly-graph BLOCKER from TASK-2202 dispatch

The Phase-1 dispatch of TASK-2202 (`IEventStore.EnqueueAsync` overload) surfaced an architectural concern that all five prior review cycles missed: adding `NpgsqlConnection` / `NpgsqlTransaction` parameters to `IEventStore` (in `StatsTid.SharedKernel.Interfaces`) requires `<PackageReference Include="Npgsql" />` on `StatsTid.SharedKernel.csproj`, which transitively reaches `StatsTid.RuleEngine.Api` via its `<ProjectReference>` to SharedKernel. The post-S19 `b4fc670` cleanup deliberately extracted `StatsTid.Auth` to keep RuleEngine.Api Npgsql-free; the cycle-1-through-5 D3 framing regresses that.

Cycle-6 fix:
- D3 amended from "single interface, two overloads on `IEventStore`" to "split: `IEventStore` (SharedKernel, unchanged) + `IOutboxEnqueue` (Infrastructure, new)."
- `PostgresEventStore` implements both interfaces; DI registers the concrete once and exposes it under both contracts.
- State-change sites inject `IOutboxEnqueue` (NOT `IEventStore`).
- Q3 alternative (a) — originally rejected as "doubles type taxonomy" — promoted to chosen path with cycle-6 rationale captured in "Alternatives Rejected".
- Implications updated: SharedKernel "unchanged"; Infrastructure gets the new interface + dual-impl + dual-DI pattern.

The TASK-2202 worktree (`agent-a4d1870e0faabe412`) is discarded; TASK-2202 will be re-dispatched against the cycle-6 design before Phase 1 completes. TASK-2201 (schema migration) is unaffected by the interface change and stays as-delivered.

Cycle-6 lesson: **agent dispatch is a real review surface.** Five rounds of pre-implementation review (Reviewer + Codex × 2 + verify cycles) did not catch this because the architectural concern is at the package-graph level — neither lens reads .csproj files unprompted. Step 0a's "pattern compliance spot-check" notionally covers this (it greps for `using StatsTid.RuleEngine` from forbidden assemblies) but doesn't audit transitive package references. The ADR's first agent dispatch caught it the way agent reviews are supposed to: by trying to implement and discovering the cost. No prior-cycle BLOCKER was missed-by-laxness — just missed-by-abstraction. Recorded as a feedback memory: pre-implementation reviews of any code that adds package references should explicitly audit transitive impact.

### Cycle 7 (2026-05-09) — D13 added (S27 / TASK-2711)

Additive amendment within the already-accepted ADR family (no Status bump per S27 refinement cycle 3 Codex N1). D13 documents the canonical sync-in-tx projection requirement that S26 TASK-2604 + TASK-2606 violated (reverted at Step 7a cycle 1 because read-your-write broke without a projection table). S27 Phase 4c.6 introduced the missing projection prerequisite (`time_entries_projection` + `absences_projection`) and re-attempted the atomic-outbox migration on Skema/Time POSTs.

D13 promotion from "footnote on D6" → "discrete decision" per S27 refinement cycle 2 Reviewer W4 + cycle 3 convergent Codex/Reviewer NOTE — load-bearing architectural rule for all future endpoint work, not a stream-naming detail.

The D13 "discipline boundary" clause on projection-only enrichment fields was **deferred** to Phase 4d-3 refinement per S27 cycle 3 convergent NOTE (premature for S27 to bind a constraint that hasn't been made concrete by 4d-3's employee-profile versioned-history use case).

S27-era D13 has no test-strategy floor of its own; the sync-in-tx pattern is exercised by S27 TASK-2710's ~12-14 D-tests (publisher-stall RYW × 2, parity-with-drain-sync × 2, Skema bundle-rollback × 1, atomic forced-rollback × 2, atomic quota-breach × 2, backfill idempotency × 1, TxContractTests × 2). The marquee architectural-fix proof is the publisher-stall RYW D-test that fails on the S26-revert baseline and passes post-S27.

## References

- [ADR-001](ADR-001-event-sourcing-postgresql-npgsql.md) — augmented; outbox sits between state-change and the canonical event store.
- [ADR-002](ADR-002-pure-function-rule-engine.md) — preserved; outbox doesn't touch the rule engine.
- [ADR-004](ADR-004-outbox-pattern-guaranteed-delivery.md) — _superseded by this ADR_; approved S1 but never implemented.
- [ADR-016](ADR-016-temporal-period-handling.md) — D5b confirms local-config is NOT in the snapshot-at-calculation set; D10 replay-determinism pattern reused for D11.
- [ADR-017](ADR-017-local-agreement-configuration-as-a-profile.md) — D2 close-then-insert window math (amended to end-exclusive); D2.1 ETag pattern (amended to row-version).
- [SPRINT-22.md](../../sprints/SPRINT-22.md) — sprint log; Q1-Q12 framing + plan-review cycle 1 + Reviewer's (β) split recommendation.
- [SPRINT-23.md](../../sprints/SPRINT-23.md) — sibling sprint for D2.2 propagation.
- [src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs](../../../src/Infrastructure/StatsTid.Infrastructure/PostgresEventStore.cs) — current event-store implementation.
- [src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs](../../../src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs) — pilot ETag/If-Match implementation; row-version + end-exclusive lands here.
- [src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs](../../../src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs) — pilot PUT handler.
- Hohpe + Woolf, _Enterprise Integration Patterns_, Addison-Wesley 2003 — Transactional Outbox pattern (pp. 154–158).
- Kleppmann, _Designing Data-Intensive Applications_, O'Reilly 2017 — at-least-once delivery + idempotent consumers (Ch. 11).
