using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Compliance;

/// <summary>
/// S137 / TASK-13703 (ADR-040 D10) — PURE pins for the "first employed day of the month"
/// derivation the compliance check uses to decide (a) whether there is anything to check at all
/// and (b) which date to resolve the employment profile at.
///
/// <para>
/// In plain terms: the compliance check used to assume every employee existed on the 1st of every
/// month. Since S136 a new hire's profile starts at the hire date, so that assumption produced a
/// 500 for the hire month and a meaningless check for a month before the hire. This derivation
/// answers "which is the first day of this month the person was actually employed?" from the
/// employment window(s) — <c>null</c> meaning "no day at all — nothing to check".
/// </para>
///
/// <para>
/// <b>S138 / TASK-13806 — moved here from the Regression suite and re-targeted.</b> The
/// derivation now lives on the record as
/// <see cref="EmploymentWindow.FirstEmployedDayWithin(IReadOnlyList{EmploymentWindow}, DateOnly, DateOnly)"/>
/// (the compliance endpoint's <c>FirstEmployedDayInMonth</c> is a private one-liner over it), so
/// these pins need no Backend reference and run in the local Unit suite where a pure test
/// belongs. Semantics under test are unchanged: each window is clipped to the month with
/// ADR-040 D1/D2 (end INCLUSIVE, a <c>null</c> side unbounded), the result is the earliest
/// clipped start across ALL windows (the union), and windows that contribute no day are ignored.
/// </para>
/// </summary>
public sealed class ComplianceWindowUnionTests
{
    private static readonly DateOnly MonthStart = new(2026, 3, 1);
    private static readonly DateOnly MonthEnd = new(2026, 3, 31);

    private static DateOnly? FirstEmployedDayInMonth(IReadOnlyList<EmploymentWindow> windows)
        => EmploymentWindow.FirstEmployedDayWithin(windows, MonthStart, MonthEnd);

    [Fact]
    public void EmptyList_MeansNoEmployedDay_ReturnsNull()
    {
        var first = FirstEmployedDayInMonth(Array.Empty<EmploymentWindow>());

        Assert.Null(first);
    }

    [Fact]
    public void BothNullWindow_IsUnbounded_FirstDayIsMonthStart()
    {
        var first = FirstEmployedDayInMonth(new[] { new EmploymentWindow(null, null) });

        Assert.Equal(MonthStart, first);
    }

    [Fact]
    public void HireBeforeMonth_OpenEnded_FirstDayIsMonthStart()
    {
        var first = FirstEmployedDayInMonth(
            new[] { new EmploymentWindow(new DateOnly(2025, 1, 1), null) });

        Assert.Equal(MonthStart, first);
    }

    [Fact]
    public void MidMonthHire_FirstDayIsTheHireDate()
    {
        var hire = new DateOnly(2026, 3, 15);
        var first = FirstEmployedDayInMonth(new[] { new EmploymentWindow(hire, null) });

        Assert.Equal(hire, first);
    }

    [Fact]
    public void HireOnLastDayOfMonth_StillCounts_EndInclusive()
    {
        var first = FirstEmployedDayInMonth(new[] { new EmploymentWindow(MonthEnd, null) });

        Assert.Equal(MonthEnd, first);
    }

    [Fact]
    public void LeaverMidMonth_FirstDayIsMonthStart()
    {
        // Employed [null, 15 Mar]: the month's first day is employed; the end only trims the tail.
        var first = FirstEmployedDayInMonth(
            new[] { new EmploymentWindow(null, new DateOnly(2026, 3, 15)) });

        Assert.Equal(MonthStart, first);
    }

    [Fact]
    public void LeaverOnFirstOfMonth_EndInclusive_FirstDayIsMonthStart()
    {
        var first = FirstEmployedDayInMonth(new[] { new EmploymentWindow(null, MonthStart) });

        Assert.Equal(MonthStart, first);
    }

    [Fact]
    public void WindowEntirelyBeforeMonth_ContributesNothing_ReturnsNull()
    {
        var first = FirstEmployedDayInMonth(
            new[] { new EmploymentWindow(new DateOnly(2025, 1, 1), new DateOnly(2026, 2, 28)) });

        Assert.Null(first);
    }

    [Fact]
    public void WindowEntirelyAfterMonth_ContributesNothing_ReturnsNull()
    {
        var first = FirstEmployedDayInMonth(
            new[] { new EmploymentWindow(new DateOnly(2026, 5, 10), null) });

        Assert.Null(first);
    }

    /// <summary>
    /// The spells-proof union: two spells in one month (a re-hire) — the earliest clipped start
    /// wins regardless of list order, and a non-contributing spell is ignored.
    /// </summary>
    [Fact]
    public void MultipleWindows_UnionTakesEarliestContributingStart_RegardlessOfOrder()
    {
        var windows = new[]
        {
            new EmploymentWindow(new DateOnly(2026, 3, 20), null),                     // second spell
            new EmploymentWindow(new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 31)),  // ended before the month
            new EmploymentWindow(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 10)),  // first spell
        };

        var first = FirstEmployedDayInMonth(windows);

        Assert.Equal(new DateOnly(2026, 3, 5), first);
    }

    [Fact]
    public void SingleDaySpellInsideMonth_ThatDay()
    {
        var day = new DateOnly(2026, 3, 12);
        var first = FirstEmployedDayInMonth(new[] { new EmploymentWindow(day, day) });

        Assert.Equal(day, first);
    }
}
