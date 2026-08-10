using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Xunit;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S127 / TASK-12711 — one Postgres testcontainer + one booted API shared by the whole send-command
/// behaviour matrix (<see cref="SendCommandBehaviourTests"/>, <see cref="SendCommandNoWriteTests"/> and
/// <see cref="SendCommandAuthorizationTests"/>, joined by the <c>SendCommandMatrix</c> xUnit collection).
/// The schema is the REAL <c>docker/postgres/init.sql</c> — the send command's coverage read, its
/// allocation gate, the unique <c>(employee_id, period_start, period_end)</c> constraint
/// (<c>init.sql:892</c>) and the <c>approval_periods</c> column set all belong to production, so a
/// hand-rolled fixture DDL could drift out from under the very behaviour under test (the S122 lesson).
///
/// <para>Isolation between cases is by CASE-UNIQUE identity (each test owns its own employee id),
/// never by cleanup — the collection runs its classes sequentially against the one container.</para>
/// </summary>
public sealed class SendCommandMatrixFixture : IAsyncLifetime
{
    // Private field: TestFixtures is internal, so a public property of the harness type would be a
    // CS0053 accessibility leak — only the members the tests need are surfaced.
    private TestFixtures.DockerHarness _harness = null!;

    public StatsTidWebApplicationFactory Factory { get; private set; } = null!;
    public DbConnectionFactory Db { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;
    public PostgresEventStore Outbox { get; private set; } = null!;
    public TimeEntryProjectionRepository TimeEntryRepo { get; private set; } = null!;
    public WorkTimeProjectionRepository WorkTimeRepo { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        ConnectionString = _harness.ConnectionString;
        Db = new DbConnectionFactory(ConnectionString);
        Factory = new StatsTidWebApplicationFactory(ConnectionString);
        _ = Factory.CreateClient(); // boot seeders (baseline org tree; init.sql already seeded STY01/02)
        Outbox = new PostgresEventStore(Db, new OutboxServiceContext("backend-api"));
        TimeEntryRepo = new TimeEntryProjectionRepository(Db);
        WorkTimeRepo = new WorkTimeProjectionRepository(Db);
    }

    public async Task DisposeAsync()
    {
        Factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }
}

[CollectionDefinition("SendCommandMatrix")]
public sealed class SendCommandMatrixCollection : ICollectionFixture<SendCommandMatrixFixture> { }

/// <summary>
/// Shared seeding / sending / reading helpers for the send-command matrix classes. Deliberately thin:
/// every helper either drives the PRODUCTION write path (time entries and work time through the real
/// outbox + projection repositories, so the gate reads the shape the real writer produces) or reads
/// state back with NO copy of the rule under test (no rounding, no tolerance, no comparison).
/// </summary>
public abstract class SendCommandMatrixTestBase
{
    protected const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>STY02 (agreement HK), seeded by <c>init.sql:934</c> with a project catalogue —
    /// an org that exists before any WAF seeder runs.</summary>
    protected const string Org = "STY02";

    /// <summary>STY01 (agreement AC), a DIFFERENT existing org — used only as the DELIBERATELY WRONG
    /// stored org_id in the AC-12 transition-arm correction fixture (org_id has an FK, so the wrong
    /// value must still be a real org).</summary>
    protected const string OtherOrg = "STY01";

    protected const int MarchYear = 2026;
    protected const int MarchMonth = 3;

    /// <summary>March 2026 — carries NO Danish public holiday (asserted in the coverage helper), so
    /// "expected workday" == "weekday", and it resolves to OK24 (the OK24 window is
    /// 2024-04-01..2026-03-31; the OK24→OK26 boundary is 2026-04-01), which AC-12 pins against a
    /// "resolve at today" bug — today (the test clock) is past that boundary.</summary>
    protected static readonly DateOnly MarchStart = new(2026, 3, 1);
    protected static readonly DateOnly MarchEnd = new(2026, 3, 31);

    /// <summary>2026-03-05, a Thursday — the day the ordinary cases leave as the coverage gap.</summary>
    protected static readonly DateOnly GapDay = new(2026, 3, 5);

    protected readonly SendCommandMatrixFixture Fx;

    protected SendCommandMatrixTestBase(SendCommandMatrixFixture fx) => Fx = fx;

    // A monotonic seq for hand-written absence rows' outbox_id (a NOT-NULL BIGINT). Range is chosen
    // clear of the other suites (AllocationGateTests uses 900_000; S116 uses 51_160_000).
    private static long _absenceSeq = 71_271_000;
    private static long NextAbsenceOutboxId() => Interlocked.Increment(ref _absenceSeq);

