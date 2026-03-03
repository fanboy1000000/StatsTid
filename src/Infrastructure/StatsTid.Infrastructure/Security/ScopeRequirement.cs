using Microsoft.AspNetCore.Authorization;

namespace StatsTid.Infrastructure.Security;

public sealed class ScopeRequirement : IAuthorizationRequirement
{
    public string[] AllowedRoles { get; }
    public bool RequireOrgScope { get; }

    public ScopeRequirement(bool requireOrgScope, params string[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
        RequireOrgScope = requireOrgScope;
    }
}
