using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StatsTid.Auth;

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

    // ── QUAL-008: the ambient log scope ──

    /// <summary>
    /// QUAL-008 — the middleware opens exactly ONE logging scope carrying the SAME correlation id
    /// it placed in <see cref="HttpContext.Items"/> (not a second minted id), the scope is OPEN
    /// while the rest of the pipeline runs (so any log that inner code emits is stamped with the
    /// id), and it is DISPOSED at the request boundary (no scope leaks to the next request). This
    /// is the ambient mechanism that lets one user action be traced across a service's request logs
    /// without every call site threading the id by hand.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_OpensLogScope_WithSameItemsId_AroundThePipeline_ThenDisposes()
    {
        var logger = new ScopeCapturingLogger();

        Guid idSeenByInnerPipeline = Guid.Empty;
        int scopeDepthDuringPipeline = -1;
        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            // Snapshot the ambient state exactly when downstream code (which would log) runs.
            idSeenByInnerPipeline = (Guid)ctx.Items[CorrelationIdMiddleware.ItemKey]!;
            scopeDepthDuringPipeline = logger.ActiveScopeDepth;
            return Task.CompletedTask;
        }, logger);

        var context = new DefaultHttpContext();
        await middleware.InvokeAsync(context);

        var itemsId = (Guid)context.Items[CorrelationIdMiddleware.ItemKey]!;

        // The scope was OPEN while the inner pipeline ran, and it wrapped the SAME id as Items.
        Assert.Equal(1, scopeDepthDuringPipeline);
        Assert.Equal(itemsId, idSeenByInnerPipeline);

        // Exactly one scope opened, and it disposed at the request boundary (no leak).
        Assert.Equal(0, logger.ActiveScopeDepth);
        var scopeState = Assert.Single(logger.OpenedScopeStates);

        // The scope state carries the correlation id under the shared ItemKey property name, and
        // it is the SAME id already in Items — the middleware did not mint a second one.
        var pairs = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object>>>(scopeState);
        var pair = Assert.Single(pairs);
        Assert.Equal(CorrelationIdMiddleware.ItemKey, pair.Key);
        Assert.Equal(itemsId, pair.Value);
    }

    /// <summary>
    /// A minimal <see cref="ILogger{T}"/> that records the state object passed to each
    /// <see cref="ILogger.BeginScope{TState}(TState)"/> and tracks how many scopes are currently
    /// open (incremented on BeginScope, decremented on dispose) — enough to assert the scope's
    /// content, its openness during the pipeline, and its disposal afterward.
    /// </summary>
    private sealed class ScopeCapturingLogger : ILogger<CorrelationIdMiddleware>
    {
        public List<object?> OpenedScopeStates { get; } = new();
        public int ActiveScopeDepth { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            OpenedScopeStates.Add(state);
            ActiveScopeDepth++;
            return new ScopeToken(this);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Not exercised by this test — the scope, not the emitted lines, is under assertion.
        }

        private sealed class ScopeToken : IDisposable
        {
            private readonly ScopeCapturingLogger _owner;
            private bool _disposed;
            public ScopeToken(ScopeCapturingLogger owner) => _owner = owner;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.ActiveScopeDepth--;
            }
        }
    }
}
