namespace StatsTid.SharedKernel.Exceptions;

/// <summary>
/// Thrown by PCS-routed callers (PeriodCalculationService, ComplianceEndpoints)
/// when <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentProfileResolver.GetByEmployeeIdAtAsync"/>
/// returns null. Maps to HTTP 500 via existing middleware per ADR-023 D3 fail-closed
/// semantic. NOT thrown by the resolver itself — the resolver always returns
/// nullable; this exception is the caller's response to a null result.
/// </summary>
public sealed class EmployeeProfileNotFoundException : Exception
{
    public string EmployeeId { get; }
    public DateOnly AsOfDate { get; }

    public EmployeeProfileNotFoundException(string employeeId, DateOnly asOfDate)
        : base($"No employee profile found for employee_id='{employeeId}' as of {asOfDate:yyyy-MM-dd}.")
    {
        EmployeeId = employeeId;
        AsOfDate = asOfDate;
    }
}
