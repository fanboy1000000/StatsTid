using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Unit.Security;

/// <summary>
/// Tests for TASK-1803 (Codex BLOCKER #7 remediation) and TASK-1905 (Codex
/// WARNING #5 remediation):
///  1. Authorization-policy behaviour for GlobalAdminOnly and LocalAdminOrAbove
///     (as wired in AuthorizationPolicies.AddStatsTidPolicies and enforced by
///     ScopeAuthorizationHandler).
///  2. JWT signing-key fallback guard in JwtValidationSetup.AddStatsTidJwtAuth —
///     the dev fallback key MUST only be permitted when the host environment
///     reports IsDevelopment(), which honors both ASPNETCORE_ENVIRONMENT and
///     DOTNET_ENVIRONMENT (TASK-1905).
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
/// JWT signing-key fallback tests for JwtValidationSetup.AddStatsTidJwtAuth
/// (TASK-1905). Two layers:
///
///  1. Service-layer behaviour with an injected fake IHostEnvironment — pins
///     that the dev fallback only activates when IsDevelopment() returns true.
///     This is the contract our code controls.
///  2. Framework-layer integration — pins that the .NET 8 host actually
///     resolves DOTNET_ENVIRONMENT into IHostEnvironment.EnvironmentName, which
///     is the upstream guarantee the post-fix code relies on. Without this
///     test, the validation criterion "DOTNET_ENVIRONMENT=Development uses the
///     dev fallback" would only be defended by documentation.
///
/// The framework-integration test mutates a process-global env var and is in
/// its own non-parallel collection ("EnvVar") so it cannot interleave with
/// other tests reading the same variable.
/// </summary>
public class JwtValidationSetupTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "StatsTid.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration BuildConfig(string? signingKey)
    {
        var dict = new Dictionary<string, string?>();
        if (signingKey is not null)
        {
            dict["Jwt:SigningKey"] = signingKey;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // SPRINT-19 TASK-1905 criterion: "Both unset (or set to anything else) with
    // no Jwt:SigningKey throws InvalidOperationException at startup". An unset
    // host defaults to EnvironmentName="Production", but an explicit "Staging"
    // (or any other non-Development value) hits the same code path. Both
    // branches must fail fast — parameterising the test makes the "anything
    // else" intent explicit rather than collapsing it into a single Production
    // case.
    [Theory]
    [InlineData("Production")]   // The default an unset host produces.
    [InlineData("Staging")]      // Explicit non-Development value.
    [InlineData("")]             // Defensive: empty string is not "Development".
    public void AddStatsTidJwtAuth_ThrowsWhenSigningKeyMissingInNonDevelopment(string envName)
    {
        var services = new ServiceCollection();
        var cfg = BuildConfig(signingKey: null);
        var env = new FakeHostEnvironment { EnvironmentName = envName };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddStatsTidJwtAuth(cfg, env));

        Assert.Contains("Jwt:SigningKey", ex.Message);
    }

    [Fact]
    public void AddStatsTidJwtAuth_AllowsFallbackWhenIHostEnvironmentReportsDevelopment()
    {
        var services = new ServiceCollection();
        var cfg = BuildConfig(signingKey: null);
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };

        var returned = services.AddStatsTidJwtAuth(cfg, env);

        Assert.NotNull(returned);
        var provider = services.BuildServiceProvider();
        var settings = provider.GetService<JwtSettings>();
        Assert.NotNull(settings);
        Assert.False(string.IsNullOrWhiteSpace(settings!.SigningKey));
    }

    [Fact]
    public void AddStatsTidJwtAuth_UsesConfiguredKeyEvenInNonDevelopment()
    {
        const string explicitKey = "ProductionKey_MustBeAtLeast32BytesLong_OkForHmacSha256!";
        var services = new ServiceCollection();
        var cfg = BuildConfig(signingKey: explicitKey);
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };

        var returned = services.AddStatsTidJwtAuth(cfg, env);

        Assert.NotNull(returned);
        var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<JwtSettings>();
        Assert.Equal(explicitKey, settings.SigningKey);
    }
}

