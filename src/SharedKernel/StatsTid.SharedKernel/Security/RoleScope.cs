namespace StatsTid.SharedKernel.Security;

public sealed record RoleScope(string Role, string? OrgId, string ScopeType)
{
    // ScopeType: "GLOBAL", "ORG_ONLY"
    // S93 / ADR-035 slice 2 (flat role-scope): ORG_AND_DESCENDANTS subtree inheritance is dropped.
    // Coverage is now EXACT Organisation-set membership (the union of a user's ORG_ONLY rows); no
    // materialized-path prefix/subtree expansion.

    /// <summary>
    /// Checks if this scope covers the given organization path.
    /// GLOBAL covers everything; ORG_ONLY covers exactly its own org (exact-equality match).
    /// </summary>
    public bool CoversOrg(string? targetOrgPath, string? scopeOrgPath)
    {
        if (ScopeType == "GLOBAL") return true;
        // S93 hardening (ADR-035 slice 2, Step-7a): DEFAULT-DENY any non-GLOBAL scope_type that is
        // not ORG_ONLY. A stale pre-S93 JWT carrying a removed type (e.g. ORG_AND_DESCENDANTS) must
        // NOT fall through to exact-match its root — that would let an old MAO-rooted token pass the
        // org-structure gates, bypassing the OQ1 grant-time MAO guard for the token's lifetime.
        if (ScopeType != "ORG_ONLY") return false;
        if (targetOrgPath is null || scopeOrgPath is null) return false;
        // ORG_ONLY: exact match
        return string.Equals(targetOrgPath, scopeOrgPath, StringComparison.Ordinal);
    }
}
