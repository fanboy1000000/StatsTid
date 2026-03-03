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
        var actorId = httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? httpContext.User?.FindFirst(StatsTidClaims.EmployeeId)?.Value;
        var actorRole = httpContext.User?.FindFirst(StatsTidClaims.Role)?.Value;
        var orgId = httpContext.User?.FindFirst(StatsTidClaims.OrgId)?.Value;
        var correlationId = httpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var cid) && cid is Guid g
            ? g
            : Guid.NewGuid();

        RoleScope[]? scopes = null;
        var scopesClaim = httpContext.User?.FindFirst(StatsTidClaims.Scopes)?.Value;
        if (scopesClaim is not null)
        {
            try { scopes = JsonSerializer.Deserialize<RoleScope[]>(scopesClaim); }
            catch (JsonException) { /* invalid claim — leave null */ }
        }

        return new ActorContext(actorId, actorRole, correlationId, orgId, scopes);
    }
}
