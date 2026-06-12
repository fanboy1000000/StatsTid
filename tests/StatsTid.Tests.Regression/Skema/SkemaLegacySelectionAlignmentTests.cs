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
/// S72 / TASK-7201 — SPRINT-72 R14 alignment pins for the DEPRECATED legacy
/// project-selection endpoints (<c>POST/DELETE /api/projects/{orgId}/select/{projectId}</c>,
/// kept alive for ProjectPicker until the TASK-7205 R9 sweep): they now ALSO initialize the
/// R4 <c>user_skema_preferences</c> container on first write and maintain DENSE
/// <c>sort_order</c> (append-at-end on add; 0..n-1 reindex on remove) — while their HTTP
/// response contracts stay byte-identical to pre-S72 (200 <c>{projectId, selected}</c> /
/// 204 empty).
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaLegacySelectionAlignmentTests : IAsyncLifetime
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
    /// Legacy ADD: initializes the container on the FIRST write, appends at the end
    /// (dense 0,1,2 across three sequential adds), and keeps the pre-S72 response contract
    /// byte-identical — a 200 whose body has EXACTLY the two properties
    /// <c>projectId</c>/<c>selected</c>.
    /// </summary>
    [Fact]
    public async Task LegacyAdd_InitializesContainer_AppendsAtEndDense_ResponseUnchanged()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);
        var projB = await InsertProjectAsync(orgId, "S72B", 20);
        var projC = await InsertProjectAsync(orgId, "S72C", 30);
        var client = CreateEmployeeClient(emp, orgId);

        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM user_skema_preferences WHERE employee_id = @p0", emp));

        var rsp = await client.PostAsync($"/api/projects/{orgId}/select/{projA}", content: null);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(projA, body.GetProperty("projectId").GetGuid());
        Assert.True(body.GetProperty("selected").GetBoolean());
        Assert.Equal(2, body.EnumerateObject().Count()); // contract byte-identical: exactly the two pre-S72 fields

        // Container initialized by the FIRST legacy write (R14).
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM user_skema_preferences WHERE employee_id = @p0", emp));

        await client.PostAsync($"/api/projects/{orgId}/select/{projB}", content: null);
        await client.PostAsync($"/api/projects/{orgId}/select/{projC}", content: null);

        Assert.Equal(0, await ReadSortOrderAsync(emp, projA));
        Assert.Equal(1, await ReadSortOrderAsync(emp, projB));
        Assert.Equal(2, await ReadSortOrderAsync(emp, projC));
    }

    /// <summary>Re-adding an already-selected project stays a no-op (one row, order intact).</summary>
    [Fact]
    public async Task LegacyAdd_DuplicateAdd_NoOp_StaysDense()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);
        var client = CreateEmployeeClient(emp, orgId);

        await client.PostAsync($"/api/projects/{orgId}/select/{projA}", content: null);
        var rsp = await client.PostAsync($"/api/projects/{orgId}/select/{projA}", content: null);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM user_project_selections WHERE employee_id = @p0", emp));
        Assert.Equal(0, await ReadSortOrderAsync(emp, projA));
    }

    /// <summary>
    /// Legacy REMOVE of a middle row: the remaining rows are reindexed DENSE 0..n-1 in their
    /// existing order; the response contract stays the pre-S72 204 with an empty body.
    /// </summary>
    [Fact]
    public async Task LegacyRemove_MiddleRow_ReindexesDense_ResponseUnchanged()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);
        var projB = await InsertProjectAsync(orgId, "S72B", 20);
        var projC = await InsertProjectAsync(orgId, "S72C", 30);
        var client = CreateEmployeeClient(emp, orgId);
        await client.PostAsync($"/api/projects/{orgId}/select/{projA}", content: null);
        await client.PostAsync($"/api/projects/{orgId}/select/{projB}", content: null);
        await client.PostAsync($"/api/projects/{orgId}/select/{projC}", content: null);

        var rsp = await client.DeleteAsync($"/api/projects/{orgId}/select/{projB}");

        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);
        Assert.Empty(await rsp.Content.ReadAsStringAsync());
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM user_project_selections WHERE employee_id = @p0", emp));
        Assert.Equal(0, await ReadSortOrderAsync(emp, projA)); // dense again — no gap at the removed slot
        Assert.Equal(1, await ReadSortOrderAsync(emp, projC));
    }

    /// <summary>
    /// Removing the LAST selection leaves the R4 configured-EMPTY state: the container
    /// remains (a remove IS a preference write), and BOTH the month GET's
    /// <c>rowPreferences</c> AND the legacy <c>projects</c> field serve ZERO project rows
    /// (Step-5a B1: container exists ⇒ selections authoritative EVEN WHEN EMPTY on every
    /// row-serving read path — the all-org fallback is container-less-only. The user CHOSE
    /// zero rows; serving all projects would silently undo the choice).
    /// </summary>
    [Fact]
    public async Task LegacyRemove_LastSelection_LeavesConfiguredEmptyState()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);
        var client = CreateEmployeeClient(emp, orgId);
        await client.PostAsync($"/api/projects/{orgId}/select/{projA}", content: null);

        var rsp = await client.DeleteAsync($"/api/projects/{orgId}/select/{projA}");
        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM user_skema_preferences WHERE employee_id = @p0", emp));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM user_project_selections WHERE employee_id = @p0", emp));

        var month = await client.GetAsync($"/api/skema/{emp}/month?year=2025&month=5");
        Assert.Equal(HttpStatusCode.OK, month.StatusCode);
        var body = await month.Content.ReadFromJsonAsync<JsonElement>();
        var prefs = body.GetProperty("rowPreferences");
        Assert.True(prefs.GetProperty("configured").GetBoolean());
        Assert.Empty(prefs.GetProperty("projects").EnumerateArray());
        // Legacy field: configured-EMPTY ⇒ ALSO empty (B1 — no all-org fallback once the
        // container exists); the project stays addable via the catalog.
        Assert.Empty(body.GetProperty("projects").EnumerateArray());
        Assert.Equal(projA, body.GetProperty("catalogs").GetProperty("projects")
            .EnumerateArray().Single().GetProperty("projectId").GetGuid());
    }

    /// <summary>Legacy writes are un-evented too (the pre-S72 precedent is preserved).</summary>
    [Fact]
    public async Task LegacyAddAndRemove_StayUnEvented()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);
        var client = CreateEmployeeClient(emp, orgId);

        var outboxBefore = await CountAsync("SELECT COUNT(*) FROM outbox_events");
        var auditBefore = await CountAsync("SELECT COUNT(*) FROM audit_projection");

        await client.PostAsync($"/api/projects/{orgId}/select/{projA}", content: null);
        await client.DeleteAsync($"/api/projects/{orgId}/select/{projA}");

        Assert.Equal(outboxBefore, await CountAsync("SELECT COUNT(*) FROM outbox_events"));
        Assert.Equal(auditBefore, await CountAsync("SELECT COUNT(*) FROM audit_projection"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Step-5a B4 — per-employee serialization on EVERY preference mutation: the legacy add
    // PARKS on the established ADR-032 D4 employee advisory lock (lock FIRST inside the tx,
    // before MAX(sort_order)/reindex), so its derived reads happen against the LATEST
    // committed state once a competing preference writer releases. Choreography (the
    // WaiverResolutionTests parked-lock precedent): a foreign tx holds the lock AND mutates
    // the selection state (the row-preferences-PUT shape: delete the existing row) → the
    // legacy add provably parks (pg_locks ungranted ADVISORY wait — pre-fix the add never
    // waits on an advisory lock, only incidentally on row locks, so the probe is the
    // discriminator) → the foreign tx commits → the parked add resumes, re-derives MAX over
    // the post-commit state, and the durable table is dense/gap-free/duplicate-free.
    // Pre-fix end state: the add's MAX saw the pre-delete snapshot → the new row landed at
    // sort_order 1 with row 0 deleted → a GAP on disk (masked by the GET's dense projection).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LegacyAdd_ParksOnEmployeeAdvisoryLock_DurableEndStateDense()
    {
        var orgId = NewOrgId();
        var emp = await SeedEmployeeAsync(orgId);
        var projA = await InsertProjectAsync(orgId, "S72A", 10);
        var projB = await InsertProjectAsync(orgId, "S72B", 20);
        var client = CreateEmployeeClient(emp, orgId);
        await client.PostAsync($"/api/projects/{orgId}/select/{projA}", content: null); // row 0

        // tx A: hold the per-employee advisory lock, then perform the competing mutation
        // (delete projA's selection — what a racing PUT replacement does under this lock).
        await using var foreignConn = new NpgsqlConnection(_harness.ConnectionString);
        await foreignConn.OpenAsync();
        await using var foreignTx = await foreignConn.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @id))", foreignConn, foreignTx))
        {
            lockCmd.Parameters.AddWithValue("id", emp);
            await lockCmd.ExecuteScalarAsync();
        }
        await using (var delCmd = new NpgsqlCommand(
            "DELETE FROM user_project_selections WHERE employee_id = @e AND project_id = @p",
            foreignConn, foreignTx))
        {
            delCmd.Parameters.AddWithValue("e", emp);
            delCmd.Parameters.AddWithValue("p", projA);
            await delCmd.ExecuteNonQueryAsync();
        }

        // tx B: fire the legacy add — it must PARK on the advisory lock (lock precedes
        // MAX/reindex), not complete against the pre-delete snapshot.
        var postTask = Task.Run(() => client.PostAsync($"/api/projects/{orgId}/select/{projB}", (HttpContent?)null));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var parked = false;
        while (!parked && DateTime.UtcNow < deadline)
        {
            if (postTask.IsCompleted)
            {
                var early = await postTask;
                Assert.Fail($"the legacy add completed early ({(int)early.StatusCode}) without parking " +
                            "on the employee advisory lock — the B4 lock-first contract was not exercised.");
            }
            parked = await HasUngrantedAdvisoryWaitAsync();
            if (!parked)
                await Task.Delay(100);
        }
        Assert.True(parked, "the legacy add never parked on the employee advisory lock within 30s.");

        // Release: tx A commits; the parked add resumes against the post-commit state.
        await foreignTx.CommitAsync();
        var rsp = await postTask;
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // Durable end state: exactly the surviving row, DENSE at 0 — no gap (pre-fix: 1),
        // no duplicates. The density invariant is asserted on the RAW table, not the GET's
        // dense projection (which would mask exactly this corruption).
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM user_project_selections WHERE employee_id = @p0", emp));
        Assert.Equal(0, await ReadSortOrderAsync(emp, projB));
        Assert.Equal(0, await CountAsync(
            """
            SELECT COUNT(*) FROM (
                SELECT sort_order, ROW_NUMBER() OVER (ORDER BY sort_order) - 1 AS expected
                FROM user_project_selections WHERE employee_id = @p0
            ) d WHERE d.sort_order <> d.expected
            """, emp));
    }

    /// <summary>True when some backend on this database is waiting on an UNGRANTED advisory
    /// lock — the deterministic is-parked probe (the WaiverResolutionTests precedent; the
    /// polling backend itself never waits, and the per-class container scopes pg_locks to
    /// this class's database).</summary>
    private async Task<bool> HasUngrantedAdvisoryWaitAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted", conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private static string NewOrgId() => "S72L" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<string> SeedEmployeeAsync(string orgId)
    {
        var employeeId = "emp_s72_leg_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, orgId, "AC", "OK24");
        return employeeId;
    }

    private HttpClient CreateEmployeeClient(string employeeId, string orgId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId, orgId));
        return client;
    }

    private async Task<Guid> InsertProjectAsync(string orgId, string code, int orgSortOrder)
    {
        var result = await ScalarAsync(
            """
            INSERT INTO projects (org_id, project_code, project_name, sort_order, created_by)
            VALUES (@p0, @p1, @p2, @p3, 'test')
            RETURNING project_id
            """, orgId, code, "S72 legacy test " + code, orgSortOrder);
        return (Guid)result!;
    }

    private async Task<int> ReadSortOrderAsync(string employeeId, Guid projectId)
        => Convert.ToInt32(await ScalarAsync(
            "SELECT sort_order FROM user_project_selections WHERE employee_id = @p0 AND project_id = @p1",
            employeeId, projectId));

    private async Task<long> CountAsync(string sql, params object[] args)
        => Convert.ToInt64(await ScalarAsync(sql, args));

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
