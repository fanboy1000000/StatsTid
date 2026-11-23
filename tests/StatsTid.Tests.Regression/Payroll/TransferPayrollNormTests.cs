using System.Text.Json;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14828-T (AC-C13a pay half; R9 of TASK-14822's Step 5a cycle 1; C2-R5 of its cycle 2) - what a transfer does
/// to pay.
///
/// <para>
/// <b>In plain language.</b> Mette (HK) works at STY01 and moves to STY02 on Monday 15 June 2026. Each institution may
/// have its own local agreement profile, so June is priced in two parts, each under its own institution's profile. A
/// transfer that changes nothing else (the same agreement, no profiles) must not move one øre: June's totals are the same
/// with and without it, and a recalculation after such a transfer reverses and reissues nothing.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TransferPayrollNormTests : DatedPayrollFixture
{
    private const string FromOrg = "STY01";
    private const string ToOrg = "STY02";

    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun14 = new(2026, 6, 14);
    private static readonly DateOnly Jun15 = new(2026, 6, 15);
    private static readonly DateOnly Jun22 = new(2026, 6, 22);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C13a, pay half. STY01's HK/OK26 local profile has a weekly norm of 30 h and STY02's 32 h (both
    /// open from Monday 4 May 2026). An HK full-timer in STY01 moves to STY02 on Monday 15 June 2026 (the split is
    /// TASK-14822-T's fact); 7.4 h on every June weekday, June APPROVED. <c>calculate-and-export</c> June answers 200; the month
    /// is two parts (1-14 and 15-30 June) and the norm figure is <b>136.80</b> h: 10 weekdays at STY01's 30 / 5 = 6.00 plus 12
    /// weekdays at STY02's 32 / 5 = 6.40.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). Before the stack no local profile reached pay: both
    /// parts were priced at the compiled 37 h, so the norm total is 162.80 and the 136.80 assertion fails. Priced
    /// with one institution's profile for the whole month it would be 30 or 32 h throughout (132.00 or 140.80).
    /// </para>
    /// </summary>
    [Fact]
    public async Task EachPartResolvesItsOwnInstitutionsProfile()
    {
        const string employeeId = "EMP-S148-PAY-TRANSFER-NORM";
        await SeedEmployeeAsync(employeeId, FromOrg, "HK");
        await SeedLocalProfileAsync(FromOrg, 30m);
        await SeedLocalProfileAsync(ToOrg, 32m);
        await RecordTransferAsync(employeeId, Jun15);
        await SeedWeekdayHoursAsync(employeeId, "HK", Ok26, Jun01, Jun30);
        await SeedApprovedMonthAsync(employeeId, FromOrg, "HK", Ok26, Jun01, Jun30);

        var response = await CalculateAndExportAsync(employeeId, "HK", Ok26, ToOrg, Jun01, Jun30);

        AssertOk(response);
        Assert.Equal(new[] { (Jun01, Jun14), (Jun15, Jun30) }, (await ManifestSegmentsAsync(employeeId)).ToArray());
        Assert.Equal(136.80m, NormTotal(ReadResult(response.Json)));
    }

    /// <summary>
    /// <b>Kind R (CI).</b> R9. Two HK full-timers with the same recorded hours (7.4 h on each June weekday and 9.4 h on Monday
    /// 22 June, so the week of 22-28 June pays 2.00 h of OVERTIME_50), neither institution having a local profile: employee A
    /// stays in STY01 all June; employee B moves to STY02 on Monday 15 June 2026 and nothing else changes. Both Junes are
    /// calculated. B's month is cut at the 15th (two segments; A's has one) and the two months' export lines have equal totals:
    /// per wage type, equal hours and equal amounts, with NORMAL_HOURS (SLS_0110) and OVERTIME_50 (SLS_0210) both present. A
    /// pure transfer split moves no amount.
    ///
    /// <para>
    /// <b>Product code between the two readings:</b> each month is a real calculation by the real engine; the transfer
    /// changes only B's plan. <b>Red conditions.</b> R: green before the stack as well (the split did not exist, the totals were
    /// equal trivially); the two-segment assertion is what proves B's calculation saw the transfer. It fails if the split
    /// priced a part differently (a part dropped, a context day counted twice, a week judged twice).
    /// </para>
    /// </summary>
    [Fact]
    public async Task PureTransferSplit_JuneLineTotalsEqualWithAndWithoutTheTransfer()
    {
        const string stays = "EMP-S148-PAY-R9-STAYS";
        const string moves = "EMP-S148-PAY-R9-MOVES";
        foreach (var employeeId in new[] { stays, moves })
        {
            await SeedEmployeeAsync(employeeId, FromOrg, "HK");
            await SeedWeekdayHoursAsync(employeeId, "HK", Ok26, Jun01, Jun22.AddDays(-1));
            await SeedHoursOnAsync(employeeId, "HK", Ok26, Jun22, 9.4m); // Monday 22 June, 2.0 h above the day norm
            await SeedWeekdayHoursAsync(employeeId, "HK", Ok26, Jun22.AddDays(1), Jun30);
            await SeedApprovedMonthAsync(employeeId, FromOrg, "HK", Ok26, Jun01, Jun30);
        }
        await RecordTransferAsync(moves, Jun15);

        var a = await CalculateAndExportAsync(stays, "HK", Ok26, FromOrg, Jun01, Jun30);
        var b = await CalculateAndExportAsync(moves, "HK", Ok26, ToOrg, Jun01, Jun30);

        AssertOk(a);
        AssertOk(b);
        Assert.Single(await ManifestSegmentsAsync(stays));
        Assert.Equal(2, (await ManifestSegmentsAsync(moves)).Count);
        var totalsA = LineTotals(ReadResult(a.Json));
        var totalsB = LineTotals(ReadResult(b.Json));
        Assert.Contains("SLS_0110", totalsA.Keys);
        Assert.Contains("SLS_0210", totalsA.Keys);
        Assert.Equal(totalsA, totalsB);
    }

    /// <summary>
    /// <b>Kind R (CI).</b> C2-R5. June 2026 is exported for an HK full-timer in STY01, then a transfer to STY02 effective Monday
    /// 15 June 2026 is recorded (it changes nothing else), then a GlobalAdmin's <c>POST /api/payroll/recalculate</c> of June
    /// runs. The recalculation answers 200 and emits <b>no correction line</b>: the diff key is the wage type alone
    /// (<c>RetroactiveCorrectionService.ProduceCorrectionLines</c> groups lines by <c>WageType</c> and sums hours and amounts,
    /// so a line's period is not part of the key), the hours and amounts are unchanged, so nothing is reversed and reissued. The
    /// recalculation's own manifest has two segments, which proves it saw the transfer.
    ///
    /// <para>
    /// <b>Red conditions.</b> R: it fails if the correction keyed lines by (wage type, period): the 1-30 June line would be
    /// reversed and 1-14 and 15-30 June lines issued, so <c>correctionLines</c> would be non-empty.
    /// </para>
    /// </summary>
    [Fact]
    public async Task RecalculateAfterAPureTransfer_EmitsNothing()
    {
        const string employeeId = "EMP-S148-PAY-C2R5";
        await SeedEmployeeAsync(employeeId, FromOrg, "HK");
        await SeedWeekdayHoursAsync(employeeId, "HK", Ok26, Jun01, Jun30);
        await SeedApprovedMonthAsync(employeeId, FromOrg, "HK", Ok26, Jun01, Jun30);

        var exported = await CalculateAndExportAsync(employeeId, "HK", Ok26, FromOrg, Jun01, Jun30);
        AssertOk(exported);
        Assert.Single(await ManifestSegmentsAsync(employeeId));

        await RecordTransferAsync(employeeId, Jun15);
        var recalculated = await RecalculateAsync(employeeId, "HK", Ok26, ToOrg, Jun01, Jun30);

        AssertOk(recalculated);
        using var doc = JsonDocument.Parse(recalculated.Json);
        Assert.Equal(0, doc.RootElement.GetProperty("correctionLines").GetArrayLength());
        Assert.Equal(2, (await ManifestSegmentsAsync(employeeId)).Count);
    }

    private static SortedDictionary<string, (decimal Hours, decimal Amount)> LineTotals(StatsTid.SharedKernel.Models.PeriodCalculationResult result) =>
        new(result.ExportLines
            .GroupBy(l => l.WageType)
            .ToDictionary(g => g.Key, g => (g.Sum(l => l.Hours), g.Sum(l => l.Amount))));

    private Task SeedLocalProfileAsync(string orgId, decimal weeklyNorm) =>
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
                DATE '2026-05-04', NULL,
                @norm, NULL, NULL, NULL,
                'admin1')
            """,
            ("org", orgId), ("norm", weeklyNorm));

    /// <summary>A transfer to STY02 effective <paramref name="effectiveFrom"/>, written by SQL as the route's row shape (end-exclusive close, successor row, <c>users.primary_org_id</c>).</summary>
    private async Task RecordTransferAsync(string employeeId, DateOnly effectiveFrom)
    {
        await ExecAsync(
            "UPDATE user_organisations SET effective_to = @d WHERE user_id = @u AND effective_to IS NULL",
            ("d", effectiveFrom), ("u", employeeId));
        await ExecAsync(
            """
            INSERT INTO user_organisations (assignment_id, user_id, org_id, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, @o, @d, NULL, 2)
            """, ("u", employeeId), ("o", ToOrg), ("d", effectiveFrom));
        await ExecAsync("UPDATE users SET primary_org_id = @o WHERE user_id = @u", ("o", ToOrg), ("u", employeeId));
    }
}
