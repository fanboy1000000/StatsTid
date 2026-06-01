namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S59 / ADR-029. HR sets per-employee eligibility for a gated entitlement type
/// (CHILD_SICK this sprint; SENIOR_DAY is fully age-derived and never recorded here).
/// Dated/version-guarded fact (ADR-019/020): re-setting a (employee, entitlement_type)
/// emits a NEW superseding event; latest-wins + as-of-date resolution is the downstream
/// projection's job (not defined here). Default for an absent row is "ineligible"
/// (opt-in, refinement R1) — the absence of a record is meaningful, so the event only
/// carries explicit sets.
/// </summary>
public sealed class EmployeeEntitlementEligibilitySet : DomainEventBase
{
    public override string EventType => "EmployeeEntitlementEligibilitySet";

    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public required bool Eligible { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
}
