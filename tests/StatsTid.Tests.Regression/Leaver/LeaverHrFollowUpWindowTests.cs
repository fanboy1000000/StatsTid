using System.Net;
using System.Text.Json;
using StatsTid.SharedKernel.Security;
using Xunit;
using static StatsTid.Tests.Regression.Leaver.LeaverHrOnly;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// S147 / TASK-14713-T (AC-230-9, the server half; owner ruling OQ-2; refinement R-27) — what HR sees on day 6: the leaver's month opens, and
/// the leaver's row in HR's follow-up list says when the manager's window closed.
///
/// <para><b>Plain language.</b> HR's "leavers' final month" list (HRP-011) is where HR learns that someone has left and their last month is
/// not yet approved. From day 6 HR is the only one who can finish that month, so the row now carries the hand-over in plain data: the last day
/// the manager could act (day 5, "18 March") and the first day only HR can act (day 6, "19 March"). The screen turns that into "manager until
/// 18 March, HR only from 19 March" (TASK-14714). The row's aging clock does NOT change: it is still the month's manager deadline. HR can also
/// open the leaver's Skema month on day 6, which was refused before. The dates appear on this HR-only list and nowhere else (ADR-040 D7).</para>
///
/// <para><b>Dates.</b> Expected values come from the owner's timeline, not the code: end date D, last manager day D+5, first HR-only day D+6.
/// Three leavers with different last days (13 March, 6 March, 27 February) prove the window is derived from each person's own end date, across a
/// month boundary, and is not a constant. Host clock pinned to D+6 for Mette (Thursday 19 March 2026). Docker-gated: <b>CI-pending</b>,
/// never locally green.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("LeaverHrOnly")]
public sealed class LeaverHrFollowUpWindowTests : LeaverHrOnlyTestBase
{
    public LeaverHrFollowUpWindowTests(LeaverHrOnlyFixture fx) : base(fx) { }

    private static JsonElement FindItem(JsonElement items, string employeeId, int month)
    {
        foreach (var item in items.EnumerateArray())
            if (item.GetProperty("employeeId").GetString() == employeeId && item.GetProperty("month").GetInt32() == month)
                return item;
        throw new Xunit.Sdk.XunitException($"no item for {employeeId} month {month} in the list");
    }

