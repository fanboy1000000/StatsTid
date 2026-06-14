namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S74 / ADR-027 Phase 5 (SPRINT-74 R4, TASK-7401). Emitted when an approver-owned
/// vikar (stand-in approver) is created in <c>manager_vikar</c> — the go-forward
/// storage for self-service delegation, REPLACING the per-report
/// <c>SELF_DELEGATION</c> ACTING fan-out + the retired-from-emission
/// <see cref="ReportingLineSelfDelegated"/> batch event.
///
/// <para>
/// One event per vikar row. The vikar covers the absent approver's CURRENT + FUTURE
/// PRIMARY reports automatically (the resolver consults the row at approval-routing
/// time — ADR-027 D5). <c>UntilDate</c> is INCLUSIVE ("til og med"): the vikar is
/// effective THROUGH that date; expiry closes it the day AFTER (the R4a fix).
/// </para>
/// </summary>
public sealed class ManagerVikarCreated : DomainEventBase
{
    public override string EventType => "ManagerVikarCreated";

    public required Guid VikarId { get; init; }
    /// <summary>The manager who is away (the vikar acts on their behalf).</summary>
    public required string AbsentApproverId { get; init; }
    /// <summary>The stand-in approver covering the absent approver's reports.</summary>
    public required string VikarUserId { get; init; }
    /// <summary>INCLUSIVE last-covered day ("til og med").</summary>
    public required DateOnly UntilDate { get; init; }
    /// <summary>FERIE / SYGDOM / ORLOV / TJENESTEREJSE / ANDET (CHECK-constrained).</summary>
    public required string Reason { get; init; }
    /// <summary>The styrelse/ministry tree boundary (ADR-027); the audit target org.</summary>
    public required string TreeRootOrgId { get; init; }
    public required long RowVersion { get; init; }
}
