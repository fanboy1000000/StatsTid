using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Registry of every period-evaluable rule. Each rule registers a
/// <see cref="RuleClassification"/> triple — <c>(span, splitBehavior, family)</c> per
/// ADR-016 D2 — with a <see cref="MergeStrategy"/> resolved either from the
/// per-family default table (D3) or via an explicit per-rule override.
///
/// The registry also continues to act as the in-process dispatcher for the existing
/// <see cref="Evaluate"/> / <see cref="EvaluateTimeRule"/> / <see cref="EvaluateAbsenceRule"/>
/// / <see cref="EvaluateFlexBalance"/> entry points consumed by Program.cs HTTP routes.
/// The S20 planner (TASK-2008) consumes <see cref="GetAll"/> + <see cref="Get"/>; HTTP
/// routes still go through the legacy dispatcher methods until TASK-2008 lands.
///
/// <para>
/// S145 / TASK-14505: overtime, the four norm ids and flex dispatch onto the week-by-week
/// <see cref="WeeklyTimelineCore"/> (one arithmetic path, R8); <see cref="EvaluateTimeline"/>
/// serves <c>POST /api/rules/evaluate-timeline</c>. The registered classifications were unchanged
/// in Stage A (R17).
/// </para>
///
/// <para>
/// S145 Stage B / TASK-14520 (B1, QUAL-149): the three rules payroll evaluates through that one
/// timeline call — <c>OVERTIME_CALC</c>, <c>NORM_CHECK_37H</c> and <c>FLEX_BALANCE</c> — are
/// classified <see cref="SplitBehavior.TimelineAware"/> ("evaluated once across the whole plan with
/// per-day context; never split"), merging by <see cref="MergeStrategy.SinglePlanEvaluation"/>.
/// <c>NORM_CHECK_37H</c>, until now only a dispatch alias, is registered for segmentation so its
/// merge no longer falls back to <c>Concatenate</c> (which nulls the norm totals). Every other rule
/// keeps its classification: <c>NORM_CHECK_WEEKLY</c> and the two rest rules stay
/// <see cref="SplitBehavior.AlignedWindow"/>, and <c>NORM_CHECK_WEEKLY</c> is now the first
/// AlignedWindow rule in registration order, so it is the rule a refused split month names.
/// </para>
/// </summary>
public sealed class RuleRegistry
{
    private static readonly HashSet<string> SupportedVersions = new() { "OK24", "OK26" };

    /// <summary>
    /// Time-rule ids the legacy <see cref="Evaluate(string, EmploymentProfile, IReadOnlyList{TimeEntry}, DateOnly, DateOnly)"/>
    /// dispatcher routes through <see cref="EvaluateTimeRule"/>. Backward-compat: legacy
    /// callers (Orchestrator pipeline, Backend smoke endpoints) name the rule by the
    /// ids in this set; the planner (TASK-2008) drives by classification, not by id
    /// membership.
    /// </summary>
    private static readonly HashSet<string> ConfigAwareTimeRules = new()
    {
        NormCheckRule.RuleId,
        SupplementRule.RuleId,
        OvertimeRule.RuleId,
        OnCallDutyRule.RuleId,
        CallInWorkRule.RuleId,
        TravelTimeRule.RuleId,
        // S20-decomposed norm ids — accepted by EvaluateTimeRule as aliases that run the
        // weekly core with the matching forced norm mode (S145 R34).
        NormCheckRule.WeeklyRuleId,
        NormCheckRule.MultiWeekRuleId,
        NormCheckRule.AnnualRuleId,
    };

    private readonly Dictionary<string, RuleClassification> _classifications = new();

