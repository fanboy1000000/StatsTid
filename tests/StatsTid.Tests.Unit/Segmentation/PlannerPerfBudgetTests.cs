using System.Diagnostics;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Segmentation;

/// <summary>
/// ADR-016 D8 perf-budget pin: the planner's happy path on a 1-employee 1-period call
/// is O(1) plus O(boundaries). The whole point of the always-invoke contract is that
/// the planner is cheap enough to run on every CalculateAsync, including non-straddling
/// periods. This test catches an order-of-magnitude regression — e.g. a future change
/// that accidentally made the planner allocate per-day rather than per-segment.
///
/// <para>
/// Thresholds are set so that an honest implementation passes by 1-2 orders of magnitude
/// on a developer laptop and on CI; they exist to catch *regressions*, not to prove
/// absolute performance. p99 is a hard ceiling because the dominant cause of a single
/// 50ms+ outlier is a bug (e.g. JIT thrash on a fresh assembly, GC pause from over-
/// allocation in a hot path).
/// </para>
///
/// <para>
/// Mitigation if flaky on a slow CI runner: bump the thresholds up by 2x — but
/// understand that the test is meant to detect order-of-magnitude regressions, so
/// any threshold change should be paired with a profiling investigation. Do NOT
/// skip; noisy CI is precisely when this test earns its keep.
/// </para>
/// </summary>
public sealed class PlannerPerfBudgetTests
{
    // 5 cell-test rule shapes (one per populated cell from PlannerCellTests). Realistic
    // ruleSet size for the production registry.
    private static readonly RuleClassification[] RuleSet =
    {
        new("SUPPLEMENT_CALC",       Span.Entry,       SplitBehavior.SegmentSafe,    Family.Calculation, MergeStrategy.Concatenate,             SnapshotContract: null),
        new("REST_PERIOD_MAX_DAILY", Span.Window,      SplitBehavior.SegmentSafe,    Family.Compliance,  MergeStrategy.UnionDedupe,             SnapshotContract: null),
        new("OVERTIME_CALC",         Span.Window,      SplitBehavior.AlignedWindow,  Family.Calculation, MergeStrategy.RejectIfMultipleSegments, SnapshotContract: null),
        new("NORM_CHECK_MULTIWEEK",  Span.Period,      SplitBehavior.Mergeable,      Family.Calculation, MergeStrategy.Custom,                  SnapshotContract: null),
        new("FLEX_BALANCE",          Span.CrossPeriod, SplitBehavior.Mergeable,      Family.Calculation, MergeStrategy.Custom,                  SnapshotContract: null),
    };

    /// <summary>
    /// Runs <see cref="PeriodPlanner.Plan"/> 100 times on a non-straddling 1-employee
    /// 1-period call (no boundaries -> single segment). Asserts:
    /// p50 ≤ 5 ms, p95 ≤ 20 ms, p99 ≤ 50 ms.
    /// </summary>
    [Fact]
    public void Plan_HappyPath_StaysWithinPerfBudget()
    {
        const int iterations = 100;
        var samples = new long[iterations];

        // Warm-up: JIT the path once so the first sample doesn't capture cold-start cost.
        // The test is about steady-state planner cost, not first-call cost.
        _ = PeriodPlanner.Plan(
            employeeId: "EMP-PERF-WARM",
            periodStart: new DateOnly(2026, 5, 1),
            periodEnd: new DateOnly(2026, 5, 31),
            calculationKind: "forward-calc",
            ruleSet: RuleSet,
            sources: BoundarySources.Empty,
            options: PlannerOptions.Default);

        var periodStart = new DateOnly(2026, 5, 1);
        var periodEnd = new DateOnly(2026, 5, 31);

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            _ = PeriodPlanner.Plan(
                employeeId: "EMP-PERF",
                periodStart: periodStart,
                periodEnd: periodEnd,
                calculationKind: "forward-calc",
                ruleSet: RuleSet,
                sources: BoundarySources.Empty,
                options: PlannerOptions.Default);
            sw.Stop();
            samples[i] = sw.ElapsedMilliseconds;
        }

        Array.Sort(samples);
        // Percentile-by-index — adequate for an order-of-magnitude regression catcher.
        // For 100 samples: index 49 = p50, 94 = p95, 98 = p99.
        var p50 = samples[49];
        var p95 = samples[94];
        var p99 = samples[98];

