using System.Text.RegularExpressions;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Segmentation;

/// <summary>
/// S145 Stage B / TASK-14524 (pins for TASK-14520 contract 2-3) - the new merge strategy
/// <see cref="MergeStrategy.SinglePlanEvaluation"/>.
///
/// <para>
/// Plain language: a "timeline-aware" rule (overtime, the payroll norm check, flex) is evaluated ONCE
/// across the whole month, so exactly one answer reaches the merge step and it must pass through
/// untouched (including the norm totals that <c>Concatenate</c> would null out). Two answers for such
/// a rule means the design was broken, and the merger fails loudly instead of guessing.
/// </para>
///
/// <para>
/// NEW-API pins: they name a member Stage B creates, so they cannot compile before TASK-14520.
/// Their RED is by Unit mutation at the B.3 gate (MB-1: the arm returns the first result for two or
/// more), never by reverting.
/// </para>
/// </summary>
public sealed class SinglePlanEvaluationMergerTests
{
    private const string RuleId = "OVERTIME_CALC"; // no digit, so a count in the message is unambiguous

    private static CalculationResult Row(decimal? normTotal = null, params CalculationLineItem[] lines) => new()
    {
        RuleId = RuleId,
        EmployeeId = "EMP",
        Success = true,
        LineItems = lines.ToList(),
        NormPeriodWeeks = normTotal is null ? null : 5,
        NormHoursTotal = normTotal,
        ActualHoursTotal = normTotal is null ? null : normTotal + 1m,
        Deviation = normTotal is null ? null : 1m,
        NormFulfilled = normTotal is null ? null : true,
    };

    private static CalculationLineItem Line(int day, decimal hours) => new()
    {
        TimeType = "OVERTIME_50",
        Hours = hours,
        Rate = 1.5m,
        Date = new DateOnly(2026, 9, day),
    };

    [Fact]
    public void SinglePlanEvaluation_OneResult_PassesThroughUnchanged_NormTotalsKept()
    {
        var only = Row(147.40m, Line(20, 2.67m), Line(20, 4.53m));

        var merged = CalculationResultMerger.Apply(new[] { only }, MergeStrategy.SinglePlanEvaluation);

        Assert.Same(only, merged);
        Assert.Equal(147.40m, merged.NormHoursTotal);   // unlike Concatenate, which nulls the totals
        Assert.Equal(148.40m, merged.ActualHoursTotal);
        Assert.Equal(1m, merged.Deviation);
        Assert.Equal(5, merged.NormPeriodWeeks);
        Assert.True(merged.NormFulfilled);
        Assert.Equal(2, merged.LineItems.Count);
    }

    [Fact]
    public void SinglePlanEvaluation_TwoResults_Throws_NamesRuleAndCount_NoDate()
    {
        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            CalculationResultMerger.Apply(
                new[] { Row(null, Line(15, 1m)), Row(null, Line(20, 1m)) },
                MergeStrategy.SinglePlanEvaluation));

        Assert.Contains(RuleId, ex.Message);
        Assert.Matches(@"\b2\b", ex.Message);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", ex.Message); // no ISO date
        // A bug, not a split refusal: the structured split-refusal members stay unset, so the
        // handlers answer 500, never a split 422 (5a NOTE-3 on TASK-14520, Orchestrator).
        Assert.False(ex.IsSplitRefusal);
        Assert.Null(ex.SplitRefusalRuleId);

        // The count is the real count, not a constant: three results name 3, not 2.
        var three = Assert.Throws<PlannerInvariantViolation>(() =>
            CalculationResultMerger.Apply(
                new[] { Row(), Row(), Row() }, MergeStrategy.SinglePlanEvaluation));
        Assert.Matches(@"\b3\b", three.Message);
        Assert.DoesNotMatch(@"\b2\b", three.Message);
    }

    [Fact]
    public void SinglePlanEvaluation_EmptyList_Throws()
    {
        // The existing pre-guard: an empty list is a planner invariant violation for every strategy.
        Assert.Throws<PlannerInvariantViolation>(() =>
            CalculationResultMerger.Apply(Array.Empty<CalculationResult>(), MergeStrategy.SinglePlanEvaluation));
    }
}
