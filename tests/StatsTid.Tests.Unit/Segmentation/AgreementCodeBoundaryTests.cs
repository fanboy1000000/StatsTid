using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Segmentation;

/// <summary>
/// S144 / TASK-14405 (QUAL-150 groundwork; TASK-14402's definition of done) — the planner learns
/// that a change of an employee's AGREEMENT CODE (<c>user_agreement_codes.effective_from</c>) is a
/// segment boundary, and reports a D4 split refusal in STRUCTURED form.
///
/// <para>
/// <b>Why it matters (plain language).</b> Until S144 the planner could not see a mid-month
/// agreement change at all, so a month straddling one was calculated as ONE segment under a single
/// agreement — silently paying part of the month under the wrong agreement. Making the change a
/// boundary means the live rule set REFUSES such a month (the safe outcome: it goes to the manual
/// path) instead of paying it wrongly; the refusal carries counts and cause names — never a date —
/// so a caller can render it without leaking an employment date (ADR-040 D7).
/// </para>
///
/// <para>
/// <b>Layers pinned.</b> (a) the detector: strictly inside the period yes, on <c>periodStart</c>
/// no; (b) the ruled tie-break (R1): <c>AgreementCodeChange</c> outranks
/// <c>EmployeeProfileChange</c> on a shared date; (c) BOTH D4 refusal sites (AlignedWindow and
/// Reject) populate the structured members — one fact each, so leaving either on the message-only
/// constructor is caught by its own pin; (d) a geometric violation leaves the members null / empty.
/// </para>
///
/// <para>
/// The Reject case uses a SYNTHETIC rule: the live set has no Reject rule
/// (<c>EmploymentTruncationAlignmentTests.LiveSet_HasAlignedWindowRules_NoRejectRules_OvertimeFirst</c>).
/// All planner calls are windowless, so every segment types EMPLOYED and an interior boundary means
/// two EMPLOYED segments (the pre-S137 rule, unchanged).
/// </para>
/// </summary>
public sealed class AgreementCodeBoundaryTests
{
    // March 2026: no OK transition, so the agreement-code date is the ONLY boundary source
    // unless a test adds another.
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);
    private static readonly DateOnly Mar16 = new(2026, 3, 16);

    private static RuleClassification RejectCalc(string ruleId) => new(
        ruleId, Span.Window, SplitBehavior.Reject, Family.Calculation,
        MergeStrategy.RejectIfMultipleSegments, SnapshotContract: null);

    private static RuleClassification AlignedWindowCalc(string ruleId) => new(
        ruleId, Span.Window, SplitBehavior.AlignedWindow, Family.Calculation,
        MergeStrategy.RejectIfMultipleSegments, SnapshotContract: null);

    private static BoundarySources Sources(
        IReadOnlyList<DateOnly>? agreementCodeDates = null,
        IReadOnlyList<DateOnly>? employeeProfileDates = null) => new(
        OkTransitions: Array.Empty<(DateOnly, string, string)>(),
        AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
        PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
        EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
        NonDatedSourceValues: new Dictionary<string, object?>(),
        EmployeeProfileEffectiveDates: employeeProfileDates,
        AgreementCodeEffectiveDates: agreementCodeDates);

    // ═════════════════════════════════════════════════════════════════════
    // 5a. The detector: strictly inside the period, not on periodStart
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A date strictly inside the period is a boundary whose cause is
    /// <see cref="BoundaryCause.AgreementCodeChange"/>.
    ///
    /// Red conditions: no named mutation for THIS fact on the interior case (it is the positive
    /// control for M-7's sibling below). It goes red if the detector ignores
    /// <c>AgreementCodeEffectiveDates</c> altogether, tripping <c>Assert.Single</c>.
    /// </summary>
    [Fact]
    public void Detect_AgreementCodeDate_StrictlyInsidePeriod_EmitsAgreementCodeChange()
    {
        var boundaries = BoundaryDetector.Detect(Mar01, Mar31, Sources(agreementCodeDates: new[] { Mar16 }));

        var boundary = Assert.Single(boundaries);
        Assert.Equal(Mar16, boundary.Date);
        Assert.Equal(BoundaryCause.AgreementCodeChange, boundary.Cause);
    }

    /// <summary>
    /// A date ON <c>periodStart</c> is not a split (it is part of the first segment's starting
    /// context) — the detector must record nothing.
    ///
    /// Red conditions: mutation M-7 — <c>BoundaryDetector.Detect</c> drops the
    /// <c>IsInsidePeriod</c> guard on the agreement-code loop. A boundary is then recorded on
    /// <c>periodStart</c>, tripping <c>Assert.Empty(boundaries)</c>.
    /// </summary>
    [Fact]
    public void Detect_AgreementCodeDate_OnPeriodStart_IsNotABoundary()
    {
        var boundaries = BoundaryDetector.Detect(Mar01, Mar31, Sources(agreementCodeDates: new[] { Mar01 }));

        Assert.Empty(boundaries);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 5b. The tie-break (ruled R1): AgreementCodeChange beats EmployeeProfileChange
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// On a date that is BOTH an agreement-code change and an employee-profile change, exactly one
    /// boundary survives and its recorded cause is <c>AgreementCodeChange</c> (the agreement is the
    /// stronger fact about the date: it changes which rules and wage types apply, not just hours).
    ///
    /// Red conditions: mutation M-8 — the agreement-code loop moves AFTER the
    /// <c>EmployeeProfileChange</c> loop in <c>BoundaryDetector.Detect</c> (first-write-wins then
    /// records the profile cause). The cause comes back <c>EmployeeProfileChange</c>, tripping
    /// <c>Assert.Equal(BoundaryCause.AgreementCodeChange, boundary.Cause)</c>.
    /// </summary>
    [Fact]
    public void Detect_AgreementCodeAndProfileChangeShareADate_RecordsAgreementCodeChange()
    {
        var boundaries = BoundaryDetector.Detect(
            Mar01, Mar31,
            Sources(agreementCodeDates: new[] { Mar16 }, employeeProfileDates: new[] { Mar16 }));

        var boundary = Assert.Single(boundaries);
        Assert.Equal(Mar16, boundary.Date);
        Assert.Equal(BoundaryCause.AgreementCodeChange, boundary.Cause);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 5c / 5d. The D4 split refusal reports STRUCTURED members at BOTH sites
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AlignedWindow site (PeriodPlanner's second D4 throw): under an AlignedWindow rule and
    /// <c>PlannerOptions.Default</c> (<c>AllowUpstreamAlignment = false</c>) a mid-period
    /// agreement-code date refuses, and the exception carries the structured members: split
    /// refusal, 2 EMPLOYED segments, the single interior cause, the AlignedWindow rule's id.
    ///
    /// Red conditions: mutation M-9 — the AlignedWindow site (<c>PeriodPlanner</c>, the
    /// <c>alignedRule</c> throw) uses the message-only constructor. The exception is still the
    /// exact type and message, but <c>IsSplitRefusal</c> is false, tripping
    /// <c>Assert.True(ex.IsSplitRefusal)</c>.
    /// </summary>
    [Fact]
    public void Plan_MidPeriodAgreementCodeDate_AlignedWindowRule_ThrowsWithStructuredSplitRefusalMembers()
    {
        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(
                employeeId: "EMP-AGR-ALIGNED",
                periodStart: Mar01,
                periodEnd: Mar31,
                calculationKind: "forward-calc",
                ruleSet: new[] { AlignedWindowCalc("ALIGNED_CALC") },
                sources: Sources(agreementCodeDates: new[] { Mar16 }),
                options: PlannerOptions.Default));

        Assert.True(ex.IsSplitRefusal);
        Assert.Equal(2, ex.EmployedSegmentCount);
        Assert.Equal(new[] { BoundaryCause.AgreementCodeChange }, ex.InteriorBoundaryCauses);
        Assert.Equal("ALIGNED_CALC", ex.SplitRefusalRuleId);
    }

    /// <summary>
    /// Reject site (PeriodPlanner's first D4 throw), a SEPARATE fact from the AlignedWindow one so
    /// each site's regression is attributable: a synthetic Reject rule and the same mid-period
    /// agreement-code date refuse with the structured members populated.
    ///
    /// Red conditions: mutation M-10 — the Reject site (<c>PeriodPlanner</c>, the
    /// <c>rejectRule</c> throw) uses the message-only constructor. <c>IsSplitRefusal</c> is then
    /// false, tripping <c>Assert.True(ex.IsSplitRefusal)</c> (and <c>SplitRefusalRuleId</c> would
    /// be null, not <c>"OVERTIME_CALC"</c>).
    /// </summary>
    [Fact]
    public void Plan_MidPeriodAgreementCodeDate_RejectRule_ThrowsWithStructuredSplitRefusalMembers()
    {
        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(
                employeeId: "EMP-AGR-REJECT",
                periodStart: Mar01,
                periodEnd: Mar31,
                calculationKind: "forward-calc",
                ruleSet: new[] { RejectCalc("OVERTIME_CALC") },
                sources: Sources(agreementCodeDates: new[] { Mar16 }),
                options: PlannerOptions.Default));

        Assert.True(ex.IsSplitRefusal);
        Assert.Equal("OVERTIME_CALC", ex.SplitRefusalRuleId);
        Assert.Equal(2, ex.EmployedSegmentCount);
        Assert.Equal(new[] { BoundaryCause.AgreementCodeChange }, ex.InteriorBoundaryCauses);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 5e. A geometric violation is NOT a split refusal
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A geometric violation (periodEnd before periodStart — a bug, not a refusable month) leaves
    /// the structured members null / empty and <c>IsSplitRefusal</c> false, so the payroll layer
    /// never dresses a bug up as "this month needs manual handling".
    ///
    /// Red conditions: mutation M-11 — the message-only <c>PlannerInvariantViolation</c>
    /// constructor defaults <c>EmployedSegmentCount</c> to <c>0</c> instead of <c>null</c>.
    /// <c>IsSplitRefusal</c> (<c>EmployedSegmentCount is not null</c>) becomes true, tripping
    /// <c>Assert.False(ex.IsSplitRefusal)</c>.
    /// </summary>
    [Fact]
    public void Plan_GeometricViolation_LeavesStructuredMembersNullOrEmpty_NotASplitRefusal()
    {
        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(
                employeeId: "EMP-AGR-GEOM",
                periodStart: Mar31,
                periodEnd: Mar01,
                calculationKind: "forward-calc",
                ruleSet: new[] { RejectCalc("OVERTIME_CALC") },
                sources: Sources(agreementCodeDates: new[] { Mar16 }),
                options: PlannerOptions.Default));

        Assert.False(ex.IsSplitRefusal);
        Assert.Null(ex.EmployedSegmentCount);
        Assert.Null(ex.SplitRefusalRuleId);
        Assert.Empty(ex.InteriorBoundaryCauses);
    }
}
