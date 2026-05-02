using Microsoft.Extensions.Logging;
using Npgsql;

namespace StatsTid.Infrastructure;

/// <summary>
/// Rebuilds the <c>segment_manifests</c> projection table by replaying every
/// <c>SegmentManifestCreated</c> event in the event store. Idempotent.
///
/// <para>
/// Sprint 20 / TASK-2011. Use case: projection drift recovery — if the projection
/// goes out of sync with the event store (table corruption, missed event during
/// deployment, manual edit), an operator runs this to restore from the
/// authoritative event log.
/// </para>
///
/// <para>
/// Idempotency strategy: <c>TRUNCATE</c> then full replay (rather than upsert).
/// Chosen because the event store is the source of truth and replay is cheap
/// relative to the projected row count, and TRUNCATE sweeps away orphan rows
/// that would survive an upsert.
/// </para>
///
/// <para>
/// Concurrency: the rebuild transaction runs at <c>SERIALIZABLE</c> isolation so
/// any concurrent <c>SegmentManifestCreated</c> insert into <c>events</c> during
/// the rebuild produces a <c>40001 serialization_failure</c> rather than being
/// silently missed by the replay. On that failure the projection is unchanged
/// (the TRUNCATE is rolled back) and the operator should quiesce writers and
/// re-run. This script is intended for maintenance-window use.
/// </para>
///
/// <para>
/// JSON shape (camelCase via <see cref="EventSerializer"/>) is stable, so the
/// rebuild runs as pure SQL using <c>jsonb</c> operators — no C# deserialization
/// is required and we never have to instantiate the <c>SegmentManifestCreated</c>
/// type to project it. The same SQL is mirrored in
/// <c>docker/postgres/scripts/rebuild_segment_manifests.sql</c> for ops who want
/// to run it directly via <c>psql</c>; the two MUST be kept in sync (covered by
/// TASK-2012's projection-rebuild regression test which calls
/// <see cref="RebuildAsync"/>).
/// </para>
///
/// <para>
/// Headless invocation (e.g. from xUnit regression tests):
/// <code>
/// var rebuilt = await SegmentManifestProjectionRebuilder.RebuildAsync(dbFactory, logger);
/// </code>
/// </para>
/// </summary>
public static class SegmentManifestProjectionRebuilder
{
    /// <summary>
    /// SQL projection statement. Mirrors <c>docker/postgres/scripts/rebuild_segment_manifests.sql</c>
    /// — KEEP IN SYNC. Last-write-wins per <c>manifest_id</c>, ordered by
    /// <c>events.global_position DESC</c> so <c>DISTINCT ON</c> picks the most
    /// recently appended <c>SegmentManifestCreated</c> for any given manifest id
    /// (duplicates should be impossible by ADR-016 D10 construction; the
    /// integrity-check query below surfaces any that slip through).
    /// </summary>
    /// <remarks>
    /// Field mapping from <c>events.data</c> (camelCase JSON, ADR-005) to
    /// <c>segment_manifests</c> columns:
    /// <list type="bullet">
    ///   <item><c>manifest_id</c>            ← <c>data-&gt;&gt;'manifestId'</c> (cast to <c>uuid</c>)</item>
    ///   <item><c>period_start</c>           ← <c>data-&gt;&gt;'periodStart'</c></item>
    ///   <item><c>period_end</c>             ← <c>data-&gt;&gt;'periodEnd'</c></item>
    ///   <item><c>employee_id</c>            ← <c>data-&gt;&gt;'employeeId'</c> (no cast; column is TEXT per ADR-016 D10 amendment 2026-05-01)</item>
    ///   <item><c>calculation_kind</c>       ← <c>data-&gt;&gt;'calculationKind'</c></item>
    ///   <item><c>boundary_cause_summary</c> ← <c>data-&gt;'boundaryCauseSummary'</c> (jsonb → text[])</item>
    ///   <item><c>created_at</c>             ← <c>data-&gt;&gt;'createdAt'</c></item>
    ///   <item><c>segments_jsonb</c>         ← <c>data-&gt;'segments'</c></item>
    /// </list>
    /// </remarks>
    private const string ReplaySql = @"
        TRUNCATE TABLE segment_manifests;

        INSERT INTO segment_manifests (
            manifest_id,
            period_start,
            period_end,
            employee_id,
            calculation_kind,
            boundary_cause_summary,
            created_at,
            segments_jsonb
        )
        SELECT DISTINCT ON (manifest_id)
            manifest_id,
            period_start,
            period_end,
            employee_id,
            calculation_kind,
            boundary_cause_summary,
            created_at,
            segments_jsonb
        FROM (
            SELECT
                (data->>'manifestId')::uuid                     AS manifest_id,
                (data->>'periodStart')::date                    AS period_start,
                (data->>'periodEnd')::date                      AS period_end,
                data->>'employeeId'                             AS employee_id,
                data->>'calculationKind'                        AS calculation_kind,
                ARRAY(
                    SELECT jsonb_array_elements_text(
                        COALESCE(data->'boundaryCauseSummary', '[]'::jsonb)
                    )
                )                                               AS boundary_cause_summary,
                (data->>'createdAt')::timestamptz               AS created_at,
                COALESCE(data->'segments', '[]'::jsonb)         AS segments_jsonb,
                global_position
            FROM events
            WHERE event_type = 'SegmentManifestCreated'
        ) src
        ORDER BY manifest_id, global_position DESC;
    ";

