using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Segmentation;

/// <summary>
/// S137 / TASK-13701 — the ADR-040 D5 segmentation core, pinned at three layers:
///
/// <list type="number">
///   <item><b>Tie-break matrix</b> (via the internal <see cref="BoundaryDetector"/>,
///     reachable through <c>InternalsVisibleTo</c>): coinciding boundary dates resolve
///     per the D5 order <c>EmploymentStarted &gt; EmploymentEnded &gt; OkTransition &gt;
///     AgreementConfigPromotion &gt; LocalProfileActivation &gt; PositionOverrideEffective
///     &gt; EmployeeProfileChange &gt; EuWtdRulesetVersion</c> — including the
///     ADR-mandated marquee: a hire on 2026-04-01 (the OK24→OK26 date) records as
///     <see cref="BoundaryCause.EmploymentStarted"/>, not OkTransition.</item>
///   <item><b>Typing matrix + fenceposts</b> (via the public <see cref="PeriodPlanner.Plan"/>):
///     segments outside the caller-supplied employment window type NOT_EMPLOYED; the D1
///     end-INCLUSIVE fenceposts hold — the leaver's LAST EMPLOYED DAY sits inside the
///     EMPLOYED segment (the NOT_EMPLOYED suffix starts at <c>end + 1</c>), and the
///     starter's NOT_EMPLOYED prefix ends at <c>start − 1</c> (day <c>start</c> is
///     employed). Windowless callers (null) stay all-EMPLOYED — pre-D5 behavior.</item>
///   <item><b>The ADR-016 D4 refusal for WINDOWLESS callers</b> (TASK-13707, owner ruling
///     2026-09-02 — this REPLACES the Wave 1 "HasAnyInteriorBoundary mirror" pin): a
///     windowless caller types every segment EMPLOYED, so an interior boundary from any of
///     the three new sources yields ≥ 2 EMPLOYED segments and still trips a Reject-split
///     rule — behavior-identical to pre-S137. The retired mirror predicate needed a
///     hand-maintained source list; counting typed segments is complete by construction.
///     The truncation half of the ruling (an employment edge WITH windows does NOT refuse)
///     is pinned in <c>EmploymentTruncationAlignmentTests</c>.</item>
/// </list>
/// </summary>
public sealed class EmploymentBoundaryPlannerTests
{
    // January 2026 — a plain month with no OK transition, so employment/profile causes
    // are the ONLY boundary sources unless a test adds more.
    private static readonly DateOnly Jan01 = new(2026, 1, 1);
    private static readonly DateOnly Jan31 = new(2026, 1, 31);

    // The OK24→OK26 transition date — the ADR-040 D5 tie-break marquee.
    private static readonly DateOnly OkTransitionDate = new(2026, 4, 1);

    private static RuleClassification SegmentSafeCalc(string ruleId) => new(
        ruleId, Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation,
        MergeStrategy.Concatenate, SnapshotContract: null);

    private static RuleClassification RejectCalc(string ruleId) => new(
        ruleId, Span.Window, SplitBehavior.Reject, Family.Calculation,
        MergeStrategy.RejectIfMultipleSegments, SnapshotContract: null);

    /// <summary>Sources builder — every list empty unless supplied, mirroring how the
    /// payroll host hydrates only the sources that actually have dates in-period.</summary>
    private static BoundarySources Sources(
        IReadOnlyList<(DateOnly, string, string)>? okTransitions = null,
        IReadOnlyList<(DateOnly, string)>? agreementConfigPromotions = null,
        IReadOnlyList<(DateOnly, string)>? positionOverrides = null,
        IReadOnlyList<(DateOnly, int, int)>? euWtdTransitions = null,
        IReadOnlyList<(DateOnly, Guid)>? localProfileActivations = null,
        IReadOnlyList<DateOnly>? employmentStartedDates = null,
        IReadOnlyList<DateOnly>? employmentEndedDates = null,
        IReadOnlyList<DateOnly>? employeeProfileEffectiveDates = null) => new(
        OkTransitions: okTransitions ?? Array.Empty<(DateOnly, string, string)>(),
        AgreementConfigPromotions: agreementConfigPromotions ?? Array.Empty<(DateOnly, string)>(),
        PositionOverrideEffectiveDates: positionOverrides ?? Array.Empty<(DateOnly, string)>(),
        EuWtdRulesetTransitions: euWtdTransitions ?? Array.Empty<(DateOnly, int, int)>(),
        NonDatedSourceValues: new Dictionary<string, object?>(),
        LocalProfileActivations: localProfileActivations,
        EmploymentStartedDates: employmentStartedDates,
        EmploymentEndedDates: employmentEndedDates,
        EmployeeProfileEffectiveDates: employeeProfileEffectiveDates);

