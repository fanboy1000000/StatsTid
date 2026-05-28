using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

// =============================================================================
// S27 Phase 4c.6 / Step 7a cycle 1 BLOCKER fix —
// Single source of truth for projection backfill.
// =============================================================================
//
// Rebuilds `time_entries_projection` + `absences_projection` from the canonical
// `events` log so deployments don't strand pre-S27 events outside the new sync
// projection tables. Re-runnable in any state — `ON CONFLICT (event_id) DO
// NOTHING` makes the backfill idempotent.
//
// This service is the canonical backfill implementation. Three call sites:
//
//   1. Backend.Api startup (Program.cs) — invoked as a one-shot after the
//      schema seeders run, BEFORE the host begins serving GETs. Required
//      because the migrated GET handlers (TimeEndpoints, SkemaEndpoints,
//      BalanceEndpoints, ComplianceEndpoints) read EXCLUSIVELY from the
//      projection tables; a deploy against an existing events table without
//      this hook would surface empty GETs until ops manually ran the tool.
//
//   2. tools/ProjectionBackfill console app — wraps this service for ad-hoc
//      ops use (point at any DB connstr, dump counters to stdout).
//
//   3. ProjectionBackfillTests (Docker-gated regression) — pins the
//      idempotency contract by exercising this same code path.
//
// Mirrors the S20 SegmentManifestProjectionRebuilder pattern (single-tx
// replay from the event store; counters returned to the caller). Differs in
// two ways:
//
//   1. No TRUNCATE — TimeEntryRegistered / AbsenceRegistered events flow in
//      from sync POST handlers (TASK-2706/2707) on the live system, so a
//      TRUNCATE here would race with normal traffic. Idempotent UPSERT
//      (ON CONFLICT DO NOTHING) is safer for online use.
//   2. C# deserialization — payload shape uses optional fields (StartTime,
//      EndTime, TaskId, ActivityType, ActorId, ActorRole, CorrelationId) and
//      pure-SQL extraction would be brittle. EventSerializer handles the
//      camelCase + nullable-DateTime / TimeOnly / Guid edge cases for us.

/// <summary>
/// Counters returned by <see cref="ProjectionBackfillService.RunAsync"/>.
/// Allows callers (startup hook, console app, regression tests) to assert on
/// the per-table insert / conflict / fallback-warning split.
/// </summary>
public sealed record ProjectionBackfillResult(
    int Scanned,
    int InsertedTime,
    int InsertedAbsences,
    int ConflictsTime,
    int ConflictsAbsences,
    int FallbackWarnings,
    int UnknownEventTypes,
    // S56 TASK-5603: work_time_projection backfill counters. Optional (defaulted)
    // so existing 7-arg positional callers (console app, startup, pre-S56 tests)
    // keep compiling. work_time_projection is LATEST-WINS keyed (employee_id, date),
    // so "Applied" = upsert affected a row; "Skipped" = the outbox_id<=guard blocked
    // a stale-event overwrite (a no-op, not an error).
    int AppliedWorkTime = 0,
    int SkippedWorkTime = 0);

