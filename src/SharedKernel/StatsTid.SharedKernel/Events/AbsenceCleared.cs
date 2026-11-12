namespace StatsTid.SharedKernel.Events;

/// <summary>
/// A Skema absence cell (one employee, one day, one absence type) emptied by a Skema save (owner Q2;
/// addendum A1 D-3). The cell's current Skema row in <c>absences_projection</c> is kept and marked
/// <c>superseded_by_outbox_id</c> = this event's outbox id; nothing is inserted. A quota-consuming row's stored
/// feriedage is given back in the same save, with its <c>EntitlementBalanceAdjusted</c> (ADR-032 D2: what is
/// given back is what is stored, never a recomputation). Rides <c>employee-{EmployeeId}</c>, the stream of the
/// registration it retires (ADR-018 D5).
/// </summary>
public sealed class AbsenceCleared : DomainEventBase
{
    public override string EventType => "AbsenceCleared";

    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required string AbsenceType { get; init; }

    /// <summary>The <c>AbsenceRegistered</c> whose row this clear retired.</summary>
    public required Guid ClearedEventId { get; init; }

    /// <summary>The retired row's hours.</summary>
    public required decimal ClearedHours { get; init; }

    /// <summary>
    /// The retired row's stored feriedage, the consumption the clear gives back; null for an absence type that
    /// draws on no entitlement balance.
    /// </summary>
    public decimal? ClearedFeriedage { get; init; }
}
