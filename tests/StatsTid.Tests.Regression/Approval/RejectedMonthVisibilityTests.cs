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
/// S127 / TASK-12712 — AC-13. The end-to-end companion to the aggregate-level REJECTED withholding
/// pinned in <c>TeamOverviewAggregateTests.RejectedPeriod_WithholdsMonthDerivedFields_ButKeepsDecisionRecord</c>.
///
/// <para><b>Ruling R1 (12706) closes exactly TWO display surfaces</b>, both through
/// <c>ApprovalVisibility.IsSubmittedToManager</c> (from which REJECTED was removed): the leder-
/// Teamoversigt row (the five month-derived figures nulled) and the Skema leader-tier grid
/// (<c>GET /api/skema/{id}/month</c> 403s for a REJECTED month). Those are proven by
/// <see cref="RejectedMonth_WithheldAtTeamOverviewAndSkemaLeaderGrid"/>.</para>
///
/// <para><b>Ruling R5 (the KNOWN, ACCEPTED hole — deliberately NOT closed here, RES-002 follow-up):</b>
/// the sibling READ endpoints — allocation-breakdown and the compliance detail — authorize the SAME
/// designated-approver population (<c>IsEffectiveApproverOrUnitLeaderAsync</c>, the identical predicate
/// the team-overview roster filters through, <c>ApprovalEndpoints.cs</c> allocation-breakdown auth /
/// <c>ComplianceEndpoints.cs</c> the S88-8801 B2 edge OR-branch) and then read the projections with
/// <b>NO period-status gate</b>. A manager who calls them directly therefore still sees a rejected
/// month's current figures. <see cref="RejectedMonth_StillDisclosedByAllocationBreakdown_R5Gap"/>
/// pins that as intended-and-open by asserting the figures come back <b>non-zero and recognizable</b>
/// (not merely a non-403 — a 200 that returned zeros would pass while proving nothing, per RES-002).
/// The disclosure figure-proof is anchored on allocation-breakdown, which is self-contained (it reads
/// <c>work_time_projection</c> / <c>time_entries_projection</c> directly with no rule-engine round-trip
/// and no period-status gate) — the compliance <c>/period</c> detail discloses on the identical auth
/// basis but round-trips the rule-engine service, so it is documented here rather than figure-asserted
/// to keep the pin deterministic.</para>
///
/// <para>Topology mirrors <see cref="TeamOverviewAggregateTests"/>: an isolated <c>ac13_*</c> employee
/// + designated leader on the baseline STY02 Organisation, the PRIMARY edge granting act authority.
/// FRESH testcontainer, cleaned before + after (FAIL-002).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RejectedMonthVisibilityTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Emp = "ac13_emp";   // STY02 — the REJECTED employee
    private const string Mgr = "ac13_mgr";   // STY02 — the designated approver (PRIMARY edge)
    private const string TreeRootSty02 = "STY02";

    // The rejected month + the day carrying the sentinel figures.
    private static readonly DateOnly RejMonthStart = new(2026, 5, 1);
    private static readonly DateOnly RejMonthEnd = new(2026, 5, 31);
    private static readonly DateOnly RegDay = new(2026, 5, 4); // a Monday in-month
    private const decimal WorkedSentinel = 6.5m;               // recognizable, non-zero, balanced
    private const decimal AllocSentinel = 6.5m;
    private const string SentinelTask = "AC13-TASK";

    private static readonly string[] AllUsers = { Emp, Mgr };

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
    //  R1 — the two display surfaces WITHHOLD a rejected month
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Both R1-governed display surfaces withhold. Team-overview: the five month-derived
    /// figures are null while status / submittedAt / decisionAt / rejectionReason survive. The Skema
    /// leader tier: the whole month grid 403s (a leader may read a month's grid only once it is sent).
    /// Real registered hours are seeded, so the team-overview nulls are meaningful (a leak would show
    /// the seeded 6.5, not null).</summary>
    [Fact]
    public async Task RejectedMonth_WithheldAtTeamOverviewAndSkemaLeaderGrid()
    {
        using var mgrClient = LeaderClient(Mgr, TreeRootSty02);

        // ── Surface 1: the leder-Teamoversigt row ──
        var rsp = await mgrClient.GetAsync("/api/approval/team-overview?year=2026&month=5");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var employees = (await rsp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("employees").EnumerateArray().ToList();
        var row = employees.Single(e => e.GetProperty("employeeId").GetString() == Emp);

        Assert.Equal("REJECTED", row.GetProperty("status").GetString());
        // The five month-derived figures are withheld (null) — never 0.
        Assert.Equal(JsonValueKind.Null, row.GetProperty("normRegistered").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("overtime").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("hasWarning").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("ferieUsed").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("flexBalance").ValueKind);
        // The decision record survives.
        Assert.Equal(JsonValueKind.String, row.GetProperty("submittedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("decisionAt").ValueKind);
        Assert.Equal("rettelser nødvendige", row.GetProperty("rejectionReason").GetString());

        // ── Surface 2: the Skema leader-tier grid ──
        var grid = await mgrClient.GetAsync($"/api/skema/{Emp}/month?year=2026&month=5");
        Assert.Equal(HttpStatusCode.Forbidden, grid.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  R5 — the sibling reads STILL disclose (the accepted, RES-002-tracked hole)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The R5 gap, pinned so it cannot be mistaken for closed: allocation-breakdown authorizes
    /// the SAME designated-approver population and reads the projections with no period-status gate, so
    /// the leader still sees the rejected month's figures. Asserted by RECOGNIZABLE NON-ZERO SENTINELS
    /// (worked / allocated / the allocations bar), not merely a non-403 — a 200 returning zeros would
    /// pass while proving nothing (RES-002). The month is REJECTED and withheld at the two display
    /// surfaces above, yet these exact figures come straight back here.</summary>
    [Fact]
    public async Task RejectedMonth_StillDisclosedByAllocationBreakdown_R5Gap()
    {
        using var mgrClient = LeaderClient(Mgr, TreeRootSty02);

        var rsp = await mgrClient.GetAsync($"/api/approval/{Emp}/allocation-breakdown?year=2026&month=5");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode); // NOT 403 — same population, no status gate
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // The sentinel figures the rejected month's projections carry — disclosed verbatim.
        Assert.Equal(WorkedSentinel, body.GetProperty("worked").GetDecimal());
        Assert.Equal(AllocSentinel, body.GetProperty("allocated").GetDecimal());

        var allocations = body.GetProperty("allocations").EnumerateArray().ToList();
        Assert.NotEmpty(allocations); // NOT an empty array — a real, non-zero disclosure
        var bar = allocations.Single(a => a.GetProperty("taskId").GetString() == SentinelTask);
        Assert.Equal(AllocSentinel, bar.GetProperty("hours").GetDecimal());
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
                (@emp, @emp, '$2a$11$fake', 'AC13 Emp', 'ac13_emp@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@mgr, @mgr, '$2a$11$fake', 'AC13 Mgr', 'ac13_mgr@test.dk', 'STY02', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", Emp);
            cmd.Parameters.AddWithValue("mgr", Mgr);
            await cmd.ExecuteNonQueryAsync();
        }

        // Mgr is an active LOCAL_LEADER on STY02 (the DB floor the R5 predicate + roster require); Emp
        // is an EMPLOYEE.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
                (@mgr, 'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@emp, 'EMPLOYEE',     'STY02', 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", Emp);
            cmd.Parameters.AddWithValue("mgr", Mgr);
            await cmd.ExecuteNonQueryAsync();
        }

        // The designated PRIMARY edge Emp → Mgr (same Organisation) — grants Mgr act authority, which
        // is what puts Emp in Mgr's team-overview roster AND authorizes the allocation-breakdown read.
        var rlRepo = new ReportingLineRepository(_dbFactory);
        await rlRepo.AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = Emp,
            ManagerId = Mgr,
            OrganisationId = TreeRootSty02,
            Relationship = "PRIMARY",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });

        // The REJECTED period (submitted_at + approved_at written; a reason). Directly seeded — this
        // read path bypasses the send command entirely.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status,
                 agreement_code, ok_version, submitted_at, submitted_by, approved_at, approved_by, rejection_reason)
            VALUES
                (@id, @emp, 'STY02', @start, @end, 'MONTHLY', 'REJECTED', 'HK', 'OK24',
                 NOW(), @emp, NOW(), @mgr, 'rettelser nødvendige')
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("emp", Emp);
            cmd.Parameters.AddWithValue("mgr", Mgr);
            cmd.Parameters.AddWithValue("start", RejMonthStart);
            cmd.Parameters.AddWithValue("end", RejMonthEnd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Registered hours on one in-month day: a NORMAL + task-tagged time entry (the ALLOCATED side)
        // and a matching work_time row (the WORKED side) → allocation-breakdown discloses 6.5 / 6.5.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, task_id, activity_type, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, @hours, @task, 'NORMAL', 'HK', 'OK24', FALSE, NOW(), @outbox)
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", Emp);
            cmd.Parameters.AddWithValue("date", RegDay);
            cmd.Parameters.AddWithValue("hours", AllocSentinel);
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
            cmd.Parameters.AddWithValue("emp", Emp);
            cmd.Parameters.AddWithValue("date", RegDay);
            cmd.Parameters.AddWithValue("manual", WorkedSentinel);
            cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
            await cmd.ExecuteNonQueryAsync();
        }

        // Flex + vacation balance, so the team-overview flexBalance / ferieUsed nulls are meaningful
        // (a leak would surface these seeded values rather than pass against an empty employee).
        await SetVacationBalanceAsync(conn, Emp, 2025, used: 7m, totalQuota: 25m);
        await InsertFlexEventAsync(conn, Emp, 4m);
    }

    private static async Task SetVacationBalanceAsync(
        NpgsqlConnection conn, string employeeId, int year, decimal used, decimal totalQuota)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_balances (balance_id, employee_id, entitlement_type, entitlement_year, total_quota, used, planned, carryover_in, updated_at)
            VALUES (gen_random_uuid(), @emp, 'VACATION', @year, @total, @used, 0, 0, NOW())
            ON CONFLICT (employee_id, entitlement_type, entitlement_year)
            DO UPDATE SET used = @used, total_quota = @total, updated_at = NOW()
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("year", year);
        cmd.Parameters.AddWithValue("used", used);
        cmd.Parameters.AddWithValue("total", totalQuota);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertFlexEventAsync(NpgsqlConnection conn, string employeeId, decimal newBalance)
    {
        var streamId = $"employee-{employeeId}";
        await using (var sCmd = new NpgsqlCommand(
            "INSERT INTO event_streams (stream_id) VALUES (@s) ON CONFLICT DO NOTHING", conn))
        {
            sCmd.Parameters.AddWithValue("s", streamId);
            await sCmd.ExecuteNonQueryAsync();
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var data = $"{{\"eventId\":\"{Guid.NewGuid()}\",\"employeeId\":\"{employeeId}\",\"previousBalance\":0,\"newBalance\":{newBalance.ToString(inv)},\"delta\":{newBalance.ToString(inv)},\"reason\":\"test\"}}";
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO events (event_id, stream_id, stream_version, event_type, data, occurred_at)
            VALUES (gen_random_uuid(), @s, 1, 'FlexBalanceUpdated', @data::jsonb, NOW())
            """, conn);
        cmd.Parameters.AddWithValue("s", streamId);
        cmd.Parameters.AddWithValue("data", data);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        await ExecAsync(conn,
            "DELETE FROM approval_audit WHERE actor_id = ANY(@ids) OR period_id IN (SELECT period_id FROM approval_periods WHERE employee_id = ANY(@ids))");
        await ExecAsync(conn, "DELETE FROM approval_periods WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM time_entries_projection WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM work_time_projection WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM entitlement_balances WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM events WHERE stream_id = ANY(@streams)");
        await ExecAsync(conn, "DELETE FROM event_streams WHERE stream_id = ANY(@streams)");
        // The factory boot backfills an employee_profiles row per user (TASK-3403) → delete before users.
        await ExecAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM users WHERE user_id = ANY(@ids)");

        async Task ExecAsync(NpgsqlConnection c, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.AddWithValue("ids", AllUsers);
            cmd.Parameters.AddWithValue("streams", AllUsers.Select(u => $"employee-{u}").ToArray());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private HttpClient LeaderClient(string userId, string orgId)
    {
        var client = _factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_ONLY") };
        var token = tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalLeader,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
