namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S98 / ADR-035 — emitted when an ORGANISATION is re-parented under a different MAO via the
/// GlobalAdmin structural-ops surface (<c>PUT /api/admin/organizations/{orgId}/move</c>). Only
/// ORGANISATIONs move (a MAO is a root); the new parent must be an active MAO. Carries BOTH the
/// OLD and NEW <c>parent_org_id</c> AND the OLD and NEW <c>materialized_path</c> so a replay can
/// reconstruct the exact path rewrite (the moved row's OWN path only — Organisations are leaves,
/// no descendant cascade). The path rewrite is load-bearing: the tree-roster reads scope by
/// <c>materialized_path LIKE</c> (ApprovalPeriodRepository GetMedarbejderRosterForTreeAsync /
/// GetPeriodStatusProjectionForTreeAsync). Stream: <c>org-{orgId}</c>. TENANT_TARGETED audit.
/// </summary>
public sealed class OrganizationMoved : DomainEventBase
{
    public override string EventType => "OrganizationMoved";

    public required string OrgId { get; init; }
    public string? OldParentOrgId { get; init; }
    public required string NewParentOrgId { get; init; }
    public required string OldMaterializedPath { get; init; }
    public required string NewMaterializedPath { get; init; }
}
