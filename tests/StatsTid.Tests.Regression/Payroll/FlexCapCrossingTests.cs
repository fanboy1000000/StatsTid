namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S147 / TASK-14708-T (QUAL-231) — AC-231-7: the extra flex credit a stored sick day now earns can carry the balance
/// over the agreement's flex cap, and then the excess becomes a <c>FLEX_PAYOUT</c> line: on the export and on a later
/// correction.
///
/// <para>
/// <b>The story, in plain language.</b> Flex balance is capped (HK 100 h). Hours above the cap are paid out as a
/// <c>FLEX_PAYOUT</c> line instead of being kept. Before S147 a stored sick day was NOT credited, so it quietly cost a
/// day of flex and could hide a cap crossing; now it counts as worked, so an employee close to the cap can cross it
/// through the credit. These facts put an HK employee 2.00 h under the cap (opening 98.00, from a recorded May ledger
/// record), give them a surplus of 5.00 h on Tuesday 9 June and a stored <c>SICK_DAY</c> (7.40 h, Wednesday 10 June,
/// through the real Skema save), and read what payroll does with the month.
/// </para>
///
/// <para>
/// <b>Expected values come from the spec</b> (Statens arbejdstidsaftale § 7 nr. 2: a paid sick day counts with the day's
/// planned hours, 7.40 for a full-timer; the HK flex cap of 100 h, <c>docs/references/danish-agreements.md</c>; the
/// screen's rule <c>worked + absence - norm</c> per registered day, <c>useSkema.ts:712-736</c>, re-stated in
/// <see cref="SkemaPayrollScenario.ScreenTotal"/>). The month's flex movement is the screen total: 0.00 on the sick
/// day, +5.00 on 9 June, so <b>+5.00</b>. Raw balance 98.00 + 5.00 = 103.00; the cap holds the closing at <b>100.00</b>
/// and the <b>3.00</b> above it is the <c>FLEX_PAYOUT</c>. (Weekly overtime does not enter: the week of 8-14 June has
/// 34.60 h worked, under the 37 h norm.)
/// </para>
///
/// <para>Docker-gated: <b>CI-pending</b>, never locally green.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FlexCapCrossingTests : IClassFixture<FlexLedgerHostFixture>, IAsyncLifetime
{
    private static readonly DateOnly May = new(2026, 5, 1);
    private readonly FlexLedgerHostFixture _fx;
    private SkemaPayrollScenario _s = null!;

    public FlexCapCrossingTests(FlexLedgerHostFixture fixture) => _fx = fixture;

    public Task InitializeAsync()
    {
        _s = SkemaPayrollScenario.Create(_fx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _s.DisposeAsync();

    /// <summary>
    /// AC-231-7, on EXPORT (kind F, Docker). May closed at 98.00 (a recorded ledger month), so June opens at 98.00
    /// (<c>CHAINED</c>). The plan above, with <c>SICK_DAY</c> stored through Skema: the export answers 200 and the month
    /// closes at exactly <b>100.00</b> (the cap) with exactly one <c>FLEX_PAYOUT</c> line of exactly <b>3.00</b> h, the
    /// same figure on the response and on the record's current lines.
    ///
    /// <para><b>Red conditions (F; Docker).</b> Before S147 the stored sick day was not credited: the month's movement
    /// was 5.00 - 7.40 = -2.40, raw balance 95.60, so the closing was <b>95.60</b> and NO <c>FLEX_PAYOUT</c> line existed,
    /// failing both <c>closing == 100.00</c> and <c>FLEX_PAYOUT == 3.00</c>.</para>
    /// </summary>
    [Fact]
    public async Task Export_StoredSickDayCreditCrossesTheFlexCap_PaysTheExcessAsFlexPayout()
    {
        var employeeId = await NewHkEmployeeNearTheCapAsync();
        var plan = SkemaPayrollScenario.FullMonthPlan(sickDayAbsenceHours: 7.4m, workedOnSickDay: 0m, surplusOnTuesday: 5.0m);
        Assert.Equal(5.00m, SkemaPayrollScenario.ScreenTotal(plan));

        await _s.SaveAndSeedAsync(employeeId, "HK", plan, "SICK_DAY");
        await _s.SeedApprovedJuneAsync(employeeId, "HK");
        var response = await _s.ExportJuneAsync(employeeId, "HK");

        SkemaPayrollScenario.AssertOk(response);
        var result = FlexLedgerHostFixture.ReadResult(response.Json);
        var payouts = result.RuleResults.SelectMany(r => r.LineItems).Where(li => li.TimeType == "FLEX_PAYOUT").ToList();
        var record = await _fx.RequireRecordAsync(employeeId, SkemaPayrollScenario.June);
        var recordedPayouts = record.CurrentLines.Where(l => l.SourceTimeType == "FLEX_PAYOUT").ToList();

        Assert.Multiple(
            () => Assert.Equal((decimal?)98.00m, result.OpeningFlexBalance),
            () => Assert.Equal((decimal?)100.00m, result.ClosingFlexBalance),
            () => Assert.Equal(new[] { 3.00m }, payouts.Select(li => li.Hours).ToArray()),
            () => Assert.Equal(new[] { 3.00m }, recordedPayouts.Select(l => l.Hours).ToArray()),
            () => Assert.Equal((decimal?)100.00m, record.Closing));
    }

    /// <summary>
    /// AC-231-7, on CORRECTION (kind F, Docker). June is first exported WITHOUT the sick day (nothing registered for
    /// Wednesday 10 June: the month moves 5.00 - 7.40 = -2.40 and closes at 95.60, no payout). The month is then reopened
    /// (the approval row removed: the fixture-only state of "a late change"), the stored <c>SICK_DAY</c> is saved
    /// through Skema, and <c>POST /api/payroll/recalculate</c> corrects the month: the correction reports the closing
    /// before (<b>95.60</b>) and after (<b>100.00</b>) and carries a <c>FLEX_PAYOUT</c> correction line that APPEARS
    /// (original 0.00 h, corrected 3.00 h, difference 3.00 h).
    ///
    /// <para><b>Red conditions (F; Docker).</b> Before S147 the correction did not credit the stored sick day: the closing
    /// after stayed <b>95.60</b> and no <c>FLEX_PAYOUT</c> correction line existed, failing <c>closingAfter == 100.00</c>
    /// and the line's 3.00 h.</para>
    /// </summary>
    [Fact]
    public async Task Correction_StoredSickDayCreditCrossesTheFlexCap_AddsTheFlexPayoutLine()
    {
        var employeeId = await NewHkEmployeeNearTheCapAsync();
        var plan = SkemaPayrollScenario.FullMonthPlan(sickDayAbsenceHours: 7.4m, workedOnSickDay: 0m, surplusOnTuesday: 5.0m);
        foreach (var day in plan.Where(d => d.Worked != 0m))
            await _s.SeedHoursAsync(employeeId, "HK", day.Date, day.Worked);
        await _s.SeedApprovedJuneAsync(employeeId, "HK");

        var original = await _s.ExportJuneAsync(employeeId, "HK");
        SkemaPayrollScenario.AssertOk(original);
        var originalResult = FlexLedgerHostFixture.ReadResult(original.Json);
        Assert.Equal((decimal?)95.60m, originalResult.ClosingFlexBalance); // the control: no sick day, no payout
        Assert.DoesNotContain(originalResult.RuleResults.SelectMany(r => r.LineItems), li => li.TimeType == "FLEX_PAYOUT");

        await _s.RemoveApprovalAsync(employeeId, SkemaPayrollScenario.June);
        var save = await _s.SaveAbsencesAsync(employeeId, (SkemaPayrollScenario.AbsenceDay, "SICK_DAY", 7.4m));
        Assert.True(save.Status == System.Net.HttpStatusCode.OK, $"the late Skema save expected 200, got {(int)save.Status}: {save.Json}");

        var correction = await _s.RecalculateJuneAsync(employeeId, "HK");

        SkemaPayrollScenario.AssertOk(correction);
        var result = FlexLedgerHostFixture.ReadCorrection(correction.Json);
        var payoutLines = result.CorrectionLines.Where(l => l.SourceTimeType == "FLEX_PAYOUT").ToList();
        Assert.Multiple(
            () => Assert.Equal((decimal?)95.60m, result.ClosingFlexBalanceBefore),
            () => Assert.Equal((decimal?)100.00m, result.ClosingFlexBalanceAfter),
            () => Assert.Equal(new[] { 0.00m }, payoutLines.Select(l => l.OriginalHours).ToArray()),
            () => Assert.Equal(new[] { 3.00m }, payoutLines.Select(l => l.CorrectedHours).ToArray()),
            () => Assert.Equal(new[] { 3.00m }, payoutLines.Select(l => l.DifferenceHours).ToArray()));
    }

    /// <summary>An HK employee whose May ledger record closed at 98.00 h, 2.00 h under the cap of 100 h; May is approved.</summary>
    private async Task<string> NewHkEmployeeNearTheCapAsync()
    {
        var employeeId = await _s.NewEmployeeAsync("HK");
        await _fx.SeedLedgerRecordAsync(employeeId, May, closing: 98m);
        await _s.SeedApprovedMonthAsync(employeeId, "HK", May, new DateOnly(2026, 5, 31));
        return employeeId;
    }
}
