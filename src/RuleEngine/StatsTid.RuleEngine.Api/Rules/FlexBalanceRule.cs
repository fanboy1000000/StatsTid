using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: calculates flex balance delta for a period.
/// Combines actual hours worked + absence norm credits - norm for period.
/// Clamps to MaxFlexBalance, flags excess for auto-payout.
/// </summary>
public static class FlexBalanceRule
{
    public const string RuleId = "FLEX_BALANCE";

    public static FlexBalanceResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config,
        decimal previousBalance)
    {
        var normHours = config.WeeklyNormHours * profile.PartTimeFraction;

        // Reduce norm by unpaid leave
        var normReduction = AbsenceRule.GetNormReductionHours(profile, absences, periodStart, periodEnd);
        var effectiveNorm = normHours - normReduction;

        var workedHours = entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd)
            .Sum(e => e.Hours);

        var absenceNormCredits = AbsenceRule.GetNormCreditHours(
            profile, absences, periodStart, periodEnd);

        var effectiveWorked = workedHours + absenceNormCredits;
        var flexDelta = effectiveWorked - effectiveNorm;
        var rawNewBalance = previousBalance + flexDelta;

        // Clamp to max balance
        var clampedBalance = Math.Min(rawNewBalance, config.MaxFlexBalance);
        var excessForPayout = rawNewBalance > config.MaxFlexBalance
            ? rawNewBalance - config.MaxFlexBalance
            : 0m;

        return new FlexBalanceResult
        {
            EmployeeId = profile.EmployeeId,
            PreviousBalance = previousBalance,
            NewBalance = clampedBalance,
            Delta = flexDelta,
            WorkedHours = workedHours,
            AbsenceNormCredits = absenceNormCredits,
            NormHours = effectiveNorm,
            ExcessForPayout = excessForPayout,
            Success = true
        };
    }
}

public sealed class FlexBalanceResult
{
    public required string EmployeeId { get; init; }
    public required decimal PreviousBalance { get; init; }
    public required decimal NewBalance { get; init; }
    public required decimal Delta { get; init; }
    public required decimal WorkedHours { get; init; }
    public required decimal AbsenceNormCredits { get; init; }
    public required decimal NormHours { get; init; }
    public required decimal ExcessForPayout { get; init; }
    public required bool Success { get; init; }
}
