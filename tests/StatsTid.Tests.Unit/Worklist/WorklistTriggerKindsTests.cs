using StatsTid.Infrastructure;

namespace StatsTid.Tests.Unit.Worklist;

/// <summary>
/// S148 / TASK-14823-T (AC-C2h, the Unit half; the page's Danish label is TASK-14824's vitest) - the worklist knows
/// the reason "a configuration changed" (<c>CONFIG_PUBLISHED</c>) and that reason never stops HR recalculating.
///
/// <para>
/// <b>In plain language.</b> A configuration saved with a past date puts paid months on HR's worklist under a new
/// reason. The worklist must accept that reason (an unknown reason is refused), and it must not tell HR that the
/// month cannot be recalculated: a configuration change touches no wage-type key, so nothing blocks. The one
/// reason that can block, an agreement-code change in the middle of a week the month settles, is used as the
/// control so the "no block" answer is shown to be a choice and not the absence of any blocking rule.
/// </para>
///
/// <para>
/// The kind is written as a literal, never through <c>WorklistTriggerKinds.ConfigPublished</c>, so a wrong constant
/// fails the fact and the fact compiles against code that lacks the constant.
/// </para>
/// </summary>
public sealed class WorklistTriggerKindsTests
{
    /// <summary>
    /// <b>F (Unit).</b> <c>CONFIG_PUBLISHED</c> is a known trigger kind and derives no recalculation block, on
    /// Wednesday 3 June 2026 (inside a week June settles), where the control kind <c>AGREEMENT_CODE_CHANGE</c>
    /// answers <c>QUAL-209</c>.
    /// <para><b>Red conditions.</b> Before TASK-14823 <c>IsKnown("CONFIG_PUBLISHED")</c> returns false, so the first
    /// assertion fails. The null answer alone also holds for any unknown kind; the first assertion is what makes it
    /// mean "known and never blocks", and the control shows the date can block.</para>
    /// </summary>
    [Fact]
    public void ConfigPublished_KnownAndDerivesNoBlock()
    {
        Assert.True(WorklistTriggerKinds.IsKnown("CONFIG_PUBLISHED"));

        var wednesdayInsideJune = new DateOnly(2026, 6, 3);
        Assert.Equal(DayOfWeek.Wednesday, wednesdayInsideJune.DayOfWeek);
        Assert.Equal("QUAL-209",
            BackdateWorklistDerivation.RecalcBlockedByForTrigger("AGREEMENT_CODE_CHANGE", wednesdayInsideJune, 2026, 6));
        Assert.Null(BackdateWorklistDerivation.RecalcBlockedByForTrigger("CONFIG_PUBLISHED", wednesdayInsideJune, 2026, 6));
    }
}
