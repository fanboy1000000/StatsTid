using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure.Security;

public sealed record ActorContext(string? ActorId, string? ActorRole, Guid CorrelationId);

public static class ActorContextExtensions
{
    public static ActorContext GetActorContext(this HttpContext httpContext)
    {
        var actorId = httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? httpContext.User?.FindFirst(StatsTidClaims.EmployeeId)?.Value;
        var actorRole = httpContext.User?.FindFirst(StatsTidClaims.Role)?.Value;
        var correlationId = httpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var cid) && cid is Guid g
            ? g
            : Guid.NewGuid();

        return new ActorContext(actorId, actorRole, correlationId);
    }
}
