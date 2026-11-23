using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Contracts;

public sealed class EvaluateFlexRequest
{
    public required EmploymentProfile Profile { get; init; }
    public required List<TimeEntry> Entries { get; init; }
    public required List<AbsenceEntry> Absences { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public decimal PreviousBalance { get; init; }

    /// <summary>
    /// The agreement configuration to evaluate with, resolved by the caller for the profile's
    /// agreement, OK version and position; the engine reads no configuration of its own (ADR-043
    /// D12, R-8). It must name the profile's agreement code and OK version (A-10). Nullable, never
    /// <c>required</c>: a request without one is refused by the route with its fixed sentence, not
    /// by the JSON binder.
    /// </summary>
    public AgreementRuleConfig? Config { get; init; }
}
