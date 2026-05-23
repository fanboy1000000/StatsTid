namespace StatsTid.SharedKernel.Audit;

/// <summary>
/// S43 / ADR-026 D1. The 3-tier visibility taxonomy enforced at the
/// <c>audit_projection.visibility_scope</c> CHECK constraint (init.sql).
/// </summary>
public enum AuditVisibilityScope
{
    /// <summary>
    /// Tenant-scoped: visible only to admins of the row's <c>target_org_id</c>
    /// organization (LocalAdmin scope-by-target). Rows with this scope MUST
    /// have <c>target_org_id IS NOT NULL</c> per the
    /// <c>chk_target_org_required_when_tenant</c> CHECK constraint.
    /// </summary>
    TenantTargeted,

    /// <summary>
    /// Globally tenant-visible: visible to admins of any organization in the
    /// tenant (e.g., cross-tenant report access notifications, configuration
    /// changes affecting all orgs).
    /// </summary>
    GlobalTenantVisible,

    /// <summary>
    /// Global-admin-only: visible only to GlobalAdmin role (e.g., PII erasure
    /// events, institution provisioning events).
    /// </summary>
    GlobalAdminOnly,
}

/// <summary>
/// String-conversion helpers matching the
/// <c>audit_projection.visibility_scope</c> CHECK constraint values.
/// </summary>
public static class AuditVisibilityScopeExtensions
{
    public static string ToWireValue(this AuditVisibilityScope scope) => scope switch
    {
        AuditVisibilityScope.TenantTargeted => "TENANT_TARGETED",
        AuditVisibilityScope.GlobalTenantVisible => "GLOBAL_TENANT_VISIBLE",
        AuditVisibilityScope.GlobalAdminOnly => "GLOBAL_ADMIN_ONLY",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };
}
