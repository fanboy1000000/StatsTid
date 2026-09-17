using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.SharedKernel.Calendar;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S112 / TASK-11204 — the per-route spec≡runtime gate extended to the EMPLOYEE-PROFILE family of
/// the newly-typed merged-admin slice (3 ops on <c>/api/admin/employee-profiles/{employeeId}</c>):
/// GET (200) / PUT (200) / DELETE (204). Each response is matched against its committed
/// <c>docs/api/openapi.json</c> DECLARED success contract via <see cref="SpecRuntimeMatcher"/>
/// (status fidelity + structural schema match; 204 = status + empty body).
///
/// <para><b>Dedicated-row seeding (the ordering guarantee):</b> xUnit does NOT guarantee intra-class
/// test order, so each op targets its OWN user + live <c>employee_profiles</c> row — the DELETE's
/// soft-delete can never 404 the GET/PUT assertions. All rows seed at version=1 (schema default);
/// PUT/DELETE use If-Match "1". The PUT's <c>effectiveFrom</c> is today — the <b>COPENHAGEN business
/// day</b> since S142 / TASK-14205, derived exactly as the endpoint derives it. It said "today (UTC)"
/// until S142, and the reason it had to be today has also changed: the ADR-023 D8 same-day validator
/// that once rejected anything else with 422 is gone (S138 admitted past dates, S141 the future), but
/// the PUT's covers-today pre-check still 404s an employee no row covers on the SERVER's today — so a
/// request dated on a different calendar from the server's would not reach the 200 whose schema this
/// gate compares.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S112EmployeeProfileSpecRuntimeTests : IAsyncLifetime
{
    private const string Mao = "S112PMAO";
    private const string Org = "S112PORG";

    private const string GetEmployee = "s112p_get";
    private const string PutEmployee = "s112p_put";
    private const string DeleteEmployee = "s112p_del";

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
    //  The per-route gate — each op's committed DECLARED success contract ≡ its real response.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProfileGet_200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/admin/employee-profiles/{GetEmployee}"),
            "get");

    [Fact]
    public async Task ProfilePut_200_SchemaMatchesRuntime()
    {
        // ADR-023 D8 (as it stood at S112): the validator accepted ONLY today. That validator is
        // gone since S138/S141, but a today-dated write is still what this op exercises. The seeded
        // predecessor sits at '0001-01-01', so this routes through Case C supersession — the
        // response shape is the same named record either way (EmployeeProfileResponse).
        //
        // S142 / TASK-14205 — "today" is the COPENHAGEN business day, derived here exactly as the
        // endpoint derives it. On the unpinned host this is a second, independent read of the same
        // wall clock, so only a matching CALENDAR keeps the two in agreement: with `DateTime.UtcNow`
        // the request would carry the 15th while the server called it the 16th for the one-to-two
        // hours after Danish midnight, and the PUT's covers-today pre-check would 404 rather than
        // return the 200 whose schema this gate compares.
        var today = CopenhagenBusinessDate.Today(TimeProvider.System).ToString("yyyy-MM-dd");
        await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/admin/employee-profiles/{PutEmployee}",
                $$"""{ "effectiveFrom": "{{today}}", "partTimeFraction": 0.8, "position": "Specialkonsulent" }""",
                ifMatchVersion: 1),
            "put");
    }

    [Fact]
    public async Task ProfileDelete_204_StatusAndEmptyBodyMatchRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete, $"/api/admin/employee-profiles/{DeleteEmployee}",
                jsonBody: null, ifMatchVersion: 1),
            "delete");

    private async Task AssertOpAsync(HttpRequestMessage request, string method)
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s112p_gadmin", Mao);
        await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, client, request, "/api/admin/employee-profiles/{employeeId}", method);
    }

    // ── Fixture seed — one MAO + one Organisation + one DEDICATED user+profile per assertion. ──
    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                (@mao, 'S112 Profil Ministerie', 'MAO',          NULL, '/S112PMAO/',           'AC', 'OK24'),
                (@org, 'S112 Profil Styrelse',   'ORGANISATION', @mao, '/S112PMAO/S112PORG/',  'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("mao", Mao);
            cmd.Parameters.AddWithValue("org", Org);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active) VALUES
                (@get, @get, '$2a$11$fake', 'S112 Profil Læs',  's112p_get@test.dk', @org, 'HK', 'OK24', TRUE),
                (@put, @put, '$2a$11$fake', 'S112 Profil Ret',  's112p_put@test.dk', @org, 'HK', 'OK24', TRUE),
                (@del, @del, '$2a$11$fake', 'S112 Profil Slet', 's112p_del@test.dk', @org, 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("get", GetEmployee);
            cmd.Parameters.AddWithValue("put", PutEmployee);
            cmd.Parameters.AddWithValue("del", DeleteEmployee);
            cmd.Parameters.AddWithValue("org", Org);
            await cmd.ExecuteNonQueryAsync();
        }

        // One live profile row per user (version=1 default; effective_to NULL = live). The GET seed
        // carries a NON-null position so the nullable string is exercised with a value.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, position, effective_from,
                                           employment_category) VALUES
                (@get, 1.000, 'Konsulent', '0001-01-01', (SELECT u.employment_category FROM users u WHERE u.user_id = @get)),
                (@put, 1.000, 'Konsulent', '0001-01-01', (SELECT u.employment_category FROM users u WHERE u.user_id = @put)),
                (@del, 1.000, 'Konsulent', '0001-01-01', (SELECT u.employment_category FROM users u WHERE u.user_id = @del))
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("get", GetEmployee);
            cmd.Parameters.AddWithValue("put", PutEmployee);
            cmd.Parameters.AddWithValue("del", DeleteEmployee);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
