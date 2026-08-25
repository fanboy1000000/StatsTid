using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StatsTid.Auth;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Unit.Security;

/// <summary>
/// QUAL-009 / SEC-038 — the policy-denial trace. Three cooperating units:
/// the classifier (routine-read vs admin/mutating, deny-by-default), the result handler
/// (observe + log + annotate, delegate to the default on every path), and the audit-row gate
/// (write a row only for auditable denials). These are the falsifiable cores; the actual DB write
/// is exercised by a Docker-gated integration test (CI-only).
/// </summary>
public class PolicyDenialClassifierTests
{
    private static DefaultHttpContext ContextWithEndpoint(string method, params string[] policyNames)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        object[] metadata = policyNames.Select(p => (object)new AuthorizeAttribute(p)).ToArray();
        ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
        return ctx;
    }

    [Theory]
    // Read + a known non-admin policy → routine (log-only).
    [InlineData("GET", "EmployeeOrAbove", DenialRouteClass.RoutineRead)]
    [InlineData("GET", "LeaderOrAbove", DenialRouteClass.RoutineRead)]
    [InlineData("GET", "Authenticated", DenialRouteClass.RoutineRead)]
    [InlineData("HEAD", "EmployeeOrAbove", DenialRouteClass.RoutineRead)]
    // Read + an admin-strict policy → auditable.
    [InlineData("GET", "GlobalAdminOnly", DenialRouteClass.AdminOrMutating)]
    [InlineData("GET", "LocalAdminOrAbove", DenialRouteClass.AdminOrMutating)]
    [InlineData("GET", "HROrAbove", DenialRouteClass.AdminOrMutating)]
    // Any mutating method → auditable, regardless of the (non-admin) policy.
    [InlineData("POST", "EmployeeOrAbove", DenialRouteClass.AdminOrMutating)]
    [InlineData("PUT", "Authenticated", DenialRouteClass.AdminOrMutating)]
    [InlineData("DELETE", "EmployeeOrAbove", DenialRouteClass.AdminOrMutating)]
    [InlineData("PATCH", "LeaderOrAbove", DenialRouteClass.AdminOrMutating)]
    public void ClassifyRoute_MethodAndPolicy(string method, string policy, DenialRouteClass expected)
    {
        var ctx = ContextWithEndpoint(method, policy);
        Assert.Equal(expected, PolicyDenialClassifier.ClassifyRoute(ctx));
    }

    [Fact]
    public void ClassifyRoute_MixedPolicies_OneAdminTipsWholeRouteAuditable()
    {
        // A read whose endpoint carries BOTH a routine policy AND an admin policy is auditable —
        // the presence of one admin-strict policy is enough.
        var ctx = ContextWithEndpoint("GET", "EmployeeOrAbove", "GlobalAdminOnly");
        Assert.Equal(DenialRouteClass.AdminOrMutating, PolicyDenialClassifier.ClassifyRoute(ctx));
    }

    [Fact]
    public void ClassifyRoute_UnknownRoute_NoEndpoint_DefaultsAuditable()
    {
        // Deny-by-default: a denial with no matched endpoint cannot be proven routine.
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        Assert.Equal(DenialRouteClass.AdminOrMutating, PolicyDenialClassifier.ClassifyRoute(ctx));
    }

    [Fact]
    public void ClassifyRoute_NoNamedPolicy_DefaultsAuditable()
    {
        // An endpoint with an [Authorize] that names no policy (Policy == null) is not proven routine.
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AuthorizeAttribute()),
            "unnamed"));
        Assert.Equal(DenialRouteClass.AdminOrMutating, PolicyDenialClassifier.ClassifyRoute(ctx));
    }

    [Fact]
    public void ClassifyRoute_UnrecognizedPolicy_DefaultsAuditable()
    {
        // A brand-new policy name nobody added to the routine allowlist is auditable by default —
        // the guard fails safe.
        var ctx = ContextWithEndpoint("GET", "SomeFuturePolicyNobodyAllowlisted");
        Assert.Equal(DenialRouteClass.AdminOrMutating, PolicyDenialClassifier.ClassifyRoute(ctx));
    }

    [Fact]
    public void DescribePolicies_ReturnsStableNonPiiIdentifiers()
    {
        Assert.Equal("GlobalAdminOnly", PolicyDenialClassifier.DescribePolicies(ContextWithEndpoint("GET", "GlobalAdminOnly")));

        var noEndpoint = new DefaultHttpContext();
        Assert.Equal("(no-endpoint)", PolicyDenialClassifier.DescribePolicies(noEndpoint));

        var unnamed = new DefaultHttpContext();
        unnamed.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new AuthorizeAttribute()), "u"));
        Assert.Equal("(unnamed-authorize)", PolicyDenialClassifier.DescribePolicies(unnamed));
    }
}

