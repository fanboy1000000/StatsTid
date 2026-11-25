using System.Net.Http;
using System.Text.Json;
using Npgsql;
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

        using var put = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{OrgA}/{unknown}",
            """{ "projectName": "Ghost", "sortOrder": 1 }"""));
        await AssertNotFoundAsync(put);

        using var del = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Delete, $"/api/projects/{OrgA}/{unknown}"));
        await AssertNotFoundAsync(del);
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

    private static async Task AssertNotFoundAsync(HttpResponseMessage response)
    {
        Assert.Equal(404, (int)response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(NotFoundBody, body.GetProperty("error").GetString());
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
