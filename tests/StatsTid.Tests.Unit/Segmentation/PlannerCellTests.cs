using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Segmentation;

/// <summary>
/// Cell coverage for the ADR-016 D11 Rule Classification Inventory: one test per
/// populated <c>(span, split-behavior)</c> pair encountered in the registry. These
/// pin the planner's behaviour for each shape so that adding a new rule of an
/// existing shape does not require its own targeted test, and so that the
/// (span × split-behavior) matrix is never silently regressed by a planner change.
///
/// <para>Cell list (5 distinct populated cells today):</para>
/// <list type="number">
///   <item>(entry, segment-safe, calculation) — e.g. SupplementRule</item>
///   <item>(window, segment-safe, compliance) — e.g. RestPeriodRule.MAX_DAILY_HOURS</item>
///   <item>(window, aligned-window, calculation) — e.g. OvertimeRule</item>
///   <item>(period, mergeable, calculation) — e.g. NormCheckRule.MULTI_WEEK</item>
///   <item>(cross-period, mergeable, calculation) — e.g. FlexBalanceRule</item>
/// </list>
/// </summary>
public sealed class PlannerCellTests
{
    // OK24 -> OK26 happens on 2026-04-01. A 2-segment plan straddles this date.
    private static readonly DateOnly StraddleStart = new(2026, 3, 25);
    private static readonly DateOnly StraddleEnd = new(2026, 4, 7);

    private static BoundarySources OkStraddleSources() => new(
        OkTransitions: new List<(DateOnly, string, string)>
        {
            (new DateOnly(2026, 4, 1), "OK24", "OK26")
        },
        AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
        PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
        EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
        NonDatedSourceValues: new Dictionary<string, object?>());

