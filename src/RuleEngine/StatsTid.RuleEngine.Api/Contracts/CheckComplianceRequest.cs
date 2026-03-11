using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Contracts;

public sealed class CheckComplianceRequest
{
    public required EmploymentProfile Profile { get; init; }
    public required List<TimeEntry> Entries { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
}
