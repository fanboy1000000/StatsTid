using StatsTid.Infrastructure;

namespace StatsTid.Tests.Unit.Infrastructure;

/// <summary>
/// S147 / TASK-14725-T (Step 7a W-1; owner ruling 2026-11-05 "(a) Fix it now") — the pure rule behind the shared
/// approver predicate's manager-grace check, <c>DesignatedApproverAuthorizer.IsPastManagerGrace(endDate, asOf)</c>.
///
/// <para><b>Plain language.</b> A leaver's manager may act for 5 calendar days after the last day employed (D). On day
/// D+6 and after, only HR may. The rule answers "has the grace ended?" from the end date and the date asked about,
/// never from whether the leaver job has switched the person off. Expected values come from the owner's OQ-2 ruling
/// (D+1 .. D+5 are grace, D+6 is not), not from the code.</para>
///
/// <para><b>Kind N</b> for every fact: each names <c>IsPastManagerGrace</c>, a member this task creates. RED is
/// proven by ONE named mutation at the gate, never by reverting: <b>M-26</b>, <c>IsPastManagerGrace</c> returns
/// <c>false</c> (API kept, behaviour broken). The D+5 and "no end date" facts are also boundary pins that hold under
/// M-26 by design (they say "not past"); the D+6 facts carry the RED.</para>
/// </summary>
public class ManagerGracePredicateTests
{
    private static readonly DateOnly AsOf = new(2026, 6, 10); // a Wednesday

    /// <summary>Kind N. Red condition: the 6th day after the last day is past the grace. Under M-26
    /// <c>Assert.True</c> fails (the rule says false).</summary>
    [Fact]
    public void LastDaySixDaysBack_Day6_IsPastGrace()
        => Assert.True(DesignatedApproverAuthorizer.IsPastManagerGrace(AsOf.AddDays(-6), AsOf));

    /// <summary>Kind N and boundary pin. The 5th day after the last day is the LAST grace day: not past.
    /// Red condition: an off-by-one (<c>&lt;=</c> instead of <c>&lt;</c> on the cutoff) makes this true.</summary>
    [Fact]
    public void LastDayFiveDaysBack_Day5_IsNotPastGrace()
        => Assert.False(DesignatedApproverAuthorizer.IsPastManagerGrace(AsOf.AddDays(-5), AsOf));

    /// <summary>Kind N. A long-gone leaver (a year back) is past the grace. Under M-26 <c>Assert.True</c> fails.</summary>
    [Fact]
    public void LastDayAYearBack_IsPastGrace()
        => Assert.True(DesignatedApproverAuthorizer.IsPastManagerGrace(AsOf.AddYears(-1), AsOf));

    /// <summary>Kind N and boundary pin. The last day itself (today) and yesterday are inside the grace. Red
    /// condition: counting the last day as departed would make either true.</summary>
    [Fact]
    public void LastDayTodayAndYesterday_AreNotPastGrace()
    {
        Assert.False(DesignatedApproverAuthorizer.IsPastManagerGrace(AsOf, AsOf));
        Assert.False(DesignatedApproverAuthorizer.IsPastManagerGrace(AsOf.AddDays(-1), AsOf));
    }

    /// <summary>Kind N and boundary pin. No recorded last day (open-ended employment) is never past the grace. Red
    /// condition: treating a null end date as departed makes this true.</summary>
    [Fact]
    public void NoEndDate_IsNotPastGrace()
        => Assert.False(DesignatedApproverAuthorizer.IsPastManagerGrace(null, AsOf));

    /// <summary>Kind N and boundary pin. A last day still in the future is not past the grace. Red condition: a sign
    /// error in the cutoff (<c>today + 5</c>) makes the near-future date true.</summary>
    [Fact]
    public void FutureEndDate_IsNotPastGrace()
    {
        Assert.False(DesignatedApproverAuthorizer.IsPastManagerGrace(AsOf.AddDays(1), AsOf));
        Assert.False(DesignatedApproverAuthorizer.IsPastManagerGrace(AsOf.AddMonths(3), AsOf));
    }

    /// <summary>Kind N. The rule moves with the date asked about, not with the calendar: the same last day (2026-06-04)
    /// is inside the grace on 2026-06-09 (D+5) and past it on 2026-06-10 (D+6). Under M-26 the second assertion
    /// fails.</summary>
    [Fact]
    public void SameLastDay_FlipsFromNotPastToPast_BetweenDay5AndDay6()
    {
        var lastDay = new DateOnly(2026, 6, 4);
        Assert.False(DesignatedApproverAuthorizer.IsPastManagerGrace(lastDay, new DateOnly(2026, 6, 9)));
        Assert.True(DesignatedApproverAuthorizer.IsPastManagerGrace(lastDay, new DateOnly(2026, 6, 10)));
    }
}
