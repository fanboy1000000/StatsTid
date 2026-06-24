namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S98 / ADR-035 — emitted when an organization is SOFT-deleted (<c>is_active = FALSE</c>)
/// via the GlobalAdmin structural-ops surface (<c>DELETE /api/admin/organizations/{orgId}</c>).
/// The delete is blocked-if-employees (an Organisation with active users, or a MAO with any
/// active user beneath it, cannot be deleted) — so this event only ever fires for an empty
/// org. Recoverable (re-activation is out of scope but the row is preserved). Stream:
/// <c>org-{orgId}</c> (same as OrganizationCreated/Updated). TENANT_TARGETED audit.
/// </summary>
public sealed class OrganizationDeleted : DomainEventBase
{
    public override string EventType => "OrganizationDeleted";

    public required string OrgId { get; init; }
    public required string OrgName { get; init; }
    public required string OrgType { get; init; }
}
