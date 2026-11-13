using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Regression.Compliance;

/// <summary>One container for the leader-visibility facts; each fact seeds its own employee.</summary>
public sealed class RestLeaderVisibilityFixture : IAsyncLifetime
{
    public RestComplianceRealEngineHarness H { get; private set; } = null!;

    public async Task InitializeAsync() => H = await RestComplianceRealEngineHarness.StartAsync();

    public async Task DisposeAsync()
    {
        if (H is not null)
            await H.DisposeAsync();
    }
}

/// <summary>
/// S148 TASK-14813-T fix-up 1 (Step 5a adjudication ruling 1 and the security consult, § 4 items 1-12) -
/// Docker-gated route facts for the manager-visibility rule at the two month-edge surfaces.
///
/// <para>
/// <b>Plain language.</b> A manager sees nothing of a month the employee has not submitted (RES-002).
/// The day before a month belongs to the previous month, so a manager reading April must not learn
/// from the 11-hour rest finding, or from the Skema boundary rows, when the employee stopped work on
/// 31 March unless March is submitted. These facts prove, on the real routes: a manager's finding and
/// boundary rows appear only once the neighbouring month is sent (a missing row, a DRAFT and a REJECTED
/// month all count as unsent), the employee and HR still see everything, a leader who is also HR is
/// treated as HR only where the HR scope covers the employee, and the "not checked" marker on the 1st
/// appears only when it should and says nothing about the unsent month.
/// </para>
///
/// <para>
/// <b>Kind F (CI), argued.</b> Docker is unavailable locally, so every fact is CI-pending. "Red" means:
/// it fails with the fix-up's production change reverted (the leader-tier guards at
/// <c>ComplianceEndpoints.cs</c>' interval read and the Skema boundary block, patched out as Block A's
/// procedure describes): the manager's read then returns the finding / the boundary rows whatever the
/// neighbour's status, and never lists the 1st. The facts that guard against over-withholding (HR,
/// employee, leader-also-HR covering, sent neighbour) fail under a change that withholds from everyone.
/// The fixture is TASK-14813-T's AC-S0-3f: HK / OK24, Tue 30 Sep 2025 14:00-22:00 and Wed 1 Oct 2025
/// 07:00-15:00, 8 project hours each, a 9.0 h rest that ends in October.
/// <b>Three facts are kind R, not F</b> (S148 Step 5a cycle 2, both lenses): <c>HrRead_UnsentNeighbour_StillGetsTheBoundaryRest</c>,
/// <c>Marker_NewHireOrNoIntervalOnTheFirst_ListsNothing</c> and <c>Leader_UnsentCurrentMonth_IsRefusedWithTheUnchangedBody_OnBothSurfaces</c>
/// stay green with the fix reverted; they guard against over-withholding, a wrong marker and a changed 403 body.
/// </para>
///
/// <para>
/// Harness: <see cref="RestComplianceRealEngineHarness"/>; the leader and HR actors are tokens (the
/// endpoints admit by scope, so no actor rows are needed).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RestLeaderVisibilityTests : IClassFixture<RestLeaderVisibilityFixture>
{
    private const string MonthGateReason = "The month has not been submitted for approval";

    private readonly RestComplianceRealEngineHarness _h;

    public RestLeaderVisibilityTests(RestLeaderVisibilityFixture fx) => _h = fx.H;

    // ── actors ──────────────────────────────────────────────────────────────────────────────

    private HttpClient Leader() => _h.ClientAs(
        "t14813_leader", StatsTidRoles.LocalLeader,
        new RoleScope(StatsTidRoles.LocalLeader, RestComplianceRealEngineHarness.EmployeeOrgId, "ORG_ONLY"));

    private HttpClient Hr() => _h.ClientAs(
        "t14813_hr", StatsTidRoles.LocalHR,
        new RoleScope(StatsTidRoles.LocalHR, RestComplianceRealEngineHarness.EmployeeOrgId, "ORG_ONLY"));

    /// <summary>A leader token that also carries an HR scope, at the employee's org or at another.</summary>
    private HttpClient LeaderAlsoHr(bool hrCoversEmployee) => _h.ClientAs(
        "t14813_leader_hr", StatsTidRoles.LocalLeader,
        new RoleScope(StatsTidRoles.LocalLeader, RestComplianceRealEngineHarness.EmployeeOrgId, "ORG_ONLY"),
        new RoleScope(StatsTidRoles.LocalHR, hrCoversEmployee ? RestComplianceRealEngineHarness.EmployeeOrgId : "STY02", "ORG_ONLY"));

    // ── fixtures ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A fresh HK / OK24 employee with the month-edge shifts: the last day of the previous month
    /// 14:00-22:00 and the 1st 07:00-15:00, 8 project hours each (so both are fully on the clock).
    /// </summary>
    private async Task<string> SeedEdgeEmployeeAsync(int year, int month, bool previousDay = true)
    {
        var first = new DateOnly(year, month, 1);
        var employeeId = await _h.SeedEmployeeAsync();
        if (previousDay)
        {
            await _h.InsertWorkTimeAsync(employeeId, first.AddDays(-1), ("14:00", "22:00"));
            await _h.InsertEntryAsync(employeeId, first.AddDays(-1), 8m);
        }
        await _h.InsertWorkTimeAsync(employeeId, first, ("07:00", "15:00"));
        await _h.InsertEntryAsync(employeeId, first, 8m);
        return employeeId;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage rsp)
    {
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> Compliance(HttpClient c, string emp, int year, int month) =>
        c.GetAsync($"/api/compliance/{emp}/period?year={year}&month={month}");

    private static Task<HttpResponseMessage> SkemaMonth(HttpClient c, string emp, int year, int month) =>
        c.GetAsync($"/api/skema/{emp}/month?year={year}&month={month}");

    private static List<JsonElement> DailyRest(JsonElement body) =>
        new[] { "violations", "warnings" }
            .SelectMany(l => body.GetProperty(l).EnumerateArray())
            .Where(f => f.GetProperty("violationType").GetInt32() == (int)ComplianceViolationType.DAILY_REST)
            .ToList();

    private static List<string?> NotChecked(JsonElement body) =>
        body.GetProperty("restNotCheckedDays").EnumerateArray().Select(e => e.GetString()).ToList();

    private static List<string?> BoundaryDates(JsonElement body) =>
        body.GetProperty("boundaryWorkTime").EnumerateArray().Select(w => w.GetProperty("date").GetString()).ToList();

    private static void AssertOneFinding(JsonElement body, string date, decimal gap)
    {
        var f = Assert.Single(DailyRest(body));
        Assert.Equal(date, f.GetProperty("date").GetString());
        Assert.Equal(gap, f.GetProperty("actualValue").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    //  Compliance: the rest finding and the marker
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kind <b>F (CI)</b>, adjudication ruling 1 (consult item 1) and marker case "previous month sent".
    /// October sent, September unsent. The leader's October read has no DAILY_REST finding and lists
    /// <c>2025-10-01</c> as not checked; the employee's own read shows the 9.0 finding dated 2025-09-30
    /// and no listing, throughout; once September is sent the same leader read gives the one finding and
    /// no listing (nothing is stored, so nothing needs correcting).
    /// Red conditions: with the production change reverted the leader's first read returns the
    /// finding dated 2025-09-30 (<c>Assert.Empty</c> fails) and no listing.
    /// </summary>
    [Fact]
    public async Task LeaderRead_PreviousMonthUnsent_WithholdsTheBoundaryRest()
    {
        var emp = await SeedEdgeEmployeeAsync(2025, 10);
        await _h.InsertPeriodAsync(emp, "SUBMITTED", 2025, 10);

        var withheld = await ReadAsync(await Compliance(Leader(), emp, 2025, 10));
        Assert.Empty(DailyRest(withheld));
        Assert.Equal(new[] { "2025-10-01" }, NotChecked(withheld));

        var own = await ReadAsync(await Compliance(_h.Client(emp), emp, 2025, 10));
        AssertOneFinding(own, "2025-09-30", 9.0m);
        Assert.Empty(NotChecked(own));

        await _h.InsertPeriodAsync(emp, "SUBMITTED", 2025, 9);

        var released = await ReadAsync(await Compliance(Leader(), emp, 2025, 10));
        AssertOneFinding(released, "2025-09-30", 9.0m);
        Assert.Empty(NotChecked(released));
        var ownAfter = await ReadAsync(await Compliance(_h.Client(emp), emp, 2025, 10));
        AssertOneFinding(ownAfter, "2025-09-30", 9.0m);
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, consult items 3 and 4. A previous month that is REJECTED, has no approval row
    /// (fail-closed) or was reopened to DRAFT is withheld exactly like an unsent one. Red conditions: a
    /// guard that withholds only "no row" (or only DRAFT) lets the finding through for the other
    /// statuses; with the production change reverted all three return the finding.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("REJECTED")]
    [InlineData("DRAFT")]
    public async Task LeaderRead_PreviousMonthNotSent_WithholdsTheBoundaryRest_AndListsTheFirst(string? previousStatus)
    {
        var emp = await SeedEdgeEmployeeAsync(2025, 10);
        await _h.InsertPeriodAsync(emp, "SUBMITTED", 2025, 10);
        if (previousStatus is not null)
            await _h.InsertPeriodAsync(emp, previousStatus, 2025, 9);

        var body = await ReadAsync(await Compliance(Leader(), emp, 2025, 10));

        Assert.Empty(DailyRest(body));
        Assert.Equal(new[] { "2025-10-01" }, NotChecked(body));
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, consult item 5 (compliance, the year edge). January 2026: the previous month is
    /// December 2025 (<c>monthStart.AddMonths(-1)</c> across the year). January sent, December unsent: the
    /// leader's read withholds the 31 December rest and lists <c>2026-01-01</c>; once December is sent it
    /// reports the 9.0 finding dated 2025-12-31. Red conditions: an arithmetic that looks up the wrong
    /// month (for example the same year's December) reads no row and withholds forever, so the released
    /// read fails; with the production change reverted the first read returns the finding.
    /// </summary>
    [Fact]
    public async Task LeaderRead_January_PreviousDecemberUnsent_Withholds_ThenReportsOnceSent()
    {
        var emp = await SeedEdgeEmployeeAsync(2026, 1);
        await _h.InsertPeriodAsync(emp, "SUBMITTED", 2026, 1);

        var withheld = await ReadAsync(await Compliance(Leader(), emp, 2026, 1));
        Assert.Empty(DailyRest(withheld));
        Assert.Equal(new[] { "2026-01-01" }, NotChecked(withheld));

        await _h.InsertPeriodAsync(emp, "SUBMITTED", 2025, 12);

        var released = await ReadAsync(await Compliance(Leader(), emp, 2026, 1));
        AssertOneFinding(released, "2025-12-31", 9.0m);
        Assert.Empty(NotChecked(released));
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, consult item 7. HR is the corrective tier and is not withheld: with no approval
    /// row at all for either month, HR's October read has the 9.0 finding dated 2025-09-30 and lists
    /// nothing. Red conditions: a guard that withholds from every non-self actor (not only the leader
    /// tier) drops the finding and lists 2025-10-01.
    /// </summary>
    [Fact]
    public async Task HrRead_UnsentNeighbour_StillGetsTheBoundaryRest()
    {
        var emp = await SeedEdgeEmployeeAsync(2025, 10);

        var body = await ReadAsync(await Compliance(Hr(), emp, 2025, 10));

        AssertOneFinding(body, "2025-09-30", 9.0m);
        Assert.Empty(NotChecked(body));
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, consult item 8, compliance. A leader token that also carries an HR scope is HR
    /// tier only where that scope covers the employee. Covering (no approval rows): treated as HR, the
    /// finding is there and nothing is listed. Not covering (HR scope at another org; October sent,
    /// September not): stays leader tier, the finding is withheld and 2025-10-01 listed. Red conditions:
    /// a classifier that lets any HR scope in the token lift the tier fails the second arm; one that
    /// ignores the HR scope fails the first.
    /// </summary>
    [Fact]
    public async Task Compliance_LeaderAlsoHr_CoveringIsHrTier_NotCoveringIsLeaderTier()
    {
        var covered = await SeedEdgeEmployeeAsync(2025, 10);
        var hrBody = await ReadAsync(await Compliance(LeaderAlsoHr(hrCoversEmployee: true), covered, 2025, 10));
        AssertOneFinding(hrBody, "2025-09-30", 9.0m);
        Assert.Empty(NotChecked(hrBody));

        var other = await SeedEdgeEmployeeAsync(2025, 10);
        await _h.InsertPeriodAsync(other, "SUBMITTED", 2025, 10);
        var leaderBody = await ReadAsync(await Compliance(LeaderAlsoHr(hrCoversEmployee: false), other, 2025, 10));
        Assert.Empty(DailyRest(leaderBody));
        Assert.Equal(new[] { "2025-10-01" }, NotChecked(leaderBody));
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, consult item 9 (marker, two negative cases; the third, "previous month sent",
    /// is the last step of <see cref="LeaderRead_PreviousMonthUnsent_WithholdsTheBoundaryRest"/>).
    /// (a) A new hire hired on 1 October: 30 September was not an employed day, so nothing is listed and
    /// no finding exists. (b) An employee with no interval on 1 October (the first interval is 2 October)
    /// and September unsent: nothing is listed. Red conditions: a marker without the employed-day
    /// condition lists 2025-10-01 for (a); one without the "the 1st has a kept interval" condition lists
    /// it for (b).
    /// </summary>
    [Fact]
    public async Task Marker_NewHireOrNoIntervalOnTheFirst_ListsNothing()
    {
        // (a) hired 1 Oct 2025, September unsent
        var hire = new DateOnly(2025, 10, 1);
        var newHire = await _h.SeedEmployeeAsync(hire);
        await _h.InsertWorkTimeAsync(newHire, hire, ("07:00", "15:00"));
        await _h.InsertEntryAsync(newHire, hire, 8m);
        await _h.InsertPeriodAsync(newHire, "SUBMITTED", 2025, 10);
        var a = await ReadAsync(await Compliance(Leader(), newHire, 2025, 10));
        Assert.Empty(NotChecked(a));
        Assert.Empty(DailyRest(a));

        // (b) a registration on 30 Sep and 2 Oct, none on 1 Oct, September unsent
        var noFirst = await _h.SeedEmployeeAsync();
        await _h.InsertWorkTimeAsync(noFirst, new DateOnly(2025, 9, 30), ("14:00", "22:00"));
        await _h.InsertWorkTimeAsync(noFirst, new DateOnly(2025, 10, 2), ("07:00", "15:00"));
        await _h.InsertEntryAsync(noFirst, new DateOnly(2025, 10, 2), 8m);
        await _h.InsertPeriodAsync(noFirst, "SUBMITTED", 2025, 10);
        var b = await ReadAsync(await Compliance(Leader(), noFirst, 2025, 10));
        Assert.Empty(NotChecked(b));
        Assert.Empty(DailyRest(b));
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, consult item 10 (the no-leak proof). Two employees with identical October data
    /// and employment, one with a 30 September shift and one without, both with September unsent: the
    /// leader's two answers are identical (success, violations, warnings and the not-checked list), so
    /// the marker says nothing about what 30 September holds. Red conditions: a marker or a finding that
    /// depends on 30 September (the finding returning, or a list that differs) makes the two answers differ.
    /// </summary>
    [Fact]
    public async Task LeaderAnswers_AreIdentical_WhateverTheUnsentPreviousMonthHolds()
    {
        var withSep = await SeedEdgeEmployeeAsync(2025, 10, previousDay: true);
        var withoutSep = await SeedEdgeEmployeeAsync(2025, 10, previousDay: false);
        await _h.InsertPeriodAsync(withSep, "SUBMITTED", 2025, 10);
        await _h.InsertPeriodAsync(withoutSep, "SUBMITTED", 2025, 10);

        var a = await ReadAsync(await Compliance(Leader(), withSep, 2025, 10));
        var b = await ReadAsync(await Compliance(Leader(), withoutSep, 2025, 10));

        foreach (var member in new[] { "success", "violations", "warnings", "restNotCheckedDays" })
            Assert.Equal(a.GetProperty(member).GetRawText(), b.GetProperty(member).GetRawText());
        Assert.Equal(new[] { "2025-10-01" }, NotChecked(a));
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    //  Skema month view: the boundary rows
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kind <b>F (CI)</b>, adjudication ruling 1 (Skema sibling), consult items 1 and 11. October sent;
    /// September and November unsent. The leader's month view omits both boundary rows (30 September and
    /// 1 November); the employee's own read of the same month serves both. Red conditions: with the
    /// production change reverted the leader's <c>boundaryWorkTime</c> holds both rows.
    /// </summary>
    [Fact]
    public async Task Skema_LeaderRead_OmitsUnsentNeighbourRows_EmployeeKeepsBoth()
    {
        var emp = await SeedEdgeEmployeeAsync(2025, 10);
        await _h.InsertWorkTimeAsync(emp, new DateOnly(2025, 11, 1), ("08:00", "12:00"));
        await _h.InsertPeriodAsync(emp, "SUBMITTED", 2025, 10);

        var leader = await ReadAsync(await SkemaMonth(Leader(), emp, 2025, 10));
        Assert.Empty(BoundaryDates(leader));

        var own = await ReadAsync(await SkemaMonth(_h.Client(emp), emp, 2025, 10));
        Assert.Equal(new[] { "2025-09-30", "2025-11-01" }, BoundaryDates(own));
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, consult items 3 and 4, Skema. With November sent and September REJECTED, missing
    /// or DRAFT, the leader's read serves only the next-day row (1 November). Red conditions: a guard that
    /// withholds only "no row" (or only DRAFT) serves 30 September for the other statuses.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("REJECTED")]
    [InlineData("DRAFT")]
    public async Task Skema_LeaderRead_PreviousMonthNotSent_OmitsOnlyThePreviousRow(string? previousStatus)
    {
        var emp = await SeedEdgeEmployeeAsync(2025, 10);
        await _h.InsertWorkTimeAsync(emp, new DateOnly(2025, 11, 1), ("08:00", "12:00"));
        await _h.InsertPeriodAsync(emp, "SUBMITTED", 2025, 10);
        await _h.InsertPeriodAsync(emp, "SUBMITTED", 2025, 11);
        if (previousStatus is not null)
            await _h.InsertPeriodAsync(emp, previousStatus, 2025, 9);

        var leader = await ReadAsync(await SkemaMonth(Leader(), emp, 2025, 10));

        Assert.Equal(new[] { "2025-11-01" }, BoundaryDates(leader));
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, consult items 5 and 6. The two sides are independent and the lookups carry across
    /// the year edge. (A) December 2025 sent: neither neighbour sent gives no rows; then January sent gives
    /// only 2026-01-01. (B) December sent, November sent, January not: only 2025-11-30. (C) January 2026
    /// sent, December unsent: the 31 December row is withheld; December sent: served. Red conditions: a
    /// swapped or shared flag fails (A) or (B); a wrong <c>AddMonths</c> at the year edge fails (A) or (C).
    /// </summary>
    [Fact]
    public async Task Skema_BoundarySides_AreIndependent_AndCrossTheYearEdge()
    {
        // (A) December view, rows on 30 Nov and 1 Jan
        var a = await _h.SeedEmployeeAsync();
        await _h.InsertWorkTimeAsync(a, new DateOnly(2025, 11, 30), ("08:00", "12:00"));
        await _h.InsertWorkTimeAsync(a, new DateOnly(2026, 1, 1), ("08:00", "12:00"));
        await _h.InsertPeriodAsync(a, "SUBMITTED", 2025, 12);
        Assert.Empty(BoundaryDates(await ReadAsync(await SkemaMonth(Leader(), a, 2025, 12))));
        await _h.InsertPeriodAsync(a, "SUBMITTED", 2026, 1);
        Assert.Equal(new[] { "2026-01-01" }, BoundaryDates(await ReadAsync(await SkemaMonth(Leader(), a, 2025, 12))));

        // (B) the reverse: previous sent, next not
        var b = await _h.SeedEmployeeAsync();
        await _h.InsertWorkTimeAsync(b, new DateOnly(2025, 11, 30), ("08:00", "12:00"));
        await _h.InsertWorkTimeAsync(b, new DateOnly(2026, 1, 1), ("08:00", "12:00"));
        await _h.InsertPeriodAsync(b, "SUBMITTED", 2025, 12);
        await _h.InsertPeriodAsync(b, "SUBMITTED", 2025, 11);
        Assert.Equal(new[] { "2025-11-30" }, BoundaryDates(await ReadAsync(await SkemaMonth(Leader(), b, 2025, 12))));

        // (C) January view: the previous row is 31 December of the earlier year
        var c = await _h.SeedEmployeeAsync();
        await _h.InsertWorkTimeAsync(c, new DateOnly(2025, 12, 31), ("08:00", "12:00"));
        await _h.InsertPeriodAsync(c, "SUBMITTED", 2026, 1);
        Assert.Empty(BoundaryDates(await ReadAsync(await SkemaMonth(Leader(), c, 2026, 1))));
        await _h.InsertPeriodAsync(c, "SUBMITTED", 2025, 12);
        Assert.Equal(new[] { "2025-12-31" }, BoundaryDates(await ReadAsync(await SkemaMonth(Leader(), c, 2026, 1))));
    }

    /// <summary>
    /// Kind <b>F (CI)</b>, consult items 7 and 8, Skema. HR (no approval rows at all) and a leader token
    /// whose HR scope covers the employee both get both boundary rows; a leader token whose HR scope is
    /// elsewhere is leader tier and gets none (October sent, neighbours not). Red conditions: a guard
    /// that withholds from HR fails the first two arms; a classifier that lets any HR scope in the token
    /// lift the tier fails the third.
    /// </summary>
    [Fact]
    public async Task Skema_HrAndLeaderAlsoHr_TierDecidesTheBoundaryRows()
    {
        var emp = await SeedEdgeEmployeeAsync(2025, 10);
        await _h.InsertWorkTimeAsync(emp, new DateOnly(2025, 11, 1), ("08:00", "12:00"));

        var expected = new[] { "2025-09-30", "2025-11-01" };
        Assert.Equal(expected, BoundaryDates(await ReadAsync(await SkemaMonth(Hr(), emp, 2025, 10))));
        Assert.Equal(expected, BoundaryDates(await ReadAsync(
            await SkemaMonth(LeaderAlsoHr(hrCoversEmployee: true), emp, 2025, 10))));

        await _h.InsertPeriodAsync(emp, "SUBMITTED", 2025, 10);
        Assert.Empty(BoundaryDates(await ReadAsync(
            await SkemaMonth(LeaderAlsoHr(hrCoversEmployee: false), emp, 2025, 10))));
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    //  The refusal does not change shape
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kind <b>F (CI)</b>, consult item 12 (the ordering point). A leader reading a month that is not
    /// sent is refused with the unchanged 403 and the shared body (<c>error</c> "Access denied", <c>reason</c>
    /// the month-gate text, nothing else), on both surfaces, whatever the neighbours hold. Red conditions:
    /// a refusal that gained a member, a different status, or a body that varies with the neighbours'
    /// data fails the exact-body assertion.
    /// </summary>
    [Fact]
    public async Task Leader_UnsentCurrentMonth_IsRefusedWithTheUnchangedBody_OnBothSurfaces()
    {
        var emp = await SeedEdgeEmployeeAsync(2025, 10);
        await _h.InsertPeriodAsync(emp, "SUBMITTED", 2025, 9); // a sent neighbour must not change the refusal

        foreach (var rsp in new[]
        {
            await Compliance(Leader(), emp, 2025, 10),
            await SkemaMonth(Leader(), emp, 2025, 10),
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
            var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(new[] { "error", "reason" },
                body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            Assert.Equal("Access denied", body.GetProperty("error").GetString());
            Assert.Equal(MonthGateReason, body.GetProperty("reason").GetString());
        }
    }
}
