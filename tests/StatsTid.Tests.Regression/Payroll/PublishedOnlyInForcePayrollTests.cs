using System.Net;
using StatsTid.Tests.Regression.Contracts;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14828-T (AC-C2m, pay half) - payroll never pays on a draft.
///
/// <para>
/// <b>In plain language.</b> An administrator drafts a different HK configuration dated Monday 4 May 2026 and then
/// throws the draft away without ever publishing it. May 2026 must still be paid on the published version (37 hours).
/// Payroll reads only what was published and is in force.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PublishedOnlyInForcePayrollTests : DatedPayrollFixture
{
    private const string OrgId = "STY01";
    private static readonly DateOnly Clock = new(2026, 11, 10);
    private static readonly DateOnly Apr27 = new(2026, 4, 27);
    private static readonly DateOnly Apr30 = new(2026, 4, 30);
    private static readonly DateOnly Apr01 = new(2026, 4, 1);
    private static readonly DateOnly May01 = new(2026, 5, 1);
    private static readonly DateOnly May31 = new(2026, 5, 31);

    /// <summary>
    /// <b>Kind N (CI; mutation M-9 at CI-M5).</b> AC-C2m, pay half. An HK/OK26 DRAFT with a weekly norm of 32 h is
    /// dated Monday 4 May 2026 and archived without being published. An HK full-timer with 7.4 h on every weekday from
    /// 27 April (May's opening week reaches April) to 31 May, April and May APPROVED. <c>calculate-and-export</c> May 2026
    /// answers 200 and the norm figure is <b>155.40</b> h: May's 21 weekdays at the published 37 h (7.40), which is the
    /// seeded version, not the draft's 6.40.
    ///
    /// <para>
    /// <b>Red conditions</b> (argued; Docker; run at CI-M5, never locally green). N: on the code before the stack the fact
    /// passes for another reason (pay read the compiled table, which is also 37 h). <b>M-9</b> (the in-force read and the
    /// predecessor rule select by status alone, without <c>published_at IS NOT NULL</c>) makes pay read the archived draft
    /// dated 4 May: the norm total is 21 weekdays at 6.40 = 134.40 and <c>Assert.Equal(155.40m, ...)</c> fails. Other facts
    /// expected red under M-9: <c>PublishedOnlyInForceTests.ArchivedDraft_NeverInForce_NeverPredecessor</c> (the read half), and
    /// any other fact that publishes after an archived draft.
    /// </para>
    /// </summary>
    [Fact]
    public async Task May2026PaysPublishedVersion_NotTheDraft()
    {
        using var admin = AdminAt(Clock);
        const string employeeId = "EMP-S148-PAY-DRAFT";
        var (draft, draftVersion) = await CreateDraftAsync(admin, HkBody(32m));
        await ExecAsync("UPDATE agreement_configs SET applies_from = DATE '2026-05-04' WHERE config_id = @id", ("id", draft));
        using (var archive = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
                   HttpMethod.Post, $"/api/agreement-configs/{draft}/archive", jsonBody: null, ifMatchVersion: draftVersion)))
            Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        Assert.Equal("ARCHIVED", await StatusOfConfigAsync(draft));

        await SeedEmployeeAsync(employeeId, OrgId, "HK");
        await SeedWeekdayHoursAsync(employeeId, "HK", Ok26, Apr27, May31);
        await SeedApprovedMonthAsync(employeeId, OrgId, "HK", Ok26, Apr01, Apr30);
        await SeedApprovedMonthAsync(employeeId, OrgId, "HK", Ok26, May01, May31);

        var response = await CalculateAndExportAsync(employeeId, "HK", Ok26, OrgId, May01, May31);

        AssertOk(response);
        Assert.Equal(155.40m, NormTotal(ReadResult(response.Json)));
    }
}