    // ── Seeding ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Seeds the three resolver-required rows (users + user_agreement_codes +
    /// employee_profiles) for a fresh employee. <c>ensureOrg:false</c> — the org already exists in
    /// init.sql. The dated agreement-code row anchors at 0001-01-01, so the send command's
    /// <c>GetByUserIdAtAsync(employeeId, monthStart)</c> resolves <paramref name="agreementCode"/>.</summary>
    protected Task SeedEmployeeAsync(string employeeId, string orgId = Org, string agreementCode = "HK")
        => RegressionSeed.SeedEmployeeAsync(
            Fx.ConnectionString, employeeId, orgId, agreementCode, "OK24", ensureOrg: false);

    /// <summary>Covers March's expected weekdays (minus <paramref name="gap"/>) with full-day VACATION
    /// absences. Absences satisfy the coverage check and are read from a table NEITHER side of the
    /// allocation gate touches, so a covered month is vacuously balanced unless work/allocation rows
    /// are added.</summary>
    protected async Task CoverMonthWithAbsencesAsync(string employeeId, DateOnly? gap = null)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();

        // Asserted, not assumed: March 2026 carries no public holiday, so covering every weekday
        // covers exactly the expected-workday set the send command computes.
        await using (var holidayCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM danish_public_holidays WHERE holiday_date >= @s AND holiday_date <= @e", conn))
        {
            holidayCmd.Parameters.AddWithValue("s", MarchStart);
            holidayCmd.Parameters.AddWithValue("e", MarchEnd);
            Assert.Equal(0L, (long)(await holidayCmd.ExecuteScalarAsync())!);
        }

        for (var d = MarchStart; d <= MarchEnd; d = d.AddDays(1))
        {
            if (d == gap || d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await InsertAbsenceRowAsync(conn, employeeId, d, "VACATION", 7.4m);
        }
    }

    /// <summary>One absence row on its own open connection (used to CLOSE a coverage gap in a retry).</summary>
    protected async Task InsertAbsenceAsync(string employeeId, DateOnly date, string type, decimal hours)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await InsertAbsenceRowAsync(conn, employeeId, date, type, hours);
    }

