using System.Globalization;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Segmentation;

/// <summary>
/// S137 / TASK-13707 — ADR-016 D4 as applied after the owner ruling of 2026-09-02:
/// <b>hire and leave edges are TRUNCATIONS, not splits.</b>
///
/// <para>
/// Plain language. A whole-window rule (weekly norm, overtime, rest) must not be cut in two,
/// because two half-results cannot be merged into one correct answer — that is what ADR-016
/// D4 refuses. Before this ruling the planner refused whenever ANY boundary fell inside the
/// period. After ADR-040 D5 typed segments EMPLOYED / NOT_EMPLOYED, that made every month with
/// a mid-month hire or leave un-plannable under the live rule set, although the NOT_EMPLOYED
/// side evaluates nothing — the rule would still have run exactly once, over a shorter span.
/// The planner now refuses only when a whole-window rule would be EVALUATED in two or more
/// EMPLOYED segments: a profile change while employed, or two spells in one month.
/// </para>
///
/// <para>
/// Every pin here uses the LIVE classification set (<c>new RuleRegistry().GetAll()</c> — the
/// same set the payroll host reads over HTTP), so the pins are faithful to production wiring:
/// the live set has four AlignedWindow rules (OVERTIME_CALC first in registration order) and
/// no Reject rule. Where a Reject rule is needed one is constructed explicitly.
/// </para>
/// </summary>
public sealed class EmploymentTruncationAlignmentTests
{
    // March 2026 — a plain month with no OK transition, so every interior boundary below
    // comes from the source the test names.
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    // The OK24→OK26 transition date — the windowless straddle every pre-S137 pin uses.
    private static readonly DateOnly OkTransitionDate = new(2026, 4, 1);

    /// <summary>The production registry's classification set — what
    /// <c>HttpRuleClassificationProvider</c> serves the payroll host.</summary>
    private static readonly IReadOnlyList<RuleClassification> LiveSet = new RuleRegistry().GetAll();

    private static RuleClassification RejectCalc(string ruleId) => new(
        ruleId, Span.Window, SplitBehavior.Reject, Family.Calculation,
        MergeStrategy.RejectIfMultipleSegments, SnapshotContract: null);

    /// <summary>Sources builder — every list empty unless supplied, mirroring how the
    /// payroll host hydrates only the sources that actually have dates in-period.</summary>
    private static BoundarySources Sources(
        IReadOnlyList<(DateOnly, string, string)>? okTransitions = null,
        IReadOnlyList<DateOnly>? employmentStartedDates = null,
        IReadOnlyList<DateOnly>? employmentEndedDates = null,
        IReadOnlyList<DateOnly>? employeeProfileEffectiveDates = null) => new(
        OkTransitions: okTransitions ?? Array.Empty<(DateOnly, string, string)>(),
        AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
        PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
        EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
        NonDatedSourceValues: new Dictionary<string, object?>(),
        LocalProfileActivations: null,
        EmploymentStartedDates: employmentStartedDates,
        EmploymentEndedDates: employmentEndedDates,
        EmployeeProfileEffectiveDates: employeeProfileEffectiveDates);

    private static PlannedCalculation PlanMarch(
        string employeeId,
        IReadOnlyList<RuleClassification> ruleSet,
        BoundarySources sources,
        IReadOnlyList<EmploymentWindow>? windows,
        PlannerOptions? options = null) =>
        PeriodPlanner.Plan(
            employeeId: employeeId,
            periodStart: Mar01,
            periodEnd: Mar31,
            calculationKind: "forward-calc",
            ruleSet: ruleSet,
            sources: sources,
            options: options ?? PlannerOptions.Default,
            employmentWindows: windows);

    private static int EmployedCount(PlannedCalculation plan) =>
        plan.Segments.Count(s => s.EmploymentStatus == EmploymentWindowStatus.EMPLOYED);