/// <summary>
/// The audit-row gate — <see cref="PolicyDenialAuditRowMiddleware.BuildRowIfAuditable"/>. Proves,
/// with no database, that a row is produced ONLY for auditable denials and NEVER for routine reads
/// or allowed requests (no double-write / no false-positive).
/// </summary>
public class PolicyDenialAuditRowGateTests
{
    private static DefaultHttpContext ContextWithAudit(PolicyDenialAudit? audit)
    {
        var ctx = new DefaultHttpContext();
        if (audit is not null)
            ctx.Items[DenialLoggingAuthorizationResultHandler.ItemKey] = audit;
        return ctx;
    }

    private static PolicyDenialAudit Audit(DenialRouteClass routeClass, int status = 403, string outcome = "Forbidden") =>
        new(Outcome: outcome, StatusCode: status, PolicyId: "GlobalAdminOnly", RouteClass: routeClass,
            ActorId: "USR01", ActorRole: "Employee", Method: status == 401 ? "GET" : "POST",
            Path: "/api/admin/thing", CorrelationId: Guid.NewGuid());

    [Fact]
    public void AdminOrMutatingDenial_ProducesExactlyOneFailureRow()
    {
        var audit = Audit(DenialRouteClass.AdminOrMutating);
        var ctx = ContextWithAudit(audit);

        var entry = PolicyDenialAuditRowMiddleware.BuildRowIfAuditable(ctx);

        Assert.NotNull(entry);
        Assert.Equal("failure", entry!.Result);
        Assert.Equal(403, entry.HttpStatus);
        Assert.Equal("POST /api/admin/thing", entry.Action);
        Assert.Equal("/api/admin/thing", entry.Resource);
        Assert.Equal("POST", entry.HttpMethod);
        Assert.Equal(audit.CorrelationId, entry.CorrelationId);
        Assert.Contains("policy-authorization", entry.Details);
        Assert.Contains("GlobalAdminOnly", entry.Details);
    }

    [Fact]
    public void RoutineReadDenial_ProducesNoRow_LogOnly()
    {
        var ctx = ContextWithAudit(Audit(DenialRouteClass.RoutineRead, status: 403, outcome: "Forbidden"));
        Assert.Null(PolicyDenialAuditRowMiddleware.BuildRowIfAuditable(ctx));
    }

    [Fact]
    public void AllowedRequest_NoAnnotation_ProducesNoRow()
    {
        // No PolicyDenialAudit in Items = an allowed request (or a non-authorization short-circuit):
        // this middleware writes nothing, so it never double-writes with the allowed-request row.
        Assert.Null(PolicyDenialAuditRowMiddleware.BuildRowIfAuditable(ContextWithAudit(null)));
    }

    [Fact]
    public void EmptyCorrelationId_MapsToNullOnTheRow()
    {
        var audit = new PolicyDenialAudit("Forbidden", 403, "GlobalAdminOnly",
            DenialRouteClass.AdminOrMutating, "USR01", "Employee", "POST", "/api/x", Guid.Empty);
        var entry = PolicyDenialAuditRowMiddleware.BuildRowIfAuditable(ContextWithAudit(audit));
        Assert.NotNull(entry);
        Assert.Null(entry!.CorrelationId);
    }
}