    /// <summary>
    /// Counts <c>manifest_id</c> values that appear more than once in
    /// <c>events</c> as <c>SegmentManifestCreated</c>. By ADR-016 D10 construction
    /// this should always be 0; a non-zero result signals a buggy emitter and is
    /// logged at WARNING level so it doesn't get masked by the rebuild's
    /// last-write-wins dedupe.
    /// </summary>
    // The manifest id lives inside events.data JSON (events has no physical
    // manifest_id column); extract it the same way RebuildSql does.
    private const string DuplicateCheckSql = @"
        SELECT COUNT(*) AS duplicate_manifest_count
        FROM (
            SELECT (data->>'manifestId')::uuid AS manifest_id
            FROM events
            WHERE event_type = 'SegmentManifestCreated'
            GROUP BY (data->>'manifestId')::uuid
            HAVING COUNT(*) > 1
        ) AS duplicates;
    ";

    /// <summary>
    /// Returns up to 5 <c>manifest_id</c> values with duplicate
    /// <c>SegmentManifestCreated</c> events, for ops triage.
    /// </summary>
    private const string DuplicateSampleSql = @"
        SELECT (data->>'manifestId')::uuid AS manifest_id
        FROM events
        WHERE event_type = 'SegmentManifestCreated'
        GROUP BY (data->>'manifestId')::uuid
        HAVING COUNT(*) > 1
        ORDER BY (data->>'manifestId')::uuid
        LIMIT 5;
    ";

    /// <summary>
    /// Truncates <c>segment_manifests</c> and replays every <c>SegmentManifestCreated</c>
    /// event in the event store into it. Returns the count of manifests rebuilt for
    /// ops sanity-checking. Logs pre/post row counts (with delta), source-event count,
    /// and any duplicate-emitter detections.
    /// </summary>
    /// <param name="dbFactory">Connection factory for the event store + projection DB.</param>
    /// <param name="logger">Used for the structured summary line on completion.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of rows in <c>segment_manifests</c> after the rebuild.</returns>
    public static async Task<int> RebuildAsync(
        DbConnectionFactory dbFactory,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(logger);

        await using var conn = dbFactory.Create();
        await conn.OpenAsync(ct);

        // Count source events so the summary line shows replay coverage.
        await using var sourceCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM events WHERE event_type = 'SegmentManifestCreated'",
            conn);
        var sourceEventCount = (long)(await sourceCmd.ExecuteScalarAsync(ct))!;

        // SERIALIZABLE: a concurrent SegmentManifestCreated insert during the
        // rebuild produces a 40001 serialization_failure on COMMIT rather than
        // being silently missed. Operator quiesces writers and re-runs.
        // The TRUNCATE + INSERT are wrapped in a single transaction so a
        // failure mid-replay leaves the projection in its prior state.
        await using var tx = await conn.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

        // Pre-rebuild row count for delta visibility (read inside the same
        // transaction so the delta is consistent with the rebuild's view of
        // the table).
        await using var previousCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM segment_manifests",
            conn,
            tx);
        var previousRowCount = (long)(await previousCmd.ExecuteScalarAsync(ct))!;

        await using (var replayCmd = new NpgsqlCommand(ReplaySql, conn, tx))
        {
            await replayCmd.ExecuteNonQueryAsync(ct);
        }

        await using var countCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM segment_manifests",
            conn,
            tx);
        var rebuiltRowCount = (long)(await countCmd.ExecuteScalarAsync(ct))!;

        // Duplicate-manifest integrity check — runs in the same transaction so
        // it sees exactly the events the replay saw.
        await using var duplicateCmd = new NpgsqlCommand(DuplicateCheckSql, conn, tx);
        var duplicateCount = (long)(await duplicateCmd.ExecuteScalarAsync(ct))!;

        var sampleIds = Array.Empty<Guid>();
        if (duplicateCount > 0)
        {
            var collected = new List<Guid>(capacity: 5);
            await using var sampleCmd = new NpgsqlCommand(DuplicateSampleSql, conn, tx);
            await using var reader = await sampleCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                collected.Add(reader.GetGuid(0));
            }
            sampleIds = collected.ToArray();
        }

        await tx.CommitAsync(ct);

        var delta = rebuiltRowCount - previousRowCount;
        logger.LogInformation(
            "segment_manifests projection rebuilt: {RebuiltRows} rows from {SourceEvents} SegmentManifestCreated events; previous row count {PreviousRows} (delta {Delta})",
            rebuiltRowCount,
            sourceEventCount,
            previousRowCount,
            delta);

        if (duplicateCount > 0)
        {
            logger.LogWarning(
                "segment_manifests rebuild detected {DuplicateCount} manifest_id values with duplicate SegmentManifestCreated events; last-write-wins applied. Sample manifest ids: {SampleManifestIds}",
                duplicateCount,
                sampleIds);
        }

        return (int)rebuiltRowCount;
    }
}
