using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;
using StatsTid.Tests.Regression.PhaseE;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S90 / TASK-9002 (ADR-034) — regression tests for the ATOMIC payroll-export refactor:
/// the per-(employee, year, month) lock record, idempotency, the B2 APPROVED <c>FOR UPDATE</c>
/// re-check, the B4 multi-month/multi-employee grouping + month-spanning rejection, the
/// <c>PayrollExportGenerated</c> event emission to <c>outbox_events</c>, the audit row, and the
/// zero-line no-lock rule.
///
/// <para>
/// The idempotency tests are RED on the pre-refactor <c>ExportAsync(lines, ct)</c> (which wrote a
/// fresh <c>outbox_messages</c> row on every call and NO <c>payroll_export_records</c> row, so a
/// 2nd call duplicated rather than no-op'd and there was no lock to read).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollExportRecordTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private PayrollExportService _service = null!;

    private const string OrgId = "ORG_PAYEXP";

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        // outbox_events + schema_migrations; audit_projection + organizations + users; the new tables.
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await AuditProjectionTestSchema.ApplyAsync(_harness.ConnectionString);
        await PayrollExportRecordsTestSchema.ApplyAsync(_harness.ConnectionString);

        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
                  VALUES (@id, 'Payroll Export Test Org', 'STYRELSE', @path)
                  ON CONFLICT DO NOTHING", conn);
            cmd.Parameters.AddWithValue("id", OrgId);
            cmd.Parameters.AddWithValue("path", $"/{OrgId}/");
            await cmd.ExecuteNonQueryAsync();
        }

        _service = BuildService();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    private PayrollExportService BuildService()
    {
        var factory = _harness.Factory;
        var eventStore = new PostgresEventStore(factory, new OutboxServiceContext("payroll"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ServiceUrls:MockPayroll"] = "http://mock-payroll-test" }).Build();
        return new PayrollExportService(
            new OkHttpClientFactory(),
            factory,
            eventStore,
            new AuditProjectionRepository(factory),
            new PayrollExportGeneratedAuditMapper(),
            configuration,
            NullLogger<PayrollExportService>.Instance);
    }

    private static PayrollExportContext CalcAndExportContext(Guid periodId) => new()
    {
        PeriodId = periodId,
        Source = "CALCULATE_AND_EXPORT",
        ActorId = "admin-test",
        ActorRole = "GlobalAdmin",
        ResolvedTargetOrgId = OrgId,
    };

    private static PayrollExportContext RawExportContext() => new()
    {
        PeriodId = null,
        Source = "EXPORT_PERIOD",
        ActorId = "admin-test",
        ActorRole = "GlobalAdmin",
        ResolvedTargetOrgId = OrgId,
    };

    private static PayrollExportLine Line(
        string employeeId, int year, int month, string wageType = "SLS_0110",
        decimal hours = 7.4m, decimal amount = 1000m) => new()
    {
        EmployeeId = employeeId,
        WageType = wageType,
        Hours = hours,
        Amount = amount,
        PeriodStart = new DateOnly(year, month, 1),
        PeriodEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
        OkVersion = "OK24",
        SourceTimeType = "NORMAL_HOURS",
    };

    private async Task<Guid> SeedApprovedPeriodAsync(string employeeId, int year, int month, string status = "APPROVED")
    {
        var periodId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version)
            VALUES
                (@pid, @emp, @org, @start, @end, 'MONTHLY', @status, 'AC', 'OK24')
            """, conn);
        cmd.Parameters.AddWithValue("pid", periodId);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", new DateOnly(year, month, 1));
        cmd.Parameters.AddWithValue("end", new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync();
        return periodId;
    }

    private async Task<int> CountRecordsAsync(string employeeId, int year, int month)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id=@e AND year=@y AND month=@m", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountExportEventsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id=@s AND event_type='PayrollExportGenerated'", conn);
        cmd.Parameters.AddWithValue("s", $"employee-{employeeId}");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountAuditRowsAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type='PayrollExportGenerated'", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Returns the JSON payloads of all <c>destination='payroll'</c> outbox envelopes, newest first.</summary>
    private async Task<IReadOnlyList<string>> ReadPayrollOutboxPayloadsAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT payload::text FROM outbox_messages WHERE destination='payroll' ORDER BY created_at DESC", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var payloads = new List<string>();
        while (await reader.ReadAsync())
            payloads.Add(reader.GetString(0));
        return payloads;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Idempotency (RED on the pre-refactor behaviour)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SecondExport_SameApprovedMonth_SameContent_IsNoOp_NoDuplicateRecordOrEvent()
    {
        const string emp = "EMP_IDEM";
        var periodId = await SeedApprovedPeriodAsync(emp, 2026, 1);
        var lines = new[] { Line(emp, 2026, 1) };

        var first = await _service.ExportAsync(lines, CalcAndExportContext(periodId));
        Assert.True(first.Success);

        // RED-on-old: pre-refactor, a 2nd call wrote ANOTHER outbox_messages row (no record table at
        // all); now it is an idempotent no-op returning the SAME export id.
        var second = await _service.ExportAsync(lines, CalcAndExportContext(periodId));
        Assert.True(second.Success);
        Assert.Equal(first.ExportId, second.ExportId);

        Assert.Equal(1, await CountRecordsAsync(emp, 2026, 1));
        Assert.Equal(1, await CountExportEventsAsync(emp)); // exactly ONE event, not two
    }

    [Fact]
    public async Task SecondExport_SameApprovedMonth_DifferentContent_Throws409Conflict()
    {
        const string emp = "EMP_DIFF";
        var periodId = await SeedApprovedPeriodAsync(emp, 2026, 2);

        var first = await _service.ExportAsync(new[] { Line(emp, 2026, 2, amount: 1000m) }, CalcAndExportContext(periodId));
        Assert.True(first.Success);

        // Different content (amount changed) for the SAME (employee, year, month) → must reject.
        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(
            () => _service.ExportAsync(new[] { Line(emp, 2026, 2, amount: 9999m) }, CalcAndExportContext(periodId)));
        Assert.Contains("already has a payroll export", ex.Message);

        // The original record + event remain singular; the rejected attempt wrote nothing.
        Assert.Equal(1, await CountRecordsAsync(emp, 2026, 2));
        Assert.Equal(1, await CountExportEventsAsync(emp));
    }

    [Fact]
    public async Task MixedExport_OneNoOpMonth_OneNewMonth_DeliversOnlyNewMonthLines()
    {
        // BLOCKER 1 — a mixed /export-period: month A already exported (same hash → no-op) +
        // month B new. The post-commit delivery (outbox_messages envelope + the mock HTTP POST)
        // must carry ONLY month B's lines, NOT re-deliver the already-exported month A.
        const string emp = "EMP_MIXED";
        // Distinct, identifiable wage types so each month's lines are recognisable in the payload JSON.
        var monthA = Line(emp, 2026, 1, wageType: "SLS_MONTH_A", amount: 111m);
        var monthB = Line(emp, 2026, 2, wageType: "SLS_MONTH_B", amount: 222m);

        // First: export month A alone → it is delivered (and its envelope contains A).
        var firstA = await _service.ExportAsync(new[] { monthA }, RawExportContext());
        Assert.True(firstA.Success);
        var afterFirst = await ReadPayrollOutboxPayloadsAsync();
        Assert.Single(afterFirst);
        Assert.Contains("SLS_MONTH_A", afterFirst[0]);

        // Now: a MIXED call — month A (same hash → no-op) + month B (new).
        var mixed = await _service.ExportAsync(new[] { monthA, monthB }, RawExportContext());
        Assert.True(mixed.Success);

        // Exactly ONE new envelope was written by the mixed call (the no-op month wrote nothing new).
        var afterMixed = await ReadPayrollOutboxPayloadsAsync();
        Assert.Equal(2, afterMixed.Count);
        var newest = afterMixed[0]; // newest first

        // RED-on-old: pre-fix the envelope/POST were built from the FULL `lines`, so the newest
        // payload would contain BOTH SLS_MONTH_A and SLS_MONTH_B → month A re-delivered.
        Assert.Contains("SLS_MONTH_B", newest);          // the NEW month IS delivered
        Assert.DoesNotContain("SLS_MONTH_A", newest);    // the already-exported month is NOT re-delivered

        // Records/events are singular per month (the no-op added neither).
        Assert.Equal(1, await CountRecordsAsync(emp, 2026, 1));
        Assert.Equal(1, await CountRecordsAsync(emp, 2026, 2));
        Assert.Equal(2, await CountExportEventsAsync(emp)); // one per month, total
    }

    [Fact]
    public async Task ReorderedLines_SameMonth_HashesIdentically_NoOp()
    {
        const string emp = "EMP_ORDER";
        var periodId = await SeedApprovedPeriodAsync(emp, 2026, 3);
        var a = Line(emp, 2026, 3, wageType: "SLS_0110", amount: 100m);
        var b = Line(emp, 2026, 3, wageType: "SLS_0220", amount: 200m);

        var first = await _service.ExportAsync(new[] { a, b }, CalcAndExportContext(periodId));
        // Reordered → same content hash → idempotent no-op (not a 409).
        var second = await _service.ExportAsync(new[] { b, a }, CalcAndExportContext(periodId));

        Assert.Equal(first.ExportId, second.ExportId);
        Assert.Equal(1, await CountRecordsAsync(emp, 2026, 3));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  B4 — multi-month / multi-employee grouping + month-spanning rejection
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportPeriod_TwoMonths_TwoEmployees_WritesOneRecordPerEmployeeMonth()
    {
        // The raw /export-period flattens many months/employees into one call (PeriodId null →
        // no APPROVED re-check). Each (employee, month) tuple gets exactly ONE record, atomically.
        var lines = new[]
        {
            Line("EMP_A", 2026, 4),
            Line("EMP_A", 2026, 5),
            Line("EMP_B", 2026, 4),
            Line("EMP_B", 2026, 5),
        };

        var result = await _service.ExportAsync(lines, RawExportContext());
        Assert.True(result.Success);

        Assert.Equal(1, await CountRecordsAsync("EMP_A", 2026, 4));
        Assert.Equal(1, await CountRecordsAsync("EMP_A", 2026, 5));
        Assert.Equal(1, await CountRecordsAsync("EMP_B", 2026, 4));
        Assert.Equal(1, await CountRecordsAsync("EMP_B", 2026, 5));

        // One PayrollExportGenerated per (employee, month): 2 per employee.
        Assert.Equal(2, await CountExportEventsAsync("EMP_A"));
        Assert.Equal(2, await CountExportEventsAsync("EMP_B"));
    }

    [Fact]
    public async Task MonthSpanningLine_IsRejected_NoRecordWritten()
    {
        var spanning = new PayrollExportLine
        {
            EmployeeId = "EMP_SPAN",
            WageType = "SLS_0110",
            Hours = 10m,
            Amount = 500m,
            PeriodStart = new DateOnly(2026, 6, 15),
            PeriodEnd = new DateOnly(2026, 7, 15), // spans June→July
            OkVersion = "OK24",
            SourceTimeType = "NORMAL_HOURS",
        };

        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(
            () => _service.ExportAsync(new[] { spanning }, RawExportContext()));
        Assert.Contains("spans more than one calendar month", ex.Message);

        Assert.Equal(0, await CountRecordsAsync("EMP_SPAN", 2026, 6));
        Assert.Equal(0, await CountRecordsAsync("EMP_SPAN", 2026, 7));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  B2 — the APPROVED FOR UPDATE re-check
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NonApprovedPeriod_AtExportTime_AbortsTx_NoRecordWritten()
    {
        const string emp = "EMP_NOTAPP";
        // The period exists but is SUBMITTED (e.g. a concurrent reopen flipped it before export).
        var periodId = await SeedApprovedPeriodAsync(emp, 2026, 8, status: "SUBMITTED");

        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(
            () => _service.ExportAsync(new[] { Line(emp, 2026, 8) }, CalcAndExportContext(periodId)));
        Assert.Contains("not APPROVED", ex.Message);

        Assert.Equal(0, await CountRecordsAsync(emp, 2026, 8));
        Assert.Equal(0, await CountExportEventsAsync(emp));
    }

    [Fact]
    public async Task MissingPeriod_AtExportTime_AbortsTx_NoRecordWritten()
    {
        const string emp = "EMP_NOPERIOD";
        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(
            () => _service.ExportAsync(new[] { Line(emp, 2026, 9) }, CalcAndExportContext(Guid.NewGuid())));
        Assert.Contains("not found", ex.Message);
        Assert.Equal(0, await CountRecordsAsync(emp, 2026, 9));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Emission + audit + zero-line
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Export_EmitsEventOnEmployeeStream_AndWritesAuditRow()
    {
        const string emp = "EMP_EMIT";
        var periodId = await SeedApprovedPeriodAsync(emp, 2026, 10);

        var auditBefore = await CountAuditRowsAsync();
        await _service.ExportAsync(new[] { Line(emp, 2026, 10) }, CalcAndExportContext(periodId));

        Assert.Equal(1, await CountExportEventsAsync(emp)); // stream_id = employee-EMP_EMIT
        Assert.Equal(auditBefore + 1, await CountAuditRowsAsync());

        // Audit row carries the resolved target org + the employee resource id (TENANT_TARGETED).
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT visibility_scope, target_org_id, target_resource_id
              FROM audit_projection WHERE event_type='PayrollExportGenerated' AND target_resource_id=@emp", conn);
        cmd.Parameters.AddWithValue("emp", emp);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("TENANT_TARGETED", reader.GetString(0));
        Assert.Equal(OrgId, reader.GetString(1));
        Assert.Equal(emp, reader.GetString(2));
    }

    [Fact]
    public async Task ZeroLines_WritesNoRecord_NoEvent()
    {
        const string emp = "EMP_ZERO";
        var periodId = await SeedApprovedPeriodAsync(emp, 2026, 11);

        var result = await _service.ExportAsync(Array.Empty<PayrollExportLine>(), CalcAndExportContext(periodId));
        Assert.True(result.Success); // a benign no-op

        Assert.Equal(0, await CountRecordsAsync(emp, 2026, 11));
        Assert.Equal(0, await CountExportEventsAsync(emp));
    }

    [Fact]
    public async Task RecordRow_CapturesManifest_ContentHash_AndSource()
    {
        const string emp = "EMP_MANIFEST";
        var periodId = await SeedApprovedPeriodAsync(emp, 2026, 12);
        await _service.ExportAsync(new[] { Line(emp, 2026, 12) }, CalcAndExportContext(periodId));

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT period_id, content_hash, source, original_lines::text, current_effective_lines::text
              FROM payroll_export_records WHERE employee_id=@e AND year=2026 AND month=12", conn);
        cmd.Parameters.AddWithValue("e", emp);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(periodId, reader.GetGuid(0));
        Assert.False(string.IsNullOrWhiteSpace(reader.GetString(1)));   // content_hash
        Assert.Equal("CALCULATE_AND_EXPORT", reader.GetString(2));       // source
        // original_lines == current_effective_lines on a first export (B3: corrections update the latter).
        Assert.Equal(reader.GetString(3), reader.GetString(4));
        Assert.Contains("SLS_0110", reader.GetString(3));               // the manifest holds the line
    }

    // ── A stub IHttpClientFactory whose client always returns 200 OK (mock-payroll delivery). ──
    private sealed class OkHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new OkHandler());
        private sealed class OkHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
