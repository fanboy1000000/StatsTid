using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// QUAL-019 (S133 TASK-13303) — pins the SHIPPED rule-classification registry
/// (<see cref="RuleRegistry"/>, ADR-016 D2/D3 inventory).
///
/// <para><b>Why this file exists / the defect it closes:</b> before S133 every segmentation
/// test hand-built its OWN <see cref="RuleClassification"/> tuples with invented rule ids
/// (e.g. <c>PlannerCellTests</c> constructs <c>new RuleClassification("OVERTIME_CALC", …)</c>
/// locally). NO test read what the production <see cref="RuleRegistry"/> actually registers.
/// Consequence, in plain terms: if a real rule were mis-registered — for instance if the
/// weekly-overtime rule's <see cref="SplitBehavior"/> (the flag that decides what happens when
/// an OK-version boundary, e.g. OK24→OK26 on 2026-04-01, falls INSIDE the rule's evaluation
/// window) were flipped from <see cref="SplitBehavior.AlignedWindow"/> to
/// <see cref="SplitBehavior.SegmentSafe"/> — nothing would turn red, yet payroll would silently
/// split a window that must never be split. That is a Domain-correctness invariant risk.</para>
///
/// <para><b>What these tests do differently:</b> they instantiate the real
/// <c>new RuleRegistry()</c> and assert its <see cref="RuleRegistry.Get"/> /
/// <see cref="RuleRegistry.GetAll"/> output directly. A wrong registration turns the exact
/// affected row RED (see the falsifiability note on <see cref="RegistryEntries"/>). The
/// expected triples below are derived by hand from ADR-016 D2/D3, NOT read back from the
/// implementation.</para>
/// </summary>
public sealed class RuleRegistryClassificationTests
{
    /// <summary>
    /// The hand-derived expected classification for every registered rule (16 total, per
    /// <c>RuleRegistry</c>'s own inventory comment). Each row is
    /// <c>(ruleId, span, splitBehavior, family, mergeStrategyKind)</c>.
    ///
    /// <para><b>Falsifiability (QUAL-019):</b> the real guard is each <c>Register(…)</c> call in
    /// <c>RuleRegistry.cs</c> (lines 53–94). Mutating any single axis of a real registration —
    /// e.g. changing <c>Register(OvertimeRule.RuleId, Span.Window, SplitBehavior.AlignedWindow, …)</c>
    /// to <c>SplitBehavior.SegmentSafe</c> — turns exactly the <c>OVERTIME_CALC</c> row RED at the
    /// <see cref="SplitBehavior"/> assertion, while every other row stays green (one inversion →
    /// one localized failure, per PAT-014 Rule 1).</para>
    /// </summary>
    public static readonly TheoryData<string, Span, SplitBehavior, Family, MergeStrategyKind> RegistryEntries = new()
    {
        // (entry, segment-safe, calculation) -> default merge Concatenate
        { SupplementRule.RuleId,   Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategyKind.Concatenate },
        { OnCallDutyRule.RuleId,   Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategyKind.Concatenate },
        { CallInWorkRule.RuleId,   Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategyKind.Concatenate },
        { TravelTimeRule.RuleId,   Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategyKind.Concatenate },
        { AbsenceRule.RuleId,      Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategyKind.Concatenate },

        // (window, segment-safe, compliance) -> default merge UnionDedupe
        { RestPeriodRule.MaxDailyRuleId, Span.Window, SplitBehavior.SegmentSafe, Family.Compliance, MergeStrategyKind.UnionDedupe },

        // (window, aligned-window, calculation) -> default merge RejectIfMultipleSegments
        { OvertimeRule.RuleId,        Span.Window, SplitBehavior.AlignedWindow, Family.Calculation, MergeStrategyKind.RejectIfMultipleSegments },
        { NormCheckRule.WeeklyRuleId, Span.Window, SplitBehavior.AlignedWindow, Family.Calculation, MergeStrategyKind.RejectIfMultipleSegments },

        // (window, aligned-window, compliance) -> default merge RejectIfMultipleSegments
        { RestPeriodRule.DailyRestRuleId,  Span.Window, SplitBehavior.AlignedWindow, Family.Compliance, MergeStrategyKind.RejectIfMultipleSegments },
        { RestPeriodRule.WeeklyRestRuleId, Span.Window, SplitBehavior.AlignedWindow, Family.Compliance, MergeStrategyKind.RejectIfMultipleSegments },

        // (period, mergeable, calculation) -> explicit override Custom
        { NormCheckRule.MultiWeekRuleId, Span.Period, SplitBehavior.Mergeable, Family.Calculation, MergeStrategyKind.Custom },
        { NormCheckRule.AnnualRuleId,    Span.Period, SplitBehavior.Mergeable, Family.Calculation, MergeStrategyKind.Custom },

        // (period, mergeable, compliance) -> explicit override UnionDedupe (ADR-016 D3: compliance always unions)
        { RestPeriodRule.Weekly48HCeilingRuleId,    Span.Period, SplitBehavior.Mergeable, Family.Compliance, MergeStrategyKind.UnionDedupe },
        { OvertimeGovernanceRule.MaxHoursRuleId,    Span.Period, SplitBehavior.Mergeable, Family.Compliance, MergeStrategyKind.UnionDedupe },
        { OvertimeGovernanceRule.PreApprovalRuleId, Span.Period, SplitBehavior.Mergeable, Family.Compliance, MergeStrategyKind.UnionDedupe },

        // (cross-period, mergeable, calculation) -> explicit override Custom (chained carry)
        { FlexBalanceRule.RuleId, Span.CrossPeriod, SplitBehavior.Mergeable, Family.Calculation, MergeStrategyKind.Custom },
    };

