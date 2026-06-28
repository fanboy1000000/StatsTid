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
/// S98 / TASK-9805 / ADR-035 — the regression suite for the org-structure backend gaps the
/// redesigned Organisation (Global administration) page needs on the flat S97 Enhed model:
/// <list type="number">
///   <item><b>Org soft-delete</b> — <c>DELETE /api/admin/organizations/{orgId}</c>. GlobalAdmin-floored;
///     in-tx <c>SELECT … FOR UPDATE</c>; 204 on soft-delete (is_active=false); 422 + employeeCount if
///     blocked (an Organisation with active users; a MAO with any active user beneath its
///     materialized_path); 404 if absent/already-inactive; 403 for a non-GlobalAdmin.</item>
///   <item><b>Org move/re-parent</b> — <c>PUT /api/admin/organizations/{orgId}/move</c>. GlobalAdmin;
///     ORGANISATION-only (MAO → 422); target must be an active MAO (else 422); no-op / self → 400;
///     200 returns the moved org with a RECOMPUTED <c>materialized_path</c> = newParent.path + orgId + "/".</item>
///   <item><b>Aggregated tree</b> — <c>GET /api/admin/organizations/tree</c>. HROrAbove read; the
///     MAO→Organisation forest with per-node employeeCount; visibility-bounded (GlobalAdmin all;
///     scoped roles their accessible orgs). (S103 / TASK-10305: the per-Organisation enheder nesting
///     was retired with the legacy Enhed model — units return in S104+.)</item>
/// </list>
///
/// <para><b>The BLOCKER-1 pin (move-preserves-roster):</b> a move REWRITES the moved row's
/// <c>materialized_path</c> in the SAME tx. The tree-roster reads
/// (<c>GET …/reporting-lines/tree/{org}/medarbejdere</c> →
/// <see cref="ApprovalPeriodRepository.GetMedarbejderRosterForTreeAsync"/>) scope by
/// <c>materialized_path LIKE prefix</c>; an unrewritten path would silently drop the org's employees.
/// We MOVE an Organisation (with an active employee) to a different MAO and re-query the SAME roster
/// read — the employee is STILL returned under the NEW path.</para>
///
/// <para><b>Topology.</b> The fixture seeds its OWN fresh org tree (disjoint from the heavily-populated
/// init.sql seed orgs, so the counts stay deterministic and cleanup is tractable): two MAOs
/// (<c>T98_MAO_A</c>, <c>T98_MAO_B</c>), an ORGANISATION <c>T98_ORG_EMP</c> under MAO_A holding ONE active
/// employee (the move + MAO-subtree-block subject), and an EMPTY ORGANISATION <c>T98_ORG_EMPTY</c> under
/// MAO_A (the 204 subject). A GlobalAdmin / a scoped LocalHR / a LocalAdmin are minted via
/// <see cref="JwtTokenService"/>. Endpoint-level via <see cref="StatsTidWebApplicationFactory"/>; mirrors
/// <see cref="S97EnhedTests"/> (token minting + raw seed + cleanup) and
/// <see cref="StatsTid.Tests.Regression.Approval.MedarbejderRosterReadTests"/> (the tree-roster read).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S98OrgStructureTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // Fresh test org tree (disjoint from the init.sql seed orgs).
    private const string MaoA = "T98_MAO_A";        // MAO — origin parent
    private const string MaoB = "T98_MAO_B";        // MAO — move target
    private const string OrgEmp = "T98_ORG_EMP";    // ORGANISATION under MAO_A — holds 1 active employee
    private const string OrgEmpty = "T98_ORG_EMPTY"; // ORGANISATION under MAO_A — empty (the 204 subject)

    private const string MaoAPath = "/T98_MAO_A/";
    private const string MaoBPath = "/T98_MAO_B/";
    private const string OrgEmpPath = "/T98_MAO_A/T98_ORG_EMP/";
    private const string OrgEmptyPath = "/T98_MAO_A/T98_ORG_EMPTY/";

    private const string EmpUser = "t98_emp_user"; // active employee homed on OrgEmp

    private static readonly string[] AllTestOrgs = { OrgEmp, OrgEmpty, MaoA, MaoB };
    private static readonly string[] AllTestUsers = { EmpUser };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
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
    //  (1) MOVE-PRESERVES-ROSTER — the BLOCKER-1 pin (the in-tx materialized_path rewrite).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(1) The move REWRITES the moved org's <c>materialized_path</c> in the SAME tx, so the
    /// tree-roster read (which scopes by <c>materialized_path LIKE prefix</c>) STILL returns the org's
    /// employees under the NEW path. We seed an employee on <c>T98_ORG_EMP</c> (under MAO_A), confirm the
    /// roster returns them, MOVE the Organisation to MAO_B, then re-query the SAME roster read — the
    /// employee is STILL present, and the moved org's new path is <c>/T98_MAO_B/T98_ORG_EMP/</c>. Without
    /// the in-tx path rewrite the LIKE-prefix read would silently drop the employee.</summary>
    [Fact]
    public async Task Move_RewritesMaterializedPath_RosterStillReturnsEmployeeUnderNewPath()
    {
        var admin = GlobalAdminClient();

        // Pre-move: the roster for T98_ORG_EMP returns the employee.
        var before = await GetRosterEmployeeIdsAsync(admin, OrgEmp);
        Assert.Contains(EmpUser, before);

        // S98 Step-7a FIX 3 — the MAO-keyed roster (which scopes by the MAO's materialized_path
        // LIKE prefix, covering the whole subtree) also sees the employee under MAO_A BEFORE the
        // move, and NOT under MAO_B yet. Asserting BOTH prefixes proves a path rewrite that forgot
        // to strip the OLD root (or never re-rooted) would fail — not just that the moved org's own
        // roster still works.
        Assert.Contains(EmpUser, await GetRosterEmployeeIdsAsync(admin, MaoA));      // old root: present
        Assert.DoesNotContain(EmpUser, await GetRosterEmployeeIdsAsync(admin, MaoB)); // new root: absent

        // Move T98_ORG_EMP from MAO_A → MAO_B.
        var move = await MoveOrgAsync(admin, OrgEmp, MaoB);
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);

        var moveBody = await move.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(MaoB, moveBody.GetProperty("parentOrgId").GetString());
        // The recomputed materialized_path = newParent.path + orgId + "/".
        Assert.Equal($"{MaoBPath}{OrgEmp}/", moveBody.GetProperty("materializedPath").GetString());

        // The DB row carries the rewritten path + new parent.
        var (dbPath, dbParent) = await ReadOrgPathAndParentAsync(OrgEmp);
        Assert.Equal($"{MaoBPath}{OrgEmp}/", dbPath);
        Assert.Equal(MaoB, dbParent);

        // Post-move: the SAME roster read (keyed on the moved org) STILL returns the employee
        // (now resolved under the new path).
        var after = await GetRosterEmployeeIdsAsync(admin, OrgEmp);
        Assert.Contains(EmpUser, after);

        // S98 Step-7a FIX 3 — the COMPLEMENTARY invariant: the employee NO LONGER appears under the
        // OLD MAO_A subtree-roster, and NOW appears under the NEW MAO_B subtree-roster. A path rewrite
        // that forgot to strip the old root (e.g. kept the org under both prefixes) would FAIL here.
        Assert.DoesNotContain(EmpUser, await GetRosterEmployeeIdsAsync(admin, MaoA)); // old root: stripped
        Assert.Contains(EmpUser, await GetRosterEmployeeIdsAsync(admin, MaoB));        // new root: present
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) Soft-delete blocked-if-employees (Organisation + MAO-subtree) + empty → 204.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(2a) Deleting an ORGANISATION that has active users → 422 with employeeCount>0, and the
    /// org STAYS active (not flipped).</summary>
    [Fact]
    public async Task Delete_OrganisationWithEmployees_Returns422_EmployeeCount_OrgStaysActive()
    {
        var admin = GlobalAdminClient();

        var rsp = await DeleteOrgAsync(admin, OrgEmp);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("employeeCount").GetInt64() > 0);

        // The org was NOT flipped — still active.
        Assert.True(await IsOrgActiveAsync(OrgEmp));
    }

    /// <summary>(2b) Deleting a MAO that has a child Organisation holding active users → 422 (the
    /// MAO-subtree block: any active user beneath the MAO's materialized_path), and the MAO stays
    /// active.</summary>
    [Fact]
    public async Task Delete_MaoWithEmployeesBeneath_Returns422_SubtreeBlock_MaoStaysActive()
    {
        var admin = GlobalAdminClient();

        var rsp = await DeleteOrgAsync(admin, MaoA);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("employeeCount").GetInt64() > 0);

        Assert.True(await IsOrgActiveAsync(MaoA));
    }

    /// <summary>(2c) Deleting an EMPTY Organisation (no active users) → 204; afterwards it is is_active=false
    /// and gone from both GET /organizations and GET /organizations/tree.</summary>
    [Fact]
    public async Task Delete_EmptyOrganisation_Returns204_GoneFromListAndTree()
    {
        var admin = GlobalAdminClient();

        var rsp = await DeleteOrgAsync(admin, OrgEmpty);
        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);

        Assert.False(await IsOrgActiveAsync(OrgEmpty));

        // Gone from the org list.
        var listIds = await GetOrgListIdsAsync(admin);
        Assert.DoesNotContain(OrgEmpty, listIds);

        // Gone from the aggregated tree (under MAO_A's children).
        var tree = await GetTreeAsync(admin);
        var orgEmptyInTree = tree.EnumerateArray()
            .SelectMany(m => m.GetProperty("organisations").EnumerateArray())
            .Any(o => o.GetProperty("orgId").GetString() == OrgEmpty);
        Assert.False(orgEmptyInTree);
    }

    /// <summary>(2d) Deleting an absent / already-soft-deleted org → 404. (After deleting the empty org,
    /// a second delete on it sees no ACTIVE row → 404.)</summary>
    [Fact]
    public async Task Delete_AlreadyInactiveOrAbsent_Returns404()
    {
        var admin = GlobalAdminClient();

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteOrgAsync(admin, OrgEmpty)).StatusCode);

        var second = await DeleteOrgAsync(admin, OrgEmpty);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);

        var absent = await DeleteOrgAsync(admin, "T98_NOPE");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) GlobalAdmin floor — move + delete are GlobalAdmin-only (403 for under-tier).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(3a) A non-GlobalAdmin (LocalAdmin / LocalHR), even one scoped to the org, calling DELETE
    /// → 403. Move + delete are GlobalAdmin-only.</summary>
    [Fact]
    public async Task Delete_NonGlobalAdmin_Returns403()
    {
        var localAdmin = ScopedClient("t98_ladm", StatsTidRoles.LocalAdmin, OrgEmpty);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteOrgAsync(localAdmin, OrgEmpty)).StatusCode);

        var hr = ScopedClient("t98_hr_del", StatsTidRoles.LocalHR, OrgEmpty);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteOrgAsync(hr, OrgEmpty)).StatusCode);

        // The org was NOT touched.
        Assert.True(await IsOrgActiveAsync(OrgEmpty));
    }

    /// <summary>(3b) A non-GlobalAdmin calling move → 403 (even scoped to the moved org).</summary>
    [Fact]
    public async Task Move_NonGlobalAdmin_Returns403()
    {
        var localAdmin = ScopedClient("t98_ladm_mv", StatsTidRoles.LocalAdmin, OrgEmp);
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveOrgAsync(localAdmin, OrgEmp, MaoB)).StatusCode);

        var hr = ScopedClient("t98_hr_mv", StatsTidRoles.LocalHR, OrgEmp);
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveOrgAsync(hr, OrgEmp, MaoB)).StatusCode);

        // Unmoved.
        var (path, parent) = await ReadOrgPathAndParentAsync(OrgEmp);
        Assert.Equal(OrgEmpPath, path);
        Assert.Equal(MaoA, parent);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (4) Move guards — MAO → 422; target not a MAO → 422; self / no-op → 400.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(4a) Moving a MAO → 422 (a MAO is a root; only Organisations move).</summary>
    [Fact]
    public async Task Move_Mao_Returns422()
    {
        var admin = GlobalAdminClient();
        var rsp = await MoveOrgAsync(admin, MaoA, MaoB);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    /// <summary>(4b) Moving an Organisation to a non-MAO target (another ORGANISATION) → 422.</summary>
    [Fact]
    public async Task Move_TargetNotMao_Returns422()
    {
        var admin = GlobalAdminClient();
        var rsp = await MoveOrgAsync(admin, OrgEmp, OrgEmpty); // OrgEmpty is an ORGANISATION, not a MAO
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    /// <summary>(4c) Self-parent (newParent == orgId) → 400.</summary>
    [Fact]
    public async Task Move_ToSelf_Returns400()
    {
        var admin = GlobalAdminClient();
        var rsp = await MoveOrgAsync(admin, OrgEmp, OrgEmp);
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
    }

    /// <summary>(4d) No-op (already under this parent: T98_ORG_EMP is already under MAO_A) → 400.</summary>
    [Fact]
    public async Task Move_NoOpSameParent_Returns400()
    {
        var admin = GlobalAdminClient();
        var rsp = await MoveOrgAsync(admin, OrgEmp, MaoA);
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (5) The aggregated tree — counts roll up; visibility-bounded.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(5a) The MAO employeeCount = Σ its Organisations' counts; an Organisation's employeeCount =
    /// its active users. (S103 / TASK-10305: the per-Organisation enheder nesting + taggedUserCount were
    /// retired with the legacy Enhed model — units return in S104+.)</summary>
    [Fact]
    public async Task Tree_CountsRollUp()
    {
        var admin = GlobalAdminClient();

        var tree = await GetTreeAsync(admin);

        var maoA = tree.EnumerateArray().First(m => m.GetProperty("orgId").GetString() == MaoA);
        var children = maoA.GetProperty("organisations").EnumerateArray().ToList();

        var orgEmp = children.First(o => o.GetProperty("orgId").GetString() == OrgEmp);
        Assert.Equal(1, orgEmp.GetProperty("employeeCount").GetInt64()); // one active user

        var orgEmpty = children.First(o => o.GetProperty("orgId").GetString() == OrgEmpty);
        Assert.Equal(0, orgEmpty.GetProperty("employeeCount").GetInt64()); // empty

        // MAO count = Σ visible children's counts (1 + 0 = 1).
        var sumChildren = children.Sum(o => o.GetProperty("employeeCount").GetInt64());
        Assert.Equal(sumChildren, maoA.GetProperty("employeeCount").GetInt64());
        Assert.Equal(1, maoA.GetProperty("employeeCount").GetInt64());
    }

    /// <summary>(5b) Visibility: a scoped HR (covering only T98_ORG_EMP) sees its MAO header + only the
    /// orgs it can reach (T98_ORG_EMP) — NOT the sibling T98_ORG_EMPTY it does not cover, and NOT MAO_B
    /// (childless from its view). A GlobalAdmin sees the full forest incl. MAO_B.</summary>
    [Fact]
    public async Task Tree_VisibilityBounded_ScopedHrSeesOnlyAccessibleOrgs()
    {
        var hr = ScopedClient("t98_hr_tree", StatsTidRoles.LocalHR, OrgEmp); // covers ONLY T98_ORG_EMP

        var hrTree = await GetTreeAsync(hr);
        var hrChildOrgIds = hrTree.EnumerateArray()
            .SelectMany(m => m.GetProperty("organisations").EnumerateArray())
            .Select(o => o.GetProperty("orgId").GetString())
            .ToList();
        var hrMaoIds = hrTree.EnumerateArray().Select(m => m.GetProperty("orgId").GetString()).ToList();

        Assert.Contains(OrgEmp, hrChildOrgIds);        // in-scope child
        Assert.DoesNotContain(OrgEmpty, hrChildOrgIds); // sibling NOT covered
        Assert.Contains(MaoA, hrMaoIds);                // the MAO header (has a visible child)
        Assert.DoesNotContain(MaoB, hrMaoIds);          // childless from the HR's view

        // GlobalAdmin sees the full forest (incl. both MAOs).
        var adminTree = await GetTreeAsync(GlobalAdminClient());
        var adminMaoIds = adminTree.EnumerateArray().Select(m => m.GetProperty("orgId").GetString()).ToList();
        Assert.Contains(MaoA, adminMaoIds);
        Assert.Contains(MaoB, adminMaoIds);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (6) Home-guard regression — NOT RED-on-old; pins the existing GetByIdAsync protection.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(6) After soft-deleting an EMPTY Organisation, a soft-deleted org can never become a
    /// user's home — BOTH a CREATE and a TRANSFER with that org as primaryOrgId are REJECTED. The
    /// enforcement point is the SAME is_active-filtered <c>OrganizationRepository.GetByIdAsync</c>
    /// (filters <c>is_active=TRUE</c> → null for a deleted org): the org-scope gate
    /// (<c>ValidateOrgAccessAsync</c>) resolves the target through it FIRST and, seeing null →
    /// "Organization not found", DENIES (403) before the dedicated home-guard's own
    /// GetByIdAsync→400 "Primary org not found" can fire. Either layer would reject; the scope gate
    /// wins ordering on both paths, so the ACTUAL observed code is 403 on BOTH (not the home-guard's
    /// 400 — asserting the real code per the task). Pins the protection (Step-0b BLOCKER B: it
    /// already exists — NOT RED-on-old).</summary>
    [Fact]
    public async Task SoftDeletedOrg_RejectsCreateAndTransferAsHome()
    {
        var admin = GlobalAdminClient();

        // Soft-delete the empty org.
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteOrgAsync(admin, OrgEmpty)).StatusCode);

        // CREATE onto the dead org → REJECTED. The actor's org-scope gate resolves the soft-deleted
        // target via the is_active-filtered GetByIdAsync (null → "Organization not found") and denies
        // → 403, ahead of the home-guard. The dead org never becomes a home.
        var create = await admin.PostAsJsonAsync("/api/admin/users", new
        {
            userId = "t98_home_new",
            username = "t98_home_new",
            password = "password123",
            displayName = "T98 Home New",
            primaryOrgId = OrgEmpty,
            agreementCode = "HK",
            okVersion = "OK24",
        });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        // TRANSFER the existing employee onto the dead org → REJECTED for the SAME reason (the new-org
        // ValidateOrgAccessAsync resolves the soft-deleted target → null → denied) → 403.
        var transfer = await TransferUserAsync(admin, EmpUser, OrgEmpty);
        Assert.Equal(HttpStatusCode.Forbidden, transfer.StatusCode);

        // Either way the employee was NOT moved onto the dead org.
        var (_, parent) = await ReadOrgPathAndParentAsync(OrgEmp);
        Assert.Equal(MaoA, parent);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT primary_org_id FROM users WHERE user_id = @u", conn);
        cmd.Parameters.AddWithValue("u", EmpUser);
        Assert.Equal(OrgEmp, (string?)await cmd.ExecuteScalarAsync());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (7) Events — OrganizationDeleted / OrganizationMoved land (+ the move delta).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(7a) A soft-delete emits an <c>OrganizationDeleted</c> event on the <c>org-{orgId}</c>
    /// stream (outbox).</summary>
    [Fact]
    public async Task SoftDelete_EmitsOrganizationDeletedEvent()
    {
        var admin = GlobalAdminClient();
        var before = await CountOutboxEventsAsync($"org-{OrgEmpty}", "OrganizationDeleted");

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteOrgAsync(admin, OrgEmpty)).StatusCode);

        Assert.True(await CountOutboxEventsAsync($"org-{OrgEmpty}", "OrganizationDeleted") > before,
            "An OrganizationDeleted event must be emitted on the soft-delete.");
    }

    /// <summary>(7b) A move emits an <c>OrganizationMoved</c> event (outbox) AND the audit-projection row
    /// carries the OLD+NEW parent + OLD+NEW materialized_path delta (for replay).</summary>
    [Fact]
    public async Task Move_EmitsOrganizationMovedEvent_WithOldAndNewParentAndPath()
    {
        var admin = GlobalAdminClient();
        var before = await CountOutboxEventsAsync($"org-{OrgEmp}", "OrganizationMoved");

        Assert.Equal(HttpStatusCode.OK, (await MoveOrgAsync(admin, OrgEmp, MaoB)).StatusCode);

        Assert.True(await CountOutboxEventsAsync($"org-{OrgEmp}", "OrganizationMoved") > before,
            "An OrganizationMoved event must be emitted on the move.");

        // The audit-projection row carries the full re-parent delta (old+new parent + old+new path).
        var details = await ReadLatestAuditDetailsAsync("OrganizationMoved", OrgEmp);
        Assert.Equal(MaoA, details.GetProperty("oldParentOrgId").GetString());
        Assert.Equal(MaoB, details.GetProperty("newParentOrgId").GetString());
        Assert.Equal(OrgEmpPath, details.GetProperty("oldMaterializedPath").GetString());
        Assert.Equal($"{MaoBPath}{OrgEmp}/", details.GetProperty("newMaterializedPath").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed / cleanup
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        // Two MAOs + two ORGANISATIONs under MAO_A.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version, is_active)
            VALUES
                (@maoA,     'T98 MAO A',     'MAO',          NULL,   @maoAPath,     'AC', 'OK24', TRUE),
                (@maoB,     'T98 MAO B',     'MAO',          NULL,   @maoBPath,     'AC', 'OK24', TRUE),
                (@orgEmp,   'T98 Org Emp',   'ORGANISATION', @maoA,  @orgEmpPath,   'AC', 'OK24', TRUE),
                (@orgEmpty, 'T98 Org Empty', 'ORGANISATION', @maoA,  @orgEmptyPath, 'AC', 'OK24', TRUE)
            ON CONFLICT (org_id) DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("maoA", MaoA);
            cmd.Parameters.AddWithValue("maoB", MaoB);
            cmd.Parameters.AddWithValue("orgEmp", OrgEmp);
            cmd.Parameters.AddWithValue("orgEmpty", OrgEmpty);
            cmd.Parameters.AddWithValue("maoAPath", MaoAPath);
            cmd.Parameters.AddWithValue("maoBPath", MaoBPath);
            cmd.Parameters.AddWithValue("orgEmpPath", OrgEmpPath);
            cmd.Parameters.AddWithValue("orgEmptyPath", OrgEmptyPath);
            await cmd.ExecuteNonQueryAsync();
        }

        // One active employee homed on T98_ORG_EMP (users + employee_profiles + user_agreement_codes).
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, EmpUser, OrgEmp, "AC", "OK24", ensureOrg: false);
    }

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        // Users created via the create-user test (and any transferred). S103 / TASK-10305: the legacy
        // enheder/user_enheder tables were dropped, so there is no tag table to clean up here.
        await ExecAsync(conn, "DELETE FROM approval_periods WHERE employee_id = ANY(@users) OR employee_id = 't98_home_new'");
        await ExecAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@users) OR manager_id = ANY(@users)");
        await ExecAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@users) OR employee_id = 't98_home_new'");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@users) OR user_id = 't98_home_new'");
        await ExecAsync(conn, "DELETE FROM users WHERE user_id = ANY(@users) OR user_id = 't98_home_new'");

        // Audit-projection rows referencing the test orgs (FK target_org_id → organizations) MUST go
        // before the orgs.
        await ExecAsync(conn, "DELETE FROM audit_projection WHERE target_org_id = ANY(@orgs)");
        await ExecAsync(conn, "DELETE FROM outbox_events WHERE stream_id = ANY(@streams)");
        await ExecAsync(conn, "DELETE FROM events WHERE stream_id = ANY(@streams)");
        await ExecAsync(conn, "DELETE FROM event_streams WHERE stream_id = ANY(@streams)");

        // Finally the orgs (children before parents — but all keyed; no org→org FK cascade issue
        // since users/enheder/audit already gone).
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

    private static async Task<HttpResponseMessage> MoveOrgAsync(HttpClient client, string orgId, string newParentOrgId)
        => await client.PutAsJsonAsync($"/api/admin/organizations/{orgId}/move", new { newParentOrgId });

    private async Task<List<string?>> GetRosterEmployeeIdsAsync(HttpClient client, string orgId)
    {
        var rsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{orgId}/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("employees").EnumerateArray()
            .Select(e => e.GetProperty("employeeId").GetString())
            .ToList();
    }

    private async Task<List<string?>> GetOrgListIdsAsync(HttpClient client)
    {
        var rsp = await client.GetAsync("/api/admin/organizations");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().Select(o => o.GetProperty("orgId").GetString()).ToList();
    }

    private async Task<JsonElement> GetTreeAsync(HttpClient client)
    {
        var rsp = await client.GetAsync("/api/admin/organizations/tree");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("tree");
    }

    private async Task<HttpResponseMessage> TransferUserAsync(HttpClient client, string userId, string newOrgId)
    {
        var getRsp = await client.GetAsync($"/api/admin/users/{userId}");
        getRsp.EnsureSuccessStatusCode();
        var version = getRsp.Headers.ETag!.Tag.Trim('"');
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new { primaryOrgId = newOrgId, effectiveFrom = today }),
        };
        req.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        return await client.SendAsync(req);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  DB reads
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<bool> IsOrgActiveAsync(string orgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT is_active FROM organizations WHERE org_id = @o", conn);
        cmd.Parameters.AddWithValue("o", orgId);
        var result = await cmd.ExecuteScalarAsync();
        return result is true;
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

    private async Task<int> CountOutboxEventsAsync(string streamId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = @t", conn);
        cmd.Parameters.AddWithValue("s", streamId);
        cmd.Parameters.AddWithValue("t", eventType);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<JsonElement> ReadLatestAuditDetailsAsync(string eventType, string targetOrgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT details
            FROM audit_projection
            WHERE event_type = @t AND target_org_id = @o
            ORDER BY occurred_at DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("t", eventType);
        cmd.Parameters.AddWithValue("o", targetOrgId);
        var json = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(json);
        return JsonSerializer.Deserialize<JsonElement>(json!);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Tokens / clients
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "t98_gadmin", name: "t98_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: MaoA,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A single-scope client at <paramref name="role"/> anchored at <paramref name="orgId"/>
    /// (ORG_ONLY, S93 flat role-scope) — covers exactly that Organisation, disjoint from others.</summary>
    private HttpClient ScopedClient(string actorId, string role, string orgId)
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(role, orgId, "ORG_ONLY") });
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
