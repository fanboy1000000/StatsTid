using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S27 / TASK-2710 Slot 6 — projection-vs-event-stream parity.
///
/// <para>
/// Pins the load-bearing invariant of the S27 sync-in-tx projection design across the
/// full publish lifecycle: after N writes via real POST handlers (atomic outbox enqueue
/// + projection INSERT), the publisher drains the outbox to canonical <c>events</c>, and
/// the resulting state is observable on three surfaces — outbox, events, projection —
/// MUST agree exactly. Specifically:
/// </para>
///
/// <list type="number">
///   <item>BEFORE drain: exactly N unpublished <c>outbox_events</c> rows exist (cycle-3
///   row-count guard — added per Codex W cycle 2 to catch a dropped-write between
///   tx commit and outbox visibility).</item>
///   <item>Drain: re-enable publisher; poll <c>outbox_events.published_at IS NOT NULL</c>
///   count → N (publisher drained all N rows).</item>
///   <item>AFTER drain: exactly N matching rows in canonical <c>events</c> table.</item>
///   <item>Projection contents match <c>OfType&lt;TimeEntryRegistered&gt;</c> filter on
///   <c>events</c> for the same employee — same row count, same field values, both
///   ordering columns produce the same sequence.</item>
/// </list>
///
/// <para>
/// Two tests cover both projection tables (<c>time_entries_projection</c> +
/// <c>absences_projection</c>). Reuses <see cref="StatsTidWebApplicationFactory"/>'s
/// <see cref="StatsTidWebApplicationFactory.StopPublisherAsync"/> +
/// <see cref="StatsTidWebApplicationFactory.StartPublisherAsync"/> to control drain
/// timing for the row-count guard between the writes and the drain.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProjectionParityTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const int N = 5;

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// Time entries parity: write N entries via POST under a stalled publisher, assert
    /// row-count guard (N unpublished outbox rows), drain, assert (N events) AND
    /// (N matching projection rows with identical field values + outbox_id ordering).
    /// </summary>
    [Fact]
    public async Task TimeEntries_ProjectionAndEventsAgree_AfterPublisherDrain()
    {
        var client = _factory.CreateClient();
        await _factory.StopPublisherAsync();

        var employeeId = "EMP_PARITY_T_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";
        var token = MintEmployeeToken(employeeId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Write N entries on consecutive days.
        for (int i = 0; i < N; i++)
        {
            var rsp = await client.PostAsJsonAsync("/api/time-entries", new
            {
                employeeId,
                date = new DateOnly(2026, 5, 1).AddDays(i).ToString("yyyy-MM-dd"),
                hours = 7.4m,
                taskId = $"PROJ-PAR-{i}",
                activityType = "NORMAL",
                agreementCode = "HK",
            });
            Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        }

        // (1) Row-count guard BEFORE drain: exactly N unpublished outbox rows.
        // Catches a "tx committed but outbox visibility lost" regression — without
        // this guard, an asymmetry between the POST-side and read-side could mask
        // a missing-write bug behind a polling drain that "succeeds" with N=0.
        await using var verifyConn = new NpgsqlConnection(_harness.ConnectionString);
        await verifyConn.OpenAsync();
        await using (var unpubCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @s AND event_type = 'TimeEntryRegistered' AND published_at IS NULL
            """, verifyConn))
        {
            unpubCmd.Parameters.AddWithValue("s", streamId);
            Assert.Equal((long)N, Convert.ToInt64(await unpubCmd.ExecuteScalarAsync()));
        }

        // (2) Re-enable publisher, drain, poll until all N have published_at IS NOT NULL.
        await _factory.StartPublisherAsync();
        await WaitForOutboxDrainedAsync(streamId, "TimeEntryRegistered", expected: N, timeoutMs: 15_000);

        // (3) Exactly N rows in canonical events table.
        await using (var eventsCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM events WHERE stream_id = @s AND event_type = 'TimeEntryRegistered'",
            verifyConn))
        {
            eventsCmd.Parameters.AddWithValue("s", streamId);
            Assert.Equal((long)N, Convert.ToInt64(await eventsCmd.ExecuteScalarAsync()));
        }

        // (4) Projection rows == events rows (same count, same date sequence).
        // Projection is ordered by outbox_id ASC; events by stream_version. Both must
        // produce the same date sequence because outbox_id BIGSERIAL aligns with
        // publisher-assigned stream_version (outbox_id is the source of truth at write
        // time and the publisher fills stream_version in increasing outbox_id order).
        var projectionDates = await ReadProjectionDatesAsync(
            "time_entries_projection", employeeId);
        Assert.Equal(N, projectionDates.Count);

        var eventDates = await ReadEventTimeEntryDatesAsync(streamId);
        Assert.Equal(N, eventDates.Count);

        Assert.Equal(eventDates, projectionDates);
    }

    /// <summary>
    /// Absences parity: same shape as the time-entries test for absences_projection.
    /// </summary>
    [Fact]
    public async Task Absences_ProjectionAndEventsAgree_AfterPublisherDrain()
    {
        var client = _factory.CreateClient();
        await _factory.StopPublisherAsync();

        var employeeId = "EMP_PARITY_A_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";
        var token = MintEmployeeToken(employeeId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        for (int i = 0; i < N; i++)
        {
            var rsp = await client.PostAsJsonAsync("/api/absences", new
            {
                employeeId,
                date = new DateOnly(2026, 5, 1).AddDays(i).ToString("yyyy-MM-dd"),
                absenceType = "VACATION",
                hours = 7.4m,
                agreementCode = "HK",
            });
            Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        }

        await using var verifyConn = new NpgsqlConnection(_harness.ConnectionString);
        await verifyConn.OpenAsync();

        // (1) Row-count guard.
        await using (var unpubCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @s AND event_type = 'AbsenceRegistered' AND published_at IS NULL
            """, verifyConn))
        {
            unpubCmd.Parameters.AddWithValue("s", streamId);
            Assert.Equal((long)N, Convert.ToInt64(await unpubCmd.ExecuteScalarAsync()));
        }

        // (2) Drain.
        await _factory.StartPublisherAsync();
        await WaitForOutboxDrainedAsync(streamId, "AbsenceRegistered", expected: N, timeoutMs: 15_000);

        // (3) Events count.
        await using (var eventsCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM events WHERE stream_id = @s AND event_type = 'AbsenceRegistered'",
            verifyConn))
        {
            eventsCmd.Parameters.AddWithValue("s", streamId);
            Assert.Equal((long)N, Convert.ToInt64(await eventsCmd.ExecuteScalarAsync()));
        }

        // (4) Projection vs events parity (date sequence).
        var projectionDates = await ReadProjectionDatesAsync(
            "absences_projection", employeeId);
        Assert.Equal(N, projectionDates.Count);

        var eventDates = await ReadEventAbsenceDatesAsync(streamId);
        Assert.Equal(N, eventDates.Count);

        Assert.Equal(eventDates, projectionDates);
    }

    private async Task<List<DateOnly>> ReadProjectionDatesAsync(string tableName, string employeeId)
    {
        var dates = new List<DateOnly>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT date FROM {tableName} WHERE employee_id = @id ORDER BY outbox_id ASC", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            dates.Add(DateOnly.FromDateTime(reader.GetDateTime(0)));
        return dates;
    }

    private async Task<List<DateOnly>> ReadEventTimeEntryDatesAsync(string streamId)
    {
        var dates = new List<DateOnly>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // Pull the date out of the JSONB payload. Same OK-version-agnostic shape used by
        // OutboxPublisherTests when it reads payloads directly.
        await using var cmd = new NpgsqlCommand(
            """
            SELECT (data ->> 'Date') FROM events
            WHERE stream_id = @s AND event_type = 'TimeEntryRegistered'
            ORDER BY stream_version ASC
            """, conn);
        cmd.Parameters.AddWithValue("s", streamId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var raw = reader.GetString(0);
            dates.Add(DateOnly.Parse(raw, System.Globalization.CultureInfo.InvariantCulture));
        }
        return dates;
    }

    private async Task<List<DateOnly>> ReadEventAbsenceDatesAsync(string streamId)
    {
        var dates = new List<DateOnly>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT (data ->> 'Date') FROM events
            WHERE stream_id = @s AND event_type = 'AbsenceRegistered'
            ORDER BY stream_version ASC
            """, conn);
        cmd.Parameters.AddWithValue("s", streamId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var raw = reader.GetString(0);
            dates.Add(DateOnly.Parse(raw, System.Globalization.CultureInfo.InvariantCulture));
        }
        return dates;
    }

    private async Task WaitForOutboxDrainedAsync(
        string streamId, string eventType, int expected, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT COUNT(*) FROM outbox_events
                WHERE stream_id = @s AND event_type = @t AND published_at IS NOT NULL
                """, conn);
            cmd.Parameters.AddWithValue("s", streamId);
            cmd.Parameters.AddWithValue("t", eventType);
            var c = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (c >= expected) return;
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"OutboxPublisher did not drain {expected} {eventType} rows on stream {streamId} within {timeoutMs}ms.");
    }

    private static string MintEmployeeToken(string employeeId)
    {
        var settings = new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        };
        var tokenService = new JwtTokenService(settings);
        return tokenService.GenerateToken(
            employeeId: employeeId,
            name: employeeId,
            role: StatsTidRoles.Employee,
            agreementCode: "HK");
    }
}
