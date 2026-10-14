using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
/// S145 / TASK-14503a — HOST-LEVEL pins against the REAL Rule Engine: what the Payroll host actually pays
/// for a month, with the engine's own arithmetic, the host's own hours read and nothing stubbed on the
/// calculation path. Three facts: the QUAL-186 "a month is judged as one week" defect for HK and for AC
/// (A1), and the R14 HTTP fact (payroll reads its OWN hours from the projections).
///
/// <para>
/// <b>The story, in plain language.</b> Payroll used to be handed the month's hours in the request and judged
/// the whole month against ONE week's norm: an employee who worked exactly their day norm (7.4 h) on every
/// weekday of September 2026 was "paid" 125.8 h of overtime (HK) or merarbejde (AC) and a 25.8 h flex payout.
/// The fix has two halves, and these facts pin both through HTTP. (1) The engine judges week by week
/// (Monday-Sunday); a week belongs to the month that holds its Sunday and is dated that Sunday, and the days of
/// the opening week that fall in the previous month ("context days", here 31 August) count toward the week's
/// judgement but never produce a <c>NORMAL_HOURS</c> line or enter the norm total. (2) Payroll reads the
/// employee's hours itself, from <c>time_entries_projection</c>; the request carries none. The previous month
/// must be APPROVED when the opening week reaches it (R5(b), R26's host-fact rule), so every fact seeds it.
/// </para>
///
/// <para>
/// <b>What is real, what is seeded.</b> The host is <see cref="PayrollHostFactory"/> with
/// <c>useRealRuleEngine: true</c> (an in-process Rule Engine, the production dispatch and the live
/// classification set). Hours are seeded as RAW projection rows (<c>INSERT INTO time_entries_projection</c>);
/// every other column keeps its default, which is the TIME_POST source (one registration per day) once the
/// schema carries it. The body carries the profile, the period and an opening flex balance of 0, and
/// <b>no hours</b>: <c>entries</c> and <c>absences</c> are sent as EMPTY lists, which both the pre-S145 request
/// (those properties are <c>required</c> today) and the post-R14 request (optional; empty accepted, non-empty
/// refused 400) take. Sending empty lists, rather than omitting the members, makes a fact fail today on an
/// ASSERTION about what was paid, not on a binding error.
/// </para>
///
/// <para>
/// <b>Fixture conditions (plan § "RED evidence" → "Fixture conditions").</b> C-RE1: the AC fact asserts no
/// merarbejde line and no flex payout, and does NOT inspect <c>NORMAL_HOURS</c>. C-RE2: the October fact also
/// asserts that every <c>NORMAL_HOURS</c> line is dated in October. C-RE3: both September facts carry exactly
/// the day norm (7.40 h) on every weekday, 31 August included.
/// </para>
///
/// <para>
/// Docker-gated (Testcontainers Postgres + full <c>init.sql</c>): CI-verified, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollHostRealEngineTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";
    private const string OrgId = "STY_S145_REALENGINE";
    private const string OkVersion = "OK26";

    /// <summary>The full-time day norm: 37 h / 5.</summary>
    private const decimal DayNorm = 7.4m;

    private static readonly DateOnly Aug01 = new(2026, 8, 1);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);
    private static readonly DateOnly Sep01 = new(2026, 9, 1);
    private static readonly DateOnly Sep28 = new(2026, 9, 28);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);
    private static readonly DateOnly Oct01 = new(2026, 10, 1);
    private static readonly DateOnly Oct04 = new(2026, 10, 4);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    private PostgreSqlContainer _container = null!;
    private DbConnectionFactory _db = null!;
    private PayrollHostFactory _factory = null!;
    private long _outboxSeq = 1000;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage(ImageTag)
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await _container.StartAsync();

        var cs = _container.GetConnectionString();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(cs);
        _db = new DbConnectionFactory(cs);
        await TestFixtures.SeedWageTypeMappingsAsync(_db);

        // The LIVE rule set for the planner; the REAL engine for every rule call.
        _factory = new PayrollHostFactory(cs, new RuleRegistry().GetAll(), useRealRuleEngine: true);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // RE1 — A1 defect, HK
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// RE1. Full-time HK, 7.40 h on every weekday of September 2026 and on Monday 31 August (the opening week's
    /// context day), August and September APPROVED, opening flex balance 0: <c>POST
    /// /api/payroll/calculate-and-export</c> for September answers <b>200</b> and pays NOTHING extra: no
    /// <c>OVERTIME_50</c>, no <c>OVERTIME_100</c>, no <c>FLEX_PAYOUT</c> line. The window contract holds at the
    /// host: the <c>NORMAL_HOURS</c> lines are exactly the 22 September weekdays (sum 162.80, NONE dated 31
    /// August), <c>NormHoursTotal</c> and <c>ActualHoursTotal</c> are 162.80, and the month is exported once.
    ///
    /// <para>
    /// <b>Before the fix</b> (refinement A1, Stage A "Defect pinned"): 3.0 h <c>OVERTIME_50</c>, 122.8 h
    /// <c>OVERTIME_100</c>, <c>NormHoursTotal</c> 37, flex delta +125.8 and a 25.8 h <c>FLEX_PAYOUT</c> — the
    /// month judged as one 37 h week. (Those figures are recorded from the Unit characterization file
    /// <c>MonthAsWeekCharacterizationTests</c>.)
    /// </para>
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (0) On today's master the host takes its hours from the request body and ignores the seeded rows, so with
    /// empty lists the month has no hours: the 22-date <c>NORMAL_HOURS</c> assertion and the 162.80 sums trip
    /// (0 lines, 0.00 h, <c>NormHoursTotal</c> 37).
    /// (1) mutation MD-42 — the timeline endpoint handler hands the core <c>PeriodStart = Pieces[0].From</c>
    /// instead of the request's <c>PeriodStart</c>: Monday 31 August becomes an in-period day, so the response
    /// carries a <c>NORMAL_HOURS</c> line dated 31 August (23 lines, sum 170.20, <c>NormHoursTotal</c> 170.20):
    /// the exact-dates assertion, the "none dated 31 August" assertion and the 162.80 sums trip.
    /// (2) mutation MD-21 — the <c>IPriorPeriodApprovalReader</c> registration removed (PCS gets a null reader;
    /// R21's throw escapes the TestServer, 500 on Kestrel): the fact fails before or at the 200 assertion.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_September_FullTimeHK_AtNorm_NoOvertimeNoFlexPayout_RealEngine()
    {
        const string employeeId = "EMP-S145-RE-HK";
        await SeedEmployeeAsync(employeeId, "HK");
        await SeedApprovedMonthAsync(employeeId, "HK", Aug01, Aug31);
        await SeedApprovedMonthAsync(employeeId, "HK", Sep01, Sep30);
        foreach (var date in SeptemberAtNormDates())
            await SeedHoursAsync(employeeId, "HK", date, DayNorm);

        var (status, json) = await CalculateAndExportAsync(employeeId, "HK", Sep01, Sep30);

        Assert.True(status == HttpStatusCode.OK, $"expected 200, got {(int)status}: {json}");
        var result = ReadResult(json);
        var normal = LineItemsOf(result, "NORMAL_HOURS");
        var normalDates = normal.Select(li => li.Date).OrderBy(d => d).ToList();
        var norm = result.RuleResults.Single(r => r.RuleId == "NORM_CHECK_37H");
        var exportRecords = await CountExportRecordsAsync(employeeId, 2026, 9);

        Assert.Multiple(
            // No overtime, no flex payout: every Monday-Sunday week is exactly at norm.
            () => Assert.Empty(LineItemsOf(result, "OVERTIME_50")),
            () => Assert.Empty(LineItemsOf(result, "OVERTIME_100")),
            () => Assert.Empty(LineItemsOf(result, "FLEX_PAYOUT")),
            // Window contract (R6): one NORMAL_HOURS line per entry dated in September, none for 31 August.
            () => Assert.Equal(WeekdaysBetween(Sep01, Sep30).ToList(), normalDates),
            () => Assert.DoesNotContain(Aug31, normalDates),
            () => Assert.Equal(162.80m, normal.Sum(li => li.Hours)),
            () => Assert.Equal(162.80m, norm.NormHoursTotal),
            () => Assert.Equal(162.80m, norm.ActualHoursTotal),
            // Exported once.
            () => Assert.Equal(1, exportRecords));
    }

    // ═════════════════════════════════════════════════════════════════════
    // RE2 — A1 defect, AC
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// RE2. The AC twin of RE1: full-time AC, 7.40 h on every weekday of September 2026 and on Monday 31
    /// August, August and September APPROVED, opening flex balance 0: the September <c>calculate-and-export</c>
    /// answers <b>200</b> and books NO merarbejde (no <c>MERARBEJDE</c> line, none exported) and NO flex payout.
    /// The month is exported once: that is the non-vacuity guard (below).
    ///
    /// <para>
    /// <b>Before the fix</b> (refinement Stage A "Defect pinned": "the AC twin: before 125.8 merarbejde, after
    /// 0"): 125.8 h <c>MERARBEJDE</c> — the month judged as one 37 h week. As C-RE1 requires, this fact asserts
    /// merarbejde and payout only; it does NOT inspect <c>NORMAL_HOURS</c> lines or any norm total. (31 August
    /// at exactly the day norm moves neither merarbejde nor the flex payout, so it cannot trip MD-42.)
    /// </para>
    ///
    /// <para>
    /// <b>Non-vacuity guard (a deviation worth reading).</b> "No merarbejde and no payout" is trivially true of
    /// a month in which the host saw NO hours at all, and that is exactly today's master: it ignores the seeded
    /// rows and the body carries none. So the fact also asserts that exactly one <c>payroll_export_records</c>
    /// row exists for September. A month with hours has <c>NORMAL_HOURS</c> lines to export; a month without
    /// hours has nothing, and the host writes no record. This asserts that the month WAS exported — not
    /// what its <c>NORMAL_HOURS</c> lines are — so C-RE1 stands.
    /// </para>
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (0) On today's master the host ignores the seeded rows (hours come from the body, which is empty):
    /// nothing is calculated into export lines, no record is written, and the "exported once" assertion trips
    /// (0 rows, expected 1). The merarbejde and payout assertions pass vacuously today.
    /// (1) mutation MD-21 — the <c>IPriorPeriodApprovalReader</c> registration removed (PCS gets a null reader;
    /// R21's throw escapes the TestServer, 500 on Kestrel): the fact fails before or at the 200 assertion.
    /// MD-42 and MD-20 do not trip this fact (partition table: "Not: RE2").
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_September_FullTimeAC_AtNorm_NoMerarbejde_RealEngine()
    {
        const string employeeId = "EMP-S145-RE-AC";
        await SeedEmployeeAsync(employeeId, "AC");
        await SeedApprovedMonthAsync(employeeId, "AC", Aug01, Aug31);
        await SeedApprovedMonthAsync(employeeId, "AC", Sep01, Sep30);
        foreach (var date in SeptemberAtNormDates())
            await SeedHoursAsync(employeeId, "AC", date, DayNorm);

        var (status, json) = await CalculateAndExportAsync(employeeId, "AC", Sep01, Sep30);

        Assert.True(status == HttpStatusCode.OK, $"expected 200, got {(int)status}: {json}");
        var result = ReadResult(json);
        var exportRecords = await CountExportRecordsAsync(employeeId, 2026, 9);

        Assert.Multiple(
            () => Assert.Empty(LineItemsOf(result, "MERARBEJDE")),
            () => Assert.Empty(result.ExportLines.Where(l => l.SourceTimeType == "MERARBEJDE")),
            () => Assert.Empty(LineItemsOf(result, "FLEX_PAYOUT")),
            // Non-vacuity guard: hours WERE read, so the month had something to export.
            () => Assert.Equal(1, exportRecords));
    }

    // ═════════════════════════════════════════════════════════════════════
    // RE3 — R14 over HTTP, closing week
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// RE3 (the R14 HTTP fact). Full-time HK; the week that straddles the September/October boundary (Monday 28
    /// September - Sunday 4 October; under F1 a week is settled by the month containing its Sunday, so this week
    /// belongs to OCTOBER, its opening week, and is dated Sunday 4 October) is worked 9 h on each of 28, 29 and 30
    /// September and 7.40 h on 1 and 2 October (41.80 h in all); the rest of October at norm (October's last week,
    /// Monday 26 October - Sunday 1 November, belongs to NOVEMBER under F1, not to October; its weekdays 26-30
    /// October are at norm, so it carries no overtime in either month, and their <c>NORMAL_HOURS</c> lines are
    /// still dated in October). The hours exist ONLY as projection rows; the body carries none. September
    /// and October are APPROVED (October because the route checks its own month, September because the opening
    /// week reaches it). <c>POST /api/payroll/calculate-and-export</c> for October answers <b>200</b> and pays
    /// exactly <c>OVERTIME_50</c> 3.00 + <c>OVERTIME_100</c> 1.80 — the 41.80 h against the 37 h norm and the 40
    /// h second threshold — both DATED Sunday 4 October, and writes the same two wage lines
    /// (<c>SLS_0210</c> 3.00, <c>SLS_0220</c> 1.80) into the export record. That 28-30 September (context days of
    /// October) were read at all proves payroll reads its own hours over the whole opening week.
    ///
    /// <para>
    /// The window contract (R6) at host level, and MD-42's observation point (C-RE2): every
    /// <c>NORMAL_HOURS</c> line is dated in October (22 lines for the 22 October weekdays, sum 162.80), none for
    /// the September context days even though they carry 9 h.
    /// </para>
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (0) On today's master the host ignores the seeded rows and takes its hours from the (empty) body: no
    /// overtime is paid and no <c>NORMAL_HOURS</c> line exists — the overtime and 22-line assertions trip.
    /// (1) mutation MD-20 — <c>ProjectionHoursReader.ReadCurrentAsync</c> starts at the first day of
    /// <c>toInclusive</c>'s month instead of <c>fromInclusive</c>: 28-30 September are never read, the closing
    /// week holds 14.80 h against a 37 h norm, and the exact overtime-lines assertion trips (expected 3.00 + 1.80
    /// dated 4 October, got none).
    /// (2) mutation MD-42 — the timeline endpoint handler hands the core <c>PeriodStart = Pieces[0].From</c>:
    /// 28-30 September become in-period days and the response carries <c>NORMAL_HOURS</c> lines dated in
    /// September (25 lines): the "every line dated in October" and 22-line assertions trip.
    /// (3) mutation MD-21 — the <c>IPriorPeriodApprovalReader</c> registration removed: R21's throw escapes the
    /// TestServer (500 on Kestrel); the fact fails before or at the 200 assertion.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_October_HoursFromProjections_ClosingWeekPaidDated4Oct_RealEngine()
    {
        const string employeeId = "EMP-S145-RE-R14";
        await SeedEmployeeAsync(employeeId, "HK");
        await SeedApprovedMonthAsync(employeeId, "HK", Sep01, Sep30);
        await SeedApprovedMonthAsync(employeeId, "HK", Oct01, Oct31);

        // October's opening week (28 Sep - 4 Oct, F1): 28-30 September at 9 h (context days of October),
        // 1-2 October at the day norm.
        foreach (var date in WeekdaysBetween(Sep28, Sep30))
            await SeedHoursAsync(employeeId, "HK", date, 9.0m);
        // The rest of October at norm (1-2 October above are part of this loop: every October weekday).
        var octoberWeekdays = WeekdaysBetween(Oct01, Oct31).ToList();
        foreach (var date in octoberWeekdays)
            await SeedHoursAsync(employeeId, "HK", date, DayNorm);

        var (status, json) = await CalculateAndExportAsync(employeeId, "HK", Oct01, Oct31);

        Assert.True(status == HttpStatusCode.OK, $"expected 200, got {(int)status}: {json}");
        var result = ReadResult(json);

        // The overtime the engine booked: exactly these two lines, both dated the closing Sunday.
        var overtime = result.RuleResults
            .SelectMany(r => r.LineItems)
            .Where(li => li.TimeType is "OVERTIME_50" or "OVERTIME_100" or "MERARBEJDE")
            .OrderBy(li => li.TimeType, StringComparer.Ordinal)
            .Select(li => (li.TimeType, li.Hours, li.Date))
            .ToList();
        var normal = LineItemsOf(result, "NORMAL_HOURS");
        var normalDates = normal.Select(li => li.Date).OrderBy(d => d).ToList();

        // What the export record carries (the lines payroll will send to SLS). A missing record is reported by
        // the count assertion below, not by a read failure that would hide the figures above it.
        var exportRecords = await CountExportRecordsAsync(employeeId, 2026, 10);
        IReadOnlyList<PayrollExportLine> exported = exportRecords == 1
            ? await ReadExportedLinesAsync(employeeId, 2026, 10)
            : Array.Empty<PayrollExportLine>();
        var exportedOvertime = exported
            .Where(l => l.SourceTimeType is "OVERTIME_50" or "OVERTIME_100")
            .OrderBy(l => l.WageType, StringComparer.Ordinal)
            .Select(l => (l.WageType, l.Hours))
            .ToList();

        Assert.Multiple(
            () => Assert.Equal(
                new[] { ("OVERTIME_100", 1.80m, Oct04), ("OVERTIME_50", 3.00m, Oct04) },
                overtime),
            () => Assert.Equal(1, exportRecords),
            () => Assert.Equal(new[] { ("SLS_0210", 3.00m), ("SLS_0220", 1.80m) }, exportedOvertime),
            // Window contract (R6) at host level, and MD-42's observation point (C-RE2): one NORMAL_HOURS line
            // per October weekday, every one dated in October, none for the September context days.
            () => Assert.Equal(octoberWeekdays, normalDates),
            () => Assert.All(normalDates, d => Assert.InRange(d, Oct01, Oct31)),
            () => Assert.Equal(162.80m, normal.Sum(li => li.Hours)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Dates
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Monday 31 August (the opening week's context day) and every weekday 1-30 September 2026.</summary>
    private static IEnumerable<DateOnly> SeptemberAtNormDates() =>
        new[] { Aug31 }.Concat(WeekdaysBetween(Sep01, Sep30));

    private static IEnumerable<DateOnly> WeekdaysBetween(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            yield return d;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // HTTP
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>The body carries the profile, the period and the opening flex balance, and NO hours:
    /// <c>entries</c> / <c>absences</c> are empty lists (see the class remarks).</summary>
    private async Task<(HttpStatusCode Status, string Json)> CalculateAndExportAsync(
        string employeeId, string agreementCode, DateOnly periodStart, DateOnly periodEnd)
    {
        var body = new
        {
            profile = Profile(employeeId, agreementCode),
            entries = Array.Empty<TimeEntry>(),
            absences = Array.Empty<AbsenceEntry>(),
            periodStart,
            periodEnd,
            previousFlexBalance = 0m,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/calculate-and-export")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static PeriodCalculationResult ReadResult(string json) =>
        JsonSerializer.Deserialize<PeriodCalculationResult>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("calculate-and-export answered 200 with an empty body");

    private static List<CalculationLineItem> LineItemsOf(PeriodCalculationResult result, string timeType) =>
        result.RuleResults.SelectMany(r => r.LineItems).Where(li => li.TimeType == timeType).ToList();

    private static string MintGlobalAdminToken()
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = PayrollHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        });

        // GLOBAL scope: OrgScopeValidator (the /calculate-and-export resource guard) admits a
        // GLOBAL-scoped actor for any employee. The Payroll host forwards this same token to the Rule
        // Engine, which validates it against the same dev key.
        return tokenService.GenerateToken(
            employeeId: "ROOT-S145",
            name: "ROOT-S145",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private static EmploymentProfile Profile(string employeeId, string agreementCode) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = agreementCode,
        OkVersion = OkVersion,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = OrgId,
    };

    // ═════════════════════════════════════════════════════════════════════
    // Seeding
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>users + employee_profiles + user_agreement_codes (history-covering '0001-01-01' rows) + the
    /// organization. Employment dates NULL: employed throughout, so 31 August is an EMPLOYED context day.</summary>
    private async Task SeedEmployeeAsync(string employeeId, string agreementCode)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await RegressionSeed.SeedEmployeeAsync(
            conn, employeeId, OrgId, agreementCode, OkVersion, partTimeFraction: 1.0m);
    }

    /// <summary>An APPROVED monthly approval period: the month's own approval (the
    /// <c>/calculate-and-export</c> guard) or the previous month's (R5(b) / R26).</summary>
    private async Task SeedApprovedMonthAsync(
        string employeeId, string agreementCode, DateOnly periodStart, DateOnly periodEnd)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (
                period_id, employee_id, org_id, period_start, period_end, period_type, status,
                submitted_at, submitted_by, approved_by, approved_at, agreement_code, ok_version)
            VALUES (
                gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', 'APPROVED',
                NOW(), @emp, 'ROOT-S145', NOW(), @agreement, @ok)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", periodStart);
        cmd.Parameters.AddWithValue("end", periodEnd);
        cmd.Parameters.AddWithValue("agreement", agreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>One RAW time-entry projection row: one registration for the day, no start/end time, no task.
    /// The columns the S145 schema adds (<c>source</c>, <c>superseded_by_outbox_id</c>) are left to their
    /// defaults — a current TIME_POST row.</summary>
    private async Task SeedHoursAsync(string employeeId, string agreementCode, DateOnly date, decimal hours)
    {
        await using var conn = _db.Create();
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
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("hours", hours);
        cmd.Parameters.AddWithValue("agreement", agreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ═════════════════════════════════════════════════════════════════════
    // Reads
    // ═════════════════════════════════════════════════════════════════════

    private async Task<int> CountExportRecordsAsync(string employeeId, int year, int month)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM payroll_export_records
            WHERE employee_id = @e AND year = @y AND month = @m
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>The export record's current effective lines, through the production manifest deserializer.
    /// Fails if the month has no record: a missing record is the finding, not an empty list.</summary>
    private async Task<IReadOnlyList<PayrollExportLine>> ReadExportedLinesAsync(string employeeId, int year, int month)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT current_effective_lines::text FROM payroll_export_records
            WHERE employee_id = @e AND year = @y AND month = @m
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "no payroll_export_records row for the month");
        return PayrollExportManifest.Deserialize(reader.GetString(0));
    }
}