    private static async Task InsertAbsenceRowAsync(
        NpgsqlConnection conn, string employeeId, DateOnly date, string type, decimal hours)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage,
                 agreement_code, ok_version, occurred_at, outbox_id)
            VALUES (gen_random_uuid(), @emp, @date, @type, @hours, 1.0, 'HK', 'OK24', NOW(), @seq)
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("type", type);
        cmd.Parameters.AddWithValue("hours", hours);
        cmd.Parameters.AddWithValue("seq", NextAbsenceOutboxId());
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Records time at work through the PRODUCTION write path (outbox event +
    /// <see cref="WorkTimeProjectionRepository.UpsertAsync"/> in one tx) — the worked side of the gate,
    /// in the shape the real writer produces. Upsert: one row per (employee, date). Note: a
    /// work_time_projection row does NOT satisfy coverage (coverage reads time-entries + absences).</summary>
    protected async Task WorkedAsync(
        string employeeId, DateOnly date, (string Start, string End)[]? intervals = null, decimal manualHours = 0m)
    {
        var @event = new WorkTimeRegistered
        {
            EmployeeId = employeeId,
            Date = date,
            Intervals = (intervals ?? Array.Empty<(string, string)>())
                .Select(t => new WorkInterval { Start = t.Start, End = t.End }).ToList(),
            ManualHours = manualHours,
        };
        await using var conn = Fx.Db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var oid = await Fx.Outbox.EnqueueAndReturnIdAsync(conn, tx, $"employee-{employeeId}", @event);
        await Fx.WorkTimeRepo.UpsertAsync(conn, tx, @event, oid);
        await tx.CommitAsync();
    }

    /// <summary>Registers one time entry through the production write path (outbox + projection). The
    /// allocated side of the gate is Σ of NORMAL entries with a non-null task id; several calls on one
    /// day accumulate (append, not upsert). A NORMAL entry with any activity type also satisfies
    /// coverage for its day.</summary>
    protected async Task AllocatedAsync(
        string employeeId, DateOnly date, decimal hours, string activityType, string? taskId)
    {
        var @event = new TimeEntryRegistered
        {
            EmployeeId = employeeId,
            Date = date,
            Hours = hours,
            TaskId = taskId,
            ActivityType = activityType,
            AgreementCode = "HK",
            OkVersion = "OK24",
        };
        await using var conn = Fx.Db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var oid = await Fx.Outbox.EnqueueAndReturnIdAsync(conn, tx, $"employee-{employeeId}", @event);
        await Fx.TimeEntryRepo.InsertAsync(conn, tx, @event, oid);
        await tx.CommitAsync();
    }

    /// <summary>Directly seeds an <c>approval_periods</c> row (the by-id adapter's source, and the
    /// legacy-row shape). Dimensions default to the CORRECT STY02/HK/OK24 unless a test deliberately
    /// seeds wrong ones to prove the send corrects them (AC-12). No submitted_at / employee_approved_at
    /// is written, so a stamp becomes observable.</summary>
    protected async Task<Guid> SeedApprovalRowAsync(
        string employeeId, string status, DateOnly start, DateOnly end,
        string orgId = Org, string agreementCode = "HK", string okVersion = "OK24",
        string periodType = "MONTHLY")
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (employee_id, org_id, period_start, period_end,
                                          period_type, status, agreement_code, ok_version)
            VALUES (@e, @org, @s, @en, @pt, @st, @ac, @ok)
            RETURNING period_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("s", start);
        cmd.Parameters.AddWithValue("en", end);
        cmd.Parameters.AddWithValue("pt", periodType);
        cmd.Parameters.AddWithValue("st", status);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    // ── Sending ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The month-keyed adapter. The server derives the range from (year, month) — the body
    /// carries no dates, which is what closes defect 3 on the create path.</summary>
    protected static async Task<HttpResponseMessage> PostSendAsync(
        HttpClient client, string employeeId, int year = MarchYear, int month = MarchMonth)
        => await client.PostAsJsonAsync("/api/approval/send", new { employeeId, year, month });

    /// <summary>The by-id adapter (the <i>Mine perioder</i> re-send button's route). No body.</summary>
    protected static async Task<HttpResponseMessage> PostEmployeeApproveAsync(HttpClient client, Guid periodId)
        => await client.PostAsync($"/api/approval/{periodId}/employee-approve", content: null);

    /// <summary>The manager approve (AC-18 legacy bypass, R6). No body.</summary>
    protected static async Task<HttpResponseMessage> PostApproveAsync(HttpClient client, Guid periodId)
        => await client.PostAsync($"/api/approval/{periodId}/approve", content: null);

    /// <summary>Reopen (used only to reach the reopen→re-send arm of AC-8; the EMPLOYEE arm reopens
    /// its own EMPLOYEE_APPROVED period → DRAFT, taking no advisory/tree lock).</summary>
    protected static async Task<HttpResponseMessage> PostReopenAsync(HttpClient client, Guid periodId, string reason)
        => await client.PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason });

    // ── Response assertions ────────────────────────────────────────────────────────────────────────

    protected static async Task AssertOkEmployeeApprovedAsync(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {raw}");
        Assert.Equal("EMPLOYEE_APPROVED",
            JsonDocument.Parse(raw).RootElement.GetProperty("status").GetString());
    }

    /// <summary>The allocation refusal — discriminated from the coverage 422 by the <c>kind</c> field,
    /// which ONLY the allocation arm emits. (Anchoring on the discriminating field, not the status
    /// code, is the S123 shape-collision lesson: two 422s can share a body shape.)</summary>
    protected static async Task<JsonElement> AssertAllocation422Async(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)rsp.StatusCode}: {raw}");
        var root = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("allocation", root.GetProperty("kind").GetString());
        return root.Clone();
    }

    /// <summary>The coverage refusal — the SIBLING 422, which carries <c>missingDays</c> and NO
    /// <c>kind</c>. Asserting the absence of <c>kind</c> is what keeps this from silently passing when
    /// the allocation arm fired instead.</summary>
    protected static async Task AssertCoverage422Async(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)rsp.StatusCode}: {raw}");
        var root = JsonDocument.Parse(raw).RootElement;
        Assert.False(root.TryGetProperty("kind", out _),
            $"coverage 422 must carry NO 'kind' — its presence means the ALLOCATION gate fired instead: {raw}");
        Assert.True(root.TryGetProperty("missingDays", out _), $"coverage 422 must carry missingDays: {raw}");
    }

    protected static async Task AssertStatusAsync(HttpResponseMessage rsp, HttpStatusCode expected)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == expected, $"expected {(int)expected}, got {(int)rsp.StatusCode}: {raw}");
    }

    // ── Reading state back ───────────────────────────────────────────────────────────────────────

    // S127 Step-7a F10 — the snapshot must span EVERY mutable approval_periods column, or the AC-4
    // "nothing written" claim is only pinned over the columns it happens to list. designated_approver_id
    // and approval_method (init.sql:2683/2690 — written by the APPROVED/REJECTED SET branches) were the
    // omitted pair: an erroneous early write to either before a 422 would have left before==after under
    // the shorter record. The immutable identity columns (employee_id/period_start/period_end/period_type/
    // created_at) are deliberately NOT in the snapshot — they are written only by INSERT.
    protected sealed record ApprovalRowSnapshot(
        string Status,
        DateTime? SubmittedAt, string? SubmittedBy,
        DateTime? EmployeeApprovedAt, string? EmployeeApprovedBy,
        DateTime? ApprovedAt, string? ApprovedBy,
        string? RejectionReason,
        string OrgId, string AgreementCode, string OkVersion,
        DateOnly? EmployeeDeadline, DateOnly? ManagerDeadline,
        string? DesignatedApproverId, string? ApprovalMethod);

    protected async Task<ApprovalRowSnapshot?> ReadRowAsync(Guid periodId)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT status, submitted_at, submitted_by, employee_approved_at, employee_approved_by,
                   approved_at, approved_by, rejection_reason, org_id, agreement_code, ok_version,
                   employee_deadline, manager_deadline, designated_approver_id, approval_method
            FROM approval_periods WHERE period_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", periodId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return null;
        return new ApprovalRowSnapshot(
            Status: r.GetString(0),
            SubmittedAt: r.IsDBNull(1) ? null : r.GetFieldValue<DateTime>(1),
            SubmittedBy: r.IsDBNull(2) ? null : r.GetString(2),
            EmployeeApprovedAt: r.IsDBNull(3) ? null : r.GetFieldValue<DateTime>(3),
            EmployeeApprovedBy: r.IsDBNull(4) ? null : r.GetString(4),
            ApprovedAt: r.IsDBNull(5) ? null : r.GetFieldValue<DateTime>(5),
            ApprovedBy: r.IsDBNull(6) ? null : r.GetString(6),
            RejectionReason: r.IsDBNull(7) ? null : r.GetString(7),
            OrgId: r.GetString(8),
            AgreementCode: r.GetString(9),
            OkVersion: r.GetString(10),
            EmployeeDeadline: r.IsDBNull(11) ? null : r.GetFieldValue<DateOnly>(11),
            ManagerDeadline: r.IsDBNull(12) ? null : r.GetFieldValue<DateOnly>(12),
            DesignatedApproverId: r.IsDBNull(13) ? null : r.GetString(13),
            ApprovalMethod: r.IsDBNull(14) ? null : r.GetString(14));
    }

    /// <summary>
    /// S127 Step-7a F8 — force the LIVE denormalized cache (<c>users.agreement_code</c>) to a value that
    /// DIFFERS from the dated <c>user_agreement_codes</c> row seeded by <see cref="SeedEmployeeAsync"/>.
    /// The send resolves <c>agreement_code</c> as <c>GetByUserIdAtAsync(monthStart) ?? user.AgreementCode</c>;
    /// with the two sources equal, an AC-12 assertion could not tell which was read. After this call the
    /// dated lookup and the cache disagree, so the P4 test fails iff production reads the live cache.
    /// </summary>
    protected async Task SetLiveAgreementCodeAsync(string employeeId, string liveAgreementCode)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET agreement_code = @code WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("code", liveAgreementCode);
        cmd.Parameters.AddWithValue("id", employeeId);
        var rows = await cmd.ExecuteNonQueryAsync();
        Assert.Equal(1, rows); // the user row must exist, or the "live differs from dated" premise is vacuous
    }

    protected async Task<Guid?> FindPeriodIdAsync(string employeeId, DateOnly start, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT period_id FROM approval_periods WHERE employee_id=@e AND period_start=@s AND period_end=@en",
            conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("s", start);
        cmd.Parameters.AddWithValue("en", end);
        var result = await cmd.ExecuteScalarAsync();
        return result is Guid g ? g : null;
    }

    protected async Task<bool> RowExistsAsync(string employeeId, DateOnly start, DateOnly end)
        => (await FindPeriodIdAsync(employeeId, start, end)) is not null;

    // ── Counting audit / outbox / work-time rows ─────────────────────────────────────────────────

    protected static string StreamId(string employeeId) => $"approval-{employeeId}-{MarchStart:yyyy-MM-dd}";

    protected Task<long> CountApprovalAuditAsync(Guid periodId, string? action = null)
        => ScalarLongAsync(
            action is null
                ? "SELECT COUNT(*) FROM approval_audit WHERE period_id=@id"
                : "SELECT COUNT(*) FROM approval_audit WHERE period_id=@id AND action=@a",
            ("id", periodId), ("a", action ?? (object)DBNull.Value));

    /// <summary>The audit COMMENT for the (single, or first) SUBMITTED row on a period — AC-8 checks the
    /// conditional self / on-behalf comment (P3).</summary>
    protected async Task<string?> ReadFirstAuditCommentAsync(Guid periodId, string action)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT comment FROM approval_audit WHERE period_id=@id AND action=@a ORDER BY audit_id LIMIT 1", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        cmd.Parameters.AddWithValue("a", action);
        var v = await cmd.ExecuteScalarAsync();
        return v is null or DBNull ? null : (string)v;
    }

    // outbox_events rows are UPDATEd (published_at) by the publisher, NEVER deleted, so this COUNT is
    // stable regardless of whether the background publisher has drained the row.
    protected Task<long> CountOutboxAsync(string streamId, string eventType)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id=@s AND event_type=@t",
            ("s", streamId), ("t", eventType));

    // S127 Step-7a F6 — TOTAL outbox events on the stream, of ANY type. The by-type COUNT above cannot
    // catch a SPURIOUS extra event of a different type per send (e.g. a resurrected PeriodSubmitted): it
    // filters that event out. A fresh employee's single send emits exactly ONE event on its stream, so
    // "exactly one" must be pinned as the TOTAL, not just the count of the expected type.
    protected Task<long> CountOutboxTotalAsync(string streamId)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id=@s", ("s", streamId));

    protected Task<long> CountAuditProjectionByPeriodAsync(Guid periodId)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type='PeriodEmployeeApproved' AND target_resource_id=@id",
            ("id", periodId.ToString()));

    // S127 Step-7a F6 — TOTAL audit_projection rows for the period, of ANY event type. The by-type COUNT
    // above would miss a spurious projection row of another type carrying the same target_resource_id.
    protected Task<long> CountAuditProjectionTotalByPeriodAsync(Guid periodId)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE target_resource_id=@id",
            ("id", periodId.ToString()));

    protected Task<long> CountAuditProjectionByEmployeeAsync(string employeeId)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type='PeriodEmployeeApproved' AND details->>'employeeId'=@e",
            ("e", employeeId));

    protected Task<long> CountWorkTimeRowsAsync(string employeeId)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM work_time_projection WHERE employee_id=@e AND date >= @s AND date <= @en",
            ("e", employeeId), ("s", MarchStart), ("en", MarchEnd));

    protected async Task<long> ScalarLongAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    // ── Clients / tokens ─────────────────────────────────────────────────────────────────────────

    protected HttpClient RoleClient(string actorId, string role, string orgId, params RoleScope[] scopes)
    {
        var client = Fx.Factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>An ordinary employee acting on their OWN periods (the role floor's self case).</summary>
    protected HttpClient EmployeeClient(string employeeId, string orgId = Org)
        => RoleClient(employeeId, StatsTidRoles.Employee, orgId,
            new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY"));

    /// <summary>A GlobalAdmin ('/' GLOBAL scope) — passes every policy and the org-scope validator's
    /// GLOBAL short-circuit (its GLOBAL scope clears any role floor).</summary>
    protected HttpClient GlobalAdminClient(string actorId)
        => RoleClient(actorId, StatsTidRoles.GlobalAdmin, "/",
            new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL"));

    /// <summary>A client for <paramref name="actorId"/> at <paramref name="role"/> with the single
    /// scope that role would naturally carry: GLOBAL for GlobalAdmin, else an ORG_ONLY scope over
    /// <paramref name="orgId"/> AT that role. The AT-that-role part is what makes the R4 send-floor
    /// (LocalHR) discriminate LocalLeader from LocalHR/LocalAdmin.</summary>
    protected HttpClient ClientForRole(string actorId, string role, string orgId = Org)
        => role == StatsTidRoles.GlobalAdmin
            ? GlobalAdminClient(actorId)
            : RoleClient(actorId, role, orgId, new RoleScope(role, orgId, "ORG_ONLY"));
}