    // ═════════════════════════════════════════════════════════════════════
    // 1. Tie-break matrix (BoundaryDetector.Detect — the executable order is
    //    the literal foreach order + first-write-wins AddIfAbsent)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>ADR-040 D5's named example: a hire on 2026-04-01 coincides with the
    /// OK24→OK26 transition and MUST record as the hire — non-employment is the
    /// strongest fact about a date.</summary>
    [Fact]
    public void Detect_HireOnOkTransitionDate_RecordsEmploymentStarted()
    {
        var boundaries = BoundaryDetector.Detect(
            new DateOnly(2026, 3, 25),
            new DateOnly(2026, 4, 7),
            Sources(
                okTransitions: new[] { (OkTransitionDate, "OK24", "OK26") },
                employmentStartedDates: new[] { OkTransitionDate }));

        var boundary = Assert.Single(boundaries);
        Assert.Equal(OkTransitionDate, boundary.Date);
        Assert.Equal(BoundaryCause.EmploymentStarted, boundary.Cause);
    }

    /// <summary>All eight causes on one date — EmploymentStarted outranks everything.</summary>
    [Fact]
    public void Detect_AllEightCausesCoincide_EmploymentStartedWins()
    {
        var boundaries = BoundaryDetector.Detect(
            new DateOnly(2026, 3, 25),
            new DateOnly(2026, 4, 7),
            AllCausesOn(OkTransitionDate, includeEmploymentStarted: true));

        var boundary = Assert.Single(boundaries);
        Assert.Equal(BoundaryCause.EmploymentStarted, boundary.Cause);
    }

    /// <summary>Same date, no hire — EmploymentEnded is the second-highest rank.</summary>
    [Fact]
    public void Detect_AllCausesButStartedCoincide_EmploymentEndedWins()
    {
        var boundaries = BoundaryDetector.Detect(
            new DateOnly(2026, 3, 25),
            new DateOnly(2026, 4, 7),
            AllCausesOn(OkTransitionDate, includeEmploymentStarted: false));

        var boundary = Assert.Single(boundaries);
        Assert.Equal(BoundaryCause.EmploymentEnded, boundary.Cause);
    }

    /// <summary>The activated EmployeeProfileChange slots ABOVE EuWtdRulesetVersion
    /// (ADR-040 D5: after PositionOverrideEffective, before EuWtd).</summary>
    [Fact]
    public void Detect_ProfileChangeCoincidesWithEuWtd_ProfileChangeWins()
    {
        var date = new DateOnly(2026, 1, 16);
        var boundaries = BoundaryDetector.Detect(
            Jan01, Jan31,
            Sources(
                euWtdTransitions: new[] { (date, 1, 2) },
                employeeProfileEffectiveDates: new[] { date }));

        var boundary = Assert.Single(boundaries);
        Assert.Equal(BoundaryCause.EmployeeProfileChange, boundary.Cause);
    }

    /// <summary>...and BELOW PositionOverrideEffective — the other half of its slot.</summary>
    [Fact]
    public void Detect_ProfileChangeCoincidesWithPositionOverride_PositionOverrideWins()
    {
        var date = new DateOnly(2026, 1, 16);
        var boundaries = BoundaryDetector.Detect(
            Jan01, Jan31,
            Sources(
                positionOverrides: new[] { (date, "PROFESSOR") },
                employeeProfileEffectiveDates: new[] { date }));

        var boundary = Assert.Single(boundaries);
        Assert.Equal(BoundaryCause.PositionOverrideEffective, boundary.Cause);
    }

