using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S146 / TASK-14606 — the test-local Rule Engine stand-in for the flex-ledger host facts: it answers
/// <c>POST /api/rules/evaluate-timeline</c> with a CONFIGURED, NON-ZERO closing flex balance (and, when asked, a
/// <c>FLEX_PAYOUT</c> line), and every other endpoint exactly as <see cref="TestFixtures.DefaultRuleEngineHandler"/>.
///
/// <para>
/// <b>Why it exists (AC-C1).</b> The shared stub (<c>DefaultRuleEngineHandler</c>) returns <c>newBalance = 0m</c> for
/// every month, so a fact built on it cannot tell "the record stores the engine's closing balance" from "the record
/// stores 0". This stub lets a fact say "June closes at 10" and then assert that the export record, the response and
/// the event all say 10. It also echoes back the opening balance payroll SENT it (<c>previousFlexBalance</c>), which a
/// fact reads from <see cref="TimelineCalls"/> to prove which opening the calculation ran on.
/// </para>
///
/// <para>
/// It is keyed by (employee, calendar month of the request's <c>periodStart</c>); an unconfigured month closes at 0
/// with no payout. Thread-safe: the race facts run two requests at once.
/// </para>
/// </summary>
internal sealed class FlexLedgerStubEngine
{
    /// <summary>One timeline call payroll made: who, which period, and the opening balance it sent.</summary>
    public sealed record TimelineCall(string EmployeeId, DateOnly PeriodStart, decimal? PreviousFlexBalance);

    private readonly ConcurrentDictionary<(string Employee, DateOnly Month), (decimal Closing, decimal? Payout)> _config = new();
    private readonly ConcurrentQueue<TimelineCall> _timelineCalls = new();
    private readonly ConcurrentQueue<(string Path, string EmployeeId)> _allCalls = new();

    /// <summary>The month's closing balance the stub reports, and an optional payout (hours above the cap).</summary>
    private readonly ConcurrentDictionary<(string Employee, DateOnly Month), decimal> _delta = new();

    /// <summary>
    /// An OPENING-DEPENDENT closing for one employee and month (S146 TASK-14606 fix-up, replay fact): the stub answers
    /// <c>closing = (the opening it was sent) + delta</c>. Absolute <see cref="Configure"/> closings are unchanged. With
    /// this, a replay that sends the engine a different opening gets a different closing.
    /// </summary>
    public void ConfigureOpeningDependent(string employeeId, DateOnly month, decimal closingDelta, decimal? payoutHours = null)
    {
        var key = (employeeId, new DateOnly(month.Year, month.Month, 1));
        _delta[key] = closingDelta;
        _config[key] = (0m, payoutHours);
    }

    public void Configure(string employeeId, DateOnly month, decimal closing, decimal? payoutHours = null)
    {
        var key = (employeeId, new DateOnly(month.Year, month.Month, 1));
        _delta.TryRemove(key, out _); // the absolute and the opening-dependent modes exclude each other
        _config[key] = (closing, payoutHours);
    }

    /// <summary>Every timeline call, in arrival order.</summary>
    public IReadOnlyList<TimelineCall> TimelineCalls => _timelineCalls.ToList();

    /// <summary>How many rule-engine requests of any kind the employee caused (the "nothing was calculated" count).</summary>
    public int CallCountFor(string employeeId) => _allCalls.Count(c => c.EmployeeId == employeeId);

    public HttpResponseMessage Handle(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (request.Method == HttpMethod.Post
            && string.Equals(path, "/api/payroll/receive", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK); // the post-commit delivery
        }

        var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "{}";
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var employeeId = root.TryGetProperty("employeeId", out var eid) && eid.ValueKind == JsonValueKind.String
            ? eid.GetString()!
            : root.TryGetProperty("profile", out var prof) && prof.TryGetProperty("employeeId", out var pid)
                ? pid.GetString() ?? "EMP"
                : "EMP";
        _allCalls.Enqueue((path, employeeId));

        if (!path.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal))
            return TestFixtures.DefaultRuleEngineHandler(request);

