using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Unit.Security;

/// <summary>
/// Tests for TASK-1803 (Codex BLOCKER #7 remediation):
///  1. Authorization-policy behaviour for GlobalAdminOnly and LocalAdminOrAbove
///     (as wired in AuthorizationPolicies.AddStatsTidPolicies and enforced by
///     ScopeAuthorizationHandler).
///  2. JWT signing-key fallback guard in JwtValidationSetup.AddStatsTidJwtAuth —
///     the dev fallback key MUST only be permitted when
///     ASPNETCORE_ENVIRONMENT=Development.
///
/// Env-var mutation tests are placed in a dedicated xUnit collection so they do
/// not race with other tests that may read the same variable.
/// </summary>
public class AuthorizationPolicyTests
{
    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static ClaimsPrincipal BuildPrincipal(string role, IEnumerable<RoleScope>? scopes = null)
    {
        var claims = new List<Claim>
        {
            new(StatsTidClaims.Role, role),
            new(StatsTidClaims.EmployeeId, "USR01")
        };

        if (scopes is not null)
        {
            var json = JsonSerializer.Serialize(scopes);
            claims.Add(new Claim(StatsTidClaims.Scopes, json));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> EvaluateAsync(ClaimsPrincipal principal, ScopeRequirement requirement)
    {
        var handler = new ScopeAuthorizationHandler();
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, principal, resource: null);

        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ScopeRequirement GlobalAdminOnlyRequirement() =>
        new(requireOrgScope: false, StatsTidRoles.GlobalAdmin);

    private static ScopeRequirement LocalAdminOrAboveRequirement() =>
        new(requireOrgScope: true, StatsTidRoles.GlobalAdmin, StatsTidRoles.LocalAdmin);

    // -----------------------------------------------------------------
    // GlobalAdminOnly
    // -----------------------------------------------------------------

    [Fact]
    public async Task GlobalAdminOnly_RejectsEmployeeToken()
    {
        // Arrange: an Employee principal with no GlobalAdmin role claim
        var principal = BuildPrincipal(StatsTidRoles.Employee);

        // Act
        var succeeded = await EvaluateAsync(principal, GlobalAdminOnlyRequirement());

        // Assert: policy must NOT succeed
        Assert.False(succeeded);
    }

    [Fact]
    public async Task GlobalAdminOnly_AcceptsGlobalAdminToken()
    {
        // Arrange: a GlobalAdmin principal (no org scope required for this policy)
        var principal = BuildPrincipal(StatsTidRoles.GlobalAdmin);

        // Act
        var succeeded = await EvaluateAsync(principal, GlobalAdminOnlyRequirement());

        // Assert
        Assert.True(succeeded);
    }

    [Fact]
    public async Task GlobalAdminOnly_RejectsLocalAdminToken()
    {
        // Arrange: a LocalAdmin — even with a valid org scope — is NOT a global admin
        var principal = BuildPrincipal(
            StatsTidRoles.LocalAdmin,
            new[] { new RoleScope(StatsTidRoles.LocalAdmin, "MIN01", "ORG_AND_DESCENDANTS") });

        // Act
        var succeeded = await EvaluateAsync(principal, GlobalAdminOnlyRequirement());

        // Assert: global-only policy must reject LocalAdmin
        Assert.False(succeeded);
    }

    // -----------------------------------------------------------------
    // LocalAdminOrAbove
    // -----------------------------------------------------------------

    [Fact]
    public async Task LocalAdminOrAbove_RejectsEmployeeToken()
    {
        // Arrange: Employee role is not in AllowedRoles for LocalAdminOrAbove
        var principal = BuildPrincipal(
            StatsTidRoles.Employee,
            new[] { new RoleScope(StatsTidRoles.Employee, "AFD01", "ORG_ONLY") });

        // Act
        var succeeded = await EvaluateAsync(principal, LocalAdminOrAboveRequirement());

        // Assert
        Assert.False(succeeded);
    }

    [Fact]
    public async Task LocalAdminOrAbove_AcceptsLocalAdminWithOrgScope()
    {
        // Arrange: LocalAdmin with a LocalAdmin-role scope claim — RequireOrgScope=true
        var principal = BuildPrincipal(
            StatsTidRoles.LocalAdmin,
            new[] { new RoleScope(StatsTidRoles.LocalAdmin, "MIN01", "ORG_AND_DESCENDANTS") });

        // Act
        var succeeded = await EvaluateAsync(principal, LocalAdminOrAboveRequirement());

        // Assert
        Assert.True(succeeded);
    }

    [Fact]
    public async Task LocalAdminOrAbove_RejectsLocalAdminWithoutOrgScope()
    {
        // Arrange: LocalAdmin with NO scopes claim at all — requireOrgScope=true must reject
        var principal = BuildPrincipal(StatsTidRoles.LocalAdmin, scopes: null);

        // Act
        var succeeded = await EvaluateAsync(principal, LocalAdminOrAboveRequirement());

        // Assert: without scopes, the org-scoped policy cannot succeed
        Assert.False(succeeded);
    }

    [Fact]
    public async Task LocalAdminOrAbove_AcceptsGlobalAdmin()
    {
        // Arrange: GlobalAdmin with a GLOBAL scope — must pass LocalAdminOrAbove as well
        var principal = BuildPrincipal(
            StatsTidRoles.GlobalAdmin,
            new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

        // Act
        var succeeded = await EvaluateAsync(principal, LocalAdminOrAboveRequirement());

        // Assert: GlobalAdmin is an allowed role and has a matching scope entry
        Assert.True(succeeded);
    }
}

/// <summary>
/// JWT signing-key fallback tests for JwtValidationSetup.AddStatsTidJwtAuth.
///
/// These tests mutate ASPNETCORE_ENVIRONMENT, so they are placed in a
/// non-parallel collection to avoid interleaving with other tests.
/// </summary>
[Collection("EnvVar")]
public class JwtValidationSetupTests
{
    private const string EnvVarName = "ASPNETCORE_ENVIRONMENT";

    private static IConfiguration BuildConfig(string? signingKey)
    {
        var dict = new Dictionary<string, string?>();
        if (signingKey is not null)
        {
            dict["Jwt:SigningKey"] = signingKey;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void AddStatsTidJwtAuth_ThrowsWhenSigningKeyMissingInProduction()
    {
        // Arrange: Production env, no signing key
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, "Production");
        try
        {
            var services = new ServiceCollection();
            var cfg = BuildConfig(signingKey: null);

            // Act + Assert: fail-fast refuses the dev fallback outside Development
            var ex = Assert.Throws<InvalidOperationException>(() =>
                services.AddStatsTidJwtAuth(cfg));

            Assert.Contains("Jwt:SigningKey", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void AddStatsTidJwtAuth_AllowsFallbackInDevelopment()
    {
        // Arrange: Development env, no signing key — dev fallback is permitted
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, "Development");
        try
        {
            var services = new ServiceCollection();
            var cfg = BuildConfig(signingKey: null);

            // Act + Assert: must NOT throw; JwtSettings should be registered
            var returned = services.AddStatsTidJwtAuth(cfg);

            Assert.NotNull(returned);
            var provider = services.BuildServiceProvider();
            var settings = provider.GetService<JwtSettings>();
            Assert.NotNull(settings);
            Assert.False(string.IsNullOrWhiteSpace(settings!.SigningKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void AddStatsTidJwtAuth_UsesConfiguredKeyWhenPresent()
    {
        // Arrange: Production env, explicit signing key — no throw, key is used
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, "Production");
        try
        {
            const string explicitKey = "ProductionKey_MustBeAtLeast32BytesLong_OkForHmacSha256!";
            var services = new ServiceCollection();
            var cfg = BuildConfig(signingKey: explicitKey);

            // Act
            var returned = services.AddStatsTidJwtAuth(cfg);

            // Assert: registration succeeds and the configured key is the one stored
            Assert.NotNull(returned);
            var provider = services.BuildServiceProvider();
            var settings = provider.GetRequiredService<JwtSettings>();
            Assert.Equal(explicitKey, settings.SigningKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }
}
