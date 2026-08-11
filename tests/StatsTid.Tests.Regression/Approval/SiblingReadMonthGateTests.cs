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
/// S128 / TASK-12804 — the RES-002 closure: the leader-tier MONTH GATE on the three year+month
/// sibling READ endpoints that previously served a not-sent month's figures to the same leader-tier
/// actors the display surfaces withheld them from:
/// <list type="bullet">
///   <item><description><c>GET /api/approval/{id}/allocation-breakdown</c> (designated-edge population);</description></item>
///   <item><description><c>GET /api/compliance/{id}/period</c> (self / org-scope / designated-edge);</description></item>
///   <item><description><c>GET /api/balance/{id}/summary</c> (self / org-scope — NO edge branch, R5).</description></item>
/// </list>
///
/// <para><b>The rulings pinned here (S128):</b> R1 TIERED — self and HR-or-above (covering scope at
/// the LocalHR floor, the corrective tier) are exempt; leader-tier actors are gated by
/// <c>ApprovalVisibility.IsSubmittedToManager</c> (SUBMITTED / EMPLOYEE_APPROVED / APPROVED pass;
/// REJECTED, DRAFT and — fail-closed — a missing row are withheld). R5 NARROW-ONLY — each
/// endpoint's existing access population is untouched (the pre-existing forbidden-arm tests in
/// <see cref="AllocationBreakdownEndpointTests"/> / <c>TerminatedEmployeeAccessTests</c> stay green
/// UNMODIFIED as the zero-widening proof). R6 — the withhold is 403 with the Skema month-GET body
/// ("The month has not been submitted for approval"), asserted verbatim below so a month-gate 403
/// is never confused with an auth 403.</para>
///
/// <para><b>Falsification:</b> each gated arm turns red if the corresponding endpoint's
/// <c>ApprovalReadTier.IsLeaderTierReadAsync</c>/<c>IsSubmittedToManager</c> block is removed (the
/// figures come back 200), and each exempt/SENT arm turns red if the gate over-reaches (self,
/// HR-or-above, or a SENT month starts 403ing). Compliance's non-403 arms assert NOT-403 rather
/// than 200 because this Postgres-only harness has no rule engine (auth+gate pass ⇒ 5xx there,
/// never 403 — the <see cref="AllocationBreakdownEndpointTests"/> B2 convention).</para>
///
/// <para>Topology mirrors <see cref="RejectedMonthVisibilityTests"/>: isolated <c>t128g_*</c> users
/// on the baseline STY02 Organisation; Mgr holds the PRIMARY designated edge over Emp (and covering
/// LOCAL_LEADER org-scope); Hr holds covering LOCAL_HR org-scope, plus the PRIMARY edge over the
/// separate EmpHr so the HR-exemption is provable on the edge-only breakdown population too. Months:
/// May 2026 = REJECTED, June 2026 = NO ROW, July 2026 = SUBMITTED — each with recognizable non-zero
/// figures seeded, so a 403 withholds something real. FRESH testcontainer, cleaned before + after
/// (FAIL-002).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SiblingReadMonthGateTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Emp = "t128g_emp";       // STY02 — the gated employee (Mgr's PRIMARY report)
    private const string Mgr = "t128g_mgr";       // STY02 — LOCAL_LEADER, designated approver of Emp
    private const string Hr = "t128g_hr";         // STY02 — LOCAL_HR covering scope (+ edge over EmpHr)
    private const string EmpHr = "t128g_emp_hr";  // STY02 — reports PRIMARY to Hr (HR-edge population proof)
    private const string Org = "STY02";

    private const string SentinelTask = "T128G-TASK";
    private const string MonthGateReason = "The month has not been submitted for approval";

    // May = REJECTED, June = NO ROW (fail-closed), July = SUBMITTED.
    private const int Year = 2026;
    private const int RejMonth = 5;
    private const int NoRowMonth = 6;
    private const int SentMonth = 7;

    private static readonly string[] AllUsers = { Emp, Mgr, Hr, EmpHr };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;
    private int _outboxSeq = 1;

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
    //  1) GET /api/approval/{id}/allocation-breakdown
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Leader tier + REJECTED month ⇒ the month-gate 403 (the S127/R1 status set minus
    /// REJECTED), with the seeded figures NOT in the body.</summary>
    [Fact]
    public async Task Breakdown_LeaderTier_RejectedMonth_Is403()
    {
        var rsp = await GetAsync(Mgr, StatsTidRoles.LocalLeader, BreakdownUrl(Emp, RejMonth));
        await AssertMonthGate403Async(rsp);
        Assert.DoesNotContain(SentinelTask, await rsp.Content.ReadAsStringAsync());
    }

    /// <summary>Leader tier + NO approval_periods row ⇒ 403 — the fail-closed arm (null status is
    /// never "sent"). Figures ARE seeded for June, so the withhold is non-vacuous.</summary>
    [Fact]
    public async Task Breakdown_LeaderTier_NoPeriodRow_Is403_FailClosed()
    {
        var rsp = await GetAsync(Mgr, StatsTidRoles.LocalLeader, BreakdownUrl(Emp, NoRowMonth));
        await AssertMonthGate403Async(rsp);
    }

    /// <summary>Leader tier + SUBMITTED month ⇒ 200 with the real figures — the gate withholds a
    /// not-sent month, it must not narrow the designated approver's legitimate read.</summary>
    [Fact]
    public async Task Breakdown_LeaderTier_SubmittedMonth_Is200()
    {
        var rsp = await GetAsync(Mgr, StatsTidRoles.LocalLeader, BreakdownUrl(Emp, SentMonth));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7.4m, body.GetProperty("worked").GetDecimal());
        Assert.Equal(SentinelTask,
            body.GetProperty("allocations").EnumerateArray().Single().GetProperty("taskId").GetString());
    }

    /// <summary>HR-OR-ABOVE (covering LOCAL_HR scope) who also sits in this endpoint's edge-only
    /// population (Hr is EmpHr's PRIMARY designated approver) reads an UNSENT month ⇒ 200 — the
    /// corrective tier is exempt from the month gate (R1). The population itself is unchanged: an
    /// HR without the edge stays 403 (pinned by the existing forbidden arms, zero-widening R5).</summary>
    [Fact]
    public async Task Breakdown_HrOrAboveWithEdge_UnsentMonth_Is200_CorrectiveTierExempt()
    {
        var rsp = await GetAsync(Hr, StatsTidRoles.LocalHR, BreakdownUrl(EmpHr, NoRowMonth));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4.5m, body.GetProperty("worked").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  2) GET /api/compliance/{id}/period
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Leader tier + REJECTED month ⇒ the month-gate 403 (fires BEFORE the rule-engine
    /// round-trip, so it is deterministic in this rule-engine-less harness).</summary>
    [Fact]
    public async Task Compliance_LeaderTier_RejectedMonth_Is403()
    {
        var rsp = await GetAsync(Mgr, StatsTidRoles.LocalLeader, ComplianceUrl(Emp, RejMonth));
        await AssertMonthGate403Async(rsp);
    }

    /// <summary>Leader tier + NO row ⇒ 403 fail-closed.</summary>
    [Fact]
    public async Task Compliance_LeaderTier_NoPeriodRow_Is403_FailClosed()
    {
        var rsp = await GetAsync(Mgr, StatsTidRoles.LocalLeader, ComplianceUrl(Emp, NoRowMonth));
        await AssertMonthGate403Async(rsp);
    }

    /// <summary>Leader tier + SUBMITTED month ⇒ the gate passes. NOT-403 (rather than 200) because
    /// the handler then round-trips the rule engine, unreachable in this harness (the
    /// AllocationBreakdownEndpointTests B2 convention: auth/gate verdicts are 403-vs-not).</summary>
    [Fact]
    public async Task Compliance_LeaderTier_SubmittedMonth_PassesGate_NotForbidden()
    {
        var rsp = await GetAsync(Mgr, StatsTidRoles.LocalLeader, ComplianceUrl(Emp, SentMonth));
        Assert.NotEqual(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>SELF on an UNSENT month ⇒ never month-gated (R1 self exemption): the employee may
    /// always check their own in-progress month's compliance.</summary>
    [Fact]
    public async Task Compliance_Self_UnsentMonth_NotForbidden()
    {
        var rsp = await GetAsync(Emp, StatsTidRoles.Employee, ComplianceUrl(Emp, NoRowMonth));
        Assert.NotEqual(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>HR-OR-ABOVE covering org-scope on an UNSENT month ⇒ exempt (the corrective tier).</summary>
    [Fact]
    public async Task Compliance_HrOrAbove_UnsentMonth_NotForbidden()
    {
        var rsp = await GetAsync(Hr, StatsTidRoles.LocalHR, ComplianceUrl(Emp, NoRowMonth));
        Assert.NotEqual(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  3) GET /api/balance/{id}/summary
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Leader tier + REJECTED month ⇒ 403, and the month-derived figures (normHoursActual,
    /// flexBalance) never reach the wire.</summary>
    [Fact]
    public async Task Balance_LeaderTier_RejectedMonth_Is403()
    {
        var rsp = await GetAsync(Mgr, StatsTidRoles.LocalLeader, SummaryUrl(Emp, RejMonth));
        await AssertMonthGate403Async(rsp);
        Assert.DoesNotContain("normHoursActual", await rsp.Content.ReadAsStringAsync());
    }

    /// <summary>Leader tier + NO row ⇒ 403 fail-closed.</summary>
    [Fact]
    public async Task Balance_LeaderTier_NoPeriodRow_Is403_FailClosed()
    {
        var rsp = await GetAsync(Mgr, StatsTidRoles.LocalLeader, SummaryUrl(Emp, NoRowMonth));
        await AssertMonthGate403Async(rsp);
    }

    /// <summary>Leader tier + SUBMITTED month ⇒ 200 with the summary (the seeded July hours are in
    /// normHoursActual) — no narrowing of the sent-month read.</summary>
    [Fact]
    public async Task Balance_LeaderTier_SubmittedMonth_Is200()
    {
        var rsp = await GetAsync(Mgr, StatsTidRoles.LocalLeader, SummaryUrl(Emp, SentMonth));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Emp, body.GetProperty("employeeId").GetString());
        Assert.Equal(7.4m, body.GetProperty("normHoursActual").GetDecimal());
    }

    /// <summary>SELF on an UNSENT month ⇒ 200 (R1 self exemption) — the employee's own running
    /// balances stay readable while the month is in progress.</summary>
    [Fact]
    public async Task Balance_Self_UnsentMonth_Is200()
    {
        var rsp = await GetAsync(Emp, StatsTidRoles.Employee, SummaryUrl(Emp, NoRowMonth));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    /// <summary>HR-OR-ABOVE covering org-scope on an UNSENT month ⇒ 200 (the corrective tier).</summary>
    [Fact]
    public async Task Balance_HrOrAbove_UnsentMonth_Is200()
    {
        var rsp = await GetAsync(Hr, StatsTidRoles.LocalHR, SummaryUrl(Emp, NoRowMonth));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Assert / URL / client helpers
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The R6 verdict: 403 carrying the SHARED month-gate body (the Skema month-GET shape,
    /// one construction site — ApprovalReadTier.MonthNotSubmittedForbidden). Asserting the reason
    /// text distinguishes the month gate from an access-population 403, so a broken seed cannot
    /// green these arms via the wrong denial.</summary>
    private static async Task AssertMonthGate403Async(HttpResponseMessage rsp)
    {
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.Contains(MonthGateReason, await rsp.Content.ReadAsStringAsync());
    }

    private static string BreakdownUrl(string employeeId, int month) =>
        $"/api/approval/{employeeId}/allocation-breakdown?year={Year}&month={month}";

    private static string ComplianceUrl(string employeeId, int month) =>
        $"/api/compliance/{employeeId}/period?year={Year}&month={month}";

    private static string SummaryUrl(string employeeId, int month) =>
        $"/api/balance/{employeeId}/summary?year={Year}&month={month}";

    private async Task<HttpResponseMessage> GetAsync(string actorId, string role, string url)
    {
        var client = _factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "HK", orgId: Org,
            scopes: new[] { new RoleScope(role, Org, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync(url);
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
                (@emp,   @emp,   '$2a$11$fake', 'T128G Emp',   't128g_emp@test.dk',    @org, 'HK', 'OK24', TRUE),
                (@mgr,   @mgr,   '$2a$11$fake', 'T128G Mgr',   't128g_mgr@test.dk',    @org, 'HK', 'OK24', TRUE),
                (@hr,    @hr,    '$2a$11$fake', 'T128G HR',    't128g_hr@test.dk',     @org, 'HK', 'OK24', TRUE),
                (@emphr, @emphr, '$2a$11$fake', 'T128G EmpHR', 't128g_emp_hr@test.dk', @org, 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
                (@mgr,   'LOCAL_LEADER', @org, 'ORG_ONLY', 'TEST'),
                (@hr,    'LOCAL_HR',     @org, 'ORG_ONLY', 'TEST'),
                (@emp,   'EMPLOYEE',     @org, 'ORG_ONLY', 'TEST'),
                (@emphr, 'EMPLOYEE',     @org, 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Designated PRIMARY edges: Emp → Mgr (the leader-tier actor's population membership on the
        // edge-only breakdown), and EmpHr → Hr (so the HR-exemption is provable INSIDE the edge-only
        // population without touching it).
        var rlRepo = new ReportingLineRepository(_dbFactory);
        await rlRepo.AssignAsync(null, MakeLine(Emp, Mgr));
        await rlRepo.AssignAsync(null, MakeLine(EmpHr, Hr));

        // May = REJECTED (decision recorded), July = SUBMITTED. June deliberately has NO row.
        await InsertPeriodAsync(conn, Emp, "REJECTED", RejMonth, rejected: true);
        await InsertPeriodAsync(conn, Emp, "SUBMITTED", SentMonth, rejected: false);

        // Recognizable figures in every asserted month, so each 403 withholds something real and
        // each 200 returns something checkable: worked==allocated on one weekday per month.
        await SeedDayFiguresAsync(conn, Emp, new DateOnly(Year, RejMonth, 4), 6.5m);   // Mon
        await SeedDayFiguresAsync(conn, Emp, new DateOnly(Year, NoRowMonth, 2), 5.5m); // Tue
        await SeedDayFiguresAsync(conn, Emp, new DateOnly(Year, SentMonth, 1), 7.4m);  // Wed
        await SeedDayFiguresAsync(conn, EmpHr, new DateOnly(Year, NoRowMonth, 2), 4.5m);
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("hr", Hr);
        cmd.Parameters.AddWithValue("emphr", EmpHr);
        cmd.Parameters.AddWithValue("org", Org);
    }

    private static ReportingLineModel MakeLine(string employeeId, string managerId) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        OrganisationId = Org,
        Relationship = "PRIMARY",
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    private static async Task InsertPeriodAsync(
        NpgsqlConnection conn, string employeeId, string status, int month, bool rejected)
    {
        var start = new DateOnly(Year, month, 1);
        var end = new DateOnly(Year, month, DateTime.DaysInMonth(Year, month));
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status,
                 agreement_code, ok_version, submitted_at, submitted_by, approved_at, approved_by, rejection_reason)
            VALUES
                (gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', @status, 'HK', 'OK24',
                 NOW(), @emp, @decisionAt, @decisionBy, @reason)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", Org);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("decisionAt", rejected ? DateTime.UtcNow : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("decisionBy", rejected ? Mgr : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("reason", rejected ? "rettelser nødvendige" : (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>One balanced day: a NORMAL + task-tagged time entry (allocated side, also the
    /// balance summary's normHoursActual) and a matching work_time row (worked side).</summary>
    private async Task SeedDayFiguresAsync(NpgsqlConnection conn, string employeeId, DateOnly date, decimal hours)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, task_id, activity_type, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, @hours, @task, 'NORMAL', 'HK', 'OK24', FALSE, NOW(), @outbox)
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", date);
            cmd.Parameters.AddWithValue("hours", hours);
            cmd.Parameters.AddWithValue("task", SentinelTask);
            cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO work_time_projection (employee_id, date, intervals, manual_hours, occurred_at, outbox_id)
            VALUES (@emp, @date, '[]'::jsonb, @manual, NOW(), @outbox)
            ON CONFLICT (employee_id, date) DO UPDATE SET manual_hours = @manual
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", date);
            cmd.Parameters.AddWithValue("manual", hours);
            cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        // One constant multi-statement command (ordered: dependents before users; the factory boot
        // backfills an employee_profiles row per user, TASK-3403, so profiles go before users).
        await using var cmd = new NpgsqlCommand(
            """
            DELETE FROM approval_audit WHERE actor_id = ANY(@ids) OR period_id IN (SELECT period_id FROM approval_periods WHERE employee_id = ANY(@ids));
            DELETE FROM approval_periods WHERE employee_id = ANY(@ids);
            DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids);
            DELETE FROM role_assignments WHERE user_id = ANY(@ids);
            DELETE FROM time_entries_projection WHERE employee_id = ANY(@ids);
            DELETE FROM work_time_projection WHERE employee_id = ANY(@ids);
            DELETE FROM absences_projection WHERE employee_id = ANY(@ids);
            DELETE FROM entitlement_balances WHERE employee_id = ANY(@ids);
            DELETE FROM events WHERE stream_id = ANY(@streams);
            DELETE FROM event_streams WHERE stream_id = ANY(@streams);
            DELETE FROM employee_profiles WHERE employee_id = ANY(@ids);
            DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids);
            DELETE FROM users WHERE user_id = ANY(@ids);
            """, conn);
        cmd.Parameters.AddWithValue("ids", AllUsers);
        cmd.Parameters.AddWithValue("streams", AllUsers.Select(u => $"employee-{u}").ToArray());
        await cmd.ExecuteNonQueryAsync();
    }
}