        var periodStart = DateOnly.Parse(root.GetProperty("periodStart").GetString()!, CultureInfo.InvariantCulture);
        var periodEnd = DateOnly.Parse(root.GetProperty("periodEnd").GetString()!, CultureInfo.InvariantCulture);
        decimal? sentOpening = root.TryGetProperty("previousFlexBalance", out var pfb) && pfb.ValueKind == JsonValueKind.Number
            ? pfb.GetDecimal()
            : null;
        _timelineCalls.Enqueue(new TimelineCall(employeeId, periodStart, sentOpening));

        var config = _config.TryGetValue((employeeId, new DateOnly(periodStart.Year, periodStart.Month, 1)), out var c)
            ? c
            : (Closing: 0m, Payout: (decimal?)null);
        var opening = sentOpening ?? 0m;
        if (_delta.TryGetValue((employeeId, new DateOnly(periodStart.Year, periodStart.Month, 1)), out var delta))
            config = (opening + delta, config.Payout);

        var inMonth = (root.TryGetProperty("entries", out var ents) ? ents.EnumerateArray().ToList() : new List<JsonElement>())
            .Where(e =>
            {
                var d = DateOnly.Parse(e.GetProperty("date").GetString()!, CultureInfo.InvariantCulture);
                return d >= periodStart && d <= periodEnd;
            })
            .ToList();

