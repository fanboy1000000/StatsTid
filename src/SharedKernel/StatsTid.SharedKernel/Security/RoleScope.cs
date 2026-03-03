namespace StatsTid.SharedKernel.Security;

public sealed record RoleScope(string Role, string? OrgId, string ScopeType)
{
    // ScopeType: "GLOBAL", "ORG_ONLY", "ORG_AND_DESCENDANTS"

    /// <summary>
    /// Checks if this scope covers the given organization path.
    /// Uses materialized path prefix matching for ORG_AND_DESCENDANTS.
    /// </summary>
    public bool CoversOrg(string? targetOrgPath, string? scopeOrgPath)
    {
        if (ScopeType == "GLOBAL") return true;
        if (targetOrgPath is null || scopeOrgPath is null) return false;
        if (ScopeType == "ORG_AND_DESCENDANTS")
            return targetOrgPath.StartsWith(scopeOrgPath, StringComparison.Ordinal);
        // ORG_ONLY: exact match
        return string.Equals(targetOrgPath, scopeOrgPath, StringComparison.Ordinal);
    }
}
