using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Interfaces;

public interface IRuleEngine
{
    CalculationResult Evaluate(string ruleId, EmploymentProfile profile, IReadOnlyList<TimeEntry> entries);
}
