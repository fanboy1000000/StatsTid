using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Segmentation;

/// <summary>
/// Negative tests for the geometric invariants enforced by
/// <see cref="PlannedCalculation"/>'s internal constructor (ADR-016 D9). The unit-test
/// project has <c>InternalsVisibleTo</c> access to <c>StatsTid.SharedKernel</c> so it
/// can construct deliberately-bad <see cref="PlannedCalculation"/> instances directly,
/// proving each invariant throws <see cref="PlannerInvariantViolation"/> with a
/// structured message that names the violated invariant.
///
/// <para>Four invariants under test:</para>
/// <list type="number">
///   <item>Empty <see cref="PlannedCalculation.Segments"/> list</item>
///   <item>Non-contiguous segments (gap between successive segments)</item>
///   <item>Coverage shortfall — first segment doesn't start at PeriodStart, OR last
///         segment doesn't end at PeriodEnd</item>
///   <item>Overlapping segments — segment N+1 starts on or before segment N's end</item>
/// </list>
/// </summary>
public sealed class PlannedCalculationInvariantTests
{
    private static readonly DateOnly PeriodStart = new(2026, 3, 25);
    private static readonly DateOnly PeriodEnd = new(2026, 4, 7);

    private static PlannedSegment Segment(DateOnly start, DateOnly end) =>
        new(start, end, BoundaryCause.OkTransition, Snapshot: null);

    /// <summary>
    /// D9 invariant #1: an empty segment list is rejected. The ctor message must name
    /// "at least one segment" so test failures pinpoint the violated geometry.
    /// </summary>
    [Fact]
    public void EmptySegments_ThrowsPlannerInvariantViolation()
    {
        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            new PlannedCalculation(
                manifestId: Guid.NewGuid(),
                employeeId: "EMP-INV-1",
                periodStart: PeriodStart,
                periodEnd: PeriodEnd,
                segments: Array.Empty<PlannedSegment>(),
                calculationKind: "forward-calc"));

        Assert.Contains("at least one segment", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// D9 invariant #2: segments must be contiguous — segment 1 ends 2026-04-15 and
    /// segment 2 starts 2026-04-17 leaves a gap on 2026-04-16. Ctor must throw and the
    /// message must name the offending pair indices and dates.
    /// </summary>
    [Fact]
    public void NonContiguousSegments_ThrowsPlannerInvariantViolation()
    {
        var seg1 = Segment(PeriodStart, new DateOnly(2026, 4, 15));
        // Skip 2026-04-16 — gap.
        var seg2 = Segment(new DateOnly(2026, 4, 17), PeriodEnd);

        // Adjust the period to span both segments so coverage checks the contiguity check.
        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            new PlannedCalculation(
                manifestId: Guid.NewGuid(),
                employeeId: "EMP-INV-2",
                periodStart: PeriodStart,
                periodEnd: PeriodEnd,
                segments: new[] { seg1, seg2 },
                calculationKind: "forward-calc"));

        Assert.Contains("not contiguous", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// D9 invariant #3: segments must cover the period EXACTLY. Two distinct
    /// shortfalls are exercised in this single test:
    /// (a) first segment doesn't start at PeriodStart (starts one day late);
    /// (b) last segment doesn't end at PeriodEnd (ends one day early).
    /// </summary>
    [Fact]
    public void CoverageShortfall_FirstAndLastEdges_ThrowPlannerInvariantViolation()
    {
        // Case (a): first segment starts AFTER PeriodStart.
        var lateStart = Segment(PeriodStart.AddDays(1), PeriodEnd);
        var exA = Assert.Throws<PlannerInvariantViolation>(() =>
            new PlannedCalculation(
                manifestId: Guid.NewGuid(),
                employeeId: "EMP-INV-3A",
                periodStart: PeriodStart,
                periodEnd: PeriodEnd,
                segments: new[] { lateStart },
                calculationKind: "forward-calc"));

        Assert.Contains("First segment StartDate", exA.Message, StringComparison.OrdinalIgnoreCase);

        // Case (b): last segment ends BEFORE PeriodEnd.
        var earlyEnd = Segment(PeriodStart, PeriodEnd.AddDays(-1));
        var exB = Assert.Throws<PlannerInvariantViolation>(() =>
            new PlannedCalculation(
                manifestId: Guid.NewGuid(),
                employeeId: "EMP-INV-3B",
                periodStart: PeriodStart,
                periodEnd: PeriodEnd,
                segments: new[] { earlyEnd },
                calculationKind: "forward-calc"));

        Assert.Contains("Last segment EndDate", exB.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// D9 invariant #4: segments must NOT overlap. Segment 2's start is on the
    /// SAME day as segment 1's end — overlap by one day. Ctor's contiguity check
    /// uses <c>next.StartDate != current.EndDate.AddDays(1)</c>; the same predicate
    /// catches overlaps. Production emits the same "not contiguous" message for both
    /// gap and overlap — they share the contiguity check; the test name is descriptive
    /// of the input shape, not of a distinct production code path.
    /// </summary>
    [Fact]
    public void OverlappingSegments_ThrowsPlannerInvariantViolation()
    {
        var seg1 = Segment(PeriodStart, new DateOnly(2026, 3, 31));
        // seg2 starts on 2026-03-31 (same day seg1 ends) — overlap.
        var seg2 = Segment(new DateOnly(2026, 3, 31), PeriodEnd);

        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            new PlannedCalculation(
                manifestId: Guid.NewGuid(),
                employeeId: "EMP-INV-4",
                periodStart: PeriodStart,
                periodEnd: PeriodEnd,
                segments: new[] { seg1, seg2 },
                calculationKind: "forward-calc"));

        Assert.Contains("not contiguous", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
