using StatsTid.RuleEngine.Api.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

public sealed class RuleRegistry
{
    private static readonly HashSet<string> SupportedVersions = new() { "OK24", "OK26" };

    private static readonly HashSet<string> ConfigAwareTimeRules = new()
    {
        NormCheckRule.RuleId,
        SupplementRule.RuleId,
        OvertimeRule.RuleId,
        OnCallDutyRule.RuleId,
        CallInWorkRule.RuleId,
        TravelTimeRule.RuleId,
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
    /// Backward-compatible Evaluate for simple time-entry rules (NormCheck).
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
    /// Multi-dispatch for config-aware time rules (supplements, overtime).
    /// </summary>
    public CalculationResult EvaluateTimeRule(
        string ruleId,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var config = AgreementConfigProvider.GetConfig(profile.AgreementCode, profile.OkVersion);

        return ruleId switch
        {
            "NORM_CHECK_37H" => NormCheckRule.Evaluate(profile, entries, periodStart, periodEnd, config),
            "SUPPLEMENT_CALC" => SupplementRule.Evaluate(profile, entries, periodStart, periodEnd, config),
            "OVERTIME_CALC" => OvertimeRule.Evaluate(profile, entries, periodStart, periodEnd, config),
            "ON_CALL_DUTY" => OnCallDutyRule.Evaluate(profile, entries, periodStart, periodEnd, config),
            "CALL_IN_WORK" => CallInWorkRule.Evaluate(profile, entries, periodStart, periodEnd, config),
            "TRAVEL_TIME" => TravelTimeRule.Evaluate(profile, entries, periodStart, periodEnd, config),
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
        var config = AgreementConfigProvider.GetConfig(profile.AgreementCode, profile.OkVersion);
        return FlexBalanceRule.Evaluate(profile, entries, absences, periodStart, periodEnd, config, previousBalance);
    }
}
