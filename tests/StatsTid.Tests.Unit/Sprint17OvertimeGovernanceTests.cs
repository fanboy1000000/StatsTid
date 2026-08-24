using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// QUAL-022 (S133 TASK-13303) — GENUINE tests for <see cref="OvertimeGovernanceRule"/>.
///
/// <para><b>What this file used to be (the defect):</b> before S133 this file was a
/// byte-for-byte copy of <c>OvertimeRuleTests</c> — only the class name differed. Its ~10
/// "[Fact]" tests all called <c>OvertimeRule.Evaluate</c> and never once touched
/// <see cref="OvertimeGovernanceRule"/>. So the rule that is actually registered AND invoked
/// (<c>OVERTIME_MAX_HOURS</c> + <c>OVERTIME_PRE_APPROVAL</c>, registered in <c>RuleRegistry.cs</c>
/// and reachable via <c>/api/rules/check-overtime-governance</c>) had ZERO coverage, while the
/// suite's test count was inflated by ten phantom duplicates. Those phantom tests were REMOVED
/// and replaced by the genuine exercises below (the real <c>OvertimeRule</c> coverage still
/// lives, unduplicated, in <c>OvertimeRuleTests.cs</c>).</para>
///
/// <para><b>What the rule does (plain terms):</b> it raises WARNINGS (never hard violations)
/// about overtime governance. Check 1 (max-hours ceiling): if a per-period overtime ceiling is
/// configured (&gt; 0) and the period's overtime exceeds it, warn <c>OVERTIME_EXCEEDED</c>.
/// Check 2 (pre-approval): if the agreement requires pre-approval and overtime exists without
/// it, warn <c>OVERTIME_UNAPPROVED</c>. <c>Evaluate</c> runs both under the legacy id;
/// <c>EvaluateMaxHours</c> / <c>EvaluatePreApproval</c> run one check each under their own
/// S20-decomposed ids.</para>
///
/// <para><b>Falsifiability (QUAL-022):</b> two real guards in <c>OvertimeGovernanceRule.cs</c>:
/// (1) <c>overtimeHoursInPeriod &gt; config.MaxOvertimeHoursPerPeriod</c> — mutating <c>&gt;</c>
/// to <c>&gt;=</c> turns <see cref="MaxHours_ExactlyAtCeiling_DoesNotWarn"/> RED; mutating it to
/// <c>&lt;</c> turns <see cref="MaxHours_OverCeiling_WarnsOvertimeExceeded"/> RED. (2)
/// <c>config.OvertimeRequiresPreApproval &amp;&amp; overtimeHoursInPeriod &gt; 0 &amp;&amp; !hasPreApproval</c>
/// — deleting the <c>!</c> turns <see cref="PreApproval_RequiredButGranted_DoesNotWarn"/> RED.</para>
/// </summary>
public class Sprint17OvertimeGovernanceTests
{
    private static readonly DateOnly PeriodStart = new(2024, 4, 8);
    private static readonly DateOnly PeriodEnd = PeriodStart.AddDays(6);

