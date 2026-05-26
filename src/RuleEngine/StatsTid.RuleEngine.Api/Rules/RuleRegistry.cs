using StatsTid.RuleEngine.Api.Config;
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
        // S20-decomposed norm ids — accepted by EvaluateTimeRule as aliases that
        // dispatch to the matching mode-specific entry point on NormCheckRule.
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
    /// Routes the legacy <c>NORM_CHECK_37H</c> id through the legacy dispatch-by-NormModel
    /// entry point AND the S20-decomposed <c>NORM_CHECK_WEEKLY/MULTIWEEK/ANNUAL</c> ids
    /// through the matching new entry points on <see cref="NormCheckRule"/>.
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
            NormCheckRule.RuleId          => NormCheckRule.Evaluate(profile, entries, periodStart, periodEnd, config),
            NormCheckRule.WeeklyRuleId    => NormCheckRule.EvaluateWeekly(profile, entries, periodStart, periodEnd, config),
            NormCheckRule.MultiWeekRuleId => NormCheckRule.EvaluateMultiWeek(profile, entries, periodStart, periodEnd, config),
            NormCheckRule.AnnualRuleId    => NormCheckRule.EvaluateAnnual(profile, entries, periodStart, periodEnd, config),
            SupplementRule.RuleId         => SupplementRule.Evaluate(profile, entries, periodStart, periodEnd, config),
            OvertimeRule.RuleId           => OvertimeRule.Evaluate(profile, entries, periodStart, periodEnd, config),
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
    /// Evaluates flex balance.
    /// </summary>
    public FlexBalanceResult EvaluateFlexBalance(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousBalance)
    {
        var config = AgreementConfigProvider.GetConfig(profile.AgreementCode, profile.OkVersion, profile.Position);
        return FlexBalanceRule.Evaluate(profile, entries, absences, periodStart, periodEnd, config, previousBalance);
    }
}
