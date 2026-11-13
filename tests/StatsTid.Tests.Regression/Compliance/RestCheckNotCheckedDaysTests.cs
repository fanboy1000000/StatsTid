using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Compliance;

/// <summary>
/// S147 TASK-14706-T (QUAL-234, refinement T7, AC-234i-1 server half), re-pointed by S148 TASK-14813-T
/// (S0-3, P-12, R-22) - Docker-gated HTTP pins: <c>GET /api/compliance/{employeeId}/period</c> serves
/// <c>restNotCheckedDays</c>, the days whose project hours are not all on the clock.
///
/// <para>
/// <b>Plain language.</b> A month registered in Skema has project hours, and the 11-hour rest check
/// measures the clock intervals the employee typed. A day with project hours and no intervals cannot
/// be measured, so it is listed instead of looking compliant. These facts prove that the REAL Backend
/// route (projection reads, normalization, employment-window read, profile resolve, the request to the
/// real rule engine, deserialization of its answer) hands the dates to the caller, that a real breach
/// on clocked days is still reported beside them, and that a month with no employed day returns the
/// empty list rather than a missing member.
/// </para>
///
/// <para>
/// <b>Harness.</b> <see cref="RestComplianceRealEngineHarness"/>: the Backend's named rule-engine client
/// is routed to the real rule-engine host in-process, so the engine's own <c>check-compliance</c>
/// mapping and binding run (S148 BC-6). The expected dates are written from the spec (the seeded
/// rows), not read back from the rule.
/// </para>
///
/// <para>
/// <b>CI-pending.</b> Docker is unavailable locally; both facts are reported CI-pending until CI-W3.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RestCheckNotCheckedDaysTests : IAsyncLifetime
{
    private RestComplianceRealEngineHarness _h = null!;

    public async Task InitializeAsync() => _h = await RestComplianceRealEngineHarness.StartAsync();

    public async Task DisposeAsync()
    {
        if (_h is not null)
            await _h.DisposeAsync();
    }

    /// <summary>
    /// Kind <b>F</b> (CI; argued, S148 S0-3 re-pin). A Skema-registered March 2026 (HK, OK24): project
    /// hours on Mon 2, Tue 3 and Wed 4 (7.4 each, no intervals), plus two CLOCKED weekdays: Thu 12 March
    /// 8 project hours with an interval 14:00-22:00 and Fri 13 March 8 project hours with an interval
    /// 06:00-14:00 (rows in <c>work_time_projection</c>), whose rest gap is exactly 8 hours. The clock
    /// times moved from the time-entry rows (which the rest check no longer reads) to the interval
    /// rows; expected values unchanged.
    ///
    /// Red conditions (today, before TASK-14813): the compliance read sends no intervals, so the engine
    /// forms no work period: Thu 12 and Fri 13 March are listed (the dates list is five long, not
    /// three) and no DAILY_REST finding exists (<c>Assert.Equal</c> on the dates and
    /// <c>Assert.Single(findings)</c> both fail).
    ///
    /// Also asserted: the 8-hour gap is still reported as a daily-rest finding (violationType 0, actual
    /// 8.0, threshold 11.0) in violations OR warnings - HK OK24 derogates, which makes it a warning;
    /// either way it is not hidden by the list - and the not-checked list holds exactly the three
    /// project-hours-only dates, in order, none of the clocked days.
    /// </summary>
    [Fact]
    public async Task SkemaMonth_HoursOnlyDays_AreServed_AndTheEightHourGapIsStillFound()
    {
        var employeeId = await _h.SeedEmployeeAsync();
        await _h.InsertEntryAsync(employeeId, new DateOnly(2026, 3, 2), 7.4m);
        await _h.InsertEntryAsync(employeeId, new DateOnly(2026, 3, 3), 7.4m);
        await _h.InsertEntryAsync(employeeId, new DateOnly(2026, 3, 4), 7.4m);
        // S148 S0-3 (P-12): the clock times are work-time intervals now, the hours stay entries
        await _h.InsertEntryAsync(employeeId, new DateOnly(2026, 3, 12), 8m);
        await _h.InsertEntryAsync(employeeId, new DateOnly(2026, 3, 13), 8m);
        await _h.InsertWorkTimeAsync(employeeId, new DateOnly(2026, 3, 12), ("14:00", "22:00"));
        await _h.InsertWorkTimeAsync(employeeId, new DateOnly(2026, 3, 13), ("06:00", "14:00"));

        var rsp = await _h.Client(employeeId).GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=3");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var dates = body.GetProperty("restNotCheckedDays").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { "2026-03-02", "2026-03-03", "2026-03-04" }, dates);

        var findings = body.GetProperty("violations").EnumerateArray()
            .Concat(body.GetProperty("warnings").EnumerateArray())
            .Where(f => f.GetProperty("violationType").GetInt32() == (int)ComplianceViolationType.DAILY_REST)
            .ToList();
        var rest = Assert.Single(findings);
        Assert.Equal(8.0m, rest.GetProperty("actualValue").GetDecimal());
        Assert.Equal(11.0m, rest.GetProperty("thresholdValue").GetDecimal());
    }

    /// <summary>
    /// Kind <b>F</b>. A month with NO employed day (hire 2026-05-10, month April 2026): the route's own
    /// empty result returns <c>restNotCheckedDays</c> as an EMPTY ARRAY, never a missing member.
    ///
    /// Red conditions (today): <c>GetProperty("restNotCheckedDays")</c> throws KeyNotFoundException,
    /// the empty-month result is built in the endpoint and had no such member.
    /// </summary>
    [Fact]
    public async Task MonthWithNoEmployedDay_ReturnsAnEmptyNotCheckedList()
    {
        var hire = new DateOnly(2026, 5, 10);
        var employeeId = await _h.SeedEmployeeAsync(hire);

        var rsp = await _h.Client(employeeId).GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=4");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var list = body.GetProperty("restNotCheckedDays");
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Empty(list.EnumerateArray());
    }
}
