using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S115 / TASK-11503 — the per-route spec≡runtime gate extended to the HR employee
/// FIELD-ENDPOINTS drained in Pass 2 (7 ops): birth-date GET+PUT, employment-start-date GET+PUT,
/// employment-end-date GET+PUT (backend truth — NO FE caller, asserted anyway), and the
/// entitlement-eligibility PUT. Each response is matched against its committed
/// <c>docs/api/openapi.json</c> DECLARED success contract via <see cref="SpecRuntimeMatcher"/>.
///
/// <para><b>The deferred op:</b> GET .../entitlement-eligibility/{entitlementType} is DELIBERATELY
/// NOT asserted — it stays grandfathered (genuinely polymorphic: its no-row branch OMITS the
/// effectiveFrom/version KEYS; the S112 flag-and-defer rule's first firing, see the manifest).</para>
///
/// <para><b>Dedicated-row seeding (the ordering guarantee):</b> xUnit does NOT guarantee
/// intra-class test order, so every op — including each READ — acts on its OWN seeded user (7
/// users; version=1 schema default). The strict-If-Match PUTs FETCH the ETag first (a plain GET on
/// the SAME dedicated user inside the same test) rather than assuming the seed version, exactly as
/// the FE composes the precondition. The eligibility PUT exercises the CREATE branch
/// (If-None-Match: * — no eligibility row is seeded; the toggle IS the create).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S115EmployeeFieldSpecRuntimeTests : IAsyncLifetime
{
    private const string Mao = "S115FMAO";
    private const string Org = "S115FORG";

    // One dedicated user per operation (see class doc).
    private const string BirthDateGetUser = "s115f_bdget";
    private const string BirthDatePutUser = "s115f_bdput";
    private const string StartDateGetUser = "s115f_sdget";
    private const string StartDatePutUser = "s115f_sdput";
    private const string EndDateGetUser = "s115f_edget";
    private const string EndDatePutUser = "s115f_edput";
    private const string EligibilityUser = "s115f_elig";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (baseline org tree)

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn);

        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Birth-date (HR-only DOB surface).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BirthDate_Get200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/admin/employees/{BirthDateGetUser}/birth-date"),
            "/api/admin/employees/{employeeId}/birth-date", "get");

    [Fact]
    public async Task BirthDate_Put200_SchemaMatchesRuntime()
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s115f_gadmin", Mao);
        var version = await FetchEtagVersionAsync(client, $"/api/admin/employees/{BirthDatePutUser}/birth-date");
        await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(_spec, client,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/admin/employees/{BirthDatePutUser}/birth-date",
                """{ "birthDate": "1990-05-17" }""", ifMatchVersion: version),
            "/api/admin/employees/{employeeId}/birth-date", "put");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Employment-start-date.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmploymentStartDate_Get200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/admin/employees/{StartDateGetUser}/employment-start-date"),
            "/api/admin/employees/{employeeId}/employment-start-date", "get");

    [Fact]
    public async Task EmploymentStartDate_Put200_SchemaMatchesRuntime()
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s115f_gadmin", Mao);
        var version = await FetchEtagVersionAsync(client, $"/api/admin/employees/{StartDatePutUser}/employment-start-date");
        await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(_spec, client,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/admin/employees/{StartDatePutUser}/employment-start-date",
                """{ "employmentStartDate": "2024-02-01" }""", ifMatchVersion: version),
            "/api/admin/employees/{employeeId}/employment-start-date", "put");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Employment-end-date (NO FE caller — asserted as backend truth). The PUT sets a
    //  FUTURE end date on a clean dedicated user: the R1 lifecycle keeps the user active
    //  (endDateDeactivated=false, isActive=true) and no settlement span exists → no 409.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmploymentEndDate_Get200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/admin/employees/{EndDateGetUser}/employment-end-date"),
            "/api/admin/employees/{employeeId}/employment-end-date", "get");

    [Fact]
    public async Task EmploymentEndDate_Put200_SchemaMatchesRuntime()
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s115f_gadmin", Mao);
        var version = await FetchEtagVersionAsync(client, $"/api/admin/employees/{EndDatePutUser}/employment-end-date");
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(_spec, client,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/admin/employees/{EndDatePutUser}/employment-end-date",
                """{ "employmentEndDate": "2030-12-31" }""", ifMatchVersion: version),
            "/api/admin/employees/{employeeId}/employment-end-date", "put");
        // Lifecycle pin: a FUTURE end date must NOT deactivate (guards the seed assumption).
        var root = JsonDocument.Parse(body).RootElement;
        Assert.True(root.GetProperty("isActive").GetBoolean(), "future end date unexpectedly deactivated the user");
        Assert.False(root.GetProperty("endDateDeactivated").GetBoolean());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Entitlement-eligibility PUT (CHILD_SICK) — the CREATE branch (If-None-Match: *;
    //  no eligibility row seeded — the absent-row default is the create signal).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EntitlementEligibility_Put200_CreateBranch_SchemaMatchesRuntime()
    {
        var request = SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put,
            $"/api/admin/employees/{EligibilityUser}/entitlement-eligibility/CHILD_SICK",
            """{ "eligible": true }""");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        await AssertOpAsync(request,
            "/api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}", "put");
    }

    // ── Support ──

    private async Task<string> AssertOpAsync(HttpRequestMessage request, string specPath, string method)
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s115f_gadmin", Mao);
        return await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(_spec, client, request, specPath, method);
    }

    /// <summary>GET the route and parse its ETag header into the If-Match version — the
    /// read-then-If-Match composition every strict field-PUT rides (ADR-019 D2).</summary>
    private static async Task<long> FetchEtagVersionAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(200, (int)response.StatusCode);
        var etag = response.Headers.ETag?.Tag;
        Assert.False(string.IsNullOrEmpty(etag), $"GET {url} returned no ETag header");
        return long.Parse(etag!.Trim('"'));
    }

    // ── Fixture seed — one MAO + one Organisation + one DEDICATED user per operation. ──
    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                ('S115FMAO', 'S115 Felt Ministerie', 'MAO',          NULL,       '/S115FMAO/',           'AC', 'OK24'),
                ('S115FORG', 'S115 Felt Styrelse',   'ORGANISATION', 'S115FMAO', '/S115FMAO/S115FORG/',  'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active) VALUES
                ('s115f_bdget', 's115f_bdget', '$2a$11$fake', 'S115 DOB Læser',      's115f_bdget@test.dk', 'S115FORG', 'HK', 'OK24', TRUE),
                ('s115f_bdput', 's115f_bdput', '$2a$11$fake', 'S115 DOB Skriver',    's115f_bdput@test.dk', 'S115FORG', 'HK', 'OK24', TRUE),
                ('s115f_sdget', 's115f_sdget', '$2a$11$fake', 'S115 Start Læser',    's115f_sdget@test.dk', 'S115FORG', 'HK', 'OK24', TRUE),
                ('s115f_sdput', 's115f_sdput', '$2a$11$fake', 'S115 Start Skriver',  's115f_sdput@test.dk', 'S115FORG', 'HK', 'OK24', TRUE),
                ('s115f_edget', 's115f_edget', '$2a$11$fake', 'S115 Slut Læser',     's115f_edget@test.dk', 'S115FORG', 'HK', 'OK24', TRUE),
                ('s115f_edput', 's115f_edput', '$2a$11$fake', 'S115 Slut Skriver',   's115f_edput@test.dk', 'S115FORG', 'HK', 'OK24', TRUE),
                ('s115f_elig',  's115f_elig',  '$2a$11$fake', 'S115 Ret Til Barn Syg','s115f_elig@test.dk', 'S115FORG', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
