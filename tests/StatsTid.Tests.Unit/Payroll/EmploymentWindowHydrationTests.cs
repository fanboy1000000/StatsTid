using System.Reflection;
using System.Text.Json;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S137 / TASK-13702 — the payroll host's planner HYDRATION of the employment window
/// (ADR-040 D5/D7): <c>PeriodCalculationService.BuildPlanForLegacyCallersAsync</c> reads the
/// window via <c>IEmploymentWindowResolver.GetWindowsAsync</c> and turns it into boundary
/// dates + segment typing. Locally runnable (no DB).
///
/// <para>
/// Plain-language: this is where the recurring inclusive/exclusive ("fencepost") hazard
/// lives. A leaver's <c>employment_end_date</c> is the LAST day employed, so the day payroll
/// stops is <c>end + 1</c>; a starter's <c>employment_start_date</c> is itself the first
/// paid day. Off by one either way silently drops or adds a paid day. These pins freeze the
/// correct arithmetic at the seam that performs it, plus the ADR-mandated tie-break (a hire
/// on 2026-04-01 — the OK24→OK26 date — records as the hire, not as the OK transition) and
/// the byte-parity promise for windowless / unbounded employees (no <c>employmentStatus</c>
/// key ever serializes).
/// </para>
///
/// <para>
/// The hydration method is private; it is invoked via reflection — the established idiom for
/// this exact seam (<c>ProfileBoundaryHydrationTests</c> in the Regression suite does the
/// same for the S21 local-profile hydration). Going through the public shim would also
/// exercise rule evaluation, which <see cref="EmploymentWindowSegmentSkipTests"/> covers.
/// </para>
/// </summary>
public sealed class EmploymentWindowHydrationTests
{
    private const string EmployeeId = "EMP-S137-HYDRATE";

    // The OK24→OK26 transition date — the ADR-040 D5 tie-break marquee.
    private static readonly DateOnly OkTransitionDate = new(2026, 4, 1);

    // S137 Step-5a (Reviewer WARNING 3): THE REAL PeriodCalculationService.JsonOptions — the
    // segments_jsonb writer — via the shared reflection accessor. No replica to keep in sync.
    private static JsonSerializerOptions PcsWriter => PcsJsonOptions.Real;

    // ═════════════════════════════════════════════════════════════════════
    // Fenceposts (ADR-040 D1) at the hydration seam
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>THE leaver fencepost: end = 2026-03-15 (inclusive, last day employed) →
    /// the EmploymentEnded boundary is 03-16, so segment 0 is [03-01 .. 03-15] EMPLOYED and
    /// the NOT_EMPLOYED suffix starts 03-16. Hydrating <c>End</c> itself would cut 03-15 —
    /// the last PAID day — out of the employed segment.</summary>
    [Fact]
    public async Task Leaver_EndInclusive_NotEmployedSuffixStartsAtEndPlusOne()
    {
        var lastDay = new DateOnly(2026, 3, 15);
        var resolver = new FakeWindowResolver(new EmploymentWindow(null, lastDay));

        var plan = await BuildPlanAsync(resolver, Mar01, Mar31);

        Assert.Equal(2, plan.Segments.Count);

        Assert.Equal(Mar01, plan.Segments[0].StartDate);
        Assert.Equal(lastDay, plan.Segments[0].EndDate);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[0].EmploymentStatus);

