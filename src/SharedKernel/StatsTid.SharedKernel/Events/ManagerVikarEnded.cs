namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S74 / ADR-027 Phase 5 (SPRINT-74 R4, TASK-7401). Emitted when an approver-owned
/// vikar row in <c>manager_vikar</c> is closed — either by the explicit
/// <c>DELETE /api/reporting-lines/delegate</c> revoke, or by the
/// <c>DelegationExpiryService</c> the day AFTER its INCLUSIVE <c>UntilDate</c>
/// (the R4a "til og med" fix). The go-forward end-of-life counterpart to
/// <see cref="ManagerVikarCreated"/>, REPLACING the per-report
/// <see cref="ReportingLineSuperseded"/> fan-out the legacy self-delegation
/// revoke/expiry emitted.
/// </summary>
public sealed class ManagerVikarEnded : DomainEventBase
{
    public override string EventType => "ManagerVikarEnded";

    public required Guid VikarId { get; init; }
    public required string AbsentApproverId { get; init; }
    public required string VikarUserId { get; init; }
    public required DateOnly UntilDate { get; init; }
    public required string Reason { get; init; }
    public required string TreeRootOrgId { get; init; }
    /// <summary>The date the vikar was closed (effective_to).</summary>
    public required DateOnly EffectiveTo { get; init; }
    /// <summary>
    /// Why the vikar was closed: REVOKED (explicit DELETE /delegate), EXPIRED
    /// (DelegationExpiryService past until_date), or APPROVER_REMOVED (the absent
    /// approver — or the vikar user — was removed from the tree via the R10
    /// delete-with-reassignment closure). Free-text payload (no DB CHECK).
    /// </summary>
    public required string EndReason { get; init; }
    public required long RowVersion { get; init; }
}