    /// <summary>
    /// Reads the SHIPPED registry (<c>new RuleRegistry().Get(ruleId)</c>) and asserts every
    /// axis of the classification triple plus the resolved merge strategy against the
    /// hand-derived expectation. A wrong registration of any axis turns this row RED.
    /// </summary>
    [Theory]
    [MemberData(nameof(RegistryEntries))]
    public void ShippedRegistry_ClassifiesRule_WithExpectedTripleAndMergeStrategy(
        string ruleId,
        Span expectedSpan,
        SplitBehavior expectedSplit,
        Family expectedFamily,
        MergeStrategyKind expectedMerge)
    {
        var registry = new RuleRegistry();

        var classification = registry.Get(ruleId);

        Assert.Equal(ruleId, classification.RuleId);
        Assert.Equal(expectedSpan, classification.Span);
        Assert.Equal(expectedSplit, classification.SplitBehavior);
        Assert.Equal(expectedFamily, classification.Family);
        Assert.Equal(expectedMerge, classification.MergeStrategy.Kind);
    }

    /// <summary>
    /// Pins the FULL inventory: the shipped registry must register exactly these 16 ids — no
    /// more, no fewer. Catches a rule that is dropped from registration (would silently stop
    /// being segmented) or an unexpected new registration added without a matching pin above.
    /// </summary>
    [Fact]
    public void ShippedRegistry_RegistersExactlyTheExpectedInventory()
    {
        var registry = new RuleRegistry();
        var actualIds = registry.GetAll().Select(c => c.RuleId).ToHashSet();

        var expectedIds = new HashSet<string>
        {
            SupplementRule.RuleId, OnCallDutyRule.RuleId, CallInWorkRule.RuleId,
            TravelTimeRule.RuleId, AbsenceRule.RuleId,
            RestPeriodRule.MaxDailyRuleId,
            OvertimeRule.RuleId, NormCheckRule.WeeklyRuleId,
            RestPeriodRule.DailyRestRuleId, RestPeriodRule.WeeklyRestRuleId,
            NormCheckRule.MultiWeekRuleId, NormCheckRule.AnnualRuleId,
            RestPeriodRule.Weekly48HCeilingRuleId,
            OvertimeGovernanceRule.MaxHoursRuleId, OvertimeGovernanceRule.PreApprovalRuleId,
            FlexBalanceRule.RuleId,
        };

        var missing = expectedIds.Except(actualIds).ToList();
        var unexpected = actualIds.Except(expectedIds).ToList();

        Assert.True(
            missing.Count == 0 && unexpected.Count == 0,
            $"Registry inventory drift. Missing: [{string.Join(", ", missing)}]. Unexpected: [{string.Join(", ", unexpected)}].");
        Assert.Equal(16, registry.GetAll().Count);
    }

    /// <summary>
    /// Pins the documented out-of-segmentation-scope decisions so they cannot be silently
    /// reversed. The legacy <c>NORM_CHECK_37H</c> dispatch id is a config-aware time-rule alias,
    /// NOT a segmentation classification (only the S20-decomposed Weekly/MultiWeek/Annual ids
    /// are registered); and a genuinely unknown id must raise, not resolve to a default.
    /// </summary>
    [Fact]
    public void ShippedRegistry_DoesNotClassify_LegacyNormIdOrUnknownIds()
    {
        var registry = new RuleRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.Get(NormCheckRule.RuleId));
        Assert.Throws<KeyNotFoundException>(() => registry.Get("NOT_A_REGISTERED_RULE"));
    }
}
