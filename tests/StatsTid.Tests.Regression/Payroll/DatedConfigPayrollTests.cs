using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using StatsTid.Auth;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14828-T (ADR-043 D12, ADR-016 D10) - the shared Docker fixture of the payroll facts that prove pay
/// follows the DATED, CONFIGURED values: a fresh database with the full schema, a Backend host (the routes an
/// administrator publishes through; <see cref="DatedConfigDockerFixture"/>), and the REAL Payroll host over the same
/// database with the REAL Rule Engine behind it (<see cref="PayrollHostFactory"/>, <c>useRealRuleEngine: true</c>),
/// so a published value really reaches a pay line and nothing on the calculation path is stubbed. One container per
/// fact (xunit builds one instance per fact).
///
/// <para>
/// <b>Boot order (PAT-008).</b> A fact calls <c>AdminAt(...)</c> (it boots a Backend host, whose startup seeders run)
/// before it seeds anyone. Recorded hours are raw <c>time_entries_projection</c> rows (7.4 h on every weekday, the
/// shape the other payroll host facts seed), approvals are SQL rows (an APPROVED monthly period is the route's guard
/// and the previous-month check's input), and the person is seeded by <see cref="RegressionSeed"/> (the organisation
/// row starts at the SQL literal <c>0001-01-01</c>).
/// </para>
/// </summary>
public abstract class DatedPayrollFixture : DatedConfigDockerFixture, IAsyncLifetime
{
    /// <summary>The monthly day norm of a 37-hour full-timer.</summary>
    protected const decimal DayNorm37 = 7.4m;

