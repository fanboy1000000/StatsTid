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
        //
        // Note: When a JWT claim is stored as JsonClaimValueTypes.JsonArray, the
        // JWT bearer middleware may split the array into individual claims (one per
        // element). We therefore collect ALL "scopes" claims and try to parse each
        // as either a single RoleScope or an array of RoleScope.
        var scopesClaims = context.User.FindAll(StatsTidClaims.Scopes).ToList();
        if (scopesClaims.Count == 0) return Task.CompletedTask;

        try
        {
            var allScopes = new List<RoleScope>();
            foreach (var claim in scopesClaims)
            {
                var value = claim.Value.TrimStart();
                if (value.StartsWith("["))
                {
                    var arr = JsonSerializer.Deserialize<RoleScope[]>(value);
                    if (arr is not null) allScopes.AddRange(arr);
                }
                else if (value.StartsWith("{"))
                {
                    var single = JsonSerializer.Deserialize<RoleScope>(value);
                    if (single is not null) allScopes.Add(single);
                }
            }

            if (allScopes.Any(s => requirement.AllowedRoles.Contains(s.Role)))
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
