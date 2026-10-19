using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Unit.Worklist;

/// <summary>
/// S145 Stage B / TASK-14524 (pins for TASK-14521) - the worklist's "cannot recalculate" marker
/// follows the ONE shared week rule (<see cref="WeeklyContext.MidWeekAgreementChange"/>), under
/// the single Stage B register id <c>QUAL-209</c> (an agreement change dated inside a settled
/// Monday-Sunday week). Profile, category and Monday agreement changes are payable per part
/// (refinement :702) and derive NO block.
///
/// <para>
/// COMPILE-TODAY pins: they use today's signature and the residual id as a string literal, so
/// they are observed RED against master before TASK-14521 lands (constraint (a)). Expected
/// values come from the specification (Q5 (c), refinement :699-:705), never from the code.
/// </para>
/// </summary>
public sealed class BackdateWorklistResidualDerivationTests
{
    private const string Residual = "QUAL-209";

    private static string? Derive(string kind, DateOnly effectiveFrom, int year, int month) =>
        BackdateWorklistDerivation.RecalcBlockedByForTrigger(kind, effectiveFrom, year, month);

    // -- Non-residual triggers derive no block (refinement :702) ------------------------------

    [Theory]
    [InlineData(WorklistTriggerKinds.ProfileChange)]
    [InlineData(WorklistTriggerKinds.EmploymentCategoryChange)]
    public void ProfileAndCategoryChangeInsideExportedMonth_DeriveNoBlock(string kind)
    {
        // Wed 15 Apr 2026 is strictly inside April; today the derivation answers QUAL-149.
        Assert.Null(Derive(kind, new DateOnly(2026, 4, 15), 2026, 4));
    }

