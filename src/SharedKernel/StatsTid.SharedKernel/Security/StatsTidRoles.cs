namespace StatsTid.SharedKernel.Security;

public static class StatsTidRoles
{
    public const string GlobalAdmin = "GlobalAdmin";
    public const string LocalAdmin = "LocalAdmin";
    public const string LocalHR = "LocalHR";
    public const string LocalLeader = "LocalLeader";
    public const string Employee = "Employee";

    // Legacy role mappings for backward compatibility with existing events/audit logs
    public const string LegacyAdmin = "Admin";
    public const string LegacyManager = "Manager";
    public const string LegacyReadOnly = "ReadOnly";
    // Note: "Employee" remains the same

    // Backward-compatible aliases — allows existing code referencing the old names to compile
    // These will be removed once all consumers are migrated to the new role names
    public const string Admin = LegacyAdmin;
    public const string Manager = LegacyManager;
    public const string ReadOnly = LegacyReadOnly;

    /// <summary>
    /// Hierarchy level (lower = higher privilege). Used for role comparison.
    /// </summary>
    public static int GetHierarchyLevel(string role) => role switch
    {
        GlobalAdmin => 1,
        LocalAdmin => 2,
        LocalHR => 3,
        LocalLeader => 4,
        Employee => 5,
        // Legacy mappings
        LegacyAdmin => 1,
        LegacyManager => 4,
        LegacyReadOnly => 6,
        _ => int.MaxValue
    };

    public static bool IsAtLeast(string actualRole, string requiredRole)
        => GetHierarchyLevel(actualRole) <= GetHierarchyLevel(requiredRole);
}
