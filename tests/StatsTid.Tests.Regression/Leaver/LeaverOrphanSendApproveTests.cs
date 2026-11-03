using System.Net;
using Xunit;
using static StatsTid.Tests.Regression.Leaver.LeaverHrOnly;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// S147 / TASK-14713-T (AC-230-10, the server half; owner ruling OQ-1 = (a) four-eyes, OQ-2) — a leaver with nobody to approve for her.
///
/// <para><b>Plain language.</b> Some leavers have no active manager at all (the roster calls them orphans). From day 6 their final month can
/// only be finished by HR, and HR then has to do BOTH halves: send the month on the leaver's behalf (nobody else can), and approve it. The
/// four-eyes rule (TASK-14704) says the person who sent a month may not also approve it, so a SECOND HR person must approve. These facts walk
/// the whole path through the real routes: HR-A sends; HR-A's own approve is refused with the four-eyes reason; HR-B's approve works; payroll's
/// LocalAdmin pays the month. (The screen path is TASK-14714's.)</para>
///
/// <para><b>Where state comes from.</b> The leaver is seeded switched off (day 6) on a Backend host with no leaver job and a pinned clock; she has
/// no reporting line, so there is no edge to fall back on. Docker-gated: <b>CI-pending</b>, never locally green.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("LeaverHrOnly")]
public sealed class LeaverOrphanSendApproveTests : LeaverHrOnlyTestBase
{
    public LeaverOrphanSendApproveTests(LeaverHrOnlyFixture fx) : base(fx) { }

    /// <summary>
    /// AC-230-10. Kind <b>F</b> (the HR-B approve and the export; the HR-A refusal is a boundary pin that holds today because the four-eyes guard
    /// runs before the access gate). On D+6 a switched-off leaver with no approver and a fully covered employed span: HR-A sends March (200,
    /// EMPLOYEE_APPROVED, HR-A recorded as the sender); HR-A's approve is 403 with exactly the four-eyes reason and the month does not move;
    /// HR-B's approve is 200 (APPROVED, HR-B recorded); a LocalAdmin's export of the month is 200 and writes the ledger record (opening 1.0,
    /// closing 2.5).
    ///
    /// <para><b>Red conditions.</b> Today HR-B's approve is 403 ("Target employee not found" from the active-only validator, and no edge exists):
    /// <c>AssertStatusAsync(OK)</c> fails, and so do the APPROVED and export assertions. Mutation (argued, Docker-only): the gate swapped back to
    /// the active-only validator.</para>
    /// </summary>
    [Fact]
    public async Task OrphanLeaver_HrASends_HrAApprove403FourEyes_HrBApproves200_MonthExports()
    {
        var leaver = await SeedLeaverAsync(Cs, "os_orphan");
        await CoverWeekdaysAsync(Cs, leaver, new DateOnly(2026, 3, 2), LastDay);
        await PrepareExportableMarchAsync(leaver, marchClosing: 2.5m);
        var hrA = NewId("os_hr_a");
        var hrB = NewId("os_hr_b");

        using var clientA = HrAt(Day(6), hrA);
        await AssertSendOkAsync(await SendAsync(clientA, leaver), "HR-A's send of the orphan leaver's March on D+6");
        var periodId = await FindPeriodIdAsync(Cs, leaver, 3);
        Assert.Equal(hrA, (await ReadPeriodAsync(Cs, periodId)).EmployeeApprovedBy);

        await AssertFourEyesRefusalAsync(await ApproveAsync(clientA, periodId), "the sender's own approve");
        var afterRefusal = await ReadPeriodAsync(Cs, periodId);
        Assert.Equal("EMPLOYEE_APPROVED", afterRefusal.Status);
        Assert.Null(afterRefusal.ApprovedBy);

        using var clientB = HrAt(Day(6), hrB);
        await AssertStatusAsync(await ApproveAsync(clientB, periodId), HttpStatusCode.OK, "the second HR person's approve");
        var approved = await ReadPeriodAsync(Cs, periodId);
        Assert.Equal("APPROVED", approved.Status);
        Assert.Equal(hrB, approved.ApprovedBy);

        var (status, body) = await ExportAsync(LocalAdminToken(NewId("os_admin")), leaver);
        Assert.True(status == HttpStatusCode.OK, $"the orphan leaver's approved month must export, got {(int)status}: {body}");
        var record = await ReadExportRecordAsync(Cs, leaver, 3);
        Assert.NotNull(record);
        Assert.Equal(1.0m, record!.Opening);
        Assert.Equal(2.5m, record.Closing);
        Assert.Equal("CHAINED", record.Provenance);
    }
}
