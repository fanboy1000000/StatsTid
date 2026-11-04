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
/// S147 / TASK-14716-T (SEC-054 step 2, Orchestrator ruling R-G2) — the B2 re-check also asserts that the approval period an
/// export names is the approval of the month being exported.
///
/// <para>
/// <b>In plain language.</b> Every export now needs an approved month with two signers behind it, and the caller names which
/// approval that is. Without this check a caller could name ANY approved month — another employee's, or the same employee's
/// March while exporting April's lines — and the export would pass the approval test and write a record carrying the wrong
/// month's approval. The service now refuses (409) and writes nothing. The one route that exports reads the approval by the
/// request's own employee and dates, so it can never trip this; the check guards any other caller of the service.
/// </para>
///
/// <para>
/// <b>Why direct service calls.</b> The route cannot produce the mismatch, so the pin calls
/// <see cref="PayrollExportService.ExportAsync"/> itself with an APPROVED, four-eyes-clean period (<c>approved_by</c> and the
/// sender both NULL, so neither four-eyes rule can be what refuses) that belongs to someone or somewhere else.
/// </para>
///
/// <para>
/// <b>The check, and which fact pins which clause.</b> The identity check in <c>AssertPeriodApprovedForUpdateAsync</c> refuses
/// when <c>employeeId != group employee || periodStart != the 1st || periodEnd != the last day</c> (each clause alone is enough
/// to refuse). Each fact below is built so that exactly ONE clause refuses it, the others holding, and its docstring names the
/// single mutation that turns it red. Together: (a) delete the end clause is caught by <c>PeriodWithWrongEnd</c>; (b) delete the
/// start clause by <c>PeriodWithWrongStart</c>; (c) relax both date clauses to "within the month" by
/// <c>WeeklyPeriodInsideTheMonth</c>; (d) delete the employee clause by <c>PeriodOfAnotherEmployee</c>; (e) delete BOTH date
/// clauses by <c>PeriodOfAnotherMonth</c> (and by the three date facts). The facts are Docker facts and cannot run locally: each
/// mutation is argued by reading the check, not executed.
/// </para>
///
/// <para>
/// <b>Seeding.</b> <c>approval_periods</c> carries <c>UNIQUE (employee_id, period_start, period_end)</c> (init.sql and the test
/// schema). It is harmless here: every fact seeds one period for its own employee, so no two seeds can collide.
/// </para>
///
/// Docker-gated (Testcontainers Postgres): CI-pending until the sprint checkpoint, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollExportPeriodIdentityTests : IAsyncLifetime
{
    private const string OrgId = "ORG_PAYEXP_IDENT";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private PayrollExportService _service = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await AuditProjectionTestSchema.ApplyAsync(_harness.ConnectionString);
        await PayrollExportRecordsTestSchema.ApplyAsync(_harness.ConnectionString);

        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
                  VALUES (@id, 'Payroll Export Identity Test Org', 'ORGANISATION', @path)
                  ON CONFLICT DO NOTHING", conn);
            cmd.Parameters.AddWithValue("id", OrgId);
            cmd.Parameters.AddWithValue("path", $"/{OrgId}/");
            await cmd.ExecuteNonQueryAsync();
        }

        var factory = _harness.Factory;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ServiceUrls:MockPayroll"] = "http://mock-payroll-test" }).Build();
        _service = new PayrollExportService(
            new OkHttpClientFactory(),
            factory,
            new PostgresEventStore(factory, new OutboxServiceContext("payroll")),
            new AuditProjectionRepository(factory),
            new PayrollExportGeneratedAuditMapper(),
            configuration,
            NullLogger<PayrollExportService>.Instance);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// R-G2 (i), kind N (names <c>PayrollExportService.PeriodNotTheExportedMonthMessage</c>, which fix-up 1 created); against
    /// the code before it the behaviour is F: a call naming another employee's APPROVED period for the same month passed the
    /// status check and WROTE a record carrying that other employee's period id.
    /// Exporting EMP_IDENT_A's March lines under EMP_IDENT_B's APPROVED March period throws
    /// <see cref="PayrollExportConflictException"/> (→ 409) with exactly
    /// <c>PayrollExportService.PeriodNotTheExportedMonthMessage(periodId, "EMP_IDENT_B", 2026-03-01, 2026-03-31, "EMP_IDENT_A", 2026, 3)</c>,
    /// and writes no record, no event and no audit row (for either employee).
    ///
    /// <para><b>Red conditions.</b> The mutation "drop the employee comparison" (delete
    /// <c>!string.Equals(employeeId, key.EmployeeId, StringComparison.Ordinal) ||</c> from the identity check in
    /// <c>AssertPeriodApprovedForUpdateAsync</c>) leaves a file that compiles; the period is APPROVED and four-eyes clean, the
    /// month matches, so the export commits and <c>ThrowsAsync</c> fails with "no exception was thrown". This is the one fact that turns red under mutation (d) alone; the period is the exact whole
    /// month, so no date clause could refuse it. A check that ran AFTER the status check would not change this fact's outcome (the period
    /// is APPROVED); the order is pinned by the code's docstring, not here. CI-pending.</para>
    /// </summary>
    [Fact]
    public async Task ExportAsync_PeriodOfAnotherEmployee_Throws409_WritesNothing()
    {
        const string exported = "EMP_IDENT_A";
        const string other = "EMP_IDENT_B";
        var periodId = await SeedApprovedPeriodAsync(other, 2026, 3);
        var auditBefore = await CountAuditRowsAsync();

        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(
            () => _service.ExportAsync(new[] { Line(exported, 2026, 3) }, ContextWith(periodId)));

        Assert.Equal(
            PayrollExportService.PeriodNotTheExportedMonthMessage(
                periodId, other, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), exported, 2026, 3),
            ex.Message);
        await AssertNothingWrittenAsync(exported, 2026, 3, auditBefore);
        await AssertNothingWrittenAsync(other, 2026, 3, auditBefore);
    }

    /// <summary>
    /// R-G2 (ii), kind N (names <c>PeriodNotTheExportedMonthMessage</c>); behaviour F against master <c>19c1e6a1</c> (no export-time identity check) and against
    /// <c>124a7933</c> (status check only): a call naming the SAME employee's APPROVED March
    /// period while exporting April's lines passed the status check and wrote April's record with March's period id.
    /// Exporting EMP_IDENT_C's April lines under EMP_IDENT_C's own APPROVED March period throws
    /// <see cref="PayrollExportConflictException"/> with exactly
    /// <c>PeriodNotTheExportedMonthMessage(periodId, "EMP_IDENT_C", 2026-03-01, 2026-03-31, "EMP_IDENT_C", 2026, 4)</c>, and
    /// writes no record, no event and no audit row.
    ///
    /// <para><b>Red conditions.</b> Both date clauses refuse this period (March's start is not 1 April, its end is not 30 April),
    /// so deleting ONE clause leaves it green; it turns red only under mutation (e), deleting BOTH date clauses (the employee is
    /// equal and the status APPROVED, so the export commits an April record carrying March's period id and <c>ThrowsAsync</c>
    /// fails with "no exception was thrown"). It does NOT catch (a), (b) or (c) (March is outside April, so even "within the
    /// month" refuses it); the three facts that follow do. At <c>75b463b7</c> (both dates compared) it was green. CI-pending.</para>
    /// </summary>
    [Fact]
    public async Task ExportAsync_PeriodOfAnotherMonth_Throws409_WritesNothing()
    {
        const string emp = "EMP_IDENT_C";
        var periodId = await SeedApprovedPeriodAsync(emp, 2026, 3);
        var auditBefore = await CountAuditRowsAsync();

        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(
            () => _service.ExportAsync(new[] { Line(emp, 2026, 4) }, ContextWith(periodId)));

        Assert.Equal(
            PayrollExportService.PeriodNotTheExportedMonthMessage(
                periodId, emp, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), emp, 2026, 4),
            ex.Message);
        await AssertNothingWrittenAsync(emp, 2026, 4, auditBefore);
        await AssertNothingWrittenAsync(emp, 2026, 3, auditBefore);
    }

    /// <summary>
    /// R-G8 END-clause pin (renamed from <c>ExportAsync_PeriodStraddlingTheMonthEnd_Throws409_WritesNothing</c>), kind N (names
    /// <c>PeriodNotTheExportedMonthMessage</c>). The same employee's APPROVED, four-eyes-clean period 2026-04-01..2026-05-03
    /// (it STARTS on the 1st and runs past the month end) with that employee's whole-month April lines. The employee matches and
    /// the start is exactly 1 April, so ONLY the end clause (<c>periodEnd != 30 April</c>) refuses it: 409 with exactly
    /// <c>PeriodNotTheExportedMonthMessage</c>, and no record, event or audit row.
    ///
    /// <para><b>Red conditions.</b> Mutation (a): delete <c>|| periodEnd != monthEnd</c>. The remaining clauses all hold, the
    /// period is APPROVED and four-eyes clean, so the export commits an April record whose period reaches into May and
    /// <c>ThrowsAsync</c> fails with "no exception was thrown". Mutation (b) (start clause deleted) leaves it green because the
    /// end clause still refuses; mutation (c) ("within the month") also refuses it (3 May is outside April). Kind against
    /// master <c>19c1e6a1</c>: F in behaviour (the export committed); it cannot compile there. Against <c>75b463b7</c> (both
    /// dates within the month): refused there too, so a regression pin there. Argued from the code, not run (Docker). CI-pending.</para>
    /// </summary>
    [Fact]
    public async Task ExportAsync_PeriodWithWrongEnd_Throws409_WritesNothing()
    {
        const string emp = "EMP_IDENT_D";
        var start = new DateOnly(2026, 4, 1);
        var end = new DateOnly(2026, 5, 3);
        var periodId = await SeedApprovedPeriodAsync(emp, start, end, "MONTHLY");
        var auditBefore = await CountAuditRowsAsync();

        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(
            () => _service.ExportAsync(new[] { Line(emp, 2026, 4) }, ContextWith(periodId)));

        Assert.Equal(PayrollExportService.PeriodNotTheExportedMonthMessage(periodId, emp, start, end, emp, 2026, 4), ex.Message);
        await AssertNothingWrittenAsync(emp, 2026, 4, auditBefore);
    }

    /// <summary>
    /// R-G8 START-clause pin, kind N (names <c>PeriodNotTheExportedMonthMessage</c>). The same employee's APPROVED, four-eyes-clean
    /// period 2026-04-02..2026-04-30 (it ENDS on the last day but starts on the 2nd) with that employee's whole-month April lines.
    /// The employee matches and the end is exactly 30 April, so ONLY the start clause (<c>periodStart != 1 April</c>) refuses it:
    /// 409 with exactly <c>PeriodNotTheExportedMonthMessage</c>, and no record, event or audit row.
    ///
    /// <para><b>Red conditions.</b> Mutation (b): delete <c>|| periodStart != monthStart</c>. The remaining clauses all hold, so
    /// the export commits and <c>ThrowsAsync</c> fails with "no exception was thrown". Mutation (a) (end clause deleted) leaves it
    /// green because the start clause still refuses. Mutation (c) ("within the month") ACCEPTS this period (both dates are in
    /// April) and turns it red too. Against master <c>19c1e6a1</c>: F in behaviour (the export committed). Against
    /// <c>75b463b7</c> (both dates only had to be within the month): F as well, the period passed. Argued from the code, not run
    /// (Docker). CI-pending.</para>
    /// </summary>
    [Fact]
    public async Task ExportAsync_PeriodWithWrongStart_Throws409_WritesNothing()
    {
        const string emp = "EMP_IDENT_F";
        var start = new DateOnly(2026, 4, 2);
        var end = new DateOnly(2026, 4, 30);
        var periodId = await SeedApprovedPeriodAsync(emp, start, end, "MONTHLY");
        var auditBefore = await CountAuditRowsAsync();

        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(
            () => _service.ExportAsync(new[] { Line(emp, 2026, 4) }, ContextWith(periodId)));

        Assert.Equal(PayrollExportService.PeriodNotTheExportedMonthMessage(periodId, emp, start, end, emp, 2026, 4), ex.Message);
        await AssertNothingWrittenAsync(emp, 2026, 4, auditBefore);
    }

    /// <summary>
    /// R-G8 "within the month" pin, kind N (names <c>PeriodNotTheExportedMonthMessage</c>): a WEEKLY period wholly inside April,
    /// 2026-04-06..2026-04-12, for the SAME employee, with that employee's whole-month April lines. A week is not the month's
    /// approval. Both date clauses refuse it (neither date is the 1st or the last day); 409 with exactly
    /// <c>PeriodNotTheExportedMonthMessage</c>, and no record, event or audit row.
    ///
    /// <para><b>Red conditions.</b> Mutation (c): relax the two equalities to "within the month" (<c>periodStart</c> and
    /// <c>periodEnd</c> must merely lie in the exported year and month, the behaviour at <c>75b463b7</c>). Both dates are in April,
    /// so the period is accepted, the export commits and <c>ThrowsAsync</c> fails with "no exception was thrown". Deleting only one
    /// date clause (a or b) leaves it green, because the other still refuses it. Against master <c>19c1e6a1</c>: F in behaviour;
    /// against <c>75b463b7</c>: F. Argued from the code, not run (Docker). CI-pending.</para>
    /// </summary>
    [Fact]
    public async Task ExportAsync_WeeklyPeriodInsideTheMonth_Throws409_WritesNothing()
    {
        const string emp = "EMP_IDENT_E";
        var start = new DateOnly(2026, 4, 6);
        var end = new DateOnly(2026, 4, 12);
        var periodId = await SeedApprovedPeriodAsync(emp, start, end, "WEEKLY");
        var auditBefore = await CountAuditRowsAsync();

        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(
            () => _service.ExportAsync(new[] { Line(emp, 2026, 4) }, ContextWith(periodId)));

        Assert.Equal(PayrollExportService.PeriodNotTheExportedMonthMessage(periodId, emp, start, end, emp, 2026, 4), ex.Message);
        await AssertNothingWrittenAsync(emp, 2026, 4, auditBefore);
    }

    private async Task AssertNothingWrittenAsync(string employeeId, int year, int month, int auditBefore)
    {
        Assert.Equal(0, await CountRecordsAsync(employeeId, year, month));
        Assert.Equal(0, await CountExportEventsAsync(employeeId));
        Assert.Equal(auditBefore, await CountAuditRowsAsync());
    }

    private static PayrollExportContext ContextWith(Guid periodId) => new()
    {
        PeriodId = periodId,
        Source = "CALCULATE_AND_EXPORT",
        ActorId = "admin-test",
        ActorRole = "GlobalAdmin",
        ResolvedTargetOrgId = OrgId,
    };

    private static PayrollExportLine Line(string employeeId, int year, int month) => new()
    {
        EmployeeId = employeeId,
        WageType = "SLS_0110",
        Hours = 7.4m,
        Amount = 1000m,
        PeriodStart = new DateOnly(year, month, 1),
        PeriodEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
        OkVersion = "OK24",
        SourceTimeType = "NORMAL_HOURS",
    };

    /// <summary>An APPROVED whole-month period with <c>approved_by</c>, <c>employee_approved_by</c> and
    /// <c>submitted_by</c> all NULL: neither four-eyes rule can match, so only the identity check can refuse it.</summary>
    private Task<Guid> SeedApprovedPeriodAsync(string employeeId, int year, int month) =>
        SeedApprovedPeriodAsync(
            employeeId, new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)), "MONTHLY");

    private async Task<Guid> SeedApprovedPeriodAsync(string employeeId, DateOnly start, DateOnly end, string periodType)
    {
        var periodId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version)
            VALUES
                (@pid, @emp, @org, @start, @end, @ptype, 'APPROVED', 'AC', 'OK24')
            """, conn);
        cmd.Parameters.AddWithValue("pid", periodId);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("ptype", periodType);
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