    private async Task<JsonDocument> ReadListAsync(HttpClient hr, string path)
    {
        var rsp = await hr.GetAsync(path);
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"GET {path}: expected 200, got {(int)rsp.StatusCode}: {raw}");
        return JsonDocument.Parse(raw);
    }

    /// <summary>
    /// AC-230-9, the month view. Kind <b>F</b>. On D+6 an in-scope LocalHR opens the switched-off Mette's Skema March: 200, her March, the 31 days,
    /// and the ten absences she was registered for (the view served her data, not an empty shell). A switched-off person reading her OWN month is
    /// still refused with 404 and the body carries none of her data: the widening is HR-only. (That second half is
    /// a boundary pin, holds today.)
    ///
    /// <para><b>Red conditions.</b> Today HR falls into the leader tier for a switched-off employee and is refused 403: <c>Assert.True(status == OK)</c>
    /// fails. Mutation (argued, Docker-only): the shared <c>IsLeaderTierReadAsync</c> used in place of the terminated-inclusive tier check.</para>
    /// </summary>
    [Fact]
    public async Task OnDay6_HrOpensMettesSkemaMonth_200_AndMetteHerselfIsStillRefused()
    {
        var mette = await SeedLeaverAsync(Cs, "fu_skema");
        await CoverWeekdaysAsync(Cs, mette, new DateOnly(2026, 3, 2), LastDay);
        await SeedPeriodAsync(Cs, mette, "EMPLOYEE_APPROVED");

        using (var hr = HrAt(Day(6), NewId("fu_hr")))
        {
            var rsp = await SkemaMonthAsync(hr, mette, 3);
            var raw = await rsp.Content.ReadAsStringAsync();
            Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"HR's Skema month of Mette on D+6 must be 200, got {(int)rsp.StatusCode}: {raw}");
            using var doc = JsonDocument.Parse(raw);
            Assert.Equal(2026, doc.RootElement.GetProperty("year").GetInt32());
            Assert.Equal(3, doc.RootElement.GetProperty("month").GetInt32());
            Assert.Equal(31, doc.RootElement.GetProperty("daysInMonth").GetInt32());
            Assert.Equal(10, doc.RootElement.GetProperty("absences").GetArrayLength());
        }

        using var self = ClientAt(Day(6), RoleToken(mette, StatsTidRoles.Employee));
        var own = await SkemaMonthAsync(self, mette, 3);
        var ownRaw = await own.Content.ReadAsStringAsync();
        Assert.True(own.StatusCode == HttpStatusCode.NotFound,
            $"a switched-off person reading her own month must get 404 (SkemaEndpoints.cs:263-264), got {(int)own.StatusCode}: {ownRaw}");
        Assert.DoesNotContain("absences", ownRaw);
    }

    /// <summary>
    /// AC-230-9, the row. Kind <b>F</b>. On D+6 HR's leaver list (HRP-011) carries, per leaver, <c>lastManagerDay</c> = last day + 5 and
    /// <c>firstHrOnlyDay</c> = last day + 6: Mette (last day 13 March, EMPLOYEE_APPROVED March with a STORED manager deadline of 8 April)
    /// shows 2026-03-18 and 2026-03-19; a second leaver (last day 6 March, March never sent) shows 2026-03-11 and 2026-03-12; a third (last day
    /// 27 February, February never sent) shows 2026-03-04 and 2026-03-05, across the month boundary. The aging anchor is UNCHANGED: Mette's
    /// is her stored manager deadline (2026-04-08, source "stored"); the never-sent months' is month-end + 5 days (2026-04-05 and 2026-03-05, source
    /// "computed"), and a never-sent month has no period id.
    ///
    /// <para><b>Red conditions.</b> Today the row has no window members: <c>GetProperty("lastManagerDay")</c> throws on the first item. Mutation
    /// (argued, Docker-only): deriving the window from a constant, or from the month end, fails the second and third leavers; deriving the anchor
    /// from the window fails the anchor assertions.</para>
    /// </summary>
    [Fact]
    public async Task OnDay6_HrLeaverList_RowsCarryTheManagersWindow_AnchorUnchanged()
    {
        var mette = await SeedLeaverAsync(Cs, "fu_row_a", end: new DateOnly(2026, 3, 13));
        var metteMonth = await SeedPeriodAsync(Cs, mette, "EMPLOYEE_APPROVED", managerDeadline: new DateOnly(2026, 4, 8));
        var second = await SeedLeaverAsync(Cs, "fu_row_b", end: new DateOnly(2026, 3, 6));
        var third = await SeedLeaverAsync(Cs, "fu_row_c", end: new DateOnly(2026, 2, 27));

        using var hr = HrAt(Day(6), NewId("fu_hr"));
        using var doc = await ReadListAsync(hr, "/api/hr/follow-up/leaver-final-month");
        var items = doc.RootElement.GetProperty("items");

        var a = FindItem(items, mette, 3);
        Assert.Equal("EMPLOYEE_APPROVED", a.GetProperty("periodStatus").GetString());
        Assert.Equal(metteMonth, a.GetProperty("periodId").GetGuid());
        Assert.Equal("2026-03-18", a.GetProperty("lastManagerDay").GetString());
        Assert.Equal("2026-03-19", a.GetProperty("firstHrOnlyDay").GetString());
        Assert.Equal("2026-04-08", a.GetProperty("ageAnchor").GetString());
        Assert.Equal("stored", a.GetProperty("deadlineSource").GetString());

        var b = FindItem(items, second, 3);
        Assert.Equal("NONE", b.GetProperty("periodStatus").GetString());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("periodId").ValueKind);
        Assert.Equal("2026-03-11", b.GetProperty("lastManagerDay").GetString());
        Assert.Equal("2026-03-12", b.GetProperty("firstHrOnlyDay").GetString());
        Assert.Equal("2026-04-05", b.GetProperty("ageAnchor").GetString());
        Assert.Equal("computed", b.GetProperty("deadlineSource").GetString());

        var c = FindItem(items, third, 2);
        Assert.Equal("NONE", c.GetProperty("periodStatus").GetString());
        Assert.Equal("2026-03-04", c.GetProperty("lastManagerDay").GetString());
        Assert.Equal("2026-03-05", c.GetProperty("firstHrOnlyDay").GetString());
        Assert.Equal("2026-03-05", c.GetProperty("ageAnchor").GetString());
        Assert.Equal("computed", c.GetProperty("deadlineSource").GetString());
    }

    /// <summary>
    /// AC-230-9, the dates stay on the leaver list (ADR-040 D7; R-27: "in S147 only HRP-011's leaver row"). Kind <b>F</b> (today the members are
    /// absent, not null). The same switched-off leaver appears on the other two lists that carry the shared row shape, and there both window
    /// members are present and NULL: on the past-deadline list's approver-late half (EMPLOYEE_APPROVED, stored manager deadline 10 March, already
    /// passed on D+6) and on the approved-not-exported list (March APPROVED, never exported).
    ///
    /// <para><b>Red conditions.</b> Today <c>GetProperty("lastManagerDay")</c> throws (the member does not exist). Mutation (argued, Docker-only):
    /// filling the window for every list's rows fails the null assertions.</para>
    /// </summary>
    [Fact]
    public async Task OnDay6_OtherLists_CarryTheWindowMembers_AsNull()
    {
        var late = await SeedLeaverAsync(Cs, "fu_oth_late");
        await SeedPeriodAsync(Cs, late, "EMPLOYEE_APPROVED", managerDeadline: new DateOnly(2026, 3, 10));
        var unexported = await SeedLeaverAsync(Cs, "fu_oth_apr");
        await SeedPeriodAsync(Cs, unexported, "APPROVED", approvedBy: "fu_oth_hr_seed");

        using var hr = HrAt(Day(6), NewId("fu_hr"));
        using (var past = await ReadListAsync(hr, "/api/hr/follow-up/past-deadline"))
        {
            var item = FindItem(past.RootElement.GetProperty("approverLate"), late, 3);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("lastManagerDay").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("firstHrOnlyDay").ValueKind);
        }
        using (var notExported = await ReadListAsync(hr, "/api/hr/follow-up/approved-not-exported"))
        {
            var item = FindItem(notExported.RootElement.GetProperty("items"), unexported, 3);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("lastManagerDay").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("firstHrOnlyDay").ValueKind);
        }
    }
}