/// <summary>
/// Replays <c>TimeEntryRegistered</c> + <c>AbsenceRegistered</c> events from
/// the canonical <c>events</c> log into the S27 projection tables. Idempotent
/// via <c>ON CONFLICT (event_id) DO NOTHING</c>. Tolerant of an empty events
/// table (returns all-zero counters; no error).
/// </summary>
public sealed class ProjectionBackfillService
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly ILogger<ProjectionBackfillService> _logger;

    // S22 deploy date — ADR-018 (transactional outbox) shipped on commit
    // a278f34 on 2026-05-05. Any event whose `stored_at >= this` should have
    // a matching outbox_events row; if the LEFT JOIN comes back NULL for such
    // an event, we warn-log because that's anomalous (manual event insertion,
    // mid-tx crash before the outbox row landed, etc.). Pre-S22 events
    // legitimately have no outbox row and we silently fall back to
    // stream_version for ordering.
    private const string S22DeployDate = "2026-05-05";
    private static readonly DateTime S22DeployUtc = DateTime.SpecifyKind(
        DateTime.Parse(S22DeployDate, CultureInfo.InvariantCulture),
        DateTimeKind.Utc);

    // SQL: stream-aligned read of TimeEntryRegistered + AbsenceRegistered
    // events joined to their outbox row (LEFT JOIN — pre-S22 events have no
    // outbox). Ordered by (stream_id, stream_version) so per-stream replay
    // determinism is preserved per ADR-016 D10.
    private const string SelectSql = @"
        SELECT
            events.event_id,
            events.event_type,
            events.data,
            events.stream_version,
            outbox_events.outbox_id,
            events.stored_at
        FROM events
        LEFT JOIN outbox_events ON events.event_id = outbox_events.event_id
        WHERE events.event_type IN ('TimeEntryRegistered', 'AbsenceRegistered', 'WorkTimeRegistered')
        ORDER BY events.stream_id, events.stream_version
    ";

    // S56 TASK-5603: camelCase matches WorkTimeProjectionRepository.IntervalsJsonOptions
    // so the JSONB shape written by backfill is byte-identical to the live POST path.
    private static readonly JsonSerializerOptions IntervalsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string InsertTimeSql = @"
        INSERT INTO time_entries_projection (
            event_id, employee_id, date, hours, start_time, end_time,
            task_id, activity_type, agreement_code, ok_version,
            voluntary_unsocial_hours, occurred_at, actor_id, actor_role,
            correlation_id, outbox_id
        ) VALUES (
            @eventId, @employeeId, @date, @hours, @startTime, @endTime,
            @taskId, @activityType, @agreementCode, @okVersion,
            @voluntaryUnsocialHours, @occurredAt, @actorId, @actorRole,
            @correlationId, @outboxId
        )
        ON CONFLICT (event_id) DO NOTHING
    ";

    private const string InsertAbsSql = @"
        INSERT INTO absences_projection (
            event_id, employee_id, date, absence_type, hours,
            agreement_code, ok_version, occurred_at,
            actor_id, actor_role, correlation_id, outbox_id
        ) VALUES (
            @eventId, @employeeId, @date, @absenceType, @hours,
            @agreementCode, @okVersion, @occurredAt,
            @actorId, @actorRole, @correlationId, @outboxId
        )
        ON CONFLICT (event_id) DO NOTHING
    ";

    // S56 TASK-5603: LATEST-WINS upsert — IDENTICAL to WorkTimeProjectionRepository.UpsertAsync.
    // Replaying in (stream_id, stream_version) order with the outbox_id<= guard yields the
    // correct final per-(employee,date) state; re-running is idempotent (equal outbox_id
    // re-applies the same values; a newer live row is never clobbered by an older replayed event).
    private const string UpsertWorkTimeSql = @"
        INSERT INTO work_time_projection (
            employee_id, date, intervals, manual_hours,
            occurred_at, actor_id, actor_role, correlation_id, outbox_id
        ) VALUES (
            @employeeId, @date, @intervals, @manualHours,
            @occurredAt, @actorId, @actorRole, @correlationId, @outboxId
        )
        ON CONFLICT (employee_id, date) DO UPDATE SET
            intervals = EXCLUDED.intervals,
            manual_hours = EXCLUDED.manual_hours,
            occurred_at = EXCLUDED.occurred_at,
            actor_id = EXCLUDED.actor_id,
            actor_role = EXCLUDED.actor_role,
            correlation_id = EXCLUDED.correlation_id,
            outbox_id = EXCLUDED.outbox_id
        WHERE work_time_projection.outbox_id <= EXCLUDED.outbox_id
    ";

    public ProjectionBackfillService(
        DbConnectionFactory connectionFactory,
        ILogger<ProjectionBackfillService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Replays the events table into the projection tables. Idempotent.
    /// Tolerant of an empty events table (returns all-zero counters).
    /// </summary>
    public async Task<ProjectionBackfillResult> RunAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        // RepeatableRead: a snapshot of `events` + `outbox_events` taken at
        // the start of the tx — sufficient for a backfill since (a) the LEFT
        // JOIN is read-only and (b) idempotent ON CONFLICT INSERTs against
        // the projection are tolerant of concurrent sync writes from the
        // live POST handlers. SERIALIZABLE would be overkill (no
        // read-modify-write on rows we're inserting; PRIMARY KEY conflict
        // resolution is atomic in Postgres regardless of isolation level).
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        // Pre-stage the INSERT commands so we don't pay the prepare cost on
        // every row.
        await using var insertTimeCmd = new NpgsqlCommand(InsertTimeSql, conn, tx);
        insertTimeCmd.Parameters.Add("eventId", NpgsqlDbType.Uuid);
        insertTimeCmd.Parameters.Add("employeeId", NpgsqlDbType.Text);
        insertTimeCmd.Parameters.Add("date", NpgsqlDbType.Date);
        insertTimeCmd.Parameters.Add("hours", NpgsqlDbType.Numeric);
        insertTimeCmd.Parameters.Add("startTime", NpgsqlDbType.Time);
        insertTimeCmd.Parameters.Add("endTime", NpgsqlDbType.Time);
        insertTimeCmd.Parameters.Add("taskId", NpgsqlDbType.Text);
        insertTimeCmd.Parameters.Add("activityType", NpgsqlDbType.Text);
        insertTimeCmd.Parameters.Add("agreementCode", NpgsqlDbType.Text);
        insertTimeCmd.Parameters.Add("okVersion", NpgsqlDbType.Text);
        insertTimeCmd.Parameters.Add("voluntaryUnsocialHours", NpgsqlDbType.Boolean);
        insertTimeCmd.Parameters.Add("occurredAt", NpgsqlDbType.TimestampTz);
        insertTimeCmd.Parameters.Add("actorId", NpgsqlDbType.Text);
        insertTimeCmd.Parameters.Add("actorRole", NpgsqlDbType.Text);
        insertTimeCmd.Parameters.Add("correlationId", NpgsqlDbType.Uuid);
        insertTimeCmd.Parameters.Add("outboxId", NpgsqlDbType.Bigint);

        await using var insertAbsCmd = new NpgsqlCommand(InsertAbsSql, conn, tx);
        insertAbsCmd.Parameters.Add("eventId", NpgsqlDbType.Uuid);
        insertAbsCmd.Parameters.Add("employeeId", NpgsqlDbType.Text);
        insertAbsCmd.Parameters.Add("date", NpgsqlDbType.Date);
        insertAbsCmd.Parameters.Add("absenceType", NpgsqlDbType.Text);
        insertAbsCmd.Parameters.Add("hours", NpgsqlDbType.Numeric);
        insertAbsCmd.Parameters.Add("agreementCode", NpgsqlDbType.Text);
        insertAbsCmd.Parameters.Add("okVersion", NpgsqlDbType.Text);
        insertAbsCmd.Parameters.Add("occurredAt", NpgsqlDbType.TimestampTz);
        insertAbsCmd.Parameters.Add("actorId", NpgsqlDbType.Text);
        insertAbsCmd.Parameters.Add("actorRole", NpgsqlDbType.Text);
        insertAbsCmd.Parameters.Add("correlationId", NpgsqlDbType.Uuid);
        insertAbsCmd.Parameters.Add("outboxId", NpgsqlDbType.Bigint);

        await using var upsertWorkTimeCmd = new NpgsqlCommand(UpsertWorkTimeSql, conn, tx);
        upsertWorkTimeCmd.Parameters.Add("employeeId", NpgsqlDbType.Text);
        upsertWorkTimeCmd.Parameters.Add("date", NpgsqlDbType.Date);
        upsertWorkTimeCmd.Parameters.Add("intervals", NpgsqlDbType.Jsonb);
        upsertWorkTimeCmd.Parameters.Add("manualHours", NpgsqlDbType.Numeric);
        upsertWorkTimeCmd.Parameters.Add("occurredAt", NpgsqlDbType.TimestampTz);
        upsertWorkTimeCmd.Parameters.Add("actorId", NpgsqlDbType.Text);
        upsertWorkTimeCmd.Parameters.Add("actorRole", NpgsqlDbType.Text);
        upsertWorkTimeCmd.Parameters.Add("correlationId", NpgsqlDbType.Uuid);
        upsertWorkTimeCmd.Parameters.Add("outboxId", NpgsqlDbType.Bigint);

        // Buffer rows from the SELECT before issuing INSERTs. We can't run
        // INSERT statements while a DataReader is open on the same
        // connection in Npgsql.
        var rows = new List<(Guid EventId, string EventType, string Data, int StreamVersion, long? OutboxId, DateTime StoredAt)>();
        await using (var selectCmd = new NpgsqlCommand(SelectSql, conn, tx))
        await using (var reader = await selectCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                rows.Add((
                    EventId: reader.GetGuid(0),
                    EventType: reader.GetString(1),
                    Data: reader.GetString(2),
                    StreamVersion: reader.GetInt32(3),
                    OutboxId: reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    StoredAt: reader.GetDateTime(5)
                ));
            }
        }

        int scanned = 0;
        int insertedTime = 0;
        int insertedAbs = 0;
        int conflictsTime = 0;
        int conflictsAbs = 0;
        int fallbackWarns = 0;
        int unknownEventTypes = 0;
        int appliedWorkTime = 0;
        int skippedWorkTime = 0;

        foreach (var row in rows)
        {
            scanned++;

            // Resolve outbox_id: prefer the real outbox row; fall back to
            // stream_version for pre-S22 events. Warn-log post-S22 events
            // that surprisingly have no outbox row (anomalous in steady
            // state per ADR-018).
            long outboxId;
            if (row.OutboxId.HasValue)
            {
                outboxId = row.OutboxId.Value;
            }
            else
            {
                outboxId = row.StreamVersion;
                if (row.StoredAt >= S22DeployUtc)
                {
                    fallbackWarns++;
                    _logger.LogWarning(
                        "Post-S22 event {EventId} (stored_at={StoredAt:O}) has no matching outbox_events row; falling back to stream_version={StreamVersion}. This is anomalous in steady state.",
                        row.EventId, row.StoredAt, row.StreamVersion);
                }
            }

            IDomainEvent domainEvent;
            try
            {
                domainEvent = EventSerializer.Deserialize(row.EventType, row.Data);
            }
            catch (InvalidOperationException ex)
            {
                // Should be impossible given the WHERE clause restricts to
                // two known types, but if EventSerializer's map drifts we
                // want a clear signal rather than a swallowed error.
                unknownEventTypes++;
                _logger.LogWarning(
                    "Event {EventId} type={EventType}: {Message}",
                    row.EventId, row.EventType, ex.Message);
                continue;
            }

            if (domainEvent is TimeEntryRegistered te)
            {
                insertTimeCmd.Parameters["eventId"].Value = te.EventId;
                insertTimeCmd.Parameters["employeeId"].Value = te.EmployeeId;
                insertTimeCmd.Parameters["date"].Value = te.Date;
                insertTimeCmd.Parameters["hours"].Value = te.Hours;
                insertTimeCmd.Parameters["startTime"].Value = (object?)te.StartTime ?? DBNull.Value;
                insertTimeCmd.Parameters["endTime"].Value = (object?)te.EndTime ?? DBNull.Value;
                insertTimeCmd.Parameters["taskId"].Value = (object?)te.TaskId ?? DBNull.Value;
                insertTimeCmd.Parameters["activityType"].Value = (object?)te.ActivityType ?? DBNull.Value;
                insertTimeCmd.Parameters["agreementCode"].Value = te.AgreementCode;
                insertTimeCmd.Parameters["okVersion"].Value = te.OkVersion;
                insertTimeCmd.Parameters["voluntaryUnsocialHours"].Value = te.VoluntaryUnsocialHours;
                // Normalize OccurredAt to UTC — DomainEventBase defaults to
                // DateTime.UtcNow, but DB-roundtripped events may come back
                // as Unspecified-kind from JSON. Postgres timestamptz wants
                // a UTC value; DateTime.SpecifyKind is safe because the
                // event store always stores UTC per ADR-005.
                insertTimeCmd.Parameters["occurredAt"].Value = te.OccurredAt.Kind == DateTimeKind.Utc
                    ? te.OccurredAt
                    : DateTime.SpecifyKind(te.OccurredAt, DateTimeKind.Utc);
                insertTimeCmd.Parameters["actorId"].Value = (object?)te.ActorId ?? DBNull.Value;
                insertTimeCmd.Parameters["actorRole"].Value = (object?)te.ActorRole ?? DBNull.Value;
                insertTimeCmd.Parameters["correlationId"].Value = (object?)te.CorrelationId ?? DBNull.Value;
                insertTimeCmd.Parameters["outboxId"].Value = outboxId;

                var affected = await insertTimeCmd.ExecuteNonQueryAsync(ct);
                if (affected == 1) insertedTime++;
                else conflictsTime++;
            }
            else if (domainEvent is AbsenceRegistered ab)
            {
                insertAbsCmd.Parameters["eventId"].Value = ab.EventId;
                insertAbsCmd.Parameters["employeeId"].Value = ab.EmployeeId;
                insertAbsCmd.Parameters["date"].Value = ab.Date;
                insertAbsCmd.Parameters["absenceType"].Value = ab.AbsenceType;
                insertAbsCmd.Parameters["hours"].Value = ab.Hours;
                insertAbsCmd.Parameters["agreementCode"].Value = ab.AgreementCode;
                insertAbsCmd.Parameters["okVersion"].Value = ab.OkVersion;
                insertAbsCmd.Parameters["occurredAt"].Value = ab.OccurredAt.Kind == DateTimeKind.Utc
                    ? ab.OccurredAt
                    : DateTime.SpecifyKind(ab.OccurredAt, DateTimeKind.Utc);
                insertAbsCmd.Parameters["actorId"].Value = (object?)ab.ActorId ?? DBNull.Value;
                insertAbsCmd.Parameters["actorRole"].Value = (object?)ab.ActorRole ?? DBNull.Value;
                insertAbsCmd.Parameters["correlationId"].Value = (object?)ab.CorrelationId ?? DBNull.Value;
                insertAbsCmd.Parameters["outboxId"].Value = outboxId;

                var affected = await insertAbsCmd.ExecuteNonQueryAsync(ct);
                if (affected == 1) insertedAbs++;
                else conflictsAbs++;
            }
            else if (domainEvent is WorkTimeRegistered wt)
            {
                upsertWorkTimeCmd.Parameters["employeeId"].Value = wt.EmployeeId;
                upsertWorkTimeCmd.Parameters["date"].Value = wt.Date;
                upsertWorkTimeCmd.Parameters["intervals"].Value =
                    JsonSerializer.Serialize(wt.Intervals, IntervalsJsonOptions);
                upsertWorkTimeCmd.Parameters["manualHours"].Value = wt.ManualHours;
                upsertWorkTimeCmd.Parameters["occurredAt"].Value = wt.OccurredAt.Kind == DateTimeKind.Utc
                    ? wt.OccurredAt
                    : DateTime.SpecifyKind(wt.OccurredAt, DateTimeKind.Utc);
                upsertWorkTimeCmd.Parameters["actorId"].Value = (object?)wt.ActorId ?? DBNull.Value;
                upsertWorkTimeCmd.Parameters["actorRole"].Value = (object?)wt.ActorRole ?? DBNull.Value;
                upsertWorkTimeCmd.Parameters["correlationId"].Value = (object?)wt.CorrelationId ?? DBNull.Value;
                upsertWorkTimeCmd.Parameters["outboxId"].Value = outboxId;

                // affected==1 → inserted or updated; affected==0 → outbox_id<= guard
                // blocked a stale-event overwrite (a no-op, not a conflict error).
                var affected = await upsertWorkTimeCmd.ExecuteNonQueryAsync(ct);
                if (affected == 1) appliedWorkTime++;
                else skippedWorkTime++;
            }
            // Any other type is impossible given the SELECT WHERE clause;
            // the unknown-type branch above already handles deserialization
            // mismatches.
        }

        await tx.CommitAsync(ct);

        return new ProjectionBackfillResult(
            Scanned: scanned,
            InsertedTime: insertedTime,
            InsertedAbsences: insertedAbs,
            ConflictsTime: conflictsTime,
            ConflictsAbsences: conflictsAbs,
            FallbackWarnings: fallbackWarns,
            UnknownEventTypes: unknownEventTypes,
            AppliedWorkTime: appliedWorkTime,
            SkippedWorkTime: skippedWorkTime);
    }
}