    [Fact]
    public void AgreementChange_MondayInsideMonth_DerivesNoBlock()
    {
        // Mon 13 Apr 2026 starts a clean week: never a mid-week change. Today: QUAL-150.
        Assert.Null(Derive(WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 4, 13), 2026, 4));
    }

    // -- The residual: an agreement change inside a settled week ------------------------------

    [Fact]
    public void AgreementChange_MidWeekInsideMonth_DerivesResidual()
    {
        // Wed 15 Apr 2026 is inside the settled week Mon 13 - Sun 19 Apr. Today: QUAL-150.
        Assert.Equal(Residual, Derive(WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 4, 15), 2026, 4));
    }

    [Fact]
    public void AgreementChange_WeekdayFirst_DerivesResidual()
    {
        // Wed 1 Apr 2026 is a weekday 1st: it sits INSIDE April's opening week (Mon 30 Mar - Sun 5 Apr,
        // settled by Sunday 5 Apr in April), so it is a mid-week change. Today null (A8 drift).
        Assert.Equal(Residual, Derive(WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 4, 1), 2026, 4));
    }

    [Fact]
    public void AgreementChange_ContextDayOnNextMonthsRow_DerivesResidual()
    {
        // Tue 29 Sep 2026 lies in the week Mon 28 Sep - Sun 4 Oct, which October settles (its Sunday
        // is 4 Oct). October's row carries the real backdate (R5(a)). Today null (A8 drift).
        Assert.Equal(Residual, Derive(WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 9, 29), 2026, 10));
    }

    // -- "One predicate, three consumers": the worklist matrix (:699 as amended) --------------
    // For an employee employed throughout the month's settled weeks. The literal expectation is
    // derived by hand from Q5 (c) (a non-Monday date inside a week whose Sunday lies in the
    // month); the derivation is then also compared with the shared predicate itself.

    [Theory]
    // April 2026 (1 Apr = Wed; opening week Mon 30 Mar - Sun 5 Apr; last week Mon 27 Apr - Sun 3 May settles in May)
    [InlineData(2026, 4, 2026, 4, 13, false)] // Monday
    [InlineData(2026, 4, 2026, 4, 15, true)]  // mid-week
    [InlineData(2026, 4, 2026, 4, 1, true)]   // weekday 1st (Wed)
    [InlineData(2026, 4, 2026, 3, 31, true)]  // context day (Tue) on April's row
    [InlineData(2026, 4, 2026, 3, 30, false)] // context Monday
    [InlineData(2026, 4, 2026, 4, 30, false)] // Thu 30 Apr: its week settles in May, so not April's
    // September 2026 (1 Sep = Tue; opening week Mon 31 Aug - Sun 6 Sep)
    [InlineData(2026, 9, 2026, 9, 14, false)] // Monday
    [InlineData(2026, 9, 2026, 9, 16, true)]  // mid-week
    [InlineData(2026, 9, 2026, 9, 1, true)]   // weekday 1st (Tue)
    [InlineData(2026, 9, 2026, 8, 31, false)] // context Monday
    [InlineData(2026, 10, 2026, 9, 29, true)] // context day on October's row
    public void ResidualMatrix_AgreesWithTheSharedWeekPredicate(
        int year, int month, int dateYear, int dateMonth, int dateDay, bool expectedBlocks)
    {
        var date = new DateOnly(dateYear, dateMonth, dateDay);
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var predicate = WeeklyContext.MidWeekAgreementChange(WeeklyContext.SettledWeeks(first, last, null), new[] { date });
        var derived = Derive(WorklistTriggerKinds.AgreementCodeChange, date, year, month);

        Assert.Equal(expectedBlocks, predicate);       // the hand-derived spec value
        Assert.Equal(predicate, derived is not null);  // one predicate, two consumers
        if (expectedBlocks) Assert.Equal(Residual, derived);
        else Assert.Null(derived);
    }

    // -- QUAL-210: where the worklist and payroll DISAGREE (registered residual, R-55) --------

    /// <summary>
    /// QUAL-210 - LEAVER. The worklist has no employment window (<c>WeeklyContext.cs:84-88</c>), so
    /// it judges Mon 28 Sep - Sun 4 Oct as OCTOBER's week and derives null for September. Payroll
    /// passes the leaver's last employed day (Wed 30 Sep) as the settlement cutoff, settles that
    /// week in SEPTEMBER, and refuses it. The first assertion (the worklist half) flips when the
    /// derivation is given the employment window; a fix that moves the derivation behind a
    /// payroll-owned query retires this fact instead.
    /// </summary>
    [Fact]
    public void KnownDrift_QUAL210_LeaverLastDayWed30Sep_ChangeTue29Sep_WorklistDerivesNull_PayrollPredicateRefuses()
    {
        var change = new DateOnly(2026, 9, 29);

        Assert.Null(Derive(WorklistTriggerKinds.AgreementCodeChange, change, 2026, 9));

        var payrollWeeks = WeeklyContext.SettledWeeks(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), cutoff: new DateOnly(2026, 9, 30));
        Assert.True(WeeklyContext.MidWeekAgreementChange(payrollWeeks, new[] { change }));
    }

    /// <summary>
    /// QUAL-210 - HIRE. A hire's first agreement row is not a change; payroll drops hire-dated rows
    /// (R43, <c>PeriodCalculationService.cs:3385-3397</c>) so it pays the month. The worklist cannot
    /// tell a hire row from a change and derives the residual (over-blocks). The first assertion
    /// repeats <c>AgreementChange_MidWeekInsideMonth_DerivesResidual</c> on purpose: this fact names
    /// the drift for the register and adds no coverage. It flips when the derivation gets the
    /// window; a payroll-owned-query fix retires it.
    /// <para>
    /// The payroll half is ILLUSTRATIVE (R-89): payroll's R43 filter is private, so the empty list
    /// stands for what it leaves and that assertion cannot fail. The executable proof is the Stage A
    /// pin <c>PayrollHostWeeklyTests.CalculateAndExport_StarterThursday3Sep_NoAugustApprovalNeeded_200</c> (H8).
    /// </para>
    /// </summary>
    [Fact]
    public void KnownDrift_QUAL210_HireDatedAgreementRowWed15Apr_WorklistBlocks_PayrollDropsItByR43()
    {
        Assert.Equal(Residual, Derive(WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 4, 15), 2026, 4));

        var payrollWeeks = WeeklyContext.SettledWeeks(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), cutoff: null);
        Assert.False(WeeklyContext.MidWeekAgreementChange(payrollWeeks, Array.Empty<DateOnly>()));
    }
}
