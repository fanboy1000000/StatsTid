// =============================================================================
// S27 / TASK-2705 — One-shot Projection Backfill
// =============================================================================
//
// Rebuilds `time_entries_projection` + `absences_projection` from the canonical
// `events` log so deployments don't strand pre-S27 events outside the new sync
// projection tables. Re-runnable in any state — `ON CONFLICT (event_id) DO
// NOTHING` makes the backfill idempotent (closes Acceptance Criteria
// "Schema + repos + backfill" in REFINEMENT-s27-phase-4c6.md).
//
// Mirrors the S20 SegmentManifestProjectionRebuilder pattern (single-tx replay
// from the event store; counters logged at the end). Differs in two ways:
//
//   1. No TRUNCATE — TimeEntryRegistered / AbsenceRegistered events flow in
//      from sync POST handlers (TASK-2706/2707) on the live system, so a
//      TRUNCATE here would race with normal traffic. Idempotent UPSERT
//      (ON CONFLICT DO NOTHING) is safer for online use.
//   2. C# deserialization — payload shape uses optional fields (StartTime,
//      EndTime, TaskId, ActivityType, ActorId, ActorRole, CorrelationId) and
//      pure-SQL extraction would be brittle. EventSerializer handles the
//      camelCase + nullable-DateTime / TimeOnly / Guid edge cases for us.
//
// CLI:
//   dotnet run --project tools/ProjectionBackfill -- --connection "<connstr>"
// Or env var:
//   POSTGRES_CONNECTION_STRING="Host=...;..." dotnet run --project tools/ProjectionBackfill
//
// Exit codes:
//   0  success
//   2  missing connection string
// (any unhandled exception bubbles up with the default non-zero exit code)

using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

// -----------------------------------------------------------------------------
// Connection string resolution: --connection flag wins, env var is fallback.
// -----------------------------------------------------------------------------
string? connStr = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--connection")
    {
        connStr = args[i + 1];
        break;
    }
}
connStr ??= Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connStr))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/ProjectionBackfill -- --connection <postgres-conn-string>");
    Console.Error.WriteLine("       (or set POSTGRES_CONNECTION_STRING env var)");
    return 2;
}

// S22 deploy date — ADR-018 (transactional outbox) shipped on commit a278f34
// on 2026-05-05. Any event whose `created_at >= this` should have a matching
// outbox_events row; if the LEFT JOIN comes back NULL for such an event, we
// warn-log because that's anomalous (manual event insertion, mid-tx crash
// before the outbox row landed, etc.). Pre-S22 events legitimately have no
// outbox row and we silently fall back to stream_version for ordering.
const string S22DeployDate = "2026-05-05";
var s22DeployUtc = DateTime.SpecifyKind(
    DateTime.Parse(S22DeployDate, System.Globalization.CultureInfo.InvariantCulture),
    DateTimeKind.Utc);

// -----------------------------------------------------------------------------
// SQL: stream-aligned read of TimeEntryRegistered + AbsenceRegistered events
// joined to their outbox row (LEFT JOIN — pre-S22 events have no outbox).
// Ordered by (stream_id, stream_version) so per-stream replay determinism is
// preserved per ADR-016 D10.
// -----------------------------------------------------------------------------
const string SelectSql = @"
    SELECT
        events.event_id,
        events.event_type,
        events.data,
        events.stream_version,
        outbox_events.outbox_id,
        events.stored_at
    FROM events
    LEFT JOIN outbox_events ON events.event_id = outbox_events.event_id
    WHERE events.event_type IN ('TimeEntryRegistered', 'AbsenceRegistered')
    ORDER BY events.stream_id, events.stream_version
";

const string InsertTimeSql = @"
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

const string InsertAbsSql = @"
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

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

// RepeatableRead: a snapshot of `events` + `outbox_events` taken at the start
// of the tx — sufficient for a backfill since (a) the LEFT JOIN is read-only
// and (b) idempotent ON CONFLICT INSERTs against the projection are tolerant
// of concurrent sync writes from the live POST handlers. SERIALIZABLE would
// be overkill (no read-modify-write on rows we're inserting; PRIMARY KEY
// conflict resolution is atomic in Postgres regardless of isolation level).
await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);

