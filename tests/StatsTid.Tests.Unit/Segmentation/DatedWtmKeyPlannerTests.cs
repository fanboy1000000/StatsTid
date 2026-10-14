using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Segmentation;

/// <summary>
/// S145 / TASK-14514 — QUAL-150's planner half (refinement A4 :310-313; Stage A criterion "Dated key",
/// :630-634): the planner fills each segment's wage-type natural key (<c>WtmNaturalKey</c>: OK version,
/// agreement, position) from the employee's DATED histories at that segment's own start, not once per plan from
/// the request body.
///
/// <para>
/// <b>Plain language.</b> The payroll export looks up each wage code by (OK version, agreement, position) as they
/// were when a segment began. Until now every segment of a plan carried one shared snapshot hydrated from the
/// request's profile, so a stale request position would be paid under for every segment, and a split month's
/// second segment would carry the first segment's key. The planner stays pure (it does no I/O): the histories are
/// DATA handed in through the enrollment's hydrator, which now also receives the segment start.
/// </para>
///
/// <para>
/// <b>Fixture.</b> A straddle-safe synthetic rule set (every rule SegmentSafe/Concatenate, as
/// <c>TestFixtures.StraddleSafeRuleSet</c>), so a two-segment plan is plannable; the live AlignedWindow set would
/// refuse it. The OK24 to OK26 transition on 1 April 2026 splits March-April into two segments. The synthetic
/// dated history: position CLERK before 1 April, SENIOR from 1 April; the request profile carries a stale
/// position (STALE) that neither segment's key may use.
/// </para>
///
/// <para>
/// <b>Red conditions</b> (names a hydrator shape TASK-14512 creates, so RED by MUTATION at the A.4 gate, never by
/// reverting; <c>docs/AGENTS.md</c> § "RED evidence"): MU-25 (the key is hydrated once per plan, in the planner:
/// both segments take the first segment's key) trips the second segment's assertion (expected OK26 / SENIOR).
/// </para>
/// </summary>
public sealed class DatedWtmKeyPlannerTests
{
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Apr01 = new(2026, 4, 1);
    private static readonly DateOnly Apr30 = new(2026, 4, 30);

    private static readonly RuleClassification[] StraddleSafeRuleSet =
    {
        new("SUPPLEMENT_CALC", Span.Entry,       SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategy.Concatenate, SnapshotContract: null),
        new("ON_CALL_DUTY",    Span.Entry,       SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategy.Concatenate, SnapshotContract: null),
        new("OVERTIME_CALC",   Span.Entry,       SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategy.Concatenate, SnapshotContract: null),
        new("NORM_CHECK_37H",  Span.Period,      SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategy.Concatenate, SnapshotContract: null),
        new("FLEX_BALANCE",    Span.CrossPeriod, SplitBehavior.Mergeable,   Family.Calculation, MergeStrategy.Custom,      SnapshotContract: null),
    };

    private static BoundarySources OkStraddle() => new(
        OkTransitions: new List<(DateOnly, string, string)> { (Apr01, "OK24", "OK26") },
        AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
        PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
        EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
        NonDatedSourceValues: new Dictionary<string, object?>());

    /// <summary>
    /// Two segments (1-31 March under OK24; 1-30 April under OK26), each carrying the key of ITS OWN start:
    /// segment 0 (OK24, HK, CLERK), segment 1 (OK26, HK, SENIOR). The request profile's position STALE appears in
    /// neither.
    /// </summary>
    [Fact]
    public void Plan_TwoSegments_EachCarriesOwnDatedWtmKey()
    {
        var enrollment = new PlannerEnrollment();
        // The synthetic dated history: what the histories say AT the segment start. The hydrator receives the
        // segment start (S145 TASK-14512 Do 5); the profile it also receives is the request's, stale on purpose.
        enrollment.RegisterSnapshotContract("WtmNaturalKey", (EmploymentProfile profile, DateOnly segmentStart) =>
            new WtmNaturalKey(
                OkVersion: segmentStart < Apr01 ? "OK24" : "OK26",
                AgreementCode: profile.AgreementCode,
                Position: segmentStart < Apr01 ? "CLERK" : "SENIOR"));

        var stale = new EmploymentProfile
        {
            EmployeeId = "EMP-S145-DATEDKEY",
            AgreementCode = "HK",
            OkVersion = "OK24",
            EmploymentCategory = "Standard",
            PartTimeFraction = 1.0m,
            Position = "STALE",
        };

        var plan = PeriodPlanner.Plan(
            employeeId: stale.EmployeeId,
            periodStart: Mar01,
            periodEnd: Apr30,
            calculationKind: "forward-calc",
            ruleSet: StraddleSafeRuleSet,
            sources: OkStraddle(),
            options: PlannerOptions.Default,
            enrollment: enrollment,
            profile: stale);

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(Mar01, plan.Segments[0].StartDate);
        Assert.Equal(Apr01, plan.Segments[1].StartDate);

        var key0 = Assert.IsType<WtmNaturalKey>(plan.Segments[0].Snapshot!.Values["WtmNaturalKey"]);
        var key1 = Assert.IsType<WtmNaturalKey>(plan.Segments[1].Snapshot!.Values["WtmNaturalKey"]);

        Assert.Equal(new WtmNaturalKey("OK24", "HK", "CLERK"), key0);
        Assert.Equal(new WtmNaturalKey("OK26", "HK", "SENIOR"), key1);
    }
}
