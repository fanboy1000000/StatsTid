using System.Net;
using System.Text.Json;
using Xunit;
using static StatsTid.Tests.Regression.Leaver.LeaverHrOnly;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// S147 / TASK-14713-T (AC-230-13, a characterization pin; R-30 / TASK-14703) — correcting a leaver's already-paid month stays possible.
///
/// <para><b>Plain language.</b> After a switched-off leaver's final month has been paid, payroll may later have to correct it (a late
/// timesheet fix, a rule change). TASK-14703 added a guard so that a departed person cannot correct THEIR OWN pay, and the question the
/// refinement asked is whether that guard also locks everybody else out of a leaver's month. It does not: the guard compares the actor's
/// identity with the employee's, so another administrator is untouched. This fact pins that, so a future change to the guard cannot
/// quietly stop payroll from correcting leavers.</para>
///
/// <para>Kind <b>R</b>: the behaviour holds today (the route is GlobalAdminOnly and its only departed-subject check is the identity clause).
/// Docker-gated: <b>CI-pending</b>, never locally green.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("LeaverHrOnly")]
public sealed class LeaverRecalculateGuardTests : LeaverHrOnlyTestBase
{
    public LeaverRecalculateGuardTests(LeaverHrOnlyFixture fx) : base(fx) { }

    /// <summary>
    /// AC-230-13. Kind <b>R</b> — regression pin, holds today. A switched-off leaver's March has been exported (opening 1.0, closing 2.5). A
    /// GlobalAdmin who is NOT the leaver posts <c>/recalculate</c> for that month with the stand-in now closing at 4.0: 200, the response
    /// reports the closing before (2.5) and after (4.0), and the stored record's closing is 4.0. The identity guard (TASK-14703) did not
    /// refuse another person's correction.
    ///
    /// <para><b>Red conditions (argued).</b> It holds today, so it is not F. It fails if the guard is widened from "the actor is the departed
    /// employee" to "the subject is departed": the other GlobalAdmin would get 403 and <c>Assert.True(status == OK)</c> would fail
    /// (mutation: the guard's identity test dropped, i.e. refusing whenever the subject is departed).</para>
    /// </summary>
    [Fact]
    public async Task AnotherGlobalAdmin_CanRecalculate_ASwitchedOffLeaversExportedMonth()
    {
        var leaver = await SeedLeaverAsync(Cs, "rg_mette", deactivated: false);
        var manager = await SeedManagerAsync(Cs, "rg_mgr", manages: leaver);
        await PrepareExportableMarchAsync(leaver, marchClosing: 2.5m);
        var periodId = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED");
        using (var mgr = LeaderAt(Day(3), manager))
            await AssertStatusAsync(await ApproveAsync(mgr, periodId), HttpStatusCode.OK, "the manager's approve on D+3");
        var export = await ExportAsync(LocalAdminToken(NewId("rg_admin")), leaver);
        Assert.True(export.Status == HttpStatusCode.OK, $"precondition: the month must be exported, got {(int)export.Status}: {export.Body}");
        await DeactivateAsync(Cs, leaver); // day 6: switched off AFTER the export, so the setup holds on code that predates the widening

        Fx.Engine.Configure(leaver, new DateOnly(2026, 3, 1), closing: 4.0m);
        var (status, body) = await RecalculateAsync(
            Fx.PayrollHost, GlobalAdminToken(NewId("rg_global")), leaver, month: 3);

        Assert.True(status == HttpStatusCode.OK, $"another GlobalAdmin's /recalculate of a leaver's exported month must be 200, got {(int)status}: {body}");
        using (var doc = JsonDocument.Parse(body))
        {
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(2.5m, doc.RootElement.GetProperty("closingFlexBalanceBefore").GetDecimal());
            Assert.Equal(4.0m, doc.RootElement.GetProperty("closingFlexBalanceAfter").GetDecimal());
        }
        var record = await ReadExportRecordAsync(Cs, leaver, 3);
        Assert.NotNull(record);
        Assert.Equal(4.0m, record!.Closing);
    }
}
