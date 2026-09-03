namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted on the predecessor's stream when a cross-day supersession closes
/// the predecessor profile row (effective_to set) and a new history row is
/// inserted via <see cref="EmployeeProfileCreated"/>. This event carries the
/// audit-trail of the close on the predecessor stream; the new row's INSERT
/// is recorded on its own stream as <c>EmployeeProfileCreated</c>.
/// Stream: <c>employee-profile-{employeeId}</c> (shared across predecessor +
/// successor — one stream per employee lineage).
/// Per ADR-020 D2 Case B + ADR-018 D6 + Sprint 31 / Phase 4d-3 Part 1.
/// RESERVED — registered in S31, NOT emitted until S32.
/// </summary>
public sealed class EmployeeProfileSuperseded : DomainEventBase
{
    public override string EventType => "EmployeeProfileSuperseded";

    // Identity — predecessor + successor
    public required Guid PredecessorProfileId { get; init; }
    public required Guid NewProfileId { get; init; }
    public required string EmployeeId { get; init; }

    // Temporal validity transition
    public required DateOnly PredecessorEffectiveFrom { get; init; }
    public required DateOnly PredecessorEffectiveTo { get; init; }
    public required DateOnly NewEffectiveFrom { get; init; }

    // S138 / TASK-13801 (ADR-040 D8) — where the NEW row ends: null = it is the open row (the
    // pre-S138 Case C shape); a date = D8's insert-between (the covering row was a history row
    // and the new row runs [NewEffectiveFrom, NewEffectiveTo), later rows untouched). Additive
    // and optional (not `required`): pre-S138 payloads deserialize with null.
    public DateOnly? NewEffectiveTo { get; init; }

    // New-row payload (post-mutation state of the successor)
    public required decimal PartTimeFraction { get; init; }
    public string? Position { get; init; }

    // S138 / TASK-13801 (ADR-040 D4/D8) — the dated fourth field, written on every row the
    // writer produces. Additive and optional (not `required`): pre-S138 payloads deserialize
    // with null; EventSerializer registrations are unchanged.
    public string? EmploymentCategory { get; init; }

    // Optimistic-concurrency row-versions
    public required long PredecessorVersion { get; init; }
    public required long NewVersion { get; init; }
}