        Assert.Equal(lastDay.AddDays(1), plan.Segments[1].StartDate);
        Assert.Equal(Mar31, plan.Segments[1].EndDate);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[1].EmploymentStatus);
        Assert.Equal(BoundaryCause.EmploymentEnded, plan.Segments[1].BoundaryCause);
    }

    /// <summary>The starter fencepost: start = 2026-03-10 is ITSELF the boundary (the first
    /// paid day); the pre-hire NOT_EMPLOYED prefix is [03-01 .. 03-09].</summary>
    [Fact]
    public async Task Starter_PreHirePrefixEndsAtStartMinusOne()
    {
        var hire = new DateOnly(2026, 3, 10);
        var resolver = new FakeWindowResolver(new EmploymentWindow(hire, null));

        var plan = await BuildPlanAsync(resolver, Mar01, Mar31);

        Assert.Equal(2, plan.Segments.Count);

        Assert.Equal(Mar01, plan.Segments[0].StartDate);
        Assert.Equal(hire.AddDays(-1), plan.Segments[0].EndDate);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[0].EmploymentStatus);

        Assert.Equal(hire, plan.Segments[1].StartDate);
        Assert.Equal(Mar31, plan.Segments[1].EndDate);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[1].EmploymentStatus);
        Assert.Equal(BoundaryCause.EmploymentStarted, plan.Segments[1].BoundaryCause);
    }

    /// <summary>Hire AND leave inside one period: both fenceposts at once, three typed
    /// segments — NOT_EMPLOYED [03-01..03-09] / EMPLOYED [03-10..03-20] / NOT_EMPLOYED
    /// [03-21..03-31].</summary>
    [Fact]
    public async Task HireAndLeaveInsidePeriod_ThreeTypedSegments_BothFenceposts()
    {
        var hire = new DateOnly(2026, 3, 10);
        var lastDay = new DateOnly(2026, 3, 20);
        var resolver = new FakeWindowResolver(new EmploymentWindow(hire, lastDay));

        var plan = await BuildPlanAsync(resolver, Mar01, Mar31);

        Assert.Equal(3, plan.Segments.Count);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[0].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[1].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[2].EmploymentStatus);
        Assert.Equal(hire, plan.Segments[1].StartDate);
        Assert.Equal(lastDay, plan.Segments[1].EndDate);

        // ADR-016 planner convention (PeriodPlanner.Plan step 2+3): a segment's BoundaryCause
        // names the boundary that ENDS it (segment 0 and every middle segment); only the FINAL
        // segment names the boundary that started it. So the pre-hire prefix is attributed to
        // the hire, the employed middle span to the leave, and the suffix to the leave.
        Assert.Equal(BoundaryCause.EmploymentStarted, plan.Segments[0].BoundaryCause);
        Assert.Equal(BoundaryCause.EmploymentEnded, plan.Segments[1].BoundaryCause);
        Assert.Equal(BoundaryCause.EmploymentEnded, plan.Segments[2].BoundaryCause);
    }

    /// <summary>The ADR-040 D5 tie-break AC through the REAL hydration path: a hire on
    /// 2026-04-01 coincides with the OK24→OK26 transition the shim also hydrates. The
    /// boundary must record as EmploymentStarted — non-employment is the strongest fact
    /// about a date — not as OkTransition.</summary>
    [Fact]
    public async Task HireOnOkTransitionDate_RecordsEmploymentStarted_NotOkTransition()
    {
        var resolver = new FakeWindowResolver(new EmploymentWindow(OkTransitionDate, null));

        var plan = await BuildPlanAsync(resolver, new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 15));

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(OkTransitionDate, plan.Segments[1].StartDate);
        Assert.Equal(BoundaryCause.EmploymentStarted, plan.Segments[1].BoundaryCause);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[0].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[1].EmploymentStatus);
        // The OK transition is still IN the manifest's cause summary vocabulary only via the
        // segment it would have introduced; the planner's convention names the winning cause.
        Assert.DoesNotContain(plan.Segments, s => s.BoundaryCause == BoundaryCause.OkTransition
            && s.StartDate == OkTransitionDate);
    }

    // ═════════════════════════════════════════════════════════════════════
    // The read contract (D7) and the empty-vs-null distinction
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>D7: the window is read server-side, once, for exactly the calculation period
    /// — the resolver is asked (employeeId, periodStart, periodEnd).</summary>
    [Fact]
    public async Task Hydration_AsksTheWindowResolverOnceForExactlyThePeriod()
    {
        var resolver = new FakeWindowResolver(new EmploymentWindow(null, null));

        _ = await BuildPlanAsync(resolver, Mar01, Mar31);

        Assert.Equal((EmployeeId, Mar01, Mar31), Assert.Single(resolver.Calls));
    }

    /// <summary>An EMPTY list from the resolver ("window known; no employed day in the
    /// period" — e.g. a leaver whose end date precedes the period) is NOT "no information":
    /// the whole plan types NOT_EMPLOYED.</summary>
    [Fact]
    public async Task EmptyWindowsFromResolver_WholePeriodNotEmployed()
    {
        var resolver = new FakeWindowResolver(); // no windows overlap the period

        var plan = await BuildPlanAsync(resolver, Mar01, Mar31);

        var segment = Assert.Single(plan.Segments);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, segment.EmploymentStatus);
        Assert.Equal(Mar01, segment.StartDate);
        Assert.Equal(Mar31, segment.EndDate);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Byte-parity: windowless + unbounded employees serialize exactly as before
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>A both-NULL window (every pre-S136 employee, ADR-040 D2) is ONE unbounded
    /// entry → a single EMPLOYED segment whose serialized form (the segments_jsonb writer)
    /// carries NO employmentStatus key — byte-identical to pre-S137 output.</summary>
    [Fact]
    public async Task UnboundedWindow_SingleEmployedSegment_SerializesWithoutEmploymentStatusKey()
    {
        var resolver = new FakeWindowResolver(new EmploymentWindow(null, null));

        var plan = await BuildPlanAsync(resolver, Mar01, Mar31);

        var segment = Assert.Single(plan.Segments);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, segment.EmploymentStatus);
        Assert.DoesNotContain("employmentStatus", JsonSerializer.Serialize(plan.Segments, PcsWriter));
    }

    /// <summary>The legacy null-resolver fixture path (no S137 dependencies wired) is
    /// byte-identical to before this change: single EMPLOYED segment, no key serialized.</summary>
    [Fact]
    public async Task NullResolver_LegacyPath_SingleEmployedSegment_NoEmploymentStatusKey()
    {
        var plan = await BuildPlanAsync(windowResolver: null, Mar01, Mar31);

        var segment = Assert.Single(plan.Segments);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, segment.EmploymentStatus);
        Assert.Equal(BoundaryCause.OkTransition, segment.BoundaryCause); // the no-boundary sentinel, unchanged
        Assert.DoesNotContain("employmentStatus", JsonSerializer.Serialize(plan.Segments, PcsWriter));
    }

    /// <summary>...and a typed NOT_EMPLOYED segment DOES carry the key under the same
    /// writer — the parity is selective, not accidental omission.</summary>
    [Fact]
    public async Task NotEmployedSegment_SerializesWithEmploymentStatusKey_EmployedSiblingDoesNot()
    {
        var lastDay = new DateOnly(2026, 3, 15);
        var resolver = new FakeWindowResolver(new EmploymentWindow(null, lastDay));

        var plan = await BuildPlanAsync(resolver, Mar01, Mar31);

        var employedJson = JsonSerializer.Serialize(plan.Segments[0], PcsWriter);
        var notEmployedJson = JsonSerializer.Serialize(plan.Segments[1], PcsWriter);
        Assert.DoesNotContain("employmentStatus", employedJson);
        Assert.Contains("\"employmentStatus\":\"NOT_EMPLOYED\"", notEmployedJson);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static async Task<PlannedCalculation> BuildPlanAsync(
        FakeWindowResolver? windowResolver, DateOnly periodStart, DateOnly periodEnd)
    {
        var pcs = BuildPcs(new RecordingRuleEngine(), windowResolver: windowResolver);

        var method = typeof(PeriodCalculationService).GetMethod(
            "BuildPlanForLegacyCallersAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<PlannedCalculation>)method!.Invoke(
            pcs, new object[] { Profile(EmployeeId), periodStart, periodEnd, CancellationToken.None })!;
        return await task;
    }
}
