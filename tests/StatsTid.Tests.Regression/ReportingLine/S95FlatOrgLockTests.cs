using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.ReportingLine;

/// <summary>
/// S95 / TASK-9506 (ADR-035 slice 4) — the FLAT-ORG lock + guard suite. Proves that retiring the
/// recursive tree-WALK (<c>ResolveOrganisationIdAsync</c> / <c>GetDescendantsAsync</c>) and replacing
/// the same-tree abstraction with a DIRECT <c>primary_org</c> equality
/// (<c>ValidateSameTreeAsync</c> → <c>ValidateSameOrganisationAsync</c>;
/// <c>CrossTreeAssignmentException</c> → <c>CrossOrganisationAssignmentException</c>) preserves the
/// EXACT serialization the tree-root advisory provided. The advisory DOMAIN is unchanged — it stays
/// the Organisation — and is now re-derived from <c>primary_org_id</c> directly; the
/// <c>reporting-org-{org}</c> lock prefix + the <c>organisation_id</c> columns are KEPT (deliberate).
///
/// <para><b>Coverage (the TASK-9506 interleave matrix):</b></para>
/// <list type="bullet">
///   <item>(a) a cross-Organisation PRIMARY assign + an admin-vikar are REJECTED — the repository
///         throws <see cref="CrossOrganisationAssignmentException"/>, and the endpoints map it to 400
///         (the existing byte-stable "samme styrelse" contract). RED-on-old at the symbol level: the
///         retired <c>ValidateSameTreeAsync</c>/<c>CrossTreeAssignmentException</c> no longer exist.</item>
///   <item>(b) the Organisation advisory (re-derived from <c>primary_org</c>) SERIALIZES a concurrent
///         first-assign 2-cycle: a held-lock interleave parks one leg on the
///         <c>reporting-org-{org}</c> key, and the reciprocal leg is then rejected by the cycle guard
///         under the SAME single Organisation lock (exactly one leg can ever be active). This is the
///         KEY equivalence claim — the lock is not weaker.</item>
///   <item>(e) the vikar REVOKE keys on the PERSISTED Organisation anchor
///         (<c>manager_vikar.organisation_id</c>) after the owning manager TRANSFERS to a different
///         Organisation — proven by holding the persisted-anchor advisory and showing the revoke
///         BLOCKS on it (S83 D19 revoke-safety, preserved verbatim under the re-keyed derivation).</item>
///   <item>(f) the ORGANISATION-HOME GUARD via the ENDPOINT (not raw SQL): user create POST + transfer
///         PUT REJECT a MAO <c>primary_org_id</c> (400) and ACCEPT an ORGANISATION (201 / 200).</item>
/// </list>
///
/// <para><b>Topology (init.sql seed orgs, S92/ADR-035 flatten):</b> MIN01/MIN02 = MAO; STY01/STY02/STY03
/// under MIN01, STY04/STY05 under MIN02 = ORGANISATION. Users sit on STY0x (the Organisation-home
/// invariant). STY02 and STY05 are different Organisations under different MAOs — the cross-Organisation
/// fixture. Endpoint-level via <see cref="StatsTidWebApplicationFactory"/>; direct repository + DB reads
/// for the lock + guard assertions. Idioms mirror <see cref="AdminVikarOnBehalfTests"/> and
/// <see cref="ReportingLineWriteLifecycleTests"/> (the side-conn / <c>pg_locks</c> waiter harness).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S95FlatOrgLockTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;
    private ReportingLineRepository _rlRepo = null!;
    private ManagerVikarRepository _vikarRepo = null!;

    // ── STY02 Organisation (under MAO MIN01) ──
    private const string Admin = "s95_admin";    // primary org MIN01 — LOCAL_ADMIN scope @ STY02 (covers STY02)
    private const string Mgr = "s95_mgr";        // STY02 — the absent manager (LOCAL_LEADER)
    private const string Emp = "s95_emp";        // STY02 — reports PRIMARY to Mgr
    private const string Vik = "s95_vik";        // STY02 — valid vikar, LOCAL_LEADER @ STY02 (covers Emp)
    private const string CycX = "s95_cyc_x";     // STY02 — no edges; 2-cycle leg X
    private const string CycY = "s95_cyc_y";     // STY02 — no edges; 2-cycle leg Y
    // ── STY05 Organisation (under a DIFFERENT MAO MIN02) ──
    private const string EmpCross = "s95_emp_cross"; // STY05 — a different Organisation
    // ── STY99 — an INACTIVE Organisation (S96 / TASK-9601), raw-seeded (no app path deactivates an org) ──
    private const string InactEmp = "s95_inact_emp"; // homed on the INACTIVE STY99
    private const string InactMgr = "s95_inact_mgr"; // same INACTIVE STY99 home — so the ONLY reject reason is the inactive org

    private const string OrgSty02 = "STY02";   // an ORGANISATION
    private const string OrgSty05 = "STY05";   // a DIFFERENT ORGANISATION
    private const string MaoMin01 = "MIN01";   // a MAO (NOT an employee home)
    private const string OrgInact = "STY99";   // an INACTIVE ORGANISATION (is_active = FALSE)

    private static readonly string[] AllUsers =
        { Admin, Mgr, Emp, Vik, CycX, CycY, EmpCross, InactEmp, InactMgr };

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        _vikarRepo = new ManagerVikarRepository(_dbFactory);
        _rlRepo = new ReportingLineRepository(_dbFactory, _vikarRepo);
        _ = _factory.CreateClient(); // boot seeders (the MAO→ORGANISATION seed tree + configs)

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
        // S96 / TASK-9601: a raw-seeded INACTIVE ORGANISATION (STY99 under MIN01, is_active = FALSE).
        // No application path deactivates an Organisation (no org-deactivation endpoint; the home guard
        // keeps users on ACTIVE Organisations), so the inactive-home pair MUST be seeded directly,
        // bypassing the guarded endpoints, to exercise the S96 is_active join guard.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version, is_active)
            VALUES ('STY99', 'S95/S96 Inactive Org', 'ORGANISATION', 'MIN01', '/MIN01/STY99/', 'HK', 'OK24', FALSE)
            ON CONFLICT (org_id) DO UPDATE SET is_active = FALSE
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@admin, @admin, '$2a$11$fake', 'S95 Admin', 's95_admin@test.dk', 'MIN01', 'AC', 'OK24', TRUE),
                (@mgr,   @mgr,   '$2a$11$fake', 'S95 Mgr',   's95_mgr@test.dk',   'STY02', 'HK', 'OK24', TRUE),
                (@emp,   @emp,   '$2a$11$fake', 'S95 Emp',   's95_emp@test.dk',   'STY02', 'HK', 'OK24', TRUE),
                (@vik,   @vik,   '$2a$11$fake', 'S95 Vik',   's95_vik@test.dk',   'STY02', 'HK', 'OK24', TRUE),
                (@cycx,  @cycx,  '$2a$11$fake', 'S95 CycX',  's95_cyc_x@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@cycy,  @cycy,  '$2a$11$fake', 'S95 CycY',  's95_cyc_y@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@cross, @cross, '$2a$11$fake', 'S95 Cross', 's95_cross@test.dk', 'STY05', 'HK', 'OK24', TRUE),
                -- Both inactive-home users sit on the INACTIVE STY99 (the user rows themselves are
                -- is_active = TRUE — the ONLY reason to reject the assign is the inactive HOME org).
                (@inactEmp, @inactEmp, '$2a$11$fake', 'S95 InactEmp', 's95_inact_emp@test.dk', 'STY99', 'HK', 'OK24', TRUE),
                (@inactMgr, @inactMgr, '$2a$11$fake', 'S95 InactMgr', 's95_inact_mgr@test.dk', 'STY99', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES
                -- The admin's covering scope keys directly on STY02 (S93 flat role-scope: exact
                -- ORG_ONLY membership); its USER primary org stays MIN01.
                (@admin, 'LOCAL_ADMIN',  'STY02', 'ORG_ONLY', 'TEST'),
                (@mgr,   'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@vik,   'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@emp,   'EMPLOYEE',     'STY02', 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Emp (STY02) reports PRIMARY to Mgr (STY02) — the manager's report (same Organisation).
        await _rlRepo.AssignAsync(null, MakeLine(Emp, Mgr));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("admin", Admin);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("vik", Vik);
        cmd.Parameters.AddWithValue("cycx", CycX);
        cmd.Parameters.AddWithValue("cycy", CycY);
        cmd.Parameters.AddWithValue("cross", EmpCross);
        cmd.Parameters.AddWithValue("inactEmp", InactEmp);
        cmd.Parameters.AddWithValue("inactMgr", InactMgr);
    }

    private static ReportingLineModel MakeLine(
        string employeeId, string managerId, string organisationId = OrgSty02) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        OrganisationId = organisationId,
        Relationship = "PRIMARY",
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        await ExecAsync(conn,
            """
            DELETE FROM audit_projection
            WHERE event_type IN ('ManagerVikarCreated','ManagerVikarEnded','ReportingLineAssigned','ReportingLineSuperseded','UserCreated','UserUpdated','EmployeeProfileCreated','UserAgreementCodeSeeded')
              AND (actor_id = ANY(@ids)
                   OR target_resource_id = ANY(@ids)
                   OR target_resource_id IN (
                       SELECT vikar_id::text FROM manager_vikar
                       WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)))
            """);
        await ExecStreamsAsync(conn);
        await ExecAsync(conn, "DELETE FROM manager_vikar WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)");
        await ExecAsync(conn,
            "DELETE FROM reporting_line_audit WHERE reporting_line_id IN (SELECT reporting_line_id FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids))");
        await ExecAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM users WHERE user_id = ANY(@ids)");
        // S96 — drop the raw-seeded inactive Organisation (after the homed users are gone; users FK it).
        await using (var orgCmd = new NpgsqlCommand("DELETE FROM organizations WHERE org_id = @org", conn))
        {
            orgCmd.Parameters.AddWithValue("org", OrgInact);
            await orgCmd.ExecuteNonQueryAsync();
        }

        async Task ExecAsync(NpgsqlConnection c, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.AddWithValue("ids", AllUsers);
            await cmd.ExecuteNonQueryAsync();
        }

        async Task ExecStreamsAsync(NpgsqlConnection c)
        {
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM outbox_events WHERE stream_id = ANY(@streams)", c);
            cmd.Parameters.AddWithValue("streams",
                AllUsers.SelectMany(id => new[] { $"reporting-line-{id}", $"user-{id}", $"employee-profile-{id}" }).ToArray());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (a) Cross-Organisation rejection — the retired same-tree abstraction is now the direct
    //      same-primary_org equality. The repository throws CrossOrganisationAssignmentException;
    //      the assign + admin-vikar endpoints map it to 400 (the byte-stable "styrelse" contract).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Repository: a cross-Organisation PRIMARY pair (Emp on STY02, EmpCross on STY05)
    /// throws <see cref="CrossOrganisationAssignmentException"/> — the direct primary_org equality
    /// (no tree walk).</summary>
    [Fact]
    public async Task ValidateSameOrganisation_CrossOrganisation_Throws()
    {
        await Assert.ThrowsAsync<CrossOrganisationAssignmentException>(
            () => _rlRepo.ValidateSameOrganisationAsync(Emp, EmpCross));
    }

    /// <summary>Repository: a SAME-Organisation pair (Emp + Mgr, both STY02) returns the common
    /// Organisation (= STY02, the value persisted into reporting_lines.organisation_id).</summary>
    [Fact]
    public async Task ValidateSameOrganisation_SameOrganisation_ReturnsTheOrganisation()
    {
        var org = await _rlRepo.ValidateSameOrganisationAsync(Emp, Mgr);
        Assert.Equal(OrgSty02, org);
    }

    /// <summary>Endpoint: a cross-Organisation admin PRIMARY assign (Emp@STY02 under EmpCross@STY05)
    /// → 400 and NO edge created. The in-tx ValidateSameOrganisationAsync rejects the cross-Organisation
    /// manager via CrossOrganisationAssignmentException → 400.</summary>
    [Fact]
    public async Task AdminAssign_CrossOrganisationManager_Returns400_NoEdge()
    {
        var client = TransferAdminClient(); // covers both STY02 + STY05 so the scope gate passes and
                                            // the same-Organisation guard is the decisive layer.

        // Emp ALREADY has an active PRIMARY edge (Emp → Mgr, seeded) → this is a REASSIGN, which the
        // endpoint requires the admin-strict If-Match "<currentVersion>" precondition for (checked
        // BEFORE the lock + the cross-Organisation guard; without it the endpoint 428s before the
        // same-Organisation check ever runs). Supply Emp's current PRIMARY edge version so the request
        // reaches the in-tx ValidateSameOrganisationAsync → CrossOrganisationAssignmentException → 400.
        var current = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Emp, "PRIMARY");
        var rsp = await PostAssignAsync(client, "/api/admin/reporting-lines", new
        {
            employeeId = Emp,
            managerId = EmpCross,
            effectiveFrom = "2026-06-01",
        }, ifMatchVersion: current!.Version);

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        // The pre-existing PRIMARY edge (Emp → Mgr) is untouched; no cross-Organisation edge created.
        var primary = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Emp, "PRIMARY");
        Assert.Equal(Mgr, primary!.ManagerId);
    }

    /// <summary>Endpoint: a cross-Organisation admin-vikar POST (Mgr@STY02, vikar EmpCross@STY05)
    /// → 400 and NO vikar created. The in-tx ValidateSameOrganisationAsync rejects the cross-Organisation
    /// vikar.</summary>
    [Fact]
    public async Task AdminVikarPost_CrossOrganisationVikar_Returns400_NoVikar()
    {
        // Give EmpCross a STY02-covering leader scope so the coverage census PASSES and the ONLY thing
        // that can reject is the same-Organisation guard (EmpCross's primary_org STY05 != Mgr's STY02).
        await GrantLeaderScopeAsync(EmpCross, OrgSty02);

        var client = AdminClient(Admin, "MIN01", OrgSty02);
        var rsp = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = EmpCross, effectiveTo = Today().AddDays(30).ToString("yyyy-MM-dd") });

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (b) The Organisation advisory (re-derived from primary_org) SERIALIZES a concurrent
    //      first-assign 2-cycle. This is the KEY equivalence claim: the lock is NOT weaker than the
    //      retired tree-root lock. We HOLD the STY02 Organisation advisory on a side connection, fire
    //      one leg of a would-be CycX↔CycY 2-cycle, and assert it BLOCKS on the lock (cannot pass by
    //      running sequentially). Release → the leg resolves (201); the reciprocal leg is then rejected
    //      by the cycle guard (409) under the SAME single Organisation lock — exactly one leg active.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OrganisationAdvisory_SerializesConcurrentFirstAssign2Cycle_CycleGuardFires()
    {
        var client = TransferAdminClient(); // covers STY02 (admin write gate).

        // 1. HOLD the STY02 Organisation advisory key on a side connection (the same key the endpoint
        //    derives from primary_org).
        var (holdConn, holdTx) = await AcquireOrgLockOnSideConnAsync(OrgSty02);
        var lockReleased = false;
        try
        {
            // 2. Fire leg A (CycX → CycY). CycX has NO active PRIMARY edge → a FIRST assign, which the
            //    endpoint requires the If-None-Match: * precondition for (checked BEFORE the lock; a raw
            //    POST 428s before ever reaching the held advisory). With the precondition it reaches and
            //    must BLOCK on the held advisory key.
            var legA = PostAssignAsync(client, "/api/admin/reporting-lines", new
            {
                employeeId = CycX,
                managerId = CycY,
                effectiveFrom = "2026-06-01",
            });

            // 3a. STRICT BARRIER: poll pg_locks until the endpoint backend is actually WAITING on OUR
            //     Organisation advisory key — proves it REACHED + BLOCKED on the lock (re-derived from
            //     primary_org), not that it is merely slow.
            Assert.True(await WaitForAdvisoryLockWaiterAsync(OrgSty02),
                "No backend was observed WAITING on the STY02 Organisation advisory — the assign did not serialize on the re-derived-from-primary_org lock (S95 equivalence broken).");

            // 3b. PROOF OF BLOCKING: parked while we hold the key.
            Assert.False(await Task.WhenAny(legA, Task.Delay(500)) == legA,
                "The assign completed while the Organisation advisory was held — it did not serialize on the lock.");

            // 4. Release the held lock → the parked assign acquires it and proceeds.
            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();
            lockReleased = true;

            var legAResp = await legA;
            Assert.Equal(HttpStatusCode.Created, legAResp.StatusCode);
        }
        finally
        {
            if (!lockReleased)
            {
                await holdTx.RollbackAsync();
                await holdConn.DisposeAsync();
            }
        }

        // 5. The reciprocal leg B (CycY → CycX) closes a cycle with the committed leg A → 409. It
        //    serialized through the SAME single Organisation key and its descendant walk SEES leg A's
        //    committed edge (ReadCommitted) — the cycle guard fires under the one Organisation lock.
        // CycY also has NO active PRIMARY edge → a FIRST assign needing If-None-Match: * (without it the
        // endpoint 428s before the cycle guard runs). With the precondition it reaches the guard → 409.
        var legB = await PostAssignAsync(client, "/api/admin/reporting-lines", new
        {
            employeeId = CycY,
            managerId = CycX,
            effectiveFrom = "2026-06-01",
        });
        Assert.Equal(HttpStatusCode.Conflict, legB.StatusCode);

        // Structural proof: never both reciprocal edges active.
        var xUnderY = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(CycX, "PRIMARY");
        var yUnderX = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(CycY, "PRIMARY");
        Assert.Equal(CycY, xUnderY!.ManagerId);
        Assert.Null(yUnderX); // leg B never committed.
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (e) The vikar REVOKE keys on the PERSISTED Organisation anchor (manager_vikar.organisation_id)
    //      after the owning manager TRANSFERS to a different Organisation (S83 D19). With persisted
    //      STY02 ≠ the manager's current STY05, hold the PERSISTED-anchor (STY02) advisory and show
    //      the admin-revoke BLOCKS on it (it locks the persisted root + the current root). RED on a
    //      variant that keyed only on the manager's CURRENT org (it would not block on STY02).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task VikarRevoke_KeysOnPersistedOrganisationAnchor_AfterManagerTransfer_BlocksOnIt()
    {
        // Plant a vikar with persisted Organisation anchor STY02, then TRANSFER the (still active)
        // manager to STY05 — now persisted(STY02) ≠ current(STY05).
        var vikar = await PlantVikarAsync(Mgr, Vik, Today().AddDays(30), OrgSty02);
        await TransferUserAsync(Mgr, OrgSty05);
        var restored = false;
        try
        {
            // Hold the PERSISTED-anchor (STY02) advisory on a side connection.
            var (sideConn, sideTx) = await AcquireOrgLockOnSideConnAsync(OrgSty02);
            var released = false;
            try
            {
                var client = AdminClient(Admin, "MIN01", OrgSty02);
                var deleteTask = client.DeleteAsync($"/api/admin/reporting-lines/{Mgr}/vikar");

                // The revoke must REACH + BLOCK on the held STY02 (persisted-anchor) advisory — proving
                // it keys on the persisted Organisation anchor, not just the manager's current org.
                Assert.True(await WaitForAdvisoryLockWaiterAsync(OrgSty02),
                    "The admin-revoke did not block on the STY02 (persisted-anchor) advisory — it did not key on the persisted Organisation anchor (S83 D19 not preserved under S95 re-keying).");
                Assert.False(await Task.WhenAny(deleteTask, Task.Delay(500)) == deleteTask,
                    "The admin-revoke completed while the STY02 persisted-anchor key was held — it did not serialize on the persisted Organisation anchor.");

                // Release STY02 → the revoke proceeds.
                await sideTx.RollbackAsync();
                await sideConn.DisposeAsync();
                released = true;

                var completed = await Task.WhenAny(deleteTask, Task.Delay(5000)) == deleteTask;
                Assert.True(completed, "The admin-revoke did not complete after the persisted-anchor key released.");
                var rsp = await deleteTask;
                Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
                Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
                Assert.True(await CountAsync(
                    "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'ManagerVikarEnded' AND target_resource_id = @id",
                    ("id", vikar.ToString())) == 1);
            }
            finally
            {
                if (!released) { await sideTx.RollbackAsync(); await sideConn.DisposeAsync(); }
            }
        }
        finally
        {
            await TransferUserAsync(Mgr, OrgSty02); // restore for cleanup symmetry.
            restored = true;
        }
        Assert.True(restored);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (f) The ORGANISATION-HOME GUARD via the ENDPOINT (not raw SQL). A user's primary_org must be an
    //      ORGANISATION (employees live on Organisations, not MAOs). The create POST + transfer PUT
    //      REJECT a MAO primary_org (400) and ACCEPT an ORGANISATION. A GlobalAdmin actor (GLOBAL
    //      scope) is used so the org-scope gate passes for BOTH a MAO and an ORGANISATION target — the
    //      home guard is then the decisive layer.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>User create POST: a MAO (MIN01) primary_org is REJECTED with 400 (the home guard). No
    /// user row created.</summary>
    [Fact]
    public async Task UserCreate_MaoPrimaryOrg_Returns400_NoUser()
    {
        var newId = "s95new_mao_" + Guid.NewGuid().ToString("N")[..8];
        var client = GlobalAdminClient();

        var rsp = await client.PostAsync("/api/admin/users", JsonContent.Create(new
        {
            userId = newId,
            username = newId,
            password = "password",
            displayName = "S95 MAO Reject",
            primaryOrgId = MaoMin01,   // a MAO — not a valid employee home
            agreementCode = "AC",
            okVersion = "OK24",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        Assert.Equal(0, await CountUsersAsync(newId));
    }

    /// <summary>User create POST: an ORGANISATION (STY02) primary_org is ACCEPTED → 201.</summary>
    [Fact]
    public async Task UserCreate_OrganisationPrimaryOrg_Returns201()
    {
        var newId = "s95new_org_" + Guid.NewGuid().ToString("N")[..8];
        var client = GlobalAdminClient();

        var rsp = await client.PostAsync("/api/admin/users", JsonContent.Create(new
        {
            userId = newId,
            username = newId,
            password = "password",
            displayName = "S95 Org Accept",
            primaryOrgId = OrgSty02,   // an ORGANISATION — a valid employee home
            agreementCode = "AC",
            okVersion = "OK24",
        }));

        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        Assert.Equal(1, await CountUsersAsync(newId));

        // Cleanup the created user.
        await DeleteUserAsync(newId);
    }

    /// <summary>Transfer PUT: moving an existing STY02 user to a MAO (MIN01) primary_org is REJECTED
    /// with 400 (the home guard). The user's home is unchanged.</summary>
    [Fact]
    public async Task UserTransfer_ToMaoPrimaryOrg_Returns400_HomeUnchanged()
    {
        var client = GlobalAdminClient();

        // Read the current version (ETag) for the If-Match precondition.
        var (etag, _) = await GetUserVersionAsync(client, Emp);

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{Emp}")
        {
            Content = JsonContent.Create(new { primaryOrgId = MaoMin01 }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", etag);
        var rsp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        // Emp's primary org is unchanged (still STY02).
        Assert.Equal(OrgSty02, await SelectPrimaryOrgAsync(Emp));
    }

    /// <summary>Transfer PUT: moving an existing STY02 user to a DIFFERENT ORGANISATION (STY05) is
    /// ACCEPTED → 200 (the home guard passes; the org-scope gate passes for a GlobalAdmin). The home
    /// changed.</summary>
    [Fact]
    public async Task UserTransfer_ToOrganisationPrimaryOrg_Returns200_HomeChanged()
    {
        var client = GlobalAdminClient();
        try
        {
            var (etag, _) = await GetUserVersionAsync(client, Emp);

            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{Emp}")
            {
                Content = JsonContent.Create(new { primaryOrgId = OrgSty05 }),
            };
            req.Headers.TryAddWithoutValidation("If-Match", etag);
            var rsp = await client.SendAsync(req);

            Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
            Assert.Equal(OrgSty05, await SelectPrimaryOrgAsync(Emp));
        }
        finally
        {
            // Restore Emp to STY02 directly (the edge cleanup runs in DisposeAsync).
            await TransferUserAsync(Emp, OrgSty02);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (g) S96 / TASK-9601 — the INACTIVE-ORG-HOME GUARD. S96 added
    //      "JOIN organizations o ON o.org_id = u.primary_org_id AND o.is_active = TRUE" to BOTH
    //      ValidateSameOrganisationAsync AND DeriveEmployeeTreeRootInTxAsync. So a user whose HOME
    //      Organisation is DEACTIVATED (organizations.is_active = FALSE) resolves to NOTHING — the
    //      method throws InvalidOperationException("...home Organisation inactive."), and the PRIMARY
    //      assign endpoint maps that to 400 (NO edge created). This state is UNREACHABLE via the app
    //      (no org-deactivation endpoint; the home guard keeps users on active Organisations), so the
    //      inactive org + its homed users are raw-SQL-seeded (bypassing the guarded endpoints).
    //
    //      RED-on-old: pre-S96 the JOIN did not exist — the inactive-home user resolved normally from
    //      users.primary_org_id alone, so InactEmp + InactMgr (same STY99 home, both is_active = TRUE
    //      user rows) would pass the same-Organisation check and the FIRST assign would COMMIT (201),
    //      failing BOTH assertions below (the throw expectation and the 400/no-edge expectation).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Repository (unit-level pin, no endpoint exception-mapping dependency): an inactive-home
    /// pair (InactEmp + InactMgr, both homed on the DEACTIVATED STY99) throws
    /// <see cref="InvalidOperationException"/> — the S96 <c>is_active = TRUE</c> join on
    /// <c>organizations</c> filters the inactive-home row out, so it resolves to nothing.</summary>
    [Fact]
    public async Task ValidateSameOrganisation_InactiveHomeOrg_ThrowsInvalidOperation()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rlRepo.ValidateSameOrganisationAsync(InactEmp, InactMgr));
        Assert.Contains("home Organisation inactive", ex.Message);
    }

    /// <summary>Endpoint: a FIRST PRIMARY assign for a user whose HOME Organisation is INACTIVE
    /// (InactEmp@STY99 under InactMgr@STY99) is REJECTED and NO edge created.
    ///
    /// <para><b>Observed status = 403 Forbidden, NOT the S96 in-tx 400.</b> The PRIMARY-assign endpoint
    /// runs the org-scope gate (<c>OrgScopeValidator.ValidateEmployeeAccessAsync</c>) FIRST, and that
    /// gate resolves the employee's org via <c>OrganizationRepository.GetByIdAsync</c>, which already
    /// filters <c>is_active = TRUE</c> (a PRE-EXISTING filter, NOT the S96 change). So the inactive
    /// STY99 home resolves to null → "Target organization not found" → 403, BEFORE the request ever
    /// reaches the S96 same-Organisation / tree-root lock guard. This is still a faithful
    /// "rejected, no edge" proof at the endpoint layer; the S96-SPECIFIC repository guard is pinned by
    /// the sibling <see cref="ValidateSameOrganisation_InactiveHomeOrg_ThrowsInvalidOperation"/> (the
    /// unit-level test that exercises exactly the changed method).</para>
    ///
    /// <para>The actual status is ASSERTED (not assumed): the endpoint maps the pre-gate org-resolution
    /// miss to 403 — both pre- and post-S96. The S96 RED-on-old proof lives in the repository test.</para></summary>
    [Fact]
    public async Task AdminAssign_InactiveHomeOrg_Rejected_NoEdge()
    {
        // A GlobalAdmin so the actor's SCOPE cannot be the reject reason (GLOBAL covers every org).
        var client = GlobalAdminClient();

        // InactEmp has NO active PRIMARY edge → a FIRST assign (If-None-Match: *).
        var rsp = await PostAssignAsync(client, "/api/admin/reporting-lines", new
        {
            employeeId = InactEmp,
            managerId = InactMgr,
            effectiveFrom = "2026-06-01",
        });

        // The ACTUAL observed status: the org-scope gate's GetByIdAsync(is_active = TRUE) resolves the
        // inactive STY99 home to null → "Target organization not found" → 403 (a pre-S96 filter, fired
        // BEFORE the S96 in-tx guard). A faithful "rejected" outcome; the assert reflects reality.
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        // No edge was created for the inactive-home user.
        Assert.Null(await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(InactEmp, "PRIMARY"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — clients / tokens
    // ════════════════════════════════════════════════════════════════════════════════

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private HttpClient AdminClient(string userId, string orgId, string? scopeOrg = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintToken(userId, orgId, StatsTidRoles.LocalAdmin, scopeOrg));
        return client;
    }

    /// <summary>A both-Organisation (STY02 + STY05) LocalAdmin so an admin assign over a cross-Organisation
    /// pair passes the org-scope gate and the same-Organisation guard is the decisive layer.</summary>
    private HttpClient TransferAdminClient()
    {
        var tokenService = NewTokenService();
        var scopes = new[]
        {
            new RoleScope(StatsTidRoles.LocalAdmin, OrgSty02, "ORG_ONLY"),
            new RoleScope(StatsTidRoles.LocalAdmin, OrgSty05, "ORG_ONLY"),
        };
        var bearer = tokenService.GenerateToken(
            employeeId: "s95_xadmin", name: "s95_xadmin", role: StatsTidRoles.LocalAdmin,
            agreementCode: "HK", orgId: OrgSty02, scopes: scopes);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    /// <summary>A GlobalAdmin (GLOBAL scope, org_id null) — covers every org so the org-scope gate
    /// passes for a MAO and an ORGANISATION target alike, isolating the Organisation-home guard.</summary>
    private HttpClient GlobalAdminClient()
    {
        var tokenService = NewTokenService();
        var bearer = tokenService.GenerateToken(
            employeeId: "s95_gadmin", name: "s95_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private string MintToken(string userId, string orgId, string role, string? scopeOrg = null)
    {
        var tokenService = NewTokenService();
        var scopes = new[] { new RoleScope(role, scopeOrg ?? orgId, "ORG_ONLY") };
        return tokenService.GenerateToken(
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

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers — DB
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task GrantLeaderScopeAsync(string userId, string orgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@id, 'LOCAL_LEADER', @org, 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("org", orgId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> PlantVikarAsync(string absentApprover, string vikarUser, DateOnly untilDate, string treeRoot)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var v = await _vikarRepo.CreateAsync(conn, tx, new ManagerVikar
        {
            VikarId = Guid.NewGuid(),
            AbsentApproverId = absentApprover,
            VikarUserId = vikarUser,
            UntilDate = untilDate,
            Reason = "ANDET",
            OrganisationId = treeRoot,
            Version = 1,
            CreatedBy = "TEST",
        });
        await tx.CommitAsync();
        return v.VikarId;
    }

    /// <summary>An out-of-band Organisation transfer (a raw primary_org_id UPDATE — the move the
    /// unlocked pre-acquire derive cannot pre-see). The owning test restores STY02 in its finally.</summary>
    private async Task TransferUserAsync(string userId, string newPrimaryOrgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET primary_org_id = @org WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("org", newPrimaryOrgId);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string?> SelectPrimaryOrgAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT primary_org_id FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (await cmd.ExecuteScalarAsync()) as string;
    }

    private async Task<long> CountUsersAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task DeleteUserAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM audit_projection WHERE target_resource_id = @id OR actor_id = @id",
            "DELETE FROM outbox_events WHERE stream_id IN (@u, @ep)",
            "DELETE FROM employee_profiles WHERE employee_id = @id",
            "DELETE FROM user_agreement_codes WHERE user_id = @id",
            "DELETE FROM role_assignments WHERE user_id = @id",
            "DELETE FROM users WHERE user_id = @id",
        })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", userId);
            if (sql.Contains("@u")) cmd.Parameters.AddWithValue("u", $"user-{userId}");
            if (sql.Contains("@ep")) cmd.Parameters.AddWithValue("ep", $"employee-profile-{userId}");
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Reads a user's current ETag (the version, quoted) via the GET endpoint so the transfer
    /// PUT's admin-strict If-Match precondition is satisfiable.</summary>
    private async Task<(string Etag, string Body)> GetUserVersionAsync(HttpClient client, string userId)
    {
        var rsp = await client.GetAsync($"/api/admin/users/{userId}");
        rsp.EnsureSuccessStatusCode();
        var body = await rsp.Content.ReadAsStringAsync();
        var etag = rsp.Headers.ETag?.Tag;
        if (string.IsNullOrEmpty(etag))
        {
            // Fall back to the version field in the body (quoted per the admin-strict If-Match contract).
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var version = doc.RootElement.GetProperty("version").GetInt64();
            etag = $"\"{version}\"";
        }
        return (etag!, body);
    }

    private async Task<long> CountAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// POSTs a JSON body to the PRIMARY-assign endpoint with the optimistic-concurrency precondition the
    /// endpoint REQUIRES before the lock + the cross-Organisation guard (mirrors
    /// <c>ReportingLineWriteLifecycleTests.PostAssignAsync</c>; <see cref="HttpClientJsonExtensions.PostAsJsonAsync"/>
    /// cannot set the header, so a raw POST 428s). A FIRST assign (no active PRIMARY edge) uses
    /// <c>If-None-Match: *</c>; a REASSIGN (an active edge exists) uses <c>If-Match: "&lt;version&gt;"</c>
    /// (the admin-strict quoted-ETag contract).
    /// </summary>
    private static async Task<HttpResponseMessage> PostAssignAsync(
        HttpClient client, string url, object body, long? ifMatchVersion = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        if (ifMatchVersion is null)
            req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        else
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchVersion}\"");
        return await client.SendAsync(req);
    }

    // ── Held-lock interleave harness (mirrors ReportingLineWriteLifecycleTests / AdminVikarOnBehalfTests).
    //    The advisory key prefix is the KEPT 'reporting-org-' (S95: the prefix is unchanged; only the
    //    DERIVATION changed — read from primary_org instead of the recursive walk). The key is the
    //    Organisation (= the user's primary_org). ──

    /// <summary>Holds the <c>reporting-org-{org}</c> xact advisory key (the Organisation lock) on a SIDE
    /// connection so a test can deterministically BLOCK any in-Organisation mutator that takes the same
    /// key, until the returned tx is rolled back. The caller owns disposal.</summary>
    private async Task<(NpgsqlConnection conn, NpgsqlTransaction tx)> AcquireOrgLockOnSideConnAsync(string organisationId)
    {
        var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using (var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('reporting-org-' || @org))", conn, tx))
        {
            cmd.Parameters.AddWithValue("org", organisationId);
            await cmd.ExecuteScalarAsync();
        }
        return (conn, tx);
    }

    /// <summary>Polls <c>pg_locks</c> (joined to <c>pg_stat_activity</c> to exclude this session) until at
    /// least one OTHER backend is WAITING (<c>granted = false</c>) on the <c>reporting-org-{org}</c>
    /// advisory key — proving a request actually REACHED and BLOCKED ON THE LOCK. Returns <c>true</c> once
    /// a waiter is seen; <c>false</c> on timeout.</summary>
    private async Task<bool> WaitForAdvisoryLockWaiterAsync(string organisationId, int timeoutMs = 5000)
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
                          = hashtext('reporting-org-' || @org)::bigint
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
}