/// <summary>
/// The result handler — <see cref="DenialLoggingAuthorizationResultHandler"/>. Proves it OBSERVES +
/// LOGS + ANNOTATES on a denial (RED if the emission is removed), applies the level discipline
/// (Information for routine reads, Warning for admin/mutating), DELEGATES to the built-in handler on
/// every path (403/401 on denial, proceed on success), redacts (no claim dump), and sanitizes CR/LF.
/// </summary>
public class DenialLoggingAuthorizationResultHandlerTests
{
    private sealed record Captured(string Category, LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<Captured> Records { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Records);
        public void Dispose() { }

        private sealed class Logger : ILogger
        {
            private readonly string _cat;
            private readonly ConcurrentQueue<Captured> _sink;
            public Logger(string cat, ConcurrentQueue<Captured> sink) { _cat = cat; _sink = sink; }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
                Func<TState, Exception?, string> formatter)
                => _sink.Enqueue(new Captured(_cat, level, formatter(state, ex)));
        }
    }

    // A no-op authentication scheme so the built-in AuthorizationMiddlewareResultHandler's
    // Forbid/Challenge (which resolve IAuthenticationService) set 403/401 without real auth.
    private sealed class StubAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public StubAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
            : base(o, l, e) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private static (DenialLoggingAuthorizationResultHandler Handler, CapturingLoggerProvider Logs, ServiceProvider Sp) BuildHandler()
    {
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => { b.AddProvider(logs); b.SetMinimumLevel(LogLevel.Trace); });
        services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, StubAuthHandler>("Test", _ => { });
        services.AddAuthorization();
        var sp = services.BuildServiceProvider();
        var handler = new DenialLoggingAuthorizationResultHandler(
            sp.GetRequiredService<ILogger<DenialLoggingAuthorizationResultHandler>>());
        return (handler, logs, sp);
    }

    private static DefaultHttpContext DenialContext(ServiceProvider sp, string method, string policy,
        IEnumerable<Claim>? claims = null)
    {
        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Request.Method = method;
        ctx.Request.Path = "/api/route";
        ctx.Items[CorrelationIdMiddleware.ItemKey] = Guid.NewGuid();
        if (claims is not null)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new AuthorizeAttribute(policy)), "test"));
        return ctx;
    }

    private static AuthorizationPolicy TrivialPolicy() =>
        new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();

    [Fact]
    public async Task Forbidden_AdminRoute_LogsWarning_Annotates_Delegates403_DoesNotCallNext()
    {
        var (handler, logs, sp) = BuildHandler();
        var ctx = DenialContext(sp, "POST", "GlobalAdminOnly",
            new[] { new Claim("sub", "USR01"), new Claim(StatsTidClaims.Role, "Employee") });
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        await handler.HandleAsync(next, ctx, TrivialPolicy(), PolicyAuthorizationResult.Forbid());

        // Delegation: the built-in handler produced the 403 and the denial short-circuited.
        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
        Assert.False(nextCalled);

        // Annotation for the audit-row middleware (RED if the emission is removed).
        var audit = Assert.IsType<PolicyDenialAudit>(ctx.Items[DenialLoggingAuthorizationResultHandler.ItemKey]);
        Assert.Equal(DenialRouteClass.AdminOrMutating, audit.RouteClass);
        Assert.Equal("Forbidden", audit.Outcome);
        Assert.Equal(StatusCodes.Status403Forbidden, audit.StatusCode);

        // Level discipline: admin/mutating denial → Warning.
        Assert.Contains(logs.Records, r => r.Level == LogLevel.Warning && r.Message.Contains("Authorization denied"));
    }

    [Fact]
    public async Task Challenged_RoutineRead_LogsInformation_Delegates401()
    {
        var (handler, logs, sp) = BuildHandler();
        var ctx = DenialContext(sp, "GET", "EmployeeOrAbove"); // unauthenticated challenge
        RequestDelegate next = _ => Task.CompletedTask;

        await handler.HandleAsync(next, ctx, TrivialPolicy(), PolicyAuthorizationResult.Challenge());

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        var audit = Assert.IsType<PolicyDenialAudit>(ctx.Items[DenialLoggingAuthorizationResultHandler.ItemKey]);
        Assert.Equal(DenialRouteClass.RoutineRead, audit.RouteClass);
        Assert.Null(audit.ActorId); // anonymous — no claims

        // Level discipline: routine read → Information, NOT Warning.
        Assert.Contains(logs.Records, r => r.Level == LogLevel.Information && r.Message.Contains("Authorization denied"));
        Assert.DoesNotContain(logs.Records, r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Success_Delegates_ProceedsToNext_NoDenialLogOrAnnotation()
    {
        var (handler, logs, sp) = BuildHandler();
        var ctx = DenialContext(sp, "GET", "GlobalAdminOnly");
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        await handler.HandleAsync(next, ctx, TrivialPolicy(), PolicyAuthorizationResult.Success());

        // Delegation on success → the request proceeds.
        Assert.True(nextCalled);
        // No false-positive: an allowed request gets neither a denial annotation nor a denial log,
        // and the audit-row gate therefore produces nothing.
        Assert.False(ctx.Items.ContainsKey(DenialLoggingAuthorizationResultHandler.ItemKey));
        Assert.DoesNotContain(logs.Records, r => r.Message.Contains("Authorization denied"));
        Assert.Null(PolicyDenialAuditRowMiddleware.BuildRowIfAuditable(ctx));
    }

    [Fact]
    public async Task Denial_DoesNotDumpClaims_AndSanitizesCrLf()
    {
        const string scopeSecret = "SECRET-SCOPE-VALUE-MUST-NOT-BE-LOGGED";
        var (handler, logs, sp) = BuildHandler();
        var ctx = DenialContext(sp, "POST", "GlobalAdminOnly", new[]
        {
            new Claim("sub", "attacker\r\n[FORGED] admin ok"),      // CR/LF forge attempt in an identifier
            new Claim(StatsTidClaims.Role, "Employee"),
            new Claim(StatsTidClaims.Scopes, scopeSecret),          // a claim we must NOT dump
        });
        RequestDelegate next = _ => Task.CompletedTask;

        await handler.HandleAsync(next, ctx, TrivialPolicy(), PolicyAuthorizationResult.Forbid());

        var messages = logs.Records.Select(r => r.Message).ToList();
        Assert.NotEmpty(messages);
        // Redaction: the scopes claim value never reaches any log line (no blanket claim dump).
        Assert.All(messages, m => Assert.DoesNotContain(scopeSecret, m));
        // Log-forging: no raw CR/LF survives into the rendered message.
        Assert.All(messages, m =>
        {
            Assert.DoesNotContain("\r", m);
            Assert.DoesNotContain("\n", m);
        });
    }
}
