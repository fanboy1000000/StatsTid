using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S27 / TASK-2710 Slot 5 — Projection backfill idempotency.
///
/// <para>
/// Pins the load-bearing invariant of <see cref="ProjectionBackfillService"/>'s
/// <c>ON CONFLICT (event_id) DO NOTHING</c> idempotency: re-running the backfill against
/// a database whose projections are already populated (e.g. from a prior backfill run, or
/// from sync-in-tx writes that landed concurrently with the backfill query) MUST NOT
/// duplicate rows. Per <see cref="ProjectionBackfillService"/>'s class doc, no TRUNCATE is
/// performed (live POST handlers may be racing the backfill in production), so idempotency
/// is the correctness guarantee.
/// </para>
///
/// <para>
/// Post-S27 Step 7a cycle 1 BLOCKER fix: invokes the production
/// <see cref="ProjectionBackfillService"/> directly (single source of truth) rather than
/// duplicating the SELECT/INSERT SQL inline. This eliminates the third site that had a
/// drift surface flagged by the TASK-2710 Reviewer (NOTE-2). The console app at
/// <c>tools/ProjectionBackfill</c> and Backend.Api startup also delegate to the same service.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProjectionBackfillTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private ProjectionBackfillService _service = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ProjectionSchemaTestFixture.ApplyAsync(_harness.ConnectionString);
        _service = new ProjectionBackfillService(
            _harness.Factory,
            NullLogger<ProjectionBackfillService>.Instance);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// Seed N events directly into <c>events</c> + <c>outbox_events</c> (mark them as
    /// published with <c>stream_version</c> set). Run the production backfill service;
    /// assert N rows in the projection table. Re-run; assert STILL N rows (no duplicates
    /// from <c>ON CONFLICT (event_id) DO NOTHING</c>).
    /// </summary>
    [Fact]
    public async Task Backfill_TimeEntries_IsIdempotent_ReRunDoesNotDuplicate()
    {
        const int seedCount = 5;
        var employeeId = "EMP_BF_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";

        // Seed N TimeEntryRegistered events directly into events + outbox_events
        // (already-published rows representing pre-S27 history that needs backfilling).
        var seededEventIds = new List<Guid>();
        for (int i = 0; i < seedCount; i++)
        {
            var evt = new TimeEntryRegistered
            {
                EmployeeId = employeeId,
                Date = new DateOnly(2026, 5, 1).AddDays(i),
                Hours = 7.4m,
                TaskId = $"PROJ-BF-{i}",
                ActivityType = "NORMAL",
                AgreementCode = "HK",
                OkVersion = "OK24",
            };
            var streamVersion = i + 1;
            await SeedEventAndOutboxRowAsync(evt, streamId, streamVersion);
            seededEventIds.Add(evt.EventId);
        }

        // Run the production backfill via the canonical service (single source of truth).
        var run1 = await _service.RunAsync();
        Assert.Equal(seedCount, run1.InsertedTime);
        Assert.Equal(0, run1.ConflictsTime);

        // Verify projection has N rows for this employee.
        var rowCountAfterRun1 = await CountProjectionRowsAsync("time_entries_projection", employeeId);
        Assert.Equal((long)seedCount, rowCountAfterRun1);

        // Re-run — every row should ON CONFLICT DO NOTHING.
        var run2 = await _service.RunAsync();
        Assert.Equal(0, run2.InsertedTime);
        Assert.Equal(seedCount, run2.ConflictsTime);

        // Row count UNCHANGED — no duplicates.
        var rowCountAfterRun2 = await CountProjectionRowsAsync("time_entries_projection", employeeId);
        Assert.Equal((long)seedCount, rowCountAfterRun2);

        // Each seeded event_id appears exactly once in the projection.
        foreach (var evtId in seededEventIds)
        {
            var c = await CountProjectionByEventIdAsync("time_entries_projection", evtId);
            Assert.Equal(1L, c);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════════
    // S145 / TASK-14501 (A0, refinement R22) — a rebuild from `events` reproduces the current
    // set of a re-saved Skema cell, with supersession decided in outbox_id order.
    //
    // Plain language. A Skema "cell" is one employee + one day + one project. Saving a cell
    // twice leaves two events in the log, but the product's meaning is that the LATER save
    // replaces the earlier one: only one row per cell may count. The projection table is a
    // read-model rebuilt from those events, so the rebuild must reach the same answer the live
    // save path reaches: the earlier Skema row is marked "superseded by <later outbox_id>" and
    // the later one is the only current Skema row. A row written by POST /api/time-entries
    // (source TIME_POST, or an old event with no source at all) is additive and is never
    // replaced. These pins seed `events` directly (not through the Skema endpoint), so no
    // live-path change can reach them: they test the rebuild alone.
    // ════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The cell every S145 backfill fact uses: a WEEKDAY (Monday), one task.</summary>
    private static readonly DateOnly CellDate = new(2026, 5, 4);
    private const string CellTask = "PROJ-S145-CELL";

    /// <summary>One projection row of the cell, as read back by SQL.</summary>
    private sealed record CellRow(Guid EventId, decimal Hours, string Source, long? SupersededBy, long OutboxId);

    /// <summary>The three events seeded for the cell, in outbox order.</summary>
    private sealed record SeededCell(
        (Guid EventId, long OutboxId) SkemaFirst,
        (Guid EventId, long OutboxId) TimePost,
        (Guid EventId, long OutboxId) SkemaResave);

    /// <summary>
    /// Re-save, then rebuild. Seeds one cell as it would sit in the log after: a Skema save (7.4 h),
    /// a <c>POST /api/time-entries</c> for the same day and task (2.0 h, no <c>source</c> in the
    /// payload, the shape of every pre-S145 event), and a Skema re-save (8.0 h). The Time-POST event
    /// sits BETWEEN the two Skema events on purpose: a mark that ignored the source would then
    /// either supersede the Time-POST row (Skema insert reaching back) or stamp the earlier Skema
    /// row with the Time-POST outbox id (Time-POST insert reaching back), and either shows below.
    ///
    /// <para><b>Expected (spec: refinement A0 constraints 1, 2 and 4, R22).</b> After one backfill
    /// run: three rows (superseded rows are retained, so a replay by event id still finds them);
    /// exactly one CURRENT Skema row, the later one (8.0 h); the earlier Skema row carries
    /// <c>superseded_by_outbox_id</c> = the later Skema row's <c>outbox_id</c>; the Time-POST row is
    /// current and unmarked.</para>
    ///
    /// <para><b>Red conditions (Docker; compiles today, so its RED is argued here).</b>
    /// (1) TODAY's backfill never reads <c>source</c> and never marks: it inserts all three rows
    /// with the column default <c>TIME_POST</c>, so the fact fails on "exactly one current SKEMA
    /// row" with 0 found. (2) <b>MD-11</b> (<c>ProjectionBackfillService</c>, the per-event mark step
    /// removed, with <c>source</c> still stamped): the second Skema insert meets
    /// <c>ux_time_entries_proj_current_skema_cell</c> and <c>RunAsync</c> throws a unique violation
    /// (23505), which <see cref="RunBackfillAsync"/> turns into the named assertion failure "the
    /// backfill must complete". Which of the two shows depends on whether the implementation has
    /// merged; both are red. Not reached by MD-12 or MD-13 (live-path edits; the rebuild has its
    /// own mark step).</para>
    /// </summary>
    [Fact]
    public async Task Backfill_AfterSkemaResave_RebuildsCurrentSet_SupersessionInOutboxOrder()
    {
        var employeeId = "EMP_BF_S145_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var seeded = await SeedSkemaResaveCellAsync(employeeId);

        await RunBackfillAsync();

        AssertRebuiltCell(await ReadCellRowsAsync(employeeId), seeded);
    }

    /// <summary>
    /// The same cell, rebuilt twice. The backfill never truncates and inserts with
    /// <c>ON CONFLICT (event_id) DO NOTHING</c>, so the second run inserts nothing and must leave
    /// the supersession marks exactly where the first run put them. The full assertion set runs
    /// after BOTH runs and the two row sets must be identical, so the fact cannot pass on "no marks
    /// at all": a run that never marks fails the first call of the assertions, a second run that
    /// unmarks or re-marks fails the second.
    ///
    /// <para><b>Red conditions (Docker; compiles today).</b> The same two as
    /// <see cref="Backfill_AfterSkemaResave_RebuildsCurrentSet_SupersessionInOutboxOrder"/>: TODAY
    /// the first assertion pass fails (0 current SKEMA rows); <b>MD-11</b> (mark step removed) makes
    /// the first run throw 23505. A second-run-only regression (a re-run that clears or rewrites the
    /// marks) is not one of the catalogued mutations; the final row-set equality is what would catch
    /// it.</para>
    /// </summary>
    [Fact]
    public async Task Backfill_AfterSkemaResave_ReRun_LeavesSupersessionMarksUnchanged()
    {
        var employeeId = "EMP_BF_S145_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var seeded = await SeedSkemaResaveCellAsync(employeeId);

        await RunBackfillAsync();
        var afterFirst = await ReadCellRowsAsync(employeeId);
        AssertRebuiltCell(afterFirst, seeded);

        var second = await RunBackfillAsync();
        // The idempotency contract this fact stands on (the existing fact pins it for the
        // untouched case): every row conflicts on event_id, nothing is inserted.
        Assert.Equal(0, second.InsertedTime);
        Assert.Equal(3, second.ConflictsTime);

        var afterSecond = await ReadCellRowsAsync(employeeId);
        AssertRebuiltCell(afterSecond, seeded);
        Assert.Equal(afterFirst, afterSecond);
    }

    /// <summary>
    /// Runs the production backfill and fails with a message that names the likely cause when it
    /// throws. A thrown unique violation on the Skema cell index is what a rebuild without a mark
    /// step produces, so it is reported as an assertion, not as an unexplained exception.
    /// </summary>
    private async Task<ProjectionBackfillResult> RunBackfillAsync()
    {
        ProjectionBackfillResult? result = null;
        var thrown = await Record.ExceptionAsync(async () => result = await _service.RunAsync());
        Assert.True(thrown is null,
            "The backfill must complete. A unique violation (23505) on " +
            "ux_time_entries_proj_current_skema_cell means the rebuild inserted a second CURRENT " +
            "Skema row for one cell without superseding the first (the per-event mark step is missing). " +
            $"Threw: {thrown}");
        return result!;
    }

    private void AssertRebuiltCell(List<CellRow> rows, SeededCell seeded)
    {
        Assert.Equal(3, rows.Count); // superseded rows are retained: replay by event id still needs them.

        var currentSkema = rows.Where(r => r.Source == "SKEMA" && r.SupersededBy is null).ToList();
        Assert.True(currentSkema.Count == 1,
            $"Exactly one current SKEMA row expected for the cell, found {currentSkema.Count}. Rows: {Describe(rows)}");
        Assert.Equal(seeded.SkemaResave.EventId, currentSkema[0].EventId); // the later outbox_id wins
        Assert.Equal(8.0m, currentSkema[0].Hours);

        var earlier = rows.Single(r => r.EventId == seeded.SkemaFirst.EventId);
        Assert.Equal("SKEMA", earlier.Source);
        Assert.Equal(7.4m, earlier.Hours);
        Assert.Equal(seeded.SkemaResave.OutboxId, earlier.SupersededBy); // marked by the LATER row, in outbox order

        var timePost = rows.Single(r => r.EventId == seeded.TimePost.EventId);
        Assert.Equal("TIME_POST", timePost.Source); // an event with no source is TIME_POST
        Assert.Equal(2.0m, timePost.Hours);
        Assert.Null(timePost.SupersededBy);          // additive: never replaced
    }

    private static string Describe(List<CellRow> rows) =>
        string.Join("; ", rows.Select(r => $"{r.Source} {r.Hours}h outbox={r.OutboxId} supersededBy={r.SupersededBy?.ToString() ?? "null"}"));

    private async Task<SeededCell> SeedSkemaResaveCellAsync(string employeeId)
    {
        var streamId = $"employee-{employeeId}";
        var first = NewCellEvent(employeeId, hours: 7.4m);
        var post = NewCellEvent(employeeId, hours: 2.0m);
        var resave = NewCellEvent(employeeId, hours: 8.0m);

        var firstOutbox = await SeedPayloadAsync(first, SerializeWithSource(first, "SKEMA"), streamId, streamVersion: 1);
        var postOutbox = await SeedPayloadAsync(post, SerializeWithSource(post, source: null), streamId, streamVersion: 2);
        var resaveOutbox = await SeedPayloadAsync(resave, SerializeWithSource(resave, "SKEMA"), streamId, streamVersion: 3);

        // The outbox ids are assigned by BIGSERIAL in insertion order; the facts depend on that order.
        Assert.True(firstOutbox < postOutbox && postOutbox < resaveOutbox,
            "fixture: outbox ids must follow the insertion order");

        return new SeededCell(
            (first.EventId, firstOutbox), (post.EventId, postOutbox), (resave.EventId, resaveOutbox));
    }

    private static TimeEntryRegistered NewCellEvent(string employeeId, decimal hours) => new()
    {
        EmployeeId = employeeId,
        Date = CellDate,
        Hours = hours,
        TaskId = CellTask,
        ActivityType = "NORMAL",
        AgreementCode = "HK",
        OkVersion = "OK24",
    };

    /// <summary>
    /// Serializes the event the way the product does, then stamps <c>source</c> into the payload.
    /// <c>TimeEntryRegistered.Source</c> is created by S145 (TASK-14500), so the property is written
    /// into the JSON rather than set on the type: this file compiles against today's code. The
    /// serializer is camelCase (<c>EventSerializer</c>); the guard below proves that on an existing
    /// property (<c>employeeId</c>) before the lower-case <c>source</c> key is written, so a change of
    /// naming policy fails here with a clear message instead of silently dropping the stamp.
    /// A null <paramref name="source"/> writes nothing: that is every pre-S145 event.
    /// </summary>
    private static string SerializeWithSource(TimeEntryRegistered evt, string? source)
    {
        var node = JsonNode.Parse(EventSerializer.Serialize(evt))!.AsObject();
        Assert.True(node.ContainsKey("employeeId"),
            "EventSerializer is expected to write camelCase property names (employeeId); " +
            "the 'source' key below would then be wrongly cased.");
        if (source is not null)
            node["source"] = source;
        return node.ToJsonString();
    }

    /// <summary>
    /// Inserts one event (<paramref name="dataJson"/> as given) into <c>events</c> and a matching
    /// published <c>outbox_events</c> row, and returns the <c>outbox_id</c> Postgres assigned.
    /// </summary>
    private async Task<long> SeedPayloadAsync(
        TimeEntryRegistered evt, string dataJson, string streamId, int streamVersion)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var ensureCmd = new NpgsqlCommand(
            "INSERT INTO event_streams (stream_id) VALUES (@s) ON CONFLICT DO NOTHING",
            conn, tx))
        {
            ensureCmd.Parameters.AddWithValue("s", streamId);
            await ensureCmd.ExecuteNonQueryAsync();
        }

        await using (var eventsCmd = new NpgsqlCommand(
            """
            INSERT INTO events (event_id, stream_id, stream_version, event_type, data, occurred_at, actor_id, actor_role, correlation_id)
            VALUES (@id, @s, @v, @t, @d::jsonb, @o, NULL, NULL, NULL)
            """, conn, tx))
        {
            eventsCmd.Parameters.AddWithValue("id", evt.EventId);
            eventsCmd.Parameters.AddWithValue("s", streamId);
            eventsCmd.Parameters.AddWithValue("v", streamVersion);
            eventsCmd.Parameters.AddWithValue("t", evt.EventType);
            eventsCmd.Parameters.AddWithValue("d", NpgsqlDbType.Text, dataJson);
            eventsCmd.Parameters.AddWithValue("o", DateTime.SpecifyKind(evt.OccurredAt, DateTimeKind.Utc));
            await eventsCmd.ExecuteNonQueryAsync();
        }

        long outboxId;
        await using (var outboxCmd = new NpgsqlCommand(
            """
            INSERT INTO outbox_events (
                service_id, stream_id, event_id, event_type, event_payload,
                correlation_id, actor_id, actor_role, published_at, stream_version)
            VALUES (
                'backend-api', @s, @id, @t, @p::jsonb,
                NULL, NULL, NULL, NOW(), @v)
            RETURNING outbox_id
            """, conn, tx))
        {
            outboxCmd.Parameters.AddWithValue("s", streamId);
            outboxCmd.Parameters.AddWithValue("id", evt.EventId);
            outboxCmd.Parameters.AddWithValue("t", evt.EventType);
            outboxCmd.Parameters.AddWithValue("p", NpgsqlDbType.Text, dataJson);
            outboxCmd.Parameters.AddWithValue("v", streamVersion);
            outboxId = Convert.ToInt64(await outboxCmd.ExecuteScalarAsync());
        }

        await tx.CommitAsync();
        return outboxId;
    }

    private async Task<List<CellRow>> ReadCellRowsAsync(string employeeId)
    {
        var rows = new List<CellRow>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_id, hours, source, superseded_by_outbox_id, outbox_id
            FROM time_entries_projection
            WHERE employee_id = @e AND date = @d AND task_id = @t
            ORDER BY outbox_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", CellDate);
        cmd.Parameters.AddWithValue("t", CellTask);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new CellRow(
                EventId: reader.GetGuid(0),
                Hours: reader.GetDecimal(1),
                Source: reader.GetString(2),
                SupersededBy: reader.IsDBNull(3) ? null : reader.GetInt64(3),
                OutboxId: reader.GetInt64(4)));
        }
        return rows;
    }

    /// <summary>
    /// Insert a TimeEntryRegistered event into <c>events</c> + a matching
    /// <c>outbox_events</c> row marked published with <c>stream_version</c> set. Mirrors
    /// the post-publisher-drain steady-state that the backfill is designed to handle.
    /// </summary>
    private async Task SeedEventAndOutboxRowAsync(
        TimeEntryRegistered evt, string streamId, int streamVersion)
    {
        var data = EventSerializer.Serialize(evt);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Ensure stream row exists (FK target for events.stream_id).
        await using (var ensureCmd = new NpgsqlCommand(
            "INSERT INTO event_streams (stream_id) VALUES (@s) ON CONFLICT DO NOTHING",
            conn, tx))
        {
            ensureCmd.Parameters.AddWithValue("s", streamId);
            await ensureCmd.ExecuteNonQueryAsync();
        }

        // Insert into events. occurred_at = evt.OccurredAt (already UTC).
        await using (var eventsCmd = new NpgsqlCommand(
            """
            INSERT INTO events (event_id, stream_id, stream_version, event_type, data, occurred_at, actor_id, actor_role, correlation_id)
            VALUES (@id, @s, @v, @t, @d::jsonb, @o, NULL, NULL, NULL)
            """, conn, tx))
        {
            eventsCmd.Parameters.AddWithValue("id", evt.EventId);
            eventsCmd.Parameters.AddWithValue("s", streamId);
            eventsCmd.Parameters.AddWithValue("v", streamVersion);
            eventsCmd.Parameters.AddWithValue("t", evt.EventType);
            eventsCmd.Parameters.AddWithValue("d", NpgsqlDbType.Text, data);
            eventsCmd.Parameters.AddWithValue("o", DateTime.SpecifyKind(evt.OccurredAt, DateTimeKind.Utc));
            await eventsCmd.ExecuteNonQueryAsync();
        }

        // Insert matching outbox_events row marked PUBLISHED with stream_version set.
        await using (var outboxCmd = new NpgsqlCommand(
            """
            INSERT INTO outbox_events (
                service_id, stream_id, event_id, event_type, event_payload,
                correlation_id, actor_id, actor_role, published_at, stream_version)
            VALUES (
                'backend-api', @s, @id, @t, @p::jsonb,
                NULL, NULL, NULL, NOW(), @v)
            """, conn, tx))
        {
            outboxCmd.Parameters.AddWithValue("s", streamId);
            outboxCmd.Parameters.AddWithValue("id", evt.EventId);
            outboxCmd.Parameters.AddWithValue("t", evt.EventType);
            outboxCmd.Parameters.AddWithValue("p", NpgsqlDbType.Text, data);
            outboxCmd.Parameters.AddWithValue("v", streamVersion);
            await outboxCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    private async Task<long> CountProjectionRowsAsync(string tableName, string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {tableName} WHERE employee_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> CountProjectionByEventIdAsync(string tableName, Guid eventId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {tableName} WHERE event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
