using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: validates overtime governance constraints.
/// Check 1: MaxOvertimeHoursPerPeriod ceiling — WARNING if exceeded (overtime is legitimate with approval).
/// Check 2: OvertimeRequiresPreApproval — WARNING if overtime exists without pre-approval.
/// No I/O, fully deterministic.
/// </summary>
public static class OvertimeGovernanceRule
{
    public const string RuleId = "OVERTIME_GOVERNANCE_CHECK";

    public static ComplianceCheckResult Evaluate(
        EmploymentProfile profile,
        decimal overtimeHoursInPeriod,
        bool hasPreApproval,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var warnings = new List<ComplianceViolation>();

        // Check 1: MaxOvertimeHoursPerPeriod (0 = unlimited, skip check)
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

        // Check 2: Pre-approval required but not granted
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

        return new ComplianceCheckResult
        {
            RuleId = RuleId,
            EmployeeId = profile.EmployeeId,
            Success = warnings.Count == 0,
            Violations = new List<ComplianceViolation>(),
            Warnings = warnings
        };
    }
}
