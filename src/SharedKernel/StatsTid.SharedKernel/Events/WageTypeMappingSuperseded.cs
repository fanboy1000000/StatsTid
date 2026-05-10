namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted when a wage type mapping is superseded by a cross-day edit:
/// the predecessor row is closed (effective_to set) and a new history row is
/// inserted with effective_from = today, version = 1. Distinct from
/// <see cref="WageTypeMappingUpdated"/> (same-day in-place edit, no new history row).
/// Per ADR-020 D2 + Sprint 29 / Phase 4d-1.
/// </summary>
public sealed class WageTypeMappingSuperseded : DomainEventBase
{
    public override string EventType => "WageTypeMappingSuperseded";
    public required string TimeType { get; init; }
    public required string WageType { get; init; }
    public required string OkVersion { get; init; }
    public required string AgreementCode { get; init; }
    public string Position { get; init; } = "";
}
