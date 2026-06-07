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
            INSERT INTO employee_profiles (profile_id, employee_id, part_time_fraction, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @e, 1.0, '0001-01-01', NULL, 1)
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
        public HttpClient CreateClient(string name) => new(new RuleEngineStubHandler(), disposeHandler: false);
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
