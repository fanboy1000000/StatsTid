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
///
/// <para>
/// <b>S147 / TASK-14708 (QUAL-231).</b> Whether an absence is credited or reduces the norm is no
/// longer decided here: it is read from the one SharedKernel classification,
/// <see cref="AbsenceClassification"/>, which the Backend's Skema save also uses to refuse an
/// unknown code. Two things changed with it. The child's sick days are now matched under the
/// spelling Skema stores (<c>CHILD_SICK_DAY</c>, <c>_2</c>, <c>_3</c>; before, the list used an
/// old numbered spelling that nothing stored), and a paid sick day (<c>SICK_DAY</c>) is
/// credited for every agreement (Statens arbejdstidsaftale § 7 nr. 2; for AC-agreement staff a
/// labelled default by analogy, owner ruling D-8b = (a)). The amount is unchanged: the stored
/// hours (<see cref="EffectiveHours"/>), which the Skema save caps at the day's norm. The
/// evaluation-model marker moved to <c>weekly-timeline-v2</c> with this change
/// (<see cref="WeeklyTimelineCore.EvaluationModel"/>).
/// </para>
/// </summary>
public static class AbsenceRule
{
    public const string RuleId = "ABSENCE_CALC";

    private static readonly decimal FullTimeNormCreditPerDay = 7.4m;

    /// <summary>The weekly basis the 0-hour default is taken from: 7.4 h × 5 days = 37.0 h (R35).</summary>
    private static readonly decimal DefaultCreditWeeklyBasis = FullTimeNormCreditPerDay * DayNorm.WorkingDaysPerWeek;

    /// <summary>
    /// The absence types that grant norm credit (count toward norm fulfilment): a projection of
    /// <see cref="AbsenceClassification"/>, never a list of its own (S147 / TASK-14708, P-14).
    /// LEAVE_WITHOUT_PAY is not among them — it reduces the effective norm instead.
    /// </summary>
    private static readonly HashSet<string> NormCreditTypes = AbsenceClassification.InDeclarationOrder
        .Where(kv => kv.Value == AbsenceNormEffect.Credit)
        .Select(kv => kv.Key)
        .ToHashSet(StringComparer.Ordinal);

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
                // The line's time type IS the stored absence code, which the payroll wage-type
                // mapping keys on (S147 / TASK-14708: one vocabulary). The pre-S147 code switch
                // mapped every code to itself except the never-stored old numbered child's-sick
                // spelling, so for every code Skema stores the line is byte-identical.
                TimeType = absence.AbsenceType,
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

    /// <summary>
    /// True for an absence type that reduces the norm instead of crediting it (unpaid leave), as
    /// <see cref="AbsenceClassification"/> classifies it.
    /// </summary>
    public static bool ReducesNorm(string absenceType) => AbsenceClassification.IsReduction(absenceType);

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
}
