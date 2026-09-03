namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S138 / TASK-13803 (ADR-040 D8, Increment 3). Emitted when HR RESOLVES a row on the backdate
/// diagnostic worklist (<c>POST /api/hr/backdate-worklist/{id}/resolve</c>): the operator's
/// recorded assertion that the stale exported month / settled year has been dealt with
/// (<c>RECALCULATED</c> — re-planned through the correction path, or reversed-then-re-settled) or
/// deliberately left as is (<c>DISMISSED</c>), with the reason given.
///
/// <para>
/// <b>This event + its ADR-026 audit-projection row IS the audit record of the resolution</b>
/// (refinement rev 4 W8). The worklist row keeps <c>resolved_at/by/resolution/reason</c> as
/// queryable state; the event on <c>employee-{employeeId}</c> is the durable who/when/what. No
/// separate <c>*_audit</c> table — it would be a third copy of the same fact.
/// </para>
///
/// <para>
/// The resolution is the operator's VERB, never a derived truth: whether the export was actually
/// recalculated (<c>content_hash</c> moved) or the settlement actually reversed (<c>sequence</c>
/// advanced) stays a READ-time derivation against the per-trigger baselines.
/// </para>
/// </summary>
public sealed class BackdateWorklistRowResolved : DomainEventBase
{
    public override string EventType => "BackdateWorklistRowResolved";

    public required Guid WorklistId { get; init; }
    public required string EmployeeId { get; init; }

    /// <summary><c>EXPORTED_MONTH</c> | <c>SETTLED_YEAR</c>.</summary>
    public required string Kind { get; init; }

    public int? Year { get; init; }
    public int? Month { get; init; }
    public Guid? ExportId { get; init; }
    public string? EntitlementType { get; init; }
    public int? EntitlementYear { get; init; }

    /// <summary><c>RECALCULATED</c> | <c>DISMISSED</c> — the operator's assertion.</summary>
    public required string Resolution { get; init; }
    public string? Reason { get; init; }

    /// <summary>How many correction triggers the row had accumulated when it was resolved.</summary>
    public required int TriggerCount { get; init; }

    // ADR-019 D8 version transition of the worklist row.
    public required long VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
}
