using StatsTid.Tests.Regression.Balance;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S148 / TASK-14828-T (AC-C1m (2)) - Teamoversigt's expected hours include a position override's weekly norm and an
/// institution's local weekly norm, not the agreement's alone.
///
/// <para>
/// <b>In plain language.</b> Two employees on HK, June 2026 (22 weekdays). A holds a position whose database override sets a
/// weekly norm of 30 hours, B works in an institution whose local profile sets 32. Teamoversigt must show what payroll judges:
/// A 132.00 (22 x 6.00), B 140.80 (22 x 6.40). Before this stack Teamoversigt read the agreement alone and showed 162.80 for
/// both.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TeamoversigtNormSourcesTests : ScreenNormFixture
{
    private const string OrgA = "T828ORGA";
    private const string OrgB = "T828ORGB";

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C1m (2). Employee A (organisation <c>T828ORGA</c>) holds a test-unique HK position whose override
    /// sets <c>WeeklyNormHours</c> 30, applying from Monday 4 May 2026 (created through the route, so its version row exists);
    /// employee B (organisation <c>T828ORGB</c>, no position) has a local HK/OK26 profile of 32 h from Monday 4 May 2026. The two
    /// are in different organisations so B's local profile cannot also reach A. Each leader reads June 2026: A's
    /// <c>normExpected</c> is <b>132.00</b>, B's <b>140.80</b>.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). Before the stack Teamoversigt read the agreement config
    /// alone: both rows are 162.80, so both assertions fail.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OverrideAndLocalNorm_Included()
    {
        using var admin = AdminAt(Clock);
        var host = ScreenHost();
        var position = await SeedPositionAsync("T828P");
        await CreateOverrideAsync(admin, OverrideBody("HK", Ok26, position, maxFlexBalance: null, weeklyNormHours: 30m, appliesFrom: "2026-05-04"));
        var leaderA = await SeedLeaderAsync(host, "t828ns_leadA", OrgA);
        var leaderB = await SeedLeaderAsync(host, "t828ns_leadB", OrgB);
        await SeedRosterMemberAsync("t828ns_a", OrgA, "t828ns_leadA", "HK", Ok26, position);
        await SeedRosterMemberAsync("t828ns_b", OrgB, "t828ns_leadB", "HK", Ok26);
        await ExecAsync(
            """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version, effective_from, effective_to,
                weekly_norm_hours, max_flex_balance, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by)
            VALUES (gen_random_uuid(), @org, 'HK', 'OK26', DATE '2026-05-04', NULL, 32, NULL, NULL, NULL, 'admin1')
            """, ("org", OrgB));

        var rowsA = await TeamRowsAsync(leaderA, 2026, 6);
        var rowsB = await TeamRowsAsync(leaderB, 2026, 6);

        Assert.Equal(132.00m, rowsA["t828ns_a"].GetProperty("normExpected").GetDecimal());
        Assert.Equal(140.80m, rowsB["t828ns_b"].GetProperty("normExpected").GetDecimal());
    }
}
