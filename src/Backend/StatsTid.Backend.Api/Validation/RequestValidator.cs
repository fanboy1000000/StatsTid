using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Validation;

public static class RequestValidator
{
    private static readonly HashSet<string> ValidAgreementCodes = new() { "AC", "HK", "PROSA" };
    private static readonly HashSet<string> ValidOkVersions = new() { "OK24", "OK26" };
    // S147 / TASK-14708 (QUAL-231): the private absence-type list is gone. It was a third, stale
    // copy of the vocabulary (it used the old numbered child's-sick-day spelling and lacked
    // SICK_DAY, days 2 and 3 and SPECIAL_HOLIDAY_ALLOWANCE); ValidateAbsence now reads the one
    // SharedKernel classification.

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

    /// <summary>
    /// Validates an absence's fields. No route calls it today (the Skema save, the only absence
    /// writer, has its own checks, including the vocabulary 422); it is kept, re-pointed at
    /// <see cref="AbsenceClassification"/>, rather than deleted (S147 / TASK-14708 contract 6).
    /// </summary>
    public static (bool IsValid, string? Error) ValidateAbsence(string? employeeId, decimal hours, string? absenceType, string? agreementCode, string? okVersion)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return (false, "EmployeeId is required");
        if (hours <= 0 || hours > 24)
            return (false, "Hours must be greater than 0 and at most 24");
        if (!AbsenceClassification.IsKnown(absenceType))
            return (false, $"AbsenceType must be one of: {string.Join(", ", AbsenceClassification.InDeclarationOrder.Select(kv => kv.Key))}");
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
