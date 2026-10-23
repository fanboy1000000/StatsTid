using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Config;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S145 / TASK-14514 — the calculation pins for the week-by-week payroll core, at PCS level
/// (<see cref="PeriodCalculationService"/>) over an IN-PROCESS REAL Rule Engine, a REAL Postgres, the REAL
/// <see cref="ProjectionHoursReader"/> over seeded projection rows and the REAL
/// <see cref="ApprovalPeriodPriorPeriodReader"/> with each previous month seeded APPROVED (ruling R26's rule
/// for <c>WeeklyPayrollTests</c>). Twelve facts, WP1..WP12 of the plan's fact key.
///
/// <para>
/// <b>The story, in plain language.</b> Payroll used to judge a whole month against one week's norm, and it
/// paid a month's overtime on the month's own days only. S145 makes it judge week by week
/// (Monday-Sunday). A week belongs to the month that holds its <b>Sunday</b> and its overtime is dated that
/// Sunday (ruling Q3 = F1); a leaver's last week is settled on their last employed day (R41); each weekday
/// carries its own norm from that day's contract (Q2 = A); and the days of a month's opening week that fall in
/// the previous month ("context days") count toward the week's judgement but never toward the month's own
/// norm total or its <c>NORMAL_HOURS</c> lines. These facts pin that through the whole payroll path: the hours
/// payroll reads for itself, the pieces it sends the engine, the engine's arithmetic and the wage lines.
/// </para>
///
/// <para>
/// <b>Written from the spec, before the code existed.</b> These pins use the types TASK-14508 / 14511 / 14512
/// created (<c>IRecordedHoursReader</c>, <c>ProjectionHoursReader</c>, <c>IPriorPeriodApprovalReader</c>,
/// <c>ApprovalPeriodPriorPeriodReader</c>) and the PCS signature without hours; re-spawn 1 reconciled the names
/// with the merged code, and no expected value changed. Docker-gated: CI-verified, never locally green.
/// </para>
///
/// <para>
/// <b>Fixture conditions (plan § "Fixture conditions the table relies on").</b>
/// C-W1: WP1-WP7 and WP9 assert overtime / merarbejde lines (amount, date, key), pieces, <c>NormPeriodWeeks</c>,
/// fingerprints and refusals; WP11 asserts flex payouts and balances; none of them asserts a
/// <c>NORMAL_HOURS</c> line or the norm total (so MD-42 reaches only WP8 and WP10).
/// C-W2: every context day of WP11 (31 August; 28-30 September) carries exactly the day norm.
/// C-W3: WP8, WP9, WP11 context days carry exactly the day norm.
/// C-W4: the request profile equals the dated record; only WP12's is stale.
/// C-W5: WP6 and WP7's closing week (28 September - 4 October) carries exactly the day norm.
/// C-W6: WP9 compares with its own no-position-change twin, calculated in the same fact.
/// C-W7: hours are read through the real <see cref="ProjectionHoursReader"/> over seeded rows; the engine is the
/// in-process real one.
/// </para>
///
/// <para>
/// <b>Calendar facts used throughout</b> (2026): Tuesday 1 September; Monday 31 August is the context day of
/// September's opening week; Sundays 2, 16, 30 August, 6, 20, 27 September, 4, 18, 25 October, 5 April; Wednesday
/// 30 September; Monday 27 July opens August's week; 1 April is a Wednesday (OK24 to OK26). A full-time HK
/// weekday is 7.40 h (norm and 50% threshold) and 8.00 h (100% threshold); a Monday-Sunday week is 37.00 /
/// 37.00 / 40.00. HK pays hours between the norm and the 100% threshold as <c>OVERTIME_50</c> and hours above
/// as <c>OVERTIME_100</c>; AC pays the whole excess as <c>MERARBEJDE</c>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class WeeklyPayrollTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";
    private const string OrgId = "STY_S145_WEEKLYPAY";
    private const decimal DayNorm = 7.4m;

    private PostgreSqlContainer _container = null!;
    private DbConnectionFactory _db = null!;
    private PostgresEventStore _store = null!;
    private RuleEngineHost _engine = null!;
    private long _outboxSeq = 5000;

    private static readonly IReadOnlyList<RuleClassification> LiveSet = new RuleRegistry().GetAll();

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
        // S146 R-24: the enqueue-capable store (the manifest moves onto the outbox in S146).
        _store = new PostgresEventStore(_db, new StatsTid.Infrastructure.Outbox.OutboxServiceContext("payroll"));
        await TestFixtures.SeedWageTypeMappingsAsync(_db);
        _engine = new RuleEngineHost();
    }

    public async Task DisposeAsync()
    {
        if (_engine is not null)
            await _engine.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // WP1 — closing week, F1
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WP1 (refinement "Closing week and tail crossing", :583-585). Full-time HK, no absences. 31 August and every
    /// September weekday at the day norm 7.40 h, except 28-30 September at 9 h each; October's weekdays at norm
    /// (1-2 October 7.40 h each). August and September APPROVED. The closing week Monday 28 September - Sunday 4
    /// October holds 3 x 9 + 2 x 7.40 = 41.80 h against the norm 37.00 and the 100% threshold 40.00.
    /// <list type="bullet">
    /// <item><b>September pays nothing</b> for that week: its Sunday (4 October) lies in October (Q3 = F1), so no
    /// <c>OVERTIME_50</c> / <c>OVERTIME_100</c> / <c>MERARBEJDE</c> line is dated in September at all.</item>
    /// <item><b>October pays</b> exactly <c>OVERTIME_50</c> 3.00 (41.80 over 37.00, capped at the 40.00 - 37.00 =
    /// 3.00 band) and <c>OVERTIME_100</c> 1.80 (41.80 - 40.00), both dated Sunday 4 October, and the same two
    /// export lines (<c>SLS_0210</c> 3.00, <c>SLS_0220</c> 1.80).</item>
    /// </list>
    /// Non-vacuity (not a <c>NORMAL_HOURS</c> assertion, C-W1): the September engine request carries the 28-30
    /// September entries at 9.00, so "pays nothing" is not the trivial result of an empty read.
    ///
    /// Red conditions (Docker-gated, CI-verified):
    /// (0) on master the host-less PCS signature and the readers do not exist: compile-RED, not evidence.
    /// (1) mutation MD-20 (<c>ProjectionHoursReader.ReadCurrentAsync</c> starts at the first day of
    /// <c>toInclusive</c>'s month): October never reads 28-30 September, the week holds 14.80 h and the exact
    /// October-lines assertion trips (expected 3.00 + 1.80 dated 4 October, got none).
    /// (2) mutation MD-43 (the cutoff derivation returns the last piece day, <c>periodEnd</c> for a stayer):
    /// September then settles the closing week on Wednesday 30 September with a cutoff of 30 September and pays
    /// 1.80 + 3.00 dated 30 September, so the "September pays nothing" assertion trips.
    /// </summary>
    [Fact]
    public async Task TwoMonths_ClosingWeek_SeptemberPaysNothing_OctoberPays300And180_Dated4Oct()
    {
        const string emp = "EMP-S145-WP1";
        await SeedEmployeeAsync(emp, "HK");
        await SeedApprovedAsync(emp, "HK", Aug01, Aug31);
        await SeedApprovedAsync(emp, "HK", Sep01, Sep30);
        await SeedWeekdaysAsync(emp, "HK", Aug31, Oct30, d =>
            d >= Sep28 && d <= Sep30 ? 9.0m : DayNorm);

        var september = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Sep01, Sep30);
        var october = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Oct01, Oct31);

        Assert.Multiple(
            () => Assert.Empty(OvertimeLines(september.Result)),
            () => Assert.Contains(
                september.Recorder.Timeline.Single().EntryHours(),
                e => e.Date == Sep28 && e.Hours == 9.0m),
            () => Assert.Equal(
                new[] { ("OVERTIME_100", 1.80m, Oct04), ("OVERTIME_50", 3.00m, Oct04) },
                OvertimeLines(october.Result)),
            () => Assert.Equal(
                new[] { ("SLS_0210", 3.00m), ("SLS_0220", 1.80m) },
                ExportedOvertime(october.Result)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // WP2 — tail crossing, F1
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WP2 (refinement :585). The tail-crossing twin of WP1: 28 September - 2 October worked 8 h each (40.00 h),
    /// the rest at norm. September pays nothing for the week (its Sunday is 4 October). October pays exactly
    /// <c>OVERTIME_50</c> 3.00 dated 4 October (40.00 - 37.00 = 3.00; 40.00 is not above the 100% threshold, so no
    /// <c>OVERTIME_100</c>).
    ///
    /// Red conditions (Docker-gated, CI-verified): mutation MD-20 (28-30 September never read, the week holds
    /// 16.00 h, October pays nothing); mutation MD-43 (September settles the week on 30 September with a cutoff:
    /// 3 x 8.00 = 24.00 h against 22.20 and a 100% threshold of 24.00 pays 1.80 dated 30 September, so the
    /// "September pays nothing" assertion trips).
    /// </summary>
    [Fact]
    public async Task TwoMonths_TailCrossing_OctoberPays300_Dated4Oct()
    {
        const string emp = "EMP-S145-WP2";
        await SeedEmployeeAsync(emp, "HK");
        await SeedApprovedAsync(emp, "HK", Aug01, Aug31);
        await SeedApprovedAsync(emp, "HK", Sep01, Sep30);
        await SeedWeekdaysAsync(emp, "HK", Aug31, Oct30, d =>
            d >= Sep28 && d <= Oct02 ? 8.0m : DayNorm);

        var september = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Sep01, Sep30);
        var october = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Oct01, Oct31);

        Assert.Multiple(
            () => Assert.Empty(OvertimeLines(september.Result)),
            () => Assert.Equal(
                new[] { ("OVERTIME_50", 3.00m, Oct04) },
                OvertimeLines(october.Result)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // WP3 — no duplicate, no gap
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WP3 (refinement "No duplicate, no gap", :596). July, August and September APPROVED (August's opening week
    /// starts Monday 27 July). August, September and October are calculated in sequence. The weekly hours are
    /// distinct per week so a duplicate or a gap changes a figure:
    /// <code>
    /// week (Mon-Sun)     per-day h   total   OT50   OT100   settled by (Sunday)
    /// 27 Jul - 2 Aug        8.0       40.0   3.00   0.00    August  (2 Aug)
    /// 10 - 16 Aug           8.2       41.0   3.00   1.00    August  (16 Aug)
    /// 24 - 30 Aug           7.8       39.0   2.00   0.00    August  (30 Aug)
    /// 31 Aug - 6 Sep        8.0       40.0   3.00   0.00    September (6 Sep)
    /// 14 - 20 Sep           9.0       45.0   3.00   5.00    September (20 Sep)
    /// 28 Sep - 4 Oct   3x9.0 + 2x7.4  41.8   3.00   1.80    October (4 Oct)
    /// 12 - 18 Oct           8.4       42.0   3.00   2.00    October (18 Oct)
    /// </code>
    /// Every other week is at norm (37.00: nothing). 26-30 October is worked at 9.0 h (45.00 h): that week's
    /// Sunday is 1 November, so October must NOT pay it (it would add 3.00 + 5.00). The overtime lines of each
    /// month are exactly those of the weeks whose Sunday lies in it, dated that Sunday; together they equal the
    /// sum of the weekly excesses (OT50 20.00, OT100 9.80), computed independently below from the seeded hours.
    ///
    /// Red conditions (Docker-gated, CI-verified): mutation MD-20 (August never reads 27-31 July, so the first
    /// week holds 0 h and its 3.00 vanishes, and October never reads 28-30 September); mutation MD-43 (the
    /// cutoff is the last piece day, so each month settles the week crossing its end early and the next month
    /// settles nothing for it: the September and August lists change).
    /// </summary>
    [Fact]
    public async Task AugustToOctober_NoDuplicateNoGap_OvertimeEqualsWeeklyExcessOfSettledWeeks()
    {
        const string emp = "EMP-S145-WP3";
        await SeedEmployeeAsync(emp, "HK");
        await SeedApprovedAsync(emp, "HK", Jul01, Jul31);
        await SeedApprovedAsync(emp, "HK", Aug01, Aug31);
        await SeedApprovedAsync(emp, "HK", Sep01, Sep30);

        var perDayByMonday = new Dictionary<DateOnly, decimal>
        {
            [Jul27] = 8.0m, [Aug10] = 8.2m, [Aug24] = 7.8m, [Aug31] = 8.0m,
            [Sep14] = 9.0m, [Oct12] = 8.4m, [Oct26] = 9.0m,
        };
        decimal HoursFor(DateOnly d)
        {
            if (d >= Sep28 && d <= Sep30)
                return 9.0m;
            var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
            return perDayByMonday.TryGetValue(monday, out var h) ? h : DayNorm;
        }

        await SeedWeekdaysAsync(emp, "HK", Jul27, Oct30, HoursFor);

        var august = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Aug01, Aug31);
        var september = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Sep01, Sep30);
        var october = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Oct01, Oct31);

        // Independent derivation: the weeks whose Sunday lies in August..October (13 weeks, Mon 27 Jul .. Mon 19 Oct).
        decimal expectedOt50 = 0m, expectedOt100 = 0m;
        for (var monday = Jul27; monday.AddDays(6) <= Oct25; monday = monday.AddDays(7))
        {
            var total = Enumerable.Range(0, 5).Sum(i => HoursFor(monday.AddDays(i)));
            expectedOt50 += Math.Min(Math.Max(total - 37.00m, 0m), 3.00m);
            expectedOt100 += Math.Max(total - 40.00m, 0m);
        }
        var all = OvertimeLines(august.Result)
            .Concat(OvertimeLines(september.Result))
            .Concat(OvertimeLines(october.Result))
            .ToList();

        Assert.Multiple(
            () => Assert.Equal(
                new[]
                {
                    ("OVERTIME_100", 1.00m, Aug16),
                    ("OVERTIME_50", 3.00m, Aug02), ("OVERTIME_50", 3.00m, Aug16), ("OVERTIME_50", 2.00m, Aug30),
                }.OrderBy(l => l.Item1, StringComparer.Ordinal).ThenBy(l => l.Item3).ToList(),
                OvertimeLines(august.Result)),
            () => Assert.Equal(
                new[]
                {
                    ("OVERTIME_100", 5.00m, Sep20),
                    ("OVERTIME_50", 3.00m, Sep06), ("OVERTIME_50", 3.00m, Sep20),
                }.OrderBy(l => l.Item1, StringComparer.Ordinal).ThenBy(l => l.Item3).ToList(),
                OvertimeLines(september.Result)),
            () => Assert.Equal(
                new[]
                {
                    ("OVERTIME_100", 1.80m, Oct04), ("OVERTIME_100", 2.00m, Oct18),
                    ("OVERTIME_50", 3.00m, Oct04), ("OVERTIME_50", 3.00m, Oct18),
                }.OrderBy(l => l.Item1, StringComparer.Ordinal).ThenBy(l => l.Item3).ToList(),
                OvertimeLines(october.Result)),
            () => Assert.Equal(20.00m, expectedOt50),
            () => Assert.Equal(9.80m, expectedOt100),
            () => Assert.Equal(expectedOt50, all.Where(l => l.TimeType == "OVERTIME_50").Sum(l => l.Hours)),
            () => Assert.Equal(expectedOt100, all.Where(l => l.TimeType == "OVERTIME_100").Sum(l => l.Hours)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // WP4 — leaver (R41)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WP4 (ruling R41; the leaver case of refinement Q3). The same September hours as WP1 (31 August and the rest
    /// at norm; 28-30 September 9 h each), but the employment window ends <b>Wednesday 30 September</b>
    /// (<c>users.employment_end_date</c>). The employee's last employed day is the settlement day of their last
    /// week, so <b>September pays the closing week</b>, dated 30 September: covered days 28, 29, 30 September, norm
    /// 3 x 7.40 = 22.20 (and 50% threshold 22.20), 100% threshold 3 x 8.00 = 24.00, worked 3 x 9 = 27.00, hence
    /// <c>OVERTIME_50</c> min(27.00 - 22.20, 24.00 - 22.20) = 1.80 and <c>OVERTIME_100</c> 27.00 - 24.00 = 3.00.
    /// October evaluates nothing for that week (the cutoff, 30 September, precedes October): no overtime line and
    /// the calculation succeeds. This is the case where the frozen cutoff is the ONLY thing that tells a leaver
    /// from a stayer: WP1's stayer pays nothing in September for the same week.
    ///
    /// Red conditions (Docker-gated, CI-verified): mutation MD-52 (the cutoff derivation is always null, even for a
    /// leaver): September settles nothing for the closing week (its Sunday is 4 October) and the exact
    /// September-lines assertion trips (expected 1.80 + 3.00 dated 30 September, got none). MD-43 does not reach
    /// this fact (for a leaver the last piece day equals the window end).
    /// </summary>
    [Fact]
    public async Task Leaver_LastDayWed30Sep_SeptemberPaysClosingWeek_Dated30Sep()
    {
        const string emp = "EMP-S145-WP4";
        await SeedEmployeeAsync(emp, "HK");
        await SetEmploymentWindowAsync(emp, start: null, end: Sep30);
        await SeedApprovedAsync(emp, "HK", Aug01, Aug31);
        await SeedApprovedAsync(emp, "HK", Sep01, Sep30);
        await SeedWeekdaysAsync(emp, "HK", Aug31, Sep30, d =>
            d >= Sep28 && d <= Sep30 ? 9.0m : DayNorm);

        var september = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Sep01, Sep30);
        var october = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Oct01, Oct31);

        Assert.Multiple(
            () => Assert.Equal(
                new[] { ("OVERTIME_100", 3.00m, Sep30), ("OVERTIME_50", 1.80m, Sep30) },
                OvertimeLines(september.Result)),
            () => Assert.Empty(OvertimeLines(october.Result)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // WP5 — OK boundary
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WP5 (refinement "OK-boundary week", :602-606; MD-39's observation point). April 2026, full-time HK, March
    /// APPROVED. The opening week Monday 30 March - Sunday 5 April crosses the OK24 to OK26 boundary (1 April):
    /// 30-31 March are OK24 context days. 30 March - 3 April worked 8 h each (40.00 h), the rest of April at norm.
    /// <list type="bullet">
    /// <item>April pays exactly <c>OVERTIME_50</c> 3.00 (40.00 - 37.00; the live OK24 and OK26 values are
    /// identical placeholders: norm 37, 100% threshold 40), <b>dated Sunday 5 April</b>, and nothing else.</item>
    /// <item>It exports under the <b>OK26</b> key: the OK24 <c>OVERTIME_50</c> mapping is given a distinct code
    /// (<c>SLS_0210_OK24</c>) in this fact, and the exported line carries the OK26 code <c>SLS_0210</c>.</item>
    /// <item>The request carries 30-31 March as context: the timeline call's entries include both days and its
    /// first piece starts on 30 March.</item>
    /// <item>The timeline response's config fingerprint equals <c>ConfigFingerprint.Compute</c> over BOTH the OK24
    /// configuration (30-31 March) and the OK26 configuration (from 1 April): the engine resolves config per day
    /// (refinement :235) and fingerprints what it actually resolved (:369).</item>
    /// </list>
    ///
    /// Red conditions (Docker-gated, CI-verified): mutation MD-39 (the timeline endpoint resolves one config for the
    /// whole timeline, from the first piece): the fingerprint is over OK24 only and the fingerprint assertion
    /// trips. Mutation MD-20 (30-31 March never read): the week holds 24.00 h and the overtime assertion trips.
    /// </summary>
    [Fact]
    public async Task OkBoundary_April2026_OvertimeDated5April_OK26Key_ContextCarries30And31March()
    {
        const string emp = "EMP-S145-WP5";
        await SeedEmployeeAsync(emp, "HK");
        await SeedApprovedAsync(emp, "HK", Mar01, Mar31);
        await SeedWeekdaysAsync(emp, "HK", Mar30, Apr30, d => d <= Apr03 ? 8.0m : DayNorm);
        await ExecuteAsync(
            "UPDATE wage_type_mappings SET wage_type = 'SLS_0210_OK24' " +
            "WHERE time_type = 'OVERTIME_50' AND ok_version = 'OK24' AND agreement_code = 'HK' AND position = ''");

        var april = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Apr01, Apr30);

        var call = april.Recorder.Timeline.Single();
        var expectedFingerprint = ConfigFingerprint.Compute(new (string AgreementCode, string OkVersion, string? Position, AgreementRuleConfig Config)[]
        {
            ("HK", "OK24", null, AgreementConfigProvider.GetConfig("HK", "OK24")),
            ("HK", "OK26", null, AgreementConfigProvider.GetConfig("HK", "OK26")),
        });
        var entryDates = call.EntryHours().Select(e => e.Date).ToList();

        Assert.Multiple(
            () => Assert.Equal(new[] { ("OVERTIME_50", 3.00m, Apr05) }, OvertimeLines(april.Result)),
            () => Assert.Equal(new[] { ("SLS_0210", 3.00m) }, ExportedOvertime(april.Result)),
            () => Assert.Contains(Mar30, entryDates),
            () => Assert.Contains(Mar31, entryDates),
            () => Assert.Equal(Mar30, call.Pieces().Min(p => p.From)),
            () => Assert.Equal(new[] { expectedFingerprint }, call.Fingerprints()));
    }

    // ═════════════════════════════════════════════════════════════════════
    // WP6 / WP7 — opening-week fraction change (Q2)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WP6 (refinement "Opening-week fraction change", :607). Full-time HK becomes 30 h (fraction 0.811)
    /// effective <b>Tuesday 1 September</b>: the change falls ON the period start, so September is one segment and
    /// the planner does not refuse it. August APPROVED. The week 31 August - 6 September worked 8 h per weekday
    /// (40.00 h): Monday 31 August is a context day at fraction 1.0 (norm 7.40, 100% threshold 8.00), 1-4 September
    /// at 0.811 (norm 6.00, threshold 6.49 per day: 37 x 0.811 / 5 = 6.0014, 40 x 0.811 / 5 = 6.488). Norm 7.40 +
    /// 4 x 6.00 = 31.40; 100% threshold 8.00 + 4 x 6.49 = 33.96. <c>OVERTIME_50</c> min(40.00 - 31.40, 33.96 -
    /// 31.40) = <b>2.56</b>, <c>OVERTIME_100</c> 40.00 - 33.96 = <b>6.04</b>, both dated Sunday 6 September. Every
    /// other September day is worked at its day norm 6.00 (C-W5: the closing week included).
    ///
    /// Red conditions (Docker-gated, CI-verified): mutation MD-41 (the timeline endpoint takes the first piece's
    /// fraction for every piece): one fraction for the week changes the norm and the lines; mutation MD-20
    /// (31 August never read): the week holds 32.00 h against 24.00 and the lines change.
    /// </summary>
    [Fact]
    public async Task OpeningWeekFractionChange_Tue1Sep_OneSegmentMonth_Pays256And604_Dated6Sep()
    {
        const string emp = "EMP-S145-WP6";
        await SeedEmployeeAsync(emp, "HK");
        await SupersedeProfileAsync(emp, fraction: 0.811m, position: null, effectiveFrom: Sep01);
        await SeedApprovedAsync(emp, "HK", Aug01, Aug31);
        await SeedWeekdaysAsync(emp, "HK", Aug31, Sep30, d =>
            d <= Sep04 ? 8.0m : 6.00m);

        var september = await CalculateAsync(emp, ProfileOf(emp, "HK", 0.811m), Sep01, Sep30);

        Assert.Equal(
            new[] { ("OVERTIME_100", 6.04m, Sep06), ("OVERTIME_50", 2.56m, Sep06) },
            OvertimeLines(september.Result));
    }

    /// <summary>
    /// WP7 (refinement :607, "AC: 8.60 merarbejde"). The AC twin of WP6: the same fraction change and hours. AC
    /// pays the whole excess over the norm as <c>MERARBEJDE</c>: 40.00 - 31.40 = <b>8.60</b>, dated 6 September, and
    /// nothing else.
    ///
    /// Red conditions (Docker-gated, CI-verified): mutations MD-41 and MD-20, as WP6.
    /// </summary>
    [Fact]
    public async Task OpeningWeekFractionChange_Tue1Sep_AC_Merarbejde860()
    {
        const string emp = "EMP-S145-WP7";
        await SeedEmployeeAsync(emp, "AC");
        await SupersedeProfileAsync(emp, fraction: 0.811m, position: null, effectiveFrom: Sep01);
        await SeedApprovedAsync(emp, "AC", Aug01, Aug31);
        await SeedWeekdaysAsync(emp, "AC", Aug31, Sep30, d =>
            d <= Sep04 ? 8.0m : 6.00m);

        var september = await CalculateAsync(emp, ProfileOf(emp, "AC", 0.811m), Sep01, Sep30);

        Assert.Equal(
            new[] { ("MERARBEJDE", 8.60m, Sep06) },
            OvertimeLines(september.Result));
    }

    // ═════════════════════════════════════════════════════════════════════
    // WP8 / WP9 / WP10 — the window contract (R6)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WP8 (R6 table; refinement "Window contract", :608-612). September 2026, full-time HK, August APPROVED, every
    /// weekday INCLUDING Monday 31 August at exactly 7.40 h (C-W3). The norm figures cover the month's OWN days
    /// only: <c>NormHoursTotal</c> and <c>ActualHoursTotal</c> 22 x 7.40 = <b>162.80</b>, one <c>NORMAL_HOURS</c>
    /// line per September entry (the 22 September weekdays) and <b>none for 31 August</b>.
    ///
    /// Red conditions (Docker-gated, CI-verified): mutation MD-42 (the timeline endpoint hands the core
    /// <c>PeriodStart = Pieces[0].From</c> instead of the request's period start): 31 August becomes an in-period
    /// day: a <c>NORMAL_HOURS</c> line dated 31 August appears (23 lines) and both totals become 170.20, so the
    /// exact-dates, the "none for 31 August" and the 162.80 assertions trip.
    /// </summary>
    [Fact]
    public async Task WindowContract_September_NormTotal16280_NoNormalHoursFor31Aug()
    {
        const string emp = "EMP-S145-WP8";
        await SeedEmployeeAsync(emp, "HK");
        await SeedApprovedAsync(emp, "HK", Aug01, Aug31);
        await SeedWeekdaysAsync(emp, "HK", Aug31, Sep30, _ => DayNorm);

        var september = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Sep01, Sep30);

        var normal = LineItemsOf(september.Result, "NORMAL_HOURS");
        var norm = september.Result.RuleResults.Single(r => r.RuleId == "NORM_CHECK_37H");
        Assert.Multiple(
            () => Assert.Equal(
                Weekdays(Sep01, Sep30).ToList(),
                normal.Select(l => l.Date).OrderBy(d => d).ToList()),
            () => Assert.DoesNotContain(Aug31, normal.Select(l => l.Date)),
            () => Assert.Equal(162.80m, normal.Sum(l => l.Hours)),
            () => Assert.Equal(162.80m, norm.NormHoursTotal),
            () => Assert.Equal(162.80m, norm.ActualHoursTotal));
    }

    /// <summary>
    /// WP9 (R6 table; refinement :610, :612; N7). The fixture is <b>AC</b>, the only agreement with a
    /// DEPARTMENT_HEAD override (<c>PositionOverrideConfigs.cs:27-36</c>: <c>NormPeriodWeeks</c> 4, flex cap 200,
    /// weekly norm unchanged). The employee's position becomes DEPARTMENT_HEAD effective Tuesday 1 September (the
    /// period start, so one segment); the result reports <c>NormPeriodWeeks</c> from the configuration in force
    /// on the period's last covered day (30 September, DEPARTMENT_HEAD; the week's Sunday 6 September gives the
    /// same value): <b>4</b>. The no-position-change twin (a second AC
    /// employee with the same hours, calculated in the same fact, C-W6) reports the base AC value, 1. Every day is
    /// at exactly the day norm (C-W3), so the figures are comparable, and the export lines of the two employees
    /// are identical ("no export line change"; wage type, hours and dates, in order).
    ///
    /// Red conditions (Docker-gated, CI-verified): mutation MD-39 (one config for the whole timeline, from the
    /// first piece, which carries no position): the position-change employee reports <c>NormPeriodWeeks</c> 1 and
    /// the "4" assertion trips. MD-42 does not reach this fact (C-W1, C-W6).
    /// </summary>
    [Fact]
    public async Task WindowContract_PositionChangeTue1Sep_SundayModeReported_NoExportLineChange()
    {
        const string changed = "EMP-S145-WP9-CHG";
        const string twin = "EMP-S145-WP9-TWIN";
        await SeedEmployeeAsync(changed, "AC");
        await SupersedeProfileAsync(changed, fraction: 1.0m, position: "DEPARTMENT_HEAD", effectiveFrom: Sep01);
        await SeedEmployeeAsync(twin, "AC");
        foreach (var emp in new[] { changed, twin })
        {
            await SeedApprovedAsync(emp, "AC", Aug01, Aug31);
            await SeedWeekdaysAsync(emp, "AC", Aug31, Sep30, _ => DayNorm);
        }

        var changedRun = await CalculateAsync(changed, ProfileOf(changed, "AC", 1.0m, "DEPARTMENT_HEAD"), Sep01, Sep30);
        var twinRun = await CalculateAsync(twin, ProfileOf(twin, "AC", 1.0m), Sep01, Sep30);

        static List<(string?, string, decimal, DateOnly, DateOnly)> Exported(PeriodCalculationResult r) =>
            r.ExportLines
                .Select(l => (l.SourceTimeType, l.WageType, l.Hours, l.PeriodStart, l.PeriodEnd))
                .OrderBy(l => l.Item1, StringComparer.Ordinal).ThenBy(l => l.Item4).ThenBy(l => l.Item2, StringComparer.Ordinal)
                .ToList();

        Assert.Multiple(
            () => Assert.Equal(4, changedRun.Result.RuleResults.Single(r => r.RuleId == "NORM_CHECK_37H").NormPeriodWeeks),
            () => Assert.Equal(1, twinRun.Result.RuleResults.Single(r => r.RuleId == "NORM_CHECK_37H").NormPeriodWeeks),
            () => Assert.NotEmpty(twinRun.Result.ExportLines),
            () => Assert.Equal(Exported(twinRun.Result), Exported(changedRun.Result)));
    }

    /// <summary>
    /// WP10 (R6 table; refinement :611). An AC_RESEARCH September keeps its days / 365 total: annual-activity norm
    /// 1924 h x 30 September days / 365 = 57720 / 365 = <b>158.136986</b> (to six decimals; 31 August is a context
    /// day and is NOT counted: 31 days would give 163.40). <c>ActualHoursTotal</c> 22 x 7.40 = 162.80, and no
    /// overtime or merarbejde line is paid (the annual mode is not judged by weekday norms). August APPROVED; every
    /// weekday at 7.40 h.
    ///
    /// Red conditions (Docker-gated, CI-verified): mutation MD-42 (the core starts at the first piece, 31 August):
    /// 31 days / 365 gives 163.40 and the total assertion trips. MU-10 covers the arithmetic at Unit level.
    /// </summary>
    [Fact]
    public async Task WindowContract_AcResearch_AnnualTotalKept()
    {
        const string emp = "EMP-S145-WP10";
        await SeedEmployeeAsync(emp, "AC_RESEARCH");
        await SeedApprovedAsync(emp, "AC_RESEARCH", Aug01, Aug31);
        await SeedWeekdaysAsync(emp, "AC_RESEARCH", Aug31, Sep30, _ => DayNorm);

        var september = await CalculateAsync(emp, ProfileOf(emp, "AC_RESEARCH", 1.0m), Sep01, Sep30);

        var norm = september.Result.RuleResults.Single(r => r.RuleId == "NORM_CHECK_37H");
        Assert.Multiple(
            () => Assert.Equal(158.136986m, Math.Round(norm.NormHoursTotal!.Value, 6)),
            () => Assert.Equal(162.80m, norm.ActualHoursTotal),
            () => Assert.Empty(OvertimeLines(september.Result)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // WP11 — flex across two months
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WP11 (refinement "Flex (Stage A part, R9)", :617). Full-time HK, August and September APPROVED. September
    /// is worked at norm + 10 h: 9.0 h per weekday on 14-18 September (+8.00) and 7.8 h per weekday on 21-25
    /// September (+2.00); every other day, every context day included (31 August; 28-30 September, C-W2), at
    /// exactly the day norm 7.40. Opening balance 95.
    /// <list type="bullet">
    /// <item>September: delta = worked 172.80 - norm 162.80 = +10.00; 95 + 10 = 105 exceeds the HK cap 100, so
    /// <c>NewBalance</c> is <b>100.00</b> and exactly one <c>FLEX_PAYOUT</c> of <b>5.00</b> is paid, dated 30
    /// September. (Today September would pay out 130.8: the month judged against one week.)</item>
    /// <item>October, fed the 100.00 and worked at norm: no <c>FLEX_PAYOUT</c> and the balance stays 100.00.</item>
    /// </list>
    /// The balance is read from the engine's own timeline response (<c>flex.newBalance</c>), which is what the next
    /// month is fed.
    ///
    /// Red conditions (Docker-gated, CI-verified): mutation MD-29 (the per-plan lane's opening flex is 0): the
    /// engine receives 0 instead of 95, so 0 + 10 = 10 stays under the cap: no payout, <c>NewBalance</c> 10.00, and
    /// the payout and balance assertions trip.
    /// </summary>
    [Fact]
    public async Task Flex_TwoMonths_SeptemberPaysOut500Dated30Sep_OctoberFed100_NoPayout()
    {
        const string emp = "EMP-S145-WP11";
        await SeedEmployeeAsync(emp, "HK");
        await SeedApprovedAsync(emp, "HK", Aug01, Aug31);
        await SeedApprovedAsync(emp, "HK", Sep01, Sep30);
        await SeedWeekdaysAsync(emp, "HK", Aug31, Oct30, d =>
            d >= Sep14 && d <= Sep18 ? 9.0m
            : d >= Sep21 && d <= Sep25 ? 7.8m
            : DayNorm);

        var september = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Sep01, Sep30, previousFlex: 95m);
        var october = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Oct01, Oct31, previousFlex: 100m);

        var septemberPayouts = LineItemsOf(september.Result, "FLEX_PAYOUT");
        Assert.Multiple(
            () => Assert.Equal(
                new[] { (5.00m, Sep30) },
                septemberPayouts.Select(l => (l.Hours, l.Date)).ToArray()),
            () => Assert.Equal(100.00m, september.Recorder.Timeline.Single().FlexNewBalance()),
            () => Assert.Empty(LineItemsOf(october.Result, "FLEX_PAYOUT")),
            () => Assert.Equal(100.00m, october.Recorder.Timeline.Single().FlexNewBalance()));
    }

    // ═════════════════════════════════════════════════════════════════════
    // WP12 — the dated key (QUAL-150)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WP12 (refinement "Dated key (QUAL-150)", :630-634). June 2026 starts on a Monday, so no context hours are
    /// involved and no previous month needs approving. The request body carries a STALE position
    /// (<c>STALE_POS</c>); the employee's dated record (<c>employee_profiles</c>) says <c>DATED_POS</c>. A
    /// position-specific wage-type row exists for the dated position only (<c>NORMAL_HOURS</c> to
    /// <c>SLS_0110_DATEDPOS</c>); the generic row <c>SLS_0110</c> is what a lookup under the stale position falls
    /// back to. Expected: the five NORMAL_HOURS export lines (1-5 June, 7.40 h each) map under the dated record's
    /// position, so every one carries <c>SLS_0110_DATEDPOS</c>.
    ///
    /// Red conditions (Docker-gated, CI-verified): mutation MD-30 (the <c>WtmNaturalKey</c> hydrator returns the
    /// request profile's key for every segment): the lookup runs under <c>STALE_POS</c>, finds no row, falls back
    /// to <c>SLS_0110</c>, and the wage-type assertion trips. (On master the key is hydrated once from the request
    /// profile, which is exactly this behaviour.)
    /// </summary>
    [Fact]
    public async Task DatedKey_StaleBodyPosition_MapsWithDatedRecordPosition()
    {
        const string emp = "EMP-S145-WP12";
        await SeedEmployeeAsync(emp, "HK", position: "DATED_POS");
        await ExecuteAsync(
            """
            INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description, effective_from)
            VALUES ('NORMAL_HOURS', 'SLS_0110_DATEDPOS', 'OK26', 'HK', 'DATED_POS', 'S145 WP12 dated-position row', DATE '2020-01-01')
            ON CONFLICT DO NOTHING
            """);
        await SeedWeekdaysAsync(emp, "HK", Jun01, Jun05, _ => DayNorm);

        var june = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m, "STALE_POS"), Jun01, Jun30);

        var normalExport = june.Result.ExportLines.Where(l => l.SourceTimeType == "NORMAL_HOURS").ToList();
        Assert.Multiple(
            () => Assert.Equal(5, normalExport.Count),
            () => Assert.All(normalExport, l => Assert.Equal("SLS_0110_DATEDPOS", l.WageType)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Calculation harness
    // ═════════════════════════════════════════════════════════════════════

    private sealed record MonthRun(PeriodCalculationResult Result, RecordingHandler Recorder);

    /// <summary>One PCS built per calculation over a fresh recording wrapper of the in-process real engine, so a
    /// fact reads exactly the timeline calls its own calculation made. The signature carries NO hours (R14): PCS
    /// reads them itself through the registered reader.</summary>
    private async Task<MonthRun> CalculateAsync(
        string employeeId,
        EmploymentProfile profile,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousFlex = 0m)
    {
        var recorder = new RecordingHandler(_engine.Server.CreateHandler());
        var pcs = BuildPcs(recorder);
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var outcome = await pcs.CalculateWithOutcomeAsync(
            profile, periodStart, periodEnd, previousFlexBalance: previousFlex,
            authorizationHeader: "Bearer " + MintGlobalAdminToken());
#pragma warning restore CS0618
        Assert.True(outcome.Result.Success, $"{employeeId} {periodStart:yyyy-MM}: {outcome.Result.ErrorMessage}");
        return new MonthRun(outcome.Result, recorder);
    }

    /// <summary>The production DI shape, hand-built: every optional dependency the Payroll host registers, the REAL
    /// hours reader and the REAL approval adapter (R26, R25).</summary>
    private PeriodCalculationService BuildPcs(HttpMessageHandler engineHandler)
    {
        var wtmRepo = new WageTypeMappingRepository(_db);
        var mappingService = new PayrollMappingService(_db, NullLogger<PayrollMappingService>.Instance, wtmRepo);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceUrls:RuleEngine"] = "http://rule-engine.test" })
            .Build();
        var agreementCodes = new UserAgreementCodeRepository(_db);

        return new PeriodCalculationService(
            new SingleClientFactory(engineHandler),
            mappingService,
            new PostgresSegmentManifestStore(_db, _store, _store),
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new LiveClassifications(),
            localAgreementProfileRepo: null,
            profileResolver: new EmploymentProfileResolver(_db, agreementCodes),
            employmentWindowResolver: new EmploymentWindowResolver(_db),
            employeeProfileRepo: new EmployeeProfileRepository(_db),
            userAgreementCodeRepo: agreementCodes,
            hoursReader: new ProjectionHoursReader(_db,
                new TimeEntryProjectionRepository(_db), new AbsenceProjectionRepository(_db)),
            priorPeriodApprovalReader: new ApprovalPeriodPriorPeriodReader(new ApprovalPeriodRepository(_db)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Reading results
    // ═════════════════════════════════════════════════════════════════════

    private static List<CalculationLineItem> LineItemsOf(PeriodCalculationResult result, string timeType) =>
        result.RuleResults.SelectMany(r => r.LineItems).Where(li => li.TimeType == timeType).ToList();

    /// <summary>Every overtime / merarbejde line, ordered by time type (ordinal) then date.</summary>
    private static List<(string TimeType, decimal Hours, DateOnly Date)> OvertimeLines(PeriodCalculationResult result) =>
        result.RuleResults
            .SelectMany(r => r.LineItems)
            .Where(li => li.TimeType is "OVERTIME_50" or "OVERTIME_100" or "MERARBEJDE")
            .OrderBy(li => li.TimeType, StringComparer.Ordinal).ThenBy(li => li.Date)
            .Select(li => (li.TimeType, li.Hours, li.Date))
            .ToList();

    /// <summary>The export lines that carry overtime: (wage type, hours), ordered by wage type.</summary>
    private static List<(string WageType, decimal Hours)> ExportedOvertime(PeriodCalculationResult result) =>
        result.ExportLines
            .Where(l => l.SourceTimeType is "OVERTIME_50" or "OVERTIME_100")
            .OrderBy(l => l.WageType, StringComparer.Ordinal)
            .Select(l => (l.WageType, l.Hours))
            .ToList();

    // ═════════════════════════════════════════════════════════════════════
    // Dates
    // ═════════════════════════════════════════════════════════════════════

    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar30 = new(2026, 3, 30);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);
    private static readonly DateOnly Apr01 = new(2026, 4, 1);
    private static readonly DateOnly Apr03 = new(2026, 4, 3);
    private static readonly DateOnly Apr05 = new(2026, 4, 5);
    private static readonly DateOnly Apr30 = new(2026, 4, 30);
    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun05 = new(2026, 6, 5);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);
    private static readonly DateOnly Jul01 = new(2026, 7, 1);
    private static readonly DateOnly Jul27 = new(2026, 7, 27);
    private static readonly DateOnly Jul31 = new(2026, 7, 31);
    private static readonly DateOnly Aug01 = new(2026, 8, 1);
    private static readonly DateOnly Aug02 = new(2026, 8, 2);
    private static readonly DateOnly Aug10 = new(2026, 8, 10);
    private static readonly DateOnly Aug16 = new(2026, 8, 16);
    private static readonly DateOnly Aug24 = new(2026, 8, 24);
    private static readonly DateOnly Aug30 = new(2026, 8, 30);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);
    private static readonly DateOnly Sep01 = new(2026, 9, 1);
    private static readonly DateOnly Sep04 = new(2026, 9, 4);
    private static readonly DateOnly Sep06 = new(2026, 9, 6);
    private static readonly DateOnly Sep14 = new(2026, 9, 14);
    private static readonly DateOnly Sep18 = new(2026, 9, 18);
    private static readonly DateOnly Sep20 = new(2026, 9, 20);
    private static readonly DateOnly Sep21 = new(2026, 9, 21);
    private static readonly DateOnly Sep25 = new(2026, 9, 25);
    private static readonly DateOnly Sep28 = new(2026, 9, 28);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);
    private static readonly DateOnly Oct01 = new(2026, 10, 1);
    private static readonly DateOnly Oct02 = new(2026, 10, 2);
    private static readonly DateOnly Oct04 = new(2026, 10, 4);
    private static readonly DateOnly Oct12 = new(2026, 10, 12);
    private static readonly DateOnly Oct18 = new(2026, 10, 18);
    private static readonly DateOnly Oct25 = new(2026, 10, 25);
    private static readonly DateOnly Oct26 = new(2026, 10, 26);
    private static readonly DateOnly Oct30 = new(2026, 10, 30);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    private static IEnumerable<DateOnly> Weekdays(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            yield return d;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Seeding
    // ═════════════════════════════════════════════════════════════════════

    private static EmploymentProfile ProfileOf(string employeeId, string agreementCode, decimal fraction, string? position = null) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = agreementCode,
        OkVersion = "OK26",
        EmploymentCategory = "Standard",
        PartTimeFraction = fraction,
        Position = position,
        OrgId = OrgId,
    };

    /// <summary>users + employee_profiles + user_agreement_codes with the history-covering '0001-01-01' rows (the
    /// dated record equals the request profile, C-W4) + the organization.</summary>
    private async Task SeedEmployeeAsync(string employeeId, string agreementCode, string? position = null)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await RegressionSeed.SeedEmployeeAsync(
            conn, employeeId, OrgId, agreementCode, "OK26", partTimeFraction: 1.0m, position: position);
    }

    private async Task SetEmploymentWindowAsync(string employeeId, DateOnly? start, DateOnly? end)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_start_date = @s, employment_end_date = @e WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        cmd.Parameters.AddWithValue("s", (object?)start ?? DBNull.Value);
        cmd.Parameters.AddWithValue("e", (object?)end ?? DBNull.Value);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>Closes the live profile row at <paramref name="effectiveFrom"/> (end-exclusive) and inserts the
    /// successor with the new fraction and position.</summary>
    private async Task SupersedeProfileAsync(string employeeId, decimal fraction, string? position, DateOnly effectiveFrom)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var close = new NpgsqlCommand(
            "UPDATE employee_profiles SET effective_to = @from WHERE employee_id = @id AND effective_to IS NULL", conn, tx))
        {
            close.Parameters.AddWithValue("id", employeeId);
            close.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await close.ExecuteNonQueryAsync());
        }
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version, employment_category)
            VALUES (gen_random_uuid(), @id, @fraction, @position, @from, NULL, 1,
                    (SELECT u.employment_category FROM users u WHERE u.user_id = @id))
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", employeeId);
            insert.Parameters.AddWithValue("fraction", fraction);
            insert.Parameters.AddWithValue("position", (object?)position ?? DBNull.Value);
            insert.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }
        await tx.CommitAsync();
    }

    /// <summary>An APPROVED monthly approval period (R26: the previous month, seeded at repository level).</summary>
    private async Task SeedApprovedAsync(string employeeId, string agreementCode, DateOnly periodStart, DateOnly periodEnd)
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
                NOW(), @emp, 'ROOT-S145', NOW(), @agreement, 'OK26')
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", periodStart);
        cmd.Parameters.AddWithValue("end", periodEnd);
        cmd.Parameters.AddWithValue("agreement", agreementCode);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>One RAW time-entry projection row per WEEKDAY in [from, to]: one registration for the day, no task,
    /// the S145 columns left to their defaults (a current TIME_POST row).</summary>
    private async Task SeedWeekdaysAsync(
        string employeeId, string agreementCode, DateOnly from, DateOnly to, Func<DateOnly, decimal> hoursFor)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        foreach (var date in Weekdays(from, to))
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO time_entries_projection
                    (event_id, employee_id, date, hours, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, outbox_id)
                VALUES
                    (gen_random_uuid(), @emp, @date, @hours, @agreement, 'OK26', FALSE, NOW(), @outbox)
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", date);
            cmd.Parameters.AddWithValue("hours", hoursFor(date));
            cmd.Parameters.AddWithValue("agreement", agreementCode);
            cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): both callers pass a compile-time test constant
        // (WP5's mapping update, WP12's mapping row) — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // The in-process real engine, with a recording wrapper
    // ═════════════════════════════════════════════════════════════════════

    private static string MintGlobalAdminToken()
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = PayrollHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: "ROOT-S145",
            name: "ROOT-S145",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    /// <summary>The REAL Rule Engine hosted in-process (copied from <c>PayrollHostFactory</c>'s private twin: the
    /// house idiom for a private fixture). JWT settings go into host configuration so the bearer token PCS forwards
    /// validates against the dev key.</summary>
    private sealed class RuleEngineHost : WebApplicationFactory<RuleRegistry>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:SigningKey"] = PayrollHostFactory.DevSigningKey,
                    ["Jwt:Issuer"] = "statstid",
                    ["Jwt:Audience"] = "statstid",
                }));
            return base.CreateHost(builder);
        }
    }

    /// <summary>One <c>/api/rules/evaluate-timeline</c> call: the request and the response exactly as sent and
    /// received (the real engine's own answer).</summary>
    private sealed record TimelineCall(string RequestJson, string ResponseJson)
    {
        public sealed record Piece(DateOnly From, DateOnly To, decimal PartTimeFraction, string AgreementCode, string? Position);

        public IReadOnlyList<(DateOnly Date, decimal Hours)> EntryHours()
        {
            using var doc = JsonDocument.Parse(RequestJson);
            return doc.RootElement.GetProperty("entries").EnumerateArray()
                .Select(e => (DateOnly.Parse(e.GetProperty("date").GetString()!, CultureInfo.InvariantCulture), e.GetProperty("hours").GetDecimal()))
                .ToList();
        }

        public IReadOnlyList<Piece> Pieces()
        {
            using var doc = JsonDocument.Parse(RequestJson);
            return doc.RootElement.GetProperty("pieces").EnumerateArray()
                .Select(p => new Piece(
                    DateOnly.Parse(p.GetProperty("from").GetString()!, CultureInfo.InvariantCulture),
                    DateOnly.Parse(p.GetProperty("to").GetString()!, CultureInfo.InvariantCulture),
                    p.GetProperty("partTimeFraction").GetDecimal(),
                    p.GetProperty("agreementCode").GetString()!,
                    p.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.String ? pos.GetString() : null))
                .ToList();
        }

        public IReadOnlyList<string> Fingerprints()
        {
            using var doc = JsonDocument.Parse(ResponseJson);
            return doc.RootElement.GetProperty("configFingerprints").EnumerateArray().Select(f => f.GetString()!).ToList();
        }

        public decimal FlexNewBalance()
        {
            using var doc = JsonDocument.Parse(ResponseJson);
            return doc.RootElement.GetProperty("flex").GetProperty("newBalance").GetDecimal();
        }
    }

    /// <summary>Forwards every call to the real engine and keeps the timeline calls. The per-plan lane makes ONE
    /// timeline call per calculation; the facts read it as the observation point of what PCS sent and what the
    /// engine resolved.</summary>
    private sealed class RecordingHandler : DelegatingHandler
    {
        private readonly List<TimelineCall> _timeline = new();

        public RecordingHandler(HttpMessageHandler inner) : base(inner) { }

        public IReadOnlyList<TimelineCall> Timeline
        {
            get { lock (_timeline) return _timeline.ToList(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isTimeline = request.RequestUri?.AbsolutePath.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal) == true;
            var requestJson = isTimeline && request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : null;

            var response = await base.SendAsync(request, cancellationToken);

            if (isTimeline && response.StatusCode == HttpStatusCode.OK)
            {
                await response.Content.LoadIntoBufferAsync();
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                lock (_timeline) _timeline.Add(new TimelineCall(requestJson ?? "{}", responseJson));
            }
            return response;
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class LiveClassifications : IRuleClassificationProvider
    {
        public IReadOnlyList<RuleClassification> GetClassifications() => LiveSet;
    }
}
