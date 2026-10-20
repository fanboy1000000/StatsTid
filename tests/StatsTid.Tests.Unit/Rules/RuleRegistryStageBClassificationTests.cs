using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S145 Stage B / TASK-14524 (pins for TASK-14520 contracts 1, 4, 5) - the new "timeline-aware" label
/// and which shipped rules carry it.
///
/// <para>
/// Plain language: overtime, the payroll norm check and flex are evaluated once for the whole month, so
/// they now carry the label "TimelineAware" and the merge kind "SinglePlanEvaluation" (one answer, never
/// cut in two). The weekly norm and the rest rules are NOT run by payroll and keep "AlignedWindow", which
/// is what still makes the planner refuse a split month for a rule set that contains them. Expected
/// values come from the refinement (B1, :429-433, :684) and TASK-14520's contract, not from the code.
/// </para>
///
/// <para>
/// NEW-API pins (they name <c>SplitBehavior.TimelineAware</c> / <c>MergeStrategyKind.SinglePlanEvaluation</c>):
/// RED by Unit mutation at the B.3 gate, MB-10a (the default maps TimelineAware to
/// RejectIfMultipleSegments), MB-10b (OVERTIME_CALC registered AlignedWindow again), MB-10c (the
/// NORM_CHECK_37H registration removed).
/// </para>
/// </summary>
public sealed class RuleRegistryStageBClassificationTests
{
    [Fact]
    public void Register_TimelineAwareCalculation_WithoutStrategy_ResolvesSinglePlanEvaluation()
    {
        var registry = new RuleRegistry();

        registry.Register("SYNTHETIC_TIMELINE_CALC", Span.Window, SplitBehavior.TimelineAware, Family.Calculation);

        var c = registry.Get("SYNTHETIC_TIMELINE_CALC");
        Assert.Equal(SplitBehavior.TimelineAware, c.SplitBehavior);
        Assert.Equal(MergeStrategyKind.SinglePlanEvaluation, c.MergeStrategy.Kind);
        Assert.Same(MergeStrategy.SinglePlanEvaluation, c.MergeStrategy);
    }

    [Fact]
    public void Register_TimelineAwareCompliance_WithoutStrategy_Throws()
    {
        // The compliance merger has no single-plan arm (it throws on an unknown kind), so there is no
        // default: like a Mergeable rule without an override, the registration must say how it merges.
        var registry = new RuleRegistry();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("SYNTHETIC_TIMELINE_COMPLIANCE", Span.Window, SplitBehavior.TimelineAware, Family.Compliance));

        Assert.Contains("SYNTHETIC_TIMELINE_COMPLIANCE", ex.Message);
        Assert.Throws<KeyNotFoundException>(() => registry.Get("SYNTHETIC_TIMELINE_COMPLIANCE")); // nothing registered
    }

    [Fact]
    public void ShippedRegistry_PayrollTimelineRules_TimelineAware_RestRulesAndWeeklyNormKeepAlignedWindow()
    {
        var registry = new RuleRegistry();

        // The three rules payroll evaluates in its ONE timeline call (NORM_CHECK_37H is newly registered).
        foreach (var id in new[] { OvertimeRule.RuleId, NormCheckRule.RuleId, FlexBalanceRule.RuleId })
        {
            var c = registry.Get(id);
            Assert.Equal(SplitBehavior.TimelineAware, c.SplitBehavior);
            Assert.Equal(MergeStrategyKind.SinglePlanEvaluation, c.MergeStrategy.Kind);
            Assert.Equal(Family.Calculation, c.Family);
        }

        // The rules payroll never runs keep their "never cut in two" label (refinement :432).
        foreach (var id in new[] { NormCheckRule.WeeklyRuleId, RestPeriodRule.DailyRestRuleId, RestPeriodRule.WeeklyRestRuleId })
        {
            var c = registry.Get(id);
            Assert.Equal(SplitBehavior.AlignedWindow, c.SplitBehavior);
            Assert.Equal(MergeStrategyKind.RejectIfMultipleSegments, c.MergeStrategy.Kind);
        }

        // The inventory is then 17 (16 + the newly registered NORM_CHECK_37H).
        Assert.Equal(17, registry.GetAll().Count);
    }
}
