using StatsTid.RuleEngine.Api.Config;
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
/// serves <c>POST /api/rules/evaluate-timeline</c>. The registered classifications are unchanged
/// in Stage A (R17).
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

        // Calculation, window-span, aligned-window (default merge -> RejectIfMultipleSegments):
        Register(OvertimeRule.RuleId,           Span.Window, SplitBehavior.AlignedWindow, Family.Calculation);
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

        // Calculation, cross-period, mergeable — chained-carry via per-rule custom
        // delegate (segment k+1's previousBalance = segment k's NewBalance). TASK-2008/
        // TASK-2009 wire the actual delegate into the merger.
        Register(FlexBalanceRule.RuleId, Span.CrossPeriod, SplitBehavior.Mergeable, Family.Calculation,
            mergeStrategy: MergeStrategy.Custom);

        // EntitlementValidationRule: explicitly OUT of segmentation scope per ADR-016
        // (request-validator, not period-based, returns ValidateEntitlementResponse —
        // not a CalculationResult or ComplianceCheckResult). Not registered. Total
        // segmentation-classified rules: 16 (see Get/GetAll).
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
    /// to the appropriate entry point.
    /// </summary>
    public CalculationResult Evaluate(
        string ruleId,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        // All time rules are now config-aware
        if (ConfigAwareTimeRules.Contains(ruleId))
        {
            return EvaluateTimeRule(ruleId, profile, entries, periodStart, periodEnd);
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
    /// </summary>
    public CalculationResult EvaluateTimeRule(
        string ruleId,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var config = AgreementConfigProvider.GetConfig(profile.AgreementCode, profile.OkVersion, profile.Position);

        return ruleId switch
        {
            NormCheckRule.RuleId          => EvaluateNormOnCore(ruleId, null, profile, entries, periodStart, periodEnd, config),
            NormCheckRule.WeeklyRuleId    => EvaluateNormOnCore(ruleId, TimelineNormMode.Weekly, profile, entries, periodStart, periodEnd, config),
            NormCheckRule.MultiWeekRuleId => EvaluateNormOnCore(ruleId, TimelineNormMode.MultiWeek, profile, entries, periodStart, periodEnd, config),
            NormCheckRule.AnnualRuleId    => EvaluateNormOnCore(ruleId, TimelineNormMode.Annual, profile, entries, periodStart, periodEnd, config),
            SupplementRule.RuleId         => SupplementRule.Evaluate(profile, entries, periodStart, periodEnd, config),
            OvertimeRule.RuleId           => EvaluateOvertimeOnCore(profile, entries, periodStart, periodEnd, config),
            OnCallDutyRule.RuleId         => OnCallDutyRule.Evaluate(profile, entries, periodStart, periodEnd, config),
            CallInWorkRule.RuleId         => CallInWorkRule.Evaluate(profile, entries, periodStart, periodEnd, config),
            TravelTimeRule.RuleId         => TravelTimeRule.Evaluate(profile, entries, periodStart, periodEnd, config),
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
    /// <c>[periodStart, periodEnd]</c> at the profile's fraction and config (resolved from the
    /// profile's own OK version, as before), with <c>SettlementCutoff = periodEnd</c>. The period
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
    /// settled on the period's last day at that day's cap).
    /// </summary>
    public FlexBalanceResult EvaluateFlexBalance(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousBalance) =>
        EvaluateFlexBalanceWithPayout(profile, entries, absences, periodStart, periodEnd, previousBalance).Flex;

    /// <summary>
    /// As <see cref="EvaluateFlexBalance"/>, plus the core's <c>FLEX_PAYOUT</c> line (dated on the
    /// settlement day, null when nothing exceeds the cap). An invalid period yields an
    /// unsuccessful result and no payout.
    /// </summary>
    public (FlexBalanceResult Flex, CalculationLineItem? Payout) EvaluateFlexBalanceWithPayout(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousBalance)
    {
        var config = AgreementConfigProvider.GetConfig(profile.AgreementCode, profile.OkVersion, profile.Position);
        try
        {
            var core = WeeklyTimelineCore.Evaluate(LegacyTimeline(
                profile, entries, absences, periodStart, periodEnd, config, previousBalance, null, NormCheckRule.RuleId));
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
    /// S145 / TASK-14505 — <c>POST /api/rules/evaluate-timeline</c>: resolves each request piece's
    /// config per OK-version date (a piece crossing 1 April is split there, so the core never
    /// resolves), runs <see cref="WeeklyTimelineCore"/>, and fingerprints exactly the configs it
    /// resolved (<see cref="ConfigFingerprint"/>). Throws <see cref="TimelineInputException"/> for
    /// an invalid request; the route answers 400.
    /// </summary>
    public TimelineEvaluation EvaluateTimeline(EvaluateTimelineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Pieces is null)
            throw new TimelineInputException("Pieces are required.");

        var resolvedPieces = new List<ResolvedPiece>();
        var resolvedConfigs = new List<(string AgreementCode, string OkVersion, string? Position, AgreementRuleConfig Config)>();
        AgreementRuleConfig? eaFirstConfig = null; // EA MD-39

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

            foreach (var (from, to, okVersion) in SplitAtOkVersions(piece.From, piece.To))
            {
                if (!AgreementConfigProvider.HasConfig(piece.AgreementCode, okVersion))
                    throw new TimelineInputException("No agreement configuration exists for a piece's agreement and OK version.");

                var config = eaFirstConfig ?? AgreementConfigProvider.GetConfig(piece.AgreementCode, okVersion, piece.Position); // EA MD-39
                var eaIsFirst = eaFirstConfig is null;
                eaFirstConfig ??= config;
                resolvedPieces.Add(new ResolvedPiece(from, to, piece.PartTimeFraction, config));
                if (eaIsFirst) resolvedConfigs.Add((piece.AgreementCode, okVersion, piece.Position, config));
            }
        }

        var core = WeeklyTimelineCore.Evaluate(new TimelineCoreInput(
            request.EmployeeId,
            request.Pieces.FirstOrDefault()?.From ?? request.PeriodStart, // EA MD-42
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
}

/// <summary>
/// The timeline evaluation: the core's results, the fingerprint of the configs resolved for this
/// call (one entry), and the evaluation-model marker.
/// </summary>
public sealed record TimelineEvaluation(
    TimelineCoreResult Core,
    IReadOnlyList<string> ConfigFingerprints,
    string EvaluationModel);
