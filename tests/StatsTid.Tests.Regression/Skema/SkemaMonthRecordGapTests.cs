using System.Net;
using System.Text.Json;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 T14b FU-T (5a ruling T8; TASK-14828 deviation 8) - the Skema month's served minimum rest on a day whose employment record has
/// a gap.
///
/// <para>
/// <b>In plain language.</b> The Skema month serves the minimum rest in force on each day, so the screen can warn about short rests.
/// For an employee whose agreement-code history has a hole (a profile row covers the day but no agreement-code row does) a
/// <b>weekend</b> day inside the hole is shown with no minimum rest (<c>null</c>) and the month still loads: the figure is only a
/// display value, and the authoritative compliance check still answers <c>employment_record_gap</c> for a worked day. A <b>weekday</b>
/// inside the hole is not softened: the month's per-day norm rows resolve every weekday without catching the gap, so the month is
/// refused as it was before the stack. Saturday 21 and Sunday 22 March 2026 are the weekend; Wednesday 18 March 2026 the weekday.
/// Docker-gated: CI-verified, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaMonthRecordGapTests : DatedConfigDockerFixture
{
    private const string Org = "T8ORG";
    private static readonly DateOnly Clock = new(2026, 11, 9);

    /// <summary>
    /// <b>Kind N (CI).</b> T8 (weekend-only null). An HK / OK24 employee whose agreement-code history has no row for Saturday 21 and
    /// Sunday 22 March 2026 (HK to the 21st exclusive, HK again from Monday 23 March). The employee's own March month answers 200 and
    /// <c>minimumRest</c> holds <c>hours</c> null on 2026-03-21 and 2026-03-22 and <b>11</b> (HK's minimum rest) on Friday 20 and Monday 23 March.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending). N: the ruling accepts the built behaviour. A change that let the gap propagate
    /// from the served minimum rest would fail the 200; one that dropped the null (a value on the gap days) would fail the null assertions.
    /// </para>
    /// </summary>
    [Fact]
    public async Task WeekendOnlyRecordGap_Month200_MinimumRestNullOnThoseDays()
    {
        using var employee = await SeedGapEmployeeAsync("t8_we", "2026-03-21", "2026-03-23");

        using var response = await employee.GetAsync("/api/skema/t8_we/month?year=2026&month=3");
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)response.StatusCode}: {text}");
        var rest = JsonDocument.Parse(text).RootElement.GetProperty("minimumRest").EnumerateArray()
            .ToDictionary(r => r.GetProperty("date").GetString()!, r => r.GetProperty("hours"));
        Assert.Equal(JsonValueKind.Null, rest["2026-03-21"].ValueKind);
        Assert.Equal(JsonValueKind.Null, rest["2026-03-22"].ValueKind);
        Assert.Equal(11m, rest["2026-03-20"].GetDecimal());
        Assert.Equal(11m, rest["2026-03-23"].GetDecimal());
    }

    /// <summary>
    /// <b>Kind R (CI).</b> T8 (a weekday gap is refused as today). The same employee shape with the hole on Wednesday 18 March 2026 only
    /// (HK to the 18th exclusive, HK again from the 19th): the month is not served. Either the host answers 500 or the in-process test
    /// server surfaces the unhandled <c>EmployeeProfileNotFoundException</c> to the caller (the per-day norm rows throw it and no middleware
    /// maps it); a 200 fails the fact.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending). R: refused before and after the stack. A change that softened the weekday gap
    /// (the norm rows catching it) would serve a 200 and fail this fact.
    /// </para>
    /// </summary>
    [Fact]
    public async Task WeekdayRecordGap_MonthIsRefused()
    {
        using var employee = await SeedGapEmployeeAsync("t8_wd", "2026-03-18", "2026-03-19");

        HttpResponseMessage? response = null;
        var thrown = await Record.ExceptionAsync(async () =>
            response = await employee.GetAsync("/api/skema/t8_wd/month?year=2026&month=3"));

        if (thrown is not null)
            Assert.IsType<StatsTid.SharedKernel.Exceptions.EmployeeProfileNotFoundException>(thrown);
        else
            Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
    }

    /// <summary>An HK / OK24 employee whose agreement-code row ends (exclusive) on <paramref name="gapFrom"/> and resumes on <paramref name="gapTo"/>.</summary>
    private async Task<HttpClient> SeedGapEmployeeAsync(string employeeId, string gapFrom, string gapTo)
    {
        var host = Factory.WithFixedToday(Clock);
        var client = S120ContractAssert.EmployeeClient(host, employeeId, Org); // boots the host first
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, employeeId, Org, "HK", "OK24");
        await ExecAsync(
            "UPDATE user_agreement_codes SET effective_to = @gapFrom::date WHERE user_id = @e AND effective_to IS NULL",
            ("gapFrom", gapFrom), ("e", employeeId));
        await ExecAsync(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @e, 'HK', @gapTo::date, NULL, 1)
            """, ("e", employeeId), ("gapTo", gapTo));
        return client;
    }
}