        Assert.True(p50 <= 5, $"Planner p50 over budget: {p50} ms (limit 5 ms). Order-of-magnitude regression suspected.");
        Assert.True(p95 <= 20, $"Planner p95 over budget: {p95} ms (limit 20 ms). Order-of-magnitude regression suspected.");
        Assert.True(p99 <= 50, $"Planner p99 over hard ceiling: {p99} ms (limit 50 ms). Investigate before relaxing.");
    }

    /// <summary>
    /// Boundary-path variant: OK-straddling sources + a non-null <see cref="SnapshotContract"/>
    /// on one rule. Exercises the productive paths the cold-fast variant cannot touch —
    /// boundary detection across <see cref="BoundaryDetector"/>, snapshot completeness
    /// validation, multi-segment construction. Looser ceilings than the cold-fast variant
    /// because there is real per-segment work; the order-of-magnitude regression catcher
    /// remains useful (post-S20 cleanup, addresses Phase 4 Reviewer WARNING 1).
    /// </summary>
    [Fact]
    public void Plan_OkStraddlingWithSnapshotContract_StaysWithinBoundaryBudget()
    {
        const int iterations = 100;
        var samples = new long[iterations];

        // One rule declares a SnapshotContract — the planner must validate snapshot
        // presence on intersecting segments. Combined with two segments from the OK
        // transition, this exercises the productive happy-path the cold-fast variant
        // cannot reach.
        var ruleSet = new RuleClassification[]
        {
            new("SUPPLEMENT_CALC",       Span.Entry,       SplitBehavior.SegmentSafe,    Family.Calculation, MergeStrategy.Concatenate,             SnapshotContract: null),
            new("REST_PERIOD_MAX_DAILY", Span.Window,      SplitBehavior.SegmentSafe,    Family.Compliance,  MergeStrategy.UnionDedupe,             SnapshotContract: null),
            new("NORM_CHECK_MULTIWEEK",  Span.Period,      SplitBehavior.Mergeable,      Family.Calculation, MergeStrategy.Custom,                  SnapshotContract: new("NORM_CHECK_MULTIWEEK", new[] { "EmployeeProfile.WeeklyNormHours" })),
            new("FLEX_BALANCE",          Span.CrossPeriod, SplitBehavior.Mergeable,      Family.Calculation, MergeStrategy.Custom,                  SnapshotContract: null),
        };

        // OK24 -> OK26 transition on 2026-04-01; period spans the boundary -> two segments.
        var sources = new BoundarySources(
            OkTransitions: new[] { (new DateOnly(2026, 4, 1), "OK24", "OK26") },
            AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
            PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
            EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
            NonDatedSourceValues: new Dictionary<string, object?>
            {
                ["EmployeeProfile.WeeklyNormHours"] = 37m,
            });

        var periodStart = new DateOnly(2026, 3, 25);
        var periodEnd = new DateOnly(2026, 4, 30);

        // Warm-up.
        _ = PeriodPlanner.Plan(
            employeeId: "EMP-PERF-BOUNDARY-WARM",
            periodStart: periodStart,
            periodEnd: periodEnd,
            calculationKind: "forward-calc",
            ruleSet: ruleSet,
            sources: sources,
            options: PlannerOptions.Default);

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            _ = PeriodPlanner.Plan(
                employeeId: "EMP-PERF-BOUNDARY",
                periodStart: periodStart,
                periodEnd: periodEnd,
                calculationKind: "forward-calc",
                ruleSet: ruleSet,
                sources: sources,
                options: PlannerOptions.Default);
            sw.Stop();
            samples[i] = sw.ElapsedMilliseconds;
        }

        Array.Sort(samples);
        var p50 = samples[49];
        var p95 = samples[94];
        var p99 = samples[98];

        // Looser ceilings than the cold-fast variant (real per-segment + snapshot work).
        Assert.True(p50 <= 10, $"Planner boundary-path p50 over budget: {p50} ms (limit 10 ms). Order-of-magnitude regression suspected.");
        Assert.True(p95 <= 40, $"Planner boundary-path p95 over budget: {p95} ms (limit 40 ms). Order-of-magnitude regression suspected.");
        Assert.True(p99 <= 100, $"Planner boundary-path p99 over hard ceiling: {p99} ms (limit 100 ms). Investigate before relaxing.");
    }
}
