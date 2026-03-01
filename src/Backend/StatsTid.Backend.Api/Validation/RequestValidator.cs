namespace StatsTid.Backend.Api.Validation;

public static class RequestValidator
{
    private static readonly HashSet<string> ValidAgreementCodes = new() { "AC", "HK", "PROSA" };
    private static readonly HashSet<string> ValidOkVersions = new() { "OK24", "OK26" };
    private static readonly HashSet<string> ValidAbsenceTypes = new()
    {
        "VACATION", "CARE_DAY", "CHILD_SICK_1", "PARENTAL_LEAVE",
        "SENIOR_DAY", "LEAVE_WITH_PAY", "LEAVE_WITHOUT_PAY"
    };

    public static (bool IsValid, string? Error) ValidateTimeEntry(string? employeeId, decimal hours, string? agreementCode, string? okVersion)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return (false, "EmployeeId is required");
        if (hours <= 0 || hours > 24)
            return (false, "Hours must be greater than 0 and at most 24");
        if (string.IsNullOrWhiteSpace(agreementCode) || !ValidAgreementCodes.Contains(agreementCode))
            return (false, $"AgreementCode must be one of: {string.Join(", ", ValidAgreementCodes)}");
        if (string.IsNullOrWhiteSpace(okVersion) || !ValidOkVersions.Contains(okVersion))
            return (false, $"OkVersion must be one of: {string.Join(", ", ValidOkVersions)}");
        return (true, null);
    }

    public static (bool IsValid, string? Error) ValidateAbsence(string? employeeId, decimal hours, string? absenceType, string? agreementCode, string? okVersion)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return (false, "EmployeeId is required");
        if (hours <= 0 || hours > 24)
            return (false, "Hours must be greater than 0 and at most 24");
        if (string.IsNullOrWhiteSpace(absenceType) || !ValidAbsenceTypes.Contains(absenceType))
            return (false, $"AbsenceType must be one of: {string.Join(", ", ValidAbsenceTypes)}");
        if (string.IsNullOrWhiteSpace(agreementCode) || !ValidAgreementCodes.Contains(agreementCode))
            return (false, $"AgreementCode must be one of: {string.Join(", ", ValidAgreementCodes)}");
        if (string.IsNullOrWhiteSpace(okVersion) || !ValidOkVersions.Contains(okVersion))
            return (false, $"OkVersion must be one of: {string.Join(", ", ValidOkVersions)}");
        return (true, null);
    }

    public static (bool IsValid, string? Error) ValidatePartTimeFraction(decimal fraction)
    {
        if (fraction <= 0 || fraction > 1)
            return (false, "PartTimeFraction must be greater than 0 and at most 1");
        return (true, null);
    }
}
