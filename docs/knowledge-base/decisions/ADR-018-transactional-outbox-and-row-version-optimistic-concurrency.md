# ADR-018: Transactional Outbox + Row-Version Optimistic Concurrency

**Status**: DRAFT (pending Orchestrator approval)
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
    event_type       TEXT         NOT NULL,
    event_payload    JSONB        NOT NULL,
    correlation_id   TEXT         NULL,
    actor_id         TEXT         NULL,
    actor_role       TEXT         NULL,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    published_at     TIMESTAMPTZ  NULL,                -- NULL = unpublished; doubles as consumed marker
    stream_version   BIGINT       NULL,                -- assigned by publisher post-publish; debug join only
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

The `correlation_id`, `actor_id`, `actor_role` columns mirror the audit-context fields the canonical events already carry; capturing them at enqueue time keeps the publisher's INSERT stateless (no need to re-resolve `ActorContext` in the publisher loop).

### D2 — Outbox publisher topology (Q2)

Three options were enumerated at Step 0b cycle 1:
- (a) Single in-process publisher inside Backend.Api.
- (b) Per-service in-process publisher (Backend.Api, Payroll, External, Orchestrator each runs its own).
- (c) Dedicated `StatsTid.OutboxPublisher` worker (new 9th docker service).

**Decision: (b) per-service in-process publisher.** Each service hosts a `BackgroundService`-implementing `OutboxPublisher` that polls `WHERE service_id = @ownServiceId AND published_at IS NULL ORDER BY outbox_id` every poll interval (default 250ms with 1s backoff on quiet polls). Rationale:

- **(a) is wrong** — Backend.Api can only publish events whose payload types it can deserialize; cross-service event types (e.g., `PayrollExportGenerated` from Payroll) would require Backend.Api to depend on Payroll's event assemblies, breaking ADR-002 isolation.
- **(c) adds operational footprint** — a 9th docker service for what is essentially a polling loop is over-engineering for pre-production. Defer to a future sprint if the per-service publisher's latency or contention becomes a measured problem.
- **(b)** keeps each service self-contained: it knows its own event types, its own outbox partition, its own `IEventStore` (the canonical store is shared but the writer is the service-local instance).

Cross-service event ordering: per-stream FIFO is preserved within a single service because stream_ids are service-local in practice (e.g., `local-agreement-profile-{org}-{agreement}-{ok_version}` is only written by Backend.Api). Cross-service streams would require a global publisher, but no such streams exist today; D6 enforces the assumption.

### D3 — `IEventStore` in-tx overload (Q3)

The state-change-emitting `IEventStore.AppendAsync` gains an in-tx overload:

```csharp
public interface IEventStore
{
    // Existing self-contained overload — used by the publisher loop only post-S22.
    Task AppendAsync(string streamId, IDomainEvent @event, CancellationToken ct = default);

    // New in-tx overload — used by state-change sites; writes to outbox_events
    // within the caller's transaction.
    Task EnqueueAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string streamId,
        IDomainEvent @event,
        CancellationToken ct = default);
}
```

Naming: `EnqueueAsync` (not `AppendAsync`) for the in-tx overload to make the call-site difference visible — state-change sites are NOT writing to the canonical event store directly; they're enqueuing for the publisher to forward.

State-change sites swap one line:
```csharp
// Pre-S22 (post-commit, residual crash window):
await tx.CommitAsync(ct);
await eventStore.AppendAsync(streamId, @event, ct);

// Post-S22 (in-tx, atomic with state-change):
await eventStore.EnqueueAsync(conn, tx, streamId, @event, ct);
await tx.CommitAsync(ct);
```

Q3's "evolution to `IEventEmitter`" alternative was rejected — adding a new interface keeps the publisher's post-S22 caller surface stable (the publisher still calls `AppendAsync`) but doubles the type taxonomy admin code must navigate; a single `IEventStore` with two overloads is simpler.

### D4 — At-least-once via publisher-time version assignment (Q5)

The publisher computes `stream_version` at publish time, not at enqueue time. Sequence per outbox row:

1. `BEGIN` (publisher's own self-contained tx).
2. `SELECT MAX(stream_version) FROM event_streams WHERE stream_id = @streamId FOR UPDATE`. The `FOR UPDATE` row-lock serializes concurrent publishers operating on the same stream (only the per-service publisher does this, so contention is minimal in practice).
3. `INSERT INTO events (...) VALUES (...)` with `stream_version = result + 1`.
4. `INSERT INTO event_streams (...)` or `UPDATE event_streams SET last_version = ...`.
5. `UPDATE outbox_events SET published_at = NOW(), stream_version = @assignedVersion WHERE outbox_id = @outboxId`.
6. `COMMIT`.

The events table's `(stream_id, stream_version)` UNIQUE constraint is the natural deduplication mechanism for the at-least-once protocol: if the publisher crashes after step 4 but before step 5, restart re-attempts the row, the events INSERT fails with 23505, the publisher recognizes the duplicate and only updates the outbox row's `published_at`. The publisher's idempotency protocol:

```csharp
async Task PublishAsync(OutboxRow row, CancellationToken ct)
{
    await using var conn = _connectionFactory.Create();
    await conn.OpenAsync(ct);
    await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    try
    {
        var version = await ComputeNextStreamVersionAsync(conn, tx, row.StreamId, ct);
        try
        {
            await InsertEventAsync(conn, tx, row, version, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // Crash-during-publish recovery: event already in canonical store.
            // Look up existing version and proceed to mark outbox published.
            version = await GetExistingStreamVersionAsync(conn, tx, row.StreamId, row.OutboxId, ct);
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

Q5 option (a) — outbox row carries `stream_version` at enqueue time — was the cycle-1-cycle-2 antipattern from S21. Listed in the sprint plan only for forward-readability; rejected here with the explicit rationale that in-tx `MAX(stream_version)` reads from the caller's `RepeatableRead` snapshot, producing UNIQUE violations on concurrent saves to the same stream.

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

ADR-018 establishes the invariant: **each `stream_id` is owned by exactly one service.** No cross-service writers on the same stream. This is true today by construction:

- `local-agreement-profile-*` streams: Backend.Api only.
- `period-calculation-*` streams: Payroll only.
- `payroll-export-*` streams: Payroll only.
- `integration-delivery-*` streams: External only.
- `org-*`, `user-*`, `role-*`, `agreement-config-*`, etc.: Backend.Api only.

If a future feature requires cross-service writes to the same stream, it MUST first introduce a global publisher (Q2 option (c)) — the per-service topology cannot guarantee FIFO across service boundaries. This invariant is enforced by code review; no structural enforcement in the schema.

### D7 — Row-version column on `local_agreement_profiles` (Q7)

Schema migration adds a `version` column:

```sql
ALTER TABLE local_agreement_profiles
ADD COLUMN version BIGINT NOT NULL DEFAULT 1;
```

Existing rows get `version = 1` by the DEFAULT. Subsequent UPDATEs bump via app-side increment (not a trigger): `UPDATE local_agreement_profiles SET version = version + 1, ... WHERE profile_id = @id AND version = @expectedVersion`. The `WHERE version = @expectedVersion` clause provides optimistic concurrency: if two admins both load at version V, both compute the UPDATE expecting `version = V`, only one's UPDATE affects rows (returns 1); the other affects 0 rows and the repository throws `OptimisticConcurrencyException`.

App-side increment (not trigger) rationale:
- Repository code is the single source of truth for the bump; testable via unit tests.
- Trigger-based bump is invisible from C# code, hard to assert correctly in tests, and obscures the optimistic-concurrency contract.
- The increment is a single line in `LocalAgreementProfileRepository.UpdateInPlaceAsync`; complexity is minimal.

ETag header value becomes the version: `ETag: "<version>"` (e.g., `ETag: "5"`). The frontend's `If-Match: 5` header asserts "I read at version 5; only proceed if that's still current." This replaces ADR-017 D2.1's `If-Match: <profile_id>` shape — `profile_id` was an immutable identifier per row, so ETag-via-profile_id worked only for the close-then-insert path; UPDATE-in-place needs a mutable token.

### D8 — End-exclusive `effective_to` semantics + migration (Q8)

ADR-018 amends ADR-017 D2: `effective_to` becomes end-exclusive. A row with `effective_from = X, effective_to = Y` is active for `[X, Y)` (inclusive lower, exclusive upper). Predecessor close becomes:

```csharp
// Pre-S22 (end-inclusive, off-by-one negative-range bug):
predecessor.effective_to = newProfile.EffectiveFrom.AddDays(-1);

// Post-S22 (end-exclusive, no -1):
predecessor.effective_to = newProfile.EffectiveFrom;
```

Migration converts existing closed rows in the same DDL block as the row-version column addition:

```sql
-- D7 + D8 schema migration: add version column + convert effective_to to end-exclusive.
ALTER TABLE local_agreement_profiles
ADD COLUMN version BIGINT NOT NULL DEFAULT 1;

-- Convert end-inclusive effective_to to end-exclusive: shift by +1 day.
-- A row that was active January through March (effective_to = '2026-03-31') becomes
-- [2026-01-01, 2026-04-01) (effective_to = '2026-04-01'). Open rows stay NULL.
UPDATE local_agreement_profiles
SET effective_to = effective_to + INTERVAL '1 day'
WHERE effective_to IS NOT NULL;

-- Audit-action enum extension (D9): add 'MODIFIED' for in-place edits.
ALTER TABLE local_agreement_profile_audit
DROP CONSTRAINT local_agreement_profile_audit_action_check;

ALTER TABLE local_agreement_profile_audit
ADD CONSTRAINT local_agreement_profile_audit_action_check
CHECK (action IN ('CREATED', 'MODIFIED', 'SUPERSEDED', 'DEACTIVATED', 'MIGRATED_FROM_LEGACY'));
```

Open rows (`effective_to IS NULL`) need no conversion — open is open under either convention. The version backfill is the DEFAULT 1, applied automatically.

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
| `LocalAgreementProfileMigrator.GetActiveSnapshotAsync` (S21) | `effective_to IS NULL OR effective_to >= today` | `effective_to IS NULL OR effective_to > today` | Same reasoning as `GetActivationsInPeriodAsync`. |
| `ConfigResolutionService.ResolveAsync` (profile lookup branch) | calls `GetCurrentOpenAsync` | unchanged | Indirect via the repo. |
| `BuildPlanForLegacyCallers` D9c (PCS) | calls `GetActivationsInPeriodAsync` | propagates the predicate change | Indirect via the repo. |

No production code outside the repository directly compares `effective_to` to a date — the predicate update is localized to the repository's three filter sites + the migrator's snapshot read.

### D11 — Replay-determinism proof for pre-S22 `SegmentManifest`s

Pre-S22 `SegmentManifest`s are replayed by `PCS.ReplayAsync(manifestId)` to reproduce historical calculations. ADR-016 D5b establishes that local-config (post-S21: profile data) is NOT in the snapshot-at-calculation set — manifests do NOT carry profile snapshots; they carry boundary dates derived from `LocalProfileActivation` events.

Replay's interaction with end-exclusive migration:

1. Manifest payload contains `LocalProfileActivations: [(date, profileId)]` per ADR-017 D9b.
2. Replay reads the manifest and re-evaluates rules against the boundary timeline.
3. Boundary detection uses `effective_from` (the activation date), NOT `effective_to`.
4. End-exclusive migration changes `effective_to` values; `effective_from` is unchanged.
5. Therefore, the boundary timeline reproduces identically.

The only S22-introduced replay risk is for code paths that compute "is this profile currently active on date D?" — which use the predicate `effective_from <= D AND (effective_to IS NULL OR effective_to ? D)`. The `?` operator changes from `>=` to `>` under end-exclusive. For replay against pre-S22 manifests, this matters only if the manifest's snapshot-of-the-day overlapped a closed predecessor's `effective_to = D` exactly; under pre-S22 (end-inclusive) the predecessor was active on D, under post-S22 (end-exclusive) it ended *before* D. **However**, profile manifests don't snapshot the profile contents — they snapshot the `effective_from` boundary date and the profile_id, and replay re-fetches the live profile row to evaluate. So the live row, post-migration, has the corrected end-exclusive `effective_to`, and the live read predicate uses `>` accordingly. The replay sees a consistent (end-exclusive) view of the row.

**Test guarantee** (D12 below): regression test that pre-S22 manifests replay byte-identically to post-S22 evaluation of the same period. Floor: 1 dedicated test per the matrix.

### D12 — Test strategy: committed minimum matrix (Q12)

Matching ADR-016 D11 + ADR-017 D11 format. Categories pre-committed as IN; scenario depth committed per category. Total floor: **15 new tests**.

| Category | Scenarios | Floor | File |
|----------|-----------|-------|------|
| Outbox enqueue + publish | Happy path; publisher restart resumes; per-stream FIFO; concurrent cross-stream parallelism; **rolled-back state-change → no outbox row → no publish** | 5 | `tests/StatsTid.Tests.Regression/Outbox/OutboxPublisherTests.cs` |
| Profile row-version + end-exclusive | In-place UPDATE bumps version; concurrent in-place 412; same-day routes to UPDATE; end-exclusive predecessor close | 4 | `tests/StatsTid.Tests.Regression/Config/ProfileRowVersionTests.cs` |
| End-exclusive migration | Closed row converts (`effective_to + 1`); open row stays NULL with version 1; pre-S22 SegmentManifest replays byte-identical | 3 | `tests/StatsTid.Tests.Regression/Config/EndExclusiveMigrationTests.cs` |
| `IEventStore` in-tx overload | In-tx `EnqueueAsync` writes outbox row in caller's tx; rollback removes outbox row; ETag/version backfill on existing profiles returns `1` | 3 | `tests/StatsTid.Tests.Regression/Outbox/EventStoreInTxTests.cs` (+ `tests/StatsTid.Tests.Unit/Outbox/EventStoreInterfaceTests.cs` if needed for unit coverage) |

The "rolled-back state-change → no publish" scenario (cycle 1 Reviewer R-B4) is the inverse-direction guarantee that defines transactional-outbox correctness. Without this scenario, the test matrix only proves "things that committed get published" and not "things that didn't commit don't get published" — which is the entire point of the outbox.

The "pre-S22 SegmentManifest replays byte-identical" scenario (cycle 1 Reviewer R-B3) provides the replay-determinism proof for D11. The test seeds a pre-S22-shape profile (end-inclusive `effective_to`), creates a manifest using S21-era `BuildPlanForLegacyCallers`, runs the migration, then replays the manifest and asserts byte-identical output.

## Rationale

**Why outbox?** The post-commit-append shape S21 cycle 2 settled on is functionally correct under normal operation but leaves a residual partial-failure window that grows operationally (more emit sites = more crash-windows = more divergence opportunities). The transactional outbox pattern is the canonical industry solution for this exact problem (Hohpe + Woolf 2003, Kleppmann 2017). PostgreSQL's MVCC + UNIQUE constraints make it cheap to implement correctly; no message broker required.

**Why per-service publisher?** Backend.Api can't deserialize event types it doesn't reference (ADR-002 isolation). Each service owns its event types; each service publishes its own outbox. The trade-off is N polling loops instead of 1, but the polling cost is trivial (250ms intervals against a partial index on `WHERE published_at IS NULL` is microseconds).

**Why row-version (not XMIN, not optimistic-skipping)?** PostgreSQL's `xmin` system column works for optimistic concurrency without an explicit version column, but it's opaque to application code (you can't write `WHERE xmin = @expected` cleanly across all PG versions; the value changes on VACUUM operations in some configurations). An explicit BIGINT column is self-documenting, testable, and portable. The cost is one column per row.

**Why end-exclusive `effective_to`?** It eliminates the off-by-one arithmetic entirely. Same-day saves no longer produce negative-range rows under any path. The migration is a single `+1 day` UPDATE on closed rows; open rows are unaffected. The convention also matches PostgreSQL's `tsrange` and ISO 8601 interval semantics — a future migration to `daterange` (Phase 5+) would land cleanly.

**Why ship D6 + Step-7a-D2 together?** The row-version column needs the in-tx outbox path to atomically record `LocalAgreementProfileChanged` events for in-place edits. Without the outbox, in-place edits would still need post-commit append, leaving the same partial-failure window the outbox is designed to close — a half-fix.

**Why split D2.2 to S23?** `agreement_configs` has three distinct race conditions (DRAFT in-place / DRAFT→ACTIVE publish / ACTIVE→ARCHIVED clone) that the row-version pattern alone doesn't address; threading those through ADR-018 would more than double the ADR's scope. Sequencing gives the row-version pattern a published exemplar; S23 is largely mechanical replication once the exemplar is in place.

## Implications

### For SharedKernel.Events
- `IEventStore` gains an `EnqueueAsync` overload. Existing callers that use `AppendAsync` directly become publisher-only; state-change sites switch to `EnqueueAsync`.

### For Infrastructure
- `PostgresEventStore` implements both overloads. The in-tx overload writes to `outbox_events`; the self-contained overload writes to `events` + `event_streams` (publisher-only).
- New `OutboxPublisher : BackgroundService` per service. Polls its `service_id` partition; publishes in per-stream FIFO order with at-least-once semantics.
- `LocalAgreementProfileRepository` gains:
  - `Version` field on the model + read mappings.
  - `SupersedeAndCreateAsync` returns `(Guid ProfileId, long Version)` instead of just `Guid`.
  - `UpdateInPlaceAsync` private helper.
  - `AcquireLockAsync` returns `(Guid ProfileId, long Version, DateOnly EffectiveFrom)` (or null).
  - `OptimisticConcurrencyException` reshaped to carry expected/actual versions instead of profile_ids.
  - `InvalidProfileSupersessionException` re-introduced (cycle 3-9 of S21 Step 7a removed it; D9's strict-less-than guard re-introduces it).

### For Backend.Api
- `ConfigEndpoints.MapPut` flow:
  - Parse `If-Match: <version>` from header (was `If-Match: <profile_id>` pre-S22).
  - Build candidate profile, validate alignment.
  - Call `EnqueueAsync` for `LocalAgreementProfileChanged` event in-tx (replaces post-commit `AppendAsync`).
  - Call `SupersedeAndCreateAsync` returning `(profileId, newVersion)`.
  - Set `ETag: "<newVersion>"` on response.
  - Audit-action `MODIFIED` for in-place edits.
- All 12 state-change-emitting endpoint sites (across `ConfigEndpoints`, `AdminEndpoints`, `AgreementConfigEndpoints`, `PositionOverrideEndpoints`, `WageTypeMappingEndpoints`, `ApprovalEndpoints`, `OvertimeEndpoints`, `ComplianceEndpoints`, `SkemaEndpoints`, `TimerEndpoints`, `TimeEndpoints`, plus `Payroll` + `External` integrations) swap one line: `tx.Commit + AppendAsync` → `EnqueueAsync(conn, tx, ...) + tx.Commit`.

### For Frontend
- `useConfig.ts` ETag handling: parse `ETag: "<number>"` header on GET responses; send `If-Match: "<number>"` on PUT. Numeric ETag is simpler than UUID — no quoting weirdness, parse with `parseInt`.
- 412 response handling unchanged.

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
- **Q3 (`IEventStore` evolution):** option (a) `IOutboxEnqueue` rejected — adds a new interface without changing semantics. Option (c) `IEventEmitter` rejected — same reason; type taxonomy doubles.
- **Q5 (at-least-once idempotency):** option (a) outbox row carries `stream_version` at enqueue rejected — reproduces S21 cycle-2 MVCC snapshot conflict. Option (c) per-stream sequencer table rejected — mechanically equivalent to (b) without behavior gain.
- **Q7 (row-version bump):** trigger-based bump rejected — invisible from C# code, hard to assert correctly in tests, obscures the optimistic-concurrency contract. PostgreSQL `xmin` rejected — opaque, version-dependent, changes on VACUUM in some configurations.
- **Q9 (in-place routing):** explicit `EditMode.InPlace` / `EditMode.Supersede` parameter on the repo rejected — the repo can detect from `newProfile.EffectiveFrom` cleanly; explicit mode adds caller burden without disambiguation gain (caller would just compute the same condition).

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
