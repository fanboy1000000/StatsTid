using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using StatsTid.Auth;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// SEC-027 (S130 task 3) — the POSITIVE accept-test for the least-privilege payroll
/// service-to-service token.
///
/// <para>
/// <b>What this proves, in plain terms:</b> the Payroll integration fetches wage-type rule
/// classifications from the Rule Engine's <c>GET /api/rules/classifications</c> endpoint by
/// minting its OWN service token (no user is on the call). Before SEC-027 that token carried
/// <c>role: "GlobalAdmin"</c> — the highest privilege in the system — even though the endpoint
/// only requires the <c>"Authenticated"</c> policy (any valid JWT; the handler never looks at
/// the role). The fix drops the token to the LOWEST role,
/// <see cref="StatsTidRoles.Employee"/>. The risk of that reduction is a silent break: if the
/// endpoint had actually needed more than <c>Authenticated</c>, an Employee-role token would be
/// rejected (401/403) and payroll wage-type classification would quietly degrade. This test
/// boots the REAL Rule Engine in-process and pins the accept path: an Employee-role service
/// token is ACCEPTED (HTTP 200) and returns the real classification inventory.
/// </para>
///
/// <para>
/// <b>Why there is no database container:</b> the Rule Engine has no persistence — it serves an
/// in-memory rule registry — so it is hosted directly with
/// <see cref="WebApplicationFactory{TEntryPoint}"/>. The generic marker
/// <see cref="RuleRegistry"/> is used only to locate the Rule Engine assembly's entry point
/// (its <c>Program</c> is internal and not otherwise reachable from the test project). The JWT
/// signing key + issuer/audience are injected into HOST configuration so the minted token
/// validates deterministically against a known key regardless of the ambient environment. One
/// host is shared for the whole class via <see cref="IClassFixture{TFixture}"/>.
/// </para>
/// </summary>
public sealed class RuleClassificationsLeastPrivilegeAcceptTests
    : IClassFixture<RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory>
{
    private const string ClassificationsPath = "/api/rules/classifications";

    private readonly RuleEngineHostFactory _factory;

    public RuleClassificationsLeastPrivilegeAcceptTests(RuleEngineHostFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// THE accept-test: an Employee-role service token — minted with the SAME subject, role,
    /// and agreement code that <c>HttpRuleClassificationProvider</c> now uses — is accepted
    /// (200) by the real <c>/api/rules/classifications</c> endpoint and returns a non-empty
    /// classification payload (so payroll wage-type classification still works).
    /// </summary>
    [Fact]
    public async Task EmployeeRoleServiceToken_IsAccepted_AndReturnsClassifications()
    {
        var client = _factory.CreateClient();
        var token = MintPayrollServiceToken(StatsTidRoles.Employee);

        var request = new HttpRequestMessage(HttpMethod.Get, ClassificationsPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.True(
            body.GetArrayLength() > 0,
            "Rule Engine returned an EMPTY classification inventory — payroll wage-type classification would silently degrade.");
        // Confirm the payload really is the classification inventory (each entry carries a ruleId),
        // not some coincidental empty/other 200 body.
        Assert.True(
            body[0].TryGetProperty("ruleId", out var ruleId) && !string.IsNullOrWhiteSpace(ruleId.GetString()),
            "First classification entry is missing a non-empty ruleId.");
    }

    /// <summary>
    /// Negative control proving the 200 above is EARNED rather than an open endpoint: with NO
    /// bearer the same endpoint rejects with 401. Together the two facts show the endpoint truly
    /// enforces authentication AND that the lowest (Employee) role clears it — so the SEC-027
    /// privilege reduction is safe.
    /// </summary>
    [Fact]
    public async Task NoToken_IsRejectedUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(ClassificationsPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Mirrors <c>HttpRuleClassificationProvider</c>'s mint exactly (subject / name /
    /// agreementCode), varying only the role — so the token under test has the real provider's
    /// service-token shape.
    /// </summary>
    private static string MintPayrollServiceToken(string role)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = RuleEngineHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        });

        return tokenService.GenerateToken(
            employeeId: "system:payroll-classification-provider",
            name: "Payroll Classification Provider",
            role: role,
            agreementCode: "system");
    }

    /// <summary>
    /// Hosts the REAL Rule Engine (<see cref="RuleRegistry"/> marks its assembly) in-process.
    /// Injects the JWT signing key + issuer/audience into HOST configuration via
    /// <see cref="IHostBuilder.ConfigureHostConfiguration"/> (which fires BEFORE the Rule
    /// Engine's <c>Program.cs</c> reads <c>builder.Configuration</c> for
    /// <c>AddStatsTidJwtAuth</c>), so the minted token validates against a known key regardless
    /// of the ambient environment. No database is required — the Rule Engine registers only an
    /// in-memory <see cref="RuleRegistry"/> and JWT auth.
    /// </summary>
    public sealed class RuleEngineHostFactory : WebApplicationFactory<RuleRegistry>
    {
        internal const string DevSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:SigningKey"] = DevSigningKey,
                    ["Jwt:Issuer"] = "statstid",
                    ["Jwt:Audience"] = "statstid",
                }));

            return base.CreateHost(builder);
        }
    }
}
