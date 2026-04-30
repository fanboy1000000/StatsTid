using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: validates overtime governance constraints.
/// Check 1: MaxOvertimeHoursPerPeriod ceiling — WARNING if exceeded (overtime is legitimate with approval).
/// Check 2: OvertimeRequiresPreApproval — WARNING if overtime exists without pre-approval.
/// No I/O, fully deterministic.
///
/// S20 / TASK-2006: this rule decomposes into two separately-registered classifications
/// — <see cref="MaxHoursRuleId"/> and <see cref="PreApprovalRuleId"/>. The legacy
/// <see cref="RuleId"/> entry point (<see cref="Evaluate"/>) is preserved verbatim so
/// existing callers (<c>/api/rules/check-overtime-governance</c> in Program.cs and the
/// Sprint 17 unit tests) continue to work without modification — it runs both checks
/// and unions the warnings under the legacy id.
/// </summary>
public static class OvertimeGovernanceRule
{
    /// <summary>
    /// Legacy RuleId — preserved for backward compatibility with the
    /// <c>/api/rules/check-overtime-governance</c> endpoint and existing tests.
    /// </summary>
    public const string RuleId = "OVERTIME_GOVERNANCE_CHECK";

    // S20 / TASK-2006 — multi-mode decomposition (ADR-016 D2):
    public const string MaxHoursRuleId = "OVERTIME_MAX_HOURS";
    public const string PreApprovalRuleId = "OVERTIME_PRE_APPROVAL";

    /// <summary>
    /// Evaluates both governance checks for the given period. Pure function,
    /// deterministic, no I/O. Result is tagged with the legacy <see cref="RuleId"/>.
    /// Callers needing per-check classification should call
    /// <see cref="EvaluateMaxHours"/> or <see cref="EvaluatePreApproval"/> directly.
    /// </summary>
    public static ComplianceCheckResult Evaluate(
        EmploymentProfile profile,
        decimal overtimeHoursInPeriod,
        bool hasPreApproval,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var warnings = new List<ComplianceViolation>();
        AppendMaxHoursWarning(overtimeHoursInPeriod, periodStart, config, warnings);
        AppendPreApprovalWarning(overtimeHoursInPeriod, hasPreApproval, periodStart, config, warnings);
        return BuildResult(RuleId, profile, warnings);
    }

    /// <summary>
    /// S20 — single-check entry point for the per-period overtime ceiling
    /// (<see cref="MaxHoursRuleId"/>). Logic is bit-identical to the corresponding
    /// branch of <see cref="Evaluate"/>.
    /// </summary>
    public static ComplianceCheckResult EvaluateMaxHours(
        EmploymentProfile profile,
        decimal overtimeHoursInPeriod,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var warnings = new List<ComplianceViolation>();
        AppendMaxHoursWarning(overtimeHoursInPeriod, periodStart, config, warnings);
        return BuildResult(MaxHoursRuleId, profile, warnings);
    }

    /// <summary>
    /// S20 — single-check entry point for the pre-approval requirement
    /// (<see cref="PreApprovalRuleId"/>). Logic is bit-identical to the corresponding
    /// branch of <see cref="Evaluate"/>.
    /// </summary>
    public static ComplianceCheckResult EvaluatePreApproval(
        EmploymentProfile profile,
        decimal overtimeHoursInPeriod,
        bool hasPreApproval,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var warnings = new List<ComplianceViolation>();
        AppendPreApprovalWarning(overtimeHoursInPeriod, hasPreApproval, periodStart, config, warnings);
        return BuildResult(PreApprovalRuleId, profile, warnings);
    }

    private static void AppendMaxHoursWarning(
        decimal overtimeHoursInPeriod,
        DateOnly periodStart,
        AgreementRuleConfig config,
        List<ComplianceViolation> warnings)
    {
        // MaxOvertimeHoursPerPeriod (0 = unlimited, skip check)
        if (config.MaxOvertimeHoursPerPeriod > 0 && overtimeHoursInPeriod > config.MaxOvertimeHoursPerPeriod)
        {
            warnings.Add(new ComplianceViolation
            {
                ViolationType = ComplianceViolationType.OVERTIME_EXCEEDED,
                Date = periodStart,
                ActualValue = overtimeHoursInPeriod,
                ThresholdValue = config.MaxOvertimeHoursPerPeriod,
                Severity = ComplianceSeverity.WARNING,
                Message = $"Overarbejde {overtimeHoursInPeriod:F1}t overstiger grænsen på {config.MaxOvertimeHoursPerPeriod:F1}t for perioden"
            });
        }
    }

    private static void AppendPreApprovalWarning(
        decimal overtimeHoursInPeriod,
        bool hasPreApproval,
        DateOnly periodStart,
        AgreementRuleConfig config,
        List<ComplianceViolation> warnings)
    {
        // Pre-approval required but not granted
        if (config.OvertimeRequiresPreApproval && overtimeHoursInPeriod > 0 && !hasPreApproval)
        {
            warnings.Add(new ComplianceViolation
            {
                ViolationType = ComplianceViolationType.OVERTIME_UNAPPROVED,
                Date = periodStart,
                ActualValue = overtimeHoursInPeriod,
                ThresholdValue = 0,
                Severity = ComplianceSeverity.WARNING,
                Message = $"Overarbejde på {overtimeHoursInPeriod:F1}t kræver forhåndsgodkendelse"
            });
        }
    }

    private static ComplianceCheckResult BuildResult(
        string ruleId,
        EmploymentProfile profile,
        List<ComplianceViolation> warnings) =>
        new()
        {
            RuleId = ruleId,
            EmployeeId = profile.EmployeeId,
            Success = warnings.Count == 0,
            Violations = new List<ComplianceViolation>(),
            Warnings = warnings
        };
}
