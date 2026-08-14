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
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// SEC-009 / RES-003 — the segregation-of-duties (SoD) self-guard on the MANAGER-side approval
/// decisions. The differential MATRIX that RES-003 item 1 asked for (the audit's output is a test
/// matrix, not a document): for EACH authority leg — org-scope/HR-fallback, designated edge,
/// unit-leader, vikar — a self actor is DENIED on approve while an OTHER actor via that SAME leg is
/// ALLOWED (no regression); plus a positive self-match (a real self-id actually denies — guards
/// against a no-op comparison), guard-ordering (self on an INELIGIBLE status still self-denies 403,
/// not a state-leaking 409), the reopen split (HR/Admin self-reopen of an APPROVED period DENIED, of
/// an EMPLOYEE_APPROVED period ALLOWED per owner ruling OQ-1a; the Employee-role arm unchanged), and
/// the no-over-block cases (employee-approve + send self still succeed).
///
/// <para><b>The rule.</b> Nobody performs a manager DECISION on their own period. That blocks self on
/// approve, reject, and reopen-of-<c>APPROVED</c>. It PERMITS self on the pre-approval self-undo
/// (reopen of one's own <c>EMPLOYEE_APPROVED → DRAFT</c>), on <c>employee-approve</c>, and on
/// <c>send</c> — the legitimate self-service the two-step flow depends on.</para>
///
/// <para><b>RED-on-old.</b> The org-scope/HR-fallback leg is SEC-009's exact defect: an
/// HR/LocalAdmin/GlobalAdmin scoped over their own Organisation could approve their OWN period,
/// because that leg does NOT route through <c>DesignatedApproverAuthorizer</c>'s predicate (where the
/// unit-leader/vikar legs already self-exclude). Pre-fix, <c>Hr</c> approving their own period was a
/// 200; now the endpoint's <c>ApprovalSelfGuard</c> denies it (403) before the status check. The
/// edge/unit-leader/vikar self cases were already 403 via their per-path SQL exclusions — the matrix
/// asserts each STILL denies (now via the guard, proven by the denial reason) AND that the other-actor
/// leg is not regressed.</para>
///
/// <para>Each [Fact] boots a FRESH Postgres testcontainer (init.sql + host seeders) so the demo tree
/// exists and the isolated <c>sec009_*</c> fixtures are seeded fresh per test. Endpoint-level via
/// <see cref="StatsTidWebApplicationFactory"/>; idioms mirror <see cref="S105UnitLeaderApprovalTests"/>
/// and <see cref="S94FlatApprovalTests"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SelfApprovalGuardTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // STY02 is an ORGANISATION under MAO MIN01 (init.sql seed); the isolated unit + fixtures live here.
    private const string OrgA = "STY02";

    // A single member unit under STY02 (disjoint from the demo 000000d0-… tree).
    private static readonly Guid UnitMember = Guid.Parse("5ec00900-0000-0000-0000-000000000001");

    // ── The four authority legs, each an actor who can act on Emp (the victim) ──
    private const string Emp        = "sec009_emp";      // EMPLOYEE, member unit; PRIMARY edge → PrimaryMgr
    private const string PrimaryMgr = "sec009_pmgr";     // LocalLeader — Emp's designated EDGE approver
    private const string DirectLdr  = "sec009_direct";   // LocalLeader — leader of Emp's unit (UNIT-LEADER leg)
    private const string VikarUsr   = "sec009_vikar";    // LocalLeader — active vikar of DirectLdr (VIKAR leg)
    private const string Hr         = "sec009_hr";       // LocalHR over STY02 (ORG-SCOPE/HR fallback leg)

    // ── Send / employee-approve self-success (no-over-block) actors — full profile chain via RegressionSeed ──
    private const string SelfSend   = "sec009_selfsend"; // EMPLOYEE — sends their OWN month
    private const string SelfSend2  = "sec009_selfsend2";// EMPLOYEE — employee-approves their OWN period

    // A monotonic seq for hand-written absence rows' outbox_id (a NOT-NULL BIGINT), clear of other suites.
    private static long _absenceSeq = 90_090_000;

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
        await SeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (1) THE PER-LEG SELF-PAIR MATRIX — self DENIED, other-actor via the SAME leg ALLOWED.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>ORG-SCOPE / HR-FALLBACK leg (SEC-009's exact defect). Self: Hr (LocalHR over STY02)
    /// approving their OWN period is DENIED (403, SoD) — pre-fix this was a 200. Other: Hr approves
    /// Emp's period via the org-scope fallback → 200 (ORG_SCOPE_FALLBACK), no regression.</summary>
    [Fact]
    public async Task SelfPair_OrgScopeHrFallback_SelfDenied_OtherAllowed()
    {
        // SELF (RED-on-old): Hr approving their own period is denied by the SoD guard.
        var pSelf = await InsertPeriodAsync(Hr, "SUBMITTED", 5);
        var selfRsp = await AdminRoleClient(StatsTidRoles.LocalHR, Hr).PostAsync($"/api/approval/{pSelf}/approve", null);
        await AssertSelfDeniedAsync(selfRsp);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pSelf));

        // OTHER: Hr approves Emp via the org-scope fallback → 200, ORG_SCOPE_FALLBACK (no regression).
        var pOther = await InsertPeriodAsync(Emp, "SUBMITTED", 6);
        var otherRsp = await AdminRoleClient(StatsTidRoles.LocalHR, Hr).PostAsync($"/api/approval/{pOther}/approve", null);
        Assert.Equal(HttpStatusCode.OK, otherRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(pOther));
        Assert.Equal("ORG_SCOPE_FALLBACK", await ReadColumnAsync(pOther, "approval_method"));
    }

    /// <summary>DESIGNATED EDGE leg. Self: PrimaryMgr (Emp's edge approver) approving their OWN period is
    /// DENIED (403, SoD). Other: PrimaryMgr approves Emp via the edge → 200, DESIGNATED_MANAGER.</summary>
    [Fact]
    public async Task SelfPair_DesignatedEdge_SelfDenied_OtherAllowed()
    {
        var pSelf = await InsertPeriodAsync(PrimaryMgr, "SUBMITTED", 5);
        var selfRsp = await LeaderClient(PrimaryMgr).PostAsync($"/api/approval/{pSelf}/approve", null);
        await AssertSelfDeniedAsync(selfRsp);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pSelf));

        var pOther = await InsertPeriodAsync(Emp, "SUBMITTED", 6);
        var otherRsp = await LeaderClient(PrimaryMgr).PostAsync($"/api/approval/{pOther}/approve", null);
        Assert.Equal(HttpStatusCode.OK, otherRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(pOther));
        Assert.Equal("DESIGNATED_MANAGER", await ReadColumnAsync(pOther, "approval_method"));
    }

    /// <summary>UNIT-LEADER leg. Self: DirectLdr (leader of Emp's unit, and — the D3 member-invariant — a
    /// member of it) approving their OWN period is DENIED (403, SoD). Other: DirectLdr approves Emp via
    /// the secondary unit-leader path → 200, UNIT_LEADER.</summary>
    [Fact]
    public async Task SelfPair_UnitLeader_SelfDenied_OtherAllowed()
    {
        var pSelf = await InsertPeriodAsync(DirectLdr, "SUBMITTED", 5);
        var selfRsp = await LeaderClient(DirectLdr).PostAsync($"/api/approval/{pSelf}/approve", null);
        await AssertSelfDeniedAsync(selfRsp);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pSelf));

        var pOther = await InsertPeriodAsync(Emp, "SUBMITTED", 6);
        var otherRsp = await LeaderClient(DirectLdr).PostAsync($"/api/approval/{pOther}/approve", null);
        Assert.Equal(HttpStatusCode.OK, otherRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(pOther));
        Assert.Equal("UNIT_LEADER", await ReadColumnAsync(pOther, "approval_method"));
    }

    /// <summary>VIKAR leg. Self: VikarUsr (active stand-in for DirectLdr) approving their OWN period is
    /// DENIED (403, SoD). Other: VikarUsr approves Emp via the unit-leader-vikar path → 200,
    /// UNIT_LEADER_VIKAR (the approvals the absent leader OWES — RES-003 instance 3).</summary>
    [Fact]
    public async Task SelfPair_Vikar_SelfDenied_OtherAllowed()
    {
        var pSelf = await InsertPeriodAsync(VikarUsr, "SUBMITTED", 5);
        var selfRsp = await LeaderClient(VikarUsr).PostAsync($"/api/approval/{pSelf}/approve", null);
        await AssertSelfDeniedAsync(selfRsp);
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pSelf));

        var pOther = await InsertPeriodAsync(Emp, "SUBMITTED", 6);
        var otherRsp = await LeaderClient(VikarUsr).PostAsync($"/api/approval/{pOther}/approve", null);
        Assert.Equal(HttpStatusCode.OK, otherRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadStatusAsync(pOther));
        Assert.Equal("UNIT_LEADER_VIKAR", await ReadColumnAsync(pOther, "approval_method"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (1b) CHOKE-POINT BACKSTOP — the predicate self-guard pinned DIRECTLY (RES-003 item 2).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// RES-003 item 2 — the structural fail-CLOSED choke point inside
    /// <see cref="DesignatedApproverAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(string, string, System.DateOnly?, System.Threading.CancellationToken)"/>,
    /// pinned DIRECTLY rather than through an endpoint. The RES-003 CLASS closure rests on this backstop:
    /// at the endpoints the <c>ApprovalSelfGuard</c> helper fires first, and on the read surfaces the SQL
    /// already self-excludes, so NO differential is possible today — remove the choke-point guard and
    /// every OTHER test still passes. This is therefore a CONTRACT / regression pin (defense-in-depth),
    /// asserting the predicate itself denies self so a future authorization path that funnels through it
    /// inherits the SoD rule fail-closed instead of re-omitting it.
    ///
    /// <para>Positive control + pin, together proving the guard fired: <c>DirectLdr</c> IS a legitimate
    /// unit-leader approver of <c>Emp</c> (a DIFFERENT employee) — the predicate has real authority facts
    /// and returns TRUE — yet <c>(DirectLdr, DirectLdr)</c> (actor == employee) returns FALSE.</para>
    /// </summary>
    [Fact]
    public async Task ChokePoint_Predicate_DeniesSelf_Directly()
    {
        // Construct the authorizer exactly as the app wires it (ctor: DbConnectionFactory +
        // ReportingLineRepository), over the same test database — a read-only predicate, so a directly
        // constructed instance and the DI-resolved one evaluate identically.
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Positive control: DirectLdr is a real unit-leader approver of Emp, so the predicate returns TRUE
        // — without this, a self-FALSE could be a vacuous "no authority anywhere" rather than the guard.
        Assert.True(await authorizer.IsEffectiveApproverOrUnitLeaderAsync(DirectLdr, Emp, asOf: today),
            "precondition: DirectLdr must be a legitimate approver of Emp for the self-denial to be meaningful");

        // The pin: actor == employee → the FIRST-thing choke-point guard denies, even though DirectLdr
        // holds genuine approval authority over OTHERS.
        Assert.False(await authorizer.IsEffectiveApproverOrUnitLeaderAsync(DirectLdr, DirectLdr, asOf: today));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) POSITIVE SELF-MATCH — a real self-id actually denies (no no-op comparison).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Guards against a comparison that silently never matches (a no-op / wrong id space): a REAL
    /// self-id must be DENIED on BOTH approve and reject, and the period must stay SUBMITTED. If
    /// <c>ApprovalSelfGuard.IsSelf</c> were a no-op returning false, Hr (org-scope) would APPROVE their
    /// own period (200/APPROVED) — so 403 + unchanged status proves the equality actually fired.</summary>
    [Fact]
    public async Task PositiveSelfMatch_RealSelfId_Denies_Approve_And_Reject()
    {
        var pApprove = await InsertPeriodAsync(Hr, "SUBMITTED", 5);
        await AssertSelfDeniedAsync(await AdminRoleClient(StatsTidRoles.LocalHR, Hr).PostAsync($"/api/approval/{pApprove}/approve", null));
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pApprove));

        var pReject = await InsertPeriodAsync(Hr, "SUBMITTED", 6);
        await AssertSelfDeniedAsync(await AdminRoleClient(StatsTidRoles.LocalHR, Hr)
            .PostAsJsonAsync($"/api/approval/{pReject}/reject", new { reason = "self" }));
        Assert.Equal("SUBMITTED", await ReadStatusAsync(pReject));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3) GUARD ORDERING — self on an INELIGIBLE status still self-denies (403, not 409).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The guard precedes the status-eligibility check: a self-request on a status the approve
    /// endpoint would otherwise 409 (DRAFT — not SUBMITTED/EMPLOYEE_APPROVED; or an already-APPROVED
    /// period) STILL returns the self-denial 403, never a state-leaking 409. Both ineligible statuses
    /// are probed.</summary>
    [Fact]
    public async Task GuardOrdering_SelfOnIneligibleStatus_Returns403SelfDenial_Not409()
    {
        // DRAFT — the status check would 409 ("Only SUBMITTED or EMPLOYEE_APPROVED …"); the guard wins.
        var pDraft = await InsertPeriodAsync(Hr, "DRAFT", 5);
        var draftRsp = await AdminRoleClient(StatsTidRoles.LocalHR, Hr).PostAsync($"/api/approval/{pDraft}/approve", null);
        await AssertSelfDeniedAsync(draftRsp);
        Assert.NotEqual(HttpStatusCode.Conflict, draftRsp.StatusCode);
        Assert.Equal("DRAFT", await ReadStatusAsync(pDraft));

        // Already-APPROVED — the status check would also 409; the guard still wins with a 403.
        var pApproved = await InsertPeriodAsync(Hr, "APPROVED", 6);
        var approvedRsp = await AdminRoleClient(StatsTidRoles.LocalHR, Hr).PostAsync($"/api/approval/{pApproved}/approve", null);
        await AssertSelfDeniedAsync(approvedRsp);
        Assert.NotEqual(HttpStatusCode.Conflict, approvedRsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (4) REOPEN SPLIT — self-reopen of APPROVED DENIED; of EMPLOYEE_APPROVED ALLOWED (OQ-1a).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The reopen guard is scoped to the manager-DECIDED (APPROVED) source state only. An
    /// HR/Admin reopening their OWN <c>APPROVED</c> period (a manager decision) is DENIED (403, SoD);
    /// reopening their OWN <c>EMPLOYEE_APPROVED → DRAFT</c> (a pre-approval self-undo — owner ruling
    /// OQ-1a) is ALLOWED (200 → DRAFT), because a higher-role user files their own timesheet through the
    /// Leader arm and blocking it would strand their own self-correction.</summary>
    [Fact]
    public async Task ReopenSplit_HrSelfReopen_ApprovedDenied_EmployeeApprovedAllowed()
    {
        // APPROVED self-reopen → DENIED.
        var pApproved = await InsertPeriodAsync(Hr, "APPROVED", 5);
        var deniedRsp = await AdminRoleClient(StatsTidRoles.LocalHR, Hr)
            .PostAsJsonAsync($"/api/approval/{pApproved}/reopen", new { reason = "self" });
        await AssertSelfDeniedAsync(deniedRsp);
        Assert.Equal("APPROVED", await ReadStatusAsync(pApproved));

        // EMPLOYEE_APPROVED self-reopen → ALLOWED (pre-approval self-undo).
        var pEmpApproved = await InsertPeriodAsync(Hr, "EMPLOYEE_APPROVED", 6);
        var allowedRsp = await AdminRoleClient(StatsTidRoles.LocalHR, Hr)
            .PostAsJsonAsync($"/api/approval/{pEmpApproved}/reopen", new { reason = "self-undo" });
        Assert.Equal(HttpStatusCode.OK, allowedRsp.StatusCode);
        Assert.Equal("DRAFT", await ReadStatusAsync(pEmpApproved));
    }

    /// <summary>The Employee-role reopen arm is UNCHANGED: an ordinary employee reopening their OWN
    /// <c>EMPLOYEE_APPROVED → DRAFT</c> still succeeds (200 → DRAFT). The guard lives only in the Leader
    /// arm, so the two-step flow's self-undo is untouched.</summary>
    [Fact]
    public async Task ReopenEmployeeArm_Unchanged_EmployeeSelfReopenEmployeeApproved_Allowed()
    {
        var pEmpApproved = await InsertPeriodAsync(Emp, "EMPLOYEE_APPROVED", 5);
        var rsp = await EmployeeClient(Emp).PostAsJsonAsync($"/api/approval/{pEmpApproved}/reopen", new { reason = "fix" });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("DRAFT", await ReadStatusAsync(pEmpApproved));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (5) NO OVER-BLOCK — send + employee-approve self still SUCCEED (self by design).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary><c>send</c> is a self action by design and carries NO SoD guard. An employee sending their
    /// OWN month (a fully absence-covered, vacuously-balanced March) still succeeds → 200,
    /// EMPLOYEE_APPROVED.</summary>
    [Fact]
    public async Task NoOverBlock_SelfSend_Succeeds()
    {
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, SelfSend, OrgA, "HK", "OK24", ensureOrg: false);
        await CoverMarchWithAbsencesAsync(SelfSend);

        var rsp = await EmployeeClient(SelfSend).PostAsJsonAsync(
            "/api/approval/send", new { employeeId = SelfSend, year = 2026, month = 3 });

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {raw}");
        Assert.Equal("EMPLOYEE_APPROVED", JsonDocument.Parse(raw).RootElement.GetProperty("status").GetString());
    }

    /// <summary><c>employee-approve</c> (the by-id send adapter) is a self action by design and carries NO
    /// SoD guard. An employee employee-approving their OWN DRAFT period (a fully covered March) still
    /// succeeds → 200, EMPLOYEE_APPROVED.</summary>
    [Fact]
    public async Task NoOverBlock_SelfEmployeeApprove_Succeeds()
    {
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, SelfSend2, OrgA, "HK", "OK24", ensureOrg: false);
        await CoverMarchWithAbsencesAsync(SelfSend2);
        var periodId = await InsertPeriodWithRangeAsync(
            SelfSend2, "DRAFT", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var rsp = await EmployeeClient(SelfSend2).PostAsync($"/api/approval/{periodId}/employee-approve", null);

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {raw}");
        Assert.Equal("EMPLOYEE_APPROVED", JsonDocument.Parse(raw).RootElement.GetProperty("status").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        // (1) The isolated member unit under STY02.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES
                (@member, @orgA, NULL, 'kontor', 'SEC009 Member Unit')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("member", UnitMember);
            cmd.Parameters.AddWithValue("orgA", OrgA);
            await cmd.ExecuteNonQueryAsync();
        }

        // (2) The four-leg users (Emp + the three leaders + Hr). primary_org_id == STY02; the leaders
        //     + Emp home in the member unit (Hr has no unit). SelfSend/SelfSend2 are seeded per-test
        //     through RegressionSeed (they need the full profile chain the send resolver requires).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active)
            VALUES
                (@emp,    @emp,    '$2a$11$fake', 'SEC009 Emp',    'sec009_emp@test.dk',    @orgA, @member, 'HK','OK24', TRUE),
                (@pmgr,   @pmgr,   '$2a$11$fake', 'SEC009 PMgr',   'sec009_pmgr@test.dk',   @orgA, @member, 'HK','OK24', TRUE),
                (@direct, @direct, '$2a$11$fake', 'SEC009 Direct', 'sec009_direct@test.dk', @orgA, @member, 'HK','OK24', TRUE),
                (@vikar,  @vikar,  '$2a$11$fake', 'SEC009 Vikar',  'sec009_vikar@test.dk',  @orgA, @member, 'HK','OK24', TRUE),
                (@hr,     @hr,     '$2a$11$fake', 'SEC009 Hr',     'sec009_hr@test.dk',     @orgA, NULL,    'AC','OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddFourLegUserParams(cmd);
            cmd.Parameters.AddWithValue("orgA", OrgA);
            cmd.Parameters.AddWithValue("member", UnitMember);
            await cmd.ExecuteNonQueryAsync();
        }

        // (3) Role assignments — LOCAL_LEADER (hierarchy 4 = LeaderOrAbove) for the leaders/vikar,
        //     LOCAL_HR for Hr, EMPLOYEE for Emp.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
                (@pmgr,   'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST'),
                (@direct, 'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST'),
                (@vikar,  'LOCAL_LEADER', @orgA, 'ORG_ONLY', 'TEST'),
                (@hr,     'LOCAL_HR',     @orgA, 'ORG_ONLY', 'TEST'),
                (@emp,    'EMPLOYEE',     @orgA, 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddFourLegUserParams(cmd);
            cmd.Parameters.AddWithValue("orgA", OrgA);
            await cmd.ExecuteNonQueryAsync();
        }

        // (4) DirectLdr is the designated leader of Emp's OWN unit (the secondary unit-leader path).
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO unit_leaders (unit_id, user_id) VALUES (@member, @direct) ON CONFLICT DO NOTHING", conn))
        {
            cmd.Parameters.AddWithValue("member", UnitMember);
            cmd.Parameters.AddWithValue("direct", DirectLdr);
            await cmd.ExecuteNonQueryAsync();
        }

        // (5) VikarUsr is the ACTIVE stand-in for DirectLdr (→ UNIT_LEADER_VIKAR over DirectLdr's members).
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO manager_vikar (absent_approver_id, vikar_user_id, until_date, reason, organisation_id, created_by)
            VALUES (@direct, @vikar, @future, 'FERIE', @orgA, 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("direct", DirectLdr);
            cmd.Parameters.AddWithValue("vikar", VikarUsr);
            cmd.Parameters.AddWithValue("future", new DateOnly(2099, 12, 31));
            cmd.Parameters.AddWithValue("orgA", OrgA);
            await cmd.ExecuteNonQueryAsync();
        }

        // (6) Emp reports PRIMARY to PrimaryMgr — the designated EDGE approver.
        await new ReportingLineRepository(_dbFactory).AssignAsync(null, MakeLine(Emp, PrimaryMgr));
    }

    private void AddFourLegUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("pmgr", PrimaryMgr);
        cmd.Parameters.AddWithValue("direct", DirectLdr);
        cmd.Parameters.AddWithValue("vikar", VikarUsr);
        cmd.Parameters.AddWithValue("hr", Hr);
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

    // ════════════════════════════════════════════════════════════════════════════════
    //  Coverage seeding (send success) — vacuously-balanced March via full-day absences.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Covers every March-2026 weekday with a full-day VACATION absence. March 2026 carries no
    /// Danish public holiday (asserted), so covering the weekdays covers exactly the send command's
    /// expected-workday set; absences live in a table NEITHER side of the allocation gate reads, so the
    /// month is vacuously balanced (0 worked, 0 allocated) → the send passes both gates.</summary>
    private async Task CoverMarchWithAbsencesAsync(string employeeId)
    {
        var start = new DateOnly(2026, 3, 1);
        var end = new DateOnly(2026, 3, 31);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        await using (var holidayCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM danish_public_holidays WHERE holiday_date >= @s AND holiday_date <= @e", conn))
        {
            holidayCmd.Parameters.AddWithValue("s", start);
            holidayCmd.Parameters.AddWithValue("e", end);
            Assert.Equal(0L, (long)(await holidayCmd.ExecuteScalarAsync())!);
        }

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO absences_projection
                    (event_id, employee_id, date, absence_type, hours, feriedage,
                     agreement_code, ok_version, occurred_at, outbox_id)
                VALUES (gen_random_uuid(), @emp, @date, 'VACATION', 7.4, 1.0, 'HK', 'OK24', NOW(), @seq)
                ON CONFLICT DO NOTHING
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", d);
            cmd.Parameters.AddWithValue("seq", Interlocked.Increment(ref _absenceSeq));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Period seeding + reads
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Inserts an <c>approval_periods</c> row for <paramref name="employeeId"/> in the given
    /// 2026 month (each month is a distinct natural key, avoiding the exact-tuple unique constraint).</summary>
    private Task<Guid> InsertPeriodAsync(string employeeId, string status, int month)
        => InsertPeriodWithRangeAsync(employeeId, status,
            new DateOnly(2026, month, 1),
            new DateOnly(2026, month, DateTime.DaysInMonth(2026, month)));

    private async Task<Guid> InsertPeriodWithRangeAsync(string employeeId, string status, DateOnly start, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status,
                 agreement_code, ok_version, submitted_at, submitted_by,
                 employee_approved_at, employee_approved_by)
            VALUES
                (@id, @emp, @org, @start, @end, 'MONTHLY', @status, 'HK', 'OK24', NOW(), @emp,
                 CASE WHEN @status IN ('EMPLOYEE_APPROVED','APPROVED') THEN NOW() ELSE NULL END,
                 CASE WHEN @status IN ('EMPLOYEE_APPROVED','APPROVED') THEN @emp ELSE NULL END)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgA);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("status", status);
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

    // ════════════════════════════════════════════════════════════════════════════════
    //  Assertions
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Asserts the response is the SoD self-denial: 403 Forbidden AND a body whose
    /// <c>reason</c> names the segregation-of-duties rule — which distinguishes the SELF-GUARD firing
    /// from any downstream authorization / status / conflict outcome (the guard-ordering guarantee).</summary>
    private static async Task AssertSelfDeniedAsync(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {raw}");
        var reason = JsonDocument.Parse(raw).RootElement.GetProperty("reason").GetString();
        Assert.Contains("segregation of duties", reason);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Clients / tokens
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient LeaderClient(string userId, string scopeOrg = OrgA)
        => RoleClient(userId, StatsTidRoles.LocalLeader, "HK", scopeOrg);

    private HttpClient EmployeeClient(string userId, string scopeOrg = OrgA)
        => RoleClient(userId, StatsTidRoles.Employee, "HK", scopeOrg);

    private HttpClient AdminRoleClient(string role, string userId, string scopeOrg = OrgA)
        => RoleClient(userId, role, "AC", scopeOrg);

    private HttpClient RoleClient(string userId, string role, string agreementCode, string scopeOrg)
    {
        var client = _factory.CreateClient();
        var scopes = new[] { new RoleScope(role, scopeOrg, "ORG_ONLY") };
        var token = NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: role,
            agreementCode: agreementCode, orgId: scopeOrg, scopes: scopes);
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