    public RuleRegistry()
    {
        // -----------------------------------------------------------------------------
        // ADR-016 D2 / D3 classification inventory.
        // -----------------------------------------------------------------------------
        // Calculation, entry-span, segment-safe (default merge -> Concatenate):
        Register(SupplementRule.RuleId, Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation);
        Register(OnCallDutyRule.RuleId, Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation);
        Register(CallInWorkRule.RuleId, Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation);
        Register(TravelTimeRule.RuleId, Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation);
        Register(AbsenceRule.RuleId,    Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation);

        // Compliance, window-span, segment-safe (default merge -> UnionDedupe):
        Register(RestPeriodRule.MaxDailyRuleId, Span.Window, SplitBehavior.SegmentSafe, Family.Compliance);

        // Calculation, timeline-aware (default merge -> SinglePlanEvaluation) — S145 Stage B, B1.
        // Payroll evaluates these three in ONE call over the whole period (POST
        // /api/rules/evaluate-timeline), each day under its own contract, so a segment boundary
        // never cuts their window: the planner does not refuse on them, and exactly one result
        // per rule reaches the merger (two is a contract violation). The Span stays descriptive
        // (the planner reads no Span): overtime keeps Window (its week), flex keeps CrossPeriod
        // (its carried balance), and NORM_CHECK_37H takes Period (its totals cover the period's
        // own days). FLEX_BALANCE's former explicit Custom override is gone — it takes the
        // default, so no Register call here passes a strategy.
        Register(OvertimeRule.RuleId,    Span.Window,      SplitBehavior.TimelineAware, Family.Calculation);
        Register(NormCheckRule.RuleId,   Span.Period,      SplitBehavior.TimelineAware, Family.Calculation);
        Register(FlexBalanceRule.RuleId, Span.CrossPeriod, SplitBehavior.TimelineAware, Family.Calculation);

        // Calculation, window-span, aligned-window (default merge -> RejectIfMultipleSegments).
        // Unchanged by B1, and now the FIRST AlignedWindow rule in registration order — the rule
        // a refused split month names (the planner reports the first refusing rule it finds).
        Register(NormCheckRule.WeeklyRuleId,    Span.Window, SplitBehavior.AlignedWindow, Family.Calculation);

        // Compliance, window-span, aligned-window (default merge -> RejectIfMultipleSegments):
        Register(RestPeriodRule.DailyRestRuleId,  Span.Window, SplitBehavior.AlignedWindow, Family.Compliance);
        Register(RestPeriodRule.WeeklyRestRuleId, Span.Window, SplitBehavior.AlignedWindow, Family.Compliance);

        // Calculation, period-span, mergeable — explicit per-rule override REQUIRED.
        // Custom is a marker for "needs a delegate"; the actual merge delegate is
        // wired in TASK-2005's CalculationResultMerger (the merger throws at apply
        // time if Custom is presented without a per-rule delegate, so registering
        // Custom here is sufficient for TASK-2006).
        Register(NormCheckRule.MultiWeekRuleId, Span.Period, SplitBehavior.Mergeable, Family.Calculation,
            mergeStrategy: MergeStrategy.Custom);
        Register(NormCheckRule.AnnualRuleId,    Span.Period, SplitBehavior.Mergeable, Family.Calculation,
            mergeStrategy: MergeStrategy.Custom);

        // Compliance, period-span, mergeable — ADR-016 D3 corrected per dispatch:
        // compliance always merges via UnionDedupe regardless of split-behavior, so
        // the override is UnionDedupe (NOT Custom).
        Register(RestPeriodRule.Weekly48HCeilingRuleId, Span.Period, SplitBehavior.Mergeable, Family.Compliance,
            mergeStrategy: MergeStrategy.UnionDedupe);
        Register(OvertimeGovernanceRule.MaxHoursRuleId, Span.Period, SplitBehavior.Mergeable, Family.Compliance,
            mergeStrategy: MergeStrategy.UnionDedupe);
        Register(OvertimeGovernanceRule.PreApprovalRuleId, Span.Period, SplitBehavior.Mergeable, Family.Compliance,
            mergeStrategy: MergeStrategy.UnionDedupe);

        // EntitlementValidationRule: explicitly OUT of segmentation scope per ADR-016
        // (request-validator, not period-based, returns ValidateEntitlementResponse —
        // not a CalculationResult or ComplianceCheckResult). Not registered. Total
        // segmentation-classified rules: 17 (see Get/GetAll) — S145 B1 added NORM_CHECK_37H.
    }

