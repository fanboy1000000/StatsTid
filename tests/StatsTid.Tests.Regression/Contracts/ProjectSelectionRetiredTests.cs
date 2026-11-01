using System.Net;
using System.Net.Http.Headers;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S147 / TASK-14710-T (AC-230-5, the project bullet; rulings R-23, R-33) — the two deprecated single-row
/// project-selection routes are GONE.
///
/// <para><b>Plain language.</b> Since S72 the Skema screen saves a person's chosen project rows through the
/// row-preferences save. The two older routes (<c>POST</c> and <c>DELETE /api/projects/{orgId}/select/{projectId}</c>)
/// had no screen using them, and because they act on whoever is calling — with no look at the person's record —
/// they could not carry the new rule that a leaver's own access ends the day after their last working day. So
/// they were deleted, and both now answer "not found". (The one remaining way to choose project rows is the
/// row-preferences save, which does carry the rule.)</para>
///
/// <para><b>Kind F.</b> Today both routes exist: the POST answers 200 with the selection echo and the DELETE
/// answers 204 for a seeded, in-scope employee, so each <c>Assert.Equal(NotFound)</c> fails. Red conditions are
/// named per fact. The caller is a REAL seeded Employee with a valid token and scope over the organisation, so a
/// 404 cannot be an authentication or scope artefact. Docker — CI-pending.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProjectSelectionRetiredTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string Org = "STY02";
    private const string EmployeeId = "s14710_proj_emp";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, EmployeeId, Org, "HK", "OK24", ensureOrg: false);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private HttpClient EmployeeClient()
    {
        var client = _factory.CreateClient();
        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid", Audience = "statstid", SigningKey = DevFallbackSigningKey, ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: EmployeeId, name: EmployeeId, role: StatsTidRoles.Employee, agreementCode: "HK", orgId: Org,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, Org, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Kind F. <c>POST /api/projects/{orgId}/select/{projectId}</c> answers 404 for an in-scope Employee. Red
    /// condition: <c>Assert.Equal(NotFound)</c> — today it answers 200 with <c>{projectId, selected: true}</c> for a real
    /// project (here a seeded STY02 catalogue project, so the route would otherwise succeed).</summary>
    [Fact]
    public async Task SelectPost_Answers404()
    {
        using var client = EmployeeClient();
        var projectId = await FirstSeededProjectIdAsync();

        using var rsp = await client.PostAsync($"/api/projects/{Org}/select/{projectId}", content: null);

        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
        Assert.Equal(0L, await SelectionCountAsync());
    }

    /// <summary>Kind F. <c>DELETE /api/projects/{orgId}/select/{projectId}</c> answers 404 for an in-scope Employee. Red
    /// condition: <c>Assert.Equal(NotFound)</c> — today it answers 204 for a real project.</summary>
    [Fact]
    public async Task SelectDelete_Answers404()
    {
        using var client = EmployeeClient();
        var projectId = await FirstSeededProjectIdAsync();

        using var rsp = await client.DeleteAsync($"/api/projects/{Org}/select/{projectId}");

        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
    }

    private async Task<Guid> FirstSeededProjectIdAsync()
    {
        await using var conn = new Npgsql.NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT project_id FROM projects WHERE org_id = @o AND is_active = TRUE ORDER BY sort_order LIMIT 1", conn);
        cmd.Parameters.AddWithValue("o", Org);
        var v = await cmd.ExecuteScalarAsync();
        Assert.NotNull(v); // the init.sql STY02 catalogue (5 rows) must be there, or a 404 would prove nothing
        return (Guid)v!;
    }

    private async Task<long> SelectionCountAsync()
    {
        await using var conn = new Npgsql.NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT COUNT(*) FROM user_project_selections WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", EmployeeId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
