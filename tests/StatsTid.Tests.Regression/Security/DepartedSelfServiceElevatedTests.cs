using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Tests.Regression.Approval;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S147 / TASK-14703-T (AC-230-5, the LocalHR self-service bullet; R-30, R-32) — a departed person who is
/// HR-tier reaches their OWN month through the SELF-SERVICE screens (Skema save, time entry, send,
/// reopen), and through the leader arm of reopen. All four routes ask the shared organisation-scope
/// check "may this actor act on this employee?" through its EMPLOYEE overloads
/// (<c>SkemaEndpoints.cs:658</c>, <c>TimeEndpoints.cs:87</c>, <c>ApprovalEndpoints.cs:1986</c>,
/// <c>:1658</c>), so the clause inside the validator closes them with no handler change.
///
/// <para><b>Plain language.</b> Mette used to be HR. She left on her last day (13 March 2026) but her
/// account is still active (the grace) and her token is still valid. She must not be able to save, register
/// or send HER OWN March, nor undo her own approved month, even though she is HR; the same token still works
/// for any other employee in scope.</para>
///
/// <para><b>Kind F</b> for every refused leg (today the HR self is admitted by the validator, so each of the
/// four calls succeeds); the "other employee" legs are <b>B</b> (boundary pin, holds today). Each refusal
/// asserts the date-free <see cref="DepartedSubject.DenyReason"/> (the CLAUSE), not the HR gate's or the
/// SEC-009 guard's reason. <i>The refinement's R-32 had the reopen fact RED by mutation, because the clause
/// and the grace were one task; split, it is a plain F here.</i></para>
///
/// <para>Shares the S127 send-command fixture (real <c>init.sql</c>, one booted API) through the
/// <c>SendCommandMatrix</c> collection; the departed state is a FIXED past end date (2026-03-13), so it holds
/// for any real calendar day after it. <b>Docker</b> — CI-pending.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendCommandMatrix")]
public sealed class DepartedSelfServiceElevatedTests : SendCommandMatrixTestBase
{
    /// <summary>The leaver's last day employed: Friday 2026-03-13.</summary>
    private static readonly DateOnly LastDay = new(2026, 3, 13);

    public DepartedSelfServiceElevatedTests(SendCommandMatrixFixture fx) : base(fx) { }

    private static async Task AssertClauseRefusalAsync(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(DepartedSubject.DenyReason, doc.RootElement.GetProperty("reason").GetString());
    }

    /// <summary>
    /// Kind F (three legs) + B (three "other employee" legs). Mette as LocalHR, her own March: Skema save,
    /// time entry and send each answer 403 with the clause's reason and write nothing; the SAME token for
    /// another employee in scope answers 200 / 201 / 200. Red conditions (today): the Skema save answers 200
    /// <c>{saved:1}</c>, the time entry 201 and the send 200 EMPLOYEE_APPROVED, because the validator's
    /// employee overloads admit an HR self; each <c>Assert 403</c> fails.
    /// </summary>
    [Fact]
    public async Task LocalHr_OwnSkemaSave_TimeEntry_Send_403_OtherEmployee_200()
    {
        const string mette = "ds_svc_mette";
        const string otherAc = "ds_svc_other_ac";
        const string otherHk = "ds_svc_other_hk";
        await SeedEmployeeAsync(mette, Org, "HK");
        await SetEmploymentWindowAsync(mette, start: null, end: LastDay);   // active, past her last day
        await CoverMonthWithAbsencesAsync(mette, gap: null, to: LastDay);
        await SeedEmployeeAsync(otherAc, Org, "AC");
        await SeedEmployeeAsync(otherHk, Org, "HK");
        await CoverMonthWithAbsencesAsync(otherHk);
        using var client = ClientForRole(mette, StatsTidRoles.LocalHR, Org);

        // her own Skema save
        var skema = await client.PostAsJsonAsync($"/api/skema/{mette}/save", new
        {
            year = MarchYear, month = MarchMonth,
            entries = new[] { new { date = new DateOnly(2026, 3, 10), projectCode = "PROJ-S147", hours = 7.4m } },
        });
        await AssertClauseRefusalAsync(skema);
        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id=@e", ("e", mette)));

        // her own time entry
        var entry = await client.PostAsJsonAsync("/api/time-entries", new
        {
            employeeId = mette, date = new DateOnly(2026, 3, 10), hours = 7.4m,
            taskId = (string?)null, activityType = "NORMAL", agreementCode = "HK",
        });
        await AssertClauseRefusalAsync(entry);
        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id=@e", ("e", mette)));

        // her own send
        var send = await PostSendAsync(client, mette);
        await AssertClauseRefusalAsync(send);
        Assert.False(await RowExistsAsync(mette, MarchStart, MarchEnd), "a refused send must create no approval period");
        Assert.Equal(0L, await CountOutboxTotalAsync(StreamId(mette)));

        // the same token for ANOTHER employee in scope (boundary pins, hold today)
        var skemaOther = await client.PostAsJsonAsync($"/api/skema/{otherAc}/save", new
        {
            year = MarchYear, month = MarchMonth,
            entries = new[] { new { date = new DateOnly(2026, 3, 10), projectCode = "PROJ-S147", hours = 7.4m } },
        });
        Assert.Equal(HttpStatusCode.OK, skemaOther.StatusCode);

        var entryOther = await client.PostAsJsonAsync("/api/time-entries", new
        {
            employeeId = otherAc, date = new DateOnly(2026, 3, 11), hours = 7.4m,
            taskId = (string?)null, activityType = "NORMAL", agreementCode = "AC",
        });
        Assert.Equal(HttpStatusCode.Created, entryOther.StatusCode);

        var sendOther = await PostSendAsync(client, otherHk);
        await AssertOkEmployeeApprovedAsync(sendOther);
    }

    /// <summary>
    /// Kind F. Mette as LocalHR reopens HER OWN EMPLOYEE_APPROVED month through the LEADER arm (an HR-tier
    /// actor is never the exact "Employee" role, so the leader arm is where she lands): 403 with the CLAUSE's
    /// reason, the period unchanged. The SEC-009 self-decision guard does not fire for this source state
    /// (it covers APPROVED only), so the validator clause is the only thing that refuses it, which is what this
    /// fact pins. Red conditions: <c>Assert 403</c> and the unchanged status — today the active-only validator
    /// admits the covered HR self and the month is reopened to DRAFT (200).
    /// </summary>
    [Fact]
    public async Task LocalHr_LeaderArmReopenOwnMonth_403()
    {
        const string mette = "ds_svc_reopen";
        await SeedEmployeeAsync(mette, Org, "HK");
        await SetEmploymentWindowAsync(mette, start: null, end: LastDay);
        var periodId = await SeedApprovalRowAsync(mette, "EMPLOYEE_APPROVED", MarchStart, MarchEnd);
        using var client = ClientForRole(mette, StatsTidRoles.LocalHR, Org);

        var rsp = await PostReopenAsync(client, periodId, "s147 pin: reopen own month");

        await AssertClauseRefusalAsync(rsp);
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadRowAsync(periodId))!.Status);
        Assert.Equal(0L, await CountOutboxTotalAsync(StreamId(mette)));
    }
}