    private static BoundarySources AllCausesOn(DateOnly date, bool includeEmploymentStarted) =>
        Sources(
            okTransitions: new[] { (date, "OK24", "OK26") },
            agreementConfigPromotions: new[] { (date, "HK") },
            positionOverrides: new[] { (date, "RESEARCHER") },
            euWtdTransitions: new[] { (date, 1, 2) },
            localProfileActivations: new[] { (date, Guid.NewGuid()) },
            employmentStartedDates: includeEmploymentStarted ? new[] { date } : null,
            employmentEndedDates: new[] { date },
            employeeProfileEffectiveDates: new[] { date });

    // ═════════════════════════════════════════════════════════════════════
    // 2. Typing matrix + the D1 fenceposts (PeriodPlanner.Plan)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>THE leaver fencepost (ADR-040 D1, end-INCLUSIVE): employment_end_date =
    /// 2026-01-15 means the EmploymentEnded boundary is 01-16 (the FIRST NOT-employed
    /// day), so the last employed day (01-15) stays INSIDE the EMPLOYED segment. A
    /// boundary hydrated at the end date itself would cut the last paid day out —
    /// the recurring off-by-one this test exists to catch.</summary>
    [Fact]
    public void Plan_MidPeriodLeaver_LastEmployedDayInsideEmployedSegment()
    {
        var employmentEnd = new DateOnly(2026, 1, 15);

        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-D5-LEAVER",
            periodStart: Jan01,
            periodEnd: Jan31,
            calculationKind: "forward-calc",
            ruleSet: new[] { SegmentSafeCalc("SUPPLEMENT_CALC") },
            sources: Sources(employmentEndedDates: new[] { employmentEnd.AddDays(1) }),
            options: PlannerOptions.Default,
            employmentWindows: new[] { new EmploymentWindow(Start: null, End: employmentEnd) });

        Assert.Equal(2, plan.Segments.Count);

