using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S110 / TASK-11003 (Enhedsspor Phase 4, ADR-037) — the MAO-delete-vs-child-orgs lifecycle guard + the
/// symmetric create-side TOCTOU close. The pre-existing S98 gap: <c>DELETE /api/admin/organizations/{id}</c>
/// (the MAO branch) blocked only on active USERS in the subtree, NOT on active child Organisations — so an
/// "empty" MAO (no direct users) with active child Organisations could be soft-deleted, orphaning them
/// path-rooted under an inactive MAO. Two symmetric halves:
/// <list type="number">
///   <item><b>Delete-side block</b> — deleting a MAO with active child Organisations → 422
///     (<c>organisationCount</c>); the existing active-user block + Organisation-delete are unchanged.</item>
///   <item><b>Create-side TOCTOU</b> — the org-CREATE-under-MAO path re-checks the parent MAO's
///     <c>is_active</c> UNDER A SHARED ROW LOCK (FOR SHARE) inside its tx, BEFORE the INSERT. FOR SHARE
///     conflicts with the delete's FOR UPDATE, so "create reads MAO active → delete locks+counts(0)+
///     soft-deletes+commits → create's INSERT lands" can no longer commit an active org under a dead MAO:
///     the create blocks on the lock, then 422s on the is_active re-read.</item>
/// </list>
///
/// <para><b>Topology.</b> A fresh org tree disjoint from the init.sql seed: a MAO with ONE empty active
/// child Organisation (<c>T110_MAO_KIDS</c> → <c>T110_ORG_EMPTY</c>) [the child-org block subject], a
/// childless MAO (<c>T110_MAO_BARE</c>) [the 204 subject], a MAO whose child holds an active user
/// (<c>T110_MAO_USERS</c> → <c>T110_ORG_USER</c> + 1 user) [the unchanged active-user block], and a
/// childless MAO (<c>T110_MAO_RACE</c>) [the concurrency subject]. Endpoint-level via
/// <see cref="StatsTidWebApplicationFactory"/>; the concurrency test mirrors the S95/S98 side-connection
/// lock harness — but on a ROW lock (FOR UPDATE), so the waiter barrier uses <c>pg_blocking_pids</c>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S110MaoDeleteGuardTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string MaoKids = "T110_MAO_KIDS";       // MAO with one EMPTY active child Organisation
    private const string OrgEmpty = "T110_ORG_EMPTY";     // empty child under MaoKids
    private const string MaoBare = "T110_MAO_BARE";       // childless MAO (the 204 subject)
    private const string MaoUsers = "T110_MAO_USERS";     // MAO whose child holds an active user
    private const string OrgUser = "T110_ORG_USER";       // child under MaoUsers — holds 1 active user
    private const string MaoRace = "T110_MAO_RACE";       // childless MAO (the concurrency subject)
    private const string OrgNew = "T110_ORG_NEW";         // explicit-id org created in the happy-path control
    private const string OrgRaceChild = "T110_ORG_RACE";  // explicit-id org the race create attempts

    private const string MaoKidsPath = "/T110_MAO_KIDS/";
    private const string OrgEmptyPath = "/T110_MAO_KIDS/T110_ORG_EMPTY/";
    private const string MaoBarePath = "/T110_MAO_BARE/";
    private const string MaoUsersPath = "/T110_MAO_USERS/";
    private const string OrgUserPath = "/T110_MAO_USERS/T110_ORG_USER/";
    private const string MaoRacePath = "/T110_MAO_RACE/";

    private const string EmpUser = "t110_emp_user"; // active employee homed on OrgUser

    private static readonly string[] AllTestOrgs =
        { OrgEmpty, OrgUser, OrgNew, OrgRaceChild, MaoKids, MaoBare, MaoUsers, MaoRace };
    private static readonly string[] AllTestUsers = { EmpUser };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (init.sql seed orgs are present)

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await CleanupAsync(conn);
        await SeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await CleanupAsync(conn);
        }
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (1) Delete-side block — a MAO with active child Organisations → 422 (RED-on-old).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(1) Deleting a MAO that has an active CHILD Organisation (but NO direct active users, so the
    /// pre-existing active-user block passes) → 422 with <c>organisationCount &gt; 0</c>; the MAO STAYS
    /// active and the child STAYS active. RED-on-old: pre-S110 the delete counted only active users (0 here)
    /// → it would soft-delete the MAO, orphaning the active child under an inactive root.</summary>
    [Fact]
    public async Task Delete_MaoWithActiveChildOrg_Returns422_MaoAndChildStayActive()
    {
        var admin = GlobalAdminClient();

        var rsp = await DeleteOrgAsync(admin, MaoKids);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("organisationCount").GetInt64() > 0);

        // Neither the MAO nor its active child was flipped.
        Assert.True(await IsOrgActiveAsync(MaoKids));
        Assert.True(await IsOrgActiveAsync(OrgEmpty));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) A childless MAO → 204 (the empty-MAO path still works).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(2) Deleting a MAO with NO child Organisations and NO users → 204; it is is_active=false
    /// afterwards.</summary>
    [Fact]
    public async Task Delete_EmptyMao_NoChildren_Returns204()
    {
        var admin = GlobalAdminClient();

        var rsp = await DeleteOrgAsync(admin, MaoBare);
        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);

        Assert.False(await IsOrgActiveAsync(MaoBare));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) The pre-existing active-user block is UNCHANGED (a MAO whose subtree holds a user → 422).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(3) Deleting a MAO whose child Organisation holds an active user still → 422 with
    /// <c>employeeCount &gt; 0</c> (the S98 active-user block, unchanged). The MAO stays active.</summary>
    [Fact]
    public async Task Delete_MaoWithActiveUsersBeneath_Returns422_EmployeeCount_Unchanged()
    {
        var admin = GlobalAdminClient();

        var rsp = await DeleteOrgAsync(admin, MaoUsers);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("employeeCount").GetInt64() > 0);

        Assert.True(await IsOrgActiveAsync(MaoUsers));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (4) The Organisation-delete path is UNCHANGED (the new block is MAO-only).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(4) Deleting an empty ORGANISATION (the child of MaoKids) → 204 — the new child-org block is
    /// MAO-only and does not affect an Organisation delete.</summary>
    [Fact]
    public async Task Delete_EmptyOrganisation_Returns204_Unchanged()
    {
        var admin = GlobalAdminClient();

        var rsp = await DeleteOrgAsync(admin, OrgEmpty);
        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);

        Assert.False(await IsOrgActiveAsync(OrgEmpty));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (5) Happy-path control — an org create under an ACTIVE, uncontended MAO still succeeds.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(5) The new in-tx FOR-SHARE active-parent re-check does NOT break the happy path: creating an
    /// ORGANISATION under an active MAO (no contention) → 201, and the org is active under the MAO.</summary>
    [Fact]
    public async Task CreateUnderActiveMao_Succeeds()
    {
        var admin = GlobalAdminClient();

        var rsp = await CreateOrgAsync(admin, OrgNew, "Org Ny", MaoBare);
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        Assert.True(await IsOrgActiveAsync(OrgNew));
        var (_, parent) = await ReadOrgPathAndParentAsync(OrgNew);
        Assert.Equal(MaoBare, parent);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (6) THE KEYSTONE — the create-AFTER-count ordering cannot commit an active org under a MAO
    //      that is concurrently being soft-deleted. We simulate the DELETE handler having locked the
    //      MAO (FOR UPDATE) + counted 0 children + flipped is_active=FALSE (NOT yet committed) on a side
    //      connection, then fire the org-create endpoint: its out-of-tx parent read still sees the MAO
    //      ACTIVE (the side tx is uncommitted), so it proceeds — and its in-tx FOR-SHARE re-check BLOCKS
    //      on the side tx's FOR UPDATE. We prove it parks (a waiter blocked by the side connection), then
    //      commit the side tx (is_active=FALSE committed); the create's re-read now sees NO active parent
    //      → 422, and NO active org was created under the (now inactive) MAO.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateUnderMao_RacesAgainstSoftDelete_BlocksThen422_NoActiveOrgUnderInactiveMao()
    {
        var admin = GlobalAdminClient();

        // 1. Side connection: replicate the DELETE handler's in-tx steps — FOR UPDATE the active MAO
        //    (count-then-soft-delete already done for a childless MAO) + flip is_active=FALSE. Hold open.
        var sideConn = new NpgsqlConnection(_harness.ConnectionString);
        await sideConn.OpenAsync();
        var holderPid = await BackendPidAsync(sideConn);
        var sideTx = await sideConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var committed = false;
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT org_id FROM organizations WHERE org_id = @id AND is_active = TRUE FOR UPDATE",
                sideConn, sideTx))
            {
                lockCmd.Parameters.AddWithValue("id", MaoRace);
                Assert.NotNull(await lockCmd.ExecuteScalarAsync()); // the MAO is active + now locked.
            }
            await using (var delCmd = new NpgsqlCommand(
                "UPDATE organizations SET is_active = FALSE, updated_at = now() WHERE org_id = @id",
                sideConn, sideTx))
            {
                delCmd.Parameters.AddWithValue("id", MaoRace);
                await delCmd.ExecuteNonQueryAsync();
            }

            // 2. Fire the org-create under the (being-deleted) MAO. The out-of-tx parent read sees the MAO
            //    ACTIVE (side tx uncommitted), so it reaches the in-tx FOR-SHARE re-check → BLOCKS.
            var createTask = CreateOrgAsync(admin, OrgRaceChild, "Org Race", MaoRace);

            // 3a. Barrier: a backend is WAITING, blocked specifically by our side connection (the FOR SHARE
            //     parked on the FOR UPDATE). Proves the create REACHED + BLOCKED on the lock, not merely slow.
            Assert.True(await WaitForWaiterBlockedByAsync(holderPid),
                "No backend was observed blocked by the side connection's MAO lock — the org-create did not serialize on the FOR-SHARE active-parent re-check (the create-side TOCTOU is open).");

            // 3b. Proof of blocking: the create is parked while we hold the lock.
            Assert.False(await Task.WhenAny(createTask, Task.Delay(500)) == createTask,
                "The org-create completed while the MAO row was locked + flipped — it did not serialize on the FOR-SHARE re-check.");

            // 4. Commit the soft-delete → the parked create acquires the FOR SHARE and re-reads is_active=FALSE.
            await sideTx.CommitAsync();
            committed = true;

            var createRsp = await createTask;

            // 5. The create is rejected (the parent MAO is now soft-deleted) → 422, NOT a committed child.
            Assert.Equal(HttpStatusCode.UnprocessableEntity, createRsp.StatusCode);
        }
        finally
        {
            if (!committed)
                await sideTx.RollbackAsync();
            await sideTx.DisposeAsync();
            await sideConn.DisposeAsync();
        }

        // 6. The decisive structural invariant: NO active org is parented under the (now inactive) MAO,
        //    and the attempted child does not exist as an active row.
        Assert.Equal(0, await CountActiveChildOrgsAsync(MaoRace));
        Assert.False(await IsOrgActiveAsync(OrgRaceChild));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed / cleanup
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version, is_active)
            VALUES
                (@maoKids,  'T110 MAO Kids',  'MAO',          NULL,      @maoKidsPath,  'AC', 'OK24', TRUE),
                (@orgEmpty, 'T110 Org Empty', 'ORGANISATION', @maoKids,  @orgEmptyPath, 'AC', 'OK24', TRUE),
                (@maoBare,  'T110 MAO Bare',  'MAO',          NULL,      @maoBarePath,  'AC', 'OK24', TRUE),
                (@maoUsers, 'T110 MAO Users', 'MAO',          NULL,      @maoUsersPath, 'AC', 'OK24', TRUE),
                (@orgUser,  'T110 Org User',  'ORGANISATION', @maoUsers, @orgUserPath,  'AC', 'OK24', TRUE),
                (@maoRace,  'T110 MAO Race',  'MAO',          NULL,      @maoRacePath,  'AC', 'OK24', TRUE)
            ON CONFLICT (org_id) DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("maoKids", MaoKids);
            cmd.Parameters.AddWithValue("orgEmpty", OrgEmpty);
            cmd.Parameters.AddWithValue("maoBare", MaoBare);
            cmd.Parameters.AddWithValue("maoUsers", MaoUsers);
            cmd.Parameters.AddWithValue("orgUser", OrgUser);
            cmd.Parameters.AddWithValue("maoRace", MaoRace);
            cmd.Parameters.AddWithValue("maoKidsPath", MaoKidsPath);
            cmd.Parameters.AddWithValue("orgEmptyPath", OrgEmptyPath);
            cmd.Parameters.AddWithValue("maoBarePath", MaoBarePath);
            cmd.Parameters.AddWithValue("maoUsersPath", MaoUsersPath);
            cmd.Parameters.AddWithValue("orgUserPath", OrgUserPath);
            cmd.Parameters.AddWithValue("maoRacePath", MaoRacePath);
            await cmd.ExecuteNonQueryAsync();
        }

        // One active employee homed on OrgUser (so MaoUsers trips the unchanged active-user block).
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, EmpUser, OrgUser, "AC", "OK24", ensureOrg: false);
    }

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        await ExecAsync(conn, "DELETE FROM approval_periods WHERE employee_id = ANY(@users)");
        await ExecAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@users) OR manager_id = ANY(@users)");
        await ExecAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@users)");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@users)");
        await ExecAsync(conn, "DELETE FROM users WHERE user_id = ANY(@users)");

        // Audit-projection rows (FK target_org_id → organizations) before the orgs.
        await ExecAsync(conn, "DELETE FROM audit_projection WHERE target_org_id = ANY(@orgs)");
        await ExecAsync(conn, "DELETE FROM outbox_events WHERE stream_id = ANY(@streams)");
        await ExecAsync(conn, "DELETE FROM events WHERE stream_id = ANY(@streams)");
        await ExecAsync(conn, "DELETE FROM event_streams WHERE stream_id = ANY(@streams)");
        await ExecAsync(conn, "DELETE FROM organizations WHERE org_id = ANY(@orgs)");

        async Task ExecAsync(NpgsqlConnection c, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.AddWithValue("users", AllTestUsers);
            cmd.Parameters.AddWithValue("orgs", AllTestOrgs);
            cmd.Parameters.AddWithValue("streams", AllTestOrgs.Select(o => $"org-{o}").ToArray());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  HTTP helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private static async Task<HttpResponseMessage> DeleteOrgAsync(HttpClient client, string orgId)
        => await client.DeleteAsync($"/api/admin/organizations/{orgId}");

    private static async Task<HttpResponseMessage> CreateOrgAsync(
        HttpClient client, string orgId, string orgName, string parentOrgId)
        => await client.PostAsJsonAsync("/api/admin/organizations",
            new { orgId, orgName, orgType = "ORGANISATION", parentOrgId });

    // ════════════════════════════════════════════════════════════════════════════════
    //  DB reads
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<bool> IsOrgActiveAsync(string orgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT is_active FROM organizations WHERE org_id = @o", conn);
        cmd.Parameters.AddWithValue("o", orgId);
        return (await cmd.ExecuteScalarAsync()) is true;
    }

    private async Task<(string Path, string? Parent)> ReadOrgPathAndParentAsync(string orgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT materialized_path, parent_org_id FROM organizations WHERE org_id = @o", conn);
        cmd.Parameters.AddWithValue("o", orgId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var path = reader.GetString(0);
        var parent = reader.IsDBNull(1) ? null : reader.GetString(1);
        return (path, parent);
    }

    private async Task<int> CountActiveChildOrgsAsync(string maoId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM organizations WHERE parent_org_id = @m AND is_active = TRUE", conn);
        cmd.Parameters.AddWithValue("m", maoId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<int> BackendPidAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_backend_pid()", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Polls <c>pg_stat_activity</c>/<c>pg_blocking_pids</c> until at least one OTHER backend is
    /// blocked specifically BY <paramref name="holderPid"/> (the side connection holding the MAO row lock)
    /// — proving a request REACHED + BLOCKED on the row lock. Returns <c>true</c> once a waiter is seen.</summary>
    private async Task<bool> WaitForWaiterBlockedByAsync(int holderPid, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        while (DateTime.UtcNow < deadline)
        {
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity sa
                    WHERE sa.pid <> @holder
                      AND @holder = ANY(pg_blocking_pids(sa.pid))
                )
                """, conn))
            {
                cmd.Parameters.AddWithValue("holder", holderPid);
                if (await cmd.ExecuteScalarAsync() is true)
                    return true;
            }
            await Task.Delay(50);
        }
        return false;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Tokens / clients
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "t110_gadmin", name: "t110_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: MaoKids,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });
}
