using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: evaluates absence entries and produces calculation line items.
/// Determines norm credit and payroll time type per absence type.
///
/// <para>
/// <b>S145 / TASK-14505 (R35).</b> An absence registered with 0 hours stands for one working
/// day at the employee's fraction. That default is now <see cref="DefaultHoursForZeroHourAbsence"/>
/// — <c>DayNorm.ForWeekday(7.4 × 5, fraction)</c>, i.e. the shared per-day helper over the same
/// 7.4-hour full-time basis, rounded to 2 decimals — instead of the unrounded
/// <c>7.4 × fraction</c>. For the exact fractions (1.0, 0.5, 0.8) nothing changes; for the
/// stored 30-hour fraction 0.811 a day is 6.00 h instead of 6.0014 h, the same figure the
/// screen and the weekly norm use. It applies in <see cref="Evaluate"/>,
/// <see cref="GetNormCreditHours"/>, <see cref="GetNormReductionHours"/> and the weekly core.
/// </para>
/// </summary>
public static class AbsenceRule
{
    public const string RuleId = "ABSENCE_CALC";

    private static readonly decimal FullTimeNormCreditPerDay = 7.4m;

    /// <summary>The weekly basis the 0-hour default is taken from: 7.4 h × 5 days = 37.0 h (R35).</summary>
    private static readonly decimal DefaultCreditWeeklyBasis = FullTimeNormCreditPerDay * DayNorm.WorkingDaysPerWeek;

    /// <summary>
    /// Absence types that grant norm credit (count toward norm fulfillment).
    /// LEAVE_WITHOUT_PAY does NOT grant norm credit — it reduces the effective norm.
    /// </summary>
    private static readonly HashSet<string> NormCreditTypes = new()
    {
        AbsenceTypes.Vacation,
        AbsenceTypes.CareDay,
        AbsenceTypes.ChildSick1,
        AbsenceTypes.ChildSick2,
        AbsenceTypes.ChildSick3,
        AbsenceTypes.ParentalLeave,
        AbsenceTypes.SeniorDay,
        AbsenceTypes.SpecialHolidayAllowance,
        AbsenceTypes.LeaveWithPay,
    };

    public static CalculationResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var lineItems = new List<CalculationLineItem>();

        var periodAbsences = absences
            .Where(a => a.Date >= periodStart && a.Date <= periodEnd)
            .ToList();

        foreach (var absence in periodAbsences)
        {
            var hours = EffectiveHours(absence, profile.PartTimeFraction);

            lineItems.Add(new CalculationLineItem
            {
                TimeType = MapAbsenceToTimeType(absence.AbsenceType),
                Hours = hours,
                Rate = 1.0m,
                Date = absence.Date
            });
        }

        return new CalculationResult
        {
            RuleId = RuleId,
            EmployeeId = profile.EmployeeId,
            Success = true,
            LineItems = lineItems
        };
    }

    /// <summary>
    /// Returns the total norm credit hours from absences in the period.
    /// Used by FlexBalanceRule to adjust norm fulfillment.
    /// </summary>
    public static decimal GetNormCreditHours(
        EmploymentProfile profile,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        return absences
            .Where(a => a.Date >= periodStart && a.Date <= periodEnd)
            .Where(a => NormCreditTypes.Contains(a.AbsenceType))
            .Sum(a => EffectiveHours(a, profile.PartTimeFraction));
    }

    /// <summary>
    /// Returns norm reduction from unpaid leave in the period.
    /// </summary>
    public static decimal GetNormReductionHours(
        EmploymentProfile profile,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        return absences
            .Where(a => a.Date >= periodStart && a.Date <= periodEnd)
            .Where(a => ReducesNorm(a.AbsenceType))
            .Sum(a => EffectiveHours(a, profile.PartTimeFraction));
    }

    public static bool GrantsNormCredit(string absenceType)
    {
        return NormCreditTypes.Contains(absenceType);
    }

    /// <summary>True for the absence type that reduces the norm instead of crediting it (unpaid leave).</summary>
    public static bool ReducesNorm(string absenceType) => absenceType == AbsenceTypes.LeaveWithoutPay;

    /// <summary>
    /// Pure (R35): the hours a 0-hour absence stands for — one working day at
    /// <paramref name="partTimeFraction"/>, <c>DayNorm.ForWeekday(37.0, fraction)</c>
    /// (2 decimals, half-to-even). A weekday amount whatever the absence's date, as before.
    /// </summary>
    public static decimal DefaultHoursForZeroHourAbsence(decimal partTimeFraction) =>
        DayNorm.ForWeekday(DefaultCreditWeeklyBasis, partTimeFraction);

    /// <summary>
    /// Pure: the hours an absence counts for — its own hours when positive, else
    /// <see cref="DefaultHoursForZeroHourAbsence"/> at <paramref name="partTimeFraction"/>.
    /// </summary>
    public static decimal EffectiveHours(AbsenceEntry absence, decimal partTimeFraction)
    {
        ArgumentNullException.ThrowIfNull(absence);
        return absence.Hours > 0
            ? absence.Hours
            : DefaultHoursForZeroHourAbsence(partTimeFraction);
    }

    private static string MapAbsenceToTimeType(string absenceType) => absenceType switch
    {
        AbsenceTypes.Vacation => "VACATION",
        AbsenceTypes.CareDay => "CARE_DAY",
        AbsenceTypes.ChildSick1 => "CHILD_SICK_DAY",
        AbsenceTypes.ChildSick2 => "CHILD_SICK_DAY_2",
        AbsenceTypes.ChildSick3 => "CHILD_SICK_DAY_3",
        AbsenceTypes.ParentalLeave => "PARENTAL_LEAVE",
        AbsenceTypes.SeniorDay => "SENIOR_DAY",
        AbsenceTypes.SpecialHolidayAllowance => "SPECIAL_HOLIDAY_ALLOWANCE",
        AbsenceTypes.LeaveWithPay => "LEAVE_WITH_PAY",
        AbsenceTypes.LeaveWithoutPay => "LEAVE_WITHOUT_PAY",
        _ => absenceType
    };
}
