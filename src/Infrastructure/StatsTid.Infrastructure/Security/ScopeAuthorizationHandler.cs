using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure.Security;

public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        var roleClaim = context.User.FindFirst(StatsTidClaims.Role)?.Value;
        if (roleClaim is null) return Task.CompletedTask;

        // Check if role is in allowed roles
        if (!requirement.AllowedRoles.Contains(roleClaim))
            return Task.CompletedTask;

        // If no org scope check needed, succeed
        if (!requirement.RequireOrgScope)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // For org-scoped checks, we need to verify the scope covers the target.
        // The target org is extracted from route/request by calling code.
        // At this level, we just verify the user HAS scopes — endpoint-level code
        // does the actual org path matching using the RoleScope.CoversOrg method.
        var scopesClaim = context.User.FindFirst(StatsTidClaims.Scopes)?.Value;
        if (scopesClaim is null) return Task.CompletedTask;

        try
        {
            var scopes = JsonSerializer.Deserialize<RoleScope[]>(scopesClaim);
            if (scopes is not null && scopes.Any(s => requirement.AllowedRoles.Contains(s.Role)))
            {
                context.Succeed(requirement);
            }
        }
        catch (JsonException)
        {
            // Invalid scopes claim — do not succeed
        }

        return Task.CompletedTask;
    }
}
