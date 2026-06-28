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
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// SPRINT-104 / TASK-10404 (Enhedsspor Phase 1b, ADR-038 D3/D8/D10) — the units MANAGEMENT regression
/// suite over the just-landed <c>UnitRepository</c> + <c>UnitEndpoints</c> + the extended cross-Org
/// transfer branch of <c>AdminEndpoints</c>. It pins the heavier half of Phase 1: the two-regime
/// concurrency (within-Organisation <c>unit-org-</c> advisory + recursive-CTE cycle guard; the
/// cross-Organisation person unit-change EXTENDING the users-transfer path), CRUD + If-Match, the
/// delete cascade (re-parent children UP + re-home members UP + clear leaders), the leader
/// member-invariant + on-move re-sync, the cross-Org transfer parity (leader cleared, edges
/// re-anchored, primary_org recompute, manager-with-reports BLOCKED), audit-projection parity, and the
/// LocalHR floor / cross-styrelse no-leak (the S76 invariant).
///
/// <list type="number">
///   <item><b>Held-lock interleave</b> — a held <c>unit-org-</c> advisory BLOCKS a concurrent
///     in-Organisation structural op AND a cross-Organisation transfer that takes the same key
///     (<c>pg_locks ⋈ pg_stat_activity</c> waiter barrier, the S95/S100 pattern); a DISTINCT
///     Organisation does NOT block.</item>
///   <item><b>Cycle guard</b> — a move making a unit its own ancestor (or itself) → 422.</item>
///   <item><b>Partial-rank ordering</b> — a create/move with rank(child) ≤ rank(parent) is rejected; a
///     level-skip (an <c>omrade</c> directly parenting a <c>team</c>) is ALLOWED.</item>
///   <item><b>CRUD + If-Match</b> — create/rename/move/delete happy paths; a stale If-Match → 412.</item>
///   <item><b>Delete cascade</b> — deleting a mid-level unit re-parents surviving CHILDREN up
///     (per-child <c>UnitMoved</c>) + re-homes DIRECT members up (per-member <c>UserUnitChanged</c>,
///     or unit_id NULL if top-level) + clears <c>unit_leaders</c>. Members keep <c>primary_org_id</c>.</item>
///   <item><b>Leader member-invariant + on-move re-sync</b> — designating a non-member → rejected; a
///     member-leader's same-Org unit move removes their old-unit <c>unit_leaders</c> row in-tx.</item>
///   <item><b>Cross-Org transfer parity</b> — sets unit_id + recomputes primary_org_id, clears the
///     moved user's old-unit leadership (+ <c>UnitLeaderRemoved</c>), re-anchors the user's own
///     reporting edges, BLOCKS (422) a transfer of a user with active reports; the both-org LocalHR
///     floor + If-Match hold.</item>
///   <item><b>Audit-projection parity</b> — the new Unit*/UserUnitChanged events produce
///     <c>audit_projection</c> rows with the correct <c>target_org_id</c>.</item>
///   <item><b>Floor / no-leak</b> — a LocalHR scoped to org A cannot create/move/delete a unit in org B
///     (S76 per-scope floor; cross-styrelse 403); the same-org LocalHR is admitted.</item>
/// </list>
///
/// <para>Each [Fact] boots a fresh Postgres testcontainer (init.sql + the host seeders), so the demo
/// STY02 unit tree exists and the isolated <c>t104_*</c> fixtures (an A-subtree under STY02 + a B-root
/// under STY05) are seeded fresh per test. Endpoint-level via
/// <see cref="StatsTidWebApplicationFactory"/>; direct DB reads for the lock-waiter + audit assertions.
/// Idioms mirror <see cref="StatsTid.Tests.Regression.ReportingLine.S95FlatOrgLockTests"/> +
/// <see cref="UnitFoundationTests"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S104UnitManagementTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // ── Organisations (init.sql seed): STY02 (under MAO MIN01) + STY05 (under a DIFFERENT MAO MIN02)
    //    are the cross-Organisation pair; STY01 (under MIN01) is the disjoint-scope no-leak fixture. ──
    private const string OrgA = "STY02";   // an ORGANISATION — the isolated A-subtree lives here
    private const string OrgB = "STY05";   // a DIFFERENT ORGANISATION — the transfer destination
    private const string OrgC = "STY01";   // a third ORGANISATION — the cross-styrelse no-leak probe
    private const string Min01 = "MIN01";  // a MAO

    // ── Isolated A-subtree under STY02 (fixed UUIDs, disjoint from the demo 000000d0-… tree). The
    //    partial-rank chain direktion(1) → omrade(2) → kontor(3); a separate top-level team leaf. ──
    private static readonly Guid UA1 = Guid.Parse("a1000000-0000-0000-0000-000000000001"); // direktion, top
    private static readonly Guid UA2 = Guid.Parse("a1000000-0000-0000-0000-000000000002"); // omrade,   child UA1
    private static readonly Guid UA3 = Guid.Parse("a1000000-0000-0000-0000-000000000003"); // kontor,   child UA2
    private static readonly Guid UATop = Guid.Parse("a1000000-0000-0000-0000-000000000004"); // team, top-level leaf
    // ── Isolated B-root under STY05 (the transfer destination unit). ──
    private static readonly Guid UB1 = Guid.Parse("b1000000-0000-0000-0000-000000000001"); // direktion, top

    // ── Isolated users (all STY02, is_active TRUE). ──
    private const string LeaderUser = "t104_leader"; // unit UA2 — designated leader of UA2; childless; reports PRIMARY to MgrUser
    private const string MemberUser = "t104_member"; // unit UA3 — a plain member (the non-member-of-UA2 probe)
    private const string MgrUser = "t104_mgr";       // unit UA2 — MANAGES ReportUser (the with-reports transfer block)
    private const string ReportUser = "t104_report"; // unit UA3 — reports PRIMARY to MgrUser
    private const string TopUser = "t104_top";       // unit UATop — direct member of the top-level leaf

    private static readonly string[] AllUsers = { LeaderUser, MemberUser, MgrUser, ReportUser, TopUser };
    private static readonly Guid[] AllUnits = { UA1, UA2, UA3, UATop, UB1 };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (MAO→ORGANISATION tree + the demo unit tree + configs)

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
    //  Seed / cleanup
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        // Units: the A-subtree under STY02 + the B-root under STY05. version defaults to 1.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES
                (@ua1,  @orgA, NULL,  'direktion', 'S104 A Direktion'),
                (@ua2,  @orgA, @ua1,  'omrade',    'S104 A Omrade'),
                (@ua3,  @orgA, @ua2,  'kontor',    'S104 A Kontor'),
                (@uatop,@orgA, NULL,  'team',      'S104 A Top Leaf'),
                (@ub1,  @orgB, NULL,  'direktion', 'S104 B Direktion')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("ua1", UA1);
            cmd.Parameters.AddWithValue("ua2", UA2);
            cmd.Parameters.AddWithValue("ua3", UA3);
            cmd.Parameters.AddWithValue("uatop", UATop);
            cmd.Parameters.AddWithValue("ub1", UB1);
            cmd.Parameters.AddWithValue("orgA", OrgA);
            cmd.Parameters.AddWithValue("orgB", OrgB);
            await cmd.ExecuteNonQueryAsync();
        }

        // Users with their single structural unit (primary_org_id == the unit's Organisation, the
        // derived anchor — all STY02 here).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active)
            VALUES
                (@leader, @leader, '$2a$11$fake', 'T104 Leader', 't104_leader@test.dk', @orgA, @ua2,   'HK', 'OK24', TRUE),
                (@member, @member, '$2a$11$fake', 'T104 Member', 't104_member@test.dk', @orgA, @ua3,   'HK', 'OK24', TRUE),
                (@mgr,    @mgr,    '$2a$11$fake', 'T104 Mgr',    't104_mgr@test.dk',    @orgA, @ua2,   'HK', 'OK24', TRUE),
                (@report, @report, '$2a$11$fake', 'T104 Report', 't104_report@test.dk', @orgA, @ua3,   'HK', 'OK24', TRUE),
                (@top,    @top,    '$2a$11$fake', 'T104 Top',    't104_top@test.dk',    @orgA, @uatop, 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            cmd.Parameters.AddWithValue("orgA", OrgA);
            cmd.Parameters.AddWithValue("ua2", UA2);
            cmd.Parameters.AddWithValue("ua3", UA3);
            cmd.Parameters.AddWithValue("uatop", UATop);
            await cmd.ExecuteNonQueryAsync();
        }

        // LeaderUser is the designated leader of UA2 (the member-invariant holds — leader.unit_id = UA2).
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO unit_leaders (unit_id, user_id) VALUES (@ua2, @leader) ON CONFLICT DO NOTHING", conn))
        {
            cmd.Parameters.AddWithValue("ua2", UA2);
            cmd.Parameters.AddWithValue("leader", LeaderUser);
            await cmd.ExecuteNonQueryAsync();
        }

        // Reporting edges (same Organisation STY02): ReportUser + LeaderUser both report PRIMARY to
        // MgrUser → MgrUser has ACTIVE reports (the with-reports transfer block); LeaderUser manages
        // nobody (childless → its transfer is allowed).
        var rlRepo = new ReportingLineRepository(_dbFactory);
        await rlRepo.AssignAsync(null, MakeLine(ReportUser, MgrUser));
        await rlRepo.AssignAsync(null, MakeLine(LeaderUser, MgrUser));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("leader", LeaderUser);
        cmd.Parameters.AddWithValue("member", MemberUser);
        cmd.Parameters.AddWithValue("mgr", MgrUser);
        cmd.Parameters.AddWithValue("report", ReportUser);
        cmd.Parameters.AddWithValue("top", TopUser);
    }

    private static ReportingLineModel MakeLine(string employeeId, string managerId) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        OrganisationId = OrgA,
        Relationship = "PRIMARY",
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        await ExecUsersAsync(conn, "DELETE FROM audit_projection WHERE actor_id = ANY(@ids) OR target_resource_id = ANY(@ids)");
        // Audit rows whose target_resource is a unit (UnitCreated/Moved/Deleted/LeaderDesignated carry
        // the unit id) — keyed on the isolated unit UUIDs (rendered as text).
        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM audit_projection WHERE target_resource_id = ANY(@uids)", conn))
        {
            cmd.Parameters.AddWithValue("uids", AllUnits.Select(u => u.ToString()).ToArray());
            await cmd.ExecuteNonQueryAsync();
        }
        await ExecUsersAsync(conn, "DELETE FROM reporting_line_audit WHERE reporting_line_id IN (SELECT reporting_line_id FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids))");
        await ExecUsersAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM unit_leaders WHERE user_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM users WHERE user_id = ANY(@ids)");
        // Drop EVERY non-demo unit in the test Organisations (the isolated A/B fixtures AND any units
        // the test itself created via the API) — not just the fixed fixture ids: a test-created CHILD of
        // a fixture unit would otherwise block the fixture parent's delete on the `parent_unit_id`
        // self-FK (the bug this replaces; production DELETE is a soft-delete, so a hard cascade only
        // ever runs here). Fresh container per test → the demo `000000d0-…` tree is the only thing that
        // must survive (the cleanup also runs PRE-seed in InitializeAsync). Detach users + leaders +
        // break the self-FK first, then delete (one batched command).
        await using (var nuke = new NpgsqlCommand(
            """
            UPDATE users SET unit_id = NULL
             WHERE unit_id IN (SELECT unit_id FROM units
                                WHERE organisation_id = ANY(@orgs) AND unit_id::text NOT LIKE '000000d0-%');
            DELETE FROM unit_leaders
             WHERE unit_id IN (SELECT unit_id FROM units
                                WHERE organisation_id = ANY(@orgs) AND unit_id::text NOT LIKE '000000d0-%');
            UPDATE units SET parent_unit_id = NULL
             WHERE organisation_id = ANY(@orgs) AND unit_id::text NOT LIKE '000000d0-%';
            DELETE FROM units
             WHERE organisation_id = ANY(@orgs) AND unit_id::text NOT LIKE '000000d0-%';
            """, conn))
        {
            nuke.Parameters.AddWithValue("orgs", new[] { OrgA, OrgB });
            await nuke.ExecuteNonQueryAsync();
        }

        async Task ExecUsersAsync(NpgsqlConnection c, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.AddWithValue("ids", AllUsers);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (1) Held-lock interleave — the `unit-org-` advisory (the two-regime concurrency spine).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A held STY02 <c>unit-org-</c> advisory BLOCKS a concurrent in-Organisation structural
    /// op (a rename) on the SAME Organisation: a <c>pg_locks ⋈ pg_stat_activity</c> waiter barrier
    /// proves the endpoint REACHED + BLOCKED on the lock; releasing it lets the op complete (200).</summary>
    [Fact]
    public async Task HeldUnitOrgLock_BlocksConcurrentInOrgStructuralOp_ThenCompletesOnRelease()
    {
        var admin = GlobalAdminClient();
        var version = await SelectUnitVersionAsync(UA3);

        var (holdConn, holdTx) = await AcquireUnitOrgLockOnSideConnAsync(OrgA);
        var released = false;
        try
        {
            // Fire a rename of UA3 (acquires the STY02 unit-org advisory → must BLOCK on the held key).
            var renameTask = SendAsync(admin, HttpMethod.Put, $"/api/admin/units/{UA3}",
                new { name = "S104 A Kontor (renamed)" }, ifMatch: Quote(version));

            Assert.True(await WaitForUnitOrgLockWaiterAsync(OrgA),
                "No backend was observed WAITING on the STY02 unit-org advisory — the rename did not serialize on the lock.");
            Assert.False(await Task.WhenAny(renameTask, Task.Delay(500)) == renameTask,
                "The rename completed while the unit-org advisory was held — it did not serialize on the lock.");

            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();
            released = true;

            var rsp = await renameTask;
            Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        }
        finally
        {
            if (!released) { await holdTx.RollbackAsync(); await holdConn.DisposeAsync(); }
        }
    }

    /// <summary>A held STY02 <c>unit-org-</c> advisory does NOT block a structural op in a DISTINCT
    /// Organisation (STY05) — distinct advisory keys do not interfere. The STY05 create completes
    /// promptly while STY02 is held.</summary>
    [Fact]
    public async Task HeldUnitOrgLock_DoesNotBlock_DistinctOrganisation()
    {
        var admin = GlobalAdminClient();
        var (holdConn, holdTx) = await AcquireUnitOrgLockOnSideConnAsync(OrgA);
        try
        {
            var createTask = admin.PostAsJsonAsync("/api/admin/units",
                new { organisationId = OrgB, type = "direktion", name = "S104 distinct-org probe" });

            var completed = await Task.WhenAny(createTask, Task.Delay(3000)) == createTask;
            Assert.True(completed,
                "A STY05 unit create blocked while the STY02 unit-org advisory was held — distinct Organisations must not interfere.");
            Assert.Equal(HttpStatusCode.Created, (await createTask).StatusCode);
        }
        finally
        {
            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();
        }
    }

    /// <summary>A held STY02 <c>unit-org-</c> advisory BLOCKS a concurrent CROSS-Organisation transfer
    /// (STY02 → STY05). The transfer composes under the fixed total lock order (all
    /// <c>reporting-org-</c> → all <c>unit-org-</c>, id-sorted → row FOR UPDATE), so it takes BOTH
    /// Organisations' <c>unit-org-</c> keys and parks on the held STY02 key; releasing it completes
    /// the transfer (200). Proves the transfer serializes with in-Organisation structural ops.</summary>
    [Fact]
    public async Task HeldUnitOrgLock_BlocksConcurrentCrossOrgTransfer_ThenCompletesOnRelease()
    {
        var admin = GlobalAdminClient();
        var userVersion = await SelectUserVersionAsync(LeaderUser);

        var (holdConn, holdTx) = await AcquireUnitOrgLockOnSideConnAsync(OrgA);
        var released = false;
        try
        {
            // LeaderUser (childless) transfers STY02 → STY05. The transfer takes unit-org for BOTH
            // STY02 + STY05 (id-sorted → STY02 first) → blocks on the held STY02 key.
            var transferTask = SendAsync(admin, HttpMethod.Put, $"/api/admin/users/{LeaderUser}",
                new { primaryOrgId = OrgB, unitId = UB1 }, ifMatch: Quote(userVersion));

            Assert.True(await WaitForUnitOrgLockWaiterAsync(OrgA),
                "No backend was observed WAITING on the STY02 unit-org advisory — the cross-Org transfer did not serialize on the unit-org lock.");
            Assert.False(await Task.WhenAny(transferTask, Task.Delay(500)) == transferTask,
                "The transfer completed while the STY02 unit-org advisory was held — it did not serialize on the lock.");

            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();
            released = true;

            var completed = await Task.WhenAny(transferTask, Task.Delay(8000)) == transferTask;
            Assert.True(completed, "The transfer did not complete after the unit-org key released.");
            Assert.Equal(HttpStatusCode.OK, (await transferTask).StatusCode);
            Assert.Equal(OrgB, await SelectUserPrimaryOrgAsync(LeaderUser));
        }
        finally
        {
            if (!released) { await holdTx.RollbackAsync(); await holdConn.DisposeAsync(); }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) Cycle guard — a move forming a cycle (or self-parenting) is rejected (422).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Move_FormingCycle_OrSelfParent_Returns422()
    {
        var admin = GlobalAdminClient();

        // Move UA1 (the root) under UA3 (its own descendant) → would create a cycle → 422.
        var ua1Version = await SelectUnitVersionAsync(UA1);
        var cycleRsp = await SendAsync(admin, HttpMethod.Put, $"/api/admin/units/{UA1}/move",
            new { newParentUnitId = UA3 }, ifMatch: Quote(ua1Version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cycleRsp.StatusCode);

        // Self-parent: move UA2 under itself → 422.
        var ua2Version = await SelectUnitVersionAsync(UA2);
        var selfRsp = await SendAsync(admin, HttpMethod.Put, $"/api/admin/units/{UA2}/move",
            new { newParentUnitId = UA2 }, ifMatch: Quote(ua2Version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, selfRsp.StatusCode);

        // Neither move took effect (UA1 stays top-level; UA2 stays under UA1).
        Assert.Null(await SelectUnitParentAsync(UA1));
        Assert.Equal(UA1, await SelectUnitParentAsync(UA2));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) Partial-rank ordering — rank(child) ≤ rank(parent) rejected; a level-skip is ALLOWED.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PartialRank_RejectsShallowerOrEqual_AllowsLevelSkip()
    {
        var admin = GlobalAdminClient();

        // CREATE rank-equal: an 'omrade' (rank 2) under UA2 'omrade' (rank 2) → rejected (422).
        var equalRsp = await admin.PostAsJsonAsync("/api/admin/units",
            new { organisationId = OrgA, parentUnitId = UA2, type = "omrade", name = "S104 rank-equal" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, equalRsp.StatusCode);

        // CREATE level-skip: a 'team' (rank 4) directly under UA1 'direktion' (rank 1) → ALLOWED (201).
        var skipRsp = await admin.PostAsJsonAsync("/api/admin/units",
            new { organisationId = OrgA, parentUnitId = UA1, type = "team", name = "S104 level-skip team" });
        Assert.Equal(HttpStatusCode.Created, skipRsp.StatusCode);

        // MOVE rank-shallower: move UA3 'kontor' (rank 3) under UATop 'team' (rank 4) → rejected (422)
        // (rank 3 ≤ rank 4). UATop is unrelated to UA3 so the cycle guard cannot pre-empt the rank check.
        var ua3Version = await SelectUnitVersionAsync(UA3);
        var moveRsp = await SendAsync(admin, HttpMethod.Put, $"/api/admin/units/{UA3}/move",
            new { newParentUnitId = UATop }, ifMatch: Quote(ua3Version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, moveRsp.StatusCode);
        Assert.Equal(UA2, await SelectUnitParentAsync(UA3)); // unmoved
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (4) CRUD + If-Match — happy paths; a stale If-Match → 412.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Crud_CreateRenameMoveDelete_HappyPaths_StaleIfMatch412()
    {
        var admin = GlobalAdminClient();

        // CREATE → 201, version 1.
        var createRsp = await admin.PostAsJsonAsync("/api/admin/units",
            new { organisationId = OrgA, type = "team", name = "S104 CRUD team" });
        Assert.Equal(HttpStatusCode.Created, createRsp.StatusCode);
        var created = await createRsp.Content.ReadFromJsonAsync<JsonElement>();
        var unitId = created.GetProperty("unitId").GetGuid();
        Assert.Equal(1L, created.GetProperty("version").GetInt64());

        // RENAME with If-Match "1" → 200, version 2.
        var renameRsp = await SendAsync(admin, HttpMethod.Put, $"/api/admin/units/{unitId}",
            new { name = "S104 CRUD team v2" }, ifMatch: Quote(1));
        Assert.Equal(HttpStatusCode.OK, renameRsp.StatusCode);
        Assert.Equal(2L, (await renameRsp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt64());

        // RENAME with a STALE If-Match "1" → 412 (current version is 2).
        var staleRsp = await SendAsync(admin, HttpMethod.Put, $"/api/admin/units/{unitId}",
            new { name = "S104 CRUD team stale" }, ifMatch: Quote(1));
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleRsp.StatusCode);

        // MOVE with If-Match "2" under UA1 → 200, version 3 (team rank 4 under direktion rank 1 → OK).
        var moveRsp = await SendAsync(admin, HttpMethod.Put, $"/api/admin/units/{unitId}/move",
            new { newParentUnitId = UA1 }, ifMatch: Quote(2));
        Assert.Equal(HttpStatusCode.OK, moveRsp.StatusCode);
        Assert.Equal(UA1, await SelectUnitParentAsync(unitId));

        // DELETE with a STALE If-Match "2" → 412 (current version is 3).
        var delStaleRsp = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/units/{unitId}",
            body: null, ifMatch: Quote(2));
        Assert.Equal(HttpStatusCode.PreconditionFailed, delStaleRsp.StatusCode);

        // DELETE with If-Match "3" → 204 (soft-deleted).
        var delRsp = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/units/{unitId}",
            body: null, ifMatch: Quote(3));
        Assert.Equal(HttpStatusCode.NoContent, delRsp.StatusCode);
        Assert.True(await IsUnitSoftDeletedAsync(unitId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (5) Delete cascade — re-parent children UP + re-home members UP + clear leaders.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Deleting a MID-level unit (UA2) soft-deletes it, RE-PARENTS its surviving child (UA3)
    /// UP to UA2's own parent (UA1) via a per-child <c>UnitMoved</c>, RE-HOMES its direct members
    /// (LeaderUser + MgrUser) UP to UA1 via a per-member <c>UserUnitChanged</c> (primary_org_id kept),
    /// and CLEARS its <c>unit_leaders</c> (LeaderUser) via <c>UnitLeaderRemoved</c>. RED on a naive
    /// hard-delete / orphaned children.</summary>
    [Fact]
    public async Task Delete_MidLevelUnit_ReparentsChildrenUp_RehomesMembersUp_ClearsLeaders()
    {
        var admin = GlobalAdminClient();
        var ua2Version = await SelectUnitVersionAsync(UA2);

        var rsp = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/units/{UA2}", body: null, ifMatch: Quote(ua2Version));
        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);

        // UA2 soft-deleted; the child UA3 re-parented UP to UA1.
        Assert.True(await IsUnitSoftDeletedAsync(UA2));
        Assert.Equal(UA1, await SelectUnitParentAsync(UA3));

        // Direct members of UA2 (LeaderUser + MgrUser) re-homed UP to UA1; primary_org_id unchanged.
        Assert.Equal(UA1, await SelectUserUnitAsync(LeaderUser));
        Assert.Equal(UA1, await SelectUserUnitAsync(MgrUser));
        Assert.Equal(OrgA, await SelectUserPrimaryOrgAsync(LeaderUser));
        Assert.Equal(OrgA, await SelectUserPrimaryOrgAsync(MgrUser));

        // Members of UA3 (MemberUser + ReportUser) are NOT direct members of UA2 → unchanged (still UA3).
        Assert.Equal(UA3, await SelectUserUnitAsync(MemberUser));
        Assert.Equal(UA3, await SelectUserUnitAsync(ReportUser));

        // UA2's leader rows cleared.
        Assert.Equal(0, await CountLeaderRowsAsync(UA2));

        // Events surfaced as audit_projection rows (P3): per-child UnitMoved, per-member UserUnitChanged,
        // UnitLeaderRemoved, the UnitDeleted itself — each with the STY02 target_org_id.
        Assert.Equal(1, await CountAuditAsync("UnitDeleted", UA2.ToString(), OrgA));
        Assert.Equal(1, await CountAuditAsync("UnitMoved", UA3.ToString(), OrgA));
        Assert.Equal(1, await CountAuditAsync("UserUnitChanged", LeaderUser, OrgA));
        Assert.Equal(1, await CountAuditAsync("UserUnitChanged", MgrUser, OrgA));
        Assert.Equal(1, await CountAuditAsync("UnitLeaderRemoved", LeaderUser, OrgA));
    }

    /// <summary>Deleting a TOP-LEVEL leaf unit (UATop) re-homes its direct member (TopUser) to
    /// <c>unit_id</c> NULL (homed directly at the Organisation), keeping primary_org_id; there are no
    /// children, so only the <c>UnitDeleted</c> (and the member's <c>UserUnitChanged</c>) fire.</summary>
    [Fact]
    public async Task Delete_TopLevelLeafUnit_RehomesMembersToNull_KeepsPrimaryOrg()
    {
        var admin = GlobalAdminClient();
        var version = await SelectUnitVersionAsync(UATop);

        var rsp = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/units/{UATop}", body: null, ifMatch: Quote(version));
        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);

        Assert.True(await IsUnitSoftDeletedAsync(UATop));
        // TopUser re-homed to NULL (the Organisation) but stays attributed to STY02.
        Assert.Null(await SelectUserUnitAsync(TopUser));
        Assert.Equal(OrgA, await SelectUserPrimaryOrgAsync(TopUser));
        Assert.Equal(1, await CountAuditAsync("UserUnitChanged", TopUser, OrgA));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (6) Leader member-invariant + on-move re-sync.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Designating a NON-member (MemberUser, in UA3) as a leader of UA2 → 422; designating a
    /// MEMBER (MemberUser, of UA3) as a leader of UA3 → 200 + the <c>unit_leaders</c> row +
    /// <c>UnitLeaderDesignated</c> audit.</summary>
    [Fact]
    public async Task DesignateLeader_NonMemberRejected_MemberAccepted()
    {
        var admin = GlobalAdminClient();

        // MemberUser is in UA3, NOT UA2 → cannot lead UA2 (the member-invariant).
        var nonMemberRsp = await admin.PostAsJsonAsync($"/api/admin/units/{UA2}/leaders", new { userId = MemberUser });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, nonMemberRsp.StatusCode);
        Assert.Equal(0, await CountLeaderRowAsync(UA2, MemberUser));

        // MemberUser IS a member of UA3 → can lead UA3.
        var memberRsp = await admin.PostAsJsonAsync($"/api/admin/units/{UA3}/leaders", new { userId = MemberUser });
        Assert.Equal(HttpStatusCode.OK, memberRsp.StatusCode);
        Assert.Equal(1, await CountLeaderRowAsync(UA3, MemberUser));
        Assert.Equal(1, await CountAuditAsync("UnitLeaderDesignated", MemberUser, OrgA));
    }

    /// <summary>A member-leader's SAME-Organisation unit move removes their OLD-unit
    /// <c>unit_leaders</c> row in-tx (the D3 member-invariant re-sync) + emits <c>UnitLeaderRemoved</c>.
    /// LeaderUser leads UA2; moving them to UA3 strips the UA2 leadership.</summary>
    [Fact]
    public async Task MemberMove_SameOrg_RemovesOldUnitLeadership_InTx()
    {
        var admin = GlobalAdminClient();
        Assert.Equal(1, await CountLeaderRowAsync(UA2, LeaderUser)); // precondition (seeded)

        var version = await SelectUserVersionAsync(LeaderUser);
        var rsp = await SendAsync(admin, HttpMethod.Put, $"/api/admin/users/{LeaderUser}/unit",
            new { unitId = UA3 }, ifMatch: Quote(version));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Equal(UA3, await SelectUserUnitAsync(LeaderUser));      // moved to UA3
        Assert.Equal(0, await CountLeaderRowAsync(UA2, LeaderUser));   // old-unit leadership stripped
        Assert.Equal(OrgA, await SelectUserPrimaryOrgAsync(LeaderUser)); // same Organisation → unchanged
        Assert.Equal(1, await CountAuditAsync("UnitLeaderRemoved", LeaderUser, OrgA));
        Assert.Equal(1, await CountAuditAsync("UserUnitChanged", LeaderUser, OrgA));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (7) Cross-Org transfer parity.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A cross-Organisation transfer (LeaderUser STY02 → STY05/UB1) sets <c>unit_id</c> +
    /// recomputes <c>primary_org_id</c> to the new unit's Organisation, CLEARS the moved user's old-unit
    /// <c>unit_leaders</c> (+ <c>UnitLeaderRemoved</c> on the OLD org), RE-ANCHORS the user's own
    /// reporting edges (a cross-Organisation PRIMARY edge is forbidden → closed), and emits
    /// <c>UserUnitChanged</c> + its audit row keyed on the NEW Organisation.</summary>
    [Fact]
    public async Task Transfer_SetsUnitAndPrimaryOrg_ClearsOldLeadership_ReanchorsEdges()
    {
        var admin = GlobalAdminClient();
        Assert.Equal(1, await CountLeaderRowAsync(UA2, LeaderUser)); // precondition
        Assert.Equal(1, await CountActiveEmployeeEdgesAsync(LeaderUser)); // its own PRIMARY edge

        var version = await SelectUserVersionAsync(LeaderUser);
        var rsp = await SendAsync(admin, HttpMethod.Put, $"/api/admin/users/{LeaderUser}",
            new { primaryOrgId = OrgB, unitId = UB1 }, ifMatch: Quote(version));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // Home + unit recomputed to STY05 / UB1.
        Assert.Equal(OrgB, await SelectUserPrimaryOrgAsync(LeaderUser));
        Assert.Equal(UB1, await SelectUserUnitAsync(LeaderUser));

        // Old-unit leadership cleared (the UnitLeaderRemoved is audited on the OLD Organisation STY02).
        Assert.Equal(0, await CountLeaderRowAsync(UA2, LeaderUser));
        Assert.Equal(1, await CountAuditAsync("UnitLeaderRemoved", LeaderUser, OrgA));

        // The user's own reporting edge is re-anchored (closed — no active cross-Org edge survives).
        Assert.Equal(0, await CountActiveEmployeeEdgesAsync(LeaderUser));

        // UserUnitChanged audited on the NEW Organisation (the derived primary_org_id).
        Assert.Equal(1, await CountAuditAsync("UserUnitChanged", LeaderUser, OrgB));
    }

    /// <summary>A cross-Organisation transfer of a user who still MANAGES active reports (MgrUser
    /// manages ReportUser) is BLOCKED (422) — re-assign the reports first, avoiding a silent cross-Org
    /// orphan. The home is unchanged.</summary>
    [Fact]
    public async Task Transfer_UserWithActiveReports_Blocked422_HomeUnchanged()
    {
        var admin = GlobalAdminClient();
        var version = await SelectUserVersionAsync(MgrUser);

        var rsp = await SendAsync(admin, HttpMethod.Put, $"/api/admin/users/{MgrUser}",
            new { primaryOrgId = OrgB, unitId = UB1 }, ifMatch: Quote(version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        Assert.Equal(OrgA, await SelectUserPrimaryOrgAsync(MgrUser)); // unchanged
    }

    /// <summary>The cross-Organisation transfer floors BOTH the OLD and NEW Organisations: a LocalHR
    /// scoped ONLY to the OLD Organisation (STY02) cannot move a user to STY05 → 403, home unchanged.
    /// (S76 containment-on-transfer.)</summary>
    [Fact]
    public async Task Transfer_OnlyOldOrgScope_Forbidden_HomeUnchanged()
    {
        var oldOrgOnly = LocalHrClient(OrgA); // covers STY02, NOT STY05
        var version = await SelectUserVersionAsync(TopUser);

        var rsp = await SendAsync(oldOrgOnly, HttpMethod.Put, $"/api/admin/users/{TopUser}",
            new { primaryOrgId = OrgB, unitId = UB1 }, ifMatch: Quote(version));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal(OrgA, await SelectUserPrimaryOrgAsync(TopUser)); // unchanged
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (8) Audit-projection parity — the new Unit* events produce rows with the right target_org_id.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AuditParity_CreateRenameMoveDesignate_ProduceProjectionRows_WithTargetOrg()
    {
        var admin = GlobalAdminClient();

        // CREATE → UnitCreated (target_org STY02, target_resource = the new unit id).
        var createRsp = await admin.PostAsJsonAsync("/api/admin/units",
            new { organisationId = OrgA, type = "team", name = "S104 audit team" });
        Assert.Equal(HttpStatusCode.Created, createRsp.StatusCode);
        var unitId = (await createRsp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unitId").GetGuid();
        Assert.Equal(1, await CountAuditAsync("UnitCreated", unitId.ToString(), OrgA));

        // RENAME → UnitRenamed (target_org resolved via ResolvedTargetOrgId = STY02).
        var renameRsp = await SendAsync(admin, HttpMethod.Put, $"/api/admin/units/{unitId}",
            new { name = "S104 audit team v2" }, ifMatch: Quote(1));
        Assert.Equal(HttpStatusCode.OK, renameRsp.StatusCode);
        Assert.Equal(1, await CountAuditAsync("UnitRenamed", unitId.ToString(), OrgA));

        // MOVE → UnitMoved (target_org STY02).
        var moveRsp = await SendAsync(admin, HttpMethod.Put, $"/api/admin/units/{unitId}/move",
            new { newParentUnitId = UA1 }, ifMatch: Quote(2));
        Assert.Equal(HttpStatusCode.OK, moveRsp.StatusCode);
        Assert.Equal(1, await CountAuditAsync("UnitMoved", unitId.ToString(), OrgA));

        // DESIGNATE → UnitLeaderDesignated (target_org STY02, target_resource = the designated user).
        var designateRsp = await admin.PostAsJsonAsync($"/api/admin/units/{UA3}/leaders", new { userId = MemberUser });
        Assert.Equal(HttpStatusCode.OK, designateRsp.StatusCode);
        Assert.Equal(1, await CountAuditAsync("UnitLeaderDesignated", MemberUser, OrgA));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (9) Floor / no-leak — a LocalHR scoped to org C cannot touch a unit in org A (S76).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnitOps_LocalHRScopedToOtherOrg_Forbidden_SameOrgAdmitted()
    {
        var otherOrg = LocalHrClient(OrgC); // a LocalHR scoped to STY01 only

        // CREATE a unit in STY02 → 403 (the floor over STY02 is not covered by an STY01 scope).
        var createRsp = await otherOrg.PostAsJsonAsync("/api/admin/units",
            new { organisationId = OrgA, type = "team", name = "S104 leak probe" });
        Assert.Equal(HttpStatusCode.Forbidden, createRsp.StatusCode);

        // MOVE an STY02 unit → 403.
        var ua3Version = await SelectUnitVersionAsync(UA3);
        var moveRsp = await SendAsync(otherOrg, HttpMethod.Put, $"/api/admin/units/{UA3}/move",
            new { newParentUnitId = UA1 }, ifMatch: Quote(ua3Version));
        Assert.Equal(HttpStatusCode.Forbidden, moveRsp.StatusCode);

        // DELETE an STY02 unit → 403.
        var ua2Version = await SelectUnitVersionAsync(UA2);
        var deleteRsp = await SendAsync(otherOrg, HttpMethod.Delete, $"/api/admin/units/{UA2}",
            body: null, ifMatch: Quote(ua2Version));
        Assert.Equal(HttpStatusCode.Forbidden, deleteRsp.StatusCode);

        // Nothing leaked: UA2 still active, UA3 still under UA2.
        Assert.False(await IsUnitSoftDeletedAsync(UA2));
        Assert.Equal(UA2, await SelectUnitParentAsync(UA3));

        // POSITIVE control: a LocalHR scoped to STY02 IS admitted (the floor admits the right org).
        var sameOrg = LocalHrClient(OrgA);
        var okRsp = await sameOrg.PostAsJsonAsync("/api/admin/units",
            new { organisationId = OrgA, type = "team", name = "S104 same-org admitted" });
        Assert.Equal(HttpStatusCode.Created, okRsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — clients / tokens
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "t104_gadmin", name: "t104_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: Min01,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient LocalHrClient(string scopeOrg)
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "t104_hr", name: "t104_hr", role: StatsTidRoles.LocalHR,
            agreementCode: "HK", orgId: scopeOrg,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, scopeOrg, "ORG_ONLY") });
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

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — HTTP
    // ════════════════════════════════════════════════════════════════════════════════

    private static string Quote(long version) => $"\"{version}\"";

    /// <summary>Sends a request with an optional JSON body + an optional If-Match precondition (the
    /// admin-strict quoted-ETag the units/transfer endpoints require for mutate). The typed
    /// <c>PostAsJsonAsync</c> helpers cannot set headers, hence the raw builder.</summary>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, object? body, string? ifMatch = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        if (ifMatch is not null)
            req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — held-lock interleave harness (the unit-org advisory; mirrors S95FlatOrgLockTests).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Holds the <c>unit-org-{org}</c> xact advisory key on a SIDE connection so a test can
    /// deterministically BLOCK any in-Organisation unit mutator (or a transfer touching that
    /// Organisation) that takes the same key, until the returned tx is rolled back. The caller owns
    /// disposal. The expression is byte-identical to <c>UnitRepository.AcquireUnitOrgLockAsync</c>.</summary>
    private async Task<(NpgsqlConnection conn, NpgsqlTransaction tx)> AcquireUnitOrgLockOnSideConnAsync(string organisationId)
    {
        var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using (var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('unit-org-' || @org))", conn, tx))
        {
            cmd.Parameters.AddWithValue("org", organisationId);
            await cmd.ExecuteScalarAsync();
        }
        return (conn, tx);
    }

    /// <summary>Polls <c>pg_locks</c> (joined to <c>pg_stat_activity</c> to exclude this session) until
    /// at least one OTHER backend is WAITING (<c>granted = false</c>) on the <c>unit-org-{org}</c>
    /// advisory key — proving a request actually REACHED and BLOCKED ON THE LOCK. Returns <c>true</c>
    /// once a waiter is seen; <c>false</c> on timeout.</summary>
    private async Task<bool> WaitForUnitOrgLockWaiterAsync(string organisationId, int timeoutMs = 5000)
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
                    FROM pg_locks pl
                    JOIN pg_stat_activity sa ON sa.pid = pl.pid
                    WHERE pl.locktype = 'advisory'
                      AND pl.granted = FALSE
                      AND pl.pid <> pg_backend_pid()
                      AND ((pl.classid::bigint << 32) | pl.objid::bigint)
                          = hashtext('unit-org-' || @org)::bigint
                )
                """, conn))
            {
                cmd.Parameters.AddWithValue("org", organisationId);
                if (await cmd.ExecuteScalarAsync() is true)
                    return true;
            }
            await Task.Delay(50);
        }
        return false;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — DB reads
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<long> SelectUnitVersionAsync(Guid unitId) =>
        await ScalarLongAsync("SELECT version FROM units WHERE unit_id = @id", ("id", unitId));

    private async Task<Guid?> SelectUnitParentAsync(Guid unitId) =>
        await ScalarGuidAsync("SELECT parent_unit_id FROM units WHERE unit_id = @id", ("id", unitId));

    private async Task<bool> IsUnitSoftDeletedAsync(Guid unitId) =>
        await ScalarLongAsync("SELECT COUNT(*) FROM units WHERE unit_id = @id AND deleted_at IS NOT NULL", ("id", unitId)) == 1;

    private async Task<long> SelectUserVersionAsync(string userId) =>
        await ScalarLongAsync("SELECT version FROM users WHERE user_id = @id", ("id", userId));

    private async Task<string?> SelectUserPrimaryOrgAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT primary_org_id FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (await cmd.ExecuteScalarAsync()) as string;
    }

    private async Task<Guid?> SelectUserUnitAsync(string userId) =>
        await ScalarGuidAsync("SELECT unit_id FROM users WHERE user_id = @id", ("id", userId));

    private async Task<long> CountLeaderRowAsync(Guid unitId, string userId) =>
        await ScalarLongAsync("SELECT COUNT(*) FROM unit_leaders WHERE unit_id = @u AND user_id = @uid",
            ("u", unitId), ("uid", userId));

    private async Task<long> CountLeaderRowsAsync(Guid unitId) =>
        await ScalarLongAsync("SELECT COUNT(*) FROM unit_leaders WHERE unit_id = @u", ("u", unitId));

    private async Task<long> CountActiveEmployeeEdgesAsync(string employeeId) =>
        await ScalarLongAsync("SELECT COUNT(*) FROM reporting_lines WHERE employee_id = @e AND effective_to IS NULL",
            ("e", employeeId));

    private async Task<long> CountAuditAsync(string eventType, string targetResourceId, string targetOrgId) =>
        await ScalarLongAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = @et AND target_resource_id = @trid AND target_org_id = @org",
            ("et", eventType), ("trid", targetResourceId), ("org", targetOrgId));

    private async Task<long> ScalarLongAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        var res = await cmd.ExecuteScalarAsync();
        return res is long l ? l : Convert.ToInt64(res);
    }

    private async Task<Guid?> ScalarGuidAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        var res = await cmd.ExecuteScalarAsync();
        return res is Guid g ? g : (Guid?)null;
    }
}
