using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: evaluates absence entries and produces calculation line items.
/// Determines norm credit and payroll time type per absence type.
/// </summary>
public static class AbsenceRule
{
    public const string RuleId = "ABSENCE_CALC";

    private static readonly decimal FullTimeNormCreditPerDay = 7.4m;

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
            var hours = absence.Hours > 0
                ? absence.Hours
                : FullTimeNormCreditPerDay * profile.PartTimeFraction;

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
            .Sum(a => a.Hours > 0
                ? a.Hours
                : FullTimeNormCreditPerDay * profile.PartTimeFraction);
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
            .Where(a => a.AbsenceType == AbsenceTypes.LeaveWithoutPay)
            .Sum(a => a.Hours > 0
                ? a.Hours
                : FullTimeNormCreditPerDay * profile.PartTimeFraction);
    }

    public static bool GrantsNormCredit(string absenceType)
    {
        return NormCreditTypes.Contains(absenceType);
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
