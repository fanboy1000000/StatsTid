using StatsTid.Tests.Regression.Hosting;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14811-T (QUAL-236), AC-S0-2e — the child's call-home day through Skema and payroll, as two facts, one per
/// half of the claim. In plain terms: from 1 April 2026 (OK26) an employee whose child falls ill during the workday may
/// register the rest of that day as "call-home" hours (<c>CHILD_SICK_CALL_HOME</c>). Those hours count as worked time
/// toward the day's norm, like a sick day, and are exported for pay under the wage code <c>SLS_0530</c> (provisional:
/// the code the child's first sick day uses; Phase B names the real one).
///
/// <para><b>The fixture (both facts).</b> HK, June 2026 (OK26), opted in, through <see cref="SkemaPayrollScenario"/>:
/// every June weekday 7,40 h worked except Wednesday 10 June, which holds 3,90 h worked and <c>CHILD_SICK_CALL_HOME</c>
/// 3,50 h saved through the real Skema save, plus a +1,00 surplus on Tuesday 9 June so the month's total is not
/// trivially zero. The worked hours are seeded BEFORE the save, so the paid-absence day bound (S0-7) judges 3,50 against
/// 7,40 - 3,90 = 3,50, exactly at its limit. The screen's rule (<c>useSkema.ts</c> <c>computeDayDiffs</c>, restated in
/// <see cref="SkemaPayrollScenario.ScreenTotal"/>) gives 3,90 + 3,50 - 7,40 = 0 for 10 June and +1,00 for the month.</para>
///
/// <para><b>Kind N for both</b> (they name a code this sprint creates). Docker-gated, <b>CI-pending</b> (CI-W2, CI-M2,
/// CI-M2b); never run locally. Each is proved red by a named mutation, never by reverting:</para>
/// <list type="bullet">
///   <item><description><b>M-8</b> (drop the call-home code's classification row; CI-M2). The save's own vocabulary
///   gate refuses an unclassified code (422 <c>absence_type_unknown</c>, <c>SkemaEndpoints.cs:957-977</c>), so BOTH facts
///   trip first at the save's
///   own 200 assertion in <c>SaveCallHomeAsync</c>, before either payroll assertion is reached (NR-P29). M-8 therefore
///   proves nothing about the credit or the export half; the Unit census facts carry the classification.</description></item>
///   <item><description><b>M-22</b> (reclassify the code <c>Neutral</c>; CI-M2b). The save still accepts the code and the
///   engine still emits the line (<c>AbsenceRule.cs:66-80</c>), but the 3,50 h are no longer credited, so the month's
///   closing is -2,50, not +1,00: <b>only the credit fact</b> trips, at its closing assertion. The export fact stays
///   green.</description></item>
///   <item><description><b>M-23</b> (drop the five call-home <c>wage_type_mappings</c> rows from <c>init.sql</c>; CI-M2b).
///   Payroll skips an unmapped line with a warning (<c>PeriodCalculationService.cs:3956-3963</c>), so the export holds no
///   <c>CHILD_SICK_CALL_HOME</c> line: <b>only the export fact</b> trips, at its line assertion. The credit fact stays
///   green, because the credit is arithmetic and does not read the mapping.</description></item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ChildSickCallHomePayrollTests : IClassFixture<FlexLedgerHostFixture>, IAsyncLifetime
{
    private const string Agreement = "HK";
    private const string CallHomeCode = "CHILD_SICK_CALL_HOME";

    private readonly FlexLedgerHostFixture _fx;
    private SkemaPayrollScenario _s = null!;

    public ChildSickCallHomePayrollTests(FlexLedgerHostFixture fixture) => _fx = fixture;

    public Task InitializeAsync()
    {
        _s = SkemaPayrollScenario.Create(_fx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _s.DisposeAsync();

    /// <summary>
    /// AC-S0-2e, the credit half (kind N; M-8 at CI-M2 and M-22 at CI-M2b, see the class docstring). The month's flex
    /// closing equals the opening (0) plus the screen's total, <b>+1,00</b>: the 3,50 call-home hours count as worked
    /// time, so 10 June closes at 3,90 + 3,50 - 7,40 = 0.
    ///
    /// <para><b>Red conditions.</b> Under M-22 the hours are not credited: 10 June closes at 3,90 - 7,40 = -3,50 and the
    /// month at <b>-2,50</b>, failing <c>closing == screen total</c>. Under M-8 the save itself answers 422 and the
    /// fact fails at <c>SaveCallHomeAsync</c>'s 200 assertion.</para>
    /// </summary>
    [Fact]
    public async Task CallHome3Point5Hours_Worked3Point9_Credited()
    {
        var (employeeId, plan) = await SaveCallHomeAsync();
        await _s.SeedApprovedJuneAsync(employeeId, Agreement);
        var response = await _s.ExportJuneAsync(employeeId, Agreement);

        SkemaPayrollScenario.AssertOk(response);
        var result = FlexLedgerHostFixture.ReadResult(response.Json);
        var screenTotal = SkemaPayrollScenario.ScreenTotal(plan);
        Assert.Equal(1.00m, screenTotal); // the screen's own figure: 10 June is 0, the surplus Tuesday +1,00
        Assert.Multiple(
            () => Assert.Equal((decimal?)0m, result.OpeningFlexBalance),
            () => Assert.Equal((decimal?)screenTotal, result.ClosingFlexBalance));
    }

    /// <summary>
    /// AC-S0-2e, the export half (kind N; M-8 at CI-M2 and M-23 at CI-M2b, see the class docstring). The export holds
    /// exactly one line with source time type <c>CHILD_SICK_CALL_HOME</c>, wage type <c>SLS_0530</c> and 3,50 hours.
    ///
    /// <para><b>Red conditions.</b> Under M-23 the line is unmapped and skipped: no line carries the source time type,
    /// failing <c>Assert.Single</c>. Under M-8 the save itself answers 422 and the fact fails at
    /// <c>SaveCallHomeAsync</c>'s 200 assertion. Under M-22 it stays green (the engine emits the line whatever the
    /// classification).</para>
    /// </summary>
    [Fact]
    public async Task CallHome3Point5Hours_Worked3Point9_ExportedUnderSls0530()
    {
        var (employeeId, _) = await SaveCallHomeAsync();
        await _s.SeedApprovedJuneAsync(employeeId, Agreement);
        var response = await _s.ExportJuneAsync(employeeId, Agreement);

        SkemaPayrollScenario.AssertOk(response);
        var result = FlexLedgerHostFixture.ReadResult(response.Json);
        var line = Assert.Single(result.ExportLines, l => l.SourceTimeType == CallHomeCode);
        Assert.Multiple(
            () => Assert.Equal("SLS_0530", line.WageType),
            () => Assert.Equal(3.50m, line.Hours));
    }

    /// <summary>The shared fixture: a fresh opted-in HK employee, June's worked hours seeded first, the call-home hours saved through Skema (200 asserted).</summary>
    private async Task<(string EmployeeId, IReadOnlyList<SkemaPayrollScenario.PlannedDay> Plan)> SaveCallHomeAsync()
    {
        var employeeId = await _s.NewEmployeeAsync(Agreement, childSickEligible: true);
        var plan = SkemaPayrollScenario.FullMonthPlan(sickDayAbsenceHours: 3.5m, workedOnSickDay: 3.9m, surplusOnTuesday: 1.0m);

        foreach (var day in plan.Where(d => d.Worked != 0m))
            await _s.SeedHoursAsync(employeeId, Agreement, day.Date, day.Worked);

        var absenceDay = plan.Single(d => d.Absence != 0m);
        var save = await _s.SaveAbsencesAsync(employeeId, Agreement, (absenceDay.Date, CallHomeCode, absenceDay.Absence));
        Assert.True(save.Status == System.Net.HttpStatusCode.OK, $"the Skema save of {CallHomeCode} expected 200, got {(int)save.Status}: {save.Json}");
        return (employeeId, plan);
    }
}
