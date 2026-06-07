using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S66 / TASK-6607 / ADR-032 D2 — projection-backfill feriedage materialization pins.
/// Exercises <see cref="ProjectionBackfillService"/> directly (the canonical single-source-of-truth
/// replay) against directly-seeded <c>events</c> + <c>outbox_events</c>, mirroring the
/// <see cref="ProjectionBackfillTests"/> scaffold.
///
/// <list type="bullet">
///   <item><description><b>Pre-S66 null-payload backfill (the convention pin):</b> an
///   <c>AbsenceRegistered</c> event serialized WITHOUT a Feriedage field (null) backfills to
///   <c>ROUND(hours / 7.4, 4)</c> — the convention in force when written
///   (ProjectionBackfillService.cs:393-395). Byte-identical to the init.sql legacy
///   <c>ROUND(hours/7.4,4)</c> ALTER backfill.</description></item>
///   <item><description><b>Post-S66 payload backfill:</b> an event carrying an explicit Feriedage
///   (e.g. the ADR-032 D1 value 2.0 for a half-timer's 7.4h day) backfills to exactly that recorded
///   value — NOT re-derived from hours/7.4 (which would be 1.0). Proves the payload is authoritative
///   on replay.</description></item>
///   <item><description><b>EntitlementBalanceRevalued replay:</b> a revaluation event's replacement
///   set is applied AFTER the absence INSERT, overwriting the absence row's feriedage — so a
///   from-events rebuild equals live post-revaluation state (ADR-032 D2 replay contract).</description></item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class Adr032BackfillFeriedageTests : IAsyncLifetime
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
    /// A pre-S66 AbsenceRegistered event (Feriedage = null in the payload) backfills its
    /// projection <c>feriedage</c> to <c>ROUND(hours / 7.4, 4)</c>. A 3.7h row → 0.5.
    /// </summary>
    [Fact]
    public async Task Backfill_PreS66NullFeriedageEvent_BackfillsAsHoursOver74()
    {
        var employeeId = "EMP_BF_NULL_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";

        // Pre-S66 shape: Feriedage NOT set (null) — the serialized payload lacks the field.
        var evt = new AbsenceRegistered
        {
            EmployeeId = employeeId,
            Date = new DateOnly(2024, 11, 4),
            AbsenceType = "VACATION",
            Hours = 3.7m,
            AgreementCode = "AC",
            OkVersion = "OK24",
            // Feriedage intentionally omitted ⇒ null.
        };
        await SeedAbsenceEventAndOutboxRowAsync(evt, streamId, streamVersion: 1);

        var run = await _service.RunAsync();
        Assert.Equal(1, run.InsertedAbsences);

        // ROUND(3.7 / 7.4, 4) = 0.5.
        Assert.Equal(0.5m, await ReadFeriedageAsync(employeeId, evt.Date));
    }

    /// <summary>
    /// A post-S66 AbsenceRegistered event carrying an explicit Feriedage (2.0 — the ADR-032 D1
    /// value for a half-timer's 7.4h day) backfills to exactly 2.0, NOT the hours/7.4 fallback
    /// (which would be 1.0). The payload is authoritative on replay.
    /// </summary>
    [Fact]
    public async Task Backfill_PostS66PayloadFeriedage_BackfillsRecordedValue_NotHoursOver74()
    {
        var employeeId = "EMP_BF_VAL_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";

        var evt = new AbsenceRegistered
        {
            EmployeeId = employeeId,
            Date = new DateOnly(2025, 1, 6),
            AbsenceType = "VACATION",
            Hours = 7.4m,
            AgreementCode = "AC",
            OkVersion = "OK24",
            Feriedage = 2.0m, // ADR-032 D1: half-timer's 7.4h day at norm 3.7 = 2.0
        };
        await SeedAbsenceEventAndOutboxRowAsync(evt, streamId, streamVersion: 1);

        var run = await _service.RunAsync();
        Assert.Equal(1, run.InsertedAbsences);

        Assert.Equal(2.0m, await ReadFeriedageAsync(employeeId, evt.Date)); // recorded value, not 1.0
    }

    /// <summary>
    /// ADR-032 D2 replay contract: an EntitlementBalanceRevalued event (on the employee-{id} stream)
    /// whose replacement set targets a prior AbsenceRegistered overwrites that absence's feriedage on
    /// rebuild — so a from-events backfill equals live POST-revaluation state. The absence is seeded
    /// at feriedage 1.0; the revaluation replaces it with 2.0.
    /// </summary>
    [Fact]
    public async Task Backfill_AppliesEntitlementBalanceRevaluedReplacementSet()
    {
        var employeeId = "EMP_BF_REV_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var absenceStream = $"employee-{employeeId}";

        // Original absence: 7.4h, recorded feriedage 1.0 (full-time at booking).
        var absence = new AbsenceRegistered
        {
            EmployeeId = employeeId,
            Date = new DateOnly(2026, 2, 3),
            AbsenceType = "VACATION",
            Hours = 7.4m,
            AgreementCode = "AC",
            OkVersion = "OK24",
            Feriedage = 1.0m,
        };
        await SeedAbsenceEventAndOutboxRowAsync(absence, absenceStream, streamVersion: 1);

        // Revaluation event (same employee-{id} stream) replacing the absence's feriedage with 2.0.
        var revalued = new EntitlementBalanceRevalued
        {
            EmployeeId = employeeId,
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            Replacements = new[] { new AbsenceFeriedageReplacement(absence.EventId, 2.0m) },
            UsedDelta = 1.0m,
            TriggeringProfileEventId = Guid.NewGuid(),
        };
        await SeedRevaluedEventAndOutboxRowAsync(revalued, absenceStream, streamVersion: 2);

        var run = await _service.RunAsync();
        Assert.Equal(1, run.InsertedAbsences);

        // Backfill applied the replacement set AFTER the absence INSERT → 2.0, not the original 1.0.
        Assert.Equal(2.0m, await ReadFeriedageAsync(employeeId, absence.Date));
    }

    // ── Seeding helpers (mirror ProjectionBackfillTests.SeedEventAndOutboxRowAsync) ──

    private async Task SeedAbsenceEventAndOutboxRowAsync(
        AbsenceRegistered evt, string streamId, int streamVersion)
        => await SeedEventAndOutboxRowAsync(
            evt.EventId, streamId, streamVersion, evt.EventType, EventSerializer.Serialize(evt), evt.OccurredAt);

    private async Task SeedRevaluedEventAndOutboxRowAsync(
        EntitlementBalanceRevalued evt, string streamId, int streamVersion)
        => await SeedEventAndOutboxRowAsync(
            evt.EventId, streamId, streamVersion, evt.EventType, EventSerializer.Serialize(evt), evt.OccurredAt);

    private async Task SeedEventAndOutboxRowAsync(
        Guid eventId, string streamId, int streamVersion, string eventType, string data, DateTime occurredAt)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var ensureCmd = new NpgsqlCommand(
            "INSERT INTO event_streams (stream_id) VALUES (@s) ON CONFLICT DO NOTHING", conn, tx))
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
            eventsCmd.Parameters.AddWithValue("id", eventId);
            eventsCmd.Parameters.AddWithValue("s", streamId);
            eventsCmd.Parameters.AddWithValue("v", streamVersion);
            eventsCmd.Parameters.AddWithValue("t", eventType);
            eventsCmd.Parameters.AddWithValue("d", NpgsqlDbType.Text, data);
            eventsCmd.Parameters.AddWithValue("o", DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc));
            await eventsCmd.ExecuteNonQueryAsync();
        }

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
            outboxCmd.Parameters.AddWithValue("id", eventId);
            outboxCmd.Parameters.AddWithValue("t", eventType);
            outboxCmd.Parameters.AddWithValue("p", NpgsqlDbType.Text, data);
            outboxCmd.Parameters.AddWithValue("v", streamVersion);
            await outboxCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    private async Task<decimal> ReadFeriedageAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT feriedage FROM absences_projection WHERE employee_id = @e AND date = @d", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", date);
        var result = await cmd.ExecuteScalarAsync();
        Assert.False(result is null or DBNull, $"Expected a non-null feriedage for {date}.");
        return (decimal)result!;
    }
}
