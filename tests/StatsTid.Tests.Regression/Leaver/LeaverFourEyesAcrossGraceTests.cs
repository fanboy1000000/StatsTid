using System.Net;
using Xunit;
using static StatsTid.Tests.Regression.Leaver.LeaverHrOnly;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// S147 / TASK-14713-T (AC-230-11; owner ruling OQ-1 = (a), strict four-eyes, with OQ-2's grace) — the second signature, before and after day 6.
///
/// <para><b>Plain language.</b> A month is signed twice: sent, then approved, by two different people. While the leaver is inside the 5-day grace
/// her manager is the natural second signer, so HR can send the month on day 2 and the manager approves it on day 3. Once the manager's window has
/// closed (day 6) only HR can approve, and if HR sent the month, a DIFFERENT HR person must approve it: the sender is still barred, even though
/// HR is now the only door. These facts pin both halves, so the day-6 widening cannot be read as an exception to four-eyes.</para>
///
/// <para><b>Where state comes from.</b> The grace half runs on hosts pinned to D+2 (Sunday 15 March) and D+3 (Monday 16 March) with the leaver
/// seeded ACTIVE and past her end date (the grace state); the day-6 half seeds her switched off on a host pinned to D+6. No leaver job runs.
/// Docker-gated: <b>CI-pending</b>, never locally green.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("LeaverHrOnly")]
public sealed class LeaverFourEyesAcrossGraceTests : LeaverHrOnlyTestBase
{
    public LeaverFourEyesAcrossGraceTests(LeaverHrOnlyFixture fx) : base(fx) { }

    /// <summary>
    /// AC-230-11, within the grace. Kind <b>R</b> — regression pin, holds today (the grace and the four-eyes guard are both merged). HR sends Mette's
    /// March on D+2 (200, EMPLOYEE_APPROVED, HR the sender); her manager, a different person, approves it on D+3: 200, APPROVED, the manager
    /// recorded. The manager is the second signer; nobody is barred.
    ///
    /// <para><b>Red conditions (argued).</b> Holds today. It fails if the four-eyes guard is widened to bar anyone but the sender (the manager's
    /// approve would answer 403), or if the grace were closed early.</para>
    /// </summary>
    [Fact]
    public async Task InGrace_HrSendsOnDay2_ManagerApprovesOnDay3_200()
    {
        var mette = await SeedLeaverAsync(Cs, "fe_grace", deactivated: false);
        var manager = await SeedManagerAsync(Cs, "fe_mgr", manages: mette);
        await CoverWeekdaysAsync(Cs, mette, new DateOnly(2026, 3, 2), LastDay);
        var hr = NewId("fe_hr");

        using (var hrClient = HrAt(Day(2), hr))
            await AssertSendOkAsync(await SendAsync(hrClient, mette), "HR's send on D+2");
        var periodId = await FindPeriodIdAsync(Cs, mette, 3);
        Assert.Equal(hr, (await ReadPeriodAsync(Cs, periodId)).EmployeeApprovedBy);

        using var mgrClient = LeaderAt(Day(3), manager);
        await AssertStatusAsync(await ApproveAsync(mgrClient, periodId), HttpStatusCode.OK, "the manager's approve on D+3");
        var row = await ReadPeriodAsync(Cs, periodId);
        Assert.Equal("APPROVED", row.Status);
        Assert.Equal(manager, row.ApprovedBy);
    }

    /// <summary>
    /// AC-230-11, after the grace. Kind <b>F</b> (HR-B's 200; HR-A's two refusals are boundary pins that hold today because the four-eyes guard runs
    /// before the access gate). On D+6 Mette is switched off and has a manager who can no longer act. HR-A sends March (200). HR-A's approve is 403
    /// and HR-A's reject is 403, each with exactly the four-eyes reason, and the month does not move. HR-B's approve is 200 (APPROVED, HR-B recorded).
    ///
    /// <para><b>Red conditions.</b> Today HR-B's approve is 403 ("Target employee not found"): <c>AssertStatusAsync(OK)</c> and the APPROVED assertion
    /// fail. Mutation (argued, Docker-only): the four-eyes guard skipped for a switched-off subject answers HR-A's approve 200 and fails
    /// <c>AssertFourEyesRefusalAsync</c>.</para>
    /// </summary>
    [Fact]
    public async Task AfterGrace_HrASends_HrAApproveAndReject403_HrBApproves200()
    {
        var mette = await SeedLeaverAsync(Cs, "fe_day6");
        await SeedManagerAsync(Cs, "fe_mgr6", manages: mette);
        await CoverWeekdaysAsync(Cs, mette, new DateOnly(2026, 3, 2), LastDay);
        var hrA = NewId("fe_hr_a");
        var hrB = NewId("fe_hr_b");

        using var clientA = HrAt(Day(6), hrA);
        await AssertSendOkAsync(await SendAsync(clientA, mette), "HR-A's send on D+6");
        var periodId = await FindPeriodIdAsync(Cs, mette, 3);

        await AssertFourEyesRefusalAsync(await ApproveAsync(clientA, periodId), "the sender's approve on D+6");
        await AssertFourEyesRefusalAsync(await RejectAsync(clientA, periodId), "the sender's reject on D+6");
        var afterRefusals = await ReadPeriodAsync(Cs, periodId);
        Assert.Equal("EMPLOYEE_APPROVED", afterRefusals.Status);
        Assert.Null(afterRefusals.ApprovedBy);
        Assert.Null(afterRefusals.RejectionReason);

        using var clientB = HrAt(Day(6), hrB);
        await AssertStatusAsync(await ApproveAsync(clientB, periodId), HttpStatusCode.OK, "the second HR person's approve on D+6");
        var approved = await ReadPeriodAsync(Cs, periodId);
        Assert.Equal("APPROVED", approved.Status);
        Assert.Equal(hrB, approved.ApprovedBy);
    }
}
