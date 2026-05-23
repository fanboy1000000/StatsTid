using System.Globalization;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

// =============================================================================
// S43 / ADR-026 D7 — Audit projection backfill (single source of truth).
// =============================================================================
//
// Replays events from the canonical `events` log into `audit_projection`
// via the mapper registry. Re-runnable in any state — the repository's
// `ON CONFLICT (event_id) DO NOTHING` makes it idempotent.
//
// Mirrors the S27 `ProjectionBackfillService` SSOT contract. Three call
// sites:
//
//   1. Backend.Api startup (Program.cs) — unconditional invocation after
//      seeders run, BEFORE GETs are served. Per Step 0b cycle 1 BLOCKER
//      absorption: NO row-count gate (a row-count > 0 from Sub-Sprint 1
//      would prevent Sub-Sprint 2 backfill from picking up events whose
//      mappers had just landed).
//
//   2. tools/ProjectionBackfill console app — wraps this service for
//      ad-hoc ops use with `--target audit_projection`.
//
//   3. AuditProjectionBackfillIdempotencyTests (Phase E Test #2) — pins
//      the idempotency contract by exercising this same code path with
//      a seeded test event + test mapper.
//
// Sub-Sprint 1 behavior: no concrete mappers exist; every audit-relevant
// event misses the registry lookup and is counted as `NoMapper`. By design
// — Sub-Sprint 2 (S44) populates the ~53 mapper sites. Sub-Sprint 2's
// startup hook will pick up events the new mappers cover thanks to
// idempotent re-runs.
//
// `audit_projection.actor_primary_org_id` is left NULL during backfill;
// resolving it would require a JOIN against `users.primary_org_id` for
// each row. The scope-by-actor query path (ADR-026 D5 secondary) gracefully
// handles NULL actor_primary_org_id via the partial index predicate.
// Sub-Sprint 2 endpoint mappers populate the field from JWT at emit time.
//
// Pre-S22 events with NULL outbox_id are skipped (audit_projection.outbox_id
// is NOT NULL). Logged as INFO; benign in pre-launch context.

/// <summary>
/// Counters returned by <see cref="AuditProjectionBackfillService.RunAsync"/>.
/// Allows callers (startup hook, console app, Phase E tests) to assert on
/// the insert / conflict / no-mapper / pre-S22-skip split.
/// </summary>
public sealed record AuditProjectionBackfillResult(
    int Scanned,
    int Inserted,
    int Conflicts,
    int NoMapper,
    int PreS22Skipped,
    int UnknownEventTypes,
    int DeserializationErrors);

