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
using Xunit;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S147 / TASK-14704-T (SEC-053, AC-053-4) — payroll is the last place an approval turns into money, so it
/// re-checks four-eyes itself: an APPROVED month whose approver is the month's SENDER, or the month's own
/// EMPLOYEE, is refused at export (the existing 409-class <see cref="PayrollExportConflictException"/>) and
/// the whole export transaction writes nothing. A month with no sender stamp is not blocked by the sender
/// half.
///
/// <para><b>Why the rows are seeded directly.</b> The Backend now refuses to PRODUCE such a month, so the
/// only way to test payroll's own re-check is to seed the forbidden state the way an older release, a manual
/// edit or a future bug could have left it — exactly the case a re-check at the last gate exists for.</para>
///
/// <para><b>Level.</b> These facts drive <see cref="PayrollExportService.ExportAsync"/> with a
/// <c>PeriodId</c> in the context (the only route that threads an approval period into the export — the raw
/// routes carry none and skip the check), over a real Postgres, against the test copy of
/// <c>approval_periods</c> in <see cref="PayrollExportRecordsTestSchema"/> (which now carries
/// <c>employee_approved_by</c>, as init.sql does). The expected values are the contract's
/// (<c>COALESCE(employee_approved_by, submitted_by)</c> = sender; refusal when <c>approved_by</c> equals the
/// sender or the employee; a null / empty value never matches), not read from the code.</para>
///
/// Docker-gated (CI-verified only).
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollSenderSeparationHostTests : IAsyncLifetime
{
    private const string OrgId = "ORG_SENDERSEP";
    private static readonly DateOnly Oct01 = new(2026, 10, 1);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

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
                  VALUES (@id, 'Sender Separation Org', 'ORGANISATION', @path)
                  ON CONFLICT DO NOTHING", conn);
            cmd.Parameters.AddWithValue("id", OrgId);
            cmd.Parameters.AddWithValue("path", $"/{OrgId}/");
            await cmd.ExecuteNonQueryAsync();
        }

        var factory = _harness.Factory;
        var eventStore = new PostgresEventStore(factory, new OutboxServiceContext("payroll"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ServiceUrls:MockPayroll"] = "http://mock-payroll-test" }).Build();
        _service = new PayrollExportService(
            new OkHttpClientFactory(),
            factory,
            eventStore,
            new AuditProjectionRepository(factory),
            new PayrollExportGeneratedAuditMapper(),
            configuration,
            NullLogger<PayrollExportService>.Instance);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// AC-053-4 (sender, current shape) — kind <b>F</b>. APPROVED month whose approver equals
    /// <c>employee_approved_by</c> (the sender): the export is refused (<see cref="PayrollExportConflictException"/>),
    /// mentions four-eyes, and NOTHING is written.
    ///
    /// <para><b>Red conditions.</b> Today <c>AssertPeriodApprovedForUpdateAsync</c> reads only <c>status</c>:
    /// an APPROVED row passes, the export commits a record and <c>Assert.ThrowsAsync</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task Export_ApprovedBySender_EmployeeApprovedByStamp_Refused_NothingWritten()
    {
        const string emp = "EMP_SENDERSEP_1";
        var periodId = await SeedAsync(emp, submittedBy: "hr-sender", employeeApprovedBy: "hr-sender", approvedBy: "hr-sender");

        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(() => ExportAsync(emp, periodId));
        Assert.Contains("four-eyes", ex.Message);
        await AssertNothingWrittenAsync(emp);
    }

    /// <summary>
    /// AC-053-4 (sender, legacy shape) — kind <b>F</b>. A legacy row stamped only with <c>submitted_by</c>
    /// (<c>employee_approved_by</c> NULL): the sender is <c>submitted_by</c>; an approver equal to it is
    /// refused.
    ///
    /// <para><b>Red conditions.</b> Today the legacy row exports: <c>Assert.ThrowsAsync</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task Export_ApprovedBySender_LegacySubmittedByOnly_Refused_NothingWritten()
    {
        const string emp = "EMP_SENDERSEP_2";
        var periodId = await SeedAsync(emp, submittedBy: "hr-legacy", employeeApprovedBy: null, approvedBy: "hr-legacy");

        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(() => ExportAsync(emp, periodId));
        Assert.Contains("four-eyes", ex.Message);
        await AssertNothingWrittenAsync(emp);
    }

    /// <summary>
    /// AC-053-4 (employee) — kind <b>F</b>. APPROVED month whose approver is the month's own EMPLOYEE (the
    /// SEC-009 rule, re-checked at payroll) while the sender is someone else: refused.
    ///
    /// <para><b>Red conditions.</b> Today payroll does not compare the approver with the employee: the
    /// export commits and <c>Assert.ThrowsAsync</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task Export_ApprovedByTheEmployee_Refused_NothingWritten()
    {
        const string emp = "EMP_SENDERSEP_3";
        var periodId = await SeedAsync(emp, submittedBy: "hr-other", employeeApprovedBy: "hr-other", approvedBy: emp);

        var ex = await Assert.ThrowsAsync<PayrollExportConflictException>(() => ExportAsync(emp, periodId));
        Assert.Contains("own employee", ex.Message);
        await AssertNothingWrittenAsync(emp);
    }

    /// <summary>
    /// AC-053-4 (null sender passes) — kind <b>R</b> (regression pin: months with no sender stamp — as the
    /// existing payroll host fixtures seed them — keep exporting). Both sender stamps NULL, approver a third
    /// person: the export succeeds and exactly one record is written.
    /// </summary>
    [Fact]
    public async Task Export_NullSender_ApprovedByThirdPerson_Passes()
    {
        const string emp = "EMP_SENDERSEP_4";
        var periodId = await SeedAsync(emp, submittedBy: null, employeeApprovedBy: null, approvedBy: "hr-approver");

        var result = await ExportAsync(emp, periodId);
        Assert.True(result.Success);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", emp)));
    }

    /// <summary>
    /// AC-053-4 (two different people sign) — kind <b>R</b>. Sender and approver are different people and
    /// neither is the employee: the export succeeds. The positive control that the refusals above are about
    /// the equality and not about any month carrying sender stamps.
    /// </summary>
    [Fact]
    public async Task Export_SenderAndApproverDistinct_Passes()
    {
        const string emp = "EMP_SENDERSEP_5";
        var periodId = await SeedAsync(emp, submittedBy: "hr-a", employeeApprovedBy: "hr-a", approvedBy: "hr-b");

        var result = await ExportAsync(emp, periodId);
        Assert.True(result.Success);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", emp)));
    }

    /// <summary>
    /// AC-053-4 (sender definition) — kind <b>B</b> (boundary pin — the sender is
    /// <c>COALESCE(employee_approved_by, submitted_by)</c>, so <c>employee_approved_by</c> wins when both are
    /// stamped). <c>submitted_by = hr-x</c>, <c>employee_approved_by = hr-y</c>, approver <c>hr-x</c>: the
    /// sender is hr-y, hr-x is not it, the export passes. A re-check that compared against
    /// <c>submitted_by</c> would refuse it.
    /// </summary>
    [Fact]
    public async Task Export_ApproverEqualsSubmittedByButNotEmployeeApprovedBy_Passes()
    {
        const string emp = "EMP_SENDERSEP_6";
        var periodId = await SeedAsync(emp, submittedBy: "hr-x", employeeApprovedBy: "hr-y", approvedBy: "hr-x");

        var result = await ExportAsync(emp, periodId);
        Assert.True(result.Success);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", emp)));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<ExportResult> ExportAsync(string employeeId, Guid periodId)
    {
        var context = new PayrollExportContext
        {
            PeriodId = periodId,
            Source = "CALCULATE_AND_EXPORT",
            ActorId = "admin-test",
            ActorRole = "GlobalAdmin",
            ResolvedTargetOrgId = OrgId,
        };
        var lines = new[]
        {
            new PayrollExportLine
            {
                EmployeeId = employeeId,
                WageType = "SLS_0110",
                Hours = 7.4m,
                Amount = 1000m,
                PeriodStart = Oct01,
                PeriodEnd = Oct31,
                OkVersion = "OK26",
                SourceTimeType = "NORMAL_HOURS",
            },
        };
        return _service.ExportAsync(lines, context);
    }

    /// <summary>Seeds an APPROVED October-2026 row with EXACTLY the three stamps the test names (any may be
    /// NULL).</summary>
    private async Task<Guid> SeedAsync(string employeeId, string? submittedBy, string? employeeApprovedBy, string? approvedBy)
    {
        var periodId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version,
                 submitted_by, employee_approved_by, approved_by, approved_at)
            VALUES
                (@pid, @emp, @org, @start, @end, 'MONTHLY', 'APPROVED', 'AC', 'OK26', @sb, @eab, @ab, NOW())
            """, conn);
        cmd.Parameters.AddWithValue("pid", periodId);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", Oct01);
        cmd.Parameters.AddWithValue("end", Oct31);
        cmd.Parameters.AddWithValue("sb", (object?)submittedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("eab", (object?)employeeApprovedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ab", (object?)approvedBy ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return periodId;
    }

    /// <summary>A refused export rolls back as a whole: no record, no event, no audit row for the employee.</summary>
    private async Task AssertNothingWrittenAsync(string employeeId)
    {
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", employeeId)));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'PayrollExportGenerated'",
            ("s", $"employee-{employeeId}")));
    }

#pragma warning disable CA2100 // every sql here is a hardcoded literal; values flow through parameters.
    private async Task<int> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
#pragma warning restore CA2100

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
