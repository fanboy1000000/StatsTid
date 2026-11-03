using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Payroll;
using StatsTid.Tests.Regression.Segmentation;
using Xunit;
using static StatsTid.Tests.Regression.Leaver.LeaverHrOnly;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// One container for <see cref="LeaverHrOnlyTests"/>: the real <c>init.sql</c>, the REAL Payroll host over it, and a Backend factory
/// that KEEPS the leaver job (Step A). Every fact seeds BEFORE it boots its own pinned host, so the job's immediate first poll sees
/// the seeded people, and disposes that host in a <c>finally</c>, so a job can never poll again into a later fact's state.
/// </summary>
public sealed class StepAFixture : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private PayrollHostFactory _payrollBase = null!;

    public string ConnectionString { get; private set; } = null!;
    public StatsTidWebApplicationFactory BackendWithJob { get; private set; } = null!;
    internal FlexLedgerStubEngine Engine { get; } = new();
    public WebApplicationFactory<RetroactiveCorrectionService> PayrollHost { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        ConnectionString = _harness.ConnectionString;
        // The leaver job is KEPT (the default): these facts need the real Step A.
        BackendWithJob = new StatsTidWebApplicationFactory(ConnectionString);
        await TestFixtures.SeedWageTypeMappingsAsync(new DbConnectionFactory(ConnectionString));
        var (baseFactory, host) = BuildPayrollHost(ConnectionString, Engine);
        _payrollBase = baseFactory;
        PayrollHost = host;
    }

    public async Task DisposeAsync()
    {
        if (PayrollHost is not null)
            await PayrollHost.DisposeAsync();
        if (_payrollBase is not null)
            await _payrollBase.DisposeAsync();
        BackendWithJob?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }
}