    private PayrollHostFactory? _payroll;
    private long _outboxSeq = 8_000_000;

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await TestFixtures.SeedWageTypeMappingsAsync(Db);
    }

    public new async Task DisposeAsync()
    {
        if (_payroll is not null)
            await _payroll.DisposeAsync();
        await base.DisposeAsync();
    }

    private PayrollHostFactory PayrollHost =>
        _payroll ??= new PayrollHostFactory(ConnectionString, new RuleRegistry().GetAll(), useRealRuleEngine: true);

    // ── Payroll routes ────────────────────────────────────────────────────────────────────────

    private static string MintGlobalAdminToken() =>
        new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = PayrollHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: "ROOT-S148", name: "ROOT-S148", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    private static EmploymentProfile PayrollProfile(string employeeId, string agreementCode, string okVersion, string orgId, string? position) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = orgId,
        Position = position,
    };

    /// <summary><c>POST /api/payroll/calculate-and-export</c> as GlobalAdmin; the body carries the profile and the period only.</summary>
    protected async Task<(HttpStatusCode Status, string Json)> CalculateAndExportAsync(
        string employeeId, string agreementCode, string okVersion, string orgId, DateOnly start, DateOnly end,
        string? position = null, Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configureServices = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/calculate-and-export")
        {
            Content = JsonContent.Create(new
            {
                profile = PayrollProfile(employeeId, agreementCode, okVersion, orgId, position),
                periodStart = start,
                periodEnd = end,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        var response = await PayrollClient(configureServices).SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The Payroll host's client; <paramref name="configureServices"/> swaps registrations on a derived host (a fact about a failing dependency).</summary>
    private HttpClient PayrollClient(Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configureServices) =>
        (configureServices is null
            ? (Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<RetroactiveCorrectionService>)PayrollHost
            : PayrollHost.WithWebHostBuilder(b => b.ConfigureTestServices(configureServices))).CreateClient();

    /// <summary><c>POST /api/payroll/recalculate</c> as GlobalAdmin (a correction of an exported month).</summary>
    protected async Task<(HttpStatusCode Status, string Json)> RecalculateAsync(
        string employeeId, string agreementCode, string okVersion, string orgId, DateOnly start, DateOnly end,
        string? position = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/recalculate")
        {
            Content = JsonContent.Create(new
            {
                profile = PayrollProfile(employeeId, agreementCode, okVersion, orgId, position),
                periodStart = start,
                periodEnd = end,
                reason = "S148 TASK-14828-T: the configuration changed",
                idempotencyToken = Guid.NewGuid(),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        var response = await PayrollHost.CreateClient().SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    protected static PeriodCalculationResult ReadResult(string json) =>
        JsonSerializer.Deserialize<PeriodCalculationResult>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("calculate-and-export answered 200 with an empty body");

    /// <summary>The month's norm figure: the NORM_CHECK_37H result(s)' norm total (the period figures ride on one result).</summary>
    protected static decimal NormTotal(PeriodCalculationResult result) =>
        result.RuleResults.Where(r => r.RuleId == "NORM_CHECK_37H").Sum(r => r.NormHoursTotal ?? 0m);

    protected static void AssertOk((HttpStatusCode Status, string Json) response) =>
        Assert.True(response.Status == HttpStatusCode.OK, $"expected 200, got {(int)response.Status}: {response.Json}");

    // ── Seeding ───────────────────────────────────────────────────────────────────────────────

    /// <summary>users + employee_profiles + user_agreement_codes + the first user_organisations row (from <c>0001-01-01</c>).</summary>
    protected Task SeedEmployeeAsync(
        string employeeId, string orgId, string agreementCode, string okVersion = Ok26, string? position = null) =>
        RegressionSeed.SeedEmployeeAsync(
            ConnectionString, employeeId, orgId, agreementCode, okVersion, partTimeFraction: 1.0m, position: position);

    /// <summary>The recorded hours: <paramref name="hours"/> on every weekday in the range, as raw projection rows.</summary>
    protected async Task SeedWeekdayHoursAsync(
        string employeeId, string agreementCode, string okVersion, DateOnly from, DateOnly to, decimal hours = DayNorm37)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await SeedHoursOnAsync(employeeId, agreementCode, okVersion, day, hours);
        }
    }

    /// <summary>One recorded-hours row on one date (a weekend date included).</summary>
    protected Task SeedHoursOnAsync(string employeeId, string agreementCode, string okVersion, DateOnly day, decimal hours) =>
        ExecAsync(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, @hours, @agreement, @ok, FALSE, NOW(), @outbox)
            """,
            ("emp", employeeId), ("date", day), ("hours", hours), ("agreement", agreementCode), ("ok", okVersion),
            ("outbox", _outboxSeq++));

    /// <summary>An APPROVED monthly approval period (the route's guard, and the previous-month check's input).</summary>
    protected Task SeedApprovedMonthAsync(
        string employeeId, string orgId, string agreementCode, string okVersion, DateOnly start, DateOnly end) =>
        ExecAsync(
            """
            INSERT INTO approval_periods (
                period_id, employee_id, org_id, period_start, period_end, period_type, status,
                submitted_at, submitted_by, approved_by, approved_at, agreement_code, ok_version)
            VALUES (
                gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', 'APPROVED',
                NOW(), @emp, 'ROOT-S148', NOW(), @agreement, @ok)
            """,
            ("emp", employeeId), ("org", orgId), ("start", start), ("end", end), ("agreement", agreementCode), ("ok", okVersion));

    /// <summary>The employee's one <c>segment_manifests</c> row: its segments' (start, end) in order.</summary>
    protected async Task<IReadOnlyList<(DateOnly Start, DateOnly End)>> ManifestSegmentsAsync(string employeeId)
    {
        await using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT segments_jsonb::text FROM segment_manifests WHERE employee_id = @e ORDER BY created_at DESC LIMIT 1", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        var json = (string?)await cmd.ExecuteScalarAsync() ?? throw new Xunit.Sdk.XunitException($"no segment_manifests row for {employeeId}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(s => (
                DateOnly.Parse(s.GetProperty("startDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                DateOnly.Parse(s.GetProperty("endDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
    }

    /// <summary>
    /// A validation-passing HK (or other code) config body with <paramref name="weeklyNorm"/>; the other members are
    /// <see cref="DatedConfigDockerFixture.ConfigBody"/>'s, which every dated fact publishes. A fact asserts only what
    /// the weekly norm changes (the norm figure), never a line another member of the body could move.
    /// </summary>
    protected static object HkBody(decimal weeklyNorm, string okVersion = Ok26, string agreementCode = "HK")
        => ConfigBody(agreementCode, okVersion, weeklyNorm);
}

/// <summary>
/// S148 / TASK-14828-T (AC-C1b, AC-C2a pay half) - payroll pays on a published configuration dated into the month.
///
/// <para>
/// <b>In plain language.</b> An administrator publishes HK's agreement with a 32-hour week "applying from" a
/// Monday. Payroll must price each day of the month with the version in force that day: a version dated before the
/// month prices the whole month at 32 h; one dated in the middle of the month prices the days before it at 37 h and
/// the days from it at 32 h, and the month is calculated in two parts. Before this stack payroll read the values
/// compiled into the code, so a published value never reached pay.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DatedConfigPayrollTests : DatedPayrollFixture
{
    private const string OrgId = "STY01";

    // 4 May and 15 June 2026 are Mondays; June 2026 opens on a Monday (no context days, no previous month to approve).
    private static readonly DateOnly Clock = new(2026, 11, 10);
    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C1b. HK/OK26 republished with a 32-hour weekly norm applying from Monday 4 May 2026; an
    /// HK full-timer with 7.4 h on every June weekday, June APPROVED. <c>calculate-and-export</c> June 2026 answers 200 and
    /// the month's norm figure is <b>140.80</b> h (22 weekdays at 32 / 5 = 6.40), not the 162.80 of the compiled 37 h.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). On the code before the stack payroll sent
    /// the engine no configuration and the engine resolved the COMPILED HK/OK26 (37 h): the norm total is 162.80 and
    /// <c>Assert.Equal(140.80m, ...)</c> fails. (The same assertion fails under any mutation that reads the compiled
    /// table or ignores the applies-from date.)
    /// </para>
    /// </summary>
    [Fact]
    public async Task PublishedEditBeforeMonth_ChangesThatMonthsLines()
    {
        using var admin = AdminAt(Clock);
        const string employeeId = "EMP-S148-PAY-EDIT";
        await CreateAndPublishAsync(admin, HkBody(32m), "2026-05-04");
        await SeedEmployeeAsync(employeeId, OrgId, "HK");
        await SeedWeekdayHoursAsync(employeeId, "HK", Ok26, Jun01, Jun30);
        await SeedApprovedMonthAsync(employeeId, OrgId, "HK", Ok26, Jun01, Jun30);

        var response = await CalculateAndExportAsync(employeeId, "HK", Ok26, OrgId, Jun01, Jun30);

        AssertOk(response);
        Assert.Equal(140.80m, NormTotal(ReadResult(response.Json)));
    }

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C2a, pay half. HK/OK26 published with a 32-hour weekly norm applying from Monday 15 June
    /// 2026 (the seeded 37 h is in force before it); an HK full-timer, 7.4 h on every June weekday, June APPROVED. The
    /// month is calculated in TWO parts, 1-14 and 15-30 June (the planner's cut is TASK-14818-T's; asserted here as the
    /// manifest's segments), and the norm figure is <b>150.80</b> h: 10 weekdays before the 15th at 7.40 plus 12 from it at
    /// 6.40.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). Before the stack both parts were priced
    /// on the compiled 37 h: the norm total is 162.80, failing the 150.80 assertion (the two-segment assertion passes
    /// after TASK-14818 and fails before it).
    /// </para>
    /// </summary>
    [Fact]
    public async Task EachPartPaidUnderItsOwnVersion()
    {
        using var admin = AdminAt(Clock);
        const string employeeId = "EMP-S148-PAY-SPLIT";
        await CreateAndPublishAsync(admin, HkBody(32m), "2026-06-15");
        await SeedEmployeeAsync(employeeId, OrgId, "HK");
        await SeedWeekdayHoursAsync(employeeId, "HK", Ok26, Jun01, Jun30);
        await SeedApprovedMonthAsync(employeeId, OrgId, "HK", Ok26, Jun01, Jun30);

        var response = await CalculateAndExportAsync(employeeId, "HK", Ok26, OrgId, Jun01, Jun30);

        AssertOk(response);
        var segments = await ManifestSegmentsAsync(employeeId);
        Assert.Equal(
            new[] { (Jun01, new DateOnly(2026, 6, 14)), (new DateOnly(2026, 6, 15), Jun30) },
            segments.ToArray());
        Assert.Equal(150.80m, NormTotal(ReadResult(response.Json)));
    }
}
