using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S72 / TASK-7201 — the <c>PUT /api/skema/{employeeId}/row-preferences</c> matrix
/// (SPRINT-72 R4): full-replacement semantics, dense 0..n-1 reindex in submitted order,
/// first-write container init (<c>initialized_at</c> preserved on later writes), write-side
/// catalog validation (422 listing offenders — unknown/foreign/inactive projects,
/// org-hidden and eligibility-filtered absence types, duplicates), SELF-ONLY authorization
/// (Step-5a B3, owner-adjudicated: row preferences are personal view state — only the
/// employee themself writes them; the elevated covering-scope path is removed for the PUT
/// only, incl. the S70-R9f1-shaped mixed-role bypass), and the R4 UN-EVENTED pin: a
/// preference write adds NO <c>outbox_events</c> row and NO <c>audit_projection</c> row
/// (view preference ≠ domain state — the ProjectRepository selection precedent).
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaRowPreferencesPutTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot once (seeders run here, before per-test users)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ───────────────────── happy path ─────────────────────

    /// <summary>
    /// First write: container initialized, rows stored DENSE 0..n-1 ordered by the submitted
    /// sortOrder (sparse/odd submitted values are densified), response = the new effective
    /// rowPreferences (same shape as the month GET's field).
    /// </summary>
    [Fact]
    public async Task FirstWrite_InitializesContainer_StoresDenseRows_ReturnsEffectivePreferences()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);
        var projB = await InsertProjectAsync(orgId, "S72B", 20);

        // Sparse, out-of-array-order sortOrders: B=5, A=2 ⇒ effective order A(0), B(1).
        var rsp = await PutPreferencesAsync(emp, orgId, new
        {
            projects = new object[]
            {
                new { projectId = projB, sortOrder = 5 },
                new { projectId = projA, sortOrder = 2 },
            },
            absenceTypes = new object[]
            {
                new { absenceType = "VACATION", sortOrder = 9 },
                new { absenceType = "SICK_DAY", sortOrder = 0 },
            },
        });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("configured").GetBoolean());

        var projects = body.GetProperty("projects").EnumerateArray().ToList();
        Assert.Equal(new[] { projA, projB }, projects.Select(p => p.GetProperty("projectId").GetGuid()).ToArray());
        Assert.Equal(new[] { 0, 1 }, projects.Select(p => p.GetProperty("sortOrder").GetInt32()).ToArray());
        Assert.Equal("S72A", projects[0].GetProperty("projectCode").GetString());

        var absenceTypes = body.GetProperty("absenceTypes").EnumerateArray().ToList();
        Assert.Equal(new[] { "SICK_DAY", "VACATION" }, absenceTypes.Select(a => a.GetProperty("type").GetString()).ToArray());
        Assert.Equal(new[] { 0, 1 }, absenceTypes.Select(a => a.GetProperty("sortOrder").GetInt32()).ToArray());
        Assert.Equal("Sygedag", absenceTypes[0].GetProperty("label").GetString());

        // DB state: container + DENSE rows.
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM user_skema_preferences WHERE employee_id = @p0", emp));
        Assert.Equal(0, await ReadProjectSortOrderAsync(emp, projA));
        Assert.Equal(1, await ReadProjectSortOrderAsync(emp, projB));
        Assert.Equal(0, await ReadAbsenceSortOrderAsync(emp, "SICK_DAY"));
        Assert.Equal(1, await ReadAbsenceSortOrderAsync(emp, "VACATION"));
    }

    /// <summary>FULL replacement: a second PUT with a smaller set removes the rows it omits.</summary>
    [Fact]
    public async Task SecondWrite_FullReplacement_OmittedRowsAreGone()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);
        var projB = await InsertProjectAsync(orgId, "S72B", 20);

        await PutOkAsync(emp, orgId, Projects((projA, 0), (projB, 1)), AbsenceTypes(("VACATION", 0), ("SICK_DAY", 1)));
        await PutOkAsync(emp, orgId, Projects((projB, 0)), AbsenceTypes(("SICK_DAY", 0)));

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM user_project_selections WHERE employee_id = @p0", emp));
        Assert.Equal(0, await ReadProjectSortOrderAsync(emp, projB));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM user_absence_selections WHERE employee_id = @p0", emp));
        Assert.Equal(0, await ReadAbsenceSortOrderAsync(emp, "SICK_DAY"));
    }

    /// <summary>
    /// <c>initialized_at</c> marks the FIRST write only — a later replacement write must not
    /// re-stamp it (the container upsert is ON CONFLICT DO NOTHING).
    /// </summary>
    [Fact]
    public async Task SecondWrite_PreservesInitializedAt()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);

        await PutOkAsync(emp, orgId, Projects((projA, 0)), AbsenceTypes());
        var first = (DateTime)(await ScalarAsync(
            "SELECT initialized_at FROM user_skema_preferences WHERE employee_id = @p0", emp))!;

        await PutOkAsync(emp, orgId, Projects(), AbsenceTypes(("VACATION", 0)));
        var second = (DateTime)(await ScalarAsync(
            "SELECT initialized_at FROM user_skema_preferences WHERE employee_id = @p0", emp))!;

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Empty arrays are a LEGAL full replacement (the R4 even-when-empty state): the
    /// container exists, zero selection rows persist, and the response serves zero rows.
    /// </summary>
    [Fact]
    public async Task EmptyArrays_ConfiguredWithZeroRows()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);

        var rsp = await PutPreferencesAsync(emp, orgId, new { projects = Array.Empty<object>(), absenceTypes = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("configured").GetBoolean());
        Assert.Empty(body.GetProperty("projects").EnumerateArray());
        Assert.Empty(body.GetProperty("absenceTypes").EnumerateArray());

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM user_skema_preferences WHERE employee_id = @p0", emp));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM user_project_selections WHERE employee_id = @p0", emp));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM user_absence_selections WHERE employee_id = @p0", emp));
    }

    // ───────────────────── validation 422s ─────────────────────

    /// <summary>
    /// Project offenders — an unknown id, another org's project, and a DEACTIVATED project —
    /// are each rejected 422 with the offending ids listed; NOTHING persists (no container,
    /// no rows: validation precedes the write).
    /// </summary>
    [Fact]
    public async Task InvalidProjects_UnknownForeignAndInactive_422ListingOffenders()
    {
        var orgId = NewOrgId();
        var otherOrgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        await SeedEmployeeAsync(otherOrgId); // ensures the other org exists
        var unknown = Guid.NewGuid();
        var foreign = await InsertProjectAsync(otherOrgId, "S72F", 10);
        var inactive = await InsertProjectAsync(orgId, "S72I", 10);
        await ExecAsync("UPDATE projects SET is_active = FALSE WHERE project_id = @p0", inactive);

        var rsp = await PutPreferencesAsync(emp, orgId,
            new { projects = Projects((unknown, 0), (foreign, 1), (inactive, 2)), absenceTypes = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("row_preferences_invalid", body.GetProperty("error").GetString());
        var offenders = body.GetProperty("invalidProjectIds").EnumerateArray().Select(e => e.GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { unknown, foreign, inactive }, offenders);

        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM user_skema_preferences WHERE employee_id = @p0", emp));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM user_project_selections WHERE employee_id = @p0", emp));
    }

    /// <summary>
    /// Absence-type offenders: an ORG-HIDDEN type (absence_type_visibility) and an
    /// eligibility-filtered type (CHILD_SICK_DAY with no eligibility row ⇒ ineligible,
    /// the opt-in default) are both outside the CURRENT catalog ⇒ 422 listing them; a
    /// catalog type submitted alongside does not rescue the request, and nothing persists.
    /// </summary>
    [Fact]
    public async Task InvalidAbsenceTypes_OrgHiddenAndIneligible_422ListingOffenders()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        await ExecAsync(
            "INSERT INTO absence_type_visibility (org_id, absence_type, is_hidden, set_by) VALUES (@p0, 'CARE_DAY', TRUE, 'test')",
            orgId);

        var rsp = await PutPreferencesAsync(emp, orgId, new
        {
            projects = Array.Empty<object>(),
            absenceTypes = AbsenceTypes(("VACATION", 0), ("CARE_DAY", 1), ("CHILD_SICK_DAY", 2)),
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("row_preferences_invalid", body.GetProperty("error").GetString());
        var offenders = body.GetProperty("invalidAbsenceTypes").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?> { "CARE_DAY", "CHILD_SICK_DAY" }, offenders);

        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM user_absence_selections WHERE employee_id = @p0", emp));
    }

    /// <summary>
    /// Duplicates are a validation 422 (listed separately) — never a PK-violation 500 from
    /// the replacement INSERT.
    /// </summary>
    [Fact]
    public async Task Duplicates_422ListingOffenders()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);

        var rsp = await PutPreferencesAsync(emp, orgId, new
        {
            projects = Projects((projA, 0), (projA, 1)),
            absenceTypes = AbsenceTypes(("VACATION", 0), ("VACATION", 1)),
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(projA, body.GetProperty("duplicateProjectIds").EnumerateArray().Single().GetGuid());
        Assert.Equal("VACATION", body.GetProperty("duplicateAbsenceTypes").EnumerateArray().Single().GetString());
    }

    // ───────────────────── R4: un-evented ─────────────────────

    /// <summary>
    /// THE R4 un-evented pin: a successful preference write adds NO outbox_events row and NO
    /// audit_projection row (view preference ≠ domain state — no event family, no audit
    /// projection, the ProjectRepository selection precedent).
    /// </summary>
    [Fact]
    public async Task PreferenceWrite_IsUnEvented_NoOutboxRow_NoAuditProjectionRow()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);

        var outboxBefore = await CountAsync("SELECT COUNT(*) FROM outbox_events");
        var auditBefore = await CountAsync("SELECT COUNT(*) FROM audit_projection");
        var eventsBefore = await CountAsync("SELECT COUNT(*) FROM events");

        await PutOkAsync(emp, orgId, Projects((projA, 0)), AbsenceTypes(("VACATION", 0)));

        Assert.Equal(outboxBefore, await CountAsync("SELECT COUNT(*) FROM outbox_events"));
        Assert.Equal(auditBefore, await CountAsync("SELECT COUNT(*) FROM audit_projection"));
        Assert.Equal(eventsBefore, await CountAsync("SELECT COUNT(*) FROM events"));
    }

    // ───────────────── authorization (SELF-ONLY — Step-5a B3) ─────────────────

    /// <summary>B3 case 1 (pure Employee, foreign target): an employee may only write their
    /// OWN preferences — a same-org colleague's id is refused 403, nothing persists.</summary>
    [Fact]
    public async Task Employee_CannotWriteAnotherEmployeesPreferences_403()
    {
        var orgId = NewOrgId();
        var victim = await SeedEmployeeAsync(orgId);
        var attacker = await SeedEmployeeAsync(orgId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(attacker, orgId));
        var rsp = await client.PutAsJsonAsync($"/api/skema/{victim}/row-preferences",
            new { projects = Array.Empty<object>(), absenceTypes = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM user_skema_preferences WHERE employee_id = @p0", victim));
    }

    /// <summary>
    /// B3 case 2 — THE MIXED-ROLE CASE (the S70-R9f1 escalation shape): a JWT carrying an
    /// ELEVATED primary role (LocalHR) anchored in a DISJOINT org PLUS an Employee-level
    /// scope covering the victim's org. Pre-fix this bypassed the Employee own-data branch
    /// (primary role ≠ Employee) and the covering-scope branch admitted the write
    /// (ValidateEmployeeAccessAsync accepts ANY covering scope without requiring the
    /// admitting scope to be elevated). Self-only refuses it outright: 403, nothing persists.
    /// </summary>
    [Fact]
    public async Task MixedRoleJwt_ElevatedDisjointPlusEmployeeScopeCoveringVictim_403()
    {
        var victimOrg = NewOrgId();
        var disjointOrg = NewOrgId();
        var victim = await SeedEmployeeAsync(victimOrg);
        await SeedEmployeeAsync(disjointOrg); // ensures the disjoint org exists

        var token = MintToken("hr_s72_mixed", StatsTidRoles.LocalHR, disjointOrg, new[]
        {
            new RoleScope(StatsTidRoles.LocalHR, disjointOrg, "ORG_ONLY"),
            new RoleScope(StatsTidRoles.Employee, victimOrg, "ORG_ONLY"), // covers the victim
        });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var rsp = await client.PutAsJsonAsync($"/api/skema/{victim}/row-preferences",
            new { projects = Array.Empty<object>(), absenceTypes = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM user_skema_preferences WHERE employee_id = @p0", victim));
    }

    /// <summary>B3 case 3 (positive control): the self-write stays 200 — the employee
    /// writing their OWN preferences is the one admitted path.</summary>
    [Fact]
    public async Task SelfWrite_Returns200()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);

        var rsp = await PutPreferencesAsync(emp, orgId, new
        {
            projects = Projects((projA, 0)),
            absenceTypes = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM user_skema_preferences WHERE employee_id = @p0", emp));
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private static string NewOrgId() => "S72P" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static object[] Projects(params (Guid Id, int SortOrder)[] items)
        => items.Select(i => (object)new { projectId = i.Id, sortOrder = i.SortOrder }).ToArray();

    private static object[] AbsenceTypes(params (string Type, int SortOrder)[] items)
        => items.Select(i => (object)new { absenceType = i.Type, sortOrder = i.SortOrder }).ToArray();

    private async Task<string> SeedEmployeeAsync(string orgId)
    {
        var employeeId = "emp_s72_put_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, orgId, "AC", "OK24");
        return employeeId;
    }

    private async Task<HttpResponseMessage> PutPreferencesAsync(string employeeId, string orgId, object body)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId, orgId));
        return await client.PutAsJsonAsync($"/api/skema/{employeeId}/row-preferences", body);
    }

    private async Task PutOkAsync(string employeeId, string orgId, object[] projects, object[] absenceTypes)
    {
        var rsp = await PutPreferencesAsync(employeeId, orgId, new { projects, absenceTypes });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    private async Task<Guid> InsertProjectAsync(string orgId, string code, int orgSortOrder)
    {
        var result = await ScalarAsync(
            """
            INSERT INTO projects (org_id, project_code, project_name, sort_order, created_by)
            VALUES (@p0, @p1, @p2, @p3, 'test')
            RETURNING project_id
            """, orgId, code, "S72 PUT test " + code, orgSortOrder);
        return (Guid)result!;
    }

    private async Task<int> ReadProjectSortOrderAsync(string employeeId, Guid projectId)
        => Convert.ToInt32(await ScalarAsync(
            "SELECT sort_order FROM user_project_selections WHERE employee_id = @p0 AND project_id = @p1",
            employeeId, projectId));

    private async Task<int> ReadAbsenceSortOrderAsync(string employeeId, string absenceType)
        => Convert.ToInt32(await ScalarAsync(
            "SELECT sort_order FROM user_absence_selections WHERE employee_id = @p0 AND absence_type = @p1",
            employeeId, absenceType));

    private async Task<long> CountAsync(string sql, params object[] args)
        => Convert.ToInt64(await ScalarAsync(sql, args));

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return await cmd.ExecuteScalarAsync();
    }

    private static string MintEmployeeToken(string actorId, string orgId)
        => MintToken(actorId, StatsTidRoles.Employee, orgId,
            new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });

    private static string MintToken(string actorId, string role, string orgId, RoleScope[] scopes)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: role,
            agreementCode: "AC",
            orgId: orgId,
            scopes: scopes);
    }
}
