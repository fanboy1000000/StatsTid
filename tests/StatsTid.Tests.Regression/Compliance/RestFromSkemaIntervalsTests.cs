using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Tests.Shared;
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
/// engine only). The severity expectation rests on the config the request carries (ADR-043 D12;
/// the Backend sends the config in force), which for the seeded rows equals
/// <c>EngineConfigRequests.Config("HK", "OK24", position)</c>, and each fact asserts first that
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
    /// derogates), date 2026-03-11, actual 8.0, threshold 11.0; <c>restNotCheckedDays</c> holds
    /// neither date. S148 3b (RR-1): the finding is dated the day the work ends and holds the longest
    /// unbroken rest in the 24 hours before Wed 14:00 (window [Tue 14:00, Wed 14:00]: 14:00-22:00 is 8 h,
    /// 24:00-06:00 is 6 h); it was 6.0 dated 2026-03-10. 8.0 is on the 8-hour floor, so it stays a WARNING.
    ///
    /// Red conditions (the wave-3 engine): it reports 6.0 dated 2026-03-10 (the wall-clock gap between
    /// the two days), so the <c>date</c> and <c>actualValue</c> assertions fail.
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
        // S148 3b (RR-1): "2026-03-10" / 6.0 -> "2026-03-11" / 8.0
        Assert.Equal("2026-03-11", rest.Finding.GetProperty("date").GetString());
        Assert.Equal(8.0m, rest.Finding.GetProperty("actualValue").GetDecimal());
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
    /// 2025-10-01, actual 9.0, threshold 11.0, severity WARNING (HK OK24 derogates, asserted first).
    /// <b>September's GET</b>: 200 and no DAILY_REST finding (the work ends in October). S148 3b (RR-1):
    /// the finding is dated the day the work ends (end Wed 1 Oct 15:00, window from Tue 30 Sep 15:00,
    /// rest 22:00-07:00 is 9 h); it was dated 2025-09-30.
    ///
    /// Red conditions (the wave-3 engine): it dates the finding 2025-09-30, the earlier day, so the
    /// <c>date</c> assertion fails. (A reading that judged the rest in the month it STARTS would put it
    /// in September: the September <c>Assert.Empty</c> fails.)
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
        // S148 3b (RR-1): "2025-09-30" -> "2025-10-01" (the day the work ends)
        Assert.Equal("2025-10-01", rest.Finding.GetProperty("date").GetString());
        Assert.Equal(9.0m, rest.Finding.GetProperty("actualValue").GetDecimal());
        Assert.Equal(11.0m, rest.Finding.GetProperty("thresholdValue").GetDecimal());

        var september = await GetPeriodAsync(employeeId, 2025, 9);
        Assert.Empty(DailyRestFindings(september));
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, AC-S0-3k (RR-1 data edge; BC-6: the two-days-back read can only be
    /// exercised in 2024, because 31 March 2024 was a 23-hour day). A fresh HK / OK24 employee. By SQL
    /// (no save-path date gate): Sat 30 March 2024 22:00-23:59 (project hours 1.98), Sun 31 March 2024
    /// 11:30-20:00 (8.5), Mon 1 April 2024 00:00-00:30 (0.5). The employee's GET of April 2024: 200;
    /// exactly one DAILY_REST finding across violations and warnings, in <c>warnings</c> (HK derogates,
    /// <c>RestPeriodDerogationAllowed</c> = true for HK / OK24, <c>CentralAgreementConfigs.cs:65</c>,
    /// asserted first; 10.5 is at least 8), severity WARNING, date 2024-04-01, actual 10.5, threshold
    /// 11.0; <c>restNotCheckedDays</c> empty.
    ///
    /// <b>By hand:</b> the end Mon 00:30 CEST is 31 Mar 22:30 UTC; its window starts 30 Mar 22:30 UTC
    /// (23:30 CET), so work 23:30-24:00 CET is inside; the rest runs 31 Mar 00:00 CET (23:00 UTC) to
    /// 11:30 CEST (09:30 UTC): 10.5 elapsed hours. Without the 30 March read the window would start at
    /// 23:30 with no work, and the rest would be 11.0 (no finding).
    ///
    /// Red conditions (the wave-3 engine): its read starts on 31 March, so it never sees the 30 March
    /// work; the old pairing reports one finding dated 2024-03-31, 4.0 (20:00 to 00:00), so the
    /// <c>date</c> and <c>actualValue</c> assertions fail.
    /// </summary>
    [Fact]
    public async Task WorkEndOnTheFirst_AfterA23HourDay_IsJudgedFromTwoDaysBack()
    {
        AssertHkOk24Derogates();
        var employeeId = await _h.SeedEmployeeAsync();
        var sat = new DateOnly(2024, 3, 30);
        var sun = new DateOnly(2024, 3, 31);
        var mon = new DateOnly(2024, 4, 1);
        await _h.InsertWorkTimeAsync(employeeId, sat, ("22:00", "23:59"));
        await _h.InsertWorkTimeAsync(employeeId, sun, ("11:30", "20:00"));
        await _h.InsertWorkTimeAsync(employeeId, mon, ("00:00", "00:30"));
        await _h.InsertEntryAsync(employeeId, sat, 1.98m);
        await _h.InsertEntryAsync(employeeId, sun, 8.5m);
        await _h.InsertEntryAsync(employeeId, mon, 0.5m);

        var body = await GetPeriodAsync(employeeId, 2024, 4);

        var rest = Assert.Single(DailyRestFindings(body));
        Assert.Equal("warnings", rest.List);
        Assert.Equal((int)ComplianceSeverity.WARNING, rest.Finding.GetProperty("severity").GetInt32());
        Assert.Equal("2024-04-01", rest.Finding.GetProperty("date").GetString());
        Assert.Equal(10.5m, rest.Finding.GetProperty("actualValue").GetDecimal());
        Assert.Equal(11.0m, rest.Finding.GetProperty("thresholdValue").GetDecimal());
        Assert.Empty(NotCheckedDays(body));
    }

    // --------------------------------------------------------------------------------------
    // helpers
    // --------------------------------------------------------------------------------------

    /// <summary>The value the composed engine reads for HK / OK24 (seeded position: none).</summary>
    private static void AssertHkOk24Derogates() =>
        Assert.True(
            EngineConfigRequests.Config("HK", "OK24", null).RestPeriodDerogationAllowed,
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