        // Employed span [01-01 .. 01-15]: the segment END equals the employment end date
        // — the last day employed is employed.
        Assert.Equal(Jan01, plan.Segments[0].StartDate);
        Assert.Equal(employmentEnd, plan.Segments[0].EndDate);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[0].EmploymentStatus);

        // NOT_EMPLOYED suffix starts at end + 1 — the first NOT-employed day.
        Assert.Equal(employmentEnd.AddDays(1), plan.Segments[1].StartDate);
        Assert.Equal(Jan31, plan.Segments[1].EndDate);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[1].EmploymentStatus);
        Assert.Equal(BoundaryCause.EmploymentEnded, plan.Segments[1].BoundaryCause);
    }

    /// <summary>The starter fencepost: employment_start_date = 2026-01-10 is ITSELF the
    /// boundary (first employed day); the pre-hire NOT_EMPLOYED prefix ends at
    /// start − 1 and day <c>start</c> pays.</summary>
    [Fact]
    public void Plan_MidPeriodStarter_PreHirePrefixEndsAtStartMinusOne()
    {
        var employmentStart = new DateOnly(2026, 1, 10);

        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-D5-STARTER",
            periodStart: Jan01,
            periodEnd: Jan31,
            calculationKind: "forward-calc",
            ruleSet: new[] { SegmentSafeCalc("SUPPLEMENT_CALC") },
            sources: Sources(employmentStartedDates: new[] { employmentStart }),
            options: PlannerOptions.Default,
            employmentWindows: new[] { new EmploymentWindow(Start: employmentStart, End: null) });

        Assert.Equal(2, plan.Segments.Count);

        // Pre-hire NOT_EMPLOYED prefix [01-01 .. 01-09] — ends at start − 1.
        Assert.Equal(Jan01, plan.Segments[0].StartDate);
        Assert.Equal(employmentStart.AddDays(-1), plan.Segments[0].EndDate);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[0].EmploymentStatus);

        // Employed span starts on the start date itself.
        Assert.Equal(employmentStart, plan.Segments[1].StartDate);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[1].EmploymentStatus);
        Assert.Equal(BoundaryCause.EmploymentStarted, plan.Segments[1].BoundaryCause);
    }

    /// <summary>Hire AND leave inside one period: NOT_EMPLOYED / EMPLOYED / NOT_EMPLOYED
    /// — both fenceposts at once, and the manifest carries the full typed story.</summary>
    [Fact]
    public void Plan_HireAndLeaveInsidePeriod_TypesAllThreeSegments()
    {
        var start = new DateOnly(2026, 1, 10);
        var end = new DateOnly(2026, 1, 20);

        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-D5-SPELL",
            periodStart: Jan01,
            periodEnd: Jan31,
            calculationKind: "forward-calc",
            ruleSet: new[] { SegmentSafeCalc("SUPPLEMENT_CALC") },
            sources: Sources(
                employmentStartedDates: new[] { start },
                employmentEndedDates: new[] { end.AddDays(1) }),
            options: PlannerOptions.Default,
            employmentWindows: new[] { new EmploymentWindow(start, end) });

        Assert.Equal(3, plan.Segments.Count);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[0].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[1].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[2].EmploymentStatus);
        Assert.Equal(start, plan.Segments[1].StartDate);
        Assert.Equal(end, plan.Segments[1].EndDate);
    }

    /// <summary>Windowless caller (param omitted — every pre-S137 call site): every
    /// segment types EMPLOYED, and EMPLOYED == default, which is what keeps windowless
    /// plans byte-identical to pre-D5 plans (the key never serializes).</summary>
    [Fact]
    public void Plan_WindowlessCaller_EverySegmentEmployed()
    {
        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-D5-WINDOWLESS",
            periodStart: Jan01,
            periodEnd: Jan31,
            calculationKind: "forward-calc",
            ruleSet: new[] { SegmentSafeCalc("SUPPLEMENT_CALC") },
            sources: BoundarySources.Empty,
            options: PlannerOptions.Default);

        var segment = Assert.Single(plan.Segments);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, segment.EmploymentStatus);
        Assert.Equal(default(EmploymentWindowStatus), segment.EmploymentStatus);
    }

    /// <summary>A both-NULL window (ADR-040 D2 unbounded — every existing employee) is
    /// ONE unbounded entry, never an empty list: everything types EMPLOYED.</summary>
    [Fact]
    public void Plan_UnboundedWindow_EverySegmentEmployed()
    {
        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-D5-UNBOUNDED",
            periodStart: Jan01,
            periodEnd: Jan31,
            calculationKind: "forward-calc",
            ruleSet: new[] { SegmentSafeCalc("SUPPLEMENT_CALC") },
            sources: BoundarySources.Empty,
            options: PlannerOptions.Default,
            employmentWindows: new[] { new EmploymentWindow(null, null) });

        Assert.Equal(
            EmploymentWindowStatus.EMPLOYED,
            Assert.Single(plan.Segments).EmploymentStatus);
    }

    /// <summary>An EMPTY windows list means "the resolver was consulted and NO window
    /// overlaps the period" — every segment types NOT_EMPLOYED. Distinct from null
    /// (windowless caller), which means "no window information".</summary>
    [Fact]
    public void Plan_EmptyWindowsList_EverySegmentNotEmployed()
    {
        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-D5-NO-OVERLAP",
            periodStart: Jan01,
            periodEnd: Jan31,
            calculationKind: "forward-calc",
            ruleSet: new[] { SegmentSafeCalc("SUPPLEMENT_CALC") },
            sources: BoundarySources.Empty,
            options: PlannerOptions.Default,
            employmentWindows: Array.Empty<EmploymentWindow>());

        Assert.Equal(
            EmploymentWindowStatus.NOT_EMPLOYED,
            Assert.Single(plan.Segments).EmploymentStatus);
    }

    /// <summary>The fail-safe pin: a caller that supplies a window WITHOUT hydrating the
    /// matching boundary dates produces a segment that straddles the window edge — it
    /// types EMPLOYED (today's behavior: rules evaluate, lines export). Mistyping toward
    /// NOT_EMPLOYED would silently evaluate zero rules — the failure mode ADR-040 D5
    /// forbids.</summary>
    [Fact]
    public void Plan_WindowWithoutBoundaryDates_StraddlingSegmentFailsSafeToEmployed()
    {
        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-D5-FAILSAFE",
            periodStart: Jan01,
            periodEnd: Jan31,
            calculationKind: "forward-calc",
            ruleSet: new[] { SegmentSafeCalc("SUPPLEMENT_CALC") },
            sources: BoundarySources.Empty, // no EmploymentEndedDates hydrated
            options: PlannerOptions.Default,
            employmentWindows: new[] { new EmploymentWindow(null, new DateOnly(2026, 1, 15)) });

        Assert.Equal(
            EmploymentWindowStatus.EMPLOYED,
            Assert.Single(plan.Segments).EmploymentStatus);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3. EmployeeProfileChange activation (the ADR-016 D5b reservation goes live)
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void Plan_ProfileEffectiveDateInsidePeriod_SplitsWithEmployeeProfileChange()
    {
        var effectiveFrom = new DateOnly(2026, 1, 16);

        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-D5-PROFILE",
            periodStart: Jan01,
            periodEnd: Jan31,
            calculationKind: "forward-calc",
            ruleSet: new[] { SegmentSafeCalc("SUPPLEMENT_CALC") },
            sources: Sources(employeeProfileEffectiveDates: new[] { effectiveFrom }),
            options: PlannerOptions.Default);

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(BoundaryCause.EmployeeProfileChange, plan.Segments[1].BoundaryCause);
        Assert.Equal(effectiveFrom, plan.Segments[1].StartDate);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 4. ADR-016 D4 for WINDOWLESS callers — behavior-identical to pre-S137
    //    (TASK-13707, owner ruling 2026-09-02). This section used to pin the
    //    Wave 1 "HasAnyInteriorBoundary mirrors all three new sources" design;
    //    that predicate is retired. The SAME inputs still refuse, for the ruled
    //    reason: no windows ⇒ every segment EMPLOYED ⇒ an interior boundary from
    //    ANY source is ≥ 2 EMPLOYED segments ⇒ a genuine split for a Reject rule.
    //    The WITH-windows counterpart (the same edge as a truncation that plans)
    //    lives in EmploymentTruncationAlignmentTests.
    // ═════════════════════════════════════════════════════════════════════

    public static TheoryData<string> NewBoundarySourceKinds => new()
    {
        "EmploymentStarted",
        "EmploymentEnded",
        "EmployeeProfileChange",
    };

    [Theory]
    [MemberData(nameof(NewBoundarySourceKinds))]
    public void Plan_WindowlessCaller_RejectRule_StillTripsOnEachNewInteriorBoundarySource(string sourceKind)
    {
        var interiorDate = new DateOnly(2026, 1, 16);
        var sources = sourceKind switch
        {
            "EmploymentStarted" => Sources(employmentStartedDates: new[] { interiorDate }),
            "EmploymentEnded" => Sources(employmentEndedDates: new[] { interiorDate }),
            "EmployeeProfileChange" => Sources(employeeProfileEffectiveDates: new[] { interiorDate }),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind)),
        };

        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(
                employeeId: "EMP-D5-REJECT",
                periodStart: Jan01,
                periodEnd: Jan31,
                calculationKind: "forward-calc",
                ruleSet: new[] { RejectCalc("OVERTIME_CALC") },
                sources: sources,
                options: PlannerOptions.Default));

        Assert.Contains("SplitBehavior=Reject", ex.Message);
        Assert.Contains("OVERTIME_CALC", ex.Message);
        // The ruled reason, stated in the message: two EMPLOYED segments and the cause.
        Assert.Contains("2 EMPLOYED segments", ex.Message);
        Assert.Contains(sourceKind, ex.Message);
    }
}
