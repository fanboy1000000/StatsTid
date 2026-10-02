namespace StatsTid.SharedKernel.Calendar;

/// <summary>
/// S145 / TASK-14505 — the per-working-day norm: the one place the system turns a weekly
/// hours figure and a part-time fraction into the hours one weekday carries.
///
/// <para>
/// <b>The rule (owner ruling Q2 = A, 2026-10-01).</b> A weekday's amount is
/// <c>weekly × fraction ÷ 5</c>, rounded to 2 decimals half-to-even (the .NET default,
/// <see cref="MidpointRounding.ToEven"/>). Saturdays and Sundays carry 0. Week and month
/// figures are SUMS of these per-day amounts, never <c>weekly × fraction</c> computed once —
/// so the payslip equals what the employee sees on the Skema screen, day by day. The same
/// helper serves the norm (<c>WeeklyNormHours</c>), both overtime thresholds
/// (<c>OvertimeThreshold50</c> / <c>OvertimeThreshold100</c>) and the 0-hour absence default.
/// Example: the stored 30-hour fraction is 0.811, so a weekday's norm is
/// <c>round(37 × 0.811 ÷ 5) = round(6.0014) = 6.00</c> and the 100 % threshold is
/// <c>round(40 × 0.811 ÷ 5) = round(6.488) = 6.49</c>; a full week is 30.00 / 32.45, where the
/// pre-S145 whole-week formula gave 30.007 / 32.44.
/// </para>
///
/// <para>
/// <b>Why it lives in SharedKernel/Calendar (ADR-002 note, R11).</b> The arithmetic is shared
/// by three bounded contexts — the Rule Engine (overtime, norm, flex), payroll (via the engine)
/// and the Backend's screen norm (<c>DailyNormCalculator</c>, moved onto this helper by
/// TASK-14509). One implementation means the three can never disagree. The helper is a pure
/// function of its arguments: no I/O, no clock, no ambient state, so it is safe to call from
/// the pure rule engine (ADR-002) and deterministic under replay. Weekend detection reads only
/// <see cref="DateOnly.DayOfWeek"/>; public holidays do NOT reduce the norm (refinement
/// Assumption 3).
/// </para>
/// </summary>
public static class DayNorm
{
    /// <summary>The number of working days a weekly figure is spread over (Monday–Friday).</summary>
    public const decimal WorkingDaysPerWeek = 5m;

    /// <summary>The precision of every per-day amount (2 decimals).</summary>
    public const int Decimals = 2;

    /// <summary>
    /// A weekday's amount: <c>Math.Round(weekly × fraction / 5, 2, MidpointRounding.ToEven)</c>.
    /// The caller decides that the day IS a weekday; use <see cref="ForDay"/> when the date is known.
    /// </summary>
    public static decimal ForWeekday(decimal weeklyHours, decimal partTimeFraction) =>
        Math.Round(weeklyHours * partTimeFraction / WorkingDaysPerWeek, Decimals, MidpointRounding.ToEven);

    /// <summary>
    /// The amount <paramref name="day"/> carries: <see cref="ForWeekday"/> on Monday–Friday,
    /// 0 on Saturday and Sunday.
    /// </summary>
    public static decimal ForDay(DateOnly day, decimal weeklyHours, decimal partTimeFraction) =>
        IsWeekday(day) ? ForWeekday(weeklyHours, partTimeFraction) : 0m;

    /// <summary>True on Monday–Friday; false on Saturday and Sunday.</summary>
    public static bool IsWeekday(DateOnly day) =>
        day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