/// <summary>
/// Framework-integration test for TASK-1905: pins that the .NET 8 host honors
/// DOTNET_ENVIRONMENT when ASPNETCORE_ENVIRONMENT is unset. Pre-fix the
/// AddStatsTidJwtAuth code read ASPNETCORE_ENVIRONMENT directly and missed
/// this case; post-fix it uses IHostEnvironment.IsDevelopment() which the
/// framework populates from either env var. Without this test the criterion
/// "DOTNET_ENVIRONMENT=Development uses the dev fallback" is defended only by
/// documentation.
///
/// Mutates two process-global env vars so it lives in its own non-parallel
/// collection and saves/restores both.
/// </summary>
[Collection("EnvVar")]
public class JwtValidationFrameworkIntegrationTests
{
    private const string AspNetCoreEnv = "ASPNETCORE_ENVIRONMENT";
    private const string DotnetEnv = "DOTNET_ENVIRONMENT";

    [Fact]
    public void DotnetEnvironment_FlowsThroughHostEnvironmentAndUnlocksDevFallback()
    {
        var originalAspNet = Environment.GetEnvironmentVariable(AspNetCoreEnv);
        var originalDotnet = Environment.GetEnvironmentVariable(DotnetEnv);
        try
        {
            // Only DOTNET_ENVIRONMENT is set — exactly the launch shape the
            // Codex WARNING flagged.
            Environment.SetEnvironmentVariable(AspNetCoreEnv, null);
            Environment.SetEnvironmentVariable(DotnetEnv, Environments.Development);

            var builder = Host.CreateApplicationBuilder();

            // Framework guarantee: IHostEnvironment.IsDevelopment() honors both
            // env vars. Demonstrate it directly so the chain is visible in test
            // output, not just inferred from the AddStatsTidJwtAuth call below.
            Assert.True(builder.Environment.IsDevelopment(),
                "Host did not resolve DOTNET_ENVIRONMENT=Development into IHostEnvironment.IsDevelopment(). " +
                "If this assertion fails, the .NET hosting model changed and TASK-1905's premise needs re-evaluation.");

            // End-to-end: feed that real IHostEnvironment into our setup. With
            // no Jwt:SigningKey configured and only DOTNET_ENVIRONMENT set, the
            // dev fallback must activate without throwing.
            var cfg = new ConfigurationBuilder().Build();
            var services = new ServiceCollection();
            services.AddStatsTidJwtAuth(cfg, builder.Environment);

            var settings = services.BuildServiceProvider().GetRequiredService<JwtSettings>();
            Assert.False(string.IsNullOrWhiteSpace(settings.SigningKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AspNetCoreEnv, originalAspNet);
            Environment.SetEnvironmentVariable(DotnetEnv, originalDotnet);
        }
    }
}

/// <summary>
/// Policy-name wiring tests for AuthorizationPolicies.AddStatsTidPolicies
/// (TASK-1902 internal-Reviewer WARNING). Resolves each named policy via the real
/// IAuthorizationPolicyProvider so any typo in a [RequireAuthorization("...")]
/// call site or a dropped policy registration is caught at test time, not at
/// production runtime.
/// </summary>
public class AuthorizationPolicyWiringTests
{
    private static IAuthorizationPolicyProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStatsTidPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationPolicyProvider>();
    }

    [Theory]
    [InlineData("GlobalAdminOnly")]
    [InlineData("LocalAdminOrAbove")]
    [InlineData("HROrAbove")]
    [InlineData("LeaderOrAbove")]
    [InlineData("EmployeeOrAbove")]
    [InlineData("Authenticated")]
    public async Task AddStatsTidPolicies_RegistersAllExpectedPolicyNames(string policyName)
    {
        var provider = BuildProvider();

        var policy = await provider.GetPolicyAsync(policyName);

        Assert.NotNull(policy);
        Assert.NotEmpty(policy!.Requirements);
    }