// Pre-stage the INSERT commands so we don't pay the prepare cost on every row.
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

// Buffer rows from the SELECT before issuing INSERTs. We can't run INSERT
// statements while a DataReader is open on the same connection in Npgsql.
var rows = new List<(Guid EventId, string EventType, string Data, int StreamVersion, long? OutboxId, DateTime StoredAt)>();
await using (var selectCmd = new NpgsqlCommand(SelectSql, conn, tx))
await using (var reader = await selectCmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
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

long scanned = 0;
long insertedTime = 0;
long insertedAbs = 0;
long conflictsTime = 0;
long conflictsAbs = 0;
long fallbackWarns = 0;
long unknownEventTypes = 0;

foreach (var row in rows)
{
    scanned++;

    // Resolve outbox_id: prefer the real outbox row; fall back to stream_version
    // for pre-S22 events. Warn-log post-S22 events that surprisingly have no
    // outbox row (anomalous in steady state per ADR-018).
    long outboxId;
    if (row.OutboxId.HasValue)
    {
        outboxId = row.OutboxId.Value;
    }
    else
    {
        outboxId = row.StreamVersion;
        if (row.StoredAt >= s22DeployUtc)
        {
            fallbackWarns++;
            Console.Error.WriteLine(
                $"WARN: post-S22 event {row.EventId} (stored_at={row.StoredAt:O}) has no matching outbox_events row; falling back to stream_version={row.StreamVersion}. This is anomalous in steady state.");
        }
    }

    IDomainEvent domainEvent;
    try
    {
        domainEvent = EventSerializer.Deserialize(row.EventType, row.Data);
    }
    catch (InvalidOperationException ex)
    {
        // Should be impossible given the WHERE clause restricts to two known
        // types, but if EventSerializer's map drifts we want a clear signal
        // rather than a swallowed error.
        unknownEventTypes++;
        Console.Error.WriteLine($"WARN: event {row.EventId} type={row.EventType}: {ex.Message}");
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
        // Normalize OccurredAt to UTC — DomainEventBase defaults to DateTime.UtcNow,
        // but DB-roundtripped events may come back as Unspecified-kind from JSON.
        // Postgres timestamptz wants a UTC value; DateTime.SpecifyKind is safe
        // because the event store always stores UTC per ADR-005.
        insertTimeCmd.Parameters["occurredAt"].Value = te.OccurredAt.Kind == DateTimeKind.Utc
            ? te.OccurredAt
            : DateTime.SpecifyKind(te.OccurredAt, DateTimeKind.Utc);
        insertTimeCmd.Parameters["actorId"].Value = (object?)te.ActorId ?? DBNull.Value;
        insertTimeCmd.Parameters["actorRole"].Value = (object?)te.ActorRole ?? DBNull.Value;
        insertTimeCmd.Parameters["correlationId"].Value = (object?)te.CorrelationId ?? DBNull.Value;
        insertTimeCmd.Parameters["outboxId"].Value = outboxId;

        var affected = await insertTimeCmd.ExecuteNonQueryAsync();
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

        var affected = await insertAbsCmd.ExecuteNonQueryAsync();
        if (affected == 1) insertedAbs++;
        else conflictsAbs++;
    }
    // Any other type is impossible given the SELECT WHERE clause; the
    // unknown-type branch above already handles deserialization mismatches.
}

await tx.CommitAsync();

Console.WriteLine($"Scanned: {scanned}");
Console.WriteLine($"time_entries_projection: inserted={insertedTime}, conflicts={conflictsTime}");
Console.WriteLine($"absences_projection: inserted={insertedAbs}, conflicts={conflictsAbs}");
Console.WriteLine($"stream_version fallback warnings: {fallbackWarns}");
if (unknownEventTypes > 0)
{
    Console.WriteLine($"unknown event types skipped: {unknownEventTypes}");
}
return 0;
