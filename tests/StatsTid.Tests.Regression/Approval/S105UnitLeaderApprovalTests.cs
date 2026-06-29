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

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// SPRINT-105 / TASK-10504 (+ the TASK-10503 behavioural boundedness RED test) — Enhedsspor Phase 2,
/// ADR-038 D4/D5: the <b>unit-leader APPROVAL authority</b> suite. The keystone-evidence test set for the
/// FIRST time <c>unit_leaders</c> legitimately enters authority (<c>CanApprove</c> gains a secondary/peer
/// unit-leader path + that leader's vikar), STRICTLY bounded to <c>E.unit_id</c>'s own direct members.
///
/// <para><b>The keystone (D5 boundedness, RED-on-naive-subtree).</b> The fixture builds a 4-level unit tree
/// (Grandparent <c>direktion</c> → Parent <c>omrade</c> → Member-unit <c>kontor</c>, plus a Sibling
/// <c>kontor</c> under Parent). A leader designated on the GRANDPARENT, the PARENT, or a SIBLING unit holds
/// no <c>unit_leaders</c> row for the member's own unit → grants NO <c>CanApprove</c> over the member AND
/// appears in NONE of that leader's dashboard / team-overview reads. A naive ancestor/subtree implementation
/// (re-opening the S76/S85/S91 inheritance bug class) goes RED here. The DIRECT leader of the member's own
/// unit IS allowed — the positive control. Both ACTION authorization and dashboard/team-overview VISIBILITY
/// are covered, on a ≥2-level (grandparent) fixture.</para>
///
/// <para>Covered: secondary-leader approves a direct member (200, <c>approval_method = UNIT_LEADER</c>);
/// vikar-of-a-unit-leader approves (<c>UNIT_LEADER_VIKAR</c>) while a bad vikar (role floor) is denied;
/// see==act parity (dashboard + team-overview set == the act-able set; my-reports is edge-OR-unit-leader
/// only, NOT HR/Admin scope); dashboard negatives (Employee-role / inactive / expired-role unit leader →
/// sees nothing + cannot act); the in-lock race (a held <c>unit-org-</c> advisory + a winning
/// <c>UnitLeaderRemoved</c> denies the in-flight approve — no stale-authority approval, a
/// <c>pg_locks ⋈ pg_stat_activity</c> waiter barrier); cross-Organisation designation denied (the
/// same-Org re-check, defense-in-depth on a directly-planted row); orphan → in-scope HR/Admin; the HR/Admin
/// fallback unchanged; the S104 Step-7a follow-ups (delete-cascade inactive-member rehome; leaderless-unit
/// fallback).</para>
///
/// <para>Each [Fact] boots a FRESH Postgres testcontainer (init.sql + host seeders) so the demo tree exists
/// and the isolated <c>s105_*</c> fixtures are seeded fresh per test; the cleanup mirrors the S104 robust
/// org-scoped non-demo nuke (NOT fixed-id deletes). Endpoint-level via
/// <see cref="StatsTidWebApplicationFactory"/>; direct DB reads for the lock-waiter + audit assertions.
/// Idioms mirror <see cref="S94FlatApprovalTests"/> +
/// <see cref="StatsTid.Tests.Regression.Security.S104UnitManagementTests"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S105UnitLeaderApprovalTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // ── Organisations (init.sql seed): STY02 under MAO MIN01; STY05 under a DIFFERENT MAO (disjoint). ──
    private const string OrgA = "STY02";  // an ORGANISATION — the isolated unit tree + members live here
    private const string OrgB = "STY05";  // a DIFFERENT ORGANISATION — the cross-Org leader's home
    private const string Min01 = "MIN01"; // a MAO (GlobalAdmin token home)

    // ── The isolated 4-level unit tree under STY02 (disjoint from the demo 000000d0-… tree). ──
    private static readonly Guid UnitGrand  = Guid.Parse("51050000-0000-0000-0000-000000000001"); // direktion (top)
    private static readonly Guid UnitParent = Guid.Parse("51050000-0000-0000-0000-000000000002"); // omrade, child Grand
    private static readonly Guid UnitMember = Guid.Parse("51050000-0000-0000-0000-000000000003"); // kontor, child Parent — E's own unit
    private static readonly Guid UnitSib    = Guid.Parse("51050000-0000-0000-0000-000000000004"); // kontor, child Parent — sibling of Member
    private static readonly Guid UnitLeaderless = Guid.Parse("51050000-0000-0000-0000-000000000005"); // team (top), NO leaders
    private static readonly Guid UnitDeletable  = Guid.Parse("51050000-0000-0000-0000-000000000006"); // team (top), the delete-cascade fixture

    private static readonly Guid[] AllUnits =
        { UnitGrand, UnitParent, UnitMember, UnitSib, UnitLeaderless, UnitDeletable };

    // ── Users (STY02 unless noted; is_active TRUE unless noted). ──
    private const string Emp        = "s105_emp";        // unit Member, EMPLOYEE — the target; PRIMARY edge → PrimaryMgr
    private const string PrimaryMgr = "s105_pmgr";       // unit Member, LocalLeader — Emp's designated edge approver
    private const string DirectLdr  = "s105_direct";     // unit Member, LocalLeader — leader of Member (the SECONDARY unit-leader)
    private const string DirectLdr2 = "s105_direct2";    // unit Member, LocalLeader — leader of Member (host for the bad vikar)
    private const string GrandLdr   = "s105_grand";      // unit Grand,  LocalLeader — leader of Grand (boundedness)
    private const string ParentLdr  = "s105_parent";     // unit Parent, LocalLeader — leader of Parent (boundedness)
    private const string SibLdr     = "s105_sib";        // unit Sib,    LocalLeader — leader of Sib (boundedness)
    private const string VikarUsr   = "s105_vikar";      // unit Member, LocalLeader — active manager_vikar for DirectLdr
    private const string BadVikar   = "s105_badvikar";   // unit Member, EMPLOYEE   — active vikar for DirectLdr2 (role-floor negative)
    private const string EmpRoleLdr = "s105_emprole";    // unit Member, EMPLOYEE   — leader of Member (role-coupling negative)
    private const string InactiveLdr= "s105_inactive";   // unit Member, LocalLeader, is_active FALSE — leader of Member (negative)
    private const string ExpiredLdr = "s105_expired";    // unit Member, LocalLeader (expired role) — leader of Member (negative)
    private const string Hr         = "s105_hr";         // STY02, LocalHR — the HR/Admin fallback
    private const string Orphan     = "s105_orphan";     // unit NULL, EMPLOYEE, NO edge — orphan → HR
    private const string LeaderlessM= "s105_lm";         // unit Leaderless, EMPLOYEE; PRIMARY edge → PrimaryMgr
    private const string CrossLdr   = "s105_cross";      // STY05, LocalLeader — a PLANTED leader of Member (cross-Org defense-in-depth)
    private const string InactMember= "s105_inactmember";// unit Deletable, EMPLOYEE, is_active FALSE — the delete-cascade rehome probe

    private static readonly string[] AllUsers =
    {
        Emp, PrimaryMgr, DirectLdr, DirectLdr2, GrandLdr, ParentLdr, SibLdr, VikarUsr, BadVikar,
        EmpRoleLdr, InactiveLdr, ExpiredLdr, Hr, Orphan, LeaderlessM, CrossLdr, InactMember,
    };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot the host seeders (MAO→ORGANISATION tree + demo units + configs)

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
        // (1) The isolated 4-level unit tree under STY02 + the two extra top-level units.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES
                (@grand,  @orgA, NULL,    'direktion', 'S105 Grand'),
                (@parent, @orgA, @grand,  'omrade',    'S105 Parent'),
                (@member, @orgA, @parent, 'kontor',    'S105 Member Unit'),
                (@sib,    @orgA, @parent, 'kontor',    'S105 Sibling Unit'),
                (@lu,     @orgA, NULL,    'team',      'S105 Leaderless Unit'),
                (@du,     @orgA, NULL,    'team',      'S105 Deletable Unit')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("grand", UnitGrand);
            cmd.Parameters.AddWithValue("parent", UnitParent);
            cmd.Parameters.AddWithValue("member", UnitMember);
            cmd.Parameters.AddWithValue("sib", UnitSib);
            cmd.Parameters.AddWithValue("lu", UnitLeaderless);
            cmd.Parameters.AddWithValue("du", UnitDeletable);
            cmd.Parameters.AddWithValue("orgA", OrgA);
            await cmd.ExecuteNonQueryAsync();
        }

        // (2) Users with their single structural unit. primary_org_id == the unit's Organisation (the
        //     derived anchor); CrossLdr homes on STY05 (unit_id NULL). Inactive flags as noted.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active)
            VALUES
                (@emp,      @emp,      '$2a$11$fake', 'S105 Emp',      's105_emp@test.dk',      @orgA, @member, 'HK','OK24', TRUE),
                (@pmgr,     @pmgr,     '$2a$11$fake', 'S105 PMgr',     's105_pmgr@test.dk',     @orgA, @member, 'HK','OK24', TRUE),
                (@direct,   @direct,   '$2a$11$fake', 'S105 Direct',   's105_direct@test.dk',   @orgA, @member, 'HK','OK24', TRUE),
                (@direct2,  @direct2,  '$2a$11$fake', 'S105 Direct2',  's105_direct2@test.dk',  @orgA, @member, 'HK','OK24', TRUE),
                (@grand,    @grand,    '$2a$11$fake', 'S105 Grand',    's105_grand@test.dk',    @orgA, @ugrand, 'HK','OK24', TRUE),
                (@parent,   @parent,   '$2a$11$fake', 'S105 Parent',   's105_parent@test.dk',   @orgA, @uparent,'HK','OK24', TRUE),
                (@sib,      @sib,      '$2a$11$fake', 'S105 Sib',      's105_sib@test.dk',      @orgA, @usib,   'HK','OK24', TRUE),
                (@vikar,    @vikar,    '$2a$11$fake', 'S105 Vikar',    's105_vikar@test.dk',    @orgA, @member, 'HK','OK24', TRUE),
                (@badvikar, @badvikar, '$2a$11$fake', 'S105 BadVikar', 's105_badvikar@test.dk', @orgA, @member, 'HK','OK24', TRUE),
                (@emprole,  @emprole,  '$2a$11$fake', 'S105 EmpRole',  's105_emprole@test.dk',  @orgA, @member, 'HK','OK24', TRUE),
                (@inactive, @inactive, '$2a$11$fake', 'S105 Inactive', 's105_inactive@test.dk', @orgA, @member, 'HK','OK24', FALSE),
                (@expired,  @expired,  '$2a$11$fake', 'S105 Expired',  's105_expired@test.dk',  @orgA, @member, 'HK','OK24', TRUE),
                (@hr,       @hr,       '$2a$11$fake', 'S105 Hr',       's105_hr@test.dk',       @orgA, NULL,    'AC','OK24', TRUE),
                (@orphan,   @orphan,   '$2a$11$fake', 'S105 Orphan',   's105_orphan@test.dk',   @orgA, NULL,    'HK','OK24', TRUE),
                (@lm,       @lm,       '$2a$11$fake', 'S105 LM',       's105_lm@test.dk',       @orgA, @lu,     'HK','OK24', TRUE),
                (@cross,    @cross,    '$2a$11$fake', 'S105 Cross',    's105_cross@test.dk',    @orgB, NULL,    'AC','OK24', TRUE),
                (@inactm,   @inactm,   '$2a$11$fake', 'S105 InactM',   's105_inactm@test.dk',   @orgA, @du,     'HK','OK24', FALSE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            cmd.Parameters.AddWithValue("orgA", OrgA);
            cmd.Parameters.AddWithValue("orgB", OrgB);
            cmd.Parameters.AddWithValue("member", UnitMember);
            cmd.Parameters.AddWithValue("ugrand", UnitGrand);
            cmd.Parameters.AddWithValue("uparent", UnitParent);
            cmd.Parameters.AddWithValue("usib", UnitSib);
            cmd.Parameters.AddWithValue("lu", UnitLeaderless);
            cmd.Parameters.AddWithValue("du", UnitDeletable);
            await cmd.ExecuteNonQueryAsync();
        }

        // (3) Role assignments. LOCAL_LEADER (hierarchy 4 = LeaderOrAbove) for the leaders/vikar;
        //     LOCAL_HR for Hr; EMPLOYEE for the plain members + the role-coupling negatives. ExpiredLdr's
        //     LOCAL_LEADER row is EXPIRED (expires_at in the past) → below the active-role floor. CrossLdr
        //     is LOCAL_LEADER over STY05 (org irrelevant to the floor; the same-Org re-check denies it).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by, expires_at) VALUES
                (@pmgr,     'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@direct,   'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@direct2,  'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@grand,    'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@parent,   'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@sib,      'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@vikar,    'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@expired,  'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST', NOW() - INTERVAL '1 day'),
                (@hr,       'LOCAL_HR',     @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@cross,    'LOCAL_LEADER', @orgB, 'ORG_ONLY', 'TEST', NULL),
                (@emp,      'EMPLOYEE',     @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@badvikar, 'EMPLOYEE',     @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@emprole,  'EMPLOYEE',     @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@inactive, 'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@orphan,   'EMPLOYEE',     @orgA, 'ORG_ONLY', 'TEST', NULL),
                (@lm,       'EMPLOYEE',     @orgA, 'ORG_ONLY', 'TEST', NULL)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            cmd.Parameters.AddWithValue("orgA", OrgA);
            cmd.Parameters.AddWithValue("orgB", OrgB);
            await cmd.ExecuteNonQueryAsync();
        }

        // (4) Unit-leader designations (the member-invariant holds for all but CrossLdr, which is a
        //     DIRECTLY-PLANTED cross-Org row — the defense-in-depth same-Org probe, bypassing the
        //     endpoint's member-invariant exactly as the edge authorizer's planted-vikar test does).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO unit_leaders (unit_id, user_id) VALUES
                (@memberUnit, @directLdr),
                (@memberUnit, @direct2Ldr),
                (@memberUnit, @emproleLdr),
                (@memberUnit, @inactiveLdr),
                (@memberUnit, @expiredLdr),
                (@memberUnit, @crossLdr),
                (@grandUnit,  @grandLdr),
                (@parentUnit, @parentLdr),
                (@sibUnit,    @sibLdr)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("memberUnit", UnitMember);
            cmd.Parameters.AddWithValue("grandUnit", UnitGrand);
            cmd.Parameters.AddWithValue("parentUnit", UnitParent);
            cmd.Parameters.AddWithValue("sibUnit", UnitSib);
            cmd.Parameters.AddWithValue("directLdr", DirectLdr);
            cmd.Parameters.AddWithValue("direct2Ldr", DirectLdr2);
            cmd.Parameters.AddWithValue("emproleLdr", EmpRoleLdr);
            cmd.Parameters.AddWithValue("inactiveLdr", InactiveLdr);
            cmd.Parameters.AddWithValue("expiredLdr", ExpiredLdr);
            cmd.Parameters.AddWithValue("crossLdr", CrossLdr);
            cmd.Parameters.AddWithValue("grandLdr", GrandLdr);
            cmd.Parameters.AddWithValue("parentLdr", ParentLdr);
            cmd.Parameters.AddWithValue("sibLdr", SibLdr);
            await cmd.ExecuteNonQueryAsync();
        }

        // (5) Vikars: VikarUsr is the ACTIVE stand-in for DirectLdr (→ UNIT_LEADER_VIKAR); BadVikar is an
        //     active stand-in for DirectLdr2 but an EMPLOYEE-role user (→ denied by the role floor).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO manager_vikar (absent_approver_id, vikar_user_id, until_date, reason, organisation_id, created_by) VALUES
                (@direct,  @vikar,    @future, 'FERIE', @orgA, 'TEST'),
                (@direct2, @badvikar, @future, 'FERIE', @orgA, 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("direct", DirectLdr);
            cmd.Parameters.AddWithValue("direct2", DirectLdr2);
            cmd.Parameters.AddWithValue("vikar", VikarUsr);
            cmd.Parameters.AddWithValue("badvikar", BadVikar);
            cmd.Parameters.AddWithValue("future", new DateOnly(2099, 12, 31));
            cmd.Parameters.AddWithValue("orgA", OrgA);
            await cmd.ExecuteNonQueryAsync();
        }

        // (6) Reporting edges (same Organisation STY02): Emp + LeaderlessM both report PRIMARY to
        //     PrimaryMgr → PrimaryMgr is the designated EDGE approver. Orphan has NO edge.
        var rlRepo = new ReportingLineRepository(_dbFactory);
        await rlRepo.AssignAsync(null, MakeLine(Emp, PrimaryMgr));
        await rlRepo.AssignAsync(null, MakeLine(LeaderlessM, PrimaryMgr));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("pmgr", PrimaryMgr);
        cmd.Parameters.AddWithValue("direct", DirectLdr);
        cmd.Parameters.AddWithValue("direct2", DirectLdr2);
        cmd.Parameters.AddWithValue("grand", GrandLdr);
        cmd.Parameters.AddWithValue("parent", ParentLdr);
        cmd.Parameters.AddWithValue("sib", SibLdr);
        cmd.Parameters.AddWithValue("vikar", VikarUsr);
        cmd.Parameters.AddWithValue("badvikar", BadVikar);
        cmd.Parameters.AddWithValue("emprole", EmpRoleLdr);
        cmd.Parameters.AddWithValue("inactive", InactiveLdr);
        cmd.Parameters.AddWithValue("expired", ExpiredLdr);
        cmd.Parameters.AddWithValue("hr", Hr);
        cmd.Parameters.AddWithValue("orphan", Orphan);
        cmd.Parameters.AddWithValue("lm", LeaderlessM);
        cmd.Parameters.AddWithValue("cross", CrossLdr);
        cmd.Parameters.AddWithValue("inactm", InactMember);
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
        await ExecUsersAsync(conn,
            "DELETE FROM approval_audit WHERE actor_id = ANY(@ids) OR period_id IN (SELECT period_id FROM approval_periods WHERE employee_id = ANY(@ids))");
        await ExecUsersAsync(conn, "DELETE FROM approval_periods WHERE employee_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM audit_projection WHERE actor_id = ANY(@ids) OR target_resource_id = ANY(@ids)");
        // Unit-event audit rows (UnitDeleted/Moved/LeaderRemoved) carry the unit id as target_resource.
        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM audit_projection WHERE target_resource_id = ANY(@uids)", conn))
        {
            cmd.Parameters.AddWithValue("uids", AllUnits.Select(u => u.ToString()).ToArray());
            await cmd.ExecuteNonQueryAsync();
        }
        await ExecUsersAsync(conn, "DELETE FROM manager_vikar WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM reporting_line_audit WHERE reporting_line_id IN (SELECT reporting_line_id FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids))");
        await ExecUsersAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM unit_leaders WHERE user_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM users WHERE user_id = ANY(@ids)");

        // Drop EVERY non-demo unit in the test Organisations (the isolated s105 fixtures AND any units a
        // test created via the API) — the S104 robust org-scoped nuke, NOT fixed-id deletes: detach any
        // straggler members + leaders + break the parent self-FK first, then delete. Fresh container per
        // test → the demo `000000d0-…` tree is the only thing that must survive.
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
    //  (1) THE KEYSTONE — ≥2-level boundedness, ACTION authorization (RED-on-naive-subtree).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The D5 keystone. A leader designated on the GRANDPARENT (UnitGrand), the PARENT
    /// (UnitParent), or a SIBLING (UnitSib) of Emp's own unit holds no <c>unit_leaders</c> row for
    /// UnitMember → is DENIED the approve (403) and Emp's period stays SUBMITTED. The DIRECT leader of
    /// UnitMember (the positive control) approves (200) and the approval is classified <c>UNIT_LEADER</c>.
    /// RED on any naive ancestor/subtree implementation (which would let the grandparent/parent leader
    /// through — the S76/S85/S91 inheritance bug class).</summary>
    [Fact]
    public async Task Boundedness_GrandparentParentSiblingLeader_DeniedApprove_DirectLeaderAllowed()
    {
        // Grandparent-unit leader → DENIED (the ≥2-level pin).
        var pGrand = await InsertSubmittedPeriodAsync(Emp);
        var grandRsp = await LeaderClient(GrandLdr).PostAsync($"/api/approval/{pGrand}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, grandRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pGrand));

        // Parent-unit leader → DENIED.
        var parentRsp = await LeaderClient(ParentLdr).PostAsync($"/api/approval/{pGrand}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, parentRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pGrand));

        // Sibling-unit leader → DENIED.
        var sibRsp = await LeaderClient(SibLdr).PostAsync($"/api/approval/{pGrand}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, sibRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pGrand));

        // Positive control: the DIRECT leader of Emp's own unit approves → 200, UNIT_LEADER.
        var directRsp = await LeaderClient(DirectLdr).PostAsync($"/api/approval/{pGrand}/approve", null);
        Assert.Equal(HttpStatusCode.OK, directRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(pGrand));
        Assert.Equal("UNIT_LEADER", await ReadColumnAsync(pGrand, "approval_method"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) THE KEYSTONE — ≥2-level boundedness, dashboard / team-overview VISIBILITY.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The see-side mirror of the keystone. A grandparent/parent/sibling-unit leader sees Emp in
    /// NEITHER their my-reports dashboard NOR their team-overview; the DIRECT leader sees Emp in BOTH. RED
    /// on a naive ancestor walk (which would surface the descendant-unit member to the parent leader).</summary>
    [Fact]
    public async Task Boundedness_DashboardVisibility_NonDirectLeadersSeeNothing_DirectLeaderSeesMember()
    {
        await InsertSubmittedPeriodAsync(Emp);

        foreach (var nonDirect in new[] { GrandLdr, ParentLdr, SibLdr })
        {
            var pending = await GetEmployeeIdsAsync(LeaderClient(nonDirect), "/api/approval/pending?my-reports=true");
            Assert.DoesNotContain(Emp, pending);
            var overview = await GetEmployeeIdsAsync(LeaderClient(nonDirect), "/api/approval/team-overview?year=2026&month=5");
            Assert.DoesNotContain(Emp, overview);
        }

        // The DIRECT leader sees Emp in BOTH reads.
        var directPending = await GetEmployeeIdsAsync(LeaderClient(DirectLdr), "/api/approval/pending?my-reports=true");
        Assert.Contains(Emp, directPending);
        var directOverview = await GetEmployeeIdsAsync(LeaderClient(DirectLdr), "/api/approval/team-overview?year=2026&month=5");
        Assert.Contains(Emp, directOverview);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) Secondary unit-leader approves / rejects a direct member → UNIT_LEADER.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A secondary/peer unit-leader (DirectLdr leads Emp's unit; Emp's PRIMARY edge points at a
    /// DIFFERENT manager, PrimaryMgr) approves Emp → 200, <c>approval_method = UNIT_LEADER</c> (NOT the
    /// edge classification, NOT the misleading ORG_SCOPE_FALLBACK). The reject mirror is also admitted.</summary>
    [Fact]
    public async Task SecondaryUnitLeader_ApprovesAndRejects_DirectMember_RecordsUnitLeader()
    {
        var pApprove = await InsertSubmittedPeriodAsync(Emp);
        var approveRsp = await LeaderClient(DirectLdr).PostAsync($"/api/approval/{pApprove}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(pApprove));
        Assert.Equal("UNIT_LEADER", await ReadColumnAsync(pApprove, "approval_method"));

        var pReject = await InsertSubmittedPeriodAsync(Emp, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var rejectRsp = await LeaderClient(DirectLdr).PostAsJsonAsync($"/api/approval/{pReject}/reject", new { reason = "needs fixing" });
        Assert.Equal(HttpStatusCode.OK, rejectRsp.StatusCode);
        Assert.Equal("REJECTED", await ReadStatusAsync(pReject));
        Assert.Equal("UNIT_LEADER", await ReadColumnAsync(pReject, "approval_method"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3b) SEGREGATION OF DUTIES — a unit leader CANNOT approve/see their OWN period.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>S105 Step-7a BLOCKER (self-approval). A unit leader IS a member of the unit they lead
    /// (the D3 member-invariant), so without the self-exclusion the `unit_leaders(E.unit_id)` edge would
    /// match DirectLdr as the approver of DirectLdr's OWN period. Assert it is DENIED (403, no
    /// self-approval) AND that DirectLdr's own period does NOT appear in DirectLdr's my-reports /
    /// team-overview via the unit-leader edge (a leader's own period routes to their primary edge /
    /// HR-Admin, never to themselves). RED on the pre-fix code (which 200'd the self-approve).</summary>
    [Fact]
    public async Task SelfApproval_UnitLeader_CannotApproveOrSeeOwnPeriod()
    {
        var pSelf = await InsertSubmittedPeriodAsync(DirectLdr);

        var selfRsp = await LeaderClient(DirectLdr).PostAsync($"/api/approval/{pSelf}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, selfRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pSelf));

        var pending = await GetEmployeeIdsAsync(LeaderClient(DirectLdr), "/api/approval/pending?my-reports=true");
        Assert.DoesNotContain(DirectLdr, pending);
        var overview = await GetEmployeeIdsAsync(LeaderClient(DirectLdr), "/api/approval/team-overview?year=2026&month=5");
        Assert.DoesNotContain(DirectLdr, overview);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (4) Vikar of a unit-leader → UNIT_LEADER_VIKAR; a bad vikar (role floor) → denied.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>An ACTIVE <c>manager_vikar</c> stand-in (VikarUsr) for a unit-leader (DirectLdr) of Emp's
    /// own unit approves Emp → 200, <c>UNIT_LEADER_VIKAR</c> (path-3). A vikar (BadVikar) of a unit-leader
    /// (DirectLdr2) of the SAME unit but holding only an EMPLOYEE role is below the LeaderOrAbove floor →
    /// DENIED (403), proving the vikar path applies the full role floor.</summary>
    [Fact]
    public async Task VikarOfUnitLeader_Approves_RecordsUnitLeaderVikar_BadVikarDenied()
    {
        var pVikar = await InsertSubmittedPeriodAsync(Emp);
        var vikarRsp = await LeaderClient(VikarUsr).PostAsync($"/api/approval/{pVikar}/approve", null);
        Assert.Equal(HttpStatusCode.OK, vikarRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(pVikar));
        Assert.Equal("UNIT_LEADER_VIKAR", await ReadColumnAsync(pVikar, "approval_method"));

        // BadVikar is an EMPLOYEE-role vikar → below the floor → denied (and stays SUBMITTED). It is sent
        // with an EMPLOYEE token, which the LeaderOrAbove policy rejects (403) — either way, no approval.
        var pBad = await InsertSubmittedPeriodAsync(Emp, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var badRsp = await EmployeeClient(BadVikar).PostAsync($"/api/approval/{pBad}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, badRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pBad));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (5) see == act parity for a secondary unit-leader (+ my-reports is NOT HR/Admin scope).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>For the secondary unit-leader DirectLdr, the my-reports dashboard + team-overview set EQUALS
    /// the set they can ACT on (every UnitMember active member; NOTHING from sibling/parent/grandparent/
    /// leaderless units). And the my-reports reads are edge-OR-unit-leader ONLY, NOT HR/Admin scope: Hr —
    /// who CAN act on Emp via the org-scope fallback — sees Emp in NEITHER my-reports report (Hr holds no
    /// edge and leads no unit), proving the my-reports surface does not expand to the HR/Admin scope.</summary>
    [Fact]
    public async Task SeeEqualsAct_SecondaryUnitLeader_MyReportsIsEdgeOrUnitLeaderOnly()
    {
        await InsertSubmittedPeriodAsync(Emp);

        // SEE: DirectLdr's team-overview = the active members of UnitMember (Emp + the peer leaders),
        //      and excludes the sibling/parent/grandparent/leaderless members.
        var overview = await GetEmployeeIdsAsync(LeaderClient(DirectLdr), "/api/approval/team-overview?year=2026&month=5");
        Assert.Contains(Emp, overview);
        Assert.Contains(DirectLdr2, overview); // a peer member of the same unit
        Assert.DoesNotContain(GrandLdr, overview);
        Assert.DoesNotContain(ParentLdr, overview);
        Assert.DoesNotContain(SibLdr, overview);
        Assert.DoesNotContain(LeaderlessM, overview);
        // The inactive member is filtered out of the candidate superset (is_active = TRUE bound).
        Assert.DoesNotContain(InactiveLdr, overview);

        // ACT: every employee DirectLdr SEES, DirectLdr can ACT on (see == act). Probe Emp + a peer.
        foreach (var seen in new[] { Emp, DirectLdr2 })
        {
            var p = await InsertSubmittedPeriodAsync(seen, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
            var rsp = await LeaderClient(DirectLdr).PostAsync($"/api/approval/{p}/approve", null);
            Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        }

        // my-reports is edge-OR-unit-leader ONLY (NOT HR/Admin scope): Hr can ACT on Emp (org-scope
        // fallback) yet sees Emp in NEITHER my-reports report.
        var hrPending = await GetEmployeeIdsAsync(AdminRoleClient(StatsTidRoles.LocalHR, Hr), "/api/approval/pending?my-reports=true");
        Assert.DoesNotContain(Emp, hrPending);
        var hrByMonth = await GetEmployeeIdsAsync(AdminRoleClient(StatsTidRoles.LocalHR, Hr), "/api/approval/by-month?year=2026&month=5&my-reports=true");
        Assert.DoesNotContain(Emp, hrByMonth);
        // ...but Hr CAN act (the fallback) — proving see(my-reports) ⊊ act for HR (no drift for the unit-leader).
        var pHr = await InsertSubmittedPeriodAsync(Emp, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        var hrActRsp = await AdminRoleClient(StatsTidRoles.LocalHR, Hr).PostAsync($"/api/approval/{pHr}/approve", null);
        Assert.Equal(HttpStatusCode.OK, hrActRsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (6) Dashboard negatives — Employee-role / inactive / expired-role unit leader.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A bare <c>unit_leaders</c> row never grants see-or-act without the floors. An Employee-role
    /// leader (EmpRoleLdr — role-coupling), an INACTIVE leader (InactiveLdr), and an EXPIRED-role leader
    /// (ExpiredLdr) — all designated leaders of Emp's OWN unit — each see Emp in NO read AND are DENIED the
    /// approve. (The Employee-role / inactive actors are rejected by the LeaderOrAbove policy; the expired
    /// actor passes the policy but is denied by the active-role floor in the authorizer.)</summary>
    [Fact]
    public async Task DashboardNegatives_EmployeeRole_Inactive_ExpiredLeader_SeeNothing_CannotAct()
    {
        // VISIBILITY: the expired-role leader carries a LocalLeader JWT (passes the policy) but sees
        // nothing (below the active-role floor). My-reports + team-overview exclude Emp.
        var expiredPending = await GetEmployeeIdsAsync(LeaderClient(ExpiredLdr), "/api/approval/pending?my-reports=true");
        Assert.DoesNotContain(Emp, expiredPending);
        var expiredOverview = await GetEmployeeIdsAsync(LeaderClient(ExpiredLdr), "/api/approval/team-overview?year=2026&month=5");
        Assert.DoesNotContain(Emp, expiredOverview);

        // ACTION: each negative is denied the approve; Emp's period stays SUBMITTED.
        var pEmpRole = await InsertSubmittedPeriodAsync(Emp);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await EmployeeClient(EmpRoleLdr).PostAsync($"/api/approval/{pEmpRole}/approve", null)).StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pEmpRole));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await LeaderClient(ExpiredLdr).PostAsync($"/api/approval/{pEmpRole}/approve", null)).StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pEmpRole));

        // The inactive leader: even with a LocalLeader token the authorizer's active-user floor denies it.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await LeaderClient(InactiveLdr).PostAsync($"/api/approval/{pEmpRole}/approve", null)).StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pEmpRole));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (7) The in-lock race — a winning UnitLeaderRemoved denies the in-flight approve.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The S105 BLOCKER-fix race (P4/P7). A concurrent <c>UnitLeaderRemoved</c> on the employee's
    /// unit serializes on the employee's <c>unit-org-</c> advisory (a DIFFERENT key from the approve's
    /// <c>reporting-org-</c>). We hold the STY02 <c>unit-org-</c> advisory on a side connection AND delete
    /// the <c>unit_leaders(UnitMember, DirectLdr)</c> row in that same (uncommitted) tx, then fire
    /// DirectLdr's approve of Emp — which (admitted pre-tx as a unit-leader) acquires <c>reporting-org-</c>
    /// then BLOCKS on the held <c>unit-org-</c> (a <c>pg_locks ⋈ pg_stat_activity</c> waiter barrier proves
    /// it parked on the lock). When we COMMIT (the revoke WINS, advisory releases), the approve's in-lock
    /// re-eval observes the frozen committed state — the leadership is GONE — and DENIES (403). No
    /// stale-authority approval: Emp's period stays SUBMITTED.</summary>
    [Fact]
    public async Task InLockRace_WinningUnitLeaderRemoval_DeniesInFlightApprove()
    {
        var periodId = await InsertSubmittedPeriodAsync(Emp);

        // Side tx: hold the STY02 unit-org advisory AND delete DirectLdr's leadership of UnitMember
        // (uncommitted — not yet visible; the approve's pre-tx check still sees the row).
        var (holdConn, holdTx) = await AcquireUnitOrgLockAndRevokeLeaderAsync(OrgA, UnitMember, DirectLdr);
        var committed = false;
        try
        {
            var approveTask = LeaderClient(DirectLdr).PostAsync($"/api/approval/{periodId}/approve", null);

            Assert.True(await WaitForUnitOrgLockWaiterAsync(OrgA),
                "No backend was observed WAITING on the STY02 unit-org advisory — the approve did not serialize on the unit-org lock (the BLOCKER-fix advisory is missing).");
            Assert.False(await Task.WhenAny(approveTask, Task.Delay(500)) == approveTask,
                "The approve completed while the unit-org advisory was held — it did not serialize against the concurrent UnitLeaderRemoved.");

            // The revoke WINS: commit the delete + release the advisory.
            await holdTx.CommitAsync();
            await holdConn.DisposeAsync();
            committed = true;

            var rsp = await approveTask;
            Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode); // stale authority denied
            Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));
            Assert.Equal(0, await CountLeaderRowAsync(UnitMember, DirectLdr)); // the revoke really committed
        }
        finally
        {
            if (!committed) { await holdTx.RollbackAsync(); await holdConn.DisposeAsync(); }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (8) Cross-Organisation unit-leader designation → denied (the same-Org re-check).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A DIRECTLY-PLANTED cross-Organisation <c>unit_leaders(UnitMember, CrossLdr)</c> row (CrossLdr
    /// homes on STY05, Emp on STY02) cannot approve Emp: the authorizer's same-Organisation re-check denies
    /// it even though the membership row exists and CrossLdr is an active LeaderOrAbove — the structural
    /// cross-styrelse bound (D12), defense-in-depth against a planted row. → 403, period SUBMITTED.</summary>
    [Fact]
    public async Task CrossOrgUnitLeaderDesignation_CannotApprove()
    {
        var periodId = await InsertSubmittedPeriodAsync(Emp);
        var rsp = await LeaderClient(CrossLdr, OrgB).PostAsync($"/api/approval/{periodId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (9) Orphan (no edge, no unit leader) → in-scope HR/Admin; denied to a unit leader.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>An orphan (Orphan: <c>unit_id</c> NULL, NO reporting edge) routes to the HR/Admin fallback:
    /// an in-scope LocalHR approves it (200), while a unit-leader of a DIFFERENT unit (DirectLdr leads
    /// UnitMember, not the orphan's null unit) is DENIED (403) — the NULL-unit case grants no unit-leader
    /// path.</summary>
    [Fact]
    public async Task Orphan_NoEdgeNoUnitLeader_RoutesToHr_DeniedToUnitLeader()
    {
        var pLeader = await InsertSubmittedPeriodAsync(Orphan);
        var leaderRsp = await LeaderClient(DirectLdr).PostAsync($"/api/approval/{pLeader}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, leaderRsp.StatusCode);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pLeader));

        var pHr = await InsertSubmittedPeriodAsync(Orphan, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var hrRsp = await AdminRoleClient(StatsTidRoles.LocalHR, Hr).PostAsync($"/api/approval/{pHr}/approve", null);
        Assert.Equal(HttpStatusCode.OK, hrRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(pHr));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (10) The HR/Admin fallback + the edge path are UNCHANGED (a regression guard).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>S94 regression: an in-scope LocalHR approves Emp with NO edge → 200, ORG_SCOPE_FALLBACK
    /// (the unit-leader path did not steal the HR fallback's classification); the designated edge manager
    /// (PrimaryMgr) still approves via the edge → 200, DESIGNATED_MANAGER.</summary>
    [Fact]
    public async Task HrAdminFallback_AndEdge_Unchanged()
    {
        var pHr = await InsertSubmittedPeriodAsync(Emp);
        var hrRsp = await AdminRoleClient(StatsTidRoles.LocalHR, Hr).PostAsync($"/api/approval/{pHr}/approve", null);
        Assert.Equal(HttpStatusCode.OK, hrRsp.StatusCode);
        Assert.Equal("ORG_SCOPE_FALLBACK", await ReadColumnAsync(pHr, "approval_method"));

        var pEdge = await InsertSubmittedPeriodAsync(Emp, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var edgeRsp = await LeaderClient(PrimaryMgr).PostAsync($"/api/approval/{pEdge}/approve", null);
        Assert.Equal(HttpStatusCode.OK, edgeRsp.StatusCode);
        Assert.Equal("DESIGNATED_MANAGER", await ReadColumnAsync(pEdge, "approval_method"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (11) S104 follow-up — leaderless-unit fallback (no dead end).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>S104 Step-7a follow-up: a member of a unit with NO <c>unit_leaders</c> (LeaderlessM in
    /// UnitLeaderless) is not stranded — the unit-leader path yields nothing, so approval routes via the
    /// primary edge / HR-Admin. An in-scope LocalHR approves it (200) and the designated edge manager
    /// (PrimaryMgr) also approves a member-unit-less period (200) — no dead end.</summary>
    [Fact]
    public async Task LeaderlessUnit_ApprovalRoutesToHrAndEdge_NoDeadEnd()
    {
        var pHr = await InsertSubmittedPeriodAsync(LeaderlessM);
        var hrRsp = await AdminRoleClient(StatsTidRoles.LocalHR, Hr).PostAsync($"/api/approval/{pHr}/approve", null);
        Assert.Equal(HttpStatusCode.OK, hrRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(pHr));

        var pEdge = await InsertSubmittedPeriodAsync(LeaderlessM, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var edgeRsp = await LeaderClient(PrimaryMgr).PostAsync($"/api/approval/{pEdge}/approve", null);
        Assert.Equal(HttpStatusCode.OK, edgeRsp.StatusCode);
        Assert.Equal("DESIGNATED_MANAGER", await ReadColumnAsync(pEdge, "approval_method"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (12) S104 follow-up — delete-cascade re-homes an INACTIVE member (no dangling pointer).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>S104 Step-7a follow-up: deleting a unit re-homes EVERY direct member UP, including INACTIVE
    /// ones (the rehome UPDATE has no <c>is_active</c> filter). UnitDeletable holds an inactive member
    /// (InactMember); after the GlobalAdmin soft-deletes the unit, the inactive member's <c>unit_id</c> is
    /// re-homed to the deleted unit's parent (NULL here — top-level), so a later reactivation can never land
    /// the member back into a now-deleted unit. <c>primary_org_id</c> is unchanged.</summary>
    [Fact]
    public async Task Delete_UnitWithInactiveMember_RehomesInactiveMemberUp_PreventsReactivationIntoDeletedUnit()
    {
        Assert.Equal(UnitDeletable, await SelectUserUnitAsync(InactMember)); // precondition
        var version = await SelectUnitVersionAsync(UnitDeletable);

        var rsp = await SendAsync(GlobalAdminClient(), HttpMethod.Delete, $"/api/admin/units/{UnitDeletable}",
            body: null, ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);

        Assert.True(await IsUnitSoftDeletedAsync(UnitDeletable));
        // The inactive member is re-homed UP to the deleted unit's parent (NULL) — NOT left pointing at
        // the deleted unit (which would risk reactivation into a deleted unit).
        Assert.Null(await SelectUserUnitAsync(InactMember));
        Assert.Equal(OrgA, await SelectUserPrimaryOrgAsync(InactMember)); // attribution unchanged
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — clients / tokens
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient LeaderClient(string userId, string scopeOrg = OrgA)
    {
        var client = _factory.CreateClient();
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalLeader, scopeOrg, "ORG_ONLY") };
        var token = NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalLeader,
            agreementCode: "HK", orgId: scopeOrg, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient EmployeeClient(string userId, string scopeOrg = OrgA)
    {
        var client = _factory.CreateClient();
        var scopes = new[] { new RoleScope(StatsTidRoles.Employee, scopeOrg, "ORG_ONLY") };
        var token = NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.Employee,
            agreementCode: "HK", orgId: scopeOrg, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient AdminRoleClient(string role, string userId, string scopeOrg = OrgA)
    {
        var client = _factory.CreateClient();
        var scopes = new[] { new RoleScope(role, scopeOrg, "ORG_ONLY") };
        var token = NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: role,
            agreementCode: "AC", orgId: scopeOrg, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient GlobalAdminClient()
    {
        var client = _factory.CreateClient();
        var token = NewTokenService().GenerateToken(
            employeeId: "s105_gadmin", name: "s105_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: Min01,
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

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — HTTP
    // ════════════════════════════════════════════════════════════════════════════════

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

    /// <summary>GETs an approval read and returns the set of <c>employeeId</c> strings. Handles BOTH the
    /// bare-array reads (<c>/pending</c>, <c>/by-month</c>) and the <c>{ employees: [...] }</c> envelope
    /// (<c>/team-overview</c>).</summary>
    private static async Task<HashSet<string>> GetEmployeeIdsAsync(HttpClient client, string url)
    {
        var rsp = await client.GetAsync(url);
        rsp.EnsureSuccessStatusCode();
        var doc = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var array = doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("employees", out var emps)
            ? emps
            : doc;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (array.ValueKind == JsonValueKind.Array)
            foreach (var el in array.EnumerateArray())
                if (el.TryGetProperty("employeeId", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    ids.Add(idEl.GetString()!);
        return ids;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — the in-lock race harness (unit-org advisory + a concurrent revoke).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Opens a SIDE connection, takes the <c>unit-org-{org}</c> xact advisory (byte-identical to
    /// <c>UnitRepository.AcquireUnitOrgLockAsync</c>) AND deletes the <c>unit_leaders(unit, user)</c> row
    /// — the <c>UnitLeaderRemoved</c> effect — in the SAME uncommitted tx. While held, any approve that
    /// takes the same key BLOCKS; on commit the delete becomes visible AND the advisory releases (the
    /// revoke "wins"). The caller owns disposal.</summary>
    private async Task<(NpgsqlConnection conn, NpgsqlTransaction tx)> AcquireUnitOrgLockAndRevokeLeaderAsync(
        string organisationId, Guid unitId, string userId)
    {
        var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('unit-org-' || @org))", conn, tx))
        {
            lockCmd.Parameters.AddWithValue("org", organisationId);
            await lockCmd.ExecuteScalarAsync();
        }
        await using (var delCmd = new NpgsqlCommand(
            "DELETE FROM unit_leaders WHERE unit_id = @u AND user_id = @uid", conn, tx))
        {
            delCmd.Parameters.AddWithValue("u", unitId);
            delCmd.Parameters.AddWithValue("uid", userId);
            await delCmd.ExecuteNonQueryAsync();
        }
        return (conn, tx);
    }

    /// <summary>Polls <c>pg_locks ⋈ pg_stat_activity</c> until at least one OTHER backend is WAITING
    /// (<c>granted = FALSE</c>) on the <c>unit-org-{org}</c> advisory key — proving a request actually
    /// REACHED + BLOCKED ON the lock. Mirrors the S104/S95 waiter barrier.</summary>
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
    //  Helpers — period seeding + DB reads
    // ════════════════════════════════════════════════════════════════════════════════

    private Task<Guid> InsertSubmittedPeriodAsync(string employeeId)
        => InsertSubmittedPeriodAsync(employeeId, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

    private async Task<Guid> InsertSubmittedPeriodAsync(string employeeId, DateOnly start, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version, submitted_at, submitted_by)
            VALUES
                (@id, @emp, @org, @start, @end, 'MONTHLY', 'SUBMITTED', 'HK', 'OK24', NOW(), @emp)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgA);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<string> ReadStatusAsync(Guid periodId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Reads a single (string) column of the period row. The column name is a fixed test-local
    /// literal (never user input), so direct interpolation is safe here.</summary>
    private async Task<string?> ReadColumnAsync(Guid periodId, string column)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT {column} FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : (string)result;
    }

    private async Task<long> CountLeaderRowAsync(Guid unitId, string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM unit_leaders WHERE unit_id = @u AND user_id = @uid", conn);
        cmd.Parameters.AddWithValue("u", unitId);
        cmd.Parameters.AddWithValue("uid", userId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> SelectUnitVersionAsync(Guid unitId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT version FROM units WHERE unit_id = @id", conn);
        cmd.Parameters.AddWithValue("id", unitId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<bool> IsUnitSoftDeletedAsync(Guid unitId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM units WHERE unit_id = @id AND deleted_at IS NOT NULL", conn);
        cmd.Parameters.AddWithValue("id", unitId);
        return (long)(await cmd.ExecuteScalarAsync())! == 1;
    }

    private async Task<Guid?> SelectUserUnitAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT unit_id FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        var res = await cmd.ExecuteScalarAsync();
        return res is Guid g ? g : (Guid?)null;
    }

    private async Task<string?> SelectUserPrimaryOrgAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT primary_org_id FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (await cmd.ExecuteScalarAsync()) as string;
    }
}
