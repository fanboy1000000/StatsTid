using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S137 Step-5a (Reviewer WARNING 2) — the MERGE half of the 2026-09-02 truncation ruling,
/// pinned DB-free with a rule classified exactly like the production <c>OVERTIME_CALC</c>
/// (<c>Span.Window / AlignedWindow / RejectIfMultipleSegments</c>).
///
/// <para>
/// Plain-language: a "whole-window" rule (weekly norm, overtime) must not be cut in two —
/// two half-answers cannot be merged into one correct answer. The ruling says a hire or leave
/// edge is a TRUNCATION, not a split: the rule still runs exactly once, over the employed
/// span, so the planner lets the month through. That claim rests on a second line of defence
/// downstream: when such a rule's results reach the merge step, <c>RejectIfMultipleSegments</c>
/// passes ONE segment and refuses TWO. These pins prove both directions of that backstop in
/// <see cref="StatsTid.Integrations.Payroll.Services.PeriodCalculationService"/>'s own merge
/// path — the Docker-gated <c>EmploymentWindowLiveRulesetTests</c> proves the same against a
/// real database and the live registry.
/// </para>
/// </summary>
public sealed class EmploymentWindowAlignedWindowMergeTests
{
    private const string EmployeeId = "EMP-S137-ALIGNED";
    private const string AlignedRuleId = "OVERTIME_CALC";

    /// <summary>(i) Mid-month leaver: EMPLOYED / NOT_EMPLOYED. The planner accepts the month
    /// under <see cref="PlannerOptions.Default"/> (one EMPLOYED segment — a truncation), the
    /// aligned-window rule is evaluated once, reaches the merger as a single segment, and its
    /// merged row is a SUCCESS.</summary>
    [Fact]
    public async Task Leaver_AlignedWindowRule_OneEmployedSegment_MergedResultSucceeds()
    {
        var lastDay = new DateOnly(2026, 3, 15);
        var plan = Plan(EmployeeId, Mar01, Mar31,
            windows: new[] { new EmploymentWindow(null, lastDay) },
            ruleSet: AlignedWindowRuleSet); // PlannerOptions.Default — no pass-through flag needed
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[1].EmploymentStatus);

        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine, ruleSet: AlignedWindowRuleSet);

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId), WeekdayEntries(EmployeeId, Mar01, Mar31),
            Array.Empty<AbsenceEntry>(), previousFlexBalance: 0m);

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);

        var aligned = Assert.Single(outcome.Result.RuleResults, r => r.RuleId == AlignedRuleId);
        Assert.True(aligned.Success, aligned.ErrorMessage);
        Assert.Null(aligned.ErrorMessage);

        // The rule ran exactly once — over the employed span only.
        var alignedCalls = engine.Calls.Where(c => c.RuleId == AlignedRuleId).ToList();
        var alignedCall = Assert.Single(alignedCalls);
        Assert.Equal(Mar01, alignedCall.PeriodStart);
        Assert.Equal(lastDay, alignedCall.PeriodEnd);
    }

    /// <summary>(ii) Two EMPLOYED segments (a profile change while employed — a genuine
    /// split). The planner is told to pass it through (<c>AllowUpstreamAlignment = true</c>);
    /// the aligned-window rule is then evaluated TWICE and the merge step is the backstop: its
    /// row becomes the <c>RejectIfMultipleSegments</c> failure row while every other rule
    /// merges normally and the calculation as a whole still completes.</summary>
    [Fact]
    public async Task TwoEmployedSegments_AlignedWindowRule_PassedThroughByPlanner_MergerRejectsThatRuleOnly()
    {
        var profileChange = new DateOnly(2026, 3, 16);
        var plan = Plan(EmployeeId, Mar01, Mar31,
            windows: null, // windowless → both segments EMPLOYED
            ruleSet: AlignedWindowRuleSet,
            options: new PlannerOptions { AllowUpstreamAlignment = true },
            profileChangeDates: new[] { profileChange });
        Assert.Equal(2, plan.Segments.Count);
        Assert.All(plan.Segments, s => Assert.Equal(EmploymentWindowStatus.EMPLOYED, s.EmploymentStatus));
        Assert.Equal(BoundaryCause.EmployeeProfileChange, plan.Segments[1].BoundaryCause);

        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine, ruleSet: AlignedWindowRuleSet);

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId), WeekdayEntries(EmployeeId, Mar01, Mar31),
            Array.Empty<AbsenceEntry>(), previousFlexBalance: 0m);

        // The calculation continues; the failure surfaces ON THE RULE (PCS merge contract).
        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.Equal(2, engine.Calls.Count(c => c.RuleId == AlignedRuleId));

        var aligned = Assert.Single(outcome.Result.RuleResults, r => r.RuleId == AlignedRuleId);
        Assert.False(aligned.Success);
        Assert.NotNull(aligned.ErrorMessage);
        Assert.Contains("RejectIfMultipleSegments", aligned.ErrorMessage);
        Assert.Contains(AlignedRuleId, aligned.ErrorMessage);
        Assert.Contains("2 segments", aligned.ErrorMessage);
        Assert.Empty(aligned.LineItems);

        // Every OTHER rule merged normally across the two employed segments.
        Assert.All(
            outcome.Result.RuleResults.Where(r => r.RuleId != AlignedRuleId),
            r => Assert.True(r.Success, r.ErrorMessage));
        Assert.Equal(RuleCallsPerEmployedSegment, outcome.Result.RuleResults.Count);
    }

    /// <summary>Negative control for (ii): WITHOUT the pass-through flag the planner itself
    /// refuses the two-EMPLOYED-segment split for an aligned-window rule (ADR-016 D4) — the
    /// merger is the backstop BEHIND the planner, not a replacement for it.</summary>
    [Fact]
    public void TwoEmployedSegments_AlignedWindowRule_DefaultOptions_PlannerRefuses()
    {
        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            Plan(EmployeeId, Mar01, Mar31,
                windows: null,
                ruleSet: AlignedWindowRuleSet,
                profileChangeDates: new[] { new DateOnly(2026, 3, 16) }));

        Assert.Contains("SplitBehavior=AlignedWindow", ex.Message);
        Assert.Contains(AlignedRuleId, ex.Message);
    }
}
