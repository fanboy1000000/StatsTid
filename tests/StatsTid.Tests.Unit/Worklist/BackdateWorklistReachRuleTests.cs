using StatsTid.Infrastructure;

namespace StatsTid.Tests.Unit.Worklist;

/// <summary>
/// S148 / TASK-14823-T fix-up 1 (R3) - the two pure functions that decide which days a configuration write reaches.
///
/// <para>
/// <b>In plain language.</b> A configuration is saved for one OK version (OK24 governs until 31 March 2026, OK26 from
/// 1 April 2026). A write dated 5 January 2026 for OK26 changes nothing before April; for OK24 it changes nothing from
/// April. These facts pin that, and that an employee whose agreement began "since always" is clipped to that reach
/// instead of listing every day since year 1. Every date is a literal, never read from the resolver, so a wrong
/// resolver table or a wrong branch fails the fact.
/// </para>
/// </summary>
public sealed class BackdateWorklistReachRuleTests
{
    private static readonly DateOnly Jan5 = new(2026, 1, 5);
    private static readonly DateOnly Apr1 = new(2026, 4, 1);

    /// <summary>
    /// <b>F (Unit).</b> An OK26 write dated 5 January 2026 reaches <c>[1 April 2026, open)</c>.
    /// <para><b>Red conditions.</b> The "version does not govern on the date, so search the next change day" branch
    /// (<c>if (from is null)</c> loop) made to <c>return null</c> (local mutation) answers null: the equality fails.</para>
    /// </summary>
    [Fact]
    public void OkVersionReach_Ok26DatedBeforeItsStart_StartsAtItsFirstDay_OpenEnded()
    {
        var reach = BackdateWorklistDerivation.OkVersionReach("OK26", Jan5);
        Assert.Equal((Apr1, (DateOnly?)null), reach);
    }

    /// <summary>
    /// <b>F (Unit).</b> An OK24 write dated 5 January 2026 reaches <c>[5 January 2026, 1 April 2026)</c>.
    /// <para><b>Red conditions.</b> The closing loop's bounded return <c>return (reachFrom, day)</c> made
    /// <c>return (reachFrom, null)</c> (local mutation) answers an open end: the equality fails.</para>
    /// </summary>
    [Fact]
    public void OkVersionReach_Ok24_EndsWhereTheNextVersionBegins()
    {
        var reach = BackdateWorklistDerivation.OkVersionReach("OK24", Jan5);
        Assert.Equal((Jan5, (DateOnly?)Apr1), reach);
    }

    /// <summary>
    /// <b>F (Unit).</b> A version string the resolver never returns reaches nothing (null).
    /// <para><b>Red conditions.</b> The final <c>if (from is not { } reachFrom) return null</c> made to return
    /// <c>(appliesFrom, null)</c> (local mutation) answers a reach: <c>Assert.Null</c> fails.</para>
    /// </summary>
    [Fact]
    public void OkVersionReach_UnknownVersion_IsNull()
    {
        Assert.Null(BackdateWorklistDerivation.OkVersionReach("OK99", Jan5));
    }

    /// <summary>
    /// <b>F (Unit).</b> An agreement-code row that began since always (<see cref="DateOnly.MinValue"/>) and a
    /// second-dimension row ending 1 June 2026 (inside the reach <c>[1 April 2026, open)</c>) match
    /// <c>[1 April 2026, 1 June 2026)</c>: the start is clipped to the reach, the end to the second row.
    /// <para><b>Red conditions.</b> <c>Intersect</c>'s upper bound for a null first bound
    /// (<c>a.ToExclusive is not { } aTo ? b.ToExclusive</c>) made to <c>? null</c> (local mutation) drops the second
    /// row's end and answers an open interval: the equality fails.</para>
    /// </summary>
    [Fact]
    public void MatchingIntervals_SinceAlwaysAgreementRow_ClippedToReachAndSecondDimensionEnd()
    {
        var jun1 = new DateOnly(2026, 6, 1);
        var intervals = BackdateWorklistDerivation.MatchingIntervals(
            (Apr1, null),
            new[] { (DateOnly.MinValue, (DateOnly?)null) },
            new[] { (new DateOnly(2026, 1, 1), (DateOnly?)jun1) });

        Assert.Equal(new[] { (Apr1, (DateOnly?)jun1) }, intervals);
    }
}
