using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S148 / TASK-14853-T (SEC-070) — a project's edit (PUT) and deactivation (DELETE) are bound to the
/// organisation in the address. Another organisation's project answers 404 "Project not found" and is left
/// untouched; the answer for "no such project" is identical, so a caller cannot tell the two apart; and one's
/// own project still works, including a repeated deactivation (204 again).
///
/// <para><b>Seed discipline (FAIL-002):</b> a fresh testcontainer per test. Two organisations
/// (<c>S148PRA</c>, <c>S148PRB</c>), one project each, inserted by SQL; the actors are JWT-only. Docker-gated:
/// CI-pending until a checkpoint run shows it green.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProjectOrgBindingTests : IAsyncLifetime
{
    private const string OrgA = "S148PRA";
    private const string OrgB = "S148PRB";
    private const string NotFoundBody = "Project not found";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private Guid _projectA;
    private Guid _projectB;
    private Guid _inactiveProjectA;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        foreach (var org in new[] { OrgA, OrgB })
            await Exec(conn,
                """
                INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version)
                VALUES (@orgId, @name, 'ORGANISATION', NULL, @path, 'AC', 'OK24')
                ON CONFLICT (org_id) DO NOTHING
                """,
                ("orgId", org), ("name", $"{org} Test Org"), ("path", $"/{org}/"));
        _projectA = await InsertProjectAsync(conn, OrgA, "S148_A", "Projekt A", 3);
        _projectB = await InsertProjectAsync(conn, OrgB, "S148_B", "Projekt B", 5);
        _inactiveProjectA = await InsertProjectAsync(conn, OrgA, "S148_A_OFF", "Lukket A", 4);
        await Exec(conn, "UPDATE projects SET is_active = FALSE WHERE project_id = @id", ("id", _inactiveProjectA));
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// What it proves: an administrator of organisation A who puts A in the address and organisation B's
    /// project id in the path is told "Project not found" and B's project keeps its name, order and active flag.
    /// <b>Kind F.</b> Red conditions: the 404 assertion. On <c>45b86bcc</c> the PUT ran
    /// <c>UPDATE projects SET project_name = @projectName, sort_order = @sortOrder WHERE project_id = @projectId</c>
    /// with no <c>org_id</c> condition and answered 200, and B's row changed.
    /// </summary>
    [Fact]
    public async Task Put_AnotherOrganisationsProject_404_AndNothingChanges()
    {
        using var admin = LocalAdminOfA();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{OrgA}/{_projectB}",
            """{ "projectName": "Hijacked", "sortOrder": 99 }"""));

        await AssertNotFoundAsync(response);
        await AssertProjectBUnchangedAsync();
    }

    /// <summary>
    /// What it proves: the same administrator cannot deactivate B's project; it answers 404 and stays active.
    /// <b>Kind F.</b> Red conditions: the 404 assertion. On <c>45b86bcc</c> the DELETE ran
    /// <c>UPDATE projects SET is_active = FALSE WHERE project_id = @projectId</c> and answered 204, and B went inactive.
    /// </summary>
    [Fact]
    public async Task Delete_AnotherOrganisationsProject_404_AndItStaysActive()
    {
        using var admin = LocalAdminOfA();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Delete, $"/api/projects/{OrgA}/{_projectB}"));

        await AssertNotFoundAsync(response);
        await AssertProjectBUnchangedAsync();
    }

    /// <summary>
    /// What it proves: the binding is to the address, whatever the role. A GlobalAdmin who puts A in the address
    /// with B's project id gets 404 on both writes and B's row is unchanged.
    /// <b>Kind F.</b> Red conditions: both 404 assertions. On <c>45b86bcc</c> the PUT answered 200 and the DELETE 204
    /// (the statements had no <c>org_id</c> condition), changing B's row.
    /// </summary>
    [Fact]
    public async Task GlobalAdmin_AddressOrganisationMismatch_404()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, "s148_gadmin", OrgA);

        using var put = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{OrgA}/{_projectB}",
            """{ "projectName": "Hijacked", "sortOrder": 99 }"""));
        await AssertNotFoundAsync(put);

        using var del = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Delete, $"/api/projects/{OrgA}/{_projectB}"));
        await AssertNotFoundAsync(del);

        await AssertProjectBUnchangedAsync();
    }

    /// <summary>
    /// What it proves: an id that exists nowhere, in the administrator's own address, gets the same 404 and the
    /// same body as another organisation's project, so a probe cannot tell the two cases apart.
    /// <b>Kind F.</b> Red conditions: the 404 assertions. On <c>45b86bcc</c> an UPDATE that matched no row still
    /// returned success: the PUT answered 200 and the DELETE 204.
    /// </summary>
    [Fact]
    public async Task UnknownProjectInOwnOrganisation_404_SameBody()
    {
        using var admin = LocalAdminOfA();
        var unknown = Guid.NewGuid();
        const string putBody = """{ "projectName": "Ghost", "sortOrder": 1 }""";

        using var putAbsent = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{OrgA}/{unknown}", putBody));
        var putAbsentJson = await AssertNotFoundAsync(putAbsent);
        using var putElsewhere = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{OrgA}/{_projectB}", putBody));
        var putElsewhereJson = await AssertNotFoundAsync(putElsewhere);
        Assert.Equal(putAbsentJson, putElsewhereJson);

        using var delAbsent = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Delete, $"/api/projects/{OrgA}/{unknown}"));
        var delAbsentJson = await AssertNotFoundAsync(delAbsent);
        using var delElsewhere = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Delete, $"/api/projects/{OrgA}/{_projectB}"));
        var delElsewhereJson = await AssertNotFoundAsync(delElsewhere);
        Assert.Equal(delAbsentJson, delElsewhereJson);
    }

    /// <summary>
    /// What it proves: the scope check still comes first for a plain LocalAdmin. An administrator of A who puts
    /// B in the address, with B's project, is refused 403 and B's row is unchanged.
    /// <b>Kind R</b> (guard). Red conditions: none on <c>45b86bcc</c>, where it is green too (the scope check was
    /// already first and unchanged); it goes red if the binding replaced or reordered the scope check.
    /// </summary>
    [Fact]
    public async Task LocalAdminOfA_AddressingB_403_AndNothingChanges()
    {
        using var admin = LocalAdminOfA();

        using var put = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{OrgB}/{_projectB}",
            """{ "projectName": "Hijacked", "sortOrder": 99 }"""));
        Assert.Equal(403, (int)put.StatusCode);

        using var del = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Delete, $"/api/projects/{OrgB}/{_projectB}"));
        Assert.Equal(403, (int)del.StatusCode);

        await AssertProjectBUnchangedAsync();
    }

    /// <summary>
    /// What it proves: the binding holds for an administrator with scopes over both A and B. With A in the
    /// address and B's project the answer is 404 and B is unchanged; with B in the address and B's project the
    /// same administrator's edit works (200).
    /// <b>Kind F</b> for the first half. Red conditions: the 404 assertion. On <c>45b86bcc</c>
    /// <c>UpdateAsync(Guid projectId, ...)</c> ran <c>UPDATE projects SET project_name = @projectName, sort_order =
    /// @sortOrder WHERE project_id = @projectId</c>, and the scope check passed for A, so it answered 200 and B's row
    /// changed. The second half is a guard (green on both).
    /// </summary>
    [Fact]
    public async Task AdminOverAAndB_AddressBinding_404ForMismatch_200ForMatch()
    {
        using var admin = LocalAdminOver(OrgA, OrgB);

        using var mismatch = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{OrgA}/{_projectB}",
            """{ "projectName": "Hijacked", "sortOrder": 99 }"""));
        await AssertNotFoundAsync(mismatch);
        await AssertProjectBUnchangedAsync();

        using var match = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{OrgB}/{_projectB}",
            """{ "projectName": "Omdøbt B", "sortOrder": 6 }"""));
        Assert.Equal(200, (int)match.StatusCode);
        var (name, sort, active) = await ReadProjectAsync(_projectB);
        Assert.Equal("Omdøbt B", name);
        Assert.Equal(6, sort);
        Assert.True(active);
    }

    /// <summary>
    /// What it proves: editing one's own inactive project still works. The PUT answers 200, the name and sort order
    /// are updated, and the project stays inactive.
    /// <b>Kind R</b> (guard against over-blocking). Red conditions: none on <c>45b86bcc</c> (its UPDATE had no
    /// <c>is_active</c> condition); it goes red if the fix adds an active-only condition.
    /// </summary>
    [Fact]
    public async Task OwnInactiveProject_Put_200_UpdatesAndStaysInactive()
    {
        using var admin = LocalAdminOfA();

        using var put = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{OrgA}/{_inactiveProjectA}",
            """{ "projectName": "Genåbnet navn", "sortOrder": 11 }"""));
        Assert.Equal(200, (int)put.StatusCode);

        var (name, sort, active) = await ReadProjectAsync(_inactiveProjectA);
        Assert.Equal("Genåbnet navn", name);
        Assert.Equal(11, sort);
        Assert.False(active);
    }

    /// <summary>
    /// What it proves: the binding does not over-block. The administrator edits A's own project (200,
    /// <c>ProjectUpdateResponse</c>, row changed), deactivates it (204, inactive), and deactivates it again (204).
    /// <b>Kind R</b> (regression guard). Red conditions: none on the base, where this is green too; it goes red if
    /// the fix over-blocks the own organisation or makes a repeated deactivation a 404.
    /// </summary>
    [Fact]
    public async Task OwnOrganisation_PutAndDelete_StillWork()
    {
        using var admin = LocalAdminOfA();

        using var put = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{OrgA}/{_projectA}",
            """{ "projectName": "Omdøbt A", "sortOrder": 8 }"""));
        Assert.Equal(200, (int)put.StatusCode);
        var body = JsonDocument.Parse(await put.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(_projectA, body.GetProperty("projectId").GetGuid());
        Assert.True(body.GetProperty("updated").GetBoolean());
        var (name, sort, active) = await ReadProjectAsync(_projectA);
        Assert.Equal("Omdøbt A", name);
        Assert.Equal(8, sort);
        Assert.True(active);

        using var del = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Delete, $"/api/projects/{OrgA}/{_projectA}"));
        Assert.Equal(204, (int)del.StatusCode);
        Assert.False((await ReadProjectAsync(_projectA)).Active);

        using var again = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Delete, $"/api/projects/{OrgA}/{_projectA}"));
        Assert.Equal(204, (int)again.StatusCode);
        Assert.False((await ReadProjectAsync(_projectA)).Active);
    }

    // ── helpers ──

    private HttpClient LocalAdminOfA()
        => SpecRuntimeTestSupport.CreateLocalAdminClient(_factory, "s148_ladm_a", OrgA);

    /// <summary>A client whose JWT carries a LocalAdmin ORG_ONLY scope over each of the given organisations
    /// (scopes are matched exactly per organisation). Same token shape as
    /// <c>SpecRuntimeTestSupport.CreateLocalAdminClient</c>, with several scopes.</summary>
    private HttpClient LocalAdminOver(params string[] orgIds)
    {
        var client = _factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!",
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: "s148_ladm_multi", name: "s148_ladm_multi", role: StatsTidRoles.LocalAdmin,
            agreementCode: "AC", orgId: orgIds[0],
            scopes: orgIds.Select(o => new RoleScope(StatsTidRoles.LocalAdmin, o, "ORG_ONLY")).ToArray());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Asserts a 404 whose body is a JSON object with exactly one member, <c>error</c>, equal to
    /// "Project not found"; returns the body's raw JSON text for whole-body comparison.</summary>
    private static async Task<string> AssertNotFoundAsync(HttpResponseMessage response)
    {
        Assert.Equal(404, (int)response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        var members = body.EnumerateObject().ToList();
        Assert.Single(members);
        Assert.Equal("error", members[0].Name);
        Assert.Equal(NotFoundBody, members[0].Value.GetString());
        return body.GetRawText();
    }

    private async Task AssertProjectBUnchangedAsync()
    {
        var (name, sort, active) = await ReadProjectAsync(_projectB);
        Assert.Equal("Projekt B", name);
        Assert.Equal(5, sort);
        Assert.True(active);
    }

    private async Task<(string Name, int Sort, bool Active)> ReadProjectAsync(Guid projectId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // NpgsqlBatch (not NpgsqlCommand) so CA2100's warning budget does not grow (QUAL-073): a literal, parameters only.
        await using var batch = new NpgsqlBatch(conn);
        var cmd = new NpgsqlBatchCommand(
            "SELECT project_name, sort_order, is_active FROM projects WHERE project_id = @id");
        cmd.Parameters.AddWithValue("id", projectId);
        batch.BatchCommands.Add(cmd);
        await using var reader = await batch.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "the project row exists");
        return (reader.GetString(0), reader.GetInt32(1), reader.GetBoolean(2));
    }

    private static async Task<Guid> InsertProjectAsync(
        NpgsqlConnection conn, string orgId, string code, string name, int sortOrder)
    {
        await using var batch = new NpgsqlBatch(conn);
        var cmd = new NpgsqlBatchCommand(
            """
            INSERT INTO projects (org_id, project_code, project_name, sort_order, created_by)
            VALUES (@orgId, @code, @name, @sort, 'TEST') RETURNING project_id
            """);
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("sort", sortOrder);
        batch.BatchCommands.Add(cmd);
        return (Guid)(await batch.ExecuteScalarAsync())!;
    }

    private static async Task Exec(NpgsqlConnection conn, string sql, params (string Name, object Value)[] ps)
    {
        await using var batch = new NpgsqlBatch(conn);
        var cmd = new NpgsqlBatchCommand(sql);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        batch.BatchCommands.Add(cmd);
        await batch.ExecuteNonQueryAsync();
    }
}
