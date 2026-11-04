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
    /// month matches, so the export commits and <c>ThrowsAsync</c> fails with "no exception was thrown". Dropping the whole
    /// check fails this fact and (ii). A check that ran AFTER the status check would not change this fact's outcome (the period
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
    /// R-G2 (ii), kind N, behaviour F against the code before fix-up 1: a call naming the SAME employee's APPROVED March
    /// period while exporting April's lines passed the status check and wrote April's record with March's period id.
    /// Exporting EMP_IDENT_C's April lines under EMP_IDENT_C's own APPROVED March period throws
    /// <see cref="PayrollExportConflictException"/> with exactly
    /// <c>PeriodNotTheExportedMonthMessage(periodId, "EMP_IDENT_C", 2026-03-01, 2026-03-31, "EMP_IDENT_C", 2026, 4)</c>, and
    /// writes no record, no event and no audit row.
    ///
    /// <para><b>Red conditions.</b> The mutation "drop the month comparison" (delete the start and end clauses of the
    /// whole-month check) leaves the employee equal and the status
    /// APPROVED, so the export commits an April record carrying March's period id and <c>ThrowsAsync</c> fails with "no
    /// exception was thrown". The two facts that follow pin the half-measures. CI-pending.</para>
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
    /// R-G8 (i), kind N, behaviour F against `e81e02b9^` (before fix-up 2 only a start inside the month was
    /// compared): a period that STRADDLES the month end, 2026-04-27..2026-05-03, for the SAME employee, with that employee's
    /// whole-month April lines. The period is APPROVED and four-eyes clean, the employee matches, its start lies in April, so
    /// only an end-equals-last-day check refuses it: 409 with exactly <c>PeriodNotTheExportedMonthMessage</c>, and no record,
    /// event or audit row. (Both seeds satisfy the constraints: <c>period_type</c> WEEKLY is allowed by its CHECK in the test schema and <c>init.sql</c>, and there is no
    /// UNIQUE on the period dates.)
    ///
    /// <para><b>Red conditions.</b> The mutation "drop the <c>period_end</c> equality" (the end is no longer compared, or is
    /// only required to start in the month) lets this period through; the export commits April's record carrying a period
    /// that reaches into May, so <c>ThrowsAsync</c> fails with "no exception was thrown". CI-pending.</para>
    /// </summary>
    [Fact]
    public async Task ExportAsync_PeriodStraddlingTheMonthEnd_Throws409_WritesNothing()
    {
        const string emp = "EMP_IDENT_D";
        var start = new DateOnly(2026, 4, 27);
        var end = new DateOnly(2026, 5, 3);
        var periodId = await SeedApprovedPeriodAsync(emp, start, end, "WEEKLY");
        var auditBefore = await CountAuditRowsAsync();

        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(
            () => _service.ExportAsync(new[] { Line(emp, 2026, 4) }, ContextWith(periodId)));

        Assert.Equal(PayrollExportService.PeriodNotTheExportedMonthMessage(periodId, emp, start, end, emp, 2026, 4), ex.Message);
        await AssertNothingWrittenAsync(emp, 2026, 4, auditBefore);
    }

    /// <summary>
    /// R-G8 (ii), kind N, behaviour F against the within-the-month check of <c>e81e02b9^</c>: a WEEKLY period wholly inside
    /// April, 2026-04-06..2026-04-12, for the SAME employee, with that employee's whole-month April lines. Both dates lie in
    /// April, so only a "start is the 1st and end is the last day" check refuses it: 409 with exactly
    /// <c>PeriodNotTheExportedMonthMessage</c>, and no record, event or audit row. (A week is not the month's approval; a
    /// month is paid on the month's own approval.)
    ///
    /// <para><b>Red conditions.</b> The mutation "relax the start/end equalities to 'within the month'" (the behaviour before
    /// fix-up 2) accepts this period; the export commits and <c>ThrowsAsync</c> fails with "no exception was thrown".
    /// CI-pending.</para>
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
