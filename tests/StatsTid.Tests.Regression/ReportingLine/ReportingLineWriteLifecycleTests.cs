using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.ReportingLine;

/// <summary>
/// S74 / ADR-027 (SPRINT-74 R8/R9/R10, TASK-7403) — the WRITE LIFECYCLE: the write-time cycle
/// guard, the atomic create-person-with-approver, and the no-orphans delete-with-reassignment.
///
/// <para>
/// <b>R8 (cycle guard):</b> both assign endpoints REJECT (409) an approver that is the employee
/// or a descendant; a tree-wide <c>pg_advisory_xact_lock</c> serializes a tree's assigns through
/// the bounded descendant walk so concurrent FIRST assignments cannot each form half a cycle.
/// </para>
/// <para>
/// <b>R9 (atomic create+assign):</b> the create tx, given an <c>approverId</c>, ALSO creates the
/// new person's PRIMARY edge under it — user + profile + agreement + edge + all events/audit
/// commit atomically, or a forced rollback leaves NONE; a cyclic/cross-tree approver → 4xx and
/// nothing committed.
/// </para>
/// <para>
/// <b>R10 (delete-with-reassignment, ADR-027 D9 = no orphans):</b> removing a manager-with-reports
/// WITHOUT replacements → 409 + zero mutation; WITH replacements → reports reassigned + all the
/// removed person's edges closed (both directions) + their vikar rows (both sides) closed + user
/// deactivated, all atomic; a forced rollback leaves the ORIGINAL state intact; no orphan.
/// </para>
///
/// <para>
/// Topology (init.sql seed orgs, S92/ADR-035 flatten): MIN01 (MAO) has Organisation STY02; MIN02 (MAO)
/// has Organisation STY05 — a different tree_root. Each Organisation is its own tree root (the former
/// former sub-org rows are gone; cross-tree fixtures move between Organisations). Endpoint-level via
/// <see cref="StatsTidWebApplicationFactory"/>; direct repository assertions for the forced-rollback
/// atomicity (the endpoint lambda cannot be invoked here, mirroring the TASK-7401 pattern).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ReportingLineWriteLifecycleTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;
    private ReportingLineRepository _rlRepo = null!;
    private ManagerVikarRepository _vikarRepo = null!;
    private PostgresEventStore _outbox = null!;
    private AuditProjectionRepository _auditRepo = null!;

    // ── STY02 Organisation users ──
    private const string Top = "t743_top";          // STY02 — top manager (root-ish, scope cover)
    private const string Mgr = "t743_mgr";          // STY02 — reports to Top; manager of Emp/Emp2
    private const string Emp = "t743_emp";          // STY02 — reports PRIMARY to Mgr
    private const string Emp2 = "t743_emp2";        // STY02 — reports PRIMARY to Mgr
    private const string Sub = "t743_sub";          // STY02 — reports PRIMARY to Emp (Emp's descendant)
    private const string Repl = "t743_repl";        // STY02 — a valid replacement approver
    private const string NewGuy = "t743_new";       // STY02 — created fresh in R9 tests (not pre-seeded)
    private const string CycX = "t743_cyc_x";       // STY02 — no edges; W2 concurrent-cycle leg X
    private const string CycY = "t743_cyc_y";       // STY02 — no edges; W2 concurrent-cycle leg Y
    private const string LateRep = "t743_late_rep"; // STY02 — no edges; W2 concurrent assign-vs-delete report
    // ── STY05 (cross-tree Organisation under a DIFFERENT MAO MIN02) ──
    private const string MgrX = "t743_mgr_x";       // STY05 — different tree_root

    private const string TreeRootSty02 = "STY02";

    private static readonly string[] AllUsers =
        { Top, Mgr, Emp, Emp2, Sub, Repl, NewGuy, CycX, CycY, LateRep, MgrX };

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        _vikarRepo = new ManagerVikarRepository(_dbFactory);
        _rlRepo = new ReportingLineRepository(_dbFactory, _vikarRepo);
        _outbox = new PostgresEventStore(_dbFactory, new OutboxServiceContext("backend-api"));
        _auditRepo = new AuditProjectionRepository(_dbFactory);

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
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@top,  @top,  '$2a$11$fake', 'T743 Top',  't743_top@test.dk',  'STY02', 'HK', 'OK24', TRUE),
                (@mgr,  @mgr,  '$2a$11$fake', 'T743 Mgr',  't743_mgr@test.dk',  'STY02', 'HK', 'OK24', TRUE),
                (@emp,  @emp,  '$2a$11$fake', 'T743 Emp',  't743_emp@test.dk',  'STY02', 'HK', 'OK24', TRUE),
                (@emp2, @emp2, '$2a$11$fake', 'T743 Emp2', 't743_emp2@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@sub,  @sub,  '$2a$11$fake', 'T743 Sub',  't743_sub@test.dk',  'STY02', 'HK', 'OK24', TRUE),
                (@repl, @repl, '$2a$11$fake', 'T743 Repl', 't743_repl@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@cycx, @cycx, '$2a$11$fake', 'T743 CycX', 't743_cyc_x@test.dk','STY02', 'HK', 'OK24', TRUE),
                (@cycy, @cycy, '$2a$11$fake', 'T743 CycY', 't743_cyc_y@test.dk','STY02', 'HK', 'OK24', TRUE),
                (@late, @late, '$2a$11$fake', 'T743 Late', 't743_late@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@mgrx, @mgrx, '$2a$11$fake', 'T743 MgrX', 't743_mgr_x@test.dk','STY05', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Admin actor scope: Top is a LOCAL_ADMIN over STY02 (covers the whole STY02 Organisation) so the admin
        // endpoints (LocalAdminOrAbove) pass org-scope on every test user.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@top, 'LOCAL_ADMIN', 'STY02', 'ORG_AND_DESCENDANTS', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("top", Top);
            await cmd.ExecuteNonQueryAsync();
        }

        // Hierarchy: Mgr → Top; Emp → Mgr; Emp2 → Mgr; Sub → Emp (so Sub is Emp's descendant).
        await _rlRepo.AssignAsync(null, MakeLine(Mgr, Top));
        await _rlRepo.AssignAsync(null, MakeLine(Emp, Mgr));
        await _rlRepo.AssignAsync(null, MakeLine(Emp2, Mgr));
        await _rlRepo.AssignAsync(null, MakeLine(Sub, Emp));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("top", Top);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("emp2", Emp2);
        cmd.Parameters.AddWithValue("sub", Sub);
        cmd.Parameters.AddWithValue("repl", Repl);
        cmd.Parameters.AddWithValue("cycx", CycX);
        cmd.Parameters.AddWithValue("cycy", CycY);
        cmd.Parameters.AddWithValue("late", LateRep);
        cmd.Parameters.AddWithValue("mgrx", MgrX);
    }

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        var idsPlusNew = AllUsers; // includes NewGuy

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
        await ExecAsync(conn,
            "DELETE FROM outbox_events WHERE stream_id = ANY(@streams)", c =>
            {
                c.Parameters.AddWithValue("streams",
                    idsPlusNew.SelectMany(id => new[] { $"reporting-line-{id}", $"user-{id}", $"employee-profile-{id}" }).ToArray());
            });
        await ExecAsync(conn, "DELETE FROM manager_vikar WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)");
        await ExecAsync(conn,
            "DELETE FROM reporting_line_audit WHERE reporting_line_id IN (SELECT reporting_line_id FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids))");
        await ExecAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM users_audit WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM employee_profile_audit WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes_audit WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM users WHERE user_id = ANY(@ids)");

        async Task ExecAsync(NpgsqlConnection c, string sql, Action<NpgsqlCommand>? extra = null)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            if (extra is null)
                cmd.Parameters.AddWithValue("ids", idsPlusNew);
            else
                extra(cmd);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static ReportingLineModel MakeLine(
        string employeeId, string managerId, string relationship = "PRIMARY") => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        TreeRootOrgId = TreeRootSty02,
        Relationship = relationship,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    // ════════════════════════════════════════════════════════════════════════════════
    //  R8 — write-time cycle guard (BOTH assign paths)
    // ════════════════════════════════════════════════════════════════════════════════

    // A cyclic PRIMARY assign — make Mgr report to Sub (Sub is Mgr's descendant via Emp) → 409.
    [Fact]
    public async Task R8_CyclicPrimaryAssign_IsRejected_409()
    {
        var client = AdminClient();
        // Mgr → ... → Emp → Sub: Sub is a descendant of Mgr. Assigning Mgr UNDER Sub closes a loop.
        var rsp = await PostAssignAsync(client, "/api/admin/reporting-lines", new
        {
            employeeId = Mgr,
            managerId = Sub,
            effectiveFrom = "2026-06-01",
        });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("cycle", body.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    // A cyclic ACTING assign — Emp's ACTING manager = Sub (Emp's own descendant) → 409.
    [Fact]
    public async Task R8_CyclicActingAssign_IsRejected_409()
    {
        var client = AdminClient();
        var rsp = await PostAssignAsync(client, $"/api/admin/reporting-lines/{Emp}/acting", new
        {
            managerId = Sub,
            effectiveFrom = "2026-06-01",
        });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
    }

    // Self-assign (manager == employee) — DB CHECK-blocked, but the guard returns a friendly 409.
    [Fact]
    public async Task R8_SelfAssign_IsRejected_409_Friendly()
    {
        var client = AdminClient();
        var rsp = await PostAssignAsync(client, "/api/admin/reporting-lines", new
        {
            employeeId = Emp,
            managerId = Emp,
            effectiveFrom = "2026-06-01",
        });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("own manager", body.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    // A legitimate (non-cyclic) deep assignment still succeeds — Repl under Top (no loop).
    [Fact]
    public async Task R8_LegitimateAssign_StillSucceeds()
    {
        var client = AdminClient();
        var rsp = await PostAssignAsync(client, "/api/admin/reporting-lines", new
        {
            employeeId = Repl,
            managerId = Top,
            effectiveFrom = "2026-06-01",
        });

        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        var active = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Repl, "PRIMARY");
        Assert.NotNull(active);
        Assert.Equal(Top, active!.ManagerId);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  R9 — atomic create-person-with-approver
    // ════════════════════════════════════════════════════════════════════════════════

    // Create-with-approver: user + profile + agreement + PRIMARY edge + events all commit atomically.
    [Fact]
    public async Task R9_CreateWithApprover_CreatesUser_Profile_Agreement_AndPrimaryEdge_Atomically()
    {
        var client = AdminClient();
        var rsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId = NewGuy,
            username = NewGuy,
            password = "password",
            displayName = "T743 New",
            email = "t743_new@test.dk",
            primaryOrgId = "STY02",
            agreementCode = "HK",
            okVersion = "OK24",
            approverId = Mgr,    // R9 — atomic create + assign under Mgr
        });

        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        // User row exists + active.
        Assert.True(await UserIsActiveAsync(NewGuy));
        // Profile row exists.
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM employee_profiles WHERE employee_id = @id AND effective_to IS NULL", NewGuy));
        // Agreement-code row exists.
        Assert.True(await CountAsync("SELECT COUNT(*) FROM user_agreement_codes WHERE user_id = @id AND effective_to IS NULL", NewGuy) >= 1);
        // PRIMARY reporting edge under Mgr.
        var active = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(NewGuy, "PRIMARY");
        Assert.NotNull(active);
        Assert.Equal(Mgr, active!.ManagerId);
        // A reporting_line_audit ASSIGNED row landed for the edge.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM reporting_line_audit WHERE reporting_line_id = @id::uuid AND action = 'ASSIGNED'",
            active.ReportingLineId.ToString()));
        // The ReportingLineAssigned outbox event landed on the new person's stream.
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @id AND event_type = 'ReportingLineAssigned'",
            $"reporting-line-{NewGuy}") >= 1);
    }

    // Create with a CROSS-TREE approver → 400 and NOTHING committed (no user, no edge).
    [Fact]
    public async Task R9_CreateWithCrossTreeApprover_Is4xx_AndNothingCommitted()
    {
        var client = AdminClient();
        var rsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId = NewGuy,
            username = NewGuy,
            password = "password",
            displayName = "T743 New",
            primaryOrgId = "STY02",
            agreementCode = "HK",
            okVersion = "OK24",
            approverId = MgrX,    // STY05 — different reporting tree
        });

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        // Atomicity: the whole create rolled back — no user row, no edge.
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM users WHERE user_id = @id", NewGuy));
        Assert.Null(await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(NewGuy, "PRIMARY"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM employee_profiles WHERE employee_id = @id", NewGuy));
    }

    // Create with a self-referencing approver (approverId == userId) → 409, nothing committed.
    [Fact]
    public async Task R9_CreateWithSelfApprover_Is4xx_AndNothingCommitted()
    {
        var client = AdminClient();
        var rsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId = NewGuy,
            username = NewGuy,
            password = "password",
            displayName = "T743 New",
            primaryOrgId = "STY02",
            agreementCode = "HK",
            okVersion = "OK24",
            approverId = NewGuy,   // self-cycle
        });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM users WHERE user_id = @id", NewGuy));
    }

    // Create WITHOUT approver → behaviour unchanged (user created, NO reporting edge).
    [Fact]
    public async Task R9_CreateWithoutApprover_CreatesUser_NoEdge_Unchanged()
    {
        var client = AdminClient();
        var rsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId = NewGuy,
            username = NewGuy,
            password = "password",
            displayName = "T743 New",
            primaryOrgId = "STY02",
            agreementCode = "HK",
            okVersion = "OK24",
            // no approverId
        });

        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        Assert.True(await UserIsActiveAsync(NewGuy));
        Assert.Null(await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(NewGuy, "PRIMARY"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  R10 — delete-with-reassignment (NO orphans)
    // ════════════════════════════════════════════════════════════════════════════════

    // Removing a manager-with-reports WITHOUT replacements → 409 + ZERO mutation.
    [Fact]
    public async Task R10_RemoveManagerWithReports_NoReplacements_409_ZeroMutation()
    {
        var client = AdminClient();
        // Mgr has reports Emp + Emp2 (PRIMARY) — removing without replacements must 409.
        var rsp = await client.PostAsJsonAsync($"/api/admin/reporting-lines/{Mgr}/remove", new
        {
            replacements = new Dictionary<string, string>(),
        });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var needing = body.GetProperty("reportsNeedingReassignment").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        Assert.Contains(Emp, needing);
        Assert.Contains(Emp2, needing);

        // ZERO mutation — Mgr still active, edges intact.
        Assert.True(await UserIsActiveAsync(Mgr));
        Assert.Equal(Mgr, (await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Emp, "PRIMARY"))!.ManagerId);
        Assert.Equal(Mgr, (await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Emp2, "PRIMARY"))!.ManagerId);
    }

    // WITH replacements → reports reassigned + all removed person's edges closed (both directions)
    // + vikar rows (both sides) closed + user deactivated, all atomic + NO orphan.
    [Fact]
    public async Task R10_RemoveManagerWithReplacements_FullClosure_Atomic_NoOrphan()
    {
        // Give Mgr an active vikar (Mgr is absent_approver) AND make Mgr a vikar for someone
        // (Top is absent_approver, Mgr is the stand-in) → BOTH sides must be closed by step 4.
        await CreateVikarAsync(Mgr, Repl, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)); // Mgr = absent
        await CreateVikarAsync(Top, Mgr, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));  // Mgr = vikar

        var client = AdminClient();
        var rsp = await client.PostAsJsonAsync($"/api/admin/reporting-lines/{Mgr}/remove", new
        {
            replacements = new Dictionary<string, string> { [Emp] = Top, [Emp2] = Top },
        });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // (1) Reports reassigned to Top — no orphan, every report has an active PRIMARY approver.
        var empPrimary = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Emp, "PRIMARY");
        var emp2Primary = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Emp2, "PRIMARY");
        Assert.NotNull(empPrimary);
        Assert.NotNull(emp2Primary);
        Assert.Equal(Top, empPrimary!.ManagerId);
        Assert.Equal(Top, emp2Primary!.ManagerId);

        // (3) The removed person's OWN outgoing edge (Mgr → Top PRIMARY) is closed.
        Assert.Null(await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Mgr, "PRIMARY"));

        // (4) BOTH vikar rows closed (Mgr as absent approver AND Mgr as the stand-in).
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));     // absent side
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Top));     // Mgr was Top's vikar
        // The reverse lookup finds no active row naming Mgr as a stand-in either.
        Assert.Empty(await _vikarRepo.GetActiveByVikarUserAsync(Mgr));

        // (5) User deactivated.
        Assert.False(await UserIsActiveAsync(Mgr));

        // NO ORPHAN: no active PRIMARY report points at the now-inactive Mgr.
        Assert.Empty(await _rlRepo.GetDirectReportsAsync(Mgr));

        // ManagerVikarEnded audit_projection rows landed for the closed vikars (APPROVER_REMOVED).
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'ManagerVikarEnded' AND details::text LIKE '%APPROVER_REMOVED%'") >= 2);

        // reporting_line_audit: each reassigned report produced a SUPERSEDED (old edge) + an
        // ASSIGNED (new edge) row, written via the actual endpoint choreography.
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM reporting_line_audit a JOIN reporting_lines rl ON rl.reporting_line_id = a.reporting_line_id WHERE rl.employee_id = @id AND a.action = 'ASSIGNED'",
            Emp) >= 1);
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM reporting_line_audit a JOIN reporting_lines rl ON rl.reporting_line_id = a.reporting_line_id WHERE rl.employee_id = @id AND a.action = 'ASSIGNED'",
            Emp2) >= 1);

        // users_audit: the soft-deactivation wrote an UPDATED row flagged removedViaReassignment.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM users_audit WHERE user_id = @id AND action = 'UPDATED' AND new_data::text LIKE '%removedViaReassignment%'",
            Mgr));

        // S74-7403 B5: the UserUpdated soft-deactivate carries an ADR-026 audit_projection row
        // (TENANT_TARGETED) — previously emitted via a plain EnqueueAsync with NO projection row.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'UserUpdated' AND target_resource_id = @id",
            Mgr));
    }

    // R10 also closes incoming ACTING edges (manager_id = removed) — no replacement, just ends.
    [Fact]
    public async Task R10_Remove_ClosesIncomingActingEdges()
    {
        // Give Emp2 an ACTING manager = Mgr (an incoming ACTING edge on the removed person).
        await _rlRepo.AssignAsync(null, MakeLine(Emp2, Mgr, "ACTING"));

        var client = AdminClient();
        var rsp = await client.PostAsJsonAsync($"/api/admin/reporting-lines/{Mgr}/remove", new
        {
            replacements = new Dictionary<string, string> { [Emp] = Top, [Emp2] = Top },
        });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // Emp2's ACTING edge (held by Mgr) is closed.
        Assert.Null(await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Emp2, "ACTING"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Forced-rollback atomicity (ADR-018 D3) — REAL: state AND outbox AND audit all gone.
    // ════════════════════════════════════════════════════════════════════════════════

    // Replays the R10 closure choreography directly, then ROLLS BACK — assert NOTHING survived:
    // the reassignment edge, the vikar close, the deactivation; the outbox events; the audit rows.
    [Fact]
    public async Task R10_ForcedRollback_LeavesOriginalStateIntact_NoState_NoEvent_NoAudit()
    {
        // A pre-existing vikar (Mgr absent) to close inside the rolled-back tx.
        var vikar = await CreateVikarAsync(Mgr, Repl, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        var empPrimaryBefore = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Emp, "PRIMARY");
        Assert.Equal(Mgr, empPrimaryBefore!.ManagerId);

        var newEdgeId = Guid.NewGuid();
        await using (var conn = _dbFactory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // (1) reassign Emp → Top (supersede Mgr-held PRIMARY).
            await ReportingLineRepository.AcquireTreeLockAsync(conn, tx, TreeRootSty02);
            await _rlRepo.GuardNoCycleAsync(conn, tx, Emp, Top);
            var persisted = await _rlRepo.AssignAsync(conn, tx, empPrimaryBefore.Version, new ReportingLineModel
            {
                ReportingLineId = newEdgeId,
                EmployeeId = Emp,
                ManagerId = Top,
                TreeRootOrgId = TreeRootSty02,
                Relationship = "PRIMARY",
                EffectiveFrom = today,
                Source = "MANUAL",
                Version = 1,
                CreatedBy = "TEST",
            });
            await _outbox.EnqueueAsync(conn, tx, $"reporting-line-{Emp}", new ReportingLineAssigned
            {
                ReportingLineId = persisted.ReportingLineId,
                EmployeeId = Emp,
                ManagerId = Top,
                TreeRootOrgId = TreeRootSty02,
                Relationship = "PRIMARY",
                EffectiveFrom = today,
                Source = "MANUAL",
                RowVersion = persisted.Version,
                ActorId = Top,
                ActorRole = "LocalAdmin",
            });

            // (4) close the vikar + ManagerVikarEnded audit trio.
            var closed = await _vikarRepo.CloseAsync(conn, tx, vikar.VikarId, today);
            var endedEvent = new ManagerVikarEnded
            {
                VikarId = closed!.VikarId,
                AbsentApproverId = closed.AbsentApproverId,
                VikarUserId = closed.VikarUserId,
                UntilDate = closed.UntilDate,
                Reason = closed.Reason,
                TreeRootOrgId = closed.TreeRootOrgId,
                EffectiveTo = closed.EffectiveTo!.Value,
                EndReason = "APPROVER_REMOVED",
                RowVersion = closed.Version,
                ActorId = Top,
                ActorRole = "LocalAdmin",
            };
            var outboxId = await _outbox.EnqueueAndReturnIdAsync(conn, tx, $"reporting-line-{Mgr}", endedEvent);
            var ctx = new AuditProjectionContext(
                ActorId: Top,
                ActorPrimaryOrgId: TreeRootSty02,
                CorrelationId: endedEvent.CorrelationId,
                OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(endedEvent.OccurredAt, DateTimeKind.Utc)),
                ResolvedTargetOrgId: endedEvent.TreeRootOrgId);
            var rowData = new ManagerVikarEndedAuditMapper().Map(endedEvent, ctx);
            await _auditRepo.InsertAsync(conn, tx, endedEvent.EventId, outboxId, endedEvent.EventType, rowData, ctx);

            // (5) deactivate Mgr.
            await using (var deact = new NpgsqlCommand(
                "UPDATE users SET is_active = FALSE WHERE user_id = @id", conn, tx))
            {
                deact.Parameters.AddWithValue("id", Mgr);
                await deact.ExecuteNonQueryAsync();
            }

            await tx.RollbackAsync(); // FORCED ROLLBACK — nothing must survive.
        }

        // STATE: original intact — Emp still under Mgr; the new edge never persisted; vikar still
        // active; Mgr still active.
        var empPrimaryAfter = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Emp, "PRIMARY");
        Assert.Equal(Mgr, empPrimaryAfter!.ManagerId);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM reporting_lines WHERE reporting_line_id = @id::uuid", newEdgeId.ToString()));
        Assert.NotNull(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
        Assert.True(await UserIsActiveAsync(Mgr));

        // EVENT: no outbox row for the rolled-back assign or vikar-end.
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'ManagerVikarEnded' AND event_payload::text LIKE '%' || @id || '%'",
            vikar.VikarId.ToString()));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'ReportingLineAssigned' AND event_payload::text LIKE '%' || @id || '%'",
            newEdgeId.ToString()));

        // AUDIT: no audit_projection row for the rolled-back vikar end.
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE target_resource_id = @id", vikar.VikarId.ToString()));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  W2 — concurrency proofs (DETERMINISTIC held-lock interleaves, S74-7403 C2-4)
    //
    //  The previous W2 tests used bare Task.WhenAll with no barrier, so the two HTTP ops could
    //  execute ENTIRELY sequentially and still pass — they did not actually exercise contention,
    //  so they would NOT have failed if the advisory lock or ReadCommitted serialization regressed.
    //  These replacements hold the SAME tree advisory key the endpoint uses on a side connection and
    //  prove the endpoint BLOCKS on it, then prove the post-lock ReadCommitted read sees the
    //  committed interleaved effect — a guarantee that is impossible to satisfy by running
    //  sequentially with no lock.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The tree advisory key the endpoints take for the STY02 tree:
    /// <c>pg_advisory_xact_lock(hashtext('reporting-tree-STY02'))</c>. Holding it on a side
    /// connection lets a test deterministically BLOCK any in-tree assign/remove until released.
    /// </summary>
    private async Task<(NpgsqlConnection conn, NpgsqlTransaction tx)> AcquireTreeLockOnSideConnAsync(string treeRoot)
    {
        var conn = _dbFactory.Create();
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using (var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('reporting-tree-' || @root))", conn, tx))
        {
            cmd.Parameters.AddWithValue("root", treeRoot);
            await cmd.ExecuteScalarAsync();
        }
        return (conn, tx);
    }

    /// <summary>
    /// S74-7403 B3 — a STRICT barrier proving a backend has actually REACHED and BLOCKED ON the
    /// tree advisory lock for <paramref name="treeRoot"/>. Polls <c>pg_locks</c> (joined to
    /// <c>pg_stat_activity</c> to exclude this test's own session) until at least one OTHER backend
    /// holds a NOT-GRANTED (<c>granted = false</c>) <c>advisory</c> lock whose 64-bit key
    /// reconstructs to <c>hashtext('reporting-tree-' || treeRoot)</c>. For a single-argument
    /// <c>pg_advisory_xact_lock(bigint)</c> the key is split as <c>classid</c> = high 32 bits,
    /// <c>objid</c> = low 32 bits, so the key is rebuilt as
    /// <c>(classid::bigint &lt;&lt; 32) | objid::bigint</c>. Returns <c>true</c> once a waiter is
    /// confirmed; <c>false</c> on timeout. This makes the held-lock tests prove the request blocked
    /// ON THE LOCK (a merely-slow or sequentially-run request cannot satisfy it).
    /// </summary>
    private async Task<bool> WaitForAdvisoryLockWaiterAsync(string treeRoot, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        await using var conn = _dbFactory.Create();
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
                          = hashtext('reporting-tree-' || @root)::bigint
                )
                """, conn))
            {
                cmd.Parameters.AddWithValue("root", treeRoot);
                if (await cmd.ExecuteScalarAsync() is true)
                    return true;
            }
            await Task.Delay(50);
        }
        return false;
    }

    /// <summary>
    /// S78 R9 (BLOCKER 2) — like <see cref="WaitForAdvisoryLockWaiterAsync"/> but proves at least
    /// <paramref name="minWaiters"/> DISTINCT OTHER backends are simultaneously WAITING
    /// (<c>granted = false</c>) on the <c>reporting-tree-{treeRoot}</c> advisory key. Used to prove
    /// MUTUAL EXCLUSION between two different operation types (a transfer AND an assign) on the SAME
    /// shared key: when a side connection holds the key and BOTH a transfer and an assign are queued
    /// behind it, two distinct backends wait on the key. Counts DISTINCT <c>pid</c>s (excluding this
    /// test's own session) so a single backend cannot satisfy the threshold. Returns <c>true</c> once
    /// the count is met; <c>false</c> on timeout.
    /// </summary>
    private async Task<bool> WaitForAdvisoryLockWaiterCountAsync(string treeRoot, int minWaiters, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync();
        while (DateTime.UtcNow < deadline)
        {
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT COUNT(DISTINCT pl.pid)
                FROM pg_locks pl
                JOIN pg_stat_activity sa ON sa.pid = pl.pid
                WHERE pl.locktype = 'advisory'
                  AND pl.granted = FALSE
                  AND pl.pid <> pg_backend_pid()
                  AND ((pl.classid::bigint << 32) | pl.objid::bigint)
                      = hashtext('reporting-tree-' || @root)::bigint
                """, conn))
            {
                cmd.Parameters.AddWithValue("root", treeRoot);
                if ((long)(await cmd.ExecuteScalarAsync())! >= minWaiters)
                    return true;
            }
            await Task.Delay(50);
        }
        return false;
    }

    // S74-7403 C2-4 (held-lock serialization). PROVES the advisory lock genuinely serializes a
    // tree's assigns — the assign CANNOT proceed while the key is held, so it cannot pass by running
    // sequentially. We HOLD the STY02 tree key on a side connection, fire one leg of a would-be
    // CycX↔CycY 2-cycle, and assert it BLOCKS (does not complete within a short window). We release
    // the held lock, confirm the assign then RESOLVES (201), and that the reciprocal leg is then
    // rejected by the cycle guard (409) — exactly one leg can ever be active.
    [Fact]
    public async Task W2_HeldTreeLock_BlocksAssign_ThenSerializesCycleGuard()
    {
        var client = AdminClient();

        // 1. HOLD the STY02 tree advisory key on a side connection.
        var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        try
        {
            // 2. Fire leg A (CycX → CycY) — it must block on AcquireTreeLockAsync (same key, held).
            var legA = PostAssignAsync(client, "/api/admin/reporting-lines", new
            {
                employeeId = CycX,
                managerId = CycY,
                effectiveFrom = "2026-06-01",
            });

            // 3a. STRICT BARRIER (S74-7403 B3): poll pg_locks until the endpoint's backend is actually
            //     WAITING (granted = false) on OUR tree advisory key. This proves the request REACHED
            //     and BLOCKED ON THE LOCK — a merely-slow or sequentially-running request can never
            //     satisfy this, closing the "pass for the wrong reason" hole in the old bare-delay test.
            var sawWaiter = await WaitForAdvisoryLockWaiterAsync(TreeRootSty02);
            Assert.True(sawWaiter,
                "No backend was observed WAITING on the STY02 tree advisory lock — the endpoint did not block on the lock.");

            // 3b. PROOF OF BLOCKING: the assign must NOT have completed (it is parked waiting for the
            //     advisory lock we hold). If the lock were not actually taken by the endpoint, this
            //     assign would complete immediately and the test fails.
            var finishedEarly = await Task.WhenAny(legA, Task.Delay(500)) == legA;
            Assert.False(finishedEarly,
                "The assign completed while the tree advisory lock was held — the endpoint did not serialize on the lock.");

            // 4. Release the held lock → the parked assign acquires it and proceeds.
            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();

            var legAResp = await legA;
            Assert.Equal(HttpStatusCode.Created, legAResp.StatusCode);
        }
        finally
        {
            await holdConn.DisposeAsync(); // idempotent if already disposed above
        }

        // 5. The reciprocal leg B (CycY → CycX) now closes a cycle with the committed leg A → 409.
        //    It serialized through the SAME key (no held lock now) and its descendant walk SEES
        //    leg A's committed edge (ReadCommitted).
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

    // S74-7403 C2-4 (assign-vs-remove no-orphan, deterministic interleave). PROVES the assign blocks
    // on the held tree lock AND that its post-lock ReadCommitted re-read of manager-active state
    // rejects an edge to a manager deactivated WHILE the assign was parked — so no orphan edge can
    // be created. We:
    //   (1) HOLD the STY02 tree key on a side connection;
    //   (2) fire assign LateRep → Mgr — it BLOCKS on the held key;
    //   (3) while it is parked, deactivate Mgr + close its edges via the real R10 remove on ANOTHER
    //       side connection — but R10 ALSO needs the key, so we instead deactivate Mgr's reachability
    //       by committing the deactivation via a direct UPDATE that does NOT take the advisory key,
    //       simulating the post-preflight window deterministically;
    //   (4) release the held key → the parked assign acquires it, RE-READS Mgr as inactive
    //       (ReadCommitted), and is rejected (400) — NO orphan edge to the inactive Mgr.
    [Fact]
    public async Task W2_HeldLock_AssignToConcurrentlyDeactivatedManager_IsRejected_NoOrphan()
    {
        var client = AdminClient();

        // 1. HOLD the STY02 tree key.
        var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        var lockReleased = false;
        try
        {
            // 2. Fire assign LateRep → Mgr. It blocks on the held advisory key.
            var assignTask = PostAssignAsync(client, "/api/admin/reporting-lines", new
            {
                employeeId = LateRep,
                managerId = Mgr,
                effectiveFrom = "2026-06-01",
            });

            // 2a. STRICT BARRIER (S74-7403 B3): wait until the endpoint's backend is actually WAITING
            //     (granted = false) on OUR tree advisory key — proving it blocked ON THE LOCK, not that
            //     it is merely slow / running sequentially.
            var sawWaiter = await WaitForAdvisoryLockWaiterAsync(TreeRootSty02);
            Assert.True(sawWaiter,
                "No backend was observed WAITING on the STY02 tree advisory lock — the assign did not block on the lock.");

            // 2b. PROOF OF BLOCKING: parked while we hold the key.
            var finishedEarly = await Task.WhenAny(assignTask, Task.Delay(500)) == assignTask;
            Assert.False(finishedEarly,
                "The assign completed while the tree lock was held — it did not serialize on the lock.");

            // 3. While the assign is parked, DEACTIVATE Mgr on a separate connection (a direct UPDATE
            //    does not contend on the advisory key) — this is the committed effect the assign must
            //    observe once it acquires the lock. Mimics an R10 remove having deactivated Mgr in the
            //    window between the assign's out-of-tx checks and its in-tx (post-lock) validation.
            await using (var sideConn = _dbFactory.Create())
            {
                await sideConn.OpenAsync();
                await using var deact = new NpgsqlCommand(
                    "UPDATE users SET is_active = FALSE WHERE user_id = @id", sideConn);
                deact.Parameters.AddWithValue("id", Mgr);
                await deact.ExecuteNonQueryAsync();
            }

            // 4. Release the held key → the parked assign proceeds, re-reads Mgr as inactive in-tx.
            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();
            lockReleased = true;

            var assignResp = await assignTask;
            // The in-tx ValidateSameTreeAsync filters is_active = TRUE; Mgr now reads inactive →
            // InvalidOperationException → 400. No edge created.
            Assert.Equal(HttpStatusCode.BadRequest, assignResp.StatusCode);
        }
        finally
        {
            if (!lockReleased)
            {
                await holdTx.RollbackAsync();
                await holdConn.DisposeAsync();
            }
        }

        // NO NEW ORPHAN: the assign did NOT create an edge for LateRep pointing at the now-inactive
        // Mgr. (Note: this test deliberately deactivates Mgr via a RAW UPDATE — bypassing R10's edge
        // closure — purely to exercise the assign's post-lock manager-active re-read; so Mgr's
        // pre-existing seeded reports Emp/Emp2 intentionally remain and are NOT this test's concern.
        // The invariant under test is that the BLOCKED-then-resumed assign created no fresh orphan.)
        var lateRepPrimary = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(LateRep, "PRIMARY");
        Assert.True(lateRepPrimary is null || lateRepPrimary.ManagerId != Mgr,
            "LateRep is orphaned onto an inactive Mgr — the in-tx manager-active validation failed.");
        Assert.Null(lateRepPrimary); // the assign was rejected → no LateRep PRIMARY edge at all.
    }

    // S74-7403 fix4 / S78 R9 — the cross-tree (cross-MAO) transfer-mid-assign case. UNDER S78 R9 the assign no
    // longer proceeds on a STALE key: the drift-guarded acquire RE-DERIVES the root under the held
    // advisory, detects the drift, ROLLS BACK, and RETRIES the whole request on a fresh tx (re-keyed on
    // the NEW root). The terminal outcome is still a CLEAN 400 (cross-tree) — Mgr stayed in STY02 — with
    // NO deadlock and NO cross-tree edge. (We use a both-trees admin so the retry's out-of-tx scope check
    // covers LateRep's NEW org; a STY02-only admin would 403 at the scope gate on retry, which is the
    // separate single-scope behaviour. The drift-rollback-retry MECHANISM itself — that the retry re-keys
    // on the NEW root and serialises there — is proven by R9_DriftMidAcquire.)
    //   (1) HOLD the STY02 tree key on a side connection;
    //   (2) fire assign LateRep → Mgr (both STY02-tree at seed) — it BLOCKS on the held key (the advisory
    //       is taken on the now-soon-to-be-stale key, BEFORE the post-acquire re-derive);
    //   (3) while parked, TRANSFER LateRep's primary_org_id to STY05 via a raw UPDATE (the cross-tree
    //       org-transfer the unlocked pre-acquire read cannot see);
    //   (4) release the held key → the parked assign acquires STY02, RE-DERIVES LateRep=STY05 → DRIFT →
    //       rollback → RETRY on STY05; ValidateSameTreeAsync then sees LateRep=STY05, Mgr=STY02 → 400. No
    //       deadlock, no cross-tree edge.
    [Fact]
    public async Task Fix4_CrossStyrelseTransferMidAssign_IsRejected_400_NoDeadlock_NoCrossTreeEdge()
    {
        var client = TransferAdminClient(); // both STY02 + STY05 so the retry's scope check passes.

        // 1. HOLD the STY02 tree key.
        var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
        var lockReleased = false;
        try
        {
            // 2. Fire assign LateRep → Mgr (same STY02 tree at seed). Blocks on the held advisory key.
            var assignTask = PostAssignAsync(client, "/api/admin/reporting-lines", new
            {
                employeeId = LateRep,
                managerId = Mgr,
                effectiveFrom = "2026-06-01",
            });

            // 2a. STRICT BARRIER: the backend is actually WAITING on OUR STY02 advisory key — so the
            //     advisory was taken on the (about-to-be-stale) key BEFORE the post-acquire re-derive ran.
            var sawWaiter = await WaitForAdvisoryLockWaiterAsync(TreeRootSty02);
            Assert.True(sawWaiter,
                "No backend was observed WAITING on the STY02 tree advisory lock — the assign did not block on the lock.");

            // 2b. Proof of blocking: parked while we hold the key.
            var finishedEarly = await Task.WhenAny(assignTask, Task.Delay(500)) == assignTask;
            Assert.False(finishedEarly,
                "The assign completed while the tree lock was held — it did not serialize on the lock.");

            // 3. While parked, TRANSFER LateRep to STY05 (a DIFFERENT Organisation/MAO/tree) — the cross-tree
            //    org-transfer that committed in the unlocked pre-acquire → advisory window. A raw UPDATE
            //    does not contend on the advisory, so the held key stays stale for the parked assign.
            await using (var sideConn = _dbFactory.Create())
            {
                await sideConn.OpenAsync();
                await using var transfer = new NpgsqlCommand(
                    "UPDATE users SET primary_org_id = 'STY05' WHERE user_id = @id", sideConn);
                transfer.Parameters.AddWithValue("id", LateRep);
                await transfer.ExecuteNonQueryAsync();
            }

            // 4. Release the held key → the parked assign acquires STY02, RE-DERIVES LateRep=STY05 → DRIFT
            //    → rollback → RETRY on STY05; ValidateSameTreeAsync sees LateRep=STY05, Mgr=STY02 → 400.
            await holdTx.RollbackAsync();
            await holdConn.DisposeAsync();
            lockReleased = true;

            // No deadlock: the assign returns promptly (the drift-retry rolls back, never RETAINS the old
            // key while taking a new one). Bound the wait so a (regressed) deadlock surfaces as a failure.
            var completed = await Task.WhenAny(assignTask, Task.Delay(5000)) == assignTask;
            Assert.True(completed,
                "The assign did not complete after the lock was released — a drift-retry deadlock has regressed.");

            var assignResp = await assignTask;
            // The RETRIED assign (re-keyed on STY05) sees employee + manager in DIFFERENT trees →
            // CrossTreeAssignmentException → 400. No proceed-under-stale-key.
            Assert.Equal(HttpStatusCode.BadRequest, assignResp.StatusCode);
        }
        finally
        {
            if (!lockReleased)
            {
                await holdTx.RollbackAsync();
                await holdConn.DisposeAsync();
            }
        }

        // NO CROSS-TREE EDGE: the rejected assign created no LateRep PRIMARY edge at all.
        Assert.Null(await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(LateRep, "PRIMARY"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  S78 R9 — the SHARED DRIFT-GUARDED ACQUIRE (held-lock interleave proofs, R6)
    //
    //  These prove the S74-7403 cross-tree-transfer STALE-KEY residual is now CLOSED by the shared
    //  drift-guarded acquire (AcquireTreeLockForEmployeeAsync): the advisory key is RE-DERIVED under the
    //  held advisory and, on drift, the mutator THROWS TreeRootDriftException → RunAsync ROLLS BACK +
    //  RETRIES the whole request on a fresh tx (re-keyed on the NEW root). Unlike the prior Fix4 test
    //  (stale key + reject), the retry serializes on the CURRENT root.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// S78 R9 DRIFT-ROLLBACK-RETRY (the core proof). An assign whose advisory key DRIFTS mid-acquire —
    /// because a transfer moves the employee to a NEW tree (Organisation/MAO) while the assign is parked on the OLD key —
    /// must detect the drift under the lock, ROLL BACK, and RETRY on the NEW root (proven by the retry
    /// BLOCKING on the NEW-root advisory we also hold), then complete (here: 400 cross-tree, since the
    /// manager stayed in the OLD tree). No proceed-under-stale-key; bounded retry terminates.
    ///
    /// Interleave (two held keys):
    ///   (1) seed Drifter in STY02 (STY02 tree); HOLD both the STY02 key AND the STY05 key on side conns;
    ///   (2) fire assign Drifter → Mgr — it derives Drifter's root = STY02, then BLOCKS on the held STY02 key;
    ///   (3) while parked, TRANSFER Drifter to STY05 via a raw UPDATE (commits — the drift the unlocked
    ///       pre-acquire read cannot see);
    ///   (4) release ONLY the STY02 key → the parked assign acquires STY02, RE-DERIVES Drifter = STY05 →
    ///       DRIFT → TreeRootDriftException → rollback → RETRY. The retry derives STY05 and BLOCKS on the
    ///       held STY05 key (PROOF it re-keyed on the NEW root, not the stale one);
    ///   (5) release the STY05 key → the retried assign proceeds; ValidateSameTreeAsync sees Drifter=STY05,
    ///       Mgr=STY02 → 400. No edge created.
    /// </summary>
    [Fact]
    public async Task R9_DriftMidAcquire_RollsBack_RetriesOnNewRoot_AndSerializes()
    {
        const string Drifter = "t743_drifter";
        await SeedUserAsync(Drifter, "STY02"); // STY02 tree at seed.
        try
        {
            // Multi-scope admin (STY02 + STY05): on the post-drift RETRY the out-of-tx employee-scope
            // check runs against Drifter's NEW org (STY05) — a single-Organisation admin would 403 there
            // before reaching the advisory, so we use a both-trees admin to PROVE the retry re-keys on
            // the STY05 advisory (the whole point of this test).
            var client = TransferAdminClient();

            // (1) HOLD both tree keys on independent side connections.
            var (sty02Conn, sty02Tx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
            var (sty05Conn, sty05Tx) = await AcquireTreeLockOnSideConnAsync("STY05");
            var sty02Released = false;
            var sty05Released = false;
            try
            {
                // (2) Fire assign Drifter → Mgr. Derives Drifter=STY02 (unlocked), blocks on the STY02 key.
                var assignTask = PostAssignAsync(client, "/api/admin/reporting-lines", new
                {
                    employeeId = Drifter,
                    managerId = Mgr,
                    effectiveFrom = "2026-06-01",
                });

                Assert.True(await WaitForAdvisoryLockWaiterAsync(TreeRootSty02),
                    "The assign did not block on the STY02 advisory — it never reached the drift-guarded acquire.");
                Assert.False(await Task.WhenAny(assignTask, Task.Delay(500)) == assignTask,
                    "The assign completed while the STY02 key was held — it did not serialize on the lock.");

                // (3) TRANSFER Drifter to STY05 (raw UPDATE — does not contend on the advisory).
                await using (var sideConn = _dbFactory.Create())
                {
                    await sideConn.OpenAsync();
                    await using var transfer = new NpgsqlCommand(
                        "UPDATE users SET primary_org_id = 'STY05' WHERE user_id = @id", sideConn);
                    transfer.Parameters.AddWithValue("id", Drifter);
                    await transfer.ExecuteNonQueryAsync();
                }

                // (4) Release ONLY the STY02 key → the parked assign acquires STY02, RE-DERIVES Drifter as
                //     STY05 → DRIFT → rollback → RETRY. The retry must now BLOCK on the held STY05 key.
                await sty02Tx.RollbackAsync();
                await sty02Conn.DisposeAsync();
                sty02Released = true;

                Assert.True(await WaitForAdvisoryLockWaiterAsync("STY05"),
                    "After the drift, the retry did not block on the STY05 advisory — it did not re-key on the NEW root.");
                Assert.False(await Task.WhenAny(assignTask, Task.Delay(500)) == assignTask,
                    "The retried assign completed while the STY05 key was held — drift-retry did not re-serialize on the new root.");

                // (5) Release the STY05 key → the retried assign proceeds and rejects cross-tree (Mgr=STY02).
                await sty05Tx.RollbackAsync();
                await sty05Conn.DisposeAsync();
                sty05Released = true;

                var completed = await Task.WhenAny(assignTask, Task.Delay(5000)) == assignTask;
                Assert.True(completed, "The retried assign did not complete — the drift-retry loop hung.");
                var resp = await assignTask;
                Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            }
            finally
            {
                if (!sty02Released) { await sty02Tx.RollbackAsync(); await sty02Conn.DisposeAsync(); }
                if (!sty05Released) { await sty05Tx.RollbackAsync(); await sty05Conn.DisposeAsync(); }
            }

            // No edge created on either tree.
            Assert.Null(await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Drifter, "PRIMARY"));
        }
        finally
        {
            await CleanupChainAsync(new[] { Drifter });
        }
    }

    /// <summary>
    /// S78 R3/R9 TRANSFER-vs-ASSIGN serialization. The REAL transfer endpoint (PUT /api/admin/users/{id}
    /// moving primary_org_id) and a concurrent assign serialize through the SAME tree advisory: while a
    /// side connection holds the OLD-tree key, the transfer BLOCKS on it (it acquires the OLD+NEW roots via
    /// AcquireTreeLocksForTransferAsync before the users-row FOR UPDATE), then completes once released —
    /// proving the transfer is in-lock (no unserialized cross-tree move). No spurious 400; the org
    /// actually moved.
    /// </summary>
    [Fact]
    public async Task R9_Transfer_SerializesOnTreeAdvisory_NoSpurious400()
    {
        const string Mover = "t743_mover";
        await SeedUserAsync(Mover, "STY02"); // STY02 tree.
        try
        {
            var client = TransferAdminClient(); // covers BOTH STY02 + STY05.

            // HOLD the STY02 (OLD) tree key — the transfer must block on it (advisory before users FOR UPDATE).
            var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
            var released = false;
            try
            {
                var transferTask = PutTransferAsync(client, Mover, newPrimaryOrgId: "STY05", ifMatchVersion: 1);

                Assert.True(await WaitForAdvisoryLockWaiterAsync(TreeRootSty02),
                    "The transfer did not block on the OLD-tree (STY02) advisory — it skipped the in-lock acquire.");
                Assert.False(await Task.WhenAny(transferTask, Task.Delay(500)) == transferTask,
                    "The transfer completed while the OLD-tree key was held — it did not serialize on the advisory.");

                // Release → the transfer acquires OLD+NEW and proceeds.
                await holdTx.RollbackAsync();
                await holdConn.DisposeAsync();
                released = true;

                var completed = await Task.WhenAny(transferTask, Task.Delay(5000)) == transferTask;
                Assert.True(completed, "The transfer did not complete after the OLD-tree key was released.");
                var resp = await transferTask;
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            }
            finally
            {
                if (!released) { await holdTx.RollbackAsync(); await holdConn.DisposeAsync(); }
            }

            // The org actually moved (no spurious rejection).
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT primary_org_id FROM users WHERE user_id = @id", conn);
            cmd.Parameters.AddWithValue("id", Mover);
            Assert.Equal("STY05", (string?)await cmd.ExecuteScalarAsync());
        }
        finally
        {
            await CleanupChainAsync(new[] { "t743_mover" });
        }
    }

    /// <summary>
    /// S78 R5/R9 TRANSFER-vs-ASSIGN MUTUAL EXCLUSION (the strengthened proof — held-lock interleave).
    /// PROVES the cross-tree transfer (PUT /api/admin/users/{id} moving primary_org_id) and a
    /// concurrent reporting-line assign genuinely SERIALIZE on the SHARED <c>reporting-tree-STY02</c>
    /// advisory key — NEITHER can proceed while a side connection holds it — and that the post-release
    /// state is a coherent SERIALIZED outcome, never a torn interleave. The prior version of this test
    /// fired both with no barrier, accepted any non-500, and asserted NO final invariant, so it passed
    /// even if the two paths never shared a lock; this rewrite FAILS unless BOTH paths actually block on
    /// the shared key (the pg_locks granted=false waiter assertions, incl. a ≥2-waiter assertion that
    /// proves the transfer AND the assign contend on the SAME key) AND the final serialized invariant holds.
    ///
    /// Interleave:
    ///   (1) seed Dl in STY02 (STY02 tree); HOLD the STY02 tree key on a side connection;
    ///   (2) fire the TRANSFER Dl STY02→STY05 — it must BLOCK on the held STY02 key (it acquires the OLD
    ///       root STY02 via AcquireTreeLocksForTransferAsync BEFORE the users-row FOR UPDATE). Prove the
    ///       waiter (pg_locks granted=false on the STY02 advisory) → the transfer takes the shared key;
    ///   (3) fire the ASSIGN Dl → Mgr — it must ALSO block on the SAME held STY02 key (its drift-guarded
    ///       acquire takes STY02). Prove ≥2 DISTINCT backends now WAIT on the key → the transfer AND the
    ///       assign mutually exclude on the shared tree key (the core mutual-exclusion proof);
    ///   (4) release the held key → the two serialize one-at-a-time. Both complete (no 500 / no hang);
    ///   (5) FINAL INVARIANT (grant-order-INDEPENDENT, so non-flaky): the transfer ALWAYS succeeds (200)
    ///       and moves Dl to STY05 (it does not depend on the assign); and the assign's terminal status is
    ///       TIGHTLY COUPLED to the surviving edge state — there is never a torn combination. Concretely:
    ///         • assign 201 ⟺ a Dl→Mgr PRIMARY edge survives (the assign won the key first, created it
    ///           while Dl was STILL same-tree in STY02; the transfer — which does not re-close existing
    ///           edges — then moved Dl, an ACCEPTED residual);
    ///         • assign 400 ⟺ NO Dl edge survives (the assign was serialized behind the committed transfer,
    ///           re-derived Dl=STY05 vs Mgr=STY02 → cross-tree reject).
    ///       A non-serialized (torn) execution — 400 WITH a surviving edge, or 201 WITH no edge — fails the
    ///       coupling and the test. This is exactly the guarantee that is impossible to satisfy without the
    ///       two paths genuinely serializing on the shared key.
    /// </summary>
    [Fact]
    public async Task R9_TransferVsAssign_SerializeOnSharedKey_MutualExclusion()
    {
        const string Dl = "t743_dl";
        await SeedUserAsync(Dl, "STY02"); // STY02 tree.
        try
        {
            var transferClient = TransferAdminClient(); // covers STY02 + STY05.
            var assignClient = TransferAdminClient();   // covers STY02 + STY05 so the post-drift assign retry's
                                                        // out-of-tx employee-scope check passes on Dl's NEW org (STY05).

            // (1) HOLD the STY02 tree advisory key — BOTH the transfer and the assign must block on it.
            var (holdConn, holdTx) = await AcquireTreeLockOnSideConnAsync(TreeRootSty02);
            var released = false;
            HttpResponseMessage transferResp, assignResp;
            try
            {
                // (2) Fire the TRANSFER Dl STY02→STY05. It acquires the OLD root (STY02) FIRST → blocks on the held key.
                var transferTask = PutTransferAsync(transferClient, Dl, newPrimaryOrgId: "STY05", ifMatchVersion: 1);

                Assert.True(await WaitForAdvisoryLockWaiterAsync(TreeRootSty02),
                    "The transfer did not block on the held STY02 advisory — it did not acquire the shared tree key before mutating.");
                Assert.False(await Task.WhenAny(transferTask, Task.Delay(500)) == transferTask,
                    "The transfer completed while the STY02 key was held — it did not serialize on the shared advisory.");

                // (3) Fire the ASSIGN Dl → Mgr (both STY02 at this point). Its drift-guarded acquire ALSO
                //     takes the STY02 key → it queues behind the side conn. Prove ≥2 DISTINCT OTHER backends
                //     now WAIT (granted=false) on the SAME key — the transfer AND the assign both contend on
                //     it. THIS is the mutual-exclusion proof: two different operation types serialize on one key.
                var assignTask = PostAssignAsync(assignClient, "/api/admin/reporting-lines", new
                {
                    employeeId = Dl,
                    managerId = Mgr,
                    effectiveFrom = "2026-06-01",
                });

                Assert.True(await WaitForAdvisoryLockWaiterCountAsync(TreeRootSty02, minWaiters: 2),
                    "Fewer than 2 backends waited on the STY02 advisory — the assign and the transfer did not BOTH contend on the shared tree key (mutual exclusion not proven).");
                Assert.False(await Task.WhenAny(assignTask, Task.Delay(500)) == assignTask,
                    "The assign completed while the STY02 key was held — it did not serialize on the shared advisory.");

                // (4) Release the held key → the two queued backends serialize one-at-a-time through the
                //     shared key. Both must complete promptly (no deadlock / no hang).
                await holdTx.RollbackAsync();
                await holdConn.DisposeAsync();
                released = true;

                await Task.WhenAny(Task.WhenAll(transferTask, assignTask), Task.Delay(10000));
                Assert.True(transferTask.IsCompleted && assignTask.IsCompleted,
                    "A transfer-vs-assign pair did not complete after the shared key was released — a serialization hang regressed.");
                transferResp = transferTask.Result;
                assignResp = assignTask.Result;
            }
            finally
            {
                if (!released) { await holdTx.RollbackAsync(); await holdConn.DisposeAsync(); }
            }

            // Neither may deadlock (40P01 → 500).
            Assert.NotEqual(HttpStatusCode.InternalServerError, transferResp.StatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, assignResp.StatusCode);

            // (5) FINAL INVARIANT — grant-order-INDEPENDENT (non-flaky), proves serialization:
            //     (a) the transfer ALWAYS moves Dl to STY05 (it does not depend on the assign);
            Assert.Equal(HttpStatusCode.OK, transferResp.StatusCode);
            await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("SELECT primary_org_id FROM users WHERE user_id = @id", conn);
                cmd.Parameters.AddWithValue("id", Dl);
                Assert.Equal("STY05", (string?)await cmd.ExecuteScalarAsync());
            }
            //     (b) the assign's status is TIGHTLY COUPLED to the surviving edge — never a torn state.
            var dlEdge = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Dl, "PRIMARY");
            if (assignResp.StatusCode == HttpStatusCode.Created)
            {
                // Assign won the key first → it created a same-tree edge (Dl was STY02 then); the transfer,
                // which does not re-close existing edges, then moved Dl (the accepted residual). The edge
                // MUST exist and point at Mgr — a 201 with NO edge would be a torn/non-serialized outcome.
                Assert.NotNull(dlEdge);
                Assert.Equal(Mgr, dlEdge!.ManagerId);
            }
            else
            {
                // Assign serialized behind the committed transfer → re-derived Dl=STY05 vs Mgr=STY02 → 400,
                // and NO edge was created. A 400 WITH a surviving edge would be a torn/non-serialized outcome.
                Assert.Equal(HttpStatusCode.BadRequest, assignResp.StatusCode);
                Assert.Null(dlEdge);
            }
        }
        finally
        {
            await CleanupChainAsync(new[] { "t743_dl" });
        }
    }

    /// <summary>
    /// S78 R5 DEADLOCK-ABSENCE (shake test, hardened). A transfer and an assign over OVERLAPPING work, fired
    /// CONCURRENTLY with no barrier (so they genuinely contend on the tree advisory + user rows), must both
    /// complete with a clean status — NOT a deadlock (40P01 → 500) or a hang. The total order advisory →
    /// user-rows FOR UPDATE (id-ordered) is consistent across both paths, so no cycle can form. Several
    /// rounds shake out ordering-dependent deadlocks. UNLIKE the prior version (which asserted only
    /// "not a 500"), each round now also asserts a per-round FINAL INVARIANT so a regression that silently
    /// stops serializing — leaving a torn state — fails the test: the transfer ALWAYS moves Dl to STY05,
    /// and the assign NEVER leaves a surviving cross-tree Dl→Mgr edge (Dl in STY05, Mgr in STY02).
    /// </summary>
    [Fact]
    public async Task R9_TransferVsAssign_OverlappingRows_DoNotDeadlock()
    {
        const string Dl = "t743_dl";
        await SeedUserAsync(Dl, "STY02"); // STY02 tree.
        try
        {
            for (var round = 0; round < 4; round++)
            {
                // Reset Dl to STY02 each round so the transfer always has work to do.
                await using (var reset = new NpgsqlConnection(_harness.ConnectionString))
                {
                    await reset.OpenAsync();
                    await using (var del = new NpgsqlCommand(
                        "DELETE FROM reporting_lines WHERE employee_id = @id OR manager_id = @id", reset))
                    {
                        del.Parameters.AddWithValue("id", Dl);
                        await del.ExecuteNonQueryAsync();
                    }
                    await using var upd = new NpgsqlCommand(
                        "UPDATE users SET primary_org_id = 'STY02', version = 1 WHERE user_id = @id", reset);
                    upd.Parameters.AddWithValue("id", Dl);
                    await upd.ExecuteNonQueryAsync();
                }

                // BOTH clients cover STY02 + STY05 so that whichever order wins, the assign's post-drift
                // retry scope check (against Dl's possibly-NEW org) does not 403 for the wrong reason.
                var transferClient = TransferAdminClient();
                var assignClient = TransferAdminClient();

                // Transfer Dl STY02→STY05; concurrently assign Dl → Mgr (STY02). They contend on Dl's row
                // + the STY02 advisory. Whichever wins, neither may deadlock (clean status both).
                var transferTask = PutTransferAsync(transferClient, Dl, newPrimaryOrgId: "STY05", ifMatchVersion: 1);
                var assignTask = PostAssignAsync(assignClient, "/api/admin/reporting-lines", new
                {
                    employeeId = Dl,
                    managerId = Mgr,
                    effectiveFrom = "2026-06-01",
                });

                await Task.WhenAny(Task.WhenAll(transferTask, assignTask), Task.Delay(15000));
                Assert.True(transferTask.IsCompleted && assignTask.IsCompleted,
                    $"Round {round}: a transfer-vs-assign pair did not complete within 15s — a deadlock/hang regressed.");

                // Neither may be a 500 (a deadlock would surface as 40P01 → 500). Any 2xx/4xx is acceptable —
                // the point is the absence of a deadlock, not a particular winner.
                Assert.NotEqual(HttpStatusCode.InternalServerError, transferTask.Result.StatusCode);
                Assert.NotEqual(HttpStatusCode.InternalServerError, assignTask.Result.StatusCode);

                // PER-ROUND FINAL INVARIANT — the serialized outcome, regardless of which path won:
                //   (a) the transfer always moves Dl to STY05 (it does not depend on the assign);
                await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
                {
                    await conn.OpenAsync();
                    await using var cmd = new NpgsqlCommand("SELECT primary_org_id FROM users WHERE user_id = @id", conn);
                    cmd.Parameters.AddWithValue("id", Dl);
                    Assert.Equal("STY05", (string?)await cmd.ExecuteScalarAsync());
                }
                //   (b) no SURVIVING cross-tree edge: if the assign committed an edge it must have been
                //       same-tree-valid (Dl in STY02) at the time — but since the transfer always moves Dl
                //       to STY05, a non-serialized assign-then-no-revalidation would leave a cross-tree row.
                //       The serialization guarantee is: the assign either (i) was rejected cross-tree (no
                //       edge), or (ii) committed an edge while Dl was STILL in STY02 AND the transfer was
                //       serialized AFTER it. Case (ii) would leave a row whose tree_root_org_id = STY02 even
                //       though Dl now resolves to STY05 — which IS the accepted "transfer does not re-close
                //       existing edges" residual. To keep this shake test deterministic we assert the
                //       SAFETY property that always holds: no edge exists that points Dl at Mgr ACROSS the
                //       current trees — i.e. either no edge, or (if one exists) it was committed pre-transfer
                //       and the assign therefore won the race, which a 201 on assignTask confirms.
                var dlEdge = await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(Dl, "PRIMARY");
                if (dlEdge is not null)
                {
                    // The only way an edge survives is the assign-won-first ordering → assign returned 201
                    // (same-tree at creation). Anything else is a non-serialized torn state.
                    Assert.Equal(HttpStatusCode.Created, assignTask.Result.StatusCode);
                    Assert.Equal(Mgr, dlEdge.ManagerId);
                }
                else
                {
                    // No edge → the assign was serialized behind the transfer and rejected cross-tree (400).
                    Assert.Equal(HttpStatusCode.BadRequest, assignTask.Result.StatusCode);
                }
            }
        }
        finally
        {
            await CleanupChainAsync(new[] { "t743_dl" });
        }
    }

    /// <summary>
    /// S78 R9 BOUNDED-RETRY TERMINATION. The shared retry wrapper (TreeRootDriftRetry.RunAsync) must, when
    /// the body throws TreeRootDriftException on EVERY attempt, terminate after a BOUNDED number of retries
    /// and return a PINNED 409 — never a hang and never an incidental 5xx. Driven directly (deterministic;
    /// the DB-level drift is timing-bound and is covered by R9_DriftMidAcquire above): an attempt delegate
    /// that always drifts is invoked at most MaxDriftRetries+1 times, then a 409 is returned.
    /// </summary>
    [Fact]
    public async Task R9_BoundedRetry_AlwaysDrifts_TerminatesWithPinned409()
    {
        var attempts = 0;
        var result = await StatsTid.Backend.Api.Endpoints.Helpers.TreeRootDriftRetry.RunAsync(() =>
        {
            attempts++;
            throw new TreeRootDriftException("emp", staleTreeRoot: "STY02", currentTreeRoot: "STY05");
        });

        // Bounded: the body ran exactly (first attempt + MaxDriftRetries) times — not unbounded, not a hang.
        Assert.Equal(StatsTid.Backend.Api.Endpoints.Helpers.TreeRootDriftRetry.MaxDriftRetries + 1, attempts);

        // Pinned 409 (a retryable conflict), NOT a 5xx. Results.Json(..., statusCode: 409) returns an
        // IResult that also implements IStatusCodeHttpResult exposing the pinned status — assert it
        // directly (no HttpContext plumbing needed).
        var statusResult = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
        Assert.Equal(409, statusResult.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  C2-4 — deep-cycle detection + termination (the B3 depth-cap removal actually works)
    // ════════════════════════════════════════════════════════════════════════════════

    // S74-7403 C2-4 (deep-cycle DETECTION). Builds a LEGITIMATE chain DEEPER than the old depth-10
    // cap (14 levels: dc0 ← dc1 ← … ← dc14, each reporting to the one above), then attempts to close
    // a cycle at that depth by assigning the top (dc0) UNDER the deepest descendant (dc14). The
    // descendant walk from dc0 must traverse all 14 levels to find dc14; the old depth-10 cap would
    // have STOPPED short and FALSELY ADMITTED the cycle. With the cap removed (CycleWalkMaxDepth
    // 10_000, path-array as the real terminator), the deep cycle is REJECTED (409).
    [Fact]
    public async Task C2_4_DeepCycle_BeyondOldDepthCap_IsDetected_AndRejected()
    {
        const int depth = 14; // > the old cap of 10.
        var chain = await SeedLinearChainAsync("dc", depth, "STY02");

        var client = AdminClient();
        // Assign the chain TOP (chain[0]) to report to the DEEPEST node (chain[depth]) — a cycle that
        // only a walk reaching depth 14 can detect.
        var rsp = await PostAssignAsync(client, "/api/admin/reporting-lines", new
        {
            employeeId = chain[0],
            managerId = chain[depth],
            effectiveFrom = "2026-06-01",
        });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("cycle", body.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

        // The cycle was NOT admitted: chain[0] still has no active PRIMARY (it is the chain root).
        Assert.Null(await _rlRepo.GetActiveByEmployeeAndRelationshipAsync(chain[0], "PRIMARY"));

        await CleanupChainAsync(chain);
    }

    // S74-7403 C2-4 / B3 (TERMINATION on a pre-existing loop — ISOLATED from the depth ceiling).
    // Plants a pre-existing 2-cycle DIRECTLY via SQL (bypassing the guard), then runs the descendant
    // walk for a fresh assign whose walk traverses that loop. To PROVE the path-array visited-set
    // guard — NOT the CycleWalkMaxDepth ceiling — is what terminates the walk, we run it via the
    // explicit-maxDepth overload with the ceiling set to int.MaxValue: far above any loop length, so
    // the depth bound cannot be the terminator. If the path-array guard regressed, the walk would
    // recurse on the 2-cycle until ~2 billion iterations and BLOW PAST the timeout (the test fails);
    // it returns promptly ONLY because the visited-set guard stops it.
    [Fact]
    public async Task C2_4_GuardNoCycle_TerminatesOnPreExistingLoop()
    {
        // Plant pc_a ↔ pc_b directly (a reciprocal active PRIMARY 2-cycle the guard would never allow).
        var pcA = "t743_pc_a";
        var pcB = "t743_pc_b";
        await SeedUserAsync(pcA, "STY02");
        await SeedUserAsync(pcB, "STY02");
        await InsertRawEdgeAsync(pcA, pcB); // pc_a reports to pc_b
        await InsertRawEdgeAsync(pcB, pcA); // pc_b reports to pc_a  → loop

        // Run the descendant walk (via GuardNoCycleAsync) for an assign whose walk enters the loop.
        // It must RETURN (terminate) within the timeout, not hang. We guard-check pc_a → Top: the
        // walk descends from pc_a, hits the pc_a↔pc_b loop, and the path-array guard stops it. The
        // explicit maxDepth = int.MaxValue DISABLES the safety ceiling as a possible terminator, so a
        // prompt return isolates the path-array guard as the cause (B3).
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var walk = _rlRepo.GuardNoCycleAsync(conn, tx, pcA, Top, int.MaxValue); // ceiling disabled → path-guard must terminate
        var terminated = await Task.WhenAny(walk, Task.Delay(5000)) == walk;
        Assert.True(terminated, "GuardNoCycleAsync did not terminate on a pre-existing loop with the depth ceiling disabled — the path-array visited-set guard failed.");
        await walk; // surface any (unexpected) exception; should complete cleanly.

        await tx.RollbackAsync();

        // Cleanup the planted loop (FK-ordered, mirrors CleanupChainAsync).
        await CleanupChainAsync(new[] { pcA, pcB });
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken(Top, "STY02"));
        return client;
    }

    /// <summary>
    /// S78 R9 — an admin client whose token carries LocalAdmin scope over BOTH STY02 and STY05, so the
    /// cross-tree transfer endpoint (PUT /api/admin/users/{id}) passes the source-org AND target-org
    /// scope checks. The transfer requires the actor to cover the user's current org (STY02 tree) and the
    /// new org (STY05 tree).
    /// </summary>
    private HttpClient TransferAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintMultiScopeAdminToken(Top, "STY02", "STY05"));
        return client;
    }

    /// <summary>
    /// S78 R9 — PUTs the user transfer (changing primary_org_id) with the admin-strict If-Match header the
    /// endpoint requires (ADR-019 D2). EffectiveFrom = today (UTC) so the agreement-code validator is a
    /// no-op (agreement_code is unchanged here — this is a pure org transfer).
    /// </summary>
    private static async Task<HttpResponseMessage> PutTransferAsync(
        HttpClient client, string userId, string newPrimaryOrgId, long ifMatchVersion)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new { primaryOrgId = newPrimaryOrgId, effectiveFrom = today }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchVersion}\"");
        return await client.SendAsync(req);
    }

    /// <summary>
    /// POSTs a JSON body to an assign endpoint with the <c>If-None-Match: *</c> first-assignment
    /// precondition header (which <see cref="HttpClientJsonExtensions.PostAsJsonAsync"/> cannot
    /// set). The assign endpoints 428 without a concurrency precondition header.
    /// </summary>
    private static async Task<HttpResponseMessage> PostAssignAsync(HttpClient client, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return await client.SendAsync(req);
    }

    private async Task<long> CountAsync(string sql, string? param = null)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        if (param is not null) cmd.Parameters.AddWithValue("id", param);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<bool> UserIsActiveAsync(string userId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT is_active FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result is true;
    }

    /// <summary>
    /// Seeds <paramref name="depth"/>+1 fresh users <c>{prefix}0..{prefix}{depth}</c> in
    /// <paramref name="orgId"/> and a LINEAR reporting chain where each lower node reports PRIMARY to
    /// the node above it: <c>{prefix}{i+1} → {prefix}{i}</c>. So <c>{prefix}{depth}</c> is a
    /// descendant of <c>{prefix}0</c> at depth <paramref name="depth"/>. Returns the user-id array
    /// indexed 0..depth (index 0 = chain root, index depth = deepest leaf).
    /// </summary>
    private async Task<string[]> SeedLinearChainAsync(string prefix, int depth, string orgId)
    {
        var ids = Enumerable.Range(0, depth + 1).Select(i => $"t743_{prefix}{i}").ToArray();
        foreach (var id in ids)
            await SeedUserAsync(id, orgId);
        // {i+1} reports to {i} → {i+1} is a child of {i}. Walk 1..depth.
        for (var i = 0; i < depth; i++)
            await _rlRepo.AssignAsync(null, MakeLine(ids[i + 1], ids[i]));
        return ids;
    }

    private async Task CleanupChainAsync(string[] ids)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // Delete dependent rows before users (FK order), mirroring the main CleanupAsync.
        async Task DelAsync(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("ids", ids);
            await cmd.ExecuteNonQueryAsync();
        }
        await DelAsync("DELETE FROM reporting_line_audit WHERE reporting_line_id IN (SELECT reporting_line_id FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids))");
        await DelAsync("DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await DelAsync("DELETE FROM employee_profiles WHERE employee_id = ANY(@ids)");
        await DelAsync("DELETE FROM employee_profile_audit WHERE employee_id = ANY(@ids)");
        await DelAsync("DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await DelAsync("DELETE FROM user_agreement_codes_audit WHERE user_id = ANY(@ids)");
        await DelAsync("DELETE FROM users_audit WHERE user_id = ANY(@ids)");
        await DelAsync("DELETE FROM users WHERE user_id = ANY(@ids)");
    }

    private async Task SeedUserAsync(string userId, string orgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@id, @id, '$2a$11$fake', @id, @id || '@test.dk', @org, 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("org", orgId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts a RAW active PRIMARY reporting edge (employee → manager) directly, BYPASSING the
    /// cycle guard — used only to PLANT a pre-existing loop for the termination test.
    /// </summary>
    private async Task InsertRawEdgeAsync(string employeeId, string managerId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines (
                reporting_line_id, employee_id, manager_id, tree_root_org_id,
                relationship, effective_from, effective_to, source, version, created_by, created_at)
            VALUES (@id, @emp, @mgr, @root, 'PRIMARY', '2026-01-01', NULL, 'MANUAL', 1, 'TEST', NOW())
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("mgr", managerId);
        cmd.Parameters.AddWithValue("root", TreeRootSty02);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<ManagerVikar> CreateVikarAsync(string absentApprover, string vikarUser, DateOnly untilDate)
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
            TreeRootOrgId = TreeRootSty02,
            Version = 1,
            CreatedBy = "TEST",
        });
        await tx.CommitAsync();
        return v;
    }

    private static string MintAdminToken(string userId, string orgId)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalAdmin, orgId, "ORG_AND_DESCENDANTS") };
        return tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalAdmin,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    /// <summary>
    /// S78 R9 — mints a LocalAdmin token carrying ORG_AND_DESCENDANTS scope over EVERY org in
    /// <paramref name="orgIds"/> (the transfer endpoint needs the actor to cover both the source and the
    /// target tree). The primary <c>orgId</c> claim is the first entry.
    /// </summary>
    private static string MintMultiScopeAdminToken(string userId, params string[] orgIds)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var scopes = orgIds
            .Select(o => new RoleScope(StatsTidRoles.LocalAdmin, o, "ORG_AND_DESCENDANTS"))
            .ToArray();
        return tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalAdmin,
            agreementCode: "HK", orgId: orgIds[0], scopes: scopes);
    }
}
