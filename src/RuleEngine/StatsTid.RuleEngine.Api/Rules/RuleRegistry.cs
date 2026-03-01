using StatsTid.RuleEngine.Api.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

public sealed class RuleRegistry
{
    private static readonly HashSet<string> SupportedVersions = new() { "OK24", "OK26" };

    private static readonly Dictionary<string, Func<EmploymentProfile, IReadOnlyList<TimeEntry>, DateOnly, DateOnly, CalculationResult>> TimeRules = new()
    {
        [NormCheckRule.RuleId] = NormCheckRule.Evaluate,
    };

    private static readonly HashSet<string> ConfigAwareTimeRules = new()
    {
        SupplementRule.RuleId,
        OvertimeRule.RuleId,
    };

    public IReadOnlyList<string> GetAvailableRules(string okVersion)
    {
        if (!SupportedVersions.Contains(okVersion))
            return [];

        var rules = new List<string>(TimeRules.Keys);
        rules.AddRange(ConfigAwareTimeRules);
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
        if (TimeRules.TryGetValue(ruleId, out var rule))
            return rule(profile, entries, periodStart, periodEnd);

        // Config-aware time rules
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
            "SUPPLEMENT_CALC" => SupplementRule.Evaluate(profile, entries, periodStart, periodEnd, config),
            "OVERTIME_CALC" => OvertimeRule.Evaluate(profile, entries, periodStart, periodEnd, config),
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
