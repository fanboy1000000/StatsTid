using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure.Outbox;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
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
        // S136 / TASK-13603 — POST /api/time-entries now resolves the subject's users row
        // (terminated-inclusive read, ADR-040 D3 / SEC-046) before writing, so the employee must
        // exist. NULL employment dates ⇒ window-unbounded (D2); is_active=true ⇒ no role floor.
        await SeedFullTimeEmployeeAsync(employeeId, "HK");
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
    ///
    /// <para>
    /// S66 / TASK-6607a (ADR-032 D5): the N writes were MIGRATED off the retired
    /// <c>POST /api/absences</c> (which bypassed all consumption validation) onto
    /// <c>POST /api/skema/{employeeId}/save</c> — one save per day, mirroring the original
    /// per-day POST loop. The coverage class is unchanged: N unpublished outbox rows before
    /// drain, then live projection rows vs <c>OfType&lt;AbsenceRegistered&gt;</c> on
    /// <c>events</c> must agree (same count, same date sequence). The Skema save validates
    /// everything the old POST skipped, so a FULLY-SEEDED full-time employee is stood up
    /// (user + open dated 1.0 <c>employee_profiles</c> + open <c>user_agreement_codes</c> AC)
    /// and bookings are full-time WEEKDAY 7.4h VACATION days (byte-identical valuation
    /// before/after the parallel ADR-032 D1 cutover). The <see cref="IHttpClientFactory"/>
    /// is stubbed to drive the REAL <see cref="EntitlementValidationRule.Evaluate"/> over
    /// the validate-entitlement seam. Publisher stop/start AND the writes all ride ONE host
    /// derived via <c>WithWebHostBuilder</c> so the stall + drain govern that host's writes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Absences_ProjectionAndEventsAgree_AfterPublisherDrain()
    {
        var stubbedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory());
            });
        });
        var client = stubbedFactory.CreateClient();
        await StopPublisherAsync(stubbedFactory.Services);

        var employeeId = "EMP_PARITY_A_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";
        await SeedFullTimeEmployeeAsync(employeeId, "AC");
        var token = MintEmployeeToken(employeeId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // N VACATION days on consecutive WEEKDAYS (Mon 2026-05-04 … Fri 2026-05-08) — all
        // weekdays, in the 2025 ferieår (reset month 9), N ≪ the full annual 25 forskud cap
        // for a whole-ferieår full-timer ⇒ each save succeeds. One Skema save per day mirrors
        // the original per-day POST loop and keeps the per-day-norm cap (7.4h/day) satisfied.
        var weekdayStart = new DateOnly(2026, 5, 4); // Monday
        for (int i = 0; i < N; i++)
        {
            var date = weekdayStart.AddDays(i); // Mon..Fri, all weekdays
            var rsp = await client.PostAsJsonAsync($"/api/skema/{employeeId}/save", new
            {
                year = date.Year,
                month = date.Month,
                absences = new[]
                {
                    new
                    {
                        date = date.ToString("yyyy-MM-dd"),
                        absenceType = "VACATION",
                        hours = 7.4m,
                    },
                },
            });
            Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
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
        await StartPublisherAsync(stubbedFactory.Services);
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

    // ════════════════════════════════════════════════════════════════════════════════════
    // S145 / TASK-14501 (A0, refinement R22) — the live current set equals the fold of the events.
    // ════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Plain language.</b> The projection table is a read-model kept in step with the event log. After
    /// S145 it must hold, for a Skema cell that was saved twice, only the LATER save as current, while a
    /// <c>POST /api/time-entries</c> for the same day and task stays additive. This fact proves the live
    /// write path and the event log tell the same story: it writes through the real endpoints (two Skema
    /// saves of one cell, then one time-entry POST for the same day and task), lets the publisher drain,
    /// folds the drained events in <c>outbox_id</c> order under the ruled semantics, and requires the fold
    /// to equal the rows the live path left current. This suite never runs the rebuild; the rebuild is
    /// pinned in <see cref="ProjectionBackfillTests"/>.
    ///
    /// <para><b>The ruled semantics (refinement A0).</b> A <c>source</c> of <c>SKEMA</c> replaces the cell's
    /// earlier SKEMA event (same employee, date, task). Anything else is <c>TIME_POST</c> and is additive,
    /// and an event with no <c>source</c> at all is <c>TIME_POST</c>: that is every pre-S145 event.</para>
    ///
    /// <para><b>Guard: passes today.</b> Today no event carries <c>source</c>, so the fold keeps all three
    /// events, and the live table holds all three rows with no supersession column to filter on (the
    /// column is read only when it exists). After S145 the events carry <c>source</c>, the fold keeps two
    /// and the live current rows are the same two. The fold reads the payload, so it is not decided by
    /// the live path under test.</para>
    ///
    /// <para><b>Red conditions (after S145; <b>MD-12</b>, <b>MD-13</b>).</b> MD-12 (the save handler updates a
    /// cell with a current SKEMA row in place and emits no second event): the log holds the 7.4 h save
    /// and the Time-POST event only, so the fold's Skema row says 7.4 h while the live row says 8.0 h, and
    /// the set comparison fails on the hours. MD-13 (both repositories' <c>InsertAsync</c> mark for every
    /// insert, dropping the <c>source = 'SKEMA'</c> restriction): the Time-POST insert, which comes last,
    /// supersedes the current Skema row, so the live current set is missing the 8.0 h Skema row that the
    /// fold keeps. Both are compared on (event id, date, task, hours), so neither can hide behind a
    /// matching count.</para>
    /// </summary>
    [Fact]
    public async Task Parity_AfterSkemaResaveAndTimePost_LiveCurrentSetEqualsFoldOfEvents()
    {
        var client = _factory.CreateClient();
        var employeeId = "EMP_PARITY_S145_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";
        await SeedFullTimeEmployeeAsync(employeeId, "HK");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId));

        // A WEEKDAY (Monday 4 May 2026); the same cell for all three writes.
        var cellDate = new DateOnly(2026, 5, 4);
        Assert.Equal(DayOfWeek.Monday, cellDate.DayOfWeek);
        const string task = "PROJ-PAR-S145-CELL";

        // Two Skema saves of one cell: 7.4 h, then the re-save at 8.0 h.
        foreach (var hours in new[] { 7.4m, 8.0m })
        {
            var save = await client.PostAsJsonAsync($"/api/skema/{employeeId}/save", new
            {
                year = cellDate.Year,
                month = cellDate.Month,
                entries = new[] { new { date = cellDate.ToString("yyyy-MM-dd"), projectCode = task, hours } },
            });
            Assert.True(save.StatusCode == HttpStatusCode.OK,
                $"Skema save at {hours} h: expected 200, got {(int)save.StatusCode} {await save.Content.ReadAsStringAsync()}");
        }

        // One additive registration for the same day and task.
        var post = await client.PostAsJsonAsync("/api/time-entries", new
        {
            employeeId,
            date = cellDate.ToString("yyyy-MM-dd"),
            hours = 2.0m,
            taskId = task,
            activityType = "NORMAL",
            agreementCode = "HK",
        });
        Assert.True(post.StatusCode == HttpStatusCode.Created,
            $"POST /api/time-entries: expected 201, got {(int)post.StatusCode} {await post.Content.ReadAsStringAsync()}");

        // Let the publisher drain EVERY TimeEntryRegistered row of the stream into `events` (the count
        // is deliberately not fixed: under MD-12 the log legitimately holds one event fewer).
        await WaitForStreamFullyDrainedAsync(streamId, "TimeEntryRegistered", timeoutMs: 15_000);

        var fold = await FoldCurrentSetFromEventsAsync(streamId);
        var live = await ReadLiveCurrentSetAsync(employeeId);

        Assert.Equal(fold, live);
    }

    /// <summary>
    /// Folds the drained <c>TimeEntryRegistered</c> events of the stream in <c>outbox_id</c> order under
    /// the ruled semantics and returns the current set as sorted, comparable lines
    /// (<c>eventId|date|task|hours</c>). The order key is <c>outbox_events.outbox_id</c>, the global
    /// write order; the payload is the canonical <c>events.data</c>.
    /// </summary>
    private async Task<List<string>> FoldCurrentSetFromEventsAsync(string streamId)
    {
        var current = new List<(Guid EventId, string Date, string Task, decimal Hours, string Source)>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT e.event_id, e.data::text
            FROM events e
            JOIN outbox_events o ON o.event_id = e.event_id
            WHERE e.stream_id = @s AND e.event_type = 'TimeEntryRegistered'
            ORDER BY o.outbox_id ASC
            """, conn);
        cmd.Parameters.AddWithValue("s", streamId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var eventId = reader.GetGuid(0);
            using var doc = JsonDocument.Parse(reader.GetString(1));
            var root = doc.RootElement;
            var date = root.GetProperty("date").GetString()!;
            var task = root.TryGetProperty("taskId", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
            var hours = root.GetProperty("hours").GetDecimal();
            // The ruled semantics: SKEMA is the only value that replaces; absent or anything else is TIME_POST.
            var source = root.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String
                         && string.Equals(s.GetString(), "SKEMA", StringComparison.Ordinal)
                ? "SKEMA"
                : "TIME_POST";

            if (source == "SKEMA")
                current.RemoveAll(r => r.Source == "SKEMA" && r.Date == date && r.Task == task);
            current.Add((eventId, date, task, hours, source));
        }

        return current
            .Select(r => $"{r.EventId}|{r.Date}|{r.Task}|{r.Hours.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The rows the live path left CURRENT, in the same line shape as the fold. "Current" means
    /// <c>superseded_by_outbox_id IS NULL</c>; until S145 adds that column every row is current, so
    /// the filter is applied only when the column exists (this fact is a guard that passes today).
    /// </summary>
    private async Task<List<string>> ReadLiveCurrentSetAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        bool hasSupersessionColumn;
        await using (var colCmd = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'time_entries_projection' AND column_name = 'superseded_by_outbox_id')
            """, conn))
        {
            hasSupersessionColumn = (bool)(await colCmd.ExecuteScalarAsync())!;
        }

        // Two constant statements (no interpolation): the only difference is the current-row predicate.
        const string AllRows =
            """
            SELECT event_id, date, task_id, hours FROM time_entries_projection
            WHERE employee_id = @e
            """;
        const string CurrentRows =
            """
            SELECT event_id, date, task_id, hours FROM time_entries_projection
            WHERE employee_id = @e AND superseded_by_outbox_id IS NULL
            """;

        var lines = new List<string>();
        // Each constructor call gets a literal (a conditional over the two constants would be a CA2100 warning).
        await using var cmd = hasSupersessionColumn
            ? new NpgsqlCommand(CurrentRows, conn)
            : new NpgsqlCommand(AllRows, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var date = DateOnly.FromDateTime(reader.GetDateTime(1)).ToString("yyyy-MM-dd");
            var task = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var hours = reader.GetDecimal(3).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            lines.Add($"{reader.GetGuid(0)}|{date}|{task}|{hours}");
        }
        return lines.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>Waits until the stream has at least one <paramref name="eventType"/> outbox row and none unpublished.</summary>
    private async Task WaitForStreamFullyDrainedAsync(string streamId, string eventType, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT COUNT(*), COUNT(*) FILTER (WHERE published_at IS NULL)
                FROM outbox_events WHERE stream_id = @s AND event_type = @t
                """, conn);
            cmd.Parameters.AddWithValue("s", streamId);
            cmd.Parameters.AddWithValue("t", eventType);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            if (reader.GetInt64(0) > 0 && reader.GetInt64(1) == 0) return;
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"OutboxPublisher did not drain every {eventType} row on stream {streamId} within {timeoutMs}ms.");
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
        // Pull the date out of the JSONB payload. EventSerializer camelCase since S3 (0cb4ced);
        // S64 replay-parity sweep: all production readers camelCase/typed — so the JSONB key is
        // 'date' (PascalCase 'Date' returns NULL under Postgres' case-sensitive ->> lookup).
        await using var cmd = new NpgsqlCommand(
            """
            SELECT (data ->> 'date') FROM events
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
        // EventSerializer camelCase since S3 (0cb4ced); S64 replay-parity sweep: the JSONB key
        // is 'date' (case-sensitive ->> lookup — PascalCase 'Date' returns NULL).
        await using var cmd = new NpgsqlCommand(
            """
            SELECT (data ->> 'date') FROM events
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

    // ── S66 TASK-6607a: Skema-save seeding scaffold (mirrors SkemaMonthlyAccrualGuardTests) ──

    /// <summary>
    /// Stands up a fully-seeded full-time employee so the migrated VACATION Skema saves pass
    /// every consumption-validation gate the retired POST skipped: a user row, an OPEN
    /// full-time <c>employee_profiles</c> row (1.0, '0001-01-01' — covers the MONTHLY_ACCRUAL
    /// anchor), and an OPEN <c>user_agreement_codes</c> row. Verbatim seeder shape from
    /// <see cref="SkemaMonthlyAccrualGuardTests"/>.
    /// </summary>
    private async Task SeedFullTimeEmployeeAsync(string employeeId, string agreementCode)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        await using (var userCmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@u, @u, 'dev-only', 'S66 Parity Migration Test User', NULL, 'STY01', @ac, 'OK24', TRUE)
            ON CONFLICT (user_id) DO NOTHING
            """, conn))
        {
            userCmd.Parameters.AddWithValue("u", employeeId);
            userCmd.Parameters.AddWithValue("ac", agreementCode);
            await userCmd.ExecuteNonQueryAsync();
        }

        await using (var profileCmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (profile_id, employee_id, part_time_fraction, effective_from, effective_to, version,
                                           employment_category)
            VALUES (gen_random_uuid(), @e, 1.0, '0001-01-01', NULL, 1,
                    (SELECT u.employment_category FROM users u WHERE u.user_id = @e))
            ON CONFLICT (employee_id, effective_from) DO UPDATE SET part_time_fraction = EXCLUDED.part_time_fraction
            """, conn))
        {
            profileCmd.Parameters.AddWithValue("e", employeeId);
            await profileCmd.ExecuteNonQueryAsync();
        }

        await using (var agreementCmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, @a, '0001-01-01', NULL, 1)
            ON CONFLICT (user_id, effective_from) DO UPDATE SET agreement_code = EXCLUDED.agreement_code
            """, conn))
        {
            agreementCmd.Parameters.AddWithValue("u", employeeId);
            agreementCmd.Parameters.AddWithValue("a", agreementCode);
            await agreementCmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Stops the <see cref="OutboxPublisher"/> on the SUPPLIED host's provider — the
    /// rule-stubbed write path rides a host derived via <c>WithWebHostBuilder</c>, so the
    /// stall must apply to that host (not the base <c>_factory</c>). Same resolution path as
    /// <see cref="StatsTidWebApplicationFactory.StopPublisherAsync"/>.
    /// </summary>
    private static async Task StopPublisherAsync(IServiceProvider services)
    {
        var publisher = services.GetServices<IHostedService>().OfType<OutboxPublisher>().Single();
        await publisher.StopAsync(CancellationToken.None);
    }

    /// <summary>Companion to <see cref="StopPublisherAsync"/> on the same supplied host.</summary>
    private static async Task StartPublisherAsync(IServiceProvider services)
    {
        var publisher = services.GetServices<IHostedService>().OfType<OutboxPublisher>().Single();
        await publisher.StartAsync(CancellationToken.None);
    }

    /// <summary>
    /// Rule-engine stub: drives the REAL <see cref="EntitlementValidationRule.Evaluate"/>
    /// over the <c>/api/rules/validate-entitlement</c> seam (the in-process WAF has no
    /// rule-engine container). Identical to the sibling Skema-Docker suites.
    /// </summary>
    private sealed class RuleEngineStubFactory : IHttpClientFactory
    {
        // S73 / TASK-7300 (R1): the endpoints now resolve the NAMED RuleEngine client and use
        // RELATIVE request URIs; this stub replaces the whole factory, so it supplies the
        // BaseAddress the production registration sets (behavior-preserving fixture change).
        public HttpClient CreateClient(string name) => new(new RuleEngineStubHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("http://rule-engine:8080"),
        };
    }

    private sealed class RuleEngineStubHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.EndsWith("/api/rules/validate-entitlement", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            var req = JsonSerializer.Deserialize<ValidateEntitlementRequest>(json, Camel)!;
            var result = EntitlementValidationRule.Evaluate(req);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(result, Camel), Encoding.UTF8, "application/json"),
            };
        }
    }
}
