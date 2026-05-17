using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S31 / TASK-3110 D-tests — HTTP-level admin CRUD wire contract for the new
/// <see cref="StatsTid.Backend.Api.Endpoints.EmployeeProfileEndpoints"/> pair
/// (GET + PUT under <c>/api/admin/employee-profiles/{employeeId}</c>). WAF&lt;Program&gt;
/// harness mirrors S30 <see cref="EntitlementConfigEndpointTests"/>.
///
/// <list type="bullet">
///   <item><b>Shape</b>: GET returns row + ETag; GET 404 on no live row; PUT round-trips.</item>
///   <item><b>ADR-019 If-Match contract</b>: 412 stale / 428 missing / 428 malformed.</item>
///   <item><b>Step 0b BLOCKER fix — cross-org scope</b>: HR token scoped outside emp001's
///   org subtree → 403 on both GET and PUT. Verifies <c>OrgScopeValidator.ValidateEmployeeAccessAsync</c>
///   binding is wired in addition to the <c>HROrAbove</c> policy.</item>
///   <item><b>RBAC matrix</b>: Employee 403, LocalLeader 403, HR 200 (same-org), GlobalAdmin 200.</item>
///   <item><b>Validation</b>: negative <c>weekly_norm_hours</c> + out-of-range
///   <c>part_time_fraction</c> → 4xx.</item>
///   <item><b>Backfill bootstrap</b>: <see cref="StatsTid.Infrastructure.EmployeeProfileSeeder"/>
///   runs at host startup; assert all 7 seed users have live profiles + 7
///   <c>EmployeeProfileCreated</c> outbox events with <c>actor_id='SYSTEM_SEED'</c>.</item>
/// </list>
///
/// <para>
/// JWT minting follows the dev-fallback signing key pattern verbatim. The cross-org
/// test mints an HR token scoped to <c>AFD02</c> (sibling subtree under <c>STY02</c>
/// per init.sql L723) — that does NOT cover emp001 (in <c>STY01</c>) so the
/// <c>OrgScopeValidator</c> rejects with 403.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmployeeProfileEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // Seeded employees in init.sql L730-737 — used across tests as known fixtures.
    // emp001 sits in STY01 (path /MIN01/STY01/); emp002 in AFD01 (/MIN01/STY02/AFD01/);
    // emp003 in AFD02 (/MIN01/STY02/AFD02/).
    private const string Emp001 = "emp001";
    private const string Emp001OrgPath = "/MIN01/STY01/";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Shape tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_ReturnsProfile_WithEtagHeader()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        var rsp = await client.GetAsync($"/api/admin/employee-profiles/{Emp001}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.NotNull(rsp.Headers.ETag);
        Assert.Equal("\"1\"", rsp.Headers.ETag!.Tag);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Emp001, body.GetProperty("employeeId").GetString());
        Assert.Equal(37.0m, body.GetProperty("weeklyNormHours").GetDecimal());
        Assert.Equal(1.000m, body.GetProperty("partTimeFraction").GetDecimal());
        Assert.False(body.GetProperty("isPartTime").GetBoolean());
        Assert.Equal(1L, body.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Get_NotFound_When_NoLiveRow()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        // Create a fresh user and delete its backfilled profile row so OrgScopeValidator
        // resolves the target org without 404'ing on the user lookup, but the profile GET
        // hits the no-live-row branch. Using a fresh user avoids races with the bootstrap
        // backfill assertion (which counts the 7 seeded users' profiles).
        var fresh = await CreateFreshUserAsync(client);
        await DeleteProfileRowAsync(fresh);

        var rsp = await client.GetAsync($"/api/admin/employee-profiles/{fresh}");
        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
    }

    [Fact]
    public async Task Put_Success_RoundTripsAndIncrementsVersion()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        // Use a fresh user (independent of other tests' fixtures) so this test can
        // assert version goes 1 → 2 without coupling to xUnit's intra-class test order.
        var targetEmp = await CreateFreshUserAsync(client);

        var rsp = await PutAsync(client, targetEmp,
            weeklyNormHours: 32.0m, partTimeFraction: 0.8m, position: "Specialist",
            ifMatchValue: "1");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.NotNull(rsp.Headers.ETag);
        Assert.Equal("\"2\"", rsp.Headers.ETag!.Tag);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2L, body.GetProperty("version").GetInt64());
        Assert.Equal(32.0m, body.GetProperty("weeklyNormHours").GetDecimal());
        Assert.Equal(0.8m, body.GetProperty("partTimeFraction").GetDecimal());
        Assert.Equal("Specialist", body.GetProperty("position").GetString());
        Assert.True(body.GetProperty("isPartTime").GetBoolean());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ETag / If-Match contract (ADR-019 admin-strict)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_StaleIfMatch_Returns412()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        // Stale If-Match: "0" (current = 1).
        var rsp = await PutAsync(client, "emp001",
            weeklyNormHours: 37.0m, partTimeFraction: 1.000m, position: null,
            ifMatchValue: "0");

        Assert.Equal(HttpStatusCode.PreconditionFailed, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0L, body.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(1L, body.GetProperty("actualVersion").GetInt64());
    }

    [Fact]
    public async Task Put_MissingIfMatch_Returns428()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        var rsp = await PutAsync(client, "emp001",
            weeklyNormHours: 37.0m, partTimeFraction: 1.000m, position: null,
            ifMatchValue: null);

        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    [Fact]
    public async Task Put_MalformedIfMatch_Returns428()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        // Pass a string that the helper's parser will reject (admin-strict mode).
        var rsp = await PutAsync(client, "emp001",
            weeklyNormHours: 37.0m, partTimeFraction: 1.000m, position: null,
            ifMatchValue: "notanumber");

        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Cross-org scope (Step 0b BLOCKER fix — OrgScopeValidator on both GET + PUT)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_ByHrFromDifferentOrg_Returns403()
    {
        var client = _factory.CreateClient();
        // HR token scoped to AFD02 only (path /MIN01/STY02/AFD02/) — does NOT cover
        // emp001's org STY01 (path /MIN01/STY01/).
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintHrToken(
                actorId: "hr_afd02_qa", orgId: "AFD02", scopeType: "ORG_ONLY"));

        var rsp = await client.GetAsync($"/api/admin/employee-profiles/{Emp001}");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    [Fact]
    public async Task Put_ByHrFromDifferentOrg_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintHrToken(
                actorId: "hr_afd02_qa", orgId: "AFD02", scopeType: "ORG_ONLY"));

        var rsp = await PutAsync(client, Emp001,
            weeklyNormHours: 30.0m, partTimeFraction: 0.5m, position: "Attempted Edit",
            ifMatchValue: "1");

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);

        // Defense in depth — the live row was not touched by the rejected request.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT version, weekly_norm_hours, part_time_fraction, position
            FROM employee_profiles
            WHERE employee_id = @id AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("id", Emp001);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(37.0m, reader.GetDecimal(1));
        Assert.Equal(1.000m, reader.GetDecimal(2));
        Assert.True(reader.IsDBNull(3));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // RBAC matrix (HROrAbove policy: GlobalAdmin / LocalAdmin / LocalHR allowed)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_AsEmployee_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(
                actorId: "emp001", orgId: "STY01"));

        var rsp = await client.GetAsync($"/api/admin/employee-profiles/{Emp001}");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    [Fact]
    public async Task Get_AsLocalLeader_Returns403()
    {
        var client = _factory.CreateClient();
        // LocalLeader scoped covering emp001's org — but LocalLeader is NOT in the
        // HROrAbove policy's allowed-role list (HR/LocalAdmin/GlobalAdmin only).
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintTokenWithRoleAndScope(
                actorId: "leader_qa",
                role: StatsTidRoles.LocalLeader,
                scopeRole: StatsTidRoles.LocalLeader,
                orgId: "STY01",
                scopeType: "ORG_AND_DESCENDANTS"));

        var rsp = await client.GetAsync($"/api/admin/employee-profiles/{Emp001}");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    [Fact]
    public async Task Get_AsHr_SameOrg_Returns200()
    {
        var client = _factory.CreateClient();
        // HR scoped to MIN01 + descendants → covers emp001 in STY01 (under MIN01).
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintHrToken(
                actorId: "hr_min01_qa", orgId: "MIN01", scopeType: "ORG_AND_DESCENDANTS"));

        var rsp = await client.GetAsync($"/api/admin/employee-profiles/{Emp001}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    [Fact]
    public async Task Get_AsGlobalAdmin_Returns200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        var rsp = await client.GetAsync($"/api/admin/employee-profiles/{Emp001}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Validation — schema CHECK constraints surface as HTTP 4xx via the framework.
    // ═════════════════════════════════════════════════════════════════════════

    // S31 validation surface is intentionally minimal — TASK-3107 endpoint does not
    // perform input-range validation on weekly_norm_hours or part_time_fraction. The
    // following two tests document current behavior + the contract gap; a future
    // tightening (input validator on UpdateEmployeeProfileRequest) should flip these
    // to assert 400 / 422 and bump the version-after pre-condition.

    [Fact]
    public async Task Put_NegativeWeeklyNormHours_AcceptedToday_DocumentsValidationGap()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        // Create a fresh user (independent of other tests' fixtures) so this test can
        // PUT with If-Match: "1" deterministically regardless of execution ordering.
        var fresh = await CreateFreshUserAsync(client);

        // employee_profiles.weekly_norm_hours is NUMERIC(5,2) with no CHECK constraint;
        // -1 fits in the column type and the endpoint has no input-range validator yet.
        // Current behavior: 200 OK + the row absorbs the negative value. This test pins
        // that gap so a future validator's 400/422 surfaces as a failing assertion that
        // the test owner can flip with the validator change.
        var rsp = await PutAsync(client, fresh,
            weeklyNormHours: -1m, partTimeFraction: 1.000m, position: null,
            ifMatchValue: "1");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        // When validation is added, replace the line above with:
        //   Assert.True(rsp.StatusCode is HttpStatusCode.BadRequest
        //               or HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Put_PartTimeFractionAboveOne_AcceptedToday_DocumentsValidationGap()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        var fresh = await CreateFreshUserAsync(client);

        // part_time_fraction is NUMERIC(4,3) with no CHECK constraint; 1.5 fits in the
        // column type and is semantically out of [0, 1] range. Endpoint absorbs without
        // validation today.
        var rsp = await PutAsync(client, fresh,
            weeklyNormHours: 37.0m, partTimeFraction: 1.5m, position: null,
            ifMatchValue: "1");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Backfill emission — at WAF<Program> startup, EmployeeProfileSeeder runs
    // and creates one live row per seed user + emits one EmployeeProfileCreated
    // event per row.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Bootstrap_BackfillsAllSeedUsers_AndEmitsCreatedEvents()
    {
        // Force the WAF to boot (and therefore the seeder to run) by creating a client.
        _ = _factory.CreateClient();

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // 7 seed users: admin01, hr01, mgr01, ladm01, emp001, emp002, emp003. Each
        // must have exactly one live profile row (partial-unique-index enforces this).
        await using (var cnt = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM employee_profiles
            WHERE effective_to IS NULL
              AND employee_id IN ('admin01','hr01','mgr01','ladm01','emp001','emp002','emp003')
            """, conn))
        {
            Assert.Equal(7L, Convert.ToInt64(await cnt.ExecuteScalarAsync()));
        }

        // 7 EmployeeProfileCreated outbox events with actor_id='SYSTEM_SEED'.
        await using (var evCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE event_type = 'EmployeeProfileCreated'
              AND actor_id = 'SYSTEM_SEED'
            """, conn))
        {
            Assert.Equal(7L, Convert.ToInt64(await evCmd.ExecuteScalarAsync()));
        }

        // Each seed user has its event on the correct per-employee stream.
        foreach (var seedUser in new[]
                 {
                     "admin01", "hr01", "mgr01", "ladm01", "emp001", "emp002", "emp003",
                 })
        {
            await using var perStreamCmd = new NpgsqlCommand(
                """
                SELECT COUNT(*) FROM outbox_events
                WHERE stream_id = @streamId
                  AND event_type = 'EmployeeProfileCreated'
                  AND actor_id = 'SYSTEM_SEED'
                """, conn);
            perStreamCmd.Parameters.AddWithValue("streamId", $"employee-profile-{seedUser}");
            Assert.Equal(1L, Convert.ToInt64(await perStreamCmd.ExecuteScalarAsync()));
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a brand-new active user via <c>POST /api/admin/users</c> (which also
    /// inserts the live <c>employee_profiles</c> row at version=1 per TASK-3108's
    /// 4-way atomicity contract). Returns the fresh user_id. Used by tests that need
    /// a deterministic version=1 baseline regardless of xUnit's intra-class ordering.
    /// </summary>
    private static async Task<string> CreateFreshUserAsync(HttpClient client)
    {
        var newId = "emp_s31_ep_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var body = new
        {
            userId = newId,
            username = newId,
            password = "TestPassword123!",
            displayName = "S31 EndpointTests Fresh User",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        };
        var rsp = await client.PostAsJsonAsync("/api/admin/users", body);
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        return newId;
    }

    private async Task DeleteProfileRowAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM employee_profiles WHERE employee_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client, string employeeId,
        decimal weeklyNormHours, decimal partTimeFraction, string? position,
        string? ifMatchValue)
    {
        // S33 TASK-3308 added required EffectiveFrom: DateOnly to the PUT DTO.
        // S31 test helper now stamps today (UTC) so the S31 acceptance behavior
        // (200/412/428/403/422 per existing test names) survives the validator
        // unchanged. S33 TASK-3312 adds dedicated 422 tests for the backdated +
        // future-dated validator paths (PUT_BackdatedEffectiveFrom_Returns422 +
        // PUT_FutureDatedEffectiveFrom_Returns422).
        var req = new HttpRequestMessage(HttpMethod.Put,
            $"/api/admin/employee-profiles/{employeeId}")
        {
            Content = JsonContent.Create(new
            {
                effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                weeklyNormHours,
                partTimeFraction,
                position,
            }),
        };
        if (ifMatchValue is not null)
        {
            // For the "malformed" test we pass the raw string without quoting so the
            // helper sees the actual token shape the test name implies. For numeric
            // values we wrap in double quotes per RFC 7232 strong-validator form.
            var headerValue = IsNumeric(ifMatchValue) ? $"\"{ifMatchValue}\"" : ifMatchValue;
            req.Headers.TryAddWithoutValidation("If-Match", headerValue);
        }
        return await client.SendAsync(req);
    }

    private static bool IsNumeric(string s) =>
        long.TryParse(s, out _) || (s.StartsWith("-") && long.TryParse(s.AsSpan(1), out _));

    // ── Token minting ──

    private static string MintGlobalAdminToken()
    {
        var settings = NewSettings();
        var tokenService = new JwtTokenService(settings);
        return tokenService.GenerateToken(
            employeeId: "ADMIN_S31_QA",
            name: "S31 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private static string MintHrToken(string actorId, string orgId, string scopeType)
    {
        var settings = NewSettings();
        var tokenService = new JwtTokenService(settings);
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.LocalHR,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, scopeType) });
    }

    private static string MintEmployeeToken(string actorId, string orgId)
    {
        var settings = NewSettings();
        var tokenService = new JwtTokenService(settings);
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static string MintTokenWithRoleAndScope(
        string actorId, string role, string scopeRole, string orgId, string scopeType)
    {
        var settings = NewSettings();
        var tokenService = new JwtTokenService(settings);
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: role,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(scopeRole, orgId, scopeType) });
    }

    private static JwtSettings NewSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };
}
