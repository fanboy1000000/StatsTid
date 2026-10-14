using System.Net;
using System.Text.Json;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;

namespace StatsTid.Tests.Regression.TestSupport;

/// <summary>
/// S145 O-10 — the one shared stub for the rule-engine's <c>POST /api/rules/validate-entitlement</c>
/// route (consolidates the four S145 copies that R50 / R62 made necessary).
///
/// <para>
/// Plain language: the in-process backend host has no real rule-engine service to call, so a test
/// plugs this handler in as the HTTP client. It answers the entitlement-validation route by running
/// the REAL <see cref="EntitlementValidationRule.Evaluate"/> on the request, and answers every other
/// path with 404. Behaviour is identical to the four private copies it replaced.
/// </para>
/// </summary>
internal sealed class EntitlementValidationStubHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (!path.EndsWith("/api/rules/validate-entitlement", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        var json = await request.Content!.ReadAsStringAsync(cancellationToken);
        var req = JsonSerializer.Deserialize<ValidateEntitlementRequest>(json, Camel)!;
        var result = EntitlementValidationRule.Evaluate(req);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(result, Camel), System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
