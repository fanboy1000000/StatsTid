using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Flex balance: worked hours + absence norm credits − (norm − unpaid-leave reduction), added to
/// the opening balance and clamped to the agreement's <c>MaxFlexBalance</c>; the excess above the
/// cap becomes a <c>FLEX_PAYOUT</c> line (auto-payout). Caps per agreement:
/// danish-agreements.md "Key Behavioral Differences" (MaxFlexBalance).
///
/// <para>
/// <b>S145 / TASK-14505 (R8).</b> The single-period entry point <c>Evaluate</c> is retired: it
/// took one week's norm for any period, so a month's "excess" overflowed the cap (QUAL-186).
/// <see cref="WeeklyTimelineCore"/> now sums the per-day norm and the credits over the period's
/// own covered days and settles flex ONCE, on the last covered day, at the cap in force that
/// day, through <see cref="Settle"/>. What stays here is the settle-and-clamp arithmetic
/// (unchanged), the payout-line helper and the result type.
/// </para>
/// </summary>
public static class FlexBalanceRule
{
    public const string RuleId = "FLEX_BALANCE";

    /// <summary>
    /// Pure: settles one flex period. <c>Delta = (worked + credits) − (norm − reduction)</c>;
    /// <c>NewBalance = min(previous + Delta, cap)</c>; <c>ExcessForPayout</c> is what the raw
    /// balance exceeds the cap by (0 when it does not). Same arithmetic, in the same order, as the
    /// retired single-period method.
    /// </summary>
    public static FlexBalanceResult Settle(
        string employeeId,
        decimal previousBalance,
        decimal workedHours,
        decimal absenceNormCredits,
        decimal normHours,
        decimal normReduction,
        decimal maxFlexBalance)
    {
        var effectiveNorm = normHours - normReduction;
        var effectiveWorked = workedHours + absenceNormCredits;
        var flexDelta = effectiveWorked - effectiveNorm;
        var rawNewBalance = previousBalance + flexDelta;

        // Clamp to max balance
        var clampedBalance = Math.Min(rawNewBalance, maxFlexBalance);
        var excessForPayout = rawNewBalance > maxFlexBalance
            ? rawNewBalance - maxFlexBalance
            : 0m;

        return new FlexBalanceResult
        {
            EmployeeId = employeeId,
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

    /// <summary>
    /// Pure function: produces a FLEX_PAYOUT line item when there is excess balance above the cap,
    /// dated <paramref name="settlementDay"/>. Returns null if no payout is needed.
    /// </summary>
    public static CalculationLineItem? GetPayoutLineItem(FlexBalanceResult result, DateOnly settlementDay)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.ExcessForPayout <= 0)
            return null;

        return new CalculationLineItem
        {
            TimeType = "FLEX_PAYOUT",
            Hours = result.ExcessForPayout,
            Rate = 1.0m,
            Date = settlementDay
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
