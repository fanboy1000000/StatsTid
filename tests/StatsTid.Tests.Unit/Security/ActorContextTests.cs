using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Unit.Security;

public class ActorContextTests
{
    [Fact]
    public void AuthenticatedUser_ExtractsActorIdFromSubClaim()
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "EMP042"),
            new Claim(StatsTidClaims.Role, "Manager"),
        }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var actor = context.GetActorContext();

        Assert.Equal("EMP042", actor.ActorId);
        Assert.Equal("Manager", actor.ActorRole);
    }

    [Fact]
    public void UnauthenticatedUser_ReturnsNullActorId()
    {
        var context = new DefaultHttpContext();
        // No user/claims set — default anonymous

        var actor = context.GetActorContext();

        Assert.Null(actor.ActorId);
        Assert.Null(actor.ActorRole);
    }

    [Fact]
    public void CorrelationId_ExtractedFromHttpContextItems()
    {
        var expectedId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var context = new DefaultHttpContext();
        context.Items[CorrelationIdMiddleware.ItemKey] = expectedId;

        var actor = context.GetActorContext();

        Assert.Equal(expectedId, actor.CorrelationId);
    }
}
