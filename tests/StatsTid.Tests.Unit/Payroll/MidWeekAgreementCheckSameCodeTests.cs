using System.Reflection;
using System.Runtime.ExceptionServices;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S148 T14b FU-T (5a ruling T3; AC-C13k (2); QUAL-209): payroll's "no agreement change mid-week" check counts the
/// start of an agreement-code row as a change only when the agreement actually differs from the day before.
///
/// <para>
/// <b>Plain language.</b> A new hire whose start date HR moved back has two AC rows in the agreement history: AC
/// from Monday 2 November 2026 and AC again from Tuesday 10 November 2026 (the row created on the day). The rule is
/// "a week cannot be judged under two agreements"; AC followed by AC is one agreement, so the Tuesday row is no
/// change and November calculates. A Tuesday row that switches AC to HK is still refused
/// (<c>agreement-change-mid-week</c>), and a row dated on the hire itself is still not a change at all (R43).
/// Tuesday 10 November 2026 is a Tuesday (Monday 9 November opens that week).
/// </para>
///
/// <para>
/// <b>Seam.</b> The check is a private static method over private nested types, so it is reached by reflection:
/// <c>PeriodCalculationService.EnsureNoMidWeekAgreementChange(PlannedCalculation, PlanHistories)</c> with
/// <c>PlanHistories</c>'s five-argument constructor (windows, profile dates, agreement-code dates, organisation
/// dates, <c>SameAgreementCodeRowStarts</c>). The payroll project's <c>InternalsVisibleTo</c> does not reach private
/// members. The database read that fills <c>SameAgreementCodeRowStarts</c> needs a real database and is proven by the
/// Docker fact <c>CreatedPersonBackdatedPayrollTests.November2026_CalculatesWithNoRecordGap</c>.
/// </para>
///
/// <para>
/// <b>Red conditions.</b> The N fact (<see cref="SameCodeRowStart_Tuesday10November2026_Calculates"/>) covers the new
/// check; at <c>043c5f35</c> it is red only structurally: <c>PlanHistories</c> has no <c>SameAgreementCodeRowStarts</c>
/// member (it has four constructor arguments), so the five-argument constructor lookup fails, which a compile error
/// would equally show. T3's F evidence is the Docker fact
/// <c>CreatedPersonBackdatedPayrollTests.November2026_CalculatesWithNoRecordGap</c> (refused 422
/// <c>agreement-change-mid-week</c> at <c>043c5f35</c>). The two R facts hold on the changed check
/// and are green before it as well when the constructor is called with the arguments of that day.
/// </para>
/// </summary>
public sealed class MidWeekAgreementCheckSameCodeTests
{
    private static readonly DateOnly Nov01 = new(2026, 11, 1);   // Sunday
    private static readonly DateOnly Nov02 = new(2026, 11, 2);   // Monday
    private static readonly DateOnly Nov10 = new(2026, 11, 10);  // Tuesday
    private static readonly DateOnly Nov30 = new(2026, 11, 30);  // Monday

    /// <summary>N. A same-code row starting Tuesday 10 November, for someone employed the day before, calculates.</summary>
    [Fact]
    public void SameCodeRowStart_Tuesday10November2026_Calculates()
    {
        var windows = new[] { new EmploymentWindow(Nov02, null) };

        var thrown = Record.Exception(() => Check(windows, agreementDates: [Nov02, Nov10], sameCode: [Nov10]));

        Assert.Null(thrown);
    }

    /// <summary>R. The same Tuesday row, where the agreement differs from the day before, is still refused.</summary>
    [Fact]
    public void DifferentCodeRowStart_Tuesday10November2026_IsRefusedMidWeek()
    {
        var windows = new[] { new EmploymentWindow(Nov02, null) };

        var thrown = Assert.Throws<PeriodNotCalculableException>(
            () => Check(windows, agreementDates: [Nov02, Nov10], sameCode: []));

        Assert.Equal("agreement-change-mid-week", thrown.Reason);
        Assert.Contains("2026-11-10", thrown.Message);
    }

    /// <summary>R (R43). A row dated on the hire itself (the day before is not employed) is dropped, not a change.</summary>
    [Fact]
    public void HireDatedRow_Tuesday10November2026_IsStillDropped()
    {
        var windows = new[] { new EmploymentWindow(Nov10, null) };

        var thrown = Record.Exception(() => Check(windows, agreementDates: [Nov10], sameCode: []));

        Assert.Null(thrown);
    }

    private static void Check(
        IReadOnlyList<EmploymentWindow> windows, DateOnly[] agreementDates, DateOnly[] sameCode)
    {
        var plan = EmploymentWindowPcsFixture.Plan("EMP-T3-MIDWEEK", Nov01, Nov30, windows);

        var historiesType = typeof(PeriodCalculationService).GetNestedType("PlanHistories", BindingFlags.NonPublic)!;
        var histories = historiesType.GetConstructors().Single().Invoke(new object?[]
        {
            windows,
            null,                          // employee profile dates: not read by the check
            agreementDates,
            Array.Empty<DateOnly>(),       // organisation dates: not read by the check
            new HashSet<DateOnly>(sameCode),
        });

        var check = typeof(PeriodCalculationService).GetMethod(
            "EnsureNoMidWeekAgreementChange", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            check.Invoke(null, new object?[] { plan, histories });
        }
        catch (TargetInvocationException ex)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
        }
    }
}