/// <summary>
/// S147 / TASK-14713-T (AC-230-2; owner ruling OQ-2) — day 6 with the REAL automatic switch-off (Step A) in the loop.
///
/// <para><b>Plain language — Mette on 19 March.</b> Mette's last working day was Friday 13 March (D). Her manager had until
/// Wednesday 18 March (day 5) to approve, reject or reopen her March. On Thursday 19 March (day 6) the leaver job switches her
/// off, and from then only HR can finish her final month, and payroll's LocalAdmin can pay it. These three facts run the live job
/// against the seeded Mette (no hand-flipped flag) and then ask the three questions of AC-230-2: is her manager refused, does HR
/// get through, does payroll pay.</para>
///
/// <para><b>Where state comes from.</b> "Switched off" here is NOT seeded: Mette is seeded ACTIVE with her end date in the past and
/// the clock pinned to D+6, the host keeps <c>SettlementCloseService</c>, and each fact waits for the job's own flip before it asks
/// anything (the LeaverGraceTests precedent). The other classes of this task seed <c>is_active = FALSE</c> directly.</para>
///
/// <para><b>Expected values</b> come from the refinement (T5b, AC-230-2) and the owner's ruling, not from the code: manager 403, HR
/// 200, a LocalAdmin's export 200 with the flex-ledger record. The manager's refusal reason, and the approving HR's recorded
/// identity, are pinned too.</para>
///
/// <para>Docker-gated: <b>CI-pending</b> (first seen green at CI-W3b), never locally green.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class LeaverHrOnlyTests : IClassFixture<StepAFixture>
{
    private readonly StepAFixture _fx;

    public LeaverHrOnlyTests(StepAFixture fx) => _fx = fx;

    /// <summary>Boots a Backend host whose clock is <paramref name="today"/>, with the job KEPT. Seed first, call this second.</summary>
    private WebApplicationFactory<Program> BootJobHostAt(DateOnly today)
    {
        var host = _fx.BackendWithJob.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(today))));
        _ = host.CreateClient(); // builds the host and starts the hosted services: the job's immediate first poll
        return host;
    }

    private static async Task<bool> WaitForInactiveAsync(string cs, string employeeId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT is_active FROM users WHERE user_id = @id", conn);
            cmd.Parameters.AddWithValue("id", employeeId);
            if (!(bool)(await cmd.ExecuteScalarAsync())!) return true;
            await Task.Delay(250);
        }
        return false;
    }

    /// <summary>
    /// AC-230-2, the manager leg. Kind <b>F</b> on the reason text, <b>B</b> (boundary pin, holds today) on the status. On D+6 Step A has
    /// switched Mette off; her designated manager's approve answers 403 with the "Terminated-employee access requires LocalHR or above"
    /// reason, her March stays EMPLOYEE_APPROVED, nobody is recorded as approver, and no approval audit row or event is written. A
    /// day-5 sibling would still answer 200 (<c>LeaverGraceTests</c>), so this proves the manager's window closed on day 6.
    ///
    /// <para><b>Red conditions.</b> Today the manager is refused with the reason "Target employee not found" (the active-only
    /// validator answers that for every caller of a switched-off subject), so <c>Assert.Equal(BelowHrReason, reason)</c> fails. The
    /// 403 and the unchanged-state assertions hold today and guard the other direction: a widening that lets the manager's edge
    /// reach a switched-off employee would answer 200 and flip them. Mutation (argued; Docker-only): the manager's edge arm admitting a
    /// switched-off subject, or the org-scope arm ignoring the LocalHR floor, answers 200 and fails the status assertion.</para>
    /// </summary>
    [Fact]
    public async Task OnDay6_StepAHasSwitchedMetteOff_ManagerApprove_Is403_WithTheBelowHrReason_MonthUnchanged()
    {
        var cs = _fx.ConnectionString;
        var mette = await SeedLeaverAsync(cs, "ho_s6_mette", deactivated: false);
        var manager = await SeedManagerAsync(cs, "ho_s6_mgr", manages: mette);
        var periodId = await SeedPeriodAsync(cs, mette, "EMPLOYEE_APPROVED");

        using var host = BootJobHostAt(Day(6));
        Assert.True(await WaitForInactiveAsync(cs, mette), "Step A must switch Mette off on D+6 (the job's first poll).");

        using var client = WithBearer(host.CreateClient(), RoleToken(manager, StatsTidRoles.LocalLeader));
        var (_, reason) = await AssertForbiddenAsync(await ApproveAsync(client, periodId), "the manager's approve on D+6");

        Assert.Equal(BelowHrReason, reason);
        var row = await ReadPeriodAsync(cs, periodId);
        Assert.Equal("EMPLOYEE_APPROVED", row.Status);
        Assert.Null(row.ApprovedBy);
        Assert.Equal(0L, await CountApprovalAuditAsync(cs, periodId));
        Assert.Equal(0L, await CountApprovalEventsAsync(cs, mette, 3));
    }

    /// <summary>
    /// AC-230-2, the HR leg. Kind <b>F</b>. On D+6 (Step A has run) an in-scope LocalHR's approve of Mette's EMPLOYEE_APPROVED March
    /// answers 200 <c>APPROVED</c>; the month is APPROVED in the database with that HR user as the approver, and exactly one approval
    /// audit row and one approval event exist.
    ///
    /// <para><b>Red conditions.</b> Today the active-only validator answers "Target employee not found" to HR for a switched-off
    /// subject and the designated edge fails, so the approve answers 403: <c>Assert.Equal(HttpStatusCode.OK, ...)</c> fails (and the
    /// APPROVED, approver and audit assertions with it). Mutation (argued; Docker-only): swapping the approve gate back to the
    /// active-only <c>ValidateEmployeeAccessAsync</c>.</para>
    /// </summary>
    [Fact]
    public async Task OnDay6_StepAHasSwitchedMetteOff_HrApprove_Is200_ApprovedByHr()
    {
        var cs = _fx.ConnectionString;
        var mette = await SeedLeaverAsync(cs, "ho_s6hr_mette", deactivated: false);
        var hr = NewId("ho_s6hr_hr");
        var periodId = await SeedPeriodAsync(cs, mette, "EMPLOYEE_APPROVED");

        using var host = BootJobHostAt(Day(6));
        Assert.True(await WaitForInactiveAsync(cs, mette), "Step A must switch Mette off on D+6 (the job's first poll).");

        using var client = WithBearer(host.CreateClient(), RoleToken(hr, StatsTidRoles.LocalHR));
        var rsp = await ApproveAsync(client, periodId);

        await AssertStatusAsync(rsp, HttpStatusCode.OK, "HR's approve of Mette's March on D+6");
        using (var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync()))
            Assert.Equal("APPROVED", doc.RootElement.GetProperty("status").GetString());
        var row = await ReadPeriodAsync(cs, periodId);
        Assert.Equal("APPROVED", row.Status);
        Assert.Equal(hr, row.ApprovedBy);
        Assert.Equal(1L, await CountApprovalAuditAsync(cs, periodId));
        Assert.Equal(1L, await CountApprovalEventsAsync(cs, mette, 3));
    }

    /// <summary>
    /// AC-230-2, the payroll leg. Kind <b>F</b>. On D+6 (Step A has run) a LocalAdmin's <c>/calculate-and-export</c> of Mette's
    /// APPROVED March answers 200 and writes the flex-ledger record: opening 1.0 (February's recorded closing, CHAINED) and closing 2.5
    /// (the Rule Engine stand-in's configured March closing), the numbers the record holds and not merely a row's existence.
    ///
    /// <para><b>Seeding.</b> Mette is a continuous employee, so February must be APPROVED and recorded (closing 1.0) before March can
    /// chain; March is seeded APPROVED directly (the approval itself is the previous fact's), approved by someone who is neither the
    /// sender (none) nor the employee.</para>
    ///
    /// <para><b>Red conditions.</b> Today payroll's scope check is the active-only validator: a switched-off employee is "Target
    /// employee not found", so the export answers 403 and writes nothing: <c>Assert.Equal(HttpStatusCode.OK, ...)</c> and the record
    /// assertions fail. Mutation (argued; Docker-only): the scope check reverted to <c>ValidateEmployeeAccessAsync</c>.</para>
    /// </summary>
    [Fact]
    public async Task OnDay6_StepAHasSwitchedMetteOff_LocalAdminExport_Is200_WithTheLedgerRecord()
    {
        var cs = _fx.ConnectionString;
        var mette = await SeedLeaverAsync(cs, "ho_s6px_mette", deactivated: false);
        await SeedPriorMonthAsync(cs, mette);
        _fx.Engine.Configure(mette, new DateOnly(2026, 3, 1), closing: 2.5m);
        await SeedPeriodAsync(cs, mette, "APPROVED", approvedBy: "ho_s6px_hr");

        using var host = BootJobHostAt(Day(6));
        Assert.True(await WaitForInactiveAsync(cs, mette), "Step A must switch Mette off on D+6 (the job's first poll).");

        var (status, body) = await CalculateAndExportAsync(
            _fx.PayrollHost, LocalAdminToken(NewId("ho_s6px_admin")), mette, month: 3);

        Assert.True(status == HttpStatusCode.OK, $"LocalAdmin's export on D+6 must be 200, got {(int)status}: {body}");
        var record = await ReadExportRecordAsync(cs, mette, 3);
        Assert.NotNull(record);
        Assert.Equal(1.0m, record!.Opening);
        Assert.Equal(2.5m, record.Closing);
        Assert.Equal("CHAINED", record.Provenance);
    }
}
