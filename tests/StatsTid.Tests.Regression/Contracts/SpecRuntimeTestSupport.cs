using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S112 / TASK-11204 — shared plumbing for the per-route spec≡runtime gate classes
/// (<see cref="OpenApiSpecRuntimeTests"/> S111 proof reads + the S112 per-family mutation classes
/// <c>S112UnitSpecRuntimeTests</c> / <c>S112AdminOrgUserRoleSpecRuntimeTests</c> /
/// <c>S112EmployeeProfileSpecRuntimeTests</c>): locating the committed spec, minting the
/// GlobalAdmin client, building requests, and the one-call per-operation assert
/// (resolve the DECLARED success contract → perform the REAL request → status + body matched).
/// </summary>
internal static class SpecRuntimeTestSupport
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>Locate + load the committed spec (walk up from the test bin dir for
    /// <c>docs/api/openapi.json</c>; mirrors the S111 loader).</summary>
    public static JsonElement LoadCommittedSpec()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "api", "openapi.json");
            if (File.Exists(candidate))
            {
                var json = File.ReadAllText(candidate);
                // Detach from the JsonDocument lifetime by cloning the root element.
                return JsonDocument.Parse(json).RootElement.Clone();
            }
            dir = dir.Parent;
        }
        throw new XunitException(
            "Could not locate docs/api/openapi.json by walking up from AppContext.BaseDirectory. " +
            "Regenerate it with `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi`.");
    }

    /// <summary>A client carrying a GlobalAdmin JWT (GLOBAL '/' scope) — passes every policy floor
    /// (HROrAbove / LocalAdminOrAbove) + every in-handler HasGlobalScope gate on the slice.</summary>
    public static HttpClient CreateGlobalAdminClient(StatsTidWebApplicationFactory factory, string actorId, string orgId)
    {
        var client = factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Build a request with an optional JSON body and an optional admin-strict
    /// <c>If-Match: "&lt;version&gt;"</c> header.</summary>
    public static HttpRequestMessage JsonRequest(HttpMethod method, string url, string? jsonBody = null, long? ifMatchVersion = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        if (ifMatchVersion is long version)
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return request;
    }

    /// <summary>
    /// THE per-operation gate call: resolve the operation's DECLARED success contract from the
    /// committed <paramref name="spec"/>, perform the REAL <paramref name="request"/>, and assert
    /// status fidelity + (200/201) the structural schema≡body match or (204) the empty body — via
    /// <see cref="SpecRuntimeMatcher.AssertSuccessMatches"/>. A non-2xx runtime status fails with the
    /// response body included (seed/authorization diagnostics); a WRONG 2xx still flows through the
    /// matcher (the status-lie path stays load-bearing). Returns the raw body for extra asserts
    /// (e.g. non-empty search items).
    /// </summary>
    public static async Task<string> AssertOperationMatchesRuntimeAsync(
        JsonElement spec, HttpClient client, HttpRequestMessage request, string specPath, string method)
    {
        var contract = SpecRuntimeMatcher.ResolveSuccessContract(spec, specPath, method);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var status = (int)response.StatusCode;
        if (status is < 200 or >= 300)
            throw new XunitException(
                $"{method.ToUpperInvariant()} {specPath}: expected the declared success {contract.DescribeStatuses()} " +
                $"but the endpoint returned {status}. Body: {body}");
        SpecRuntimeMatcher.AssertSuccessMatches(spec, contract, status, body, $"{method.ToUpperInvariant()} {specPath}");
        return body;
    }
}
