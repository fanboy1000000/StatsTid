using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Interfaces;

public interface IPayrollMapper
{
    Task<IReadOnlyList<PayrollExportLine>> MapAsync(
        CalculationResult result,
        EmploymentProfile profile,
        CancellationToken ct = default);
}