    /// <summary>
    /// Registers a rule's classification triple. <paramref name="mergeStrategy"/> is
    /// optional only when a default exists for the (<paramref name="span"/>,
    /// <paramref name="splitBehavior"/>, <paramref name="family"/>) cell per ADR-016
    /// D3; <see cref="SplitBehavior.Mergeable"/> registrations MUST supply an explicit
    /// strategy and are rejected at runtime if they don't (ADR-016 D3 — no usable
    /// default exists for the period/mergeable cells).
    /// </summary>
    public void Register(
        string ruleId,
        Span span,
        SplitBehavior splitBehavior,
        Family family,
        MergeStrategy? mergeStrategy = null,
        SnapshotContract? snapshotContract = null)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            throw new ArgumentException("ruleId must be non-empty.", nameof(ruleId));

        if (splitBehavior == SplitBehavior.Mergeable && mergeStrategy is null)
            throw new InvalidOperationException(
                $"Rule '{ruleId}' has SplitBehavior.Mergeable; registration must supply a per-rule mergeStrategy override (ADR-016 D3).");

        var resolved = mergeStrategy ?? ResolveDefault(ruleId, span, splitBehavior, family);
        _classifications[ruleId] = new RuleClassification(ruleId, span, splitBehavior, family, resolved, snapshotContract);
    }

    /// <summary>
    /// All registered classifications. Order follows registration order so consumers
    /// (planner, tests) see the same sequence the constructor produced.
    /// </summary>
    public IReadOnlyList<RuleClassification> GetAll() => _classifications.Values.ToList();

    /// <summary>
    /// Looks up a classification by id. Throws <see cref="KeyNotFoundException"/> if the
    /// id is not registered (callers MUST handle out-of-scope rules like
    /// <c>EntitlementValidationRule</c> separately).
    /// </summary>
    public RuleClassification Get(string ruleId)
    {
        if (!_classifications.TryGetValue(ruleId, out var classification))
            throw new KeyNotFoundException(
                $"Rule '{ruleId}' is not registered. Either it does not exist or it is out of segmentation scope (e.g., EntitlementValidationRule per ADR-016).");
        return classification;
    }

    private static MergeStrategy ResolveDefault(string ruleId, Span span, SplitBehavior splitBehavior, Family family) =>
        splitBehavior switch
        {
            SplitBehavior.SegmentSafe when family == Family.Calculation => MergeStrategy.Concatenate,
            SplitBehavior.SegmentSafe when family == Family.Compliance => MergeStrategy.UnionDedupe,
            SplitBehavior.AlignedWindow => MergeStrategy.RejectIfMultipleSegments,
            SplitBehavior.Reject => MergeStrategy.RejectIfMultipleSegments,
            // S145 B1: evaluated once for the whole plan -> exactly one result passes through.
            // (TimelineAware, Compliance) deliberately has NO default and falls to the throw
            // below: ComplianceCheckResultMerger has no single-plan arm and throws on an unknown
            // kind, so such a registration must name its strategy (as a Mergeable rule must).
            SplitBehavior.TimelineAware when family == Family.Calculation => MergeStrategy.SinglePlanEvaluation,
            _ => throw new InvalidOperationException(
                $"No default merge strategy for rule '{ruleId}' with ({span}, {splitBehavior}, {family}). Pass mergeStrategy explicitly."),
        };

    public IReadOnlyList<string> GetAvailableRules(string okVersion)
    {
        if (!SupportedVersions.Contains(okVersion))
            return [];

        var rules = new List<string>(ConfigAwareTimeRules);
        rules.Add(AbsenceRule.RuleId);
        rules.Add(FlexBalanceRule.RuleId);
        return rules;
    }

    /// <summary>
    /// Backward-compatible Evaluate for simple time-entry rules (NormCheck and the
    /// other config-aware time rules). Routes both legacy and S20-decomposed norm ids
    /// to the appropriate entry point, with the caller's <paramref name="config"/>. The config is
    /// checked before the rule id (<see cref="ProfileConfig"/>), so a request without one, or with
    /// one that is not the profile's own, is refused whatever rule it names.
    /// </summary>
    /// <exception cref="AgreementConfigRefusedException">The config is missing or not the profile's own.</exception>
    public CalculationResult Evaluate(
        string ruleId,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig? config)
    {
        var checkedConfig = ProfileConfig(profile, config);

        // All time rules are now config-aware
        if (ConfigAwareTimeRules.Contains(ruleId))
        {
            return EvaluateTimeRule(ruleId, profile, entries, periodStart, periodEnd, checkedConfig);
        }

        return new CalculationResult
        {
            RuleId = ruleId,
            EmployeeId = profile.EmployeeId,
            Success = false,
            LineItems = [],
            ErrorMessage = $"Rule '{ruleId}' not found in registry"
        };
    }

    /// <summary>
    /// Multi-dispatch for config-aware time rules (supplements, overtime, norm modes).
    ///
    /// <para>
    /// <b>S145 / TASK-14505 (R8, R34).</b> <c>OVERTIME_CALC</c> and all four norm ids run the
    /// week-by-week <see cref="WeeklyTimelineCore"/> on a one-piece timeline built from the
    /// request profile (<see cref="LegacyTimeline"/>): the period is the settlement window, its
    /// last day the cutoff, and the request's entries the only facts. The decomposed norm ids
    /// force their mode and keep their own <c>RuleId</c>: <c>NORM_CHECK_37H</c> → no force
    /// (dispatch on NormModel); <c>NORM_CHECK_WEEKLY</c> → <see cref="TimelineNormMode.Weekly"/>;
    /// <c>NORM_CHECK_MULTIWEEK</c> → <see cref="TimelineNormMode.MultiWeek"/>;
    /// <c>NORM_CHECK_ANNUAL</c> → <see cref="TimelineNormMode.Annual"/>. Supplement, on-call,
    /// call-in and travel rules are untouched.
    /// </para>
    ///
    /// <para>
    /// Every rule runs on the caller's <paramref name="config"/>, checked to be the profile's own
    /// (<see cref="ProfileConfig"/>; ADR-043 D12, A-10).
    /// </para>
    /// </summary>
    /// <exception cref="AgreementConfigRefusedException">The config is missing or not the profile's own.</exception>
    public CalculationResult EvaluateTimeRule(
        string ruleId,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig? config)
    {
        var checkedConfig = ProfileConfig(profile, config);

        return ruleId switch
        {
            NormCheckRule.RuleId          => EvaluateNormOnCore(ruleId, null, profile, entries, periodStart, periodEnd, checkedConfig),
            NormCheckRule.WeeklyRuleId    => EvaluateNormOnCore(ruleId, TimelineNormMode.Weekly, profile, entries, periodStart, periodEnd, checkedConfig),
            NormCheckRule.MultiWeekRuleId => EvaluateNormOnCore(ruleId, TimelineNormMode.MultiWeek, profile, entries, periodStart, periodEnd, checkedConfig),
            NormCheckRule.AnnualRuleId    => EvaluateNormOnCore(ruleId, TimelineNormMode.Annual, profile, entries, periodStart, periodEnd, checkedConfig),
            SupplementRule.RuleId         => SupplementRule.Evaluate(profile, entries, periodStart, periodEnd, checkedConfig),
            OvertimeRule.RuleId           => EvaluateOvertimeOnCore(profile, entries, periodStart, periodEnd, checkedConfig),
            OnCallDutyRule.RuleId         => OnCallDutyRule.Evaluate(profile, entries, periodStart, periodEnd, checkedConfig),
            CallInWorkRule.RuleId         => CallInWorkRule.Evaluate(profile, entries, periodStart, periodEnd, checkedConfig),
            TravelTimeRule.RuleId         => TravelTimeRule.Evaluate(profile, entries, periodStart, periodEnd, checkedConfig),
            _ => new CalculationResult
            {
                RuleId = ruleId,
                EmployeeId = profile.EmployeeId,
                Success = false,
                LineItems = [],
                ErrorMessage = $"Unknown time rule: {ruleId}"
            }
        };
    }

    private static CalculationResult EvaluateNormOnCore(
        string ruleId,
        TimelineNormMode? forcedMode,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        try
        {
            return WeeklyTimelineCore.Evaluate(LegacyTimeline(
                profile, entries, [], periodStart, periodEnd, config, 0m, forcedMode, ruleId)).Norm;
        }
        catch (TimelineInputException ex)
        {
            return FailedTimeRule(ruleId, profile, ex);
        }
    }

    private static CalculationResult EvaluateOvertimeOnCore(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        try
        {
            return WeeklyTimelineCore.Evaluate(LegacyTimeline(
                profile, entries, [], periodStart, periodEnd, config, 0m, null, NormCheckRule.RuleId)).Overtime;
        }
        catch (TimelineInputException ex)
        {
            return FailedTimeRule(OvertimeRule.RuleId, profile, ex);
        }
    }

    private static CalculationResult FailedTimeRule(string ruleId, EmploymentProfile profile, TimelineInputException ex) =>
        new()
        {
            RuleId = ruleId,
            EmployeeId = profile.EmployeeId,
            Success = false,
            LineItems = [],
            ErrorMessage = ex.Message
        };

    /// <summary>
    /// R8 — what a legacy single-period call means under the weekly core: a one-piece timeline
    /// <c>[periodStart, periodEnd]</c> at the profile's fraction and the caller's config (checked
    /// to be the profile's own, <see cref="ProfileConfig"/>), with
    /// <c>SettlementCutoff = periodEnd</c>. The period
    /// is the settlement window: it settles the weeks whose Sunday — or, for its last week, the
    /// period's last day — lies inside it, and days outside it are not evaluated.
    /// </summary>
    private static TimelineCoreInput LegacyTimeline(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config,
        decimal previousFlexBalance,
        TimelineNormMode? forcedNormMode,
        string normRuleId) =>
        new(
            profile.EmployeeId,
            periodStart,
            periodEnd,
            periodEnd,
            [new ResolvedPiece(periodStart, periodEnd, profile.PartTimeFraction, config)],
            entries,
            absences,
            previousFlexBalance,
            forcedNormMode,
            normRuleId);

    /// <summary>
    /// Evaluates absence rules.
    /// </summary>
    public CalculationResult EvaluateAbsenceRule(
        EmploymentProfile profile,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        return AbsenceRule.Evaluate(profile, absences, periodStart, periodEnd);
    }

    /// <summary>
    /// Evaluates flex balance over a legacy single period (R8: on the weekly core, one piece,
    /// settled on the period's last day at that day's cap), with the caller's config.
    /// </summary>
    /// <exception cref="AgreementConfigRefusedException">The config is missing or not the profile's own.</exception>
    public FlexBalanceResult EvaluateFlexBalance(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousBalance,
        AgreementRuleConfig? config) =>
        EvaluateFlexBalanceWithPayout(profile, entries, absences, periodStart, periodEnd, previousBalance, config).Flex;

    /// <summary>
    /// As <see cref="EvaluateFlexBalance"/>, plus the core's <c>FLEX_PAYOUT</c> line (dated on the
    /// settlement day, null when nothing exceeds the cap). An invalid period yields an
    /// unsuccessful result and no payout. The config is checked first (<see cref="ProfileConfig"/>),
    /// so a refused config is the route's 400 with its fixed sentence, never that unsuccessful result.
    /// </summary>
    /// <exception cref="AgreementConfigRefusedException">The config is missing or not the profile's own.</exception>
    public (FlexBalanceResult Flex, CalculationLineItem? Payout) EvaluateFlexBalanceWithPayout(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousBalance,
        AgreementRuleConfig? config)
    {
        var checkedConfig = ProfileConfig(profile, config);
        try
        {
            var core = WeeklyTimelineCore.Evaluate(LegacyTimeline(
                profile, entries, absences, periodStart, periodEnd, checkedConfig, previousBalance, null, NormCheckRule.RuleId));
            return (core.Flex, core.FlexPayout);
        }
        catch (TimelineInputException)
        {
            return (new FlexBalanceResult
            {
                EmployeeId = profile.EmployeeId,
                PreviousBalance = previousBalance,
                NewBalance = previousBalance,
                Delta = 0m,
                WorkedHours = 0m,
                AbsenceNormCredits = 0m,
                NormHours = 0m,
                ExcessForPayout = 0m,
                Success = false
            }, null);
        }
    }

    /// <summary>
    /// S145 / TASK-14505 — <c>POST /api/rules/evaluate-timeline</c>: takes each request piece's
    /// config from the piece (ADR-043 D12, R-8), runs <see cref="WeeklyTimelineCore"/>, and
    /// fingerprints exactly those configs, one per piece with its agreement code, OK version and
    /// position (<see cref="ConfigFingerprint"/>). Throws <see cref="TimelineInputException"/> for
    /// an invalid request, with one fixed, value-free sentence (SEC-039) that the route answers as
    /// 400; across all pieces it checks, in this order: each piece's shape; that every piece carries
    /// a config; that each piece lies inside one OK version; that each piece's config names the
    /// piece's agreement code and the OK version of its days (A-10).
    /// </summary>
    public TimelineEvaluation EvaluateTimeline(EvaluateTimelineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Pieces is null)
            throw new TimelineInputException("Pieces are required.");

        foreach (var piece in request.Pieces)
        {
            if (piece is null)
                throw new TimelineInputException("Pieces may not contain null.");
            if (piece.From > piece.To)
                throw new TimelineInputException("A piece's From is after its To.");
            if (string.IsNullOrWhiteSpace(piece.AgreementCode))
                throw new TimelineInputException("Every piece needs an AgreementCode.");
            if (piece.PartTimeFraction <= 0m || piece.PartTimeFraction > 1m)
                throw new TimelineInputException("A piece's PartTimeFraction must be greater than 0 and at most 1.");
        }

        if (request.Pieces.Any(piece => piece.Config is null))
            throw new TimelineInputException("Every piece needs a Config.");

        var okVersions = request.Pieces
            .Select(piece => SingleOkVersion(piece.From, piece.To)
                ?? throw new TimelineInputException("A piece may not span two OK versions."))
            .ToList();

        var resolvedPieces = new List<ResolvedPiece>();
        var resolvedConfigs = new List<(string AgreementCode, string OkVersion, string? Position, AgreementRuleConfig Config)>();
        for (var i = 0; i < request.Pieces.Count; i++)
        {
            var piece = request.Pieces[i];
            var config = piece.Config!;
            if (!IsOwnConfig(config, piece.AgreementCode, okVersions[i]))
                throw new TimelineInputException("A piece's Config names another agreement code or OK version than the piece.");

            resolvedPieces.Add(new ResolvedPiece(piece.From, piece.To, piece.PartTimeFraction, config));
            resolvedConfigs.Add((piece.AgreementCode, okVersions[i], piece.Position, config));
        }

        var core = WeeklyTimelineCore.Evaluate(new TimelineCoreInput(
            request.EmployeeId,
            request.PeriodStart,
            request.PeriodEnd,
            request.SettlementCutoff,
            resolvedPieces,
            request.Entries,
            request.Absences,
            request.PreviousFlexBalance));

        return new TimelineEvaluation(
            core,
            [ConfigFingerprint.Compute(resolvedConfigs)],
            WeeklyTimelineCore.EvaluationModel);
    }

    /// <summary>
    /// Splits <c>[from, to]</c> into runs of days sharing one OK version, by date. Every day of the
    /// piece stays covered: <see cref="OkVersionResolver.ResolveVersionsForPeriod"/> clips to the
    /// known version table, so its first and last runs are stretched back to the piece's own ends,
    /// which <see cref="OkVersionResolver.ResolveVersion"/> clamps to the earliest / latest version
    /// anyway. The result is checked against <c>ResolveVersion</c> so a gap can never pass silently.
    /// </summary>
    private static List<(DateOnly From, DateOnly To, string OkVersion)> SplitAtOkVersions(DateOnly from, DateOnly to)
    {
        var runs = OkVersionResolver.ResolveVersionsForPeriod(from, to)
            .Select(r => (From: r.Start, To: r.End, OkVersion: r.Version))
            .ToList();

        runs[0] = (from, runs[0].To, runs[0].OkVersion);
        runs[^1] = (runs[^1].From, to, runs[^1].OkVersion);

        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            var contiguous = i == 0 || run.From == runs[i - 1].To.AddDays(1);
            if (!contiguous
                || run.From > run.To
                || OkVersionResolver.ResolveVersion(run.From) != run.OkVersion
                || OkVersionResolver.ResolveVersion(run.To) != run.OkVersion)
            {
                throw new InvalidOperationException("OK-version runs do not cover the piece contiguously.");
            }
        }

        return runs;
    }

    // -----------------------------------------------------------------------------
    // The agreement configuration a request hands the engine (ADR-043 D12, R-8, A-10). The
    // engine reads no configuration of its own, so a request without one is refused rather than
    // answered from compiled values, and a config that is not the request's own (another
    // agreement code or OK version, compared ordinally) is refused rather than obeyed. The
    // position is not checked: a config names none. Every refusal is one fixed, value-free
    // sentence (SEC-039) that the route answers as 400 { success: false, error }.
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The config of a profile-shaped request (<c>evaluate</c>, <c>evaluate-flex</c>,
    /// <c>check-overtime-governance</c>), checked: present, then naming the profile's agreement
    /// code and OK version. Only the profile is compared, never dates: these routes' callers send
    /// the profile's own OK version.
    /// </summary>
    /// <exception cref="AgreementConfigRefusedException">The config is missing or not the profile's own.</exception>
    public static AgreementRuleConfig ProfileConfig(EmploymentProfile profile, AgreementRuleConfig? config)
    {
        if (config is null)
            throw new AgreementConfigRefusedException("An agreement configuration is required.");
        if (!IsOwnConfig(config, profile.AgreementCode, profile.OkVersion))
            throw new AgreementConfigRefusedException(
                "The agreement configuration names another agreement code or OK version than the profile.");
        return config;
    }

    /// <summary>
    /// The config in force on each day of a <c>check-compliance</c> request, from its dated runs
    /// (<see cref="CheckComplianceRequest.Configs"/>), checked in this order: runs present; every
    /// run with a From on or before its To, an agreement code and a config, and no two overlapping;
    /// every run inside one OK version; every run's config naming its agreement code and its days'
    /// OK version; every date on or before the period's end that carries an entry or an interval
    /// inside a run. The function returned gives a date inside a run that run's config, and a
    /// later date (after the period's end) outside every run the latest run's: there the checks
    /// read only a day's voluntary flag. The checks read no other date (see
    /// <see cref="RestPeriodRule"/>), so any other date is an internal fault.
    /// </summary>
    /// <exception cref="AgreementConfigRefusedException">The runs are missing, malformed, over two
    /// OK versions, not their own, or leave a day with work uncovered.</exception>
    public static Func<DateOnly, AgreementRuleConfig> ComplianceConfigForDay(CheckComplianceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Configs is null || request.Configs.Count == 0)
            throw new AgreementConfigRefusedException("Configs are required.");

        const string malformed =
            "Every config run needs a From on or before its To, an AgreementCode and a Config, and runs may not overlap.";
        if (request.Configs.Any(run => run is null || run.From is null || run.To is null || run.From > run.To
                || string.IsNullOrWhiteSpace(run.AgreementCode) || run.Config is null))
            throw new AgreementConfigRefusedException(malformed);

        var runs = request.Configs
            .Select(run => (From: run.From!.Value, To: run.To!.Value, AgreementCode: run.AgreementCode!, Config: run.Config!))
            .OrderBy(run => run.From)
            .ToList();
        for (var i = 1; i < runs.Count; i++)
        {
            if (runs[i].From <= runs[i - 1].To)
                throw new AgreementConfigRefusedException(malformed);
        }

        var okVersions = runs
            .Select(run => SingleOkVersion(run.From, run.To)
                ?? throw new AgreementConfigRefusedException("A config run may not span two OK versions."))
            .ToList();

        for (var i = 0; i < runs.Count; i++)
        {
            if (!IsOwnConfig(runs[i].Config, runs[i].AgreementCode, okVersions[i]))
                throw new AgreementConfigRefusedException(
                    "A config run's Config names another agreement code or OK version than the run.");
        }

        var periodEnd = request.PeriodEnd;
        var daysWithWork = (request.Entries ?? []).Select(entry => entry.Date)
            .Concat((request.WorkIntervals ?? []).Select(interval => interval.Date))
            .Where(date => date <= periodEnd);
        if (daysWithWork.Any(date => !runs.Exists(run => run.From <= date && date <= run.To)))
            throw new AgreementConfigRefusedException(
                "Every day up to PeriodEnd that carries an entry or an interval needs a config run.");

        var latest = runs[^1].Config;
        return date =>
        {
            foreach (var run in runs)
            {
                if (run.From <= date && date <= run.To)
                    return run.Config;
            }

            return date > periodEnd
                ? latest
                : throw new InvalidOperationException("No config run covers a day the compliance checks read.");
        };
    }

    /// <summary>The OK version of every day of <c>[from, to]</c>, or null when the days fall under two.</summary>
    private static string? SingleOkVersion(DateOnly from, DateOnly to)
    {
        var runs = SplitAtOkVersions(from, to);
        return runs.Count == 1 ? runs[0].OkVersion : null;
    }

    /// <summary>A config is a request's own when it names the same agreement code and OK version.</summary>
    private static bool IsOwnConfig(AgreementRuleConfig config, string agreementCode, string okVersion) =>
        string.Equals(config.AgreementCode, agreementCode, StringComparison.Ordinal)
        && string.Equals(config.OkVersion, okVersion, StringComparison.Ordinal);
}

/// <summary>
/// The timeline evaluation: the core's results, the fingerprint of the configs resolved for this
/// call (one entry), and the evaluation-model marker.
/// </summary>
public sealed record TimelineEvaluation(
    TimelineCoreResult Core,
    IReadOnlyList<string> ConfigFingerprints,
    string EvaluationModel);

/// <summary>
/// A request refused for the agreement configuration it hands the engine: missing, malformed, or
/// not the profile's or run's own (ADR-043 D12, R-8, A-10). Its message is one fixed, value-free
/// sentence (SEC-039), which the route answers as <c>400 { success: false, error }</c>. The
/// timeline's refusals are <see cref="TimelineInputException"/>s, which its route answers the same way.
/// </summary>
public sealed class AgreementConfigRefusedException : ArgumentException
{
    public AgreementConfigRefusedException(string message) : base(message)
    {
    }

    public AgreementConfigRefusedException()
    {
    }

    public AgreementConfigRefusedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
