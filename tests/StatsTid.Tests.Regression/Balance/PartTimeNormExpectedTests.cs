using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S146 / TASK-14602-T (QUAL-198) — Docker-gated HTTP pin: the balance <c>/summary</c> shows a
/// part-time employee THEIR OWN monthly norm, not a full-timer's.
///
/// <para>
/// <b>Plain language.</b> "Hours expected this month" used to be (weekdays / 5) x the agreement's
/// weekly norm for everyone. An employee on 30 h a week (stored as the fraction 0.811) therefore saw
/// 162.80 where the Rule Engine judges the month as 22 weekdays x 6.00 = 132.00. The spec (register
/// QUAL-198, owner Q-1 2026-10-14): <c>weekdays x DayNorm.ForWeekday(weekly norm, fraction at month
/// end)</c>.
/// </para>
///
/// <para>
/// <b>Fixture.</b> <b>March 2026</b> — it starts on a Sunday and has 31 days, so 22 weekdays
/// (Mon 2 .. Tue 31). AC / OK24-OK26 weekly norm is the seeded 37.0, so a full-timer shows
/// 22 x 7.40 = 162.80 and the 0.811 employee 22 x round(37 x 0.811 / 5, 2) = 22 x 6.00 = 132.00
/// (expected values derived from the spec arithmetic, written as literals).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PartTimeNormExpectedTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (agreement configs)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>Kind F.</b> A 0.811 employee in March 2026 (22 weekdays) has <c>normHoursExpected</c>
    /// exactly 132.00; a full-time employee in the same month keeps 162.80 (regression pin R, held
    /// in the same fact so the two figures are proven to differ for the right reason).
    ///
    /// <para>
    /// <b>Red conditions.</b> The first assertion (<c>132.00m</c>) FAILS on today's (pre-14602) code:
    /// <c>BalanceEndpoints</c> computed <c>(weekdays / 5) x weeklyNormHours</c> with no fraction and
    /// answered 162.80 for the 0.811 employee, although the dated profile (0.811) was already read
    /// for the quota. Docker-gated: CI-pending, not run locally.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Summary_PartTime0811_NormExpected132()
    {
        var partTime = "emp_s146_pt_" + Guid.NewGuid().ToString("N")[..8];
        var fullTime = "emp_s146_ft_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, partTime, OrgId, "AC", "OK24", partTimeFraction: 0.811m);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, fullTime, OrgId, "AC", "OK24", partTimeFraction: 1.000m);

        var pt = await GetSummaryAsync(partTime, 2026, 3);
        var ft = await GetSummaryAsync(fullTime, 2026, 3);

        Assert.Equal(132.00m, pt.GetProperty("normHoursExpected").GetDecimal());
        Assert.Equal(162.80m, ft.GetProperty("normHoursExpected").GetDecimal());
    }

    private async Task<JsonElement> GetSummaryAsync(string employeeId, int year, int month)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId));
        var rsp = await client.GetAsync($"/api/balance/{employeeId}/summary?year={year}&month={month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string MintEmployeeToken(string actorId)
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return svc.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
    }
}
