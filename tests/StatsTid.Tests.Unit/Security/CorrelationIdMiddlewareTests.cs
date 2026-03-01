using Microsoft.AspNetCore.Http;
using StatsTid.Infrastructure.Security;

namespace StatsTid.Tests.Unit.Security;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task NoHeader_GeneratesNewCorrelationId()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.True(context.Items.ContainsKey(CorrelationIdMiddleware.ItemKey));
        var correlationId = context.Items[CorrelationIdMiddleware.ItemKey];
        Assert.IsType<Guid>(correlationId);
        Assert.NotEqual(Guid.Empty, (Guid)correlationId);
    }

    [Fact]
    public async Task ExistingHeader_UsesProvidedId()
    {
        var expectedId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = expectedId.ToString();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var correlationId = (Guid)context.Items[CorrelationIdMiddleware.ItemKey]!;
        Assert.Equal(expectedId, correlationId);
    }

    [Fact]
    public async Task InvalidGuidHeader_GeneratesNewId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "not-a-guid";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var correlationId = context.Items[CorrelationIdMiddleware.ItemKey];
        Assert.IsType<Guid>(correlationId);
        Assert.NotEqual(Guid.Empty, (Guid)correlationId);
    }

    [Fact]
    public async Task ResponseHeader_ContainsCorrelationId()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var itemsId = (Guid)context.Items[CorrelationIdMiddleware.ItemKey]!;
        var responseHeader = context.Response.Headers[CorrelationIdMiddleware.HeaderName].FirstOrDefault();

        Assert.NotNull(responseHeader);
        Assert.Equal(itemsId.ToString(), responseHeader);
    }
}
