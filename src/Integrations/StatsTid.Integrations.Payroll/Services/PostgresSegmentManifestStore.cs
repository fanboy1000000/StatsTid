using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S146 / TASK-14604 (QUAL-205; rulings R-21, R-22) — the production <see cref="ISegmentManifestStore"/>: the
/// manifest travels through the ADR-018 transactional outbox, like every other Payroll event.
///
/// <para>
/// <b>Plain language.</b> The project has one designed way to write a domain event atomically with a state
/// change: enqueue it in an <c>outbox_events</c> row inside the same database transaction as the state change,
/// and let the per-service publisher copy it into the event store afterwards (ADR-018 D3/D4). Payroll's export
/// and correction events already travel that way. The manifest's writer predated the outbox and appended
/// straight to the event store on its own connection; this class moves it onto the outbox, so the manifest
/// event, its projection row and the legacy summary event commit together or not at all.
/// </para>
///
/// <para>
/// <b>Composition (R-21 (c)).</b> <see cref="DbConnectionFactory"/> for the one connection and transaction,
/// <see cref="IOutboxEnqueue"/> for the two in-transaction enqueues, and <see cref="IEventStore"/> for the
/// load path's event fallback only: this class never appends to the event store. No Infrastructure method was
/// added and neither <see cref="IEventStore"/> nor <c>PostgresEventStore</c> changed.
/// </para>
///
/// <para>
/// <b>What "provable" means after the commit (R-22).</b> The outbox rows and the projection row are durable in
/// one transaction. The publisher then delivers each event to <c>events</c> at least once and in per-stream
/// order (ADR-018 D4/D5), the guarantee every other Payroll event has. Between the commit and the publisher's
/// drain the event is not yet in <c>events</c>; the projection is, which is why a replay reads the projection
/// first (ADR-018 D13).
/// </para>
/// </summary>
public sealed class PostgresSegmentManifestStore : ISegmentManifestStore
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly IOutboxEnqueue _outbox;
    private readonly IEventStore _eventStore;
    private readonly ILogger<PostgresSegmentManifestStore> _logger;

    /// <param name="connectionFactory">The Payroll database.</param>
    /// <param name="outbox">The in-transaction enqueue surface (Payroll's outbox partition).</param>
    /// <param name="eventStore">Read only: the load path's fallback when the projection has no row.</param>
    /// <param name="logger">Optional; the host injects one. The load path's warnings are written here.</param>
    public PostgresSegmentManifestStore(
        DbConnectionFactory connectionFactory,
        IOutboxEnqueue outbox,
        IEventStore eventStore,
        ILogger<PostgresSegmentManifestStore>? logger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? NullLogger<PostgresSegmentManifestStore>.Instance;
    }

    /// <summary>
    /// One connection, one transaction, in ADR-018 D13's order: (1) the <see cref="SegmentManifestCreated"/>
    /// event enqueued on <c>segment-manifest-{manifestId}</c>; (2) the <c>segment_manifests</c> insert (SQL
    /// unchanged since S145); (3) the legacy <see cref="PeriodCalculationCompleted"/> enqueued on its
    /// <c>period-calc-{employeeId}-{periodStart:yyyy-MM-dd}</c> stream; (4) commit. Any failure before the commit
    /// throws, and the transaction is rolled back when it is disposed uncommitted, so nothing is left behind.
    ///
    /// <para>
    /// <b>The enqueue-first order is a convention here.</b> D13 enqueues first so a projection can store the
    /// returned <c>outbox_id</c> as its ordering column. <c>segment_manifests</c> is keyed by
    /// <c>manifest_id</c>, one row per manifest, and gains no <c>outbox_id</c> column (R-21), so the returned id
    /// is not used; the order is kept so this writer reads like every other outbox writer.
    /// </para>
    ///
    /// <para>
    /// <b>A failed commit.</b> If the commit itself fails (for example the connection drops while committing),
    /// the database may or may not have committed. This throws either way, and the calculation is refused. The
    /// safe direction: at worst a manifest exists for a calculation that was never exported or corrected.
    /// </para>
    /// </summary>
    public async Task PersistAsync(
        SegmentManifestCreated manifestEvent,
        SegmentManifest manifest,
        PeriodCalculationCompleted legacyEvent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifestEvent);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(legacyEvent);

        // A programmer error, never a month: the event and the projection row must name the same manifest, or the
        // audit chain would join a row to an event of another calculation.
        if (manifestEvent.ManifestId != manifest.ManifestId)
        {
            throw new InvalidOperationException(
                $"PostgresSegmentManifestStore.PersistAsync was handed a SegmentManifestCreated for manifest " +
                $"{manifestEvent.ManifestId} and a SegmentManifest for manifest {manifest.ManifestId}; they must be the " +
                "same manifest. Nothing was written.");
        }

        // Serialized before the connection opens, so a serialization fault holds no transaction open.
        var segmentsJson = JsonSerializer.Serialize(manifest.Segments, PeriodCalculationService.JsonOptions);
        var planInputsJson = PlanInputsJsonOf(manifestEvent);

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1. The manifest event, enqueued first (ADR-018 D13's order). The event is the audit-of-record (ADR-016
        //    D10): since S145 / TASK-14513 it carries the frozen PlanInputs, and the projection is rebuildable from
        //    it once the publisher has drained it into `events`.
        _ = await _outbox.EnqueueAndReturnIdAsync(
            conn, tx, PeriodCalculationService.ManifestStreamId(manifest.ManifestId), manifestEvent, ct);

        // 2. The segment_manifests projection row, in the same transaction: what a replay reads first, so a replay
        //    right after the calculation finds the manifest without waiting for the publisher.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO segment_manifests
                (manifest_id, period_start, period_end, employee_id, calculation_kind,
                 boundary_cause_summary, created_at, segments_jsonb, plan_inputs_jsonb)
            VALUES
                (@manifestId, @periodStart, @periodEnd, @employeeId, @calculationKind,
                 @boundaryCauseSummary, @createdAt, @segmentsJson::jsonb, @planInputsJson::jsonb)
            ON CONFLICT (manifest_id) DO NOTHING
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("manifestId", manifest.ManifestId);
            cmd.Parameters.AddWithValue("periodStart", manifest.PeriodStart);
            cmd.Parameters.AddWithValue("periodEnd", manifest.PeriodEnd);
            cmd.Parameters.AddWithValue("employeeId", manifest.EmployeeId);
            cmd.Parameters.AddWithValue("calculationKind", manifest.CalculationKind);
            cmd.Parameters.AddWithValue("boundaryCauseSummary", manifest.BoundaryCauseSummary.ToArray());
            cmd.Parameters.AddWithValue("createdAt", manifest.CreatedAt);
            cmd.Parameters.AddWithValue("segmentsJson", NpgsqlTypes.NpgsqlDbType.Text, segmentsJson);
            cmd.Parameters.AddWithValue("planInputsJson", NpgsqlTypes.NpgsqlDbType.Text, (object?)planInputsJson ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 3. The legacy PeriodCalculationCompleted (the pre-S20 audit chain's period-result counts), in the SAME
        //    transaction: written exactly when the manifest is, never without it (R-18, by construction).
        await _outbox.EnqueueAsync(
            conn, tx,
            PeriodCalculationService.PeriodCalculationCompletedStreamId(legacyEvent.EmployeeId, legacyEvent.PeriodStart),
            legacyEvent, ct);

        // 4. Commit: all three, or (on any throw above, the transaction disposed uncommitted) none.
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// S145 / TASK-14513 — the <c>plan_inputs_jsonb</c> value: exactly the <c>planInputs</c> object as
    /// <see cref="EventSerializer"/> writes it on <paramref name="manifestEvent"/> (camelCase, null members
    /// omitted), or <c>null</c> when the event carries none.
    ///
    /// <para>
    /// <b>Why it is taken from the serialized event (TASK-14509's shape contract).</b> The projection rebuild and
    /// its SQL twin copy <c>data-&gt;'planInputs'</c> verbatim into this column, so a live-written row and a
    /// rebuilt one must hold the same JSON; reading it out of the event's own serialization makes that true by
    /// construction instead of keeping a second copy of the serializer's options in step. (The segments column
    /// keeps its own options: its documented null-snapshot residual, QUAL-146, is not this column's.)
    /// </para>
    /// </summary>
    private static string? PlanInputsJsonOf(SegmentManifestCreated manifestEvent)
    {
        if (manifestEvent.PlanInputs is null)
            return null;

        using var serialized = JsonDocument.Parse(EventSerializer.Serialize(manifestEvent));
        return serialized.RootElement.TryGetProperty("planInputs", out var planInputs)
            && planInputs.ValueKind == JsonValueKind.Object
                ? planInputs.GetRawText()
                : throw new InvalidOperationException(
                    "SegmentManifestCreated serialized without its planInputs object although the event carries " +
                    "PlanInputs; the projection row would lose the frozen inputs. " +
                    $"ManifestId={manifestEvent.ManifestId}.");
    }

    /// <summary>
    /// The raw values of one <c>segment_manifests</c> row, read before anything is decoded (S145 ruling R-17):
    /// the scalars as typed by the reader, and the two JSON columns as text (<c>plan_inputs_jsonb</c> is
    /// <c>null</c> when the column is NULL).
    /// </summary>
    private sealed record ManifestProjectionRow(
        Guid ManifestId,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string EmployeeId,
        string CalculationKind,
        List<string> BoundaryCauseSummary,
        DateTimeOffset CreatedAt,
        string SegmentsJson,
        string? PlanInputsJson);

    /// <inheritdoc />
    /// <remarks>
    /// Moved from <c>PeriodCalculationService.LoadManifestAsync</c> by S146 / TASK-14604 without a change of
    /// behaviour: the same projection SQL, the same fallback stream, the same found-row rule (R-17).
    /// </remarks>
    public async Task<SegmentManifest?> TryLoadAsync(Guid manifestId, CancellationToken ct = default)
    {
        // 1. Try the projection table first — single-row indexed lookup. Since S145 / TASK-14513 the row's
        //    plan_inputs_jsonb is read too. Three outcomes (S145 rulings R-14 vii, R-17):
        //    - no row, or the projection unreachable (a connection failure, a missing table, a SQL error)
        //      → the event (step 2 below);
        //    - a found row with a NULL slot → refused by the replay (R-14 vii): a manifest without frozen inputs;
        //    - a found row with an undecodable slot → refused here (R-17), by the same name.
        //    A found row never falls back to the event for the slot: the row is what the projection says was
        //    calculated, and a rebuild (not a replay) is what restores a damaged row. That is why the catch-all
        //    below wraps ONLY the SQL read into raw values; the decoding happens after it, where no catch can
        //    turn a damaged row into a silent event substitution.
        //    A SegmentManifestCreated whose own planInputs cannot be decoded fails the stream read and surfaces
        //    as "not found / cannot replay": a store-level fault in an append-only record.
        ManifestProjectionRow? row = null;
        try
        {
            await using var conn = _connectionFactory.Create();
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(
                """
                SELECT manifest_id, period_start, period_end, employee_id, calculation_kind,
                       boundary_cause_summary, created_at, segments_jsonb, plan_inputs_jsonb
                FROM segment_manifests
                WHERE manifest_id = @manifestId
                """, conn);
            cmd.Parameters.AddWithValue("manifestId", manifestId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                row = new ManifestProjectionRow(
                    ManifestId: reader.GetGuid(0),
                    PeriodStart: DateOnly.FromDateTime(reader.GetDateTime(1)),
                    PeriodEnd: DateOnly.FromDateTime(reader.GetDateTime(2)),
                    EmployeeId: reader.GetString(3),
                    CalculationKind: reader.GetString(4),
                    BoundaryCauseSummary: ((string[])reader.GetValue(5)).ToList(),
                    CreatedAt: reader.GetFieldValue<DateTimeOffset>(6),
                    SegmentsJson: reader.GetString(7),
                    PlanInputsJson: reader.IsDBNull(8) ? null : reader.GetString(8));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A fault in the read means the projection could not be consulted; no decoding has happened yet,
            // so nothing here can be a damaged row. A row already read stays read: a fault after it (closing
            // the connection) does not hand a found row to the event (R-17). A cancelled replay propagates
            // as cancellation, not as "manifest not found".
            _logger.LogWarning(ex,
                "Projection lookup for manifest {ManifestId} failed; falling back to event-replay.",
                manifestId);
        }

        if (row is not null)
            return DecodeProjectionRow(row);

        // 2. Event-replay fallback — read SegmentManifestCreated events on the manifest's stream and rehydrate the
        //    manifest. This path covers the projection-truncated / projection-not-yet-rebuilt case and is the same
        //    path TASK-2011's rebuild script uses. Since S146 the event reaches `events` through the outbox
        //    publisher, so between a calculation's commit and the publisher's drain this misses (the projection row,
        //    committed with the outbox row, is what a replay finds then): the latency ADR-018 D13 accepts for every
        //    event.
        try
        {
            var events = await _eventStore.ReadStreamAsync(PeriodCalculationService.ManifestStreamId(manifestId), ct);
            var created = events.OfType<SegmentManifestCreated>().FirstOrDefault();
            if (created is not null)
            {
                return new SegmentManifest(
                    ManifestId: created.ManifestId,
                    PeriodStart: created.PeriodStart,
                    PeriodEnd: created.PeriodEnd,
                    EmployeeId: created.EmployeeId,
                    CalculationKind: created.CalculationKind,
                    BoundaryCauseSummary: created.BoundaryCauseSummary,
                    CreatedAt: created.CreatedAt,
                    Segments: created.Segments,
                    PlanInputs: created.PlanInputs);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Event-replay fallback for manifest {ManifestId} failed.", manifestId);
        }

        return null;
    }

    /// <summary>
    /// S145 ruling R-17 — decodes a FOUND <c>segment_manifests</c> row. Runs outside the projection read's
    /// catch-all, so a damaged row fails loudly and is never replaced by the event:
    /// <list type="bullet">
    /// <item><c>segments_jsonb</c> not decodable → <see cref="InvalidOperationException"/> naming the manifest and
    /// the column. The segments are the plan, not frozen inputs, so this is a damaged row, not a named replay
    /// refusal;</item>
    /// <item><c>plan_inputs_jsonb</c> present but not decodable as <see cref="PlanInputs"/> (the wrong shape, for
    /// example <c>[]</c>, or a missing required member, R-18) → logged at Warning with the exception, then refused
    /// <see cref="ReplayRefusedException.ManifestWithoutFrozenInputs"/>;</item>
    /// <item><c>plan_inputs_jsonb</c> NULL (or the JSON literal <c>null</c>) → a manifest without frozen inputs,
    /// which <c>PeriodCalculationService.ReplayAsync</c> refuses by the same name (R-14 vii).</item>
    /// </list>
    /// </summary>
    private SegmentManifest DecodeProjectionRow(ManifestProjectionRow row)
    {
        List<PlannedSegment> segments;
        try
        {
            segments = JsonSerializer.Deserialize<List<PlannedSegment>>(row.SegmentsJson, PeriodCalculationService.JsonOptions)
                ?? new List<PlannedSegment>();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Damaged projection row: segment_manifests.segments_jsonb of manifest {row.ManifestId} could not be " +
                "decoded as the planned segments; rebuild the projection. The replay does not fall back to the " +
                "event for a found row.",
                ex);
        }

        PlanInputs? planInputs = null;
        if (row.PlanInputsJson is not null)
        {
            try
            {
                planInputs = JsonSerializer.Deserialize<PlanInputs>(row.PlanInputsJson, PeriodCalculationService.JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "The segment_manifests row of manifest {ManifestId} holds a plan_inputs_jsonb that cannot be " +
                    "decoded as frozen inputs.",
                    row.ManifestId);
                throw PeriodCalculationService.LogAndBuildReplayRefusal(
                    _logger, ReplayRefusedException.ManifestWithoutFrozenInputs, row.ManifestId, row.EmployeeId,
                    "the projection row's plan_inputs_jsonb could not be decoded as frozen inputs; rebuild the projection");
            }
        }

        return new SegmentManifest(
            ManifestId: row.ManifestId,
            PeriodStart: row.PeriodStart,
            PeriodEnd: row.PeriodEnd,
            EmployeeId: row.EmployeeId,
            CalculationKind: row.CalculationKind,
            BoundaryCauseSummary: row.BoundaryCauseSummary,
            CreatedAt: row.CreatedAt,
            Segments: segments,
            PlanInputs: planInputs);
    }
}
