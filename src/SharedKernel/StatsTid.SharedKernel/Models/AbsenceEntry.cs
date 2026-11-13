namespace StatsTid.SharedKernel.Models;

public sealed class AbsenceEntry
{
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required string AbsenceType { get; init; }
    public required decimal Hours { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
}

/// <summary>
/// The ONE absence-code vocabulary: every value is exactly the string the Skema save stores in
/// <c>absences_projection.absence_type</c>, which payroll passes to the Rule Engine unchanged and
/// which the payroll wage-type mapping keys on (e.g. <c>SICK_DAY</c> → SLS_0540,
/// <c>CHILD_SICK_DAY</c> → SLS_0530).
///
/// <para>
/// <b>S147 / TASK-14708 (QUAL-231).</b> Before S147 this class spelled the child's sick days with
/// an old numbered spelling and had no <c>SICK_DAY</c>, while Skema stored <c>CHILD_SICK_DAY</c>,
/// <c>CHILD_SICK_DAY_2</c>, <c>CHILD_SICK_DAY_3</c> and <c>SICK_DAY</c>. The engine's credit list
/// therefore never matched a stored sick or child's-sick code, and payroll charged a full day of
/// flex for each one while the Skema screen showed it as neutral. The values now take the stored
/// spelling (no data change: nothing ever stored the old spelling) and <see cref="SickDay"/> is
/// added. The member NAMES <c>ChildSick1/2/3</c> are kept so existing callers compile unchanged;
/// only their values moved.
/// </para>
///
/// <para>
/// Every code here is classified exactly once in <see cref="AbsenceClassification"/> (credit,
/// reduction or neutral); the Skema save refuses a code that is not in this vocabulary (422).
/// </para>
/// </summary>
public static class AbsenceTypes
{
    public const string SickDay = "SICK_DAY";
    public const string Vacation = "VACATION";
    public const string CareDay = "CARE_DAY";
    public const string ChildSick1 = "CHILD_SICK_DAY";
    public const string ChildSick2 = "CHILD_SICK_DAY_2";
    public const string ChildSick3 = "CHILD_SICK_DAY_3";

    /// <summary>
    /// The call-home day ("hjemkaldelsesdag"): paid leave for the remaining working hours of the
    /// day a child falls ill after the parent has met for work (021-26 and 020-26 § 14, from
    /// OK26). Registered in hours, beside the child's 1st to 3rd sick day; which dates offer it is
    /// <c>DefaultEntitlementConfigs.ChildSickCodesOffered</c> (QUAL-236).
    /// </summary>
    public const string ChildSickCallHome = "CHILD_SICK_CALL_HOME";
    public const string ParentalLeave = "PARENTAL_LEAVE";
    public const string SeniorDay = "SENIOR_DAY";
    public const string SpecialHolidayAllowance = "SPECIAL_HOLIDAY_ALLOWANCE";
    public const string LeaveWithPay = "LEAVE_WITH_PAY";
    public const string LeaveWithoutPay = "LEAVE_WITHOUT_PAY";
}
