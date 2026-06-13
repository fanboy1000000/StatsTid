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
/// S72 / TASK-7201 — THE critical R4 regression class (SPRINT-72 R18): existing
/// CONTAINER-LESS users see ZERO behavior change on the month GET. Two pre-S72 populations
/// exist, and each fallback path is pinned EXPLICITLY:
///
/// <list type="number">
///   <item><b>No selections at all</b> — the `projects` field serves ALL org projects in the
///     org order (<c>ORDER BY p.sort_order, p.project_code</c>), exactly as pre-S72.</item>
///   <item><b>Legacy selections</b> (post-TASK-7200-backfill state:
///     <c>ups.sort_order = p.sort_order</c> copied, duplicates included) — the `projects`
///     field serves the selected set in the IDENTICAL sequence the pre-S72
///     <c>ORDER BY p.sort_order, p.project_code</c> produced, BECAUSE container-less reads
///     STAY on the LIVE org ordering (Step-5a B2: the per-user
///     <c>ups.sort_order</c> ordering applies ONLY when the R4 container exists; the
///     backfilled values are a one-shot snapshot and are never consulted for this
///     population — so a post-migration admin reorder of org sort_order is followed
///     exactly as it was pre-S72, pinned below).</item>
/// </list>
///
/// The existing field set (shape + values) is pinned too, so the four ADDITIVE S72 fields
/// provably did not perturb a container-less user's response.
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaRowPreferencesFallbackRegressionTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot once
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// Container-less, NO selections: the `projects` field = ALL org active projects in the
    /// pre-S72 org order — including the (sort_order, project_code) tiebreak across
    /// duplicate org sort_orders — and the org sortOrder values are served verbatim.
    /// </summary>
    [Fact]
    public async Task ContainerLess_NoSelections_ProjectsField_AllOrgProjectsInOrgOrder()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        // Deliberate duplicate org sort_order (20) — the code tiebreak decides.
        var projB = await InsertProjectAsync(orgId, "B-CODE", orgSortOrder: 20);
        var projA = await InsertProjectAsync(orgId, "A-CODE", orgSortOrder: 10);
        var projC = await InsertProjectAsync(orgId, "A2-CODE", orgSortOrder: 20);

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        var served = body.GetProperty("projects").EnumerateArray().ToList();
        // Pre-S72 order: 10/A-CODE, 20/A2-CODE, 20/B-CODE.
        Assert.Equal(new[] { projA, projC, projB },
            served.Select(p => p.GetProperty("projectId").GetGuid()).ToArray());
        Assert.Equal(new[] { 10, 20, 20 },
            served.Select(p => p.GetProperty("sortOrder").GetInt32()).ToArray());
    }

    /// <summary>
    /// Container-less WITH legacy selections (the post-backfill population:
    /// <c>ups.sort_order = p.sort_order</c>, duplicates included): the `projects` field
    /// serves the SELECTED set in a sequence IDENTICAL to the pre-S72
    /// <c>ORDER BY p.sort_order, p.project_code</c> — container-less reads stay on the
    /// LIVE org ordering (Step-5a B2), so this population is order-invariant by
    /// construction. This is the named zero-behavior-change pin.
    /// </summary>
    [Fact]
    public async Task ContainerLess_WithBackfilledSelections_OrderIdenticalToPreS72()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projB = await InsertProjectAsync(orgId, "B-CODE", orgSortOrder: 20);
        var projA = await InsertProjectAsync(orgId, "A-CODE", orgSortOrder: 10);
        var projC = await InsertProjectAsync(orgId, "A2-CODE", orgSortOrder: 20); // duplicate 20
        await InsertProjectAsync(orgId, "UNSELECTED", orgSortOrder: 5);

        // The exact post-TASK-7200-backfill state of a legacy user's rows: per-user
        // sort_order copied from the matching projects.sort_order (NO container row).
        await InsertBackfilledSelectionAsync(emp, projB, 20);
        await InsertBackfilledSelectionAsync(emp, projA, 10);
        await InsertBackfilledSelectionAsync(emp, projC, 20);

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        var served = body.GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("projectId").GetGuid()).ToArray();
        // Pre-S72 sequence (p.sort_order, p.project_code): A(10), A2(20), B(20).
        Assert.Equal(new[] { projA, projC, projB }, served);

        // And the new fields agree without changing the old one: not configured; visible
        // mirrors the same selected sequence (today's fallback).
        var prefs = body.GetProperty("rowPreferences");
        Assert.False(prefs.GetProperty("configured").GetBoolean());
        Assert.Equal(served, prefs.GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("projectId").GetGuid()).ToArray());
    }

    /// <summary>
    /// Step-5a B2, the container-less half: ordering stays LIVE. An admin reorders the org
    /// <c>projects.sort_order</c> AFTER the TASK-7200 backfill — a container-less user's
    /// `projects` field must follow the NEW org order exactly as it did pre-S72 (the
    /// backfilled <c>ups.sort_order</c> snapshot still carries the OLD values and must not
    /// be consulted; ordering by it would freeze the user on the stale sequence and
    /// additionally disagree with the served org <c>sortOrder</c> values). The configured
    /// half (per-user order frozen) is pinned in <c>SkemaRowPreferencesMonthGetTests</c>.
    /// </summary>
    [Fact]
    public async Task ContainerLess_AdminReorderAfterMigration_FollowsLiveOrgOrder()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "A-CODE", orgSortOrder: 10);
        var projB = await InsertProjectAsync(orgId, "B-CODE", orgSortOrder: 20);
        var projC = await InsertProjectAsync(orgId, "C-CODE", orgSortOrder: 30);

        // The post-backfill legacy rows: ups.sort_order copied from the ORIGINAL org values.
        await InsertBackfilledSelectionAsync(emp, projA, 10);
        await InsertBackfilledSelectionAsync(emp, projB, 20);
        await InsertBackfilledSelectionAsync(emp, projC, 30);

        // Admin reorders the org catalog post-migration: C first, A last.
        await UpdateOrgSortOrderAsync(projC, 1);
        await UpdateOrgSortOrderAsync(projA, 99);

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        // LIVE org order (1/C, 20/B, 99/A) — NOT the stale backfilled 10/A, 20/B, 30/C.
        var served = body.GetProperty("projects").EnumerateArray().ToList();
        Assert.Equal(new[] { projC, projB, projA },
            served.Select(p => p.GetProperty("projectId").GetGuid()).ToArray());
        // Sequence and served org sortOrder values agree (no sequence/value disagreement).
        Assert.Equal(new[] { 1, 20, 99 },
            served.Select(p => p.GetProperty("sortOrder").GetInt32()).ToArray());

        // The fallback-mode rowPreferences view mirrors the same live sequence.
        Assert.Equal(new[] { projC, projB, projA },
            body.GetProperty("rowPreferences").GetProperty("projects").EnumerateArray()
                .Select(p => p.GetProperty("projectId").GetGuid()).ToArray());
    }

    /// <summary>
    /// The container-less month GET still serves the COMPLETE pre-S72 field set with its
    /// established shapes (the S72 fields are purely additive): year/month/daysInMonth,
    /// projects (projectId/projectCode/projectName/sortOrder per item), absenceTypes
    /// (type/label/fullDayOnly per item), entries, absences, workTime, dailyNorm, approval,
    /// employeeDeadline, managerDeadline.
    /// S73 / TASK-7301 (SPRINT-73 R3/R7 — legitimate behavior change, refinement
    /// REFINEMENT-s73-ui-testing-fix-bundle): the absence-type item set gained
    /// <c>fullDayOnly</c> (owner ruling D-A) — the pinned per-item field set was updated to
    /// include it, and the response gained the additive <c>consumptionBasis</c> array.
    /// </summary>
    [Fact]
    public async Task ContainerLess_ExistingFieldSet_IntactAlongsideAdditiveFields()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        await InsertProjectAsync(orgId, "A-CODE", orgSortOrder: 10);

        var body = await GetMonthAsync(emp, orgId, 2025, 5);

        // Existing scalar/envelope fields.
        Assert.Equal(2025, body.GetProperty("year").GetInt32());
        Assert.Equal(5, body.GetProperty("month").GetInt32());
        Assert.Equal(31, body.GetProperty("daysInMonth").GetInt32());
        Assert.Equal("2025-06-02", body.GetProperty("employeeDeadline").GetString());
        Assert.Equal("2025-06-05", body.GetProperty("managerDeadline").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("approval").ValueKind);

        // Existing per-item shapes.
        var project = body.GetProperty("projects").EnumerateArray().Single();
        Assert.Equal(new[] { "projectId", "projectCode", "projectName", "sortOrder" },
            project.EnumerateObject().Select(p => p.Name).ToArray());
        // S73 / TASK-7301 (R3/R7 — cited above): the item set gained fullDayOnly.
        var absenceType = body.GetProperty("absenceTypes").EnumerateArray().First();
        Assert.Equal(new[] { "type", "label", "fullDayOnly" },
            absenceType.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Empty(body.GetProperty("entries").EnumerateArray());
        Assert.Empty(body.GetProperty("absences").EnumerateArray());
        Assert.Empty(body.GetProperty("workTime").EnumerateArray());
        Assert.Equal(31, body.GetProperty("dailyNorm").EnumerateArray().Count());

        // The four S72 additions exist WITHOUT having displaced anything.
        Assert.True(body.TryGetProperty("rowPreferences", out _));
        Assert.True(body.TryGetProperty("catalogs", out _));
        Assert.True(body.TryGetProperty("boundaryWorkTime", out _));
        Assert.True(body.TryGetProperty("fullDayNormAtMonthEnd", out _));
        // The S73 addition (R3) exists without having displaced anything either.
        Assert.True(body.TryGetProperty("consumptionBasis", out _));
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private static string NewOrgId() => "S72F" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<string> SeedEmployeeAsync(string orgId)
    {
        var employeeId = "emp_s72_fb_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, orgId, "AC", "OK24");
        return employeeId;
    }

    private async Task<JsonElement> GetMonthAsync(string employeeId, string orgId, int year, int month)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId, orgId));
        var rsp = await client.GetAsync($"/api/skema/{employeeId}/month?year={year}&month={month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> InsertProjectAsync(string orgId, string code, int orgSortOrder)
    {
        var result = await ScalarAsync(
            """
            INSERT INTO projects (org_id, project_code, project_name, sort_order, created_by)
            VALUES (@p0, @p1, @p2, @p3, 'test')
            RETURNING project_id
            """, orgId, code, "S72 fallback test " + code, orgSortOrder);
        return (Guid)result!;
    }

    /// <summary>The post-TASK-7200-backfill row shape of a LEGACY selection
    /// (<c>sort_order</c> copied from <c>projects.sort_order</c>; no container row).</summary>
    private async Task InsertBackfilledSelectionAsync(string employeeId, Guid projectId, int backfilledSortOrder)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_project_selections (employee_id, project_id, sort_order)
            VALUES (@p0, @p1, @p2)
            """, conn);
        cmd.Parameters.AddWithValue("p0", employeeId);
        cmd.Parameters.AddWithValue("p1", projectId);
        cmd.Parameters.AddWithValue("p2", backfilledSortOrder);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>The post-migration ADMIN REORDER (the B2 trigger): a live org-level
    /// <c>projects.sort_order</c> update, as the project-update endpoint performs.</summary>
    private async Task UpdateOrgSortOrderAsync(Guid projectId, int sortOrder)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE projects SET sort_order = @p0 WHERE project_id = @p1", conn);
        cmd.Parameters.AddWithValue("p0", sortOrder);
        cmd.Parameters.AddWithValue("p1", projectId);
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
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }
}
