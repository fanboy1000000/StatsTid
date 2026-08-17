using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StatsTid.Auth;
using StatsTid.Integrations.External.Services;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// SEC-023 — the authorization + payload-hardening end-to-end tests for the outbound
/// external-dispatch endpoint <c>POST /api/external/send</c>.
///
/// <para>
/// <b>What this proves, in plain terms:</b> before SEC-023 this endpoint required only the
/// <c>"Authenticated"</c> policy (any valid JWT — including the lowest <c>Employee</c> role) and
/// forwarded caller-supplied arbitrary JSON to the external system. Its direct sibling, the other
/// Orchestrator-dispatched outbound-integration action <c>/api/payroll/export</c>, requires
/// <c>GlobalAdminOnly</c>. The Orchestrator forwards the caller's JWT to whichever service it
/// dispatches, so the endpoint's role floor is the real control point. The fix (1) raises the floor
/// to <c>GlobalAdminOnly</c> and (2) hardens the body envelope BEFORE it is forwarded: a size cap
/// (256&#160;KB → 413) applied before deserialization, and an object-shape check (a bare
/// string/number/array/null body → 400). A valid JSON object still forwards unchanged — no
/// per-field schema is imposed, because the real external contract does not exist yet (deferred).
/// </para>
///
/// <para>
/// The five assertions map one-to-one to the owner ruling:
/// <list type="bullet">
/// <item>Employee-role token → <b>403</b> (was genuinely accepted — 200/422 — under the old
/// <c>Authenticated</c> floor).</item>
/// <item>Leader-role token → <b>403</b> (likewise).</item>
/// <item>GlobalAdmin token + a valid JSON object → <b>2xx</b> — the positive control proving the
/// raise did not lock out the legitimate caller (the outbound HTTP call is stubbed).</item>
/// <item>GlobalAdmin token + a non-object body → <b>400</b>.</item>
/// <item>GlobalAdmin token + an oversized body → <b>413</b>.</item>
/// </list>
/// A no-token → 401 control is added so the 403s are shown to be role decisions, not a blanket-open
/// endpoint.
/// </para>
///
/// <para>
/// <b>Docker:</b> NONE of these tests need a container. The External host's two background services
/// (<c>OutboxPublisher</c>, <c>EventConsumerService</c>) swallow all non-cancellation exceptions in
/// their poll loops, so the host boots with no live Postgres; the 401/403/400/413 paths short-circuit
/// (authorization runs before the handler; the size/shape checks run before any outbound call), and
/// the 200 happy-path's only side effect — the outbound HTTP POST — is replaced by an in-memory stub.
/// So the whole class runs in CI without <c>[Trait("Category","Docker")]</c>.
/// </para>
/// </summary>
public sealed class ExternalSendAuthorizationAndEnvelopeTests
    : IClassFixture<ExternalSendAuthorizationAndEnvelopeTests.ExternalHostFactory>
{
    private const string SendPath = "/api/external/send";

    private readonly ExternalHostFactory _factory;

    public ExternalSendAuthorizationAndEnvelopeTests(ExternalHostFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Employee-role token → 403. Under the pre-SEC-023 <c>Authenticated</c> floor this exact token
    /// was ACCEPTED (the handler forwarded the envelope and returned 200/422); the raised floor now
    /// rejects it as authenticated-but-not-authorized.
    /// </summary>
    [Fact]
    public async Task EmployeeRoleToken_IsForbidden()
    {
        var response = await PostSendAsync(
            role: StatsTidRoles.Employee,
            content: JsonContent.Create(new { eventType = "test", data = new { x = 1 } }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Leader-role token → 403. Same RED-before-green story as the Employee case: a Leader is still
    /// below <c>GlobalAdmin</c>, so the raised floor rejects it.
    /// </summary>
    [Fact]
    public async Task LeaderRoleToken_IsForbidden()
    {
        var response = await PostSendAsync(
            role: StatsTidRoles.LocalLeader,
            content: JsonContent.Create(new { eventType = "test", data = new { x = 1 } }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Positive control: a GlobalAdmin token with a valid JSON object envelope is ACCEPTED (2xx) and
    /// the endpoint reports a delivered message — proving the SEC-023 raise did not lock out the
    /// legitimate caller. The outbound external call is stubbed (see <see cref="ExternalHostFactory"/>),
    /// so no real external system or database is involved.
    /// </summary>
    [Fact]
    public async Task GlobalAdminToken_WithValidJsonObject_IsAcceptedAndForwardedUnchanged()
    {
        // A distinctive, well-formed object envelope (nested object + array), sent as a raw JSON
        // string so the forwarding contract has an unambiguous baseline to compare against.
        const string sentJson = "{\"eventType\":\"test\",\"data\":{\"x\":1,\"tags\":[\"a\",\"b\"]}}";

        var response = await PostSendAsync(
            role: StatsTidRoles.GlobalAdmin,
            content: new StringContent(sentJson, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(
            body.TryGetProperty("success", out var success) && success.GetBoolean(),
            "Expected the delivered-envelope success response from the stubbed external client.");

        // The endpoint must forward a valid JSON object UNCHANGED (no per-field schema — deferred).
        // Assert the body the external system actually received is semantically identical to what we
        // sent — this pins the byte-equivalent-forward contract against regression, rather than
        // trusting it by inspection. The stub captured the outbound request body.
        var forwarded = _factory.LastForwardedBody;
        Assert.NotNull(forwarded);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(sentJson), JsonNode.Parse(forwarded!)),
            $"Forwarded JSON differs from the sent envelope.\n  sent      = {sentJson}\n  forwarded = {forwarded}");
    }

    /// <summary>
    /// A GlobalAdmin token with a valid-JSON-but-non-object top-level body (array, bare string,
    /// number, or null) → 400. This exercises the object-shape envelope check specifically — the
    /// body parses fine as JSON, it just is not an object.
    /// </summary>
    [Theory]
    [InlineData("[1,2,3]")]      // array
    [InlineData("\"hello\"")]    // bare string
    [InlineData("42")]           // number
    [InlineData("null")]         // null literal
    public async Task GlobalAdminToken_WithNonObjectBody_IsBadRequest(string rawJson)
    {
        var response = await PostSendAsync(
            role: StatsTidRoles.GlobalAdmin,
            content: new StringContent(rawJson, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A GlobalAdmin token with a body over the 256&#160;KB cap → 413. The body is a well-formed JSON
    /// object (so this is a SIZE rejection, not a shape rejection) carrying a declared Content-Length
    /// over the cap; the endpoint rejects it before deserialization.
    /// </summary>
    [Fact]
    public async Task GlobalAdminToken_WithOversizedBody_IsPayloadTooLarge()
    {
        // Coverage note: this exercises the DECLARED-Content-Length path — the pre-deserialization
        // ContentLength check. The endpoint's second line of defence for a CHUNKED / no-Content-Length
        // oversize body (the IHttpMaxRequestBodySizeFeature backstop → BadHttpRequestException → 413)
        // is correct by construction but is NOT exercisable here: the in-memory WebApplicationFactory
        // TestServer does not expose IHttpMaxRequestBodySizeFeature, so a chunked-oversize test would be
        // vacuous. That backstop is verifiable only against a real Kestrel host (out of scope for this
        // harness); we deliberately do not add a chunked test.
        // ~300 KB object — comfortably over the 256 KB cap.
        var oversized = "{\"blob\":\"" + new string('a', 300 * 1024) + "\"}";

        var response = await PostSendAsync(
            role: StatsTidRoles.GlobalAdmin,
            content: new StringContent(oversized, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    /// <summary>
    /// No-token control: the endpoint challenges with 401 when unauthenticated — so the 403s above
    /// are genuine role decisions on a policy-guarded endpoint, not an artifact of an open route.
    /// </summary>
    [Fact]
    public async Task NoToken_IsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            SendPath,
            JsonContent.Create(new { eventType = "test" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostSendAsync(string role, HttpContent content)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, SendPath) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(role));
        return await client.SendAsync(request);
    }

    private static string MintToken(string role)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = ExternalHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        });

        // GlobalAdminOnly needs only the role claim (requireOrgScope: false) — no org scopes.
        return tokenService.GenerateToken(
            employeeId: "system:external-send-test",
            name: "External Send Test",
            role: role,
            agreementCode: "system");
    }

    /// <summary>
    /// Hosts the REAL External integration service (<see cref="ExternalApiClient"/> marks its
    /// assembly — its <c>Program</c> is internal top-level-statements and not otherwise nameable from
    /// the test project, mirroring how the Rule Engine harness uses <c>RuleRegistry</c>) in-process.
    ///
    /// <para>
    /// Two overrides make the host bootable and the happy-path hermetic:
    /// <list type="bullet">
    /// <item><see cref="CreateHost"/> injects <c>Jwt:SigningKey/Issuer/Audience</c> into HOST
    /// configuration (fires BEFORE <c>Program.cs</c> reads them for <c>AddStatsTidJwtAuth</c>) so
    /// minted tokens validate deterministically.</item>
    /// <item><see cref="ConfigureWebHost"/> replaces the outbound <see cref="IHttpClientFactory"/>
    /// with a stub returning a 200 + <c>messageId</c>, so the GlobalAdmin happy-path never touches a
    /// real external system. The last registration wins, so the singleton
    /// <see cref="ExternalApiClient"/> captures the stub.</item>
    /// </list>
    /// No database is provided: the host's background poll loops tolerate an unreachable Postgres, and
    /// none of the tested paths perform DB work.
    /// </para>
    /// </summary>
    public sealed class ExternalHostFactory : WebApplicationFactory<ExternalApiClient>
    {
        internal const string DevSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

        // One shared outbound stub so a test can read back what the endpoint forwarded.
        private readonly StubExternalHttpClientFactory _outboundStub = new();

        /// <summary>The raw JSON body of the most recent request the endpoint forwarded to the
        /// external system (captured by the outbound stub). Null until the first forward.</summary>
        public string? LastForwardedBody => _outboundStub.LastRequestBody;

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

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                // Replace the real outbound HttpClient factory so ExternalApiClient.SendAsync does
                // not need a live external system for the 200 happy-path. Last registration wins.
                services.AddSingleton<IHttpClientFactory>(_outboundStub);
            });
        }
    }

    /// <summary>An <see cref="IHttpClientFactory"/> whose clients always return a 200 + a JSON body
    /// carrying a <c>messageId</c> GUID — the shape <see cref="ExternalApiClient"/> parses on success —
    /// and CAPTURE the outbound request body so a test can assert what was forwarded to the external
    /// system. A single instance is shared as a singleton; tests in this class run sequentially (xUnit
    /// does not parallelise within a class), so the one capture slot needs no locking.</summary>
    private sealed class StubExternalHttpClientFactory : IHttpClientFactory
    {
        /// <summary>The raw body of the most recent outbound request forwarded to the external system.</summary>
        public string? LastRequestBody { get; private set; }

        public HttpClient CreateClient(string name) => new(new StubExternalHandler(this));

        private sealed class StubExternalHandler(StubExternalHttpClientFactory owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Content is not null)
                {
                    owner.LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { messageId = Guid.NewGuid().ToString() }),
                        Encoding.UTF8,
                        "application/json"),
                };
            }
        }
    }
}
