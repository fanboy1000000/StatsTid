using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// SPRINT-103 / TASK-10305 (Enhedsspor Phase 1a, ADR-038) — the foundation regression suite for the
/// new <c>units</c> / <c>unit_leaders</c> / <c>users.unit_id</c> model that replaced the legacy
/// <c>enheder</c> / <c>user_enheder</c> / <c>employee_profiles.enhed_label</c> Enhed model
/// (greenfield reseed, ADR-038 D9). It pins the Phase-1a hooks BEFORE the S104 units CRUD + the
/// unit-leader exception approval path land:
///
/// <list type="number">
///   <item><b>Reseed determinism / FK integrity</b> — a fresh DB boots the demo STY02 unit tree
///     (direktion→omrade→kontor→team) FK-valid, with the fixed UUIDs + correct parent chain + types,
///     mgr01 homed in IT-Drift, the unit_leaders (IT-Drift, mgr01) row, and the member-invariant (a
///     leader is a member of the unit it leads).</item>
///   <item><b>Derived-anchor attribution (ADR-038 D1/D2)</b> — <c>users.primary_org_id</c> is the
///     authority/payroll Organisation anchor for BOTH a NULL-<c>unit_id</c> org-homed user (hr01) AND
///     a unit-homed user (mgr01 → STY02); a scope read keyed on <c>primary_org_id</c> returns the
///     right Organisation in both cases.</item>
///   <item><b>Retained-approval safety</b> — approve / reject / reopen + the my-reports dashboard +
///     the medarbejder-roster reads all work (200 / correct status) AFTER the Enhed drop, with no
///     dropped-table join 500 — even for a unit-homed employee.</item>
///   <item><b>By-construction authority absence (ADR-038 D5 / P7)</b> — two users sharing a unit get
///     NO cross-user approval (<c>IsEffectiveDesignatedApprover</c>) nor access
///     (<c>ValidateEmployeeAccess</c>) from that fact alone (mirrors the S100 / ADR-036
///     "shared-ancestor grants nothing"). The source-level twin lives in
///     <c>StatsTid.Tests.Unit.ArchitectureConstraints.UnitAuthorityAbsenceTests</c>.</item>
/// </list>
///
/// <para>Endpoint-level via <see cref="StatsTidWebApplicationFactory"/> over a fresh testcontainer
/// (init.sql applied + the host seeders booted). The demo unit tree + memberships are seeded by
/// init.sql; the isolated approval/scope fixtures are <c>t103_*</c>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class UnitFoundationTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // The init.sql demo unit tree under STY02 (fixed UUIDs for reseed determinism).
    private const string UnitDirektion = "000000d0-0000-0000-0000-000000000001";   // type 'direktion', root
    private const string UnitDriftsomraadet = "000000d0-0000-0000-0000-000000000002"; // type 'omrade'
    private const string UnitItDrift = "000000d0-0000-0000-0000-000000000003";       // type 'kontor'  (mgr01 leads)
    private const string UnitTeamInfra = "000000d0-0000-0000-0000-000000000004";     // type 'team'

    private const string Sty02 = "STY02";
    private const string Sty01 = "STY01";

    // Isolated approval/scope fixtures (disjoint from the seed).
    private const string Emp = "t103_emp";   // STY02, unit-homed in Team Infrastruktur — the report
    private const string Mgr = "t103_mgr";   // STY02 — PRIMARY manager of Emp (LocalLeader on STY02)
    private const string U1 = "t103_u1";     // STY02, unit IT-Drift — a Leader scoped to STY01 (disjoint)
    private const string U2 = "t103_u2";     // STY02, unit IT-Drift — shares U1's unit, NO edge

    private static readonly string[] AllUsers = { Emp, Mgr, U1, U2 };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);

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
        // Isolated users (all on STY02 → the Organisation-home invariant holds). Emp is unit-homed in
        // Team Infrastruktur; U1/U2 share IT-Drift; Mgr is org-homed (unit_id NULL).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active)
            VALUES
                (@emp, @emp, '$2a$11$fake', 'T103 Emp', 't103_emp@test.dk', 'STY02', @teamUnit, 'HK', 'OK24', TRUE),
                (@mgr, @mgr, '$2a$11$fake', 'T103 Mgr', 't103_mgr@test.dk', 'STY02', NULL,       'HK', 'OK24', TRUE),
                (@u1,  @u1,  '$2a$11$fake', 'T103 U1',  't103_u1@test.dk',  'STY02', @itUnit,    'HK', 'OK24', TRUE),
                (@u2,  @u2,  '$2a$11$fake', 'T103 U2',  't103_u2@test.dk',  'STY02', @itUnit,    'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            cmd.Parameters.AddWithValue("teamUnit", Guid.Parse(UnitTeamInfra));
            cmd.Parameters.AddWithValue("itUnit", Guid.Parse(UnitItDrift));
            await cmd.ExecuteNonQueryAsync();
        }

        // Mgr is a LocalLeader covering STY02; U1 is a LocalLeader on STY01 (a DIFFERENT Organisation —
        // it does NOT org-cover U2 on STY02, so the only thing U1 and U2 share is the unit).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES
                (@mgr, 'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@emp, 'EMPLOYEE',     'STY02', 'ORG_ONLY', 'TEST'),
                (@u1,  'LOCAL_LEADER', 'STY01', 'ORG_ONLY', 'TEST'),
                (@u2,  'EMPLOYEE',     'STY02', 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Emp reports PRIMARY to Mgr (the same-STY02 designated edge). U1/U2 have NO edge.
        await new ReportingLineRepository(_dbFactory).AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = Emp,
            ManagerId = Mgr,
            OrganisationId = Sty02,
            Relationship = "PRIMARY",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("u1", U1);
        cmd.Parameters.AddWithValue("u2", U2);
    }

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        // Only the isolated t103_* fixtures are removed — the seeded demo unit tree + memberships
        // (mgr01 / units) are baseline and left intact. (No enheder/user_enheder tables exist post-S103.)
        await ExecAsync(conn,
            "DELETE FROM approval_audit WHERE actor_id = ANY(@ids) OR period_id IN (SELECT period_id FROM approval_periods WHERE employee_id = ANY(@ids))");
        await ExecAsync(conn, "DELETE FROM approval_periods WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM users WHERE user_id = ANY(@ids)");

        async Task ExecAsync(NpgsqlConnection c, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.AddWithValue("ids", AllUsers);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (C.6) Reseed determinism / FK integrity — the demo STY02 unit tree.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReseedUnitTree_IsFkValid_WithCorrectChainTypesLeaderAndMemberInvariant()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // The 4 STY02 units exist with the fixed UUIDs, correct types, and the parent chain
        // direktion(root) → omrade → kontor → team.
        var (typeDir, parentDir) = await ReadUnitAsync(conn, UnitDirektion);
        var (typeOmr, parentOmr) = await ReadUnitAsync(conn, UnitDriftsomraadet);
        var (typeKon, parentKon) = await ReadUnitAsync(conn, UnitItDrift);
        var (typeTeam, parentTeam) = await ReadUnitAsync(conn, UnitTeamInfra);

        Assert.Equal("direktion", typeDir);
        Assert.Null(parentDir); // the root unit
        Assert.Equal("omrade", typeOmr);
        Assert.Equal(Guid.Parse(UnitDirektion), parentOmr);
        Assert.Equal("kontor", typeKon);
        Assert.Equal(Guid.Parse(UnitDriftsomraadet), parentKon);
        Assert.Equal("team", typeTeam);
        Assert.Equal(Guid.Parse(UnitItDrift), parentTeam);

        // All 4 belong to STY02 (an ORGANISATION).
        var orgOfUnits = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM units u JOIN organizations o ON o.org_id = u.organisation_id " +
            "WHERE u.organisation_id = 'STY02' AND o.org_type = 'ORGANISATION'");
        Assert.Equal(4, orgOfUnits);

        // mgr01 is homed in IT-Drift.
        var mgr01Unit = await ScalarGuidAsync(conn, "SELECT unit_id FROM users WHERE user_id = 'mgr01'");
        Assert.Equal(Guid.Parse(UnitItDrift), mgr01Unit);

        // unit_leaders carries (IT-Drift, mgr01).
        var leaderRow = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM unit_leaders WHERE unit_id = @u AND user_id = 'mgr01'",
            ("u", Guid.Parse(UnitItDrift)));
        Assert.Equal(1, leaderRow);

        // FK integrity: no orphan parent_unit_id, no orphan users.unit_id (the FKs guarantee this;
        // assert directly so a future raw-seed regression is caught).
        var orphanParents = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM units c WHERE c.parent_unit_id IS NOT NULL " +
            "AND NOT EXISTS (SELECT 1 FROM units p WHERE p.unit_id = c.parent_unit_id)");
        Assert.Equal(0, orphanParents);
        var orphanMembers = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM users u WHERE u.unit_id IS NOT NULL " +
            "AND NOT EXISTS (SELECT 1 FROM units x WHERE x.unit_id = u.unit_id)");
        Assert.Equal(0, orphanMembers);

        // The MEMBER-INVARIANT (ADR-038 D4): every unit_leaders row's user is a MEMBER of the unit it
        // leads (user.unit_id == unit_leaders.unit_id). Zero violations.
        var invariantViolations = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM unit_leaders ul JOIN users u ON u.user_id = ul.user_id " +
            "WHERE u.unit_id IS DISTINCT FROM ul.unit_id");
        Assert.Equal(0, invariantViolations);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (C.7) Derived-anchor attribution — primary_org_id is the Organisation for BOTH homing modes.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DerivedAnchor_PrimaryOrgId_ResolvesOrganisation_ForOrgHomedAndUnitHomed()
    {
        var users = new UserRepository(_dbFactory);
        var orgs = new OrganizationRepository(_dbFactory);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // hr01 — an ORGANISATION-homed user (unit_id IS NULL): the anchor IS the Organisation.
        var hr01UnitId = await ScalarGuidAsync(conn, "SELECT unit_id FROM users WHERE user_id = 'hr01'");
        Assert.Null(hr01UnitId);
        var hr01 = await users.GetByIdAsync("hr01");
        Assert.NotNull(hr01);
        Assert.Equal("STY02", hr01!.PrimaryOrgId);

        // mgr01 — a UNIT-homed user (unit_id = IT-Drift): the DERIVED anchor is STILL the Organisation
        // (ADR-038 D2 — primary_org_id is the unit's organisation_id), NOT the unit.
        var mgr01UnitId = await ScalarGuidAsync(conn, "SELECT unit_id FROM users WHERE user_id = 'mgr01'");
        Assert.Equal(Guid.Parse(UnitItDrift), mgr01UnitId);
        var mgr01 = await users.GetByIdAsync("mgr01");
        Assert.NotNull(mgr01);
        Assert.Equal("STY02", mgr01!.PrimaryOrgId);

        // A scope/payroll read keyed on primary_org_id (exactly what OrgScopeValidator does) returns
        // the right Organisation in BOTH cases — the unit dimension never enters.
        var hrOrg = await orgs.GetByIdAsync(hr01.PrimaryOrgId);
        var mgrOrg = await orgs.GetByIdAsync(mgr01.PrimaryOrgId);
        Assert.NotNull(hrOrg);
        Assert.NotNull(mgrOrg);
        Assert.Equal("STY02", hrOrg!.OrgId);
        Assert.Equal("ORGANISATION", hrOrg.OrgType);
        Assert.Equal("STY02", mgrOrg!.OrgId);
        Assert.Equal("ORGANISATION", mgrOrg.OrgType);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (C.8) Retained-approval safety — approve/reject/reopen + dashboard/roster reads post-drop.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RetainedApproval_ApproveRejectReopen_AndDashboardRosterReads_Work_NoEnhedJoin500()
    {
        var mgrClient = _factory.CreateClient();
        mgrClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintToken(Mgr, StatsTidRoles.LocalLeader, Sty02));

        // ── period-1: SUBMITTED → APPROVE → REOPEN ──
        var p1 = await InsertPeriodAsync(Emp, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        // The my-reports dashboard read surfaces Emp's period for the designated approver (no 500 from
        // a dropped-table join — the unit-homed employee flows through cleanly).
        var pending = await GetMyReportsAsync(mgrClient);
        Assert.Contains(pending, p => p == p1);

        var approve = await mgrClient.PostAsync($"/api/approval/{p1}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(p1));

        var reopen = await mgrClient.PostAsJsonAsync($"/api/approval/{p1}/reopen", new { reason = "t103-reopen" });
        Assert.Equal(HttpStatusCode.OK, reopen.StatusCode);
        Assert.Equal("DRAFT", await ReadStatusAsync(p1));

        // ── period-2 (distinct range): SUBMITTED → REJECT ──
        var p2 = await InsertPeriodAsync(Emp, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        var reject = await mgrClient.PostAsJsonAsync($"/api/approval/{p2}/reject", new { reason = "t103-reject" });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal("REJECTED", await ReadStatusAsync(p2));

        // The DESIGNATED-reports dashboard read works directly (no enhed join).
        var repo = NewApprovalRepo();
        var dashboard = await repo.GetPendingForDesignatedReportsAsync(Mgr, Array.Empty<RoleScope>());
        // (p1 is DRAFT now and p2 REJECTED — neither pending — so the dashboard simply must not throw.)
        Assert.NotNull(dashboard);

        // The medarbejder-roster read (the S97/S99/S100 fetchEnheder bug surface) returns 200 for a
        // STY02-scoped admin AND lists the unit-homed employee — proving no dropped-enhed-table join.
        var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintToken("t103_admin", StatsTidRoles.LocalAdmin, Sty02));
        var rosterRsp = await adminClient.GetAsync($"/api/admin/reporting-lines/tree/{Sty02}/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, rosterRsp.StatusCode);
        var rosterBody = await rosterRsp.Content.ReadFromJsonAsync<JsonElement>();
        var rosterIds = rosterBody.GetProperty("employees").EnumerateArray()
            .Select(e => e.GetProperty("employeeId").GetString()).ToList();
        Assert.Contains(Emp, rosterIds);
        // enhedLabel is now the primary-org name (the column was dropped) — present, never a 500.
        var empRow = rosterBody.GetProperty("employees").EnumerateArray()
            .First(e => e.GetProperty("employeeId").GetString() == Emp);
        Assert.Equal("Statens IT", empRow.GetProperty("enhedLabel").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (C.9) By-construction authority absence — a shared unit grants NOTHING (ADR-038 D5 / P7).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SharedUnit_GrantsNoCrossUserApproval_NorEmployeeAccess()
    {
        // U1 and U2 share the SAME unit (IT-Drift) and the SAME Organisation (STY02), but there is NO
        // reporting edge between them and U1's role-scope is STY01 (a DIFFERENT Organisation). The unit
        // membership is the ONLY thing they share.
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // (a) Approval authority: a shared unit does NOT make U1 U2's designated approver (no edge) —
        //     the unit-leader exception path is not wired until S104.
        Assert.False(await authorizer.IsEffectiveDesignatedApproverAsync(U1, U2, asOf: today));
        Assert.False(await authorizer.IsEffectiveDesignatedApproverAsync(U2, U1, asOf: today));

        // (b) Org-scope access: a shared unit does NOT let U1 (scoped STY01) reach U2 (homed STY02) —
        //     CoversOrg is exact-Organisation, and unit_id never enters the scope path.
        var validator = new OrgScopeValidator(
            new OrganizationRepository(_dbFactory),
            new UserRepository(_dbFactory),
            NullLogger<OrgScopeValidator>.Instance);

        var u1Actor = new ActorContext(
            ActorId: U1,
            ActorRole: StatsTidRoles.LocalLeader,
            CorrelationId: Guid.NewGuid(),
            OrgId: Sty01,
            Scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, Sty01, "ORG_ONLY") });

        var (allowed, _) = await validator.ValidateEmployeeAccessAsync(u1Actor, U2);
        Assert.False(allowed);

        // Sanity (the discriminator): if U1 were instead scoped to U2's OWN Organisation (STY02), the
        // ORG-scope (NOT the unit) would admit — proving the denial above is the disjoint scope, and
        // that a shared unit contributed nothing either way.
        var u1ActorSty02 = u1Actor with
        {
            OrgId = Sty02,
            Scopes = new[] { new RoleScope(StatsTidRoles.LocalLeader, Sty02, "ORG_ONLY") },
        };
        var (allowedSty02, _) = await validator.ValidateEmployeeAccessAsync(u1ActorSty02, U2);
        Assert.True(allowedSty02);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private ApprovalPeriodRepository NewApprovalRepo()
    {
        var reportingRepo = new ReportingLineRepository(_dbFactory);
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, reportingRepo);
        return new ApprovalPeriodRepository(_dbFactory, authorizer, reportingRepo);
    }

    private async Task<Guid> InsertPeriodAsync(string employeeId, DateOnly start, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version, submitted_at, submitted_by)
            VALUES
                (@id, @emp, 'STY02', @start, @end, 'MONTHLY', 'SUBMITTED', 'HK', 'OK24', NOW(), @emp)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<string> ReadStatusAsync(Guid periodId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<List<Guid>> GetMyReportsAsync(HttpClient client)
    {
        var rsp = await client.GetAsync("/api/approval/pending?my-reports=true");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var arr = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return arr.EnumerateArray().Select(e => e.GetProperty("periodId").GetGuid()).ToList();
    }

    private static async Task<(string Type, Guid? Parent)> ReadUnitAsync(NpgsqlConnection conn, string unitId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT type, parent_unit_id FROM units WHERE unit_id = @id", conn);
        cmd.Parameters.AddWithValue("id", Guid.Parse(unitId));
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"unit {unitId} missing from the reseeded DB.");
        var type = reader.GetString(0);
        Guid? parent = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        return (type, parent);
    }

    private static async Task<Guid?> ScalarGuidAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var res = await cmd.ExecuteScalarAsync();
        return res is Guid g ? g : (Guid?)null;
    }

    private static async Task<long> ScalarLongAsync(
        NpgsqlConnection conn, string sql, params (string Name, object Value)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        var res = await cmd.ExecuteScalarAsync();
        return res is long l ? l : Convert.ToInt64(res);
    }

    private static string MintToken(string userId, string role, string orgId)
    {
        var scopes = new[] { new RoleScope(role, orgId, "ORG_ONLY") };
        return NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: role,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });
}
