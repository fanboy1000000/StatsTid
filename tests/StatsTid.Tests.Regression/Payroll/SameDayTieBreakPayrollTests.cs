namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14828-T (AC-C2p (3)) - two publishes dated the same Monday make one boundary, and the later publish is
/// the one that pays.
///
/// <para>
/// <b>In plain language.</b> An administrator publishes HK's agreement twice with the same "applying from" date,
/// Monday 16 March 2026: first a 37-hour version, then a 32-hour one. Only the later is in force from that Monday, and
/// March is cut at the 16th exactly once. The days from the 16th are priced on 32 hours.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SameDayTieBreakPayrollTests : DatedPayrollFixture
{
    private const string OrgId = "STY01";

    // The clock is injected at Monday 9 November 2026 (AC-C2p's note); both publishes send the date explicitly.
    private static readonly DateOnly Clock = new(2026, 11, 9);
    private static readonly DateOnly Feb01 = new(2026, 2, 1);
    private static readonly DateOnly Feb23 = new(2026, 2, 23);
    private static readonly DateOnly Feb28 = new(2026, 2, 28);
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar15 = new(2026, 3, 15);
    private static readonly DateOnly Mar16 = new(2026, 3, 16);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    /// <summary>
    /// <b>Kind N (CI; mutation M-15, pay half, at CI-M5).</b> AC-C2p (3). HK/OK24: v2 (37 h) then v3 (32 h), both
    /// published dated Monday 16 March 2026 (clock Monday 9 November 2026). An HK full-timer with 7.4 h on every weekday
    /// from 23 February (March's opening week, Monday 23 February to Sunday 1 March, reaches February) to 31 March,
    /// February and March APPROVED. <c>calculate-and-export</c> March answers 200; the month has exactly ONE boundary, at 16
    /// March (segments 1-15 and 16-31 March), and the norm figure is <b>150.80</b> h: 10 weekdays (2-13 March) at 7.40 plus
    /// 12 weekdays (16-31 March) at the later publish's 32 h / 5 = 6.40.
    ///
    /// <para>
    /// <b>Red conditions</b> (argued; Docker; run at CI-M5). N: the code before the stack priced March on the compiled 37 h
    /// (norm 162.80, failing the 150.80 assertion, so the fact is red there for a different reason). <b>M-15</b> (the
    /// same-date tie-break inverted: <c>published_at</c> ascending) puts v2 in force from the 16th: 22 weekdays at 7.40 =
    /// 162.80, and the 150.80 assertion fails. Facts expected red under M-15 besides this one: AC-C2p (1), (2) and (4)
    /// (<c>SameDayTieBreakTests</c>' three facts) again, the read halves.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TwoPublishesDatedMonday16March2026_OneBoundary_16To31MarchPaidOnTheLater()
    {
        using var admin = AdminAt(Clock);
        const string employeeId = "EMP-S148-PAY-TIEBREAK";
        await CreateAndPublishAsync(admin, HkBody(37m, "OK24"), "2026-03-16");
        await CreateAndPublishAsync(admin, HkBody(32m, "OK24"), "2026-03-16");
        await SeedEmployeeAsync(employeeId, OrgId, "HK", "OK24");
        await SeedWeekdayHoursAsync(employeeId, "HK", "OK24", Feb23, Mar31);
        await SeedApprovedMonthAsync(employeeId, OrgId, "HK", "OK24", Feb01, Feb28);
        await SeedApprovedMonthAsync(employeeId, OrgId, "HK", "OK24", Mar01, Mar31);

        var response = await CalculateAndExportAsync(employeeId, "HK", "OK24", OrgId, Mar01, Mar31);

        AssertOk(response);
        Assert.Equal(
            new[] { (Mar01, Mar15), (Mar16, Mar31) },
            (await ManifestSegmentsAsync(employeeId)).ToArray());
        Assert.Equal(150.80m, NormTotal(ReadResult(response.Json)));
    }
}