    [Fact]
    public async Task GlobalAdminOnly_HasScopeRequirementWithGlobalAdminOnly()
    {
        var provider = BuildProvider();

        var policy = await provider.GetPolicyAsync("GlobalAdminOnly");

        Assert.NotNull(policy);
        var scopeReq = Assert.Single(policy!.Requirements.OfType<ScopeRequirement>());
        Assert.False(scopeReq.RequireOrgScope);
        Assert.Single(scopeReq.AllowedRoles);
        Assert.Contains(StatsTidRoles.GlobalAdmin, scopeReq.AllowedRoles);
    }

    [Fact]
    public async Task LocalAdminOrAbove_HasScopeRequirementWithGlobalAndLocalAdmin()
    {
        var provider = BuildProvider();

        var policy = await provider.GetPolicyAsync("LocalAdminOrAbove");

        Assert.NotNull(policy);
        var scopeReq = Assert.Single(policy!.Requirements.OfType<ScopeRequirement>());
        Assert.True(scopeReq.RequireOrgScope);
        Assert.Contains(StatsTidRoles.GlobalAdmin, scopeReq.AllowedRoles);
        Assert.Contains(StatsTidRoles.LocalAdmin, scopeReq.AllowedRoles);
    }

    [Fact]
    public async Task UnknownPolicyName_ReturnsNull()
    {
        var provider = BuildProvider();

        // Defensive: typos at RequireAuthorization sites resolve to null at runtime,
        // which is what the framework treats as "no policy" (denied).
        var policy = await provider.GetPolicyAsync("GlobalAdminOnlyy");

        Assert.Null(policy);
    }

    // -----------------------------------------------------------------------
    // Call-site coverage (TASK-1902 Reviewer WARNING follow-up).
    //
    // The InlineData test above asserts that AddStatsTidPolicies REGISTERS the
    // expected policy names. That catches "developer dropped a registration".
    // The asymmetric concern that motivated TASK-1902's policy-wiring test —
    // "policy-name typos at RequireAuthorization('...') CALL SITES would fail
    // the test suite" — is closed here: scan every .cs file under src/ for
    // RequireAuthorization("...") literals, then prove each resolves to a real
    // policy via the same IAuthorizationPolicyProvider production uses.
    //
    // A typo introduced at any future call site fails this test, even if the
    // policy registration set is unchanged.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task EveryRequireAuthorizationCallSite_ResolvesToARegisteredPolicy()
    {
        var srcDir = LocateRepoSrcDirectory();
        var policyNames = ScanRequireAuthorizationLiterals(srcDir);

        // Sanity guard: if the scan finds nothing, the test is silently broken
        // (e.g. the source layout moved). Pin a non-empty result so a future
        // refactor can't accidentally make this test trivially green.
        Assert.NotEmpty(policyNames);

        var provider = BuildProvider();
        var unresolved = new List<string>();
        foreach (var name in policyNames)
        {
            var policy = await provider.GetPolicyAsync(name);
            if (policy is null) unresolved.Add(name);
        }

        Assert.True(unresolved.Count == 0,
            $"RequireAuthorization call site(s) reference unregistered policy name(s): {string.Join(", ", unresolved)}. " +
            "Either the literal at the call site is a typo, or AddStatsTidPolicies needs the missing registration.");
    }

    private static string LocateRepoSrcDirectory()
    {
        // Walk up from the test bin output dir until we find the .sln, then
        // resolve src/. Robust against shadow-copy / per-test-host layouts.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
            {
                var src = Path.Combine(dir.FullName, "src");
                if (Directory.Exists(src)) return src;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repository src/ directory from test bin output. " +
            $"Searched upward from {AppContext.BaseDirectory}.");
    }

    private static IReadOnlyCollection<string> ScanRequireAuthorizationLiterals(string srcDir)
    {
        // RequireAuthorization("PolicyName") — only the string-literal form
        // matters for typo detection. The parameterless form (RequireAuthorization())
        // does not name a policy and is out of scope for this test.
        var pattern = new System.Text.RegularExpressions.Regex(
            @"RequireAuthorization\s*\(\s*""([^""]+)""\s*\)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            // Skip generated obj/ trees if anything ends up under src/.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in pattern.Matches(text))
                names.Add(m.Groups[1].Value);
        }
        return names;
    }
}