        var flexLines = config.Payout is > 0m
            ? new object[]
            {
                new { timeType = "FLEX_PAYOUT", hours = config.Payout.Value, rate = 1.0m, date = periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            }
            : Array.Empty<object>();

        var payload = new
        {
            results = new object[]
            {
                new { ruleId = "OVERTIME_CALC", employeeId, success = true, lineItems = Array.Empty<object>() },
                new
                {
                    ruleId = "NORM_CHECK_37H",
                    employeeId,
                    success = true,
                    lineItems = inMonth.Select(e => (object)new
                    {
                        timeType = "NORMAL_HOURS",
                        hours = e.GetProperty("hours").GetDecimal(),
                        rate = 1.0m,
                        date = e.GetProperty("date").GetString(),
                    }).ToList(),
                },
                new { ruleId = "FLEX_BALANCE", employeeId, success = true, lineItems = flexLines },
            },
            flex = new
            {
                ruleId = "FLEX_BALANCE",
                employeeId,
                success = true,
                lineItems = Array.Empty<object>(),
                previousBalance = opening,
                newBalance = config.Closing,
                delta = config.Closing - opening,
                workedHours = 0m,
                absenceNormCredits = 0m,
                effectiveNorm = 0m,
                excessForPayout = config.Payout ?? 0m,
            },
            configFingerprints = new[] { "stub" },
            evaluationModel = "weekly-timeline-v1",
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>
/// S146 / TASK-14606 — the shared, Docker-backed harness of the flex-ledger host facts (<c>FlexLedgerHostTests</c>,
/// <c>FlexLedgerRaceTests</c>, <c>FlexLedgerReplayTests</c>): one Postgres container with the full <c>init.sql</c>, the
/// REAL Payroll host over it with <see cref="FlexLedgerStubEngine"/> behind it (and a second host with the REAL Rule
/// Engine for the facts that need the engine's own arithmetic), plus the seeding, HTTP and read helpers.
///
/// <para>
/// It is an xUnit class fixture: one container per test class, so every fact seeds its OWN employee (a unique id) and
/// never reads another fact's rows. Docker-gated by its users (<c>[Trait("Category","Docker")]</c>): CI-verified,
/// never locally green.
/// </para>
///
/// <para>
/// <b>Calendar anchors the facts rely on.</b> 1 June 2026 is a Monday (its opening week holds no May day, so June
/// needs no previous-month approval); 31 May 2026 is a Sunday; 1 July 2026 is a Wednesday (its opening week holds
/// 29-30 June, so July needs June APPROVED); 1 May 2026 is a Friday (its opening week holds 27-30 April, so May needs
/// April APPROVED).
/// </para>
/// </summary>
public sealed class FlexLedgerHostFixture : IAsyncLifetime
{
    internal const string OrgId = "STY_S146_FLEX";
    internal const string Agreement = "HK";
    internal const string Ok = "OK26";

    private PostgreSqlContainer _container = null!;
    private PayrollHostFactory _realEngineFactory = null!;
    private PayrollHostFactory _stubFactoryBase = null!;
    private long _outboxSeq = 70_000;

    internal string ConnectionString { get; private set; } = null!;
    internal DbConnectionFactory Db { get; private set; } = null!;
    internal FlexLedgerStubEngine Engine { get; } = new();
    internal WebApplicationFactory<RetroactiveCorrectionService> StubHost { get; private set; } = null!;
    internal PayrollHostFactory RealEngineHost => _realEngineFactory;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(ConnectionString);
        Db = new DbConnectionFactory(ConnectionString);
        await TestFixtures.SeedWageTypeMappingsAsync(Db);

        var classifications = new RuleRegistry().GetAll();
        _stubFactoryBase = new PayrollHostFactory(ConnectionString, classifications);
        StubHost = _stubFactoryBase.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new StubClientFactory(Engine));
        }));
        _realEngineFactory = new PayrollHostFactory(ConnectionString, classifications, useRealRuleEngine: true);
    }

    public async Task DisposeAsync()
    {
        if (_realEngineFactory is not null)
            await _realEngineFactory.DisposeAsync();
        if (StubHost is not null)
            await StubHost.DisposeAsync();
        if (_stubFactoryBase is not null)
            await _stubFactoryBase.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private sealed class StubClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubClientFactory(FlexLedgerStubEngine engine) => _handler = new TestFixtures.StubHandler(engine.Handle);
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    // ═════════════════════════════════════════════════════════════════════
    // HTTP
    // ═════════════════════════════════════════════════════════════════════

    internal static EmploymentProfile Profile(string employeeId) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = Agreement,
        OkVersion = Ok,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = OrgId,
    };

    internal static string MintGlobalAdminToken()
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = PayrollHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: "ROOT-S146",
            name: "ROOT-S146",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private WebApplicationFactory<RetroactiveCorrectionService> HostFor(bool realEngine) =>
        realEngine ? _realEngineFactory : StubHost;

    internal async Task<(HttpStatusCode Status, string Json)> PostAsync(
        string path, object body, bool realEngine = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        var response = await HostFor(realEngine).CreateClient().SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>POST /api/payroll/calculate-and-export. The body carries the profile and the period and NO hours;
    /// <paramref name="previousFlexBalance"/> is added only when given (the refused member).</summary>
    internal Task<(HttpStatusCode Status, string Json)> CalculateAndExportAsync(
        string employeeId, DateOnly periodStart, DateOnly periodEnd,
        decimal? previousFlexBalance = null, bool realEngine = false)
    {
        var body = new Dictionary<string, object?>
        {
            ["profile"] = Profile(employeeId),
            ["entries"] = Array.Empty<TimeEntry>(),
            ["absences"] = Array.Empty<AbsenceEntry>(),
            ["periodStart"] = periodStart,
            ["periodEnd"] = periodEnd,
        };
        if (previousFlexBalance is not null)
            body["previousFlexBalance"] = previousFlexBalance;
        return PostAsync("/api/payroll/calculate-and-export", body, realEngine);
    }

    /// <summary>Calculate and export one whole calendar month (<paramref name="month"/> is any day of it).</summary>
    internal Task<(HttpStatusCode Status, string Json)> CalculateAndExportMonthAsync(
        string employeeId, DateOnly month, bool realEngine = false) =>
        CalculateAndExportAsync(employeeId, MonthStart(month), MonthEnd(month), realEngine: realEngine);

    /// <summary>POST /api/payroll/recalculate (a correction).</summary>
    internal Task<(HttpStatusCode Status, string Json)> RecalculateAsync(
        string employeeId, DateOnly periodStart, DateOnly periodEnd,
        Guid? token = null, decimal? previousFlexBalance = null, bool realEngine = false)
    {
        var body = new Dictionary<string, object?>
        {
            ["profile"] = Profile(employeeId),
            ["entries"] = Array.Empty<TimeEntry>(),
            ["absences"] = Array.Empty<AbsenceEntry>(),
            ["periodStart"] = periodStart,
            ["periodEnd"] = periodEnd,
            ["reason"] = "S146 flex-ledger fact",
            ["idempotencyToken"] = token ?? Guid.NewGuid(),
        };
        if (previousFlexBalance is not null)
            body["previousFlexBalance"] = previousFlexBalance;
        return PostAsync("/api/payroll/recalculate", body, realEngine);
    }

    internal Task<(HttpStatusCode Status, string Json)> RecalculateMonthAsync(
        string employeeId, DateOnly month, Guid? token = null, bool realEngine = false) =>
        RecalculateAsync(employeeId, MonthStart(month), MonthEnd(month), token, realEngine: realEngine);

    internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    internal static PeriodCalculationResult ReadResult(string json) =>
        JsonSerializer.Deserialize<PeriodCalculationResult>(json, Web)
        ?? throw new InvalidOperationException("calculate-and-export answered 200 with an empty body");

    internal static RetroactiveCorrectionResult ReadCorrection(string json) =>
        JsonSerializer.Deserialize<RetroactiveCorrectionResult>(json, Web)
        ?? throw new InvalidOperationException("recalculate answered 200 with an empty body");

    internal static DateOnly MonthStart(DateOnly day) => new(day.Year, day.Month, 1);

    internal static DateOnly MonthEnd(DateOnly day) => MonthStart(day).AddMonths(1).AddDays(-1);

    // ═════════════════════════════════════════════════════════════════════
    // Seeding
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>users + employee_profiles + user_agreement_codes + the organization; employed throughout unless a
    /// window is given.</summary>
    internal async Task SeedEmployeeAsync(string employeeId, DateOnly? employmentStart = null, DateOnly? employmentEnd = null)
    {
        await using (var conn = Db.Create())
        {
            await conn.OpenAsync();
            await RegressionSeed.SeedEmployeeAsync(conn, employeeId, OrgId, Agreement, Ok, partTimeFraction: 1.0m);
        }
        if (employmentStart is not null || employmentEnd is not null)
            await SetEmploymentWindowAsync(employeeId, employmentStart, employmentEnd);
    }

    internal async Task SetEmploymentWindowAsync(string employeeId, DateOnly? start, DateOnly? end)
    {
        await using var conn = Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_start_date = @s, employment_end_date = @e WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("s", (object?)start ?? DBNull.Value);
        cmd.Parameters.AddWithValue("e", (object?)end ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", employeeId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    internal async Task SetPartTimeFractionAsync(string employeeId, decimal fraction)
    {
        await using var conn = Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE employee_profiles SET part_time_fraction = @f WHERE employee_id = @id AND effective_to IS NULL", conn);
        cmd.Parameters.AddWithValue("f", fraction);
        cmd.Parameters.AddWithValue("id", employeeId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>An APPROVED MONTHLY approval period for the whole calendar month of <paramref name="month"/>.</summary>
    internal Task SeedApprovedMonthAsync(string employeeId, DateOnly month) =>
        SeedApprovalAsync(employeeId, MonthStart(month), MonthEnd(month), "MONTHLY");

    internal async Task SeedApprovalAsync(string employeeId, DateOnly start, DateOnly end, string periodType)
    {
        await using var conn = Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (
                period_id, employee_id, org_id, period_start, period_end, period_type, status,
                submitted_at, submitted_by, approved_by, approved_at, agreement_code, ok_version)
            VALUES (
                gen_random_uuid(), @emp, @org, @start, @end, @type, 'APPROVED',
                NOW(), @emp, 'ROOT-S146', NOW(), @agreement, @ok)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("type", periodType);
        cmd.Parameters.AddWithValue("agreement", Agreement);
        cmd.Parameters.AddWithValue("ok", Ok);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>One RAW time-entry projection row per WEEKDAY of [from, to]: a current TIME_POST row of
    /// <paramref name="hours"/> hours (payroll reads its own hours; the body carries none).</summary>
    internal async Task SeedWeekdayHoursAsync(string employeeId, DateOnly from, DateOnly to, decimal hours = 7.4m)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await using var conn = Db.Create();
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO time_entries_projection
                    (event_id, employee_id, date, hours, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, outbox_id)
                VALUES
                    (gen_random_uuid(), @emp, @date, @hours, @agreement, @ok, FALSE, NOW(), @outbox)
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", d);
            cmd.Parameters.AddWithValue("hours", hours);
            cmd.Parameters.AddWithValue("agreement", Agreement);
            cmd.Parameters.AddWithValue("ok", Ok);
            cmd.Parameters.AddWithValue("outbox", Interlocked.Increment(ref _outboxSeq));
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
    }

    /// <summary>
    /// A RAW record (no balances: what /export and /export-period write) for the month, holding
    /// <paramref name="lines"/> (empty by default) with the matching content hash.
    /// </summary>
    internal async Task SeedRawRecordAsync(string employeeId, DateOnly month, IReadOnlyList<PayrollExportLine>? lines = null)
    {
        var ordered = PayrollExportManifest.OrderLines(lines ?? Array.Empty<PayrollExportLine>());
        await using var conn = Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records (
                export_id, period_id, employee_id, year, month, exported_at,
                original_lines, current_effective_lines, content_hash, source)
            VALUES (
                @id, NULL, @emp, @year, @month, NOW(),
                @lines::jsonb, @lines::jsonb, @hash, 'EXPORT')
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("year", month.Year);
        cmd.Parameters.AddWithValue("month", month.Month);
        cmd.Parameters.AddWithValue("lines", NpgsqlTypes.NpgsqlDbType.Jsonb, PayrollExportManifest.Serialize(ordered));
        cmd.Parameters.AddWithValue("hash", PayrollExportManifest.ComputeContentHash(ordered));
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>
    /// A LEDGER record (all three balance columns set, no lines) for the month. Its content hash is the hash of the
    /// EMPTY line set, so a zero-hours re-run of the same month reaches the (opening, provenance, closing) compare.
    /// </summary>
    internal Task SeedLedgerRecordAsync(
        string employeeId, DateOnly month, decimal closing, decimal opening = 0m,
        string provenance = FlexLedgerOpening.LedgerStart) =>
        FlexLedgerSeed.SeedAsync(
            ConnectionString, employeeId, month, closing, opening, provenance,
            contentHash: EmptyLinesHash);

    /// <summary>The content hash of an empty line set: <see cref="PayrollExportManifest.ComputeContentHash"/> on an empty
    /// list (R-50: the one definition; <c>PayrollExportService.EmptyLineSetContentHash</c> no longer exists).</summary>
    internal static string EmptyLinesHash => PayrollExportManifest.ComputeContentHash(Array.Empty<PayrollExportLine>());

    /// <summary>A FLEX_PAYOUT export line (what a payout adds to the record), dated in the month.</summary>
    internal static PayrollExportLine PayoutLine(string employeeId, DateOnly date, decimal hours) => new()
    {
        EmployeeId = employeeId,
        WageType = "SLS_0610",
        Hours = hours,
        Amount = hours,
        PeriodStart = date,
        PeriodEnd = date,
        OkVersion = Ok,
        SourceRuleId = "FLEX_BALANCE",
        SourceTimeType = "FLEX_PAYOUT",
    };

    // ═════════════════════════════════════════════════════════════════════
    // Reads
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>One export record as read back. The line lists are derived from the stored JSON text, which is a
    /// positional member, so two reads of an unchanged row are EQUAL (a list member would compare by reference).</summary>
    internal sealed record ExportRecordRow(
        Guid ExportId, string ContentHash, decimal? Opening, decimal? Closing, string? Provenance, string Source,
        string OriginalLinesJson, string CurrentLinesJson)
    {
        public IReadOnlyList<PayrollExportLine> OriginalLines => PayrollExportManifest.Deserialize(OriginalLinesJson);

        public IReadOnlyList<PayrollExportLine> CurrentLines => PayrollExportManifest.Deserialize(CurrentLinesJson);
    }

    internal async Task<ExportRecordRow?> ReadRecordAsync(string employeeId, DateOnly month)
    {
        await using var conn = Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT export_id, content_hash, opening_balance, closing_balance, balance_provenance, source,
                   original_lines::text, current_effective_lines::text
            FROM payroll_export_records
            WHERE employee_id = @e AND year = @y AND month = @m
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", month.Year);
        cmd.Parameters.AddWithValue("m", month.Month);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return new ExportRecordRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetDecimal(2),
            reader.IsDBNull(3) ? null : reader.GetDecimal(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7));
    }

    /// <summary>The record, or a failure naming the employee and month (a missing record is the finding).</summary>
    internal async Task<ExportRecordRow> RequireRecordAsync(string employeeId, DateOnly month)
    {
        var row = await ReadRecordAsync(employeeId, month);
        Assert.True(row is not null, $"expected a payroll_export_records row for {employeeId} {month:yyyy-MM}");
        return row!;
    }

#pragma warning disable CA2100 // every sql here is a hardcoded literal; values flow through parameters.
    internal async Task<int> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
#pragma warning restore CA2100

    /// <summary>Marks an idempotency token as already delivered, the way the correction's own marker does
    /// (<c>outbox_messages</c> status 'delivered'), so <c>IdempotencyGuard.HasBeenDeliveredAsync</c> answers true.</summary>
    internal async Task PreMarkTokenDeliveredAsync(Guid token)
    {
        await using var conn = Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO outbox_messages (destination, payload, status, delivered_at, idempotency_token)
            VALUES ('retroactive-correction', '{}', 'delivered', NOW(), @t)
            """, conn);
        cmd.Parameters.AddWithValue("t", token);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    internal Task<int> CountRecordsAsync(string employeeId) =>
        CountAsync("SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", employeeId));

    internal Task<int> CountManifestsAsync(string employeeId) =>
        CountAsync("SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", employeeId));

    /// <summary>PayrollExportGenerated events of the employee (stream <c>employee-{id}</c>) on the outbox (this host
    /// runs no publisher, so they are on <c>outbox_events</c>).</summary>
    internal Task<int> CountExportEventsAsync(string employeeId) =>
        CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'PayrollExportGenerated'",
            ("s", $"employee-{employeeId}"));

    internal Task<int> CountCorrectionEventsAsync(string employeeId) =>
        CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id LIKE @s AND event_type = 'RetroactiveCorrectionRequested'",
            ("s", $"retro-correction-{employeeId}-%"));

    internal Task<int> CountAuditRowsAsync(string employeeId, string eventType) =>
        CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE target_resource_id = @e AND event_type = @t",
            ("e", employeeId), ("t", eventType));

    /// <summary>The newest event payload of the type for the employee, as a JSON document.</summary>
    internal async Task<JsonDocument> LatestEventAsync(string employeeId, string eventType)
    {
        await using var conn = Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload::text FROM outbox_events
            WHERE event_type = @t AND (stream_id = @exact OR stream_id LIKE @like)
            ORDER BY outbox_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("t", eventType);
        cmd.Parameters.AddWithValue("exact", $"employee-{employeeId}");
        cmd.Parameters.AddWithValue("like", $"retro-correction-{employeeId}-%");
        var payload = await cmd.ExecuteScalarAsync() as string;
        Assert.True(payload is not null, $"no {eventType} event for {employeeId}");
        return JsonDocument.Parse(payload!);
    }

    internal async Task<JsonDocument> LatestAuditDetailsAsync(string employeeId, string eventType)
    {
        await using var conn = Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT details::text FROM audit_projection
            WHERE target_resource_id = @e AND event_type = @t
            ORDER BY projected_at DESC, outbox_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", eventType);
        var details = await cmd.ExecuteScalarAsync() as string;
        Assert.True(details is not null, $"no {eventType} audit row for {employeeId}");
        return JsonDocument.Parse(details!);
    }

    /// <summary>A decimal member of a JSON object, or null when absent or null.</summary>
    internal static decimal? Dec(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : null;

    /// <summary>A string member of a JSON object, or null when absent or null.</summary>
    internal static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static bool Has(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;
}
