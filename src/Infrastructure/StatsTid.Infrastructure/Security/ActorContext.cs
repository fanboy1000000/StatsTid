using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure.Security;

public sealed record ActorContext(
    string? ActorId,
    string? ActorRole,
    Guid CorrelationId,
    string? OrgId = null,
    RoleScope[]? Scopes = null);

public static class ActorContextExtensions
{
    public static ActorContext GetActorContext(this HttpContext httpContext)
    {
        var actorId = httpContext.User?.FindFirst("sub")?.Value
                   ?? httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? httpContext.User?.FindFirst(StatsTidClaims.EmployeeId)?.Value;
        var actorRole = httpContext.User?.FindFirst(StatsTidClaims.Role)?.Value;
        var orgId = httpContext.User?.FindFirst(StatsTidClaims.OrgId)?.Value;
        var correlationId = httpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var cid) && cid is Guid g
            ? g
            : Guid.NewGuid();

        RoleScope[]? scopes = null;
        var scopesClaims = httpContext.User?.FindAll(StatsTidClaims.Scopes).ToList();
        if (scopesClaims is { Count: > 0 })
        {
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
                if (allScopes.Count > 0) scopes = allScopes.ToArray();
            }
            catch (JsonException) { /* invalid claim — leave null */ }
        }

        return new ActorContext(actorId, actorRole, correlationId, orgId, scopes);
    }
}
