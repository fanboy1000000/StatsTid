using System.Net;
using Xunit;
using static StatsTid.Tests.Regression.Leaver.LeaverHrOnly;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// S147 / TASK-14713-T (AC-230-8; owner ruling OQ-2) — a month approved inside the grace is still paid after day 6.
///
/// <para><b>Plain language.</b> Mette's manager approves her March on day 3 (Monday 16 March), while she is still switched on. On day 6 the
/// leaver job switches her off. Before TASK-14713 the person in payroll who then tried to pay that already-approved month was told "target
/// employee not found", because payroll looked only at active people. These facts prove the approval survives the switch-off and payroll can
/// pay it.</para>
///
/// <para><b>Where state comes from.</b> The approval is made through the real route on a Backend host pinned to D+3; the day-6 switch-off is
/// seeded directly (<c>is_active = FALSE</c>) because the fixture's Backend host has no leaver job, and the export runs on the real Payroll
/// host. (The live Step A in the loop is the job of <see cref="LeaverHrOnlyTests"/>.) Docker-gated: <b>CI-pending</b>, never locally
/// green.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("LeaverHrOnly")]
public sealed class LeaverGraceExportTests : LeaverHrOnlyTestBase
{
    public LeaverGraceExportTests(LeaverHrOnlyFixture fx) : base(fx) { }

    /// <summary>
    /// AC-230-8. Kind <b>F</b>. The manager approves Mette's EMPLOYEE_APPROVED March on D+3 (200, APPROVED, the manager recorded). Mette is then
    /// switched off (day 6, seeded). A LocalAdmin's <c>/calculate-and-export</c> of that March answers 200 and writes the flex-ledger record
    /// with the numbers the Rule Engine stand-in was configured for: opening 1.0 (February's closing, CHAINED), closing 2.5. The approval is
    /// untouched by the switch-off.
    ///
    /// <para><b>Red conditions.</b> Today payroll's scope check is the active-only validator: once Mette is switched off it answers "Target
    /// employee not found" (403) and writes nothing, so <c>Assert.True(status == OK)</c> and the record assertions fail. The D+3 approval 200
    /// holds today (the grace). Mutation (argued, Docker-only): the scope check reverted to <c>ValidateEmployeeAccessAsync</c>.</para>
    /// </summary>
    [Fact]
    public async Task MonthApprovedByTheManagerInTheGrace_IsExportedAfterTheDay6SwitchOff()
    {
        var leaver = await SeedLeaverAsync(Cs, "ge_mette", deactivated: false);
        var manager = await SeedManagerAsync(Cs, "ge_mgr", manages: leaver);
        await PrepareExportableMarchAsync(leaver, marchClosing: 2.5m);
        var periodId = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED");

        using (var mgr = LeaderAt(Day(3), manager))
            await AssertStatusAsync(await ApproveAsync(mgr, periodId), HttpStatusCode.OK, "the manager's approve on D+3 (the grace)");
        var approved = await ReadPeriodAsync(Cs, periodId);
        Assert.Equal("APPROVED", approved.Status);
        Assert.Equal(manager, approved.ApprovedBy);

        await DeactivateAsync(Cs, leaver); // day 6: Step A's flip, seeded directly

        var (status, body) = await ExportAsync(LocalAdminToken(NewId("ge_admin")), leaver);

        Assert.True(status == HttpStatusCode.OK, $"the export of a month approved in the grace must be 200 after the switch-off, got {(int)status}: {body}");
        var record = await ReadExportRecordAsync(Cs, leaver, 3);
        Assert.NotNull(record);
        Assert.Equal(1.0m, record!.Opening);
        Assert.Equal(2.5m, record.Closing);
        Assert.Equal("CHAINED", record.Provenance);
        var after = await ReadPeriodAsync(Cs, periodId);
        Assert.Equal("APPROVED", after.Status);
        Assert.Equal(manager, after.ApprovedBy);
    }
}
