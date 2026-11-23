using StatsTid.Tests.Regression.Balance;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S148 T14b FU-T (5a ruling T17) - Teamoversigt's expected hours follow a local agreement profile that takes effect in the
/// middle of the month, day by day.
///
/// <para>
/// <b>In plain language.</b> An institution's local agreement profile can change the weekly norm from one Monday on. Teamoversigt
/// reuses one configuration for each stretch of unchanged days (B6), and the stretch must end the day before the local profile
/// starts. March 2026 has 22 weekdays; with 37 h before Monday 16 March (a day is 37 / 5 = 7.40) and 32 h from that Monday (a day is
/// 32 / 5 = 6.40) the month is 10 x 7.40 + 12 x 6.40 = 74.00 + 76.80 = <b>150.80</b>. (2-6 and 9-13 March are the 10 days before;
/// 16-20, 23-27 and 30-31 March the 12 from the 16th; the days are each rounded by the shared day-norm helper.)
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TeamoversigtLocalLayerMidMonthTests : ScreenNormFixture
{
    private const string Org = "T17ORG";

    /// <summary>
    /// <b>Kind N (CI).</b> T17. An HK / OK24 employee of organisation <c>T17ORG</c> on a leader's roster; a local HK / OK24 profile of
    /// that organisation with a 32-hour weekly norm taking effect on Monday 16 March 2026 (no end). Teamoversigt for March 2026
    /// shows <c>normExpected</c> <b>150.80</b>, the per-day sum.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; N: no red is claimed at <c>043c5f35</c>, the question is whether the
    /// request-scoped reuse cuts at the local layer's start). A stretch that did not end the day before the local profile starts would
    /// value all 22 days at one figure: 162.80 (the first stretch reused to month end) or 140.80 (the later one applied backwards).
    /// </para>
    /// </summary>
    [Fact]
    public async Task LocalProfileFrom16March_WeeklyNorm32_Shows150Point80()
    {
        var host = ScreenHost();
        var leader = await SeedLeaderAsync(host, "t17_lead", Org);
        await SeedRosterMemberAsync("t17_e1", Org, "t17_lead", "HK", "OK24");
        await ExecAsync(
            """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version, effective_from, effective_to,
                weekly_norm_hours, max_flex_balance, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by)
            VALUES (gen_random_uuid(), @org, 'HK', 'OK24', DATE '2026-03-16', NULL, 32, NULL, NULL, NULL, 'admin1')
            """, ("org", Org));

        var rows = await TeamRowsAsync(leader, 2026, 3);

        Assert.Equal(150.80m, rows["t17_e1"].GetProperty("normExpected").GetDecimal());
    }
}