    private static EmploymentProfile Profile() => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = "HK",
        OkVersion = "OK24",
        EmploymentCategory = "Standard",
    };

    /// <summary>
    /// Builds a config exposing ONLY the two governance knobs under test; every other required
    /// field is given an inert default so the test's cause-and-effect is unambiguous.
    /// </summary>
    private static AgreementRuleConfig Config(decimal maxOvertimeHoursPerPeriod, bool requiresPreApproval) => new()
    {
        AgreementCode = "HK",
        OkVersion = "OK24",
        WeeklyNormHours = 37m,
        HasOvertime = true,
        HasMerarbejde = false,
        MaxFlexBalance = 0m,
        FlexCarryoverMax = 0m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        MaxOvertimeHoursPerPeriod = maxOvertimeHoursPerPeriod,
        OvertimeRequiresPreApproval = requiresPreApproval,
    };

    // -------------------------------------------------------------------------------------
    // Check 1 — MaxOvertimeHoursPerPeriod ceiling (EvaluateMaxHours, id OVERTIME_MAX_HOURS)
    // -------------------------------------------------------------------------------------

    [Fact]
    public void MaxHours_OverCeiling_WarnsOvertimeExceeded()
    {
        var result = OvertimeGovernanceRule.EvaluateMaxHours(
            Profile(), overtimeHoursInPeriod: 12m, PeriodStart, PeriodEnd,
            Config(maxOvertimeHoursPerPeriod: 10m, requiresPreApproval: false));

        Assert.Equal(OvertimeGovernanceRule.MaxHoursRuleId, result.RuleId);
        Assert.False(result.Success);
        Assert.Empty(result.Violations); // warning-only rule: findings never land in Violations
        var w = Assert.Single(result.Warnings);
        Assert.Equal(ComplianceViolationType.OVERTIME_EXCEEDED, w.ViolationType);
        Assert.Equal(ComplianceSeverity.WARNING, w.Severity);
        Assert.Equal(12m, w.ActualValue);
        Assert.Equal(10m, w.ThresholdValue);
        Assert.Equal(PeriodStart, w.Date);
    }

    [Fact]
    public void MaxHours_ExactlyAtCeiling_DoesNotWarn()
    {
        // Boundary guard: the check fires on STRICTLY-greater-than, so overtime == ceiling is
        // compliant. If the production comparison were '>=' this test would go RED.
        var result = OvertimeGovernanceRule.EvaluateMaxHours(
            Profile(), overtimeHoursInPeriod: 10m, PeriodStart, PeriodEnd,
            Config(maxOvertimeHoursPerPeriod: 10m, requiresPreApproval: false));

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void MaxHours_BelowCeiling_DoesNotWarn()
    {
        var result = OvertimeGovernanceRule.EvaluateMaxHours(
            Profile(), overtimeHoursInPeriod: 8m, PeriodStart, PeriodEnd,
            Config(maxOvertimeHoursPerPeriod: 10m, requiresPreApproval: false));

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void MaxHours_UnlimitedCeiling_SkipsCheckEntirely()
    {
        // A configured ceiling of 0 means "unlimited": the check is skipped even for enormous
        // overtime. Guards the 'config.MaxOvertimeHoursPerPeriod > 0' clause.
        var result = OvertimeGovernanceRule.EvaluateMaxHours(
            Profile(), overtimeHoursInPeriod: 1000m, PeriodStart, PeriodEnd,
            Config(maxOvertimeHoursPerPeriod: 0m, requiresPreApproval: false));

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
    }

    // -------------------------------------------------------------------------------------
    // Check 2 — OvertimeRequiresPreApproval (EvaluatePreApproval, id OVERTIME_PRE_APPROVAL)
    // -------------------------------------------------------------------------------------

    [Fact]
    public void PreApproval_RequiredAndMissing_WarnsUnapproved()
    {
        var result = OvertimeGovernanceRule.EvaluatePreApproval(
            Profile(), overtimeHoursInPeriod: 5m, hasPreApproval: false, PeriodStart, PeriodEnd,
            Config(maxOvertimeHoursPerPeriod: 0m, requiresPreApproval: true));

        Assert.Equal(OvertimeGovernanceRule.PreApprovalRuleId, result.RuleId);
        Assert.False(result.Success);
        Assert.Empty(result.Violations);
        var w = Assert.Single(result.Warnings);
        Assert.Equal(ComplianceViolationType.OVERTIME_UNAPPROVED, w.ViolationType);
        Assert.Equal(ComplianceSeverity.WARNING, w.Severity);
        Assert.Equal(5m, w.ActualValue);
        Assert.Equal(PeriodStart, w.Date);
    }

    [Fact]
    public void PreApproval_RequiredButGranted_DoesNotWarn()
    {
        // Guards the '!hasPreApproval' clause: with approval present, no warning. Deleting the
        // negation in production would turn this RED.
        var result = OvertimeGovernanceRule.EvaluatePreApproval(
            Profile(), overtimeHoursInPeriod: 5m, hasPreApproval: true, PeriodStart, PeriodEnd,
            Config(maxOvertimeHoursPerPeriod: 0m, requiresPreApproval: true));

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void PreApproval_RequiredButNoOvertime_DoesNotWarn()
    {
        // Guards 'overtimeHoursInPeriod > 0': zero overtime cannot require approval.
        var result = OvertimeGovernanceRule.EvaluatePreApproval(
            Profile(), overtimeHoursInPeriod: 0m, hasPreApproval: false, PeriodStart, PeriodEnd,
            Config(maxOvertimeHoursPerPeriod: 0m, requiresPreApproval: true));

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void PreApproval_NotRequiredByAgreement_DoesNotWarn()
    {
        // Guards the 'config.OvertimeRequiresPreApproval' clause: when the agreement does not
        // require pre-approval, unapproved overtime is fine.
        var result = OvertimeGovernanceRule.EvaluatePreApproval(
            Profile(), overtimeHoursInPeriod: 5m, hasPreApproval: false, PeriodStart, PeriodEnd,
            Config(maxOvertimeHoursPerPeriod: 0m, requiresPreApproval: false));

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
    }

    // -------------------------------------------------------------------------------------
    // Combined Evaluate — legacy entry point (id OVERTIME_GOVERNANCE_CHECK) runs BOTH checks
    // -------------------------------------------------------------------------------------

    [Fact]
    public void Evaluate_BothChecksFail_UnionsWarningsUnderLegacyId()
    {
        var result = OvertimeGovernanceRule.Evaluate(
            Profile(), overtimeHoursInPeriod: 12m, hasPreApproval: false, PeriodStart, PeriodEnd,
            Config(maxOvertimeHoursPerPeriod: 10m, requiresPreApproval: true));

        Assert.Equal(OvertimeGovernanceRule.RuleId, result.RuleId); // tagged with the LEGACY id
        Assert.False(result.Success);
        Assert.Empty(result.Violations);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.ViolationType == ComplianceViolationType.OVERTIME_EXCEEDED);
        Assert.Contains(result.Warnings, w => w.ViolationType == ComplianceViolationType.OVERTIME_UNAPPROVED);
    }

    [Fact]
    public void Evaluate_OnlyMaxHoursFails_ProducesSingleExceededWarning()
    {
        // Ceiling exceeded, pre-approval not required -> exactly one OVERTIME_EXCEEDED warning.
        // Proves the two checks are independent within the combined entry point.
        var result = OvertimeGovernanceRule.Evaluate(
            Profile(), overtimeHoursInPeriod: 12m, hasPreApproval: false, PeriodStart, PeriodEnd,
            Config(maxOvertimeHoursPerPeriod: 10m, requiresPreApproval: false));

        Assert.False(result.Success);
        var w = Assert.Single(result.Warnings);
        Assert.Equal(ComplianceViolationType.OVERTIME_EXCEEDED, w.ViolationType);
    }

    [Fact]
    public void Evaluate_CompliantCase_SucceedsWithNoFindings()
    {
        var result = OvertimeGovernanceRule.Evaluate(
            Profile(), overtimeHoursInPeriod: 8m, hasPreApproval: true, PeriodStart, PeriodEnd,
            Config(maxOvertimeHoursPerPeriod: 10m, requiresPreApproval: true));

        Assert.Equal(OvertimeGovernanceRule.RuleId, result.RuleId);
        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Violations);
    }
}
