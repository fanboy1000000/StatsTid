using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// SPRINT-106 / TASK-10602 + TASK-10604 (Enhedsspor Phase 3a, ADR-038 D2/D4/D6, PAT-010) — the unit-tagged
/// medarbejder roster + the closed S105 tile-count scope-out.
///
/// <para><b>TASK-10602</b> adds to each <c>materialized_path</c>-scoped roster row: <c>unitId</c>/<c>unitName</c>
/// + the row's unit's aggregated <c>leaderIds</c> (the multi-peer-leader join must NOT fan the set-based roster
/// out — one row per employee) + a NULLABLE <c>primaryReportingLineVersion</c> etag (the active PRIMARY
/// <c>reporting_lines.version</c>; null for a root/orphan with no active PRIMARY edge → "Ret" creates vs
/// supersedes, S99). A DISPLAY-ONLY by-id <c>nameResolution</c> map labels the upward-reference + cross-unit-
/// leader chips even for an id NOT in the active roster (an inactive manager/leader). The incoming "Vikar for X"
/// tag is derivable by INVERTING the existing <c>outgoingVikar</c> within the loaded set (no new field).</para>
///
/// <para><b>TASK-10604</b> closes the S105 EDGE-ONLY tile scope-out: <c>pendingCountByManager</c> now ENUMERATES
/// per pending employee the edge manager AND the employee's unit's leaders + active vikar-of-leader, tallying
/// EACH authorized approver (the inverse of the S105 <c>unit_led_members</c> CTE). Semantic shift: a pending
/// employee now counts toward MULTIPLE managers' tiles (Σ tiles ≥ pending count; the "exactly once" docstring
/// inverts). The roster reuses the same tally, so the medarbejder-page tiles shift accordingly.</para>
///
/// <para>Topology (under STY02 = <c>/MIN01/STY02/</c>, org_name "Statens IT"): an isolated multi-leader unit
/// <c>UnitMulti</c> (2 active peer leaders LdrA/LdrB + 1 INACTIVE leader InactiveLdr) holding Member1 (EMPLOYEE,
/// PRIMARY edge → a DIFFERENT manager EdgeMgr); VikarA is an active <c>manager_vikar</c> stand-in for LdrA;
/// Orphan106 is a no-edge no-unit orphan (null etag); EdgeOnlyEmp is a no-unit member whose pure-edge tally is
/// unchanged. Each [Fact] boots a FRESH Postgres testcontainer (init.sql demo data + the isolated
/// <c>s106_*</c> fixtures); idioms mirror <see cref="MedarbejderRosterReadTests"/> +
/// <see cref="S105UnitLeaderApprovalTests"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S106RosterUnitTagTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string OrgA = "STY02";
    private const string Sty02Path = "/MIN01/STY02/";

    // The isolated multi-leader unit under STY02 (distinct from demo `000000d0-…` + s105 `51050000-…`).
    private static readonly Guid UnitMulti = Guid.Parse("61060000-0000-0000-0000-000000000001");
    private static readonly Guid[] AllUnits = { UnitMulti };

    private const string Member1   = "s106_member1";   // UnitMulti, EMPLOYEE — PRIMARY edge → EdgeMgr
    private const string EdgeMgr    = "s106_edgemgr";   // unit NULL, LocalLeader — Member1's edge approver (upward-ref)
    private const string LdrA       = "s106_ldra";      // UnitMulti, LocalLeader — a leader of UnitMulti
    private const string LdrB       = "s106_ldrb";      // UnitMulti, LocalLeader — a PEER leader of UnitMulti
    private const string InactiveLdr= "s106_inactldr";  // UnitMulti, LocalLeader, is_active FALSE — leader (name-resolution / floor)
    private const string VikarA     = "s106_vikara";    // UnitMulti, LocalLeader — active manager_vikar for LdrA
    private const string Orphan106  = "s106_orphan";    // unit NULL, EMPLOYEE, NO edge — null etag
    private const string EdgeOnlyEmp= "s106_edgeonly";  // unit NULL, EMPLOYEE — PRIMARY edge → EdgeMgr2 (pure-edge tile)
    private const string EdgeMgr2   = "s106_edgemgr2";  // unit NULL, LocalLeader — EdgeOnlyEmp's edge approver

    private static readonly string[] AllUsers =
    {
        Member1, EdgeMgr, LdrA, LdrB, InactiveLdr, VikarA, Orphan106, EdgeOnlyEmp, EdgeMgr2,
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
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES
                (@multi, @orgA, NULL, 'kontor', 'S106 Multi-Leader Unit')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("multi", UnitMulti);
            cmd.Parameters.AddWithValue("orgA", OrgA);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active)
            VALUES
                (@member1,  @member1,  '$2a$11$fake', 'S106 Member1',  's106_member1@test.dk',  @orgA, @multi, 'HK','OK24', TRUE),
                (@edgemgr,  @edgemgr,  '$2a$11$fake', 'S106 EdgeMgr',  's106_edgemgr@test.dk',  @orgA, NULL,   'HK','OK24', TRUE),
                (@ldra,     @ldra,     '$2a$11$fake', 'S106 LdrA',     's106_ldra@test.dk',     @orgA, @multi, 'HK','OK24', TRUE),
                (@ldrb,     @ldrb,     '$2a$11$fake', 'S106 LdrB',     's106_ldrb@test.dk',     @orgA, @multi, 'HK','OK24', TRUE),
                (@inactldr, @inactldr, '$2a$11$fake', 'S106 InactLdr', 's106_inactldr@test.dk', @orgA, @multi, 'HK','OK24', FALSE),
                (@vikara,   @vikara,   '$2a$11$fake', 'S106 VikarA',   's106_vikara@test.dk',   @orgA, @multi, 'HK','OK24', TRUE),
                (@orphan,   @orphan,   '$2a$11$fake', 'S106 Orphan',   's106_orphan@test.dk',   @orgA, NULL,   'HK','OK24', TRUE),
                (@edgeonly, @edgeonly, '$2a$11$fake', 'S106 EdgeOnly', 's106_edgeonly@test.dk', @orgA, NULL,   'HK','OK24', TRUE),
                (@edgemgr2, @edgemgr2, '$2a$11$fake', 'S106 EdgeMgr2', 's106_edgemgr2@test.dk', @orgA, NULL,   'HK','OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            cmd.Parameters.AddWithValue("orgA", OrgA);
            cmd.Parameters.AddWithValue("multi", UnitMulti);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
                (@edgemgr,  'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST'),
                (@ldra,     'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST'),
                (@ldrb,     'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST'),
                (@inactldr, 'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST'),
                (@vikara,   'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST'),
                (@edgemgr2, 'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST'),
                (@member1,  'EMPLOYEE',     @orgA, 'ORG_ONLY', 'TEST'),
                (@orphan,   'EMPLOYEE',     @orgA, 'ORG_ONLY', 'TEST'),
                (@edgeonly, 'EMPLOYEE',     @orgA, 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            cmd.Parameters.AddWithValue("orgA", OrgA);
            await cmd.ExecuteNonQueryAsync();
        }

        // UnitMulti's leaders: LdrA + LdrB (active peers) + InactiveLdr (inactive — in leaderIds but
        // not in the active roster, the name-resolution + floor probe).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO unit_leaders (unit_id, user_id) VALUES
                (@multi, @ldra),
                (@multi, @ldrb),
                (@multi, @inactldr)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("multi", UnitMulti);
            cmd.Parameters.AddWithValue("ldra", LdrA);
            cmd.Parameters.AddWithValue("ldrb", LdrB);
            cmd.Parameters.AddWithValue("inactldr", InactiveLdr);
            await cmd.ExecuteNonQueryAsync();
        }

        // VikarA is the ACTIVE manager_vikar stand-in for LdrA (an outgoing vikar on LdrA's row →
        // the incoming "Vikar for LdrA" tag is derivable by inversion; also the unit-leader-vikar tile).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO manager_vikar (absent_approver_id, vikar_user_id, until_date, reason, organisation_id, created_by) VALUES
                (@ldra, @vikara, @future, 'FERIE', @orgA, 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("ldra", LdrA);
            cmd.Parameters.AddWithValue("vikara", VikarA);
            cmd.Parameters.AddWithValue("future", new DateOnly(2099, 12, 31));
            cmd.Parameters.AddWithValue("orgA", OrgA);
            await cmd.ExecuteNonQueryAsync();
        }

        // Reporting edges (same Organisation STY02, active managers): Member1 → EdgeMgr (a DIFFERENT
        // person from the unit's leaders — the cross-unit exception); EdgeOnlyEmp → EdgeMgr2 (pure edge).
        var rlRepo = new ReportingLineRepository(_dbFactory);
        await rlRepo.AssignAsync(null, MakeLine(Member1, EdgeMgr));
        await rlRepo.AssignAsync(null, MakeLine(EdgeOnlyEmp, EdgeMgr2));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("member1", Member1);
        cmd.Parameters.AddWithValue("edgemgr", EdgeMgr);
        cmd.Parameters.AddWithValue("ldra", LdrA);
        cmd.Parameters.AddWithValue("ldrb", LdrB);
        cmd.Parameters.AddWithValue("inactldr", InactiveLdr);
        cmd.Parameters.AddWithValue("vikara", VikarA);
        cmd.Parameters.AddWithValue("orphan", Orphan106);
        cmd.Parameters.AddWithValue("edgeonly", EdgeOnlyEmp);
        cmd.Parameters.AddWithValue("edgemgr2", EdgeMgr2);
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
        await ExecUsersAsync(conn, "DELETE FROM approval_periods WHERE employee_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM manager_vikar WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM unit_leaders WHERE user_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@ids)");
        await ExecUsersAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        // Detach any straggler members + leaders, then drop the isolated s106 units (NOT the demo tree).
        await using (var nuke = new NpgsqlCommand(
            """
            UPDATE users SET unit_id = NULL WHERE unit_id = ANY(@uids);
            DELETE FROM unit_leaders WHERE unit_id = ANY(@uids);
            DELETE FROM users WHERE user_id = ANY(@ids);
            DELETE FROM units WHERE unit_id = ANY(@uids);
            """, conn))
        {
            nuke.Parameters.AddWithValue("ids", AllUsers);
            nuke.Parameters.AddWithValue("uids", AllUnits);
            await nuke.ExecuteNonQueryAsync();
        }

        async Task ExecUsersAsync(NpgsqlConnection c, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.AddWithValue("ids", AllUsers);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task InsertSubmittedPeriodAsync(string employeeId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodEnd = today.AddDays(-1);
        var periodStart = periodEnd.AddDays(-30);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version, submitted_at, submitted_by)
            VALUES
                (@id, @emp, @org, @start, @end, 'MONTHLY', 'SUBMITTED', 'HK', 'OK24', NOW(), @emp)
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgA);
        cmd.Parameters.AddWithValue("start", periodStart);
        cmd.Parameters.AddWithValue("end", periodEnd);
        await cmd.ExecuteNonQueryAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  TASK-10602 — fan-out, etag nullability, name resolution, vikar inverse
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The multi-peer-leader unit MUST NOT fan the set-based roster out: Member1 (in UnitMulti
    /// with ≥2 leaders) appears EXACTLY once, and its aggregated <c>leaderIds</c> carry every designated
    /// leader of its unit (LdrA + LdrB + the inactive InactiveLdr — the source is <c>unit_leaders</c>, not
    /// the active roster). RED on a naive <c>LEFT JOIN unit_leaders</c> (which would yield 3 Member1 rows).</summary>
    [Fact]
    public async Task Roster_MultiLeaderUnit_DoesNotFanOut_LeaderIdsAggregated()
    {
        var repo = NewApprovalRepo();
        var roster = await repo.GetMedarbejderRosterForTreeAsync(Sty02Path);

        // EXACTLY one roster row for Member1 despite the unit having 3 leader rows.
        Assert.Equal(1, roster.Employees.Count(e => e.EmployeeId == Member1));

        var row = roster.Employees.Single(e => e.EmployeeId == Member1);
        Assert.Equal(UnitMulti, row.UnitId);
        Assert.Equal("S106 Multi-Leader Unit", row.UnitName);
        // The aggregated leaderIds carry all three designated leaders (the inactive one included — the
        // source is unit_leaders, not the active-roster filter).
        Assert.Contains(LdrA, row.LeaderIds);
        Assert.Contains(LdrB, row.LeaderIds);
        Assert.Contains(InactiveLdr, row.LeaderIds);
        Assert.Equal(3, row.LeaderIds.Count);

        // A no-unit member carries a null unit + an empty leaderIds (never null — a clean wire array).
        var orphan = roster.Employees.Single(e => e.EmployeeId == Orphan106);
        Assert.Null(orphan.UnitId);
        Assert.Null(orphan.UnitName);
        Assert.Empty(orphan.LeaderIds);

        // The cross-unit exception is derivable: Member1's reporting manager (EdgeMgr) ∉ its unit leaders.
        Assert.DoesNotContain(row.StructuralApproverId!, row.LeaderIds);
    }

    /// <summary>The etag <c>primaryReportingLineVersion</c> = the active PRIMARY <c>reporting_lines.version</c>
    /// (non-null for a person WITH an active PRIMARY edge; NULL for a root/orphan with none → the FE's "Ret"
    /// creates vs supersedes, the S99 distinction).</summary>
    [Fact]
    public async Task Roster_PrimaryReportingLineVersion_IsActivePrimaryEdgeVersion_NullableForRootOrphan()
    {
        var repo = NewApprovalRepo();
        var roster = await repo.GetMedarbejderRosterForTreeAsync(Sty02Path);

        MedarbejderRosterRow Row(string id) => roster.Employees.Single(e => e.EmployeeId == id);

        // Member1 has an active PRIMARY edge → a non-null etag equal to that row's version.
        var member1 = Row(Member1);
        Assert.NotNull(member1.PrimaryReportingLineVersion);
        var dbVersion = await SelectActivePrimaryVersionAsync(Member1);
        Assert.Equal(dbVersion, member1.PrimaryReportingLineVersion);

        // Orphan106 (no edge) + EdgeMgr (a parent with no own edge) → NULL etag.
        Assert.Null(Row(Orphan106).PrimaryReportingLineVersion);
        Assert.Null(Row(EdgeMgr).PrimaryReportingLineVersion);
    }

    /// <summary>The DISPLAY-ONLY by-id <c>nameResolution</c> map labels the upward-reference (Member1 →
    /// EdgeMgr) + the cross-unit-leader chips (LdrA/LdrB), AND resolves a referenced leader who is NOT an
    /// active in-roster row (the INACTIVE InactiveLdr) — so the FE never shows a blank chip. It is display-only:
    /// it admits nobody into scope (InactiveLdr stays absent from the roster body).</summary>
    [Fact]
    public async Task Roster_NameResolution_ResolvesReferencedIds_IncludingOutOfRosterLeader_DisplayOnly()
    {
        var repo = NewApprovalRepo();
        var roster = await repo.GetMedarbejderRosterForTreeAsync(Sty02Path);

        // The upward-reference (EdgeMgr) + the cross-unit leaders (LdrA/LdrB) resolve.
        Assert.Equal("S106 EdgeMgr", roster.NameResolution[EdgeMgr].DisplayName);
        Assert.Equal("S106 LdrA", roster.NameResolution[LdrA].DisplayName);
        Assert.Equal("S106 LdrB", roster.NameResolution[LdrB].DisplayName);

        // The INACTIVE leader is referenced (∈ Member1.leaderIds) → resolves via the by-id lookup, carrying
        // its unit name, EVEN THOUGH it is NOT an active roster row (display-only, no scope widening).
        Assert.True(roster.NameResolution.ContainsKey(InactiveLdr));
        Assert.Equal("S106 InactLdr", roster.NameResolution[InactiveLdr].DisplayName);
        Assert.Equal("S106 Multi-Leader Unit", roster.NameResolution[InactiveLdr].UnitName);
        Assert.DoesNotContain(roster.Employees, e => e.EmployeeId == InactiveLdr);
    }

    /// <summary>The stand-in "Vikar for X" (incoming-vikar) tag is derivable by INVERTING the existing
    /// <c>outgoingVikar</c> within the loaded set — no new field needed. LdrA carries an outgoing vikar
    /// (VikarA); VikarA is an active in-roster row; so scanning the roster for a row whose
    /// <c>outgoingVikar.vikarUserId == VikarA</c> finds LdrA → "VikarA is Vikar for LdrA".</summary>
    [Fact]
    public async Task Roster_IncomingVikarTag_DerivableByInvertingOutgoingVikar_WithinLoadedSet()
    {
        var repo = NewApprovalRepo();
        var roster = await repo.GetMedarbejderRosterForTreeAsync(Sty02Path);

        // The stand-in is in the loaded set (active, same Organisation).
        Assert.Contains(roster.Employees, e => e.EmployeeId == VikarA);

        // LdrA's row carries the outgoing-vikar marker pointing at VikarA.
        var ldrA = roster.Employees.Single(e => e.EmployeeId == LdrA);
        Assert.NotNull(ldrA.OutgoingVikar);
        Assert.Equal(VikarA, ldrA.OutgoingVikar!.VikarUserId);

        // Inversion: the rows VikarA stands in for = those whose outgoingVikar names VikarA → {LdrA}.
        var standsInFor = roster.Employees
            .Where(e => e.OutgoingVikar?.VikarUserId == VikarA)
            .Select(e => e.EmployeeId)
            .ToList();
        Assert.Equal(new[] { LdrA }, standsInFor);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  TASK-10604 — the per-authorized-approver tile-count enumeration
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The S105 EDGE-ONLY tile scope-out is closed: a single pending employee (Member1) now tallies
    /// to EVERY authorized approver — the edge manager (EdgeMgr) AND each active unit-leader of its unit (LdrA,
    /// LdrB) AND the active vikar-of-a-unit-leader (VikarA). The INACTIVE leader (InactiveLdr) is NOT tallied
    /// (the active-LeaderOrAbove floor). The cardinality inverts: ONE pending employee → 4 distinct manager
    /// tiles (Σ ≥ pending count), the inverse of the old "tallied exactly once".</summary>
    [Fact]
    public async Task TileCount_PendingEmployee_TalliesToEdgeAndUnitLeadersAndVikar_InactiveExcluded()
    {
        await InsertSubmittedPeriodAsync(Member1);

        var repo = NewApprovalRepo();
        var projection = await repo.GetPeriodStatusProjectionForTreeAsync(Sty02Path);
        var tiles = projection.PendingCountByManager;

        // The edge manager + both active unit-leaders + the unit-leader's vikar each see Member1 (== 1).
        Assert.Equal(1, tiles.GetValueOrDefault(EdgeMgr));
        Assert.Equal(1, tiles.GetValueOrDefault(LdrA));
        Assert.Equal(1, tiles.GetValueOrDefault(LdrB));
        Assert.Equal(1, tiles.GetValueOrDefault(VikarA));   // the unit-leader-vikar tile (the S105 dashboard set)

        // The inactive leader is NOT tallied (the floor) — never a leak.
        Assert.False(tiles.ContainsKey(InactiveLdr));

        // CARDINALITY (Σ tiles ≥ pending count): the ONE pending Member1 contributes to 4 distinct tiles.
        var tilesForMember1 = new[] { EdgeMgr, LdrA, LdrB, VikarA }.Sum(m => tiles.GetValueOrDefault(m));
        Assert.True(tilesForMember1 >= 1);
        Assert.Equal(4, tilesForMember1);
    }

    /// <summary>The pure-edge case is UNCHANGED: a pending employee with NO unit (EdgeOnlyEmp) tallies ONLY to
    /// its single edge manager (EdgeMgr2 == 1) — the unit-leader enumeration adds nothing when there is no
    /// unit, and EdgeMgr2 (who leads no unit) receives no spurious unit-leader tally.</summary>
    [Fact]
    public async Task TileCount_PureEdgeEmployee_TalliesOnlyToEdgeManager_Unchanged()
    {
        await InsertSubmittedPeriodAsync(EdgeOnlyEmp);

        var repo = NewApprovalRepo();
        var projection = await repo.GetPeriodStatusProjectionForTreeAsync(Sty02Path);
        var tiles = projection.PendingCountByManager;

        Assert.Equal(1, tiles.GetValueOrDefault(EdgeMgr2));
        // EdgeMgr2 leads no unit; no unit-leader path inflated any other isolated tile from this employee.
        Assert.False(tiles.ContainsKey(LdrA));
        Assert.False(tiles.ContainsKey(LdrB));
        Assert.False(tiles.ContainsKey(VikarA));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  HTTP endpoint — the new wire fields (PAT-010 camelCase, no [JsonPropertyName])
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The roster endpoint serves the new fields on the real wire shape (camelCase): each row carries
    /// <c>unitId</c>/<c>unitName</c>/<c>leaderIds</c>/<c>primaryReportingLineVersion</c>, and the envelope
    /// carries a <c>nameResolution</c> object keyed by user id. The nullable etag is JSON-null for an orphan.</summary>
    [Fact]
    public async Task RosterEndpoint_ServesUnitTagFields_AndNameResolution()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken("admin_sty02", OrgA));

        var rsp = await client.GetAsync("/api/admin/reporting-lines/tree/STY02/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // ENVELOPE (NOT a bare array — the S97/S99/S100 distinction): { employees: [...],
        // pendingCountByManager: {...}, nameResolution: {...} }. The roster contract surface.
        var employees = ContractAssert.IsEnvelope(body, "employees");
        ContractAssert.HasFields(body, "employees", "pendingCountByManager", "nameResolution");

        var member1 = employees.EnumerateArray().First(e => e.GetProperty("employeeId").GetString() == Member1);
        // The full row field-set (camelCase, literally) — a dropped/renamed field is RED here.
        ContractAssert.HasFields(member1,
            "employeeId", "displayName", "enhedLabel", "position", "structuralApproverId", "periodStatus",
            "outgoingVikar", "isRoot", "isOrphan", "unitId", "unitName", "leaderIds", "primaryReportingLineVersion");
        Assert.Equal(UnitMulti.ToString(), member1.GetProperty("unitId").GetString());
        Assert.Equal("S106 Multi-Leader Unit", member1.GetProperty("unitName").GetString());
        var leaderIds = member1.GetProperty("leaderIds").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains(LdrA, leaderIds);
        Assert.Contains(LdrB, leaderIds);
        Assert.Equal(JsonValueKind.Number, member1.GetProperty("primaryReportingLineVersion").ValueKind);

        // Orphan106: null etag, null unit, empty leaderIds (a clean wire array).
        var orphan = employees.EnumerateArray().First(e => e.GetProperty("employeeId").GetString() == Orphan106);
        Assert.Equal(JsonValueKind.Null, orphan.GetProperty("primaryReportingLineVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, orphan.GetProperty("unitId").ValueKind);
        Assert.Empty(orphan.GetProperty("leaderIds").EnumerateArray());

        // The nameResolution envelope object resolves the upward-ref (EdgeMgr) by id; each entry carries
        // the resolved-ref field-set (camelCase, literally — userId/displayName/position/unitName).
        var nameResolution = body.GetProperty("nameResolution");
        Assert.Equal(JsonValueKind.Object, nameResolution.ValueKind);
        var edgeMgrRef = nameResolution.GetProperty(EdgeMgr);
        ContractAssert.HasFields(edgeMgrRef, "userId", "displayName", "position", "unitName");
        Assert.Equal("S106 EdgeMgr", edgeMgrRef.GetProperty("displayName").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task<long> SelectActivePrimaryVersionAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT version FROM reporting_lines WHERE employee_id = @emp AND relationship = 'PRIMARY' AND effective_to IS NULL",
            conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        var result = await cmd.ExecuteScalarAsync();
        return (long)result!;
    }

    private ApprovalPeriodRepository NewApprovalRepo()
    {
        var reportingRepo = new ReportingLineRepository(_dbFactory);
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, reportingRepo);
        return new ApprovalPeriodRepository(_dbFactory, authorizer, reportingRepo);
    }

    private static string MintAdminToken(string userId, string orgId)
    {
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalAdmin, orgId, "ORG_ONLY") };
        return NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalAdmin,
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