    /// <summary>ADR-040 D7: a segment date can reveal an employment date, so the refusal
    /// message must not carry one — in the current culture's rendering (what the planner's
    /// interpolation uses) or in ISO form.</summary>
    private static void AssertNoDateLeak(string message, params DateOnly[] dates)
    {
        foreach (var d in dates)
        {
            Assert.DoesNotContain(d.ToString(), message);
            Assert.DoesNotContain(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), message);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // 0. Precondition on the live set — the pins below rely on these facts
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>If the registry ever gains a Reject rule or loses its AlignedWindow rules,
    /// the pins below change meaning — fail here first, legibly.</summary>
    [Fact]
    public void LiveSet_HasAlignedWindowRules_NoRejectRules_OvertimeFirst()
    {
        Assert.Contains(LiveSet, r => r.SplitBehavior == SplitBehavior.AlignedWindow);
        Assert.DoesNotContain(LiveSet, r => r.SplitBehavior == SplitBehavior.Reject);
        // FindFirst semantics: the refusal names the first AlignedWindow rule in
        // registration order — OVERTIME_CALC (RuleRegistry ctor).
        Assert.Equal("OVERTIME_CALC", LiveSet.First(r => r.SplitBehavior == SplitBehavior.AlignedWindow).RuleId);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 1. Windowless callers — behavior-identical to pre-S137 (any interior
    //    boundary ⟺ ≥ 2 EMPLOYED segments)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>The classic OK-straddle (March 15 – April 15 across 2026-04-01) with the live
    /// set and no windows STILL refuses, naming OVERTIME_CALC — the unchanged-behavior pin.</summary>
    [Fact]
    public void Windowless_InteriorOkBoundary_LiveSet_StillRefuses()
    {
        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(
                employeeId: "EMP-13707-WINDOWLESS-OK",
                periodStart: new DateOnly(2026, 3, 15),
                periodEnd: new DateOnly(2026, 4, 15),
                calculationKind: "forward-calc",
                ruleSet: LiveSet,
                sources: Sources(okTransitions: new[] { (OkTransitionDate, "OK24", "OK26") }),
                options: PlannerOptions.Default));

        Assert.Contains("'OVERTIME_CALC'", ex.Message);
        Assert.Contains("AlignedWindow", ex.Message);
        Assert.Contains("AllowUpstreamAlignment", ex.Message);
        Assert.Contains("2 EMPLOYED segments", ex.Message);
        Assert.Contains("OkTransition", ex.Message);
        Assert.Contains("ADR-016 D4", ex.Message);
    }

    /// <summary>Windowless + an interior boundary from each S137 source: every segment types
    /// EMPLOYED, so the SAME edge that is a truncation WITH windows (section 2) is a split
    /// WITHOUT them — exactly the pre-S137 rule.</summary>
    [Theory]
    [InlineData("EmploymentStarted")]
    [InlineData("EmploymentEnded")]
    [InlineData("EmployeeProfileChange")]
    public void Windowless_InteriorBoundaryFromAnySource_LiveSet_StillRefuses(string sourceKind)
    {
        var interior = new DateOnly(2026, 3, 16);
        var sources = sourceKind switch
        {
            "EmploymentStarted" => Sources(employmentStartedDates: new[] { interior }),
            "EmploymentEnded" => Sources(employmentEndedDates: new[] { interior }),
            "EmployeeProfileChange" => Sources(employeeProfileEffectiveDates: new[] { interior }),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind)),
        };

        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PlanMarch("EMP-13707-WINDOWLESS-" + sourceKind, LiveSet, sources, windows: null));

