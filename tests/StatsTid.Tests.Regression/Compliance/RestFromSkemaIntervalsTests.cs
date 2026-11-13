using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.RuleEngine.Api.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Compliance;

/// <summary>
/// S148 TASK-14813-T (S0-3, P-12, R-22; AC-S0-3a, -3c, -3f) - Docker-gated route facts: the server's
/// 11-hour rest check reads the clock intervals typed in Skema.
///
/// <para>
/// <b>Plain language.</b> An employee saves clock intervals in Skema through the real save, the
/// project hours stand in the database, and the real compliance route runs the real rule engine over
/// them. These facts prove that a real short rest between two Skema days is now reported (before, the
/// days were only listed as "not checked"), that a day is "not checked" only when its project hours
/// are not all on the clock, and that a short rest across a month boundary is reported once, in the
/// month in which it ends.
/// </para>
///
/// <para>
/// <b>Harness.</b> <see cref="RestComplianceRealEngineHarness"/> (the real engine host, S148 BC-6).
/// Every fact is kind <b>F (CI)</b> and names no member the sprint creates (HTTP, SQL and the real
/// engine only). The severity expectation rests on the composed engine's config: the engine reads
/// <c>AgreementConfigProvider.GetConfig("HK", "OK24", position)</c>, and each fact asserts first that
/// this returns <c>RestPeriodDerogationAllowed == true</c> for the seeded (null) position.
/// </para>
///
/// <para>
/// <b>CI-pending.</b> Docker is unavailable locally; every fact is reported CI-pending until CI-W3.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RestFromSkemaIntervalsTests : IAsyncLifetime
{
    private RestComplianceRealEngineHarness _h = null!;

    public async Task InitializeAsync() => _h = await RestComplianceRealEngineHarness.StartAsync();

    public async Task DisposeAsync()
    {
        if (_h is not null)
            await _h.DisposeAsync();
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, AC-S0-3a. A fresh HK / OK24 employee. Through the real Skema save: Tue 10
    /// March 2026 22:00-23:59 and Wed 11 March 2026 06:00-14:00; project hours 1.98 on Tue 10 and 8.0 on
    /// Wed 11 (so both days are fully on the clock and analysed). March's GET: 200, exactly one
    /// DAILY_REST finding across violations and warnings, in <c>warnings</c> with severity WARNING (HK
    /// derogates), date 2026-03-10, actual 6.0, threshold 11.0; <c>restNotCheckedDays</c> holds
    /// neither date.
    ///
    /// Red conditions (today, before TASK-14813): the compliance read sends no intervals, so the engine
    /// forms no work period and measures no rest: <c>Assert.Single(findings)</c> fails (none), and both
    /// days are listed in <c>restNotCheckedDays</c>, so the two <c>Assert.DoesNotContain</c> fail.
    /// </summary>
    [Fact]
    public async Task Interval2200To2359_Then0600To1400_DailyRestFinding()
    {
        AssertHkOk24Derogates();
        var employeeId = await _h.SeedEmployeeAsync();
        var tue = new DateOnly(2026, 3, 10);
        var wed = new DateOnly(2026, 3, 11);
        await SaveWorkTimeAsync(employeeId, 2026, 3,
            (tue, ("22:00", "23:59")), (wed, ("06:00", "14:00")));
        await _h.InsertEntryAsync(employeeId, tue, 1.98m);
        await _h.InsertEntryAsync(employeeId, wed, 8.0m);

        var body = await GetPeriodAsync(employeeId, 2026, 3);

        var rest = Assert.Single(DailyRestFindings(body));
        Assert.Equal("warnings", rest.List);
        Assert.Equal((int)ComplianceSeverity.WARNING, rest.Finding.GetProperty("severity").GetInt32());
        Assert.Equal("2026-03-10", rest.Finding.GetProperty("date").GetString());
        Assert.Equal(6.0m, rest.Finding.GetProperty("actualValue").GetDecimal());
        Assert.Equal(11.0m, rest.Finding.GetProperty("thresholdValue").GetDecimal());

        var notChecked = NotCheckedDays(body);
        Assert.DoesNotContain("2026-03-10", notChecked);
        Assert.DoesNotContain("2026-03-11", notChecked);
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, AC-S0-3c. A fresh HK / OK24 employee. Mon 16 March 2026: interval 08:00-12:00,
    /// project hours 7.4. Tue 17 March 2026: interval 08:00-15:24 (7.4 h), project hours 7.4. March's
    /// GET: 200; <c>restNotCheckedDays</c> equals exactly <c>["2026-03-16"]</c>; no DAILY_REST finding
    /// (20 hours between the days).
    ///
    /// Red conditions (today): the read sends no intervals, so Tue 17 is listed too: the exact list
    /// <c>["2026-03-16"]</c> fails (actual: both days), the "analysed" half.
    /// </summary>
    [Fact]
    public async Task ProjectHoursAboveIntervalHours_Listed_EqualHours_Analysed()
    {
        var employeeId = await _h.SeedEmployeeAsync();
        var mon = new DateOnly(2026, 3, 16);
        var tue = new DateOnly(2026, 3, 17);
        await SaveWorkTimeAsync(employeeId, 2026, 3,
            (mon, ("08:00", "12:00")), (tue, ("08:00", "15:24")));
        await _h.InsertEntryAsync(employeeId, mon, 7.4m);
        await _h.InsertEntryAsync(employeeId, tue, 7.4m);

        var body = await GetPeriodAsync(employeeId, 2026, 3);

        Assert.Equal(new[] { "2026-03-16" }, NotCheckedDays(body));
        Assert.Empty(DailyRestFindings(body));
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, AC-S0-3f (14813 BC-4: a rest is judged in the month in which it ends). A
    /// fresh HK / OK24 employee. Through the real Skema save, one save per month: Tue 30 September 2025
    /// 14:00-22:00 and Wed 1 October 2025 07:00-15:00, project hours 8.0 on each day. <b>October's GET</b>
    /// (year 2025, month 10): 200, exactly one DAILY_REST finding across violations and warnings, date
    /// 2025-09-30, actual 9.0, threshold 11.0, severity WARNING (HK OK24 derogates, asserted first).
    /// <b>September's GET</b>: 200 and no DAILY_REST finding (the rest ends in October).
    ///
    /// Red conditions (today, before TASK-14813): the read sends no intervals, so October has no
    /// finding: <c>Assert.Single(findings)</c> fails. (A reading that judged the rest in the month it
    /// STARTS would put it in September: the September <c>Assert.Empty</c> fails.)
    ///
    /// The 2025 fixture is the one written; the brief's 2026 fallback (31 March / 1 April 2026, April's
    /// GET, severity unasserted) is for a harness that refuses 2025. Reading the save's code, it
    /// bounds dates to the posted month only, so 2025 is expected to be accepted (unproven: CI-W3
    /// settles it).
    /// </summary>
    [Fact]
    public async Task ShortRestAcrossMonthEdge_ReportedInTheMonthItEnds()
    {
        AssertHkOk24Derogates();
        var employeeId = await _h.SeedEmployeeAsync();
        var sep30 = new DateOnly(2025, 9, 30);
        var oct1 = new DateOnly(2025, 10, 1);
        await SaveWorkTimeAsync(employeeId, 2025, 9, (sep30, ("14:00", "22:00")));
        await SaveWorkTimeAsync(employeeId, 2025, 10, (oct1, ("07:00", "15:00")));
        await _h.InsertEntryAsync(employeeId, sep30, 8.0m);
        await _h.InsertEntryAsync(employeeId, oct1, 8.0m);

        var october = await GetPeriodAsync(employeeId, 2025, 10);
        var rest = Assert.Single(DailyRestFindings(october));
        Assert.Equal((int)ComplianceSeverity.WARNING, rest.Finding.GetProperty("severity").GetInt32());
        Assert.Equal("2025-09-30", rest.Finding.GetProperty("date").GetString());
        Assert.Equal(9.0m, rest.Finding.GetProperty("actualValue").GetDecimal());
        Assert.Equal(11.0m, rest.Finding.GetProperty("thresholdValue").GetDecimal());

        var september = await GetPeriodAsync(employeeId, 2025, 9);
        Assert.Empty(DailyRestFindings(september));
    }

    // --------------------------------------------------------------------------------------
    // helpers
    // --------------------------------------------------------------------------------------

    /// <summary>The value the composed engine reads for HK / OK24 (seeded position: none).</summary>
    private static void AssertHkOk24Derogates() =>
        Assert.True(
            AgreementConfigProvider.GetConfig("HK", "OK24", null).RestPeriodDerogationAllowed,
            "the expected WARNING severity rests on HK OK24 allowing derogation");

    /// <summary>The real Skema save of one month's work-time days (intervals only, no hours cells).</summary>
    private async Task SaveWorkTimeAsync(
        string employeeId, int year, int month, params (DateOnly Date, (string Start, string End) Interval)[] days)
    {
        var rsp = await _h.Client(employeeId).PostAsJsonAsync($"/api/skema/{employeeId}/save", new
        {
            year,
            month,
            workTime = days.Select(d => new
            {
                date = d.Date,
                intervals = new[] { new { start = d.Interval.Start, end = d.Interval.End } },
            }).ToArray(),
        });
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"save expected 200, got {(int)rsp.StatusCode}: {raw}");
    }

    private async Task<JsonElement> GetPeriodAsync(string employeeId, int year, int month)
    {
        var rsp = await _h.Client(employeeId).GetAsync($"/api/compliance/{employeeId}/period?year={year}&month={month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<(string List, JsonElement Finding)> DailyRestFindings(JsonElement body) =>
        new[] { "violations", "warnings" }
            .SelectMany(list => body.GetProperty(list).EnumerateArray().Select(f => (List: list, Finding: f)))
            .Where(x => x.Finding.GetProperty("violationType").GetInt32() == (int)ComplianceViolationType.DAILY_REST)
            .ToList();

    private static List<string?> NotCheckedDays(JsonElement body) =>
        body.GetProperty("restNotCheckedDays").EnumerateArray().Select(e => e.GetString()).ToList();
}
