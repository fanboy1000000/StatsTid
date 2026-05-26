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
/// the insert / conflict / no-mapper / null-outbox-skip split.
///
/// <para>
/// <see cref="NullOutboxSkipped"/> covers BOTH legitimate pre-S22 events (no
/// matching outbox row by design) AND post-S22 anomalies (events whose
/// stored_at is post-S22 but with no outbox row, e.g. mid-tx crash before
/// the outbox INSERT landed). The anomaly case is warn-logged at scan time;
/// the counter rolls both into the same skip total — observability surfaces
/// should treat any non-zero <see cref="NullOutboxSkipped"/> as worth
/// investigating in steady state. Per Step 7a cycle 1 Reviewer W2
/// absorption (renamed from <c>NullOutboxSkipped</c> to remove the implication
/// that this only catches the pre-S22 path).
/// </para>
/// </summary>
public sealed record AuditProjectionBackfillResult(
    int Scanned,
    int Inserted,
    int Conflicts,
    int NoMapper,
    int NullOutboxSkipped,
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

    // SELECT events joined to their outbox row, filtered by event types
    // that have registered mappers. Per Step 7a cycle 1 Codex W1 absorption:
    // earlier draft scanned the full events log (O(total events) at every
    // boot) which doesn't scale; the registry's RegisteredEventTypeNames
    // narrows the scan to only events that could possibly produce a
    // projection row. Sub-Sprint 1 default = empty set → backfill is a no-op
    // (the WHERE clause filters to nothing). Sub-Sprint 2 mapper registration
    // populates the set.
    //
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
        WHERE events.event_type = ANY(@eventTypes)
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
    /// Tolerant of an empty registered-event-types set (Sub-Sprint 1
    /// default) — scans nothing, returns all-zero counters, exits early.
    /// </summary>
    public async Task<AuditProjectionBackfillResult> RunAsync(CancellationToken ct = default)
    {
        // Fast path: no mappers registered → nothing to backfill.
        // Sub-Sprint 1 default state; expected zero-cost no-op.
        var eventTypeFilter = _registry.RegisteredEventTypeNames.ToArray();
        if (eventTypeFilter.Length == 0)
        {
            _logger.LogInformation(
                "Audit projection backfill: no mapper-registered event types — skipping scan (Sub-Sprint 1 default state; Sub-Sprint 2 mapper registrations will populate the filter).");
            return new AuditProjectionBackfillResult(0, 0, 0, 0, 0, 0, 0);
        }

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Buffer rows from the SELECT before issuing INSERTs (Npgsql can't
        // run INSERTs while a DataReader is open on the same connection).
        var rows = new List<(Guid EventId, string EventType, string Data, long? OutboxId, DateTime StoredAt)>();
        await using (var selectCmd = new NpgsqlCommand(SelectSql, conn, tx))
        {
            selectCmd.Parameters.AddWithValue("eventTypes", eventTypeFilter);
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
        }

        int scanned = 0;
        int inserted = 0;
        int conflicts = 0;
        int noMapper = 0;
        int nullOutboxSkipped = 0;
        int unknownEventTypes = 0;
        int deserializationErrors = 0;

        // Batch-load user→org mapping for employee-scoped events whose
        // payload carries EmployeeId but not OrgId. Single query, cached
        // for the duration of the backfill run.
        var userOrgLookup = await LoadUserOrgLookupAsync(conn, tx, ct);

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
                nullOutboxSkipped++;
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
            // ResolvedTargetOrgId is extracted from the event payload
            // (OrgId property if present, otherwise employee→user lookup).
            var occurredAtUtc = DateTime.SpecifyKind(
                domainEvent.OccurredAt.Kind == DateTimeKind.Utc
                    ? domainEvent.OccurredAt
                    : DateTime.SpecifyKind(domainEvent.OccurredAt, DateTimeKind.Utc),
                DateTimeKind.Utc);
            var resolvedOrgId = ResolveTargetOrgId(domainEvent, userOrgLookup);
            var ctx = new AuditProjectionContext(
                ActorId: domainEvent.ActorId,
                ActorPrimaryOrgId: null,
                CorrelationId: domainEvent.CorrelationId,
                OccurredAt: new DateTimeOffset(occurredAtUtc, TimeSpan.Zero),
                ResolvedTargetOrgId: resolvedOrgId);

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
            "Audit projection backfill complete: scanned={Scanned} inserted={Inserted} conflicts={Conflicts} noMapper={NoMapper} nullOutboxSkipped={NullOutboxSkipped} unknownEventTypes={UnknownTypes} deserializationErrors={Errors}",
            scanned, inserted, conflicts, noMapper, nullOutboxSkipped, unknownEventTypes, deserializationErrors);

        return new AuditProjectionBackfillResult(
            Scanned: scanned,
            Inserted: inserted,
            Conflicts: conflicts,
            NoMapper: noMapper,
            NullOutboxSkipped: nullOutboxSkipped,
            UnknownEventTypes: unknownEventTypes,
            DeserializationErrors: deserializationErrors);
    }

    private static async Task<Dictionary<string, string>> LoadUserOrgLookupAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id, primary_org_id FROM users WHERE primary_org_id IS NOT NULL", conn, tx);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lookup[reader.GetString(0)] = reader.GetString(1);
        }
        return lookup;
    }

    private static string? ResolveTargetOrgId(
        IDomainEvent domainEvent, Dictionary<string, string> userOrgLookup)
    {
        // Events that carry OrgId directly — use it.
        var orgProp = domainEvent.GetType().GetProperty("OrgId");
        if (orgProp?.GetValue(domainEvent) is string orgId)
            return orgId;

        // Events that carry PrimaryOrgId (e.g. UserCreated) — use it.
        var primaryOrgProp = domainEvent.GetType().GetProperty("PrimaryOrgId");
        if (primaryOrgProp?.GetValue(domainEvent) is string primaryOrgId)
            return primaryOrgId;

        // Events that carry EmployeeId — look up user's primary_org_id.
        var empProp = domainEvent.GetType().GetProperty("EmployeeId");
        if (empProp?.GetValue(domainEvent) is string employeeId &&
            userOrgLookup.TryGetValue(employeeId, out var empOrg))
            return empOrg;

        // Events that carry UserId (e.g. UserUpdated) — look up.
        var userProp = domainEvent.GetType().GetProperty("UserId");
        if (userProp?.GetValue(domainEvent) is string userId &&
            userOrgLookup.TryGetValue(userId, out var userOrg))
            return userOrg;

        return null;
    }
}