    /// <summary>
    /// Cell 1 — (entry, segment-safe, calculation): SupplementRule shape. Default merge
    /// resolves to Concatenate per ADR-016 D3. The 2-segment OK-straddling plan must
    /// produce exactly 2 segments and the rule's classification must carry Concatenate.
    /// </summary>
    [Fact]
    public void Cell_EntrySegmentSafeCalculation_ProducesTwoSegmentsWithConcatenate()
    {
        var supplementClassification = new RuleClassification(
            RuleId: "SUPPLEMENT_CALC",
            Span: Span.Entry,
            SplitBehavior: SplitBehavior.SegmentSafe,
            Family: Family.Calculation,
            MergeStrategy: MergeStrategy.Concatenate,
            SnapshotContract: null);

        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-CELL-1",
            periodStart: StraddleStart,
            periodEnd: StraddleEnd,
            calculationKind: "forward-calc",
            ruleSet: new[] { supplementClassification },
            sources: OkStraddleSources(),
            options: PlannerOptions.Default);

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(StraddleStart, plan.Segments[0].StartDate);
        Assert.Equal(new DateOnly(2026, 3, 31), plan.Segments[0].EndDate);
        Assert.Equal(new DateOnly(2026, 4, 1), plan.Segments[1].StartDate);
        Assert.Equal(StraddleEnd, plan.Segments[1].EndDate);
        Assert.Same(MergeStrategy.Concatenate, supplementClassification.MergeStrategy);
        Assert.Equal(MergeStrategyKind.Concatenate, supplementClassification.MergeStrategy.Kind);
    }

    /// <summary>
    /// Cell 2 — (window, segment-safe, compliance): RestPeriodRule.MAX_DAILY_HOURS shape.
    /// Default merge resolves to UnionDedupe per ADR-016 D3 (compliance family).
    /// </summary>
    [Fact]
    public void Cell_WindowSegmentSafeCompliance_ProducesTwoSegmentsWithUnionDedupe()
    {
        var restPeriodClassification = new RuleClassification(
            RuleId: "REST_PERIOD_MAX_DAILY",
            Span: Span.Window,
            SplitBehavior: SplitBehavior.SegmentSafe,
            Family: Family.Compliance,
            MergeStrategy: MergeStrategy.UnionDedupe,
            SnapshotContract: null);

        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-CELL-2",
            periodStart: StraddleStart,
            periodEnd: StraddleEnd,
            calculationKind: "forward-calc",
            ruleSet: new[] { restPeriodClassification },
            sources: OkStraddleSources(),
            options: PlannerOptions.Default);

        Assert.Equal(2, plan.Segments.Count);
        Assert.Same(MergeStrategy.UnionDedupe, restPeriodClassification.MergeStrategy);
        Assert.Equal(MergeStrategyKind.UnionDedupe, restPeriodClassification.MergeStrategy.Kind);
    }

    /// <summary>
    /// Cell 3a — (window, aligned-window, calculation): OvertimeRule shape. When the
    /// period does NOT contain an interior boundary the planner produces a single
    /// segment and the aligned-window rule passes cleanly.
    /// </summary>
    [Fact]
    public void Cell_WindowAlignedWindow_NonStraddlingPeriod_SingleSegment()
    {
        var overtimeClassification = new RuleClassification(
            RuleId: "OVERTIME_CALC",
            Span: Span.Window,
            SplitBehavior: SplitBehavior.AlignedWindow,
            Family: Family.Calculation,
            MergeStrategy: MergeStrategy.RejectIfMultipleSegments,
            SnapshotContract: null);

        // Mon 2026-03-23 .. Sun 2026-03-29 — fully inside OK24, no interior boundary.
        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-CELL-3A",
            periodStart: new DateOnly(2026, 3, 23),
            periodEnd: new DateOnly(2026, 3, 29),
            calculationKind: "forward-calc",
            ruleSet: new[] { overtimeClassification },
            sources: BoundarySources.Empty,
            options: PlannerOptions.Default);

        Assert.Single(plan.Segments);
        Assert.Same(MergeStrategy.RejectIfMultipleSegments, overtimeClassification.MergeStrategy);
    }

    /// <summary>
    /// Cell 3b — (window, aligned-window, calculation): the same shape but with an
    /// interior OK boundary and AllowUpstreamAlignment=false (default) — the planner
    /// MUST throw <see cref="PlannerInvariantViolation"/> per ADR-016 D4. The error
    /// message names the offending rule id and the AlignedWindow contract.
    /// </summary>
    [Fact]
    public void Cell_WindowAlignedWindow_StraddlingPeriod_ThrowsWhenAlignmentDisallowed()
    {
        var overtimeClassification = new RuleClassification(
            RuleId: "OVERTIME_CALC",
            Span: Span.Window,
            SplitBehavior: SplitBehavior.AlignedWindow,
            Family: Family.Calculation,
            MergeStrategy: MergeStrategy.RejectIfMultipleSegments,
            SnapshotContract: null);

        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(
                employeeId: "EMP-CELL-3B",
                periodStart: StraddleStart,
                periodEnd: StraddleEnd,
                calculationKind: "forward-calc",
                ruleSet: new[] { overtimeClassification },
                sources: OkStraddleSources(),
                options: PlannerOptions.Default));

        Assert.Contains("OVERTIME_CALC", ex.Message);
        Assert.Contains("AlignedWindow", ex.Message);
        Assert.Contains("AllowUpstreamAlignment", ex.Message);
    }

    /// <summary>
    /// Cell 4 — (period, mergeable, calculation): NormCheckRule.MULTI_WEEK shape.
    /// Custom merge strategy (per-rule delegate, ADR-016 D3). The 2-segment plan must
    /// succeed and the classification must carry a non-null MergeStrategy.
    /// </summary>
    [Fact]
    public void Cell_PeriodMergeableCalculation_ProducesTwoSegmentsWithCustom()
    {
        var multiWeekNormClassification = new RuleClassification(
            RuleId: "NORM_CHECK_MULTIWEEK",
            Span: Span.Period,
            SplitBehavior: SplitBehavior.Mergeable,
            Family: Family.Calculation,
            MergeStrategy: MergeStrategy.Custom,
            SnapshotContract: null);

        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-CELL-4",
            periodStart: StraddleStart,
            periodEnd: StraddleEnd,
            calculationKind: "forward-calc",
            ruleSet: new[] { multiWeekNormClassification },
            sources: OkStraddleSources(),
            options: PlannerOptions.Default);

        Assert.Equal(2, plan.Segments.Count);
        Assert.NotNull(multiWeekNormClassification.MergeStrategy);
        Assert.Equal(MergeStrategyKind.Custom, multiWeekNormClassification.MergeStrategy.Kind);
    }

    /// <summary>
    /// Cell 5 — (cross-period, mergeable, calculation): FlexBalanceRule shape. The
    /// chained-carry semantics across segments require Custom (per-rule delegate)
    /// per ADR-016 D3. Plan must succeed for the 2-segment OK-straddling case.
    /// </summary>
    [Fact]
    public void Cell_CrossPeriodMergeableCalculation_ProducesTwoSegmentsWithCustom()
    {
        var flexBalanceClassification = new RuleClassification(
            RuleId: "FLEX_BALANCE",
            Span: Span.CrossPeriod,
            SplitBehavior: SplitBehavior.Mergeable,
            Family: Family.Calculation,
            MergeStrategy: MergeStrategy.Custom,
            SnapshotContract: null);

        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-CELL-5",
            periodStart: StraddleStart,
            periodEnd: StraddleEnd,
            calculationKind: "forward-calc",
            ruleSet: new[] { flexBalanceClassification },
            sources: OkStraddleSources(),
            options: PlannerOptions.Default);

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(BoundaryCause.OkTransition, plan.Segments[0].BoundaryCause);
        Assert.Equal(BoundaryCause.OkTransition, plan.Segments[1].BoundaryCause);
        Assert.Equal(MergeStrategyKind.Custom, flexBalanceClassification.MergeStrategy.Kind);
    }
}
