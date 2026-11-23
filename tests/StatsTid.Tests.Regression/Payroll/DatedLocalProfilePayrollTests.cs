namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14828-T (AC-C1c) - each day of a month is priced with the institution's local profile version in force
/// that day.
///
/// <para>
/// <b>In plain language.</b> An institution (here STY01) can have a local agreement profile with its own weekly norm.
/// When the institution replaces it with a new version from a Monday in the middle of June, the days before that
/// Monday use the old version and the days from it the new one. Before this stack the profile's values never reached
/// pay at all: every day was priced on the compiled 37 hours.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DatedLocalProfilePayrollTests : DatedPayrollFixture
{
    private const string OrgId = "STY01";
    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C1c. STY01's HK/OK26 local profile has a weekly norm of 30 h from Monday 4 May 2026 and a
    /// second version with 32 h from Monday 15 June 2026 (the first is closed on the 15th). An HK full-timer in STY01,
    /// 7.4 h on every June weekday, June APPROVED. <c>calculate-and-export</c> June answers 200; the month is cut at the
    /// new version (1-14 and 15-30 June) and the norm figure is <b>136.80</b> h: 10 weekdays at 30 / 5 = 6.00 plus 12
    /// weekdays at 32 / 5 = 6.40.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). Before the stack the profile's values never
    /// reached pay: both parts were priced at the compiled 37 h, so the norm total is 162.80 and the 136.80 assertion
    /// fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TwoVersions_EachDayUsesTheVersionInForce()
    {
        const string employeeId = "EMP-S148-PAY-LOCALPROFILE";
        await SeedEmployeeAsync(employeeId, OrgId, "HK");
        await SeedLocalProfileAsync(OrgId, weeklyNorm: 30m, from: new DateOnly(2026, 5, 4), to: new DateOnly(2026, 6, 15));
        await SeedLocalProfileAsync(OrgId, weeklyNorm: 32m, from: new DateOnly(2026, 6, 15), to: null);
        await SeedWeekdayHoursAsync(employeeId, "HK", Ok26, Jun01, Jun30);
        await SeedApprovedMonthAsync(employeeId, OrgId, "HK", Ok26, Jun01, Jun30);

        var response = await CalculateAndExportAsync(employeeId, "HK", Ok26, OrgId, Jun01, Jun30);

        AssertOk(response);
        Assert.Equal(
            new[] { (Jun01, new DateOnly(2026, 6, 14)), (new DateOnly(2026, 6, 15), Jun30) },
            (await ManifestSegmentsAsync(employeeId)).ToArray());
        Assert.Equal(136.80m, NormTotal(ReadResult(response.Json)));
    }

    /// <summary>An HK/OK26 local profile of <paramref name="orgId"/> over [<paramref name="from"/>, <paramref name="to"/>), open when null.</summary>
    private Task SeedLocalProfileAsync(string orgId, decimal weeklyNorm, DateOnly from, DateOnly? to) =>
        ExecAsync(
            """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version,
                effective_from, effective_to,
                weekly_norm_hours, max_flex_balance,
                max_overtime_hours_per_period, overtime_requires_pre_approval,
                created_by)
            VALUES (
                gen_random_uuid(), @org, 'HK', 'OK26',
                @from, @to,
                @norm, NULL, NULL, NULL,
                'admin1')
            """,
            ("org", orgId), ("from", from), ("to", (object?)to ?? DBNull.Value), ("norm", weeklyNorm));
}
