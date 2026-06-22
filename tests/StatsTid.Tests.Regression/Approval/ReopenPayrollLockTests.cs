using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S90 / TASK-9003 — the reopen PAYROLL-EXPORT LOCK gate (ADR-034). Once a month has been sent to
/// payroll (a <c>payroll_export_records</c> row exists for the period's <c>(employee, year, month)</c>),
/// the period can NO LONGER be reopened — corrections only, for ALL roles (OQ-2: no recall, no admin
/// reopen). The check is an ADDITIVE in-tx gate placed AFTER the period row lock and BEFORE the existing
/// S78 conditional status UPDATE, so it composes with the S78/S83 reopen hardening.
///
/// <para>Topology mirrors <see cref="ApprovalConcurrencyHardeningTests"/> (S92/ADR-035 flatten — STY02
/// Organisation, distinct <c>s90_*</c> users): the designated approver <c>s90_mgr</c> (STY02) is the
/// PRIMARY manager of <c>s90_emp</c> (STY02) — the PRIMARY edge grants the leader reopen. A LocalHR
/// (<c>s90_hr</c>, STY02) and a GlobalAdmin (<c>s90_ga</c>) exercise the "all roles" arm of OQ-2.</para>
///
/// <para>RED-on-old: against the pre-9003 handler ALL of these reopens of an APPROVED, exported period
/// would have returned 200 → DRAFT. The export-lock gate makes them a discriminated
/// <c>409 kind="payroll-locked"</c> instead.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ReopenPayrollLockTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    // STY02 Organisation — distinct from the seed + the S78 test users.
    private const string Emp = "s90_emp";   // STY02 — the report
    private const string Mgr = "s90_mgr";   // STY02 — PRIMARY manager of Emp
    private const string Hr = "s90_hr";     // STY02 — LOCAL_HR @ STY02
    private const string Ga = "s90_ga";     // GLOBAL_ADMIN
    private const string TreeRootSty02 = "STY02";

    // The exported period sits in May 2026 → the lock key is (Emp, 2026, 5).
    private static readonly DateOnly PeriodStart = new(2026, 5, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 5, 31);

    private static readonly string[] AllUsers = { Emp, Mgr, Hr, Ga };

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
    //  Pre-export — the S89 leader reopen is PRESERVED (no lock row → 200 → DRAFT)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// PRE-EXPORT (no payroll_export_records row): a LocalLeader reopen of an APPROVED period succeeds →
    /// 200 → DRAFT. This guards against the gate over-firing — the S89 leader-reopen behavior is intact
    /// when the month has NOT been sent to payroll.
    /// </summary>
    [Fact]
    public async Task PreExport_LeaderReopen_Approved_Succeeds_200_Draft()
    {
        var periodId = await InsertApprovedPeriodAsync();
        var client = LeaderClient(Mgr, "STY02");

        var rsp = await client.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "pre-export" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("DRAFT", await ReadStatusAsync(periodId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Post-export — corrections-only for ALL roles (OQ-2): discriminated 409
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST-EXPORT (a payroll_export_records row exists for (Emp, 2026, 5)): a LocalLeader reopen of the
    /// APPROVED period is REFUSED → discriminated 409 (kind="payroll-locked"), and the period stays
    /// APPROVED (no transition). RED-on-old: the pre-9003 handler would have returned 200 → DRAFT.
    /// </summary>
    [Fact]
    public async Task PostExport_LeaderReopen_Refused_409_PayrollLocked_StatusUnchanged()
    {
        var periodId = await InsertApprovedPeriodAsync();
        await InsertExportRecordAsync(periodId, Emp, 2026, 5);
        var client = LeaderClient(Mgr, "STY02");

        var rsp = await client.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "post-export" });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        Assert.Equal("payroll-locked", await ReadKindAsync(rsp));
        // The period never transitioned — the gate runs before any mutation.
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
    }

    /// <summary>
    /// POST-EXPORT — the OQ-2 "ALL roles, no recall" rule: a LocalHR reopen is ALSO refused with the same
    /// discriminated 409. RED-on-old: the pre-9003 handler would have reopened it (HR is org-scope-admitted
    /// over STY02).
    /// </summary>
    [Fact]
    public async Task PostExport_LocalHrReopen_Refused_409_PayrollLocked()
    {
        var periodId = await InsertApprovedPeriodAsync();
        await InsertExportRecordAsync(periodId, Emp, 2026, 5);
        var client = HrClient(Hr, "STY02");

        var rsp = await client.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "hr" });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        Assert.Equal("payroll-locked", await ReadKindAsync(rsp));
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
    }

    /// <summary>
    /// POST-EXPORT — the OQ-2 "no admin reopen" rule: a GlobalAdmin reopen is ALSO refused with the same
    /// discriminated 409. RED-on-old: a GlobalAdmin would have reopened any APPROVED period.
    /// </summary>
    [Fact]
    public async Task PostExport_GlobalAdminReopen_Refused_409_PayrollLocked()
    {
        var periodId = await InsertApprovedPeriodAsync();
        await InsertExportRecordAsync(periodId, Emp, 2026, 5);
        var client = GlobalAdminClient(Ga);

        var rsp = await client.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "ga" });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        Assert.Equal("payroll-locked", await ReadKindAsync(rsp));
        Assert.Equal("APPROVED", await ReadStatusAsync(periodId));
    }

    /// <summary>
    /// The export-lock 409 is DISCRIMINATED (kind="payroll-locked") and distinguishable from the existing
    /// S78 status-conflict 409, which carries NO <c>kind</c>. We trigger the status-conflict by reopening a
    /// DRAFT period (outside the leader arm's {EMPLOYEE_APPROVED, APPROVED} source set) with NO export row
    /// — that path 409s without a <c>kind</c>. (A DRAFT period is rejected by the pre-tx status guard with a
    /// kind-less conflict; the point is purely that the two 409s are distinguishable by <c>kind</c>.)
    /// </summary>
    [Fact]
    public async Task ExportLock409_IsDiscriminated_FromStatusConflict409()
    {
        // A non-reopenable (DRAFT) period, NOT exported → the status-conflict 409 (no kind).
        // A DISTINCT month (April) so it doesn't collide with the May exported period below on the
        // approval_periods (employee_id, period_start, period_end) UNIQUE.
        var draftId = await InsertPeriodAsync("DRAFT", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        var client = LeaderClient(Mgr, "STY02");
        var statusRsp = await client.PostAsJsonAsync($"/api/approval/{draftId}/reopen", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Conflict, statusRsp.StatusCode);
        Assert.Null(await ReadKindAsync(statusRsp)); // no discriminator on the status-conflict 409

        // An exported, APPROVED period → the payroll-lock 409 (kind="payroll-locked").
        var exportedId = await InsertApprovedPeriodAsync();
        await InsertExportRecordAsync(exportedId, Emp, 2026, 5);
        var lockRsp = await client.PostAsJsonAsync($"/api/approval/{exportedId}/reopen", new { reason = "y" });
        Assert.Equal(HttpStatusCode.Conflict, lockRsp.StatusCode);
        Assert.Equal("payroll-locked", await ReadKindAsync(lockRsp));
    }

    /// <summary>
    /// In-tx visibility (the B2 placement proof): the export-lock check reads
    /// <c>payroll_export_records</c> INSIDE the reopen tx AFTER taking the period row lock, so it
    /// observes a COMMITTED export-lock row. We commit the lock row first, then reopen — the gate sees it
    /// → 409. (A pre-tx-only read at the period load would also see this committed row; the placement's
    /// load-bearing value is the row lock that serializes against a CONCURRENT export's own FOR UPDATE —
    /// proven structurally; here we assert the committed-lock-row is observed under the in-tx read.)
    /// </summary>
    [Fact]
    public async Task CommittedExportRow_IsObserved_UnderInTxReopenCheck_409()
    {
        var periodId = await InsertApprovedPeriodAsync();
        await InsertExportRecordAsync(periodId, Emp, 2026, 5); // committed BEFORE the reopen
        var client = LeaderClient(Mgr, "STY02");

        var rsp = await client.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "race" });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        Assert.Equal("payroll-locked", await ReadKindAsync(rsp));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed / cleanup / helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@emp, @emp, '$2a$11$fake', 'S90 Emp', 's90_emp@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@mgr, @mgr, '$2a$11$fake', 'S90 Mgr', 's90_mgr@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@hr,  @hr,  '$2a$11$fake', 'S90 HR',  's90_hr@test.dk',  'STY02', 'AC', 'OK24', TRUE),
                (@ga,  @ga,  '$2a$11$fake', 'S90 GA',  's90_ga@test.dk',  'STY02', 'AC', 'OK24', TRUE)
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
                (@mgr, 'LOCAL_LEADER', 'STY02', 'ORG_AND_DESCENDANTS', 'TEST'),
                (@hr,  'LOCAL_HR',     'STY02', 'ORG_AND_DESCENDANTS', 'TEST'),
                -- GLOBAL_ADMIN must be (org_id IS NULL, scope_type='GLOBAL') per the S85 CHECKs.
                (@ga,  'GLOBAL_ADMIN', NULL,    'GLOBAL',              'TEST'),
                (@emp, 'EMPLOYEE',     'STY02', 'ORG_ONLY',            'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Emp (STY02) reports PRIMARY to Mgr (STY02) — the same-Organisation, same-tree edge that grants
        // the leader reopen.
        await new ReportingLineRepository(_dbFactory).AssignAsync(null, MakeLine(Emp, Mgr, TreeRootSty02));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("hr", Hr);
        cmd.Parameters.AddWithValue("ga", Ga);
    }

    private static ReportingLineModel MakeLine(string employeeId, string managerId, string treeRoot) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        TreeRootOrgId = treeRoot,
        Relationship = "PRIMARY",
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        await ExecAsync(conn,
            "DELETE FROM payroll_export_records WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn,
            "DELETE FROM approval_audit WHERE actor_id = ANY(@ids) OR period_id IN (SELECT period_id FROM approval_periods WHERE employee_id = ANY(@ids))");
        await ExecAsync(conn, "DELETE FROM audit_projection WHERE actor_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM outbox_events WHERE stream_id LIKE 'approval-s90_%' OR stream_id LIKE 'reporting-line-s90_%'");
        await ExecAsync(conn, "DELETE FROM approval_periods WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM users WHERE user_id = ANY(@ids)");

        async Task ExecAsync(NpgsqlConnection c, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            if (sql.Contains("@ids"))
                cmd.Parameters.AddWithValue("ids", AllUsers);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private Task<Guid> InsertApprovedPeriodAsync() => InsertPeriodAsync("APPROVED");

    // start/end default to the May (PeriodStart/PeriodEnd) period the export-record fixtures key on
    // (Emp, 2026, 5); pass a DISTINCT month to insert a 2nd period for the same Emp without tripping
    // the approval_periods (employee_id, period_start, period_end) UNIQUE natural key.
    private async Task<Guid> InsertPeriodAsync(string status, DateOnly? start = null, DateOnly? end = null)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version, submitted_at, submitted_by)
            VALUES
                (@id, @emp, 'STY02', @start, @end, 'MONTHLY', @status, 'HK', 'OK24', NOW(), @emp)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("start", start ?? PeriodStart);
        cmd.Parameters.AddWithValue("end", end ?? PeriodEnd);
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// Inserts a COMMITTED <c>payroll_export_records</c> row = "this (employee, year, month) is sent to
    /// payroll" (the lock). The Payroll service is the real writer (TASK-9002); the test writes it directly
    /// to stage the lock state. Minimal JSONB manifests + a content hash satisfy the NOT-NULL columns.
    /// </summary>
    private async Task InsertExportRecordAsync(Guid periodId, string employeeId, int year, int month)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month, exported_at,
                 original_lines, current_effective_lines, content_hash, source)
            VALUES
                (@xid, @pid, @emp, @y, @m, NOW(), '[]'::jsonb, '[]'::jsonb, 'test-hash', 'CALCULATE_AND_EXPORT')
            """, conn);
        cmd.Parameters.AddWithValue("xid", Guid.NewGuid());
        cmd.Parameters.AddWithValue("pid", periodId);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        await cmd.ExecuteNonQueryAsync();
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

    /// <summary>Reads the <c>kind</c> discriminator out of a JSON error body (null when absent).</summary>
    private static async Task<string?> ReadKindAsync(HttpResponseMessage rsp)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await rsp.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("kind", out var kind) && kind.ValueKind == System.Text.Json.JsonValueKind.String
            ? kind.GetString()
            : null;
    }

    private HttpClient LeaderClient(string userId, string orgId)
        => ClientWithToken(MintLeaderToken(userId, orgId));

    private HttpClient HrClient(string userId, string orgId)
        => ClientWithToken(MintHrToken(userId, orgId));

    private HttpClient GlobalAdminClient(string userId)
        => ClientWithToken(MintGlobalAdminToken(userId));

    private HttpClient ClientWithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string MintLeaderToken(string userId, string orgId)
    {
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_AND_DESCENDANTS") };
        return NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalLeader,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    private static string MintHrToken(string userId, string orgId)
    {
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_AND_DESCENDANTS") };
        return NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: orgId, scopes: scopes);
    }

    private static string MintGlobalAdminToken(string userId)
    {
        var scopes = new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") };
        return NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: "STY02", scopes: scopes);
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });
}
