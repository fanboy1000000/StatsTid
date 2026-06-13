using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Http;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S73 / TASK-7300 (SPRINT-73 R1 + R1a) — the rule-engine auth-carriage pin suite.
///
/// <para><b>The incident:</b> three of the four backend→rule-engine call families used BARE
/// <c>IHttpClientFactory.CreateClient()</c> clients carrying no bearer, so in the composed stack
/// the rule engine answered 401 and the Backend mapped it to a blanket 503 (quota validation +
/// compliance dead); the fourth (check-overtime-governance) had its own AD-HOC Authorization
/// forwarder — two coexisting mechanisms = the wiring drift. TASK-7300 replaces all of it with
/// ONE mechanism: the named <see cref="RuleEngineClient.Name"/> client whose
/// <see cref="RuleEngineHeaderForwardingHandler"/> forwards the inbound <c>Authorization</c> +
/// <c>X-Correlation-Id</c>.</para>
///
/// <para><b>Mechanism:</b> the WAF keeps the REAL named-client registration (BaseAddress + the
/// forwarding DelegatingHandler) and replaces only the named client's PRIMARY handler with a
/// message-capture (via <see cref="HttpClientFactoryOptions"/>), so the capture sees exactly the
/// outgoing request that would leave the process — unlike the sibling suites' whole-factory
/// stub, which bypasses the handler pipeline entirely. validate-entitlement responses drive the
/// REAL <see cref="EntitlementValidationRule.Evaluate"/> (sibling-suite convention); the
/// compliance/governance routes serve a canned success result.</para>
///
/// <para><b>The R1a NEGATIVE pin:</b> the forwarding handler is registered ONLY on the named
/// client — a non-rule-engine outbound client created under the same ambient request context
/// carries NO forwarded Authorization (a global registration would leak user bearers to every
/// outbound host).</para>
///
/// <para>Seeding/token helpers mirror <see cref="Skema.Adr032ConsumptionPinTests"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RuleEngineAuthForwardingTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string ValidateEntitlementPath = "/api/rules/validate-entitlement";
    private const string CheckCompliancePath = "/api/rules/check-compliance";
    private const string CheckOvertimeGovernancePath = "/api/rules/check-overtime-governance";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // The 4-family POSITIVE pins (R1): each call site's outgoing rule-engine
    // request carries the inbound bearer + correlation id.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Family 1 — the SkemaEndpoints QUOTA validate-entitlement site (the post-S30 two-step
    /// quota loop). A VACATION save fires exactly one validate-entitlement call; it must carry
    /// the inbound bearer + X-Correlation-Id. Pre-TASK-7300 this site was a bare client (the
    /// composed-stack 503 of the incident).
    /// </summary>
    [Fact]
    public async Task SkemaSave_QuotaValidateEntitlementSite_ForwardsBearerAndCorrelationId()
    {
        var (client, capture) = CreateCaptureClient();
        var employeeId = await SeedEmployeeAsync(fraction: 1.000m);
        var (token, correlationId) = AuthorizeWithCorrelation(client, employeeId);

        var day = new DateOnly(2026, 5, 4); // Monday (the Adr032ConsumptionPinTests anchor)
        var rsp = await PostAbsencesAsync(client, employeeId, 2026, 5,
            new[] { (day, "VACATION", 7.4m) });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var validateCall = Assert.Single(capture.Requests(ValidateEntitlementPath));
        Assert.Equal($"Bearer {token}", validateCall.Authorization);
        Assert.Equal(correlationId, validateCall.CorrelationId);
    }

    /// <summary>
    /// Family 2 — the SkemaEndpoints SENIOR-GATE validate-entitlement site (S59 / PAT-005: the
    /// rule engine decides the age gate). A SENIOR_DAY save by a 66-year-old fires the senior
    /// gate call (identified by its <c>employeeAgeAsOfAbsenceDate</c> payload — the quota-loop
    /// body has no such field); it must carry the inbound bearer + X-Correlation-Id.
    /// </summary>
    [Fact]
    public async Task SkemaSave_SeniorGateValidateEntitlementSite_ForwardsBearerAndCorrelationId()
    {
        var (client, capture) = CreateCaptureClient();
        // 1960-01-01 ⇒ age 66 on the absence date (≥ the seeded SENIOR_DAY min_age 62), the
        // SkemaEntitlementEligibilityGuardTests allowed-case anchor.
        var employeeId = await SeedEmployeeAsync(fraction: 1.000m, birthDate: new DateOnly(1960, 1, 1));
        var (token, correlationId) = AuthorizeWithCorrelation(client, employeeId);

        var day = new DateOnly(2026, 3, 2); // Monday
        var rsp = await PostAbsencesAsync(client, employeeId, 2026, 3,
            new[] { (day, "SENIOR_DAY", 7.4m) });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var validateCalls = capture.Requests(ValidateEntitlementPath);
        var seniorGateCall = Assert.Single(
            validateCalls.Where(r => r.Body.Contains("employeeAgeAsOfAbsenceDate", StringComparison.Ordinal)));
        Assert.Equal($"Bearer {token}", seniorGateCall.Authorization);
        Assert.Equal(correlationId, seniorGateCall.CorrelationId);
        // Every validate-entitlement hop of the save (senior gate + the SENIOR_DAY quota call)
        // carries the same forwarded pair — no per-site drift inside one family.
        Assert.All(validateCalls, r =>
        {
            Assert.Equal($"Bearer {token}", r.Authorization);
            Assert.Equal(correlationId, r.CorrelationId);
        });
    }

    /// <summary>
    /// Family 3 — the ComplianceEndpoints check-compliance site (the SILENT half of the
    /// incident: compliance warnings 503'd in the composed stack). The outgoing request must
    /// carry the inbound bearer + X-Correlation-Id, and the endpoint passes the rule-engine
    /// result through as 200.
    /// </summary>
    [Fact]
    public async Task CompliancePeriod_CheckComplianceSite_ForwardsBearerAndCorrelationId()
    {
        var (client, capture) = CreateCaptureClient();
        var employeeId = await SeedEmployeeAsync(fraction: 1.000m);
        var (token, correlationId) = AuthorizeWithCorrelation(client, employeeId);

        var rsp = await client.GetAsync($"/api/compliance/{employeeId}/period?year=2026&month=5");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var complianceCall = Assert.Single(capture.Requests(CheckCompliancePath));
        Assert.Equal($"Bearer {token}", complianceCall.Authorization);
        Assert.Equal(correlationId, complianceCall.CorrelationId);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean()); // canned result passed through
    }

    /// <summary>
    /// Family 4 — the OvertimeEndpoints check-overtime-governance site. This was the ONE
    /// working site pre-TASK-7300 (its ad-hoc Authorization forwarder is now DELETED in favor
    /// of the central handler), so this pin doubles as the no-regression proof: the same bearer
    /// still crosses the hop, X-Correlation-Id is newly carried, and the externally observable
    /// response shape (the ComplianceCheckResult passthrough) is unchanged.
    /// </summary>
    [Fact]
    public async Task OvertimeGovernance_CheckOvertimeGovernanceSite_ForwardsBearerAndCorrelationId_ResponseShapeUnchanged()
    {
        var (client, capture) = CreateCaptureClient();
        var employeeId = await SeedEmployeeAsync(fraction: 1.000m);
        var (token, correlationId) = AuthorizeWithCorrelation(client, employeeId);

        var rsp = await client.GetAsync(
            $"/api/overtime/{employeeId}/governance?periodStart=2026-05-01&periodEnd=2026-05-31&overtimeHours=5");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var governanceCall = Assert.Single(capture.Requests(CheckOvertimeGovernancePath));
        Assert.Equal($"Bearer {token}", governanceCall.Authorization);
        Assert.Equal(correlationId, governanceCall.CorrelationId);

        // Response-shape pin (the pre-S73 passthrough contract, byte-level field set).
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("ruleId", out _));
        Assert.True(body.TryGetProperty("employeeId", out _));
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("violations").ValueKind);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("warnings").ValueKind);
    }

    // ════════════════════════════════════════════════════════════════════════
    // The R1a NEGATIVE pin + the no-header edge pins.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// R1a — the forwarding handler attaches ONLY to the named rule-engine client. Under the
    /// SAME ambient request context (an HttpContext carrying Authorization + X-Correlation-Id),
    /// a DEFAULT (non-rule-engine) outbound client carries NEITHER forwarded header, while the
    /// named client carries BOTH (the in-test positive control proving the ambient context was
    /// live when the default client sent). A global registration would leak the user's bearer
    /// to every outbound host.
    /// </summary>
    [Fact]
    public async Task R1aNegativePin_DefaultOutboundClient_SameRequestContext_CarriesNoForwardedHeaders()
    {
        var namedCapture = new RuleEngineCaptureHandler();
        var defaultCapture = new RuleEngineCaptureHandler();
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.Configure<HttpClientFactoryOptions>(RuleEngineClient.Name,
                    o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = namedCapture));
                services.Configure<HttpClientFactoryOptions>(Options.DefaultName,
                    o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = defaultCapture));
            });
        });

        var accessor = factory.Services.GetRequiredService<IHttpContextAccessor>();
        var clientFactory = factory.Services.GetRequiredService<IHttpClientFactory>();

        var ambient = new DefaultHttpContext();
        ambient.Request.Headers["Authorization"] = "Bearer ambient-user-token";
        ambient.Request.Headers["X-Correlation-Id"] = "ambient-correlation-id";
        accessor.HttpContext = ambient;
        try
        {
            // A non-rule-engine outbound client created under the live request context.
            var defaultClient = clientFactory.CreateClient();
            _ = await defaultClient.GetAsync("http://other-service.example/ping");

            // The positive control: the NAMED client under the SAME context forwards both.
            var namedClient = clientFactory.CreateClient(RuleEngineClient.Name);
            _ = await namedClient.GetAsync("/api/rules/forwarding-probe");
        }
        finally
        {
            accessor.HttpContext = null;
        }

        var defaultRequest = Assert.Single(defaultCapture.Requests("/ping"));
        Assert.False(defaultRequest.HasAuthorization,
            "R1a: a non-rule-engine outbound client must NOT carry the forwarded user bearer.");
        Assert.False(defaultRequest.HasCorrelationId,
            "R1a: a non-rule-engine outbound client must NOT carry the forwarded correlation id.");

        var namedRequest = Assert.Single(namedCapture.Requests("/api/rules/forwarding-probe"));
        Assert.Equal("Bearer ambient-user-token", namedRequest.Authorization);
        Assert.Equal("ambient-correlation-id", namedRequest.CorrelationId);
    }

    /// <summary>
    /// Absent inbound X-Correlation-Id ⇒ NO outgoing X-Correlation-Id header at all — never an
    /// empty value (pinned end-to-end through the governance family; the bearer still forwards).
    /// </summary>
    [Fact]
    public async Task OvertimeGovernance_AbsentInboundCorrelationId_OmitsHeaderEntirely()
    {
        var (client, capture) = CreateCaptureClient();
        var employeeId = await SeedEmployeeAsync(fraction: 1.000m);
        var token = MintEmployeeToken(employeeId, OrgId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Deliberately NO X-Correlation-Id on the inbound request.

        var rsp = await client.GetAsync(
            $"/api/overtime/{employeeId}/governance?periodStart=2026-05-01&periodEnd=2026-05-31&overtimeHours=5");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var governanceCall = Assert.Single(capture.Requests(CheckOvertimeGovernancePath));
        Assert.Equal($"Bearer {token}", governanceCall.Authorization);
        Assert.False(governanceCall.HasCorrelationId,
            "An absent inbound X-Correlation-Id must produce NO outgoing header — never an empty value.");
    }

    /// <summary>
    /// No ambient HttpContext (the background-caller posture) ⇒ the named client forwards
    /// NOTHING — the handler never invents or empties headers. A future Backend background
    /// caller must MINT its own service token (the S20 Payroll HttpRuleClassificationProvider
    /// precedent), not piggyback on this handler (the R1 partition).
    /// </summary>
    [Fact]
    public async Task NamedClient_NoAmbientHttpContext_ForwardsNothing()
    {
        var capture = new RuleEngineCaptureHandler();
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.Configure<HttpClientFactoryOptions>(RuleEngineClient.Name,
                    o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = capture));
            });
        });

        var accessor = factory.Services.GetRequiredService<IHttpContextAccessor>();
        var clientFactory = factory.Services.GetRequiredService<IHttpClientFactory>();
        accessor.HttpContext = null; // explicit: the background-caller posture

        var namedClient = clientFactory.CreateClient(RuleEngineClient.Name);
        _ = await namedClient.GetAsync("/api/rules/background-probe");

        var request = Assert.Single(capture.Requests("/api/rules/background-probe"));
        Assert.False(request.HasAuthorization, "No HttpContext ⇒ no Authorization header (MINT is the caller's job, never this handler's).");
        Assert.False(request.HasCorrelationId, "No HttpContext ⇒ no X-Correlation-Id header.");
    }

    // ── WAF wiring: replace ONLY the named client's primary handler ──

    private (HttpClient Client, RuleEngineCaptureHandler Capture) CreateCaptureClient()
    {
        var capture = new RuleEngineCaptureHandler();
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Keep the production registration (BaseAddress + the forwarding handler);
                // swap only the primary transport so no real socket is opened.
                services.Configure<HttpClientFactoryOptions>(RuleEngineClient.Name,
                    o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = capture));
            });
        });
        return (factory.CreateClient(), capture);
    }

    private static (string Token, string CorrelationId) AuthorizeWithCorrelation(
        HttpClient client, string employeeId)
    {
        var token = MintEmployeeToken(employeeId, OrgId);
        var correlationId = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        return (token, correlationId);
    }

    private static async Task<HttpResponseMessage> PostAbsencesAsync(
        HttpClient client, string employeeId, int year, int month,
        (DateOnly Date, string Type, decimal Hours)[] absences)
    {
        var request = new
        {
            year,
            month,
            absences = absences.Select(a => new
            {
                date = a.Date.ToString("yyyy-MM-dd"),
                absenceType = a.Type,
                hours = a.Hours,
            }).ToArray(),
        };
        return await client.PostAsJsonAsync($"/api/skema/{employeeId}/save", request);
    }

    // ── Scenario seeding (mirrors Adr032ConsumptionPinTests) ──

    /// <summary>
    /// Fresh AC/OK24 employee: users row (+ optional birth_date for the senior gate), an OPEN
    /// dated AC agreement-code row and an OPEN dated profile row from '0001-01-01'.
    /// </summary>
    private async Task<string> SeedEmployeeAsync(decimal fraction, DateOnly? birthDate = null)
    {
        var employeeId = "emp_s73_authfwd_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active, birth_date)
            VALUES (@u, @u, 'dev-only', 'S73 Auth Forwarding Pin User', NULL, @org, 'AC', 'OK24', TRUE, @dob)
            """, conn))
        {
            cmd.Parameters.AddWithValue("u", employeeId);
            cmd.Parameters.AddWithValue("org", OrgId);
            cmd.Parameters.AddWithValue("dob", (object?)birthDate ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes
                (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, 'AC', @from, NULL, 1)
            ON CONFLICT (user_id, effective_from) DO UPDATE SET agreement_code = EXCLUDED.agreement_code
            """, conn))
        {
            cmd.Parameters.AddWithValue("u", employeeId);
            cmd.Parameters.AddWithValue("from", new DateOnly(1, 1, 1));
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles
                (profile_id, employee_id, part_time_fraction, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @e, @f, @from, NULL, 1)
            ON CONFLICT (employee_id, effective_from) DO UPDATE SET part_time_fraction = EXCLUDED.part_time_fraction
            """, conn))
        {
            cmd.Parameters.AddWithValue("e", employeeId);
            cmd.Parameters.AddWithValue("f", fraction);
            cmd.Parameters.AddWithValue("from", new DateOnly(1, 1, 1));
            await cmd.ExecuteNonQueryAsync();
        }

        return employeeId;
    }

    // ── Token minting (mirrors Adr032ConsumptionPinTests) ──

    private static string MintEmployeeToken(string actorId, string orgId)
    {
        var tokenService = new JwtTokenService(DevSettings());
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    // ── The message-capture primary handler ──

    /// <summary>
    /// Records every outgoing request (path + the two forwarded headers + body) and serves:
    /// validate-entitlement via the REAL <see cref="EntitlementValidationRule.Evaluate"/>
    /// (sibling-suite convention), check-compliance / check-overtime-governance via a canned
    /// success <c>ComplianceCheckResult</c>, anything else 404.
    /// </summary>
    private sealed class RuleEngineCaptureHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public sealed record CapturedRequest(
            string Path,
            string? Authorization,
            bool HasAuthorization,
            string? CorrelationId,
            bool HasCorrelationId,
            string Body);

        private readonly List<CapturedRequest> _requests = new();

        public IReadOnlyList<CapturedRequest> Requests(string pathSuffix)
        {
            lock (_requests)
                return _requests.Where(r => r.Path.EndsWith(pathSuffix, StringComparison.Ordinal)).ToList();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var hasAuth = request.Headers.TryGetValues("Authorization", out var authValues);
            var hasCorr = request.Headers.TryGetValues("X-Correlation-Id", out var corrValues);
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (_requests)
            {
                _requests.Add(new CapturedRequest(
                    path,
                    hasAuth ? string.Join(",", authValues!) : null,
                    hasAuth,
                    hasCorr ? string.Join(",", corrValues!) : null,
                    hasCorr,
                    body));
            }

            if (path.EndsWith(ValidateEntitlementPath, StringComparison.Ordinal))
            {
                var req = JsonSerializer.Deserialize<ValidateEntitlementRequest>(body, Camel)!;
                var result = EntitlementValidationRule.Evaluate(req);
                return JsonResponse(JsonSerializer.Serialize(result, Camel));
            }

            if (path.EndsWith(CheckCompliancePath, StringComparison.Ordinal)
                || path.EndsWith(CheckOvertimeGovernancePath, StringComparison.Ordinal))
            {
                // Canned success ComplianceCheckResult (camelCase, the wire shape both
                // endpoints deserialize and pass through).
                return JsonResponse(
                    """{"ruleId":"S73_TEST_RULE","employeeId":"s73-canned","success":true,"violations":[],"warnings":[]}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
