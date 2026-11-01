using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// SPRINT-147 / TASK-14712-T — QUAL-230 T5c (NR-10 (ii)), AC-230-15: a manager who has LEFT stops
/// being an approver over other people on day 1, not on day 6.
///
/// <para><b>Plain words.</b> The 5-day grace keeps a leaver switched on so that THEIR manager can still
/// sign their final month. If the leaver is herself a manager, that must not also keep her in charge of
/// her own reports. From the day after her last day, her reports' months go to the next manager up and
/// she can no longer approve them. A stand-in appointed BEFORE she left keeps working (owner ruling
/// Q-B = (c), P-2): that is deliberate and pinned below so nobody "fixes" it.</para>
///
/// <para><b>Setup rule (P-6).</b> Mette is a <b>LocalLeader whose only authority over her report R is the
/// designated reporting-line edge</b> (or, in fact 1b, a designated unit-leader row) — NOT HR-tier. The
/// approve handler admits an HR-tier actor through the org-scope fallback BEFORE it asks the edge
/// question (<c>ApprovalEndpoints.cs</c> ~:293-304), and that fallback reads the token's claims, never the
/// actor's state (SEC-003, R-25). An HR-tier Mette would make these facts pass or fail for the wrong
/// reason; <see cref="HrFallback_DepartedHr_InGrace_StillApproves_200"/> pins that separately.</para>
///
/// <para><b>Dates.</b> The host clock is pinned to <see cref="F"/> (a Wednesday). "Yesterday" is the Tuesday
/// before. The grace state is seeded directly: <c>is_active = TRUE</c> with <c>employment_end_date</c> in
/// the past, which is a real state between Copenhagen midnight and Step A's poll and for D+1..D+5.</para>
///
/// <para>All facts are Docker-gated: CI-pending until a checkpoint run shows them green.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DepartedApproverTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgA = "STY02";

    /// <summary>The pinned "today" (Copenhagen business date) — a Wednesday.</summary>
    private static readonly DateOnly F = new(2026, 6, 10);
    private static readonly DateOnly Yesterday = F.AddDays(-1);

    // ── Users (all STY02). ──
    private const string Mette = "s147d_mette";     // LocalLeader; the departing approver; PRIMARY manager of R, R2; reports to Gudrun
    private const string Gudrun = "s147d_gudrun";   // LocalLeader; Mette's OWN manager — the escalated approver
    private const string Nils = "s147d_nils";       // LocalLeader, active, NO end date; PRIMARY manager of R3; reports to Gudrun
    private const string Dora = "s147d_dora";       // LocalLeader, DEACTIVATED (is_active FALSE); PRIMARY manager of R5; reports to Gudrun
    private const string R = "s147d_r";             // Employee -> Mette
    private const string R2 = "s147d_r2";           // Employee -> Mette
    private const string R3 = "s147d_r3";           // Employee -> Nils
    private const string R4 = "s147d_r4";           // Employee in unit U, NO reporting-line edge; Mette leads U (fact 1b)
    private const string R5 = "s147d_r5";           // Employee -> Dora
    private const string Vikar = "s147d_vikar";     // LocalLeader, active — the stand-in
    private const string Hana = "s147d_hana";       // LocalHR — the HR actor (admin vikar route; org-scope fallback)

    private static readonly Guid UnitU = Guid.Parse("51470000-0000-0000-0000-000000000001");

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;
    private DbConnectionFactory _dbFactory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        // S147 TASK-14712-T fix-up 1 (W-1): no leaver-flip job (TASK-14719's reason) — otherwise the job, which
        // polls at host start and every 5 minutes, could deactivate a grace-state subject mid-fact and the
        // facts would test the deactivated case instead (passing on 3dbc42da: a weak Red condition).
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString, withoutLeaverJob: true);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        // PAT-008 boot order: the FIXED host boots (and runs the seeders) BEFORE any fixture is seeded.
        _host = _factory.WithFixedToday(F);
        _ = _host.CreateClient();

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await Exec(conn,
            "INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name) VALUES (@u, @org, NULL, 'kontor', 'S147 Unit U')",
            ("u", UnitU), ("org", OrgA));

        await InsertUserAsync(conn, Mette, "LOCAL_LEADER", unit: UnitU);
        await InsertUserAsync(conn, Gudrun, "LOCAL_LEADER");
        await InsertUserAsync(conn, Nils, "LOCAL_LEADER");
        await InsertUserAsync(conn, Dora, "LOCAL_LEADER", isActive: false);
        await InsertUserAsync(conn, R, "EMPLOYEE");
        await InsertUserAsync(conn, R2, "EMPLOYEE");
        await InsertUserAsync(conn, R3, "EMPLOYEE");
        await InsertUserAsync(conn, R4, "EMPLOYEE", unit: UnitU);
        await InsertUserAsync(conn, R5, "EMPLOYEE");
        await InsertUserAsync(conn, Vikar, "LOCAL_LEADER");
        await InsertUserAsync(conn, Hana, "LOCAL_HR");

        // Mette leads U (member of U herself — the D3 member invariant); R4 is a direct member with NO edge.
        await Exec(conn, "INSERT INTO unit_leaders (unit_id, user_id) VALUES (@u, @m)", ("u", UnitU), ("m", Mette));

        var rl = new ReportingLineRepository(_dbFactory);
        await rl.AssignAsync(null, Line(R, Mette));
        await rl.AssignAsync(null, Line(R2, Mette));
        await rl.AssignAsync(null, Line(Mette, Gudrun));
        await rl.AssignAsync(null, Line(Nils, Gudrun));
        await rl.AssignAsync(null, Line(Dora, Gudrun));
        await rl.AssignAsync(null, Line(R3, Nils));
        await rl.AssignAsync(null, Line(R5, Dora));
    }

    private static async Task InsertUserAsync(
        NpgsqlConnection conn, string id, string roleId, bool isActive = true, Guid? unit = null)
    {
        await Exec(conn,
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, unit_id, agreement_code, ok_version, is_active)
            VALUES (@id, @id, '$2a$11$fake', @id, @email, @org, @unit, 'HK', 'OK24', @active)
            """,
            ("id", id), ("email", id + "@test.dk"), ("org", OrgA),
            ("unit", (object?)unit ?? DBNull.Value), ("active", isActive));
        await Exec(conn,
            "INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by, expires_at) VALUES (@id, @role, @org, 'ORG_ONLY', 'TEST', NULL)",
            ("id", id), ("role", roleId), ("org", OrgA));
    }

    private static ReportingLineModel Line(string employeeId, string managerId) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        OrganisationId = OrgA,
        Relationship = "PRIMARY",
        EffectiveFrom = F.AddYears(-1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    private static async Task Exec(NpgsqlConnection conn, string sql, params (string Name, object Value)[] ps)
    {
        // NpgsqlBatch (not NpgsqlCommand) so CA2100's warning budget does not grow: every caller passes a
        // literal, and the values travel as parameters.
        await using var batch = new NpgsqlBatch(conn);
        var cmd = new NpgsqlBatchCommand(sql);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        batch.BatchCommands.Add(cmd);
        await batch.ExecuteNonQueryAsync();
    }

    private async Task SetEndDateAsync(string userId, DateOnly? end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await Exec(conn, "UPDATE users SET employment_end_date = @d WHERE user_id = @id",
            ("d", end.HasValue ? end.Value : DBNull.Value), ("id", userId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  1 / 1b / 1c / 1d — the departed approver is refused
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind F.</b> Mette (active, last day yesterday = the grace state, LocalLeader, authority = the edge only)
    /// approving R's EMPLOYEE_APPROVED month is refused 403, the month stays EMPLOYEE_APPROVED, and the body
    /// carries no date (ADR-040 D7).
    /// <para><b>Red conditions:</b> <c>Assert.Equal(Forbidden, rsp.StatusCode)</c> — today the role floor
    /// (<c>u.is_active = TRUE</c>) admits her and the resolver returns her, so she gets 200.</para>
    /// </summary>
    [Fact]
    public async Task DepartedManager_InGrace_Approve_Is403_MonthStaysPending_BodyDateFree()
    {
        await SetEndDateAsync(Mette, Yesterday);
        var period = await InsertPeriodAsync(R, "EMPLOYEE_APPROVED");

        var rsp = await Client(Mette).PostAsync($"/api/approval/{period}/approve", null);
        var body = await rsp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("EMPLOYEE_APPROVED", await ReadColumnAsync(period, "status"));
        Assert.DoesNotContain(Yesterday.ToString("yyyy-MM-dd"), body);
    }

    /// <summary>
    /// <b>Kind F (P-6).</b> Mette as a designated <b>unit leader</b> of R4's unit, with NO reporting-line edge to
    /// R4: on D+1 her approve of R4's month is 403. The unit-leader path rides the same role floor.
    /// <para><b>Red conditions:</b> <c>Assert.Equal(Forbidden, ...)</c> — today the floor admits her and the
    /// unit-leader leg returns UNIT_LEADER, so 200.</para>
    /// </summary>
    [Fact]
    public async Task DepartedUnitLeader_InGrace_Approve_Is403_NoEdge()
    {
        await SetEndDateAsync(Mette, Yesterday);
        var period = await InsertPeriodAsync(R4, "EMPLOYEE_APPROVED");

        var rsp = await Client(Mette).PostAsync($"/api/approval/{period}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("EMPLOYEE_APPROVED", await ReadColumnAsync(period, "status"));
    }

    /// <summary>
    /// <b>Kind B — boundary pin, holds today (the comparison is "end date &lt; today", so the last day itself
    /// still counts).</b> With her end date = today, Mette still approves through the edge (R) and through the
    /// unit-leader row (R4): 200 and 200. Together with the two facts above this proves the 403 is about the
    /// departure and not about some other fixture gap.
    /// <para><b>Red conditions:</b> a "&lt;=" off-by-one in the reads approve reaches (the role floor, read 1, and the resolver's activity check, read 3) makes one of these 403.</para>
    /// </summary>
    [Fact]
    public async Task Manager_OnHerLastDay_StillApproves_200_EdgeAndUnitLeader()
    {
        await SetEndDateAsync(Mette, F);
        var pEdge = await InsertPeriodAsync(R, "EMPLOYEE_APPROVED");
        var pUnit = await InsertPeriodAsync(R4, "EMPLOYEE_APPROVED");

        Assert.Equal(HttpStatusCode.OK, (await Client(Mette).PostAsync($"/api/approval/{pEdge}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(Mette).PostAsync($"/api/approval/{pUnit}/approve", null)).StatusCode);
        Assert.Equal("APPROVED", await ReadColumnAsync(pEdge, "status"));
        Assert.Equal("APPROVED", await ReadColumnAsync(pUnit, "status"));
    }

    /// <summary>
    /// <b>Kind R — regression pin, holds today.</b> The other arm of "acting user": a DEACTIVATED manager
    /// (<c>is_active = FALSE</c>, no end date) is still refused. Separate fixture from the end-dated arm, so
    /// neither arm can hide behind the other.
    /// <para><b>Red conditions:</b> dropping the <c>is_active</c> half of the rule makes this 200.</para>
    /// </summary>
    [Fact]
    public async Task DeactivatedManager_Approve_Is403()
    {
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await Exec(conn, "UPDATE users SET is_active = FALSE WHERE user_id = @id", ("id", Mette));
        }
        var period = await InsertPeriodAsync(R, "EMPLOYEE_APPROVED");

        var rsp = await Client(Mette).PostAsync($"/api/approval/{period}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Equal("EMPLOYEE_APPROVED", await ReadColumnAsync(period, "status"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  2 — the resolver escalates; the escalated manager approves
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind F.</b> At D+1 the resolver returns Mette's OWN manager (Gudrun) for R, and Gudrun's approve of
    /// R's month is 200, recorded with Gudrun as designated approver.
    /// <para><b>Red conditions:</b> <c>Assert.Equal(Gudrun, resolved.ManagerId)</c> — today the resolver
    /// returns Mette (active, so no escalation); and the 200 for Gudrun fails today because the edge
    /// resolves to Mette, not her.</para>
    /// </summary>
    [Fact]
    public async Task Resolver_ReturnsEscalatedManager_AndEscalatedManagerApproves_200()
    {
        await SetEndDateAsync(Mette, Yesterday);
        var period = await InsertPeriodAsync(R, "EMPLOYEE_APPROVED");

        var repo = new ReportingLineRepository(_dbFactory);
        var resolved = await repo.ResolveDesignatedApproverAsync(R, default, asOf: F);
        Assert.Equal(Gudrun, resolved.ManagerId);

        var rsp = await Client(Gudrun).PostAsync($"/api/approval/{period}/approve", null);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadColumnAsync(period, "status"));
        Assert.Equal(Gudrun, await ReadColumnAsync(period, "designated_approver_id"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  3 — the pending list and the resolver agree (see = act)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind F.</b> At D+1 Gudrun's pending list contains R's month (and R2's); Mette's contains neither; and
    /// the resolver names Gudrun for both.
    /// <para><b>Red conditions:</b> <c>Assert.Contains(R, gudrunList)</c> — today the walk does not descend past
    /// an ACTIVE Mette, so Gudrun's list misses R; and <c>Assert.DoesNotContain(R, metteList)</c> — today Mette
    /// still sees and may act.</para>
    /// </summary>
    [Fact]
    public async Task PendingList_EscalatedManagerSeesTheMonth_DepartedDoesNot_AndAgreesWithResolver()
    {
        await SetEndDateAsync(Mette, Yesterday);
        await InsertPeriodAsync(R, "EMPLOYEE_APPROVED");
        await InsertPeriodAsync(R2, "EMPLOYEE_APPROVED");

        var gudrunList = await PendingEmployeesAsync(Gudrun);
        var metteList = await PendingEmployeesAsync(Mette);

        Assert.Contains(R, gudrunList);
        Assert.Contains(R2, gudrunList);
        Assert.DoesNotContain(R, metteList);
        Assert.DoesNotContain(R2, metteList);

        var repo = new ReportingLineRepository(_dbFactory);
        Assert.Equal(Gudrun, (await repo.ResolveDesignatedApproverAsync(R, default, asOf: F)).ManagerId);
        Assert.Equal(Gudrun, (await repo.ResolveDesignatedApproverAsync(R2, default, asOf: F)).ManagerId);
    }

    /// <summary>
    /// <b>Kind F — the drift guard for the walk's hand-copied SQL form of the rule (security consult, residual 7).</b>
    /// For every (actor, subject) pair over {Gudrun, Mette, Nils} x {R, R2, R3, Mette, Gudrun, Nils} at D+1 —
    /// the actor = subject pairs included (RES-003: nobody sees or acts on her own month) — the actor appears in
    /// the pending list EXACTLY when the live authorizer admits her AND exactly when the resolver names her.
    /// Explicit expectations: Gudrun {Mette, Nils, R, R2} (Mette's and Nils's own months go to their manager,
    /// R and R2 escalate past the departed Mette), Mette {}, Nils {R3}.
    /// <para><b>Red conditions:</b> the explicit sets (Gudrun lacks R/R2 today) and the equality of list inclusion
    /// with the authorizer's verdict. <b>Limit:</b> the pending list's stage-b filter runs the authorizer, so an
    /// OVER-inclusive walk (including a SQL NULL-end-date bug that made the walk descend) is masked in the list;
    /// this fact guards UNDER-inclusion (the walk missing a departed manager). It does NOT pin the SQL NULL case.</para>
    /// </summary>
    [Fact]
    public async Task SeeEqualsAct_ForEveryActorSubjectPair_IncludingSelfPairs()
    {
        await SetEndDateAsync(Mette, Yesterday);
        var subjects = new[] { R, R2, R3, Mette, Gudrun, Nils };
        foreach (var r in subjects)
            await InsertPeriodAsync(r, "EMPLOYEE_APPROVED");

        var lists = new Dictionary<string, HashSet<string>>
        {
            [Gudrun] = await PendingEmployeesAsync(Gudrun),
            [Mette] = await PendingEmployeesAsync(Mette),
            [Nils] = await PendingEmployeesAsync(Nils),
        };

        Assert.Equal(
            new[] { Mette, Nils, R, R2 }.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            lists[Gudrun].OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Empty(lists[Mette]);
        Assert.Equal(new[] { R3 }, lists[Nils].ToArray());

        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, new ReportingLineRepository(_dbFactory), new FixedTimeProvider(F));
        var repo = new ReportingLineRepository(_dbFactory);
        foreach (var actor in lists.Keys)
        {
            foreach (var report in subjects)
            {
                var listed = lists[actor].Contains(report);
                var acts = await authorizer.IsEffectiveApproverOrUnitLeaderAsync(actor, report, asOf: F);
                var resolvedIsActor = (await repo.ResolveDesignatedApproverAsync(report, default, asOf: F)).ManagerId == actor;
                Assert.True(listed == acts, $"see != act for {actor} over {report}: listed={listed} authorizer={acts}");
                Assert.True(acts == resolvedIsActor, $"act != resolver for {actor} over {report}: authorizer={acts} resolver={resolvedIsActor}");
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  4 — the stand-ins (AC-230-15 last bullet; P-2; owner Q-B = (c)) — boundary pins
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind B — boundary pin — holds today; owner Q-B = (c) (ruled, not provisional). Do NOT "fix".</b>
    /// A stand-in Mette appointed for herself BEFORE she left (via <c>POST /api/reporting-lines/delegate</c>,
    /// covering today) keeps approving R's month on D+1 (her last day was yesterday): 200. Outcome recorded
    /// in the report: the stand-in survives the manager's departure until its end date or HR's revocation.
    /// <para><b>Red conditions:</b> approving R exercises the resolver's vikar consult (read 3's stand-in leg) and
    /// the role floor for the holder; a delegator-side rule there (skipping a stand-in whose absent approver has
    /// left) turns this into 403. It does NOT exercise the unit-leader stand-in reads or the pending-list
    /// stand-in legs; <see cref="StandInOfDepartedUnitLeader_StillApprovesUnitMember_200"/> covers the former.</para>
    /// </summary>
    [Fact]
    public async Task StandInOfDepartedManager_SelfAppointed_StillApproves_200()
    {
        var create = await Client(Mette).PostAsJsonAsync("/api/reporting-lines/delegate",
            new { actingManagerId = Vikar, effectiveTo = F.AddDays(20).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode); // appointed while still employed (D = F)
        await SetEndDateAsync(Mette, Yesterday);            // she has now left: D+1

        var period = await InsertPeriodAsync(R, "EMPLOYEE_APPROVED");
        var rsp = await Client(Vikar).PostAsync($"/api/approval/{period}/approve", null);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadColumnAsync(period, "status"));
    }

    /// <summary>
    /// <b>Kind B — boundary pin — holds today; owner Q-B = (c) (ruled, not provisional). Do NOT "fix".</b>
    /// A stand-in HR appointed for Mette (via <c>POST /api/admin/reporting-lines/{managerId}/vikar</c>, covering
    /// today) keeps approving R's month on D+1: 200.
    /// <para><b>Red conditions:</b> as for the self-appointed fact (the resolver's vikar consult only).</para>
    /// </summary>
    [Fact]
    public async Task StandInOfDepartedManager_HrAppointed_StillApproves_200()
    {
        var create = await Client(Hana, StatsTidRoles.LocalHR).PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mette}/vikar",
            new { vikarUserId = Vikar, effectiveTo = F.AddDays(20).ToString("yyyy-MM-dd"), reason = "ANDET" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        await SetEndDateAsync(Mette, Yesterday);

        var period = await InsertPeriodAsync(R, "EMPLOYEE_APPROVED");
        var rsp = await Client(Vikar).PostAsync($"/api/approval/{period}/approve", null);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadColumnAsync(period, "status"));
    }

    /// <summary>
    /// <b>Kind B — boundary pin — holds today; owner Q-B = (c) (the unit-leader half). Do NOT "fix".</b> Mette
    /// (designated unit leader of R4's unit, no edge to R4) appoints Vikar as her stand-in while still employed,
    /// then leaves (last day yesterday). Vikar's approve of R4's month on D+1 is 200 through the unit-leader
    /// stand-in route (<c>GetUnitLeaderKind</c>'s vikar leg), which T5c deliberately does not touch.
    /// <para><b>Red conditions:</b> any rule that ignores a stand-in for a departed unit leader makes this 403.</para>
    /// </summary>
    [Fact]
    public async Task StandInOfDepartedUnitLeader_StillApprovesUnitMember_200()
    {
        var create = await Client(Mette).PostAsJsonAsync("/api/reporting-lines/delegate",
            new { actingManagerId = Vikar, effectiveTo = F.AddDays(20).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        await SetEndDateAsync(Mette, Yesterday);

        var period = await InsertPeriodAsync(R4, "EMPLOYEE_APPROVED");
        var rsp = await Client(Vikar).PostAsync($"/api/approval/{period}/approve", null);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("APPROVED", await ReadColumnAsync(period, "status"));
    }

    /// <summary>
    /// <b>Kind F (security 5a B-1; P-1's holder rule).</b> A stand-in who has LEFT herself grants nothing, and the
    /// month goes back to the absent manager. Mette (no end date) delegates to Vikar, covering today; then Vikar's
    /// last day is yesterday. Vikar's approve of R is 403; the resolver returns Mette for R; Mette's approve is 200;
    /// R is on Mette's pending list and not on Vikar's.
    /// <para><b>Red conditions (on 3dbc42da):</b> <c>Assert.Equal(Mette, resolved.ManagerId)</c> — the resolver
    /// returns Vikar (the holder's own state was not checked beyond is_active) — and Vikar's 403 — she gets 200.
    /// A regression that also dropped the fall-through would leave the month stuck (Mette, Vikar and Gudrun all
    /// refused): the 200 for Mette and her pending-list entry catch that.</para>
    /// </summary>
    [Fact]
    public async Task DepartedStandIn_GrantsNothing_MonthReturnsToTheAbsentManager()
    {
        var create = await Client(Mette).PostAsJsonAsync("/api/reporting-lines/delegate",
            new { actingManagerId = Vikar, effectiveTo = F.AddDays(20).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        await SetEndDateAsync(Vikar, Yesterday);

        var period = await InsertPeriodAsync(R, "EMPLOYEE_APPROVED");

        var vikarRsp = await Client(Vikar).PostAsync($"/api/approval/{period}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, vikarRsp.StatusCode);
        Assert.Equal("EMPLOYEE_APPROVED", await ReadColumnAsync(period, "status"));

        var repo = new ReportingLineRepository(_dbFactory);
        Assert.Equal(Mette, (await repo.ResolveDesignatedApproverAsync(R, default, asOf: F)).ManagerId);

        Assert.Contains(R, await PendingEmployeesAsync(Mette));
        Assert.DoesNotContain(R, await PendingEmployeesAsync(Vikar));

        var metteRsp = await Client(Mette).PostAsync($"/api/approval/{period}/approve", null);
        Assert.Equal(HttpStatusCode.OK, metteRsp.StatusCode);
        Assert.Equal("APPROVED", await ReadColumnAsync(period, "status"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  5 — the HR/Admin org-scope fallback is a DELIBERATE NON-CHANGE
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind B — boundary pin; not changed by T5c; bounded by TASK-14710's login refusal and the SEC-003
    /// token-lifetime residual.</b> A departed HR user (Hana: active, last day yesterday) holding a token still
    /// passes the org-scope fallback on approve (it reads the token's claims, not her state): 200. T5c narrows the
    /// EDGE and UNIT-LEADER paths only; this is recorded so nobody reads T5c as closing the fallback.
    /// <para><b>Red conditions:</b> making the fallback departed-aware (a deliberate future change) turns this 403.</para>
    /// </summary>
    [Fact]
    public async Task HrFallback_DepartedHr_InGrace_StillApproves_200()
    {
        await SetEndDateAsync(Hana, Yesterday);
        var period = await InsertPeriodAsync(R, "EMPLOYEE_APPROVED");

        var rsp = await Client(Hana, StatsTidRoles.LocalHR).PostAsync($"/api/approval/{period}/approve", null);

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  6 — live vs prefetched agree (RES-003 item 4), incl. deactivated leaders
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind N (differential; calls <c>IsActingLeaderOrAboveAsync</c> and the date-taking resolver, created this
    /// sprint, so it cannot compile on 3dbc42da — a compile error is not RED).</b> The live and the prefetched forms of the role floor, the resolver and the
    /// combined verdict agree, and both DENY a still-active-but-departed leader (Mette, last day yesterday) and a
    /// DEACTIVATED leader (Dora, <c>is_active = FALSE</c>; the prefetched floor now also returns deactivated
    /// leaders and relies on the C# rule to deny them — implementation deviation 4). Explicit values are
    /// asserted (Mette false, Dora false, Gudrun true on both floors; R and R5 resolve to Gudrun on both), so the
    /// comparison cannot pass vacuously. Self-pairs are included.
    /// <para><b>Named mutations (argued — Docker-only; run in a detached worktree at CI):</b> (M1) drop the
    /// end-date check in <c>PrefetchedAuthorityFacts.IsActingLeaderOrAboveAsync</c>, keeping the API: the prefetched
    /// floor admits Mette, so <c>Assert.Equal(false, prefetched floor(Mette))</c> fails and the live-vs-prefetched
    /// verdict for (Mette, R) diverges. (M2) drop the <c>is_active</c> half there: the prefetched floor admits Dora.</para>
    /// <para><b>Red conditions:</b> <c>Assert.False(floor(Mette))</c> on both forms;
    /// <c>Assert.False(prefetchedFloor(Dora))</c> if the prefetched rule ever stops denying deactivated leaders;
    /// any divergence between the forms for any (candidate, employee) pair.</para>
    /// </summary>
    [Fact]
    public async Task LiveAndPrefetched_DenyDepartedAndDeactivatedLeaders_AndAgree()
    {
        await SetEndDateAsync(Mette, Yesterday);

        string pathParam;
        await using (var c0 = new NpgsqlConnection(_harness.ConnectionString))
        {
            await c0.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT materialized_path FROM organizations WHERE org_id = @o", c0);
            cmd.Parameters.AddWithValue("o", OrgA);
            pathParam = (string)(await cmd.ExecuteScalarAsync())! + "%";
        }

        var vikarRepo = new ManagerVikarRepository(_dbFactory);
        var repo = new ReportingLineRepository(_dbFactory, vikarRepo);
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, repo, new FixedTimeProvider(F));

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);

        var sqlLines = repo.CreateSqlDataSource(conn, tx);
        var lines = await PrefetchedReportingLineDataSource.BuildAsync(conn, tx, pathParam, F, sqlLines, default);
        var prefetchedFacts = await PrefetchedAuthorityFacts.BuildAsync(conn, tx, pathParam, F, default);
        var liveFacts = new SqlAuthorityFactsSource(conn, tx, F);

        // Role floor, both forms, explicit.
        foreach (var (user, expected) in new[] { (Mette, false), (Dora, false), (Gudrun, true), (Nils, true) })
        {
            Assert.Equal(expected, await liveFacts.IsActingLeaderOrAboveAsync(user, F, default));
            Assert.Equal(expected, await prefetchedFacts.IsActingLeaderOrAboveAsync(user, F, default));
        }

        // Resolver triples, both forms, for every employee; explicit escalation targets.
        var everyone = new[] { R, R2, R3, R4, R5, Mette, Gudrun, Nils, Dora, Vikar };
        foreach (var e in everyone)
        {
            var viaSql = await repo.ResolveDesignatedApproverAsync(sqlLines, e, F);
            var viaPre = await repo.ResolveDesignatedApproverAsync(lines, e, F);
            Assert.Equal(viaSql, viaPre);
        }
        Assert.Equal(Gudrun, (await repo.ResolveDesignatedApproverAsync(lines, R, F)).ManagerId);   // past departed Mette
        Assert.Equal(Gudrun, (await repo.ResolveDesignatedApproverAsync(lines, R5, F)).ManagerId);  // past deactivated Dora

        // Combined verdict, every (candidate, employee) pair incl. self-pairs, both forms.
        var candidates = new[] { Mette, Dora, Gudrun, Nils, Vikar };
        var divergences = new List<string>();
        var verdict = new Dictionary<(string, string), bool>();
        foreach (var cand in candidates)
        {
            foreach (var emp in everyone)
            {
                var live = await authorizer.IsEffectiveApproverOrUnitLeaderAsync(
                    conn, tx, ctx: null, source: null, facts: null, cand, emp, F, default);
                var pre = await authorizer.IsEffectiveApproverOrUnitLeaderAsync(
                    conn, tx, ctx: null, lines, prefetchedFacts, cand, emp, F, default);
                verdict[(cand, emp)] = live;
                if (live != pre) divergences.Add($"({cand} -> {emp}): live={live} prefetched={pre}");
            }
        }
        Assert.Empty(divergences);
        Assert.False(verdict[(Mette, R)]);   // departed: denied on both forms
        Assert.False(verdict[(Mette, R4)]);  // departed unit leader: denied on both forms
        Assert.False(verdict[(Dora, R5)]);   // deactivated: denied on both forms
        Assert.False(verdict[(Mette, Mette)]);
        Assert.True(verdict[(Gudrun, R)]);   // non-vacuity: the escalated manager IS admitted
        Assert.True(verdict[(Nils, R3)]);

        await tx.RollbackAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  7 — EnsureContextDateMatches
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind N</b> (names <c>EnsureContextDateMatches</c>, created this sprint). A context built for date F,
    /// asked with asOf F+1, throws <see cref="InvalidOperationException"/> naming F; asked with asOf = null it
    /// uses its own date and answers (Gudrun is the escalated approver of R: true).
    /// <para><b>Why Docker, not Unit:</b> an <c>ApprovalAuthorityContext</c> is only accepted with a real
    /// REPEATABLE READ transaction (<c>EnsureContextIsSnapshotBound</c>), and a transaction needs a database.</para>
    /// <para><b>Red conditions (argued; Docker):</b> mutation "delete the throw in
    /// <c>DesignatedApproverAuthorizer.EnsureContextDateMatches</c>, keep the method" makes the F+1 call
    /// return a bool instead of throwing, so <c>Assert.ThrowsAsync</c> fails; mutation "throw even when
    /// <c>ctx.AsOf == effectiveAsOf</c>" fails the null-asOf half.</para>
    /// </summary>
    [Fact]
    public async Task ApprovalAuthorityContext_AskedForAnotherDay_Throws_AskedWithNull_UsesItsOwnDate()
    {
        await SetEndDateAsync(Mette, Yesterday);
        var repo = new ReportingLineRepository(_dbFactory);
        var authorizer = new DesignatedApproverAuthorizer(_dbFactory, repo, new FixedTimeProvider(F));

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);

        using (var ctx = new ApprovalAuthorityContext(F))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                authorizer.IsEffectiveApproverOrUnitLeaderAsync(conn, tx, ctx, Gudrun, R, F.AddDays(1), default));
            Assert.Contains(F.ToString("yyyy-MM-dd"), ex.Message);
        }

        using (var ctx = new ApprovalAuthorityContext(F))
        {
            Assert.True(await authorizer.IsEffectiveApproverOrUnitLeaderAsync(conn, tx, ctx, Gudrun, R, asOf: null, default));
        }

        await tx.RollbackAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient Client(string userId, string role = StatsTidRoles.LocalLeader)
    {
        var client = _host.CreateClient();
        var scopes = new[] { new RoleScope(role, OrgA, "ORG_ONLY") };
        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: userId, name: userId, role: role,
            agreementCode: "HK", orgId: OrgA, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HashSet<string>> PendingEmployeesAsync(string actor)
    {
        var rsp = await Client(actor).GetAsync("/api/approval/pending?my-reports=true");
        rsp.EnsureSuccessStatusCode();
        var doc = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var el in doc.EnumerateArray())
            ids.Add(el.GetProperty("employeeId").GetString()!);
        return ids;
    }

    /// <summary>Inserts a month in the given status. The sender (employee_approved_by / submitted_by) is the
    /// employee, so SEC-053's four-eyes guard never confounds the actor under test.</summary>
    private async Task<Guid> InsertPeriodAsync(string employeeId, string status)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await Exec(conn,
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version,
                 submitted_at, submitted_by, employee_approved_at, employee_approved_by)
            VALUES
                (@id, @emp, @org, @start, @end, 'MONTHLY', @status, 'HK', 'OK24', NOW(), @emp, NOW(), @emp)
            """,
            ("id", id), ("emp", employeeId), ("org", OrgA),
            ("start", new DateOnly(2026, 5, 1)), ("end", new DateOnly(2026, 5, 31)), ("status", status));
        return id;
    }

    private async Task<string?> ReadColumnAsync(Guid periodId, string column)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = column switch
        {
            "status" => new NpgsqlCommand("SELECT status FROM approval_periods WHERE period_id = @id", conn),
            "designated_approver_id" => new NpgsqlCommand("SELECT designated_approver_id FROM approval_periods WHERE period_id = @id", conn),
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };
        cmd.Parameters.AddWithValue("id", periodId);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : (string)result;
    }
}