/// <summary>
/// Replays events into <c>audit_projection</c> via the registered mapper
/// set. Idempotent via the repository's <c>ON CONFLICT (event_id) DO NOTHING</c>.
/// Tolerant of an empty events table (returns all-zero counters; no error).
/// </summary>
public sealed class AuditProjectionBackfillService
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly AuditProjectionRepository _repository;
    private readonly IAuditProjectionMapperRegistry _registry;
    private readonly ILogger<AuditProjectionBackfillService> _logger;

    // S22 deploy date — ADR-018 (transactional outbox) shipped on commit
    // a278f34 on 2026-05-05. Any event whose `stored_at >= this` should
    // have a matching outbox_events row.
    private const string S22DeployDate = "2026-05-05";
    private static readonly DateTime S22DeployUtc = DateTime.SpecifyKind(
        DateTime.Parse(S22DeployDate, CultureInfo.InvariantCulture),
        DateTimeKind.Utc);

    // SELECT all events joined to their outbox row. No WHERE filter — the
    // mapper registry is the source of truth for audit-relevance.
    // Ordered by (stream_id, stream_version) so per-stream replay
    // determinism is preserved per ADR-016 D10.
    private const string SelectSql = @"
        SELECT
            events.event_id,
            events.event_type,
            events.data,
            outbox_events.outbox_id,
            events.stored_at
        FROM events
        LEFT JOIN outbox_events ON events.event_id = outbox_events.event_id
        ORDER BY events.stream_id, events.stream_version
    ";

    public AuditProjectionBackfillService(
        DbConnectionFactory connectionFactory,
        AuditProjectionRepository repository,
        IAuditProjectionMapperRegistry registry,
        ILogger<AuditProjectionBackfillService> logger)
    {
        _connectionFactory = connectionFactory;
        _repository = repository;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Replays the events table into <c>audit_projection</c>. Idempotent.
    /// Tolerant of an empty events table.
    /// </summary>
    public async Task<AuditProjectionBackfillResult> RunAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Buffer rows from the SELECT before issuing INSERTs (Npgsql can't
        // run INSERTs while a DataReader is open on the same connection).
        var rows = new List<(Guid EventId, string EventType, string Data, long? OutboxId, DateTime StoredAt)>();
        await using (var selectCmd = new NpgsqlCommand(SelectSql, conn, tx))
        await using (var reader = await selectCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                rows.Add((
                    EventId: reader.GetGuid(0),
                    EventType: reader.GetString(1),
                    Data: reader.GetString(2),
                    OutboxId: reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    StoredAt: reader.GetDateTime(4)
                ));
            }
        }

        int scanned = 0;
        int inserted = 0;
        int conflicts = 0;
        int noMapper = 0;
        int preS22Skipped = 0;
        int unknownEventTypes = 0;
        int deserializationErrors = 0;

        foreach (var row in rows)
        {
            scanned++;

            // Pre-S22 events have no outbox row; audit_projection.outbox_id
            // is NOT NULL. Skip rather than crash. Steady-state post-S22
            // events without an outbox row are anomalous (warn-log).
            if (!row.OutboxId.HasValue)
            {
                if (row.StoredAt >= S22DeployUtc)
                {
                    _logger.LogWarning(
                        "Post-S22 event {EventId} (stored_at={StoredAt:O}) has no matching outbox_events row; skipping audit projection backfill (anomalous in steady state).",
                        row.EventId, row.StoredAt);
                }
                preS22Skipped++;
                continue;
            }

            IDomainEvent domainEvent;
            try
            {
                domainEvent = EventSerializer.Deserialize(row.EventType, row.Data);
            }
            catch (InvalidOperationException ex)
            {
                unknownEventTypes++;
                _logger.LogWarning(
                    "Event {EventId} type={EventType}: {Message}",
                    row.EventId, row.EventType, ex.Message);
                continue;
            }
            catch (Exception ex)
            {
                deserializationErrors++;
                _logger.LogWarning(ex,
                    "Event {EventId} type={EventType}: deserialization failed",
                    row.EventId, row.EventType);
                continue;
            }

            // Backfill context — actor fields come from the event itself
            // (DomainEventBase ActorId / CorrelationId / OccurredAt).
            // actor_primary_org_id stays NULL for backfilled rows (see
            // file header).
            var occurredAtUtc = DateTime.SpecifyKind(
                domainEvent.OccurredAt.Kind == DateTimeKind.Utc
                    ? domainEvent.OccurredAt
                    : DateTime.SpecifyKind(domainEvent.OccurredAt, DateTimeKind.Utc),
                DateTimeKind.Utc);
            var ctx = new AuditProjectionContext(
                ActorId: domainEvent.ActorId,
                ActorPrimaryOrgId: null,
                CorrelationId: domainEvent.CorrelationId,
                OccurredAt: new DateTimeOffset(occurredAtUtc, TimeSpan.Zero));

            var rowData = _registry.TryMap(domainEvent, ctx);
            if (rowData is null)
            {
                noMapper++;
                continue;
            }

            var affected = await _repository.InsertAsync(
                conn, tx,
                row.EventId, row.OutboxId.Value, row.EventType,
                rowData, ctx, ct);
            if (affected == 1) inserted++;
            else conflicts++;
        }

        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Audit projection backfill complete: scanned={Scanned} inserted={Inserted} conflicts={Conflicts} noMapper={NoMapper} preS22Skipped={PreS22Skipped} unknownEventTypes={UnknownTypes} deserializationErrors={Errors}",
            scanned, inserted, conflicts, noMapper, preS22Skipped, unknownEventTypes, deserializationErrors);

        return new AuditProjectionBackfillResult(
            Scanned: scanned,
            Inserted: inserted,
            Conflicts: conflicts,
            NoMapper: noMapper,
            PreS22Skipped: preS22Skipped,
            UnknownEventTypes: unknownEventTypes,
            DeserializationErrors: deserializationErrors);
    }
}
