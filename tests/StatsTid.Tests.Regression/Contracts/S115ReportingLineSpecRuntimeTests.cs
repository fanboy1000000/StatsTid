using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S115 / TASK-11503 — the per-route spec≡runtime gate extended to the REPORTING-LINES admin
/// family drained in Pass 2 (13 ops): the two CONDITIONAL 201-or-200 assigns (PRIMARY POST +
/// ACTING POST — the program's first HOMOGENEOUS multi-2xx contracts, ONE shared
/// <c>ReportingLineResponse</c> <c>$ref</c> behind both statuses), the two true-204 DELETEs
/// (PRIMARY + ACTING removes), the two BARE arrays (tree + reports), the two envelopes (employee
/// lines {active,history} + period-status), the import + remove-with-reassignment 200 receipts,
/// and the vikar trio (GET nullable-complex envelope / POST 200-by-contract / DELETE genuine
/// 200-with-body). Each response is matched against its committed <c>docs/api/openapi.json</c>
/// DECLARED success contract via <see cref="SpecRuntimeMatcher"/>.
///
/// <para><b>The 2-branch conditional proofs (load-bearing):</b> each conditional POST is proven on
/// BOTH branches from TWO dedicated seed states — a VIRGIN employee (no line → the REAL runtime
/// status is asserted to be 201) and a PREDECESSOR-CARRYING employee (a pre-seeded PRIMARY/ACTING
/// line at version=1 → If-Match reassign → the REAL runtime status is asserted to be 200). The
/// reassign branch is NEVER proven by mutating the first-assign row — separate rows, separate
/// Organisations (each scenario gets its OWN Organisation so the reporting-tree invariants — the
/// DELETE's multi-root census, the assigns' cycle guard — can never couple sibling assertions).</para>
///
/// <para><b>Dedicated-row seeding (the ordering guarantee):</b> xUnit does NOT guarantee
/// intra-class test order, so every MUTATION acts on its OWN seeded rows (one Organisation + its
/// own users/lines/vikar rows per assertion) — a DELETE assertion can never invalidate a sibling
/// assertion's row. All versioned rows seed at version=1 (schema default), so each If-Match is
/// deterministic. ONE Docker fixture for the whole FAMILY (FAIL-002 discipline).</para>
///
/// <para><b>The multi-2xx RED-on-lie proof:</b> a REAL 201 first-assign response (its own
/// dedicated seed pair) is matched against a SYNTHETIC multi-status contract whose declared set
/// ({200, 202}) OMITS the actual 201 — the matcher MUST stay RED on an undeclared runtime status
/// even when the contract is a multi-2xx set (extends the S112 injected-status-lie pattern to
/// the S115 / TASK-11500 set-shaped contract).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S115ReportingLineSpecRuntimeTests : IAsyncLifetime
{
    private const string Mao = "S115RMAO";

    // One dedicated Organisation (= one reporting tree) per scenario — see class doc.
    private const string AssignVirginOrg = "S115RL01";
    private const string AssignReassignOrg = "S115RL02";
    private const string PrimaryDeleteOrg = "S115RL03";
    private const string ReadOrg = "S115RL04";          // read-only: tree / lines / reports / period-status
    private const string ActingVirginOrg = "S115RL05";
    private const string ActingReassignOrg = "S115RL06";
    private const string ActingDeleteOrg = "S115RL07";
    private const string ImportOrg = "S115RL08";
    private const string RemoveOrg = "S115RL09";
    private const string VikarWithOrg = "S115RL10";
    private const string VikarNoneOrg = "S115RL11";
    private const string VikarCreateOrg = "S115RL12";
    private const string VikarRevokeOrg = "S115RL13";
    private const string RedProofOrg = "S115RL99";      // the multi-2xx RED proof's own virgin pair

    // Pre-seeded manager_vikar rows (fixed ids; version=1 schema default).
    private static readonly Guid VikarWithId = Guid.Parse("51150000-0000-0000-0000-000000000001");
    private static readonly Guid VikarRevokeId = Guid.Parse("51150000-0000-0000-0000-000000000002");

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
    //  The CONDITIONAL POSTs — both branches proven on dedicated seed states.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>PRIMARY assign, the VIRGIN branch: no existing line + If-None-Match: * →
    /// the runtime MUST serve 201, and the body must match the shared multi-2xx schema.</summary>
    [Fact]
    public async Task AssignPrimary_FirstAssignment_201Branch_SchemaMatchesRuntime()
    {
        var request = WithIfNoneMatchStar(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/reporting-lines",
            """{ "employeeId": "s115r_e01", "managerId": "s115r_m01", "effectiveFrom": "2026-01-01" }"""));
        var (status, body) = await SendAsync(request);

        Assert.Equal(201, status); // the first-assign branch REALLY produced 201
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/reporting-lines", "post");
        Assert.Equal(2, truth.StatusCodes.Count);   // the committed contract IS the multi-2xx set
        Assert.Contains(201, truth.StatusCodes);
        Assert.Contains(200, truth.StatusCodes);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, status, body, "POST /api/admin/reporting-lines (201 first-assign)");
    }

    /// <summary>PRIMARY assign, the PREDECESSOR branch: a pre-seeded PRIMARY line (version=1) +
    /// If-Match reassign to a NEW manager → the runtime MUST serve 200 from the SAME shared schema.
    /// Dedicated row — never the first-assign scenario's employee.</summary>
    [Fact]
    public async Task AssignPrimary_Reassignment_200Branch_SchemaMatchesRuntime()
    {
        var request = SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/reporting-lines",
            """{ "employeeId": "s115r_e02", "managerId": "s115r_m02b", "effectiveFrom": "2026-01-01" }""",
            ifMatchVersion: 1);
        var (status, body) = await SendAsync(request);

        Assert.Equal(200, status); // the supersession branch REALLY produced 200
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/reporting-lines", "post");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, status, body, "POST /api/admin/reporting-lines (200 reassign)");
    }

    /// <summary>ACTING assign, the VIRGIN branch: no existing ACTING line + If-None-Match: * → 201.</summary>
    [Fact]
    public async Task AssignActing_FirstAssignment_201Branch_SchemaMatchesRuntime()
    {
        var request = WithIfNoneMatchStar(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/reporting-lines/s115r_e05/acting",
            """{ "managerId": "s115r_m05", "effectiveFrom": "2026-01-01" }"""));
        var (status, body) = await SendAsync(request);

        Assert.Equal(201, status);
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/reporting-lines/{employeeId}/acting", "post");
        Assert.Equal(2, truth.StatusCodes.Count);
        Assert.Contains(201, truth.StatusCodes);
        Assert.Contains(200, truth.StatusCodes);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, status, body, "POST .../acting (201 first-assign)");
    }

    /// <summary>ACTING assign, the PREDECESSOR branch: a pre-seeded ACTING line (version=1) +
    /// If-Match reassign → 200. Dedicated row — never the acting first-assign's employee.</summary>
    [Fact]
    public async Task AssignActing_Reassignment_200Branch_SchemaMatchesRuntime()
    {
        var request = SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/reporting-lines/s115r_e06/acting",
            """{ "managerId": "s115r_m06b", "effectiveFrom": "2026-01-01" }""",
            ifMatchVersion: 1);
        var (status, body) = await SendAsync(request);

        Assert.Equal(200, status);
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/reporting-lines/{employeeId}/acting", "post");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, status, body, "POST .../acting (200 reassign)");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The true-204 DELETEs (status + empty body; If-Match from the seeded line version).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PrimaryLineRemove_Delete204_StatusAndEmptyBodyMatchRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete, "/api/admin/reporting-lines/s115r_e03",
                jsonBody: null, ifMatchVersion: 1),
            "/api/admin/reporting-lines/{employeeId}", "delete");

    [Fact]
    public async Task ActingLineRemove_Delete204_StatusAndEmptyBodyMatchRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete, "/api/admin/reporting-lines/s115r_e07/acting",
                jsonBody: null, ifMatchVersion: 1),
            "/api/admin/reporting-lines/{employeeId}/acting", "delete");

    // ════════════════════════════════════════════════════════════════════════════════
    //  The reads — bare arrays + envelopes, on the read-only Organisation.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Tree_Get200_BareArraySchemaMatchesRuntime()
    {
        var body = await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/admin/reporting-lines/tree/{ReadOrg}"),
            "/api/admin/reporting-lines/tree/{organisationId}", "get");
        // Non-vacuous: the seeded line is in the array (the item schema was actually exercised).
        Assert.True(JsonDocument.Parse(body).RootElement.GetArrayLength() >= 1, "tree read returned an empty array — seed missing");
    }

    [Fact]
    public async Task EmployeeLines_Get200_EnvelopeSchemaMatchesRuntime()
    {
        var body = await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/admin/reporting-lines/s115r_e04"),
            "/api/admin/reporting-lines/{employeeId}", "get");
        // Non-vacuous: the active set carries the seeded line (the element schema was exercised).
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("active").GetArrayLength() >= 1,
            "employee-lines read returned an empty active set — seed missing");
    }

    [Fact]
    public async Task DirectReports_Get200_BareArraySchemaMatchesRuntime()
    {
        var body = await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/admin/reporting-lines/s115r_m04/reports"),
            "/api/admin/reporting-lines/{managerId}/reports", "get");
        Assert.True(JsonDocument.Parse(body).RootElement.GetArrayLength() >= 1,
            "direct-reports read returned an empty array — seed missing");
    }

    [Fact]
    public async Task TreePeriodStatus_Get200_EnvelopeSchemaMatchesRuntime()
    {
        var body = await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/admin/reporting-lines/tree/{ReadOrg}/period-status"),
            "/api/admin/reporting-lines/tree/{organisationId}/period-status", "get");
        // Non-vacuous: every active employee in the styrelse projects a row (e04 + m04 seeded).
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("employees").GetArrayLength() >= 1,
            "period-status read returned an empty employees set — seed missing");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Import + remove-with-reassignment (200 receipts; NO precondition by contract).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Import_Post200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/admin/reporting-lines/import",
                $$"""
                { "organisationId": "{{ImportOrg}}",
                  "rows": [ { "employeeId": "s115r_e08", "managerId": "s115r_m08", "effectiveFrom": "2026-01-01" } ] }
                """),
            "/api/admin/reporting-lines/import", "post");

    [Fact]
    public async Task RemoveWithReassignment_Post200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/admin/reporting-lines/s115r_p09/remove",
                """{ "replacements": {} }"""),
            "/api/admin/reporting-lines/{employeeId}/remove", "post");

    // ════════════════════════════════════════════════════════════════════════════════
    //  The vikar trio — the GET proven on BOTH branches (object AND the null member: since
    //  S117 fired the nullable-$ref escalation, activeVikar emits as the nullable-complex
    //  WRAPPER [type: object + allOf: [$ref] + nullable: true] and IS required — the null
    //  branch exercises the wrapper's null admission, the object branch the matcher's
    //  resolve-THROUGH-the-wrapper recursion. These two pins are the mechanism's LIVE
    //  re-proof on a real route; their assertions are unchanged from S115.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task VikarGet_WithActiveVikar_200_ObjectBranchSchemaMatchesRuntime()
    {
        var body = await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/admin/reporting-lines/s115r_m10/vikar"),
            "/api/admin/reporting-lines/{managerId}/vikar", "get");
        // Branch pin: the seeded row REALLY surfaced as the object (the nested schema was exercised).
        Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(body).RootElement.GetProperty("activeVikar").ValueKind);
    }

    [Fact]
    public async Task VikarGet_NoActiveVikar_200_NullBranchSchemaMatchesRuntime()
    {
        var body = await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/admin/reporting-lines/s115r_m11/vikar"),
            "/api/admin/reporting-lines/{managerId}/vikar", "get");
        // Branch pin: the member is EMITTED as null (null-or-object stable envelope, never absent).
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(body).RootElement.GetProperty("activeVikar").ValueKind);
    }

    [Fact]
    public async Task VikarCreate_Post200_SchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, "/api/admin/reporting-lines/s115r_m12/vikar",
                """{ "vikarUserId": "s115r_v12", "effectiveTo": "2030-12-31", "reason": "FERIE" }"""),
            "/api/admin/reporting-lines/{managerId}/vikar", "post");

    [Fact]
    public async Task VikarRevoke_Delete200_GenuineBodySchemaMatchesRuntime()
        => await AssertOpAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete, "/api/admin/reporting-lines/s115r_m13/vikar"),
            "/api/admin/reporting-lines/{managerId}/vikar", "delete");

    // ════════════════════════════════════════════════════════════════════════════════
    //  The multi-2xx RED-on-lie proof (extends the S112 injected-status-lie pattern to
    //  the S115 SET-shaped contract) — its OWN dedicated virgin seed pair.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A REAL 201 first-assign response matched against a SYNTHETIC multi-status
    /// contract whose declared set ({200, 202}) omits the actual 201 MUST throw — an UNDECLARED
    /// runtime status stays RED even on a multi-2xx operation.</summary>
    [Fact]
    public async Task Gate_IsRed_OnUndeclaredRuntimeStatus_AgainstMulti2xxContract()
    {
        var request = WithIfNoneMatchStar(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/reporting-lines",
            """{ "employeeId": "s115r_e99", "managerId": "s115r_m99", "effectiveFrom": "2026-01-01" }"""));
        var (status, body) = await SendAsync(request);
        Assert.Equal(201, status);

        // The truth (the committed {201, 200} shared-$ref contract) passes on the real 201…
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/reporting-lines", "post");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, status, body, "truth");

        // …the lie (a multi-2xx set that does NOT declare 201) is RED on the same real response.
        var lie = new SpecRuntimeMatcher.SuccessContract(new[] { 200, 202 }, truth.Schema);
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertSuccessMatches(_spec, lie, status, body, "injected-multi-2xx-status-lie"));
        Assert.Contains("status", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNDECLARED", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Support ──

    private async Task<string> AssertOpAsync(HttpRequestMessage request, string specPath, string method)
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s115r_gadmin", Mao);
        return await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(_spec, client, request, specPath, method);
    }

    private async Task<(int Status, string Body)> SendAsync(HttpRequestMessage request)
    {
        using var client = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s115r_gadmin", Mao);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, body);
    }

    private static HttpRequestMessage WithIfNoneMatchStar(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return request;
    }

    // ── Fixture seed — one MAO + one Organisation PER SCENARIO + dedicated users/lines/vikar rows. ──
    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        // The MAO + the per-scenario Organisations (each = its own reporting tree).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                ('S115RMAO', 'S115 RL Ministerie',        'MAO',          NULL,       '/S115RMAO/',           'AC', 'OK24'),
                ('S115RL01', 'S115 Første-tildeling',     'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL01/',  'HK', 'OK24'),
                ('S115RL02', 'S115 Gen-tildeling',        'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL02/',  'HK', 'OK24'),
                ('S115RL03', 'S115 Slet-primær',          'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL03/',  'HK', 'OK24'),
                ('S115RL04', 'S115 Læse-styrelse',        'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL04/',  'HK', 'OK24'),
                ('S115RL05', 'S115 Acting-første',        'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL05/',  'HK', 'OK24'),
                ('S115RL06', 'S115 Acting-gen',           'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL06/',  'HK', 'OK24'),
                ('S115RL07', 'S115 Acting-slet',          'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL07/',  'HK', 'OK24'),
                ('S115RL08', 'S115 Import',               'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL08/',  'HK', 'OK24'),
                ('S115RL09', 'S115 Fjern-person',         'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL09/',  'HK', 'OK24'),
                ('S115RL10', 'S115 Vikar-med',            'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL10/',  'HK', 'OK24'),
                ('S115RL11', 'S115 Vikar-uden',           'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL11/',  'HK', 'OK24'),
                ('S115RL12', 'S115 Vikar-opret',          'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL12/',  'HK', 'OK24'),
                ('S115RL13', 'S115 Vikar-tilbagekald',    'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL13/',  'HK', 'OK24'),
                ('S115RL99', 'S115 Rød-bevis',            'ORGANISATION', 'S115RMAO', '/S115RMAO/S115RL99/',  'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Dedicated users (users.version=1 schema default → If-Match "1" deterministic).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active) VALUES
                ('s115r_e01',  's115r_e01',  '$2a$11$fake', 'S115 Første Medarbejder', 's115r_e01@test.dk',  'S115RL01', 'HK', 'OK24', TRUE),
                ('s115r_m01',  's115r_m01',  '$2a$11$fake', 'S115 Første Leder',       's115r_m01@test.dk',  'S115RL01', 'HK', 'OK24', TRUE),
                ('s115r_e02',  's115r_e02',  '$2a$11$fake', 'S115 Gen Medarbejder',    's115r_e02@test.dk',  'S115RL02', 'HK', 'OK24', TRUE),
                ('s115r_m02a', 's115r_m02a', '$2a$11$fake', 'S115 Gammel Leder',       's115r_m02a@test.dk', 'S115RL02', 'HK', 'OK24', TRUE),
                ('s115r_m02b', 's115r_m02b', '$2a$11$fake', 'S115 Ny Leder',           's115r_m02b@test.dk', 'S115RL02', 'HK', 'OK24', TRUE),
                ('s115r_e03',  's115r_e03',  '$2a$11$fake', 'S115 Slet Medarbejder',   's115r_e03@test.dk',  'S115RL03', 'HK', 'OK24', TRUE),
                ('s115r_m03',  's115r_m03',  '$2a$11$fake', 'S115 Slet Leder',         's115r_m03@test.dk',  'S115RL03', 'HK', 'OK24', TRUE),
                ('s115r_e04',  's115r_e04',  '$2a$11$fake', 'S115 Læse Medarbejder',   's115r_e04@test.dk',  'S115RL04', 'HK', 'OK24', TRUE),
                ('s115r_m04',  's115r_m04',  '$2a$11$fake', 'S115 Læse Leder',         's115r_m04@test.dk',  'S115RL04', 'HK', 'OK24', TRUE),
                ('s115r_e05',  's115r_e05',  '$2a$11$fake', 'S115 Acting Medarbejder', 's115r_e05@test.dk',  'S115RL05', 'HK', 'OK24', TRUE),
                ('s115r_m05',  's115r_m05',  '$2a$11$fake', 'S115 Acting Leder',       's115r_m05@test.dk',  'S115RL05', 'HK', 'OK24', TRUE),
                ('s115r_e06',  's115r_e06',  '$2a$11$fake', 'S115 ActGen Medarbejder', 's115r_e06@test.dk',  'S115RL06', 'HK', 'OK24', TRUE),
                ('s115r_m06a', 's115r_m06a', '$2a$11$fake', 'S115 ActGen Gammel',      's115r_m06a@test.dk', 'S115RL06', 'HK', 'OK24', TRUE),
                ('s115r_m06b', 's115r_m06b', '$2a$11$fake', 'S115 ActGen Ny',          's115r_m06b@test.dk', 'S115RL06', 'HK', 'OK24', TRUE),
                ('s115r_e07',  's115r_e07',  '$2a$11$fake', 'S115 ActSlet Medarb',     's115r_e07@test.dk',  'S115RL07', 'HK', 'OK24', TRUE),
                ('s115r_m07',  's115r_m07',  '$2a$11$fake', 'S115 ActSlet Leder',      's115r_m07@test.dk',  'S115RL07', 'HK', 'OK24', TRUE),
                ('s115r_e08',  's115r_e08',  '$2a$11$fake', 'S115 Import Medarbejder', 's115r_e08@test.dk',  'S115RL08', 'HK', 'OK24', TRUE),
                ('s115r_m08',  's115r_m08',  '$2a$11$fake', 'S115 Import Leder',       's115r_m08@test.dk',  'S115RL08', 'HK', 'OK24', TRUE),
                ('s115r_p09',  's115r_p09',  '$2a$11$fake', 'S115 Fjernet Person',     's115r_p09@test.dk',  'S115RL09', 'HK', 'OK24', TRUE),
                ('s115r_m10',  's115r_m10',  '$2a$11$fake', 'S115 Fraværende Leder',   's115r_m10@test.dk',  'S115RL10', 'HK', 'OK24', TRUE),
                ('s115r_v10',  's115r_v10',  '$2a$11$fake', 'S115 Vikar Stand-in',     's115r_v10@test.dk',  'S115RL10', 'HK', 'OK24', TRUE),
                ('s115r_m11',  's115r_m11',  '$2a$11$fake', 'S115 Vikarløs Leder',     's115r_m11@test.dk',  'S115RL11', 'HK', 'OK24', TRUE),
                ('s115r_m12',  's115r_m12',  '$2a$11$fake', 'S115 VikarOpret Leder',   's115r_m12@test.dk',  'S115RL12', 'HK', 'OK24', TRUE),
                ('s115r_v12',  's115r_v12',  '$2a$11$fake', 'S115 VikarOpret Standin', 's115r_v12@test.dk',  'S115RL12', 'HK', 'OK24', TRUE),
                ('s115r_m13',  's115r_m13',  '$2a$11$fake', 'S115 VikarSlet Leder',    's115r_m13@test.dk',  'S115RL13', 'HK', 'OK24', TRUE),
                ('s115r_v13',  's115r_v13',  '$2a$11$fake', 'S115 VikarSlet Standin',  's115r_v13@test.dk',  'S115RL13', 'HK', 'OK24', TRUE),
                ('s115r_e99',  's115r_e99',  '$2a$11$fake', 'S115 Rød Medarbejder',    's115r_e99@test.dk',  'S115RL99', 'HK', 'OK24', TRUE),
                ('s115r_m99',  's115r_m99',  '$2a$11$fake', 'S115 Rød Leder',          's115r_m99@test.dk',  'S115RL99', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Pre-seeded reporting lines (version=1 default): the PREDECESSOR states for the two
        // 200-branches, the two DELETE targets, and the read-only line.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines (employee_id, manager_id, organisation_id, relationship, effective_from, source, created_by) VALUES
                ('s115r_e02', 's115r_m02a', 'S115RL02', 'PRIMARY', '2024-01-01', 'MANUAL', 'seed'),
                ('s115r_e03', 's115r_m03',  'S115RL03', 'PRIMARY', '2024-01-01', 'MANUAL', 'seed'),
                ('s115r_e04', 's115r_m04',  'S115RL04', 'PRIMARY', '2024-01-01', 'MANUAL', 'seed'),
                ('s115r_e06', 's115r_m06a', 'S115RL06', 'ACTING',  '2024-01-01', 'MANUAL', 'seed'),
                ('s115r_e07', 's115r_m07',  'S115RL07', 'ACTING',  '2024-01-01', 'MANUAL', 'seed')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Pre-seeded ACTIVE manager_vikar rows (effective_to NULL): the GET's object branch +
        // the DELETE-revoke's own dedicated row.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO manager_vikar (vikar_id, absent_approver_id, vikar_user_id, until_date, reason, organisation_id, created_by) VALUES
                (@withId,   's115r_m10', 's115r_v10', '2030-12-31', 'FERIE',  'S115RL10', 'seed'),
                (@revokeId, 's115r_m13', 's115r_v13', '2030-12-31', 'SYGDOM', 'S115RL13', 'seed')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("withId", VikarWithId);
            cmd.Parameters.AddWithValue("revokeId", VikarRevokeId);
            await cmd.ExecuteNonQueryAsync();
        }

        // The vikar-CREATE candidate must hold a qualifying role (LocalLeader+) — ORG_ONLY on the
        // scenario Organisation (the manager has no reports, so coverage is trivially satisfied).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
                ('s115r_v12', 'LOCAL_LEADER', 'S115RL12', 'ORG_ONLY', 'seed')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
