namespace StatsTid.Backend.Api.Validation;

public static class RequestValidator
{
    private static readonly HashSet<string> ValidAgreementCodes = new() { "AC", "HK", "PROSA" };
    private static readonly HashSet<string> ValidOkVersions = new() { "OK24", "OK26" };
    // S147 / TASK-14708 (QUAL-231) removed the private absence-type list, a third, stale copy of the
    // vocabulary (old child's-sick-day spelling; no SICK_DAY, days 2 and 3 or SPECIAL_HOLIDAY_ALLOWANCE).
    // The S147 tidy pass (TASK-14721) deleted the two uncalled validators, for an absence and for a
    // part-time fraction; the Skema save, the only absence writer, checks the vocabulary itself (a 422).

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
}