        Assert.Contains("'OVERTIME_CALC'", ex.Message);
        Assert.Contains("2 EMPLOYED segments", ex.Message);
        Assert.Contains(sourceKind, ex.Message);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 2. Employment edges WITH windows are truncations — the month plans
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Mid-month leaver (last day 03-15): 2 segments, exactly 1 EMPLOYED, the
    /// NOT_EMPLOYED suffix from 03-16. Before the ruling this exact input threw.</summary>
    [Fact]
    public void MidMonthLeaver_LiveSet_Plans_OneEmployedSegment()
    {
        var lastDay = new DateOnly(2026, 3, 15);

        var plan = PlanMarch(
            "EMP-13707-LEAVER", LiveSet,
            Sources(employmentEndedDates: new[] { lastDay.AddDays(1) }),
            windows: new[] { new EmploymentWindow(Start: null, End: lastDay) });

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(1, EmployedCount(plan));
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[0].EmploymentStatus);
        Assert.Equal(lastDay, plan.Segments[0].EndDate);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[1].EmploymentStatus);
        Assert.Equal(lastDay.AddDays(1), plan.Segments[1].StartDate);
        Assert.Equal(BoundaryCause.EmploymentEnded, plan.Segments[1].BoundaryCause);
    }

    /// <summary>Mid-month starter (first day 03-10): the mirror image plans too.</summary>
    [Fact]
    public void MidMonthStarter_LiveSet_Plans_OneEmployedSegment()
    {
        var hire = new DateOnly(2026, 3, 10);

        var plan = PlanMarch(
            "EMP-13707-STARTER", LiveSet,
            Sources(employmentStartedDates: new[] { hire }),
            windows: new[] { new EmploymentWindow(Start: hire, End: null) });

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(1, EmployedCount(plan));
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[0].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[1].EmploymentStatus);
        Assert.Equal(hire, plan.Segments[1].StartDate);
    }

    /// <summary>Hire AND leave inside one month: two interior boundaries, three segments,
    /// still only ONE EMPLOYED — two boundaries are not two splits.</summary>
    [Fact]
    public void HireAndLeaveInOneMonth_LiveSet_Plans_OneEmployedMiddleSegment()
    {
        var hire = new DateOnly(2026, 3, 10);
        var lastDay = new DateOnly(2026, 3, 20);

        var plan = PlanMarch(
            "EMP-13707-SPELL", LiveSet,
            Sources(
                employmentStartedDates: new[] { hire },
                employmentEndedDates: new[] { lastDay.AddDays(1) }),
            windows: new[] { new EmploymentWindow(hire, lastDay) });

        Assert.Equal(3, plan.Segments.Count);
        Assert.Equal(1, EmployedCount(plan));
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[0].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[1].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[2].EmploymentStatus);
    }

    /// <summary>The post-S136 hire shape: a hire on 03-10 AND a profile row effective 03-10
    /// (S136 dates the first profile row at hire). The coinciding date is ONE boundary,
    /// recorded as EmploymentStarted (D5 tie-break), and there is 1 EMPLOYED segment — so
    /// every new hire's first month plans. Had the profile date counted as a separate
    /// split, every post-S136 starter would have been refused.</summary>
    [Fact]
    public void PostS136HireShape_HireDatedProfileRow_LiveSet_Plans()
    {
        var hire = new DateOnly(2026, 3, 10);

        var plan = PlanMarch(
            "EMP-13707-S136-HIRE", LiveSet,
            Sources(
                employmentStartedDates: new[] { hire },
                employeeProfileEffectiveDates: new[] { hire }),
            windows: new[] { new EmploymentWindow(Start: hire, End: null) });

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(1, EmployedCount(plan));
        Assert.Equal(BoundaryCause.EmploymentStarted, plan.Segments[1].BoundaryCause);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[1].EmploymentStatus);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3. Genuine splits (≥ 2 EMPLOYED segments) still refuse — with a
    //    message that carries the count and the causes, never a date
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>A leaver (last day 03-20) whose part-time fraction changed on 03-10 WHILE
    /// employed: EMPLOYED [03-01..03-09], EMPLOYED [03-10..03-20], NOT_EMPLOYED from 03-21.
    /// Two EMPLOYED segments ⇒ OVERTIME_CALC would run twice ⇒ refuse. This is the
    /// registered live-set limitation (QUAL-149 / ADR-016 D4 follow-up).</summary>
    [Fact]
    public void LeaverWithProfileChangeWhileEmployed_LiveSet_Refuses_MessageCarriesCountAndCauses()
    {
        var change = new DateOnly(2026, 3, 10);
        var lastDay = new DateOnly(2026, 3, 20);

        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PlanMarch(
                "EMP-13707-PROFILE-SPLIT", LiveSet,
                Sources(
                    employmentEndedDates: new[] { lastDay.AddDays(1) },
                    employeeProfileEffectiveDates: new[] { change }),
                windows: new[] { new EmploymentWindow(Start: null, End: lastDay) }));

        Assert.Contains("'OVERTIME_CALC'", ex.Message);
        Assert.Contains("AlignedWindow", ex.Message);
        Assert.Contains("2 EMPLOYED segments", ex.Message);
        Assert.Contains("EmployeeProfileChange", ex.Message);
        // The reader learns the rule from the error.
        Assert.Contains("employment edge alone", ex.Message);

        // The period itself is named (the caller's own input) …
        Assert.Contains(Mar01.ToString(), ex.Message);
        Assert.Contains(Mar31.ToString(), ex.Message);
        // … but no segment date: the change date, the last employed day, the first
        // NOT-employed day and the day before the change are all employment-revealing.
        AssertNoDateLeak(ex.Message, change, change.AddDays(-1), lastDay, lastDay.AddDays(1));
    }

    /// <summary>Two spells in one month (windows (null, 03-10) and (03-20, null)):
    /// EMPLOYED / NOT_EMPLOYED / EMPLOYED — 2 EMPLOYED segments ⇒ refuse, naming both
    /// employment causes so the reader sees it was the second SPELL, not the edge.</summary>
    [Fact]
    public void TwoSpellsInOneMonth_LiveSet_Refuses()
    {
        var firstSpellEnd = new DateOnly(2026, 3, 10);
        var secondSpellStart = new DateOnly(2026, 3, 20);

        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PlanMarch(
                "EMP-13707-TWO-SPELLS", LiveSet,
                Sources(
                    employmentStartedDates: new[] { secondSpellStart },
                    employmentEndedDates: new[] { firstSpellEnd.AddDays(1) }),
                windows: new[]
                {
                    new EmploymentWindow(null, firstSpellEnd),
                    new EmploymentWindow(secondSpellStart, null),
                }));

        Assert.Contains("'OVERTIME_CALC'", ex.Message);
        Assert.Contains("2 EMPLOYED segments", ex.Message);
        Assert.Contains("EmploymentEnded", ex.Message);
        Assert.Contains("EmploymentStarted", ex.Message);
        AssertNoDateLeak(ex.Message, firstSpellEnd, firstSpellEnd.AddDays(1), secondSpellStart, secondSpellStart.AddDays(-1));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 4. Reject rules follow the same truncation rule
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void RejectRule_OneEmployedSegment_Plans()
    {
        var lastDay = new DateOnly(2026, 3, 15);

        var plan = PlanMarch(
            "EMP-13707-REJECT-TRUNC", new[] { RejectCalc("REJECT_RULE") },
            Sources(employmentEndedDates: new[] { lastDay.AddDays(1) }),
            windows: new[] { new EmploymentWindow(Start: null, End: lastDay) });

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(1, EmployedCount(plan));
    }

    [Fact]
    public void RejectRule_TwoEmployedSegments_Refuses()
    {
        var change = new DateOnly(2026, 3, 10);

        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PlanMarch(
                "EMP-13707-REJECT-SPLIT", new[] { RejectCalc("REJECT_RULE") },
                Sources(employeeProfileEffectiveDates: new[] { change }),
                windows: new[] { new EmploymentWindow(null, null) }));

        Assert.Contains("'REJECT_RULE'", ex.Message);
        Assert.Contains("SplitBehavior=Reject", ex.Message);
        Assert.Contains("2 EMPLOYED segments", ex.Message);
        Assert.Contains("EmployeeProfileChange", ex.Message);
        AssertNoDateLeak(ex.Message, change, change.AddDays(-1));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 5. AllowUpstreamAlignment — the S20 stub semantics are unchanged
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>With the flag on and only AlignedWindow rules (the live set), a 2-EMPLOYED plan
    /// passes THROUGH unchanged — the planner cannot shrink to a rule's natural edge
    /// (rule-specific arithmetic), so the S20 stub is a pass-through, not a shrink.</summary>
    [Fact]
    public void AllowUpstreamAlignment_TwoEmployed_AlignedWindowOnly_PassesThrough()
    {
        var change = new DateOnly(2026, 3, 10);

        var plan = PlanMarch(
            "EMP-13707-ALLOW", LiveSet,
            Sources(employeeProfileEffectiveDates: new[] { change }),
            windows: new[] { new EmploymentWindow(null, null) },
            options: new PlannerOptions { AllowUpstreamAlignment = true });

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(2, EmployedCount(plan));
        Assert.Equal(Mar01, plan.PeriodStart);
        Assert.Equal(Mar31, plan.PeriodEnd);
    }

    /// <summary>The flag never overrides Reject (ADR-016 D4).</summary>
    [Fact]
    public void AllowUpstreamAlignment_TwoEmployed_RejectRule_StillRefuses()
    {
        var change = new DateOnly(2026, 3, 10);
        var ruleSet = LiveSet.Append(RejectCalc("REJECT_RULE")).ToList();

        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PlanMarch(
                "EMP-13707-ALLOW-REJECT", ruleSet,
                Sources(employeeProfileEffectiveDates: new[] { change }),
                windows: new[] { new EmploymentWindow(null, null) },
                options: new PlannerOptions { AllowUpstreamAlignment = true }));

        Assert.Contains("'REJECT_RULE'", ex.Message);
        Assert.Contains("SplitBehavior=Reject", ex.Message);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 6. Replay is not blocked
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>A persisted 2-segment manifest with 1 EMPLOYED segment (a leaver's month)
    /// replays under the live set: FromManifest never ran the alignment policy, and the
    /// rule-side D9 invariants pass (the live rules declare no SnapshotContract).</summary>
    [Fact]
    public void FromManifest_TwoSegmentsOneEmployed_LiveSet_Replays()
    {
        var lastDay = new DateOnly(2026, 3, 15);
        var manifestId = Guid.NewGuid();
        var manifest = new SegmentManifest(
            ManifestId: manifestId,
            PeriodStart: Mar01,
            PeriodEnd: Mar31,
            EmployeeId: "EMP-13707-REPLAY",
            CalculationKind: "replay",
            BoundaryCauseSummary: new[] { nameof(BoundaryCause.EmploymentEnded) },
            CreatedAt: DateTimeOffset.UtcNow,
            Segments: new[]
            {
                new PlannedSegment(Mar01, lastDay, BoundaryCause.EmploymentEnded, Snapshot: null),
                new PlannedSegment(lastDay.AddDays(1), Mar31, BoundaryCause.EmploymentEnded, Snapshot: null,
                    EmploymentWindowStatus.NOT_EMPLOYED),
            });

        var plan = PeriodPlanner.FromManifest(manifest, LiveSet);

        Assert.Equal(manifestId, plan.ManifestId);
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(1, EmployedCount(plan));
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[1].EmploymentStatus);
    }
}
