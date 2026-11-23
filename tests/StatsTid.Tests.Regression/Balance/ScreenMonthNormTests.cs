using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.TestSupport;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S148 / TASK-14828-T - the shared Docker fixture of the facts about the two "expected hours this month" figures
/// (Teamoversigt's <c>normExpected</c> and the balance page's <c>normHoursExpected</c>): a leader whose roster holds
/// the employees, the employees (<see cref="RegressionSeed"/>, the dated record), the two reads and the profile edits
/// the facts need. One container per fact; the Backend host (its clock pinned) is booted before anyone is seeded.
/// </summary>
public abstract class ScreenNormFixture : DatedConfigDockerFixture
{
    /// <summary>The clock of the screen facts: Monday 9 November 2026 (a publish dated into 2026 is a past date).</summary>
    protected static readonly DateOnly Clock = new(2026, 11, 9);

    /// <summary>A Backend host whose clock is pinned to <see cref="Clock"/>; its first <c>CreateClient</c> runs the seeders, so call this before seeding.</summary>
    protected WebApplicationFactory<Program> ScreenHost()
    {
        var host = Factory.WithFixedToday(Clock);
        _ = host.CreateClient();
        return host;
    }

    /// <summary>A LocalLeader of <paramref name="orgId"/>: the person, the role assignment and the client.</summary>
    protected async Task<HttpClient> SeedLeaderAsync(WebApplicationFactory<Program> host, string leaderId, string orgId)
    {
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, leaderId, orgId, "HK", "OK24");
        await ExecAsync(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@leader, 'LOCAL_LEADER', @org, 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, ("leader", leaderId), ("org", orgId));
        return S120ContractAssert.LeaderClient(host, leaderId, orgId);
    }

    /// <summary>An HK employee (or <paramref name="agreementCode"/>) of <paramref name="orgId"/> reporting PRIMARY to <paramref name="leaderId"/>.</summary>
    protected async Task SeedRosterMemberAsync(
        string employeeId, string orgId, string leaderId, string agreementCode = "HK", string okVersion = "OK24",
        string? position = null)
    {
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, employeeId, orgId, agreementCode, okVersion, position: position);
        await ExecAsync(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@u, 'EMPLOYEE', @org, 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, ("u", employeeId), ("org", orgId));
        await new ReportingLineRepository(Db).AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = employeeId,
            ManagerId = leaderId,
            OrganisationId = orgId,
            Relationship = "PRIMARY",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });
    }

    /// <summary>The leader's Teamoversigt rows for a month, by employee id.</summary>
    protected static async Task<IReadOnlyDictionary<string, JsonElement>> TeamRowsAsync(HttpClient leader, int year, int month)
    {
        using var response = await leader.GetAsync($"/api/approval/team-overview?year={year}&month={month}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"team-overview expected 200, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("employees").EnumerateArray()
            .ToDictionary(r => r.GetProperty("employeeId").GetString()!, r => r.Clone());
    }

    /// <summary>One employee's balance page figure <c>normHoursExpected</c> for a month, read as the employee.</summary>
    protected static async Task<decimal> BalanceNormAsync(WebApplicationFactory<Program> host, string employeeId, string orgId, int year, int month)
    {
        using var employee = S120ContractAssert.EmployeeClient(host, employeeId, orgId);
        using var response = await employee.GetAsync($"/api/balance/{employeeId}/summary?year={year}&month={month}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"balance summary expected 200, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("normHoursExpected").GetDecimal();
    }
}

/// <summary>
/// S148 / TASK-14828-T (AC-C1n) - the two month figures add up each day's own configured norm, keep the month-end
/// fraction, and fall back to the central 37 hours for a person with no record at the month's end.
///
/// <para>
/// <b>In plain language.</b> "Hours expected this month" on Teamoversigt and on the balance page must agree with what
/// payroll judges. When an administrator changes a weekly norm in the middle of a month the figure must follow it day by day
/// (150.80 h, not 140.80 h, when 37 h becomes 32 h on Monday 16 March 2026); a part-time change in the middle of a month
/// is valued at the fraction at the month's end (81.40 h, the owner's ruling, QUAL-226 stays open); and someone with no
/// record at the month's end (a profile that closed on Friday 20 March, or none at all) still sees today's 162.80 h.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ScreenMonthNormTests : ScreenNormFixture
{
    private const string Org = "STY02";

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C1n (1). HK/OK24 republished with a 32-hour weekly norm applying from Monday 16 March 2026; an
    /// HK full-timer on Teamoversigt and the balance page, March 2026 (22 weekdays): <c>normExpected</c> and
    /// <c>normHoursExpected</c> both <b>150.80</b> (10 weekdays at 7.40 + 12 at 6.40), the figure payroll judges.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). On the code before the stack both screens
    /// multiplied the version in force at the month's end (6.40 h) by all 22 weekdays: 140.80 on each, so each
    /// assertion fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task WeeklyNormChange16March2026_Shows150Point80()
    {
        using var admin = AdminAt(Clock);
        var host = ScreenHost();
        await CreateAndPublishAsync(admin, ConfigBody("HK", "OK24", weeklyNorm: 32m), "2026-03-16");
        var leader = await SeedLeaderAsync(host, "t828sn_lead1", Org);
        await SeedRosterMemberAsync("t828sn_e1", Org, "t828sn_lead1");

        var rows = await TeamRowsAsync(leader, 2026, 3);
        var balance = await BalanceNormAsync(host, "t828sn_e1", Org, 2026, 3);

        Assert.Equal(150.80m, rows["t828sn_e1"].GetProperty("normExpected").GetDecimal());
        Assert.Equal(150.80m, balance);
    }

    /// <summary>
    /// <b>Kind N (CI; mutation M-12 at CI-M5).</b> AC-C1n (2). A constant 37-hour HK/OK24 agreement; the employee's part-time
    /// fraction is 1.0 until Sunday 15 March 2026 and 0.5 from Monday 16 March (two <c>employee_profiles</c> rows); no
    /// configuration change. Both screens show <b>81.40</b> for March 2026: 22 weekdays at the month-end fraction, round(37 x 0.5
    /// / 5, 2) = 3.70 each.
    ///
    /// <para>
    /// <b>Red conditions</b> (argued; Docker; run at CI-M5). N: the code before the stack also gives 81.40 (it used the fraction at
    /// the month's end), so no assertion fails there. <b>M-12</b> (the screens use each day's own fraction) gives 10 x 7.40 + 12
    /// x 3.70 = 118.40 on each screen, so both assertions fail. Facts expected red under M-12 besides these two: none
    /// (the other screen facts have a constant fraction).
    /// </para>
    /// </summary>
    [Fact]
    public async Task FractionChange16March2026_Shows81Point40()
    {
        var host = ScreenHost();
        var leader = await SeedLeaderAsync(host, "t828sf_lead", Org);
        await SeedRosterMemberAsync("t828sf_e1", Org, "t828sf_lead");
        await ExecAsync(
            "UPDATE employee_profiles SET effective_to = DATE '2026-03-16' WHERE employee_id = @e AND effective_to IS NULL",
            ("e", "t828sf_e1"));
        await ExecAsync(
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from, effective_to, employment_category)
            VALUES (@e, 0.500, DATE '2026-03-16', NULL, (SELECT u.employment_category FROM users u WHERE u.user_id = @e))
            """, ("e", "t828sf_e1"));

        var rows = await TeamRowsAsync(leader, 2026, 3);
        var balance = await BalanceNormAsync(host, "t828sf_e1", Org, 2026, 3);

        Assert.Equal(81.40m, rows["t828sf_e1"].GetProperty("normExpected").GetDecimal());
        Assert.Equal(81.40m, balance);
    }

    /// <summary>
    /// <b>Kind R (CI).</b> AC-C1n (4), N-2's pin. A 37-hour HK/OK24 agreement; employee A's profile closes on Friday 20 March
    /// 2026 (its last day; the end-exclusive date is Saturday 21 March) and employee B has no profile at all, so no record covers the
    /// month's end for either. Both screens answer 200 and show <b>162.80</b> (22 x 7.40: the central agreement's weekly norm,
    /// fraction 1.0) for both employees. No record is a graceful absence, not a read failure.
    ///
    /// <para>
    /// <b>Red conditions.</b> R: today's figure, held before and after the stack. The discriminating assertion is the 200 with
    /// 162.80: a calculator that refused an employee with no record at the month's end (or valued the missing days at 0) fails it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NoRecordAtMonthEnd_ProfileClosed20March_And_NoProfile_Show162Point80()
    {
        var host = ScreenHost();
        var leader = await SeedLeaderAsync(host, "t828sn_lead4", Org);
        await SeedRosterMemberAsync("t828sn_closed", Org, "t828sn_lead4");
        await SeedRosterMemberAsync("t828sn_none", Org, "t828sn_lead4");
        await ExecAsync(
            "UPDATE employee_profiles SET effective_to = DATE '2026-03-21' WHERE employee_id = @e AND effective_to IS NULL",
            ("e", "t828sn_closed"));
        await ExecAsync("DELETE FROM employee_profiles WHERE employee_id = @e", ("e", "t828sn_none"));

        var rows = await TeamRowsAsync(leader, 2026, 3);
        var closedBalance = await BalanceNormAsync(host, "t828sn_closed", Org, 2026, 3);
        var noneBalance = await BalanceNormAsync(host, "t828sn_none", Org, 2026, 3);

        Assert.Equal(162.80m, rows["t828sn_closed"].GetProperty("normExpected").GetDecimal());
        Assert.Equal(162.80m, rows["t828sn_none"].GetProperty("normExpected").GetDecimal());
        Assert.Equal(162.80m, closedBalance);
        Assert.Equal(162.80m, noneBalance);
    }
}
