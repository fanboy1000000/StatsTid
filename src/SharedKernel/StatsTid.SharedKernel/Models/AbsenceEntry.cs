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

public static class AbsenceTypes
{
    public const string Vacation = "VACATION";
    public const string CareDay = "CARE_DAY";
    public const string ChildSick1 = "CHILD_SICK_1";
    public const string ParentalLeave = "PARENTAL_LEAVE";
    public const string SeniorDay = "SENIOR_DAY";
    public const string LeaveWithPay = "LEAVE_WITH_PAY";
    public const string LeaveWithoutPay = "LEAVE_WITHOUT_PAY";
}
